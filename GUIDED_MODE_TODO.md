# Manual Integration Steps for Guided Mode

## ✅ What's Already Done

1. ✅ Added `chkGuidedMode` checkbox to the form
2. ✅ Added guided mode logic (`UpdateGuidedModeUI`, `EnableAllControls`, tooltip helpers)
3. ✅ Added `_guidedModeEnabled` and `_tooltips` fields
4. ✅ Added initial UpdateGuidedModeUI() calls in constructor and SetCurrentRegion()
5. ✅ Fixed cross-thread operation issues with UIHelper methods

## 🔧 What Needs Manual Integration

### Step 1: Find and Update These Methods

Search MainForm.cs for these method signatures and add `UpdateGuidedModeUI();` at the end (after SaveSettings or after state changes):

```csharp
// 1. When choosing output folder
private void btnChooseFolder_Click(object? sender, EventArgs e)
{
    // ... existing code ...
    UpdateGuidedModeUI(); // ADD THIS
}

// 2. When creating new session
private void btnNewSession_Click(object? sender, EventArgs e)
{
    // ... existing code ...
    UpdateGuidedModeUI(); // ADD THIS
}

// 3. When loading session
private void btnLoadSession_Click(object? sender, EventArgs e)
{
    // ... existing code ...
    UpdateGuidedModeUI(); // ADD THIS
}

// 4. When starting capture
private void btnStart_Click(object? sender, EventArgs e)
{
    // ... existing code ...
    UpdateGuidedModeUI(); // ADD THIS
}

// 5. When stopping capture  
private void btnStop_Click(object? sender, EventArgs e)
{
    // ... existing code ...
    UpdateGuidedModeUI(); // ADD THIS
}

// 6. After FFmpeg download
private void btnDownloadFfmpeg_Click(object? sender, EventArgs e)
{
    // ... existing code ...
    UpdateGuidedModeUI(); // ADD THIS
}

// 7. After browsing for FFmpeg
private void btnBrowseFfmpeg_Click(object? sender, EventArgs e)
{
    // ... existing code ...
    UpdateGuidedModeUI(); // ADD THIS
}

// 8. When clearing region
private void ClearCurrentRegion()
{
    // ... existing code ...
    UpdateGuidedModeUI(); // ADD THIS
}
```

### Step 2: Save Guided Mode Preference

Add to `CaptureSettings.cs`:

```csharp
public bool GuidedModeEnabled { get; set; } = true;
```

Add to `LoadSettings()` method in MainForm.cs:

```csharp
_guidedModeEnabled = settings.GuidedModeEnabled;
if (chkGuidedMode != null)
    chkGuidedMode.Checked = _guidedModeEnabled;
```

Update `SaveSettings()` or the checkbox event handler to save:

```csharp
settings.GuidedModeEnabled = _guidedModeEnabled;
```

### Step 3: Test The Feature

1. **Launch app** - Guided Mode checkbox should be checked
2. **Without output folder**, try:
   - Click "New Session" → Should be disabled with tooltip "Choose an output folder first"
   - Click "Select Region" → Should be disabled with tooltip "Create or load a session first"
3. **Select output folder** → Session buttons should enable
4. **Create session** → Region buttons should enable
5. **Select region** → Start button should enable
6. **Uncheck Guided Mode** → Everything should enable
7. **Re-check Guided Mode** → Should return to progressive state

### Step 4: Add Disposal for Tooltips

In the `Dispose()` method (in MainForm.Designer.cs), add tooltip cleanup:

```csharp
// Dispose tooltips
if (_tooltips != null)
{
    foreach (var tooltip in _tooltips.Values)
    {
        tooltip?.Dispose();
    }
    _tooltips.Clear();
}
```

## Quick Search-and-Replace Pattern

For each button click handler, add after the main logic:

**Find pattern**: Look for lines like `SaveSettings();` or `UpdateStatusDisplay();` at the end of button handlers

**Add after**: `UpdateGuidedModeUI(); // Update guided mode UI state`

## Why This Approach?

The guided mode system provides:
- **Beginner-friendly**: Clear visual guidance through setup steps
- **Optional**: Can be turned off for advanced users who want full control
- **Informative**: Tooltips explain prerequisites 
- **Non-blocking**: All controls remain clickable (just greyed out) so users can experiment

The progressive steps are:
1. Output Folder → 2. Session → 3. Region → 4. Capture Settings → 5. Start Capture → 6. Encode

This matches the natural workflow while maintaining flexibility.
