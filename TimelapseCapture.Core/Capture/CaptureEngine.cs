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
        private OverlayConfig? _overlay;

        // Window tracking: when _trackedWindow is non-zero, each tick follows the window. Output is always
        // _lockedSize (frozen at Start) so every saved frame stays uniform (encodable). _resizeMode decides
        // how a resized window is handled: 0 = lock (crop the locked box at the window's top-left), 1 = fit
        // (capture the whole window, letterbox-scale into the frame), 2 = stretch (scale to fill, distort).
        public const int ResizeLock = 0, ResizeFit = 1, ResizeStretch = 2;
        private IntPtr _trackedWindow = IntPtr.Zero;
        private Size _lockedSize;
        private int _resizeMode;
        private Rectangle _lastTrackedRect;   // last followed rect (pos+size), to detect (and skip) motion/resize
        private bool _trackingInitialized;
        private bool _pauseOnTrackedMinimize; // true = hold while a tracked window is minimized; false = stop
        private int _consecutiveTrackSkips;   // bound transit-skipping so a perpetually-moving window still captures
        private const int MaxTrackSkips = 10;

        // Thrown when the screen can't be grabbed (secure desktop / lock screen / UAC / RDP disconnect). Treated
        // as a skip-this-tick (transient) rather than a hard failure, so a UAC prompt doesn't bake black frames.
        private sealed class ScreenUnavailableException : Exception
        {
            public ScreenUnavailableException(string message) : base(message) { }
        }

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
                          bool captureCursor = false, OverlayConfig? overlay = null, IntPtr trackedWindow = default,
                          bool pauseOnTrackedMinimize = false, int resizeMode = ResizeLock)
        {
            lock (_lock)
            {
                if (IsRunning) return;

                _sessionFolder = sessionFolder;
                _session = session;
                _region = region;
                _trackedWindow = trackedWindow;                         // zero = static region (default)
                _lockedSize = new Size(region.Width, region.Height);    // frozen, even (the VM trimmed it)
                _trackingInitialized = false;                           // first tracked tick always captures
                _consecutiveTrackSkips = 0;
                _pauseOnTrackedMinimize = pauseOnTrackedMinimize;
                _resizeMode = resizeMode;
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
                _overlay = overlay;

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
                _trackedWindow = IntPtr.Zero;
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

            // Drop overlapping ticks instead of queueing them: if the previous capture is still
            // running (slow disk / large frame), skip this tick rather than let callbacks pile up.
            if (!Monitor.TryEnter(_lock)) return;
            try
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
                        // Window tracking: resolve the follow rect. Closed/unreadable → throw (surfaces as a
                        // CaptureFailed → auto-stop). Minimized → stop, or hold if the user chose to wait. Moving
                        // → skip the transit frame (on-screen pixels lag the window rect during a drag).
                        bool skipFrame = false;
                        if (_trackedWindow != IntPtr.Zero)
                        {
                            bool ok = WindowEnumerator.TryGetLiveBounds(_trackedWindow, out var live, out bool minimized, out bool alive);
                            if (!alive)
                                throw new InvalidOperationException("The tracked window was closed.");
                            if (!ok)
                            {
                                // A rect read can fail transiently (window mid-transition / briefly hung). Skip a few
                                // ticks like a transit frame; only a PERSISTENT failure should end a long run.
                                if (_consecutiveTrackSkips < MaxTrackSkips) { skipFrame = true; _consecutiveTrackSkips++; }
                                else throw new InvalidOperationException("Couldn't read the tracked window's position.");
                            }
                            else if (minimized)
                            {
                                // Minimized, hidden (closed-to-tray), or cloaked (another virtual desktop): the window
                                // is showing no pixels, so capturing its rect would record whatever is BEHIND it.
                                // Default: stop (surface a failure). Opt-in: hold and resume when it's back.
                                if (!_pauseOnTrackedMinimize)
                                    throw new InvalidOperationException("The tracked window is minimized or hidden — restore it to keep capturing.");
                                skipFrame = true;
                            }
                            else
                            {
                                // Lock mode: follow the top-left at the locked size, clamped onto the desktop. Scale
                                // modes: capture the whole visible window, to be resampled to the locked size on save.
                                Rectangle resolved;
                                bool cantPlace = false;
                                if (_resizeMode == ResizeLock)
                                {
                                    var box = new Rectangle(live.X, live.Y, _lockedSize.Width, _lockedSize.Height);
                                    var fit = ScreenHelper.FitRegionOnScreen(box, out _);
                                    if (fit == null) { cantPlace = true; resolved = _region; } // box can't be placed → wait
                                    else resolved = fit.Value;
                                }
                                else
                                {
                                    var vis = Rectangle.Intersect(live, ScreenHelper.VirtualScreenBounds());
                                    if (vis.Width < 2 || vis.Height < 2) { cantPlace = true; resolved = _region; } // off-screen → wait
                                    else resolved = vis;
                                }

                                if (cantPlace)
                                {
                                    skipFrame = true;   // window not placeable on-screen — don't capture stale pixels
                                }
                                else if (_trackingInitialized && resolved != _lastTrackedRect)
                                {
                                    // Moving/resizing → skip the transit frame (pixels lag the rect), but bound the
                                    // skipping so a perpetually-moving window still yields frames instead of starving.
                                    if (_consecutiveTrackSkips < MaxTrackSkips) { skipFrame = true; _consecutiveTrackSkips++; }
                                    else _consecutiveTrackSkips = 0;
                                }
                                else
                                {
                                    _consecutiveTrackSkips = 0;
                                }
                                _lastTrackedRect = resolved;
                                _trackingInitialized = true;
                                _region = resolved;
                            }
                        }

                        // A blocked screen grab (secure desktop / lock / UAC) is transient — skip this tick and
                        // resume automatically, rather than baking a black frame or hard-failing a long run.
                        Bitmap? frame = null;
                        if (!skipFrame)
                        {
                            try { frame = CaptureFrameBitmap(); }
                            catch (ScreenUnavailableException ex) { Logger.Log("CaptureEngine", $"Skipped frame: {ex.Message}"); }
                        }

                        if (frame != null)
                        {
                            long next = (_session?.FramesCaptured ?? 0) + 1;
                            string file = Path.Combine(_framesFolder, $"{next:D5}.{_extension}");

                            using (frame)
                            {
                                if (frame.Width == 0 || frame.Height == 0)
                                    throw new InvalidOperationException("Captured bitmap is invalid");
                                if (_overlay?.Enabled == true) DrawOverlay(frame, _overlay);  // overlay sits on the final frame
                                SaveBitmap(frame, file);
                            }

                            // Increment + reload in one shot. IncrementFrameCount returns the updated session,
                            // or null if session.json vanished (folder moved/deleted mid-capture) — treat that as
                            // a failure so it surfaces instead of running blind with a frozen count. (Avoids a
                            // second full read+deserialize of session.json every frame in this hot path.)
                            var updated = SessionManager.IncrementFrameCount(_sessionFolder);
                            if (updated == null)
                                throw new InvalidOperationException("session.json is missing — the session folder may have been moved or deleted.");
                            _session = updated;
                            newCount = (int)updated.FramesCaptured;
                            _lastCaptureTicks = Environment.TickCount64;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("CaptureEngine", $"Capture failed: {ex.Message}");
                        error = ex.Message;
                    }
                }
            }
            finally
            {
                Monitor.Exit(_lock);
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

        // Burn a configurable text overlay (timestamp / custom label) onto a captured frame.
        // Overlay drawing lives in OverlayRenderer — shared with the Overlay dialog's live preview so
        // the preview is pixel-identical to what gets burned into frames.
        private static void DrawOverlay(Bitmap bmp, OverlayConfig cfg) => OverlayRenderer.Draw(bmp, cfg);

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

        // Produce the frame to save. Static region or lock-size tracking captures _region directly; scale modes
        // capture the live window then resample to the locked size (the cursor is drawn on the source so it
        // scales with the content). Output is always uniform (_region size, or _lockedSize for scale modes).
        private Bitmap CaptureFrameBitmap()
        {
            if (_trackedWindow == IntPtr.Zero || _resizeMode == ResizeLock)
            {
                var bmp = CaptureRegion(_region);
                if (_captureCursor) DrawCursor(bmp, _region);
                return bmp;
            }
            using var src = CaptureRegion(_region);
            if (_captureCursor) DrawCursor(src, _region);
            return ScaleToLocked(src);
        }

        // Resample the captured window into a locked-size frame: Fit = letterbox (preserve aspect, black bars),
        // Stretch = fill exactly (distorts). Keeps every saved frame at the locked WxH regardless of window size.
        private Bitmap ScaleToLocked(Bitmap src)
        {
            var dest = new Bitmap(_lockedSize.Width, _lockedSize.Height);
            using var g = Graphics.FromImage(dest);
            g.Clear(Color.Black);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(src, ComputeScaledDest(src.Size, _lockedSize, _resizeMode));
            return dest;
        }

        /// <summary>
        /// Destination rect for drawing a <paramref name="src"/>-sized image into a <paramref name="dest"/>-sized
        /// frame: Stretch fills exactly; Fit (and any non-stretch mode) scales to fit inside, centred (letterbox).
        /// Pure + side-effect-free so it can be unit-tested. Assumes positive sizes.
        /// </summary>
        internal static Rectangle ComputeScaledDest(Size src, Size dest, int resizeMode)
        {
            if (resizeMode == ResizeStretch)
                return new Rectangle(0, 0, dest.Width, dest.Height);

            double scale = Math.Min((double)dest.Width / src.Width, (double)dest.Height / src.Height);
            int w = Math.Max(1, (int)Math.Round(src.Width * scale));
            int h = Math.Max(1, (int)Math.Round(src.Height * scale));
            return new Rectangle((dest.Width - w) / 2, (dest.Height - h) / 2, w, h);
        }

        private static Bitmap CaptureRegion(Rectangle region)
        {
            IntPtr hdcScreen = GetDC(IntPtr.Zero);
            // A null screen DC means GDI handles are exhausted — a real problem worth surfacing (→ auto-stop).
            if (hdcScreen == IntPtr.Zero)
                throw new InvalidOperationException("Screen capture failed — no screen device context (GDI handles may be exhausted).");
            try
            {
                var bmp = new Bitmap(region.Width, region.Height);
                bool ok;
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr hdcBitmap = g.GetHdc();
                    try
                    {
                        ok = BitBlt(hdcBitmap, 0, 0, region.Width, region.Height, hdcScreen, region.X, region.Y, SRCCOPY);
                    }
                    finally
                    {
                        g.ReleaseHdc(hdcBitmap);
                    }
                }
                // BitBlt fails against the secure desktop / lock screen / a protected window. Don't return the
                // freshly-zeroed (all-black) bitmap as a valid frame — signal a transient skip instead.
                if (!ok)
                {
                    bmp.Dispose();
                    throw new ScreenUnavailableException("Screen capture unavailable (secure desktop, lock screen or a protected window is active).");
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
