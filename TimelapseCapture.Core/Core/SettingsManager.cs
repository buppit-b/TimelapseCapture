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
        // Exact, sub-second-capable capture interval. 0 = fall back to IntervalSeconds (older settings files).
        public decimal IntervalSecondsExact { get; set; } = 0m;
        public string? Format { get; set; } = "JPEG";
        public int JpegQuality { get; set; } = 90;
        public string EncodePreset { get; set; } = "medium"; // x264 preset: ultrafast..veryslow (speed vs file size)
        public Rectangle? Region { get; set; }
        public string? FfmpegPath { get; set; }
        public int AspectRatioIndex { get; set; } = 0; // Default to "Free" aspect ratio
        
        // Smart interval settings
        public bool SmartIntervalEnabled { get; set; }
        public decimal ActiveIntervalSeconds { get; set; } = 2.0m;   // legacy (WinForms); WPF uses IdleIntervalSeconds
        public decimal IdleIntervalSeconds { get; set; } = 30m;      // slower capture rate used while idle
        public int IdleThresholdSeconds { get; set; } = 30;
        public bool SkipIdleFrames { get; set; }
        
        // UI settings
        public bool GuidedModeEnabled { get; set; } = true; // Progressive disclosure for new users
        public string Theme { get; set; } = "Synth";        // colour palette / theme name (default: Synth — greens/accent pop)
        public bool AlwaysOnTop { get; set; }               // keep the main window above others
        public bool HideFromCapture { get; set; }           // exclude this window from screen capture
        public bool HideWindowDuringRegionSelect { get; set; } = true; // hide the app while picking a region so it doesn't block the target
        public bool MinimizeToTray { get; set; } = true;    // minimizing hides to the system tray (icon shows recording status)
        public bool PauseOnTrackedMinimize { get; set; }     // window tracking: minimized → wait (true) vs stop (false)
        public bool KeepTrackedWindowOnTop { get; set; }     // window tracking: force the tracked window topmost while capturing
        public bool StopAtTarget { get; set; }               // auto-stop capture when the frame count reaches the target
        public int TrackResizeMode { get; set; }             // tracked-window resize: 0 lock size, 1 scale-to-fit, 2 stretch
        public bool AutoStopOnLowDisk { get; set; } = true;  // unattended safety: stop before the drive fills
        public int LowDiskStopMB { get; set; } = 500;        // free-space threshold (MB) for the low-disk auto-stop
        public bool MaxDurationEnabled { get; set; }         // opt-in: stop after a maximum capture duration
        public int MaxDurationMinutes { get; set; } = 480;   // the cap (minutes of accumulated capture time)
        public bool StopAtStorageEnabled { get; set; }       // opt-in: stop once the session's frames reach a size
        public int StopAtStorageMB { get; set; } = 2000;     // the cap (MB of captured frames in this session)
        public int EncodeEveryNth { get; set; } = 1;         // encode speed-up: use every Nth frame (1 = all)
        public bool NotifyOnFinish { get; set; } = true;     // sound + taskbar flash when a capture/encode finishes
        public bool SimpleMode { get; set; }                 // simplified UI: speed slider + hides advanced controls
        public bool FirstRunCompleted { get; set; }          // the setup wizard has been shown once
        public bool IntervalShownAsFps { get; set; }         // show the capture interval as fps instead of seconds
        public bool CaptureCursor { get; set; }             // draw the mouse cursor into each frame
        public bool OverlayTimestamp { get; set; }          // master enable for the on-frame text overlay
        public string OverlayText { get; set; } = "{datetime}";
        public int OverlayPosition { get; set; } = 3;        // 0=TL 1=TR 2=BL 3=BR
        public int OverlayFontSize { get; set; } = 0;        // pixels; 0 = auto
        public string OverlayFontFamily { get; set; } = "Consolas";
        public double OverlayCustomX { get; set; } = -1;     // free placement (0..1); <0 = use corner Position
        public double OverlayCustomY { get; set; } = -1;
        public bool OpenFolderAfterEncode { get; set; }     // auto-open the output folder when encoding finishes
        public string OutputNameTemplate { get; set; } = "timelapse_{date}_{time}"; // tokens: {session} {date} {time} {datetime}
        public bool HotkeysEnabled { get; set; }            // global start/stop hotkey (off by default)
        public int HotkeyModifiers { get; set; } = 0x0006;  // Win32 fsModifiers: Ctrl(0x2) + Shift(0x4)
        public int HotkeyVk { get; set; } = 0x78;           // Win32 virtual-key: F9
    }




    public static class SettingsManager
    {
        private static readonly string SettingsFilePath = System.IO.Path.Combine(AppPaths.DataDir, "settings.json");

        public static CaptureSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                    return new CaptureSettings();

                var json = File.ReadAllText(SettingsFilePath);
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

                // Atomic write: write to a temp file then swap, so a crash or power loss
                // mid-write can never leave settings.json truncated or empty (which would
                // silently reset all settings to defaults on next launch).
                var tmp = SettingsFilePath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(SettingsFilePath))
                    File.Replace(tmp, SettingsFilePath, null);
                else
                    File.Move(tmp, SettingsFilePath);
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>Write settings to an arbitrary path (for export). Throws on failure.</summary>
        public static void ExportTo(CaptureSettings settings, string path)
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(settings, opts));
        }

        /// <summary>Read settings from an arbitrary path (for import). Null if the file isn't valid settings.</summary>
        public static CaptureSettings? LoadFrom(string path)
        {
            try
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<CaptureSettings>(File.ReadAllText(path), opts);
            }
            catch { return null; }
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
            string folder = System.IO.Path.Combine(capturesRoot, safeName);

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
            Directory.CreateDirectory(System.IO.Path.Combine(folder, "frames"));
            Directory.CreateDirectory(System.IO.Path.Combine(folder, "output"));
            Directory.CreateDirectory(System.IO.Path.Combine(folder, ".temp"));

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

            SessionManager.SaveSession(folder, info);
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
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
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