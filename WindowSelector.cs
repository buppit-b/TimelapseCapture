using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TimelapseCapture
{
    /// <summary>
    /// Dialog for selecting a window to capture.
    /// Lists all visible windows with titles and allows user to select one.
    /// </summary>
    public class WindowSelector : Form
    {
        private ListView lstWindows;
        private Button btnSelect;
        private Button btnCancel;
        private Button btnRefresh;
        private Label lblInstructions;
        
        /// <summary>
        /// The selected window's bounds in screen coordinates.
        /// </summary>
        public Rectangle SelectedRegion { get; private set; }
        
        /// <summary>
        /// The selected window's title.
        /// </summary>
        public string SelectedWindowTitle { get; private set; } = "";

        #region Win32 API

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        private const uint GW_OWNER = 4;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        #endregion

        private class WindowInfo
        {
            public IntPtr Handle { get; set; }
            public string Title { get; set; } = "";
            public Rectangle Bounds { get; set; }
            public bool IsMinimized { get; set; }
        }

        public WindowSelector()
        {
            InitializeComponent();
            PopulateWindowList();
        }

        private void InitializeComponent()
        {
            this.Text = "Select Window to Capture";
            this.Size = new Size(700, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(45, 45, 45);

            // Instructions label
            lblInstructions = new Label
            {
                Text = "Select a window to capture. The entire window area will be recorded.",
                Location = new Point(20, 20),
                Size = new Size(640, 40),
                ForeColor = Color.LightGray,
                Font = new Font("Segoe UI", 9f)
            };
            this.Controls.Add(lblInstructions);

            // Window list
            lstWindows = new ListView
            {
                Location = new Point(20, 70),
                Size = new Size(640, 320),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.LightGray,
                Font = new Font("Segoe UI", 9f)
            };
            
            lstWindows.Columns.Add("Window Title", 400);
            lstWindows.Columns.Add("Size", 120);
            lstWindows.Columns.Add("Position", 100);
            
            lstWindows.DoubleClick += (s, e) => SelectWindow();
            
            this.Controls.Add(lstWindows);

            // Refresh button
            btnRefresh = new Button
            {
                Text = "🔄 Refresh",
                Location = new Point(20, 410),
                Size = new Size(120, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(70, 130, 180),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnRefresh.FlatAppearance.BorderSize = 0;
            btnRefresh.Click += (s, e) => PopulateWindowList();
            this.Controls.Add(btnRefresh);

            // Select button
            btnSelect = new Button
            {
                Text = "✓ Select Window",
                Location = new Point(430, 410),
                Size = new Size(120, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 180, 75),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSelect.FlatAppearance.BorderSize = 0;
            btnSelect.Click += (s, e) => SelectWindow();
            this.Controls.Add(btnSelect);

            // Cancel button
            btnCancel = new Button
            {
                Text = "✕ Cancel",
                Location = new Point(560, 410),
                Size = new Size(100, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(220, 50, 50),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            this.Controls.Add(btnCancel);
        }

        /// <summary>
        /// Populate the window list with all visible windows.
        /// </summary>
        private void PopulateWindowList()
        {
            lstWindows.Items.Clear();
            var windows = GetAllWindows();

            foreach (var window in windows)
            {
                var item = new ListViewItem(window.Title);
                item.SubItems.Add($"{window.Bounds.Width}×{window.Bounds.Height}");
                item.SubItems.Add($"({window.Bounds.X}, {window.Bounds.Y})");
                item.Tag = window;
                
                if (window.IsMinimized)
                {
                    item.ForeColor = Color.Gray;
                    item.Text += " (minimized)";
                }
                
                lstWindows.Items.Add(item);
            }

            if (lstWindows.Items.Count == 0)
            {
                var item = new ListViewItem("No windows found");
                item.ForeColor = Color.Gray;
                lstWindows.Items.Add(item);
            }
        }

        /// <summary>
        /// Get all visible windows with titles.
        /// </summary>
        private List<WindowInfo> GetAllWindows()
        {
            var windows = new List<WindowInfo>();
            IntPtr shellWindow = GetShellWindow();

            EnumWindows((hWnd, lParam) =>
            {
                // Skip invisible windows
                if (!IsWindowVisible(hWnd))
                    return true;

                // Skip windows without titles
                int length = GetWindowTextLength(hWnd);
                if (length == 0)
                    return true;

                // Get window title
                var builder = new StringBuilder(length + 1);
                GetWindowText(hWnd, builder, builder.Capacity);
                string title = builder.ToString();

                if (string.IsNullOrWhiteSpace(title))
                    return true;

                // Skip the shell window (desktop)
                if (hWnd == shellWindow)
                    return true;

                // Skip windows with no owner (usually system windows)
                IntPtr owner = GetWindow(hWnd, GW_OWNER);
                if (owner != IntPtr.Zero)
                    return true;

                // Get window bounds
                if (GetWindowRect(hWnd, out RECT rect))
                {
                    var bounds = new Rectangle(
                        rect.Left,
                        rect.Top,
                        rect.Right - rect.Left,
                        rect.Bottom - rect.Top
                    );

                    // Skip windows with invalid dimensions
                    if (bounds.Width <= 0 || bounds.Height <= 0)
                        return true;

                    // Skip windows that are too small (likely system tray icons)
                    if (bounds.Width < 50 || bounds.Height < 50)
                        return true;

                    windows.Add(new WindowInfo
                    {
                        Handle = hWnd,
                        Title = title,
                        Bounds = bounds,
                        IsMinimized = IsIconic(hWnd)
                    });
                }

                return true;
            }, IntPtr.Zero);

            // Sort by title
            windows.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));

            return windows;
        }

        /// <summary>
        /// Select the currently highlighted window.
        /// </summary>
        private void SelectWindow()
        {
            if (lstWindows.SelectedItems.Count == 0)
            {
                MessageBox.Show(
                    "Please select a window from the list.",
                    "No Window Selected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var selectedItem = lstWindows.SelectedItems[0];
            if (selectedItem.Tag is WindowInfo windowInfo)
            {
                // Check if window is minimized
                if (windowInfo.IsMinimized)
                {
                    var result = MessageBox.Show(
                        $"The selected window is minimized:\n\n{windowInfo.Title}\n\n" +
                        "Minimized windows cannot be captured properly.\n\n" +
                        "Would you like to select it anyway?\n" +
                        "(You'll need to restore the window before capturing)",
                        "Window Minimized",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (result == DialogResult.No)
                        return;
                }

                // Get fresh bounds in case window moved
                if (GetWindowRect(windowInfo.Handle, out RECT rect))
                {
                    var bounds = new Rectangle(
                        rect.Left,
                        rect.Top,
                        rect.Right - rect.Left,
                        rect.Bottom - rect.Top
                    );

                    // Ensure even dimensions for video encoding
                    if ((bounds.Width & 1) == 1) bounds.Width = Math.Max(2, bounds.Width - 1);
                    if ((bounds.Height & 1) == 1) bounds.Height = Math.Max(2, bounds.Height - 1);

                    SelectedRegion = bounds;
                    SelectedWindowTitle = windowInfo.Title;

                    Logger.Log("WindowSelector", $"Selected window: '{SelectedWindowTitle}' " +
                        $"Bounds: {bounds.X}, {bounds.Y}, {bounds.Width}×{bounds.Height}");

                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    MessageBox.Show(
                        "Could not get window bounds.\n\nThe window may have been closed.",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }
    }
}
