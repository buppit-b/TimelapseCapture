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
- **`TimelapseCapture.Tests`** — 8 tests, cover `SessionManager` + `ValidationHelper`.

- Repo: https://github.com/buppit-b/TimelapseCapture (default branch `main`)
- **Build:** `dotnet build TimelapseCapture.sln`
- **Run the WPF app:** `dotnet run --project TimelapseCapture.Wpf`
  (or launch `TimelapseCapture.Wpf/bin/Debug/net9.0-windows/TimelapseCapture.Wpf.exe`)
- **Test:** `dotnet test TimelapseCapture.sln`
- Windows only (.NET 9, `net9.0-windows`).

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
- **Keep the build green** — `dotnet build` at 0 errors, `dotnet test` at 8/8.
- **Respect the invariants below** — each came from a shipped bug.
- Improving/simplifying nearby code is welcome; for a true architectural shift,
  align on the approach first.
- **Don't add dependencies** without a clear reason — the app is intentionally lean.
- Aesthetics are **deprioritized** right now (Spike's call): clean up what we had,
  green accent is fine as placeholder, function/parity first. A richer aesthetic
  pass comes later.

---

## Critical invariants (these are real — don't "clean them up")

### 1. Capture-engine threading (`TimelapseCapture.Core/Capture/CaptureEngine.cs`)

- Capture runs on a `System.Threading.Timer` (NOT the UI thread).
- All shared-state access is inside `lock (_lock)`.
- **Events (`FrameCaptured`, `CaptureFailed`, `SmartStatusChanged`) are raised
  OUTSIDE the lock** — a subscriber may call `Stop()` on another thread; raising
  inside the lock would deadlock. Keep it that way.
- **No async/await in the capture path, no fire-and-forget tasks.** Hard constraint.
- The WPF VM marshals these events to the UI thread via
  `Application.Current.Dispatcher.BeginInvoke` (see `MainViewModel.OnFrameCaptured`
  / `OnSmartStatus`). Never touch WPF controls directly from an engine event.

### 2. Region / DPI

- Capture works in **physical pixels**; WPF works in **DIPs**. Convert with
  `ScreenHelper.SystemDpiScale()` (see `RegionOverlay.ShowForRegion` and
  `RegionSelectOverlay`).
- In the WPF VM the region lives in three mirrored spots that must stay in sync:
  `_region` (runtime), `_session.CaptureRegion`, `_settings.Region`. They're set
  together in `SelectRegion` / `SelectFullScreen` / load — keep them consistent.
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
  session already has frames (`ConfirmRegionChange()` / the `UsePng` setter).
- JPEG quality only applies because the engine saves via a JPEG quality encoder
  (`CaptureEngine.SaveBitmap`) — plain `Bitmap.Save(file, ImageFormat.Jpeg)`
  silently ignores the quality. Don't revert that.

---

## WPF file map

```
TimelapseCapture.Wpf/
├── App.xaml(.cs)              palette + styles (Card, Btn*, DarkTextBox,
│                              BtnOverlay toggle, BoolToVis converter, pulse)
├── MainWindow.xaml(.cs)       two-column layout: left OUTPUT/SESSION/CAPTURE/
│                              SMART INTERVAL cards, right STATUS + STATS cards
├── RegionOverlay.xaml(.cs)    click-through region outline + WxH label (toggle)
├── RegionSelectOverlay.xaml(.cs)  drag-select → physical-pixel Rectangle
└── ViewModels/
    ├── MainViewModel.cs       the brain — all commands/properties/state
    ├── RelayCommand.cs        ICommand (CommandManager.RequerySuggested)
    └── ViewModelBase.cs       INotifyPropertyChanged + SetProperty

TimelapseCapture.Core/
├── Capture/  CaptureEngine, ActivityMonitor (smart interval), ScreenHelper, AspectRatio
├── Core/     SessionManager, SettingsManager (CaptureSettings), Logger, Constants, UIState
├── Video/    VideoEncoder, FfmpegRunner, FfmpegDownloader
└── Utilities/ SystemMonitor (storage/mem stats), ValidationHelper, PerformanceOptimizations
```

`MainViewModel` is the place to look for almost any WPF behavior. Key shape:
engine events → marshalled to UI; a 1s `DispatcherTimer` drives `RefreshStats()`
(elapsed every tick, the heavier disk probe throttled to ~2s); commands gate on
`IsCapturing` / `NotCapturing` / `RegionNeeded` / `SessionNeeded` etc.

---

## WPF feature status (verified 2026-06-24)

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
elapsed** (Total accumulates across stop/start within a run) · lock settings during
capture · **mid-session change warnings** (region/format with frames) · tooltips
throughout · pulse/highlight cues for the next required action.

**Still on the list (next thread, in priority order):**
1. **Encode preset** (ultrafast→slow). Needs a dark dropdown (see note below).
2. **Aspect-ratio lock** on region select (16:9, 4:3, …). `Core/Capture/AspectRatio.cs`
   exists; `RegionSelectOverlay` would constrain the drag. Same dropdown need.
3. **Editable overlay (toggle "edit mode")** — Spike's idea: drag/resize the
   capture region directly from the on-screen box, **as a toggleable mode** so it
   doesn't fight the click-through (WS_EX_TRANSPARENT). When edit-mode is on, drop
   the transparent ex-style and show 8 resize handles + a move grab in the center;
   on change, convert DIPs→physical px and update `_region`/session/settings (route
   through the same path as `SelectRegion`, and re-run `ConfirmRegionChange()` if
   frames exist). This is the meatiest remaining piece — give it a dedicated step.

> **Dropdown note:** there's no dark `ComboBox` style yet, and the default WPF
> ComboBox renders light/jarring on the dark theme. Either add a proper dark
> ComboBox `ControlTemplate` to `App.xaml`, or model preset as a small slider +
> name label (0–3, "Preset: Medium") which themes more easily. `VideoEncoder`
> currently hardcodes preset `"medium"` — thread the chosen value through
> `MainViewModel.Encode()` → `VideoEncoder.EncodeAsync(..., preset, ...)`.

**Persistence gaps (minor, not yet requested):** `Total` elapsed and the
accumulated capture time are **in-memory only** (reset on new/load session and on
app restart). If Spike wants them to persist, store to `SessionInfo.TotalCaptureSeconds`
on stop and restore on load.

---

## Handoff notes for the next thread

- This session was a long, iterative polish pass on the WPF app driven by Spike
  testing each build and sending screenshots. Everything above under "Done" landed
  this session and is pushed to `main` (latest commit ~`fca75ab`).
- The most recent asks, all addressed: progress % + total time, smart-interval
  status, mid-session warnings, smooth 1s elapsed, overlay dimensions, cancel
  encode, working JPEG quality, smart-interval tooltips, Show-button toggle state,
  un-clipped ffmpeg Cancel button.
- **Next:** encode preset → aspect-ratio lock → editable overlay (see list above).
- **Ignore `C:\Users\Spike\.claude\plans\jazzy-baking-trinket.md`** — it's a plan
  for reworking the *old WinForms* UI and is obsolete (we rebuilt in WPF instead).
- Keep committing per-feature and relaunching the exe for Spike to verify.

---

## Issue log (newest first)

#### WPF rebuild + full feature port (in progress — 2026-06-24)
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

**Last updated:** 2026-06-24 · **Maintainer:** Spike (+ Claude)
