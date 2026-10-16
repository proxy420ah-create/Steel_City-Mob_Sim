# Combat Cover State — Autonomous Combat Phase 1

**Created**: Oct 16, 2026
**Status**: 🚧 In Progress — Phase 1 (seek cover behind vehicle) implementing now
**Depends on**: PROJECTILE_SYSTEM.md, GLASS_SHATTER_MECHANIC.md, VEHICLE_VOXEL_ASSETS.md

---

## Goal

First autonomous combat behavior: **Vinny enters a Combat state, scans a radius for cover,
moves to the best spot, crouches, and aims at the threat.** All combat in Steel City is
autonomous — the player issues orders; units execute. The third-person possess rig stays
available as an observer/test harness (and is the control path Steel Tide will reuse).

## Phase 1 Scope

1. **Vehicles become solid voxel bodies** — registered into `VoxelCollisionWorld` with yaw,
   so projectiles/raycasts hit them exactly like buildings.
2. **Vehicles become damageable** — bullet impacts write the same bullet-hole char as walls;
   windshield glass (mat 147 + pane ids) shatters like building panes.
3. **Cover search** — radius query around the combatant; candidate points validated by an
   actual occlusion raycast toward the threat (cover = physically true, not geometric guess).
4. **Combat state machine** — `Idle → SeekingCover → InCover` (crouch + IK aim at threat).

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
                                   └── no cover ──▶ HoldPosition (crouch+aim in place)
```

- SeekingCover: kinematic steer (walk speed, ground-probe Y, `animState=Walking`).
  Cover moves off-sidewalk — no waypoint graph needed at this range.
- InCover: `animState=Crouching` (state 5 — authored crouch pose lands here) +
  `ArmAimSolver` aims at the threat over the cover edge.
- `ExitCombat()` → back to Idle, IK released.

## Phase 1 explicit non-goals

- Return fire / fire discipline (Combat v2: peek-and-shoot cadence)
- Threat is a fixed debug point — live threat tracking (last shooter, muzzle reports) next
- Character↔projectile hits (spatial-hash actor damage is a separate milestone)
- Multi-unit combat / squad orders (Phase 4d gang sim territory)

## Test (manual)

1. Combat tab → **ENTER COMBAT** (threat = crosshair aim point).
2. Vinny scans, picks the best cover within radius, walks, crouches, aims.
3. Shoot the vehicle he's hiding behind → body shows char pits, windshield shatters.
4. `V` debug still available; cover candidates visible as beams.
