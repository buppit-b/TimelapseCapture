# Window Capture Feature

## Overview
The window capture feature allows you to capture a specific application window instead of a full screen or custom region. This is particularly useful for:

- Recording specific application activity (e.g., Blender, Photoshop, Visual Studio)
- Avoiding desktop clutter in your timelapse
- Capturing windows that move or resize during recording
- Creating focused timelapses of a single application

## How to Use

### 1. Select a Window
1. Click the **🪟 Window** button in the Capture Settings section
2. A window selector dialog will appear showing all open windows
3. Select the window you want to capture
4. Click **✓ Select Window**

### 2. Important Notes
- **Keep Window Visible**: The selected window must remain visible (not minimized) during capture
- **Window Position**: If the window moves, the capture region moves with it automatically
- **Window Resizing**: If the window is resized, you'll need to reselect it to update the capture bounds
- **Minimized Windows**: You cannot select minimized windows. Restore the window first.

### 3. Technical Details

#### Window Detection
The feature uses Win32 APIs to enumerate and detect visible windows:
- Filters out system windows (desktop, taskbar, etc.)
- Shows only windows with titles
- Excludes windows smaller than 50×50 pixels
- Lists windows alphabetically for easy selection

#### Dimension Requirements
- Window dimensions are automatically adjusted to be even numbers (required for video encoding)
- Minimum dimensions: 2×2 pixels
- Maximum dimensions: No limit (constrained by monitor size)

#### Multi-Monitor Support
Window capture works seamlessly across multiple monitors. The window's screen coordinates are tracked accurately regardless of which monitor it's on.

## UI Layout

The three capture mode buttons are arranged horizontally:
- **📐 Region** - Select custom region with mouse
- **🪟 Window** - Select specific window
- **🖥️ Full ▼** - Select full monitor (dropdown)

## Implementation Details

### Files Added
- `WindowSelector.cs` - Dialog for selecting windows with live preview

### Files Modified
- `MainForm.cs` - Added `btnSelectWindow_Click` handler
- `MainForm.Designer.cs` - Added window button to UI layout

### Key Functions
- **EnumWindows**: Enumerates all top-level windows
- **GetWindowRect**: Gets window bounds in screen coordinates
- **IsIconic**: Checks if window is minimized
- **GetWindowText**: Retrieves window title

## Troubleshooting

### Window Not Appearing in List
- Ensure the window is not minimized
- Verify the window has a title bar
- Check if the window is larger than 50×50 pixels

### Capture Not Working
- Verify the window remains visible during capture
- Check that the window hasn't been minimized
- Ensure the window hasn't closed or crashed

### Wrong Area Being Captured
- The window may have moved - reselect it
- The window may have been resized - reselect it
- Multiple windows with same title - verify correct window selected

## Future Enhancements
Possible improvements for future versions:
- Auto-tracking of window movement/resize
- Window-relative capture (content only, no title bar)
- Named window presets for quick selection
- Process-based filtering in window list
