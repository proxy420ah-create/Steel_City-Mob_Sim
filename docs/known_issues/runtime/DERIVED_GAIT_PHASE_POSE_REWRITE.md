# Derived Gait Phase — Pose "Rewrite" On Speed/Duck Change

**Date**: 2026-10-19
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH

## Symptoms
- In `character_animator.html`: enabling Duck→Speed and scrubbing the Duck slider mid-stride visibly "rewrote" the walk cycle — the pose jumped to a different stride frame on every change instead of smoothly decelerating.
- Repro: Walking + Duck→Spd ON → drag duck from 0 toward 2 → feet teleport to a new gait phase at each slider step.

## Root Cause
Gait phase was **derived**, not integrated:
`cycleDur = walkKeyframes.cycleDuration / animSpeed`, then
`phase = (animTime % cycleDur) / cycleDur`.
Every `animSpeed` change (speed slider, duck coupling) rescaled the modulus
denominator, so the same `animTime` landed on a different normalized phase —
an instant phase teleport at every rate change.

## Fix
Integrated clock: `gaitTime += dt × effectiveSpeed` per frame; phase derives
from `gaitTime` at the fixed base cycle duration. Speed changes alter the
*rate of advance* only — the current phase is never recomputed.

- HTML: `gaitTime` clock + wrappers (`getWalkPose`/`getCrouchWalkPose` feed it
  at speed 1.0); reset on stop/scrub; NOT reset on state change (transitions
  carry stride phase).
- Unity port: `CharacterAnimation.gaitTime` integrates
  `dt × walkSpeed × DuckSpeedFactor`; rides `_InstanceAnimData[i*3+2].z`;
  `VoxelCharacterAnimator`/`CharacterPoseCompute` call `GetWalkCyclePhase(gaitTime, 1.0)`.
  CPU fallback `gaitTime<0 → animTime×animSpeed` preserves legacy callers.
- Look Scan / Aim Sweep stay on wall-clock `animTime` (a ducked sentry scans
  at normal rate while shuffling slowly).

## Related Files
- `VoxelAssetStudio/character_animator.html` — `gaitTime` clock + wrappers
- `Assets/Scripts/Sim/CharacterAnimation.cs` — `gaitTime`, `DuckSpeedFactor`
- `Assets/Scripts/Sim/VoxelCharacterAnimator.cs` — `gaitClock` plumbing
- `Assets/Resources/Shaders/CharacterPoseCompute.compute` — slot [2].z
- `Assets/Scripts/UI/VoxelChunkManager.cs` — `InstancedCharacter.gaitTime`

## Resolution
FIXED 2026-10-19 — phase-continuous rate changes verified in the HTML
prototype; Unity side ported as part of the scalar/channel model (all 82
project .cs compile clean via Unity 6.0.3 Roslyn).
