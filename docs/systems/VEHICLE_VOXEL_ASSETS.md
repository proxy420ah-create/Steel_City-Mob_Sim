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
| V3 | ~~Vehicles were registered under `voxel_buildings`.~~ `VoxelVehicle` now probes `voxel_vehicles/` first and falls back to `voxel_buildings/` — editor exports land in `voxel_vehicles/`, legacy assets keep working. | ✅ FIXED |
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

## 5. Vehicles Editor Submodule ✅ IMPLEMENTED

`voxel_editor.html` has a fourth asset type. It reuses the three generic paint layers — `groupMap` (gid articulation), `clothingRegionMap` (region tags), `itemPartMap` (named-point centroids) — with vehicle def sets swapped in via `activeBodyGroups()` / `activeClothingRegions()` / `activePartGroups()`:

```
vehicle: { label: 'Vehicle', icon: '🚗', voxelSize: 0.01,   // uniform lattice — same as authored+rendered char scale
           defaultDims: [130, 95, 280],                    // ≈1.95 × 1.4 × 4.2 m volume
           defaultModel: null }
```

**Tabs under vehicle**: "Body Parts" → **Car Parts** (`VEHICLE_GROUPS` — gid 0 Body, 1–4 wheels FL/FR/RL/RR, 5–6 doors L/R, 7 hood, 8 trunk; the .groups export feeds the future pose kernel); "Wardrobe" → **Paint / Decal** (`VEHICLE_REGIONS` — 0 Body Paint *remappable per-instance*, 1 Accent, 2 Chrome/Trim, 3 Glass, 4 Interior, 5 Tires, 6 Lights, 7 Decal); "Attach Pts" → `VEHICLE_PART_GROUPS` (`axle_*`/`door_hinge_*`/`hood_hinge`/`trunk_hinge` pivots + `seat_*`/`entry_*` boarding contract + `exhaust_tip`). Character-only blocks (anatomy auto-assign, arm pose test, wardrobe presets/base-template/strip) hide for vehicles.

**Export**: `format: steelcity_vehicle`, `assetType: "vehicle"`, folder hint `voxel_vehicles/` (V3 resolved — loader probes it first). Vehicle materials = palette ids **140–147** (`MaterialCount` bumped to 148); id 140 "Vehicle Paint" is the per-instance remap contract.

**Import to Unity**: `Tools → Voxel Import → Vehicle` — writes a **v2 .stasset** (attachments embedded in the SKEL tail), copies the source JSON beside it, and skips `.groups` (rigid body — a sidecar would fire the character pose kernel). CLI equivalent: `python Tools/json_to_stasset.py <json> <stasset>`.

**Handedness (mirror)**: the editor (right-handed) and Unity (left-handed) read the same voxel indices, so Unity draws every asset as a mirror image of the editor view. Characters patch this with promotion (L/R labels + params); vehicles and buildings have nothing to promote — the geometry itself lands mirrored. The 490 puts `seat_driver` at x=19 and `exhaust_tip` at x=40: Unity draws that as left-hand drive (correct for 1920), the unmirrored editor view draws right-hand drive. **Use the "Unity" view toggle** (checkbox in the left panel, `U` hotkey — auto-on for vehicle/building/prop asset types, off for characters) to see the model exactly as Unity renders it while editing; data is untouched, orbit and picking work normally. The animator's `U` key is a view-only mirror of the same idea.

**Mirror support**: `MIRROR_GID_PAIRS` still needs vehicle entries (wheel_l↔wheel_r, door_l↔door_r) or vehicles get literal-copy mirroring only. 📋 Open.

## 6. Authored Assets

### `vehicle_490_touring` — 1920 Chevrolet Series 490 Touring (Oct 6, 2026)

Procedurally generated by `tools/gen_vehicle_490.py` — edit parameters there or
round-trip `VoxelAssetStudio/JSON Models In Progress/vehicle_490_touring.json` in the Vehicle editor.
The generator emits three outputs: the binary `.stasset` (runtime geometry) and
**two JSON copies** — WIP in `JSON Models In Progress/`, final in
`StreamingAssets/voxel_vehicles/` (mirrors `voxel_characters/Civilian1.json`:
final assets are inspectable + metadata-readable in StreamingAssets).

**Character-relative sizing + 1:1 voxel parity**: characters render at
characters author AND render at 0.01 (~0.62 m for Civilian1's 62-voxel height —
confirmed in-scene), so the car is BOTH proportioned to the character AND
authored on the same 0.01 lattice — one vehicle voxel ≡ one character voxel
in-world, and the editor's char reference renders exactly 1:1 (s = 0.01/0.01).

