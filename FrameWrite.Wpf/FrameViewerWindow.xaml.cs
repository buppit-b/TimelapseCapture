using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameWrite.Wpf
{
    /// <summary>
    /// The loupe: a floating, resizable frame inspector. Wheel zooms at the cursor, drag pans,
    /// double-click toggles fit ↔ 1:1, and the scrubber walks the session's frames while the
    /// zoom/pan stays put — so you can compare the same detail across frames. Non-modal.
    /// </summary>
    public partial class FrameViewerWindow : Window
    {
        private readonly string _sessionFolder;
        private string[] _files = Array.Empty<string>();
        private int _current;          // 1-based frame number
        private double _scale = 1;
        private double _tx, _ty;
        private bool _autoFit = true;  // re-fit on resize until the user zooms/pans by hand
        private bool _panning;
        private Point _panStart;
        private (double x, double y) _panStartT;
        private bool _scrubReady;      // ignore slider events during (re)initialisation

        // 1:1 means one FRAME pixel per DEVICE pixel — on a scaled monitor (e.g. 150%) that is a
        // layout scale of 1/1.5, not 1.0 (which would render each pixel soft at 150%).
        private double _pixelScale = 1;

        // Playback: step the scrubber at the encode fps so you can watch the timelapse before encoding.
        private readonly System.Windows.Threading.DispatcherTimer _playTimer = new();
        private readonly double _playbackFps;

        public FrameViewerWindow(string sessionFolder, double playbackFps = 30)
        {
            InitializeComponent();
            _sessionFolder = sessionFolder;
            _playbackFps = Math.Clamp(playbackFps, 0.1, 240);
            _playTimer.Interval = TimeSpan.FromSeconds(1.0 / _playbackFps);
            _playTimer.Tick += OnPlayTick;
            Loaded += (s, e) =>
            {
                _pixelScale = 1.0 / VisualTreeHelper.GetDpi(this).DpiScaleX;
                fpsText.Text = $"@ {_playbackFps:0.##} fps";
                Reload(goToLast: true);
            };
            // The initial fit must run AFTER the viewport is actually rendered/sized. A deferred fit at
            // Loaded priority can fire before that (leaving the frame at 1:1 top-left — the "top-left
            // quadrant" bug). ContentRendered is the reliable "everything is laid out and drawn" signal.
            ContentRendered += (s, e) =>
            {
                FrameWrite.Logger.Log("Loupe", $"ContentRendered: autoFit={_autoFit}, vp={viewport.ActualWidth:F0}x{viewport.ActualHeight:F0}, src={(img.Source as BitmapSource)?.PixelWidth}");
                Fit();   // unconditional on first render — guarantee an initial centred fit
            };
            Closed += (s, e) => _playTimer.Stop();   // don't leak the timer past the window
        }

        // ---- playback ----

        private void OnPlayToggle(object sender, RoutedEventArgs e)
        {
            if (_playTimer.IsEnabled) StopPlaying();
            else
            {
                if (_files.Length < 2) return;                 // nothing to play
                if (_current >= _files.Length) scrub.Value = 1; // restart from the top if parked at the end
                _playTimer.Start();
                playBtn.Content = "⏸ Pause";
            }
        }

        private void StopPlaying()
        {
            _playTimer.Stop();
            if (IsLoaded) playBtn.Content = "▶ Play";
        }

        private void OnPlayTick(object? sender, EventArgs e)
        {
            // Advance one frame; loop back to the start at the end so the preview repeats.
            scrub.Value = _current >= _files.Length ? 1 : _current + 1;
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            _pixelScale = 1.0 / newDpi.DpiScaleX;   // dragged to a differently-scaled monitor
            ApplyTransform();                        // zoom % readout re-baselines
        }

        // ---- frame loading ----

        private void Reload(bool goToLast)
        {
            int before = _files.Length;
            _files = SessionManager.GetFrameFiles(_sessionFolder);
            if (_files.Length == 0) { Close(); return; }

            _scrubReady = false;
            scrub.Maximum = _files.Length;
            // Follow the newest frame when asked, or when we were sitting on the previous newest.
            if (goToLast || (_current >= before && _files.Length > before)) _current = _files.Length;
            _current = Math.Clamp(_current, 1, _files.Length);
            scrub.Value = _current;
            _scrubReady = true;
            LoadFrame(_current, firstLoad: goToLast);
        }

        private void LoadFrame(int n, bool firstLoad = false)
        {
            if (n < 1 || n > _files.Length) return;
            _current = n;
            try
            {
                var file = _files[n - 1];
                // Load from bytes so the file on disk is never locked (capture may still be writing).
                var bytes = File.ReadAllBytes(file);
                var bi = new BitmapImage();
                using (var ms = new MemoryStream(bytes))
                {
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = ms;
                    bi.EndInit();
                }
                bi.Freeze();
                img.Source = bi;
                // 1 layout unit = 1 frame pixel, regardless of the image's DPI metadata.
                img.Width = bi.PixelWidth;
                img.Height = bi.PixelHeight;

                posText.Text = $"frame {n} / {_files.Length}";
                infoText.Text = $"{bi.PixelWidth}×{bi.PixelHeight} · {bytes.Length / 1024.0:F1} KB · " +
                                $"{File.GetLastWriteTime(file):yyyy-MM-dd HH:mm:ss}";
                // Defer the first fit to Loaded priority: at this point the viewport often isn't measured
                // yet (ActualWidth 0), so an immediate Fit() would no-op and leave the frame pinned
                // top-left. Running after layout guarantees it centres. (OnViewportSized also re-fits.)
                if (firstLoad) Dispatcher.BeginInvoke(new Action(Fit), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                infoText.Text = $"couldn't load frame {n}: {ex.Message}";
            }
        }

        private void OnScrub(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_scrubReady) LoadFrame((int)Math.Round(e.NewValue));
        }

        private void OnStep(object sender, RoutedEventArgs e)
        {
            StopPlaying();   // stepping is manual, frame-by-frame control — stop any playback
            if (sender is FrameworkElement el && int.TryParse(el.Tag as string, out int d))
                scrub.Value = Math.Clamp(scrub.Value + d, scrub.Minimum, scrub.Maximum);
        }

        private void OnRefresh(object sender, RoutedEventArgs e) => Reload(goToLast: false);

        // ---- zoom / pan ----

        private void OnWheelZoom(object sender, MouseWheelEventArgs e)
        {
            if (img.Source == null) return;
            double factor = e.Delta > 0 ? 1.2 : 1 / 1.2;
            double newScale = Math.Clamp(_scale * factor, 0.05, 32);
            factor = newScale / _scale;
            var pos = e.GetPosition(viewport);
            // Keep the frame point under the cursor stationary while the scale changes around it.
            _tx = pos.X - factor * (pos.X - _tx);
            _ty = pos.Y - factor * (pos.Y - _ty);
            _scale = newScale;
            _autoFit = false;
            ApplyTransform();
            e.Handled = true;
        }

        private void OnViewportDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double-click: 1:1 at the clicked spot, or back to fit if already close to 1:1.
                if (Math.Abs(_scale - _pixelScale) < 0.01) Fit();
                else ZoomToActualSize(e.GetPosition(viewport));
                return;
            }
            _panning = true;
            _panStart = e.GetPosition(viewport);
            _panStartT = (_tx, _ty);
            viewport.CaptureMouse();
            viewport.Cursor = Cursors.SizeAll;
        }

        private void OnViewportMove(object sender, MouseEventArgs e)
        {
            if (!_panning) return;
            var p = e.GetPosition(viewport);
            _tx = _panStartT.x + (p.X - _panStart.X);
            _ty = _panStartT.y + (p.Y - _panStart.Y);
            _autoFit = false;
            ApplyTransform();
        }

        private void OnViewportUp(object sender, MouseButtonEventArgs e)
        {
            _panning = false;
            viewport.ReleaseMouseCapture();
            viewport.Cursor = Cursors.Cross;
        }

        private void OnViewportSized(object sender, SizeChangedEventArgs e)
        {
            if (_autoFit) Fit();
        }

        private void OnFit(object sender, RoutedEventArgs e) => Fit();

        private void OnActualSize(object sender, RoutedEventArgs e)
            => ZoomToActualSize(new Point(viewport.ActualWidth / 2, viewport.ActualHeight / 2));

        private void Fit()
        {
            if (img.Source is not BitmapSource src || viewport.ActualWidth < 1 || viewport.ActualHeight < 1)
            {
                FrameWrite.Logger.Log("Loupe", $"Fit skipped: hasSrc={img.Source is BitmapSource}, vp={viewport.ActualWidth:F1}x{viewport.ActualHeight:F1}");
                return;
            }
            _scale = Math.Min(viewport.ActualWidth / src.PixelWidth, viewport.ActualHeight / src.PixelHeight);
            _tx = (viewport.ActualWidth - src.PixelWidth * _scale) / 2;
            _ty = (viewport.ActualHeight - src.PixelHeight * _scale) / 2;
            FrameWrite.Logger.Log("Loupe", $"Fit: vp={viewport.ActualWidth:F0}x{viewport.ActualHeight:F0}, frame={src.PixelWidth}x{src.PixelHeight}, scale={_scale:F3}, tx={_tx:F0}, ty={_ty:F0}");
            _autoFit = true;
            ApplyTransform();
        }

        private void ZoomToActualSize(Point focus)
        {
            if (img.Source == null) return;
            // Keep the frame point at 'focus' in place while snapping to true pixel-for-pixel.
            double factor = _pixelScale / _scale;
            _tx = focus.X - factor * (focus.X - _tx);
            _ty = focus.Y - factor * (focus.Y - _ty);
            _scale = _pixelScale;
            _autoFit = false;
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            scaleT.ScaleX = scaleT.ScaleY = _scale;
            transT.X = _tx;
            transT.Y = _ty;
            zoomText.Text = $"{_scale / _pixelScale * 100:F0}%";   // 100% = one frame pixel per screen pixel
            // Crisp pixels when magnifying (what a detail check needs); smooth when shrinking.
            RenderOptions.SetBitmapScalingMode(img,
                _scale >= _pixelScale ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        }
    }
}
