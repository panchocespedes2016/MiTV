using System;
using System.IO;
using System.Text.Json;

namespace MiTV
{
    // Estilo de subtítulos persistido. Se aplica como opciones del medio
    // (:freetype-*) al crearlo; al cambiarlo se recrea el medio.
    public static class SubStyle
    {
        // 18 Pequeño, 16 Normal, 12 Grande, 6 Muy grande (escala de VLC)
        public static int RelFontSize = 16;
        public static int FontColor = 0xFFFFFF;
        public static int BgColor = 0x000000;
        public static int BgOpacity = 0; // 0-255
        public static int OutlineColor = 0x000000;

        static string FilePath =>
            Path.Combine(AppContext.BaseDirectory, "subs-style.json");

        static SubStyle() { Load(); }

        class Data
        {
            public int RelFontSize { get; set; }
            public int FontColor { get; set; }
            public int BgColor { get; set; }
            public int BgOpacity { get; set; }
            public int OutlineColor { get; set; }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var d = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath));
                if (d == null) return;
                RelFontSize = d.RelFontSize;
                FontColor = d.FontColor;
                BgColor = d.BgColor;
                BgOpacity = d.BgOpacity;
                OutlineColor = d.OutlineColor;
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(new Data
                {
                    RelFontSize = RelFontSize,
                    FontColor = FontColor,
                    BgColor = BgColor,
                    BgOpacity = BgOpacity,
                    OutlineColor = OutlineColor
                }));
            }
            catch { }
        }

        public static string[] LibVlcOptions() => new[]
        {
            "--freetype-rel-fontsize=" + RelFontSize,
            "--freetype-color=" + FontColor,
            "--freetype-background-color=" + BgColor,
            "--freetype-background-opacity=" + BgOpacity,
            "--freetype-outline-color=" + OutlineColor,
        };
    }
}
