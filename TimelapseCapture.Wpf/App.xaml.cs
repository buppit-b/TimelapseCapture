using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using TimelapseCapture;

namespace TimelapseCapture.Wpf
{
    public partial class App : Application
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string windowTitle);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
        private const int SW_RESTORE = 9;

        private static Mutex? _instanceMutex;   // held for the app's lifetime (field so it can't be GC'd)

        protected override void OnStartup(StartupEventArgs e)
        {
            // Single instance: two copies would race on settings.json (each holds an in-memory snapshot
            // and rewrites the whole file on any change — last writer silently reverts the other's
            // choices, e.g. a newly picked output folder) and could double-capture into one session.
            _instanceMutex = new Mutex(initiallyOwned: true, @"Local\TimelapseCapture.Wpf.SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                IntPtr existing = FindWindow(null, "Timelapse Capture");
                if (existing != IntPtr.Zero)
                {
                    ShowWindow(existing, SW_RESTORE);
                    SetForegroundWindow(existing);
                }
                Shutdown();
                return;
            }

            // Apply the saved theme before the first window renders (resources already exist here).
            try { ThemeManager.Apply(SettingsManager.Load().Theme); } catch { }
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _instanceMutex?.ReleaseMutex(); } catch { /* second-instance path never owned it */ }
            _instanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
