using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MiTV
{
    // Sub-pantalla genérica de ajustes
    public class SettingSubView : ViewBase
    {
        readonly string title;
        readonly Action<StackPanel> populate;
        readonly StackPanel sp;
        public SettingSubView(string title, Action<StackPanel> populate)
        {
            this.title = title;
            this.populate = populate;
            sp = new StackPanel { Margin = new Thickness(26, 14, 26, 18) };
            Rebuild();
            Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
        // Re-dibuja con los valores actuales (tras elegir en un picker)
        public void Rebuild()
        {
            sp.Children.Clear();
            sp.Children.Add(Ui.BackHeader(title, () => Nav.Back()));
            populate(sp);
        }
    }

    // Lista de opciones tipo radio
    public class PickerView : ViewBase
    {
        public PickerView(string title, string[] options, string current, Action<string> onPick)
        {
            var sp = new StackPanel { Margin = new Thickness(26, 14, 26, 18) };
            sp.Children.Add(Ui.BackHeader(title, () => Nav.Back()));
            foreach (var o in options)
            {
                string oo = o;
                sp.Children.Add(Ui.PickerOption(o, o == current, () =>
                {
                    Nav.Toast(title + ": " + oo);
                    onPick(oo);
                }));
            }
            Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
    }

    public class SettingsView : ViewBase
    {
        StackPanel sp;

        public SettingsView()
        {
            sp = new StackPanel { Margin = new Thickness(26, 14, 26, 18) };
            Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Build();
        }

        public override void OnShow() => Build();

        void Build()
        {
            sp.Children.Clear();
            sp.Children.Add(Ui.BackHeader(Lang.T("Configuración"), () => Nav.Back()));

            sp.Children.Add(Ui.GroupHeader(Lang.T("General")));
            sp.Children.Add(Ui.SettingRow(Lang.T("Configuración general"), Lang.T("Idioma, tema, actualizaciones"), null, () => Nav.Push(BuildGeneral())));

            sp.Children.Add(Ui.GroupHeader(Lang.T("Contenido")));
            sp.Children.Add(Ui.SettingRow(Lang.T("Reproducción"), Lang.T("Decodificador, buffer, timeshift"), null, () => Nav.Push(BuildPlayback())));
            sp.Children.Add(Ui.SettingRow(Lang.T("Subtítulos"), Lang.T("Fuente, tamaño, color, fondo"), null, () => Nav.Push(BuildSubs())));
            sp.Children.Add(Ui.SettingRow(Lang.T("TV en vivo"), Lang.T("Grupos, favoritos, orden"), null, () => Nav.Push(BuildLiveSet())));
            sp.Children.Add(Ui.SettingRow(Lang.T("Películas y Series (VOD)"), Lang.T("Límite de resultados, intros"), null, () => Nav.Push(BuildVod())));
            sp.Children.Add(Ui.SettingRow(Lang.T("Guía (EPG)"), Lang.T("Fuente EPG, actualización"), null, () => Nav.Push(BuildEpg())));

            sp.Children.Add(Ui.GroupHeader(Lang.T("Cuenta")));
            sp.Children.Add(Ui.SettingRow("Listas IPTV", "Agregar, editar y activar tus listas",
                PlaylistStore.Active?.Name, () => Nav.Push(new PlaylistView())));
            sp.Children.Add(Ui.SettingRow(Lang.T("Grabaciones"), Lang.T("Carpeta de destino, programadas"), null, () => Nav.Push(BuildRecordings())));

            sp.Children.Add(Ui.GroupHeader(Lang.T("Avanzado")));
            sp.Children.Add(Ui.SettingRow(Lang.T("Ajustes avanzados"), Lang.T("Caché de imágenes"), null, () => Nav.Push(BuildAdvanced())));
        }

        static SettingSubView BuildGeneral()
        {
            return new SettingSubView(Lang.T("Configuración general"), sp =>
            {
                sp.Children.Add(Ui.GroupHeader(Lang.T("Apariencia")));
                sp.Children.Add(Ui.SettingRow(Lang.T("Idioma"), Lang.T("Idioma de la aplicación"), Lang.DisplayName, () =>
                    Nav.Push(new PickerView(Lang.T("Idioma"), new[] { "Español", "English" }, Lang.DisplayName,
                        v => { Lang.Set(v == "English" ? "en" : "es"); Nav.Main.ApplyLanguage(); Nav.BackToTab(); Nav.RefreshContent(); }))));
                // Tema visual
                var themeRow = new Border
                {
                    Background = Ui.Panel, BorderBrush = Ui.Brd, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 14, 18, 14),
                    Margin = new Thickness(0, 0, 0, 10)
                };
                var tsp = new StackPanel();
                tsp.Children.Add(new TextBlock { Text = Lang.T("Tema de la aplicación"), FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Ui.Text });
                tsp.Children.Add(new TextBlock { Text = string.Format(Lang.T("{0} temas disponibles"), Theme.ThemeNames.Length), FontSize = 13, Foreground = Ui.Gray, Margin = new Thickness(0, 3, 0, 0) });
                var swatches = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
                string sel = Theme.Current;
                foreach (var tname in Theme.ThemeNames)
                {
                    string tn = tname;
                    var (hex1, hex2) = Theme.Preview(tn);
                    swatches.Children.Add(Ui.ThemeSwatch(Lang.T(tn), hex1, hex2, tn == sel, () =>
                    {
                        Theme.Set(tn);
                        Nav.Main.ApplyTheme();
                        Nav.BackToTab();
                        Nav.RefreshContent();
                        Nav.Toast(Lang.T("Tema: ") + Lang.T(tn));
                    }));
                }
                tsp.Children.Add(swatches);
                themeRow.Child = tsp;
                sp.Children.Add(themeRow);

                sp.Children.Add(Ui.GroupHeader(Lang.T("Listas")));
                sp.Children.Add(Ui.SettingRow(Lang.T("Editor de categorías"), Lang.T("Oculta o restaura categorías enteras"), null,
                    () => Nav.Push(new CatEditorView())));
                {
                    string[] tabs = { "Inicio", "TV en vivo", "Películas", "Series", "Deportes", "Guía", "Repetición" };
                    string[] disp = tabs.Select(t => Lang.T(t)).ToArray();
                    int cur = ViewState.StartTab;
                    sp.Children.Add(Ui.SettingRow(Lang.T("Pestaña inicial"), Lang.T("Qué ver al abrir la app"), disp[cur], () =>
                        Nav.Push(new PickerView(Lang.T("Pestaña inicial"), disp, disp[cur],
                            v => { int idx = Array.IndexOf(disp, v); if (idx >= 0) ViewState.StartTab = idx; Nav.PickDone(); }))));
                }

                sp.Children.Add(Ui.GroupHeader(Lang.T("Acerca de")));
                sp.Children.Add(Ui.SettingRow(Lang.T("Versión"), Lang.T("Acerca de MiTV"), AppVersion,
                    () => Nav.Push(BuildAbout())));
            });
        }

        static SettingSubView BuildPlayback()
        {
            return new SettingSubView(Lang.T("Reproducción"), sp =>
            {
                string decCur = PlaybackSettings.Decoder == "sw" ? Lang.T("Software") : Lang.T("Hardware");
                sp.Children.Add(Ui.SettingRow(Lang.T("Decodificador"), Lang.T("Hardware o software") + " \u00b7 " + Lang.T("se aplica al próximo canal"), decCur, () =>
                    Nav.Push(new PickerView(Lang.T("Decodificador"), new[] { Lang.T("Hardware"), Lang.T("Software") }, decCur,
                        v => { PlaybackSettings.Decoder = v == Lang.T("Software") ? "sw" : "hw"; Nav.PickDone(); }))));
                string bufCur = PlaybackSettings.BufferMs == 400 ? Lang.T("Pequeño") : PlaybackSettings.BufferMs == 2500 ? Lang.T("Grande") : Lang.T("Medio");
                sp.Children.Add(Ui.SettingRow(Lang.T("Tamaño del buffer"), Lang.T("Cuánto precarga antes de reproducir"), bufCur, () =>
                    Nav.Push(new PickerView(Lang.T("Tamaño del buffer"), new[] { Lang.T("Pequeño"), Lang.T("Medio"), Lang.T("Grande") }, bufCur,
                        v => { PlaybackSettings.BufferMs = v == Lang.T("Pequeño") ? 400 : v == Lang.T("Grande") ? 2500 : 1000; Nav.PickDone(); }))));
                string[] tsVals = { "0", "30", "60", "120", "180", "240" };
                string[] tsNames = { Lang.T("Desactivado"), "30 " + Lang.T("minutos"), "1 " + Lang.T("hora"), "2 " + Lang.T("horas"), "3 " + Lang.T("horas"), "4 " + Lang.T("horas") };
                int tsIdx = Math.Max(0, Array.IndexOf(tsVals, PlaybackSettings.TimeshiftMin.ToString()));
                sp.Children.Add(Ui.SettingRow(Lang.T("Timeshift"), Lang.T("Pausar y retroceder TV en vivo") + " \u00b7 RAM", tsNames[tsIdx], () =>
                    Nav.Push(new PickerView(Lang.T("Timeshift"), tsNames, tsNames[tsIdx],
                        v => { int i = Array.IndexOf(tsNames, v); if (i >= 0) PlaybackSettings.TimeshiftMin = int.Parse(tsVals[i]); Nav.PickDone(); }))));
                sp.Children.Add(Ui.SwitchRow(Lang.T("Reanudar automáticamente"), Lang.T("Continúa donde lo dejaste"), PlaybackSettings.AutoResume,
                    v => PlaybackSettings.AutoResume = v));
                sp.Children.Add(Ui.SwitchRow(Lang.T("Reproducción en segundo plano"), Lang.T("Sigue sonando al salir del reproductor"), PlaybackSettings.BackgroundPlay,
                    v => PlaybackSettings.BackgroundPlay = v));
                sp.Children.Add(Ui.SettingRow(Lang.T("Botones de salto"), Lang.T("Elige cuáles se muestran"), null,
                    () => Nav.Push(BuildSkipButtons())));
                sp.Children.Add(Ui.SwitchRow(Lang.T("Mostrar reloj"), Lang.T("Reloj permanente en el reproductor"), PlaybackSettings.ShowClock,
                    v => PlaybackSettings.ShowClock = v));
            });
        }

        static SettingSubView BuildSkipButtons()
        {
            return new SettingSubView(Lang.T("Botones de salto"), sp =>
            {
                foreach (var sd in PlayerView.SkipDefs)
                {
                    var id = sd.id;
                    sp.Children.Add(Ui.SwitchRow(sd.name, null, PlaybackSettings.IsSkipVisible(id),
                        v => PlaybackSettings.SetSkipVisible(id, v)));
                }
            });
        }

        static SettingSubView BuildSubs()
        {
            return new SettingSubView(Lang.T("Subtítulos"), sp =>
            {
                // Tamaño de fuente (escala VLC: número menor = letra más grande)
                string sizeCur = SubStyle.RelFontSize == 18 ? Lang.T("Pequeño")
                    : SubStyle.RelFontSize == 12 ? Lang.T("Grande")
                    : SubStyle.RelFontSize == 6 ? Lang.T("Muy grande") : Lang.T("Normal");
                sp.Children.Add(Ui.SettingRow(Lang.T("Tamaño de fuente"), Lang.T("Tamaño del texto"), sizeCur, () =>
                    Nav.Push(new PickerView(Lang.T("Tamaño de fuente"),
                        new[] { Lang.T("Pequeño"), Lang.T("Normal"), Lang.T("Grande"), Lang.T("Muy grande") }, sizeCur,
                        v =>
                        {
                            SubStyle.RelFontSize = v == Lang.T("Pequeño") ? 18
                                : v == Lang.T("Grande") ? 12 : v == Lang.T("Muy grande") ? 6 : 16;
                            SubStyle.Save(); Nav.PickDone();
                        }))));

                sp.Children.Add(Ui.GroupHeader(Lang.T("Color de fuente")));
                sp.Children.Add(ColorDots(new[] { ("Blanco", 0xFFFFFF), ("Amarillo", 0xFFFF00), ("Verde", 0x00FF00), ("Celeste", 0x00BFFF), ("Magenta", 0xFF00FF), ("Naranja", 0xFF8000) },
                    SubStyle.FontColor, c => { SubStyle.FontColor = c; SubStyle.Save(); Nav.RefreshCurrent(); }));

                sp.Children.Add(Ui.GroupHeader(Lang.T("Color del fondo")));
                sp.Children.Add(ColorDots(new[] { ("Negro", 0x000000), ("Blanco", 0xFFFFFF), ("Gris", 0x808080), ("Azul", 0x2B4BD8), ("Rojo oscuro", 0x8E1616) },
                    SubStyle.BgColor, c =>
                    {
                        SubStyle.BgColor = c;
                        if (SubStyle.BgOpacity == 0) SubStyle.BgOpacity = 192; // con color elegido, el fondo se ve
                        SubStyle.Save(); Nav.RefreshCurrent();
                    }));

                sp.Children.Add(Ui.GroupHeader(Lang.T("Opacidad del fondo")));
                sp.Children.Add(OpBoxes(SubStyle.BgOpacity, o => { SubStyle.BgOpacity = o; SubStyle.Save(); Nav.RefreshCurrent(); }));

                sp.Children.Add(Ui.GroupHeader(Lang.T("Color del contorno")));
                sp.Children.Add(ColorDots(new[] { ("Negro", 0x000000), ("Blanco", 0xFFFFFF), ("Amarillo", 0xFFFF00), ("Verde", 0x00FF00), ("Celeste", 0x00BFFF), ("Magenta", 0xFF00FF), ("Naranja", 0xFF8000) },
                    SubStyle.OutlineColor, c => { SubStyle.OutlineColor = c; SubStyle.Save(); Nav.RefreshCurrent(); }));
            });
        }

        // Fila de círculos de color (el seleccionado lleva anillo rojo; tooltip con el nombre)
        static WrapPanel ColorDots((string name, int rgb)[] colors, int current, Action<int> onPick)
        {
            var wp = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var (name, c) in colors)
            {
                bool sel = c == current;
                var b = new Border
                {
                    Width = 44, Height = 44,
                    CornerRadius = new CornerRadius(22),
                    Background = Ui.B($"#{c:X6}"),
                    BorderBrush = sel ? Ui.Red : Ui.B("#5A6A73"),
                    BorderThickness = new Thickness(sel ? 3 : 2),
                    Margin = new Thickness(0, 0, 12, 12),
                    Cursor = Cursors.Hand,
                    ToolTip = Lang.T(name),
                };
                int cc = c;
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; onPick(cc); };
                wp.Children.Add(b);
            }
            return wp;
        }

        // Fila de cajas de opacidad (la seleccionada en rojo)
        static WrapPanel OpBoxes(int current, Action<int> onPick)
        {
            var opts = new (string label, int val)[]
                { ("0%", 0), ("25%", 64), ("50%", 128), ("75%", 192), ("100%", 255) };
            int best = opts[0].val, bestDiff = int.MaxValue;
            foreach (var o in opts) { int d = Math.Abs(o.val - current); if (d < bestDiff) { bestDiff = d; best = o.val; } }
            var wp = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var (label, val) in opts)
            {
                bool sel = val == best;
                var b = new Border
                {
                    Background = sel ? Ui.Red : Ui.Panel2,
                    BorderBrush = sel ? Ui.Red : Ui.Brd,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(18, 10, 18, 10),
                    Margin = new Thickness(0, 0, 10, 10),
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = label, FontSize = 15, FontWeight = FontWeights.SemiBold,
                        Foreground = sel ? Brushes.White : Ui.Text,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                int vv = val;
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; onPick(vv); };
                wp.Children.Add(b);
            }
            return wp;
        }

        static SettingSubView BuildLiveSet()
        {
            return new SettingSubView(Lang.T("TV en vivo"), sp =>
            {
                sp.Children.Add(Ui.SwitchRow(Lang.T("Ordenar por grupos"), Lang.T("Agrupa los canales por categoría"), LiveSettings.GroupByCategory, v => LiveSettings.GroupByCategory = v));
                sp.Children.Add(Ui.SwitchRow(Lang.T("Favoritos primero"), Lang.T("Tus favoritos arriba de la lista"), LiveSettings.FavoritesFirst, v => LiveSettings.FavoritesFirst = v));
                sp.Children.Add(Ui.SwitchRow(Lang.T("Ocultar canales sin EPG"), Lang.T("Solo canales con guía"), LiveSettings.HideNoEpg, v => LiveSettings.HideNoEpg = v));
                sp.Children.Add(Ui.Notice(Lang.T("Para marcar favoritos toca la estrella en cada canal.")));
            });
        }

        static SettingSubView BuildVod()
        {
            return new SettingSubView(Lang.T("Películas y Series (VOD)"), sp =>
            {
                var limOpts = new[] { "0", "250", "500", "1000" };
                var limLbl = new[] { Lang.T("Sin límite"), "250", "500", "1000" };
                int limIdx = Array.IndexOf(limOpts, VodSettings.ResultLimit.ToString());
                if (limIdx < 0) limIdx = 0;
                sp.Children.Add(Ui.SettingRow(Lang.T("Límite de resultados"), Lang.T("0 = sin límite; con límite vuelve el botón Mostrar más"), limLbl[limIdx], () =>
                    Nav.Push(new PickerView(Lang.T("Límite de resultados"), limLbl, limLbl[limIdx], v =>
                    {
                        int idx = Array.IndexOf(limLbl, v);
                        VodSettings.ResultLimit = idx > 0 ? int.Parse(limOpts[idx]) : 0;
                        Nav.PickDone();
                    }))));
                sp.Children.Add(Ui.SwitchRow(Lang.T("Mostrar intros"), Lang.T("Imagen mientras carga el video"), VodSettings.ShowIntros, v => VodSettings.ShowIntros = v));
                sp.Children.Add(Ui.SwitchRow(Lang.T("Marcar como visto"), Lang.T("Marca lo que terminas con \u2713"), VodSettings.MarkWatched, v => VodSettings.MarkWatched = v));
            });
        }

        static SettingSubView BuildEpg()
        {
            return new SettingSubView(Lang.T("Guía (EPG)"), sp =>
            {
                sp.Children.Add(Ui.SwitchRow(Lang.T("Activar EPG"), Lang.T("Muestra la sección Guía en la app"), EpgSettings.Enabled, v =>
                {
                    EpgSettings.Enabled = v;
                    Nav.Main?.RefreshTabs();
                    if (!v && Nav.Main?.MainContent.Content is EpgView) Nav.Main.GoTabPublic(0);
                }));
                string srcLbl = EpgSettings.Source == "manual" ? Lang.T("Manual") : Lang.T("Automática");
                sp.Children.Add(Ui.SettingRow(Lang.T("Fuente EPG"), Lang.T("De dónde toma la guía"), srcLbl, () =>
                    Nav.Push(new PickerView(Lang.T("Fuente EPG"), new[] { Lang.T("Automática"), Lang.T("Manual") }, srcLbl,
                        v =>
                        {
                            EpgSettings.Source = v == Lang.T("Manual") ? "manual" : "auto";
                            Nav.PickDone();
                        }))));
                if (EpgSettings.Source == "manual")
                {
                    var txt = new System.Windows.Controls.TextBox { Text = EpgSettings.ManualUrl, FontSize = 14, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 12) };
                    txt.LostFocus += (s, e) => { EpgSettings.ManualUrl = txt.Text.Trim(); };
                    sp.Children.Add(new TextBlock { Text = Lang.T("URL del XMLTV"), Foreground = Ui.Gray, Margin = new Thickness(0, 0, 0, 4) });
                    sp.Children.Add(txt);
                }
                sp.Children.Add(Ui.SwitchRow(Lang.T("Actualizar al iniciar"), Lang.T("Descarga la guía al abrir la app"), EpgSettings.UpdateOnStart, v => EpgSettings.UpdateOnStart = v));
                sp.Children.Add(Ui.SettingRow(Lang.T("Retención"), Lang.T("Cuánto muestra la parrilla"), EpgSettings.PastLabel + " \u2190 \u2022 \u2192 " + EpgSettings.FutureLabel, () => Nav.Push(BuildEpgRetention())));
                string upd = EpgStore.UpdatedUtc == default ? Lang.T("Nunca") : EpgStore.UpdatedUtc.ToLocalTime().ToString("d MMM HH:mm");
                sp.Children.Add(Ui.SettingRow(Lang.T("Última actualización"), null, upd, null));
                var btn = new Button { Content = Lang.T("Actualizar ahora"), Style = Ui.RoundedButtonStyle(Ui.Blue), Margin = new Thickness(0, 14, 0, 0) };
                btn.Click += async (s, e) =>
                {
                    btn.IsEnabled = false;
                    Nav.Toast(Lang.T("Descargando guía..."));
                    try
                    {
                        var pl = PlaylistStore.Active;
                        if (pl != null) await EpgStore.RefreshAsync(pl);
                        Nav.Toast(Lang.T("Guía actualizada"));
                    }
                    catch { Nav.Toast(Lang.T("No se pudo actualizar")); }
                    btn.IsEnabled = true;
                    Nav.Back(); Nav.Push(BuildEpg());
                };
                sp.Children.Add(btn);
            });
        }

        static SettingSubView BuildEpgRetention()
        {
            return new SettingSubView(Lang.T("Retención"), sp =>
            {
                var pastOpts = new string[13];
                for (int i = 0; i <= 12; i++) pastOpts[i] = i == 0 ? "0 h" : i == 1 ? "1 hora" : $"{i} horas";
                sp.Children.Add(Ui.SettingRow(Lang.T("Pasado"), Lang.T("Horas hacia atrás en la parrilla"), EpgSettings.PastLabel, () =>
                    Nav.Push(new PickerView(Lang.T("Pasado"), pastOpts, EpgSettings.PastLabel, v =>
                    {
                        int h = 0;
                        int.TryParse(new string(v.TakeWhile(char.IsDigit).ToArray()), out h);
                        EpgSettings.PastHours = h;
                        Nav.PickDone();
                    }))));
                var futVals = new[] { 3, 6, 12, 24, 48, 72, 96, 120, 144, 168, 192, 216 };
                var futOpts = futVals.Select(h => h < 24 ? $"{h} horas" : h % 24 == 0 ? (h / 24 == 1 ? "1 día" : $"{h / 24} días") : $"{h} horas").ToArray();
                sp.Children.Add(Ui.SettingRow(Lang.T("Futuro"), Lang.T("Cuánto hacia adelante muestra"), EpgSettings.FutureLabel, () =>
                    Nav.Push(new PickerView(Lang.T("Futuro"), futOpts, EpgSettings.FutureLabel, v =>
                    {
                        int idx = Array.IndexOf(futOpts, v);
                        if (idx >= 0) EpgSettings.FutureHours = futVals[idx];
                        Nav.PickDone();
                    }))));
                sp.Children.Add(Ui.Notice(Lang.T("La parrilla se reconstruye al volver a la Guía.")));
            });
        }

        static string AppVersion =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

        static SettingSubView BuildAbout()
        {
            return new SettingSubView(Lang.T("Acerca de"), sp =>
            {
                var asm = Assembly.GetExecutingAssembly();
                string fecha;
                try { fecha = File.GetLastWriteTime(asm.Location).ToString("d MMM yyyy"); }
                catch { fecha = "?"; }
                sp.Children.Add(Ui.SettingRow(Lang.T("Versión"), null, AppVersion, null));
                sp.Children.Add(Ui.SettingRow(Lang.T("Compilación"), null, fecha, null));
                sp.Children.Add(Ui.SettingRow(".NET", null, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, null));
                sp.Children.Add(Ui.SettingRow(Lang.T("Reproductor"), null, "VLC", null));
            });
        }

        static SettingSubView BuildRecordings()
        {
            return new SettingSubView(Lang.T("Grabaciones"), sp =>
            {
                sp.Children.Add(Ui.GroupHeader(Lang.T("Destino")));
                sp.Children.Add(Ui.SettingRow(Lang.T("Carpeta de destino"), Recording.DestFolder, null, () =>
                {
                    var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Lang.T("Carpeta de destino") };
                    if (dlg.ShowDialog() == true)
                    {
                        Recording.DestFolder = dlg.FolderName;
                        Nav.Toast(Lang.T("Carpeta actualizada"));
                        SettingSubView_Refresh(sp);
                    }
                }));

                sp.Children.Add(Ui.GroupHeader(Lang.T("Programadas") + $" ({Recording.Scheduled.Count})"));
                if (Recording.Scheduled.Count == 0)
                    sp.Children.Add(Ui.Notice(Lang.T("Sin grabaciones programadas. Usa clic derecho en un programa de la guía.")));
                foreach (var s in Recording.Scheduled.ToList())
                {
                    var sc = s;
                    string info;
                    if (sc.Active)
                    {
                        var el = DateTime.UtcNow - sc.StartUtc;
                        var rem = sc.EndUtc - DateTime.UtcNow;
                        string fEl = el.TotalHours >= 1 ? $"{(int)el.TotalHours}:{el.Minutes:D2}:{el.Seconds:D2}" : $"{el.Minutes:D2}:{el.Seconds:D2}";
                        string fRem = rem.TotalHours >= 1 ? $"{(int)rem.TotalHours}:{rem.Minutes:D2}:{rem.Seconds:D2}" : $"{rem.Minutes:D2}:{rem.Seconds:D2}";
                        info = $"\u23FA {fEl} / {fRem}";
                    }
                    else
                        info = sc.StartUtc.ToLocalTime().ToString("dd/MM HH:mm") + " - " + sc.EndUtc.ToLocalTime().ToString("HH:mm");
                    sp.Children.Add(Ui.SettingRow(sc.ChannelName + " \u00b7 " + sc.Title, info, null, () => Nav.Push(BuildEditScheduled(sc))));
                }

            });
        }

        static void SettingSubView_Refresh(StackPanel sp)
        {
            // reabrir para refrescar
            Nav.Back(); Nav.Push(BuildRecordings());
        }

        static string FormatBytes(long b)
        {
            if (b < 1024) return b + " B";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0.0") + " KB";
            if (b < 1024L * 1024 * 1024) return (b / 1048576.0).ToString("0.0") + " MB";
            return (b / 1073741824.0).ToString("0.00") + " GB";
        }

        static SettingSubView BuildEditScheduled(Recording.ScheduledRec sc)
        {
            var txtTitle = new System.Windows.Controls.TextBox { Text = sc.Title, Margin = new Thickness(0, 0, 0, 10), FontSize = 15, Padding = new Thickness(8) };
            var txtStart = new System.Windows.Controls.TextBox { Text = sc.StartUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), Margin = new Thickness(0, 0, 0, 10), FontSize = 15, Padding = new Thickness(8) };
            var txtEnd = new System.Windows.Controls.TextBox { Text = sc.EndUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), Margin = new Thickness(0, 0, 0, 10), FontSize = 15, Padding = new Thickness(8) };
            return new SettingSubView(Lang.T("Editar grabación"), sp =>
            {
                sp.Children.Add(Ui.BackHeader(Lang.T("Editar grabación"), () => Nav.Back()));
                sp.Children.Add(new TextBlock { Text = sc.ChannelName, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Ui.Text, Margin = new Thickness(0, 0, 0, 10) });
                sp.Children.Add(new TextBlock { Text = Lang.T("Programa"), Foreground = Ui.Gray, Margin = new Thickness(0, 0, 0, 4) });
                sp.Children.Add(txtTitle);
                sp.Children.Add(new TextBlock { Text = Lang.T("Inicio (aaaa-mm-dd hh:mm)"), Foreground = Ui.Gray, Margin = new Thickness(0, 0, 0, 4) });
                sp.Children.Add(txtStart);
                sp.Children.Add(new TextBlock { Text = Lang.T("Fin (aaaa-mm-dd hh:mm)"), Foreground = Ui.Gray, Margin = new Thickness(0, 0, 0, 4) });
                sp.Children.Add(txtEnd);
                var btnSave = new Button { Content = Lang.T("Guardar"), Style = Ui.RoundedButtonStyle(Ui.Blue), Margin = new Thickness(0, 10, 0, 0) };
                btnSave.Click += (s, e) =>
                {
                    DateTime ns, ne;
                    if (!DateTime.TryParse(txtStart.Text, out ns) || !DateTime.TryParse(txtEnd.Text, out ne) || ne <= ns)
                    { Nav.Toast(Lang.T("Fechas inválidas")); return; }
                    sc.Title = txtTitle.Text.Trim();
                    sc.StartUtc = ns.ToUniversalTime();
                    sc.EndUtc = ne.ToUniversalTime();
                    Recording.UpdateScheduled(sc);
                    Nav.Toast(Lang.T("Guardado"));
                    Nav.Back();
                };
                sp.Children.Add(btnSave);
                var btnDel = new Button { Content = Lang.T("Borrar"), Style = Ui.RoundedButtonStyle(Ui.B("#7a2020")), Margin = new Thickness(0, 10, 0, 0) };
                btnDel.Click += (s, e) => Ui.Confirm(Lang.T("Borrar"), Lang.T("¿Borrar esta grabación programada?"), Lang.T("Borrar"), () =>
                {
                    Recording.RemoveScheduled(sc.Id);
                    Nav.Back(); Nav.Back(); Nav.Push(BuildRecordings());
                });
                sp.Children.Add(btnDel);
            });
        }

        static SettingSubView BuildAdvanced()
        {
            return new SettingSubView(Lang.T("Ajustes avanzados"), sp =>
            {
                sp.Children.Add(Ui.SettingRow(Lang.T("Borrar caché"), Lang.T("Libera espacio de temporales"), null, () =>
                    Ui.Confirm(Lang.T("Borrar caché"),
                        Lang.T("¿Borrar la caché de imágenes? Se volverán a descargar cuando las necesites."),
                        Lang.T("Borrar"),
                        () =>
                        {
                            long freed = ImgCache.Clear();
                            Nav.Toast(Lang.T("Caché borrada") + " (" + ImgCache.FormatSize(freed) + ")");
                        })));
            });
        }
    }
}
