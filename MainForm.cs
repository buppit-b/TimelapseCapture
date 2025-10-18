using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading.Tasks;

namespace TimelapseCapture
{
    /// <summary>
    /// Main application form for timelapse capture functionality.
    /// Handles region selection, capture scheduling, and video encoding.
    /// </summary>
    public partial class MainForm : Form
    {
        #region Fields

        private AspectRatio? _selectedAspectRatio;
        private Rectangle captureRegion = Rectangle.Empty;
        private System.Threading.Timer? _captureTimer;
        private CaptureSettings settings = new CaptureSettings();
        private string? _ffmpegPath;
        private string? _activeSessionFolder;
        private SessionInfo? _activeSession;
        private bool _isEncoding = false;

        #endregion

        #region Initialization

        public MainForm()
        {
            InitializeComponent();
            ApplyModernStyling();
            LoadSettings();
            WireInitialValues();
            CheckForActiveSession();
            UpdateEstimate();
            InitializeFfmpeg();
        }

        /// <summary>
        /// Apply modern font styling to all controls.
        /// </summary>
        private void ApplyModernStyling()
        {
            foreach (Control c in this.Controls)
                c.Font = new Font("Segoe UI", 9f);
        }

        /// <summary>
        /// Initialize FFmpeg - find existing installation or download if needed.
        /// </summary>
        private async void InitializeFfmpeg()
        {
            _ffmpegPath = FfmpegRunner.FindFfmpeg(settings.FfmpegPath);
            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                if (lblStatus != null) lblStatus.Text = "Downloading/locating FFmpeg...";
                var dirTarget = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
                _ffmpegPath = await FfmpegDownloader.EnsureFfmpegPresentAsync(dirTarget);

                if (!string.IsNullOrEmpty(_ffmpegPath))
                {
                    settings.FfmpegPath = _ffmpegPath;
                    SaveSettings();
                    if (lblStatus != null) lblStatus.Text = "FFmpeg ready";
                }
                else
                {
                    if (lblStatus != null) lblStatus.Text = "FFmpeg not available - please browse to ffmpeg.exe";
                }
            }
            else
            {
                UpdateStatusDisplay();
            }

            if (txtFfmpegPath != null)
                txtFfmpegPath.Text = _ffmpegPath ?? "";
        }

        /// <summary>
        /// Check for any active session in the save folder and resume if found.
        /// </summary>
        private void CheckForActiveSession()
        {
            if (string.IsNullOrEmpty(settings.SaveFolder)) return;

            var capturesRoot = Path.Combine(settings.SaveFolder, "captures");
            _activeSessionFolder = SessionManager.FindActiveSession(capturesRoot);

            if (_activeSessionFolder != null)
            {
                _activeSession = SessionManager.LoadSession(_activeSessionFolder);
                if (_activeSession != null)
                {
                    captureRegion = _activeSession.CaptureRegion;
                    if (lblRegion != null)
                        lblRegion.Text = $"Region: {captureRegion.Width}×{captureRegion.Height} at ({captureRegion.X},{captureRegion.Y})";

                    if (numInterval != null) numInterval.Value = _activeSession.IntervalSeconds;
                    if (cmbFormat != null) cmbFormat.SelectedItem = _activeSession.ImageFormat ?? "JPEG";
                    if (numQuality != null && _activeSession.ImageFormat == "JPEG")
                        numQuality.Value = _activeSession.JpegQuality;

                    UpdateStatusDisplay();
                }
            }
        }

