using System.Drawing;
using System.Windows.Forms;

namespace TimelapseCapture
{
    partial class MainForm
    {
        #region Component Designer Generated Code

        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        #region Control Declarations

        // === Buttons ===
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Button btnSelectRegion;
        private System.Windows.Forms.Button btnFullScreen;
        private System.Windows.Forms.Button btnChooseFolder;
        private System.Windows.Forms.Button btnEncode;
        private System.Windows.Forms.Button btnOpenFolder;
        private System.Windows.Forms.Button btnBrowseFfmpeg;
        private System.Windows.Forms.Button btnDownloadFfmpeg;
        private System.Windows.Forms.Button btnNewSession;
        private System.Windows.Forms.Button btnLoadSession;
        private System.Windows.Forms.Button btnShowRegion;

        // === Labels ===
        private System.Windows.Forms.Label lblSessionNameText;
        private System.Windows.Forms.Label lblRegion;
        private System.Windows.Forms.Label lblFullScreenInfo;
        private System.Windows.Forms.Label lblFolder;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Label lblQuality;
        private System.Windows.Forms.Label lblEstimate;
        private System.Windows.Forms.Label lblIntervalText;
        private System.Windows.Forms.Label lblFormatText;
        private System.Windows.Forms.Label lblFfmpegText;
        private System.Windows.Forms.Label lblAspectRatioText;

        // === Text Boxes ===
        private System.Windows.Forms.TextBox txtSessionName;
        private System.Windows.Forms.TextBox txtFfmpegPath;

        // === Numeric Controls ===
        private System.Windows.Forms.NumericUpDown numInterval;
        private System.Windows.Forms.NumericUpDown numQuality;
        private System.Windows.Forms.NumericUpDown numDesiredSec;

        // === Combo Boxes ===
        private System.Windows.Forms.ComboBox cmbFormat;
        private System.Windows.Forms.ComboBox cmbAspectRatio;

        // === Other Controls ===
        private System.Windows.Forms.TrackBar trkQuality;

        // === Group Boxes ===
        private System.Windows.Forms.GroupBox grpCaptureSettings;
        private System.Windows.Forms.GroupBox grpSession;
        private System.Windows.Forms.GroupBox grpOutput;
        private System.Windows.Forms.GroupBox grpReadiness;
        private System.Windows.Forms.GroupBox grpSessionInfo;
        private System.Windows.Forms.GroupBox grpEncodingSettings;

        // === Readiness Panel Labels ===
        private System.Windows.Forms.Label? lblReadiness1;
        private System.Windows.Forms.Label? lblReadiness2;
        private System.Windows.Forms.Label? lblReadiness3;
        private System.Windows.Forms.Label? lblReadiness4;
        private System.Windows.Forms.Label? lblReadiness5;
        private System.Windows.Forms.Label? lblReadiness6;

        // === Session Info Panel Labels ===
        private System.Windows.Forms.Label lblSessionInfoRegion;
        private System.Windows.Forms.Label lblSessionInfoFormat;
        private System.Windows.Forms.Label lblSessionInfoQuality;
        private System.Windows.Forms.Label lblSessionInfoInterval;
        
        // === Encoding Settings Controls ===
        private System.Windows.Forms.Label lblFrameRateText;
        private System.Windows.Forms.ComboBox cmbFrameRate;
        private System.Windows.Forms.NumericUpDown numCustomFrameRate;
        private System.Windows.Forms.Label lblEncodingPresetText;
        private System.Windows.Forms.ComboBox cmbEncodingPreset;
        private System.Windows.Forms.Label lblVideoCodecText;
        private System.Windows.Forms.ComboBox cmbVideoCodec;

        #endregion

        #region Color Palette (Design Reference)

        // Background: #141414 (20, 20, 20)
        // Foreground: #C8C8C8 (200, 200, 200)
        // Primary Button: #007ACC (0, 122, 204)
        // Stop Button: #C00000 (192, 0, 0)
        // Encode Button: #107C10 (16, 124, 16)
        // Accent Green: #00C864 (0, 200, 100)

