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
- **Settings/log/ffmpeg live next to the exe** — breaks under Program Files (no write
  permission). Move config to `%APPDATA%` (keep a portable-mode fallback) before any installer.
- **Packaging** — an installer or at least a versioned release zip on GitHub Releases.
- **LICENSE** — the repo has none; Spike's call (MIT recommended for a tool like this). Plus a
  one-line in-app note that the downloaded ffmpeg is BtbN's GPL build (invoked as a separate
  process, so it doesn't constrain the app's own licence).

### 1.1 candidates (top three, in recommended order)
1. **Tray icon with recording state** — minimize to tray, red-dot "recording" glance state,
   right-click start/stop/open; plus an optional **chime on hotkey start/stop** (today the
   global hotkey gives zero feedback when the window is hidden). The missing daily-driver
   affordance for a background app.
2. **Finish-line encode options** — **hold the final frame** for N seconds (ffmpeg `tpad`; the
   finished artwork is the frame viewers want to see), and **encode to a target duration**
   ("make it exactly 60s" — fps computed from frame count; social platforms have ceilings).
   Plus *(Spike, 2026-07-03)*: **frame-skip encode** — use every Nth frame ("every 3rd") to speed
   the timelapse up without recapturing. Natural fit alongside the other two speed levers (fps =
   playback speed, skip = time compression, duration = the goal); non-destructive (unlike cull);
   ffmpeg `select='not(mod(n,N))'` filter on the image2 input. UI: a "use every [N]th frame"
   spinner in the encode card + Trim dialog, with the outcome hint updated to show the effect.
3. **Multi-session combine** — select several sessions and encode one continuous video (the
   "100 hours in 10 minutes" workflow). Needs uniform frame sizes across sessions + guardrails.

### 1.x smaller ideas (parked, roughly by value)
**Advanced-stats visual mode** *(Spike, 2026-07-03 — direction TBD with him)*: at-a-glance visual
elements beyond the target bar + encode bar (both shipped) — e.g. a session-storage gauge vs free
disk, a sparkline of capture cadence (gaps = idle skips), frame-size trend. Possibly a toggleable
"advanced stats" view so the default stays clean. ·
Start-capture-on-launch (+ optional launch-with-Windows) · **in-app bug report** *(Spike,
2026-07-02 — wants this before going public; simple is fine: a "Report a bug…" button that opens
a prefilled GitHub issue with app version/OS in the body and copies the recent `debug.log` tail
to the clipboard)* · GIF export · all-screens preset (item 8) · in-app playback preview at
target fps · zoom/loupe frame viewer (parked from 0.9.x) · {elapsed}/{frame} overlay tokens
(item 6 follow-up) · provenance (item 10, direction decided) · forward an open-session argument
from a second launch to the running instance via WM_COPYDATA (today the single-instance guard
drops it; drag-and-drop onto the window covers the case meanwhile).

### Explicitly out of scope (identity discipline)
Webcam/facecam, audio, a general video editor, cloud sync, sub-0.1s capture — those pull
toward OBS/editor territory; this app stays the reliable art-timelapse tool.

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
