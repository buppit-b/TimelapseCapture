using System.Windows.Forms;

namespace TimelapseCapture
{
    /// <summary>
    /// Partial class extension to expose controls for state management.
    /// This allows ControlStateManager to access form controls without breaking encapsulation.
    /// </summary>
    public partial class MainForm
    {
        // Expose controls for state management
        internal Button BtnChooseFolder => btnChooseFolder;
        internal Button BtnDownloadFfmpeg => btnDownloadFfmpeg;
        internal Button BtnBrowseFfmpeg => btnBrowseFfmpeg;
        internal Button BtnNewSession => btnNewSession;
        internal Button BtnLoadSession => btnLoadSession;
        internal Button BtnSelectRegion => btnSelectRegion;
        internal Button BtnFullScreen => btnFullScreen;
        internal Button BtnShowRegion => btnShowRegion;
        internal Button BtnStart => btnStart;
        internal Button BtnStop => btnStop;
        internal Button BtnEncode => btnEncode;
        internal Button BtnOpenFolder => btnOpenFolder;
        
        internal TextBox TxtSessionName => txtSessionName;
        internal NumericUpDown NumInterval => numInterval;
        internal ComboBox CmbFormat => cmbFormat;
        internal ComboBox CmbAspectRatio => cmbAspectRatio;
        internal NumericUpDown NumQuality => numQuality;
        internal TrackBar TrkQuality => trkQuality;
        
        internal ComboBox CmbFrameRate => cmbFrameRate;
        internal NumericUpDown NumCustomFrameRate => numCustomFrameRate;
        internal ComboBox CmbEncodingPreset => cmbEncodingPreset;
        internal ComboBox CmbVideoCodec => cmbVideoCodec;
        internal NumericUpDown NumCrf => numCrf;
        internal NumericUpDown NumDesiredSec => numDesiredSec;

        /// <summary>
        /// Property to check if capturing is currently in progress.
        /// </summary>
        internal bool IsCapturing => _captureTimer != null;

        /// <summary>
        /// Update all control states based on current application status.
        /// Call this whenever any state changes that might affect what the user can do.
        /// </summary>
        private void UpdateControlStates()
        {
            bool hasOutputFolder = !string.IsNullOrEmpty(settings.SaveFolder);
            bool hasFfmpeg = !string.IsNullOrEmpty(_ffmpegPath) && System.IO.File.Exists(_ffmpegPath);
            bool hasSession = _activeSession != null;
            bool hasRegion = captureRegion.HasValue;
            bool isCapturing = IsCapturing;
            bool isEncoding = _isEncoding;
            bool hasFrames = _activeSession?.FramesCaptured > 0;

            ControlStateManager.UpdateAllControlStates(
                this,
                hasOutputFolder,
                hasFfmpeg,
                hasSession,
                hasRegion,
                isCapturing,
                isEncoding,
                hasFrames
            );

            // Also update readiness panel
            UpdateReadinessPanel();
            
            // Update tooltips from control.Tag values
            if (_mainTooltip != null)
            {
                ControlStateManager.SetupTooltips(this, _mainTooltip);
            }
        }

        // ToolTip component for showing helpful hints
        private ToolTip? _mainTooltip;

        /// <summary>
        /// Initialize the tooltip system.
        /// </summary>
        private void InitializeTooltips()
        {
            _mainTooltip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 500,
                ReshowDelay = 100,
                ShowAlways = true
            };
        }
    }
}
