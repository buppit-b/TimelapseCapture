# TimelapseCapture — Structural Map

> **Start at `/CLAUDE.md` (project root) first.** This file is deep-reference
> material for system ownership — read it when a bug doesn't obviously belong
> to one file. It is not the session-start doc anymore.

## Purpose of This Document

This document describes the **structural layout** of the TimelapseCapture codebase.

It exists to:
- Give humans and LLMs a mental map of the system
- Define system boundaries and ownership
- Prevent architectural drift and accidental coupling
- Reduce the amount of code that must be read at once

This is not implementation documentation.
This file changes rarely.

If code behavior contradicts this document, the code is wrong.

---

## High-Level System Overview

TimelapseCapture is composed of six major systems coordinated by a single UI orchestrator.

Flow of control is:

```
User Interaction
    ↓
MainForm (UI Orchestrator)
    ↓
    ├→ Capture Engine (timer-based frame capture)
    ├→ Session System (persistence & state)
    ├→ Region System (selection & overlay)
    ├→ Activity Monitor (smart intervals)
    ├→ Settings System (user preferences)
    └→ Encoding Pipeline (FFmpeg video generation)
```

All subsystem coordination flows through `MainForm`.

There is no peer-to-peer communication between subsystems.

---

## System 1: UI Orchestration — MainForm

**Primary Files**
- `src/UI/MainForm.cs` (~3800 lines, verified 2026-06-22 - main application logic)
- `src/UI/MainForm.Designer.cs` (auto-generated UI definitions)
- `src/UI/MainForm.ControlState.cs` (guided mode & control state management)

**Role**
- Owns application lifecycle
- Owns user intent
- Coordinates all other systems
- Acts as the only integration point

**Owns**
- Capture start and stop
- Active session reference (`_activeSession`)
- Capture timers (`_captureTimer`)
- UI state and guided mode
- Readiness checks
- Error presentation
- Thread synchronization (`_captureLock`)

**Key Methods**
- `SetCurrentRegion()` - Single point for region updates (CRITICAL)
- `ClearCurrentRegion()` - Single point for region clearing (CRITICAL)
- `GetCurrentRegion()` - Region accessor with session priority
- `CaptureFrame()` - Timer callback for periodic captures
- `StartCapture()` / `StopCapture()` - Capture lifecycle
- `UpdateGuidedModeUI()` - Progressive disclosure logic
- `SaveSettings()` / `SaveSettingsImmediate()` - Settings persistence

**Allowed To**
- Call into Capture Engine (internal methods)
- Call SessionManager methods
- Update UI via UIHelper
- Trigger encoding via FfmpegRunner
- Control ActivityMonitor lifecycle

**Must NOT**
- Perform low-level capture logic directly (use CaptureFrame pattern)
- Mutate session files directly (use SessionManager)
- Encode video logic internally (use FfmpegRunner)
- Store persistent state outside SettingsManager or SessionManager
- Bypass region synchronization methods

**Threading Model**
- UI thread: Handles all user interaction and control updates
- Timer thread: Runs CaptureFrame() via System.Threading.Timer
- All timer-to-UI communication uses UIHelper for thread safety

**Notes**
- Large by necessity (~4000 lines)
- Changes here are high-risk
- Prefer surgical edits only
- File is organized into regions for readability

---

## System 2: Capture Engine

**Primary Location**
- Implemented inside `MainForm.cs` (logical subsystem, not isolated file)

**Key Entry Point**
- `CaptureFrame(object? state)` - Timer callback

**Supporting Methods**
- `CaptureScreen()` - Win32 BitBlt wrapper for screen capture
- `SaveFrame()` - Writes bitmap to disk
- `UpdateCaptureStatistics()` - Updates frame count and timing

**Role**
- Periodic screen capture via timer
- Timing authority for capture intervals
- Frame creation and storage
- Error tracking and auto-stop

**Threading**
- Runs on System.Threading.Timer thread (NOT UI thread)
- Synchronizes via `lock(_captureLock)` for all session access
- UI updates via UIHelper.SafeX() methods

