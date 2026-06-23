using System;

namespace TimelapseCapture
{
    /// <summary>
    /// Application constants and configuration values.
    /// </summary>
    public static class Constants
    {
        // UI Constants
        public const int UI_UPDATE_INTERVAL_MS = 500;
        public const int REGION_SELECTION_DELAY_MS = 200;
        public const int BRACKET_SIZE = 20;
        public const int BORDER_SIZE = 50;
        
        // Capture Constants
        public const int MAX_CONSECUTIVE_ERRORS = 3;
        public const int MIN_DISK_SPACE_MB = 50;
        public const int DISK_SPACE_CHECK_INTERVAL = 10; // Check every 10 frames
        public const int MIN_REGION_SIZE = 2;
        
        // Default Values
        public const int DEFAULT_INTERVAL_SECONDS = 5;
        public const int DEFAULT_JPEG_QUALITY = 90;
        public const int DEFAULT_FRAME_RATE = 25;
        public const string DEFAULT_IMAGE_FORMAT = "JPEG";
        public const string DEFAULT_ENCODING_PRESET = "medium";
        
        // File Extensions
        public static readonly string[] IMAGE_EXTENSIONS = { "*.jpg", "*.jpeg", "*.png", "*.bmp" };
        public static readonly string[] VIDEO_EXTENSIONS = { "*.mp4", "*.avi", "*.mkv", "*.webm" };
        
        // Messages
        public const string MSG_NO_REGION_SELECTED = "Please select a capture region first.";
        public const string MSG_INVALID_REGION = "Invalid capture region dimensions: {0}×{1}\n\nDimensions must be even numbers for video encoding.\nPlease select a new region.";
        public const string MSG_NO_SAVE_FOLDER = "Please select a save folder.";
        public const string MSG_NO_SESSION = "No session is loaded.\n\nPlease create a session first using:\n• 'New' button (custom name)\n• 'Load' button (resume existing)";
        public const string MSG_NO_FRAMES = "No frames to encode!\n\nPlease capture some frames before encoding.";
        public const string MSG_FFMPEG_NOT_FOUND = "FFmpeg is not configured!\n\nFFmpeg is required for video encoding but has not been set up.\n\nWould you like to:\n• YES: Browse for ffmpeg.exe on your system\n• NO: Cancel encoding\n\nTo download FFmpeg:\nVisit https://ffmpeg.org/download.html";
        
        // Status Messages
        public const string STATUS_CAPTURING = "🔴 CAPTURING - {0} ({1} frames)";
        public const string STATUS_SESSION_LOADED = "📂 Session loaded: {0} ({1} frames) - Ready to capture";
        public const string STATUS_NEW_SESSION = "🆕 New session ready: {0} - Press Start to begin";
        public const string STATUS_NO_SESSION = "⚪ No session - Select region and press Start to create one";
        public const string STATUS_ENCODING = "Encoding {0} frames...";
        public const string STATUS_ENCODING_COMPLETE = "✅ Encoding complete!";
        public const string STATUS_ENCODING_FAILED = "❌ Encoding failed";
    }
}

