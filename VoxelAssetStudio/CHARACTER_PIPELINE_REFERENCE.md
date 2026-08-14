# Character Pipeline & Animator — Technical Reference

**Date**: August 9, 2026  
**Status**: ✅ Active  
**Editors**: `character_pipeline.html`, `character_animator.html`

---

## Overview

The character pipeline is a **Portrait-First** workflow that generates 3D voxel characters from decoded Gangsters: Organized Crime (1998) portrait data. Characters are generated procedurally with animation groups, pivots, and parameters.

```
1. PORTRAIT  →  Select face from decoded catalog
2. BUST      →  Generate head+shoulders voxel preview
3. FULL BODY →  Procedural generation with locked features
4. RIG       →  Auto-assign animation groups + pivots
5. ANIMATE   →  Load in animator, test animation states
6. EXPORT    →  .stasset + .groups + .anim JSON for Unity
```

---

## Editor Files

| File | Purpose |
|------|---------|
| `character_pipeline.html` | Portrait selection → feature refinement → full body generation → export |
| `character_animator.html` | Load generated model → test animation states → export groups/params |
| `voxel_editor.html` | General voxel editing (buildings, props) |

---

## Portrait-First Pipeline (`character_pipeline.html`)

### Phase 1: Portrait Selection
- `PORTRAIT_CATALOG` array contains decoded feature presets from Gangsters 1998 portraits
- Each entry: `{label, hair, hat, beard, glasses, skin, eyes, nose, mouth}`
- Clicking a portrait card calls `selectPortrait(idx)` → sets UI dropdowns
- `generateBust(features)` builds head+shoulders only (16×32×10 dims)

### Phase 2: Feature Refinement
- UI dropdowns for: Hair (0-7), Hat (0-4), Eyes (0-5), Nose (0-4), Mouth (0-4), Glasses (0-3), Beard (0-2), Scar (0-7), Skin (0-63), Body type
- "Build Full Body" → `generateFromFeatures()` → `generateCharacter(seed, features)`
- "Preview Bust Only" → `generateBust(getFeatures())`

### Phase 3: Animation Groups
- `autoAssignGroups()` segments voxels by spatial position using `ANATOMY1` constants
- Groups can be manually painted or isolated for inspection

### Phase 4: Export
- `.stasset.json` — voxels only (dims + voxels array)
- `.groups.json` — group assignments (dims + groups array)
- `.anim.json` — pivots + animation parameters
- `.project.json` — everything combined (voxels + groups + pivots + params + seed)

---

## Procedural Generation — Hoodlum 0

### Hood 0 Default Features
```javascript
{ hair:1, hat:1, beard:0, glasses:0, skin:0, eyes:1, nose:1, mouth:0, body:'hoodlum', scar:0 }
```

### Body Construction (16×32×10)
| Region | Y Range | X Range | Z Range | Material |
|--------|---------|---------|---------|----------|
| Legs (L) | 0-12 | 3-5 | 2-7 | 126 (hoodlum) |
| Legs (R) | 0-12 | 10-12 | 2-7 | 126 |
| Torso+Arms | 13-21 | 0-15 | 1-8 | 126 |
| Wrist skin | 13-14 | 0-15 | 1,8 | skin tone |
| Accents | 15-21 | 7,8 | 1,8 | 127/120 |
| Shoulders | 22 | 0-15 | 0-9 | 126 |
| Head (upper) | 23-24 | 6-9 | 3-6 | skin |
| Head (lower) | 25-27 | 4-11 | 2-7 | skin |
| Hat brim | 28 | 0-15 | 0-9 | 126 |
| Hat band | 29 | 2-13 | 1-8 | 120 |
| Hat crown | 30-31 | 2-13 | 1-8 | 126 |

### Shared `buildHead()` Function
Used by both `generateCharacter` (full body) and `generateBust` (portrait). Builds:
- Skull (y=23-27, skin material)
- Hair (cases 0-7, mat 128)
- Eyes (cases 0-5, mat 109)
- Nose (cases 0-4, skin)
- Mouth (cases 0-4, mat 127)
- Glasses (cases 0-3, mat 109)
- Hat (cases 0-4: None, Flat Cap, Fedora, Boater, Wide Brim)
- Beard (cases 0-2, mat 128)
- Scar/Accessory (cases 0-7, mats 122/123/121/108)

---

## Group Assignment

### Pipeline (10 groups — Anatomy 1)
| GID | Name | Spatial Rule |
|-----|------|-------------|
| 0 | Body/Torso | Default fallback |
| 1 | Head | y ≥ 23 |
| 2 | Left Upper Arm | x < 3, y ≥ 17 |
| 3 | Right Upper Arm | x ≥ 13, y ≥ 17 |
| 4 | Left Thigh | x < 6, 6 ≤ y < 13 |
| 5 | Right Thigh | x ≥ 10, 6 ≤ y < 13 |
| 6 | Left Shin | x < 6, y < 6 |
| 7 | Right Shin | x ≥ 10, y < 6 |
| 8 | Left Forearm | x < 3, 13 ≤ y < 17 |
| 9 | Right Forearm | x ≥ 13, 13 ≤ y < 17 |

### Animator (6 groups — merged upper/lower)
| GID | Name | Pipeline Mapping |
|-----|------|-----------------|
| 0 | Body | 0 |
| 1 | Head | 1 |
| 2 | Left Arm | 2 + 8 (upper + forearm) |
| 3 | Right Arm | 3 + 9 (upper + forearm) |
| 4 | Left Leg | 4 + 6 (thigh + shin) |
| 5 | Right Leg | 5 + 7 (thigh + shin) |

