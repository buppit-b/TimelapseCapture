# Roadmap & Versioning — TimelapseCapture

## Versioning

We use **Semantic Versioning** (`MAJOR.MINOR.PATCH`):

- **MAJOR** — incompatible / milestone releases (1.0 = the first stable "daily driver").
- **MINOR** — new features, backward-compatible.
- **PATCH** — bug fixes and small tweaks, backward-compatible.

While pre-1.0 we stay on **`0.x`**: minor bumps for features, patch bumps for fixes;
breaking changes are allowed but called out. The version lives in the two
`.csproj` files (`<Version>`) and is shown in the **Settings** dialog (the cog).

**Current: `0.9.4` — the 1.0 release candidate.** Everything on the 1.0 feature line has
landed (window tracking, unattended safety, trim + cull, Simple mode + setup wizard, custom
chrome, hardening/perf passes; 0-warning build, 33/33 tests). **1.0 = this RC + a clean
soak test** (see "1.0 gate" below) + whatever that soak reveals.

### What 1.0 means here
A **versatile daily driver for timelapse capture**, aimed at artists capturing
their digital work: stable, no known data-loss or capture-correctness bugs, the
core capture/region/encode flow friction-free, and the must-have features below in.
The exact 1.0 feature line is Spike's call — this file is the shortlist to choose from.

---

## Toward 1.0 — candidate features

Ranked roughly by value for the artist use case.
**Current priority (2026-07-02):** the 1.0 line is complete — see the **1.0 gate + 1.1
candidates** section below for what happens next.

