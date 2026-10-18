using System;
using System.IO;
using System.Text.Json;

namespace MiTV
{
    // Ajustes de la Guía EPG. Portable: mitv-epg.json junto al exe.
    public static class EpgSettings
    {
        static string FilePath => Path.Combine(AppContext.BaseDirectory, "mitv-epg.json");

        public class Data
        {
            public bool Enabled { get; set; } = true;
            public string Source { get; set; } = "auto"; // auto | manual
            public string ManualUrl { get; set; } = "";
            public bool UpdateOnStart { get; set; } = true;
            public int PastHours { get; set; } = 4;   // 0..12
            public int FutureHours { get; set; } = 24; // 3..216 (3h..9 días)
        }

        static Data d = new();
        static EpgSettings() { Load(); }

        static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var x = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath));
                    if (x != null) d = x;
                }
            }
            catch { }
            d.PastHours = Math.Max(0, Math.Min(12, d.PastHours));
            d.FutureHours = Math.Max(3, Math.Min(216, d.FutureHours));
        }
        static void Save()
        {
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(d)); } catch { }
        }

        public static bool Enabled { get => d.Enabled; set { d.Enabled = value; Save(); } }
        public static string Source { get => d.Source; set { d.Source = value; Save(); } }
        public static string ManualUrl { get => d.ManualUrl; set { d.ManualUrl = value ?? ""; Save(); } }
        public static bool UpdateOnStart { get => d.UpdateOnStart; set { d.UpdateOnStart = value; Save(); } }
        public static int PastHours { get => d.PastHours; set { d.PastHours = Math.Max(0, Math.Min(12, value)); Save(); } }
        public static int FutureHours { get => d.FutureHours; set { d.FutureHours = Math.Max(3, Math.Min(216, value)); Save(); } }

        public static string PastLabel => d.PastHours == 0 ? "0 h" : d.PastHours == 1 ? "1 hora" : $"{d.PastHours} horas";
        public static string FutureLabel =>
            d.FutureHours < 24 ? $"{d.FutureHours} horas" :
            d.FutureHours % 24 == 0 ? (d.FutureHours / 24 == 1 ? "1 día" : $"{d.FutureHours / 24} días") :
            $"{d.FutureHours} horas";
    }
}
