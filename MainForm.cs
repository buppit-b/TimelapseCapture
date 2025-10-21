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
        private System.Windows.Forms.Timer? _uiUpdateTimer;

        // Remember the user's aspect-ratio selection so we can restore it when leaving Full Screen.
        private int _lastAspectRatioIndex = 0;

        // Suppress saving when programmatically changing aspect ratio (prevents unwanted saves during restoration)
        private bool _suppressAspectRatioSave = false;

        // Track consecutive capture errors for safety
        private int _consecutiveCaptureErrors = 0;
        private const int MAX_CONSECUTIVE_ERRORS = 3;

        // Encoding settings
        private int _targetFrameRate = 25; // Default to 25fps

        // Region overlay
        private RegionOverlay? _regionOverlay;
        private bool _isOverlayVisible = false;

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
            _uiUpdateTimer.Interval = 500; // Update twice per second
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

        /// <summary>
        /// Download FFmpeg when user requests it.
        /// </summary>
        private async void DownloadFfmpeg()
        {
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

            if (lblStatus != null) lblStatus.Text = "Starting FFmpeg download...";
            var dirTarget = Path.Combine(AppContext.BaseDirectory, "ffmpeg");

            // Disable buttons during download
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
                    UpdateSessionInfoPanel();
                }
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
            captureRegion = session.CaptureRegion;

            if (txtSessionName != null)
                txtSessionName.Text = session.Name;

            if (numInterval != null)
                numInterval.Value = session.IntervalSeconds;

            if (cmbFormat != null)
                cmbFormat.SelectedItem = session.ImageFormat ?? "JPEG";

            if (numQuality != null && session.ImageFormat == "JPEG")
                numQuality.Value = session.JpegQuality;

            string ratioInfo = AspectRatio.CalculateRatioString(captureRegion.Width, captureRegion.Height);
            if (lblRegion != null)
                lblRegion.Text = $"Region: {captureRegion.Width}×{captureRegion.Height} ({ratioInfo}) at ({captureRegion.X},{captureRegion.Y})";

            SaveSettings();
            UpdateStatusDisplay();
            UpdateCaptureTimer();
            UpdateSessionInfoPanel();

            MessageBox.Show(
                $"Session '{session.Name}' loaded!\n\n" +
                $"Frames: {session.FramesCaptured}\n" +
                $"Region: {captureRegion.Width}×{captureRegion.Height}\n" +
                $"Location: {Path.GetFileName(sessionPath)}\n\n" +
                $"Ready to continue capturing.",
                "Session Loaded",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        /// <summary>
        /// Handle New Session button click.
        /// Prompts for session name and creates new session.
        /// </summary>
        private void btnNewSession_Click(object? sender, EventArgs e)
        {
            // Warn if active session exists with frames
            if (_activeSession != null && _activeSession.FramesCaptured > 0)
            {
                var result = MessageBox.Show(
                    $"Current session '{_activeSession.Name}' has {_activeSession.FramesCaptured} frames.\n\n" +
                    "Starting a new session will:\n" +
                    "• Save and close the current session\n" +
                    "• Start fresh with new settings\n\n" +
                    "Continue?",
                    "Start New Session?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No)
                    return;

                // Mark current session inactive
                if (_activeSessionFolder != null)
                    SessionManager.MarkSessionInactive(_activeSessionFolder);
            }

            // Validate required settings first
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

            if (captureRegion.IsEmpty || !IsValidRegion(captureRegion))
            {
                MessageBox.Show(
                    "Please select a valid capture region first.\n\n" +
                    "Click 'Select Region' to choose the area to capture.",
                    "Capture Region Required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Show session name dialog
            using (var dialog = new SessionNameDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string sessionName = dialog.SessionName;

                    // Create new session with custom name
                    var capturesRoot = Path.Combine(settings.SaveFolder, "captures");
                    int interval = (int)(numInterval?.Value ?? 5);
                    var format = cmbFormat?.SelectedItem?.ToString() ?? "JPEG";
                    var quality = (int)(numQuality?.Value ?? 90);

                    try
                    {
                        // Mark old session as inactive first
                        if (_activeSessionFolder != null)
                            SessionManager.MarkSessionInactive(_activeSessionFolder);

                        _activeSessionFolder = SessionManager.CreateNamedSession(
                            capturesRoot,
                            sessionName,
                            interval,
                            captureRegion,
                            format,
                            quality);

                        _activeSession = SessionManager.LoadSession(_activeSessionFolder);

                        // Update UI
                        if (txtSessionName != null)
                            txtSessionName.Text = _activeSession?.Name ?? sessionName;

                        UpdateStatusDisplay();
                        UpdateCaptureTimer();
                        UpdateSessionInfoPanel();

                        // Show warning if name was adjusted for duplicates
                        string message = $"✅ New session '{_activeSession?.Name ?? sessionName}' created!\n\n";
                        if (_activeSession?.Name != sessionName && _activeSession?.Name?.Contains("(") == true)
                        {
                            message += "⚠️ A session with this name already exists.\n";
                            message += "The new session was renamed to avoid conflicts.\n\n";
                        }
                        message += "Session folder: " + Path.GetFileName(_activeSessionFolder) + "\n\n";
                        message += "👉 Press 'Start Capture' when you're ready to begin recording.";

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
        private bool IsValidRegion(Rectangle r)
        {
            return r.Width > 0 && r.Height > 0 && (r.Width & 1) == 0 && (r.Height & 1) == 0;
        }

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
        /// </summary>
        private void ShowRegionOverlay()
        {
            if (_regionOverlay == null) return;

            // Check if region is set
            if (captureRegion.IsEmpty || captureRegion.Width == 0 || captureRegion.Height == 0)
            {
                MessageBox.Show(
                    "No region selected yet.\n\n" +
                    "Please select a capture region first using:\n" +
                    "• 'Select' button - Manual selection\n" +
                    "• 'Full Screen' button - Entire monitor",
                    "No Region",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // Get all screens to calculate overlay bounds
            var allScreens = Screen.AllScreens;
            int minX = allScreens.Min(s => s.Bounds.X);
            int minY = allScreens.Min(s => s.Bounds.Y);
            int maxX = allScreens.Max(s => s.Bounds.Right);
            int maxY = allScreens.Max(s => s.Bounds.Bottom);

            // Position overlay to cover entire virtual screen
            _regionOverlay.Bounds = new Rectangle(minX, minY, maxX - minX, maxY - minY);
            _regionOverlay.Region = captureRegion;
            _regionOverlay.IsActive = IsCapturing;
            _regionOverlay.Show();

            _isOverlayVisible = true;
            UpdateRegionOverlayButton();
        }

        /// <summary>
        /// Hide the region overlay.
        /// </summary>
        private void HideRegionOverlay()
        {
            if (_regionOverlay == null) return;

            _regionOverlay.Hide();
            _isOverlayVisible = false;
            UpdateRegionOverlayButton();
        }

        /// <summary>
        /// Update region overlay with current settings.
        /// Call this whenever region or capture state changes.
        /// </summary>
        private void UpdateRegionOverlay()
        {
            if (_regionOverlay == null || !_isOverlayVisible) return;

            _regionOverlay.Region = captureRegion;
            _regionOverlay.IsActive = IsCapturing;
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

            if (_activeSession != null)
            {
                var result = MessageBox.Show(
                    $"Session '{_activeSession.Name}' is currently loaded.\n\n" +
                    $"Status: {_activeSession.FramesCaptured} frames captured\n" +
                    $"Region: {_activeSession.CaptureRegion.Width}×{_activeSession.CaptureRegion.Height}\n\n" +
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

            // Set capture region to this monitor
            captureRegion = new Rectangle(bounds.X, bounds.Y, width, height);

            // Calculate and display aspect ratio string
            string ratioInfo = AspectRatio.CalculateRatioString(captureRegion.Width, captureRegion.Height);
            if (lblRegion != null)
            {
                lblRegion.Text = $"Region: {captureRegion.Width}×{captureRegion.Height} ({ratioInfo}) at ({captureRegion.X},{captureRegion.Y})";
            }

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

            // Persist region while preserving the user's saved aspect-ratio preference
            int preservedAspectIndex = _lastAspectRatioIndex;
            settings.Region = captureRegion;
            settings.AspectRatioIndex = preservedAspectIndex; // Preserve user's original choice
            SettingsManager.Save(settings);

            UpdateStatusDisplay();
            UpdateCaptureTimer();
            UpdateRegionOverlay();
        }

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

                        // Clear full screen info since this is manual selection
                        if (lblFullScreenInfo != null)
                            lblFullScreenInfo.Text = "";

                        settings.Region = captureRegion;
                        SaveSettings();

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

            Show();
            UpdateStatusDisplay();
            UpdateCaptureTimer();
            UpdateSessionInfoPanel();
            UpdateRegionOverlay();
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
                // No active session - must create one first
                MessageBox.Show(
                    "No session is loaded.\n\n" +
                    "Please create a session first using:\n" +
                    "• 'New' button (custom name)\n" +
                    "• 'Load' button (resume existing)",
                    "Create Session First",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            else
            {
                // Existing session - validate settings
                if (!SessionManager.ValidateSessionSettings(_activeSession, captureRegion, format, quality))
                {
                    // Build detailed mismatch list
                    var mismatches = new System.Text.StringBuilder();
                    mismatches.AppendLine("Your current settings don't match the loaded session:\n");

                    if (_activeSession.CaptureRegion != captureRegion)
                    {
                        mismatches.AppendLine($"• Region:");
                        mismatches.AppendLine($"  Session: {_activeSession.CaptureRegion.Width}×{_activeSession.CaptureRegion.Height}");
                        mismatches.AppendLine($"  Current: {captureRegion.Width}×{captureRegion.Height}\n");
                    }

                    if (_activeSession.ImageFormat != format)
                    {
                        mismatches.AppendLine($"• Format:");
                        mismatches.AppendLine($"  Session: {_activeSession.ImageFormat}");
                        mismatches.AppendLine($"  Current: {format}\n");
                    }

                    if (_activeSession.JpegQuality != quality && format == "JPEG")
                    {
                        mismatches.AppendLine($"• JPEG Quality:");
                        mismatches.AppendLine($"  Session: {_activeSession.JpegQuality}");
                        mismatches.AppendLine($"  Current: {quality}\n");
                    }

                    var result = MessageBox.Show(
                        mismatches.ToString() +
                        "What would you like to do?\n\n" +
                        "• YES: Start a new session with current settings\n" +
                        "• NO: Cancel and adjust settings to match session",
                        "Settings Mismatch",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (result == DialogResult.Yes)
                    {
                        SessionManager.MarkSessionInactive(_activeSessionFolder!);
                        btnNewSession_Click(sender, e);

                        if (_activeSession == null)
                            return;
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

            // Reset error counter at start of new capture
            _consecutiveCaptureErrors = 0;

            var intervalMs = intervalSec * 1000;
            _captureTimer = new System.Threading.Timer(CaptureFrame, null, 0, intervalMs);
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
                // SAFETY CHECK: Disk space (check every 10 frames to reduce overhead)
                if (_activeSession.FramesCaptured % 10 == 0 && !string.IsNullOrEmpty(_activeSessionFolder))
                {
                    if (!CheckDiskSpace(_activeSessionFolder, 50_000_000)) // Require 50MB free
                    {
                        BeginInvoke(new Action(() =>
                        {
                            StopCapture();
                            MessageBox.Show(
                                "⚠️ Disk space is running low!\n\n" +
                                "Capture has been stopped automatically to prevent data loss.\n\n" +
                                "Please free up disk space before continuing.",
                                "Low Disk Space",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }));
                        return;
                    }
                }

                DateTime now = DateTime.UtcNow;

                // Initialize LastCaptureTime on first frame
                if (!_activeSession.LastCaptureTime.HasValue)
                {
                    _activeSession.LastCaptureTime = now;
                }
                else
                {
                    // Calculate actual elapsed time since last capture
                    double elapsedSeconds = (now - _activeSession.LastCaptureTime.Value).TotalSeconds;
                    _activeSession.TotalCaptureSeconds += elapsedSeconds;
                    _activeSession.LastCaptureTime = now;
                }

                // Use sequential frame numbering
                long nextFrameNumber = _activeSession.FramesCaptured + 1;
                string frameNumber = $"{nextFrameNumber:D5}";

                string framesFolder = SessionManager.GetFramesFolder(_activeSessionFolder!);
                string fileName = Path.Combine(framesFolder, $"{frameNumber}.jpg");

                // SAFETY: Use using statement to ensure bitmap disposal
                using (var bmp = CaptureScreen())
                {
                    if (bmp == null || bmp.Width == 0 || bmp.Height == 0)
                    {
                        throw new InvalidOperationException("Captured bitmap is invalid");
                    }

                    bmp.Save(fileName, ImageFormat.Jpeg);
                }

                // Save session with updated time
                SessionManager.SaveSession(_activeSessionFolder!, _activeSession);
                SessionManager.IncrementFrameCount(_activeSessionFolder!);
                _activeSession = SessionManager.LoadSession(_activeSessionFolder);

                // Reset error counter on success
                _consecutiveCaptureErrors = 0;

                // UI updates
                BeginInvoke(new Action(() =>
                {
                    UpdateStatusDisplay();
                    UpdateCaptureTimer();
                    UpdateSessionInfoPanel();
                }));
            }
            catch (IOException ioEx)
            {
                Debug.WriteLine($"I/O error during capture: {ioEx.Message}");
                _consecutiveCaptureErrors++;

                BeginInvoke(new Action(() =>
                {
                    if (_consecutiveCaptureErrors >= MAX_CONSECUTIVE_ERRORS)
                    {
                        StopCapture();
                        MessageBox.Show(
                            $"❌ Capture stopped after {MAX_CONSECUTIVE_ERRORS} consecutive errors.\n\n" +
                            $"Last error: {ioEx.Message}\n\n" +
                            "Possible causes:\n" +
                            "• Disk full or nearly full\n" +
                            "• File system errors\n" +
                            "• Antivirus blocking file access\n" +
                            "• Network drive disconnected",
                            "Capture Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    else
                    {
                        if (lblStatus != null)
                            lblStatus.Text = $"⚠️ I/O Error ({_consecutiveCaptureErrors}/{MAX_CONSECUTIVE_ERRORS})";
                    }
                }));
            }
            catch (UnauthorizedAccessException uaEx)
            {
                Debug.WriteLine($"Permission error: {uaEx.Message}");

                BeginInvoke(new Action(() =>
                {
                    StopCapture();
                    MessageBox.Show(
                        "🚫 Permission denied during capture.\n\n" +
                        $"Cannot write to: {_activeSessionFolder}\n\n" +
                        "Solutions:\n" +
                        "• Run as administrator\n" +
                        "• Choose a different output folder\n" +
                        "• Check folder permissions",
                        "Permission Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }));
            }
            catch (OutOfMemoryException memEx)
            {
                Debug.WriteLine($"Out of memory: {memEx.Message}");

                BeginInvoke(new Action(() =>
                {
                    StopCapture();
                    MessageBox.Show(
                        "💥 Out of memory!\n\n" +
                        "The system has run out of available memory.\n\n" +
                        "Solutions:\n" +
                        "• Close other applications\n" +
                        "• Reduce capture resolution\n" +
                        "• Restart the application",
                        "Memory Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected capture error: {ex.GetType().Name} - {ex.Message}");
                _consecutiveCaptureErrors++;

                BeginInvoke(new Action(() =>
                {
                    if (_consecutiveCaptureErrors >= MAX_CONSECUTIVE_ERRORS)
                    {
                        StopCapture();
                        MessageBox.Show(
                            $"❌ Unexpected error during capture.\n\n" +
                            $"Type: {ex.GetType().Name}\n" +
                            $"Message: {ex.Message}\n\n" +
                            "Capture has been stopped for safety.",
                            "Capture Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    else
                    {
                        if (lblStatus != null)
                            lblStatus.Text = $"⚠️ Error ({_consecutiveCaptureErrors}/{MAX_CONSECUTIVE_ERRORS}): {ex.Message}";
                    }
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
        /// Update the Session Info Panel to show current session settings.
        /// Provides at-a-glance visibility of locked settings during capture.
        /// </summary>
        private void UpdateSessionInfoPanel()
        {
            if (lblSessionInfoRegion == null || lblSessionInfoFormat == null ||
                lblSessionInfoQuality == null || lblSessionInfoInterval == null)
                return;

            if (_activeSession != null)
            {
                // Show session settings
                var region = _activeSession.CaptureRegion;
                string ratioInfo = AspectRatio.CalculateRatioString(region.Width, region.Height);
                lblSessionInfoRegion.Text = $"📍 Region: {region.Width}×{region.Height} ({ratioInfo})";
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
                lblStatus.Text = $"🔴 CAPTURING - {_activeSession?.Name ?? "Session"} ({_activeSession?.FramesCaptured ?? 0} frames)";
                lblStatus.BackColor = Color.FromArgb(220, 50, 50); // Red
                lblStatus.ForeColor = Color.White;
            }
            else if (_activeSession != null)
            {
                // Session loaded but not capturing - GREEN background
                if (_activeSession.FramesCaptured > 0)
                {
                    lblStatus.Text = $"📂 Session loaded: {_activeSession.Name} ({_activeSession.FramesCaptured} frames) - Ready to capture";
                    lblStatus.BackColor = Color.FromArgb(60, 180, 75); // Green
                }
                else
                {
                    lblStatus.Text = $"🆕 New session ready: {_activeSession.Name} - Press Start to begin";
                    lblStatus.BackColor = Color.FromArgb(70, 160, 220); // Blue
                }
                lblStatus.ForeColor = Color.White;
            }
            else
            {
                // No session - GRAY background
                lblStatus.Text = "⚪ No session - Select region and press Start to create one";
                lblStatus.BackColor = Color.FromArgb(120, 120, 120); // Gray
                lblStatus.ForeColor = Color.White;
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

                // Build FFmpeg arguments with selected settings
                string preset = "medium"; // Default
                if (cmbEncodingPreset != null && cmbEncodingPreset.SelectedIndex >= 0)
                {
                    preset = cmbEncodingPreset.SelectedIndex switch
                    {
                        0 => "ultrafast",
                        1 => "fast",
                        2 => "medium",
                        3 => "slow",
                        _ => "medium"
                    };
                }

                string ffmpegArgs = $"-y -f concat -safe 0 -i \"{fileListPath}\" -r {_targetFrameRate} -c:v libx264 -preset {preset} -crf 23 \"{outputPath}\"";

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

        #region Error Handling & Safety

        /// <summary>
        /// Check if there's sufficient disk space for capture operations.
        /// </summary>
        private bool CheckDiskSpace(string path, long requiredBytes = 100_000_000) // 100MB default
        {
            try
            {
                var rootPath = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(rootPath)) return true;

                var drive = new DriveInfo(rootPath);
                return drive.AvailableFreeSpace > requiredBytes;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not check disk space: {ex.Message}");
                return true; // If we can't check, assume OK rather than block
            }
        }

        /// <summary>
        /// Sanitize session name for filesystem compatibility.
        /// </summary>
        private string SanitizeSessionName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return $"Session_{DateTime.Now:yyyyMMdd_HHmmss}";

            // Remove invalid filename characters
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = name;
            foreach (var c in invalid)
                sanitized = sanitized.Replace(c, '_');

            // Also remove some problematic characters that are technically valid
            sanitized = sanitized.Replace('.', '_').Replace(' ', '_');

            // Limit length to prevent path too long errors
            if (sanitized.Length > 50)
                sanitized = sanitized.Substring(0, 50);

            return sanitized.Trim('_'); // Remove leading/trailing underscores
        }

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