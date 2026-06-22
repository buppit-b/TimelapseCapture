# Documentation Guide

## Quick Reference

📄 **Start Here (Read in Order):**
1. **WORKING_WITH_CLAUDE.md** - How to collaborate with AI on this project (START HERE!)
2. **STRUCTURAL_MAP.md** - System boundaries and ownership (READ FIRST before code changes)
3. **CLAUDE_WORKING_CONTRACT.md** - Development rules and invariants
4. **PROJECT_CONTEXT.md** - Current state, known issues, how to work with the code
5. **PROJECT.md** - What is this project? What does it do?

📁 **Folders:**
- **development/claude/** - Active work, current bugs, features in progress
- **archive/** - Completed features, fixed bugs, historical documentation

## File Organization

### Active Development (development/claude/)

Put new documents here when working on something:

```
development/claude/
├── BUGFIX_[Description]_YYYY-MM-DD.md    # Current bug investigations
├── FEATURE_[Name].md                      # Features being developed
├── SESSION_YYYY-MM-DD_[Topic].md         # Development session notes
└── fix needed.txt                         # Quick notes on current issues
```

**Move to archive/ when complete!**

### Completed Work (archive/)

Move documents here when done:
- Feature is shipped
- Bug is fixed and verified  
- Documentation is outdated
- No longer needed for daily reference

**Before archiving:**
1. ✅ Update PROJECT_CONTEXT.md with the outcome
2. ✅ Add to Issue Log if it's a bug fix
3. ✅ Add summary at top of document

### Historical Sessions (development/claude/ignore/)

Old session logs that might be useful later:
- More than 2 weeks old
- Issue already documented elsewhere
- Keeping for historical reference

**Don't delete!** These show why decisions were made.

## Naming Conventions

```bash
# Bug fixes
BUGFIX_MEMORY_LEAK_2025-01-07.md
BUGFIX_CROSS_THREAD_UI.md

# Features  
FEATURE_WEBCAM_INTEGRATION.md
FEATURE_SMART_INTERVALS.md

# Sessions
SESSION_2025-01-08_UI_REDESIGN.md
SESSION_2025-01-10_REFACTORING.md

# Planning
PLAN_AUDIO_CAPTURE.md
PLAN_CLOUD_BACKUP.md
```

**Rules:**
- ALL_CAPS with underscores
- Include date: YYYY-MM-DD
- Descriptive name
- Start with type prefix

## Monthly Cleanup

**First of each month:**

1. Review `development/claude/`
   - Move docs older than 2 weeks to appropriate location
   - Archive completed work
   - Move old sessions to `ignore/`

2. Update PROJECT_CONTEXT.md
   - Add any missing bug fixes to Issue Log
   - Update "Last Updated" date
   - Verify Working Features list is current

3. Clean up
   - Delete truly obsolete files (rare!)
   - Consolidate duplicate information
   - Update this README if structure changed

## Document Lifecycle

```
┌─────────────────────────────────────────┐
│ 1. CREATE in development/claude/        │
│    BUGFIX_SOMETHING.md                  │
└────────────┬────────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────┐
│ 2. UPDATE PROJECT_CONTEXT.md            │
│    Add to Issue Log                     │
└────────────┬────────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────┐
│ 3. MOVE to archive/                     │
│    Add summary at top                   │
└────────────┬────────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────┐
│ 4. (Optional) MOVE to ignore/           │
│    After 2+ weeks                       │
└─────────────────────────────────────────┘
```

## Tips

✅ **DO:**
- Document as you go
- Update PROJECT_CONTEXT.md after each fix
- Use descriptive filenames
- Move completed work to archive
- Keep docs/ root clean (only PROJECT files)

❌ **DON'T:**
- Leave random .txt files in docs/
- Create nested folders without purpose
- Forget to archive completed work
- Delete session logs (move to ignore/)
- Let development/ folder grow forever

## Questions?

- Where does X go? → Check "File Organization" above
- How do I name it? → Check "Naming Conventions"
- When to update PROJECT_CONTEXT.md? → After every bug fix/feature
- When to clean up? → Monthly, first of the month

---

**Last Updated**: 2025-01-06  
**Maintainer**: Claude + Spike
