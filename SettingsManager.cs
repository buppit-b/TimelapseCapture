using System;
using System.IO;
using System.Text.Json;

namespace TimelapseCapture
{
    /// <summary>
    /// Holds configuration values for timelapse capture.
    /// </summary>
    public class CaptureSettings
    {
        /// <summary>
        /// Directory under which all capture sessions/images are stored.
        /// </summary>
        public string CapturesRoot { get; set; } = "";

        /// <summary>
        /// Interval between frames (in seconds).
        /// </summary>
        public int IntervalSeconds { get; set; }

        /// <summary>
        /// Frames per second in the output video.
        /// </summary>
        public int VideoFps { get; set; }

        // You can add more settings fields here, e.g. image format, resolution, etc.
    }

    public static class SettingsManager
    {
        private const string SettingsFileName = "settings.json";

        /// <summary>
        /// Loads settings from the given base folder. Returns null if file is missing or fails to parse.
        /// </summary>
        public static CaptureSettings? LoadSettings(string baseFolder)
        {
            if (string.IsNullOrEmpty(baseFolder))
                throw new ArgumentNullException(nameof(baseFolder));

            string filePath = Path.Combine(baseFolder, SettingsFileName);
            if (!File.Exists(filePath))
                return null;

            try
            {
                string json = File.ReadAllText(filePath);
                var opts = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                return JsonSerializer.Deserialize<CaptureSettings>(json, opts);
            }
            catch (Exception ex)
            {
                // Optionally log ex.Message
                return null;
            }
        }

        /// <summary>
        /// Saves the provided settings to the given base folder (writes JSON).
        /// </summary>
        public static void SaveSettings(string baseFolder, CaptureSettings settings)
        {
            if (string.IsNullOrEmpty(baseFolder))
                throw new ArgumentNullException(nameof(baseFolder));
            if (settings is null)
                throw new ArgumentNullException(nameof(settings));

            string filePath = Path.Combine(baseFolder, SettingsFileName);
            var opts = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            string json = JsonSerializer.Serialize(settings, opts);
            File.WriteAllText(filePath, json);
        }
    }
}
