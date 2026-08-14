# Development Guidelines - Kraken Trading Dashboard

**Version**: 13.0.0 | **Updated**: 2026-08-06 2:35 PM | **Status**: 🔒 ACTIVE

---

## 📑 Rules Quick Reference

### Meta & Reference Rules (1-5)
1. **Sequential Execution** - Execute rules in numbered order
2. **Documentation-First** - Read `docs/core/DOCUMENTATION_INDEX.md` BEFORE any work
3. **New Rules** - Add new rules in logical execution order
4. **Utility Tools (Self-Scripting First!)** - Use for >50 lines OR 3+ edits
5. **Large Document Creation** - Use Python scripts for documents >500 lines

### Implementation Rules (6-10)
6. **Event-Driven State** - Use data/sample counts, NOT time-based logic
7. **Race Conditions** - Guard async operations, stop timers before state changes
8. **Expected Behavior** - State exact behavior before user tests
9. **Logging & Live Debugging** - Use emoji prefixes, run app yourself to verify logs
10. **RE Verification Before Design Assumptions** - When in doubt, don't guess; request RE pass and/or user playtest

- **Comments & Documentation Edits** – Comments/docs MAY be updated when they are inaccurate confusing, or if it would be of significant benifit to the task. Keep edits minimal, task-focused, and aligned with the current implementation.

### Quality & Completion Rules (11-16)
11. **PowerShell Debugging** - Run in PowerShell, watch console output
12. **Testing** - Batch 3–5 safe changes; explicitly call out critical test points
13. **Bug Tracker** - Always catalog and record new bugs when discovered
14. **Recent Changes** - Update RECENT_CHANGES.md AFTER fixes, BEFORE commits
15. **Documentation** - Document ONLY after ALL issues resolved
16. **Commit** - Commit code + docs together after testing

---

## 1️⃣ Sequential Rule Execution

**Core**: Execute rules in numbered order. Never skip or reorder.

**Why**: Rules have dependencies (meta → reference → design → implementation → quality → docs → completion).

**Checklist**: Current rule complete? Requirements satisfied? Ready for next?

---

## 2️⃣ Documentation-First Rule

**Core**: 🚨 Read `docs/core/DOCUMENTATION_INDEX.md` as FIRST ACTION before ANY code search/grep/implementation.

**Execution Order**:
1. Open `docs/core/DOCUMENTATION_INDEX.md`
2. Search for relevant keywords
3. Read ALL relevant doc files COMPLETELY
4. Review tool guidance in `docs/core/WINDSURF_TOOLS_REFERENCE.md` **and** the `docs/utility_tools/` folder before selecting Windsurf/Cascade tools
5. ONLY THEN use grep/code_search/read_file

**File Sync Requirement**: When `global_rules.md` updated → ALWAYS copy to `docs/core/DEVELOPMENT_GUIDELINES.md`

**Zero Tolerance**: First tool call MUST be `read_file` on `docs/core/DOCUMENTATION_INDEX.md`. Violation = grep/code_search before docs.

---

## 3️⃣ Instructions for New Rules

**Core**: New rules MUST be placed in logical execution order within `global_rules.md`. Order matters!

**Rule Categories & Order**:
1. **Meta & Reference (1-4)**: Rules about rules, documentation lookup, utility tools
2. **Implementation (5-8)**: Rules used DURING coding (patterns, design, logging)
3. **Quality & Completion (9-12)**: Rules for testing, docs, and commit

**Process for Adding New Rules**:
1. **Identify Category**: Determine if rule is meta, implementation, or quality
2. **Find Logical Position**: 
   - Implementation tools (editing) → After #3, before design patterns
   - Design patterns → After tools, before specific systems
   - System-specific → After general patterns
   - Quality/verification → After implementation, before commit
3. **Verify Dependencies**: What does this rule need? What needs this rule?
4. **Insert & Renumber**: Add rule in correct position, renumber ALL subsequent rules
5. **Update Quick Reference**: Update the categorized list at top
6. **Sync to `DEVELOPMENT_GUIDELINES.md`**: Copy entire file
7. **Document Rationale**: Explain WHY this position in rule description

