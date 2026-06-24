using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TimelapseCapture; // Core: ScreenHelper

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// A non-interactive, click-through outline that marks the capture region on screen. The 2px
    /// border sits in the ring just outside the region so it is never captured into the frames.
    /// </summary>
    public partial class RegionOverlay : Window
    {
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;   // mouse passes through
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TOOLWINDOW = 0x80;     // keep out of Alt-Tab

        private const double Border = 2; // device-independent units; matches the XAML BorderThickness

        public RegionOverlay()
        {
            InitializeComponent();
        }

        /// <summary>Position the outline around <paramref name="region"/> (physical pixels) and show it.</summary>
        public void ShowForRegion(System.Drawing.Rectangle region)
        {
            double scale = ScreenHelper.SystemDpiScale();
            double rx = region.X / scale, ry = region.Y / scale;
            double rw = region.Width / scale, rh = region.Height / scale;

            Left = rx - Border;
            Top = ry - Border;
            Width = rw + Border * 2;
            Height = rh + Border * 2;

            if (!IsVisible) Show();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
        }
    }
}
