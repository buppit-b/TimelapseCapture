# TimelapseCapture - Project Context

## Purpose of This File

This document serves as the **primary reference** for understanding the current state of the TimelapseCapture project. It contains:

- **Current project status** - What works, what's broken, what's in progress
- **Known issues and their fixes** - Complete bug fix history with solutions
- **Architecture overview** - How the code is organized and why
- **Critical code patterns** - DO's and DON'Ts for safe development
- **Build and testing procedures** - How to compile and verify changes

**When to update this file:**
- After fixing bugs (add to Issue Log)
- After major refactoring (update Architecture)
- After adding features (update Working Features)
- When build process changes (update Compilation section)
- When critical patterns change (update Code Patterns)

**Related Documentation:**
- `STRUCTURAL_MAP.md` - System boundaries and architectural structure (READ THIS FIRST for code changes)
- `PROJECT.md` - High-level project overview, features, and goals
- `CLAUDE_WORKING_CONTRACT.md` - Rules and guidelines for development
- `docs/development/` - Active development notes and session logs
- `docs/archive/` - Completed features and historical documentation

---

## Documentation Organization

### docs/ Structure

```
docs/
├── STRUCTURAL_MAP.md          # System boundaries, ownership (READ FIRST before code changes)
├── PROJECT.md                 # High-level project overview (features, architecture)
├── PROJECT_CONTEXT.md         # This file - current state & working patterns
├── CLAUDE_WORKING_CONTRACT.md # Development rules and guidelines
├── README.md                  # Documentation workflow
├── archive/                   # Completed/obsolete documentation
│   ├── CLEANUP_WINDOW_FEATURE.txt
│   ├── CONTROL_STATE_INTEGRATION.md
│   ├── GUIDED_MODE_INTEGRATION.md
│   └── [other completed features]
└── development/               # Active development documentation
    └── claude/                # Claude AI session notes
        ├── BUGFIX_*.md        # Active bug investigation notes
        ├── fix needed.txt     # Current issues being worked on
        └── ignore/            # Old session logs (kept for history)
```

### Where to Put New Documents

#### 🟢 Active Work (docs/development/claude/)
Put documents here when:
- Investigating a bug or issue
- Planning a new feature
- Documenting a work-in-progress session
- Need quick reference during development

**Examples:**
- `BUGFIX_MEMORY_LEAK_2025-01-07.md`
- `FEATURE_WEBCAM_INTEGRATION.md`
- `SESSION_2025-01-08_UI_REDESIGN.md`

**Naming Convention:**
- Use ALL_CAPS with underscores
- Include date for bug fixes and sessions: `YYYY-MM-DD`
- Start with type: `BUGFIX_`, `FEATURE_`, `SESSION_`, `PLAN_`

#### 🔵 Completed Work (docs/archive/)
Move documents here when:
- Feature is complete and merged
- Bug is fixed and verified
- Documentation is outdated/obsolete
- No longer needed for active reference

**Before archiving:**
1. Update PROJECT_CONTEXT.md with the outcome
2. Add entry to Issue Log if it's a bug fix
3. Update Working Features if it's a feature
4. Add brief summary comment at top of archived file

#### 🔴 Historical Sessions (docs/development/claude/ignore/)
Move here when:
- Session notes are more than 2 weeks old
- Issue is resolved and documented elsewhere
- Keeping for historical reference only

**Don't delete** - these provide context for why decisions were made.

### Document Lifecycle

```
1. CREATE → docs/development/claude/BUGFIX_*.md
   (Active investigation)
   ↓
2. UPDATE → docs/PROJECT_CONTEXT.md
   (Document the fix in Issue Log)
   ↓
3. MOVE → docs/archive/BUGFIX_*.md
   (Feature complete, add summary)
   ↓
4. MOVE → docs/development/claude/ignore/
   (After 2+ weeks, keep for history)
```

### Cleanup Guidelines

**Monthly Review (1st of each month):**
1. Check `docs/development/claude/` for documents older than 2 weeks
2. Move completed work to `docs/archive/`
3. Move old session logs to `ignore/`
4. Update PROJECT_CONTEXT.md with any missing info

**Keep docs/ Clean:**
- ✅ Root docs/ should only have PROJECT.md and PROJECT_CONTEXT.md
- ✅ Active development stays in development/claude/
- ✅ Completed work goes to archive/
- ❌ Don't create random .txt files in root
- ❌ Don't leave old session logs in active area

