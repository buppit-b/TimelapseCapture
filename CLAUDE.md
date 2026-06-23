# CLAUDE.md — TimelapseCapture

Read this first — it's the single source of truth for working in this repo.
Treat any **specific** claim here (line numbers, call sites, status) as a
hypothesis to confirm against the code before you rely on it. This file has
gone stale before (it once claimed `GetCurrentRegion()` had zero call sites
when it didn't), so the standing rule is: **verify, then act.**

---

## What this is

Desktop C# WinForms app (.NET 9) that captures screen frames on a timer and
encodes them into timelapse videos via FFmpeg. Built for digital art and
long-running, often unattended capture. Power-user oriented, not hand-holdy.

- Repo: https://github.com/buppit-b/TimelapseCapture (default branch `main`)
- Build/run: `dotnet build` then `dotnet run` (Windows only).

---

## How to work here

This is a small, single-maintainer app that has been burned twice: by large
speculative rewrites that lost architectural intent, and by threading/async
mistakes in the capture loop. So the working bar is simple:

> **Understand the system you're touching, make a focused change, and verify it
> builds and runs.**

Within that bar, improving and simplifying code is welcome — you do **not** have
to preserve bad code just because it exists. Best-practice defaults:

- **Verify before you trust.** Grep/read to confirm a claim (including claims in
  this file) before acting on it.
- **Keep the build green.** `dotnet build` must stay at 0 errors. Run the app
  when you change capture, encoding, or UI behavior — a green build is necessary
  but not sufficient.
- **Respect the invariants below.** They are real (each came from a shipped bug),
  not style preferences.
- **Prefer focused changes,** but fixing an adjacent issue or simplifying nearby
  code is fine when you genuinely understand the impact. For a true architectural
  shift (new threading model, replacing the capture engine, restructuring
  sessions/settings), align on the approach first rather than doing it silently.
- **Don't add dependencies** without a clear reason — the app is intentionally lean.
- **Leave the tree better:** small, well-described commits; no dead scaffolding
  (don't call methods that don't exist yet — that's what broke the build before).

---

## Critical invariants (these are real — don't "clean them up")

### 1. Region synchronization

The capture region lives in **3 places** that must stay consistent:
1. `captureRegion` — runtime field, `MainForm.cs`
2. `_activeSession.CaptureRegion` — in `SessionInfo`
3. `settings.Region` — in `CaptureSettings`

**Mutate runtime region state only via** (defined in `MainForm.cs`, ~line 80–105):
`SetCurrentRegion(Rectangle)`, `ClearCurrentRegion()`, `GetCurrentRegion()`,
`SetCaptureRegionFromNullable(Rectangle?)`.

`GetCurrentRegion()` (MainForm.cs:81) **is** used to read region with session
priority — e.g. `MainForm.Menu.cs` `UpdateMenuStates()`. (Earlier docs wrongly
said it was unused.)

**Known, verified-safe exceptions — do NOT route these through the setters:**
`SaveSettings()`, `SaveSettingsImmediate()`, and `StopCapture()` each contain a
one-directional mirror (`settings.Region = captureRegion;` or
`_activeSession.CaptureRegion = captureRegion.Value;`) immediately before
persisting. They copy the canonical `captureRegion` into a to-be-saved object;
they never originate a new value, so they can't desync. The one in
`StopCapture()` is a deliberate fix for a real "Region: Not set on restart" bug.
Routing them through `SetCurrentRegion()` would recurse (it calls
`SaveSettings()` internally).

### 2. Threading & the capture path

- Capture runs on a `System.Threading.Timer` (NOT the UI thread).
- All timer→UI updates MUST go through `UIHelper.SafeX()` methods.
- Session/shared-state access during capture MUST be inside `lock(_captureLock)`.
- **No async/await in the capture path, and no fire-and-forget tasks.** This is a
  hard constraint, not a preference — the loop's correctness depends on its
  synchronous, locked structure.
- `UIHelper` must check `InvokeRequired` **before** `IsDisposed` (checking
  `IsDisposed` off the UI thread throws). See
  `docs/archive/BUGFIX_CROSS_THREAD_UI.md`.
- **Persistence on the capture path must be crash-safe and must not throw into
  the loop.** `SessionManager.SaveSession()` now writes atomically (temp file +
  swap) and swallows/logs IO errors — keep it that way; an IO hiccup must not
  trip the capture error counter or corrupt `session.json`.

### 3. Sessions

- Session files are user data. **Never delete automatically.**
- `FramesCaptured > 0 && CaptureRegion == null` is a known recoverable
  corruption state — use `ValidateAndRepairSession()`, don't hand-roll a fix.
- At most one session has `Active = true` at a time.

---

## File map (re-verify if it's been a while)

```
src/
├── Program.cs                      DPI awareness, entry point
├── UI/
│   ├── MainForm.cs                 ~3800 lines — UI orchestrator
│   ├── MainForm.Designer.cs        generated
│   ├── MainForm.ControlState.cs    guided mode / control enable-disable
│   ├── MainForm.Menu.cs            menu bar (UI reorg Phase 1)
│   ├── SessionSetupForm.cs         setup wizard (UI reorg Phase 1)
│   ├── ReadinessCheck.cs, SessionNameDialog.cs, ControlStateManager.cs,
│   │   FfmpegDownloaderDemo.cs, ActivityMonitorTestForm.cs
├── Capture/
│   ├── RegionSelector.cs, RegionOverlay.cs, AspectRatio.cs
│   ├── ActivityMonitor.cs          smart-interval input hooks
│   └── WindowSelector.cs           (partial / future)
├── Core/
│   └── SessionManager.cs, SettingsManager.cs, Logger.cs, Constants.cs, UIState.cs
├── Video/
│   └── FfmpegRunner.cs, FfmpegDownloader.cs
└── Utilities/
    └── UIHelper.cs, ValidationHelper.cs, SystemMonitor.cs, PerformanceOptimizations.cs
```

`docs/STRUCTURAL_MAP.md` has per-system ownership detail — read it when a bug
doesn't obviously belong to one file.

---

## Current status (verified 2026-06-23)

- **Build:** PASSES — 0 errors, ~24 warnings. Warnings are mostly nullable-ref
  (`CS8602`/`CS8618`) in `SessionSetupForm.cs` and `WindowSelector.cs`; low
  priority but worth tidying when touching those files.
- **Recently done (this session):**
  - Fixed a build break — the constructor called two never-implemented methods
    (`InitializeCollapsiblePanels`, `RefreshUIState`); removed the dead calls.
  - Committed previously-**untracked** Phase 1 UI work (menu bar, setup wizard,
    `UIState` enum) that was at risk of being lost.
  - Fixed 3 reviewed bugs: atomic writes for `settings.json` and `session.json`
    (no more corruption on crash/power-loss), and `ffmpeg -version` validation
    no longer deadlocks/false-rejects a valid ffmpeg.
  - Re-ran the full review and fixed 19 confirmed findings: startup-crash guards
    for corrupt settings/session files, session-folder/active-session integrity,
    a close-while-capturing data race, a capture-thread UI-control read, a wizard
    download crash, atomic ffmpeg output handling, bounded logging, and several
    leaks. Build green, 8/8 tests pass.
- **Tests — thin (real gap):** `TimelapseCapture.Tests/BasicTests.cs` covers only
  `SessionManager` + `ValidationHelper`. **Not covered:** capture engine,
  region-sync invariant, `ActivityMonitor`, FFmpeg pipeline. Prioritize those.
- **Open / next:**
  - The full multi-agent review is complete (21 confirmed findings) and **all 21
    are addressed** — including an encode Cancel button (ffmpeg encodes can now be
    stopped) and a confirmation before "Download FFmpeg" overrides a custom path.
  - UI rework Phases 2–4 (compact session bar, collapsible Smart Interval panel,
    encoding-settings dialog) are planned, not built — see
    `docs/development/claude/UI_WORKFLOW_REORGANIZATION.md` before proposing a
    new plan.

---

## Issue log (newest first)

#### Full code review — 21 findings, all addressed (2026-06-23)
- A verified multi-agent review found 21 real issues; all addressed: crashes on
  bad settings/session files, a close-time `session.json` race, a capture-thread
  control read, a wizard download crash, ffmpeg/logging/leak hardening, plus an
  encode Cancel button and a custom-ffmpeg-path override warning.

#### Build break from dead UI scaffolding (FIXED — 2026-06-23)
- Constructor called `InitializeCollapsiblePanels()` / `RefreshUIState()` which
  were never implemented → `CS0103`. Removed the calls (behavior-neutral;
  `UpdateControlStates()` and `CheckAndShowWizard()` already cover the work).

#### Non-atomic persistence could corrupt settings/sessions (FIXED — 2026-06-23)
- `settings.json` and `session.json` were written with in-place truncate-then-write.
  A crash mid-write left them empty; `Load`/`LoadSession` then silently reset to
  defaults / returned null. Now temp-file + atomic swap.

#### ffmpeg validation could deadlock (FIXED — 2026-06-23)
- `IsValidFfmpegExecutable` redirected stdout/stderr but never drained them;
  `ffmpeg -version`'s multi-KB output could fill the pipe buffer and hang. Dropped
  the unused redirects and guarded `ExitCode` behind the `WaitForExit` result.

#### Five overlapping "read first" docs slowed every start (FIXED — 2026-06-22)
- Consolidated into this file; superseded docs archived under `docs/archive/`.

#### Region state desync (FIXED — see invariant #1)
- Centralized through the region setter methods; remaining mirror lines are the
  documented safe exceptions.

(Older/full history: `docs/archive/` and `docs/development/claude/ignore/`.)

---

## Where to look for more (only if pointed here)

- `docs/STRUCTURAL_MAP.md` — per-system ownership, thread-model detail.
- `docs/PROJECT.md` — roadmap, `settings.json`/`session.json` schemas, output
  folder layout.
- `docs/development/claude/` — UI reorg plan (still open).
- `docs/archive/` — completed features, fixed bugs, superseded docs (historical).

---

**Last updated:** 2026-06-23 · **Maintainer:** Spike (+ Claude)
