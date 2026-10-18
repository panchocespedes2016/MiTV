using System;
using System.Windows;

namespace MiTV
{
    public class App : Application
    {
        [STAThread]
        public static void Main()
        {
            // El límite de conexiones simultáneas por servidor se configura en
            // ImgCache mediante SocketsHttpHandler.MaxConnectionsPerServer.
            var app = new App();
            Ui.ApplyDarkScrollBars();
            app.Run(new MainWindow());
        }
    }
}
