using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace MiTV
{
    public class EpgProgramme
    {
        public string ChannelId = "";
        public DateTime StartUtc, StopUtc;
        public string Title = "", Desc = "";
        public string Category = "", Director = "", Actors = "", Rating = "", Icon = "";
    }

    // Guía de programación (XMLTV). Se descarga al sincronizar la lista:
    // Xtream -> xmltv.php del servidor; M3U -> URL de EPG de la lista.
    // El XML crudo se guarda en epg-cache.xml junto al exe y se parsea
    // en segundo plano al arrancar.
    public static class EpgStore
    {
        static readonly HttpClient http = new HttpClient();
        static Dictionary<string, List<EpgProgramme>> byChannel = new();
        static Dictionary<string, string> nameToId = new(StringComparer.OrdinalIgnoreCase);
        static HashSet<string> ambiguousIds = new(); // ids que el proveedor repite en varios canales
        public static bool HasData => byChannel.Count > 0;
        public static DateTime UpdatedUtc;

        static string FilePath => Path.Combine(AppContext.BaseDirectory, "epg-cache.xml");

        public static async Task RefreshAsync(Playlist pl)
        {
            string url = null;
            // URL manual tiene prioridad si la fuente es manual
            if (EpgSettings.Source == "manual" && !string.IsNullOrWhiteSpace(EpgSettings.ManualUrl))
                url = EpgSettings.ManualUrl.Trim();
            else if (pl.Kind == "xtream")
            {
                string srv = (pl.Server ?? "").Trim().TrimEnd('/');
                string u = Uri.EscapeDataString(pl.Username ?? "");
                string p = Uri.EscapeDataString(pl.Password ?? "");
                if (srv.Length > 0) url = $"{srv}/xmltv.php?username={u}&password={p}";
            }
            else url = (pl.EpgUrl ?? "").Trim();
            if (string.IsNullOrEmpty(url)) return;
            var bytes = await http.GetByteArrayAsync(url);
            bytes = MaybeDecompress(bytes);
            if (bytes.Length < 100) return;
            Parse(bytes);
            try { File.WriteAllBytes(FilePath, bytes); } catch { }
            UpdatedUtc = DateTime.UtcNow;
        }

        public static void LoadCache()
        {
            Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(FilePath)) return;
                    Parse(File.ReadAllBytes(FilePath));
                    UpdatedUtc = File.GetLastWriteTimeUtc(FilePath);
                }
                catch { }
            });
        }

        static byte[] MaybeDecompress(byte[] b)
        {
            if (b.Length > 2 && b[0] == 0x1F && b[1] == 0x8B)
                using (var ms = new MemoryStream(b))
                using (var gz = new GZipStream(ms, CompressionMode.Decompress))
                using (var out_ = new MemoryStream())
                { gz.CopyTo(out_); return out_.ToArray(); }
            return b;
        }

        static void Parse(byte[] xml)
        {
            var byCh = new Dictionary<string, List<EpgProgramme>>();
            var n2id = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var idNames = new Dictionary<string, HashSet<string>>();
            var cutoff = DateTime.UtcNow.AddHours(-6); // lo viejo no sirve
            var settings = new XmlReaderSettings
            {
                IgnoreWhitespace = true,
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null
            };
            using var ms = new MemoryStream(xml);
            using var xr = XmlReader.Create(ms, settings);
            while (xr.Read())
            {
                if (xr.NodeType != XmlNodeType.Element) continue;
                if (xr.Name == "channel")
                {
                    string id = xr.GetAttribute("id");
                    var names = new List<string>();
                    if (!xr.IsEmptyElement)
                    {
                        while (xr.Read() && !(xr.NodeType == XmlNodeType.EndElement && xr.Name == "channel"))
                        {
                            if (xr.NodeType == XmlNodeType.Element && xr.Name == "display-name")
                                names.Add(xr.ReadString().Trim());
                        }
                    }
                    if (!string.IsNullOrEmpty(id))
                    {
                        if (!byCh.ContainsKey(id)) byCh[id] = new List<EpgProgramme>();
                        // id ambiguo si ya se vio con otro nombre distinto
                        foreach (var n in names)
                        {
                            var nk = Norm(n);
                            if (nk.Length == 0) continue;
                            if (!idNames.TryGetValue(id, out var set)) { set = new HashSet<string>(); idNames[id] = set; }
                            set.Add(nk);
                        }
                        foreach (var n in names)
                        {
                            var k = Norm(n);
                            if (k.Length > 0 && !n2id.ContainsKey(k)) n2id[k] = id;
                        }
                    }
                }
                else if (xr.Name == "programme")
                {
                    string ch = xr.GetAttribute("channel");
                    DateTime s, e;
                    try
                    {
                        s = ParseTime(xr.GetAttribute("start"));
                        e = ParseTime(xr.GetAttribute("stop"));
                    }
                    catch { xr.Skip(); continue; }
                    if (string.IsNullOrEmpty(ch) || e < cutoff) { xr.Skip(); continue; }
                    string title = "", desc = "", category = "", director = "", actors = "", rating = "", icon = "";
                    if (!xr.IsEmptyElement)
                    {
                        while (xr.Read() && !(xr.NodeType == XmlNodeType.EndElement && xr.Name == "programme"))
                        {
                            if (xr.NodeType != XmlNodeType.Element) continue;
                            if (xr.Name == "title" && title == "") title = xr.ReadString().Trim();
                            else if (xr.Name == "desc" && desc == "") desc = xr.ReadString().Trim();
                            else if (xr.Name == "category" && category == "") category = xr.ReadString().Trim();
                            else if (xr.Name == "credits")
                            {
                                if (!xr.IsEmptyElement)
                                {
                                    while (xr.Read() && !(xr.NodeType == XmlNodeType.EndElement && xr.Name == "credits"))
                                    {
                                        if (xr.NodeType != XmlNodeType.Element) continue;
                                        if (xr.Name == "director" && director == "") director = xr.ReadString().Trim();
                                        else if (xr.Name == "actor")
                                        {
                                            string a = xr.ReadString().Trim();
                                            if (a != "") actors = actors == "" ? a : actors + ", " + a;
                                        }
                                    }
                                }
                            }
                            else if (xr.Name == "rating" && rating == "")
                            {
                                if (!xr.IsEmptyElement)
                                {
                                    while (xr.Read() && !(xr.NodeType == XmlNodeType.EndElement && xr.Name == "rating"))
                                    {
                                        if (xr.NodeType == XmlNodeType.Element && xr.Name == "value" && rating == "")
                                            rating = xr.ReadString().Trim();
                                    }
                                }
                            }
                            else if (xr.Name == "icon" && icon == "")
                            {
                                string srcIcon = xr.GetAttribute("src");
                                if (!string.IsNullOrEmpty(srcIcon)) icon = srcIcon.Trim();
                            }
                        }
                    }
                    if (!byCh.TryGetValue(ch, out var list)) { list = new List<EpgProgramme>(); byCh[ch] = list; }
                    list.Add(new EpgProgramme { ChannelId = ch, StartUtc = s, StopUtc = e, Title = title, Desc = desc, Category = category, Director = director, Actors = actors, Rating = rating, Icon = icon });
                }
            }
            foreach (var kv in byCh) kv.Value.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
            var amb = new HashSet<string>();
            foreach (var kv in idNames) if (kv.Value.Count > 1) amb.Add(kv.Key);
            ambiguousIds = amb;
            byChannel = byCh;
            nameToId = n2id;
        }

        static DateTime ParseTime(string s)
        {
            // "20261016200000 +0000"
            s = (s ?? "").Trim();
            string dt = s.Length >= 14 ? s.Substring(0, 14) : s;
            string off = s.Length > 14 ? s.Substring(14).Trim() : "+0000";
            if (off.Length == 0) off = "+0000";
            var dto = DateTimeOffset.ParseExact(dt + " " + off, "yyyyMMddHHmmss zzz", CultureInfo.InvariantCulture);
            return dto.UtcDateTime;
        }

        static string Norm(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in (s ?? "").ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        // Todos los programas de un canal, ordenados (null si no hay datos). Para la parrilla.
        public static List<EpgProgramme> GetPrograms(LiveChannel ch)
        {
            if (!string.IsNullOrEmpty(ch.TvgId) && !ambiguousIds.Contains(ch.TvgId)
                && byChannel.TryGetValue(ch.TvgId, out var list)) return list;
            var k = Norm(ch.Name);
            if (k.Length > 0 && nameToId.TryGetValue(k, out var id) && !ambiguousIds.Contains(id)
                && byChannel.TryGetValue(id, out var l2)) return l2;
            return null;
        }

        // Programa actual y siguiente para un canal (null si no hay datos)
        public static (EpgProgramme now, EpgProgramme next) GetNowNext(LiveChannel ch)
        {
            List<EpgProgramme> list = null;
            if (!string.IsNullOrEmpty(ch.TvgId) && !ambiguousIds.Contains(ch.TvgId))
                byChannel.TryGetValue(ch.TvgId, out list);
            if (list == null)
            {
                var k = Norm(ch.Name);
                if (k.Length > 0 && nameToId.TryGetValue(k, out var id) && !ambiguousIds.Contains(id))
                    byChannel.TryGetValue(id, out list);
            }
            if (list == null || list.Count == 0) return (null, null);
            var now = DateTime.UtcNow;
            int lo = 0, hi = list.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                var p = list[mid];
                if (p.StartUtc <= now && now < p.StopUtc) { found = mid; break; }
                else if (p.StartUtc > now) hi = mid - 1;
                else lo = mid + 1;
            }
            if (found < 0) return (null, null);
            return (list[found], found + 1 < list.Count ? list[found + 1] : null);
        }
    }
}
