using System.Windows.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using Microsoft.Win32;
using VlcPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace MiTV
{
    // Reloj permanente: números blancos con la transparencia del fondo de
    // subtítulos y contorno amarillo fino
    public class ClockFace : FrameworkElement
    {
        string text = "";
        double fontSize = 52;
        public double FontSizePx { get => fontSize; set { if (fontSize != value) { fontSize = value; InvalidateVisual(); } } }
        public void SetTime(string t) { if (text != t) { text = t; InvalidateVisual(); } }
        FormattedText BuildFt() => new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            fontSize, Brushes.White, 1.0);
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (string.IsNullOrEmpty(text)) return;
            byte a = (byte)Math.Max(0, Math.Min(255, SubStyle.BgOpacity));
            dc.DrawGeometry(
                new SolidColorBrush(Color.FromArgb(a, 255, 255, 255)),
                new Pen(new SolidColorBrush(Color.FromArgb(a, 255, 213, 0)), 1.4),
                BuildFt().BuildGeometry(new Point(0, 0)));
        }
        protected override Size MeasureOverride(Size s)
        {
            if (string.IsNullOrEmpty(text)) return new Size(0, 0);
            var ft = BuildFt();
            return new Size(Math.Ceiling(ft.Width) + 8, Math.Ceiling(ft.Height) + 8);
        }
    }

    public class PlayerView : ViewBase
    {
        public static void Open(string name, string sub, bool live, string url = null, string logo = null, long startMs = 0, string kind = null, string category = null)
        {
            StopBackground(); // abrir otro elemento corta el segundo plano
            if (!string.IsNullOrEmpty(url))
                WatchHistory.RecordPlay(url, name, sub, logo, kind ?? (live ? "channel" : "movie"), category ?? sub);
            Nav.Push(new PlayerView(name, sub, live, url, startMs, kind ?? (live ? "channel" : "movie")));
        }

        // Reproducción en segundo plano: el player sobrevive al salir de la vista
        class BgState
        {
            public LibVLC libvlc;
            public VlcPlayer mp;
            public Timeshift ts;
        }
        static BgState bg;

        public static void StopBackground()
        {
            var b = bg; bg = null;
            if (b == null) return;
            try
            {
                if (b.ts != null) { try { b.ts.Dispose(); } catch { } }
                if (b.mp != null) { try { b.mp.Stop(); } catch { } b.mp.Dispose(); }
                if (b.libvlc != null) b.libvlc.Dispose();
            }
            catch { }
        }

        // VLC se inicializa una sola vez (los codecs van junto al exe)
        static bool vlcOk, vlcTried;
        public static void EnsureVlc()
        {
            if (vlcTried) return;
            vlcTried = true;
            try { Core.Initialize(); vlcOk = true; }
            catch { vlcOk = false; }
        }

        DispatcherTimer tick, hideTimer, loadTimer, infoTimer, clockTimer;
        Border infoPanel, infoProgFill;
        TextBlock infoRemain;
        ClockFace clockFace;
        EpgProgramme infoProg;
        DispatcherTimer clockHoverTimer;
        bool clockDragging, clockShowingDate;
        Point clockDragOffset;
        bool playing, isLive, muted, real;
        string chName = "", chSub = "";
        double t, dur, vol = 80;
        int aspect;
        readonly string[] aspects = { "16:9", "4:3", "Llenar pantalla" };
        readonly string streamUrl;

        LibVLC libvlc;
        VlcPlayer mp;
        VideoView videoView;
        Timeshift ts;
        TextBlock liveBadgeText;
        long tsDelayMs; // retraso respecto al en vivo (ms), lo llevamos nosotros
        DateTime tsPauseAt;

        Border osdTop, osdBottom, seekBar, seekFill, seekBuf;
        TextBlock timeLabel, bigPause, loadText;
        Button playBtn, muteBtn, rateBtn, recBtn;
        Image introImg;
        double playRate = 1.0;

        // Botones de salto: id, etiqueta, nombre para ajustes, segundos
        public static readonly (string id, string label, string name, int secs)[] SkipDefs =
        {
            ("m10", "-10m", "-10 min", -600), ("m1", "-1m", "-1 min", -60),
            ("s10", "-10s", "-10 seg", -10), ("s10p", "+10s", "+10 seg", 10),
            ("m1p", "+1m", "+1 min", 60), ("m10p", "+10m", "+10 min", 600),
        };
        DateTime lastMoveAt; // último seek: el spinner no debe salir al mover
        static readonly double[] Rates = { 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0 };
        Border spinner, volFill;
        bool isFullscreen;
        bool prevMaximized;
        bool noBackground; // enviar a VLC: no transferir a segundo plano al salir

        // Menú de audio / subtítulos
        Border menuPanel;
        StackPanel menuContent;
        TextBlock menuTitle;
        string menuMode;
        string externalSubPath;
        bool externalSubSelected;
        (long time, int spu, bool extSel)? restorePending;
        long pendingSeek; // continuar donde quedó (ms)
        int saveCounter;

        public PlayerView(string name, string sub, bool live, string url, long startMs = 0, string kind = null)
        {
            isLive = live;
            chName = name; chSub = sub;
            real = !string.IsNullOrEmpty(url);
            streamUrl = url;
            pendingSeek = startMs;
            t = live ? 0 : 1200;
            dur = live ? 0 : 7200;

            var root = new Grid { Background = Brushes.Black };

            if (real)
            {
                videoView = new VideoView
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                root.Children.Add(videoView);
            }
            else
            {
                // Fondo "video" simulado
                var videoBg = new Border
                {
                    Background = new RadialGradientBrush(
                        Ui.B("#1B2B3A").Color, Ui.B("#05080B").Color)
                        { GradientOrigin = new Point(0.3, 0.2), RadiusX = 1, RadiusY = 1 }
                };
                root.Children.Add(videoBg);
                root.Children.Add(new TextBlock
                {
                    Text = live ? "📺" : "🎬",
                    FontSize = 120,
                    Opacity = 0.22,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            // Spinner de carga
            spinner = new Border { Background = new SolidColorBrush(Color.FromArgb(115, 0, 0, 0)) };
            var spLoad = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            spLoad.Children.Add(new ProgressBar { Width = 220, Height = 6, IsIndeterminate = true, Margin = new Thickness(0, 0, 0, 14) });
            loadText = new TextBlock { Text = Lang.T("Cargando..."), Foreground = Ui.B("#C4D0D8"), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center };
            spLoad.Children.Add(loadText);
            spinner.Child = spLoad;

            // Pausa grande
            bigPause = new TextBlock
            {
                Text = "▶",
                FontSize = 92,
                Foreground = new SolidColorBrush(Color.FromArgb(208, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 34, ShadowDepth = 0 }
            };

            // OSD superior
            osdTop = new Border
            {
                VerticalAlignment = VerticalAlignment.Top,
                Padding = new Thickness(28, 18, 28, 26),
                Background = new LinearGradientBrush(
                    Color.FromArgb(200, 0, 0, 0), Colors.Transparent, 90)
            };
            var topDock = new DockPanel();
            var back = new Button { Content = "←", Style = Ui.TransparentButton() };
            back.Click += (s, e) => { e.Handled = true; GoBack(); };
            DockPanel.SetDock(back, Dock.Left);
            topDock.Children.Add(back);
            if (live)
            {
                liveBadgeText = new TextBlock { Text = Lang.T("EN VIVO"), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
                var lb = new Border
                {
                    Background = Ui.Red, CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(12, 4, 12, 4),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand,
                    ToolTip = Lang.T("Volver al en vivo"),
                    Child = liveBadgeText
                };
                lb.MouseLeftButtonUp += (s, e) => { e.Handled = true; GoLive(); };
                DockPanel.SetDock(lb, Dock.Right);
                topDock.Children.Add(lb);
            }
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            titles.Children.Add(new TextBlock { Text = name, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
            titles.Children.Add(new TextBlock { Text = sub, FontSize = 14, Foreground = Ui.Gray, Margin = new Thickness(0, 2, 0, 0) });
            topDock.Children.Add(titles);
            osdTop.Child = topDock;

            // OSD inferior
            osdBottom = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Padding = new Thickness(28, 26, 28, 20),
                Background = new LinearGradientBrush(
                    Colors.Transparent, Color.FromArgb(225, 0, 0, 0), 90)
            };
            var botSp = new StackPanel();
            // Fila de seek
            var seekRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            seekBar = new Border
            {
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = Ui.BarBg,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = live ? Cursors.Arrow : Cursors.Hand,
                Opacity = live ? 0.35 : 1
            };
            var seekGrid = new Grid();
            seekBuf = new Border { Background = Ui.BarBuf, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            seekFill = new Border { Background = Ui.B("#FFD500"), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            seekGrid.Children.Add(seekBuf);
            seekGrid.Children.Add(seekFill);
            seekBar.Child = seekGrid;
            seekBar.MouseLeftButtonDown += (s, e) =>
            {
                var p = e.GetPosition(seekBar);
                if (seekBar.ActualWidth > 0)
                {
                    double frac = Math.Max(0, Math.Min(1, p.X / seekBar.ActualWidth));
                    SeekToFraction(frac);
                }
                Upd();
                Poke();
            };
            seekBar.SizeChanged += (s, e) => Upd();
            seekRow.Children.Add(seekBar);
            botSp.Children.Add(seekRow);
            // Fila de botones
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            playBtn = new Button { Content = "⏸", Style = Ui.TransparentButton(), FontSize = 22 };
            playBtn.Click += (s, e) => { TogglePlay(); Poke(); };
            var skipBtns = new System.Collections.Generic.List<Button>();
            foreach (var sd in SkipDefs)
            {
                if (!PlaybackSettings.IsSkipVisible(sd.id)) continue;
                var sb = new Button
                {
                    Content = sd.label, Style = Ui.TransparentButton(), FontSize = 13,
                    FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                    Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand, ToolTip = sd.name
                };
                int secs = sd.secs;
                sb.Click += (s, e) => { SeekBy(secs); Poke(); };
                skipBtns.Add(sb);
            }
            muteBtn = new Button { Content = "🔊", Style = Ui.TransparentButton(), FontSize = 22, ToolTip = Lang.T("Silenciar") };
            muteBtn.Click += (s, e) => { ToggleMute(); Poke(); };
            // volumen visual
            var volBar = new Border { Width = 90, Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.BarBg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0), Cursor = Cursors.Hand };
            volFill = new Border { Background = Ui.B("#FFD500"), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 90 * vol / 100 };
            timeLabel = new TextBlock { Text = "00:00/00:00", FontSize = 13, Foreground = Ui.B("#C4D0D8"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            volBar.Child = volFill;
            volBar.MouseLeftButtonDown += (s, e) =>
            {
                var p = e.GetPosition(volBar);
                vol = 100 * Math.Max(0, Math.Min(1, p.X / 90));
                ApplyVolume();
                Poke();
            };
            row.Children.Add(playBtn);
            foreach (var sb in skipBtns) row.Children.Add(sb);
            row.Children.Add(muteBtn);
            row.Children.Add(volBar);
            row.Children.Add(timeLabel);
            row.Children.Add(new Border { Width = 10 });
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var aspectBtn = new Button { Content = "🖵", Style = Ui.TransparentButton(), FontSize = 22, ToolTip = Lang.T("Relación de aspecto") };
            aspectBtn.Click += (s, e) => { CycleAspect(); Poke(); };
            var audioBtn = new Button { Content = "🎧", Style = Ui.TransparentButton(), FontSize = 22, ToolTip = Lang.T("Pista de audio") };
            audioBtn.Click += (s, e) => { ToggleMenu("audio"); Poke(); };
            var ccBtn = new Button { Style = Ui.TransparentButton(), ToolTip = Lang.T("Subtítulos"), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
            var ccIcon = new Canvas { Width = 30, Height = 27, Background = Brushes.Transparent };
            var ccOval = new Ellipse { Width = 28, Height = 20, Stroke = Brushes.White, StrokeThickness = 2.4 };
            Canvas.SetLeft(ccOval, 1); Canvas.SetTop(ccOval, 0);
            var ccTailFig = new PathFigure { StartPoint = new Point(9, 16.5), IsClosed = false };
            ccTailFig.Segments.Add(new LineSegment(new Point(6, 24), true));
            ccTailFig.Segments.Add(new LineSegment(new Point(14.5, 18), true));
            var ccTail = new Path
            {
                Data = new PathGeometry(new[] { ccTailFig }), Stroke = Brushes.White,
                StrokeThickness = 2.4, StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            };
            ccIcon.Children.Add(ccOval); ccIcon.Children.Add(ccTail);
            ccBtn.Content = ccIcon;
            ccBtn.Click += (s, e) => { ToggleMenu("subs"); Poke(); };
            rateBtn = new Button { Content = "1x", Style = Ui.TransparentButton(), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, ToolTip = Lang.T("Velocidad de reproducción"), Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
            rateBtn.Click += (s, e) => { CycleRate(); Poke(); };
            var fsBtn = new Button { Content = "⛶", Style = Ui.TransparentButton(), FontSize = 22, ToolTip = Lang.T("Pantalla completa") };
            fsBtn.Click += (s, e) => ToggleFullscreen();
            var vlcBtn = new Button { Content = "VLC", Style = Ui.TransparentButton(), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, ToolTip = Lang.T("Abrir en VLC"), Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
            vlcBtn.Click += (s, e) => { SendToVlc(); Poke(); };
            recBtn = new Button { Content = "\u23FA", Style = Ui.TransparentButton(), FontSize = 20, Foreground = Brushes.White, ToolTip = Lang.T("Grabar"), Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Visibility = isLive ? Visibility.Visible : Visibility.Collapsed };
            recBtn.Click += (s, e) => { ToggleRec(); Poke(); };
            right.Children.Add(aspectBtn);
            right.Children.Add(audioBtn);
            right.Children.Add(ccBtn);
            right.Children.Add(rateBtn);
            right.Children.Add(recBtn);
            right.Children.Add(vlcBtn);
            right.Children.Add(fsBtn);
            var rowDock = new DockPanel();
            DockPanel.SetDock(right, Dock.Right);
            rowDock.Children.Add(right);
            rowDock.Children.Add(row);
            botSp.Children.Add(rowDock);
            osdBottom.Child = botSp;

            // Panel del menú (audio / subtítulos), arriba del OSD
            menuTitle = new TextBlock { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Ui.Text, Margin = new Thickness(0, 0, 0, 8) };
            menuContent = new StackPanel();
            var menuScroll = new ScrollViewer { Content = menuContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 400 };
            var menuInner = new StackPanel();
            menuInner.Children.Add(menuTitle);
            menuInner.Children.Add(menuScroll);
            menuPanel = new Border
            {
                Background = Ui.Panel,
                BorderBrush = Ui.Brd,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Width = 340,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 28, 130),
                Visibility = Visibility.Collapsed
            };
            menuPanel.Child = menuInner;

            // Capa de superposición con spinner, pausa grande y OSD.
            // En modo real va DENTRO del VideoView: LibVLCSharp la muestra en una
            // ventana transparente sobre el video (resuelve el airspace de WPF).
            // El fondo casi-transparente hace que reciba el mouse sin tapar el video.
            var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
            overlay.Children.Add(spinner);
            overlay.Children.Add(bigPause);
            overlay.Children.Add(osdTop);
            overlay.Children.Add(osdBottom);
            overlay.Children.Add(menuPanel);
            // Posición al pasar el mouse sobre la barra de tiempo
            var hoverBox = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 10, 10, 10)),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4, 8, 4),
                Visibility = Visibility.Collapsed, IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
            };
            var hoverTxt = new TextBlock { FontSize = 12, Foreground = Brushes.White };
            hoverBox.Child = hoverTxt;
            overlay.Children.Add(hoverBox);
            seekBar.MouseEnter += (s, e) => { if (CanSeek()) hoverBox.Visibility = Visibility.Visible; };
            seekBar.MouseLeave += (s, e) => hoverBox.Visibility = Visibility.Collapsed;
            seekBar.MouseMove += (s, e) =>
            {
                if (!CanSeek() || seekBar.ActualWidth <= 0) return;
                double frac = Math.Max(0, Math.Min(1, e.GetPosition(seekBar).X / seekBar.ActualWidth));
                hoverTxt.Text = HoverText(frac);
                var p = seekBar.TranslatePoint(new Point(e.GetPosition(seekBar).X, 0), overlay);
                hoverBox.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double bw = hoverBox.DesiredSize.Width, bh = hoverBox.DesiredSize.Height;
                double bx = Math.Max(0, Math.Min(p.X - bw / 2, Math.Max(0, overlay.ActualWidth - bw)));
                hoverBox.Margin = new Thickness(bx, Math.Max(0, p.Y - bh - 10), 0, 0);
            };
            // Reloj permanente (segundo cuadrante, debajo del EN VIVO): se puede arrastrar
            clockFace = new ClockFace();
            clockFace.SetTime(DateTime.Now.ToString("HH:mm"));
            var clockHost = new ContentControl
            {
                Content = clockFace,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = PlaybackSettings.ShowClock ? Visibility.Visible : Visibility.Collapsed,
                Cursor = Cursors.SizeAll
            };
            if (PlaybackSettings.ClockX >= 0)
            {
                clockHost.HorizontalAlignment = HorizontalAlignment.Left;
                clockHost.Margin = new Thickness(PlaybackSettings.ClockX, PlaybackSettings.ClockY, 0, 0);
            }
            else
            {
                clockHost.HorizontalAlignment = HorizontalAlignment.Right;
                clockHost.Margin = new Thickness(0, live ? 78 : 24, 28, 0);
            }
            overlay.Children.Add(clockHost);
            clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            clockTimer.Tick += (s, e) => { if (!clockShowingDate) clockFace.SetTime(DateTime.Now.ToString("HH:mm")); };
            clockTimer.Start();
            // Fecha completa al pasar 2 s sobre el reloj
            clockHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            clockHoverTimer.Tick += (s, e) =>
            {
                clockHoverTimer.Stop();
                if (clockDragging) return;
                clockShowingDate = true;
                clockFace.FontSizePx = 30;
                clockFace.SetTime(DateTime.Now.ToString("dddd, d 'de' MMMM 'de' yyyy"));
            };
            clockHost.MouseEnter += (s, e) => { if (!clockDragging) clockHoverTimer.Start(); };
            clockHost.MouseLeave += (s, e) =>
            {
                clockHoverTimer.Stop();
                if (clockShowingDate)
                {
                    clockShowingDate = false;
                    clockFace.FontSizePx = 52;
                    clockFace.SetTime(DateTime.Now.ToString("HH:mm"));
                }
            };
            // Arrastrar el reloj
            clockHost.PreviewMouseLeftButtonDown += (s, e) =>
            {
                clockHoverTimer.Stop();
                if (clockShowingDate)
                {
                    clockShowingDate = false;
                    clockFace.FontSizePx = 52;
                    clockFace.SetTime(DateTime.Now.ToString("HH:mm"));
                }
                if (infoPanel != null && infoPanel.Visibility == Visibility.Visible)
                {
                    infoPanel.Visibility = Visibility.Collapsed;
                    infoTimer?.Stop();
                }
                clockDragging = true;
                clockDragOffset = e.GetPosition(clockHost);
                clockHost.CaptureMouse();
                e.Handled = true;
            };
            clockHost.PreviewMouseMove += (s, e) =>
            {
                if (!clockDragging) return;
                var p = e.GetPosition(overlay);
                clockHost.HorizontalAlignment = HorizontalAlignment.Left;
                clockHost.VerticalAlignment = VerticalAlignment.Top;
                double x = Math.Max(0, Math.Min(p.X - clockDragOffset.X, Math.Max(0, overlay.ActualWidth - clockHost.ActualWidth)));
                double y = Math.Max(0, Math.Min(p.Y - clockDragOffset.Y, Math.Max(0, overlay.ActualHeight - 60)));
                clockHost.Margin = new Thickness(x, y, 0, 0);
            };
            clockHost.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (!clockDragging) return;
                clockDragging = false;
                clockHost.ReleaseMouseCapture();
                PlaybackSettings.ClockX = clockHost.Margin.Left;
                PlaybackSettings.ClockY = clockHost.Margin.Top;
                e.Handled = true;
            };
            // Panel de info (primer cuadrante): 15 s al abrir, click lo quita
            infoPanel = BuildInfoPanel(kind);
            if (infoPanel != null)
            {
                overlay.Children.Add(infoPanel);
                infoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                infoTimer.Tick += (s, e) => { infoPanel.Visibility = Visibility.Collapsed; infoTimer.Stop(); };
                infoTimer.Start();
            }
            // Imagen de intro (VOD): se muestra hasta que arranca el video
            if (!isLive && VodSettings.ShowIntros)
            {
                try
                {
                    string ip = System.IO.Path.Combine(AppContext.BaseDirectory, "intro.jpg");
                    if (System.IO.File.Exists(ip))
                    {
                        introImg = new Image
                        {
                            Source = new BitmapImage(new Uri(ip)),
                            Stretch = Stretch.UniformToFill,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch
                        };
                        overlay.Children.Add(introImg);
                    }
                }
                catch { }
            }
            if (real) videoView.Content = overlay;
            else root.Children.Add(overlay);

            // Interacciones OSD
            overlay.MouseMove += (s, e) => Poke();
            overlay.MouseRightButtonDown += (s, e) => { e.Handled = true; ShowPlayerMenu(); Poke(); };
            overlay.MouseLeftButtonDown += (s, e) =>
            {
                if (infoPanel != null && infoPanel.Visibility == Visibility.Visible)
                {
                    infoPanel.Visibility = Visibility.Collapsed;
                    infoTimer?.Stop();
                    return;
                }
                if (menuPanel.Visibility == Visibility.Visible)
                {
                    // clic dentro del menú: no alternar el OSD
                    DependencyObject m = e.OriginalSource as DependencyObject;
                    while (m != null && m != menuPanel) m = VisualTreeHelper.GetParent(m);
                    if (m == menuPanel) return;
                    HideMenu();
                }
                DependencyObject d = e.OriginalSource as DependencyObject;
                while (d != null && !(d is Button)) d = VisualTreeHelper.GetParent(d);
                if (d != null) return;
                if (osdBottom.Visibility == Visibility.Visible) ShowOsd(false);
                else Poke();
            };
            bigPause.MouseLeftButtonDown += (s, e) => { TogglePlay(); Poke(); };
            // Escape también cuando el foco está en la ventana del reproductor
            overlay.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { OnEscapeKey(); e.Handled = true; }
            };

            Content = root;

            tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            tick.Tick += (s, e) =>
            {
                if (!real && playing && !isLive) t = Math.Min(dur, t + 1);
                Upd();
                if (real && !isLive && mp != null && ++saveCounter >= 10)
                {
                    saveCounter = 0;
                    try
                    {
                        if (mp.IsPlaying && mp.Length > 0)
                            WatchHistory.RecordProgress(streamUrl, mp.Time, mp.Length);
                    }
                    catch { }
                }
            };
            hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3200) };
            hideTimer.Tick += (s, e) => { hideTimer.Stop(); if (playing) ShowOsd(false); };

            if (real) StartReal(url);
            else
            {
                loadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
                loadTimer.Tick += (s, e) => { loadTimer.Stop(); spinner.Visibility = Visibility.Collapsed; playing = true; playBtn.Content = "⏸"; bigPause.Visibility = Visibility.Collapsed; tick.Start(); Poke(); };
                loadTimer.Start();
            }
            ApplyVolume();
            Upd();
        }

        void TrySeek()
        {
            try
            {
                if (pendingSeek > 3000 && mp != null && mp.Length > 0)
                {
                    mp.Time = Math.Min(pendingSeek, mp.Length - 2000);
                    pendingSeek = 0;
                }
            }
            catch { }
        }

        void Disp(Action a)
        {
            // BeginInvoke (no Invoke): los eventos de VLC llegan en sus propios hilos;
            // si el hilo de UI estuviera dentro de una llamada a VLC, Invoke se interbloquea
            try { Dispatcher.BeginInvoke(a); } catch { }
        }

        string[] VlcOptions()
        {
            var list = new List<string> { "--no-video-title-show" };
            if (PlaybackSettings.Decoder == "sw") list.Add("--avcodec-hw=none");
            list.AddRange(SubStyle.LibVlcOptions());
            return list.ToArray();
        }

        void StartReal(string url)
        {
            EnsureVlc();
            if (!vlcOk)
            {
                real = false;
                loadText.Text = Lang.T("No se pudo iniciar el reproductor");
                return;
            }
            try
            {
                CreatePlayer();
                if (isLive && PlaybackSettings.TimeshiftMin > 0)
                {
                    loadText.Text = Lang.T("Preparando timeshift\u2026");
                    Task.Run(() =>
                    {
                        var t = new Timeshift();
                        bool ok = false;
                        try { ok = t.Start(url, PlaybackSettings.TimeshiftMin); } catch { }
                        Disp(() =>
                        {
                            if (mp == null) { t.Dispose(); return; }
                            if (ok) ts = t; else { t.Dispose(); Nav.Toast(Lang.T("Timeshift no disponible")); }
                            PlayMedia(EffectiveUrl());
                            ApplyVolume();
                        });
                    });
                }
                else
                {
                    PlayMedia(url);
                    ApplyVolume();
                }
            }
            catch
            {
                real = false;
                loadText.Text = Lang.T("No se pudo iniciar el reproductor");
            }
        }

        void CreatePlayer()
        {
            libvlc = new LibVLC(VlcOptions());
            mp = new VlcPlayer(libvlc);
            muted = false;
            mp.Mute = false; // al abrir, el sonido siempre activado
            playRate = 1.0;
            if (rateBtn != null) rateBtn.Content = "1x";
            videoView.MediaPlayer = mp;
            HookPlayerEvents();
        }

        void PlayMedia(string url)
        {
            var media = new Media(libvlc, url, FromType.FromLocation);
            media.AddOption(":network-caching=" + PlaybackSettings.BufferMs);
            mp.Play(media);
            media.Dispose();
        }

        string EffectiveUrl() => ts != null && ts.LocalUrl != null ? ts.LocalUrl : streamUrl;

        // Vuelve al borde en vivo (re-sintoniza el timeshift local)
        // Panel con los detalles del programa actual (TV) o del contenido (VOD)
        Border BuildInfoPanel(string kind)
        {
            string kicker, title, meta, desc = null;
            infoProg = null;
            if (isLive)
            {
                var ch = ContentStore.Channels.FirstOrDefault(c => c.StreamUrl == streamUrl);
                if (ch == null) return null;
                var (now, _) = EpgStore.GetNowNext(ch);
                if (now == null) return null;
                infoProg = now;
                kicker = Lang.T("Ahora");
                title = now.Title;
                meta = now.StartUtc.ToLocalTime().ToString("HH:mm") + " \u2013 " + now.StopUtc.ToLocalTime().ToString("HH:mm");
                desc = now.Desc;
            }
            else
            {
                kicker = kind == "movie" ? Lang.T("Pel\u00edcula") : Lang.T("Serie");
                title = chName;
                meta = chSub;
            }
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = kicker.ToUpperInvariant(), FontSize = 12, Foreground = Ui.B("#8FA3B3") });
            sp.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Margin = new Thickness(0, 6, 0, 0) });
            sp.Children.Add(new TextBlock { Text = meta, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ui.B("#FFD500"), Margin = new Thickness(0, 4, 0, 0) });
            if (!string.IsNullOrEmpty(desc))
                sp.Children.Add(new TextBlock { Text = desc, FontSize = 14, Foreground = Ui.B("#C4D0D8"), TextWrapping = TextWrapping.Wrap, MaxWidth = 360, MaxHeight = 66, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 10, 0, 0) });
            var pbar = new Border { Height = 5, Width = 364, Background = Ui.B("#3A3A3A"), CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 14, 0, 0) };
            infoProgFill = new Border { Background = Ui.B("#FFD500"), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            pbar.Child = infoProgFill;
            sp.Children.Add(pbar);
            infoRemain = new TextBlock { FontSize = 12, Foreground = Ui.B("#8FA3B3"), Margin = new Thickness(0, 6, 0, 0) };
            sp.Children.Add(infoRemain);
            UpdInfoPanel();
            return new Border
            {
                Child = sp,
                Background = new SolidColorBrush(Color.FromArgb(184, 10, 10, 10)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18), Width = 400,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(28, 96, 0, 0)
            };
        }

        void ShowInfo()
        {
            if (infoPanel == null) { Nav.Toast(Lang.T("Sin información disponible")); return; }
            UpdInfoPanel();
            infoPanel.Visibility = Visibility.Visible;
            try { infoTimer?.Stop(); } catch { }
            infoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            infoTimer.Tick += (s, e) => { infoPanel.Visibility = Visibility.Collapsed; try { infoTimer.Stop(); } catch { } };
            infoTimer.Start();
        }

        void ShowPlayerMenu()
        {
            var cm = new ContextMenu { Background = Ui.Panel, BorderBrush = Ui.Brd, Foreground = Ui.Text };
            void Add(string label, Action act)
            {
                var mi = new MenuItem { Header = label };
                mi.Click += (s2, e2) => { act(); Poke(); };
                cm.Items.Add(mi);
            }
            Add(playing ? Lang.T("Pausar") : Lang.T("Reproducir"), () => TogglePlay());
            foreach (var sd in SkipDefs)
            {
                if (!PlaybackSettings.IsSkipVisible(sd.id)) continue;
                int secs = sd.secs;
                Add(sd.name, () => SeekBy(secs));
            }
            Add(muted ? Lang.T("Activar sonido") : Lang.T("Silenciar"), () => ToggleMute());
            Add(Lang.T("Subir volumen"), () => SetVol(vol + 10));
            Add(Lang.T("Bajar volumen"), () => SetVol(vol - 10));
            Add(Lang.T("Relación de aspecto"), () => CycleAspect());
            Add(Lang.T("Pista de audio"), () => ToggleMenu("audio"));
            Add(Lang.T("Subtítulos"), () => ToggleMenu("subs"));
            Add(Lang.T("Velocidad") + $" ({rateBtn?.Content})", () => CycleRate());
            if (isLive) Add(Recording.IsManualRecording ? Lang.T("Detener grabación") : Lang.T("Grabar"), () => ToggleRec());
            Add(Lang.T("Abrir en VLC"), () => SendToVlc());
            Add(Lang.T("Pantalla completa"), () => ToggleFullscreen());
            Add(Lang.T("Info"), () => ShowInfo());
            cm.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            cm.IsOpen = true;
        }

        void UpdInfoPanel()
        {
            if (infoProgFill == null || infoRemain == null) return;
            double prog = 0; long remainMs = 0;
            if (infoProg != null)
            {
                var now = DateTime.UtcNow;
                double total = (infoProg.StopUtc - infoProg.StartUtc).TotalMilliseconds;
                if (total > 0) prog = Math.Max(0, Math.Min(1, (now - infoProg.StartUtc).TotalMilliseconds / total));
                remainMs = Math.Max(0, (long)(infoProg.StopUtc - now).TotalMilliseconds);
            }
            else if (dur > 0)
            {
                prog = Math.Max(0, Math.Min(1, t / dur));
                remainMs = Math.Max(0, (long)((dur - t) * 1000));
            }
            infoProgFill.Width = 364 * prog;
            long mins = remainMs / 60000;
            infoRemain.Text = mins >= 60
                ? Lang.T("Quedan") + " " + (mins / 60) + "h " + (mins % 60).ToString("00") + " min"
                : Lang.T("Quedan") + " " + mins + " min";
        }

        void GoLive()
        {
            if (ts == null || mp == null) return;
            lastMoveAt = DateTime.UtcNow;
            tsDelayMs = 0;
            tsPauseAt = default;
            try { PlayMedia(EffectiveUrl()); } catch { }
        }

        void StopTimeshift()
        {
            if (ts != null) { try { ts.Dispose(); } catch { } ts = null; }
            tsDelayMs = 0;
            tsPauseAt = default;
        }

        void HookPlayerEvents()
        {
                mp.Playing += (s, e) => Disp(() =>
                {
                    spinner.Visibility = Visibility.Collapsed;
                    if (introImg != null) introImg.Visibility = Visibility.Collapsed;
                    loadText.Text = Lang.T("Cargando..."); // que no quede el texto viejo si re-bufferiza al mover
                    playing = true; playBtn.Content = "⏸"; bigPause.Visibility = Visibility.Collapsed;
                    if (restorePending != null)
                    {
                        var r = restorePending.Value;
                        restorePending = null;
                        try
                        {
                            if (!string.IsNullOrEmpty(externalSubPath))
                                mp.AddSlave(MediaSlaveType.Subtitle, new Uri(externalSubPath).AbsoluteUri, r.extSel);
                            if (!r.extSel && r.spu >= 0) mp.SetSpu(r.spu);
                            if (!isLive && PlaybackSettings.AutoResume && r.time > 3000 && mp.Length > 0)
                                mp.Time = Math.Min(r.time, mp.Length - 2000);
                        }
                        catch { }
                    }
                    Poke();
                    TrySeek();
                });
                mp.Paused += (s, e) => Disp(() =>
                {
                    playing = false; playBtn.Content = "▶️"; bigPause.Visibility = Visibility.Visible;
                });
                mp.Stopped += (s, e) => Disp(() =>
                {
                    playing = false; playBtn.Content = "▶️";
                });
                mp.EncounteredError += (s, e) => Disp(() =>
                {
                    loadText.Text = Lang.T("No se pudo reproducir este contenido");
                });
                mp.Buffering += (s, e) => Disp(() =>
                {
                    if (e.Cache < 100 && playing && (DateTime.UtcNow - lastMoveAt).TotalSeconds > 2) spinner.Visibility = Visibility.Visible;
                    else if (e.Cache >= 100) spinner.Visibility = Visibility.Collapsed;
                });
                mp.LengthChanged += (s, e) => Disp(() =>
                {
                    if (mp.Length > 0) dur = mp.Length / 1000.0;
                    Upd();
                    TrySeek();
                });
                mp.TimeChanged += (s, e) => Disp(() =>
                {
                    t = mp.Time / 1000.0;
                    Upd();
                });
        }

        void TogglePlay()
        {
            if (mp != null)
            {
                if (mp.IsPlaying)
                {
                    if (isLive && ts != null) tsPauseAt = DateTime.UtcNow;
                    mp.Pause();
                }
                else
                {
                    if (isLive && ts != null && tsPauseAt != default)
                    {
                        tsDelayMs += (long)(DateTime.UtcNow - tsPauseAt).TotalMilliseconds;
                        tsDelayMs = Math.Min(tsDelayMs, ts.LiveEdgeMs);
                        tsPauseAt = default;
                    }
                    mp.Play();
                }
                return;
            }
            playing = !playing;
            playBtn.Content = playing ? "⏸" : "▶️";
            bigPause.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        }

        bool CanSeek() => !isLive || ts != null;

        string HoverText(double frac)
        {
            if (isLive && ts != null) return "-" + Fmt((1 - frac) * ts.LiveEdgeMs / 1000);
            double dd = (mp != null && mp.Length > 0) ? mp.Length / 1000.0 : dur;
            return Fmt(frac * dd);
        }

        // Salto absoluto en la barra: fracción 0 = más atrás, 1 = en vivo
        void SeekToFraction(double frac)
        {
            if (Recording.IsManualRecording) { Nav.Toast(Lang.T("Operación inválida mientras se graba")); return; }
            if (isLive)
            {
                if (ts == null || mp == null) return;
                lastMoveAt = DateTime.UtcNow;
                long target = (long)((1 - frac) * ts.LiveEdgeMs);
                target = Math.Max(0, Math.Min(ts.LiveEdgeMs, target));
                long dMs = target - tsDelayMs; // >0 = ir más atrás
                if (dMs != 0) mp.Time = Math.Max(0, mp.Time - dMs);
                tsDelayMs = target;
                if (tsPauseAt != default) tsPauseAt = DateTime.UtcNow;
            }
            else
            {
                if (mp != null && mp.Length > 0) mp.Time = (long)(frac * mp.Length);
                else t = dur * frac;
            }
        }

        void SeekBy(int secs)
        {
            if (Recording.IsManualRecording) { Nav.Toast(Lang.T("Operación inválida mientras se graba")); return; }
            if (isLive && ts == null) return;
            lastMoveAt = DateTime.UtcNow;
            if (mp != null) mp.Time = Math.Max(0, mp.Time + secs * 1000L);
            else t = Math.Max(0, Math.Min(dur, t + secs));
            if (isLive && ts != null)
            {
                if (secs < 0) tsDelayMs -= (long)secs * 1000;
                else tsDelayMs = Math.Max(0, tsDelayMs - (long)secs * 1000);
                tsDelayMs = Math.Min(tsDelayMs, ts.LiveEdgeMs);
            }
            Upd();
        }

        void ToggleMute()
        {
            muted = !muted;
            if (mp != null) mp.Mute = muted;
            muteBtn.Content = muted ? "🔇" : "🔊";
            Nav.Toast(muted ? Lang.T("Silenciado") : Lang.T("Sonido activado"));
        }

        void ApplyVolume()
        {
            volFill.Width = 90 * vol / 100;
            muted = vol <= 1;
            if (mp != null) { mp.Volume = (int)vol; mp.Mute = muted; }
            muteBtn.Content = muted ? "🔇" : "🔊";
        }

        void CycleAspect()
        {
            aspect = (aspect + 1) % aspects.Length;
            if (mp != null)
                mp.AspectRatio = aspects[aspect] == "Llenar pantalla" ? "" : aspects[aspect];
            Nav.Toast(Lang.T("Aspecto: ") + Lang.T(aspects[aspect]));
        }

        // ---------- Menú de pista de audio ----------
        void ShowAudioMenu()
        {
            menuMode = "audio";
            menuTitle.Text = Lang.T("Pista de audio");
            menuContent.Children.Clear();
            try
            {
                var tracks = mp.AudioTrackDescription.Where(x => x.Id >= 0).ToArray();
                if (tracks.Length == 0)
                    menuContent.Children.Add(MenuNote(Lang.T("Sin pistas de audio")));
                int cur = mp.AudioTrack;
                foreach (var tr in tracks)
                {
                    var t = tr;
                    menuContent.Children.Add(MenuRow(t.Name, t.Id == cur, () =>
                    {
                        mp.SetAudioTrack(t.Id);
                        Nav.Toast("Audio: " + t.Name);
                        ShowAudioMenu();
                    }));
                }
            }
            catch { menuContent.Children.Add(MenuNote(Lang.T("Sin pistas de audio"))); }
            menuPanel.Visibility = Visibility.Visible;
            Poke();
        }

        // ---------- Menú de subtítulos ----------
        void ShowSubMenu()
        {
            menuMode = "subs";
            menuTitle.Text = Lang.T("Subtítulos");
            menuContent.Children.Clear();

            menuContent.Children.Add(MenuSection(Lang.T("Pista")));
            try
            {
                var tracks = mp.SpuDescription.Where(x => x.Id >= 0).ToArray();
                int cur = mp.Spu;
                menuContent.Children.Add(MenuRow(Lang.T("Desactivados"), cur < 0, () =>
                {
                    mp.SetSpu(-1);
                    externalSubSelected = false;
                    Nav.Toast("Subtítulos: desactivados");
                    ShowSubMenu();
                }));
                foreach (var tr in tracks)
                {
                    var t = tr;
                    menuContent.Children.Add(MenuRow(t.Name, t.Id == cur, () =>
                    {
                        mp.SetSpu(t.Id);
                        externalSubSelected = false;
                        Nav.Toast("Subtítulos: " + t.Name);
                        ShowSubMenu();
                    }));
                }
            }
            catch { menuContent.Children.Add(MenuNote(Lang.T("Sin subtítulos disponibles"))); }
            var extBtn = Ui.ActionButton("📂 Cargar subtítulo externo…", LoadExternalSub);
            extBtn.Margin = new Thickness(0, 8, 0, 6);
            extBtn.FontSize = 14;
            menuContent.Children.Add(extBtn);

            menuContent.Children.Add(MenuSection(Lang.T("Tamaño de fuente")));
            menuContent.Children.Add(ChipRow(
                new (string, int)[] { (Lang.T("Pequeño"), 18), (Lang.T("Normal"), 16), (Lang.T("Grande"), 12), (Lang.T("Muy grande"), 6) },
                SubStyle.RelFontSize,
                v => { SubStyle.RelFontSize = v; SubStyle.Save(); RecreateForStyle(); ShowSubMenu(); }));

            menuContent.Children.Add(MenuSection(Lang.T("Color de fuente")));
            menuContent.Children.Add(SwatchRow(
                new (string, int)[] { (Lang.T("Blanco"), 0xFFFFFF), (Lang.T("Amarillo"), 0xFFFF00), (Lang.T("Verde"), 0x00FF00), (Lang.T("Cian"), 0x00FFFF), (Lang.T("Rosa"), 0xFF00FF), (Lang.T("Naranja"), 0xFF8000) },
                SubStyle.FontColor,
                v => { SubStyle.FontColor = v; SubStyle.Save(); RecreateForStyle(); ShowSubMenu(); }));

            menuContent.Children.Add(MenuSection(Lang.T("Color del fondo")));
            menuContent.Children.Add(SwatchRow(
                new (string, int)[] { (Lang.T("Negro"), 0x000000), (Lang.T("Blanco"), 0xFFFFFF), (Lang.T("Gris"), 0x808080), (Lang.T("Azul"), 0x000080), (Lang.T("Rojo"), 0x800000) },
                SubStyle.BgColor,
                v =>
                {
                    SubStyle.BgColor = v;
                    if (SubStyle.BgOpacity == 0) SubStyle.BgOpacity = 255; // con color elegido, el fondo se ve
                    SubStyle.Save(); RecreateForStyle(); ShowSubMenu();
                }));

            menuContent.Children.Add(MenuSection(Lang.T("Opacidad del fondo")));
            menuContent.Children.Add(ChipRow(
                new (string, int)[] { ("0%", 0), ("25%", 64), ("50%", 128), ("75%", 192), ("100%", 255) },
                SubStyle.BgOpacity,
                v => { SubStyle.BgOpacity = v; SubStyle.Save(); RecreateForStyle(); ShowSubMenu(); }));

            menuPanel.Visibility = Visibility.Visible;
            Poke();
        }

        void LoadExternalSub()
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = Lang.T("Cargar subtítulo externo"),
                    Filter = "Subtítulos|*.srt;*.ass;*.ssa;*.vtt;*.sub|Todos los archivos|*.*"
                };
                if (dlg.ShowDialog() == true)
                {
                    externalSubPath = dlg.FileName;
                    bool ok = mp.AddSlave(MediaSlaveType.Subtitle, new Uri(externalSubPath).AbsoluteUri, true);
                    externalSubSelected = ok;
                    Nav.Toast(ok ? Lang.T("Subtítulo externo cargado") : Lang.T("No se pudo cargar el subtítulo"));
                    ShowSubMenu();
                }
            }
            catch (Exception ex) { Nav.Toast("Error: " + ex.Message); }
        }

        // Reaplica el estilo recreando el reproductor con las opciones nuevas
        // (las opciones freetype son configuración de instancia: así llegan seguro).
        // El trabajo pesado corre en segundo plano; el intercambio final en el hilo de UI.
        readonly object recreateLock = new object();

        void RecreateForStyle()
        {
            var oldMp = mp;
            var oldVlc = libvlc;
            if (oldMp == null || oldVlc == null || string.IsNullOrEmpty(streamUrl)) return;
            long savedTime = 0;
            int savedSpu = -1;
            try { savedTime = oldMp.Time; savedSpu = oldMp.Spu; } catch { }
            bool extSel = externalSubSelected;
            restorePending = (savedTime, savedSpu, extSel);
            Nav.Toast("Aplicando estilo de subtítulos…");
            Task.Run(() =>
            {
                lock (recreateLock)
                {
                    LibVLC newVlc = null;
                    VlcPlayer newMp = null;
                    try
                    {
                        try { oldMp.Stop(); } catch { }
                        var sw = Stopwatch.StartNew();
                        try { while (oldMp.IsPlaying && sw.ElapsedMilliseconds < 2000) Thread.Sleep(50); } catch { }
                        newVlc = new LibVLC(VlcOptions());
                        newMp = new VlcPlayer(newVlc);
                        Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                videoView.MediaPlayer = null;
                                try { oldMp.Dispose(); } catch { }
                                try { oldVlc.Dispose(); } catch { }
                                libvlc = newVlc;
                                mp = newMp;
                                videoView.MediaPlayer = newMp;
                                HookPlayerEvents();
                                tsDelayMs = 0;
                                tsPauseAt = default;
                                PlayMedia(EffectiveUrl());
                                ApplyVolume();
                            }
                            catch
                            {
                                restorePending = null;
                                try { newMp?.Dispose(); } catch { }
                                try { newVlc?.Dispose(); } catch { }
                            }
                        });
                    }
                    catch
                    {
                        restorePending = null;
                        try { newMp?.Dispose(); } catch { }
                        try { newVlc?.Dispose(); } catch { }
                    }
                }
            });
        }

        // ---------- Piezas del menú ----------
        TextBlock MenuSection(string t) => new TextBlock
        {
            Text = t.ToUpper(),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Ui.Gray,
            Margin = new Thickness(0, 8, 0, 6)
        };

        TextBlock MenuNote(string t) => new TextBlock
        {
            Text = t,
            FontSize = 14,
            Foreground = Ui.Gray,
            Margin = new Thickness(0, 0, 0, 6)
        };

        Border MenuRow(string text, bool selected, Action onClick)
        {
            var b = new Border
            {
                Background = selected ? Ui.Panel2 : Brushes.Transparent,
                BorderBrush = selected ? Ui.Red : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 2),
                Cursor = Cursors.Hand
            };
            b.Child = new TextBlock
            {
                Text = (selected ? "● " : "○ ") + text,
                FontSize = 14,
                Foreground = Ui.Text,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; onClick?.Invoke(); };
            b.MouseEnter += (s, e) => { if (!selected) b.Background = Ui.Panel2; };
            b.MouseLeave += (s, e) => { if (!selected) b.Background = Brushes.Transparent; };
            return b;
        }

        WrapPanel ChipRow((string label, int value)[] opts, int current, Action<int> onPick)
        {
            var row = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            foreach (var (label, value) in opts)
            {
                int v = value;
                var b = new Button
                {
                    Content = label,
                    Margin = new Thickness(0, 0, 8, 6),
                    Padding = new Thickness(12, 6, 12, 6),
                    FontSize = 13,
                    Background = v == current ? Ui.Red : Ui.Panel2,
                    Foreground = v == current ? Brushes.White : Ui.Text,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                b.Click += (s, e) => { e.Handled = true; if (v != current) onPick(v); };
                row.Children.Add(b);
            }
            return row;
        }

        WrapPanel SwatchRow((string label, int value)[] opts, int current, Action<int> onPick)
        {
            var row = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            foreach (var (label, value) in opts)
            {
                int v = value;
                bool sel = v == current;
                var b = new Border
                {
                    Width = 34,
                    Height = 34,
                    CornerRadius = new CornerRadius(17),
                    Background = new SolidColorBrush(Color.FromRgb((byte)(v >> 16), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF))),
                    BorderBrush = sel ? Ui.Red : Ui.Brd,
                    BorderThickness = new Thickness(sel ? 3 : 1),
                    Margin = new Thickness(0, 0, 10, 8),
                    Cursor = Cursors.Hand,
                    ToolTip = label
                };
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; if (v != current) onPick(v); };
                row.Children.Add(b);
            }
            return row;
        }

        void ShowOsd(bool v)
        {
            osdTop.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
            osdBottom.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
            if (!v) HideMenu();
        }

        void HideMenu()
        {
            menuMode = null;
            if (menuPanel != null) menuPanel.Visibility = Visibility.Collapsed;
        }

        void ToggleMenu(string mode)
        {
            if (mp == null)
            {
                Nav.Toast(mode == "audio" ? Lang.T("Sin audio disponible") : Lang.T("Sin subtítulos disponibles"));
                return;
            }
            if (menuPanel.Visibility == Visibility.Visible && menuMode == mode) HideMenu();
            else if (mode == "audio") ShowAudioMenu();
            else ShowSubMenu();
        }

        void Poke()
        {
            ShowOsd(true);
            hideTimer.Stop();
            hideTimer.Start();
        }

        void ToggleFullscreen()
        {
            var w = Nav.Main;
            if (!isFullscreen)
            {
                prevMaximized = w.WindowState == WindowState.Maximized;
                w.WindowStyle = WindowStyle.None;
                w.WindowState = WindowState.Maximized;
            }
            else RestoreChrome();
            isFullscreen = !isFullscreen;
        }

        void RestoreChrome()
        {
            var w = Nav.Main;
            w.WindowStyle = WindowStyle.SingleBorderWindow;
            w.WindowState = prevMaximized ? WindowState.Maximized : WindowState.Normal;
        }

        public override void OnEscapeKey()
        {
            if (isFullscreen) ToggleFullscreen();
            else GoBack();
        }

        void GoBack()
        {
            if (Recording.IsManualRecording)
            {
                Ui.Confirm(Lang.T("Grabación en curso"), Lang.T("Si sales se cancelará la grabación. ¿Continuar?"), Lang.T("Salir"), () => { Recording.StopManual(); GoBack(); });
                return;
            }
            if (isFullscreen) ToggleFullscreen();
            Nav.Back();
        }

        void Upd()
        {
            UpdRecBtn();
            double w = seekBar.ActualWidth;
            double pct;
            bool seekable = CanSeek();
            seekBar.Opacity = seekable ? 1 : 0.35;
            seekBar.Cursor = seekable ? Cursors.Hand : Cursors.Arrow;
            if (isLive && ts != null)
            {
                long dly = tsDelayMs;
                if (tsPauseAt != default) dly += (long)(DateTime.UtcNow - tsPauseAt).TotalMilliseconds;
                dly = Math.Min(dly, ts.LiveEdgeMs);
                pct = ts.LiveEdgeMs > 0 ? 1.0 - (double)dly / ts.LiveEdgeMs : 1;
            }
            else pct = dur > 0 ? Math.Min(1, t / dur) : 1;
            seekFill.Width = w * pct;
            seekBuf.Width = w * Math.Min(1, pct + 0.18);
            string cur, tot;
            if (isLive && ts != null)
            {
                long delay = tsDelayMs;
                if (tsPauseAt != default) delay += (long)(DateTime.UtcNow - tsPauseAt).TotalMilliseconds;
                delay = Math.Min(delay, ts.LiveEdgeMs);
                cur = delay > 2000 ? "-" + Fmt(delay / 1000) : Lang.T("EN VIVO");
                tot = Fmt(ts.LiveEdgeMs / 1000);
            }
            else if (isLive) { cur = Lang.T("EN VIVO"); tot = ""; }
            else { cur = Fmt(t); tot = Fmt(dur); }
            timeLabel.Text = tot == "" ? cur : cur + "/" + tot;
            if (infoPanel != null && infoPanel.Visibility == Visibility.Visible) UpdInfoPanel();
            if (isLive && liveBadgeText != null)
            {
                long shown = tsDelayMs;
                if (ts != null)
                {
                    if (tsPauseAt != default) shown += (long)(DateTime.UtcNow - tsPauseAt).TotalMilliseconds;
                    shown = Math.Min(shown, ts.LiveEdgeMs);
                }
                liveBadgeText.Text = ts != null && shown > 20000 ? "\u23EA -" + Fmt(shown / 1000) : Lang.T("EN VIVO");
            }
        }

        static string Fmt(double s)
        {
            int i = (int)s, h = i / 3600, m = (i % 3600) / 60, sec = i % 60;
            return h > 0 ? h + ":" + m.ToString("00") + ":" + sec.ToString("00")
                         : m.ToString("00") + ":" + sec.ToString("00");
        }

        // Atajos de teclado: ↑↓ volumen, ←→ ∓5 s, V enviar a VLC, F pantalla completa, I info
        public bool HandleKey(Key key)
        {
            if (menuPanel != null && menuPanel.Visibility == Visibility.Visible) return false;
            switch (key)
            {
                case Key.Up: SetVol(vol + 5); return true;
                case Key.Down: SetVol(vol - 5); return true;
                case Key.Left: SeekBy(-5); Poke(); return true;
                case Key.Right: SeekBy(5); Poke(); return true;
                case Key.V: SendToVlc(); return true;
                case Key.F: ToggleFullscreen(); Poke(); return true;
                case Key.I: ShowInfo(); Poke(); return true;
            }
            return false;
        }

        void SetVol(double v)
        {
            vol = Math.Max(0, Math.Min(100, v));
            ApplyVolume();
            Poke();
        }

        // Velocidad de reproducción: 0.5x -> 2x en ciclo
        void CycleRate()
        {
            int i = Array.IndexOf(Rates, playRate);
            playRate = Rates[(i + 1) % Rates.Length];
            if (mp != null) mp.SetRate((float)playRate);
            rateBtn.Content = playRate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";
        }

        // Rueda del mouse (viene del hook de bajo nivel de MainWindow)
        public bool WheelVol(int step)
        {
            if (menuPanel != null && menuPanel.Visibility == Visibility.Visible) return false;
            SetVol(vol + step);
            return true;
        }

        // Envía lo que se está viendo al VLC de escritorio y detiene la app
        void SendToVlc()
        {
            if (string.IsNullOrEmpty(streamUrl)) return;
            if (Recording.IsManualRecording)
            {
                Ui.Confirm(Lang.T("Grabación en curso"), Lang.T("Si sales se cancelará la grabación. ¿Continuar?"), Lang.T("Salir"), () => { Recording.StopManual(); SendToVlc(); });
                return;
            }
            if (!ExternalVlc.Play(streamUrl)) { Nav.Toast(Lang.T("VLC no encontrado")); return; }
            noBackground = true;
            try { if (mp != null) mp.Stop(); } catch { }
            StopTimeshift();
            Nav.Back();
        }

        void ToggleRec()
        {
            if (Recording.IsManualRecording)
            {
                Ui.Confirm(Lang.T("Detener grabación"), Lang.T("¿Detener la grabación en curso?"), Lang.T("Detener"), () =>
                {
                    Recording.StopManual();
                    Nav.Toast(Lang.T("Grabación guardada"));
                    UpdRecBtn();
                });
                return;
            }
            if (!isLive || string.IsNullOrEmpty(streamUrl)) return;
            // Nombre del programa actual (EPG) para el archivo
            string prog = "";
            try
            {
                var ch = ContentStore.Channels.FirstOrDefault(c => c.StreamUrl == streamUrl);
                if (ch != null)
                {
                    var (now, _) = EpgStore.GetNowNext(ch);
                    if (now != null) prog = now.Title;
                }
            }
            catch { }
            string path;
            if (ts != null && tsDelayMs > 2000)
                path = Recording.StartManualTimeshift(ts, tsDelayMs, chName, prog);
            else
                path = Recording.StartManualLive(streamUrl, chName, prog);
            if (path != null) Nav.Toast(Lang.T("Grabando..."));
            else Nav.Toast(Lang.T("No se pudo iniciar la grabación"));
            UpdRecBtn();
        }

        void UpdRecBtn()
        {
            if (recBtn == null) return;
            if (Recording.IsManualRecording)
            {
                var el = DateTime.UtcNow - Recording.ManualStartUtc;
                string t = el.TotalHours >= 1 ? $"{(int)el.TotalHours}:{el.Minutes:D2}:{el.Seconds:D2}" : $"{el.Minutes:D2}:{el.Seconds:D2}";
                recBtn.Content = "\u23FA " + t;
                recBtn.Foreground = Brushes.Red;
            }
            else
            {
                recBtn.Content = "\u23FA";
                recBtn.Foreground = Brushes.White;
            }
        }

        public override void OnLeave()
        {
            tick?.Stop();
            hideTimer?.Stop();
            loadTimer?.Stop();
            infoTimer?.Stop();
            clockTimer?.Stop();
            clockHoverTimer?.Stop();
            if (isFullscreen) RestoreChrome();
            isFullscreen = false;
            try
            {
                if (real && !isLive && mp != null && mp.Length > 0)
                    WatchHistory.RecordProgress(streamUrl, mp.Time, mp.Length);
                WatchHistory.SaveNow();
                if (videoView != null) videoView.MediaPlayer = null;
                if (PlaybackSettings.BackgroundPlay && mp != null && !noBackground)
                {
                    // Segundo plano: el audio sigue sonando; se corta al abrir otro elemento
                    StopBackground();
                    bg = new BgState { libvlc = libvlc, mp = mp, ts = ts };
                    libvlc = null; mp = null; ts = null;
                }
                else
                {
                    if (mp != null) { try { mp.Stop(); } catch { } mp.Dispose(); mp = null; }
                    if (libvlc != null) { libvlc.Dispose(); libvlc = null; }
                    StopTimeshift();
                }
                // Si queda una grabación manual activa al salir, detenerla
                if (Recording.IsManualRecording) Recording.StopManual();
                if (videoView != null) { try { videoView.Dispose(); } catch { } videoView = null; }
            }
            catch { }
        }
    }
}
