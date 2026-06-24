using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TimelapseCapture; // Core: SessionManager, SessionInfo

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// In-app session picker: lists the sessions found under the output folder's "captures" root
    /// (name · date · frame count · region size) instead of making the user pick a timestamp folder
    /// out of a native file browser. Sets <see cref="SelectedFolder"/> and DialogResult=true on load.
    /// </summary>
    public partial class LoadSessionDialog : Window
    {
        public string? SelectedFolder { get; private set; }

        public LoadSessionDialog(string capturesRoot)
        {
            InitializeComponent();

            var items = new List<SessionListItem>();
            try
            {
                if (Directory.Exists(capturesRoot))
                {
                    foreach (var dir in Directory.GetDirectories(capturesRoot))
                    {
                        var s = SessionManager.LoadSession(dir);
                        if (s != null) items.Add(new SessionListItem(dir, s));
                    }
                }
            }
            catch { /* best-effort listing — a bad folder shouldn't break the picker */ }

            items.Sort((a, b) => b.SortKey.CompareTo(a.SortKey)); // newest first

            list.ItemsSource = items;
            if (items.Count == 0) emptyMsg.Visibility = Visibility.Visible;
            else list.SelectedIndex = 0;
        }

        private void OnDoubleClick(object sender, MouseButtonEventArgs e) => TryLoad();
        private void OnLoad(object sender, RoutedEventArgs e) => TryLoad();
        private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

        private void TryLoad()
        {
            if (list.SelectedItem is SessionListItem item)
            {
                SelectedFolder = item.FolderPath;
                DialogResult = true;
            }
        }
    }

    /// <summary>One row in the session picker.</summary>
    public sealed class SessionListItem
    {
        public string FolderPath { get; }
        public string Name { get; }
        public string DateText { get; }
        public string FramesText { get; }
        public string SizeText { get; }
        public string Detail { get; }
        public DateTime SortKey { get; }
        public ImageSource? Thumbnail { get; }

        public SessionListItem(string folder, SessionInfo s)
        {
            FolderPath = folder;
            Name = string.IsNullOrWhiteSpace(s.Name) ? Path.GetFileName(folder) : s.Name!;
            Thumbnail = LoadThumbnail(folder);

            DateTime when = s.StartTime;
            if (when == default)
            {
                try { when = Directory.GetCreationTime(folder); } catch { when = DateTime.Now; }
            }
            SortKey = when;
            DateText = when.ToString("dd MMM, HH:mm");

            FramesText = $"{s.FramesCaptured} frame{(s.FramesCaptured == 1 ? "" : "s")}";
            SizeText = s.CaptureRegion.HasValue
                ? $"{s.CaptureRegion.Value.Width}×{s.CaptureRegion.Value.Height}"
                : "no region";
            Detail = $"{DateText}   ·   {FramesText}   ·   {SizeText}";
        }

        // Load the most recent frame as a small, frozen thumbnail (null if the session has no frames).
        private static ImageSource? LoadThumbnail(string folder)
        {
            try
            {
                string frames = SessionManager.GetFramesFolder(folder);
                if (!Directory.Exists(frames)) return null;

                string? last = null;
                foreach (var f in Directory.GetFiles(frames))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if ((ext == ".jpg" || ext == ".jpeg" || ext == ".png") &&
                        (last == null || string.CompareOrdinal(f, last) > 0))
                        last = f;
                }
                if (last == null) return null;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(last);
                bmp.DecodePixelWidth = 120;                 // keep memory small
                bmp.CacheOption = BitmapCacheOption.OnLoad;  // don't lock the file
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }
}
