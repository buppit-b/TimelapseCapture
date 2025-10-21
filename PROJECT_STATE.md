# TimelapseCapture - Project State Document

**Purpose**: This document maintains project continuity across development sessions.

---

## Project Overview

**Name**: TimelapseCapture  
**Type**: Windows Forms (.NET 9) Desktop Application  
**Purpose**: Lightweight timelapse screen capture tool optimized for streaming  
**Current Version**: 1.1.0-dev  
**Last Updated**: 2025-10-21

---

## Core Functionality

### What It Does
- Captures screen regions at set intervals
- Supports JPEG/PNG output with quality control
- Organizes captures into named sessions
- Encodes timelapses to MP4 using FFmpeg
- Persists settings and session metadata

### Key Features
- Dark mode UI with aerospace/HUD aesthetic
- Multi-monitor region selection
- Session management with metadata
- Aspect ratio presets (16:9, 4:3, 1:1, 21:9, custom)
- FFmpeg auto-downloader with progress reporting
- Settings locked during capture to prevent encoding issues

---

## Current Architecture

### Core Files
- `MainForm.cs` - Primary UI orchestration (~1200 lines, needs refactoring)
- `SessionManager.cs` - Session lifecycle and metadata management
- `SettingsManager.cs` - JSON-based settings persistence
- `RegionSelector.cs` - Full-screen region selection overlay
- `FfmpegRunner.cs` - FFmpeg command execution
- `FfmpegDownloader.cs` - Automatic FFmpeg acquisition
- `AspectRatio.cs` - Aspect ratio calculations and constraints

### Data Structure
```
AppDirectory/
├── settings.json (global settings)
├── sessions/
│   ├── {SessionName}/
│   │   ├── session.json (metadata)
│   │   ├── frames/
│   │   │   ├── frame_0001.jpg
│   │   │   ├── frame_0002.jpg
│   │   │   └── ...
│   │   └── {SessionName}.mp4 (encoded output)
```

### Session Metadata (session.json)
```json
{
  "Name": "SessionName",
  "RegionX": 100,
  "RegionY": 100,
  "RegionWidth": 1920,
  "RegionHeight": 1080,
  "Interval": 5.0,
  "Format": "jpeg",
  "Quality": 90,
  "FramesCaptured": 150,
  "IsActive": true,
  "CreatedDate": "2025-10-21T10:30:00"
}
```

---

## Design Philosophy

### Core Principles
1. **Power User First** - Advanced features accessible, not hidden
2. **Pragmatic & Efficient** - No feature bloat, every feature earns its place
3. **Encoding Integrity** - Never compromise video encoding requirements
4. **Visual Consistency** - Aerospace/HUD aesthetic throughout

### Design Language
- **Aesthetic**: Dark theme with aerospace/HUD inspiration
- **Colors**:
  - Background: #141414 (20, 20, 20)
  - Foreground: #C8C8C8 (200, 200, 200)
  - Primary: #007ACC (Blue)
  - Success: #00C864 (Green)
  - Warning: #FFB900 (Yellow)
  - Danger: #C00000 (Red)
- **Typography**: Segoe UI 9pt, Consolas 11pt (monospace)
- **Visual Style**: Corner brackets, targeting reticles, minimalist overlays

### Technical Constraints
- **Cannot change mid-capture**: Region dimensions, format, JPEG quality
- **Can change mid-capture**: Interval (affects playback speed but not encoding)
- **Encoding requirements**: Consistent frame dimensions throughout session

---

## Version History

### v1.1.0-dev (Current Development - 2025-10-21)
**Status**: Active Development

**Completed**:
- ✅ Fixed multi-monitor region capture offset bug
- ✅ User-triggered FFmpeg download (removed auto-download)
- ✅ Session name collision handling with user warnings
- ✅ Fixed new session format validation (van2 bug)
- ✅ Enhanced region selector (right-click retry, instructions)
- ✅ FFmpeg downloader overhaul (retry, validation, progress)

**In Progress**:
- 🚧 Region overlay system (Priority 1)

**Planned for this version**:
- Region overlay toggle with HUD-style display
- Region info display (dimensions, position)
- Region adjustment before session start
- Investigation: Mid-session region changes (if viable)

### v1.0.0 (Initial Release)
- Basic timelapse capture functionality
- Session management
- Region selection
- FFmpeg integration
- Video encoding

---

## Active Development Priorities

### Priority 1: Region Overlay & Adjustment (CURRENT)
**Goal**: Visualize and adjust capture region

**Requirements**:
- [ ] Toggle overlay showing selected region
- [ ] Semi-transparent HUD-style border
- [ ] Display region info (dimensions, position)
- [ ] Show region when loading previous sessions
- [ ] Allow region adjustment BEFORE capture starts
- [ ] Investigate: Mid-session changes (if FFmpeg supports varying dimensions)

