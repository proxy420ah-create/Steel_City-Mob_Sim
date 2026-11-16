# Construct Mini-Mount Lifecycle Races — Stale Top, Ghost Composite, Orphan Recs

**Date**: November 16, 2026
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH (block visibly hung under the tabletop on affected boots; ghost composite on fast pad swaps)

## Symptoms
- Block composite loads **under the table** — mounted at slab-origin height while the
  tabletop sits a saved-lift higher. Adjusting the lift snaps it up; F5 re-breaks it.
- (Latent) Pad "next" during a slow mount → two overlapping composites + corrupted tops.
- (Latent) Deleting a table leaves its parked-car recs in `PLACED` as ghost hosts.

## Root Cause — the mount's lifecycle stages weren't coordinated with world-state changes

`emitVox` runs three observable stages — entry, `t.g.add(miniG)` (line ~650),
`userData.mini` flag + `miniSyncTops` (line ~691) — and boot/interaction events land anywhere inside it:

1. **Stale `top` capture**: `top = tableTop(t)` was read at entry and consumed ~60 lines
   later. The boot lift-restore (`ready.then`, after `ready`) lands mid-emit; the group
   positions at the pre-lift top. `tableLiftSet`'s mini-reposition only fixes groups
   **flagged at that instant** — a mini created-or-flagged after keeps the stale height.
2. **Ghost composite on swap**: `mountModel`'s cleanup filters `c.userData.mini` — an
   in-flight emit's group is attached *unflagged*, survives the swap, then flags and
   writes tops over the new model. (The parked cars got a `!miniG.parent` liveness guard
   earlier — the mini itself didn't.)
3. **Orphan recs on table delete**: `del` spliced only the table rec; descendant recs
   (parked cars under its miniG) stayed in `PLACED` — ghost hosts, and un-delete
   re-mounts a duplicate set.

## Resolution

- `emitVox` reads `tableTop(t)` fresh at group creation **and settles
  `miniG.position.y = tableTop(t)` at completion** — the mount lands on the pedestal's
  real height regardless of where the lift-restore fell inside the emit.
- `mountModel` bumps `t._mountGen`; `emitVox` captures it at entry and bails
  (`return null`) before group attach and before flag/tops — same liveness contract as
  the parked-car `!miniG.parent` guard. Null-safe: `emitBlock`/`emitZone` already gate
  on `if (miniG)`.
- Both del paths (`__place.del`, `/cmd del`) splice `PLACED` recs whose group descends
  from the deleted table's group.
- `liftRiders` gains `q.g.parent !== scene` — completes the parented-rec frame law
  (fixpoint, `settleOrphans`, `carryOf`, selection already enforce it).

## Verification
- `_mini_lift_race.mjs` (saved `.25` lift + delayed stasset → emit straddles restore):
  BEFORE → `minis [{y:.26}]` under `.51` top; AFTER → `.51`, cars `.5164`. Three runs.
- `_car_measure.mjs`: swap path unchanged (`.2664` cars, `block_41` mounts).

## Related Files
- `docs/construct.html` — `emitVox` (~L632/650/696), `mountModel` gen bump (~L756),
  `liftRiders` (~L366), del paths (~L1498/2002)
- `docs/known_issues/runtime/CONSTRUCT_MOUNT_PROMISE_TOPS_RACE.md` — sibling: mount
  promise vs fixpoint (`_mountP` await)
- `docs/known_issues/runtime/CONSTRUCT_PARKED_CAR_WORLD_SWEEP_DROP.md` — same session's
  frame-law fix for parked-rec sweeps

## Postscript (2026-11-16) — parked-car leg structurally closed
The block table now mounts cars on a **static street ring** (`spec.ring`, `mn.static`) that
never enters `mountModel`'s unmount sweep — the swap/remount race class this doc cataloged
cannot recur for parked cars: they are never unmounted. The mid-emit swap guards remain in
force for the interior core and all other mounts. See `CONSTRUCT_TENEMENT_CURB_STRADDLE.md`.
