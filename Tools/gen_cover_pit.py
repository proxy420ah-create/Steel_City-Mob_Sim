# gen_cover_pit.py — "Cover Pit" double-sided testbed block generator.
# Emits the consolidated stasset JSON the HTML editor / json_to_stasset import.
#
# Container matches shooting_range: 192 x 48 x 192 @ 0.05m/voxel
# (full city-block footprint, 9.6m x 2.4m x 9.6m — low profile, no walls).
# CityMap3D places blocks at voxelSize=0.05 (2x-upscaled convention) — every
# authored voxel is 5cm, so metric heights need 2x the voxel count.
#
# Purpose: adaptive-crouch depth-ladder + phase-0 two-entity combat.
#   Vinny starts at cover_stand_a (south plate), Civilian_02 "Tan Suit" at
#   cover_stand_b (north plate). ONE shared row of 4 boxes sits mid-lot —
#   an ascending duck-depth staircase, usable from either face:
#     [DEEP 0.8m] [MID 1.0m] [CROUCH 1.3m] [WALL 2.0m]   (top heights)
#   Expected reqDepth (eyeShallow~1.0m, eyeDeep~0.8m, margin 0.1m):
#     deep  → ~1.0 (deepest usable duck — above this the eye can't hide)
#     mid   → ~0.5 (between the authored endpoints)
#     crouch→  0   (shallow authored crouch clears it)
#     wall  → Full class, no crouch at all
#
# Layout (front = low Z, same convention as the range):
#   z 0..191  asphalt pad y 0-1
#   perimeter stone curb y 2
#   z 24-26   cover_stand_a painted plate (south — Vinny), centered on x=96
#   z 166-168 cover_stand_b painted plate (north — Tan Suit), straight across
#   z 92-99   the 4-box ladder row, ~7m in front of Vinny's plate
#   z 88-91   dark-wood marker pads south of each box = Vinny's pick anchors
#             (cover_deep/_mid/_crouch/_wall; *_n twins on the north face
#             for Tan-side picks)
#   ~1.0m gaps between boxes (20v @ 0.05m) leave navigation lanes.

import json

W, H, D = 192, 48, 192
RED_BRICK, STONE, CONCRETE, ASPHALT = 100, 101, 102, 104
DARK_WOOD, LIGHT_WOOD, PAINTED_METAL, WHITE_FABRIC = 106, 107, 111, 127

STAND_A_X, STAND_A_Z = 94, 24    # Vinny — south plate, centered on x=96
STAND_B_X, STAND_B_Z = 94, 166   # Tan — north plate, straight across

# The depth ladder — one shared row mid-lot, ascending left→right.
# h = column top in voxels (top = h*0.05m above corner; bodies start at pad
# top y2 = 0.1m). Doubled from the original 0.1m assumption — runtime vs=0.05.
#   deep   16 → 0.8m top — only the deepest authored duck hides the eye
#   mid    20 → 1.0m top — mid-range, needs a partial duck
#   crouch 26 → 1.3m top — the shallow authored crouch clears it
#   wall   40 → 2.0m top — above standing eye, Full-class, no duck
LADDER = [
    ("cover_deep",   18, 16),
    ("cover_mid",    62, 20),
    ("cover_crouch",106, 26),
    ("cover_wall",  150, 40),
]
BOX_W, BOX_D = 24, 8          # 1.2m wide × 0.4m deep
ROW_Z0 = 92                   # row z 92..99 — ~3.4m ahead of Vinny's plate
PAD_Z0, PAD_D = 88, 3         # marker pads just south of each face

vox = {}
def put(x0, x1, y0, y1, z0, z1, m):
    for x in range(x0, x1):
        for y in range(y0, y1):
            for z in range(z0, z1):
                vox[(x, y, z)] = m

# --- ground pad ---
put(0, W, 0, 2, 0, D, ASPHALT)
# perimeter curb (1 voxel high over pad) — lot reads bounded, no wall class
put(0, W, 2, 3, 0, 2, STONE); put(0, W, 2, 3, D - 2, D, STONE)
put(0, 2, 2, 3, 0, D, STONE); put(W - 2, W, 2, 3, 0, D, STONE)

# --- opposing stand plates (painted-metal 4x2 pads, surface layer y2) ---
put(STAND_A_X, STAND_A_X + 4, 2, 3, STAND_A_Z, STAND_A_Z + 2, PAINTED_METAL)
put(STAND_B_X, STAND_B_X + 4, 2, 3, STAND_B_Z, STAND_B_Z + 2, PAINTED_METAL)