**Technical Constraints**:
- MUST maintain encoding integrity
- CANNOT change dimensions mid-session unless verified safe
- MUST validate FFmpeg concat demuxer behavior with varying dimensions
- Moving region (same dimensions): Likely safe
- Resizing region (different dimensions): Requires investigation

**Design Approach**:
- HUD-style overlay with corner brackets
- Color-coded border (green=active, blue=inactive, yellow=adjusting)
- Hotkey toggle (Ctrl+R suggested)
- Button in main UI
- Draggable overlay for repositioning (if safe)
- Corner/edge drag handles for resizing (if safe, pre-capture only)

### Priority 2: Simple Video Editor
**Goal**: Trim and crop captured timelapses

**Scope**: ESSENTIAL FEATURES ONLY
- Trim: Set in/out points
- Crop: Select subregion
- Save as new / overwrite original

**Excluded** (complexity creep):
- ❌ Color correction
- ❌ Transitions/effects
- ❌ Audio editing
- ❌ Multi-track editing
- ❌ Text overlays

### Priority 3: Last Frame Thumbnail
**Goal**: Visual feedback during capture

**Features**:
- Display last captured frame
- Real-time updates during capture
- Click to open full image
- HUD-style frame with corner brackets
- Color-coded border by status

---

## Known Technical Debt

### Code Refactoring Needed
1. **MainForm.cs** - Too large, needs splitting:
   - MainForm (orchestration)
   - CaptureController (capture logic)
   - SessionController (session lifecycle)
   - UIStateManager (UI updates)

2. **SessionManager.cs** - Could split into:
   - SessionMetadata (data)
   - SessionFileSystem (folders)
   - SessionValidation (validation)

3. **Settings** - Consider INotifyPropertyChanged for auto-save

### Performance Optimizations
- Frame capture: Async/await pattern
- Thumbnail loading: LRU cache
- File I/O: Batch session.json updates

---

## FFmpeg Integration Details

### Download Source
- URL: `https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip`
- Version: Latest stable essentials build
- Extracts: `ffmpeg.exe` and `ffprobe.exe`

### Encoding Command
```bash
ffmpeg -f concat -safe 0 -i filelist.txt -c:v libx264 -pix_fmt yuv420p -crf 23 output.mp4
```

### Requirements
- Consistent frame dimensions across all frames
- Even width/height (divisible by 2)
- Same format for all frames

### Known Limitations
- Cannot mix JPEG and PNG in same video
- Cannot vary frame dimensions (unverified if fixable)
- Quality changes create inconsistent compression

---

## User Feedback & Pain Points

### Addressed
✅ "FFmpeg shouldn't download automatically"  
✅ "Region capture offset on second monitor"  
✅ "Can't tell where I was capturing"  
✅ "Right-click should let me try again"  
✅ "Session names getting confused"  
✅ "Format locked when shouldn't be"

### Outstanding
- Region visibility when loading sessions (Priority 1)
- Quick video trimming (Priority 2)
- Visual capture feedback (Priority 3)

---

## Development Environment

### Requirements
- .NET SDK 9.0+
- Windows OS (WinForms)
- Visual Studio 2022 or VS Code

### Build Commands
```bash
# Build
dotnet build

# Run
dotnet run

# Publish single-file
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

### Project Structure
- Solution: `TimelapseCapture.sln`
- Project: `TimelapseCapture.csproj`
- Target: `net9.0-windows`
- Output: `bin/Release/net9.0-windows/`

---

## Testing Scenarios

### Critical Test Cases
1. ✅ Multi-monitor region selection
2. ✅ FFmpeg download with retry
3. ✅ Session name collisions
4. ✅ Format validation for new sessions
5. ⏳ Region overlay display
6. ⏳ Region adjustment pre-capture
7. ⏳ FFmpeg with varying dimensions (investigation)

### Edge Cases to Monitor
- Network interruption during FFmpeg download
- Disk full during capture
- Invalid region selection (off-screen)
- Session folder permissions
- Rapid start/stop cycles

---

## Next Session Checklist

When resuming development:
1. ✅ Read this PROJECT_STATE.md
2. ✅ Check BUGFIXES_AND_ROADMAP.md for latest priorities
3. ✅ Review version.json for current version
4. ✅ Check git log for recent changes
5. ✅ Run application to verify current state

---

## Contact & Philosophy

**Development Approach**:
- User experience first
- Code quality matters
- Performance is essential
- Feedback drives iteration

**Code Standards**:
- XML documentation for public APIs
- Consistent naming conventions
- Minimal dependencies
- Clear separation of concerns

---

*This document is the source of truth for project continuity.*  
*Update after every significant development session.*

**Last Updated**: 2025-10-21  
**Updated By**: Development session with Claude  
**Next Priority**: Region Overlay System (Priority 1)
