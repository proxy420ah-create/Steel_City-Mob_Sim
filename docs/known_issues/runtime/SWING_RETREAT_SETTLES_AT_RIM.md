# Swing Retreat Settles at the Rim — Stale Step-Guard Clock

**Date**: 2026-10-20
**Status**: 🟡 FIX APPLIED (pending runtime verification)
**Severity**: 🟡 HIGH

## Symptoms
- After a correct lateral peek (`Swing` → dwell → "zero contact, stepping back"), Vinny **folds and settles in the open at the zone rim** instead of walking back to the cover centroid — `InCover`/`Settle` state while physically standing beside the wall.
- Same class on the contact path: `Engaged` entered at the rim, exits to `Duck`/`Settle` — he stayed out there.

## Repro
- Cover pit duel on a tall wall (rise refused → `Swing`): peek sweeps, finds nothing, logs `stepping back` — then `beat Settle` lights while he's still at the rim.

## Root Cause
`StepToward`'s stuck guard (`swingStuckPos`/`swingStuckT`) is shared across all swing phases. `TryStartSwing` resets it at step-out; the phase-1 **dwell (~5s) consumed the 0.6s no-progress budget**; on phase-2's first tick `(Feet() − swingStuckPos) ≈ 0` (he never moved during the dwell) AND `unscaledTime − swingStuckT > 0.6s` → the guard declared **instant arrival** — the `swing-p2` return leg never ran. The Y-snap + `CrouchAndCover` + `EnterBeat(Duck)` then fired at the rim.

The contact path had a second gap: `contact → EnterBeat(Engaged)` at the rim, and `Duck`'s completion check only tested `folded ∧ floor` — nothing tested *position*, so the seam held wherever he stood.

## Fix
1. `swingStuckPos`/`swingStuckT` reset at the phase-1→2 boundary (each phase owns its progress window).
2. `Duck` completion now checks **position**: folded ∧ floor ∧ `|CoverPoint − feet| > arriveEps` → `return` leg — `Walking` pose + `StepToward(CoverPoint,"return")` until arrival, then Y-snap + `CrouchAndCover()` + `EnterBeat(Settle)`. Covers both paths (zero-contact phase-2 AND off-spot Engaged exits) with one guard.

## Investigation Notes
- FORCES tab + `LastRiseDiag` made it readable: `stepping back` logged, then `SETTLE → next TopUp` with him at the rim — the step-back claimed completion in one frame.

## Follow-up — the deeper bug: "stuck" reported as "arrived" (2026-10-20, same session)

Post-fix runtime showed him still landing **~0.3–0.5 m off the painted centroid** — completing "too soon or offset somewhere." Root cause: the stuck guard's `return true` conflated two outcomes — *reached* `dest` vs *stopped moving*. Any corner-wrap stall (the fixed X-then-Z slide can't wrap a convex lip — one axis kept feeding the wall) became a silent off-target arrival, and the `return`-leg completion then settled **without re-verifying position**.

Fix — honest leg outcomes:
1. `MoveWithCollision` slides on the **dominant component first** (was fixed X-then-Z) — corner wraps get the free axis first.
2. `StepToward` spends **unstick sidesteps** before reporting done: ~0.6 s no-progress → a ±perpendicular 0.28 m hop (alternating sides, ≤3 nudges, facing follows the sidestep); only an exhausted budget reports `true` — and callers **re-measure distance** to split arrived from aborted.
3. `swing-p2`: verified arrival → `CrouchAndCover` (rolls) → `Duck`; **aborted** → straight to `Duck` — the `return` leg owns the walk home and *its* arrival rolls.
4. `return` leg: re-verifies `home` on completion — `>arriveEps` → `⚠️ return leg aborted Nm off centroid` log + `LastResult`, he stands where the leg ended (no fake arrival, no infinite grind).
5. `SeekingCover` now rides the same `StepToward` mechanics (was inline — no stuck guard at all, could grind forever): exhausted budget → `⚠️ cover unreachable` → `HoldPosition` fallback (the no-cover last-stand semantics).
6. `stuckNudges`/`stuckNudgeSide` fields; nudge budget re-armed wherever the stuck clock arms (`SetState(SeekingCover)`, `TryStartSwing`, phase-1→2 boundary, `returnWalked` first step).

## Related Files
- `Assets/Scripts/Sim/CombatController.cs` — `StepToward` stuck guard, `Swing` phases, `Duck` return leg
- `docs/systems/ENGAGEMENT_BEATS.md` — Swing section (return-leg ownership documented)
