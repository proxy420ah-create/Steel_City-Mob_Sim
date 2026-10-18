# Cover Pick Reads Wrong Volume in Splat Overlap (+ No Full-Wall Stand Path)

**Date**: 2026-10-14
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH

## Symptoms
- Every pit pick (`deep`/`mid`/`crouch`) put Vinny at `crouchDepth ≈ 1.0` — no shallow or intermediate duck.
- Picking the 2.0m `wall` (Full class — "just stands behind") still crouched him.

## Root Cause
Two defects at the pick seam:

1. **`CoverField.Splat` ownership** — each wall splats a `standoff`-radius disc claiming cells for the *highest-quality* wall (`qw`: Full 1.0 / Crouch 0.7), and ties go to whichever wall splatted first (`qw > quality[idx]` is strict). The four pit boxes' discs overlap, so cells spatially nearest `cover_mid`/`cover_crouch` were owned by `cover_deep` — `volIdOf` → `volDepth` returned 1.0 for neighboring boxes. `PickNear` found the right *cell* but read the wrong *wall's* depth.
2. **`CrouchAndCover` unconditionally requested Crouching** — a Full-class wall's `reqDepth` is 0, and depth-0 crouch is still a crouch. There was no "stand behind tall cover" path at all.

## Fix
- `CoverField.PickNear(anchor, maxDist, out point, out volId, requireVol)` — optional owner pin.
- `CoverSensor.TryPickCoverNear` — resolves the *wall* by proximity to the anchor first (nearest non-Low `facePos`), gets `volId`/`reqDepth`/`cls` straight from the `walls` list, then asks the field for that volume's nearest Hidden cell. No validated cell owned by that box → pick fails honestly.
- `volClass` dict (paired with `volDepth` — class of the volume's highest-demand wall) + `TryPickCover` reports class on the natural path too.
- `CombatController` carries `coverWallClass`; `CrouchAndCover` requests **Idle** behind Full walls (stands hidden) and Crouching otherwise. Hold-position defaults to Crouch.

## Related Files
- `Assets/Scripts/Sim/CoverField.cs` — `Splat` ownership rule, `PickNear` `requireVol`
- `Assets/Scripts/Sim/CoverSensor.cs` — `TryPickCoverNear` wall-first resolve, `volClass`
- `Assets/Scripts/Sim/CombatController.cs` — `coverWallClass`, stand-vs-crouch in `CrouchAndCover`
