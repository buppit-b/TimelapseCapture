# ============================================================================
# TimelapseCapture Directory Cleanup Script
# ============================================================================
# This script reorganizes the project into a clean, professional structure
# 
# SAFETY: Creates a git checkpoint before making any changes
# ============================================================================

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "TimelapseCapture Directory Cleanup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if we're in the right directory
if (-not (Test-Path "TimelapseCapture.csproj")) {
    Write-Host "ERROR: Must run from TimelapseCapture root directory" -ForegroundColor Red
    exit 1
}

Write-Host "Current directory structure:" -ForegroundColor Yellow
$itemCount = (Get-ChildItem -Force | Where-Object { $_.Name -ne ".git" }).Count
Write-Host "  Items in root: $itemCount" -ForegroundColor White
Write-Host ""

# Safety checkpoint
Write-Host "SAFETY CHECK:" -ForegroundColor Yellow
Write-Host "This script will reorganize your directory structure." -ForegroundColor White
Write-Host ""
Write-Host "Before proceeding, we'll create a Git checkpoint." -ForegroundColor White
Write-Host "You can undo all changes with: git reset --hard HEAD" -ForegroundColor Cyan
Write-Host ""

$response = Read-Host "Continue with cleanup? (y/N)"
if ($response -ne 'y' -and $response -ne 'Y') {
    Write-Host "Cleanup cancelled" -ForegroundColor Yellow
    exit 0
}

