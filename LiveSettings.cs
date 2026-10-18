using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MiTV
{
    // Ajustes de TV en vivo. Portable: mitv-live.json junto al exe.
    public static class LiveSettings
    {
        static string FilePath => Path.Combine(AppContext.BaseDirectory, "mitv-live.json");

        class Data
        {
            public bool GroupByCategory { get; set; } = true;
            public bool FavoritesFirst { get; set; } = true;
            public bool HideNoEpg { get; set; } = false;
        }
        static Data d = new();
        static LiveSettings() { Load(); }

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

        public static bool GroupByCategory { get => d.GroupByCategory; set { d.GroupByCategory = value; Save(); } }
        public static bool FavoritesFirst { get => d.FavoritesFirst; set { d.FavoritesFirst = value; Save(); } }
        public static bool HideNoEpg { get => d.HideNoEpg; set { d.HideNoEpg = value; Save(); } }
    }

    // Canales favoritos (por StreamUrl, único). Portable: mitv-favorites.json
    public static class Favorites
    {
        static string FilePath => Path.Combine(AppContext.BaseDirectory, "mitv-favorites.json");
        static HashSet<string> favs = new();
        static Favorites() { Load(); }

        static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var l = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath));
                    if (l != null) favs = new HashSet<string>(l);
                }
            }
            catch { }
        }
        static void Save()
        {
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(favs.ToArray())); } catch { }
        }

        static string Key(LiveChannel c) => c.StreamUrl ?? "";
        public static bool IsFav(LiveChannel c) => IsFavKey(Key(c));
        public static void Toggle(LiveChannel c) => ToggleKey(Key(c));

        // Variante por clave arbitraria (películas, series, etc.)
        public static bool IsFavKey(string k) => !string.IsNullOrEmpty(k) && favs.Contains(k);
        public static void ToggleKey(string k)
        {
            if (string.IsNullOrEmpty(k)) return;
            if (!favs.Remove(k)) favs.Add(k);
            Save();
        }
    }
}
