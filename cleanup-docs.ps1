# TimelapseCapture Documentation Cleanup Script
# Run this from the project root: C:\Users\Spike\source\TimelapseCapture

param(
    [switch]$DryRun = $false  # Use -DryRun to preview without making changes
)

$ErrorActionPreference = "Stop"
$root = Get-Location

Write-Host "🧹 TimelapseCapture Documentation Cleanup" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

if ($DryRun) {
    Write-Host "🔍 DRY RUN MODE - No changes will be made" -ForegroundColor Yellow
    Write-Host ""
}

# Safety check
if (-not (Test-Path "TimelapseCapture.sln")) {
    Write-Host "❌ Error: Must run from project root (where .sln file is)" -ForegroundColor Red
    exit 1
}

# Function to safely move files
function Move-SafeFile {
    param($Source, $Destination)
    
    if (-not (Test-Path $Source)) {
        Write-Host "  ⚠️  Skipping: $Source (not found)" -ForegroundColor Yellow
        return
    }
    
    $destDir = Split-Path $Destination -Parent
    if (-not (Test-Path $destDir)) {
        if ($DryRun) {
            Write-Host "  📁 Would create: $destDir" -ForegroundColor Gray
        } else {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            Write-Host "  📁 Created: $destDir" -ForegroundColor Green
        }
    }
    
    if ($DryRun) {
        Write-Host "  📄 Would move: $Source → $Destination" -ForegroundColor Gray
    } else {
        Move-Item -Path $Source -Destination $Destination -Force
        Write-Host "  ✅ Moved: $(Split-Path $Source -Leaf)" -ForegroundColor Green
    }
}

# ============================================================================
# PHASE 1: Create new folder structure
# ============================================================================
Write-Host "📁 Phase 1: Creating new folder structure..." -ForegroundColor Cyan

$newFolders = @(
    "docs\guides",
    "docs\features\smart-interval",
    "docs\features\window-capture", 
    "docs\features\activity-monitoring",
    "docs\archive\sessions\2025-10-23",
    "docs\archive\sessions\2025-10-24",
    "docs\archive\sessions\2025-10-25",
    "docs\archive\sessions\2025-10-26",
    "docs\archive\sessions\2025-10-27",
    "docs\archive\features\smart-interval",
    "docs\archive\features\ui-redesign",
    "docs\archive\features\ffmpeg-downloader",
    "docs\archive\bugfixes"
)

foreach ($folder in $newFolders) {
    if (-not (Test-Path $folder)) {
        if ($DryRun) {
            Write-Host "  Would create: $folder" -ForegroundColor Gray
        } else {
            New-Item -ItemType Directory -Path $folder -Force | Out-Null
            Write-Host "  ✅ Created: $folder" -ForegroundColor Green
        }
    }
}

Write-Host ""

# ============================================================================
# PHASE 2: Archive files from docs/development/claude/ignore/
# ============================================================================
Write-Host "📦 Phase 2: Archiving historical documentation..." -ForegroundColor Cyan

$ignorePath = "docs\development\claude\ignore"

