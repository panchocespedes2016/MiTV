using System.Collections.Generic;
using System.Windows.Controls;

namespace MiTV
{
    public static class Nav
    {
        public static MainWindow Main;
        private static readonly Stack<ViewBase> stack = new Stack<ViewBase>();
        private static int lastTab = 0;
        public static int CurrentTab => lastTab;

        // Pantalla principal (pestaña): limpia el historial y muestra chrome
        public static void Tab(ViewBase v, int tabIndex)
        {
            var cur = Main.MainContent.Content as ViewBase;
            cur?.OnLeave();
            stack.Clear();
            lastTab = tabIndex;
            Main.MainContent.Content = v;
            v.OnShow();
            Main.SetChrome(true);
            Main.SelectTab(tabIndex);
        }

        // Sub-pantalla: oculta chrome, apila para volver
        public static void Push(ViewBase v)
        {
            var cur = Main.MainContent.Content as ViewBase;
            if (cur != null) stack.Push(cur);
            Main.MainContent.Content = v;
            v.OnShow();
            Main.SetChrome(false);
        }

        // Re-dibuja la vista de ajustes actual con los valores frescos
        public static void RefreshCurrent()
        {
            if (Main.MainContent.Content is SettingSubView s) s.Rebuild();
        }

        // Cierra el picker y refresca la vista de ajustes que queda debajo
        // (evita apilar una copia nueva sobre la vieja con valores desactualizados)
        public static void PickDone()
        {
            Back();
            RefreshCurrent();
        }

        public static void Back()
        {
            var cur = Main.MainContent.Content as ViewBase;
            cur?.OnLeave();
            if (stack.Count > 0)
            {
                var v = stack.Pop();
                Main.MainContent.Content = v;
                v.OnShow();
                Main.SetChrome(stack.Count == 0);
                if (stack.Count == 0) Main.SelectTab(lastTab);
            }
        }

        // Vuelve hasta la pestaña actual, vaciando la pila (p. ej. tras cambiar el idioma)
        public static void BackToTab()
        {
            while (stack.Count > 0) Back();
        }

        public static void Toast(string msg) => Main.ShowToast(msg);

        // Reconstruye la vista actual si es de contenido, para reflejar
        // una lista recién sincronizada sin tener que salir y volver
        public static void RefreshContent()
        {
            if (Main.MainContent.Content is not ViewBase cur) return;
            ViewBase fresh = cur switch
            {
                HomeView => new HomeView(),
                LiveTvView => new LiveTvView(),
                MoviesView => new MoviesView(),
                ShowsView => new ShowsView(),
                SportsView => new SportsView(),
                EpgView => new EpgView(),
                _ => null
            };
            if (fresh == null) return;
            cur.OnLeave();
            Main.MainContent.Content = fresh;
            fresh.OnShow();
        }
    }
}
