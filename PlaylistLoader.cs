using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace MiTV
{
    // Descarga la lista activa (M3U o Xtream Codes) y llena el ContentStore
    // con canales, películas y series.
    public static class PlaylistLoader
    {
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        static PlaylistLoader()
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MiTV/1.0");
        }

        public static async Task<(bool ok, string msg)> RefreshAsync(bool skipEpg = false)
        {
            var pl = PlaylistStore.Active;
            if (pl == null) return (false, "No hay lista activa");

            if (pl.Kind == "xtream")
                await LoadXtreamAsync(pl);
            else
                await LoadM3uAsync(pl);

            ContentStore.SourceName = pl.Name ?? "";
            ContentStore.UpdatedUtc = DateTime.UtcNow;
            CatFilter.PurgeHidden();
            ContentStore.SaveCache();

            if (!skipEpg) { try { await EpgStore.RefreshAsync(pl); } catch { } }

            return (true, $"{ContentStore.Channels.Count} canales, " +
                          $"{ContentStore.Movies.Count} películas, " +
                          $"{ContentStore.Series.Count} series");
        }

        // quiet=true: solo avisa si todo salió bien (para el arranque)
        public static void RefreshInBackground(bool quiet = false)
        {
            var pl = PlaylistStore.Active;
            if (pl == null) return;
            if (!quiet) Nav.Toast("Sincronizando " + pl.Name + "…");
            Task.Run(async () =>
            {
                bool ok; string msg;
                try { (ok, msg) = await RefreshAsync(skipEpg: quiet && !EpgSettings.UpdateOnStart); }
                catch (Exception ex) { ok = false; msg = "Error: " + ex.Message; }
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (ok || !quiet) Nav.Toast(ok ? "Sincronizado: " + msg : msg);
                    if (ok) Nav.RefreshContent();
                });
            });
        }

        // ---------- Xtream Codes ----------

        static async Task LoadXtreamAsync(Playlist pl)
        {
            string srv = (pl.Server ?? "").Trim().TrimEnd('/');
            string u = Uri.EscapeDataString(pl.Username ?? "");
            string p = Uri.EscapeDataString(pl.Password ?? "");
            string auth = $"username={u}&password={p}";

            var liveCats = await GetCategories($"{srv}/player_api.php?{auth}&action=get_live_categories");
            var vodCats = await GetCategories($"{srv}/player_api.php?{auth}&action=get_vod_categories");
            var seriesCats = await GetCategories($"{srv}/player_api.php?{auth}&action=get_series_categories");

            var channels = new List<LiveChannel>();
            foreach (var el in await GetArray($"{srv}/player_api.php?{auth}&action=get_live_streams"))
            {
                string id = JStr(el, "stream_id");
                if (string.IsNullOrEmpty(id)) continue;
                string chName = JStr(el, "name");
                if (IsJunkChannel(chName)) continue;
                channels.Add(new LiveChannel
                {
                    Name = chName,
                    Logo = JStr(el, "stream_icon"),
                    Group = CatName(liveCats, JStr(el, "category_id")),
                    TvgId = JStr(el, "epg_channel_id"),
                    StreamUrl = $"{srv}/live/{u}/{p}/{id}.m3u8",
                    ProviderNum = JStr(el, "num"),
                    HasCatchup = JStr(el, "tv_archive") == "1",
                    StreamId = id,
                    ArchiveDays = (int)(JLong(el, "tv_archive_duration") / 24)
                });
            }

            var movies = new List<VodMovie>();
        static string ExtractYear(string name, long added)
        {
            // Buscar año de 4 dígitos en el nombre (1900-2030)
            var m = System.Text.RegularExpressions.Regex.Match(name ?? "", @"\b(19\d{2}|20[0-2]\d|2030)\b");
            if (m.Success) return m.Value;
            if (added > 0)
            {
                try { return DateTimeOffset.FromUnixTimeSeconds(added).Year.ToString(); } catch { }
            }
            return "";
        }

            foreach (var el in await GetArray($"{srv}/player_api.php?{auth}&action=get_vod_streams"))
            {
                string id = JStr(el, "stream_id");
                if (string.IsNullOrEmpty(id)) continue;
                string ext = JStr(el, "container_extension");
                if (string.IsNullOrEmpty(ext)) ext = "mp4";
                string mName = JStr(el, "name");
                long mAdded = JLong(el, "added");
                movies.Add(new VodMovie
                {
                    Name = mName,
                    Logo = JStr(el, "stream_icon"),
                    Category = CatName(vodCats, JStr(el, "category_id")),
                    StreamUrl = $"{srv}/movie/{u}/{p}/{id}.{ext}",
                    Added = mAdded,
                    StreamId = id,
                    Year = ExtractYear(mName, mAdded),
                    Rating = JStr(el, "rating")
                });
            }

            var series = new List<SerieItem>();
            foreach (var el in await GetArray($"{srv}/player_api.php?{auth}&action=get_series"))
            {
                string id = JStr(el, "series_id");
                if (string.IsNullOrEmpty(id)) continue;
                series.Add(new SerieItem
                {
                    Name = JStr(el, "name"),
                    Cover = JStr(el, "cover"),
                    SerieId = id,
                    Category = CatName(seriesCats, JStr(el, "category_id")),
                    Added = JLong(el, "added")
                });
            }

            ContentStore.Channels = channels;
            ContentStore.Movies = movies;
            ContentStore.Series = series;
        }

        static async Task<Dictionary<string, string>> GetCategories(string url)
        {
            var d = new Dictionary<string, string>();
            try
            {
                foreach (var el in await GetArray(url))
                {
                    string id = JStr(el, "category_id");
                    string name = JStr(el, "category_name");
                    if (!string.IsNullOrEmpty(id) && !d.ContainsKey(id))
                        d[id] = name;
                }
            }
            catch { }
            return d;
        }

        static string CatName(Dictionary<string, string> cats, string id) =>
            cats.TryGetValue(id ?? "", out var n) ? n : "";

        static async Task<List<JsonElement>> GetArray(string url)
        {
            string json = await http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var list = new List<JsonElement>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var el in doc.RootElement.EnumerateArray())
                    list.Add(el.Clone());
            return list;
        }

        static string JStr(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var v)) return "";
            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? "",
                JsonValueKind.Number => v.GetRawText(),
                JsonValueKind.True => "1",
                JsonValueKind.False => "0",
                _ => ""
            };
        }

        static long JLong(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var v)) return 0;
            string s = v.ValueKind == JsonValueKind.Number ? v.GetRawText() : v.GetString() ?? "";
            return long.TryParse(s, out var n) ? n : 0;
        }

        // Canales basura del proveedor (ej: "##### PRIME #####"): se descartan
        // durante la sincronización y no se cuentan en ningún lugar.
        static bool IsJunkChannel(string name)
        {
            return !string.IsNullOrEmpty(name) && name.Contains("###");
        }

        // ---------- Episodios de serie (Xtream) ----------

        public class EpisodeInfo
        {
            public string Season;
            public string Num;
            public string Title;
            public string Url;
            public string Plot = "";
            public string Duration = "";
            public string Image = "";
        }

        public class SeriesInfo
        {
            public string Plot = "";
            public string Cast = "";
            public string Rating = "";
            public string Genre = "";
            public string ReleaseDate = "";
            public string Director = "";
        }

        public static async Task<SeriesInfo> GetSeriesInfoAsync(string seriesId)
        {
            var info = new SeriesInfo();
            try
            {
                var pl = PlaylistStore.Active;
                if (pl == null || pl.Kind != "xtream" || string.IsNullOrEmpty(seriesId)) return info;
                string srv = pl.Server.TrimEnd('/');
                string u = pl.Username, p = pl.Password;
                string json = await http.GetStringAsync($"{srv}/player_api.php?username={u}&password={p}&action=get_series_info&series_id={seriesId}");
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("info", out var inf))
                {
                    info.Plot = JStr(inf, "plot");
                    info.Cast = JStr(inf, "cast");
                    info.Rating = JStr(inf, "rating");
                    if (string.IsNullOrEmpty(info.Rating)) info.Rating = JStr(inf, "rating_5based");
                    info.Genre = JStr(inf, "genre");
                    info.ReleaseDate = JStr(inf, "releaseDate");
                    info.Director = JStr(inf, "director");
                }
            }
            catch { }
            return info;
        }

        public class VodInfo
        {
            public string Plot = "";
            public string Cast = "";
            public string Rating = "";
            public string Genre = "";
            public string Duration = "";
            public string Director = "";
            public string ReleaseDate = "";
            public string Country = "";
            public string Age = "";
        }

        public static async Task<VodInfo> GetVodInfoAsync(string streamId)
        {
            var info = new VodInfo();
            try
            {
                var pl = PlaylistStore.Active;
                if (pl == null || pl.Kind != "xtream" || string.IsNullOrEmpty(streamId)) return info;
                string srv = pl.Server.TrimEnd('/');
                string u = pl.Username, p = pl.Password;
                string json = await http.GetStringAsync($"{srv}/player_api.php?username={u}&password={p}&action=get_vod_info&vod_id={streamId}");
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var el = doc.RootElement;
                if (el.TryGetProperty("info", out var inf))
                {
                    info.Plot = JStr(inf, "plot");
                    info.Cast = JStr(inf, "cast");
                    info.Rating = JStr(inf, "rating");
                    info.Genre = JStr(inf, "genre");
                    info.Duration = JStr(inf, "duration");
                    if (string.IsNullOrEmpty(info.Duration)) info.Duration = JStr(inf, "episode_run_time");
                    info.Director = JStr(inf, "director");
                    info.ReleaseDate = JStr(inf, "releaseDate");
                    if (string.IsNullOrEmpty(info.ReleaseDate)) info.ReleaseDate = JStr(inf, "releasedate");
                    info.Country = JStr(inf, "country");
                    info.Age = JStr(inf, "age");
                }
            }
            catch { }
            return info;
        }

        public static async Task<List<EpisodeInfo>> GetSeriesEpisodesAsync(string seriesId)
        {
            var pl = PlaylistStore.Active;
            var eps = new List<EpisodeInfo>();
            if (pl == null || pl.Kind != "xtream" || string.IsNullOrEmpty(seriesId))
                return eps;
            string srv = (pl.Server ?? "").Trim().TrimEnd('/');
            string u = Uri.EscapeDataString(pl.Username ?? "");
            string p = Uri.EscapeDataString(pl.Password ?? "");
            string json = await http.GetStringAsync(
                $"{srv}/player_api.php?username={u}&password={p}&action=get_series_info&series_id={Uri.EscapeDataString(seriesId)}");
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("episodes", out var episodes)) return eps;
            foreach (var season in episodes.EnumerateObject())
            {
                foreach (var el in season.Value.EnumerateArray())
                {
                    string id = JStr(el, "id");
                    if (string.IsNullOrEmpty(id)) continue;
                    string ext = JStr(el, "container_extension");
                    if (string.IsNullOrEmpty(ext)) ext = "mp4";
                    var epi = new EpisodeInfo
                    {
                        Season = season.Name,
                        Num = JStr(el, "episode_num"),
                        Title = JStr(el, "title"),
                        Url = $"{srv}/series/{u}/{p}/{id}.{ext}"
                    };
                    try
                    {
                        if (el.TryGetProperty("info", out var einf))
                        {
                            epi.Plot = JStr(einf, "plot");
                            epi.Duration = JStr(einf, "duration");
                            epi.Image = JStr(einf, "movie_image");
                        }
                    }
                    catch { }
                    eps.Add(epi);
                }
            }
            return eps;
        }

        // ---------- M3U ----------

        static async Task LoadM3uAsync(Playlist pl)
        {
            string text = await http.GetStringAsync(pl.Url);

            var channels = new List<LiveChannel>();
            var movies = new List<VodMovie>();
            var series = new List<SerieItem>();

            string name = null, logo = null, group = null, tvgId = null;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                {
                    name = ExtInfName(line);
                    logo = Attr(line, "tvg-logo");
                    group = Attr(line, "group-title");
                    tvgId = Attr(line, "tvg-id");
                }
                else if (name != null && line.Length > 0 && !line.StartsWith("#"))
                {
                    string url = line;
                    string g = (group ?? "").ToLowerInvariant();
                    if (g.Contains("series"))
                        series.Add(new SerieItem { Name = name, Cover = logo ?? "", Category = group ?? "", StreamUrl = url });
                    else if (g.Contains("movie") || g.Contains("film") || g.Contains("vod") ||
                             g.Contains("pelicula") || g.Contains("película") || g.Contains("cine") ||
                             url.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                             url.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
                        movies.Add(new VodMovie { Name = name, Logo = logo ?? "", StreamUrl = url, Category = group ?? "" });
                    else if (!IsJunkChannel(name))
                        channels.Add(new LiveChannel { Name = name, Logo = logo ?? "", StreamUrl = url, Group = group ?? "", TvgId = tvgId ?? "" });
                    name = null; logo = null; group = null; tvgId = null;
                }
            }

            ContentStore.Channels = channels;
            ContentStore.Movies = movies;
            ContentStore.Series = series;
        }

        // Nombre después de la primera coma fuera de comillas
        // (los atributos pueden contener comas entre comillas)
        static string ExtInfName(string line)
        {
            bool inQ = false;
            for (int i = 8; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') inQ = !inQ;
                else if (c == ',' && !inQ) return line.Substring(i + 1).Trim();
            }
            return "";
        }

        static string Attr(string extinf, string key)
        {
            var m = Regex.Match(extinf, key + "=\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }
    }
}
