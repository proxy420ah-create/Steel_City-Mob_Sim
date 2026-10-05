# gen_shooting_range.py — M2 "Shooting Range" city block generator.
# Emits the consolidated stasset JSON the HTML editor imports
# ({dims, voxels, materials, assetType:'building'}).
#
# Container matches tenement_block_0.stasset: 192 x 120 x 192 @ 0.1m/voxel
# (full city-block footprint, 19.2m x 12m x 19.2m).
#
# Layout (front = low Z per building convention — firing line near street):
#   z 0-1     stone curb border (whole perimeter)
#   z 0..191  asphalt pad y 0-1
#   z 12-13   painted-metal firing line stripe (full width)
#   z 10-11   painted-metal firing-spot square (where the shooter stands)
#   z 14-15   light-wood bench in front of line
#   z 14..180 concrete center lane (4m wide, x 76-115)
#   z 14..180 low concrete rails along lane edges (x 74-75, 116-117)
#   z 62/112/162 painted-metal distance marks: 5m / 10m / 15m from line
#   z 168-170 target: wood posts + white board + red bullseye (NPC chest h.)
#   z 184-187 brick backstop wall (full width, 3.6m tall, concrete cap)

import json

W, H, D = 192, 120, 192
RED_BRICK, STONE, CONCRETE, ASPHALT = 100, 101, 102, 104
DARK_WOOD, LIGHT_WOOD, PAINTED_METAL, WHITE_FABRIC = 106, 107, 111, 127

FIRE_Z = 12          # firing line
LANE_X = (76, 116)   # lane edges (exclusive)
TGT_Z = 169          # target board plane
BACK_Z0, BACK_Z1 = 184, 188  # backstop slab

vox = {}
def put(x0, x1, y0, y1, z0, z1, m):
    for x in range(x0, x1):
        for y in range(y0, y1):
            for z in range(z0, z1):
                vox[(x, y, z)] = m

# --- ground pad ---
put(0, W, 0, 2, 0, D, ASPHALT)
# perimeter curb (2 voxels high over pad)
put(0, W, 2, 3, 0, 2, STONE); put(0, W, 2, 3, D - 2, D, STONE)
put(0, 2, 2, 3, 0, D, STONE); put(W - 2, W, 2, 3, 0, D, STONE)

# --- firing line + shooter spot ---
put(0, W, 2, 3, FIRE_Z, FIRE_Z + 2, PAINTED_METAL)
put(94, 98, 2, 3, FIRE_Z - 2, FIRE_Z, PAINTED_METAL)          # stand spot
# bench just downrange of the line
put(90, 102, 2, 5, FIRE_Z + 2, FIRE_Z + 4, LIGHT_WOOD)

# --- center lane + rails ---
put(LANE_X[0], LANE_X[1], 2, 3, FIRE_Z + 4, 182, CONCRETE)
put(LANE_X[0] - 2, LANE_X[0], 2, 4, FIRE_Z + 4, 182, STONE)   # left rail
put(LANE_X[1], LANE_X[1] + 2, 2, 4, FIRE_Z + 4, 182, STONE)   # right rail

# --- distance marks across the lane (5m / 10m / 15m from firing line) ---
for dist in (50, 100, 150):
    z = FIRE_Z + dist
    put(LANE_X[0], LANE_X[1], 3, 4, z, z + 2, PAINTED_METAL)

# --- target (posts, white board, red bullseye at NPC chest height) ---
put(88, 91, 2, 15, TGT_Z, TGT_Z + 2, DARK_WOOD)               # left post
put(101, 104, 2, 15, TGT_Z, TGT_Z + 2, DARK_WOOD)             # right post
put(84, 108, 4, 13, TGT_Z, TGT_Z + 1, WHITE_FABRIC)           # board
put(94, 98, 6, 10, TGT_Z, TGT_Z + 1, RED_BRICK)               # bullseye

# --- backstop ---
put(0, W, 0, 36, BACK_Z0, BACK_Z1, RED_BRICK)
put(0, W, 36, 37, BACK_Z0, BACK_Z1, CONCRETE)                 # cap

