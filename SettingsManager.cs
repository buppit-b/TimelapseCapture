using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace TimelapseCapture
{
    public class CaptureSettings
    {
        public int IntervalSeconds { get; set; } = 5;
        public int JpegQuality { get; set; } = 90;
        public System.Drawing.Rectangle? Region { get; set; } = null;
        public string? SaveFolder { get; set; } = null;
        public string? Format { get; set; } = "JPEG";
        public string? FfmpegPath { get; set; } = null;
        public int HotkeyKey { get; set; } = 0;
        public int HotkeyModifiers { get; set; } = 0;
    }

    public static class SettingsManager
    {
        private static string GetSettingsFile() =>
            Path.Combine(Application.StartupPath, "settings.json");

        public static CaptureSettings LoadSettings()
        {
            try
            {
                string file = GetSettingsFile();
                if (File.Exists(file))
                {
                    string json = File.ReadAllText(file);
                    var settings = JsonSerializer.Deserialize<CaptureSettings>(json);
                    if (settings != null) return settings;
                }
            }
            catch { }
            return new CaptureSettings();
        }

        public static void SaveSettings(CaptureSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetSettingsFile(), json);
            }
            catch { }
        }
    }
}
