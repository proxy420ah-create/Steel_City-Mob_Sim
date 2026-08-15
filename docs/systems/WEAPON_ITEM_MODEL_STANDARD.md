# Weapon & Item Model Standard

**Created**: August 14, 2026
**Status**: Active — defines voxel modeling standards for weapons, items, and props

---

## 1. Overview

This document defines the voxel modeling standards for all non-building, non-character assets in Steel City: weapons, throwables, cover props, map decorations, and vehicle debris. These assets use a dedicated "Item / Decor" asset type in the voxel editor at 0.01m/voxel — the same voxel scale as upscaled characters (96³ at 0.01m/voxel), enabling direct compositing of weapon models into character hand regions without scale conversion.

---

## 2. Voxel Scale

| Asset Type | Voxel Size | Current Dims | Purpose |
|------------|-----------|--------------|---------|
| Building | 0.1m/voxel | 96×68×96 | City structures |
| Character | 0.01m/voxel | 96×96×96 | All character entities |
| **Item / Decor** | **0.01m/voxel** | **24×16×8** (default) | Weapons, props, decorations |

**Why 0.01m/voxel for items:**
- A Colt Detective Special revolver is ~17cm long → 17 voxels — enough resolution for cylinder, barrel, grip, trigger guard
- At character scale (0.02m/voxel pre-upscale), the same pistol would be ~8 voxels — too blocky for a held weapon
- At 0.01m/voxel, items get double the resolution while remaining small enough to model quickly
- Characters and items share the same voxel scale → weapon models can be composited into character models without scale conversion

**Default dims [24, 16, 8] at 0.01m/voxel:**
- X=24 → 24cm (enough for a Tommy Gun barrel assembly or a rifle lying flat)
- Y=16 → 16cm (enough for a revolver standing on its grip)
- Z=8 → 8cm (depth — enough for a cylinder diameter or stock thickness)

---

## 3. Weapon Classes

### 3.1 Original Game Weapons (from hit/damage tables)

