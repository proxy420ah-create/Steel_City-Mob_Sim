# Character Lifecycle Races — Weapon Weld/Pose Divergence

**Date**: 2026-10-05
**Status**: 🟢 FIXED
**Severity**: 🔴 CRITICAL

## Symptoms

- Model 10s welded at the **T-pose hand position** instead of following the
  posed arm during idle; appeared aligned with the **left** hand.
- Hotkey poses on the rig character (Vinny) didn't stick — **T-Pose worked,
  everything else reverted; pressing Aim flipped back to Idle.**
- Earlier in the session: hoodlums spawned **naked with separated arms** and
  no weapons after `Civilian1JointTestFINAL.json` was promoted (trimmed
  `animParams`).

## Root Cause

Not one bug — a **structural failure mode**: the character lifecycle had no
single authority. Five code paths each re-read and re-parsed the same
`.character.json` (`VoxelCharacter`, `VoxelChunkManager`, `WeaponMount`,
`ClothingSystem`, plus anim-param upload), three systems wrote animation state
through one unowned field, and the CPU weld animator + GPU pose shader filled
missing anim sections from **two different hardcoded default tables that had
diverged**:

| Section | CPU animator default | GPU upload fallback (`VoxelCharacter.cs`) |
|---|---|---|
| `aiming.armSwing` | L=0, **R=−1.4** | **L=−1.4**, R=0 |
| `aiming.torsoTwist` | **−0.2** | **+0.2** |
| `aiming.elbowBend` | L=0, **R=0.3** | **L=0.3**, R=0 |
| `legStride.sign` | **−1, −1** | +1, +1 |

The animator's defaults had been corrected to the right-arm/−1-stride
convention in the earlier animParams hygiene pass; the GPU-side `??`-fallback
constants in `LoadAndApplyAnimParams` were left stale. On a trimmed file every
default fires → the rendered arm is the LEFT one while the weld solver followed
the RIGHT → "weapon on the left hand".

**"Aim resets to Idle"** — `CharacterAnimation.currentState` was written by
`SetState` from every caller (hotkeys, `PedestrianLookAround` timer coroutine,
`EventPlayer`) AND by `autoDetectWalking` every frame (which exempted only
`Looking`/`AimWalk` — a moving character's `Aiming` → `Walking` → `Idle`), and
`PedestrianLookAround`'s coroutine unconditionally `SetState(Idle)` after its
timer — restoring over whatever had been set meanwhile.

**Weapon at T-pose** — the weld animator was privately built inside
`WeaponMount` from its own re-extraction; when any subset came back empty the
weld fell back to bind-space position (`animator == null` → `posed = bind`).

**Naked/separated arms** — `LoadAndApplyAnimParams` early-returned on missing
`walkKeyframes`, skipping pivot/jointOffset/static upload; an NRE there aborted
`Start` before `ClothingSystem` was added.

## Resolution

Atomic character lifecycle (see `docs/systems/CHARACTER_ASSET_LIFECYCLE.md`):

1. **`CharacterAssets` registry** (`Sim/CharacterAssets.cs`) — one cached parse
   per filename feeds every consumer: voxels, groups, regions, pivots
   (painted `pivot_N` overrides applied + canonical fallback filled),
   attachment points, joint offsets, and a defaults-filled `AnimParamsData`.
2. **`VoxelCharacter.WhenReady(cb)`** — explicit ready notification replaces
   `WeaponMount`'s `IsInitialized` polling coroutine. Fires immediately when
   already initialized; otherwise queued to the end of `Start`.
3. **Pose-override channel** in `CharacterAnimation` — `RequestPose(state,
   priority, duration)` holds an override lane over the locomotion base lane;
   `autoDetectWalking` suspends while held; timed overrides auto-release.
   `CharacterRig` hotkeys hold at `PRIORITY_DEBUG`; `PedestrianLookAround`
   requests timed `PRIORITY_BEHAVIOR` (no coroutine restore — can't stomp).
4. **Single defaults table** — `LoadAndApplyAnimParams` emits `kfs[]`/`jc[]`/
   `asp[]` directly from `asset.ParamsData` (the same object the weld animator
   reads). GPU and CPU can no longer disagree on missing sections.
   `WeaponMount` uses `asset.CreateAnimator()` — shared params, per-instance
   `ikOverrides`.

Also removed: `VoxelCharacter`'s duplicate `AnimParamsData` class tree
(superset lives in `VoxelCharacterAnimator`), the private pivot/jointOffset
re-parsers, and the redundant per-instance file reads.

## Follow-on: null `WalkKFPose` → frozen weld (2026-10-05)

Post-refactor runtime NRE:

```
NullReferenceException at VoxelCharacterAnimator.GetKFPoseValue (:298)
  ← GetWalkPose (:259) ← PosePoint (:723) ← WeaponMount.LateUpdate (:323)
```

**Mechanism** — `GetWalkKfPose` returned null when a keyframe slot was null:
`kf0`/`kf1` returned unchecked (idx 0/1), and the `kf2 ?? (autoMirror ?
Mirror(kf0) : kf0)` chain yields null when `kf0` itself is null. JsonUtility
materializes a **non-null `WalkKeyframesData` with null `kf*` fields** for a
partial `"walkKeyframes"` object, so `p.walkKeyframes == null` default-fill
never fired. One null pose → NRE every `LateUpdate` → the item transform was
never written → **weapon frozen at spawn position** (read as "T-pose anchor on
the wrong hand") + per-frame exception spam hobbling the visible walk.

**Fix (three layers)**:
- `LoadFromAnimJson` — default fill now fires when `walkKeyframes` is null
  **or both `kf0`/`kf1` are null**; partial sections get per-field fills
  (missing `kf0`/`kf1`/`cycleDuration`/`interpolation`/`bodyBob`/`weightShift`).
- `GetWalkKfPose` — never returns null; missing slot → shared
  `ZERO_WALK_POSE` + warn-once naming the missing kf index.
- `GetKFPoseValue` — `p == null → 0f` last-resort guard.

**Data note** — `ARCHIVE/voxel_characters/Vinny.anim.json` and
`character_hoodlum_0.anim.json` are byte-identical to each other and their
`walkKeyframes`/`walk`/`restPose`/`armSwing`/`legStride`/`elbowBend`/
`kneeBend`/`legTwist`/`looking` match the animator's defaults exactly — the
defaults were extracted from them. **Their `aiming` section is the stale
left-armed table** (`armSwingL=-1.4`, `elbowBendL=0.3`, `torsoTwist=+0.2`) and
`jointOffset` (gid2/3 ±3) is already baked into Civilian1 geometry — do NOT
re-import either section.

## Related Files

- `Assets/Scripts/Sim/CharacterAssets.cs` — registry (new)
- `Assets/Scripts/Sim/VoxelCharacter.cs` — registry consumer + WhenReady
- `Assets/Scripts/Sim/WeaponMount.cs` — shared asset + CreateAnimator
- `Assets/Scripts/Sim/CharacterAnimation.cs` — pose-override arbitration
- `Assets/Scripts/Sim/PedestrianLookAround.cs` — timed RequestPose
- `Assets/Scripts/Sim/CharacterRig.cs` — DEBUG-priority hotkey poses
- `Assets/Scripts/Sim/ClothingSystem.cs` — shared regions
- `Assets/Scripts/UI/VoxelChunkManager.cs` — shared voxel/region parse
- `docs/systems/CHARACTER_ASSET_LIFECYCLE.md` — design doc + gotchas G1-G8
