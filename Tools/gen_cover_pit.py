# gen_cover_pit.py — "Cover Pit" double-sided testbed block generator.
# Emits the consolidated stasset JSON the HTML editor / json_to_stasset import.
#
# Container matches shooting_range: 192 x 48 x 192 @ 0.05m/voxel
# (full city-block footprint, 9.6m x 2.4m x 9.6m — low profile, no walls).
# CityMap3D places blocks at voxelSize=0.05 (2x-upscaled convention) — every
# authored voxel is 5cm, so metric heights need 2x the voxel count.
#
# Purpose: gradient-wall cover testbed + phase-0 two-entity combat.
#   Vinny starts at cover_stand_a (south plate), Civilian_02 "Tan Suit" at
#   cover_stand_b (north plate). ONE continuous wall runs east-west mid-lot
#   (the old ladder lane) — its top sweeps Vinny's measured silhouette band
#   (stand 0.62 / shallow 0.50 / deep 0.45): the crouch band is ~1 voxel wide
#   at 0.05m/vox, which is exactly what this wall makes legible.
#     [FULL h16 0.80m] → taper h15..h8 in 1-vox steps → [NULL h6 0.30m] → ends
#   x=94 (the duel axis) lands mid-taper on h12 — the live crouch step
#   (d≈0.4 at pad support 0.10m). Possess-walking the face folds Vinny
#   standing → crouch → standing as the wall top sweeps the band.
#
# Layout (front = low Z, same convention as the range):
#   z 0..191  asphalt pad y 0-1
#   perimeter stone curb y 2
#   z 24-26   cover_stand_a painted plate (south — Vinny), centered on x=96
#   z 166-168 cover_stand_b painted plate (north — Tan Suit), straight across
#   z 92-99   the gradient wall, x 20..184 — full→taper→null, east-descending
#   z 88-90   dark-wood marker pads at wall landmarks = Vinny's pick anchors
#             (cover_wall=full, cover_crouch=h12 crouch step, cover_mid=h11
#             Low edge, cover_deep=null tail — last two are expected-fail
#             picks that exercise the classifier's reject path; *_n twins
#             on the north face for Tan-side picks)

import json

W, H, D = 192, 48, 192
RED_BRICK, STONE, SIDEWALK, ASPHALT = 100, 101, 102, 104
DARK_WOOD, LIGHT_WOOD, DARK_TRIM, PAINTED_METAL, WHITE_FABRIC = 106, 107, 108, 111, 127

STAND_A_X, STAND_A_Z = 94, 24    # Vinny — south plate, centered on x=96
STAND_B_X, STAND_B_Z = 94, 166   # Tan — north plate, straight across

# The gradient wall — one continuous wall in the ladder lane, descending
# west→east. h = column top in voxels (top = h*0.05m; pad top = y2 = 0.10m).
# With measured heights 0.62/0.50/0.45 + margin, the crouch band is tops
# ~0.57-0.62m → h12 is THE crouch step (d≈0.4); h13+ is Full, h11- is Low.
# (x0, run_vox, top_h)
GRADIENT = [
    ( 20, 28, 16),              # FULL — 0.80m top covers him standing
    ( 48, 14, 15), ( 62, 14, 14), ( 76, 14, 13), ( 90, 14, 12),
    (104, 14, 11), (118, 14, 10), (132, 14,  9), (146, 14,  8),
    (160, 24,  6),              # NULL tail — 0.30m, below h1 → release
]
GRAD_Z0, GRAD_D = 92, 8         # same lane as the retired ladder
PAD_Z0, PAD_D = 88, 3           # marker pads just south of each face

# Pick anchors → wall landmarks. cover_crouch sits at the h12 step's center;
# mid/deep deliberately hit Low territory so the buttons also exercise the
# classifier's reject path. (anchor → pad center x)
PAD_AT = {
    "cover_wall":   34,         # full segment — Full-class pick
    "cover_crouch": 97,         # h12 — the live crouch step (d≈0.4)
    "cover_mid":   111,         # h11 — Low edge, expect a reject
    "cover_deep":  172,         # null tail — Low, expect a reject
}

