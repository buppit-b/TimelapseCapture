# Changelog

All notable changes to FrameWrite are recorded here. Format follows
[Keep a Changelog](https://keepachangelog.com/); versions follow [SemVer](ROADMAP.md).

## [0.9.5] — 2026-07-13 — the FrameWrite rename

- **Mechanical rename to FrameWrite** — projects, folders, namespaces, the solution, the
  single-instance mutex, and the **shipped exe (now `FrameWrite.exe`)** all go
  `TimelapseCapture*` → `FrameWrite*`. Data dir was already `%APPDATA%\FrameWrite`, so existing
  settings and sessions are unaffected. (The GitHub repo name is unchanged for now.)
- **Legacy WinForms front-end removed** — the original `src/` app, superseded by the WPF rebuild
  and kept only for reference, is gone from the solution (−9.5k lines; recoverable via the
  `pre-framewrite-rename` tag). This also fixed a latent oddity: the test project had been
  referencing the legacy project rather than Core; it now references Core directly.
- **Soak #1 substantively passed** (2026-07-12): a 5.5-hour unattended run, 6798 gapless uniform
  frames, clean recording-timer auto-stop, quiet log, and a verified end-to-end encode. Memory
  flatness pending a heartbeat-logged run; then 1.0 is a version bump.
- The recording-interval is now recorded accurately in `session.json` (a sub-second-capable
  field; the picker shows it).
- **Developer mode** — a hidden power-user switch (click the version line in Settings five times)
  that relaxes the safe limits for edge testing: it lowers the interval floor from 0.1s to 0.01s
  (up to 100 fps) and skips the configurable low-disk auto-stop and storage-rate warning. A
  persistent red banner shows while it's on, and a **hard 256 MB emergency disk floor still holds
  even in developer mode** — the one limit it can't switch off. Toggle it back off from Settings;
  leaving it re-clamps any sub-0.1s interval.

## [0.9.4] — 2026-07-02 — the 1.0 release candidate

Everything on the 1.0 feature line is in. 1.0 = this RC + a clean multi-hour soak test
*(2026-07-10: soak no longer gates development — it runs when Spike has the hours)*.

### RC refinements (2026-07-08 → 12)
- **Capture cadence sparkline** — a small live line graph at the top of Stats shows frames/min
  over the last couple of minutes: the capture's heartbeat. It peaks while you're working and
  dips into valleys where Smart Interval slowed or skipped, so a glance confirms an unattended
  run is actually capturing at the expected rate. Dependency-free (drawn on a Canvas), EMA-
  smoothed so it stays readable at any interval, freezes when stopped and resumes on restart.
- **Encode to an exact length** — the encode panel gains an fps ⇄ "exact length" toggle: pick a
  target like 60s and FrameWrite computes the playback fps from however many frames actually
  encode (a trim range counts too), so the finished video lands on the length you asked for —
  handy for platform time limits. Very long sessions clamp to 240 fps (they come out a bit
  longer rather than unplayable), and the video-length line shows the fps it worked out. Proven
  end-to-end against real ffmpeg.
- **ffmpeg lifecycle hardening** — closing the app mid-encode used to leave an **invisible
  ffmpeg still running** (and writing) after exit; a real close now asks first (an encode is
  minutes of work) and cancelling kills the process before the app exits — proven by an
  integration test that cancels a genuinely running ffmpeg. Also fixed: the PATH fallback in
  the ffmpeg lookup ignored its own timeout and could block the UI thread indefinitely on a
  hung `where` probe.
- **Cloud-sync / antivirus resilience** (hostile-filesystem pass) — the per-frame count update
  now retries briefly (~80ms worst case) when session.json is momentarily locked by a sync
  client or AV scanner. Before: a blip on the read struck toward auto-stop with a misleading
  "session missing" message, and a silently failed WRITE made the next tick reload the old
  count and **overwrite the frame just captured**. The failure message now names the lock
  cause; three lock-contract tests pin the behaviour (settings + log writes were already safe).
- **Region select/edit on mixed-DPI monitors** — both full-screen overlays are now placed in
  raw physical pixels and map every drag point through the window's real per-monitor transform
  (PointToScreen) instead of the system DPI scale. On desktops with differently-scaled monitors
  the overlay used to under/over-cover the far screen and selections could land offset — the
  same class of bug the outline had. The live dims label now shows exact physical pixels.
  *(Needs a multi-monitor hands-on verify, like the outline fix.)*
- **MainViewModel split into 11 partial classes** by concern (core/State/Target/Stats/Session/
  Prefs/Hotkeys/SettingsOps/Region/Capture/Encode) — a pure mechanical move; navigation and
  future feature placement get dramatically easier.
- **Hardening round 2** (long-run + hostile-input sweep) — frame files now sort **numerically**:
  past frame 99,999 the names grow a sixth digit and the old string sort put "100000" before
  "99999", which would scramble preview/trim/**cull** (destructive!) on very long runs; ffmpeg
  itself handles 6+ digits fine, so 100k+ sessions stay encodable. The **Run clock** now shows
  the run's active time (it used to reset to 0:00 at every resume, and now holds the final time
  after a stop). The loupe's **1:1 is now true pixel-for-pixel** on scaled monitors (100% used
  to mean 150%-soft on a 150% display; re-baselines when dragged across monitors). And a
  corrupt/hand-edited session.json with negative counters is clamped at load — a negative
  frame count would have produced unencodable "-0004.jpg" filenames.
- **Hardening pass** (self-audit of the recent arc) — fixed: presets carried the NEW keymap,
  dismissed-confirmation list, and panel fold state (applying one could silently rebind your
  hotkeys — now excluded on both save and apply, contract-tested) · imported settings left
  hotkey bindings and hide-from-capture inert until restart · renaming / switching sessions or
  the output folder was possible DURING a bake/backup/cull (the rename moves the folder out
  from under the running rewrite — now gated) · the region-select hotkey could stack a second
  full-screen picker over an open one · a failed RegisterHotKey (combo owned by another app)
  was silent — Settings now shows exactly which binding didn't take · clearing a numeric box
  and tabbing away left it empty while the old value silently lived on (all numeric boxes now
  restore on blur) · 8-digit hex was accepted by the overlay colour box but silently ignored
  by the renderer.
- **Provenance metadata** — every encode/trim now carries open metadata tags
  (encoder "FrameWrite x.y.z" + a comment), readable in ffprobe/MediaInfo/file properties.
  Non-destructive, never touches the picture; verified against a real encode. (ROADMAP item 10
  approach 1; the optional visible watermark remains a separate, off-by-default idea.)
- **Cull gets the backup choice** — deleting fumble frames now offers the same
  "Back up, then delete" primary option as bake/crop, and the delete + renumber pass runs off
  the UI thread behind the busy flag with progress in the status line.
- **Overlay nudge keys** — after dragging the overlay text into place, arrow keys move it by
  1% of the frame and Shift+arrows by 0.1% — the precision the drag can't give (your cursor
  covers the text). Inert while a text field has focus.
- **The keymap** — Settings → HOTKEYS is now a table of rebindable global actions:
  **Start/Stop**, **Pause/Resume** (new), and **Select region** (new — opens the region picker
  from anywhere, even with the app minimized or in the tray). Click a box, press a combination;
  duplicate combos are caught with a clear message; Clear unbinds an action. Your existing
  start/stop binding migrates automatically, and the Start/Stop tooltips now advertise the
  live binding instead of a hard-coded one.
- **Fixed: closing the app left the region outline on screen** — the outline is an unowned
  window (so it can survive minimize-to-tray), and with WPF's default shutdown mode it kept
  the whole PROCESS alive as an invisible zombie plus an orphan outline. Two-layer fix: the
  app now explicitly closes the outline on exit, and the shutdown mode is tied to the main
  window so no stray window can ever keep FrameWrite running after you close it.
- **The GO strip** — Start / Stop / Pause moved out of the capture card onto their own strip
  at the end of the left column's workflow (session → area → speed → GO), with a larger Start,
  the live frame count as a bold readout, and the status line + capture error banner riding
  along. Kept deliberately card-quiet (lighter fill, no accent outline) so it doesn't fight
  the rest of the window. The capture card is now purely settings.
- **Region flip acts immediately** — the ⇄ beside the ratio lock now rotates the CURRENT
  selection 90° about its centre (watch the outline move), exactly like the Crop dialog's
  flip, as well as setting the orientation for the next drag — no re-select needed to see it
  work. Relocates on-screen if the flip pokes past an edge; mid-session it passes through the
  same (suppressible) scale confirmation as every other region change.
- **Session name shown once** — the SESSION card is now the single home of the session's
  identity: the name sits above the New/Load/Open actions (semibold, click to rename, hover
  for the full name), doubling as the loaded-state cue. The title-bar copy was dropped —
  too easy to miss as the only display, and removing it decongests the caption row.
- **One-click backup before destructive ops** — the bake-overlay and crop-frames-on-disk
  confirmations now offer **"Back up, then …"** right in the dialog (instead of just advising a
  copy): the session's frames + session.json are copied to a sibling
  "NAME (backup date time)" folder — with free-space pre-check, capture times preserved, videos
  (output/) skipped, and the copy's Active flag cleared so crash recovery ignores it. The backup
  is loadable from the session picker like any session. A failed backup aborts the operation
  before anything is touched.
- **Stuck-green stats fixed** — rapid target changes (e.g. wheel-scrolling) could interrupt the
  green "recalculated" pulse mid-flight and leave values green forever (killing the cue for the
  next change). The pulse now always settles back to the base colour.
- **Frame viewer (the loupe)** — click the Preview thumbnail to open a floating, resizable
  inspector: **scroll to zoom at the cursor** (crisp 1:1 pixels when magnified), drag to pan,
  double-click for fit ↔ 100%, and **scrub through the whole session** (slider, ±1/±10
  steppers, wheel) while the zoom stays put — compare the same detail across frames. Shows
  each frame's size and real capture time; a refresh button picks up frames captured since
  it opened. Non-modal — open several to compare frames side by side.
- **Main surface slimmed** (layout-reshape slice 1) — the encode tuning cluster (fps · CRF ·
  preset · speed-up · end-hold) and the Smart-interval settings now **fold away** behind
  section headers, each leaving a one-line summary ("30 fps · CRF 23 · Medium";
  "on — skips frames after 60s idle") so nothing goes dark. Collapsed by default, state
  persists, and the live Active/Idle status stays visible even when folded. The encoder card
  at rest is now: FFmpeg line · Encode button · Trim/Cull/Crop.
- **Hue-picker fix** — clicking the colour chip no longer opens-then-instantly-closes the
  popup (a WPF light-dismiss race: opening on mouse-down let the same click's release dismiss
  it); clicking the chip while open now cleanly closes it too.
- **Stats panel rebuilt** (UI-arc opener) — the emoji text blob is now clean **icon · label ·
  value rows** (proper Segoe MDL2 glyphs): Video length leads as the anchor, Disk rate and Free
  space carry their warning states, and the detail rows (frame size, on-disk, at-target
  projection, memory, total elapsed) are **advanced-only, so Simple mode shows a calm panel** —
  target, video, rate, free space, run time. One structured `GetStorageStats` computation feeds
  the rows (unit-tested), replacing string parsing.
- **Hue-box colour picker** — the overlay colour rows gained a compact picker chip: a popup
  saturation/value square + hue strip, applying live while you drag (the preview follows).
- **Overlay colour & opacity** — the overlay text and its backdrop box each get a colour
  (swatches or hex) and an opacity (0–100%); backdrop at 0% removes the box entirely. Defaults
  match the old look exactly (white text, black box ~59%). Applies live at capture, in the
  dialog preview, and through the retroactive bake — one shared renderer.
- **Target rework** — the "30s"-style unit box is now **three wheelable h / m / s fields**
  (overflow normalizes: 90m reads back as 1h 30m; commits as you go, no Set step). And the
  target now has **two kinds**: *Video length* (the original frames goal) or *Timer* — record
  for that much time, then stop automatically. The timer counts **active recording only**
  (pause suspends it, which finally gives pause a real job) and stopping early asks first.
- **"Don't ask me again"** — repeat-prone confirmations (mid-session region change, stopping
  an armed timer) offer a persistent opt-out; destructive consents (cull, crop-on-disk, bake)
  never do. Settings gains **"Ask everything again"** to restore them all.
- **FFmpeg card tidied** — the stuck "FFmpeg already installed" status now settles back to
  Ready (it was a dispatcher race), and once ffmpeg is Ready the Download/Browse row folds
  away behind a slim "Change…" link.
- **Interval hint** — the advanced interval row no longer flashes Simple-mode speed names
  (Fine/Standard/…) as you scroll through values; names stay in Simple mode and the wizard.
- **Retroactive overlay bake** — Overlay dialog → "Bake into frames…" burns the overlay into
  every frame already on disk, for when you forgot to enable it before capturing. The trick:
  timestamp tokens resolve from each frame FILE's own write time (= its capture moment), so past
  frames get their **real** times, not today's — and the bake preserves those write times through
  the rewrite so the record survives. Destructive with explicit consent (crash-safe temp+replace,
  progress in the status line). The other on-disk rewrites (destructive crop, format convert) now
  preserve frame write times too. Also hardened: capture can no longer start (incl. via
  hotkey/tray) while an encode or an on-disk rewrite is running.
- **Scroll-wheel stepping everywhere** — hover any numeric field and wheel to adjust it
  (interval, quality, fps, CRF, overlay size/position, crop X/Y/W/H, safety limits, …):
  ±1 per notch, **Shift = ×10**, **Ctrl = fine 0.1** on decimal fields; values commit and
  clamp live. Sliders step too — including the Trim/Cull/Crop scrubbers, so the wheel now
  scrubs frame-by-frame. (Also fixed en route: integer fields never had their typing/paste
  filter attached — a WPF callback quirk when a property is explicitly set to its default.)
- **The name is FrameWrite** (settled 2026-07-10, "for now") — all display branding, dialog
  titles, tray strings, and the data dir (`%APPDATA%\FrameWrite`) renamed from Framewright;
  project/namespace rename still lands at the 1.0 cut.
- **Release packaging** — `scripts/publish-release.ps1` builds a self-contained single-file
  `dist/FrameWrite-v{version}-win-x64.zip` (no .NET needed on the target machine; FFmpeg still
  downloaded on demand). Verified: publishes clean, launches, uses the installed-mode data dir.
- **Canonical frame size** — the frames already on disk now define a session's frame size, and
  *every* capture source (static region, mid-session region change, tracked window, post-crop
  capture) is scaled/letterboxed to it automatically. Closes the "saved region doesn't fit this
  display" dead-end (art-tablet/second-monitor gone): reload on any machine, capture continues,
  frames stay uniform, encode stays safe. The region row shows a "→ scaled to W×H" suffix
  whenever scaling is active, and the mid-session change warning now promises scaling instead
  of threatening mixed sizes.
- **Crop: frame scrubber + clean flip** — the Crop dialog gained the same scrub-through-frames
  bar as Trim/Cull (slider + ±1/±10 steppers), and the ratio **flip toggle (⇄)** now transposes
  the current selection cleanly about its center (works on Free too). Region select has the same
  flip toggle; picking Track Window resets the ratio to Free to reflect reality.
- **Region overlay on mixed-DPI monitors** — the outline is now positioned in raw physical
  pixels via `SetWindowPos` + per-monitor DPI, fixing the offset outline on a second monitor
  with different scaling *(needs a multi-monitor verify)*.
- **Window layout fixes** — long region/status text finally wraps (the ScrollViewer was
  measuring content at infinite width, silently disabling wrapping app-wide); min/max/close
  caption buttons can no longer be pushed off-screen when the window is squashed (session name
  ellipsizes, Simple/Stay-on-top hide below 660px).
- Known OS limit recorded: with "hide this window from capture" on, overlapping the capture area
  can produce a **black box** (GDI reads `WDA_EXCLUDEFROMCAPTURE` windows as black) — WGC
  (tracking slice 2) is the real fix; meanwhile keep the app off the captured area.
- Tests 51 → **68**.

### RC refinements (2026-07-02 → 04, tagged v0.9.4)
- **System tray** — a tray icon shows capture status at a glance (green stopped, red-ring dot +
  frame count while recording, **amber** while idle/paused); double-click restores, right-click
  menu has Show / Start-Stop / Exit; "Minimize to tray" (default on) hides the window from the
  taskbar; optional close-to-tray; a balloon announces a finished capture while you're minimized.
  (Uses in-SDK WinForms NotifyIcon — no new dependency.)
- **Header capture-state pill** — red REC (pulsing) while actively grabbing, amber IDLE/PAUSED for
  smart inactivity or pause, with the smart-interval detail beside it.
- **Cross-feature oversight sweep** (multi-agent) fixed a batch of small state bugs: New/Load
  Session left the region overlay showing and inherited a stale tracked window; keep-tracked-window-
  on-top was a no-op mid-capture (and could strand a window topmost); stop-at-target ignored the
  frame-skip factor (stopped N× early); auto-stops could fire while paused; the target and encode
  fps/CRF weren't reset on Restore-defaults / carried across sessions; preset/import/restore didn't
  refresh the stats readouts. **EncodeFps/EncodeCrf now persist** (were VM-only, lost on restart)
  and travel in presets. New Session now always yields a clean, freshly-named session (it used to
  look like a rename when the current one was empty).
- **Themed tooltips** — long tooltips wrap into a dark multi-line card instead of one huge line;
  optional start/stop audio cue.
- **Themed popups** — every confirmation/warning now renders in the app's dark chrome instead of
  the native Windows message box.
- Fixed: couldn't restart a session that had already hit its stop-at-target/max-duration/storage
  limit (it started then auto-stopped ~0.5s later with the finish sound) — Start now pre-flights
  those and explains; every auto-stop logs its reason. Default theme is now Synth; "Restore
  defaults" button; hide-window-while-selecting-a-region (default on); dropped the preloaded
  built-in presets (users make their own).
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
- **The app is named FrameWrite** (display branding; project rename lands at 1.0), with in-app
  credits, an MIT LICENSE, and a rewritten README.
- **Reliability:** single-instance guard (a second copy focuses the first — two instances used to
  silently clobber each other's settings) · never pin a FULLSCREEN window topmost (it blocked
  alt-tab over the whole desktop) + auto-release if the tracked window goes fullscreen · a
  tracked window that hides to the tray or moves to another virtual desktop now pauses/stops
  instead of silently recording what's behind it · transient window-read failures no longer kill
  an unattended run · settings/log/ffmpeg self-select portable vs `%APPDATA%\FrameWrite`.
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
- **WPF/MVVM front-end** on the shared `FrameWrite.Core` engine (the active app).
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
