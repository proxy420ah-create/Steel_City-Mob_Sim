# Cover Pit Ladder Authored at Wrong Voxel Scale (0.1m assumption vs 0.05m world)

**Date**: 2026-10-15
**Status**: 🟡 FIX APPLIED (pending runtime test)
**Severity**: 🟡 HIGH

## Symptoms
- `wall` pick put Vinny into a *crouch* behind the 2.0m box instead of standing.
- `🥊 [Combat]` log: `picked block_45_building (Crouch, reqDepth 0.00)`.
- `🧱 [CoverPick]` diagnostic: nearest wall column to the `cover_wall` anchor
  reported `top+1.0m` — the "2.0m wall" measured as 1.0m of contiguous solid.

## Root Cause
`Tools/gen_cover_pit.py` authored heights assuming **0.1m/voxel**
(`top = h*0.1m`, `voxelSize: 0.1` in the emitted JSON), but the entire city —
terrain, blocks, buildings — is placed at **0.05m/voxel**
(`CityMap3D.voxelSize = 0.05f`, forced at line ~529 as the "2x-upscaled"
convention: `SectorBaker` → `info.voxelSize` → `RegisterVolume` → `v.vs`).

Detection was correct end-to-end and *reported the truth*: the wall box's
column is 20 contiguous voxels = 20 × 0.05 = **exactly 1.0m**. Verified by
reading the `.stasset` bytes directly (column y0–19 solid, header
192×48×192, X-major/Y-mid/Z-slow order matching `EnsureColumns`' indexing).
Every ladder box was half its intended height:

| Box | Voxels | At 0.05 (actual) | Intended |
|-----|--------|-------------------|----------|
| deep | 8 → 0.40m | 0.80m |
| mid | 10 → 0.50m | 1.00m |
| crouch | 13 → 0.65m | 1.30m |
| wall | 20 → **1.00m** | 2.00m |

A 1.0m span can't cover the 1.35m standing eye → class Crouch, not Full —
exactly the reported `top+1.0m Crouch`.

## Fix
- `LADDER` heights doubled for the 0.05m convention:
  deep 16 / mid 20 / crouch 26 / wall 40 voxels → 0.8/1.0/1.3/2.0m tops.
- `voxelSize` in the emitted JSON corrected to `0.05`; header/ladder comments
  re-derived in meters (box 1.2m×0.4m, gaps ~1.0m, row ~3.4m ahead).
- `cover_pit.stasset` rebuilt; all four columns verified in the binary:
  16/20/26/40 voxels contiguous = 0.80/1.00/1.30/2.00m.

## Architectural Note (for the "is detection volume-id-based?" question)
Detection IS surface/column-based: `SolidVolumeSet.EnsureColumns` walks every
(x,z) column of the registered array, records `colBase`/`colSpan` (first solid
+ contiguous run), and `EmitWalls` emits a world-space `CoverWall` per column
with an open side at the crouch-eye band. `block_45_building` is only the
*container* volume — all four boxes share its `volumeId`; the per-column
`CoverWall` carries the real geometry. Volume ids enter only at field
bookkeeping (`volIdOf`/`volDepth`/`volClass`), which is why
`COVER_PICK_SPLAT_OVERLAP_WRONG_VOLUME.md` needed the anchor→column resolve.

## Related Files
- `Tools/gen_cover_pit.py` — LADDER heights, voxelSize, comments
- `Assets/StreamingAssets/voxel_buildings/cover_pit.stasset` — regenerated
- `Assets/Scripts/Sim/SolidVolumeSet.cs` — `EnsureColumns`/`EmitWalls` (verified correct)
- `Assets/Scripts/UI/CityMap3D.cs` — `voxelSize = 0.05f` world convention

## Risk to Check
Other generators/authored assets with baked `0.1m` assumptions
(`gen_shooting_range.py`, `generate_empty_land`, older `.stasset`s) may have
half-height geometry the same way — worth a sweep if any other content reads
"too short".
