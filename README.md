# FrameWrite

**A timelapse studio for digital artists.** FrameWrite captures frames of your screen — a
region, a monitor, or a window it follows around — on a timer, then turns them into a timelapse
video. Built for long, often-unattended capture sessions: it protects your frames, tells you
when something goes wrong, and stays out of your way while you work.

> *A wright is a craftsman — a playwright works in plays, a shipwright in ships.
> FrameWrite works in frames.*

## Highlights

- **Window tracking** — pick a window and FrameWrite follows it as you move it; resize
  handling (lock / fit / stretch), minimize handling (stop or wait), optional keep-on-top.
- **Set-and-forget safety** — pre-flight disk check, low-disk auto-stop, capture-failure
  auto-stop, optional max-duration and storage-budget caps, crash recovery, and a finish
  notification. A locked screen or UAC prompt pauses capture instead of recording black frames.
- **The always-there recorder** — optionally launch at Windows sign-in and start capturing
  immediately (continues your most recent session), so a work session can never be forgotten.
- **Smart interval** — capture at full speed while you work, slow down or skip while you're idle.
- **A real editing pass before encoding** — trim to a frame range (markers persist), cull fumble
  frames (range marking, gapless renumber), non-destructive crop, speed-up (keep 1 frame in
  every N), end-frame hold, live encode progress, custom output naming. The crop/cull/trim
  dialogs zoom to the pixel (wheel zoom, middle-drag pan).
- **MP4, WebM and GIF export** — H.264 for everywhere, VP9 for smaller files, palette-optimized
  GIF with its own tuning (colors, dither, rate/width caps). Encode at a fixed fps or to an
  exact output length.
- **Combine sessions into one video** — tick sessions in a staging dialog, prep each (cull/crop),
  and encode one continuous timelapse; different sizes letterbox onto a shared canvas. Or
  **merge sessions into one continuable session** (move = no extra disk, or copy).
- **Archive finished sessions** — pack a session's frames into one verified video file, typically
  5–15× smaller; fully reversible (PNG sessions restore pixel-identical). The session picker
  shows per-session size on disk and sorts by date, name, frames or size.
- **Configurable frame overlay** — burn a timestamp or label into frames, previewed live at
  real frame size with an installed-font picker. Off by default.
- **Simple mode + a first-run wizard** for the "open the app and press go" experience; the full
  surface is one toggle away. Global hotkeys (rebindable), tray control (start/stop/pause), and
  every sound individually optional.
- **Clean dark terminal aesthetic** with live-switchable themes and custom window chrome.

## Building from source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/en-us/download), Windows 10/11.

```bash
dotnet build FrameWrite.sln            # build everything (0 warnings expected)
dotnet test  FrameWrite.sln            # run the test suite
dotnet run --project FrameWrite.Wpf    # launch the app
```

For a standalone executable:

```bash
dotnet publish FrameWrite.Wpf -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

**Where data lives:** settings, the log, and the downloaded FFmpeg go to `%APPDATA%\FrameWrite`.
Prefer a self-contained folder (USB stick)? Place a `settings.json` next to the exe and
FrameWrite keeps everything there — portable mode. Captured sessions always live in the output
folder you choose.

## Video encoding

FrameWrite encodes video by invoking **FFmpeg** as a separate program. It is not bundled: the
app offers a one-click download of [BtbN's FFmpeg build](https://github.com/BtbN/FFmpeg-Builds)
(GPL-licensed, its own terms), or you can point it at any `ffmpeg.exe` you already have.

## License & credits

FrameWrite is [MIT-licensed](LICENSE).

Created by **Spike Tickner** · video by **FFmpeg**.
