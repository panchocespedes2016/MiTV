using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MiTV
{
    // Editor de categorías: chips con circulito (lleno = activa, vacío = desactivada).
    // Tocar en cualquier parte del chip alterna. Buscador para filtrar.
    public class CatEditorView : ViewBase
    {
        bool dirty;
        string curTab = "tv";
        string filter = "";
        StackPanel tabsPanel;
        TextBox searchBox;
        bool searchHint = true;
        TextBlock summaryText;
        WrapPanel chipPanel;
        List<(string name, bool hidden)> shown = new();

        public CatEditorView()
        {
            var sp = new StackPanel { Margin = new Thickness(26, 14, 26, 18) };
            sp.Children.Add(Ui.BackHeader(Lang.T("Editor de categorías"), GoBack));

            tabsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            sp.Children.Add(tabsPanel);
            BuildTabs();

            searchBox = Ui.DarkTextBox("");
            searchBox.Margin = new Thickness(0, 0, 0, 14);
            SetSearchHint();
            searchBox.GotFocus += (s, e) =>
            {
                if (searchHint) { searchHint = false; searchBox.Text = ""; searchBox.Foreground = Ui.Text; }
            };
            searchBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(searchBox.Text)) SetSearchHint();
            };
            searchBox.TextChanged += (s, e) =>
            {
                if (searchHint) return;
                filter = searchBox.Text ?? "";
                RefreshChips();
            };
            sp.Children.Add(searchBox);

            var sumRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            summaryText = new TextBlock { FontSize = 14, Foreground = Ui.Gray, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(summaryText, Dock.Left);
            sumRow.Children.Add(summaryText);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            btns.Children.Add(Ui.Chip(Lang.T("Activar todas"), false, () => SetAll(true)));
            btns.Children.Add(Ui.Chip(Lang.T("Desactivar todas"), false, () => SetAll(false)));
            sumRow.Children.Add(btns);
            sp.Children.Add(sumRow);

            chipPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(chipPanel);
            RefreshChips();

            Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        public override void OnEscapeKey() => GoBack();

        void SetSearchHint()
        {
            searchHint = true;
            searchBox.Text = "🔍 " + Lang.T("Buscar categoría…");
            searchBox.Foreground = Ui.Gray;
        }

        void BuildTabs()
        {
            tabsPanel.Children.Clear();
            string t = curTab;
            tabsPanel.Children.Add(Ui.Chip(Lang.T("TV en vivo"), t == "tv", () => SetTab("tv")));
            tabsPanel.Children.Add(Ui.Chip(Lang.T("Películas"), t == "movies", () => SetTab("movies")));
            tabsPanel.Children.Add(Ui.Chip(Lang.T("Series"), t == "series", () => SetTab("series")));
        }

        void SetTab(string tab)
        {
            if (curTab == tab) return;
            curTab = tab;
            filter = "";
            SetSearchHint();
            BuildTabs();
            RefreshChips();
        }


        IEnumerable<string> GroupsOf(string section) => section switch
        {
            "movies" => ContentStore.Movies.Select(m => m.Category),
            "series" => ContentStore.Series.Select(s => s.Category),
            _ => ContentStore.Channels.Select(c => c.Group),
        };

        void RefreshChips()
        {
            chipPanel.Children.Clear();
            shown.Clear();
            var names = GroupsOf(curTab)
                .Where(g => !string.IsNullOrEmpty(g))
                .Distinct()
                .Union(CatFilter.HiddenNames(curTab))
                .OrderBy(n => n)
                .ToList();
            string f = (filter ?? "").Trim();
            int active = 0;
            foreach (var name in names)
            {
                if (f.Length > 0 && name.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool isHidden = CatFilter.IsHidden(curTab, name);
                if (!isHidden) active++;
                string nn = name;
                shown.Add((nn, isHidden));
                chipPanel.Children.Add(CatChip(nn, isHidden, () => Toggle(nn)));
            }
            summaryText.Text = string.Format(Lang.T("{0} de {1} activas"), active, shown.Count);
        }

        Border CatChip(string name, bool hidden, Action onToggle)
        {
            var dot = new Ellipse
            {
                Width = 14, Height = 14,
                Fill = hidden ? Brushes.Transparent : Ui.Blue,
                Stroke = hidden ? Ui.Gray : null,
                StrokeThickness = 2,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(dot);
            row.Children.Add(new TextBlock
            {
                Text = name, FontSize = 14,
                Foreground = hidden ? Ui.Gray : Ui.Text,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var tint = Ui.Red.Color;
            var b = new Border
            {
                Background = hidden ? Ui.Panel : new SolidColorBrush(Color.FromArgb(36, tint.R, tint.G, tint.B)),
                BorderBrush = hidden ? Ui.Brd : Ui.Red,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 9, 14, 9),
                Margin = new Thickness(0, 0, 10, 10),
                Cursor = Cursors.Hand,
                Child = row,
            };
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; onToggle(); };
            return b;
        }

        void Toggle(string name)
        {
            if (CatFilter.IsHidden(curTab, name))
                CatFilter.Unhide(curTab, name);
            else
                CatFilter.Hide(curTab, name);
            dirty = true;
            RefreshChips();
        }

        void SetAll(bool activate)
        {
            var targets = shown.Where(x => x.hidden == activate).Select(x => x.name).ToList();
            if (targets.Count == 0) return;
            if (activate)
                CatFilter.UnhideMany(curTab, targets);
            else
                foreach (var n in targets)
                    CatFilter.Hide(curTab, n);
            dirty = true;
            RefreshChips();
        }

        void GoBack()
        {
            if (dirty) { Nav.BackToTab(); Nav.RefreshContent(); }
            else Nav.Back();
        }
    }
}
