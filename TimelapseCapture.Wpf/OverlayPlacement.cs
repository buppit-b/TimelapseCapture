using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// Places full-screen overlay windows in RAW physical pixels. WPF's Left/Top/Width/Height are
    /// DIPs interpreted through ONE monitor's DPI — on mixed-DPI desktops that leaves a spanning
    /// overlay short of (or past) the far monitor's edge. SetWindowPos speaks physical pixels, the
    /// same space the capture engine uses, so coverage is exact. (Same fix RegionOverlay uses.)
    /// </summary>
    internal static class OverlayPlacement
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        /// <summary>Stretch the window over the entire virtual screen, exactly. Call on SourceInitialized.</summary>
        public static void CoverVirtualScreen(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var vs = ScreenHelper.VirtualScreenBounds();
            SetWindowPos(hwnd, IntPtr.Zero, vs.X, vs.Y, vs.Width, vs.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }
}
