using System.Windows;

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// Gate for repeat-prone confirmations with a persistent "don't ask again" escape hatch.
    /// A suppressed prompt auto-answers YES (the box only records on a Yes — suppressing a No
    /// would silently veto the action forever). Reset lives in Settings.
    /// NEVER route destructive consents (cull / crop-on-disk / overlay bake) through this.
    /// </summary>
    public static class Prompts
    {
        public static bool Confirm(CaptureSettings settings, string key, string message, string title,
                                   MessageBoxImage image = MessageBoxImage.Question)
        {
            if (settings.SuppressedPrompts?.Contains(key) == true) return true;

            var (result, dontAsk) = MessageDialog.ShowWithSuppress(message, title, MessageBoxButton.YesNo, image);
            if (result == MessageBoxResult.Yes && dontAsk)
            {
                (settings.SuppressedPrompts ??= new()).Add(key);
                SettingsManager.Save(settings);
            }
            return result == MessageBoxResult.Yes;
        }
    }
}
