# Construct — Enter Drop Crashes on Huge-Composite Hosts (stack overflow)

**Date**: 2026-11-18
**Status**: 🟢 FIXED
**Severity**: 🔴 CRITICAL (silent crash leaves item grabbed; looked like a dead Enter key)

## Symptoms

- In place mode, moving `table:studio` (the big studio drafting table) then
  pressing **Enter did nothing** — item stayed grabbed, no commit.
- Smaller items (item table, both small drafting tables, doors, desk props,
  cars) planted correctly — the failure was per-item, not global.

## Root Cause

`placeDrop` computed the old host top as
`Math.max(...moved.tops.values())` (`13_place.js:145`). Spreading a Map's
values as function args pushes every entry onto the call stack — Chrome caps
argument lists around ~125k. `table:studio` carries **231,361** column tops →
`RangeError: Maximum call stack size exceeded`, thrown *before*
`placeSel = null`, leaving the selection grabbed forever.

Reproduced headlessly via `docs/_place_enter_sweep.mjs`: sweep-grabs every
PLACED record, dispatches a real Enter keydown, reports which stay selected.
1 of 13 stuck (table:studio) before the fix; 0 of 13 after.

## Fix

- New spread-safe helper `topOf(p)` in `02_placed.js` — manual max over
  `tops.forEach`, no argument spread.
- Applied at both call sites: `13_place.js:145` (placeDrop) and
  `09_live_ctl.js:36` (live-agent `move` orphan settle).

## Related

- Same session fixed `CONSTRUCT_CHAT_FIELD_STEALS_KEYBOARD.md` (chat input
  holding focus while hidden) — same symptom family (dead Enter), different
  mechanism; both verified.
