using System;
using System.Collections.Generic;
using System.Windows;

namespace FrameWrite.Wpf
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
        private readonly System.Drawing.Size _frameDims;   // uniform across the session

        public IReadOnlyCollection<int> MarkedForDeletion => _marked;

        public CullDialog(string sessionFolder, int frameCount, IEnumerable<int>? savedMarks = null)
        {
            InitializeComponent();
            _folder = sessionFolder;
            _count = Math.Max(1, frameCount);
            _frameDims = SessionManager.GetFrameSize(sessionFolder);   // all frames share the canonical size

            // Restore previously placed marks (persisted in session.json), dropping any that no
            // longer point at an existing frame.
            if (savedMarks != null)
                foreach (int n in savedMarks)
                    if (n >= 1 && n <= _count) _marked.Add(n);

            scrub.Maximum = _count;
            scrub.ValueChanged += (s, e) => ShowFrame((int)e.NewValue);
            Loaded += (s, e) => { scrub.Value = 1; ShowFrame(1); UpdateCount(); RedrawMarkers(); };
            markerCanvas.SizeChanged += (s, e) => RedrawMarkers();
            PreviewKeyDown += OnKey;
        }

        private int Current => (int)scrub.Value;
        private int _markAnchor = 1;   // last frame a mark action touched — Shift+mark extends from here

        // Keyboard workflow: ←/→ ±1 (Shift ±10), Space or M = mark,
        // Shift+Space/M = mark range, Ctrl+Space/M = unmark range.
        private void OnKey(object sender, System.Windows.Input.KeyEventArgs e)
        {
            bool shift = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;
            bool ctrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0;
            switch (e.Key)
            {
                case System.Windows.Input.Key.Left: scrub.Value = Math.Clamp(Current - (shift ? 10 : 1), 1, _count); e.Handled = true; break;
                case System.Windows.Input.Key.Right: scrub.Value = Math.Clamp(Current + (shift ? 10 : 1), 1, _count); e.Handled = true; break;
                case System.Windows.Input.Key.Space:
                case System.Windows.Input.Key.M:
                    if (ctrl) SetRangeToCurrent(marked: false);
                    else if (shift) SetRangeToCurrent(marked: true);
                    else ToggleMark();
                    e.Handled = true; break;
            }
        }

        private void ToggleMark()
        {
            int n = Current;
            if (!_marked.Remove(n)) _marked.Add(n);
            _markAnchor = n;
            ShowFrame(n); UpdateCount(); RedrawMarkers();
        }

        // Bulk operations without clicking through every frame: (un)marks everything between the last
        // mark action and here (inclusive) — anchor one end, scrub to the other, Shift/Ctrl + mark.
        private void SetRangeToCurrent(bool marked)
        {
            int a = Math.Min(_markAnchor, Current), b = Math.Max(_markAnchor, Current);
            for (int i = a; i <= b; i++)
            {
                if (marked) _marked.Add(i);
                else _marked.Remove(i);
            }
            _markAnchor = Current;
            ShowFrame(Current); UpdateCount(); RedrawMarkers();
        }

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
            preview.Source = FramePreview.LoadAt(_folder, n, 0);   // native res — the view zooms now
            posText.Text = $"Frame {n} of {_count}";
            metaText.Text = DescribeFrame(n);
            bool marked = _marked.Contains(n);
            markedBadge.Visibility = marked ? Visibility.Visible : Visibility.Collapsed;
            markBtn.Content = marked ? "Unmark" : "Mark for deletion";
        }

        // dimensions (session-uniform) · file size · capture time (the file's own write time).
        private string DescribeFrame(int n)
        {
            try
            {
                string? path = FramePreview.PathFor(_folder, n);
                if (path == null) return "";
                var fi = new System.IO.FileInfo(path);
                string dims = _frameDims.Width > 0 ? $"{_frameDims.Width}×{_frameDims.Height}  ·  " : "";
                string size = fi.Length >= 1024 * 1024 ? $"{fi.Length / (1024.0 * 1024):F1} MB" : $"{fi.Length / 1024.0:F0} KB";
                return $"{dims}{size}  ·  {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}";
            }
            catch { return ""; }
        }

        private void OnToggleMark(object sender, RoutedEventArgs e)
        {
            var mods = System.Windows.Input.Keyboard.Modifiers;
            if ((mods & System.Windows.Input.ModifierKeys.Control) != 0) SetRangeToCurrent(marked: false);
            else if ((mods & System.Windows.Input.ModifierKeys.Shift) != 0) SetRangeToCurrent(marked: true);
            else ToggleMark();
        }

        private void OnClearMarks(object sender, RoutedEventArgs e)
        {
            _marked.Clear();
            ShowFrame(Current); UpdateCount(); RedrawMarkers();
        }

        private void UpdateCount()
        {
            markedCount.Text = _marked.Count == 0 ? "none marked" : $"{_marked.Count} marked";
            applyBtn.IsEnabled = _marked.Count > 0;
        }

        /// <summary>Back up the session (frames + session.json) before deleting — the safe default.</summary>
        public bool BackupFirstRequested { get; private set; }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            if (_marked.Count == 0) return;
            if (_marked.Count >= _count)
            {
                MessageDialog.Show("That would delete every frame — leave at least one.",
                    "Cull frames", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int choice = MessageDialog.ShowChoices(
                $"Delete {_marked.Count} frame(s) and renumber the rest? This can't be undone.",
                "Cull frames", MessageBoxImage.Warning,
                "Back up, then delete", "Delete without backup", "Cancel");
            if (choice is not (0 or 1)) return;
            BackupFirstRequested = choice == 0;
            DialogResult = true;
        }
    }
}
