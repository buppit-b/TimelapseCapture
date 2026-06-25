# Roadmap & Versioning — TimelapseCapture

## Versioning

We use **Semantic Versioning** (`MAJOR.MINOR.PATCH`):

- **MAJOR** — incompatible / milestone releases (1.0 = the first stable "daily driver").
- **MINOR** — new features, backward-compatible.
- **PATCH** — bug fixes and small tweaks, backward-compatible.

While pre-1.0 we stay on **`0.x`**: minor bumps for features, patch bumps for fixes;
breaking changes are allowed but called out. The version lives in the two
`.csproj` files (`<Version>`) and is shown in the **Settings** dialog (the cog).

**Current: `0.9.0`** — the WPF rebuild has reached WinForms parity plus a large
polish/feature pass. We're in the run-up to 1.0: closing issues and edge cases.

### What 1.0 means here
A **versatile daily driver for timelapse capture**, aimed at artists capturing
their digital work: stable, no known data-loss or capture-correctness bugs, the
core capture/region/encode flow friction-free, and the must-have features below in.
The exact 1.0 feature line is Spike's call — this file is the shortlist to choose from.

---

## Toward 1.0 — candidate features

Ranked roughly by value for the artist use case. None are committed yet.

1. **Window / application capture** *(Spike's biggest want)* — capture a specific
   window instead of a fixed screen rectangle, and **follow it** as it moves/resizes.
   - *Feasibility:* easy version — pick a window (HWND), capture its `GetWindowRect`
     region each tick (re-read each frame so it follows). Robust version —
     **Windows.Graphics.Capture (WGC)** API (Win10 1803+): captures a window even when
     occluded or off-screen, hardware-accelerated; the modern, correct approach.
     `PrintWindow` is a middle option (works for many but not all windows).
   - *Element capture* (a sub-control inside a window) has no general OS API — best
     approximated as a region within a chosen window. Likely out of scope for 1.0.
2. **Hotkeys** — ✅ **global start/stop hotkey done (0.9.x)** (Ctrl+Shift+F9). Still
   wanted: an explicit **pause/resume** distinct from stop, and making the hotkey
   user-configurable.
3. **Auto-encode on stop** (optional) + **frame review/cull** before encoding
   (scrub the frames, delete fumbles) — makes finishing one step. (Frame cull is the
   prerequisite for **clip trimming** / light editing — slated, non-trivial: needs a
   scrub UI + renumber, so the gapped-frame encode edge case matters there.)
4. **Unattended safety** — auto-stop on low disk space or an optional max duration;
   notification when a long run / encode finishes.
5. **Crash recovery** — ✅ **done (0.9.x)**: the Active flag is managed (start/stop),
   and on launch the app offers to resume a session left recording when it died.
6. **Timestamp / elapsed overlay** — ✅ **date/time burn-in done (0.9.x)** (Settings
   toggle). Could extend with elapsed-time or a custom label later.
7. **Advanced encode settings** — a power-user panel to pass extra/custom ffmpeg
   arguments (codec, pix_fmt, extra filters, two-pass, etc.) on top of the simple
   fps/CRF/preset. Good idea for this audience; keep the simple controls as the
   default and tuck the raw-args box behind an "Advanced" toggle. Validate/escape
   args and guard against breaking the image2 input the app relies on.
8. **Multi-monitor / all-screens** capture as a region preset.
9. **Output naming templates** and a chosen encode output path.

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
- **Numeric fields** clamp + show a red border on junk input, but could use the
  target field's inline-hint treatment for consistency.
- **Mixed-DPI**: a single system-DPI is used for all monitors (region/cursor offset
  on mixed-DPI multi-monitor setups).

---

**Maintainer:** Spike (+ Claude) · see `CHANGELOG.md` for released changes.
