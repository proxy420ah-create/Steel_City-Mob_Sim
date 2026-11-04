# DVM-2253 — Possess Cam (third-person follow + guest release) + seat binding

**Status**: v0.2 — **IMPLEMENTED by CHAIR bench (BOSS-directed bypass of the rail's build seat)**. Headless Edge drill verified; pending boss live eyeball + rail record.
**Lane**: ARCHITECT draft → INFORMANT gate → poll → GO → ~~BUTTON bench~~ CHAIR bench on BOSS word → AUDITOR drill → CHAIR certify.
**Depends on**: DVM-2252 shipped seams (char block, WASD controller, `chPos`/`chYaw` live).
**Source of truth**: `Assets/Scripts/Sim/PossessController.cs` (verbatim port of constants + mechanism, minus combat/cover/fire lanes).

## v0.2 amendment — as-built deviations from v0.1.x

1. **Chibi scale factor `PS = VH/1.8`** (VH = actual voxel bounds .62m, NOT model-space 96vx): all Unity *distances* scale — `SUGGEST = 4·PS ≈ 1.38m`, `CAM_PAD = .35·PS ≈ .12`, `MIN_ORBIT = .9·PS ≈ .31`. A verbatim 4m boom would sit outside the 4.5m room; eye = `VH·0.85 ≈ .527m` (ground-relative). Angles/sensitivities/`BOOM_RETURN = 7` stay verbatim.
2. **Release restores the guest view** (boss law): saved camera position + orbit target restored on release — NOT Unity's freeze-the-shot. Filed as an intentional divergence.
3. **Seat binding added** (was not in v0.1 scope): `E` at `SEAT_REACH = .85` seats Vinny at the nearest chair — seat pos `SEAT_OFF = [.01, -.01, .04]` in the chair group's frame, **fitted against the authored `vinny_seated_preview` composite (all 12,608 voxels match, 0 clip, 56 pad contacts)**. Any seat key (`E`, `G`, WASD) stands him at radius `STAND_R = 1.75` and re-possesses. Sit pose = animator `DEFAULT_PARAMS.sitting` (state 11), with `legStride` sign flipped (`-1.45`) — the port's rotation convention; verified by the composite fit.
4. **OrbitControls arbitration**: `controls.enabled = false` + `controls.update()` skipped while `__possessOn` — the frame loop's `controls.update()` would otherwise clamp the boom camera back into the guest range every frame.

---

## Scope

Two camera modes over the existing deck scene:

1. **Guest cam** (current): OrbitControls view of the office — unchanged, this is the default.
2. **Possess cam** (new): third-person orbit boom pinned behind Vinny's head, driven by mouse delta. `G` toggles possess on; `G` or `Esc` releases back to the guest view.

No new render passes, no new assets. The character already exposes `chPos` (position) and `chYaw` (heading); the deck already exposes `camera` + `controls` via `initDeck3D`.

---

## ① Possess cam — grab

On `G` (while deck mode is `3d` and Vinny is loaded):

- `mode = 'possess'`; OrbitControls disabled (`controls.enabled = false`).
- Camera snaps behind his back — classic third-person:
  - `lookYaw = chYaw` (his facing yaw, degrees or rad — store consistent with three.js convention: radians)
  - `lookPitch = 12°` (`0.21 rad`, slight high angle — verbatim from `TryPossess`)
  - `boomCur = -1` (forces hard snap on first boom eval — verbatim)
- Orbit pivot pinned at head height (over-the-shoulder framing, `EyePoint` verbatim):
  - **v0.1.1 correction** — the source anchors `WorldCenter + 0.35·dims.y`; `WorldCenter` is model CENTER, so ground-relative that's `0.5h + 0.35h = 0.85h`. `chPos` is GROUND-level → `eye = chPos + up·(vinnyHeight·0.85)` (≈1.6m), not 0.35 (that lands the pivot at his waist).
  - camera position = `eye + boomDir * boomCur`; camera `lookAt(eye)`
- Cursor locked (`canvas.requestPointerLock()`) — Unity locks `Cursor.lockState`; the web equivalent is pointer lock on the renderer canvas.

## ② Possess cam — mouse look (no smoothing)

While possessed, mouse delta drives the orbit angles **1:1, immediate** — the source explicitly avoids look smoothing (`SetCameraAnglesImmediate` per frame):

- `lookYaw += d.x * yawSens`, `yawSens = 0.18°/px` → in radians: `0.00314 rad/px`
- `lookPitch = clamp(lookPitch - d.y * pitchSens, -85°, +85°)`, `pitchSens = 0.15°/px` → `0.00262 rad/px`
- Recompute `boomDir` each frame from the fresh angles:
  - `horiz = cos(pitch)`; `boomDir = (-horiz * sin(yaw), sin(pitch), -horiz * cos(yaw))`
  - (verbatim — the boom points *behind* the look direction; camera sits at `eye + boomDir * dist` facing `eye`)

## ③ Boom collision — snap in, ease out

The Unity version raycasts the voxel world along the boom and clamps before the camera clips geometry. Web port: the controller's obstacle set is `SOLIDS[]` — 2D circles `[x, z, r]` (capsule→circle pushout, no height). **v0.1.1 correction** — the honest equivalent is ray-vs-**cylinder-with-cap**: intersect the boom ray against each circle's cylinder (radius `r`, capped at a furniture height ≈1.2m) plus the FLOOR wall/ceiling ranges; no voxel BVH needed, no AABB claims — the set is circular, not boxed.

Per frame:

- `want = orbitSuggest = 4.0` (verbatim initial/suggested boom length)
- Raycast from `eye` along `boomDir`, range `want + camPad`:
  - `camPad = 0.35` (verbatim — padding between camera and the surface hit)
  - if hit at distance `d`: `want = max(camMinOrbit, d - camPad)`
  - `camMinOrbit = 0.9` (verbatim — boom can't collapse into his head)
- `boomCur = boomCur < 0 || want < boomCur ? want : lerp(boomCur, want, boomReturnSpeed * dt)`
  - `boomReturnSpeed = 7.0` (verbatim — snap in hard on a clip, ease back out at 7/s; no pop when the blocker clears)
- Camera sits at `eye + boomDir * boomCur`.

Room bounds also clamp: `FLOOR` x/z ranges + ceiling y — same constants the controller already uses.

## ④ While possessed — character control

- WASD stays camera-relative **to the look yaw**, not the guest cam (Unity: `fwd = (sin(yaw), 0, cos(yaw))`).
- Speeds verbatim: `walkSpeed = 2.5`, `sprintMult = 2.2` (Shift), `turnSpeed = 10` — already shipped constants.
- Facing: the character turret-faces the aim direction — for the cosmetic port, `chYaw` slerps toward `lookYaw` while moving (turn-speed capped), matching the source's `Slerp(rotation, want, turnSpeed * dt)`.
- Walk gait continues from the existing `pose(state, phase)` — state machine unchanged.

## ⑤ Release — `G` or `Esc`

- `mode = 'guest'`; OrbitControls re-enabled (`controls.enabled = true`).
- **Boss-override law**: the camera stays wherever the possess boom left it — "lock the shot" (verbatim `Release()`: focus deliberately left, `prevOrbit` restored but position held). The guest cam resumes orbiting from that view.
- Pointer lock released (`document.exitPointerLock()`).
- Vinny holds his position/heading; state drops to `idle` (verbatim: `anim.SetState(Idle)` on release).

## Out of scope (source lanes NOT ported)

- LMB fire / weapon mount / aim solver — no combat in the office.
- Cover drive / duck probes — no cover field in the room.
- `CamFree` fly-yield, freedom clamps, Alt+pose latches — deck has no free-cam mode.
- Unstuck teleport — the room's obstacle set is too small to wedge into; revisit if a pocket appears.

## Acceptance drill

- `G` snaps cam behind Vinny at 12° pitch, 4m boom, cursor locked.
- Mouse-look orbits his head 1:1 (no smoothing lag); pitch clamps at ±85°.
- Boom clamps before clipping furniture/walls (`camPad .35`), never closer than `camMinOrbit .9`; eases back out when clear.
- WASD walks him camera-relative; Shift sprints; facing follows look yaw.
- `G`/`Esc` releases: controls resume, the cam holds its last view (lock-the-shot), Vinny idles in place.
- Perf: possess adds one cylinder raycast/frame — negligible over the gated pose eval.

## Est. size

~120 lines inside the existing char block: mode flag + keyhook, angle state, boom ray-vs-cylinder helper, camera drive in `tickChar`, release path.
