using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Input;
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
        private SessionInfo? _session;
        private string? _sessionFolder;
        private Rectangle? _region;

        public MainViewModel()
        {
            _settings = SettingsManager.Load();
            _outputFolder = string.IsNullOrWhiteSpace(_settings.SaveFolder) ? "(not set)" : _settings.SaveFolder!;
            RefreshFfmpegStatus();

            _engine.FrameCaptured += OnFrameCaptured;
            _engine.CaptureFailed += OnCaptureFailed;

            ChooseFolderCommand = new RelayCommand(_ => ChooseFolder());
            NewSessionCommand = new RelayCommand(_ => NewSession(), _ => HasOutputFolder && !IsCapturing);
            FullScreenCommand = new RelayCommand(_ => SelectFullScreen(), _ => _session != null && !IsCapturing);
            SelectRegionCommand = new RelayCommand(_ => SelectRegion(), _ => _session != null && !IsCapturing);
            StartCommand = new RelayCommand(_ => StartCapture(), _ => _session != null && _region.HasValue && !IsCapturing);
            StopCommand = new RelayCommand(_ => StopCapture(), _ => IsCapturing);
            OpenFolderCommand = new RelayCommand(_ => OpenSessionFolder(), _ => CanOpenFolder);
        }

        // ---- bound state ----
        private string _outputFolder;
        public string OutputFolder { get => _outputFolder; set => SetProperty(ref _outputFolder, value); }

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
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string StatusText =>
            IsCapturing ? $"● Capturing every {_settings.IntervalSeconds}s…" :
            _session == null ? "Create a session to begin." :
            _region.HasValue ? "Ready to capture." :
            "Select a region (Full Screen for now).";

        private bool HasOutputFolder =>
            !string.IsNullOrWhiteSpace(_settings.SaveFolder) && Directory.Exists(_settings.SaveFolder);

        // ---- commands ----
        public ICommand ChooseFolderCommand { get; }
        public ICommand NewSessionCommand { get; }
        public ICommand FullScreenCommand { get; }
        public ICommand SelectRegionCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand OpenFolderCommand { get; }

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

                SessionName = _session?.Name ?? name;
                RegionText = "Not selected";
                FrameCount = (int)(_session?.FramesCaptured ?? 0);
                OnPropertyChanged(nameof(StatusText));
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create session:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SelectFullScreen()
        {
            var r = ScreenHelper.PrimaryScreenBounds();
            r.Width -= r.Width % 2;   // even dimensions required by the H.264 encoder
            r.Height -= r.Height % 2;
            _region = r;
            RegionText = $"{r.Width}×{r.Height} (full screen)";
            OnPropertyChanged(nameof(StatusText));
            CommandManager.InvalidateRequerySuggested();
        }

        private void SelectRegion()
        {
            var overlay = new RegionSelectOverlay();
            if (overlay.ShowDialog() == true && overlay.SelectedRegion.HasValue)
            {
                var r = overlay.SelectedRegion.Value;
                _region = r;
                RegionText = $"{r.Width}×{r.Height} at ({r.X},{r.Y})";
                OnPropertyChanged(nameof(StatusText));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void StartCapture()
        {
            if (_session == null || _sessionFolder == null || !_region.HasValue) return;
            _engine.Start(_sessionFolder, _session, _region.Value, _settings.IntervalSeconds, _settings.Format ?? "JPEG");
            IsCapturing = true;
        }

        private void StopCapture()
        {
            _engine.Stop();
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

        private void RefreshFfmpegStatus()
        {
            var path = FfmpegRunner.FindFfmpeg(_settings.FfmpegPath);
            FfmpegStatus = string.IsNullOrEmpty(path) ? "Not found — Settings ▸ Download" : "Ready";
        }

        public void Dispose() => _engine.Dispose();
    }
}