**Example Placement Logic**:
```
New rule: "API Rate Limiting" (implementation pattern)
├─ Needs: Documentation lookup (#2) ✓
├─ Used during: Implementation phase ✓
├─ Depends on: Event-driven patterns (#5) ✓
└─ Position: After #5, before Race Conditions → Insert as new #6
    (Renumber old #6-13 to #7-14)
```

**Critical**: NEVER add rules at the end by default. Always find the logical execution position!

---

## 4️⃣ Utility Tools (Self-Scripting First!)

**Core**: Use self-scripting methodology from `docs/utility_tools/` when encountering large code edits.

**Threshold**: Apply this rule when:
- Replacing functions >50 lines
- Requiring 3+ sequential edit operations
- Complete module redesigns
- Complex HTML/template generation
- Batch operations across multiple files

**Process**:
1. **STOP** - Don't use sequential `edit` or `multi_edit` operations
2. **REFERENCE** - Check BOTH `docs/core/WINDSURF_TOOLS_REFERENCE.md` for tool behavior and `docs/utility_tools/README.md` for the appropriate methodology
3. **CHOOSE TOOL**:
   - Single large function → `LARGE_CODE_REPLACEMENT_METHODOLOGY.md`
   - Multiple files → `SELF_SCRIPTING_ADVANCED_PATTERNS.md`
   - First time → Start with `SELF_SCRIPTING_EXAMPLES.md`
   - Documentation cleanup → `DOCUMENT_CLEANUP_SCRIPT.md`
4. **APPLY** - Follow the 5-phase self-scripting process
5. **VERIFY** - Run validation scripts and syntax checks
6. **CLEANUP** - Remove temporary files

**Benefits**:
- ⚡ **2-3x faster** than traditional sequential edits
- ✅ **Atomic operations** (all-or-nothing, no partial states)
- 🔍 **Easy to verify** (single diff to review)
- 📝 **Repeatable** (scripts can be reused)
- 🛡️ **Lower risk** (no token limits, no syntax errors from partial edits)

**Example Workflow**:
```
Large function replacement (194 lines):
  Traditional: 15-25 edit operations, 30+ minutes, high error risk
  Self-scripting: 5-8 operations, 11 minutes, zero errors ✅
```

**Tool Selection Matrix**:
```
├─ Single function >100 lines?
│  └─ Use: LARGE_CODE_REPLACEMENT_METHODOLOGY.md
│
├─ Multiple independent files?
│  └─ Use: SELF_SCRIPTING_ADVANCED_PATTERNS.md
│
├─ First time with self-scripting?
│  └─ Use: SELF_SCRIPTING_EXAMPLES.md
│
└─ Documentation scattered?
   └─ Use: DOCUMENT_CLEANUP_SCRIPT.md
```

**Required Reading**:
- `docs/utility_tools/README.md` - Tool selection and quick start
- Methodology file specific to your use case
- Examples for copy-paste templates

**Zero Tolerance**: Never use 5+ sequential edits for large replacements. Always check utility_tools/ first.

