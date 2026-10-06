# Character Asset Lifecycle & Pose Authority

**Status**: Design + Implementation (2026-08-15)
**Depends on**: `CHARACTER_SYSTEM.md`, `WEAPON_ATTACHMENT_SYSTEM.md`, `VOXEL_GROUP_ANIMATION.md`
**Motivated by**: left-hand/T-pose weapon weld, "Aim resets to Idle", separated arms on trimmed animParams

---

## Overview

The character pipeline grew one consumer at a time until **five separate code paths
each parsed the same `.character.json`**, **three independent systems wrote the
animation state**, and **two pose authorities (CPU weld animator vs GPU pose
shader) carried divergent copies of the anim-param default tables**. Every bug in
the Aug-15 session (naked characters, separated arms, left-hand weapons, poses
that won't stick) traces to one of those structural faults — not to bad JSON.

This document defines the atomic replacement: a single cached `CharacterAsset`
per filename, an explicit init ordering, and a single arbitration channel for
pose state.

---

## Current (Fragmented) Architecture

```
VoxelCharacter.Start
  ├─ LoadAsset              → StAssetReader.LoadVoxelsFromJson   (parse #1: voxels)
  ├─ RegisterInstancedWithManager
  │    └─ VoxelChunkManager → CharacterJsonLoader.Load           (parse #2: voxels/groups/regions)
  ├─ LoadAndApplyAnimParams → File.ReadAllText + manual extract  (parse #3: animParams/pivots)
  │    └─ SetPivots / SetWalkKeyframes / SetAnimStaticParams     (GPU pose authority)
  └─ AddComponent<ClothingSystem>
         └─ LoadRegionData  → CharacterJsonLoader.Load           (parse #4: regions)

WeaponMount coroutine — polls IsInitialized each frame, then:
  └─ LoadCharacterAttachData → File.ReadAllText + CharacterJsonLoader.Load
       + ParseAttachmentPoints + LoadFromAnimJson                (parse #5)
       └─ builds a PRIVATE VoxelCharacterAnimator                (CPU pose authority)

State writers → CharacterAnimation.currentState:
  autoDetectWalking (per-frame velocity), PedestrianLookAround (timer coroutine),
  CharacterRig hotkeys, EventPlayer, CityMap3D, ArmAimSolver (ikOverrides on the
  mount's private animator)
```

---

## Gotchas Catalog

Each entry: symptom → structural cause → owning fix.

### G1 — Five parses, five subsets (DIVERGENCE)
`VoxelCharacter`, `VoxelChunkManager`, `WeaponMount`, `ClothingSystem` each
`File.ReadAllText`/`CharacterJsonLoader.Load` the same file and extract different
subsets. Any subset that silently fails (extractor misses a key, JsonUtility drops
a field) produces a *local* inconsistency no other consumer can see.
**Fix**: `CharacterAssets.Get()` parses once; consumers read shared fields.

### G2 — Coroutine-polling init ordering (RACE)
`WeaponMount.Start` spins `while (!character.IsInitialized) yield return null`.
Works only because `VoxelCharacter.Start` happens to run first; any reorder
(component added later, `enabled` toggled, prefab order) deadlocks or equips
against half-loaded data.
**Fix**: `VoxelCharacter.WhenReady(callback)` — fires immediately when already
initialized, else queued. No polling.

### G3 — Contested pose-state ownership (STOMP)
`CharacterAnimation.currentState` is written by `SetState` calls from
`PedestrianLookAround` (Idle↔Looking timer, CoastClearCheck AimWalk→Idle restore),
`CharacterRig` hotkeys, `EventPlayer`, `CityMap3D` — and by `autoDetectWalking`
every frame. Auto-detect exempts only `Looking`/`AimWalk`; a moving character's
**Aiming → Walking → Idle** is the literal "Aim resets to idle" path. Timed
LookAround coroutines restore `Idle` over whatever was set meanwhile.
**Fix**: priority pose-override channel (`RequestPose/ReleasePose`) — locomotion
writes the base state; overrides win while held.

### G4 — Two pose authorities, divergent default tables (THE WEAPON BUG)
`VoxelCharacter.LoadAndApplyAnimParams` fills missing anim sections with one set
of constants for the GPU (`jc[]`/`asp[]` `??`-fallbacks);
`VoxelCharacterAnimator.LoadFromAnimJson` fills a *different* set for the CPU
weld. On a trimmed `{"crouching":{…}}` file **every default fires**:

| Section | CPU animator default | GPU fallback constant |
|---|---|---|
| `aiming.armSwing` | L=0, **R=−1.4** | **L=−1.4**, R=0 — arms swapped |
| `aiming.torsoTwist` | **−0.2** | **+0.2** — opposite sign |
| `aiming.elbowBend` | L=0, **R=0.3** | **L=0.3**, R=0 — swapped |
| `legStride.sign` | **−1, −1** | +1, +1 — mirrored strides |

The weld follows the CPU pose; the voxels render the GPU pose → weapon appears
on the *other* arm in Aiming, drifts in Walking, sits at bind pose whenever the
CPU animator fails to build. This is the "mass sign-correction over-corrected"
artifact the user predicted.
**Fix**: GPU upload arrays are *emitted from* `asset.ParamsData` — the same
defaults-filled object the CPU animator uses. One default table, one truth.

### G5 — Optional params starving required init (FIXED 2026-08-15)
`LoadAndApplyAnimParams` early-returned on missing `walkKeyframes`, skipping
pivot/jointOffset/static upload → arms posed around shader fallback pivots
("separated arms"). An NRE there aborted `Start` → `ClothingSystem` never added
("naked"). Fixed: keyframe upload is guarded; the call is try/catch'd; pivots
always upload.
**Rule going forward**: optional sections must never gate mandatory geometry/
attachment/clothing setup.

### G6 — Duplicate AnimParamsData class trees (FIELD LOSS)
`VoxelCharacter` and `VoxelCharacterAnimator` each declare their own
`AnimParamsData`/`AimingData`/etc. The animator's copy is a superset
(`weaponType`, `walkAmp`, `twistWalkAmp`). Fields present in JSON but absent
from the GPU-path copy are silently dropped.
**Fix**: `VoxelCharacterAnimator`'s classes become the single definition; the
registry stores `ParamsData` in that type.

### G7 — Stale InstancedCharacter handle (LATENT)
`CharacterAnimation` caches `instancedHandle` once and never re-fetches unless
null. Re-registration swaps the handle object; writes land on a dead handle —
state never reaches the GPU (character stuck at whatever the new handle
defaults to).
**Fix**: re-fetch via `character.GetInstancedHandle()` when writes matter;
handle exposed as authoritative getter, not a cached field.

### G8 — Per-asset GPU uploads are first-writer-wins (LATENT)
`SetPivots`/`SetWalkKeyframes`/`SetAnimStaticParams` key on `assetFileName`;
whoever registers first wins and later writers overwrite the shared group's
buffers. Harmless when all instances share one file — exactly why a single
registry that uploads *once per asset* is the right shape.
**Fix**: `VoxelCharacter` uploads only when the LIVE group lacks them
(`VoxelChunkManager.HasAnimUploads(assetFileName)`). A `GpuUploaded` flag on the
cached asset was tried and removed: the static cache outlives the chunk
manager's groups (rebuilt on scene reload), so the flag went stale and the new
group never got pivots/restPose/keyframes (permanent T-pose). **Gate on the
consumer's live state, never on a flag stored on a longer-lived cache.**

### G9 — Null-checking JsonUtility sections (SILENT ZERO-FILL)
`JsonUtility` can return non-null, zero-filled objects for sections absent from
the JSON. `restPose` 0/0 = arms never drop (T-pose on CPU weld AND GPU); zero
joint signs = frozen limbs. **Fix**: `LoadFromAnimJson` decides "missing" from
the raw text (`Missing(key)`); null is only a backstop.

### G10 — Editor vs Unity handedness (MIRRORED L/R)
The editor (three.js, right-handed) and Unity (left-handed) read the same voxel
indices, so authored L/R is mirrored in Unity. Models face **+Z** (thumbs and
the Face slab point +Z). Facing +Z: editor right = −X (gid 3/5/7/9), **Unity
right = +X (gid 2/4/6/8)**. Symptom: weapon in the left hand, left arm raised
on Aim, while the editor looks correct.
**Fix**: flip once at promotion — `tools/promote_character_to_unity.py` swaps
`right_*`/`left_*` attachment names, L/R-swaps (and Y-negates) the asymmetric
`aiming`/`crouching`/rest params, and stamps `"handedness": "unity"`. It aborts
on an already-flipped file (no double flip). `CharacterAssets` warns at load if
`right_hand.x` is on the −X side. Animator preview: press **U** to mirror the
view (Unity-handedness preview). Never hand-edit runtime character JSON.

---

## Design

### 1. `CharacterAssets` — one parse, shared truth (static registry)

```csharp
public sealed class CharacterAsset {
    public string FileName;
    public ushort[,,] Voxels;
    public int DimX, DimY, DimZ;
    public uint[] GroupIDs;
    public Dictionary<int, Vector3> Pivots;                 // painted pivot_N overrides applied
    public Dictionary<string, AttachmentPoint> AttachPoints;
    public Dictionary<string,int> Regions;
    public List<RegionDef> RegionDefs;
    public Dictionary<int, Vector3> JointOffsets;
    public string AnimParamsRaw, Json;
    public float VoxelSize;
    public VoxelCharacterAnimator.AnimParamsData ParamsData; // defaults filled — single table
    public bool GpuUploaded;                                 // G8 guard
    public VoxelCharacterAnimator CreateAnimator();          // per-instance pose context
}
public static class CharacterAssets { public static CharacterAsset Get(string fileName); }
```

- `Get()` is idempotent — first call parses, everyone else gets the cached asset.
- `CreateAnimator()` returns a **new** `VoxelCharacterAnimator` sharing
  `ParamsData`/`Pivots`/`JointOffsets` but with its own `ikOverrides`/`ikBlend`.
  The animator cannot be a shared singleton: IK overrides are per-character —
  two hoodlums sharing Civilian1 must not inherit each other's aim.
- Non-JSON `.stasset` assets bypass the registry (legacy path stays).

### 2. Explicit init ordering — `WhenReady`, no polling

```csharp
// VoxelCharacter
public bool IsInitialized { get; }
public void WhenReady(Action<VoxelCharacter> cb); // fires now if ready, else queued
```

`VoxelCharacter.Start` is the single orchestrator:

```
LoadAsset (registry) → position → Register → upload anim params (from asset)
→ FindCollisionWorld → initialized → ClothingSystem → flush WhenReady queue
```

`WeaponMount.Start` becomes: `character.WhenReady(_ => { if (equipOnStart) Equip(itemFileName); })`.
`ClothingSystem` reads `asset.Regions` in its own `Start` (registry is already
warm — parse is instant). Components added *after* init still work:
`WhenReady` fires synchronously.

### 3. Pose authority — one arbitration channel

`CharacterAnimation` remains the **only writer** to `instancedHandle.animState`.
Writers split into two lanes:

- **Base lane** — `SetState(state)` = locomotion intent (idle/walk). Auto-detect
  continues to own this lane (velocity → Walking/Idle).
- **Override lane** — `RequestPose(state, priority, duration)` wins over the
  base lane while held; `duration < 0` = persistent until `ReleasePose`.

```csharp
public const int PRIORITY_BEHAVIOR = 10; // PedestrianLookAround, scripted beats
public const int PRIORITY_COMBAT   = 30; // aim/combat AI
public const int PRIORITY_DEBUG    = 50; // rigs, hotkeys
```

- `CharacterRig` hotkeys → `RequestPose(state, PRIORITY_DEBUG)` — nothing can
  stomp a debug pose; `I` (idle) releases the override.
- `PedestrianLookAround` → `RequestPose(Looking, PRIORITY_BEHAVIOR, duration)`;
  expiry auto-releases — **no coroutine restore**, so it can never stomp a
  higher-priority state. Same for `CoastClearCheck`.
- Auto-detect is skipped while any override is held (it resumes the base lane
  on release).

### 4. One pose authority — GPU arrays emitted from `ParamsData`

`VoxelCharacter.LoadAndApplyAnimParams` no longer parses JSON and no longer
owns fallback constants. It *emits* `kfs[]`/`jc[]`/`asp[]` directly from
`asset.ParamsData` (already defaults-filled by `LoadFromAnimJson`). The CPU
weld animator and the GPU pose shader now read the **same numeric defaults** —
G4's swapped-arm/mirrored-sign divergences are eliminated structurally, not
patched constant-by-constant.

`WeaponMount` uses `asset.CreateAnimator()` — ArmAimSolver's `ikOverrides` and
the weld's `PosePoint` share one pose context per character.

---

## Data Flow (After)

```
CharacterAssets.Get("Civilian1.json")  ← single File.ReadAllText + parse
        │
        ├─ VoxelCharacter      → Voxels, Dims
        ├─ VoxelChunkManager   → Voxels, GroupIDs, Regions (shared buffers)
        ├─ ClothingSystem      → Regions, RegionDefs
        ├─ WeaponMount         → AttachPoints, Pivots, CreateAnimator()
        └─ VoxelCharacter      → ParamsData → emits kfs/jc/asp → GPU upload (once per asset)

Pose:  writers → RequestPose/SetState → CharacterAnimation (arbiter)
                                          └─ instancedHandle.animState → GPU
                                          └─ read by WeaponMount weld → item transform
```

---

## Migration Notes

- `VoxelCharacter`'s private `ParsePivotsManual`/`ParseJointOffsetsManual`/
  `AnimParamsJson`/`AnimParamsData` tree become unused in the load path —
  superseded by `VoxelCharacterAnimator`'s canonical classes (superset fields:
  `weaponType`, `walkAmp`, `twistWalkAmp`).
- `CharacterJsonLoader` stays the low-level parser; `CharacterAssets` is the
  caching layer above it.
- `WeaponMount.hand` remains the semantic selector (`right_hand`/`left_hand`);
  no index-side heuristics are introduced. The right hand is `right_hand`
  (gid 9 chain through `pivot_9`→`pivot_3`).

---

## Verification Checklist

- [ ] Console: single `[CharacterAssets] Loaded` line per filename (not 5 parses)
- [ ] Idle: weapon welded to **right** hand at hip level (not T-pose, not left)
- [ ] `A` hotkey: Aiming state **sticks** — arm raised, bore ray forward
- [ ] `T`/`I`/`W`/`L`/`C` all hold their pose until released
- [ ] LookAround resumes only when no override held
- [ ] Walking: weapon tracks the rendered hand (CPU/GPU pose agree)
- [ ] Aiming: weapon stays on right arm (GPU no longer raises the left arm)
- [ ] Trimmed animParams file: pivots/clothing/init all still succeed (G5)
