using System;
using System.Collections.Generic;
using System.Text;
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
        Grid rootGrid, titleBarGrid;

        // En fullscreen el reproductor oculta la barra superior propia
        // (título + botones) y la franja de arrastre, para que solo se vea el video.
        public void SetFullscreenChrome(bool fullscreen)
        {
            if (titleBarGrid != null)
                titleBarGrid.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
            if (rootGrid != null)
                rootGrid.RowDefinitions[0].Height = new GridLength(fullscreen ? 0 : 36);
            try
            {
                var wc = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
                if (wc != null) wc.CaptionHeight = fullscreen ? 0 : 36;
            }
            catch { }
        }

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
                HwndSource.FromHwnd(hwnd)?.AddHook(_workAreaHook);
                InstallSubclass(hwnd);
                // Reafirmar la subclase cada segundo: VLC puede reinstalar
                // su procedimiento sobre la ventana del video y dejarnos
                // fuera sin crear ventana nueva. La guardia de ForwardProc
                // impide el ciclo de reenvío que tumbó el intento anterior.
                _rescanTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (s, ev) => { try { SubclassVideoChildren(); } catch { } }, Dispatcher.CurrentDispatcher);
                _rescanTimer.Start();
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            try { _rescanTimer?.Stop(); } catch { }
            RemoveSubclass();
            base.OnClosed(e);
        }

        private DispatcherTimer _rescanTimer;

        // Referencia fuerte al delegado del hook: HwndSource guarda los
        // hooks con referencia débil y el GC puede recolectarlos.
        private static readonly HwndSourceHook _workAreaHook = WndProcWorkArea;

        // En modo fullscreen del reproductor, maximizar debe cubrir el monitor
        // completo (incluida la barra de tareas); si no, solo el área de trabajo.
        public static bool FullscreenMax = false;

        // Al maximizar, limitar la ventana al área de trabajo del monitor: con el
        // marco propio de la app, Windows maximiza a pantalla completa y tapa la
        // barra de tareas; WM_GETMINMAXINFO permite corregir tamaño y posición.
        // El agarre de los bordes (WM_NCHITTEST) ya no vive aquí: el hook de
        // HwndSource no llegaba a responderlo y lo atiende la subclase nativa.
        private static IntPtr WndProcWorkArea(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            if (msg == WM_GETMINMAXINFO)
            {
                try
                {
                    var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    IntPtr mon = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
                    if (mon != IntPtr.Zero)
                    {
                        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                        if (GetMonitorInfo(mon, ref mi))
                        {
                            var lim = FullscreenMax ? mi.rcMonitor : mi.rcWork;
                            mmi.ptMaxPosition.x = lim.left - mi.rcMonitor.left;
                            mmi.ptMaxPosition.y = lim.top - mi.rcMonitor.top;
                            mmi.ptMaxSize.x = lim.right - lim.left;
                            mmi.ptMaxSize.y = lim.bottom - lim.top;
                            Marshal.StructureToPtr(mmi, lParam, true);
                        }
                    }
                }
                catch { }
            }
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)] private struct POINTL { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct MINMAXINFO { public POINTL ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
        [StructLayout(LayoutKind.Sequential)] private struct RECTL { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECTL rcMonitor, rcWork; public int dwFlags; }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECTL lpRect);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int GetDpiForWindow(IntPtr hwnd);

        // 8 px independientes del dispositivo, en píxeles físicos
        static int GripPx(IntPtr hwnd)
        {
            try
            {
                int dpi = GetDpiForWindow(hwnd);
                if (dpi > 0) return Math.Max(4, (int)Math.Round(8.0 * dpi / 96.0));
            }
            catch { }
            return 8;
        }

        // Código HT de la franja de agarre (0 = fuera de la franja),
        // calculado desde el rectángulo real de la ventana.
        static int HitTestBorder(IntPtr hwnd, IntPtr lParam)
        {
            try
            {
                if (GetWindowRect(hwnd, out RECTL rc))
                {
                    long v = lParam.ToInt64();
                    int x = (short)(v & 0xFFFF);
                    int y = (short)((v >> 16) & 0xFFFF);
                    int grip = GripPx(hwnd);
                    bool left = x - rc.left < grip;
                    bool right = rc.right - x < grip;
                    bool top = y - rc.top < grip;
                    bool bottom = rc.bottom - y < grip;
                    if (top && left) return 13;         // HTTOPLEFT
                    if (top && right) return 14;        // HTTOPRIGHT
                    if (bottom && left) return 16;      // HTBOTTOMLEFT
                    if (bottom && right) return 17;     // HTBOTTOMRIGHT
                    if (left) return 10;                // HTLEFT
                    if (right) return 11;               // HTRIGHT
                    if (top) return 12;                 // HTTOP
                    if (bottom) return 15;              // HTBOTTOM
                }
            }
            catch { }
            return 0;
        }

        // Subclase nativa del procedimiento de ventana: responde WM_NCHITTEST
        // ANTES que el procedimiento de WPF y que el manejador del
        // WindowChrome (que contestaba primero y solo devolvía el agarre
        // arriba). El resto de mensajes sigue al procedimiento original.
        // Los delegados quedan en campos estáticos: si el GC los recolecta,
        // Windows llamaría a código muerto y la ventana se rompería.
        private delegate IntPtr SubclassWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private static SubclassWndProc _subclassDel;
        private static IntPtr _oldWndProc = IntPtr.Zero;
        private static IntPtr _subclassHwnd = IntPtr.Zero;
        private const int GWLP_WNDPROC = -4;

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        // Reenvío con guardia contra ciclos: si el procedimiento al que
        // reenviamos vuelve a entrar al nuestro con la misma ventana y el
        // mismo mensaje (otro componente nos capturó como su "anterior"),
        // contestar con el procedimiento por defecto en vez de reenviar
        // otra vez. Sin esto, la recursión desborda la pila y la app muere.
        [ThreadStatic] private static HashSet<(IntPtr hWnd, uint msg)> _procGuard;
        private static IntPtr ForwardProc(IntPtr oldProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (_procGuard == null) _procGuard = new HashSet<(IntPtr, uint)>();
            if (!_procGuard.Add((hWnd, msg))) return DefWindowProc(hWnd, msg, wParam, lParam);
            try { return CallWindowProc(oldProc, hWnd, msg, wParam, lParam); }
            finally { _procGuard.Remove((hWnd, msg)); }
        }

        private static void InstallSubclass(IntPtr hwnd)
        {
            if (_oldWndProc != IntPtr.Zero) return;
            _myPid = Process.GetCurrentProcess().Id;
            _subclassDel = SubclassProc;
            IntPtr fn = Marshal.GetFunctionPointerForDelegate(_subclassDel);
            IntPtr old = SetWindowLongPtr64(hwnd, GWLP_WNDPROC, fn);
            try { System.IO.File.Delete(System.IO.Path.Combine(System.AppContext.BaseDirectory, "mitv-resize-diag.txt")); _diagCount = 0; } catch { }
            Diag("MAIN subclass hwnd=" + hwnd + " old=" + old + " pid=" + _myPid);
            if (old != IntPtr.Zero) { _oldWndProc = old; _subclassHwnd = hwnd; SubclassVideoChildren(); }
        }

        // Diagnóstico temporal del agarre (borrar tras verificar): escribe
        // en mitv-resize-diag.txt junto al exe, con tope de líneas.
        private static int _diagCount;
        private static int _diagChildHit;
        private static int _diagChildPN;
        private static int _diagMainHit;
        private static int _diagVlcHit;
        private static readonly HashSet<IntPtr> _vlcWindows = new HashSet<IntPtr>();
        private static void Diag(string line)
        {
            try
            {
                if (_diagCount >= 300) return;
                _diagCount++;
                string path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "mitv-resize-diag.txt");
                System.IO.File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + "\r\n");
            }
            catch { }
        }

        private static string WinClass(IntPtr hWnd)
        {
            try { var sb = new StringBuilder(256); GetClassName(hWnd, sb, 256); return sb.ToString(); }
            catch { return "?"; }
        }

        private static void RemoveSubclass()
        {
            try
            {
                if (_oldWndProc != IntPtr.Zero && _subclassHwnd != IntPtr.Zero)
                    SetWindowLongPtr64(_subclassHwnd, GWLP_WNDPROC, _oldWndProc);
            }
            catch { }
            _oldWndProc = IntPtr.Zero;
            _subclassHwnd = IntPtr.Zero;
            // Restaurar también las ventanas hijas de video subclasadas.
            try
            {
                lock (_childLock)
                {
                    foreach (var kv in _childOldProcs)
                        SetWindowLongPtr64(kv.Key, GWLP_WNDPROC, kv.Value);
                    _childOldProcs.Clear();
                }
            }
            catch { }
        }

        private static IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            const uint WM_NCDESTROY = 0x0082;
            const uint WM_NCHITTEST = 0x0084;
            try
            {
                if (msg == WM_NCDESTROY)
                {
                    // Último mensaje de la ventana: restaurar el procedimiento
                    // original antes de reenviar.
                    IntPtr old = _oldWndProc;
                    _oldWndProc = IntPtr.Zero;
                    _subclassHwnd = IntPtr.Zero;
                    if (old != IntPtr.Zero)
                    {
                        SetWindowLongPtr64(hWnd, GWLP_WNDPROC, old);
                        return ForwardProc(old, hWnd, msg, wParam, lParam);
                    }
                    return DefWindowProc(hWnd, msg, wParam, lParam);
                }
                // Un hijo nuevo (el video) o un cambio de posición: buscar
                // ventanas hijas de video para subclasarlas.
                const uint WM_PARENTNOTIFY = 0x0210;
                const uint WM_WINDOWPOSCHANGED = 0x0047;
                if ((msg == WM_PARENTNOTIFY && (wParam.ToInt64() & 0xFFFF) == 1) || msg == WM_WINDOWPOSCHANGED)
                    SubclassVideoChildren();
                // Agarre de 8 px en los 4 bordes y las 4 esquinas. Solo se
                // contesta dentro de la franja: el resto sigue su manejo
                // normal (título, botones, contenido). Sin agarre si está
                // maximizada o en fullscreen del reproductor.
                if (msg == WM_NCHITTEST && !FullscreenMax && !IsZoomed(hWnd))
                {
                    int ht = HitTestBorder(hWnd, lParam);
                    if (_diagMainHit < 10) { _diagMainHit++; Diag("MAIN HT ht=" + ht); }
                    if (ht != 0) return (IntPtr)ht;
                }
            }
            catch { }
            IntPtr prev = _oldWndProc;
            return prev != IntPtr.Zero
                ? ForwardProc(prev, hWnd, msg, wParam, lParam)
                : DefWindowProc(hWnd, msg, wParam, lParam);
        }

        // Ventanas hijas de video (el VideoView de LibVLC pinta en un HWND
        // hijo que se come el hit-test de los bordes cuando el video está
        // corriendo). Se subclasa TODA ventana del propio proceso (hijas,
        // nietas y superiores) sin filtros de tamaño ni visibilidad: el
        // hijo del video puede crearse sin tamaño y luego agrandarse, y
        // VLC puede anidar su propia ventana dentro de la del host. En
        // la franja de agarre devuelve el código HT de la ventana
        // principal; el resto sigue a su procedimiento original.
        // Referencias fuertes en campos estáticos.
        private static SubclassWndProc _childSubclassDel;
        private static readonly Dictionary<IntPtr, IntPtr> _childOldProcs = new Dictionary<IntPtr, IntPtr>();
        private static readonly object _childLock = new object();
        private static int _myPid;
        private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        private static EnumChildProc _enumChildDel;
        private static EnumWindowsProc _enumWindowsDel;

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private static void SubclassVideoChildren()
        {
            IntPtr main = _subclassHwnd;
            if (main == IntPtr.Zero) return;
            try
            {
                _enumChildDel = EnumChildCallback;
                EnumChildWindows(main, _enumChildDel, IntPtr.Zero);
                _enumWindowsDel = EnumTopCallback;
                EnumWindows(_enumWindowsDel, IntPtr.Zero);
            }
            catch { }
        }

        private static bool EnumChildCallback(IntPtr hWnd, IntPtr lParam)
        {
            try { SubclassOne(hWnd); } catch { }
            return true;
        }

        private static bool EnumTopCallback(IntPtr hWnd, IntPtr lParam)
        {
            try
            {
                uint pid = 0;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid == (uint)_myPid && hWnd != _subclassHwnd) SubclassOne(hWnd);
            }
            catch { }
            return true;
        }

        private static void SubclassOne(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || hWnd == _subclassHwnd) return;
            try
            {
                if (_childSubclassDel == null) _childSubclassDel = ChildSubclassProc;
                IntPtr mine = Marshal.GetFunctionPointerForDelegate(_childSubclassDel);
                // Verificar: VLC puede reinstalar su propio procedimiento
                // después de nosotros y dejarnos fuera; si el actual ya no
                // es el nuestro, reinstalar encima. La guardia de
                // ForwardProc impide el ciclo de reenvío que tumbó la
                // versión anterior.
                IntPtr cur = GetWindowLongPtr64(hWnd, GWLP_WNDPROC);
                if (cur == mine) return;
                bool re;
                lock (_childLock) { re = _childOldProcs.ContainsKey(hWnd); }
                IntPtr old = SetWindowLongPtr64(hWnd, GWLP_WNDPROC, mine);
                if (old != IntPtr.Zero)
                {
                    lock (_childLock) { _childOldProcs[hWnd] = old; }
                    string cls = WinClass(hWnd);
                    if (cls.StartsWith("VLC")) { lock (_childLock) { _vlcWindows.Add(hWnd); } }
                    Diag((re ? "RE-SUB " : "SUB ") + "hwnd=" + hWnd + " class=" + cls + " old=" + old + " cur=" + cur);
                    // Recursión: los nietos también (VLC anida su ventana).
                    _enumChildDel = EnumChildCallback;
                    EnumChildWindows(hWnd, _enumChildDel, IntPtr.Zero);
                }
                else Diag("SUB-FAILED hwnd=" + hWnd + " class=" + WinClass(hWnd));
            }
            catch { }
        }

        private static IntPtr ChildSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            const uint WM_NCDESTROY = 0x0082;
            const uint WM_NCHITTEST = 0x0084;
            const uint WM_PARENTNOTIFY = 0x0210;
            try
            {
                if (msg == WM_NCDESTROY)
                {
                    IntPtr old = IntPtr.Zero;
                    lock (_childLock)
                    {
                        if (_childOldProcs.TryGetValue(hWnd, out old)) _childOldProcs.Remove(hWnd);
                        _vlcWindows.Remove(hWnd);
                    }
                    if (old != IntPtr.Zero)
                    {
                        SetWindowLongPtr64(hWnd, GWLP_WNDPROC, old);
                        return ForwardProc(old, hWnd, msg, wParam, lParam);
                    }
                    return DefWindowProc(hWnd, msg, wParam, lParam);
                }
                // Un descendiente nuevo (VLC crea su ventana al arrancar el
                // video): re-escanear el árbol.
                if (msg == WM_PARENTNOTIFY && (wParam.ToInt64() & 0xFFFF) == 1)
                {
                    if (_diagChildPN < 10) { _diagChildPN++; Diag("CHILD PARENTNOTIFY hwnd=" + hWnd + " class=" + WinClass(hWnd)); }
                    SubclassVideoChildren();
                }
                IntPtr main = _subclassHwnd;
                if (msg == WM_NCHITTEST && main != IntPtr.Zero && !FullscreenMax && !IsZoomed(main))
                {
                    int ht = HitTestBorder(main, lParam);
                    if (_diagChildHit < 20) { _diagChildHit++; Diag("CHILD HT hwnd=" + hWnd + " class=" + WinClass(hWnd) + " ht=" + ht); }
                    bool isVlc;
                    lock (_childLock) { isVlc = _vlcWindows.Contains(hWnd); }
                    if (isVlc && _diagVlcHit < 100)
                    {
                        _diagVlcHit++;
                        long v = lParam.ToInt64();
                        int px = (short)(v & 0xFFFF);
                        int py = (short)((v >> 16) & 0xFFFF);
                        RECTL mrc = new RECTL();
                        GetWindowRect(main, out mrc);
                        Diag("VLC HT hwnd=" + hWnd + " pt=" + px + "," + py + " main=" + mrc.left + "," + mrc.top + "," + mrc.right + "," + mrc.bottom + " grip=" + GripPx(main) + " ht=" + ht + " fs=" + FullscreenMax + " zoom=" + IsZoomed(main));
                    }
                    if (ht != 0) return (IntPtr)ht;
                }
            }
            catch { }
            IntPtr prev = IntPtr.Zero;
            lock (_childLock) { _childOldProcs.TryGetValue(hWnd, out prev); }
            if (prev != IntPtr.Zero)
                return ForwardProc(prev, hWnd, msg, wParam, lParam);
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        // Rectángulo COMPLETO del monitor donde está la ventana (píxeles físicos).
        // Para el fullscreen del reproductor: no depende del hook de maximizar.
        public static (int Left, int Top, int Right, int Bottom)? MonitorFullRect(IntPtr hwnd)
        {
            try
            {
                IntPtr mon = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
                if (mon == IntPtr.Zero) return null;
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(mon, ref mi)) return null;
                return (mi.rcMonitor.left, mi.rcMonitor.top, mi.rcMonitor.right, mi.rcMonitor.bottom);
            }
            catch { return null; }
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
                // 1px de cristal + borde de agarre de 8px: con cristal en 0 los
                // agarres laterales no respondían (quirk del WindowChrome).
                GlassFrameThickness = new Thickness(1),
                ResizeBorderThickness = new Thickness(8)
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
            rootGrid = root;
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            // Barra de título personalizada
            var titleBar = new Grid { Background = Ui.B("#1a1a1a") };
            titleBarGrid = titleBar;
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
