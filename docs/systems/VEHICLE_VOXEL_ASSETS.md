# Vehicle Voxel Assets — Rendering, Articulation Plan, Editor Submodule

**Status**: 📋 DESIGN + POSTMORTEM — how vehicles render today, the instancing gotchas we hit, and the roadmap to articulated vehicles
**Created**: October 5, 2026
**Companion docs**: `INSTANCED_RENDERING_PITFALLS.md`, `ROAD_LANES_AND_TRAFFIC.md`, `COMBAT_VEHICLE_DESIGN.md`, `VOXEL_GROUP_ANIMATION.md`, `../core/VEHICLE_RE_REFERENCE.md`

---

## 1. How Vehicles Render Today

`VoxelVehicle` (`Assets/Scripts/Sim/VoxelVehicle.cs`) is a self-contained component:

- Loads `StreamingAssets/voxel_buildings/<asset>.stasset` for voxel data + dims (note the folder — see §5).
- `transform.localPosition` is the volume **corner**; `centerPosition`/`PlaceAtCenter()` take a center and subtract half the XZ extent.
- Registers via `VoxelChunkManager.RegisterInstancedCharacter(...)` → joins the per-`assetFileName` `InstancedGroup`. One shared voxel buffer per asset; every `VoxelVehicle` with the same asset is another entry in `group.instances`.
- Per instance the system carries **position + yaw only** (`ic.worldOffset`, `ic.yaw` → `float4` in `_InstanceOffsets`). Yaw rotates about the volume center in volume-local space — there is no pitch/roll/scale channel.
- Each frame `RenderInstancedGroup` packs offsets + `Matrix4x4` proxy transforms and issues one `DrawMeshInstanced(proxyCubeMesh, …, visibleCount)` per group. `VoxelRenderBridge` executes the CommandBuffer via `endCameraRendering`.

Because `vehicle_civilian_car_0.stasset` has **no `.groups` file**, the group renders on the **static shared-buffer path** — every instance raymarches the same 20×16×30 rest buffer at its own offset/yaw. This is the cheapest possible instancing: one 9,600-voxel buffer serves the whole fleet, zero per-frame compute.

## 2. The Three Buffer Modes (and the flag that used to conflate them)

`RenderInstancedGroup` picks a buffer per group; the raymarch shader picks a per-instance read offset. Before the `_SharedRestBuffer` fix, the shader inferred the mode from `_GroupIDsEnabled` alone and got mode 3 wrong — see Pitfall #7 in `INSTANCED_RENDERING_PITFALLS.md` (second vehicle invisible) and `../known_issues/rendering/STATIC_INSTANCED_OOB.md`.

| Mode | Gate | Buffer bound as `_VoxelData` | Instance offset | Used by |
|---|---|---|---|---|
| **Posed per-instance** | `groupIDBuffer != null && useComputePose` | `posedVoxelBuffer` (N slices) | `instanceID × totalVoxels` | Characters (walk/aim/flinch on GPU) |
| **Inverse-transform groups** | `groupIDBuffer != null && !useComputePose` | `sharedVoxelBuffer` | 0 | (available, currently unused) |
| **Static shared** | no groupIDs | `sharedVoxelBuffer` | 0 | Vehicles, props |

Uniform truth table: `_GroupIDsEnabled` = "groupIDs exist AND not compute-posed" (fragment-side inverse-transform sampling, body bob). `_SharedRestBuffer` = "the bound buffer is a single shared copy, offset is always 0". Set by `RenderInstancedGroup` as `!useComputePose`; everything else (chunk/sector/building blocks) leaves it 0.

**Cost for scale**: posed character slice = `884,736 × 4 B ≈ 3.5 MB per instance` (Civilian1, 96³) + 2 compute dispatches per group per frame. Vehicle slice would be `9,600 × 4 B ≈ 38 KB` — cheap either way, but pointless while the shape is rigid.

## 3. Gotchas Catalog (vehicle-flavored)

