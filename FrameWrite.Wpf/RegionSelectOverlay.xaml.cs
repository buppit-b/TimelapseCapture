using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FrameWrite; // Core: ScreenHelper

namespace FrameWrite.Wpf
{
    /// <summary>
    /// Full-screen drag-to-select overlay. Returns the chosen region in PHYSICAL pixels (the
    /// coordinate space the capture engine uses). Covers the whole virtual screen (placed in raw
    /// pixels via SetWindowPos); DIP→pixel mapping goes through PointToScreen, which applies the
    /// window's REAL per-monitor transform — no hand-rolled DPI math to drift on mixed-DPI setups.
    /// </summary>
    public partial class RegionSelectOverlay : Window
    {
        private Point _start;
        private bool _dragging;
        private readonly int _ratioW;
        private readonly int _ratioH;

        /// <summary>The selected region in physical pixels, or null if cancelled.</summary>
        public System.Drawing.Rectangle? SelectedRegion { get; private set; }

        /// <param name="ratioW">Aspect-ratio width component (0 = free / no constraint).</param>
        /// <param name="ratioH">Aspect-ratio height component (0 = free / no constraint).</param>
        public RegionSelectOverlay(int ratioW = 0, int ratioH = 0)
        {
            InitializeComponent();

            _ratioW = ratioW;
            _ratioH = ratioH;

            // First approximation in DIPs (avoids a flash at 0,0); the exact physical placement
            // happens in SourceInitialized below.
            double scale = ScreenHelper.SystemDpiScale();
            var vs = ScreenHelper.VirtualScreenBounds();
            Left = vs.X / scale;
            Top = vs.Y / scale;
            Width = vs.Width / scale;
            Height = vs.Height / scale;
            SourceInitialized += (s, e) => OverlayPlacement.CoverVirtualScreen(this);

            KeyDown += (s, e) => { if (e.Key == Key.Escape) Cancel(); };

            // Force foreground on show. Launched from the modal setup wizard the overlay could come up
            // WITHOUT activation, so Windows ate the first mouse-down as an activation click and the
            // drag didn't start — the "region didn't take the first time" report. Activating on load
            // (and again once rendered) makes the very first click begin the selection.
            Loaded += (s, e) => { Activate(); Focus(); };
        }

        // The final DIP box, mapped to physical pixels through the window's own transform.
        private System.Drawing.Rectangle PhysicalRect(double x, double y, double w, double h)
        {
            var p1 = canvas.PointToScreen(new Point(x, y));            // device pixels, absolute
            var p2 = canvas.PointToScreen(new Point(x + w, y + h));
            int px = (int)Math.Round(Math.Min(p1.X, p2.X));
            int py = (int)Math.Round(Math.Min(p1.Y, p2.Y));
            int pw = (int)Math.Round(Math.Abs(p2.X - p1.X));
            int ph = (int)Math.Round(Math.Abs(p2.Y - p1.Y));
            return new System.Drawing.Rectangle(px, py, pw, ph);
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            Activate();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            _start = e.GetPosition(canvas);
            _dragging = true;
            selRect.Visibility = Visibility.Visible;
            dimBox.Visibility = Visibility.Visible;
            CaptureMouse();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            var p = e.GetPosition(canvas);
            double x = Math.Min(_start.X, p.X), y = Math.Min(_start.Y, p.Y);
            double w = Math.Abs(p.X - _start.X), h = Math.Abs(p.Y - _start.Y);
            (w, h) = Constrain(w, h);

            Canvas.SetLeft(selRect, x);
            Canvas.SetTop(selRect, y);
            selRect.Width = w;
            selRect.Height = h;

            var live = PhysicalRect(x, y, w, h);
            dimText.Text = $"{live.Width} × {live.Height}";
            Canvas.SetLeft(dimBox, x);
            Canvas.SetTop(dimBox, Math.Max(0, y - 28));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();

            var p = e.GetPosition(canvas);
            double x = Math.Min(_start.X, p.X), y = Math.Min(_start.Y, p.Y);
            double w = Math.Abs(p.X - _start.X), h = Math.Abs(p.Y - _start.Y);
            (w, h) = Constrain(w, h);

            if (w < 5 || h < 5) { Cancel(); return; }

            var phys = PhysicalRect(x, y, w, h);
            int pw = phys.Width - phys.Width % 2;   // even dimensions required by the H.264 encoder
            int ph = phys.Height - phys.Height % 2;

            SelectedRegion = new System.Drawing.Rectangle(phys.X, phys.Y, Math.Max(2, pw), Math.Max(2, ph));
            DialogResult = true;
            Close();
        }

        // Shrink (w,h) to the locked aspect ratio, keeping the box inside the drag. Free = unchanged.
        private (double w, double h) Constrain(double w, double h)
        {
            if (_ratioW <= 0 || _ratioH <= 0 || w <= 0 || h <= 0) return (w, h);
            double target = (double)_ratioW / _ratioH;
            double current = w / h;
            if (current > target) return (h * target, h); // too wide → limit width to the height
            return (w, w / target);                        // too tall → limit height to the width
        }

        private void Cancel()
        {
            DialogResult = false;
            Close();
        }
    }
}