if (Test-Path $ignorePath) {
    # Session files by date
    $sessionFiles = @{
        "2025-10-23" = @("BUGFIXES_2025-10-23.md", "BUGFIXES_AND_ROADMAP.md", "BUGFIXES_COMPILATION.md")
        "2025-10-24" = @("BUGFIXES_STATE_SYNC_2025-10-24.md", "BUG_ANALYSIS_STATE_DESYNC.md")
        "2025-10-25" = @("FIXES_APPLIED_2025-10-25.md", "MULTIMONITOR_DEBUG_2025-10-25.md", "SESSION_2025-10-25_MULTIMONITOR_FIX.md", "SESSION_SUMMARY_2025-10-25.md")
        "2025-10-26" = @("BUG_DISCOVERED_INVALID_SESSION_STATE_2025-10-26.md", "CRITICAL_FIX_RACE_CONDITION_2025-10-26.md", "CRITICAL_FIX_REGION_STATE_DESYNC_2025-10-26.md")
        "2025-10-27" = @("BUGFIX_CONTINUATION_PLAN_2025-10-27.md", "IMPLEMENTATION_SUMMARY_2025-10-27.md", "PHASE_1_COMPLETE_2025-10-27.md", "SESSION_SUMMARY_2025-10-27.md")
    }

    foreach ($date in $sessionFiles.Keys) {
        foreach ($file in $sessionFiles[$date]) {
            $src = Join-Path $ignorePath $file
            $dst = "docs\archive\sessions\$date\$file"
            Move-SafeFile -Source $src -Destination $dst
        }
    }

    # Smart interval feature docs
    $smartIntervalFiles = @(
        "SMART_INTERVAL_IMPLEMENTATION_PLAN.md",
        "SMART_INTERVAL_PHASE2_COMPLETE.md", 
        "SMART_INTERVAL_PHASE2_IMPLEMENTATION.md"
    )
    
    foreach ($file in $smartIntervalFiles) {
        $src = Join-Path $ignorePath $file
        $dst = "docs\archive\features\smart-interval\$file"
        Move-SafeFile -Source $src -Destination $dst
    }

    # UI Redesign docs
    $uiFiles = @(
        "UI_REDESIGN_ERROR.md",
        "UI_REDESIGN_PROGRESS.md",
        "UI_REDESIGN_PROPOSAL.md",
        "UI_REDESIGN_STEP1_DESIGNER.md",
        "UI_REDESIGN_VISUAL.md"
    )
    
    foreach ($file in $uiFiles) {
        $src = Join-Path $ignorePath $file
        $dst = "docs\archive\features\ui-redesign\$file"
        Move-SafeFile -Source $src -Destination $dst
    }

    # FFmpeg downloader docs
    $ffmpegFiles = @(
        "FFMPEG_DOWNLOADER_IMPROVEMENTS.md",
        "FFMPEG_DIMENSION_INVESTIGATION.md"
    )
    
    foreach ($file in $ffmpegFiles) {
        $src = Join-Path $ignorePath $file
        $dst = "docs\archive\features\ffmpeg-downloader\$file"
        Move-SafeFile -Source $src -Destination $dst
    }

    # Bug fix documentation
    $bugfixFiles = @(
        "BUG_FIXES_IMPLEMENTATION.md",
        "BUG_FIX_COMPLETE_STOP_RESTART.md",
        "BUG_FIX_MULTIMONITOR_REGIONS.md",
        "BUG_FIX_STOP_CAPTURE_REGION.md",
        "BUG_FOUND_COPYFROMSCREEN.md",
        "BUILD_ERROR_FIXED.md",
        "CRITICAL_BUGFIXES_TODO.md",
        "DPI_FIX_APPLIED.md",
        "DPI_SCALING_INVESTIGATION.md",
        "MAINFORM_FIXES.md",
        "MULTIMONITOR_FIX_AND_REDESIGN.md"
    )
    
    foreach ($file in $bugfixFiles) {
        $src = Join-Path $ignorePath $file
        $dst = "docs\archive\bugfixes\$file"
        Move-SafeFile -Source $src -Destination $dst
    }

    # Phase completion docs
    $phaseFiles = @(
        "PHASE_1_COMPLETE.md",
        "PHASE_2_COMPLETE_CELEBRATION.md",
        "PHASE_2_IMPLEMENTATION_COMPLETE.md",
        "PHASE_2_SETTINGS_DEBOUNCE_INSTRUCTIONS.md",
        "REGION_OVERLAY_COMPLETE.md"
    )
    
    foreach ($file in $phaseFiles) {
        $src = Join-Path $ignorePath $file
        $dst = "docs\archive\features\$file"
        Move-SafeFile -Source $src -Destination $dst
    }

    # Remaining files to archive
    $remainingFiles = Get-ChildItem $ignorePath -File -Filter "*.md"
    foreach ($file in $remainingFiles) {
        $dst = "docs\archive\features\$($file.Name)"
        Move-SafeFile -Source $file.FullName -Destination $dst
    }
}

Write-Host ""

# ============================================================================
# PHASE 3: Consolidate root-level integration docs
# ============================================================================
Write-Host "📄 Phase 3: Consolidating integration guides..." -ForegroundColor Cyan

