using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
 
namespace TimelapseCapture
{
    /// <summary>
    /// MainForm - Menu functionality
    /// Handles menu bar creation and menu item event handlers
    /// </summary>
    partial class MainForm
    {
        #region Menu Controls
        
        private MenuStrip? menuStrip;
        private ToolStripMenuItem? menuFile;
        private ToolStripMenuItem? menuSession;
        private ToolStripMenuItem? menuSettings;
        private ToolStripMenuItem? menuHelp;

        // Direct references to the state-dependent items so UpdateMenuStates() doesn't index
        // DropDownItems by hard-coded position (which silently breaks if a menu is reordered).
        private ToolStripMenuItem? _menuCloseSession;
        private ToolStripMenuItem? _menuStartCapture;
        private ToolStripMenuItem? _menuStopCapture;
        private ToolStripMenuItem? _menuSessionDetails;
        private ToolStripMenuItem? _menuOpenFolder;

        #endregion

        #region Menu Initialization

        /// <summary>
        /// Creates and initializes the menu bar
        /// </summary>
        private void InitializeMenuBar()
        {
            menuStrip = new MenuStrip
            {
                BackColor = System.Drawing.Color.FromArgb(30, 30, 30),
                ForeColor = System.Drawing.Color.FromArgb(220, 220, 220),
                Dock = DockStyle.Top,
                Renderer = new ToolStripProfessionalRenderer(new DarkMenuColorTable())
            };

            // File Menu
            _menuCloseSession = CreateMenuItem("&Close Session", MenuFile_CloseSession_Click);
            menuFile = new ToolStripMenuItem("&File");
            menuFile.DropDownItems.AddRange(new ToolStripItem[]
            {
                CreateMenuItem("&New Session...", MenuFile_NewSession_Click, "Ctrl+N"),
                CreateMenuItem("&Load Session...", MenuFile_LoadSession_Click, "Ctrl+O"),
                _menuCloseSession,
                new ToolStripSeparator(),
                CreateMenuItem("E&xit", MenuFile_Exit_Click, "Alt+F4")
            });

            // Session Menu
            _menuStartCapture = CreateMenuItem("&Start Capture", MenuSession_StartCapture_Click, "F5");
            _menuStopCapture = CreateMenuItem("S&top Capture", MenuSession_StopCapture_Click, "F6");
            _menuSessionDetails = CreateMenuItem("Session &Details...", MenuSession_Details_Click);
            _menuOpenFolder = CreateMenuItem("&Open Session Folder", MenuSession_OpenFolder_Click, "Ctrl+E");
            menuSession = new ToolStripMenuItem("&Session");
            menuSession.DropDownItems.AddRange(new ToolStripItem[]
            {
                _menuStartCapture,
                _menuStopCapture,
                new ToolStripSeparator(),
                _menuSessionDetails,
                _menuOpenFolder
            });

            // Settings Menu
            menuSettings = new ToolStripMenuItem("&Settings");
            menuSettings.DropDownItems.AddRange(new ToolStripItem[]
            {
                CreateMenuItem("&Output Folder...", MenuSettings_OutputFolder_Click),
                CreateMenuItem("&FFmpeg Path...", MenuSettings_FfmpegPath_Click),
                new ToolStripSeparator(),
                CreateMenuItem("&Encoding Settings...", MenuSettings_EncodingSettings_Click),
                CreateMenuItem("&Smart Interval Settings...", MenuSettings_SmartInterval_Click),
                new ToolStripSeparator(),
                CreateMenuItem("&Preferences...", MenuSettings_Preferences_Click)
            });

            // Help Menu
            menuHelp = new ToolStripMenuItem("&Help");
            menuHelp.DropDownItems.AddRange(new ToolStripItem[]
            {
                CreateMenuItem("&Documentation", MenuHelp_Documentation_Click, "F1"),
                CreateMenuItem("&Keyboard Shortcuts", MenuHelp_Shortcuts_Click),
                new ToolStripSeparator(),
                CreateMenuItem("&About", MenuHelp_About_Click)
            });

            menuStrip.Items.AddRange(new ToolStripItem[] { menuFile, menuSession, menuSettings, menuHelp });
            
            // Add menu to form (at top)
            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);
            menuStrip.BringToFront();
            
