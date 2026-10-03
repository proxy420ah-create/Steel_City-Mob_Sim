# Weapon & Item Model Standard

**Created**: August 14, 2026
**Status**: Active — defines voxel modeling standards for weapons, items, and props

---

## 1. Overview

This document defines the voxel modeling standards for all non-building, non-character assets in Steel City: weapons, throwables, cover props, map decorations, and vehicle debris. These assets use a dedicated "Item / Decor" asset type in the voxel editor at 0.005m/voxel — twice the density of upscaled characters (96³ at 0.01m/voxel) — for detailed weapon modeling. Because item and character voxel scales differ, weapons attach to hands via **transform-based alignment** (attachment points + FK pose, see `WEAPON_ATTACHMENT_SYSTEM.md`) rather than direct buffer compositing. Every model file self-describes its scale via the `voxelSize` JSON field.

---

## 2. Voxel Scale

| Asset Type | Voxel Size | Current Dims | Purpose |
|------------|-----------|--------------|---------|
| Building | 0.1m/voxel | 96×68×96 | City structures |
| Character | 0.01m/voxel | 96×96×96 | All character entities |
| **Item / Decor** | **0.005m/voxel** | **48×26×10** (default) | Weapons, props, decorations |

**Why 0.005m/voxel for items (raised Oct 2, 2026):**
- A S&W Model 10 (~24cm long) is 48 voxels — enough resolution for cylinder, ejector rod, hammer, trigger guard, front sight, and a shaped grip
- At 0.01m/voxel the same pistol was 24 voxels — too coarse to distinguish the cylinder from the frame or fit a trigger inside the guard
- Per-file `voxelSize` means item scale is a per-model decision, not a global constant — mixing 0.01 and 0.005 items is valid
- Item scale differs from character scale on purpose: weapons align to hands via attachment-point transforms, so no scale conversion shortcut is needed

**Default dims [48, 26, 10] at 0.005m/voxel:**
- X=48 → 24cm (barrel length — enough for a revolver or M1911A1 lying flat)
- Y=26 → 13cm (side profile height — frame top to grip bottom)
- Z=10 → 5cm (thickness — cylinder is the widest part)

---

## 3. Weapon Classes

### 3.1 Original Game Weapons (from hit/damage tables)

