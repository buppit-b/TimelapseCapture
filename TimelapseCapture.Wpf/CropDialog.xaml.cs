using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// Pick the frame area encodes keep. Full edit interaction, matching the region-edit overlay's
    /// conventions: drag empty space to draw, drag inside to MOVE, drag the 8 edge/corner handles to
    /// fine-tune; the ratio lock applies to corners and new drags (edges stay free, Shift frees).
    /// Non-destructive by default (SessionInfo.EncodeCrop, applied by ffmpeg at encode); a consented
    /// power-user button crops the frames ON DISK instead. All stored coordinates are FRAME pixels.
    /// </summary>
    public partial class CropDialog : Window
    {
        private enum Mode { None, New, Move, NW, N, NE, E, SE, S, SW, W }

        private readonly string[] _files;
        private int _frameW, _frameH;

        /// <summary>The chosen crop in frame pixels, or null for "no crop". Valid when DialogResult is true.</summary>
        public System.Drawing.Rectangle? CropRect { get; private set; }

        /// <summary>True when the user chose (and confirmed) the destructive crop-frames-on-disk action.</summary>
        public bool DestructiveRequested { get; private set; }

        private Mode _mode = Mode.None;
        private System.Drawing.Point _dragStart;          // frame px where the drag began
        private System.Drawing.Rectangle _startRect;      // rect at drag start (Move/resize anchor)
        private System.Drawing.Rectangle? _rect;          // current crop, frame px
        private int _ratioW, _ratioH;                     // 0 = free

        private readonly System.Windows.Shapes.Rectangle _rubber = new()
        {
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
        };

        public CropDialog(string sessionFolder, System.Drawing.Rectangle? existingCrop, bool overlayEnabled = false)
        {
            InitializeComponent();
            _rubber.Stroke = TryFindResource("AccentBrush") as Brush ?? Brushes.LimeGreen;
            rectCanvas.Children.Add(_rubber);
            _rubber.Visibility = Visibility.Collapsed;
            _rubber.IsHitTestVisible = false;   // all hit-testing happens on the canvas

            _files = SessionManager.GetFrameFiles(sessionFolder);
            overlayHint.Visibility = overlayEnabled ? Visibility.Visible : Visibility.Collapsed;

            _rect = existingCrop;
            scrub.Maximum = Math.Max(1, _files.Length);
            scrub.ValueChanged += (s, e) => LoadFrame((int)e.NewValue);
            Loaded += (s, e) => { scrub.Value = Math.Max(1, _files.Length); LoadFrame((int)scrub.Value); };
            rectCanvas.SizeChanged += (s, e) => SyncUi();
        }

        // Show frame n at FULL resolution (the crop mapping needs true pixel dims), without locking the
        // file. Frames are uniform by invariant, so the crop rect stays valid across the scrub.
        private void LoadFrame(int n)
        {
            if (_files.Length == 0) return;
            n = Math.Clamp(n, 1, _files.Length);
            try
            {
                var bi = new BitmapImage();
                using var ms = new MemoryStream(File.ReadAllBytes(_files[n - 1]));
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                preview.Source = bi;
                _frameW = bi.PixelWidth;
                _frameH = bi.PixelHeight;
            }
            catch { /* unreadable frame — keep the previous preview */ }
            posText.Text = $"Frame {n} of {_files.Length}";
            SyncUi();
        }

        // ±1 / ±10 frame steppers (hold to repeat) — the step size rides in the button's Tag.
        private void OnStep(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is string t && int.TryParse(t, out int delta))
                scrub.Value = Math.Clamp((int)scrub.Value + delta, 1, Math.Max(1, _files.Length));
        }

        // ---- ratio lock (Seg row + orientation flip) ----
        private int _baseRatioW, _baseRatioH;   // the chosen preset; _ratioW/H are the EFFECTIVE (flipped) values

        private void OnRatio(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is string t)
            {
                var parts = t.Split(':');
                _baseRatioW = parts.Length == 2 && int.TryParse(parts[0], out int w) ? w : 0;
                _baseRatioH = parts.Length == 2 && int.TryParse(parts[1], out int h) ? h : 0;
                ApplyRatioOrientation();
                ReshapeToRatio();
            }
        }

        // Flip = TRANSPOSE the current selection (swap its width/height about its own centre) — a
        // clean 90° rotation that works on the Free ratio too — and flip the lock's orientation for
        // future drags. (The old behavior re-derived height from width via the ratio, which ballooned
        // a wide selection into an enormous tall one instead of rotating it.)
        private void OnFlip(object sender, RoutedEventArgs e)
        {
            ApplyRatioOrientation();
            if (_rect is { } r)
            {
                double cx = r.X + r.Width / 2.0, cy = r.Y + r.Height / 2.0;
                int nw = r.Height, nh = r.Width;
                // Shrink proportionally if the rotated rect can't fit on the frame.
                double s = Math.Min(1.0, Math.Min((double)_frameW / nw, (double)_frameH / nh));
                nw = Math.Max(2, (int)(nw * s));
                nh = Math.Max(2, (int)(nh * s));
                int nx = Math.Clamp((int)Math.Round(cx - nw / 2.0), 0, Math.Max(0, _frameW - nw));
                int ny = Math.Clamp((int)Math.Round(cy - nh / 2.0), 0, Math.Max(0, _frameH - nh));
                _rect = VideoEncoder.ClampCrop(new System.Drawing.Rectangle(nx, ny, nw, nh),
                    new System.Drawing.Size(_frameW, _frameH));
                SyncUi();
            }
        }

        private void ApplyRatioOrientation()
        {
            bool flip = flipToggle.IsChecked == true;
            _ratioW = flip ? _baseRatioH : _baseRatioW;
            _ratioH = flip ? _baseRatioW : _baseRatioH;
        }

        // Re-shape the current crop to the active ratio: keep the width, adjust the height about the
        // rect's centre; shrink the width when the new height can't fit the frame.
        private void ReshapeToRatio()
        {
            if (_rect is not { } r || _ratioW <= 0 || _ratioH <= 0) return;
            double cy = r.Y + r.Height / 2.0;
            int nw = r.Width, nh = Math.Max(2, nw * _ratioH / _ratioW);
            if (nh > _frameH) { nh = _frameH; nw = Math.Max(2, nh * _ratioW / _ratioH); }
            int nx = Math.Clamp(r.X + (r.Width - nw) / 2, 0, Math.Max(0, _frameW - nw));
            int ny = Math.Clamp((int)Math.Round(cy - nh / 2.0), 0, Math.Max(0, _frameH - nh));
            _rect = VideoEncoder.ClampCrop(new System.Drawing.Rectangle(nx, ny, nw, nh),
                new System.Drawing.Size(_frameW, _frameH));
            SyncUi();
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

        // Which part of the crop is under the pointer (frame px, tolerance scaled from ~8 display px).
        private Mode HitTest(System.Drawing.Point p)
        {
            if (_rect is not { } r || Mapping() is not { } m) return Mode.New;
            int tol = Math.Max(2, (int)Math.Round(8 / m.disp));
            bool nearL = Math.Abs(p.X - r.Left) <= tol, nearR = Math.Abs(p.X - r.Right) <= tol;
            bool nearT = Math.Abs(p.Y - r.Top) <= tol, nearB = Math.Abs(p.Y - r.Bottom) <= tol;
            bool inX = p.X >= r.Left - tol && p.X <= r.Right + tol;
            bool inY = p.Y >= r.Top - tol && p.Y <= r.Bottom + tol;

            if (nearT && nearL) return Mode.NW;
            if (nearT && nearR) return Mode.NE;
            if (nearB && nearL) return Mode.SW;
            if (nearB && nearR) return Mode.SE;
            if (nearT && inX) return Mode.N;
            if (nearB && inX) return Mode.S;
            if (nearL && inY) return Mode.W;
            if (nearR && inY) return Mode.E;
            if (r.Contains(p)) return Mode.Move;
            return Mode.New;
        }

        private static Cursor CursorFor(Mode m) => m switch
        {
            Mode.NW or Mode.SE => Cursors.SizeNWSE,
            Mode.NE or Mode.SW => Cursors.SizeNESW,
            Mode.N or Mode.S => Cursors.SizeNS,
            Mode.E or Mode.W => Cursors.SizeWE,
            Mode.Move => Cursors.SizeAll,
            _ => Cursors.Cross,
        };

        // ---- drag interaction ----
        private void OnCanvasDown(object sender, MouseButtonEventArgs e)
        {
            if (_frameW < 1) return;
            _dragStart = ToFrame(e.GetPosition(rectCanvas));
            _mode = HitTest(_dragStart);
            _startRect = _rect ?? System.Drawing.Rectangle.Empty;
            rectCanvas.CaptureMouse();
        }

        private void OnCanvasMove(object sender, MouseEventArgs e)
        {
            var fp = ToFrame(e.GetPosition(rectCanvas));

            if (_mode == Mode.None)   // hover: just show what a drag would do
            {
                rectCanvas.Cursor = CursorFor(HitTest(fp));
                return;
            }

            int dx = fp.X - _dragStart.X, dy = fp.Y - _dragStart.Y;
            switch (_mode)
            {
                case Mode.New:
                    _rect = System.Drawing.Rectangle.FromLTRB(
                        Math.Min(_dragStart.X, fp.X), Math.Min(_dragStart.Y, fp.Y),
                        Math.Max(_dragStart.X, fp.X), Math.Max(_dragStart.Y, fp.Y));
                    _rect = ConstrainNew(_rect.Value);
                    break;

                case Mode.Move:
                    var moved = _startRect;
                    moved.X = Math.Clamp(_startRect.X + dx, 0, Math.Max(0, _frameW - _startRect.Width));
                    moved.Y = Math.Clamp(_startRect.Y + dy, 0, Math.Max(0, _frameH - _startRect.Height));
                    _rect = moved;
                    break;

                default:
                    _rect = Resize(_startRect, _mode, dx, dy);
                    break;
            }
            SyncUi();
        }

        private void OnCanvasUp(object sender, MouseButtonEventArgs e)
        {
            if (_mode == Mode.None) return;
            bool wasNew = _mode == Mode.New;
            _mode = Mode.None;
            rectCanvas.ReleaseMouseCapture();
            if (_rect is { } r)
            {
                var clamped = VideoEncoder.ClampCrop(r, new System.Drawing.Size(_frameW, _frameH));
                // A stray CLICK (no real drag) shouldn't wipe an existing crop; a degenerate new-drag clears to none.
                _rect = clamped.Width >= 2 && clamped.Height >= 2 ? clamped : (wasNew ? _startRect == System.Drawing.Rectangle.Empty ? null : _startRect : _startRect);
                if (_rect is { Width: < 2 } or { Height: < 2 }) _rect = null;
            }
            SyncUi();
        }

        // Shrink a fresh drag to the locked ratio (same rule as the region-select overlay). Shift frees.
        private System.Drawing.Rectangle ConstrainNew(System.Drawing.Rectangle r)
        {
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (_ratioW <= 0 || _ratioH <= 0 || shift || r.Width <= 0 || r.Height <= 0) return r;
            double target = (double)_ratioW / _ratioH;
            if ((double)r.Width / r.Height > target) r.Width = (int)Math.Round(r.Height * target);
            else r.Height = (int)Math.Round(r.Width / target);
            return r;
        }

        // Handle-resize with the region-edit conventions: corners keep the locked ratio (height follows
        // width, anchored at the opposite corner; Shift frees), edges always resize one axis freely.
        private System.Drawing.Rectangle Resize(System.Drawing.Rectangle s, Mode m, int dx, int dy)
        {
            int L = s.Left, T = s.Top, R = s.Right, B = s.Bottom;
            switch (m)
            {
                case Mode.NW: L += dx; T += dy; break;
                case Mode.NE: R += dx; T += dy; break;
                case Mode.SW: L += dx; B += dy; break;
                case Mode.SE: R += dx; B += dy; break;
                case Mode.N: T += dy; break;
                case Mode.S: B += dy; break;
                case Mode.W: L += dx; break;
                case Mode.E: R += dx; break;
            }
            // Keep at least a sliver and normalized edges.
            if (R - L < 2) { if (m is Mode.W or Mode.NW or Mode.SW) L = R - 2; else R = L + 2; }
            if (B - T < 2) { if (m is Mode.N or Mode.NW or Mode.NE) T = B - 2; else B = T + 2; }

            bool corner = m is Mode.NW or Mode.NE or Mode.SW or Mode.SE;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (corner && _ratioW > 0 && _ratioH > 0 && !shift)
            {
                int h = Math.Max(2, (R - L) * _ratioH / _ratioW);
                if (m is Mode.NW or Mode.NE) T = B - h; else B = T + h;   // anchor the opposite edge
            }
            return Clamp(System.Drawing.Rectangle.FromLTRB(L, T, R, B));
        }

        private System.Drawing.Rectangle Clamp(System.Drawing.Rectangle r) =>
            System.Drawing.Rectangle.Intersect(r, new System.Drawing.Rectangle(0, 0, _frameW, _frameH));

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
                "This re-writes the frames and can't be undone. If you capture more frames into this session afterwards, they'll be scaled (letterboxed) down to the cropped size to stay consistent.",
                "Crop frames on disk", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;
            CropRect = r;
            DestructiveRequested = true;
            DialogResult = true;
        }
    }
}
