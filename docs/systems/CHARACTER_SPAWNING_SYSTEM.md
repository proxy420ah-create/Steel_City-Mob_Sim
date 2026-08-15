# Character Spawning & Instancing System

**Last Updated**: 2026-08-14
**Status**: Active — reflects current codebase implementation

---

## 1. Overview

Steel City uses a **GPU instanced voxel character system** where a single model file (`Civilian1.json`) is shared across all character entities in the game world. Each entity gets its own position, rotation, animation state, and clothing remap — but the underlying voxel data is uploaded to GPU once and shared.

This document covers:
- What gets spawned and why
- The four spawner scripts and when to use each
- The GPU instancing pipeline (how one model becomes many entities)
- Component stack on a character entity
- Asset file format and location
- Voxel scale standards
- How to properly set up a new character entity

---

## 2. Current State

### Single Model, Many Instances

There is currently **one character model file**: `Civilian1.json` (Vinny).

All characters in the game world — civilians, hoods, police — are instances of this single model. They are differentiated at runtime by:
- **Position** (world offset)
- **Rotation** (yaw)
- **Animation state** (Idle, Walking, Aiming, etc.)
- **Clothing remap** (per-instance material overrides via region IDs)

When multiple character types are needed (distinct body shapes, hood variants, police uniforms), additional model files will be added to `StreamingAssets/voxel_characters/`. Each new file gets its own `InstancedGroup` with a separate shared voxel buffer.

### Voxel Scale Standards

| Asset Type | Voxel Size | Current Dims | Purpose |
|------------|-----------|--------------|---------|
| Building | 0.1m/voxel | 96×68×96 | City structures |
| Character | 0.01m/voxel | 96×96×96 | All character entities |
| Item/Decor | 0.01m/voxel | 24×16×8 (proposed) | Weapons, props, decorations |

**Character resolution note**: Civilian1 was upscaled from 48³ to 96³ (2× nearest-neighbor) on 2026-08-14 to fix raymarch see-through artifacts during leg animation. The physical size is unchanged (96 × 0.01m = 0.96m). The `voxelSize` parameter was halved from `0.02f` to `0.01f` to maintain the same world-space size while doubling voxel density per axis.

---

## 3. Component Stack

A fully-configured character entity has these components on a single GameObject:

```
GameObject
├── VoxelCharacter        — Core: loads asset, registers with GPU, handles gravity
├── CharacterAnimation    — Drives animation state (10 states), auto-detects walking
├── ClothingSystem        — Per-instance clothing remap (auto-added for .json assets)
└── PedestrianLookAround  — Random head-turn behavior (optional, for NPCs)
```

### Component Details

#### VoxelCharacter (`Assets/Scripts/Sim/VoxelCharacter.cs`)
- **Purpose**: Core component. Loads the .json/.stasset file, creates/reuses the GPU buffer, registers with `VoxelChunkManager` for raymarch rendering, handles gravity and ground snapping.
- **Key fields**:
  - `assetFileName` — filename in `StreamingAssets/voxel_characters/` (default: `"Civilian1.json"`)
  - `voxelSize` — world units per voxel (default: `0.01f`, must match authoring: 96³ model at 0.01m = 0.96m tall)
  - `useInstancing` — if true, uses GPU instancing (shared buffer, 1 draw call for all instances of same asset). If false, creates a dedicated ComputeBuffer (non-instanced mode, for testing).
  - `centerPosition` — world-space center of the character volume
  - `useWorldPosition` — if true, position is world-space; if false, local-space relative to parent
- **Lifecycle**: `Start()` → `LoadAsset()` → `ApplyCenterPosition()` → `RegisterInstancedWithManager()` → `LoadAndApplyAnimParams()` → auto-adds `ClothingSystem` for .json assets
- **Ground probe**: Probes downward from feet via `VoxelCollisionWorld.ProbeGround()`. Snaps to ground within `snapDistance`, applies gravity otherwise.

#### CharacterAnimation (`Assets/Scripts/Sim/CharacterAnimation.cs`)
- **Purpose**: Drives per-group limb transforms on the GPU by updating `animState`, `animTime`, and `animSpeed` on the `InstancedCharacter` handle.
- **Animation states** (must match shader `GroupTransformOffset` logic):

| ID | State | Description |
|----|-------|-------------|
| 0 | Idle | Standing still |
| 1 | Walking | Walking cycle — arms/legs swing |
| 2 | Looking | Head looking around — scanning environment |
| 3 | AimWalk | Aiming pose (upper body) + walking gait (lower body) |
| 4 | Aiming | Static aiming pose — arms raised |
| 5 | Crouching | Legs bent, arms forward |
| 6 | Flinching | Hit reaction |
| 7 | Falling | Falling/knocked down |
| 8 | Down | Lying down / defeated |
| 9 | T-Pose | Bind/rest pose — no rotations applied |

