# Construct Surface-Climb Family — Placed Assets Ratcheting Upward

**Date**: November 16, 2026
**Status**: 🟢 FIXED
**Severity**: 🔴 CRITICAL (visible every F5 — door airborne, tables elevated, items on floor)

## Symptoms
- Door + doorframe floating ~.43m (later +.85m per Enter commit — "keeps floating away").
- Hand-placed S&W Model 10 restored on the floor, not the tabletop.
- Items table elevated .34 after rearrange + F5; its passengers at .64.
- Second desk extra restored at .135, stamper on the floor.

## Root Cause — three stacked defects, one family

Support relationships were **inferred from geometry**, and every inference had a hole:

1. **No seat at all**: `emitAsset` only honored `on`/`seatY`; `/cmd emit`, paste, and
   boot-restore passed neither → every extra landed `y0=0` ("gun on floor").
2. **Restore order**: extras emitted BEFORE `dvm_place` overrides — the gun seated
   where the desk wasn't yet. And a manifest passenger kept its stale emit-seat at
   its override spot — an extra desk emitted onto the *passenger's* top.
3. **Ghost geometry**: `surfaceAt` used rec x/z (parent-local) — the car's surface
   map floated at the origin under the door (car lives in `carG` at (0,−2.5)).
   And `p.bbox` was computed once at emit, never updated by `placeApply` — moved
   items kept footprints at their old spots.

Each fix exposed the next: once seats existed, the settle/re-seat passes let items
*climb* — door onto frame, frame onto door, desk onto passenger — because nothing
declared which recs may host which.

## Resolution — surface-role law (commit `d483154`)

`PLACED` records carry `surf` — `assetType ∈ {furniture,vehicle,room,building}` OR
`!pickable` → surface; else rider. `placeSurf`/`worldSurfaceAt` take `{surfOnly,cap}`:

- `surfOnly` — furniture's floor query can't see riders (emit `'auto'`, B, `/cmd move`,
  `settleOrphans`, `placeSpot`).
- `cap = p.y0 + .01` — the boot fixpoint is settle-only; nothing ever climbs.

Riders conform freely at emit/place/move. Canonical doc: `docs/CONSTRUCT_PLACEMENT.md`.

## Related Files
- `docs/construct.html` — `emitAsset` (surf derive + 'auto'), `placeSurf` opt,
  boot fixpoint, `settleOrphans`, `footB` live bbox
- `docs/_place_probe.mjs` — cascade regression + role assertions (ALL PASS)
- `docs/CONSTRUCT_PLACEMENT.md` — the locked contract
