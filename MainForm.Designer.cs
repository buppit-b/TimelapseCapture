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
        private System.Windows.Forms.Label lblRegion;
        private System.Windows.Forms.Label lblFolder;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.NumericUpDown numInterval;
        private System.Windows.Forms.ComboBox cmbFormat;
        private System.Windows.Forms.TrackBar trkQuality;
        private System.Windows.Forms.NumericUpDown numQuality;
        private System.Windows.Forms.GroupBox grpActions;
        private System.Windows.Forms.TextBox txtFfmpegPath;
        private System.Windows.Forms.NumericUpDown numDesiredSec;

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
            this.lblRegion = new System.Windows.Forms.Label();
            this.lblFolder = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.numInterval = new System.Windows.Forms.NumericUpDown();
            this.cmbFormat = new System.Windows.Forms.ComboBox();
            this.trkQuality = new System.Windows.Forms.TrackBar();
            this.numQuality = new System.Windows.Forms.NumericUpDown();
            this.grpActions = new System.Windows.Forms.GroupBox();
            this.txtFfmpegPath = new System.Windows.Forms.TextBox();
            this.numDesiredSec = new System.Windows.Forms.NumericUpDown();

            ((System.ComponentModel.ISupportInitialize)(this.numInterval)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkQuality)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numQuality)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numDesiredSec)).BeginInit();
            this.SuspendLayout();

            // btnStart
            this.btnStart.Location = new System.Drawing.Point(12, 12);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(75, 23);
            this.btnStart.TabIndex = 0;
            this.btnStart.Text = "Start";
            this.btnStart.UseVisualStyleBackColor = true;
            this.btnStart.Click += new System.EventHandler(this.btnStartCapture_Click);

            // btnStop
            this.btnStop.Location = new System.Drawing.Point(93, 12);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(75, 23);
            this.btnStop.TabIndex = 1;
            this.btnStop.Text = "Stop";
            this.btnStop.UseVisualStyleBackColor = true;
            this.btnStop.Click += new System.EventHandler(this.btnStopCapture_Click);

            // btnSelectRegion
            this.btnSelectRegion.Location = new System.Drawing.Point(12, 41);
            this.btnSelectRegion.Name = "btnSelectRegion";
            this.btnSelectRegion.Size = new System.Drawing.Size(100, 23);
            this.btnSelectRegion.TabIndex = 2;
            this.btnSelectRegion.Text = "Select Region";
            this.btnSelectRegion.UseVisualStyleBackColor = true;
            this.btnSelectRegion.Click += new System.EventHandler(this.btnSelectRegion_Click);

            // btnChooseFolder
            this.btnChooseFolder.Location = new System.Drawing.Point(118, 41);
            this.btnChooseFolder.Name = "btnChooseFolder";
            this.btnChooseFolder.Size = new System.Drawing.Size(100, 23);
            this.btnChooseFolder.TabIndex = 3;
            this.btnChooseFolder.Text = "Choose Folder";
            this.btnChooseFolder.UseVisualStyleBackColor = true;
            this.btnChooseFolder.Click += new System.EventHandler(this.btnChooseFolder_Click);

            // btnEncode
            this.btnEncode.Location = new System.Drawing.Point(12, 70);
            this.btnEncode.Name = "btnEncode";
            this.btnEncode.Size = new System.Drawing.Size(100, 23);
            this.btnEncode.TabIndex = 4;
            this.btnEncode.Text = "Encode Video";
            this.btnEncode.UseVisualStyleBackColor = true;
            this.btnEncode.Click += new System.EventHandler(this.btnEncode_Click);

            // lblRegion
            this.lblRegion.AutoSize = true;
            this.lblRegion.Location = new System.Drawing.Point(12, 100);
            this.lblRegion.Name = "lblRegion";
            this.lblRegion.Size = new System.Drawing.Size(46, 17);
            this.lblRegion.TabIndex = 5;
            this.lblRegion.Text = "Region";

            // lblFolder
            this.lblFolder.AutoSize = true;
            this.lblFolder.Location = new System.Drawing.Point(12, 120);
            this.lblFolder.Name = "lblFolder";
            this.lblFolder.Size = new System.Drawing.Size(44, 17);
            this.lblFolder.TabIndex = 6;
            this.lblFolder.Text = "Folder";

            // lblStatus
            this.lblStatus.AutoSize = true;
            this.lblStatus.Location = new System.Drawing.Point(12, 140);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(48, 17);
            this.lblStatus.TabIndex = 7;
            this.lblStatus.Text = "Status";

            // numInterval
            this.numInterval.Location = new System.Drawing.Point(12, 160);
            this.numInterval.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numInterval.Maximum = new decimal(new int[] { 3600, 0, 0, 0 });
            this.numInterval.Name = "numInterval";
            this.numInterval.Size = new System.Drawing.Size(80, 22);
            this.numInterval.ValueChanged += new System.EventHandler(this.numInterval_ValueChanged);

            // cmbFormat
            this.cmbFormat.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbFormat.Items.AddRange(new object[] { "JPG", "PNG", "BMP" });
            this.cmbFormat.Location = new System.Drawing.Point(100, 160);
            this.cmbFormat.Name = "cmbFormat";
            this.cmbFormat.Size = new System.Drawing.Size(80, 24);
            this.cmbFormat.SelectedIndexChanged += new System.EventHandler(this.cmbFormat_SelectedIndexChanged);

            // trkQuality
            this.trkQuality.Location = new System.Drawing.Point(12, 190);
            this.trkQuality.Minimum = 1;
            this.trkQuality.Maximum = 100;
            this.trkQuality.TickFrequency = 5;
            this.trkQuality.Scroll += new System.EventHandler(this.trkQuality_Scroll);

            // numQuality
            this.numQuality.Location = new System.Drawing.Point(100, 190);
            this.numQuality.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numQuality.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.numQuality.ValueChanged += new System.EventHandler(this.numQuality_ValueChanged);

            // grpActions
            this.grpActions.Location = new System.Drawing.Point(10, 220);
            this.grpActions.Name = "grpActions";
            this.grpActions.Size = new System.Drawing.Size(200, 100);
            this.grpActions.Enter += new System.EventHandler(this.grpActions_Enter);

            // txtFfmpegPath
            this.txtFfmpegPath.Location = new System.Drawing.Point(12, 330);
            this.txtFfmpegPath.Name = "txtFfmpegPath";
            this.txtFfmpegPath.Size = new System.Drawing.Size(200, 22);
            this.txtFfmpegPath.TextChanged += new System.EventHandler(this.txtFfmpegPath_TextChanged);

            // numDesiredSec
            this.numDesiredSec.Location = new System.Drawing.Point(12, 360);
            this.numDesiredSec.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numDesiredSec.Maximum = new decimal(new int[] { 3600, 0, 0, 0 });
            this.numDesiredSec.ValueChanged += new System.EventHandler(this.numDesiredSec_ValueChanged);

            // MainForm
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(400, 400);
            this.Controls.Add(this.btnStart);
            this.Controls.Add(this.btnStop);
            this.Controls.Add(this.btnSelectRegion);
            this.Controls.Add(this.btnChooseFolder);
            this.Controls.Add(this.btnEncode);
            this.Controls.Add(this.lblRegion);
            this.Controls.Add(this.lblFolder);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.numInterval);
            this.Controls.Add(this.cmbFormat);
            this.Controls.Add(this.trkQuality);
            this.Controls.Add(this.numQuality);
            this.Controls.Add(this.grpActions);
            this.Controls.Add(this.txtFfmpegPath);
            this.Controls.Add(this.numDesiredSec);
            this.Name = "MainForm";
            this.Text = "Timelapse Capture";
            this.Load += new System.EventHandler(this.MainForm_Load);

            ((System.ComponentModel.ISupportInitialize)(this.numInterval)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkQuality)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numQuality)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numDesiredSec)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
