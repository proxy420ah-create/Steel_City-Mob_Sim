# CONSTRUCT — boot fixpoint races the async mini mounts

**Date**: 2026-08 (flashlight-controls session)
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH — nondeterministic seat loss on reload

## Symptoms
- A pad (or any placed item) left on a table **miniature's roof** sometimes came back on the **felt** after F5.
- Flaky — passed most runs, failed on slow stasset parse times. Caught by `studio roof seat survives F5` returning `.26` instead of `.7475`.

## Root Cause
`mountModel(t, mspec)` returns the async `emitBldg`/`emitBlock` promise — the `MANIFEST.tables.forEach` boot loop **discarded it**. The mini mounts fire and forget, so a table's `tops` columns populate whenever the fetch+parse finishes.

The `ready.then` fixpoint passes read `tops` via `placeSurf` with `cap: p.y0 + .01`. If a mount hadn't resolved yet:

1. saved override restores `p.y0 = .7475` (the roof seat) ✓
2. fixpoint runs while `tops` still reads felt → best surface under the cap = `.26`
3. `changed` → pad re-seats to `.26`, permanently
4. mini finishes loading, roof tops appear — too late

Same race bit extras too: `seatY:'auto'` reads `tops` at emit — an extra over a mini could seat on felt.

## Fix
`mountModel` now assigns the promise to `t._mountP`; `ready.then` awaits
`Promise.all(TABLES.map(t => t._mountP))` **after saved lifts, before extras emit and the
fixpoint passes** — the first two consumers of `tops`. One line each; the ordering law
stays "everything deterministic before anything that reads surfaces."

## Verification
- `_place_probe.mjs` `studio roof seat survives F5` → `.7475` (was `.26` on the failing run)
- Full suite ALL PASS.

## Related Files
- `docs/construct.html` — `mountModel` (~line 552), boot sequence (~line 1876)
- `docs/_place_probe.mjs` — §12.99d placed-seat persistence checks
- `docs/CONSTRUCT_PLACEMENT.md` — boot order table
