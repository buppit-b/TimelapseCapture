using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;

namespace TimelapseCapture
{
    public class SessionInfo
    {
        public string? Name { get; set; }
        public int IntervalSeconds { get; set; }
        public int VideoFps { get; set; } = 30;
        public DateTime StartTime { get; set; }
        public bool Active { get; set; } = true;
        public long FramesCaptured { get; set; } = 0;

        // Critical: Track capture settings to prevent mismatches
        public Rectangle CaptureRegion { get; set; }
        public string? ImageFormat { get; set; }
        public int JpegQuality { get; set; }
    }

    public static class SessionManager
    {
        private const string SessionFileName = "session.json";

        public static List<string> GetAllSessions(string capturesRoot)
        {
            if (string.IsNullOrEmpty(capturesRoot) || !Directory.Exists(capturesRoot))
                return new List<string>();

            var sessions = new List<string>();
            foreach (var dir in Directory.GetDirectories(capturesRoot))
            {
                var sessionFile = Path.Combine(dir, SessionFileName);
                if (File.Exists(sessionFile))
                {
                    sessions.Add(dir);
                }
            }
            return sessions.OrderByDescending(s => Directory.GetCreationTime(s)).ToList();
        }

        public static string? FindActiveSession(string capturesRoot)
        {
            if (string.IsNullOrEmpty(capturesRoot) || !Directory.Exists(capturesRoot))
                return null;

            foreach (var dir in Directory.GetDirectories(capturesRoot))
            {
                var sessionFile = Path.Combine(dir, SessionFileName);
                if (File.Exists(sessionFile))
                {
                    try
                    {
                        var json = File.ReadAllText(sessionFile);
                        var s = JsonSerializer.Deserialize<SessionInfo>(json);
                        if (s != null && s.Active)
                            return dir;
                    }
                    catch { }
                }
            }
            return null;
        }

        public static SessionInfo? LoadSession(string sessionFolder)
        {
            var sessionFile = Path.Combine(sessionFolder, SessionFileName);
            if (!File.Exists(sessionFile))
                return null;

            try
            {
                var json = File.ReadAllText(sessionFile);
                return JsonSerializer.Deserialize<SessionInfo>(json);
            }
            catch
            {
                return null;
            }
        }

        public static void SaveSession(string sessionFolder, SessionInfo info)
        {
            var sessionFile = Path.Combine(sessionFolder, SessionFileName);
            var opts = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(info, opts);
            File.WriteAllText(sessionFile, json);
        }

        public static string CreateNewSession(string capturesRoot, int intervalSeconds, Rectangle region, string format, int jpegQuality)
        {
            Directory.CreateDirectory(capturesRoot);
            var name = $"session_{DateTime.Now:yyyyMMdd_HHmmss}";
            var folder = Path.Combine(capturesRoot, name);
            Directory.CreateDirectory(folder);

            var info = new SessionInfo
            {
                Name = name,
                IntervalSeconds = intervalSeconds,
                VideoFps = 30,
                StartTime = DateTime.UtcNow,
                Active = true,
                FramesCaptured = 0,
                CaptureRegion = region,
                ImageFormat = format,
                JpegQuality = jpegQuality
            };

            SaveSession(folder, info);
            return folder;
        }

        public static void MarkSessionInactive(string sessionFolder)
        {
            var info = LoadSession(sessionFolder);
            if (info == null)
                return;

            info.Active = false;
            SaveSession(sessionFolder, info);
        }

        public static void IncrementFrameCount(string sessionFolder)
        {
            var info = LoadSession(sessionFolder);
            if (info == null)
                return;

            info.FramesCaptured++;
            SaveSession(sessionFolder, info);
        }

        public static bool ValidateSessionSettings(SessionInfo session, Rectangle region, string format, int jpegQuality)
        {
            // Check if region changed
            if (session.CaptureRegion != region)
                return false;

            // Check if format changed
            if (session.ImageFormat != format)
                return false;

            // Check if quality changed (only matters for JPEG)
            if (format == "JPEG" && session.JpegQuality != jpegQuality)
                return false;

            return true;
        }

        public static void MarkAllSessionsInactive(string capturesRoot)
        {
            var sessions = GetAllSessions(capturesRoot);
            foreach (var sessionFolder in sessions)
            {
                MarkSessionInactive(sessionFolder);
            }
        }
    }
}