using System;
using System.Windows.Forms;

namespace TimelapseCapture
{
    public partial class MainForm
    {
        /// <summary>
        /// Handle guided mode checkbox change.
        /// </summary>
        private void chkGuidedMode_CheckedChanged(object? sender, EventArgs e)
        {
            _guidedModeEnabled = chkGuidedMode?.Checked ?? true;
            UpdateGuidedModeUI();
            SaveSettings();
        }
        
        /// <summary>
        /// Update the UI based on guided mode and current prerequisites.
        /// This provides progressive disclosure to guide users through setup.
        /// </summary>
        private void UpdateGuidedModeUI()
        {
            if (!_guidedModeEnabled)
            {
                // Guided mode off - enable everything
                EnableAllControls();
                return;
            }

            // Guided mode enabled - implement progressive disclosure
            UpdateControlStates();
        }
        
        /// <summary>
        /// Initialize the tooltip system for guided setup.
        /// </summary>
        private void InitializeTooltips()
        {
            // Tooltips are created on-demand in SetControlTooltip
        }
        
        /// <summary>
        /// Update control enabled/disabled states based on prerequisites.
        /// </summary>
        private void UpdateControlStates()
        {
            bool hasOutputFolder = !string.IsNullOrEmpty(settings.SaveFolder);
            bool hasFfmpeg = !string.IsNullOrEmpty(_ffmpegPath) && System.IO.File.Exists(_ffmpegPath);
            bool hasSession = _activeSession != null;
            bool hasRegion = captureRegion.HasValue;
            bool hasFrames = _activeSession?.FramesCaptured > 0;
            
            // Output folder (always available)
            UIHelper.SafeSetEnabled(btnChooseFolder, true);
            UIHelper.SafeSetEnabled(btnOpenFolder, hasOutputFolder);
            
            // FFmpeg (always available)
            UIHelper.SafeSetEnabled(btnDownloadFfmpeg, !_isDownloadingFfmpeg);
            UIHelper.SafeSetEnabled(btnBrowseFfmpeg, !_isDownloadingFfmpeg);
            
            // Session management (requires output folder)
            UIHelper.SafeSetEnabled(btnNewSession, hasOutputFolder && !IsCapturing);
            SetControlTooltip(btnNewSession, !hasOutputFolder ? "Choose output folder first" : null);
            
            UIHelper.SafeSetEnabled(btnLoadSession, hasOutputFolder && !IsCapturing);
            SetControlTooltip(btnLoadSession, !hasOutputFolder ? "Choose output folder first" : null);
            
            // Region selection (requires session)
            UIHelper.SafeSetEnabled(btnSelectRegion, hasSession && !IsCapturing);
            SetControlTooltip(btnSelectRegion, !hasSession ? "Create session first" : null);
            
            UIHelper.SafeSetEnabled(btnFullScreen, hasSession && !IsCapturing);
            SetControlTooltip(btnFullScreen, !hasSession ? "Create session first" : null);
            
            UIHelper.SafeSetEnabled(btnShowRegion, hasRegion);
            SetControlTooltip(btnShowRegion, !hasRegion ? "Select region first" : null);
            
            // Aspect ratio (enabled during region selection)
            UIHelper.SafeSetEnabled(cmbAspectRatio, hasSession && !IsCapturing);
            
            // Capture settings (requires session)
            UIHelper.SafeSetEnabled(numInterval, hasSession && !IsCapturing);
            SetControlTooltip(numInterval, !hasSession ? "Create session first" : null);
            
            UIHelper.SafeSetEnabled(cmbFormat, hasSession && !IsCapturing);
            SetControlTooltip(cmbFormat, !hasSession ? "Create session first" : null);
            
            bool jpegSelected = cmbFormat?.SelectedItem?.ToString() == "JPEG";
            UIHelper.SafeSetEnabled(trkQuality, hasSession && !IsCapturing && jpegSelected);
            UIHelper.SafeSetEnabled(numQuality, hasSession && !IsCapturing && jpegSelected);
            
            // Capture controls
            bool canStart = hasSession && hasRegion && !IsCapturing;
            UIHelper.SafeSetEnabled(btnStart, canStart);
            SetControlTooltip(btnStart,
                !hasSession ? "Create session first" :
                !hasRegion ? "Select region first" :
                null);
            
            UIHelper.SafeSetEnabled(btnStop, IsCapturing);
            
            // Encoding (during an active encode the button stays enabled and acts as Cancel)
            bool canEncode = hasFfmpeg && hasFrames && !_isEncoding;
            UIHelper.SafeSetEnabled(btnEncode, canEncode || _isEncoding);
            SetControlTooltip(btnEncode,
                !hasFfmpeg ? "Download FFmpeg first" :
                !hasFrames ? "Capture some frames first" :
                null);
            
            // Encoding settings (always configurable)
            UIHelper.SafeSetEnabled(cmbFrameRate, true);
            UIHelper.SafeSetEnabled(numCustomFrameRate, cmbFrameRate?.SelectedIndex == 4);
            UIHelper.SafeSetEnabled(cmbEncodingPreset, true);
            UIHelper.SafeSetEnabled(numCrf, true);
            
            // Smart interval (requires session)
            UIHelper.SafeSetEnabled(chkSmartInterval, hasSession);
            bool smartEnabled = (chkSmartInterval?.Checked ?? false) && hasSession;
            UIHelper.SafeSetEnabled(cmbSmartPreset, smartEnabled);
            UIHelper.SafeSetEnabled(numActiveInterval, smartEnabled);
            UIHelper.SafeSetEnabled(numIdleThreshold, smartEnabled);
            UIHelper.SafeSetEnabled(rbSlowIdle, smartEnabled);
            UIHelper.SafeSetEnabled(rbSkipIdle, smartEnabled);
        }
        
