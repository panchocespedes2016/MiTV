using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MiTV
{
    // Caché de imágenes en disco (portable, junto al exe).
    // Las otras apps muestran todo al instante porque no descargan dos veces:
    // la primera vez se baja, después sale del disco.
    public static class ImgCache
    {
        static string Dir => Path.Combine(AppContext.BaseDirectory, "imgcache");

        // Tamaño máximo configurable (mitv-imgcache.json): GB, 0 = sin límite.
        // Al llenarse se expulsan las imágenes más viejas.
        static string SettingsFile => Path.Combine(AppContext.BaseDirectory, "mitv-imgcache.json");
        class CacheData { public int LimitGB { get; set; } = 10; }
        static readonly CacheData cacheData = LoadCacheData();

        static CacheData LoadCacheData()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var x = JsonSerializer.Deserialize<CacheData>(File.ReadAllText(SettingsFile));
                    if (x != null) return x;
                }
            }
            catch { }
            return new CacheData();
        }

        public static int LimitGB
        {
            get => cacheData.LimitGB;
            set
            {
                cacheData.LimitGB = value;
                try { File.WriteAllText(SettingsFile, JsonSerializer.Serialize(cacheData)); } catch { }
            }
        }

        static long MaxBytes => cacheData.LimitGB <= 0 ? long.MaxValue : (long)cacheData.LimitGB * 1024L * 1024 * 1024;

        static readonly HttpClient http = new HttpClient(new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 32,
            AutomaticDecompression = DecompressionMethods.All
        });

        // Límite de operaciones simultáneas (descarga + decodificado):
        // sin esto, miles de imágenes a la vez saturan red e hilo de UI
        static readonly SemaphoreSlim slots = new SemaphoreSlim(16);

        // Tamaño actual de la caché en disco
        public static long CurrentSize()
        {
            try
            {
                var d = new DirectoryInfo(Dir);
                if (!d.Exists) return 0;
                return d.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch { return 0; }
        }

        // Borra toda la caché de imágenes. Devuelve los bytes liberados.
        public static long Clear()
        {
            long freed = 0;
            try
            {
                var d = new DirectoryInfo(Dir);
                if (!d.Exists) return 0;
                foreach (var f in d.GetFiles()) { try { freed += f.Length; f.Delete(); } catch { } }
                foreach (var sub in d.GetDirectories())
                {
                    try
                    {
                        freed += sub.GetFiles("*", SearchOption.AllDirectories).Sum(x => x.Length);
                        sub.Delete(true);
                    }
                    catch { }
                }
            }
            catch { }
            return freed;
        }

        public static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024 * 1024)).ToString("0.0") + " GB";
            if (bytes >= 1024L * 1024) return (bytes / (1024.0 * 1024)).ToString("0") + " MB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("0") + " KB";
            return bytes + " B";
        }

        static ImgCache()
        {
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MiTV", "1.0"));
            try { Task.Run(Cleanup); } catch { }
        }

        // Borra del disco las imágenes de las URLs dadas
        public static void DeleteForUrls(IEnumerable<string> urls)
        {
            if (urls == null) return;
            foreach (var u in urls)
            {
                if (string.IsNullOrEmpty(u)) continue;
                try
                {
                    string p = PathFor(u);
                    if (File.Exists(p)) File.Delete(p);
                }
                catch { }
            }
        }

        static string PathFor(string url)
        {
            string ext = ".jpg";
            try
            {
                var e = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
                if (e == ".jpg" || e == ".jpeg" || e == ".png" || e == ".webp" || e == ".gif")
                    ext = e == ".jpeg" ? ".jpg" : e;
            }
            catch { }
            using var sha = SHA256.Create();
            var h = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(url)))
                .Replace("-", "").ToLowerInvariant();
            return Path.Combine(Dir, h + ext);
        }

        // Carga la imagen en el thumb: del disco si está, si no la descarga y la guarda.
        // Todo lo pesado corre fuera del hilo de UI, con reintentos si falla la red.
        public static void Load(Border thumb, TextBlock placeholder, string url, int decodeWidth)
        {
            if (thumb == null || string.IsNullOrEmpty(url)) return;
            var disp = thumb.Dispatcher;
            Task.Run(async () =>
            {
                await slots.WaitAsync();
                try
                {
                    string path = PathFor(url);
                    if (!File.Exists(path))
                    {
                        byte[] bytes = null;
                        for (int attempt = 0; attempt < 3 && bytes == null; attempt++)
                        {
                            try
                            {
                                var b = await http.GetByteArrayAsync(url);
                                if (b.Length >= 100) bytes = b;
                            }
                            catch { }
                            if (bytes == null && attempt < 2)
                                await Task.Delay(1000 * (attempt + 1));
                        }
                        if (bytes == null) return;
                        Directory.CreateDirectory(Dir);
                        try { File.WriteAllBytes(path, bytes); } catch { }
                    }
                    else
                    {
                        try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { }
                    }
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.DecodePixelWidth = decodeWidth;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    _ = disp.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            thumb.Background = new ImageBrush(bmp)
                            {
                                Stretch = Stretch.UniformToFill,
                                AlignmentX = AlignmentX.Center,
                                AlignmentY = AlignmentY.Center
                            };
                            if (placeholder != null) placeholder.Visibility = Visibility.Collapsed;
                        }
                        catch { }
                    }));
                }
                catch { }
                finally { slots.Release(); }
            });
        }

        // Descarga en segundo plano sin mostrar (para adelantar la próxima página)
        public static void Prefetch(IEnumerable<string> urls)
        {
            foreach (var u in urls)
            {
                if (string.IsNullOrEmpty(u)) continue;
                string url = u;
                Task.Run(async () =>
                {
                    try
                    {
                        string path = PathFor(url);
                        if (File.Exists(path)) return;
                        var bytes = await http.GetByteArrayAsync(url);
                        if (bytes.Length < 100) return;
                        Directory.CreateDirectory(Dir);
                        File.WriteAllBytes(path, bytes);
                    }
                    catch { }
                });
            }
        }

        static void Cleanup()
        {
            try
            {
                var d = new DirectoryInfo(Dir);
                if (!d.Exists) return;
                var files = d.GetFiles().OrderBy(f => f.LastWriteTimeUtc).ToList();
                long total = files.Sum(f => f.Length);
                foreach (var f in files)
                {
                    if (total <= MaxBytes) break;
                    try { total -= f.Length; f.Delete(); } catch { }
                }
            }
            catch { }
        }
    }
}
