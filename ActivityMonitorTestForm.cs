using System;
using System.Drawing;
using System.Windows.Forms;

namespace TimelapseCapture
{
    /// <summary>
    /// Test form for ActivityMonitor - shows real-time activity detection.
    /// Use this to verify the monitor works before full integration.
    /// </summary>
    public class ActivityMonitorTestForm : Form
    {
        private ActivityMonitor? _monitor;
        private System.Windows.Forms.Timer? _updateTimer;

        // UI Controls
        private Label lblStatus = null!;
        private Label lblTimeSince = null!;
        private Label lblActivityLog = null!;
        private Button btnStart = null!;
        private Button btnStop = null!;
        private CheckBox chkEnabled = null!;
        private CheckBox chkTrackMouseMove = null!;
        private NumericUpDown numIdleThreshold = null!;
        private TextBox txtLog = null!;

        public ActivityMonitorTestForm()
        {
            InitializeUI();
            _monitor = new ActivityMonitor();
            _monitor.ActivityDetected += OnActivityDetected;
            _monitor.IdleDetected += OnIdleDetected;
        }

        private void InitializeUI()
        {
            // Form setup
            this.Text = "Activity Monitor Test";
            this.Size = new Size(600, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormClosing += (s, e) =>
            {
                _monitor?.Dispose();
                _updateTimer?.Dispose();
            };

            // Status display
            lblStatus = new Label
            {
                Location = new Point(20, 20),
                Size = new Size(550, 40),
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                Text = "⚪ Not started",
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter
            };
            this.Controls.Add(lblStatus);

            // Time since label
            lblTimeSince = new Label
            {
                Location = new Point(20, 70),
                Size = new Size(550, 30),
                Font = new Font("Segoe UI", 12),
                Text = "Time since activity: N/A",
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.LightGray,
                TextAlign = ContentAlignment.MiddleCenter
            };
            this.Controls.Add(lblTimeSince);

            // Controls group
            var grpControls = new GroupBox
            {
                Location = new Point(20, 110),
                Size = new Size(550, 100),
                Text = "Controls",
                ForeColor = Color.White
            };

            btnStart = new Button
            {
                Location = new Point(10, 25),
                Size = new Size(100, 30),
                Text = "▶️ Start",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(70, 160, 220),
                ForeColor = Color.White
            };
            btnStart.Click += (s, e) => StartMonitoring();
            grpControls.Controls.Add(btnStart);

            btnStop = new Button
            {
                Location = new Point(120, 25),
                Size = new Size(100, 30),
                Text = "⏹ Stop",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(220, 50, 50),
                ForeColor = Color.White,
                Enabled = false
            };
            btnStop.Click += (s, e) => StopMonitoring();
            grpControls.Controls.Add(btnStop);

            chkEnabled = new CheckBox
            {
                Location = new Point(230, 25),
                Size = new Size(100, 30),
                Text = "Enabled",
                Checked = true,
                ForeColor = Color.White
            };
            chkEnabled.CheckedChanged += (s, e) =>
            {
                if (_monitor != null)
                {
                    _monitor.IsEnabled = chkEnabled.Checked;
                    LogEvent($"Monitoring {(chkEnabled.Checked ? "enabled" : "disabled")}");
                }
            };
            grpControls.Controls.Add(chkEnabled);

            chkTrackMouseMove = new CheckBox
            {
                Location = new Point(340, 25),
                Size = new Size(180, 30),
                Text = "Track Mouse Move",
                Checked = true,
                ForeColor = Color.White
            };
            chkTrackMouseMove.CheckedChanged += (s, e) =>
            {
                if (_monitor != null)
                {
                    _monitor.TrackMouseMovement = chkTrackMouseMove.Checked;
                    LogEvent($"Mouse movement tracking {(chkTrackMouseMove.Checked ? "enabled" : "disabled")}");
                }
            };
            grpControls.Controls.Add(chkTrackMouseMove);

            var lblThreshold = new Label
            {
                Location = new Point(10, 65),
                Size = new Size(150, 20),
                Text = "Idle Threshold (seconds):",
                ForeColor = Color.White
            };
            grpControls.Controls.Add(lblThreshold);

            numIdleThreshold = new NumericUpDown
            {
                Location = new Point(170, 63),
                Size = new Size(80, 25),
                Minimum = 5,
                Maximum = 300,
                Value = 30
            };
            numIdleThreshold.ValueChanged += (s, e) =>
            {
                if (_monitor != null)
                {
                    _monitor.IdleThresholdSeconds = (int)numIdleThreshold.Value;
                    LogEvent($"Idle threshold set to {numIdleThreshold.Value}s");
                }
            };
            grpControls.Controls.Add(numIdleThreshold);

            this.Controls.Add(grpControls);

            // Event log
            lblActivityLog = new Label
            {
                Location = new Point(20, 220),
                Size = new Size(550, 20),
                Text = "Event Log",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            this.Controls.Add(lblActivityLog);

            txtLog = new TextBox
            {
                Location = new Point(20, 245),
                Size = new Size(550, 200),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.LightGray,
                Font = new Font("Consolas", 9)
            };
            this.Controls.Add(txtLog);

            // Update timer
            _updateTimer = new System.Windows.Forms.Timer();
            _updateTimer.Interval = 500;
            _updateTimer.Tick += UpdateDisplay;

            // Dark theme
            this.BackColor = Color.FromArgb(45, 45, 45);
        }

        private void StartMonitoring()
        {
            try
            {
                _monitor?.Start();
                _monitor!.IsEnabled = chkEnabled.Checked;
                _monitor.IdleThresholdSeconds = (int)numIdleThreshold.Value;
                _monitor.TrackMouseMovement = chkTrackMouseMove.Checked;

                btnStart.Enabled = false;
                btnStop.Enabled = true;
                _updateTimer?.Start();

                LogEvent("✅ Monitoring started");
                LogEvent($"Settings: Idle={numIdleThreshold.Value}s, MouseMove={chkTrackMouseMove.Checked}, Enabled={chkEnabled.Checked}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to start monitoring:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                LogEvent($"❌ Failed to start: {ex.Message}");
            }
        }

        private void StopMonitoring()
        {
            _monitor?.Stop();
            _updateTimer?.Stop();
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            LogEvent("⏹ Monitoring stopped");
        }

        private void OnActivityDetected(object? sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnActivityDetected(sender, e)));
                return;
            }

            LogEvent("🟢 Activity detected (transition from idle)");
        }

