# Vinny fires on belief — trigger never required a resolved probe

**Date**: 2026-08-11
**Status**: 🟢 FIXED
**Severity**: 🔴 CRITICAL — combatants could discharge without any confirmed contact

## Symptoms
- Vinny fired at 77.02s — during the *entry seek*, before any sighting existed.
- Vinny fired twice mid-orbit-transit (111.75s, 118.14s) while `searchAim` was
  presenting the gun forward on open ground. Both expired at the 80m range cap.
- Vision cone never swept; `LastSeenTarget`/probe data showed zero contact.

## Root Cause
`TickExposureFire`'s fire gate (`CombatController.cs`) checked: `solver.IsAiming`,
cylinder > 0, `BeatAllowsFire`, cadence, solver blend ≥ 0.7, angular error, and
`BoreBuried` — but **never `canSee`**. Any aim channel that kept the gun up
(searchAim scout posture, swingAim dwell, disarm-hold) could converge on a
non-target point; with a clear bore and elapsed cadence the hammer fell on
nothing. The doctrine comment above the gate ("aim-without-fire until a probe
resolves") described intent the code didn't enforce.

## Fix
Added `!canSee` to the fire gate — the trigger requires the cadenced probe to
have resolved visible voxels (`vis.visible > 0`). Aim channels are unchanged:
the arm may still present forward (scout), at belief (swing dwell), or hold on
last-seen (disarmHold) — only the hammer is gated. The opportunity channel
survives intact: a mid-leg sighting flips `canSee`, retargets the aim to
`vis.centroid`, and the same gate then authorizes the shot.

## Related Files
- `Assets/Scripts/Sim/CombatController.cs` — `TickExposureFire` fire gate
- `docs/systems/ENGAGEMENT_BEATS.md` — exposure/fire doctrine
