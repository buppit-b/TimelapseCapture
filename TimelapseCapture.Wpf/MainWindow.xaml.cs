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
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_TOGGLE = 1; // start/stop capture (configurable, off by default)

        private HwndSource? _source;
        private bool _hotkeyRegistered;

        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WndProc);
            if (DataContext is MainViewModel vm)
                vm.HotkeysChanged += RefreshHotkey;
            RefreshHotkey();
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
            if (DataContext is MainViewModel vm) vm.HotkeysChanged -= RefreshHotkey;
            _source?.RemoveHook(WndProc);
            (DataContext as MainViewModel)?.OnAppClosing();
        }
    }
}
