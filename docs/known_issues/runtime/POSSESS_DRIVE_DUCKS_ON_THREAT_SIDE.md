# Possess cover drive ducked Vinny on the threat side of a wall

**Date**: Oct 20, 2026
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH — posture told a lie exactly where truth mattered

## Symptoms

With a threat pinned, walking Vinny along the **threat-facing** side of a wall
still ducked him (`cover: deep 0.70`, `zone Crouch duck1.70` in the metrics
panel), even though the COVER·THREAT layer correctly showed zero valid cover
cells on that side.

## Root Cause

Two independent posture paths, both threat-blind about *which side* he's on:

1. **Ring probes** (`PossessController`) — the 8 horizontal face-find rays hit
   any wall face within reach and only gated on axial alignment
   (`dot(dir,−normal) ≥ 0.85`). A face on the threat side passes every test —
   it IS a wall — so `ProbeD` solved a valid duck and `DriveCrouch` fired.
   Nothing asked whether the wall sits *between* Vinny and the threat.

2. **`CoverSensor.TryZoneAt`** — checked `volIdOf != null && quality >= SeedMin`
   but never `state[idx]`. The quality pass is deliberately threat-independent
   (both faces of a wall splat their own side), so the threat side's cells are
   claimed + painted, then skipped by the facing filter (stays `Untested`) or
   marked `Exposed` — and `TryZoneAt` returned their class/depth anyway.

## Fix

- `CoverField.BuiltWithThreat` records whether the build ran a real threat.
  `TryZoneAt` now returns false on any cell whose `state != Hidden` **when the
  field was built with a threat** — passive no-threat paint keeps the old
  claimed-cell behavior (inspection mode).
- `PossessController` ring: after the axial gate, each hit now applies the
  field's own facing test — `dot(fh.normal, dirToThreat) >= facingDot` ⇒ the
  face's open side points AT the shooter ⇒ he's on the exposed side ⇒ the
  probe doesn't count. `fh.normal` plays `outDir`'s role (out of the wall
  toward the hider).

## Verification

Threat side of the wall → `zone` readout blank, `req 0`, stands upright,
`coverContext` clear (aimable — exposed honestly). Hidden side → duck +
`coverContext` as before. Other probe directions still work — the gate is
per-direction.

## Related Files

- `Assets/Scripts/Sim/PossessController.cs` — ring probe threat-side gate
- `Assets/Scripts/Sim/CoverSensor.cs` — `TryZoneAt` Hidden-validated gate
- `Assets/Scripts/Sim/CoverField.cs` — `BuiltWithThreat` flag
