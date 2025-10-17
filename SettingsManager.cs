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
    }
}