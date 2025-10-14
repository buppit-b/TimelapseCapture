# TimelapseCapture - Dark Mode (WinForms)
This is a lightweight WinForms timelapse capture tool optimized for screen capture while streaming.
Features:
- Dark mode modern look
- Region selection
- Interval (seconds) with field lock while capturing
- JPEG or PNG output, JPEG quality control (1-100, default 90)
- Settings persisted to `settings.json` in the app directory

Build & run (requires .NET SDK 9+):
```bash
dotnet build
dotnet run
```

Publish single-file EXE:
```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

