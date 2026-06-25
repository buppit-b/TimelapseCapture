# Changelog

All notable changes to TimelapseCapture are recorded here. Format follows
[Keep a Changelog](https://keepachangelog.com/); versions follow [SemVer](ROADMAP.md).

## [0.9.2] — 2026-06-26

### Added
- **Clip trimming** — a **Trim…** button on the ENCODER opens a scrubber over the captured
  frames (live preview); set a start and end frame and encode only that range. Trims by frame
  range straight from the frames (image2 `-start_number`/`-frames:v`) — no intermediate video
  and no re-encode, so there's no quality loss.
- **Hide window from screen capture** (Settings → Window) — excludes the Timelapse Capture
  window from screen captures via `SetWindowDisplayAffinity`, so it never lands in a frame.

### Changed
- **Settings reorganized into collapsible sections** (Appearance / Window / Capture / Encoding /
  Hotkey); the hotkey field now prompts you to press a combination, so it's discoverable.

### Fixed
- **Colour themes now actually switch** — palette brushes resolve via `DynamicResource` and the
  theme manager swaps them live (theme changes were silently no-ops before).

[0.9.2]: https://github.com/buppit-b/TimelapseCapture

## [0.9.1] — 2026-06-25

A big feature + hardening pass toward 1.0.

### Added
- **Crash recovery** — sessions track an Active flag; on launch the app offers to resume
  one that was still recording when it last closed.
- **Global start/stop hotkey** — opt-in and user-configurable in Settings (key-capture field).
- **Pause / resume** capture (keeps the run armed; settings stay locked while paused).
- **Customizable frame overlay** — text with tokens (`{datetime}`/`{date}`/`{time}`/`{t:FORMAT}`)
  + literal text, corner position, font family and size (replaces the fixed timestamp stamp).
- **Optional mouse-cursor capture** in frames.
- **Multi-monitor full-screen picker** (shows each monitor's resolution + ratio).
- **Settings export/import**, **open-folder-after-encode**, and **selectable colour themes**
  (Terminal / Ocean / Ember / Synth / Light, applied live).
- Numeric-only input on the number fields; stats flash on target/fps change; ENCODER card
  (capture/encode split) with an fps-aware projection.

### Fixed
- **FFmpeg downloads ~160× faster** — from BtbN's GitHub-CDN build instead of throttled gyan.dev.
- Encode hardened: try/finally so the button can't stick, partial `.mp4` cleaned on cancel/fail,
  unique output filenames, actual output size shown.
- Clean shutdown stops capture; capture-tick re-entrancy dropped (no pile-up on a slow disk);
  New-Session spam guard; ffmpeg-browse validation; New-Session pulse now stops once a session
  exists; Stop button turns red while capturing.
- CI simplified to one reliable build+test job (stopped the false "failed" emails).

[0.9.1]: https://github.com/buppit-b/TimelapseCapture

## [0.9.0] — 2026-06-24

The WPF rebuild at WinForms parity plus a large polish + feature pass. First
tagged version on the road to a stable 1.0 daily driver.

### Added
- **WPF/MVVM front-end** on the shared `TimelapseCapture.Core` engine (the active app).
- In-app **session picker** with name · date · frame count · region size and **thumbnails**.
- **Encode preset** (Fast/Medium/Slow) and **aspect-ratio lock** (Free/16:9/4:3/1:1/9:16)
  via a reusable dark segmented control.
- **Editable region overlay** — drag to move, 8 resize handles; corners keep the locked
  ratio (Shift frees), edges free.
- **Live preview** of the latest frame and a pulsing red **● REC** indicator.
- **Rename session** from the header (also renames the session folder).
- **Settings cog**: *keep window always on top*, **capture the mouse cursor in frames**,
  and the app version.
- Working **JPEG quality**, **cancel encode**, stats **% progress** + **Run/Total** elapsed
  (Total persists), mid-session region/format change warnings, tooltips throughout.

### Fixed
- **Encode dropped frames** — switched ffmpeg concat → image2 demuxer (165 in → 165 out).
- **Smart interval** corrected: the main Interval is the working rate; idle only slows
  (or skips), never captures faster — and now **resumes within one working interval**
  on renewed activity instead of waiting out the whole idle interval.
- **Capture region now persists** with the session and is restored on load; if the saved
  spot is gone (monitor changed), it's relocated at the same size onto a current monitor.
- Load Session crash (read-only `Run.Text` TwoWay binding).
- Sub-second interval; JPEG quality actually applying; ContextMenu dispose race.

### Notes
- See `ROADMAP.md` for candidate 1.0 features (window/app capture, pause/hotkeys,
  auto-encode, crash recovery, …) and known issues.

[0.9.0]: https://github.com/buppit-b/TimelapseCapture
