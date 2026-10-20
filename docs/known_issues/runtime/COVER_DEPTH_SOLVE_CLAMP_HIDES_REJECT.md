# Cover Depth Solve Clamp Hides the Reject Signal

**Date**: 2026-10-18
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH — drive crouched Vinny at depth 1.00 anywhere with a threat pinned

## Symptoms

- Possessed Vinny deep-crouched (`crouchDepth 1.00`) on open asphalt, beside
  bare pad, "nowhere near the arena wall" — user report.
- Metrics panel: all 8 ring probes read top `0.20` (pad floor inside
  `block_45_building`) yet `best d 1.00 → req 1.00`.

## Root Cause

`CoverSensor.DepthForTop` returns `Mathf.Clamp01(d)` — correct for its
original callers (wall `reqDepth` reports, pose feeding), but the clamp
destroys the class signal:

- True `d < 0` (wall covers him standing → Full) clamps to **0**
- True `d > 1` (column too low to hide → Low) clamps to **1.0**

The possess-drive probe consumed the clamped value directly as its
discriminator — every floor/pad column reported `d = 1.0` = "deepest usable
crouch" → `req 1.0` → Vinny folded to max depth wherever he stood.

## Resolution

New `CoverSensor.DepthForTopRaw(top, baseY)` — unclamped solve where the
raw value carries the class: `d <= 0` Full, `0 < d <= 1` Crouch band,
`d > 1` Low. `PossessController`'s probe ring uses it; `DepthForTop` stays
clamped for pose-feeding callers (`WallMark.reqDepth`, pick reports) where
>1 is filtered by `WallClass` anyway.

Same pass added probe-identity recording (`ProbeVol[8]`/`DriveBestDir` on
`PossessController`) — the metrics ring now names what each probe hit
(`t`=terrain / `v`=volume + winner's volId), so the next phantom column is
identifiable from a snapshot.

## Related Files

- `Assets/Scripts/Sim/CoverSensor.cs` — `DepthForTop` (clamped) vs `DepthForTopRaw`
- `Assets/Scripts/Sim/PossessController.cs` — drive probe ring
- `docs/systems/COVER_HEIGHTFIELD.md` — `h(d)` classifier spec
