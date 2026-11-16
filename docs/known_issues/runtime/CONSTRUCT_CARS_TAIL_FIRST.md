# Construct Cars Tail-First — Authored Front Assumed −z, Really +z

**Date**: November 16, 2026
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH (every parked and cruising car on the street tables read backwards — visible immediately, user-reported)

## Symptoms
- Cruise cars "drive backwards" — the rear leads the motion, reads as reversing around the block.
- Parked cars shared the same flip (nose pointed against its lane's legal travel direction).
- Everything else was right all along — seat height `.2664`, curb offset, pull-out/repark logic — only the yaw convention was wrong.

## Root Cause
Both heading→rotY sites assumed the touring's authored front = **−z** (`Math.atan2(-hx, -hz)` under the "authored front = −z" comment):
- `blockTrf`'s `put()` — moving cars
- `mountParked` — curb slots

Unity's real drive law is `Quaternion.LookRotation(dir.normalized, Vector3.up)` (`VehicleTestSpawner`/`VehicleAgent`) — it rotates the model's **+z** onto the heading with no axis-offset correction, so the authored front is +z. `atan2(-hx,-hz)` is exactly a 180° flip at every pose the car takes.

## Resolution — `docs/construct/03_tables.js`
- `put()`: `rotY = Math.atan2(hx * mir, hz)` — +z nose follows the (mirror-adjusted) travel heading.
- `mountParked`: `rotY = Math.atan2(hx, hz)` — same convention at the curb (`hx` already mirrored there).
- Same pass closed two more parity gaps vs the Unity source (drift, not defects):
  - **Speed**: `V .05` → `V 3.0` real m/s (`VehicleTestSpawner.driveSpeed = 3.0f` serialized); `vc = V/BLDG_VS` = 60 cells/s — scale-independent. Old pace ≈ 0.9 m/s real.
  - **Lane position**: `carW + gap` off the road edge → `road + BLK.ROAD/2` = half-road center = `roadWidth/4` (1.125 m) off the street centerline — each tile paints its half's cobble stripe there, so the cars now track the painted stripe like Unity's `laneOffset`.
- `docs/systems/ROAD_LANES_AND_TRAFFIC.md` stale values corrected against source: `roadWidth` 3.0 const → **4.5 serialized**, parkOffset 1.0 → 1.9 m, lane offset .75 → 1.125 m, `VehicleAgent` row gains `driveSpeed` + the +z front-axis law.

## Verification — `docs/_cars_verify.mjs` (ALL PASS)
- 2 parked cars; each nose matches its edge's right-of-travel direction (s-edge → east `ry=π/2`, w-edge → south `ry=±π` — note −π and +π are the SAME yaw; assertions compare wrapped angular distance, not raw floats).
- Cruise `dist/time ≈ 3.02–3.03` m/s real for both cars.
- Nose leads velocity: angular distance ≈ 0 while looping.
- `__cars.seek()` → repark restores each `_slot.rotY` verbatim.
- Screenshots `probe_cars_fixed.png` / `probe_car_closeup.png` — fronts visually confirmed (grille/headlamp end toward travel direction).

## Follow-up — the maneuvers were wrong too (same session, second report)
"Parking and unparking still doesn't match": the old pull-out slid the car a
car-length BACKWARD along the curb holding its slot heading (a sideways
crab-walk) at a serialized 1.6 s stagger. Unity does neither — `RequestDepart`
lerps the slot → `lanePos + dir·1.2` (a real ~1.4 m diagonal ahead), `TrySpliceParking`
truncates the lane leg at `lanePos − dir·1.2` then diagonals into the curb, yaw
`Slerp`s onto the leg direction at 6/s (the visible angle), every segment runs at
`speed` (3.0 m/s — "consistent speed"), and all cars respond to the same keypress
("together"). `blockTrf` now mirrors that law: `stn(sc, ±LEAD)` endpoints
(LEAD = 1.2 m = 24 cells, clamped inside the leg for `sOf`), per-car `yaw` slerp
state during `leave`/`park`, `delay: 0`. Verified same run: both cars leave at
+0.06 s, diagonal ≈35 cells ≈1.75 m, yaw swings ~0.78 rad mid-maneuver, durations
match len/v, nose exactly on path tangent (Δ≈0), repark restores.

## Related Files
- `docs/construct/03_tables.js` — `blockTrf` (~line 191: `V`, `build` lane law, `put` yaw), `mountParked` (`rotY`)
- `docs/_cars_verify.mjs` — the regression probe
- `Assets/Scripts/VehicleTestSpawner.cs` — Unity ground truth (`driveSpeed`, `laneOffset`, `parkOffset`, `LookRotation`)
- `docs/CONSTRUCT_DRAFTING.md` — parked/CARS bullets hold the corrected laws
