using System;
using System.Drawing;
using System.Windows.Forms;

namespace TimelapseCapture
{
    /// <summary>
    /// Helper class for common UI operations and updates.
    /// </summary>
    public static class UIHelper
    {
        /// <summary>
        /// Safely updates a label's text, checking for null and disposed state.
        /// </summary>
        public static void SafeUpdateLabel(Label? label, string text)
        {
            if (label != null && !label.IsDisposed)
            {
                label.Text = text;
            }
        }

        /// <summary>
        /// Safely enables or disables a control, checking for null and disposed state.
        /// </summary>
        public static void SafeSetEnabled(Control? control, bool enabled)
        {
            if (control != null && !control.IsDisposed)
            {
                control.Enabled = enabled;
            }
        }

        /// <summary>
        /// Safely sets a control's text, checking for null and disposed state.
        /// </summary>
        public static void SafeSetText(Control? control, string text)
        {
            if (control != null && !control.IsDisposed)
            {
                control.Text = text;
            }
        }

        /// <summary>
        /// Safely sets a control's value, checking for null and disposed state.
        /// </summary>
        public static void SafeSetValue(NumericUpDown? control, decimal value)
        {
            if (control != null && !control.IsDisposed)
            {
                control.Value = Math.Max(control.Minimum, Math.Min(control.Maximum, value));
            }
        }

        /// <summary>
        /// Safely sets a control's value, checking for null and disposed state.
        /// </summary>
        public static void SafeSetValue(TrackBar? control, int value)
        {
            if (control != null && !control.IsDisposed)
            {
                control.Value = Math.Max(control.Minimum, Math.Min(control.Maximum, value));
            }
        }

        /// <summary>
        /// Safely sets a combo box selection, checking for null and disposed state.
        /// </summary>
        public static void SafeSetSelectedItem(ComboBox? control, object? item)
        {
            if (control != null && !control.IsDisposed)
            {
                control.SelectedItem = item;
            }
        }

        /// <summary>
        /// Safely sets a combo box selection by index, checking for null and disposed state.
        /// </summary>
        public static void SafeSetSelectedIndex(ComboBox? control, int index)
        {
            if (control != null && !control.IsDisposed && index >= 0 && index < control.Items.Count)
            {
                control.SelectedIndex = index;
            }
        }

        /// <summary>
        /// Safely sets a control's visibility, checking for null and disposed state.
        /// </summary>
        public static void SafeSetVisible(Control? control, bool visible)
        {
            if (control != null && !control.IsDisposed)
            {
                control.Visible = visible;
            }
        }

        /// <summary>
        /// Safely sets a control's color, checking for null and disposed state.
        /// </summary>
        public static void SafeSetColor(Control? control, Color color)
        {
            if (control != null && !control.IsDisposed)
            {
                control.ForeColor = color;
            }
        }

        /// <summary>
        /// Safely sets a control's back color, checking for null and disposed state.
        /// </summary>
        public static void SafeSetBackColor(Control? control, Color color)
        {
            if (control != null && !control.IsDisposed)
            {
                control.BackColor = color;
            }
        }

        /// <summary>
        /// Safely sets a control's border size, checking for null and disposed state.
        /// </summary>
        public static void SafeSetBorderSize(Button? button, int borderSize)
        {
            if (button != null && !button.IsDisposed)
            {
                button.FlatAppearance.BorderSize = borderSize;
            }
        }

        /// <summary>
        /// Safely invokes an action on the UI thread, checking for disposed state.
        /// </summary>
        public static void SafeInvoke(Control control, Action action)
        {
            if (control != null && !control.IsDisposed)
            {
                if (control.InvokeRequired)
                {
                    control.Invoke(action);
                }
                else
                {
                    action();
                }
            }
        }

        /// <summary>
        /// Safely begins an invoke on the UI thread, checking for disposed state.
        /// </summary>
        public static void SafeBeginInvoke(Control control, Action action)
        {
            if (control != null && !control.IsDisposed)
            {
                if (control.InvokeRequired)
                {
                    control.BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
        }

        /// <summary>
        /// Shows a message box with consistent styling.
        /// </summary>
        public static DialogResult ShowMessage(string message, string title, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.Information)
        {
            return MessageBox.Show(message, title, buttons, icon);
        }

        /// <summary>
        /// Shows a warning message box.
        /// </summary>
        public static DialogResult ShowWarning(string message, string title)
        {
            return ShowMessage(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// Shows an error message box.
        /// </summary>
        public static DialogResult ShowError(string message, string title)
        {
            return ShowMessage(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>
        /// Shows a question message box.
        /// </summary>
        public static DialogResult ShowQuestion(string message, string title)
        {
            return ShowMessage(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        }

        /// <summary>
        /// Shows an information message box.
        /// </summary>
        public static DialogResult ShowInfo(string message, string title)
        {
            return ShowMessage(message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
