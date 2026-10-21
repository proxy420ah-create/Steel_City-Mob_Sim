# Orphaned IK Arm Fights Settle Facing — Torso/Root Tug-of-War

**Date**: 2026-10-20
**Status**: 🟡 FIX APPLIED (bisected via kill-switches; event-driven belief landed — pending runtime verification)
**Severity**: 🟡 HIGH

## Symptoms
- During `Settle`, Vinny visibly **wiggles/shudders in place** — looks like the body wants to resolve toward the IK/aim heading while something else pulls it toward the settle facing.
- NOT camera-correlated (initial F6 free-cam correlation was ruled out by the user — happens with a still camera too).
- `transform.eulerAngles.y` stays put — the fight is **joint-space**, not root yaw.

## Repro
- Duel/cover arena: Vinny settles behind tall cover → subtle continuous wobble while `beat = Settle`, no external input.

## Root Cause
`CombatController.TickExposureFire`'s disarm gate:

```csharp
if (!wantAim && !holding && ExposureArmed && solver.IsAiming)
    solver.Disarm();
```

`ExposureArmed` is only true when **combat itself** armed the solver. Any arm granted by another path — the possess-entry `solver.Arm(forward·10)` (orphaned the moment `CamFree` yields possess input, since possess never disarms mid-yield), the VISION tab's ARM test button, or a pre-combat carryover — keeps `solver.IsAiming == true` **indefinitely inside combat**. The disarm gate never fires because `ExposureArmed` is false.

While armed, `ArmAimSolver.Update` keeps solving toward its **stale aim point** every frame: `torsoAssist` writes `ikOverrides[0]` (gid-0 torso yaw toward the target) and `PerturbTarget`'s aim-feel sway wanders the equilibrium continuously. Meanwhile the settle ease pulls the root toward `settleYawDeg` (back/lateral — deliberately AWAY from the threat bearing after the cover-posture redesign). Two drivers on one spine = the visible tug-of-war.

## Fix
The arm is **beat-owned outright** — the tick asserts sole ownership:

```csharp
if (!wantAim && !holding && solver != null && solver.IsAiming)
    solver.Disarm();
```

`ExposureArmed` kept only as the display flag, no longer part of the ownership check. Since `holding` also requires `BeatAllowsFire` (`clearToArm`), ANY armed solver during Settle/Duck/TopUp disarms on the next tick regardless of who armed it. `ExitCombat` already disarmed; the gap was only mid-combat orphans.

## Investigation Notes (follow-up)

**Bisect result** (HUD kill-switches): the old intent-lane toggle OFF
silenced the jitter — but the lane's coverage was a lie: the coupled
`rise`/`duck`/`swing-dwell`/`hold-position` writes bypassed
`YawIntentDeg()` entirely (a `[Yaw] …← duck` log fired with all
switches off — direct proof). The toggle patched two write-path
families; "off" was never zero.

**ROOT CAUSE CAUGHT — `EventPlayer`'s zombie rotation claim** (FORCES
tab): the monitor showed `settle` pinned at **−439°/s sustained** — a
`Hold` writing ~7°/frame toward a CONSTANT target can only do that if
something pushes back +7°/frame every frame. The old binary watchdog
(`flag only frames with no combat write`) was blind to exactly this —
the driver wrote every frame, masking the attacker. Residual detection
(observed Δ − Σ combat writes) exposed it: **`EventPlayer.DebugSnapTo`
armed `hasTargetRotation` on every pit snap** — the scripted-facing
slerp channel ran every frame FOREVER while `EventPlayer.running`
(it only cleared on a zero-direction move event), pulling him ~8%/frame
toward the snap facing while the driver pulled toward `settleYawDeg`.
Two systems, one transform, ~180° apart = the standing jitter.

**Fix — snap is instant + combat yields the channel:**
`DebugSnapTo` writes `transform.rotation` directly (a teleport has no
business holding a persistent slerp claim), and EventPlayer's
rotate-toward-target channel now **voids `hasTargetRotation` whenever
the character's `CombatController` is non-Idle** — combat owns facing;
any scripted/snap claim is dead on entry.

