# Construct Pad — Wall-Mount Hijack at Annex Tables + Saved Seat Floored on Reload

**Date**: November 15, 2026
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH (pad unplaceable on 2 of 3 tables; any elevated seat lost on F5)

## Symptoms
- Debug pad placeable on Table 1 (primary) but not Tables 2/3 (block/studio) —
  near the annex wall the E-drop offered a wall-stick instead of the felt.
- After placing on a rear table and reloading, the pad restored to the floor —
  actually to emit-height `.26`, buried inside the tabletop miniature.

## Root Cause — two defects, one report

1. **Wall-mount hijack**: `dropTarget` calls `padWallTarget` first for pads, and
   `padWallTarget` raycast ONLY the `wallSlab` set — a hit within 2.4 m won
   unconditionally. Aiming at the annex tables carried the ray through to the
   wall slab BEHIND the table → the mount branch stole the drop. The primary
   has no wall behind it, so Table 1 looked fine.
2. **Emit-height re-seat cap**: `dvm_place` carried no `y`, so the boot fixpoint
   capped re-seat at emit-height `.26+.01`. A pad left on the block composite's
   roof (`.556`) or street ring had no reachable surface → settled to `.26`
   under the model (visually: the floor).

## Resolution

- `padWallTarget` now runs the pruned aim-family raycast (`rayPruned`, proxies
  live) — **the wall must own the ray**: the first surface hit must be a
  `wallSlab` within 2.4 m, else the drop-march proceeds. Bare-wall aim still
  mounts.
- `placeSave` writes `o.y` (placed seat) for non-mount items; boot restores
  `p.y0 = o.y` before `placeApply` — the cap honors the real seat.
- Saved lifts (`dvm_lift:<id>`) moved from `queueMicrotask` to a boot step
  AFTER overrides and BEFORE extras/fixpoint — `liftRiders` bumps riders from
  their restored seats (a `queueMicrotask` raced the override restore and
  clobbered the bump → capped fixpoint floored the pad).

## Related Files
- `docs/construct.html` — `padWallTarget` (own-the-ray law), `placeSave` (`o.y`),
  boot `ready.then` (override → lift → extras → fixpoint order)
- `docs/_place_probe.mjs` — annex E-drop checks + roof/studio/margin seat-persist
  reloads (ALL PASS)
- `docs/CONSTRUCT_PLACEMENT.md` — save schema + boot-order contract updated
- `docs/CONSTRUCT_DRAFTING.md` — pad wall-mount law updated
