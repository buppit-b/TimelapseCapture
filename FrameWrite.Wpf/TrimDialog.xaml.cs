using System;
using System.Windows;

namespace FrameWrite.Wpf
{
    /// <summary>
    /// Scrub the captured frames and pick a start/end range to encode (trim by frame range — no
    /// re-cut of an existing video). Returns StartFrame/EndFrame (1-based) and DialogResult=true.
    /// </summary>
    public partial class TrimDialog : Window
    {
        private readonly string _folder;
        private readonly int _count;
        private readonly int _targetFrames;

        public int StartFrame { get; private set; }
        public int EndFrame { get; private set; }

        public TrimDialog(string sessionFolder, int frameCount, int targetFrames = 0, string? targetLabel = null,
            int savedStart = 0, int savedEnd = 0)
        {
            InitializeComponent();
            _folder = sessionFolder;
            _count = Math.Max(1, frameCount);
            _targetFrames = targetFrames;
            StartFrame = 1;
            EndFrame = _count;

            // Restore previously placed markers (persisted in session.json) — clamped to the current
            // frame count, and only when they still form a sane range.
            if (savedEnd > 0 && savedStart >= 1 && savedStart <= savedEnd)
            {
                StartFrame = Math.Min(savedStart, _count);
                EndFrame = Math.Min(savedEnd, _count);
            }

            // Offer the Stats target as a one-click range when the session overshot it (e.g. the user
            // didn't enable stop-at-target but still wants a video of exactly the planned length).
            if (_targetFrames > 0 && _targetFrames < _count)
            {
                targetRow.Visibility = Visibility.Visible;
                targetText.Text = $"Target: {_targetFrames} frames ({targetLabel})";
            }

            scrub.Maximum = _count;
            scrub.ValueChanged += (s, e) => ShowFrame((int)e.NewValue);
            Loaded += (s, e) => { scrub.Value = 1; ShowFrame(1); UpdateRange(); };
            rangeCanvas.SizeChanged += (s, e) => RedrawRangeMarkers();
            PreviewKeyDown += OnKey;
        }

        // Keyboard workflow: ←/→ ±1 (Shift ±10), S = set start, E = set end.
        private void OnKey(object sender, System.Windows.Input.KeyEventArgs e)
        {
            int step = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0 ? 10 : 1;
            switch (e.Key)
            {
                case System.Windows.Input.Key.Left: scrub.Value = Math.Clamp((int)scrub.Value - step, 1, _count); e.Handled = true; break;
                case System.Windows.Input.Key.Right: scrub.Value = Math.Clamp((int)scrub.Value + step, 1, _count); e.Handled = true; break;
                case System.Windows.Input.Key.S: OnSetStart(this, new RoutedEventArgs()); e.Handled = true; break;
                case System.Windows.Input.Key.E: OnSetEnd(this, new RoutedEventArgs()); e.Handled = true; break;
            }
        }

        // Accent ticks at start/end plus a translucent band between them, aligned to thumb travel
        // (its center spans 7 .. width-7) — same visual language as the cull dialog's red marks.
        private void RedrawRangeMarkers()
        {
            rangeCanvas.Children.Clear();
            double w = rangeCanvas.ActualWidth;
            if (_count < 1 || w < 16) return;
            var accent = TryFindResource("AccentBrush") as System.Windows.Media.Brush
                         ?? System.Windows.Media.Brushes.MediumSpringGreen;
            double X(int n) => 7 + (_count == 1 ? 0 : (n - 1) / (double)(_count - 1)) * (w - 14);
            double x1 = X(StartFrame), x2 = X(EndFrame);

            var band = new System.Windows.Shapes.Rectangle
            { Width = Math.Max(2, x2 - x1), Height = 3, Fill = accent, Opacity = 0.35, RadiusX = 1.5, RadiusY = 1.5 };
            System.Windows.Controls.Canvas.SetLeft(band, x1);
            System.Windows.Controls.Canvas.SetTop(band, 1.5);
            rangeCanvas.Children.Add(band);

            foreach (double x in new[] { x1, x2 })
            {
                var tick = new System.Windows.Shapes.Rectangle
                { Width = 2.5, Height = 6, Fill = accent, RadiusX = 1, RadiusY = 1 };
                System.Windows.Controls.Canvas.SetLeft(tick, x - 1.25);
                rangeCanvas.Children.Add(tick);
            }
        }

        private void OnClipToTarget(object sender, RoutedEventArgs e)
        {
            StartFrame = 1;
            EndFrame = Math.Min(_targetFrames, _count);
            scrub.Value = EndFrame;
            UpdateRange();
        }

        // ±1 / ±10 frame steppers (hold to repeat) — the step size rides in the button's Tag.
        private void OnStep(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is string t && int.TryParse(t, out int delta))
                scrub.Value = Math.Clamp((int)scrub.Value + delta, 1, _count);
        }

        private void ShowFrame(int n)
        {
            preview.Source = FramePreview.LoadAt(_folder, n, 540);
            posText.Text = $"Frame {n} of {_count}";
        }

        private void OnSetStart(object sender, RoutedEventArgs e)
        {
            StartFrame = (int)scrub.Value;
            if (EndFrame < StartFrame) EndFrame = StartFrame;
            UpdateRange();
        }

        private void OnSetEnd(object sender, RoutedEventArgs e)
        {
            EndFrame = (int)scrub.Value;
            if (StartFrame > EndFrame) StartFrame = EndFrame;
            UpdateRange();
        }

        private void UpdateRange()
        {
            rangeText.Text = $"From {StartFrame}  ·  To {EndFrame}  ·  {EndFrame - StartFrame + 1} frames";
            RedrawRangeMarkers();
        }

        private void OnEncode(object sender, RoutedEventArgs e)
        {
            // Defense in depth: Set start/end mutually clamp so an inverted range shouldn't exist,
            // but if one ever slips through, normalize by swapping rather than silently doing nothing.
            if (EndFrame < StartFrame) (StartFrame, EndFrame) = (EndFrame, StartFrame);
            DialogResult = true;
        }
    }
}