# --- cover ladder (light wood; dark-wood lip on BOTH long faces — either
#     fighter can hide on either side) + south/north marker pads that carry
#     the pick anchors ---
marker_pads = {}
for key, x0, h in LADDER:
    x1, z0, z1 = x0 + BOX_W, ROW_Z0, ROW_Z0 + BOX_D
    put(x0, x1, 2, h, z0, z1, LIGHT_WOOD)
    put(x0, x1, h - 1, h, z0, z0 + 1, DARK_WOOD)   # south lip = Vinny's side
    put(x0, x1, h - 1, h, z1 - 1, z1, DARK_WOOD)   # north lip = Tan's side
    cx = x0 + BOX_W // 2
    # marker pads (dark wood, surface layer): south = Vinny's pick anchor,
    # north = Tan's. Centroids feed attachmentPoints directly.
    for side, pz in ((key, PAD_Z0), (key + "_n", z1 + 2)):
        put(cx - 2, cx + 2, 2, 3, pz, pz + PAD_D, DARK_WOOD)
        marker_pads[side] = [(x, 2, z) for x in range(cx - 2, cx + 2)
                                       for z in range(pz, pz + PAD_D)]

# --- building event points (painted regions → centroids; same contract as
#     the range's firing_position/firing_target groups) ---
PART_DEFS = [
    {"id": 0, "name": "Body",          "key": "body",          "color": "#888888", "desc": "Unassigned"},
    {"id": 1, "name": "Cover Stand A", "key": "cover_stand_a", "color": "#ff6b6b", "desc": "South stand spot — primary combatant (Vinny)"},
    {"id": 2, "name": "Cover Stand B", "key": "cover_stand_b", "color": "#00ddff", "desc": "North stand spot — opposing combatant (Tan Suit)"},
    {"id": 3, "name": "Cover Deep",    "key": "cover_deep",    "color": "#cc3333", "desc": "Pick anchor — deepest-duck box (0.8m)"},
    {"id": 4, "name": "Cover Mid",     "key": "cover_mid",     "color": "#dd8833", "desc": "Pick anchor — mid-duck box (1.0m)"},
    {"id": 5, "name": "Cover Crouch",  "key": "cover_crouch",  "color": "#ddcc33", "desc": "Pick anchor — shallow-crouch box (1.3m)"},
    {"id": 6, "name": "Cover Wall",    "key": "cover_wall",    "color": "#33cc55", "desc": "Pick anchor — full wall, no duck (2.0m)"},
]

painted = {
    "cover_stand_a": [(x, 2, z) for x in range(STAND_A_X, STAND_A_X + 4) for z in range(STAND_A_Z, STAND_A_Z + 2)],
    "cover_stand_b": [(x, 2, z) for x in range(STAND_B_X, STAND_B_X + 4) for z in range(STAND_B_Z, STAND_B_Z + 2)],
}
painted.update(marker_pads)
part_ids = {d["key"]: d["id"] for d in PART_DEFS}

def centroid(pts):
    n = float(len(pts))
    return {k: round(sum(p[i] for p in pts) / n, 3) for i, k in enumerate("xyz")}

attachment_points = {k: centroid(v) for k, v in painted.items()}
# firing_* aliases so the Target Practice order treats the pit like a range:
# skip-walk spawns Vinny on stand_a, facing resolves toward stand_b (the
# order falls back from the _N lane names to these base names, same as the
# range's lane-2 aliases).
attachment_points["firing_position"] = attachment_points["cover_stand_a"]
attachment_points["firing_target"]   = attachment_points["cover_stand_b"]
# Marker pads are also named pick anchors — the HUD's pit buttons target the
# nearest Hidden field cell to each anchor (Vinny's south set).
item_parts = {f"{x},{y},{z}": part_ids[k] for k, pts in painted.items() for x, y, z in pts
              if k in part_ids}   # *_n marker pads carry anchors, no part def

def mat(i, name, r, g, b):
    h = '#%02x%02x%02x' % (r, g, b)
    return {"id": i, "name": name, "r": r, "g": g, "b": b, "hex": h, "category": "building"}

materials = [
    mat(101, "Stone", 122, 107, 87), mat(104, "Asphalt", 46, 46, 51),
    mat(106, "Dark Wood", 76, 45, 25), mat(107, "Light Wood", 153, 107, 64),
    mat(111, "Painted Metal", 230, 224, 209),
]

data = {
    "name": "cover_pit",
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

out = "VoxelAssetStudio/JSON Models In Progress/cover_pit.json"
with open(out, "w", encoding="utf-8") as f:
    json.dump(data, f)
print(f"Wrote {out}: {len(data['voxels'])} voxels, {W}x{H}x{D} @ 0.05m")
print(f"  stands: a={attachment_points['cover_stand_a']} b={attachment_points['cover_stand_b']}")
