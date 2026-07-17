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
        private readonly ObservableCollection<CombineItem> _items = new();
        private CancellationTokenSource? _cts;
        private bool _busy;
        private bool _closeWhenIdle;

        public CombineDialog(IEnumerable<string> sessionFolders, MainViewModel vm, string ffmpegPath)
        {
            InitializeComponent();
            _vm = vm;
            _ffmpegPath = ffmpegPath;
            _cfg = CombineSettings.SeededFrom(vm);
            DataContext = _cfg;   // this combine's settings only — the main window never moves

            foreach (var f in sessionFolders) _items.Add(new CombineItem(f));
            SortOldestFirst();
            list.ItemsSource = _items;

            _cfg.PropertyChanged += (s, e) => RefreshOutcome();
            RefreshOutcome();
        }

        private void SortOldestFirst()
        {
            var ordered = _items.OrderBy(i => i.SortKey).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                int cur = _items.IndexOf(ordered[i]);
                if (cur != i) _items.Move(cur, i);
            }
        }

        // ---- the live outcome line + combine gating ----

        private (int totalFrames, int nth, double fps) Plan()
        {
            int total = _items.Sum(i => i.Frames);
            int nth = _cfg.SpeedUpEnabled ? Math.Max(1, _cfg.SpeedUpN) : 1;
            double fps = _cfg.UnitIndex == 1
                ? VideoEncoder.FpsForDuration(total, nth, _cfg.DurationSeconds)
                : Math.Max(1, _cfg.Fps);
            return (total, nth, fps);
        }

        private void RefreshOutcome()
        {
            if (_busy) return;
            var (total, nth, fps) = Plan();
            int kept = Math.Max(1, (total + nth - 1) / nth);
            double seconds = kept / fps + Math.Max(0, _cfg.HoldLastSeconds);
            var canvas = VideoEncoder.CombineTargetSize(_items.Where(i => i.Frames > 0)
                .Select(i => (i.FrameSize, i.Crop)).ToList());

            resultText.Text = _items.Count == 0 ? "No sessions left to combine."
                : $"{_items.Count} sessions · {total} frames" + (nth > 1 ? $" → {kept} kept (1 in {nth})" : "")
                  + $" → ≈ {FormatLen(seconds)} at {fps:0.#} fps · canvas {canvas.Width}×{canvas.Height}"
                  + $" · {_cfg.Format.ToUpperInvariant()}";

            string? blocked =
                _items.Count < 2 ? "Needs at least two sessions."
                : _items.Any(i => i.Frames == 0) ? "A listed session has no frames — remove it (✕)."
                : null;
            combineBtn.IsEnabled = blocked == null;
            combineBtn.ToolTip = blocked ?? "Encode the list, top to bottom, into one video.";
        }

        private static string FormatLen(double s)
        {
            if (s < 60) return $"{s:0.#}s";
            int m = (int)(s / 60);
            return m < 60 ? $"{m}m {(int)(s % 60)}s" : $"{m / 60}h {m % 60}m";
        }

        // ---- per-row prep: the real dialogs, run against THAT session's folder ----

        private void OnRemoveRow(object sender, RoutedEventArgs e)
        {
            if (_busy || (sender as FrameworkElement)?.Tag is not CombineItem item) return;
            _items.Remove(item);
            RefreshOutcome();
        }

        private async void OnCullRow(object sender, RoutedEventArgs e)
        {
            if (_busy || (sender as FrameworkElement)?.Tag is not CombineItem item || item.Frames < 1) return;
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

        private async void OnCropRow(object sender, RoutedEventArgs e)
        {
            if (_busy || (sender as FrameworkElement)?.Tag is not CombineItem item || item.Frames < 1) return;
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
            if (_busy || _items.Count < 2 || _items.Any(i => i.Frames == 0)) return;
            var (total, nth, fps) = Plan();
            int expectedOut = Math.Max(1, (total + nth - 1) / nth);

            BeginBusy($"Combining {_items.Count} sessions…");
            var token = _cts!.Token;   // survives EndBusy's dispose — needed for the cancel check
            VideoEncoder.Result result;
            try
            {
                try
                {
                    result = await VideoEncoder.CombineAsync(_ffmpegPath, _items.Select(i => i.Folder).ToList(),
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
                MessageDialog.Show($"Combined {_items.Count} sessions ({total} frames):\n{result.OutputPath}",
                    "Sessions combined");
                DialogResult = true;   // done — back to the picker
            }
            else if (!token.IsCancellationRequested)   // deliberate cancel stays quiet
                MessageDialog.Show($"Combine failed — nothing was changed.\n\n{TailLine(result.Error)}",
                    "Combine sessions", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

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

    /// <summary>One staged session: fresh-from-disk facts, refreshable after a cull/crop.</summary>
    public sealed class CombineItem : System.ComponentModel.INotifyPropertyChanged
    {
        public string Folder { get; }
        public string Name { get; private set; } = "";
        public string Detail { get; private set; } = "";
        public int Frames { get; private set; }
        public System.Drawing.Size FrameSize { get; private set; }
        public System.Drawing.Rectangle? Crop { get; private set; }
        public DateTime SortKey { get; private set; }

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
            var files = SessionManager.GetFrameFiles(Folder);
            Frames = files.Length;
            FrameSize = System.Drawing.Size.Empty;
            if (files.Length > 0)
            {
                // Header-level read for the real frame size (regions can lie after a destructive crop).
                try { using var img = System.Drawing.Image.FromFile(files[0]); FrameSize = img.Size; } catch { }
            }
            string size = FrameSize.IsEmpty ? "?" : $"{FrameSize.Width}×{FrameSize.Height}";
            Detail = $"{Frames} frame{(Frames == 1 ? "" : "s")}   ·   {size}"
                + (Crop is { } c ? $"   ·   crop {c.Width}×{c.Height}" : "");
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(""));   // all props
        }
    }
}
