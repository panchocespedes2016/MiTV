using System;
using System.IO;
using System.Text.Json;

namespace MiTV
{
    // Ajustes de Películas y Series. Portable: mitv-vod.json junto al exe.
    public static class VodSettings
    {
        static string FilePath => Path.Combine(AppContext.BaseDirectory, "mitv-vod.json");

        class Data
        {
            public int ResultLimit { get; set; } = 0; // 0 = sin límite
            public bool ShowIntros { get; set; } = true;
            public bool MarkWatched { get; set; } = true;
        }
        static Data d = new();
        static VodSettings() { Load(); }

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
        }
        static void Save()
        {
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(d)); } catch { }
        }

        public static int ResultLimit { get => d.ResultLimit; set { d.ResultLimit = value; Save(); } }
        public static bool ShowIntros { get => d.ShowIntros; set { d.ShowIntros = value; Save(); } }
        public static bool MarkWatched { get => d.MarkWatched; set { d.MarkWatched = value; Save(); } }

        public static string LimitLabel => d.ResultLimit == 0 ? "Sin límite" : d.ResultLimit.ToString();
    }
}
