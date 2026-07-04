# Changelog

All notable changes to TimelapseCapture are recorded here. Format follows
[Keep a Changelog](https://keepachangelog.com/); versions follow [SemVer](ROADMAP.md).

## [0.9.4] — 2026-07-02 — the 1.0 release candidate

Everything on the 1.0 feature line is in. 1.0 = this RC + a clean multi-hour soak test.

### RC refinements (2026-07-02 → 04, tagged v0.9.4)
- **Presets** — save named capture/encode/look setups and apply them from a dropdown in Settings
  (Save as / Apply / Rename / Delete). A preset deliberately carries your interval, format,
  quality, smart-interval, tracking prefs, encode preset, overlay, and theme — but NOT your output
  folder, ffmpeg path, or safety limits, so applying one never repoints capture at a dead folder
  or silently disables low-disk auto-stop. Stored one JSON per preset under the data dir (same
  format as an exported settings file); four editable built-ins seed on first run. Apply is
  blocked while capturing and warns before a format change on a session with frames.
- **Overlay drag-to-place** — drag the text anywhere on the live preview, with configurable X/Y%
  fields; the four corner presets still work.
- Fixed: the Encode button rendered too tall (an emoji forced a tall emoji font); region select
  "not taking the first time" when launched from the setup wizard (activation/z-order).
- **The app is named Framewright** (display branding; project rename lands at 1.0), with in-app
  credits, an MIT LICENSE, and a rewritten README.
- **Reliability:** single-instance guard (a second copy focuses the first — two instances used to
  silently clobber each other's settings) · never pin a FULLSCREEN window topmost (it blocked
  alt-tab over the whole desktop) + auto-release if the tracked window goes fullscreen · a
  tracked window that hides to the tray or moves to another virtual desktop now pauses/stops
  instead of silently recording what's behind it · transient window-read failures no longer kill
  an unattended run · settings/log/ffmpeg self-select portable vs `%APPDATA%\Framewright`.
- **Encoding:** frame-skip speed-up ("keep 1 frame in every N", non-destructive, Trim-aware) ·
  live encode progress bar + % · CRF slider · mixed-format sessions offer to self-repair
  (convert to the majority format, with consent) · trim start/end shown on the scrubber and
  markers persist per session · cull marks persist, range-mark AND range-unmark, position ticks ·
  frame-precise steppers + keyboard workflow in Trim and Cull.
- **UX:** overlay dialog rebuilt around a live real-size preview with an installed-font picker
  (overlay stays off by default; size safe at any value) · out-of-range entries flash red
  app-wide (one generic behavior) · interval normalize (no more 0.1000000000…) + 3600s ceiling ·
  New Session name prompt · drag a session onto the window / pass it as an exe argument ·
  encoder card layout fixed and the right column made responsive · Settings can never clip its
  footer again · stop-at-a-storage-budget option · themed sliders/ComboBox.
- Tests 33 → **51**; the interactive scenario matrix lives in `docs/QA_CHECKLIST.md`.

### Added
- **Simple mode** — a header toggle that swaps the raw interval for a **speed slider** with
  named notches (Rapid 0.5s … All-day 60s) and a plain-language outcome hint ("every 3s ·
  ≈20 frames/min · a 1-hour session → ~40s video @ 30fps"), and hides the advanced surface.
  Full control stays one toggle away.
- **First-run setup wizard** — folder → what to capture → speed (slider + exact field) →
  one-click FFmpeg download → done. Shows once on first launch; re-runnable from Settings.
- **Frame cull** — scrub the captured frames, mark fumbles, delete them; the rest renumber to a
  gapless sequence so the encode stays exact.
- **Unattended safety completed** — pre-flight + low-disk auto-stop (default on, configurable
  threshold; Stats names the watched drive), opt-in **max-duration cap**, **stop at target**,
  and a **finish notification** (sound + taskbar flash) when a capture auto-stops or an encode
  completes.
- **Custom themed title bars** — the main window and every dialog use the app's own caption
  (native resize/snap/maximize preserved); larger default window so nothing opens cut off.
- **sec ⇄ fps interval toggle** + the outcome hint in Advanced mode; interval clamping is now
  visible (out-of-range entries snap back instead of silently displaying a value the app isn't
  using).
- **"Open log"** button in Settings (observability), **keep-on-top release** hardened against
  recycled window handles.

### Fixed
- **Silent black frames** — a blocked screen grab (UAC prompt, lock screen, RDP disconnect) used
  to save valid all-black frames with no error; now it pauses and resumes automatically.
- **ffmpeg preset injection** hardened (allowlist), mixed JPEG+PNG sessions refuse to encode
  with a clear message, imported settings are re-clamped, huge targets can't overflow the
  stop-at-target math, loaded/foreign session regions are validated to even dimensions.
- Perf for long runs: no double session-file read per frame, O(1) preview lookup, sampled
  frame-size stats, the overlay follow-timer skips no-op relayouts.
- Settings footer buttons could overlap Close; title-bar text could overlap header controls on
  narrow windows; stats text no longer clips.
- Solution builds with **0 warnings** (legacy WinForms noise suppressed in that project only);
  tests 31 → **33**.

[0.9.4]: https://github.com/buppit-b/TimelapseCapture

## [0.9.3] — 2026-06-26

The headline 1.0 feature plus a big reliability + UX pass.

### Added
- **Window / element tracking** — pick a top-level window and capture it; the capture **follows
  the window as it moves**. Frame size is locked when you press Track so frames stay uniform.
  Transit frames are skipped while the window is in motion (no smear). A **Show** outline follows
  the window live. Options (Settings → Window tracking): **on minimize** stop (default) or wait;
  **keep the tracked window on top** while capturing; and **on resize** — *Lock size* (default,
  crops), *Fit* (letterbox-scale the whole window into the frame), or *Stretch*.
- **Capture-failure surfacing** — if frames stop saving (output folder deleted, disk full,
  permissions, tracked window closed), a red banner appears and capture auto-stops after 3
  consecutive failures instead of silently running with a frozen count. Dismissible; full detail
  in the tooltip.
- **Stop at target** — optional auto-stop once the frame count reaches the Target (STATS panel).
- **Overlay editor** — the frame text overlay moved out of Settings into its own **Overlay**
  button in the header, with a token cheat-sheet.
- **Custom output naming** — a filename template for encodes/trims (`{session}` `{date}` `{time}`
  `{datetime}`), Settings → Encoding.
- **Selectable colour themes** working live, **themed scrollbars + checkboxes**, and a clearer
  **"Stay on top"** header toggle.

### Changed
- **Target field** commits on Enter / tab-away (no Set button) with a confirm pulse on the field
  and label.
- The window can be **shrunk freely** — content scrolls when it no longer fits.
- **Capture-affecting options surfaced** on the main UI: *Capture cursor* and *Hide this window
  from captures* (CAPTURE card), *Stay on top* (header).
- **Full Screen** no longer warns when the region is unchanged (a verified no-op).
- New Session confirms before replacing a session that already has frames.

### Fixed
- Colour themes actually switch now (DynamicResource palette + live swap).
- Stats lines wrap instead of clipping in the narrowed right column.

[0.9.3]: https://github.com/buppit-b/TimelapseCapture

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
