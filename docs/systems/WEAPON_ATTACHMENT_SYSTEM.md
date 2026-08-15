# Weapon Attachment & Grip Point System

**Status**: Design (2026-08-14)
**Depends on**: `WEAPON_ITEM_MODEL_STANDARD.md`, `CHARACTER_SYSTEM.md`, character pose engine FK

---

## Overview

A voxel-painted attachment point system for aligning weapons (and future accessories)
to character hands at runtime. Uses the same painting workflow as groups/regions —
no skeletal mesh sockets, no hardcoded Unity transforms.

The core idea: **paint marker voxels** on both the weapon and character to define
named attachment points. At runtime, Unity aligns the weapon's grip point to the
character's posed hand point, inheriting rotation from the FK chain.

---

## Design Principles

1. **Data-driven** — all attachment points stored in JSON, not hardcoded in C#
2. **Voxel-painted** — uses the same click-a-voxel workflow as group/region painting
3. **Per-weapon customizable** — a pistol grip differs from a rifle grip; each weapon
   defines its own points
4. **Works with animation** — grip point moves with the posed hand via FK, not rest pose
5. **No bone hierarchy needed** — the FK system already computes hand world position
6. **Extensible** — supports multiple named points for two-handed weapons, cheek weld,
   sling mounts, optics, etc.

---

## Attachment Point Types

### Weapon-Side Points

| Point Name     | Purpose                                          | Required |
|----------------|--------------------------------------------------|----------|
| `grip_right`   | Where the right hand grips the weapon            | Yes      |
| `grip_left`    | Where the left hand grips (foregrip/stock)       | No       |
| `muzzle`       | Barrel tip — muzzle flash origin, aim ray origin | Yes      |
| `cheek_weld`   | Where the cheek rests on the stock               | No       |
| `sling_mount`  | Sling attachment point                           | No       |
| `optics_mount` | Optic/sight rail position                        | No       |

### Character-Side Points

| Point Name      | Purpose                                          | Required |
|-----------------|--------------------------------------------------|----------|
| `right_hand`    | Right hand position (primary grip)               | Yes      |
| `left_hand`     | Left hand position (support grip)                | No       |
| `right_shoulder`| Shoulder stock position (rifle shouldering)      | No       |
| `cheek`         | Face/cheek position for aiming weld              | No       |

---

## JSON Format

### Weapon JSON (`SW_Model_10.json`)

```json
{
  "format": "steelcity_stasset",
  "version": 1,
  "name": "S&W Model 10 Revolver",
  "assetType": "prop",
  "voxelSize": 0.01,
  "dims": [24, 12, 6],
  "attachmentPoints": {
    "grip_right": { "x": 2, "y": 5, "z": 2 },
    "muzzle":     { "x": 23, "y": 5, "z": 2 }
  },
  "voxels": [...]
}
```

### Character JSON (`Civilian1.json`)

```json
{
  "format": "steelcity_character",
  "version": 1,
  "name": "Vinny",
  "dims": [96, 96, 96],
  "attachmentPoints": {
    "right_hand": { "x": 72, "y": 54, "z": 48 }
  },
  "voxels": [...]
}
```

### Coordinate Space

- All attachment points are in **voxel-local space** (same as voxel coordinates)
- Unity converts to world space using `voxelSize` and the character's world position
- For posed characters, the FK chain transforms the rest-position point to the
  animated world position

---

## Runtime Alignment (Unity)

### One-Handed Weapon (Pistol)

```
1. Pose character via FK → right_hand voxel moves to world position (hx, hy, hz)
2. Read weapon's grip_right voxel → local position (gx, gy, gz)
3. Position weapon so grip_right maps to right_hand:
   weapon.transform.position = handWorld - (gripLocal * voxelSize)
4. Inherit rotation from FK chain (gid 3 = right arm → gid 9 = right forearm)
5. Apply weapon-specific rotational offset if needed
```

### Two-Handed Weapon (Rifle)

```
1. Pose character via FK → right_hand and left_hand world positions computed
2. Align weapon's grip_right → character's right_hand (primary anchor)
3. Use weapon's grip_left → character's left_hand for rotational alignment
    (the vector from grip_right to grip_left defines the weapon's "up" axis)
4. Optionally align cheek_weld → character's cheek for head positioning
```

### Rotation Derivation

The weapon's orientation is derived from two painted points:

