using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TimelapseCapture
{
    public class SessionInfo
    {
        public string Name { get; set; } = "";
        public Rectangle CaptureRegion { get; set; } = Rectangle.Empty;
        public string? ImageFormat { get; set; } = "JPEG";
        public int JpegQuality { get; set; } = 90;
        public int IntervalSeconds { get; set; } = 5;
        public int VideoFps { get; set; } = 30;
        public int FramesCaptured { get; set; } = 0;
        public bool Active { get; set; } = true;
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
    }

    public static class SessionManager
    {
        private const string SessionFileName = "session.json";

        /// <summary>
        /// Creates a new session folder with metadata
        /// </summary>
        public static string CreateNewSession(string capturesRoot, int intervalSeconds, Rectangle captureRegion, string format, int jpegQuality)
        {
            Directory.CreateDirectory(capturesRoot);

            var sessionName = $"session_{DateTime.Now:yyyyMMdd_HHmmss}";
            var sessionFolder = Path.Combine(capturesRoot, sessionName);
            Directory.CreateDirectory(sessionFolder);

            var session = new SessionInfo
            {
                Name = sessionName,
                CaptureRegion = captureRegion,
                ImageFormat = format,
                JpegQuality = jpegQuality,
                IntervalSeconds = intervalSeconds,
                VideoFps = 30,
                FramesCaptured = 0,
                Active = true,
                StartTime = DateTime.UtcNow
            };

            SaveSession(sessionFolder, session);
            return sessionFolder;
        }

        /// <summary>
        /// Finds the currently active session folder
        /// </summary>
        public static string? FindActiveSession(string capturesRoot)
        {
            if (!Directory.Exists(capturesRoot))
                return null;

            var sessionFolders = Directory.GetDirectories(capturesRoot, "session_*");
            
            foreach (var folder in sessionFolders)
            {
                var session = LoadSession(folder);
                if (session != null && session.Active)
                    return folder;
            }

            return null;
        }

        /// <summary>
        /// Loads session metadata from a session folder
        /// </summary>
        public static SessionInfo? LoadSession(string sessionFolder)
        {
            var metadataPath = Path.Combine(sessionFolder, SessionFileName);
            
            if (!File.Exists(metadataPath))
                return null;

            try
            {
                var json = File.ReadAllText(metadataPath);
                return JsonSerializer.Deserialize<SessionInfo>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading session: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Saves session metadata to folder
        /// </summary>
        private static void SaveSession(string sessionFolder, SessionInfo session)
        {
            var metadataPath = Path.Combine(sessionFolder, SessionFileName);
            
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(session, options);
                File.WriteAllText(metadataPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving session: {ex.Message}");
            }
        }

        /// <summary>
        /// Marks a session as inactive
        /// </summary>
        public static void MarkSessionInactive(string sessionFolder)
        {
            var session = LoadSession(sessionFolder);
            if (session != null)
            {
                session.Active = false;
                SaveSession(sessionFolder, session);
            }
        }

        /// <summary>
        /// Increments the frame count for a session
        /// </summary>
        public static void IncrementFrameCount(string sessionFolder)
        {
            var session = LoadSession(sessionFolder);
            if (session != null)
            {
                session.FramesCaptured++;
                SaveSession(sessionFolder, session);
            }
        }

        /// <summary>
        /// Validates that current settings match session settings
        /// </summary>
        public static bool ValidateSessionSettings(SessionInfo session, Rectangle captureRegion, string format, int jpegQuality)
        {
            return session.CaptureRegion == captureRegion
                && session.ImageFormat == format
                && (format != "JPEG" || session.JpegQuality == jpegQuality);
        }

        /// <summary>
        /// Gets all session folders in captures root
        /// </summary>
        public static List<string> GetAllSessions(string capturesRoot)
        {
            if (!Directory.Exists(capturesRoot))
                return new List<string>();

            var sessionFolders = Directory.GetDirectories(capturesRoot, "session_*");
            return sessionFolders.ToList();
        }
    }
}
