using System.Windows.Input;
using Microsoft.Win32;
using TimelapseCapture; // Core: SettingsManager, CaptureSettings, FfmpegRunner, SessionManager

namespace TimelapseCapture.Wpf.ViewModels
{
    /// <summary>
    /// Main window view-model. Connected to the reused TimelapseCapture.Core services so the WPF
    /// shell shows real state (settings, ffmpeg availability) rather than mock data. Capture /
    /// region / encode wiring is built out incrementally.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly CaptureSettings _settings;

        public MainViewModel()
        {
            _settings = SettingsManager.Load();
            _outputFolder = string.IsNullOrWhiteSpace(_settings.SaveFolder) ? "(not set)" : _settings.SaveFolder!;
            RefreshFfmpegStatus();

            ChooseFolderCommand = new RelayCommand(_ => ChooseFolder());
        }

        private string _outputFolder;
        public string OutputFolder
        {
            get => _outputFolder;
            set => SetProperty(ref _outputFolder, value);
        }

        private string _ffmpegStatus = "Checking…";
        public string FfmpegStatus
        {
            get => _ffmpegStatus;
            set => SetProperty(ref _ffmpegStatus, value);
        }

        private bool _ffmpegReady;
        public bool FfmpegReady
        {
            get => _ffmpegReady;
            set => SetProperty(ref _ffmpegReady, value);
        }

        public string SessionName => "No active session";

        public string StatusText => "Choose an output folder and create a session to begin.";

        public ICommand ChooseFolderCommand { get; }

        private void ChooseFolder()
        {
            var dlg = new OpenFolderDialog { Title = "Select output folder for captures" };
            if (dlg.ShowDialog() == true)
            {
                OutputFolder = dlg.FolderName;
                _settings.SaveFolder = dlg.FolderName;
                SettingsManager.Save(_settings);
            }
        }

        private void RefreshFfmpegStatus()
        {
            var path = FfmpegRunner.FindFfmpeg(_settings.FfmpegPath);
            FfmpegReady = !string.IsNullOrEmpty(path);
            FfmpegStatus = FfmpegReady ? "Ready" : "Not found — download or browse in Settings";
        }
    }
}
