using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace TimelapseCapture
{
    /// <summary>
    /// UI-framework-agnostic timelapse capture engine: grabs a screen region on a timer and writes
    /// numbered frames into a session's frames folder. Reused by any front-end (WinForms, WPF) — it
    /// raises events and never touches UI directly. Uses BitBlt for multi-monitor-safe capture
    /// (Graphics.CopyFromScreen mishandles virtual-screen coordinates and mixed DPI).
    /// </summary>
    public sealed class CaptureEngine : IDisposable
    {
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest,
            int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int rop);
        private const int SRCCOPY = 0x00CC0020;

        private readonly object _lock = new object();
        private Timer? _timer;
        private Rectangle _region;
        private string _sessionFolder = "";
        private string _framesFolder = "";
        private SessionInfo? _session;
        private ImageFormat _imageFormat = ImageFormat.Jpeg;
        private string _extension = "jpg";

        /// <summary>Raised after each successful frame, with the new total frame count. UI thread not guaranteed.</summary>
        public event Action<int>? FrameCaptured;

        /// <summary>Raised when a capture/save fails (the engine keeps running). UI thread not guaranteed.</summary>
        public event Action<string>? CaptureFailed;

        public bool IsRunning { get; private set; }

        /// <summary>
        /// Begin capturing <paramref name="region"/> every <paramref name="intervalSeconds"/> seconds
        /// into the session at <paramref name="sessionFolder"/>.
        /// </summary>
        public void Start(string sessionFolder, SessionInfo session, Rectangle region,
                          double intervalSeconds, string format)
        {
            lock (_lock)
            {
                if (IsRunning) return;

                _sessionFolder = sessionFolder;
                _session = session;
                _region = region;
                _framesFolder = SessionManager.GetFramesFolder(sessionFolder);
                Directory.CreateDirectory(_framesFolder);

                if (string.Equals(format, "PNG", StringComparison.OrdinalIgnoreCase))
                {
                    _imageFormat = ImageFormat.Png;
                    _extension = "png";
                }
                else
                {
                    _imageFormat = ImageFormat.Jpeg;
                    _extension = "jpg";
                }

                int intervalMs = Math.Max(100, (int)(intervalSeconds * 1000));
                IsRunning = true;
                _timer = new Timer(OnTick, null, intervalMs, intervalMs);
                Logger.Log("CaptureEngine", $"Started: region={region}, interval={intervalSeconds}s, format={_extension}");
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!IsRunning) return;
                IsRunning = false;
                _timer?.Dispose();
                _timer = null;
                Logger.Log("CaptureEngine", "Stopped");
            }
        }

        private void OnTick(object? state)
        {
            int? newCount = null;
            string? error = null;

            lock (_lock)
            {
                if (!IsRunning) return;
                try
                {
                    long next = (_session?.FramesCaptured ?? 0) + 1;
                    string file = Path.Combine(_framesFolder, $"{next:D5}.{_extension}");

                    using (var bmp = CaptureRegion(_region))
                    {
                        if (bmp.Width == 0 || bmp.Height == 0)
                            throw new InvalidOperationException("Captured bitmap is invalid");
                        bmp.Save(file, _imageFormat);
                    }

                    SessionManager.IncrementFrameCount(_sessionFolder);
                    _session = SessionManager.LoadSession(_sessionFolder) ?? _session;
                    newCount = (int)(_session?.FramesCaptured ?? next);
                }
                catch (Exception ex)
                {
                    Logger.Log("CaptureEngine", $"Capture failed: {ex.Message}");
                    error = ex.Message;
                }
            }

            // Raise events outside the lock so subscribers (which may call Stop on another thread)
            // can't deadlock against the capture lock.
            if (newCount.HasValue) FrameCaptured?.Invoke(newCount.Value);
            if (error != null) CaptureFailed?.Invoke(error);
        }

        private static Bitmap CaptureRegion(Rectangle region)
        {
            IntPtr hdcScreen = GetDC(IntPtr.Zero);
            try
            {
                var bmp = new Bitmap(region.Width, region.Height);
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr hdcBitmap = g.GetHdc();
                    try
                    {
                        BitBlt(hdcBitmap, 0, 0, region.Width, region.Height, hdcScreen, region.X, region.Y, SRCCOPY);
                    }
                    finally
                    {
                        g.ReleaseHdc(hdcBitmap);
                    }
                }
                return bmp;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdcScreen);
            }
        }

        public void Dispose() => Stop();
    }
}
