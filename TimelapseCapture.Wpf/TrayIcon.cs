using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using WinForms = System.Windows.Forms;
using TimelapseCapture.Wpf.ViewModels;

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// System-tray presence for the app: a NotifyIcon that shows recording status at a glance (a green
    /// dot when idle, a pulsing-red-style filled dot + count while recording), restores the window on
    /// double-click, and offers Show / Start-Stop / Exit from its menu. When "Minimize to tray" is on,
    /// minimizing hides the window from the taskbar into the tray.
    ///
    /// Uses System.Windows.Forms.NotifyIcon (in-SDK, no external dependency), aliased to avoid the
    /// Application/MessageBox name clash with WPF.
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);

        private readonly Window _window;
        private readonly MainViewModel _vm;
        private readonly WinForms.NotifyIcon _icon;
        private readonly WinForms.ToolStripMenuItem _showItem;
        private readonly WinForms.ToolStripMenuItem _toggleItem;
        private Icon? _idleIcon, _recIcon;
        private bool _disposed;

        /// <summary>True once the user picked "Exit" from the tray menu — so close-to-tray is bypassed.</summary>
        public bool ForceExit { get; private set; }

        public TrayIcon(Window window, MainViewModel vm)
        {
            _window = window;
            _vm = vm;

            _idleIcon = MakeDotIcon(Color.FromArgb(0x3F, 0xB9, 0x50), recording: false); // green
            _recIcon = MakeDotIcon(Color.FromArgb(0xE5, 0x53, 0x4B), recording: true);   // red ring

            var menu = new WinForms.ContextMenuStrip();
            _showItem = new WinForms.ToolStripMenuItem("Show Framewright", null, (_, _) => RestoreWindow());
            _toggleItem = new WinForms.ToolStripMenuItem("Start capture", null, (_, _) => ToggleCapture());
            var exitItem = new WinForms.ToolStripMenuItem("Exit", null, (_, _) => ExitApp());
            menu.Items.Add(_showItem);
            menu.Items.Add(_toggleItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            _icon = new WinForms.NotifyIcon
            {
                Icon = _idleIcon,
                Text = "Framewright",
                Visible = true,
                ContextMenuStrip = menu,
            };
            _icon.DoubleClick += (_, _) => RestoreWindow();

            _vm.PropertyChanged += OnVmChanged;
            _window.StateChanged += OnWindowStateChanged;
            UpdateStatus();
        }

        private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MainViewModel.IsCapturing) or nameof(MainViewModel.FrameCount) or null)
                UpdateStatus();
        }

        // Reflect recording state in the icon, tooltip, and the menu's Start/Stop label.
        private void UpdateStatus()
        {
            bool rec = _vm.IsCapturing;
            _icon.Icon = rec ? _recIcon : _idleIcon;
            // NotifyIcon.Text is capped at 63 chars.
            _icon.Text = rec ? $"Framewright — Recording ({_vm.FrameCount} frames)" : "Framewright — Idle";
            _toggleItem.Text = rec ? "Stop capture" : "Start capture";
        }

        private void ToggleCapture() => _window.Dispatcher.Invoke(() => _vm.ToggleCaptureHotkey());

        /// <summary>Show a tray balloon (used when a capture finishes while the app is hidden in the tray).</summary>
        public void ShowBalloon(string message)
        {
            try
            {
                _icon.BalloonTipTitle = "Framewright";
                _icon.BalloonTipText = message;
                _icon.ShowBalloonTip(4000);
            }
            catch { /* balloons are best-effort */ }
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (_window.WindowState == WindowState.Minimized && _vm.MinimizeToTray)
            {
                _window.Hide();               // remove from the taskbar; the tray icon is the way back
                _window.ShowInTaskbar = false;
            }
        }

        private void RestoreWindow()
        {
            _window.Dispatcher.Invoke(() =>
            {
                _window.Show();
                _window.ShowInTaskbar = true;
                if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
                _window.Activate();
                _window.Topmost = _vm.AlwaysOnTop;   // don't leave it forced-topmost from Activate
            });
        }

        // Tray "Exit": force a real close even if close-to-tray is on.
        private void ExitApp()
        {
            ForceExit = true;
            _window.Dispatcher.Invoke(() => _window.Close());
        }

        // A 16x16 tray icon: a filled status dot, with a thin ring when recording so it reads as "live".
        private Icon MakeDotIcon(Color color, bool recording)
        {
            using var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                if (recording)
                {
                    using var ring = new Pen(Color.FromArgb(0x80, color), 2f);
                    g.DrawEllipse(ring, 1, 1, 13, 13);
                    using var fill = new SolidBrush(color);
                    g.FillEllipse(fill, 4, 4, 8, 8);
                }
                else
                {
                    using var fill = new SolidBrush(color);
                    g.FillEllipse(fill, 3, 3, 10, 10);
                }
            }
            IntPtr h = bmp.GetHicon();
            try { return (Icon)Icon.FromHandle(h).Clone(); }
            finally { DestroyIcon(h); }   // Clone() copies it; free the GDI handle so it doesn't leak
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _vm.PropertyChanged -= OnVmChanged;
            _window.StateChanged -= OnWindowStateChanged;
            _icon.Visible = false;
            _icon.Dispose();
            _idleIcon?.Dispose();
            _recIcon?.Dispose();
        }
    }
}
