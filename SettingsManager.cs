using System;
using System.Drawing;
using System.IO;
using System.Text.Json;

namespace TimelapseCapture
{
    public class CaptureSettings
    {
        public string? SaveFolder { get; set; }
        public int IntervalSeconds { get; set; } = 5;
        public string? Format { get; set; } = "JPEG";
        public int JpegQuality { get; set; } = 90;
        public Rectangle? Region { get; set; }
        public string? FfmpegPath { get; set; }
        public int AspectRatioIndex { get; set; } = 0; // Default to "Free" aspect ratio
    }




    public static class SettingsManager
    {
        private static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json");

        public static CaptureSettings Load()
        {
            try
            {
                if (!File.Exists(Path))
                    return new CaptureSettings();

                var json = File.ReadAllText(Path);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var s = JsonSerializer.Deserialize<CaptureSettings>(json, opts) ?? new CaptureSettings();
                return s;
            }
            catch
            {
                return new CaptureSettings();
            }
        }

        public static void Save(CaptureSettings settings)
        {
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(settings, opts);
                File.WriteAllText(Path, json);
            }
            catch
            {
                // ignore
            }
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

    }
}