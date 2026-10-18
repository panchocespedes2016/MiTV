using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace MiTV
{
    public class MainWindow : Window
    {
        // Hook WH_KEYBOARD_LL: las teclas deben llegar aunque el foco esté en la
        // ventana transparente del overlay del video (no en la ventana principal)
        IntPtr kbHook = IntPtr.Zero;
        KbProc kbProc;
        delegate IntPtr KbProc(int nCode, IntPtr wParam, IntPtr lParam);

        void InstallKbHook()
        {
            kbProc = KbHookProc;
            using (var p = Process.GetCurrentProcess())
                kbHook = SetWindowsHookEx(13, kbProc, GetModuleHandle(p.MainModule.ModuleName), 0);
        }

        IntPtr KbHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            const int WM_KEYDOWN = 0x0100;
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN && IsOurAppForeground()
                && MainContent.Content is PlayerView pl)
            {
                var key = KeyInterop.KeyFromVirtualKey(Marshal.ReadInt32(lParam));
                if (key == Key.Escape) { pl.OnEscapeKey(); return (IntPtr)1; }
                if (pl.HandleKey(key)) return (IntPtr)1;
            }
            return CallNextHookEx(kbHook, nCode, wParam, lParam);
        }

        static bool IsOurAppForeground()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            return pid != 0 && pid == (uint)Process.GetCurrentProcess().Id;
        }

        // Rueda del mouse: tampoco llega a la ventana (el overlay la intercepta)
        IntPtr mouseHook = IntPtr.Zero;
        MouseProc mouseProc;
        delegate IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        void InstallMouseHook()
        {
            mouseProc = MouseHookProc;
            using (var p = Process.GetCurrentProcess())
                mouseHook = SetWindowsHookEx(14, mouseProc, GetModuleHandle(p.MainModule.ModuleName), 0);
        }

        IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEWHEEL && IsOurAppForeground()
                && MainContent.Content is PlayerView pl)
            {
                short delta = (short)(Marshal.ReadInt32(lParam, 8) >> 16);
                if (pl.WheelVol(delta > 0 ? 5 : -5)) return (IntPtr)1;
            }
            return CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, KbProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, MouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Barra de título oscura (Windows 10/11)
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int dark = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
            }
            catch { }
        }
        public ContentControl MainContent;
        private Border pill, tabBar;
        private UniformGrid tabsGrid;
        private Button guideTabBtn;
        private TextBlock[] tabLabels;
        private string[] tabKeys;
        private TextBlock[] tabIcons;
        private TextBlock toastText;
        private DispatcherTimer toastTimer;
        private System.Windows.Documents.Run logoMiRun;
        private int curTab = 0;

        public MainWindow()
        {
            Title = "MiTV - Reproductor de IPTV";
            WindowStyle = WindowStyle.None;
            var chrome = new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 36,
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(6)
            };
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);
            try
            {
                var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "icon.png");
                if (System.IO.File.Exists(iconPath))
                    Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath, UriKind.Absolute));
            }
            catch { }
            Width = 1280;
            Height = 720;
            WindowState = WindowState.Maximized;
            Background = Ui.Bg;
            RestoreWindowPlacement();
            Closing += (_, __) => SaveWindowPlacement();

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            // Barra de título personalizada
            var titleBar = new Grid { Background = Ui.B("#1a1a1a") };
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var leftTitle = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            try
            {
                var iconPath2 = System.IO.Path.Combine(AppContext.BaseDirectory, "icon.png");
                if (System.IO.File.Exists(iconPath2))
                    leftTitle.Children.Add(new System.Windows.Controls.Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath2, UriKind.Absolute)), Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0) });
            }
            catch { }
            leftTitle.Children.Add(new TextBlock { Text = "MiTV", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Ui.Text, VerticalAlignment = VerticalAlignment.Center });
            leftTitle.Children.Add(new TextBlock { Text = "  Reproductor de IPTV", FontSize = 13, Foreground = Ui.Gray, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(leftTitle, 0);
            titleBar.Children.Add(leftTitle);
            string verStr = "";
            try { verStr = "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? ""); } catch { }
            var verTxt = new TextBlock { Text = verStr, FontSize = 13, Foreground = Ui.Gray, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(verTxt, 1);
            titleBar.Children.Add(verTxt);
            var winBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Stretch };
            var btnMin = new Button { Content = "\u2212", FontSize = 14, Width = 46, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Ui.Text, Cursor = System.Windows.Input.Cursors.Hand };
            btnMin.Click += (s, e) => WindowState = WindowState.Minimized;
            var btnMax = new Button { Content = "\u2610", FontSize = 12, Width = 46, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Ui.Text, Cursor = System.Windows.Input.Cursors.Hand };
            btnMax.Click += (s, e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            var btnClose = new Button { Content = "\u2715", FontSize = 13, Width = 46, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Ui.Text, Cursor = System.Windows.Input.Cursors.Hand };
            btnClose.Click += (s, e) => Close();
            btnClose.MouseEnter += (s, e) => btnClose.Background = Ui.Red;
            btnClose.MouseLeave += (s, e) => btnClose.Background = Brushes.Transparent;
            winBtns.Children.Add(btnMin); winBtns.Children.Add(btnMax); winBtns.Children.Add(btnClose);
            // No interceptar los botones con el chrome
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(btnMin, true);
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(btnMax, true);
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(btnClose, true);
            Grid.SetColumn(winBtns, 2);
            titleBar.Children.Add(winBtns);
            // Arrastrar con doble clic para maximizar
            titleBar.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; };
            Grid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(grid, 1);
            root.Children.Add(grid);

            // Rectángulo flotante arriba a la derecha con logo + buscar + ajustes
            pill = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = Ui.Panel2,
                BorderBrush = Ui.Brd,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 6, 8, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 14, 16, 0),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = 0.45,
                    BlurRadius = 14,
                    ShadowDepth = 3
                }
            };
            var hp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var logo = new TextBlock { FontSize = 20, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            logoMiRun = new System.Windows.Documents.Run("Mi") { Foreground = Ui.Text };
            logo.Inlines.Add(logoMiRun);
            logo.Inlines.Add(new System.Windows.Documents.Run("TV") { Foreground = Ui.Red });
            hp.Children.Add(logo);
            var bs = new Button { Content = "🔍", Style = Ui.TransparentButton() };
            bs.Click += (s, e) =>
            {
                var cur = MainContent.Content as ViewBase;
                cur?.ToggleSearch();
            };
            var bg = new Button { Content = "⚙️", Style = Ui.TransparentButton() };
            bg.Click += (s, e) => Nav.Push(new SettingsView());
            hp.Children.Add(bs);
            hp.Children.Add(bg);
            pill.Child = hp;

            // Contenido
            MainContent = new ContentControl();

            // Barra de pestañas
            tabBar = new Border
            {
                Background = Ui.TopBg,
                BorderBrush = Ui.Brd,
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            var ug = new UniformGrid { Columns = 7 };
            tabsGrid = ug;
            tabKeys = new[] { "Inicio", "TV en vivo", "Películas", "Series", "Deportes", "Guía", "Repetición" };
            string[] icons = { "🏠", "📺", "🎬", "🎭", "⚽", "📅", "↺" };
            tabLabels = new TextBlock[7];
            tabIcons = new TextBlock[7];
            for (int i = 0; i < 7; i++)
            {
                int idx = i;
                var sp = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 12)
                };
                tabIcons[i] = new TextBlock { Text = icons[i], FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Ui.Gray };
                tabLabels[i] = new TextBlock { Text = Lang.T(tabKeys[i]), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Ui.Gray, Margin = new Thickness(0, 4, 0, 0) };
                sp.Children.Add(tabIcons[i]);
                sp.Children.Add(tabLabels[i]);
                var b = new Button
                {
                    Content = sp,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                b.Click += (s, e) => GoTab(idx);
                ug.Children.Add(b);
                if (idx == 5) guideTabBtn = b;
            }
            tabBar.Child = ug;
            Grid.SetRow(tabBar, 1);

            grid.Children.Add(MainContent);
            grid.Children.Add(tabBar);
            Grid.SetRow(pill, 1);
            root.Children.Add(pill); // flotante sobre el contenido

            // Toast global
            toastText = new TextBlock
            {
                Background = Ui.Brd,
                Foreground = Ui.Text,
                FontSize = 15,
                Padding = new Thickness(24, 12, 24, 12),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 90),
                Visibility = Visibility.Collapsed
            };
            root.Children.Add(toastText);
            toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
            toastTimer.Tick += (s, e) => { toastText.Visibility = Visibility.Collapsed; toastTimer.Stop(); };

            Content = root;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) (MainContent.Content as ViewBase)?.OnEscapeKey(); };
            InstallKbHook();
            InstallMouseHook();
            Closed += (s, e) => { if (kbHook != IntPtr.Zero) UnhookWindowsHookEx(kbHook); if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook); };

            Nav.Main = this;
            ContentStore.LoadCache();
            CatFilter.PurgeHidden();
            EpgStore.LoadCache();
            Recording.Init();
            RefreshTabs();
            int st = ViewState.StartTab;
            if (st == 5 && !EpgSettings.Enabled) st = 0;
            GoTab(st);
            PlaylistLoader.RefreshInBackground(quiet: true);
        }

        public void RefreshTabs()
        {
            if (guideTabBtn == null || tabsGrid == null) return;
            bool show = EpgSettings.Enabled;
            guideTabBtn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            tabsGrid.Columns = show ? 7 : 6;
        }

        public void GoTabPublic(int i) => GoTab(i);

        void GoTab(int i)
        {
            ViewBase v = i switch
            {
                0 => new HomeView(),
                1 => new LiveTvView(),
                2 => new MoviesView(),
                3 => new ShowsView(),
                4 => new SportsView(),
                5 => new EpgView(),
                _ => new CatchupView(),
            };
            Nav.Tab(v, i);
        }

        public void SetChrome(bool show)
        {
            pill.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            tabBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        // Reaplica el idioma a las pestañas (tras cambiarlo en Ajustes)
        public void ApplyLanguage()
        {
            if (tabLabels == null || tabKeys == null) return;
            for (int i = 0; i < tabLabels.Length && i < tabKeys.Length; i++)
                tabLabels[i].Text = Lang.T(tabKeys[i]);
        }

        // Reaplica el tema a la ventana y la barra de pestañas (tras cambiarlo en Ajustes)
        public void ApplyTheme()
        {
            Background = Ui.Bg;
            tabBar.Background = Ui.TopBg;
            tabBar.BorderBrush = Ui.Brd;
            pill.Background = Ui.Panel2;
            pill.BorderBrush = Ui.Brd;
            if (logoMiRun != null) logoMiRun.Foreground = Ui.Text;
            SelectTab(curTab);
        }

        public void SelectTab(int i)
        {
            curTab = i;
            for (int k = 0; k < tabLabels.Length; k++)
            {
                var fg = k == i ? Ui.Red : Ui.Gray;
                tabLabels[k].Foreground = fg;
                tabIcons[k].Foreground = fg;
            }
        }

        public void ShowToast(string msg)
        {
            toastText.Text = msg;
            toastText.Visibility = Visibility.Visible;
            toastTimer.Stop();
            toastTimer.Start();
        }

        // Recuerda la última posición/tamaño/estado de la ventana
        private string PlacementFile =>
            Path.Combine(AppContext.BaseDirectory, "mitv-window.json");

        private void SaveWindowPlacement()
        {
            try
            {
                var b = WindowState == WindowState.Normal
                    ? new Rect(Left, Top, Width, Height) : RestoreBounds;
                var st = WindowState == WindowState.Minimized ? "Normal" : WindowState.ToString();
                File.WriteAllText(PlacementFile,
                    JsonSerializer.Serialize(new
                    {
                        l = (int)b.Left,
                        t = (int)b.Top,
                        w = (int)b.Width,
                        h = (int)b.Height,
                        s = st
                    }));
            }
            catch { }
        }

        private void RestoreWindowPlacement()
        {
            try
            {
                if (!File.Exists(PlacementFile)) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(PlacementFile));
                var r = doc.RootElement;
                double l = r.GetProperty("l").GetDouble();
                double t = r.GetProperty("t").GetDouble();
                double w = Math.Max(640, r.GetProperty("w").GetDouble());
                double h = Math.Max(400, r.GetProperty("h").GetDouble());
                string s = r.TryGetProperty("s", out var sv) ? sv.GetString() ?? "Normal" : "Normal";

                // Si la posición guardada quedó fuera de las pantallas actuales, ignorarla
                double vl = SystemParameters.VirtualScreenLeft;
                double vt = SystemParameters.VirtualScreenTop;
                double vr = vl + SystemParameters.VirtualScreenWidth;
                double vb = vt + SystemParameters.VirtualScreenHeight;
                if (l + 50 < vl || l + 50 > vr || t + 50 < vt || t + 50 > vb) return;

                WindowState = WindowState.Normal;
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = l; Top = t; Width = w; Height = h;
                if (s == "Maximized") WindowState = WindowState.Maximized;
            }
            catch { }
        }
    }
}
