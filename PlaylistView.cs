using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MiTV
{
    public class Playlist
    {
        public string Kind = "m3u"; // "m3u" | "xtream"
        public string Name, Url, EpgUrl;          // lista M3U
        public string Server, Username, Password; // Xtream Codes
        public bool IsActive;

        [JsonIgnore] public string Detail => Kind == "xtream" ? (Server ?? "") : (Url ?? "");
        [JsonIgnore] public string KindTag => Kind == "xtream" ? "XTREAM" : "M3U";
    }

    public static class PlaylistStore
    {
        // Persistencia portable: playlists.json junto al exe (lo puedo escribir por SSH)
        static readonly JsonSerializerOptions jsonOpts = new() { IncludeFields = true, WriteIndented = true };
        static string FilePath => Path.Combine(AppContext.BaseDirectory, "playlists.json");

        public static System.Collections.Generic.List<Playlist> Items = new();

        static PlaylistStore() { Load(); }

        public static Playlist Active => Items.FirstOrDefault(p => p.IsActive);

        public static void SetActive(Playlist p)
        {
            foreach (var x in Items) x.IsActive = false;
            p.IsActive = true;
            Save();
        }

        public static void Add(Playlist p) { Items.Add(p); Save(); }

        public static void Remove(Playlist p)
        {
            Items.Remove(p);
            if (p.IsActive && Items.Count > 0) Items[0].IsActive = true;
            Save();
        }

        public static void Save()
        {
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Items, jsonOpts)); }
            catch { }
        }

        static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var items = JsonSerializer.Deserialize<System.Collections.Generic.List<Playlist>>(
                        File.ReadAllText(FilePath), jsonOpts);
                    if (items != null && items.Count > 0) { Items = items; return; }
                }
            }
            catch { }
            Items = new System.Collections.Generic.List<Playlist>
            {
                new Playlist { Kind = "m3u", Name = "Mi lista principal", Url = "http://ejemplo.com/lista.m3u", EpgUrl = "", IsActive = true },
                new Playlist { Kind = "m3u", Name = "Deportes", Url = "http://ejemplo.com/deportes.m3u", EpgUrl = "", IsActive = false },
                new Playlist { Kind = "xtream", Name = "Servidor Xtream", Server = "http://ejemplo.tv:8080", Username = "demo", Password = "demo", IsActive = false },
            };
        }
    }

    // Lista de listas IPTV: agregar, editar, eliminar, marcar activa
    public class PlaylistView : ViewBase
    {
        public PlaylistView()
        {
            var sp = new StackPanel { Margin = new Thickness(26, 14, 26, 18) };
            Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Build(sp);
        }

        public override void OnShow()
        {
            var sp = (StackPanel)((ScrollViewer)Content).Content;
            Build(sp);
        }

        void Build(StackPanel sp)
        {
            sp.Children.Clear();
            sp.Children.Add(Ui.BackHeader(Lang.T("Listas IPTV"), () => Nav.Back()));
            var topRow = new StackPanel { Orientation = Orientation.Horizontal };
            var addBtn = Ui.ActionButton(Lang.T("+ Agregar lista"), () =>
                Nav.Push(new PlaylistEditView(null, Refresh)));
            addBtn.Margin = new Thickness(0, 0, 12, 14);
            System.Windows.Controls.Button syncBtn = null;
            syncBtn = Ui.ActionButton(Lang.T("⟳ Sincronizar"), () =>
            {
                // Feedback visual: cambia a verde 2 segundos para confirmar que se activó
                var origBg = syncBtn.Background;
                syncBtn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x32));
                var fbTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                fbTimer.Tick += (ts, te) => { fbTimer.Stop(); syncBtn.Background = origBg; };
                fbTimer.Start();
                PlaylistLoader.RefreshInBackground(quiet: false);
            });
            syncBtn.Margin = new Thickness(0, 0, 0, 14);
            topRow.Children.Add(addBtn);
            topRow.Children.Add(syncBtn);
            sp.Children.Add(topRow);

            foreach (var p in PlaylistStore.Items)
            {
                var pl = p;
                var row = new Border
                {
                    Background = Ui.Panel,
                    BorderBrush = Ui.Brd,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(18, 14, 18, 14),
                    Margin = new Thickness(0, 0, 0, 10),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                var dock = new DockPanel();
                var chev = new TextBlock { Text = "›", FontSize = 18, Foreground = Ui.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                DockPanel.SetDock(chev, Dock.Right);
                dock.Children.Add(chev);
                if (pl.IsActive)
                {
                    var badge = new Border
                    {
                        Background = Ui.Red,
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(10, 3, 10, 3),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(10, 0, 0, 0),
                        Child = new TextBlock { Text = Lang.T("ACTIVA"), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White }
                    };
                    DockPanel.SetDock(badge, Dock.Right);
                    dock.Children.Add(badge);
                }
                var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var nameRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                nameRow.Children.Add(new TextBlock { Text = pl.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Ui.Text, VerticalAlignment = VerticalAlignment.Center });
                nameRow.Children.Add(new Border
                {
                    Background = Ui.Panel2,
                    BorderBrush = Ui.Brd,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = pl.KindTag, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Ui.Gray }
                });
                tx.Children.Add(nameRow);
                tx.Children.Add(new TextBlock { Text = pl.Detail, FontSize = 13, Foreground = Ui.Gray, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
                dock.Children.Add(tx);
                row.Child = dock;
                row.MouseLeftButtonUp += (s, e) => Nav.Push(new PlaylistEditView(pl, Refresh));
                row.MouseEnter += (s, e) => row.BorderBrush = Ui.Red;
                row.MouseLeave += (s, e) => row.BorderBrush = Ui.Brd;
                sp.Children.Add(row);
            }
        }

        void Refresh() { Nav.Back(); Nav.Push(new PlaylistView()); }
    }

    // Agregar / editar una lista (M3U o Xtream Codes)
    public class PlaylistEditView : ViewBase
    {
        public PlaylistEditView(Playlist p, System.Action onDone)
        {
            bool isNew = p == null;
            var pl = p ?? new Playlist();
            string kind = pl.Kind ?? "m3u";

            var sp = new StackPanel { Margin = new Thickness(26, 14, 26, 18) };
            sp.Children.Add(Ui.BackHeader(isNew ? Lang.T("Agregar lista") : Lang.T("Editar lista"), () => Nav.Back()));

            sp.Children.Add(Ui.FieldLabel(Lang.T("Tipo de lista")));
            var typeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            sp.Children.Add(typeRow);

            sp.Children.Add(Ui.FieldLabel(Lang.T("Nombre")));
            var nameBox = Ui.DarkTextBox(pl.Name);
            sp.Children.Add(nameBox);

            // Campos M3U
            var m3uPanel = new StackPanel();
            m3uPanel.Children.Add(Ui.FieldLabel(Lang.T("URL de la lista (m3u)")));
            var urlBox = Ui.DarkTextBox(pl.Url);
            m3uPanel.Children.Add(urlBox);
            m3uPanel.Children.Add(Ui.FieldLabel(Lang.T("URL del EPG (opcional)")));
            var epgBox = Ui.DarkTextBox(pl.EpgUrl);
            m3uPanel.Children.Add(epgBox);
            sp.Children.Add(m3uPanel);

            // Campos Xtream
            var xtPanel = new StackPanel();
            xtPanel.Children.Add(Ui.FieldLabel(Lang.T("Dirección del servidor")));
            var serverBox = Ui.DarkTextBox(pl.Server);
            if (string.IsNullOrEmpty(pl.Server)) serverBox.Text = "http://";
            xtPanel.Children.Add(serverBox);
            xtPanel.Children.Add(Ui.FieldLabel(Lang.T("Usuario")));
            var userBox = Ui.DarkTextBox(pl.Username);
            xtPanel.Children.Add(userBox);
            xtPanel.Children.Add(Ui.FieldLabel(Lang.T("Contraseña")));
            var passBox = Ui.DarkPasswordBox(pl.Password);
            xtPanel.Children.Add(passBox);
            sp.Children.Add(xtPanel);

            System.Action renderKind = null;
            renderKind = () =>
            {
                typeRow.Children.Clear();
                typeRow.Children.Add(Ui.Chip(Lang.T("📄 Lista M3U"), kind == "m3u", () => { kind = "m3u"; renderKind(); }));
                typeRow.Children.Add(Ui.Chip("🔑 Xtream Codes", kind == "xtream", () => { kind = "xtream"; renderKind(); }));
                m3uPanel.Visibility = kind == "m3u" ? Visibility.Visible : Visibility.Collapsed;
                xtPanel.Visibility = kind == "xtream" ? Visibility.Visible : Visibility.Collapsed;
            };
            renderKind();

            sp.Children.Add(Ui.ActionButton(Lang.T("Guardar"), () =>
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    Nav.Toast(Lang.T("El nombre es obligatorio"));
                    return;
                }
                pl.Name = nameBox.Text.Trim();
                pl.Kind = kind;
                if (kind == "m3u")
                {
                    if (string.IsNullOrWhiteSpace(urlBox.Text)) { Nav.Toast(Lang.T("La URL es obligatoria")); return; }
                    pl.Url = urlBox.Text.Trim();
                    pl.EpgUrl = epgBox.Text.Trim();
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(serverBox.Text) || serverBox.Text.Trim() == "http://"
                        || string.IsNullOrWhiteSpace(userBox.Text) || string.IsNullOrEmpty(passBox.Password))
                    { Nav.Toast(Lang.T("Servidor, usuario y contraseña son obligatorios")); return; }
                    pl.Server = serverBox.Text.Trim();
                    pl.Username = userBox.Text.Trim();
                    pl.Password = passBox.Password;
                }
                if (isNew)
                {
                    if (PlaylistStore.Items.Count == 0) pl.IsActive = true;
                    PlaylistStore.Add(pl);
                }
                else PlaylistStore.Save();
                Nav.Toast(Lang.T("Lista guardada"));
                onDone();
            }));

            if (!isNew)
            {
                if (!pl.IsActive)
                {
                    sp.Children.Add(Ui.ActionButton(Lang.T("Marcar como activa"), () =>
                    {
                        PlaylistStore.SetActive(pl);
                        Nav.Toast(Lang.T("Lista activa: ") + pl.Name);
                        PlaylistLoader.RefreshInBackground(quiet: true);
                        onDone();
                    }));
                }
                bool confirm = false;
                Button delBtn = null;
                delBtn = Ui.ActionButton(Lang.T("Eliminar"), () =>
                {
                    if (!confirm)
                    {
                        confirm = true;
                        delBtn.Content = Lang.T("¿Eliminar? Toca de nuevo para confirmar");
                        return;
                    }
                    PlaylistStore.Remove(pl);
                    Nav.Toast(Lang.T("Lista eliminada"));
                    onDone();
                }, danger: true);
                sp.Children.Add(delBtn);
            }

            Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
    }
}
