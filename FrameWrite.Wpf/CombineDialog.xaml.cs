using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FrameWrite; // Core
using FrameWrite.Wpf.ViewModels;

namespace FrameWrite.Wpf
{
    /// <summary>
    /// Combine staging: the selected sessions listed oldest-first, each preparable IN PLACE with
    /// the real Cull/Crop dialogs (they're folder-based, so they work on cold sessions), encode
    /// settings editable inline, and a live outcome line (frames → length, canvas). Encode only
    /// starts from the Combine button, with progress + cancel here.
    ///
    /// The settings are a PER-COMBINE snapshot — seeded from the app's encode settings but applied
    /// to this run only. (v1 live-bound the app's own settings, which made the main window's
    /// encode panel visibly shuffle behind the dialog — functional, but it read as glitchy.)
    /// </summary>
    public partial class CombineDialog : Window
    {
        private readonly MainViewModel _vm;
        private readonly CombineSettings _cfg;
        private readonly string _ffmpegPath;
        private readonly string _capturesRoot;
        private readonly string? _currentSessionFolder;   // merge must never consume the loaded session
        private readonly ObservableCollection<CombineItem> _items = new();
        private CancellationTokenSource? _cts;
        private bool _busy;
        private bool _closeWhenIdle;

        public CombineDialog(string capturesRoot, IEnumerable<string> preselect, MainViewModel vm, string ffmpegPath,
            string? currentSessionFolder = null)
        {
            InitializeComponent();
            _vm = vm;
            _ffmpegPath = ffmpegPath;
            _capturesRoot = capturesRoot;
            _currentSessionFolder = currentSessionFolder;
            _cfg = CombineSettings.SeededFrom(vm);
            DataContext = _cfg;   // this combine's settings only — the main window never moves

            // EVERY session is a row (tick = include) — a wrong pick in the session list is fixed
            // here by ticking, not by starting over. Pre-tick what the picker had selected.
            var wanted = new HashSet<string>(preselect, StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var dir in Directory.Exists(capturesRoot) ? Directory.GetDirectories(capturesRoot) : Array.Empty<string>())
                {
                    if (SessionManager.LoadSession(dir) == null) continue;
                    var item = new CombineItem(dir);
                    item.Included = item.Eligible && wanted.Contains(dir);
                    item.PropertyChanged += OnItemChanged;   // ticking re-plans live
                    _items.Add(item);
                }
            }
            catch { /* best-effort listing — a bad folder shouldn't break staging */ }

            // Same sort control + saved preference as the session picker — the list orders the
            // SAME way in both, so a session picked there is where you expect it here. Display
            // order only: the encode itself always runs oldest -> newest.
            _sortReady = false;
            string by = vm.SessionSortBy;
            (by == "frames" ? sortFrames : by == "size" ? sortSize : by == "name" ? sortName : sortDate).IsChecked = true;
            sortDir.IsChecked = vm.SessionSortDescending;
            SyncSortGlyph();
            _sortReady = true;

            ApplySort();
            list.ItemsSource = _items;
            if (_items.Count > 0) list.SelectedIndex = 0;

            _cfg.PropertyChanged += (s, e) => RefreshOutcome();
            RefreshOutcome();
        }

        private bool _sortReady;

        private void OnSortChanged(object sender, RoutedEventArgs e)
        {
            if (!_sortReady) return;
            SyncSortGlyph();
            _vm.SessionSortBy = sortFrames.IsChecked == true ? "frames" : sortSize.IsChecked == true ? "size"
                : sortName.IsChecked == true ? "name" : "date";
            _vm.SessionSortDescending = sortDir.IsChecked == true;
            var keep = list.SelectedItem as CombineItem;
            ApplySort();
            if (keep != null) list.SelectedItem = keep;
        }

        private void SyncSortGlyph() => sortDir.Content = sortDir.IsChecked == true ? "↓" : "↑";