- **Auto-detect walking**: If `autoDetectWalking = true`, measures horizontal velocity and switches between Idle/Walking automatically. Walk speed is clamped to 0.5–2.0× based on velocity magnitude.

#### ClothingSystem (`Assets/Scripts/Sim/ClothingSystem.cs`)
- **Purpose**: Per-instance material remapping. Each instance of the same model can wear different clothing without affecting other instances.
- **How it works**: Uses the `regionIDBuffer` (per-voxel region tags from the .json) and `instanceMaterialRemapBuffer` (per-instance region→material overrides) to swap materials on GPU during the CSPose compute shader pass.
- **Regions** (from Civilian1.json):
  - 0 = Skin (exposed skin — arms, neck, hands)
  - 1 = Face (never clothing)
  - 2 = Hair
  - 3 = Torso (suit/shirt)
  - 4 = Arms (sleeves)
  - 5 = Hands (skin or gloves)
  - 6 = Legs (pants)
  - 7 = Feet (shoes/boots)
- **Presets**: Naked, Suit Blue, Suit Brown, etc. — defined in code, applied via `SetInstanceOutfit()`.
- **Auto-added**: `VoxelCharacter.Start()` auto-adds `ClothingSystem` when `assetFileName` ends with `.json` and `useInstancing = true`.