            Logger.Log("UI", "Menu bar initialized");
        }

        /// <summary>
        /// Helper to create menu items with consistent styling
        /// </summary>
        private ToolStripMenuItem CreateMenuItem(string text, EventHandler? clickHandler, string? shortcut = null)
        {
            var item = new ToolStripMenuItem(text)
            {
                ForeColor = System.Drawing.Color.FromArgb(220, 220, 220),
                BackColor = System.Drawing.Color.FromArgb(45, 45, 45)
            };

            if (clickHandler != null)
                item.Click += clickHandler;

            if (!string.IsNullOrEmpty(shortcut))
                item.ShortcutKeyDisplayString = shortcut;

            return item;
        }

        /// <summary>
        /// Custom color table for dark theme menu rendering
        /// </summary>
        private class DarkMenuColorTable : ProfessionalColorTable
        {
            public override Color MenuItemSelected => Color.FromArgb(62, 62, 64);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(62, 62, 64);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(62, 62, 64);
            public override Color MenuItemPressedGradientBegin => Color.FromArgb(50, 50, 52);
            public override Color MenuItemPressedGradientEnd => Color.FromArgb(50, 50, 52);
            public override Color MenuItemBorder => Color.FromArgb(70, 70, 70);
            public override Color MenuBorder => Color.FromArgb(70, 70, 70);
            public override Color ImageMarginGradientBegin => Color.FromArgb(40, 40, 40);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(40, 40, 40);
            public override Color ImageMarginGradientEnd => Color.FromArgb(40, 40, 40);
            public override Color ToolStripDropDownBackground => Color.FromArgb(45, 45, 45);
        }

        /// <summary>
        /// Updates menu item enabled states based on current application state
        /// </summary>
        private void UpdateMenuStates()
        {
            bool hasActiveSession = _activeSession != null;
            bool isCapturing = IsCapturing;

            if (_menuCloseSession != null)
                _menuCloseSession.Enabled = hasActiveSession && !isCapturing;

            if (_menuStartCapture != null)
                _menuStartCapture.Enabled = hasActiveSession && !isCapturing && GetCurrentRegion() != null;

            if (_menuStopCapture != null)
                _menuStopCapture.Enabled = isCapturing;

            if (_menuSessionDetails != null)
                _menuSessionDetails.Enabled = hasActiveSession;

            if (_menuOpenFolder != null)
                _menuOpenFolder.Enabled = hasActiveSession;
        }

        #endregion

        #region File Menu Handlers

        private void MenuFile_NewSession_Click(object? sender, EventArgs e)
        {
            // Stop capture if running
            if (IsCapturing)
            {
                var result = MessageBox.Show(
                    "Stop current capture to create a new session?",
                    "Capture Active",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result != DialogResult.Yes)
                    return;

                StopCapture();
            }

            // Show session setup wizard
            ShowSessionSetupWizard();
        }

        private void MenuFile_LoadSession_Click(object? sender, EventArgs e)
        {
            btnLoadSession_Click(sender, e);
        }

