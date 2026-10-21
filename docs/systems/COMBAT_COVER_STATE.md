# Combat Cover State — Autonomous Combat

**Created**: Oct 16, 2026
**Status**: ✅ Phase 1 complete (verified cover seek + measured-silhouette duck) — Phase 2 exposure engagement ✅ implemented
**Depends on**: PROJECTILE_SYSTEM.md, GLASS_SHATTER_MECHANIC.md, VEHICLE_VOXEL_ASSETS.md

---

## Goal

First autonomous combat behavior: **Vinny enters a Combat state, scans a radius for cover,
moves to the best spot, and ducks behind it at the required depth.** Phase 2 replaces the
verdict-based fire gate with **exposure** — he may IK-aim and fire whenever enemy voxels
are visible from his posed eye, including mid-duck over his own parapet; when his eye can
no longer see any enemy voxel he stows and crouch-idles, retaining `lastSeenTarget` for
the pop-up/search phases (see *Phase 2 — Exposure-Based Engagement* below). The
`ArmAimSolver` refuses to arm while the deep-crouch pose is active and drops its channels
instantly if a crouch begins mid-aim (see
`docs/known_issues/runtime/IK_TORSO_OVERRIDE_STOMPS_CROUCH_LEAN.md`).
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
- InCover: `animState=Crouching` (state 5, deep band) or `Idle` (morph/Full) at
  the picked wall's solved `duck`,
  yaw-faced toward the threat — **no IK aim** (the solver's torso-assist quat would
  replace `bodyRot` and erase the crouch lean; gated in `ArmAimSolver` on
  `EffectiveState == Crouching || duck > 1.02 || coverContext` — the scalar arm
  catches ducked-Walking movers; `coverContext` is the drive's live posture
  verdict. Phase 2 changes the *fire* gate from posture to exposure: `coverContext`
  keeps driving duck/pose, while aiming eligibility becomes `visibleVoxels > 0` —
  a ducked hood whose posed eye still sees the enemy can shoot over his own wall).
- **Movement discriminates the duck channel**: the same `duck` scalar renders the
  authored hold pose when stationary (`Crouching` pick → `crouching`/`crouchDeep`
  endpoints) and the crumple-gait when moving (`Walking` pick → `walkDuckMid`/
  `walkDuckPose` deltas under the gait). Applies to possess cover-walk and to
  SeekingCover, where **combat owns the approach posture** — `anim.duck` eases
  toward the *destination* wall's requirement (`seekDuckEase` 2.5/s) for the
  whole advance and `DuckSpeedFactor` slows the step to match the gait (deep
  approach = crab-walk). The painted zone no longer reaches out — it's a
  uniform `standoff` band marking the stand spot; possess stays zone-bound
  (in = duck, out = release), and **threat-side is never a zone**: with a
  threat pinned, `TryZoneAt` only answers `Hidden`-validated cells and the
  ring probes apply the field's own facing test (`dot(normal, dirToThreat) <
  facingDot`) — standing on the shooter's side of a wall can't duck him
  (`POSSESS_DRIVE_DUCKS_ON_THREAT_SIDE`).
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

The classification floor is no longer a single fixed eye band — it is **three
authored anchors** (stand, shallow crouch, deep crouch) and a continuous `duck`
scalar spanning them. Cover now speaks **duck-space (0..2) end-to-end** —
`anim.duck` is written directly, no `crouchDepth` alias detour. The solve is
piecewise over `h(duck)`: sub-1 yields the stand→shallow **morph band** (walls
between stand- and shallow-top produce a partial fold via `icr`/`wd`, never a
quantized snap); over-1 yields the shallow→deep band (state-5 pick):

- `.anim.json` carries `crouching` (shallow) + `crouchDeep` (deep) — same
  scalar block. `duck` lerps every crouch scalar inside the pose eval
  (CPU `ComputeGroupRotation`/`PosePoint`, GPU `_InstanceAnimData[i*3+2].x`
  → `CharacterPoseCompute` + proxy DDA path, ASP slots 15-18 hold the deep copy).
  Missing `crouchDeep` in old files → shallow cloned into it = depth inert.
- `CoverSensor` measures the posed silhouette top at stand/shallow/deep
  (`HStand`/`HShallow`/`HDeep`) — the deepest achievable top IS the
  usable-cover floor, authored anatomy rather than a hardcoded threshold.
  Walls whose top clears the deepest silhouette classify `Crouch` and become
  duckable; below that = `Low`, genuinely unusable.
- Per-wall required duck: piecewise-invert `h(duck)` — `clear = spanTop −
  depthMargin − baseY`; `clear ≥ HStand` → 0; `clear ≥ HShallow` →
  `(HStand − clear)/(HStand − HShallow)` ∈ (0,1]; else `1 + (HShallow −
  clear)/(HShallow − HDeep)` ∈ (1,2], >2 = Low. The solver picks the
  shallowest working duck, so agents visibly perch behind tall cover and
  flatten behind low cover.
- `CoverField` validates each cell's threat→eye LOS at *its* wall's
  required-duck eye (`volEye` map: volId → eye height/forward via the same
  piecewise `h(duck)`) — a low wall only validates if the ducked eye actually
  clears it.
- `TryPickCover` reports `reqDepth` (duck units); `CombatController` writes
  `anim.duck` — **eased during SeekingCover** (`seekDuckEase`, combat posture),
  **immediately** on arrival/hold. Exit resets to 0. The zone itself is a
  uniform `standoff` band — it marks the stand spot, it does not reach out.
  `standoff` default 0.55 → **0.3 m (flush)**: the eye must sit close behind
  the face or the descending bore clips the lip even when the sight-line
  muzzle would clear it — the rise solve's `sees but can't shoot` verdict
  was standoff-driven as much as pose-driven. HUD slider `cover standoff`.

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