**Why This Position**: Comes immediately after documentation lookup (#2) and rule management (#3) because it defines HOW to efficiently edit code BEFORE you start implementing patterns.

---

## 5️⃣ Large Document Creation Rule

**Core**: For documents >500 lines, use Python scripts with incremental file appending instead of single large edits.

**Threshold**: Apply this rule when:
- Creating comprehensive documentation >500 lines
- Generating analysis documents with multiple sections
- Building reference guides with code examples
- Any document that would exceed token limits in single edit

**Process**:
1. **Create initial file** with `write_to_file` tool (header + intro section)
2. **Create Python script** for appending remaining sections
3. **Use `run_command`** to execute Python script
4. **Verify output** with file size/line count check
5. **Cleanup** temporary Python scripts

**Example Pattern**:
```python
# append_sections.py
import codecs

content = '''
## Section 1
[Content here]

## Section 2
[Content here]
'''

with codecs.open('docs/core/DOCUMENT.md', 'a', encoding='utf-8') as f:
    f.write(content)

print('✅ Section appended')
```

**Benefits**:
- ✅ Avoids token limit errors on large documents
- ✅ Atomic operations (complete sections or nothing)
- ✅ Easy to verify (file size + line count)
- ✅ Repeatable for similar documents
- ✅ Clean final output (no partial states)

**Real Example**: EVENT_DRIVEN_ARCHITECTURE_ANALYSIS.md (927 lines, 33.7 KB)
- Step 1: Created with `write_to_file` (header + matrix)
- Step 2: Ran Python script to append detailed analysis
- Step 3: Ran Python script to append implementation plan
- Step 4: Verified with line count (927 lines ✅)
- Step 5: Cleaned up temporary scripts

**When NOT to Use**:
- Documents <500 lines (use `write_to_file` directly)
- Simple files (use `edit` or `multi_edit`)
- Code files (use `edit` or `multi_edit`)

**Why This Position**: Comes after Utility Tools (#4) because it's a specialized technique for large documents. Comes before implementation rules because it's a META-LEVEL tool selection rule, not a code pattern.

---

## 6️⃣ Event-Driven State Management Rule

**Core**: State machines driven by DATA/SAMPLE COUNT, NOT time. Calculate time FROM data if showing to user.

**Why**: Time-based = negative timers, backward progression, race conditions, non-deterministic.

**Pattern**:
```python
# ❌ BAD: Time-based
if uptime_minutes < 1.5:
    remaining = 1.5 - uptime_minutes  # Can go negative!

# ✅ GOOD: Event-driven
sample_count = len(self.samples)
if sample_count < 25:
    remaining_samples = 25 - sample_count
    time_estimate = remaining_samples * 5 / 60  # Always positive!
```

**Checklist**:
- State determined by data count?
- Forward-only progression?
- Deterministic (same count = same state)?
- Time estimates calculated FROM data?

**Why This Position**: This is a fundamental DESIGN PRINCIPLE that must be understood before implementing race condition guards.

---

## 7️⃣ Race Conditions Avoidance

**Core**: Guard async operations. Stop timers BEFORE state changes. Clear visuals BEFORE flag transitions.

**Required for**: WebSocket handlers, QTimer callbacks, async price updates, UI drawing functions.

**Standard Pattern**:
```python
def event_handler(self):
    # 1. Guard clause at TOP
    if not self.running_flag:
        return
    # 2. Rest of function...
```

**Stop Pattern**:
```python
def stop(self):
    # 1. Stop timers
    # 2. Clear visuals
    # 3. Reset variables
    # 4. Transition flags
```

**Detection**: When race bug found → scan for similar patterns, apply preventive fixes, document in "Examples" section.

---

## 8️⃣ Expected Behavior Declaration Rule

**Core**: State EXACT expected behavior BEFORE user tests.

**Format**:
```
## 🧪 Expected Behavior:
### Change #1: [Description]
- Step 1: [Action] → [Expected result]
- Step 2: [Action] → [Expected result]
```

**Required**: List changes, describe behavior, include workflow, wait for user confirmation.

---

## 9️⃣ Logging & Live Debugging Rule

**Core**: Use consistent emoji prefixes for visual identification.

**Categories**:
- `🔢` Calculations | `🟠` NEXT grid | `🟢` Success | `🔴` Errors | `🛑` Stop/Cancel
- `🌐` WebSocket connection | `📡` Data received | `⚡` Price updates
- `🔵` Button states

**Format**: `logging.info("🟠 STATE TRANSITION: LIVE → LIVE + NEXT")`

---

## 🔟 RE Verification Before Design Assumptions Rule

**Core**: When uncertain about original Gangsters game mechanics or design decisions, DO NOT guess. Instead, request one or both of:
1. **RE Pass** — Create/run Ghidra scripts to decompile and analyze the relevant binary functions
2. **User Playtest** — Ask the user to play Gangsters and observe the specific behavior in question

**When to Apply**:
- ✅ Uncertain about how the original game handles a mechanic
- ✅ About to implement a feature based on an assumption about original behavior
- ✅ RE findings are ambiguous or incomplete on a specific point
- ✅ Disagreeing with existing RE documentation interpretation
- ✅ User observes behavior that contradicts RE findings

**When NOT to Apply**:
- ❌ RE data already clearly confirms the behavior (cite the evidence)
- ❌ Pure Steel City design decisions with no original game equivalent
- ❌ User has explicitly stated the desired behavior regardless of original

**Process**:
1. **Identify the uncertainty** — State exactly what is unknown
2. **Check existing RE** — Search `REVERSE_ENGINEERING_FINDINGS.md` and Ghidra output files
3. **If unresolved** — Propose either:
   - A targeted Ghidra script to decompile specific functions/patterns
   - A user playtest request with specific observations to make
4. **Wait for evidence** — Do NOT proceed with implementation based on assumptions
5. **Document findings** — Update RE docs with confirmed behavior

**Example**:
```
Uncertainty: "Are traffic signals dynamic or static?"
→ Check RE: All references to +0x1220 are reads, no writes found
→ Still uncertain (absence of evidence ≠ evidence of absence)
→ Propose: RE pass searching for writes to DAT_007c0024+0x1220
→ OR: Ask user to observe if traffic light behavior changes during a game week
→ Only then implement based on confirmed answer
```

**Why This Position**: Last implementation rule because it governs HOW to approach design decisions BEFORE writing code. Must come after Logging (#9) since you need to know what to verify before debugging. Comes before Quality rules (#11-16) since verification happens before testing.

---

## 1️⃣1️⃣ PowerShell Debugging Rule

**Core**: Run in PowerShell, watch console to verify logging and detect race conditions.

**Required**: After ANY logging/state code → Run in PowerShell, verify logs appear in expected order.

---

## 1️⃣2️⃣ Testing Protocol Rule

**Core**: Batch 3–5 **safe** changes when confident, and explicitly mark when a test run is **required**.

**When batching is OK**:
- Logging, diagnostics, or documentation-only changes
- Shadow-mode features (no live orders or capital flow)
- Pure refactors that do not touch routing, broker calls, or async flows

**When testing is CRITICAL** (call out clearly to the user):
- Changes to live START/STOP routing or engine selection
- Capital, normalization, or sizing logic that affects real order amounts
- Async / event-driven flows where race conditions are possible

**Sequence**:
1. Implement up to 3–5 related safe changes.
2. If any change is in a CRITICAL area, clearly label a "🧪 TEST NOW" checkpoint.
3. User runs tests and reports results for those checkpoints.
4. AFTER confirmation on critical areas, proceed to documentation (Rule #16).

---

## 1️⃣3️⃣ Bug Tracker Rule

**Core**: Always catalog and record new bugs when discovered in `docs/known_issues/`.

**When to Use**:
- ✅ New bug discovered during development
- ✅ User reports unexpected behavior
- ✅ Race condition or edge case found
- ✅ Systematic issue affecting multiple features

**Process**:
1. **Create bug file** in `docs/known_issues/[category]/[BUG_NAME].md`
2. **Document symptoms**: What's broken? How to reproduce?
3. **Document investigation**: Root cause analysis, attempted fixes
4. **Link related files**: Reference relevant code, docs, logs
5. **Update** `docs/known_issues/README.md` with bug entry
6. **Track status**: ACTIVE → INVESTIGATING → FIXED → ARCHIVED

**Bug File Template**:
```markdown
# [Bug Name]

**Date**: [Date discovered]
**Status**: 🔴 ACTIVE / 🟡 INVESTIGATING / 🟢 FIXED
**Severity**: 🔴 CRITICAL / 🟡 HIGH / 🟢 MEDIUM / ⚪ LOW

## Symptoms
- [What's broken]
- [How to reproduce]

## Root Cause
[Analysis of why it happens]

## Investigation Notes
[Findings, attempted fixes, related issues]

## Related Files
- `path/to/file.py` - [Description]
- `docs/reference.md` - [Description]

## Resolution
[How it was fixed, or current status]
```

**Why This Position**: Comes after Testing (#12) because bugs are often discovered during testing. Comes before Documentation (#15) because bug tracking is part of the development process, not final documentation.

---

## 1️⃣4️⃣ Recent Changes File Rule

**Core**: Always update `RECENT_CHANGES.md` AFTER bug fixes but BEFORE any commit suggestions.

**Purpose**: Track changes across multiple concurrent chat sessions for consolidated commits.

**When to Update**:
- ✅ After completing a bug fix
- ✅ After implementing a feature
- ✅ After making any code changes
- ✅ BEFORE suggesting a commit

**Process**:
1. **Add entry** to `RECENT_CHANGES.md` under "Current Session Changes"
2. **Include**:
   - Issue description
   - Root cause
   - Changes made (files, line numbers)
   - Testing status
3. **Before commit**: Read ALL entries, consolidate into commit message
4. **After commit**: Archive entries to `RECENT_CHANGES_ARCHIVE.md`

**Entry Template**:
```markdown
### Session [N]: [Feature/Fix Name] ([Date] [Time])

**Issue**: [Brief description]
**Root Cause**: [Why it happened]

**Changes Made**:
1. **Code - `file.py`**
   - [Description of changes] (lines X-Y)

2. **Documentation**
   - [Files created/updated]

**Testing**: ✅/❌ [Status]

**Files Modified**:
- `path/to/file1.py`
- `path/to/file2.md`
```

**Multi-Session Workflow**:
- Session A: Fixes bug X → Updates RECENT_CHANGES.md
- Session B: Fixes bug Y → Updates RECENT_CHANGES.md
- Session C: Ready to commit → Reads RECENT_CHANGES.md → Consolidates all changes into one commit

**Why This Position**: Comes after Bug Tracker (#12) because you track bugs first, then record changes. Comes before Documentation (#15) because recent changes tracking happens during development, while documentation happens after all work is complete. Comes before Commit (#16) because you must update recent changes BEFORE suggesting commits.

---

## 1️⃣5️⃣ Documentation Timing Rule

**Core**: Document ONLY AFTER ALL issues resolved and work complete. Create ONE comprehensive doc.

**When to Document**:
- ✅ All issues resolved + user confirms works + no more changes expected
- ❌ NOT during: individual fixes, design iterations, testing cycles

**Sequence**:
1. User confirms testing complete
2. User confirms ALL issues resolved
3. Create ONE comprehensive doc (all fixes together)
4. Update `docs/core/DOCUMENTATION_INDEX.md`
5. Suggest git commit

**Format**: Single doc with all fixes, solution overview, before/after, testing results.

---

## 1️⃣6️⃣ Commit Protocol Rule

**Core**: Commit code + docs together AFTER testing confirmed and docs updated, then PUSH to GitHub.

**Before Commit**:
1. Testing confirmed (Rule #12)
2. Bug tracker updated if applicable (Rule #13)
3. RECENT_CHANGES.md updated (Rule #14)
4. ALL issues resolved (Rule #15)
5. Documentation updated (Rule #15)
6. Rules followed sequentially
7. Race conditions reviewed (Rule #7)
8. RE verification completed if applicable (Rule #10)

**Execution Sequence**:
1. `git add -A` - Stage all changes
2. `git commit -m "[message]"` - Create commit locally
3. `git push origin main` - **CRITICAL**: Push to GitHub remote

**Commit Message Format**:
```
[Phase X.Y] Brief description

- Change 1
- Change 2

Closes #issue
```

**Zero Tolerance**: ALWAYS push after commit. A commit is NOT complete until it's on GitHub.

---

**END OF RULES**