| Spec | Voxels (0.01m) | Meters |
|---|---|---|
| Fill bbox W×H×L | 66×72×150 | 0.66 × 0.72 × 1.50 |
| Grid dims | 70×75×155 | — |
| Filled | ~79k | — |
| Wheels | r14 disc, axle y=14 | 0.28 m dia |
| Wheelbase | z 34 → 132 | 0.98 m |
| Cabin | z 42–100, beltline y=46 | ~0.58 m open tub |
| Seats | cushions y≈26, backs to y=48 | pelvis anchor y≈28 |
| Canvas top | y ~62–71 | interior head ~0.66 m |

Axes: +Z forward (grille high-z), +Y up, ground = y 0. LHD — driver at low-x.

**Proportion contract** (matched to period photos, verified in-editor vs the
char reference): a standing character's head sits at the windshield top edge
(~62-64), beltline ~46 = chest height, wheel tops ~knee. Occupants sit *deep*
in the tub — a full torso shows above the beltline, exactly like period
touring-car drivers.

**Seating math** (no sit pose exists yet — provisional): seat attach points are
pelvis anchors just above cushion top (`seat_driver`/`seat_passenger`, y=28).
Cushion y≈26, footwell floor 18. A seated Civilian1 (62 vox — same lattice)
puts head ≈ y 64–68 — a full torso above beltline 46, under interior roof
~66-69 (tight but valid — roof bows crown at cabin edges). Revisit once a
`sit` pose exists; the seat point is the contract, not the pose.

**Attachment points** (embedded in the v2 `.stasset`, also painted as fiducial
blobs in the JSON): `axle_fl/fr/rl/rr`, `door_hinge_l/r`, `hood_hinge`,
`seat_driver/passenger`, `entry_l/r` (running boards), `exhaust_tip`.

**Groups painted**: wheels gid 1–4, doors 5/6, hood 7 — but **no `.groups`
sidecar is shipped** (it would force the char pose kernel; see §4.2). Groups
live in the JSON until a vehicle pose mode exists.

**Status: wired as the default traffic asset** — `VoxelVehicle.assetFileName`/
`voxelSize`, `VehicleTestSpawner.vehicleAsset`/`vehicleVoxelSize`, and
`CityMap3D.carHalfWidth` (0.35 — the car is ~0.66 m wide) all point at the 490.
`ParkingMap.Build` slot pitch sized for the 1.5 m length (carLength 1.7).
Verified in playtest (Oct 6): correct scale vs characters, parked + patrolling
traffic on the HQ-front street, proxy-yaw fix holding on N-S links.

**stasset_io fix**: `save_stasset`/`load_stasset_full` previously dropped
attachments-only skeleton blocks (checked bones/joints only) — now treats
`attachments`/`materials`/`ams` as v2-worthy content.

## 7. Sequencing

1. ✅ Static shared-buffer instancing works for N rigid cars (`_SharedRestBuffer` fix).
2. ✅ Pitfall #8 confirmed + fixed — proxy bounds now bound the yawed footprint (`INSTANCED_PROXY_YAW_CLIP.md`); Amsterdam cars clipped at ±90° yaw.
3. Next feature gate: paint (§4.3 lightweight) — enables per-citizen car colors + faction vehicles without touching the pose path.
4. Door/wheel articulation → `vehicle` editor submodule + `.groups` export + vehicle pose mode in the kernel → `useComputePose` flips on automatically once `.groups` exists.
5. Riding: `seat_*`/`entry_*` attach points are the "Vinny uses the car" contract — the ped agent path-finds to `entry_*`, welds to `seat_*`, door animates via gid 5/6.

---

## Revision History

| Date | Change |
|---|---|
| Oct 5, 2026 | Created — after `_SharedRestBuffer` fix made multi-instance vehicles render; captures the offset gotcha, the static-vs-posed decision rule, articulation + paint paths, and the Vehicles editor submodule plan |
| Oct 6, 2026 | Vehicles editor submodule shipped in `voxel_editor.html` — `vehicle` asset type (0.01m char parity), Car Parts / Paint-Decal / Attach Pts tabs via `active*()` def switching, `steelcity_vehicle` format → `voxel_vehicles/` (loader probes it first — V3 fixed), palette ids 140–147 with remappable Vehicle Paint at 140 |
| Oct 6, 2026 | `vehicle_490_touring` authored via `tools/gen_vehicle_490.py` (~79k voxels, 0.66×0.72×1.50m on the **uniform 0.01 lattice** — 1:1 with character voxels; seat/entry/axle attach points embedded v2); `stasset_io` fixed to keep attachments-only v2 blocks; Runtime flipped to the 490 (`VehicleTestSpawner`/`VoxelVehicle` @0.01 + `carHalfWidth` 0.35 + parking slot pitch 1.7 m). Convention settled empirically: **all voxel assets = 0.01** — chars render at authored scale; the earlier 0.015 runtime assumption was wrong (`characterVoxelSize` scene knob also corrected to 0.01) — pending Unity playtest |
