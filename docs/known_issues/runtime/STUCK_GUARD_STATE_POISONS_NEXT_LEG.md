# Step-Guard State Inherited Across Legs — Instant "Refused" Aborts

**Date**: 2026-10-28
**Status**: 🟡 FIX APPLIED (pending playtest)
**Severity**: 🔴 CRITICAL

## Symptoms
- Dice legs (STRAFE / ADVANCE / recon peek / deep-recon bare point) aborted on their FIRST tick with ZERO movement: `REFUSE ... short=<full leg length>` stamped the same tick (or +0.08s) as the `DEST` line. Verdict: `blocked — leg aborted N m short`.
- Map said the destination was reachable (`dest reach=Y@..m`, `routed=1`) — it was not a planning refusal.
- Bursts: 8 refusals in 1 s for both fighters (t1105 in the 2026-10-28 11:23 run); first run: Vinny wedged 14+ s, 16 instant refusals.
- Deep recon ended instantly (`stop 1 cover unreachable — walking the bare point` then `deep exhausted — he was never there` in the SAME tick) -> fighter left combat on a navigation failure, not a search result.

## Root Cause
`StepToward`'s stuck guard keeps per-leg state in shared fields: `stuckBest` (best waypoint distance), `swingStuckT` (last-progress time), `stuckNudges` (sidestep budget). Those were reset by `SetState(SeekingCover)`, `TryStartSwing`, waypoint advance, and a few seek paths — but **NOT when a dice micro-leg started** (`DiceExecFace` / `DiceReconRoll` / `DiceDeepDispatch` only set `diceLegActive`/`diceLegBuilt`). A new leg inherited the previous leg's small `stuckBest` (so `wpDist < stuckBest - 0.05` was false), a stale `swingStuckT` (> 0.6 s old after any STAND hold / reload), and often an exhausted `stuckNudges == 3` -> `StepToward` returned "leg done" immediately. Once poisoned it stayed poisoned until a SeekingCover transition happened to reset it (self-perpetuating wedge).

## Investigation Notes
- Found by reading `Logs/dice_trace.log` after adding `DEST`/`REFUSE` lines: every strafe/advance refusal had `short ≈ leg length` and timestamp == DEST timestamp (zero elapsed time).
- Separate real map issue also found: `ZoneReachable(zone)` only proves SOME Hidden member is reachable; the dispatched stand cell itself could be unreachable (`reach=N`, `flood serves 3396/4096`) -> `no corridor`. Fixed by `StandReachable` (same snap BuildRoute uses) in `DiceCoverCands` and `DiceDeepSnap`.
- Map-derived destinations (`DicePickStepCell`) fixed the planning side; they could not fix this.

## Related Files
- `Assets/Scripts/Sim/CombatController.cs` — `StepToward` (stuck guard), `StepRouted`, `DiceExecFace`, `DiceReconRoll`, `DiceDeepDispatch`, `DiceCoverCands`, `DiceDeepSnap`
- `docs/known_issues/runtime/DICE_TURN_BASED_SCAFFOLDING_VS_REALTIME.md`, `EYE_SEES_MUZZLE_CANT_SHOOTABLE_GAP.md`

## Resolution
`StepRouted` now resets the guard (`swingStuckPos/T`, `stuckNudges`, `stuckBest`) whenever a leg is (re)materialized (`!built` or stale-stamp reroute) — every dice leg goes through it. `StandReachable` gate on cover / deep-recon candidates. Deep-exhaust verdict now reports stops actually swept (`swept N/3`). Trace: `REFUSE` no longer prints a stale `routeFail` for routed legs; `diceRunning` carries `deep N/6` / `recon N/6` labels.

## Addendum (2026-10-28, later run)
The re-route reset added here regressed: the field rebuilds every 0.4 s so the guard never expired. Reset now only on a NEW leg. See `MOVING_VEHICLES_COVER_EMBED_GUARD_REGRESSION.md`.
