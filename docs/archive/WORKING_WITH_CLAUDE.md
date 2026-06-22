# Working with Claude on TimelapseCapture

## For Humans: How to Start a Session

### Quick Start (Recommended)

```
Hi Claude, I'd like to work on TimelapseCapture.

Please read the project documentation in this order:
1. docs/STRUCTURAL_MAP.md
2. docs/CLAUDE_WORKING_CONTRACT.md  
3. docs/PROJECT_CONTEXT.md

Then let me know you're ready to work.
```

### With a Specific Task

```
Hi Claude, I need to [fix bug/add feature/debug] in TimelapseCapture.

Please:
1. Read STRUCTURAL_MAP.md, CLAUDE_WORKING_CONTRACT.md, and PROJECT_CONTEXT.md
2. Identify which system owns this issue
3. Check for any relevant recent fixes
4. Then we can work on it

Issue: [describe what's happening]
```

### Ultra-Short Version

```
TimelapseCapture - read the docs first, then let's [task]
```

---

## Why This Works

**The Problem:**
- LLMs (including Claude) tend to over-engineer solutions
- Without context, they may violate architectural invariants
- They might "improve" code in ways that break existing patterns
- They may not know about recent fixes or current state

**The Solution:**
- Force documentation reading BEFORE coding
- Prime with system boundaries (STRUCTURAL_MAP)
- Load constraints and rules (CONTRACT)
- Update on current state (CONTEXT)

**Result:**
- Surgical, minimal fixes
- Respect for existing architecture
- Awareness of critical invariants (especially region sync)
- No accidental refactoring

---

## What Claude Should Do (Self-Instructions)

### On Session Start

When you see "TimelapseCapture" or references to this project:

1. **STOP and READ** (in this order):
   - `docs/STRUCTURAL_MAP.md` - Understand system boundaries
   - `docs/CLAUDE_WORKING_CONTRACT.md` - Load rules and constraints
   - `docs/PROJECT_CONTEXT.md` - Check current state and recent fixes

2. **CONFIRM** you've read them:
   ```
   ✅ Read STRUCTURAL_MAP.md - understood system boundaries
   ✅ Read CLAUDE_WORKING_CONTRACT.md - loaded constraints
   ✅ Read PROJECT_CONTEXT.md - current state loaded
   
   Ready to work. What needs attention?
   ```

3. **BEFORE ANY CODE CHANGES**, identify:
   - Which system owns this code (consult STRUCTURAL_MAP)
   - What invariants must be preserved (consult CONTRACT)
   - Any recent related fixes (consult CONTEXT)

### During Work

**When fixing bugs:**
- Follow the 5-step process in CLAUDE_WORKING_CONTRACT.md
- Identify the owning system FIRST
- Make the SMALLEST change that fixes the issue
- Don't refactor unless explicitly told
- Don't touch multiple systems

**When writing code:**
- Respect the "Must NOT" lists for each system
- Use the correct synchronization methods (especially for region)
- Use UIHelper for all timer-to-UI updates
- Lock _captureLock when accessing session during capture

**Critical Invariants to NEVER Violate:**

1. **Region Synchronization** (MOST IMPORTANT)
   - ONLY use: `SetCurrentRegion()`, `ClearCurrentRegion()`, `GetCurrentRegion()`
   - NEVER assign `captureRegion`, `_activeSession.CaptureRegion`, or `settings.Region` directly

2. **Thread Safety**
   - Timer thread → UI updates MUST use `UIHelper.SafeX()` methods
   - Session access during capture MUST be inside `lock(_captureLock)`

3. **Session Safety**
   - Never delete frames automatically
   - FramesCaptured must match reality
   - CaptureRegion can be null before first frame (this is OK)

### After Fixes

1. **Document the fix:**
   - Provide summary for PROJECT_CONTEXT.md Issue Log
   - Suggest creating `docs/development/claude/BUGFIX_[DESC]_YYYY-MM-DD.md`

2. **Update timestamp:**
   - Remind human to update "Last Updated" dates where relevant

---

## Common Pitfalls (What NOT to Do)

❌ **Don't:** Jump straight into coding without reading docs
❌ **Don't:** "Improve" code that's working fine
❌ **Don't:** Refactor for "cleanliness" or style
❌ **Don't:** Introduce async/await without explicit permission
❌ **Don't:** Modify multiple systems at once
❌ **Don't:** Bypass `SetCurrentRegion()` / `ClearCurrentRegion()`
❌ **Don't:** Access UI controls from timer thread without UIHelper
❌ **Don't:** Add new features unless explicitly asked

✅ **Do:** Read docs first
✅ **Do:** Make minimal, surgical changes
✅ **Do:** Respect system boundaries
✅ **Do:** Preserve existing behavior
✅ **Do:** Ask before large changes
✅ **Do:** Follow the 5-step bug fix process

