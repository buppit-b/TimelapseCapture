# FFmpeg Dimension Investigation

## Goal
Determine if FFmpeg's concat demuxer can handle varying frame dimensions within a single timelapse video.

## Questions to Answer
1. Can concat demuxer accept frames with different dimensions?
2. If yes, what encoding parameters are needed?
3. What are the quality/performance implications?
4. Are there better alternatives (scale filter, padding)?

## Test Plan

### Test 1: Mixed Dimensions (Resize Mid-Session)
Create test frames with varying dimensions:
- Frames 1-10: 1920x1080
- Frames 11-20: 1280x720
- Frames 21-30: 1920x1080

**Command**:
```bash
ffmpeg -f concat -safe 0 -i filelist.txt -c:v libx264 -pix_fmt yuv420p -crf 23 output.mp4
```

**Expected Result**: ERROR or automatic scaling behavior

### Test 2: Scale Filter (Force Consistent Output)
Use scale filter to normalize all frames:

**Command**:
```bash
ffmpeg -f concat -safe 0 -i filelist.txt -vf "scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2" -c:v libx264 -pix_fmt yuv420p -crf 23 output.mp4
```

**Expected Result**: Success, but requires knowing target dimensions upfront

### Test 3: Moving Region (Same Dimensions)
Create test frames with same dimensions but different content positions:
- All frames: 1920x1080
- Content "moves" across the frame

**Command**:
```bash
ffmpeg -f concat -safe 0 -i filelist.txt -c:v libx264 -pix_fmt yuv420p -crf 23 output.mp4
```

**Expected Result**: SUCCESS - Content position doesn't affect encoding

## Findings

### FFmpeg Documentation Research
From FFmpeg documentation:
- **concat demuxer**: "All files must have the same streams (same codecs, same time base, etc.)"
- **Implication**: Stream properties (including dimensions) must match
- **Workaround**: Use scale filter to normalize

### Recommended Approach

#### Option A: Strict Dimensions (Current Behavior)
- **Pro**: Simple, reliable, no encoding issues
- **Pro**: Best quality (no scaling artifacts)
- **Con**: Cannot resize mid-session

**Verdict**: Keep for v1.1.0, safest option

#### Option B: Pre-Capture Adjustment Only
- **Pro**: Flexibility before committing to session
- **Pro**: No encoding complexity
- **Pro**: User can fine-tune region
- **Con**: Still locked once capture starts

**Verdict**: IMPLEMENT THIS - Best balance

#### Option C: Scale Filter Normalization
- **Pro**: Allows dimension changes mid-session
- **Pro**: FFmpeg handles scaling automatically
- **Con**: Requires knowing target dimensions
- **Con**: Quality loss from scaling
- **Con**: More complex encoding command
- **Con**: Potential aspect ratio issues

**Verdict**: DEFER - Complexity outweighs benefit for v1.1.0

#### Option D: Stop/Resume with New Dimensions
- **Pro**: Clean separation between dimension sets
- **Pro**: Each segment has consistent quality
- **Con**: Creates separate video files
- **Con**: User must manually merge if desired

**Verdict**: DEFER - Consider for v1.2.0

### Conclusion for v1.1.0

**ALLOW**:
- ✅ Region adjustment BEFORE first capture
- ✅ Moving region (same dimensions) - verify this is actually safe
- ✅ Viewing region overlay at any time

**DO NOT ALLOW**:
- ❌ Resizing region after first capture
- ❌ Dimension changes mid-session

**IMPLEMENTATION**:
1. Region overlay system (view-only when session has frames)
2. Region adjustment UI (enabled only pre-capture)
3. Lock dimensions after first frame captured
4. Clear messaging about why locked

### Testing Required
Before allowing "move region (same dimensions)":
1. Capture 10 frames at position (100, 100, 1920x1080)
2. Stop capture
3. Move region to (500, 500, 1920x1080)
4. Resume capture for 10 more frames
5. Encode video
6. Verify: Video plays correctly, content "jumps" position

**If test passes**: Moving is safe
**If test fails**: Lock position and dimensions after first frame

---

**Recommendation for This Session**:
Implement Option B (Pre-Capture Adjustment) with visual overlay system.
Test "move region" scenario before allowing it in release.

**Date**: 2025-10-21
**Status**: Investigation Complete - Proceeding with Option B
