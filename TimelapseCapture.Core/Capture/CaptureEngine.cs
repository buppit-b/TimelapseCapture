using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
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

        // Cursor overlay (BitBlt does not capture the cursor).
        [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO pci);
        [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);
        [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon,
            int cx, int cy, int istep, IntPtr hbrFlickerFreeDraw, int diFlags);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
        private const int CURSOR_SHOWING = 0x0001;
        private const int DI_NORMAL = 0x0003;

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] private struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT ptScreenPos; }
        [StructLayout(LayoutKind.Sequential)] private struct ICONINFO { public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }

        private readonly object _lock = new object();
        private Timer? _timer;
        private Rectangle _region;
        private string _sessionFolder = "";
        private string _framesFolder = "";
        private SessionInfo? _session;
        private ImageFormat _imageFormat = ImageFormat.Jpeg;
        private string _extension = "jpg";
        private int _jpegQuality = 90;
        private ImageCodecInfo? _jpegEncoder;
        private bool _captureCursor;

        private ActivityMonitor? _activityMonitor;
        private bool _smartEnabled;
        private int _baseIntervalMs;   // working rate (used while active) — also the smart-mode poll rate
        private int _idleIntervalMs;   // slower rate (used while idle)
        private long _lastCaptureTicks; // Environment.TickCount64 of the last saved frame (smart pacing)
        private bool _skipIdleFrames;

        /// <summary>Raised after each successful frame, with the new total frame count. UI thread not guaranteed.</summary>
        public event Action<int>? FrameCaptured;

        /// <summary>Raised when a capture/save fails (the engine keeps running). UI thread not guaranteed.</summary>
        public event Action<string>? CaptureFailed;

        /// <summary>Raised (on change) with the smart-interval activity state, e.g. "Active" / "Idle — slowed". UI thread not guaranteed.</summary>
        public event Action<string>? SmartStatusChanged;
        private string _lastSmartStatus = "";

        public bool IsRunning { get; private set; }

        /// <summary>
        /// Begin capturing <paramref name="region"/> every <paramref name="intervalSeconds"/> seconds
        /// into the session at <paramref name="sessionFolder"/>.
        /// </summary>
        public void Start(string sessionFolder, SessionInfo session, Rectangle region,
                          double intervalSeconds, string format,
                          bool smartEnabled = false, double idleIntervalSeconds = 30.0,
                          int idleThresholdSeconds = 30, bool skipIdleFrames = false, int jpegQuality = 90,
                          bool captureCursor = false)
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
                _jpegQuality = Math.Clamp(jpegQuality, 1, 100);
                _captureCursor = captureCursor;

                _baseIntervalMs = Math.Max(100, (int)(intervalSeconds * 1000));
                _smartEnabled = smartEnabled;
                _idleIntervalMs = Math.Max(100, (int)(idleIntervalSeconds * 1000));
                _skipIdleFrames = skipIdleFrames;

                if (_smartEnabled)
                {
                    _activityMonitor = new ActivityMonitor { IdleThresholdSeconds = idleThresholdSeconds, IsEnabled = true };
                    _activityMonitor.Start();
                }

                _lastSmartStatus = "";
                _lastCaptureTicks = Environment.TickCount64 - Math.Max(_baseIntervalMs, _idleIntervalMs); // first tick captures
                IsRunning = true;
                int startMs = _baseIntervalMs; // poll at the working rate; idle only changes whether we capture
                _timer = new Timer(OnTick, null, startMs, startMs);
                Logger.Log("CaptureEngine",
                    $"Started: region={region}, interval={intervalSeconds}s, format={_extension}, smart={_smartEnabled}");
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
                if (_activityMonitor != null)
                {
                    _activityMonitor.IsEnabled = false;
                    _activityMonitor.Stop();
                    _activityMonitor.Dispose();
                    _activityMonitor = null;
                }
                Logger.Log("CaptureEngine", "Stopped");
            }
        }

        private void OnTick(object? state)
        {
            int? newCount = null;
            string? error = null;
            string? smartStatus = null;
            bool capture = true;

            lock (_lock)
            {
                if (!IsRunning) return;

                // Smart interval: the timer always polls at the working rate, so renewed activity is
                // detected within one working interval (not stuck waiting out a long idle interval).
                // Whether we actually capture this tick is decided here.
                if (_smartEnabled && _activityMonitor != null)
                {
                    bool isActive = _activityMonitor.IsCurrentlyActive();
                    string status = isActive ? "Active" : (_skipIdleFrames ? "Idle — skipping frames" : "Idle — slowed");
                    if (status != _lastSmartStatus) { _lastSmartStatus = status; smartStatus = status; }

                    if (isActive)
                        capture = true;                                  // working rate == poll rate → every tick
                    else if (_skipIdleFrames)
                        capture = false;                                 // skip frames while idle
                    else                                                 // slowed: capture only once the idle rate elapses
                        capture = (Environment.TickCount64 - _lastCaptureTicks) >= Math.Max(_baseIntervalMs, _idleIntervalMs);
                }

                if (capture)
                {
                    try
                    {
                        long next = (_session?.FramesCaptured ?? 0) + 1;
                        string file = Path.Combine(_framesFolder, $"{next:D5}.{_extension}");

                        using (var bmp = CaptureRegion(_region))
                        {
                            if (bmp.Width == 0 || bmp.Height == 0)
                                throw new InvalidOperationException("Captured bitmap is invalid");
                            if (_captureCursor) DrawCursor(bmp, _region);
                            SaveBitmap(bmp, file);
                        }

                        SessionManager.IncrementFrameCount(_sessionFolder);
                        _session = SessionManager.LoadSession(_sessionFolder) ?? _session;
                        newCount = (int)(_session?.FramesCaptured ?? next);
                        _lastCaptureTicks = Environment.TickCount64;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("CaptureEngine", $"Capture failed: {ex.Message}");
                        error = ex.Message;
                    }
                }
            }

            // Raise events outside the lock so subscribers can't deadlock against the capture lock.
            if (smartStatus != null) SmartStatusChanged?.Invoke(smartStatus);
            if (newCount.HasValue) FrameCaptured?.Invoke(newCount.Value);
            if (error != null) CaptureFailed?.Invoke(error);
        }

        private void SaveBitmap(Bitmap bmp, string file)
        {
            // Apply the JPEG quality setting (plain Bitmap.Save(file, ImageFormat.Jpeg) ignores it).
            if (_imageFormat == ImageFormat.Jpeg)
            {
                _jpegEncoder ??= ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
                if (_jpegEncoder != null)
                {
                    using var ep = new EncoderParameters(1);
                    ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)_jpegQuality);
                    bmp.Save(file, _jpegEncoder, ep);
                    return;
                }
            }
            bmp.Save(file, _imageFormat);
        }

        // Draw the live mouse cursor onto a captured frame at its position within the region.
        private static void DrawCursor(Bitmap bmp, Rectangle region)
        {
            try
            {
                var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
                if (!GetCursorInfo(ref ci) || ci.flags != CURSOR_SHOWING || ci.hCursor == IntPtr.Zero) return;

                // Only draw if the pointer is inside the captured region.
                if (ci.ptScreenPos.X < region.X || ci.ptScreenPos.Y < region.Y ||
                    ci.ptScreenPos.X > region.Right || ci.ptScreenPos.Y > region.Bottom) return;

                int hotX = 0, hotY = 0;
                if (GetIconInfo(ci.hCursor, out var ii))
                {
                    hotX = ii.xHotspot; hotY = ii.yHotspot;
                    if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);   // GetIconInfo allocates these
                    if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
                }

                int x = ci.ptScreenPos.X - region.X - hotX;
                int y = ci.ptScreenPos.Y - region.Y - hotY;

                using var g = Graphics.FromImage(bmp);
                IntPtr hdc = g.GetHdc();
                try { DrawIconEx(hdc, x, y, ci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL); }
                finally { g.ReleaseHdc(hdc); }
            }
            catch { /* cursor overlay is best-effort; never break the capture loop */ }
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
