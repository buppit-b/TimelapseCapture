# CLAUDE.md — TimelapseCapture

**Read this file fully before doing anything else. Don't read other docs unless this file points you there or something here looks wrong when you check it against the code.**

This file replaces what used to be five separate "read me first" docs
(`CLAUDE_WORKING_CONTRACT.md`, `PROJECT_CONTEXT.md`, `WORKING_WITH_CLAUDE.md`,
`docs/README.md`, and parts of `STRUCTURAL_MAP.md`/`PROJECT.md`). Those are
either archived (`docs/archive/`) or kept as deep-reference only — see
"Where to look for more" at the bottom.

---

## What this is

Desktop C# WinForms app (.NET 9) that captures screen frames on a timer and
encodes them into timelapse videos via FFmpeg. Built for digital art /
long-running unattended capture. Optimized for power users, not hand-holding.

---

## Rules (non-negotiable)

This project has previously suffered from over-refactoring, lost architectural
intent, state desync bugs, and async/timer misuse. Your job is to fix bugs and
reduce complexity *only when necessary* — not to redesign.

❌ Don't: add features, refactor for style, add abstractions/dependencies,
introduce async/await in the capture path, touch multiple systems in one
change, or bypass the region-sync methods below.

✅ Do: make the smallest change that fixes the bug, preserve method
signatures, ask before anything that feels architectural, and treat any doc
claim about the code as a hypothesis to verify with a quick grep, not a fact
to trust blindly (docs here have gone stale before — see Issue Log).

**If a change touches more than one system, or you're tempted to refactor —
stop and ask.**

---

## Critical invariants

### 1. Region synchronization (MOST IMPORTANT)

Region lives in 3 places and must stay consistent:
1. `captureRegion` — runtime field, `MainForm.cs`
2. `_activeSession.CaptureRegion` — in `SessionInfo`
3. `settings.Region` — in `CaptureSettings`

**Only mutate runtime region state via:**
`SetCurrentRegion(Rectangle)`, `ClearCurrentRegion()`, `GetCurrentRegion()`,
`SetCaptureRegionFromNullable(Rectangle?)` — all in `MainForm.cs` (~line 105).

**Known, verified-safe exceptions (do NOT "fix" these):**
`SaveSettings()`, `SaveSettingsImmediate()`, and `StopCapture()` each contain
a line like `settings.Region = captureRegion;` or
`_activeSession.CaptureRegion = captureRegion.Value;`. These are one-directional
mirrors of the canonical `captureRegion` field into a persisted copy,
immediately before writing to disk — they never originate a new value, so
they can't cause desync. The one in `StopCapture()` is a deliberate, commented
fix for a real prior bug ("Region: Not set" on restart). **Do not route these
through `SetCurrentRegion()`** — it calls `SaveSettings()` internally, so
calling it *from* `SaveSettings()` would recurse.

**Note:** `GetCurrentRegion()` is defined but has zero call sites in
`MainForm.cs` as of 2026-06-22. Verify where region is actually *read* from
before assuming this method is in the live path.

### 2. Threading

- Capture runs on `System.Threading.Timer` (NOT the UI thread).
- All timer→UI updates MUST go through `UIHelper.SafeX()` methods.
- Session access during capture MUST be inside `lock(_captureLock)`.
- No fire-and-forget tasks. No async/await in the capture path without
  explicit approval.
- `UIHelper` thread-safety pattern: check `InvokeRequired` **before**
  `IsDisposed` (checking `IsDisposed` off the UI thread throws). See
  `docs/archive/BUGFIX_CROSS_THREAD_UI.md` for the bug this caused.

### 3. Sessions

- Session files are user data. **Never delete automatically.**
- `FramesCaptured > 0 && CaptureRegion == null` is a known recoverable
  corruption state — use `ValidateAndRepairSession()`, don't hand-roll a fix.
- `Active = true` for at most one session at a time.

---

## File map (verified 2026-06-22 — re-check if it's been a while)

