# Builds a distributable FrameWrite release: a self-contained single-file exe (no .NET install
# needed on the target machine) zipped with the README and LICENSE.
#
#   powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1
#
# Output: dist\FrameWrite-v{version}-win-x64.zip
# Notes:
#  - FFmpeg is deliberately NOT bundled (it's GPL and ~100 MB) — the app offers a one-click
#    download on first use, or the user points it at an existing ffmpeg.exe.
#  - A fresh machine (no settings.json beside the exe) stores data in %APPDATA%\FrameWrite;
#    placing a settings.json next to the exe makes it portable instead.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Version from the WPF project file.
$csproj = Join-Path $root "FrameWrite.Wpf\FrameWrite.Wpf.csproj"
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Couldn't read <Version> from $csproj" }
Write-Host "Publishing FrameWrite v$version (win-x64, self-contained, single file)..."

# Publish: one exe, natives embedded, compressed.
dotnet publish "FrameWrite.Wpf" -c Release -r win-x64 --self-contained true `
    /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true -o "dist\publish" --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Stage the zip contents: the exe + docs. (The publish folder may contain a .pdb — skip it.)
# The published exe is FrameWrite.exe directly now (AssemblyName=FrameWrite) — no rename needed.
$stage = "dist\stage"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage -Confirm:$false }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item "dist\publish\FrameWrite.exe" (Join-Path $stage "FrameWrite.exe")
Copy-Item "README.md", "LICENSE" $stage

$zip = "dist\FrameWrite-v$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item -Force $zip -Confirm:$false }
Compress-Archive -Path "$stage\*" -DestinationPath $zip

$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ""
Write-Host "Done: $zip ($size MB)"
Write-Host "Contents: FrameWrite.exe (self-contained), README.md, LICENSE"
