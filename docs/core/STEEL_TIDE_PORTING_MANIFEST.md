# Steel City → Steel Tide: Porting Manifest

**Status**: 📐 REFERENCE — cross-project architecture bridge
**Steel City**: `SteelCityMobSim` (Unity 6000.3.18f1) — vertical slice, systems proven in player build
**Steel Tide**: `SteelTide/My project` (Unity 6000.3.18f1, same version) — pre-production prototype

---

## Why this document exists

Steel Tide was the original voxel laboratory: `.stasset` reader/writer, VoxelAssetStudio,
per-object raymarch rendering, and the AMS physically-driven-actor experiments all started there.
Steel City inherited that base and became the **forge** — the place where the infrastructure-grade
versions of those same systems were built, stress-tested at city scale, and verified in a real
player build (instanced city rendering, physical projectiles, dynamic destruction, direct character
control — all confirmed working).

The result: much of what Steel Tide's design docs *plan* (DOTS-heavy tactical layer, capsule FPS
controller, per-voxel damage) already exists in Steel City in **working, lighter-weight form**. The
correct move for Steel Tide is **port, don't rebuild** — then spend its unique budget on the things
Steel City doesn't have (Geoscape strategy, AMS physics actors, structural collapse).

Both projects share Unity **6000.3.18f1** — ports are file-level, not rewrites.

---

## Lineage map

```
SteelTide (voxel lab: .stasset, VoxelAssetStudio, per-object raymarch, AMS research)
    |   +- DEPRECATEDSteel_City-Mob_Sim/ — early shared codebase, DELETED Oct 2026
    |      (unique .stasset files preserved in SteelCity/VoxelAssetStudio/ARCHIVE/steeltide_snapshot_voxel_buildings/)
    v
SteelCityMobSim (the forge: city-scale infrastructure, battle-tested in player builds)
    |
    v   port back the proven systems (this document)
SteelTide (the game: Geoscape + operational layer + FPS tactical layer)
```

---

## What Steel City proved — the port catalog

### 1. Voxel collision world — the physics that isn't PhysX

`Assets/Scripts/Sim/VoxelCollisionWorld.cs` + `SolidVolumeSet.cs`

- Flat terrain grid + a registry of **live voxel volume arrays** (770+ buildings in the city build).
- `Raycast` — 3D DDA over terrain + volumes, returns exact cell/voxel/material/volumeId.
- `SamplePoint` — occupancy query (the "am I inside a wall" primitive).
- `ProbeGround` — downward scan that unions volume floors with terrain (volume-aware gravity:
  characters stand on roofs, floors, and **dynamic rubble**).
- **Replaces in Steel Tide**: the planned Burst/PhysX collision approach for voxels; `VoxelWorld`
  occupancy queries. This is the single most valuable port — every other system consumes it.

### 2. Volume-aware locomotion — proven by PossessController

`Assets/Scripts/Sim/PossessController.cs`

- WASD movement with **voxel wall blocking** (`SamplePoint` at feet + chest, X/Z slide),
  gravity via `ProbeGround`, no CharacterController, no colliders.
- Crawls through dynamic rubble. Walks on rooftops.
- **Replaces in Steel Tide**: `SimpleFPSController`, `VoxelPlayerController`, the capsule +
  CharacterController plan. For FPS mode the same locomotion core applies — camera at eye
  height instead of the third-person orbit.

### 3. Physical projectiles

`Assets/Scripts/Sim/ProjectileSystem.cs`

- Swept DDA through `VoxelCollisionWorld` — real bullets hitting real voxels.
- Seeded cone spread (skill × base cone), ShotReport (exact volume/cell/material), tracers,
  glass puncture, impact marks.
- **Replaces in Steel Tide**: `FPSVoxelShooter` (one voxel per shot), `VoxelWeaponController`
  raycast path.

### 4. Destruction + dynamic rubble

`VoxelDamageSystem.cs`, `VoxelDamageMath.cs`, `ShardPhysics.cs`, occupancy-grid edit path in
`VoxelChunkManager`/`MobSimVoxelRaymarch.compute`

- Voxel edits rebake into GPU occupancy — craters are real geometry you can walk into.
- Debris shards collide with the world via the same raycast — rubble you can crawl through.
- **Replaces in Steel Tide**: `VoxelDamageQueue`, `VoxelModifier`. **Steel Tide keeps**: the
  Teardown-style structural-integrity/flood-fill collapse vision — layer it on the same
  occupancy/dirty-edit pipeline (Steel City does local damage, not connectivity collapse yet).

