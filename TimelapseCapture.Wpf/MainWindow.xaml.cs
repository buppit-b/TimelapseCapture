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
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_TOGGLE = 1; // start/stop capture (configurable, off by default)
        private const uint WDA_NONE = 0x0, WDA_EXCLUDEFROMCAPTURE = 0x11; // hide window from screen capture

        private HwndSource? _source;
        private bool _hotkeyRegistered;

        public MainWindow()
        {
            InitializeComponent();
        }

        // Apply the capture-length target when the field loses focus (tab away / click out),
        // mirroring the Enter key binding — so there's no separate "Set" button.
        private void OnTargetCommit(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.SetTargetCommand.CanExecute(null))
                vm.SetTargetCommand.Execute(null);
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
            }
            RefreshHotkey();
            ApplyAffinity();
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
            return IntPtr.Zero;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            var handle = new WindowInteropHelper(this).Handle;
            if (_hotkeyRegistered) UnregisterHotKey(handle, HOTKEY_TOGGLE);
            if (DataContext is MainViewModel vm) { vm.HotkeysChanged -= RefreshHotkey; vm.WindowAffinityChanged -= ApplyAffinity; }
            _source?.RemoveHook(WndProc);
            (DataContext as MainViewModel)?.OnAppClosing();
        }
    }
}
