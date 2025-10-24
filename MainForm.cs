using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
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
        private System.Threading.Timer? _captureTimer;
        private CaptureSettings settings = new CaptureSettings();
        private string? _ffmpegPath;
        private string? _activeSessionFolder;
        private SessionInfo? _activeSession;
        private bool _isEncoding = false;
        private System.Windows.Forms.Timer? _uiUpdateTimer;

        // Remember the user's aspect-ratio selection so we can restore it when leaving Full Screen.
        private int _lastAspectRatioIndex = 0;

        // Suppress saving when programmatically changing aspect ratio (prevents unwanted saves during restoration)
        private bool _suppressAspectRatioSave = false;

        // Track consecutive capture errors for safety
        private int _consecutiveCaptureErrors = 0;

        // Encoding settings
        private int _targetFrameRate = 25; // Default to 25fps

        // Region overlay
        private RegionOverlay? _regionOverlay;
        private bool _isOverlayVisible = false;

        private Rectangle captureRegion = Rectangle.Empty;

        /// <summary>
        /// Get the current capture region - session takes priority over settings.
        /// </summary>
        private Rectangle GetCurrentRegion()
        {
            // Session region takes priority
            if (_activeSession != null && _activeSession.CaptureRegion.HasValue)
                return _activeSession.CaptureRegion.Value;
            
            // Fall back to settings if no session
            if (settings.Region.HasValue)
                return settings.Region.Value;
            
            return Rectangle.Empty;
        }

        /// <summary>
        /// Set the current capture region - synchronizes ALL state locations.
        /// This is the ONLY way to change the region to ensure consistency.
        /// </summary>
        private void SetCurrentRegion(Rectangle region)
        {
            Logger.Log("Region", $"SetCurrentRegion called with: {region}");
            
            // Sanitize dimensions (must be even)
            if ((region.Width & 1) == 1) region.Width = Math.Max(2, region.Width - 1);
            if ((region.Height & 1) == 1) region.Height = Math.Max(2, region.Height - 1);
            
            // Update local runtime state
            captureRegion = region;
            
            // Update session if exists
            if (_activeSession != null)
            {
                _activeSession.CaptureRegion = region;
                if (_activeSessionFolder != null)
                {
                    SessionManager.SaveSession(_activeSessionFolder, _activeSession);
                    Logger.Log("Region", "Session updated with region");
                }
            }
            
            // Update settings
            settings.Region = region;
            SaveSettings();
            
            // Update UI
            UpdateRegionDisplay();
            UpdateSessionInfoPanel();
            
            // Log final state
            Logger.LogState("Sync", "captureRegion", captureRegion);
            Logger.LogState("Sync", "_activeSession.CaptureRegion", _activeSession?.CaptureRegion);
            Logger.LogState("Sync", "settings.Region", settings.Region);
        }

        /// <summary>
        /// Clear the current region from all state locations.
        /// </summary>
        private void ClearCurrentRegion()
        {
            Logger.Log("Region", "ClearCurrentRegion called");
            
            captureRegion = Rectangle.Empty;
            
            if (_activeSession != null)
            {
                _activeSession.CaptureRegion = null;
                if (_activeSessionFolder != null)
                    SessionManager.SaveSession(_activeSessionFolder, _activeSession);
            }
            
            settings.Region = null;
            SaveSettings();
            
            UpdateRegionDisplay();
            UpdateSessionInfoPanel();
        }

        /// <summary>
        /// Update the region display label.
        /// </summary>
        private void UpdateRegionDisplay()
        {
            if (lblRegion == null) return;
            
            if (captureRegion.IsEmpty || captureRegion.Width == 0 || captureRegion.Height == 0)
            {
                lblRegion.Text = "No region selected";
            }
            else
            {
                string ratioInfo = AspectRatio.CalculateRatioString(captureRegion.Width, captureRegion.Height);
                lblRegion.Text = $"Region: {captureRegion.Width}×{captureRegion.Height} ({ratioInfo}) at ({captureRegion.X},{captureRegion.Y})";
            }
        }

        /// <summary>
        /// DEPRECATED: Use SetCurrentRegion() instead.
        /// Left for backward compatibility during transition.
        /// </summary>
        private void SetCaptureRegionFromNullable(Rectangle? maybeRegion)
        {
            if (maybeRegion.HasValue)
                SetCurrentRegion(maybeRegion.Value);
            else
                ClearCurrentRegion();
        }

        /// <summary>
        /// Validate that all region state locations are synchronized.
        /// Returns true if consistent, logs and returns false if desync detected.
        /// </summary>
        private bool ValidateRegionStateConsistency()
        {
            var sessionRegion = _activeSession?.CaptureRegion;
            var settingsRegion = settings.Region;
            var runtimeRegion = captureRegion;
            
            bool consistent = true;
            
            // If we have a session, runtime should match session
            if (_activeSession != null)
            {
                if (sessionRegion.HasValue != !runtimeRegion.IsEmpty)
                {
                    Logger.Log("DESYNC", $"Session has region: {sessionRegion.HasValue}, Runtime is empty: {runtimeRegion.IsEmpty}");
                    consistent = false;
                }
                else if (sessionRegion.HasValue && sessionRegion.Value != runtimeRegion)
                {
                    Logger.Log("DESYNC", $"Session region {sessionRegion.Value} != Runtime region {runtimeRegion}");
                    consistent = false;
                }
            }
            
            // Settings should always match runtime (it's a cache)
            if (settingsRegion.HasValue != !runtimeRegion.IsEmpty)
            {
                Logger.Log("DESYNC", $"Settings has region: {settingsRegion.HasValue}, Runtime is empty: {runtimeRegion.IsEmpty}");
                consistent = false;
            }
            else if (settingsRegion.HasValue && settingsRegion.Value != runtimeRegion)
            {
                Logger.Log("DESYNC", $"Settings region {settingsRegion.Value} != Runtime region {runtimeRegion}");
                consistent = false;
            }
            
            if (!consistent)
            {
                Logger.Log("DESYNC", "=== STATE DUMP ===");
                Logger.LogState("Runtime", "captureRegion", runtimeRegion);
                Logger.LogState("Session", "_activeSession.CaptureRegion", sessionRegion);
                Logger.LogState("Settings", "settings.Region", settingsRegion);
            }
            
            return consistent;
        }

        #endregion

        #region Initialization

        public MainForm()
        {
            InitializeComponent();
            ApplyModernStyling();
            LoadSettings();
            WireInitialValues();
            CheckForActiveSession();
            UpdateCaptureTimer();
            InitializeFfmpeg();

            _uiUpdateTimer = new System.Windows.Forms.Timer();
            _uiUpdateTimer.Interval = Constants.UI_UPDATE_INTERVAL_MS;
            _uiUpdateTimer.Tick += (s, e) => UpdateCaptureTimer();
            _uiUpdateTimer.Start();

            // Initialize region overlay
            InitializeRegionOverlay();

            // Register keyboard shortcuts
            this.KeyPreview = true;
            this.KeyDown += MainForm_KeyDown;
        }

        /// <summary>
        /// Initialize the region overlay system.
        /// </summary>
        private void InitializeRegionOverlay()
        {
            _regionOverlay = new RegionOverlay();
            _regionOverlay.Hide();
            UpdateRegionOverlayButton();
        }

        /// <summary>
        /// Handle keyboard shortcuts.
        /// </summary>
        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            // Ctrl+R: Toggle region overlay
            if (e.Control && e.KeyCode == Keys.R)
            {
                e.Handled = true;
                ToggleRegionOverlay();
            }
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
        /// Initialize FFmpeg - find existing installation only (no auto-download).
        /// </summary>
        private void InitializeFfmpeg()
        {
            _ffmpegPath = FfmpegRunner.FindFfmpeg(settings.FfmpegPath);

            if (txtFfmpegPath != null)
                txtFfmpegPath.Text = _ffmpegPath ?? "Not configured";

            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                // Don't auto-download - let user initiate it
                if (lblStatus != null)
                    lblStatus.Text = "⚠️ FFmpeg not found - Click 'Download FFmpeg' button";
            }
            else
            {
                UpdateStatusDisplay();
            }
        }

        // Track if FFmpeg download is in progress
        private bool _isDownloadingFfmpeg = false;

        /// <summary>
        /// Download FFmpeg when user requests it.
        /// Prevents multiple simultaneous downloads.
        /// </summary>
        private async void DownloadFfmpeg()
        {
            // Prevent spam clicking
            if (_isDownloadingFfmpeg)
            {
                MessageBox.Show(
                    "FFmpeg download is already in progress.\n\nPlease wait for it to complete.",
                    "Download In Progress",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!string.IsNullOrEmpty(_ffmpegPath) && File.Exists(_ffmpegPath))
            {
                var result = MessageBox.Show(
                    "FFmpeg is already installed.\n\nDo you want to download again?",
                    "FFmpeg Already Installed",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No)
                    return;
            }

            _isDownloadingFfmpeg = true;
            Logger.Log("FFmpeg", "Download started");

            if (lblStatus != null) lblStatus.Text = "Starting FFmpeg download...";
            var dirTarget = Path.Combine(AppContext.BaseDirectory, "ffmpeg");

            // Disable ALL buttons during download
            if (btnDownloadFfmpeg != null) btnDownloadFfmpeg.Enabled = false;
            if (btnBrowseFfmpeg != null) btnBrowseFfmpeg.Enabled = false;
            if (btnEncode != null) btnEncode.Enabled = false;

            try
            {
                _ffmpegPath = await FfmpegDownloader.EnsureFfmpegPresentAsync(dirTarget,
                    (bytesDownloaded, totalBytes, status) =>
                    {
                        // Update status label on UI thread
                        if (lblStatus != null && !lblStatus.IsDisposed)
                        {
                            try
                            {
                                if (InvokeRequired)
                                    Invoke(new Action(() => lblStatus.Text = status));
                                else
                                    lblStatus.Text = status;
                            }
                            catch { /* Form may be closing */ }
                        }
                    });

                if (!string.IsNullOrEmpty(_ffmpegPath))
                {
                    settings.FfmpegPath = _ffmpegPath;
                    SaveSettings();
                    if (lblStatus != null) lblStatus.Text = "✅ FFmpeg ready!";
                    if (txtFfmpegPath != null) txtFfmpegPath.Text = _ffmpegPath;

                    MessageBox.Show(
                        "FFmpeg downloaded and installed successfully!\n\n" +
                        $"Location: {_ffmpegPath}",
                        "Download Complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    if (lblStatus != null) lblStatus.Text = "❌ FFmpeg download failed";
                    MessageBox.Show(
                        "Failed to download FFmpeg.\n\n" +
                        "Please check your internet connection or use the Browse button to locate ffmpeg.exe manually.",
                        "Download Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            finally
            {
                // Re-enable buttons
                _isDownloadingFfmpeg = false;
                Logger.Log("FFmpeg", "Download completed");
                if (btnDownloadFfmpeg != null) btnDownloadFfmpeg.Enabled = true;
                if (btnBrowseFfmpeg != null) btnBrowseFfmpeg.Enabled = true;
                if (btnEncode != null) btnEncode.Enabled = true;
            }
        }

        /// <summary>
        /// Check for any active session in the save folder and resume if found.
        /// </summary>
        private void CheckForActiveSession()
        {
            if (string.IsNullOrEmpty(settings.SaveFolder)) return;

            var capturesRoot = Path.Combine(settings.SaveFolder, "captures");
            _activeSessionFolder = SessionManager.FindActiveSession(capturesRoot);

            if (!string.IsNullOrEmpty(_activeSessionFolder))
            {
                _activeSession = SessionManager.LoadSession(_activeSessionFolder);
                if (_activeSession != null)
                {
                    // Use helper to safely set captureRegion from nullable session value
                    SetCaptureRegionFromNullable(_activeSession.CaptureRegion);

                    if (numInterval != null) numInterval.Value = _activeSession.IntervalSeconds;
                    if (cmbFormat != null) cmbFormat.SelectedItem = _activeSession.ImageFormat ?? "JPEG";
                    if (numQuality != null && _activeSession.ImageFormat == "JPEG")
                        numQuality.Value = _activeSession.JpegQuality;

                    // Update session name display
                    if (txtSessionName != null)
                        txtSessionName.Text = _activeSession.Name ?? "Session";

                    UpdateStatusDisplay();
                    UpdateSessionInfoPanel();
                }
            }
            else
            {
                // If no active session found, clear any stored region from settings
                if (settings.Region.HasValue)
                {
                    captureRegion = Rectangle.Empty;
                    settings.Region = null;
                    SaveSettings();
                }
                if (lblRegion != null)
                    lblRegion.Text = "No region selected";
                if (txtSessionName != null)
                    txtSessionName.Text = "No active session";
            }
        }

        /// <summary>
        /// Load saved settings and apply to UI controls.
        /// Auto-fixes any invalid region dimensions (e.g., odd widths/heights).
        /// </summary>
        private void WireInitialValues()
        {
            // Load session name display
            if (txtSessionName != null)
            {
                if (_activeSession != null && !string.IsNullOrEmpty(_activeSession.Name))
                {
                    txtSessionName.Text = _activeSession.Name;
                }
                else
                {
                    txtSessionName.Text = "No active session";
                }
            }

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
                    if (lblRegion != null) lblRegion.Text = $"Region: {captureRegion.Width}×{captureRegion.Height} at ({captureRegion.X},{captureRegion.Y})";
                }
            }

            if (!string.IsNullOrEmpty(settings.SaveFolder))
            {
                if (lblFolder != null) lblFolder.Text = "Save to: " + settings.SaveFolder;
            }

            if (trkQuality != null) trkQuality.Value = settings.JpegQuality;
            if (numQuality != null) numQuality.Value = settings.JpegQuality;
            if (lblQuality != null) lblQuality.Text = $"JPEG Quality: {settings.JpegQuality}";

            // Load saved aspect ratio preference
            if (cmbAspectRatio != null)
            {
                cmbAspectRatio.Items.Clear();
                cmbAspectRatio.Items.AddRange(AspectRatio.CommonRatios);
                int savedIndex = settings.AspectRatioIndex;
                if (savedIndex >= 0 && savedIndex < cmbAspectRatio.Items.Count)
                {
                    cmbAspectRatio.SelectedIndex = savedIndex;
                    _selectedAspectRatio = AspectRatio.CommonRatios[savedIndex];
                    // If "Free" mode (width = 0), treat as unconstrained in runtime but keep index
                    if (_selectedAspectRatio.Width == 0) _selectedAspectRatio = null;
                    _lastAspectRatioIndex = savedIndex;
                }
                else
                {
                    // default to first if saved index invalid
                    cmbAspectRatio.SelectedIndex = 0;
                    _lastAspectRatioIndex = 0;
                    _selectedAspectRatio = AspectRatio.CommonRatios[0];
                    if (_selectedAspectRatio.Width == 0) _selectedAspectRatio = null;
                }
            }

            UpdateQualityControls();

            // Initialize encoding settings
            if (cmbFrameRate != null)
            {
                cmbFrameRate.SelectedIndex = 1; // Default to 25 fps (PAL)
            }
            if (cmbEncodingPreset != null)
            {
                cmbEncodingPreset.SelectedIndex = 2; // Default to Medium
            }
        }

        #endregion

        #region Settings Management

        /// <summary>
        /// Handle Load Session button click.
        /// Opens file browser to select session.json file.
        /// </summary>
        private void btnLoadSession_Click(object? sender, EventArgs e)
        {
            if (IsCapturing)
            {
                MessageBox.Show(
                    "Cannot load session while capturing. Stop capture first.",
                    "Cannot Load Session",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Warn if active session exists
            if (_activeSession != null && _activeSession.FramesCaptured > 0)
            {
                var result = MessageBox.Show(
                    $"Current session '{_activeSession.Name}' has {_activeSession.FramesCaptured} frames.\n\n" +
                    "Loading another session will close the current one.\n\n" +
                    "Continue?",
                    "Close Current Session?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No)
                    return;

                if (_activeSessionFolder != null)
                    SessionManager.MarkSessionInactive(_activeSessionFolder);
            }

            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Load Session";
                ofd.Filter = "Session Files (session.json)|session.json|All Files (*.*)|*.*";
                ofd.CheckFileExists = true;

                // Start in captures folder if available
                if (!string.IsNullOrEmpty(settings.SaveFolder))
                {
                    var capturesRoot = Path.Combine(settings.SaveFolder, "captures");
                    if (Directory.Exists(capturesRoot))
                        ofd.InitialDirectory = capturesRoot;
                }

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    string sessionFile = ofd.FileName;
                    string? sessionFolder = Path.GetDirectoryName(sessionFile);

                    if (string.IsNullOrEmpty(sessionFolder))
                    {
                        MessageBox.Show(
                            "Invalid session file location.",
                            "Load Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }

                    LoadSessionFromPath(sessionFolder);
                }
            }
        }

        /// <summary>
        /// Load a session from a path.
        /// </summary>
        private void LoadSessionFromPath(string? sessionPath)
        {
            if (string.IsNullOrEmpty(sessionPath))
            {
                MessageBox.Show(
                    "Invalid session path.",
                    "Load Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var session = SessionManager.LoadSession(sessionPath);
            if (session == null)
            {
                MessageBox.Show(
                    "Failed to load session.",
                    "Load Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            // Mark all other sessions as inactive first
            if (!string.IsNullOrEmpty(settings.SaveFolder))
            {
                var capturesRoot = Path.Combine(settings.SaveFolder, "captures");
                SessionManager.MarkAllSessionsInactive(capturesRoot);
            }

            // Mark this session as active
            session.Active = true;
            SessionManager.SaveSession(sessionPath, session);

            // Load into UI
            _activeSessionFolder = sessionPath;
            _activeSession = session;
            SetCaptureRegionFromNullable(session.CaptureRegion);

            if (numInterval != null) numInterval.Value = session.IntervalSeconds;
            if (cmbFormat != null) cmbFormat.SelectedItem = session.ImageFormat ?? "JPEG";
            if (numQuality != null && session.ImageFormat == "JPEG")
                numQuality.Value = session.JpegQuality;

            string ratioInfo = captureRegion.IsEmpty ? "N/A" : AspectRatio.CalculateRatioString(captureRegion.Width, captureRegion.Height);
            if (lblRegion != null)
            {
                if (captureRegion.IsEmpty)
                    lblRegion.Text = "No region selected";
                else
                    lblRegion.Text = $"Region: {captureRegion.Width}×{captureRegion.Height} ({ratioInfo}) at ({captureRegion.X},{captureRegion.Y})";
            }

            SaveSettings();
            UpdateStatusDisplay();
            UpdateCaptureTimer();
            UpdateSessionInfoPanel();

            string sessionMessage = $"Session '{session.Name}' loaded!\n\n" +
                $"Frames: {session.FramesCaptured}\n";

            if (session.CaptureRegion.HasValue)
            {
                var r = session.CaptureRegion.Value;
                sessionMessage += $"Region: {r.Width}×{r.Height} at ({r.X},{r.Y})\n";
                sessionMessage += $"Location: {Path.GetFileName(sessionPath)}\n\n";
                sessionMessage += "✅ Ready to continue capturing.";
            }
            else
            {
                sessionMessage += "Region: Not set\n";
                sessionMessage += $"Location: {Path.GetFileName(sessionPath)}\n\n";
                sessionMessage += "⚠️ Before capturing:\n";
                sessionMessage += "1. Click 'Select' or 'Full Screen' to choose region\n";
                sessionMessage += "2. Then press 'Start Capture' to begin";
            }

            MessageBox.Show(
                sessionMessage,
                "Session Loaded",
                MessageBoxButtons.OK,
                session.CaptureRegion.HasValue ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }


        /// <summary>
        /// Handle New Session button click.
        /// Creates session WITHOUT requiring region first.
        /// </summary>
        private void btnNewSession_Click(object? sender, EventArgs e)
        {
            // Warn if active session exists with frames
            if (_activeSession != null && _activeSession.FramesCaptured > 0)
            {
                string regionInfo;
                if (_activeSession.CaptureRegion.HasValue)
                {
                    var r = _activeSession.CaptureRegion.Value;
                    regionInfo = $"{r.Width}×{r.Height}";
                }
                else
                {
                    regionInfo = "Not set (select before capture)";
                }

                var result = MessageBox.Show(
                    $"Session '{_activeSession.Name}' is currently loaded.\n\n" +
                    $"Status: {_activeSession.FramesCaptured} frames captured\n" +
                    $"Region: {regionInfo}\n\n" +
                    "Changing to full screen requires closing this session.\n\n" +
                    "• YES: Close current session and select full screen\n" +
                    "• NO: Keep current session (cancel)",
                    "Switch to Full Screen?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No)
                    return;

                    if (_activeSessionFolder != null)
                    SessionManager.MarkSessionInactive(_activeSessionFolder);
            }

            // Validate ONLY output folder is required
            if (string.IsNullOrEmpty(settings.SaveFolder))
            {
                MessageBox.Show(
                    "Please choose an output folder first.\n\n" +
                    "Click 'Choose Folder' to select where sessions will be saved.",
                    "Output Folder Required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Region is NOT required for session creation!

            // Show session name dialog
            using (var dialog = new SessionNameDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string sessionName = dialog.SessionName;

                    try
                    {
                        // Mark old session as inactive first
                        if (_activeSessionFolder != null)
                            SessionManager.MarkSessionInactive(_activeSessionFolder);

                        // Create session WITHOUT region (optional parameters)
                        var capturesRoot = Path.Combine(settings.SaveFolder, "captures");
                        int interval = (int)(numInterval?.Value ?? 5);

                        _activeSessionFolder = SessionManager.CreateNamedSession(
                            capturesRoot,
                            sessionName,
                            interval);  // Region NOT required!

                        _activeSession = SessionManager.LoadSession(_activeSessionFolder);

                        Logger.Log("Session", $"New session created: {_activeSession?.Name}");
                        Logger.LogState("Session", "CaptureRegion", _activeSession?.CaptureRegion);
                        Logger.LogState("Session", "FramesCaptured", _activeSession?.FramesCaptured);

                        // Update UI
                        if (txtSessionName != null)
                            txtSessionName.Text = _activeSession?.Name ?? sessionName;

                        UpdateStatusDisplay();
                        UpdateCaptureTimer();
                        UpdateSessionInfoPanel();

                        // Show success message
                        string message = $"✅ New session '{_activeSession?.Name ?? sessionName}' created!\n\n";

                        if (_activeSession?.Name != sessionName && _activeSession?.Name?.Contains("(") == true)
                        {
                            message += "⚠️ A session with this name already exists.\n";
                            message += "The new session was renamed to avoid conflicts.\n\n";
                        }

                        message += "Session folder: " + Path.GetFileName(_activeSessionFolder) + "\n\n";

                        // Updated instruction
                        if (captureRegion.IsEmpty)
                        {
                            message += "📋 Next steps:\n";
                            message += "1. Click 'Select' or 'Full Screen' to choose capture region\n";
                            message += "2. Press 'Start Capture' to begin recording";
                        }
                        else
                        {
                            message += "👉 Region already selected - Press 'Start Capture' to begin.";
                        }

                        MessageBox.Show(
                            message,
                            "Session Created",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Failed to create session:\n\n{ex.Message}",
                            "Error Creating Session",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
            }
        }

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

            // Only save aspect-ratio preference if the user can actively set it (not while we are temporarily in full-screen mode)
            if (cmbAspectRatio != null && cmbAspectRatio.Enabled)
            {
                settings.AspectRatioIndex = cmbAspectRatio.SelectedIndex;
            }

            SettingsManager.Save(settings);
        }

        /// <summary>
        /// Validate that a region has even dimensions (required for video encoding).
        /// </summary>
        private bool IsValidRegion(Rectangle r) => ValidationHelper.IsValidRegion(r);

        #endregion

        #region Region Overlay

        /// <summary>
        /// Toggle region overlay visibility.
        /// </summary>
        private void ToggleRegionOverlay()
        {
            if (_isOverlayVisible)
            {
                HideRegionOverlay();
            }
            else
            {
                ShowRegionOverlay();
            }
        }

        /// <summary>
        /// Show the region overlay.
        /// Creates a fresh overlay instance each time.
        /// </summary>
        private void ShowRegionOverlay()
        {
            // Dispose old overlay if exists
            if (_regionOverlay != null)
            {
                try
                {
                    _regionOverlay.Hide();
                    _regionOverlay.Dispose();
                }
                catch { }
                finally
                {
                    _regionOverlay = null;
                }
            }

            // Check if region is set
            if (captureRegion.IsEmpty || captureRegion.Width == 0 || captureRegion.Height == 0)
            {
                MessageBox.Show(
                    "No region selected.\n\n" +
                    "Select a region first:\n" +
                    "• Click 'Select' for manual selection\n" +
                    "• Click 'Full Screen' for entire monitor",
                    "No Region Selected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // Create fresh overlay instance
            _regionOverlay = new RegionOverlay();

            // Position overlay to cover only the capture region (plus border)
            int borderSize = 50; // Extra space for info box and brackets
            _regionOverlay.Bounds = new Rectangle(
                captureRegion.X - borderSize,
                captureRegion.Y - borderSize,
                captureRegion.Width + (borderSize * 2),
                captureRegion.Height + (borderSize * 2)
            );

            _regionOverlay.CaptureRegion = captureRegion;
            _regionOverlay.IsActiveCapture = IsCapturing;
            _regionOverlay.Show();

            _isOverlayVisible = true;
            UpdateRegionOverlayButton();
        }

        /// <summary>
        /// Hide the region overlay and dispose it.
        /// </summary>
        private void HideRegionOverlay()
        {
            if (_regionOverlay == null)
            {
                _isOverlayVisible = false;
                UpdateRegionOverlayButton();
                return;
            }

            _isOverlayVisible = false;
            UpdateRegionOverlayButton();

            try
            {
                _regionOverlay.Hide();
                _regionOverlay.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disposing overlay: {ex.Message}");
            }
            finally
            {
                _regionOverlay = null;
            }
        }

        /// <summary>
        /// Update region overlay with current settings.
        /// Recreates overlay if settings changed.
        /// </summary>
        private void UpdateRegionOverlay()
        {
            // If overlay is visible, recreate it with new settings
            if (_isOverlayVisible && !captureRegion.IsEmpty)
            {
                // Hide current
                if (_regionOverlay != null)
                {
                    try
                    {
                        _regionOverlay.Hide();
                        _regionOverlay.Dispose();
                    }
                    catch { }
                    _regionOverlay = null;
                }

                // Create new with updated settings
                _regionOverlay = new RegionOverlay();

                int borderSize = 50;
                _regionOverlay.Bounds = new Rectangle(
                    captureRegion.X - borderSize,
                    captureRegion.Y - borderSize,
                    captureRegion.Width + (borderSize * 2),
                    captureRegion.Height + (borderSize * 2)
                );

                _regionOverlay.CaptureRegion = captureRegion;
                _regionOverlay.IsActiveCapture = IsCapturing;
                _regionOverlay.Show();
            }
        }

        /// <summary>
        /// Update the Show Region button appearance based on overlay state.
        /// </summary>
        private void UpdateRegionOverlayButton()
        {
            if (btnShowRegion == null) return;

            if (_isOverlayVisible)
            {
                btnShowRegion.Text = "👁 Hide";
                btnShowRegion.ForeColor = Color.LimeGreen;
                btnShowRegion.FlatAppearance.BorderSize = 2;
            }
            else
            {
                btnShowRegion.Text = "👁 Show";
                btnShowRegion.ForeColor = Color.MediumPurple;
                btnShowRegion.FlatAppearance.BorderSize = 1;
            }
        }

        /// <summary>
        /// Handle Show Region button click.
        /// </summary>
        private void btnShowRegion_Click(object? sender, EventArgs e)
        {
            ToggleRegionOverlay();
        }

        #endregion

        #region UI Event Handlers

        /// <summary>
        /// Handle full screen button click.
        /// Shows context menu with all available monitors.
        /// </summary>
        private void btnFullScreen_Click(object? sender, EventArgs e)
        {
            if (IsCapturing)
            {
                MessageBox.Show(
                    "Cannot change region while capturing.\n\n" +
                    "Please stop the current capture session first.",
                    "Stop Capture First",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (_activeSession != null && _activeSession.FramesCaptured > 0)
            {
                var result = MessageBox.Show(
                    $"Session '{_activeSession.Name}' is currently loaded.\n\n" +
                    $"Status: {_activeSession.FramesCaptured} frames captured\n" +
                    $"Region: {_activeSession.CaptureRegion?.Width ?? 0}×{_activeSession.CaptureRegion?.Height ?? 0}\n\n" +
                    "Changing to full screen requires closing this session.\n\n" +
                    "• YES: Close current session and select full screen\n" +
                    "• NO: Keep current session (cancel)",
                    "Close Current Session?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No) return;

                // User confirmed - close the session
                if (_activeSessionFolder != null) SessionManager.MarkSessionInactive(_activeSessionFolder);
                _activeSession = null;
                _activeSessionFolder = null;
                if (txtSessionName != null) txtSessionName.Text = "No session loaded";
                UpdateStatusDisplay();
                UpdateSessionInfoPanel();
            }

            // Create context menu with all screens
            var contextMenu = new ContextMenuStrip();
            contextMenu.BackColor = Color.FromArgb(45, 45, 45);
            contextMenu.ForeColor = Color.LightGray;

            var screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                var bounds = screen.Bounds;

                // Create descriptive label
                string label = $"Monitor {i + 1}: {bounds.Width}×{bounds.Height}";
                if (screen.Primary)
                    label += " (Primary)";

                var menuItem = new ToolStripMenuItem(label);
                menuItem.Tag = screen;
                menuItem.Click += (s, args) =>
                {
                    SelectFullScreenRegion((Screen)menuItem.Tag!);
                };

                contextMenu.Items.Add(menuItem);
            }

            // Show menu below the button
            if (btnFullScreen != null)
            {
                contextMenu.Show(btnFullScreen, new Point(0, btnFullScreen.Height));
            }
        }

        /// <summary>
        /// Select full screen region for a specific monitor.
        /// Ensures even dimensions for video encoding.
        /// </summary>
        private void SelectFullScreenRegion(Screen screen)
        {
            var bounds = screen.Bounds;

            // Ensure even dimensions (required for video encoding)
            int width = bounds.Width;
            int height = bounds.Height;
            if ((width & 1) == 1) width--;
            if ((height & 1) == 1) height--;
            width = Math.Max(2, width);
            height = Math.Max(2, height);

            // Save user's previous aspect-ratio selection so we can restore after region selection
            if (cmbAspectRatio != null)
            {
                _lastAspectRatioIndex = cmbAspectRatio.SelectedIndex >= 0 ? cmbAspectRatio.SelectedIndex : _lastAspectRatioIndex;
            }

            // Use centralized setter to update ALL state locations
            SetCurrentRegion(new Rectangle(bounds.X, bounds.Y, width, height));

            // Indicate full screen monitor in UI
            if (lblFullScreenInfo != null)
            {
                string monitorInfo = screen.Primary ? "Primary Monitor" : "Secondary Monitor";
                lblFullScreenInfo.Text = $"Full screen\n{monitorInfo}";
            }

            // Temporarily set the aspect ratio UI to "Unconstrained" / Free and disable it
            if (cmbAspectRatio != null)
            {
                // Try to find an AspectRatio entry with width == 0 (convention used for 'Free' / unconstrained)
                int freeIndex = -1;
                for (int i = 0; i < AspectRatio.CommonRatios.Length; i++)
                {
                    if (AspectRatio.CommonRatios[i].Width == 0)
                    {
                        freeIndex = i;
                        break;
                    }
                }

                if (freeIndex >= 0 && freeIndex < cmbAspectRatio.Items.Count)
                {
                    cmbAspectRatio.SelectedIndex = freeIndex;
                }
                else
                {
                    // No free mode item found in list — fallback: disable and clear selection
                    cmbAspectRatio.SelectedIndex = -1;
                }

                cmbAspectRatio.Enabled = false;
            }

            // Persist aspect ratio preference
            int preservedAspectIndex = _lastAspectRatioIndex;
            settings.AspectRatioIndex = preservedAspectIndex;
            // Note: SetCurrentRegion() will handle saving settings

            // Update format/quality if session exists
            if (_activeSession != null && _activeSessionFolder != null)
            {
                _activeSession.ImageFormat = cmbFormat?.SelectedItem?.ToString() ?? "JPEG";
                _activeSession.JpegQuality = (int)(numQuality?.Value ?? 90);
                SessionManager.SaveSession(_activeSessionFolder, _activeSession);
            }

            UpdateStatusDisplay();
            UpdateCaptureTimer();
            UpdateRegionOverlay();
            
            // Note: SetCurrentRegion() already called UpdateSessionInfoPanel()
        }

        /// <summary>
        /// Handle region selection button click.
        /// Shows full-screen overlay for region selection.
        /// </summary>
        private void btnSelectRegion_Click(object? sender, EventArgs e)
        {
            Logger.Log("RegionSelect", $"Button clicked - Session: {_activeSession?.Name ?? "None"}, Frames: {_activeSession?.FramesCaptured ?? 0}");

            if (IsCapturing)
            {
                MessageBox.Show(
                    "Cannot change region while capturing. Stop capture first.",
                    "Cannot Change Region",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // ONLY warn if session has frames captured - empty sessions can freely change region
            if (_activeSession != null && _activeSession.FramesCaptured > 0)
            {
                Logger.Log("RegionSelect", $"Warning user - session has {_activeSession.FramesCaptured} frames");
                var result = MessageBox.Show(
                    $"Session '{_activeSession.Name}' has {_activeSession.FramesCaptured} captured frames.\n\n" +
                    "Changing the region requires starting a new session.\n\n" +
                    "Do you want to:\n" +
                    "• YES: Close current session and select new region\n" +
                    "• NO: Keep current session (cancel)",
                    "Session Has Frames",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No)
                {
                    Logger.Log("RegionSelect", "User cancelled - keeping session");
                    return;
                }

                Logger.Log("RegionSelect", "User confirmed - closing session");
                if (_activeSessionFolder != null) SessionManager.MarkSessionInactive(_activeSessionFolder);
                _activeSession = null;
                _activeSessionFolder = null;
            }
            else
            {
                Logger.Log("RegionSelect", "No frames captured - allowing region change");
            }

            // Hide main form temporarily for clean region selection
            Hide();
            Task.Delay(Constants.REGION_SELECTION_DELAY_MS).Wait();

            using (var selector = new RegionSelector(_selectedAspectRatio))
            {
                if (selector.ShowDialog() == DialogResult.OK)
                {
                    var selectedRegion = selector.SelectedRegion;

                    // Validate region
                    if (!IsValidRegion(selectedRegion))
                    {
                        MessageBox.Show(
                            $"Selected region has invalid dimensions: {selectedRegion.Width}×{selectedRegion.Height}\n\n" +
                            "This should not happen - please report this bug.",
                            "Invalid Region",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        ClearCurrentRegion();
                    }
                    else
                    {
                        // Use centralized setter to update ALL state locations
                        SetCurrentRegion(selectedRegion);

                        // Clear full screen info since this is manual selection
                        if (lblFullScreenInfo != null)
                            lblFullScreenInfo.Text = "";

                        if (cmbAspectRatio != null)
                        {
                            cmbAspectRatio.Enabled = true;

                            // Suppress save during programmatic restoration
                            _suppressAspectRatioSave = true;

                            // Restore previous user selection if valid
                            if (_lastAspectRatioIndex >= 0 && _lastAspectRatioIndex < cmbAspectRatio.Items.Count)
                            {
                                cmbAspectRatio.SelectedIndex = _lastAspectRatioIndex;
                            }
                            else if (cmbAspectRatio.Items.Count > 0)
                            {
                                cmbAspectRatio.SelectedIndex = 0;
                                _lastAspectRatioIndex = 0;
                            }

                            // Re-enable saves now that restoration is complete
                            _suppressAspectRatioSave = false;

                            // Update the runtime aspect ratio object
                            if (_lastAspectRatioIndex >= 0 && _lastAspectRatioIndex < AspectRatio.CommonRatios.Length)
                            {
                                _selectedAspectRatio = AspectRatio.CommonRatios[_lastAspectRatioIndex];
                                if (_selectedAspectRatio?.Width == 0)
                                    _selectedAspectRatio = null;
                            }
                        }
                    }
                }
            }

            // Update format/quality if session exists
            if (_activeSession != null && _activeSessionFolder != null)
            {
                _activeSession.ImageFormat = cmbFormat?.SelectedItem?.ToString() ?? "JPEG";
                _activeSession.JpegQuality = (int)(numQuality?.Value ?? 90);
                SessionManager.SaveSession(_activeSessionFolder, _activeSession);
            }

            Show();
            UpdateStatusDisplay();
            UpdateCaptureTimer();
            UpdateRegionOverlay();
            
            // Note: SetCurrentRegion() already called UpdateSessionInfoPanel()
        }

        /// <summary>
        /// Handle aspect ratio dropdown change.
        /// Only saves when user manually changes it (not during programmatic restoration).
        /// </summary>
        private void cmbAspectRatio_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (cmbAspectRatio == null || _suppressAspectRatioSave) return;

            int index = cmbAspectRatio.SelectedIndex;
            if (index >= 0 && index < AspectRatio.CommonRatios.Length)
            {
                _selectedAspectRatio = AspectRatio.CommonRatios[index];

                // If "Free" mode (width=0), clear the lock
                if (_selectedAspectRatio.Width == 0)
                    _selectedAspectRatio = null;

                // Track this as the user's preference
                _lastAspectRatioIndex = index;
                SaveSettings();
            }
        }

        /// <summary>
        /// Handle frame rate dropdown change.
        /// Shows/hides custom frame rate input.
        /// </summary>
        private void cmbFrameRate_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (cmbFrameRate == null || numCustomFrameRate == null) return;

            int index = cmbFrameRate.SelectedIndex;

            // Show custom input if "Custom..." selected (last item)
            bool isCustom = index == cmbFrameRate.Items.Count - 1;
            numCustomFrameRate.Visible = isCustom;

            if (!isCustom)
            {
                // Parse FPS from preset selection
                switch (index)
                {
                    case 0: _targetFrameRate = 24; break; // Film
                    case 1: _targetFrameRate = 25; break; // PAL
                    case 2: _targetFrameRate = 30; break; // NTSC
                    case 3: _targetFrameRate = 60; break; // Smooth
                    default: _targetFrameRate = 25; break;
                }
            }
            else
            {
                // Use custom value
                _targetFrameRate = (int)numCustomFrameRate.Value;
            }

            // Update estimates with new frame rate
            UpdateCaptureTimer();
        }

        /// <summary>
        /// Handle custom frame rate value change.
        /// </summary>
        private void numCustomFrameRate_ValueChanged(object? sender, EventArgs e)
        {
            if (numCustomFrameRate != null && numCustomFrameRate.Visible)
            {
                _targetFrameRate = (int)numCustomFrameRate.Value;
                UpdateCaptureTimer();
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
            UpdateCaptureTimer();

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
            // Only validate if there's an active session with frames
            if (_activeSession != null && _activeSession.FramesCaptured > 0 && !IsCapturing)
            {
                var newFormat = cmbFormat?.SelectedItem?.ToString();
                if (newFormat != _activeSession.ImageFormat)
                {
                    MessageBox.Show(
                        $"Active session '{_activeSession.Name}' has {_activeSession.FramesCaptured} frames using {_activeSession.ImageFormat} format.\n\n" +
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
            if (!ValidateStartCapturePrerequisites())
                return;

            if (!ValidateActiveSession(sender, e))
                return;

            StartCaptureSession();
        }

        /// <summary>
        /// Validates all prerequisites for starting capture.
        /// </summary>
        private bool ValidateStartCapturePrerequisites()
        {
            // Validate region selection
            if (!ValidationHelper.IsRegionSelected(captureRegion))
            {
                UIHelper.ShowWarning(Constants.MSG_NO_REGION_SELECTED, "Missing Region");
                return false;
            }

            // Validate region dimensions
            if (!ValidationHelper.IsValidRegion(captureRegion))
            {
                UIHelper.ShowError(
                    string.Format(Constants.MSG_INVALID_REGION, captureRegion.Width, captureRegion.Height),
                    "Invalid Region");
                return false;
            }

            // Validate save folder
            if (!ValidationHelper.IsSaveFolderConfigured(settings.SaveFolder))
            {
                UIHelper.ShowWarning(Constants.MSG_NO_SAVE_FOLDER, "Missing Folder");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates active session and handles settings mismatches.
        /// </summary>
        private bool ValidateActiveSession(object? sender, EventArgs e)
        {
            if (!ValidationHelper.HasActiveSession(_activeSession))
            {
                UIHelper.ShowInfo(Constants.MSG_NO_SESSION, "Create Session First");
                return false;
            }

            // Get current settings
            int intervalSec = (int)(numInterval?.Value ?? Constants.DEFAULT_INTERVAL_SECONDS);
            var format = cmbFormat?.SelectedItem?.ToString() ?? Constants.DEFAULT_IMAGE_FORMAT;
            var quality = (int)(numQuality?.Value ?? Constants.DEFAULT_JPEG_QUALITY);

            // Validate session settings match
            if (!SessionManager.ValidateSessionSettings(_activeSession!, captureRegion, format, quality))
            {
                return HandleSessionSettingsMismatch(sender, e, intervalSec, format, quality);
            }

            return true;
        }

        /// <summary>
        /// Handles session settings mismatch by showing dialog and creating new session if needed.
        /// </summary>
        private bool HandleSessionSettingsMismatch(object? sender, EventArgs e, int intervalSec, string format, int quality)
        {
            string mismatchMessage = ValidationHelper.BuildSettingsMismatchMessage(_activeSession!, captureRegion, format, quality);
            
            var result = UIHelper.ShowQuestion(mismatchMessage, "Settings Mismatch");

            if (result == DialogResult.Yes)
            {
                SessionManager.MarkSessionInactive(_activeSessionFolder!);
                btnNewSession_Click(sender, e);
                return ValidationHelper.HasActiveSession(_activeSession);
            }

            return false;
        }

        /// <summary>
        /// Starts the capture session with current settings.
        /// </summary>
        private void StartCaptureSession()
        {
            // Validate state consistency before starting
            if (!ValidateRegionStateConsistency())
            {
                Logger.Log("Capture", "WARNING: State desync detected before capture start - attempting recovery");
                // Try to recover by syncing from session
                if (_activeSession?.CaptureRegion.HasValue == true)
                    SetCurrentRegion(_activeSession.CaptureRegion.Value);
            }

            // Get current settings
            int intervalSec = (int)(numInterval?.Value ?? Constants.DEFAULT_INTERVAL_SECONDS);
            var format = cmbFormat?.SelectedItem?.ToString() ?? Constants.DEFAULT_IMAGE_FORMAT;
            var quality = (int)(numQuality?.Value ?? Constants.DEFAULT_JPEG_QUALITY);

            // Update settings and save
            settings.IntervalSeconds = intervalSec;
            settings.Format = format;
            settings.JpegQuality = quality;
            SaveSettings();

            // Lock UI and start capture timer
            LockCaptureUI(true);
            _consecutiveCaptureErrors = 0;

            var intervalMs = intervalSec * 1000;
            _captureTimer = new System.Threading.Timer(CaptureFrame, null, 0, intervalMs);
            
            // Update UI
            UpdateStatusDisplay();
            UpdateCaptureTimer();
            UpdateSessionInfoPanel();
            UpdateRegionOverlay();
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
            UpdateCaptureTimer();
            UpdateSessionInfoPanel();
            UpdateRegionOverlay();
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
            if (btnFullScreen != null) btnFullScreen.Enabled = !locked;
            if (btnChooseFolder != null) btnChooseFolder.Enabled = !locked;
            if (btnBrowseFfmpeg != null) btnBrowseFfmpeg.Enabled = !locked;
            if (btnStart != null) btnStart.Enabled = !locked;
            if (btnStop != null) btnStop.Enabled = locked;
            if (btnEncode != null) btnEncode.Enabled = !locked;
        }


        /// <summary>
        /// Capture a single frame (called by timer).
        /// Tracks actual elapsed time and updates real-time counter.
        /// Enhanced with comprehensive error handling and safety checks.
        /// </summary>
        private void CaptureFrame(object? state)
        {
            if (_activeSession == null || _captureTimer == null) return;

            try
            {
                if (!PerformSafetyChecks())
                    return;

                UpdateCaptureTiming();
                SaveCapturedFrame();
                UpdateSessionAfterCapture();
                ResetErrorCounter();
                UpdateUIAfterCapture();
            }
            catch (IOException ioEx)
            {
                HandleCaptureError(ioEx, "I/O Error", "I/O error during capture");
            }
            catch (UnauthorizedAccessException uaEx)
            {
                HandlePermissionError(uaEx);
            }
            catch (OutOfMemoryException memEx)
            {
                HandleMemoryError(memEx);
            }
            catch (Exception ex)
            {
                HandleUnexpectedError(ex);
            }
        }

        /// <summary>
        /// Performs safety checks before capture.
        /// </summary>
        private bool PerformSafetyChecks()
        {
            // Check disk space periodically
            if (_activeSession!.FramesCaptured % Constants.DISK_SPACE_CHECK_INTERVAL == 0 && 
                !string.IsNullOrEmpty(_activeSessionFolder))
            {
                if (!ValidationHelper.CheckDiskSpace(_activeSessionFolder))
                {
                    UIHelper.SafeBeginInvoke(this, () =>
                    {
                        StopCapture();
                        UIHelper.ShowWarning(
                            "⚠️ Disk space is running low!\n\n" +
                            "Capture has been stopped automatically to prevent data loss.\n\n" +
                            "Please free up disk space before continuing.",
                            "Low Disk Space");
                    });
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Updates capture timing information.
        /// </summary>
        private void UpdateCaptureTiming()
        {
            DateTime now = DateTime.UtcNow;

            if (!_activeSession!.LastCaptureTime.HasValue)
            {
                _activeSession.LastCaptureTime = now;
            }
            else
            {
                double elapsedSeconds = (now - _activeSession.LastCaptureTime.Value).TotalSeconds;
                _activeSession.TotalCaptureSeconds += elapsedSeconds;
                _activeSession.LastCaptureTime = now;
            }
        }

        /// <summary>
        /// Saves the captured frame to disk.
        /// </summary>
        private void SaveCapturedFrame()
        {
            long nextFrameNumber = _activeSession!.FramesCaptured + 1;
            string frameNumber = $"{nextFrameNumber:D5}";
            string framesFolder = SessionManager.GetFramesFolder(_activeSessionFolder!);
            string fileName = Path.Combine(framesFolder, $"{frameNumber}.jpg");

            using (var bmp = CaptureScreen())
            {
                if (bmp == null || bmp.Width == 0 || bmp.Height == 0)
                {
                    throw new InvalidOperationException("Captured bitmap is invalid");
                }

                bmp.Save(fileName, ImageFormat.Jpeg);
            }
        }

        /// <summary>
        /// Updates session information after successful capture.
        /// </summary>
        private void UpdateSessionAfterCapture()
        {
            SessionManager.SaveSession(_activeSessionFolder!, _activeSession!);
            SessionManager.IncrementFrameCount(_activeSessionFolder!);
            _activeSession = SessionManager.LoadSession(_activeSessionFolder!);
        }

        /// <summary>
        /// Resets the error counter after successful capture.
        /// </summary>
        private void ResetErrorCounter()
        {
            _consecutiveCaptureErrors = 0;
        }

        /// <summary>
        /// Updates UI after successful capture.
        /// </summary>
        private void UpdateUIAfterCapture()
        {
            UIHelper.SafeBeginInvoke(this, () =>
            {
                UpdateStatusDisplay();
                UpdateCaptureTimer();
                UpdateSessionInfoPanel();
                UpdateRegionOverlay();
            });
        }

        /// <summary>
        /// Handles I/O errors during capture.
        /// </summary>
        private void HandleCaptureError(Exception ex, string errorType, string debugMessage)
        {
            Debug.WriteLine($"{debugMessage}: {ex.Message}");
            _consecutiveCaptureErrors++;

            UIHelper.SafeBeginInvoke(this, () =>
            {
                if (_consecutiveCaptureErrors >= Constants.MAX_CONSECUTIVE_ERRORS)
                {
                    StopCapture();
                    UIHelper.ShowError(
                        $"❌ Capture stopped after {Constants.MAX_CONSECUTIVE_ERRORS} consecutive errors.\n\n" +
                        $"Last error: {ex.Message}\n\n" +
                        "Possible causes:\n" +
                        "• Disk full or nearly full\n" +
                        "• File system errors\n" +
                        "• Antivirus blocking file access\n" +
                        "• Network drive disconnected",
                        "Capture Error");
                }
                else
                {
                    UIHelper.SafeUpdateLabel(lblStatus, $"⚠️ {errorType} ({_consecutiveCaptureErrors}/{Constants.MAX_CONSECUTIVE_ERRORS})");
                }
            });
        }

        /// <summary>
        /// Handles permission errors during capture.
        /// </summary>
        private void HandlePermissionError(UnauthorizedAccessException uaEx)
        {
            Debug.WriteLine($"Permission error: {uaEx.Message}");

            UIHelper.SafeBeginInvoke(this, () =>
            {
                StopCapture();
                UIHelper.ShowError(
                    "🚫 Permission denied during capture.\n\n" +
                    $"Cannot write to: {_activeSessionFolder}\n\n" +
                    "Solutions:\n" +
                    "• Run as administrator\n" +
                    "• Choose a different output folder\n" +
                    "• Check folder permissions",
                    "Permission Error");
            });
        }

        /// <summary>
        /// Handles memory errors during capture.
        /// </summary>
        private void HandleMemoryError(OutOfMemoryException memEx)
        {
            Debug.WriteLine($"Out of memory: {memEx.Message}");

            UIHelper.SafeBeginInvoke(this, () =>
            {
                StopCapture();
                UIHelper.ShowError(
                    "💥 Out of memory!\n\n" +
                    "The system has run out of available memory.\n\n" +
                    "Solutions:\n" +
                    "• Close other applications\n" +
                    "• Reduce capture resolution\n" +
                    "• Restart the application",
                    "Memory Error");
            });
        }

        /// <summary>
        /// Handles unexpected errors during capture.
        /// </summary>
        private void HandleUnexpectedError(Exception ex)
        {
            Debug.WriteLine($"Unexpected capture error: {ex.GetType().Name} - {ex.Message}");
            _consecutiveCaptureErrors++;

            UIHelper.SafeBeginInvoke(this, () =>
            {
                if (_consecutiveCaptureErrors >= Constants.MAX_CONSECUTIVE_ERRORS)
                {
                    StopCapture();
                    UIHelper.ShowError(
                        $"❌ Unexpected error during capture.\n\n" +
                        $"Type: {ex.GetType().Name}\n" +
                        $"Message: {ex.Message}\n\n" +
                        "Capture has been stopped for safety.",
                        "Capture Error");
                }
                else
                {
                    UIHelper.SafeUpdateLabel(lblStatus, $"⚠️ Error ({_consecutiveCaptureErrors}/{Constants.MAX_CONSECUTIVE_ERRORS}): {ex.Message}");
                }
            });
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
        /// Update the Session Info Panel to show current session settings.
        /// Provides at-a-glance visibility of locked settings during capture.
        /// </summary>
        private void UpdateSessionInfoPanel()
        {
            if (lblSessionInfoRegion == null || lblSessionInfoFormat == null ||
                lblSessionInfoQuality == null || lblSessionInfoInterval == null)
                return;

            // Validate state consistency
            ValidateRegionStateConsistency();

            Logger.Log("UI", $"UpdateSessionInfoPanel - Session: {_activeSession?.Name}, CaptureRegion: {_activeSession?.CaptureRegion}");

            if (_activeSession != null)
            {
                // Show session settings
                var region = _activeSession.CaptureRegion;
                Logger.LogState("UI", "SessionInfoPanel.region", region);
                string ratioInfo = region.HasValue ? AspectRatio.CalculateRatioString(region.Value.Width, region.Value.Height) : "N/A";
                lblSessionInfoRegion.Text = region.HasValue ? $"📍 Region: {region.Value.Width}×{region.Value.Height} ({ratioInfo})" : "📍 Region: Not set";
                lblSessionInfoRegion.ForeColor = Color.FromArgb(100, 200, 255); // Light blue

                lblSessionInfoFormat.Text = $"🗄 Format: {_activeSession.ImageFormat}";
                lblSessionInfoFormat.ForeColor = Color.FromArgb(100, 200, 255);

                if (_activeSession.ImageFormat == "JPEG")
                {
                    lblSessionInfoQuality.Text = $"⭐ Quality: {_activeSession.JpegQuality}";
                    lblSessionInfoQuality.ForeColor = Color.FromArgb(100, 200, 255);
                    lblSessionInfoQuality.Visible = true;
                }
                else
                {
                    lblSessionInfoQuality.Text = "⭐ Quality: N/A (lossless)";
                    lblSessionInfoQuality.ForeColor = Color.FromArgb(150, 150, 150);
                    lblSessionInfoQuality.Visible = true;
                }

                lblSessionInfoInterval.Text = $"⏱ Interval: {_activeSession.IntervalSeconds}s";
                lblSessionInfoInterval.ForeColor = Color.FromArgb(100, 200, 255);

                // Change panel title based on state
                if (grpSessionInfo != null)
                {
                    if (IsCapturing)
                        grpSessionInfo.Text = "🔒 Session Settings (Locked - Capturing)";
                    else if (_activeSession.FramesCaptured > 0)
                        grpSessionInfo.Text = "📋 Session Settings (Active)";
                    else
                        grpSessionInfo.Text = "🆕 Session Settings (New)";
                }
            }
            else
            {
                // No session - show placeholders
                lblSessionInfoRegion.Text = "📍 Region: Not set";
                lblSessionInfoFormat.Text = "🗄 Format: Not set";
                lblSessionInfoQuality.Text = "⭐ Quality: Not set";
                lblSessionInfoInterval.Text = "⏱ Interval: Not set";

                lblSessionInfoRegion.ForeColor = Color.FromArgb(100, 100, 100);
                lblSessionInfoFormat.ForeColor = Color.FromArgb(100, 100, 100);
                lblSessionInfoQuality.ForeColor = Color.FromArgb(100, 100, 100);
                lblSessionInfoInterval.ForeColor = Color.FromArgb(100, 100, 100);

                if (grpSessionInfo != null)
                    grpSessionInfo.Text = "📋 Session Settings";
            }
        }
        private void UpdateStatusDisplay()
        {
            if (lblStatus == null) return;

            if (IsCapturing)
            {
                // Currently capturing - RED background
                string statusText = string.Format(Constants.STATUS_CAPTURING, _activeSession?.Name ?? "Session", _activeSession?.FramesCaptured ?? 0);
                UIHelper.SafeSetText(lblStatus, statusText);
                UIHelper.SafeSetBackColor(lblStatus, Color.FromArgb(220, 50, 50)); // Red
                UIHelper.SafeSetColor(lblStatus, Color.White);
            }
            else if (_activeSession != null)
            {
                // Session loaded but not capturing - GREEN background
                if (_activeSession.FramesCaptured > 0)
                {
                    string statusText = string.Format(Constants.STATUS_SESSION_LOADED, _activeSession.Name, _activeSession.FramesCaptured);
                    UIHelper.SafeSetText(lblStatus, statusText);
                    UIHelper.SafeSetBackColor(lblStatus, Color.FromArgb(60, 180, 75)); // Green
                }
                else
                {
                    string statusText = string.Format(Constants.STATUS_NEW_SESSION, _activeSession.Name);
                    UIHelper.SafeSetText(lblStatus, statusText);
                    UIHelper.SafeSetBackColor(lblStatus, Color.FromArgb(70, 160, 220)); // Blue
                }
                UIHelper.SafeSetColor(lblStatus, Color.White);
            }
            else
            {
                // No session - GRAY background
                UIHelper.SafeSetText(lblStatus, Constants.STATUS_NO_SESSION);
                UIHelper.SafeSetBackColor(lblStatus, Color.FromArgb(120, 120, 120)); // Gray
                UIHelper.SafeSetColor(lblStatus, Color.White);
            }
        }

        /*
         * STUBBED OUT - Replaced by UpdateCaptureTimer for real-time updates
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
        */

        /// <summary>
        /// Format elapsed time with adaptive precision (MM:SS, HH:MM:SS, Dd HH:MM:SS).
        /// </summary>
        private string FormatElapsedTime(double totalSeconds)
        {
            if (totalSeconds < 1)
                return "00:00";

            TimeSpan elapsed = TimeSpan.FromSeconds(totalSeconds);

            if (totalSeconds < 3600)
            {
                // Under 1 hour: MM:SS
                return $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
            }
            else if (totalSeconds < 86400)
            {
                // Under 1 day: HH:MM:SS
                return $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            }
            else
            {
                // 1 day or more: Dd HH:MM:SS
                int days = (int)elapsed.TotalDays;
                int hours = elapsed.Hours;
                int minutes = elapsed.Minutes;
                int seconds = elapsed.Seconds;
                return $"{days}d {hours:D2}:{minutes:D2}:{seconds:D2}";
            }
        }

        /// <summary>
        /// Update the real-time capture counter display.
        /// Called by timer every 500ms.
        /// </summary>
        private void UpdateCaptureTimer()
        {
            if (lblEstimate == null) return;

            if (_activeSession == null || _activeSession.FramesCaptured == 0)
            {
                // No active session or no frames yet
                if (!IsCapturing)
                {
                    // Show planning mode
                    int desiredSec = (int)(numDesiredSec?.Value ?? 30);
                    int interval = (int)(numInterval?.Value ?? 5);
                    int neededFrames = desiredSec * _targetFrameRate; // Use dynamic FPS
                    double captureTimeSeconds = neededFrames * interval;

                    string captureTimeDisplay = FormatElapsedTime(captureTimeSeconds);

                    lblEstimate.Text =
                        $"Planning: {neededFrames} frames needed for {desiredSec}s video @ {_targetFrameRate}fps\n" +
                        $"Estimated capture time: {captureTimeDisplay} @ {interval}s intervals";
                }
                else
                {
                    // Just started capturing
                    lblEstimate.Text = "⏱️  00:00  |  0 frames\nStarting capture...";
                }
                return;
            }

            // Calculate current elapsed time
            double elapsedSeconds = _activeSession.TotalCaptureSeconds;

            // If currently capturing, add time since last frame save
            if (IsCapturing && _activeSession.LastCaptureTime.HasValue)
            {
                elapsedSeconds += (DateTime.UtcNow - _activeSession.LastCaptureTime.Value).TotalSeconds;
            }

            string timeDisplay = FormatElapsedTime(elapsedSeconds);
            int frames = (int)_activeSession.FramesCaptured;

            // Calculate resulting video length at selected frame rate
            double videoLength = frames / (double)_targetFrameRate;

            // Also show at standard frame rates for comparison
            double videoAt24fps = frames / 24.0;
            double videoAt30fps = frames / 30.0;
            double videoAt60fps = frames / 60.0;

            // Show warning if interval was changed
            string warningIcon = _activeSession.IntervalChanged ? " ⚠️" : "";

            // Build display with selected FPS highlighted
            string videoLengthDisplay = $"Video @ {_targetFrameRate}fps: {videoLength:F1}s";

            // Add comparison rates if different from selected
            var comparisons = new System.Collections.Generic.List<string>();
            if (_targetFrameRate != 24) comparisons.Add($"{videoAt24fps:F1}s @24");
            if (_targetFrameRate != 30) comparisons.Add($"{videoAt30fps:F1}s @30");
            if (_targetFrameRate != 60) comparisons.Add($"{videoAt60fps:F1}s @60");

            if (comparisons.Count > 0)
                videoLengthDisplay += $" | {string.Join(" | ", comparisons)}";

            // Update display
            if (IsCapturing)
            {
                // Active capture - emphasize timer
                lblEstimate.Text =
                    $"⏱️  {timeDisplay}  |  {frames} frames{warningIcon}\n" +
                    videoLengthDisplay;
            }
            else
            {
                // Capture stopped - show summary
                lblEstimate.Text =
                    $"⏹  {timeDisplay}  |  {frames} frames{warningIcon}\n" +
                    videoLengthDisplay;
            }
        }

        #endregion

        #region FFmpeg & Encoding

        /// <summary>
        /// Open the session folder to access frames, videos, and session data.
        /// </summary>
        private void btnOpenFolder_Click(object? sender, EventArgs e)
        {
            string folderToOpen;

            if (_activeSessionFolder != null && Directory.Exists(_activeSessionFolder))
            {
                // Open the session folder (contains frames/, output/, and session.json)
                folderToOpen = _activeSessionFolder;
                Logger.Log("UI", $"Opening session folder: {folderToOpen}");
            }
            else if (!string.IsNullOrEmpty(settings.SaveFolder) && Directory.Exists(settings.SaveFolder))
            {
                // No active session - open the captures root folder
                var capturesRoot = Path.Combine(settings.SaveFolder, "captures");
                folderToOpen = Directory.Exists(capturesRoot) ? capturesRoot : settings.SaveFolder;
                Logger.Log("UI", $"Opening captures folder: {folderToOpen}");
            }
            else
            {
                MessageBox.Show(
                    "No folder to open.\n\n" +
                    "Please select an output folder or create a session first.",
                    "No Folder Available",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo() { FileName = folderToOpen, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Log("UI", $"Error opening folder: {ex.Message}");
                MessageBox.Show(
                    $"Could not open folder:\n\n{folderToOpen}\n\nError: {ex.Message}",
                    "Error Opening Folder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Download FFmpeg button click.
        /// </summary>
        private void btnDownloadFfmpeg_Click(object? sender, EventArgs e)
        {
            DownloadFfmpeg();
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
            if (!ValidateEncodingPrerequisites(sender, e))
                return;

            await PerformEncoding();
        }

        /// <summary>
        /// Validates all prerequisites for encoding.
        /// </summary>
        private bool ValidateEncodingPrerequisites(object? sender, EventArgs e)
        {
            // Check if already encoding
            if (_isEncoding)
            {
                UIHelper.ShowInfo("Encoding already in progress.\nPlease wait for current encode to complete.", "Encoding In Progress");
                return false;
            }

            // Check FFmpeg configuration
            if (!ValidationHelper.IsFfmpegConfigured(_ffmpegPath))
            {
                return HandleFfmpegNotConfigured(sender, e);
            }

            // Check save folder
            if (!ValidationHelper.IsSaveFolderConfigured(settings.SaveFolder))
            {
                UIHelper.ShowWarning("Please select a save folder first.", "No Save Folder");
                return false;
            }

            // Check for frames
            string sessionFolder = _activeSessionFolder ?? settings.SaveFolder!;
            var frameFiles = SessionManager.GetFrameFiles(sessionFolder);
            if (!ValidationHelper.HasFramesToEncode(frameFiles))
            {
                UIHelper.ShowWarning(Constants.MSG_NO_FRAMES, "No Frames");
                return false;
            }

            // Check region validity
            if (!ValidationHelper.IsValidRegion(captureRegion))
            {
                UIHelper.ShowError("Invalid capture region dimensions.\n\nPlease select a new region before encoding.", "Invalid Region");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Handles FFmpeg not configured scenario.
        /// </summary>
        private bool HandleFfmpegNotConfigured(object? sender, EventArgs e)
        {
            var result = UIHelper.ShowQuestion(Constants.MSG_FFMPEG_NOT_FOUND, "FFmpeg Not Found");

            if (result == DialogResult.Yes)
            {
                btnBrowseFfmpeg_Click(sender, e);
                return false; // Don't proceed with encoding after browse
            }

            return false;
        }

        /// <summary>
        /// Performs the actual encoding process.
        /// </summary>
        private async Task PerformEncoding()
        {
            _isEncoding = true;
            string sessionFolder = _activeSessionFolder ?? settings.SaveFolder!;
            var frameFiles = SessionManager.GetFrameFiles(sessionFolder);

            try
            {
                UpdateEncodingUI(true, frameFiles.Length);
                await ExecuteFfmpegEncoding(sessionFolder, frameFiles);
            }
            catch (FileNotFoundException fnfEx)
            {
                HandleEncodingError("❌ FFmpeg not found", "FFmpeg Not Found", 
                    $"FFmpeg executable not found:\n\n{fnfEx.Message}\n\nPlease use 'Browse...' button to locate ffmpeg.exe");
            }
            catch (IOException ioEx)
            {
                HandleEncodingError("❌ File access error", "File Access Error",
                    $"File access error during encoding:\n\n{ioEx.Message}\n\nThis may be caused by:\n• Another program using the files\n• Insufficient permissions\n• Disk full or read-only");
            }
            catch (UnauthorizedAccessException uaEx)
            {
                HandleEncodingError("❌ Permission denied", "Permission Error",
                    $"Permission denied:\n\n{uaEx.Message}\n\nTry running the application as administrator.");
            }
            catch (Exception ex)
            {
                HandleEncodingError("❌ Encoding error", "Encoding Error",
                    $"Unexpected error during encoding:\n\n{ex.Message}\n\nType: {ex.GetType().Name}");
            }
            finally
            {
                ResetEncodingState();
            }
        }

        /// <summary>
        /// Updates UI during encoding process.
        /// </summary>
        private void UpdateEncodingUI(bool isEncoding, int frameCount = 0)
        {
            if (isEncoding)
            {
                UIHelper.SafeSetEnabled(btnEncode, false);
                UIHelper.SafeSetText(btnEncode, "🎬 Encoding...");
                UIHelper.SafeUpdateLabel(lblStatus, string.Format(Constants.STATUS_ENCODING, frameCount));
            }
            else
            {
                UIHelper.SafeSetEnabled(btnEncode, true);
                UIHelper.SafeSetText(btnEncode, "🎬 Encode Video");
            }
        }

        /// <summary>
        /// Executes the FFmpeg encoding process.
        /// </summary>
        private async Task ExecuteFfmpegEncoding(string sessionFolder, string[] frameFiles)
        {
            // Verify FFmpeg still accessible
            if (!File.Exists(_ffmpegPath))
            {
                throw new FileNotFoundException($"FFmpeg not found at: {_ffmpegPath}");
            }

            // Create filelist
            string fileListPath = CreateFileList(sessionFolder, frameFiles);

            // Prepare output
            string outputPath = PrepareOutputPath(sessionFolder);

            // Build FFmpeg arguments
            string preset = GetEncodingPreset();
            string ffmpegArgs = BuildFfmpegArguments(fileListPath, outputPath, preset);

            // Run FFmpeg
            var result = await FfmpegRunner.RunFfmpegAsync(_ffmpegPath, ffmpegArgs);

            // Clean up
            SessionManager.CleanTempFolder(sessionFolder);

            // Handle result
            if (result.exitCode == 0)
            {
                HandleEncodingSuccess(frameFiles.Length, outputPath);
            }
            else
            {
                HandleEncodingFailure(result);
            }
        }

        /// <summary>
        /// Creates the file list for FFmpeg.
        /// </summary>
        private string CreateFileList(string sessionFolder, string[] frameFiles)
        {
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

            return fileListPath;
        }

        /// <summary>
        /// Prepares the output path for the encoded video.
        /// </summary>
        private string PrepareOutputPath(string sessionFolder)
        {
            string outputFolder = SessionManager.GetOutputFolder(sessionFolder);
            Directory.CreateDirectory(outputFolder);
            return Path.Combine(outputFolder, $"timelapse_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        }

        /// <summary>
        /// Gets the encoding preset from UI selection.
        /// </summary>
        private string GetEncodingPreset()
        {
            if (cmbEncodingPreset?.SelectedIndex >= 0)
            {
                return cmbEncodingPreset.SelectedIndex switch
                {
                    0 => "ultrafast",
                    1 => "fast",
                    2 => "medium",
                    3 => "slow",
                    _ => Constants.DEFAULT_ENCODING_PRESET
                };
            }
            return Constants.DEFAULT_ENCODING_PRESET;
        }

        /// <summary>
        /// Builds FFmpeg command line arguments.
        /// </summary>
        private string BuildFfmpegArguments(string fileListPath, string outputPath, string preset)
        {
            return $"-y -f concat -safe 0 -i \"{fileListPath}\" -r {_targetFrameRate} -c:v libx264 -preset {preset} -crf 23 \"{outputPath}\"";
        }

        /// <summary>
        /// Handles successful encoding completion.
        /// </summary>
        private void HandleEncodingSuccess(int frameCount, string outputPath)
        {
            UIHelper.SafeUpdateLabel(lblStatus, Constants.STATUS_ENCODING_COMPLETE);

            UIHelper.ShowInfo(
                $"✅ Video encoded successfully!\n\n" +
                $"Frames: {frameCount}\n" +
                $"Output: {Path.GetFileName(outputPath)}\n" +
                $"Location: output/ folder\n\n" +
                $"Full path:\n{outputPath}",
                "Encoding Complete");

            OpenOutputFolder(outputPath);
        }

        /// <summary>
        /// Handles encoding failure.
        /// </summary>
        private void HandleEncodingFailure((int exitCode, string output, string error) result)
        {
            UIHelper.SafeUpdateLabel(lblStatus, Constants.STATUS_ENCODING_FAILED);

            UIHelper.ShowError(
                $"FFmpeg encoding failed with exit code {result.exitCode}\n\n" +
                $"Error output:\n{result.error}\n\n" +
                $"Standard output:\n{result.output}",
                "Encoding Failed");
        }

        /// <summary>
        /// Handles encoding errors with consistent UI updates.
        /// </summary>
        private void HandleEncodingError(string statusText, string title, string message)
        {
            UIHelper.SafeUpdateLabel(lblStatus, statusText);
            UIHelper.ShowError(message, title);
        }

        /// <summary>
        /// Opens the output folder with the encoded video.
        /// </summary>
        private void OpenOutputFolder(string outputPath)
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{outputPath}\"");
            }
            catch
            {
                Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(outputPath), UseShellExecute = true });
            }
        }

        /// <summary>
        /// Resets the encoding state and UI.
        /// </summary>
        private void ResetEncodingState()
        {
            _isEncoding = false;
            UpdateEncodingUI(false);
            UpdateStatusDisplay();
        }


        #endregion

        #region Error Handling & Safety


        /// <summary>
        /// Sanitize session name for filesystem compatibility.
        /// </summary>
        private string SanitizeSessionName(string name) => ValidationHelper.SanitizeSessionName(name);

        #endregion

        #region Form Lifecycle
        /// <summary>
        /// Dispose UI update timer and region overlay.
        /// </summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _captureTimer?.Dispose();
            _uiUpdateTimer?.Dispose();
            _regionOverlay?.Dispose();

            if (_activeSessionFolder != null)
                SessionManager.MarkSessionInactive(_activeSessionFolder);
        }

        #endregion

        private void lblQuality_Click(object sender, EventArgs e)
        {

        }
    }
}