        #endregion

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            btnStart = new Button();
            btnStop = new Button();
            btnSelectRegion = new Button();
            btnFullScreen = new Button();
            btnChooseFolder = new Button();
            btnEncode = new Button();
            btnOpenFolder = new Button();
            btnBrowseFfmpeg = new Button();
            btnDownloadFfmpeg = new Button();
            btnNewSession = new Button();
            btnLoadSession = new Button();
            btnShowRegion = new Button();
            lblSessionNameText = new Label();
            lblRegion = new Label();
            lblFullScreenInfo = new Label();
            lblFolder = new Label();
            lblStatus = new Label();
            lblQuality = new Label();
            lblEstimate = new Label();
            lblIntervalText = new Label();
            lblFormatText = new Label();
            lblFfmpegText = new Label();
            lblAspectRatioText = new Label();
            txtSessionName = new TextBox();
            txtFfmpegPath = new TextBox();
            numInterval = new NumericUpDown();
            numQuality = new NumericUpDown();
            numDesiredSec = new NumericUpDown();
            cmbFormat = new ComboBox();
            cmbAspectRatio = new ComboBox();
            trkQuality = new TrackBar();
            grpCaptureSettings = new GroupBox();
            grpSession = new GroupBox();
            grpOutput = new GroupBox();
            grpReadiness = new GroupBox();
            lblReadiness1 = new Label();
            lblReadiness2 = new Label();
            lblReadiness3 = new Label();
            lblReadiness4 = new Label();
            lblReadiness5 = new Label();
            lblReadiness6 = new Label();
            grpSessionInfo = new GroupBox();
            lblSessionInfoRegion = new Label();
            lblSessionInfoFormat = new Label();
            lblSessionInfoQuality = new Label();
            lblSessionInfoInterval = new Label();
            grpEncodingSettings = new GroupBox();
            lblFrameRateText = new Label();
            cmbFrameRate = new ComboBox();
            numCustomFrameRate = new NumericUpDown();
            lblEncodingPresetText = new Label();
            cmbEncodingPreset = new ComboBox();
            lblVideoCodecText = new Label();
            cmbVideoCodec = new ComboBox();
            ((System.ComponentModel.ISupportInitialize)numInterval).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numQuality).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numDesiredSec).BeginInit();
            ((System.ComponentModel.ISupportInitialize)trkQuality).BeginInit();
            grpCaptureSettings.SuspendLayout();
            grpSession.SuspendLayout();
            grpOutput.SuspendLayout();
            grpReadiness.SuspendLayout();
            grpSessionInfo.SuspendLayout();
            grpEncodingSettings.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numCustomFrameRate).BeginInit();
            SuspendLayout();
            // 
            // btnStart
            // 
            btnStart.BackColor = Color.FromArgb(0, 122, 204);
            btnStart.FlatStyle = FlatStyle.Flat;
            btnStart.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnStart.ForeColor = Color.White;
            btnStart.Location = new Point(15, 95);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(200, 32);
            btnStart.TabIndex = 2;
            btnStart.Text = "▶ Start Capture";
            btnStart.UseVisualStyleBackColor = false;
            btnStart.Click += btnStart_Click;
            // 
            // btnStop
            // 
            btnStop.BackColor = Color.FromArgb(192, 0, 0);
            btnStop.Enabled = false;
            btnStop.FlatStyle = FlatStyle.Flat;
            btnStop.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnStop.ForeColor = Color.White;
            btnStop.Location = new Point(225, 95);
            btnStop.Name = "btnStop";
            btnStop.Size = new Size(200, 32);
            btnStop.TabIndex = 3;
            btnStop.Text = "⏹ Stop";
            btnStop.UseVisualStyleBackColor = false;
            btnStop.Click += btnStop_Click;
            // 
            // btnSelectRegion
            // 
            btnSelectRegion.FlatStyle = FlatStyle.Flat;
            btnSelectRegion.ForeColor = Color.OrangeRed;
            btnSelectRegion.Location = new Point(15, 66);
            btnSelectRegion.Name = "btnSelectRegion";
            btnSelectRegion.Size = new Size(110, 32);
            btnSelectRegion.TabIndex = 3;
            btnSelectRegion.Text = "📐 Select";
            btnSelectRegion.UseVisualStyleBackColor = true;
            btnSelectRegion.Click += btnSelectRegion_Click;
            // 
            // btnFullScreen
            // 
            btnFullScreen.FlatStyle = FlatStyle.Flat;
            btnFullScreen.ForeColor = Color.DeepSkyBlue;
            btnFullScreen.Location = new Point(131, 66);
            btnFullScreen.Name = "btnFullScreen";
            btnFullScreen.Size = new Size(140, 32);
            btnFullScreen.TabIndex = 5;
            btnFullScreen.Text = "🖥️ Full Screen ▼";
            btnFullScreen.UseVisualStyleBackColor = true;
            btnFullScreen.Click += btnFullScreen_Click;
            // 
            // btnChooseFolder
            // 
            btnChooseFolder.FlatStyle = FlatStyle.Flat;
            btnChooseFolder.ForeColor = Color.RoyalBlue;
            btnChooseFolder.Location = new Point(15, 25);
            btnChooseFolder.Name = "btnChooseFolder";
            btnChooseFolder.Size = new Size(130, 32);
            btnChooseFolder.TabIndex = 0;
            btnChooseFolder.Text = "📁 Choose Folder";
            btnChooseFolder.UseVisualStyleBackColor = true;
            btnChooseFolder.Click += btnChooseFolder_Click;
            // 
            // btnEncode
            // 
            btnEncode.BackColor = Color.FromArgb(16, 124, 16);
            btnEncode.FlatStyle = FlatStyle.Flat;
            btnEncode.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnEncode.ForeColor = Color.White;
            btnEncode.Location = new Point(15, 130);
            btnEncode.Name = "btnEncode";
            btnEncode.Size = new Size(150, 30);
            btnEncode.TabIndex = 5;
            btnEncode.Text = "🎬 Encode Video";
            btnEncode.UseVisualStyleBackColor = false;
            btnEncode.Click += btnEncode_Click;
            // 
            // btnOpenFolder
            // 
            btnOpenFolder.FlatStyle = FlatStyle.Flat;
            btnOpenFolder.Location = new Point(175, 130);
            btnOpenFolder.Name = "btnOpenFolder";
            btnOpenFolder.Size = new Size(150, 30);
            btnOpenFolder.TabIndex = 6;
            btnOpenFolder.Text = "📂 Open Folder";
            btnOpenFolder.UseVisualStyleBackColor = true;
            btnOpenFolder.Click += btnOpenFolder_Click;
            // 
            // btnBrowseFfmpeg
            // 
            btnBrowseFfmpeg.FlatStyle = FlatStyle.Flat;
            btnBrowseFfmpeg.Location = new Point(331, 94);
            btnBrowseFfmpeg.Name = "btnBrowseFfmpeg";
            btnBrowseFfmpeg.Size = new Size(80, 25);
            btnBrowseFfmpeg.TabIndex = 5;
            btnBrowseFfmpeg.Text = "Browse...";
            btnBrowseFfmpeg.UseVisualStyleBackColor = true;
            btnBrowseFfmpeg.Click += btnBrowseFfmpeg_Click;
            // 
            // btnDownloadFfmpeg
            // 
            btnDownloadFfmpeg.FlatStyle = FlatStyle.Flat;
            btnDownloadFfmpeg.ForeColor = Color.LimeGreen;
            btnDownloadFfmpeg.Location = new Point(287, 63);
            btnDownloadFfmpeg.Name = "btnDownloadFfmpeg";
            btnDownloadFfmpeg.Size = new Size(125, 25);
            btnDownloadFfmpeg.TabIndex = 4;
            btnDownloadFfmpeg.Text = "⬇ Download FFmpeg";
            btnDownloadFfmpeg.UseVisualStyleBackColor = true;
            btnDownloadFfmpeg.Click += btnDownloadFfmpeg_Click;
            // 
            // btnNewSession
            // 
            btnNewSession.FlatStyle = FlatStyle.Flat;
            btnNewSession.ForeColor = Color.FromArgb(0, 200, 100);
            btnNewSession.Location = new Point(300, 24);
            btnNewSession.Name = "btnNewSession";
            btnNewSession.Size = new Size(84, 32);
            btnNewSession.TabIndex = 2;
            btnNewSession.Text = "🆕 New";
            btnNewSession.UseVisualStyleBackColor = true;
            btnNewSession.Click += btnNewSession_Click;
            // 
            // btnLoadSession
            // 
            btnLoadSession.FlatStyle = FlatStyle.Flat;
            btnLoadSession.ForeColor = Color.DodgerBlue;
            btnLoadSession.Location = new Point(341, 24);
            btnLoadSession.Name = "btnLoadSession";
            btnLoadSession.Size = new Size(84, 32);
            btnLoadSession.TabIndex = 3;
            btnLoadSession.Text = "📂 Load";
            btnLoadSession.UseVisualStyleBackColor = true;
            btnLoadSession.Click += btnLoadSession_Click;
            // 
            // btnShowRegion
            // 
            btnShowRegion.FlatStyle = FlatStyle.Flat;
            btnShowRegion.ForeColor = Color.MediumPurple;
            btnShowRegion.Location = new Point(284, 66);
            btnShowRegion.Name = "btnShowRegion";
            btnShowRegion.Size = new Size(110, 32);
            btnShowRegion.TabIndex = 4;
            btnShowRegion.Text = "👁 Show";
            btnShowRegion.UseVisualStyleBackColor = true;
            btnShowRegion.Click += btnShowRegion_Click;
            // 
            // lblSessionNameText
            // 
            lblSessionNameText.AutoSize = true;
            lblSessionNameText.Location = new Point(15, 31);
            lblSessionNameText.Name = "lblSessionNameText";
            lblSessionNameText.Size = new Size(84, 15);
            lblSessionNameText.TabIndex = 0;
            lblSessionNameText.Text = "Session Name:";
            // 
            // lblRegion
            // 
            lblRegion.Location = new Point(15, 111);
            lblRegion.Name = "lblRegion";
            lblRegion.Size = new Size(430, 25);
            lblRegion.TabIndex = 5;
            lblRegion.Text = "No region selected";
            // 
            // lblFullScreenInfo
            // 
            lblFullScreenInfo.Location = new Point(287, 66);
            lblFullScreenInfo.Name = "lblFullScreenInfo";
            lblFullScreenInfo.Size = new Size(158, 32);
            lblFullScreenInfo.TabIndex = 6;
            lblFullScreenInfo.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // lblFolder
            // 
            lblFolder.AutoSize = true;
            lblFolder.Location = new Point(160, 33);
            lblFolder.Name = "lblFolder";
            lblFolder.Size = new Size(103, 15);
            lblFolder.TabIndex = 1;
            lblFolder.Text = "No folder selected";
            // 
            // lblStatus
            // 
            lblStatus.AutoSize = true;
            lblStatus.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            lblStatus.Location = new Point(15, 25);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(147, 15);
            lblStatus.TabIndex = 0;
            lblStatus.Text = "Ready - No active session";
            // 
            // lblQuality
            // 
            lblQuality.AutoSize = true;
            lblQuality.Location = new Point(15, 235);
            lblQuality.Name = "lblQuality";
            lblQuality.Size = new Size(91, 15);
            lblQuality.TabIndex = 12;
            lblQuality.Text = "JPEG Quality: 90";
            lblQuality.Click += lblQuality_Click;
            // 
            // lblEstimate
            // 
            lblEstimate.Location = new Point(15, 50);
            lblEstimate.Name = "lblEstimate";
            lblEstimate.Size = new Size(430, 42);
            lblEstimate.TabIndex = 1;
            lblEstimate.Text = "No frames captured yet";
            // 
            // lblIntervalText
            // 
            lblIntervalText.AutoSize = true;
            lblIntervalText.Location = new Point(15, 195);
            lblIntervalText.Name = "lblIntervalText";
            lblIntervalText.Size = new Size(103, 15);
            lblIntervalText.TabIndex = 8;
            lblIntervalText.Text = "Interval (seconds):";
            // 
            // lblFormatText
            // 
            lblFormatText.AutoSize = true;
            lblFormatText.Location = new Point(230, 195);
            lblFormatText.Name = "lblFormatText";
            lblFormatText.Size = new Size(48, 15);
            lblFormatText.TabIndex = 10;
            lblFormatText.Text = "Format:";
            // 
            // lblFfmpegText
            // 
            lblFfmpegText.AutoSize = true;
            lblFfmpegText.Location = new Point(15, 75);
            lblFfmpegText.Name = "lblFfmpegText";
            lblFfmpegText.Size = new Size(80, 15);
            lblFfmpegText.TabIndex = 2;
            lblFfmpegText.Text = "FFmpeg Path:";
            // 
            // lblAspectRatioText
            // 
            lblAspectRatioText.AutoSize = true;
            lblAspectRatioText.Location = new Point(15, 151);
            lblAspectRatioText.Name = "lblAspectRatioText";
            lblAspectRatioText.Size = new Size(76, 15);
            lblAspectRatioText.TabIndex = 6;
            lblAspectRatioText.Text = "Aspect Ratio:";
            // 
            // txtSessionName
            // 
            txtSessionName.BackColor = SystemColors.InactiveCaptionText;
            txtSessionName.BorderStyle = BorderStyle.FixedSingle;
            txtSessionName.ForeColor = SystemColors.ScrollBar;
            txtSessionName.Location = new Point(110, 29);
            txtSessionName.Name = "txtSessionName";
            txtSessionName.ReadOnly = true;
            txtSessionName.Size = new Size(178, 23);
            txtSessionName.TabIndex = 1;
            txtSessionName.Text = "No active session";
            // 
            // txtFfmpegPath
            // 
            txtFfmpegPath.BackColor = SystemColors.MenuText;
            txtFfmpegPath.BorderStyle = BorderStyle.FixedSingle;
            txtFfmpegPath.ForeColor = SystemColors.ScrollBar;
            txtFfmpegPath.Location = new Point(15, 97);
            txtFfmpegPath.Name = "txtFfmpegPath";
            txtFfmpegPath.ReadOnly = true;
            txtFfmpegPath.Size = new Size(310, 23);
            txtFfmpegPath.TabIndex = 3;
            // 
            // numInterval
            // 
            numInterval.BackColor = SystemColors.InactiveCaptionText;
            numInterval.BorderStyle = BorderStyle.FixedSingle;
            numInterval.ForeColor = SystemColors.ScrollBar;
            numInterval.Location = new Point(124, 192);
            numInterval.Maximum = new decimal(new int[] { 3600, 0, 0, 0 });
            numInterval.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numInterval.Name = "numInterval";
            numInterval.Size = new Size(80, 23);
            numInterval.TabIndex = 9;
            numInterval.Value = new decimal(new int[] { 5, 0, 0, 0 });
            numInterval.ValueChanged += numInterval_ValueChanged;
            // 
            // numQuality
            // 
            numQuality.BackColor = SystemColors.InactiveCaptionText;
            numQuality.ForeColor = SystemColors.ScrollBar;
            numQuality.Location = new Point(355, 268);
            numQuality.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numQuality.Name = "numQuality";
            numQuality.Size = new Size(70, 23);
            numQuality.TabIndex = 14;
            numQuality.Value = new decimal(new int[] { 90, 0, 0, 0 });
            numQuality.ValueChanged += numQuality_ValueChanged;
            // 
            // numDesiredSec
            // 
            numDesiredSec.Location = new Point(0, 0);
            numDesiredSec.Name = "numDesiredSec";
            numDesiredSec.Size = new Size(0, 23);
            numDesiredSec.TabIndex = 0;
            numDesiredSec.Visible = false;
            // 
            // cmbFormat
            // 
            cmbFormat.BackColor = SystemColors.InactiveCaptionText;
            cmbFormat.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbFormat.FlatStyle = FlatStyle.Popup;
            cmbFormat.ForeColor = SystemColors.ScrollBar;
            cmbFormat.FormattingEnabled = true;
            cmbFormat.Items.AddRange(new object[] { "JPEG", "PNG", "BMP" });
            cmbFormat.Location = new Point(284, 192);
            cmbFormat.Name = "cmbFormat";
            cmbFormat.Size = new Size(100, 23);
            cmbFormat.TabIndex = 11;
            cmbFormat.SelectedIndexChanged += cmbFormat_SelectedIndexChanged;
            // 
            // cmbAspectRatio
            // 
            cmbAspectRatio.BackColor = SystemColors.InactiveCaptionText;
            cmbAspectRatio.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbAspectRatio.FlatStyle = FlatStyle.Popup;
            cmbAspectRatio.ForeColor = SystemColors.ScrollBar;
            cmbAspectRatio.FormattingEnabled = true;
            cmbAspectRatio.Location = new Point(110, 148);
            cmbAspectRatio.Name = "cmbAspectRatio";
            cmbAspectRatio.Size = new Size(134, 23);
            cmbAspectRatio.TabIndex = 7;
            cmbAspectRatio.SelectedIndexChanged += cmbAspectRatio_SelectedIndexChanged;
            // 
            // trkQuality
            // 
            trkQuality.Location = new Point(15, 268);
            trkQuality.Maximum = 100;
            trkQuality.Minimum = 1;
            trkQuality.Name = "trkQuality";
            trkQuality.Size = new Size(340, 45);
            trkQuality.TabIndex = 13;
            trkQuality.TickFrequency = 5;
            trkQuality.Value = 90;
            trkQuality.Scroll += trkQuality_Scroll;
            // 
            // grpCaptureSettings
            // 
            grpCaptureSettings.Controls.Add(lblSessionNameText);
            grpCaptureSettings.Controls.Add(txtSessionName);
            grpCaptureSettings.Controls.Add(btnNewSession);
            grpCaptureSettings.Controls.Add(btnLoadSession);
            grpCaptureSettings.Controls.Add(btnSelectRegion);
            grpCaptureSettings.Controls.Add(btnShowRegion);
            grpCaptureSettings.Controls.Add(btnFullScreen);
            grpCaptureSettings.Controls.Add(lblRegion);
            grpCaptureSettings.Controls.Add(lblFullScreenInfo);
            grpCaptureSettings.Controls.Add(lblAspectRatioText);
            grpCaptureSettings.Controls.Add(cmbAspectRatio);
            grpCaptureSettings.Controls.Add(lblIntervalText);
            grpCaptureSettings.Controls.Add(numInterval);
            grpCaptureSettings.Controls.Add(lblFormatText);
            grpCaptureSettings.Controls.Add(cmbFormat);
            grpCaptureSettings.Controls.Add(lblQuality);
            grpCaptureSettings.Controls.Add(trkQuality);
            grpCaptureSettings.Controls.Add(numQuality);
            grpCaptureSettings.ForeColor = Color.LightGray;
            grpCaptureSettings.Location = new Point(15, 8);
            grpCaptureSettings.Name = "grpCaptureSettings";
            grpCaptureSettings.Size = new Size(445, 322);
            grpCaptureSettings.TabIndex = 0;
            grpCaptureSettings.TabStop = false;
            grpCaptureSettings.Text = "Capture Settings";
            // 
            // grpSession
            // 
            grpSession.Controls.Add(lblStatus);
            grpSession.Controls.Add(lblEstimate);
            grpSession.Controls.Add(btnStart);
            grpSession.Controls.Add(btnStop);
            grpSession.ForeColor = Color.LightGray;
            grpSession.Location = new Point(15, 340);
            grpSession.Name = "grpSession";
            grpSession.Size = new Size(445, 135);
            grpSession.TabIndex = 1;
            grpSession.TabStop = false;
            grpSession.Text = "Session";
            // 
            // grpOutput
            // 
            grpOutput.Controls.Add(btnChooseFolder);
            grpOutput.Controls.Add(lblFolder);
            grpOutput.Controls.Add(lblFfmpegText);
            grpOutput.Controls.Add(txtFfmpegPath);
            grpOutput.Controls.Add(btnDownloadFfmpeg);
            grpOutput.Controls.Add(btnBrowseFfmpeg);
            grpOutput.Controls.Add(btnEncode);
            grpOutput.Controls.Add(btnOpenFolder);
            grpOutput.ForeColor = Color.LightGray;
            grpOutput.Location = new Point(15, 485);
            grpOutput.Name = "grpOutput";
            grpOutput.Size = new Size(440, 175);
            grpOutput.TabIndex = 2;
            grpOutput.TabStop = false;
            grpOutput.Text = "Output";
            // 
            // grpReadiness
            // 
            grpReadiness.Controls.Add(lblReadiness1);
            grpReadiness.Controls.Add(lblReadiness2);
            grpReadiness.Controls.Add(lblReadiness3);
            grpReadiness.Controls.Add(lblReadiness4);
            grpReadiness.Controls.Add(lblReadiness5);
            grpReadiness.Controls.Add(lblReadiness6);
            grpReadiness.ForeColor = Color.LightGray;
            grpReadiness.Location = new Point(490, 8);
            grpReadiness.Name = "grpReadiness";
            grpReadiness.Size = new Size(280, 140);
            grpReadiness.TabIndex = 3;
            grpReadiness.TabStop = false;
            grpReadiness.Text = "⚡ READINESS";
            // 
            // lblReadiness1
            // 
            lblReadiness1.AutoSize = true;
            lblReadiness1.Font = new Font("Segoe UI", 8.25F);
            lblReadiness1.ForeColor = Color.FromArgb(180, 180, 180);
            lblReadiness1.Location = new Point(10, 25);
            lblReadiness1.Name = "lblReadiness1";
            lblReadiness1.Size = new Size(120, 13);
            lblReadiness1.TabIndex = 0;
            lblReadiness1.Text = "📁 Output: Not configured";
            // 
            // lblReadiness2
            // 
            lblReadiness2.AutoSize = true;
            lblReadiness2.Font = new Font("Segoe UI", 8.25F);
            lblReadiness2.ForeColor = Color.FromArgb(180, 180, 180);
            lblReadiness2.Location = new Point(10, 45);
            lblReadiness2.Name = "lblReadiness2";
            lblReadiness2.Size = new Size(120, 13);
            lblReadiness2.TabIndex = 1;
            lblReadiness2.Text = "🎬 FFmpeg: Not configured";
            // 
            // lblReadiness3
            // 
            lblReadiness3.AutoSize = true;
            lblReadiness3.Font = new Font("Segoe UI", 8.25F);
            lblReadiness3.ForeColor = Color.FromArgb(180, 180, 180);
            lblReadiness3.Location = new Point(10, 65);
            lblReadiness3.Name = "lblReadiness3";
            lblReadiness3.Size = new Size(120, 13);
            lblReadiness3.TabIndex = 2;
            lblReadiness3.Text = "📋 Session: Not created";
            // 
            // lblReadiness4
            // 
            lblReadiness4.AutoSize = true;
            lblReadiness4.Font = new Font("Segoe UI", 8.25F);
            lblReadiness4.ForeColor = Color.FromArgb(180, 180, 180);
            lblReadiness4.Location = new Point(10, 85);
            lblReadiness4.Name = "lblReadiness4";
            lblReadiness4.Size = new Size(120, 13);
            lblReadiness4.TabIndex = 3;
            lblReadiness4.Text = "🎯 Region: Not selected";
            // 
            // lblReadiness5
            // 
            lblReadiness5.AutoSize = true;
            lblReadiness5.Font = new Font("Segoe UI", 8.25F);
            lblReadiness5.ForeColor = Color.FromArgb(180, 180, 180);
            lblReadiness5.Location = new Point(10, 105);
            lblReadiness5.Name = "lblReadiness5";
            lblReadiness5.Size = new Size(110, 13);
            lblReadiness5.TabIndex = 4;
            lblReadiness5.Text = "▶️ Capture: Not ready";
            // 
            // lblReadiness6
            // 
            lblReadiness6.AutoSize = true;
            lblReadiness6.Font = new Font("Segoe UI", 8.25F);
            lblReadiness6.ForeColor = Color.FromArgb(180, 180, 180);
            lblReadiness6.Location = new Point(155, 25);
            lblReadiness6.Name = "lblReadiness6";
            lblReadiness6.Size = new Size(110, 13);
            lblReadiness6.TabIndex = 5;
            lblReadiness6.Text = "🎬 Encode: Not ready";
            // 
            // grpSessionInfo
            // 
            grpSessionInfo.Controls.Add(lblSessionInfoRegion);
            grpSessionInfo.Controls.Add(lblSessionInfoFormat);
            grpSessionInfo.Controls.Add(lblSessionInfoQuality);
            grpSessionInfo.Controls.Add(lblSessionInfoInterval);
            grpSessionInfo.ForeColor = Color.LightGray;
            grpSessionInfo.Location = new Point(490, 158);
            grpSessionInfo.Name = "grpSessionInfo";
            grpSessionInfo.Size = new Size(280, 140);
            grpSessionInfo.TabIndex = 4;
            grpSessionInfo.TabStop = false;
            grpSessionInfo.Text = "📋 Session Settings (Locked)";
            // 
            // lblSessionInfoRegion
            // 
            lblSessionInfoRegion.ForeColor = Color.FromArgb(180, 180, 180);
            lblSessionInfoRegion.Location = new Point(10, 25);
            lblSessionInfoRegion.Name = "lblSessionInfoRegion";
            lblSessionInfoRegion.Size = new Size(260, 20);
            lblSessionInfoRegion.TabIndex = 0;
            lblSessionInfoRegion.Text = "Region: Not set";
            // 
            // lblSessionInfoFormat
            // 
            lblSessionInfoFormat.ForeColor = Color.FromArgb(180, 180, 180);
            lblSessionInfoFormat.Location = new Point(10, 52);
            lblSessionInfoFormat.Name = "lblSessionInfoFormat";
            lblSessionInfoFormat.Size = new Size(260, 20);
            lblSessionInfoFormat.TabIndex = 1;
            lblSessionInfoFormat.Text = "Format: Not set";
            // 
            // lblSessionInfoQuality
            // 
            lblSessionInfoQuality.ForeColor = Color.FromArgb(180, 180, 180);
            lblSessionInfoQuality.Location = new Point(10, 79);
            lblSessionInfoQuality.Name = "lblSessionInfoQuality";
            lblSessionInfoQuality.Size = new Size(260, 20);
            lblSessionInfoQuality.TabIndex = 2;
            lblSessionInfoQuality.Text = "Quality: Not set";
            // 
            // lblSessionInfoInterval
            // 
            lblSessionInfoInterval.ForeColor = Color.FromArgb(180, 180, 180);
            lblSessionInfoInterval.Location = new Point(10, 106);
            lblSessionInfoInterval.Name = "lblSessionInfoInterval";
            lblSessionInfoInterval.Size = new Size(260, 20);
            lblSessionInfoInterval.TabIndex = 3;
            lblSessionInfoInterval.Text = "Interval: Not set";
            // 
            // grpEncodingSettings
            // 
            grpEncodingSettings.Controls.Add(lblFrameRateText);
            grpEncodingSettings.Controls.Add(cmbFrameRate);
            grpEncodingSettings.Controls.Add(numCustomFrameRate);
            grpEncodingSettings.Controls.Add(lblEncodingPresetText);
            grpEncodingSettings.Controls.Add(cmbEncodingPreset);
            grpEncodingSettings.Controls.Add(lblVideoCodecText);
            grpEncodingSettings.Controls.Add(cmbVideoCodec);
            grpEncodingSettings.ForeColor = Color.LightGray;
            grpEncodingSettings.Location = new Point(490, 308);
            grpEncodingSettings.Name = "grpEncodingSettings";
            grpEncodingSettings.Size = new Size(280, 167);
            grpEncodingSettings.TabIndex = 5;
            grpEncodingSettings.TabStop = false;
            grpEncodingSettings.Text = "🎬 Encoding Settings";
            // 
            // lblFrameRateText
            // 
            lblFrameRateText.AutoSize = true;
            lblFrameRateText.Location = new Point(10, 25);
            lblFrameRateText.Name = "lblFrameRateText";
            lblFrameRateText.Size = new Size(69, 15);
            lblFrameRateText.TabIndex = 0;
            lblFrameRateText.Text = "Frame Rate:";
            // 
            // cmbFrameRate
            // 
            cmbFrameRate.BackColor = SystemColors.InactiveCaptionText;
            cmbFrameRate.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbFrameRate.FlatStyle = FlatStyle.Popup;
            cmbFrameRate.ForeColor = SystemColors.ScrollBar;
            cmbFrameRate.FormattingEnabled = true;
            cmbFrameRate.Items.AddRange(new object[] { "24 fps (Film)", "25 fps (PAL)", "30 fps (NTSC)", "60 fps (Smooth)", "Custom..." });
            cmbFrameRate.Location = new Point(10, 45);
            cmbFrameRate.Name = "cmbFrameRate";
            cmbFrameRate.Size = new Size(140, 23);
            cmbFrameRate.TabIndex = 1;
            cmbFrameRate.SelectedIndexChanged += cmbFrameRate_SelectedIndexChanged;
            // 
            // numCustomFrameRate
            // 
            numCustomFrameRate.BackColor = SystemColors.InactiveCaptionText;
            numCustomFrameRate.BorderStyle = BorderStyle.FixedSingle;
            numCustomFrameRate.ForeColor = SystemColors.ScrollBar;
            numCustomFrameRate.Location = new Point(160, 45);
            numCustomFrameRate.Maximum = new decimal(new int[] { 120, 0, 0, 0 });
            numCustomFrameRate.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numCustomFrameRate.Name = "numCustomFrameRate";
            numCustomFrameRate.Size = new Size(110, 23);
            numCustomFrameRate.TabIndex = 2;
            numCustomFrameRate.Value = new decimal(new int[] { 30, 0, 0, 0 });
            numCustomFrameRate.Visible = false;
            numCustomFrameRate.ValueChanged += numCustomFrameRate_ValueChanged;
            // 
            // lblEncodingPresetText
            // 
            lblEncodingPresetText.AutoSize = true;
            lblEncodingPresetText.Location = new Point(10, 78);
            lblEncodingPresetText.Name = "lblEncodingPresetText";
            lblEncodingPresetText.Size = new Size(42, 15);
            lblEncodingPresetText.TabIndex = 3;
            lblEncodingPresetText.Text = "Preset:";
            // 
            // cmbEncodingPreset
            // 
            cmbEncodingPreset.BackColor = SystemColors.InactiveCaptionText;
            cmbEncodingPreset.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbEncodingPreset.FlatStyle = FlatStyle.Popup;
            cmbEncodingPreset.ForeColor = SystemColors.ScrollBar;
            cmbEncodingPreset.FormattingEnabled = true;
            cmbEncodingPreset.Items.AddRange(new object[] { "Ultrafast (Fast encode, large file)", "Fast (Balanced)", "Medium (Good quality)", "Slow (Best quality, slow)" });
            cmbEncodingPreset.Location = new Point(10, 98);
            cmbEncodingPreset.Name = "cmbEncodingPreset";
            cmbEncodingPreset.Size = new Size(260, 23);
            cmbEncodingPreset.TabIndex = 4;
            // 
            // lblVideoCodecText
            // 
            lblVideoCodecText.AutoSize = true;
            lblVideoCodecText.Location = new Point(10, 131);
            lblVideoCodecText.Name = "lblVideoCodecText";
            lblVideoCodecText.Size = new Size(44, 15);
            lblVideoCodecText.TabIndex = 5;
            lblVideoCodecText.Text = "Codec:";
            lblVideoCodecText.Visible = false;
            // 
            // cmbVideoCodec
            // 
            cmbVideoCodec.BackColor = SystemColors.InactiveCaptionText;
            cmbVideoCodec.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbVideoCodec.FlatStyle = FlatStyle.Popup;
            cmbVideoCodec.ForeColor = SystemColors.ScrollBar;
            cmbVideoCodec.FormattingEnabled = true;
            cmbVideoCodec.Items.AddRange(new object[] { "H.264 (Best compatibility)", "H.265 (Smaller files)" });
            cmbVideoCodec.Location = new Point(65, 128);
            cmbVideoCodec.Name = "cmbVideoCodec";
            cmbVideoCodec.Size = new Size(205, 23);
            cmbVideoCodec.TabIndex = 6;
            cmbVideoCodec.Visible = false;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(20, 20, 20);
            ClientSize = new Size(785, 690);
            Controls.Add(grpEncodingSettings);
            Controls.Add(grpSessionInfo);
            Controls.Add(grpReadiness);
            Controls.Add(grpOutput);
            Controls.Add(grpSession);
            Controls.Add(grpCaptureSettings);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.FromArgb(200, 200, 200);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Timelapse Capture";
            FormClosing += MainForm_FormClosing;
            ((System.ComponentModel.ISupportInitialize)numInterval).EndInit();
            ((System.ComponentModel.ISupportInitialize)numQuality).EndInit();
            ((System.ComponentModel.ISupportInitialize)numDesiredSec).EndInit();
            ((System.ComponentModel.ISupportInitialize)trkQuality).EndInit();
            grpCaptureSettings.ResumeLayout(false);
            grpCaptureSettings.PerformLayout();
            grpSession.ResumeLayout(false);
            grpSession.PerformLayout();
            grpOutput.ResumeLayout(false);
            grpOutput.PerformLayout();
            grpReadiness.ResumeLayout(false);
            grpReadiness.PerformLayout();
            grpSessionInfo.ResumeLayout(false);
            grpEncodingSettings.ResumeLayout(false);
            grpEncodingSettings.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numCustomFrameRate).EndInit();
            ResumeLayout(false);
        }

        #endregion

        #endregion
    }
}