vox = {}
def put(x0, x1, y0, y1, z0, z1, m):
    for x in range(x0, x1):
        for y in range(y0, y1):
            for z in range(z0, z1):
                vox[(x, y, z)] = m

# --- ground pad — SIDEWALK (102): the designated walkable surface (same
#     MAT id as city sidewalks — walkableMaterials masks read this stamp).
put(0, W, 0, 2, 0, D, SIDEWALK)
# perimeter curb (1 voxel high over pad) — lot reads bounded, no wall class
put(0, W, 2, 3, 0, 2, STONE); put(0, W, 2, 3, D - 2, D, STONE)
put(0, 2, 2, 3, 0, D, STONE); put(W - 2, W, 2, 3, 0, D, STONE)

# --- opposing stand plates (painted-metal 4x2 pads, surface layer y2) ---
put(STAND_A_X, STAND_A_X + 4, 2, 3, STAND_A_Z, STAND_A_Z + 2, PAINTED_METAL)
put(STAND_B_X, STAND_B_X + 4, 2, 3, STAND_B_Z, STAND_B_Z + 2, PAINTED_METAL)

# --- gradient wall (light wood; dark-wood top course on BOTH long faces —
#     either fighter can hide on either side) + marker pads that carry the
#     pick anchors ---
marker_pads = {}
for x0, run, h in GRADIENT:
    x1, z0, z1 = x0 + run, GRAD_Z0, GRAD_Z0 + GRAD_D
    put(x0, x1, 2, h, z0, z1, LIGHT_WOOD)
    # Lip = 108 Dark Trim, NOT 106 — a dark-wood lip on the null tail would
    # sit inside the walkable family and terrace a legal staircase over
    # the wall's low end.
    put(x0, x1, h - 1, h, z0, z0 + 1, DARK_TRIM)   # south lip = Vinny's side
    put(x0, x1, h - 1, h, z1 - 1, z1, DARK_TRIM)   # north lip = Tan's side
for key, cx in PAD_AT.items():
    # marker pads (dark wood, surface layer): south = Vinny's pick anchor,
    # north = Tan's. Centroids feed attachmentPoints directly.
    for side, pz in ((key, PAD_Z0), (key + "_n", GRAD_Z0 + GRAD_D + 2)):
        put(cx - 2, cx + 2, 2, 3, pz, pz + PAD_D, DARK_WOOD)
        marker_pads[side] = [(x, 2, z) for x in range(cx - 2, cx + 2)
                                       for z in range(pz, pz + PAD_D)]

# --- building event points (painted regions → centroids; same contract as
#     the range's firing_position/firing_target groups) ---
PART_DEFS = [
    {"id": 0, "name": "Body",          "key": "body",          "color": "#888888", "desc": "Unassigned"},
    {"id": 1, "name": "Cover Stand A", "key": "cover_stand_a", "color": "#ff6b6b", "desc": "South stand spot — primary combatant (Vinny)"},
    {"id": 2, "name": "Cover Stand B", "key": "cover_stand_b", "color": "#00ddff", "desc": "North stand spot — opposing combatant (Tan Suit)"},
    {"id": 3, "name": "Cover Deep",    "key": "cover_deep",    "color": "#cc3333", "desc": "Pick anchor — gradient null tail (h6, Low — expected reject)"},
    {"id": 4, "name": "Cover Mid",     "key": "cover_mid",     "color": "#dd8833", "desc": "Pick anchor — gradient h11 Low edge (expected reject)"},
    {"id": 5, "name": "Cover Crouch",  "key": "cover_crouch",  "color": "#ddcc33", "desc": "Pick anchor — gradient h12 crouch step (d≈0.4)"},
    {"id": 6, "name": "Cover Wall",    "key": "cover_wall",    "color": "#33cc55", "desc": "Pick anchor — gradient full segment (Full class)"},
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
    mat(101, "Stone", 122, 107, 87), mat(102, "Sidewalk", 150, 145, 138),
    mat(106, "Dark Wood", 76, 45, 25), mat(107, "Light Wood", 153, 107, 64),
    mat(108, "Dark Trim", 60, 35, 18),
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
