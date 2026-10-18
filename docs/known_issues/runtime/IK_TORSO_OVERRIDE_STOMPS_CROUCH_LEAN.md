# IK Torso Override Stomps Crouch Lean

**Date**: 2026-10-14
**Status**: 🟢 FIXED (design gate — posture doctrine)
**Severity**: 🟡 HIGH

## Symptoms
- In-game deep crouch showed kneeling legs but a nearly **upright torso** — the authored `bodyLean 0.67` (≈38°) from `crouchDeep` was visually absent, while the identical data leaned correctly in Voxel Asset Studio.
- Repro: pit testbed → pick `deep` cover (or `Num5` depth toggle) while the character has a weapon mounted and ArmAimSolver engaged.
- Turning the IK channel off made the authored lean appear immediately — confirmed by user.

## Root Cause
`CombatController.CrouchAndAim()` armed `ArmAimSolver` the moment the character reached cover. The solver's torso assist writes `ikOverrides[0]` — a **pure yaw** quaternion — every frame. `ApplyIKOverride` (compute + CPU mirror) nlerp-blends `bodyRot` **toward** that quat rather than composing on top of it, i.e. at blend≈1 it *replaces* the crouch lean rotation entirely. Legs kept their own-group crouch bends → kneeling legs + upright torso.

The pose eval, JSON parse, ASP upload, and depth lerp were all correct — the lean was being erased downstream of posing.

## Fix
Semantically gated per design doctrine: **crouched = taking cover = not aiming**.

- `ArmAimSolver.Arm()` rejects while `anim.EffectiveState == Crouching` → `LastError = "cannot aim while crouched (in cover)"`.
- `ArmAimSolver.Update()` releases all IK channels instantly when the effective state becomes Crouching (no blend-out — the crouch transition carries the arm down).
- `CombatController.CrouchAndAim()` → `CrouchAndCover()` — no longer arms the solver on cover arrival or the no-cover hold path; keeps the yaw-face toward the threat for the later pop-up phase.

## Notes / Residual
- `ApplyIKOverride` still *replaces* rather than composes (`slerp(baseRot → q)`). With the posture gate this no longer collides with crouch; if torso assist is ever wanted alongside another authored `bodyRot` (e.g. a lean in a future state), compose `q * baseRot` or have the solver include the base rotation in its quat.
- The pop-up/exposed phase (peek → acquire → ≤6 shots → duck → reload) is planned combat work; aiming lives there.

## Related Files
- `Assets/Scripts/Sim/ArmAimSolver.cs` — posture gate, `ReleaseInstant()`
- `Assets/Scripts/Sim/CombatController.cs` — `CrouchAndCover()` (was `CrouchAndAim`)
- `Assets/Resources/Shaders/CharacterPoseCompute.compute` — `ApplyIKOverride` replace semantics (line ~111)
- `Assets/Scripts/Sim/VoxelCharacterAnimator.cs` — CPU mirror of the same blend (line ~397)
