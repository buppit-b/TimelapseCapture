using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading.Tasks;

namespace TimelapseCapture
{
    public partial class MainForm : Form
    {
        private Rectangle captureRegion = Rectangle.Empty;
        private System.Threading.Timer? _captureTimer;
        private CaptureSettings settings = new CaptureSettings();
        private int hotkeyId = 0x1000;
        private string? currentSessionFolder = null;

        // Win32 hotkey constants
        private const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public MainForm()
        {
            InitializeComponent();
            LoadSettings();
            WireInitialValues();
            try
            {
                RegisterHotKey(this.Handle, hotkeyId, (uint)settings.HotkeyModifiers, (uint)settings.HotkeyKey);
            }
            catch { }

            UpdateEstimate();
        }

        private void SafeInvoke(Action act)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
                Invoke(act);
            else
                act();
        }

        private void WireInitialValues()
        {
            if (cmbFormat != null) cmbFormat.SelectedItem = settings.Format ?? "JPEG";
            if (numInterval != null) numInterval.Value = settings.IntervalSeconds > 0 ? settings.IntervalSeconds : 5;

            if (settings.Region.HasValue)
            {
                captureRegion = settings.Region.Value;
                SafeInvoke(() =>
                    lblRegion.Text = $"Region: {captureRegion.Width}×{captureRegion.Height} @ ({captureRegion.X},{captureRegion.Y})");
            }

            if (!string.IsNullOrEmpty(settings.SaveFolder))
            {
                SafeInvoke(() =>
                    lblFolder.Text = $"Save to: {settings.SaveFolder}");
            }

            if (trkQuality != null)
            {
                int clampedValue = Math.Min(Math.Max(settings.JpegQuality, trkQuality.Minimum), trkQuality.Maximum);
                trkQuality.Value = clampedValue;
            }

            if (numQuality != null)
            {
                int clampedValue = Math.Min(Math.Max(settings.JpegQuality, (int)numQuality.Minimum), (int)numQuality.Maximum);
                numQuality.Value = clampedValue;
            }

            if (txtFfmpegPath != null) txtFfmpegPath.Text = settings.FfmpegPath ?? "";

            UpdateQualityControls();
        }

        private void btnStart_Click(object? sender, EventArgs e)
        {
            if (captureRegion.Width == 0 || string.IsNullOrEmpty(settings.SaveFolder))
            {
                MessageBox.Show("Please select region and save folder first.", "Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int intervalSec = (int)(numInterval?.Value ?? 5);
            if (intervalSec <= 0)
            {
                MessageBox.Show("Interval must be > 0", "Invalid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Session logic (resume or new)
            var capturesRoot = Path.Combine(settings.SaveFolder ?? AppContext.BaseDirectory, "captures");
            var active = SessionManager.FindActiveSession(capturesRoot);
            if (!string.IsNullOrEmpty(active))
            {
                var res = MessageBox.Show("Resume existing session?", "Session", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (res == DialogResult.Yes)
                {
                    currentSessionFolder = active;
                    var info = SessionManager.LoadSession(currentSessionFolder);
                    if (info != null)
                    {
                        if (numInterval != null) numInterval.Value = info.IntervalSeconds;
                    }
                }
                else
                {
                    currentSessionFolder = SessionManager.CreateNewSession(capturesRoot, intervalSec, 30);
                }
            }
            else
            {
                currentSessionFolder = SessionManager.CreateNewSession(capturesRoot, intervalSec, 30);
            }

            SafeInvoke(() =>
            {
                numInterval.Enabled = false;
                cmbFormat.Enabled = false;
                trkQuality.Enabled = false;
                numQuality.Enabled = false;
                btnSelectRegion.Enabled = false;
                btnChooseFolder.Enabled = false;
                btnBrowseFfmpeg.Enabled = false;
                btnStart.Enabled = false;
                btnStop.Enabled = true;
                lblStatus.Text = "Capturing...";
            });

            settings.SaveFolder = settings.SaveFolder;
            settings.IntervalSeconds = intervalSec;
            settings.Format = cmbFormat?.SelectedItem?.ToString();
            settings.JpegQuality = (int)(numQuality?.Value ?? 90);
            SaveSettings();

            var intervalMs = intervalSec * 1000;
            _captureTimer = new System.Threading.Timer(CaptureFrame, null, 0, intervalMs);
            UpdateEstimate();
        }

        private void btnStop_Click(object? sender, EventArgs e)
        {
            StopCapture();
        }