**Owns**
- Capture timing and scheduling
- Bitmap creation from screen region
- Win32 BitBlt API calls
- Frame file writing to session frames folder
- Consecutive error counting (`_consecutiveCaptureErrors`)

**Invariants**
- Only one active capture loop at a time
- All session mutation during capture must be inside `lock(_captureLock)`
- Errors are counted and capped at `Constants.MAX_CONSECUTIVE_ERRORS` (3)
- Capture region must be set before capture starts
- Frame filenames are zero-padded sequential: 00001.jpg, 00002.jpg, etc.

**Must NOT**
- Touch UI controls directly (use UIHelper)
- Modify region state directly (use SetCurrentRegion/ClearCurrentRegion)
- Perform encoding (that's FFmpeg's job)
- Spawn background tasks or use async/await
- Mutate session without holding _captureLock

**Error Handling**
- Tracks consecutive errors via `_consecutiveCaptureErrors`
- Auto-stops capture after MAX_CONSECUTIVE_ERRORS
- Resets error counter on: capture start, new session, successful capture

---

## System 3: Session Persistence — SessionManager

**Primary Files**
- `src/Core/SessionManager.cs`
- `SessionInfo` model class (in same file)

**Role**
- Session creation, discovery, and lifecycle
- Save, load, and repair operations
- Folder structure enforcement
- Frame counting and metadata persistence
- Format versioning (V1 flat → V2 organized)

**Owns**
- `session.json` file format and structure
- Session folder layout:
  ```
  captures/{session-name}/
  ├── session.json          (metadata)
  ├── frames/               (captured images)
  ├── output/               (encoded videos)
  └── .temp/                (FFmpeg working files)
  ```
- Frame counting via `IncrementFrameCount()`
- Active session detection
- Session validation and repair logic

**Key Methods**
- `CreateNamedSession()` - Creates new session with optional region
- `LoadSession()` - Deserializes session.json
- `SaveSession()` - Persists session state to disk
- `FindActiveSession()` - Discovers currently active session
- `GetFrameFiles()` - Returns sorted list of frame paths
- `MigrateSessionToV2()` - Upgrades old flat structure
- `MarkSessionInactive()` - Marks session as complete

**SessionInfo Properties (Important)**
- `Name` - Session identifier
- `Active` - Is this the current session?
- `FramesCaptured` - Must match actual frame count
- `CaptureRegion` - **Nullable** (can be set after creation)
- `IntervalSeconds` - Base capture interval
- `TotalCaptureSeconds` - Actual elapsed capture time
- `LastCaptureTime` - Timestamp of last frame
- `SmartIntervalEnabled` - Activity-based timing enabled
- `FormatVersion` - 2 = current (organized folders)

**Invariants**
- Session files are user data - NEVER delete automatically
- FramesCaptured must reflect actual frame count on disk
- CaptureRegion may be null before first frame (recoverable state)
- Active = true for at most ONE session at a time
- Sessions must have FormatVersion = 2 (V1 auto-migrates)

**Repair Logic**
- `ValidateAndRepairSession()` (in MainForm) detects corruption
- FramesCaptured > 0 && CaptureRegion == null triggers repair
- Infers region from first frame dimensions via Image.FromFile()
- User prompted before applying repair

**Must NOT**
- Capture frames (that's Capture Engine's job)
- Interact with UI directly (return data to MainForm)
- Encode video (that's FFmpeg's job)
- Assume capture is running (sessions persist after stop)

---

## System 4: Region and Overlay System

**Primary Files**
- `src/Capture/RegionSelector.cs` - Interactive selection overlay
- `src/Capture/RegionOverlay.cs` - Live capture region HUD
- `src/Capture/AspectRatio.cs` - Ratio calculations

**Role**
- Region selection with aspect ratio locking
- Visual feedback during selection and capture
- Aspect ratio enforcement and validation

**Critical Invariant (NON-NEGOTIABLE)**

Region exists in exactly **three locations** and MUST remain synchronized:

1. `captureRegion` (nullable Rectangle, runtime field in MainForm)
2. `_activeSession.CaptureRegion` (nullable Rectangle, in SessionInfo)
3. `settings.Region` (nullable Rectangle, in CaptureSettings)

