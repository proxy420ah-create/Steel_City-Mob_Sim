# Recent Changes — Steel City: Mob Sim

**Last Updated**: October 5, 2026 (two-car traffic + slot reservation)

---

## October 5, 2026 — Second Vehicle + Parking-Slot Reservation

### Changes
- **Second vehicle, opposite curb (NEW)** — `vehicleCount` default is now 2. Car 0 spawns parked in the nearest free slot to HQ as before; every subsequent car uses the new `ParkingMap.NearestFreeOpposite(pos, reference)` — slots on the **mirror link** (`fromId↔toId` swapped = same street, far curb) win; other antiparallel-heading slots are a 4×-penalized fallback so the car never lands on a parallel street blocks away. Both cars take the same F10 commands, rolling-route random walk, lane polylines, and debug beams via `activeVehicles`. Spawn now logs `Car N slot: from→to @ pos` (Editor.log confirmed mirrored links `i_r3_c5→i_r2_c5` / `i_r2_c5→i_r3_c5` at x = ±2.0).
- **Second car invisible — static instanced assets read past the shared buffer (FIXED)** — `VoxelProxyRaymarch` vertex stage computed the per-instance voxel offset as `_GroupIDsEnabled == 0 ? instanceID * totalVoxels : 0`, i.e. it used `_GroupIDsEnabled == 0` to mean "bound buffer is the per-instance *posed* buffer". Assets with no `.groups` file (vehicles) also get `_GroupIDsEnabled = 0` but are bound to the single shared rest buffer (9,600 voxels), so instance 0 read offset 0 (rendered) while instance 1 read offset 9,600 = one past the end → out-of-bounds read = empty → car never drew (GameObject, slot, and registration were all correct). New `_SharedRestBuffer` uniform (set by `RenderInstancedGroup` as `!useComputePose`, default 0 = legacy behavior so chunk/sector/building blocks are untouched) forces offset 0 when the shared rest buffer is bound. Affects every instanced asset without groupIDs, not just vehicles.
- **Parking-slot reservation (FIXED)** — `PickParkTarget` now `Occupy()`s the chosen slot immediately, so `NearestFreeAhead` can never hand the same slot to two cars approaching simultaneously (previously occupancy was only set on arrival — a true double-booking race). The reservation is released on every abandon path: `RequestDepart` mid-seek cancel and the `TrySpliceParking` overshoot-repick.
- **Lane-bound parking confirmed** — slots were already restricted to the car's own side: `NearestFreeAhead` rejects any slot whose heading disagrees with travel direction (`Dot < 0.3`) or that isn't ahead of the car (`>1 m`). No U-turns across the street for parking, and no lane-changing exists anywhere — the only cross-lane motion is the pull-in/out diagonal.

### 🧪 TEST NOW
- **Shader changed** — let Unity recompile `VoxelProxyRaymarch` (watch the Console for shader errors on first Play).
- Play → **both** cars visible, parked across the street from each other near HQ, facing opposite directions, each on its own right curb. Vinny + hoodlums unchanged (they use the compute-pose path, `_SharedRestBuffer = 0`).
- Unverified watch item: `RenderInstancedGroup`'s proxy cube is axis-aligned and ignores yaw. For the non-cubic 20×16×30 car, a 90° heading (east-west streets) would crop nose/tail — if a car looks sliced on an E-W street, that's the proxy extents, not the shader offset.
- F10 → both pull out on opposite-side diagonals and cruise in opposite lanes; on a straight street they pass each other correctly (right-hand traffic).
- F10 again → both seek slots on *their* side only; if both converge on one slot the second car picks the next free one instead of stacking.

### Docs
- New: `docs/systems/VEHICLE_VOXEL_ASSETS.md` — vehicle render path, three buffer modes + `_SharedRestBuffer` truth table, V1–V6 gotcha catalog, articulation/paint decision rule, and the planned **`vehicle` asset-type submodule** for `voxel_editor.html` (wheel/door gids, axle/hinge pivots, seat/entry attach points, `.vehicle.json` contract).
- `INSTANCED_RENDERING_PITFALLS.md` — added Pitfall #7 (groupID-less instances read OOB → invisible) + Pitfall #8 (axis-aligned proxy ignores yaw).
- Bug entry: `docs/known_issues/rendering/STATIC_INSTANCED_OOB.md` (🟢 FIXED) + README row; index entry in `docs/core/DOCUMENTATION_INDEX.md`.

---

## October 5, 2026 — Character Asset Lifecycle (Atomic Refactor)

### Changes
- **CharacterAssets registry (NEW)** — `Sim/CharacterAssets.cs`: single cached parse of each `.character.json` feeding every consumer — voxels, dims, groupIDs, regions, attachment points, pivots (painted `pivot_N` overrides applied + canonical fallback), joint offsets, and a **defaults-filled `AnimParamsData`** produced by `VoxelCharacterAnimator.LoadFromAnimJson`. Kills the five-independent-parses divergence (each used to extract a different subset and fail alone). `CreateAnimator()` returns a per-character animator over the shared table so `ikOverrides`/`ikBlend` stay per-instance. `GpuUploaded` flag makes per-asset GPU uploads first-writer-wins.
- **GPU/CPU pose parity (FIXED)** — `VoxelCharacter.LoadAndApplyAnimParams` now emits `kfs[]`/`jc[]`/`asp[]` **directly from `asset.ParamsData`** instead of a second hardcoded fallback table. The stale table had pre-correction constants (aim raised the LEFT arm, `legStride` +1/+1, `torsoTwist` +0.2) while the animator's defaults carried the corrected right-arm/−1-stride convention — on a trimmed file every default fired, so the rendered arm diverged from the weld solve. One table now feeds both authorities.
- **VoxelCharacter.WhenReady (NEW)** — explicit init-completion callback replaces `WeaponMount`'s per-frame `IsInitialized` coroutine poll. `WhenReady` fires immediately when already initialized, else queues to the end of `Start` — equip ordering is deterministic regardless of component add order.
- **Pose-override channel (NEW)** — `CharacterAnimation` gains a priority arbitration lane: `RequestPose(state, priority, duration)` (PRIORITY_BEHAVIOR=10, COMBAT=30, DEBUG=50) holds a pose over the locomotion base lane; `ReleasePose(priority)` releases; timed overrides auto-expire. `SetState` remains the base-lane write. `autoDetectWalking` suspends while an override is held — the "Aim resets to Idle" stomp path is gone. The instanced handle is re-fetched every frame (a stale handle previously kept writing to a dead instance).
- **CharacterRig hotkeys → DEBUG priority** — T/I/W/L/A/C poses now hold until changed; `I` explicitly releases the override + base-idles.
- **PedestrianLookAround → timed BEHAVIOR requests** — `LookAround`/`CoastClearCheck` request timed overrides instead of `SetState` + coroutine restore; can't stomp a higher-priority pose and never leaves a stale `isLooking` flag.
- **WeaponMount/ClothingSystem/VoxelChunkManager → shared asset** — weapon attach data (attach points, pivots, `CreateAnimator`), clothing regions, and the shared instanced voxel buffer all read the registry instead of re-reading the file. Right-hand weld resolves `right_hand` → gid 9 chain (`pivot_9`→`pivot_3`) from the same pivots the GPU uses.
- **Dead code removed** — `VoxelCharacter`'s duplicate `AnimParamsData`/`AnimParamsJson`/`WalkKFPose`/etc. class tree and its private `ParsePivotsManual`/`ParseJointOffsetsManual`/`ExtractPivotsRaw` (superset definitions live in `VoxelCharacterAnimator`).
- **Null `WalkKFPose` NRE → frozen weapon weld (FIXED)** — `GetWalkKfPose` could return null for `kf0`/`kf1` (and for `kf2`/`kf3` via the `?? autoMirror` chain when the mirror source was null): JsonUtility materializes a **non-null `WalkKeyframesData` with null `kf*` fields** for a partial `"walkKeyframes"` section, so the `== null` default-fill never fired. `GetKFPoseValue` then dereferenced null every frame inside `WeaponMount.LateUpdate` — the weld transform was never written (weapon frozen at spawn, read as "T-pose anchor on the wrong hand") and per-frame exception spam wrecked the visible walk. Three layers: `LoadFromAnimJson` now fills defaults when `walkKeyframes` is absent **or** has no usable `kf0`/`kf1` (partial sections get per-field fills); `GetWalkKfPose` never returns null (shared `ZERO_WALK_POSE` + warn-once naming the missing index); `GetKFPoseValue` null-guards as last resort.
- **Stale `GpuUploaded` flag → T-pose + dead walk after group rebuild (FIXED)** — the per-asset `GpuUploaded` flag lived on the static `CharacterAssets` cache, which outlives `VoxelChunkManager`'s instanced groups (`ReleaseAllInstancedGroups` on scene reload/rebuild). The new group never received pivots, `jc[]` (carries `restPose` arm-drop), walk keyframes or static params → arms stayed in the authored T-pose and the shader fell back to its sin() walk. Flag removed; `VoxelCharacter` now gates uploads on `VoxelChunkManager.HasAnimUploads(assetFileName)` (live group state).
- **Default-fill missed zero-filled sections → permanent T-pose (FIXED)** — `LoadFromAnimJson` decided "section missing" with `== null`, but JsonUtility can return non-null zero-filled objects for absent sections: `restPose` 0/0 (arms never drop on CPU weld AND GPU), zero joint signs (frozen limbs). Missing-ness is now decided from the raw JSON text (`Missing(key)`), null is only a backstop. New log `[VoxelCharacter] GPU params …` prints restPose/signs/aim arms at upload.
- **Handedness reconciliation (NEW)** — root cause confirmed from data: models face +Z (hand thumbs + Face slab at z50–51); editor is right-handed (right = −X, gid 9), Unity is left-handed (right = +X, gid 8); no X reflection exists in the shaders. `tools/promote_character_to_unity.py` flips once at promotion (attachment L/R names; aiming/crouching/rest L/R swap with Y-rotation negation; materializes the editor-default aiming table; `"handedness": "unity"` stamp; aborts on double flip; `--check` mode). Runtime `Civilian1.json` regenerated from `Civilian1JointTestFINAL.json` with it (crouching L/R now mirrored too). `CharacterAssets` logs a warning if `right_hand` is on the −X side. `character_animator.html`: **U** toggles a mirrored Unity-handedness preview (picking mirrored to match). Docs: lifecycle doc G8 corrected, G9/G10 added.
- **Reference walk-path tool (voxel_editor)** — the 🧍 Reference Preview panel gains a waypoint system so scale checks work on authored terrain (e.g. a reference walking the river promenade): **🚩 Path** toggles draw mode (click surface voxels to drop waypoints — they stand in the air cell above; click near one to remove it), **🧹 Clear** resets, cyan pins + loop line mark the path. **Follow path (walk)** checkbox: while playing, the reference walks the waypoint loop at 1.4 m/s (real-world — scaled by the editable voxel size), yawed to face travel direction (models face +Z); off/unchecked = in-place walk at the spawn offset. Path state lives in `charPath`/`charPathDist`; `sampleCharPath` does distance-parameterized loop sampling; `surfaceYAt` finds the top voxel in a column. Waypoint clicks are swallowed so voxel tools never fire (`pathDrawMode` guard in mousedown/mouseup). Also fixes the reported spawn bug: `charOffsetY` now exists and Re-center surface-snaps the reference onto terrain when the column has voxels. Path clears on New environment.
- **Character animator perf port** — `character_animator.html` gets the same fix set, adapted: (1) grow-only `InstancedMesh` reuse for BOTH `rebuildMesh` and `rebuildAnimatedMesh` — the latter runs **every frame while playing** and previously disposed + reallocated + re-uploaded two GPU buffers per frame; (2) `getMatColor` cached `Map` (`allMaterials` is `const` here → build-once, no invalidation needed); (3) `raycastVoxel` → Amanatides–Woo DDA through `voxelMap` for the static view (hover status runs it per mousemove, previously an instanced raycast + full `getVoxelList()` scan), keeping instance picking via `raycastVoxelMesh` when `useAnimatedMesh` is on since posed voxels leave their grid cells; (4) same `EDGE_VOXEL_LIMIT=60000` wireframe cap on both meshes. No `scheduleRebuild` — the animator has no drag-editing (`currentTool` locked to `'camera'`). Untested — verify pose playback smoothness + hover coordinates.
- **Voxel editor perf pass for large models (e.g. 184k-voxel river tile)** — `voxel_editor.html`: (1) `scheduleRebuild()` coalesces mesh rebuilds to 1/rAF-frame — drag strokes rebuilt per painted voxel before; (2) `getMatColor` is now a cached `Map` (was `allMaterials.find()` linear scan per voxel per rebuild), invalidated in `ensureMatVisibility`; (3) `rebuildMesh` reuses grow-only `InstancedMesh` buffers (`voxelMeshCap`/`edgesMeshCap`, `count=` subset) instead of dispose+realloc+re-upload per call; (4) per-voxel wireframe skipped above `EDGE_VOXEL_LIMIT=60000` (184k voxels ≈ 2.2M line segments); (5) `raycastVoxel` is now an Amanatides–Woo DDA through `voxelMap` honoring the same visibility filters (`isVoxelShown`, shared with `getVisibleVoxels`) — replaces `InstancedMesh.raycast` (~200k ray-box tests per mousemove) and removes a second `getVisibleVoxels()` scan per raycast; `raycastVoxelMesh` keeps instance picking for `testArmPose` where rendered voxels are displaced off-grid. Untested — verify paint/place/erase + hover accuracy on the river tile.
- **M2 spec: river + bridge tiles + prototype river tile** — new `docs/systems/RIVER_AND_BRIDGE_TILES.md`: smart-foundation design (carve depth only at the channel — land tiles stay 2-voxel pancake; grade unchanged at y=0; docks/boats enabled later via negative worldOffset.y), neighbor-mask tile recipes (water–water seams carve to continuous channel; water–land seams stay riverside roads), 4 authored tiles cover all straight/corner/cap cases, `MAT_WATER=137` (requires MaterialCount/MaxMaterials ≥138 — buffer was sized 130). Prototype `Tools/generate_river_tile.py` → `JSON Models In Progress/river_straight.json` (116×24×116 @ 0.1, 183,744 voxels, 1.4m-deep channel + quay walls + flanking sidewalks) — loadable in `voxel_editor.html` as the visual spec for the runtime recipe.
- **O5: sector LOD + front-to-back sort + sector culling enabled** — `RenderBakedSectors` now sorts visible sectors nearest-first (`sectorDrawList`, distance-to-AABB) so early-Z rejects occluded fragments (shader writes `SV_Depth`/`ZWrite On`), and applies the chunk path's screen-ratio LOD tiers per-sector via MPB (`_MaxSteps`, `_CheapShading`, `_UnlitLod`, `_LodDebugEnabled/Color`). Sector distance-culling is now `!isOrtho`-gated — `DistanceToAABB` included the camera's vertical gap, which would cull sectors directly under the top-down ortho camera (likely why `disableSectorCulling` defaulted true; now flipped to false — inspector value may still be serialized on). NOTE: `_MaxSteps` floors at each instance's cell-crossing bound in-shader, so LOD wins are cheap/unlit shading (no shadow march/normal blend), not fewer march steps. Untested in Unity.
- **O3: empty-lot debris pooling — ~1.2 GB building data → ~135 MB** — `ProceduralDebrisScatterer` now owns a pooled variant cache: `NumVariants = 16`, `GetVariantIndex(row,col,subIndex)` hash-maps each lot to a variant, `GetVariantData` scatters each variant once into a shared read-only array (`Enabled` became a property that clears the pool on toggle). `SectorBaker.BakeSector` aliases `buildingMeta[i].x` for lots sharing a variant — the merged sector buffer stores each variant once instead of all ~144 lots. Per-chunk path (`CityMap3D` 3 load sites + `RebakeEmptyPlotChunks`) uses new `VoxelChunkManager.LoadChunkCenteredShared` on pooled arrays via `LoadEmptyLotChunk`. Scatter calls per build: 9,189 → ≤16. Expected: PHASE 2B faster and less variable (was 3.4–7.3 s — GC churn from ~1.2 GB of clones), buildings visually identical per variant. Untested in Unity.
- **Docs: city scale architecture captured** — new `docs/systems/CITY_SCALE_ARCHITECTURE.md` (findings, sector invariants, measured timings/memory, optimization backlog O1–O9, milestone plan M0–M4). Cross-referenced from `GPU_DRIVEN_SECTOR_RENDERING.md` (gap #3 update), `INSTANCED_RENDERING_PITFALLS.md` (new pitfall #6), `CITY_LAYOUT_PIPELINE.md` (scale status note), and `DOCUMENTATION_INDEX.md`. Terrain-sector bug marked 🟢 FIXED (confirmed in playtest: 9 terrain sectors, whole map has roads; 32x32 build 14.0 s vs 1.1 s at 10x10). No code changes in this entry.
- **32x32: half the terrain (roads/sidewalks) missing — terrain now banded into multiple sectors** — `CityMap3D.BuildVoxelTerrain` baked all 1024 terrain chunks into ONE sector (143M voxels, 1024 instances), exceeding the `DrawMeshInstanced` 511-instance cap (≈50% of 1024), float-exact offsets (<2^24 voxels) and the 2^27 D3D11 buffer-element limit; failure was silent (no log errors). Now row-major chunks are banded into `terrain_sector_N` sectors of ≤256 chunks / ≤2^24-1 voxels (~9 sectors at 32x32; 10x10 still 1). Collision registration unchanged. Log line now reads `Terrain: N chunks baked into M sector(s)`. Root cause is a strong hypothesis from the instance-cap doc + arithmetic, not yet seen on GPU — confirm in playtest. Bug file: `docs/known_issues/rendering/TERRAIN_SECTOR_OVERFLOW_AT_SCALE.md`. Measured 32x32 build (sector-baked buildings, already on in the scene): 12.8–20.3 s total, terrain 9–12.7 s (collision regrow is the bulk of 1B), building bake 3.6–7.3 s.
- **32x32 city scale test — regenerated fresh (M0)** — new `Tools/generate_city_layout.py` scales the 10x10 test rig (`city_layout_10x10_backup.json` / `city_template_10x10_backup.json` = source of truth) to NxN: 9 empty_land slots/block, player HQ + rival HQ (tenement) + shooting range keep their relative map position (edge-preserving: `round(p*(N-1)/9)`), N/S/W/E Quarter + Central Block naming bands. `--activate` copies the pair to the active `city_layout.json`/`city_template.json` (both must change together); `--restore` re-activates the 10x10. Replaces an earlier uncommitted attempt whose output had drifted from the schema (`building_types` dict→list, template `grid` `{rows,cols}`→`2`). Result: 1024 blocks / 9192 slots (9189 empty_land, 2 tenement, 1 range), ~422 m across (spacing 13.2 m). **Not yet run in Unity.** Predicted scale pressure points (static read, unmeasured): ~1.2 GB VRAM for per-lot empty_land buffers (each lot has unique debris scatter → own 131 KB `ComputeBuffer` + GameObject), ~571 MB terrain (CPU chunk arrays + merged array + GPU buffer), quadratic `VoxelCollisionWorld.RegisterTerrainChunk` regrow, per-visible-chunk allocations/draws in the proxy cull loop. Measure via `buildmap_log.txt` + Profiler before optimizing.
- **Wardrobe-region select (`W` modifier) + 'Unassigned' region (voxel_editor)** — hold **W**+click in the Select tool to pick every voxel sharing the hovered voxel's clothing region, volume-wide (live preview + `Alt+W` additive, same pattern as `Q` group-select). Untagged voxels form an 'Unassigned' pseudo-region — select them, then `C` tool + a region click stamps them (or `B` to repaint material). Region picker gains an **Unassigned** entry that erases tags (sentinel `-1`, never serialized — `clothingRegionMap` deletes instead), and the counts panel shows an untagged total. `stampActiveTag`, selection-stamp, single-voxel, and mirror write paths all honor the erase sentinel.
- **Aim Sweep animation (state 10) added** — ported from `character_animator.html` state 10: aiming pose + sinusoidal shoulder-yaw (`sin(animTime·freq)·amp`) applied to the **armed** arm's shoulder reach (the arm with non-zero aim swing), head tracks at `headFollow`. `AimSweepData` (amp 0.55, freq 1.6, headFollow 0.6) joins `AnimParamsData` with default fill; `AnimState.AimSweep = 10`; GPU buffer `asp[]` grows 12→13 (`asp[12]` = sweep params); both `CharacterPoseCompute.compute` and `VoxelProxyRaymarch.shader` treat state 10 as aiming + sweep (and the compute T-pose early-out is now bounded `<9.5` so 10 isn't swallowed). Hotkey: **S** on controllable rigs; added to HoodSpawner/CityMap3D debug state cycles. Weapon weld follows the sweep via the shared `EffectiveState → PosePoint` path.
- **Weapon 180° roll on Unity-handed rigs (FIXED)** — after the L/R flip the Model 10 sat in the right hand but rolled 180° about the bore. Item `attachRotation` is authored for the editor's right hand; on a promoted rig the Unity right hand is the mirrored side. `CharacterAsset.UnityHanded` (from the `"handedness":"unity"` stamp) → `WeaponMount.mirroredRig`; the existing left-hand +180° X roll now applies when `(hand == Left) != mirroredRig`. Data-driven by the stamp — no weapon JSON edits, other weapons inherit it.
- **Unity right-hand = gid 8 side (runtime `Civilian1.json`)** — superseded by the promotion script above: — Unity renders gid 3/9 (x10-23) on the character's anatomical LEFT, so the editor-convention data put the gun in the left hand and raised the left arm on Aim. Runtime asset now: `right_hand` → (82.5,46.5,46.5) gid 8, `left_hand` → gid 9, and `animParams.aiming` explicitly left-armed in voxel-gid terms (`armSwingL=-1.4`, `elbowBendL=0.3`, `torsoTwist=+0.2`) — i.e. the 2f7e8a9 convention. The source `JSON Models In Progress` file keeps editor convention: re-promoting it must re-apply this swap (or fix the editor/Unity X mirror at the root).
- **Archive `.anim.json` audit** — `ARCHIVE/voxel_characters/Vinny.anim.json` + `character_hoodlum_0.anim.json` carry the full authored params and are byte-identical; their `walkKeyframes`/`walk`/`restPose`/joint sections match the animator's defaults exactly (the defaults were extracted from them — no merge needed for gait). **Do not re-import `aiming`** (stale left-armed table: `armSwingL=-1.4`, `torsoTwist=+0.2`) **or `jointOffset`** (gid2/3 ±3 already baked into Civilian1 geometry).

