using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FrameWrite.Wpf
{
    /// <summary>
    /// Central "hide the app from screen capture" control (Win32 <c>WDA_EXCLUDEFROMCAPTURE</c>).
    ///
    /// Two rules, both learned the hard way:
    ///  1. **Every app window** is covered, not just the main one — a class handler on
    ///     <see cref="FrameworkElement.LoadedEvent"/> catches dialogs, the frame viewer and overlays
    ///     automatically, so a stray dialog can't leak into the timelapse while the main window hides.
    ///  2. **Only while capturing.** The exclusion exists so FrameWrite doesn't appear in its own
    ///     frames — the only moment that matters. Applied 24/7 it also made the user's OWN
    ///     screenshots / screen-shares come out black (an excluded window renders normally to the eye
    ///     but is invisible to any capture API), which reads as a "black screen" bug. Idle ⇒ the
    ///     windows are normally capturable again.
    ///
    /// <see cref="ShouldHide"/> is the single source of truth (set by MainWindow to
    /// <c>HideFromCapture &amp;&amp; IsCapturing</c>); <see cref="ApplyAll"/> re-syncs every open window
    /// whenever that changes.
    /// </summary>
    public static class WindowCapture
    {
        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
        [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint dwAffinity);
        private const uint WDA_NONE = 0x0, WDA_EXCLUDEFROMCAPTURE = 0x11;

        /// <summary>True ⇒ hide from capture right now. Defaults to never; MainWindow wires it to the VM.</summary>
        public static Func<bool> ShouldHide { get; set; } = () => false;

        private static bool _hooked;

        /// <summary>Register the app-wide Window.Loaded handler (idempotent) — call once at startup,
        /// before the first window loads, so every window that ever opens gets the current setting.</summary>
        public static void Init()
        {
            if (_hooked) return;
            _hooked = true;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, _) => Apply((Window)s)));
        }

        /// <summary>Apply the current desired affinity to one window (no-op until its HWND exists).</summary>
        public static void Apply(Window w)
        {
            try
            {
                var h = new WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) return;
                uint want = ShouldHide() ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE;
                // Only poke the OS on a real change (the getter is cheap) — avoids needless churn.
                if (GetWindowDisplayAffinity(h, out uint cur) && cur == want) return;
                SetWindowDisplayAffinity(h, want);
            }
            catch { /* affinity is best-effort — never let it break a window */ }
        }

        /// <summary>Re-apply to every open app window (on capture start/stop or a setting toggle), then
        /// once more deferred: a window created moments earlier (auto-start-on-launch begins capturing
        /// before the main window is fully composed) may not take the exclusion on the first poke.</summary>
        public static void ApplyAll()
        {
            var app = Application.Current;
            if (app == null) return;
            foreach (Window w in app.Windows) Apply(w);
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (Window w in app.Windows) Apply(w);
            }), DispatcherPriority.Background);
        }
    }
}
