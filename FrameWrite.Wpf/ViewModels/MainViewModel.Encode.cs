using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using FrameWrite; // Core: settings, sessions, ffmpeg, capture engine, screen helper

namespace FrameWrite.Wpf.ViewModels
{
    /// <summary>
    /// MainViewModel — encode operations: encode/cancel, trim, cull, crop (incl. destructive), the
    /// ffmpeg download/browse/status plumbing.
    /// </summary>
    public partial class MainViewModel
    {
        private bool CanEncode => _session != null && _frameCount > 0 && !IsCapturing && !IsEncoding;

        private async Task EncodeOrCancel()
        {
            if (IsEncoding) { _encodeCts?.Cancel(); EncodeStatus = "Cancelling…"; return; }
            await Encode();
        }

        private async Task Trim()
        {
            if (_session == null || _sessionFolder == null || _frameCount < 1) return;
            // Offer the Stats target as a one-click range ("clip to your target") when it's meaningful.
            // With frame-skip active, hitting the target VIDEO length needs Nx as many input frames.
            long targetFrames = TargetKind == TargetVideo ? (long)_targetSeconds * Math.Max(1, EncodeFps) * Math.Max(1, EncodeEveryNth) : 0;
            int target = (targetFrames > 0 && targetFrames < _frameCount) ? (int)targetFrames : 0;
            var saved = SessionManager.LoadSession(_sessionFolder);
            var dlg = new TrimDialog(_sessionFolder, _frameCount, target,
                $"{_targetSeconds}s @ {Math.Max(1, EncodeFps)}fps" +
                (EncodeEveryNth > 1 ? $" · 1 in {EncodeEveryNth}" : ""),
                saved?.TrimStartFrame ?? 0, saved?.TrimEndFrame ?? 0)
            { Owner = Application.Current?.MainWindow };
            bool encode = dlg.ShowDialog() == true;

            // Persist marker placement even on close/cancel — coming back to trim shouldn't mean
            // re-placing markers (reload-set-save, frame-count safe).
            var s = SessionManager.LoadSession(_sessionFolder);
            if (s != null && (s.TrimStartFrame != dlg.StartFrame || s.TrimEndFrame != dlg.EndFrame))
            {
                s.TrimStartFrame = dlg.StartFrame;
                s.TrimEndFrame = dlg.EndFrame;
                SessionManager.SaveSession(_sessionFolder, s);
            }

            if (encode)
                await Encode(dlg.StartFrame, dlg.EndFrame);
        }

