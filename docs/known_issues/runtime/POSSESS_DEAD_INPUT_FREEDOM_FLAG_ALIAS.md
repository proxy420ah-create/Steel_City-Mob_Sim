# Possess Dead Input — Freedom Flag Aliasing

**Date**: October 21, 2026
**Status**: 🟢 FIX APPLIED — pending playtest
**Severity**: 🔴 CRITICAL — possess mode entered but owned zero input

## Symptoms
- Press G → HUD shows `POSSESSED`, no controls respond (WASD/mouse/fire all dead). Press G again → "released" — looks like an instant-release bug on entry.
- F6 free cam outside possess worked correctly.

## Root Cause
`TryPossess` set `cityMap.debugCameraFreedom = true` to borrow the free-cam pitch/orbit/zoom clamps. But `CamFree => debugCameraFreedom || Frozen` — the same flag doubles as the input-ownership verdict. Entering possess flipped `CamFree` on permanently, and the possess Update's `CamFree` yield (`"free-cam owns input"`) early-returned every frame: `Active = true`, HUD `POSSESSED`, and nothing ever read WASD/mouse/fire.

Three earlier input gates made the confusion worse — WASD fly required `!PossessController.Active` (dead under F6+possess from both sides), `HandleMouseCamera` yielded on `!Frozen` only (mouse camera dead under F6), and `HandleClick` had no possess gate (LMB fired the gun AND picked blocks simultaneously).

## Fix
- New `CityMap3D.borrowedFreedomClamps` — non-modal borrow of the freedom clamps. Feeds the four clamp sites (`orthographicSize`, `SetCameraPitch`, `SetCameraAnglesImmediate`, orbit-distance select) alongside `debugCameraFreedom`, but `CamFree` stays `debugCameraFreedom || Frozen` only.
- `TryPossess`/`Release` now save/set/restore `borrowedFreedomClamps`; the user's F6 flag is never touched — toggling F6 mid-possession is the designed inspect handoff and persists through Release.
- Input ownership made uniform across all consumers: **possess holds input iff `Active && !CamFree`** — WASD fly needs only `CamFree`, `HandleMouseCamera`/`HandleClick` yield at `Active && !CamFree`.
- Cursor follows ownership: unlocks while the fly owns input (F6 inspect needs a real pointer), re-locks when possess resumes holding input.

## Related Files
- `Assets/Scripts/Sim/PossessController.cs` — `TryPossess` (~498), `Release` (~549), `Update` CamFree yield + cursor (~119)
- `Assets/Scripts/UI/CityMap3D.cs` — `borrowedFreedomClamps`, `CamFree`, fly gate (~750), `HandleMouseCamera` (~861), `HandleClick` (~2358)
