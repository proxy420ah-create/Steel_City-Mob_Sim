# Route Crossing Center Arena Wall Mid-Fight

**Date**: 2026-10-20
**Status**: 🟡 FIX APPLIED (awaiting playtest)
**Severity**: 🟡 HIGH

## Symptoms
- Mid-duel (post Phase-0 field collapse), Vinny's navigation attempted to carry him
  **through the center arena wall** — a destination committed that the corridor
  could not honestly serve.
- Observed alongside: repeated `[Yaw] … swing-p0 X→Y` lines from the same fighter
  during a Swing beat (alternating-sign bearings — steering oscillation or
  successive nudges; possibly benign).
- Tan (`Civilian_02`) reached `orbit satisfied: flank 142°` → final confirming
  look → `rise refused: can't see over: wall 0.90+0.02 vs eye≤0.76` →
  `final look refused — abandoning` (fixed same session — see Resolution note).

## Reproduce
SETUP PIT → both seek cover → START DUEL. Breakdown occurred mid-fight during
search/relocate routing, not at the initial seek.

## Root Cause (hypothesis — reachability-after-commit class)
Stand/leg destinations are committed before reachability is proven; the
point-to-point flood + string-pull can produce a corridor that reads open at
cell centers while the arena's tall wall slab blocks the body column — or the
pick itself chose a stand the flood can't reach. The megaplan's Phase 1
(goal-seeded `distToCover`/`distFromAnchor` maps on `CoverField`, `Reaches`/
`CostFrom` lookups gating picks) is designed to make unreachable cells never
enter the candidate set — expected structural fix.

Plan: `~/.devin/plans/plan-edd89023dfbda8f7.md` — Phase 1.

## Investigation Notes
- Captured after Phase-0 verify (both fighters' seek/cover/fight/orbit correct;
  regression-free per user). So: not a Phase-0 merge regression — an existing
  routing limitation the new field API exposes cleanly.
- Needed for diagnosis: `routeFail` stage at the failing commit
  (`legs-off`/`no field`/`cell-of`/`no walkable cell`/`flood fenced`), the leg
  target vs. wall geometry, and whether `failedFaces`/`noCorridor` fired.
- Watchable: swing-yaw alternation spam may indicate nudge-budget oscillation
  on a convex corner — revisit if it persists after Phase 1.

## Related Files
- `Assets/Scripts/Sim/CoverField.cs` — declared channels (`flags`, `floorMat`,
  `groundY`), `Walkable`/`EdgeOpen`/`GroundPoint`/`StandY`
- `Assets/Scripts/Sim/CombatController.cs` — `BuildRoute`, `CorridorFree`,
  `TryOrbitStep`, `TryRelocateToFightingCover`
- `docs/known_issues/runtime/RELOCATE_CORRIDOR_DEAD_STANDS_LIVELOCK.md` —
  the corridor-dead livelock fixed previously (adjacent failure class)

## Resolution
- Same session: final-look refusal doctrine fixed — a terminal-look rise
  refusal now takes the one-shot swing peek like every other beat (burned
  stand or no peek edge → abandon honestly). See RECENT_CHANGES.
- Confirmed 2026-10-20 re-test: "civ 1 right back into the arena wall" —
  the maps don't gate what isn't designated. Two mechanisms, same defect:
  (a) **terraced stairs** — floor→curb(≤0.45)→wall(≤0.45) are individually
  legal `EdgeOpen` steps, so the flood honestly discovers a staircase over
  the wall; (b) **wall-top swing dests** — `SwingSideScore`'s `Walkable`
  gate is physics-only with an empty mask, so a wall TOP (grounded, fits)
  is a legal peek destination `StepToward` can't reach (0.8m > 0.45 climb)
  → face-grinding + `swing-p0` yaw spam.
- Phase 1+2 landed: goal-seeded `DistMap`s gate picks (unreachable zones
  never commit — orbit census names `unreachable`); arena + pit floors
  repainted `MAT_SIDEWALK 102`, wall lips split to `108 Dark Trim` (106
  stays only on floor-class marker pads), `walkableMaterials = {102,106,111}`
  on both duelists via `AttachCombatStack`. Physically-fitting but
  undesignated surfaces (wall tops, lips, curbs, car roofs) are now outside
  the domain by authored stamp — the staircase class dies by designation.
- Verify next duel: no routes over walls, `unreachable` census counts, wall
  tops never picked as swing destinations; any `no corridor` reappearance
  is a different failure class.
