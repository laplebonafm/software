using System;
using System.IO;
using System.Text;

namespace VirtualStreamPlayer.Logging
{
    /// <summary>
    /// Minimal thread-safe logger. Writes to %APPDATA%\VirtualStreamPlayer\logs\log-yyyyMMdd.txt
    /// and also raises an event so the UI can show a live tail without polling the file.
    /// </summary>
    public static class Logger
    {
        private static readonly object _lock = new();
        private static readonly string _logDir;

        public static event Action<string>? LineWritten;

        static Logger()
        {
            _logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VirtualStreamPlayer", "logs");
            Directory.CreateDirectory(_logDir);
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message, Exception? ex = null) =>
            Write("ERROR", ex is null ? message : $"{message} :: {ex}");

        private static void Write(string level, string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            lock (_lock)
            {
                var path = Path.Combine(_logDir, $"log-{DateTime.Now:yyyyMMdd}.txt");
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            LineWritten?.Invoke(line);
        }
    }
}
