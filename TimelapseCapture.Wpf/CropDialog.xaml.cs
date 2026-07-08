using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// Pick the frame area encodes keep: drag a rectangle on the latest frame (numeric X/Y/W/H for
    /// precision). Non-destructive by default — the rect is stored per session (SessionInfo.EncodeCrop)
    /// and applied by ffmpeg at encode time. A consented power-user button can instead crop the frames
    /// ON DISK (DestructiveRequested; the VM performs it). All coordinates are FRAME pixels.
    /// </summary>
    public partial class CropDialog : Window
    {
        private readonly int _frameW, _frameH;

        /// <summary>The chosen crop in frame pixels, or null for "no crop". Valid when DialogResult is true.</summary>
        public System.Drawing.Rectangle? CropRect { get; private set; }

        /// <summary>True when the user chose (and confirmed) the destructive crop-frames-on-disk action.</summary>
        public bool DestructiveRequested { get; private set; }

        private bool _dragging;
        private Point _dragStart;                 // canvas coords
        private System.Drawing.Rectangle? _rect;  // current crop, frame px
        private readonly Rectangle _rubber = new()
        {
            StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
        };

        public CropDialog(string sessionFolder, System.Drawing.Rectangle? existingCrop)
        {
            InitializeComponent();
            _rubber.Stroke = TryFindResource("AccentBrush") as Brush ?? Brushes.LimeGreen;
            rectCanvas.Children.Add(_rubber);
            _rubber.Visibility = Visibility.Collapsed;

            // Load the latest frame at FULL resolution (mapping needs true pixel dims), without locking it.
            var file = SessionManager.GetFrameFiles(sessionFolder).LastOrDefault();
            if (file != null)
            {
                var bi = new BitmapImage();
                using var ms = new MemoryStream(File.ReadAllBytes(file));
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                preview.Source = bi;
                _frameW = bi.PixelWidth;
                _frameH = bi.PixelHeight;
            }

            _rect = existingCrop;
            Loaded += (s, e) => SyncUi();
            rectCanvas.SizeChanged += (s, e) => SyncUi();
        }

        // ---- display ↔ frame mapping (the image is Uniform-stretched and letterboxed in the canvas) ----
        private (double disp, double offX, double offY)? Mapping()
        {
            double cw = rectCanvas.ActualWidth, ch = rectCanvas.ActualHeight;
            if (_frameW < 1 || _frameH < 1 || cw < 1 || ch < 1) return null;
            double disp = Math.Min(cw / _frameW, ch / _frameH);
            return (disp, (cw - _frameW * disp) / 2, (ch - _frameH * disp) / 2);
        }

        private System.Drawing.Point ToFrame(Point p)
        {
            if (Mapping() is not { } m) return default;
            int fx = (int)Math.Round((p.X - m.offX) / m.disp);
            int fy = (int)Math.Round((p.Y - m.offY) / m.disp);
            return new System.Drawing.Point(Math.Clamp(fx, 0, _frameW), Math.Clamp(fy, 0, _frameH));
        }

        // ---- drag to draw ----
        private void OnCanvasDown(object sender, MouseButtonEventArgs e)
        {
            if (_frameW < 1) return;
            _dragging = true;
            _dragStart = e.GetPosition(rectCanvas);
            rectCanvas.CaptureMouse();
        }

        private void OnCanvasMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var a = ToFrame(_dragStart);
            var b = ToFrame(e.GetPosition(rectCanvas));
            _rect = System.Drawing.Rectangle.FromLTRB(
                Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
            SyncUi();
        }

        private void OnCanvasUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            rectCanvas.ReleaseMouseCapture();
            if (_rect is { } r)
            {
                var clamped = VideoEncoder.ClampCrop(r, new System.Drawing.Size(_frameW, _frameH));
                _rect = clamped.Width >= 2 && clamped.Height >= 2 ? clamped : null;   // a stray click clears nothing
            }
            SyncUi();
        }

        // Numeric fields commit on focus-loss; a bad/degenerate combination just re-syncs to the last rect.
        private void OnFieldChanged(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(xBox.Text, out int x) && int.TryParse(yBox.Text, out int y) &&
                int.TryParse(wBox.Text, out int w) && int.TryParse(hBox.Text, out int h))
            {
                var clamped = VideoEncoder.ClampCrop(new System.Drawing.Rectangle(x, y, w, h),
                    new System.Drawing.Size(_frameW, _frameH));
                if (clamped.Width >= 2 && clamped.Height >= 2) _rect = clamped;
            }
            SyncUi();
        }

        private void SyncUi()
        {
            xBox.Text = (_rect?.X ?? 0).ToString();
            yBox.Text = (_rect?.Y ?? 0).ToString();
            wBox.Text = (_rect?.Width ?? 0).ToString();
            hBox.Text = (_rect?.Height ?? 0).ToString();
            cropInfo.Text = _rect is { } r ? $"→ video {r.Width}×{r.Height}" : "no crop (full frame)";
            destructiveBtn.IsEnabled = _rect != null;

            if (_rect is { } cr && Mapping() is { } m)
            {
                _rubber.Visibility = Visibility.Visible;
                Canvas.SetLeft(_rubber, m.offX + cr.X * m.disp);
                Canvas.SetTop(_rubber, m.offY + cr.Y * m.disp);
                _rubber.Width = Math.Max(1, cr.Width * m.disp);
                _rubber.Height = Math.Max(1, cr.Height * m.disp);
            }
            else
            {
                _rubber.Visibility = Visibility.Collapsed;
            }
        }

        private void OnClear(object sender, RoutedEventArgs e)
        {
            CropRect = null;
            DialogResult = true;
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            CropRect = _rect;
            DialogResult = true;
        }

        private void OnDestructive(object sender, RoutedEventArgs e)
        {
            if (_rect is not { } r) return;
            var res = MessageDialog.Show(
                $"Permanently crop EVERY frame on disk to {r.Width}×{r.Height} at ({r.X},{r.Y})?\n\n" +
                "This re-writes the frames and can't be undone. Capturing more frames into this session afterwards would mix sizes and block encoding — best used when the session is finished.",
                "Crop frames on disk", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;
            CropRect = r;
            DestructiveRequested = true;
            DialogResult = true;
        }
    }
}
