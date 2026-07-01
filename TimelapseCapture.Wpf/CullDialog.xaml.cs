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
            Loaded += (s, e) => { scrub.Value = 1; ShowFrame(1); UpdateCount(); };
        }

        private int Current => (int)scrub.Value;

        private void ShowFrame(int n)
        {
            preview.Source = FramePreview.LoadAt(_folder, n, 540);
            posText.Text = $"Frame {n} of {_count}";
            bool marked = _marked.Contains(n);
            markedBadge.Visibility = marked ? Visibility.Visible : Visibility.Collapsed;
            markBtn.Content = marked ? "Unmark" : "Mark for deletion";
        }

        private void OnPrev(object sender, RoutedEventArgs e) { if (Current > 1) scrub.Value = Current - 1; }
        private void OnNext(object sender, RoutedEventArgs e) { if (Current < _count) scrub.Value = Current + 1; }

        private void OnToggleMark(object sender, RoutedEventArgs e)
        {
            int n = Current;
            if (!_marked.Remove(n)) _marked.Add(n);
            ShowFrame(n);
            UpdateCount();
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
