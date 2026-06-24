using System.Windows;

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// Program preferences that don't belong on the main panel. Shares the MainViewModel as its
    /// DataContext, so its controls bind straight to VM settings properties. The home to grow into.
    /// </summary>
    public partial class SettingsDialog : Window
    {
        public SettingsDialog()
        {
            InitializeComponent();
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            versionText.Text = v != null ? $"Timelapse Capture v{v.Major}.{v.Minor}.{v.Build}" : "Timelapse Capture";
        }
    }
}
