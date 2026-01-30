# Control State Management Integration Guide

This document outlines where to add `UpdateControlStates()` calls to enable guided setup flow.

## What UpdateControlStates() Does

The `UpdateControlStates()` method:
- Enables/disables controls based on prerequisites
- Shows helpful tooltips explaining why controls are disabled
- Highlights the next action the user should take
- Provides visual guidance through the setup flow

## Where to Add UpdateControlStates() Calls

Add `UpdateControlStates();` after ANY method that changes these states:

### 1. After Output Folder Changes
```csharp
private void btnChooseFolder_Click(object sender, EventArgs e)
{
    // ... existing folder selection code ...
    settings.SaveFolder = selectedPath;
    SaveSettings();
    UpdateControlStates(); // <-- ADD THIS
}
```

### 2. After Session Creation/Loading
```csharp
private void btnNewSession_Click(object sender, EventArgs e)
{
    // ... existing session creation code ...
    _activeSession = newSession;
    UpdateControlStates(); // <-- ADD THIS
}

private void btnLoadSession_Click(object sender, EventArgs e)
{
    // ... existing session loading code ...
    _activeSession = loadedSession;
    UpdateControlStates(); // <-- ADD THIS
}
```

### 3. After Region Selection
```csharp
private void btnSelectRegion_Click(object sender, EventArgs e)
{
    // ... existing region selection code ...
    SetCurrentRegion(selectedRegion);
    UpdateControlStates(); // <-- ADD THIS (already called by SetCurrentRegion)
}

private void btnFullScreen_Click(object sender, EventArgs e)
{
    // ... existing fullscreen code ...
    SetCurrentRegion(screenRegion);
    UpdateControlStates(); // <-- ADD THIS (already called by SetCurrentRegion)
}
```

**NOTE**: SetCurrentRegion() and ClearCurrentRegion() should ALSO call UpdateControlStates()

### 4. After Capture State Changes
```csharp
private void btnStart_Click(object sender, EventArgs e)
{
    // ... existing start capture code ...
    _captureTimer = new System.Threading.Timer(...);
    UpdateControlStates(); // <-- ADD THIS
}

private void btnStop_Click(object sender, EventArgs e)
{
    // ... existing stop capture code ...
    _captureTimer = null;
    UpdateControlStates(); // <-- ADD THIS
}
```

### 5. After Encoding State Changes
```csharp
private void btnEncode_Click(object sender, EventArgs e)
{
    _isEncoding = true;
    UpdateControlStates(); // <-- ADD THIS
    // ... existing encoding code ...
    
    // At the end of encoding:
    _isEncoding = false;
    UpdateControlStates(); // <-- ADD THIS
}
```

### 6. After FFmpeg Installation
```csharp
private void btnDownloadFfmpeg_Click(object sender, EventArgs e)
{
    // ... existing download code ...
    _ffmpegPath = installedPath;
    UpdateControlStates(); // <-- ADD THIS
}

private void btnBrowseFfmpeg_Click(object sender, EventArgs e)
{
    // ... existing browse code ...
    _ffmpegPath = selectedPath;
    UpdateControlStates(); // <-- ADD THIS
}
```

## Also Update These Methods

Add UpdateControlStates() call at the end of:

### SetCurrentRegion()
```csharp
private void SetCurrentRegion(Rectangle region)
{
    // ... existing code ...
    UpdateSessionInfoPanel();
    UpdateControlStates(); // <-- ADD THIS
}
```

### ClearCurrentRegion()
```csharp
private void ClearCurrentRegion()
{
    // ... existing code ...
    UpdateSessionInfoPanel();
    UpdateControlStates(); // <-- ADD THIS
}
```

### After Frame Capture (for encode button)
The UI update timer already calls UpdateResourceMonitoring(), so also call:
```csharp
_uiUpdateTimer.Tick += (s, e) => {
    UpdateCaptureTimer();
    UpdateResourceMonitoring();
    UpdateControlStates(); // <-- ADD THIS (updates encode button state)
};
```

## Benefits

Once integrated, users will experience:
1. **Clear Visual Guidance**: Disabled controls show why they're disabled
2. **Highlighted Next Steps**: The next required action is highlighted
3. **No Confusion**: Users can't accidentally try to use features out of order
4. **Flexible But Guided**: Users can still configure anything, but critical path is obvious
5. **Helpful Tooltips**: Hovering over disabled controls explains prerequisites

## Testing

After integration, test this flow:
1. Launch app → Should highlight "Choose Folder"
2. Choose folder → Should highlight "New Session" / "Load Session"
3. Create session → Should highlight "Select Region" / "Full Screen"
4. Select region → Should highlight "Start"
5. Start capture → Should grey out region/session controls
6. Capture frames → Should enable "Encode" (if FFmpeg available)
7. Stop capture → Should re-enable all controls
