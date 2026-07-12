using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using FrameWrite; // Core: ScreenHelper

namespace FrameWrite.Wpf
{
    /// <summary>
    /// A non-interactive, click-through outline that marks the capture region on screen. The 2px
    /// border sits in the ring just outside the region so it is never captured into the frames.
    /// Positioned via SetWindowPos in RAW PHYSICAL pixels: WPF Left/Top are DIPs converted with a
    /// single DPI scale, which misplaced the outline on a differently-scaled second monitor
    /// (PerMonitorV2; sighted live on Spike's art-tablet setup).
    /// </summary>
    public partial class RegionOverlay : Window
    {
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;   // mouse passes through
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TOOLWINDOW = 0x80;     // keep out of Alt-Tab
        private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private const double Border = 2;       // device-independent units; matches the XAML BorderThickness
        private const double LabelHeight = 20; // top strip that holds the dimension readout (above the region)

        public RegionOverlay()
        {
            InitializeComponent();
        }

        /// <summary>Position the outline around <paramref name="region"/> (physical pixels) and show it.</summary>
        public void ShowForRegion(System.Drawing.Rectangle region)
        {
            dimLabel.Text = $"{region.Width}×{region.Height}";

            if (!IsVisible)
            {
                // Pre-show at the DIP-converted spot (exact on the primary monitor) so the first
                // appearance doesn't flash at a default location, then correct precisely below.
                double scale = ScreenHelper.SystemDpiScale();
                Left = region.X / scale - Border;
                Top = region.Y / scale - Border - LabelHeight;
                Width = region.Width / scale + Border * 2;
                Height = region.Height / scale + Border * 2 + LabelHeight;
                Show();
            }

            PositionPhysical(region);
            // PerMonitorV2 can rescale/resize the window AFTER a cross-DPI move (WM_DPICHANGED) —
            // re-assert once when that dust settles. (The tracked-window path re-calls this every
            // 40ms anyway; this covers the static one-shot case.)
            var r = region;
            Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) PositionPhysical(r); }),
                DispatcherPriority.Loaded);
        }

        // The ring/label margins are DIP constants, so size them with the TARGET monitor's own scale
        // and place the window in raw physical pixels — identical result on every monitor.
        private void PositionPhysical(System.Drawing.Rectangle region)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;   // pre-handle: the DIP pre-show already positioned us
            double monScale = MonitorScaleAt(region);
            int ring = (int)Math.Ceiling(Border * monScale);
            int label = (int)Math.Ceiling(LabelHeight * monScale);
            SetWindowPos(hwnd, IntPtr.Zero,
                region.X - ring, region.Y - ring - label,
                region.Width + ring * 2, region.Height + ring * 2 + label,
                SWP_NOZORDER | SWP_NOACTIVATE);
        }

        private static double MonitorScaleAt(System.Drawing.Rectangle region)
        {
            try
            {
                var pt = new POINT { X = region.X + region.Width / 2, Y = region.Y + region.Height / 2 };
                var mon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
                if (mon != IntPtr.Zero && GetDpiForMonitor(mon, 0 /* MDT_EFFECTIVE_DPI */, out uint dx, out _) == 0)
                    return dx / 96.0;
            }
            catch { /* shcore always present on Win8.1+; belt-and-braces */ }
            return ScreenHelper.SystemDpiScale();
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