## Phase 2 — Exposure-Based Engagement (✅ implemented)

**Full spec + as-built details**: `EXPOSURE_ENGAGEMENT.md` — query contract,
sample generation, gates table, AI fire loop, debug viz, test plan.
**Sequencing layer**: `ENGAGEMENT_BEATS.md` — the authored rhythm that now
owns the arm inside `InCover` (settle → top-up → settle → rise → engaged →
duck): the emergent "opportunity fire" rows below now run under beat
authority — `wantAim` ANDs with `BeatAllowsFire`, and the **pop-up is the
`Rise` beat** (solved triangulated duck, no longer a deferred trigger).

**As built**: `CoverSensor.TryGetVisibleVoxels(threat)` samples the target's live
posed silhouette (`VoxelCharacter.SampleSilhouetteWorld` — Y-sorted strided picks
per group, ~30–60 pts) and rays the posed eye at each. `CombatController.TickExposureFire`
probes on `visInterval`, arms `solver.Arm(centroid)` while `visible>0`, fires on
Blend/convergence/cadence/cylinder, and stows on zero. `ArmAimSolver`'s posture
gate is gone — any pose may fire if the IK resolves (deep duck drops torso-assist
only). Six-shot `cylinder` → dry starts `reloading` — one chamber seats per
`roundLoadInterval` (1.5s), arm dipped the whole beat. `LastSeenTarget` retained
for the pop-up/search phases.

**Why this subsumes scripted phases** — the phases emerge from the same query:

| Beat | Emergent mechanism |
|------|--------------------|
| Advance + fire | `SeekingCover` + `AimWalk` + armed — shots keep landing while voxels show |
| Duck-in over the parapet | `duck` rising → posed eye sinking → his own wall eats the rays → visible set decays smoothly, firing fades — no transition event |
| Tucked | zero visible voxels → stow, crouch-idle, `lastSeenTarget` retained |
| Opportunity fire | enemy strays into his sightline while tucked → voxels appear → he fires — no pop-up logic needed |
| **Pop-up** | the one *authored* override: suppress cover drive, stand, re-query `lastSeenTarget` → live centroid if still exposed, stale point if he moved (suppressing fire, emergent) |
| Reload | cylinder count — S&W Model 10 = **6 shots** → auto-duck back into crouch-idle as the reload beat (no reload anim exists yet) |

**Symmetric crossfire**: *cover verdict and aim solution are the same ray from opposite
ends* — `hidden ⇐ ray(threatEye → myTop) blocked`; `hittable ⇐ ray(myEye → threatVoxels)
clears`. When Vinny pops up, his own `h(duck)` → stand makes him visible to Tan's
identical query — the pop-up opens a **mutual exposure window** and first-shot-inside-it
is the skill race (aim-feel spring + stats, not dice).

**Emergent wrinkle — parapet fire is headshot-only**: at partial duck the only exposed
voxels are the enemy's head band, so the centroid IS the head — the safest posture yields
the deadliest aim. **Accepted** (user decision): recoil + sway plus the tiny voxel count
keep it honest; "ducked = aimed shots, standing = volume" is a real trade-off axis.

**Search hand-off**: `lastSeenTarget` + the existing `lookScan` channel = the visual
search phase — pop up, find the last-seen point empty, sweep (3-step search pattern
papered, lands after this layer is proven).

**Deferred decisions** (deliberately not settled yet):
- AI pop-up trigger — cadence / staleness / player-order; testbed knob TBD
- Possess-mode pop-up control — candidate: RMB aim-override in a blue zone
- `threatEye = 0.55` constant — the LOS-validation threat eye still assumes a standing
  shooter; Phase 2 should pose-evaluate the threat's own eye (`TryGetEyeAnchor`
  already takes pose overrides) so symmetric duels stay honest
- Hit-model question — keep outcomes purely ballistic (emergent) vs a thin
  probability layer for stat control (industry): current direction = ballistic +
  skill-shaped cone + recoil/sway

## Phase 1 explicit non-goals

- ~~Return fire / fire discipline~~ → Phase 2 above (implemented)
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
   V-modes paint both crates, Vinny folds into the destination wall's duck
   **during the approach** (combat posture — eased at `seekDuckEase`, step
   slowed by `DuckSpeedFactor`), arrives at the south-face stand spot at the
   solved depth (morph walls show a partial fold), yaws toward Tan. Phase 2
   adds: he fires while Tan's voxels are visible, stows when tucked, pops up
   on the override, ducks again after 6.
