using System;
using System.Drawing;
using System.Windows.Forms;

namespace TimelapseCapture
{
    /// <summary>
    /// Interactive region selector that ensures FFmpeg-compatible dimensions.
    /// Contract: Always returns even-numbered dimensions >= 2x2.
    /// </summary>
    public class RegionSelector : Form
    {
        private Point _start;
        private Point _end;
        private bool _drawing = false;
        private readonly Font _overlayFont = new Font("Segoe UI", 11, FontStyle.Bold);
        private readonly Font _helpFont = new Font("Segoe UI", 9, FontStyle.Regular);

        public Rectangle SelectedRegion { get; private set; }

        public RegionSelector()
        {
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            DoubleBuffered = true;
            BackColor = Color.Black;
            Opacity = 0.35;
            Cursor = Cursors.Cross;
            TopMost = true;
            KeyPreview = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _start = e.Location;
                _drawing = true;
            }
            else if (e.Button == MouseButtons.Right)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drawing)
            {
                _end = e.Location;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_drawing && e.Button == MouseButtons.Left)
            {
                _drawing = false;
                _end = e.Location;

                SelectedRegion = NormalizeRegion(_start, _end);

                // Only accept if region is meaningful (not just a click)
                if (SelectedRegion.Width >= 20 && SelectedRegion.Height >= 20)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    // Too small - let them try again
                    _drawing = false;
                    Invalidate();
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        /// <summary>
        /// Normalize a selection to FFmpeg-compatible dimensions.
        /// Ensures: even width/height, minimum 2x2, within screen bounds.
        /// </summary>
        private Rectangle NormalizeRegion(Point start, Point end)
        {
            var screen = Screen.FromPoint(start).Bounds;

            int x = Math.Max(0, Math.Min(start.X, end.X));
            int y = Math.Max(0, Math.Min(start.Y, end.Y));
            int width = Math.Abs(start.X - end.X);
            int height = Math.Abs(start.Y - end.Y);

            // Ensure minimum size
            width = Math.Max(2, width);
            height = Math.Max(2, height);

            // Snap to even numbers (required by libx264)
            width = (width >> 1) << 1;  // Equivalent to: (width / 2) * 2
            height = (height >> 1) << 1;

            // Clamp to screen bounds
            if (x + width > screen.Width)
                width = Math.Max(2, ((screen.Width - x) >> 1) << 1);
            if (y + height > screen.Height)
                height = Math.Max(2, ((screen.Height - y) >> 1) << 1);

            return new Rectangle(x, y, width, height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Draw help text at top
            DrawHelpText(e.Graphics);

            if (_drawing)
            {
                var rect = NormalizeRegion(_start, _end);

                // Draw selection rectangle with rounded corners effect
                using (var pen = new Pen(Color.FromArgb(255, 0, 122, 204), 3))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }

                // Draw semi-transparent fill
                using (var brush = new SolidBrush(Color.FromArgb(30, 0, 122, 204)))
                {
                    e.Graphics.FillRectangle(brush, rect);
                }

                // Draw dimension overlay
                DrawDimensionOverlay(e.Graphics, rect);
            }
        }

        private void DrawHelpText(Graphics g)
        {
            string help = "Left-click and drag to select region  •  Right-click or ESC to cancel";
            var helpSize = g.MeasureString(help, _helpFont);
            var helpRect = new RectangleF(
                (Width - helpSize.Width) / 2 - 10,
                20,
                helpSize.Width + 20,
                helpSize.Height + 10
            );

            using (var brush = new SolidBrush(Color.FromArgb(200, 30, 30, 30)))
            using (var textBrush = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
            {
                g.FillRoundedRectangle(brush, helpRect, 6);
                g.DrawString(help, _helpFont, textBrush,
                    helpRect.X + 10, helpRect.Y + 5);
            }
        }

        private void DrawDimensionOverlay(Graphics g, Rectangle rect)
        {
            string dims = $"{rect.Width} × {rect.Height}";
            var textSize = g.MeasureString(dims, _overlayFont);

            // Position above or below the selection based on available space
            int overlayY = rect.Y > textSize.Height + 20
                ? rect.Y - (int)textSize.Height - 12
                : rect.Bottom + 8;

            var overlayRect = new RectangleF(
                rect.X + 8,
                overlayY,
                textSize.Width + 16,
                textSize.Height + 8
            );

            using (var brush = new SolidBrush(Color.FromArgb(220, 0, 122, 204)))
            using (var textBrush = new SolidBrush(Color.White))
            {
                g.FillRoundedRectangle(brush, overlayRect, 4);

                var textRect = new RectangleF(
                    overlayRect.X,
                    overlayRect.Y,
                    overlayRect.Width,
                    overlayRect.Height
                );

                using (var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    g.DrawString(dims, _overlayFont, textBrush, textRect, sf);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _overlayFont?.Dispose();
                _helpFont?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // Extension method for rounded rectangles
    internal static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, RectangleF rect, float radius)
        {
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
                path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
                path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
                path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }
    }
}