| # | Gotcha | Status |
|---|---|---|
| V1 | GroupID-less instanced assets read `instanceID × totalVoxels` into a single-slice buffer → instances ≥1 invisible. Fixed by `_SharedRestBuffer`. | ✅ FIXED (`STATIC_INSTANCED_OOB.md`) |
| V2 | Proxy cube is axis-aligned, ignores yaw — non-cubic volume at 90° heading can crop nose/tail (Pitfall #8). | ⚠ Watch item |
| V3 | Vehicles are registered under the **`voxel_buildings`** StreamingAssets subfolder (`RegisterInstancedCharacter(…, "voxel_buildings")`, and `LoadAssetDims` reads the same folder). A `voxel_vehicles/` split is a one-line change at both sites — do it when the Vehicles editor starts emitting assets. | 📋 Open |
| V4 | Per-instance channel is **pos + yaw only**. No pitch/roll — suspension lean, flipping, axle-spin can't come from instance transforms. | By design (for now) |
| V5 | No per-instance paint: material remap exists **only** inside the pose compute kernel (`_InstanceMaterialRemap`). All vehicles of an asset currently share one paint job. | 📋 Open, see §4.3 |
| V6 | `transform.position` (world) feeds the shader, while `PlaceAtCenter` writes `localPosition` — correct today only because `TestVehicles`/`mapRoot` sit at world identity. A moved mapRoot would shift every rendered volume; keep the anchor at origin or write worldPosition in `ApplyCenterPosition`. | ⚠ Latent |

## 4. Articulated Vehicles — When and How

### 4.1 Decision rule

Keep vehicles on the **static path** while they're rigid bodies. Adopt a **posed path** the moment any voxel moves relative to the car body. The docs' greenfield list (`VEHICLE_RE_REFERENCE.md`) already names the candidates: turning wheels, steering wheels, doors, suspension. Vinny entering/exiting (the "Vinny uses the car" milestone) wants a door-swing at minimum.

### 4.2 What compute-pose actually requires — it is NOT a flag flip

The pose kernel (`CharacterPoseCompute.compute`) is **character anatomy**, not a generic rig: `gid` 0–9 are hard-coded to body/head/arms/legs/shins/forearms with default pivots by gid, and poses come from a hard-coded state table (idle/walk/look/aim/crouch/flinch/aim-sweep). A vehicle needs:

1. **`.groups` data authored** — per-voxel group IDs for each articulated part (below).
2. **A vehicle pose mode in the kernel** — a second pose function keyed on asset kind, mapping gid → (axle spin | steer | door hinge | suspension) driven by per-instance state. The existing `_InstanceAnimData` float4 has a free `.w` — can carry speed/steer/door-open fraction without ABI changes.
3. **Pivots per gid** — same authored-pivot mechanism characters use (`pivot_N` tags → normalized pivot buffer); a wheel's pivot is its axle center, a door's is its hinge edge.
4. **`useComputePose` gating already exists** — the moment a `.groups` file loads, the group flips to the posed path automatically. The work is all in the kernel + authoring.

### 4.3 Faction / per-citizen paint (cheaper than posing)

Per-instance material remap is implemented **inside** the compute kernel (`_RegionIDs` + `_InstanceMaterialRemap`), so paint-by-region currently only exists for posed groups. Two options when needed:

- **Lightweight (recommended first)**: designate the body-panel material ID(s) as "paintable" in the raymarch shader, swap in a per-instance color. `_InstanceOffsets[i + count].w` is a free float — enough to pack a palette index; a small `_VehiclePaint` color buffer does the rest. No compute pass, scales to a full fleet.
- **Full region remap**: only comes free once vehicles are on the posed path anyway.

## 5. Vehicles Editor Submodule (planned, alongside Building / Character / Item)

`VoxelAssetStudio/voxel_editor.html` currently has three asset types (`ASSET_TYPE_PRESETS`: `building`, `character`, `prop`) with per-type part-tag palettes (`CHAR_ATTACH_GROUPS`, `BUILDING_POINT_GROUPS`, `ITEM_PART_GROUPS`). A `vehicle` type gets symmetric treatment:

```
vehicle: { label: 'Vehicle', icon: '🚗', voxelSize: 0.05,
           defaultDims: [20, 16, 30],                       // ≈1.0 × 0.8 × 1.5 m car footprint
           defaultModel: null }
```

**New `VEHICLE_PART_GROUPS`** (reusing the `attachmentPoints`/`itemParts`/`groupMap`/`pivots` export contract so the same transform/undo/mirror machinery applies):

| key | kind | purpose |
|---|---|---|
| `chassis` | body group (gid 0) | everything rigid |
| `wheel_fl` / `wheel_fr` / `wheel_rl` / `wheel_rr` | part groups (gid 1–4) | spin about axle pivot; front pair also steers |
| `door_l` / `door_r` | part groups (gid 5–6) | hinge swing for enter/exit anims |
| `pivot_1..6` | painted pivots | axle centers / door hinge edges (same `pivot_N` → gid convention as characters) |
| `seat_driver` / `seat_passenger` | attach points | where a character volume welds while riding |
| `entry_l` / `entry_r` | attach points | door-side stand spot the ped path-finds to before boarding |
| `headlight_l` / `headlight_r`, `taillight_*` | attach points | light cones / material-index for future siren + headlight pass |
| `cargo`, `trunk` | attach points | prop stowage, body-in-trunk the mob-sim way |

**Export**: consolidated `.vehicle.json` mirroring `.character.json` (voxels + `groups` + `attachmentPoints` + `animParams.pivots` + `assetType: "vehicle"`). Output target should become `StreamingAssets/voxel_vehicles/` — requires the two-line folder change in `VoxelVehicle` (see V3).

**Mirror support**: `MIRROR_GID_PAIRS` needs vehicle entries (wheel_l↔wheel_r, door_l↔door_r) or vehicles get literal-copy mirroring only.

## 6. Sequencing

1. ✅ Static shared-buffer instancing works for N rigid cars (`_SharedRestBuffer` fix).
2. 🔜 Confirm Pitfall #8 (proxy yaw clip) in playtest; fix proxy extents if seen.
3. Next feature gate: paint (§4.3 lightweight) — enables per-citizen car colors + faction vehicles without touching the pose path.
4. Door/wheel articulation → `vehicle` editor submodule + `.groups` export + vehicle pose mode in the kernel → `useComputePose` flips on automatically once `.groups` exists.
5. Riding: `seat_*`/`entry_*` attach points are the "Vinny uses the car" contract — the ped agent path-finds to `entry_*`, welds to `seat_*`, door animates via gid 5/6.

---

## Revision History

| Date | Change |
|---|---|
| Oct 5, 2026 | Created — after `_SharedRestBuffer` fix made multi-instance vehicles render; captures the offset gotcha, the static-vs-posed decision rule, articulation + paint paths, and the Vehicles editor submodule plan |
