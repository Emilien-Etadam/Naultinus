using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Journal minimal vers <c>%TEMP%\Naultinus_startup.log</c> (toutes configurations) + <see cref="Debug"/> en build Debug.
    /// </summary>
    public static class NaultinusDiagnostics
    {
        private static readonly object FileLock = new object();

        public static string LogFilePath => Path.Combine(Path.GetTempPath(), "Naultinus_startup.log");

        /// <summary>Écrit toujours sur disque (verbeux réservé au démarrage via <see cref="LogDebug"/>).</summary>
        public static void Log(string category, string message, Exception? ex = null)
        {
            try
            {
                var line = DateTime.Now.ToString("o") + " [" + category + "] " + message;
                if (ex != null)
                    line += Environment.NewLine + ex;
                lock (FileLock)
                    File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
            catch
            {
                // Dernier recours : ne pas relancer depuis le chemin de diagnostic.
            }

            Debug.WriteLine("[" + category + "] " + message);
            if (ex != null)
                Debug.WriteLine(ex.ToString());
        }

        [Conditional("DEBUG")]
        public static void LogDebug(string message, Exception? ex = null)
        {
            Log("Debug", message, ex);
        }
    }
}