| Weapon | Game Entry | Real-World Basis | Approx. Length | Voxel Length |
|--------|-----------|-----------------|----------------|-------------|
| Pistol | "Pistol" | Colt Detective Special / S&W Model 10 (revolver, .38 Special, 2" barrel) | ~17cm | ~34 voxels |
| Twin Pistols | "Twin Pistols" | Dual Colt M1911A1 (semi-auto, .45 ACP) | ~21cm each | ~42 voxels each |
| Tommy Gun | "Tommy Gun" | Thompson M1921/M1928 (.45 ACP, full auto) | ~81cm (w/ 10" barrel) | ~162 voxels |
| Rifle | "Rifle" | Winchester Model 1895 or Springfield 1903 | ~110cm | ~220 voxels |
| Shotgun | "Shotgun" | Winchester Model 1897 (pump-action, 12 gauge) | ~100cm | ~200 voxels |
| Knife | "pistol whip" / melee | Switchblade or folding knife | ~25cm (open) | ~50 voxels |
| Bat / Crowbar | melee | Baseball bat or standard crowbar | ~80cm | ~160 voxels |

### 3.2 Base "Pistol" — Colt Revolver

The base "Pistol" in Steel City is a **Colt Detective Special** or **Smith & Wesson Model 10** — the ubiquitous civilian/police revolvers of the 1920s. Cheap, reliable, widespread. A revolver is simpler to voxelize than a semi-auto: cylinder + barrel + grip frame, no slide.

**Key visual features for voxelization:**
- ~24cm (9.4") overall length for the 4"-barrel Model 10 service variant (current `SW_Model_10.json`); ~17cm for a snub-nose 2" variant if modeled later
- Cylinder (6-shot, round) — the dominant middle feature, rendered in Aged Metal for contrast against the Dark Iron frame
- Barrel (slim round tube) + top rib + separate ejector rod housing suspended below — the "two tubes" profile
- Grip frame (angled, ~110° from barrel axis) — wood stocks with exposed metal backstrap
- Hammer (exposed spur, rear top, behind the topstrap)
- Trigger guard (D-loop) with brass trigger inside
- Front sight blade at the muzzle — brass marks the "forward" end

**Generator**: `Tools/gen_sw_model10.py` produces the current model parametrically — part positions/sizes are tunable constants; re-run to iterate.

**"Twin Pistols"** = dual Colt M1911A1s — the gangster film trope. Semi-auto with slide, 7-round magazine, ~21cm overall length. Modeled as a separate weapon entry, not a duplicate of the revolver.

### 3.3 Throwables

| Item | Real-World Basis | Approx. Size | Voxel Size |
|------|-----------------|-------------|-----------|
| Bomb (Molotov) | Glass bottle + rag wick | ~25cm tall | 25 voxels |
| TNT Bundle | Dynamite sticks bundled | ~20cm long | 20 voxels |

### 3.4 Cover Props

| Item | Approx. Size | Voxel Dims (at 0.005m) |
|------|-------------|----------------------|
| Barrel (oil drum) | 60cm × 90cm | 120×180×120 |
| Crate (wooden) | 50cm³ | 100×100×100 |
| Dumpster | 150cm × 100cm × 80cm | 300×200×160 |

### 3.5 Map Decorations

| Item | Approx. Size | Voxel Dims (at 0.005m) |
|------|-------------|----------------------|
| Street lamp | 400cm tall | 80×800×80 |
| Fire hydrant | 50cm tall | 40×100×40 |
| Trash can | 60cm × 80cm | 120×160×120 |
| Phone booth | 90cm × 220cm × 90cm | 180×440×180 |

---

## 4. Orientation Conventions

### Weapons (handheld) — Lying Flat (Canonical)

Weapons are authored **lying flat on the ground plane** — this is the default world state for a dropped weapon and the easiest orientation to model (recognizable side silhouette in XY).

```
    SIDE PROFILE (XY plane — what you see looking down at the gun)
    
    Y (side profile height)
    ↑
    │   ┌──────┬───────────┐
    │   │HAMMER│  BARREL   │
    │   └──┬───┴───────────┘
    │      │  CYLINDER      │
    │   ┌──┴───────────────┐
    │   │   FRAME / GRIP   │
    │   └──────────────────┘
    │
    └─────────────────────────► X (length, barrel direction)
    
    Z (thickness — into the page, 4-6 voxels for a revolver)
```

- **+X** = barrel/muzzle direction (forward) — length axis
- **+Y** = side profile height (frame top to grip bottom) — visible from above
- **+Z** = thickness (cylinder/grip width viewed from top) — thin axis
- Bottom of model sits at Y=0 (floor-anchored, same as all other asset types)
- Grip center should be at a known offset for attachment to character hand

**Why lying flat:**
- Dropped weapons lie flat — that's the default world state
- Side profile is the recognizable silhouette (barrel, cylinder, grip) — easiest to model in XY
- Hand attachment uses a transform rotation anyway — no extra cost vs standing orientation
- Consistent with props convention: +Y is up, bottom at Y=0

**Recommended dims per weapon class (lying flat):**

| Weapon | Dims (X×Y×Z) | Notes |
|--------|-------------|-------|
| Revolver (S&W Model 10) | 48×26×10 | 24cm overall, 13cm side profile, 5cm thick |
| M1911A1 | 48×28×12 | 21cm length, slightly taller slide profile |
| Tommy Gun | 180×50×20 | 81cm with drum magazine, needs larger grid |
| Rifle | 240×40×16 | 110cm long, thin profile |
| Shotgun | 220×40×16 | 100cm long, similar to rifle |
| Knife (open) | 50×16×6 | 25cm open, thin blade |

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
- **Grip capacity**: A revolver grip (~3cm wide = ~6 item voxels at 0.005m) fits comfortably in the 8-12cm hand space

**Attachment approach**: When a character enters Aiming state (animation state 4), the weapon is aligned to the posed hand via **attachment points** (see `WEAPON_ATTACHMENT_SYSTEM.md`): the weapon's `grip_right` point is transformed to the character's posed `right_hand` position, and rotation comes from the muzzle→grip vector. Because item voxels (0.005m) are half the size of character voxels (0.01m), the weapon renders as its own instanced group at its declared `voxelSize` — no resampling, no detail loss. Buffer compositing is no longer used.

---

## 7. Voxel Editor Setup

### Adding the "Item / Decor" Asset Type

The voxel editor (`VoxelAssetStudio/voxel_editor.html`) now supports three asset types:

1. **Building** (🏢, 0.1m/voxel, 96×68×96 default)
2. **Character** (🧍, 0.01m/voxel, 96×96×96 default — matches the upscaled Civilian1 standard)
3. **Item / Decor** (🔫, 0.005m/voxel, 48×26×10 default)

### Modeling Workflow

1. Open `voxel_editor.html` in a browser
2. Select "Item / Decor" from the Asset dropdown
3. The grid initializes at 48×26×10 with 0.005m/voxel (S&W Model 10 auto-loads as the reference default)
4. For larger weapons (rifle, shotgun, Tommy Gun), use Set Volume Size to expand the grid (e.g., 240×40×16 for a rifle)
5. Model the weapon following the orientation conventions (§4)
6. Use "Show all" in the material palette to access weapon-appropriate materials (Dark Iron, Aged Metal, Gold/Brass, Dark Wood)
7. Paint attachment points with the Item Part tool (🔧): grip_right (required), grip_left (two-handed), muzzle
8. Export as `.stasset` JSON (includes `assetType: "prop"` and `voxelSize: 0.005`)

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
  "voxelSize": 0.005,
  "dims": [48, 26, 10],
  "materials": [...],
  "voxels": [[x, y, z, materialId], ...],
  "itemParts": {"x,y,z": partId, ...}
}
```

Items do not need `groups`, `regions`, `pivots`, or `animParams` — they are static models (no skeletal animation). If an item needs simple animation (e.g., a spinning barrel on a discarded weapon), it can be handled as a transform rotation on the GameObject, not voxel-level animation. `itemParts` holds painted attachment points (see `WEAPON_ATTACHMENT_SYSTEM.md` for part IDs and alignment semantics).

---

## 9. Attachment to Characters

### Transform-Based Attachment (Current Standard)

With items at 0.005m/voxel and characters at 0.01m/voxel, weapons are **not** composited into the character's posed buffer. Instead:

1. **Character enters Aiming state** (animation state 4)
2. **Weapon renders as its own instanced group** at its declared `voxelSize` — full 0.005m detail preserved
3. **Alignment via attachment points**: `grip_right` (weapon) → `right_hand` (character, computed via forearm FK), rotation from muzzle→grip vector
4. One draw call per weapon type — still instanced, so N armed characters cost no extra draws

This supersedes the earlier compositing plan, which relied on characters and items sharing 0.01m/voxel. The transform approach keeps full item detail and works for any per-model `voxelSize`.

### Deprecated: Buffer Compositing

The original plan wrote weapon voxels directly into the character's posed buffer at the hand position (requires identical voxel scale — only valid for legacy 0.01m items). Retained here for history:

- Position = forearm pivot + forearm rotation × hand offset; weapon voxels overwrite character voxels in the hand region
- Would have required a CSPose compute pass extension and a hand anchor convention
- Not pursued: downsampling a 0.005m item to 0.01m at composite time destroys the detail the finer scale exists to provide

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