        private IReadOnlyList<CombineItem> Included() => _items.Where(i => i.Included).ToList();

        private void OnRowSelected(object sender, SelectionChangedEventArgs e) => UpdatePrepStrip();

        // Double-click anywhere on a row toggles its tick (the checkbox stays the visible truth).
        private void OnRowDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_busy && list.SelectedItem is CombineItem { Eligible: true } item)
                item.Included = !item.Included;
        }

        private void UpdatePrepStrip()
        {
            bool ok = !_busy && list.SelectedItem is CombineItem { Eligible: true, Frames: > 0 };
            cullBtn.IsEnabled = ok;
            cropBtn.IsEnabled = ok;
            prepHint.Text = list.SelectedItem is CombineItem it
                ? (it.Eligible ? $"acts on “{it.Name}”" : $"“{it.Name}” — {it.Reason}")
                : "highlight a session first";
        }

        private void ApplySort()
        {
            var cmp = LoadSessionDialog.SortComparer(
                sortFrames.IsChecked == true ? "frames" : sortSize.IsChecked == true ? "size"
                    : sortName.IsChecked == true ? "name" : "date",
                sortDir.IsChecked == true);
            var ordered = _items.OrderBy(i => (i.Name, i.SortKey, i.Frames, i.DiskBytes),
                Comparer<(string, DateTime, int, long)>.Create((a, b) => cmp(a, b))).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                int cur = _items.IndexOf(ordered[i]);
                if (cur != i) _items.Move(cur, i);
            }
        }

        // ---- the live outcome line + combine gating ----

        private (int totalFrames, int nth, double fps) Plan()
        {
            int total = Included().Sum(i => i.Frames);
            int nth = _cfg.SpeedUpEnabled ? Math.Max(1, _cfg.SpeedUpN) : 1;
            double fps = _cfg.UnitIndex == 1
                ? VideoEncoder.FpsForDuration(total, nth, _cfg.DurationSeconds)
                : Math.Max(1, _cfg.Fps);
            return (total, nth, fps);
        }

        private void RefreshOutcome()
        {
            if (_busy) return;
            var included = Included();
            var (total, nth, fps) = Plan();
            int kept = Math.Max(1, (total + nth - 1) / nth);
            double seconds = kept / fps + Math.Max(0, _cfg.HoldLastSeconds);
            var canvas = VideoEncoder.CombineTargetSize(included.Where(i => i.Frames > 0)
                .Select(i => (i.FrameSize, i.Crop)).ToList());

            resultText.Text = included.Count < 2
                ? $"Tick two or more sessions ({included.Count} ticked)."
                : $"{included.Count} sessions · {total} frames" + (nth > 1 ? $" → {kept} kept (1 in {nth})" : "")
                  + $" → ≈ {FormatLen(seconds)} at {fps:0.#} fps · canvas {canvas.Width}×{canvas.Height}"
                  + $" · {_cfg.Format.ToUpperInvariant()}";

            combineBtn.IsEnabled = included.Count >= 2 && included.Count <= 24;
            combineBtn.ToolTip = included.Count < 2 ? "Tick two or more sessions first."
                : included.Count > 24 ? "Combine supports up to 24 sessions per run."
                : "Encode the ticked sessions, oldest first, into one video.";

            // Merge needs true uniformity (one frame size + format) — that's what lets the merged
            // session keep recording. Gate live with the exact reason.
            string? mergeBlock =
                included.Count < 2 ? "Tick two or more sessions first."
                : included.Select(i => i.Ext).Distinct().Count() > 1 ? "The ticked sessions use different frame formats — a merged session must be uniform."
                : included.Select(i => i.FrameSize).Distinct().Count() > 1 ? "The ticked sessions have different frame sizes — a merged session must be uniform. (Destructive crop can equalize them.)"
                : IncludesCurrent(included) ? "The currently loaded session is ticked — merge would pull the frames out from under the main window. Load another session first."
                : null;
            mergeBtn.IsEnabled = mergeBlock == null;
            mergeBtn.ToolTip = mergeBlock ??
                "Merge the ticked sessions' FRAMES into one new session (oldest first, renumbered) that can keep recording like any other. You choose Move (no extra disk, sources consumed) or Copy (sources kept).";
            UpdatePrepStrip();
        }

        private static string FormatLen(double s)
        {
            if (s < 60) return $"{s:0.#}s";
            int m = (int)(s / 60);
            return m < 60 ? $"{m}m {(int)(s % 60)}s" : $"{m / 60}h {m % 60}m";
        }

        // ---- per-row prep: the real dialogs, run against THAT session's folder ----

        private async void OnCullSelected(object sender, RoutedEventArgs e)
        {
            if (_busy || list.SelectedItem is not CombineItem { Eligible: true } item || item.Frames < 1) return;
            var saved = SessionManager.LoadSession(item.Folder);
            var dlg = new CullDialog(item.Folder, item.Frames, saved?.CullMarkedFrames) { Owner = this };
            bool apply = dlg.ShowDialog() == true && dlg.MarkedForDeletion.Count > 0;
            if (!apply)
            {
                // Closed without deleting — keep the marks for a return visit (same contract as the VM).
                var keep = SessionManager.LoadSession(item.Folder);
                if (keep != null)
                {
                    keep.CullMarkedFrames = dlg.MarkedForDeletion.Count > 0
                        ? new List<int>(dlg.MarkedForDeletion) : null;
                    SessionManager.SaveSession(item.Folder, keep);
                }
                return;
            }

            BeginBusy($"Deleting {dlg.MarkedForDeletion.Count} frame(s) from “{item.Name}”…");
            string? backupPath = null;
            try
            {
                if (dlg.BackupFirstRequested && (backupPath = await RunBackup(item)) == null) return;
                await Task.Run(() => SessionManager.CullAndRenumber(item.Folder, new HashSet<int>(dlg.MarkedForDeletion)));
                // Renumbering shifted positions — stale trim markers / consumed marks must not survive.
                var s = SessionManager.LoadSession(item.Folder);
                if (s != null)
                {
                    s.TrimStartFrame = 0;
                    s.TrimEndFrame = 0;
                    s.CullMarkedFrames = null;
                    SessionManager.SaveSession(item.Folder, s);
                }
            }
            catch (Exception ex)
            {
                MessageDialog.Show($"Couldn't delete the frames:\n{ex.Message}", "Cull frames",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { EndBusy(item); }
            if (backupPath != null)
                MessageDialog.Show($"Backup saved first:\n{backupPath}", "Cull frames");
        }

        private async void OnCropSelected(object sender, RoutedEventArgs e)
        {
            if (_busy || list.SelectedItem is not CombineItem { Eligible: true } item || item.Frames < 1) return;
            var saved = SessionManager.LoadSession(item.Folder);
            var dlg = new CropDialog(item.Folder, saved?.EncodeCrop, _vm.OverlayTimestamp) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            if (dlg.DestructiveRequested && dlg.CropRect is { } rect)
            {
                BeginBusy($"Cropping “{item.Name}” frames on disk…");
                string? backupPath = null;
                bool cropped = false;
                try
                {
                    if (dlg.BackupFirstRequested && (backupPath = await RunBackup(item)) == null) return;
                    int q = saved?.JpegQuality > 0 ? saved.JpegQuality : 90;   // the session's own quality
                    await Task.Run(() => SessionManager.CropFrames(item.Folder, rect, q));
                    cropped = true;
                }
                catch (Exception ex)
                {
                    MessageDialog.Show($"Couldn't crop the frames:\n{ex.Message}", "Crop",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { EndBusy(item); }
                // Baked in → the saved non-destructive crop must not apply on top of it.
                if (cropped) SaveCrop(item.Folder, null);
                if (backupPath != null)
                    MessageDialog.Show($"Backup saved first:\n{backupPath}", "Crop");
            }
            else
            {
                SaveCrop(item.Folder, dlg.CropRect);   // Apply (a rect) or Clear (null)
                item.Refresh();
                RefreshOutcome();
            }
        }

        private static void SaveCrop(string folder, System.Drawing.Rectangle? crop)
        {
            var s = SessionManager.LoadSession(folder);
            if (s == null) return;
            s.EncodeCrop = crop;
            SessionManager.SaveSession(folder, s);
        }

        /// <summary>Backup with footer progress; null (after showing the error) means ABORT the op.</summary>
        private async Task<string?> RunBackup(CombineItem item)
        {
            try
            {
                progressText.Text = $"Backing up “{item.Name}”…";
                return await Task.Run(() => SessionManager.BackupSession(item.Folder, (done, total) =>
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_busy) progressText.Text = $"Backing up “{item.Name}”… {done}/{total}";
                    }))));
            }
            catch (Exception ex)
            {
                MessageDialog.Show($"Backup failed — nothing was changed:\n{ex.Message}", "Backup",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }

        // ---- the combine itself ----

        private async void OnCombine(object sender, RoutedEventArgs e)
        {
            var included = Included();
            if (_busy || included.Count < 2 || included.Count > 24 || included.Any(i => i.Frames == 0)) return;
            var (total, nth, fps) = Plan();
            int expectedOut = Math.Max(1, (total + nth - 1) / nth);

            BeginBusy($"Combining {included.Count} sessions…");
            var token = _cts!.Token;   // survives EndBusy's dispose — needed for the cancel check
            VideoEncoder.Result result;
            try
            {
                try
                {
                    result = await VideoEncoder.CombineAsync(_ffmpegPath,
                        included.OrderBy(i => i.SortKey).Select(i => i.Folder).ToList(),   // the video is ALWAYS oldest -> newest
                        fps, _cfg.Preset, _cfg.Crf, token,
                        outputName: $"combined_{DateTime.Now:yyyyMMdd_HHmmss}",
                        onFrameProgress: n => Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (_busy) progressText.Text = $"Combining… {Math.Min(100, n * 100 / expectedOut)}%";
                        })),
                        everyNth: nth, holdLastSeconds: _cfg.HoldLastSeconds, format: _cfg.Format,
                        gif: new VideoEncoder.GifOptions(_cfg.GifMaxFps, _cfg.GifMaxWidth, _cfg.GifColors, _cfg.GifDither));
                }
                catch (Exception ex) { result = new VideoEncoder.Result { Success = false, Error = ex.Message }; }
            }
            finally { EndBusy(null); }
            if (_closeWhenIdle) return;

            if (result.Success)
            {
                MessageDialog.Show($"Combined {included.Count} sessions ({total} frames):\n{result.OutputPath}",
                    "Sessions combined");
                DialogResult = true;   // done — back to the picker
            }
            else if (!token.IsCancellationRequested)   // deliberate cancel stays quiet
                MessageDialog.Show($"Combine failed — nothing was changed.\n\n{TailLine(result.Error)}",
                    "Combine sessions", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ---- merge: the ticked sessions' frames become ONE continuable session ----

        private async void OnMerge(object sender, RoutedEventArgs e)
        {
            var included = Included().OrderBy(i => i.SortKey).ToList();
            if (_busy || included.Count < 2 || IncludesCurrent(included)) return;
            if (included.Select(i => i.Ext).Distinct().Count() > 1 || included.Select(i => i.FrameSize).Distinct().Count() > 1) return;

            int total = included.Sum(i => i.Frames);
            long bytes = 0;
            foreach (var it in included)
                foreach (var f in SessionManager.GetFrameFiles(it.Folder))
                    try { bytes += new FileInfo(f).Length; } catch { }

            int choice = MessageDialog.ShowChoices(
                $"Merge {included.Count} sessions into ONE session ({total} frames, renumbered oldest first)?\n\n" +
                "The merged session loads and keeps recording like any other.\n\n" +
                $"Move — frames are moved: no extra disk; the source sessions are consumed (their videos carry over).\n" +
                $"Copy — the sources stay untouched: needs ~{bytes / 1048576.0:0.#} MB more disk.",
                "Merge sessions", MessageBoxImage.Question,
                "Move (no extra disk)", "Copy (keep sources)", "Cancel");
            if (choice is not (0 or 1)) return;
            bool move = choice == 0;

            BeginBusy($"Merging {included.Count} sessions…");
            string? merged = null, error = null;
            try
            {
                var folders = included.Select(i => i.Folder).ToList();
                merged = await Task.Run(() => SessionManager.MergeSessions(folders, _capturesRoot, move,
                    (done, tot) => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_busy) progressText.Text = $"Merging… {done}/{tot} frames";
                    }))));
            }
            catch (Exception ex) { error = ex.Message; }
            finally { EndBusyAfterMerge(merged); }
            if (_closeWhenIdle) return;

            if (merged != null)
                MessageDialog.Show(
                    $"Merged {included.Count} sessions into “{SessionManager.LoadSession(merged)?.Name}” ({total} frames)." +
                    (move ? "\nThe source sessions were consumed (their videos moved across)." : "\nThe source sessions are untouched.") +
                    "\nLoad it from the session list to keep recording into it.",
                    "Sessions merged");
            else
                MessageDialog.Show($"Merge failed:\n{error}", "Merge sessions",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // A merge changes the world (sources may be gone; a new session exists) — re-enumerate
        // rather than patch, keeping surviving ticks and highlighting the merged result.
        private void EndBusyAfterMerge(string? mergedFolder)
        {
            var stillTicked = Included().Select(i => i.Folder).Where(Directory.Exists).ToList();
            _busy = false;
            _cts?.Dispose();
            _cts = null;
            if (_closeWhenIdle) { Close(); return; }
            list.IsEnabled = true;
            prepStrip.IsEnabled = true;
            settingsPanel.IsEnabled = true;
            progressText.Text = "";

            foreach (var it in _items) it.PropertyChanged -= OnItemChanged;
            _items.Clear();
            try
            {
                foreach (var dir in Directory.Exists(_capturesRoot) ? Directory.GetDirectories(_capturesRoot) : Array.Empty<string>())
                {
                    if (SessionManager.LoadSession(dir) == null) continue;
                    var item = new CombineItem(dir);
                    item.Included = item.Eligible && stillTicked.Contains(dir, StringComparer.OrdinalIgnoreCase);
                    item.PropertyChanged += OnItemChanged;
                    _items.Add(item);
                }
            }
            catch { }
            ApplySort();
            if (mergedFolder != null)
                list.SelectedItem = _items.FirstOrDefault(i => string.Equals(i.Folder, mergedFolder, StringComparison.OrdinalIgnoreCase));
            RefreshOutcome();
        }

        private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshOutcome();

        private bool IncludesCurrent(IEnumerable<CombineItem> items) => _currentSessionFolder != null
            && items.Any(i => string.Equals(i.Folder, _currentSessionFolder, StringComparison.OrdinalIgnoreCase));

        // ffmpeg errors run pages long — the tail line is the useful one.
        private static string TailLine(string error)
        {
            var lines = (error ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("frame=", StringComparison.Ordinal)).ToArray();
            return lines.Length == 0 ? error ?? "" : lines[^1];
        }

        // ---- busy plumbing (one op at a time; Close/X cancels a running combine safely) ----

        private void BeginBusy(string status)
        {
            _busy = true;
            _cts = new CancellationTokenSource();
            list.IsEnabled = false;
            prepStrip.IsEnabled = false;
            settingsPanel.IsEnabled = false;
            combineBtn.IsEnabled = false;
            progressText.Text = status;
        }

        private void EndBusy(CombineItem? refreshItem)
        {
            _busy = false;
            _cts?.Dispose();
            _cts = null;
            if (_closeWhenIdle) { Close(); return; }
            list.IsEnabled = true;
            prepStrip.IsEnabled = true;
            settingsPanel.IsEnabled = true;
            progressText.Text = "";
            refreshItem?.Refresh();
            RefreshOutcome();
        }

        private void OnCloseBtn(object sender, RoutedEventArgs e)
        {
            if (_busy) { _cts?.Cancel(); return; }   // first press cancels the running op
            DialogResult = false;
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_busy) return;
            e.Cancel = true;         // finish unwinding first, then close — no dead-window continuations
            _closeWhenIdle = true;
            _cts?.Cancel();
        }
    }

    /// <summary>
    /// This combine's encode settings: seeded from the app's, mutated freely here, persisted
    /// nowhere. Setters clamp; the encoder validates again anyway.
    /// </summary>
    public sealed class CombineSettings : ViewModelBase
    {
        private string _format = "mp4";
        private int _unitIndex;
        private int _fps = 30;
        private double _durationSeconds = 30;
        private int _crf = 23;
        private string _preset = "medium";
        private int _gifColors = 256;
        private string _gifDither = "bayer";
        private int _gifMaxFps = 15;
        private int _gifMaxWidth = 720;
        private bool _speedUpEnabled;
        private int _speedUpN = 2;
        private double _holdLastSeconds;

        public static CombineSettings SeededFrom(MainViewModel vm) => new()
        {
            Format = vm.EncodeFormat,
            UnitIndex = vm.EncodeUnitIndex == "1" ? 1 : 0,   // the VM stores the seg index as a string
            Fps = vm.EncodeFps,
            DurationSeconds = vm.EncodeDurationSeconds,
            Crf = vm.EncodeCrf,
            Preset = vm.EncodePreset,
            GifColors = vm.GifMaxColors,
            GifDither = vm.GifDither,
            GifMaxFps = vm.GifMaxFps,
            GifMaxWidth = vm.GifMaxWidth,
            SpeedUpEnabled = vm.SpeedUpEnabled,
            SpeedUpN = Math.Max(2, vm.EncodeEveryNth),
            HoldLastSeconds = vm.EncodeHoldLastSeconds,
        };

        public string Format
        {
            get => _format;
            set { if (SetProperty(ref _format, (value ?? "mp4").ToLowerInvariant())) { OnPropertyChanged(nameof(IsGif)); OnPropertyChanged(nameof(ShowTuning)); } }
        }
        public int UnitIndex
        {
            get => _unitIndex;
            set { if (SetProperty(ref _unitIndex, Math.Clamp(value, 0, 1))) { OnPropertyChanged(nameof(ShowFps)); OnPropertyChanged(nameof(ShowLength)); } }
        }
        public int Fps { get => _fps; set => SetProperty(ref _fps, Math.Clamp(value, 1, 240)); }
        public double DurationSeconds { get => _durationSeconds; set => SetProperty(ref _durationSeconds, Math.Clamp(value, 0.1, 86400)); }
        public int Crf { get => _crf; set => SetProperty(ref _crf, Math.Clamp(value, 0, 51)); }
        public string Preset { get => _preset; set => SetProperty(ref _preset, value ?? "medium"); }
        public int GifColors { get => _gifColors; set => SetProperty(ref _gifColors, value is 32 or 64 or 128 or 256 ? value : 256); }
        public string GifDither { get => _gifDither; set => SetProperty(ref _gifDither, value ?? "bayer"); }
        public int GifMaxFps { get => _gifMaxFps; set => SetProperty(ref _gifMaxFps, Math.Clamp(value, 1, 50)); }
        public int GifMaxWidth { get => _gifMaxWidth; set => SetProperty(ref _gifMaxWidth, Math.Clamp(value, 120, 3840)); }
        public bool SpeedUpEnabled { get => _speedUpEnabled; set => SetProperty(ref _speedUpEnabled, value); }
        public int SpeedUpN { get => _speedUpN; set => SetProperty(ref _speedUpN, Math.Clamp(value, 2, 1000)); }
        public double HoldLastSeconds { get => _holdLastSeconds; set => SetProperty(ref _holdLastSeconds, Math.Clamp(value, 0, 600)); }

        public bool IsGif => Format == "gif";
        public bool ShowTuning => Format != "gif";
        public bool ShowFps => UnitIndex == 0;
        public bool ShowLength => UnitIndex == 1;
    }

    /// <summary>One staged session: fresh-from-disk facts, refreshable after a cull/crop.
    /// Included is the tick; Eligible=false rows (archived / recording / empty) grey out with the
    /// reason in their detail line and can't be ticked.</summary>
    public sealed class CombineItem : System.ComponentModel.INotifyPropertyChanged
    {
        public string Folder { get; }
        public string Name { get; private set; } = "";
        public string Detail { get; private set; } = "";
        public int Frames { get; private set; }
        public System.Drawing.Size FrameSize { get; private set; }
        public System.Drawing.Rectangle? Crop { get; private set; }
        public DateTime SortKey { get; private set; }
        public bool Eligible { get; private set; }
        public string Reason { get; private set; } = "";
        /// <summary>Frame extension (".jpg"), for merge's uniformity gate. Empty when frameless.</summary>
        public string Ext { get; private set; } = "";
        /// <summary>Bytes of frames on disk — the "Size" sort key and the size shown per row.</summary>
        public long DiskBytes { get; private set; }
        public System.Windows.Media.ImageSource? Thumbnail { get; private set; }

        private bool _included;
        public bool Included
        {
            get => _included;
            set
            {
                if (_included == value || (value && !Eligible)) return;
                _included = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Included)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public CombineItem(string folder)
        {
            Folder = folder;
            Refresh();
        }

        public void Refresh()
        {
            var s = SessionManager.LoadSession(Folder);
            Name = string.IsNullOrWhiteSpace(s?.Name) ? Path.GetFileName(Folder) : s!.Name!;
            SortKey = s?.StartTime ?? DateTime.MinValue;
            Crop = s?.EncodeCrop;
            Thumbnail = FramePreview.LoadLatest(Folder, 120);
            var files = SessionManager.GetFrameFiles(Folder);
            Frames = files.Length;
            FrameSize = System.Drawing.Size.Empty;
            Ext = files.Length > 0 ? Path.GetExtension(files[0]).ToLowerInvariant() : "";
            long bytes = 0;
            foreach (var f in files) { try { bytes += new FileInfo(f).Length; } catch { } }
            DiskBytes = bytes;
            if (files.Length > 0)
            {
                // Header-level read for the real frame size (regions can lie after a destructive crop).
                try { using var img = System.Drawing.Image.FromFile(files[0]); FrameSize = img.Size; } catch { }
            }

            Reason = s?.Archived == true ? "archived — unarchive first"
                : s?.Active == true ? "marked as recording"
                : Frames == 0 ? "no frames"
                : "";
            Eligible = Reason.Length == 0;
            if (!Eligible && _included) _included = false;   // e.g. culled to zero mid-staging

            string size = FrameSize.IsEmpty ? "?" : $"{FrameSize.Width}×{FrameSize.Height}";
            Detail = $"{SortKey:dd MMM}   ·   {Frames} frame{(Frames == 1 ? "" : "s")}   ·   {size}   ·   {SessionListItem.FmtBytes(DiskBytes)}"
                + (Crop is { } c ? $"   ·   crop {c.Width}×{c.Height}" : "")
                + (Eligible ? "" : $"   ·   {Reason}");
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(""));   // all props
        }
    }
}
