using System.Windows;
using System.Windows.Input;
using TimelapseCapture.Wpf.ViewModels;

namespace TimelapseCapture.Wpf
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
            versionText.Text = v != null ? $"FrameWrite v{v.Major}.{v.Minor}.{v.Build} — timelapse capture" : "FrameWrite";
            creditsText.Text = "Created and directed by Spike Tickner · engineered with Claude (Anthropic) · video by FFmpeg";

            Loaded += (s, e) => RefreshHotkeyText();
            hotkeyBox.GotKeyboardFocus += (s, e) => hotkeyBox.Text = "Press a key combination…";
            hotkeyBox.LostKeyboardFocus += (s, e) => RefreshHotkeyText();
        }

        private void RefreshHotkeyText() => hotkeyBox.Text = (DataContext as MainViewModel)?.HotkeyDisplay ?? "";

        // Clear every persisted "don't ask me again" choice; the button reports the outcome inline.
        private void OnResetPrompts(object sender, RoutedEventArgs e)
        {
            int n = (DataContext as MainViewModel)?.ResetSuppressedPrompts() ?? 0;
            resetPromptsBtn.Content = n > 0 ? $"Restored {n} confirmation(s) ✓" : "Nothing was dismissed";
            resetPromptsBtn.IsEnabled = false;   // one-shot per visit; reopening Settings re-arms it
        }

        // Capture a key combination for the global hotkey.
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

            (DataContext as MainViewModel)?.SetHotkey(Keyboard.Modifiers, key);
            RefreshHotkeyText();
        }
    }
}
