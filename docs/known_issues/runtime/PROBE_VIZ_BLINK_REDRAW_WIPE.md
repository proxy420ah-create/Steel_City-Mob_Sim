# Probe Viz Blink — Redraw Wipe + Setup Throttle

**Date**: 2026-10-21
**Status**: 🟡 FIX APPLIED (pending playtest)
**Severity**: 🟢 MEDIUM (debug visualization only)

## Symptoms
- `vmk_` probe impact marks and `tv_s` probe rays visibly blinked during the
  setup/pit phase; in combat they read smooth/streamlined.
- `vdm_` vision detect marks never rendered at all (set + wiped same frame).

## Root Cause
Two stacked issues:

1. **CoverSensor.Redraw wiped probe viz.** `Redraw()` ran `ClearViz()` which
   called `ClearVisBeams()` (probe rays + marks) on every field rescan
   (`Refresh`/`TryPickCover`/`TryPickCoverFacingThreat` cadence). In combat the
   per-frame probe (`visInterval=0`) repainted within the same frame so the
   wipe was invisible; in setup the HUD manual probe ran at a hardcoded 4 Hz
   (`nextManualProbe + 0.25f`), leaving a multi-frame visible gap → blink.
2. **VisionSensor.Redraw wiped detect marks.** `Update` ran `Evaluate` (which
   draws `vdm_` cubes) then `Redraw` → `ClearViz` → cleared those same keys —
   marks died before render every frame.

## Resolution
Ownership split, same pattern on both sensors:

- `CoverSensor`: `ClearViz()` = `ClearFieldBeams()` + `ClearVisBeams()`
  (disable/consumer-loss only). `Redraw()` now calls `ClearFieldBeams()` only —
  probe rays/marks are owned exclusively by `TryGetVisibleVoxels`'s trim loops.
- `VisionSensor`: `ClearViz()` = `ClearBeams()` + vdm_ clear (disable only).
  `Redraw()` calls `ClearBeams()` only; `Evaluate`'s slot-trim owns mark
  lifecycle (toggle-off, list shrink, per-target not-visible).
- `DebugHUDManager`: removed `nextManualProbe`/`0.25f` throttle — the idle
  manual probe now runs per-frame, same live rate combat uses.

## Related Files
- `Assets/Scripts/Sim/CoverSensor.cs` — ClearViz/ClearFieldBeams/Redraw split
- `Assets/Scripts/Sim/VisionSensor.cs` — ClearViz/ClearBeams/Redraw split
- `Assets/Scripts/Sim/DebugHUDManager.cs` — manual probe throttle removed
- `docs/systems/PERCEPTION_AWARENESS.md` — probe/mark ownership semantics
