using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TimelapseCapture
{
    internal static class Program
    {
        [DllImport("SHCore.dll", SetLastError = true)]
        private static extern int SetProcessDpiAwareness(int dpiAwareness);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();

        private const int PROCESS_PER_MONITOR_DPI_AWARE = 2;

        [STAThread]
        static void Main()
        {
            // Enable per-monitor DPI awareness for proper multi-monitor support
            // This ensures coordinates are not scaled by Windows
            try
            {
                // Try modern API first (Windows 8.1+)
                SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE);
            }
            catch
            {
                // Fallback to older API (Windows Vista+)
                try
                {
                    SetProcessDPIAware();
                }
                catch
                {
                    // If both fail, continue anyway - app will work but may have DPI issues
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
