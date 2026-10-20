using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using VlcPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace MiTV
{
    // Pestaña "Guía": parrilla de programación estilo WiseOTT.
    // - Columna de categorías a la izquierda (clic filtra).
    // - Ventana: 4 horas atrás + 24 horas adelante (28 h).
    // - Panel superior: 1 clic muestra detalles, doble clic inicia el video en vivo (muteado).
    // - Nombres largos con marquesina suave, 6 palabras de detalle bajo el título,
    //   ficha contextual al pasar el mouse 1 segundo, línea roja del "ahora".
    public class EpgView : ViewBase
    {
        const double PxPerMin = 3.0;
        const double ChColW = 200;
        const double RowH = 64;
        const double CatW = 170;

        StackPanel catsPanel;
        ScrollViewer headerScroll, rowsScroll, chanScroll;
        Canvas headerCanvas;
        StackPanel chanPanel, timePanel;
        string curCat = "Todo";
        int renderToken;

        DateTime winStartUtc, winEndUtc;
        double totalW;
        readonly List<Border> nowLines = new();
        readonly Dictionary<string, Border> cells = new(); // StreamUrl -> celda del canal

        // Panel de detalle + preview
        VideoView previewVideo;
        Button fsBtn, vlcBtn;
        TextBlock liveBadge, dPreviewTitle, previewHint, dProgName, dChName, dTime, dDesc, dExtra;
        LibVLC previewVlc;
        VlcPlayer previewMp;
        LiveChannel playingCh;
        Border playingCell;

        // Selección de bloque
        Border selBlock;
        object[] selOrig;

        // 1 clic vs doble clic
        DispatcherTimer clickTimer;
        Border pendingBlock;
        LiveChannel pendingCh;
        EpgProgramme pendingProg;

        DispatcherTimer nowTimer;

        static readonly SolidColorBrush BlkBg = Ui.B("#1A2330");
        static readonly SolidColorBrush Purple = Ui.B("#7C6CF0");
        static readonly SolidColorBrush PurpleFill = Ui.B("#4B3FA3");
        static readonly SolidColorBrush Orange = Ui.B("#E8A13D");

        public EpgView()
        {
            var now = DateTime.UtcNow;
            int pastH = EpgSettings.PastHours, futH = EpgSettings.FutureHours;
            winStartUtc = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(-pastH);
            winEndUtc = winStartUtc.AddHours(pastH + futH);
            totalW = (pastH + futH) * 60 * PxPerMin;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(BuildDetail());
            var gridArea = BuildGridArea();
            Grid.SetRow(gridArea, 1);
            root.Children.Add(gridArea);
            Content = root;

            clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            clickTimer.Tick += (s, e) =>
            {
                clickTimer.Stop();
                if (pendingBlock != null)
                {
                    SelectBlock(pendingBlock);
                    ShowDetails(pendingCh, pendingProg);
                    pendingBlock = null;
                }
            };
            nowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            nowTimer.Tick += (s, e) => MoveNowLines();
            nowTimer.Start();

            string startCat = ViewState.Get("epg");
            if (Array.IndexOf(Cats(), startCat) < 0) startCat = "Todo";
            RenderCats(startCat);
            RenderGrid(startCat);
        }

        // ---------------- Panel superior: detalle + preview ----------------

        UIElement BuildDetail()
        {
            var wrap = new Border
            {
                Background = Ui.TopBg,
                BorderBrush = Ui.Brd,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(18, 14, 18, 14)
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Preview de video
            var pv = new Border
            {
                Height = 200,
                CornerRadius = new CornerRadius(10),
                Background = Brushes.Black,
                BorderBrush = Ui.Brd,
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                Margin = new Thickness(0, 0, 18, 0)
            };
            var pvg = new Grid();
            previewVideo = new VideoView
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            pvg.Children.Add(previewVideo);
            liveBadge = new TextBlock
            {
                Text = Lang.T("EN VIVO"),
                FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                Background = Ui.Red, Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 10, 0), Visibility = Visibility.Collapsed
            };
            previewHint = new TextBlock
            {
                Text = Lang.T("Doble clic en un canal para verlo aquí"),
                FontSize = 13, Foreground = Ui.Gray,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                Margin = new Thickness(30, 0, 30, 0)
            };
            dPreviewTitle = new TextBlock
            {
                FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(14, 0, 14, 10), TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 350
            };
            fsBtn = new Button
            {
                Content = "⛶",
                Style = Ui.TransparentButton(),
                FontSize = 18, Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(130, 0, 0, 0)),
                Padding = new Thickness(10, 4, 10, 4), Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 10, 8), Visibility = Visibility.Collapsed,
                ToolTip = Lang.T("Ver en grande")
            };
            fsBtn.Click += (s, e) => OpenFull();
            vlcBtn = new Button
            {
                Content = "VLC",
                Style = Ui.TransparentButton(),
                FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(130, 0, 0, 0)),
                Padding = new Thickness(10, 4, 10, 4), Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 58, 8), Visibility = Visibility.Collapsed,
                ToolTip = Lang.T("Abrir en VLC")
            };
            vlcBtn.Click += (s, e) => SendPreviewToVlc();
            // Los overlays van DENTRO del VideoView: LibVLCSharp los muestra en una
            // ventana transparente sobre el video (resuelve el airspace de WPF,
            // igual que el OSD del reproductor principal). Si van como hermanos,
            // el video los tapa y no se ve nada.
            var over = new Grid { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
            over.Children.Add(liveBadge);
            over.Children.Add(previewHint);
            over.Children.Add(dPreviewTitle);
            over.Children.Add(fsBtn);
            over.Children.Add(vlcBtn);
            over.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2 && playingCh != null) OpenFull(); };
            previewVideo.Content = over;
            pv.Child = pvg;
            g.Children.Add(pv);

            // Detalles del programa
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            dProgName = new TextBlock { FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Ui.Text, TextWrapping = TextWrapping.Wrap };
            dChName = new TextBlock { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Purple, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 4) };
            dTime = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Orange, Margin = new Thickness(0, 6, 0, 0) };
            dDesc = new TextBlock { FontSize = 14, Foreground = Ui.Gray, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, MaxHeight = 110 };
            dExtra = new TextBlock { FontSize = 13, Foreground = Ui.Gray, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            info.Children.Add(dChName);
            info.Children.Add(dProgName);
            info.Children.Add(dTime);
            info.Children.Add(dDesc);
            info.Children.Add(dExtra);
            Grid.SetColumn(info, 1);
            g.Children.Add(info);

            wrap.Child = g;
            return wrap;
        }

        void ShowDetails(LiveChannel c, EpgProgramme p)
        {
            dProgName.Text = p.Title;
            dChName.Text = c.Name;
            dTime.Text = p.StartUtc.ToLocalTime().ToString("HH:mm") + " - " + p.StopUtc.ToLocalTime().ToString("HH:mm");
            dDesc.Text = string.IsNullOrWhiteSpace(p.Desc) ? "Sin descripción disponible." : p.Desc;
            var xp = new List<string>();
            if (!string.IsNullOrEmpty(p.Category)) xp.Add(p.Category);
            if (!string.IsNullOrEmpty(p.Rating)) xp.Add("\u2605 " + p.Rating);
            if (!string.IsNullOrEmpty(p.Director)) xp.Add(Lang.T("Director: ") + p.Director);
            if (!string.IsNullOrEmpty(p.Actors)) xp.Add(Lang.T("Reparto: ") + p.Actors);
            dExtra.Text = string.Join("\n", xp);
            dExtra.Visibility = xp.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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

                playingCh = c;
                liveBadge.Visibility = Visibility.Visible;
                previewHint.Visibility = Visibility.Collapsed;
                if (fsBtn != null) fsBtn.Visibility = Visibility.Visible;
                if (vlcBtn != null) vlcBtn.Visibility = Visibility.Visible;
                var (now, _) = EpgStore.GetNowNext(c);
                dPreviewTitle.Text = now != null ? now.Title : c.Name;
                if (now != null) ShowDetails(c, now);
                HighlightCell(c);
            }
            catch
            {
                previewHint.Text = Lang.T("No se pudo reproducir este canal");
                previewHint.Visibility = Visibility.Visible;
            }
        }

        void HighlightCell(LiveChannel c)
        {
            if (playingCell != null) playingCell.Background = Ui.Panel;
            playingCell = null;
            if (c != null && cells.TryGetValue(c.StreamUrl, out var cell))
            {
                cell.Background = Ui.B("#2A1A1E");
                playingCell = cell;
            }
        }

        // Envía el preview al VLC de escritorio y lo detiene en la app
        void SendPreviewToVlc()
        {
            if (playingCh == null || string.IsNullOrEmpty(playingCh.StreamUrl)) return;
            if (!ExternalVlc.Play(playingCh.StreamUrl)) { Nav.Toast(Lang.T("VLC no encontrado")); return; }
            StopPreview();
        }

        DateTime lastFull;
        void OpenFull()
        {
            if ((DateTime.Now - lastFull).TotalMilliseconds < 800) return;
            lastFull = DateTime.Now;
            if (playingCh == null || string.IsNullOrEmpty(playingCh.StreamUrl)) return;
            var c = playingCh;
            StopPreview(); // que no siga sonando en el fondo
            PlayerView.Open(c.Name, c.Group, true, c.StreamUrl, c.Logo, 0, "channel");
        }

        void StopPreview()
        {
            try { if (previewMp != null) previewMp.Stop(); } catch { }
            playingCh = null;
            liveBadge.Visibility = Visibility.Collapsed;
            if (fsBtn != null) fsBtn.Visibility = Visibility.Collapsed;
            if (vlcBtn != null) vlcBtn.Visibility = Visibility.Collapsed;
            previewHint.Text = Lang.T("Doble clic en un canal para verlo aquí");
            previewHint.Visibility = Visibility.Visible;
            dPreviewTitle.Text = "";
        }

        public override void OnLeave()
        {
            clickTimer?.Stop();
            nowTimer?.Stop();
            try
            {
                if (previewVideo != null) previewVideo.MediaPlayer = null;
                if (previewMp != null) { try { previewMp.Stop(); } catch { } previewMp.Dispose(); previewMp = null; }
                if (previewVlc != null) { previewVlc.Dispose(); previewVlc = null; }
            }
            catch { }
        }

        // ---------------- Área de la parrilla ----------------

        UIElement BuildGridArea()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CatW) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var catScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            catsPanel = new StackPanel { Margin = new Thickness(12, 10, 8, 10) };
            catScroll.Content = catsPanel;
            g.Children.Add(catScroll);

            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            headerScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            headerCanvas = new Canvas { Width = totalW, Height = 34, Background = Ui.TopBg };
            headerScroll.Content = headerCanvas;
            DrawHeader();

            // Columna de canales fija + área de tiempo con scroll
            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ChColW) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            body.Children.Add(new Border { Background = Ui.TopBg }); // esquina vacía

            Grid.SetColumn(headerScroll, 1);
            body.Children.Add(headerScroll);

            chanScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden
            };
            chanPanel = new StackPanel();
            chanScroll.Content = chanPanel;
            Grid.SetRow(chanScroll, 1);
            body.Children.Add(chanScroll);

            rowsScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            timePanel = new StackPanel();
            rowsScroll.Content = timePanel;
            // Sincronización en AMBAS direcciones: la columna de canales también
            // recibe la rueda del mouse, así que debe arrastrar a la parrilla y viceversa
            bool syncingV = false;
            rowsScroll.ScrollChanged += (s, e) =>
            {
                if (e.HorizontalChange != 0)
                    headerScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
                if (e.VerticalChange != 0 && !syncingV)
                {
                    syncingV = true;
                    chanScroll.ScrollToVerticalOffset(e.VerticalOffset);
                    syncingV = false;
                }
            };
            chanScroll.ScrollChanged += (s, e) =>
            {
                if (e.VerticalChange != 0 && !syncingV)
                {
                    syncingV = true;
                    rowsScroll.ScrollToVerticalOffset(e.VerticalOffset);
                    syncingV = false;
                }
            };
            Grid.SetRow(rowsScroll, 1);
            Grid.SetColumn(rowsScroll, 1);
            body.Children.Add(rowsScroll);

            Grid.SetRow(body, 1);
            right.Children.Add(body);

            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            return g;
        }

        void DrawHeader()
        {
            headerCanvas.Children.Clear();
            for (var t = winStartUtc; t <= winEndUtc; t = t.AddHours(2))
            {
                double x = (t - winStartUtc).TotalMinutes * PxPerMin;
                var tb = new TextBlock
                {
                    Text = t.ToLocalTime().ToString("HH:mm"),
                    FontSize = 12, Foreground = Ui.Gray,
                    Width = 120, TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(tb, x - 60);
                Canvas.SetTop(tb, 9);
                headerCanvas.Children.Add(tb);
            }
        }

        string[] Cats()
        {
            return new[] { "Todo" }.Concat(ContentStore.Channels
                .Select(c => c.Group)
                .Where(gr => !string.IsNullOrWhiteSpace(gr))
                .Distinct().OrderBy(gr => gr)).ToArray();
        }

        void RenderCats(string sel)
        {
            catsPanel.Children.Clear();
            int efn = ContentStore.Channels.Count(ch => Favorites.IsFav(ch));
            catsPanel.Children.Add(Ui.CatButton("\u2605 " + Lang.T("Favoritos"), efn, "Favoritos" == sel, () => { ViewState.Set("epg", "Favoritos"); RenderCats("Favoritos"); RenderGrid("Favoritos"); }));
            foreach (var c in Cats())
            {
                string cc = c;
                int n = cc == "Todo" ? ContentStore.Channels.Count : ContentStore.Channels.Count(ch => ch.Group == cc);
                catsPanel.Children.Add(Ui.CatButton(c, n, c == sel, () => { ViewState.Set("epg", cc); RenderCats(cc); RenderGrid(cc); }));
            }
        }

        void RenderGrid(string cat)
        {
            curCat = cat;
            int token = ++renderToken;
            chanPanel.Children.Clear();
            timePanel.Children.Clear();
            cells.Clear();
            nowLines.Clear();
            selBlock = null;
            playingCell = null;

            if (ContentStore.Channels.Count == 0)
            {
                timePanel.Children.Add(Ui.Notice(Lang.T("Sin lista activa. Ve a Ajustes → Listas IPTV, agrega una lista y sincroniza.")));
                return;
            }
            if (!EpgStore.HasData)
            {
                timePanel.Children.Add(Ui.Notice(Lang.T("Guía no disponible. Sincroniza la lista (la EPG se descarga junto con el contenido).")));
                return;
            }
            var items = ContentStore.Channels.Where(ch => cat == "Todo" || (cat == "Favoritos" ? Favorites.IsFav(ch) : ch.Group == cat)).ToList();
            if (items.Count == 0)
            {
                timePanel.Children.Add(Ui.Notice(Lang.T("Sin canales en esta categoría.")));
                return;
            }
            int i = 0;
            bool first = true;
            Action addChunk = null;
            addChunk = () =>
            {
                if (token != renderToken) return;
                int end = Math.Min(i + 40, items.Count);
                for (; i < end; i++)
                {
                    chanPanel.Children.Add(ChannelCell(items[i], i + 1));
                    timePanel.Children.Add(TimeCanvas(items[i]));
                }
                if (first)
                {
                    first = false;
                    // detalle inicial: programa actual del primer canal
                    var c0 = items[0];
                    var (now0, _) = EpgStore.GetNowNext(c0);
                    if (now0 != null) ShowDetails(c0, now0);
                    else { dChName.Text = c0.Name; dTime.Text = ""; dDesc.Text = "Sin información de guía."; }
                    // desplazar a la hora actual
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            double nx = (DateTime.UtcNow - winStartUtc).TotalMinutes * PxPerMin;
                            rowsScroll.ScrollToHorizontalOffset(Math.Max(0, nx - rowsScroll.ViewportWidth / 3));
                        }
                        catch { }
                    }), DispatcherPriority.Background);
                }
                if (i < items.Count)
                    Dispatcher.BeginInvoke(addChunk, DispatcherPriority.Background);
            };
            addChunk();
        }

        // Celda del canal (columna fija): número + logo con borde blanco + nombre con marquesina
        Border ChannelCell(LiveChannel c, int num)
        {
            var cell = new Border
            {
                Background = Ui.Panel,
                BorderBrush = Ui.Brd,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Height = RowH
            };
            var dock = new DockPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            dock.Children.Add(new TextBlock { Text = num.ToString(), FontSize = 14, Foreground = Ui.Gray, Width = 22, VerticalAlignment = VerticalAlignment.Center });
            // Estrella de favorito antes del logo
            var cc2 = c;
            var starTxt = new TextBlock
            {
                Text = Favorites.IsFav(cc2) ? "\u2605" : "\u2606",
                FontSize = 18,
                Foreground = Favorites.IsFav(cc2) ? Ui.B("#FFD500") : Ui.Gray,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = true,
                ToolTip = Lang.T("Favorito")
            };
            var starBox = new Border { Background = Brushes.Transparent, Child = starTxt, Focusable = true, Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            starBox.MouseLeftButtonUp += (s, e) => { e.Handled = true; Favorites.Toggle(cc2); bool f = Favorites.IsFav(cc2); starTxt.Text = f ? "\u2605" : "\u2606"; starTxt.Foreground = f ? Ui.B("#FFD500") : Ui.Gray; };
            starBox.TouchDown += (s, e) => { e.Handled = true; Favorites.Toggle(cc2); bool f = Favorites.IsFav(cc2); starTxt.Text = f ? "\u2605" : "\u2606"; starTxt.Foreground = f ? Ui.B("#FFD500") : Ui.Gray; };
            starBox.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Space) { e.Handled = true; Favorites.Toggle(cc2); bool f = Favorites.IsFav(cc2); starTxt.Text = f ? "\u2605" : "\u2606"; starTxt.Foreground = f ? Ui.B("#FFD500") : Ui.Gray; } };
            DockPanel.SetDock(starBox, Dock.Left);
            dock.Children.Add(starBox);
            var logoBox = new Border
            {
                Width = 52, Height = 32,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Black,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var ph = new TextBlock { Text = "📺", FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            logoBox.Child = ph;
            if (!string.IsNullOrEmpty(c.Logo)) ImgCache.Load(logoBox, ph, c.Logo, 104);
            DockPanel.SetDock(logoBox, Dock.Left);
            dock.Children.Add(logoBox);
            dock.Children.Add(MarqueeName(c.Name, 96));
            cell.Child = dock;
            var cc = c;
            cell.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) PlayPreview(cc); };
            cells[c.StreamUrl] = cell;
            return cell;
        }

        // Línea de tiempo del canal con los bloques de programas
        Border TimeCanvas(LiveChannel c)
        {
            var wrap = new Border
            {
                Width = totalW,
                Height = RowH,
                BorderBrush = Ui.Brd,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var inner = new Canvas { Width = totalW, Height = RowH, Background = Brushes.Transparent };
            var progs = EpgStore.GetPrograms(c);
            if (progs != null)
            {
                foreach (var p in progs)
                {
                    if (p.StopUtc < winStartUtc || p.StartUtc > winEndUtc) continue;
                    inner.Children.Add(ProgBlock(c, p));
                }
            }
            var nl = new Border { Width = 2, Height = RowH, Background = Ui.Red };
            double nx = (DateTime.UtcNow - winStartUtc).TotalMinutes * PxPerMin;
            Canvas.SetLeft(nl, Math.Max(0, Math.Min(totalW, nx)));
            inner.Children.Add(nl);
            nowLines.Add(nl);
            wrap.Child = inner;
            return wrap;
        }

        Border ProgBlock(LiveChannel c, EpgProgramme p)
        {
            var s = p.StartUtc < winStartUtc ? winStartUtc : p.StartUtc;
            var e = p.StopUtc > winEndUtc ? winEndUtc : p.StopUtc;
            double left = (s - winStartUtc).TotalMinutes * PxPerMin;
            double w = Math.Max(36, (e - s).TotalMinutes * PxPerMin);
            bool isNow = p.StartUtc <= DateTime.UtcNow && DateTime.UtcNow < p.StopUtc;
            var b = new Border
            {
                Background = BlkBg,
                BorderBrush = isNow ? Ui.Red : Ui.Brd,
                BorderThickness = isNow ? new Thickness(3, 1, 1, 1) : new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 5, 8, 5),
                Cursor = Cursors.Hand,
                Width = w,
                Height = RowH - 12
            };
            var sp = new StackPanel();
            sp.Children.Add(Ui.MarqueeWrap(new TextBlock { Text = p.Title, FontSize = 13, Foreground = Brushes.White, ToolTip = p.Title }));
            if (w > 120)
            {
                string det = ShortWords(p.Desc, 6);
                if (!string.IsNullOrEmpty(det))
                    sp.Children.Add(new TextBlock { Text = det, FontSize = 11, Foreground = Ui.Gray, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
            }
            b.Child = sp;
            Canvas.SetLeft(b, left);
            Canvas.SetTop(b, 6);
            var ch = c; var pr = p;
            b.MouseLeftButtonDown += (s2, e2) =>
            {
                if (e2.ClickCount == 2) { clickTimer.Stop(); pendingBlock = null; SelectBlock(b); PlayPreview(ch); }
                else { pendingBlock = b; pendingCh = ch; pendingProg = pr; clickTimer.Stop(); clickTimer.Start(); }
            };
            // Clic derecho: programar grabación
            b.MouseRightButtonDown += (s2, e2) =>
            {
                e2.Handled = true;
                ShowRecMenu(b, ch, pr);
            };
            // Marca si ya está programada
            if (IsScheduled(ch, pr))
            {
                var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Brushes.Red, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 4, 0), ToolTip = Lang.T("Grabación programada") };
                var gd = new Grid();
                gd.Children.Add(sp);
                gd.Children.Add(dot);
                b.Child = gd;
            }
            ToolTipService.SetInitialShowDelay(b, 1000);
            ToolTipService.SetShowDuration(b, 20000);
            var tip = new ToolTip { Background = Ui.Panel, BorderBrush = Ui.Brd, Foreground = Ui.Text };
            tip.Content = BuildTip(c, p);
            b.ToolTip = tip;
            return b;
        }

        static string ShortWords(string s, int n)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var words = s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= n) return string.Join(" ", words);
            return string.Join(" ", words.Take(n)) + "…";
        }

        bool IsScheduled(LiveChannel c, EpgProgramme p)
        {
            return Recording.Scheduled.Any(s => s.StreamUrl == c.StreamUrl && s.StartUtc == p.StartUtc);
        }

        void ShowRecMenu(Border b, LiveChannel c, EpgProgramme p)
        {
            var cm = new ContextMenu { Background = Ui.Panel, BorderBrush = Ui.Brd, Foreground = Ui.Text };
            if (IsScheduled(c, p))
            {
                var mi = new MenuItem { Header = Lang.T("Quitar grabación programada"), Foreground = Brushes.IndianRed };
                mi.Click += (s2, e2) =>
                {
                    var s = Recording.Scheduled.FirstOrDefault(x => x.StreamUrl == c.StreamUrl && x.StartUtc == p.StartUtc);
                    if (s != null) Recording.RemoveScheduled(s.Id);
                    RenderGrid(curCat);
                };
                cm.Items.Add(mi);
            }
            else
            {
                var mi = new MenuItem { Header = "\u23FA " + Lang.T("Programar grabación") };
                mi.Click += (s2, e2) =>
                {
                    Recording.AddScheduled(new Recording.ScheduledRec
                    {
                        ChannelName = c.Name,
                        StreamUrl = c.StreamUrl,
                        Logo = c.Logo,
                        Title = p.Title,
                        StartUtc = p.StartUtc,
                        EndUtc = p.StopUtc
                    });
                    Nav.Toast(Lang.T("Grabación programada"));
                    RenderGrid(curCat);
                };
                cm.Items.Add(mi);
            }
            cm.PlacementTarget = b;
            cm.IsOpen = true;
        }

        object BuildTip(LiveChannel c, EpgProgramme p)
        {
            var sp = new StackPanel { MaxWidth = 320 };
            sp.Children.Add(new TextBlock { Text = p.Title, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Ui.Text, TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(new TextBlock
            {
                Text = p.StartUtc.ToLocalTime().ToString("HH:mm") + " - " + p.StopUtc.ToLocalTime().ToString("HH:mm"),
                FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Orange, Margin = new Thickness(0, 4, 0, 0)
            });
            sp.Children.Add(new TextBlock { Text = c.Name, FontSize = 12, Foreground = Purple, Margin = new Thickness(0, 2, 0, 0) });
            sp.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(p.Desc) ? "Sin descripción disponible." : p.Desc,
                FontSize = 12, Foreground = Ui.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0)
            });
            return new Border
            {
                Background = Ui.B("#161E27"),
                BorderBrush = Ui.Brd,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Child = sp
            };
        }

        void SelectBlock(Border b)
        {
            if (selBlock == b) return;
            if (selBlock != null && selOrig != null)
            {
                selBlock.Background = (Brush)selOrig[0];
                selBlock.BorderBrush = (Brush)selOrig[1];
                selBlock.BorderThickness = (Thickness)selOrig[2];
            }
            selOrig = new object[] { b.Background, b.BorderBrush, b.BorderThickness };
            b.Background = PurpleFill;
            b.BorderBrush = Purple;
            selBlock = b;
        }

        void MoveNowLines()
        {
            try
            {
                double nx = (DateTime.UtcNow - winStartUtc).TotalMinutes * PxPerMin;
                nx = Math.Max(0, Math.Min(totalW, nx));
                foreach (var nl in nowLines) Canvas.SetLeft(nl, nx);
            }
            catch { }
        }

        // Nombre con marquesina suave: usa el patrón común de Ui (pausa 5 s al
        // inicio, desplazamiento, pausa 3 s al final y regreso)
        static Border MarqueeName(string text, double width)
        {
            var tb = new TextBlock { Text = text, FontWeight = FontWeights.Bold, FontSize = 14, Foreground = Ui.Text, ToolTip = text };
            var host = Ui.MarqueeWrap(tb);
            host.Width = width;
            host.VerticalAlignment = VerticalAlignment.Center;
            return host;
        }
    }
}
