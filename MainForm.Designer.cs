using System.Drawing;

namespace TimelapseCapture
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Button btnSelectRegion;
        private System.Windows.Forms.Button btnChooseFolder;
        private System.Windows.Forms.Button btnEncode;
        private System.Windows.Forms.Button btnOpenFolder;
        private System.Windows.Forms.Button btnBrowseFfmpeg;
        private System.Windows.Forms.Label lblRegion;
        private System.Windows.Forms.Label lblFolder;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Label lblQuality;
        private System.Windows.Forms.Label lblEstimate;
        private System.Windows.Forms.Label lblIntervalText;
        private System.Windows.Forms.Label lblFormatText;
        private System.Windows.Forms.Label lblFfmpegText;
        private System.Windows.Forms.NumericUpDown numInterval;
        private System.Windows.Forms.NumericUpDown numQuality;
        private System.Windows.Forms.NumericUpDown numDesiredSec;
        private System.Windows.Forms.ComboBox cmbFormat;
        private System.Windows.Forms.TrackBar trkQuality;
        private System.Windows.Forms.TextBox txtFfmpegPath;
        private System.Windows.Forms.GroupBox grpCaptureSettings;
        private System.Windows.Forms.GroupBox grpSession;
        private System.Windows.Forms.GroupBox grpOutput;

        //
        // Pallete (needs revision, not a guide just a placeholder)
        //  
        // Background: #141414 (20, 20, 20)
        // Foreground: #C8C8C8 (200, 200, 200)
        // Primary Button: #007ACC (0, 122, 204)
        // Stop Button: #C00000 (192, 0, 0)
        // Encode Button: #107C10 (16, 124, 16)


        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            btnStart = new System.Windows.Forms.Button();
            btnStop = new System.Windows.Forms.Button();
            btnSelectRegion = new System.Windows.Forms.Button();
            btnChooseFolder = new System.Windows.Forms.Button();
            btnEncode = new System.Windows.Forms.Button();
            btnOpenFolder = new System.Windows.Forms.Button();
            btnBrowseFfmpeg = new System.Windows.Forms.Button();
            lblRegion = new System.Windows.Forms.Label();
            lblFolder = new System.Windows.Forms.Label();
            lblStatus = new System.Windows.Forms.Label();
            lblQuality = new System.Windows.Forms.Label();
            lblEstimate = new System.Windows.Forms.Label();
            lblIntervalText = new System.Windows.Forms.Label();
            lblFormatText = new System.Windows.Forms.Label();
            lblFfmpegText = new System.Windows.Forms.Label();
            numInterval = new System.Windows.Forms.NumericUpDown();
            numQuality = new System.Windows.Forms.NumericUpDown();
            numDesiredSec = new System.Windows.Forms.NumericUpDown();
            cmbFormat = new System.Windows.Forms.ComboBox();
            trkQuality = new System.Windows.Forms.TrackBar();
            txtFfmpegPath = new System.Windows.Forms.TextBox();
            grpCaptureSettings = new System.Windows.Forms.GroupBox();
            grpSession = new System.Windows.Forms.GroupBox();
            grpOutput = new System.Windows.Forms.GroupBox();
            ((System.ComponentModel.ISupportInitialize)numInterval).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numQuality).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numDesiredSec).BeginInit();
            ((System.ComponentModel.ISupportInitialize)trkQuality).BeginInit();
            grpCaptureSettings.SuspendLayout();
            grpSession.SuspendLayout();
            grpOutput.SuspendLayout();
            SuspendLayout();
            // 
            // btnStart
            // 
            btnStart.BackColor = Color.DarkBlue;
            btnStart.FlatStyle = System.Windows.Forms.FlatStyle.System;
            btnStart.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnStart.ForeColor = Color.White;
            btnStart.Location = new Point(15, 95);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(180, 30);
            btnStart.TabIndex = 2;
            btnStart.Text = "▶ Start Capture";
            btnStart.UseVisualStyleBackColor = false;
            btnStart.Click += btnStart_Click;
            // 
            // btnStop
            // 
            btnStop.BackColor = Color.FromArgb(192, 0, 0);
            btnStop.Enabled = false;
            btnStop.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            btnStop.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnStop.ForeColor = Color.White;
            btnStop.Location = new Point(220, 95);
            btnStop.Name = "btnStop";
            btnStop.Size = new Size(180, 30);
            btnStop.TabIndex = 3;
            btnStop.Text = "⏹ Stop";
            btnStop.UseVisualStyleBackColor = false;
            btnStop.Click += btnStop_Click;
            // 
            // btnSelectRegion
            // 
            btnSelectRegion.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnSelectRegion.ForeColor = Color.OrangeRed;
            btnSelectRegion.Location = new Point(15, 25);
            btnSelectRegion.Name = "btnSelectRegion";
            btnSelectRegion.Size = new Size(130, 32);
            btnSelectRegion.TabIndex = 0;
            btnSelectRegion.Text = "📐 Select Region";
            btnSelectRegion.UseVisualStyleBackColor = true;
            btnSelectRegion.Click += btnSelectRegion_Click;
            // 
            // btnChooseFolder
            // 
            btnChooseFolder.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
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
            btnEncode.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
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
            btnOpenFolder.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
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
            btnBrowseFfmpeg.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnBrowseFfmpeg.Location = new Point(365, 94);
            btnBrowseFfmpeg.Name = "btnBrowseFfmpeg";
            btnBrowseFfmpeg.Size = new Size(80, 25);
            btnBrowseFfmpeg.TabIndex = 4;
            btnBrowseFfmpeg.Text = "Browse...";
            btnBrowseFfmpeg.UseVisualStyleBackColor = true;
            btnBrowseFfmpeg.Click += btnBrowseFfmpeg_Click;
            // 
            // lblRegion
            // 
            lblRegion.AutoSize = true;
            lblRegion.Location = new Point(160, 33);
            lblRegion.Name = "lblRegion";
            lblRegion.Size = new Size(106, 15);
            lblRegion.TabIndex = 1;
            lblRegion.Text = "No region selected";
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
            lblQuality.Location = new Point(15, 120);
            lblQuality.Name = "lblQuality";
            lblQuality.Size = new Size(91, 15);
            lblQuality.TabIndex = 6;
            lblQuality.Text = "JPEG Quality: 90";
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
            lblIntervalText.Location = new Point(15, 75);
            lblIntervalText.Name = "lblIntervalText";
            lblIntervalText.Size = new Size(103, 15);
            lblIntervalText.TabIndex = 2;
            lblIntervalText.Text = "Interval (seconds):";
            // 
            // lblFormatText
            // 
            lblFormatText.AutoSize = true;
            lblFormatText.Location = new Point(240, 75);
            lblFormatText.Name = "lblFormatText";
            lblFormatText.Size = new Size(48, 15);
            lblFormatText.TabIndex = 4;
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
            // numInterval
            // 
            numInterval.BackColor = SystemColors.InactiveCaptionText;
            numInterval.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            numInterval.ForeColor = SystemColors.ScrollBar;
            numInterval.Location = new Point(130, 73);
            numInterval.Maximum = new decimal(new int[] { 3600, 0, 0, 0 });
            numInterval.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numInterval.Name = "numInterval";
            numInterval.Size = new Size(80, 23);
            numInterval.TabIndex = 3;
            numInterval.Value = new decimal(new int[] { 5, 0, 0, 0 });
            // 
            // numQuality
            // 
            numQuality.BackColor = SystemColors.InactiveCaptionText;
            numQuality.ForeColor = SystemColors.ScrollBar;
            numQuality.Location = new Point(375, 145);
            numQuality.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numQuality.Name = "numQuality";
            numQuality.Size = new Size(70, 23);
            numQuality.TabIndex = 8;
            numQuality.Value = new decimal(new int[] { 90, 0, 0, 0 });
            numQuality.ValueChanged += numQuality_ValueChanged;
            // 
            // numDesiredSec
            // 
            numDesiredSec.Location = new Point(0, 0);
            numDesiredSec.Name = "numDesiredSec";
            numDesiredSec.Size = new Size(120, 23);
            numDesiredSec.TabIndex = 0;
            // 
            // cmbFormat
            // 
            cmbFormat.BackColor = SystemColors.InactiveCaptionText;
            cmbFormat.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cmbFormat.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            cmbFormat.ForeColor = SystemColors.ScrollBar;
            cmbFormat.FormattingEnabled = true;
            cmbFormat.Items.AddRange(new object[] { "JPEG", "PNG", "BMP" });
            cmbFormat.Location = new Point(300, 72);
            cmbFormat.Name = "cmbFormat";
            cmbFormat.Size = new Size(100, 23);
            cmbFormat.TabIndex = 5;
            cmbFormat.SelectedIndexChanged += cmbFormat_SelectedIndexChanged;
            // 
            // trkQuality
            // 
            trkQuality.Location = new Point(15, 145);
            trkQuality.Maximum = 100;
            trkQuality.Minimum = 1;
            trkQuality.Name = "trkQuality";
            trkQuality.Size = new Size(340, 45);
            trkQuality.TabIndex = 7;
            trkQuality.TickFrequency = 5;
            trkQuality.Value = 90;
            trkQuality.Scroll += trkQuality_Scroll;
            // 
            // txtFfmpegPath
            // 
            txtFfmpegPath.BackColor = SystemColors.MenuText;
            txtFfmpegPath.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            txtFfmpegPath.ForeColor = SystemColors.ScrollBar;
            txtFfmpegPath.Location = new Point(15, 95);
            txtFfmpegPath.Name = "txtFfmpegPath";
            txtFfmpegPath.ReadOnly = true;
            txtFfmpegPath.Size = new Size(340, 23);
            txtFfmpegPath.TabIndex = 3;
            // 
            // grpCaptureSettings
            // 
            grpCaptureSettings.Controls.Add(btnSelectRegion);
            grpCaptureSettings.Controls.Add(lblRegion);
            grpCaptureSettings.Controls.Add(lblIntervalText);
            grpCaptureSettings.Controls.Add(numInterval);
            grpCaptureSettings.Controls.Add(lblFormatText);
            grpCaptureSettings.Controls.Add(cmbFormat);
            grpCaptureSettings.Controls.Add(lblQuality);
            grpCaptureSettings.Controls.Add(trkQuality);
            grpCaptureSettings.Controls.Add(numQuality);
            grpCaptureSettings.ForeColor = Color.LightGray;
            grpCaptureSettings.Location = new Point(15, 15);
            grpCaptureSettings.Name = "grpCaptureSettings";
            grpCaptureSettings.Size = new Size(460, 210);
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
            grpSession.Location = new Point(15, 235);
            grpSession.Name = "grpSession";
            grpSession.Size = new Size(460, 150);
            grpSession.TabIndex = 1;
            grpSession.TabStop = false;
            grpSession.Text = "Session";
            grpSession.Enter += grpSession_Enter;
            // 
            // grpOutput
            // 
            grpOutput.Controls.Add(btnChooseFolder);
            grpOutput.Controls.Add(lblFolder);
            grpOutput.Controls.Add(lblFfmpegText);
            grpOutput.Controls.Add(txtFfmpegPath);
            grpOutput.Controls.Add(btnBrowseFfmpeg);
            grpOutput.Controls.Add(btnEncode);
            grpOutput.Controls.Add(btnOpenFolder);
            grpOutput.ForeColor = Color.LightGray;
            grpOutput.Location = new Point(15, 391);
            grpOutput.Name = "grpOutput";
            grpOutput.Size = new Size(460, 181);
            grpOutput.TabIndex = 2;
            grpOutput.TabStop = false;
            grpOutput.Text = "Output";
            grpOutput.Enter += grpOutput_Enter;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            BackColor = Color.FromArgb(20, 20, 20);
            ClientSize = new Size(490, 584);
            Controls.Add(grpOutput);
            Controls.Add(grpSession);
            Controls.Add(grpCaptureSettings);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.FromArgb(200, 200, 200);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Name = "MainForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
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
            ResumeLayout(false);
        }
    }
}