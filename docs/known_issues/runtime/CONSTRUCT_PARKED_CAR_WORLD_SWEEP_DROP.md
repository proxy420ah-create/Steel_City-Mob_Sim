# Construct Parked-Car World-Sweep Drop — Re-Seat Passes Read Parent-Local Coords as World

**Date**: November 16, 2026
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH (visible whenever the sweep sees the cars — sunk wheels every affected boot/move)

## Symptoms
- Parked cars sit "plainly on the table surface" — wheels buried one slab-height into the
  voxel road, car floor flush with the tabletop (world y `.26` instead of `.2664`).
- Session-dependent: some boots fine, some sunk (a ~76 ms emit-vs-fixpoint race); and
  sinking could appear mid-session after furniture moved through the room centre.

## Root Cause — same frame-confusion class as PARKED_REC_NAME_OVERRIDE_DISPLACEMENT

A parked car's `rec.x/z/y0` are **miniG-local** (it parents to the composite, not the scene).
Two world-frame re-seat sweeps consumed those coords as world coordinates:

1. **Boot fixpoint** (`ready.then`, ≤4 settle-only passes): iterated ALL of `PLACED` —
   `placeSurf(p.x, p.z, …, cap: p.y0 + .01)` at e.g. local `(0.003, 0.33)` reads the room
   centre → no host → `s = 0` → `p.g.position.y = 0`. Car bottom lands at slab origin =
   tabletop level; the road top passes `.0056` above the wheel bottoms.
   *Race*: the parked emit chain (`vehFetch` → `emitAsset`) wasn't awaited by `_mountP`, so
   the cars only got caught when `dvm_extra` restores widened the pre-fixpoint window
   (clean profile: cars land ~76 ms AFTER the sweep; 40 seeded extras: cars land before →
   dropped to `worldY .26`, reproduced deterministically).
2. **`settleOrphans`** (live move/del/undo/paste sweep): `inB(q, box)` tested the car's
   local x/z against world boxes — both cars' local coords sit inside the spinner car's
   floating world bbox (the spinner's own rec x/z are `(0,0)` carG-local) and inside any
   furniture box dragged through the room centre → `worldSurfaceAt` → `s = 0 < y0` → drop.

The spinner car (`carG` child, rec `(0,0)`, seatY `.001`) was a third victim in miniature:
the fixpoint measurably re-seated it `.001 → 0` every boot.

## Resolution

`g.parent !== scene` is the rec's world-frame test — the same filter `carryOf`, place-mode
selection, and `placeSpot` already use. Parented recs' coords are parent-local, so no
world-coords sweep can ever address them; they answer to their host's seat law
(`seatY` at emit, ParkingMap.Build curbside law, the spinner's turntable seat).

- Fixpoint guard: `if (p.mount || p.g.parent !== scene) return;`
- `settleOrphans` guard: `… || q.g.parent !== scene || …`

Parked cars stay valid `PLACED` **hosts** throughout — `surfaceAt` reads their world
transform, so riders still seat on car roofs. Only subject-side sweeps change.

## Verification
- `_parked_drop_repro.mjs` (40 seeded extras): BEFORE → `y0 0, worldY .26`; AFTER →
  `y0 .0064, worldY .2664` both cars. Spinner `.001` preserved.
- `_car_measure.mjs` regression: boot census + post-swap `MEASURE2` unchanged (.2664).
- `_parked_surf_probe.mjs` / `_parked_timing_probe.mjs` — mechanism + race instrumentation.

## Related Files
- `docs/construct.html` — boot fixpoint (~L2106), `settleOrphans` (~L1856), `mountParked` seatY law
- `docs/CONSTRUCT_PLACEMENT.md` — §3 gains the parented-rec exemption clause
- `docs/known_issues/runtime/PARKED_REC_NAME_OVERRIDE_DISPLACEMENT.md` — same class via name-keyed saves
- `docs/known_issues/runtime/CONSTRUCT_SURFACE_CLIMB_FAMILY.md` — sibling surface-law family

> **Postscript:** `mountParked` now returns `Promise.all` of the per-car emit chains and
> `emitBlock` awaits it — the race window cited at *Race* no longer exists; `_mountP`
> resolves only when the whole street (model + parked cars) is flagged.
