# Possess-Mode Shots Resolve but Deal No Damage (Lazy VDS Spawn)

**Date**: 2026-10-11
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH

## Symptoms
- Possess Vinny (G), LMB fire: tracers fly, red impact ticks land on buildings — but **zero damage** (no bullet decals, no glass puncture).
- Place + detonate a bomb first, THEN re-possess → shots suddenly work.
- Repro: fresh Play → G → shoot anything → nothing damaged.

## Root Cause
`VoxelDamageSystem` (the only `ShotResolved` subscriber — turns resolved shots into
damage) is spawned lazily by `GetOrCreate()`. Nothing created it at session start:
possess LMB created `ProjectileSystem` itself but never the damage consumer, so
`ShotResolved?.Invoke()` fired into zero subscribers. The first Demolitions
DETONATE (or Combat tab open) happened to call `VoxelDamageSystem.GetOrCreate()`,
after which the event had a listener — "shots become real after the first bomb."

Impact marks/tracers live in ProjectileSystem (independent of the subscriber),
which is why the shots *looked* live.

## Investigation Notes
- `VoxelDamageSystem.Update` binds `subscribed.ShotResolved += OnShotResolved`
  only once `ProjectileSystem.Instance != null` — and only if VDS exists at all.
- `OnShotResolved` gates: `r.hit`, non-"terrain"/"none" target, `HasWritableVolume`
  (chunks OR sector-baked volumes — both writable post-P2). None of these were
  the problem — the method simply never ran.

## Related Files
- `Assets/Scripts/Sim/ProjectileSystem.cs` — `Fire()` now `GetOrCreate()`s VDS
- `Assets/Scripts/Sim/VoxelDamageSystem.cs` — `Awake` eager-subscribes if
  `ProjectileSystem.Instance` already exists (covers same-frame point-blank
  resolves before the lazy Update bind)

## Resolution
Two-sided fix: `ProjectileSystem.Fire()` guarantees the consumer exists before a
shot can resolve (covers possess LMB, Combat FIRE, future callers); `VoxelDamageSystem.Awake`
eagerly subscribes when the publisher already exists. csc clean.
