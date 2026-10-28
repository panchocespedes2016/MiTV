using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using VlcPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace MiTV
{
    public class HomeView : ViewBase
    {
        StackPanel sp;
        Border searchBar;
        TextBox searchInput;
        string searchQ = "";

        public override void ToggleSearch() => Ui.ToggleSearchBar(searchBar, searchInput);

        public HomeView()
        {
            sp = new StackPanel { Margin = new Thickness(26, 4, 26, 18) };
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            searchBar = Ui.SearchBar(out searchInput, q => { searchQ = q; RenderHome(); });
            Grid.SetRow(searchBar, 0);
            root.Children.Add(searchBar);
            var scroll = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
            Content = root;
            RenderHome();
        }

        void RenderHome()
        {
            sp.Children.Clear();
            bool real = ContentStore.HasData;
            AddFavoritesRow(sp, searchQ);
            AddContinueWatching(sp, searchQ);
            AddFeaturedChannels(sp, real, searchQ);
            AddRecentMovies(sp, real, searchQ);
            AddRecentSeries(sp, real, searchQ);
            if (sp.Children.Count == 0 && !string.IsNullOrEmpty(searchQ))
                sp.Children.Add(Ui.Notice(Lang.T("Sin resultados para esta búsqueda.")));
        }

        static bool Matches(string q, string text) => string.IsNullOrEmpty(q) || Ui.Norm(text ?? "").Contains(q);

        // Favoritos: películas y series marcadas con ★, primero en Inicio.
        // Si no hay ninguno no se muestra la fila.
        void AddFavoritesRow(StackPanel sp, string q)
        {
            if (!ContentStore.HasData) return;
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 26) };
            foreach (var m in ContentStore.Movies.Where(m => Favorites.IsFavKey(m.StreamUrl)))
            {
                if (!Matches(q, m.Name + " " + m.Category)) continue;
                var mm = m;
                row.Children.Add(Ui.Card(mm.Name, mm.Category, "\U0001F3AC", null, -1,
                    () => Nav.Push(new MovieDetailView(mm)), mm.Logo,
                    fav: () => (Favorites.IsFavKey(mm.StreamUrl), () => { Favorites.ToggleKey(mm.StreamUrl); RenderHome(); }),
                    menu: () => CardMenus.MovieMenu(mm.Name, mm.Category, mm.StreamUrl, mm.Logo, RenderHome)));
            }
            foreach (var s in ContentStore.Series.Where(s => Favorites.IsFavKey("serie:" + (s.SerieId ?? s.Name))))
            {
                if (!Matches(q, s.Name + " " + s.Category)) continue;
                var ss = s;
                row.Children.Add(Ui.Card(ss.Name, ss.Category, "\U0001F39D", null, -1, () => OpenSerie(ss), ss.Cover,
                    fav: () => (Favorites.IsFavKey("serie:" + (ss.SerieId ?? ss.Name)), () => { Favorites.ToggleKey("serie:" + (ss.SerieId ?? ss.Name)); RenderHome(); }),
                    menu: () => CardMenus.SerieMenu(ss, RenderHome)));
            }
            if (row.Children.Count == 0) return;
            sp.Children.Add(Ui.SectionTitle("★ " + Lang.T("Tus favoritos")));
            sp.Children.Add(row);
        }

        // Continuar viendo: películas incompletas (las últimas que dejó a medias)
        void AddContinueWatching(StackPanel sp, string q)
        {
            var list = WatchHistory.ContinueWatching().Where(e => Matches(q, e.Title + " " + e.Sub)).ToList();
            if (list.Count == 0) return; // sin historial no se muestra la fila
            sp.Children.Add(Ui.SectionTitle(Lang.T("Continuar viendo")));
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 26) };
            foreach (var e in list)
            {
                string key = e.Key;
                int pct = e.DurationMs > 0 ? (int)(e.PositionMs * 100 / e.DurationMs) : 0;
                row.Children.Add(Ui.Card(e.Title, e.Sub, "\U0001F3AC", null, pct,
                    () => PlayerView.Open(e.Title, e.Sub, false, key, e.Logo, e.PositionMs), e.Logo,
                    fav: () => (Favorites.IsFavKey(key), () => { Favorites.ToggleKey(key); RenderHome(); }),
                    menu: () => CardMenus.MovieMenu(e.Title, e.Sub, key, e.Logo, RenderHome)));
            }
            sp.Children.Add(row);
        }

        // Canales destacados: los más sintonizados por él
        void AddFeaturedChannels(StackPanel sp, bool real, string q)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 26) };
            var top = WatchHistory.TopChannels(8).Where(e => Matches(q, e.Title + " " + e.Sub)).ToList();
            if (top.Count > 0)
            {
                foreach (var e in top)
                {
                    string key = e.Key;
                    row.Children.Add(Ui.Card(e.Title, e.Sub, "\U0001F4FA", Lang.T("EN VIVO"), -1,
                        () => PlayerView.Open(e.Title, e.Sub, true, key, e.Logo), e.Logo, true,
                        fav: () => (Favorites.IsFavKey(key), () => { Favorites.ToggleKey(key); RenderHome(); }),
                        menu: () => CardMenus.ChannelMenu(key, RenderHome)));
                }
            }
            else if (real)
            {
                foreach (var cc in ContentStore.Channels.Take(8))
                {
                    if (!Matches(q, cc.Name + " " + cc.Group)) continue;
                    row.Children.Add(Ui.Card(cc.Name, cc.Group, "\U0001F4FA", Lang.T("EN VIVO"), -1,
                        () => PlayerView.Open(cc.Name, cc.Group, true, cc.StreamUrl, cc.Logo), cc.Logo, true,
                        fav: () => (Favorites.IsFav(cc), () => { Favorites.Toggle(cc); RenderHome(); }),
                        menu: () => CardMenus.ChannelMenu(cc.StreamUrl, RenderHome)));
                }
            }
            else
            {
                foreach (var c in Data.Channels.Take(4))
                {
                    if (!Matches(q, c.Name + " " + c.Programme)) continue;
                    row.Children.Add(Ui.Card(c.Name, c.Programme, c.Icon, Lang.T("EN VIVO"), c.Progress,
                        () => PlayerView.Open(c.Name, c.Programme, true)));
                }
            }
            if (row.Children.Count == 0) return;
            sp.Children.Add(Ui.SectionTitle(Lang.T("Canales destacados")));
            sp.Children.Add(row);
        }

        // Películas: lo más reciente del catálogo
        void AddRecentMovies(StackPanel sp, bool real, string q)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 26) };
            if (real)
                foreach (var mm in ContentStore.Movies.OrderByDescending(m => m.Added).Take(8))
                {
                    if (!Matches(q, mm.Name + " " + mm.Category)) continue;
                    var mmm = mm;
                    row.Children.Add(Ui.Card(mmm.Name, mmm.Category, "\U0001F3AC", null, -1,
                        () => Nav.Push(new MovieDetailView(mmm)), mmm.Logo,
                        fav: () => (Favorites.IsFavKey(mmm.StreamUrl), () => { Favorites.ToggleKey(mmm.StreamUrl); RenderHome(); }),
                        menu: () => CardMenus.MovieMenu(mmm.Name, mmm.Category, mmm.StreamUrl, mmm.Logo, RenderHome)));
                }
            else
                foreach (var m in Data.Movies.Take(4))
                {
                    if (!Matches(q, m.Title)) continue;
                    row.Children.Add(Ui.Card(m.Title, m.Year + " \u2022 " + m.Dur, "\U0001F3AC", m.Tag, -1,
                        () => PlayerView.Open(m.Title, m.Year + " \u2022 " + m.Dur, false)));
                }
            if (row.Children.Count == 0) return;
            sp.Children.Add(Ui.SectionTitle(Lang.T("Pel\u00edculas populares")));
            sp.Children.Add(row);
        }

        // Series: lo más reciente del catálogo
        void AddRecentSeries(StackPanel sp, bool real, string q)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            if (real)
                foreach (var ss in ContentStore.Series.OrderByDescending(x => x.Added).Take(8))
                {
                    if (!Matches(q, ss.Name + " " + ss.Category)) continue;
                    var s2 = ss;
                    row.Children.Add(Ui.Card(ss.Name, ss.Category, "\U0001F39D", null, -1, () => OpenSerie(s2), ss.Cover,
                        fav: () => (Favorites.IsFavKey("serie:" + (s2.SerieId ?? s2.Name)), () => { Favorites.ToggleKey("serie:" + (s2.SerieId ?? s2.Name)); RenderHome(); }),
                        menu: () => CardMenus.SerieMenu(s2, RenderHome)));
                }
            else
                foreach (var s in Data.Shows)
                {
                    if (!Matches(q, s.Title)) continue;
                    row.Children.Add(Ui.Card(s.Title, s.Seasons, "\U0001F39D", null, -1,
                        () => PlayerView.Open(s.Title, s.Seasons, false)));
                }
            if (row.Children.Count == 0) return;
            sp.Children.Add(Ui.SectionTitle(Lang.T("Series")));
            sp.Children.Add(row);
        }

        public static void OpenSerie(SerieItem s)
        {
            if (!string.IsNullOrEmpty(s.StreamUrl))
                PlayerView.Open(s.Name, s.Category, false, s.StreamUrl, s.Cover);
            else
                Nav.Push(new SerieDetailView(s));
        }
    }

    // Menús de clic derecho en tarjetas y filas: favorito, reproducir y visto.
    // Se arman en el momento de abrirlos, con el estado actual.
    static class CardMenus
    {
        static ContextMenu Base()
            => new ContextMenu { Background = Ui.Panel, BorderBrush = Ui.Brd, Foreground = Ui.Text };

        public static ContextMenu MovieMenu(string name, string category, string url, string logo, Action refresh)
        {
            var cm = Base();
            bool fav = Favorites.IsFavKey(url);
            var miFav = new MenuItem { Header = (fav ? "☆ " : "★ ") + Lang.T(fav ? "Quitar de favoritos" : "Agregar a favoritos") };
            miFav.Click += (s, e) => { Favorites.ToggleKey(url); refresh?.Invoke(); };
            cm.Items.Add(miFav);
            long posMs = 0, durMs = 0;
            try
            {
                var he = WatchHistory.LastOpened(1000, x => x.Kind == "movie" && x.Key == url).FirstOrDefault();
                if (he != null) { posMs = he.PositionMs; durMs = he.DurationMs; }
            }
            catch { }
            bool hasProgress = durMs > 0 && posMs >= durMs * 0.03 && posMs < durMs * 0.95;
            var miPlay = new MenuItem { Header = "▶ " + Lang.T(hasProgress ? "Continuar" : "Reproducir") };
            miPlay.Click += (s, e) => PlayerView.Open(name, category, false, url, logo, hasProgress ? posMs : 0);
            cm.Items.Add(miPlay);
            if (!WatchHistory.IsWatched(url))
            {
                var miSeen = new MenuItem { Header = "✓ " + Lang.T("Marcar como visto") };
                miSeen.Click += (s, e) => { WatchHistory.SetWatched(url, name, category, logo, "movie", category); refresh?.Invoke(); };
                cm.Items.Add(miSeen);
            }
            return cm;
        }

        public static ContextMenu SerieMenu(SerieItem s, Action refresh)
        {
            var cm = Base();
            string key = "serie:" + (s.SerieId ?? s.Name);
            bool fav = Favorites.IsFavKey(key);
            var miFav = new MenuItem { Header = (fav ? "☆ " : "★ ") + Lang.T(fav ? "Quitar de favoritos" : "Agregar a favoritos") };
            miFav.Click += (s, e) => { Favorites.ToggleKey(key); refresh?.Invoke(); };
            cm.Items.Add(miFav);
            var miPlay = new MenuItem { Header = "▶ " + Lang.T("Reproducir") };
            miPlay.Click += (s2, e2) => PlaySerieAsync(s);
            cm.Items.Add(miPlay);
            return cm;
        }

        public static ContextMenu ChannelMenu(string url, Action refresh)
        {
            var cm = Base();
            bool fav = Favorites.IsFavKey(url);
            var miFav = new MenuItem { Header = (fav ? "☆ " : "★ ") + Lang.T(fav ? "Quitar de favoritos" : "Agregar a favoritos") };
            miFav.Click += (s, e) => { Favorites.ToggleKey(url); refresh?.Invoke(); };
            cm.Items.Add(miFav);
            return cm;
        }

        // Reproducir una serie: continúa el episodio a medias si lo hay,
        // si no el primero; si no cargan los episodios abre la ficha.
        static async void PlaySerieAsync(SerieItem s)
        {
            try
            {
                var eps = await PlaylistLoader.GetSeriesEpisodesAsync(s.SerieId);
                if (eps != null && eps.Count > 0)
                {
                    var urls = new HashSet<string>(eps.Select(x => x.Url));
                    var he = WatchHistory.LastOpened(2000, x => x.Kind == "episode" && urls.Contains(x.Key) && x.DurationMs > 0 && x.PositionMs >= x.DurationMs * 0.03 && x.PositionMs < x.DurationMs * 0.95).FirstOrDefault();
                    var ep = he != null ? eps.FirstOrDefault(x => x.Url == he.Key) : null;
                    long from = 0;
                    if (ep == null) ep = eps[0];
                    else from = he.PositionMs;
                    string title = string.IsNullOrEmpty(ep.Title) ? Lang.T("Episodio ") + ep.Num : ep.Title;
                    PlayerView.Open(title, "S" + Ui.Pad2(ep.Season) + " E" + Ui.Pad2(ep.Num), false, ep.Url, s.Cover, from, "episode", s.Category);
                    return;
                }
            }
            catch { }
            Nav.Push(new SerieDetailView(s));
        }
    }

    public class LiveTvView : ViewBase
    {
        StackPanel catsPanel, list;
        string curCat = "Todo";
        int renderToken;
        Border searchBar;
        TextBox searchInput;
        string searchQ = "";

        // Vista previa (panel derecho): mismo patrón que la Guía — video
        // muteado con overlays DENTRO del VideoView (airspace de WPF).
        VideoView previewVideo;
        Border logoBox, nextBox;
        TextBlock logoEmoji, liveBadge, previewPill, previewHint;
        TextBlock dChName, dChSub, dProgName, dTime, dDesc, nextTitle, nextTime;
        LibVLC previewVlc;
        VlcPlayer previewMp;
        LiveChannel selectedChannel;
        string selectedUrl = "";
        int selectedNumber;
        Border selectedRow;
        DispatcherTimer detailTimer, listTimer;
        ScrollViewer listScroll;
        double pendingScrollRestore = -1;
        DateTime lastFullOpen;

        public override void ToggleSearch() => Ui.ToggleSearchBar(searchBar, searchInput);

        public override void OnLeave()
        {
            try { detailTimer?.Stop(); } catch { }
            try { listTimer?.Stop(); } catch { }
            try
            {
                if (previewVideo != null) previewVideo.MediaPlayer = null;
                if (previewMp != null) { try { previewMp.Stop(); } catch { } previewMp.Dispose(); previewMp = null; }
                if (previewVlc != null) { previewVlc.Dispose(); previewVlc = null; }
            }
            catch { }
        }

        public LiveTvView()
        {
            var grid = Ui.TwoColumns(240);
            // La columna del preview es proporcional (0.55*): con 480 fijos la
            // lista quedaba sin ancho en ventanas no maximizadas y los nombres
            // solo se veían por la rendija de la marquesina.
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.55, GridUnitType.Star) });
            catsPanel = Ui.CategorySidebar(grid, 0, out _);

            // El título queda FUERA del scroll: fijo arriba, solo se mueve la lista
            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var listTitle = Ui.SectionTitle(Lang.T("Todos los canales"));
            listTitle.Margin = new Thickness(20, 14, 26, 12);
            Grid.SetRow(listTitle, 0);
            right.Children.Add(listTitle);
            list = new StackPanel { Margin = new Thickness(20, 0, 26, 18) };
            var listScroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            this.listScroll = listScroll;
            Grid.SetRow(listScroll, 1);
            right.Children.Add(listScroll);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            var previewPanel = BuildPreviewPanel();
            Grid.SetColumn(previewPanel, 2);
            grid.Children.Add(previewPanel);

            detailTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            detailTimer.Tick += (s, e) => { if (selectedChannel != null) UpdateDetailPanel(selectedChannel); };

            // La guía de las filas caduca (programa y % viejos si la app queda
            // abierta): refresca la lista cada 5 min sin tocar el preview
            // y restaurando la posición de scroll al terminar el dibujo.
            listTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            listTimer.Tick += (s, e) =>
            {
                if (ContentStore.Channels.Count == 0) return;
                pendingScrollRestore = listScroll.VerticalOffset;
                RenderList(curCat);
            };
            listTimer.Start();

            // Franja de búsqueda integrada encima de todo el ancho
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            searchBar = Ui.SearchBar(out searchInput, q => { searchQ = q; RenderList(curCat); });
            Grid.SetRow(searchBar, 0);
            root.Children.Add(searchBar);
            Grid.SetRow(grid, 1);
            root.Children.Add(grid);
            Content = root;
            string startCat = ViewState.Get("livetv");
            if (startCat != "Historial" && startCat != "Favoritos" && Array.IndexOf(Cats(), startCat) < 0) startCat = "Todo";
            RenderCats(startCat);
            RenderList(startCat);
        }

        // Panel derecho: video 16:9 + canal + ahora/siguiente.
        Border BuildPreviewPanel()
        {
            var panel = new Border
            {
                Background = Ui.Bg,
                BorderBrush = Ui.Brd,
                BorderThickness = new Thickness(1, 0, 0, 0),
                Padding = new Thickness(18, 14, 18, 18)
            };
            var sp = new StackPanel();

            var pv = new Border
            {
                Height = 250,
                CornerRadius = new CornerRadius(10),
                Background = Brushes.Black,
                BorderBrush = Ui.Brd,
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                Margin = new Thickness(0, 0, 0, 16)
            };
            // El alto sigue al ancho (16:9) porque la columna ya no es fija
            pv.SizeChanged += (s, e) => { if (e.WidthChanged) pv.Height = Math.Max(120, pv.ActualWidth * 9.0 / 16.0); };
            var pvg = new Grid();
            previewVideo = new VideoView
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            pvg.Children.Add(previewVideo);
            liveBadge = new TextBlock
            {
                Text = "● " + Lang.T("EN VIVO"),
                FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                Background = Ui.Red, Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10, 10, 0, 0), Visibility = Visibility.Collapsed
            };
            previewHint = new TextBlock
            {
                Text = Lang.T("Haz clic en un canal para ver la vista previa"),
                FontSize = 13, Foreground = Ui.Gray,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                Margin = new Thickness(30, 0, 30, 0)
            };
            previewPill = new TextBlock
            {
                Text = "🔇 " + Lang.T("Vista previa"),
                FontSize = 12, Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 10, 10), Visibility = Visibility.Collapsed
            };
            // Los overlays van DENTRO del VideoView (igual que en la Guía):
            // LibVLCSharp los muestra sobre el video; como hermanos, el video los tapa.
            var over = new Grid { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
            over.Children.Add(liveBadge);
            over.Children.Add(previewHint);
            over.Children.Add(previewPill);
            over.MouseLeftButtonDown += (s, e) => { if (selectedChannel != null) OpenFull(); };
            previewVideo.Content = over;
            pv.Child = pvg;
            sp.Children.Add(pv);

            // Encabezado del canal: logo + nombre + grupo
            var head = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            logoBox = new Border
            {
                Width = 52, Height = 52,
                CornerRadius = new CornerRadius(8),
                Background = Ui.Panel2,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            logoEmoji = new TextBlock { Text = "\U0001F4FA", FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            logoBox.Child = logoEmoji;
            head.Children.Add(logoBox);
            var headTx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            dChName = new TextBlock { FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Ui.Text, TextTrimming = TextTrimming.CharacterEllipsis };
            dChSub = new TextBlock { FontSize = 13, Foreground = Ui.Gray, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            headTx.Children.Add(dChName);
            headTx.Children.Add(dChSub);
            Grid.SetColumn(headTx, 1);
            head.Children.Add(headTx);
            sp.Children.Add(head);

            dProgName = new TextBlock { FontSize = 21, FontWeight = FontWeights.Bold, Foreground = Ui.Text, TextWrapping = TextWrapping.Wrap };
            sp.Children.Add(dProgName);
            dTime = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ui.B("#E8A13D"), Margin = new Thickness(0, 4, 0, 0) };
            sp.Children.Add(dTime);
            dDesc = new TextBlock { FontSize = 14, Foreground = Ui.Gray, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, MaxHeight = 120 };
            sp.Children.Add(dDesc);

            nextBox = new Border
            {
                Background = Ui.Panel,
                BorderBrush = Ui.Brd,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 16, 0, 0),
                Visibility = Visibility.Collapsed
            };
            var nextSp = new StackPanel();
            nextSp.Children.Add(new TextBlock { Text = Lang.T("A CONTINUACIÓN"), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Ui.Red });
            nextTitle = new TextBlock { FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Ui.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            nextSp.Children.Add(nextTitle);
            nextTime = new TextBlock { FontSize = 13, Foreground = Ui.Gray, Margin = new Thickness(0, 2, 0, 0) };
            nextSp.Children.Add(nextTime);
            nextBox.Child = nextSp;
            sp.Children.Add(nextBox);

            sp.Children.Add(new TextBlock
            {
                Text = Lang.T("Doble clic o Enter = ver en pantalla completa · clic en la vista previa también abre el canal"),
                FontSize = 12, Foreground = Ui.Gray, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 16, 0, 0)
            });

            var scroll = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            panel.Child = scroll;
            return panel;
        }

        // Programa actual y siguiente de un canal (título, progreso %, horario).
        static (string prog, int pct, string range) NowInfo(LiveChannel c)
        {
            var (now, _) = EpgStore.GetNowNext(c);
            if (now == null) return (Lang.T("Sin información de guía"), 0, "");
            int pct = 0;
            double total = (now.StopUtc - now.StartUtc).TotalMinutes;
            if (total > 0) pct = (int)Math.Round((DateTime.UtcNow - now.StartUtc).TotalMinutes / total * 100);
            pct = Math.Max(0, Math.Min(100, pct));
            string range = now.StartUtc.ToLocalTime().ToString("HH:mm") + " – " + now.StopUtc.ToLocalTime().ToString("HH:mm");
            return (now.Title, pct, range);
        }

        void SelectChannel(LiveChannel c, int number, Border rowB)
        {
            if (c == null || string.IsNullOrEmpty(c.StreamUrl)) return;
            // El 2º clic del doble clic no debe re-seleccionar tras abrir el reproductor
            if ((DateTime.Now - lastFullOpen).TotalMilliseconds < 600) return;
            selectedChannel = c;
            selectedUrl = c.StreamUrl;
            selectedNumber = number;
            if (selectedRow != null && selectedRow != rowB)
            {
                try { selectedRow.BorderBrush = Ui.Brd; } catch { }
            }
            selectedRow = rowB;
            if (rowB != null)
            {
                try { rowB.BorderBrush = Ui.Red; } catch { }
            }
            UpdateDetailPanel(c);
            PlayPreview(c);
            detailTimer.Start();
        }

        void UpdateDetailPanel(LiveChannel c)
        {
            logoBox.Background = Ui.Panel2;
            logoEmoji.Visibility = Visibility.Visible;
            if (!string.IsNullOrEmpty(c.Logo)) ImgCache.Load(logoBox, logoEmoji, c.Logo, 104);
            dChName.Text = (selectedNumber > 0 ? selectedNumber + " · " : "") + c.Name;
            string sub = c.Group ?? "";
            if (c.HasCatchup) sub += (sub.Length > 0 ? " · " : "") + "↺ " + Lang.T("Con repetición");
            dChSub.Text = sub;
            dChSub.Visibility = sub.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            var (now, next) = EpgStore.GetNowNext(c);
            if (now != null)
            {
                dProgName.Text = now.Title;
                int mins = Math.Max(1, (int)Math.Ceiling((now.StopUtc - DateTime.UtcNow).TotalMinutes));
                dTime.Text = now.StartUtc.ToLocalTime().ToString("HH:mm") + " – " + now.StopUtc.ToLocalTime().ToString("HH:mm") + " · " + Lang.T("Termina en") + " " + mins + " min";
                dTime.Visibility = Visibility.Visible;
                dDesc.Text = now.Desc ?? "";
                dDesc.Visibility = string.IsNullOrWhiteSpace(now.Desc) ? Visibility.Collapsed : Visibility.Visible;
            }
            else
            {
                dProgName.Text = Lang.T("Sin información de guía");
                dTime.Visibility = Visibility.Collapsed;
                dDesc.Visibility = Visibility.Collapsed;
            }
            if (next != null)
            {
                nextTitle.Text = next.Title;
                nextTime.Text = next.StartUtc.ToLocalTime().ToString("HH:mm") + " – " + next.StopUtc.ToLocalTime().ToString("HH:mm");
                nextBox.Visibility = Visibility.Visible;
            }
            else nextBox.Visibility = Visibility.Collapsed;
        }

        void PlayPreview(LiveChannel c)
        {
            if (string.IsNullOrEmpty(c.StreamUrl)) return;
            try
            {
                if (previewVlc == null)
                {
                    try { Core.Initialize(); } catch { }
                    previewVlc = new LibVLC("--no-video-title-show");
                    previewMp = new VlcPlayer(previewVlc);
                    previewMp.Mute = true;
                    previewVideo.MediaPlayer = previewMp;
                }
                else { try { previewMp.Stop(); } catch { } }
                var media = new Media(previewVlc, c.StreamUrl, FromType.FromLocation);
                media.AddOption(":network-caching=1000");
                previewMp.Play(media);
                media.Dispose();
                liveBadge.Visibility = Visibility.Visible;
                previewPill.Visibility = Visibility.Visible;
                previewHint.Visibility = Visibility.Collapsed;
            }
            catch
            {
                previewHint.Text = Lang.T("No se pudo reproducir este canal");
                previewHint.Visibility = Visibility.Visible;
                liveBadge.Visibility = Visibility.Collapsed;
                previewPill.Visibility = Visibility.Collapsed;
            }
        }

        void StopPreview()
        {
            try { if (previewMp != null) previewMp.Stop(); } catch { }
            liveBadge.Visibility = Visibility.Collapsed;
            previewPill.Visibility = Visibility.Collapsed;
            previewHint.Text = Lang.T("Haz clic en un canal para ver la vista previa");
            previewHint.Visibility = Visibility.Visible;
        }

        void OpenFull()
        {
            if ((DateTime.Now - lastFullOpen).TotalMilliseconds < 800) return;
            lastFullOpen = DateTime.Now;
            var c = selectedChannel;
            if (c == null || string.IsNullOrEmpty(c.StreamUrl)) return;
            StopPreview(); // que no siga sonando en el fondo
            PlayerView.Open(c.Name, c.Group, true, c.StreamUrl, c.Logo);
        }

        string[] Cats()
        {
            return new[] { "Todo" }.Concat(ContentStore.Channels
                .Select(c => c.Group)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct().OrderBy(g => g)).ToArray();
        }

        // Restaura la posición de scroll guardada por el refresco periódico,
        // solo cuando el dibujo terminó (si se restaura antes, el ScrollViewer
        // recorta el desplazamiento porque el contenido aún está incompleto).
        void RestoreScrollIfPending()
        {
            if (pendingScrollRestore < 0 || listScroll == null) return;
            double o = pendingScrollRestore;
            pendingScrollRestore = -1;
            try { listScroll.ScrollToVerticalOffset(o); } catch { }
        }

        void RenderCats(string sel)
        {
            pendingScrollRestore = -1; // navegar de categoría manda: el scroll vuelve arriba
            catsPanel.Children.Clear();
            int hn = WatchHistory.LastOpened(15, e => e.Kind == "channel").Count;
            catsPanel.Children.Add(Ui.CatButton(Lang.T("\U0001F558 Historial"), hn, sel == "Historial",
                () => { ViewState.Set("livetv", "Historial"); RenderCats("Historial"); RenderList("Historial"); }));
            int tvfn = ContentStore.Channels.Count(ch => Favorites.IsFav(ch));
            catsPanel.Children.Add(Ui.CatButton("\u2605 " + Lang.T("Favoritos"), tvfn, sel == "Favoritos",
                () => { ViewState.Set("livetv", "Favoritos"); RenderCats("Favoritos"); RenderList("Favoritos"); }));
            catsPanel.Children.Add(Ui.CatButton(Lang.T("Todo"), ContentStore.Channels.Count, sel == "Todo",
                () => { ViewState.Set("livetv", "Todo"); RenderCats("Todo"); RenderList("Todo"); }));
            foreach (var c in Cats().Skip(1))
            {
                string cc = c;
                int n = ContentStore.Channels.Count(ch => ch.Group == cc);
                catsPanel.Children.Add(Ui.CatButton(c, n, c == sel, () => { ViewState.Set("livetv", cc); RenderCats(cc); RenderList(cc); }));
            }
        }

        void RenderList(string cat)
        {
            curCat = cat;
            selectedRow = null;
            int token = ++renderToken;
            list.Children.Clear();
            bool searching = !string.IsNullOrEmpty(searchQ);
            if (cat == "Historial" && !searching)
            {
                var hist = WatchHistory.LastOpened(15, e => e.Kind == "channel");
                if (hist.Count == 0)
                    list.Children.Add(Ui.Notice(Lang.T("Sin historial todav\u00eda. Los canales que abras aparecer\u00e1n aqu\u00ed.")));
                foreach (var e in hist)
                {
                    var ee = e;
                    var ch = new LiveChannel { Name = ee.Title, Group = ee.Sub, Logo = ee.Logo, StreamUrl = ee.Key };
                    var (prog, pct, range) = NowInfo(ch);
                    var hrow = new Channel(ee.Title, prog, "\U0001F4FA", pct)
                    {
                        Logo = ee.Logo,
                        TimeRange = range
                    };
                    Border hrowB = null;
                    hrowB = Ui.ChannelRow(hrow,
                        () => SelectChannel(ch, 0, hrowB),
                        null, null,
                        isSelected: () => selectedUrl == ee.Key,
                        onOpen: OpenFull);
                    if (ee.Key == selectedUrl) selectedRow = hrowB;
                    hrowB.MouseLeftButtonDown += (s, e2) => { if (e2.ClickCount == 2) OpenFull(); };
                    list.Children.Add(hrowB);
                }
                RestoreScrollIfPending();
                return;
            }
            if (ContentStore.Channels.Count == 0)
            {
                if (PlaylistStore.Active == null)
                    list.Children.Add(Ui.Notice(Lang.T("Sin lista activa. Ve a Ajustes → Listas IPTV, agrega una lista y sincroniza.")));
                foreach (var c in Data.Channels)
                {
                    if (searching && !Ui.Norm(c.Name).Contains(searchQ)) continue;
                    list.Children.Add(Ui.ChannelRow(c, () => PlayerView.Open(c.Name, c.Programme, true)));
                }
                RestoreScrollIfPending();
                return;
            }
            // La búsqueda cubre toda la sección, no solo la categoría abierta
            var items = searching
                ? ContentStore.Channels.Where(ch => Ui.Norm(ch.Name).Contains(searchQ)).ToList()
                : ContentStore.Channels.Where(ch => cat == "Todo" || cat == "Favoritos" || ch.Group == cat).ToList();
            if (LiveSettings.HideNoEpg)
                items = items.Where(ch => EpgStore.GetPrograms(ch) != null).ToList();
            List<LiveChannel> favItems = new(), restItems = items;
            if (cat == "Favoritos")
            {
                restItems = items.Where(ch => Favorites.IsFav(ch)).ToList();
            }
            else if (LiveSettings.FavoritesFirst)
            {
                favItems = items.Where(ch => Favorites.IsFav(ch)).ToList();
                restItems = items.Where(ch => !Favorites.IsFav(ch)).ToList();
            }
            if (items.Count == 0) list.Children.Add(Ui.Notice(Lang.T("Sin canales en esta categoría.")));
            int rowNum = 0;
            Func<LiveChannel, Border> makeRow = (LiveChannel c) =>
            {
                var cc = c;
                int num = ++rowNum;
                var (prog, pct, range) = NowInfo(cc);
                var row = new Channel(cc.Name, prog, "\U0001F4FA", pct) { Logo = cc.Logo, Number = num, Tag = cc.HasCatchup ? "catchup" : "", TimeRange = range };
                Border rowB = null;
                rowB = Ui.ChannelRow(row,
                    () => SelectChannel(cc, num, rowB),
                    () => (Favorites.IsFav(cc), new Action(() => { Favorites.Toggle(cc); })),
                    () => CardMenus.ChannelMenu(cc.StreamUrl, () => RenderList(curCat)),
                    isSelected: () => selectedUrl == cc.StreamUrl,
                    onOpen: OpenFull);
                if (cc.StreamUrl == selectedUrl) selectedRow = rowB;
                rowB.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) OpenFull(); };
                return rowB;
            };
            if (favItems.Count > 0)
            {
                list.Children.Add(Ui.GroupHeader("\u2605 " + Lang.T("Favoritos")));
                foreach (var c in favItems) list.Children.Add(makeRow(c));
            }
            if (cat == "Todo" && LiveSettings.GroupByCategory)
            {
                var groups = restItems.GroupBy(ch => string.IsNullOrEmpty(ch.Group) ? Lang.T("Otros") : ch.Group).OrderBy(g => g.Key);
                foreach (var g in groups)
                {
                    list.Children.Add(Ui.GroupHeader(g.Key));
                    foreach (var c in g) list.Children.Add(makeRow(c));
                }
                RestoreScrollIfPending();
            }
            else
            {
                int i = 0;
                var flat = restItems;
                Action addChunk = null;
                addChunk = () =>
                {
                    if (token != renderToken) return;
                    int end = Math.Min(i + 200, flat.Count);
                    for (; i < end; i++) list.Children.Add(makeRow(flat[i]));
                    if (i < flat.Count)
                        Dispatcher.BeginInvoke(addChunk, DispatcherPriority.Background);
                    else
                        RestoreScrollIfPending();
                };
                addChunk();
            }
        }
    }

    public class SportsView : ViewBase
    {
        StackPanel catsPanel, list;
        string curCat = "Todo";
        Border searchBar;
        TextBox searchInput;
        string searchQ = "";

        public override void ToggleSearch() => Ui.ToggleSearchBar(searchBar, searchInput);

        public SportsView()
        {
            var grid = Ui.TwoColumns(240);
            catsPanel = Ui.CategorySidebar(grid, 0, out _);

            var right = new StackPanel { Margin = new Thickness(20, 14, 26, 18) };
            right.Children.Add(Ui.SectionTitle(Lang.T("Deportes en vivo")));
            list = new StackPanel();
            right.Children.Add(list);
            var listScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(listScroll, 1);
            grid.Children.Add(listScroll);

            // Franja de búsqueda integrada encima de todo el ancho
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            searchBar = Ui.SearchBar(out searchInput, q => { searchQ = q; RenderList(curCat); });
            Grid.SetRow(searchBar, 0);
            root.Children.Add(searchBar);
            Grid.SetRow(grid, 1);
            root.Children.Add(grid);
            Content = root;
            string startCat = ViewState.Get("sports");
            if (startCat != "Historial" && startCat != "Favoritos" && Array.IndexOf(Cats(), startCat) < 0) startCat = "Todo";
            RenderCats(startCat);
            RenderList(startCat);
        }

        // Categorías de canales en vivo cuyo nombre contiene "sport" o "deporte"
        string[] Cats()
        {
            var groups = ContentStore.Channels
                .Select(c => c.Group)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct()
                .Where(g => g.IndexOf("sport", StringComparison.OrdinalIgnoreCase) >= 0
                         || g.IndexOf("deporte", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(g => g);
            return new[] { "Todo" }.Concat(groups).ToArray();
        }

        void RenderCats(string sel)
        {
            catsPanel.Children.Clear();
            int hn = WatchHistory.LastOpened(15, e => e.Kind == "channel" && WatchHistory.IsSportsGroup(e.Sub)).Count;
            catsPanel.Children.Add(Ui.CatButton(Lang.T("\U0001F558 Historial"), hn, sel == "Historial",
                () => { ViewState.Set("sports", "Historial"); RenderCats("Historial"); RenderList("Historial"); }));
            int spfn = SportChannels().Count(ch => Favorites.IsFav(ch));
            catsPanel.Children.Add(Ui.CatButton("\u2605 " + Lang.T("Favoritos"), spfn, sel == "Favoritos",
                () => { ViewState.Set("sports", "Favoritos"); RenderCats("Favoritos"); RenderList("Favoritos"); }));
            catsPanel.Children.Add(Ui.CatButton(Lang.T("Todo"), SportChannels().Count, sel == "Todo",
                () => { ViewState.Set("sports", "Todo"); RenderCats("Todo"); RenderList("Todo"); }));
            foreach (var c in Cats().Skip(1))
            {
                string cc = c;
                int n = ContentStore.Channels.Count(ch => ch.Group == cc);
                catsPanel.Children.Add(Ui.CatButton(c, n, c == sel, () => { ViewState.Set("sports", cc); RenderCats(cc); RenderList(cc); }));
            }
        }

        List<LiveChannel> SportChannels()
        {
            var cats = new HashSet<string>(Cats().Skip(1));
            return ContentStore.Channels.Where(ch => cats.Contains(ch.Group)).ToList();
        }

        void RenderList(string cat)
        {
            curCat = cat;
            list.Children.Clear();
            bool searching = !string.IsNullOrEmpty(searchQ);
            if (cat == "Historial" && !searching)
            {
                var hist = WatchHistory.LastOpened(15, e => e.Kind == "channel" && WatchHistory.IsSportsGroup(e.Sub));
                if (hist.Count == 0)
                    list.Children.Add(Ui.Notice(Lang.T("Sin historial todav\u00eda. Los canales de deportes que abras aparecer\u00e1n aqu\u00ed.")));
                foreach (var e in hist)
                {
                    var ee = e;
                    var hrow = new Channel(ee.Title, string.IsNullOrEmpty(ee.Sub) ? Lang.T("En vivo") : ee.Sub, "\u26BD", 0)
                    {
                        Logo = ee.Logo
                    };
                    list.Children.Add(Ui.ChannelRow(hrow,
                        () => PlayerView.Open(ee.Title, ee.Sub, true, ee.Key, ee.Logo)));
                }
                return;
            }
            if (ContentStore.Channels.Count > 0)
            {
                // La búsqueda cubre toda la sección, no solo la categoría abierta
                var all = searching ? SportChannels().Where(ch => Ui.Norm(ch.Name).Contains(searchQ)).ToList()
                    : cat == "Todo" ? SportChannels()
                    : cat == "Favoritos" ? SportChannels().Where(ch => Favorites.IsFav(ch)).ToList()
                    : ContentStore.Channels.Where(ch => ch.Group == cat).ToList();
                if (all.Count == 0)
                    list.Children.Add(Ui.Notice(searching ? Lang.T("Sin resultados para esta búsqueda.") : Lang.T("No hay categorías de deportes en tu lista.")));
                int sNum = 0;
                foreach (var ch in all)
                {
                    var c = ch;
                    var row = new Channel(c.Name, string.IsNullOrEmpty(c.Group) ? Lang.T("En vivo") : c.Group, "⚽", 0)
                    {
                        Logo = c.Logo,
                        Number = ++sNum
                    };
                    list.Children.Add(Ui.ChannelRow(row,
                        () => PlayerView.Open(c.Name, c.Group, true, c.StreamUrl, c.Logo),
                        () => (Favorites.IsFav(c), new Action(() => { Favorites.Toggle(c); })),
                        () => CardMenus.ChannelMenu(c.StreamUrl, () => RenderList(curCat))));
                }
            }
            else
            {
                foreach (var c in Data.Sports)
                {
                    if (searching && !Ui.Norm(c.Name).Contains(searchQ)) continue;
                    list.Children.Add(Ui.ChannelRow(c, () => PlayerView.Open(c.Name, c.Programme, true)));
                }
            }
        }
    }

    // Búsquedas recientes (portable, por sección)
    public static class RecentSearches
    {
        static readonly string FilePath = System.IO.Path.Combine(AppContext.BaseDirectory, "mitv-recent-search.json");
        static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> data = new();
        static RecentSearches() { Load(); }
        static void Load()
        {
            try
            {
                if (System.IO.File.Exists(FilePath))
                    data = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>>(System.IO.File.ReadAllText(FilePath)) ?? new();
            }
            catch { }
        }
        static void Save()
        {
            try { System.IO.File.WriteAllText(FilePath, System.Text.Json.JsonSerializer.Serialize(data)); } catch { }
        }
        public static System.Collections.Generic.List<string> Get(string section)
        {
            if (data.TryGetValue(section, out var l)) return l;
            return new System.Collections.Generic.List<string>();
        }
        public static void Add(string section, string query)
        {
            query = (query ?? "").Trim();
            if (query.Length < 2) return;
            if (!data.TryGetValue(section, out var l)) { l = new System.Collections.Generic.List<string>(); data[section] = l; }
            l.Remove(query);
            l.Insert(0, query);
            while (l.Count > 8) l.RemoveAt(l.Count - 1);
            Save();
        }
    }

    // Caché de metadatos por película (StreamId -> director, género, reparto) - persistente en disco
    public static class VodMetaCache
    {
        public class Meta { public string Director = ""; public string Genre = ""; public string Cast = ""; }
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Meta> cache = new();
        static readonly string FilePath = System.IO.Path.Combine(AppContext.BaseDirectory, "mitv-vod-meta.json");
        // Meta usa CAMPOS, no propiedades: sin IncludeFields=true se serializaba como "{}"
        static readonly System.Text.Json.JsonSerializerOptions jsonOpts = new() { IncludeFields = true };
        static bool loaded = false;
        static readonly object loadLock = new();
        static void EnsureLoaded()
        {
            if (loaded) return;
            lock (loadLock)
            {
                if (loaded) return;
                try
                {
                    if (System.IO.File.Exists(FilePath))
                    {
                        var d = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, Meta>>(System.IO.File.ReadAllText(FilePath), jsonOpts);
                        if (d != null)
                            foreach (var kv in d)
                                if (kv.Value != null && (!string.IsNullOrEmpty(kv.Value.Director) || !string.IsNullOrEmpty(kv.Value.Genre) || !string.IsNullOrEmpty(kv.Value.Cast)))
                                    cache[kv.Key] = kv.Value;
                    }
                }
                catch { }
                loaded = true;
            }
        }
        static void Save()
        {
            try
            {
                var dict = new System.Collections.Generic.Dictionary<string, Meta>(cache);
                System.IO.File.WriteAllText(FilePath, System.Text.Json.JsonSerializer.Serialize(dict, jsonOpts));
            }
            catch { }
        }
        public static Meta Get(string streamId)
        {
            if (string.IsNullOrEmpty(streamId)) return null;
            EnsureLoaded();
            cache.TryGetValue(streamId, out var m);
            return m;
        }
        public static void Set(string streamId, string director, string genre, string cast)
        {
            if (string.IsNullOrEmpty(streamId)) return;
            EnsureLoaded();
            cache[streamId] = new Meta { Director = director ?? "", Genre = genre ?? "", Cast = cast ?? "" };
        }
        public static void Persist() { EnsureLoaded(); Save(); }
        public static int Count { get { EnsureLoaded(); return cache.Count; } }

        // ID efectivo de una película (StreamId o extraído de la URL /movie/u/p/{id}.ext)
        public static string EffectiveSid(VodMovie m)
        {
            if (m == null) return "";
            if (!string.IsNullOrEmpty(m.StreamId)) return m.StreamId;
            try
            {
                string u = m.StreamUrl.Split('?')[0];
                int slash = u.LastIndexOf('/');
                int dot = u.LastIndexOf('.');
                if (slash >= 0 && dot > slash) return u.Substring(slash + 1, dot - slash - 1);
            }
            catch { }
            return "";
        }

        // Rellena en segundo plano los metadatos que falten (para el filtro por artista
        // y para la búsqueda global). onDone solo se llama si se añadieron datos nuevos.
        static bool filling = false;
        public static void EnsureAllAsync(System.Action onDone = null)
        {
            if (filling) return;
            filling = true;
            System.Threading.Tasks.Task.Run(async () =>
            {
                int added = 0;
                try
                {
                    var movies = ContentStore.Movies.ToList();
                    var pending = movies.Where(m => Get(EffectiveSid(m)) == null).ToList();
                    var sem = new System.Threading.SemaphoreSlim(8);
                    var tasks = pending.Select(async m =>
                    {
                        await sem.WaitAsync();
                        try
                        {
                            string sid = EffectiveSid(m);
                            if (string.IsNullOrEmpty(sid)) return;
                            var info = await PlaylistLoader.GetVodInfoAsync(sid);
                            if (info != null && (!string.IsNullOrEmpty(info.Director) || !string.IsNullOrEmpty(info.Genre) || !string.IsNullOrEmpty(info.Cast)))
                            {
                                Set(sid, info.Director, info.Genre, info.Cast);
                                System.Threading.Interlocked.Increment(ref added);
                            }
                        }
                        catch { }
                        finally { sem.Release(); }
                    });
                    await System.Threading.Tasks.Task.WhenAll(tasks);
                    if (added > 0) Persist();
                }
                finally { filling = false; }
                if (added > 0 && onDone != null) { try { onDone(); } catch { } }
            });
        }
    }

    public class MoviesView : ViewBase
    {
        StackPanel catsPanel;
        WrapPanel grid;
        string curCat = "Todo";
        int renderToken;
        Border searchBar;
        TextBox searchInput;
        string searchQ = "";
        StackPanel activeFiltersPanel;
        WrapPanel recentPanel;
        TextBlock metaInfoBar;
        System.Collections.Generic.HashSet<string> activeSimpleFilters = new();
        string filterYear = "", filterGenre = "", filterDirector = "", filterActor = "";
        bool actorFilterOn = false, directorFilterOn = false, genreFilterOn = false;
        // Obtiene el ID efectivo (StreamId o extraído de la URL)
        static string EffectiveSid(VodMovie m) => VodMetaCache.EffectiveSid(m);
        public MoviesView()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // searchBar
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // activeFiltersPanel
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // recentPanel
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // metaInfoBar
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // filterOptsPanel
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // outer
            // Barra de búsqueda a TODO el ancho (no solo la categoría abierta)
            searchBar = new Border
            {
                Background = Ui.Panel, BorderBrush = Ui.Brd, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(12),
                Margin = new Thickness(20, 14, 190, 0), Visibility = Visibility.Collapsed
            };
            var sbDock = new DockPanel();
            var addFilterBtn = new Button
            {
                Content = "+ " + Lang.T("Filtro"), MinWidth = 110, Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(12, 6, 12, 6),
                Background = Ui.B("#2a4a6b"), Foreground = Brushes.White, BorderBrush = Ui.B("#3a6a9b"),
                BorderThickness = new Thickness(1), FontSize = 14, FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            // Panel de opciones de filtro (botones navegables con control remoto)
            var filterOptsPanel = new WrapPanel { Margin = new Thickness(20, 8, 190, 0), Visibility = Visibility.Collapsed };
            string[] filterOpts = { Lang.T("Solo favoritos"), Lang.T("No vistas"), Lang.T("Año"), Lang.T("Género"), Lang.T("Director"), Lang.T("Artista") };
            Action[] filterActions = { AddFavFilter, AddUnseenFilter, AddYearFilter, AddGenreFilter, AddDirectorFilter, AddActorFilter };
            for (int fi = 0; fi < filterOpts.Length; fi++)
            {
                int idx = fi;
                var btn = new Button { Content = filterOpts[fi], FontSize = 13, Margin = new Thickness(0, 0, 8, 0),
                    Padding = new Thickness(12, 6, 12, 6), Background = Ui.B("#2a4a6b"), Foreground = Brushes.White,
                    BorderBrush = Ui.B("#3a6a9b"), BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand,
                    Focusable = true };
                btn.Click += (s, e) => { filterActions[idx](); filterOptsPanel.Visibility = Visibility.Collapsed; };
                // Resaltar al enfocar (para control remoto)
                btn.GotFocus += (s, e) => btn.Background = Ui.B("#3a6a9b");
                btn.LostFocus += (s, e) => btn.Background = Ui.B("#2a4a6b");
                filterOptsPanel.Children.Add(btn);
            }
            addFilterBtn.Click += (s, e) => {
                filterOptsPanel.Visibility = filterOptsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                if (filterOptsPanel.Visibility == Visibility.Visible && filterOptsPanel.Children.Count > 0)
                    (filterOptsPanel.Children[0] as Button)?.Focus();
            };
            DockPanel.SetDock(addFilterBtn, Dock.Right);
            sbDock.Children.Add(addFilterBtn);
            sbDock.Children.Add(new TextBlock { Text = "\U0001F50D", FontSize = 16, Foreground = Ui.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            searchInput = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Ui.Text, CaretBrush = Ui.Text, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
            searchInput.TextChanged += (s, e) => { searchQ = Ui.Norm(searchInput.Text ?? ""); UpdateRecentVisibility();
                if (actorFilterOn) filterActor = searchQ;
                if (directorFilterOn) filterDirector = searchQ;
                if (genreFilterOn) filterGenre = searchQ;
                if (searchQ.Length >= 2 && (actorFilterOn || directorFilterOn || genreFilterOn)) EnsureMetaForFilter();
                RenderMovies(curCat); };
            searchInput.GotFocus += (s, e) => UpdateRecentVisibility();
            searchInput.LostFocus += (s, e) =>
            {
                // Guardar si hay texto (con pequeño delay para permitir clic en chip)
                string t = (searchInput.Text ?? "").Trim();
                if (t.Length >= 2) RecentSearches.Add("movies", t);
            };
            // Guardar también con Enter
            searchInput.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    string t = (searchInput.Text ?? "").Trim();
                    if (t.Length >= 2) { RecentSearches.Add("movies", t); RenderRecent(); }
                    searchInput.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
                }
            };
            sbDock.Children.Add(searchInput);
            searchBar.Child = sbDock;
            Grid.SetRow(searchBar, 0);
            root.Children.Add(searchBar);
            // Panel de filtros activos (debajo de la barra)
            activeFiltersPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 8, 190, 0), Visibility = Visibility.Collapsed };
            Grid.SetRow(activeFiltersPanel, 1);
            root.Children.Add(activeFiltersPanel);
            // Búsquedas recientes
            recentPanel = new WrapPanel { Margin = new Thickness(20, 8, 190, 0), Visibility = Visibility.Collapsed };
            Grid.SetRow(recentPanel, 2);
            root.Children.Add(recentPanel);
            metaInfoBar = new TextBlock { FontSize = 13, Foreground = Ui.Gray, Margin = new Thickness(20, 6, 190, 0), Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(metaInfoBar, 3);
            root.Children.Add(metaInfoBar);
            // Columnas: categorías | contenido
            var outer = Ui.TwoColumns(240);
            catsPanel = Ui.CategorySidebar(outer, 0, out _);
            grid = new WrapPanel { Margin = new Thickness(20, 14, 26, 18) };
            var scroll = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            // Rueda del ratón para el panel de películas
            scroll.PreviewMouseWheel += (s, e) => { scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta); e.Handled = true; };
            Grid.SetColumn(scroll, 1);
            outer.Children.Add(scroll);
            Grid.SetRow(filterOptsPanel, 4);
            root.Children.Add(filterOptsPanel);
            Grid.SetRow(outer, 5);
            root.Children.Add(outer);

            Content = root;
            string startCat = ViewState.Get("movies");
            if (startCat != "Historial" && startCat != "Favoritos" && Array.IndexOf(Cats(), startCat) < 0) startCat = "Todo";
            RenderCats(startCat);
            RenderMovies(startCat);
        }

        void UpdateRecentVisibility()
        {
            bool show = searchBar.Visibility == Visibility.Visible
                && string.IsNullOrWhiteSpace(searchInput.Text)
                && RecentSearches.Get("movies").Count > 0;
            recentPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) RenderRecent();
        }

        void RenderRecent()
        {
            recentPanel.Children.Clear();
            recentPanel.Children.Add(new TextBlock { Text = Lang.T("Recientes:"), FontSize = 13, Foreground = Ui.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 4) });
            foreach (var r in RecentSearches.Get("movies"))
            {
                string rr = r;
                var chip = Ui.Chip(rr, false, () => { searchInput.Text = rr; searchInput.Focus(); });
                chip.Margin = new Thickness(0, 0, 8, 4);
                recentPanel.Children.Add(chip);
            }
        }

        public override void ToggleSearch()
        {
            bool show = searchBar.Visibility != Visibility.Visible;
            searchBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            activeFiltersPanel.Visibility = show && activeFiltersPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (show) searchInput.Focus();
            else
            {
                searchQ = ""; searchInput.Text = "";
                recentPanel.Visibility = Visibility.Collapsed;
                activeSimpleFilters.Clear();
                filterYear = ""; filterGenre = ""; filterDirector = ""; filterActor = "";
                actorFilterOn = false; directorFilterOn = false; genreFilterOn = false;
                activeFiltersPanel.Children.Clear();
                activeFiltersPanel.Visibility = Visibility.Collapsed;
                RenderMovies(curCat);
            }
        }

        Border MakeFilterChip(string label, Action onRemove)
        {
            var b = new Border { Background = Ui.B("#1d3a5c"), CornerRadius = new CornerRadius(16), Padding = new Thickness(12, 6, 8, 6), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            var x = new Button { Content = "✕", FontSize = 11, Style = Ui.TransparentButton(), Foreground = Brushes.White, Padding = new Thickness(6, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            x.Click += (s, e) => onRemove();
            sp.Children.Add(x);
            b.Child = sp;
            return b;
        }

        TextBox MakeFilterTextBox(Action<string> onText)
        {
            var tb = new TextBox
            {
                MinWidth = 120, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = Ui.Panel2, Foreground = Ui.Text, BorderBrush = Ui.Brd, BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 4, 8, 4), FontSize = 13
            };
            tb.TextChanged += (s, e) => onText(Ui.Norm(tb.Text ?? ""));
            return tb;
        }

        void AddFavFilter()
        {
            if (!activeSimpleFilters.Add("fav")) return;
            activeFiltersPanel.Visibility = Visibility.Visible;
            Border chip = null;
            chip = MakeFilterChip(Lang.T("★ Favoritos"), () => { activeSimpleFilters.Remove("fav"); activeFiltersPanel.Children.Remove(chip); if (activeFiltersPanel.Children.Count == 0) activeFiltersPanel.Visibility = Visibility.Collapsed; RenderMovies(curCat); });
            activeFiltersPanel.Children.Add(chip);
            RenderMovies(curCat);
        }

        void AddUnseenFilter()
        {
            if (!activeSimpleFilters.Add("unseen")) return;
            activeFiltersPanel.Visibility = Visibility.Visible;
            Border chip = null;
            chip = MakeFilterChip(Lang.T("No vistas"), () => { activeSimpleFilters.Remove("unseen"); activeFiltersPanel.Children.Remove(chip); if (activeFiltersPanel.Children.Count == 0) activeFiltersPanel.Visibility = Visibility.Collapsed; RenderMovies(curCat); });
            activeFiltersPanel.Children.Add(chip);
            RenderMovies(curCat);
        }

        void AddYearFilter()
        {
            activeFiltersPanel.Visibility = Visibility.Visible;
            var years = ContentStore.Movies.Where(m => !string.IsNullOrEmpty(m.Year)).Select(m => m.Year).Distinct().OrderByDescending(y => y).ToList();
            if (years.Count == 0) { Nav.Toast(Lang.T("Sin años disponibles")); return; }
            var cb = new ComboBox { MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            cb.Items.Add(Lang.T("Todas"));
            foreach (var y in years) cb.Items.Add(y);
            cb.SelectedIndex = 0;
            cb.SelectionChanged += (s, e) => { filterYear = cb.SelectedIndex == 0 ? "" : (cb.SelectedItem as string ?? ""); RenderMovies(curCat); };
            Border chip = null;
            chip = MakeFilterChip(Lang.T("Año") + ": ", () => { filterYear = ""; activeFiltersPanel.Children.Remove(chip); if (activeFiltersPanel.Children.Count == 0) activeFiltersPanel.Visibility = Visibility.Collapsed; RenderMovies(curCat); });
            (chip.Child as StackPanel)?.Children.Insert(1, cb);
            activeFiltersPanel.Children.Add(chip);
        }

        void AddGenreFilter()
        {
            activeFiltersPanel.Visibility = Visibility.Visible;
            Border chip = null;
            genreFilterOn = true;
            chip = MakeFilterChip(Lang.T("Género") + ": " + Lang.T("(usa la barra de búsqueda)"), () => { filterGenre = ""; genreFilterOn = false; activeFiltersPanel.Children.Remove(chip); if (activeFiltersPanel.Children.Count == 0) activeFiltersPanel.Visibility = Visibility.Collapsed; RenderMovies(curCat); });
            activeFiltersPanel.Children.Add(chip);
            filterGenre = searchQ;
            if (filterGenre.Length >= 2) EnsureMetaForFilter();
            RenderMovies(curCat);
            Nav.Toast(Lang.T("Escribe el género en la barra de búsqueda"));
        }

        void AddDirectorFilter()
        {
            activeFiltersPanel.Visibility = Visibility.Visible;
            Border chip = null;
            directorFilterOn = true;
            chip = MakeFilterChip(Lang.T("Director") + ": " + Lang.T("(usa la barra de búsqueda)"), () => { filterDirector = ""; directorFilterOn = false; activeFiltersPanel.Children.Remove(chip); if (activeFiltersPanel.Children.Count == 0) activeFiltersPanel.Visibility = Visibility.Collapsed; RenderMovies(curCat); });
            activeFiltersPanel.Children.Add(chip);
            filterDirector = searchQ;
            if (filterDirector.Length >= 2) EnsureMetaForFilter();
            RenderMovies(curCat);
            Nav.Toast(Lang.T("Escribe el director en la barra de búsqueda"));
        }

        void AddActorFilter()
        {
            activeFiltersPanel.Visibility = Visibility.Visible;
            actorFilterOn = true;
            Border chip = null;
            chip = MakeFilterChip(Lang.T("Artista") + ": " + Lang.T("(usa la barra de búsqueda)"), () => { filterActor = ""; actorFilterOn = false; activeFiltersPanel.Children.Remove(chip); if (activeFiltersPanel.Children.Count == 0) activeFiltersPanel.Visibility = Visibility.Collapsed; RenderMovies(curCat); });
            activeFiltersPanel.Children.Add(chip);
            filterActor = searchQ;
            if (filterActor.Length >= 2) EnsureMetaForFilter();
            RenderMovies(curCat);
            Nav.Toast(Lang.T("Escribe el artista en la barra de búsqueda"));
        }

        bool metaLoading = false;
        int metaTotal = 0, metaDone = 0;
        DateTime metaStartTime;
        void EnsureMetaForFilter()
        {
            Nav.Toast("Iniciando búsqueda…");
            // Resetear flag siempre (evitar que se quede trabado)
            metaLoading = false;
            metaLoading = true;
            metaStartTime = DateTime.Now;
            if (metaInfoBar != null) { metaInfoBar.Text = "Iniciando…"; metaInfoBar.Visibility = Visibility.Visible; }
            Dispatcher.Invoke(() => RenderMovies(curCat));
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var movies = ContentStore.Movies.ToList();
                    metaTotal = movies.Count;
                    metaDone = movies.Count(m => VodMetaCache.Get(EffectiveSid(m)) != null);
                    Dispatcher.Invoke(() => RenderMovies(curCat));
                    var pending = movies.Where(m => VodMetaCache.Get(EffectiveSid(m)) == null).ToList();
                    var sem = new System.Threading.SemaphoreSlim(8);
                    int done = 0;
                    var tasks = pending.Select(async m =>
                    {
                        await sem.WaitAsync();
                        try
                        {
                            // Usar StreamId, con respaldo sacándolo de la URL (igual que la ficha)
                            string sid = m.StreamId;
                            if (string.IsNullOrEmpty(sid))
                            {
                                try
                                {
                                    string u = m.StreamUrl.Split('?')[0];
                                    int slash = u.LastIndexOf('/');
                                    int dot = u.LastIndexOf('.');
                                    if (slash >= 0 && dot > slash) sid = u.Substring(slash + 1, dot - slash - 1);
                                }
                                catch { }
                            }
                            var info = await PlaylistLoader.GetVodInfoAsync(sid);
                            // Solo guardar si hay datos reales (no cachear fallos)
                            // Usar sid (el ID real) como clave, no m.StreamId que puede estar vacío
                            string cacheKey = !string.IsNullOrEmpty(m.StreamId) ? m.StreamId : sid;
                            if (info != null && !string.IsNullOrEmpty(cacheKey) && (!string.IsNullOrEmpty(info.Director) || !string.IsNullOrEmpty(info.Genre) || !string.IsNullOrEmpty(info.Cast)))
                                VodMetaCache.Set(cacheKey, info.Director, info.Genre, info.Cast);
                        }
                        catch { }
                        finally
                        {
                            sem.Release();
                            int d = System.Threading.Interlocked.Increment(ref done);
                            int md = System.Threading.Interlocked.Increment(ref metaDone);
                            if (d % 50 == 0) Dispatcher.Invoke(() =>
                            {
                                // Actualizar solo la barra de progreso (rápido)
                                if (metaInfoBar != null)
                                {
                                    string prog = metaTotal > 0 ? $" ({md}/{metaTotal})" : "";
                                    metaInfoBar.Text = Lang.T("Buscando en la información de las películas…") + prog;
                                    metaInfoBar.Visibility = Visibility.Visible;
                                }
                                // Re-renderizar la lista cada 200 para mostrar resultados
                                if (d % 200 == 0) RenderMovies(curCat);
                            });
                        }
                    });
                    await System.Threading.Tasks.Task.WhenAll(tasks);
                    VodMetaCache.Persist();
                }
                finally
                {
                    metaLoading = false;
                    Dispatcher.Invoke(() => { RenderMovies(curCat); Nav.Toast(Lang.T("Búsqueda lista")); });
                }
            });
        }

        string[] Cats()
        {
            if (ContentStore.Movies.Count == 0) return Data.MovieCats;
            return new[] { "Todo" }.Concat(
                ContentStore.Movies.Select(m => m.Category)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct().OrderBy(c => c)).ToArray();
        }

        void RenderCats(string sel)
        {
            catsPanel.Children.Clear();
            bool real = ContentStore.Movies.Count > 0;
            int hn = WatchHistory.LastOpened(15, e => e.Kind == "movie").Count;
            catsPanel.Children.Add(Ui.CatButton(Lang.T("\U0001F558 Historial"), hn, sel == "Historial",
                () => { ViewState.Set("movies", "Historial"); RenderCats("Historial"); RenderMovies("Historial"); }));
            int fn = real ? ContentStore.Movies.Count(m => Favorites.IsFavKey(m.StreamUrl)) : 0;
            catsPanel.Children.Add(Ui.CatButton("\u2605 " + Lang.T("Favoritos"), fn, sel == "Favoritos",
                () => { ViewState.Set("movies", "Favoritos"); RenderCats("Favoritos"); RenderMovies("Favoritos"); }));
            catsPanel.Children.Add(Ui.CatButton(Lang.T("Todo"), real ? ContentStore.Movies.Count : Data.Movies.Length, sel == "Todo",
                () => { ViewState.Set("movies", "Todo"); RenderCats("Todo"); RenderMovies("Todo"); }));
            foreach (var c in Cats().Skip(1))
            {
                string cc = c;
                int n = real ? ContentStore.Movies.Count(m => m.Category == cc) : Data.Movies.Count(m => m.Cat == cc);
                catsPanel.Children.Add(Ui.CatButton(c, n, c == sel, () => { ViewState.Set("movies", cc); RenderCats(cc); RenderMovies(cc); }));
            }
        }

        void RenderMovies(string cat)
        {
            curCat = cat;
            int token = ++renderToken;
            grid.Children.Clear();
            if (cat == "Historial")
            {
                var hist = WatchHistory.LastOpened(15, e => e.Kind == "movie");
                if (hist.Count == 0)
                    grid.Children.Add(Ui.Notice(Lang.T("Sin historial todav\u00eda. Las pel\u00edculas que abras aparecer\u00e1n aqu\u00ed.")));
                foreach (var e in hist)
                {
                    var ee = e;
                    int pct = ee.DurationMs > 0 ? (int)(ee.PositionMs * 100 / ee.DurationMs) : 0;
                    var hcard = Ui.Card(ee.Title, ee.Sub, "\U0001F3AC", null, pct,
                        () => MovieDetailView.OpenMovieByKey(ee.Key), ee.Logo,
                        fav: () => (Favorites.IsFavKey(ee.Key), () => { Favorites.ToggleKey(ee.Key); RenderMovies(curCat); }),
                        menu: () => CardMenus.MovieMenu(ee.Title, ee.Sub, ee.Key, ee.Logo, () => RenderMovies(curCat)));
                    hcard.Margin = new Thickness(0, 0, 14, 14);
                    grid.Children.Add(hcard);
                }
                return;
            }
            if (ContentStore.Movies.Count == 0)
            {
                foreach (var m in Data.Movies)
                {
                    if (cat != "Todo" && m.Cat != cat) continue;
                    var card = Ui.Card(m.Title, m.Year + " • " + m.Dur, "🎬", m.Tag, -1, () => PlayerView.Open(m.Title, m.Year + " • " + m.Dur, false));
                    card.Margin = new Thickness(0, 0, 14, 14);
                    grid.Children.Add(card);
                }
                return;
            }
            bool searching = !string.IsNullOrEmpty(searchQ) || activeSimpleFilters.Count > 0
                || !string.IsNullOrEmpty(filterYear) || !string.IsNullOrEmpty(filterGenre)
                || !string.IsNullOrEmpty(filterDirector) || !string.IsNullOrEmpty(filterActor);
            var items = searching
                ? ContentStore.Movies.ToList() // la búsqueda cubre todo, no solo la categoría abierta
                : ContentStore.Movies.Where(m => cat == "Todo" || (cat == "Favoritos" ? Favorites.IsFavKey(m.StreamUrl) : m.Category == cat)).ToList();
            // Con un meta-filtro activo (artista/director/género) la barra define ESE valor,
            // no el título: no aplicar el filtro por nombre o se anulan los resultados.
            bool metaFilterActive = !string.IsNullOrEmpty(filterDirector) || !string.IsNullOrEmpty(filterGenre) || !string.IsNullOrEmpty(filterActor);
            if (!string.IsNullOrEmpty(searchQ) && !metaFilterActive)
                items = items.Where(m => Ui.Norm(m.Name).Contains(searchQ)).ToList();
            if (activeSimpleFilters.Contains("fav"))
                items = items.Where(m => Favorites.IsFavKey(m.StreamUrl)).ToList();
            if (activeSimpleFilters.Contains("unseen"))
                items = items.Where(m => !WatchHistory.IsWatched(m.StreamUrl)).ToList();
            if (!string.IsNullOrEmpty(filterYear))
                items = items.Where(m => m.Year == filterYear).ToList();
            if (!string.IsNullOrEmpty(filterGenre))
                items = items.Where(m => Ui.Norm(VodMetaCache.Get(EffectiveSid(m))?.Genre).Contains(Ui.Norm(filterGenre))).ToList();
            if (!string.IsNullOrEmpty(filterDirector))
                items = items.Where(m => Ui.Norm(VodMetaCache.Get(EffectiveSid(m))?.Director).Contains(filterDirector)).ToList();
            if (!string.IsNullOrEmpty(filterActor))
                items = items.Where(m => Ui.Norm(VodMetaCache.Get(EffectiveSid(m))?.Cast).Contains(filterActor)).ToList();
            // Mostrar progreso en la barra compacta
            if (metaFilterActive && metaLoading)
            {
                string prog = metaTotal > 0 ? $" ({metaDone}/{metaTotal})" : "";
                metaInfoBar.Text = Lang.T("Buscando en la información de las películas…") + prog
                    + (items.Count > 0 ? $" — {items.Count} " + Lang.T("encontradas") : "");
                metaInfoBar.Visibility = Visibility.Visible;
            }
            else metaInfoBar.Visibility = Visibility.Collapsed;
            int limit = VodSettings.ResultLimit;
            int shown = 0;
            Func<VodMovie, Border> makeCard = (VodMovie mm) =>
            {
                string badge = (VodSettings.MarkWatched && WatchHistory.IsWatched(mm.StreamUrl)) ? "\u2713" : null;
                var card = Ui.Card(mm.Name, mm.Category, "🎬", badge, -1, () => Nav.Push(new MovieDetailView(mm)), mm.Logo,
                    fav: () => (Favorites.IsFavKey(mm.StreamUrl), () => { Favorites.ToggleKey(mm.StreamUrl); RenderMovies(curCat); }),
                    menu: () => CardMenus.MovieMenu(mm.Name, mm.Category, mm.StreamUrl, mm.Logo, () => RenderMovies(curCat)));
                card.Margin = new Thickness(0, 0, 14, 14);
                return card;
            };
            Action addBatch = null;
            addBatch = () =>
            {
                if (token != renderToken) return;
                for (int k = grid.Children.Count - 1; k >= 0; k--)
                    if (grid.Children[k] is Button bb && bb.Tag as string == "more") grid.Children.RemoveAt(k);
                int batch = limit == 0 ? 400 : limit;
                int end = Math.Min(shown + batch, items.Count);
                for (; shown < end; shown++) grid.Children.Add(makeCard(items[shown]));
                if (shown < items.Count)
                {
                    if (limit > 0)
                    {
                        var moreBtn = new Button { Content = Lang.T("Mostrar más") + $" ({items.Count - shown})", Tag = "more", Style = Ui.RoundedButtonStyle(Ui.Blue), Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(24, 12, 24, 12) };
                        moreBtn.Click += (s, e) => addBatch();
                        grid.Children.Add(moreBtn);
                    }
                    else Dispatcher.BeginInvoke(addBatch, DispatcherPriority.Background);
                }
            };
            addBatch();
        }
    }

    public class ShowsView : ViewBase
    {
        StackPanel catsPanel;
        WrapPanel grid;
        string curCat = "Todo";
        int renderToken;
        Border searchBar;
        TextBox searchInput;
        string searchQ = "";

        public override void ToggleSearch() => Ui.ToggleSearchBar(searchBar, searchInput);

        public ShowsView()
        {
            var outer = Ui.TwoColumns(240);
            catsPanel = Ui.CategorySidebar(outer, 0, out _);

            grid = new WrapPanel { Margin = new Thickness(20, 14, 26, 18) };
            var scroll = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(scroll, 1);
            outer.Children.Add(scroll);

            // Franja de búsqueda integrada encima de todo el ancho
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            searchBar = Ui.SearchBar(out searchInput, q => { searchQ = q; RenderShows(curCat); });
            Grid.SetRow(searchBar, 0);
            root.Children.Add(searchBar);
            Grid.SetRow(outer, 1);
            root.Children.Add(outer);
            Content = root;
            string startCat = ViewState.Get("series");
            if (startCat != "Historial" && startCat != "Favoritos" && Array.IndexOf(Cats(), startCat) < 0) startCat = "Todo";
            RenderCats(startCat);
            RenderShows(startCat);
        }

        string[] Cats()
        {
            if (ContentStore.Series.Count == 0) return new[] { "Todo" };
            return new[] { "Todo" }.Concat(
                ContentStore.Series.Select(s => s.Category)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct().OrderBy(c => c)).ToArray();
        }

        void RenderCats(string sel)
        {
            catsPanel.Children.Clear();
            bool real = ContentStore.Series.Count > 0;
            int hn = WatchHistory.LastOpened(15, e => e.Kind == "episode").Count;
            catsPanel.Children.Add(Ui.CatButton(Lang.T("\U0001F558 Historial"), hn, sel == "Historial",
                () => { RenderCats("Historial"); RenderShows("Historial"); }));
            int sfn = real ? ContentStore.Series.Count(s => Favorites.IsFavKey("serie:" + (s.SerieId ?? s.Name))) : 0;
            catsPanel.Children.Add(Ui.CatButton("\u2605 " + Lang.T("Favoritos"), sfn, sel == "Favoritos",
                () => { ViewState.Set("series", "Favoritos"); RenderCats("Favoritos"); RenderShows("Favoritos"); }));
            catsPanel.Children.Add(Ui.CatButton(Lang.T("Todo"), real ? ContentStore.Series.Count : Data.Shows.Length, sel == "Todo",
                () => { RenderCats("Todo"); RenderShows("Todo"); }));
            foreach (var c in Cats().Skip(1))
            {
                string cc = c;
                int n = ContentStore.Series.Count(s => s.Category == cc);
                catsPanel.Children.Add(Ui.CatButton(c, n, c == sel, () => { RenderCats(cc); RenderShows(cc); }));
            }
        }

        void RenderShows(string cat)
        {
            curCat = cat;
            int token = ++renderToken;
            grid.Children.Clear();
            bool searching = !string.IsNullOrEmpty(searchQ);
            if (cat == "Historial" && !searching)
            {
                var hist = WatchHistory.LastOpened(15, e => e.Kind == "episode");
                if (hist.Count == 0)
                    grid.Children.Add(Ui.Notice(Lang.T("Sin historial todav\u00eda. Los episodios que abras aparecer\u00e1n aqu\u00ed.")));
                foreach (var e in hist)
                {
                    var ee = e;
                    var hcard = Ui.Card(ee.Title, ee.Sub, "\U0001F39D", null, -1,
                        () => PlayerView.Open(ee.Title, ee.Sub, false, ee.Key, ee.Logo, ee.PositionMs, "episode"), ee.Logo);
                    hcard.Margin = new Thickness(0, 0, 14, 14);
                    grid.Children.Add(hcard);
                }
                return;
            }
            if (ContentStore.Series.Count == 0)
            {
                if (PlaylistStore.Active == null)
                    grid.Children.Add(Ui.Notice(Lang.T("Sin lista activa. Ve a Ajustes → Listas IPTV, agrega una lista y sincroniza.")));
                foreach (var s in Data.Shows)
                {
                    if (searching && !Ui.Norm(s.Title).Contains(searchQ)) continue;
                    var card = Ui.Card(s.Title, s.Seasons, "🎭", null, -1, () => PlayerView.Open(s.Title, s.Seasons, false));
                    card.Margin = new Thickness(0, 0, 14, 14);
                    grid.Children.Add(card);
                }
                return;
            }
            // La búsqueda cubre toda la sección, no solo la categoría abierta
            var items = searching
                ? ContentStore.Series.Where(s => Ui.Norm(s.Name).Contains(searchQ)).ToList()
                : ContentStore.Series.Where(s => cat == "Todo" || (cat == "Favoritos" ? Favorites.IsFavKey("serie:" + (s.SerieId ?? s.Name)) : s.Category == cat)).ToList();
            if (items.Count == 0) grid.Children.Add(Ui.Notice(searching ? Lang.T("Sin resultados para esta búsqueda.") : Lang.T("Sin series en esta categoría.")));
            int limit = VodSettings.ResultLimit;
            int shown = 0;
            Action addBatch = null;
            addBatch = () =>
            {
                if (token != renderToken) return;
                for (int k = grid.Children.Count - 1; k >= 0; k--)
                    if (grid.Children[k] is Button bb && bb.Tag as string == "more") grid.Children.RemoveAt(k);
                int batch = limit == 0 ? 400 : limit;
                int end = Math.Min(shown + batch, items.Count);
                for (; shown < end; shown++)
                {
                    var ss = items[shown];
                    var card = Ui.Card(ss.Name, ss.Category, "🎭", null, -1, () => HomeView.OpenSerie(ss), ss.Cover,
                        fav: () => (Favorites.IsFavKey("serie:" + (ss.SerieId ?? ss.Name)), () => { Favorites.ToggleKey("serie:" + (ss.SerieId ?? ss.Name)); RenderShows(curCat); }),
                        menu: () => CardMenus.SerieMenu(ss, () => RenderShows(curCat)));
                    card.Margin = new Thickness(0, 0, 14, 14);
                    grid.Children.Add(card);
                }
                if (shown < items.Count)
                {
                    if (limit > 0)
                    {
                        var moreBtn = new Button { Content = Lang.T("Mostrar más") + $" ({items.Count - shown})", Tag = "more", Style = Ui.RoundedButtonStyle(Ui.Blue), Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(24, 12, 24, 12) };
                        moreBtn.Click += (s, e) => addBatch();
                        grid.Children.Add(moreBtn);
                    }
                    else Dispatcher.BeginInvoke(addBatch, DispatcherPriority.Background);
                }
            };
            addBatch();
        }
    }

    // Detalle de serie Xtream: temporadas y episodios
    public class SerieDetailView : ViewBase
    {
        StackPanel chips, list;
        List<PlaylistLoader.EpisodeInfo> eps = new List<PlaylistLoader.EpisodeInfo>();
        SerieItem serie;
        Button contBtn;
        PlaylistLoader.EpisodeInfo contEp;
        long contPos;
        TextBlock serieDetLine, seriePlot, serieCast;

        public SerieDetailView(SerieItem s)
        {
            serie = s;
            var sp = new StackPanel { Margin = new Thickness(26, 14, 26, 18) };
            sp.Children.Add(Ui.BackHeader(s.Name, () => Nav.Back()));

            // Cabecera con póster
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var poster = new Border { Width = 160, Height = 240, CornerRadius = new CornerRadius(12), Background = Ui.Panel2, BorderBrush = Ui.Brd, BorderThickness = new Thickness(1) };
            var pGrid = new Grid();
            var emoji = new TextBlock { Text = "\U0001F39D", FontSize = 48, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Gray };
            pGrid.Children.Add(emoji);
            poster.Child = pGrid;
            if (!string.IsNullOrEmpty(s.Cover)) ImgCache.Load(poster, emoji, s.Cover, 320);
            Grid.SetColumn(poster, 0);
            grid.Children.Add(poster);
            var info = new StackPanel { Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            var stitle = new StackPanel { Orientation = Orientation.Horizontal };
            stitle.Children.Add(new TextBlock { Text = s.Name, FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Ui.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            string skey = "serie:" + (s.SerieId ?? s.Name);
            var sfav = new Button { Content = Favorites.IsFavKey(skey) ? "\u2605" : "\u2606", FontSize = 26, Foreground = Ui.B("#FFD500"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(4), Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Focusable = true, ToolTip = Lang.T("Favorito") };
            sfav.Click += (a, b) => { Favorites.ToggleKey(skey); sfav.Content = Favorites.IsFavKey(skey) ? "\u2605" : "\u2606"; };
            stitle.Children.Add(sfav);
            info.Children.Add(stitle);
            if (!string.IsNullOrEmpty(s.Category))
                info.Children.Add(new TextBlock { Text = s.Category, FontSize = 14, Foreground = Ui.Gray, Margin = new Thickness(0, 6, 0, 0) });
            serieDetLine = new TextBlock { FontSize = 14, Foreground = Ui.Text, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            seriePlot = new TextBlock { FontSize = 14, Foreground = Ui.Text, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, MaxHeight = 120, TextTrimming = TextTrimming.CharacterEllipsis };
            serieCast = new TextBlock { FontSize = 13, Foreground = Ui.Gray, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, MaxHeight = 60, TextTrimming = TextTrimming.CharacterEllipsis };
            info.Children.Add(serieDetLine);
            info.Children.Add(seriePlot);
            info.Children.Add(serieCast);
            LoadSerieInfo(s);
            contBtn = new Button { Content = Lang.T("Continuar"), Style = Ui.RoundedButtonStyle(Ui.Red), Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(28, 12, 28, 12), FontSize = 16, Cursor = System.Windows.Input.Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
            contBtn.Click += (a, b) => { if (contEp != null) PlayEp(contEp, contPos); };
            info.Children.Add(contBtn);
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);
            sp.Children.Add(grid);

            chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            sp.Children.Add(chips);
            list = new StackPanel();
            list.Children.Add(Ui.Notice(Lang.T("Cargando episodios…")));
            sp.Children.Add(list);
            Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Load(s);
        }

        async void Load(SerieItem s)
        {
            try
            {
                eps = await PlaylistLoader.GetSeriesEpisodesAsync(s.SerieId);
            }
            catch { eps = new List<PlaylistLoader.EpisodeInfo>(); }
            if (eps.Count == 0)
            {
                list.Children.Clear();
                list.Children.Add(Ui.Notice(Lang.T("No se pudieron cargar los episodios.")));
                return;
            }
            var seasons = eps.Select(e => e.Season).Distinct().OrderBy(x => x).ToArray();
            RenderChips(seasons[0], seasons);
            RenderList(seasons[0]);
            SetupContinue();
        }

        void RenderChips(string sel, string[] seasons)
        {
            chips.Children.Clear();
            var lbl = new TextBlock { Text = Lang.T("Temporada:"), FontSize = 15, Foreground = Ui.Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            chips.Children.Add(lbl);
            var cb = new ComboBox
            {
                MinWidth = 120,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            foreach (var sn in seasons)
                cb.Items.Add("S" + Ui.Pad2(sn));
            cb.SelectedItem = "S" + Ui.Pad2(sel);
            cb.SelectionChanged += (s, e) =>
            {
                if (cb.SelectedItem is string item)
                {
                    string num = item.Substring(1).TrimStart('0');
                    if (num == "") num = "0";
                    string found = seasons.FirstOrDefault(x => x == num || Ui.Pad2(x) == item.Substring(1)) ?? sel;
                    RenderChips(found, seasons);
                    RenderList(found);
                }
            };
            chips.Children.Add(cb);
            var snVlc = new Button { Content = "VLC", FontSize = 13, FontWeight = FontWeights.SemiBold, Style = Ui.RoundedButtonStyle(Ui.Panel2), Padding = new Thickness(16, 8, 16, 8), Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White, ToolTip = Lang.T("Temporada en VLC") };
            snVlc.Click += (s, e) =>
            {
                var urls = eps.Where(x => x.Season == sel).OrderBy(x => x.Num).Select(x => x.Url);
                if (!ExternalVlc.PlayMultiple(urls)) Nav.Toast(Lang.T("VLC no encontrado"));
            };
            chips.Children.Add(snVlc);
        }

        void RenderList(string season)
        {
            list.Children.Clear();
            foreach (var e in eps.Where(x => x.Season == season).OrderBy(x => x.Num))
            {
                var ep = e;
                string title = string.IsNullOrEmpty(ep.Title) ? Lang.T("Episodio ") + ep.Num : ep.Title;
                // Estado de visto / progreso
                long posMs = 0, durMs = 0;
                try
                {
                    var he = WatchHistory.LastOpened(2000, x => x.Kind == "episode" && x.Key == ep.Url).FirstOrDefault();
                    if (he != null) { posMs = he.PositionMs; durMs = he.DurationMs; }
                }
                catch { }
                bool watched = VodSettings.MarkWatched && durMs > 0 && posMs >= durMs * 0.95;
                bool hasProg = durMs > 0 && posMs >= durMs * 0.03 && !watched;
                string sub = "S" + Ui.Pad2(ep.Season) + " E" + Ui.Pad2(ep.Num);
                if (watched) sub += "  \u2713";
                else if (hasProg)
                {
                    var ts = TimeSpan.FromMilliseconds(posMs);
                    string t = ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}" : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
                    sub += "  \u23F5 " + t;
                }
                var b = new Border
                {
                    Background = Ui.Panel,
                    BorderBrush = Ui.Brd,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(18, 16, 18, 16),
                    Margin = new Thickness(0, 0, 0, 14),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                var dock = new DockPanel { LastChildFill = false };
                var badge = new Border
                {
                    Background = watched ? Ui.B("#1d5c38") : Ui.Panel2,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 14, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "E" + Ui.Pad2(ep.Num), FontSize = 14, FontWeight = FontWeights.Bold, Foreground = watched ? Ui.B("#3ddc84") : Ui.Text }
                };
                DockPanel.SetDock(badge, Dock.Left);
                dock.Children.Add(badge);
                // Miniatura del episodio
                if (!string.IsNullOrEmpty(ep.Image))
                {
                    var thumbBox = new Border
                    {
                        Width = 128, Height = 72,
                        CornerRadius = new CornerRadius(8),
                        Background = Ui.Panel2,
                        Margin = new Thickness(0, 0, 14, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var thPh = new TextBlock { Text = "\U0001F39E", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Gray };
                    thumbBox.Child = thPh;
                    ImgCache.Load(thumbBox, thPh, ep.Image, 192);
                    DockPanel.SetDock(thumbBox, Dock.Left);
                    dock.Children.Add(thumbBox);
                }
                var txts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                txts.Children.Add(Ui.MarqueeWrap(new TextBlock { Text = title, FontSize = 15, Foreground = Ui.Text, ToolTip = title }));
                string sub2 = sub;
                if (!string.IsNullOrEmpty(ep.Duration)) sub2 += (sub2 == "" ? "" : "  \u00B7 ") + ep.Duration;
                txts.Children.Add(new TextBlock { Text = sub2, FontSize = 12, Foreground = Ui.Gray });
                if (!string.IsNullOrEmpty(ep.Plot))
                    txts.Children.Add(new TextBlock { Text = ep.Plot, FontSize = 13, Foreground = Ui.Gray, TextWrapping = TextWrapping.Wrap, MaxHeight = 66, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0) });
                dock.Children.Add(txts);
                // Botón descargar
                var dlBtn = new Button { Content = "\u2B07", FontSize = 16, Style = Ui.RoundedButtonStyle(Ui.Panel2), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(12, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = Lang.T("Descargar") };
                dlBtn.Click += (s2, e2) => { e2.Handled = true; MovieDetailView.DownloadEpisode(ep, serie); };
                DockPanel.SetDock(dlBtn, Dock.Right);
                dock.Children.Add(dlBtn);
                var epVlc = new Button { Content = "VLC", FontSize = 14, FontWeight = FontWeights.SemiBold, Style = Ui.RoundedButtonStyle(Ui.Panel2), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(10, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White, ToolTip = Lang.T("Abrir en VLC") };
                var epUrl = ep.Url;
                epVlc.Click += (s2, e2) => { e2.Handled = true; if (!ExternalVlc.Play(epUrl)) Nav.Toast(Lang.T("VLC no encontrado")); };
                DockPanel.SetDock(epVlc, Dock.Right);
                dock.Children.Add(epVlc);
                b.Child = dock;
                b.MouseLeftButtonUp += (s2, e2) => PlayEp(ep, hasProg ? posMs : 0);
                b.MouseEnter += (s2, e2) => b.BorderBrush = Ui.Red;
                b.MouseLeave += (s2, e2) => b.BorderBrush = Ui.Brd;
                list.Children.Add(b);
            }
        }

        void PlayEp(PlaylistLoader.EpisodeInfo ep, long fromMs)
        {
            string title = string.IsNullOrEmpty(ep.Title) ? Lang.T("Episodio ") + ep.Num : ep.Title;
            PlayerView.Open(title, "S" + Ui.Pad2(ep.Season) + " E" + Ui.Pad2(ep.Num), false, ep.Url, serie?.Cover, fromMs, "episode", serie?.Category);
        }

        async void LoadSerieInfo(SerieItem s)
        {
            var si = await PlaylistLoader.GetSeriesInfoAsync(s.SerieId);
            try
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(si.Genre)) parts.Add(si.Genre);
                    if (!string.IsNullOrEmpty(si.Rating)) parts.Add("\u2605 " + si.Rating);
                    if (!string.IsNullOrEmpty(si.ReleaseDate)) parts.Add(si.ReleaseDate);
                    if (serieDetLine != null) serieDetLine.Text = string.Join(" \u00B7 ", parts);
                    if (seriePlot != null && !string.IsNullOrEmpty(si.Plot)) seriePlot.Text = si.Plot;
                    string ct = "";
                    if (!string.IsNullOrEmpty(si.Cast)) ct = Lang.T("Reparto: ") + si.Cast;
                    if (!string.IsNullOrEmpty(si.Director)) ct += (ct == "" ? "" : "\n") + Lang.T("Director: ") + si.Director;
                    if (serieCast != null) serieCast.Text = ct;
                });
            }
            catch { }
        }

        void SetupContinue()
        {
            try
            {
                var urls = new HashSet<string>(eps.Select(x => x.Url));
                var he = WatchHistory.LastOpened(2000, x => x.Kind == "episode" && urls.Contains(x.Key) && x.DurationMs > 0 && x.PositionMs >= x.DurationMs * 0.03 && x.PositionMs < x.DurationMs * 0.95).FirstOrDefault();
                if (he != null)
                {
                    contEp = eps.FirstOrDefault(x => x.Url == he.Key);
                    contPos = he.PositionMs;
                    if (contEp != null && contBtn != null)
                    {
                        string t = string.IsNullOrEmpty(contEp.Title) ? Lang.T("Episodio ") + contEp.Num : contEp.Title;
                        contBtn.Content = "\u25B6 " + Lang.T("Continuar") + " S" + Ui.Pad2(contEp.Season) + " E" + Ui.Pad2(contEp.Num);
                        contBtn.Visibility = Visibility.Visible;
                    }
                }
            }
            catch { }
        }
    }

    // Repetición (catchup): canales con tv_archive y sus programas pasados
    public class CatchupView : ViewBase
    {
        StackPanel catsPanel, progList;
        TextBlock chTitle;
        LiveChannel selCh;
        Border searchBar;
        TextBox searchInput;
        string searchQ = "";

        public override void ToggleSearch() => Ui.ToggleSearchBar(searchBar, searchInput);

        public CatchupView()
        {
            var outer = Ui.TwoColumns(280);
            catsPanel = Ui.CategorySidebar(outer, 0, out _);
            var right = new StackPanel { Margin = new Thickness(20, 14, 26, 18) };
            chTitle = new TextBlock { Text = Lang.T("Selecciona un canal"), FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Ui.Text, Margin = new Thickness(0, 0, 0, 12) };
            right.Children.Add(chTitle);
            progList = new StackPanel();
            var scroll = new ScrollViewer { Content = progList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            right.Children.Add(scroll);
            Grid.SetColumn(right, 1);
            outer.Children.Add(right);

            // Franja de búsqueda integrada encima de todo el ancho
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            searchBar = Ui.SearchBar(out searchInput, q => { searchQ = q; RenderChannels(); });
            Grid.SetRow(searchBar, 0);
            root.Children.Add(searchBar);
            Grid.SetRow(outer, 1);
            root.Children.Add(outer);
            Content = root;
            RenderChannels();
        }

        void RenderChannels()
        {
            catsPanel.Children.Clear();
            var chans = ContentStore.Channels.Where(c => c.HasCatchup && (string.IsNullOrEmpty(searchQ) || Ui.Norm(c.Name).Contains(searchQ))).OrderBy(c => c.Name).ToList();
            if (chans.Count == 0)
            {
                catsPanel.Children.Add(Ui.Notice(string.IsNullOrEmpty(searchQ) ? Lang.T("Ningún canal tiene repetición disponible.") : Lang.T("Sin resultados para esta búsqueda.")));
                return;
            }
            int n = 0;
            foreach (var c in chans)
            {
                var cc = c;
                n++;
                var b = new Border
                {
                    Background = Ui.Panel, BorderBrush = Ui.Brd, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 8), Cursor = System.Windows.Input.Cursors.Hand
                };
                var sp = new Grid();
                sp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                sp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                sp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var numTb = new TextBlock { Text = n.ToString(), FontSize = 13, Foreground = Ui.Gray, Width = 24, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(numTb, 0);
                sp.Children.Add(numTb);
                var logoBox = new Border { Width = 40, Height = 26, CornerRadius = new CornerRadius(4), Background = Brushes.Black, BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                var ph = new TextBlock { Text = "\U0001F4FA", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                logoBox.Child = ph;
                if (!string.IsNullOrEmpty(cc.Logo)) ImgCache.Load(logoBox, ph, cc.Logo, 80);
                Grid.SetColumn(logoBox, 1);
                sp.Children.Add(logoBox);
                var ccNameHost = Ui.MarqueeWrap(new TextBlock { Text = cc.Name, FontSize = 14, Foreground = Ui.Text, VerticalAlignment = VerticalAlignment.Center, ToolTip = cc.Name });
                Grid.SetColumn(ccNameHost, 2);
                sp.Children.Add(ccNameHost);
                b.Child = sp;
                b.MouseLeftButtonUp += (s, e) => SelectChannel(cc);
                catsPanel.Children.Add(b);
            }
        }

        void SelectChannel(LiveChannel c)
        {
            selCh = c;
            chTitle.Text = c.Name;
            progList.Children.Clear();
            var progs = EpgStore.GetPrograms(c);
            if (progs == null || progs.Count == 0)
            {
                progList.Children.Add(Ui.Notice(Lang.T("Sin guía para este canal.")));
                return;
            }
            var now = DateTime.UtcNow;
            int maxDays = c.ArchiveDays > 0 ? c.ArchiveDays : 7;
            var past = progs.Where(p => p.StopUtc < now && p.StartUtc > now.AddDays(-maxDays))
                            .OrderByDescending(p => p.StartUtc).ToList();
            if (past.Count == 0)
            {
                progList.Children.Add(Ui.Notice(Lang.T("Sin programas pasados disponibles.")));
                return;
            }
            foreach (var p in past)
            {
                var pp = p;
                var b = new Border
                {
                    Background = Ui.Panel, BorderBrush = Ui.Brd, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 16, 12),
                    Margin = new Thickness(0, 0, 0, 10), Cursor = System.Windows.Input.Cursors.Hand
                };
                var sp = new StackPanel();
                sp.Children.Add(Ui.MarqueeWrap(new TextBlock { Text = pp.Title, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Ui.Text, ToolTip = pp.Title }));
                string dt = pp.StartUtc.ToLocalTime().ToString("dd/MM HH:mm") + " - " + pp.StopUtc.ToLocalTime().ToString("HH:mm");
                if (!string.IsNullOrEmpty(pp.Category)) dt += "  \u00B7 " + pp.Category;
                sp.Children.Add(new TextBlock { Text = dt, FontSize = 12, Foreground = Ui.Gray, Margin = new Thickness(0, 4, 0, 0) });
                if (!string.IsNullOrEmpty(pp.Desc))
                    sp.Children.Add(new TextBlock { Text = pp.Desc, FontSize = 13, Foreground = Ui.Gray, TextWrapping = TextWrapping.Wrap, MaxHeight = 44, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0) });
                b.Child = sp;
                b.MouseLeftButtonUp += (s, e) => PlayCatchup(selCh, pp);
                b.MouseEnter += (s2, e2) => b.BorderBrush = Ui.Red;
                b.MouseLeave += (s2, e2) => b.BorderBrush = Ui.Brd;
                progList.Children.Add(b);
            }
        }

        void PlayCatchup(LiveChannel c, EpgProgramme p)
        {
            var pl = PlaylistStore.Active;
            if (pl == null || pl.Kind != "xtream") { Nav.Toast(Lang.T("Solo disponible en Xtream.")); return; }
            try
            {
                string srv = pl.Server.TrimEnd('/');
                int durMin = Math.Max(1, (int)(p.StopUtc - p.StartUtc).TotalMinutes);
                string start = p.StartUtc.ToString("yyyy-MM-dd:HH-mm");
                string url = $"{srv}/timeshift/{pl.Username}/{pl.Password}/{durMin}/{start}/{c.StreamId}.m3u8";
                PlayerView.Open(p.Title, c.Name + " \u21BA", false, url, c.Logo);
            }
            catch (Exception ex) { Nav.Toast(Lang.T("Error: ") + ex.Message); }
        }
    }

    public class MovieDetailView : ViewBase
    {
        public MovieDetailView(VodMovie m)
        {
            var sp = new StackPanel { Margin = new Thickness(26, 14, 26, 18) };
            sp.Children.Add(Ui.BackHeader(m.Name, () => Nav.Back()));
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Poster
            var poster = new Border { Width = 260, Height = 380, CornerRadius = new CornerRadius(12), Background = Ui.Panel2, BorderBrush = Ui.Brd, BorderThickness = new Thickness(1) };
            var pGrid = new Grid();
            var emoji = new TextBlock { Text = "\U0001F3AC", FontSize = 64, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Gray };
            pGrid.Children.Add(emoji);
            poster.Child = pGrid;
            if (!string.IsNullOrEmpty(m.Logo)) ImgCache.Load(poster, emoji, m.Logo, 520);
            Grid.SetColumn(poster, 0);
            grid.Children.Add(poster);

            // Info
            var info = new StackPanel { Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            // Título + estrella de favorito
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock { Text = m.Name, FontSize = 28, FontWeight = FontWeights.Bold, Foreground = Ui.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            var favBtn = new Button { Content = Favorites.IsFavKey(m.StreamUrl) ? "\u2605" : "\u2606", FontSize = 28, Foreground = Ui.B("#FFD500"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(4), Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Focusable = true, ToolTip = Lang.T("Favorito") };
            favBtn.Click += (s, e) => { Favorites.ToggleKey(m.StreamUrl); favBtn.Content = Favorites.IsFavKey(m.StreamUrl) ? "\u2605" : "\u2606"; };
            titleRow.Children.Add(favBtn);
            info.Children.Add(titleRow);
            if (!string.IsNullOrEmpty(m.Category))
                info.Children.Add(new TextBlock { Text = m.Category, FontSize = 15, Foreground = Ui.Gray, Margin = new Thickness(0, 8, 0, 0) });
            // Detalles del proveedor (género · duración · ★)
            var detLine = new TextBlock { FontSize = 15, Foreground = Ui.Text, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            var detPlot = new TextBlock { FontSize = 15, Foreground = Ui.Text, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
            var detCast = new TextBlock { FontSize = 14, Foreground = Ui.Gray, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
            info.Children.Add(detLine);
            info.Children.Add(detPlot);
            info.Children.Add(detCast);
            LoadVodDetails(m, detLine, detPlot, detCast);

            // Progreso / visto
            long posMs = 0, durMs = 0;
            try
            {
                var e = WatchHistory.LastOpened(1000, x => x.Kind == "movie" && x.Key == m.StreamUrl).FirstOrDefault();
                if (e != null) { posMs = e.PositionMs; durMs = e.DurationMs; }
            }
            catch { }
            bool watched = durMs > 0 && posMs >= durMs * 0.95;
            bool hasProgress = durMs > 0 && posMs >= durMs * 0.03 && !watched;

            if (VodSettings.MarkWatched && watched)
                info.Children.Add(new TextBlock { Text = "\u2713 " + Lang.T("Visto"), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Ui.B("#3ddc84"), Margin = new Thickness(0, 12, 0, 0) });
            else if (hasProgress)
            {
                var ts = TimeSpan.FromMilliseconds(posMs);
                string t = ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}" : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
                info.Children.Add(new TextBlock { Text = Lang.T("Continuar desde ") + t, FontSize = 15, Foreground = Ui.B("#FFD500"), Margin = new Thickness(0, 12, 0, 0) });
            }

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
            var btnPlay = new Button { Content = hasProgress ? Lang.T("Continuar") : Lang.T("Reproducir"), Style = Ui.RoundedButtonStyle(Ui.Red), Padding = new Thickness(32, 14, 32, 14), FontSize = 17, Cursor = System.Windows.Input.Cursors.Hand };
            btnPlay.Click += (s, e) => PlayerView.Open(m.Name, m.Category, false, m.StreamUrl, m.Logo, hasProgress ? posMs : 0);
            btnRow.Children.Add(btnPlay);
            var btnVlc = new Button { Content = "VLC", Style = Ui.RoundedButtonStyle(Ui.Panel2), Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(24, 14, 24, 14), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = Lang.T("Abrir en VLC") };
            btnVlc.Click += (s, e) => { if (!ExternalVlc.Play(m.StreamUrl)) Nav.Toast(Lang.T("VLC no encontrado")); };
            btnRow.Children.Add(btnVlc);
            info.Children.Add(btnRow);
            var btnDl = new Button { Content = "\u2B07 " + Lang.T("Descargar"), Style = Ui.RoundedButtonStyle(Ui.Panel2), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(24, 10, 24, 10), Cursor = System.Windows.Input.Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
            var dlProg = new TextBlock { FontSize = 13, Foreground = Ui.Gray, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
            btnDl.Click += (s, e) => DownloadMovie(m, btnDl, dlProg);
            info.Children.Add(btnDl);
            info.Children.Add(dlProg);

            if (hasProgress)
            {
                var btnRestart = new Button { Content = Lang.T("Desde el inicio"), Style = Ui.RoundedButtonStyle(Ui.Panel2), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(24, 10, 24, 10), Cursor = System.Windows.Input.Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
                btnRestart.Click += (s, e) => PlayerView.Open(m.Name, m.Category, false, m.StreamUrl, m.Logo, 0);
                info.Children.Add(btnRestart);
            }

            Grid.SetColumn(info, 1);
            grid.Children.Add(info);
            sp.Children.Add(grid);
            Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        static async void LoadVodDetails(VodMovie m, TextBlock line, TextBlock plot, TextBlock cast)
        {
            string sid = m.StreamId;
            if (string.IsNullOrEmpty(sid))
            {
                // Respaldo: sacar el ID de la URL /movie/u/p/{id}.ext
                try
                {
                    string u = m.StreamUrl.Split('?')[0];
                    int slash = u.LastIndexOf('/');
                    int dot = u.LastIndexOf('.');
                    if (slash >= 0 && dot > slash) sid = u.Substring(slash + 1, dot - slash - 1);
                }
                catch { }
            }
            var vi = await PlaylistLoader.GetVodInfoAsync(sid);
            // Guardar en el caché de búsqueda para que los filtros lo usen
            if (vi != null && !string.IsNullOrEmpty(sid) && (!string.IsNullOrEmpty(vi.Director) || !string.IsNullOrEmpty(vi.Genre) || !string.IsNullOrEmpty(vi.Cast)))
                VodMetaCache.Set(sid, vi.Director, vi.Genre, vi.Cast);
            App.Current.Dispatcher.Invoke(() =>
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(vi.Genre)) parts.Add(vi.Genre);
                if (!string.IsNullOrEmpty(vi.Duration)) parts.Add(vi.Duration);
                if (!string.IsNullOrEmpty(vi.Rating)) parts.Add("\u2605 " + vi.Rating);
                if (!string.IsNullOrEmpty(vi.Age)) parts.Add(vi.Age);
                line.Text = string.Join(" \u00B7 ", parts);
                if (!string.IsNullOrEmpty(vi.Plot)) plot.Text = vi.Plot;
                var extra = new List<string>();
                if (!string.IsNullOrEmpty(vi.Director)) extra.Add(Lang.T("Director: ") + vi.Director);
                if (!string.IsNullOrEmpty(vi.ReleaseDate)) extra.Add(Lang.T("Estreno: ") + vi.ReleaseDate);
                if (!string.IsNullOrEmpty(vi.Country)) extra.Add(vi.Country);
                string castTxt = "";
                if (!string.IsNullOrEmpty(vi.Cast)) castTxt = Lang.T("Reparto: ") + vi.Cast;
                if (extra.Count > 0) castTxt = (castTxt == "" ? "" : castTxt + "\n") + string.Join(" \u00B7 ", extra);
                cast.Text = castTxt;
            });
        }

        public static void OpenMovieByKey(string key)
        {
            var m = ContentStore.Movies.FirstOrDefault(x => x.StreamUrl == key);
            if (m != null) Nav.Push(new MovieDetailView(m));
            else PlayerView.Open("", "", false, key, null, 0);
        }

        public static void DownloadEpisode(PlaylistLoader.EpisodeInfo ep, SerieItem serie)
        {
            string name = (serie != null ? serie.Name + " " : "") + "S" + Ui.Pad2(ep.Season) + "E" + Ui.Pad2(ep.Num);
            if (!string.IsNullOrEmpty(ep.Title)) name += " " + ep.Title;
            Nav.Toast(Lang.T("Descargando episodio…"));
            _ = DownloadUrlAsync(name, ep.Url);
        }

        public static async System.Threading.Tasks.Task DownloadUrlAsync(string name, string url)
        {
            try
            {
                string folder = Recording.DestFolder;
                System.IO.Directory.CreateDirectory(folder);
                string safe = name;
                foreach (var ch in System.IO.Path.GetInvalidFileNameChars()) safe = safe.Replace(ch, '-');
                safe = safe.Trim();
                if (safe.Length == 0) safe = "video";
                string ext = ".mp4";
                try
                {
                    string u = url.Split('?')[0];
                    int dot = u.LastIndexOf('.');
                    if (dot > 0 && dot > u.LastIndexOf('/'))
                    {
                        string e2 = u.Substring(dot).ToLowerInvariant();
                        if (e2.Length <= 5) ext = e2;
                    }
                }
                catch { }
                string path = System.IO.Path.Combine(folder, safe + ext);
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromHours(4) };
                using var resp = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                using var fs = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None);
                using var stream = await resp.Content.ReadAsStreamAsync();
                var buf = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buf, 0, buf.Length)) > 0)
                    await fs.WriteAsync(buf, 0, read);
                App.Current.Dispatcher.Invoke(() => Nav.Toast(Lang.T("Descargado: ") + safe + ext));
            }
            catch (Exception ex)
            {
                App.Current.Dispatcher.Invoke(() => Nav.Toast(Lang.T("Error: ") + ex.Message));
            }
        }

        static async void DownloadMovie(VodMovie m, Button btn, TextBlock prog)
        {
            btn.IsEnabled = false;
            prog.Visibility = Visibility.Visible;
            try
            {
                string folder = Recording.DestFolder;
                System.IO.Directory.CreateDirectory(folder);
                string safe = m.Name;
                foreach (var ch in System.IO.Path.GetInvalidFileNameChars()) safe = safe.Replace(ch, '-');
                safe = safe.Trim();
                if (safe.Length == 0) safe = "pelicula";
                string ext = ".mp4";
                try
                {
                    string u = m.StreamUrl.Split('?')[0];
                    int dot = u.LastIndexOf('.');
                    if (dot > 0 && dot > u.LastIndexOf('/'))
                    {
                        string e2 = u.Substring(dot).ToLowerInvariant();
                        if (e2.Length <= 5) ext = e2;
                    }
                }
                catch { }
                string path = System.IO.Path.Combine(folder, safe + ext);
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromHours(4) };
                using var resp = await http.GetAsync(m.StreamUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? -1;
                using var fs = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None);
                using var stream = await resp.Content.ReadAsStreamAsync();
                var buf = new byte[81920];
                long done = 0;
                int read;
                var lastUpd = DateTime.UtcNow;
                while ((read = await stream.ReadAsync(buf, 0, buf.Length)) > 0)
                {
                    await fs.WriteAsync(buf, 0, read);
                    done += read;
                    if ((DateTime.UtcNow - lastUpd).TotalMilliseconds > 500)
                    {
                        lastUpd = DateTime.UtcNow;
                        long d2 = done;
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            prog.Text = total > 0 ? $"{d2 * 100 / total}% ({d2 / 1048576} MB)" : $"{d2 / 1048576} MB";
                        });
                    }
                }
                App.Current.Dispatcher.Invoke(() =>
                {
                    prog.Text = Lang.T("Descargada");
                    Nav.Toast(Lang.T("Descargada en ") + folder);
                });
            }
            catch (Exception ex)
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    prog.Text = Lang.T("Error al descargar");
                    Nav.Toast(Lang.T("Error: ") + ex.Message);
                });
            }
            App.Current.Dispatcher.Invoke(() => { btn.IsEnabled = true; });
        }
    }
}
