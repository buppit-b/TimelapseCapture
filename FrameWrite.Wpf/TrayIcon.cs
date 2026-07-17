using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using WinForms = System.Windows.Forms;
using FrameWrite.Wpf.ViewModels;

namespace FrameWrite.Wpf
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
        private readonly WinForms.ToolStripMenuItem _pauseItem;
        private Icon? _idleIcon, _recIcon, _pausedIcon;
        private bool _disposed;

        /// <summary>True once the user picked "Exit" from the tray menu — so close-to-tray is bypassed.</summary>
        public bool ForceExit { get; private set; }

        public TrayIcon(Window window, MainViewModel vm)
        {
            _window = window;
            _vm = vm;

            _idleIcon = MakeDotIcon(Color.FromArgb(0x3F, 0xB9, 0x50), recording: false); // green: stopped
            _recIcon = MakeDotIcon(Color.FromArgb(0xE5, 0x53, 0x4B), recording: true);   // red ring: recording
            _pausedIcon = MakeDotIcon(Color.FromArgb(0xE3, 0xB3, 0x41), recording: true); // amber ring: idle/paused

            var menu = new WinForms.ContextMenuStrip();
            _showItem = new WinForms.ToolStripMenuItem("Show FrameWrite", null, (_, _) => RestoreWindow());
            _toggleItem = new WinForms.ToolStripMenuItem("Start capture", null, (_, _) => ToggleCapture());
            _pauseItem = new WinForms.ToolStripMenuItem("Pause capture", null, (_, _) => TogglePause());
            var exitItem = new WinForms.ToolStripMenuItem("Exit", null, (_, _) => ExitApp());
            menu.Items.Add(_showItem);
            menu.Items.Add(_toggleItem);
            menu.Items.Add(_pauseItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            _icon = new WinForms.NotifyIcon
            {
                Icon = _idleIcon,
                Text = "FrameWrite",
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
            if (e.PropertyName is nameof(MainViewModel.IsCapturing) or nameof(MainViewModel.FrameCount)
                or nameof(MainViewModel.IsCaptureIdleOrPaused) or nameof(MainViewModel.CaptureStatusDetail)
                or nameof(MainViewModel.IsPaused) or null)
                UpdateStatus();
        }

        // Reflect capture state in the icon (green stopped / red recording / amber idle-paused), the
        // tooltip, and the menu's Start/Stop label.
        private void UpdateStatus()
        {
            bool rec = _vm.IsCapturing;
            bool amber = _vm.IsCaptureIdleOrPaused;
            _icon.Icon = !rec ? _idleIcon : amber ? _pausedIcon : _recIcon;
            // NotifyIcon.Text is capped at 63 chars.
            string state = !rec ? "Idle"
                : amber ? (string.IsNullOrEmpty(_vm.CaptureStatusDetail) ? "Paused" : _vm.CaptureStatusDetail)
                : $"Recording ({_vm.FrameCount} frames)";
            _icon.Text = $"FrameWrite — {state}";
            _toggleItem.Text = rec ? "Stop capture" : "Start capture";
            _pauseItem.Enabled = rec;   // pause only means something during a run
            _pauseItem.Text = _vm.IsPaused ? "Resume capture" : "Pause capture";
        }

        private void ToggleCapture() => _window.Dispatcher.Invoke(() => _vm.ToggleCaptureHotkey());
        private void TogglePause() => _window.Dispatcher.Invoke(() => _vm.PauseHotkey());

        /// <summary>Show a tray balloon (used when a capture finishes while the app is hidden in the tray).</summary>
        public void ShowBalloon(string message)
        {
            try
            {
                _icon.BalloonTipTitle = "FrameWrite";
                _icon.BalloonTipText = message;
                _icon.ShowBalloonTip(4000);
            }
            catch { /* balloons are best-effort */ }
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            // Hide() alone removes the taskbar button and the window; the tray icon is the way back.
            // (Do NOT toggle ShowInTaskbar — changing it after load recreates the HWND, which would drop
            // the global-hotkey registration and the hide-from-capture affinity hook.)
            if (_window.WindowState == WindowState.Minimized && _vm.MinimizeToTray)
                _window.Hide();
        }

        private void RestoreWindow()
        {
            _window.Dispatcher.Invoke(() =>
            {
                if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
                _window.Show();
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
            try
            {
                using var tmp = Icon.FromHandle(h);   // dispose the transient wrapper deterministically
                return (Icon)tmp.Clone();             // Clone owns an independent copy
            }
            finally { DestroyIcon(h); }   // free the GDI handle so it doesn't leak
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
            _pausedIcon?.Dispose();
        }
    }
}
