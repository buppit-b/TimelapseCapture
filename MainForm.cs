using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading.Tasks;

namespace TimelapseCapture
{
    public partial class MainForm : Form
    {
        private Rectangle captureRegion = Rectangle.Empty;
        private System.Threading.Timer? _captureTimer;
        private CaptureSettings settings = new CaptureSettings();
        private string? _ffmpegPath;
        private string? _activeSessionFolder;
        private SessionInfo? _activeSession;

        public MainForm()
        {
            InitializeComponent();
            ApplyModernStyling();
            LoadSettings();
            WireInitialValues();
            CheckForActiveSession();
            UpdateEstimate();
            InitializeFfmpeg();
        }

        private void ApplyModernStyling()
        {
            foreach (Control c in this.Controls)
                c.Font = new Font("Segoe UI", 9f);
        }

        private async void InitializeFfmpeg()
        {
            _ffmpegPath = FfmpegRunner.FindFfmpeg(settings.FfmpegPath);

            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                if (lblStatus != null) lblStatus.Text = "Downloading FFmpeg...";
                var ffmpegDir = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
                _ffmpegPath = await FfmpegDownloader.EnsureFfmpegPresentAsync(ffmpegDir);

                if (!string.IsNullOrEmpty(_ffmpegPath))
                {
                    settings.FfmpegPath = _ffmpegPath;
                    SaveSettings();
                    if (lblStatus != null) lblStatus.Text = "FFmpeg ready";
                }
                else
                {
                    if (lblStatus != null) lblStatus.Text = "FFmpeg not available - please browse to ffmpeg.exe";
                }
            }
            else
            {
                UpdateStatusDisplay();
            }

            if (txtFfmpegPath != null) txtFfmpegPath.Text = _ffmpegPath ?? "";
        }

        private void CheckForActiveSession()
        {
            if (string.IsNullOrEmpty(settings.SaveFolder))
                return;

            var capturesRoot = Path.Combine(settings.SaveFolder, "captures");
            _activeSessionFolder = SessionManager.FindActiveSession(capturesRoot);

            if (_activeSessionFolder != null)
            {
                _activeSession = SessionManager.LoadSession(_activeSessionFolder);
                if (_activeSession != null)
                {
                    captureRegion = _activeSession.CaptureRegion;
                    if (lblRegion != null)
                        lblRegion.Text = $"Region: {captureRegion.Width}x{captureRegion.Height} at ({captureRegion.X},{captureRegion.Y})";

                    if (numInterval != null) numInterval.Value = _activeSession.IntervalSeconds;
                    if (cmbFormat != null) cmbFormat.SelectedItem = _activeSession.ImageFormat ?? "JPEG";
                    if (numQuality != null && _activeSession.ImageFormat == "JPEG")
                        numQuality.Value = _activeSession.JpegQuality;

                    UpdateStatusDisplay();
                }
            }
        }

        private void WireInitialValues()
        {
            if (cmbFormat != null) cmbFormat.SelectedItem = settings.Format ?? "JPEG";
            if (numInterval != null) numInterval.Value = settings.IntervalSeconds > 0 ? settings.IntervalSeconds : 5;
            if (numDesiredSec != null) numDesiredSec.Value = 30;

            if (settings.Region.HasValue && _activeSession == null)
            {
                captureRegion = settings.Region.Value;
                if (lblRegion != null)
                    lblRegion.Text = $"Region: {captureRegion.Width}x{captureRegion.Height} at ({captureRegion.X},{captureRegion.Y})";
            }

            if (!string.IsNullOrEmpty(settings.SaveFolder))
            {
                if (lblFolder != null) lblFolder.Text = "Save to: " + settings.SaveFolder;
            }

            if (trkQuality != null) trkQuality.Value = settings.JpegQuality;
            if (numQuality != null) numQuality.Value = settings.JpegQuality;
            UpdateQualityControls();
        }

        private void LoadSettings() => settings = SettingsManager.Load();

        private void SaveSettings()
        {
            settings.Region = captureRegion;
            settings.IntervalSeconds = (int)(numInterval?.Value ?? 5);
            settings.Format = cmbFormat?.SelectedItem?.ToString();
            settings.JpegQuality = (int)(numQuality?.Value ?? 90);
            settings.FfmpegPath = _ffmpegPath;
            SettingsManager.Save(settings);
        }

