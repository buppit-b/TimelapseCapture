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
    /// MainViewModel — bound state: settings-backed capture/encode tuning properties, Simple mode,
    /// capture/encode/ffmpeg state flags, and the derived status texts the window binds.
    /// </summary>
    public partial class MainViewModel
    {
        // ---- bound state ----
        private string _outputFolder;
        public string OutputFolder { get => _outputFolder; set => SetProperty(ref _outputFolder, value); }

        public decimal IntervalSeconds
        {
            get => _settings.IntervalSecondsExact > 0 ? _settings.IntervalSecondsExact : _settings.IntervalSeconds;
            set
            {
                // 0.01s (100 fps) is the floor — and the engine's real ceiling (10 ms timer). Below 0.1s
                // is video-recording territory: allowed, but resource-intensive and flagged (IsVideoRate).
                // Ceiling 3600s (one frame an hour) — beyond that is almost certainly a typo, not a plan.
                decimal floor = MinIntervalSeconds;
                decimal v = value < floor ? floor : value > 3600m ? 3600m : value;
                // Normalize: 4 dp is plenty, and strip decimal's trailing-zero scale so a pasted
                // "0.1000000000000000000000000000" (or an fps round-trip artifact) displays as "0.1".
                // 6 dp, not 4: an fps like 60 → interval 1/60 = 0.016667, and 4 dp (0.0167) round-trips
                // back to 59.88. 6 dp keeps round-number fps (60/30/24…) landing exactly on themselves.
                v = decimal.Parse(Math.Round(v, 6).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.CultureInfo.InvariantCulture);
                if (_settings.IntervalSecondsExact != v)
                {
                    _settings.IntervalSecondsExact = v;
                    _settings.IntervalSeconds = (int)Math.Max(1, Math.Round(v)); // rounded metadata for int consumers
                    SettingsManager.Save(_settings);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(SpeedNotch));
                    OnPropertyChanged(nameof(IsVideoRate));
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
            set { if (value > 0) IntervalSeconds = Math.Round(1m / value, 6); }
        }

        // Interval-field tooltips — dry, and note the video-rate threshold rather than pretending 0.1s is a wall.
        public string IntervalTooltip =>
            "Seconds between frames (decimals OK). 0.01s to 3600s; out-of-range entries snap back. Below 0.1s is video-rate — resource-intensive.";
        public string FpsTooltip =>
            "Capture rate in frames per second. Up to 100 fps (the 0.01s floor); out-of-range entries snap back. Above 10 fps is video-rate — resource-intensive.";

        /// <summary>True when the interval is into video-recording territory (&lt; 0.1s / &gt; 10 fps) — flagged, not blocked.</summary>
        public bool IsVideoRate => IntervalSeconds > 0 && IntervalSeconds < 0.1m;

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
            (EncodeDurationMode ? $"exactly {HumanDuration(EncodeDurationSeconds)}" : $"{Math.Max(1, EncodeFps)} fps") +
            $" · CRF {EncodeCrf} · " +
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

        // ---- Encode-to-duration: fixed fps vs "make the video exactly N seconds" (fps computed) ----
        public bool EncodeDurationMode
        {
            get => _settings.EncodeDurationMode;
            set
            {
                if (_settings.EncodeDurationMode == value) return;
                _settings.EncodeDurationMode = value;
                SettingsManager.Save(_settings);
                OnPropertyChanged();
                OnPropertyChanged(nameof(EncodeUnitIndex));
                OnPropertyChanged(nameof(ShowEncodeFps));
                OnPropertyChanged(nameof(ShowEncodeLength));
                OnPropertyChanged(nameof(EncodeSummaryText));
                RefreshStats();   // VideoLengthText recomputes for the new mode (shows the implied fps)
                BumpRecalc();
            }
        }

        public double EncodeDurationSeconds
        {
            get => _settings.EncodeDurationSeconds;
            set
            {
                var v = Math.Clamp(value, 0.5, 36000);   // half a second to 10 hours
                if (Math.Abs(_settings.EncodeDurationSeconds - v) < 0.0001) return;
                _settings.EncodeDurationSeconds = v;
                SettingsManager.Save(_settings);
                OnPropertyChanged();
                OnPropertyChanged(nameof(EncodeSummaryText));
                RefreshStats();   // VideoLengthText recomputes the implied fps for the new length
                BumpRecalc();
            }
        }

        // Segmented-control binding (StrEq converter): "0" = fps mode, "1" = exact-length mode.
        public string EncodeUnitIndex
        {
            get => EncodeDurationMode ? "1" : "0";
            set => EncodeDurationMode = value == "1";
        }

        // Which of the two mutually-exclusive inputs shows (bound via BoolToVis — the StrEq converter
        // returns a bool, which can't drive Visibility directly, so use these instead).
        public bool ShowEncodeFps => !EncodeDurationMode;
        public bool ShowEncodeLength => EncodeDurationMode;

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
                if (_settings.AspectRatioIndex != v) { _settings.AspectRatioIndex = v; SettingsManager.Save(_settings); OnPropertyChanged(); OnPropertyChanged(nameof(CanFlipRatio)); }
            }
        }

        /// <summary>Flip only means something with a locked ratio — on Free there's no orientation to swap,
        /// so the control is disabled (clicking it otherwise just rotates the region and warns, which reads
        /// as "no-op that nags"). Index 0 = Free.</summary>
        public bool CanFlipRatio => AspectRatioIndex != 0;

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
            set
            {
                if (!SetProperty(ref _frameCount, value)) return;
                OnPropertyChanged(nameof(FrameCountText));
            }
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

    }
}
