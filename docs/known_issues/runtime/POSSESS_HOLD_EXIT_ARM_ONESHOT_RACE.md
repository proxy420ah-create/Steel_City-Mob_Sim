# Possess Hold-Exit Arm — One-Shot Race With Pose Transition

**Date**: 2026-10-19
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH

## Symptoms
- Possess a character who was `InCover` (combatHold) → walk >0.25 m from the
  cover spot → he stands up into the `Aiming`/`AimWalk` pose but the IK aim
  never engages — no tracking, gun follows the authored pose only.
- Only recovers on the next possess cycle (`G` toggle re-arms at entry).

## Root Cause
`PossessController.Update()` combatHold block: on crossing the 0.25 m radius,
`combatHold = false` → `solver.Arm(aimPoint)` — a **single-shot** call in the
same frame. Two races stack against it:
1. `ArmAimSolver`'s posture gate reads `EffectiveState`, which is still
   `Crouching` — this frame's `RequestPose` (the new non-crouch pick) runs
   *later* in the same Update.
2. `crouchDepth` is still ≈holdDepth, so the pose pick keeps choosing
   `Crouching` until the cover drive eases depth ≤0.02 (~0.2 s).

`Arm` is rejected ("cannot aim while crouched"), `IsAiming` stays false, and
no code path ever calls `Arm` again — permanent no-IK state.

## Fix
After `HoldActive = combatHold;` added a per-frame retry:
```csharp
if (!combatHold && solver != null && solver.enabled && !solver.IsAiming)
    solver.Arm(aimPoint);
```
The solver's own posture gate keeps refusing while the drive holds the
`Crouching` pick; the retry lands the first frame the pick leaves Crouching.
Also auto-implements the pop-up/duck cycle: re-entering a crouch pick
releases IK via the solver's Update gate, re-exiting re-arms on the next
retry — no extra state needed.

## Related Files
- `Assets/Scripts/Sim/PossessController.cs` — retry added after `HoldActive`
- `Assets/Scripts/Sim/ArmAimSolver.cs` — posture gate (TryArm + Update)
- `Assets/Scripts/Sim/CombatController.cs` — `CrouchAndCover` pick source

## Resolution
FIXED 2026-10-19 — pending in-editor verification (possess → walk out of
cover → confirm IK arm engages once `EffectiveState` leaves Crouching).