# Create git checkpoint
Write-Host ""
Write-Host "Creating Git checkpoint..." -ForegroundColor Cyan
try {
    git add .
    git commit -m "Pre-cleanup checkpoint - automated backup"
    Write-Host "Git checkpoint created successfully" -ForegroundColor Green
} catch {
    Write-Host "WARNING: Could not create git checkpoint" -ForegroundColor Yellow
    Write-Host "This might be okay if you have no changes to commit" -ForegroundColor White
    $response = Read-Host "Continue anyway? (y/N)"
    if ($response -ne 'y' -and $response -ne 'Y') {
        exit 1
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Starting Cleanup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Phase 1: Create new directory structure
Write-Host "[1/6] Creating new directory structure..." -ForegroundColor Cyan

$directories = @(
    "src/Core",
    "src/Capture",
    "src/Video",
    "src/UI",
    "src/Utilities",
    "docs/development/claude",
    "docs/archive",
    "scripts/archive",
    "config"
)

foreach ($dir in $directories) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    Write-Host "  Created: $dir" -ForegroundColor Green
}

# Phase 2: Move source files
Write-Host ""
Write-Host "[2/6] Moving source files..." -ForegroundColor Cyan

$moves = @{
    # Core
    "SessionManager.cs" = "src/Core/"
    "SettingsManager.cs" = "src/Core/"
    "Logger.cs" = "src/Core/"
    "Constants.cs" = "src/Core/"
    
    # Capture
    "ActivityMonitor.cs" = "src/Capture/"
    "RegionSelector.cs" = "src/Capture/"
    "RegionSelector.resx" = "src/Capture/"
    "RegionOverlay.cs" = "src/Capture/"
    "RegionOverlay.resx" = "src/Capture/"
    "AspectRatio.cs" = "src/Capture/"
    "WindowSelector.cs" = "src/Capture/"
    
    # Video
    "FfmpegRunner.cs" = "src/Video/"
    "FfmpegDownloader.cs" = "src/Video/"
    
    # UI
    "MainForm.cs" = "src/UI/"
    "MainForm.Designer.cs" = "src/UI/"
    "MainForm.resx" = "src/UI/"
    "SessionNameDialog.cs" = "src/UI/"
    "SessionNameDialog.resx" = "src/UI/"
    "FfmpegDownloaderDemo.cs" = "src/UI/"
    "ActivityMonitorTestForm.cs" = "src/UI/"
    "ReadinessCheck.cs" = "src/UI/"
    
    # Utilities
    "ValidationHelper.cs" = "src/Utilities/"
    "UIHelper.cs" = "src/Utilities/"
    "SystemMonitor.cs" = "src/Utilities/"
    "PerformanceOptimizations.cs" = "src/Utilities/"
    
    # Program
    "Program.cs" = "src/"
}

$movedCount = 0
foreach ($file in $moves.Keys) {
    if (Test-Path $file) {
        Move-Item $file $moves[$file] -Force
        Write-Host "  Moved: $file -> $($moves[$file])" -ForegroundColor Green
        $movedCount++
    } else {
        Write-Host "  Skip: $file (not found)" -ForegroundColor Yellow
    }
}

Write-Host "  Moved $movedCount source files" -ForegroundColor White

# Phase 3: Move documentation
Write-Host ""
Write-Host "[3/6] Moving documentation..." -ForegroundColor Cyan

$docMoves = @{
    "PROJECT.md" = "docs/"
    "SETUP_TESTING.md" = "docs/"
    "WINDOW_CAPTURE_FEATURE.md" = "docs/"
    "CLEANUP_WINDOW_FEATURE.txt" = "docs/archive/"
}

foreach ($file in $docMoves.Keys) {
    if (Test-Path $file) {
        Move-Item $file $docMoves[$file] -Force
        Write-Host "  Moved: $file -> $($docMoves[$file])" -ForegroundColor Green
    }
}

# Move claude directory
if (Test-Path "claude") {
    Move-Item "claude/*" "docs/development/claude/" -Force
    Remove-Item "claude" -Recurse -Force
    Write-Host "  Moved: claude/ -> docs/development/claude/" -ForegroundColor Green
}

# README.md stays in root - don't touch it
Write-Host "  Keeping: README.md (unchanged)" -ForegroundColor Cyan

# Phase 4: Move scripts and config
Write-Host ""
Write-Host "[4/6] Moving scripts and configuration..." -ForegroundColor Cyan

$scriptMoves = @{
    "setup-tests.ps1" = "scripts/"
    "setup-tests-manual.ps1" = "scripts/"
    "bfg.jar" = "scripts/archive/"
    "git-filter-repo.py" = "scripts/archive/"
}

foreach ($file in $scriptMoves.Keys) {
    if (Test-Path $file) {
        Move-Item $file $scriptMoves[$file] -Force
        Write-Host "  Moved: $file -> $($scriptMoves[$file])" -ForegroundColor Green
    }
}

$configMoves = @{
    "settings.template.json" = "config/"
    "version.json" = "config/"
}

foreach ($file in $configMoves.Keys) {
    if (Test-Path $file) {
        Move-Item $file $configMoves[$file] -Force
        Write-Host "  Moved: $file -> $($configMoves[$file])" -ForegroundColor Green
    }
}

# Phase 5: Delete unnecessary directories
Write-Host ""
Write-Host "[5/6] Cleaning up..." -ForegroundColor Cyan

if (Test-Path "..bfg-report") {
    Remove-Item "..bfg-report" -Recurse -Force
    Write-Host "  Deleted: ..bfg-report/" -ForegroundColor Green
}

# Phase 6: Update .csproj
Write-Host ""
Write-Host "[6/6] Updating project file..." -ForegroundColor Cyan

$csprojContent = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- Exclude test project from compilation -->
    <Compile Remove="TimelapseCapture.Tests\**" />
    <EmbeddedResource Remove="TimelapseCapture.Tests\**" />
    <None Remove="TimelapseCapture.Tests\**" />
    
    <!-- Exclude docs, scripts, config from compilation -->
    <Compile Remove="docs\**" />
    <Compile Remove="scripts\**" />
    <Compile Remove="config\**" />
  </ItemGroup>
</Project>
'@

Set-Content -Path "TimelapseCapture.csproj" -Value $csprojContent -Encoding UTF8
Write-Host "  Updated TimelapseCapture.csproj" -ForegroundColor Green

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Cleanup Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

$newItemCount = (Get-ChildItem -Force | Where-Object { $_.Name -ne ".git" }).Count
Write-Host "Results:" -ForegroundColor Cyan
Write-Host "  Before: $itemCount items in root" -ForegroundColor White
Write-Host "  After:  $newItemCount items in root" -ForegroundColor White
Write-Host "  Reduction: $($itemCount - $newItemCount) items" -ForegroundColor Green
Write-Host ""

Write-Host "New structure:" -ForegroundColor Cyan
Write-Host "  src/              - All source code" -ForegroundColor White
Write-Host "  docs/             - All documentation" -ForegroundColor White
Write-Host "  scripts/          - Utility scripts" -ForegroundColor White
Write-Host "  config/           - Configuration files" -ForegroundColor White
Write-Host ""

Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Build and test:" -ForegroundColor White
Write-Host "   dotnet build" -ForegroundColor Cyan
Write-Host "   dotnet test TimelapseCapture.Tests" -ForegroundColor Cyan
Write-Host ""
Write-Host "2. If everything works, commit the changes:" -ForegroundColor White
Write-Host "   git add ." -ForegroundColor Cyan
Write-Host "   git commit -m ""Reorganize project structure""" -ForegroundColor Cyan
Write-Host ""
Write-Host "3. If something broke, revert:" -ForegroundColor White
Write-Host "   git reset --hard HEAD~1" -ForegroundColor Cyan
Write-Host ""

Write-Host "Cleanup script completed successfully!" -ForegroundColor Green
