using System;
using System.IO;

namespace Launcher
{
    public static class Logger
    {
        private static readonly string LogPath = Path.Combine(Application.StartupPath, "launcher.log");

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath, $"{DateTime.Now}: {message}\n");
            }
            catch
            {
                // Ignore logging errors
            }
        }
    }
}