# Bug Fixes & Roadmap

## ✅ Critical Bugs Fixed (This Session)

### 1. **Multi-Monitor Region Capture Offset** 🔴 CRITICAL
**Problem**: Region capture was offset incorrectly on multi-monitor setups, especially with secondary displays positioned to the right or at different positions.

**Root Cause**: The RegionSelector form was incorrectly calculating absolute screen coordinates by adding `Bounds.X` and `Bounds.Y` twice.

**Fix**: Changed to use `PointToScreen()` method which properly converts client coordinates to screen coordinates accounting for the virtual screen layout.

**Impact**: Region capture now works correctly across all monitor configurations.

---

### 2. **FFmpeg Auto-Download on App Start** 🟠 HIGH
**Problem**: FFmpeg downloader ran automatically when the app started, which could be:
- Surprising to users
- Problematic on metered connections
- Slow on first launch without user awareness

**Fix**: 
- Removed auto-download from `InitializeFfmpeg()`
- Added new `DownloadFfmpeg()` method triggered by user action
- Added "⬇ Download FFmpeg" button (green, prominent)
- Shows clear message: "⚠️ FFmpeg not found - Click 'Download FFmpeg' button"

**Impact**: Users now have full control over when to download FFmpeg.

---

### 3. **Session Name Collision Handling** 🟡 MEDIUM
**Problem**: Creating sessions with duplicate names would:
- Silently append `_1`, `_2`, etc. to folder names
- Not inform the user about the rename
- Cause confusion about which session is which

**Fix**:
- Session display name now shows adjusted name: "test (1)", "test (2)", etc.
- Warning message appears when name is adjusted:
  > ⚠️ A session with this name already exists.  
  > The new session was renamed to avoid conflicts.
- Folder structure still uses `test_1`, `test_2` (filesystem-safe)

**Impact**: Users are now aware of duplicate names and can distinguish sessions.

---

### 4. **New Session Format Validation Bug (van2 scenario)** 🟡 MEDIUM
**Problem**: When creating a new session (e.g., "van2"), changing the format would incorrectly validate against the previous session:
- User creates session "van" with JPEG
- User creates new session "van2" (marks "van" inactive)
- User tries to change format to PNG
- Error: "Active session uses JPEG format" ← WRONG!

**Root Cause**: Format validation was checking `_activeSession` without considering if it had frames yet.

**Fix**: Format validation now only triggers if:
1. Session exists AND
2. Session has frames captured (`FramesCaptured > 0`) AND
3. Not currently capturing

**Impact**: New sessions can freely change settings before capturing starts.

---

### 5. **Right-Click in Region Selector** 🟢 LOW
**Problem**: Right-clicking during region selection would exit completely, requiring the user to go through the entire flow again.

**Enhancement**: 
- Right-click during drag: Cancels current selection, allows immediate retry
- Right-click with no drag: Exits selector
- Added instructions overlay: "LEFT CLICK & DRAG to select region • RIGHT CLICK to cancel • ESC to exit"

**Impact**: Better UX for correcting mistakes during region selection.

---

## 🎯 Design Philosophy Clarifications

### Power User Controls
**Guideline**: Settings should be lockable/unlockable based on encoding requirements, not user convenience.

**Categories**:

