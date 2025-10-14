// MainForm.Designer.cs
namespace TimelapseCapture
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.GroupBox grpCapture;
        private System.Windows.Forms.Button btnSelectRegion;
        private System.Windows.Forms.Button btnChooseFolder;
        private System.Windows.Forms.Label lblFolder;
        private System.Windows.Forms.Label lblRegion;
        private System.Windows.Forms.Label lblInterval;
        private System.Windows.Forms.NumericUpDown numInterval;
        private System.Windows.Forms.GroupBox grpImage;
        private System.Windows.Forms.ComboBox cmbFormat;
        private System.Windows.Forms.TrackBar trkQuality;
        private System.Windows.Forms.Label lblQuality;
        private System.Windows.Forms.NumericUpDown numQuality;
        private System.Windows.Forms.GroupBox grpActions;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Button btnOpenFolder;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Button btnEncode;
        private System.Windows.Forms.TextBox txtFfmpegPath;
        private System.Windows.Forms.Button btnBrowseFfmpeg;
        private System.Windows.Forms.Label lblEstimate;
        private System.Windows.Forms.NumericUpDown numDesiredSec;

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

        private void InitializeComponent()
        {
            grpCapture = new System.Windows.Forms.GroupBox();
            btnSelectRegion = new System.Windows.Forms.Button();
            lblRegion = new System.Windows.Forms.Label();
            btnChooseFolder = new System.Windows.Forms.Button();
            btnBrowseFfmpeg = new System.Windows.Forms.Button();
            lblFolder = new System.Windows.Forms.Label();
            lblInterval = new System.Windows.Forms.Label();
            numInterval = new System.Windows.Forms.NumericUpDown();
            grpImage = new System.Windows.Forms.GroupBox();
            cmbFormat = new System.Windows.Forms.ComboBox();
            lblQuality = new System.Windows.Forms.Label();
            trkQuality = new System.Windows.Forms.TrackBar();
            numQuality = new System.Windows.Forms.NumericUpDown();
            grpActions = new System.Windows.Forms.GroupBox();
            btnStart = new System.Windows.Forms.Button();
            btnStop = new System.Windows.Forms.Button();
            btnOpenFolder = new System.Windows.Forms.Button();
            btnEncode = new System.Windows.Forms.Button();
            lblStatus = new System.Windows.Forms.Label();
            lblEstimate = new System.Windows.Forms.Label();
            txtFfmpegPath = new System.Windows.Forms.TextBox();
            numDesiredSec = new System.Windows.Forms.NumericUpDown();
            grpCapture.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numInterval).BeginInit();
            grpImage.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)trkQuality).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numQuality).BeginInit();
            grpActions.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numDesiredSec).BeginInit();
            SuspendLayout();
            // 
            // grpCapture
            // 
            grpCapture.BackColor = System.Drawing.Color.FromArgb(28, 28, 30);
            grpCapture.Controls.Add(btnSelectRegion);
            grpCapture.Controls.Add(lblRegion);
            grpCapture.Controls.Add(btnEncode);
            grpCapture.Controls.Add(btnOpenFolder);
            grpCapture.Controls.Add(btnChooseFolder);
            grpCapture.Controls.Add(btnBrowseFfmpeg);
            grpCapture.Controls.Add(lblFolder);
            grpCapture.Controls.Add(lblInterval);
            grpCapture.Controls.Add(numInterval);
            grpCapture.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
            grpCapture.Location = new System.Drawing.Point(12, 12);
            grpCapture.Name = "grpCapture";
            grpCapture.Size = new System.Drawing.Size(596, 140);
            grpCapture.TabIndex = 0;
            grpCapture.TabStop = false;
            grpCapture.Text = "Capture Settings";
            // 
            // btnSelectRegion
            // 
            btnSelectRegion.BackColor = System.Drawing.Color.FromArgb(45, 45, 45);
            btnSelectRegion.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnSelectRegion.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
            btnSelectRegion.Location = new System.Drawing.Point(18, 26);
            btnSelectRegion.Name = "btnSelectRegion";
            btnSelectRegion.Size = new System.Drawing.Size(120, 30);
            btnSelectRegion.TabIndex = 0;
            btnSelectRegion.Text = "Select Region";
            btnSelectRegion.UseVisualStyleBackColor = false;
            btnSelectRegion.Click += btnSelectRegion_Click;
            // 
            // lblRegion
            // 
            lblRegion.AutoSize = true;
            lblRegion.Location = new System.Drawing.Point(150, 34);
            lblRegion.Name = "lblRegion";
            lblRegion.Size = new System.Drawing.Size(79, 15);
            lblRegion.TabIndex = 1;
            lblRegion.Text = "Region: None";
            // 
            // btnChooseFolder
            // 
            btnChooseFolder.BackColor = System.Drawing.Color.FromArgb(45, 45, 45);
            btnChooseFolder.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnChooseFolder.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
            btnChooseFolder.Location = new System.Drawing.Point(18, 66);
            btnChooseFolder.Name = "btnChooseFolder";
            btnChooseFolder.Size = new System.Drawing.Size(120, 30);
            btnChooseFolder.TabIndex = 2;
            btnChooseFolder.Text = "Choose Folder";
            btnChooseFolder.UseVisualStyleBackColor = false;
            btnChooseFolder.Click += btnChooseFolder_Click;
            // 
            // btnBrowseFfmpeg
            // 
            btnBrowseFfmpeg.BackColor = System.Drawing.Color.FromArgb(45, 45, 45);
            btnBrowseFfmpeg.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnBrowseFfmpeg.ForeColor = System.Drawing.Color.White;
            btnBrowseFfmpeg.Location = new System.Drawing.Point(460, 21);
            btnBrowseFfmpeg.Name = "btnBrowseFfmpeg";
            btnBrowseFfmpeg.Size = new System.Drawing.Size(120, 28);
            btnBrowseFfmpeg.TabIndex = 5;
            btnBrowseFfmpeg.Text = "Browse FFmpeg";
            btnBrowseFfmpeg.UseVisualStyleBackColor = false;
            btnBrowseFfmpeg.Click += btnBrowseFfmpeg_Click;
            // 
            // lblFolder
            // 
            lblFolder.AutoSize = true;
            lblFolder.Location = new System.Drawing.Point(150, 74);
            lblFolder.MaximumSize = new System.Drawing.Size(420, 0);
            lblFolder.Name = "lblFolder";
            lblFolder.Size = new System.Drawing.Size(80, 15);
            lblFolder.TabIndex = 3;
            lblFolder.Text = "Save to: None";
            // 
            // lblInterval
            // 
            lblInterval.AutoSize = true;
            lblInterval.Location = new System.Drawing.Point(18, 106);
            lblInterval.Name = "lblInterval";
            lblInterval.Size = new System.Drawing.Size(103, 15);
            lblInterval.TabIndex = 4;
            lblInterval.Text = "Interval (seconds):";
            // 
            // numInterval
            // 
            numInterval.Location = new System.Drawing.Point(150, 102);
            numInterval.Maximum = new decimal(new int[] { 3600, 0, 0, 0 });
            numInterval.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numInterval.Name = "numInterval";
            numInterval.Size = new System.Drawing.Size(100, 23);
            numInterval.TabIndex = 5;
            numInterval.Value = new decimal(new int[] { 5, 0, 0, 0 });
            numInterval.ValueChanged += numInterval_ValueChanged;
            // 
            // grpImage
            // 
            grpImage.BackColor = System.Drawing.Color.FromArgb(28, 28, 30);
            grpImage.Controls.Add(cmbFormat);
            grpImage.Controls.Add(lblQuality);
            grpImage.Controls.Add(trkQuality);
            grpImage.Controls.Add(numQuality);
            grpImage.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
            grpImage.Location = new System.Drawing.Point(12, 160);
            grpImage.Name = "grpImage";
            grpImage.Size = new System.Drawing.Size(596, 110);
            grpImage.TabIndex = 1;
            grpImage.TabStop = false;
            grpImage.Text = "Image Settings";
            // 
            // cmbFormat
            // 
            cmbFormat.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cmbFormat.Items.AddRange(new object[] { "JPEG", "PNG" });
            cmbFormat.Location = new System.Drawing.Point(18, 30);
            cmbFormat.Name = "cmbFormat";
            cmbFormat.Size = new System.Drawing.Size(120, 23);
            cmbFormat.TabIndex = 0;
            cmbFormat.SelectedIndexChanged += cmbFormat_SelectedIndexChanged;
            // 
            // lblQuality
            // 
            lblQuality.AutoSize = true;
            lblQuality.Location = new System.Drawing.Point(150, 33);
            lblQuality.Name = "lblQuality";
            lblQuality.Size = new System.Drawing.Size(76, 15);
            lblQuality.TabIndex = 1;
            lblQuality.Text = "JPEG Quality:";
            // 
            // trkQuality
            // 
            trkQuality.Location = new System.Drawing.Point(240, 26);
            trkQuality.Maximum = 100;
            trkQuality.Minimum = 1;
            trkQuality.Name = "trkQuality";
            trkQuality.Size = new System.Drawing.Size(320, 45);
            trkQuality.TabIndex = 2;
            trkQuality.TickFrequency = 5;
            trkQuality.Value = 90;
            trkQuality.Scroll += trkQuality_Scroll;
            // 
            // numQuality
            // 
            numQuality.Location = new System.Drawing.Point(150, 60);
            numQuality.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numQuality.Name = "numQuality";
            numQuality.Size = new System.Drawing.Size(80, 23);
            numQuality.TabIndex = 3;
            numQuality.Value = new decimal(new int[] { 90, 0, 0, 0 });
            numQuality.ValueChanged += numQuality_ValueChanged;
            // 
            // grpActions
            // 
            grpActions.BackColor = System.Drawing.Color.FromArgb(28, 28, 30);
            grpActions.Controls.Add(btnStart);
            grpActions.Controls.Add(btnStop);
            grpActions.Controls.Add(lblStatus);
            grpActions.Controls.Add(lblEstimate);
            grpActions.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
            grpActions.Location = new System.Drawing.Point(12, 276);
            grpActions.Name = "grpActions";
            grpActions.Size = new System.Drawing.Size(596, 114);
            grpActions.TabIndex = 2;
            grpActions.TabStop = false;
            grpActions.Text = "Actions";
            grpActions.Enter += grpActions_Enter;
            // 
            // btnStart
            // 
            btnStart.BackColor = System.Drawing.Color.FromArgb(0, 122, 204);
            btnStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnStart.ForeColor = System.Drawing.Color.White;
            btnStart.Location = new System.Drawing.Point(18, 30);
            btnStart.Name = "btnStart";
            btnStart.Size = new System.Drawing.Size(110, 34);
            btnStart.TabIndex = 0;
            btnStart.Text = "Start";
            btnStart.UseVisualStyleBackColor = false;
            btnStart.Click += btnStart_Click;
            // 
            // btnStop
            // 
            btnStop.BackColor = System.Drawing.Color.FromArgb(80, 80, 80);
            btnStop.Enabled = false;
            btnStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnStop.ForeColor = System.Drawing.Color.White;
            btnStop.Location = new System.Drawing.Point(140, 30);
            btnStop.Name = "btnStop";
            btnStop.Size = new System.Drawing.Size(110, 34);
            btnStop.TabIndex = 1;
            btnStop.Text = "Stop";
            btnStop.UseVisualStyleBackColor = false;
            btnStop.Click += btnStop_Click;
            // 
            // btnOpenFolder
            // 
            btnOpenFolder.BackColor = System.Drawing.Color.FromArgb(45, 45, 45);
            btnOpenFolder.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnOpenFolder.ForeColor = System.Drawing.Color.White;
            btnOpenFolder.Location = new System.Drawing.Point(316, 74);
            btnOpenFolder.Name = "btnOpenFolder";
            btnOpenFolder.Size = new System.Drawing.Size(110, 34);
            btnOpenFolder.TabIndex = 2;
            btnOpenFolder.Text = "Open Folder";
            btnOpenFolder.UseVisualStyleBackColor = false;
            btnOpenFolder.Click += btnOpenFolder_Click;
            // 
            // btnEncode
            // 
            btnEncode.BackColor = System.Drawing.Color.FromArgb(0, 140, 80);
            btnEncode.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnEncode.ForeColor = System.Drawing.Color.White;
            btnEncode.Location = new System.Drawing.Point(440, 74);
            btnEncode.Name = "btnEncode";
            btnEncode.Size = new System.Drawing.Size(150, 34);
            btnEncode.TabIndex = 3;
            btnEncode.Text = "Encode Timelapse";
            btnEncode.UseVisualStyleBackColor = false;
            btnEncode.Click += btnEncode_Click;
            // 
            // lblStatus
            // 
            lblStatus.AutoSize = true;
            lblStatus.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
            lblStatus.Location = new System.Drawing.Point(269, 40);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new System.Drawing.Size(26, 15);
            lblStatus.TabIndex = 6;
            lblStatus.Text = "Idle";
            // 
            // lblEstimate
            // 
            lblEstimate.AutoSize = true;
            lblEstimate.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
            lblEstimate.Location = new System.Drawing.Point(18, 66);
            lblEstimate.Name = "lblEstimate";
            lblEstimate.Size = new System.Drawing.Size(0, 15);
            lblEstimate.TabIndex = 7;
            // 
            // txtFfmpegPath
            // 
            txtFfmpegPath.Location = new System.Drawing.Point(30, 411);
            txtFfmpegPath.Name = "txtFfmpegPath";
            txtFfmpegPath.ReadOnly = true;
            txtFfmpegPath.Size = new System.Drawing.Size(430, 23);
            txtFfmpegPath.TabIndex = 4;
            txtFfmpegPath.TextChanged += txtFfmpegPath_TextChanged;
            // 
            // numDesiredSec
            // 
            numDesiredSec.Location = new System.Drawing.Point(532, 411);
            numDesiredSec.Maximum = new decimal(new int[] { 36000, 0, 0, 0 });
            numDesiredSec.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numDesiredSec.Name = "numDesiredSec";
            numDesiredSec.Size = new System.Drawing.Size(60, 23);
            numDesiredSec.TabIndex = 8;
            numDesiredSec.Value = new decimal(new int[] { 30, 0, 0, 0 });
            numDesiredSec.ValueChanged += numDesiredSec_ValueChanged;
            // 
            // MainForm
            // 
            BackColor = System.Drawing.Color.FromArgb(28, 28, 30);
            ClientSize = new System.Drawing.Size(622, 460);
            Controls.Add(grpCapture);
            Controls.Add(grpImage);
            Controls.Add(grpActions);
            Controls.Add(txtFfmpegPath);
            Controls.Add(numDesiredSec);
            Font = new System.Drawing.Font("Segoe UI", 9F);
            ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Name = "MainForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            Text = "Timelapse Capture - Dark";
            Load += MainForm_Load;
            grpCapture.ResumeLayout(false);
            grpCapture.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numInterval).EndInit();
            grpImage.ResumeLayout(false);
            grpImage.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)trkQuality).EndInit();
            ((System.ComponentModel.ISupportInitialize)numQuality).EndInit();
            grpActions.ResumeLayout(false);
            grpActions.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numDesiredSec).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
