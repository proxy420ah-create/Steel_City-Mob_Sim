# Relocate Target Collapses to Current Position — "Cancel + Re-Settle"

**Date**: 2026-10-20
**Status**: 🟡 FIX APPLIED (pending playtest)
**Severity**: 🟡 HIGH

## Symptoms
- Vinny reaches cover, tops up through opportunity fire, then a refused rise
  (or the buried-bore verdict) correctly triggers `TryRelocateToFightingCover`.
- Instead of driving to the new position, he "cancels": ducks back down and
  immediately runs a new `Settle` at ~where he already stood.

## Root Cause
The relocate target was the **face-anchored column spot**
(`facePos + outDir·(standoff + vs·0.25)`) of the *nearest* unfailed,
rise-solvable face. On the terraced wall in the test scene (stepped sections
along one volume), the nearest fightable face's anchor lands within
`arriveEps` (0.18 m then — now 0.06 m with exact sub-eps placement, GOTCHAS #13) of where he is already standing → the SeekingCover
arrival check fires the same frame → instant `InCover → Settle`. The relocate
*worked*; it just had nowhere to go, and it looked like a cancel.

No actual mid-seek cancellation path exists: `TickBeat` only runs inside
`InCover`, `TickExposureFire` never touches state, and nothing rewrites
`CoverPoint`/`targetDuck`/`CoverObstacleId` between commit and arrival — the
structural atomicity was already intact. The failure was a degenerate target.

## Fix
**Stand-point centroids** (`CoverField.TryZoneCentroid(volId, anchor, radius)`):
the relocate target is now the centroid of the chosen volume's Hidden-validated
cells near the face anchor — the middle of the painted cover gradient at its
mean probed ground height — i.e. a real, reachable, *distinct* destination:

- Each candidate face anchors, takes the zone centroid within
  `relocateCentroidRadius` (1.6 m), re-validates `TryZoneAt` **at the
  centroid**, and runs `SolveRiseAtSpot` at that exact stand point — the
  verdict now applies to where he will actually stand.
- `CoverPoint` commits once to the cached centroid; the only
  `SeekingCover → InCover` transition remains `dist ≤ arriveEps` at that point.
- Sparse zones (<3 cells) fall back to the face anchor.
- `LastRelocateDiag` now prints `stand@(x,z)` — the centroid, not the face.

## Related Questions Resolved
- *"Can character movement / weapon swing moving the muzzle ray cause
  miscalculations?"* — no: `BoreBuried` (the live `mount.MuzzleWorld` check)
  only runs inside `Engaged`; it is never evaluated during SeekingCover, so a
  swinging barrel cannot cancel a relocate. Candidate validity uses the
  hypothetical `SolveRiseAtSpot` at the committed centroid; the real muzzle is
  re-checked only post-arrival with `boreBurySec` hysteresis absorbing swing.
- *"Are relocation positions cached and targeting the cover's ground spot?"* —
  now yes by construction: `CoverPoint` = the centroid, written once at commit.

## Related Files
- `Assets/Scripts/Sim/CoverField.cs` — `TryZoneCentroid`
- `Assets/Scripts/Sim/CombatController.cs` — `TryRelocateToFightingCover`,
  `relocateCentroidRadius`, seek-arrival block
- `docs/systems/ENGAGEMENT_BEATS.md` — stand-point centroid section

## Resolution
Pending playtest — expect `RELOCATE` in the flow lane, a `relocate →` diag
naming `stand@(x,z)`, a real duck-gait move to the band middle, then the full
`SETTLE → TOPUP → SETTLE → RISE → ENGAGED` replay.
