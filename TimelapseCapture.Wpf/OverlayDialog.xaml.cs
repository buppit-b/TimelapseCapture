using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TimelapseCapture.Wpf.ViewModels;

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// Dedicated editor for the frame text overlay (opened from the header, beside Settings).
    /// Shows a LIVE example rendered by the same OverlayRenderer that burns into frames, over the
    /// session's latest frame (or a placeholder) at the real frame size — adjust, see, then record.
    /// </summary>
    public partial class OverlayDialog : Window
    {
        private readonly System.Windows.Threading.DispatcherTimer _renderDebounce;

        public OverlayDialog()
        {
            InitializeComponent();
            fontBox.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source)
                                       .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            // The text field updates per keystroke and a render is a full-frame-size bitmap + encode —
            // debounce so typing stays fluid and the preview settles ~150ms after the last change.
            _renderDebounce = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(150) };
            _renderDebounce.Tick += (s, e) => { _renderDebounce.Stop(); RenderPreview(); };
            Loaded += (s, e) => { Subscribe(); RenderPreview(); };
            Closed += (s, e) => { Unsubscribe(); _renderDebounce.Stop(); };
            sizeBox.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) OnApplySize(s, new RoutedEventArgs()); };
        }

        // The size box commits on focus-loss like every numeric field — this applies it immediately
        // so "type a size, see the example change" needs no tab-away.
        private void OnApplySize(object sender, RoutedEventArgs e)
        {
            sizeBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            RenderPreview();
        }

        // ---- drag-to-place on the preview ----
        private bool _dragging;

        private void OnPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _dragging = true;
            previewImage.CaptureMouse();
            PlaceFromPoint(e.GetPosition(previewImage));
        }

        private void OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_dragging) PlaceFromPoint(e.GetPosition(previewImage));
        }

        private void OnPreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _dragging = false;
            previewImage.ReleaseMouseCapture();
        }

        // Map a point on the Uniform-stretched preview Image to normalized frame coords (0..1),
        // accounting for the letterbox bars, and set the overlay's free position there.
        private void PlaceFromPoint(Point p)
        {
            if (Vm is not { } vm || previewImage.Source is not System.Windows.Media.Imaging.BitmapSource src) return;
            double cw = previewImage.ActualWidth, ch = previewImage.ActualHeight;
            if (cw < 1 || ch < 1) return;
            double disp = Math.Min(cw / src.PixelWidth, ch / src.PixelHeight);
            double dw = src.PixelWidth * disp, dh = src.PixelHeight * disp;
            double offX = (cw - dw) / 2, offY = (ch - dh) / 2;
            double nx = (p.X - offX) / dw, ny = (p.Y - offY) / dh;
            vm.SetOverlayCustomNormalized(nx, ny);
            _renderDebounce.Stop();
            RenderPreview();   // immediate feedback while dragging (debounce would lag the drag)
        }

        private MainViewModel? Vm => DataContext as MainViewModel;

        private void Subscribe() { if (Vm is { } vm) vm.PropertyChanged += OnVmChanged; }
        private void Unsubscribe() { if (Vm is { } vm) vm.PropertyChanged -= OnVmChanged; }

        private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName?.StartsWith("Overlay", StringComparison.Ordinal) == true)
            { _renderDebounce.Stop(); _renderDebounce.Start(); }
        }

        private void RenderPreview()
        {
            if (Vm is not { } vm) return;
            try
            {
                // Render at the REAL frame size so proportions are truthful: a 24px font on a 1080p
                // frame looks exactly this small. Backdrop = the latest captured frame when one exists.
                var size = vm.CurrentRegionSize is { Width: > 0, Height: > 0 } r
                    ? r : new System.Drawing.Size(1920, 1080);
                // Cap the preview surface — smaller keeps drag-to-place responsive (a full bitmap is
                // rebuilt each mouse-move); ~1000px is crisp for a 270px-tall preview even on hi-DPI.
                double scale = Math.Min(1.0, 1000.0 / Math.Max(size.Width, size.Height));
                int w = Math.Max(64, (int)(size.Width * scale)), h = Math.Max(36, (int)(size.Height * scale));

                using var bmp = new System.Drawing.Bitmap(w, h);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    DrawBackdrop(g, vm, w, h);
                }
                // Same renderer as the capture path. Font size scales with the preview surface so the
                // example stays proportionally exact even when the bitmap was capped.
                OverlayRenderer.Draw(bmp, new OverlayConfig
                {
                    Enabled = true,
                    Text = vm.OverlayText,
                    Position = vm.OverlayPosition,
                    FontSize = vm.OverlayFontSize > 0 ? Math.Max(1, (int)(vm.OverlayFontSize * scale)) : 0,
                    FontFamily = vm.OverlayFontFamily,
                    CustomX = vm.OverlayCustomX,
                    CustomY = vm.OverlayCustomY,
                });

                previewImage.Source = ToSource(bmp);
                previewCaption.Text = $"Example at your frame size ({size.Width}×{size.Height}) — exactly what gets burned in.";
            }
            catch { previewImage.Source = null; }
        }

        private static void DrawBackdrop(System.Drawing.Graphics g, MainViewModel vm, int w, int h)
        {
            // Latest frame if the session has one…
            try
            {
                string? folder = vm.CurrentSessionFolder;
                if (folder != null)
                {
                    var frames = SessionManager.GetFrameFiles(folder);
                    if (frames.Length > 0)
                    {
                        using var img = System.Drawing.Image.FromFile(frames[^1]);
                        g.DrawImage(img, 0, 0, w, h);
                        return;
                    }
                }
            }
            catch { /* fall through to the placeholder */ }

            // …else a placeholder canvas: dark gradient + a faint grid, so position/size still read well.
            using var grad = new System.Drawing.Drawing2D.LinearGradientBrush(
                new System.Drawing.Rectangle(0, 0, w, h),
                System.Drawing.Color.FromArgb(255, 24, 26, 32),
                System.Drawing.Color.FromArgb(255, 10, 10, 12), 60f);
            g.FillRectangle(grad, 0, 0, w, h);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(28, 255, 255, 255));
            for (int x = w / 8; x < w; x += w / 8) g.DrawLine(pen, x, 0, x, h);
            for (int y = h / 8; y < h; y += h / 8) g.DrawLine(pen, 0, y, w, y);
        }

        private static BitmapImage ToSource(System.Drawing.Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
    }
}
