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

        /// <summary>Set when the user confirmed a retroactive bake — the VM runs it after the dialog closes.</summary>
        public bool BakeRequested { get; private set; }

        /// <summary>Back up the session (frames + session.json) before baking — the safe default.</summary>
        public bool BackupFirstRequested { get; private set; }

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
            Loaded += (s, e) => { Subscribe(); RenderPreview(); RefreshBakeEnabled(); };
            Closed += (s, e) => { Unsubscribe(); _renderDebounce.Stop(); };
            sizeBox.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) OnApplySize(s, new RoutedEventArgs()); };
            BuildSwatches(textSwatches, (vm, hex) => vm.OverlayTextColor = hex);
            BuildSwatches(backSwatches, (vm, hex) => vm.OverlayBackColor = hex);
        }

        // Compact clickable colour chips beside the hex boxes — the hex box stays the precise input.
        private static readonly string[] Swatches =
        {
            "#FFFFFF", "#000000", "#808080", "#E5534B", "#F5A623",
            "#F7D74A", "#39D353", "#2FC7D8", "#4A90D9", "#C061F0",
        };

        private void BuildSwatches(System.Windows.Controls.Panel host, Action<MainViewModel, string> apply)
        {
            foreach (var hex in Swatches)
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                var chip = new System.Windows.Controls.Border
                {
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 4, 0),
                    CornerRadius = new CornerRadius(3),
                    BorderThickness = new Thickness(1),
                    Background = new System.Windows.Media.SolidColorBrush(color),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = hex,
                };
                chip.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "StrokeBrush");
                string h = hex;   // capture per-chip
                chip.MouseLeftButtonDown += (s, e) => { if (Vm is { } vm) apply(vm, h); };
                host.Children.Add(chip);
            }
        }

        // Bake needs frames on disk, an idle app, and overlay text to draw — kept fresh via VM changes.
        private void RefreshBakeEnabled()
        {
            bakeBtn.IsEnabled = Vm is { } vm && vm.FrameCount > 0 && !vm.IsCapturing && !vm.IsEncoding
                                && !string.IsNullOrWhiteSpace(vm.OverlayText);
        }

        private void OnBake(object sender, RoutedEventArgs e)
        {
            if (Vm is not { } vm || vm.FrameCount < 1) return;
            int choice = MessageDialog.ShowChoices(
                $"Permanently burn this overlay into all {vm.FrameCount} frame(s) on disk?\n\n" +
                "Timestamp tokens use each frame file's own capture time, so past frames get their real " +
                "times — not today's. This re-writes every frame and can't be undone.\n\n" +
                "Note: frames that already had the overlay burned in at capture would get a second copy " +
                "drawn over the first.",
                "Bake overlay into frames", MessageBoxImage.Warning,
                "Back up, then bake", "Bake without backup", "Cancel");
            if (choice is not (0 or 1)) return;
            BackupFirstRequested = choice == 0;
            BakeRequested = true;
            Close();   // the bake runs from the main window (progress in the encode status line)
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
            { _renderDebounce.Stop(); _renderDebounce.Start(); RefreshBakeEnabled(); }
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
                    TextColor = vm.OverlayTextColor,
                    TextOpacity = vm.OverlayTextOpacity,
                    BackColor = vm.OverlayBackColor,
                    BackOpacity = vm.OverlayBackOpacity,
                }, Math.Max(1, vm.FrameCount));   // sample frame number so {frame} previews realistically

                // If the session has an encode-crop, show it: dim everything outside + an accent outline,
                // so the overlay can be positioned INSIDE the area that will survive the crop.
                bool cropShown = false;
                if (vm.CurrentSessionFolder is { } sf &&
                    SessionManager.LoadSession(sf)?.EncodeCrop is { } crop)
                {
                    var cr = new System.Drawing.Rectangle(
                        (int)(crop.X * scale), (int)(crop.Y * scale),
                        (int)(crop.Width * scale), (int)(crop.Height * scale));
                    cr = System.Drawing.Rectangle.Intersect(cr, new System.Drawing.Rectangle(0, 0, w, h));
                    if (cr.Width > 1 && cr.Height > 1)
                    {
                        using var g2 = System.Drawing.Graphics.FromImage(bmp);
                        using var dim = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(140, 0, 0, 0));
                        g2.FillRectangle(dim, 0, 0, w, cr.Top);                                  // above
                        g2.FillRectangle(dim, 0, cr.Bottom, w, h - cr.Bottom);                    // below
                        g2.FillRectangle(dim, 0, cr.Top, cr.Left, cr.Height);                     // left
                        g2.FillRectangle(dim, cr.Right, cr.Top, w - cr.Right, cr.Height);         // right
                        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(220, 0xC0, 0x61, 0xF0), 2)
                        { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                        g2.DrawRectangle(pen, cr);
                        cropShown = true;
                    }
                }

                previewImage.Source = ToSource(bmp);
                previewCaption.Text = $"Example at your frame size ({size.Width}×{size.Height}) — exactly what gets burned in." +
                                      (cropShown ? " Dimmed area = outside the encode crop." : "");
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
