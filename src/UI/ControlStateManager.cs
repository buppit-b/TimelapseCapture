using System;
using System.Drawing;
using System.Windows.Forms;

namespace TimelapseCapture
{
    /// <summary>
    /// Manages the enabled/disabled state of controls based on application setup status.
    /// Provides visual guidance to users about what they need to configure next.
    /// </summary>
    public static class ControlStateManager
    {
        // Color scheme for different states
        private static readonly Color EnabledColor = Color.FromArgb(33, 33, 33);
        private static readonly Color DisabledColor = Color.FromArgb(120, 120, 120);
        private static readonly Color HighlightColor = Color.FromArgb(0, 122, 204);

        /// <summary>
        /// Update all control states based on current application status.
        /// This is the main entry point that should be called whenever state changes.
        /// </summary>
        public static void UpdateAllControlStates(
            MainForm form,
            bool hasOutputFolder,
            bool hasFfmpeg,
            bool hasSession,
            bool hasRegion,
            bool isCapturing,
            bool isEncoding,
            bool hasFrames)
        {
            // Step 1: Output Folder (always available)
            SetControlState(form.btnChooseFolder, true, "Choose where to save your timelapse sessions");
            
            // Step 2: FFmpeg (always available, but prioritized if missing)
            SetControlState(form.btnDownloadFfmpeg, !hasFfmpeg, 
                hasFfmpeg ? "FFmpeg is already configured" : "Download FFmpeg to enable video encoding");
            SetControlState(form.btnBrowseFfmpeg, true, "Browse for an existing FFmpeg installation");
            
            // Step 3: Session (requires output folder)
            bool canCreateSession = hasOutputFolder && !isCapturing && !isEncoding;
            SetControlState(form.btnNewSession, canCreateSession,
                !hasOutputFolder ? "❗ Choose an output folder first" :
                isCapturing ? "Stop capturing before creating a new session" :
                isEncoding ? "Wait for encoding to finish" :
                "Create a new timelapse session");
            
            SetControlState(form.btnLoadSession, canCreateSession,
                !hasOutputFolder ? "❗ Choose an output folder first" :
                isCapturing ? "Stop capturing before loading a session" :
                isEncoding ? "Wait for encoding to finish" :
                "Load an existing timelapse session");
            
            SetControlState(form.txtSessionName, hasSession && !isCapturing && !isEncoding,
                !hasSession ? "❗ Create or load a session first" :
                isCapturing ? "Stop capturing to rename session" :
                isEncoding ? "Wait for encoding to finish" :
                "Rename this session");
            
            // Step 4: Region Selection (requires session)
            bool canSelectRegion = hasSession && !isCapturing && !isEncoding;
            SetControlState(form.btnSelectRegion, canSelectRegion,
                !hasOutputFolder ? "❗ Choose an output folder first" :
                !hasSession ? "❗ Create or load a session first" :
                isCapturing ? "Stop capturing before changing region" :
                isEncoding ? "Wait for encoding to finish" :
                "Select a specific screen region to capture");
            
            SetControlState(form.btnFullScreen, canSelectRegion,
                !hasOutputFolder ? "❗ Choose an output folder first" :
                !hasSession ? "❗ Create or load a session first" :
                isCapturing ? "Stop capturing before changing region" :
                isEncoding ? "Wait for encoding to finish" :
                "Capture the entire primary screen");
            
            SetControlState(form.btnShowRegion, hasRegion && !isCapturing,
                !hasRegion ? "No region selected yet" :
                isCapturing ? "Cannot show overlay while capturing" :
                "Show/hide the capture region overlay");
            
            // Step 5: Capture Controls (requires session and region)
            bool canStartCapture = hasSession && hasRegion && !isCapturing && !isEncoding;
            bool canStopCapture = isCapturing;
            
            SetControlState(form.btnStart, canStartCapture,
                !hasOutputFolder ? "❗ Choose an output folder first" :
                !hasSession ? "❗ Create or load a session first" :
                !hasRegion ? "❗ Select a capture region first" :
                isEncoding ? "Wait for encoding to finish" :
                isCapturing ? "Already capturing" :
                "✅ Start capturing frames");
            
            SetControlState(form.btnStop, canStopCapture,
                !isCapturing ? "No capture in progress" : "Stop capturing frames");
            
            // Capture Settings (can be configured anytime, but warn if capturing)
            bool canEditCaptureSettings = !isEncoding;
            string captureSettingsTooltip = isCapturing ? 
                "⚠️ Changes will apply to new frames" : 
                isEncoding ? "Wait for encoding to finish" :
                "Configure capture settings";
            
            SetControlState(form.numInterval, canEditCaptureSettings, captureSettingsTooltip);
            SetControlState(form.cmbFormat, canEditCaptureSettings, captureSettingsTooltip);
            SetControlState(form.cmbAspectRatio, canEditCaptureSettings && !isCapturing, 
                isCapturing ? "Cannot change aspect ratio while capturing" : captureSettingsTooltip);
            SetControlState(form.numQuality, canEditCaptureSettings, captureSettingsTooltip);
            SetControlState(form.trkQuality, canEditCaptureSettings, captureSettingsTooltip);
            
            // Step 6: Encoding (requires FFmpeg and frames)
            bool canEncode = hasFfmpeg && hasFrames && !isCapturing && !isEncoding;
            SetControlState(form.btnEncode, canEncode,
                !hasFfmpeg ? "❗ Download FFmpeg first" :
                !hasFrames ? "No frames captured yet - start capturing first" :
                isCapturing ? "Stop capturing before encoding" :
                isEncoding ? "Encoding in progress..." :
                "✅ Encode captured frames into a video");
            
            bool canConfigureEncoding = hasFfmpeg && !isEncoding;
            string encodingSettingsTooltip = !hasFfmpeg ?
                "❗ Download FFmpeg first" :
                isEncoding ? "Cannot change settings during encoding" :
                "Configure video encoding settings";
            
            SetControlState(form.cmbFrameRate, canConfigureEncoding, encodingSettingsTooltip);
            SetControlState(form.numCustomFrameRate, canConfigureEncoding, encodingSettingsTooltip);
            SetControlState(form.cmbEncodingPreset, canConfigureEncoding, encodingSettingsTooltip);
            SetControlState(form.cmbVideoCodec, canConfigureEncoding, encodingSettingsTooltip);
            SetControlState(form.numCrf, canConfigureEncoding, encodingSettingsTooltip);
            SetControlState(form.numDesiredSec, canConfigureEncoding, encodingSettingsTooltip);
            
            // File Operations (always available if session exists)
            SetControlState(form.btnOpenFolder, hasSession && hasOutputFolder,
                !hasSession ? "No active session" : "Open the session folder in File Explorer");
            
            // Highlight the next required action
            HighlightNextAction(form, hasOutputFolder, hasFfmpeg, hasSession, hasRegion, hasFrames, isCapturing);
        }

