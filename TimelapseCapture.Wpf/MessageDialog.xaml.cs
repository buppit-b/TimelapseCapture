using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// Themed drop-in for System.Windows.MessageBox — same Show(...) signatures and MessageBoxResult,
    /// but in the app's dark chrome (DialogWindow style) instead of the native Win32 popup.
    /// </summary>
    public partial class MessageDialog : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;
        private int _choice = -1;

        private MessageDialog(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            InitializeComponent();
            Title = string.IsNullOrEmpty(title) ? "FrameWrite" : title;
            messageText.Text = message;
            ApplyIcon(image);
            BuildButtons(buttons);
        }

        // Custom-labelled buttons: choices[0] is the primary (rendered rightmost, default),
        // the LAST choice is the cancel/escape one. Used where Yes/No can't carry the meaning.
        private MessageDialog(string message, string title, MessageBoxImage image, string[] choices)
        {
            InitializeComponent();
            Title = string.IsNullOrEmpty(title) ? "FrameWrite" : title;
            messageText.Text = message;
            ApplyIcon(image);
            for (int i = choices.Length - 1; i >= 0; i--)   // reverse: primary ends up rightmost
            {
                int index = i;
                var b = new Button
                {
                    Content = choices[i],
                    Style = (Style)FindResource(i == 0 ? "BtnPrimary" : "BtnBase"),
                    MinWidth = 88,
                    Margin = new Thickness(8, 0, 0, 0),
                    IsDefault = i == 0,
                    IsCancel = i == choices.Length - 1,
                };
                b.Click += (_, _) => { _choice = index; DialogResult = true; };
                buttonPanel.Children.Add(b);
            }
        }

        private void ApplyIcon(MessageBoxImage image)
        {
            // Danger palette for stop/error/warning; accent for question/info; hidden for None.
            (string glyph, string brushKey)? spec = image switch
            {
                MessageBoxImage.Error => ("!", "DangerBrush"),        // == Stop / Hand
                MessageBoxImage.Warning => ("!", "DangerBrush"),      // == Exclamation
                MessageBoxImage.Question => ("?", "AccentBrush"),
                MessageBoxImage.Information => ("i", "AccentBrush"),  // == Asterisk
                _ => null,
            };
            if (spec is not { } s) { iconBadge.Visibility = Visibility.Collapsed; return; }

            var brush = TryFindResource(s.brushKey) as SolidColorBrush ?? Brushes.Gray;
            iconGlyph.Text = s.glyph;
            iconGlyph.Foreground = brush;
            var c = brush.Color;
            iconBadge.Background = new SolidColorBrush(Color.FromArgb(0x26, c.R, c.G, c.B));   // ~15% tint
        }

        private void BuildButtons(MessageBoxButton buttons)
        {
            switch (buttons)
            {
                case MessageBoxButton.OK:
                    Add("OK", MessageBoxResult.OK, primary: true, isDefault: true, isCancel: true);
                    break;
                case MessageBoxButton.OKCancel:
                    Add("Cancel", MessageBoxResult.Cancel, primary: false, isDefault: false, isCancel: true);
                    Add("OK", MessageBoxResult.OK, primary: true, isDefault: true, isCancel: false);
                    break;
                case MessageBoxButton.YesNo:
                    Add("No", MessageBoxResult.No, primary: false, isDefault: false, isCancel: true);
                    Add("Yes", MessageBoxResult.Yes, primary: true, isDefault: true, isCancel: false);
                    break;
                case MessageBoxButton.YesNoCancel:
                    Add("Cancel", MessageBoxResult.Cancel, primary: false, isDefault: false, isCancel: true);
                    Add("No", MessageBoxResult.No, primary: false, isDefault: false, isCancel: false);
                    Add("Yes", MessageBoxResult.Yes, primary: true, isDefault: true, isCancel: false);
                    break;
            }
        }

        private void Add(string text, MessageBoxResult result, bool primary, bool isDefault, bool isCancel)
        {
            var b = new Button
            {
                Content = text,
                Style = (Style)FindResource(primary ? "BtnPrimary" : "BtnBase"),
                MinWidth = 88,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = isDefault,
                IsCancel = isCancel,
            };
            b.Click += (_, _) => { Result = result; DialogResult = true; };
            buttonPanel.Children.Add(b);
        }

        // ---- MessageBox.Show-compatible entry points ----
        public static MessageBoxResult Show(string message) => Show(message, "FrameWrite");
        public static MessageBoxResult Show(string message, string title) => Show(message, title, MessageBoxButton.OK);
        public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons) =>
            Show(message, title, buttons, MessageBoxImage.None);

        public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            var dlg = new MessageDialog(message, title, buttons, image) { Owner = ActiveOwner() };
            dlg.ShowDialog();
            return dlg.Result;
        }

        /// <summary>
        /// Custom-labelled choice buttons. choices[0] = primary/default (rightmost), the last
        /// choice = cancel (Esc / close). Returns the clicked choice's index, or -1 when dismissed.
        /// </summary>
        public static int ShowChoices(string message, string title, MessageBoxImage image, params string[] choices)
        {
            var dlg = new MessageDialog(message, title, image, choices) { Owner = ActiveOwner() };
            dlg.ShowDialog();
            return dlg._choice;
        }

        /// <summary>
        /// Same dialog with a "Don't ask me again" checkbox. For repeat-prone confirmations only —
        /// NEVER for destructive consents (cull / crop-on-disk / overlay bake), those must always ask.
        /// Callers persist the suppression; see Prompts.Confirm.
        /// </summary>
        public static (MessageBoxResult result, bool dontAskAgain) ShowWithSuppress(
            string message, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            var dlg = new MessageDialog(message, title, buttons, image) { Owner = ActiveOwner() };
            dlg.dontAskBox.Visibility = Visibility.Visible;
            dlg.ShowDialog();
            return (dlg.Result, dlg.dontAskBox.IsChecked == true);
        }

        private static Window? ActiveOwner()
        {
            var app = Application.Current;
            if (app == null) return null;
            var active = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible);
            return active ?? (app.MainWindow is { IsVisible: true } m ? m : null);
        }
    }
}
