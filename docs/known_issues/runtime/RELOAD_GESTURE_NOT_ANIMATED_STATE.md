# Reload Gesture Reads Wrong — Solver Override Instead of an Animated State

**Date**: 2026-10-28
**Status**: 🟡 FIX APPLIED (pending playtest)
**Severity**: 🟡 HIGH

## Symptoms
- During reload the support forearm appeared to rotate **back into the upper arm** instead of coming forward and center.
- The gun (right) arm stayed frozen in its last aim pose — the reload gesture could not smoothly lerp it over.
- After an FK-override rewrite, the pose still "looks wrong" (user screenshot: both shoulders flared outward like a shrug).

## Root Cause
Two stacked defects:

1. **Gesture was a runtime solver, not an authored animation.** v1 drove arm chains via a pivot-space two-bone `BasisDelta` reach (forearm aligned to a hand point); elbow hints authored for one side folded the other side through the torso. v2 composed authored-convention quats on the IK override channel, but overrides bypass the anim JSON's authored values — the pose existed only as solver numbers nobody could see or tune.

2. **Single arm-IK pair.** `_InstanceIKData` originally carried ONE arm pair (gidA/gidB). Whichever arm the gesture drove owned the channel; the other arm kept its last authored/aim pose — hence the frozen gun arm.

## Resolution
Reload became a **real authored animation**, evaluated in the same branches as every other state — the user directive: *"refer to AimSweep during recon and how the arm sweeps for directional input"*.

- **States 13 `Reloading` / 14 `ReloadCrouch`** added across `CharacterAnimation` enum, JS studio (`character_animator.html`), CPU (`VoxelCharacterAnimator`), GPU (`CharacterPoseCompute.compute`).
- **`reload`/`reloadCrouch` param blocks** — studio sliders + defaults + tap preview; ASP rows 27–34 on the GPU; `Missing()`-fill defaults in `LoadFromAnimJson` for old JSONs.
- **Arms use the AimSweep convention**: `arm = RotationY(reach) · RotationByAxis(axis, sign·swing) · RotationZ(restZ)` — `reach` is the directional channel the sweep modulates.
- **`tapPulse` channel (0..1)**: `animData` stride 3→4 (`[inst*4+3].x`); `ReloadGestureSolver` reduced to a tap driver (RoundSeated → SimClock envelope → `animRef.tapPulse` → instanced handle). Support arm adds `tapPulse·tap*` deltas — press lands on the reload_click tick.
- **State 14 rides the crouch body channel** (`isCrouchBody` widen) — legs/lean/lower come from `crouching→crouchDeep`, only arms/head/twist come from `reloadCrouch`.
- Drivers: `CombatController` stationary lane, `PossessController` (real reload + Alt+R/Alt+E latch), `CityMap3D.debugAnimStates`.
- **Second arm-IK pair** (stride-7, nibble-packed, `ikPair2Enabled`) shipped during v1/v2 and remains — dormant now, it is the phase-4 two-handed (rifle) channel.

## Related Files
- `Assets/Scripts/Sim/VoxelCharacterAnimator.cs` — reload branch + `ReloadData` + defaults fill
- `Assets/Scripts/Sim/CharacterAnimation.cs` — states 13/14 + `tapPulse` field
- `Assets/Scripts/Sim/ReloadGestureSolver.cs` — tap-pulse driver only
- `Assets/Scripts/Sim/CombatController.cs` — `reloading` first in the stationary pose pick
- `Assets/Scripts/Sim/PossessController.cs` — reload pick + Alt+R/Alt+E test latch
- `Assets/Scripts/UI/VoxelChunkManager.cs` — `tapPulse` channel + animData stride 4
- `Assets/Resources/Shaders/CharacterPoseCompute.compute` — stride-4 reads + reload branches
- `VoxelAssetStudio/character_animator.html` — states, param blocks, sliders, tap preview
- `Assets/Scripts/Sim/VoxelCharacter.cs` / `WeaponMount.cs` — `anim.tapPulse` mirrors (CPU eval parity)

## Notes
- Transitions morph via the standard pose-transition path (Aiming→Reloading slerp), which is the "smoothly lerp over" the report asked for.
- Tap timing = `RoundSeated`/`SimClock` — deterministic, freezes under F8, steps under '.'
- **Unity L/R flip**: authored L/R columns landed on flipped gids in-game, so the gun arm played the "support work" pose. Fixed by role-assignment — L column = support role, R column = gun role, applied to whichever gid `mount.HandGid`'s parent arm is (`reloadGunSide` ±1 in `animData[3].y`); direction-sensitive angles (reach/twist) mirror by the sign. The tap now always lands on the non-gun hand regardless of flip or handedness.
