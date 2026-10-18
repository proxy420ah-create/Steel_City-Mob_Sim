# Debug Beam Emission Flood — 120 → 40 FPS Regression

**Date**: October 16, 2026
**Status**: 🟢 FIXED
**Severity**: 🔴 CRITICAL (3× FPS regression)

## Symptoms
- 120 FPS → ~40 FPS; frame 7.8ms → ~25ms.
- Resolution scale (R key) had **no effect** — the canonical "not fill-bound" signature.
- Profiler: `GfxDeviceD3D12.WaitForGPU` ~80%, scripts ~2.4ms, Rendering ~19.8ms; Unity's "GPU Time" counter read ~3ms (it does not see the custom RT raymarch path).
- Stats panel tell: **Instancing — Batched Draw Calls: ~1.3k** for a scene with only ~65 real batches.
- Toggling all detection debug in the HUD did NOT fix it — **Tan Suit's emitters kept running**: the COVER/VISION tabs resolve `SpawnedCharacter` (Vinny) only; the second character's own components carried default-on flags with no HUD reach.

## Root Cause
`PathDebugRenderer` emits every debug segment as `CommandBuffer.DrawMeshInstanced` into the voxel view's color RT — a per-frame command-buffer build + submit of ~hundreds–thousands of tiny instanced draws:

- `CoverSensor.DrawRing` — 28-seg circle per emitter per frame
- `VisionSensor` — cone rim beams + eye→target rays per emitter per frame
- `ArmAimSolver.showSightBeam`, pick beams, node markers — additional emitters

Per-draw submission overhead is **resolution-independent** — matching the R-key null result and the render-thread-bound profile exactly. The defaults being on (`showRing`/`showCone`/`showTargetRay` = true) meant BOTH characters emitted every frame regardless of HUD state.

## Fix
- Defaults flipped off: `CoverSensor.showRing`, `VisionSensor.showCone`, `VisionSensor.showTargetRay` → false.
- New **TANSUIT HUD tab**: per-component `enabled` kills (Update stops entirely; `OnDisable` already cleans viz+globals), full detection/logic surface for `Civilian_02`.
- `VisionSensor.ConeOwner` static — only the designated sensor pushes `_VisConeA/B` globals (last-writer-wins bug eliminated).
- Verified: FPS 120, frame 7.81ms, scale 1.00.

## Lessons / Pattern
- **Debug emitters are per-emitter-per-frame draw calls** — cost scales with emitter count × segment count, not pixels. Any "all debug off" baseline must cover EVERY character's components, not just the primary one.
- `enabled=false` on sensor components is the hard kill — Update stops, `OnDisable` clears beams/globals.
- If a future regression shows res-independent GPU cost again, check `Batched Draw Calls` in Stats first — the beam channel hides inside instancing counts.

## Related Files
- `Assets/Scripts/Sim/PathDebugRenderer.cs` — DrawMeshInstanced beam emission into voxel RT
- `Assets/Scripts/Sim/CoverSensor.cs`, `VisionSensor.cs`, `ArmAimSolver.cs` — emitters
- `Assets/Scripts/Sim/DebugHUDManager.cs` — TANSUIT tab
