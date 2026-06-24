using System.Windows;

namespace TimelapseCapture.Wpf
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
            var t = input.Text?.Trim();
            if (string.IsNullOrEmpty(t)) { DialogResult = false; return; }
            Value = t;
            DialogResult = true;
        }
    }
}
