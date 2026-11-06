using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MiTV
{
    // Sirve en RAM (sin tocar el disco) la lista M3U que VLC abre con los nombres.
    // Mismo patrón que Timeshift: TcpListener puro en loopback, puerto asignado
    // por el sistema, sin permisos de administrador. El servidor vive mientras
    // la app esté abierta y cada envío a VLC reemplaza el contenido.
    public static class RamPlaylist
    {
        static TcpListener tcp;
        static int port;
        static string content = "";
        static readonly object gate = new();

        // Publica el texto M3U en memoria y devuelve su URL local (null si falla)
        public static string Serve(string m3u)
        {
            lock (gate)
            {
                content = m3u;
                if (tcp == null)
                {
                    try
                    {
                        tcp = new TcpListener(IPAddress.Loopback, 0);
                        tcp.Start();
                        port = ((IPEndPoint)tcp.LocalEndpoint).Port;
                        _ = Task.Run(ServeLoop);
                    }
                    catch { tcp = null; return null; }
                }
                return $"http://127.0.0.1:{port}/playlist.m3u";
            }
        }

        static async Task ServeLoop()
        {
            while (true)
            {
                TcpClient client;
                try { client = await tcp.AcceptTcpClientAsync(); }
                catch { break; }
                _ = Task.Run(() => HandleClient(client));
            }
        }

        static void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (var ns = client.GetStream())
                {
                    if (string.IsNullOrEmpty(ReadHeaders(ns))) return;
                    string body;
                    lock (gate) { body = content; }
                    byte[] data = Encoding.UTF8.GetBytes(body);
                    // BOM UTF-8: así VLC decodifica bien los acentos en Windows
                    byte[] full = new byte[3 + data.Length];
                    full[0] = 0xEF; full[1] = 0xBB; full[2] = 0xBF;
                    Buffer.BlockCopy(data, 0, full, 3, data.Length);
                    WriteResponse(ns, 200, "OK", "audio/x-mpegurl; charset=utf-8", full);
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

        static void WriteResponse(NetworkStream ns, int code, string reason, string contentType, byte[] body)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(code).Append(' ').Append(reason).Append("\r\n");
            if (contentType != null) sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            sb.Append("Content-Length: ").Append(body?.Length ?? 0).Append("\r\n");
            sb.Append("Connection: close\r\n\r\n");
            byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
            ns.Write(head, 0, head.Length);
            if (body != null && body.Length > 0) ns.Write(body, 0, body.Length);
        }
    }

    // Envía contenido al VLC de escritorio (externo a la app)
    public static class ExternalVlc
    {
        public static string FindVlc()
        {
            string[] paths =
            {
                @"C:\Program Files\VideoLAN\VLC\vlc.exe",
                @"C:\Program Files (x86)\VideoLAN\VLC\vlc.exe",
            };
            foreach (var p in paths)
                if (File.Exists(p)) return p;
            // VLC registra vlc.exe en "App Paths" al instalarse: lo resuelve el shell
            return "vlc.exe";
        }

        // Devuelve false si no se pudo lanzar VLC
        public static bool Play(string url, string title = null)
            => PlayMultiple(new[] { (url, title) });

        // Varias URLs como lista de reproducción (sin nombres)
        public static bool PlayMultiple(IEnumerable<string> urls)
        {
            var items = new List<(string, string)>();
            foreach (var u in urls) if (!string.IsNullOrEmpty(u)) items.Add((u, null));
            return PlayMultiple(items);
        }

        // Varias URLs como lista de reproducción, con su nombre cada una.
        // Vía principal: lista M3U en RAM con #EXTINF (VLC ignora el
        // :meta-title en este entorno). Respaldo: URLs directas con :meta-title.
        public static bool PlayMultiple(IEnumerable<(string url, string title)> items)
        {
            var list = items.Where(x => !string.IsNullOrEmpty(x.url)).ToList();
            if (list.Count == 0) return false;
            string local = RamPlaylist.Serve(BuildM3u(list));
            if (local != null) return Launch(new[] { "\"" + local + "\"" });
            var args = new List<string>();
            foreach (var (url, title) in list)
            {
                args.Add("\"" + url + "\"");
                string t = CleanTitle(title);
                if (t.Length > 0) args.Add("\":meta-title=" + t + "\"");
            }
            return Launch(args);
        }

        static string BuildM3u(IEnumerable<(string url, string title)> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#EXTM3U");
            foreach (var (url, title) in items)
            {
                string t = string.IsNullOrWhiteSpace(title) ? "" : title.Replace("\r", " ").Replace("\n", " ").Trim();
                sb.Append("#EXTINF:-1,").AppendLine(t);
                sb.AppendLine(url);
            }
            return sb.ToString();
        }

        static bool Launch(IEnumerable<string> args)
        {
            try
            {
                Process.Start(new ProcessStartInfo(FindVlc(), string.Join(" ", args)) { UseShellExecute = true });
                return true;
            }
            catch { return false; }
        }

        // Sin comillas ni saltos de línea: romperían el argumento citado
        static string CleanTitle(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return "";
            return t.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
