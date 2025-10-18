using System;
using System.Drawing;
using System.Windows.Forms;

namespace TimelapseCapture
{
    public class SessionNameDialog : Form
    {
        private TextBox txtName;
        private Button btnOK;
        private Button btnCancel;
        private Label lblPrompt;
        private Label lblHint;

        public string SessionName => txtName.Text.Trim();

        public SessionNameDialog(string suggestedName = "")
        {
            Text = "New Capture Session";
            Size = new Size(450, 180);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(20, 20, 20);
            ForeColor = Color.FromArgb(200, 200, 200);

            lblPrompt = new Label
            {
                Text = "Enter a name for this capture session:",
                Location = new Point(20, 20),
                AutoSize = true,
                ForeColor = Color.FromArgb(200, 200, 200)
            };

            txtName = new TextBox
            {
                Location = new Point(20, 50),
                Size = new Size(400, 23),
                Text = string.IsNullOrEmpty(suggestedName)
                    ? $"Session_{DateTime.Now:yyyyMMdd_HHmm}"
                    : suggestedName,
                Font = new Font("Segoe UI", 10F),
                BackColor = SystemColors.InactiveCaptionText,
                ForeColor = SystemColors.ScrollBar
            };

            lblHint = new Label
            {
                Text = "Examples: Painting Process, Tutorial Part 3, Sunset Timelapse",
                Location = new Point(20, 78),
                Size = new Size(400, 15),
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8F)
            };

            btnOK = new Button
            {
                Text = "Create Session",
                DialogResult = DialogResult.OK,
                Location = new Point(240, 105),
                Size = new Size(100, 32),
                BackColor = Color.FromArgb(0, 122, 204),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(350, 105),
                Size = new Size(70, 32),
                FlatStyle = FlatStyle.Flat
            };

            Controls.AddRange(new Control[] { lblPrompt, txtName, lblHint, btnOK, btnCancel });
            AcceptButton = btnOK;
            CancelButton = btnCancel;

            // Validate on OK
            btnOK.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtName.Text))
                {
                    MessageBox.Show(
                        "Please enter a session name.",
                        "Name Required",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                }
            };

            // Select all text for easy typing
            txtName.SelectAll();
            txtName.Focus();
        }
    }
}   