# Model Design Standard — Source of Truth

**Version**: 2.0 | **Date**: August 12, 2026 | **Status**: 🔒 MASTER REFERENCE

---

## Purpose

This document is the **single source of truth** for scale, proportion, orientation, and door
conventions across all Steel City: Mob Sim voxel models (buildings, characters, vehicles). It
exists because an audit (Aug 8, 2026) found real drift between documented standards and what
generators actually produce — most visibly, buildings use three different door heights (4v, 5v,
8v) with no consistent rule tying them back to NPC scale.

**Rule going forward**: any new model, or any rework of an existing model, must conform to this
document. If a new requirement doesn't fit, **update this document first**, then update the
generator. Don't let generators and docs drift apart again.

This document supersedes the door-size table in `MOB_SIM_SCALE_STANDARD.md` (kept for the voxel
size constants and reference object list, which are still accurate) and consolidates orientation
rules that were previously scattered across code docstrings and `COORDINATE_SYSTEM.md`.

---

## 1. The Scale Root: Vinny Moretti (NPC Wise Guy)

All Mob Sim scale derives from one reference: the player character model, **"Vinny Moretti."**

The production model is `animationtest1.stasset` (identical to `character_hoodlum_0.stasset`),
loaded at runtime by `CharacterRig` with `voxelSize = 0.02f`.

| Property | Value |
|---|---|
| Character voxel grid (container) | **48 × 48 × 48** (W×H×D) |
| Tight bounds (rest pose) | **34 × 31 × 6** (W×H×D) |
| Tight bounds offset | (7, 0, 21) — character sits lower-left of grid |
| Character voxel size | 0.02m/voxel |
| **Rest-pose height** | 31 × 0.02 = **0.62m** |
| Rest-pose width | 34 × 0.02 = 0.68m |
| Rest-pose depth | 6 × 0.02 = 0.12m |
| Non-air voxels | 1,544 (1.4% of grid) |
| Materials | 1 (mat 125 = Flesh) — prototype, will be expanded |
| Skeleton | None yet (anim params in `animationtest1.anim.json`) |
| Animation pivots | 10 (root, torso, L/R arm, L/R forearm, L/R leg, L/R shoulder) |

### Why the grid is larger than the tight bounds

The 48³ grid is **intentional**, not waste. The extra space provides **animation headroom** —
when a character ragdolls, flinches, raises arms in T-pose, or splays limbs, the voxels extend
beyond the rest-pose tight bounds. If the grid were cropped to 34×31×6, any animated limb
extending outside that box would be clipped or culled during rendering.

The 48³ grid provides:
- **17 voxels above** the head (0.34m) — room for raised arms, jumping, ragdoll sprawl
- **7 voxels padding** on each side (0.14m) — room for arms swinging outward
- **21 voxels** front and back (0.42m each) — room for forward falls, backward ragdoll

Additionally, all 10 animation pivots in `animationtest1.anim.json` are **normalized to the 48³
grid** (e.g., root pivot = `(0.5, 0.365, 0.5)` = voxel `(24, 17.5, 24)`). Cropping the grid would
require recalculating every pivot and joint offset.

**Rule**: the grid dimensions are part of the model spec. Do not crop or resize the grid without
recalculating all animation parameters.

Everything else — building voxel size, door heights, vehicle size — must be sized so it reads
correctly **next to Vinny at 0.62m tall** (rest pose). This is why the barber shop and the apartment
block (both built with an 8-voxel door at 0.1m/voxel = 0.8m) are the two currently-correct buildings:
0.8m is 1.29× Vinny's height, which is a comfortable, believable doorway for a 0.62m-tall NPC to
walk through without looking like a mouse-hole or a garage door.

**Golden ratio check**: door height ÷ NPC height should land close to **1.2×–1.3×** for a
normal pedestrian door. This is the test any new/reworked building door must pass.

```
0.8m door ÷ 0.62m NPC = 1.29×   ✅ (barber, apartment_block)
0.4m door ÷ 0.62m NPC = 0.65×   ❌ (apartments, speakeasy, hq side, diner — door is SHORTER than the NPC)
0.5m door ÷ 0.62m NPC = 0.81×   ❌ (casino, police_station — still shorter than NPC)
```

This is the concrete, numeric version of "I think other buildings need rework" — the 4v and 5v
doors are literally shorter than the NPC that has to walk through them.

