using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MiTV
{
    public class SearchView : ViewBase
    {
        TextBox input;
        StackPanel results;
        StackPanel recentBox;
        StackPanel filterPanel;
        ComboBox catFilter;
        CheckBox favFilter;
        CheckBox catchupFilter;
        int scope; // 0=todos, 1=TV, 2=Películas, 3=Series, 4=Deportes, 5=Guía, 6=Repetición
        bool closed;

        public override void ToggleSearch() => input?.Focus();

        public SearchView(int scopeTab = -1)
        {
            scope = scopeTab < 0 ? 0 : scopeTab;
            var sp = new StackPanel { Margin = new Thickness(26, 14, 26, 18) };
            string scopeName = scope switch
            {
                1 => Lang.T("TV en vivo"),
                2 => Lang.T("Películas"),
                3 => Lang.T("Series"),
                4 => Lang.T("Deportes"),
                5 => Lang.T("Guía"),
                6 => Lang.T("Repetición"),
                _ => ""
            };
            string title = string.IsNullOrEmpty(scopeName) ? Lang.T("Buscar") : Lang.T("Buscar en ") + scopeName;
            sp.Children.Add(Ui.BackHeader(title, () => Nav.Back()));

            var bar = new Border
            {
                Background = Ui.Panel2,
                BorderBrush = Ui.Red,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var dock = new DockPanel();
            var clear = new Button { Content = "✕", Style = Ui.TransparentButton(), Foreground = Ui.Gray, FontSize = 18 };
            clear.Click += (s, e) => { input.Text = ""; input.Focus(); };
            DockPanel.SetDock(clear, Dock.Right);
            dock.Children.Add(clear);
            dock.Children.Add(new TextBlock { Text = "🔍", FontSize = 18, Foreground = Ui.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            input = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Ui.Text,
                CaretBrush = Ui.Text,
                FontSize = 17,
                VerticalAlignment = VerticalAlignment.Center
            };
            input.TextChanged += (s, e) => DoSearch();
            dock.Children.Add(input);
            bar.Child = dock;
            sp.Children.Add(bar);

            // Filtros según el ámbito
            filterPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            BuildFilters();
            sp.Children.Add(filterPanel);

            results = new StackPanel();
            sp.Children.Add(results);

            recentBox = new StackPanel();
            recentBox.Children.Add(Ui.SectionTitle(Lang.T("Búsquedas recientes")));
            var chips = new WrapPanel();
            foreach (var r in Data.Recent)
            {
                string rr = r;
                var ch = Ui.Chip(r, false, () => { input.Text = rr; });
                ch.Margin = new Thickness(0, 0, 10, 10);
                chips.Children.Add(ch);
            }
            recentBox.Children.Add(chips);
            sp.Children.Add(recentBox);

            Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Loaded += (s, e) => input.Focus();
            Unloaded += (s, e) => closed = true;
        }

        static bool IsSportsChannel(LiveChannel c)
        {
            string n = Norm(c.Name + " " + c.Group);
            return n.Contains("sport") || n.Contains("deporte") || n.Contains("espn") || n.Contains("fox sport");
        }

        static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string n = s.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (char c in n)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            return sb.ToString().ToLowerInvariant();
        }

        void BuildFilters()
        {
            filterPanel.Children.Clear();
            // Categorías según ámbito
            var cats = new System.Collections.Generic.List<string> { Lang.T("Todas") };
            if (scope == 0 || scope == 1 || scope == 4 || scope == 5 || scope == 6)
                cats.AddRange(ContentStore.Channels.Select(c => c.Group).Where(g => !string.IsNullOrEmpty(g)).Distinct().OrderBy(g => g));
            else if (scope == 2)
                cats.AddRange(ContentStore.Movies.Select(m => m.Category).Where(g => !string.IsNullOrEmpty(g)).Distinct().OrderBy(g => g));
            else if (scope == 3)
                cats.AddRange(ContentStore.Series.Select(s => s.Category).Where(g => !string.IsNullOrEmpty(g)).Distinct().OrderBy(g => g));

            var lbl = new TextBlock { Text = Lang.T("Categoría:"), Foreground = Ui.Gray, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            filterPanel.Children.Add(lbl);
            catFilter = new ComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            foreach (var c in cats) catFilter.Items.Add(c);
            catFilter.SelectedIndex = 0;
            catFilter.SelectionChanged += (s, e) => DoSearch();
            filterPanel.Children.Add(catFilter);

            favFilter = new CheckBox { Content = Lang.T("Solo favoritos"), Foreground = Ui.Text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            favFilter.Checked += (s, e) => DoSearch();
            favFilter.Unchecked += (s, e) => DoSearch();
            filterPanel.Children.Add(favFilter);

            if (scope == 1 || scope == 6 || scope == 0)
            {
                catchupFilter = new CheckBox { Content = Lang.T("Con repetición"), Foreground = Ui.Text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
                catchupFilter.Checked += (s, e) => DoSearch();
                catchupFilter.Unchecked += (s, e) => DoSearch();
                filterPanel.Children.Add(catchupFilter);
            }
        }

        void DoSearch()
        {
            string q = Norm((input.Text ?? "").Trim());
            results.Children.Clear();
            // Filtros activos (para saber si hay búsqueda aunque q esté vacío)
            bool hasFilter = (catFilter?.SelectedIndex ?? 0) > 0 || (favFilter?.IsChecked == true) || (catchupFilter?.IsChecked == true);
            recentBox.Visibility = (q.Length > 0 || hasFilter) ? Visibility.Collapsed : Visibility.Visible;
            if (q.Length == 0 && !hasFilter) return;

            // Filtros activos
            string selCat = catFilter?.SelectedItem as string;
            bool allCats = selCat == null || selCat == Lang.T("Todas");
            bool onlyFav = favFilter?.IsChecked == true;
            bool onlyCatchup = catchupFilter?.IsChecked == true;

            // Según el ámbito, buscar solo en esa sección
            bool inTv = scope == 0 || scope == 1;
            bool inMovies = scope == 0 || scope == 2;
            bool inSeries = scope == 0 || scope == 3;
            bool inSports = scope == 0 || scope == 4;
            // Películas: rellenar metadatos que falten en segundo plano y re-buscar al terminar
            if (inMovies && q.Length >= 3)
                VodMetaCache.EnsureAllAsync(() => Dispatcher.Invoke(() => { if (!closed) DoSearch(); }));
            // Guía y Repetición usan canales de TV
            var fcQ = (inTv || scope == 5 || scope == 6 ? ContentStore.Channels.Where(c => Norm(c.Name).Contains(q)) : Enumerable.Empty<LiveChannel>());
            // Películas: por título, reparto o director (el reparto sale de la caché de metadatos)
            var fmQ = (inMovies ? ContentStore.Movies.Where(m =>
                Norm(m.Name).Contains(q)
                || Norm(VodMetaCache.Get(VodMetaCache.EffectiveSid(m))?.Cast).Contains(q)
                || Norm(VodMetaCache.Get(VodMetaCache.EffectiveSid(m))?.Director).Contains(q)
            ) : Enumerable.Empty<VodMovie>());
            var fsQ = (inSeries ? ContentStore.Series.Where(s => Norm(s.Name).Contains(q)) : Enumerable.Empty<SerieItem>());
            // Deportes: filtrar canales deportivos
            if (inSports && scope != 0)
                fcQ = fcQ.Where(c => IsSportsChannel(c));
            // Filtro de categoría
            if (!allCats)
            {
                fcQ = fcQ.Where(c => c.Group == selCat);
                fmQ = fmQ.Where(m => m.Category == selCat);
                fsQ = fsQ.Where(s => s.Category == selCat);
            }
            // Solo favoritos
            if (onlyFav)
            {
                fcQ = fcQ.Where(c => Favorites.IsFavKey(c.StreamUrl));
                fmQ = fmQ.Where(m => Favorites.IsFavKey(m.StreamUrl));
                fsQ = fsQ.Where(s => Favorites.IsFavKey("serie:" + s.SerieId));
            }
            // Con repetición
            if (onlyCatchup)
                fcQ = fcQ.Where(c => c.HasCatchup);
            var fc = fcQ.Take(50).ToArray();
            var fm = fmQ.Take(50).ToArray();
            var fs = fsQ.Take(50).ToArray();

            if (fc.Length > 0)
            {
                results.Children.Add(Ui.SectionTitle(string.Format(Lang.T("Canales ({0})"), fc.Length)));
                foreach (var c in fc)
                {
                    var cc = c;
                    results.Children.Add(Ui.ChannelRow(new Channel(cc.Name, "", "\U0001F4FA", 0) { Logo = cc.Logo },
                        () => PlayerView.Open(cc.Name, "", true, cc.StreamUrl, cc.Logo),
                        () => (Favorites.IsFavKey(cc.StreamUrl), () => Favorites.ToggleKey(cc.StreamUrl))));
                }
            }
            if (fm.Length > 0)
            {
                results.Children.Add(Ui.SectionTitle(string.Format(Lang.T("Películas ({0})"), fm.Length)));
                foreach (var m in fm)
                {
                    var mm = m;
                    results.Children.Add(Ui.ChannelRow(new Channel(mm.Name, "", "\U0001F3AC", 0) { Logo = mm.Logo },
                        () => Nav.Push(new MovieDetailView(mm)),
                        () => (Favorites.IsFavKey(mm.StreamUrl), () => Favorites.ToggleKey(mm.StreamUrl))));
                }
            }
            if (fs.Length > 0)
            {
                results.Children.Add(Ui.SectionTitle(string.Format(Lang.T("Series ({0})"), fs.Length)));
                foreach (var s in fs)
                {
                    var ss = s;
                    results.Children.Add(Ui.ChannelRow(new Channel(ss.Name, "", "\U0001F3AD", 0) { Logo = ss.Cover },
                        () => Nav.Push(new SerieDetailView(ss)),
                        () => (Favorites.IsFavKey("serie:" + ss.SerieId), () => Favorites.ToggleKey("serie:" + ss.SerieId))));
                }
            }
            if (fc.Length + fm.Length + fs.Length == 0)
                results.Children.Add(new TextBlock { Text = "Sin resultados para \"" + input.Text + "\"", Foreground = Ui.Gray, Margin = new Thickness(0, 20, 0, 0) });
        }

        ScrollViewer HRow(System.Collections.Generic.IEnumerable<Border> cards)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var c in cards) row.Children.Add(c);
            return new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }
    }
}
