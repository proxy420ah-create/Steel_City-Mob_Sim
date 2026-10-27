# "Sees but can't shoot" livelock — peek→swing→peek forever at buried-bore cover

**Date**: 2026-10-22
**Status**: 🟡 FIX APPLIED (pending playtest)
**Severity**: 🔴 CRITICAL — combat could stall indefinitely mid-episode

## Symptoms
- Vinny loops: `rise refused: sees but can't shoot; wall 0.33 — bore path
  blocked` repeating, alternating with swing-peek / duck / settle. Search
  markers (spokes, ring, pick) stay live but the orbit never resolves; the
  actor appears "corrupted" — actually a legal-state livelock.
- Triggered at cover whose parapet clears the *eye* but not the *muzzle*
  (e.g., the 0.33m lip on a car's hood).

## Root Cause — two stacked gaps
1. `Rise` refusal always routed to `Swing` for non-scripted actors
   (`if (!scriptedMode) EnterBeat(Swing)`); `TryRelocateToFightingCover` —
   the re-pick-for-fighting-cover path — was unreachable. A stand that can
   never shoot looped rise→swing→rise→swing with no exit.
2. In `Engaged`, sustained `canSee ∧ BoreBuried` had no verdict: cone holds
   the target (no drop, no search arm) while the bore never clears — a
   staring contest.

## Fix (CombatController.cs)
- `riseRefusedAt` records the stand position that burned a swing attempt;
  a SECOND refusal at the same stand calls `TryRelocateToFightingCover()`
  (failed-face memory already prevents re-picking the same face) before
  falling back to another swing.
- `boreBuriedSince` accumulates sustained buried-bore under live contact;
  >1.2s → `TryRelocateToFightingCover()` with a named log line.
- Both reset on `CommitSearchSeek` / `ExitCombat` — per-stand accounting.

## Follow-up hole (same family) — "every exit failed" rhythm flap
The relocate-or-swing fix still left a loop when the stand can't fight
**and** has no swing edge **and** no relocation exists: refusal → Swing
fails instantly → relocate fails → `Duck` → `Settle` → `TopUp` → `Rise`
→ refused → `Duck` → … (observed: "Settle → Top up → Settle → RISE
flips into DUCK → Settle" forever). The state machine had no terminal
verdict for *unfightable position*.

**Fix — the PINNED verdict** (`pinned`/`pinUntil`, `pinRecheckSec` = 4s):
when all three exits fail (rise over, swing around, relocate away), the
stand is declared hide-only for this fight: `Duck` → `Settle` seam parks
at `Beat.None`; the clock re-arms ONE real rhythm cycle every
`pinRecheckSec` — a real rise is a real probe, so a moved threat or a
changed world wakes the rhythm honestly. Re-refusal re-pins. Also applied
to the `Engaged` buried-bore relocate-fail fallback and scripted-mode
refusals; `pinned` clears on every commit path (`ScriptedGoTo`,
`CommitSearchSeek`, `TryRelocateToFightingCover`, Enter/ExitCombat);
`NextAfterSettle` preview shows `None` while pinned.

**Also fixed**: `failedFaces` marking added every successively-nearer
wall mark on the obstacle (order-dependent — could fail ALL faces at
once and starve the relocation pool). Now marks only the single nearest
face — the one the rise actually solved against — under the same 2m
cap `SolveRiseDuck` uses.

## Related Files
- `Assets/Scripts/Sim/CombatController.cs` — rise-refusal branch, fire gate
- `docs/systems/ENGAGEMENT_BEATS.md` — relocate doctrine
