using System;
using System.IO;
using System.Text.Json;
using System.Drawing;

namespace TimelapseCapture
{
    public class CaptureSettings
    {
        public string SaveFolder { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "TimelapseCapture");

        public Rectangle? Region { get; set; }
        public string? Format { get; set; } = "JPEG";
        public int JpegQuality { get; set; } = 90;
        public int IntervalSeconds { get; set; } = 5;
        public string? FfmpegPath { get; set; }
        public int VideoFps { get; set; } = 30;
    }

    public static class SettingsManager
    {
        private static readonly string SettingsFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TimelapseCapture", "settings.json");

        public static CaptureSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<CaptureSettings>(json);
                    return settings ?? new CaptureSettings();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading settings: {ex.Message}");
            }

            return new CaptureSettings();
        }

        public static void Save(CaptureSettings settings)
        {
            try
            {
                string? dir = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings: {ex.Message}");
            }
        }
    }
}
