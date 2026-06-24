using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TimelapseCapture; // Core: ScreenHelper

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// Full-screen drag-to-select overlay. Returns the chosen region in PHYSICAL pixels (the
    /// coordinate space the capture engine uses). Covers the whole virtual screen so a region can
    /// be drawn on any monitor; DIP↔pixel conversion uses the system DPI scale.
    /// </summary>
    public partial class RegionSelectOverlay : Window
    {
        private Point _start;
        private bool _dragging;
        private readonly double _scale;
        private readonly System.Drawing.Rectangle _vs;
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

            _scale = ScreenHelper.SystemDpiScale();
            _vs = ScreenHelper.VirtualScreenBounds();

            // Cover the whole virtual screen, in device-independent units.
            Left = _vs.X / _scale;
            Top = _vs.Y / _scale;
            Width = _vs.Width / _scale;
            Height = _vs.Height / _scale;

            KeyDown += (s, e) => { if (e.Key == Key.Escape) Cancel(); };
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

            dimText.Text = $"{(int)(w * _scale)} × {(int)(h * _scale)}";
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

            // DIPs (relative to the overlay) -> physical pixels in virtual-screen space.
            int px = _vs.X + (int)Math.Round(x * _scale);
            int py = _vs.Y + (int)Math.Round(y * _scale);
            int pw = (int)Math.Round(w * _scale);
            int ph = (int)Math.Round(h * _scale);
            pw -= pw % 2; // even dimensions required by the H.264 encoder
            ph -= ph % 2;

            SelectedRegion = new System.Drawing.Rectangle(px, py, Math.Max(2, pw), Math.Max(2, ph));
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
