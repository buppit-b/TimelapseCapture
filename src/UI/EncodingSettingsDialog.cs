using System;
using System.Drawing;
using System.Windows.Forms;

namespace TimelapseCapture
{
    /// <summary>
    /// Modal dialog for editing the encoding settings (frame rate, x264 preset, CRF quality).
    /// Opened from Settings > Encoding Settings. The caller reads FrameRate/PresetIndex/Crf on
    /// DialogResult.OK and writes them back into MainForm's encoding controls, so the encode
    /// path (GetEncodingPreset / BuildFfmpegArguments) needs no changes.
    /// </summary>
    public class EncodingSettingsDialog : Form
    {
        private ComboBox cmbFrameRate = null!;
        private NumericUpDown numCustomFrameRate = null!;
        private ComboBox cmbPreset = null!;
        private NumericUpDown numCrf = null!;
        private Label lblCrfHint = null!;

        /// <summary>Selected frame rate in fps.</summary>
        public int FrameRate { get; private set; }
        /// <summary>x264 preset index: 0=ultrafast, 1=fast, 2=medium, 3=slow.</summary>
        public int PresetIndex { get; private set; }
        /// <summary>CRF quality (0 best / 51 worst).</summary>
        public int Crf { get; private set; }

        private static readonly Color Bg = Color.FromArgb(20, 20, 20);
        private static readonly Color Fg = Color.LightGray;
        private static readonly Color FieldBg = Color.FromArgb(45, 45, 45);

        public EncodingSettingsDialog(int frameRate, int presetIndex, int crf)
        {
            FrameRate = frameRate;
            PresetIndex = Math.Clamp(presetIndex, 0, 3);
            Crf = Math.Clamp(crf, 0, 51);

            Text = "Encoding Settings";
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(380, 260);

            int x = 20, y = 20;

            Controls.Add(MakeLabel("Frame rate (output video FPS):", x, y));
            y += 24;
            cmbFrameRate = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Popup,
                BackColor = FieldBg,
                ForeColor = Fg,
                Location = new Point(x, y),
                Size = new Size(220, 23),
            };
            cmbFrameRate.Items.AddRange(new object[]
            {
                "24 fps (Film)", "25 fps (PAL)", "30 fps (NTSC)", "60 fps (Smooth)", "Custom..."
            });
            cmbFrameRate.SelectedIndexChanged += (s, e) =>
            {
                numCustomFrameRate.Visible = cmbFrameRate.SelectedIndex == 4;
            };
            Controls.Add(cmbFrameRate);

            numCustomFrameRate = new NumericUpDown
            {
                BackColor = FieldBg,
                ForeColor = Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(x + 230, y),
                Size = new Size(90, 23),
                Minimum = 1,
                Maximum = 120,
                Value = Math.Clamp(frameRate, 1, 120),
                Visible = false,
            };
            Controls.Add(numCustomFrameRate);

            // Pre-select the frame rate.
            int idx = frameRate switch { 24 => 0, 25 => 1, 30 => 2, 60 => 3, _ => 4 };
            cmbFrameRate.SelectedIndex = idx;

            y += 42;
            Controls.Add(MakeLabel("Encoding speed/quality preset:", x, y));
            y += 24;
            cmbPreset = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Popup,
                BackColor = FieldBg,
                ForeColor = Fg,
                Location = new Point(x, y),
                Size = new Size(220, 23),
            };
            cmbPreset.Items.AddRange(new object[]
            {
                "Ultrafast (largest file, fastest)", "Fast", "Medium (balanced)", "Slow (smallest file)"
            });
            cmbPreset.SelectedIndex = PresetIndex;
            Controls.Add(cmbPreset);

            y += 42;
            Controls.Add(MakeLabel("Quality (CRF, 0 = best / 51 = worst):", x, y));
            y += 24;
            numCrf = new NumericUpDown
            {
                BackColor = FieldBg,
                ForeColor = Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(x, y),
                Size = new Size(90, 23),
                Minimum = 0,
                Maximum = 51,
                Value = Crf,
            };
            numCrf.ValueChanged += (s, e) => UpdateCrfHint();
            Controls.Add(numCrf);

            lblCrfHint = new Label
            {
                AutoSize = false,
                ForeColor = Color.FromArgb(150, 150, 150),
                Location = new Point(x + 100, y + 2),
                Size = new Size(220, 20),
            };
            Controls.Add(lblCrfHint);
            UpdateCrfHint();

            var btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Location = new Point(ClientSize.Width - 200, ClientSize.Height - 40),
                Size = new Size(85, 28),
            };
            btnOk.Click += (s, e) =>
            {
                PresetIndex = cmbPreset.SelectedIndex < 0 ? 2 : cmbPreset.SelectedIndex;
                Crf = (int)numCrf.Value;
                FrameRate = cmbFrameRate.SelectedIndex switch
                {
                    0 => 24, 1 => 25, 2 => 30, 3 => 60,
                    _ => (int)numCustomFrameRate.Value,
                };
            };
            Controls.Add(btnOk);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Fg,
                Location = new Point(ClientSize.Width - 105, ClientSize.Height - 40),
                Size = new Size(85, 28),
            };
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private void UpdateCrfHint()
        {
            int v = (int)numCrf.Value;
            string q = v <= 18 ? "visually lossless" : v <= 23 ? "high quality" : v <= 28 ? "good" : "smaller / lower";
            lblCrfHint.Text = $"({q})";
        }

        private static Label MakeLabel(string text, int x, int y) => new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Fg,
            Location = new Point(x, y),
        };
    }
}