        /// <summary>
        /// Load saved settings and apply to UI controls.
        /// Auto-fixes any invalid region dimensions (e.g., odd widths/heights).
        /// </summary>
        private void WireInitialValues()
        {
            if (cmbFormat != null) cmbFormat.SelectedItem = settings.Format ?? "JPEG";
            if (numInterval != null) numInterval.Value = settings.IntervalSeconds > 0 ? settings.IntervalSeconds : 5;
            if (numDesiredSec != null) numDesiredSec.Value = 30;

            // Load and validate saved region
            if (settings.Region.HasValue && _activeSession == null)
            {
                var r = settings.Region.Value;

                if (r.Width > 0 && r.Height > 0)
                {
                    bool wasFixed = false;

                    // Ensure even dimensions for video encoding compatibility
                    if ((r.Width & 1) == 1)
                    {
                        r.Width = Math.Max(2, r.Width - 1);
                        wasFixed = true;
                    }
                    if ((r.Height & 1) == 1)
                    {
                        r.Height = Math.Max(2, r.Height - 1);
                        wasFixed = true;
                    }

                    captureRegion = r;

                    if (wasFixed)
                    {
                        settings.Region = r;
                        SaveSettings();
                    }

                    if (lblRegion != null)
                        lblRegion.Text = $"Region: {captureRegion.Width}×{captureRegion.Height} at ({captureRegion.X},{captureRegion.Y})";
                }
            }

            if (!string.IsNullOrEmpty(settings.SaveFolder))
            {
                if (lblFolder != null)
                    lblFolder.Text = "Save to: " + settings.SaveFolder;
            }

            if (trkQuality != null) trkQuality.Value = settings.JpegQuality;
            if (numQuality != null) numQuality.Value = settings.JpegQuality;
            if (lblQuality != null) lblQuality.Text = $"JPEG Quality: {settings.JpegQuality}";

            // Load saved aspect ratio preference
            if (cmbAspectRatio != null)
            {
                cmbAspectRatio.Items.Clear();
                cmbAspectRatio.Items.AddRange(AspectRatio.CommonRatios); // AspectRatio.ToString() returns Name

                int savedIndex = settings.AspectRatioIndex;
                if (savedIndex >= 0 && savedIndex < cmbAspectRatio.Items.Count)
                {
                    cmbAspectRatio.SelectedIndex = savedIndex;
                    _selectedAspectRatio = AspectRatio.CommonRatios[savedIndex];
                    if (_selectedAspectRatio.Width == 0)
                        _selectedAspectRatio = null;
                }
            }

            UpdateQualityControls();
        }

        #endregion

        #region Settings Management

        /// <summary>
        /// Load settings from persistent storage.
        /// </summary>
        private void LoadSettings() => settings = SettingsManager.Load();

        /// <summary>
        /// Save current settings to persistent storage.
        /// </summary>
        private void SaveSettings()
        {
            settings.Region = captureRegion;
            settings.IntervalSeconds = (int)(numInterval?.Value ?? 5);
            settings.Format = cmbFormat?.SelectedItem?.ToString();
            settings.JpegQuality = (int)(numQuality?.Value ?? 90);
            settings.FfmpegPath = _ffmpegPath;
            settings.AspectRatioIndex = cmbAspectRatio?.SelectedIndex ?? 0; // NEW
            SettingsManager.Save(settings);
        }

        /// <summary>
        /// Validate that a region has even dimensions (required for video encoding).
        /// </summary>
        private bool IsValidRegion(Rectangle r)
        {
            return r.Width > 0 && r.Height > 0 && (r.Width & 1) == 0 && (r.Height & 1) == 0;
        }

        #endregion

        #region UI Event Handlers

