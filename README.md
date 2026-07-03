# Framewright

**A timelapse studio for digital artists.** Framewright captures frames of your screen — a
region, a monitor, or a window it follows around — on a timer, then turns them into a timelapse
video. Built for long, often-unattended capture sessions: it protects your frames, tells you
when something goes wrong, and stays out of your way while you work.

> *A wright is a craftsman — a playwright works in plays, a shipwright in ships.
> Framewright works in frames.*

## Highlights

- **Window tracking** — pick a window and Framewright follows it as you move it; resize
  handling (lock / fit / stretch), minimize handling (stop or wait), optional keep-on-top.
- **Set-and-forget safety** — pre-flight disk check, low-disk auto-stop, capture-failure
  auto-stop, optional max-duration and storage-budget caps, crash recovery, and a finish
  notification. A locked screen or UAC prompt pauses capture instead of recording black frames.
- **Smart interval** — capture at full speed while you work, slow down or skip while you're idle.
- **A real editing pass before encoding** — trim to a frame range (markers persist), cull fumble
  frames (range marking, gapless renumber), speed-up (keep 1 frame in every N), live encode
  progress, custom output naming.
- **Configurable frame overlay** — burn a timestamp or label into frames, previewed live at
  real frame size with an installed-font picker. Off by default.
- **Simple mode + a first-run wizard** for the "open the app and press go" experience; the full
  surface is one toggle away.
- **Clean dark terminal aesthetic** with live-switchable themes and custom window chrome.

## Building from source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/en-us/download), Windows 10/11.

```bash
dotnet build TimelapseCapture.sln            # build everything (0 warnings expected)
dotnet test  TimelapseCapture.sln            # run the test suite
dotnet run --project TimelapseCapture.Wpf    # launch the app
```

For a standalone executable:

```bash
dotnet publish TimelapseCapture.Wpf -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

(The solution and projects still carry the working name `TimelapseCapture`; the mechanical
rename to Framewright lands with 1.0.)

**Where data lives:** settings, the log, and the downloaded FFmpeg go to `%APPDATA%\Framewright`.
Prefer a self-contained folder (USB stick)? Place a `settings.json` next to the exe and
Framewright keeps everything there — portable mode. Captured sessions always live in the output
folder you choose.

## Video encoding

Framewright encodes video by invoking **FFmpeg** as a separate program. It is not bundled: the
app offers a one-click download of [BtbN's FFmpeg build](https://github.com/BtbN/FFmpeg-Builds)
(GPL-licensed, its own terms), or you can point it at any `ffmpeg.exe` you already have.

## License & credits

Framewright is [MIT-licensed](LICENSE).

Created and directed by **Spike Tickner** · engineered with **Claude** (Anthropic) · video by
**FFmpeg**.