        /// <summary>
        /// Enable all controls (guided mode disabled).
        /// </summary>
        private void EnableAllControls()
        {
            UIHelper.SafeSetEnabled(btnChooseFolder, true);
            UIHelper.SafeSetEnabled(btnOpenFolder, true);
            UIHelper.SafeSetEnabled(btnDownloadFfmpeg, true);
            UIHelper.SafeSetEnabled(btnBrowseFfmpeg, true);
            UIHelper.SafeSetEnabled(btnNewSession, true);
            UIHelper.SafeSetEnabled(btnLoadSession, true);
            UIHelper.SafeSetEnabled(btnSelectRegion, true);
            UIHelper.SafeSetEnabled(btnFullScreen, true);
            UIHelper.SafeSetEnabled(btnShowRegion, true);
            UIHelper.SafeSetEnabled(cmbAspectRatio, true);
            UIHelper.SafeSetEnabled(numInterval, true);
            UIHelper.SafeSetEnabled(cmbFormat, true);
            UIHelper.SafeSetEnabled(trkQuality, true);
            UIHelper.SafeSetEnabled(numQuality, true);
            UIHelper.SafeSetEnabled(btnStart, true);
            UIHelper.SafeSetEnabled(btnStop, false); // Stop only enabled when capturing
            UIHelper.SafeSetEnabled(btnEncode, true);
            UIHelper.SafeSetEnabled(cmbFrameRate, true);
            UIHelper.SafeSetEnabled(numCustomFrameRate, cmbFrameRate?.SelectedIndex == 4);
            UIHelper.SafeSetEnabled(cmbEncodingPreset, true);
            UIHelper.SafeSetEnabled(numCrf, true);
            UIHelper.SafeSetEnabled(chkSmartInterval, true);
            
            bool smartEnabled = chkSmartInterval?.Checked ?? false;
            UIHelper.SafeSetEnabled(cmbSmartPreset, smartEnabled);
            UIHelper.SafeSetEnabled(numActiveInterval, smartEnabled);
            UIHelper.SafeSetEnabled(numIdleThreshold, smartEnabled);
            UIHelper.SafeSetEnabled(rbSlowIdle, smartEnabled);
            UIHelper.SafeSetEnabled(rbSkipIdle, smartEnabled);
            
            // Clear all tooltips
            ClearAllTooltips();
        }
        
        /// <summary>
        /// Set a tooltip for a control.
        /// </summary>
        private void SetControlTooltip(Control? control, string? text)
        {
            if (control == null) return;
            
            // Create tooltip if needed
            if (!_tooltips.ContainsKey(control))
            {
                var tooltip = new ToolTip();
                _tooltips[control] = tooltip;
            }
            
            if (string.IsNullOrEmpty(text))
            {
                _tooltips[control].SetToolTip(control, "");
            }
            else
            {
                _tooltips[control].SetToolTip(control, text);
            }
        }
        
        /// <summary>
        /// Clear all tooltips.
        /// </summary>
        private void ClearAllTooltips()
        {
            foreach (var kvp in _tooltips)
            {
                kvp.Value.SetToolTip(kvp.Key, "");
            }
        }
    }
}
