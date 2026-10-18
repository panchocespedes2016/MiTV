using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace MiTV
{
    // Temas visuales de la app. Cada tema define todos los roles de color.
    // Ui.cs lee los colores de aquí, así que al cambiar de tema basta con
    // reconstruir las vistas (los pinceles se cachean por tema).
    static class Theme
    {
        public static string Current { get; private set; } = "Oscuro";

        static readonly string[] Names = { "Oscuro", "Claro", "Azul", "Vino" };
        public static string[] ThemeNames => Names;

        // rol -> color por tema (Oscuro, Claro, Azul, Vino)
        static readonly Dictionary<string, string[]> Colors = new()
        {
            ["Bg"]     = new[] { "#0C1215", "#F2F4F6", "#141428", "#211420" },
            ["TopBg"]  = new[] { "#10171B", "#FFFFFF", "#1A1A2E", "#2D1B2E" },
            ["Panel"]  = new[] { "#161E22", "#FFFFFF", "#1E1E38", "#33202F" },
            ["Panel2"] = new[] { "#1E2A30", "#E9EDF0", "#252545", "#3E2839" },
            ["Brd"]    = new[] { "#232F36", "#D5DDE3", "#2E2E52", "#4A3044" },
            ["Text"]   = new[] { "#FFFFFF", "#1A1A1A", "#FFFFFF", "#FFFFFF" },
            ["Dim"]    = new[] { "#8A9AA3", "#5A6A73", "#9AA3B8", "#A898A0" },
            ["Gray"]   = new[] { "#8A9AA3", "#6A7A83", "#8A9AA3", "#8A9AA3" },
            ["Red"]    = new[] { "#E14B5A", "#D63A4A", "#E14B5A", "#E14B5A" },
            ["Blue"]   = new[] { "#3B82F6", "#2B6CB0", "#5B8DEF", "#3B82F6" },
            ["BarBg"]  = new[] { "#2A3A44", "#D0D8DE", "#2A2A4A", "#402A3A" },
            ["BarBuf"] = new[] { "#4A5A66", "#A0AEB8", "#45456A", "#5A4254" },
        };

        static int Index => Math.Max(0, Array.IndexOf(Names, Current));

        static readonly Dictionary<string, SolidColorBrush> cache = new();

        public static SolidColorBrush Brush(string role)
        {
            string key = Current + "|" + role;
            if (!cache.TryGetValue(key, out var b))
            {
                b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Colors[role][Index]));
                b.Freeze();
                cache[key] = b;
            }
            return b;
        }

        // Colores de muestra para la tarjeta del tema en Ajustes
        public static (string, string) Preview(string name)
        {
            int i = Math.Max(0, Array.IndexOf(Names, name));
            return (Colors["Panel"][i], Colors["Bg"][i]);
        }

        static string FilePath => Path.Combine(AppContext.BaseDirectory, "mitv-theme.json");

        public static void Set(string name)
        {
            if (Array.IndexOf(Names, name) < 0) name = "Oscuro";
            Current = name;
            try { File.WriteAllText(FilePath, "{\"theme\":\"" + Current + "\"}"); } catch { }
        }

        static Theme()
        {
            try
            {
                var j = File.ReadAllText(FilePath);
                var m = Regex.Match(j, "\"theme\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success && Array.IndexOf(Names, m.Groups[1].Value) >= 0)
                    Current = m.Groups[1].Value;
            }
            catch { }
        }
    }
}
