# Combat Cover State — Autonomous Combat Phase 1

**Created**: Oct 16, 2026
**Status**: 🚧 In Progress — Phase 1 (seek cover behind vehicle) implementing now
**Depends on**: PROJECTILE_SYSTEM.md, GLASS_SHATTER_MECHANIC.md, VEHICLE_VOXEL_ASSETS.md

---

## Goal

First autonomous combat behavior: **Vinny enters a Combat state, scans a radius for cover,
moves to the best spot, and ducks behind it at the required depth.** Aiming is the
pop-up/exposed phase's job (planned: peek → acquire → ≤6 shots → duck → reload) —
**crouched = taking cover = not aiming**. The `ArmAimSolver` refuses to arm while
the crouch pose is active and drops its channels instantly if a crouch begins
mid-aim (see `docs/known_issues/runtime/IK_TORSO_OVERRIDE_STOMPS_CROUCH_LEAN.md`).
All combat in Steel City is
autonomous — the player issues orders; units execute. The third-person possess rig stays
available as an observer/test harness (and is the control path Steel Tide will reuse).

## Phase 1 Scope

1. **Vehicles become solid voxel bodies** — registered into `VoxelCollisionWorld` with yaw,
   so projectiles/raycasts hit them exactly like buildings.
2. **Vehicles become damageable** — bullet impacts write the same bullet-hole char as walls;
   windshield glass (mat 147 + pane ids) shatters like building panes.
3. **Cover search** — radius query around the combatant; candidate points validated by an
   actual occlusion raycast toward the threat (cover = physically true, not geometric guess).
4. **Combat state machine** — `Idle → SeekingCover → InCover` (crouch at solved depth; no aim — exposed phase TBD).

## Architecture

### Vehicle solidity

`VoxelVehicle` loads the asset's flat voxel array (cached per `assetFileName` — the shared
rest copy is safe because damage copy-on-writes before mutating) and calls
`world.RegisterVolume($"veh_{instanceId}", data, dims, transform.position, voxelSize, yawDeg)`.
`transform.position` is already the volume corner (existing convention) and `yawDeg` rides the
existing `SolidVolumeSet` yaw path. Re-registration happens when the transform moves beyond an
epsilon — parked cars register once, traffic re-registers on motion. `OnDestroy` unregisters.

### Vehicle damage — promote-on-hit

Damage writes need a writable GPU buffer, but instanced vehicles share `sharedVoxelBuffer`.
Rule: **stay instanced until first hit, then promote.**

- `VoxelChunkManager.PromoteToDamageable(volumeId)` moves the `InstancedCharacter` into a
  new singleton `InstancedGroup` keyed `dmg_<volumeId>` — same dims/asset, private
  `sharedVoxelBuffer` uploaded from the COW'd CPU array, shared `paneBuffer` reference.
- `HasWritableVolume` / `UploadVoxelEdits` gain a third branch (`promotedVolumeLookup`)
  next to chunk and sector paths — same dirty-cell upload machinery.
- `VoxelDamageSystem` resolves `veh_*` targets by promoting first, then the existing
  puncture/halo/char path runs unchanged. Shots before promotion hit the collision copy
  but skip visuals — promotion happens inside `OnShotResolved`, so the *first* visible
  impact already writes.

