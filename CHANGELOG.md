# Changelog

All notable changes to TimelapseCapture will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Investigation
- FFmpeg compatibility with varying frame dimensions for mid-session region changes
- Pre-capture region adjustment (decision: allow before first frame, lock after)
- Mid-session region moving with same dimensions (requires testing)

---

## [1.1.0-dev] - 2025-10-21

### Added
- **Region Overlay System**: Toggle HUD-style overlay showing capture region
  - Semi-transparent overlay with corner brackets (aerospace aesthetic)
  - Info box displaying dimensions and position
  - Click-through functionality (doesn't block interaction)
  - Fade in/out animations
  - Color-coded borders (green=capturing, blue=inactive)
  - Keyboard shortcut: Ctrl+R
  - Button: "👁 Show/Hide" (toggles between states)
- User-triggered FFmpeg download with prominent "⬇ Download FFmpeg" button
- Download progress reporting with live status updates
- Session name collision warnings and display name adjustments
- Right-click retry functionality in region selector
- Instructional overlay in region selector
- FFmpeg download retry logic (up to 3 attempts)
- FFmpeg download validation (file size, executable verification)
- Enhanced error handling with specific exception types
- Comprehensive project state documentation (PROJECT_STATE.md)
- FFmpeg dimension investigation and findings (FFMPEG_DIMENSION_INVESTIGATION.md)

### Changed
- FFmpeg no longer auto-downloads on app start
- Session display names now show collision suffixes: "test (1)", "test (2)"
- Region selector cancels current selection on right-click (instead of exiting)
- Format validation only applies to sessions with captured frames

### Fixed
- **CRITICAL**: Multi-monitor region capture offset bug
- **HIGH**: Format validation incorrectly checking previous session (van2 scenario)
- Session name collision handling now properly informs user
- Region selector right-click behavior improved

### Technical
- Added XML documentation to FfmpegDownloader
- Improved async/await patterns in downloader
- Better resource management with proper `using` statements
- Debug output for troubleshooting

---

## [1.0.0] - 2025-10-14

### Added
- Initial release
- Dark mode WinForms interface
- Screen region selection with aspect ratio presets
- Interval-based capture (configurable seconds)
- JPEG and PNG output support
- JPEG quality control (1-100, default 90)
- Session management system
- Settings persistence to settings.json
- FFmpeg integration for video encoding
- Multi-monitor support
- Session metadata tracking

### Features
- Capture screen regions at set intervals
- Organize captures into named sessions
- Encode timelapses to MP4 using FFmpeg
- Aspect ratio presets: 16:9, 4:3, 1:1, 21:9, Custom
- Settings locked during capture to prevent encoding issues
- Session folder structure with frames and metadata

---

## Version Numbering Scheme

**Format**: `MAJOR.MINOR.PATCH[-STAGE]`

- **MAJOR**: Breaking changes or significant feature overhauls
- **MINOR**: New features, non-breaking changes
- **PATCH**: Bug fixes, minor improvements
- **STAGE**: `dev` (development), `rc` (release candidate), omitted for stable

### Examples
- `1.0.0` - Stable release
- `1.1.0-dev` - Development version with new features
- `1.1.0-rc1` - Release candidate 1
- `1.1.0` - Stable release of version 1.1
- `1.1.1` - Patch release (bug fixes only)
- `2.0.0` - Major version (breaking changes)

### Release Process
1. Development happens in `X.Y.0-dev`
2. Feature complete: bump to `X.Y.0-rc1`
3. Testing and bug fixes: `X.Y.0-rc2`, `X.Y.0-rc3`, etc.
4. Stable release: `X.Y.0`
5. Hot fixes: `X.Y.1`, `X.Y.2`, etc.
6. Next feature cycle: `X.Y+1.0-dev`

---

## Types of Changes

- **Added**: New features
- **Changed**: Changes to existing functionality
- **Deprecated**: Features that will be removed
- **Removed**: Removed features
- **Fixed**: Bug fixes
- **Security**: Security fixes
- **Technical**: Internal improvements, refactoring, or technical debt reduction
- **Investigation**: Features being researched or explored

---

*Keep this file updated with every significant change.*  
*Group changes by release version, newest at top.*
