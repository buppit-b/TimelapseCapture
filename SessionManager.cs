using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;

namespace TimelapseCapture
{
    /// <summary>
    /// Session metadata with organized folder structure.
    /// </summary>
    public class SessionInfo
    {
        public string? Name { get; set; }
        public int IntervalSeconds { get; set; }
        public int VideoFps { get; set; } = 30;
        public DateTime StartTime { get; set; }
        public bool Active { get; set; } = true;
        public long FramesCaptured { get; set; } = 0;

        // Capture settings to prevent mismatches
        public Rectangle CaptureRegion { get; set; }
        public string? ImageFormat { get; set; }
        public int JpegQuality { get; set; }

        // File organization
        public int FormatVersion { get; set; } = 2;

        // NEW: Track actual capture time and interval changes
        public DateTime? LastCaptureTime { get; set; }
        public double TotalCaptureSeconds { get; set; } = 0; // Actual elapsed time
        public bool IntervalChanged { get; set; } = false; // Flag if interval was changed mid-session
    }


    /// <summary>
    /// Manages timelapse capture sessions with organized folder structure.
    /// </summary>
    public static class SessionManager
    {
        private const string SessionFileName = "session.json";
        private const string FramesFolder = "frames";
        private const string OutputFolder = "output";
        private const string TempFolder = ".temp";

        #region Public Session Operations

        /// <summary>
        /// Get all session folders in captures root.
        /// </summary>
        public static List<string> GetAllSessions(string capturesRoot)
        {
            if (string.IsNullOrEmpty(capturesRoot) || !Directory.Exists(capturesRoot))
                return new List<string>();

            var sessions = new List<string>();
            foreach (var dir in Directory.GetDirectories(capturesRoot))
            {
                var sessionFile = Path.Combine(dir, SessionFileName);
                if (File.Exists(sessionFile))
                {
                    sessions.Add(dir);
                }
            }
            return sessions.OrderByDescending(s => Directory.GetCreationTime(s)).ToList();
        }

        /// <summary>
        /// Find the currently active session.
        /// </summary>
        public static string? FindActiveSession(string capturesRoot)
        {
            if (string.IsNullOrEmpty(capturesRoot) || !Directory.Exists(capturesRoot))
                return null;

            foreach (var dir in Directory.GetDirectories(capturesRoot))
            {
                var sessionFile = Path.Combine(dir, SessionFileName);
                if (File.Exists(sessionFile))
                {
                    try
                    {
                        var json = File.ReadAllText(sessionFile);
                        var s = JsonSerializer.Deserialize<SessionInfo>(json);
                        if (s != null && s.Active)
                        {
                            // Migrate old session format if needed
                            if (s.FormatVersion < 2)
                            {
                                MigrateSessionToV2(dir);
                                s = LoadSession(dir); // Reload after migration
                            }
                            return dir;
                        }
                    }
                    catch { }
                }
            }
            return null;
        }

