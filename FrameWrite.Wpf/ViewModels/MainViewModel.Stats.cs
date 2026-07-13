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
    /// MainViewModel — the stats panel: structured stat rows, progress/elapsed readouts, the
    /// 1s RefreshStats tick (auto-stop checks live here), storage rate, and the frame preview.
    /// </summary>
    public partial class MainViewModel
    {
        // ---- Capture cadence sparkline: EMA-smoothed frames/min over recent time ----
        // Sampled once per 1s stats tick while capturing; the trace freezes (stays visible) when
        // stopped and continues on the next start. EMA smooths the spiky slow-interval case AND
        // converges to the true rate (60/interval), dipping to zero during idle-skip / pause —
        // so the graph literally shows the capture's heartbeat and where Smart Interval kicked in.
        private double _lastAvgFrameKb;                      // last sampled avg-or-estimate frame size (Size target math)
        private const int CadenceCapacity = 120;             // ~2 minutes of history at 1 sample/s
        private readonly List<double> _cadence = new();
        private double _cadenceEma = -1;                     // < 0 = uninitialised (first sample seeds it)
        private int _lastCadenceFrame;
        private DateTime _lastCadenceStamp;

        /// <summary>Recent frames-per-minute samples for the sparkline (fresh array each read — cheap at 120).</summary>
        public double[] CadenceSamples => _cadence.ToArray();
        public bool HasCadence => _cadence.Count > 1;

        private string _cadenceText = "";
        public string CadenceText { get => _cadenceText; set => SetProperty(ref _cadenceText, value); }

        /// <summary>Prime the cadence datum without clearing the trace — called at each capture start so a
        /// stop→wait→start gap isn't billed as one giant instantaneous rate.</summary>
        private void PrimeCadence()
        {
            _lastCadenceFrame = _frameCount;
            _lastCadenceStamp = DateTime.Now;
        }

        /// <summary>Clear the trace entirely — a new/loaded session has a different frame history.</summary>
        private void ResetCadence()
        {
            _cadence.Clear();
            _cadenceEma = -1;
            _lastCadenceFrame = _frameCount;
            _lastCadenceStamp = DateTime.Now;
            CadenceText = "";
            OnPropertyChanged(nameof(CadenceSamples));
            OnPropertyChanged(nameof(HasCadence));
        }

        private void SampleCadence()
        {
            var now = DateTime.Now;
            double secs = (now - _lastCadenceStamp).TotalSeconds;
            if (secs <= 0.05) return;   // guard against a double-tick producing a divide-by-tiny spike
            _lastCadenceStamp = now;
            int delta = Math.Max(0, _frameCount - _lastCadenceFrame);
            _lastCadenceFrame = _frameCount;

            double instant = delta * 60.0 / secs;   // frames per minute over the last tick
            _cadenceEma = _cadenceEma < 0 ? instant : 0.3 * instant + 0.7 * _cadenceEma;
            _cadence.Add(_cadenceEma);
            if (_cadence.Count > CadenceCapacity) _cadence.RemoveAt(0);

            CadenceText = $"≈ {_cadenceEma:0} frames/min";
            OnPropertyChanged(nameof(CadenceSamples));
            OnPropertyChanged(nameof(HasCadence));
        }

        // Structured stat rows (icon · label · value in the XAML) — replaced the old emoji text blobs.
        private string _statFrameSize = "";
        public string StatFrameSize { get => _statFrameSize; set => SetProperty(ref _statFrameSize, value); }

        private string _statSession = "";
        public string StatSession { get => _statSession; set => SetProperty(ref _statSession, value); }

        private string _statAtTarget = "";
        public string StatAtTarget { get => _statAtTarget; set => SetProperty(ref _statAtTarget, value); }

        private string _statFreeSpace = "";
        public string StatFreeSpace { get => _statFreeSpace; set => SetProperty(ref _statFreeSpace, value); }

        private bool _statLowSpace;
        public bool StatLowSpace { get => _statLowSpace; set => SetProperty(ref _statLowSpace, value); }

        private string _statMemory = "";
        public string StatMemory { get => _statMemory; set => SetProperty(ref _statMemory, value); }

        // "512 MB" below 10 GB, "24.3 GB" above — big numbers stay readable.
        private static string FormatMB(double mb) => mb >= 10240 ? $"{mb / 1024.0:F1} GB" : $"{mb:F0} MB";

        private string _videoLengthText = "";
        public string VideoLengthText { get => _videoLengthText; set => SetProperty(ref _videoLengthText, value); }

        private string _elapsedText = "00:00";
        public string ElapsedText { get => _elapsedText; set => SetProperty(ref _elapsedText, value); }

        private string _totalElapsedText = "00:00";
        public string TotalElapsedText { get => _totalElapsedText; set => SetProperty(ref _totalElapsedText, value); }

        private double _captureProgress;
        public double CaptureProgress { get => _captureProgress; set => SetProperty(ref _captureProgress, value); }

        private string _progressText = "";
        public string ProgressText { get => _progressText; set => SetProperty(ref _progressText, value); }

        // How long you need to capture (at the current interval) to reach the target video length —
        // live: total when idle, remaining while capturing. The clarity the target field was missing.
        private string _captureToTargetText = "";
        public string CaptureToTargetText { get => _captureToTargetText; set => SetProperty(ref _captureToTargetText, value); }

        private void UpdateCaptureToTarget()
        {
            double interval = (double)IntervalSeconds;
            if (_targetSeconds <= 0 || interval <= 0) { CaptureToTargetText = ""; return; }

            if (TargetKind == TargetTimer)
            {
                // Recording timer: time left on the clock (active time only; accumulates across stops).
                double remaining = Math.Max(0, _targetSeconds - TimerProgressSeconds());
                CaptureToTargetText = IsCapturing
                    ? (remaining == 0 ? "✓ timer reached" : $"≈ {HumanDurationPrecise(remaining)} left on the timer (pause doesn't count)")
                    : $"will record for {HumanDurationPrecise(_targetSeconds)}, then stop";
                return;
            }

            if (TargetKind == TargetSize)
            {
                // Disk budget: how much more you can capture before the session reaches the budget.
                var (frames, secs) = SystemMonitor.ProjectCaptureBudget(
                    _targetSizeMB, _lastAvgFrameKb, _frameCount, (double)IntervalSeconds);
                CaptureToTargetText = frames <= 0
                    ? (SessionSizeMB() > 0 ? "✓ budget reached" : $"set a budget — captures up to {FormatBudget(_targetSizeMB)}")
                    : $"≈ {HumanDuration(secs)} / {frames:N0} more frames until {FormatBudget(_targetSizeMB)}";
                return;
            }

            long targetFrames = (long)_targetSeconds * Math.Max(1, EncodeFps) * Math.Max(1, EncodeEveryNth);
            if (IsCapturing)
            {
                long remaining = Math.Max(0, targetFrames - _frameCount);
                CaptureToTargetText = remaining == 0
                    ? "✓ target reached — you can stop"
                    : $"≈ {HumanDuration(remaining * interval)} more to reach your {HumanDuration(_targetSeconds)} target";
            }
            else
            {
                CaptureToTargetText = $"≈ {HumanDuration(targetFrames * interval)} of capturing → a {HumanDuration(_targetSeconds)} video";
            }
        }

        // Live storage-consumption rate + a warning when the current settings would eat the drive fast.
        private string _storageRateText = "";
        public string StorageRateText { get => _storageRateText; set => SetProperty(ref _storageRateText, value); }
        private bool _storageRateWarn;
        public bool StorageRateWarn { get => _storageRateWarn; set => SetProperty(ref _storageRateWarn, value); }

        // (mbPerHour, hoursToFillTheDrive) for the current region/format/interval. avgFrameKb from a
        // sampled frame when available, else estimated. Powers the stats readout and the start warning.
        private (double mbPerHour, double fillHours) EstimateStorageRate(double avgFrameKb)
        {
            int w = _region?.Width ?? 0, h = _region?.Height ?? 0;
            double frameKb = avgFrameKb > 0 ? avgFrameKb
                : (w > 0 && h > 0 ? SystemMonitor.EstimateFrameSizeKB(w, h, _settings.Format ?? "JPEG", _settings.JpegQuality) : 0);
            double interval = (double)IntervalSeconds;
            if (frameKb <= 0 || interval <= 0) return (0, double.PositiveInfinity);
            double mbPerHour = frameKb * (3600.0 / interval) / 1024.0;
            long freeMb = _sessionFolder != null ? SystemMonitor.GetAvailableDiskSpaceMB(_sessionFolder) : 0;
            double fillHours = (freeMb > 0 && mbPerHour > 0) ? freeMb / mbPerHour : double.PositiveInfinity;
            return (mbPerHour, fillHours);
        }

        private void UpdateStorageRate(double avgFrameKb)
        {
            var (mbPerHour, fillHours) = EstimateStorageRate(avgFrameKb);
            if (mbPerHour <= 0) { StorageRateText = ""; StorageRateWarn = false; return; }
            string rate = mbPerHour >= 1024 ? $"{mbPerHour / 1024.0:F1} GB/hour" : $"{mbPerHour:F0} MB/hour";
            StorageRateWarn = fillHours < 2;   // under 2 hours to fill the drive = worth flagging
            StorageRateText = StorageRateWarn && !double.IsInfinity(fillHours)
                ? $"≈ {rate} — fills this drive in ~{HumanDuration(fillHours * 3600)}"
                : $"≈ {rate}";
        }

        // Compact human duration for planning ("2h 30m", "45m", "30s") — clearer than hh:mm:ss here.
        private static string HumanDuration(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Round(seconds));
            if (t.TotalHours >= 1) return t.Minutes == 0 ? $"{(int)t.TotalHours}h" : $"{(int)t.TotalHours}h {t.Minutes}m";
            if (t.TotalMinutes >= 1) return t.Seconds == 0 ? $"{t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";
            return $"{t.Seconds}s";
        }

        // The recording timer echoes an EXACT h/m/s the user set, so it must never drop a nonzero
        // seconds component the way HumanDuration does in its hours branch (fine for fuzzy planning
        // readouts, wrong for a precise timer — the "record for 1h 30s" showed as "1h" bug).
        private static string HumanDurationPrecise(double seconds)
        {
            int total = (int)Math.Round(Math.Max(0, seconds));
            int h = total / 3600, m = total % 3600 / 60, s = total % 60;
            var parts = new List<string>();
            if (h > 0) parts.Add($"{h}h");
            if (m > 0) parts.Add($"{m}m");
            if (s > 0 || parts.Count == 0) parts.Add($"{s}s");
            return string.Join(" ", parts);
        }

        // Bumped when the user changes an input (target / fps) that recalculates the stats — the
        // affected on-screen values flash briefly so you can see what got recomputed.
        private int _recalcPulse;
        public int RecalcPulse { get => _recalcPulse; set => SetProperty(ref _recalcPulse, value); }
        private void BumpRecalc() => RecalcPulse++;

        // Bumped only on an explicit Target commit (Enter / tab-away) to drive the field's confirm pulse.
        private int _targetPulse;
        public int TargetPulse { get => _targetPulse; set => SetProperty(ref _targetPulse, value); }

        private string _smartStatus = "";
        public string SmartStatus { get => _smartStatus; set { if (SetProperty(ref _smartStatus, value)) NotifyCaptureState(); } }

        private ImageSource? _previewImage;
        public ImageSource? PreviewImage
        {
            get => _previewImage;
            set { if (SetProperty(ref _previewImage, value)) OnPropertyChanged(nameof(NoPreview)); }
        }
        public bool NoPreview => _previewImage == null;

        private int _lastPreviewedFrame = -1;
        // Load the latest frame by its known number (O(1)) rather than scanning the whole frames folder
        // every refresh — that folder grows to tens of thousands on a long run. Falls back to a scan only
        // if the exact-numbered file is missing (unusual / partially-deleted session).
        private void UpdatePreview()
        {
            _lastPreviewedFrame = _frameCount;
            PreviewImage = _frameCount > 0
                ? (FramePreview.LoadAt(_sessionFolder, _frameCount, 260) ?? FramePreview.LoadLatest(_sessionFolder, 260))
                : null;
        }

        private bool HasOutputFolder =>
            !string.IsNullOrWhiteSpace(_settings.SaveFolder) && Directory.Exists(_settings.SaveFolder);

        // Surfaced when the saved output folder no longer exists on disk (deleted/renamed/unplugged) —
        // otherwise the card shows a healthy-looking path while New Session is mysteriously disabled.
        private bool _outputFolderMissing;
        public bool OutputFolderMissing { get => _outputFolderMissing; private set => SetProperty(ref _outputFolderMissing, value); }
        private void RefreshOutputFolderMissing() =>
            OutputFolderMissing = !string.IsNullOrWhiteSpace(_settings.SaveFolder) && !Directory.Exists(_settings.SaveFolder);

        // For the Overlay dialog's live preview: render at the REAL frame size over the latest frame,
        // so the example is exactly what will be burned in.
        public string? CurrentSessionFolder => _sessionFolder;
        public System.Drawing.Size? CurrentRegionSize => _region?.Size;

        // Make the layout explicit: sessions land in a "captures" subfolder of the chosen folder.
        public bool HasSaveFolderSet => !string.IsNullOrWhiteSpace(_settings.SaveFolder);
        public string CapturesRootHint => HasSaveFolderSet
            ? $"Sessions are saved to {Path.Combine(_settings.SaveFolder!, "captures")}\\<session name>"
            : "";

        /// <summary>
        /// Load a session from an arbitrary path: the session folder itself, its session.json, or a file/
        /// subfolder inside it (frames, output). Used by drag-drop onto the window and by a command-line
        /// argument (e.g. dragging a session folder onto the exe in Explorer). Returns false if no session
        /// was found at or above the path.
        /// </summary>
        public bool TryLoadSessionPath(string path)
        {
            // Swapping the session mid-capture is unsafe; mid-encode it would misattribute the encode's
            // completion UI to the newly loaded session — refuse both (the drop handler explains).
            if (IsCapturing || IsEncoding || string.IsNullOrWhiteSpace(path)) return false;

            string? dir = SessionManager.FindSessionRoot(path);   // folder, or walk up from a file inside one
            if (dir == null) return false;

            // A parseable session.json OUTSIDE the captures root could be a stray copy (backup on the
            // Desktop, an extracted zip) — loading it would make that folder the live capture target.
            // Confirm rather than adopt silently.
            try
            {
                string root = string.IsNullOrWhiteSpace(_settings.SaveFolder)
                    ? "" : Path.GetFullPath(Path.Combine(_settings.SaveFolder!, "captures"));
                bool outsideRoot = root.Length == 0 ||
                    !Path.GetFullPath(dir).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                if (outsideRoot)
                {
                    var r = MessageDialog.Show(
                        $"Load this session from outside your captures folder?\n\n{dir}\n\nNew frames would be captured into that folder.",
                        "Open session", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r != MessageBoxResult.Yes) return true;   // handled — don't show "not a session"
                }
                LoadSessionFromFolder(dir, fromPicker: false);
                return true;
            }
            catch { return false; }
        }

        private void RefreshStats()
        {
            try
            {
                _statsTick++;

                // Elapsed (current run) + total across start/stop — updated every tick (1s) so it counts smoothly.
                double current = (IsCapturing && _captureStart.HasValue) ? (DateTime.Now - _captureStart.Value).TotalSeconds : 0;
                ElapsedText = FormatTime(RunActiveSeconds());
                double totalCaptureSeconds = _accumulatedSeconds + current;
                TotalElapsedText = FormatTime(totalCaptureSeconds);
                UpdateCaptureToTarget();   // live time-to-target readout
                if (IsCapturing) SampleCadence();   // feed the cadence sparkline (frozen trace while stopped)

                // Soak-diagnostic heartbeat: once every ~5 min of capture, log the vitals a long
                // unattended run is judged on (memory flat? cadence steady? disk draining?). Quiet
                // otherwise, so the log stays readable — this is the trail if a soak reveals drift.
                if (IsCapturing && _statsTick % 300 == 0)
                {
                    try
                    {
                        long freeMb = _sessionFolder != null ? SystemMonitor.GetAvailableDiskSpaceMB(_sessionFolder) : 0;
                        Logger.Log("Heartbeat",
                            $"frames={_frameCount} run={FormatTime(RunActiveSeconds())} " +
                            $"mem={SystemMonitor.GetProcessMemoryMB():F0}MB freeDisk={freeMb}MB " +
                            $"cadence={(_cadenceEma < 0 ? 0 : _cadenceEma):F0}/min " +
                            $"smart={( _settings.SmartIntervalEnabled ? SmartStatus : "off")}");
                    }
                    catch { /* diagnostics must never break the tick */ }
                }

                // Opt-in max-duration cap: stop once accumulated capture time reaches the limit (a normal
                // completion — notify, but no red error banner).
                if (IsCapturing && !_isPaused && _settings.MaxDurationEnabled && totalCaptureSeconds >= _settings.MaxDurationMinutes * 60.0)
                {
                    Logger.Log("Wpf", $"Auto-stop: reached max duration ({totalCaptureSeconds:F0}s >= {_settings.MaxDurationMinutes * 60}s).");
                    StopCapture();
                    NotifyFinished();
                    return;   // nothing else to refresh this tick
                }

                // Recording timer: stop once ACTIVE capture time (accumulated across stops) reaches the
                // target — a normal completion (notify, no error banner). Paused time never advances it.
                if (IsCapturing && TargetKind == TargetTimer && TimerProgressSeconds() >= _targetSeconds)
                {
                    Logger.Log("Wpf", $"Auto-stop: recording timer reached ({TimerProgressSeconds():F0}s >= {_targetSeconds}s of active capture).");
                    StopCapture();
                    NotifyFinished();
                    return;
                }

                // Size budget: stop once the session's frames reach the budget on disk.
                if (IsCapturing && TargetKind == TargetSize && _lastAvgFrameKb > 0 && SessionSizeMB() >= _targetSizeMB)
                {
                    Logger.Log("Wpf", $"Auto-stop: size budget reached ({SessionSizeMB():F0}MB >= {_targetSizeMB}MB).");
                    StopCapture();
                    NotifyFinished();
                    return;
                }

                int projectedFrames;   // also feeds the storage projection below, for every kind
                if (TargetKind == TargetTimer)
                {
                    // Timer kind: frames expected from running the timer out at the current interval.
                    projectedFrames = Math.Max(_frameCount, (int)(_targetSeconds / Math.Max(0.1, (double)IntervalSeconds)));
                    double progress = TimerProgressSeconds();
                    double pct = Math.Min(100.0, progress * 100.0 / Math.Max(1, _targetSeconds));
                    CaptureProgress = pct;
                    ProgressText = IsCapturing
                        ? $"{FormatTime(progress)} / {FormatTime(_targetSeconds)} recorded · {pct:F0}% of the timer"
                        : $"{_frameCount} frames · timer set for {HumanDurationPrecise(_targetSeconds)}";
                }
                else if (TargetKind == TargetSize)
                {
                    // Size kind: progress = session bytes on disk toward the budget.
                    var (frames, _) = SystemMonitor.ProjectCaptureBudget(_targetSizeMB, _lastAvgFrameKb, _frameCount, (double)IntervalSeconds);
                    projectedFrames = Math.Max(_frameCount, _frameCount + (int)Math.Min(int.MaxValue, frames));
                    double used = SessionSizeMB();
                    double pct = _targetSizeMB > 0 ? Math.Min(100.0, used * 100.0 / _targetSizeMB) : 0;
                    CaptureProgress = pct;
                    ProgressText = $"{FormatMB(used)} / {FormatBudget(_targetSizeMB)} · {pct:F0}% of the budget";
                }
                else
                {
                    projectedFrames = Math.Max(_frameCount, _targetSeconds * Math.Max(1, EncodeFps) * Math.Max(1, EncodeEveryNth));
                    double pct = projectedFrames > 0 ? Math.Min(100.0, _frameCount * 100.0 / projectedFrames) : 0;
                    CaptureProgress = pct;
                    ProgressText = $"{_frameCount} / {projectedFrames} frames · {pct:F0}% of a {_targetSeconds}s video @ {Math.Max(1, EncodeFps)}fps";
                }

                int everyNth = Math.Max(1, EncodeEveryNth);
                int encodedFrames = (_frameCount + everyNth - 1) / everyNth;
                if (EncodeDurationMode && _frameCount > 0)
                {
                    // Exact-length mode: the length IS the setting; show the fps it currently implies.
                    double dFps = VideoEncoder.FpsForDuration(_frameCount, everyNth, EncodeDurationSeconds);
                    double actual = encodedFrames / dFps;
                    VideoLengthText = actual > EncodeDurationSeconds + 0.5
                        ? $"≈ {HumanDuration(actual)} (240 fps ceiling)"
                        : $"= {HumanDuration(EncodeDurationSeconds)} @ {dFps:0.##}fps";
                }
                else
                {
                    double vidLen = EncodeFps > 0 ? encodedFrames / (double)EncodeFps : 0;
                    VideoLengthText = $"≈ {vidLen:F1}s @ {EncodeFps}fps" + (everyNth > 1 ? $" · 1 in {everyNth}" : "");
                }

                // The storage/disk/memory probe reads frame files — throttle it to ~every 2s.
                if (_statsTick % 2 == 0 || string.IsNullOrEmpty(StatFrameSize))
                {
                    RefreshOutputFolderMissing();   // folder can vanish while the app is running

                    // Safety: if the tracked window went fullscreen after we pinned it (game toggled modes),
                    // release the pin — a fullscreen topmost window blocks alt-tab for the whole desktop.
                    if (_trackedWindowMadeTopmost && WindowEnumerator.CoversFullMonitor(_trackedWindow))
                    {
                        UnpinTrackedWindow();
                        Logger.Log("Wpf", "Keep-on-top released: the tracked window went fullscreen.");
                    }

                    int w = _region?.Width ?? 0;
                    int h = _region?.Height ?? 0;
                    // Sample the average frame size ONCE per tick and reuse it for the readout and the
                    // storage-cap check (it reads frame files off disk — don't do it twice).
                    double avgFrameKb = (_sessionFolder != null && _frameCount > 0)
                        ? SystemMonitor.GetActualAverageFrameSizeKB(_sessionFolder) : 0;
                    var st = SystemMonitor.GetStorageStats(_sessionFolder, w, h,
                        _settings.Format ?? "JPEG", _settings.JpegQuality, _frameCount, projectedFrames, avgFrameKb);
                    _lastAvgFrameKb = st.FrameSizeKB;   // actual-or-estimate — feeds the Size target math
                    StatFrameSize = st.FrameSizeIsActual ? $"{st.FrameSizeKB:F1} KB avg" : $"~{st.FrameSizeKB:F1} KB est.";
                    StatSession = st.CurrentFrames > 0 ? $"{FormatMB(st.SessionMB)} · {st.CurrentFrames:N0} frames" : "no frames yet";
                    StatAtTarget = st.RemainingFrames > 0
                        ? $"+{FormatMB(st.RemainingMB)} more → {FormatMB(st.TotalAtTargetMB)}"
                        : (st.CurrentFrames > 0 ? "✓ at the target size" : "");
                    StatFreeSpace = st.AvailableMB > 0
                        ? $"{FormatMB(st.AvailableMB)}{(st.Drive.Length > 0 ? $" on {st.Drive}" : "")}"
                        : "—";
                    StatLowSpace = st.LowSpaceWarning;
                    StatMemory = $"{SystemMonitor.GetProcessMemoryMB():F0} MB";
                    UpdateStorageRate(avgFrameKb);   // live "≈ X GB/hour" + fast-fill warning

                    // Unattended safety: stop before the drive fills (writes would start failing, and a full
                    // disk can disrupt other apps). freeMb == 0 is treated as a probe error and ignored — the
                    // threshold stop fires well before a genuine zero.
                    if (IsCapturing && _sessionFolder != null)
                    {
                        long freeMb = SystemMonitor.GetAvailableDiskSpaceMB(_sessionFolder);
                        if (freeMb > 0 && freeMb < _settings.LowDiskStopMB)
                        {
                            Logger.Log("Wpf", $"Auto-stop: low disk ({freeMb} MB free < {_settings.LowDiskStopMB} MB limit).");
                            StopCapture();
                            CaptureError = $"Capture stopped — low disk space ({freeMb} MB free, limit {_settings.LowDiskStopMB} MB). Free up space or change the limit in Settings, then start again.";
                            NotifyFinished();
                        }
                    }

                    // Opt-in session-size cap: stop once the captured frames reach the chosen size (a normal
                    // completion, like stop-at-target — notify, no error banner). Sized via the sampled
                    // average frame size — no O(n) folder walk on the stats tick.
                    if (IsCapturing && !_isPaused && _settings.StopAtStorageEnabled && _sessionFolder != null && _frameCount > 0)
                    {
                        double sessionMb = avgFrameKb * _frameCount / 1024.0;   // reuse the once-sampled average
                        if (sessionMb >= _settings.StopAtStorageMB)
                        {
                            Logger.Log("Wpf", $"Auto-stop: reached storage budget (~{sessionMb:F0} MB >= {_settings.StopAtStorageMB} MB).");
                            StopCapture();
                            NotifyFinished();
                            return;
                        }
                    }
                }

                if (IsCapturing && PreviewExpanded && _frameCount != _lastPreviewedFrame) UpdatePreview();   // only on a new frame, only when shown
            }
            catch { /* stats are best-effort */ }
        }

        private static string FormatTime(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" : $"{t.Minutes:D2}:{t.Seconds:D2}";
        }
    }
}
