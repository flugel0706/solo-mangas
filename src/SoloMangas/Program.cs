using System;
using System.Windows;
using System.Windows.Threading;

namespace SoloMangasNative
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Modo aplicador: a nova versao e iniciada a partir da pasta temporaria
            // com "--apply-update <alvo> <pid>" e troca o executavel antigo.
            if (args != null && args.Length >= 3 &&
                string.Equals(args[0], "--apply-update", StringComparison.OrdinalIgnoreCase))
            {
                var pid = 0;
                int.TryParse(args[2], out pid);
                UpdateService.ApplyUpdateAndExit(args[1], pid);
                return;
            }

            var app = new Application
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose,
            };

            app.DispatcherUnhandledException += (_, e) =>
            {
                MessageBox.Show("Ocorreu um erro inesperado:\n\n" + e.Exception.Message,
                    "Solo Mangás 2.7", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };

            Theme.Init(app);
            LocalStore.Instance.Load();
            SourceRegistry.Instance.Session.Load();

            var window = new MainWindow();
            app.MainWindow = window;
            window.Show();
            app.Run();
        }
    }
}
