using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using TimelapseCapture; // Core: WindowEnumerator

namespace TimelapseCapture.Wpf
{
    /// <summary>Picks a top-level window to capture and follow. Shows title, size and position.</summary>
    public partial class WindowPickerDialog : Window
    {
        public IntPtr SelectedHwnd { get; private set; }
        public string SelectedTitle { get; private set; } = "";

        public WindowPickerDialog()
        {
            InitializeComponent();
            Refresh();
        }

        private void Refresh()
        {
            var items = WindowEnumerator.Enumerate().Select(w => new WindowPickItem(
                w.IsMinimized ? $"{w.Title}   (minimized)" : w.Title,
                $"{w.Bounds.Width}×{w.Bounds.Height}  ·  at ({w.Bounds.X},{w.Bounds.Y})",
                w)).ToList();

            list.ItemsSource = items;
            if (items.Count > 0) list.SelectedIndex = 0;
        }

        private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();
        private void OnDoubleClick(object sender, MouseButtonEventArgs e) => TryPick();
        private void OnOk(object sender, RoutedEventArgs e) => TryPick();

        private void TryPick()
        {
            if (list.SelectedItem is not WindowPickItem item) return;
            var w = item.Window;

            // Re-check it's still around (it may have closed since the list was built).
            if (!WindowEnumerator.TryGetLiveBounds(w.Handle, out _, out bool minimized, out bool alive) || !alive)
            {
                MessageDialog.Show("That window is no longer available. Refresh and pick another.",
                    "Window unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
                Refresh();
                return;
            }

            if (minimized)
            {
                var r = MessageDialog.Show(
                    $"“{w.Title}” is minimized and can't be captured until you restore it.\n\nPick it anyway?",
                    "Window minimized", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
            }

            SelectedHwnd = w.Handle;
            SelectedTitle = w.Title;
            DialogResult = true;
        }
    }

    public sealed class WindowPickItem
    {
        public string Label { get; }
        public string Detail { get; }
        public WindowEnumerator.WindowInfo Window { get; }
        public WindowPickItem(string label, string detail, WindowEnumerator.WindowInfo window)
        {
            Label = label;
            Detail = detail;
            Window = window;
        }
    }
}
