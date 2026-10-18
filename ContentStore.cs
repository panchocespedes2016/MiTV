using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;

namespace MiTV
{
    public class LiveChannel
    {
        public string Name { get; set; } = "";
        public string Logo { get; set; } = "";
        public string StreamUrl { get; set; } = "";
        public string Group { get; set; } = "";
        public string TvgId { get; set; } = ""; // tvg-id (M3U) o epg_channel_id (Xtream)
        public string ProviderNum { get; set; } = ""; // num del proveedor (Xtream)
        public bool HasCatchup { get; set; } // tv_archive (Xtream)
        public string StreamId { get; set; } = ""; // stream_id (Xtream, para catchup)
        public int ArchiveDays { get; set; } // tv_archive_duration en días
    }

    public class VodMovie
    {
        public string Name { get; set; } = "";
        public string Logo { get; set; } = "";
        public string StreamUrl { get; set; } = "";
        public string Category { get; set; } = "";
        public long Added { get; set; } // unix timestamp (Xtream); 0 si se desconoce
        public string StreamId { get; set; } = ""; // Xtream stream_id para get_vod_info
        public string Year { get; set; } = ""; // año extraído del nombre o Added
        public string Rating { get; set; } = ""; // rating del proveedor
    }

    public class SerieItem
    {
        public string Name { get; set; } = "";
        public string Cover { get; set; } = "";
        public string SerieId { get; set; } = "";
        public string Category { get; set; } = "";
        public string StreamUrl { get; set; } = ""; // M3U: URL directa; Xtream: se listan episodios
        public long Added { get; set; } // unix timestamp (Xtream); 0 si se desconoce
    }

    // Contenido cargado de la lista activa. Vive en content-cache.json
    // junto al exe: la app lo carga de ahí al abrir (rápido) y lo
    // refresca de la fuente en segundo plano.
    public static class ContentStore
    {
        public static List<LiveChannel> Channels = new();
        public static List<VodMovie> Movies = new();
        public static List<SerieItem> Series = new();
        public static string SourceName = "";
        public static DateTime UpdatedUtc;

        public static bool HasData =>
            Channels.Count > 0 || Movies.Count > 0 || Series.Count > 0;

        static string FilePath =>
            Path.Combine(AppContext.BaseDirectory, "content-cache.json");

        class Cache
        {
            public List<LiveChannel> Channels { get; set; }
            public List<VodMovie> Movies { get; set; }
            public List<SerieItem> Series { get; set; }
            public string SourceName { get; set; }
            public DateTime UpdatedUtc { get; set; }
        }

        public static void LoadCache()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var c = JsonSerializer.Deserialize<Cache>(File.ReadAllText(FilePath));
                if (c == null) return;
                Channels = c.Channels ?? new();
                Movies = c.Movies ?? new();
                Series = c.Series ?? new();
                SourceName = c.SourceName ?? "";
                UpdatedUtc = c.UpdatedUtc;
            }
            catch { }
        }

        public static void SaveCache()
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(new Cache
                {
                    Channels = Channels,
                    Movies = Movies,
                    Series = Series,
                    SourceName = SourceName,
                    UpdatedUtc = UpdatedUtc
                }));
            }
            catch { }
        }

        // Borra todos los ítems de una categoría ("tv"/"movies"/"series").
        // Devuelve las URLs de sus imágenes para purgar la caché.
        public static List<string> RemoveCategory(string section, string name)
        {
            var urls = new List<string>();
            if (string.IsNullOrEmpty(name)) return urls;
            if (section == "tv")
            {
                urls.AddRange(Channels.Where(c => c.Group == name).Select(c => c.Logo));
                Channels.RemoveAll(c => c.Group == name);
            }
            else if (section == "movies")
            {
                urls.AddRange(Movies.Where(m => m.Category == name).Select(m => m.Logo));
                Movies.RemoveAll(m => m.Category == name);
            }
            else if (section == "series")
            {
                urls.AddRange(Series.Where(s => s.Category == name).Select(s => s.Cover));
                Series.RemoveAll(s => s.Category == name);
            }
            return urls;
        }

        public static void Clear()
        {
            Channels = new(); Movies = new(); Series = new();
            SourceName = ""; UpdatedUtc = default;
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }
    }
}
