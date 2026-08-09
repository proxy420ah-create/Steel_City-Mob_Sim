# Refactoring Roadmap — Steel City: Mob Sim

**Status**: 📋 PLANNING — Extract modules when natural seams emerge, not prematurely
**Created**: August 9, 2026
**Companion docs**: `DYNAMIC_OBJECT_RENDERING_TIERS.md`, `GPU_DRIVEN_RENDERING_PLAN.md`, `GANG_SIMULATION_ARCHITECTURE.md`

---

## Current File Sizes (Aug 9, 2026)

| File | Lines | Responsibility Count | Priority |
|---|---|---|---|
| `VoxelChunkManager.cs` | ~1,988 | High (chunk loading, GPU buffers, sector baking, instancing, rendering, perf) | **High** |
| `CityMap3D.cs` | ~1,576 | High (camera, block rendering, building loading, addresses, spawning, rebaking) | **High** |
| `GameUIController.cs` | ~1,376 | Medium (UI tabs, order system, simulation lifecycle, block info) | **Medium** |
| `StressTestSpawner.cs` | ~533 | Low (single purpose — stress testing) | Low |
| `GameEngine.cs` | ~420 | Low (game state + week processing) | Low |

**Rule of thumb**: Extract when a file exceeds ~1,500 lines AND has 3+ distinct responsibilities that can be separated without breaking tight coupling to Unity lifecycle methods.

---

## Extraction Plan

### 1. `CityMapCamera.cs` (~150 lines extracted from `CityMap3D.cs`)

**When**: After animation work session, before building destruction system

**What moves**:
- Camera orbit fields (`cameraYaw`, `cameraPitch`, `targetYaw`, `targetPitch`, `CameraOrbitDistance`)
- `HandleMouseCamera()` — mouse zoom/rotate/pan
- `UpdateCameraTransform()` — smooth lerp + position computation
- `SetCameraYaw()`, `SetCameraPitch()`, `SetCameraOrthoSize()`, `GetCameraOrthoSize()`, `ResetCamera()`
- `SetCameraFocus()` — focus point control
- Debug toggles (`debugCameraFreedom`, `debugCameraOrbitDistance`)
- F6 hotkey handling (camera portion)

**What stays in `CityMap3D`**:
- `mapCamera` reference (passed to extracted component)
- `mapRoot` reference (for camera follow)
- `IsExecutionMode` flag (controls whether camera handles input)

**Interface**:
```csharp
public class CityMapCamera : MonoBehaviour
{
    public void Initialize(Camera cam, Transform mapRoot);
    public void SetCameraFocus(Vector3 worldPos);
    public void SetCameraOrthoSize(float size);
    public float GetCameraOrthoSize();
    public void SetCameraYaw(float yaw);
    public void SetCameraPitch(float pitch);
    public void ResetCamera();
    public bool IsExecutionMode { get; set; }
    // Debug toggles remain public for Inspector access
    public bool debugCameraFreedom;
    public float debugCameraOrbitDistance;
}
```

**Why this is a clean seam**: Camera logic has zero dependency on building data, address registry, or chunk management. It only needs `Camera`, `Transform mapRoot`, and input. Self-contained.

---

### 2. `BuildingLoader.cs` (~250 lines extracted from `CityMap3D.cs`)

**When**: When implementing building destruction / sector rebake pipeline

**What moves**:
- `BuildRaymarchBlock()` — building loading logic (the stasset loading part, not block setup)
- `IsEmptyLand()` — static helper
- `RegisterAddress()` + `BuildingAddress` class + `addressRegistry`
- `RebakeEmptyPlotChunks()` — the rebake pipeline prototype
- `GetBuildingHeight()` + `heightCache`
- `GetStassetDimensions()` wrapper (if any)

**What stays in `CityMap3D`**:
- Block view management (`views` dictionary, `BlockView3D`)
- Ground collider creation
- Block label creation
- Character spawning (`SpawnCharacter`)
- Road ticker
- Terrain generation delegation (`BuildVoxelTerrain`)

**Interface**:
```csharp
public class BuildingLoader
{
    public void Initialize(VoxelChunkManager chunkManager, Transform mapRoot);
    public BuildingFootprint LoadBuilding(string blockId, string stassetPath, Vector3 anchorPos, int row, int col, int subIndex);
    public void RebakeEmptyPlotChunks();
    public void RebakeChunk(string chunkName, string stassetPath, Vector3 worldCenter, int row, int col, int subIndex, Action<uint[],int,int,int> modifier);
    public IReadOnlyList<BuildingAddress> Addresses { get; }
}
```

**Why this is a clean seam**: Building loading is already separated from block view setup by the `RegisterAddress` call pattern. The rebake pipeline (`RemoveChunk` → modify → reload) is self-contained and will grow with the destruction system. Extracting it now gives destruction modifiers a natural home.

