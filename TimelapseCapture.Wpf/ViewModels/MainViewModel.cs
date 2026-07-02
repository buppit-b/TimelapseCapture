using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using TimelapseCapture; // Core: settings, sessions, ffmpeg, capture engine, screen helper

namespace TimelapseCapture.Wpf.ViewModels
{
    /// <summary>
    /// Main window view-model. Drives the first working vertical slice on top of the reused
    /// TimelapseCapture.Core engine: choose folder → new session → full screen → start/stop,
    /// with a live frame count. Region drag-select and encode are layered on next.
    /// </summary>
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private CaptureSettings _settings; // reassigned by Import settings
        private readonly CaptureEngine _engine = new CaptureEngine();
        private System.Threading.CancellationTokenSource? _ffmpegCts;
        private System.Threading.CancellationTokenSource? _encodeCts;
        private RegionOverlay? _overlay;
        private SessionInfo? _session;
        private string? _sessionFolder;
        private Rectangle? _region;
        private IntPtr _trackedWindow = IntPtr.Zero;   // non-zero = the active region follows this window
        private DateTime? _captureStart;
        private double _accumulatedSeconds; // total capture time across start/stop within this app run
        private int _statsTick;
        private readonly DispatcherTimer _statsTimer;
        private readonly DispatcherTimer _trackOverlayTimer;   // moves the on-screen outline with a tracked window
        private bool _trackedWindowMadeTopmost;                // true if we forced the tracked window topmost
        private string _pinnedIdentity = "";                   // identity of the pinned window (guards HWND reuse)

        public MainViewModel()
        {
            _settings = SettingsManager.Load();
            _outputFolder = string.IsNullOrWhiteSpace(_settings.SaveFolder) ? "(not set)" : _settings.SaveFolder!;
            RefreshOutputFolderMissing();   // warn immediately if the saved folder was deleted since last run
            RefreshFfmpegStatus();

            _engine.FrameCaptured += OnFrameCaptured;
            _engine.CaptureFailed += OnCaptureFailed;
            _engine.SmartStatusChanged += OnSmartStatus;

            ChooseFolderCommand = new RelayCommand(_ => ChooseFolder(), _ => !IsCapturing);
            NewSessionCommand = new RelayCommand(_ => NewSession(), _ => HasOutputFolder && !IsCapturing);
            LoadSessionCommand = new RelayCommand(_ => LoadSession(), _ => HasOutputFolder && !IsCapturing && !IsEncoding);
            RenameSessionCommand = new RelayCommand(_ => RenameSession(), _ => _session != null && !IsCapturing);
            OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
            OpenOverlayCommand = new RelayCommand(_ => OpenOverlay());
            DismissCaptureErrorCommand = new RelayCommand(_ => ClearCaptureError());
            OpenLogCommand = new RelayCommand(_ => OpenLog());
            OpenWizardCommand = new RelayCommand(_ => OpenWizard(), _ => !IsCapturing);
            ExportSettingsCommand = new RelayCommand(_ => ExportSettings());
            ImportSettingsCommand = new RelayCommand(_ => ImportSettings());
            SetTargetCommand = new RelayCommand(_ => SetTarget());
            ValidateTarget();

            // After the window is up, check whether a previous run was interrupted mid-capture.
            Application.Current?.Dispatcher.BeginInvoke(new Action(CheckForInterruptedSession), DispatcherPriority.Background);
            FullScreenCommand = new RelayCommand(_ => SelectFullScreen(), _ => _session != null && !IsCapturing);
            TrackWindowCommand = new RelayCommand(_ => TrackWindow(), _ => _session != null && !IsCapturing);
            SelectRegionCommand = new RelayCommand(_ => SelectRegion(), _ => _session != null && !IsCapturing);
            EditRegionCommand = new RelayCommand(_ => EditRegion(), _ => _session != null && _region.HasValue && !IsCapturing);
            StartCommand = new RelayCommand(_ => StartCapture(), _ => _session != null && _region.HasValue && !IsCapturing);
            StopCommand = new RelayCommand(_ => StopCapture(), _ => IsCapturing);
            PauseResumeCommand = new RelayCommand(_ => PauseResume(), _ => IsCapturing);
            OpenFolderCommand = new RelayCommand(_ => OpenSessionFolder(), _ => CanOpenFolder);
            EncodeCommand = new RelayCommand(async _ => await EncodeOrCancel(), _ => CanEncode || IsEncoding);
            TrimCommand = new RelayCommand(async _ => await Trim(), _ => CanEncode);
            CullCommand = new RelayCommand(_ => Cull(), _ => CanEncode);
            DownloadFfmpegCommand = new RelayCommand(async _ => await DownloadFfmpeg(), _ => !IsFfmpegBusy);
            BrowseFfmpegCommand = new RelayCommand(_ => BrowseFfmpeg(), _ => !IsFfmpegBusy);
            CancelDownloadCommand = new RelayCommand(_ => _ffmpegCts?.Cancel(), _ => IsFfmpegBusy);
            ShowOverlayCommand = new RelayCommand(_ => ToggleOverlay(), _ => _region.HasValue || _isOverlayShown);

            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statsTimer.Tick += (s, e) => RefreshStats();
            _statsTimer.Start();

            _trackOverlayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _trackOverlayTimer.Tick += OnTrackOverlayTick;
            RefreshStats();
        }

        // ---- bound state ----
        private string _outputFolder;
        public string OutputFolder { get => _outputFolder; set => SetProperty(ref _outputFolder, value); }