        /// <summary>
        /// Load session metadata from folder.
        /// </summary>
        public static SessionInfo? LoadSession(string sessionFolder)
        {
            var sessionFile = Path.Combine(sessionFolder, SessionFileName);
            if (!File.Exists(sessionFile))
                return null;

            try
            {
                var json = File.ReadAllText(sessionFile);
                var session = JsonSerializer.Deserialize<SessionInfo>(json);

                // Auto-migrate old format
                if (session != null && session.FormatVersion < 2)
                {
                    MigrateSessionToV2(sessionFolder);
                    session = LoadSession(sessionFolder); // Reload
                }

                return session;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Save session metadata to folder.
        /// </summary>
        public static void SaveSession(string sessionFolder, SessionInfo info)
        {
            var sessionFile = Path.Combine(sessionFolder, SessionFileName);
            var opts = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(info, opts);
            File.WriteAllText(sessionFile, json);
        }

        /// <summary>
        /// Create new session with organized folder structure.
        /// </summary>
        public static string CreateNewSession(string capturesRoot, int intervalSeconds, Rectangle region, string format, int jpegQuality)
        {
            Directory.CreateDirectory(capturesRoot);
            var name = $"session_{DateTime.Now:yyyyMMdd_HHmmss}";
            var folder = Path.Combine(capturesRoot, name);

            // Create organized folder structure
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(folder, FramesFolder));
            Directory.CreateDirectory(Path.Combine(folder, OutputFolder));
            Directory.CreateDirectory(Path.Combine(folder, TempFolder));

            var info = new SessionInfo
            {
                Name = name,
                IntervalSeconds = intervalSeconds,
                VideoFps = 30,
                StartTime = DateTime.UtcNow,
                Active = true,
                FramesCaptured = 0,
                CaptureRegion = region,
                ImageFormat = format,
                JpegQuality = jpegQuality,
                FormatVersion = 2 // New organized format
            };

            SaveSession(folder, info);
            return folder;
        }

        /// <summary>
        /// Create new session with custom name.
        /// </summary>
        public static string CreateNamedSession(
            string capturesRoot,
            string sessionName,
            int intervalSeconds,
            Rectangle region,
            string format,
            int jpegQuality)
        {
            Directory.CreateDirectory(capturesRoot);

            // Sanitize session name for folder
            string safeName = SanitizeFolderName(sessionName);
            string folder = Path.Combine(capturesRoot, safeName);

            // Handle duplicates by appending number
            int counter = 1;
            string originalFolder = folder;
            while (Directory.Exists(folder))
            {
                folder = $"{originalFolder}_{counter}";
                counter++;
            }

            // Create organized folder structure
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(folder, FramesFolder));
            Directory.CreateDirectory(Path.Combine(folder, OutputFolder));
            Directory.CreateDirectory(Path.Combine(folder, TempFolder));

            var info = new SessionInfo
            {
                Name = sessionName, // Store original display name
                IntervalSeconds = intervalSeconds,
                VideoFps = 30,
                StartTime = DateTime.UtcNow,
                Active = true,
                FramesCaptured = 0,
                CaptureRegion = region,
                ImageFormat = format,
                JpegQuality = jpegQuality,
                FormatVersion = 2,
                TotalCaptureSeconds = 0,
                LastCaptureTime = null,
                IntervalChanged = false
            };

            SaveSession(folder, info);
            return folder;
        }

        /// <summary>
        /// Sanitize session name for use as folder name.
        /// Removes invalid characters and limits length.
        /// </summary>
        private static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return $"session_{DateTime.Now:yyyyMMdd_HHmmss}";

