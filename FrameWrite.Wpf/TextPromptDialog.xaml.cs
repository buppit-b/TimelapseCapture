using System.Windows;

namespace FrameWrite.Wpf
{
    /// <summary>Minimal single-line text prompt (title, label, initial value) → trimmed Value on OK.</summary>
    public partial class TextPromptDialog : Window
    {
        public string? Value { get; private set; }

        public TextPromptDialog(string title, string label, string initial)
        {
            InitializeComponent();
            Title = title;
            promptLabel.Text = label;
            input.Text = initial;
            Loaded += (s, e) => { input.Focus(); input.SelectAll(); };
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            // Blank + OK is an ACCEPT with an empty value (callers apply their own fallback/keep-current),
            // not a fake cancel — closing with false here made "clear the text, press Enter" a silent no-op.
            var t = input.Text?.Trim();
            Value = string.IsNullOrEmpty(t) ? null : t;
            DialogResult = true;
        }
    }
}
