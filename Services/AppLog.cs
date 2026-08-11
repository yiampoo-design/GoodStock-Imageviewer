using System;
using System.IO;

namespace WpfApp1.Services
{
    public static class AppLog
    {
        private static readonly object _lock = new();
        private static readonly string _logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WpfApp1", "logs");

        static AppLog()
        {
            Directory.CreateDirectory(_logDir);
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message, Exception? ex = null)
        {
            var msg = ex != null ? $"{message}: {ex.Message}" : message;
            Write("ERROR", msg);
        }

        private static void Write(string level, string message)
        {
            try
            {
                var path = Path.Combine(_logDir, $"goodstock-{DateTime.Now:yyyyMMdd}.log");
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
                lock (_lock)
                {
                    File.AppendAllText(path, line);
                }
            }
            catch { }
        }
    }
}