### 5. Instanced sector raymarch renderer

`Resources/Shaders/VoxelProxyRaymarch.shader`, `VoxelChunkManager.cs`,
`MobSimVoxelRaymarch.compute`

- Sector atlases + per-volume instancing, occupancy-skip marching, transparent glass pass,
  GPU edit path, LOD stepping.
- **Replaces in Steel Tide**: per-object `VoxelRenderer`/`VoxelRenderFeature`/`VoxelVolumeProxy`
  as the *world* path (fine for single volumes; wrong for a battlefield).
- Scale conventions already shared: building 0.1m, character 0.01m, item/decor 0.005m voxels.

### 6. Weapon mount + IK aiming

`WeaponMount.cs`, `ArmAimSolver.cs`, painted attachment points in `.character.json`

- Two-bone IK arm aims at arbitrary targets; muzzle/transform truth drives where shots leave.
- **Replaces in Steel Tide**: `VoxelWeaponController` alignment logic.

### 7. Instanced characters + animation

`VoxelCharacter`, `CharacterAnimation`, `CharacterRig`, `ClothingSystem`

- One shared voxel buffer per model, posed-instance buffers, hundreds of animated characters.
- **Steel Tide keeps**: AMS (`VoxelActor2` family — balance, PID, ragdoll, stand-up) for
  hero/physics-driven bodies. The two are **complementary**: instanced kinematic+IK characters
  for crowds and squadmates; AMS for the bodies that must physically react.

### 8. Build pipeline hardening — the battle scars

`GpuUpload.cs`, `BuildReadinessAudit.cs` (editor), `BuildDiagnostics.cs`, canary ladder,
`GraphicsSettings m_InstancingStripping: 2`, serialized keeper materials, SVC coverage

- Hard-won: runtime-only `enableInstancing` is invisible to Unity's `StripUnused` — every
  instanced draw silently dies in a player build. D3D12 rejects >~16MB `SetData` uploads —
  chunk everything through `GpuUpload`.
- **Steel Tide hasn't hit player-build stage yet** — port the audit + diagnostics + canaries
  *before* the first build attempt, not after.

### 9. Debug/inspection workflow

`DebugHUDManager` (pick voxels, free-fire combat tab, possess), BuildLog tab, perf ticker

- "See what the player sees" workflow: audit → build → canaries + BuildLog.

---

## Deprecation list (Steel Tide side)

| Current | Status |
|---|---|
| `SimpleFPSController` + capsule | **Deprecated** — replaced by volume-aware locomotion |
| `FPSVoxelShooter` (1 voxel/shot) | **Deprecated** — replaced by ProjectileSystem |
| `VoxelPlayerController_legacy` | **Deprecated** already in name |
| Per-object `VoxelRenderer` world path | **Superseded** for world rendering (keep for props/tests) |
| DOTS-everything tactical plan | **Descoped** — Steel City proves MonoBehaviour + sparse arrays + instancing is enough at city scale; DOTS remains a valid choice for the *operational* layer (1000s of squads) only |
| `DEPRECATEDSteel_City-Mob_Sim/` | **Deleted** — was a 24-script pre-voxel prototype; unique assets archived to `VoxelAssetStudio/ARCHIVE/steeltide_snapshot_voxel_buildings/` |

## What stays uniquely Steel Tide

- **Geoscape** — planetary strategic layer; no Steel City equivalent exists.
- **AMS physics actors** — VoxelActor2, BalanceController, PIDAssistant, ragdoll, stand-up.
- **Structural collapse** — flood-fill integrity on the occupancy pipeline (new work, not a port).
- **First-person camera** — same possess locomotion core, camera at eye point, pitch drives view.
- Its own assets — but `.stasset` is the shared format, so VoxelAssetStudio serves both.

## Suggested port order

1. **Collision world** (`VoxelCollisionWorld` + `SolidVolumeSet`) — everything else consumes it.
2. **Locomotion core** — extract possess movement/gravity into a shared character-motor; add FPS camera variant.
3. **ProjectileSystem + damage + rubble** — combat becomes real immediately.
4. **Renderer migration** — per-object → instanced sectors/chunks (biggest effort; do it before content scales up).
5. **Build hardening** — audit + diagnostics + canary ladder, then first player build.
6. **WeaponMount + ArmAimSolver + character instancing** — armed crowds.
7. **Layer AMS on top** — physics actors riding the same collision world.

---

*Generated October 2026 — after the possess-mode playtest confirmed the locomotion/combat feel
ports directly to the Steel Tide experience.*
