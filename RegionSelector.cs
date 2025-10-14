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
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
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
            if (drawing)
            {
                end = e.Location;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (drawing)
            {
                drawing = false;
                end = e.Location;
                SelectedRegion = new Rectangle(
                    Math.Min(start.X, end.X),
                    Math.Min(start.Y, end.Y),
                    Math.Abs(start.X - end.X),
                    Math.Abs(start.Y - end.Y));
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
                    Math.Abs(start.Y - end.Y));
                using (var pen = new Pen(Color.FromArgb(220, 0, 122, 204), 2))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }
            }
        }
    }
}
