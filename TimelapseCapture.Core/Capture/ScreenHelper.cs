using System.Drawing;
using System.Runtime.InteropServices;

namespace TimelapseCapture
{
    /// <summary>
    /// Screen geometry in physical pixels (via Win32), independent of any UI framework's
    /// DPI-scaled coordinate system. Callers must be per-monitor DPI aware for these to be correct.
    /// </summary>
    public static class ScreenHelper
    {
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        /// <summary>Primary monitor bounds in physical pixels (origin 0,0).</summary>
        public static Rectangle PrimaryScreenBounds()
            => new Rectangle(0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));

        /// <summary>Bounding rectangle of all monitors in physical pixels (may have negative origin).</summary>
        public static Rectangle VirtualScreenBounds()
            => new Rectangle(
                GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
                GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));

        [DllImport("user32.dll")] private static extern uint GetDpiForSystem();

        /// <summary>
        /// System DPI scale factor (1.0 = 100%, 1.5 = 150%). Used to map WPF device-independent
        /// units to the physical pixels that screen capture works in. Correct for single-monitor
        /// or uniform-DPI setups; mixed per-monitor DPI may need a per-monitor refinement.
        /// </summary>
        public static double SystemDpiScale()
        {
            try { return GetDpiForSystem() / 96.0; }
            catch { return 1.0; }
        }
    }
}