        /// <summary>
        /// Handle region selection button click.
        /// Shows full-screen overlay for region selection.
        /// </summary>
        private void btnSelectRegion_Click(object? sender, EventArgs e)
        {
            if (IsCapturing)
            {
                MessageBox.Show(
                    "Cannot change region while capturing. Stop capture first.",
                    "Cannot Change Region",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (_activeSession != null && captureRegion != Rectangle.Empty)
            {
                var result = MessageBox.Show(
                    $"An active session exists with {_activeSession.FramesCaptured} frames.\n\n" +
                    "Changing the region will require starting a new session.\n\n" +
                    "Do you want to:\n" +
                    "• YES: Close current session and start fresh\n" +
                    "• NO: Keep current session and cancel region change",
                    "Active Session Detected",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No) return;

                if (_activeSessionFolder != null) SessionManager.MarkSessionInactive(_activeSessionFolder);
                _activeSession = null;
                _activeSessionFolder = null;
            }

            // Hide main form temporarily for clean region selection
            Hide();
            Task.Delay(200).Wait();

            using (var selector = new RegionSelector(_selectedAspectRatio))
            {
                if (selector.ShowDialog() == DialogResult.OK)
                {
                    captureRegion = selector.SelectedRegion;

                    // Region is already validated and constrained by RegionSelector
                    // Just verify it's valid
                    if (!IsValidRegion(captureRegion))
                    {
                        MessageBox.Show(
                            $"Selected region has invalid dimensions: {captureRegion.Width}×{captureRegion.Height}\n\n" +
                            "This should not happen - please report this bug.",
                            "Invalid Region",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        captureRegion = Rectangle.Empty;
                    }
                    else
                    {
                        // Calculate and display aspect ratio
                        string ratioInfo = AspectRatio.CalculateRatioString(captureRegion.Width, captureRegion.Height);

                        if (lblRegion != null)
                            lblRegion.Text = $"Region: {captureRegion.Width}×{captureRegion.Height} ({ratioInfo}) at ({captureRegion.X},{captureRegion.Y})";

                        settings.Region = captureRegion;
                        SaveSettings();
                    }
                }
            }

            Show();
            UpdateStatusDisplay();
            UpdateEstimate();
        }

        /// <summary>
        /// Handle aspect ratio dropdown change.
        /// </summary>
        private void cmbAspectRatio_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (cmbAspectRatio == null) return;

            int index = cmbAspectRatio.SelectedIndex;
            if (index >= 0 && index < AspectRatio.CommonRatios.Length)
            {
                _selectedAspectRatio = AspectRatio.CommonRatios[index];

                // If "Free" mode (width=0), clear the lock
                if (_selectedAspectRatio.Width == 0)
                    _selectedAspectRatio = null;

                SaveSettings();
            }
        }

        /// <summary>
        /// Handle folder selection button click.
        /// </summary>
        private void btnChooseFolder_Click(object? sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                if (!string.IsNullOrEmpty(settings.SaveFolder))
                    fbd.SelectedPath = settings.SaveFolder;

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    settings.SaveFolder = fbd.SelectedPath;
                    if (lblFolder != null) lblFolder.Text = "Save to: " + settings.SaveFolder;
                    SaveSettings();
                    CheckForActiveSession();
                }
            }
        }

        /// <summary>
        /// Handle interval value change - update estimates and validate if session active.
        /// </summary>
        private void numInterval_ValueChanged(object? sender, EventArgs e)
        {
            // Always update estimates when interval changes
            UpdateEstimate();

            // If there's an active session with frames, warn about changing interval
            if (_activeSession != null && _activeSession.FramesCaptured > 0 && !IsCapturing)
            {
                int currentInterval = _activeSession.IntervalSeconds;
                int newInterval = (int)(numInterval?.Value ?? 5);

                if (currentInterval != newInterval)
                {
                    var result = MessageBox.Show(
                        $"This session was captured at {currentInterval} second intervals.\n\n" +
                        $"Changing to {newInterval} seconds will affect:\n" +
                        $"• Time calculations (will be inaccurate)\n" +
                        $"• Video playback speed consistency\n\n" +
                        $"Recommended: Start a new session for different intervals.\n\n" +
                        $"Continue with interval change?",
                        "Interval Change Warning",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (result == DialogResult.No)
                    {
                        // Revert to original interval
                        if (numInterval != null)
                            numInterval.Value = currentInterval;
                        return;
                    }
                    else
                    {
                        // User accepted - mark that interval has changed
                        // (We'll track this in the session)
                    }
                }
            }
        }

        /// <summary>
        /// Handle format dropdown change.
        /// Validates against active session and updates quality control visibility.
        /// </summary>
        private void cmbFormat_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_activeSession != null && !IsCapturing)
            {
                var newFormat = cmbFormat?.SelectedItem?.ToString();
                if (newFormat != _activeSession.ImageFormat)
                {
                    MessageBox.Show(
                        $"Active session uses {_activeSession.ImageFormat} format.\n" +
                        $"Format change requires starting a new session.",
                        "Format Mismatch",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    if (cmbFormat != null) cmbFormat.SelectedItem = _activeSession.ImageFormat;
                    return;
                }
            }

            UpdateQualityControls();
        }

