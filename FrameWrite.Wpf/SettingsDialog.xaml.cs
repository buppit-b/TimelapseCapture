using System;
using System.Windows;
using System.Windows.Input;
using FrameWrite.Wpf.ViewModels;

namespace FrameWrite.Wpf
{
    /// <summary>
    /// Program preferences (grouped into collapsible sections). Shares the MainViewModel as its
    /// DataContext, so its controls bind straight to VM settings properties.
    /// </summary>
    public partial class SettingsDialog : Window
    {
        public SettingsDialog()
        {
            InitializeComponent();

            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            versionText.Text = v != null ? $"FrameWrite v{v.Major}.{v.Minor}.{v.Build} — a timelapse capture application" : "FrameWrite";
            creditsText.Text = "Created by Spike Tickner · video by FFmpeg";

            Loaded += (s, e) => { RefreshHotkeyBoxes(); BuildDriveBar(); HookVm(); };
            Closed += (s, e) => UnhookVm();
            HookHotkeyBox(hkStartStop);
            HookHotkeyBox(hkPause);
            HookHotkeyBox(hkRegion);
        }

        // ---- drive gauge (Capture safety): used | free with the auto-stop floor zoned at the right ----

        private void HookVm()
        {
            if (DataContext is MainViewModel vm) vm.PropertyChanged += OnVmPropChanged;
        }

        private void UnhookVm()
        {
            if (DataContext is MainViewModel vm) vm.PropertyChanged -= OnVmPropChanged;
        }

        private void OnVmPropChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MainViewModel.LowDiskStopMB) or nameof(MainViewModel.OutputFolder))
                BuildDriveBar();
        }

        private void BuildDriveBar()
        {
            long total = 0, free = 0;
            string root = "";
            if (DataContext is MainViewModel vm && !string.IsNullOrWhiteSpace(vm.OutputFolder)
                && System.IO.Directory.Exists(vm.OutputFolder))
            {
                total = SystemMonitor.GetTotalDiskSpaceMB(vm.OutputFolder);
                free = SystemMonitor.GetAvailableDiskSpaceMB(vm.OutputFolder);
                try { root = System.IO.Path.GetPathRoot(vm.OutputFolder) ?? ""; } catch { }
            }
            if (total <= 0)   // no folder yet / probe failed — hide rather than draw nonsense
            {
                driveBarBox.Visibility = Visibility.Collapsed;
                driveBarText.Visibility = Visibility.Collapsed;
                return;
            }
            driveBarBox.Visibility = Visibility.Visible;
            driveBarText.Visibility = Visibility.Visible;

            long floor = Math.Clamp((DataContext as MainViewModel)?.LowDiskStopMB ?? 0, 0, total);
            long used = Math.Max(0, total - free);
            long freeAbove = Math.Max(0, free - floor);      // the space capture may still use
            long floorZone = Math.Min(free, floor);          // the protected tail (all of free, if already inside it)

            driveBar.Children.Clear();
            driveBar.ColumnDefinitions.Clear();
            void Seg(long mb, System.Windows.Media.Brush brush, double opacity, string tip, double minPx = 0)
            {
                driveBar.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
                { Width = new GridLength(Math.Max(0, mb), GridUnitType.Star), MinWidth = minPx });
                var r = new System.Windows.Shapes.Rectangle { Fill = brush, Opacity = opacity, ToolTip = tip };
                System.Windows.Controls.Grid.SetColumn(r, driveBar.ColumnDefinitions.Count - 1);
                driveBar.Children.Add(r);
            }
            Seg(used, (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"), 0.35, $"In use: {FmtMb(used)}");
            Seg(freeAbove, (System.Windows.Media.Brush)FindResource("AccentBrush"), 0.75, $"Free for capture: {FmtMb(freeAbove)}");
            Seg(floorZone, (System.Windows.Media.Brush)FindResource("DangerBrush"), 0.6,
                $"Auto-stop floor: capture stops before eating into the last {FmtMb(floor)}", minPx: 3);

            driveBarText.Text = $"{root} used {FmtMb(used)} · free {FmtMb(free)} · auto-stop keeps the last {FmtMb(floor)} free";
        }

        private static string FmtMb(long mb) => mb >= 1048576
            ? $"{mb / 1048576.0:0.##} TB" : mb >= 1024 ? $"{mb / 1024.0:0.#} GB" : $"{mb} MB";

        private void HookHotkeyBox(System.Windows.Controls.TextBox box)
        {
            box.GotKeyboardFocus += (s, e) => box.Text = "Press a key combination…";
            box.LostKeyboardFocus += (s, e) => RefreshHotkeyBoxes();
        }

        private void RefreshHotkeyBoxes()
        {
            if (DataContext is not MainViewModel vm) return;
            hkStartStop.Text = vm.GetHotkeyDisplay(MainViewModel.HotkeyStartStop);
            hkPause.Text = vm.GetHotkeyDisplay(MainViewModel.HotkeyPause);
            hkRegion.Text = vm.GetHotkeyDisplay(MainViewModel.HotkeyRegionSelect);
        }

        // Restore the main window to its default size + centre (a quick escape hatch after resizing).
        private void OnResetWindowSize(object sender, RoutedEventArgs e)
            => (Application.Current.MainWindow as MainWindow)?.ResetWindowSize();

        // Clear every persisted "don't ask me again" choice; the button reports the outcome inline.
        private void OnResetPrompts(object sender, RoutedEventArgs e)
        {
            int n = (DataContext as MainViewModel)?.ResetSuppressedPrompts() ?? 0;
            resetPromptsBtn.Content = n > 0 ? $"Restored {n} confirmation(s) ✓" : "Nothing was dismissed";
            resetPromptsBtn.IsEnabled = false;   // one-shot per visit; reopening Settings re-arms it
        }

        // Capture a key combination for whichever action's box has focus (its Tag names the action).
        private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            var key = (e.Key == Key.System) ? e.SystemKey : e.Key;

            // Ignore modifier-only presses — wait for the actual key.
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                    or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
                return;

            // A global hotkey should include at least one modifier (otherwise it would hijack a plain key).
            if (Keyboard.Modifiers == ModifierKeys.None) return;

            if (DataContext is not MainViewModel vm || sender is not System.Windows.Controls.TextBox box) return;
            string action = (string)box.Tag;
            string? conflict = vm.SetHotkey(action, Keyboard.Modifiers, key);
            if (conflict != null)
            {
                hotkeyConflictText.Text = $"That combination is already bound to {MainViewModel.HotkeyFriendly(conflict)} — pick another.";
                hotkeyConflictText.Visibility = Visibility.Visible;
                return;   // keep focus so the user can try again straight away
            }
            hotkeyConflictText.Visibility = Visibility.Collapsed;
            RefreshHotkeyBoxes();
            box.Text = (DataContext as MainViewModel)?.GetHotkeyDisplay(action) ?? box.Text;
        }

        private void OnHotkeyClear(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm || sender is not System.Windows.FrameworkElement el) return;
            vm.ClearHotkey((string)el.Tag);
            hotkeyConflictText.Visibility = Visibility.Collapsed;
            RefreshHotkeyBoxes();
        }
    }
}
