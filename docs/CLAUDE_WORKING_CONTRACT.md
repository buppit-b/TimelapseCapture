CLAUDE WORKING CONTRACT — TimelapseCapture
READ THIS FIRST — REQUIRED CONTEXT

You are working on an existing C# WinForms application called TimelapseCapture.

This project has suffered from:

Over-refactoring

Loss of architectural intent

State desynchronization bugs

Excessive async/timer misuse

Your role is NOT to redesign or “improve” the architecture.

Your role is to:

Fix bugs

Improve reliability

Reduce complexity only when strictly necessary

Preserve existing behavior unless explicitly instructed otherwise

Canonical Project Documents (ALWAYS TRUST THESE)

These files define reality.
If code disagrees with them, the code is wrong.

docs/STRUCTURAL_MAP.md
→ System boundaries, ownership, architectural invariants
→ Read this FIRST to understand which system owns what

docs/PROJECT.md
→ High-level purpose, architecture, feature list

docs/PROJECT_CONTEXT.md
→ Most important file
→ Current working state, known fixes, critical invariants, safe patterns

docs/README.md
→ Documentation workflow and lifecycle rules

Do not contradict these documents.
Do not invent new rules.
Do not ignore stated invariants.

HARD RULES (NON-NEGOTIABLE)
❌ DO NOT

Add new features

Refactor for “cleanliness” or style

Introduce new abstractions unless fixing a bug

Change threading/timer models

Introduce new dependencies

Convert code to async/await unless explicitly told

Modify multiple systems at once

✅ DO

Make the smallest change that fixes the bug

Preserve existing method signatures when possible

Prefer clarity over cleverness

Add logging if it helps debug this specific issue

Ask for confirmation before large changes

CRITICAL INVARIANTS (YOU MUST ENFORCE THESE)
Region State Synchronization (MOST IMPORTANT)

Region exists in three locations and MUST remain synchronized:

captureRegion (runtime, MainForm)

_activeSession.CaptureRegion

settings.Region

ALLOWED METHODS ONLY:

SetCurrentRegion(Rectangle region)

ClearCurrentRegion()

GetCurrentRegion()

SetCaptureRegionFromNullable(Rectangle?)

❌ Never assign region fields directly
❌ Never introduce new region storage
❌ Never “fix” region bugs by bypassing these methods

Thread Safety Rules

Capture runs on a timer thread

UI updates MUST use UIHelper.SafeX() helpers

Session access during capture MUST be inside lock(_captureLock)

No fire-and-forget tasks

If unsure → do nothing and ask

Session Safety Rules

Session files are user data — corruption is unacceptable

FramesCaptured > 0 && CaptureRegion == null is a known recoverable state

Use existing repair logic (ValidateAndRepairSession())

Never delete session data automatically.

HOW TO WORK ON A BUG (REQUIRED PROCESS)

When fixing a bug, you MUST follow this order:

1. Restate the bug

Observable behavior

When it happens

Severity (crash / corruption / annoyance)

2. Identify the owning system

Consult docs/STRUCTURAL_MAP.md to find which system owns the bug.

Examples:

Capture Engine (MainForm.CaptureFrame)

Session System (SessionManager)

Region System (RegionSelector/RegionOverlay)

Activity Monitor (smart intervals)

Encoding Pipeline (FfmpegRunner)

UI Orchestration (MainForm coordination)

3. Identify violated invariant

State desync?

Thread safety?

Invalid timing assumption?

Incorrect lifecycle handling?

4. Propose the smallest fix

Point to exact files and methods

Explain why this does not affect other systems

5. Apply fix

Modify only the necessary files

No opportunistic cleanup

FILE MODIFICATION RULES

When editing files:

State which file you are modifying

State why

Keep diffs minimal

Do not reformat unrelated code

Do not reorder methods

If a file exceeds ~3000 lines (MainForm.cs):

Touch the smallest possible region

Never restructure the file without permission

DOCUMENTATION REQUIREMENTS AFTER A FIX

After fixing a bug, you must:

Instruct the user to create:

docs/development/claude/BUGFIX_<DESCRIPTION>_YYYY-MM-DD.md


Provide a short summary suitable for adding to:
PROJECT_CONTEXT.md → Issue Log

Use this format:

#### Issue #X: <Title> (FIXED - YYYY-MM-DD)
- **Problem**:
- **Root Cause**:
- **Solution**:
- **Result**:

IF YOU ARE UNSURE

If any of the following are true:

The fix touches multiple systems

The change feels architectural

You are tempted to refactor

You are unsure about threading implications

STOP and ask for clarification.

Silence is better than breaking invariants.

GOAL REMINDER

This is a long-running, unattended timelapse capture tool.

The highest priorities are:

Reliability

Deterministic behavior

Data safety

Minimal CPU/GPU usage

Not elegance.
Not cleverness.
Not novelty.

End of Contract