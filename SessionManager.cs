using System;
using System.IO;
using System.Text.Json;

namespace TimelapseCapture
{
    /// <summary>
    /// Persistent capture settings that define how sessions are created.
    /// These settings are validated against existing sessions when resuming.
    /// </summary>
    public class CaptureSettings
    {
        public string CapturesRoot { get; set; } = "";
        public int IntervalSeconds { get; set; } = 5;
        public int VideoFps { get; set; } = 30;
        public string ImageFormat { get; set; } = "jpeg"; // "jpeg" or "png"
        public int JpegQuality { get; set; } = 90;

        // Saved region (so app can restore previous region)
        public int RegionX { get; set; }
        public int RegionY { get; set; }
        public int RegionWidth { get; set; }
        public int RegionHeight { get; set; }

        public string GetRegionKey() => $"{RegionX},{RegionY},{RegionWidth},{RegionHeight}";
    }

    /// <summary>
    /// Handles saving and loading global application settings separate from session metadata.
    /// </summary>
    public static class SettingsManager
    {
        private const string SettingsFileName = "settings.json";

        private static string GetSettingsPath(string baseFolder)
        {
            if (string.IsNullOrEmpty(baseFolder))
                throw new ArgumentNullException(nameof(baseFolder));

            return Path.Combine(baseFolder, SettingsFileName);
        }

        /// <summary>
        /// Load persistent capture settings. Returns defaults if missing.
        /// </summary>
        public static CaptureSettings LoadSettings(string baseFolder)
        {
            string path = GetSettingsPath(baseFolder);
            if (!File.Exists(path))
                return new CaptureSettings(); // default settings

            try
            {
                string json = File.ReadAllText(path);
                var opts = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                return JsonSerializer.Deserialize<CaptureSettings>(json, opts)
                       ?? new CaptureSettings();
            }
            catch
            {
                return new CaptureSettings();
            }
        }

        /// <summary>
        /// Save the current capture settings to disk.
        /// </summary>
        public static void SaveSettings(string baseFolder, CaptureSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            string path = GetSettingsPath(baseFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var opts = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(settings, opts);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Validate settings compatibility with an existing session.
        /// Prevents mixing regions, formats, or quality levels.
        /// </summary>
        public static bool ValidateAgainstSession(SessionInfo session, CaptureSettings settings, out string? reason)
        {
            if (session.CaptureRegion != settings.GetRegionKey())
            {
                reason = "Capture region does not match the active session.";
                return false;
            }

            if (!string.Equals(session.ImageFormat, settings.ImageFormat, StringComparison.OrdinalIgnoreCase))
            {
                reason = "Image format does not match the active session.";
                return false;
            }

            if (session.ImageFormat.Equals("jpeg", StringComparison.OrdinalIgnoreCase) &&
                session.JpegQuality != settings.JpegQuality)
            {
                reason = "JPEG quality does not match the active session.";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
