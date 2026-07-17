using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameWrite; // Core: SessionManager

namespace FrameWrite.Wpf
{
    /// <summary>Loads captured frames of a session as small, frozen images (latest, or a given index).</summary>
    internal static class FramePreview
    {
        public static ImageSource? LoadLatest(string? sessionFolder, int decodePixelWidth)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionFolder)) return null;
                // Reuse the engine's frame list — it sorts NUMERICALLY, so the newest frame is still
                // the last one past 99,999 (an ordinal scan put "100000" before "99999" and would
                // freeze the preview on frame 99999 for the rest of a very long run).
                var files = SessionManager.GetFrameFiles(sessionFolder);
                return files.Length == 0 ? null : Decode(files[^1], decodePixelWidth);
            }
            catch { return null; }
        }

        public static ImageSource? LoadAt(string? sessionFolder, int frameNumber, int decodePixelWidth)
        {
            try
            {
                string? frames = FramesFolder(sessionFolder);
                if (frames == null) return null;
                foreach (var ext in new[] { "jpg", "png", "jpeg" })
                {
                    string p = Path.Combine(frames, $"{frameNumber:D5}.{ext}");
                    if (File.Exists(p)) return Decode(p, decodePixelWidth);
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>Absolute path to frame <paramref name="frameNumber"/> (any supported extension), or null.</summary>
        public static string? PathFor(string? sessionFolder, int frameNumber)
        {
            string? frames = FramesFolder(sessionFolder);
            if (frames == null) return null;
            foreach (var ext in new[] { "jpg", "png", "jpeg", "bmp" })
            {
                string p = Path.Combine(frames, $"{frameNumber:D5}.{ext}");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private static string? FramesFolder(string? sessionFolder)
        {
            if (string.IsNullOrEmpty(sessionFolder)) return null;
            string frames = SessionManager.GetFramesFolder(sessionFolder);
            return Directory.Exists(frames) ? frames : null;
        }

        private static ImageSource Decode(string path, int decodePixelWidth)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            if (decodePixelWidth > 0)                       // 0 = native resolution (zoomable views)
                bmp.DecodePixelWidth = decodePixelWidth;    // downscale → small + fast
            bmp.CacheOption = BitmapCacheOption.OnLoad;       // read fully, don't lock the file
            // IgnoreImageCache: WPF caches decoded bitmaps by URI, so without this a frame rewritten
            // in place (destructive crop / overlay bake reuse the same NNNNN.ext path) would show the
            // STALE cached image in the preview. Force a fresh read every time.
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }
}
