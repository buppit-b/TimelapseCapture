using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using FrameWrite;

namespace FrameWrite.Wpf
{
    public partial class App : Application
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string windowTitle);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
        private const int SW_RESTORE = 9;

        private static Mutex? _instanceMutex;   // held for the app's lifetime (field so it can't be GC'd)

        /// <summary>
        /// A session path passed on the command line (e.g. a session folder dragged onto the exe in
        /// Explorer) — consumed by MainWindow once it's rendered. Note: if the app is ALREADY running,
        /// the single-instance guard activates it and this arg is dropped (arg forwarding is a
        /// follow-up); drag-and-drop onto the window covers that case.
        /// </summary>
        public static string? PendingSessionPath { get; set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Length > 0 && !string.IsNullOrWhiteSpace(e.Args[0]))
                PendingSessionPath = e.Args[0];

            // Single instance: two copies would race on settings.json (each holds an in-memory snapshot
            // and rewrites the whole file on any change — last writer silently reverts the other's
            // choices, e.g. a newly picked output folder) and could double-capture into one session.
            // SCAFFOLDING (ui-elegance fork, do not merge): distinct mutex + title so this preview can run
            // ALONGSIDE the shipped 1.0 build for side-by-side comparison. Revert to "FrameWrite.Wpf..." / "FrameWrite" at merge.
            _instanceMutex = new Mutex(initiallyOwned: true, @"Local\FrameWrite.UiPreview.SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                IntPtr existing = FindWindow(null, "FrameWrite — UI Preview");
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
