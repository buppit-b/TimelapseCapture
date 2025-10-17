using System;
using System.Drawing;
using System.Windows.Forms;

namespace TimelapseCapture
{
    /// <summary>
    /// Full-screen overlay for selecting a screen region with optional aspect ratio locking.
    /// </summary>
    public class RegionSelector : Form
    {
        private Point start;
        private Point end;
        private bool drawing = false;
        private AspectRatio? _lockedRatio;

        /// <summary>
        /// Selected region in absolute screen coordinates.
        /// </summary>
        public Rectangle SelectedRegion { get; private set; }

        /// <summary>
        /// Create a new region selector.
        /// </summary>
        /// <param name="aspectRatio">Optional aspect ratio to lock selection to</param>
        public RegionSelector(AspectRatio? aspectRatio = null)
        {
            _lockedRatio = aspectRatio;

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

                // Apply aspect ratio constraint if locked
                if (_lockedRatio != null && _lockedRatio.Width > 0)
                {
                    // Calculate raw rectangle from drag
                    var rawRect = new Rectangle(
                        Math.Min(start.X, end.X),
                        Math.Min(start.Y, end.Y),
                        Math.Abs(start.X - end.X),
                        Math.Abs(start.Y - end.Y)
                    );

                    // Constrain to aspect ratio
                    var constrained = AspectRatio.ConstrainToRatio(
                        rawRect,
                        _lockedRatio.Width,
                        _lockedRatio.Height
                    );

                    // Update end point to match constrained rectangle
                    // Adjust based on drag direction
                    if (end.X >= start.X && end.Y >= start.Y)
                    {
                        // Bottom-right drag
                        end = new Point(start.X + constrained.Width, start.Y + constrained.Height);
                    }
                    else if (end.X < start.X && end.Y >= start.Y)
                    {
                        // Bottom-left drag
                        end = new Point(start.X - constrained.Width, start.Y + constrained.Height);
                    }
                    else if (end.X >= start.X && end.Y < start.Y)
                    {
                        // Top-right drag
                        end = new Point(start.X + constrained.Width, start.Y - constrained.Height);
                    }
                    else
                    {
                        // Top-left drag
                        end = new Point(start.X - constrained.Width, start.Y - constrained.Height);
                    }
                }

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

                var region = new Rectangle(x, y, width, height);

                // Apply aspect ratio constraint one final time
                if (_lockedRatio != null && _lockedRatio.Width > 0)
                {
                    region = AspectRatio.ConstrainToRatio(
                        region,
                        _lockedRatio.Width,
                        _lockedRatio.Height
                    );
                }
                else
                {
                    // No aspect ratio lock - just ensure even dimensions
                    region = AspectRatio.EnsureEvenDimensions(region);
                }

                // Ensure minimum size
                if (region.Width < 2) region.Width = 2;
                if (region.Height < 2) region.Height = 2;

                SelectedRegion = region;

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

                // Draw selection rectangle
                using (var pen = new Pen(Color.FromArgb(220, 0, 122, 204), 2))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }

                // Draw corner brackets (inspired by targeting systems)
                DrawCornerBrackets(e.Graphics, rect);

                // Draw dimension text with aspect ratio info
                string dimensions = $"{rect.Width} × {rect.Height}";
                if (_lockedRatio != null && _lockedRatio.Width > 0)
                {
                    dimensions += $" ({_lockedRatio.Width}:{_lockedRatio.Height})";
                }
                else
                {
                    // Show calculated ratio for free mode
                    string ratio = AspectRatio.CalculateRatioString(rect.Width, rect.Height);
                    dimensions += $" ({ratio})";
                }

                using (var brush = new SolidBrush(Color.White))
                using (var font = new Font("Consolas", 11, FontStyle.Bold))
                {
                    var textSize = e.Graphics.MeasureString(dimensions, font);
                    var textPos = new PointF(
                        rect.X + (rect.Width - textSize.Width) / 2,
                        rect.Y + (rect.Height - textSize.Height) / 2
                    );

                    // Draw background for text
                    var textRect = new RectangleF(
                        textPos.X - 8,
                        textPos.Y - 4,
                        textSize.Width + 16,
                        textSize.Height + 8
                    );
                    using (var bgBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                    {
                        e.Graphics.FillRectangle(bgBrush, textRect);
                    }

                    // Draw border around text
                    using (var borderPen = new Pen(Color.FromArgb(150, 0, 122, 204), 1))
                    {
                        e.Graphics.DrawRectangle(borderPen,
                            textRect.X, textRect.Y, textRect.Width, textRect.Height);
                    }

                    e.Graphics.DrawString(dimensions, font, brush, textPos);
                }
            }
        }

        /// <summary>
        /// Draw corner brackets around the selection (aerospace HUD style).
        /// </summary>
        private void DrawCornerBrackets(Graphics g, Rectangle rect)
        {
            int bracketSize = 20;
            using (var pen = new Pen(Color.FromArgb(200, 0, 200, 255), 2))
            {
                // Top-left
                g.DrawLine(pen, rect.Left, rect.Top, rect.Left + bracketSize, rect.Top);
                g.DrawLine(pen, rect.Left, rect.Top, rect.Left, rect.Top + bracketSize);

                // Top-right
                g.DrawLine(pen, rect.Right, rect.Top, rect.Right - bracketSize, rect.Top);
                g.DrawLine(pen, rect.Right, rect.Top, rect.Right, rect.Top + bracketSize);

                // Bottom-left
                g.DrawLine(pen, rect.Left, rect.Bottom, rect.Left + bracketSize, rect.Bottom);
                g.DrawLine(pen, rect.Left, rect.Bottom, rect.Left, rect.Bottom - bracketSize);

                // Bottom-right
                g.DrawLine(pen, rect.Right, rect.Bottom, rect.Right - bracketSize, rect.Bottom);
                g.DrawLine(pen, rect.Right, rect.Bottom, rect.Right, rect.Bottom - bracketSize);
            }
        }
    }
}