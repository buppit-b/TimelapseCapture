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
    /// MainViewModel — the capture lifecycle: start pre-flights, engine arm (canonical-size lock),
    /// stop/pause, tracked-window pinning, engine event handlers, and capture-failure surfacing.
    /// </summary>
    public partial class MainViewModel
    {

        private void StartCapture()
        {
            if (_session == null || _sessionFolder == null || !_region.HasValue) return;
            // Also reachable via hotkey/tray, which skip the command's CanExecute: never start while an
            // encode OR an on-disk rewrite (destructive crop, overlay bake) is running — capturing into a
            // session whose frames are being rewritten would mix sizes / skip frames mid-operation.
            if (IsEncoding) return;

            // Pre-flight the low-disk safety limit (always enforced; the threshold is set in Settings).
            long freeNow = SystemMonitor.GetAvailableDiskSpaceMB(_sessionFolder);
            if (_settings.AutoStopOnLowDisk && freeNow > 0 && freeNow < _settings.LowDiskStopMB)
            {
                var r = MessageDialog.Show(
                    $"Only {freeNow} MB free on the capture drive — below your {_settings.LowDiskStopMB} MB low-disk limit, so the run would stop almost immediately.\n\nChange the limit in Settings, or start anyway?",
                    "Low disk space", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
            }

            // Pre-flight the storage RATE: a fast interval over a large region can fill the drive in
            // minutes. Warn (once, at Start) if the current settings would fill it in under an hour.
            var (mbPerHour, fillHours) = EstimateStorageRate(0);
            if (mbPerHour > 0 && fillHours < 1)
            {
                string rate = mbPerHour >= 1024 ? $"{mbPerHour / 1024.0:F1} GB/hour" : $"{mbPerHour:F0} MB/hour";
                var r = MessageDialog.Show(
                    $"At this interval and capture size you'll write about {rate} — this drive would fill in roughly {HumanDuration(fillHours * 3600)}.\n\nConsider a longer interval or a smaller region. Start anyway?",
                    "That's a lot of storage", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
            }

            // Pre-flight the ACCUMULATED-state auto-stops. Without this, restarting a session that has
            // already hit its target / max-duration / storage budget would start, then the next stats
            // tick (~0.5s) would auto-stop it again with just the finish sound — looking like a bug
            // ("start, then stop with an error noise"). Tell the user what to change instead.
            if (AccumulatedStopAlreadyMet(out string blockMsg))
            {
                MessageDialog.Show(blockMsg, "Can't start — a stop limit is already reached",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ClearCaptureError();
            _consecutiveCaptureFailures = 0;
            _lastPreviewedFrame = -1;   // force the first captured frame to refresh the preview
            try
            {
                PersistRegion(_region.Value); // ensure the active region (incl. a relocated one) is saved
                SetSessionActive(true);       // a session left Active at launch = the app died mid-capture
                StartEngine();                // creates the frames folder — can throw if the path is unwritable
            }
            catch (Exception ex)
            {
                Logger.Log("Wpf", $"Start failed: {ex.Message}");
                CaptureError = $"Couldn't start capture: {ex.Message}\nCheck the output folder exists and is writable.";
                return;
            }
            IsCapturing = true;
            IsPaused = false;
            PinTrackedWindow();   // optionally keep the tracked window above everything while capturing
            PlayStartStopCue();
        }

        // User-initiated stop (Stop button / hotkey): plays the start/stop cue. Auto-stops call
        // StopCapture directly (they have their own finish notification), so no double sound.
        private void StopByUser()
        {
            if (!IsCapturing) return;
            // Stopping no longer nags about an armed recording timer: the timer accumulates ACROSS
            // stops now, so a stop just pauses progress toward the goal (a restart continues it) —
            // the explicit "Reset timer" button is the only thing that discards progress.
            StopCapture();
            PlayStartStopCue();
        }

        // True (with a user-facing message) when an opt-in accumulated-state stop is ALREADY satisfied,
        // so starting would immediately auto-stop again. Checked at Start (mirrors the low-disk pre-flight).
        private bool AccumulatedStopAlreadyMet(out string message)
        {
            message = "";
            // Input frames needed for a _targetSeconds video — with frame-skip you need N× as many.
            long targetFrames = (long)_targetSeconds * Math.Max(1, EncodeFps) * Math.Max(1, EncodeEveryNth);
            if (TargetKind == TargetVideo && _settings.StopAtTarget && targetFrames > 0 && _frameCount >= targetFrames)
            {
                message = $"This session already has {_frameCount} frames — at or beyond your target of {targetFrames} " +
                          $"({_targetSeconds}s @ {EncodeFps}fps), and “Stop at target” is on.\n\n" +
                          "Raise the target or turn off “Stop at target” to keep capturing, or start a new session.";
                return true;
            }
            if (_settings.MaxDurationEnabled && _accumulatedSeconds >= _settings.MaxDurationMinutes * 60.0)
            {
                message = $"This session has already recorded {FormatTime(_accumulatedSeconds)} — at or beyond your " +
                          $"{_settings.MaxDurationMinutes}-minute maximum.\n\n" +
                          "Raise the limit or turn off “Stop after a maximum duration”, or start a new session.";
                return true;
            }
            if (_settings.StopAtStorageEnabled && _sessionFolder != null && _frameCount > 0)
            {
                double sessionMb = SystemMonitor.GetActualAverageFrameSizeKB(_sessionFolder) * _frameCount / 1024.0;
                if (sessionMb >= _settings.StopAtStorageMB)
                {
                    message = $"This session's frames already use about {sessionMb:F0} MB — at or beyond your " +
                              $"{_settings.StopAtStorageMB} MB budget.\n\n" +
                              "Raise the budget or turn off “Stop at a storage budget”, or start a new session.";
                    return true;
                }
            }
            if (TargetKind == TargetSize && _sessionFolder != null && _frameCount > 0)
            {
                double sessionMb = SystemMonitor.GetActualAverageFrameSizeKB(_sessionFolder) * _frameCount / 1024.0;
                if (sessionMb >= _targetSizeMB)
                {
                    message = $"This session's frames already use about {sessionMb:F0} MB — at or beyond your " +
                              $"{FormatBudget(_targetSizeMB)} size target.\n\n" +
                              "Raise the size target (or switch target kind) to keep capturing, or start a new session.";
                    return true;
                }
            }
            return false;
        }

        // Force the tracked window topmost (if opted in) / release it. Used by start/stop AND pause/resume
        // so a paused capture doesn't leave the window jammed on top.
        private void PinTrackedWindow()
        {
            if (_trackedWindow == IntPtr.Zero || !_settings.KeepTrackedWindowOnTop) return;

            // NEVER pin a fullscreen-sized window (fullscreen/borderless game): topmost on it hijacks the
            // desktop — it stays above everything even after alt-tab, and the user can't reach any other
            // window (including this app) to stop the capture. It gains nothing anyway: a focused
            // fullscreen surface can't be occluded.
            if (WindowEnumerator.CoversFullMonitor(_trackedWindow))
            {
                Logger.Log("Wpf", "Keep-on-top skipped: the tracked window is fullscreen-sized (pinning it would block alt-tab).");
                return;
            }

            WindowEnumerator.SetTopmost(_trackedWindow, true);
            _pinnedIdentity = WindowEnumerator.GetWindowIdentity(_trackedWindow);   // remember what we pinned
            _trackedWindowMadeTopmost = true;
        }

        private void UnpinTrackedWindow()
        {
            if (_trackedWindowMadeTopmost)
            {
                // Only demote if it's still the same window we pinned — the HWND could have been closed and
                // recycled onto a different window, which we must not touch.
                if (WindowEnumerator.GetWindowIdentity(_trackedWindow) == _pinnedIdentity)
                    WindowEnumerator.SetTopmost(_trackedWindow, false);
                _trackedWindowMadeTopmost = false;
                _pinnedIdentity = "";
            }
        }

        // Start (or resume) the capture engine with current settings and begin a new timing segment.
        private void StartEngine()
        {
            // Refresh the session from disk so the engine's frame-number baseline (FramesCaptured) matches
            // the actual on-disk count. On RESUME the VM's _session is stale — the engine mutates its OWN
            // copy of the session during a run, so the VM's still reads the pre-run count. Without this the
            // numbering would restart and OVERWRITE already-captured frames (data loss + a gapped sequence
            // that breaks the encode). Idempotent on first start (SetSessionActive already reloaded).
            if (_sessionFolder != null)
                _session = SessionManager.LoadSession(_sessionFolder) ?? _session;

            // Frames already on disk define the session's canonical size. If the current region — or a
            // tracked window's locked size — differs (the original monitor is gone, or a differently-
            // sized window is tracked), output is locked to the canonical size so every frame stays
            // uniform and the encode stays valid.
            System.Drawing.Size scaleTo = default;
            if (_frameCount > 0 && _sessionFolder != null && _region.HasValue)
            {
                var canonical = SessionManager.GetFrameSize(_sessionFolder);
                if (canonical.Width >= 2 && canonical.Height >= 2 && canonical != _region.Value.Size)
                    scaleTo = canonical;
            }

            _engine.Start(_sessionFolder!, _session!, _region!.Value, (double)IntervalSeconds, _settings.Format ?? "JPEG",
                _settings.SmartIntervalEnabled, (double)_settings.IdleIntervalSeconds,
                _settings.IdleThresholdSeconds, _settings.SkipIdleFrames, _settings.JpegQuality,
                _settings.CaptureCursor, BuildOverlay(), _trackedWindow, _settings.PauseOnTrackedMinimize,
                _settings.TrackResizeMode, scaleOutputTo: scaleTo);
            _captureStart = DateTime.Now;
            _timerRunBase = _accumulatedSeconds;   // the rec-timer counts THIS run's active time from here
            PrimeCadence();                        // don't bill the stop→start gap as one giant rate spike
            SmartStatus = _settings.SmartIntervalEnabled ? "Active" : "";
        }

        private void PauseResume()
        {
            if (!IsCapturing) return;
            if (_isPaused)
            {
                _engine.Resume();        // instant — the engine was never torn down
                PinTrackedWindow();      // re-pin on resume (released while paused)
                _captureStart = DateTime.Now;   // the run clock resumes now
                SmartStatus = _settings.SmartIntervalEnabled ? "Active" : "";
                IsPaused = false;
            }
            else
            {
                _engine.Pause();         // keep the timer + activity hooks alive; just stop capturing
                UnpinTrackedWindow();    // don't leave the window jammed on top while paused
                if (_captureStart.HasValue)
                {
                    _accumulatedSeconds += (DateTime.Now - _captureStart.Value).TotalSeconds;
                    _captureStart = null;   // freeze the run clock while paused
                }
                SmartStatus = "Paused";
                IsPaused = true;
            }
        }

        private void StopCapture()
        {
            ClearCaptureError();   // a manual stop clears any warning; the auto-stop path re-sets it after this
            UnpinTrackedWindow();  // release the tracked window we pinned on top
            _engine.Stop();
            if (_captureStart.HasValue)
            {
                _accumulatedSeconds += (DateTime.Now - _captureStart.Value).TotalSeconds;
                _captureStart = null;
            }
            SmartStatus = "";
            IsPaused = false;
            IsCapturing = false;
            ActualFpsText = "";   // stop showing the live achieved-fps figure once capture ends
            PersistTotalTime();
            UpdatePreview();
        }

        // Persist cumulative capture time to the session without clobbering the engine's on-disk
        // frame count: reload the latest session, set only TotalCaptureSeconds, save it back.
        private void PersistTotalTime()
        {
            if (_sessionFolder == null) return;
            try
            {
                var s = SessionManager.LoadSession(_sessionFolder);
                if (s == null) return;
                s.TotalCaptureSeconds = _accumulatedSeconds;
                s.Active = false; // clean stop — mark inactive so it isn't seen as interrupted
                SessionManager.SaveSession(_sessionFolder, s);
                _session = s;
            }
            catch { /* best-effort; never throw out of Stop */ }
        }

        private bool CanOpenFolder =>
            (_sessionFolder != null && Directory.Exists(_sessionFolder)) || HasOutputFolder;

        private void OpenSessionFolder()
        {
            // Open the active session folder if there is one, else the configured output folder.
            string? path = (_sessionFolder != null && Directory.Exists(_sessionFolder))
                ? _sessionFolder
                : (HasOutputFolder ? _settings.SaveFolder : null);
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Logger.Log("Wpf", $"Open folder failed: {ex.Message}");
            }
        }

        // Visible capture-failure banner (empty = no problem). Set when saving frames fails.
        private string _captureError = "";
        public string CaptureError
        {
            get => _captureError;
            set { if (SetProperty(ref _captureError, value)) OnPropertyChanged(nameof(HasCaptureError)); }
        }
        public bool HasCaptureError => !string.IsNullOrEmpty(_captureError);

        // The raw error (incl. any long path) — shown as the banner's tooltip, kept out of the banner text.
        private string _captureErrorDetail = "";
        public string CaptureErrorDetail { get => _captureErrorDetail; set => SetProperty(ref _captureErrorDetail, value); }

        public void ClearCaptureError() { CaptureError = ""; CaptureErrorDetail = ""; }

        private int _consecutiveCaptureFailures;

        private void OnFrameCaptured(int count)
            => Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                _consecutiveCaptureFailures = 0;
                if (HasCaptureError) CaptureError = "";   // a frame saved → clear any stale warning
                FrameCount = count;

                // Optional unattended stop: end the run once we've reached the target number of INPUT
                // frames — frame-skip needs N× as many to yield the target video length (matches Trim).
                long targetFrames = (long)_targetSeconds * Math.Max(1, EncodeFps) * Math.Max(1, EncodeEveryNth);
                if (TargetKind == TargetVideo && _settings.StopAtTarget && IsCapturing && targetFrames > 0 && count >= targetFrames)
                {
                    Logger.Log("Wpf", $"Auto-stop: reached target ({count} >= {targetFrames} frames).");
                    StopCapture();
                    NotifyFinished();
                }
            }));

        // A frame failed to save (folder deleted, disk full, permissions…). Surface it — and if it keeps
        // failing, stop rather than tick forever with the count frozen and no sign anything is wrong.
        private void OnCaptureFailed(string message)
        {
            Logger.Log("Wpf", $"Capture error: {message}");
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsCapturing) return;   // already stopped — ignore late failures from in-flight ticks
                _consecutiveCaptureFailures++;
                CaptureErrorDetail = message;   // full detail (incl. path) → tooltip only
                if (_consecutiveCaptureFailures >= 3)
                {
                    StopCapture();
                    CaptureErrorDetail = message;   // StopCapture clears it; restore for the tooltip
                    CaptureError = "Capture stopped — couldn't save frames. Check the output folder still exists and has free space, then start again.";
                    NotifyFinished();
                }
                else
                {
                    CaptureError = "Couldn't save the last frame — retrying…";
                }
            }));
        }

        private void OnSmartStatus(string status)
            => Application.Current?.Dispatcher.BeginInvoke(new Action(() => SmartStatus = status));

    }
}
