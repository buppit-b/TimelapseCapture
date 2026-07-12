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

            Loaded += (s, e) => RefreshHotkeyBoxes();
            HookHotkeyBox(hkStartStop);
            HookHotkeyBox(hkPause);
            HookHotkeyBox(hkRegion);
        }

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

        // Hidden gesture: click the version line 5× to unlock developer mode (VM handles the count + confirm).
        private void OnVersionClick(object sender, MouseButtonEventArgs e)
        {
            (DataContext as MainViewModel)?.RegisterVersionClickForDevUnlock();
        }

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
