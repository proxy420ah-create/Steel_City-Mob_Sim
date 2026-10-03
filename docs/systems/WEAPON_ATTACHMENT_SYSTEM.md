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
  "format": "steelcity_item",
  "version": 1,
  "name": "SW_Model_10",
  "assetType": "prop",
  "voxelSize": 0.005,
  "dims": [48, 26, 10],
  "attachmentPoints": {
    "grip_right": { "x": 9, "y": 5, "z": 7 },
    "grip_left":  { "x": 9, "y": 5, "z": 2 },
    "muzzle":     { "x": 47, "y": 20, "z": 4 }
  },
  "itemParts": { "9,5,7": 1, "9,5,2": 2, "47,20,4": 3 },
  "voxels": [...]
}
```

**Two related fields, one source of truth:**

- `attachmentPoints` — named `{x,y,z}` points; **the runtime/Unity format**.
- `itemParts` — `"x,y,z" → partId` painted-voxel map; the editor's working
  format. Painting a small cluster (e.g. a 4×1×1 grip region) is fine —
  export reduces each named point to the **centroid** of its painted voxels.
  The editor reconstructs painted voxels from `attachmentPoints` when
  `itemParts` is absent, so either field survives a round-trip.

### Character JSON (`Civilian1.json`)

```json
{
  "format": "steelcity_character",
  "version": 1,
  "name": "Vinny",
  "assetType": "character",
  "voxelSize": 0.01,
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

## Painting Workflow (Voxel Editor) — IMPLEMENTED

### Tool: Attachment Point Painter (🔧 / G key)

1. Select the **🔧 Attach Pts** tool (G key)
2. Open the **Attach Pts** right-panel tab and pick a named point
   - **Item mode**: `grip_right`, `grip_left`, `muzzle`, `cheek_weld`,
     `sling_mount`, `optics_mount`
   - **Character mode**: `right_hand`, `left_hand`, `right_shoulder`, `cheek`
3. Click voxels to tag them — a single voxel or a small region (e.g. a 4×1×1
   grip zone on the hand). The painted region's **centroid** becomes the
   exported `attachmentPoints` entry.
4. **🔧 Parts** view mode colors tagged voxels by point type
5. Points save into the model JSON as both `itemParts` (painted map) and
   `attachmentPoints` (named centroids for Unity)

### Character workflow

Paint `right_hand` as a small cluster where the palm wraps a grip — the
centroid lands on the hand's interior centerline, which is what the weapon
`grip_right` point aligns to. Same for `left_hand` (two-handed weapons).
`right_shoulder` and `cheek` are optional rifle-aiming anchors.

### Visual Feedback

- **🔧 Parts** view mode recolors tagged voxels by point type:
  - `grip_right` / `right_hand`: red, `grip_left` / `left_hand`: green,
    `muzzle`: cyan, `cheek_weld`: amber, `sling_mount`: magenta,
    `optics_mount` / `cheek`: yellow
- Hovering a voxel shows its current tag (`Part: X | Current: Y`)
- Markers are metadata on existing voxels — they never change the model's
  silhouette or material colors

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

### Phase 4: Editor Painting Tool — ✅ DONE

- ✅ Attachment point painter (🔧 / G) with Attach Pts tab — works in both
  item mode (weapon anchors) and character mode (hand/shoulder/cheek anchors)
- ✅ Part view mode colors tagged voxels by point
- ✅ Export/import: `itemParts` painted map + `attachmentPoints` named
  centroids, both round-tripped through JSON
- ✅ Real-time grip preview — **🔗 → R/L hand** buttons in the Reference
  Preview render a copy of the editable item bound to the reference model's
  painted hand point: grip lands on the hand, forward axis continues the
  rest forearm direction, pose rotations inherited through the FK chain.
  Follows pose changes and model edits live.

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
