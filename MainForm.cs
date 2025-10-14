// MainForm.cs
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace TimelapseCapture
{
    public partial class MainForm : Form
    {
        private Rectangle captureRegion = Rectangle.Empty;
        private System.Threading.Timer? _captureTimer;
        private CaptureSettings settings = new CaptureSettings();
        private int hotkeyId = 0x1000;

        // Win32 hotkey constants
        private const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public MainForm()
        {
            InitializeComponent();
            ApplyModernStyling();
            LoadSettings();
            WireInitialValues();
            // register hotkey (use settings, ignore failure)
            try
            {
                RegisterHotKey(this.Handle, hotkeyId, (uint)settings.HotkeyModifiers, (uint)settings.HotkeyKey);
            }
            catch { }
            UpdateEstimate();
        }

        private void ApplyModernStyling()
        {
            foreach (Control c in this.Controls) c.Font = new Font("Segoe UI", 9f);
        }

        private void WireInitialValues()
        {
            if (cmbFormat != null) cmbFormat.SelectedItem = settings.Format ?? "JPEG";
            if (numInterval != null) numInterval.Value = settings.IntervalSeconds > 0 ? settings.IntervalSeconds : 5;
            if (settings.Region.HasValue)
            {
                captureRegion = settings.Region.Value;
                if (lblRegion != null) lblRegion.Text = $"Region: {captureRegion.Width}x{captureRegion.Height} at ({captureRegion.X},{captureRegion.Y})";
            }
            if (!string.IsNullOrEmpty(settings.SaveFolder))
            {
                if (lblFolder != null) lblFolder.Text = "Save to: " + settings.SaveFolder;
            }
            if (trkQuality != null) trkQuality.Value = settings.JpegQuality;
            if (numQuality != null) numQuality.Value = settings.JpegQuality;
            if (txtFfmpegPath != null) txtFfmpegPath.Text = settings.FfmpegPath ?? "";
            UpdateQualityControls();
        }

        private void LoadSettings() => settings = SettingsManager.Load();

        private void SaveSettings()
        {
            settings.Region = captureRegion;
            settings.SaveFolder = settings.SaveFolder;
            settings.IntervalSeconds = (int)(numInterval?.Value ?? 5);
            settings.Format = cmbFormat?.SelectedItem?.ToString();
            settings.JpegQuality = (int)(numQuality?.Value ?? 90);
            settings.FfmpegPath = txtFfmpegPath?.Text;
            SettingsManager.Save(settings);
        }

        private void btnSelectRegion_Click(object? sender, EventArgs e)
        {
            Hide();
            Task.Delay(200).Wait();
            using (var selector = new RegionSelector())
            {
                if (selector.ShowDialog() == DialogResult.OK)
                {
                    captureRegion = selector.SelectedRegion;
                    if (lblRegion != null) lblRegion.Text = $"Region: {captureRegion.Width}x{captureRegion.Height} at ({captureRegion.X},{captureRegion.Y})";
                    settings.Region = captureRegion;
                }
            }
            Show();
            UpdateEstimate();
        }

        private void btnChooseFolder_Click(object? sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                if (!string.IsNullOrEmpty(settings.SaveFolder)) fbd.SelectedPath = settings.SaveFolder;
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    settings.SaveFolder = fbd.SelectedPath;
                    if (lblFolder != null) lblFolder.Text = "Save to: " + settings.SaveFolder;
                }
            }
        }

        private void cmbFormat_SelectedIndexChanged(object? sender, EventArgs e) => UpdateQualityControls();

        private void UpdateQualityControls()
        {
            bool jpeg = (cmbFormat?.SelectedItem?.ToString() ?? "JPEG") == "JPEG";
            if (trkQuality != null) trkQuality.Enabled = jpeg && !IsCapturing;
            if (numQuality != null) numQuality.Enabled = jpeg && !IsCapturing;
            if (lblQuality != null) lblQuality.Enabled = jpeg;
        }

        private bool IsCapturing => _captureTimer != null;

        private void btnStart_Click(object? sender, EventArgs e)
        {
            if (captureRegion.Width == 0 || string.IsNullOrEmpty(settings.SaveFolder))
            {
                MessageBox.Show("Please select both a region and a save folder.", "Missing settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int intervalSec = (int)(numInterval?.Value ?? 5);
            if (intervalSec <= 0)
            {
                MessageBox.Show("Invalid interval.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // lock UI fields while capturing
            if (numInterval != null) numInterval.Enabled = false;
            if (cmbFormat != null) cmbFormat.Enabled = false;
            if (trkQuality != null) trkQuality.Enabled = false;
            if (numQuality != null) numQuality.Enabled = false;
            if (btnSelectRegion != null) btnSelectRegion.Enabled = false;
            if (btnChooseFolder != null) btnChooseFolder.Enabled = false;
            if (btnBrowseFfmpeg != null) btnBrowseFfmpeg.Enabled = false;

            if (btnStart != null) btnStart.Enabled = false;
            if (btnStop != null) btnStop.Enabled = true;
            if (lblStatus != null) lblStatus.Text = "Capturing...";

            settings.SaveFolder = settings.SaveFolder;
            settings.IntervalSeconds = intervalSec;
            settings.Format = cmbFormat?.SelectedItem?.ToString();
            settings.JpegQuality = (int)(numQuality?.Value ?? 90);
            SaveSettings();

            var intervalMs = intervalSec * 1000;
            _captureTimer = new System.Threading.Timer(CaptureFrame, null, 0, intervalMs);
            UpdateEstimate();
        }

        private void btnStop_Click(object? sender, EventArgs e) => StopCapture();

        private void StopCapture()
        {
            if (_captureTimer != null)
            {
                _captureTimer.Dispose();
                _captureTimer = null;
            }

            if (numInterval != null) numInterval.Enabled = true;
            if (cmbFormat != null) cmbFormat.Enabled = true;
            if (trkQuality != null) trkQuality.Enabled = (cmbFormat?.SelectedItem?.ToString() ?? "JPEG") == "JPEG";
            if (numQuality != null) numQuality.Enabled = trkQuality.Enabled;
            if (btnSelectRegion != null) btnSelectRegion.Enabled = true;
            if (btnChooseFolder != null) btnChooseFolder.Enabled = true;
            if (btnBrowseFfmpeg != null) btnBrowseFfmpeg.Enabled = true;

            if (btnStart != null) btnStart.Enabled = true;
            if (btnStop != null) btnStop.Enabled = false;
            if (lblStatus != null) lblStatus.Text = "Stopped.";
            SaveSettings();
            UpdateEstimate();
        }

        private void btnOpenFolder_Click(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(settings.SaveFolder) && Directory.Exists(settings.SaveFolder))
            {
                Process.Start(new ProcessStartInfo() { FileName = settings.SaveFolder, UseShellExecute = true });
            }
            else
            {
                MessageBox.Show("Save folder not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void btnBrowseFfmpeg_Click(object? sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "ffmpeg.exe|ffmpeg.exe|All files|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    if (txtFfmpegPath != null) txtFfmpegPath.Text = ofd.FileName;
                    settings.FfmpegPath = ofd.FileName;
                    SaveSettings();
                }
            }
        }

        private async void btnEncode_Click(object? sender, EventArgs e)
        {
            try
            {
                // Download or verify ffmpeg binaries in the default location (FFmpeg.ExecutablesPath)
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);

                // Optionally use a custom path from settings, if defined
                if (!string.IsNullOrEmpty(settings.FfmpegPath))
                {
                    FFmpeg.SetExecutablesPath(settings.FfmpegPath);
                }

                string ffmpegPath = FFmpeg.ExecutablesPath;
                if (string.IsNullOrEmpty(ffmpegPath) || !Directory.Exists(ffmpegPath))
                {
                    MessageBox.Show("ffmpeg not available.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var capturesFolder = Path.Combine(settings.SaveFolder ?? AppContext.BaseDirectory, "captures");
                var timelapsesFolder = Path.Combine(settings.SaveFolder ?? AppContext.BaseDirectory, "timelapses");
                Directory.CreateDirectory(capturesFolder);
                Directory.CreateDirectory(timelapsesFolder);

                var pattern = Path.Combine(capturesFolder, "*.jpg");
                var output = Path.Combine(timelapsesFolder, $"timelapse_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
                int framerate = 30;
                string args = $"-y -framerate {framerate} -pattern_type glob -i \"{pattern}\" -c:v libx264 -pix_fmt yuv420p \"{output}\"";

                if (lblStatus != null) lblStatus.Text = "Encoding...";
                var result = await FfmpegRunner.RunFfmpegAsync(ffmpegPath, args);
                if (result.exitCode == 0)
                {
                    if (lblStatus != null) lblStatus.Text = "Encoding complete: " + Path.GetFileName(output);
                    Process.Start(new ProcessStartInfo() { FileName = output, UseShellExecute = true });
                }
                else
                {
                    if (lblStatus != null) lblStatus.Text = "ffmpeg error: see log";
                    MessageBox.Show("ffmpeg failed:\n" + result.error, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error while encoding:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private void CaptureFrame(object? state)
        {
            try
            {
                var capturesFolder = Path.Combine(settings.SaveFolder ?? AppContext.BaseDirectory, "captures");
                Directory.CreateDirectory(capturesFolder);

                using (var bmp = new Bitmap(captureRegion.Width, captureRegion.Height))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(captureRegion.X, captureRegion.Y, 0, 0, captureRegion.Size, CopyPixelOperation.SourceCopy);
                    }

                    var filename = Path.Combine(capturesFolder, $"frame_{DateTime.Now:yyyyMMdd_HHmmss_fff}");
                    var format = (cmbFormat?.SelectedItem?.ToString() ?? settings.Format) ?? "JPEG";

                    if (format == "PNG")
                    {
                        filename += ".png";
                        bmp.Save(filename, ImageFormat.Png);
                    }
                    else
                    {
                        filename += ".jpg";
                        var quality = settings.JpegQuality;
                        var jpgEncoder = GetEncoder(ImageFormat.Jpeg);
                        if (jpgEncoder != null)
                        {
                            var encoderParams = new EncoderParameters(1);
                            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                            bmp.Save(filename, jpgEncoder, encoderParams);
                        }
                        else
                        {
                            bmp.Save(filename, ImageFormat.Jpeg);
                        }
                    }
                }

                if (!IsDisposed)
                {
                    try
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            if (lblStatus != null) lblStatus.Text = "Last saved: " + DateTime.Now.ToString("HH:mm:ss");
                        }));
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    try
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            if (lblStatus != null) lblStatus.Text = "Error: " + ex.Message;
                        }));
                    }
                    catch { }
                }
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
            if (trkQuality != null && numQuality != null)
            {
                numQuality.Value = trkQuality.Value;
                settings.JpegQuality = trkQuality.Value;
            }
        }

        private void numQuality_ValueChanged(object? sender, EventArgs e)
        {
            if (trkQuality != null && numQuality != null)
            {
                trkQuality.Value = (int)numQuality.Value;
                settings.JpegQuality = (int)numQuality.Value;
            }
        }

        private void numInterval_ValueChanged(object? sender, EventArgs e) => UpdateEstimate();

        private void numDesiredSec_ValueChanged(object? sender, EventArgs e) => UpdateEstimate();

        private void UpdateEstimate()
        {
            int interval = (int)(numInterval?.Value ?? 5);
            int videoFps = 30;
            double videoSecondsPerHour = 3600.0 / (interval * videoFps);
            int desired = (int)(numDesiredSec?.Value ?? 30);
            double framesNeeded = desired * videoFps;
            double captureSecondsNeeded = framesNeeded * interval;
            TimeSpan ts = TimeSpan.FromSeconds(captureSecondsNeeded);
            string captureNeededStr = string.Format("{0:D2}:{1:D2}:{2:D2}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
            if (lblEstimate != null) lblEstimate.Text = $"At {interval}s capture interval → {Math.Round(videoSecondsPerHour, 2)}s of video per hour captured (at {videoFps} fps). To get {desired}s of output you must capture for: {captureNeededStr}";
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == hotkeyId)
                {
                    if (IsCapturing) StopCapture(); else btnStart?.PerformClick();
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopCapture();
            try { UnregisterHotKey(this.Handle, hotkeyId); } catch { }
            SaveSettings();
            base.OnFormClosing(e);
        }

        private void txtFfmpegPath_TextChanged(object sender, EventArgs e)
        {

        }

        private void MainForm_Load(object sender, EventArgs e)
        {

        }

        private void grpActions_Enter(object sender, EventArgs e)
        {

        }
    }
}
