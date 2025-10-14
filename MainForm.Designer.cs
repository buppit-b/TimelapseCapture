namespace TimelapseCapture
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.ComboBox cmbFormat;
        private System.Windows.Forms.NumericUpDown numInterval;
        private System.Windows.Forms.NumericUpDown numQuality;
        private System.Windows.Forms.TrackBar trkQuality;
        private System.Windows.Forms.NumericUpDown numDesiredSec;
        private System.Windows.Forms.Label lblEstimate;
        private System.Windows.Forms.Label lblRegion;
        private System.Windows.Forms.Label lblFolder;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Button btnEncode;
        private System.Windows.Forms.Button btnSelectRegion;
        private System.Windows.Forms.Button btnChooseFolder;
        private System.Windows.Forms.Button btnBrowseFfmpeg;
        private System.Windows.Forms.Button btnOpenFolder;
        private System.Windows.Forms.GroupBox grpActions;
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
            this.components = new System.ComponentModel.Container();
            this.cmbFormat = new System.Windows.Forms.ComboBox();
            this.numInterval = new System.Windows.Forms.NumericUpDown();
            this.numQuality = new System.Windows.Forms.NumericUpDown();
            this.trkQuality = new System.Windows.Forms.TrackBar();
            this.numDesiredSec = new System.Windows.Forms.NumericUpDown();
            this.lblEstimate = new System.Windows.Forms.Label();
            this.lblRegion = new System.Windows.Forms.Label();
            this.lblFolder = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnEncode = new System.Windows.Forms.Button();
            this.btnSelectRegion = new System.Windows.Forms.Button();
            this.btnChooseFolder = new System.Windows.Forms.Button();
            this.btnBrowseFfmpeg = new System.Windows.Forms.Button();
            this.btnOpenFolder = new System.Windows.Forms.Button();
            this.grpActions = new System.Windows.Forms.GroupBox();
            this.txtFfmpegPath = new System.Windows.Forms.TextBox();

            ((System.ComponentModel.ISupportInitialize)(this.numInterval)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numQuality)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkQuality)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numDesiredSec)).BeginInit();

            // 
            // Event wiring
            // 
            this.numInterval.ValueChanged += new System.EventHandler(this.numInterval_ValueChanged);
            this.numDesiredSec.ValueChanged += new System.EventHandler(this.numDesiredSec_ValueChanged);
            this.trkQuality.Scroll += new System.EventHandler(this.trkQuality_Scroll);
            this.numQuality.ValueChanged += new System.EventHandler(this.numQuality_ValueChanged);
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);
            this.btnEncode.Click += new System.EventHandler(this.btnEncode_Click);
            this.btnSelectRegion.Click += new System.EventHandler(this.btnSelectRegion_Click);
            this.btnChooseFolder.Click += new System.EventHandler(this.btnChooseFolder_Click);
            this.btnBrowseFfmpeg.Click += new System.EventHandler(this.btnBrowseFfmpeg_Click);
            this.btnOpenFolder.Click += new System.EventHandler(this.btnOpenFolder_Click);
            this.cmbFormat.SelectedIndexChanged += new System.EventHandler(this.cmbFormat_SelectedIndexChanged);

            // 
            // Form settings
            // 
            this.Text = "Timelapse Capture";
            this.Load += new System.EventHandler(this.MainForm_Load);

            ((System.ComponentModel.ISupportInitialize)(this.numInterval)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numQuality)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkQuality)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numDesiredSec)).EndInit();
        }
    }
}