1. **Window / application capture** *(Spike's biggest want)* — ✅ **first slice done (0.9.3)**:
   pick a window (HWND), follow its position each tick (`GetWindowRect` + BitBlt), size locked
   at Track time so frames stay uniform; transit frames skipped while moving; live-following
   Show outline; options for on-minimize (stop/wait), keep-on-top, and on-resize
   (Lock / Fit letterbox-scale / Stretch). Core: `WindowEnumerator`; engine `ResolveTrackedRegion`-
   style logic + `CaptureFrameBitmap`/`ScaleToLocked`.
   - *Deferred (slice 2+):* **Windows.Graphics.Capture (WGC)** for occluded/off-screen windows
     (hardware-accelerated, the modern approach; `PrintWindow` is a middle option); persisting
     tracking across restarts (HWNDs aren't stable — re-match by title/process); client-area-only
     capture (drop the title bar via `DwmGetWindowAttribute`); per-DPI rescale across monitors.
   - *Element capture* (a sub-control inside a window) has no general OS API — best
     approximated as a region within a chosen window. Likely out of scope for 1.0.
   - **Hide-from-capture** toggle (0.9.2): excludes *this app's own* window from captures
     (`SetWindowDisplayAffinity`).
2. **Hotkeys / pause** — ✅ **done (0.9.x)**: global start/stop hotkey (now opt-in +
   user-configurable in Settings) and explicit **pause/resume** that keeps the run going.
3. **Auto-encode on stop** (optional) + **frame review/cull** before encoding
   (scrub the frames, delete fumbles) — makes finishing one step.
   - **Clip trimming** — ✅ **done (0.9.2)**: a scrubber picks a start/end frame and encodes
     only that contiguous range (image2 `-start_number`/`-frames:v`, no re-encode).
   - **Frame cull** — ✅ **done**: `CullDialog` marks fumble frames; `SessionManager.CullAndRenumber`
     deletes them and renumbers the rest gapless (keeping image2 happy) — which also closes the
     gapped-sequence encode concern.
4. **Unattended safety** — ✅ **done**: pre-flight + low-disk auto-stop, opt-in max-duration cap,
   stop-at-target, capture-failure auto-stop, and a finish notification (sound + taskbar flash).
5. **Crash recovery** — ✅ **done (0.9.x)**: the Active flag is managed (start/stop),
   and on launch the app offers to resume a session left recording when it died.
6. **Frame overlay** — ✅ **configurable text overlay done (0.9.x)**: tokens
   ({datetime}/{date}/{time}/{time12}/{t:FORMAT}) + literal text, corner position,
   font family, font size. Richer follow-up *(Spike wants "highly customisable")*:
   **logo/image overlay**, **free-drag placement** + live preview, colour/opacity,
   {elapsed}/{frame} tokens, and an **encode-time overlay** option (apply at encode
   via ffmpeg drawtext/overlay — covers "forgot to enable it", though a true
   per-frame capture timestamp can only be baked live, not reconstructed at encode).
7. **Advanced encode settings** — a power-user panel to pass extra/custom ffmpeg
   arguments (codec, pix_fmt, extra filters, two-pass, etc.) on top of the simple
   fps/CRF/preset. Good idea for this audience; keep the simple controls as the
   default and tuck the raw-args box behind an "Advanced" toggle. Validate/escape
   args and guard against breaking the image2 input the app relies on.
8. **Multi-monitor / all-screens** capture as a region preset.
9. **Output naming templates** — ✅ **done (0.9.3)**: `{session}`/`{date}`/`{time}`/`{datetime}`
   filename template for encodes/trims (Settings → Encoding). A chosen encode output *path*
   (separate from the session folder) is still open.
10. **Provenance / app signature** *(Spike's idea — identifying the app's output in the wild.
    Direction decided 2026-06-26; **marked for later**, toward 1.0.)* Two approved approaches:
    - **(1) ffmpeg metadata tags** on every encode/trim — `-metadata encoder="TimelapseCapture x.y.z"`
      plus `comment`/`software`. Standard, non-destructive, doesn't touch the picture; read by
      ffprobe / MediaInfo / Windows file properties. ~2 lines in `VideoEncoder`. *Caveat:* platforms
      (YouTube etc.) often strip metadata on re-encode, so this best identifies files shared directly.
    - **(2) Optional visible watermark / logo** — off by default, reusing the overlay system; survives
      re-encoding. A credit feature for users who want it (most artists will leave it off).
    - **Ruled out:** covert steganographic pixel watermarking — fragile through compression, and a
      transparency/trust problem for an artist tool. Keep provenance open, not hidden.

## 1.0 gate + 1.1 candidates (agreed with Spike, 2026-07-02)

### The 1.0 gate: a real soak test + scenario pass
The one thing never validated is the thing the app is for — a multi-hour unattended run.
Protocol (Spike runs it; costs no code): start a tracked-window or region capture at a ~1s
interval and leave it 6–8 hours. Pass criteria: memory roughly flat (Task Manager at start vs
end), frame count ≈ elapsed/interval (minus idle skips), `debug.log` quiet, and the resulting
session encodes clean end-to-end. Alongside it, run **`docs/QA_CHECKLIST.md`** — the interactive
scenario matrix (fullscreen games, lock screen, multi-monitor/DPI, drag interactions) that
automated tests can't reach; the 0.9.4 fullscreen-lockup bug is exactly the class it exists to
catch. **1.0 is the RC + a passing soak + a clean checklist pass.**

### Pre-distribution blockers (must happen before shipping 1.0 to anyone else)
- **Settings/log/ffmpeg location** — ✅ done (2026-07-03): `AppPaths.DataDir` self-selects once at
  startup — portable (next to the exe) when a settings.json already sits there (dev builds, USB
  layouts stay exactly as before), else `%APPDATA%\Framewright` (what an installer needs — the
  Program Files exe folder isn't writable). FindFfmpeg checks both locations; rule unit-tested.
- **Packaging** — an installer or at least a versioned release zip on GitHub Releases.
- **LICENSE** — ✅ done (2026-07-03): MIT, © Spike Tickner. README rewritten for Framewright
  (features, build, publish); the FFmpeg-is-a-separate-GPL-program note lives in the README and
  the Settings credits tooltip.
- **The Framewright rename, mechanically** — display branding is done (2026-07-03); at the 1.0
  cut, rename the exe/projects/namespaces/repo (`TimelapseCapture*` → `Framewright*`), the
  single-instance mutex name, and the docs. One dedicated commit; verify the FindWindow title
  lookup and settings/log paths still line up.

### 1.1 candidates (top three, in recommended order)
1. **Tray icon with recording state** — ✅ **pulled into 1.0 (2026-07-04)**: NotifyIcon (in-SDK
   WinForms, no new dependency) shows a green/red status dot + tooltip frame count, double-click
   restores, right-click Show/Start-Stop/Exit, "Minimize to tray" setting (default on) hides from
   the taskbar, balloon on finish while hidden. *Still open:* an optional **chime on hotkey
   start/stop** (audio feedback when the window's hidden — the finish sound exists, but not a
   start/stop cue).
2. **Finish-line encode options** — **hold the final frame** ✅ **shipped (0.9.4)** (ffmpeg `tpad`,
   an encode-card field, smoke-tested). **Frame-skip encode** ✅ **shipped (0.9.4)**. Remaining:
   **encode to a target duration** ("make it exactly 60s" — fps computed from frame count; social
   platforms have ceilings; ~small, reuses the frame-count math).
   **Crop at encode** *(Spike, 2026-07-03 + 07-05)* — encode only a sub-region of the frames (ffmpeg
   `crop`, even dims for yuv420p; UI: drag a crop rect on a frame preview, reusing the region-edit
   overlay). Recommend **non-destructive at encode by default**, with an **opt-in destructive
   "crop the frames on disk to reclaim space"** for power users (a Cull-style consented, irreversible
   op — re-saves every frame cropped). Medium; the smoke test can verify output dimensions.
3. **Multi-session combine** — select several sessions and encode one continuous video (the
   "100 hours in 10 minutes" workflow). Needs uniform frame sizes across sessions + guardrails.

### Overlay follow-ups (Spike, 2026-07-05)
- **Overlay: burn-in during vs after encode** — today the text is burned per-frame at capture time.
  Add a choice: *during capture* (a live per-frame timestamp is only truthful this way) OR *at
  encode* (ffmpeg `drawtext`) — the latter covers "forgot to enable it" and lets a token like
  `{frame}`/date be stamped after the fact. Recommend a segmented "Burn: during / at encode" in the
  Overlay dialog; capture-time-only tokens (a true per-frame wall-clock) get a note that they can't
  be reconstructed at encode.
- **Logo / image overlay (transparent PNG)** — overlay a PNG (logo/watermark) with position +
  opacity, at capture (GDI `DrawImage`) or encode (ffmpeg `overlay`). Reuses the drag-to-place UI.
  Pairs with the provenance idea (item 10). Off by default.

### Cull follow-up: remove static/idle stretches (Spike asked; my recommendation)
- Spike wants to bulk-cull "idle frames." **Recommendation: detect near-DUPLICATE frames rather than
  track input-idle metadata.** Marking input-idle frames needs per-frame activity state recorded at
  capture (new metadata, only catches input-idle). Comparing each frame to the previous and marking
  ones below a difference threshold is *more general* (catches ANY static stretch — a paused canvas,
  a render, a coffee break), needs no capture-time metadata, and works on already-captured sessions.
  Implement as a Cull button "Mark static frames…" with a sensitivity slider + preview count; uses a
  cheap downscaled-frame diff. (Smart-interval already SKIPS most idle frames live, so this is the
  cleanup for what slipped through.)

### Stats panel rework (Spike, 2026-07-05 — "needs attention")
- The stats panel grew organically and uses emoji (📦💾📊📁🖥️🎬💻). Rework: replace emoji with
  **dedicated glyph icons** (Segoe MDL2 Assets — already used for the window buttons, renders as
  proper monochrome icons, no image assets), restructure `SystemMonitor.GetStorageInfoString`'s
  one big string into **individual bound rows** (icon · label · value) so each can be styled/colored
  (the storage-rate warning already is), and make the key numbers (video length, storage rate,
  time-to-target — all now added) the visual anchors. A focused UI pass; pairs with the broader
  GUI reshape.
4. **Configurable keybindings** *(Spike, 2026-07-03 — power-user philosophy: every hotkey
   rebindable)* — grow the existing hotkey-capture control into a small keymap table in Settings
   (action · binding · reset-to-default), persisted additively. Covers the global start/stop plus
   the Trim/Cull editing keys (step ±1/±10, mark/unmark, set start/end).

### 1.x smaller ideas (parked, roughly by value)
**Layout reshape** *(Spike + external tester, 2026-07-04 — "greatly interested", but "can wait")*:
rework the main two-column layout; the tester felt techy/setup bits eat workspace real estate —
consider moving more into Settings/tabs and slimming the main surface. Bigger UI task, own branch. ·
**Region-select global hotkey** *(Spike, 2026-07-04)*: trigger region select while the app is
alt-tabbed/minimized (pairs with the new hide-window-on-select) — needs a second registered hotkey
alongside start/stop; do it with the configurable-keybindings work. ·
**Scoped Encode presets** (deferred from the presets design — "same capture, two export looks") ·
**Simple-mode apply-only preset dropdown** (deferred from presets) ·
**Advanced-stats visual mode** *(Spike, 2026-07-03 — direction TBD with him)*: at-a-glance visual
elements beyond the target bar + encode bar (both shipped) — e.g. a session-storage gauge vs free
disk, a sparkline of capture cadence (gaps = idle skips), frame-size trend. Possibly a toggleable
"advanced stats" view so the default stays clean. ·
Start-capture-on-launch (+ optional launch-with-Windows) · **in-app bug report** *(Spike,
2026-07-02 — wants this before going public; simple is fine: a "Report a bug…" button that opens
a prefilled GitHub issue with app version/OS in the body and copies the recent `debug.log` tail
to the clipboard)* · GIF export · all-screens preset (item 8) · in-app playback preview at
target fps · zoom/loupe frame viewer (parked from 0.9.x — **Spike wants a revisit soon**; think
click-preview → floating zoom pane with scrub, not the old cramped inline loupe) ·
**Alt-drag region select from center** (agreed 2026-07-03: PS/Illustrator muscle memory, cheap —
anchor the drag at its start point and grow symmetrically while Alt is held, works with ratio
lock for easy centered squares) · {elapsed}/{frame} overlay tokens
(item 6 follow-up) · provenance (item 10, direction decided) · forward an open-session argument
from a second launch to the running instance via WM_COPYDATA (today the single-instance guard
drops it; drag-and-drop onto the window covers the case meanwhile).

### Explicitly out of scope (identity discipline)
Webcam/facecam, audio, a general video editor, cloud sync, sub-0.1s capture — those pull
toward OBS/editor territory; this app stays the reliable art-timelapse tool.

### Design debt — the one rework worth a focused, tested session
- **Frame-count / session-state ownership** *(flagged 2026-07-05; the pause data-loss bug came from
  it).* Today the live frame count lives in THREE places kept in sync by hand: the engine's
  in-memory `_session`, the VM's `_session`, and `session.json` on disk. Per frame the engine does a
  full `session.json` read-modify-write (`IncrementFrameCount`) — the read exists to avoid clobbering
  fields the VM writes (region/total-time/name), not just for the count. This is both an efficiency
  cost (a full JSON read+write per frame — 10×/s at the 0.1s floor) and the source of the drift class.
  **Proposed rework (needs Spike's hands-on validation of capture + crash recovery, so NOT done blind):**
  the frames on disk are the single source of truth for the count → the engine owns a live in-memory
  counter seeded at Start by reconciling against the actual highest frame number on disk; `session.json`
  count is persisted throttled (every ~N frames / on pause / on stop), never per frame; crash recovery
  reconciles from disk. Removes per-frame JSON IO entirely and eliminates the drift. Extract the
  disk-count/reconcile logic as `internal static` + unit-test it; keep the folder-deleted detection
  (the per-frame frame-save failure already surfaces it). *Acute bug already fixed* (real engine Pause +
  disk-reload on Start), so this is now a correctness+efficiency cleanup, not an outstanding bug.

### Cross-cutting / tech
- Per-monitor DPI correctness for region selection and cursor overlay on mixed-DPI
  multi-monitor setups (`ScreenHelper.SystemDpiScale` is system-DPI only today).
- Cross-platform: `TimelapseCapture.Core` is UI-agnostic but uses `System.Drawing`;
  a portable imaging lib (SkiaSharp/ImageSharp) would unlock non-Windows later.

---

## Known issues / UX audit (pre-1.0 cleanup)

- **New Session can spam empty folders** — rapid clicks each create a new timestamped
  session. **Fixed**: New Session now reuses a 0-frame session instead of spawning
  another folder; buttons also show a pressed state.
- **App-wide UX consistency pass** — verify every actionable control has clear
  pressed/disabled/hover affordances, sensible tab order, and keyboard support.
- **Cursor overlay on HiDPI** — the drawn cursor may mis-scale slightly on high-DPI
  displays (drawn at the system cursor's native size).
- **Pre-0.9 sessions have no saved region** — sessions captured before region
  persistence load as "Not selected"; pick the region once and it sticks.

### Audit candidates (2026-06-25 — found by the hardening workflow, NOT yet verified)

The audit's adversarial-verify pass ran out of session budget, so these are
*candidate* findings to confirm-then-fix next. Encode investigation is **complete**:
CRF/preset reach ffmpeg correctly and behave as expected (empirically measured);
no bug — the confusion was frames-on-disk vs final-video (now clarified in the UI).
Already fixed this pass: stuck `IsEncoding` on encode exception, partial `.mp4`
left on cancel/fail.

Already fixed since the audit: sessions now manage their `Active` flag (start/stop)
and crash recovery resumes an interrupted session on launch.

Also fixed this pass: New-Session spam guard, ffmpeg-path validation on Browse,
capture-tick re-entrancy (`Monitor.TryEnter` drops overlapping ticks), unique encode
filenames, and clean shutdown (close stops capture + disposes the engine).

Still to verify/fix (roughly by value):
- **Concurrent `session.json` writes** — engine `IncrementFrameCount` vs VM writes
  could race in theory, but VM writes happen outside the capture loop and writes are
  atomic (temp+replace), so low risk — worth a confirm.
- **Encode on gapped/renumbered frames** — ✅ resolved: frame cull renumbers to a gapless
  sequence by design, and the encoder now refuses mixed-format frame folders with a clear error.
- **Numeric fields** — ✅ done: numeric-only input (`NumericInput` behavior) + range
  clamping, so they can't hold junk.
- **Mixed-DPI**: a single system-DPI is used for all monitors (region/cursor offset
  on mixed-DPI multi-monitor setups).

---

**Maintainer:** Spike (+ Claude) · see `CHANGELOG.md` for released changes.