            // Remove invalid characters
            char[] invalid = Path.GetInvalidFileNameChars();
            string safe = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));

            // Replace multiple spaces/underscores with single underscore
            while (safe.Contains("__"))
                safe = safe.Replace("__", "_");

            // Trim whitespace and underscores
            safe = safe.Trim(' ', '_');

            // Limit length
            if (safe.Length > 100)
                safe = safe.Substring(0, 100).TrimEnd('_');

            // Final fallback
            if (string.IsNullOrWhiteSpace(safe))
                safe = $"session_{DateTime.Now:yyyyMMdd_HHmmss}";

            return safe;
        }

        /// <summary>
        /// Mark session as inactive (capture stopped).
        /// </summary>
        public static void MarkSessionInactive(string sessionFolder)
        {
            var info = LoadSession(sessionFolder);
            if (info == null)
                return;

            info.Active = false;
            SaveSession(sessionFolder, info);
        }

        /// <summary>
        /// Increment frame count for session.
        /// </summary>
        public static void IncrementFrameCount(string sessionFolder)
        {
            var info = LoadSession(sessionFolder);
            if (info == null)
                return;

            info.FramesCaptured++;
            SaveSession(sessionFolder, info);
        }

        /// <summary>
        /// Validate that current settings match session settings.
        /// </summary>
        public static bool ValidateSessionSettings(SessionInfo session, Rectangle region, string format, int jpegQuality)
        {
            if (session.CaptureRegion != region)
                return false;

            if (session.ImageFormat != format)
                return false;

            if (format == "JPEG" && session.JpegQuality != jpegQuality)
                return false;

            return true;
        }

        /// <summary>
        /// Mark all sessions as inactive (app shutdown).
        /// </summary>
        public static void MarkAllSessionsInactive(string capturesRoot)
        {
            var sessions = GetAllSessions(capturesRoot);
            foreach (var sessionFolder in sessions)
            {
                MarkSessionInactive(sessionFolder);
            }
        }

        #endregion

        #region Folder Path Helpers

        /// <summary>
        /// Get path to frames folder for a session.
        /// </summary>
        public static string GetFramesFolder(string sessionFolder)
        {
            return Path.Combine(sessionFolder, FramesFolder);
        }

        /// <summary>
        /// Get path to output folder for a session.
        /// </summary>
        public static string GetOutputFolder(string sessionFolder)
        {
            return Path.Combine(sessionFolder, OutputFolder);
        }

        /// <summary>
        /// Get path to temp folder for a session.
        /// </summary>
        public static string GetTempFolder(string sessionFolder)
        {
            return Path.Combine(sessionFolder, TempFolder);
        }

        /// <summary>
        /// Get all frame files in session (sorted by name).
        /// </summary>
        public static string[] GetFrameFiles(string sessionFolder)
        {
            var framesPath = GetFramesFolder(sessionFolder);
            if (!Directory.Exists(framesPath))
                return Array.Empty<string>();

            var files = Directory.GetFiles(framesPath, "*.jpg")
                .Concat(Directory.GetFiles(framesPath, "*.png"))
                .Concat(Directory.GetFiles(framesPath, "*.bmp"))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();

            return files;
        }

        /// <summary>
        /// Clean up temp folder (delete filelist.txt, etc).
        /// </summary>
        public static void CleanTempFolder(string sessionFolder)
        {
            try
            {
                var tempPath = GetTempFolder(sessionFolder);
                if (Directory.Exists(tempPath))
                {
                    foreach (var file in Directory.GetFiles(tempPath))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        #endregion

        #region Migration from V1 (Flat) to V2 (Organized)

        /// <summary>
        /// Migrate old flat session structure to organized folders.
        /// </summary>
        private static void MigrateSessionToV2(string sessionFolder)
        {
            try
            {
                // Create new folder structure
                var framesPath = GetFramesFolder(sessionFolder);
                var outputPath = GetOutputFolder(sessionFolder);
                var tempPath = GetTempFolder(sessionFolder);

                Directory.CreateDirectory(framesPath);
                Directory.CreateDirectory(outputPath);
                Directory.CreateDirectory(tempPath);

                // Move all image files to frames/
                var imageExtensions = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp" };
                foreach (var ext in imageExtensions)
                {
                    foreach (var file in Directory.GetFiles(sessionFolder, ext))
                    {
                        var fileName = Path.GetFileName(file);
                        var destPath = Path.Combine(framesPath, fileName);

                        // Don't overwrite if already exists
                        if (!File.Exists(destPath))
                        {
                            File.Move(file, destPath);
                        }
                    }
                }

                // Move all video files to output/
                var videoExtensions = new[] { "*.mp4", "*.avi", "*.mkv", "*.webm" };
                foreach (var ext in videoExtensions)
                {
                    foreach (var file in Directory.GetFiles(sessionFolder, ext))
                    {
                        var fileName = Path.GetFileName(file);
                        var destPath = Path.Combine(outputPath, fileName);

                        if (!File.Exists(destPath))
                        {
                            File.Move(file, destPath);
                        }
                    }
                }

                // Move temp files to .temp/
                var tempFiles = new[] { "filelist.txt" };
                foreach (var fileName in tempFiles)
                {
                    var oldPath = Path.Combine(sessionFolder, fileName);
                    if (File.Exists(oldPath))
                    {
                        var destPath = Path.Combine(tempPath, fileName);
                        if (!File.Exists(destPath))
                        {
                            File.Move(oldPath, destPath);
                        }
                    }
                }

                // Update session format version
                var session = LoadSessionRaw(sessionFolder);
                if (session != null)
                {
                    session.FormatVersion = 2;
                    SaveSession(sessionFolder, session);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Migration failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Load session without triggering migration (to avoid recursion).
        /// </summary>
        private static SessionInfo? LoadSessionRaw(string sessionFolder)
        {
            var sessionFile = Path.Combine(sessionFolder, SessionFileName);
            if (!File.Exists(sessionFile))
                return null;

            try
            {
                var json = File.ReadAllText(sessionFile);
                return JsonSerializer.Deserialize<SessionInfo>(json);
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}