**ONLY Allowed Modification Methods**
- `SetCurrentRegion(Rectangle region)` - Updates ALL three locations atomically
- `ClearCurrentRegion()` - Sets ALL three to null
- `GetCurrentRegion()` - Reads region with session priority
- `SetCaptureRegionFromNullable(Rectangle? region)` - Helper for nullable assignment

**Verified exceptions (2026-06-22) — do not "fix" these:**
`SaveSettings()`, `SaveSettingsImmediate()`, and `StopCapture()` in MainForm.cs
each mirror the canonical `captureRegion` field into `settings.Region` /
`_activeSession.CaptureRegion` immediately before persisting. These are
one-directional (read canonical, write copy) and cannot cause desync on their
own. The `StopCapture()` instance is a deliberate fix for a real prior bug.
Routing them through `SetCurrentRegion()` would recurse, since that method
calls `SaveSettings()` internally. See `/CLAUDE.md` for the full note.

**Why This Matters**
- Desynchronization causes corruption (Issue #3 - FIXED)
- Session can have region even if settings doesn't (and vice versa)
- Capture timer reads from _activeSession.CaptureRegion
- UI displays read from GetCurrentRegion()
- Persistence reads from settings.Region

**RegionSelector**
- Full-screen overlay with crosshair cursor
- Supports aspect ratio locking (16:9, 4:3, 1:1, Free)
- Multi-monitor aware (uses SystemInformation.VirtualScreen)
- Returns absolute screen coordinates
- Escape key cancels selection

**RegionOverlay**
- HUD-style corner brackets showing capture area
- Displays position and dimensions
- Capture state indicator (green = active, blue = paused)
- Click-through (WS_EX_TRANSPARENT) - doesn't block input
- Toggle with Ctrl+R
- Always on top

**AspectRatio**
- Calculates aspect ratios from dimensions
- Enforces ratio constraints during selection
- Provides ratio string formatting (e.g., "16:9")

**Must NOT**
- Store region in any other location
- Bypass SetCurrentRegion/ClearCurrentRegion methods
- Modify region during active capture (locked after first frame)
- Assume region is always set (it's nullable)

**Overlay Rules**
- Overlay is visual only - no capture logic
- Overlay creation/disposal managed by MainForm
- Overlay must never affect capture timing or state

---

## System 5: Activity Monitor — Smart Intervals

**Primary Files**
- `src/Capture/ActivityMonitor.cs`

**Role**
- Monitors user input activity (keyboard, mouse, stylus)
- Enables smart interval adjustment
- Detects active vs. idle states

**How It Works**
- Uses Windows low-level hooks (WH_KEYBOARD_LL, WH_MOUSE_LL)
- Runs on its own thread via Win32 SetWindowsHookEx
- Tracks last activity time
- Fires events when activity/idle state changes

**Key Properties**
- `IsEnabled` - Activity monitoring on/off
- `IdleThresholdSeconds` - Time before considered idle (default: 30s)
- `TrackMouseMovement` - Whether mouse movement counts (default: true)
- `LastActivityTime` - Thread-safe timestamp of last input

**Events**
- `ActivityDetected` - Fired when activity resumes after idle
- `IdleDetected` - Fired when idle threshold exceeded

**Integration with Capture**
- MainForm subscribes to ActivityDetected/IdleDetected events
- When active: Use `_activeIntervalSeconds` (default: 2s)
- When idle: Use base `IntervalSeconds` (e.g., 5s) or skip frames
- Timer interval adjusted dynamically based on activity

**Thread Safety**
- All activity time access uses `lock(_activityLock)`
- Hook callbacks are synchronous and fast
- Must call Start() to install hooks
- Must call Stop() to uninstall hooks (prevents leaks)

**Lifecycle**
- Created in MainForm constructor
- Started when capture begins (if SmartIntervalEnabled)
- Stopped when capture stops
- Disposed when MainForm disposes

**Must NOT**
- Modify capture state directly
- Update UI directly (fire events instead)
- Run without proper Start/Stop lifecycle
- Leak hooks (always uninstall on Stop/Dispose)

---

## System 6: Encoding Pipeline — FFmpeg

**Primary Files**
- `src/Video/FfmpegRunner.cs` - Process execution wrapper
- `src/Video/FfmpegDownloader.cs` - Auto-download functionality

**Role**
- Offline video encoding from captured frames
- Post-capture processing only
- FFmpeg binary management

**How It Works**
- Creates filelist.txt with frame paths
- Launches FFmpeg process with libx264 encoder
- Monitors progress via stderr parsing
- Writes output to session's output/ folder
- Cleans up temp files on completion

**FfmpegRunner**
- `FindFfmpeg()` - Locates FFmpeg binary (config → local → PATH)
- `RunFfmpegAsync()` - Executes FFmpeg with arguments
- `IsValidFfmpegExecutable()` - Validates binary with -version check
- Returns exit code, stdout, and stderr

**FfmpegDownloader**
- Downloads FFmpeg from gyan.dev (essentials build)
- Progress callback: (bytesDownloaded, totalBytes, status)
- Retry logic: 3 attempts, 2s delay between attempts
- Extracts to local ffmpeg/ folder
- Validates after extraction
- Cleans up nested folders after extract

**Encoding Parameters**
- Codec: libx264 (H.264)
- Quality: CRF 0-51 (lower = better, 18-23 typical)
- Preset: ultrafast / fast / medium / slow (speed/quality tradeoff)
- FPS: User-configured (default: 25fps)
- Format: MP4 container

**Output Naming**
- Pattern: `timelapse_YYYYMMDD_HHMMSS.mp4`
- Location: `captures/{session}/output/`

**Invariants**
- Encoding must never modify or delete frames
- Encoding failure must not corrupt session state
- FFmpeg presence must be validated before encoding
- Encoding runs on background thread (async)
- Only one encoding operation at a time

**Must NOT**
- Run during active capture (blocks UI)
- Interact with UI directly (use callbacks)
- Modify session state beyond status updates
- Delete frames on error
- Assume FFmpeg is installed

---

## System 7: Settings Management — SettingsManager

**Primary Files**
- `src/Core/SettingsManager.cs`
- `CaptureSettings` model class (in same file)

**Role**
- Persistent user preferences
- Application configuration
- JSON-based storage

**CaptureSettings Properties**
- `SaveFolder` - Root directory for captures
- `IntervalSeconds` - Base capture interval
- `Format` - Image format (JPEG/PNG)
- `JpegQuality` - JPEG compression (1-100)
- `Region` - **Nullable** capture region
- `FfmpegPath` - Path to FFmpeg executable
- `AspectRatioIndex` - Selected aspect ratio (0=Free, 1=16:9, etc.)
- `SmartIntervalEnabled` - Activity-based timing
- `ActiveIntervalSeconds` - Fast interval when active
- `IdleThresholdSeconds` - Time before considered idle
- `SkipIdleFrames` - Skip vs. slow down when idle

**Key Methods**
- `Load()` - Reads settings.json or returns defaults
- `Save()` - Writes settings.json

**Storage Location**
- File: `settings.json` in application directory
- Format: JSON with case-insensitive deserialization

**Debouncing (Issue #4 - FIXED)**
- `SaveSettings()` in MainForm uses 3-second timer
- Prevents disk spam from rapid UI changes
- `SaveSettingsImmediate()` bypasses debounce for critical operations:
  - Application close
  - FFmpeg path configuration
  - Capture start/stop

**Must NOT**
- Contain capture logic
- Contain session logic
- Validate settings (that's ValidationHelper's job)
- Interact with UI

---

## Supporting Systems

### UI Safety — UIHelper

**File:** `src/Utilities/UIHelper.cs`

**Role**
- Thread-safe UI updates from background threads
- Consistent dialog presentation

**Key Methods**
- `SafeSetText()` - Updates control text
- `SafeUpdateLabel()` - Updates label text
- `SafeSetEnabled()` - Enables/disables controls
- `SafeSetColor()` - Updates foreground color
- `SafeInvoke()` - Generic action wrapper
- `SafeBeginInvoke()` - Asynchronous action wrapper
- `ShowWarning/Error/Question/Info()` - Dialog helpers

**Thread Safety Pattern**
```csharp
if (control == null) return;
try {
    if (control.InvokeRequired) {
        control.Invoke(() => {
            if (!control.IsDisposed)
                control.Text = text;
        });
    } else {
        if (!control.IsDisposed)
            control.Text = text;
    }
} catch (ObjectDisposedException) { }
  catch (InvalidOperationException) { }
```

**Why This Matters**
- Capture runs on timer thread, not UI thread
- Checking `IsDisposed` requires handle access
- Must check `InvokeRequired` BEFORE `IsDisposed`
- Race conditions during disposal are expected and caught

**Critical Fix (Issue #8 - FIXED)**
- Old code checked IsDisposed before InvokeRequired → cross-thread exception
- New code checks InvokeRequired first, then IsDisposed on UI thread
- Exception handling for disposal race conditions

### Validation — ValidationHelper

**File:** `src/Utilities/ValidationHelper.cs`

**Role**
- Input validation
- State consistency checks
- Safety constraints

**Key Methods**
- `IsValidRegion()` - Checks dimensions are even (required for encoding)
- `CheckDiskSpace()` - Ensures minimum space available
- `BuildSettingsMismatchMessage()` - Detailed comparison for warnings

**Constants**
- `MIN_DISK_SPACE_MB` = 50
- Even width/height required for libx264

### Logging — Logger

**File:** `src/Core/Logger.cs`

**Role**
- Debug visibility
- State tracking
- Error diagnosis

**Key Methods**
- `Log(category, message)` - Standard log with timestamp
- `LogState(category, name, value)` - State dump helper

**Output**
- File: `debug.log` in application directory
- Format: `[YYYY-MM-DD HH:MM:SS.mmm] [Category] Message`

**Must NOT**
- Contain application logic
- Throw exceptions on log failure
- Modify state (read-only)

### System Monitor — SystemMonitor

**File:** `src/Utilities/SystemMonitor.cs`

**Role**
- Resource tracking
- Storage and memory monitoring

**Key Methods**
- `GetStorageInfoString()` - Current and projected disk usage
- Memory tracking for long capture sessions

### Constants — Configuration

**File:** `src/Core/Constants.cs`

**Role**
- Centralized configuration values
- Magic number elimination

**Key Constants**
- `MAX_CONSECUTIVE_ERRORS` = 3
- `DISK_SPACE_CHECK_INTERVAL` = 10 frames
- `UI_UPDATE_INTERVAL_MS` = 500ms
- Minimum disk space requirements

---

## Cross-Cutting Rules

### Threading Model

**UI Thread**
- Handles all user interaction
- Updates all controls
- Shows all dialogs
- Owns Form lifecycle

**Timer Thread** (System.Threading.Timer)
- Runs CaptureFrame() callback
- NO direct UI access
- All UI updates via UIHelper
- Synchronizes session access via lock

**Rule:** NEVER use async/await in capture path unless explicitly approved

### Data Ownership

| System | Owns | Persists Via |
|--------|------|--------------|
| MainForm | User intent, UI state | SettingsManager |
| Capture Engine | Timing, frame capture | SessionManager |
| SessionManager | Session metadata | session.json |
| SettingsManager | User preferences | settings.json |
| ActivityMonitor | Activity state | (not persisted) |
| FFmpeg | Video encoding | output files |

### State Synchronization

**Region State (CRITICAL)**
- Three locations must stay in sync
- ONLY use SetCurrentRegion/ClearCurrentRegion
- Never bypass synchronization methods
- Session takes priority in GetCurrentRegion()

**Session State**
- MainForm owns active session reference
- SessionManager owns persistence
- Capture engine reads session via lock
- UI reads session via MainForm methods

**Settings State**
- SettingsManager owns persistence
- MainForm owns runtime values
- Saves are debounced (3s delay)
- Critical saves use SaveSettingsImmediate()

### Change Discipline

**Single-System Changes (Preferred)**
- Touch only one system
- Clear ownership
- Minimal risk
- Easy to review

**Multi-System Changes (Requires Justification)**
- Must fix a bug that spans systems
- Must preserve all invariants
- Requires explicit approval
- Document in PROJECT_CONTEXT.md

**Refactoring (Forbidden Unless Bug-Driven)**
- No "cleanup" refactors
- No style changes
- No architectural redesigns
- Only refactor to fix specific bugs

---

## How to Use This File

**Before modifying code:**

1. **Identify which system owns the change**
   - Look at the system boundaries above
   - Find the file in the system list
   - Check the "Owns" section

2. **Confirm no invariant is violated**
   - Read the "Invariants" section for that system
   - Check the "Must NOT" list
   - Verify thread safety requirements

3. **Limit changes to that system**
   - Don't touch files from other systems
   - Use defined interfaces only
   - Preserve existing behavior

**If a change does not clearly belong to one system:**

**STOP** — You may be introducing architectural drift.

Consult PROJECT_CONTEXT.md and CLAUDE_WORKING_CONTRACT.md before proceeding.

---

## File Location Reference

### Core Application
```
src/
├── Program.cs                      # Entry point, DPI awareness
├── UI/
│   ├── MainForm.cs                 # System 1: UI Orchestrator
│   ├── MainForm.Designer.cs        # Generated UI code
│   ├── MainForm.ControlState.cs    # Guided mode & control logic
│   ├── ReadinessCheck.cs           # Prerequisites panel
│   ├── SessionNameDialog.cs        # Session creation dialog
│   ├── FfmpegDownloaderDemo.cs     # FFmpeg download UI
│   └── ActivityMonitorTestForm.cs  # Activity monitor test UI
├── Capture/
│   ├── RegionSelector.cs           # System 4: Region selection
│   ├── RegionOverlay.cs            # System 4: HUD overlay
│   ├── AspectRatio.cs              # System 4: Ratio calculations
│   ├── ActivityMonitor.cs          # System 5: Input monitoring
│   └── WindowSelector.cs           # Window capture (future)
├── Core/
│   ├── SessionManager.cs           # System 3: Session persistence
│   ├── SettingsManager.cs          # System 7: Settings persistence
│   ├── Logger.cs                   # Logging utility
│   └── Constants.cs                # Configuration constants
├── Video/
│   ├── FfmpegRunner.cs             # System 6: Encoding
│   └── FfmpegDownloader.cs         # System 6: Auto-download
└── Utilities/
    ├── UIHelper.cs                 # Thread-safe UI updates
    ├── ValidationHelper.cs         # Input validation
    ├── SystemMonitor.cs            # Resource monitoring
    └── PerformanceOptimizations.cs # Performance helpers
```

---

## System Interaction Matrix

| From ↓ / To → | MainForm | Capture | Session | Region | Activity | FFmpeg | Settings |
|---------------|----------|---------|---------|--------|----------|--------|----------|
| **MainForm** | - | Calls | Calls | Calls | Controls | Calls | Calls |
| **Capture** | Updates | - | Reads* | Reads | - | - | - |
| **Session** | Returns | - | - | - | - | - | - |
| **Region** | Returns | - | - | - | - | - | - |
| **Activity** | Events | - | - | - | - | - | - |
| **FFmpeg** | Callbacks | - | - | - | - | - | - |
| **Settings** | Returns | - | - | - | - | - | - |

\* = via lock(_captureLock)

**Legend:**
- **Calls** = Direct method invocation
- **Returns** = Returns data via method calls
- **Reads** = Reads state (with locking if needed)
- **Updates** = Modifies via UIHelper
- **Events** = Event-based notification
- **Controls** = Lifecycle management (start/stop)
- **Callbacks** = Async completion notification

---

## Status

This structural map reflects the project as of **January 2025**.

**Last Updated:** 2025-01-30

**It should only be updated when:**
- A new system is introduced
- Ownership boundaries change
- A major subsystem is removed or split
- Significant architectural changes occur

**Do not update for:**
- Bug fixes within a system
- Method additions/removals
- UI changes
- Performance optimizations

---

## Related Documentation

- **CLAUDE_WORKING_CONTRACT.md** - Rules for AI-assisted development
- **PROJECT.md** - High-level features and architecture
- **PROJECT_CONTEXT.md** - Current state and known issues
- **docs/development/** - Active development notes