| Weapon | Game Entry | Real-World Basis | Approx. Length | Voxel Length |
|--------|-----------|-----------------|----------------|-------------|
| Pistol | "Pistol" | Colt Detective Special / S&W Model 10 (revolver, .38 Special, 2" barrel) | ~17cm | ~17 voxels |
| Twin Pistols | "Twin Pistols" | Dual Colt M1911A1 (semi-auto, .45 ACP) | ~21cm each | ~21 voxels each |
| Tommy Gun | "Tommy Gun" | Thompson M1921/M1928 (.45 ACP, full auto) | ~81cm (w/ 10" barrel) | ~81 voxels |
| Rifle | "Rifle" | Winchester Model 1895 or Springfield 1903 | ~110cm | ~110 voxels |
| Shotgun | "Shotgun" | Winchester Model 1897 (pump-action, 12 gauge) | ~100cm | ~100 voxels |
| Knife | "pistol whip" / melee | Switchblade or folding knife | ~25cm (open) | ~25 voxels |
| Bat / Crowbar | melee | Baseball bat or standard crowbar | ~80cm | ~80 voxels |

### 3.2 Base "Pistol" — Colt Revolver

The base "Pistol" in Steel City is a **Colt Detective Special** or **Smith & Wesson Model 10** — the ubiquitous civilian/police revolvers of the 1920s. Cheap, reliable, widespread. A revolver is simpler to voxelize than a semi-auto: cylinder + barrel + grip frame, no slide.

**Key visual features for voxelization:**
- ~17cm (6.7") overall length (snub nose 2" barrel)
- Cylinder (6-shot, round) — the dominant middle feature
- Barrel (short, thick cylinder on top front)
- Grip frame (angled, ~110° from barrel axis)
- Hammer (small, at rear top)
- Trigger guard (small D-loop)

**"Twin Pistols"** = dual Colt M1911A1s — the gangster film trope. Semi-auto with slide, 7-round magazine, ~21cm overall length. Modeled as a separate weapon entry, not a duplicate of the revolver.

### 3.3 Throwables

| Item | Real-World Basis | Approx. Size | Voxel Size |
|------|-----------------|-------------|-----------|
| Bomb (Molotov) | Glass bottle + rag wick | ~25cm tall | 25 voxels |
| TNT Bundle | Dynamite sticks bundled | ~20cm long | 20 voxels |

### 3.4 Cover Props

| Item | Approx. Size | Voxel Dims (at 0.01m) |
|------|-------------|----------------------|
| Barrel (oil drum) | 60cm × 90cm | 60×90×60 |
| Crate (wooden) | 50cm³ | 50×50×50 |
| Dumpster | 150cm × 100cm × 80cm | 150×100×80 |

### 3.5 Map Decorations

| Item | Approx. Size | Voxel Dims (at 0.01m) |
|------|-------------|----------------------|
| Street lamp | 400cm tall | 40×400×40 |
| Fire hydrant | 50cm tall | 20×50×20 |
| Trash can | 60cm × 80cm | 60×80×60 |
| Phone booth | 90cm × 220cm × 90cm | 90×220×90 |

---

## 4. Orientation Conventions

### Weapons (handheld)

```
        BARREL POINTS +X
        ────────────────►
        
        Z (depth, thin)
        │
        │  ┌─────────────────┐
        │ │                 │
        │ │   CYLINDER      │  ← Y (height, grip vertical)
        │ │                 │
        │  └──┐         ┌───┘
        │     │  GRIP   │
        │     │  (angled)│
        │     └─────────┘
        │
        └─── X (length, barrel direction)
```

- **+X** = barrel/muzzle direction (forward)
- **+Y** = grip up (top of weapon)
- **+Z** = thin axis (side profile width)
- Grip center should be at a known offset for attachment to character hand

### Props (cover, decorations)

- **+Y** = up (vertical, same as buildings and characters)
- **+X** = primary facing direction
- **+Z** = depth
- Bottom of model sits at Y=0 (floor-anchored, same convention as buildings)

---

## 5. Material Palette

Items use the same material system as buildings and characters. The "Show all" checkbox in the voxel editor allows pulling from any category.

**Key materials for weapons:**

| Material ID | Name | Hex | Use |
|------------|------|-----|-----|
| 109 | Dark Iron | #473d38 | Gunmetal, barrel, frame |
| 110 | Aged Metal | #6b665b | Worn metal, cylinder |
| 123 | Gold/Brass | #c69e33 | Shell casings, fittings, trigger |
| 106 | Dark Wood | #4c2d19 | Grip stocks (wooden handles) |
| 107 | Light Wood | #996b3f | Cricket bat, wooden crate |
| 108 | Weathered Wood | #6b5b42 | Aged wood props |
| 120 | Painted Red | #721e19 | Molotov rag, accent details |
| 121 | Painted Green | #26472d | Military equipment |
| 122 | Painted Brown | #381e14 | Leather grip wraps |

**Prop-specific materials** (to be added to palette as needed):
- Glass (bottle, phone booth) — reuse ID 112 (Window Glass) or 114 (Storefront Glass)
- Concrete (barriers) — reuse ID 102
- Tar (road patches) — reuse ID 118

---

## 6. Character Hand Dimensions

The character model (Civilian1.json, 96×96×96 at 0.01m/voxel) has a "Hands" region (region ID 5). Based on the body group structure:

- **Hand width**: ~8-12 voxels at 0.01m/voxel = 8-12cm (real human hand is ~8-10cm)
- **Hand position**: At the end of the forearm group (groups 8/9 — Left/Right Forearm)
- **Grip capacity**: A revolver grip (~7cm = 7 voxels at item scale) fits comfortably in the 8-12 voxel hand space

**Attachment approach**: When a character enters Aiming state (animation state 4), the weapon model is composited into the character's posed buffer at the hand position, aligned with the forearm rotation. The shared voxel scale (0.01m/voxel for both) means no scale conversion is needed — item voxels map directly to character buffer voxels.

---

## 7. Voxel Editor Setup

### Adding the "Item / Decor" Asset Type

The voxel editor (`VoxelAssetStudio/voxel_editor.html`) now supports three asset types:

1. **Building** (🏢, 0.1m/voxel, 96×68×96 default)
2. **Character** (🧍, 0.02m/voxel, 16×32×10 default — note: characters are now authored at 96³ but the editor default remains the original hoodlum dims for backward compatibility)
3. **Item / Decor** (🔫, 0.01m/voxel, 24×16×8 default)

### Modeling Workflow

1. Open `voxel_editor.html` in a browser
2. Select "Item / Decor" from the Asset dropdown
3. The grid initializes at 24×16×8 with 0.01m/voxel
4. For larger weapons (rifle, shotgun, Tommy Gun), use Set Volume Size to expand the grid (e.g., 120×20×12 for a rifle)
5. Model the weapon following the orientation conventions (§4)
6. Use "Show all" in the material palette to access weapon-appropriate materials (Dark Iron, Aged Metal, Gold/Brass, Dark Wood)
7. Export as `.stasset` JSON (includes `assetType: "prop"` and `voxelSize: 0.01`)

### Auto-Detection

The editor's `inferAssetType` function auto-detects asset type from dimensions when loading files without an explicit `assetType` field:
- Max dimension ≤ 20 → `prop` (items/weapons are small)
- Max dimension ≤ 40 → `character`
- Max dimension > 40 → `building`

---

## 8. File Storage

Item models are stored in:
```
Assets/StreamingAssets/voxel_items/{ItemName}.json
```

This is a separate folder from characters (`voxel_characters/`) and buildings (loaded via chunk system). The VoxelChunkManager's `RegisterInstancedCharacter` API can be extended to register items with their own `InstancedGroup` — items share the same GPU instancing pipeline as characters.

### File Format

Same consolidated JSON format as characters:
```json
{
  "format": "steelcity_stasset",
  "version": 1,
  "name": "Colt Detective Special",
  "assetType": "prop",
  "voxelSize": 0.01,
  "dims": [24, 16, 8],
  "materials": [...],
  "voxels": [[x, y, z, materialId], ...]
}
```

Items do not need `groups`, `regions`, `pivots`, or `animParams` — they are static models (no skeletal animation). If an item needs simple animation (e.g., a spinning barrel on a discarded weapon), it can be handled as a transform rotation on the GameObject, not voxel-level animation.

---

## 9. Attachment to Characters

### Future: Compositing Approach

When a character equips a weapon:

1. **Character enters Aiming state** (animation state 4)
2. **Weapon model is composited** into the character's posed voxel buffer at the hand position
3. **Position calculation**: Hand position = forearm pivot + forearm rotation × hand offset
4. **Rotation**: Weapon barrel aligns with forearm forward direction
5. **Voxel writing**: Weapon voxels are written into the posed buffer at the computed position, overwriting any character voxels in that region

This requires:
- A known hand anchor point in the character model (per-group offset for forearm groups 8/9)
- A known grip center in the weapon model (stored as metadata or convention: grip center = [0, 0, 0] local origin)
- The CSPose compute shader to be extended with an optional item-composite pass

### Simpler Alternative: Separate Render

Until compositing is implemented, weapons can be rendered as separate instanced volumes:
- Each weapon gets its own `InstancedGroup` in `VoxelChunkManager`
- The weapon GameObject is parented to the character's forearm bone
- The weapon renders as a separate raymarch volume, positioned at the hand

This is simpler but adds a draw call per weapon type (not per instance — still instanced).

---

## 10. Reference Images

For modeling reference, search for:
- **Base Pistol**: "Colt Detective Special 1920s" or "S&W Model 10 snub nose"
- **Twin Pistols**: "Colt M1911A1 1920s"
- **Tommy Gun**: "Thompson M1921 with drum magazine"
- **Shotgun**: "Winchester Model 1897 trench gun"
- **Rifle**: "Springfield 1903" or "Winchester Model 1895"
- **Cover props**: "1920s oil drum", "wooden crate prohibition era"
- **Map decorations**: "1920s street lamp", "1920s fire hydrant", "1920s phone booth"