1. **Can Be Changed Mid-Session** (unlockable for power users):
   - ❓ Interval (changes playback speed consistency, but won't break encoding)
   - Note: May add "Advanced" toggle to unlock these with warnings

2. **Cannot Be Changed** (always locked during capture):
   - Region dimensions (breaks frame consistency)
   - Image format (incompatible frame types)
   - JPEG quality (inconsistent compression across frames)

**Current Implementation**: Settings are locked during capture, unlocked when stopped. Future: Add "Advanced Mode" toggle for power user control.

---

## 🚀 Future Enhancements Roadmap

### Priority 1: Region Overlay & Adjustment ✅ COMPLETED

**Status**: Implemented in v1.1.0-dev

**Completed Features**:
- ✅ Hotkey/button to show region overlay (Ctrl+R, "Show/Hide" button)
- ✅ Semi-transparent colored border showing capture area
- ✅ Display region info (dimensions, position)
- ✅ Ability to see region when loading previous sessions
- ✅ HUD-style corner brackets (aerospace aesthetic)
- ✅ Click-through functionality
- ✅ Color-coded borders (green=capturing, blue=inactive)
- ✅ Fade in/out animations

**Deferred** (requires further investigation):
- ⏳ Move region by dragging overlay
- ⏳ Resize region by dragging corners/edges
- ⏳ Real-time validation of encoding requirements (even dimensions)
- ⏳ Update session metadata when region changes
- ⏳ Warning about potential issues with mid-session changes

**Technical Considerations**:
- Moving region: Safe for same dimensions (requires testing)
- Resizing region: May break encoding if frame count > 0
- Need to test: Can FFmpeg handle varying frame dimensions in concat mode?

**Design Notes**: 
- Overlay feels natural, like a targeting system
- Matches aerospace HUD aesthetic perfectly
- User feedback: Very helpful for multi-monitor setups

**Next Steps** (for v1.2.0):
1. Test "move region (same dimensions)" scenario
2. If safe, add dragging functionality to overlay
3. Consider resize handles for pre-capture adjustment only

---

### Priority 2: Simple Video Editor

**Feature**: Trim and crop videos in a streamlined interface

**Core Requirements**:
- MUST be simple and essential features only
- MUST not compromise app efficiency/pragmatism
- MUST fit aesthetically with current design
- MUST maintain power user experience

**Features to Include**:
- [ ] **Trim**: Set in/out points on timeline
- [ ] **Crop**: Select subregion of video frame
- [ ] Save as new version (keep original)
- [ ] Save over original (with confirmation)
- [ ] Quick presets (remove first/last N seconds)

**Features to EXCLUDE** (complexity creep):
- ❌ Color correction
- ❌ Transitions/effects
- ❌ Audio editing (timelapses are silent)
- ❌ Multiple video tracks
- ❌ Text overlays (maybe later)

**Implementation Ideas**:
1. **Timeline Scrubber**: Simple bar with draggable in/out markers
2. **Crop Preview**: Click to enter crop mode, drag rectangle over frame
3. **Quick Actions**: "Remove first 3 seconds", "Remove last 5 seconds"
4. **Keyboard Shortcuts**: I/O for in/out points, Space for play/pause

**Technical Stack** (options to investigate):
- Option A: FFmpeg trim/crop commands (simple, matches existing code)
- Option B: FFmpeg.NET wrapper (higher-level API)
- Option C: Custom timeline control (more work, more control)

**Design Inspiration**:
- Think: Instagram story editor (simple, touch-friendly)
- NOT: DaVinci Resolve (too complex)
- Reference: Your design mockups (aerospace HUD aesthetic)

---

### Priority 3: Last Frame Thumbnail

**Feature**: Show thumbnail of most recent captured frame

**Requirements**:
- [ ] Display last captured frame in UI
- [ ] Update in real-time during capture
- [ ] Show "No frames yet" placeholder when empty
- [ ] Click to open full frame in system viewer
- [ ] Aesthetic integration with design language

**Design Brainstorming** (to revisit):
- Location: Could replace or supplement status display
- Style: HUD-style frame with corner brackets (matches region selector)
- Animation: Subtle pulse/flash when new frame captured
- Border: Color-coded by status (green=capturing, blue=stopped, gray=no session)
- Overlay: Frame number, timestamp overlay on thumbnail?

**Technical Notes**:
- Load last frame from session/frames/ folder
- Efficient: Don't reload every paint, only on frame capture
- Memory: Scale down thumbnail (150x150px sufficient)
- Thread: Load on background thread to avoid UI freeze

**Inspiration from Design Mockups**:
- Aerospace/HUD aesthetic
- Minimalist but informative
- Corner brackets/targeting system vibe
- Dark theme with accent colors

---

## 📋 Minor Improvements & Polish

### Code Quality
- [ ] Add XML documentation to all public methods (mostly done)
- [ ] Consistent error handling patterns
- [ ] Add unit tests for critical components
- [ ] Performance profiling for high frame count sessions

### UI/UX Polish
- [ ] Loading spinner during FFmpeg download
- [ ] Progress bar for encoding (not just status text)
- [ ] Keyboard shortcuts for common actions
- [ ] Drag-and-drop session file loading
- [ ] Recent sessions list (quick load)

### Session Management
- [ ] Session notes/description field
- [ ] Session tags for organization
- [ ] Search/filter sessions
- [ ] Export session metadata as JSON/CSV
- [ ] Backup/restore sessions

### Quality of Life
- [ ] Auto-save settings more frequently
- [ ] Undo/redo for region selection
- [ ] Copy region coordinates to clipboard
- [ ] Preset aspect ratios with custom names
- [ ] Save/load application workspace layouts

---

## 🐛 Known Issues (To Monitor)

### None Currently! 
All reported bugs have been addressed in this session.

---

## 💭 Technical Debt

### Potential Refactoring
1. **SessionManager**: Could be split into:
   - SessionMetadata (data handling)
   - SessionFileSystem (folder operations)
   - SessionValidation (settings validation)

2. **MainForm**: Very large, could be split into:
   - MainForm (orchestration)
   - CaptureController (capture logic)
   - SessionController (session lifecycle)
   - UIStateManager (UI updates)

3. **Settings**: Consider using INotifyPropertyChanged for auto-save

### Performance Optimizations
1. **Frame Capture**: Consider async/await pattern for screen capture
2. **Thumbnail Loading**: Implement LRU cache for recent frames
3. **File I/O**: Batch session.json updates during rapid captures

---

## 📝 User Feedback Integration

### From Latest Testing Session
✅ "FFmpeg shouldn't download automatically" - FIXED  
✅ "Region capture is offset on my second monitor" - FIXED  
✅ "Can't tell where I was capturing when loading session" - NOTED (Priority 1)  
✅ "Right-click should let me try again" - FIXED  
✅ "Session names getting confused" - FIXED  
✅ "Format locked when it shouldn't be" - FIXED  

### Positive Feedback
👍 "App is working very well"  
👍 "Generally pragmatic and efficient"  
👍 "Strong emphasis on user experience appreciated"  

---

## 🎨 Design Language Guidelines

### Aesthetic Goals
- **Aerospace/HUD inspiration**: Corner brackets, targeting reticles
- **Dark theme with accent colors**: Dark gray base, bright accent colors
- **Minimalist but informative**: Only show what's needed, but show it clearly
- **Power user friendly**: Advanced features accessible but not in the way

### Color Palette
- Background: #141414 (20, 20, 20)
- Foreground: #C8C8C8 (200, 200, 200)
- Primary: #007ACC (0, 122, 204) - Blue
- Success: #00C864 (0, 200, 100) - Green
- Warning: #FFB900 (255, 185, 0) - Yellow
- Danger: #C00000 (192, 0, 0) - Red

### Typography
- Primary: Segoe UI (9pt, regular)
- Monospace: Consolas (11pt, bold) - for region selector
- Emphasis: Bold weight, slightly larger

---

## 🔄 Version History

### v1.1.0 (Current Development)
- ✅ Fixed multi-monitor region capture offset
- ✅ Added user-triggered FFmpeg download
- ✅ Improved session name collision handling
- ✅ Fixed new session format validation
- ✅ Enhanced region selector UX (right-click retry, instructions)
- ✅ Download progress reporting
- ✅ Automatic retry logic
- ✅ Download validation

### v1.0.0 (Previous Release)
- Initial release with basic timelapse capture
- Session management
- Region selection
- FFmpeg integration
- Video encoding

---

## 📧 Contact & Contributions

This is an actively developed project with strong focus on:
1. **User Experience**: Pragmatic, efficient, powerful
2. **Code Quality**: Clean, maintainable, documented
3. **Performance**: Fast, responsive, reliable

Feedback and suggestions are always welcome!

---

*Last Updated: 2025-10-20*  
*Status: Active Development*