**Mapping table**: `{0:0, 1:1, 2:2, 3:3, 4:4, 5:5, 6:4, 7:5, 8:2, 9:3}`

---

## Animation System

### Pivot Points (normalized 0-1 of model dims)
| Group | X | Y | Z | Description |
|-------|---|---|---|-------------|
| 1 (Head) | 0.5 | 0.78 | 0.5 | Neck pivot |
| 2 (L Arm) | 0.25 | 0.75 | 0.5 | Left shoulder |
| 3 (R Arm) | 0.75 | 0.75 | 0.5 | Right shoulder |
| 4 (L Leg) | 0.375 | 0.34 | 0.5 | Left hip |
| 5 (R Leg) | 0.625 | 0.34 | 0.5 | Right hip |

### Animation States (9)
| ID | Name | Key Parameters |
|----|------|---------------|
| 0 | Idle | (no movement) |
| 1 | Walking | armSwing, armFreq, legStride, legFreq |
| 2 | Looking | headYaw, headYawFreq, headPitch, headPitchFreq |
| 3 | Checking | headYaw, headYawFreq, headPitch, headPitchFreq |
| 4 | Aiming | headYaw, headPitch, armSwing |
| 5 | Crouching | headPitch, armSwingL/R, legStride |
| 6 | Flinching | headPitch, armSwing |
| 7 | Falling | legStrideL/R |
| 8 | Down | (static) |

### `computeGroupRotation(gid, dims, voxelSize, animState, animTime, animSpeed)`
Returns `{pivot, rot}` where pivot is a 3D point and rot is a 3×3 rotation matrix.
- Group 0 (Body): always returns `null` (no transform)
- Groups 1-5: returns rotation based on current state + time
- Walking: arms/legs use sine waves with opposite phase (PI offset)
- Aiming: static pose with fixed angles
- Crouching: static pose with bent legs

---

## JSON Formats

### Project File (animator `loadProject`)
```json
{
  "format": "character_animator_project",
  "version": 1,
  "dims": [16, 32, 10],
  "voxels": [[x, y, z, materialId], ...],
  "groups": [[x, y, z, groupId], ...],
  "pivots": { "1": {"x":0.5, "y":0.78, "z":0.5}, ... },
  "animParams": { "walk": {...}, "looking": {...}, ... }
}
```

### .stasset Export (pipeline)
```json
{
  "format": "stasset_export",
  "dims": [16, 32, 10],
  "voxels": [[x, y, z, materialId], ...]
}
```

### .groups Export (pipeline)
```json
{
  "format": "groups_export",
  "dims": [16, 32, 10],
  "groups": [[x, y, z, groupId], ...]
}
```

### .anim Export (pipeline)
```json
{
  "format": "anim_params",
  "pivots": {...},
  "params": {...}
}
```

---

## Loading Workflow (Animator)

1. Open `character_animator.html` in browser
2. Click **💾 Save / Load / Export** button
3. **Load Project** — loads full project (voxels + groups + pivots + params)
4. **Import .stasset JSON** — loads voxels only; if groups/pivots/params present in file, they are loaded too
5. Toggle **Animated Mesh** mode to see group-based transforms
6. Select animation state (Walking, Aiming, etc.) to preview

### `character_hoodlum_0.json`
Pre-generated project file with 2,404 voxels:
- Body: 820, Head: 624, L Arm: 246, R Arm: 246, L Leg: 234, R Leg: 234
- Generated by `gen_hood0.py` from Hood 0 portrait features

---

## Key Revelations (August 2026 Session)

1. **Baked data removed**: `HOOD0_VOXELS` hardcoded string and `loadHood0()` function deleted from pipeline. All generation is now procedural.
2. **`buildHead()` shared**: Head-building logic extracted so both bust (portrait) and full-body use identical code.
3. **`setV` bug fixed**: Hat generation was calling `setV(x,y,bodyMat)` (3 args) instead of `setV(x,y,z,bodyMat)` (4 args), causing model deformation.
4. **Group mapping**: Pipeline's 10 groups must be mapped to animator's 6 groups when generating JSON for the animator.
5. **`importStasset` fixed**: Previously ignored groups in JSON files, setting all voxels to group 0. Now loads groups, pivots, and animParams when present.
6. **Portrait catalog decoded**: Feature presets extracted from Gangsters 1998 portrait grids — hats (Flat Cap, Fedora, Boater, Wide Brim), hair, facial hair, eyewear, etc.
7. **Generation script**: `gen_hood0.py` replicates the pipeline's JS generation logic in Python to produce animator-ready JSON without needing Node.js.

---

## Material IDs Reference

| ID | Material | Usage |
|----|----------|-------|
| 103 | Stucco | Skin (dark), Civilian body |
| 102 | Concrete | Skin (medium) |
| 109 | Dark Glass | Eyes, glasses |
| 120 | Painted Red | Hat band, police body, accents |
| 121 | Painted Green | Tattoo |
| 122 | Regolith | Skin (light), scar |
| 123 | Basalt | Earring, gold tooth |
| 126 | Hoodlum Blue | Hoodlum body, hat brim/crown |
| 127 | Uniform Grey | Mouth, accents |
| 128 | Hair | Hair, beard |
| 108 | Overcoat | Overcoat body, cigar |
