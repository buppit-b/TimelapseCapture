# TimelapseCapture - Dark Mode (WinForms)
This is a lightweight WinForms timelapse capture tool optimized for screen capture while streaming.

## Features
- **Region Overlay**: HUD-style overlay showing capture region (Ctrl+R to toggle)
- Dark mode modern look with aerospace aesthetic
- Region selection with aspect ratio presets
- Multi-monitor support
- Session management with custom names
- Interval (seconds) with field lock while capturing
- JPEG or PNG output, JPEG quality control (1-100, default 90)
- Settings persisted to `settings.json` in the app directory
- FFmpeg integration for video encoding

## Build & Run

Requires .NET SDK 9+:
```bash
dotnet build
dotnet run
```

Publish single-file EXE:
```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

## Quick Start Guide

1. **Select Output Folder**: Click "📁 Choose Folder" to set where sessions will be saved
2. **Select Capture Region**: 
   - Click "📐 Select" for manual region selection
   - Click "🖥️ Full Screen" for entire monitor
3. **Create Session**: Click "🆕 New" and give your session a name
4. **Start Capture**: Click "▶ Start Capture"
5. **View Region** (Optional): Press **Ctrl+R** or click "👁 Show" to see capture overlay
6. **Encode Video**: Click "🎬 Encode Video" when done

## Region Overlay

The region overlay displays your capture area with:
- **HUD-style corner brackets** (aerospace aesthetic)
- **Dimensions and position info** displayed in real-time
- **Color-coded border**:
  - 🟢 **Green** = Currently capturing
  - 🔵 **Blue** = Capture stopped
- **Click-through functionality** - doesn't block mouse interaction
- **Fade in/out animations** for smooth transitions

### Using the Overlay

- **Toggle**: Press **Ctrl+R** or click "👁 Show/Hide" button
- **When to use**:
  - Verify capture region placement before starting
  - Check region when loading previous sessions  
  - Visualize capture area across multiple monitors
  - Confirm region after changing monitors or resolution

## Keyboard Shortcuts

- **Ctrl+R**: Toggle region overlay

## Tips

- **Region locking**: Dimensions are locked after first frame captured (ensures video encoding compatibility)
- **Session organization**: Captures save to `captures/{SessionName}/frames/`
- **Encoded videos**: Output saves to `captures/{SessionName}/output/`
- **Multi-monitor**: Full screen button shows menu with all available monitors
- **Aspect ratios**: Use presets (16:9, 4:3, etc.) for consistent framing

## Documentation

- **PROJECT_STATE.md** - Complete project overview and architecture
- **CHANGELOG.md** - Version history and changes
- **BUGFIXES_AND_ROADMAP.md** - Known issues and future plans
- **FFMPEG_DIMENSION_INVESTIGATION.md** - Technical research on encoding