        /// <summary>
        /// Handle quality trackbar scroll - sync with numeric control.
        /// </summary>
        private void trkQuality_Scroll(object? sender, EventArgs e)
        {
            if (numQuality != null && trkQuality != null)
            {
                numQuality.Value = trkQuality.Value;
                if (lblQuality != null)
                    lblQuality.Text = $"JPEG Quality: {trkQuality.Value}";
            }
        }

        /// <summary>
        /// Handle quality numeric control change - sync with trackbar.
        /// </summary>
        private void numQuality_ValueChanged(object? sender, EventArgs e)
        {
            if (numQuality != null && trkQuality != null)
            {
                trkQuality.Value = (int)numQuality.Value;
                if (lblQuality != null)
                    lblQuality.Text = $"JPEG Quality: {(int)numQuality.Value}";
            }
        }

        /// <summary>
        /// Enable/disable quality controls based on selected format (JPEG only).
        /// </summary>
        private void UpdateQualityControls()
        {
            bool jpeg = (cmbFormat?.SelectedItem?.ToString() ?? "JPEG") == "JPEG";
            if (trkQuality != null) trkQuality.Enabled = jpeg && !IsCapturing;
            if (numQuality != null) numQuality.Enabled = jpeg && !IsCapturing;
            if (lblQuality != null) lblQuality.Enabled = jpeg;
        }

        #endregion

        #region Capture Control

        /// <summary>
        /// Gets whether capture is currently active.
        /// </summary>
        private bool IsCapturing => _captureTimer != null;

