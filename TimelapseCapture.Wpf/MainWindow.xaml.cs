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
        private const uint MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004;
        private const uint VK_F9 = 0x78;
        private const int HOTKEY_TOGGLE = 1; // Ctrl+Shift+F9 → start/stop capture

        private HwndSource? _source;

        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var handle = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(handle);
            _source?.AddHook(WndProc);
            // Best-effort: if the combo is already taken by another app, it just won't register.
            RegisterHotKey(handle, HOTKEY_TOGGLE, MOD_CONTROL | MOD_SHIFT, VK_F9);
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
            UnregisterHotKey(handle, HOTKEY_TOGGLE);
            _source?.RemoveHook(WndProc);
            (DataContext as MainViewModel)?.OnAppClosing();
        }
    }
}