        /// <summary>
        /// Highlight the next action the user should take.
        /// </summary>
        private static void HighlightNextAction(
            MainForm form, 
            bool hasOutputFolder, 
            bool hasFfmpeg, 
            bool hasSession, 
            bool hasRegion,
            bool hasFrames,
            bool isCapturing)
        {
            // Reset all highlights first
            ResetButtonHighlight(form.btnChooseFolder);
            ResetButtonHighlight(form.btnDownloadFfmpeg);
            ResetButtonHighlight(form.btnNewSession);
            ResetButtonHighlight(form.btnLoadSession);
            ResetButtonHighlight(form.btnSelectRegion);
            ResetButtonHighlight(form.btnFullScreen);
            ResetButtonHighlight(form.btnStart);
            ResetButtonHighlight(form.btnEncode);

            // Highlight the next critical step
            if (!hasOutputFolder)
            {
                HighlightButton(form.btnChooseFolder);
            }
            else if (!hasSession)
            {
                HighlightButton(form.btnNewSession);
                HighlightButton(form.btnLoadSession);
            }
            else if (!hasRegion && !isCapturing)
            {
                HighlightButton(form.btnSelectRegion);
                HighlightButton(form.btnFullScreen);
            }
            else if (!isCapturing && hasRegion)
            {
                HighlightButton(form.btnStart);
            }
            else if (hasFrames && !isCapturing && hasFfmpeg)
            {
                HighlightButton(form.btnEncode);
            }
        }

        /// <summary>
        /// Set the state of a control with optional tooltip.
        /// </summary>
        private static void SetControlState(Control? control, bool enabled, string tooltip = "")
        {
            if (control == null) return;

            control.Enabled = enabled;
            control.ForeColor = enabled ? EnabledColor : DisabledColor;
            
            // Set tooltip if we have a ToolTip component
            if (!string.IsNullOrEmpty(tooltip) && control.FindForm() is MainForm form)
            {
                // Store tooltip in Tag for now - MainForm should have a ToolTip component
                control.Tag = tooltip;
            }
        }

        /// <summary>
        /// Highlight a button to draw attention to it.
        /// </summary>
        private static void HighlightButton(Button? button)
        {
            if (button == null || !button.Enabled) return;
            
            button.FlatAppearance.BorderSize = 2;
            button.FlatAppearance.BorderColor = HighlightColor;
        }

        /// <summary>
        /// Reset button highlight to normal state.
        /// </summary>
        private static void ResetButtonHighlight(Button? button)
        {
            if (button == null) return;
            
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
        }

        /// <summary>
        /// Setup tooltips for all controls on the form.
        /// Call this once during form initialization.
        /// </summary>
        public static void SetupTooltips(MainForm form, ToolTip toolTip)
        {
            // This will be called to sync all tooltips from control.Tag values
            foreach (Control control in GetAllControls(form))
            {
                if (control.Tag is string tooltipText && !string.IsNullOrEmpty(tooltipText))
                {
                    toolTip.SetToolTip(control, tooltipText);
                }
            }
        }

        /// <summary>
        /// Recursively get all controls in a form.
        /// </summary>
        private static System.Collections.Generic.IEnumerable<Control> GetAllControls(Control container)
        {
            foreach (Control control in container.Controls)
            {
                yield return control;
                foreach (Control child in GetAllControls(control))
                {
                    yield return child;
                }
            }
        }
    }
}
