# gen_debris_lot.py — "Debris Test Lot" prototype block generator.
# Dedicated demolition/rubble testbed for block_48, next to the glass monolith
# (block_47) and shooting range (block_46).
#
# Container matches the tenement/glass testbeds: 192 x 120 x 192 @ 0.05 m/voxel
# (9.6 m x 6 m x 9.6 m).
#
# Purpose-built so a detonation's rubble has a KNOWN landing surface and the
# pile's provenance is readable by color:
#   y 0-3        full-footprint asphalt pad — the guaranteed "floor" every pile
#                lands on; blasts above it cannot fall through the world
#   big brick    72^3 solid red-brick cube centered on the pad — the main mass
#   stone wall   thin stone slab wall — rim/edge debris + wall-through tests
#   wood block   30^3 light-wood cube — small-mass / quick-reset test
#
# Different material per structure => rubble pile color tells you exactly which
# mass flew where. Attachment points mark deterministic blast targets.
#
# Usage:
#   python Tools/gen_debris_lot.py            # writes consolidated JSON
#   python Tools/json_to_stasset.py "VoxelAssetStudio/JSON Models In Progress/debris_test_lot.json" \
#       Assets/StreamingAssets/voxel_buildings/debris_test_lot.stasset

import json

W, H, D = 192, 120, 192
RED_BRICK, STONE, CONCRETE, ASPHALT = 100, 101, 102, 104
LIGHT_WOOD = 107

PAD_Y1 = 4            # pad occupies y0..3

vox = {}
def put(x0, x1, y0, y1, z0, z1, m):
    for x in range(x0, x1):
        for y in range(y0, y1):
            for z in range(z0, z1):
                vox[(x, y, z)] = m

# --- pad: the guaranteed rubble landing surface ---
put(0, W, 0, PAD_Y1, 0, D, ASPHALT)
# concrete curb so the lot reads as a testbed enclosure (2 cells tall)
put(0, W, PAD_Y1, PAD_Y1 + 2, 0, 2, CONCRETE)
put(0, W, PAD_Y1, PAD_Y1 + 2, D - 2, D, CONCRETE)
put(0, 2, PAD_Y1, PAD_Y1 + 2, 0, D, CONCRETE)
put(W - 2, W, PAD_Y1, PAD_Y1 + 2, 0, D, CONCRETE)

# --- big red-brick cube: the main debris mass (72^3 = 373k cells) ---
BRICK_X = (60, 132)
BRICK_Y = (PAD_Y1, PAD_Y1 + 72)
BRICK_Z = (60, 132)
put(*BRICK_X, *BRICK_Y, *BRICK_Z, RED_BRICK)

# --- thin stone wall: rim/partial-mass tests (70w x 40h x 4d) ---
WALL_X = (20, 90)
WALL_Y = (PAD_Y1, PAD_Y1 + 40)
WALL_Z = (160, 164)
put(*WALL_X, *WALL_Y, *WALL_Z, STONE)

# --- small light-wood cube: quick low-mass test (30^3 = 27k cells) ---
WOOD_X = (150, 180)
WOOD_Y = (PAD_Y1, PAD_Y1 + 30)
WOOD_Z = (150, 180)
put(*WOOD_X, *WOOD_Y, *WOOD_Z, LIGHT_WOOD)

# --- deterministic blast targets (attachment-point contract) ---
PART_DEFS = [
    {"id": 0, "name": "Body",             "key": "body",              "color": "#888888", "desc": "Unassigned"},
    {"id": 1, "name": "Blast Target",     "key": "blast_target",      "color": "#ff2222", "desc": "Primary aim point — center of the brick mass"},
    {"id": 2, "name": "Blast Target Wall","key": "blast_target_wall", "color": "#ffaa00", "desc": "Aim point — center of the stone wall"},
    {"id": 3, "name": "Blast Target Wood","key": "blast_target_wood", "color": "#ffee66", "desc": "Aim point — center of the wood cube"},
]

part_ids = {d["key"]: d["id"] for d in PART_DEFS}
painted = {
    "blast_target":      [(96, 40, 96)],          # brick cube centroid
    "blast_target_wall": [(55, 24, 162)],         # stone wall centroid
    "blast_target_wood": [(165, 19, 165)],        # wood cube centroid
}
# centroids sit inside their structures already — no repaint needed

def centroid(pts):
    n = float(len(pts))
    return {k: round(sum(p[i] for p in pts) / n, 3) for i, k in enumerate("xyz")}

attachment_points = {k: centroid(v) for k, v in painted.items()}
item_parts = {f"{x},{y},{z}": part_ids[k] for k, pts in painted.items() for x, y, z in pts}

def mat(i, name, r, g, b):
    h = '#%02x%02x%02x' % (r, g, b)
    return {"id": i, "name": name, "r": r, "g": g, "b": b, "hex": h, "category": "building"}

materials = [
    mat(100, "Red Brick", 148, 66, 51), mat(101, "Stone", 122, 107, 87),
    mat(102, "Concrete", 148, 148, 138), mat(104, "Asphalt", 46, 46, 51),
    mat(107, "Light Wood", 153, 107, 64),
]

data = {
    "name": "debris_test_lot",
    "dims": [W, H, D],
    "assetType": "building",
    "voxelSize": 0.05,
    "materials": materials,
    "voxels": [[x, y, z, m] for (x, y, z), m in sorted(vox.items())],
    "groups": {}, "regions": {},
    "attachmentPoints": attachment_points,
    "itemParts": item_parts, "itemPartDefs": PART_DEFS,
    "format": "steelcity_stasset", "version": 1,
}

out = "VoxelAssetStudio/JSON Models In Progress/debris_test_lot.json"
with open(out, "w", encoding="utf-8") as f:
    json.dump(data, f)
print(f"Wrote {out}: {len(data['voxels'])} voxels, {W}x{H}x{D} @ 0.05m")