if (-not $DryRun) {
    $integrationParts = @()
    
    # Read each file if it exists
    if (Test-Path "CONTROL_STATE_INTEGRATION.md") {
        $integrationParts += "# Control State Management"
        $integrationParts += ""
        $integrationParts += Get-Content "CONTROL_STATE_INTEGRATION.md" -Raw
        $integrationParts += ""
        $integrationParts += "---"
        $integrationParts += ""
        Write-Host "  ✅ Read: CONTROL_STATE_INTEGRATION.md" -ForegroundColor Green
    }
    
    if (Test-Path "GUIDED_MODE_INTEGRATION.md") {
        $integrationParts += "# Guided Mode Setup"
        $integrationParts += ""
        $integrationParts += Get-Content "GUIDED_MODE_INTEGRATION.md" -Raw
        $integrationParts += ""
        $integrationParts += "---"
        $integrationParts += ""
        Write-Host "  ✅ Read: GUIDED_MODE_INTEGRATION.md" -ForegroundColor Green
    }
    
    if (Test-Path "GUIDED_MODE_TODO.md") {
        $integrationParts += "# Outstanding Integration Tasks"
        $integrationParts += ""
        $integrationParts += Get-Content "GUIDED_MODE_TODO.md" -Raw
        $integrationParts += ""
        $integrationParts += "---"
        $integrationParts += ""
        Write-Host "  ✅ Read: GUIDED_MODE_TODO.md" -ForegroundColor Green
    }
    
    if (Test-Path "GUIDED_SETUP_IMPLEMENTATION.md") {
        $integrationParts += "# Implementation Reference"
        $integrationParts += ""
        $integrationParts += Get-Content "GUIDED_SETUP_IMPLEMENTATION.md" -Raw
        Write-Host "  ✅ Read: GUIDED_SETUP_IMPLEMENTATION.md" -ForegroundColor Green
    }
    
    # Create consolidated file
    $header = @"
# Integration Guides for TimelapseCapture Features

This document consolidates all integration and implementation guides for features that need manual code integration.

---

## Table of Contents
1. [Control State Management](#control-state-management)
2. [Guided Mode Setup](#guided-mode-setup)
3. [Outstanding Integration Tasks](#outstanding-integration-tasks)
4. [Implementation Reference](#implementation-reference)

---

"@
    
    $footer = @"

---

## Change Log

**2024-12-20**: Consolidated from 4 separate root-level files
- CONTROL_STATE_INTEGRATION.md
- GUIDED_MODE_INTEGRATION.md
- GUIDED_MODE_TODO.md
- GUIDED_SETUP_IMPLEMENTATION.md

"@
    
    $fullContent = $header + ($integrationParts -join "`n") + $footer
    $fullContent | Out-File "docs\INTEGRATION_GUIDES.md" -Encoding utf8
    Write-Host "  ✅ Created: docs\INTEGRATION_GUIDES.md" -ForegroundColor Green
    
    # Delete original files
    $filesToDelete = @(
        "CONTROL_STATE_INTEGRATION.md",
        "GUIDED_MODE_INTEGRATION.md",
        "GUIDED_MODE_TODO.md",
        "GUIDED_SETUP_IMPLEMENTATION.md"
    )
    
    foreach ($file in $filesToDelete) {
        if (Test-Path $file) {
            Remove-Item $file -Force
            Write-Host "  🗑️  Removed: $file" -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "  Would consolidate 4 files into docs\INTEGRATION_GUIDES.md" -ForegroundColor Gray
    Write-Host "  Would delete 4 root-level files" -ForegroundColor Gray
}

Write-Host ""

# ============================================================================
# PHASE 4: Create ACTIVE_ISSUES.md
# ============================================================================
Write-Host "📋 Phase 4: Creating active issues tracker..." -ForegroundColor Cyan

$activeIssuesTemplate = @'
# Active Issues & Bugs

**Last Updated**: {0}

This file tracks current bugs, pending integrations, and feature requests. 
Historical issues are in [archive/](archive/).

---

## 🐛 Known Bugs

### Cross-Thread UI Access Error (Priority: HIGH)
**Location**: MainForm.cs:520, UIHelper.cs:41  
**Error**: System.InvalidOperationException in UpdateActivityStatusUI()  
**Status**: Needs Fix  

**Stack Trace**:
```
System.InvalidOperationException
  Message=Cross-thread operation not valid: Control 'lblActivityStatus' accessed from a thread other than the thread it was created on.
  Source=System.Windows.Forms
  at System.Windows.Forms.Control.get_Handle()
  at System.Windows.Forms.Control.set_Text(String value)
  at TimelapseCapture.UIHelper.SafeSetText(Control control, String text)
  at TimelapseCapture.MainForm.UpdateActivityStatusUI()
  at TimelapseCapture.MainForm.CaptureFrame(Object state)
```

**Solution**: UpdateActivityStatusUI() is being called from the timer thread. Need to wrap UI updates in BeginInvoke().

---

## 📋 Pending Integrations

### Control State Manager Integration
The control state management system needs UpdateControlStates() calls added to these button handlers:

- [ ] btnChooseFolder_Click - After setting SaveFolder
- [ ] btnNewSession_Click - After creating session  
- [ ] btnLoadSession_Click - After loading session
- [ ] btnStart_Click - After starting capture
- [ ] btnStop_Click - After stopping capture
- [ ] btnEncode_Click - Before/after encoding
- [ ] btnDownloadFfmpeg_Click - After FFmpeg install
- [ ] btnBrowseFfmpeg_Click - After selecting FFmpeg

See [INTEGRATION_GUIDES.md](INTEGRATION_GUIDES.md) for detailed instructions.

### Guided Mode Settings Persistence
Need to add GuidedModeEnabled property to CaptureSettings and wire up to save/load.

---

## 🎯 Feature Requests

### High Priority
1. **Window Capture** - PARTIALLY DONE
   - Basic window selection complete
   - TODO: Auto-tracking of window movement/resize
   - TODO: Window-relative capture (content only, no title bar)

2. **Smart Interval Adjustment** - IN PROGRESS
   - ✅ Phase 1: Activity monitoring complete
   - ⏳ Phase 2: Dynamic interval adjustment
   - ⏳ Phase 3: UI controls and presets

3. **Preview Scrubbing**
   - Timeline with frame thumbnails
   - Scrub through captures before encoding

### Medium Priority
4. **Multiple Preset Profiles**
5. **Input Detection Refinement** 
6. **Canvas-Only Export**
7. **Watermark/Timestamp Overlay**

### Future Enhancements
- RIFE frame interpolation
- Deflicker post-processing
- Motion blur simulation
- GPU-accelerated capture

---

## 📝 Notes

- Check [PROJECT.md](PROJECT.md) for full feature roadmap
- Report new bugs by adding them to this file
- Move completed items to appropriate archive folder

'@

$activeIssuesContent = $activeIssuesTemplate -f (Get-Date -Format "yyyy-MM-dd")

if ($DryRun) {
    Write-Host "  Would create: docs\ACTIVE_ISSUES.md" -ForegroundColor Gray
} else {
    $activeIssuesContent | Out-File "docs\ACTIVE_ISSUES.md" -Encoding utf8
    Write-Host "  ✅ Created: docs\ACTIVE_ISSUES.md" -ForegroundColor Green
    
    # Delete the old fix needed.txt
    if (Test-Path "docs\development\claude\fix needed.txt") {
        Remove-Item "docs\development\claude\fix needed.txt" -Force
        Write-Host "  🗑️  Removed: docs\development\claude\fix needed.txt" -ForegroundColor Yellow
    }
}

Write-Host ""

# ============================================================================
# PHASE 5: Create docs navigation README
# ============================================================================
Write-Host "📖 Phase 5: Creating documentation index..." -ForegroundColor Cyan

$docsReadmeTemplate = @'
# TimelapseCapture Documentation Index

Welcome to the TimelapseCapture documentation! This guide helps you find what you need.

---

## 🚀 Quick Start

**For Users**: See User Guide (coming soon)
**For Developers**: Start with [PROJECT.md](PROJECT.md)
**For Testing**: See [SETUP_TESTING.md](SETUP_TESTING.md)
**For Contributors**: Check [ACTIVE_ISSUES.md](ACTIVE_ISSUES.md)

---

## 📚 Main Documentation

### Core Documents
**[PROJECT.md](PROJECT.md)** - Complete project overview
  - Architecture & design patterns
  - Code structure & key classes
  - Configuration & data flow
  - Common issues & solutions

**[ACTIVE_ISSUES.md](ACTIVE_ISSUES.md)** - Current status
  - Known bugs needing fixes
  - Pending feature integrations
  - Feature requests & roadmap

**[INTEGRATION_GUIDES.md](INTEGRATION_GUIDES.md)** - Implementation help
  - Control State Management
  - Guided Mode Setup
  - Step-by-step integration instructions

### Testing
**[SETUP_TESTING.md](SETUP_TESTING.md)** - Complete testing guide
  - Quick setup (5 minutes)
  - Running tests & CI/CD
  - Writing new tests
  - Code coverage

---

## 🔧 Feature Documentation

### Implemented Features
**[WINDOW_CAPTURE_FEATURE.md](WINDOW_CAPTURE_FEATURE.md)** - Window selection & capture

### Feature Designs
Check the [features/](features/) folder for detailed design docs of specific features.

---

## 🗃️ Archive

Historical documentation is organized in [archive/](archive/):

### By Type
**[archive/sessions/](archive/sessions/)** - Development session logs organized by date
**[archive/features/](archive/features/)** - Completed feature documentation
**[archive/bugfixes/](archive/bugfixes/)** - Historical bug investigation & fixes

### When to Use Archive
- Reference old implementation decisions
- Understand the history of a bug fix
- See how features evolved over time

**Note**: Archive is for historical reference only. All current information is in the main docs.

---

## 🏗️ Project Structure

```
docs/
├── README.md                    # This file - navigation guide
├── PROJECT.md                   # Main project documentation
├── ACTIVE_ISSUES.md            # Current bugs & TODOs
├── INTEGRATION_GUIDES.md       # Feature integration steps
├── SETUP_TESTING.md            # Testing guide
├── WINDOW_CAPTURE_FEATURE.md   # Window capture feature
│
├── guides/                     # User & developer guides
├── features/                   # Feature-specific docs
│   ├── smart-interval/
│   ├── window-capture/
│   └── activity-monitoring/
│
├── archive/                    # Historical documentation
│   ├── sessions/              # By date
│   ├── features/              # Completed features
│   └── bugfixes/              # Fixed bugs
│
└── development/               # Development notes
    └── claude/                # AI assistant context
```

---

## 🔍 Finding Information

### "I want to..."
**Fix a bug** → Check [ACTIVE_ISSUES.md](ACTIVE_ISSUES.md)
**Add a feature** → Read [PROJECT.md](PROJECT.md) architecture section
**Integrate existing code** → See [INTEGRATION_GUIDES.md](INTEGRATION_GUIDES.md)
**Run tests** → Follow [SETUP_TESTING.md](SETUP_TESTING.md)
**Understand a class** → See [PROJECT.md](PROJECT.md) "Core Components"
**Know what changed** → Check git log or [archive/sessions/](archive/sessions/)

---

## 📞 Contributing

Found an issue? Want to add a feature?

1. Check [ACTIVE_ISSUES.md](ACTIVE_ISSUES.md) to see if it's already tracked
2. Read [PROJECT.md](PROJECT.md) to understand the architecture
3. Follow [SETUP_TESTING.md](SETUP_TESTING.md) to write tests
4. Update docs with your changes

---

**Last Updated**: {0}
'@

$docsReadme = $docsReadmeTemplate -f (Get-Date -Format "yyyy-MM-dd")

if ($DryRun) {
    Write-Host "  Would create: docs\README.md" -ForegroundColor Gray
} else {
    $docsReadme | Out-File "docs\README.md" -Encoding utf8
    Write-Host "  ✅ Created: docs\README.md" -ForegroundColor Green
}

Write-Host ""

# ============================================================================
# Summary
# ============================================================================
Write-Host "✨ Summary" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan

if ($DryRun) {
    Write-Host ""
    Write-Host "This was a DRY RUN. No changes were made." -ForegroundColor Yellow
    Write-Host "Run without -DryRun to apply changes." -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "✅ Documentation cleanup complete!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "  1. Review the changes in docs/" -ForegroundColor White
    Write-Host "  2. Read docs/README.md for navigation" -ForegroundColor White
    Write-Host "  3. Check docs/ACTIVE_ISSUES.md for TODOs" -ForegroundColor White
    Write-Host "  4. Fix the cross-thread bug listed there" -ForegroundColor White
    Write-Host "  5. Commit: git add . && git commit -m 'docs: Major reorganization'" -ForegroundColor White
}

Write-Host ""