# --- building event points (painted regions → centroids, same contract as
#     character/item attachmentPoints; ids match BUILDING_POINT_GROUPS in
#     voxel_editor.html) ---
PART_DEFS = [
    {"id": 0, "name": "Body",            "key": "body",            "color": "#888888", "desc": "Unassigned"},
    {"id": 1, "name": "Firing Position", "key": "firing_position", "color": "#ff6b6b", "desc": "Character stand spot - pathing destination"},
    {"id": 2, "name": "Firing Target",   "key": "firing_target",   "color": "#00ddff", "desc": "Aim point - muzzle axis passes through centroid"},
    {"id": 3, "name": "Backstop",        "key": "backstop",        "color": "#ffaa00", "desc": "Facing reference - orient toward centroid on arrival"},
    {"id": 4, "name": "Door",            "key": "door",            "color": "#00ff88", "desc": "Entry/exit point"},
    {"id": 5, "name": "Spawn",           "key": "spawn",           "color": "#ff00ff", "desc": "Character/prop spawn point"},
    {"id": 6, "name": "Prop Slot",       "key": "prop_slot",       "color": "#ff8800", "desc": "Auto-place prop here (bench, crate, barrel)"},
    {"id": 7, "name": "Cover",           "key": "cover",           "color": "#8844ff", "desc": "Cover position for combat AI"},
    {"id": 8, "name": "Decor",           "key": "decor",           "color": "#44ffcc", "desc": "Decorative prop anchor (lamp, sign, plant)"},
]

painted = {
    # stand spot = the painted square just behind the firing line (top layer)
    "firing_position": [(x, 2, z) for x in range(94, 98) for z in range(FIRE_Z - 2, FIRE_Z)],
    # bullseye = the red center of the target board
    "firing_target":   [(x, y, TGT_Z) for x in range(94, 98) for y in range(6, 10)],
    # backstop = a patch on the inner face of the brick wall behind the target
    "backstop":        [(x, y, BACK_Z0) for x in range(88, 104) for y in range(8, 16)],
}
part_ids = {d["key"]: d["id"] for d in PART_DEFS}

def centroid(pts):
    n = float(len(pts))
    return {k: round(sum(p[i] for p in pts) / n, 3) for i, k in enumerate("xyz")}

attachment_points = {k: centroid(v) for k, v in painted.items()}
item_parts = {f"{x},{y},{z}": part_ids[k] for k, pts in painted.items() for x, y, z in pts}

# --- materials used (colors from mob_materials.py) ---
def mat(i, name, r, g, b):
    h = '#%02x%02x%02x' % (r, g, b)
    return {"id": i, "name": name, "r": r, "g": g, "b": b, "hex": h, "category": "building"}

materials = [
    mat(100, "Red Brick", 148, 66, 51), mat(101, "Stone", 122, 107, 87),
    mat(102, "Concrete", 148, 148, 138), mat(104, "Asphalt", 46, 46, 51),
    mat(106, "Dark Wood", 76, 45, 25), mat(107, "Light Wood", 153, 107, 64),
    mat(111, "Painted Metal", 230, 224, 209), mat(127, "White Fabric", 224, 219, 209),
]

data = {
    "name": "shooting_range",
    "dims": [W, H, D],
    "assetType": "building",
    "voxelSize": 0.1,
    "materials": materials,
    "voxels": [[x, y, z, m] for (x, y, z), m in sorted(vox.items())],
    "groups": {}, "regions": {},
    "attachmentPoints": attachment_points,
    "itemParts": item_parts, "itemPartDefs": PART_DEFS,
    "format": "steelcity_stasset", "version": 1,
}

out = "VoxelAssetStudio/JSON Models In Progress/shooting_range.json"
with open(out, "w", encoding="utf-8") as f:
    json.dump(data, f)
print(f"Wrote {out}: {len(data['voxels'])} voxels, {W}x{H}x{D} @ 0.1m")
