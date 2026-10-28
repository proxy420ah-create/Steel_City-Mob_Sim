# Dice Turn-Based Scaffolding Leaks Into Real-Time Execution

**Date**: 2026-10-27
**Status**: 🟡 FIX APPLIED (pending playtest) - refusals now eliminated by construction (map-derived destinations)
**Severity**: 🟡 HIGH

## Symptoms
- Auto (real-time) dice combat idled for ~5s between every decision; one move cost ~15s of stacked waits (move lands -> 5s -> reload check -> 5s -> recon -> 5s -> next roll).
- After a dice-rolled reload the fighter's trigger went COLD ("guns down") until the next movement roll executed - a rule that is not in the Tab H design.
- Design (COMBAT_BEAT_MACHINE.html tab H) has no holding patterns: ROLL -> execute -> reload check -> shallow recon -> deep recon -> END, each step firing when the last finishes.

## Root Cause
Manual (click-driven, user-paced) mode was built first. Its operator window (`diceRollCooldownSec`, 5s), parked-await state, guns-down safety, sub-rolls and mimic lockstep were wired into the shared decision path, so AUTO inherited them.

## Investigation Notes
- `diceParkUntil` and `diceLastEvidence` were write-only (the starve clock was removed earlier) - deleted.
- Sub-roll collapse (fixed `diceStepM` strafe, nearest hidden cover on the rolled side) was already in place; only display state (`diceSubRoll`) remains for the ramp/recon rolls.
- A STAND (or refused-move-stands) face is instantaneous; with zero auto cooldown it would re-roll at tick rate, so it now owns a real duration (`diceStandHoldSec`, default 1.5s - tunable, not RE-derived).

## Related Files
- `Assets/Scripts/Sim/CombatController.cs` - ArmRoll, DiceExecFace, DiceClick, DiceRampCheck, FireHold
- `Assets/Scripts/Sim/DebugHUDManager.cs` - dice panel, Fight preset (both fighters auto now)
- `Assets/Scripts/Sim/SimClock.cs` - fixed 0.08s sim clock the dice clocks now read
- `docs/known_issues/runtime/DICE_AUTHORED_SYSTEM_LEAKS.md` - earlier leak family (authored vs dice)

## Resolution
Removed GUNS DOWN, MIMIC lockstep and the dead evidence/park fields. Auto `ArmRoll` ignores the cooldown (event-driven; manual still uses it). STAND owns `diceStandHoldSec`. Fight preset runs both fighters auto; manual click remains for pinned-face testing; F8 freeze + `.` step is the pause/step control.

## Follow-up (2026-10-28)
Refused legs eliminated at the source: STRAFE/ADVANCE/peek destinations are picked from the actor flood (`DicePickStepCell`); a face with no reachable cell resolves to STAND (`DiceStandResolved`, `MOVEFAIL` trace). See RECENT_CHANGES.md "Dice moves map-derived + shootable set".