---

## Current Status (Last Updated: 2025-01-06)

### Working Features
✅ Region selection with aspect ratio locking
✅ Session management (create, load, save)  
✅ Screen capture with multi-monitor support
✅ FFmpeg video encoding
✅ Smart interval capture (activity-based)
✅ Real-time capture statistics
✅ Guided mode (progressive UI disclosure)

### Known Issues Being Tracked
None currently - all major bugs fixed!

**Recent Fix (2025-01-06)**: Build errors resolved - duplicate methods removed, IsCapturing property added.

### Recent Bug Fixes (Issue Log)

#### Issue #3: Region State Desynchronization (FIXED)
- **Problem**: Region stored in 3 places (captureRegion, _activeSession.CaptureRegion, settings.Region) caused desyncs
- **Solution**: Centralized region management through `SetCurrentRegion()` and `ClearCurrentRegion()`
- **Changed**: All region assignments now use these methods
- **Changed**: Region is now `Rectangle?` (nullable) instead of using `Rectangle.Empty`

#### Issue #4: Settings Disk Spam (FIXED)
- **Problem**: SaveSettings() called on every UI change = excessive disk I/O
- **Solution**: Debounced saves using timer (3 second delay)
- **Added**: `SaveSettingsImmediate()` for critical operations (app close, FFmpeg config, capture start)

#### Issue #7: Capture Error Counter Reset (FIXED)
- **Problem**: Error counter persisted across sessions causing premature stops
- **Solution**: Reset counter on: capture start, new session, folder change

#### Issue #8: Build Errors - Duplicate Methods & Missing Property (FIXED - 2025-01-06)
- **Problem**: CS0121 ambiguous call errors for UpdateGuidedModeUI(), EnableAllControls(), SetControlTooltip(), ClearAllTooltips()
- **Root Cause**: Methods defined in BOTH MainForm.cs and MainForm.ControlState.cs
- **Solution**: Removed duplicate Guided Mode region from MainForm.cs (lines 3710-3911)
- **Problem**: CS0103 errors for undefined 'IsCapturing' (referenced 19 times)
- **Solution**: Added `private bool IsCapturing => _captureTimer != null;` property
- **Result**: Project now builds successfully with 0 errors

### Architecture Overview

#### State Management
- **Runtime State**: `captureRegion` (nullable Rectangle)
- **Session State**: `_activeSession.CaptureRegion` (nullable Rectangle)  
- **Persistent State**: `settings.Region` (nullable Rectangle)
- **Synchronization**: ONLY modify via `SetCurrentRegion()` or `ClearCurrentRegion()`

#### Thread Safety
- **Capture Thread**: Timer-based, uses `lock(_captureLock)` for session access
- **UI Thread**: All UI updates via `UIHelper.SafeX()` methods
- **Critical Sections**: Session load/save, region changes, capture start/stop

#### File Structure
```
TimelapseCapture/
├── src/
│   ├── UI/
│   │   ├── MainForm.cs (3850 lines - main application logic)
│   │   ├── MainForm.Designer.cs (UI definitions)
│   │   ├── RegionSelector.cs (region selection overlay)
│   │   ├── RegionOverlay.cs (capture region indicator)
│   │   └── SessionNameDialog.cs (session creation)
│   ├── Core/
│   │   ├── SessionManager.cs (session persistence)
│   │   ├── SettingsManager.cs (settings persistence)
│   │   ├── FfmpegRunner.cs (video encoding)
│   │   ├── ActivityMonitor.cs (smart intervals)
│   │   └── Logger.cs (debug logging)
│   └── Helpers/
│       ├── Constants.cs (magic numbers)
│       ├── ValidationHelper.cs (validation logic)
│       └── UIHelper.cs (thread-safe UI updates)
```

### Critical Code Patterns

#### Region Management (DO THIS)
```csharp
// ✅ CORRECT - Use centralized methods
SetCurrentRegion(newRegion);
ClearCurrentRegion();

// ❌ WRONG - Don't bypass synchronization
captureRegion = newRegion; // DON'T DO THIS
_activeSession.CaptureRegion = newRegion; // DON'T DO THIS
```

#### Settings Persistence (DO THIS)
```csharp
// ✅ Normal case - debounced save
SaveSettings();

// ✅ Critical operations - immediate save
SaveSettingsImmediate(); // For: app close, FFmpeg config, capture start
```

