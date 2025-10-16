// --- MainForm.cs (Chunk 1/3) ---
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

        // Initialize or locate ffmpeg binary. Uses your existing downloader helper if needed.
        private async void InitializeFfmpeg()
        {
            _ffmpegPath = FfmpegRunner.FindFfmpeg(settings.FfmpegPath);
            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                if (lblStatus != null) lblStatus.Text = "Downloading/locating FFmpeg...";
                // Use EnsureFfmpegPresentAsync which exists in your repo and returns path or null.
                var dirTarget = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
                _ffmpegPath = await FfmpegDownloader.EnsureFfmpegPresentAsync(dirTarget);
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

            if (txtFfmpegPath != null)
                txtFfmpegPath.Text = _ffmpegPath ?? "";
        }

        private void CheckForActiveSession()
        {
            if (string.IsNullOrEmpty(settings.SaveFolder)) return;
            var capturesRoot = Path.Combine(settings.SaveFolder, "captures");
            _activeSessionFolder = SessionManager.FindActiveSession(capturesRoot);
            if (_activeSessionFolder != null)
            {
                _activeSession = SessionManager.LoadSession(_activeSessionFolder);
                if (_activeSession != null)
                {
                    captureRegion = _activeSession.CaptureRegion;
                    if (lblRegion != null)
                        lblRegion.Text = $"Region: {captureRegion.Width}×{captureRegion.Height} at ({captureRegion.X},{captureRegion.Y})";

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

            // ==== Claude's change: load and defensively validate region from settings ====
            if (settings.Region.HasValue && _activeSession == null)
            {
                var r = settings.Region.Value;

                // Defensive validation: width/height should be even and > 0
                if (!IsValidRegion(r))
                {
                    if ((r.Width & 1) == 1) r.Width = Math.Max(2, r.Width - 1);
                    if ((r.Height & 1) == 1) r.Height = Math.Max(2, r.Height - 1);
                    settings.Region = r;
                    SaveSettings();
                }

                captureRegion = r;
                if (lblRegion != null)
                    lblRegion.Text = $"Region: {captureRegion.Width}×{captureRegion.Height} at ({captureRegion.X},{captureRegion.Y})";
            }

            if (!string.IsNullOrEmpty(settings.SaveFolder))
            {
                if (lblFolder != null)
                    lblFolder.Text = "Save to: " + settings.SaveFolder;
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

        // Helper: validate region dimensions (even width/height and positive)
        private bool IsValidRegion(Rectangle r)
        {
            return r.Width > 0 && r.Height > 0 && (r.Width & 1) == 0 && (r.Height & 1) == 0;
        }
        // --- MainForm.cs (Chunk 2/3) ---
        // Existing Select Region handler (keeps your original name and flow) with Claude's selection semantics
        private void btnSelectRegion_Click(object? sender, EventArgs e)
        {
            if (IsCapturing)
            {
                MessageBox.Show("Cannot change region while capturing. Stop capture first.", "Cannot Change Region", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

                if (result == DialogResult.No) return;

                if (_activeSessionFolder != null) SessionManager.MarkSessionInactive(_activeSessionFolder);
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
                    // RegionSelector guarantees even dimensions so no further validation required here.
                    if (lblRegion != null)
                        lblRegion.Text = $"Region: {captureRegion.Width}×{captureRegion.Height} at ({captureRegion.X},{captureRegion.Y})";

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
                if (!string.IsNullOrEmpty(settings.SaveFolder)) fbd.SelectedPath = settings.SaveFolder;
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
                        "Format Mismatch",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
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

        // Start (existing name preserved)
        private void btnStart_Click(object? sender, EventArgs e)
        {
            if (captureRegion.Width == 0 || string.IsNullOrEmpty(settings.SaveFolder))
            {
                MessageBox.Show("Please select both a region and a save folder.", "Missing settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int intervalSec = (int)(numInterval?.Value ?? 5);
            var format = cmbFormat?.SelectedItem?.ToString() ?? "JPEG";
            var quality = (int)(numQuality?.Value ?? 90);
            var capturesRoot = Path.Combine(settings.SaveFolder, "captures");

            if (_activeSession == null)
            {
                _activeSessionFolder = SessionManager.CreateNewSession(capturesRoot, intervalSec, captureRegion, format, quality);
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
                        _activeSessionFolder = SessionManager.CreateNewSession(capturesRoot, intervalSec, captureRegion, format, quality);
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
            if (btnEncode != null) btnEncode.Enabled = !locked;
        }

        private void CaptureFrame(object? state)
        {
            if (_activeSession == null || _captureTimer == null) return;

            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = Path.Combine(_activeSessionFolder!, $"{timestamp}.jpg");

                using (var bmp = CaptureScreen())
                {
                    bmp.Save(fileName, ImageFormat.Jpeg);
                }

                // Increment frame count via SessionManager helper so session file is updated
                SessionManager.IncrementFrameCount(_activeSessionFolder!);
                // reload session info in memory
                _activeSession = SessionManager.LoadSession(_activeSessionFolder);

                // Update UI on the UI thread using BeginInvoke
                BeginInvoke(new Action(() =>
                {
                    UpdateStatusDisplay();
                }));
            }
            catch (Exception ex)
            {
                // Log or handle errors during capture
                System.Diagnostics.Debug.WriteLine($"Capture error: {ex.Message}");
            }
        }

        private Bitmap CaptureScreen()
        {
            var bmp = new Bitmap(captureRegion.Width, captureRegion.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(captureRegion.Location, Point.Empty, captureRegion.Size);
            }
            return bmp;
        }

        private void UpdateStatusDisplay()
        {
            if (lblStatus == null) return;
            if (IsCapturing)
                lblStatus.Text = $"Capturing ({_activeSession?.FramesCaptured ?? 0} frames)";
            else if (_activeSession != null)
                lblStatus.Text = $"Session: {_activeSession.Name} ({_activeSession.FramesCaptured} frames)";
            else
                lblStatus.Text = "Ready - No active session";
        }

        private void UpdateEstimate()
        {
            if (lblEstimate == null) return;

            if (_activeSession != null && _activeSession.FramesCaptured > 0)
            {
                int desiredSec = (int)(numDesiredSec?.Value ?? 30);
                int interval = _activeSession.IntervalSeconds;
                int frames = (int)_activeSession.FramesCaptured;

                // Calculate current video length at different FPS options
                double videoAt25fps = frames / 25.0;
                double videoAt30fps = frames / 30.0;
                double videoAt60fps = frames / 60.0;

                // Calculate capture time
                double captureTimeMinutes = (frames * interval) / 60.0;

                lblEstimate.Text = $"{frames} frames • {captureTimeMinutes:F1}min capture\n" +
                                  $"Video: {videoAt25fps:F1}s @25fps | {videoAt30fps:F1}s @30fps | {videoAt60fps:F1}s @60fps";
            }
            else if (_activeSession != null)
            {
                lblEstimate.Text = "Session active • 0 frames captured";
            }
            else
            {
                int desiredSec = (int)(numDesiredSec?.Value ?? 30);
                int interval = (int)(numInterval?.Value ?? 5);
                int neededFrames = desiredSec * 25; // Assuming 25 FPS output
                double captureTimeMinutes = (neededFrames * interval) / 60.0;

                lblEstimate.Text = $"Need {neededFrames} frames for {desiredSec}s video @25fps\n" +
                                  $"≈ {captureTimeMinutes:F0} minutes of capture";
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
                // Ensure ffmpeg path is available (use configured path, then try downloader if not found)
                _ffmpegPath = FfmpegRunner.FindFfmpeg(settings.FfmpegPath);
                if (string.IsNullOrEmpty(_ffmpegPath) || !File.Exists(_ffmpegPath))
                {
                    // attempt to download/extract into app ffmpeg folder
                    var ffmpegDir = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
                    var found = await FfmpegDownloader.EnsureFfmpegPresentAsync(ffmpegDir);
                    if (!string.IsNullOrEmpty(found))
                    {
                        _ffmpegPath = found;
                        settings.FfmpegPath = _ffmpegPath;
                        SaveSettings();
                    }
                }

                if (string.IsNullOrEmpty(_ffmpegPath) || !File.Exists(_ffmpegPath))
                {
                    MessageBox.Show("FFmpeg not found. Please use 'Browse FFmpeg' to locate ffmpeg.exe", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (string.IsNullOrEmpty(settings.SaveFolder))
                {
                    MessageBox.Show("Please select a save folder first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string sessionFolder = _activeSessionFolder ?? settings.SaveFolder;
                var jpgFiles = Directory.GetFiles(sessionFolder, "*.jpg");
                if (jpgFiles.Length == 0)
                {
                    MessageBox.Show("No images found to encode. Capture some frames first!", "No Images", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Final defensive validation of region before encoding
                if (!IsValidRegion(captureRegion))
                {
                    MessageBox.Show("Invalid capture region. Please select a new region.", "Invalid Region", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Build filelist.txt (FFmpeg concat demuxer expects lines like: file '/path/to/file.jpg')
                string fileListPath = Path.Combine(sessionFolder, "filelist.txt");
                using (var writer = new StreamWriter(fileListPath, false))
                {
                    // sort filenames to ensure chronological order
                    Array.Sort(jpgFiles, StringComparer.Ordinal);
                    foreach (var f in jpgFiles)
                    {
                        // escape single quotes by closing and re-opening; simpler is to wrap with double quotes if path has no quotes
                        writer.WriteLine($"file '{f.Replace("'", "'\\''")}'");
                    }
                }

                string outputPath = Path.Combine(sessionFolder, $"timelapse_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

                // Build ffmpeg args: use your preferred defaults (25 fps, crf 23, preset medium)
                string ffmpegArgs = $"-y -f concat -safe 0 -i \"{fileListPath}\" -r 25 -c:v libx264 -crf 23 -preset medium \"{outputPath}\"";

                // Run ffmpeg
                var result = await FfmpegRunner.RunFfmpegAsync(_ffmpegPath, ffmpegArgs);

                if (result.exitCode == 0)
                {
                    MessageBox.Show($"Video encoded successfully!\n\nSaved to:\n{outputPath}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Process.Start("explorer.exe", $"/select,\"{outputPath}\"");
                }
                else
                {
                    MessageBox.Show($"FFmpeg encoding failed.\n\nStdout:\n{result.output}\n\nStderr:\n{result.error}", "Encoding Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during encoding: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void trkQuality_Scroll(object? sender, EventArgs e)
        {
            if (numQuality != null && trkQuality != null)
            {
                numQuality.Value = trkQuality.Value;
                if (lblQuality != null)
                    lblQuality.Text = $"JPEG Quality: {trkQuality.Value}";
            }
        }

        private void numQuality_ValueChanged(object? sender, EventArgs e)
        {
            if (numQuality != null && trkQuality != null)
            {
                trkQuality.Value = (int)numQuality.Value;
                if (lblQuality != null)
                    lblQuality.Text = $"JPEG Quality: {(int)numQuality.Value}";
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _captureTimer?.Dispose();

            if (_activeSessionFolder != null)
                SessionManager.MarkSessionInactive(_activeSessionFolder);
        }
    }
}
