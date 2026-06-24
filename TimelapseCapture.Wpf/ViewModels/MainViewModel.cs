using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
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
        private readonly CaptureSettings _settings;
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

            ChooseFolderCommand = new RelayCommand(_ => ChooseFolder());
            NewSessionCommand = new RelayCommand(_ => NewSession(), _ => HasOutputFolder && !IsCapturing);
            LoadSessionCommand = new RelayCommand(_ => LoadSession(), _ => HasOutputFolder && !IsCapturing);
            FullScreenCommand = new RelayCommand(_ => SelectFullScreen(), _ => _session != null && !IsCapturing);
            SelectRegionCommand = new RelayCommand(_ => SelectRegion(), _ => _session != null && !IsCapturing);
            StartCommand = new RelayCommand(_ => StartCapture(), _ => _session != null && _region.HasValue && !IsCapturing);
            StopCommand = new RelayCommand(_ => StopCapture(), _ => IsCapturing);
            OpenFolderCommand = new RelayCommand(_ => OpenSessionFolder(), _ => CanOpenFolder);
            EncodeCommand = new RelayCommand(async _ => await EncodeOrCancel(), _ => CanEncode || IsEncoding);
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

        public decimal ActiveIntervalSeconds
        {
            get => _settings.ActiveIntervalSeconds;
            set { var v = value < 0.1m ? 0.1m : value; if (_settings.ActiveIntervalSeconds != v) { _settings.ActiveIntervalSeconds = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
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
        public int EncodeFps { get => _encodeFps; set => SetProperty(ref _encodeFps, value < 1 ? 1 : value); }

        private int _encodeCrf = 23;
        public int EncodeCrf { get => _encodeCrf; set => SetProperty(ref _encodeCrf, value < 0 ? 0 : (value > 51 ? 51 : value)); }

        public int JpegQuality
        {
            get => _settings.JpegQuality;
            set { var v = value < 1 ? 1 : (value > 100 ? 100 : value); if (_settings.JpegQuality != v) { _settings.JpegQuality = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
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
                    CommandManager.InvalidateRequerySuggested();
                }
            }
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
        public bool IsOverlayShown { get => _isOverlayShown; set => SetProperty(ref _isOverlayShown, value); }

        private int _desiredVideoSeconds = 30;

        // Planned capture length used for storage projection. Accepts "30s", "5m", "2h" (default seconds).
        private string _targetText = "30s";
        public string TargetText
        {
            get => _targetText;
            set { if (SetProperty(ref _targetText, value)) { ParseTarget(); RefreshStats(); } }
        }

        private void ParseTarget()
        {
            var t = (_targetText ?? "").Trim().ToLowerInvariant();
            double mult = 1;
            if (t.EndsWith("h")) { mult = 3600; t = t.Substring(0, Math.Max(0, t.Length - 1)); }
            else if (t.EndsWith("m")) { mult = 60; t = t.Substring(0, Math.Max(0, t.Length - 1)); }
            else if (t.EndsWith("s")) { mult = 1; t = t.Substring(0, Math.Max(0, t.Length - 1)); }
            if (double.TryParse(t.Trim(), out var v) && v > 0)
                _desiredVideoSeconds = (int)(v * mult);
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

        private string _smartStatus = "";
        public string SmartStatus { get => _smartStatus; set => SetProperty(ref _smartStatus, value); }

        private bool HasOutputFolder =>
            !string.IsNullOrWhiteSpace(_settings.SaveFolder) && Directory.Exists(_settings.SaveFolder);

        // ---- commands ----
        public ICommand ChooseFolderCommand { get; }
        public ICommand NewSessionCommand { get; }
        public ICommand LoadSessionCommand { get; }
        public ICommand FullScreenCommand { get; }
        public ICommand SelectRegionCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand EncodeCommand { get; }
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
                string capturesRoot = Path.Combine(_settings.SaveFolder!, "captures");
                string name = $"Session_{DateTime.Now:yyyyMMdd_HHmmss}";
                _sessionFolder = SessionManager.CreateNamedSession(
                    capturesRoot, name, _settings.IntervalSeconds, null, _settings.Format ?? "JPEG", _settings.JpegQuality);
                _session = SessionManager.LoadSession(_sessionFolder);
                _region = null;
                _accumulatedSeconds = 0;

                SessionName = _session?.Name ?? name;
                RegionText = "Not selected";
                FrameCount = (int)(_session?.FramesCaptured ?? 0);
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(RegionNeeded));
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
            var dlg = new OpenFolderDialog
            {
                Title = "Select a session folder to load",
                InitialDirectory = Directory.Exists(capturesRoot) ? capturesRoot : (_settings.SaveFolder ?? ""),
            };
            if (dlg.ShowDialog() != true) return;

            var session = SessionManager.LoadSession(dlg.FolderName);
            if (session == null)
            {
                MessageBox.Show("That folder doesn't contain a valid session (no session.json).",
                    "Load Session", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _session = session;
            _sessionFolder = dlg.FolderName;
            _region = session.CaptureRegion;
            _accumulatedSeconds = 0;
            SessionName = session.Name ?? "Session";
            RegionText = _region.HasValue
                ? $"{_region.Value.Width}×{_region.Value.Height} at ({_region.Value.X},{_region.Value.Y})"
                : "Not selected";
            FrameCount = (int)session.FramesCaptured;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(RegionNeeded));
            CommandManager.InvalidateRequerySuggested();
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
            var r = ScreenHelper.PrimaryScreenBounds();
            r.Width -= r.Width % 2;   // even dimensions required by the H.264 encoder
            r.Height -= r.Height % 2;
            _region = r;
            RegionText = $"{r.Width}×{r.Height} (full screen)";
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(RegionNeeded));
            CommandManager.InvalidateRequerySuggested();
            UpdateOverlay();
        }

        private void SelectRegion()
        {
            if (!ConfirmRegionChange()) return;
            var overlay = new RegionSelectOverlay();
            if (overlay.ShowDialog() == true && overlay.SelectedRegion.HasValue)
            {
                var r = overlay.SelectedRegion.Value;
                _region = r;
                RegionText = $"{r.Width}×{r.Height} at ({r.X},{r.Y})";
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(RegionNeeded));
                CommandManager.InvalidateRequerySuggested();
                UpdateOverlay();
            }
        }

        private void StartCapture()
        {
            if (_session == null || _sessionFolder == null || !_region.HasValue) return;
            _engine.Start(_sessionFolder, _session, _region.Value, (double)IntervalSeconds, _settings.Format ?? "JPEG",
                _settings.SmartIntervalEnabled, (double)_settings.ActiveIntervalSeconds,
                _settings.IdleThresholdSeconds, _settings.SkipIdleFrames, _settings.JpegQuality);
            _captureStart = DateTime.Now;
            SmartStatus = _settings.SmartIntervalEnabled ? "Active" : "";
            IsCapturing = true;
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
            IsCapturing = false;
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

        private async Task Encode()
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

            var result = await VideoEncoder.EncodeAsync(ffmpeg, _sessionFolder, EncodeFps, "medium", EncodeCrf, _encodeCts.Token);

            bool cancelled = _encodeCts.IsCancellationRequested;
            _encodeCts.Dispose();
            _encodeCts = null;
            IsEncoding = false;

            if (cancelled)
            {
                EncodeStatus = "Encode cancelled";
                return;
            }
            if (result.Success)
            {
                EncodeStatus = "Encoded ✓";
                var open = MessageBox.Show($"Video encoded:\n{result.OutputPath}\n\nOpen the output folder?",
                    "Encode complete", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (open == MessageBoxResult.Yes)
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
                EncodeStatus = "Encode failed";
                MessageBox.Show($"Encode failed:\n{result.Error}", "Encode", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                ProgressText = $"{_frameCount} / {projectedFrames} frames ({pct:F0}% of target)";

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
