using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TimelapseCapture.Wpf.ViewModels;

namespace TimelapseCapture.Wpf
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);
        [StructLayout(LayoutKind.Sequential)] private struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }
        private const uint FLASHW_ALL = 3, FLASHW_TIMERNOFG = 12; // flash caption+taskbar until the window is focused
        private const int WM_HOTKEY = 0x0312;
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int HOTKEY_TOGGLE = 1; // start/stop capture (configurable, off by default)
        private const uint WDA_NONE = 0x0, WDA_EXCLUDEFROMCAPTURE = 0x11; // hide window from screen capture
        private const int MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)] private struct POINTL { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }
        [StructLayout(LayoutKind.Sequential)] private struct MINMAXINFO { public POINTL ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }

        private HwndSource? _source;
        private bool _hotkeyRegistered;
        private TrayIcon? _tray;

        public MainWindow()
        {
            InitializeComponent();
        }

        // First launch: show the guided setup once the window is actually visible. Marked completed
        // immediately so a mid-wizard close can't make it nag on every launch (it stays available
        // from Settings → "Setup wizard…"). Afterwards, honor a session path passed on the command
        // line (e.g. a session folder dragged onto the exe in Explorer).
        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            if (DataContext is not MainViewModel vm) return;
            // Honor a command-line session FIRST: with it loaded, a first-run wizard operates on the real
            // session instead of silently discarding one it auto-created (its EnsureDefaultSession no-ops).
            if (App.PendingSessionPath is { } pending)
            {
                App.PendingSessionPath = null;
                if (!vm.TryLoadSessionPath(pending))
                    MessageDialog.Show($"No session found at:\n{pending}\n\n(A session folder contains a session.json.)",
                        "Open session", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            if (!vm.FirstRunCompleted)
            {
                vm.FirstRunCompleted = true;
                vm.OpenWizard();
            }
        }

        // Drag a session folder (or any file inside one) onto the window to load that session.
        private void OnWindowDrop(object sender, DragEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (vm.IsCapturing || vm.IsEncoding)
            {
                MessageDialog.Show("Finish (or cancel) the current capture/encode before loading another session.",
                    "Open session", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0 &&
                !vm.TryLoadSessionPath(paths[0]))
            {
                MessageDialog.Show("That doesn't look like a session — drop a session folder (it contains a session.json), or any file inside one.",
                    "Open session", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // Apply the capture-length target when the field loses focus (tab away / click out),
        // mirroring the Enter key binding — so there's no separate "Set" button.
        // Enter in a target box commits it in place (wheel and tab-away commit on their own).
        private void OnTargetEnter(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && sender is System.Windows.Controls.TextBox tb)
            {
                tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
                e.Handled = true;
            }
        }

        // Unfold the ffmpeg Download/Browse row (it stays slim while ffmpeg is Ready).
        private void OnFfmpegChange(object sender, RoutedEventArgs e)
            => (DataContext as MainViewModel)?.ExpandFfmpegSetup();

        // The preview thumbnail opens the loupe: a floating zoom/scrub viewer over the session's frames.
        private void OnPreviewClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.CurrentSessionFolder is { } folder && vm.FrameCount > 0)
                new FrameViewerWindow(folder) { Owner = this }.Show();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WndProc);
            if (DataContext is MainViewModel vm)
            {
                vm.HotkeysChanged += RefreshHotkey;
                vm.WindowAffinityChanged += ApplyAffinity;
                vm.FinishNotified += OnFinishNotified;
                try { _tray = new TrayIcon(this, vm); }   // system-tray presence + recording status
                catch (Exception ex) { TimelapseCapture.Logger.Log("Wpf", $"Tray icon unavailable: {ex.Message}"); }
            }
            RefreshHotkey();
            ApplyAffinity();
        }

        // A capture auto-stopped or an encode finished — chime and flash the taskbar (draws attention if
        // the window is in the background; the flash stops once you focus it).
        private void OnFinishNotified()
        {
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
            if (!IsVisible) _tray?.ShowBalloon("Capture finished.");   // in the tray — surface it there
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            var fi = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = h,
                dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
                uCount = uint.MaxValue,
                dwTimeout = 0,
            };
            try { FlashWindowEx(ref fi); } catch { }
        }

        // Hide (or show) this window in screen captures per the setting.
        private void ApplyAffinity()
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            bool hide = (DataContext as MainViewModel)?.HideFromCapture ?? false;
            try { SetWindowDisplayAffinity(h, hide ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE); } catch { }
        }

        // Register (or unregister) the global hotkey to match the current settings.
        private void RefreshHotkey()
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            if (_hotkeyRegistered) { UnregisterHotKey(handle, HOTKEY_TOGGLE); _hotkeyRegistered = false; }

            if (DataContext is MainViewModel vm && vm.HotkeysEnabled)
                _hotkeyRegistered = RegisterHotKey(handle, HOTKEY_TOGGLE, (uint)vm.HotkeyModifiers, (uint)vm.HotkeyVk);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_TOGGLE)
            {
                (DataContext as MainViewModel)?.ToggleCaptureHotkey();
                handled = true;
            }
            else if (msg == WM_GETMINMAXINFO)
            {
                // Borderless (WindowStyle=None) maximize would cover the taskbar — clamp the maximized
                // size/position to the monitor's WORK area instead.
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                IntPtr mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (mon != IntPtr.Zero)
                {
                    var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                    if (GetMonitorInfo(mon, ref mi))
                    {
                        mmi.ptMaxPosition.X = mi.rcWork.Left - mi.rcMonitor.Left;
                        mmi.ptMaxPosition.Y = mi.rcWork.Top - mi.rcMonitor.Top;
                        mmi.ptMaxSize.X = mi.rcWork.Right - mi.rcWork.Left;
                        mmi.ptMaxSize.Y = mi.rcWork.Bottom - mi.rcWork.Top;
                        Marshal.StructureToPtr(mmi, lParam, true);
                    }
                }
            }
            return IntPtr.Zero;
        }

        private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void OnMaxRestore(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void OnClose(object sender, RoutedEventArgs e) => Close();

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            // Swap the caption glyph between Maximize (E922) and Restore (E923).
            if (FindName("maxButton") is System.Windows.Controls.Button b)
                b.Content = WindowState == WindowState.Maximized ? "" : "";
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            // Close-to-tray (opt-in): the X hides to the tray instead of exiting. Bypassed when the user
            // picked "Exit" from the tray menu (ForceExit). Requires a live tray icon — never trap the
            // user with no window AND no tray. Capture keeps running while hidden.
            if (_tray is { ForceExit: false } && DataContext is MainViewModel vm2 && vm2.CloseToTray)
            {
                e.Cancel = true;
                Hide();   // Hide() removes the taskbar button too; do NOT touch ShowInTaskbar (HWND-recreation).
                return;
            }

            var handle = new WindowInteropHelper(this).Handle;
            if (_hotkeyRegistered) UnregisterHotKey(handle, HOTKEY_TOGGLE);
            if (DataContext is MainViewModel vm) { vm.HotkeysChanged -= RefreshHotkey; vm.WindowAffinityChanged -= ApplyAffinity; vm.FinishNotified -= OnFinishNotified; }
            _source?.RemoveHook(WndProc);
            _tray?.Dispose();   // remove the tray icon so it doesn't linger after exit
            (DataContext as MainViewModel)?.OnAppClosing();
        }
    }
}
