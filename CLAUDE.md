# CLAUDE.md — TimelapseCapture

Read this first — it's the single source of truth for working in this repo.
Treat any **specific** claim here (line numbers, signatures, status) as a
hypothesis to confirm against the code before you rely on it. The standing rule
is: **verify, then act.**

---

## What this is

A Windows desktop app that captures screen frames on a timer and encodes them
into timelapse videos via FFmpeg. Built for digital art and long-running, often
unattended capture. Power-user oriented, not hand-holdy.

**The app is display-named "FrameWrite"** (Spike settled on it 2026-07-10, "for now"; it was
"Framewright" before — an external tester suggested the FrameWrite spelling and Spike agreed).
All display branding, the data dir (`%APPDATA%\FrameWrite`), and the release zip use FrameWrite;
the **mechanical rename** (projects/exe/namespaces still `TimelapseCapture*`) stays deferred to
the 1.0 cut in case the name shifts again. Credits (Settings footer): created and directed by
Spike Tickner · engineered with Claude (Anthropic) · video by FFmpeg.

**The app is mid-migration from WinForms to a WPF rebuild. WPF is the active
front-end.** The two front-ends share one engine:

- **`TimelapseCapture.Wpf`** — the **active** app. WPF + MVVM, clean dark theme,
  terminal/green accent. This is what we develop now.
- **`TimelapseCapture.Core`** — UI-framework-agnostic shared library: capture
  engine, sessions, settings, ffmpeg, system stats. Both front-ends use it.
