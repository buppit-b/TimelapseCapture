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
            AppPaths.DataDir, "debug.log");

        /// <summary>Full path to the log file (for an "open log" affordance in the UI).</summary>
        public static string FilePath => LogPath;

        private static readonly object _lock = new object();

        // Cap the log so an unattended multi-day run can't grow debug.log without bound.
        private const long MaxLogBytes = 5 * 1024 * 1024; // 5 MB

        /// <summary>
        /// Log a message with timestamp.
        /// </summary>
        public static void Log(string message)
        {
            try
            {
                lock (_lock)
                {
                    RollOverIfTooLarge();
                    string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                    File.AppendAllText(LogPath, entry + Environment.NewLine);
                }
            }
            catch
            {
                // Silent failure - logging should never crash the app
            }
        }

        // Roll debug.log over to debug.log.bak once it exceeds the cap. Caller holds _lock.
        private static void RollOverIfTooLarge()
        {
            try
            {
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > MaxLogBytes)
                {
                    var bak = LogPath + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(LogPath, bak);
                }
            }
            catch { /* if rollover fails, just keep appending */ }
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