**Architectural fix — the single facing driver** (user-directed rewrite):
facing intent is now a *declared mode* — `Free`/`Hold`/`Track`/`Curve` —
and ONE function (`TickFacing`, last call in `Update`) performs every
combat yaw write. Declaration sites (`EnterBeat` cases, `Engaged`/
`swing-dwell` ticks, `StepToward`, the seek block) only declare;
`dbgFacing` gates the driver itself, so off literally means zero writes.
Decision (the roll) and rendering (the driver) never share a call site.

**Belief is event-driven** (user-directed): `ThreatPos`/`LastSeenTarget`
only move on perception EDGES — acquire (miss→hit) and loss (hit→miss)
both write `ThreatFeet()` — the entity position at confirmation (the
"ring at the base"), never the wandering centroid. Mid-contact, belief
never drifts; `Engaged` tracks the live body **contact-gated**
(`contactSeen ? ThreatFeet() : AimAnchor()`) — tracking live feet while
merely reference-alive was omniscient through hiding (user-caught). The
driver is deadbanded (|Δ| ≤ 0.5° ⇒ no write) — converged beats make ZERO
yaw corrections. Bonus: `Sensor.Threat` zone re-paints no longer fire
every probe tick.

Earlier leaks fixed in the same pass:

1. `EnterBeat(Settle)` re-rolled `PeekSide`/`settleYawDeg` **every seam** —
   the rhythm loop visibly re-faced him each cycle. Now rolled once per
   cover spot (`settleSpot == CoverPoint` guard; re-rolls on relocation).
2. `Duck`/`TopUp`/`None` had **no yaw writer** — the post-`Engaged` fold
   left him threat-forward until the next seam's re-roll swung him back.
   Now every beat declares its facing (`Hold(settleYawDeg)` for the
   settle family; `Curve`/`Track` for transitions and engagement).
3. `ArmAimSolver.torsoAssist` ran while locomoting — `ikOverrides[0]`
   pulled the torso toward the target while `seek` pulled the root toward
   the path. Now skipped during `Walking`/`AimWalk` (root owns heading on
   the move; the arm still solves the residual — opportunity-fire intact).

## Channel kill-switches (bisect toggles)

Inspector-togglable live, no recompile — flip one, rerun, watch the wiggle:

| Toggle | Channel it kills |
|---|---|
| `dbgFacing` | THE DRIVER ITSELF — every combat yaw write, guaranteed (one function) |
| `dbgEngagedTrack` | `Engaged` live-track declaration — demotes to `Hold(engageYawDeg)` |
| `dbgArmSolver` | `wantAim`/`holding`/`swingAim` — arm permanently stowed in combat |
| `dbgMoveYaw` | `seek` + `StepToward` yaw writes — he strafe-slides instead of turning |
| `dbgVisionFeed` | Swing-dwell `vis.SetTarget(live threat)` — cone-detection channel |
| `yawWatch` | `[Yaw]`/`[Yaw?] EXTERNAL` logging (silences, doesn't gate) |

Plus `ArmAimSolver`'s own publics: `torsoAssist`, `aimFeelEnabled`
(perturbation/sway) — kill-switches at the solver level.

## Instrumentation (kept)
- `SetYaw(who)` wrapper on every combat rotation write + `[Yaw]` owner logs (one per new owner + 0.5s heartbeat during eases).
- `LateUpdate` yaw watchdog — **residual form**: `EXTERNAL Δ` = observed yaw change − Σ this frame's combat writes, so external writers can't hide inside a written frame (the binary `no-combat-write` gate was blind exactly when it mattered — the driver wrote every frame).
- **FORCES tab** (HUD): live L◀|0.0|▶R bars per writer-owner — EMA °/s signed by direction; two rows lit = tug-of-war, EXTERNAL red = unattributed. This is the instrument that caught the EventPlayer claim.
- `yawWatch` Inspector toggle silences the logs without a recompile.

## Related Files
- `Assets/Scripts/Sim/CombatController.cs` — disarm gate, `SetYaw`, watchdog
- `Assets/Scripts/Sim/ArmAimSolver.cs` — `torsoAssist`/`ikOverrides[0]`, `PerturbTarget` sway
- `Assets/Scripts/Sim/PossessController.cs` — possess-entry `Arm()` orphan source
- `docs/known_issues/runtime/IK_TORSO_OVERRIDE_STOMPS_CROUCH_LEAN.md` — sibling: the same `ikOverrides[0]` channel stomping authored crouch lean
