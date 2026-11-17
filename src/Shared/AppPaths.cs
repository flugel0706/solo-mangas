using System;
using System.IO;

namespace SoloMangas.Shared
{
    /// <summary>Locais de dados do app (sessao, cache do WebView2).</summary>
    public static class AppPaths
    {
        public static string DataDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MangeekPC");

        public static string SessionFile => Path.Combine(DataDir, "session.json");

        // Sessao criada pela versao Electron (v1.0) - importada no primeiro uso.
        public static string LegacySessionFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "mangeek-pc",
            "session.json");

        public static string WebViewData => Path.Combine(DataDir, "WebView2");

        public static string WebViewLoginData => Path.Combine(DataDir, "WebView2-login");

        // Porta fixa do servidor local: mantem a mesma origem entre execucoes,
        // o que preserva localStorage (configuracoes, Minha lista, leituras).
        public static string RendererPortFile => Path.Combine(DataDir, "renderer-port.txt");

        public static void EnsureDataDir()
        {
            try { Directory.CreateDirectory(DataDir); } catch { }
            try { Directory.CreateDirectory(WebViewData); } catch { }
            try { Directory.CreateDirectory(WebViewLoginData); } catch { }
        }
    }
}
