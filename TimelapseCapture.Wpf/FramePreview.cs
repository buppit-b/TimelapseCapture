using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TimelapseCapture; // Core: SessionManager

namespace TimelapseCapture.Wpf
{
    /// <summary>Loads captured frames of a session as small, frozen images (latest, or a given index).</summary>
    internal static class FramePreview
    {
        public static ImageSource? LoadLatest(string? sessionFolder, int decodePixelWidth)
        {
            try
            {
                string? frames = FramesFolder(sessionFolder);
                if (frames == null) return null;

                string? last = null;
                foreach (var f in Directory.GetFiles(frames))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if ((ext == ".jpg" || ext == ".jpeg" || ext == ".png") &&
                        (last == null || string.CompareOrdinal(f, last) > 0))
                        last = f;
                }
                return last == null ? null : Decode(last, decodePixelWidth);
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
            bmp.DecodePixelWidth = decodePixelWidth;        // downscale → small + fast
            bmp.CacheOption = BitmapCacheOption.OnLoad;       // read fully, don't lock the file
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }
}
