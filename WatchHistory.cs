using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MiTV
{
    public class WatchEntry
    {
        public string Key { get; set; } = "";       // URL del stream (única)
        public string Title { get; set; } = "";
        public string Sub { get; set; } = "";
        public string Logo { get; set; } = "";
        public string Kind { get; set; } = "";      // "movie" o "channel"
        public string Category { get; set; } = "";  // categoría (para filtrar desactivadas)
        public long PositionMs { get; set; }
        public long DurationMs { get; set; }
        public int Plays { get; set; }              // veces que lo abrió
        public DateTime UpdatedUtc { get; set; }
    }

    // Historial portable (watch-history.json junto al exe):
    // - "Continuar viendo": películas incompletas (3%–95%)
    // - "Canales destacados": los más sintonizados por él
    public static class WatchHistory
    {
        static Dictionary<string, WatchEntry> map = new();
        static DateTime lastSave = DateTime.MinValue;

        static string FilePath => Path.Combine(AppContext.BaseDirectory, "watch-history.json");

        static WatchHistory() { Load(); }

        static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var list = JsonSerializer.Deserialize<List<WatchEntry>>(File.ReadAllText(FilePath));
                if (list != null) map = list.Where(e => e != null && !string.IsNullOrEmpty(e.Key))
                    .ToDictionary(e => e.Key, e => e);
            }
            catch { }
        }

        static void Save()
        {
            try
            {
                // no guardar más de una vez cada 5 segundos
                if ((DateTime.UtcNow - lastSave).TotalSeconds < 5) return;
                lastSave = DateTime.UtcNow;
                File.WriteAllText(FilePath, JsonSerializer.Serialize(map.Values.ToList()));
            }
            catch { }
        }

        public static void SaveNow()
        {
            try
            {
                lastSave = DateTime.UtcNow;
                File.WriteAllText(FilePath, JsonSerializer.Serialize(map.Values.ToList()));
            }
            catch { }
        }

        // Cada vez que abre algo
        public static void RecordPlay(string key, string title, string sub, string logo, string kind, string category = null)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!map.TryGetValue(key, out var e))
            {
                e = new WatchEntry { Key = key };
                map[key] = e;
            }
            e.Title = title ?? "";
            e.Sub = sub ?? "";
            e.Logo = logo ?? "";
            e.Kind = kind ?? "";
            if (!string.IsNullOrEmpty(category)) e.Category = category;
            e.Plays++;
            e.UpdatedUtc = DateTime.UtcNow;
            Save();
        }

        // Progreso de una película (cada ~10s y al salir)
        public static void RecordProgress(string key, long posMs, long durMs)
        {
            if (string.IsNullOrEmpty(key) || !map.TryGetValue(key, out var e)) return;
            if (durMs <= 0) return;
            e.PositionMs = Math.Max(0, posMs);
            e.DurationMs = durMs;
            e.UpdatedUtc = DateTime.UtcNow;
            Save();
        }

        public static long ResumePosition(string key)
        {
            if (string.IsNullOrEmpty(key) || !map.TryGetValue(key, out var e)) return 0;
            return e.PositionMs;
        }

        // Un elemento del historial solo se muestra si su ítem sigue existiendo
        // en el contenido (una categoría desactivada lo hace desaparecer de todas partes)
        static bool EntryVisible(WatchEntry e)
        {
            if (e == null || string.IsNullOrEmpty(e.Key)) return false;
            string cat = !string.IsNullOrEmpty(e.Category) ? e.Category : e.Sub;
            return e.Kind switch
            {
                "channel" => !CatFilter.IsHidden("tv", cat),
                "movie" => !CatFilter.IsHidden("movies", cat),
                "episode" => !CatFilter.IsHidden("series", cat),
                _ => true,
            };
        }

        // Películas incompletas, las más recientes primero
        public static bool IsWatched(string key)
        {
            if (map.TryGetValue(key ?? "", out var e) && e.DurationMs > 0)
                return e.PositionMs >= e.DurationMs * 0.95;
            return false;
        }

        public static List<WatchEntry> ContinueWatching() =>
            map.Values
                .Where(e => e.Kind == "movie" && EntryVisible(e) && e.DurationMs > 0
                    && e.PositionMs > e.DurationMs * 0.03
                    && e.PositionMs < e.DurationMs * 0.95)
                .OrderByDescending(e => e.UpdatedUtc)
                .Take(12).ToList();

        // Canales más sintonizados
        public static List<WatchEntry> TopChannels(int n) =>
            map.Values
                .Where(e => e.Kind == "channel" && EntryVisible(e))
                .OrderByDescending(e => e.Plays)
                .ThenByDescending(e => e.UpdatedUtc)
                .Take(n).ToList();

        // Últimos N elementos abiertos que cumplan el filtro, más recientes primero
        public static List<WatchEntry> LastOpened(int n, Func<WatchEntry, bool> pred) =>
            map.Values
                .Where(e => EntryVisible(e))
                .Where(pred)
                .OrderByDescending(e => e.UpdatedUtc)
                .Take(n).ToList();

        public static bool IsSportsGroup(string g) =>
            !string.IsNullOrEmpty(g) &&
            (g.IndexOf("sport", StringComparison.OrdinalIgnoreCase) >= 0 ||
             g.IndexOf("deporte", StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