---

## 2. Core Voxel Size Constants

| Constant | Value | Applies To |
|---|---|---|
| `BUILDING_VOXEL_SIZE` | 0.1m | All building generators (`procedural_mob_buildings.py`) |
| `CHAR_VOXEL_SIZE` | 0.02m | Character generators (`procedural_mob_characters.py`) — production scale |
| `VEHICLE_VOXEL_SIZE` | 0.05m | Vehicle generators (`procedural_mob_vehicles.py`) |
| `NPC_HEIGHT` | 0.62m | 31 character voxels (rest-pose tight bounds), or 6.2 building voxels |
| `NPC_GRID` | 48×48×48 | Character voxel grid container (provides animation headroom) |

Three independent voxel grids exist (building/character/vehicle) at three different voxel sizes.
This is intentional — it gives buildings coarse-but-large detail, characters fine detail at small
size, and vehicles a middle ground. **Never assume 1 building-voxel = 1 character-voxel.** Always
convert through real-world meters.

```
Mob Sim meters = building_voxels × 0.1  = character_voxels × 0.02  = vehicle_voxels × 0.05
```

---

## 3. Door Standard (CORRECTED — replaces the table in MOB_SIM_SCALE_STANDARD.md)

Doors must be sized relative to `NPC_HEIGHT` (0.62m), not picked arbitrarily per building.

| Door Class | Height (voxels @ 0.1m) | Height (m) | Ratio to NPC | Width (voxels) | Use |
|---|---|---|---|---|---|
| **Pedestrian Standard** | **8v** | 0.8m | **1.29×** | 6v (0.6m) | Storefronts, apartments, HQ, speakeasy, diner — any door a walking NPC uses |
| **Civic / Grand** | **10-12v** | 1.0-1.2m | 1.61-1.94× | 8-12v | Police station, casino, apartment_block grand entrance |
| **Vehicle Bay** | 6v (unchanged, not a pedestrian door) | 0.6m | n/a | 16v (1.6m) | Garage — sized for the vehicle, not Vinny |

**Action required**: `apartments` (4v), `speakeasy` (4v), `diner` (4v), `hq` side door (4v),
`casino` (5v), and `police_station` (5v) all need their door height corrected to 8v (or 10-12v for
civic buildings) to pass the 1.29×+ ratio test. `butcher`, `bakery`, and `hq` storefront already
use 8v via the shared `_add_storefront()` default and pass. See Section 6 for the full audit.

---

## 4. Orientation Convention

All three model types use **numpy arrays shaped `(width, height, depth)`** with:

- **X** = width (left-right)
- **Y** = height (vertical, 0 = ground/lowest point)
- **Z** = depth (front-back)

But **"front" means different things per model type** — this was previously undocumented and is
a common source of confusion:

| Model Type | Front Direction | Reasoning |
|---|---|---|
| **Buildings** | **Z = 0** (low Z) | Storefront/door/awning are built on the `z < WALL_T` face. This is the face that must face the street. |
| **Vehicles** | **+Z (high Z)** | Per `procedural_mob_vehicles.py` header comment: "Front of vehicle faces +Z (high Z values) to match Unity's LookRotation forward." Headlights, grille, hood are at high Z. |
| **Characters** | **Z = low** (front of body) | Sunglasses/face are placed at low Z (e.g., `grid[..., 2]` in `generate_hoodlum`); hair/back of head at high Z (`grid[..., 6:8]`). |

