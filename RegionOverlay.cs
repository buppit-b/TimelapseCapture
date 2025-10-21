using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace TimelapseCapture
{
    /// <summary>
    /// Semi-transparent overlay that displays the currently selected capture region.
    /// Shows HUD-style border with corner brackets and region information.
    /// </summary>
    public class RegionOverlay : Form
    {
        private Rectangle _captureRegion;
        private string _regionInfo = string.Empty;
        private bool _isActiveCapture;
        private readonly Pen _borderPen;
        private readonly Pen _cornerPen;
        private readonly Font _infoFont;
        private readonly Brush _infoBrush;
        private readonly Brush _backgroundBrush;

        /// <summary>
        /// Gets or sets the capture region to display.
        /// </summary>
        /// 
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Rectangle CaptureRegion
        {
            get => _captureRegion;
            set
            {
                _captureRegion = value;
                UpdateRegionInfo();
                Invalidate();
            }
        }

        /// <summary>
        /// Gets or sets whether the session is actively capturing.
        /// Changes the border color (green=active, blue=inactive).
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsActiveCapture
        {
            get => _isActiveCapture;
            set
            {
                _isActiveCapture = value;
                UpdateBorderColor();
                Invalidate();
            }
        }

        public RegionOverlay()
        {
            // Form setup
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.0; // Start invisible, will fade in
            DoubleBuffered = true;

            // Make form click-through
            int initialStyle = GetWindowLong(Handle, -20);
            SetWindowLong(Handle, -20, initialStyle | 0x80000 | 0x20);

            // Initialize drawing resources
            _borderPen = new Pen(Color.FromArgb(0, 200, 100), 3); // Green
            _cornerPen = new Pen(Color.FromArgb(0, 200, 100), 4); // Thicker for corners
            _infoFont = new Font("Consolas", 11f, FontStyle.Bold);
            _infoBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
            _backgroundBrush = new SolidBrush(Color.FromArgb(180, 20, 20, 20)); // Semi-transparent dark

            Paint += RegionOverlay_Paint;
        }

        /// <summary>
        /// Shows the overlay with fade-in animation.
        /// </summary>
        public new void Show()
        {
            base.Show();
            FadeIn();
        }

        /// <summary>
        /// Hides the overlay with fade-out animation.
        /// </summary>
        public new void Hide()
        {
            FadeOut();
            base.Hide();
        }

        private void FadeIn()
        {
            Timer timer = new Timer { Interval = 20 };
            timer.Tick += (s, e) =>
            {
                if (Opacity < 1.0)
                {
                    Opacity += 0.1;
                }
                else
                {
                    timer.Stop();
                    timer.Dispose();
                }
            };
            timer.Start();
        }

        private void FadeOut()
        {
            Timer timer = new Timer { Interval = 20 };
            timer.Tick += (s, e) =>
            {
                if (Opacity > 0.0)
                {
                    Opacity -= 0.1;
                }
                else
                {
                    timer.Stop();
                    timer.Dispose();
                }
            };
            timer.Start();
        }

        private void UpdateBorderColor()
        {
            Color borderColor = _isActiveCapture
                ? Color.FromArgb(0, 200, 100)  // Green when active
                : Color.FromArgb(0, 122, 204); // Blue when inactive

            _borderPen.Color = borderColor;
            _cornerPen.Color = borderColor;
        }

        private void UpdateRegionInfo()
        {
            _regionInfo = $"{_captureRegion.Width} × {_captureRegion.Height}  •  ({_captureRegion.X}, {_captureRegion.Y})";
        }

        private void RegionOverlay_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Convert absolute region to form coordinates
            Rectangle localRegion = new Rectangle(
                _captureRegion.X - Left,
                _captureRegion.Y - Top,
                _captureRegion.Width,
                _captureRegion.Height
            );

            // Draw main border
            g.DrawRectangle(_borderPen, localRegion);

            // Draw corner brackets (HUD style)
            int bracketSize = 20;
            DrawCornerBracket(g, localRegion.Left, localRegion.Top, bracketSize, true, true);
            DrawCornerBracket(g, localRegion.Right, localRegion.Top, bracketSize, false, true);
            DrawCornerBracket(g, localRegion.Left, localRegion.Bottom, bracketSize, true, false);
            DrawCornerBracket(g, localRegion.Right, localRegion.Bottom, bracketSize, false, false);

            // Draw info box at top-left corner
            DrawInfoBox(g, localRegion.Left, localRegion.Top - 35);
        }

        private void DrawCornerBracket(Graphics g, int x, int y, int size, bool isLeft, bool isTop)
        {
            int xDir = isLeft ? 1 : -1;
            int yDir = isTop ? 1 : -1;

            // Horizontal line
            g.DrawLine(_cornerPen,
                x,
                y,
                x + (size * xDir),
                y);

            // Vertical line
            g.DrawLine(_cornerPen,
                x,
                y,
                x,
                y + (size * yDir));
        }

        private void DrawInfoBox(Graphics g, int x, int y)
        {
            // Measure text
            SizeF textSize = g.MeasureString(_regionInfo, _infoFont);
            int padding = 8;
            Rectangle boxRect = new Rectangle(
                x,
                y,
                (int)textSize.Width + (padding * 2),
                (int)textSize.Height + (padding * 2)
            );

            // Draw background
            g.FillRectangle(_backgroundBrush, boxRect);
            g.DrawRectangle(_borderPen, boxRect);

            // Draw text
            g.DrawString(_regionInfo, _infoFont, _infoBrush,
                boxRect.X + padding,
                boxRect.Y + padding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _borderPen?.Dispose();
                _cornerPen?.Dispose();
                _infoFont?.Dispose();
                _infoBrush?.Dispose();
                _backgroundBrush?.Dispose();
            }
            base.Dispose(disposing);
        }

        // P/Invoke for click-through functionality
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
