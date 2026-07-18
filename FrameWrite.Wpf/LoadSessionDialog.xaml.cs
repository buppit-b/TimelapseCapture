using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FrameWrite; // Core: SessionManager, SessionInfo, SessionArchiver, FfmpegRunner

namespace FrameWrite.Wpf
{
    /// <summary>
    /// In-app session picker: lists the sessions found under the output folder's "captures" root
    /// (name · date · frame count · region size) instead of making the user pick a timestamp folder
    /// out of a native file browser. Sets <see cref="SelectedFolder"/> and DialogResult=true on load.
    ///
    /// Also the storage-management surface: Archive packs a finished session's frames into one
    /// verified video (typically 5-15× smaller); Unarchive restores them. Loading an archived
    /// session offers to restore it first. Both run here, on cold sessions only — the currently
    /// loaded session can't be archived, so live state in the main window is never touched.
    /// </summary>
    public partial class LoadSessionDialog : Window
    {
        public string? SelectedFolder { get; private set; }

        private readonly string _capturesRoot;
        private readonly string? _currentSessionFolder;
        private readonly string? _ffmpegPath;
        private readonly ViewModels.MainViewModel? _vm;   // combine reads the live encode settings
        private CancellationTokenSource? _cts;
        private bool _busy;
        private bool _closeWhenIdle;

        public LoadSessionDialog(string capturesRoot, string? currentSessionFolder = null, string? ffmpegPath = null,
            ViewModels.MainViewModel? vm = null)
        {
            InitializeComponent();
            _capturesRoot = capturesRoot;
            _currentSessionFolder = currentSessionFolder;
            _ffmpegPath = ffmpegPath;
            _vm = vm;
            // Seed the sort control from the shared preference (default: date, newest first).
            _sortReady = false;
            string by = _vm?.SessionSortBy ?? "date";
            (by == "frames" ? sortFrames : by == "size" ? sortSize : by == "name" ? sortName : sortDate).IsChecked = true;
            sortDir.IsChecked = _vm?.SessionSortDescending ?? true;
            SyncSortGlyph();
            _sortReady = true;
            RebuildList(selectFolder: null);
        }

        // ---- session-list sorting (shared preference with the combine dialog) ----

        private bool _sortReady;   // suppress handler churn while seeding the controls

        private void OnSortChanged(object sender, RoutedEventArgs e)
        {
            if (!_sortReady) return;
            SyncSortGlyph();
            if (_vm != null)
            {
                _vm.SessionSortBy = SortField();
                _vm.SessionSortDescending = sortDir.IsChecked == true;
            }
            string? keep = (list.SelectedItem as SessionListItem)?.FolderPath;
            RebuildList(keep);
        }

        private string SortField() => sortFrames.IsChecked == true ? "frames"
            : sortSize.IsChecked == true ? "size" : sortName.IsChecked == true ? "name" : "date";

        private void SyncSortGlyph() => sortDir.Content = sortDir.IsChecked == true ? "↓" : "↑";

        /// <summary>The shared session comparer: field + direction (descending = newest/Z/most first).</summary>
        internal static Comparison<(string name, DateTime date, int frames, long area)> SortComparer(string by, bool desc)
        {
            int sign = desc ? -1 : 1;
            return by switch
            {
                "name" => (a, b) => sign * string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase),
                "frames" => (a, b) => sign * a.frames.CompareTo(b.frames),
                "size" => (a, b) => sign * a.area.CompareTo(b.area),
                _ => (a, b) => sign * a.date.CompareTo(b.date),
            };
        }