- **Forward axis**: `normalize(muzzle - grip_right)` — barrel direction
- **Up axis**: `normalize(grip_left - grip_right)` — perpendicular to barrel
  (for two-handed weapons); for one-handed, use canonical +Z from the model
- **Right axis**: `cross(forward, up)`

This forms a rotation matrix that orients the weapon in the character's hand
without needing a separate "forward marker" voxel — the muzzle point already
defines forward.

---

## Painting Workflow (Voxel Editor)

### Tool: Attachment Point Painter

1. Select "Attachment Point" tool from the toolbar (new tool mode)
2. Select a named point type from a dropdown (e.g., `grip_right`, `muzzle`)
3. Click a voxel on the model → that voxel coordinate is saved as the point
4. Visual marker (colored sphere/icon) appears at the painted voxel
5. Only one voxel per named point (clicking again moves the point)
6. Points are saved to JSON on export

### Visual Feedback

- Each point type gets a distinct color/icon:
  - `grip_right`: green circle
  - `grip_left`: blue circle
  - `muzzle`: red arrow (forward direction)
  - `cheek_weld`: yellow circle
- Points render as small overlays (not voxels) so they don't affect the model
- Hovering shows the point name and coordinates

### Character Preview Integration

When a character is loaded in the voxel editor's character preview:
- Painted `right_hand` point is highlighted on the character
- When a weapon is also loaded, the preview can show the weapon aligned to the hand
- This gives real-time visual confirmation of grip alignment before Unity import

---

## Grip Pose Integration

The existing `AIM_WEAPON_PRESETS` system defines arm angles per weapon type.
The attachment point system complements this:

- **AIM_WEAPON_PRESETS**: How the character's arms/pose should look (armSwing, elbowBend, etc.)
- **Attachment points**: Where the weapon sits in the posed hand

Together: the preset poses the hand, and the attachment point places the weapon
in the correct position within that posed hand.

### Future: Per-Weapon Grip Poses

Each weapon JSON could carry its own grip pose override:

```json
{
  "gripPose": {
    "armSwingL": -1.4,
    "elbowBendL": 0.3,
    "shoulderReachL": 0.0
  }
}
```

This would allow a pistol vs. rifle to have different arm angles without
separate preset entries — the weapon self-describes its ideal grip pose.

---

## Unity Implementation Plan

### Phase 1: Data Loading

- Extend `VoxelCharacter.cs` to load `attachmentPoints` from character JSON
- Extend weapon loading (wherever items are loaded) to read `attachmentPoints`
- Store points as `Dictionary<string, Vector3Int>` (voxel-local coords)

### Phase 2: One-Handed Alignment

- After posing, compute world position of `right_hand` point via FK
- Position weapon so `grip_right` maps to `right_hand` world position
- Inherit rotation from forearm group (gid 9)
- Test with S&W Model 10 + Civilian1 in aiming pose

### Phase 3: Two-Handed Alignment

- Compute both `right_hand` and `left_hand` world positions
- Use dual-point alignment for weapon rotation
- Add `left_hand` attachment point to character model
- Test with rifle weapon

### Phase 4: Editor Painting Tool

- Implement attachment point painter in voxel editor
- Add visual markers for painted points
- Add real-time grip preview in character preview panel
- Export/import attachment points in JSON

---

## Relationship to Existing Systems

| System | Role |
|--------|------|
| `WEAPON_ITEM_MODEL_STANDARD.md` | Defines canonical weapon orientation (barrel +X, lying flat) |
| Character pose engine (`computeGroupRotation`) | Computes FK transforms for posed limbs |
| `AIM_WEAPON_PRESETS` | Defines arm angles per weapon type (pistol, dual, rifle) |
| `ASSET_TYPE_PRESETS` (voxel editor) | Defines voxelSize per asset type (0.01 for both characters and props) |
| **Attachment points (this doc)** | **Defines where weapon connects to character hand** |

---

## Open Questions

1. **Hand shape**: Should the character's hand voxels change shape (open vs. closed grip)
   based on the weapon type? Or is the grip pose purely arm-angle based?
2. **Weapon switching**: When the player picks up a different weapon, does the character
   need to re-pose, or just re-align the weapon to the existing hand position?
3. **Holstered/stowed**: Do we need separate attachment points for hip holster, back sling,
   etc.? Or is that a character-side point, not a weapon-side point?
4. **Scale tolerance**: If a weapon is modeled at a different voxelSize than the character,
   how do we reconcile? (Currently both are 0.01, so this shouldn't be an issue.)
