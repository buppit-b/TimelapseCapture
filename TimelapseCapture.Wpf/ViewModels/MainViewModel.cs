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
        private DateTime? _captureStart;
        private double _accumulatedSeconds; // total capture time across start/stop within this app run
        private int _statsTick;
        private readonly DispatcherTimer _statsTimer;

        public MainViewModel()
        {
            _settings = SettingsManager.Load();
            _outputFolder = string.IsNullOrWhiteSpace(_settings.SaveFolder) ? "(not set)" : _settings.SaveFolder!;
            RefreshFfmpegStatus();

            _engine.FrameCaptured += OnFrameCaptured;
            _engine.CaptureFailed += OnCaptureFailed;
            _engine.SmartStatusChanged += OnSmartStatus;

            ChooseFolderCommand = new RelayCommand(_ => ChooseFolder(), _ => !IsCapturing);
            NewSessionCommand = new RelayCommand(_ => NewSession(), _ => HasOutputFolder && !IsCapturing);
            LoadSessionCommand = new RelayCommand(_ => LoadSession(), _ => HasOutputFolder && !IsCapturing);
            RenameSessionCommand = new RelayCommand(_ => RenameSession(), _ => _session != null && !IsCapturing);
            OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
            OpenOverlayCommand = new RelayCommand(_ => OpenOverlay());
            ExportSettingsCommand = new RelayCommand(_ => ExportSettings());
            ImportSettingsCommand = new RelayCommand(_ => ImportSettings());
            SetTargetCommand = new RelayCommand(_ => SetTarget());
            ValidateTarget();

            // After the window is up, check whether a previous run was interrupted mid-capture.
            Application.Current?.Dispatcher.BeginInvoke(new Action(CheckForInterruptedSession), DispatcherPriority.Background);
            FullScreenCommand = new RelayCommand(_ => SelectFullScreen(), _ => _session != null && !IsCapturing);
            SelectRegionCommand = new RelayCommand(_ => SelectRegion(), _ => _session != null && !IsCapturing);
            EditRegionCommand = new RelayCommand(_ => EditRegion(), _ => _session != null && _region.HasValue && !IsCapturing);
            StartCommand = new RelayCommand(_ => StartCapture(), _ => _session != null && _region.HasValue && !IsCapturing);
            StopCommand = new RelayCommand(_ => StopCapture(), _ => IsCapturing);
            PauseResumeCommand = new RelayCommand(_ => PauseResume(), _ => IsCapturing);
            OpenFolderCommand = new RelayCommand(_ => OpenSessionFolder(), _ => CanOpenFolder);
            EncodeCommand = new RelayCommand(async _ => await EncodeOrCancel(), _ => CanEncode || IsEncoding);
            TrimCommand = new RelayCommand(async _ => await Trim(), _ => CanEncode);
            DownloadFfmpegCommand = new RelayCommand(async _ => await DownloadFfmpeg(), _ => !IsFfmpegBusy);
            BrowseFfmpegCommand = new RelayCommand(_ => BrowseFfmpeg(), _ => !IsFfmpegBusy);
            CancelDownloadCommand = new RelayCommand(_ => _ffmpegCts?.Cancel(), _ => IsFfmpegBusy);
            ShowOverlayCommand = new RelayCommand(_ => ToggleOverlay(), _ => _region.HasValue || _isOverlayShown);

            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statsTimer.Tick += (s, e) => RefreshStats();
            _statsTimer.Start();
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
                decimal v = value < 0.1m ? 0.1m : value;
                if (_settings.IntervalSecondsExact != v)
                {
                    _settings.IntervalSecondsExact = v;
                    _settings.IntervalSeconds = (int)Math.Max(1, Math.Round(v)); // rounded metadata for int consumers
                    SettingsManager.Save(_settings);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusText));
                }
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
        public int EncodeFps { get => _encodeFps; set { if (SetProperty(ref _encodeFps, value < 1 ? 1 : value)) { RefreshStats(); BumpRecalc(); } } }

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
                seconds = (int)(v * mult);
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

        private void UpdatePreview() => PreviewImage = FramePreview.LoadLatest(_sessionFolder, 260);

        // A higher-res copy of the latest frame, loaded on demand for the preview loupe.
        public ImageSource? LoadLoupeFrame() => FramePreview.LoadLatest(_sessionFolder, 1400);

        private bool HasOutputFolder =>
            !string.IsNullOrWhiteSpace(_settings.SaveFolder) && Directory.Exists(_settings.SaveFolder);

        // ---- commands ----
        public ICommand ChooseFolderCommand { get; }
        public ICommand NewSessionCommand { get; }
        public ICommand LoadSessionCommand { get; }
        public ICommand RenameSessionCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenOverlayCommand { get; }
        public ICommand ExportSettingsCommand { get; }
        public ICommand ImportSettingsCommand { get; }
        public ICommand SetTargetCommand { get; }
        public ICommand FullScreenCommand { get; }
        public ICommand SelectRegionCommand { get; }
        public ICommand EditRegionCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand PauseResumeCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand EncodeCommand { get; }
        public ICommand TrimCommand { get; }
        public ICommand DownloadFfmpegCommand { get; }
        public ICommand BrowseFfmpegCommand { get; }
        public ICommand CancelDownloadCommand { get; }
        public ICommand ShowOverlayCommand { get; }

        private void ChooseFolder()
        {
            var dlg = new OpenFolderDialog { Title = "Select output folder for captures" };
            if (dlg.ShowDialog() == true)
            {
                _settings.SaveFolder = dlg.FolderName;
                SettingsManager.Save(_settings);
                OutputFolder = dlg.FolderName;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void NewSession()
        {
            try
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

                // Don't spawn another empty folder on repeated clicks — if the current session has no
                // frames yet, it already IS a fresh session; keep it.
                if (_session != null && _sessionFolder != null && _frameCount == 0 && !IsCapturing)
                    return;

                string capturesRoot = Path.Combine(_settings.SaveFolder!, "captures");
                string name = $"Session_{DateTime.Now:yyyyMMdd_HHmmss}";
                _sessionFolder = SessionManager.CreateNamedSession(
                    capturesRoot, name, _settings.IntervalSeconds, null, _settings.Format ?? "JPEG", _settings.JpegQuality);
                _session = SessionManager.LoadSession(_sessionFolder);
                _region = null;
                _accumulatedSeconds = 0;
                PreviewImage = null;

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

            // Restore the saved region. Keep its exact size; if its saved spot is no longer on any
            // monitor (display unplugged / resolution changed), relocate it onto the current desktop
            // rather than lose it — the size must stay constant to keep this session's frames uniform.
            _region = session.CaptureRegion;
            bool regionMoved = false, regionCantFit = false;
            if (_region.HasValue)
            {
                _region = ScreenHelper.FitRegionOnScreen(_region.Value, out regionMoved);
                regionCantFit = _region == null;
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
            _settings = imported;
            SettingsManager.Save(_settings);
            OnPropertyChanged(string.Empty); // refresh every binding against the new settings
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
            var newName = dlg.Value.Trim();
            try
            {
                // Rename the folder to match (sanitised + de-duplicated), then update the display name.
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
        }

        private void SelectFullScreen()
        {
            if (!ConfirmRegionChange()) return;

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
            ApplyRegion(r, $"{r.Width}×{r.Height} (full screen)");
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

        private void ApplyRegion(System.Drawing.Rectangle r, string? label = null)
        {
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
            PersistRegion(_region.Value); // ensure the active region (incl. a relocated one) is saved
            SetSessionActive(true);       // a session left Active at launch = the app died mid-capture
            StartEngine();
            IsCapturing = true;
            IsPaused = false;
        }

        // Start (or resume) the capture engine with current settings and begin a new timing segment.
        private void StartEngine()
        {
            _engine.Start(_sessionFolder!, _session!, _region!.Value, (double)IntervalSeconds, _settings.Format ?? "JPEG",
                _settings.SmartIntervalEnabled, (double)_settings.IdleIntervalSeconds,
                _settings.IdleThresholdSeconds, _settings.SkipIdleFrames, _settings.JpegQuality,
                _settings.CaptureCursor, BuildOverlay());
            _captureStart = DateTime.Now;
            SmartStatus = _settings.SmartIntervalEnabled ? "Active" : "";
        }

        private void PauseResume()
        {
            if (!IsCapturing) return;
            if (_isPaused)
            {
                StartEngine();           // resume → new capture segment
                IsPaused = false;
            }
            else
            {
                _engine.Stop();          // pause → stop capturing but keep the run armed
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

        private void OnFrameCaptured(int count)
            => Application.Current?.Dispatcher.BeginInvoke(new Action(() => FrameCount = count));

        private void OnCaptureFailed(string message)
            => Logger.Log("Wpf", $"Capture error: {message}");

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
            var dlg = new TrimDialog(_sessionFolder, _frameCount) { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() == true)
                await Encode(dlg.StartFrame, dlg.EndFrame);
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
                TotalElapsedText = FormatTime(_accumulatedSeconds + current);

                int projectedFrames = Math.Max(_frameCount, _desiredVideoSeconds * Math.Max(1, EncodeFps));
                double pct = projectedFrames > 0 ? Math.Min(100.0, _frameCount * 100.0 / projectedFrames) : 0;
                CaptureProgress = pct;
                ProgressText = $"{_frameCount} / {projectedFrames} frames · {pct:F0}% of a {_desiredVideoSeconds}s video @ {Math.Max(1, EncodeFps)}fps";

                double vidLen = EncodeFps > 0 ? _frameCount / (double)EncodeFps : 0;
                VideoLengthText = $"Video @ {EncodeFps}fps ≈ {vidLen:F1}s";

                // The storage/disk/memory probe reads frame files — throttle it to ~every 2s.
                if (_statsTick % 2 == 0 || string.IsNullOrEmpty(StorageInfo))
                {
                    int w = _region?.Width ?? 0;
                    int h = _region?.Height ?? 0;
                    StorageInfo = SystemMonitor.GetStorageInfoString(_sessionFolder, w, h,
                        _settings.Format ?? "JPEG", _settings.JpegQuality, _frameCount, projectedFrames);
                    ResourcesInfo = SystemMonitor.GetResourcesInfoString();
                }

                if (IsCapturing) UpdatePreview();
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
            _engine.Dispose();
            _overlay?.Close();
        }
    }
}
