using System.Drawing;

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
        private System.Windows.Forms.Button btnChooseFolder;
        private System.Windows.Forms.Button btnEncode;
        private System.Windows.Forms.Button btnOpenFolder;
        private System.Windows.Forms.Button btnBrowseFfmpeg;
        private System.Windows.Forms.Button btnNewSession;

        // === Labels ===
        private System.Windows.Forms.Label lblSessionNameText;
        private System.Windows.Forms.Label lblRegion;
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
            // === Initialize all controls ===
            this.btnStart = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnSelectRegion = new System.Windows.Forms.Button();
            this.btnChooseFolder = new System.Windows.Forms.Button();
            this.btnEncode = new System.Windows.Forms.Button();
            this.btnOpenFolder = new System.Windows.Forms.Button();
            this.btnBrowseFfmpeg = new System.Windows.Forms.Button();
            this.btnNewSession = new System.Windows.Forms.Button();

            this.lblSessionNameText = new System.Windows.Forms.Label();
            this.lblRegion = new System.Windows.Forms.Label();
            this.lblFolder = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.lblQuality = new System.Windows.Forms.Label();
            this.lblEstimate = new System.Windows.Forms.Label();
            this.lblIntervalText = new System.Windows.Forms.Label();
            this.lblFormatText = new System.Windows.Forms.Label();
            this.lblFfmpegText = new System.Windows.Forms.Label();
            this.lblAspectRatioText = new System.Windows.Forms.Label();

            this.txtSessionName = new System.Windows.Forms.TextBox();
            this.txtFfmpegPath = new System.Windows.Forms.TextBox();

            this.numInterval = new System.Windows.Forms.NumericUpDown();
            this.numQuality = new System.Windows.Forms.NumericUpDown();
            this.numDesiredSec = new System.Windows.Forms.NumericUpDown();

            this.cmbFormat = new System.Windows.Forms.ComboBox();
            this.cmbAspectRatio = new System.Windows.Forms.ComboBox();

            this.trkQuality = new System.Windows.Forms.TrackBar();

            this.grpCaptureSettings = new System.Windows.Forms.GroupBox();
            this.grpSession = new System.Windows.Forms.GroupBox();
            this.grpOutput = new System.Windows.Forms.GroupBox();

            ((System.ComponentModel.ISupportInitialize)(this.numInterval)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numQuality)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numDesiredSec)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkQuality)).BeginInit();
            this.grpCaptureSettings.SuspendLayout();
            this.grpSession.SuspendLayout();
            this.grpOutput.SuspendLayout();
            this.SuspendLayout();

            // ================================================================
            // CAPTURE SETTINGS GROUP
            // ================================================================

            // 
            // grpCaptureSettings
            // 
            this.grpCaptureSettings.Controls.Add(this.lblSessionNameText);
            this.grpCaptureSettings.Controls.Add(this.txtSessionName);
            this.grpCaptureSettings.Controls.Add(this.btnNewSession);
            this.grpCaptureSettings.Controls.Add(this.btnSelectRegion);
            this.grpCaptureSettings.Controls.Add(this.lblRegion);
            this.grpCaptureSettings.Controls.Add(this.lblAspectRatioText);
            this.grpCaptureSettings.Controls.Add(this.cmbAspectRatio);
            this.grpCaptureSettings.Controls.Add(this.lblIntervalText);
            this.grpCaptureSettings.Controls.Add(this.numInterval);
            this.grpCaptureSettings.Controls.Add(this.lblFormatText);
            this.grpCaptureSettings.Controls.Add(this.cmbFormat);
            this.grpCaptureSettings.Controls.Add(this.lblQuality);
            this.grpCaptureSettings.Controls.Add(this.trkQuality);
            this.grpCaptureSettings.Controls.Add(this.numQuality);
            this.grpCaptureSettings.ForeColor = Color.LightGray;
            this.grpCaptureSettings.Location = new Point(15, 15);
            this.grpCaptureSettings.Name = "grpCaptureSettings";
            this.grpCaptureSettings.Size = new Size(460, 280);
            this.grpCaptureSettings.TabIndex = 0;
            this.grpCaptureSettings.TabStop = false;
            this.grpCaptureSettings.Text = "Capture Settings";

            // --- Session Name Row ---

            // 
            // lblSessionNameText
            // 
            this.lblSessionNameText.AutoSize = true;
            this.lblSessionNameText.Location = new Point(15, 25);
            this.lblSessionNameText.Name = "lblSessionNameText";
            this.lblSessionNameText.Size = new Size(85, 15);
            this.lblSessionNameText.TabIndex = 0;
            this.lblSessionNameText.Text = "Session Name:";

            // 
            // txtSessionName
            // 
            this.txtSessionName.BackColor = SystemColors.InactiveCaptionText;
            this.txtSessionName.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtSessionName.ForeColor = SystemColors.ScrollBar;
            this.txtSessionName.Location = new Point(110, 23);
            this.txtSessionName.Name = "txtSessionName";
            this.txtSessionName.ReadOnly = true;
            this.txtSessionName.Size = new Size(240, 23);
            this.txtSessionName.TabIndex = 1;
            this.txtSessionName.Text = "No active session";

            // 
            // btnNewSession
            // 
            this.btnNewSession.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNewSession.ForeColor = Color.FromArgb(0, 200, 100);
            this.btnNewSession.Location = new Point(360, 22);
            this.btnNewSession.Name = "btnNewSession";
            this.btnNewSession.Size = new Size(85, 25);
            this.btnNewSession.TabIndex = 2;
            this.btnNewSession.Text = "🆕 New";
            this.btnNewSession.UseVisualStyleBackColor = true;
            this.btnNewSession.Click += new System.EventHandler(this.btnNewSession_Click);

            // --- Region Selection Row ---

            // 
            // btnSelectRegion
            // 
            this.btnSelectRegion.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSelectRegion.ForeColor = Color.OrangeRed;
            this.btnSelectRegion.Location = new Point(15, 60);
            this.btnSelectRegion.Name = "btnSelectRegion";
            this.btnSelectRegion.Size = new Size(130, 32);
            this.btnSelectRegion.TabIndex = 3;
            this.btnSelectRegion.Text = "📐 Select Region";
            this.btnSelectRegion.UseVisualStyleBackColor = true;
            this.btnSelectRegion.Click += new System.EventHandler(this.btnSelectRegion_Click);

            // 
            // lblRegion
            // 
            this.lblRegion.AutoSize = true;
            this.lblRegion.Location = new Point(160, 68);
            this.lblRegion.Name = "lblRegion";
            this.lblRegion.Size = new Size(106, 15);
            this.lblRegion.TabIndex = 4;
            this.lblRegion.Text = "No region selected";

            // --- Aspect Ratio Row ---

            // 
            // lblAspectRatioText
            // 
            this.lblAspectRatioText.AutoSize = true;
            this.lblAspectRatioText.Location = new Point(15, 110);
            this.lblAspectRatioText.Name = "lblAspectRatioText";
            this.lblAspectRatioText.Size = new Size(76, 15);
            this.lblAspectRatioText.TabIndex = 5;
            this.lblAspectRatioText.Text = "Aspect Ratio:";

            // 
            // cmbAspectRatio
            // 
            this.cmbAspectRatio.BackColor = SystemColors.InactiveCaptionText;
            this.cmbAspectRatio.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbAspectRatio.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.cmbAspectRatio.ForeColor = SystemColors.ScrollBar;
            this.cmbAspectRatio.FormattingEnabled = true;
            this.cmbAspectRatio.Location = new Point(110, 107);
            this.cmbAspectRatio.Name = "cmbAspectRatio";
            this.cmbAspectRatio.Size = new Size(200, 23);
            this.cmbAspectRatio.TabIndex = 6;
            this.cmbAspectRatio.SelectedIndexChanged += new System.EventHandler(this.cmbAspectRatio_SelectedIndexChanged);

            // --- Interval & Format Row ---

            // 
            // lblIntervalText
            // 
            this.lblIntervalText.AutoSize = true;
            this.lblIntervalText.Location = new Point(15, 154);
            this.lblIntervalText.Name = "lblIntervalText";
            this.lblIntervalText.Size = new Size(103, 15);
            this.lblIntervalText.TabIndex = 7;
            this.lblIntervalText.Text = "Interval (seconds):";

            // 
            // numInterval
            // 
            this.numInterval.BackColor = SystemColors.InactiveCaptionText;
            this.numInterval.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.numInterval.ForeColor = SystemColors.ScrollBar;
            this.numInterval.Location = new Point(130, 152);
            this.numInterval.Maximum = new decimal(new int[] { 3600, 0, 0, 0 });
            this.numInterval.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numInterval.Name = "numInterval";
            this.numInterval.Size = new Size(80, 23);
            this.numInterval.TabIndex = 8;
            this.numInterval.Value = new decimal(new int[] { 5, 0, 0, 0 });
            this.numInterval.ValueChanged += new System.EventHandler(this.numInterval_ValueChanged);

            // 
            // lblFormatText
            // 
            this.lblFormatText.AutoSize = true;
            this.lblFormatText.Location = new Point(240, 154);
            this.lblFormatText.Name = "lblFormatText";
            this.lblFormatText.Size = new Size(48, 15);
            this.lblFormatText.TabIndex = 9;
            this.lblFormatText.Text = "Format:";

            // 
            // cmbFormat
            // 
            this.cmbFormat.BackColor = SystemColors.InactiveCaptionText;
            this.cmbFormat.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbFormat.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.cmbFormat.ForeColor = SystemColors.ScrollBar;
            this.cmbFormat.FormattingEnabled = true;
            this.cmbFormat.Items.AddRange(new object[] { "JPEG", "PNG", "BMP" });
            this.cmbFormat.Location = new Point(300, 151);
            this.cmbFormat.Name = "cmbFormat";
            this.cmbFormat.Size = new Size(100, 23);
            this.cmbFormat.TabIndex = 10;
            this.cmbFormat.SelectedIndexChanged += new System.EventHandler(this.cmbFormat_SelectedIndexChanged);

            // --- Quality Row ---

            // 
            // lblQuality
            // 
            this.lblQuality.AutoSize = true;
            this.lblQuality.Location = new Point(15, 194);
            this.lblQuality.Name = "lblQuality";
            this.lblQuality.Size = new Size(91, 15);
            this.lblQuality.TabIndex = 11;
            this.lblQuality.Text = "JPEG Quality: 90";

            // 
            // trkQuality
            // 
            this.trkQuality.Location = new Point(15, 219);
            this.trkQuality.Maximum = 100;
            this.trkQuality.Minimum = 1;
            this.trkQuality.Name = "trkQuality";
            this.trkQuality.Size = new Size(340, 45);
            this.trkQuality.TabIndex = 12;
            this.trkQuality.TickFrequency = 5;
            this.trkQuality.Value = 90;
            this.trkQuality.Scroll += new System.EventHandler(this.trkQuality_Scroll);

            // 
            // numQuality
            // 
            this.numQuality.BackColor = SystemColors.InactiveCaptionText;
            this.numQuality.ForeColor = SystemColors.ScrollBar;
            this.numQuality.Location = new Point(370, 229);
            this.numQuality.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numQuality.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.numQuality.Name = "numQuality";
            this.numQuality.Size = new Size(70, 23);
            this.numQuality.TabIndex = 13;
            this.numQuality.Value = new decimal(new int[] { 90, 0, 0, 0 });
            this.numQuality.ValueChanged += new System.EventHandler(this.numQuality_ValueChanged);

            // ================================================================
            // SESSION GROUP
            // ================================================================

            // 
            // grpSession
            // 
            this.grpSession.Controls.Add(this.lblStatus);
            this.grpSession.Controls.Add(this.lblEstimate);
            this.grpSession.Controls.Add(this.btnStart);
            this.grpSession.Controls.Add(this.btnStop);
            this.grpSession.ForeColor = Color.LightGray;
            this.grpSession.Location = new Point(15, 305);
            this.grpSession.Name = "grpSession";
            this.grpSession.Size = new Size(460, 135);
            this.grpSession.TabIndex = 1;
            this.grpSession.TabStop = false;
            this.grpSession.Text = "Session";

            // 
            // lblStatus
            // 
            this.lblStatus.AutoSize = true;
            this.lblStatus.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            this.lblStatus.Location = new Point(15, 25);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new Size(147, 15);
            this.lblStatus.TabIndex = 0;
            this.lblStatus.Text = "Ready - No active session";

            // 
            // lblEstimate
            // 
            this.lblEstimate.Location = new Point(15, 50);
            this.lblEstimate.Name = "lblEstimate";
            this.lblEstimate.Size = new Size(430, 42);
            this.lblEstimate.TabIndex = 1;
            this.lblEstimate.Text = "No frames captured yet";

            // 
            // btnStart
            // 
            this.btnStart.BackColor = Color.FromArgb(0, 122, 204);
            this.btnStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStart.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            this.btnStart.ForeColor = Color.White;
            this.btnStart.Location = new Point(15, 95);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new Size(200, 32);
            this.btnStart.TabIndex = 2;
            this.btnStart.Text = "▶ Start Capture";
            this.btnStart.UseVisualStyleBackColor = false;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);

            // 
            // btnStop
            // 
            this.btnStop.BackColor = Color.FromArgb(192, 0, 0);
            this.btnStop.Enabled = false;
            this.btnStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStop.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            this.btnStop.ForeColor = Color.White;
            this.btnStop.Location = new Point(230, 95);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new Size(200, 32);
            this.btnStop.TabIndex = 3;
            this.btnStop.Text = "⏹ Stop";
            this.btnStop.UseVisualStyleBackColor = false;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);

            // ================================================================
            // OUTPUT GROUP
            // ================================================================

            // 
            // grpOutput
            // 
            this.grpOutput.Controls.Add(this.btnChooseFolder);
            this.grpOutput.Controls.Add(this.lblFolder);
            this.grpOutput.Controls.Add(this.lblFfmpegText);
            this.grpOutput.Controls.Add(this.txtFfmpegPath);
            this.grpOutput.Controls.Add(this.btnBrowseFfmpeg);
            this.grpOutput.Controls.Add(this.btnEncode);
            this.grpOutput.Controls.Add(this.btnOpenFolder);
            this.grpOutput.ForeColor = Color.LightGray;
            this.grpOutput.Location = new Point(15, 450);
            this.grpOutput.Name = "grpOutput";
            this.grpOutput.Size = new Size(460, 175);
            this.grpOutput.TabIndex = 2;
            this.grpOutput.TabStop = false;
            this.grpOutput.Text = "Output";

            // 
            // btnChooseFolder
            // 
            this.btnChooseFolder.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnChooseFolder.ForeColor = Color.RoyalBlue;
            this.btnChooseFolder.Location = new Point(15, 25);
            this.btnChooseFolder.Name = "btnChooseFolder";
            this.btnChooseFolder.Size = new Size(130, 32);
            this.btnChooseFolder.TabIndex = 0;
            this.btnChooseFolder.Text = "📁 Choose Folder";
            this.btnChooseFolder.UseVisualStyleBackColor = true;
            this.btnChooseFolder.Click += new System.EventHandler(this.btnChooseFolder_Click);

            // 
            // lblFolder
            // 
            this.lblFolder.AutoSize = true;
            this.lblFolder.Location = new Point(160, 33);
            this.lblFolder.Name = "lblFolder";
            this.lblFolder.Size = new Size(103, 15);
            this.lblFolder.TabIndex = 1;
            this.lblFolder.Text = "No folder selected";

            // 
            // lblFfmpegText
            // 
            this.lblFfmpegText.AutoSize = true;
            this.lblFfmpegText.Location = new Point(15, 75);
            this.lblFfmpegText.Name = "lblFfmpegText";
            this.lblFfmpegText.Size = new Size(80, 15);
            this.lblFfmpegText.TabIndex = 2;
            this.lblFfmpegText.Text = "FFmpeg Path:";

            // 
            // txtFfmpegPath
            // 
            this.txtFfmpegPath.BackColor = SystemColors.MenuText;
            this.txtFfmpegPath.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtFfmpegPath.ForeColor = SystemColors.ScrollBar;
            this.txtFfmpegPath.Location = new Point(15, 95);
            this.txtFfmpegPath.Name = "txtFfmpegPath";
            this.txtFfmpegPath.ReadOnly = true;
            this.txtFfmpegPath.Size = new Size(340, 23);
            this.txtFfmpegPath.TabIndex = 3;

            // 
            // btnBrowseFfmpeg
            // 
            this.btnBrowseFfmpeg.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBrowseFfmpeg.Location = new Point(365, 94);
            this.btnBrowseFfmpeg.Name = "btnBrowseFfmpeg";
            this.btnBrowseFfmpeg.Size = new Size(80, 25);
            this.btnBrowseFfmpeg.TabIndex = 4;
            this.btnBrowseFfmpeg.Text = "Browse...";
            this.btnBrowseFfmpeg.UseVisualStyleBackColor = true;
            this.btnBrowseFfmpeg.Click += new System.EventHandler(this.btnBrowseFfmpeg_Click);

            // 
            // btnEncode
            // 
            this.btnEncode.BackColor = Color.FromArgb(16, 124, 16);
            this.btnEncode.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnEncode.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            this.btnEncode.ForeColor = Color.White;
            this.btnEncode.Location = new Point(15, 130);
            this.btnEncode.Name = "btnEncode";
            this.btnEncode.Size = new Size(150, 30);
            this.btnEncode.TabIndex = 5;
            this.btnEncode.Text = "🎬 Encode Video";
            this.btnEncode.UseVisualStyleBackColor = false;
            this.btnEncode.Click += new System.EventHandler(this.btnEncode_Click);

            // 
            // btnOpenFolder
            // 
            this.btnOpenFolder.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOpenFolder.Location = new Point(175, 130);
            this.btnOpenFolder.Name = "btnOpenFolder";
            this.btnOpenFolder.Size = new Size(150, 30);
            this.btnOpenFolder.TabIndex = 6;
            this.btnOpenFolder.Text = "📂 Open Folder";
            this.btnOpenFolder.UseVisualStyleBackColor = true;
            this.btnOpenFolder.Click += new System.EventHandler(this.btnOpenFolder_Click);

            // 
            // numDesiredSec (unused, kept for compatibility)
            //
            this.numDesiredSec.Location = new Point(0, 0);
            this.numDesiredSec.Name = "numDesiredSec";
            this.numDesiredSec.Size = new Size(0, 0);
            this.numDesiredSec.TabIndex = 0;
            this.numDesiredSec.Visible = false;

            // ================================================================
            // MAIN FORM
            // ================================================================

            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = Color.FromArgb(20, 20, 20);
            this.ClientSize = new Size(490, 640);
            this.Controls.Add(this.grpOutput);
            this.Controls.Add(this.grpSession);
            this.Controls.Add(this.grpCaptureSettings);
            this.Font = new Font("Segoe UI", 9F);
            this.ForeColor = Color.FromArgb(200, 200, 200);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Timelapse Capture";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);

            ((System.ComponentModel.ISupportInitialize)(this.numInterval)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numQuality)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numDesiredSec)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkQuality)).EndInit();
            this.grpCaptureSettings.ResumeLayout(false);
            this.grpCaptureSettings.PerformLayout();
            this.grpSession.ResumeLayout(false);
            this.grpSession.PerformLayout();
            this.grpOutput.ResumeLayout(false);
            this.grpOutput.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        #endregion
    }
}