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
            RefreshTargetHint();

            _engine.FrameCaptured += OnFrameCaptured;
            _engine.CaptureFailed += OnCaptureFailed;
            _engine.SmartStatusChanged += OnSmartStatus;

            ChooseFolderCommand = new RelayCommand(_ => ChooseFolder(), _ => !IsCapturing);
            NewSessionCommand = new RelayCommand(_ => NewSession(), _ => HasOutputFolder && !IsCapturing);
            LoadSessionCommand = new RelayCommand(_ => LoadSession(), _ => HasOutputFolder && !IsCapturing && !IsEncoding);
            RenameSessionCommand = new RelayCommand(_ => RenameSession(), _ => _session != null && !IsCapturing);
            OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
            OpenOverlayCommand = new RelayCommand(async _ => await OpenOverlay());
            DismissCaptureErrorCommand = new RelayCommand(_ => ClearCaptureError());
            OpenLogCommand = new RelayCommand(_ => OpenLog());
            OpenWizardCommand = new RelayCommand(_ => OpenWizard(), _ => !IsCapturing);
            ExportSettingsCommand = new RelayCommand(_ => ExportSettings());
            ImportSettingsCommand = new RelayCommand(_ => ImportSettings());
            ApplyPresetCommand = new RelayCommand(_ => ApplyPreset(), _ => SelectedPreset != null && !IsCapturing);
            SavePresetCommand = new RelayCommand(_ => SavePreset());
            RenamePresetCommand = new RelayCommand(_ => RenamePreset(), _ => SelectedPreset != null);
            DeletePresetCommand = new RelayCommand(_ => DeletePreset(), _ => SelectedPreset != null);
            RestoreDefaultsCommand = new RelayCommand(_ => RestoreDefaults(), _ => !IsCapturing);
            RefreshPresets();   // no built-in presets — users create their own

            // After the window is up, check whether a previous run was interrupted mid-capture.
            Application.Current?.Dispatcher.BeginInvoke(new Action(CheckForInterruptedSession), DispatcherPriority.Background);
            FullScreenCommand = new RelayCommand(_ => SelectFullScreen(), _ => _session != null && !IsCapturing);
            TrackWindowCommand = new RelayCommand(_ => TrackWindow(), _ => _session != null && !IsCapturing);
            SelectRegionCommand = new RelayCommand(_ => SelectRegion(), _ => _session != null && !IsCapturing);
            EditRegionCommand = new RelayCommand(_ => EditRegion(), _ => _session != null && _region.HasValue && !IsCapturing);
            StartCommand = new RelayCommand(_ => StartCapture(), _ => _session != null && _region.HasValue && !IsCapturing && !IsEncoding);
            StopCommand = new RelayCommand(_ => StopByUser(), _ => IsCapturing);
            PauseResumeCommand = new RelayCommand(_ => PauseResume(), _ => IsCapturing);
            OpenFolderCommand = new RelayCommand(_ => OpenSessionFolder(), _ => CanOpenFolder);
            EncodeCommand = new RelayCommand(async _ => await EncodeOrCancel(), _ => CanEncode || IsEncoding);
            TrimCommand = new RelayCommand(async _ => await Trim(), _ => CanEncode);
            CullCommand = new RelayCommand(_ => Cull(), _ => CanEncode);
            CropCommand = new RelayCommand(async _ => await Crop(), _ => CanEncode);
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
                // Ceiling 3600s (one frame an hour) — beyond that is almost certainly a typo, not a plan.
                decimal v = value < 0.1m ? 0.1m : value > 3600m ? 3600m : value;
                // Normalize: 4 dp is plenty, and strip decimal's trailing-zero scale so a pasted
                // "0.1000000000000000000000000000" (or an fps round-trip artifact) displays as "0.1".
                v = decimal.Parse(Math.Round(v, 4).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.CultureInfo.InvariantCulture);
                if (_settings.IntervalSecondsExact != v)
                {
                    _settings.IntervalSecondsExact = v;
                    _settings.IntervalSeconds = (int)Math.Max(1, Math.Round(v)); // rounded metadata for int consumers
                    SettingsManager.Save(_settings);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(SpeedNotch));
                    OnPropertyChanged(nameof(SpeedHint)); OnPropertyChanged(nameof(SpeedHintNamed));
                }
                // A clamped/rounded entry must be VISIBLE: re-notify after the binding transfer completes so
                // the field snaps back to the real value instead of displaying e.g. "0.01" while running 0.1s.
                // (Raised deferred and unconditionally — WPF can ignore a PropertyChanged fired inside the
                // same transfer, and the fps view needs refreshing when edited via seconds and vice versa.)
                // (The red "adjusted" flash is the generic ClampFlash behavior on the field itself —
                // it detects the snap-back by comparing typed vs kept, no VM wiring needed.)
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    OnPropertyChanged(nameof(IntervalSeconds));
                    OnPropertyChanged(nameof(CaptureFps));
                }));
            }
        }

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
                return $"every {interval}s  ·  ≈{framesPerMin:F0} frames/min  ·  a 1-hour session → ~{oneHourVideoSec:F0}s video @ {fps}fps";
            }
        }

        // The slider surfaces (Simple panel, setup wizard) prefix the notch name — that's THEIR
        // vocabulary. The advanced interval row binds the plain SpeedHint so wheeling through values
        // doesn't flash "Fine/Standard/…" at a power user.
        public string SpeedHintNamed
        {
            get
            {
                string outcome = SpeedHint;
                if (outcome.Length == 0) return "";
                string name = SpeedIntervals[SpeedNotch] == IntervalSeconds ? SpeedNames[SpeedNotch] : "Custom";
                return $"{name}  ·  {outcome}";
            }
        }

        public bool SmartEnabled
        {
            get => _settings.SmartIntervalEnabled;
            set { if (_settings.SmartIntervalEnabled != value) { _settings.SmartIntervalEnabled = value; SettingsManager.Save(_settings); OnPropertyChanged(); OnPropertyChanged(nameof(SmartSummaryText)); } }
        }

        // ---- Progressive disclosure: the tuning sections fold away; a summary keeps values glanceable ----
        public bool SmartPanelExpanded
        {
            get => _settings.SmartPanelExpanded;
            set { if (_settings.SmartPanelExpanded != value) { _settings.SmartPanelExpanded = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public bool EncodePanelExpanded
        {
            get => _settings.EncodePanelExpanded;
            set { if (_settings.EncodePanelExpanded != value) { _settings.EncodePanelExpanded = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public string SmartSummaryText =>
            !SmartEnabled ? "off — captures at the main interval throughout"
            : SkipIdleFrames ? $"on — skips frames after {IdleThresholdSeconds}s idle"
            : $"on — slows to every {IdleIntervalSeconds}s after {IdleThresholdSeconds}s idle";

        public string EncodeSummaryText =>
            $"{Math.Max(1, EncodeFps)} fps · CRF {EncodeCrf} · " +
            (EncodePreset == "ultrafast" ? "Fast" : EncodePreset == "veryslow" ? "Slow" : "Medium") +
            (SpeedUpEnabled && EncodeEveryNth > 1 ? $" · 1 in {EncodeEveryNth}" : "") +
            (EncodeHoldLastSeconds > 0 ? $" · hold {EncodeHoldLastSeconds}s" : "");

        public decimal IdleIntervalSeconds
        {
            get => _settings.IdleIntervalSeconds;
            set { var v = value < 0.1m ? 0.1m : value; if (_settings.IdleIntervalSeconds != v) { _settings.IdleIntervalSeconds = v; SettingsManager.Save(_settings); OnPropertyChanged(); OnPropertyChanged(nameof(SmartSummaryText)); } }
        }

        public int IdleThresholdSeconds
        {
            get => _settings.IdleThresholdSeconds;
            set { var v = value < 1 ? 1 : value; if (_settings.IdleThresholdSeconds != v) { _settings.IdleThresholdSeconds = v; SettingsManager.Save(_settings); OnPropertyChanged(); OnPropertyChanged(nameof(SmartSummaryText)); } }
        }

        public bool SkipIdleFrames
        {
            get => _settings.SkipIdleFrames;
            set { if (_settings.SkipIdleFrames != value) { _settings.SkipIdleFrames = value; SettingsManager.Save(_settings); OnPropertyChanged(); OnPropertyChanged(nameof(SmartSummaryText)); } }
        }

        // Settings-backed so they persist across restart and travel in presets (they carry no identity).
        public int EncodeFps
        {
            get => _settings.EncodeFps;
            set { var v = Math.Clamp(value, 1, 240); if (_settings.EncodeFps != v) { _settings.EncodeFps = v; SettingsManager.Save(_settings); OnPropertyChanged(); RefreshStats(); BumpRecalc(); OnPropertyChanged(nameof(SpeedHint)); OnPropertyChanged(nameof(SpeedHintNamed)); OnPropertyChanged(nameof(EncodeSummaryText)); } }
        }
        public int EncodeCrf
        {
            get => _settings.EncodeCrf;
            set { var v = Math.Clamp(value, 0, 51); if (_settings.EncodeCrf != v) { _settings.EncodeCrf = v; SettingsManager.Save(_settings); OnPropertyChanged(); OnPropertyChanged(nameof(EncodeSummaryText)); } }
        }
        // Hold the final frame for N seconds at the end (0 = off) — the finished artwork lingers.
        public double EncodeHoldLastSeconds
        {
            get => _settings.EncodeHoldLastSeconds;
            set { var v = Math.Clamp(value, 0, 60); if (_settings.EncodeHoldLastSeconds != v) { _settings.EncodeHoldLastSeconds = v; SettingsManager.Save(_settings); OnPropertyChanged(); OnPropertyChanged(nameof(EncodeSummaryText)); } }
        }

        public int JpegQuality
        {
            get => _settings.JpegQuality;
            set { var v = value < 1 ? 1 : (value > 100 ? 100 : value); if (_settings.JpegQuality != v) { _settings.JpegQuality = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public string EncodePreset
        {
            get => string.IsNullOrWhiteSpace(_settings.EncodePreset) ? "medium" : _settings.EncodePreset;
            set { if (!string.Equals(_settings.EncodePreset, value, StringComparison.OrdinalIgnoreCase)) { _settings.EncodePreset = value; SettingsManager.Save(_settings); OnPropertyChanged(); OnPropertyChanged(nameof(EncodeSummaryText)); } }
        }

        // Flip the locked ratio's orientation (16:9 ⇄ 9:16, 4:3 ⇄ 3:4). Deliberately TRANSIENT —
        // not persisted — so it's always off by default per Spike's call.
        // Like the Crop dialog's flip, toggling ALSO transposes the current static region about its
        // centre right away — the on-screen outline answers the click; no re-select needed to see it.
        private bool _ratioFlipped;
        public bool RatioFlipped
        {
            get => _ratioFlipped;
            set
            {
                if (!SetProperty(ref _ratioFlipped, value)) return;
                if (_region is not { } r || _trackedWindow != IntPtr.Zero || IsCapturing) return;
                if (r.Width == r.Height) return;   // a square transposes to itself — nothing to show
                // Same gate as every other region source — a session with frames gets the (suppressible)
                // scale note first. Declining reverts the toggle: nothing happened.
                if (!ConfirmRegionChange())
                {
                    _ratioFlipped = !value;
                    OnPropertyChanged(nameof(RatioFlipped));
                    return;
                }
                var flipped = new System.Drawing.Rectangle(
                    r.X + (r.Width - r.Height) / 2, r.Y + (r.Height - r.Width) / 2, r.Height, r.Width);
                // The transpose can poke off-screen (tall flip near an edge) — relocate like a loaded region.
                flipped = ScreenHelper.FitRegionOnScreen(flipped, out _) ?? r;
                if (flipped.Width >= 2 && flipped.Height >= 2 && flipped != r)
                    ApplyRegion(flipped);
            }
        }

        // The effective ratio for region select/edit: the chosen preset, flipped when armed.
        private (int w, int h) EffectiveRatio()
        {
            var all = AspectRatio.CommonRatios;
            var ar = all[AspectRatioIndex >= 0 && AspectRatioIndex < all.Length ? AspectRatioIndex : 0];
            return _ratioFlipped ? (ar.Height, ar.Width) : (ar.Width, ar.Height);
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
                    var r = MessageDialog.Show(
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
                    NotifyCaptureState();
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
                    OnPropertyChanged(nameof(PauseResumeText));
                    NotifyCaptureState();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }
        public bool IsRecording => IsCapturing && !_isPaused && !IsCaptureIdle;   // pulsing red REC dot: actively grabbing
        public string RecLabel => _isPaused ? "PAUSED" : IsCaptureIdle ? "IDLE" : "REC";
        public string PauseResumeText => _isPaused ? "▶  Resume" : "⏸  Pause";

        // Unified capture-state indicators (header pill + tray icon). "Idle" = smart-interval inactivity
        // (slowed or skipping); "IdleOrPaused" is the amber state, distinct from red active recording.
        public bool IsCaptureIdle => IsCapturing && !_isPaused &&
            (_smartStatus?.StartsWith("Idle", StringComparison.OrdinalIgnoreCase) ?? false);
        public bool IsCaptureIdleOrPaused => IsCapturing && (_isPaused || IsCaptureIdle);
        // The smart-activity detail shown beside the REC pill (empty unless capturing with smart on).
        public string CaptureStatusDetail => IsCapturing && _settings.SmartIntervalEnabled ? (_smartStatus ?? "") : "";

        private void NotifyCaptureState()
        {
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(RecLabel));
            OnPropertyChanged(nameof(IsCaptureIdle));
            OnPropertyChanged(nameof(IsCaptureIdleOrPaused));
            OnPropertyChanged(nameof(CaptureStatusDetail));
        }

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

        // Live encode progress (0–100), fed by ffmpeg's "frame=" stderr lines — drives the bar under the button.
        private double _encodeProgress;
        public double EncodeProgress { get => _encodeProgress; set => SetProperty(ref _encodeProgress, value); }

        // Plain text (no emoji): the 🎬 clapper fell back to Segoe UI Emoji, whose tall line metrics
        // inflated the button height — the "why is the render button REALLY tall" report.
        public string EncodeButtonText => IsEncoding ? "Cancel encode" : "Encode Video";

        private bool _isFfmpegBusy;
        public bool IsFfmpegBusy
        {
            get => _isFfmpegBusy;
            set
            {
                if (SetProperty(ref _isFfmpegBusy, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                    OnPropertyChanged(nameof(FfmpegControlsVisible));
                    OnPropertyChanged(nameof(FfmpegChangeVisible));
                }
            }
        }

        // Once ffmpeg is Ready the Download/Browse row folds away behind a slim "Change…" link —
        // setup shouldn't keep charging rent on the encoder card. Auto-expands while not found/busy.
        private bool _ffmpegReady;
        private bool _ffmpegSetupExpanded;
        public bool FfmpegControlsVisible => _ffmpegSetupExpanded || IsFfmpegBusy || !_ffmpegReady;
        public bool FfmpegChangeVisible => !FfmpegControlsVisible;
        public void ExpandFfmpegSetup()
        {
            _ffmpegSetupExpanded = true;
            OnPropertyChanged(nameof(FfmpegControlsVisible));
            OnPropertyChanged(nameof(FfmpegChangeVisible));
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

        // The target value, in seconds. TargetKind decides what it MEANS: a video length to aim for
        // (frames goal, the original behaviour) or a recording timer (stop after this much active
        // capture — paused time doesn't count, which is what makes pause useful mid-run).
        private int _targetSeconds = 30;
        private double _timerRunBase;   // _accumulatedSeconds when this run started — the rec-timer datum

        private int _targetKind;   // 0 = video length · 1 = recording timer. Transient, like the value.
        public int TargetKind
        {
            get => _targetKind;
            set
            {
                if (SetProperty(ref _targetKind, value))
                {
                    OnPropertyChanged(nameof(StopAtTargetVisible));
                    RefreshTargetHint();
                    UpdateCaptureToTarget();
                    RefreshStats();
                    BumpRecalc();
                    TargetPulse++;
                }
            }
        }

        /// <summary>The "Stop at target" checkbox only applies to the video-length kind — a timer always stops.</summary>
        public bool StopAtTargetVisible => _targetKind == 0;

        // Three wheel-friendly boxes (h / m / s). Each setter recomputes the total from its component,
        // so overflow normalizes on commit: typing 90 into minutes reads back as 1h 30m.
        public int TargetHours
        {
            get => _targetSeconds / 3600;
            set => CommitTarget((long)Math.Max(0, value) * 3600 + _targetSeconds % 3600);
        }
        public int TargetMinutes
        {
            get => _targetSeconds % 3600 / 60;
            set => CommitTarget((long)(_targetSeconds / 3600) * 3600 + (long)Math.Max(0, value) * 60 + _targetSeconds % 60);
        }
        public int TargetSecondsBox
        {
            get => _targetSeconds % 60;
            set => CommitTarget((long)(_targetSeconds / 60) * 60 + Math.Max(0, value));
        }

        private string _targetHint = "";
        public string TargetHint { get => _targetHint; set => SetProperty(ref _targetHint, value); }

        private bool _targetHintError;
        public bool TargetHintError { get => _targetHintError; set => SetProperty(ref _targetHintError, value); }

        private void CommitTarget(long totalSeconds)
        {
            if (totalSeconds < 1)
            {
                TargetHint = "target must be at least 1 second";
                TargetHintError = true;
                NotifyTargetBoxes();   // snap the boxes back to the kept value
                return;
            }
            int clamped = (int)Math.Min(totalSeconds, 360000);   // 100h cap — keeps the frames math in int range
            bool changed = clamped != _targetSeconds;
            _targetSeconds = clamped;
            NotifyTargetBoxes();
            RefreshTargetHint();
            if (changed)
            {
                UpdateCaptureToTarget();
                RefreshStats();     // projection / progress reflect the new target
                BumpRecalc();       // flash the affected stats
                TargetPulse++;      // pulse the field outline + "Target" label to confirm the commit
            }
        }

        private void NotifyTargetBoxes()
        {
            OnPropertyChanged(nameof(TargetHours));
            OnPropertyChanged(nameof(TargetMinutes));
            OnPropertyChanged(nameof(TargetSecondsBox));
        }

        private void RefreshTargetHint()
        {
            TargetHintError = false;
            TargetHint = _targetKind == 1
                ? $"= record for {HumanDuration(_targetSeconds)}, then stop"
                : $"= a {HumanDuration(_targetSeconds)} video";
        }

        /// <summary>Clears every "don't ask again" choice; returns how many confirmations came back.</summary>
        public int ResetSuppressedPrompts()
        {
            int n = _settings.SuppressedPrompts?.Count ?? 0;
            if (n > 0)
            {
                _settings.SuppressedPrompts!.Clear();
                SettingsManager.Save(_settings);
            }
            return n;
        }

        // The target isn't per-session state — reset on session switches / restore-defaults.
        private void ResetTarget()
        {
            _targetSeconds = 30;
            _targetKind = 0;
            OnPropertyChanged(nameof(TargetKind));
            OnPropertyChanged(nameof(StopAtTargetVisible));
            NotifyTargetBoxes();
            RefreshTargetHint();
        }

        /// <summary>Seconds of ACTIVE recording in the current run (pause excluded) — the rec-timer's clock.</summary>
        private double RunActiveSeconds()
        {
            double current = (IsCapturing && _captureStart.HasValue) ? (DateTime.Now - _captureStart.Value).TotalSeconds : 0;
            return Math.Max(0, _accumulatedSeconds + current - _timerRunBase);
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

            if (TargetKind == 1)
            {
                // Recording timer: the readout is simply time left on the clock (active time only).
                double remaining = Math.Max(0, _targetSeconds - RunActiveSeconds());
                CaptureToTargetText = IsCapturing
                    ? (remaining == 0 ? "✓ timer reached" : $"≈ {HumanDuration(remaining)} left on the timer (pause doesn't count)")
                    : $"will record for {HumanDuration(_targetSeconds)}, then stop";
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
        public ICommand CropCommand { get; }
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
                var r = MessageDialog.Show(
                    $"The current session “{SessionName}” has {_frameCount} frame(s).\n\nIt will be kept on disk, but a new session will replace it here. Start a new session?",
                    "New session?", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
            }

            // If the current session has no frames, recycle its folder instead of spawning another empty
            // one — but still make it a genuine fresh session (new default name + cleared region/target),
            // so "New Session" always behaves like a new session, not a rename.
            bool reuseEmpty = _session != null && _sessionFolder != null && _frameCount == 0 && !IsCapturing;

            // Name it up front (prefilled with a fresh default — Enter accepts it; Cancel aborts).
            string defaultName = $"Session_{DateTime.Now:yyyyMMdd_HHmmss}";
            var dlg = new TextPromptDialog("New session", "Session name", defaultName)
            { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true) return;
            string name = string.IsNullOrWhiteSpace(dlg.Value) ? defaultName : dlg.Value.Trim();

            if (reuseEmpty)
            {
                if (!string.Equals(name, _session!.Name, StringComparison.Ordinal))
                    ApplySessionName(name);   // rename the recycled folder
                ResetToFreshSession();        // clean slate: clear region/tracking/overlay/target
                return;
            }
            CreateSession(name);
        }

        // Reset the runtime capture state to a clean, empty-session slate (shared by CreateSession and
        // the recycle-empty path). Does NOT touch the session folder/name.
        private void ResetToFreshSession()
        {
            _region = null;
            _trackedWindow = IntPtr.Zero;
            _accumulatedSeconds = 0;
            ResetTarget();
            PreviewImage = null;
            ClearCaptureError();
            RegionText = "Not selected";
            FrameCount = (int)(_session?.FramesCaptured ?? 0);
            UpdateOverlay();
            RefreshCropInfo();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(RegionNeeded));
            OnPropertyChanged(nameof(SessionNeeded));
            CommandManager.InvalidateRequerySuggested();
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
                SessionName = _session?.Name ?? name;
                ResetToFreshSession();   // clean slate (region/tracking/overlay/target/preview/frame count)
            }
            catch (Exception ex)
            {
                MessageDialog.Show($"Failed to create session:\n{ex.Message}", "Error",
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
                    MessageDialog.Show("That folder doesn't contain a valid session (no session.json).",
                        "Load Session", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _session = session;
            _sessionFolder = folder;
            _trackedWindow = IntPtr.Zero;   // a loaded session is a static region (tracking isn't persisted)
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
            ResetTarget();   // target isn't per-session — reset, don't carry over
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
                    ? (session.FramesCaptured > 0
                        ? "Saved region doesn't fit this display — select any area; it'll be scaled to match this session's frames"
                        : "Saved region doesn't fit this display — select again")
                    : "Not selected";
            }
            FrameCount = (int)session.FramesCaptured;
            UpdateOverlay();   // refresh the on-screen outline to the loaded region (or close it if none)
            RefreshCropInfo(); // the loaded session may carry a saved encode-crop
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

        public bool HideWindowDuringRegionSelect
        {
            get => _settings.HideWindowDuringRegionSelect;
            set { if (_settings.HideWindowDuringRegionSelect != value) { _settings.HideWindowDuringRegionSelect = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public bool MinimizeToTray
        {
            get => _settings.MinimizeToTray;
            set { if (_settings.MinimizeToTray != value) { _settings.MinimizeToTray = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public bool CloseToTray
        {
            get => _settings.CloseToTray;
            set { if (_settings.CloseToTray != value) { _settings.CloseToTray = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public bool SoundOnStartStop
        {
            get => _settings.SoundOnStartStop;
            set { if (_settings.SoundOnStartStop != value) { _settings.SoundOnStartStop = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        // Live-frame preview card — collapsed by default so it doesn't push the window taller for a
        // feature you don't always need at a glance. Expanding it refreshes the current frame.
        public bool PreviewExpanded
        {
            get => _settings.PreviewExpanded;
            set { if (_settings.PreviewExpanded != value) { _settings.PreviewExpanded = value; SettingsManager.Save(_settings); OnPropertyChanged(); if (value) UpdatePreview(); } }
        }

        // Audio cue on explicit start/stop (opt-in) — useful feedback when the window is hidden in the
        // tray. Auto-stops use the separate finish notification, so this only fires on user actions.
        private void PlayStartStopCue()
        {
            if (_settings.SoundOnStartStop)
                try { System.Media.SystemSounds.Beep.Play(); } catch { }
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
            set
            {
                if (_settings.KeepTrackedWindowOnTop == value) return;
                _settings.KeepTrackedWindowOnTop = value;
                SettingsManager.Save(_settings);
                // React live while actively capturing, so the toggle isn't a no-op mid-run (and turning
                // it OFF doesn't strand the window topmost until the next stop). Both calls self-guard.
                if (IsCapturing && !_isPaused) { if (value) PinTrackedWindow(); else UnpinTrackedWindow(); }
                OnPropertyChanged();
            }
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
        // Encode speed-up: keep 1 frame in every N (stored 1 = off). Non-destructive — frames stay on
        // disk. Surfaced as a checkbox + N field (the app's reveal pattern) so "off" is explicit
        // instead of a magic 1, and the visible field can honestly enforce N ≥ 2.
        public int EncodeEveryNth
        {
            get => _settings.EncodeEveryNth;
            set
            {
                var v = Math.Clamp(value, 1, 1000);
                if (_settings.EncodeEveryNth != v)
                {
                    _settings.EncodeEveryNth = v;
                    SettingsManager.Save(_settings);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SpeedUpEnabled));
                    OnPropertyChanged(nameof(SpeedUpN));
                    OnPropertyChanged(nameof(EncodeSummaryText));
                }
            }
        }
        public bool SpeedUpEnabled
        {
            get => EncodeEveryNth > 1;
            set => EncodeEveryNth = value ? Math.Max(2, EncodeEveryNth) : 1;
        }
        public int SpeedUpN
        {
            get => Math.Max(2, EncodeEveryNth);
            set => EncodeEveryNth = Math.Clamp(value, 2, 1000);
        }

        public bool StopAtStorageEnabled
        {
            get => _settings.StopAtStorageEnabled;
            set { if (_settings.StopAtStorageEnabled != value) { _settings.StopAtStorageEnabled = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }
        public int StopAtStorageMB
        {
            get => _settings.StopAtStorageMB;
            set { var v = Math.Max(10, value); if (_settings.StopAtStorageMB != v) { _settings.StopAtStorageMB = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
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
            set
            {
                var v = value is < 0 or > 3 ? 3 : value;
                bool wasCustom = OverlayUsesCustom;
                // Choosing a corner exits free-placement mode.
                _settings.OverlayCustomX = -1; _settings.OverlayCustomY = -1;
                if (_settings.OverlayPosition != v || wasCustom)
                {
                    _settings.OverlayPosition = v;
                    SettingsManager.Save(_settings);
                    OnPropertyChanged();
                    NotifyOverlayDerived();
                }
            }
        }

        // Free placement (drag-to-place). CustomX/Y are the normalized (0..1) top-left of the text;
        // < 0 means "use the corner Position instead". The overlay dialog reads these for the preview.
        public double OverlayCustomX => _settings.OverlayCustomX;
        public double OverlayCustomY => _settings.OverlayCustomY;
        public bool OverlayUsesCustom => _settings.OverlayCustomX >= 0 && _settings.OverlayCustomY >= 0;

        /// <summary>Place the overlay freely (from a drag on the preview, or the X/Y fields).</summary>
        public void SetOverlayCustomNormalized(double x, double y)
        {
            _settings.OverlayCustomX = Math.Clamp(x, 0, 1);
            _settings.OverlayCustomY = Math.Clamp(y, 0, 1);
            SettingsManager.Save(_settings);
            OnPropertyChanged(nameof(OverlayCustomX));
            OnPropertyChanged(nameof(OverlayCustomY));
            NotifyOverlayDerived();
        }

        // Bound by the corner segmented control: returns "-1" (no corner highlighted) while in free
        // placement, so the segs visibly deselect; setting it picks a corner and exits free placement.
        public string OverlayCornerSelection
        {
            get => OverlayUsesCustom ? "-1" : _settings.OverlayPosition.ToString();
            set { if (int.TryParse(value, out int p) && p is >= 0 and <= 3) OverlayPosition = p; }
        }

        // The free-placement X/Y as whole percents, for the numeric fields (only meaningful in custom mode).
        public int OverlayPosXPercent
        {
            get => OverlayUsesCustom ? (int)Math.Round(_settings.OverlayCustomX * 100) : 0;
            set => SetOverlayCustomNormalized(Math.Clamp(value, 0, 100) / 100.0,
                       _settings.OverlayCustomY >= 0 ? _settings.OverlayCustomY : 0);
        }
        public int OverlayPosYPercent
        {
            get => OverlayUsesCustom ? (int)Math.Round(_settings.OverlayCustomY * 100) : 0;
            set => SetOverlayCustomNormalized(_settings.OverlayCustomX >= 0 ? _settings.OverlayCustomX : 0,
                       Math.Clamp(value, 0, 100) / 100.0);
        }

        private void NotifyOverlayDerived()
        {
            OnPropertyChanged(nameof(OverlayUsesCustom));
            OnPropertyChanged(nameof(OverlayCornerSelection));
            OnPropertyChanged(nameof(OverlayPosXPercent));
            OnPropertyChanged(nameof(OverlayPosYPercent));
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

        // Colour/opacity for the overlay text and its backdrop box. Hex setters keep the last valid
        // value on bad input (ClampFlash's UpdateTarget snaps the box back on blur).
        public string OverlayTextColor
        {
            get => _settings.OverlayTextColor;
            set { if (TryNormalizeHex(value, out var v) && _settings.OverlayTextColor != v) { _settings.OverlayTextColor = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }
        public int OverlayTextOpacity
        {
            get => _settings.OverlayTextOpacity;
            set { var v = Math.Clamp(value, 0, 100); if (_settings.OverlayTextOpacity != v) { _settings.OverlayTextOpacity = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }
        public string OverlayBackColor
        {
            get => _settings.OverlayBackColor;
            set { if (TryNormalizeHex(value, out var v) && _settings.OverlayBackColor != v) { _settings.OverlayBackColor = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }
        public int OverlayBackOpacity
        {
            get => _settings.OverlayBackOpacity;
            set { var v = Math.Clamp(value, 0, 100); if (_settings.OverlayBackOpacity != v) { _settings.OverlayBackOpacity = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        private static bool TryNormalizeHex(string? input, out string hex)
        {
            hex = "";
            if (string.IsNullOrWhiteSpace(input)) return false;
            string s = input.Trim().TrimStart('#');
            if (s.Length is not (6 or 8)) return false;
            foreach (char c in s)
                if (!Uri.IsHexDigit(c)) return false;
            hex = "#" + s.ToUpperInvariant();
            return true;
        }

        private OverlayConfig BuildOverlay() => new()
        {
            Enabled = _settings.OverlayTimestamp,
            Text = _settings.OverlayText,
            Position = _settings.OverlayPosition,
            FontSize = _settings.OverlayFontSize,
            FontFamily = _settings.OverlayFontFamily,
            CustomX = _settings.OverlayCustomX,
            CustomY = _settings.OverlayCustomY,
            TextColor = _settings.OverlayTextColor,
            TextOpacity = _settings.OverlayTextOpacity,
            BackColor = _settings.OverlayBackColor,
            BackOpacity = _settings.OverlayBackOpacity,
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

        // Runs the offered pre-destructive-op backup behind the caller's busy flag. False = backup
        // failed and the destructive operation must NOT proceed (nothing has been changed yet).
        private async Task<bool> BackupSessionForSafety(string folder)
        {
            EncodeStatus = "Backing up session…";
            try
            {
                string dest = await Task.Run(() => SessionManager.BackupSession(folder,
                    (i, total) =>
                    {
                        if (i % 50 == 0 || i == total)
                            Application.Current?.Dispatcher.BeginInvoke(
                                () => EncodeStatus = $"Backing up… {i}/{total}");
                    }));
                Logger.Log("Wpf", $"Session backed up to {dest} before a destructive operation.");
                return true;
            }
            catch (Exception ex)
            {
                EncodeStatus = "Backup failed — nothing was changed";
                MessageDialog.Show($"Couldn't back up the session, so nothing was changed:\n{ex.Message}",
                    "Backup", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private async Task OpenOverlay()
        {
            var dlg = new OverlayDialog { Owner = Application.Current?.MainWindow, DataContext = this };
            dlg.ShowDialog();
            if (!dlg.BakeRequested || _sessionFolder == null || IsCapturing || IsEncoding) return;

            // Retroactive bake — re-writes every frame on disk (consent already given in the dialog).
            // Same busy pattern as the destructive crop: IsEncoding gates start/encode/trim/cull/switch.
            IsEncoding = true;
            EncodeStatus = "Baking overlay…";
            try
            {
                var folder = _sessionFolder;
                if (dlg.BackupFirstRequested && !await BackupSessionForSafety(folder)) return;
                EncodeStatus = "Baking overlay…";
                var overlay = BuildOverlay();
                int done = await Task.Run(() => SessionManager.BakeOverlay(
                    folder, overlay, _settings.JpegQuality,
                    (i, total) =>
                    {
                        if (i % 25 == 0 || i == total)
                            Application.Current?.Dispatcher.BeginInvoke(
                                () => EncodeStatus = $"Baking overlay… {i}/{total}");
                    }));
                EncodeStatus = $"Overlay baked into {done} frame(s) ✓";
                Logger.Log("Wpf", $"Retroactive overlay bake: {done} frame(s) in {folder}.");
            }
            catch (Exception ex)
            {
                EncodeStatus = "Overlay bake failed";
                MessageDialog.Show($"Couldn't bake the overlay into the frames:\n{ex.Message}",
                    "Bake overlay", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { IsEncoding = false; }
            UpdatePreview();   // the frames' pixels changed — show it
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
                MessageDialog.Show($"Couldn't open the log: {ex.Message}", "Open log", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExportSettings()
        {
            var dlg = new SaveFileDialog { Title = "Export settings", Filter = "Settings (*.json)|*.json", FileName = "timelapse-settings.json" };
            if (dlg.ShowDialog() != true) return;
            try { SettingsManager.ExportTo(_settings, dlg.FileName); }
            catch (Exception ex) { MessageDialog.Show($"Couldn't export settings: {ex.Message}", "Export settings", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        // ---- Presets (named capture/encode/look setups; identity + safety fields never travel) ----
        public ICommand ApplyPresetCommand { get; }
        public ICommand SavePresetCommand { get; }
        public ICommand RenamePresetCommand { get; }
        public ICommand DeletePresetCommand { get; }
        public ICommand RestoreDefaultsCommand { get; }

        public System.Collections.ObjectModel.ObservableCollection<string> Presets { get; } = new();

        private string? _selectedPreset;
        public string? SelectedPreset
        {
            get => _selectedPreset;
            set { if (_selectedPreset != value) { _selectedPreset = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); } }
        }

        private void RefreshPresets()
        {
            string? keep = SelectedPreset;
            Presets.Clear();
            foreach (var n in PresetManager.List()) Presets.Add(n);
            SelectedPreset = keep != null && Presets.Contains(keep) ? keep : null;
        }

        private void ApplyPreset()
        {
            if (SelectedPreset == null || IsCapturing) return;
            var preset = PresetManager.Load(SelectedPreset);
            if (preset == null) { MessageDialog.Show("That preset couldn't be loaded.", "Presets", MessageBoxButton.OK, MessageBoxImage.Warning); RefreshPresets(); return; }

            var merged = PresetManager.ApplyOnto(preset, _settings);
            NormalizeSettings(merged);   // re-clamp untrusted file values, same as Import

            // Guard the image2 uniformity invariant: switching format on a session that already has frames
            // would mix file types and block encoding. Warn before applying (mirrors the UsePng warning).
            if (_frameCount > 0 && !string.Equals(merged.Format, _settings.Format, StringComparison.OrdinalIgnoreCase))
            {
                var r = MessageDialog.Show(
                    $"“{SelectedPreset}” captures as {merged.Format}, but this session already has {_frameCount} {_settings.Format} frame(s). Applying it would mix formats and block encoding until you cull/convert.\n\nApply anyway?",
                    "Presets", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
            }

            _settings = merged;
            SettingsManager.Save(_settings);
            ThemeManager.Apply(_settings.Theme);   // theme is carried → apply live
            OnPropertyChanged(string.Empty);       // rebind every setting-backed property
            WindowAffinityChanged?.Invoke();        // HideFromCapture may have changed
            NotifyOverlayDerived();
            RefreshStats(); BumpRecalc();           // recompute storage/length/progress from the new fps/skip/format
        }

        private void SavePreset()
        {
            var dlg = new TextPromptDialog("Save preset", "Preset name", SelectedPreset ?? "My setup")
            { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true) return;
            string name = string.IsNullOrWhiteSpace(dlg.Value) ? "My setup" : dlg.Value!.Trim();

            bool overwrite = false;
            if (PresetManager.Exists(name))
            {
                var r = MessageDialog.Show($"A preset named “{name}” already exists. Overwrite it with the current settings?",
                    "Save preset", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;   // decline → cancel (Save-as-new would need a different name)
                overwrite = true;
            }
            string saved = PresetManager.Save(name, _settings, overwrite);
            RefreshPresets();
            SelectedPreset = saved;
        }

        private void RenamePreset()
        {
            if (SelectedPreset == null) return;
            var dlg = new TextPromptDialog("Rename preset", "New name", SelectedPreset)
            { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;
            string final = PresetManager.Rename(SelectedPreset, dlg.Value!.Trim());
            RefreshPresets();
            SelectedPreset = final;
        }

        private void DeletePreset()
        {
            if (SelectedPreset == null) return;
            var r = MessageDialog.Show($"Delete the preset “{SelectedPreset}”? This can't be undone.",
                "Delete preset", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            PresetManager.Delete(SelectedPreset);
            SelectedPreset = null;
            RefreshPresets();
        }

        // A safe way back if the user messes something up (a broken output-name template, odd encode
        // values, a theme they can't undo). Resets everything to defaults EXCEPT the machine paths
        // (output folder + ffmpeg) and the first-run flag — so it doesn't strand them or re-nag the wizard.
        private void RestoreDefaults()
        {
            if (IsCapturing) return;
            var r = MessageDialog.Show(
                "Reset all settings to their defaults?\n\nYour output folder and FFmpeg location are kept. Everything else — interval, format, encoding, overlay, theme, safety limits, hotkey, output-name template — returns to a safe default. Saved presets are not affected.",
                "Restore defaults", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            _settings = new CaptureSettings
            {
                SaveFolder = _settings.SaveFolder,
                FfmpegPath = _settings.FfmpegPath,
                FirstRunCompleted = _settings.FirstRunCompleted,   // don't re-trigger onboarding
            };
            SettingsManager.Save(_settings);
            ThemeManager.Apply(_settings.Theme);
            OnPropertyChanged(string.Empty);   // rebind everything
            WindowAffinityChanged?.Invoke();
            HotkeysChanged?.Invoke();           // hotkey returns to its default (disabled)
            NotifyOverlayDerived();
            RefreshStats(); BumpRecalc();
        }

        private void ImportSettings()
        {
            var dlg = new OpenFileDialog { Title = "Import settings", Filter = "Settings (*.json)|*.json" };
            if (dlg.ShowDialog() != true) return;
            var imported = SettingsManager.LoadFrom(dlg.FileName);
            if (imported == null)
            {
                MessageDialog.Show("That file isn't a valid settings file.", "Import settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            NormalizeSettings(imported);   // an imported file bypasses the property-setter clamps — re-bound here
            _settings = imported;
            SettingsManager.Save(_settings);
            // Resync cached display state the blanket notify can't recompute (they have backing fields).
            OutputFolder = string.IsNullOrWhiteSpace(_settings.SaveFolder) ? "(not set)" : _settings.SaveFolder!;
            RefreshOutputFolderMissing();
            OnPropertyChanged(string.Empty); // refresh every binding against the new settings
            RefreshStats(); BumpRecalc();    // recompute storage/length/progress from the imported fps/skip/format
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
            s.MaxDurationMinutes = Math.Max(1, s.MaxDurationMinutes);
            s.StopAtStorageMB = Math.Max(10, s.StopAtStorageMB);
            s.EncodeEveryNth = Math.Clamp(s.EncodeEveryNth, 1, 1000);
            s.EncodeFps = Math.Clamp(s.EncodeFps, 1, 240);
            s.EncodeCrf = Math.Clamp(s.EncodeCrf, 0, 51);
            s.EncodeHoldLastSeconds = Math.Clamp(s.EncodeHoldLastSeconds, 0, 60);
            s.OverlayCustomX = s.OverlayCustomX < 0 ? -1 : Math.Min(1, s.OverlayCustomX);
            s.OverlayCustomY = s.OverlayCustomY < 0 ? -1 : Math.Min(1, s.OverlayCustomY);
            if (s.IntervalSecondsExact > 0)
                s.IntervalSecondsExact = Math.Clamp(s.IntervalSecondsExact, 0.1m, 3600m);
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

                var r = MessageDialog.Show(
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
                string safe = SessionManager.SanitizeFolderName(newName);
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
                MessageDialog.Show($"Couldn't rename the session: {ex.Message}", "Rename",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private bool ConfirmRegionChange()
        {
            if (_session != null && _frameCount > 0)
            {
                var canonical = _sessionFolder != null ? SessionManager.GetFrameSize(_sessionFolder) : System.Drawing.Size.Empty;
                // Only promise scaling when we could actually read the canonical size — if the first
                // frame is unreadable, StartEngine can't arm scaling and sizes could genuinely mix.
                if (canonical.Width >= 2)
                {
                    // Benign case (new source auto-scales to match) — the repeat-prone prompt gets a
                    // "don't ask again" way out. The unreadable-canonical case below stays a hard ask.
                    return Prompts.Confirm(_settings, "region-change-scaled",
                        $"This session already has {_frameCount} frame(s).\n\n" +
                        $"This session's frames are {canonical.Width}×{canonical.Height}. A different-sized selection or tracked window " +
                        "will be SCALED to match (letterboxed if the shape differs) so the video stays consistent — scaling costs a " +
                        "little sharpness.\n\nChange the region?",
                        "Change region?");
                }
                var r = MessageDialog.Show(
                    $"This session already has {_frameCount} frame(s).\n\n" +
                    "The existing frames' size couldn't be read, so a different-sized selection may MIX frame sizes and break the " +
                    "final encode.\n\nChange the region?",
                    "Change region?", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
            bool ok = WindowEnumerator.TryGetLiveBounds(_trackedWindow, out var b, out bool minimized, out bool alive);
            if (!alive) { _trackOverlayTimer.Stop(); return; }   // window gone → don't poll a dead HWND forever
            if (!ok || minimized) return;                        // transient / hidden → skip this tick, keep polling

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
            // The ratio lock doesn't drive a tracked window (its size comes from the window) — reset the
            // segs to Free so the UI doesn't imply a constraint that isn't being applied.
            AspectRatioIndex = 0;
            RatioFlipped = false;
        }

        private void SelectRegion()
        {
            if (!ConfirmRegionChange()) return;
            var (rw, rh) = EffectiveRatio();
            var overlay = new RegionSelectOverlay(rw, rh);
            if (ShowRegionOverlay(overlay) == true && overlay.SelectedRegion.HasValue)
                ApplyRegion(overlay.SelectedRegion.Value);
        }

        private void EditRegion()
        {
            if (!_region.HasValue) return;
            if (!ConfirmRegionChange()) return;
            var (rw, rh) = EffectiveRatio();
            var dlg = new RegionEditOverlay(_region.Value, rw, rh);
            if (ShowRegionOverlay(dlg) == true && dlg.SelectedRegion.HasValue)
                ApplyRegion(dlg.SelectedRegion.Value);
        }

        // Show a full-screen region overlay, optionally hiding the main window first so it doesn't
        // block the very thing the user is trying to select (default on). Keeps the wizard as owner
        // when a pick is launched from it; never owns the overlay by a window we've hidden.
        private bool? ShowRegionOverlay(Window overlay)
        {
            var main = Application.Current?.MainWindow;
            var active = ActiveWindow();
            if (active != null && active != main && active.IsVisible) overlay.Owner = active;

            bool hide = _settings.HideWindowDuringRegionSelect && main != null && main.IsVisible;
            if (hide) main!.Hide();
            try { return overlay.ShowDialog(); }
            finally { if (hide) { main!.Show(); main.Activate(); } }
        }

        // The window that should own a transient dialog: the active one (e.g. the modal setup wizard
        // when a region pick is launched from it), falling back to the main window. Correct ownership
        // fixes z-order and foreground activation for nested modals.
        private static Window? ActiveWindow()
        {
            var app = Application.Current;
            if (app == null) return null;
            return app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? app.MainWindow;
        }

        private void ApplyRegion(System.Drawing.Rectangle r, string? label = null, IntPtr trackedWindow = default)
        {
            _trackedWindow = trackedWindow;   // every region source funnels here, so static sources reset to 0
            _region = r;
            RegionText = label ?? $"{r.Width}×{r.Height} at ({r.X},{r.Y})";
            RefreshRegionScaleSuffix();   // every source (incl. full screen / tracking) shows the scale note
            PersistRegion(r);
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(RegionNeeded));
            CommandManager.InvalidateRequerySuggested();
            UpdateOverlay();
        }

        // Re-evaluate the "→ scaled to W×H" tail of RegionText against the CURRENT canonical frame
        // size (it changes after e.g. a destructive crop): strip any prior suffix, re-append on mismatch.
        private void RefreshRegionScaleSuffix()
        {
            if (!_region.HasValue) return;
            int i = RegionText.IndexOf(" → scaled to", StringComparison.Ordinal);
            string baseText = i >= 0 ? RegionText[..i] : RegionText;
            string suffix = "";
            if (_frameCount > 0 && _sessionFolder != null)
            {
                var canonical = SessionManager.GetFrameSize(_sessionFolder);
                if (canonical.Width >= 2 && canonical != _region.Value.Size)
                    suffix = $" → scaled to {canonical.Width}×{canonical.Height}";
            }
            RegionText = baseText + suffix;
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
            // Also reachable via hotkey/tray, which skip the command's CanExecute: never start while an
            // encode OR an on-disk rewrite (destructive crop, overlay bake) is running — capturing into a
            // session whose frames are being rewritten would mix sizes / skip frames mid-operation.
            if (IsEncoding) return;

            // Pre-flight: don't begin a run onto a disk that's already below the low-disk safety limit
            // (it would auto-stop almost immediately) — let the user decide, but default to not starting.
            if (_settings.AutoStopOnLowDisk)
            {
                long freeMb = SystemMonitor.GetAvailableDiskSpaceMB(_sessionFolder);
                if (freeMb > 0 && freeMb < _settings.LowDiskStopMB)
                {
                    var r = MessageDialog.Show(
                        $"Only {freeMb} MB free on the capture drive — below your {_settings.LowDiskStopMB} MB low-disk limit, so the run would stop almost immediately.\n\nStart anyway?",
                        "Low disk space", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (r != MessageBoxResult.Yes) return;
                }
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
            // An armed recording timer is a commitment — stopping early deserves a check (with a
            // "don't ask again" way out, since the timer's owner may still prefer manual stops).
            if (TargetKind == 1)
            {
                double remaining = _targetSeconds - RunActiveSeconds();
                if (remaining > 0.5 && !Prompts.Confirm(_settings, "stop-active-timer",
                        $"The recording timer still has {HumanDuration(remaining)} left — stop anyway?",
                        "Stop recording?"))
                    return;
            }
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
            if (TargetKind == 0 && _settings.StopAtTarget && targetFrames > 0 && _frameCount >= targetFrames)
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
                if (TargetKind == 0 && _settings.StopAtTarget && IsCapturing && targetFrames > 0 && count >= targetFrames)
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
            long targetFrames = TargetKind == 0 ? (long)_targetSeconds * Math.Max(1, EncodeFps) * Math.Max(1, EncodeEveryNth) : 0;
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
        private void Cull()
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

            int newCount = SessionManager.CullAndRenumber(_sessionFolder, new HashSet<int>(dlg.MarkedForDeletion));
            FrameCount = newCount;
            UpdatePreview();   // the "latest" frame likely changed

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
                result = await VideoEncoder.EncodeAsync(ffmpeg, _sessionFolder, EncodeFps, EncodePreset, EncodeCrf,
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
                UpdateCaptureToTarget();   // live time-to-target readout

                // Opt-in max-duration cap: stop once accumulated capture time reaches the limit (a normal
                // completion — notify, but no red error banner).
                if (IsCapturing && !_isPaused && _settings.MaxDurationEnabled && totalCaptureSeconds >= _settings.MaxDurationMinutes * 60.0)
                {
                    Logger.Log("Wpf", $"Auto-stop: reached max duration ({totalCaptureSeconds:F0}s >= {_settings.MaxDurationMinutes * 60}s).");
                    StopCapture();
                    NotifyFinished();
                    return;   // nothing else to refresh this tick
                }

                // Recording timer: stop once this run's ACTIVE capture time reaches the target (a normal
                // completion — notify, no error banner). Paused time never advances the clock.
                if (IsCapturing && TargetKind == 1 && RunActiveSeconds() >= _targetSeconds)
                {
                    Logger.Log("Wpf", $"Auto-stop: recording timer reached ({RunActiveSeconds():F0}s >= {_targetSeconds}s of active capture).");
                    StopCapture();
                    NotifyFinished();
                    return;
                }

                int projectedFrames;   // also feeds the storage projection below, for either kind
                if (TargetKind == 1)
                {
                    // Timer kind: frames expected from running the timer out at the current interval.
                    projectedFrames = Math.Max(_frameCount, (int)(_targetSeconds / Math.Max(0.1, (double)IntervalSeconds)));
                    double active = RunActiveSeconds();
                    double pct = Math.Min(100.0, active * 100.0 / Math.Max(1, _targetSeconds));
                    CaptureProgress = pct;
                    ProgressText = IsCapturing
                        ? $"{FormatTime(active)} / {FormatTime(_targetSeconds)} recorded · {pct:F0}% of the timer"
                        : $"{_frameCount} frames · timer set for {HumanDuration(_targetSeconds)}";
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
                double vidLen = EncodeFps > 0 ? encodedFrames / (double)EncodeFps : 0;
                VideoLengthText = $"≈ {vidLen:F1}s @ {EncodeFps}fps" + (everyNth > 1 ? $" · 1 in {everyNth}" : "");

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
                    if (IsCapturing && _settings.AutoStopOnLowDisk && _sessionFolder != null)
                    {
                        long freeMb = SystemMonitor.GetAvailableDiskSpaceMB(_sessionFolder);
                        if (freeMb > 0 && freeMb < _settings.LowDiskStopMB)
                        {
                            Logger.Log("Wpf", $"Auto-stop: low disk ({freeMb} MB free < {_settings.LowDiskStopMB} MB limit).");
                            StopCapture();
                            CaptureError = $"Capture stopped — low disk space ({freeMb} MB free, limit {_settings.LowDiskStopMB} MB). Free up space, then start again.";
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

        private void RefreshFfmpegStatus()
        {
            var path = FfmpegRunner.FindFfmpeg(_settings.FfmpegPath);
            _ffmpegReady = !string.IsNullOrEmpty(path);
            FfmpegStatus = _ffmpegReady ? "Ready ✓" : "Not found";
            _ffmpegSetupExpanded = false;   // a fresh resolve folds the setup row back up
            OnPropertyChanged(nameof(FfmpegControlsVisible));
            OnPropertyChanged(nameof(FfmpegChangeVisible));
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