        private void MenuFile_CloseSession_Click(object? sender, EventArgs e)
        {
            if (_activeSession == null)
                return;

            if (IsCapturing)
            {
                MessageBox.Show(
                    "Please stop capture before closing the session.",
                    "Capture Active",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Close session '{_activeSession.Name}'?\n\nYou can reload it later from the session list.",
                "Close Session",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                if (_activeSessionFolder != null)
                    SessionManager.MarkSessionInactive(_activeSessionFolder);
                _activeSession = null;
                _activeSessionFolder = null;
                ClearCurrentRegion();
                UpdateCaptureTimer();
                RefreshUiState();
                
                Logger.Log("Session", "Session closed by user");
            }
        }

        private void MenuFile_Exit_Click(object? sender, EventArgs e)
        {
            this.Close();
        }

        #endregion

        #region Session Menu Handlers

        private void MenuSession_StartCapture_Click(object? sender, EventArgs e)
        {
            btnStart_Click(sender, e);
        }

        private void MenuSession_StopCapture_Click(object? sender, EventArgs e)
        {
            btnStop_Click(sender, e);
        }

        private void MenuSession_Details_Click(object? sender, EventArgs e)
        {
            if (_activeSession == null)
                return;

            string details = $"Session: {_activeSession.Name}\n" +
                           $"Started: {_activeSession.StartTime:yyyy-MM-dd HH:mm:ss}\n" +
                           $"Frames Captured: {_activeSession.FramesCaptured}\n" +
                           $"Total Capture Time: {FormatTimeSpan(TimeSpan.FromSeconds(_activeSession.TotalCaptureSeconds))}\n" +
                           $"Interval: {_activeSession.IntervalSeconds}s\n" +
                           $"Format: {_activeSession.ImageFormat}\n" +
                           $"Quality: {_activeSession.JpegQuality}\n" +
                           $"Smart Interval: {(_activeSession.SmartIntervalEnabled ? "Enabled" : "Disabled")}\n" +
                           $"Format Version: {_activeSession.FormatVersion}";

            if (_activeSession.CaptureRegion.HasValue)
            {
                var region = _activeSession.CaptureRegion.Value;
                details += $"\nRegion: {region.Width}x{region.Height} at ({region.X}, {region.Y})";
            }

            MessageBox.Show(details, "Session Details", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void MenuSession_OpenFolder_Click(object? sender, EventArgs e)
        {
            btnOpenFolder_Click(sender, e);
        }

        #endregion

        #region Settings Menu Handlers

        private void MenuSettings_OutputFolder_Click(object? sender, EventArgs e)
        {
            btnChooseFolder_Click(sender, e);
        }

        private void MenuSettings_FfmpegPath_Click(object? sender, EventArgs e)
        {
            btnBrowseFfmpeg_Click(sender, e);
        }

        private void MenuSettings_EncodingSettings_Click(object? sender, EventArgs e)
        {
            // TODO: Create EncodingSettingsDialog
            MessageBox.Show(
                "Encoding settings dialog coming soon.\n\nFor now, encoding settings are configured when creating a new session.",
                "Encoding Settings",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void MenuSettings_SmartInterval_Click(object? sender, EventArgs e)
        {
            // Expand smart interval panel if collapsed
            if (grpSmartInterval != null && !grpSmartInterval.Visible)
            {
                grpSmartInterval.Visible = true;
            }
            
            MessageBox.Show(
                "Smart Interval settings are in the 'Advanced: Smart Interval' panel below.",
                "Smart Interval Settings",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void MenuSettings_Preferences_Click(object? sender, EventArgs e)
        {
            // TODO: Create PreferencesDialog
            MessageBox.Show(
                "Preferences dialog coming soon.",
                "Preferences",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        #endregion

        #region Help Menu Handlers

        private void MenuHelp_Documentation_Click(object? sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/yourusername/TimelapseCapture/wiki",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to open documentation:\n{ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void MenuHelp_Shortcuts_Click(object? sender, EventArgs e)
        {
            string shortcuts = "Keyboard Shortcuts:\n\n" +
                             "Ctrl+N - New Session\n" +
                             "Ctrl+O - Load Session\n" +
                             "F5 - Start Capture\n" +
                             "F6 - Stop Capture\n" +
                             "Ctrl+E - Open Session Folder\n" +
                             "Ctrl+R - Toggle Region Overlay\n" +
                             "F1 - Help Documentation\n" +
                             "Alt+F4 - Exit";

            MessageBox.Show(shortcuts, "Keyboard Shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void MenuHelp_About_Click(object? sender, EventArgs e)
        {
            string about = "Timelapse Capture\n" +
                         "Version 2.0\n\n" +
                         "Screen capture timelapse tool optimized for digital art workflows.\n\n" +
                         "Built with C# + WinForms + FFmpeg\n" +
                         "© 2025";

            MessageBox.Show(about, "About Timelapse Capture", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        #endregion

        #region Session Setup Wizard

        /// <summary>
        /// Shows the session setup wizard and creates a new session if completed
        /// </summary>
        private void ShowSessionSetupWizard()
        {
            try
            {
                // Create wizard with existing settings pre-populated
                using var wizard = new SessionSetupForm(settings.SaveFolder ?? "", settings.FfmpegPath ?? "");
                
                if (wizard.ShowDialog(this) == DialogResult.OK && wizard.SetupCompleted)
                {
                    // Update settings
                    settings.SaveFolder = wizard.OutputFolder;
                    settings.FfmpegPath = wizard.FfmpegPath;
                    
                    // Store encoding settings (we'll need to add these to CaptureSettings)
                    // For now, just save the settings
                    SaveSettingsImmediate();
                    
                    // Deactivate any previously-active session first, so we never leave two
                    // Active=true sessions on disk (mirrors btnNewSession_Click). Otherwise
                    // FindActiveSession can resurrect the wrong session on next launch.
                    if (_activeSessionFolder != null)
                        SessionManager.MarkSessionInactive(_activeSessionFolder);

                    // Create the session
                    var capturesRoot = Path.Combine(settings.SaveFolder, "captures");
                    _activeSessionFolder = SessionManager.CreateNamedSession(
                        capturesRoot,
                        wizard.SessionName,
                        settings.IntervalSeconds,
                        null, // Region will be set later
                        settings.Format,
                        settings.JpegQuality);
                    
                    var newSession = SessionManager.LoadSession(_activeSessionFolder);
                    
                    if (newSession != null)
                    {
                        _activeSession = newSession;
                        
                        // Store encoding settings in session
                        if (_activeSession != null)
                        {
                            _activeSession.VideoFps = wizard.FrameRate;
                            // TODO: Add EncodingPreset and CrfQuality to SessionInfo if needed
                            SessionManager.SaveSession(_activeSessionFolder, _activeSession);
                        }
                        
                        UpdateCaptureTimer();
                        RefreshUiState();
                        
                        Logger.Log("Session", $"New session created via wizard: {wizard.SessionName}");
                        
                        MessageBox.Show(
                            $"Session '{wizard.SessionName}' created successfully!\n\nNext step: Select a capture region.",
                            "Session Created",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error", $"Session setup wizard failed: {ex.Message}");
                MessageBox.Show(
                    $"Failed to create session:\n{ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        #endregion

        #region First Launch Detection

        /// <summary>
        /// Check if this is first launch and show wizard if needed.
        /// </summary>
        private void CheckAndShowWizard()
        {
            // Check if critical settings are missing
            bool needsSetup = string.IsNullOrEmpty(settings.SaveFolder) || 
                            string.IsNullOrEmpty(_ffmpegPath) ||
                            !System.IO.File.Exists(_ffmpegPath);
            
            if (needsSetup)
            {
                Logger.Log("Wizard", "First launch detected - showing setup wizard");
                
                // Use BeginInvoke to show wizard after form is fully loaded
                this.BeginInvoke(new Action(() =>
                {
                    ShowSessionSetupWizard();
                }));
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Format a TimeSpan for display.
        /// </summary>
        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalHours >= 1)
                return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m";
            else if (timeSpan.TotalMinutes >= 1)
                return $"{(int)timeSpan.TotalMinutes}m {timeSpan.Seconds}s";
            else
                return $"{timeSpan.Seconds}s";
        }

        #endregion
    }
}
