using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MiTV
{
    // Categorías desactivadas por el usuario ("Editor de categorías").
    // Se recuerdan POR LISTA en mitv-hidden-cats.ini:
    //
    //   [m3u|http://proveedor/get.php?username=x]
    //   tv = DEPORTES, CINE ADULTO
    //   movies = ACCION VIEJA
    //   series =
    //
    // Desactivar = los ítems se BORRAN del caché local (contenido + imágenes);
    // reactivar = se vuelve a pedir al servidor (sincronización).
    // Las categorías se identifican por nombre dentro de cada sección
    // ("tv", "movies", "series"); deportes sale de "tv" automáticamente.
    public static class CatFilter
    {
        // lista -> sección -> nombres
        static Dictionary<string, Dictionary<string, HashSet<string>>> byList = new();

        static string IniPath => Path.Combine(AppContext.BaseDirectory, "mitv-hidden-cats.ini");
        static string JsonPath => Path.Combine(AppContext.BaseDirectory, "mitv-hidden-cats.json");

        static CatFilter() { Load(); }

        // Identidad estable de la lista activa
        static string ListId
        {
            get
            {
                var pl = PlaylistStore.Active;
                if (pl == null) return "";
                return pl.Kind == "xtream"
                    ? "xtream|" + (pl.Server ?? "").Trim().TrimEnd('/') + "|" + (pl.Username ?? "")
                    : "m3u|" + (pl.Url ?? "").Trim();
            }
        }

        static HashSet<string> SectionOf(string listId, string section)
        {
            if (!byList.TryGetValue(listId, out var secs)) byList[listId] = secs = new();
            if (!secs.TryGetValue(section, out var set)) secs[section] = set = new HashSet<string>();
            return set;
        }

        static HashSet<string> Section(string s) => SectionOf(ListId, s);

        static readonly string[] Sections = { "tv", "movies", "series" };

        static void Load()
        {
            byList.Clear();
            if (!File.Exists(IniPath) && File.Exists(JsonPath)) ImportOldJson();
            if (!File.Exists(IniPath)) return;
            try
            {
                string listId = null;
                foreach (var raw in File.ReadAllLines(IniPath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        listId = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq < 0 || listId == null) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    if (!Sections.Contains(key)) continue;
                    var set = SectionOf(listId, key);
                    foreach (var n in line.Substring(eq + 1).Split(','))
                    {
                        string name = n.Trim();
                        if (name.Length > 0) set.Add(name);
                    }
                }
            }
            catch { }
        }

        // Migra el formato viejo JSON {tv:{nombre:conteo}} una sola vez
        static void ImportOldJson()
        {
            try
            {
                var old = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(
                    File.ReadAllText(JsonPath));
                if (old == null) return;
                string id = ListId;
                bool any = false;
                foreach (var (sec, names) in old)
                {
                    if (!Sections.Contains(sec)) continue;
                    var set = SectionOf(id, sec);
                    foreach (var n in names.Keys) { set.Add(n); any = true; }
                }
                if (any) Save();
                try { File.Delete(JsonPath); } catch { }
            }
            catch { }
        }

        static void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("; MiTV - categorías desactivadas por lista (se genera solo, no editar a mano)");
                foreach (var (listId, secs) in byList.OrderBy(k => k.Key))
                {
                    if (secs.Values.All(s => s.Count == 0)) continue;
                    sb.AppendLine();
                    sb.AppendLine("[" + listId + "]");
                    foreach (var sec in Sections)
                    {
                        secs.TryGetValue(sec, out var set);
                        sb.AppendLine(sec + " = " + string.Join(", ", (set ?? new HashSet<string>()).OrderBy(n => n)));
                    }
                }
                File.WriteAllText(IniPath, sb.ToString());
            }
            catch { }
        }

        public static bool IsHidden(string section, string name) =>
            !string.IsNullOrEmpty(name) && Section(section).Contains(name);

        public static HashSet<string> HiddenNames(string section) =>
            new(Section(section));

        // Desactiva: registra, borra ítems del contenido y sus imágenes
        public static void Hide(string section, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            Section(section).Add(name);
            Save();
            var imgUrls = ContentStore.RemoveCategory(section, name);
            ContentStore.SaveCache();
            ImgCache.DeleteForUrls(imgUrls);
        }

        // Reactiva: olvida el ocultamiento y re-sincroniza del servidor
        public static void Unhide(string section, string name)
        {
            if (Section(section).Remove(name)) Save();
            PlaylistLoader.RefreshInBackground();
        }

        // Reactiva varias de una vez (una sola sincronización)
        public static void UnhideMany(string section, IEnumerable<string> names)
        {
            var d = Section(section);
            bool changed = false;
            foreach (var n in names) changed |= d.Remove(n);
            if (!changed) return;
            Save();
            PlaylistLoader.RefreshInBackground();
        }

        // Expulsa del contenido en memoria todo lo de categorías ocultas
        // (se aplica tras cada sincronización y al arrancar).
        public static void PurgeHidden()
        {
            if (!byList.TryGetValue(ListId, out var secs)) return;
            foreach (var (section, names) in secs.ToList())
                foreach (var name in names.ToList())
                    ContentStore.RemoveCategory(section, name);
        }
    }
}
