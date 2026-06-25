using System.Windows;
using TimelapseCapture;

namespace TimelapseCapture.Wpf
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Apply the saved theme before the first window renders (resources already exist here).
            try { ThemeManager.Apply(SettingsManager.Load().Theme); } catch { }
            base.OnStartup(e);
        }
    }
}