        public decimal IntervalSeconds
        {
            get => _settings.IntervalSecondsExact > 0 ? _settings.IntervalSecondsExact : _settings.IntervalSeconds;
            set
            {
                // 0.1s (10 fps) is the engine floor: each tick synchronously grabs + encodes + writes a
                // frame, and below ~100ms ticks overlap and get dropped — that's screen-recorder territory.
                decimal v = value < 0.1m ? 0.1m : value;
                if (_settings.IntervalSecondsExact != v)
                {
                    _settings.IntervalSecondsExact = v;
                    _settings.IntervalSeconds = (int)Math.Max(1, Math.Round(v)); // rounded metadata for int consumers
                    SettingsManager.Save(_settings);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(SpeedNotch));
                    OnPropertyChanged(nameof(SpeedHint));
                }
                // A clamped/rounded entry must be VISIBLE: re-notify after the binding transfer completes so
                // the field snaps back to the real value instead of displaying e.g. "0.01" while running 0.1s.
                // (Raised deferred and unconditionally — WPF can ignore a PropertyChanged fired inside the
                // same transfer, and the fps view needs refreshing when edited via seconds and vice versa.)
                // The clamp flash fires SYNCHRONOUSLY (it's a different property, so no transfer quirk) —
                // deferred it could land after a wizard step collapsed and play on a hidden ring.
                if (value != v) IntervalClampPulse++;   // flash the field red: "adjusted, see tooltip for why"
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    OnPropertyChanged(nameof(IntervalSeconds));
                    OnPropertyChanged(nameof(CaptureFps));
                }));
            }
        }

        // Bumped when an out-of-range interval entry was clamped — drives a brief red ring on the field.
        private int _intervalClampPulse;
        public int IntervalClampPulse { get => _intervalClampPulse; set => SetProperty(ref _intervalClampPulse, value); }

        // The same interval viewed as a capture rate (frames per second) — some users think in fps.
        // Round-trips through IntervalSeconds so every clamp/rule lives in one place.
        public decimal CaptureFps
        {
            get => IntervalSeconds > 0 ? Math.Round(1m / IntervalSeconds, 2) : 0;
            set { if (value > 0) IntervalSeconds = Math.Round(1m / value, 4); }
        }

        // 0 = seconds, 1 = fps — which unit the Advanced interval field shows (persisted preference).
        public int IntervalUnitIndex
        {
            get => _settings.IntervalShownAsFps ? 1 : 0;
            set
            {
                bool fps = value == 1;
                if (_settings.IntervalShownAsFps != fps)
                {
                    _settings.IntervalShownAsFps = fps;
                    SettingsManager.Save(_settings);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ShowIntervalSeconds));
                    OnPropertyChanged(nameof(ShowIntervalFps));
                }
            }
        }
        public bool ShowIntervalSeconds => !_settings.IntervalShownAsFps;
        public bool ShowIntervalFps => _settings.IntervalShownAsFps;

        // First-run flag: the setup wizard shows once on launch, then stays available from Settings.
        public bool FirstRunCompleted
        {
            get => _settings.FirstRunCompleted;
            set { if (_settings.FirstRunCompleted != value) { _settings.FirstRunCompleted = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        // ---- Simple mode: a curated view over the same settings (speed slider instead of raw interval) ----
        public bool SimpleMode
        {
            get => _settings.SimpleMode;
            set { if (_settings.SimpleMode != value) { _settings.SimpleMode = value; SettingsManager.Save(_settings); OnPropertyChanged(); OnPropertyChanged(nameof(AdvancedVisible)); } }
        }
        public bool AdvancedVisible => !SimpleMode;

        // Speed slider: named notches → sensible art-timelapse intervals (denser at the fast end, 0.5s floor).
        // The slider binds to SpeedNotch; typing an exact interval still works — the hint then reads "Custom".
        private static readonly decimal[] SpeedIntervals = { 0.5m, 1m, 2m, 3m, 5m, 15m, 60m };
        private static readonly string[] SpeedNames = { "Rapid", "Detailed", "Fine", "Standard", "Relaxed", "Long haul", "All-day" };

        public int SpeedMaxNotch => SpeedIntervals.Length - 1;   // sliders bind Maximum to this — one source of truth

        public int SpeedNotch
        {
            get
            {
                int best = 0; decimal bestDiff = decimal.MaxValue;
                for (int i = 0; i < SpeedIntervals.Length; i++)
                {
                    decimal d = Math.Abs(SpeedIntervals[i] - IntervalSeconds);
                    if (d < bestDiff) { bestDiff = d; best = i; }
                }
                return best;
            }
            set { IntervalSeconds = SpeedIntervals[Math.Clamp(value, 0, SpeedIntervals.Length - 1)]; }
        }

        // Plain-language outcome preview so an interval means something concrete. Always shows the ACTUAL
        // interval — a hand-typed value that sits between notches is labelled "Custom", not a notch name.
        public string SpeedHint
        {
            get
            {
                decimal interval = IntervalSeconds;
                if (interval <= 0) return "";
                int fps = Math.Max(1, EncodeFps);
                double framesPerMin = 60.0 / (double)interval;
                double oneHourVideoSec = 3600.0 / (double)interval / fps;
                string name = SpeedIntervals[SpeedNotch] == interval ? SpeedNames[SpeedNotch] : "Custom";
                return $"{name}  ·  every {interval}s  ·  ≈{framesPerMin:F0} frames/min  ·  a 1-hour session → ~{oneHourVideoSec:F0}s video @ {fps}fps";
            }
        }

        public bool SmartEnabled
        {
            get => _settings.SmartIntervalEnabled;
            set { if (_settings.SmartIntervalEnabled != value) { _settings.SmartIntervalEnabled = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public decimal IdleIntervalSeconds
        {
            get => _settings.IdleIntervalSeconds;
            set { var v = value < 0.1m ? 0.1m : value; if (_settings.IdleIntervalSeconds != v) { _settings.IdleIntervalSeconds = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public int IdleThresholdSeconds
        {
            get => _settings.IdleThresholdSeconds;
            set { var v = value < 1 ? 1 : value; if (_settings.IdleThresholdSeconds != v) { _settings.IdleThresholdSeconds = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public bool SkipIdleFrames
        {
            get => _settings.SkipIdleFrames;
            set { if (_settings.SkipIdleFrames != value) { _settings.SkipIdleFrames = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        private int _encodeFps = 30;
        public int EncodeFps { get => _encodeFps; set { if (SetProperty(ref _encodeFps, Math.Clamp(value, 1, 240))) { RefreshStats(); BumpRecalc(); OnPropertyChanged(nameof(SpeedHint)); } } }

        private int _encodeCrf = 23;
        public int EncodeCrf { get => _encodeCrf; set => SetProperty(ref _encodeCrf, value < 0 ? 0 : (value > 51 ? 51 : value)); }

        public int JpegQuality
        {
            get => _settings.JpegQuality;
            set { var v = value < 1 ? 1 : (value > 100 ? 100 : value); if (_settings.JpegQuality != v) { _settings.JpegQuality = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public string EncodePreset
        {
            get => string.IsNullOrWhiteSpace(_settings.EncodePreset) ? "medium" : _settings.EncodePreset;
            set { if (!string.Equals(_settings.EncodePreset, value, StringComparison.OrdinalIgnoreCase)) { _settings.EncodePreset = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public int AspectRatioIndex
        {
            get => _settings.AspectRatioIndex;
            set
            {
                var all = AspectRatio.CommonRatios;
                var v = (value < 0 || value >= all.Length) ? 0 : value;
                if (_settings.AspectRatioIndex != v) { _settings.AspectRatioIndex = v; SettingsManager.Save(_settings); OnPropertyChanged(); }
            }
        }

        public bool UsePng
        {
            get => string.Equals(_settings.Format, "PNG", StringComparison.OrdinalIgnoreCase);
            set
            {
                var fmt = value ? "PNG" : "JPEG";
                if (string.Equals(_settings.Format, fmt, StringComparison.OrdinalIgnoreCase)) return;

                if (_session != null && _frameCount > 0)
                {
                    var r = MessageBox.Show(
                        $"This session has {_frameCount} {(_settings.Format ?? "JPEG")} frame(s). Switching the frame format mid-session mixes file types and breaks encoding.\n\nSwitch anyway?",
                        "Change format?", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (r != MessageBoxResult.Yes) { OnPropertyChanged(); return; } // revert the checkbox
                }

                _settings.Format = fmt;
                SettingsManager.Save(_settings);
                OnPropertyChanged();
            }
        }

        private string _ffmpegStatus = "Checking…";
        public string FfmpegStatus { get => _ffmpegStatus; set => SetProperty(ref _ffmpegStatus, value); }

        private string _sessionName = "No active session";
        public string SessionName { get => _sessionName; set => SetProperty(ref _sessionName, value); }

        private string _regionText = "Not selected";
        public string RegionText { get => _regionText; set => SetProperty(ref _regionText, value); }

        private int _frameCount;
        public int FrameCount
        {
            get => _frameCount;
            set { if (SetProperty(ref _frameCount, value)) OnPropertyChanged(nameof(FrameCountText)); }
        }
        public string FrameCountText => $"{_frameCount} frames";

        private bool _isCapturing;
        public bool IsCapturing
        {
            get => _isCapturing;
            set
            {
                if (SetProperty(ref _isCapturing, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(RegionNeeded));
                    OnPropertyChanged(nameof(NotCapturing));
                    OnPropertyChanged(nameof(SessionNeeded));
                    OnPropertyChanged(nameof(IsRecording));
                    OnPropertyChanged(nameof(RecLabel));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private bool _isPaused;
        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                if (SetProperty(ref _isPaused, value))
                {
                    OnPropertyChanged(nameof(IsRecording));
                    OnPropertyChanged(nameof(RecLabel));
                    OnPropertyChanged(nameof(PauseResumeText));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }
        public bool IsRecording => IsCapturing && !_isPaused;     // for the pulsing REC dot
        public string RecLabel => _isPaused ? "PAUSED" : "REC";
        public string PauseResumeText => _isPaused ? "▶  Resume" : "⏸  Pause";

        private bool _isEncoding;
        public bool IsEncoding
        {
            get => _isEncoding;
            set
            {
                if (SetProperty(ref _isEncoding, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(EncodeButtonText));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private string _encodeStatus = "";
        public string EncodeStatus { get => _encodeStatus; set => SetProperty(ref _encodeStatus, value); }

        public string EncodeButtonText => IsEncoding ? "⏹  Cancel encode" : "🎬  Encode Video";

        private bool _isFfmpegBusy;
        public bool IsFfmpegBusy
        {
            get => _isFfmpegBusy;
            set { if (SetProperty(ref _isFfmpegBusy, value)) CommandManager.InvalidateRequerySuggested(); }
        }

        public string StatusText =>
            IsEncoding ? "Encoding video…" :
            IsCapturing ? $"● Capturing every {IntervalSeconds}s…" :
            _session == null ? "Create a session to begin." :
            _region.HasValue ? "Ready to capture." :
            "Select a region (Full Screen for now).";

        /// <summary>True when a session exists but no region is chosen yet — used to highlight the region buttons.</summary>
        public bool RegionNeeded => _session != null && !_region.HasValue && !IsCapturing;

        /// <summary>True when no session yet — used to pulse the New Session button.</summary>
        public bool SessionNeeded => _session == null && !IsCapturing;

        /// <summary>Capture settings are editable only when not capturing.</summary>
        public bool NotCapturing => !IsCapturing;

        private bool _isOverlayShown;
        public bool IsOverlayShown
        {
            get => _isOverlayShown;
            set { if (SetProperty(ref _isOverlayShown, value)) OnPropertyChanged(nameof(ShowButtonText)); }
        }
        public string ShowButtonText => _isOverlayShown ? "Hide" : "Show";

        private int _desiredVideoSeconds = 30;

        // Planned capture length used for storage projection. Accepts "30s", "5m", "2h" (default seconds).
        private string _targetText = "30s";
        public string TargetText
        {
            get => _targetText;
            set { if (SetProperty(ref _targetText, value)) ValidateTarget(); } // validate as you type; apply on Set
        }

        private string _targetHint = "";
        public string TargetHint { get => _targetHint; set => SetProperty(ref _targetHint, value); }

        private bool _targetHintError;
        public bool TargetHintError { get => _targetHintError; set => SetProperty(ref _targetHintError, value); }

        // Live, non-intrusive feedback for the Target field — no popups.
        private void ValidateTarget()
        {
            if (TryParseTarget(_targetText, out var secs, out var human))
            {
                TargetHint = secs == _desiredVideoSeconds ? $"= {human} ✓" : $"= {human} · Enter / tab to apply";
                TargetHintError = false;
            }
            else
            {
                TargetHint = "use e.g. 30s, 5m, 2h";
                TargetHintError = true;
            }
        }

        private void SetTarget()
        {
            if (!TryParseTarget(_targetText, out var secs, out _)) { ValidateTarget(); return; }
            _desiredVideoSeconds = secs;
            ValidateTarget();   // hint now reads "= … ✓"
            RefreshStats();     // projection / progress reflect the new target
            BumpRecalc();       // flash the affected stats
            TargetPulse++;      // pulse the field outline + "Target" label to confirm the commit
        }

        private static bool TryParseTarget(string? text, out int seconds, out string human)
        {
            seconds = 0; human = "";
            var t = (text ?? "").Trim().ToLowerInvariant();
            if (t.Length == 0) return false;
            double mult = 1;
            if (t.EndsWith("h")) { mult = 3600; t = t[..^1]; }
            else if (t.EndsWith("m")) { mult = 60; t = t[..^1]; }
            else if (t.EndsWith("s")) { mult = 1; t = t[..^1]; }
            if (double.TryParse(t.Trim(), out var v) && v > 0)
            {
                double totalSec = v * mult;
                if (totalSec < 1 || totalSec > 360000) return false;   // sub-1s rounds to 0; cap 100h (avoid int overflow)
                seconds = (int)totalSec;
                human = seconds >= 3600 ? $"{seconds / 3600.0:0.##} hr"
                      : seconds >= 60 ? $"{seconds / 60.0:0.##} min"
                      : $"{seconds} sec";
                return true;
            }
            return false;
        }

        private string _storageInfo = "";
        public string StorageInfo { get => _storageInfo; set => SetProperty(ref _storageInfo, value); }

        private string _resourcesInfo = "";
        public string ResourcesInfo { get => _resourcesInfo; set => SetProperty(ref _resourcesInfo, value); }

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

        // Bumped when the user changes an input (target / fps) that recalculates the stats — the
        // affected on-screen values flash briefly so you can see what got recomputed.
        private int _recalcPulse;
        public int RecalcPulse { get => _recalcPulse; set => SetProperty(ref _recalcPulse, value); }
        private void BumpRecalc() => RecalcPulse++;

        // Bumped only on an explicit Target commit (Enter / tab-away) to drive the field's confirm pulse.
        private int _targetPulse;
        public int TargetPulse { get => _targetPulse; set => SetProperty(ref _targetPulse, value); }

        private string _smartStatus = "";
        public string SmartStatus { get => _smartStatus; set => SetProperty(ref _smartStatus, value); }

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
            try
            {
                string? dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
                // Walk up a couple of levels so session.json / frames\00001.jpg / output\x.mp4 all resolve.
                for (int i = 0; dir != null && i < 3; i++, dir = Path.GetDirectoryName(dir))
                {
                    if (Directory.Exists(dir) && SessionManager.LoadSession(dir) != null)
                    {
                        // A parseable session.json OUTSIDE the captures root could be a stray copy (backup on
                        // the Desktop, an extracted zip) — loading it would make that folder the live capture
                        // target. Confirm rather than adopt silently.
                        string root = string.IsNullOrWhiteSpace(_settings.SaveFolder)
                            ? "" : Path.GetFullPath(Path.Combine(_settings.SaveFolder!, "captures"));
                        bool outsideRoot = root.Length == 0 ||
                            !Path.GetFullPath(dir).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                        if (outsideRoot)
                        {
                            var r = MessageBox.Show(
                                $"Load this session from outside your captures folder?\n\n{dir}\n\nNew frames would be captured into that folder.",
                                "Open session", MessageBoxButton.YesNo, MessageBoxImage.Question);
                            if (r != MessageBoxResult.Yes) return true;   // handled — don't show "not a session"
                        }
                        LoadSessionFromFolder(dir, fromPicker: false);
                        return true;
                    }
                }
            }
            catch { /* fall through to false */ }
            return false;
        }

        // ---- commands ----
        public ICommand ChooseFolderCommand { get; }
        public ICommand NewSessionCommand { get; }
        public ICommand LoadSessionCommand { get; }
        public ICommand RenameSessionCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenOverlayCommand { get; }
        public ICommand DismissCaptureErrorCommand { get; }
        public ICommand OpenLogCommand { get; }
        public ICommand OpenWizardCommand { get; }
        public ICommand ExportSettingsCommand { get; }
        public ICommand ImportSettingsCommand { get; }
        public ICommand SetTargetCommand { get; }
        public ICommand FullScreenCommand { get; }
        public ICommand TrackWindowCommand { get; }
        public ICommand SelectRegionCommand { get; }
        public ICommand EditRegionCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand PauseResumeCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand EncodeCommand { get; }
        public ICommand TrimCommand { get; }
        public ICommand CullCommand { get; }
        public ICommand DownloadFfmpegCommand { get; }
        public ICommand BrowseFfmpegCommand { get; }
        public ICommand CancelDownloadCommand { get; }
        public ICommand ShowOverlayCommand { get; }

        private void ChooseFolder()
        {
            var dlg = new OpenFolderDialog { Title = "Select output folder for captures" };
            // Open at the current folder — or its nearest ancestor that still exists (it may be deleted).
            string? seed = _settings.SaveFolder;
            while (!string.IsNullOrWhiteSpace(seed) && !Directory.Exists(seed))
                seed = Path.GetDirectoryName(seed);
            if (!string.IsNullOrWhiteSpace(seed)) dlg.InitialDirectory = seed;

            if (dlg.ShowDialog() == true)
            {
                _settings.SaveFolder = dlg.FolderName;
                SettingsManager.Save(_settings);
                OutputFolder = dlg.FolderName;
                RefreshOutputFolderMissing();
                OnPropertyChanged(nameof(HasSaveFolderSet));
                OnPropertyChanged(nameof(CapturesRootHint));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void NewSession()
        {
            // Guard against losing your place with a stray click: if the current session already
            // has frames, confirm before switching away (the old one stays safe on disk).
            if (_session != null && _frameCount > 0 && !IsCapturing)
            {
                var r = MessageBox.Show(
                    $"The current session “{SessionName}” has {_frameCount} frame(s).\n\nIt will be kept on disk, but a new session will replace it here. Start a new session?",
                    "New session?", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
            }

            // The current session with no frames yet IS a fresh session — don't spawn another folder;
            // the name prompt below just renames it if the user picks something different.
            bool reuseEmpty = _session != null && _sessionFolder != null && _frameCount == 0 && !IsCapturing;

            // Name it up front (prefilled — Enter accepts the default; Cancel aborts).
            string defaultName = $"Session_{DateTime.Now:yyyyMMdd_HHmmss}";
            var dlg = new TextPromptDialog("New session", "Session name",
                reuseEmpty ? (_session!.Name ?? defaultName) : defaultName)
            { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true) return;
            string name = string.IsNullOrWhiteSpace(dlg.Value) ? defaultName : dlg.Value.Trim();

            if (reuseEmpty)
            {
                if (!string.Equals(name, _session!.Name, StringComparison.Ordinal))
                    ApplySessionName(name);
                return;
            }
            CreateSession(name);
        }

        /// <summary>Create a default-named session if none exists — used by the setup wizard (no prompt mid-flow).</summary>
        public void EnsureDefaultSession()
        {
            if (!SessionNeeded || !HasOutputFolder || IsCapturing) return;
            CreateSession($"Session_{DateTime.Now:yyyyMMdd_HHmmss}");
        }

        private void CreateSession(string name)
        {
            try
            {
                string capturesRoot = Path.Combine(_settings.SaveFolder!, "captures");
                _sessionFolder = SessionManager.CreateNamedSession(
                    capturesRoot, name, _settings.IntervalSeconds, null, _settings.Format ?? "JPEG", _settings.JpegQuality);
                _session = SessionManager.LoadSession(_sessionFolder);
                _region = null;
                _accumulatedSeconds = 0;
                PreviewImage = null;
                ClearCaptureError();   // a fresh session starts with a clean slate

                SessionName = _session?.Name ?? name;
                RegionText = "Not selected";
                FrameCount = (int)(_session?.FramesCaptured ?? 0);
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(RegionNeeded));
                OnPropertyChanged(nameof(SessionNeeded));   // stop the New-Session pulse
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create session:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSession()
        {
            string capturesRoot = Path.Combine(_settings.SaveFolder ?? "", "captures");
            var dlg = new LoadSessionDialog(capturesRoot) { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true || dlg.SelectedFolder == null) return;
            LoadSessionFromFolder(dlg.SelectedFolder, fromPicker: true);
        }

        private void LoadSessionFromFolder(string folder, bool fromPicker)
        {
            var session = SessionManager.LoadSession(folder);
            if (session == null)
            {
                if (fromPicker)
                    MessageBox.Show("That folder doesn't contain a valid session (no session.json).",
                        "Load Session", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _session = session;
            _sessionFolder = folder;
            ClearCaptureError();   // loading a session clears any leftover warning

            // Restore the saved region. Keep its exact size; if its saved spot is no longer on any
            // monitor (display unplugged / resolution changed), relocate it onto the current desktop
            // rather than lose it — the size must stay constant to keep this session's frames uniform.
            _region = session.CaptureRegion;
            bool regionMoved = false, regionCantFit = false;
            if (_region.HasValue)
            {
                // A region from an older build / hand-edited / foreign session.json isn't guaranteed even or
                // sane. Force even dims (H.264) and reject a degenerate one, matching the fresh-selection path.
                var raw = _region.Value;
                raw.Width -= raw.Width % 2;
                raw.Height -= raw.Height % 2;
                if (raw.Width < 2 || raw.Height < 2)
                    _region = null;   // unusable → treated as "not selected" below
                else
                    _region = ScreenHelper.FitRegionOnScreen(raw, out regionMoved);
                regionCantFit = _region == null && raw.Width >= 2 && raw.Height >= 2;
            }

            _accumulatedSeconds = session.TotalCaptureSeconds; // restore cumulative capture time
            SessionName = session.Name ?? "Session";
            if (_region.HasValue)
            {
                var r = _region.Value;
                RegionText = regionMoved
                    ? $"{r.Width}×{r.Height} at ({r.X},{r.Y}) — moved onto screen"
                    : $"{r.Width}×{r.Height} at ({r.X},{r.Y})";
            }
            else
            {
                RegionText = regionCantFit
                    ? "Saved region doesn't fit this display — select again"
                    : "Not selected";
            }
            FrameCount = (int)session.FramesCaptured;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(RegionNeeded));
            OnPropertyChanged(nameof(SessionNeeded));   // stop the New-Session pulse once a session is loaded
            CommandManager.InvalidateRequerySuggested();
            UpdatePreview();
        }

        public bool AlwaysOnTop
        {
            get => _settings.AlwaysOnTop;
            set { if (_settings.AlwaysOnTop != value) { _settings.AlwaysOnTop = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public string Theme
        {
            get => _settings.Theme;
            set { if (_settings.Theme != value) { _settings.Theme = value; SettingsManager.Save(_settings); ThemeManager.Apply(value); OnPropertyChanged(); } }
        }

        public event Action? WindowAffinityChanged;
        public bool HideFromCapture
        {
            get => _settings.HideFromCapture;
            set { if (_settings.HideFromCapture != value) { _settings.HideFromCapture = value; SettingsManager.Save(_settings); OnPropertyChanged(); WindowAffinityChanged?.Invoke(); } }
        }

        // Window tracking: when the tracked window is minimized, wait for it to be restored (true) instead
        // of stopping capture (false, default). Only affects tracking mode.
        public bool PauseOnTrackedMinimize
        {
            get => _settings.PauseOnTrackedMinimize;
            set { if (_settings.PauseOnTrackedMinimize != value) { _settings.PauseOnTrackedMinimize = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        // Window tracking: force the tracked window to stay on top while capturing (truly un-occluded).
        public bool KeepTrackedWindowOnTop
        {
            get => _settings.KeepTrackedWindowOnTop;
            set { if (_settings.KeepTrackedWindowOnTop != value) { _settings.KeepTrackedWindowOnTop = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        // Auto-stop capture once the frame count reaches the Target (projected frames for the target length).
        public bool StopAtTarget
        {
            get => _settings.StopAtTarget;
            set { if (_settings.StopAtTarget != value) { _settings.StopAtTarget = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        // How a tracked window's resize is handled: 0 = lock size (crop), 1 = scale-to-fit (letterbox), 2 = stretch.
        public int TrackResizeMode
        {
            get => _settings.TrackResizeMode;
            set { if (_settings.TrackResizeMode != value) { _settings.TrackResizeMode = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        // Unattended safety: stop a run before the drive fills (a full disk fails writes + can disrupt other apps).
        public bool AutoStopOnLowDisk
        {
            get => _settings.AutoStopOnLowDisk;
            set { if (_settings.AutoStopOnLowDisk != value) { _settings.AutoStopOnLowDisk = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }
        public int LowDiskStopMB
        {
            get => _settings.LowDiskStopMB;
            set { var v = Math.Max(1, value); if (_settings.LowDiskStopMB != v) { _settings.LowDiskStopMB = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        // Opt-in: stop after a maximum accumulated capture duration (a hard wall-clock cap for unattended runs).
        public bool MaxDurationEnabled
        {
            get => _settings.MaxDurationEnabled;
            set { if (_settings.MaxDurationEnabled != value) { _settings.MaxDurationEnabled = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }
        public int MaxDurationMinutes
        {
            get => _settings.MaxDurationMinutes;
            set { var v = Math.Max(1, value); if (_settings.MaxDurationMinutes != v) { _settings.MaxDurationMinutes = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        // Sound + taskbar flash when a capture auto-stops or an encode finishes (so you don't have to watch).
        public bool NotifyOnFinish
        {
            get => _settings.NotifyOnFinish;
            set { if (_settings.NotifyOnFinish != value) { _settings.NotifyOnFinish = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public event Action? FinishNotified;
        private void NotifyFinished()
        {
            if (_settings.NotifyOnFinish)
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => FinishNotified?.Invoke()));
        }

        // Filename template for encoded/trimmed videos. Tokens resolved in ResolveOutputName().
        public string OutputNameTemplate
        {
            get => _settings.OutputNameTemplate;
            set { if (_settings.OutputNameTemplate != value) { _settings.OutputNameTemplate = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        private string ResolveOutputName()
        {
            var now = DateTime.Now;
            return (_settings.OutputNameTemplate ?? "")
                .Replace("{session}", SessionName ?? "")
                .Replace("{datetime}", now.ToString("yyyyMMdd_HHmmss"))
                .Replace("{date}", now.ToString("yyyyMMdd"))
                .Replace("{time}", now.ToString("HHmmss"));
        }

        public bool CaptureCursor
        {
            get => _settings.CaptureCursor;
            set { if (_settings.CaptureCursor != value) { _settings.CaptureCursor = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public bool OverlayTimestamp
        {
            get => _settings.OverlayTimestamp;
            set { if (_settings.OverlayTimestamp != value) { _settings.OverlayTimestamp = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public string OverlayText
        {
            get => _settings.OverlayText;
            set { if (_settings.OverlayText != value) { _settings.OverlayText = value ?? ""; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public int OverlayPosition
        {
            get => _settings.OverlayPosition;
            set { var v = value is < 0 or > 3 ? 3 : value; if (_settings.OverlayPosition != v) { _settings.OverlayPosition = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public int OverlayFontSize
        {
            get => _settings.OverlayFontSize;
            set { var v = value < 0 ? 0 : value; if (_settings.OverlayFontSize != v) { _settings.OverlayFontSize = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public string OverlayFontFamily
        {
            get => _settings.OverlayFontFamily;
            set { if (_settings.OverlayFontFamily != value) { _settings.OverlayFontFamily = value ?? "Consolas"; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        private OverlayConfig BuildOverlay() => new()
        {
            Enabled = _settings.OverlayTimestamp,
            Text = _settings.OverlayText,
            Position = _settings.OverlayPosition,
            FontSize = _settings.OverlayFontSize,
            FontFamily = _settings.OverlayFontFamily,
        };

        public bool OpenFolderAfterEncode
        {
            get => _settings.OpenFolderAfterEncode;
            set { if (_settings.OpenFolderAfterEncode != value) { _settings.OpenFolderAfterEncode = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        // ---- Global hotkey (off by default, configurable). The window registers/unregisters it. ----
        public event Action? HotkeysChanged;

        public bool HotkeysEnabled
        {
            get => _settings.HotkeysEnabled;
            set { if (_settings.HotkeysEnabled != value) { _settings.HotkeysEnabled = value; SettingsManager.Save(_settings); OnPropertyChanged(); HotkeysChanged?.Invoke(); } }
        }

        public int HotkeyModifiers => _settings.HotkeyModifiers;
        public int HotkeyVk => _settings.HotkeyVk;

        public string HotkeyDisplay
        {
            get
            {
                var parts = new List<string>();
                if ((_settings.HotkeyModifiers & 0x0002) != 0) parts.Add("Ctrl");
                if ((_settings.HotkeyModifiers & 0x0004) != 0) parts.Add("Shift");
                if ((_settings.HotkeyModifiers & 0x0001) != 0) parts.Add("Alt");
                if ((_settings.HotkeyModifiers & 0x0008) != 0) parts.Add("Win");
                parts.Add(KeyInterop.KeyFromVirtualKey(_settings.HotkeyVk).ToString());
                return string.Join(" + ", parts);
            }
        }

        /// <summary>Store a new hotkey combo (from the Settings key-capture field).</summary>
        public void SetHotkey(ModifierKeys mods, Key key)
        {
            uint fs = 0;
            if (mods.HasFlag(ModifierKeys.Alt)) fs |= 0x0001;
            if (mods.HasFlag(ModifierKeys.Control)) fs |= 0x0002;
            if (mods.HasFlag(ModifierKeys.Shift)) fs |= 0x0004;
            if (mods.HasFlag(ModifierKeys.Windows)) fs |= 0x0008;
            _settings.HotkeyModifiers = (int)fs;
            _settings.HotkeyVk = KeyInterop.VirtualKeyFromKey(key);
            SettingsManager.Save(_settings);
            OnPropertyChanged(nameof(HotkeyDisplay));
            HotkeysChanged?.Invoke();
        }

        private void OpenSettings()
        {
            var dlg = new SettingsDialog { Owner = Application.Current?.MainWindow, DataContext = this };
            dlg.ShowDialog();
        }

        private void OpenOverlay()
        {
            var dlg = new OverlayDialog { Owner = Application.Current?.MainWindow, DataContext = this };
            dlg.ShowDialog();
        }

        /// <summary>Guided setup — shown automatically on first run, re-runnable from Settings.</summary>
        public void OpenWizard()
        {
            var dlg = new SetupWizard { Owner = Application.Current?.MainWindow, DataContext = this };
            dlg.ShowDialog();
        }

        // Open the diagnostics log (or its folder if it doesn't exist yet) — observability for "what happened?".
        private void OpenLog()
        {
            try
            {
                string path = Logger.FilePath;
                string open = File.Exists(path) ? path : (Path.GetDirectoryName(path) ?? path);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = open, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't open the log: {ex.Message}", "Open log", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExportSettings()
        {
            var dlg = new SaveFileDialog { Title = "Export settings", Filter = "Settings (*.json)|*.json", FileName = "timelapse-settings.json" };
            if (dlg.ShowDialog() != true) return;
            try { SettingsManager.ExportTo(_settings, dlg.FileName); }
            catch (Exception ex) { MessageBox.Show($"Couldn't export settings: {ex.Message}", "Export settings", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void ImportSettings()
        {
            var dlg = new OpenFileDialog { Title = "Import settings", Filter = "Settings (*.json)|*.json" };
            if (dlg.ShowDialog() != true) return;
            var imported = SettingsManager.LoadFrom(dlg.FileName);
            if (imported == null)
            {
                MessageBox.Show("That file isn't a valid settings file.", "Import settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            NormalizeSettings(imported);   // an imported file bypasses the property-setter clamps — re-bound here
            _settings = imported;
            SettingsManager.Save(_settings);
            // Resync cached display state the blanket notify can't recompute (they have backing fields).
            OutputFolder = string.IsNullOrWhiteSpace(_settings.SaveFolder) ? "(not set)" : _settings.SaveFolder!;
            RefreshOutputFolderMissing();
            OnPropertyChanged(string.Empty); // refresh every binding against the new settings
        }

        // Clamp fields that aren't already re-clamped at point of use, so a hand-edited/foreign settings.json
        // can't push out-of-range values into the app. (JpegQuality/intervals/CRF are re-clamped in the engine/
        // encoder; EncodePreset is allowlisted in VideoEncoder — those don't need re-clamping here.)
        private static void NormalizeSettings(CaptureSettings s)
        {
            s.JpegQuality = Math.Clamp(s.JpegQuality, 1, 100);
            s.OverlayPosition = Math.Clamp(s.OverlayPosition, 0, 3);
            s.TrackResizeMode = Math.Clamp(s.TrackResizeMode, 0, 2);
            s.LowDiskStopMB = Math.Max(1, s.LowDiskStopMB);
            if (s.Format != "JPEG" && s.Format != "PNG") s.Format = "JPEG";
        }

        // Crash recovery: a session left Active means the app died mid-capture. On launch, offer to
        // resume the most-recently-touched such session; clear the flag on all of them either way.
        private void CheckForInterruptedSession()
        {
            if (_session != null || IsCapturing || string.IsNullOrEmpty(_settings.SaveFolder)) return;
            try
            {
                string capturesRoot = Path.Combine(_settings.SaveFolder!, "captures");
                if (!Directory.Exists(capturesRoot)) return;

                string? best = null;
                SessionInfo? bestInfo = null;
                var actives = new List<string>();
                foreach (var dir in Directory.GetDirectories(capturesRoot))
                {
                    var s = SessionManager.LoadSession(dir);
                    if (s == null || !s.Active || s.FramesCaptured <= 0) continue;
                    actives.Add(dir);
                    if (best == null || Directory.GetLastWriteTime(dir) > Directory.GetLastWriteTime(best))
                    {
                        best = dir;
                        bestInfo = s;
                    }
                }
                if (best == null || bestInfo == null) return;

                var r = MessageBox.Show(
                    $"The session “{bestInfo.Name ?? Path.GetFileName(best)}” was still recording when the app " +
                    $"last closed ({bestInfo.FramesCaptured} frame{(bestInfo.FramesCaptured == 1 ? "" : "s")}).\n\nResume it?",
                    "Resume interrupted session?", MessageBoxButton.YesNo, MessageBoxImage.Question);

                // Clear Active on every candidate so we don't keep prompting; the next Start re-marks it.
                foreach (var dir in actives) ClearActive(dir);
                if (r == MessageBoxResult.Yes) LoadSessionFromFolder(best, fromPicker: false);
            }
            catch { /* recovery is best-effort */ }
        }

        private static void ClearActive(string folder)
        {
            try
            {
                var s = SessionManager.LoadSession(folder);
                if (s == null) return;
                s.Active = false;
                SessionManager.SaveSession(folder, s);
            }
            catch { /* best-effort */ }
        }

        // Toggle the session's Active flag on disk (capture lifecycle: true on start, false on clean stop).
        private void SetSessionActive(bool active)
        {
            if (_sessionFolder == null) return;
            try
            {
                var s = SessionManager.LoadSession(_sessionFolder);
                if (s == null) return;
                s.Active = active;
                SessionManager.SaveSession(_sessionFolder, s);
                _session = s;
            }
            catch { /* best-effort */ }
        }

        /// <summary>
        /// Window is closing: stop any capture cleanly (which marks the session inactive, so a
        /// deliberate close isn't mistaken for a crash next launch) and dispose the engine.
        /// </summary>
        public void OnAppClosing()
        {
            try
            {
                if (IsCapturing) StopCapture();
                _engine.Dispose();
            }
            catch { /* best-effort shutdown */ }
        }

        /// <summary>Global-hotkey action: toggle capture, respecting the same gating as the buttons.</summary>
        public void ToggleCaptureHotkey()
        {
            if (IsCapturing)
            {
                if (StopCommand.CanExecute(null)) StopCommand.Execute(null);
            }
            else if (StartCommand.CanExecute(null))
            {
                StartCommand.Execute(null);
            }
        }

        private void RenameSession()
        {
            if (_session == null || _sessionFolder == null) return;
            var dlg = new TextPromptDialog("Rename session", "Session name", _session.Name ?? "")
            {
                Owner = Application.Current?.MainWindow
            };
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;
            ApplySessionName(dlg.Value.Trim());
        }

        // Rename the current session: folder to match (sanitised + de-duplicated), display name verbatim.
        // Shared by RenameSession and the New Session name prompt (reusing an empty session).
        private void ApplySessionName(string newName)
        {
            if (_session == null || _sessionFolder == null || string.IsNullOrWhiteSpace(newName)) return;
            try
            {
                string? parent = Path.GetDirectoryName(_sessionFolder);
                string safe = SanitizeFolderName(newName);
                if (parent != null && safe.Length > 0 &&
                    !string.Equals(Path.GetFileName(_sessionFolder), safe, StringComparison.OrdinalIgnoreCase))
                {
                    string target = Path.Combine(parent, safe);
                    int n = 2;
                    while (Directory.Exists(target)) target = Path.Combine(parent, $"{safe} ({n++})");
                    Directory.Move(_sessionFolder, target);
                    _sessionFolder = target;
                }

                var s = SessionManager.LoadSession(_sessionFolder) ?? _session;
                s.Name = newName;                                // display name kept verbatim
                SessionManager.SaveSession(_sessionFolder, s);
                _session = s;
                SessionName = s.Name;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't rename the session: {ex.Message}", "Rename",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string SanitizeFolderName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim().TrimEnd('.', ' '); // Windows folder names can't end with a dot or space
        }

        private bool ConfirmRegionChange()
        {
            if (_session != null && _frameCount > 0)
            {
                var r = MessageBox.Show(
                    $"This session already has {_frameCount} frame(s) at the current size.\n\nChanging the region will mix frame sizes and can break the final encode. Change it anyway?",
                    "Change region?", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                return r == MessageBoxResult.Yes;
            }
            return true;
        }

        private void ToggleOverlay()
        {
            if (_isOverlayShown)
            {
                _overlay?.Close();
                _overlay = null;
                IsOverlayShown = false;
            }
            else if (_region.HasValue)
            {
                _overlay ??= new RegionOverlay();
                _overlay.ShowForRegion(_region.Value);
                IsOverlayShown = true;
            }
            SyncTrackOverlay();
        }

        private void UpdateOverlay()
        {
            if (!_isOverlayShown) return;
            if (_region.HasValue && _overlay != null)
                _overlay.ShowForRegion(_region.Value);
            else
            {
                _overlay?.Close();
                _overlay = null;
                IsOverlayShown = false;
            }
            SyncTrackOverlay();
        }

        // Start/stop following the on-screen outline to the tracked window (only while the outline is shown
        // and a window is being tracked). The engine's capture already follows; this keeps the visible
        // outline in lock-step so it stays just-outside the captured region and never lands in a frame.
        private System.Drawing.Rectangle _lastOverlayRect;
        private void SyncTrackOverlay()
        {
            if (_isOverlayShown && _trackedWindow != IntPtr.Zero && _overlay != null)
            {
                if (!_trackOverlayTimer.IsEnabled)
                {
                    _lastOverlayRect = System.Drawing.Rectangle.Empty;   // force the first tick to position it
                    _trackOverlayTimer.Start();
                }
            }
            else
            {
                _trackOverlayTimer.Stop();
            }
        }

        private void OnTrackOverlayTick(object? sender, EventArgs e)
        {
            if (!_isOverlayShown || _trackedWindow == IntPtr.Zero || _overlay == null || !_region.HasValue)
            {
                _trackOverlayTimer.Stop();
                return;
            }
            if (!WindowEnumerator.TryGetLiveBounds(_trackedWindow, out var b, out bool minimized, out bool alive)
                || !alive || minimized)
                return;

            System.Drawing.Rectangle rect;
            if (_settings.TrackResizeMode == 0)   // lock size: outline is the locked box at the window's top-left
            {
                var locked = _region.Value;
                var candidate = new System.Drawing.Rectangle(b.X, b.Y, locked.Width, locked.Height);
                rect = ScreenHelper.FitRegionOnScreen(candidate, out _) ?? locked;
            }
            else                                  // scale modes: outline tracks the whole window (follows resize)
            {
                rect = b;
            }

            if (rect == _lastOverlayRect) return;   // unchanged → don't relayout the overlay window
            _lastOverlayRect = rect;
            _overlay.ShowForRegion(rect);
        }

        private void SelectFullScreen()
        {
            var monitors = ScreenHelper.Monitors();
            System.Drawing.Rectangle r;
            if (monitors.Count > 1)
            {
                var dlg = new MonitorPickerDialog(monitors) { Owner = Application.Current?.MainWindow };
                if (dlg.ShowDialog() != true || dlg.SelectedBounds == null) return;
                r = dlg.SelectedBounds.Value;
            }
            else
            {
                r = monitors[0].Bounds;
            }

            r.Width -= r.Width % 2;   // even dimensions required by the H.264 encoder
            r.Height -= r.Height % 2;

            // No change → no warning. Only prompt (about mixing frame sizes) if the region actually differs.
            if (!RegionEquals(_region, r) && !ConfirmRegionChange()) return;
            ApplyRegion(r, $"{r.Width}×{r.Height} (full screen)");
        }

        private static bool RegionEquals(System.Drawing.Rectangle? a, System.Drawing.Rectangle b)
            => a.HasValue && a.Value == b;

        // Pick a top-level window; capture follows it as it moves (size locked at the current size).
        private void TrackWindow()
        {
            if (!ConfirmRegionChange()) return;
            var dlg = new WindowPickerDialog { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true) return;
            if (!WindowEnumerator.TryGetLiveBounds(dlg.SelectedHwnd, out var b, out _, out bool alive) || !alive) return;

            int w = b.Width - b.Width % 2;     // even dims for the H.264 encoder
            int h = b.Height - b.Height % 2;
            if (w < 2 || h < 2) return;

            var r = new Rectangle(b.X, b.Y, w, h);
            ApplyRegion(r, $"{w}×{h} · tracking “{dlg.SelectedTitle}” (follows)", dlg.SelectedHwnd);
        }

        private void SelectRegion()
        {
            if (!ConfirmRegionChange()) return;
            var all = AspectRatio.CommonRatios;
            var ar = all[AspectRatioIndex >= 0 && AspectRatioIndex < all.Length ? AspectRatioIndex : 0];
            var overlay = new RegionSelectOverlay(ar.Width, ar.Height);
            if (overlay.ShowDialog() == true && overlay.SelectedRegion.HasValue)
                ApplyRegion(overlay.SelectedRegion.Value);
        }

        private void EditRegion()
        {
            if (!_region.HasValue) return;
            if (!ConfirmRegionChange()) return;
            var all = AspectRatio.CommonRatios;
            var ar = all[AspectRatioIndex >= 0 && AspectRatioIndex < all.Length ? AspectRatioIndex : 0];
            var dlg = new RegionEditOverlay(_region.Value, ar.Width, ar.Height);
            if (dlg.ShowDialog() == true && dlg.SelectedRegion.HasValue)
                ApplyRegion(dlg.SelectedRegion.Value);
        }

        private void ApplyRegion(System.Drawing.Rectangle r, string? label = null, IntPtr trackedWindow = default)
        {
            _trackedWindow = trackedWindow;   // every region source funnels here, so static sources reset to 0
            _region = r;
            RegionText = label ?? $"{r.Width}×{r.Height} at ({r.X},{r.Y})";
            PersistRegion(r);
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(RegionNeeded));
            CommandManager.InvalidateRequerySuggested();
            UpdateOverlay();
        }

        // The region is part of a session's identity (all its frames are that size and place), so save
        // it with the session — loading restores it and a continued session keeps the same area.
        private void PersistRegion(System.Drawing.Rectangle r)
        {
            if (_session == null || _sessionFolder == null) return;
            try
            {
                var s = SessionManager.LoadSession(_sessionFolder) ?? _session;
                s.CaptureRegion = r;
                SessionManager.SaveSession(_sessionFolder, s);
                _session = s;
            }
            catch { /* best-effort; never throw from a region change */ }
        }

        private void StartCapture()
        {
            if (_session == null || _sessionFolder == null || !_region.HasValue) return;

            // Pre-flight: don't begin a run onto a disk that's already below the low-disk safety limit
            // (it would auto-stop almost immediately) — let the user decide, but default to not starting.
            if (_settings.AutoStopOnLowDisk)
            {
                long freeMb = SystemMonitor.GetAvailableDiskSpaceMB(_sessionFolder);
                if (freeMb > 0 && freeMb < _settings.LowDiskStopMB)
                {
                    var r = MessageBox.Show(
                        $"Only {freeMb} MB free on the capture drive — below your {_settings.LowDiskStopMB} MB low-disk limit, so the run would stop almost immediately.\n\nStart anyway?",
                        "Low disk space", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (r != MessageBoxResult.Yes) return;
                }
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
            _engine.Start(_sessionFolder!, _session!, _region!.Value, (double)IntervalSeconds, _settings.Format ?? "JPEG",
                _settings.SmartIntervalEnabled, (double)_settings.IdleIntervalSeconds,
                _settings.IdleThresholdSeconds, _settings.SkipIdleFrames, _settings.JpegQuality,
                _settings.CaptureCursor, BuildOverlay(), _trackedWindow, _settings.PauseOnTrackedMinimize,
                _settings.TrackResizeMode);
            _captureStart = DateTime.Now;
            SmartStatus = _settings.SmartIntervalEnabled ? "Active" : "";
        }

        private void PauseResume()
        {
            if (!IsCapturing) return;
            if (_isPaused)
            {
                StartEngine();           // resume → new capture segment
                PinTrackedWindow();      // re-pin on resume (released while paused)
                IsPaused = false;
            }
            else
            {
                _engine.Stop();          // pause → stop capturing but keep the run armed
                UnpinTrackedWindow();    // don't leave the window jammed on top while paused
                if (_captureStart.HasValue)
                {
                    _accumulatedSeconds += (DateTime.Now - _captureStart.Value).TotalSeconds;
                    _captureStart = null;
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

                // Optional unattended stop: end the run once we've reached the target number of frames.
                long targetFrames = (long)_desiredVideoSeconds * Math.Max(1, EncodeFps);   // long: never wraps negative
                if (_settings.StopAtTarget && IsCapturing && targetFrames > 0 && count >= targetFrames)
                {
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
            long targetFrames = (long)_desiredVideoSeconds * Math.Max(1, EncodeFps);
            int target = (targetFrames > 0 && targetFrames < _frameCount) ? (int)targetFrames : 0;
            var dlg = new TrimDialog(_sessionFolder, _frameCount, target,
                $"{_desiredVideoSeconds}s @ {Math.Max(1, EncodeFps)}fps")
            { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() == true)
                await Encode(dlg.StartFrame, dlg.EndFrame);
        }

        // Review frames and delete fumbles, renumbering the rest so the sequence stays gapless (encodable).
        private void Cull()
        {
            if (_session == null || _sessionFolder == null || _frameCount < 1) return;
            var dlg = new CullDialog(_sessionFolder, _frameCount) { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true || dlg.MarkedForDeletion.Count == 0) return;

            int newCount = SessionManager.CullAndRenumber(_sessionFolder, new HashSet<int>(dlg.MarkedForDeletion));
            FrameCount = newCount;
            UpdatePreview();   // the "latest" frame likely changed
            CommandManager.InvalidateRequerySuggested();
        }

        private async Task Encode(int startFrame = 1, int endFrame = 0)
        {
            if (_session == null || _sessionFolder == null) return;

            var ffmpeg = FfmpegRunner.FindFfmpeg(_settings.FfmpegPath);
            if (string.IsNullOrEmpty(ffmpeg))
            {
                MessageBox.Show("FFmpeg was not found. Configure or download it first.",
                    "FFmpeg not found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _encodeCts = new System.Threading.CancellationTokenSource();
            IsEncoding = true;
            EncodeStatus = "Encoding…";

            VideoEncoder.Result? result = null;
            bool cancelled = false;
            try
            {
                int maxFrames = (endFrame >= startFrame && endFrame > 0) ? endFrame - startFrame + 1 : 0;
                result = await VideoEncoder.EncodeAsync(ffmpeg, _sessionFolder, EncodeFps, EncodePreset, EncodeCrf,
                    _encodeCts.Token, startFrame, maxFrames, ResolveOutputName());
            }
            catch (Exception ex)
            {
                EncodeStatus = "Encode failed";
                MessageBox.Show($"Encode failed:\n{ex.Message}", "Encode", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show($"Video encoded ({FormatBytes(bytes)}):\n{result.OutputPath}\n\nOpen the output folder?",
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
                MessageBox.Show($"Encode failed:\n{result.Error}", "Encode", MessageBoxButton.OK, MessageBoxImage.Error);
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
                RefreshFfmpegStatus();
                CommandManager.InvalidateRequerySuggested();
            }
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
                    MessageBox.Show("That file doesn't look like a working ffmpeg (it didn't respond to “-version”).",
                        "Select ffmpeg", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _settings.FfmpegPath = dlg.FileName;
                SettingsManager.Save(_settings);
                RefreshFfmpegStatus();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void RefreshStats()
        {
            try
            {
                _statsTick++;

                // Elapsed (current run) + total across start/stop — updated every tick (1s) so it counts smoothly.
                double current = (IsCapturing && _captureStart.HasValue) ? (DateTime.Now - _captureStart.Value).TotalSeconds : 0;
                ElapsedText = FormatTime(current);
                double totalCaptureSeconds = _accumulatedSeconds + current;
                TotalElapsedText = FormatTime(totalCaptureSeconds);

                // Opt-in max-duration cap: stop once accumulated capture time reaches the limit (a normal
                // completion — notify, but no red error banner).
                if (IsCapturing && _settings.MaxDurationEnabled && totalCaptureSeconds >= _settings.MaxDurationMinutes * 60.0)
                {
                    StopCapture();
                    NotifyFinished();
                    return;   // nothing else to refresh this tick
                }

                int projectedFrames = Math.Max(_frameCount, _desiredVideoSeconds * Math.Max(1, EncodeFps));
                double pct = projectedFrames > 0 ? Math.Min(100.0, _frameCount * 100.0 / projectedFrames) : 0;
                CaptureProgress = pct;
                ProgressText = $"{_frameCount} / {projectedFrames} frames · {pct:F0}% of a {_desiredVideoSeconds}s video @ {Math.Max(1, EncodeFps)}fps";

                double vidLen = EncodeFps > 0 ? _frameCount / (double)EncodeFps : 0;
                VideoLengthText = $"Video @ {EncodeFps}fps ≈ {vidLen:F1}s";

                // The storage/disk/memory probe reads frame files — throttle it to ~every 2s.
                if (_statsTick % 2 == 0 || string.IsNullOrEmpty(StorageInfo))
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
                    StorageInfo = SystemMonitor.GetStorageInfoString(_sessionFolder, w, h,
                        _settings.Format ?? "JPEG", _settings.JpegQuality, _frameCount, projectedFrames);
                    ResourcesInfo = SystemMonitor.GetResourcesInfoString();

                    // Unattended safety: stop before the drive fills (writes would start failing, and a full
                    // disk can disrupt other apps). freeMb == 0 is treated as a probe error and ignored — the
                    // threshold stop fires well before a genuine zero.
                    if (IsCapturing && _settings.AutoStopOnLowDisk && _sessionFolder != null)
                    {
                        long freeMb = SystemMonitor.GetAvailableDiskSpaceMB(_sessionFolder);
                        if (freeMb > 0 && freeMb < _settings.LowDiskStopMB)
                        {
                            StopCapture();
                            CaptureError = $"Capture stopped — low disk space ({freeMb} MB free, limit {_settings.LowDiskStopMB} MB). Free up space, then start again.";
                            NotifyFinished();
                        }
                    }
                }

                if (IsCapturing && _frameCount != _lastPreviewedFrame) UpdatePreview();   // only on a new frame
            }
            catch { /* stats are best-effort */ }
        }

        private static string FormatTime(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" : $"{t.Minutes:D2}:{t.Seconds:D2}";
        }

        private void RefreshFfmpegStatus()
        {
            var path = FfmpegRunner.FindFfmpeg(_settings.FfmpegPath);
            FfmpegStatus = string.IsNullOrEmpty(path) ? "Not found" : "Ready";
        }

        public void Dispose()
        {
            _statsTimer.Stop();
            _trackOverlayTimer.Stop();
            _trackOverlayTimer.Tick -= OnTrackOverlayTick;
            _engine.Dispose();
            _overlay?.Close();
        }
    }
}
