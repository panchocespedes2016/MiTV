using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MiTV
{
    // Recuerda la última categoría visitada por sección ("livetv", "movies", "series").
    // Portable: mitv-state.json junto al exe.
    public static class ViewState
    {
        static Dictionary<string, string> last = new();

        static string FilePath => Path.Combine(AppContext.BaseDirectory, "mitv-state.json");

        static ViewState() { Load(); }

        static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
                if (d != null) last = d;
            }
            catch { }
        }

        static void Save()
        {
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(last)); }
            catch { }
        }

        public static string Get(string section) =>
            last.TryGetValue(section, out var c) && !string.IsNullOrEmpty(c) ? c : "Todo";

        public static void Set(string section, string category)
        {
            string v = string.IsNullOrEmpty(category) ? "Todo" : category;
            if (last.TryGetValue(section, out var cur) && cur == v) return;
            last[section] = v;
            Save();
        }

        // Pestaña inicial de la app (0=Inicio … 5=Guía). Se lee al arrancar.
        public static int StartTab
        {
            get => last.TryGetValue("starttab", out var v) && int.TryParse(v, out var i) && i >= 0 && i <= 6 ? i : 0;
            set
            {
                if (StartTab == value) return;
                last["starttab"] = value.ToString();
                Save();
            }
        }
    }
}
