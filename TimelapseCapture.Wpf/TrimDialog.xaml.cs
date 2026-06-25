using System;
using System.Windows;

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// Scrub the captured frames and pick a start/end range to encode (trim by frame range — no
    /// re-cut of an existing video). Returns StartFrame/EndFrame (1-based) and DialogResult=true.
    /// </summary>
    public partial class TrimDialog : Window
    {
        private readonly string _folder;
        private readonly int _count;

        public int StartFrame { get; private set; }
        public int EndFrame { get; private set; }

        public TrimDialog(string sessionFolder, int frameCount)
        {
            InitializeComponent();
            _folder = sessionFolder;
            _count = Math.Max(1, frameCount);
            StartFrame = 1;
            EndFrame = _count;

            scrub.Maximum = _count;
            scrub.ValueChanged += (s, e) => ShowFrame((int)e.NewValue);
            Loaded += (s, e) => { scrub.Value = 1; ShowFrame(1); UpdateRange(); };
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
            => rangeText.Text = $"From {StartFrame}  ·  To {EndFrame}  ·  {EndFrame - StartFrame + 1} frames";

        private void OnEncode(object sender, RoutedEventArgs e)
        {
            if (EndFrame < StartFrame) return;
            DialogResult = true;
        }
    }
}
