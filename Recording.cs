using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Timers;
using LibVLCSharp.Shared;

namespace MiTV
{
    // Grabación manual y programada. Los archivos van a la carpeta destino
    // con formato: 20261018-1048-CNN-Agenda-Economica.ts
    public static class Recording
    {
        static string BaseDir => AppContext.BaseDirectory;
        static string SettingsPath => Path.Combine(BaseDir, "mitv-rec.json");
        static string SchedulePath => Path.Combine(BaseDir, "mitv-rec-schedule.json");

        public class RecSettings { public string DestFolder { get; set; } = ""; }
        static RecSettings settings = new();
        public static string DestFolder
        {
            get
            {
                string d = (settings.DestFolder ?? "").Trim();
                if (d.Length == 0) d = Path.Combine(BaseDir, "recordings");
                return d;
            }
            set { settings.DestFolder = value ?? ""; SaveSettings(); }
        }

        static void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var s = JsonSerializer.Deserialize<RecSettings>(File.ReadAllText(SettingsPath));
                    if (s != null) settings = s;
                }
            }
            catch { }
        }
        static void SaveSettings()
        {
            try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings)); } catch { }
        }

        public class ScheduledRec
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N");
            public string ChannelName { get; set; } = "";
            public string StreamUrl { get; set; } = "";
            public string Logo { get; set; } = "";
            public string Title { get; set; } = "";
            public DateTime StartUtc { get; set; }
            public DateTime EndUtc { get; set; }
            // runtime (no se persiste)
            public bool Active { get; set; }
        }

        static List<ScheduledRec> scheduled = new();
        public static IReadOnlyList<ScheduledRec> Scheduled => scheduled;

        static void LoadSchedule()
        {
            try
            {
                if (File.Exists(SchedulePath))
                {
                    var l = JsonSerializer.Deserialize<List<ScheduledRec>>(File.ReadAllText(SchedulePath));
                    if (l != null) scheduled = l.Where(s => s.EndUtc > DateTime.UtcNow.AddMinutes(-5)).ToList();
                }
            }
            catch { }
        }
        static void SaveSchedule()
        {
            try { File.WriteAllText(SchedulePath, JsonSerializer.Serialize(scheduled)); } catch { }
        }

        public static void AddScheduled(ScheduledRec s) { scheduled.Add(s); SaveSchedule(); }
        public static void RemoveScheduled(string id)
        {
            var s = scheduled.FirstOrDefault(x => x.Id == id);
            if (s != null) { if (s.Active) StopScheduled(s); scheduled.Remove(s); SaveSchedule(); }
        }
        public static void UpdateScheduled(ScheduledRec s) { SaveSchedule(); }

        // ---------------- grabación manual ----------------

        public static bool IsManualRecording { get; private set; }
        public static DateTime ManualStartUtc { get; private set; }
        public static string ManualFilePath { get; private set; } = "";

        // Grabación desde timeshift: escribe los segmentos del buffer desde el punto
        // que se está viendo (delayMs) y sigue agregando los nuevos.
        static FileStream manFile;
        static Timeshift manTs;
        static Action<byte[]> manSegHandler;
        // Grabación en vivo sin timeshift: VLC en segundo plano con sout
        static LibVLC manVlc;
        static MediaPlayer manMp;

        public static string BuildFileName(DateTime startLocal, string channel, string program)
        {
            string c = Sanitize(channel), p = Sanitize(program);
            if (c.Length == 0) c = "Canal";
            if (p.Length == 0) p = "Programa";
            return $"{startLocal:yyyyMMdd-HHmm}-{c}-{p}.ts";
        }
        static string Sanitize(string s)
        {
            var bad = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var ch in (s ?? ""))
            {
                if (bad.Contains(ch)) continue;
                if (ch == ' ' || ch == '_') sb.Append('-');
                else sb.Append(ch);
            }
            string r = sb.ToString();
            while (r.Contains("--")) r = r.Replace("--", "-");
            return r.Trim('-');
        }

        public static string StartManualTimeshift(Timeshift ts, long delayMs, string channel, string program)
        {
            StopManual();
            try
            {
                Directory.CreateDirectory(DestFolder);
                string path = Path.Combine(DestFolder, BuildFileName(DateTime.Now, channel, program));
                manFile = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                var segs = ts.GetSegmentsFromDelay(delayMs);
                foreach (var d in segs) manFile.Write(d, 0, d.Length);
                manFile.Flush();
                manTs = ts;
                manSegHandler = data =>
                {
                    try { lock (manFile) { manFile.Write(data, 0, data.Length); } } catch { }
                };
                ts.OnNewSeg += manSegHandler;
                IsManualRecording = true;
                ManualStartUtc = DateTime.UtcNow;
                ManualFilePath = path;
                return path;
            }
            catch { StopManual(); return null; }
        }

        public static string StartManualLive(string url, string channel, string program)
        {
            StopManual();
            try
            {
                Directory.CreateDirectory(DestFolder);
                string path = Path.Combine(DestFolder, BuildFileName(DateTime.Now, channel, program));
                manVlc = new LibVLC("--no-video", "--no-audio", "--no-video-title-show");
                manMp = new MediaPlayer(manVlc);
                var media = new Media(manVlc, url, FromType.FromLocation);
                media.AddOption($":sout=#standard{{access=file,mux=ts,dst={path}}}");
                media.AddOption(":sout-keep");
                manMp.Play(media);
                IsManualRecording = true;
                ManualStartUtc = DateTime.UtcNow;
                ManualFilePath = path;
                return path;
            }
            catch { StopManual(); return null; }
        }

        public static void StopManual()
        {
            try
            {
                if (manTs != null && manSegHandler != null)
                { try { manTs.OnNewSeg -= manSegHandler; } catch { } }
            }
            catch { }
            manTs = null; manSegHandler = null;
            try { if (manFile != null) { lock (manFile) { manFile.Flush(); manFile.Dispose(); } } } catch { }
            manFile = null;
            try { if (manMp != null) { manMp.Stop(); manMp.Dispose(); } } catch { }
            manMp = null;
            try { if (manVlc != null) manVlc.Dispose(); } catch { }
            manVlc = null;
            IsManualRecording = false;
            ManualFilePath = "";
        }

        // ---------------- programadas ----------------
        static System.Timers.Timer schedTimer;
        static readonly Dictionary<string, (LibVLC vlc, MediaPlayer mp, string path)> activeSched = new();

        public static void Init()
        {
            LoadSettings();
            LoadSchedule();
            schedTimer = new System.Timers.Timer(20000);
            schedTimer.Elapsed += (s, e) => CheckSchedule();
            schedTimer.AutoReset = true;
            schedTimer.Start();
        }

        static void CheckSchedule()
        {
            try
            {
                var now = DateTime.UtcNow;
                foreach (var s in scheduled.ToList())
                {
                    if (!s.Active && now >= s.StartUtc && now < s.EndUtc)
                        StartScheduled(s);
                    else if (s.Active && now >= s.EndUtc)
                    {
                        StopScheduled(s);
                        scheduled.Remove(s);
                        SaveSchedule();
                    }
                }
                // limpiar terminadas hace más de un día
                int n0 = scheduled.Count;
                scheduled.RemoveAll(s => !s.Active && s.EndUtc < now.AddDays(-1));
                if (scheduled.Count != n0) SaveSchedule();
            }
            catch { }
        }

        static void StartScheduled(ScheduledRec s)
        {
            try
            {
                Directory.CreateDirectory(DestFolder);
                string path = Path.Combine(DestFolder, BuildFileName(s.StartUtc.ToLocalTime(), s.ChannelName, s.Title));
                var vlc = new LibVLC("--no-video", "--no-audio", "--no-video-title-show");
                var mp = new MediaPlayer(vlc);
                var media = new Media(vlc, s.StreamUrl, FromType.FromLocation);
                media.AddOption($":sout=#standard{{access=file,mux=ts,dst={path}}}");
                media.AddOption(":sout-keep");
                mp.Play(media);
                activeSched[s.Id] = (vlc, mp, path);
                s.Active = true;
            }
            catch { }
        }

        static void StopScheduled(ScheduledRec s)
        {
            try
            {
                if (activeSched.TryGetValue(s.Id, out var a))
                {
                    try { a.mp.Stop(); a.mp.Dispose(); } catch { }
                    try { a.vlc.Dispose(); } catch { }
                    activeSched.Remove(s.Id);
                }
            }
            catch { }
            s.Active = false;
        }

        public static bool IsScheduledActive(string id) =>
            scheduled.Any(s => s.Id == id && s.Active);

        public static string ActiveSchedPath(string id) =>
            activeSched.TryGetValue(id, out var a) ? a.path : "";

        // Archivos grabados en la carpeta destino
        public static List<FileInfo> CompletedFiles()
        {
            try
            {
                var d = new DirectoryInfo(DestFolder);
                if (!d.Exists) return new List<FileInfo>();
                return d.GetFiles("*.ts").OrderByDescending(f => f.CreationTimeUtc).ToList();
            }
            catch { return new List<FileInfo>(); }
        }
    }
}
