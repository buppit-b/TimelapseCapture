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
    /// Main window view-model. Drives the first working vertical slice on top of the reused
    /// FrameWrite.Core engine: choose folder → new session → full screen → start/stop,
    /// with a live frame count. Region drag-select and encode are layered on next.
    /// </summary>
    public partial class MainViewModel : ViewModelBase, IDisposable
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
            NormalizeSettings(_settings);   // clamp a hand-edited/foreign settings.json at startup, like Import does
            _outputFolder = string.IsNullOrWhiteSpace(_settings.SaveFolder) ? "(not set)" : _settings.SaveFolder!;
            RefreshOutputFolderMissing();   // warn immediately if the saved folder was deleted since last run
            RefreshFfmpegStatus();
            RefreshTargetHint();

            _engine.FrameCaptured += OnFrameCaptured;
            _engine.CaptureFailed += OnCaptureFailed;
            _engine.SmartStatusChanged += OnSmartStatus;

            ChooseFolderCommand = new RelayCommand(_ => ChooseFolder(), _ => !IsCapturing && !IsEncoding);
            NewSessionCommand = new RelayCommand(_ => NewSession(), _ => HasOutputFolder && !IsCapturing && !IsEncoding);
            LoadSessionCommand = new RelayCommand(_ => LoadSession(), _ => HasOutputFolder && !IsCapturing && !IsEncoding);
            RenameSessionCommand = new RelayCommand(_ => RenameSession(), _ => _session != null && !IsCapturing && !IsEncoding);
            OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
            // Not while encoding/baking: the dialog's live preview opens a frame file (Image.FromFile locks
            // it), which collides with a bake rewriting that same frame → "file in use". Disable it instead.
            OpenOverlayCommand = new RelayCommand(async _ => await OpenOverlay(), _ => !IsEncoding);
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
            ResetTimerCommand = new RelayCommand(_ => ResetTimer());
            OpenFolderCommand = new RelayCommand(_ => OpenSessionFolder(), _ => CanOpenFolder);
            EncodeCommand = new RelayCommand(async _ => await EncodeOrCancel(), _ => CanEncode || IsEncoding);
            TrimCommand = new RelayCommand(async _ => await Trim(), _ => CanEncode);
            CullCommand = new RelayCommand(async _ => await Cull(), _ => CanEncode);
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
        public ICommand ResetTimerCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand EncodeCommand { get; }
        public ICommand TrimCommand { get; }
        public ICommand CullCommand { get; }
        public ICommand CropCommand { get; }
        public ICommand DownloadFfmpegCommand { get; }
        public ICommand BrowseFfmpegCommand { get; }
        public ICommand CancelDownloadCommand { get; }
        public ICommand ShowOverlayCommand { get; }


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