#### PedestrianLookAround (`Assets/Scripts/Sim/PedestrianLookAround.cs`)
- **Purpose**: Gangsters-inspired NPC behavior — randomly stops to look around. Innocent pedestrians and criminal hoods share the same head-turn animation, creating emergent suspicion (can't tell them apart until a crime is committed).
- **Timing**: Random interval 5–15s between look events, 2–4s look duration.
- **Optional**: Disable for hoods using crime-AI triggered checks.

---

## 4. Spawner Scripts

There are **four spawner scripts**, each for a different use case:

### 4.1 HoodSpawner (`Assets/Scripts/Sim/HoodSpawner.cs`)
**Use case**: Debug single-hood spawning for visual verification.

- Spawns one character on `Start()` (if `debugSpawnOnStart = true`)
- Finds an empty plot block in the city layout (all `empty_land` stassets)
- Ground-probes to find surface Y
- Creates GameObject with: `VoxelCharacter` + `CharacterAnimation` + `PedestrianLookAround`
- Camera auto-focuses on spawned hood at ortho size 4
- **Debug hotkeys**: 1–9 cycle animation states, 0 = Idle
- `autoDetectWalking = false` (manual state control for debugging)

### 4.2 CharacterRig (`Assets/Scripts/Sim/CharacterRig.cs`)
**Use case**: GPU instanced character with hotkey control — simplest setup for testing instanced rendering.

- Uses fixed `spawnPosition` (no ground probe, no city layout dependency)
- Creates `VoxelCharacter` (instanced) + `CharacterAnimation` on same GameObject
- `useInstancing = true`, `useWorldPosition = false`
- **Debug hotkeys**: T/I/W/L/A/C for states, Space = play/pause, +/- = speed
- Only one rig processes input at a time (`Controllable` property + static `ActiveRig`)

### 4.3 AnimationTestSpawner (`Assets/Scripts/Sim/AnimationTestSpawner.cs`)
**Use case**: CPU forward-transform animation testing — poses voxels on CPU and uploads per-frame. Does NOT use GPU instancing.

- Loads .json or .stasset directly into CPU arrays
- Uses `VoxelCharacterAnimator` to pose voxels on CPU each frame
- Uploads posed data to a dedicated (non-shared) ComputeBuffer
- Registers as a named volume with `VoxelChunkManager` (non-instanced path)
- **Debug hotkeys**: T/I/W/L/A/C for states, Space = play/pause, +/- = speed, R = reload files from disk
- Useful for verifying animation math without GPU compute complexity

### 4.4 StressTestSpawner (`Assets/Scripts/Sim/StressTestSpawner.cs`)
**Use case**: Performance testing — spawns N instances with real A* pathfinding.

- **Activation**: F8 key or `RunTest()` call
- Spawns `characterCount` (default 100) instances with staggered delay (0.05s)
- Each agent paths to a random target block via `WaypointGraph` + `Pathfinder`, "extorts" for 2s, paths home, self-destructs
- Time-sliced pathfinding (max 8 paths/frame) to avoid spikes
- **Debug**: F7 cycles path beam visibility (0/5/10/25/50/100/all)
- Uses legacy `character_hoodlum_0.stasset` by default (update to `Civilian1.json` for current assets)

### Spawner Selection Guide

| Goal | Use |
|------|-----|
| Quick visual check of a character | HoodSpawner |
| Test GPU instanced rendering | CharacterRig |
| Verify animation math on CPU | AnimationTestSpawner |
| Performance/load testing | StressTestSpawner |
| Production spawning (future) | TBD — likely a new `PopulationSpawner` that reads city population data (2000 civilians, 400 police) and spawns at building/sidewalk waypoints |

---

## 5. GPU Instancing Pipeline

### How One Model Becomes Many Entities

```
Civilian1.json (96×96×96 = 884,736 voxels)
        │
        ▼
VoxelChunkManager.RegisterInstancedCharacter()
        │
        ├── First call for this asset:
        │   ├── Load .json via CharacterJsonLoader
        │   ├── Create InstancedGroup
        │   ├── Upload sharedVoxelBuffer (884,736 uints, ~3.5MB GPU)
        │   ├── Upload groupIDBuffer (per-voxel animation group tags)
        │   ├── Upload regionIDBuffer (per-voxel clothing region tags)
        │   └── Cache in instancedGroups[assetFileName]
        │
        └── Subsequent calls (same asset):
            └── Reuse existing InstancedGroup, just add new InstancedCharacter entry
```

### Per-Frame Render Flow

1. **Collect visible instances** — cull by distance, frustum, visibility flag
2. **Build instance offset buffer** — per-instance: worldOffset (float3), yaw (float), visible
3. **Build anim data buffer** — per-instance: animState (float), animTime (float), animSpeed (float), padding
4. **GPU Compute Pose** (if groupIDs available):
   - CSPose kernel reads rest voxels + groupIDs + anim data
   - Forward-transforms each voxel into posed position
   - Writes to `posedVoxelBuffer` (sized: totalVoxels × visibleCount)
5. **Proxy render** — draws a proxy cube per instance with the posed voxel buffer bound
   - Fragment shader raymarches through the voxel volume
   - Per-instance offset/rotation/anim applied via instanced attributes

### GPU Memory Budget

| Buffer | Size | Notes |
|--------|------|-------|
| sharedVoxelBuffer | 884,736 × 4B = ~3.5MB | One per model, shared by all instances |
| groupIDBuffer | 884,736 × 4B = ~3.5MB | One per model, read-only |
| regionIDBuffer | 884,736 × 4B = ~3.5MB | One per model, read-only |
| posedVoxelBuffer | 884,736 × N × 4B | N = visible instance count. 100 instances = ~354MB |
| instanceOffsetBuffer | N × 16B | Negligible |
| instanceAnimDataBuffer | N × 2 × 16B | Negligible |
| instanceMaterialRemapBuffer | N × maxRegions × 4B | Negligible (8 regions × 4B = 32B/instance) |

**Key insight**: The `posedVoxelBuffer` is the dominant GPU memory consumer. It scales linearly with visible instance count. At 96³ voxels:
- 50 instances = ~177MB
- 100 instances = ~354MB
- 200 instances = ~708MB

The LOD system (Near/Mid/Far/Ultra/Cull tiers) mitigates this by reducing raymarch steps for distant characters, but the posed buffer is allocated per visible instance regardless of LOD tier.

---

## 6. Asset File Format

### Consolidated .character.json (Current Standard)

Location: `Assets/StreamingAssets/voxel_characters/{name}.json`

```json
{
  "format": "steelcity_character",
  "version": 1,
  "name": "Vinny",
  "dims": [96, 96, 96],
  "materials": [
    { "id": 100, "name": "Red Brick", "r": 147, "g": 66, "b": 51, "hex": "#934233" },
    ...
  ],
  "voxels": [
    [x, y, z, materialId],
    ...
  ],
  "groups": {
    "x,y,z": groupId,
    ...
  },
  "groupDefs": [
    { "id": 0, "name": "Body", "color": "#888888", "desc": "Torso / unassigned" },
    { "id": 1, "name": "Head", "color": "#ff6b6b", "desc": "Head and neck" },
    ...
  ],
  "regions": {
    "x,y,z": regionId,
    ...
  },
  "regionDefs": [
    { "id": 0, "name": "Skin", "color": "#e0c8a0", "desc": "Exposed skin" },
    ...
  ],
  "pivots": {
    "0": { "x": 0.5, "y": 0.365, "z": 0.5 },
    ...
  },
  "animParams": {
    "restPose": { "leftArmZ": -1.5708, "rightArmZ": 1.5708 },
    "jointOffset": { "1": {"x":0,"y":0,"z":0}, "2": {"x":6,"y":0,"z":0}, ... },
    "walk": { "armSwing": 0.3, "armFreq": 6, "legStride": 0.4, "legFreq": 6 },
    "walkKeyframes": { ... },
    "armSwing": { "axisL": 0, "axisR": 0, "signL": 1, "signR": 1 },
    "legStride": { ... },
    "legTwist": { ... },
    "elbowBend": { ... },
    "kneeBend": { ... },
    "looking": { "headYaw": 0.5, "headYawFreq": 2, ... },
    "aiming": { "weaponType": "pistol", "armSwingL": -1.4, ... },
    "crouching": { "modelLower": 8, "legStrideL": -1.15, ... }
  },
  "states": [
    { "id": 0, "name": "Idle", "desc": "Standing still" },
    ...
  ],
  "savedAt": "2026-08-14T..."
}
```

### Body Groups (Animation Skeleton)

| Group ID | Name | Description |
|----------|------|-------------|
| 0 | Body | Torso / unassigned |
| 1 | Head | Head and neck |
| 2 | Left Arm | Left arm (upper + lower) |
| 3 | Right Arm | Right arm (upper + lower) |
| 4 | Left Leg | Left leg (thigh + shin) |
| 5 | Right Leg | Right leg (thigh + shin) |
| 6 | Left Shin | Left shin (lower leg) |
| 7 | Right Shin | Right shin (lower leg) |
| 8 | Left Forearm | Left forearm |
| 9 | Right Forearm | Right forearm |

### Pivots

Pivots are **normalized 0.0–1.0 fractions of dims** — they are resolution-independent. A pivot at `{x: 0.5, y: 0.365, z: 0.5}` means "50% across X, 36.5% up Y, 50% across Z" regardless of whether the model is 48³ or 96³.

### Joint Offsets

Joint offsets in `animParams.jointOffset` are in **voxel space**, not normalized. When the model is upscaled, these must be scaled by the same factor. The `upscale_character.py` tool handles this automatically.

### Legacy Format (.stasset + .groups + .anim.json)

Older models use three separate files:
- `{name}.stasset` — voxel data (binary)
- `{name}.groups` — group IDs (binary)
- `{name}.anim.json` — animation parameters (JSON)

The `CharacterJsonLoader` and `VoxelCharacter` handle both formats. New models should use the consolidated `.json` format.

---

## 7. How to Set Up a Character Entity

### Option A: In Unity Editor (Manual)

1. Create an empty GameObject in the scene
2. Add `VoxelCharacter` component
   - Set `assetFileName` to your model (e.g., `"Civilian1.json"`)
   - Set `voxelSize` to `0.02` (characters)
   - Set `useInstancing` to `true`
   - Assign `chunkManager` (or leave auto-find)
3. Add `CharacterAnimation` component
   - Set `currentState` to desired starting state
   - Set `autoDetectWalking` based on needs
4. `ClothingSystem` auto-adds on Start for .json assets
5. (Optional) Add `PedestrianLookAround` for NPC behavior
6. Position the GameObject — ground probe handles Y automatically

### Option B: From Code (Runtime Spawn)

```csharp
// Create the entity
var charObj = new GameObject("Hood_001");
charObj.transform.SetParent(parentTransform, false);

// Core component
var vc = charObj.AddComponent<VoxelCharacter>();
vc.assetFileName = "Civilian1.json";
vc.voxelSize = 0.02f;
vc.chunkManager = chunkManager;  // assign or auto-find
vc.collisionWorld = collisionWorld;  // assign or auto-find
vc.centerPosition = new Vector3(spawnX, spawnY, spawnZ);
vc.useWorldPosition = false;
vc.useInstancing = true;

// Animation driver
var anim = charObj.AddComponent<CharacterAnimation>();
anim.autoDetectWalking = true;  // or false for manual control

// Pedestrian behavior (optional)
var look = charObj.AddComponent<PedestrianLookAround>();
look.enableRandomLook = true;

// ClothingSystem auto-adds in VoxelCharacter.Start() for .json assets
```

### Option C: Use a Spawner Script

Add one of the spawner scripts (§4) to a GameObject in the scene and configure via Inspector.

---

## 8. Adding New Character Models

### Step 1: Author the Model

Use `VoxelAssetStudio/voxel_editor.html` to create a new model:
- Set Asset Type to "Character (0.02m/voxel)"
- Build the voxel model
- Tag body groups (Body, Head, Arms, Legs, Forearms, Shins)
- Tag regions (Skin, Face, Hair, Torso, Arms, Hands, Legs, Feet)
- Set animation pivots (10 joint pivots, normalized 0-1)
- Export as consolidated .character.json

### Step 2: Place the File

Copy the exported JSON to:
```
Assets/StreamingAssets/voxel_characters/{NewCharacterName}.json
```

### Step 3: Use in Code

```csharp
vc.assetFileName = "NewCharacterName.json";
```

Each unique asset file gets its own `InstancedGroup` with a separate GPU buffer. All instances of the same file share that buffer (1 draw call per model type).

### Step 4: Animation Parameters

The model's `.json` includes `animParams` (walk keyframes, joint config, pivots, aiming/crouching poses). These are loaded automatically by `VoxelCharacter.LoadAndApplyAnimParams()`.

If the model has different proportions than Civilian1, the pivots and joint offsets must be re-authored — the shader's hardcoded pivot approximation only works for models similar to the original 16×32×10 hoodlum.

---

## 9. Upscaling Existing Models

To fix raymarch sampling artifacts (see-through gaps during animation), models can be upscaled using:

```
python Tools/upscale_character.py "Assets/StreamingAssets/voxel_characters/Civilian1.json"
```

This performs 2× nearest-neighbor upscaling:
- Each voxel becomes a 2×2×2 block (8 voxels)
- `dims`, `voxels`, `groups`, `regions` all scale by 2
- `jointOffset` and `crouching.modelLower` (voxel-space values) scale by 2
- `pivots` (normalized 0-1) stay unchanged
- Animation angles (radians) stay unchanged
- Original file backed up to `.original.json`

**Revert**:
```
python Tools/upscale_character.py "Assets/StreamingAssets/voxel_characters/Civilian1.json" --revert
```

**Important**: When upscaling a model by N×, you must also divide `voxelSize` by N on all spawner scripts and `VoxelCharacter` components to maintain the same physical size. The upscale script handles the model data; the voxelSize change is a code-side change.

---

## 10. Key Architecture Decisions

1. **One model, many instances** — GPU instancing means 1 draw call for all characters of the same type. Adding a second model file doubles the draw calls (still only 2 total).

2. **Consolidated JSON format** — All data (voxels, groups, regions, pivots, anim params) in one file. Eliminates the old 3-file (.stasset + .groups + .anim.json) coordination problem.

3. **Per-instance clothing via GPU** — The material remap system allows each instance to wear different clothing without duplicating voxel data. Region tags in the model define which voxels can be recolored.

4. **Ground probe, not physics** — Characters use a simple downward ray probe to find ground Y. No Rigidbody or character controller — this is intentional for the raymarched voxel world where standard colliders don't exist on terrain.

5. **CPU vs GPU animation paths** — `AnimationTestSpawner` uses CPU forward-transform (for testing). All other spawners use GPU compute pose via `VoxelChunkManager`'s CSPose kernel. The GPU path is the production path.

6. **Auto-detect walking** — `CharacterAnimation` can automatically switch between Idle and Walking based on measured velocity. This means you don't need to manually manage animation state for pathfinding agents — just move the transform and the animation follows.

7. **Cheap shading for instanced characters** — `_CheapShading = 1` is set for all instanced character renders in `VoxelChunkManager.RenderInstancedGroup()`. This skips `SmoothNormal` (6-neighbor gradient) and uses raw DDA face normals instead. Reason: after CSPose forward-transform scatters voxels into posed positions, single-voxel gaps appear at group boundaries (neck, shoulders, hips). `SmoothNormal` samples these gaps and computes zero or skewed gradients, producing black pixels under squared lighting. Raw DDA normals are always axis-aligned and stable. Visual impact is negligible since the art style is intentionally blocky.

---

## 11. Future Work

- **PopulationSpawner** — Production spawner that reads city population data (2000 civilians, 400 police, 100 FBI from `Constants.xtx`) and distributes entities across building/sidewalk waypoints
- **Multiple character models** — Distinct models for hood types (Arson Hood, Firearms Hood, etc.), police officers, FBI agents, civilians
- **Item/Decor asset type** — New voxel editor dropdown for weapons, throwables, and map decorations at 0.01m/voxel
- **Weapon attachment** — Compositing item models (e.g., revolver) into character hand region for combat animations
- **Attachment system** — Bone-attached props that follow animated limbs (weapons in hands, hats on heads)
