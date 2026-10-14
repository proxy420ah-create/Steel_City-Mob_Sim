# Domino Orphan — Single Floating Voxel Left Behind After Settling

**Date**: October 13, 2026
**Status**: 🟢 FIXED
**Severity**: 🟢 MEDIUM (visual artifact, rare — needs an external write to hit a live domino head)

## Symptoms
- After a demolition's settling phase completes, exactly one voxel (or a few) remains suspended mid-air — often near ceiling height, keeping its material color.
- Observed on the tenement blast with `pairChance = 1.0`, `frontPasses = 6`.

## Root Cause
`ShardPhysics.StepFalling`'s stale-drop branch removes only the head's `falling` entry when a cell's voxel is overwritten externally (`data[idx] != fc.vox` — re-blast AIR write, crack swap, or a later shock-front pass touching a live domino). A domino **member owns no queue slot by design** — the head steps the pair. So `falling[m]` survived pointing at the shared `FallCell`, nothing ever stepped it again, and `data[m]` kept its voxel painted forever → floating cube.

## Investigation Notes
- Confirmed by code review: the member-adoption path existed for every *internal* split/escape/destruction case but not for the generic *external* stale-drop on the head.
- With `frontPasses = 6`, five subsequent wavefront writes run against cells that may already host live dominoes — the most likely real-world trigger.

## Related Files
- `Assets/Scripts/Sim/ShardPhysics.cs` — `StepFalling` stale-drop branch (fix site), `FallCell` domino fields
- `Assets/Scripts/Sim/VoxelDamageSystem.cs` — `SeedFalling` domino registration
- `Tools/dda_selftest/Program.cs` — regression test `[20] (d) orphan rescue`

## Resolution
**FIXED** — when a domino head (`fc.partner >= 0 && fc.headIdx == idx`) stale-drops, the member is adopted: if `data[m] == fc.vox2` it becomes a single `FallCell` with a momentum snapshot and gets its own queue slot; if its cell also died it is removed cleanly. Selftest `(d)` destroys a head mid-fall and asserts the member settles to rubble instead of hanging.
