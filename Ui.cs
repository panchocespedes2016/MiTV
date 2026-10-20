using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MiTV
{
    // Base para todas las vistas: permite limpieza al salir (p. ej. parar timers del player)
    // y refresco al mostrarse (p. ej. actualizar valores de ajustes)
    public class ViewBase : UserControl
    {
        // Cada sección puede mostrar/ocultar su barra de búsqueda integrada
        public virtual void ToggleSearch() { }
        public virtual void OnLeave() { }
        public virtual void OnShow() { }
        public virtual void OnEscapeKey() { Nav.Back(); }
    }

    // Diálogo modal de confirmación (vuelve atrás al cancelar)
    public class ConfirmView : ViewBase
    {
        public ConfirmView(string title, string message, string confirmText, Action onConfirm)
        {
            var overlay = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0))
            };
            var card = new Border
            {
                Background = Ui.Panel,
                BorderBrush = Ui.Brd,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(26, 22, 26, 22),
                Width = 400,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            card.MouseLeftButtonUp += (s, e) => e.Handled = true;
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = title, FontSize = 18, FontWeight = FontWeights.Bold,
                Foreground = Ui.Text, TextWrapping = TextWrapping.Wrap
            });
            sp.Children.Add(new TextBlock
            {
                Text = message, FontSize = 14, Foreground = Ui.Gray,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 22)
            });
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button
            {
                Content = Lang.T("Cancelar"), Style = Ui.TransparentButton(),
                FontSize = 15, Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(0, 0, 10, 0)
            };
            cancel.Click += (s, e) => Nav.Back();
            var ok = new Button
            {
                Content = confirmText, Style = Ui.RoundedButtonStyle(Ui.Red),
                FontSize = 15, Padding = new Thickness(18, 10, 18, 10)
            };
            ok.Click += (s, e) => { Nav.Back(); onConfirm?.Invoke(); };
            row.Children.Add(cancel);
            row.Children.Add(ok);
            sp.Children.Add(row);
            card.Child = sp;
            overlay.Children.Add(card);
            overlay.MouseLeftButtonUp += (s, e) => Nav.Back();
            Content = overlay;
        }
    }

    public static class Ui
    {
        // Caja de búsqueda reutilizable para todas las secciones
        public static TextBox SearchBox(string placeholder, Action<string> onText)
        {
            var tb = new TextBox
            {
                FontSize = 14,
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 12),
                Background = Panel2,
                Foreground = Text,
                BorderBrush = Brd,
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };
            // Placeholder
            tb.Text = placeholder;
            tb.Foreground = Gray;
            tb.GotFocus += (s, e) =>
            {
                if (tb.Text == placeholder) { tb.Text = ""; tb.Foreground = Text; }
            };
            tb.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(tb.Text)) { tb.Text = placeholder; tb.Foreground = Gray; }
            };
            tb.TextChanged += (s, e) =>
            {
                string q = tb.Text == placeholder ? "" : tb.Text;
                onText?.Invoke(q);
            };
            return tb;
        }

        public static string Pad2(string s)
        {
            if (int.TryParse(s, out int n)) return n.ToString("D2");
            return s;
        }

        // Normaliza para búsqueda: sin acentos, minúsculas
        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string n = s.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (char c in n)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            return sb.ToString().ToLowerInvariant();
        }
        public static SolidColorBrush B(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static SolidColorBrush Bg => Theme.Brush("Bg");
        public static SolidColorBrush Panel => Theme.Brush("Panel");
        public static SolidColorBrush Panel2 => Theme.Brush("Panel2");
        public static SolidColorBrush Brd => Theme.Brush("Brd");
        public static SolidColorBrush Red => Theme.Brush("Red");
        public static SolidColorBrush Blue => Theme.Brush("Blue");
        public static SolidColorBrush Gray => Theme.Brush("Gray");
        public static SolidColorBrush Text => Theme.Brush("Text");
        public static SolidColorBrush Dim => Theme.Brush("Dim");
        public static SolidColorBrush BarBg => Theme.Brush("BarBg");
        public static SolidColorBrush BarBuf => Theme.Brush("BarBuf");
        public static SolidColorBrush TopBg => Theme.Brush("TopBg");

        public static Style TransparentButton()
        {
            var s = new Style(typeof(Button));
            s.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
            s.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
            s.Setters.Add(new Setter(Button.ForegroundProperty, Text));
            s.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
            s.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(10, 8, 10, 8)));
            s.Setters.Add(new Setter(Button.FontSizeProperty, 20.0));
            return s;
        }

        public static TextBlock SectionTitle(string t)
        {
            return new TextBlock
            {
                Text = t,
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = Text,
                Margin = new Thickness(0, 14, 0, 12)
            };
        }

        // Aviso discreto (p. ej. "sin lista activa")
        public static TextBlock Notice(string t)
        {
            return new TextBlock
            {
                Text = t,
                FontSize = 14,
                Foreground = Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 14)
            };
        }

        public static TextBlock GroupHeader(string t)
        {
            return new TextBlock
            {
                Text = t.ToUpper(),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Red,
                Margin = new Thickness(0, 18, 0, 8)
            };
        }

        // Encabezado con botón atrás para sub-pantallas
        public static DockPanel BackHeader(string title, Action onBack)
        {
            var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var back = new Button { Content = "←", Style = TransparentButton(), Foreground = Gray, FontSize = 22 };
            back.Click += (s, e) => onBack();
            DockPanel.SetDock(back, Dock.Left);
            dock.Children.Add(back);
            dock.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Text,
                VerticalAlignment = VerticalAlignment.Center
            });
            return dock;
        }

        // Tarjeta clicable (película, serie, canal destacado)
        public static Border Card(string title, string sub, string icon, string badge, int progress, Action onClick, string imageUrl = null, bool whiteBorder = false)
        {
            var b = new Border
            {
                Background = Panel,
                BorderBrush = Brd,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Width = 190,
                Margin = new Thickness(0, 0, 14, 0),
                Cursor = Cursors.Hand
            };
            var sp = new StackPanel();
            var thumb = new Border
            {
                Height = 86,
                CornerRadius = new CornerRadius(6),
                Background = new LinearGradientBrush(
                    B("#2A3A44").Color, B("#182227").Color, 45),
                Margin = new Thickness(0, 0, 0, 10),
                BorderBrush = whiteBorder ? Brushes.White : Brushes.Transparent,
                BorderThickness = new Thickness(whiteBorder ? 1 : 0)
            };
            var tg = new Grid();
            var emojiTb = new TextBlock
            {
                Text = icon,
                FontSize = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            tg.Children.Add(emojiTb);
            if (!string.IsNullOrEmpty(badge))
            {
                tg.Children.Add(new Border
                {
                    Background = Red,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(7, 2, 7, 2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(6, 6, 0, 0),
                    Child = new TextBlock { Text = badge, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White }
                });
            }
            thumb.Child = tg;
            sp.Children.Add(thumb);
            if (!string.IsNullOrEmpty(imageUrl))
                ImgCache.Load(thumb, emojiTb, imageUrl, 380);
            var tt = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 14, Foreground = Text, ToolTip = title };
            if (!string.IsNullOrEmpty(title)) tt.Text = title;
            sp.Children.Add(MarqueeWrap(tt));
            var st = new TextBlock { Text = sub ?? "", FontSize = 12, Foreground = Gray, ToolTip = sub };
            var stHost = MarqueeWrap(st);
            stHost.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(stHost);
            if (progress >= 0) sp.Children.Add(Progress(progress, new Thickness(0, 10, 0, 0)));
            b.Child = sp;
            b.MouseLeftButtonUp += (s, e) => onClick?.Invoke();
            b.MouseEnter += (s, e) => b.BorderBrush = Red;
            b.MouseLeave += (s, e) => { if (!b.IsFocused) b.BorderBrush = Brd; };
            // Navegación con teclado/control remoto
            b.Focusable = true;
            b.GotFocus += (s, e) => b.BorderBrush = Red;
            b.LostFocus += (s, e) => b.BorderBrush = Brd;
            b.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Space)
                { e.Handled = true; onClick?.Invoke(); }
            };
            return b;
        }

        // Barra de progreso simple
        public static Grid Progress(double pct, Thickness margin)
        {
            var g = new Grid { Height = 4, Margin = margin };
            g.Children.Add(new Border { Background = BarBg, CornerRadius = new CornerRadius(2) });
            g.Children.Add(new Border
            {
                Background = Blue,
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 190 * pct / 100.0
            });
            return g;
        }

        // Fila de canal (lista)
        public static Border ChannelRow(Channel c, Action onClick, Func<(bool isFav, Action toggle)> fav = null)
        {
            var b = new Border
            {
                Background = Panel,
                BorderBrush = Brd,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = Cursors.Hand
            };
            var dock = new DockPanel();
            if (c.Number > 0)
                dock.Children.Add(new TextBlock { Text = c.Number.ToString(), FontSize = 14, Foreground = Gray, Width = 28, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            if (fav != null)
            {
                var (isFavL, toggleL) = fav();
                var starTxtL = new TextBlock
                {
                    Text = isFavL ? "\u2605" : "\u2606",
                    FontSize = 20, Foreground = isFavL ? B("#FFD500") : Gray,
                    Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = isFavL ? "Quitar de favoritos" : "Agregar a favoritos", Focusable = true
                };
                var starBoxL = new Border
                {
                    Background = Brushes.Transparent, Padding = new Thickness(2),
                    Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                    Child = starTxtL, Focusable = true, Cursor = Cursors.Hand
                };
                starBoxL.GotFocus += (s, e) => starBoxL.Background = B("#2a2a2a");
                starBoxL.LostFocus += (s, e) => starBoxL.Background = Brushes.Transparent;
                Action doToggleL = () =>
                {
                    toggleL?.Invoke();
                    bool nf = false;
                    try { nf = fav().isFav; } catch { }
                    starTxtL.Text = nf ? "\u2605" : "\u2606";
                    starTxtL.Foreground = nf ? B("#FFD500") : Gray;
                    starTxtL.ToolTip = nf ? "Quitar de favoritos" : "Agregar a favoritos";
                };
                starBoxL.MouseLeftButtonUp += (s, e) => { e.Handled = true; doToggleL(); };
                starBoxL.TouchDown += (s, e) => { e.Handled = true; doToggleL(); };
                starBoxL.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Space) { e.Handled = true; doToggleL(); } };
                DockPanel.SetDock(starBoxL, Dock.Left);
                dock.Children.Add(starBoxL);
            }
            var ic = new Border
            {
                Width = 44, Height = 44,
                CornerRadius = new CornerRadius(8),
                Background = Panel2,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 14, 0)
            };
            var icGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            var emoji = new TextBlock { Text = c.Icon, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            icGrid.Children.Add(emoji);
            ic.Child = icGrid;
            if (!string.IsNullOrEmpty(c.Logo))
                ImgCache.Load(ic, emoji, c.Logo, 88);
            DockPanel.SetDock(ic, Dock.Left);
            dock.Children.Add(ic);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(right, Dock.Right);
            var bar = new Grid { Width = 150, Height = 5, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            bar.Children.Add(new Border { Background = BarBg, CornerRadius = new CornerRadius(3) });
            bar.Children.Add(new Border { Background = Blue, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 150 * c.Progress / 100.0 });
            right.Children.Add(bar);
            right.Children.Add(new TextBlock { Text = c.Progress + "%", FontSize = 12, Foreground = Gray, VerticalAlignment = VerticalAlignment.Center });
            dock.Children.Add(right);
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var nameGrid = new Grid();
            nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var nameTb2 = new TextBlock { Text = c.Name, FontWeight = FontWeights.Bold, FontSize = 15, Foreground = Text, ToolTip = c.Name };
            var nameHost = MarqueeWrap(nameTb2);
            Grid.SetColumn(nameHost, 0);
            nameGrid.Children.Add(nameHost);
            if (c.Tag == "catchup")
            {
                var cbadge = new Border { Background = B("#1d4a2a"), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "\u21BA", FontSize = 11, Foreground = B("#3ddc84"), ToolTip = Lang.T("Con repetición") } };
                Grid.SetColumn(cbadge, 1);
                nameGrid.Children.Add(cbadge);
            }
            tx.Children.Add(nameGrid);
            var progTb = new TextBlock { Text = c.Programme, FontSize = 12, Foreground = Gray, ToolTip = c.Programme };
            var progHost = MarqueeWrap(progTb);
            progHost.Margin = new Thickness(0, 2, 0, 0);
            tx.Children.Add(progHost);
            dock.Children.Add(tx);
            b.Child = dock;
            b.MouseLeftButtonUp += (s, e) => onClick?.Invoke();
            b.MouseEnter += (s, e) => b.BorderBrush = Red;
            b.MouseLeave += (s, e) => { if (!b.IsFocused) b.BorderBrush = Brd; };
            // Navegación con teclado/control remoto
            b.Focusable = true;
            b.GotFocus += (s, e) => b.BorderBrush = Red;
            b.LostFocus += (s, e) => b.BorderBrush = Brd;
            b.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Space)
                { e.Handled = true; onClick?.Invoke(); }
            };
            return b;
        }

        // Píldora de categoría / búsqueda reciente
        public static Button Chip(string text, bool selected, Action onClick)
        {
            var b = new Button
            {
                Content = text,
                FontSize = 14,
                Padding = new Thickness(18, 8, 18, 8),
                Margin = new Thickness(0, 0, 10, 0),
                Cursor = Cursors.Hand,
                Background = selected ? Red : Panel2,
                Foreground = selected ? Brushes.White : Text,
                BorderBrush = selected ? Red : Brd,
                BorderThickness = new Thickness(1)
            };
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.TemplateProperty, ChipTemplate()));
            b.Style = style;
            b.Click += (s, e) => onClick?.Invoke();
            return b;
        }

        static ControlTemplate ChipTemplate()
        {
            var t = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(20));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            t.VisualTree = border;
            return t;
        }

        // Botón de categoría para la barra lateral (ancho completo, nombre a la izquierda y conteo a la derecha)
        // Marquesina suave (ping-pong) para un texto que no cabe en su contenedor:
        // se desplaza de un lado al otro solo si el texto es más ancho que el hueco.
        // Se recalcula cuando el contenedor cambia de tamaño (la medida al cargar
        // puede no ser la definitiva y dejar el final del texto sin mostrar).
        // Envuelve un TextBlock en un contenedor con recorte y le aplica la marquesina.
        // El texto va dentro de un Canvas: así el layout nunca lo recorta a un ancho
        // menor y siempre se dibuja la línea completa (solo lo recorta el Border).
        // Devuelve el contenedor para agregarlo al layout en lugar del TextBlock.
        public static Border MarqueeWrap(TextBlock tb)
        {
            var canvas = new Canvas();
            canvas.Children.Add(tb);
            var host = new Border { ClipToBounds = true, VerticalAlignment = tb.VerticalAlignment, Child = canvas };
            ApplyMarquee(tb, host);
            return host;
        }

        public static void ApplyMarquee(TextBlock tb, FrameworkElement clipHost)
        {
            TranslateTransform tr = null;
            double lastAvail = -1;
            Action setup = () =>
            {
                try
                {
                    double avail = clipHost.ActualWidth;
                    if (avail <= 0 || Math.Abs(avail - lastAvail) < 1) return;
                    lastAvail = avail;
                    // El texto vive en un Canvas: su DesiredSize es el ancho real de la
                    // línea completa con el motor de texto verdadero.
                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double fullW = tb.DesiredSize.Width;
                    // El Canvas por sí solo reporta tamaño 0: hay que darle el tamaño
                    // real del texto o el contenedor colapsa y no se ve nada.
                    if (clipHost is Border bd && bd.Child is Canvas cv)
                    {
                        cv.Width = fullW;
                        cv.Height = tb.DesiredSize.Height;
                    }
                    double over = fullW - avail;
                    if (tr == null) { tr = new TranslateTransform(); tb.RenderTransform = tr; }
                    tr.BeginAnimation(TranslateTransform.XProperty, null);
                    tr.X = 0;
                    if (over > 6)
                    {
                        tb.HorizontalAlignment = HorizontalAlignment.Left;
                        double move = Math.Min(20, Math.Max(6, over / 11.0)); // duración del desplazamiento
                        double holdStart = 5.0, holdEnd = 3.0; // pausa de 5 s al inicio y de 3 s al final
                        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                        var kf = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                        kf.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0))));
                        kf.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(holdStart))));
                        kf.KeyFrames.Add(new EasingDoubleKeyFrame(-(over + 6), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(holdStart + move)), ease));
                        kf.KeyFrames.Add(new LinearDoubleKeyFrame(-(over + 6), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(holdStart + move + holdEnd))));
                        kf.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(holdStart + 2 * move + holdEnd)), ease));
                        tr.BeginAnimation(TranslateTransform.XProperty, kf);
                    }
                }
                catch { }
            };
            clipHost.Loaded += (s, e) => clipHost.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, setup);
            clipHost.SizeChanged += (s, e) => setup();
        }

        public static Button CatButton(string text, int count, bool selected, Action onClick)
        {
            var nameColor = selected ? Brushes.White : Text;
            var countColor = selected ? Brushes.White : Gray;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var nameTb = new TextBlock { Text = text, Foreground = nameColor, VerticalAlignment = VerticalAlignment.Center };
            var nameClip = MarqueeWrap(nameTb);
            nameClip.ToolTip = text;
            Grid.SetColumn(nameClip, 0);
            g.Children.Add(nameClip);
            var countTb = new TextBlock { Text = count.ToString(), Foreground = countColor, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(countTb, 1);
            g.Children.Add(countTb);
            var b = new Button
            {
                Content = g,
                FontSize = 14,
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = selected ? Red : Panel,
                BorderThickness = new Thickness(0)
            };
            var style = new Style(typeof(Button));
            var t = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            t.VisualTree = border;
            style.Setters.Add(new Setter(Button.TemplateProperty, t));
            b.Style = style;
            b.Click += (s, e) => onClick?.Invoke();
            return b;
        }

        // Botón "mostrar más" para listas largas (carga por lotes)
        public static Border MoreButton(string text, Action onClick)
        {
            var b = new Border
            {
                Background = Panel,
                BorderBrush = Brd,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Width = 190,
                Height = 110,
                Margin = new Thickness(0, 0, 14, 14),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = Text,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            b.MouseLeftButtonUp += (s, e) => onClick?.Invoke();
            b.MouseEnter += (s, e) => b.BorderBrush = Red;
            b.MouseLeave += (s, e) => { if (!b.IsFocused) b.BorderBrush = Brd; };
            // Navegación con teclado/control remoto
            b.Focusable = true;
            b.GotFocus += (s, e) => b.BorderBrush = Red;
            b.LostFocus += (s, e) => b.BorderBrush = Brd;
            b.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Space)
                { e.Handled = true; onClick?.Invoke(); }
            };
            return b;
        }

        // Columna lateral de categorías: título + lista con scroll. Devuelve el panel donde van los botones.
        public static StackPanel CategorySidebar(Grid host, int column, out ScrollViewer scroller)
        {
            var side = new Grid { Margin = new Thickness(26, 14, 0, 18) };
            side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            side.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var title = SectionTitle(Lang.T("Categorías"));
            Grid.SetRow(title, 0);
            side.Children.Add(title);
            var cats = new StackPanel();
            var sv = new ScrollViewer { Content = cats, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            // Asegurar que la rueda del ratón funcione aunque el foco esté en otro lado
            sv.PreviewMouseWheel += (s, e) => {
                sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
                e.Handled = true;
            };
            scroller = sv;
            Grid.SetRow(sv, 1);
            side.Children.Add(sv);
            Grid.SetColumn(side, column);
            host.Children.Add(side);
            return cats;
        }

        public static Grid TwoColumns(double sideWidth)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(sideWidth) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }

        // Fila de ajuste (navega a submenú o muestra valor)
        public static Border SettingRow(string title, string sub, string value, Action onClick)
        {
            var b = new Border
            {
                Background = Panel,
                BorderBrush = Brd,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18, 14, 18, 14),
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = Cursors.Hand
            };
            var dock = new DockPanel();
            var chev = new TextBlock { Text = "›", FontSize = 18, Foreground = Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            DockPanel.SetDock(chev, Dock.Right);
            dock.Children.Add(chev);
            if (!string.IsNullOrEmpty(value))
            {
                var vv = new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Red, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                DockPanel.SetDock(vv, Dock.Right);
                dock.Children.Add(vv);
            }
            var tx = new StackPanel();
            tx.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Text });
            if (!string.IsNullOrEmpty(sub)) tx.Children.Add(new TextBlock { Text = sub, FontSize = 13, Foreground = Gray, Margin = new Thickness(0, 3, 0, 0) });
            dock.Children.Add(tx);
            b.Child = dock;
            b.MouseLeftButtonUp += (s, e) => onClick?.Invoke();
            b.MouseEnter += (s, e) => b.BorderBrush = Red;
            b.MouseLeave += (s, e) => { if (!b.IsFocused) b.BorderBrush = Brd; };
            // Navegación con teclado/control remoto
            b.Focusable = true;
            b.GotFocus += (s, e) => b.BorderBrush = Red;
            b.LostFocus += (s, e) => b.BorderBrush = Brd;
            b.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Space)
                { e.Handled = true; onClick?.Invoke(); }
            };
            return b;
        }

        // Fila de ajuste con interruptor
        public static Border SwitchRow(string title, string sub, bool initial, Action<bool> onChange)
        {
            var b = new Border
            {
                Background = Panel,
                BorderBrush = Brd,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18, 14, 18, 14),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var dock = new DockPanel();
            var sw = MakeSwitch(initial, v => onChange?.Invoke(v));
            DockPanel.SetDock(sw, Dock.Right);
            dock.Children.Add(sw);
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            tx.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Text });
            if (!string.IsNullOrEmpty(sub)) tx.Children.Add(new TextBlock { Text = sub, FontSize = 13, Foreground = Gray, Margin = new Thickness(0, 3, 0, 0) });
            dock.Children.Add(tx);
            b.Child = dock;
            return b;
        }

        // Interruptor on/off
        public static Button MakeSwitch(bool initial, Action<bool> onChange)
        {
            bool on = initial;
            var outer = new Border
            {
                Width = 52, Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = on ? Blue : B("#3A4A54")
            };
            var dot = new Ellipse
            {
                Width = 24, Height = 24,
                Fill = Brushes.White,
                HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(3)
            };
            outer.Child = dot;
            var btn = new Button
            {
                Content = outer,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(0)
            };
            btn.Click += (s, e) =>
            {
                on = !on;
                outer.Background = on ? Blue : B("#3A4A54");
                dot.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                onChange?.Invoke(on);
            };
            return btn;
        }

        // Opción de lista tipo radio
        public static Border PickerOption(string text, bool selected, Action onClick)
        {
            var b = new Border
            {
                Background = Panel,
                BorderBrush = Brd,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20, 16, 20, 16),
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = Cursors.Hand
            };
            var dock = new DockPanel();
            var radio = new Border
            {
                Width = 24, Height = 24,
                CornerRadius = new CornerRadius(12),
                BorderBrush = selected ? Red : B("#5A6A73"),
                BorderThickness = new Thickness(2),
                Background = selected ? Red : Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (selected) radio.Child = new Border
            {
                Width = 8, Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = Panel,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(radio, Dock.Right);
            dock.Children.Add(radio);
            dock.Children.Add(new TextBlock { Text = text, FontSize = 17, Foreground = Text, VerticalAlignment = VerticalAlignment.Center });
            b.Child = dock;
            b.MouseLeftButtonUp += (s, e) => onClick?.Invoke();
            b.MouseEnter += (s, e) => b.BorderBrush = Red;
            b.MouseLeave += (s, e) => { if (!b.IsFocused) b.BorderBrush = Brd; };
            // Navegación con teclado/control remoto
            b.Focusable = true;
            b.GotFocus += (s, e) => b.BorderBrush = Red;
            b.LostFocus += (s, e) => b.BorderBrush = Brd;
            b.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Space)
                { e.Handled = true; onClick?.Invoke(); }
            };
            return b;
        }

        // Muestra de tema (cuadrito de color)
        public static StackPanel ThemeSwatch(string name, string hex1, string hex2, bool selected, Action onClick)
        {
            var sp = new StackPanel { Width = 76, Margin = new Thickness(0, 0, 12, 0), Cursor = Cursors.Hand };
            var sw = new Border
            {
                Height = 54,
                CornerRadius = new CornerRadius(8),
                BorderBrush = selected ? Red : Brushes.Transparent,
                BorderThickness = new Thickness(2),
                Margin = new Thickness(0, 0, 0, 6)
            };
            if (string.IsNullOrEmpty(hex2)) sw.Background = B(hex1);
            else sw.Background = new LinearGradientBrush(B(hex1).Color, B(hex2).Color, 45);
            sp.Children.Add(sw);
            sp.Children.Add(new TextBlock { Text = name, FontSize = 12, Foreground = selected ? Text : Gray, HorizontalAlignment = HorizontalAlignment.Center });
            sp.MouseLeftButtonUp += (s, e) => onClick?.Invoke();
            return sp;
        }

        // Botón de acción principal
        public static Button ActionButton(string text, Action onClick, bool danger = false)
        {
            var b = new Button
            {
                Content = text,
                Style = RoundedButtonStyle(danger ? B("#8B1E2A") : Red),
                Margin = new Thickness(0, 0, 0, 14),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            b.Click += (s, e) => onClick?.Invoke();
            return b;
        }

        // Muestra un diálogo modal de confirmación
        public static void Confirm(string title, string message, string confirmText, Action onConfirm)
        {
            Nav.Push(new ConfirmView(title, message, confirmText, onConfirm));
        }

        public static Style RoundedButtonStyle(SolidColorBrush bg)
        {
            var s = new Style(typeof(Button));
            s.Setters.Add(new Setter(Button.BackgroundProperty, bg));
            s.Setters.Add(new Setter(Button.ForegroundProperty, Text));
            s.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
            s.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
            s.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(18, 12, 18, 12)));
            s.Setters.Add(new Setter(Button.FontSizeProperty, 16.0));
            s.Setters.Add(new Setter(Button.FontWeightProperty, FontWeights.SemiBold));
            var t = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            t.VisualTree = border;
            s.Setters.Add(new Setter(Button.TemplateProperty, t));
            return s;
        }

        public static TextBlock FieldLabel(string text)
        {
            return new TextBlock { Text = text, FontSize = 13, Foreground = Gray, Margin = new Thickness(0, 0, 0, 6) };
        }

        public static TextBox DarkTextBox(string text)
        {
            return new TextBox
            {
                Text = text ?? "",
                Background = Panel2,
                Foreground = Text,
                CaretBrush = Text,
                BorderBrush = Brd,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10),
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 14)
            };
        }

        public static PasswordBox DarkPasswordBox(string password)
        {
            return new PasswordBox
            {
                Password = password ?? "",
                Background = Panel2,
                Foreground = Text,
                CaretBrush = Text,
                BorderBrush = Brd,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10),
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 14)
            };
        }

        // Barras de desplazamiento oscuras, consistentes con el estilo de la app.
        // Si el XAML no se puede cargar, se deja la barra por defecto (no rompe la app).
        public static void ApplyDarkScrollBars()
        {
            try
            {
                var rd = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(DarkScrollXaml);
                Application.Current.Resources.MergedDictionaries.Add(rd);
            }
            catch { }
        }

        const string DarkScrollXaml =