        // Review frames and delete fumbles, renumbering the rest so the sequence stays gapless (encodable).
        private async Task Cull()
        {
            if (_session == null || _sessionFolder == null || _frameCount < 1) return;
            var savedSession = SessionManager.LoadSession(_sessionFolder);
            var dlg = new CullDialog(_sessionFolder, _frameCount, savedSession?.CullMarkedFrames)
            { Owner = Application.Current?.MainWindow };
            bool apply = dlg.ShowDialog() == true && dlg.MarkedForDeletion.Count > 0;

            if (!apply)
            {
                // Closed without deleting — keep the marks for a return visit (same contract as trim).
                var keep = SessionManager.LoadSession(_sessionFolder);
                if (keep != null)
                {
                    keep.CullMarkedFrames = dlg.MarkedForDeletion.Count > 0
                        ? new List<int>(dlg.MarkedForDeletion) : null;
                    SessionManager.SaveSession(_sessionFolder, keep);
                }
                return;
            }

            // Deletes + renumbers on disk — busy-gated like the other destructive ops (and the
            // renumber pass on a big session is worth keeping off the UI thread anyway).
            int newCount;
            IsEncoding = true;
            EncodeStatus = "Deleting frames…";
            try
            {
                if (dlg.BackupFirstRequested && !await BackupSessionForSafety(_sessionFolder)) return;
                EncodeStatus = "Deleting frames…";
                var folder = _sessionFolder;
                newCount = await Task.Run(() => SessionManager.CullAndRenumber(folder, new HashSet<int>(dlg.MarkedForDeletion)));
                EncodeStatus = $"Deleted {dlg.MarkedForDeletion.Count} frame(s) ✓";
            }
            catch (Exception ex)
            {
                EncodeStatus = "Cull failed";
                MessageDialog.Show($"Couldn't delete the frames:\n{ex.Message}", "Cull frames",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;   // leave marks/trim as they are — the user can inspect what happened
            }
            finally { IsEncoding = false; }
            FrameCount = newCount;
            UpdatePreview();   // the "latest" frame likely changed
            NotifyBackupLocationIfAny();

            // Renumbering shifted every frame's position — the cull marks are consumed and saved trim
            // markers now point at the wrong frames; clear both rather than acting on stale positions.
            var s = SessionManager.LoadSession(_sessionFolder);
            if (s != null)
            {
                s.TrimStartFrame = 0;
                s.TrimEndFrame = 0;
                s.CullMarkedFrames = null;
                SessionManager.SaveSession(_sessionFolder, s);
            }
            CommandManager.InvalidateRequerySuggested();
        }

        // Crop the video to a frame area — non-destructive (stored per session, applied by ffmpeg at
        // encode) with a consented power-user option to crop the frames on disk instead.
        private async Task Crop()
        {
            if (_session == null || _sessionFolder == null || _frameCount < 1) return;
            var saved = SessionManager.LoadSession(_sessionFolder);
            var dlg = new CropDialog(_sessionFolder, saved?.EncodeCrop, _settings.OverlayTimestamp)
            { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true) return;

            if (dlg.DestructiveRequested && dlg.CropRect is { } rect)
            {
                // Re-writes every frame — run off the UI thread behind the busy flag so encode/trim/cull
                // and session switching are gated meanwhile.
                IsEncoding = true;
                EncodeStatus = "Cropping frames…";
                try
                {
                    // A failed backup must abort BEFORE any frame is touched (the return also skips
                    // the SetSessionCrop below — the session is exactly as it was).
                    if (dlg.BackupFirstRequested && !await BackupSessionForSafety(_sessionFolder)) return;
                    EncodeStatus = "Cropping frames…";
                    int done = await Task.Run(() => SessionManager.CropFrames(_sessionFolder, rect, _settings.JpegQuality));
                    EncodeStatus = $"Cropped {done} frame(s) ✓";
                    Logger.Log("Wpf", $"Destructive crop applied: {rect} → {done} frame(s).");
                }
                catch (Exception ex)
                {
                    EncodeStatus = "Crop failed";
                    MessageDialog.Show($"Couldn't crop the frames:\n{ex.Message}", "Crop", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { IsEncoding = false; }
                SetSessionCrop(null);   // the crop is baked into the frames now — encodes use the full (new) frame
                UpdatePreview();
                RefreshRegionScaleSuffix();   // canonical size changed — the region's scale note must follow
                NotifyBackupLocationIfAny();
            }
            else
            {
                SetSessionCrop(dlg.CropRect);   // Apply (a rect) or Clear (null)
            }
            RefreshCropInfo();
        }

        private void SetSessionCrop(System.Drawing.Rectangle? crop)
        {
            if (_sessionFolder == null) return;
            var s = SessionManager.LoadSession(_sessionFolder);
            if (s == null) return;
            s.EncodeCrop = crop;
            SessionManager.SaveSession(_sessionFolder, s);   // reload-set-save, frame-count safe
        }

        // "Crop: 800×600 at (100,50)" under the encode actions; empty when no crop is set.
        private string _cropInfoText = "";
        public string CropInfoText { get => _cropInfoText; private set => SetProperty(ref _cropInfoText, value); }
        public void RefreshCropInfo()
        {
            var c = _sessionFolder == null ? null : SessionManager.LoadSession(_sessionFolder)?.EncodeCrop;
            CropInfoText = c is { } r ? $"Crop: {r.Width}×{r.Height} at ({r.X},{r.Y})" : "";
        }

        private async Task Encode(int startFrame = 1, int endFrame = 0)
        {
            if (_session == null || _sessionFolder == null) return;

            var ffmpeg = FfmpegRunner.FindFfmpeg(_settings.FfmpegPath);
            if (string.IsNullOrEmpty(ffmpeg))
            {
                MessageDialog.Show("FFmpeg was not found. Configure or download it first.",
                    "FFmpeg not found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // A mixed-format session (a mid-session PNG toggle) can't encode — offer to unify it
            // instead of just refusing. Converts the odd ones out to the majority format, in place.
            // Safe here: CanEncode guarantees no capture is writing into this session.
            try
            {
                var formats = SessionManager.GetFrameFormatCounts(_sessionFolder);
                if (formats.Count > 1)
                {
                    string majority = formats.OrderByDescending(kv => kv.Value).First().Key;
                    // Only offer conversion INTO formats the app itself writes. A foreign session could
                    // be majority-bmp — converting into that would mislabel the bytes.
                    if (majority != "jpg" && majority != "png")
                    {
                        MessageDialog.Show(
                            $"This session mixes frame formats ({string.Join(" + ", formats.Select(kv => $"{kv.Value} {kv.Key.ToUpperInvariant()}"))}) — mixed sessions can't encode, and the dominant format isn't one this app can convert to.",
                            "Mixed frame formats", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    int odd = formats.Where(kv => kv.Key != majority).Sum(kv => kv.Value);
                    var r = MessageDialog.Show(
                        $"This session mixes frame formats ({string.Join(" + ", formats.Select(kv => $"{kv.Value} {kv.Key.ToUpperInvariant()}"))}) — mixed sessions can't encode.\n\n" +
                        $"Convert the {odd} odd frame(s) to {majority.ToUpperInvariant()} now? Frames are rewritten in place" +
                        (majority == "jpg" ? $" (PNG → JPEG at quality {_settings.JpegQuality})." : "."),
                        "Unify frame formats", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r != MessageBoxResult.Yes) return;

                    EncodeStatus = "Converting frames…";
                    int done = await Task.Run(() => SessionManager.ConvertFramesToFormat(_sessionFolder, majority, _settings.JpegQuality));
                    Logger.Log("Wpf", $"Unified session formats: {done} frame(s) → {majority}.");
                }
            }
            catch (Exception ex)
            {
                EncodeStatus = "Convert failed";
                MessageDialog.Show($"Couldn't unify the frame formats:\n{ex.Message}", "Unify frame formats",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _encodeCts = new System.Threading.CancellationTokenSource();
            IsEncoding = true;
            EncodeStatus = "Encoding…";
            EncodeProgress = 0;

            VideoEncoder.Result? result = null;
            bool cancelled = false;
            try
            {
                int maxFrames = (endFrame >= startFrame && endFrame > 0) ? endFrame - startFrame + 1 : 0;
                int nth = EncodeEveryNth;
                int inputFrames = maxFrames > 0 ? maxFrames : Math.Max(1, _frameCount - startFrame + 1);
                int totalFrames = Math.Max(1, (inputFrames + nth - 1) / nth);   // output frames after skip
                var crop = SessionManager.LoadSession(_sessionFolder)?.EncodeCrop;   // per-session crop (read fresh)
                // Duration mode: fps computed from WHATEVER is being encoded (full session or a trim
                // range), so "make it exactly 45s" holds for both.
                double fps = EncodeDurationMode
                    ? VideoEncoder.FpsForDuration(inputFrames, nth, EncodeDurationSeconds)
                    : EncodeFps;
                result = await VideoEncoder.EncodeAsync(ffmpeg, _sessionFolder, fps, EncodePreset, EncodeCrf,
                    _encodeCts.Token, startFrame, maxFrames, ResolveOutputName(),
                    onFrameProgress: n => Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // ffmpeg emits these ~twice a second; the callback arrives on a threadpool thread.
                        double pct = Math.Min(100.0, n * 100.0 / totalFrames);
                        EncodeProgress = pct;
                        if (IsEncoding && !(_encodeCts?.IsCancellationRequested ?? true))
                            EncodeStatus = $"Encoding… {pct:0}%";
                    })), everyNth: nth, holdLastSeconds: EncodeHoldLastSeconds, crop: crop);
            }
            catch (Exception ex)
            {
                EncodeStatus = "Encode failed";
                MessageDialog.Show($"Encode failed:\n{ex.Message}", "Encode", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Always clear the encoding state, even if EncodeAsync threw, so the button can't stick.
                cancelled = _encodeCts?.IsCancellationRequested ?? false;
                _encodeCts?.Dispose();
                _encodeCts = null;
                IsEncoding = false;
            }
            if (result == null) return; // exception path already reported

            if (cancelled)
            {
                TryDeletePartial(result.OutputPath);
                EncodeStatus = "Encode cancelled";
                return;
            }
            if (result.Success)
            {
                long bytes = 0;
                try { bytes = new FileInfo(result.OutputPath).Length; } catch { }
                EncodeStatus = $"Encoded ✓ ({FormatBytes(bytes)})";
                NotifyFinished();   // sound + taskbar flash — encodes can take a while
                bool open = _settings.OpenFolderAfterEncode ||
                    MessageDialog.Show($"Video encoded ({FormatBytes(bytes)}):\n{result.OutputPath}\n\nOpen the output folder?",
                        "Encode complete", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes;
                if (open)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = Path.GetDirectoryName(result.OutputPath)!,
                            UseShellExecute = true,
                        });
                    }
                    catch { /* ignore */ }
                }
            }
            else
            {
                TryDeletePartial(result.OutputPath);
                EncodeStatus = "Encode failed";
                MessageDialog.Show($"Encode failed:\n{result.Error}", "Encode", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string FormatBytes(long b)
            => b >= 1024 * 1024 ? $"{b / (1024.0 * 1024.0):F1} MB" : $"{b / 1024.0:F0} KB";

        private static void TryDeletePartial(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
        }

        private async Task DownloadFfmpeg()
        {
            string target = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
            _ffmpegCts = new System.Threading.CancellationTokenSource();
            IsFfmpegBusy = true;
            FfmpegStatus = "Starting download…";
            try
            {
                var path = await FfmpegDownloader.EnsureFfmpegPresentAsync(target, (d, t, status) =>
                {
                    if (!string.IsNullOrEmpty(status))
                        Application.Current?.Dispatcher.BeginInvoke(new Action(() => FfmpegStatus = status));
                }, _ffmpegCts.Token);
                if (!string.IsNullOrEmpty(path))
                {
                    _settings.FfmpegPath = path;
                    SettingsManager.Save(_settings);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Wpf", $"FFmpeg download failed: {ex.Message}");
            }
            finally
            {
                _ffmpegCts?.Dispose();
                _ffmpegCts = null;
                IsFfmpegBusy = false;
                CommandManager.InvalidateRequerySuggested();
            }
            // Let the downloader's last word ("FFmpeg already installed" / "Download complete") read
            // for a moment, THEN settle to Ready. Doing this inside finally raced the status callback's
            // queued BeginInvoke — the stale message landed after Ready and stuck forever.
            await Task.Delay(1500);
            if (!IsFfmpegBusy) RefreshFfmpegStatus();   // don't stomp a download the user restarted
        }

        private void BrowseFfmpeg()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select ffmpeg.exe",
                Filter = "ffmpeg|ffmpeg.exe|Executable files|*.exe|All files|*.*",
            };
            if (dlg.ShowDialog() == true)
            {
                if (!FfmpegDownloader.IsValidFfmpegExecutable(dlg.FileName))
                {
                    MessageDialog.Show("That file doesn't look like a working ffmpeg (it didn't respond to “-version”).",
                        "Select ffmpeg", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _settings.FfmpegPath = dlg.FileName;
                SettingsManager.Save(_settings);
                RefreshFfmpegStatus();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void RefreshFfmpegStatus()
        {
            var path = FfmpegRunner.FindFfmpeg(_settings.FfmpegPath);
            _ffmpegReady = !string.IsNullOrEmpty(path);
            FfmpegStatus = _ffmpegReady ? "Ready ✓" : "Not found";
            _ffmpegSetupExpanded = false;   // a fresh resolve folds the setup row back up
            OnPropertyChanged(nameof(FfmpegControlsVisible));
            OnPropertyChanged(nameof(FfmpegChangeVisible));
        }
    }
}
