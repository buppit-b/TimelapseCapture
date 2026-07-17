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
    /// the real Cull/Crop dialogs (they're folder-based, so they work on cold sessions), the app's
    /// encode settings editable inline (same live bindings as the main panel — one source of
    /// truth), and a live outcome line (frames → length, canvas). Encode only starts from the
    /// Combine button, with progress + cancel here. Sessions removed with ✕ are merely left out.
    /// </summary>
    public partial class CombineDialog : Window
    {
        private readonly MainViewModel _vm;
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
            DataContext = vm;   // the settings block binds straight to the app's encode settings

            foreach (var f in sessionFolders) _items.Add(new CombineItem(f));
            SortOldestFirst();
            list.ItemsSource = _items;

            _vm.PropertyChanged += OnVmChanged;
            Closed += (s, e) => _vm.PropertyChanged -= OnVmChanged;
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

        private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MainViewModel.EncodeFps) or nameof(MainViewModel.EncodeDurationMode)
                or nameof(MainViewModel.EncodeDurationSeconds) or nameof(MainViewModel.SpeedUpEnabled)
                or nameof(MainViewModel.EncodeEveryNth) or nameof(MainViewModel.EncodeHoldLastSeconds)
                or nameof(MainViewModel.EncodeFormat))
                RefreshOutcome();
        }

        // ---- the live outcome line + combine gating ----

        private (int totalFrames, int nth, double fps) Plan()
        {
            int total = _items.Sum(i => i.Frames);
            int nth = _vm.SpeedUpEnabled ? Math.Max(1, _vm.EncodeEveryNth) : 1;
            double fps = _vm.EncodeDurationMode
                ? VideoEncoder.FpsForDuration(total, nth, _vm.EncodeDurationSeconds)
                : Math.Max(1, _vm.EncodeFps);
            return (total, nth, fps);
        }

        private void RefreshOutcome()
        {
            if (_busy) return;
            var (total, nth, fps) = Plan();
            int kept = Math.Max(1, (total + nth - 1) / nth);
            double seconds = kept / fps + Math.Max(0, _vm.EncodeHoldLastSeconds);
            var canvas = VideoEncoder.CombineTargetSize(_items.Where(i => i.Frames > 0)
                .Select(i => (i.FrameSize, i.Crop)).ToList());

            resultText.Text = _items.Count == 0 ? "No sessions left to combine."
                : $"{_items.Count} sessions · {total} frames" + (nth > 1 ? $" → {kept} kept (1 in {nth})" : "")
                  + $" → ≈ {FormatLen(seconds)} at {fps:0.#} fps · canvas {canvas.Width}×{canvas.Height}"
                  + $" · {_vm.EncodeFormat.ToUpperInvariant()}";

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
                        fps, _vm.EncodePreset, _vm.EncodeCrf, token,
                        outputName: $"combined_{DateTime.Now:yyyyMMdd_HHmmss}",
                        onFrameProgress: n => Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (_busy) progressText.Text = $"Combining… {Math.Min(100, n * 100 / expectedOut)}%";
                        })),
                        everyNth: nth, holdLastSeconds: _vm.EncodeHoldLastSeconds, format: _vm.EncodeFormat,
                        gif: new VideoEncoder.GifOptions(_vm.GifMaxFps, _vm.GifMaxWidth, _vm.GifMaxColors, _vm.GifDither));
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
