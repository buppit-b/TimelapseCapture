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

        // Capture settings - OPTIONAL until capture starts
        public Rectangle? CaptureRegion { get; set; } // Nullable - can be set later
        public string? ImageFormat { get; set; }
        public int JpegQuality { get; set; }

        // File organization
        public int FormatVersion { get; set; } = 2;

        // Trim markers — persisted so closing the Trim dialog (accidentally or to do something else)
        // doesn't lose marker placement. 0 = unset. Cleared by a cull (renumbering shifts positions).
        public int TrimStartFrame { get; set; }
        public int TrimEndFrame { get; set; }

        // Cull marks-for-deletion — same persistence contract as the trim markers; cleared once a
        // cull applies (the marks are consumed and the survivors renumber).
        public List<int>? CullMarkedFrames { get; set; }

        // Crop-at-encode (frame-pixel rect, non-destructive — frames on disk stay full size).
        // Null = no crop. Persisted per session like the trim markers.
        public Rectangle? EncodeCrop { get; set; }

        // Track actual capture time and interval changes
        public DateTime? LastCaptureTime { get; set; }
        public double TotalCaptureSeconds { get; set; } = 0;
        public bool IntervalChanged { get; set; } = false;
        
        // ✅ NEW: Smart interval settings
        public bool SmartIntervalEnabled { get; set; }
        public decimal ActiveIntervalSeconds { get; set; } = 2.0m;
        public int IdleThresholdSeconds { get; set; } = 30;
        public bool SkipIdleFrames { get; set; }
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

            // Atomic write: this runs on the capture timer thread once per frame, so a
            // crash/power-loss must never leave session.json truncated (which would make the
            // whole session unloadable on restart even though the frames are still on disk).
            // A transient IO failure (e.g. file lock) must not throw into the capture path.
            try
            {
                var tmp = sessionFile + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(sessionFile))
                    File.Replace(tmp, sessionFile, null);
                else
                    File.Move(tmp, sessionFile);
            }
            catch (Exception ex)
            {
                Logger.Log("Session", $"SaveSession failed for '{sessionFile}': {ex.Message}");
            }
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
        /// Create new session with custom name - region is OPTIONAL.
        /// Region can be set later before starting capture.
        /// </summary>
        public static string CreateNamedSession(
            string capturesRoot,
            string sessionName,
            int intervalSeconds = 5,
            Rectangle? region = null,
            string? format = "JPEG",
            int jpegQuality = 90)
        {
            Directory.CreateDirectory(capturesRoot);

            // Sanitize session name for folder
            string safeName = SanitizeFolderName(sessionName);
            string folder = Path.Combine(capturesRoot, safeName);

            // Handle duplicates by appending number
            int counter = 1;
            string originalFolder = folder;
            string? adjustedName = null;
            while (Directory.Exists(folder))
            {
                adjustedName = $"{sessionName} ({counter})";
                folder = $"{originalFolder}_{counter}";
                counter++;
            }
            
            // If name was adjusted, update the display name
            if (adjustedName != null)
            {
                sessionName = adjustedName;
            }

            // Create organized folder structure
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(folder, FramesFolder));
            Directory.CreateDirectory(Path.Combine(folder, OutputFolder));
            Directory.CreateDirectory(Path.Combine(folder, TempFolder));

            var info = new SessionInfo
            {
                Name = sessionName,
                IntervalSeconds = intervalSeconds,
                VideoFps = 30,
                StartTime = DateTime.UtcNow,
                Active = true,
                FramesCaptured = 0,
                CaptureRegion = region, // Can be null
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
        /// <summary>Turn a display name into a safe, consistent folder name. Public so session CREATE
        /// (CreateNamedSession) and RENAME use the identical rule — they used to diverge.</summary>
        public static string SanitizeFolderName(string name)
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
        /// Increment frame count for session. Returns the updated session, or null if session.json is
        /// missing/unreadable (so the caller can detect a vanished folder without a second read).
        /// </summary>
        public static SessionInfo? IncrementFrameCount(string sessionFolder)
        {
            var info = LoadSession(sessionFolder);
            if (info == null)
                return null;

            info.FramesCaptured++;
            SaveSession(sessionFolder, info);
            return info;
        }

        /// <summary>
        /// Validate that current settings match session settings.
        /// Only validates non-null session properties.
        /// </summary>
        public static bool ValidateSessionSettings(SessionInfo session, Rectangle region, string format, int jpegQuality)
        {
            // If session has a region set and frames captured, it must match
            if (session.CaptureRegion.HasValue && session.FramesCaptured > 0)
            {
                if (session.CaptureRegion.Value != region)
                    return false;
            }

            // If session has frames, format must match
            if (session.FramesCaptured > 0 && session.ImageFormat != null)
            {
                if (session.ImageFormat != format)
                    return false;

                if (format == "JPEG" && session.JpegQuality != jpegQuality)
                    return false;
            }

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
        /// User-initiated frame cull: delete the given frame numbers, then renumber the remaining frames to a
        /// gapless 00001..N sequence (so the image2 encoder stays happy) and update the session's frame count.
        /// Destructive — the caller must confirm. Returns the new frame count.
        /// </summary>
        public static int CullAndRenumber(string sessionFolder, ISet<int> deleteNumbers)
        {
            var framesPath = GetFramesFolder(sessionFolder);
            if (!Directory.Exists(framesPath)) return 0;

            // GetFrameFiles is ordinal-sorted, i.e. ascending frame number for zero-padded names. Process in
            // that order and re-assign the next sequential number; because the new number is always <= the old
            // one and lower slots are already vacated, no rename can clobber a not-yet-processed frame.
            int next = 1;
            foreach (var file in GetFrameFiles(sessionFolder))
            {
                if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out int num))
                    continue; // ignore anything that isn't a numbered frame

                if (deleteNumbers.Contains(num))
                {
                    try { File.Delete(file); }
                    catch (Exception ex) { Logger.Log("Cull", $"Delete {file} failed: {ex.Message}"); }
                    continue;
                }

                string target = Path.Combine(framesPath, $"{next:D5}{Path.GetExtension(file)}");
                if (!string.Equals(file, target, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Move(file, target, true); }
                    catch (Exception ex) { Logger.Log("Cull", $"Renumber {file} -> {target} failed: {ex.Message}"); }
                }
                next++;
            }

            int newCount = GetFrameFiles(sessionFolder).Length;   // true count from what's actually on disk
            var info = LoadSession(sessionFolder);
            if (info != null) { info.FramesCaptured = newCount; SaveSession(sessionFolder, info); }
            return newCount;
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

        /// <summary>Frame counts by extension (lowercase, no dot) — more than one key = a mixed session.</summary>
        public static Dictionary<string, int> GetFrameFormatCounts(string sessionFolder)
        {
            return GetFrameFiles(sessionFolder)
                .GroupBy(f => Path.GetExtension(f).TrimStart('.').ToLowerInvariant())
                .Where(g => g.Key.Length > 0)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        /// <summary>
        /// Unify a mixed-format session (a mid-session PNG toggle breaks the image2 encode): re-encode
        /// every frame whose extension differs from <paramref name="targetExt"/> ("jpg"/"png") in place —
        /// same frame number, new extension, original deleted only after the replacement is written.
        /// Returns the number converted. Never call while capturing into this session.
        /// </summary>
        public static int ConvertFramesToFormat(string sessionFolder, string targetExt, int jpegQuality = 90)
        {
            targetExt = targetExt.TrimStart('.').ToLowerInvariant();
            int converted = 0;
            foreach (var src in GetFrameFiles(sessionFolder))
            {
                string ext = Path.GetExtension(src).TrimStart('.').ToLowerInvariant();
                if (ext == targetExt) continue;
                string dst = Path.ChangeExtension(src, "." + targetExt);
                using (var img = Image.FromFile(src))
                {
                    if (targetExt == "png") img.Save(dst, System.Drawing.Imaging.ImageFormat.Png);
                    else SaveJpeg(img, dst, jpegQuality);
                }
                File.Delete(src);   // only after the replacement exists — a crash mid-run loses nothing
                converted++;
            }
            return converted;
        }

        /// <summary>
        /// The session's canonical frame size — the dimensions of the first frame on disk (what the
        /// image2 encode requires every frame to match). Empty when there are no frames / unreadable.
        /// </summary>
        public static Size GetFrameSize(string sessionFolder)
        {
            try
            {
                // The NEWEST frame: for a healthy session every frame matches; for a legacy mixed-size
                // one, matching the newest block preserves the tail a user would trim/continue from.
                var last = GetFrameFiles(sessionFolder).LastOrDefault();
                if (last == null) return Size.Empty;
                using var img = Image.FromFile(last);
                return img.Size;
            }
            catch { return Size.Empty; }
        }

        /// <summary>
        /// DESTRUCTIVELY crop every frame on disk to <paramref name="crop"/> (frame-pixel rect) — a
        /// power-user space saver; the caller must obtain explicit consent first. Each frame is
        /// re-encoded cropped and written via temp+replace, so a crash mid-run leaves the frame being
        /// processed with its original content. The crop is clamped per-frame and forced to even dims.
        /// Returns the number of frames cropped. Never call while capturing into the session.
        /// </summary>
        public static int CropFrames(string sessionFolder, Rectangle crop, int jpegQuality = 90)
        {
            int done = 0;
            foreach (var src in GetFrameFiles(sessionFolder))
            {
                string ext = Path.GetExtension(src).TrimStart('.').ToLowerInvariant();
                string tmp = src + ".croptmp";
                using (var img = Image.FromFile(src))
                {
                    var r = VideoEncoder.ClampCrop(crop, img.Size);
                    if (r.Width < 2 || r.Height < 2) continue;                    // degenerate — skip
                    if (r.Width == img.Width && r.Height == img.Height) continue; // full-frame = no-op
                    using var dst = new Bitmap(r.Width, r.Height);
                    using (var g = Graphics.FromImage(dst))
                        g.DrawImage(img, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
                    if (ext == "png") dst.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);
                    else SaveJpeg(dst, tmp, jpegQuality);
                }
                File.Replace(tmp, src, null);   // original intact until the cropped copy is fully written
                done++;
            }
            return done;
        }

        // JPEG must go through a quality-parameterised encoder — plain Image.Save(file, Jpeg) silently
        // ignores quality (the same trap CaptureEngine.SaveBitmap avoids).
        private static void SaveJpeg(Image img, string path, int quality)
        {
            var codec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                .First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
            using var p = new System.Drawing.Imaging.EncoderParameters(1);
            p.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                System.Drawing.Imaging.Encoder.Quality, (long)Math.Clamp(quality, 1, 100));
            img.Save(path, codec, p);
        }

        /// <summary>
        /// Find the session folder containing <paramref name="path"/>: the folder itself, or up to
        /// <paramref name="maxLevelsUp"/> parents — the first with a loadable session.json wins. Accepts a
        /// file path (a frame, the mp4, session.json itself) or a folder. Null if nothing resolves — e.g.
        /// the captures root or an unrelated drop, where walking UP would find the wrong thing.
        /// Used by drag-drop onto the window and the exe's command-line argument.
        /// </summary>
        public static string? FindSessionRoot(string? path, int maxLevelsUp = 2)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                string? dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
                for (int i = 0; dir != null && i <= maxLevelsUp; i++, dir = Path.GetDirectoryName(dir))
                    if (Directory.Exists(dir) && LoadSession(dir) != null)
                        return dir;
            }
            catch { /* malformed path → null */ }
            return null;
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