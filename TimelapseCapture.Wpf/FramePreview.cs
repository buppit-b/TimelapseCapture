using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TimelapseCapture; // Core: SessionManager

namespace TimelapseCapture.Wpf
{
    /// <summary>Loads the most recent captured frame of a session as a small, frozen image.</summary>
    internal static class FramePreview
    {
        public static ImageSource? LoadLatest(string? sessionFolder, int decodePixelWidth)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionFolder)) return null;
                string frames = SessionManager.GetFramesFolder(sessionFolder);
                if (!Directory.Exists(frames)) return null;

                string? last = null;
                foreach (var f in Directory.GetFiles(frames))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if ((ext == ".jpg" || ext == ".jpeg" || ext == ".png") &&
                        (last == null || string.CompareOrdinal(f, last) > 0))
                        last = f;
                }
                if (last == null) return null;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(last);
                bmp.DecodePixelWidth = decodePixelWidth;        // downscale → small + fast
                bmp.CacheOption = BitmapCacheOption.OnLoad;       // read fully, don't lock the file
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; } // a frame mid-write or a corrupt file just yields no preview this tick
        }
    }
}
