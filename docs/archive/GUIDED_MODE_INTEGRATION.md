# Guided Mode Integration Guide

## Summary
A toggleable "Guided Mode" feature has been added that provides progressive UI disclosure to guide users through the setup process.

## What's Been Added

1. **Checkbox Control** (`chkGuidedMode`)
   - Located at coordinates (490, 683) - bottom right of the form
   - Labeled: "🧭 Guided Mode (Setup Assistance)"
   - Checked by default

2. **Core Logic** in MainForm.cs:
   - `_guidedModeEnabled` field (bool)
   - `_tooltips` dictionary (Control -> ToolTip mapping)
   - `UpdateGuidedModeUI()` - Main method that enables/disables controls
   - `EnableAllControls()` - Re-enables everything when guided mode is off
   - `SetControlTooltip()` - Helper to show "why is this disabled?" tooltips
   - `ClearAllTooltips()` - Cleanup helper

## Where to Add UpdateGuidedModeUI() Calls

The `UpdateGuidedModeUI()` method needs to be called whenever the application state changes that would affect which controls should be enabled. Add it to these methods:

### Already Added:
1. `MainForm()` constructor - ✅ DONE
2. `SetCurrentRegion()` - ✅ DONE

### Still Need to Add:

3. **btnChooseFolder_Click** - After folder selection
4. **btnNewSession_Click** - After session creation
5. **btnLoadSession_Click** - After session loaded
6. **btnStart_Click** - After capture starts
7. **btnStop_Click** - After capture stops
8. **btnDownloadFfmpeg_Click** - After FFmpeg download completes
9. **btnBrowseFfmpeg_Click** - After FFmpeg is selected
10. **ClearCurrentRegion()** - When region is cleared
11. **Any method that modifies _activeSession**

## How Guided Mode Works

When ENABLED (checked):
- Controls are enabled/disabled based on prerequisites
- Tooltips explain why controls are disabled
- Progressive disclosure: Output Folder → Session → Region → Capture → Encode

When DISABLED (unchecked):
- All controls are enabled (except Stop button when not capturing)
- No tooltips
- Full flexibility for advanced users

## Control Dependencies (Guided Mode Logic)

```
Step 1: Output Folder (Always available)
  └─ btnChooseFolder ✓
  └─ btnOpenFolder (requires folder)

Step 2: FFmpeg (Always available - optional)
  └─ btnDownloadFfmpeg ✓
  └─ btnBrowseFfmpeg ✓

Step 3: Session (requires output folder)
  └─ btnNewSession
  └─ btnLoadSession
  
Step 4: Region Selection (requires session)
  └─ btnSelectRegion
  └─ btnFullScreen
  └─ btnShowRegion (requires region)

Step 5: Capture Settings (requires session, disabled during capture)
  └─ cmbAspectRatio
  └─ numInterval
  └─ cmbFormat
  └─ trkQuality
  └─ numQuality

Step 6: Start Capture (requires session + region)
  └─ btnStart

Step 7: Encoding (requires FFmpeg + frames)
  └─ btnEncode
```

## Quick Integration Pattern

Add this line at the end of methods that change application state:

```csharp
UpdateGuidedModeUI(); // Update guided mode UI state
```

Example:
```csharp
private void btnChooseFolder_Click(object? sender, EventArgs e)
{
    // ... existing code ...
    settings.SaveFolder = folderPath;
    SaveSettings();
    
    UpdateGuidedModeUI(); // ADD THIS LINE
}
```

## Testing the Feature

1. Launch the app - Guided Mode should be checked by default
2. Try clicking buttons in wrong order (e.g., Select Region before creating a session)
   - Button should be grayed out
   - Hovering should show tooltip explaining why
3. Uncheck "Guided Mode" checkbox
   - All buttons should become enabled
   - Tooltips should disappear
4. Re-check "Guided Mode"
   - Should return to progressive disclosure state

## Settings Persistence

The guided mode state should be saved in CaptureSettings and restored on launch. You may want to add:

```csharp
// In CaptureSettings.cs
public bool GuidedModeEnabled { get; set; } = true;

// In LoadSettings()
_guidedModeEnabled = settings.GuidedModeEnabled;
if (chkGuidedMode != null)
    chkGuidedMode.Checked = _guidedModeEnabled;

// In SaveSettings() or chkGuidedMode_CheckedChanged
settings.GuidedModeEnabled = _guidedModeEnabled;
```
