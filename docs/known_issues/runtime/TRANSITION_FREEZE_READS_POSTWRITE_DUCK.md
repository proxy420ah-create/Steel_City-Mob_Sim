# Transition freeze read post-write duck — cover exit snapped upright

**Date**: Oct 20, 2026
**Status**: 🟢 FIXED
**Severity**: 🟢 MEDIUM — visual continuity only, no state corruption

## Symptoms

Stepping out of a cover band while ducked (duck gait or Crouching hold)
snapped the pose to upright in one frame. Entering cover the other way looked
smoothly lerped — asymmetric transitions for the same state flip.

## Root Cause

Write-order race inside the flip frame:

1. `PossessController` drive writes `anim.duck = 0` (out of the band) and
   `RequestPose`s the uncovered pick (Walking→AimWalk / Crouching→Aiming).
2. `CharacterAnimation.Update` detects the state change and freezes the
   outgoing pose — `transFromDepth = duck` — reading the scalar AFTER the
   drive zeroed it.
3. The "frozen" outgoing pose therefore evaluated **un-ducked**: the blend
   lerped upright→upright and the fold vanished instantly.

Entry looked smooth by contrast — but the same flaw ran there too
(`transFromDepth` captured the already-written deep duck, so the outgoing
pose rendered folded); the pose-swap blend simply masked it.

## Fix

`CharacterAnimation` snapshots `prevFrameDuck` at the end of every Update
(after all writers have run) and `transFromDepth` freezes THAT value. The
outgoing pose always renders the pre-write duck, so the blend is an honest
folded→upright un-fold on exit and a genuine upright→folded fold-in on entry.
Single authority — `ic.transDepthA` feeds both the GPU (`_InstanceAnimData.y`)
and CPU (`PosePoint(transDuckA)`) evaluators.

## Related Files

- `Assets/Scripts/Sim/CharacterAnimation.cs` — `prevFrameDuck` field, freeze
  site, end-of-Update snapshot
- `docs/systems/ANIMATION_SYSTEM.md` §6 — transition freeze contract
