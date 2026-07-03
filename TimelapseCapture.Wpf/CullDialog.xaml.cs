using System;
using System.Collections.Generic;
using System.Windows;

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// Scrub the captured frames and mark fumbles for deletion. On apply the marked frames are removed and
    /// the rest renumbered (SessionManager.CullAndRenumber) so the sequence stays gapless. Destructive.
    /// </summary>
    public partial class CullDialog : Window
    {
        private readonly string _folder;
        private readonly int _count;
        private readonly HashSet<int> _marked = new();

        public IReadOnlyCollection<int> MarkedForDeletion => _marked;

        public CullDialog(string sessionFolder, int frameCount)
        {
            InitializeComponent();
            _folder = sessionFolder;
            _count = Math.Max(1, frameCount);

            scrub.Maximum = _count;
            scrub.ValueChanged += (s, e) => ShowFrame((int)e.NewValue);
            Loaded += (s, e) => { scrub.Value = 1; ShowFrame(1); UpdateCount(); RedrawMarkers(); };
            markerCanvas.SizeChanged += (s, e) => RedrawMarkers();
        }

        private int Current => (int)scrub.Value;

        // ±1 / ±10 frame steppers (hold to repeat) — the step size rides in the button's Tag.
        private void OnStep(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is string t && int.TryParse(t, out int delta))
                scrub.Value = Math.Clamp(Current + delta, 1, _count);
        }

        // Red ticks above the scrubber at each marked frame's position, so the damage is visible at a
        // glance before applying. Aligned to the thumb's travel (its center spans 7 .. width-7).
        private void RedrawMarkers()
        {
            markerCanvas.Children.Clear();
            double w = markerCanvas.ActualWidth;
            if (_count < 1 || w < 16) return;
            var fill = TryFindResource("DangerBrush") as System.Windows.Media.Brush
                       ?? System.Windows.Media.Brushes.IndianRed;
            foreach (int n in _marked)
            {
                double t = _count == 1 ? 0 : (n - 1) / (double)(_count - 1);
                var tick = new System.Windows.Shapes.Rectangle { Width = 2, Height = 6, Fill = fill, RadiusX = 1, RadiusY = 1 };
                System.Windows.Controls.Canvas.SetLeft(tick, 7 + t * (w - 14) - 1);
                markerCanvas.Children.Add(tick);
            }
        }

        private void ShowFrame(int n)
        {
            preview.Source = FramePreview.LoadAt(_folder, n, 540);
            posText.Text = $"Frame {n} of {_count}";
            bool marked = _marked.Contains(n);
            markedBadge.Visibility = marked ? Visibility.Visible : Visibility.Collapsed;
            markBtn.Content = marked ? "Unmark" : "Mark for deletion";
        }

        private void OnToggleMark(object sender, RoutedEventArgs e)
        {
            int n = Current;
            if (!_marked.Remove(n)) _marked.Add(n);
            ShowFrame(n);
            UpdateCount();
            RedrawMarkers();
        }

        private void UpdateCount()
        {
            markedCount.Text = _marked.Count == 0 ? "none marked" : $"{_marked.Count} marked";
            applyBtn.IsEnabled = _marked.Count > 0;
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            if (_marked.Count == 0) return;
            if (_marked.Count >= _count)
            {
                MessageBox.Show("That would delete every frame — leave at least one.",
                    "Cull frames", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var r = MessageBox.Show($"Delete {_marked.Count} frame(s) and renumber the rest? This can't be undone.",
                "Cull frames", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Yes) DialogResult = true;
        }
    }
}
