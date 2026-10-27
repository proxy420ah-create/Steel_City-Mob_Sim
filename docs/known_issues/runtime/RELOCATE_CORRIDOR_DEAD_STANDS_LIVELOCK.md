# Relocate Livelock — Corridor-Dead Stands Never Marked Failed

**Date**: 2026-10-21
**Status**: 🟢 FIXED (awaiting runtime confirm)
**Severity**: 🟡 HIGH

## Symptoms

Pit duel: both fighters seek cover correctly, fight symmetrically, then BOTH
stop at the same moment. Console shows the cycle:

```
rise refused: bore under lip; muzzle 0.72 < wall 0.70+0.02 (Crouch, duck 0.32)
relocating to fightable cover block_36_building
⚠️ no corridor to block_36_building (2.1m) — leg ruled unreachable
⚠️ cover unreachable (2.09m out) — holding position
rise refused: bore under lip; ...   ← same verdict, same wall, repeats
```

Refuse → relocate → corridor-dead → hold → refuse — the loop never terminates.

## Root Cause

The refusal cascade (already implemented and working):

```
Rise refused (SolveRiseDuck < 0)
├── burned stand (2nd refusal, same CoverPoint) → TryRelocateToFightingCover
├── else → Swing beat → TryStartSwing (lateral step past zone rim to peek
│   around the corner — the "side-step")
├── Swing has no rim → TryRelocateToFightingCover
└── no fightable cover found → PINNED (slow re-check clock)
```

…depends on `failedFaces` to exhaust the relocate pool — but `failedFaces`
only recorded **rise-solve** failures (verdict < 0 at a stand). A stand whose
`BuildRoute` corridor died was committed, abandoned, then **re-picked the
very next refusal** — the abort resets `CoverPoint = Feet()` so the `burned`
check can't see the repeat either. The `pinned` terminal ("stand can't fight,
no peek edge, no fightable cover") was unreachable because picking always
"succeeded" on the same dead stand.

## Fix

`CombatController.cs`, in the `noCorridor` branch of the seek abort path:
before `CoverObstacleId` is nulled, the destination obstacle's nearest face
mark (within 2m of the committed `CoverPoint`) is added to `failedFaces` —
same exclusion granularity rise-failures already use. Each dead corridor now
shrinks the pool; exhaustion lands on the honest `pinned` verdict.

Also added `routeFail` — `BuildRoute`'s first-failure stage string
(`legs-off` / `no field` / `cell-of` / `no walkable cell at from|to` /
`flood fenced — reached N/4096` / `pulled 0 waypoints`), surfaced in the
no-corridor log line and the DUEL status row's `LastResult`.

## Related Files

- `Assets/Scripts/Sim/CombatController.cs` — noCorridor abort (~2102),
  `TryRelocateToFightingCover` (~1510), `SolveRiseDuck` (~947),
  `TryStartSwing` (~1031), `failedFaces` (~1500)
- `PIT_STANDS_OUTSIDE_SENSOR_RADIUS.md` — prior corridor failures (radius,
  unprobed groundY)

## Resolution

Fixed 2026-10-21. The underlying corridor failure itself (why BuildRoute
can't route a 2.1m relocate leg) is still under diagnosis — `routeFail` in
the next playtest names the stage.
