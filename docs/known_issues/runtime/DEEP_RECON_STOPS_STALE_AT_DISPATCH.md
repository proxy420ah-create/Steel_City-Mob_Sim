# Deep-Recon Stops Stale At Dispatch

**Date**: 2026-10-28
**Status**: 🟡 FIX APPLIED (pending playtest)
**Severity**: 🟡 HIGH

## Symptoms
- Run 2026-10-28 12:15 (262 s): all 9 `REFUSE` lines were deep-recon dispatches - `map unreachable — flood serves N/4096` (x7) and `cell-of fail (s2080 d-1)` / OUT-OF-FIELD (x2). Two hunts ended `deep exhausted — swept 2/3` with a stop never walked; one combat exited early.

## Root Cause
`DiceDeepRoll` stores fan stops as WORLD points (`ff.GroundPoint(cell)` chosen under that moment's field + flood). Dispatch happens seconds later, after hops: `DiceDeepDispatch` used the stored point verbatim while `BuildRoute` validates against the CURRENT field - which rebuilds every 0.4 s (origin re-snap shifts the window; the flood re-seeds from the fighter's NEW feet after each hop; chokepoints shift). Points were reachable when picked and stale at dispatch.

## Resolution
`ResolveDeepPoint` re-resolves each bare stop under the live field+flood (nearest reachable/walkable/non-`IsDyn` cell within `DeepResolveM` 3 m). Nothing reachable near it = the stop is skipped honestly (`DiceDeepNext`), never a failed leg; skipped stops do not count as sweeps. Stage candidates also skip `IsDyn`. The snapped-cover branch was already live-resolved (`DiceDeepSnap` re-runs at dispatch).

## Related Files
- `Assets/Scripts/Sim/CombatController.cs` - DiceDeepRoll, DiceDeepDispatch, ResolveDeepPoint
