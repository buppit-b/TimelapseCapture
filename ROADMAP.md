# Roadmap & Versioning — TimelapseCapture

## Versioning

We use **Semantic Versioning** (`MAJOR.MINOR.PATCH`):

- **MAJOR** — incompatible / milestone releases (1.0 = the first stable "daily driver").
- **MINOR** — new features, backward-compatible.
- **PATCH** — bug fixes and small tweaks, backward-compatible.

While pre-1.0 we stay on **`0.x`**: minor bumps for features, patch bumps for fixes;
breaking changes are allowed but called out. The version lives in the two
`.csproj` files (`<Version>`) and is shown in the **Settings** dialog (the cog).

**Current: `0.9.3`** — the WPF rebuild has reached WinForms parity plus a large
polish/feature pass, and the headline **window tracking** feature has landed. We're
in the run-up to 1.0: closing issues and edge cases.

### What 1.0 means here
A **versatile daily driver for timelapse capture**, aimed at artists capturing
their digital work: stable, no known data-loss or capture-correctness bugs, the
core capture/region/encode flow friction-free, and the must-have features below in.
The exact 1.0 feature line is Spike's call — this file is the shortlist to choose from.

---

## Toward 1.0 — candidate features

Ranked roughly by value for the artist use case.
**Current priority (2026-06-26):** window tracking shipped (0.9.3). Next likely
candidates: **unattended safety** (item 4) and **auto-encode / frame cull** (item 3).

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
     only that contiguous range (image2 `-start_number`/`-frames:v`, no re-encode). A range
     needs no renumber; *frame cull* (deleting interior fumbles, which gaps the sequence) is
     the remaining, harder piece — that's where the gapped-frame encode edge case bites.
4. **Unattended safety** — auto-stop on low disk space or an optional max duration;
   notification when a long run / encode finishes.
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
- **Encode on gapped/renumbered frames** — image2 `%05d` + `-start_number 1` stops at
  the first gap; only relevant once frame-cull/editing lands (which would renumber).
- **Numeric fields** — ✅ done: numeric-only input (`NumericInput` behavior) + range
  clamping, so they can't hold junk.
- **Mixed-DPI**: a single system-DPI is used for all monitors (region/cursor offset
  on mixed-DPI multi-monitor setups).

---

**Maintainer:** Spike (+ Claude) · see `CHANGELOG.md` for released changes.