```
src/
├── Program.cs                      DPI awareness, entry point
├── UI/
│   ├── MainForm.cs                 ~3800 lines — UI orchestrator (System 1)
│   ├── MainForm.Designer.cs        generated
│   ├── MainForm.ControlState.cs    guided mode / control state
│   ├── MainForm.Menu.cs            menu bar (added in UI reorg Phase 1)
│   ├── SessionSetupForm.cs         setup wizard (added in UI reorg Phase 1)
│   ├── ReadinessCheck.cs, SessionNameDialog.cs, FfmpegDownloaderDemo.cs,
│   │   ActivityMonitorTestForm.cs
│   └── ControlStateManager.cs / ControlStateManager/
├── Capture/
│   ├── RegionSelector.cs, RegionOverlay.cs, AspectRatio.cs
│   ├── ActivityMonitor.cs          smart-interval input hooks
│   └── WindowSelector.cs           (future / partial)
├── Core/
│   ├── SessionManager.cs, SettingsManager.cs, Logger.cs, Constants.cs,
│   │   UIState.cs
├── Video/
│   └── FfmpegRunner.cs, FfmpegDownloader.cs
└── Utilities/
    └── UIHelper.cs, ValidationHelper.cs, SystemMonitor.cs,
        PerformanceOptimizations.cs
```

For *system ownership* detail (what each file is/isn't allowed to touch),
see `docs/STRUCTURAL_MAP.md` — read that only when working a bug that isn't
obviously confined to one file.

---

## Current status (last verified 2026-06-22)

**Build status:** not verified this session — run `dotnet build` before
trusting this.

**Test coverage — thin, this is a real gap:**
`TimelapseCapture.Tests/BasicTests.cs` has 6 tests covering only
`SessionManager` and `ValidationHelper`. **Not covered at all:** Capture
Engine, region-sync invariant, `ActivityMonitor`, FFmpeg pipeline. If you're
asked to improve test coverage, these are the priority gaps, not more
`SessionManager` tests.

**UI rework — already partially planned and started:**
Phase 1 done in code: menu bar (`MainForm.Menu.cs`) + session setup wizard
(`SessionSetupForm.cs`). Phases 2–4 (compact session bar, collapsible Smart
Interval panel, encoding-settings dialog) are planned but not implemented —
see `docs/development/claude/UI_WORKFLOW_REORGANIZATION.md` for the existing
plan and mockups before proposing a new one.

**Known debris cleaned up this session (2026-06-22):**
- Moved 4 superseded "read first" docs and 3 completed/duplicate dev-notes to
  `docs/archive/`.
- Moved a stray `DELETE_THIS_FILE.txt` placeholder and 3 dead, never-executed
  cleanup `.ps1` scripts to `_DELETE_ME/` at the project root — safe to
  delete that folder; nothing in it is referenced anywhere.
- No code was changed this session.

---

## Issue log (carried forward — add new entries at the top)

#### Issue #9: Five overlapping "read first" docs slowed every session start (FIXED — 2026-06-22)
- **Problem**: Getting up to speed required reading 5+ files with duplicated/contradictory content, plus verifying stale claims (e.g. MainForm.cs line count was off by ~150-200 lines across two docs).
- **Solution**: Consolidated into this single file. Old docs archived, not deleted (history preserved).
- **Result**: One file to read at session start; everything else is opt-in reference.

#### Issue #8: Build errors — duplicate methods & missing property (FIXED — 2025-01-06)
- Removed duplicate Guided Mode region from `MainForm.cs`; added `IsCapturing` property.

#### Issue #7: Capture error counter persisted across sessions (FIXED)
- Reset on capture start, new session, folder change.

#### Issue #4: Settings disk spam (FIXED)
- Debounced via 3s timer; `SaveSettingsImmediate()` added for critical ops.

#### Issue #3: Region state desync (FIXED — see invariant #1 above for current exceptions)
- Centralized through `SetCurrentRegion()`/`ClearCurrentRegion()`.

(Full historical detail on these and many resolved issues: `docs/archive/`
and `docs/development/claude/ignore/` — only open those if you need the
*why*, not the *what*.)

---

## Where to look for more (optional — only if sent here)

- `docs/STRUCTURAL_MAP.md` — per-system ownership, "must not" lists, thread
  model detail. Read when a bug doesn't obviously belong to one file.
- `docs/PROJECT.md` — feature roadmap, `settings.json`/`session.json` schema
  examples, output folder structure. Read when you need config file shapes.
- `docs/development/claude/` — UI reorg plan (still open, see above).
- `docs/archive/` — completed features, fixed bugs, superseded docs. Historical
  reference only; don't treat as current state.
- `docs/development/claude/ignore/` — old session-by-session logs, 60+ files.
  Genuinely historical; only dig in if you need to understand *why* a past
  decision was made.

---

**Last updated:** 2026-06-22
**Maintainer:** Claude + Spike