**Future expansion**: `RebakeChunk()` with a custom modifier Action becomes the API for:
- Explosion damage (replace voxels with air + debris)
- Fire damage (material ID changes)
- Repair (reload original stasset)
- Structural collapse (shift voxels downward)

---

### 3. `VoxelChunkManager` Split (~1,988 lines)

**When**: When GPU-driven rendering phases 3-5 are implemented (compute shader culling, etc.)

**Potential splits**:

#### 3a. `SectorBakery.cs` (~400 lines)
- `BakedSector` class
- `RegisterSector()`, `UnregisterSector()`
- `RenderBakedSectors()`
- `BuildSectorMatrices()`
- Already partially separated in `SectorBaker.cs` — merge the baking logic

#### 3b. `ChunkBufferManager.cs` (~300 lines)
- `VoxelChunk` class
- `LoadChunkFromData()`, `LoadChunkCentered()`, `LoadChunkCenteredProcedural()`
- `RemoveChunk()`, `UpdateChunkPosition()`
- `chunkLookup` dictionary
- ComputeBuffer creation/release

#### 3c. `InstancedCharacterRenderer.cs` (~200 lines)
- `RegisterInstancedCharacter()`, `UnregisterInstancedCharacter()`
- Instance buffer management
- `RenderInstancedCharacters()`

**What stays in `VoxelChunkManager`**:
- `OnRenderObject()` / render dispatch
- LOD management
- Proxy render path
- Performance tracking
- Camera/render bridge coordination

**Why wait**: `VoxelChunkManager` is the rendering core. Splitting it risks breaking the render pipeline. Wait until GPU-driven rendering phases are done — the seams will be clearer after compute shader culling is implemented.

---

### 4. `GameUIController.cs` Split (~1,376 lines)

**When**: When HoodAgent state machine is implemented (more UI panels needed)

**Potential splits**:

#### 4a. `OrderPanelController.cs` (~200 lines)
- Order button creation
- Order validation (distance, hood status)
- `TryEnableOrderButtons()`
- Order submission logic

#### 4b. `BlockInfoPanel.cs` (~150 lines)
- `RefreshBlockInfo()` — block details display
- Block click handling
- Block highlight management

#### 4c. `SimulationLifecycle.cs` (~200 lines)
- `BeginExecution()` — Working phase setup
- `OnTickSimulationComplete()` — Working phase teardown
- `EndExecution()` — Planning phase restoration
- Camera focus transitions (calls `CityMapCamera`)

**What stays in `GameUIController`**:
- Tab management
- Hood list panel
- Event log
- FPS counter
- Top-level `Update()` dispatch

---

## Anti-Patterns to Avoid

### ❌ Don't extract for line count alone
A 2,000-line file with one responsibility is fine. A 500-line file with 5 responsibilities is a problem.

### ❌ Don't extract during a creative session
Refactoring destabilizes. Do it between features, not before animation work or gameplay implementation.

### ❌ Don't create deep dependency chains
Extracted modules should depend on interfaces or direct references, not call back into the parent. `CityMapCamera` should not need to know about `BuildingLoader`.

### ❌ Don't move Unity lifecycle methods unnecessarily
`Awake()`, `Start()`, `Update()` should stay on the main `MonoBehaviour`. Extracted classes can be `MonoBehaviour` (if they need their own lifecycle) or plain C# classes (if they're called from the parent's lifecycle).

---

## Trigger Conditions

Extract a module when **any** of these are true:

1. **File exceeds 1,500 lines** AND has 3+ responsibilities → extract the cleanest seam
2. **New feature needs its own lifecycle** (e.g., destruction system needs `Update()` for animation) → extract as `MonoBehaviour`
3. **Two features share no code** but live in the same file (e.g., camera + building loading in `CityMap3D`) → extract
4. **A module is referenced by name in design docs** (e.g., "SectorBakery" in `GPU_DRIVEN_RENDERING_PLAN.md`) → extract to match the architecture
5. **Merge conflicts become frequent** on a single file → extract to allow parallel work

---

## Current Assessment (Aug 9, 2026)

**No urgent refactoring needed.** The codebase is working and stable. The largest files (`VoxelChunkManager`, `CityMap3D`) are complex but coherent — they're not spaghetti, they're just big.

**Next recommended extraction**: `CityMapCamera.cs` + `BuildingLoader.cs` from `CityMap3D.cs`, timed with the building destruction system implementation. This is the natural seam — the rebake pipeline is already prototyped in `RebakeEmptyPlotChunks()`.

**After that**: `VoxelChunkManager` split, timed with GPU-driven rendering Phase 3-5.

---

## Revision History

| Date | Change |
|---|---|
| Aug 9, 2026 | Created — initial assessment + extraction plan |
