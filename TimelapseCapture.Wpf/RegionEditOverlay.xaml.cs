using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using TimelapseCapture; // Core: ScreenHelper

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// Interactive region editor: shows the current capture region on screen as a movable/resizable
    /// box (drag the center to move, drag any of the 8 handles to resize). Returns the new region in
    /// PHYSICAL pixels on Apply. This is the "edit mode" — unlike <see cref="RegionOverlay"/> it is NOT
    /// click-through, so it only makes sense when not capturing.
    /// </summary>
    public partial class RegionEditOverlay : Window
    {
        private enum Mode { None, Move, NW, N, NE, E, SE, S, SW, W }

        private readonly double _scale;
        private readonly System.Drawing.Rectangle _vs;
        private readonly int _ratioW;            // 0 = free (no constraint)
        private readonly int _ratioH;
        private double _cw, _ch;                 // canvas size in DIPs
        private double L, T, R, B;               // region edges in DIP canvas coords
        private Mode _mode = Mode.None;
        private Point _last;
        private const double MinSize = 20;       // DIP
        private const double HandleHit = 9;      // half hit-radius around a handle (DIP)

        private readonly Dictionary<Mode, Rectangle> _handles = new();

        /// <summary>The edited region in physical pixels, or null if cancelled.</summary>
        public System.Drawing.Rectangle? SelectedRegion { get; private set; }

        public RegionEditOverlay(System.Drawing.Rectangle region, int ratioW = 0, int ratioH = 0)
        {
            InitializeComponent();

            _ratioW = ratioW;
            _ratioH = ratioH;
            hintText.Text = (ratioW > 0 && ratioH > 0)
                ? $"Drag to move · corners keep {ratioW}:{ratioH} (Shift frees) · edges resize freely · Enter applies · Esc cancels"
                : "Drag to move · drag handles to resize · Enter applies · Esc cancels";

            _scale = ScreenHelper.SystemDpiScale();
            _vs = ScreenHelper.VirtualScreenBounds();

            Left = _vs.X / _scale;
            Top = _vs.Y / _scale;
            Width = _cw = _vs.Width / _scale;
            Height = _ch = _vs.Height / _scale;

            // Seed edges from the incoming physical region, converted to canvas DIPs.
            L = (region.X - _vs.X) / _scale;
            T = (region.Y - _vs.Y) / _scale;
            R = L + region.Width / _scale;
            B = T + region.Height / _scale;

            _handles[Mode.NW] = hNW; _handles[Mode.N] = hN; _handles[Mode.NE] = hNE;
            _handles[Mode.E] = hE; _handles[Mode.SE] = hSE; _handles[Mode.S] = hS;
            _handles[Mode.SW] = hSW; _handles[Mode.W] = hW;

            Loaded += (s, e) => Layout();
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) Cancel();
                else if (e.Key == Key.Enter) Apply();
            };
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            _last = e.GetPosition(canvas);
            _mode = HitTest(_last);
            if (_mode != Mode.None) CaptureMouse();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_mode == Mode.None) return;

            var p = e.GetPosition(canvas);
            double dx = p.X - _last.X, dy = p.Y - _last.Y;
            _last = p;

            if (_mode == Mode.Move)
            {
                L += dx; R += dx; T += dy; B += dy;
                if (L < 0) { R -= L; L = 0; }
                if (T < 0) { B -= T; T = 0; }
                if (R > _cw) { L -= (R - _cw); R = _cw; }
                if (B > _ch) { T -= (B - _ch); B = _ch; }
            }
            else
            {
                if (_mode is Mode.W or Mode.NW or Mode.SW) L += dx;
                if (_mode is Mode.E or Mode.NE or Mode.SE) R += dx;
                if (_mode is Mode.N or Mode.NW or Mode.NE) T += dy;
                if (_mode is Mode.S or Mode.SW or Mode.SE) B += dy;

                // Aspect-ratio lock: corner handles keep the selected ratio (height follows width,
                // anchored at the opposite edge). Hold Shift, use an edge handle, or pick the Free
                // ratio to break away. Edge handles always resize one axis freely.
                bool corner = _mode is Mode.NW or Mode.NE or Mode.SW or Mode.SE;
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                if (corner && _ratioW > 0 && _ratioH > 0 && !shift)
                {
                    double h = (R - L) * _ratioH / _ratioW;
                    if (_mode is Mode.SE or Mode.SW) B = T + h; // top edge fixed
                    else T = B - h;                             // bottom edge fixed (NW / NE)
                }

                // Clamp to the screen and enforce a minimum size on the edge being dragged.
                L = Math.Max(0, Math.Min(L, R - MinSize));
                R = Math.Min(_cw, Math.Max(R, L + MinSize));
                T = Math.Max(0, Math.Min(T, B - MinSize));
                B = Math.Min(_ch, Math.Max(B, T + MinSize));
            }

            Layout();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_mode != Mode.None) { _mode = Mode.None; ReleaseMouseCapture(); }
        }

        private Mode HitTest(Point p)
        {
            foreach (var kv in _handles)
            {
                double cx = Canvas.GetLeft(kv.Value) + kv.Value.Width / 2;
                double cy = Canvas.GetTop(kv.Value) + kv.Value.Height / 2;
                if (Math.Abs(p.X - cx) <= HandleHit && Math.Abs(p.Y - cy) <= HandleHit)
                    return kv.Key;
            }
            if (p.X >= L && p.X <= R && p.Y >= T && p.Y <= B) return Mode.Move;
            return Mode.None;
        }

        private void Layout()
        {
            double w = R - L, h = B - T;

            Place(outline, L, T, w, h);
            dimText.Text = $"{(int)Math.Round(w * _scale)} × {(int)Math.Round(h * _scale)}";

            // Dim mask around the region.
            Place(dimTop, 0, 0, _cw, T);
            Place(dimBottom, 0, B, _cw, _ch - B);
            Place(dimLeft, 0, T, L, h);
            Place(dimRight, R, T, _cw - R, h);

            PutHandle(hNW, L, T); PutHandle(hN, (L + R) / 2, T); PutHandle(hNE, R, T);
            PutHandle(hE, R, (T + B) / 2); PutHandle(hSE, R, B); PutHandle(hS, (L + R) / 2, B);
            PutHandle(hSW, L, B); PutHandle(hW, L, (T + B) / 2);

            Canvas.SetLeft(dimBox, L);
            Canvas.SetTop(dimBox, Math.Max(0, T - 26));
        }

        private static void Place(Rectangle r, double x, double y, double w, double h)
        {
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            r.Width = Math.Max(0, w);
            r.Height = Math.Max(0, h);
        }

        private static void PutHandle(Rectangle r, double cx, double cy)
        {
            Canvas.SetLeft(r, cx - r.Width / 2);
            Canvas.SetTop(r, cy - r.Height / 2);
        }

        private void OnApply(object sender, RoutedEventArgs e) => Apply();
        private void OnCancel(object sender, RoutedEventArgs e) => Cancel();

        private void Apply()
        {
            int px = _vs.X + (int)Math.Round(L * _scale);
            int py = _vs.Y + (int)Math.Round(T * _scale);
            int pw = (int)Math.Round((R - L) * _scale);
            int ph = (int)Math.Round((B - T) * _scale);
            pw -= pw % 2; // even dimensions required by the H.264 encoder
            ph -= ph % 2;

            SelectedRegion = new System.Drawing.Rectangle(px, py, Math.Max(2, pw), Math.Max(2, ph));
            DialogResult = true;
            Close();
        }

        private void Cancel()
        {
            DialogResult = false;
            Close();
        }
    }
}
