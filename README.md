# Timelapse Capture

Timelapse Capture is a desktop application for recording screen timelapses. It captures frames at fixed intervals and compiles them into a video.

## Overview

Useful for recording digital work sessions such as painting, modeling, or programming, without the overhead of continuous video capture.

## Features

- **Region Selection** – Capture a specific area, window, or the full screen.  
- **Overlay** – Optional on-screen indicator showing the active capture region.  
- **Session Management** – Organizes captures into session folders with custom names.  
- **Frame Encoding** – Uses FFmpeg to compile captured frames into a video file.

## Requirements

- Windows 10 or 11  
- [.NET 9 SDK](https://dotnet.microsoft.com/en-us/download) (for building from source)

## Quick Start

1. Choose an output folder.  
2. Select a capture region (canvas, window, or full screen).  
3. Create a session and start capture.  
4. Frames are captured automatically.  
5. Encode the session to produce a video.

## Building From Source

```bash
dotnet build
dotnet run
```

For a standalone executable:

```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

## Keyboard Shortcuts

| Shortcut | Action |
|-----------|--------|
| `Ctrl + R` | Toggle capture region overlay |

## How It Works

Each session saves image frames in its own directory. The built-in encoder uses FFmpeg to compile them into a timelapse video. Frames remain accessible for manual re-encoding or further processing.