- **`TimelapseCapture` (root `src/`)** — the **legacy WinForms app**. Still in the
  solution and still builds, kept for reference/parity-checking. Don't invest in
  it; port anything missing into the WPF app instead. (It carries its own private
  copies of the Core classes under `src/Core`, `src/Capture`, etc. — that's why
  the two projects don't collide.)
- **`TimelapseCapture.Tests`** — 68 tests, cover `SessionManager` (incl. `CullAndRenumber`,
  `FindSessionRoot`), `OverlayRenderer.ResolveTokens`, `WindowEnumerator.CoversArea`,
  `AppPaths.ResolveDataDir` (portable vs %APPDATA%),
  `ValidationHelper`, `ScreenHelper` (region-relocate geometry), `WindowEnumerator` (filtering +
  dead handle), the window-tracking scale-rect math (`CaptureEngine.ComputeScaledDest`), and the
  output-name sanitiser (`VideoEncoder.SanitizeFileName`). Core exposes internals to the test
  project via `InternalsVisibleTo` — extract pure logic to `internal static` and cover it.

- Repo: https://github.com/buppit-b/TimelapseCapture (default branch `main`)
- **Build:** `dotnet build TimelapseCapture.sln`
- **Run the WPF app:** `dotnet run --project TimelapseCapture.Wpf`
  (or launch `TimelapseCapture.Wpf/bin/Debug/net9.0-windows/TimelapseCapture.Wpf.exe`)
- **Test:** `dotnet test TimelapseCapture.sln`
- Windows only (.NET 9, `net9.0-windows`).
- **Version:** `0.9.4` — **the 1.0 release candidate** (SemVer; `<Version>` in both `.csproj`,
  shown in the Settings cog). 1.0 = this RC + a passing multi-hour soak test (protocol in
  `ROADMAP.md` "1.0 gate"). See `ROADMAP.md` (also: 1.1 candidates + pre-distribution blockers)
  and `CHANGELOG.md`; bump the version + add a CHANGELOG entry per release.

> **Testing note:** computer-use/automation **cannot drive the dev-built exe**
> (the resolver won't target it). The maintainer (Spike) runs each build by hand
> and sends screenshots. So: build green + tests green, then **commit, push, and
> relaunch the exe** for Spike to verify. Don't block waiting to screenshot it
> yourself.

> **Git:** Spike is git-averse and has asked Claude to **own git end-to-end** —
> commit/push without being asked each time, but protect against loss (tag before
> anything destructive). Single `main` branch; commit per feature with a clear
> message; push after each. `gh` CLI is **not** installed — use plain `git`.

---

## How to work here

Small, single-maintainer app. The working bar:

> **Understand the system you're touching, make a focused change, and verify it
> builds and runs.**

- **Verify before you trust** (including claims in this file).
- **Keep the build green** — `dotnet build` at 0 errors AND 0 warnings (the legacy `src/` project
  suppresses its pre-nullable noise), `dotnet test` at 68/68.
- **Respect the invariants below** — each came from a shipped bug.
- Improving/simplifying nearby code is welcome; for a true architectural shift,
  align on the approach first.
- **Don't add dependencies** without a clear reason — the app is intentionally lean.
- **Aesthetics + UX now matter** (Spike drives this actively): clean dark + terminal vibe,
  themed controls (scrollbars/checkboxes), live theme switching. Function still comes first,
  but polish is in scope — Spike often asks for my UX best-practice input on a change.
- **Think for the project, not just the ticket.** Ask what's best for the app overall,
  **recommend** rather than just execute, and **pivot** when a better approach appears.
  **Proactively flag what's missing or risky** — a safety/coverage/efficiency gap nobody asked
  about is as important to raise as the requested feature. Spike explicitly values this.
- **Preference options vs safety defaults.** *Opt-in (default off)* for taste/behaviour some users
  want and others don't — make it an additive `CaptureSettings` field. But *default **on** (still
  configurable)* for **safety / data-integrity** behaviour: a user who never opens Settings should
  still be protected (low-disk auto-stop, capture-failure auto-stop). Don't reflexively make
  everything opt-in — match the default to whether it's a preference or a protection.

### Standing 1.0 quality bar (weigh every change against these; call out regressions/gaps)
Toward a stable daily-driver for long, often-unattended capture:
- **Correctness & edge cases** — multi-monitor/DPI, window tracking (resize/minimize/close/off-screen/
  cross-DPI), encode/trim ranges, corrupt/missing/foreign session files, numeric bounds.
- **Reliability & recovery** — surface failures (never silent), crash-safe atomic persistence,
  auto-stop on trouble (low disk / repeated failure), resume after a crash.
- **Security** — any user/file input that reaches a path, a process, or an ffmpeg arg must be
  sanitised + quoted (`SanitizeFileName`/`SanitizeFolderName`; ffmpeg runs `UseShellExecute=false`).
  No path traversal; JSON deserialised only into known types.
- **Efficiency for the long run** — the per-frame hot path and per-tick UI work must stay cheap at
  hour 6 (tens of thousands of frames), not just minute 1: no O(n) folder scans per tick, no
  redundant per-frame IO, dispose every GDI/Bitmap/HDC.
- **Testing** — the capture engine is the riskiest, least-covered code; prefer **extracting pure
  logic** (scale-rect math, parsing, sanitisation, range bounds) into testable units and covering it.
- **Observability** — `Logger` exists; keep failures diagnosable (and consider surfacing the log).

---

## Critical invariants (these are real — don't "clean them up")

### 1. Capture-engine threading (`TimelapseCapture.Core/Capture/CaptureEngine.cs`)

- Capture runs on a `System.Threading.Timer` (NOT the UI thread).
- All shared-state access is inside `lock (_lock)`.
- **Events (`FrameCaptured`, `CaptureFailed`, `SmartStatusChanged`) are raised
  OUTSIDE the lock** — a subscriber may call `Stop()` on another thread; raising
  inside the lock would deadlock. Keep it that way.
- **No async/await in the capture path, no fire-and-forget tasks.** Hard constraint.
  Window-tracking per-tick work (`CaptureFrameBitmap`, the tracked-region resolve, `ScaleToLocked`)
  runs synchronously on the timer thread **inside** `lock (_lock)` — keep it that way.
- The WPF VM marshals these events to the UI thread via
  `Application.Current.Dispatcher.BeginInvoke` (see `MainViewModel.OnFrameCaptured`
  / `OnSmartStatus` / `OnCaptureFailed`). Never touch WPF controls directly from an engine event.
- **Capture failures are surfaced, not swallowed.** A failed frame-save raises `CaptureFailed`;
  the VM shows a red banner (`CaptureError`) and **auto-stops after 3 consecutive failures**.
  The engine also throws (→ `CaptureFailed`) if `session.json` vanishes mid-capture or a tracked
  window is closed/minimized (unless "wait while minimized" is on). Don't reintroduce silent failure.

### 2. Region / DPI

- Capture works in **physical pixels**; WPF works in **DIPs**. Convert with
  `ScreenHelper.SystemDpiScale()` (see `RegionOverlay.ShowForRegion`,
  `RegionSelectOverlay`, `RegionEditOverlay`).
- In the WPF VM the runtime region is `_region`. Every region source
  (`SelectRegion`, `SelectFullScreen`, `EditRegion`, **`TrackWindow`**) funnels through the
  single `ApplyRegion(rect, label, trackedWindow)` method, which updates `_region` + `RegionText`
  + the overlay, **resets `_trackedWindow`** (so static sources clear tracking automatically; only
  `TrackWindow` passes a handle), AND persists the region into `session.json` via `PersistRegion()`
  (reload-set-save, frame-count safe). Loading restores `_region` from `session.CaptureRegion`,
  validated to still intersect a current monitor. **Route any new region source through
  `ApplyRegion()`.** (The engine does NOT persist the region — `ApplyRegion` does.)
- **Window tracking:** when a window is tracked, the engine re-reads `GetWindowRect` each tick and
  follows the window (size locked at Start). `WindowEnumerator` (Core) enumerates/reads windows in
  physical pixels. Tracking is **not** persisted across restarts (HWNDs aren't stable) — a reloaded
  session is a static region at the saved spot.
- The on-screen overlay (`RegionOverlay`) draws its 2px outline in the ring
  **just outside** the region (and its dimension label **above** the top edge) so
  the outline is never captured into the frames. Don't move it onto the region.

### 3. Persistence is crash-safe

- `SettingsManager.Save()` and `SessionManager.SaveSession()` write atomically
  (temp file + `File.Replace`) and swallow/log IO errors. An IO hiccup must not
  corrupt `settings.json`/`session.json` or throw into the capture loop.
- Settings fields are **additive** for backward-compat (e.g. `IntervalSecondsExact`,
  `JpegQuality`). Add new settings the same way — never renumber/remove.

### 4. Sessions

- Session files are user data. **Never delete automatically.**
- `FramesCaptured > 0 && CaptureRegion == null` is a known recoverable corruption
  state — use `ValidateAndRepairSession()`, don't hand-roll a fix.
- At most one session has `Active = true`.

### 5. Encoding correctness (`TimelapseCapture.Core/Video/VideoEncoder.cs`)

- Uses the ffmpeg **image2 demuxer** (`-framerate {fps} -start_number 1 -i %05d.ext
  -pix_fmt yuv420p`), NOT the concat demuxer — concat + `-r` resampling dropped
  frames (165 in → tiny output). Verified 165→165. Don't "simplify" back to concat.
- Frames must be **uniform** (same WxH and same extension) for image2 to work.
  That's why the WPF app warns before changing **region** or **format** when a
  session already has frames (`ConfirmRegionChange()` / the `UsePng` setter). Window-tracking
  preserves this: lock mode keeps a fixed-size box; **scale modes (Fit/Stretch) resample every
  frame to the locked size** (`ScaleToLocked`) so output is always uniform regardless of the
  window's live size.
- **Trimming** encodes a contiguous frame range directly (`-start_number`/`-frames:v`), no
  re-encode and no renumber — see `VideoEncoder.EncodeAsync(... startFrame, maxFrames, outputName)`.
  Output filenames come from a user template (`{session}`/`{date}`/`{time}`/`{datetime}`).
- JPEG quality only applies because the engine saves via a JPEG quality encoder
  (`CaptureEngine.SaveBitmap`) — plain `Bitmap.Save(file, ImageFormat.Jpeg)`
  silently ignores the quality. Don't revert that.

---

## WPF file map

```
TimelapseCapture.Wpf/
├── App.xaml(.cs)              palette + styles (Card, Btn*, Seg, SectionHeader, HeaderToggle,
│                              DarkTextBox, themed ScrollBar + CheckBox, PulseFg/PulseRing,
│                              BoolToVis/StrEq converters) · ThemeManager.Apply on startup
├── ThemeManager.cs            colour themes (Terminal/Ocean/Ember/Synth/Light), live swap
├── MainWindow.xaml(.cs)       two-column layout in a ScrollViewer (shrink-to-scroll); header
│                              has Stay-on-top / Overlay / ⚙. Code-behind: global hotkey,
│                              SetWindowDisplayAffinity (hide-from-capture), target commit
├── Converters.cs / Behaviors.cs   StringEqualsConverter, NumericInput attached behaviour
├── FramePreview.cs            loads latest / Nth frame as a small frozen image
├── SettingsDialog · OverlayDialog · TrimDialog · LoadSessionDialog · MonitorPickerDialog ·
│   WindowPickerDialog · TextPromptDialog   (dark modal dialogs)
├── RegionOverlay / RegionSelectOverlay / RegionEditOverlay   (on-screen region UI)
└── ViewModels/
    ├── MainViewModel.cs       the brain — ONE partial class split by concern (2026-07-12):
    │     .cs (core: fields/ctor/commands/Dispose) · .State (bound settings props + flags)
    │     · .Target (h/m/s + timer + run clock) · .Stats (stat rows + RefreshStats tick)
    │     · .Session (folder/new/load/rename/crash-recovery) · .Prefs (theme/tray/safety/
    │     overlay props) · .Hotkeys (keymap) · .SettingsOps (dialogs/backup/presets/import)
    │     · .Region (sources + outline, ApplyRegion funnel) · .Capture (start/stop/engine
    │     events) · .Encode (encode/trim/cull/crop/ffmpeg)
    ├── RelayCommand.cs        ICommand (CommandManager.RequerySuggested)
    └── ViewModelBase.cs       INotifyPropertyChanged + SetProperty

TimelapseCapture.Core/
├── Capture/  CaptureEngine, WindowEnumerator (track-window enum + GetWindowRect/SetTopmost),
│             ActivityMonitor (smart interval), ScreenHelper, AspectRatio, OverlayConfig
├── Core/     SessionManager, SettingsManager (CaptureSettings), PresetManager (named setups —
│             identity/safety fields stripped), Logger, Constants, UIState
├── Video/    VideoEncoder, FfmpegRunner, FfmpegDownloader
└── Utilities/ SystemMonitor (storage/mem stats), ValidationHelper, PerformanceOptimizations
```

`MainViewModel` is the place to look for almost any WPF behavior. Key shape:
engine events → marshalled to UI; a 1s `DispatcherTimer` drives `RefreshStats()`
(elapsed every tick, the heavier disk probe throttled to ~2s); commands gate on
`IsCapturing` / `NotCapturing` / `RegionNeeded` / `SessionNeeded` etc.

---

## WPF feature status (verified 2026-06-26)

**Big features since parity (0.9.x → 0.9.3):**
- **Window / element tracking** (0.9.3, headline) — pick a window, follow it as it moves; size
  locked at Track time; transit frames skipped while moving; live-following Show outline; options
  for on-minimize (stop/wait), keep-on-top, on-resize (Lock / Fit letterbox / Stretch). Engine
  `CaptureFrameBitmap`/`ScaleToLocked` + Core `WindowEnumerator`; `MainViewModel.TrackWindow`.
- **Capture-failure surfacing** (red banner + 3-strike auto-stop) — no more silent frozen-count.
- **Clip trimming** (frame-range scrubber → encode a range), **custom output naming** (template).
- **Crash recovery** (Active flag → resume prompt), **opt-in configurable global hotkey**,
  **pause/resume**, **configurable text overlay** (own Overlay dialog), **cursor capture**,
  **hide-this-window-from-capture**, **multi-monitor full-screen picker**, **settings
  export/import**, **stop-at-target**, **selectable live colour themes** + themed scrollbars/checkboxes.

**Done — at or beyond WinForms parity:**
output folder picker · new/load session · open session folder · select region +
full screen · region overlay toggle (click-through, shows WxH, "Show"→"Hide"
accent when on) · start/stop capture · sub-second interval (`IntervalSecondsExact`,
decimal) · format JPEG/PNG · **working JPEG quality** · smart interval
(active/idle/skip) with **live status** (Active / Idle — slowed / Idle — skipping)
· encode (image2, exact frames) · **cancel encode** (button flips to "Cancel
encode") · ffmpeg download (speed + cancel) / browse / custom-path-override warning
· encode fps + CRF · stats panel (frame size, projected/total/available storage,
memory) · **% progress vs target** + live target field (s/m/h) · **Run + Total
elapsed** (Total accumulates across stop/start and persists across reload via
`SessionInfo.TotalCaptureSeconds`) · lock settings during capture ·
**mid-session change warnings** (region/format with frames) · tooltips
throughout · pulse/highlight cues for the next required action ·
**in-app session picker** (dark list: name · date · frames · size, replaces the
native folder browser) · **encode preset** (Fast/Medium/Slow segmented control) ·
**aspect-ratio lock** on region select (Free/16:9/4:3/1:1/9:16) · **editable region
overlay** ("Edit" → drag to move, 8 handles to resize, Apply/Cancel; corners keep
the locked ratio, edges free, Shift frees) · **live preview** of the latest frame
(bottom-right) · **pulsing red ● REC** header indicator · **rename session** (click
the header name) · **settings cog** (⚙ → SettingsDialog: Appearance/theme, Window tracking,
Encoding/naming, Hotkey) · **Overlay** + **Stay-on-top** header controls.

**Smart interval semantics (corrected — don't re-invert):** the main **Interval**
is the *working/active* rate; when idle, capture slows to **Idle rate**
(`IdleIntervalSeconds`, clamped to never be faster than the working rate) or skips.
Engine: active → `_baseIntervalMs`; idle → `max(base, _idleIntervalMs)` or skip.
The old model (idle used the main interval, "Active (s)" was the active rate) let a
fast main interval capture *more* while idle — that was the bug. `ActiveIntervalSeconds`
remains in settings as dead/legacy (WinForms); WPF uses `IdleIntervalSeconds`.

**WPF binding gotcha:** `<Run Text="{Binding ...}">` defaults to **TwoWay** and
throws on read-only props (crashed the session picker). Bind one `TextBlock.Text`
to a precomputed string instead of multiple `Run`s.

The original parity-plus-polish list is **complete**. Reusable bits added this
session: a dark **segmented control** (`Seg` style + `StringEqualsConverter`, in
`App.xaml`/`Converters.cs`) — prefer it over a ComboBox for any future discrete
pick; and `RegionEditOverlay` for on-screen region editing.

**Verified hands-on (Spike tests each build):** region select/edit overlays, smart interval,
encode/trim, live themes, and window tracking (move-follow, resize Lock/Fit/Stretch, minimize +
keep-on-top) have all been exercised live over the 0.9.x arc.

**0.9.4 — the 1.0 release candidate (2026-07-02):** **unattended safety complete** (pre-flight +
low-disk auto-stop default-on, opt-in max-duration cap, stop-at-target, finish notification =
sound + taskbar flash) · **frame cull** (`CullDialog` + `SessionManager.CullAndRenumber`,
renumbers gapless) · **custom themed title bars** (main window WindowChrome caption + shared
`DialogWindow` style for every dialog) · **Simple mode** (header toggle; speed slider with named
notches 0.5s–60s + plain-language outcome hint; hides the advanced surface) · **first-run setup
wizard** (`SetupWizard`: folder → capture area → speed → ffmpeg download → done; re-run from
Settings) · hardening pass from a multi-agent audit (BitBlt secure-desktop skip — no more silent
black frames; ffmpeg preset allowlist; perf: no per-frame double session read, O(1) preview,
sampled frame-size stat) · "Open log" in Settings · solution builds at 0 warnings.

**Remaining toward 1.0:** **window-tracking slice 2** (WGC for occluded windows, persist tracking
across restarts, client-area-only), seconds⇄fps interval toggle + optional power-user interval
floor, provenance (ffmpeg metadata + optional watermark — direction decided in ROADMAP item 10),
packaging/installer. The richer-aesthetic pass is well underway (themed controls, live themes,
custom chrome all landed).

---

## Handoff notes for the next thread

- The app (**FrameWrite**, display-renamed; projects still `TimelapseCapture*`) is at **0.9.4,
  the 1.0 RC**, tagged `v0.9.4`, with a large RC-refinement arc on `main` (see CHANGELOG). MIT
  LICENSE + README are in. Spike tests each build live and gives UX feedback.
- The working loop: build green (0 warnings) + `dotnet test` 68/68 → commit per feature → push →
  relaunch the exe for Spike. He's git-averse (Claude owns git). Adversarially review diffs
  (multi-agent when limits allow, manual otherwise) — the passes keep finding real bugs pre-commit.
- **1.0 posture (Spike, 2026-07-10): no deadline.** The app is professional-grade but mainly for
  personal use; development continues continuously rather than gating on the QA/soak protocol.
  Relax process where it buys development strides — but keep rigorously testing and hardening
  features, logic, and workflows as we go (empirical smoke tests, encode-with-real-ffmpeg, unit
  coverage). The soak/QA pass happens opportunistically, not as a blocker. The eventual 1.0 cut =
  mechanical FrameWrite rename (projects/exe/namespaces/mutex) + version bump + CHANGELOG.
- **Packaging is solved:** `scripts/publish-release.ps1` → self-contained single-file
  `dist/FrameWrite-v{version}-win-x64.zip` (verified: publishes, launches, installed-mode data dir).
- **Next major arc (Spike's priority): the UI elegance pass** — friend feedback says the UI is
  the weak point; make it more elegant/straightforward while keeping all power. Includes the
  stats-panel rework. Also queued: retroactive overlay bake (file-mtime timestamps), loupe/zoom
  on preview; noted for later investigation: "smart tracking" of on-screen elements.
- **1.1 line (agreed):** tray icon + hotkey chime · end-frame hold + encode-to-duration ·
  multi-session combine · configurable keybindings · crop-at-encode · loupe revisit ·
  Alt-drag-from-center region select · in-app bug report (pre-public) · WGC tracking slice 2.
- **Ignore `C:\Users\Spike\.claude\plans\jazzy-baking-trinket.md`** — obsolete WinForms-UI plan.

---

## Issue log (newest first)

#### 0.9.3 — window tracking + reliability/UX arc (2026-06-26)
- **Window tracking** shipped (pick → follow; size-locked; transit-skip; live overlay; resize
  Lock/Fit/Stretch; minimize stop/wait; keep-on-top). Designed + reviewed via multi-agent passes.
- **Silent-failure bug fixed:** deleting the output/session folder mid-capture used to keep
  "recording" with a frozen count and no error. Now the engine throws on a missing `session.json`
  and the VM shows a banner + auto-stops after 3 failures.
- UX: target field commit-pulse (no Set button), shrink-to-scroll, surfaced cursor/window options,
  Overlay editor split out, output naming, themed scrollbars/checkboxes, full-screen no-op (no warn).
- Stop-at-target edge fixed: sub-1s targets rounded to 0 and would instant-stop — now rejected.

#### WPF rebuild + full feature port (2026-06-24)
- New WPF/MVVM front-end on the shared `TimelapseCapture.Core` engine reached
  WinForms parity plus polish (see feature status). Fixed along the way: encode
  frame-drop (concat→image2), sub-second interval (decimal `IntervalSecondsExact`),
  JPEG quality actually applying (quality encoder in `CaptureEngine.SaveBitmap`),
  ContextMenu dispose race, region-overlay click-through + not-captured outline.

#### (Legacy WinForms history) Full review — 21 findings, all addressed (2026-06-23)
- Pre-rebuild hardening of the WinForms app: crash guards for bad settings/session
  files, a close-time `session.json` race, atomic persistence, ffmpeg validation
  deadlock, leaks, encode Cancel, custom-ffmpeg-path warning. (Applies to `src/`.)

(Older/full history: `docs/archive/` and `docs/development/claude/ignore/`.)

---

## Where to look for more (only if pointed here)

- `docs/STRUCTURAL_MAP.md`, `docs/PROJECT.md` — describe the **legacy WinForms**
  structure + schemas; useful for parity but not the WPF layout.
- `docs/archive/` — completed features, fixed bugs, superseded docs.

---

**Last updated:** 2026-06-26 · **Maintainer:** Spike (+ Claude)
