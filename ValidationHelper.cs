using System;
using System.Drawing;
using System.IO;
using System.Text;

namespace TimelapseCapture
{
    /// <summary>
    /// Centralized validation logic for the application.
    /// </summary>
    public static class ValidationHelper
    {
        /// <summary>
        /// Validates that a region has even dimensions (required for video encoding).
        /// </summary>
        public static bool IsValidRegion(Rectangle region)
        {
            return region.Width > 0 && 
                   region.Height > 0 && 
                   (region.Width & 1) == 0 && 
                   (region.Height & 1) == 0;
        }

        /// <summary>
        /// Validates that a region is not empty and has valid dimensions.
        /// ✅ FIX Issue #3: Method deprecated - nullable checks preferred.
        /// Kept for backward compatibility but consider removing in future.
        /// </summary>
        [Obsolete("Use nullable Rectangle? checks instead: region.HasValue")]
        public static bool IsRegionSelected(Rectangle region)
        {
            return region.Width > 0 && region.Height > 0;
        }

        /// <summary>
        /// Validates that a save folder is configured.
        /// </summary>
        public static bool IsSaveFolderConfigured(string? saveFolder)
        {
            return !string.IsNullOrEmpty(saveFolder) && Directory.Exists(saveFolder);
        }

        /// <summary>
        /// Validates that FFmpeg is configured and accessible.
        /// </summary>
        public static bool IsFfmpegConfigured(string? ffmpegPath)
        {
            return !string.IsNullOrEmpty(ffmpegPath) && File.Exists(ffmpegPath);
        }

        /// <summary>
        /// Validates that frames exist for encoding.
        /// </summary>
        public static bool HasFramesToEncode(string[] frameFiles)
        {
            return frameFiles.Length > 0;
        }

        /// <summary>
        /// Validates that an active session exists.
        /// </summary>
        public static bool HasActiveSession(SessionInfo? session)
        {
            return session != null;
        }

        /// <summary>
        /// Checks if there's sufficient disk space for capture operations.
        /// </summary>
        public static bool CheckDiskSpace(string path, long requiredBytes = Constants.MIN_DISK_SPACE_MB * 1_000_000)
        {
            try
            {
                var rootPath = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(rootPath)) return true;

                var drive = new DriveInfo(rootPath);
                return drive.AvailableFreeSpace > requiredBytes;
            }
            catch (Exception)
            {
                return true; // If we can't check, assume OK rather than block
            }
        }

        /// <summary>
        /// Builds a detailed mismatch message for session settings validation.
        /// </summary>
        public static string BuildSettingsMismatchMessage(SessionInfo session, Rectangle currentRegion, string currentFormat, int currentQuality)
        {
            var mismatches = new StringBuilder();
            mismatches.AppendLine("Your current settings don't match the loaded session:\n");

            if (session.CaptureRegion != currentRegion)
            {
                mismatches.AppendLine("• Region:");
                mismatches.AppendLine($"  Session: {session.CaptureRegion?.Width ?? 0}×{session.CaptureRegion?.Height ?? 0}");
                mismatches.AppendLine($"  Current: {currentRegion.Width}×{currentRegion.Height}\n");
            }

            if (session.ImageFormat != currentFormat)
            {
                mismatches.AppendLine("• Format:");
                mismatches.AppendLine($"  Session: {session.ImageFormat}");
                mismatches.AppendLine($"  Current: {currentFormat}\n");
            }

            if (session.JpegQuality != currentQuality && currentFormat == Constants.DEFAULT_IMAGE_FORMAT)
            {
                mismatches.AppendLine("• JPEG Quality:");
                mismatches.AppendLine($"  Session: {session.JpegQuality}");
                mismatches.AppendLine($"  Current: {currentQuality}\n");
            }

            mismatches.AppendLine("What would you like to do?\n\n");
            mismatches.AppendLine("• YES: Start a new session with current settings\n");
            mismatches.AppendLine("• NO: Cancel and adjust settings to match session");

            return mismatches.ToString();
        }

        /// <summary>
        /// Sanitizes a session name for filesystem compatibility.
        /// </summary>
        public static string SanitizeSessionName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return $"Session_{DateTime.Now:yyyyMMdd_HHmmss}";

            // Remove invalid filename characters
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = name;
            foreach (var c in invalid)
                sanitized = sanitized.Replace(c, '_');

            // Also remove some problematic characters that are technically valid
            sanitized = sanitized.Replace('.', '_').Replace(' ', '_');

            // Limit length to prevent path too long errors
            if (sanitized.Length > 50)
                sanitized = sanitized.Substring(0, 50);

            return sanitized.Trim('_'); // Remove leading/trailing underscores
        }
    }
}