Singleton groups keep `useComputePose = false` (vehicles don't pose) — the shader samples
the private buffer directly, and `worldOffset`/`yaw` per-frame sync means a damaged vehicle
can still drive away carrying its holes.

### Cover search — `CoverFinder`

`SolidVolumeSet` exposes `QueryVolumes(Bounds)` over the existing XZ broadphase grid.
For each volume within `coverRadius` of the combatant:

1. Cover point = volume center − `normalize(threat − center)` × (extent along dir + standoff)
   — i.e. the point on the far side of the obstacle from the threat, `coverStandoff` (~0.6m)
   off the surface.
2. **Validate**: raycast cover-point-chest-height → threat must hit *that* volume (LOS
   blocked = real cover), the point must be `Free` per `SamplePoint`, and groundable.
3. **Score**: prefer short move distance; reject points inside other solids.

Debug: candidates drawn via `PathDebugRenderer` — green = valid, red = rejected, cyan = chosen.

### CombatController (on Vinny's rig)

```
Idle ──EnterCombat(threatPos)──▶ SeekingCover ──reached──▶ InCover
                                   │                        │
                                   └── no cover ──▶ HoldPosition (crouch in place)
```

- SeekingCover: kinematic steer (walk speed, ground-probe Y, `animState=Walking`).
  Cover moves off-sidewalk — no waypoint graph needed at this range.
- InCover: `animState=Crouching` (state 5) at the picked wall's solved `crouchDepth`,
  yaw-faced toward the threat — **no IK aim** (the solver's torso-assist quat would
  replace `bodyRot` and erase the crouch lean; gated in `ArmAimSolver`).
- `ExitCombat()` → back to Idle, IK released.

### Cover selection — `CoverField` (influence map)

Cover picking moved from a discrete candidate list to a **scalar influence field**
(the influence-map / Dijkstra-map pattern — compute the field once, every agent
reads it locally, nobody stores routes):

Two phases — matching the fact that **quality is threat-independent**:

1. **Quality pass** (recomputed on move/rescan only): every cover-capable wall
   column — regardless of facing — splats its stand-spot into a ground-aligned
   grid (`fieldRes`² over `radius`, origin snapped), then one decayed dilation
   ring turns splats into continuous zones. Result: a dense cover-potential
   map — walls and car sides paint hot, open ground stays a dim exposed wash.
2. **Threat combine** (cheap, re-run per threat change): only quality cells ≥
   the test threshold get validated — facing filter → ground probe →
   free-space → authoritative threat-chest → **posed crouch eye** LOS ray.
   Cell score = `quality × (proxBlend + proxWeight·proximity)`.

`TryPickCover` returns the field maximum — **the heatmap IS the decision
surface**: the pick flashes white on the Threat (composite) layer. Candidate
posts/LOS beams remain as *visualization of tested cells*.

### Adaptive crouch depth — cover height picks the duck

The classification floor is no longer a single fixed eye band — it is **two
authored crouch endpoints** and a continuous depth axis between them:

- `.anim.json` carries `crouching` (shallow) + `crouchDeep` (deep) — same
  scalar block. `crouchDepth` 0..1 lerps every crouch scalar inside the pose
  eval (CPU `ComputeGroupRotation`/`PosePoint`, GPU `_InstanceAnimData[i*3+2].x`
  → `CharacterPoseCompute` + proxy DDA path, ASP slots 15-18 hold the deep copy).
  Missing `crouchDeep` in old files → shallow cloned into it = depth inert.
- `CoverSensor` evaluates the posed eye anchor at depth 0 AND depth 1
  (`EyeOffset`/`EyeOffsetDeep`) — the deepest achievable eye IS the
  usable-cover floor, authored anatomy rather than a hardcoded threshold.
  Walls whose top clears the deepest eye classify `Crouch` and become
  duckable; below that = `Low`, genuinely unusable.
- Per-wall required depth: `reqDepth = (spanTop − depthMargin − baseY −
  EyeOffset) / (EyeOffsetDeep − EyeOffset)` clamped 0..1 — *minimum* depth that
  puts the posed eye `depthMargin` (0.1 m) below the wall top. The solver picks
  the shallowest working duck, so agents visibly perch behind tall cover and
  flatten behind low cover.
- `CoverField` validates each cell's threat→eye LOS at *its* wall's
  required-depth eye (`volEye` map: volId → eye height/forward at that depth) —
  a low wall only validates if the deep-crouched eye actually clears it.
- `TryPickCover` reports `reqDepth`; `CombatController` eases
  `anim.crouchDepth` toward it (`crouchDepthSpeed`, ~3/s) on arrival — the
  settle is a parametric ease, not a snap. Exit/hold resets to 0.
- Author cover ≥ `depthMargin` above the *solvable* eye band; authoring a
  deeper `crouchDeep` literally expands the short-cover range downward.

Layers (COVER tab): **Threat** (composite score — the pick surface),
**Quality** (whole-disc paint — air = dim red wash, cover faces hot),
**Exposure** (hidden teal / exposed red / invalid slate), **Proximity**
(agent-centric radial). Field rebuilds on the sensor cadence (0.4 s / 0.4 m
move); ~hundreds of rays per build, same order as the old discrete scan.

**Multi-agent direction**: the field is keyed by threat — when squad combat
lands, combatants facing the same threat share one field instead of running
private scans (wrap `CoverField` in a threat-bucketed service). The city-wide
static half already exists: `VoxelCollisionWorld`'s registered volumes + lazy
column profiles ARE the shared cover registry.

## Phase 1 explicit non-goals

- Return fire / fire discipline (Combat v2: peek-and-shoot cadence)
- Threat is a fixed debug point — live threat tracking (last shooter, muzzle reports) next
- Character↔projectile hits (spatial-hash actor damage is a separate milestone)
- Multi-unit combat / squad orders (Phase 4d gang sim territory)

## Cover pit testbed — `cover_pit` block (phase-0 duel lot)

Dedicated debug lot modeled on the firing-range template. `block_45` ("Central
Block 45 [COVER TESTBED]", row 4 col 4 — adjacent to the range at col 5) is a
single-building block whose `cover_pit.stasset` (192×48×192, authored by
`Tools/gen_cover_pit.py` → `json_to_stasset.py`) contains:

- asphalt pad + stone curb ring (full footprint)
- **one shared 4-box depth ladder** (layout rev4 — adaptive-crouch testbed):
  a single straight row mid-lot (z≈92-99, ~7m ahead of Vinny's plate),
  ascending duck-depth left→right, usable from either face:

  | anchor | top | expected `reqDepth` |
  |---|---|---|
  | `cover_deep` | 0.8m | ~1.0 — deepest authored duck only |
  | `cover_mid` | 1.0m | ~0.5 — between the endpoints |
  | `cover_crouch` | 1.3m | 0 — the shallow authored crouch clears it |
  | `cover_wall` | 2.0m | Full class — no duck at all |

  Boxes 2.4×0.8m with 2.0m navigation gaps; dark-wood lip on BOTH long faces
  (either fighter can hide on either side). Dark-wood marker pads south/north
  of each box carry the pick anchors (`cover_*` = Vinny's south side,
  `cover_*_n` = Tan's). Expected `reqDepth` assumes eyeShallow≈1.0m /
  eyeDeep≈0.8m / margin 0.1m — tune `LADDER` heights in the generator against
  the live `EyeOffset`/`EyeOffsetDeep` readouts.
  ⚠️ **Cover-height floor**: a wall whose top sits at/below the *deepest*
  posed eye + margin is `Low` (unusable). The `cover_deep` box deliberately
  grazes this boundary — if it ever classifies unusable, the authored deep
  eye is higher than assumed; raise the box or deepen the pose.
- painted **attachment points** straight across the lot centerline:
  `cover_stand_a` (south — Vinny, x≈95.5 z≈24.5), `cover_stand_b` (north —
  Civilian_02 "Tan Suit", x≈95.5 z≈166.5), plus the four `cover_*` pick
  anchors (x≈29.5/73.5/117.5/161.5, z≈89 south / z≈103 north) — all resolved
  via the same `CityMap3D.TryGetBuildingPointLocal` path as `firing_position_N`.

**Harness flow** (DebugHUDManager → Combat tab): clicking the pit block while
`Dwelling` opens the Combat tab on a pit branch — `SETUP PIT` exits any active
combat on both rigs, forces `Idle` pose ("standing, guns at side" per the
phase-1 spec), teleports Vinny to stand A facing B (via `EventPlayer.DebugSnapTo`,
same as range lanes) and Tan to stand B facing A (via `SnapCharacterTo` —
generic corner-offset snap + down-raycast for non-EventPlayer rigs), and arms
the threat marker on Tan's feet so the V-key threat layers go live.
`START DUEL` runs Vinny's `EnterCombat` keyed to Tan's live feet. An
**opponent mode toggle** in the pit panel selects Tan's behavior:
**target dummy** (default — he stands Idle while Vinny's cover+aim pipeline
gets dialed in; any live combat on him is stood down) or **AI duel**
(symmetric — `cc.EnterCombat` fires on Tan too, keyed to Vinny's feet, so both
advance to their own cover, crouch, and aim across the lot). Tan's rig gets
the combat stack (`CoverSensor` + `ArmAimSolver` + `CombatController`) at
spawn via `CityMap3D.AttachCombatStackWhenReady` — same canonical attach rule
as Vinny; `VisionSensor` stays Vinny-only (it's the player-facing debug sense).

**Cover picks** (TEST LOTS panel): a `deep / mid / crouch / wall` button row
forces Vinny's choice to that box's anchor pad — `cc.EnterCombat(threat,
anchor)` → `Sensor.TryPickCoverNear` → `field.PickNear`, the nearest Hidden
cell to the anchor inside 2.5m (NOT a teleport — the pick still rides the real
validated field, so `reqDepth` is honestly solved). Works mid-combat for A/B
sweeps: pick deep → watch the duck settle → pick wall → he walks and stands.
`LastResult` reports `depth N.NN` per pick; an anchor miss logs loudly instead
of silently holding position.

## Test (manual)

1. Combat tab → **ENTER COMBAT** (threat = crosshair aim point).
2. Vinny scans, picks the best cover within radius, walks, crouches, aims.
3. Shoot the vehicle he's hiding behind → body shows char pits, windshield shatters.
4. `V` debug still available; cover candidates visible as beams.
5. Cover pit: while Dwelling, click the `[COVER TESTBED]` block → Combat tab →
   **SETUP PIT** → **START DUEL**. Expected: Vinny on the south plate, Tan Suit
   on the north plate, orange threat cross on Tan, CoverThreat/CoverExposure
   V-modes paint both crates, Vinny walks to the south face of a crate, crouches,
   aims north at Tan.
