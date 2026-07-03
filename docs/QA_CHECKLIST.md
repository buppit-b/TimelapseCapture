# QA Checklist — interactive scenarios automated tests can't reach

`dotnet test` covers pure logic only (33 tests). The bugs that actually bite — fullscreen games,
lock screens, multi-monitor, DPI, drag interactions — only show up live. This is the scenario
matrix for hands-on passes: run the relevant sections before a release and the whole thing before
a MAJOR one. Every entry here is either a shipped-bug regression check (marked ⚠) or a scenario
reasoned to be risky.

## Capture basics
- [ ] Region select / edit (drag + all 8 handles) / full screen, on each monitor
- [ ] Start → count climbs → stop → encode → video plays, frame count exact
- [ ] Sub-second interval (0.5s) sustained 10+ min — no drift, UI responsive
- [ ] JPEG and PNG sessions each encode; JPEG quality visibly applies ⚠
- [ ] Cursor capture on/off; overlay text renders in the chosen corner with tokens resolved

## Window tracking
- [ ] Track a normal window; drag it around → capture follows, no transit smear ⚠
- [ ] Resize modes: Lock (crops), Fit (letterbox), Stretch — resize AND maximize under each
- [ ] Minimize with stop mode → banner + auto-stop; with wait mode → holds, resumes on restore
- [ ] Close the tracked window mid-capture → banner + auto-stop ⚠
- [ ] Keep-on-top: windowed target stays above others; released on Stop AND on Pause ⚠
- [ ] **Keep-on-top + fullscreen game → pin is SKIPPED; alt-tab still works** ⚠ (0.9.4 lockup bug)
- [ ] Windowed game toggled to fullscreen mid-capture → pin auto-releases within ~2s
- [ ] Track a window on a second / different-DPI monitor; drag it across monitors
- [ ] Tracked app closes to TRAY (e.g. Discord/Telegram ✕) → treated as hidden: stop or hold ⚠
- [ ] Move the tracked window to another VIRTUAL DESKTOP → treated as hidden, not wrong pixels ⚠
- [ ] Picker lists neither cloaked windows (other desktops) nor this app's own windows ⚠
- [ ] Exclusive-fullscreen DirectX game: KNOWN LIMIT — BitBlt may record black; use borderless
      (fix = WGC, tracking slice 2)

## Unattended safety
- [ ] Lock screen or UAC prompt mid-capture → frames pause + resume, none black ⚠ (silent-black bug)
- [ ] Low-disk auto-stop fires (set the threshold just below current free space); pre-flight warns
- [ ] Max-duration stop · stop-at-target · each plays the finish notification
- [ ] Delete the session/output folder mid-capture → banner within ~3 ticks + auto-stop ⚠
- [ ] Kill the app mid-capture (Task Manager) → relaunch offers to resume the session

## Sessions & files
- [ ] New Session prompts for a name (accept prefill / type custom / cancel aborts)
- [ ] New Session on an empty session renames it instead of spawning a folder
- [ ] Rename via the header name; weird characters sanitised in the folder, verbatim in display
- [ ] Load via picker · drag a session folder onto the window · pass a path as an exe argument
- [ ] Cull marks + deletes + renumbers; encode after cull is exact
- [ ] Trim range and "Clip to target"; trim/cull markers survive close-and-reopen ⚠; a cull
      clears both marker sets
- [ ] Speed-up (keep 1 in N): video is N× faster; works combined with a Trim range ⚠
- [ ] Mixed JPEG+PNG session → encode offers to convert; converted session encodes clean ⚠
- [ ] Fresh install (no exe-side settings.json) writes to %APPDATA%\Framewright; dev/portable
      layout (settings.json beside exe) stays put ⚠
- [ ] Deleted output folder shows the red warning line; Choose… opens at nearest surviving folder ⚠
- [ ] Launching a second copy just focuses the first ⚠ (settings-clobber bug)

## UI / display
- [ ] Every dialog: themed chrome, draggable, ✕ closes; main window maximize leaves taskbar visible
- [ ] Aero-snap + Win+arrows; narrow window (title hides, controls never overlap ⚠); shrink-to-scroll
- [ ] Live theme switch including open dialogs; Simple ⇄ Advanced round-trip
- [ ] Interval `0.01` (and fps `99`) → snaps back with a red flash, in main window AND wizard ⚠
- [ ] Wizard full walk-through incl. the FFmpeg download step; re-run from Settings
- [ ] Hotkey works with the window minimized; finish notification flashes the taskbar

## Long-run soak — the 1.0 gate
- [ ] 6–8 h unattended run: memory ≈ flat, count ≈ elapsed/interval, `debug.log` quiet, encode clean

---
**Last full pass:** _never — run one before 1.0._ · Log results here (date · build · fails).
