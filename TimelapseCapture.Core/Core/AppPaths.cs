using System;
using System.IO;

namespace TimelapseCapture
{
    /// <summary>
    /// Where the app keeps its own data (settings.json, debug.log, the downloaded ffmpeg).
    ///
    /// Two modes, self-selecting once at startup:
    /// - PORTABLE: a settings.json already sits next to the exe (dev builds, or a user who keeps
    ///   the app self-contained on a USB stick) → keep everything there, exactly as before.
    /// - INSTALLED: no exe-side settings.json → %APPDATA%\FrameWrite. This is what makes an
    ///   installer possible at all: under Program Files the exe folder isn't writable, so writing
    ///   next to the exe would silently lose settings, the log, and ffmpeg.
    ///
    /// Sessions are unaffected either way — frames live in the user-chosen output folder.
    /// </summary>
    public static class AppPaths
    {
        public static string DataDir { get; } = Init();

        private static string Init()
        {
            string exeDir = AppContext.BaseDirectory;
            string dir = ResolveDataDir(
                exeDir,
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                File.Exists(Path.Combine(exeDir, "settings.json")));
            try { Directory.CreateDirectory(dir); }
            catch { dir = exeDir; }   // APPDATA unavailable/uncreatable — fall back to the old behavior
            return dir;
        }

        /// <summary>The pure resolution rule (extracted for unit tests).</summary>
        internal static string ResolveDataDir(string exeDir, string appDataRoot, bool portableSettingsExist)
            => portableSettingsExist || string.IsNullOrWhiteSpace(appDataRoot)
                ? exeDir
                : Path.Combine(appDataRoot, "FrameWrite");
    }
}
