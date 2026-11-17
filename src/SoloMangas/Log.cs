using System;
using System.IO;

namespace SoloMangasNative
{
    /// <summary>Log simples para diagnostico. Ativo apenas com SOLO_DEBUG=1.</summary>
    public static class Log
    {
        private static readonly bool Enabled =
            Environment.GetEnvironmentVariable("SOLO_DEBUG") == "1";

        private static readonly string FilePath = Path.Combine(Path.GetTempPath(), "solo2.log");
        private static readonly object Gate = new object();

        public static void Write(string message)
        {
            if (!Enabled) return;
            try
            {
                lock (Gate)
                    File.AppendAllText(FilePath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}
