using System;
using System.IO;

namespace TimelapseCapture
{
    /// <summary>
    /// Simple file-based logger for debugging state transitions and issues.
    /// Logs silently to debug.log in application directory.
    /// </summary>
    public static class Logger
    {
        private static readonly string LogPath = Path.Combine(
            AppContext.BaseDirectory, "debug.log");

        private static readonly object _lock = new object();

        /// <summary>
        /// Log a message with timestamp.
        /// </summary>
        public static void Log(string message)
        {
            try
            {
                lock (_lock)
                {
                    string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                    File.AppendAllText(LogPath, entry + Environment.NewLine);
                }
            }
            catch
            {
                // Silent failure - logging should never crash the app
            }
        }

        /// <summary>
        /// Log with category for easier filtering.
        /// </summary>
        public static void Log(string category, string message)
        {
            Log($"[{category}] {message}");
        }

        /// <summary>
        /// Log state information for debugging.
        /// </summary>
        public static void LogState(string component, string state, object? value = null)
        {
            string valueStr = value != null ? $" = {value}" : "";
            Log($"[STATE] {component}.{state}{valueStr}");
        }

        /// <summary>
        /// Clear the log file.
        /// </summary>
        public static void Clear()
        {
            try
            {
                lock (_lock)
                {
                    File.WriteAllText(LogPath, $"=== Log started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
                }
            }
            catch { }
        }
    }
}
