# Dice Ownership Leaks — Authored Systems Acting Under the Roll

**Date**: 2026-10-27
**Status**: 🟢 FIXED
**Severity**: 🔴 CRITICAL

## Symptoms

- Manual DICE mode: fighter self-issued a `relocate → <zone>` while the panel
  said `await next turn roll` — the turn engine lost movement authority.
- Cylinder auto-reloaded on empty mid-firefight under DICE — the ramp
  count-check decision was bypassed silently ("I'm noticing auto reload").
- Auto (quantum) mode: the skeleton's boundary could fire mid-SeekingCover
  leg → double-dispatch a decision underneath an executing turn, and the
  starve clock could END a live deep-recon hunt.
- Deep recon: a snapped cover leg with no corridor silently advanced the
  fan — stops skipped with no log; only the last stop ever swept.
- `StepToward`'s true-for-abort contract let blocked dice legs post
  `move complete` while wedged short of the step point.

## Root Cause

One structural failure mode: **authored combat systems never knew the dice
owned the frame.** Gates checked `scriptedMode`/`seamArbiter` but never
`DiceActive`, so the authored layer's reflexes (relocate, reload, boundary
firing) kept firing inside the roll's turn. Plus `StepToward`'s contract
(arrived AND abort return true — caller must re-verify distance) went
unhonored by the dice leg completion.

| Leak | Site | Gate missing |
|---|---|---|
| Buried-bore self-relocate | `TickExposureFire` → `TryRelocateToFightingCover` | `DiceActive` → now posts `bore buried — can't fight from here` verdict for the roll |
| Dry auto-reload | fire tick `cylinder <= 0 && !InCover → StartReload` | `DiceActive` → now arms `diceRampPending` as `reload check — cylinder DRY` |
| Mid-turn boundary | auto `DiceBoundary` fired from any state | `State == HoldPosition` + shared `DiceRollGateOpen`/`DiceConsume` |
| Silent fan advance | `SeekingCover` abort path called `DiceDeepNext()` unposted | bare-point retry + explicit spend post |
| Fake move complete | leg completion trusted `StepToward` return | distance re-verify → `blocked`/`no corridor` verdicts |

## Resolution

`CombatController.cs`: DiceActive guards on the bore-relocate + dry-reload
reflexes; the auto path now runs `DiceClick`'s literal gate + consume
(`DiceRollGateOpen`/`DiceConsume` — one implementation, zero drift);
deep-recon aborts retry the bare arc point before spending a stop; leg
completions re-verify distance. See `RECENT_CHANGES.md` — dice arbiter
manual/auto entries.

## Related Files

- `Assets/Scripts/Sim/CombatController.cs` — TickDice, DiceClick/DiceConsume,
  DiceBoundary, ArmRoll, fire-tick dry branch, deep-recon chain
- `docs/known_issues/runtime/RELOCATE_CORRIDOR_DEAD_STANDS_LIVELOCK.md` — same
  leak family (authored vs gate)