        private void btnSelectRegion_Click(object? sender, EventArgs e)
        {
            if (IsCapturing)
            {
                MessageBox.Show("Cannot change region while capturing. Stop capture first.",
                    "Cannot Change Region", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_activeSession != null && captureRegion != Rectangle.Empty)
            {
                var result = MessageBox.Show(
                    $"An active session exists with {_activeSession.FramesCaptured} frames.\n\n" +
                    "Changing the region will require starting a new session.\n\n" +
                    "Do you want to:\n" +
                    "• YES: Close current session and start fresh\n" +
                    "• NO: Keep current session and cancel region change",
                    "Active Session Detected",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No)
                    return;

                if (_activeSessionFolder != null)
                    SessionManager.MarkSessionInactive(_activeSessionFolder);
                _activeSession = null;
                _activeSessionFolder = null;
            }

            Hide();
            Task.Delay(200).Wait();
            using (var selector = new RegionSelector())
            {
                if (selector.ShowDialog() == DialogResult.OK)
                {
                    captureRegion = selector.SelectedRegion;
                    if (lblRegion != null)
                        lblRegion.Text = $"Region: {captureRegion.Width}x{captureRegion.Height} at ({captureRegion.X},{captureRegion.Y})";
                    settings.Region = captureRegion;
                    SaveSettings();
                }
            }
            Show();
            UpdateStatusDisplay();
            UpdateEstimate();
        }

        private void btnChooseFolder_Click(object? sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                if (!string.IsNullOrEmpty(settings.SaveFolder))
                    fbd.SelectedPath = settings.SaveFolder;

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    settings.SaveFolder = fbd.SelectedPath;
                    if (lblFolder != null) lblFolder.Text = "Save to: " + settings.SaveFolder;
                    SaveSettings();
                    CheckForActiveSession();
                }
            }
        }

        private void cmbFormat_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_activeSession != null && !IsCapturing)
            {
                var newFormat = cmbFormat?.SelectedItem?.ToString();
                if (newFormat != _activeSession.ImageFormat)
                {
                    MessageBox.Show(
                        $"Active session uses {_activeSession.ImageFormat} format.\n" +
                        $"Format change requires starting a new session.",
                        "Format Mismatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    if (cmbFormat != null) cmbFormat.SelectedItem = _activeSession.ImageFormat;
                    return;
                }
            }
            UpdateQualityControls();
        }

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
                MessageBox.Show("Please select both a region and a save folder.",
                    "Missing settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int intervalSec = (int)(numInterval?.Value ?? 5);
            var format = cmbFormat?.SelectedItem?.ToString() ?? "JPEG";
            var quality = (int)(numQuality?.Value ?? 90);

            var capturesRoot = Path.Combine(settings.SaveFolder, "captures");

            if (_activeSession == null)
            {
                _activeSessionFolder = SessionManager.CreateNewSession(
                    capturesRoot, intervalSec, captureRegion, format, quality);
                _activeSession = SessionManager.LoadSession(_activeSessionFolder);
            }
            else
            {
                if (!SessionManager.ValidateSessionSettings(_activeSession, captureRegion, format, quality))
                {
                    var result = MessageBox.Show(
                        "Current settings don't match the active session.\n\n" +
                        "Start a new session?",
                        "Settings Mismatch",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (result == DialogResult.Yes)
                    {
                        SessionManager.MarkSessionInactive(_activeSessionFolder!);
                        _activeSessionFolder = SessionManager.CreateNewSession(
                            capturesRoot, intervalSec, captureRegion, format, quality);
                        _activeSession = SessionManager.LoadSession(_activeSessionFolder);
                    }
                    else
                    {
                        return;
                    }
                }
            }

            LockCaptureUI(true);

            settings.IntervalSeconds = intervalSec;
            settings.Format = format;
            settings.JpegQuality = quality;
            SaveSettings();

            var intervalMs = intervalSec * 1000;
            _captureTimer = new System.Threading.Timer(CaptureFrame, null, 0, intervalMs);
            UpdateStatusDisplay();
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

            LockCaptureUI(false);
            UpdateStatusDisplay();
            SaveSettings();
            UpdateEstimate();
        }

        private void LockCaptureUI(bool locked)
        {
            if (numInterval != null) numInterval.Enabled = !locked;
            if (cmbFormat != null) cmbFormat.Enabled = !locked;
            if (trkQuality != null) trkQuality.Enabled = !locked && (cmbFormat?.SelectedItem?.ToString() == "JPEG");
            if (numQuality != null) numQuality.Enabled = !locked && (cmbFormat?.SelectedItem?.ToString() == "JPEG");
            if (btnSelectRegion != null) btnSelectRegion.Enabled = !locked;
            if (btnChooseFolder != null) btnChooseFolder.Enabled = !locked;
            if (btnBrowseFfmpeg != null) btnBrowseFfmpeg.Enabled = !locked;
            if (btnStart != null) btnStart.Enabled = !locked;
            if (btnStop != null) btnStop.Enabled = locked;
        }

        private void UpdateStatusDisplay()
        {
            if (lblStatus == null) return;

            if (IsCapturing)
            {
                lblStatus.Text = $"Capturing... ({_activeSession?.FramesCaptured ?? 0} frames)";
            }
            else if (_activeSession != null)
            {
                lblStatus.Text = $"Session: {_activeSession.Name} ({_activeSession.FramesCaptured} frames)";
            }
            else
            {
                lblStatus.Text = "Ready - No active session";
            }
        }

        private void btnOpenFolder_Click(object? sender, EventArgs e)
        {
            string folderToOpen;

            if (_activeSessionFolder != null && Directory.Exists(_activeSessionFolder))
            {
                folderToOpen = _activeSessionFolder;
            }
            else if (!string.IsNullOrEmpty(settings.SaveFolder) && Directory.Exists(settings.SaveFolder))
            {
                folderToOpen = settings.SaveFolder;
            }
            else
            {
                MessageBox.Show("No folder to open.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo() { FileName = folderToOpen, UseShellExecute = true });
        }

        private void btnBrowseFfmpeg_Click(object? sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "ffmpeg.exe|ffmpeg.exe|All files|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    _ffmpegPath = ofd.FileName;
                    if (txtFfmpegPath != null) txtFfmpegPath.Text = _ffmpegPath;
                    settings.FfmpegPath = _ffmpegPath;
                    SaveSettings();
                }
            }
        }

        private async void btnEncode_Click(object? sender, EventArgs e)
        {
            try
            {
                _ffmpegPath = FfmpegRunner.FindFfmpeg(settings.FfmpegPath);

                if (string.IsNullOrEmpty(_ffmpegPath) || !File.Exists(_ffmpegPath))
                {
                    MessageBox.Show("FFmpeg not found. Please use 'Browse FFmpeg' to locate ffmpeg.exe",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (string.IsNullOrEmpty(settings.SaveFolder))
                {
                    MessageBox.Show("Please select a save folder first.",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var capturesRoot = Path.Combine(settings.SaveFolder, "captures");
                var sessions = SessionManager.GetAllSessions(capturesRoot);

                if (sessions.Count == 0)
                {
                    MessageBox.Show("No sessions found. Capture some frames first.",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string sessionToEncode;

                if (_activeSession != null && _activeSession.FramesCaptured > 0)
                {
                    sessionToEncode = _activeSessionFolder!;
                }
                else if (sessions.Count == 1)
                {
                    sessionToEncode = sessions[0];
                }
                else
                {
                    sessionToEncode = ShowSessionPicker(sessions);
                    if (string.IsNullOrEmpty(sessionToEncode))
                        return;
                }

                await EncodeSession(sessionToEncode);
            }
            catch (Exception ex)
            {
                if (btnEncode != null) btnEncode.Enabled = true;
                if (lblStatus != null) lblStatus.Text = "Error";
                MessageBox.Show("Error while encoding:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string? ShowSessionPicker(System.Collections.Generic.List<string> sessions)
        {
            using (var picker = new Form())
            {
                picker.Text = "Select Session to Encode";
                picker.Size = new Size(500, 300);
                picker.StartPosition = FormStartPosition.CenterParent;
                picker.FormBorderStyle = FormBorderStyle.FixedDialog;
                picker.MaximizeBox = false;
                picker.MinimizeBox = false;

                var listBox = new ListBox
                {
                    Dock = DockStyle.Fill,
                    Font = new Font("Consolas", 9f)
                };

                foreach (var sessionFolder in sessions)
                {
                    var session = SessionManager.LoadSession(sessionFolder);
                    if (session != null)
                    {
                        var frameCount = Directory.GetFiles(sessionFolder, "*.jpg").Length;
                        listBox.Items.Add($"{session.Name} - {frameCount} frames - {session.StartTime.ToLocalTime():g}");
                        listBox.Tag = listBox.Tag == null ? sessionFolder : listBox.Tag + "|" + sessionFolder;
                    }
                }

                var btnOk = new Button
                {
                    Text = "Encode Selected",
                    DialogResult = DialogResult.OK,
                    Dock = DockStyle.Bottom
                };

                picker.Controls.Add(listBox);
                picker.Controls.Add(btnOk);

                if (picker.ShowDialog() == DialogResult.OK && listBox.SelectedIndex >= 0)
                {
                    var folders = listBox.Tag?.ToString()?.Split('|');
                    return folders?[listBox.SelectedIndex];
                }

                return null;
            }
        }

        private async Task EncodeSession(string sessionFolder)
        {
            var session = SessionManager.LoadSession(sessionFolder);
            if (session == null)
            {
                MessageBox.Show("Failed to load session information.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var jpgFiles = Directory.GetFiles(sessionFolder, "*.jpg", SearchOption.TopDirectoryOnly);
            if (jpgFiles.Length == 0)
            {
                MessageBox.Show("No frames found in this session.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var timelapsesFolder = Path.Combine(settings.SaveFolder!, "timelapses");
            Directory.CreateDirectory(timelapsesFolder);

            Array.Sort(jpgFiles);

            var fileListPath = Path.Combine(sessionFolder, "filelist.txt");
            using (var writer = new StreamWriter(fileListPath))
            {
                foreach (var file in jpgFiles)
                {
                    var filePath = file.Replace("\\", "/");
                    writer.WriteLine($"file '{filePath}'");
                }
            }

            var output = Path.Combine(timelapsesFolder, $"{session.Name}.mp4");
            int framerate = session.VideoFps;

            string args = $"-y -r {framerate} -f concat -safe 0 -i \"{fileListPath}\" -c:v libx264 -pix_fmt yuv420p \"{output}\"";

            if (lblStatus != null) lblStatus.Text = "Encoding...";
            if (btnEncode != null) btnEncode.Enabled = false;

            var result = await FfmpegRunner.RunFfmpegAsync(_ffmpegPath!, args);

            if (btnEncode != null) btnEncode.Enabled = true;

            if (result.exitCode == 0)
            {
                try { File.Delete(fileListPath); } catch { }

                if (lblStatus != null) lblStatus.Text = "Encoding complete!";
                var dialogResult = MessageBox.Show(
                    $"Timelapse created successfully!\n\n{Path.GetFileName(output)}\n\nOpen the video now?",
                    "Success", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                if (dialogResult == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo() { FileName = output, UseShellExecute = true });
                }
            }
            else
            {
                if (lblStatus != null) lblStatus.Text = "Encoding failed";
                MessageBox.Show("FFmpeg encoding failed:\n" + result.error, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CaptureFrame(object? state)
        {
            try
            {
                if (_activeSessionFolder == null) return;

                using (var bmp = new Bitmap(captureRegion.Width, captureRegion.Height))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(captureRegion.X, captureRegion.Y, 0, 0, captureRegion.Size, CopyPixelOperation.SourceCopy);
                    }

                    var filename = Path.Combine(_activeSessionFolder, $"frame_{DateTime.Now:yyyyMMdd_HHmmss_fff}");
                    var format = _activeSession?.ImageFormat ?? "JPEG";

                    if (format == "PNG")
                    {
                        filename += ".png";
                        bmp.Save(filename, ImageFormat.Png);
                    }
                    else
                    {
                        filename += ".jpg";
                        var quality = _activeSession?.JpegQuality ?? 90;
                        var jpgEncoder = GetEncoder(ImageFormat.Jpeg);
                        if (jpgEncoder != null)
                        {
                            var encoderParams = new EncoderParameters(1);
                            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                            bmp.Save(filename, jpgEncoder, encoderParams);
                        }
                        else
                        {
                            bmp.Save(filename, ImageFormat.Jpeg);
                        }
                    }
                }

                SessionManager.IncrementFrameCount(_activeSessionFolder);
                _activeSession = SessionManager.LoadSession(_activeSessionFolder);

                if (!IsDisposed)
                {
                    try
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            UpdateStatusDisplay();
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
            int desiredSeconds = (int)(numDesiredSec?.Value ?? 30);
            int videoFps = 30;

            double capturesPerHour = 3600.0 / interval;
            double videoSecondsPerHour = capturesPerHour / videoFps;

            double framesNeeded = desiredSeconds * videoFps;
            double captureSecondsNeeded = framesNeeded * interval;
            TimeSpan captureTime = TimeSpan.FromSeconds(captureSecondsNeeded);

            if (lblEstimate != null)
            {
                string captureTimeStr = captureTime.Hours > 0
                    ? $"{captureTime.Hours}h {captureTime.Minutes}m {captureTime.Seconds}s"
                    : captureTime.Minutes > 0
                        ? $"{captureTime.Minutes}m {captureTime.Seconds}s"
                        : $"{captureTime.Seconds}s";

                lblEstimate.Text = $"At {interval}s capture interval → {Math.Round(videoSecondsPerHour, 1)}s of video per hour captured (at {videoFps} fps). To get {desiredSeconds}s of output you must capture for: {captureTimeStr}";
            }
        }

        private void txtFfmpegPath_TextChanged(object sender, EventArgs e) { }
        private void MainForm_Load(object sender, EventArgs e) { }
        private void grpActions_Enter(object sender, EventArgs e) { }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopCapture();

            if (_activeSessionFolder != null)
            {
                SessionManager.MarkSessionInactive(_activeSessionFolder);
            }

            SaveSettings();
            base.OnFormClosing(e);
        }
    }
}