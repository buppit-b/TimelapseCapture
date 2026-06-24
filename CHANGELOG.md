# Changelog

All notable changes to TimelapseCapture are recorded here. Format follows
[Keep a Changelog](https://keepachangelog.com/); versions follow [SemVer](ROADMAP.md).

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