        private void OnIdleDetected(object? sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnIdleDetected(sender, e)));
                return;
            }

            LogEvent("🔴 User became idle");
        }

        private void UpdateDisplay(object? sender, EventArgs e)
        {
            if (_monitor == null) return;

            // Update status
            lblStatus.Text = _monitor.GetActivityStatusString();

            // Update time since
            var timeSince = _monitor.TimeSinceLastActivity();
            lblTimeSince.Text = $"Time since activity: {timeSince.TotalSeconds:F1}s";

            // Check for idle transition
            _monitor.CheckForIdleTransition();

            // Color code status
            if (timeSince.TotalSeconds < 5)
            {
                lblStatus.BackColor = Color.FromArgb(60, 180, 75); // Green
            }
            else if (timeSince.TotalSeconds < _monitor.IdleThresholdSeconds)
            {
                lblStatus.BackColor = Color.FromArgb(255, 195, 0); // Yellow
            }
            else
            {
                lblStatus.BackColor = Color.FromArgb(220, 50, 50); // Red
            }
        }

        private void LogEvent(string message)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.BeginInvoke(new Action(() => LogEvent(message)));
                return;
            }

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            txtLog.AppendText($"[{timestamp}] {message}\r\n");
            txtLog.ScrollToCaret();
        }

        /// <summary>
        /// Show the test form as a modal dialog.
        /// </summary>
        public static void ShowTest()
        {
            using (var form = new ActivityMonitorTestForm())
            {
                form.ShowDialog();
            }
        }
    }
}
