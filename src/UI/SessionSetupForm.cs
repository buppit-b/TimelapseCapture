using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TimelapseCapture
{
    /// <summary>
    /// Session setup wizard dialog - appears before main capture interface.
    /// Handles session creation, output folder selection, and encoding settings configuration.
    /// </summary>
    public partial class SessionSetupForm : Form
    {
        #region Properties

        public string SessionName { get; private set; } = string.Empty;
        public string OutputFolder { get; private set; } = string.Empty;
        public string FfmpegPath { get; private set; } = string.Empty;
        
        // Encoding Settings
        public int FrameRate { get; private set; } = 25;
        public string EncodingPreset { get; private set; } = "medium";
        public int CrfQuality { get; private set; } = 23;
        
        public bool SetupCompleted { get; private set; } = false;

        #endregion

        #region UI Controls

        // Session
        private Label? lblSessionTitle;
        private Label? lblSessionName;
        private TextBox? txtSessionName;
        
        // Output
        private Label? lblOutputTitle;
        private Label? lblOutputFolder;
        private TextBox? txtOutputFolder;
        private Button? btnBrowseOutput;
        
        // FFmpeg
        private Label? lblFfmpegTitle;
        private Label? lblFfmpegPath;
        private TextBox? txtFfmpegPath;
        private Button? btnBrowseFfmpeg;
        private Button? btnDownloadFfmpeg;
        
        // Encoding Settings
        private GroupBox? grpEncodingSettings;
        private Label? lblFrameRate;
        private ComboBox? cmbFrameRate;
        private NumericUpDown? numCustomFrameRate;
        private Label? lblPreset;
        private ComboBox? cmbPreset;
        private Label? lblQuality;
        private NumericUpDown? numCrf;
        private Label? lblQualityDesc;
        
        // Buttons
        private Button? btnContinue;
        private Button? btnCancel;
        
        // Status
        private Label? lblStatus;

        #endregion

        #region Constructor

        public SessionSetupForm()
        {
            InitializeComponents();
            LoadExistingSettings();
            UpdateStatus();
        }

        public SessionSetupForm(string existingOutputFolder, string existingFfmpegPath)
        {
            InitializeComponents();
            
            // Pre-populate with existing settings
            if (!string.IsNullOrEmpty(existingOutputFolder) && Directory.Exists(existingOutputFolder))
            {
                if (txtOutputFolder != null)
                {
                    txtOutputFolder.Text = existingOutputFolder;
                    OutputFolder = existingOutputFolder;
                }
            }
            
            if (!string.IsNullOrEmpty(existingFfmpegPath) && File.Exists(existingFfmpegPath))
            {
                if (txtFfmpegPath != null)
                {
                    txtFfmpegPath.Text = existingFfmpegPath;
                    FfmpegPath = existingFfmpegPath;
                }
            }
            
            UpdateStatus();
        }

        #endregion

        #region Initialization

        private void InitializeComponents()
        {
            // Form settings
            this.Text = "Session Setup - Timelapse Capture";
            this.Size = new Size(600, 650);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(20, 20, 20);
            this.ForeColor = Color.FromArgb(200, 200, 200);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Font = new Font("Segoe UI", 9F);

            int yPos = 20;
            int leftMargin = 30;
            int controlWidth = 520;

            // ===== SESSION NAME =====
            lblSessionTitle = new Label
            {
                Text = "1. Session Name",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 100),
                Location = new Point(leftMargin, yPos),
                AutoSize = true
            };
            this.Controls.Add(lblSessionTitle);
            yPos += 35;

            lblSessionName = new Label
            {
                Text = "Choose a name for this capture session:",
                Location = new Point(leftMargin, yPos),
                Size = new Size(controlWidth, 20),
                ForeColor = Color.LightGray
            };
            this.Controls.Add(lblSessionName);
            yPos += 25;

            txtSessionName = new TextBox
            {
                Location = new Point(leftMargin, yPos),
                Size = new Size(controlWidth, 25),
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            txtSessionName.TextChanged += TxtSessionName_TextChanged;
            this.Controls.Add(txtSessionName);
            yPos += 40;

            // ===== OUTPUT FOLDER =====
            lblOutputTitle = new Label
            {
                Text = "2. Output Folder",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 100),
                Location = new Point(leftMargin, yPos),
                AutoSize = true
            };
            this.Controls.Add(lblOutputTitle);
            yPos += 35;

            lblOutputFolder = new Label
            {
                Text = "Where should captures be saved?",
                Location = new Point(leftMargin, yPos),
                Size = new Size(controlWidth, 20),
                ForeColor = Color.LightGray
            };
            this.Controls.Add(lblOutputFolder);
            yPos += 25;

            txtOutputFolder = new TextBox
            {
                Location = new Point(leftMargin, yPos),
                Size = new Size(400, 25),
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = true
            };
            this.Controls.Add(txtOutputFolder);

            btnBrowseOutput = new Button
            {
                Text = "Browse...",
                Location = new Point(leftMargin + 410, yPos),
                Size = new Size(110, 25),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White
            };
            btnBrowseOutput.Click += BtnBrowseOutput_Click;
            this.Controls.Add(btnBrowseOutput);
            yPos += 40;

            // ===== FFMPEG PATH =====
            lblFfmpegTitle = new Label
            {
                Text = "3. FFmpeg (Video Encoder)",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 100),
                Location = new Point(leftMargin, yPos),
                AutoSize = true
            };
            this.Controls.Add(lblFfmpegTitle);
            yPos += 35;

            lblFfmpegPath = new Label
            {
                Text = "FFmpeg is required for video encoding:",
                Location = new Point(leftMargin, yPos),
                Size = new Size(controlWidth, 20),
                ForeColor = Color.LightGray
            };
            this.Controls.Add(lblFfmpegPath);
            yPos += 25;

            txtFfmpegPath = new TextBox
            {
                Location = new Point(leftMargin, yPos),
                Size = new Size(400, 25),
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = true
            };
            this.Controls.Add(txtFfmpegPath);

            btnBrowseFfmpeg = new Button
            {
                Text = "Browse...",
                Location = new Point(leftMargin + 410, yPos),
                Size = new Size(110, 25),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.LightGray
            };
            btnBrowseFfmpeg.Click += BtnBrowseFfmpeg_Click;
            this.Controls.Add(btnBrowseFfmpeg);
            yPos += 32;

            btnDownloadFfmpeg = new Button
            {
                Text = "⬇ Download FFmpeg",
                Location = new Point(leftMargin, yPos),
                Size = new Size(150, 28),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.LimeGreen
            };
            btnDownloadFfmpeg.Click += BtnDownloadFfmpeg_Click;
            this.Controls.Add(btnDownloadFfmpeg);
            yPos += 45;

            // ===== ENCODING SETTINGS =====
            grpEncodingSettings = new GroupBox
            {
                Text = "4. Video Encoding Settings",
                Location = new Point(leftMargin, yPos),
                Size = new Size(controlWidth, 180),
                ForeColor = Color.LightGray,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };

            int grpY = 30;
            
            lblFrameRate = new Label
            {
                Text = "Frame Rate:",
                Location = new Point(15, grpY),
                Size = new Size(100, 20),
                Font = new Font("Segoe UI", 9F)
            };
            grpEncodingSettings.Controls.Add(lblFrameRate);

            cmbFrameRate = new ComboBox
            {
                Location = new Point(120, grpY - 2),
                Size = new Size(150, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            cmbFrameRate.Items.AddRange(new object[] { "24 fps (Film)", "25 fps (PAL)", "30 fps (NTSC)", "60 fps (Smooth)", "Custom..." });
            cmbFrameRate.SelectedIndex = 1; // Default to 25 fps
            cmbFrameRate.SelectedIndexChanged += CmbFrameRate_SelectedIndexChanged;
            grpEncodingSettings.Controls.Add(cmbFrameRate);

            numCustomFrameRate = new NumericUpDown
            {
                Location = new Point(280, grpY - 2),
                Size = new Size(80, 25),
                Minimum = 1,
                Maximum = 120,
                Value = 30,
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };
            numCustomFrameRate.ValueChanged += NumCustomFrameRate_ValueChanged;
            grpEncodingSettings.Controls.Add(numCustomFrameRate);
            grpY += 35;

            lblPreset = new Label
            {
                Text = "Encoding Speed:",
                Location = new Point(15, grpY),
                Size = new Size(100, 20),
                Font = new Font("Segoe UI", 9F)
            };
            grpEncodingSettings.Controls.Add(lblPreset);

            cmbPreset = new ComboBox
            {
                Location = new Point(120, grpY - 2),
                Size = new Size(240, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            cmbPreset.Items.AddRange(new object[] { 
                "ultrafast - Fast encode, larger file", 
                "fast - Balanced",
                "medium - Good quality (recommended)", 
                "slow - Best quality, slower encode"
            });
            cmbPreset.SelectedIndex = 2; // Default to medium
            cmbPreset.SelectedIndexChanged += CmbPreset_SelectedIndexChanged;
            grpEncodingSettings.Controls.Add(cmbPreset);
            grpY += 35;

            lblQuality = new Label
            {
                Text = "Quality (CRF):",
                Location = new Point(15, grpY),
                Size = new Size(100, 20),
                Font = new Font("Segoe UI", 9F)
            };
            grpEncodingSettings.Controls.Add(lblQuality);

            numCrf = new NumericUpDown
            {
                Location = new Point(120, grpY - 2),
                Size = new Size(80, 25),
                Minimum = 0,
                Maximum = 51,
                Value = 23,
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            numCrf.ValueChanged += NumCrf_ValueChanged;
            grpEncodingSettings.Controls.Add(numCrf);

            lblQualityDesc = new Label
            {
                Text = "23 (Balanced) - Lower = Better quality",
                Location = new Point(210, grpY + 2),
                Size = new Size(300, 20),
                Font = new Font("Segoe UI", 8.25F),
                ForeColor = Color.FromArgb(150, 150, 150)
            };
            grpEncodingSettings.Controls.Add(lblQualityDesc);
            grpY += 40;

            Label lblEncodingNote = new Label
            {
                Text = "💡 These settings can be changed later from the menu",
                Location = new Point(15, grpY),
                Size = new Size(490, 20),
                Font = new Font("Segoe UI", 8.25F),
                ForeColor = Color.FromArgb(100, 150, 200)
            };
            grpEncodingSettings.Controls.Add(lblEncodingNote);

            this.Controls.Add(grpEncodingSettings);
            yPos += 195;

            // ===== STATUS =====
            lblStatus = new Label
            {
                Text = "⚠ Please complete all required fields",
                Location = new Point(leftMargin, yPos),
                Size = new Size(controlWidth, 20),
                ForeColor = Color.Orange,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            this.Controls.Add(lblStatus);
            yPos += 35;

            // ===== BUTTONS =====
            btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(leftMargin + 300, yPos),
                Size = new Size(100, 32),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.LightGray,
                DialogResult = DialogResult.Cancel
            };
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(100, 100, 100);
            btnCancel.FlatAppearance.BorderSize = 1;
            this.Controls.Add(btnCancel);

            btnContinue = new Button
            {
                Text = "Continue",
                Location = new Point(leftMargin + 410, yPos),
                Size = new Size(110, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 200, 100),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Enabled = false
            };
            btnContinue.FlatAppearance.BorderColor = Color.FromArgb(0, 200, 100);
            btnContinue.FlatAppearance.BorderSize = 1;
            btnContinue.Click += BtnContinue_Click;
            this.Controls.Add(btnContinue);

            this.AcceptButton = btnContinue;
            this.CancelButton = btnCancel;
        }

        #endregion

        #region Event Handlers

        private void TxtSessionName_TextChanged(object? sender, EventArgs e)
        {
            SessionName = txtSessionName?.Text.Trim() ?? "";
            UpdateStatus();
        }

        private void BtnBrowseOutput_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select output folder for captures",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                OutputFolder = dialog.SelectedPath;
                txtOutputFolder.Text = OutputFolder;
                UpdateStatus();
            }
        }

        private void BtnBrowseFfmpeg_Click(object? sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select FFmpeg executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                // Just check if file exists - FfmpegRunner will validate it
                if (File.Exists(dialog.FileName) && dialog.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    FfmpegPath = dialog.FileName;
                    txtFfmpegPath.Text = FfmpegPath;
                    UpdateStatus();
                }
                else
                {
                    MessageBox.Show("The selected file is not a valid FFmpeg executable.", "Invalid FFmpeg",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private async void BtnDownloadFfmpeg_Click(object? sender, EventArgs e)
        {
            if (btnDownloadFfmpeg != null)
                btnDownloadFfmpeg.Enabled = false;
            // Block cancel/close while the download runs, so the dialog can't be disposed out from
            // under the async continuation below (which would crash this async void handler).
            if (btnCancel != null)
                btnCancel.Enabled = false;

            try
            {
                var dirTarget = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
                string? downloadedPath = await FfmpegDownloader.EnsureFfmpegPresentAsync(
                    dirTarget,
                    (downloaded, total, status) => {
                        // Runs on a background thread. Guard against the form being closed
                        // mid-download and marshal with BeginInvoke (non-blocking).
                        if (string.IsNullOrEmpty(status)) return;
                        try
                        {
                            if (IsDisposed || Disposing) return;
                            if (InvokeRequired)
                                BeginInvoke(new Action(() => { if (!IsDisposed && lblStatus != null) lblStatus.Text = status; }));
                            else if (lblStatus != null)
                                lblStatus.Text = status;
                        }
                        catch { /* form closed during download */ }
                    });

                // The form may have been closed/disposed while awaiting; bail before touching it.
                if (IsDisposed || Disposing)
                    return;

                if (!string.IsNullOrEmpty(downloadedPath))
                {
                    FfmpegPath = downloadedPath;
                    if (txtFfmpegPath != null)
                        txtFfmpegPath.Text = FfmpegPath;
                    UpdateStatus();

                    MessageBox.Show(
                        $"FFmpeg downloaded successfully!\n\nLocation: {downloadedPath}",
                        "Download Complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(
                        "Failed to download FFmpeg.\n\nPlease try again or use the Browse button.",
                        "Download Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            finally
            {
                if (!IsDisposed)
                {
                    if (btnDownloadFfmpeg != null)
                        btnDownloadFfmpeg.Enabled = true;
                    if (btnCancel != null)
                        btnCancel.Enabled = true;
                }
            }
        }

        private void CmbFrameRate_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (cmbFrameRate.SelectedIndex == 4) // Custom
            {
                numCustomFrameRate.Visible = true;
                FrameRate = (int)numCustomFrameRate.Value;
            }
            else
            {
                numCustomFrameRate.Visible = false;
                FrameRate = cmbFrameRate.SelectedIndex switch
                {
                    0 => 24,
                    1 => 25,
                    2 => 30,
                    3 => 60,
                    _ => 25
                };
            }
        }

        private void NumCustomFrameRate_ValueChanged(object? sender, EventArgs e)
        {
            if (numCustomFrameRate.Visible)
            {
                FrameRate = (int)numCustomFrameRate.Value;
            }
        }

        private void CmbPreset_SelectedIndexChanged(object? sender, EventArgs e)
        {
            EncodingPreset = cmbPreset.SelectedIndex switch
            {
                0 => "ultrafast",
                1 => "fast",
                2 => "medium",
                3 => "slow",
                _ => "medium"
            };
        }

        private void NumCrf_ValueChanged(object? sender, EventArgs e)
        {
            CrfQuality = (int)numCrf.Value;
            
            string quality = CrfQuality switch
            {
                <= 18 => "Visually Lossless",
                <= 23 => "Balanced",
                <= 28 => "Good",
                _ => "Lower Quality"
            };
            
            lblQualityDesc.Text = $"{CrfQuality} ({quality}) - Lower = Better quality";
        }

        private void BtnContinue_Click(object? sender, EventArgs e)
        {
            // Final validation
            if (ValidateSetup())
            {
                SetupCompleted = true;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        #endregion

        #region Validation and Status

        private void UpdateStatus()
        {
            bool sessionValid = !string.IsNullOrWhiteSpace(SessionName) && SessionName.Length >= 3;
            bool outputValid = !string.IsNullOrEmpty(OutputFolder) && Directory.Exists(OutputFolder);
            bool ffmpegValid = !string.IsNullOrEmpty(FfmpegPath) && File.Exists(FfmpegPath);

            int completedSteps = (sessionValid ? 1 : 0) + (outputValid ? 1 : 0) + (ffmpegValid ? 1 : 0);

            if (completedSteps == 3)
            {
                lblStatus.Text = "✅ Setup complete - Ready to continue!";
                lblStatus.ForeColor = Color.FromArgb(0, 200, 100);
                btnContinue.Enabled = true;
            }
            else
            {
                lblStatus.Text = $"⚠ {completedSteps}/3 steps completed - Please finish setup";
                lblStatus.ForeColor = Color.Orange;
                btnContinue.Enabled = false;
            }

            // Update title colors
            lblSessionTitle.ForeColor = sessionValid ? Color.FromArgb(0, 200, 100) : Color.FromArgb(100, 100, 100);
            lblOutputTitle.ForeColor = outputValid ? Color.FromArgb(0, 200, 100) : Color.FromArgb(100, 100, 100);
            lblFfmpegTitle.ForeColor = ffmpegValid ? Color.FromArgb(0, 200, 100) : Color.FromArgb(100, 100, 100);
        }

        private bool ValidateSetup()
        {
            if (string.IsNullOrWhiteSpace(SessionName) || SessionName.Length < 3)
            {
                MessageBox.Show("Session name must be at least 3 characters.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtSessionName.Focus();
                return false;
            }

            if (string.IsNullOrEmpty(OutputFolder) || !Directory.Exists(OutputFolder))
            {
                MessageBox.Show("Please select a valid output folder.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnBrowseOutput.Focus();
                return false;
            }

            if (string.IsNullOrEmpty(FfmpegPath) || !File.Exists(FfmpegPath))
            {
                MessageBox.Show("Please select or download FFmpeg.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        private void LoadExistingSettings()
        {
            // Try to auto-detect FFmpeg if not provided
            if (string.IsNullOrEmpty(FfmpegPath))
            {
                string? foundPath = FfmpegRunner.FindFfmpeg(null);
                if (!string.IsNullOrEmpty(foundPath))
                {
                    FfmpegPath = foundPath;
                    if (txtFfmpegPath != null)
                        txtFfmpegPath.Text = FfmpegPath;
                }
            }

            // Generate default session name
            if (string.IsNullOrEmpty(SessionName))
            {
                txtSessionName.Text = $"Session_{DateTime.Now:yyyy-MM-dd_HHmm}";
            }
        }

        #endregion
    }
}