---

## Example Session Flow

### Good Session ✅

```
Human: "TimelapseCapture - read docs, then debug the capture stopping issue"

Claude: 
[reads STRUCTURAL_MAP.md]
[reads CLAUDE_WORKING_CONTRACT.md]
[reads PROJECT_CONTEXT.md]

✅ Documentation loaded.

System ownership: Capture Engine (MainForm.CaptureFrame)
Recent fixes: Issue #7 - Error counter reset fixed
Relevant invariants: MAX_CONSECUTIVE_ERRORS = 3

Ready to debug. Can you describe what's happening when capture stops?
```

**Result:** Claude has context, knows the constraints, identifies the system, checks for recent related fixes.

### Bad Session ❌

```
Human: "The capture keeps stopping, can you fix it?"

Claude:
[doesn't read docs]
[starts analyzing code]
[suggests refactoring the entire capture system]
[introduces async/await]
[bypasses SetCurrentRegion()]
[breaks thread safety]
```

**Result:** Over-engineering, broken invariants, architectural drift.

---

## Documentation Reading Order (For Claude)

### Always Read These Three (In Order):

1. **STRUCTURAL_MAP.md** (~15 min read)
   - System boundaries and ownership
   - "Which system owns this code?"
   - Critical invariants per system
   - Thread safety rules
   - System interaction matrix

2. **CLAUDE_WORKING_CONTRACT.md** (~5 min read)
   - Hard rules (DO/DON'T lists)
   - 5-step bug fix process
   - File modification rules
   - When to stop and ask

3. **PROJECT_CONTEXT.md** (~10 min read)
   - Current status and working features
   - Recent bug fixes (Issue Log)
   - Known issues being tracked
   - Critical code patterns with examples
   - Build status

### Optional (Context-Dependent):

4. **PROJECT.md** - High-level features, architecture, output structure
   - Read if you need to understand overall features
   - Read if working on new system integration
   - Skip if just fixing a bug in existing code

5. **docs/development/claude/** - Active bug/feature docs
   - Read if working on a known issue
   - Check for related investigations

---

## For Humans: After a Bug Fix

When Claude fixes a bug, make sure to:

1. **Test the fix** - Verify it actually works

2. **Update PROJECT_CONTEXT.md**
   - Add entry to "Recent Bug Fixes (Issue Log)" section
   - Use the format Claude provides
   - Update "Last Updated" date

3. **Create bug fix document** (optional but recommended)
   - Location: `docs/development/claude/BUGFIX_[DESC]_YYYY-MM-DD.md`
   - Claude will often draft this for you
   - Move to `docs/archive/` when verified

4. **Commit changes**
   - Include reference to issue number
   - Mention which system was affected

---

## Quick Reference Card

```
┌─────────────────────────────────────────────┐
│  TIMELAPCAPTURE - WORKING WITH CLAUDE       │
├─────────────────────────────────────────────┤
│                                             │
│  START SESSION:                             │
│  "TimelapseCapture - read docs first"       │
│                                             │
│  CLAUDE MUST READ:                          │
│  1. STRUCTURAL_MAP.md                       │
│  2. CLAUDE_WORKING_CONTRACT.md              │
│  3. PROJECT_CONTEXT.md                      │
│                                             │
│  CRITICAL RULES:                            │
│  • Region: ONLY use SetCurrentRegion()      │
│  • Thread: ONLY use UIHelper for UI         │
│  • Session: ALWAYS lock(_captureLock)       │
│  • Changes: MINIMAL and SURGICAL            │
│                                             │
│  FORBIDDEN:                                 │
│  ✗ Refactoring without bug                  │
│  ✗ Adding features unprompted               │
│  ✗ Touching multiple systems                │
│  ✗ Bypassing synchronization                │
│                                             │
└─────────────────────────────────────────────┘
```

---

## Troubleshooting

**Q: Claude is suggesting large refactors**
A: Stop and remind: "Follow CLAUDE_WORKING_CONTRACT.md - minimal changes only"

**Q: Claude forgot about region synchronization**
A: Remind: "Check STRUCTURAL_MAP.md System 4 - region must use SetCurrentRegion()"

**Q: Claude wants to add async/await**
A: Stop: "CONTRACT forbids async/await unless explicitly approved"

**Q: Claude is modifying multiple systems**
A: Stop: "One system per change - which system owns this bug?"

**Q: Not sure which document to update**
A: Check docs/README.md for document lifecycle guidance

---

**Last Updated:** 2025-01-30  
**Purpose:** Guide for effective human-AI collaboration on TimelapseCapture  
**Audience:** Humans (Spike) and AI assistants (Claude)