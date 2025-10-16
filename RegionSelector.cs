using System;
using System.Drawing;
using System.Windows.Forms;

namespace TimelapseCapture
{
    public class RegionSelector : Form
    {
        private Point start;
        private Point end;
        private bool drawing = false;

        // Returned in ABSOLUTE screen coordinates (so CaptureScreen() in MainForm can use it directly)
        public Rectangle SelectedRegion { get; private set; }

        public RegionSelector()
        {
            // Make the form cover the entire virtual desktop (all monitors)
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;

            FormBorderStyle = FormBorderStyle.None;
            // don't maximize — directly set bounds above so we cover multi-monitor setups
            DoubleBuffered = true;
            BackColor = Color.Black;
            Opacity = 0.35;
            Cursor = Cursors.Cross;
            TopMost = true;

            // Allow escape to cancel
            KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                // e.Location is client coords relative to the virtual-screen form
                start = e.Location;
                drawing = true;
            }
            else if (e.Button == MouseButtons.Right)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (drawing)
            {
                end = e.Location;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (drawing)
            {
                drawing = false;
                end = e.Location;

                // Convert client coordinates to ABSOLUTE screen coordinates by offsetting with the form's Bounds origin
                var absStart = new Point(start.X + Bounds.X, start.Y + Bounds.Y);
                var absEnd = new Point(end.X + Bounds.X, end.Y + Bounds.Y);

                SelectedRegion = new Rectangle(
                    Math.Min(absStart.X, absEnd.X),
                    Math.Min(absStart.Y, absEnd.Y),
                    Math.Abs(absStart.X - absEnd.X),
                    Math.Abs(absStart.Y - absEnd.Y)
                );

                DialogResult = DialogResult.OK;
                Close();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (drawing)
            {
                // draw using client coordinates (start/end are client coords)
                var rect = new Rectangle(
                    Math.Min(start.X, end.X),
                    Math.Min(start.Y, end.Y),
                    Math.Abs(start.X - end.X),
                    Math.Abs(start.Y - end.Y)
                );

                using (var pen = new Pen(Color.FromArgb(220, 0, 122, 204), 2))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }
            }
        }
    }
}
