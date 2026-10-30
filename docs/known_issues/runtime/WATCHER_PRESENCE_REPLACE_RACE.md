# Watcher Ear Killed by Presence-File Replace Race (WinError 5)

**Date**: 2026-10-30
**Status**: 🔴 ACTIVE
**Severity**: 🟡 HIGH

## Symptoms
- `commission_watch.py` dies mid-run with:
  `PermissionError: [WinError 5] Access is denied: presence_<SEAT>.js.tmp -> presence_<SEAT>.js`
- Ear goes silent; agent stops waking until a relay notices and re-arms.

## Root Cause
`stamp_presence()` does `os.replace(tmp, p)` unguarded. On Windows the
replace fails if `presence_<SEAT>.js` is open elsewhere — seen live when
two MEMBER3 watcher relays ran concurrently (duplicate arms) and raced
the same file. Transient locks (AV/indexer) can hit the same path even
single-writer.

## Investigation Notes
- Observed on 60c06fc7 relay; exit code 1, ear pulse froze ~20s stale.
- Housekeeping should never be fatal: the poll loop is the product, the
  stamp is telemetry. A missed tick self-heals next tick; a crash doesn't.
- Fix shape (when routed): wrap `os.replace` in try/except (or small
  retry) and continue polling — ~3 lines in `stamp_presence()`.
- Contributing factor noted: relay discipline should keep ONE ear per
  seat; duplicate arms invite the race regardless.

## Related Files
- `docs/commission_watch.py` — `stamp_presence()` (~line 27-37)

## Resolution
Pending — routes through the table (Rule Six).
