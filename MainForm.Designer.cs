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
        private System.Windows.Forms.NumericUpDown numInterval;
        private System.Windows.Forms.NumericUpDown numQuality;
        private System.Windows.Forms.NumericUpDown numDesiredSec;
        private System.Windows.Forms.ComboBox cmbFormat;
        private System.Windows.Forms.TrackBar trkQuality;
        private System.Windows.Forms.TextBox txtFfmpegPath;

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
            this.btnStart = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnSelectRegion = new System.Windows.Forms.Button();
            this.btnChooseFolder = new System.Windows.Forms.Button();
            this.btnEncode = new System.Windows.Forms.Button();
            this.btnOpenFolder = new System.Windows.Forms.Button();
            this.btnBrowseFfmpeg = new System.Windows.Forms.Button();
            this.lblRegion = new System.Windows.Forms.Label();
            this.lblFolder = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.lblQuality = new System.Windows.Forms.Label();
            this.lblEstimate = new System.Windows.Forms.Label();
            this.numInterval = new System.Windows.Forms.NumericUpDown();
            this.numQuality = new System.Windows.Forms.NumericUpDown();
            this.numDesiredSec = new System.Windows.Forms.NumericUpDown();
            this.cmbFormat = new System.Windows.Forms.ComboBox();
            this.trkQuality = new System.Windows.Forms.TrackBar();
            this.txtFfmpegPath = new System.Windows.Forms.TextBox();

            ((System.ComponentModel.ISupportInitialize)(this.numInterval)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numQuality)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numDesiredSec)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkQuality)).BeginInit();
            this.SuspendLayout();

            // 
            // btnStart
            // 
            this.btnStart.Location = new System.Drawing.Point(15, 15);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(90, 30);
            this.btnStart.TabIndex = 0;
            this.btnStart.Text = "Start Capture";
            this.btnStart.UseVisualStyleBackColor = true;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);

            // 
            // btnStop
            // 
            this.btnStop.Enabled = false;
            this.btnStop.Location = new System.Drawing.Point(115, 15);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(90, 30);
            this.btnStop.TabIndex = 1;
            this.btnStop.Text = "Stop Capture";
            this.btnStop.UseVisualStyleBackColor = true;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);

            // 
            // btnSelectRegion
            // 
            this.btnSelectRegion.Location = new System.Drawing.Point(15, 60);
            this.btnSelectRegion.Name = "btnSelectRegion";
            this.btnSelectRegion.Size = new System.Drawing.Size(120, 30);
            this.btnSelectRegion.TabIndex = 2;
            this.btnSelectRegion.Text = "Select Region";
            this.btnSelectRegion.UseVisualStyleBackColor = true;
            this.btnSelectRegion.Click += new System.EventHandler(this.btnSelectRegion_Click);

            // 
            // btnChooseFolder
            // 
            this.btnChooseFolder.Location = new System.Drawing.Point(145, 60);
            this.btnChooseFolder.Name = "btnChooseFolder";
            this.btnChooseFolder.Size = new System.Drawing.Size(120, 30);
            this.btnChooseFolder.TabIndex = 3;
            this.btnChooseFolder.Text = "Choose Folder";
            this.btnChooseFolder.UseVisualStyleBackColor = true;
            this.btnChooseFolder.Click += new System.EventHandler(this.btnChooseFolder_Click);

            // 
            // btnEncode
            // 
            this.btnEncode.Location = new System.Drawing.Point(15, 415);
            this.btnEncode.Name = "btnEncode";
            this.btnEncode.Size = new System.Drawing.Size(120, 30);
            this.btnEncode.TabIndex = 4;
            this.btnEncode.Text = "Encode Video";
            this.btnEncode.UseVisualStyleBackColor = true;
            this.btnEncode.Click += new System.EventHandler(this.btnEncode_Click);

            // 
            // btnOpenFolder
            // 
            this.btnOpenFolder.Location = new System.Drawing.Point(145, 415);
            this.btnOpenFolder.Name = "btnOpenFolder";
            this.btnOpenFolder.Size = new System.Drawing.Size(120, 30);
            this.btnOpenFolder.TabIndex = 5;
            this.btnOpenFolder.Text = "Open Folder";
            this.btnOpenFolder.UseVisualStyleBackColor = true;
            this.btnOpenFolder.Click += new System.EventHandler(this.btnOpenFolder_Click);

            // 
            // btnBrowseFfmpeg
            // 
            this.btnBrowseFfmpeg.Location = new System.Drawing.Point(345, 355);
            this.btnBrowseFfmpeg.Name = "btnBrowseFfmpeg";
            this.btnBrowseFfmpeg.Size = new System.Drawing.Size(80, 25);
            this.btnBrowseFfmpeg.TabIndex = 14;
            this.btnBrowseFfmpeg.Text = "Browse...";
            this.btnBrowseFfmpeg.UseVisualStyleBackColor = true;
            this.btnBrowseFfmpeg.Click += new System.EventHandler(this.btnBrowseFfmpeg_Click);

            // 
            // lblRegion
            // 
            this.lblRegion.AutoSize = true;
            this.lblRegion.Location = new System.Drawing.Point(15, 100);
            this.lblRegion.Name = "lblRegion";
            this.lblRegion.Size = new System.Drawing.Size(150, 15);
            this.lblRegion.TabIndex = 6;
            this.lblRegion.Text = "Region: Not selected";

            // 
            // lblFolder
            // 
            this.lblFolder.AutoSize = true;
            this.lblFolder.Location = new System.Drawing.Point(15, 125);
            this.lblFolder.Name = "lblFolder";
            this.lblFolder.Size = new System.Drawing.Size(150, 15);
            this.lblFolder.TabIndex = 7;
            this.lblFolder.Text = "Save to: Not selected";

            // 
            // lblStatus
            // 
            this.lblStatus.AutoSize = true;
            this.lblStatus.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblStatus.Location = new System.Drawing.Point(15, 150);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(100, 15);
            this.lblStatus.TabIndex = 8;
            this.lblStatus.Text = "Status: Ready";

            // 
            // lblQuality
            // 
            this.lblQuality.AutoSize = true;
            this.lblQuality.Location = new System.Drawing.Point(15, 275);
            this.lblQuality.Name = "lblQuality";
            this.lblQuality.Size = new System.Drawing.Size(90, 15);
            this.lblQuality.TabIndex = 13;
            this.lblQuality.Text = "JPEG Quality: 90";

            // 
            // lblEstimate
            // 
            this.lblEstimate.Location = new System.Drawing.Point(15, 330);
            this.lblEstimate.Name = "lblEstimate";
            this.lblEstimate.Size = new System.Drawing.Size(410, 40);
            this.lblEstimate.TabIndex = 15;
            this.lblEstimate.Text = "No active session";

            // 
            // numInterval
            // 
            this.numInterval.Location = new System.Drawing.Point(15, 195);
            this.numInterval.Maximum = new decimal(new int[] { 3600, 0, 0, 0 });
            this.numInterval.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numInterval.Name = "numInterval";
            this.numInterval.Size = new System.Drawing.Size(80, 23);
            this.numInterval.TabIndex = 9;
            this.numInterval.Value = new decimal(new int[] { 5, 0, 0, 0 });

            // 
            // numQuality
            // 
            this.numQuality.Location = new System.Drawing.Point(345, 295);
            this.numQuality.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.numQuality.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numQuality.Name = "numQuality";
            this.numQuality.Size = new System.Drawing.Size(60, 23);
            this.numQuality.TabIndex = 12;
            this.numQuality.Value = new decimal(new int[] { 90, 0, 0, 0 });
            this.numQuality.ValueChanged += new System.EventHandler(this.numQuality_ValueChanged);

            // 
            // numDesiredSec
            // 
            this.numDesiredSec.Location = new System.Drawing.Point(225, 195);
            this.numDesiredSec.Maximum = new decimal(new int[] { 3600, 0, 0, 0 });
            this.numDesiredSec.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numDesiredSec.Name = "numDesiredSec";
            this.numDesiredSec.Size = new System.Drawing.Size(80, 23);
            this.numDesiredSec.TabIndex = 16;
            this.numDesiredSec.Value = new decimal(new int[] { 30, 0, 0, 0 });

            // 
            // cmbFormat
            // 
            this.cmbFormat.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbFormat.FormattingEnabled = true;
            this.cmbFormat.Items.AddRange(new object[] { "JPEG", "PNG", "BMP" });
            this.cmbFormat.Location = new System.Drawing.Point(15, 235);
            this.cmbFormat.Name = "cmbFormat";
            this.cmbFormat.Size = new System.Drawing.Size(100, 23);
            this.cmbFormat.TabIndex = 10;
            this.cmbFormat.SelectedIndexChanged += new System.EventHandler(this.cmbFormat_SelectedIndexChanged);

            // 
            // trkQuality
            // 
            this.trkQuality.Location = new System.Drawing.Point(15, 295);
            this.trkQuality.Maximum = 100;
            this.trkQuality.Minimum = 1;
            this.trkQuality.Name = "trkQuality";
            this.trkQuality.Size = new System.Drawing.Size(320, 45);
            this.trkQuality.TabIndex = 11;
            this.trkQuality.TickFrequency = 5;
            this.trkQuality.Value = 90;
            this.trkQuality.Scroll += new System.EventHandler(this.trkQuality_Scroll);

            // 
            // txtFfmpegPath
            // 
            this.txtFfmpegPath.Location = new System.Drawing.Point(15, 356);
            this.txtFfmpegPath.Name = "txtFfmpegPath";
            this.txtFfmpegPath.ReadOnly = true;
            this.txtFfmpegPath.Size = new System.Drawing.Size(320, 23);
            this.txtFfmpegPath.TabIndex = 13;

            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(450, 470);
            this.Controls.Add(this.txtFfmpegPath);
            this.Controls.Add(this.btnBrowseFfmpeg);
            this.Controls.Add(this.lblEstimate);
            this.Controls.Add(this.numDesiredSec);
            this.Controls.Add(this.lblQuality);
            this.Controls.Add(this.numQuality);
            this.Controls.Add(this.trkQuality);
            this.Controls.Add(this.cmbFormat);
            this.Controls.Add(this.numInterval);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.lblFolder);
            this.Controls.Add(this.lblRegion);
            this.Controls.Add(this.btnOpenFolder);
            this.Controls.Add(this.btnEncode);
            this.Controls.Add(this.btnChooseFolder);
            this.Controls.Add(this.btnSelectRegion);
            this.Controls.Add(this.btnStop);
            this.Controls.Add(this.btnStart);
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
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}