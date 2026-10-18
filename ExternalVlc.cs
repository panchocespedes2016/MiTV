using System;
using System.Diagnostics;
using System.IO;

namespace MiTV
{
    // Envía un stream al VLC de escritorio (externo a la app)
    public static class ExternalVlc
    {
        public static string FindVlc()
        {
            string[] paths =
            {
                @"C:\Program Files\VideoLAN\VLC\vlc.exe",
                @"C:\Program Files (x86)\VideoLAN\VLC\vlc.exe",
            };
            foreach (var p in paths)
                if (File.Exists(p)) return p;
            // VLC registra vlc.exe en "App Paths" al instalarse: lo resuelve el shell
            return "vlc.exe";
        }

        // Devuelve false si no se pudo lanzar VLC
        public static bool Play(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return PlayMultiple(new[] { url });
        }

        // Varias URLs como lista de reproducción
        public static bool PlayMultiple(System.Collections.Generic.IEnumerable<string> urls)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var u in urls) if (!string.IsNullOrEmpty(u)) list.Add("\"" + u + "\"");
            if (list.Count == 0) return false;
            try
            {
                Process.Start(new ProcessStartInfo(FindVlc(), string.Join(" ", list)) { UseShellExecute = true });
                return true;
            }
            catch { return false; }
        }
    }
}
