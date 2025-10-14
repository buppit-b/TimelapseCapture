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

        /// <summary>
        /// Helper to run UI updates safely on the UI thread
        /// </summary>
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
            if (trkQuality != null) trkQuality.Value = settings.JpegQuality;
            if (numQuality != null) numQuality.Value = settings.JpegQuality;
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

            // Lock UI elements
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
            if (_captureTimer != null)
            {
                _captureTimer.Dispose();
                _captureTimer = null;
            }
            SafeInvoke(() =>
            {
                numInterval.Enabled = true;
                cmbFormat.Enabled = true;
                trkQuality.Enabled = (cmbFormat?.SelectedItem?.ToString() ?? "JPEG") == "JPEG";
                numQuality.Enabled = trkQuality.Enabled;
                btnSelectRegion.Enabled = true;
                btnChooseFolder.Enabled = true;
                btnBrowseFfmpeg.Enabled = true;
                btnStart.Enabled = true;
                btnStop.Enabled = false;
                lblStatus.Text = "Stopped.";
            });
            SaveSettings();
            UpdateEstimate();
        }

        private async void btnEncode_Click(object? sender, EventArgs e)
        {
            try
            {
                // Determine ffmpeg directory
                string ffmpegDir;
                if (!string.IsNullOrWhiteSpace(settings.FfmpegPath))
                {
                    if (File.Exists(settings.FfmpegPath))
                        ffmpegDir = Path.GetDirectoryName(settings.FfmpegPath)!;
                    else
                        ffmpegDir = settings.FfmpegPath!;
                }
                else
                {
                    ffmpegDir = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
                }
                Directory.CreateDirectory(ffmpegDir);

                string? exe = await FfmpegDownloader.EnsureFfmpegPresentAsync(ffmpegDir);
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                {
                    SafeInvoke(() =>
                        MessageBox.Show($"Ffmpeg not available in:\n{exe ?? ffmpegDir}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error));
                    return;
                }

                var capturesFolder = Path.Combine(settings.SaveFolder ?? AppContext.BaseDirectory, "captures");
                var timelapsesFolder = Path.Combine(settings.SaveFolder ?? AppContext.BaseDirectory, "timelapses");
                Directory.CreateDirectory(capturesFolder);
                Directory.CreateDirectory(timelapsesFolder);

                string useFormat = (cmbFormat?.SelectedItem?.ToString() ?? settings.Format) ?? "JPEG";
                string pattern = useFormat.Equals("PNG", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(capturesFolder, "*.png")
                    : Path.Combine(capturesFolder, "*.jpg");

                string output = Path.Combine(timelapsesFolder, $"timelapse_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
                int framerate = 30;

                string args = $"-y -framerate {framerate} -pattern_type glob -i \"{pattern}\" -c:v libx264 -pix_fmt yuv420p \"{output}\"";

                SafeInvoke(() => lblStatus.Text = "Encoding...");
                var result = await FfmpegRunner.RunFfmpegAsync(exe, args);

                if (result.exitCode == 0)
                {
                    SafeInvoke(() => lblStatus.Text = "Encoding complete: " + Path.GetFileName(output));
                    Process.Start(new ProcessStartInfo { FileName = output, UseShellExecute = true });
                }
                else
                {
                    SafeInvoke(() => lblStatus.Text = "ffmpeg error: see log");
                    SafeInvoke(() => MessageBox.Show("ffmpeg failed:\n" + result.error, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error));
                }
            }
            catch (Exception ex)
            {
                SafeInvoke(() => MessageBox.Show("Error while encoding:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error));
            }
        }

        private void CaptureFrame(object? state)
        {
            try
            {
                if (string.IsNullOrEmpty(currentSessionFolder))
                {
                    StopCapture();
                    return;
                }

                var sessionFolder = currentSessionFolder;
                Directory.CreateDirectory(sessionFolder);

                if (captureRegion.Width <= 0 || captureRegion.Height <= 0)
                {
                    var b = Screen.PrimaryScreen.Bounds;
                    captureRegion = new Rectangle(0, 0, b.Width, b.Height);
                }

                using (var bmp = new Bitmap(captureRegion.Width, captureRegion.Height))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(captureRegion.X, captureRegion.Y, 0, 0, captureRegion.Size, CopyPixelOperation.SourceCopy);
                    }

                    var filenameBase = Path.Combine(sessionFolder, $"frame_{DateTime.Now:yyyyMMdd_HHmmss_fff}");
                    var fmt = (cmbFormat?.SelectedItem?.ToString() ?? settings.Format) ?? "JPEG";
                    if (fmt.Equals("PNG", StringComparison.OrdinalIgnoreCase))
                    {
                        bmp.Save(filenameBase + ".png", ImageFormat.Png);
                    }
                    else
                    {
                        var quality = settings.JpegQuality;
                        var encoder = GetEncoder(ImageFormat.Jpeg);
                        if (encoder != null)
                        {
                            var ep = new EncoderParameters(1);
                            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                            bmp.Save(filenameBase + ".jpg", encoder, ep);
                        }
                        else
                        {
                            bmp.Save(filenameBase + ".jpg", ImageFormat.Jpeg);
                        }
                    }
                }

                try
                {
                    SessionManager.IncrementFrameCount(sessionFolder);
                }
                catch { }

                SafeInvoke(() =>
                {
                    lblStatus.Text = "Last saved: " + DateTime.Now.ToString("HH:mm:ss");
                });
            }
            catch (Exception ex)
            {
                SafeInvoke(() =>
                {
                    lblStatus.Text = "Error: " + ex.Message;
                });
            }
        }

        private static ImageCodecInfo? GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageDecoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                    return codec;
            }
            return null;
        }

        private void trkQuality_Scroll(object? sender, EventArgs e)
        {
            SafeInvoke(() =>
            {
                numQuality.Value = trkQuality.Value;
                settings.JpegQuality = trkQuality.Value;
            });
        }

        private void numQuality_ValueChanged(object? sender, EventArgs e)
        {
            SafeInvoke(() =>
            {
                trkQuality.Value = (int)numQuality.Value;
                settings.JpegQuality = (int)numQuality.Value;
            });
        }

        private void numInterval_ValueChanged(object? sender, EventArgs e)
        {
            UpdateEstimate();
        }

        private void numDesiredSec_ValueChanged(object? sender, EventArgs e)
        {
            UpdateEstimate();
        }

        private void UpdateEstimate()
        {
            int interval = (int)(numInterval?.Value ?? 5);
            int videoFps = 30;
            double videoSecondsPerHour = 3600.0 / (interval * videoFps);
            int desired = (int)(numDesiredSec?.Value ?? 30);
            double framesNeeded = desired * videoFps;
            double captureSecondsNeeded = framesNeeded * interval;
            TimeSpan ts = TimeSpan.FromSeconds(captureSecondsNeeded);
            string formatted = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            SafeInvoke(() =>
            {
                lblEstimate.Text = $"At {interval}s interval → ~{Math.Round(videoSecondsPerHour, 2)}s video per hour. To get {desired}s output: {formatted}";
            });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == hotkeyId)
                {
                    if (_captureTimer != null) btnStop_Click(null, null);
                    else btnStart_Click(null, null);
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            btnStop_Click(null, null);
            try { UnregisterHotKey(this.Handle, hotkeyId); } catch { }
            SaveSettings();
            base.OnFormClosing(e);
        }

        // Add stubs if needed for other event handlers so build doesn’t break
        private void txtFfmpegPath_TextChanged(object sender, EventArgs e) { }
        private void MainForm_Load(object sender, EventArgs e) { }
        private void grpActions_Enter(object sender, EventArgs e) { }
    }
}