- **M2 runtime: `terrain` field + `VoxelTerrainBuilder` recipes (IMPLEMENTED, untested in Unity)** — `city_layout.json` blocks may now carry `"terrain": "land"|"water"|"bridge"|"mainstreet"|"oob"` (absent = land) plus `hSeamRows`/`vSeamRows` seam grids (row-wrapped `{cells:[…]}` because JsonUtility can't parse raw nested arrays). `CityMap3D` passes a `(row,col)→terrain` map + seam grids into `VoxelTerrainBuilder.GeneratePerBlockTerrain` (new params; null = legacy all-land behavior). Builder: per-chunk recipe dispatch — `land`/`oob`/`mainstreet`/`water`/`bridge`; water & bridge chunks extend below grade (`h` 2→48 @0.05: bed 0.2 m, water 1.4 m, air 0.3 m, grade slab, parapet 0.4 m; `worldOrigin.y` goes negative, anchors stay at grade). `FillChannelChunk` is axis-agnostic (mask E|W→flow X, else Z): corridor halves on channel-open edges continue the authored cross-section (promenade|parapet|gap|quay wall|channel — the "river seam"), lateral corridors stay riverside streets (asphalt+cobble), closed ends get a full-depth cap wall (map-edge termination only); `bridge` adds a grade-level asphalt deck over the channel + stone rails at deck edges over open water. `FillMainstreetTile`: curb ring + asphalt + cobble trolley-track pair along the mainst-neighbor axis. `FillOobTile`: flat stone, no sidewalk ring. Corridor fills read seam types ('mainstreet'→plain asphalt). Unsupported water masks (corner/T/cross) → land fill + one aggregated warning listing cells — corner recipe ports when the authored corner tile lands. Palette: `StAssetReader.MaterialCount` 130→140 (headroom over reserved gameplay block), `MobColors[137]` water #1A4073, `VoxelChunkManager.MaxMaterials` now references `MaterialCount` (drift-proof). `WaypointGraph.GenerateFromLayout` is terrain-conditioned: `oob` blocks emit no nodes (unwalkable); water blocks drop the two mids on the flow-axis channel mouths/cap walls, corners + promenade mids stay (corner links across water-water seams = promenade continuation; water-land seams keep mid crosswalks = riverside street); links onto/through bridge blocks get new `WaypointType.BridgeDeck` (chokepoint metadata); all link emission is node-existence-guarded. `generate_city_layout.py` gains `--replica` (stamps terrain from embedded REPLICA1_DATA: 642 land / 220 mainstreet / 34 water / 4 bridge / 124 oob; derives river/mainstreet seams; suppresses buildings+businesses off-land) and `--river-row R --bridge-cols …` test mode. `city_editor.html` export now emits `hSeamRows`/`vSeamRows` wrapped rows alongside raw `hSeams`/`vSeams`. Generated `city_layout_10_river.json` (10x10, river row 6, bridges c3/c7) + `city_layout_32_replica.json` (full replica w/ terrain). Replica mask census: straights 21, corners 9 (all 4 rots), map-edge ends 2, bridges 4 — **no caps/ponds/T/cross mid-city**. Playtested 10x10 straight-river: clean console, channel/promenades/parapets/bridges render, seams continuous, Vinny routed across the bridge deck (not through water). Replica 32x32 + corners still pending.
- **Waypoint lanes measured from terrain voxels (NEW)** — `WaypointGraph.GenerateFromLayout` gained an optional `VoxelCollisionWorld` param; per block edge, `MeasureSidewalkLane` scans a line perpendicular to the edge at grade height (+headroom probe so quay parapets break the run), finds the contiguous MAT_SIDEWALK band, and lanes the mid/corner nodes at its voxel-measured centroid instead of the assumed `halfTile-halfSidewalk`. Falls back to the formula when no band exists (channel mouths, oob). Same convention auto-handles promenades (0.4m lane) and bridge edges. All 3 call sites (StressTestSpawner, GameUIController x2) pass `cityMap.GetComponent<VoxelCollisionWorld>()`; null = old behavior. Reports of peds riding sidewalk inner edge: if the measured band equals the formula on land blocks, remaining suspect is the corner-anchored ped transform (VoxelCharacter.cs:15).
- **Empty-lot plots bled over sidewalks (FIXED)** — the sub-lot grid sized itself to 90% of the *tile* (`GroundTileSize*0.9`), so lot content reached ±5.22m while the sidewalk ring starts at 4.8m — debris/lot ground covered the outer ~0.4m of sidewalk on every multi-slot block. Now sized to the building plot: `subSize = (GroundTileSize - 2*sidewalkWidth)/cols`, extent ±4.8m = exactly the inner sidewalk edge. Real buildings in multi-lot blocks render ~8% smaller (3.2m vs 3.48m lot pitch) — correct by construction. Full-block placements unchanged. No asset edits needed. **Second site missed on first pass:** `SectorBaker.BakeSector` (the path the scene actually uses) carried its own copy of the 0.9 math — fixed identically. Measured: `empty_land.stasset` is 64x8x64 filled edge to edge (3.2 m), so three lots = 9.6 m = the plot; old pitch 3.48 m pushed outer lots to ±5.08 m, 0.28 m over the 4.8 m sidewalk edge. Tenement footprint is inset 0.8 m inside its plot (by design), which is why it reads as a wider sidewalk.
- **Road lanes & traffic design spec (NEW DOC)** — `docs/systems/ROAD_LANES_AND_TRAFFIC.md`: measured current state (car 1.0×0.8×1.5 m; `roadWidth` live-resize already exists via Map Editor slider → `RebuildCity()`), lane model (2-lane streets at ±roadWidth/4; 4-lane mainstreet through-cell spines with trolley track pair ±0.35 m + rail buffer ≈±0.6 m where cars may cross but not cruise/park; bridge decks 2-lane), graph extensions needed (terrain/seam conditioning — **today's RoadGraph has no terrain awareness and links across open river water**; directed links; link classes), Phase-A agent behaviors for the F10 two-car test (lane offset, intersection reservation, car following) vs Phase-B (turn arcs, speed classes, Dijkstra, trolley actor), acceptance criteria incl. bridge + mainstreet traversal and a no-car-in-river regression check. Implementation order recorded; road width → 3.0 m is the user's in-progress step 0.

- **ApplyCivilianOutfits MissingReferenceException on city rebuild (FIXED)** — coroutine's second wait-loop used `rig1?.Character?.GetComponent<ClothingSystem>()`; `?.` checks real null only, so when `RebuildCity()` (Road Width slider, etc.) destroyed the rigs mid-wait it called `GetComponent` on a destroyed `VoxelCharacter` and threw every frame until timeout. Now uses `== null` (Unity fake-null semantics) at the top of both wait loops and `yield break`s — the respawn path launches a fresh coroutine that re-dresses the new rigs.
- **Accordion ▶ glyph missing from font atlas (FIXED)** — LiberationSans SDF atlas contains `▼` (U+25BC) but not `▶` (U+25B6) → TMP warning spam on every Editor tab open (`SetArraySizes` via `ScrollRect.LateUpdate`). Collapsed arrow now `>` (guaranteed-baked ASCII) in `GameUIController` + `AccordionSection.UpdateArrow`. If triangle fidelity is wanted later, bake `▶` into the atlas or add a fallback font.

- **Road width hardcoded to 3.0 m** — `CityMap3D.roadWidth` is now `private const` (was serialized 1.6). Two 1.4 m lanes around the 1.0 m `vehicle_civilian_car_0` + the existing ~0.1 m cobble center stripe already renders dead-center of every corridor → it doubles as the dual-traffic lane divider; `VehicleAgent` lane offsets will hug either side. `SetRoadWidth` + the Map Editor Road Width slider removed — live-resize was RebuildCity-per-tick and destroyed spawned entities (the disappearing-car report). Scene YAML retains a stale `roadWidth:` key — ignored data, harmless.

- **RoadGraph terrain conditioning (step 1 of traffic spec — FIXED)** — `GenerateFromLayout` takes optional `hSeams`/`vSeams`; corridor links emit only when the seam is drivable (anything but `"river"`; `mainstreet`/`road`/null pass). E-W link → `hSeams[r-1][c]`, N-S link → `vSeams[r][c-1]`, out-of-range = perimeter road. `CityMap3D.ExtractTerrainAndSeams` is the new shared extractor (terrain build refactored onto it + `VehicleTestSpawner.BuildRoadGraph` now feeds the graph). Log reports `(N river corridors suppressed)`. Known consequence: cars can't cross the river yet — bridge decks are through-cell spines (step 4), not corridors. Bug entry `docs/known_issues/runtime/ROADGRAPH_IGNORES_TERRAIN.md` → FIXED pending playtest.

- **Lane offset driving (step 2 of traffic spec — slot cars on their lane)** — `VehicleAgent` computes `side = Cross(dir, up) * (roadWidth/4 = 0.75 m)` and drives `lerp + side`; opposing directions separate automatically, no directed links needed yet. Also fixed the car rendering half-a-volume off the path: `VoxelVehicle` is corner-anchored, so the agent now positions `center - rotation * halfFootprint` (same convention as `PlaceAtCenter`, rotation-compensated). `PathDebugRenderer.RegisterPath` gained optional `lateralOffset` — car beams shift right-of-direction per segment so the debug path sits ON the lane (jogs through intersections like a real lane). `VehicleTestSpawner` passes `cityMap.GetRoadWidth()*0.25` to both. **Two follow-up fixes after first on-screen check:** (a) car sat off the beam — the raymarch shader pivots at `volumeCenter = worldOffset + dims/2` (corner-anchored transform), so placement is `center - halfFootprint` with NO rotation term (an earlier `rotation*corner` version double-shifted under yaw); measured `vehicle_civilian_car_0` fills its volume edge-to-edge so no internal offset exists. (b) endpoint node markers drew at un-offset centerline positions while segments were lane-offset — markers now take their adjacent segment's side vector (outgoing for interior, incoming for the last node). (c) beams jogged but never CONNECTED through turns — consecutive segments end/start at node+side(dir) which differ at direction changes; a turn-connector segment now bridges incoming-lane-end to outgoing-lane-start (skipped when directions match). Remaining cosmetic: car route is a random walk — it can re-cross an intersection, and stacked legs of the same plan will visually overlap (correct by design). **(d) handedness fix:** the offset used Cross(dir, up) — that is LEFT-of-travel in Unity; cars were driving on the left (UK-style). All three sites now use Cross(up, dir) = right-of-travel — 1920s US right-hand traffic. **(e) turns are real now:** the agent used to lerp node→node on centerline and teleport across the turn jog — the connector the beam drew was display-only. `PickNextTarget` now works in lane space: each leg enqueues its lane-offset endpoint, and at a turn the lane-start (which differs from where the car actually stopped) enqueues first as a real short leg — the car physically drives the connector, so the rendered beam == the driven path. Left turns cross the box diagonally; right turns take the tight corner jog; the slerp'd body rotation arcs through the short leg naturally. **(f) lane polyline = single source of truth + rolling route:** replaced the node-route+pendingLegs model with a lane-space polyline (`laneRoute`) that BOTH the agent and the beam consume — `RegisterPath` now gets the polyline (keys + resolver, lateralOffset=0), so displayed path == driven path literally, not approximately. Turn geometry is finalized lazily: each appended route node rewrites the previous tentative tail into real corner geometry, and the car only ever latches finalized points — the tentative tail is never a drive target (this is what kills the right-turn "mini triangle": for right turns the lane centerlines cross BEFORE the node at B+sideIn+sideOut, so the incoming leg ends at the corner and the car snaps 90 degrees — no overshoot, no connector). Left turns keep the 45 diagonal as TWO polyline points = its own segment + own waypoint, so it persists until the car actually enters it (previously it cleaned up with the straightaway). Route now rolls: `AdvanceTarget` extends whenever the finalized polyline runs out (dead ends fall back to U-turns), replacing plan-6/drain/replan — the beam stays continuously ~6 points ahead instead of vanishing between plans.


- **Flow fields (per-destination Dijkstra maps) — navigation infrastructure** — new `FlowField.cs`: one reverse-Dijkstra from a goal node fills `Next(nodeId)` for every reachable node, so agents need no baked path — they query the next hop per node (O(1)) and path cost scales with distinct destinations, not agents. Dual-graph from day one: `Build` overloads for `WaypointGraph` (cost=baseTickCost) and `RoadGraph` (cost=distance) — the same structure that later replaces `RandomNeighbor` in `VehicleAgent.ExtendRoute`. `Pathfinder` gained `GetFlowField(dest)` (lazy build+cache per destination node), `NextHopToward(from,dest)`, and `FindPathViaFlowField` — `FindPathBlockToBlock` now materializes through fields, so Vinny's order sim AND the stress-test walker queue both consume them with zero call-site changes. One-shot A* parity check per field logs cost match/divergence (`FlowField` fallback to A* on materialize failure). Stress HUD shows `FlowFields: N`. **Flow-field debug visual:** `PathDebugRenderer.showFlowField` + `SetDebugFlowField(field, resolver)` renders the whole Dijkstra map — a directional stub at every node pointing along its next hop, 4 color bands by remaining cost (green near-goal -> red far). `SimulationManager` feeds it on every path (target field outbound, HQ field homeward); `Pathfinder.LastGoalNodeId` exposes the current goal.


- **Curbside parking — computed slots + pull-out/pull-in + first destination-driven routing** — new `ParkingMap.cs`: every directed RoadLink yields right-side curb slots (2m keep-clear per intersection, 1.5+0.5m pitch, `parkOffset ~1.0m` hard at the curb vs lane 0.75m). `ParkingSpace{pos,lanePos,heading,fromId,toId,occupied}` + `NearestFree`/`NearestFreeAhead`/`Occupy`/`Release`. `VehicleAgent`: parked spawn (car starts in the slot nearest HQ, curb-aligned yaw); `RequestDepart` seeds pull-out legs (slot->lane merge 1.2m ahead->link lane end) INTO the lane polyline so the beam draws the maneuver; `RequestPark` builds a **RoadGraph flow field to the slot's entry node** (first real destination-driven vehicle routing — `ExtendRoute` follows `field.Next(tail)`, forces the slot link at the entry node) and `TrySpliceParking` retargets the leg that passes the slot's lanePos to a 1.2m lead-in + inserts the slot as the final polyline point — pull-in diagonal is driven and drawn. Arrival -> Occupy + squared-up parked pose. Spec: ROAD_LANES_AND_TRAFFIC.md parking section.

- **Pedestrian path-beam consumption fix + return-leg recolor** — `EventPlayer.visualPathIndex` reset once per `PathFound` event (was once per mission): the home leg's fresh `CurrentPath` previously inherited the outbound index, `remainingCount` went negative and the beam silently died for the whole walk home — and after the fix, the full-length return beam over the same streets read as "not consuming." New `PathDebugType.PedestrianReturn` (cyan) + `EventPlayer.OnPathFound` -> `GameUIController` re-registers the beam per leg; re-registration is scoped via `UnregisterPath(character.transform)` so car/trolley beams survive. Parking extras: `ParkingMap.SetExtraOffset` + `extraParkOffset` slider (0-1.5m) live-resnaps parked cars; `roadWidth` is a serialized inspector field on CityMap3D (applies on RebuildCity, not live); pull-in splices now trim the polyline tail so the car beam terminates at the slot and `awaitingParkArrival` guards `ExtendRoute` from eating the terminal slot point.

- **Sector frustum-cull false positive — terrain voids fixed + debug-render hardening pass** — block-scale black voids (roads/terrain gone, lots still rendering) traced to `TestPlanesAABB` wrongly culling the ~57x64m terrain-sector AABBs at oblique camera angles; `disableSectorCulling` now defaults true (with ~9 sectors culling bought nothing anyway; per-sector LOD + front-to-back sort unaffected). Bug filed: `docs/known_issues/rendering/SECTOR_FRUSTUM_CULL.md`. Debug renderer audit fixes: flow-field stubs now draw FIRST in the command buffer — the unlit transparent pass obeys painter's order so the field was previously painting OVER agent path beams (toggling "Show Flow Field" mid-play made Vinny's beam look dead); `MaxTypes` now enum-driven (PedestrianReturn was silently folding into bucket 0 = orange); all `DrawMeshInstanced` calls chunked to <=1023 via `DrawInstanced` (2048-sized buffers silently clipped past the D3D11 constant-buffer limit — same failure family as the terrain sector overflow). Editor.log forensics also surfaced a `WeaponMount.LateUpdate` -> `GetKFPoseValue` NRE storm worth a separate fix pass.

### Docs
- New: `docs/systems/ROAD_LANES_AND_TRAFFIC.md` — lane/traffic design + F10 two-car test plan.
- Bug entry: `docs/known_issues/runtime/ROADGRAPH_IGNORES_TERRAIN.md` (🔴 ACTIVE).
- New: `docs/systems/CHARACTER_ASSET_LIFECYCLE.md` — architecture + gotchas catalog G1–G8.
- Bug entry: `docs/known_issues/runtime/CHARACTER_LIFECYCLE_RACES.md`.

### 🧪 TEST NOW
- Spawn hoodlums → `Equipped SW_Model_10.json → Right hand (gid 9)` in console; guns welded to **right** hands at hip level in Idle (not T-pose, not left).
- Vinny rig: `T` `I` `W` `L` `A` `C` — every pose **holds**; `I` releases back to idle. No revert under auto-detect or look-around.
- `A` on a moving ped → stays aiming (auto-detect suspended under override).
- Trimmed-animParams file still: pivots upload, clothing applies, no separated arms.
- **Extortion-order walk**: Vinny walks with the keyframe gait (legs stride, arms swing ±0.3 rad, subtle body bob) — no frozen pose, no `NullReferenceException` spam, gun tracks the right hand through the stride.
- If a console warning `[VCA] walkKeyframes kfN is null` ever appears → that file's `animParams.walkKeyframes` exported partially — the zero-pose fallback keeps the weld alive, but re-export the section.

---

## October 6, 2026 — Transform/Tag-Layer Integrity

### Changes
- **⇄ Mirror Move for mirrored selections (NEW)** — nudge deltas were a single uniform `pasteOffset`, so moving a mirror-selected pair (both arms) translated both the same direction — pushing one arm out pushed the other arm *in*. New `⇄ Mirror Move` toggle in Paste Preview gives each clipboard cell its own accumulated offset (`ox/oy/oz`); cells past an active mirror plane (side tested on their *original* position, so mid-drag plane-crossings can't flip sign) get that axis's delta reflected — X+ spreads both arms outward, Y stays shared. Paste ghost and confirm use per-cell offsets; whole-group pivot translation is now per-gid (each side's pivot follows its own cells' delta; groups straddling the plane skip the shift).
- **Q+click selects whole body-part groups (NEW)** — the select tool's click was material-flood only, so grabbing a multi-material limb (arm + painted seam cells) meant additive-Alt clicks per island. Hold **Q** and click a solid voxel to select every occupied cell sharing that voxel's `groupMap` gid — works across materials and disconnected regions (`qHeld` tracked like `altHeld`; `G` was already bound to the bodypart tool). **Alt+Q+click** merges additional groups into the selection (multi-limb moves), Shift+click still forces single-voxel, Ctrl still box-selects; hover preview highlights the whole group with its name. Mirror-aware via `mirrorVoxelPositions`, untagged cells report "no body group" instead of selecting.
- **Line/Box anchor to hovered voxels in tag views (NEW)** — `anchorCell(hit, event)`: while a tag view is on, line/box endpoints anchor to the voxel under the cursor instead of the adjacent place-cell, so the shape runs through existing geometry and stamps it. Shift+click forces place-cell anchoring (extending into air still works). Preview ghosts use the same anchor so what you see is what gets stamped. Material view mode keeps the original place-cell behavior.
- **Geometry tools stamp the active tag view (NEW)** — while a tag view mode is on (Body Parts / Wardrobe / Attach Pts color buttons), every voxel written by a geometry tool — place, paint strokes + drag-paint, fill (replace/cavity/air), line, box, extrude/de-extrude, console `setVoxel` — also receives that panel's selected tag via `stampActiveTag` inside `setVoxel`. New geometry lands pre-tagged (draw a line extending the arm → cells arrive as Left Arm), and X-mirrored writes get the paired side's tag automatically. Selected `Body / Erase` (pid 0) clears part tags on written cells. View-button status lines now advertise the behavior; Material view mode disables stamping.
- **Side-aware mirror tagging (NEW)** — with mirror-X on, tag writes that cross the midline now carry the *other side's* tag instead of a literal copy: `Left Arm`→`Right Arm` (gid pairs 2↔3, 4↔5, 6↔7, 8↔9 via `MIRROR_GID_PAIRS`), `pivot_2`↔`pivot_3`/`pivot_4`↔`pivot_5`/etc. (mapped through the same gid table), `right_hand`↔`left_hand`, `right_shoulder`↔`left_shoulder`, `grip_right`↔`grip_left` — resolved by key convention so it works across character/item/building def tables. Y/Z-mirrored and midline cells keep the same value; unmatched keys (`muzzle`, `cheek`, buildings) self-map. Applied to bodypart single-cell + selection assigns, and to itempart (which previously had NO mirror support at all) — paint one armpit hinge, get both shoulders. Clothing regions unchanged (zones have no L/R pairs).
- **Selection Move carries ALL voxel layers (FIXED)** — moving a selected limb previously relocated only `voxelMap`, orphaning body-group, wardrobe-region, and attach-part tags (`pivot_N` clusters, `right_hand`, grip points) at the old coords — export then derived attachment points and pivots from the stale locations. `moveSelection()` clipboard entries now capture each cell's `gid`/`rid`/`pid`; `confirmPaste()` clears all layers at origins and restores them at destinations (a moved cell fully replaces the destination's layers). Copy/paste deliberately untouched — a pasted arm must not spawn a duplicate `pivot_N` cluster (two blobs corrupt the joint centroid).
- **Whole-group moves translate authored pivots** — when every voxel of a body group travels and no painted `pivot_N` cluster survives for that gid, `loadedAnimData.pivots[gid]` shifts by the normalized delta so the limb doesn't re-export rotating about its old joint. Partial moves leave the pivot (rigid-body correct). `snapshot()`/`applySnapshot` now include `loadedAnimData.pivots` so undo restores these too.
- **Same bug class fixed at every other transform/delete site** — single-voxel erase (`setVoxel` mid=0), erase-entire-selection, Delete Selection, clothing-preset `removedVoxels`, and volume-resize clamping all cleared tags per deleted cell; Expand Volume index-shift and Center Model now remap all four coord-keyed maps + translate authored pivots (expand re-normalizes across the new dims). `stripToBase` wiped `groupMap` with no restore — now restores base groups from new `baseTemplateGroupMap` (captured at base load) and clears `clothingRegionMap`. Bug entry: `docs/known_issues/editor/TRANSFORMS_ORPHAN_TAG_LAYERS.md`.

### 🧪 TEST NOW
- Editor → select a tagged limb (S tool) → **Move** → arrows/Enter → group colors, wardrobe region, `pivot_N` cluster, and hand tags all land on the moved limb; mirror re-poses around the moved joint; nothing left at the old coords. Undo restores everything.
- Erase or delete a tagged cell → no ghost tag. Expand/Center → all layers stay glued; export shows `groups`/`regions`/`itemParts`/`pivots` consistent.

---

## October 5, 2026 — Editor Bug Fixes

### Changes
- **Reference preview leak on asset-type switch (FIXED)** — switching the asset-type dropdown (e.g. Building → Character) reset the scene but left the previous session's reference model + any hand-attached item rendered over the fresh environment. The handler now tears down the full reference state: `charData`/`charPlaying`/`charArmed`/offsets cleared, `detachItem()` removes the welded attachment mesh, `rebuildCharMesh()` disposes the preview meshes, `char-info` + play/disarm button visuals reset. File-import paths keep the reference intentionally — it's still a valid scale comparison across model loads; only the explicit mode switch (the "fresh environment" action) clears it. Bug entry: `docs/known_issues/editor/REFERENCE_PREVIEW_SURVIVES_MODE_SWITCH.md`.
- **Reference Preview button needed two clicks to open (FIXED)** — the toggle read `panel.style.display` (inline style), which is `''` while the panel is hidden via stylesheet `display:none` — `'' !== 'none'` made the first click "close" an already-hidden panel. Now reads `getComputedStyle(panel).display` so the first click opens it. Audited every `style.display` toggle in the file — this was the only one with the inverted check.
- **Live mirror mode (NEW)** — `🔁 Mirror Editable` button in the Reference Preview panel makes the reference model pose the *editable* model itself: `charData` is re-parsed from `getEditorData()` (voxels, group gids, resolved pivots, animParams, attachment points) whenever edits land, so sculpting/tagging/pivot-painting appears on the animated pose in real time. `mirrorDirty` gates the re-parse (per-frame during play only after an edit; immediate rebuild when paused via a hook in `rebuildMesh`, before the empty-count early return). File loads disengage the mirror; asset-type switch clears it along with the rest of the preview state.
- **Skin blend ported to the reference path** — `poseVoxels` now applies the same shoulder blend as `posedPosEdit`/animator `posedPos`: `jgid`-tagged chain entries get a `bw` weight (`smoothstep(d/R)`), gid-0 cells near a shoulder get the capped bump injected leaf-most, and application lerps displacement (chord path). The mirror and file references now show the blend gradient during playback — same `blendEnabled`/`blendRadius`/`blendTorsoCap` globals drive everything.
- **Mirrored joint/attachment markers + view-mode parity** — when `🔁 Mirror Editable` is on, the posed copy now carries the same annotation layer and display state as the editable model: `pivot_N` clusters get per-cell wireframes + a centroid diamond (posed through the pivot's own gid chain — the split-across-the-seam behavior is preserved so group-boundary leaks are visible on the walking copy), other attach points (hands, grip, bore) get centroid spheres posed through their painted cells' majority gid, and the copy's colors follow the active view mode (Body Parts group colors / Wardrobe region colors / Attach Pts part colors / material). Groups hidden via the 👁 toggle stay hidden on the copy (zero-scale instances), matching `getVisibleVoxels` behavior in every view mode. New `buildMirrorMarkers()` + `charMarkerGroup` lifecycle in `rebuildCharMesh` — posed positions come from a `key → posedPos` map since `posed[i]` ↔ `charData.voxels[i]` share order.
- **Pistol preset now right-armed (Model 10 consistency)** — `aiming` defaults + `pistol` preset in both tools: `armSwingR: -1.4` (right arm raises), `elbowBendR: 0.3` (right elbow bends — signR=-1 mirrors the left's bend), `torsoTwist: -0.2` (blades left shoulder back). Aiming, Aim Walk, and Aim Sweep all now exercise the right arm — the same arm Unity's IK solver drives (gid 3/9) and the hand the Model 10 grips. `Civilian1.json` patched in place (surgical string swap — re-export would have round-tripped the stale block since `getEditorData` writes `loadedAnimData` verbatim): aiming values flipped to right-arm and `aimSweep` defaults added. Other files carrying old animParams keep left-arm aim until a preset re-applies.

- **animParams is an override layer, not a requirement** — every consumer fills missing sections with defaults (animator/editor merge, Unity's `p.X == null` fill). Only `groups` (gid tags) are required to animate. The animator's `saveProject` baked the ENTIRE merged param blob — including sections never touched — freezing default guesses into files permanently (root cause of the left-arm-aim drift).
- **Diff-on-save in animator** — `saveProject` now exports only `animParams` sections that differ from `DEFAULT_PARAMS` (`diffAnimParams` + `jsonEqual` helpers; `animParams` key omitted entirely when nothing is custom). Status line reports custom-section count. `exportAnimParams` (the standalone anim_params dump) intentionally keeps the full blob. Files now carry ONLY intentional per-character tuning → future default fixes propagate automatically.
- **Fixed stray `torsoTwist` defaults** — `DEFAULT_PARAMS.aiming.torsoTwist` was still `0.2` (left-armed blading) in both tools while the preset used `-0.2`; defaults corrected to `-0.2`.
- **Unity fallback corrected** — `VoxelCharacterAnimator.cs` aiming fill was still `armSwingL = -1.4f` (left-armed); now `armSwingR = -1.4f` / `elbowBendR = 0.3f` / `torsoTwist = -0.2f` matching the right-arm convention.
- **Civilian1.json trimmed** — 10 of 13 baked sections were byte-identical to defaults (restPose, walk, walkKeyframes, armSwing, legTwist, elbowBend, kneeBend, looking, aiming, aimSweep) → removed. Kept the 3 genuinely custom sections: `jointOffset` (gid2/3 ±6 X shoulder offsets), `legStride` (signL/R = -1), `crouching` (modelLower 8). Valid JSON, 12352 voxels intact.

- **LATENT UNITY CRASH FIX: missing-section fills completed** — `LoadFromAnimJson` only filled `restPose`/`looking`/`aiming`/`crouching`, but `ComputeGroupRotation` unconditionally dereferences `armSwing`, `legStride`, `legTwist`, `elbowBend`, `kneeBend` (every state) and `walkKeyframes` (walk states). Any file missing those sections — a fresh editor export with no animParams, or a diff-trimmed file — threw NullReferenceException on first pose. Added fills for all 6 missing sections mirroring JS `DEFAULT_PARAMS`. Paramless files now animate correctly in Unity, not just the HTML tools.
- **legStride signs flipped to -1/-1 everywhere** — it was a corrective convention fix, not per-character tuning: axis convention is shared by all models on this skeleton, so the default itself was wrong. Flipped in animator `DEFAULT_PARAMS`, editor `DEFAULT_PARAMS`, and the new Unity fill. Civilian1's `legStride` section now equals the default → trimmed from the file. Remaining custom sections: `jointOffset`, `crouching`.
- **jointOffset baked into Civilian1 geometry** — the ±6 X offsets (gid2/3 arms + inherited 8/9 forearms) were confirmed load-bearing. Baked them into the model data: 1,456 arm-group cells translated in `voxels`/`groups`/`regions`/`itemParts`, `pivots`[2,3,8,9] shifted ±6/96 normalized, `right_hand`→x10.5 / `left_hand`→x84.5 riding their forearms, then `animParams.jointOffset` dropped. `animParams` is now just `{crouching}` — the only remaining per-character tuning. Zero visual change: the bake is the identical post-rotation translation, moved from param into geometry.
- **Animator blank init** — `createDefaultCharacter()` (obsolete 16×32×10 placeholder with store-bought materials) no longer runs at startup or on failed sessionStorage import; animator opens empty like the building/character/item modules with a load-to-begin status. `pivots` now starts `{}` so no orphan gizmo rings render before a model exists. Playback starts PAUSED (`isPlaying=false`, button unprimed) — the clock no longer runs on an empty scene; `loadCharacterData` auto-plays on first real model load so load→animate still "just works". Function body left in place (dead code, possible future "demo" button).

- **Skin blend defaults OFF** — experimental and not ready; the checkbox + `blendEnabled` defaulted on in both tools so every preview (parity pose, mirror, animator) showed the unfinished armpit gradient. Now opt-in: untoggled previews match Unity's rigid FK output exactly (the compute shader has no skin-blend path — the only blend it does is the IK-override quaternion lerp).

- **attachRotation convention: authored for right hand** — the two hand weld bases differ by a 180° flip about the grip axis, so a correction authored on one hand lands inverted on the other. Convention fixed: `attachRotation` is authored for `grip_right`; left-hand welds auto-compensate with +180° X roll (`attachItem` in editor + `WeaponMount.LoadItem` in Unity — parity). Model 10 re-baked X90→X270 (its old value was authored against the left-hand basis back when the crossed hand points sent welds left).

- **ARM auto-weld + crossed-hand fix** — `Civilian1.json`'s derived `attachmentPoints` had hand names/positions crossed (`right_hand` pointed at the left-arm cluster at x78.5/gid8 — painted cells were correct, the derived dict was swapped). Corrected in place: `right_hand` {x:16.5, gid:9}, `left_hand` {x:78.5, gid:8}. ARM button now auto-welds the loaded prop via its own grip metadata (`autoAttachHand`: `grip_right` → right hand primary per right-arm convention, `grip_left` alone → left; no grip → status hint) instead of manual R/L pick buttons — removed the redundant `🔗 → R/L hand` buttons; detach moved into ARM-off + a standalone ✖ button.

- **jointOffset mechanism identified** — post-rotation group *translation* in voxel units (NOT a pivot adjustment; children inherit the parent group's offset). gid2/3 ±6 X shifts the whole arm 6 voxels outward after rest-pose rotation — a workaround from the pre-painted-pivot era ("correct limbs that end up inside the body without fighting the pivot position"). Likely obsolete now that pivots are authored; pending visual test (zero gid 2.x/3.x in the animator's jointOffset sliders → check shoulder seam).
- **Aim Sweep state (NEW, id 10)** — "aiming pose + the aiming arm(s) yaw left↔right" in BOTH tools (`character_animator.html` state grid + `voxel_editor.html` Anim-parity select). Implemented as aiming params + a sinusoidal `sweepYaw` injected into shoulder `reach` — only on arms actually in the aim pose (`swing ≠ 0`, so pistol preset sweeps one arm, dual/rifle sweep both) — plus `headFollow` so gaze tracks the gun. New `animParams.aimSweep` block (`amp` 0.55 rad ≈ ±32°, `freq` 1.6 rad/s, `headFollow` 0.6) merged over defaults like every other section. Exercises the shoulder joint/blend through the exact yaw range IK aiming will drive. Animator param panel gets sweep + aiming sliders and the weapon-preset dropdown for state 10.

### 🧪 TEST NOW
- Building mode → load a reference preview → switch dropdown to **Character** → scene should be empty when the default-model import prompt appears (no ghost model beside the volume). Repeat with the character reference **playing** and an item **attached to its hand** — all preview state clears.

---

## October 4, 2026 — IK Aim Solver + Range Tab (live firing-range tuning)

### Impact
- **Vinny can now raise his weapon and aim the Model 10's measured bore axis at the range target on demand.** While the sim is Dwelling (Target Practice hold-open), clicking a block that authored a `firing_position` opens a new **Range** tab in the debug HUD. The `IK AIM` toggle runs a closed-form 2-bone solve — SteelTide `ArmIKDrive` math (law-of-cosines elbow, twist-constrained bases, reach clamping) ported onto the kinematic voxel-group pose pipeline — driving shoulder + forearm groups every frame until released.
- **The bore axis is now authored data, not convention.** `bore_root` + `muzzle` centroids (verified `(15,0,0)` = +X on the Model 10) define `WeaponMount.AimDirection`; the forearm override slams that axis onto the target line with sights-up roll control.

### Changes
- `ArmAimSolver.cs` (new) — per-instance IK component: `Arm(target)`/`Disarm()`/`SetTarget`, validation (`Validate()` → failure reason), smooth blend in/out, azimuth/elevation + reach tuning fields, live telemetry (target distance, real welded-bore angular error). Solves in uncentered voxel space via the same world↔voxel bridge as `WeaponMount.LateUpdate`.
- `VoxelCharacterAnimator` — **IK override channel**: `ikOverrides` dict + `ikBlend` injected before chain assembly, replacing a group's param-derived `ownRot` (children still inherit the solved parent through FK). Public `QuatFromMat3`/`Mat3FromQuat` helpers + `GetParentGroup`.
- `CharacterPoseCompute.compute` — mirror channel on GPU: `_InstanceIKData` buffer (3 float4/instance: quatA, quatB, meta{gidA,gidB,blend,enabled}) + `QuatToMat`/`MatToQuat`/`Nlerp4`/`ApplyIKOverride` helpers; overrides blend into `ownRot` **and** `parentRot` (forearm voxels inherit the arm solve). `ComputeGroupRotation` gains `instIdx`.
- `VoxelChunkManager` — `InstancedCharacter.ik*` fields, `InstancedGroup.instanceIKDataBuffer`, per-frame pack/bind in `RenderInstancedGroup`, release in group teardown.
- `WeaponMount` — `boreAxisLocal` hoisted to parse-time field (was computed per-frame inline); solver context accessors (`Animator`, `Pivots`, `Dims`, `CharVoxelSize`, `HandPoint`, `HandGid`, `BasisB`, `ItemAttachRot`, `BoreAxisLocal`, `ItemFileName`).
- `DebugHUDManager` — `Tab.Range` + `DrawRangeTab()`: sim state, block point list, in-position check (XZ center vs `firing_position`), weapon/bore status, `IK AIM` toggle (gated on Dwelling + weapon + resolved points), solver telemetry, live tuning sliders (reach fraction, blend speeds, az/el offsets), reset button. `HandleBlockClicked` subscribes to `cityMap.OnBlockClicked` — fires only while `SimState.Dwelling` and only for blocks with `firing_position`, so planning-phase clicks are untouched.
- `GameUIController` — `Sim` accessor exposes `SimulationManager` for the dwell gate.
- `CityMap3D` — `GetBuildingPointNames(blockId)` accessor (union of authored attachment names per block); `HandleClick()` now also runs in execution mode so `OnBlockClicked` fires during the working week (subscribers gate themselves); **LMB click-to-center removed** — it fought `EventPlayer`'s character-follow camera (jump-then-snap-back), and was unused anyway.
- `COMBAT_VEHICLE_DESIGN.md` — new "Physics-Feel vs Physics" section: kinematic spring layer for recoil/sway/flinch (skill-tightened) vs selective PhysX for vehicles; SteelTide references added.
- `PathDebugRenderer` — **custom world-space beam API** (`SetCustomBeam(key, a, b, width, color)`/`ClearCustomBeam`) rendered through the existing CommandBuffer-into-voxel-RT path (a LineRenderer would hide under the RawImage overlay)
- `WeaponMount` — `showMuzzleRay` (default on): red beam from muzzle along the live bore axis, `muzzleRayLength` (60m); cleared on unequip
- `ArmAimSolver` — cyan sight beam (muzzle → target) while engaged — residual aim error is visually obvious against the red bore ray
- `DebugHUDManager` Range tab — muzzle ray toggle; torso-assist toggle + share/max-yaw sliders
- **Torso assist (turret)** — gid-0 yaw override via the IK channel: torso absorbs `torsoShare` of the horizontal yaw error (clamped `maxTorsoYawDeg`), arm solves the residual in the pre-torso frame so the bore still lands exactly. CPU: override folded into `GetBodyTransform`/`hasBodyRot` so it propagates through the standard body-transform append path; GPU: `_InstanceIKData` widened to 4 float4s/instance (quatA, quatB, quatTorso, meta{gidA,gidB,blend,flags bit0=arm bit1=torso}). Reduces arm swing → less shoulder-seam strain on segmented groups
- **Painted joint pivots (`pivot_N` attachments)** — joints are now authored data like bore_root/muzzle: paint a blob at a joint center in the editor → its centroid overrides `pivots[gid]` at load (`centroid ÷ dims`, index-space → normalized). 10 new `CHAR_ATTACH_GROUPS` entries (waist/neck/shoulders/hips/knees/elbows), diamond markers in Parts view. Consumed at all three pivot sites: `VoxelCharacter` → GPU `SetPivots`, `WeaponMount.pivots` (solver bone lengths + weld), `WeaponMount` animator (shares the same overridden dict). Unpainted → authored `pivots` JSON values, unchanged.
- **Editor note**: joint centers are interior — use the parts-view visibility toggles to hide the torso and reach buried shoulder-ball voxels for accurate `pivot_3`/`pivot_2` placement.
- **Editor undo fix (CRITICAL)** — `snapshot()` only captured `voxelMap` and the tag tools (bodypart/clothingregion/itempart) never pushed history, so a single Ctrl+Z after painting stepped back to the empty init snapshot and wiped the model. History now captures all four per-voxel maps and every tag stroke pushes — see `docs/known_issues/editor/UNDO_DESTROYS_MODEL_ON_TAG_STROKES.md`.
- **Editor joint-authoring tools** — per-group 👁 visibility toggles in the Body Parts list (hide Body → interior arm-ball voxels become clickable) + **arm test poses**: `Down`/`Up`/`Fwd`/`Off` buttons swing arms about the resolved pivot (painted `pivot_N` centroid → authored pivot → bbox fallback) so joint placement can be verified visually before export. Forearms swing rigidly with their parent arm; status line reports which pivot source each arm used.
- **Auto-materialized pivots** — `importAttachmentPoints` converts authored numeric `pivots` into painted `pivot_N` clusters on load (small voxel ball at each joint → diamond marker + editable centroid). Existing painted `pivot_N` data wins; painted attach points are never clobbered. Round-trips: export writes the cluster centroid back as the `pivot_N` attachment.
- **Editor view-mode fixes** — tab switches (Attach Pts ⇄ Body Parts ⇄ Wardrobe) now clear the other panels' exclusive view flags and sync button highlights, fixing the stuck-parts-view gray model. All six Material/View buttons route through `syncViewButtons`.
- **Pivot joint visualization** — `pivot_*` tagged voxels get a **per-voxel wireframe cube** + diamond centroid, drawn in every view mode (material/group/parts). Per-voxel instead of AABB — an AABB floats off clipped/interior clusters; wireframing the exact tagged cells shows true membership. Tagged voxels keep their body/group colors — the wireframe is pure annotation.
- **Pose-aware pivot markers** — during arm test poses the wireframe cells transform with their own `groupMap` gid (mixed arm/torso blobs split visibly — intentional: it reveals group membership at the seam). The diamond transforms through its pivot gid's ancestor chain: `pivot_3` stays put under its own rotation, `pivot_9` rides the arm's shoulder swing (FK semantics — a joint only moves when an ancestor rotates).
- **Anim-parity pose mode in the editor** — new "Anim parity" state select (Idle/Walk/Aim Walk/Aiming/Crouch/Down/T-Pose) under the blend controls. Runs the editable model through the *same* `computeGroupRotation` chain + displacement blend the animator applies (`posedPosEdit` = animator's `posedPos`, including `jgid` entry tags, torso bump injection, walk bob/shift). The editor was previously showing an adaptive test-swing while the animator applied ±90° param rest-pose rotations — same model, wildly different result. Editor preview is now pixel-equivalent to animator output for a chosen state. Pose buttons still work for the adaptive joint-swing test and reset the select.
- **Skin blend in `character_animator.html`** — the shoulder skinning experiment now runs during real animation playback. FK chain entries carry `jgid`; `posedPos()` applies each chain rotation and lerps the voxel a fraction `w` of the way toward the fully-rotated position — **linear displacement (chord path), not scaled angle (arc path)**: arc-path blending smears cells through space at 90° swings, chord-path lands them inside the joint crease (true LBS semantics). Weights: `smoothstep(d/R)` for owned joints + capped bump `cap·4t(1−t)` injected as a leaf-level dose into gid-0 chest cells. Arm own-rots cached per rebuild; duplicated instance/edge pose loops unified through `posedPos`. Same displacement model back-ported to the editor's test-pose path so both previews match.
- **Painted pivots export as canonical `pivots`** — `resolvedPivotsForExport()` folds `pivot_N` cluster centroids into the exported `pivots` dict (index-space → normalized, same rule as `CharacterJsonLoader.ApplyPivotOverrides`). Previously `pivots` exported stale authored values while painted joints lived only in `attachmentPoints` — the HTML animator and attach-preview ignored them. `sendToAnimator` payload now also carries `pivots` + `attachmentPoints` (it sent neither → animator silently fell back to bbox auto-detect).
- **Attach-part eraser** — `Body` (part id 0) now clears a voxel's point tag instead of writing an id-0 tag (which rendered a stray gray centroid sphere). Applies to click + selection-assign across character/item/building part lists; id-0 entries are also skipped on import so legacy tags self-clean on next save.
- **Shoulder skin-blend experiment** — voxels within `blendRadius` of a shoulder pivot rotate by `w·θ` about the same axis/pivot instead of rigidly (single transform, no dual-matrix skinning). Arm cells ramp `smoothstep(d/R)` → hinge gradient; torso cells get a capped bump `cap·4t(1−t)` → chest follows a little near the joint, fades to 0 at both ends. Live controls in Body Parts panel: blend checkbox + R + chest-cap sliders. Editor-preview only — runtime port would need the same weight in `VoxelCharacterAnimator` + `CharacterPoseCompute`.

### 🧪 TEST NOW
- Target Practice → Vinny dwells at the line → **single-click the range block** → debug HUD opens on **Range** tab (block id, point list, "In position: YES", Model 10).
- Click **IK AIM** → right arm should raise smoothly (~0.4s) and the Model 10's bore should point downrange at `firing_target`. Telemetry shows blend/target dist/aim error.
- **Torso assist ON** → chest yaws toward the backstop with the arm swinging less than before (share slider 0→1 A/B's it live). If the torso yaws the wrong way, flip `torsoYawSign` to −1.
- Az/el sliders should visibly rotate the aim; reach fraction limits extension. Toggle off → arm blends back to Idle.
- Paint `pivot_3` on the right-arm joint (hide torso part to reach interior) → re-export → IK shoulder behavior should subtly improve; console logs "painted joint pivot(s) applied".
- Regression: during planning, block clicks still select orders normally; Enter still releases the dwell.

---

## October 4, 2026 — Debug HUD Keys Tab + Runtime Spawn (hotkey cheat-sheet)

### Impact
- **The debug HUD now actually appears during play and lists every live hotkey.** `DebugHUDManager` existed but was in no scene and nothing spawned it — the "All Hotkeys" footer it carried was unreachable and stale (listed deprecated `FollowCamera` keys, missing F6/F7/R/F10). It now auto-spawns like `VehicleTestSpawner`, opens on a new always-available **Keys** tab, and only lists hotkeys whose handler components are actually present in the scene.

### Changes
- `CityMap3D.Awake` — creates a `DebugHUDManager` GameObject when none exists (same ensure-spawn pattern as `VehicleTestSpawner`)
- `DebugHUDManager` — new `Tab.Keys` + `DrawKeysTab()`: grouped cheat-sheet (This Panel / Map Camera / Scene & Render / Character Anim / Vehicles / Stress Test / Test Rigs); conditional sections driven by `FindFirstObjectByType` so inactive keys are never listed; `defaultTab` → Keys; footer rewritten to accurate essentials + pointer to Keys tab
- `DebugHUDManager.Update` — `Tab`/`Y`/`M` now gated behind `visible` (previously responded while the panel was hidden)

### Notes (discovered, not changed)
- `CityMap3D.HandleAnimationHotkeys` (digits 1-9/0 anim states) is defined but never called — dead code; the live state keys are `CharacterRig`'s letter bindings.
- `ClothingSystem` toggles a `debugPanelVisible` flag on `O` that nothing reads — vestigial; `O` and `` ` `` both still toggle this HUD.
- `F7` is shared: waypoint beams (always) and stress-test beam-count cycle (while a test runs).
- `R` conflicts with `CharacterTestRig`/test-rig reload keys if those components are ever added to the scene.

### 🧪 TEST NOW
- Play `Planning_and_Working_Scene` (or `MainScene`) → the debug HUD should appear top-left on the **Keys** tab showing the categorized hotkey list.
- Press `` ` `` or `O` → panel hides; press again → returns on the same tab. `Tab`/`Y`/`M` should do nothing while hidden.
- Footer under any tab shows the compact essentials line; spot-check listed keys (mouse on map, `R` render scale, `F6`/`F7`, `I/W/L/A/C/T` on the green-selected rig, `Space`, `=`/`-`, `F10` vehicles).

---

## October 8, 2026 — Target Practice Order + Building Attachment-Point Runtime

### Impact
- **The building event-point pipeline is now live end-to-end in Unity.** Painted `firing_position`/`firing_target` centroids in `shooting_range.stasset`'s SKEL tail are parsed at runtime, resolved to world space through the registered building address, and drive a new order: select hood → select the range block → Target Practice → run week → Vinny walks the sidewalk graph to the block, enters at the painted firing position, turns to face downrange, and holds the aim stance for the action phase. `firearms` skill +2 on resolution.
- Same machinery generalizes to `door`/`spawn`/`prop_slot`/`cover`/`decor` — the deferred "prop auto-placement layer" now has its resolution primitive.

### Changes
- `StAssetReader` — `LoadAttachmentPoints(path)`: parses v2 SKEL tail blocks (`'SKEL'`+len+JSON), scoped `attachmentPoints` extraction → `name → Vector3` voxel centroids
- `CityMap3D.TryGetBuildingPointLocal(blockId, name, out localPos)` — centroid → map-local via `addressRegistry` worldCenter/size; per-stasset cache; vs derived from footprint `size/dims`
- `SimEventType.FaceTarget` + `facePos` — new facing directive event (map-local point)
- `SimulationManager` — `StartSimulation` gains optional `faceTargetLocal`; emits `FaceTarget` after arrival before dialog; `OrderActionTicks["target_practice"]=166`; resolution bumps `hood.skills["firearms"]` +2; **`targetPracticeDwells`** (default on): new `SimState.Dwelling` — order resolves but the week stays OPEN (no ticks consumed, sim never completes) until `ReleaseDwell()` via **Enter key** in `EventPlayer.Update` — extended weapons-testing sessions at the line
- `EventPlayer` — handles `FaceTarget` (rotation slerp now runs while standing too, not only mid-move); practice holds **Idle** pose during the action phase (base for future IK-driven dynamic aiming — `Aiming` intentionally not used); **fixed corner-vs-center placement bug** — `useWorldPosition=false` branch (the path `CharacterRig`-spawned characters take) landed the volume corner on targets, offsetting body ~0.72m diagonal on EVERY waypoint + arrival; now subtracts half-size XZ (also fixed same pattern in `TickSimulation`)
- `CityMap3D.TryGetBuildingPointLocal` — `+0.5` per axis on centroids (index→cell-center convention, matching WeaponMount's bridge)
- `docs/systems/COMBAT_VEHICLE_DESIGN.md` §14 — **design decision recorded**: kinematic posing + spring-dynamics feel layer for characters (recoil/sway/flinch/knockdown, `firearms`-skill-scaled); PhysX reserved for vehicles only. Evidence drawn from SteelTide ragdoll pipeline review (their cost: skeleton extraction, per-bone bodies, re-authored wider bones, re-voxelization render path, Sense/Think/Act stability stack — wrong scale for a city sim). IK math (2-bone solve, twist constraint, reach sphere) ported from `ArmIKDrive`/`ReachSphere` — physics engine itself not ported
- `voxel_editor.html` — new `Bore Root` (`bore_root`) item part (id 7); when painted alongside `muzzle`, a **bore ray** renders in Parts view: solid beam between the centroids + dashed cyan projection 48 voxels out the barrel — empirical bore-axis verification before export
- `WeaponMount` — reads `bore_root` attachment point; `AimDirection` now uses measured axis `normalize(muzzle − bore_root)` when authored, falls back to item +X convention otherwise
- `GameUIController` — `targetPracticeButton` serialized field + `["target_practice"]` binding; execution start resolves `firing_position` (walk-to target) + `firing_target` (facing), falls back to block center with a warning when absent
- `docs/systems/WEAPON_ATTACHMENT_SYSTEM.md` — "Building Event Points — Runtime Consumption" section

### 🧪 TEST NOW
- No scene edit needed — when `targetPracticeButton` is unassigned, the controller clones the Lie Low button at startup (teal, labeled "Target Practice") and the normal order-button wiring picks it up. Assign the serialized field later if you want a permanent scene button.
- Select Vinny → click `block_46` (Central Block 46, the range) → Target Practice → Run Week
- Expected: path to the block → walks the last segment to the painted firing spot (~0.5m left of block center, 4.3m short side) → turns to face the target lane → aim stance during the action ticks → resolves "firearms +2" → walks home
- Watch for: correct final facing (+Z toward the backstop, not toward the sidewalk he entered from); aim stance holding through dialog; no stuck-at-sidewalk regression on extort orders

---

## October 8, 2026 — Fix: Angle-Dependent Building Culling (Tight-Bounds DDA + Step Floor)

### Impact
- **Flat buildings and tall-feature neighborhoods no longer drop geometry at certain camera angles.** The shooting range floor and the tenement roof around the water tower were being discarded mid-march by a fixed step budget that couldn't cross the volume. Fix also cuts ~70% of per-pixel traversal on flat buildings — a bugfix that is also a perf win.

### Root Cause (cataloged: `docs/known_issues/rendering/ANGLE_DEPENDENT_BUILDING_CULLING.md`)
- The DDA marched **full container bounds** (192×120×192 for the range) while content filled only y=0–36 — every pixel paid ~80+ empty-voxel traversal before reaching the floor.
- Step budgets were **fixed per LOD tier** (Near 264 / Mid 48 / Far 24 / Ultra 12), independent of volume size. Rays whose in-volume path exceeded `_MaxSteps` exited early → discard → holes. Tall features made it worse by inflating the marched box, lengthening the air traversal for every neighboring pixel ("tall objects cause the culling").

### Changes
- `VoxelChunkManager.cs` — chunk property block now sends `propTightBoundsMin/Max` (voxel units, Max exclusive); step budget floored at the voxel-cell crossing bound (`tightSX+SY+SZ + 4`).
- `VoxelProxyRaymarch.shader` — new `_TightBoundsMin/Max` uniforms; `RayAABB`+DDA bound to the tight AABB when set (`_TightBoundsMax.x > 0`), full container otherwise. Voxel indexing still uses container `dims`/`volOffset` — stride unchanged. Loop bound is now `stepCap = max(_MaxSteps, marchSX+SY+SZ+4)` computed from the marched bounds — authoritative for every path.
- **Correction caught in testing**: first pass floored at the Euclidean diagonal (~274 for the range) — wrong bound. The DDA counts cell crossings (≤ sx+sy+sz = 421), so near-axis-parallel rays at the lowest camera angle still starved. Fixed to the Manhattan bound; residual ~5% culling at extreme angles eliminated.
- **Docs captured**: new `docs/systems/RAYMARCH_TRAVERSAL_OPTIMIZATION.md` (shipped Phase 1 + deferred Phase 2 column-occupancy skip grid design + Phase 3 sector-path adoption); `VOXEL_ENGINE_GOTCHAS.md` Gotcha #6 (distance-vs-cell-crossing budget trap); `DOCUMENTATION_INDEX.md` updated.
- Instanced (`DrawMeshInstanced`) and sector-baked paths never set the uniforms → unchanged full-container behavior. Deferred: same tight-bounds treatment for sectors if the symptom appears there; column-occupancy skip grid as the long-term optimization.

### 🧪 TEST NOW
- Play the scene → look at `block_46` shooting range from low/grazing angles and overhead — floor should render at **every** angle, no mid-field holes.
- Top-down view of the tenement blocks — the roof region **around** the water tower should stay solid.
- Perf check: `[Perf]` log — `maxSteps` per-chunk now varies by LOD; draw calls unchanged. If a sector-baked block still shows holes, that's the deferred sector path.

---

## October 3, 2026 — Unity WeaponMount: Armed Character Transit (Phase 1–2)

### Impact
- **Characters can carry voxel weapons in Unity** — the item renders as its own raymarch volume (separate dims + voxelSize, never resampled) and is welded to the posed hand point every frame through the CPU FK port. Armed walk/aim/pathing = the M1 milestone: send Vinny on an extort mission and the Model 10 should track his right hand through the walk cycle.

### Changes
- `Assets/Scripts/Sim/WeaponMount.cs` — NEW component (on the character GO, `RequireComponent(VoxelCharacter)`): loads `voxel_items/*.json` → ComputeBuffer → `RegisterVolume`; per-frame CPU pose of the `right_hand` centroid (`animState/animTime/animSpeed` from the instanced handle) → weld `yaw ∘ R ∘ B ∘ attachRotation`; solves the item GO transform so the grip cell-center lands on the hand; exposes `MuzzleWorld`/`AimDirection` (projectile origin later). Coordinate bridge documented in-file: index space +0.5 → cell space.
- `CharacterJsonLoader` — `ParseAttachmentPoints` (fractional + `gid`), `ParseVoxelSize`, `ParseEulerDeg`, `ExtractPivotsRaw`
- `VoxelCharacterAnimator.PosePoint` — public fractional single-point pose (group chain + offsets + body bob/shift, identical output convention to `PoseVoxels`)
- `CharacterRig.equipItem` — defaults to `SW_Model_10.json` (both scene civilians spawn armed for the test; clear the field to unarm)
- `CharacterRig` — all rigs now start in **Idle** (T-Pose still reachable via the T hotkey for pose debugging); previously rig1 started T-posed → gun floated at the extended T-pose hand
- `CityMap3D.SpawnSceneCharacters` — debug civilians now spawn in a fully-vacant (all-`empty_land`) lot adjacent to HQ (nearest vacant lot as fallback) instead of inside the tenement block
- `SW_Model_10.json` — `attachRotation.x` 270→90 (180° roll compensation for the mirrored-arm weld basis)
- **Shooting range placed in-world**: `shooting_range.stasset` generated (v2, 192×120×192, event points in SKEL tail) into `voxel_buildings/`; `block_46` (r4c5, 2 blocks south of player HQ block_26) converted from 9× empty_land → full-block `shooting_range` slot-0 entry; `building_types` registered
- **Building point vocabulary extended**: `prop_slot`/`cover`/`decor` added to `BUILDING_POINT_GROUPS` (+ gen_shooting_range PART_DEFS + doc) — completes the marker set for future prop auto-placement layer. Verified export chain already carries building `attachmentPoints` end-to-end (paint → `itemParts`/`itemPartDefs`/`attachmentPoints` → `steelcity_stasset` JSON → SKEL tail)
- **Tools/json_to_stasset.py** (new): JSON → binary `.stasset` return path — completes the authoring loop (stasset→json→editor→json→stasset). Writes v2 with `attachmentPoints` embedded in the SKEL tail. Round-trip verified: shooting_range.json → .stasset = 111,082 voxels identical + all 3 event points in tail. NOTE: `StAssetReader` doesn't parse the SKEL tail yet — Unity-side point consumption is pending work
- **tenement_block_1 retired**: `block_51` (W Quarter) repointed → `tenement_block_0.stasset`; both tenement blocks now share the 192×120 building. `tenement_block_1.stasset`+meta moved to `VoxelAssetStudio/ARCHIVE/building_assets/`; stale `building_types` dims in city_layout.json corrected (32/96-era → 64/192)
- **Tools/stasset_to_json.py** (new): binary `.stasset` → consolidated editor JSON converter; converted `tenement_block_0` (192×120×192, 765k voxels) + `tenement_block_1` (192×88×192, 876k) into `VoxelAssetStudio/JSON Models In Progress/` for editor review
- **procedural_mob_buildings.py**: constants updated to city grid contract — `LOT_W/D=64` (1/9-block lot) + `BLOCK_W/D=192` (full block); all lot-building signatures → 64×64; `generate_apartment_block` core→184 so padded=192×192; `empty_land` h→8 / `road_tile` h→4 to match shipped binaries. Verified: all 5 registered generators produce standard dims
- **voxel_editor.html cleanup**: removed 1.78MB baked-in "Roof Deco" tower scene (`initialVoxels`) — editor now opens on a blank 96×68×96 building volume instead of a leftover template scene; file shrank 2.05MB→271KB; retitled "Steel City - Voxel Editor"; building default dims → **64×40×64 = one city lot** (verified: `CityMap3D.BuildingVoxelWidth=64`, 9 lots/block in city_layout.json, `empty_land`/`road_tile` both 64×H×64; full-block buildings like tenements are the exception at 192×H×192)
- **Building event points** (new): `BUILDING_POINT_GROUPS` added to editor Attach tab — `firing_position`, `firing_target`, `backstop`, `door`, `spawn` — buildings now paintable with the same `attachmentPoints` JSON contract as characters (no gid). Attach tab now visible for `assetType: 'building'`
- `Tools/gen_shooting_range.py` — M2 Shooting Range block (192×120×192, tenement container): firing line + bench at z≈12, 4m lane with rails, 5m/10m/15m distance marks, target board + bullseye at z=169, brick backstop wall at z=184 — with all three event points painted: `firing_position (95.5,2,10.5)`, `firing_target (95.5,7.5,169)`, `backstop (95.5,11.5,184)` — collinear downrange on lane center
- **Doc consistency pass**: `MODEL_DESIGN_STANDARD.md` §4 rewritten with verified facts — `Civilian1` (96³) faces −Z (face-region centroid proof), movement compensates via 180° yaw, `EventPlayer.modelFacingOffset` code-default of 0 flagged as stale; new handedness-convention subsection (labels = rendered anatomy; `right_hand` = high-x/gid 8). §1 notes Civilian1 as current production model. `WEAPON_ATTACHMENT_SYSTEM.md` + `DOCUMENTATION_INDEX.md` synced
- `CharacterPoseCompute.compute` — state 9 (T-Pose) now early-outs entirely in `ComputeGroupRotation` (no rotation chains, no `jointOffset` translations) matching CPU `VoxelCharacterAnimator` line 278. Previously the compute path applied idle `restCfg` arm drops AND the ±6-voxel shoulder `jointOffset` (exists to seat dropped arms), which detached the arms in bind pose — fragment path already guarded at line 591
- `PedestrianLookAround` — ambient look-around no longer stomps manually-set/driven states: only fidgets from Idle, and only restores Idle if it still owns the state (fixes T-pose/Aim hotkeys reverting to Idle)
- **Handedness correction**: `Civilian1.json` — swapped `right_hand`↔`left_hand` attachment point names (right_hand := x=78.5, gid 8). The low-x arm renders as the anatomical left hand (verified top-down); labels now match rendered anatomy. Aim preset moved back to the high-x chain (`armSwingL=-1.4`, `elbowBendL=0.3`, `torsoTwist=0.2`) so aim arm = gun arm. Editor `aiming` defaults + `pistol` preset in `voxel_editor.html` synced to match (`character_pose_engine.js` was already correct).
- `StressTestSpawner.equipItem` — empty by default; set in inspector to arm F8 extort-loop agents
- `docs/systems/WEAPON_ATTACHMENT_SYSTEM.md` — Phase 1–2 marked implemented
- 🧪 **TEST NOW**: play the scene — Civilian_01/_02 should spawn with the Model 10 welded to the right hand; run an extort mission → verify seat, orientation (sights-up, barrel along arm), scale (0.005 detail), and tracking through the walk cycle. Watch for: half-voxel seat offset, mirrored/rolled barrel, drift during bob/shift.

---

## October 3, 2026 — Fractional Attachment Centroids + Owning GID in Exports

### Impact
- **Sub-voxel attachment precision** — centroids no longer rounded to int on export; a painted 2×2 muzzle face now exports `{y:22.5, z:4.5}` instead of `{23,5}`, and the weld preview + centroid markers use the same fractional value the file carries
- **Unity can pick the FK chain directly** — each character point now records its owning animation `gid` (majority across the painted cluster), removing the voxel-lookup guess that breaks once points are fractional

### Changes
- `voxel_editor.html` — `buildAttachmentPoints()` exports fractional centroids (3-decimal quantize) + majority `gid` from `groupMap`; `attachItem` prefers `cPt.gid` over the groupKeys lookup. Contract: points stay index-space (index = voxel center); Unity converts to cell space with `+0.5` per axis at the weld
- `docs/systems/WEAPON_ATTACHMENT_SYSTEM.md` — documented index-space vs cell-space convention, `+0.5` weld rule, `gid` field; example updated to current Model 10 dims + `attachRotation`
- **Action required**: re-export `Civilian1` and `SW_Model_10` from the editor to get fractional points + gid into StreamingAssets

---

## October 3, 2026 — Fix: Editable Voxels Rendering 2× After Preview Load

### Impact
- **Item mode tools appear correct again** — line/paint/highlight are 1×1×1. Root cause was not a brush: `rebuildCharMesh` leaves the preview scale (s=2 for char@0.01m vs item@0.005m) on the shared `dummyMatrix`; `rebuildMesh`'s `setPosition` preserved it → every editable voxel rendered double-size after any preview rebuild. Character mode was unaffected (s=1). Cataloged: `docs/known_issues/editor/DUMMY_MATRIX_SCALE_LEAK.md`

### Changes
- `voxel_editor.html` — all 5 `dummyMatrix.setPosition(...)` sites → `makeTranslation(...)` (highlight mesh, selection overlay, ruler frozen, editable instanced mesh, editable edges) — pure translation, no stale scale possible
- `attachItem` roll fix — weapon "up" is now world-up projected ⊥ the rest arm axis (was `cross(up,forward)` which inverted the profile for both hands); gun now sits sights-up regardless of which side the arm extends
- `attachItem` baked +90° roll about item X — the generated revolver's sights axis is -Z not +Y (dial-in verified via the new attach-rot nudge buttons); `attachRotOffset` remains as a live ±90/±15 nudge layer for future items
- `attachRotation` JSON field replaces the hardcode — per-item grip correction stored on the file, dialed via nudge buttons, baked on export; loaded → seeds base + resets dial
- Aiming preset mirrored to RIGHT arm (`armSwingR/elbowBendR/torsoTwist:-0.2`) in editor defaults, weapon preset, and both Civilian1 JSONs — right-handed weapons now aim right-handed
- Right-arm joint pivots fixed in both Civilian1 files — gid 3 shoulder pivot was 1 voxel inboard of the arm's inner edge (visible arm↔torso gap), gid 9 elbow pivot was 3 voxels inside the upper arm (forearm swung around a point buried in the bicep); both now sit on their joint columns (x=35, x=27) matching the symmetric left side
- Editor UX: additive selection (Alt = add flood region / Shift+Alt = add voxel / Ctrl+Alt = additive box, live preview on Alt), centroid glow markers in Parts view (fractional, shine-through), Grid + Axes toggles next to BG picker (localStorage-persisted)
- Assets promoted: `Civilian1Test2.json` → `voxel_characters/Civilian1.json` (hand attachment points + right-arm aim + fixed pivots), `SW_Model_10FINAL.json` → `voxel_items/SW_Model_10.json` (remodel, 2734 voxels, grip_right/muzzle points, baked attachRotation X=270)

---

## October 2, 2026 — Character Attachment Points + Named `attachmentPoints` Export

### Impact
- **Attach Pts tab now works in Character mode** — paints `right_hand`, `left_hand`, `right_shoulder`, `cheek` (the character-side anchors from `WEAPON_ATTACHMENT_SYSTEM.md`). A painted region (e.g. 4×1×1 on the palm) exports as its centroid — one named point
- **`attachmentPoints` named format is now emitted by every export** — the Unity-facing field. `itemParts` (painted voxel map) remains alongside it for editor round-trips; loaders reconstruct paint marks from `attachmentPoints` when `itemParts` is absent, so either field survives a round-trip
- **`gen_sw_model10.py` emits both fields from one `ATTACH_POINTS` table** — single source of truth for generated weapons

### Changes

#### `VoxelAssetStudio/voxel_editor.html`
- `CHAR_ATTACH_GROUPS` — character point list (ids shared with item list; `key` fields are the JSON names: `right_hand`, `left_hand`, `right_shoulder`, `cheek`)
- `activePartGroups()` / `partColor()` / `partName()` helpers; all ITEM_PART_GROUPS/ITEM_PART_COLORS call sites switched
- `buildAttachmentPoints()` — centroid-per-part export; `importAttachmentPoints()` — merges `itemParts` + `attachmentPoints` on load (used by all 3 load paths + default-model loader)
- `getEditorData()` — writes `attachmentPoints` + `itemPartDefs: activePartGroups()` + `name`
- `updateTabsForAssetType()` — Attach Pts tab visible for character; selection reset guard when the active list changes; export modal title per asset type
- `.parts.json` export now carries named `attachmentPoints` + raw `parts` map + `partDefs`
- `loadedModelName` — filename stem tracked on all load paths; consolidated export downloads as `<modelname>.json` (drop-in StreamingAssets replacement — was hardcoded `Vinny.character.json`) and writes the correct `format` per type (`steelcity_item`/`steelcity_stasset`/`steelcity_character`)
- **`loadedAnimData`** — `pivots`/`animParams`/`states` preserved verbatim through load→export round-trips. Previously `getEditorData()` dropped them, so re-exported characters posed with default pivots → detached drifting arms (caught when Civilian1 was re-exported with painted hand points). Cleared on asset-type switch so stale data can't leak into a fresh model. Cataloged: `docs/known_issues/editor/EXPORT_DROPS_ANIM_METADATA.md`
- **Attach preview** — Reference Preview gains 🔗→R / 🔗→L / ✖ buttons: renders a copy of the editable model bound to the reference character's `right_hand`/`left_hand` point via `attachItem()`. Hand point posed through `poseVoxels` with its forearm gid (looked up from the model's groups map, fallback gid 9/8); item rotation = cumulative chain rotation ∘ basis aligning item +X (muzzle) to the rest forearm axis; grip→hand coincidence in world units. Re-attaches on pose change and model edits. `parseCharacterData` now returns `attachmentPoints` (+ `itemParts`→named fallback) and `groupKeys`

#### `Tools/gen_sw_model10.py`
- `ATTACH_POINTS` / `ATTACH_IDS` tables drive both `attachmentPoints` and `itemParts`; regenerated `SW_Model_10.json` (same geometry — 1,872 voxels)

#### Docs
- `WEAPON_ATTACHMENT_SYSTEM.md` — JSON examples match reality (0.005m gun, dual fields, centroid rule); Painting Workflow marked IMPLEMENTED with the dual-mode (item/character) workflow; Phase 4 marked done except in-panel grip preview

### Testing Notes
- 🧪 **TEST NOW**: Character mode → load Civilian1 → Attach Pts tab → paint a 4×1×1 cluster on each hand's palm (`right_hand`/`left_hand`) → export → JSON contains `attachmentPoints.right_hand` at the cluster centroid
- 🧪 Re-load that exported file → marks reappear in Parts view (round-trip via `itemParts`)

---

## October 2, 2026 — Animator voxelSize + Real-Time Animation + Preview Scale Consistency

### Impact
- **`character_animator.html` now renders at true world scale** — voxelSize-aware (0.01m default, read from JSON). Civilian1 displays at 0.62m instead of 62 units
- **Animator timing matches Unity production** — real `performance.now()` delta time instead of fixed 0.016; `animSpeed` no longer double-applied (matches `CharacterAnimation.cs`: time accumulates raw, speed only divides `cycleDuration`)
- **Voxel editor reference preview renders at true relative scale** — fixed stale 0.02m hardcode that made every preview model render 2× too large. Loading the same file as editable + preview now produces identical sizes
- **Character preset defaultDims corrected** — 48³ → 96³, matching the upscaled character standard
- **`voxelSize` field added to `Civilian1.json`** — files now self-describe their scale

### Changes

#### `VoxelAssetStudio/character_animator.html`
- `voxelSize = 0.01` global; all mesh building uses `dummyMatrix.compose()` with world-scale positions + scaled box
- `computeGroupRotation` gets `1.0` (voxel-space pivots); `compose()` handles world conversion
- Camera, grid, compass, pivot gizmos, raycast plane all scale by `voxelSize`
- `loadCharacterData()` / `loadProject()` read `voxelSize` from JSON; export writes it
- Animation loop: `animTime += dt` (real delta, 100ms clamp); `lastFrameTime` reset on play/pause/stop/scrub

#### `VoxelAssetStudio/voxel_editor.html`
- `getCharScaleRatio()` — preview voxelSize from loaded JSON (or `assetType` preset, or character preset fallback) ÷ editable voxelSize; replaces hardcoded `0.02 / itemVoxelSize`
- **Walk preview crash fix**: `getWalkPose` had `bobFn`/`shiftFn` assigned evaluated numbers then called (`TypeError: bobFn is not a function` whenever bodyBob enabled). Replaced with canonical `-cos(phase·4π)` bob / `sin(phase·2π)` shift — matching `character_animator.html` + Unity `VoxelCharacterAnimator.cs` (had also drifted to wrong frequencies/phases)
- Same `bobFn` crash + drift fixed in `character_pose_engine.js` (duplicated copy)

#### Item voxelSize raised to 0.005m + Model 10 rebuild
- **Decision**: items/props moved from 0.01m to 0.005m/voxel (2× density) — same pattern as the character upscale. Per-file `voxelSize` makes it a per-model decision; attachment becomes transform-based (already the design in `WEAPON_ATTACHMENT_SYSTEM.md`), superseding buffer compositing
- **`Tools/gen_sw_model10.py`**: new parametric generator — rebuilds `SW_Model_10.json` at 48×26×10 with proper anatomy: distinct Aged Metal cylinder, slim barrel + top rib, suspended ejector rod housing ("two tubes" profile), hammer spur, rear sight notch, D-loop trigger guard + brass trigger, angled wood grip with exposed metal backstrap, brass front sight. Also fixes orientation (original was muzzle −X, standard requires +X) and pre-seeds `itemParts` (grip_right, grip_left, muzzle). 1,872 voxels; original backed up to `.original.json`
- **`voxel_editor.html`**: prop preset → `voxelSize: 0.005`, `defaultDims: [48,26,10]`; dropdown label updated
- **`Tools/upscale_character.py`**: now scales `itemParts` keys and divides `voxelSize` by N — usable for future item upscales
- **Docs**: `WEAPON_ITEM_MODEL_STANDARD.md` updated throughout (scale rationale, dims tables, transform-attachment section, deprecated compositing note); `DOCUMENTATION_INDEX.md` entries updated
- `parseCharacterData()` returns `voxelSize` (explicit field → assetType preset → null)
- Character preset `defaultDims` → `[96, 96, 96]`
- Panel renamed "Reference Preview" (any model JSON works for scale comparison); "Sync Gun" → "Re-center"; info line shows resolved voxelSize + ×scale
- (Earlier uncommitted work also included: Item Part Paint tool for attachment points, per-asset-type default model loading, `savedHistoryIndex` dirty tracking, tab visibility by asset type)

#### `Assets/StreamingAssets/voxel_characters/Civilian1.json`
- Added `"voxelSize": 0.01`

#### Docs
- `CHARACTER_SPAWNING_SYSTEM.md`, `WEAPON_ITEM_MODEL_STANDARD.md`, `DOCUMENTATION_INDEX.md`, `MOB_SIM_SCALE_STANDARD.md` — stale 0.02m character scale refs corrected to 0.01m
- `docs/known_issues/` — created; first entry `editor/REFERENCE_PREVIEW_SCALE.md` (FIXED)

### Known gaps
- Unity doesn't read `voxelSize` from character JSON (`VoxelCharacter.cs` Inspector field only) — fine while standard is 0.01, flag for later
- `voxel_editor.html` duplicates the pose engine inline instead of importing `character_pose_engine.js`

### Testing Notes
- 🧪 **TEST NOW**: voxel_editor → Character mode → load Civilian1 as editable + as preview → should be identical size
- 🧪 **TEST NOW**: Item/Decor mode → gun in bounds + Civilian1 preview → character ~2.6× gun length (62 vs 24 voxels)
- 🧪 **TEST NOW**: Load SW_Model_10 into the preview picker → same size as editable gun
- 🧪 **TEST NOW**: character_animator → walk animation speed should match Unity playback rate regardless of browser FPS
- 🧪 **TEST NOW**: Item/Decor mode → new SW_Model_10 (48×26×10 @ 0.005) loads; preview Civilian1 renders at ×2.0 in item voxel units (correct: same real-world size as before, since char = 0.01m/item = 0.005m)
- 🧪 **TEST NOW**: Reference Preview info line shows `0.005m/voxel (×2.00 vs editable)` when gun loaded as reference in another mode

---

## August 14, 2026 — Character Model Upscale + VoxelSize Fix + Cheap Shading

### Impact
- **Character model upscaled 2× (48³→96³)** to fix raymarch see-through artifacts during leg animation
- **voxelSize halved (0.02→0.01)** to maintain same physical size (0.96m) with doubled voxel density
- **Cheap shading enabled for instanced characters** to fix black pixel darkening during head-turn animation

### Changes

#### Character Model Upscale (48³ → 96³)
- **`Tools/upscale_character.py`**: New Python script to 2× nearest-neighbor upscale character JSON voxel models. Scales dims, voxels, groups, regions, jointOffset, and crouching.modelLower. Pivots (normalized 0-1) unchanged. Backs up original to `.original.json`
- **`Civilian1.json`**: Upscaled from 48×48×48 to 96×96×96 (884,736 voxels). Fixes raymarch sampling gaps during walk/look animations

#### VoxelSize Correction (0.02f → 0.01f)
- **`VoxelCharacter.cs`**: Default `voxelSize` changed from `0.02f` to `0.01f`. Tooltip updated
- **`CityMap3D.cs`**: `characterVoxelSize` changed from `0.02f` to `0.01f` (used by HoodSpawner + StressTestSpawner)
- **`CharacterRig.cs`**: `voxelSize` default changed from `0.02f` to `0.01f`
- **`AnimationTestSpawner.cs`**: `voxelSize` default changed from `0.02f` to `0.01f`
- **`ForwardTransformTestRig.cs`**: `voxelSize` default changed from `0.02f` to `0.01f`
- **`Archive/ClothingTestSpawner.cs`**: `voxelSize` default changed from `0.02f` to `0.01f`
- **Note**: Existing scene prefabs/ScriptableObjects with serialized `voxelSize = 0.02` need manual Inspector update

#### Cheap Shading Fix (Black Pixel Darkening)
- **`VoxelChunkManager.cs:1664`**: `_CheapShading` set to `1` for instanced character renders
- **Root cause**: After CSPose forward-transform scatters voxels, single-voxel gaps appear at group boundaries (neck, shoulders, hips). `SmoothNormal` samples these gaps → zero/skewed gradient → squared lighting crushes pixel to black
- **Fix**: Skip `SmoothNormal` for instanced characters. Use raw DDA face normals (axis-aligned ±X/±Y/±Z). Visual impact negligible — art style is intentionally blocky

#### Documentation
- **`docs/systems/CHARACTER_SPAWNING_SYSTEM.md`**: New comprehensive doc covering spawning system, GPU instancing pipeline, component stack, spawner scripts, asset format, setup guide, upscaling, and architecture decisions
- **`docs/core/DOCUMENTATION_INDEX.md`**: Added spawning system entry under Systems Design + keywords

---

## August 14, 2026 — Per-Instance Clothing System + Consolidated Character Spawning

### Impact
- **Each instanced character can now wear a unique outfit** — different material remapping per instance, all in a single draw call
- **Consolidated hierarchy**: Replaced singleton "Vinny" with `Characters/Civilians/Civilian_01 + Civilian_02` — clean, consistent scene structure
- **Debug HUD character selector**: Isolate hotkey controls to a specific character at runtime
- **Clothing test verified**: Blue suit (Civilian_01) and brown suit (Civilian_02) render correctly side-by-side, 3 instances in 1 draw call

### Changes

#### Per-Instance Material Remap Pipeline
- **`CharacterPoseCompute.compute`**: CSPose kernel now applies per-instance material remapping via `instanceMaterialRemapBuffer` — each instance looks up its own region→material mapping
- **`VoxelChunkManager.cs`**: Added `instanceMaterialRemapBuffer` to `InstancedGroup`, built per-frame from `InstancedCharacter.materialRemap` arrays. New API: `SetInstanceOutfit()`, `GetInstanceOutfit()` for per-instance outfit management
- **`ClothingSystem.cs`**: Rewritten to use per-instance remap API (`SetInstanceOutfit`) instead of modifying the shared voxel buffer — outfits are now per-instance, not shared

#### Consolidated Character Spawning
- **`CityMap3D.cs`**: Replaced Vinny singleton + ClothingTestSpawner with `Civilians/Civilian_01 + Civilian_02`. Both get `CharacterRig` with proper spawn positions. `ApplyCivilianOutfits()` coroutine waits for init then applies blue/brown suits
- **`CharacterRig.cs`**: Added `Controllable` flag + `ActiveRig` static selector. Hotkeys only process when `Controllable == true`. `spawnPosition` made `internal` for CityMap3D access
- **`GameUIController.cs`**: Updated "Vinny Moretti" status text to "Civilian_01"
- **`EventPlayer.cs`**: Updated Vinny comment references to generic character terminology

#### Debug HUD Character Selector
- **`DebugHUDManager.cs`**: Added `CharacterRig` tracking alongside `ClothingSystem`. Clothing tab now has two selector rows:
  - **Green buttons** (Character Control): Routes hotkeys (T/I/W/L/A/C/Space) to selected character
  - **Blue buttons** (Clothing Instance): Selects which character's outfit to edit
  - Periodic refresh of both lists (every 1 second)

#### Archived
- `ClothingTestSpawner.cs` → `Assets/Scripts/Sim/Archive/` (replaced by direct CityMap3D spawning)
- `ClothingTestSpawnerEditor.cs` → `Assets/Scripts/Sim/Archive/`

### Files Modified
| File | Change |
|---|---|
| `CharacterPoseCompute.compute` | Per-instance material remap in CSPose kernel |
| `VoxelChunkManager.cs` | `instanceMaterialRemapBuffer`, `SetInstanceOutfit`/`GetInstanceOutfit` API |
| `ClothingSystem.cs` | Rewritten to use per-instance remap instead of shared buffer |
| `CityMap3D.cs` | Consolidated spawning: Civilians/Civilian_01+02, `ApplyCivilianOutfits` coroutine |
| `CharacterRig.cs` | `Controllable` flag, `ActiveRig` static, `spawnPosition` internal |
| `DebugHUDManager.cs` | Character selector (green) + clothing selector (blue) in Clothing tab |
| `GameUIController.cs` | Vinny → Civilian_01 status text |
| `EventPlayer.cs` | Comment updates |

### Testing Notes
- Both civilians spawn inside HQ tenement block, side-by-side (0.8 units apart)
- Civilian_01 wears blue suit (mat 126), Civilian_02 wears brown suit (mat 106)
- Press `O` → Debug HUD → `Tab` to Clothing tab → green buttons switch hotkey control between characters
- T/I/W/L/A/C hotkeys only affect the selected (green-highlighted) character
- Blue buttons select which character's outfit to modify via preset buttons
- All 3 instances (including any stress test agents) render in 1 draw call

---

## August 10, 2026 — Phase 1: Keyframe Shader Port

### Impact
- Walk keyframe animation from the HTML animator now runs in Unity via GPU shader
- Catmull-Rom spline interpolation ported to HLSL — no hang at passing poses
- Body bob + weight shift applied to volume offset in shader
- FK parent-child chains (forearm→arm, shin→leg) now work in shader inverse transform
- `.anim.json` files auto-loaded alongside `.stasset` files in StreamingAssets
- Falls back to hardcoded sin() if no `.anim.json` present (backward compatible)

### Changes

#### `Assets/Resources/Shaders/VoxelProxyRaymarch.shader`
- **New buffers**: `_WalkKeyframes` (10 float4s), `_JointConfig` (7 float4s), `_WalkConfig` (float4)
- **New HLSL functions**: `CatmullRom()`, `Smoothstep01()`, `CosineInterp()`, `GetWalkPoseValue()`, `GetWalkCyclePhase()`, `RotationZ()`, `RotationByAxis()`
- **Replaced** hardcoded `sin(animTime * 6.0 * animSpeed)` in `ComputeGroupRotation` with keyframe interpolation for all walking states (groups 2-9)
- **Added** groups 6/7/8/9 (shins/forearms) to `ComputeGroupRotation` — were previously unhandled
- **Added** `ParentOfGroup()` + FK chain support in `InverseGroupTransformOffset` — child groups now inverse-transform through parent rotation
- **Added** body bob + weight shift to volume offset in fragment shader (walking states only)
- **Rest pose composition**: arms now compose Z (rest) → swing axis; legs compose Y (twist) → stride axis
- **Fallback**: walking branches fall back to sin() if `_WalkKeyframesEnabled == 0`

#### `Assets/Scripts/Sim/VoxelCharacter.cs`
- **New**: `LoadAndApplyAnimParams()` — loads `{assetName}.anim.json` from StreamingAssets, parses with `JsonUtility`, uploads to `VoxelChunkManager.SetWalkKeyframes()`
- **New**: JSON data classes for anim_params format (AnimParamsJson, WalkKeyframesData, WalkKFPose, etc.)
- **Auto-mirror handling**: when `autoMirror` is true, kf2/kf3 derived from kf0/kf1 by L↔R swap in C# before upload
- **Null-safe**: guards against missing sections (armSwing, legStride, etc.) with sensible defaults

#### `Assets/Scripts/UI/VoxelChunkManager.cs`
- **New**: `SetWalkKeyframes(assetFileName, walkKeyframes, jointConfig, walkConfig)` — creates ComputeBuffers and binds per instanced group
- **New**: `InstancedGroup` fields — `walkKeyframeBuffer`, `jointConfigBuffer`, `walkConfig`, `walkKeyframesEnabled`
- **New**: shader property IDs — `propWalkKeyframes`, `propWalkKeyframesEnabled`, `propJointConfig`, `propJointConfigEnabled`, `propWalkConfig`
- **Updated**: render method binds walk keyframe buffers via MaterialPropertyBlock
- **Updated**: `ReleaseAllInstancedGroups()` releases new buffers

### How to test
1. Export `.anim.json` from the HTML animator (Export .anim.json button)
2. Rename to match the .stasset (e.g. `character_hoodlum_0.anim.json`)
3. Place in `Assets/StreamingAssets/voxel_buildings/` alongside the `.stasset`
4. Ensure `.groups` file also exists (for groupID per voxel)
5. Enter Play mode — character should walk with the animator's keyframe poses, not the old sin() wave

### Backward compatibility
- No `.anim.json` → shader uses hardcoded sin() (old behavior)
- No `.groups` file → groupIDs default to 0 (no limb transforms)
- Old `.stasset` files work unchanged

---

## August 9, 2026 — Extrude De-extrude, Paint Drag, Volume Bounds, ROM Sizing, Model Centering

## August 9, 2026 — Extrude De-extrude, Paint Drag, Volume Bounds, ROM Sizing, Model Centering

### Impact
- **De-extrude** (Ctrl+click/drag) — remove layers from a face inward, mirror-aware
- **Paint drag** — click+drag to paint across voxels in one stroke, single undo entry
- **Volume bounds wireframe** — cyan box shows the editable volume limits
- **Set Volume Size** — directly type new W/H/D, or auto-size to range of motion
- **Size to ROM** — Steel Tide approach: computes maxReach from model center, sizes cube to `2×maxReach + 2×padding`
- **Center Model** — shifts model to center of grid for symmetric animation clearance
- **Mirror fix for extrude** — extrude/de-extrude now route through `setVoxel` for mirror support
- **Mouse button remap** — left=tools only, middle=orbit, right=pan, scroll=zoom (no more ctrl/shift pan conflict)

### Changes

#### `VoxelAssetStudio/voxel_editor.html`
- **De-extrude**: `performDeExtrude(hit)` function; Ctrl+click removes face layer; Ctrl+click+drag removes multiple layers inward; `extrudeDragDeextrude` flag; preview shows voxels to be removed
- **Mirror for extrude**: `performExtrude`, `performDeExtrude`, and drag-extrude mouseup all use `setVoxel` instead of direct `voxelMap.set/delete`; status shows `+mirror` suffix
- **Paint drag**: `paintDragActive` state; mousedown starts stroke, mousemove paints each new voxel, mouseup commits single history entry; `paintDragVisited` Set prevents double-paint; mirror-aware via `setVoxel`
- **Volume bounds wireframe**: `buildVolumeBounds()` creates cyan `LineSegments`/`EdgesGeometry` box; ⬜ toolbar button toggles visibility; auto-rebuilds on expand/load/import
- **Set Volume Size modal**: 📦 button opens modal with direct W/H/D inputs; `applyVolumeSize()` resizes grid (clamps voxels if shrinking); rebuilds grid + bounds
- **Size to ROM**: `sizeToROM()` — computes model centroid, maxReach (farthest voxel from center), side = `ceil(2×maxReach) + 2×padding`; sets W/H/D inputs for review before apply
- **Center Model**: 🎯 button; `centerModel()` shifts voxels + groupMap to grid center; reports shift + new bounds
- **Mouse remap**: `controls.mouseButtons = { LEFT: null, MIDDLE: ROTATE, RIGHT: PAN }`; left freed for tools; `controls.enabled = false` during extrude drag
- **OrbitControls conflict fix**: `controls.enabled = false` on extrude drag start, `true` on mouseup

#### `VoxelAssetStudio/character_hoodlum_0.json`
- Model centered in grid: shifted from X[0-15] Y[0-31] Z[0-9] to X[40-55] Y[18-49] Z[43-52]
- All 2404 voxels + 2404 group entries + 5 pivots shifted by (+40, +18, +43)

### Files Modified
| File | Change |
|---|---|
| `VoxelAssetStudio/voxel_editor.html` | De-extrude, paint drag, volume bounds, ROM sizing, center model, mouse remap, mirror extrude fix, compass alignment fix, auto-center after resize |
| `VoxelAssetStudio/character_hoodlum_0.json` | Model centered in 96×68×96 grid |

### Alignment Fixes
- **Compass**: Moved from corner (`-W/2-4, 0, -D/2-4`) to origin `(0,0,0)` — axes now extend from volume center
- **Compass rebuild**: `buildCompass()` now called on all W/H/D changes (expand, resize, load, import) — was only built once at startup
- **Center Model camera**: Now focuses camera on model center in world space (was targeting volume center which could be empty space)
- **Auto-center after resize**: `applyVolumeSize` now calls `centerModel(true)` after changing W/H/D — model stays centered in new volume
- **Center Model refactor**: `centerModel(skipHistoryAndMesh)` flag added so callers can avoid double history pushes

### Testing Notes
- 🧪 **TEST NOW**: Extrude tool — Ctrl+click a face → removes face layer. Ctrl+drag → removes multiple layers. Mirror X on → both sides de-extrude.
- 🧪 **TEST NOW**: Paint tool — click+drag across voxels → paints in one stroke. Ctrl+Z → undoes entire stroke.
- 🧪 **TEST NOW**: 📦 button → Set Volume Size modal. Type new W/H/D → Apply. Or click "Size to ROM" → auto-computes, then Apply.
- 🧪 **TEST NOW**: ⬜ button → toggles cyan volume bounds wireframe.
- 🧪 **TEST NOW**: 🎯 button → centers model in grid.
- 🧪 **TEST NOW**: Middle-drag → orbit. Right-drag → pan. Scroll → zoom. Left-click → tools only (no camera interference).

---

## August 9, 2026 — Body Part Paint Tool, Additive Selection, Mirror Highlight Fix + Half-Voxel Offset

### Impact
- **Body Part Paint tool** (🧩) — paint voxels into animation groups (head/torso/L+R arms/L+R legs) directly in the voxel editor with a visual group view mode
- **Right-panel tabs** — Layers & View / Body Parts tabs organize the right panel
- **Move Selection tool** (↭) — move a selection as a unit
- **Additive Shift+click selection** — add/remove individual voxels to/from selection instead of replacing it
- **Mirror highlight fix** — hover previews now show mirrored landing spots for all tools (place/paint/erase/fill/line/box/ruler/bodypart); previously only the primary voxel highlighted even though edits mirrored
- **Half-voxel mirror offset** (½ button) — shifts the mirror plane from voxel boundaries (`W-1-x`) to voxel centers (`W-x`) for a self-mirroring central spine on even-sized grids

### Changes

#### `VoxelAssetStudio/voxel_editor.html`
- Added Body Part Paint tool (🧩) with `bodypart` tool action; assigns voxels to selected body group, mirrors across enabled axes
- Added right-panel tabs (`.rp-tabs` / `.rp-tab` / `.rp-panel`) — Layers & View / Body Parts
- Added Body Parts tab content with group list, swatches, counts, view-mode toggle
- Added Move Selection tool button (↭) in toolbar
- Added group view mode (`groupViewMode`) — recolors voxels by group when active
- **Mirror highlight fix**: extracted `mirrorCandidates(x,y,z)` helper; added `mirrorPositionsPreview(positions)` (no occupancy check, so empty cells show up); `updatePreview` now calls it after the per-tool switch (skipped for active `select` to avoid double-mirror of frozen selection)
- **Half-voxel mirror offset**: added `mirrorHalf` state + `mirrorCoord(c, size)` helper (returns `size-c` when half on, `size-1-c` when off); routed `setVoxel`, `mirrorVoxelPositions`, bodypart tool, and preview mirror all through it; `updateMirrorPlanes()` shifts plane position by 0.5 when half on; added ½ toggle button in adv-panel

#### `VoxelAssetStudio/character_pipeline.html`
- Additive Shift+click selection — adds/removes individual voxels instead of replacing selection
- Added Save Project / Export .stasset JSON / Export .groups JSON buttons to left panel
- Line algorithm replaced (Bresenham 3D → lerp round) for cleaner diagonal lines
- Paste mode now intercepts all keys at top of keydown handler (arrows/Enter/Esc/[/] only; blocks others)
- `Ctrl+Z` / `Ctrl+Y` handled at top of keydown (returns after) so they don't fall through to tool hotkeys
- Esc now clears all tool states (lineStart, boxStart, rulerStart, extrude drag, selection, highlight) before switching to camera
- Added `realignSceneToDims()` — repositions camera, controls target, grid, and lights to current W/H/D; called on import/load so loaded models frame correctly
- `grid` changed from `const` to `let` so `realignSceneToDims()` can swap it

#### `VoxelAssetStudio/character_hoodlum_0.json`
- `savedAt` timestamp bump only (no structural changes)

### Files Modified
| File | Change |
|---|---|
| `VoxelAssetStudio/voxel_editor.html` | Body Part Paint tool, right-panel tabs, Move Selection, group view mode, mirror highlight fix, half-voxel mirror offset |
| `VoxelAssetStudio/character_pipeline.html` | Additive selection, Save/Export buttons, line algorithm, paste keyboard intercept, realignSceneToDims, Esc clears all |
| `VoxelAssetStudio/character_hoodlum_0.json` | savedAt timestamp bump |

### Testing Notes
- 🧪 **TEST NOW**: Open voxel_editor.html, enable Mirror X, hover with Place tool → should see two highlight boxes (primary + mirrored). Click → both voxels place.
- 🧪 **TEST NOW**: Click ½ button in Mirror panel → red X plane snaps to voxel center. Place on central column → self-mirrors.
- 🧪 **TEST NOW**: Body Part tool (🧩) — select a group in Body Parts tab, click voxels → assigned to group. Toggle group view mode to verify.
- 🧪 **TEST NOW**: character_pipeline.html — Shift+click multiple voxels → selection grows; Shift+click again → removes. Esc → all tool states clear.

---

## August 9, 2026 — Portrait-First Character Pipeline + Animator JSON

### Impact
- Character generation now starts from decoded Gangsters 1998 portrait presets
- Hoodlum 0 model regenerated procedurally as `character_hoodlum_0.json` (2,404 voxels, 6 animation groups)
- Animator correctly loads groups + pivots + params from project JSON

### Changes

#### `character_pipeline.html`
- Removed baked `HOOD0_VOXELS` data and `loadHood0()` function
- Extracted shared `buildHead()` function (used by both bust and full-body generation)
- Added `generateBust()` for portrait head+shoulders preview
- Added `PORTRAIT_CATALOG` with decoded feature presets
- Added portrait catalog UI, `selectPortrait()`, `buildFullBodyFromPortrait()`
- Fixed `setV()` argument bug in hat generation (was 3 args, needed 4)
- Updated hat options to match decoded catalog (Flat Cap, Fedora, Boater, Wide Brim)

#### `character_animator.html`
- Fixed `importStasset()` to load groups, pivots, and animParams when present in JSON (previously defaulted all voxels to group 0)

#### New Files
- `VoxelAssetStudio/character_hoodlum_0.json` — Pre-generated Hood 0 project (2404 voxels)
- `VoxelAssetStudio/gen_hood0.py` — Python script to regenerate the JSON
- `VoxelAssetStudio/CHARACTER_PIPELINE_REFERENCE.md` — Full technical reference

### Files Modified
| File | Change |
|---|---|
| `character_pipeline.html` | Portrait-first refactor, buildHead extraction, setV fix |
| `character_animator.html` | importStasset groups/pivots/params loading fix |

---

## August 9, 2026 — Remove Deprecated Vinny UI + FollowCamera

### Impact
- **Planning mode**: Camera stays on debug hood (HoodSpawner) for animation work — no more Vinny teleport UI clutter
- **Working mode**: Camera smoothly transitions to HQ tenement block in isometric perspective (45° yaw, 35.264° pitch, ortho=8)
- **Working → Planning**: Camera smoothly returns to debug hood character
- **FollowCamera deprecated** — no longer instantiated, marked with deprecation header

### Changes

#### `GameUIController.cs`
- Removed `vinnyPlacementMode` and `vinnyTeleportTargetBlockId` fields
- Removed Vinny teleport hotkey handling from `Update()`
- Removed Vinny placement mode interception from `OnBlockClicked()`
- Removed Vinny button + placement mode UI from `RefreshBlockInfo()`
- Removed entire `VINNY TELEPORT` region (`OnVinnyClicked`, `ExitVinnyPlacementMode`, `TeleportVinnyToBlock`)
- Removed `followCam` field
- Replaced FollowCamera setup with `FocusCameraOnHq()` — focuses map camera on HQ block
- Replaced FollowCamera shutdown with `RestoreCameraFromHq()` — returns camera to debug hood
- Added `FocusCameraOnHq()` and `RestoreCameraFromHq()` helper methods
- Planning phase text no longer shows `[VINNY PLACEMENT]` tag

#### `CityMap3D.cs`
- Updated stale comment referencing FollowCamera

#### `VoxelChunkManager.cs`
- Updated stale comment referencing FollowCamera

#### `FollowCamera.cs`
- Added deprecation header — no longer instantiated, kept for reference

### Files Modified
| File | Change |
|---|---|
| `GameUIController.cs` | Removed Vinny UI + FollowCamera, added HQ camera focus/restore |
| `CityMap3D.cs` | Updated comment |
| `VoxelChunkManager.cs` | Updated comment |
| `FollowCamera.cs` | Added deprecation header |

### Testing Notes
- **Planning mode**: Camera stays on debug hood from HoodSpawner. Press 1-9 to cycle animations as before.
- **Working mode (Run Week)**: Camera smoothly lerps to HQ block in isometric view. Simulation runs as before.
- **Working → Planning**: Camera smoothly returns to debug hood.
- No Vinny teleport button in block info panel anymore.

---

## August 9, 2026 — Animation Fix: Inverse-Transform Sampling + Component Wiring

### Impact
- **Character animations now actually visible** — head turns, arms/legs swing, crouching, flinching all work
- **9 animation states** fully functional (Idle, Walking, Looking, Checking, Aiming, Crouching, Flinching, Falling, Down)
- **Debug key cycling** — press 1-9 in Play mode to test each state
- **No skeleton, no rigging** — purely shader-based per-voxel group transforms in the DDA raymarch

### Root Causes Fixed

#### Bug 1: Shader "Approach A" — output-only offset was invisible
The original `GroupTransformOffset` applied the animation offset to `worldHit` **after** the DDA raymarch found a hit. This only changed the depth buffer write — the screen position and color were unchanged. The character looked identical in every animation state.

**Fix**: Switched to "Approach B" — inverse-transform sampling in the DDA loop. Each DDA voxel position is inverse-transformed to find its rest-space position, and voxel data is sampled there. The ray "sees" voxels at their posed positions, producing visible movement.

#### Bug 2: Missing CharacterAnimation component on spawned hood
`HoodSpawner.SpawnDebugHood()` created a `VoxelCharacter` but never added `CharacterAnimation`. The entire animation pipeline was fed zeros (animState=0=Idle forever).

**Fix**: Added `CharacterAnimation` + `PedestrianLookAround` to spawned hood. Added debug key cycling (1-9) using `UnityEngine.InputSystem.Keyboard.current`.

### Changes

#### `VoxelProxyRaymarch.shader`
- Refactored animation functions: extracted shared `ComputeGroupRotation()` (returns bool — false when state has no transform for that group)
- Added `InverseGroupTransformOffset()` — uses `transpose(rot)` for inverse rotation
- DDA loop now inverse-transforms each voxel to rest space before sampling `_VoxelData`
- Removed output-only `worldHit += offset` (Approach A — dead code)
- Added Aiming (state 4), Crouching (state 5), Flinching (state 6), Falling (state 7) states
- `SmoothNormal` now uses `sampleVoxel` (inverse-transformed) instead of `voxel` (DDA position)

#### `HoodSpawner.cs`
- Added `using UnityEngine.InputSystem`
- Added `CharacterAnimation` component to spawned hood (with `autoDetectWalking = false`)
- Added `PedestrianLookAround` component to spawned hood
- Added `Update()` with debug key cycling: `Keyboard.current[Key.Digit1 + i].wasPressedThisFrame`
- 9 states: Idle, Walking, Looking, Checking, Aiming, Crouching, Flinching, Falling, Down

### Files Modified
| File | Change |
|---|---|
| `VoxelProxyRaymarch.shader` | Approach A → B (inverse-transform DDA sampling), added states 4-7, shared `ComputeGroupRotation` |
| `HoodSpawner.cs` | Added CharacterAnimation + PedestrianLookAround, debug key cycling (Input System) |

### Documentation Updated
| File | Change |
|---|---|
| `VOXEL_ENGINE_GOTCHAS.md` | Added Gotcha #4 (Approach A invisible) + Gotcha #5 (missing CharacterAnimation) |
| `DOCUMENTATION_INDEX.md` | Updated gotchas summary + animation keywords |

### Testing Notes
- Press 1-9 in Play mode to cycle animation states
- Console logs `[HoodSpawner] 🎬 Animation state → [state]` on each key press
- Head should turn during Looking/Checking, arms/legs swing during Walking, etc.
- If no animation: check console for groupID buffer loading log from VoxelChunkManager

---

## August 9, 2026 — Voxel Group Animation (Articulated Limbs Without Skeletons)

### Impact
- **Vinny and all pedestrians can now walk with swinging arms/legs and turn their heads**
- **Zero additional draw calls** — still 1 draw call per asset type (GPU instancing preserved)
- **+8 KB GPU memory** per 500 instances (16→32 bytes/instance) + ~16 KB one-time groupID buffer
- **<1-3ms additional GPU time** — one extra buffer read + ~10 ALU ops per hit voxel in DDA loop
- **Backward compatible** — vehicles and buildings unaffected (no .groups file = no transform)

### Changes

#### 1. Voxel Group Partitioning (`.groups` files)
- Python script partitioned 4 character `.stasset` files into 6 animation groups (head, torso, L/R arms, L/R legs)
- New `.groups` file format (STAG magic, same layout as .stasset, uint16 groupID per voxel)
- Files: `character_hoodlum_0.groups`, `character_civilian_0.groups`, `character_police_0.groups`, `character_hoodlum_overcoat_0.groups`
- Backups created: `*.stasset.bak` for all 4 characters

#### 2. Instance Buffer Expansion (`VoxelChunkManager.cs`)
- `InstancedCharacter`: added `animState`, `animTime`, `animSpeed` fields
- `InstancedGroup`: added `groupIDBuffer` (ComputeBuffer for per-voxel groupIDs)
- `RenderInstancedGroup()`: instance buffer now 2x float4 per instance (pos+yaw, anim+speed)
- `LoadGroupIDs()`: new method to load STAG-format .groups files
- Buffer stride: 16→32 bytes/instance, allocated as `visibleCount * 2` elements
- Binds `_GroupIDs`, `_GroupIDsEnabled`, `_InstanceCount` to shader via MaterialPropertyBlock
- `ReleaseAllInstancedGroups()`: releases groupIDBuffer alongside voxel/instance buffers

#### 3. Shader Group Transforms (`VoxelProxyRaymarch.shader`)
- New bindings: `_GroupIDs` (StructuredBuffer<uint>), `_GroupIDsEnabled` (int), `_InstanceCount` (int)
- `Varyings`: added `animState`, `animTime`, `animSpeed` (TEXCOORD5-7)
- Vertex shader: reads animation data from `_InstanceOffsets[instanceID + _InstanceCount]`
- `GroupTransformOffset()` function: computes per-group rotation offset based on animState/animTime
  - Walking (state 1): arms swing sin(6t)±0.3rad, legs stride sin(6t+π)±0.4rad
  - Looking/Checking (state 2-3): head yaw sin(2t)±0.5rad, pitch sin(1.3t)±0.1rad
  - Pivot points computed from dims: head(0.5,0.78,0.5), arms(0.25/0.75,0.75,0.5), legs(0.375/0.625,0.34,0.5)
- DDA loop: on voxel hit, reads groupID, computes offset, applies to worldHit via `mul(volInvRot, offset)`
- ~~Approach A: transform applied to output position only (depth compositing), not to raymarch itself~~ **SUPERSEDED — see Animation Fix entry above (Approach B: inverse-transform sampling)**
- All 3 instancing paths set anim defaults (BUILDING_INSTANCING=0, character=from buffer, non-instanced=0)

#### 4. Animation Driver (`CharacterAnimation.cs` — new file)
- `AnimState` enum: Idle, Walking, Looking, Checking, Aiming, Crouching, Flinching, Falling, Down
- Auto-detects walking from velocity (configurable threshold)
- Pushes animState/animTime/animSpeed to InstancedCharacter handle each frame
- `SetState()` resets animTime for clean transitions

#### 5. Gangsters-Inspired NPC Behavior (`PedestrianLookAround.cs` — new file)
- Random look-around: 5-15s interval, 2-4s duration, sets state to Looking
- `CoastClearCheck()` coroutine for hoods: sets state to Checking, longer pause
- Same head-turn animation for civilians and hoods = emergent suspicion

#### 6. VoxelCharacter Accessor (`VoxelCharacter.cs`)
- Added `GetInstancedHandle()` public method so CharacterAnimation can access the instanced render handle

### Design Documentation
- `docs/systems/VOXEL_GROUP_ANIMATION.md` — 547-line dedicated design doc (12 sections)
- `COMBAT_VEHICLE_DESIGN.md` — updated with Gangsters design reference + 6-step implementation plan
- `STEEL_CITY_ROADMAP.html` — added Phase 4c (Voxel Group Animation)
- `DOCUMENTATION_INDEX.md` — added VOXEL_GROUP_ANIMATION.md to systems listing + quick lookup

### Files Modified
| File | Change |
|---|---|
| `VoxelChunkManager.cs` | Instance buffer expansion, groupID loading, buffer binding |
| `VoxelProxyRaymarch.shader` | Group transform in DDA loop, anim Varyings, vertex shader reads |
| `VoxelCharacter.cs` | GetInstancedHandle() accessor |
| `RECENT_CHANGES.md` | This entry |

### Files Created
| File | Purpose |
|---|---|
| `CharacterAnimation.cs` | Animation state driver (pushes to GPU via instance buffer) |
| `PedestrianLookAround.cs` | Gangsters-inspired random look-around behavior |
| `*.groups` (4 files) | Per-voxel groupID data for each character model |
| `*.stasset.bak` (4 files) | Backups of original character assets |
| `VOXEL_GROUP_ANIMATION.md` | Design doc (547 lines, 12 sections) |

### Testing Notes
- **Expected behavior**: When CharacterAnimation is attached and state=Walking, Vinny's arms/legs should swing. When state=Looking, head should turn left/right.
- **Without CharacterAnimation**: Characters render as before (animState=0=Idle, no group transforms applied since _GroupIDsEnabled=0 for groups without .groups files... wait, _GroupIDsEnabled is set per-group based on whether groupIDBuffer exists. Characters WITH .groups but WITHOUT CharacterAnimation will have _GroupIDsEnabled=1 but animState=0, so GroupTransformOffset returns float3(0,0,0) for all groups. Safe.)
- **Vehicles**: No .groups files → groupIDBuffer=null → _GroupIDsEnabled=0 → no transform. Safe.
- **Buildings**: BUILDING_INSTANCING path → anim fields set to 0 → no transform. Safe.

---

## August 9, 2026 — Terrain Sector Baking + Collision World Flat Array Optimization

### Impact
- **Terrain load: 78,466ms → 371ms** (211x faster)
- **Total BuildMap: ~79s → ~0.5s** (estimated 158x faster)
- **Draw calls: 100 terrain chunks → 1 sector (1 draw call)**
- **ComputeBuffers: 100 → 1** for terrain
- **GameObjects: 100 → 0** for terrain (no transform hierarchy overhead)
- **Memory: ~500MB dictionary overhead → 13.3MB flat byte array** for collision world

### Changes

#### 1. Terrain Sector Baking (`CityMap3D.cs:868-935`)
Replaced 100 sequential `LoadChunkFromData` calls (each creating a `ComputeBuffer`, `SetData`, `ComputeTightAABB`, and `GameObject`) with a single sector bake:
- All 100 terrain chunks concatenated into one flat `uint[]` buffer (13.9M voxels)
- Per-chunk metadata `(bufferOffset, dims, worldOffset)` stored in `Vector4[]` arrays
- Registered via `RegisterSector("terrain_sector", ...)` — 1 `ComputeBuffer`, 1 `SetData`, 1 draw call
- No `ComputeTightAABB` needed (terrain AABB is full bounds — flat 2-voxel slab)
- No `GameObject` creation (raymarch shader doesn't need transform hierarchy)

#### 2. Collision World Flat Array (`VoxelCollisionWorld.cs`)
**Root cause of 78s bottleneck**: `Dictionary<Vector3Int, byte>` for 13.9M voxel inserts.
- Each insert: `Vector3Int` hash computation + bucket probe + collision chain + dictionary resizing (~24 resizes to grow to 13.9M entries)
- Replaced with flat `byte[]` array indexed by `x + y*gridW + z*gridW*gridH`
- Each write is now `array[idx] = value` — O(1), no hashing, no resizing
- Grid grows dynamically in all directions (handles negative offsets by shifting origin)
- Lookups (`ProbeGround`, `HasGroundAt`) also O(1) array index with bounds check
- Memory: 13.9M bytes (13.3MB) vs ~500MB+ dictionary entry overhead

### What This Enables Next
- **Larger cities**: 500-1000 block cities now feasible (terrain was the bottleneck, not buildings)
- **Faster iteration**: Sub-second reload enables rapid testing of layout/visual changes
- **More GPU headroom**: 99 fewer draw calls and 99 fewer ComputeBuffers frees GPU for characters/vehicles
- **Potential for async terrain**: With collision registration no longer blocking, terrain generation could be moved to a background thread entirely

---

## ⚠️ REMINDER: City Scale Testing — TWO Files Must Change Together

When testing different city sizes (25/100/500/1000 blocks), you MUST copy **both** files from `StreamingAssets/`:

| File | Used By | Controls |
|------|---------|----------|
| `city_template_NN.json` → `city_template.json` | `DataLoader` → `GameEngine.Setup()` | Game logic: blocks, businesses, NPCs, police, gangs |
| `city_layout_NN.json` → `city_layout.json` | `CityMap3D.LoadCityLayout()` | Visuals: .stasset building placement, voxel rendering |

**If only one is updated**, the engine block count won't match the visual layout — e.g., 500 layout with 100 template produces a 10x10 city despite the log saying "500 blocks loaded".

```powershell
# Example: switch to 500 blocks
Copy-Item "SteelCityMobSim\Assets\StreamingAssets\city_template_500.json" "SteelCityMobSim\Assets\StreamingAssets\city_template.json" -Force
Copy-Item "SteelCityMobSim\Assets\StreamingAssets\city_layout_500.json" "SteelCityMobSim\Assets\StreamingAssets\city_layout.json" -Force
```

Available tiers: `city_template_25`, `city_template_100`, `city_template_500`, `city_template_1000` (and matching `city_layout_*`).

---

## August 8, 2026 — Tenement Block 0 Final Redesign (Dual FE + Roof)

### Deployed
- `Assets/StreamingAssets/voxel_buildings/tenement_block_0.stasset` — **Replaced** with final version (96×60×96, 95,702 non-air voxels)
- `Assets/StreamingAssets/voxel_buildings/tenement_block_0_original_backup.stasset` — Backup of original

### Created
- `VoxelAssetStudio/extend_landings.py` — Extend FE landings to 2-window coverage per landing
- `VoxelAssetStudio/mirror_fe.py` — Mirror FE across X axis (back-left → back-right)
- `VoxelAssetStudio/add_side_fe.py` — Duplicate + rotate FE 90° onto adjacent wall (front-left)
- `VoxelAssetStudio/add_roof_deco.py` — Add water tower + parapet wall to roof
- `VoxelAssetStudio/analyze_fe_landings.py` — Detailed landing/window analysis
- `VoxelAssetStudio/load_firework.py` — Load user-edited firework JSON with roof buffer

### Changed
- `VoxelAssetStudio/voxel_editor_html.py` — Added 3-axis slice controls (X/Y/Z min/max sliders)
- `VoxelAssetStudio/procedural_mob_buildings.py` — Added `roof_buf=8` parameter to `generate_apartment_block()`
- `VoxelAssetStudio/json_to_stasset.py` — Fixed `save_stasset()` call (removed unsupported `scale` kwarg)
- `Assets/docs/VOXEL_EDITOR_AND_FIRE_ESCAPE.md` — Added sections: 3-axis slicer, grep gotcha, landing extension, mirroring, 90° rotation, roof buffer, updated alignment table and script/file listings

### Tenement Final Specs
- **Dimensions**: 96×60×96 (was 96×44×96 — +16 roof buffer)
- **Core**: 80×80 with 8-voxel side buffer
- **Fire escapes**: 2 (back-right + front-left, mirrored + rotated)
- **Landing Y levels**: 10, 18, 26, 34 (2 below window sills at 12, 20, 28, 36)
- **Landing coverage**: 2 windows per landing (X=62-75 back wall, Z=10-36 left wall)
- **Roof**: Water tower (8×8 wood tank on iron legs, Y=44-51) + parapet wall (2v brick, Y=44-45)
- **Total voxels**: 95,702 non-air

---

## August 8, 2026 — Voxel Editor Enhancements + Fire Escape Redesign

### Created
- `Assets/docs/VOXEL_EDITOR_AND_FIRE_ESCAPE.md` — Full documentation of voxel editor HTML system and fire escape workflow
- `VoxelAssetStudio/shift_fe.py` — Shift fire escape JSON voxels by Y offset
- `VoxelAssetStudio/fix_fe_spacing.py` — Full pipeline: fix spacing + regenerate tenement + bolt on fire escape
- `VoxelAssetStudio/bolt_fe_v2.py` — Earlier bolt-on version (80×80 core, 8v buffer)
- `VoxelAssetStudio/analyze_fe.py` — Analyze fire escape JSON (bounding box, Y distribution, landings)
- `VoxelAssetStudio/load_fe_test.py` — Load fire escape JSON into editor for inspection

### Changed
- `VoxelAssetStudio/voxel_editor_html.py` — Enhanced with:
  - **Escape key**: Universal reset — clears all tool states, switches to camera mode
  - **Camera tool**: New tool mode (gray highlight) with early returns in `performTool` and `updateHighlight`
  - **Volume expansion**: Dynamic grid resizing via modal (W/H/D changed from `const` to `let`, `gridHelper` to `let`)
  - **Enhanced selection**: Shift+click for single voxel, Ctrl+click for box select, flood-fill default
  - **Ruler tool**: White highlight with voxel count in status bar
  - Updated `TOOL_COLORS` and `TOOL_DESC` dictionaries
- `Assets/docs/DOCUMENTATION_INDEX.md` — Added Voxel Editor category (Section 3b), updated key file locations, version 1.4.0

### Tenement Buffer Change
- **Core**: 88×88 → 80×80 voxels (regenerated with `generate_apartment_block(w=80, d=80)`)
- **Buffer**: 4 → 8 voxels each side (enables 7-voxel-deep fire escape + future decorations)
- **Total footprint**: 96×96 voxels (unchanged — fits game map exactly)
- **BuildingVoxelWidth=32** in `CityMap3D.cs` constrains total to 96×96

### Fire Escape Alignment
- Landings at Y=10, 18, 26, 34 (2 below window sills, 8-voxel spacing)
- Drop ladder with guide rails (street to first landing)
- Roof ladder (top landing to roof, per 1860 NYC ordinance)
- Support posts: vertical iron posts from ground to first landing
- Materials: DARK_IRON (109) structure, PAINTED_METAL (111) railings

### Output
- `tenement_block_0_new_fe.stasset` — 96×52×96, script-generated intermediate version

---

## August 8, 2026 — Instanced Box-Beam Path Debug Rendering + Camera Fix

### Created
- `Assets/Shaders/InstancedColor.shader` — Unlit transparent instanced shader with `_Color` property for per-batch coloring
- `docs/systems/PATH_DEBUG_RENDERING.md` — Documents the CommandBuffer-based instanced beam rendering pipeline, camera hookup gotcha, batching strategy, and troubleshooting

### Changed
- `Assets/Scripts/Sim/PathDebugRenderer.cs` — Complete rewrite from LineRenderer to instanced box beams
  - Uses `CommandBuffer.DrawMeshInstanced` to composite beams into the voxel render texture
  - Per-type batching (Pedestrian/Car/Trolley) with single color per draw call (3 draw calls max)
  - Sorts `activePaths` by type for contiguous batch ranges
  - Reusable `batchBuffer` with `Array.Copy` (no per-frame GC allocation)
  - `RenderBeamsIntoCamera(Camera externalCam = null)` accepts camera from bridge
  - Fallback path in `Update()` when no `VoxelRenderBridge` present
  - Comprehensive diagnostic logging (every 60 frames): path state, batch counts, draw call counts
- `Assets/Scripts/UI/VoxelRenderBridge.cs` — Passes `_camera` to `RenderBeamsIntoCamera()` instead of relying on `Camera.main`
  - Added diagnostic logging for PDR instance status
- `Assets/Scripts/UI/VoxelChunkManager.cs` — Removed unused perf tracking fields (`perfLastActiveChunks`, etc.)

### Bug Fixed
- **Vehicle path beams not emitting**: `PathDebugRenderer` used `Camera.main` to find the render camera, but the voxel render camera (owned by `VoxelRenderBridge`) isn't tagged "MainCamera" in URP. Fix: `VoxelRenderBridge` passes its camera reference directly to `RenderBeamsIntoCamera(_camera)`.
- **Color bleeding across path types**: Setting `_Color` directly on `beamMaterial` caused all `CommandBuffer.DrawMeshInstanced` calls to use the last color set (deferred execution). Fix: Use `MaterialPropertyBlock` per draw call — same pattern as the instanced character MaterialPropertyBlock bug.
- **Pedestrian paths not shown by default**: `StressTestSpawner` started with beams off (level 0). Fix: Default to ALL level, auto-register after spawn, and periodically register agents that acquire paths async.

### Testing
- Press **F10** to toggle vehicle driving. Purple beams should appear showing the planned route.
- Console shows `[PathDebug]` diagnostic logs every 60 frames confirming active paths, batch counts, and draw calls.
- Beams composite on top of the voxel raymarch render (visible through the RawImage overlay).

---

## August 7, 2026 — Vehicle System: Generalized Instancing + RoadGraph + 1920s Touring Car Model

### Created
- `Assets/Scripts/Sim/RoadGraph.cs` — Vehicle pathfinding graph (street intersections as nodes, links between neighbors)
  - `GenerateFromLayout` builds a lattice grid of intersections aligned with city blocks
  - `RandomNodeId` / `RandomNeighbor` for basic random-walk navigation
- `Assets/Scripts/Sim/VoxelVehicle.cs` — Voxel vehicle component (analogous to VoxelCharacter)
  - Loads .stasset, registers with VoxelChunkManager's per-asset InstancedGroup
  - Uses `transform.localPosition` for mapRoot coordinate space consistency
  - `PlaceAtCenter` for external movement control
- `Assets/Scripts/Sim/VehicleTestSpawner.cs` — Test harness + VehicleAgent
  - F9 spawns N vehicles that randomly drive between RoadGraph intersections
  - VehicleAgent does endless random walk (pick random neighbor, drive there, repeat)
  - No AI/destination logic — pure navigation + rendering test
- `VoxelAssetStudio/procedural_mob_vehicles.py` — 1920s vehicle voxel generator
  - `generate_touring_car`: Ford Model T style touring car (20x16x30 voxels)
  - Open-top 4/5 seater: 2 bench seats (driver + 1 front, 2 rear passengers)
  - Artillery wheels (wooden spokes + iron tires), brass headlights, radiator grille
  - Running boards, fenders, spare tire, folding top supports
  - Interior sized to fit 4 characters at 0.05m/voxel scale
- `Assets/StreamingAssets/voxel_buildings/vehicle_civilian_car_0.stasset` — Exported voxel model (2,485 solid voxels, 19KB)
- `docs/systems/DYNAMIC_OBJECT_RENDERING_TIERS.md` — Three-tier rendering philosophy document

### Changed
- `Assets/Scripts/UI/VoxelChunkManager.cs` — Generalized instanced character/vehicle rendering
  - Replaced singular shared buffer with `Dictionary<string, InstancedGroup>` keyed by asset filename
  - Each asset type gets its own shared voxel buffer + instance offset buffer + draw call
  - `RegisterInstancedCharacter` / `UnregisterInstancedCharacter` / `RenderInstancedCharacters` updated
  - `ReleaseAllInstancedGroups` for cleanup

### Testing
- Press **F9** in Play mode to spawn test vehicles on the RoadGraph
- Vehicles should render via the generalized InstancedGroup system and drive randomly between intersections
- Vehicle model: 20x16x30 at 0.05m/voxel = 1.0m x 0.8m x 1.5m mob sim scale
- **Auto-spawn**: VehicleTestSpawner auto-spawns on Start (parked, not moving). Vehicle appears at the road intersection nearest to player HQ (Vinny's office), visible during planning phase. CityMap3D.SpawnSceneCharacters adds the spawner to the scene if not present.
- **F10 = toggle driving** (F9 conflicts with StressTestDiagnostics stop key). Press F10 once to start driving, press again to park.

---

## August 6, 2026 — Camera Controls, Perspective Rendering Fix, Sim Simplification

### Created
- `Assets/Scripts/Sim/FollowCamera.cs` — Follow camera with full debug controls
  - Spherical coordinate camera positioning (yaw, pitch, distance, height)
  - OnGUI debug HUD showing live camera metrics (distance, height, yaw, pitch, FOV, aim point, cam position)
  - Hotkeys: Arrow keys (orbit yaw/pitch), Q/E (distance), R/F (height), +/- (FOV)
  - Free-look mode (hold Left Shift): arrows rotate camera view in-place, offsets persist on release
  - Z key resets look offsets to zero
  - C key captures all camera metrics to console log (copy-pasteable)
  - H key toggles HUD visibility
  - Automatically hides all game UI panels on init, restores on shutdown (raymarch overlay preserved)
  - VoxelRenderBridge integration: swaps VoxelChunkManager render camera, attaches own bridge
- `Assets/Scripts/Sim/SimulationManager.cs` — Pure logic simulation manager (replaces TickSimulation)
  - Decoupled from rendering, produces SimEvents consumed by EventPlayer
  - "stand" order type: sets state to Idle (sim stays active for camera debugging)
- `Assets/Scripts/Sim/EventPlayer.cs` — Consumes SimEvents, drives visual updates
- `Assets/Scripts/Sim/SimEventStream.cs` — Event stream with SimEvent factory methods
- `Assets/Scripts/Sim/Pathfinder.cs` — A* pathfinding on WaypointGraph
- `Assets/Scripts/Sim/WaypointGraph.cs` — Waypoint graph with sidewalk/crosswalk/jaywalk links
- `Assets/Scripts/Sim/VoxelCharacter.cs` — Voxel character with WorldCenter property for camera aiming
- `Assets/Scripts/Sim/TickHUD.cs` — HUD overlay during working week
- `Assets/Scripts/Sim/VoxelCollisionWorld.cs` — Voxel-based collision world
- `Assets/Scripts/Sim/BuildingOrientation.cs` — Building orientation helper

### Changed
- **Perspective rendering fix**: `VoxelChunkManager.cs` now uses `renderCamera.cameraToWorldMatrix` instead of `Matrix4x4.TRS()` for the camera-to-world matrix. The previous TRS approach used Unity's transform convention (+Z forward) but the inverse projection matrix produces view-space coordinates (-Z forward), causing perspective rays to fire backward — only the massive terrain volume got accidentally hit.
- **Compute shader fix**: `MobSimVoxelRaymarch.compute` — ortho ray direction changed from +Z to -Z (view space convention), perspective clip space Z range corrected from 0..1 to -1..+1 (Unity NDC convention). Buildings, characters, and decorations now render correctly in both ortho and perspective modes.
- `GameEngine.cs` — Reduced player to 1 hood (Vinny Moretti) and rival to 1 hood for simplified testing
- `GameUIController.cs` — Auto-assigns "stand" order on Run Week (no clicking required), passes VoxelCharacter to FollowCamera.Initialize for center-based aiming, detailed camera transition logging
- `CityMap3D.cs` — Updated accessor comments for SimulationManager/EventPlayer architecture
- `SimEventStream.cs` — Moved static factory methods to SimEvent class

### Camera Control Hotkeys
| Key | Action |
|-----|--------|
| Arrow Left/Right | Orbit yaw (rotate around target) |
| Arrow Up/Down | Orbit pitch (angle above target) |
| Q / E | Zoom in/out (distance) |
| R / F | Raise/lower height |
| + / - | Narrow/widen FOV |
| Left Shift (hold) | Free-look mode (arrows = look around in-place) |
| Z | Reset look offsets to zero |
| C | Capture all metrics to console log |
| H | Toggle debug HUD |

---

## August 6, 2026 — Playtesting Insights from Manual Study

### Created
- `docs/systems/PLAYTESTING_INSIGHTS.md` — Comprehensive insights from original game manual study + live playtesting
  - Fear/Hostility/Squeal three-axis model (fear increases squealing at high levels)
  - Extortion mechanics (intimidation skill only, distance from nearest office, manpower, protection as service contract)
  - Information tiers for squealer identification (Lawler-gated, conditional reports, indirect detection)
  - Territory strategy ("baby and scare" your territory, attack rival territory, donate in neutral territory)
  - Legal system chain (post-arrest: Lawyer defends, bribe Judge/DA, intimidate witnesses/jurors)
  - Illegal business front-matching (similarity rule, confirmed business types)
  - Diplomacy system (five levels, snitches as limited resource)
  - Open questions for further playtesting

### Updated
- `docs/systems/CRIME_SQUEAL.md` — Added: Fear Trap (high fear increases squealing), Information Tiers for Squealer ID, Legal System Chain, conditional reports pattern, indirect detection methods, fear diminishing returns design note
- `docs/systems/EXTORTION_TERRITORY.md` — Added: Key Extortion Factors table (intimidation only, not intelligence), Office Proximity as territorial strategy, Protection as Service Contract (not permanent), Territory Strategy (baby/scare/attack/donate), Fear Trap cross-reference
- `docs/systems/INTELLIGENCE_TERRITORY.md` — Added: Information Infrastructure Requirements (Lawyer-gated squealer ID), conditional reports pattern, indirect detection without Lawyer, information asymmetry as intentional design

### Context
- Studied the official Gangsters: Organized Crime manual (`manual_text.txt`, 4085 lines) page by page
- Cross-referenced manual findings with binary analysis and existing Steel City design docs
- Confirmed most Steel City design decisions are correct; refined with new mechanical details
- Key new insight: Fear has a negative return on squeal suppression at high levels — over-intimidating is as dangerous as under-intimidating

---

## August 2, 2026 — Project Initialization

### Created
- Project directory structure (`SteelCityMobSim/`)
- `.gitignore` — Python, build output, saves, decoded source data
- `README.md` — Project overview, design principles, core loop, structure
- `DOCUMENTATION_INDEX.md` — Central doc hub with navigation
- `docs/core/DESIGN_PHILOSOPHY.md` — 5 founding principles
- `docs/core/SOURCE_GAME_ANALYSIS.md` — Full analysis of decoded .xtx files
- `docs/systems/SYSTEMS_OVERVIEW.md` — System interaction map, core loop, priority
- `docs/systems/CHARACTER_SYSTEM.md` — Hoods (skills, INT, loyalty) + Citizens (fear/hostility/squeal)
- `docs/systems/EXTORTION_TERRITORY.md` — Core loop, refusal chain, territory strength, info tiers
- `docs/systems/INTELLIGENCE_TERRITORY.md` — Territory-based fog of war, squealer pipeline, business radar
- `docs/systems/CORRUPTION_POLICE.md` — Beat cops, simple bribe mechanic, geographic coverage
- `docs/systems/COMBAT_AUTOBATTLE.md` — Auto-resolved combat, INT as tactical AI, combat log
- `docs/systems/CRIME_SQUEAL.md` — Crime table, squeal events, investigation leads, escalation ladder
- `docs/data/GAME_DATA_REFERENCE.md` — All extracted values from decoded original game data

### Updated
- `docs/systems/3D_CITY_RENDERING.md` — Added interactive Working Week design
  - Tactical overrides (flee, reinforce, abort, attack, hold ground, lie low)
  - Pause system (spacebar toggle, real-time + paused modes, speed controls)
  - Time-sliced simulation architecture (bidirectional: sim → render → player input → sim)
  - Radial menu HUD for mid-week hood orders
  - Updated data flow to reflect player input bridge

### Context
- All 30 .xtx files from Gangsters: Organized Crime decoded (4-byte XOR key: 0xAF, 0xDE, 0xDE, 0xFA)
- Visual data codex generated at `gangsters_decoded/index.html`
- Design philosophy established: simple mechanics, complex interactions
- Core systems conceptualized through design discussion
- Ready to begin prototyping

---

## August 3, 2026 — Unity Port + 3D Voxel Rendering

### Created
- C# simulation engine ported from Python prototype:
  - `GameEngine.cs`, `City.cs`, `NPC.cs`, `CrimeSystem.cs`, `EconomySystem.cs`, `RivalAI.cs`, `EventStream.cs`
  - `DataLoader.cs`, `DataModels.cs`, `JSONParser.cs` (custom JSON parser for dict support)
  - `GameBootstrap.cs` — Unity MonoBehaviour entry point
- 3D voxel rendering pipeline:
  - `MobSimVoxelRaymarch.compute` — GPU raymarching compute shader (DDA traversal, per-voxel material colors)
  - `VoxelChunkManager.cs` — chunk-based compute shader dispatch, frustum culling, depth buffer
  - `VoxelSun.cs` — dynamic sun position, day/night cycle, lighting presets (dawn/noon/dusk/night)
  - `CityMap3D.cs` — camera system (LMB focus, MMB rotate, RMB pan, wheel zoom), UI integration
  - `GameUIController.cs` — tabbed UI (Hoods, Block, Orders, Finance, Police, Invest, Log)
  - `StAssetReader.cs` — runtime .stasset file loading
  - `VoxelBuildingMeshifier.cs` — voxel-to-mesh conversion (legacy, now superseded by raymarch)
- Voxel asset generation:
  - `procedural_mob_buildings.py` — 1920s building generators (apartments, barber, bakery, butcher, diner, garage, casino, speakeasy, HQ, police station, empty land)
  - `generate_city_assets.py` — city layout generator (reads template, exports .stasset files)
  - `mob_materials.py` — 1920s material palette (brick, wood, concrete, glass, neon, cobblestone, etc.)
- Documentation: UI setup guide, tabbed layout, gotchas, building methodology, scale standard, porting notes

### Verified
- 5-week automated simulation test passes in Unity console
- City: 9 blocks, 16 businesses, 118 NPCs, 2 police officers
- All systems functional: extortion, squeal, investigations, rival AI, economy, territory

---

## August 4, 2026 — Raymarch-Only Rendering + Lighting/Shadow Debug + Repo Detangle

### Changed
- **Raymarch-only rendering**: Removed all mesh-based rendering from `CityMap3D.cs` (BuildVoxelBlock, BuildCubeBlock, BuildRoadNetwork, etc.). Raymarch compute shader is now the sole renderer.
- **Hybrid normals (Option B)**: `MobSimVoxelRaymarch.compute` now uses DDA face normal for top/bottom surfaces (uniform flat ground — no edge-vs-center gradient), and blends with SmoothNormal for side faces (soft wall shading). Fixes brightness inconsistency on flat ground.
- **Shadow debug controls**: Added `_ShadowEnabled`, `_ShadowNormalNudge`, `_ShadowLightNudge`, `_ShadowSkipSteps`, `_ShadowMaxSteps` shader uniforms. Exposed as UI toggles/sliders in GameUIController.
- **Lighting component toggles**: Added `_SunLightEnabled`, `_AmbientEnabled`, `_FillEnabled`, `_CamLightEnabled` shader uniforms. Each lighting term can be independently toggled live via UI.
- **Shadow ambient + safety floor**: Shadowed areas retain full ambient + fill; only sun component modulated by shadowFactor. Safety floor prevents pure black.
- **Rubble decorations**: `generate_empty_land` now adds 20 random 2×2×1 stone clusters at Y=1 on empty land plots.
- **Repo detangle**: Moved Unity project + VoxelAssetStudio out of SteelTide repo into SteelCityMobSim repo. Flattened UnityProject/ to repo root. Updated `generate_city_assets.py` path references.
- Updated all documentation to reflect current codebase.

### Files Modified
- `Assets/Resources/Shaders/MobSimVoxelRaymarch.compute` — hybrid normals, shadow/lighting debug uniforms, parameterized shadow ray
- `Assets/Scripts/UI/VoxelChunkManager.cs` — shadow/lighting debug fields, shader property IDs, Set/Get methods
- `Assets/Scripts/UI/CityMap3D.cs` — proxy API for shadow/lighting debug params, raymarch-only rendering
- `Assets/Scripts/UI/GameUIController.cs` — shadow/lighting debug UI toggles and sliders
- `VoxelAssetStudio/procedural_mob_buildings.py` — rubble decorations in generate_empty_land
- `VoxelAssetStudio/generate_city_assets.py` — path references updated for new repo structure
