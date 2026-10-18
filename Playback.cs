using System;
using System.IO;
using System.Text.Json;

namespace MiTV
{
    // Ajustes de reproducción. Portable: mitv-playback.json junto al exe.
    public static class PlaybackSettings
    {
        static PlaybackData d = new();

        static string FilePath => Path.Combine(AppContext.BaseDirectory, "mitv-playback.json");

        static PlaybackSettings() { Load(); }

        static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var j = JsonSerializer.Deserialize<PlaybackData>(File.ReadAllText(FilePath));
                    if (j != null) d = j;
                }
            }
            catch { }
        }

        static void Save()
        {
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(d)); } catch { }
        }

        // "hw" = hardware (defecto), "sw" = software
        public static string Decoder
        {
            get => d.Decoder == "sw" ? "sw" : "hw";
            set { if (Decoder != value) { d.Decoder = value; Save(); } }
        }

        // ms de caché de red (400 / 1000 / 2500)
        public static int BufferMs
        {
            get => d.BufferMs is 400 or 1000 or 2500 ? d.BufferMs : 1000;
            set { if (BufferMs != value) { d.BufferMs = value; Save(); } }
        }

        public static bool AutoResume
        {
            get => d.AutoResume;
            set { if (d.AutoResume != value) { d.AutoResume = value; Save(); } }
        }

        // Seguir reproduciendo al salir del reproductor (hasta abrir otro elemento)
        public static bool BackgroundPlay
        {
            get => d.BackgroundPlay;
            set { if (d.BackgroundPlay != value) { d.BackgroundPlay = value; Save(); } }
        }

        // Minutos de timeshift en RAM (0 = desactivado)
        public static int TimeshiftMin
        {
            get => d.TimeshiftMin is 30 or 60 or 120 or 180 or 240 ? d.TimeshiftMin : 0;
            set { if (TimeshiftMin != value) { d.TimeshiftMin = value; Save(); } }
        }

        static readonly string[] SkipOrder = { "m10", "m1", "s10", "s10p", "m1p", "m10p" };

        // Ids de botones de salto visibles, separados por coma
        public static bool IsSkipVisible(string id)
        {
            var s = "," + (d.SkipButtons ?? "") + ",";
            return s.Contains("," + id + ",");
        }

        public static bool ShowClock
        {
            get => d.ShowClock;
            set { if (d.ShowClock != value) { d.ShowClock = value; Save(); } }
        }

        // Posición del reloj (-1 = posición por defecto)
        public static double ClockX
        {
            get => d.ClockX;
            set { if (d.ClockX != value) { d.ClockX = value; Save(); } }
        }
        public static double ClockY
        {
            get => d.ClockY;
            set { if (d.ClockY != value) { d.ClockY = value; Save(); } }
        }

        public static void SetSkipVisible(string id, bool visible)
        {
            var set = new System.Collections.Generic.HashSet<string>(
                (d.SkipButtons ?? "").Split(',', System.StringSplitOptions.RemoveEmptyEntries));
            if (visible) set.Add(id); else set.Remove(id);
            var nv = string.Join(",", System.Linq.Enumerable.Where(SkipOrder, set.Contains));
            if (d.SkipButtons != nv) { d.SkipButtons = nv; Save(); }
        }

        class PlaybackData
        {
            public string Decoder { get; set; } = "hw";
            public int BufferMs { get; set; } = 1000;
            public bool AutoResume { get; set; } = true;
            public bool BackgroundPlay { get; set; } = false;
            public int TimeshiftMin { get; set; } = 0;
            public string SkipButtons { get; set; } = "m10,m1,s10,s10p,m1p,m10p";
            public bool ShowClock { get; set; } = true;
            public double ClockX { get; set; } = -1;
            public double ClockY { get; set; } = -1;
        }
    }
}