        private void RebuildList(string? selectFolder)
        {
            var items = new List<SessionListItem>();
            try
            {
                if (Directory.Exists(_capturesRoot))
                {
                    foreach (var dir in Directory.GetDirectories(_capturesRoot))
                    {
                        var s = SessionManager.LoadSession(dir);
                        if (s != null) items.Add(new SessionListItem(dir, s));
                    }
                }
            }
            catch { /* best-effort listing — a bad folder shouldn't break the picker */ }

            var cmp = SortComparer(SortField(), sortDir.IsChecked == true);
            items.Sort((a, b) => cmp((a.Name, a.SortKey, a.FrameCount, a.PixelArea), (b.Name, b.SortKey, b.FrameCount, b.PixelArea)));

            list.ItemsSource = items;
            emptyMsg.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (items.Count > 0)
            {
                int idx = selectFolder == null ? 0
                    : Math.Max(0, items.FindIndex(i => string.Equals(i.FolderPath, selectFolder, StringComparison.OrdinalIgnoreCase)));
                list.SelectedIndex = idx;
            }
            UpdateArchiveUi();
        }

        private void OnDoubleClick(object sender, MouseButtonEventArgs e) => TryLoad();
        private void OnLoad(object sender, RoutedEventArgs e) => TryLoad();

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            if (_busy) { _cts?.Cancel(); return; }   // first press cancels the running op
            DialogResult = false;
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_busy) return;
            // Mid-archive/restore: cancel the op (safe — nothing is deleted until verification)
            // and close once it has unwound, so no continuation ever touches a dead window.
            e.Cancel = true;
            _closeWhenIdle = true;
            _cts?.Cancel();
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateArchiveUi();

        private void UpdateArchiveUi()
        {
            if (_busy) return;   // the op's own status text owns the footer while running

            // Combine… is ALWAYS visible and openable — the staging dialog lists every session
            // with tick-to-include, so nothing needs pre-selecting here (a selection just arrives
            // pre-ticked). Multi-select mode hands the footer to it — Archive/Load need one session.
            var selected = list.SelectedItems.Cast<SessionListItem>().ToList();
            combineBtn.Content = selected.Count > 1 ? $"Combine {selected.Count}…" : "Combine…";
            string? blocked =
                _ffmpegPath == null ? "Needs ffmpeg — set it up from the main window first."
                : _vm == null ? "Combine isn't available here."
                : null;
            combineBtn.IsEnabled = blocked == null;
            combineBtn.ToolTip = blocked ??
                "Join sessions into one continuous video — tick them inside (anything selected here arrives pre-ticked), cull/crop each, tweak the settings, then encode.";

            if (selected.Count > 1)
            {
                archiveBtn.Visibility = Visibility.Collapsed;
                loadBtn.IsEnabled = false;   // loading needs exactly one session
                progressText.Text = $"{selected.Count} sessions · {selected.Sum(i => i.FrameCount)} frames";
                return;
            }

            loadBtn.IsEnabled = true;
            var item = list.SelectedItem as SessionListItem;
            archiveBtn.Visibility = item == null ? Visibility.Collapsed : Visibility.Visible;
            progressText.Text = "";
            if (item == null) return;

            archiveBtn.Content = item.IsArchived ? "Unarchive" : "Archive";
            bool isCurrent = _currentSessionFolder != null &&
                string.Equals(item.FolderPath, _currentSessionFolder, StringComparison.OrdinalIgnoreCase);
            if (_ffmpegPath == null)
            {
                archiveBtn.IsEnabled = false;
                archiveBtn.ToolTip = "Needs ffmpeg — set it up from the main window first.";
            }
            else if (item.IsArchived)
            {
                archiveBtn.IsEnabled = true;
                archiveBtn.ToolTip = $"Restore this session's {item.FrameCount} frames from its archive.";
            }
            else if (isCurrent)
            {
                archiveBtn.IsEnabled = false;
                archiveBtn.ToolTip = "This is the currently loaded session — load or create another one first, then archive this.";
            }
            else if (item.IsActive)
            {
                archiveBtn.IsEnabled = false;
                archiveBtn.ToolTip = "This session is marked as recording (crash recovery will resolve it on next launch).";
            }
            else if (item.FrameCount == 0)
            {
                archiveBtn.IsEnabled = false;
                archiveBtn.ToolTip = "No frames to archive.";
            }
            else
            {
                archiveBtn.IsEnabled = true;
                archiveBtn.ToolTip = "Pack this session's frames into one video file (typically 5-15× smaller). " +
                                     "Reversible — Unarchive restores the frames.";
            }
        }

        private async void OnArchive(object sender, RoutedEventArgs e)
        {
            if (_busy || _ffmpegPath == null || list.SelectedItem is not SessionListItem item) return;

            if (item.IsArchived)
            {
                await RunUnarchive(item);
                return;
            }

            string fidelity = item.FrameExt is "png" or "bmp"
                ? "Frames are re-encoded losslessly (pixel-identical on restore)."
                : "Frames are re-encoded visually lossless (restored frames are pixel-close, not byte-identical).";
            var r = MessageDialog.Show(
                $"Archive “{item.Name}”?\n\n" +
                $"Its {item.FrameCount} frames become one video file inside the session folder; the frame files are " +
                $"deleted only after the archive is verified. Unarchive restores them at any time.\n\n{fidelity}",
                "Archive session?", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            var result = await RunOp(item, "Archiving",
                (ct, tick) => SessionArchiver.ArchiveAsync(_ffmpegPath, item.FolderPath, ct, tick));
            if (result == null || result.Cancelled) return;   // window closed / user cancelled — no dialog
            if (result.Success)
                MessageDialog.Show(
                    $"Archived “{item.Name}”: {result.Frames} frames, " +
                    $"{Mb(result.BytesBefore)} → {Mb(result.BytesAfter)}.",
                    "Session archived");
            else
                MessageDialog.Show($"Archive failed — nothing was changed.\n\n{result.Error}",
                    "Archive session", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>Restore; returns true when the frames are back (shared by the button and the load gate).</summary>
        private async Task<bool> RunUnarchive(SessionListItem item)
        {
            if (_ffmpegPath == null) return false;
            var result = await RunOp(item, "Restoring",
                (ct, tick) => SessionArchiver.UnarchiveAsync(_ffmpegPath, item.FolderPath, ct, tick));
            if (result == null || result.Cancelled) return false;
            if (!result.Success)
                MessageDialog.Show($"Restore failed — the archive was kept.\n\n{result.Error}",
                    "Unarchive session", MessageBoxButton.OK, MessageBoxImage.Warning);
            return result.Success;
        }

        // ---- shared busy machinery for the long-running ops (archive / restore / combine) ----

        private void BeginBusy(string statusText)
        {
            _busy = true;
            _cts = new CancellationTokenSource();
            list.IsEnabled = false;
            archiveBtn.IsEnabled = false;
            combineBtn.IsEnabled = false;
            loadBtn.IsEnabled = false;
            progressText.Text = statusText;   // cancelBtn stays live — it doubles as op-cancel
        }

        private void EndBusy(string? reselectFolder)
        {
            _busy = false;
            _cts?.Dispose();
            _cts = null;
            if (_closeWhenIdle) { Close(); return; }
            list.IsEnabled = true;
            loadBtn.IsEnabled = true;
            RebuildList(reselectFolder);   // sizes/badges may have changed — re-read from disk
        }

        /// <summary>
        /// Run one archiver op with footer progress + cancel; UI locked while it runs. Returns null
        /// when the dialog is closing (result deliberately swallowed — the op was cancelled).
        /// </summary>
        private async Task<SessionArchiver.Result?> RunOp(SessionListItem item, string verb,
            Func<CancellationToken, Action<int>, Task<SessionArchiver.Result>> op)
        {
            int total = Math.Max(1, item.FrameCount);
            BeginBusy($"{verb} “{item.Name}”…");
            try
            {
                SessionArchiver.Result result;
                try
                {
                    result = await op(_cts!.Token, n => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_busy) progressText.Text = $"{verb} “{item.Name}”… {Math.Min(100, n * 100 / total)}%";
                    })));
                }
                catch (Exception ex)   // belt: the archiver contracts never-throw, but an async void
                {                      // handler upstream would turn any slip into an app crash
                    result = new SessionArchiver.Result { Success = false, Error = ex.Message };
                }
                return _closeWhenIdle ? null : result;
            }
            finally { EndBusy(item.FolderPath); }
        }

        // ---- multi-session combine: hand off to the staging dialog ----

        private void OnCombine(object sender, RoutedEventArgs e)
        {
            if (_busy || _ffmpegPath == null || _vm == null) return;
            // The staging dialog lists EVERY session with tick-to-include (whatever is selected
            // here arrives pre-ticked) and owns the rest: per-session cull/crop, per-combine
            // settings, and the encode. Prep may have changed disk — re-read the list after.
            var selected = list.SelectedItems.Cast<SessionListItem>().Select(i => i.FolderPath).ToList();
            var dlg = new CombineDialog(_capturesRoot, selected, _vm, _ffmpegPath, _currentSessionFolder) { Owner = this };
            dlg.ShowDialog();
            RebuildList(selected.FirstOrDefault());
        }

        private static string Mb(long bytes) => bytes >= 1073741824
            ? $"{bytes / 1073741824.0:0.##} GB" : $"{bytes / 1048576.0:0.#} MB";

        private async void TryLoad()
        {
            if (_busy || list.SelectedItems.Count != 1 || list.SelectedItem is not SessionListItem item) return;

            if (item.IsArchived)
            {
                if (_ffmpegPath == null)
                {
                    MessageDialog.Show("This session is archived — restoring its frames needs ffmpeg. Set it up from the main window first.",
                        "Load Session", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var r = MessageDialog.Show(
                    $"“{item.Name}” is archived. Restore its {item.FrameCount} frames and load it?",
                    "Unarchive and load?", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
                if (!await RunUnarchive(item)) return;
            }

            SelectedFolder = item.FolderPath;
            DialogResult = true;
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
        public bool IsArchived { get; }
        public bool IsActive { get; }
        public int FrameCount { get; }
        public string FrameExt { get; }
        /// <summary>Capture-area pixels (region W×H) — the "Size" sort key. 0 when no region.</summary>
        public long PixelArea { get; }

        public SessionListItem(string folder, SessionInfo s)
        {
            FolderPath = folder;
            Name = string.IsNullOrWhiteSpace(s.Name) ? Path.GetFileName(folder) : s.Name!;
            Thumbnail = FramePreview.LoadLatest(folder, 120);
            IsArchived = s.Archived;
            IsActive = s.Active;
            FrameCount = (int)Math.Max(s.FramesCaptured, s.Archived ? s.ArchivedFrames : 0);
            FrameExt = s.Archived
                ? (s.ArchiveFrameExt ?? "jpg")
                : (string.Equals(s.ImageFormat, "PNG", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg");

            DateTime when = s.StartTime;
            if (when == default)
            {
                try { when = Directory.GetCreationTime(folder); } catch { when = DateTime.Now; }
            }
            SortKey = when;
            DateText = when.ToString("dd MMM, HH:mm");

            FramesText = $"{FrameCount} frame{(FrameCount == 1 ? "" : "s")}";
            PixelArea = s.CaptureRegion.HasValue
                ? (long)s.CaptureRegion.Value.Width * s.CaptureRegion.Value.Height : 0;
            SizeText = s.CaptureRegion.HasValue
                ? $"{s.CaptureRegion.Value.Width}×{s.CaptureRegion.Value.Height}"
                : "no region";
            // Prefer the recorded actual interval (sub-second capable); fall back to the rounded int.
            double iv = s.IntervalSecondsActual > 0 ? s.IntervalSecondsActual : s.IntervalSeconds;
            string ivText = iv > 0 ? $"{iv:0.###}s" : "";
            Detail = $"{DateText}   ·   {FramesText}   ·   {SizeText}" + (ivText.Length > 0 ? $"   ·   {ivText}" : "");
            if (IsArchived)
            {
                long sz = SessionArchiver.GetArchiveSize(folder);   // one file stat — cheap per row
                Detail += sz > 0 ? $"   ·   {sz / 1048576.0:0.#} MB" : "";
            }
        }
    }
}
