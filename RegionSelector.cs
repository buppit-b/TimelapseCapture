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

                // Convert client coordinates to ABSOLUTE screen coordinates
                var absStart = new Point(start.X + Bounds.X, start.Y + Bounds.Y);
                var absEnd = new Point(end.X + Bounds.X, end.Y + Bounds.Y);

                int x = Math.Min(absStart.X, absEnd.X);
                int y = Math.Min(absStart.Y, absEnd.Y);
                int width = Math.Abs(absStart.X - absEnd.X);
                int height = Math.Abs(absStart.Y - absEnd.Y);

                // CRITICAL FIX: Ensure dimensions are even for video encoding compatibility
                // If width is odd, round down to make it even
                if ((width & 1) == 1) width = Math.Max(2, width - 1);
                // If height is odd, round down to make it even
                if ((height & 1) == 1) height = Math.Max(2, height - 1);

                // Ensure minimum size
                if (width < 2) width = 2;
                if (height < 2) height = 2;

                SelectedRegion = new Rectangle(x, y, width, height);

                DialogResult = DialogResult.OK;
                Close();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (drawing)
            {
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

                // Draw dimension text
                string dimensions = $"{rect.Width} × {rect.Height}";
                using (var brush = new SolidBrush(Color.White))
                using (var font = new Font("Segoe UI", 12, FontStyle.Bold))
                {
                    var textSize = e.Graphics.MeasureString(dimensions, font);
                    var textPos = new PointF(
                        rect.X + (rect.Width - textSize.Width) / 2,
                        rect.Y + (rect.Height - textSize.Height) / 2
                    );

                    // Draw background for text
                    var textRect = new RectangleF(textPos.X - 5, textPos.Y - 2, textSize.Width + 10, textSize.Height + 4);
                    using (var bgBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                    {
                        e.Graphics.FillRectangle(bgBrush, textRect);
                    }

                    e.Graphics.DrawString(dimensions, font, brush, textPos);
                }
            }
        }
    }
}