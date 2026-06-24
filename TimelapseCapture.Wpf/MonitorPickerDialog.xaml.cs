using System.Collections.Generic;
using System.Drawing;
using System.Windows;
using System.Windows.Input;
using TimelapseCapture; // Core: ScreenHelper, AspectRatio

namespace TimelapseCapture.Wpf
{
    /// <summary>Picks which monitor to capture full-screen, showing each one's resolution and ratio.</summary>
    public partial class MonitorPickerDialog : Window
    {
        public Rectangle? SelectedBounds { get; private set; }

        public MonitorPickerDialog(IReadOnlyList<ScreenHelper.MonitorInfo> monitors)
        {
            InitializeComponent();

            var items = new List<MonitorItem>();
            int i = 1;
            foreach (var m in monitors)
            {
                var b = m.Bounds;
                string ratio = AspectRatio.CalculateRatioString(b.Width, b.Height);
                items.Add(new MonitorItem(
                    $"Monitor {i}{(m.IsPrimary ? "  ·  Primary" : "")}",
                    $"{b.Width}×{b.Height}  ·  {ratio}  ·  at ({b.X},{b.Y})",
                    b));
                i++;
            }
            list.ItemsSource = items;
            list.SelectedIndex = 0;
        }

        private void OnDoubleClick(object sender, MouseButtonEventArgs e) => TryPick();
        private void OnOk(object sender, RoutedEventArgs e) => TryPick();

        private void TryPick()
        {
            if (list.SelectedItem is MonitorItem m) { SelectedBounds = m.Bounds; DialogResult = true; }
        }
    }

    public sealed class MonitorItem
    {
        public string Label { get; }
        public string Detail { get; }
        public Rectangle Bounds { get; }
        public MonitorItem(string label, string detail, Rectangle bounds) { Label = label; Detail = detail; Bounds = bounds; }
    }
}
