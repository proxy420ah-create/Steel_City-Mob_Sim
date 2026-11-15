# Name-Keyed Overrides Stamp Local Coords onto Shared Asset Names (Spinner Car Displaced)

**Date**: 2026-11-15
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH — silent persistence corruption; visually obvious but data-subtle

## Symptoms
- The showroom touring car on the rotator sat visibly ~.3 m off the ring center
  (user: "the show room car on the spinner got knocked off center").
- Overhead view: car clearly left-of-center in the rim ellipse.
- All transform data claimed centered (`g.position 0,0`, symmetric bounds,
  mass centroid within 2 cm) — the displacement lived in `rec.x/z`, not the
  group's emit coords.

## Root Cause
`placeSave()` writes `dvm_place` overrides keyed **by asset name** for every
non-`extra` rec. Parked-car overlay recs (`source:'parked'`, `_parked`) carry
**miniG-local** x/z (composite-cell coords of their street spots, e.g.
`.33, -.06`). The shared name `vehicle_490_touring` collided across three recs
(spinner + 2 parked), and a parked car's local coords landed in the override
key. At boot, the name-keyed override applied to **every** rec with that name —
including the manifest spinner → `placeApply` moved it ~.33 m off the ring.
Self-perpetuating: the moved car re-saved its wrong position.

The `dvm_del` tombstone find and the `agent.move`/`del` name resolution had the
same name-collision exposure.

## Fix
`_parked` recs are scenery owned by the table's street law, never place
citizens:
- `placeSave` skips `_parked` (never writes their local coords).
- Boot override apply skips `_parked` (name keys never reach them).
- `dvm_del` tombstone find skips `_parked` (not tombstone-able).
- `agent.move`/`del` name resolution skips `_parked`.
- Spinner car emit bumped `mv:1` — the *existing* stale `mv:0` override key
  self-invalidates via the manifest-rev mechanism (heals live sessions on F5).

## The Class (watch for)
Any rec whose `x/z` are **parent-local** (parented to a miniature/group, not
`scene`) must not participate in name-keyed world-coordinate persistence —
write AND read. `placePick`'s `p.g.parent === scene` filter already gates
selection; persistence needed the same law.

## Verification
- Live dump showed `vehicle_490_touring x:.33 z:-.06 manifest` + parked recs
  carrying local coords — matches the ~.3 m visual shift.
- Post-fix: parked cars skipped in save/apply/del/move; spinner `mv:1`
  invalidates the stale key.
- Suite: full `node _place_probe.mjs` run.

## Related Files
- `docs/construct.html` — `placeSave` (~1733), boot overrides (~2003),
  tombstones (~1998), agent move/del (~1401-1405), `mountParked` (~465),
  spinner emit (~289)