@"<ResourceDictionary xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Style x:Key=""MiTV_Thumb"" TargetType=""{x:Type Thumb}"">
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""{x:Type Thumb}"">
          <Border x:Name=""bd"" Background=""#3A4A54"" CornerRadius=""4"" Margin=""2""/>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""bd"" Property=""Background"" Value=""#4A5A66""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key=""MiTV_Repeat"" TargetType=""{x:Type RepeatButton}"">
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""{x:Type RepeatButton}"">
          <Border Background=""Transparent""/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType=""{x:Type ScrollBar}"">
    <Setter Property=""Background"" Value=""Transparent""/>
    <Style.Triggers>
      <Trigger Property=""Orientation"" Value=""Vertical"">
        <Setter Property=""Width"" Value=""14""/>
        <Setter Property=""Template"">
          <Setter.Value>
            <ControlTemplate TargetType=""{x:Type ScrollBar}"">
              <Grid Background=""#0C1215"">
                <Track x:Name=""PART_Track"" IsDirectionReversed=""True"">
                  <Track.DecreaseRepeatButton>
                    <RepeatButton Style=""{StaticResource MiTV_Repeat}"" Command=""{x:Static ScrollBar.PageUpCommand}""/>
                  </Track.DecreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb Style=""{StaticResource MiTV_Thumb}""/>
                  </Track.Thumb>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton Style=""{StaticResource MiTV_Repeat}"" Command=""{x:Static ScrollBar.PageDownCommand}""/>
                  </Track.IncreaseRepeatButton>
                </Track>
              </Grid>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
      <Trigger Property=""Orientation"" Value=""Horizontal"">
        <Setter Property=""Height"" Value=""14""/>
        <Setter Property=""Template"">
          <Setter.Value>
            <ControlTemplate TargetType=""{x:Type ScrollBar}"">
              <Grid Background=""#0C1215"">
                <Track x:Name=""PART_Track"">
                  <Track.DecreaseRepeatButton>
                    <RepeatButton Style=""{StaticResource MiTV_Repeat}"" Command=""{x:Static ScrollBar.PageLeftCommand}""/>
                  </Track.DecreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb Style=""{StaticResource MiTV_Thumb}""/>
                  </Track.Thumb>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton Style=""{StaticResource MiTV_Repeat}"" Command=""{x:Static ScrollBar.PageRightCommand}""/>
                  </Track.IncreaseRepeatButton>
                </Track>
              </Grid>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>
</ResourceDictionary>";
    }
}