**Buildings and vehicles use OPPOSITE Z conventions.** This is not a bug — it's because buildings
are placed by `BuildingOrientation.Analyze()` which checks the Z=0 face against street material,
while vehicles are rotated at runtime via `Quaternion.LookRotation` which expects +Z forward
(Unity's convention). **Do not "fix" this mismatch by flipping one of them** — both are correct
for their own placement systems. Just be aware of it when authoring new models.

**When authoring a new model of an existing type, match that type's existing convention.**

---

## 5. Proportion Reference (Width × Height × Depth ranges)

| Model Type | Typical W×H×D (voxels) | Typical Real Size | Notes |
|---|---|---|---|
| Character (NPC) | 48³ grid (34×31×6 tight) | 0.68m × 0.62m × 0.12m (rest pose) | Vinny Moretti — grid oversized for animation headroom |
| Small business (barber, bakery, diner) | 32×16-20×32-34 | 3.2m × 1.6-2.0m × 3.2-3.4m | Single story + small upper floor |
| Apartments (small) | 32×36×32 | 3.2m × 3.6m × 3.2m | 4-story walk-up |
| Apartment block (large tenement) | 96×44×96 | 9.6m × 4.4m × 9.6m | Full-block, 5-story, occupies 3×3 block footprint |
| Civic (police, casino, HQ) | 32×24-28×32 | 3.2m × 2.4-2.8m × 3.2m | Taller ground floor for grand entrance |
| Vehicle (touring car) | 20×16×30 | 1.0m × 0.8m × 1.5m | ~1.25× NPC height at roofline |

---

## 6. Model Audit (Aug 8, 2026)

Certified correct (door ratio ≥ 1.29× NPC height, proportions checked):

- ✅ **`barber`** (`generate_barbershop`) — 8v door, 1.29× ratio
- ✅ **`apartment_block`** (`generate_apartment_block`) — 8v door, 1.29× ratio, full-block scale intentional
- ✅ **`vehicle_civilian_car_0`** (touring car) — proportioned correctly per user review; roofline ≈ 0.8m aligns with NPC seated/standing scale in the cabin

Needs rework (door height fails the 1.29× ratio test, pending user/tool confirmation):

- ⚠️ `butcher`, `bakery` — use 8v via storefront default, likely OK but unverified against overall proportions (wall thickness, window scale)
- ❌ `apartments` (small, 4v door)
- ❌ `diner` (4v door)
- ❌ `speakeasy` (4v door)
- ❌ `hq` (4v side door — storefront door is 8v and fine)
- ❌ `casino` (5v door)
- ❌ `police_station` (5v door)
- ⚪ `garage` — vehicle bay door, not a pedestrian door, exempt from this test

This table should be updated as each building is reworked and re-verified with the inspector
tooling (see Section 7).

---

## 7. Verification Process (going forward)

Before merging any new or reworked model:

1. Generate the `.stasset` file.
2. Run it through the (to-be-updated) inspector tooling in `VoxelAssetStudio/toolbox/stasset_inspector.py`
   or the diagnostic scripts in `VoxelAssetStudio/` — check dimensions, door height ratio, and
   symmetry.
3. Cross-check dimensions against Section 5's proportion table.
4. Cross-check door height against Section 3's door standard.
5. If it's a new building/vehicle/character *type* (not just a variant), add a row to Section 5
   and update the audit table in Section 6.

---

## File References

- **Voxel size constants**: `VoxelAssetStudio/mob_materials.py`, `procedural_mob_buildings.py`, `procedural_mob_characters.py`, `procedural_mob_vehicles.py`
- **Building generators**: `VoxelAssetStudio/procedural_mob_buildings.py`
- **Character generators**: `VoxelAssetStudio/procedural_mob_characters.py`
- **Vehicle generators**: `VoxelAssetStudio/procedural_mob_vehicles.py`
- **Unity voxel sizes**: `CityMap3D.cs` → `voxelSize = 0.1f`, `characterVoxelSize = 0.02f`; `VehicleTestSpawner.cs` → `vehicleVoxelSize = 0.05f`
- **Vinny model**: `Assets/StreamingAssets/voxel_characters/Vinny.stasset` (renamed from `animationtest1.stasset`, identical to `character_hoodlum_0.stasset`)
- **Vinny anim params**: `Assets/StreamingAssets/voxel_characters/Vinny.anim.json` (10 pivots, normalized to 48³ grid)
- **Vinny group IDs**: `Assets/StreamingAssets/voxel_characters/Vinny.groups` (required for GPU compute pose — must match `.stasset` filename)
- **Runtime spawner**: `CharacterRig.cs` → `assetBaseName = "Vinny"`, `voxelSize = 0.02f`
- **Orientation detection**: `BuildingOrientation.cs` (`Analyze()`), `VehicleAgent` rotation via `Quaternion.LookRotation`
- **Superseded doc**: `MOB_SIM_SCALE_STANDARD.md` (door table in that doc is now out of date — this document's Section 3 is authoritative)
- **Related**: `VOXEL_BUILDING_METHODOLOGY.md` (construction pipeline), `COORDINATE_SYSTEM.md` (world-space placement), `docs/systems/DYNAMIC_OBJECT_RENDERING_TIERS.md` (future door animation, Tier 2)