        /// <summary>
        /// Start capture session.
        /// Validates settings and creates/resumes session.
        /// </summary>
        private void btnStart_Click(object? sender, EventArgs e)
        {
            // Validate region
            if (captureRegion.Width == 0 || captureRegion.Height == 0)
            {
                MessageBox.Show(
                    "Please select a capture region first.",
                    "Missing Region",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!IsValidRegion(captureRegion))
            {
                MessageBox.Show(
                    $"Invalid capture region dimensions: {captureRegion.Width}×{captureRegion.Height}\n\n" +
                    "Dimensions must be even numbers for video encoding.\n" +
                    "Please select a new region.",
                    "Invalid Region",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrEmpty(settings.SaveFolder))
            {
                MessageBox.Show(
                    "Please select a save folder.",
                    "Missing Folder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            int intervalSec = (int)(numInterval?.Value ?? 5);
            var format = cmbFormat?.SelectedItem?.ToString() ?? "JPEG";
            var quality = (int)(numQuality?.Value ?? 90);
            var capturesRoot = Path.Combine(settings.SaveFolder, "captures");

            // Create new session or validate existing
            if (_activeSession == null)
            {
                _activeSessionFolder = SessionManager.CreateNewSession(capturesRoot, intervalSec, captureRegion, format, quality);
                _activeSession = SessionManager.LoadSession(_activeSessionFolder);
            }
            else
            {
                if (!SessionManager.ValidateSessionSettings(_activeSession, captureRegion, format, quality))
                {
                    var result = MessageBox.Show(
                        "Current settings don't match the active session.\n\n" +
                        "Start a new session?",
                        "Settings Mismatch",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (result == DialogResult.Yes)
                    {
                        SessionManager.MarkSessionInactive(_activeSessionFolder!);
                        _activeSessionFolder = SessionManager.CreateNewSession(capturesRoot, intervalSec, captureRegion, format, quality);
                        _activeSession = SessionManager.LoadSession(_activeSessionFolder);
                    }
                    else
                    {
                        return;
                    }
                }
            }

            // Lock UI and start capture timer
            LockCaptureUI(true);
            settings.IntervalSeconds = intervalSec;
            settings.Format = format;
            settings.JpegQuality = quality;
            SaveSettings();

            var intervalMs = intervalSec * 1000;
            _captureTimer = new System.Threading.Timer(CaptureFrame, null, 0, intervalMs);
            UpdateStatusDisplay();
            UpdateEstimate();
        }

        /// <summary>
        /// Stop capture session.
        /// </summary>
        private void btnStop_Click(object? sender, EventArgs e) => StopCapture();

        /// <summary>
        /// Stop capture and unlock UI.
        /// </summary>
        private void StopCapture()
        {
            if (_captureTimer != null)
            {
                _captureTimer.Dispose();
                _captureTimer = null;
            }

            LockCaptureUI(false);
            UpdateStatusDisplay();
            SaveSettings();
            UpdateEstimate();
        }

        /// <summary>
        /// Lock/unlock UI controls during capture.
        /// Prevents changing settings that would invalidate the session.
        /// </summary>
        private void LockCaptureUI(bool locked)
        {
            if (numInterval != null) numInterval.Enabled = !locked;
            if (cmbFormat != null) cmbFormat.Enabled = !locked;
            if (trkQuality != null) trkQuality.Enabled = !locked && (cmbFormat?.SelectedItem?.ToString() == "JPEG");
            if (numQuality != null) numQuality.Enabled = !locked && (cmbFormat?.SelectedItem?.ToString() == "JPEG");
            if (btnSelectRegion != null) btnSelectRegion.Enabled = !locked;
            if (btnChooseFolder != null) btnChooseFolder.Enabled = !locked;
            if (btnBrowseFfmpeg != null) btnBrowseFfmpeg.Enabled = !locked;
            if (btnStart != null) btnStart.Enabled = !locked;
            if (btnStop != null) btnStop.Enabled = locked;
            if (btnEncode != null) btnEncode.Enabled = !locked;
        }


        /// <summary>
        /// Capture a single frame (called by timer).
        /// Tracks actual elapsed time for accurate statistics.
        /// </summary>
        private void CaptureFrame(object? state)
        {
            if (_activeSession == null || _captureTimer == null) return;

            try
            {
                DateTime now = DateTime.UtcNow;

                // Calculate actual elapsed time since last capture
                if (_activeSession.LastCaptureTime.HasValue)
                {
                    double elapsedSeconds = (now - _activeSession.LastCaptureTime.Value).TotalSeconds;
                    _activeSession.TotalCaptureSeconds += elapsedSeconds;
                }

                _activeSession.LastCaptureTime = now;

                // Use sequential frame numbering
                long nextFrameNumber = _activeSession.FramesCaptured + 1;
                string frameNumber = $"{nextFrameNumber:D5}";

                string framesFolder = SessionManager.GetFramesFolder(_activeSessionFolder!);
                string fileName = Path.Combine(framesFolder, $"{frameNumber}.jpg");

                using (var bmp = CaptureScreen())
                {
                    bmp.Save(fileName, ImageFormat.Jpeg);
                }

                SessionManager.IncrementFrameCount(_activeSessionFolder!);
                _activeSession = SessionManager.LoadSession(_activeSessionFolder);

                BeginInvoke(new Action(() =>
                {
                    UpdateStatusDisplay();
                    UpdateEstimate();
                }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Capture error: {ex.Message}");

                BeginInvoke(new Action(() =>
                {
                    if (lblStatus != null)
                        lblStatus.Text = $"⚠️ Capture error - check logs";
                }));
            }
        }

        /// <summary>
        /// Capture the screen region as a bitmap.
        /// </summary>
        private Bitmap CaptureScreen()
        {
            var bmp = new Bitmap(captureRegion.Width, captureRegion.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(captureRegion.Location, Point.Empty, captureRegion.Size);
            }
            return bmp;
        }

        #endregion

        #region Display Updates

        /// <summary>
        /// Update status label with current capture state.
        /// </summary>
        private void UpdateStatusDisplay()
        {
            if (lblStatus == null) return;

            if (IsCapturing)
                lblStatus.Text = $"🔴 Capturing ({_activeSession?.FramesCaptured ?? 0} frames)";
            else if (_activeSession != null)
                lblStatus.Text = $"Session: {_activeSession.Name} ({_activeSession.FramesCaptured} frames)";
            else
                lblStatus.Text = "Ready - No active session";
        }

        /// <summary>
        /// Update video length estimates based on current frame count.
        /// Uses actual tracked capture time for accuracy.
        /// </summary>
        private void UpdateEstimate()
        {
            if (lblEstimate == null) return;

            if (_activeSession != null && _activeSession.FramesCaptured > 0)
            {
                int frames = (int)_activeSession.FramesCaptured;

                // Use ACTUAL tracked time, not calculated from interval
                double captureTimeSeconds = _activeSession.TotalCaptureSeconds;

                string captureTimeDisplay;
                if (captureTimeSeconds < 60)
                {
                    captureTimeDisplay = $"{captureTimeSeconds:F0}sec";
                }
                else if (captureTimeSeconds < 3600)
                {
                    double captureTimeMinutes = captureTimeSeconds / 60.0;
                    captureTimeDisplay = $"{captureTimeMinutes:F1}min";
                }
                else
                {
                    double captureTimeHours = captureTimeSeconds / 3600.0;
                    captureTimeDisplay = $"{captureTimeHours:F1}hr";
                }

                // Calculate resulting video length at different frame rates
                double videoAt25fps = frames / 25.0;
                double videoAt30fps = frames / 30.0;
                double videoAt60fps = frames / 60.0;

                // Show warning if interval was changed
                string warningText = _activeSession.IntervalChanged ? " ⚠️" : "";

                lblEstimate.Text =
                    $"{frames} frames • {captureTimeDisplay} real time{warningText}\n" +
                    $"Video: {videoAt25fps:F1}s @25fps | {videoAt30fps:F1}s @30fps | {videoAt60fps:F1}s @60fps";
            }
            else if (_activeSession != null)
            {
                lblEstimate.Text = "Session active • 0 frames captured yet";
            }
            else
            {
                // Planning mode - uses current interval setting
                int desiredSec = (int)(numDesiredSec?.Value ?? 30);
                int interval = (int)(numInterval?.Value ?? 5);
                int neededFrames = desiredSec * 25;
                double captureTimeSeconds = neededFrames * interval;

                string captureTimeDisplay;
                if (captureTimeSeconds < 60)
                {
                    captureTimeDisplay = $"{captureTimeSeconds:F0} seconds";
                }
                else if (captureTimeSeconds < 3600)
                {
                    captureTimeDisplay = $"{(captureTimeSeconds / 60.0):F0} minutes";
                }
                else
                {
                    captureTimeDisplay = $"{(captureTimeSeconds / 3600.0):F1} hours";
                }

                lblEstimate.Text =
                    $"For {desiredSec}s video @25fps:\n" +
                    $"Need {neededFrames} frames • {captureTimeDisplay} capture time";
            }
        }



        #endregion

        #region FFmpeg & Encoding

        /// <summary>
        /// Open the session folder (or output folder if encoding complete).
        /// Smart detection: if videos exist, open output/, otherwise open frames/.
        /// </summary>
        private void btnOpenFolder_Click(object? sender, EventArgs e)
        {
            string folderToOpen;

            if (_activeSessionFolder != null && Directory.Exists(_activeSessionFolder))
            {
                // Check if output videos exist
                string outputFolder = SessionManager.GetOutputFolder(_activeSessionFolder);
                if (Directory.Exists(outputFolder) && Directory.GetFiles(outputFolder, "*.mp4").Length > 0)
                {
                    // Open output folder if videos exist
                    folderToOpen = outputFolder;
                }
                else
                {
                    // Otherwise open frames folder
                    string framesFolder = SessionManager.GetFramesFolder(_activeSessionFolder);
                    folderToOpen = Directory.Exists(framesFolder) ? framesFolder : _activeSessionFolder;
                }
            }
            else if (!string.IsNullOrEmpty(settings.SaveFolder) && Directory.Exists(settings.SaveFolder))
            {
                folderToOpen = settings.SaveFolder;
            }
            else
            {
                MessageBox.Show("No folder to open.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo() { FileName = folderToOpen, UseShellExecute = true });
        }

        /// <summary>
        /// Browse for ffmpeg.exe manually.
        /// </summary>
        private void btnBrowseFfmpeg_Click(object? sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "ffmpeg.exe|ffmpeg.exe|All files|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    _ffmpegPath = ofd.FileName;
                    if (txtFfmpegPath != null) txtFfmpegPath.Text = _ffmpegPath;
                    settings.FfmpegPath = _ffmpegPath;
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// Encode captured frames into a video file using FFmpeg.
        /// Fast pre-validation prevents multiple clicks and slow errors.
        /// </summary>
        private async void btnEncode_Click(object? sender, EventArgs e)
        {
            // FAST PRE-CHECK: Prevent multiple clicks
            if (_isEncoding)
            {
                MessageBox.Show(
                    "Encoding already in progress.\nPlease wait for current encode to complete.",
                    "Encoding In Progress",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // FAST PRE-CHECK: FFmpeg must be configured before we start
            if (string.IsNullOrEmpty(_ffmpegPath) || !File.Exists(_ffmpegPath))
            {
                var result = MessageBox.Show(
                    "FFmpeg is not configured!\n\n" +
                    "FFmpeg is required for video encoding but has not been set up.\n\n" +
                    "Would you like to:\n" +
                    "• YES: Browse for ffmpeg.exe on your system\n" +
                    "• NO: Cancel encoding\n\n" +
                    "To download FFmpeg:\n" +
                    "Visit https://ffmpeg.org/download.html",
                    "FFmpeg Not Found",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    // Trigger browse dialog
                    btnBrowseFfmpeg_Click(sender, e);
                    return;
                }
                else
                {
                    return;
                }
            }

            // FAST PRE-CHECK: Must have save folder
            if (string.IsNullOrEmpty(settings.SaveFolder))
            {
                MessageBox.Show(
                    "Please select a save folder first.",
                    "No Save Folder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // FAST PRE-CHECK: Must have frames
            string sessionFolder = _activeSessionFolder ?? settings.SaveFolder;
            var frameFiles = SessionManager.GetFrameFiles(sessionFolder);

            if (frameFiles.Length == 0)
            {
                MessageBox.Show(
                    "No frames to encode!\n\n" +
                    "Please capture some frames before encoding.",
                    "No Frames",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // FAST PRE-CHECK: Valid region
            if (!IsValidRegion(captureRegion))
            {
                MessageBox.Show(
                    "Invalid capture region dimensions.\n\n" +
                    "Please select a new region before encoding.",
                    "Invalid Region",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            // All pre-checks passed - start encoding
            _isEncoding = true;

            // Update UI to show encoding in progress
            if (btnEncode != null)
            {
                btnEncode.Enabled = false;
                btnEncode.Text = "🎬 Encoding...";
            }
            if (lblStatus != null)
                lblStatus.Text = $"Encoding {frameFiles.Length} frames...";

            try
            {
                // Verify FFmpeg still accessible (final check)
                if (!File.Exists(_ffmpegPath))
                {
                    throw new FileNotFoundException($"FFmpeg not found at: {_ffmpegPath}");
                }

                // Create filelist in .temp/ folder
                string tempFolder = SessionManager.GetTempFolder(sessionFolder);
                Directory.CreateDirectory(tempFolder);
                string fileListPath = Path.Combine(tempFolder, "filelist.txt");

                using (var writer = new StreamWriter(fileListPath, false))
                {
                    foreach (var file in frameFiles)
                    {
                        writer.WriteLine($"file '{file.Replace("'", "'\\''")}'");
                    }
                }

                // Output to organized output/ folder
                string outputFolder = SessionManager.GetOutputFolder(sessionFolder);
                Directory.CreateDirectory(outputFolder);
                string outputPath = Path.Combine(outputFolder, $"timelapse_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

                string ffmpegArgs = $"-y -f concat -safe 0 -i \"{fileListPath}\" -r 25 -c:v libx264 -crf 23 -preset medium \"{outputPath}\"";

                var result = await FfmpegRunner.RunFfmpegAsync(_ffmpegPath, ffmpegArgs);

                // Clean up temp folder
                SessionManager.CleanTempFolder(sessionFolder);

                if (result.exitCode == 0)
                {
                    if (lblStatus != null)
                        lblStatus.Text = "✅ Encoding complete!";

                    MessageBox.Show(
                        $"✅ Video encoded successfully!\n\n" +
                        $"Frames: {frameFiles.Length}\n" +
                        $"Output: {Path.GetFileName(outputPath)}\n" +
                        $"Location: output/ folder\n\n" +
                        $"Full path:\n{outputPath}",
                        "Encoding Complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    try
                    {
                        Process.Start("explorer.exe", $"/select,\"{outputPath}\"");
                    }
                    catch
                    {
                        Process.Start(new ProcessStartInfo { FileName = outputFolder, UseShellExecute = true });
                    }
                }
                else
                {
                    if (lblStatus != null)
                        lblStatus.Text = "❌ Encoding failed";

                    MessageBox.Show(
                        $"FFmpeg encoding failed with exit code {result.exitCode}\n\n" +
                        $"Error output:\n{result.error}\n\n" +
                        $"Standard output:\n{result.output}",
                        "Encoding Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            catch (FileNotFoundException fnfEx)
            {
                if (lblStatus != null)
                    lblStatus.Text = "❌ FFmpeg not found";

                MessageBox.Show(
                    $"FFmpeg executable not found:\n\n{fnfEx.Message}\n\n" +
                    "Please use 'Browse...' button to locate ffmpeg.exe",
                    "FFmpeg Not Found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch (IOException ioEx)
            {
                if (lblStatus != null)
                    lblStatus.Text = "❌ File access error";

                MessageBox.Show(
                    $"File access error during encoding:\n\n{ioEx.Message}\n\n" +
                    "This may be caused by:\n" +
                    "• Another program using the files\n" +
                    "• Insufficient permissions\n" +
                    "• Disk full or read-only",
                    "File Access Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch (UnauthorizedAccessException uaEx)
            {
                if (lblStatus != null)
                    lblStatus.Text = "❌ Permission denied";

                MessageBox.Show(
                    $"Permission denied:\n\n{uaEx.Message}\n\n" +
                    "Try running the application as administrator.",
                    "Permission Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                if (lblStatus != null)
                    lblStatus.Text = "❌ Encoding error";

                MessageBox.Show(
                    $"Unexpected error during encoding:\n\n{ex.Message}\n\n" +
                    $"Type: {ex.GetType().Name}",
                    "Encoding Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                // Always reset encoding state
                _isEncoding = false;

                if (btnEncode != null)
                {
                    btnEncode.Enabled = true;
                    btnEncode.Text = "🎬 Encode Video";
                }

                UpdateStatusDisplay();
            }
        }


        #endregion

        #region Form Lifecycle

        /// <summary>
        /// Handle form closing - cleanup resources and mark session inactive.
        /// </summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _captureTimer?.Dispose();

            if (_activeSessionFolder != null)
                SessionManager.MarkSessionInactive(_activeSessionFolder);
        }

        #endregion
    }
}