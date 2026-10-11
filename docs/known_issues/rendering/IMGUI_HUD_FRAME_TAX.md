# IMGUI Debug HUD Frame Tax (120 → 40 FPS)

**Date**: 2026-10-11
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH

## Symptoms
- Frame rate dropped from ~120 FPS to ~40 FPS in planning phase, before the
  working week or the glass composite pass were ever enabled.
- Drop was felt around the same time the F6 WASD free-look camera landed —
  timing correlation made it the prime suspect.
- View-angle dependence: straight top-down held ~120 FPS; any other angle
  degraded (separate issue — see Resolution).

## Root Cause
Two independent costs, one CPU one GPU:

1. **IMGUI repaint tax (the ~25ms mystery)** — `DebugHUDManager.OnGUI` rebuilt
   ~60 `GUILayout` widgets and ran `RefreshReferences()` (8×
   `FindFirstObjectByType` scene scans) on **every IMGUI event**. OnGUI fires
   once per event — Layout + Repaint + each mouse/key event ≈ 4–6× per frame —
   so the panel's real cost was ~4–6× its apparent cost. Profiler confirmed:
   `DebugHUDManager.OnGUI` topped the GC-contributors list; `Scripts` total was
   only 0.98ms while `EditorLoop`/`Others` absorbed the rest.
   A/B: panel open = 32 FPS, minimized = 120 FPS.
2. **Angle-dependent march depth (GPU)** — ortho top-down rays hit roofs in a
   few DDA steps; angled views march deep through mostly-air interiors at
   voxel granularity (~11ms GPU at 769×810). Classic flat-DDA cost.

## Exonerated (checked, innocent)
- F6/WASD camera code — a few `isPressed` checks + vector ops, nanoseconds.
- `VoxelChunkManager` — 1.5ms total (0.73 cull + 0.75 draw) for 788
  registered / 64 drawn chunks.
- Glass composite pass — regression predated enabling it.
- `ProceduralDebrisScatterer` / test rigs — load-time + cached, not per-frame.

## Resolution
- `DebugHUDManager`: text-only tabs (Camera/Render/Path/Keys) rebuild into
  ONE cached rich-text string at ~4 Hz drawn by a single `GUILayout.Label`;
  `RefreshReferences` throttled to 0.5s; footer cached the same way.
  Interactive tabs (Clothing buttons, Range sliders) stay live.
- `VoxelProxyRaymarch.shader` + `VoxelChunkManager`: two-level DDA — an
  8³-brick occupancy bitmask (`_OccupancyData`) jumps all-air bricks in one
  boundary step instead of ~21 cell iterations, applied to the primary march
  AND the shadow march. Per-asset grids (chunks/instanced), per-building
  slices for merged sectors (`_BuildingOccOffsets`), 1-brick dilation for
  group-animated assets. Toggle: `useOccupancySkip`.
- Sub-pixel ray jitter (`useRayJitter`, ±0.5px lateral origin dither) so
  openings narrower than the pixel grid — the aligned window-bore visibility
  issue — get stochastically sampled as stable stipple.

> **Update (2026-10-11 night)**: the occupancy skip and ray jitter above were
> later removed from the live shader and C#. Sector baking resolved the
> angle-dependent drop (overdraw from ~780 stacked per-building volumes), and
> jitter's stipple conflicted with the crisp voxel look. The HUD fix stands.
> Code preserved in branch `archive/occupancy-dda` and commit `57fb572`; see
> `docs/core/PERFORMANCE_PATTERNS.md` #13 (shelved) and #14 (the heatmap).

## Related Files
- `Assets/Scripts/Sim/DebugHUDManager.cs` — cached-content rebuild
- `Assets/Scripts/UI/VoxelChunkManager.cs` — occupancy build/bind, jitter amp
- `Assets/Resources/Shaders/VoxelProxyRaymarch.shader` — two-level DDA
- `docs/core/PERFORMANCE_PATTERNS.md` — pattern 13

## Lesson
IMGUI `OnGUI` is per-**event**, not per-frame — widget count and any
FindObject call inside it are multiplied ~4–6×. Debug HUDs should cache
content strings and throttle scene scans; never measure "script cost" with
the profiler's Scripts line alone — IMGUI bills to EditorLoop/Others.
