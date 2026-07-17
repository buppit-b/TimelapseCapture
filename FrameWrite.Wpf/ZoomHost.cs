using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FrameWrite.Wpf
{
    /// <summary>
    /// Zoomable viewport for dialog frame previews (crop/cull/trim): mouse-wheel zooms toward the
    /// cursor (1×–16×), middle-drag pans, and rendering snaps to NearestNeighbor from 2× so frame
    /// PIXELS are inspectable when detail decides a crop or a cull. A Border subclass, so XAML
    /// swaps it in for the plain preview Border with the same visual attributes.
    ///
    /// Left-button events are deliberately untouched — they pass through to the child (the crop
    /// dialog draws its rect with left-drag). Child code keeps working unchanged at any zoom:
    /// ActualWidth/Height are LAYOUT sizes (unaffected by RenderTransform) and GetPosition() is
    /// transform-aware, so display↔frame mapping math needs no knowledge of the zoom.
    /// </summary>
    public class ZoomHost : Border
    {
        private readonly MatrixTransform _xf = new();
        private bool _panning;
        private Point _panStart;      // pan anchor, host space
        private Matrix _panMatrix;    // transform at pan start

        public ZoomHost()
        {
            ClipToBounds = true;
            Loaded += (s, e) => Attach();
            SizeChanged += (s, e) => SetMatrix(_xf.Matrix);   // window resized → re-clamp the pan
        }

        public double Scale => _xf.Matrix.M11;

        /// <summary>Back to the fitted 1× view.</summary>
        public void Fit() => SetMatrix(Matrix.Identity);

        private void Attach()
        {
            if (Child != null && !ReferenceEquals(Child.RenderTransform, _xf))
                Child.RenderTransform = _xf;
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            if (Child == null) return;
            var m = _xf.Matrix;
            double target = Math.Clamp(m.M11 * (e.Delta > 0 ? 1.25 : 0.8), 1.0, 16.0);
            double factor = target / m.M11;
            e.Handled = true;   // the wheel over the preview always means zoom — never falls through
            if (Math.Abs(factor - 1) < 0.001) return;
            var p = e.GetPosition(this);
            m.ScaleAt(factor, factor, p.X, p.Y);
            SetMatrix(m);
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle || Scale <= 1.0) return;
            // Never steal capture mid-gesture (e.g. a middle-click DURING the crop's left-drag
            // would strand that drag in a buttonless state) — ignore the pan attempt instead.
            if (Mouse.Captured != null && !ReferenceEquals(Mouse.Captured, this)) return;
            _panning = true;
            _panStart = e.GetPosition(this);
            _panMatrix = _xf.Matrix;
            Cursor = Cursors.SizeAll;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            if (!_panning) return;
            if (e.MiddleButton != MouseButtonState.Pressed) { EndPan(); return; }
            var p = e.GetPosition(this);
            var m = _panMatrix;
            m.OffsetX += p.X - _panStart.X;
            m.OffsetY += p.Y - _panStart.Y;
            SetMatrix(m);
            e.Handled = true;
        }

        protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && _panning) { EndPan(); e.Handled = true; }
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            _panning = false;
            Cursor = null;
            base.OnLostMouseCapture(e);
        }

        private void EndPan()
        {
            _panning = false;
            Cursor = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        private void SetMatrix(Matrix m)
        {
            if (Child == null) return;
            Attach();
            // Snap to identity near 1× and clamp the pan so content always covers the viewport —
            // you can't lose the frame off-screen, and zooming fully out lands exactly on Fit.
            if (m.M11 <= 1.001) m = Matrix.Identity;
            else
            {
                m.OffsetX = Math.Clamp(m.OffsetX, Math.Min(0, ActualWidth - ActualWidth * m.M11), 0);
                m.OffsetY = Math.Clamp(m.OffsetY, Math.Min(0, ActualHeight - ActualHeight * m.M11), 0);
            }
            _xf.Matrix = m;
            // Crisp pixels once magnification makes them meaningful; smooth scaling below that.
            RenderOptions.SetBitmapScalingMode(Child, m.M11 >= 2 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Fant);
        }
    }
}
