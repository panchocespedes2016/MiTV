using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MiTV
{
    // Timeshift en RAM para TV en vivo.
    // Descarga el stream (HLS o directo) a un buffer circular en memoria y lo
    // re-sirve como HLS en http://127.0.0.1:puerto/live.m3u8 para que VLC pueda
    // pausar y retroceder dentro de la ventana configurada (0 = desactivado).
    // Al cambiar de canal se crea una instancia nueva: el buffer empieza de cero.
    public class Timeshift : IDisposable
    {
        class Seg
        {
            public long Seq;
            public byte[] Data = Array.Empty<byte>();
            public long DurMs;
        }

        readonly object bufLock = new();
        readonly List<Seg> segs = new();
        long liveEdgeMs; // fin del segmento más nuevo, en ms desde el inicio del buffer

        TcpListener tcp;
        HttpClient http;
        CancellationTokenSource cts;
        Task fetchTask, serveTask;
        long windowMs;
        bool aborted;

        public string LocalUrl { get; private set; }
        public long LiveEdgeMs { get { lock (bufLock) return liveEdgeMs; } }
        // Se invoca con los bytes de cada segmento nuevo (para grabación)
        public event Action<byte[]> OnNewSeg;

        // Segmentos desde el punto indicado por delayMs (lo que se está viendo)
        public List<byte[]> GetSegmentsFromDelay(long delayMs)
        {
            var out_ = new List<byte[]>();
            lock (bufLock)
            {
                if (segs.Count == 0) return out_;
                long target = liveEdgeMs - Math.Max(0, delayMs);
                long t = 0;
                int start = 0;
                for (int i = 0; i < segs.Count; i++)
                {
                    if (t + segs[i].DurMs >= target) { start = i; break; }
                    t += segs[i].DurMs;
                    start = i + 1;
                }
                for (int i = start; i < segs.Count; i++) out_.Add(segs[i].Data);
            }
            return out_;
        }

        // Inicia el buffer. Devuelve false si no se pudo (se reproduce la URL original).
        public bool Start(string url, int minutes)
        {
            if (minutes <= 0 || string.IsNullOrEmpty(url)) return false;
            windowMs = minutes * 60_000L;
            try
            {
                // TcpListener puro: no necesita permisos de administrador (http.sys sí los exige)
                tcp = new TcpListener(IPAddress.Loopback, 0);
                tcp.Start();
                int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
                LocalUrl = $"http://127.0.0.1:{port}/live.m3u8";

                http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("MiTV/1.0");
                cts = new CancellationTokenSource();

                serveTask = Task.Run(() => ServeLoop(cts.Token));
                fetchTask = Task.Run(() => FetchLoop(url, cts.Token));

                // Esperar al primer segmento (VLC necesita datos para arrancar)
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 20000)
                {
                    lock (bufLock) { if (segs.Count > 0 || aborted) break; }
                    Thread.Sleep(250);
                }
                lock (bufLock) { if (segs.Count == 0) return false; }
                return true;
            }
            catch { Stop(); return false; }
        }

        public void Stop()
        {
            try { cts?.Cancel(); } catch { }
            try { tcp?.Stop(); } catch { }
            try { http?.Dispose(); } catch { }
            lock (bufLock) { segs.Clear(); liveEdgeMs = 0; }
        }

        public void Dispose() => Stop();

        void AddSeg(long seq, byte[] data, long durMs)
        {
            if (data == null || data.Length == 0 || durMs <= 0) return;
            lock (bufLock)
            {
                if (segs.Count > 0 && seq <= segs[^1].Seq) return; // ya lo tenemos
                segs.Add(new Seg { Seq = seq, Data = data, DurMs = durMs });
                liveEdgeMs += durMs;
                try { OnNewSeg?.Invoke(data); } catch { }
                while (segs.Count > 1 && liveEdgeMs > windowMs)
                {
                    liveEdgeMs -= segs[0].DurMs;
                    segs.RemoveAt(0);
                }
            }
        }

        // ------------------- descarga -------------------

        async Task FetchLoop(string url, CancellationToken ct)
        {
            try
            {
                if (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
                    await FetchHls(url, ct);
                else
                    await FetchDirect(url, ct);
            }
            catch { }
            lock (bufLock) { aborted = true; }
        }

        static readonly Regex ReSeq = new(@"#EXT-X-MEDIA-SEQUENCE:(\d+)", RegexOptions.Compiled);
        static readonly Regex ReTarget = new(@"#EXT-X-TARGETDURATION:(\d+)", RegexOptions.Compiled);
        static readonly Regex ReSeg = new(@"#EXTINF:([\d.]+),\s*\r?\n([^\r\n#][^\r\n]*)", RegexOptions.Compiled);
        static readonly Regex ReVariant = new(@"#EXT-X-STREAM-INF[^\r\n]*\r?\n([^\r\n#][^\r\n]*)", RegexOptions.Compiled);

        async Task FetchHls(string url, CancellationToken ct)
        {
            long lastSeq = -1;
            int fails = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    string plUrl = url;
                    string txt;
                    // OJO: el servidor puede redirigir (302) a otro host; los segmentos
                    // relativos se resuelven contra la URL FINAL, no la original
                    using (var resp = await http.GetAsync(plUrl, ct))
                    {
                        resp.EnsureSuccessStatusCode();
                        plUrl = resp.RequestMessage.RequestUri.ToString();
                        txt = await resp.Content.ReadAsStringAsync(ct);
                    }
                    // Playlist maestra: seguir la primera variante
                    if (txt.Contains("#EXT-X-STREAM-INF"))
                    {
                        var mv = ReVariant.Match(txt);
                        if (!mv.Success) { await Task.Delay(3000, ct); continue; }
                        string varUrl = new Uri(new Uri(plUrl), mv.Groups[1].Value.Trim()).ToString();
                        using (var vresp = await http.GetAsync(varUrl, ct))
                        {
                            vresp.EnsureSuccessStatusCode();
                            plUrl = vresp.RequestMessage.RequestUri.ToString();
                            txt = await vresp.Content.ReadAsStringAsync(ct);
                        }
                    }
                    // Encriptado: no podemos hacer timeshift
                    if (txt.Contains("#EXT-X-KEY"))
                    {
                        lock (bufLock) { aborted = true; }
                        return;
                    }
                    var baseUri = new Uri(plUrl);
                    long baseSeq = 0;
                    var ms = ReSeq.Match(txt);
                    if (ms.Success) baseSeq = long.Parse(ms.Groups[1].Value);
                    var segMs = ReSeg.Matches(txt);
                    long target = 4;
                    var mt = ReTarget.Match(txt);
                    if (mt.Success) target = Math.Max(2, long.Parse(mt.Groups[1].Value));

                    int idx = 0;
                    foreach (Match m in segMs)
                    {
                        long seq = baseSeq + idx++;
                        if (seq <= lastSeq) continue;
                        long durMs = (long)(double.Parse(m.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture) * 1000);
                        string segUrl = new Uri(baseUri, m.Groups[2].Value.Trim()).ToString();
                        byte[] data = await http.GetByteArrayAsync(segUrl, ct);
                        AddSeg(seq, data, durMs);
                        lastSeq = seq;
                    }
                    fails = 0;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(5, Math.Max(1, target / 2))), ct);
                }
                catch (OperationCanceledException) { return; }
                catch
                {
                    if (++fails > 8) return;
                    try { await Task.Delay(3000, ct); } catch { return; }
                }
            }
        }

        async Task FetchDirect(string url, CancellationToken ct)
        {
            // Stream continuo: lo cortamos en trozos de ~4 s para servirlo como HLS
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            using var src = await resp.Content.ReadAsStreamAsync(ct);
            var chunk = new MemoryStream();
            var tmp = new byte[65536];
            long seq = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!ct.IsCancellationRequested)
            {
                int n;
                try { n = await src.ReadAsync(tmp, 0, tmp.Length, ct); }
                catch { break; }
                if (n <= 0) break;
                chunk.Write(tmp, 0, n);
                if (sw.ElapsedMilliseconds >= 4000 && chunk.Length > 0)
                {
                    AddSeg(seq++, chunk.ToArray(), sw.ElapsedMilliseconds);
                    chunk.SetLength(0);
                    sw.Restart();
                }
            }
        }

        // ------------------- servidor local -------------------

        async Task ServeLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = null;
                try { client = await tcp.AcceptTcpClientAsync(ct); }
                catch { break; }
                _ = Task.Run(() => HandleClient(client));
            }
        }

        void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (var ns = client.GetStream())
                {
                    string req = ReadHeaders(ns);
                    if (string.IsNullOrEmpty(req)) return;
                    var lines = req.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    var parts = lines[0].Split(' ');
                    if (parts.Length < 2 || !parts[0].Equals("GET", StringComparison.OrdinalIgnoreCase))
                    { WriteResponse(ns, 400, "Bad Request", null, null); return; }
                    string path = parts[1].Split('?')[0];
                    string range = null;
                    foreach (var ln in lines)
                        if (ln.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                        { range = ln.Substring(6).Trim(); break; }

                    if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
                        ServePlaylist(ns);
                    else if (path.StartsWith("/seg"))
                        ServeSegment(ns, path, range);
                    else
                        WriteResponse(ns, 404, "Not Found", null, null);
                }
            }
            catch { }
        }

        static string ReadHeaders(NetworkStream ns)
        {
            ns.ReadTimeout = 10000;
            var buf = new byte[8192];
            var ms = new MemoryStream();
            try
            {
                while (ms.Length < 8192)
                {
                    int n = ns.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    ms.Write(buf, 0, n);
                    string s = Encoding.ASCII.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                    int p = s.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (p >= 0) return s.Substring(0, p);
                }
            }
            catch { }
            return ms.Length > 0 ? Encoding.ASCII.GetString(ms.GetBuffer(), 0, (int)ms.Length) : null;
        }

        static void WriteResponse(NetworkStream ns, int code, string reason, string contentType, byte[] body, string extraHeaders = null)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(code).Append(' ').Append(reason).Append("\r\n");
            if (contentType != null) sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            sb.Append("Accept-Ranges: bytes\r\n");
            if (extraHeaders != null) sb.Append(extraHeaders);
            sb.Append("Content-Length: ").Append(body?.Length ?? 0).Append("\r\n");
            sb.Append("Connection: close\r\n\r\n");
            byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
            ns.Write(head, 0, head.Length);
            if (body != null && body.Length > 0) ns.Write(body, 0, body.Length);
        }

        void ServePlaylist(NetworkStream ns)
        {
            byte[] b;
            lock (bufLock)
            {
                if (segs.Count == 0) { WriteResponse(ns, 503, "Service Unavailable", null, null); return; }
                long target = segs.Max(s => s.DurMs);
                var sb = new StringBuilder();
                sb.AppendLine("#EXTM3U");
                sb.AppendLine("#EXT-X-VERSION:3");
                sb.AppendLine("#EXT-X-TARGETDURATION:" + Math.Max(1, (target + 999) / 1000));
                sb.AppendLine("#EXT-X-MEDIA-SEQUENCE:" + segs[0].Seq);
                foreach (var s in segs)
                {
                    sb.AppendLine("#EXTINF:" + (s.DurMs / 1000.0).ToString("0.###",
                        System.Globalization.CultureInfo.InvariantCulture) + ",");
                    sb.AppendLine("seg" + s.Seq + ".ts");
                }
                b = Encoding.UTF8.GetBytes(sb.ToString());
            }
            WriteResponse(ns, 200, "OK", "application/vnd.apple.mpegurl", b, "Cache-Control: no-cache\r\n");
        }

        void ServeSegment(NetworkStream ns, string path, string range)
        {
            var m = Regex.Match(path, @"seg(\d+)\.ts");
            if (!m.Success) { WriteResponse(ns, 404, "Not Found", null, null); return; }
            long seq = long.Parse(m.Groups[1].Value);
            byte[] data = null;
            lock (bufLock)
            {
                var s = segs.FirstOrDefault(x => x.Seq == seq);
                if (s != null) data = s.Data;
            }
            if (data == null) { WriteResponse(ns, 404, "Not Found", null, null); return; }

            long from = 0, to = data.Length - 1;
            bool partial = false;
            if (!string.IsNullOrEmpty(range))
            {
                var rm = Regex.Match(range, @"bytes=(\d*)-(\d*)");
                if (rm.Success && (!string.IsNullOrEmpty(rm.Groups[1].Value) || !string.IsNullOrEmpty(rm.Groups[2].Value)))
                {
                    if (!string.IsNullOrEmpty(rm.Groups[1].Value)) from = long.Parse(rm.Groups[1].Value);
                    if (!string.IsNullOrEmpty(rm.Groups[2].Value)) to = long.Parse(rm.Groups[2].Value);
                    to = Math.Min(to, data.Length - 1);
                    partial = true;
                }
            }
            int len = (int)(to - from + 1);
            var body = new byte[len];
            Buffer.BlockCopy(data, (int)from, body, 0, len);
            string extra = partial ? $"Content-Range: bytes {from}-{to}/{data.Length}\r\n" : null;
            WriteResponse(ns, partial ? 206 : 200, partial ? "Partial Content" : "OK", "video/mp2t", body, extra);
        }
    }
}
