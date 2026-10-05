# Recent Changes — Steel City: Mob Sim

**Last Updated**: October 4, 2026 (Debug HUD Keys tab + runtime spawn)

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