#### Thread-Safe UI Updates (DO THIS)
```csharp
// ✅ CORRECT - Thread-safe
UIHelper.SafeSetText(lblStatus, "New text");

// ❌ WRONG - Not thread-safe from timer
lblStatus.Text = "New text"; // DON'T DO THIS from capture thread
```

### MainForm.cs Structure (Current)

#### Regions & Line Numbers (Approximate)
1. **Fields** (Lines 20-80)
2. **Initialization** (Lines 82-600)
3. **Settings Management** (Lines 602-950)
4. **Region Overlay** (Lines 952-1050)
5. **UI Event Handlers** (Lines 1052-1750)
6. **Capture Control** (Lines 1752-2400)
7. **Display Updates** (Lines 2402-2850)
8. **FFmpeg & Encoding** (Lines 2852-3400)
9. **Error Handling & Safety** (Lines 3402-3450)
10. **Form Lifecycle** (Lines 3452-3500)
11. **Guided Mode** (Lines 3502-3850)

#### Key Methods Reference
- **SetCurrentRegion()**: Line ~105 - Centralized region setter
- **ClearCurrentRegion()**: Line ~160 - Centralized region clearer
- **CaptureFrame()**: Line ~1850 - Timer callback for captures
- **UpdateGuidedModeUI()**: Line ~3710 - Progressive disclosure logic
- **SaveSettings()**: Line ~750 - Debounced settings save
- **SaveSettingsImmediate()**: Line ~800 - Non-debounced save

### Dependencies
- .NET 8.0+ (C# 12)
- Windows Forms
- FFmpeg (external, auto-downloaded)

### Compilation
```bash
cd C:\Users\Spike\source\TimelapseCapture
dotnet build
# or open in Visual Studio
```

### Testing Checklist
When making changes, verify:
- [ ] Capture starts without errors
- [ ] Region persists across app restart
- [ ] Session resume works after close
- [ ] Multi-monitor capture works
- [ ] Smart interval adjusts properly
- [ ] Video encoding completes
- [ ] No CS1028 compilation errors

### Future Enhancement Ideas
- [ ] Pause/resume capture
- [ ] Keyboard shortcuts for region selection
- [ ] Preset region sizes (1080p, 4K, etc.)
- [ ] Audio capture sync
- [ ] Cloud backup of sessions
- [ ] Webcam picture-in-picture

---
**Last Verified Working**: 2025-01-06  
**Build Status**: ✅ Compiling successfully (0 errors)  
**Primary Maintainer**: Claude + Spike  
**Report Issues**: Create GitHub issue or continue Claude chat

---

## Maintaining This File

### Quick Update Checklist

When you fix a bug:
- [ ] Add new entry to "Recent Bug Fixes (Issue Log)" section
- [ ] Update "Last Updated" date at top
- [ ] Archive the bug investigation document to `docs/archive/`
- [ ] Update "Build Status" if it was a compilation issue

When you add a feature:
- [ ] Add checkmark to "Working Features" section
- [ ] Update "Last Updated" date
- [ ] Update "MainForm.cs Structure" if code organization changed
- [ ] Move feature planning docs to `docs/archive/`

When you refactor:
- [ ] Update "Architecture Overview" if structure changed
- [ ] Update "Critical Code Patterns" if patterns changed
- [ ] Update "Key Methods Reference" if important methods moved
- [ ] Update "Last Updated" date

### Issue Log Format

When adding bug fixes, use this template:

```markdown
#### Issue #X: Brief Title (FIXED - YYYY-MM-DD)
- **Problem**: What was broken and why
- **Root Cause**: Technical explanation (optional)
- **Solution**: How it was fixed
- **Result**: Verification that it works
- **Code Location**: File and line numbers (optional)
```

### This File vs PROJECT.md

**PROJECT_CONTEXT.md** (this file):
- Current state and working patterns
- What's currently broken or in progress
- Recent bug fixes and solutions
- How to work with the code TODAY
- Updated frequently (after each fix)

**PROJECT.md**:
- High-level architecture and design
- Complete feature list and roadmap
- Configuration and output structure
- Long-term project vision
- Updated occasionally (major milestones)

**Rule of thumb**: If it changes weekly, it goes in PROJECT_CONTEXT.md. If it's stable for months, it goes in PROJECT.md.
