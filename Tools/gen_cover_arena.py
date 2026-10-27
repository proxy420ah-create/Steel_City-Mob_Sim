# gen_cover_arena.py — "Cover Arena" symmetric duel lot generator.
# Emits the consolidated stasset JSON the HTML editor / json_to_stasset import.
#
# Container matches cover_pit: 192 x 48 x 192 @ 0.05m/voxel (9.6m square lot,
# low profile). Placed at block_36 (r3 c5) — directly south of block_26's
# tenement_block_0 storefront/door (low-Z face, centered on x96). Tan stands
# north (high-z edge) with his back to the facade across ~10m of street:
# misses north-bound chew the tenement front.
#
# Design: mirror-symmetric about BOTH axes — x=96 (the duel/door axis) and
# z=96 (mid-block). Every quadrant offers identical cover to both fighters:
# a fair duel, no handedness.
#
#   [V-wall]   mid-lane z92-99, stepped SYMMETRIC about x=96: Full ends step
#              down through crouch to a low null dip dead-center on the door
#              axis (open sightline — preserves every cover class the
#              original gradient wall proved, doubled and mirrored).
#   [flanks]   four N-S stone stubs approaching the mid wall per quadrant,
#              stepped along their length (h14 outer → h10 near mid).
#   [pillars]  four red-brick pillars h32 (1.6m) — eye can't clear standing:
#              true HIDE-ONLY cover (exercises refuse→relocate, the "cover
#              for hiding, not fighting" split).
#   [planters] four h12 stone planters at quadrant centers — mid-field
#              crouch cover for relocation variety.
#
# Layout (front = low Z, same convention as the pit):
#   z 0..191  asphalt pad y 0-1
#   perimeter stone curb y 2
#   z 24-26   cover_stand_a painted plate (south — Vinny)
#   z 166-168 cover_stand_b painted plate (north — Tan, back to the facade)
#   z 92-99   the valley wall
#   z 88-90 / z 102-104  dark-wood marker pads (south = Vinny's pick anchors,
#             *_n twins for Tan)

import json

W, H, D = 192, 48, 192
RED_BRICK, STONE, SIDEWALK, ASPHALT = 100, 101, 102, 104
DARK_WOOD, LIGHT_WOOD, DARK_TRIM, PAINTED_METAL, WHITE_FABRIC = 106, 107, 108, 111, 127

STAND_A_X, STAND_A_Z = 94, 24    # Vinny — south plate
STAND_B_X, STAND_B_Z = 94, 166   # Tan — north plate (back to the facade)

# ── Valley wall — symmetric about x=96, stepped Full→null toward center ──────
# h = column top in voxels (top = h*0.05m; pad top = y2 = 0.10m).
# h16 = 0.80m Full · h12 ≈ 0.60m crouch step (d≈0.4) · h9 = 0.45m Low reject
# band · h7 = 0.35m null dip — the door-axis sightline stays open.
VALLEY = [
    ( 20, 16, 16),              # west end — Full
    ( 36, 12, 15), ( 48, 12, 14), ( 60, 12, 12), ( 72, 12, 9),
    ( 84, 24,  7),              # center dip — the open lane on the door axis
    (108, 12,  9), (120, 12, 12), (132, 12, 14), (144, 12, 15),
    (156, 16, 16),              # east end — Full (mirror)
]
VAL_Z0, VAL_D = 92, 8           # same lane the pit used
PAD_S, PAD_N = 88, 102          # marker pad rows (south / north faces)

# ── Flank stubs — N-S stone walls, one per quadrant, mirrored ───────────────
# (x0, x1, z0, run, top_h) — stepped along length: h14 outer → h10 near mid.
FLANKS = [
    (38, 44, 56, 10, 14), (38, 44, 66, 12, 12), (38, 44, 78, 10, 10),   # SW
    (148, 154, 56, 10, 14), (148, 154, 66, 12, 12), (148, 154, 78, 10, 10),  # SE
    (38, 44, 104, 10, 10), (38, 44, 114, 12, 12), (38, 44, 126, 10, 14),  # NW
    (148, 154, 104, 10, 10), (148, 154, 114, 12, 12), (148, 154, 126, 10, 14),  # NE
]

# ── Pillars — h32 (1.6m): eye can't clear standing = hide-only cover ────────
PILLARS = [
    (46, 54, 44, 52), (138, 146, 44, 52),      # south pair
    (46, 54, 140, 148), (138, 146, 140, 148),  # north pair
]
PILLAR_H = 32

# ── Planters — h12 crouch cover at quadrant centers ─────────────────────────
PLANTERS = [
    (66, 76, 56, 66), (116, 126, 56, 66),      # south pair
    (66, 76, 126, 136), (116, 126, 126, 136),  # north pair
]
PLANTER_H = 12

# Pick anchors → wall landmarks (same names the pit's pick row reads).
PAD_AT = {
    "cover_wall":   27,         # h16 west end — Full-class pick
    "cover_crouch": 66,         # h12 west step — live crouch step
    "cover_mid":    78,         # h9 Low band — expected reject
    "cover_deep":   96,         # h7 center dip — expected reject, open lane
}

vox = {}
def put(x0, x1, y0, y1, z0, z1, m):
    for x in range(x0, x1):
        for y in range(y0, y1):
            for z in range(z0, z1):
                vox[(x, y, z)] = m

# --- ground pad — SIDEWALK (102) = the designated walkable surface, same
#     MAT id VoxelTerrainBuilder paints on city sidewalks and WaypointGraph
#     already measures. Fighters' walkableMaterials masks read this stamp.
put(0, W, 0, 2, 0, D, SIDEWALK)
# perimeter curb (1 voxel high over pad) — lot reads bounded, no wall class
put(0, W, 2, 3, 0, 2, STONE); put(0, W, 2, 3, D - 2, D, STONE)
put(0, 2, 2, 3, 0, D, STONE); put(W - 2, W, 2, 3, 0, D, STONE)

# --- opposing stand plates (painted-metal 4x2 pads, surface layer y2) ---
put(STAND_A_X, STAND_A_X + 4, 2, 3, STAND_A_Z, STAND_A_Z + 2, PAINTED_METAL)
put(STAND_B_X, STAND_B_X + 4, 2, 3, STAND_B_Z, STAND_B_Z + 2, PAINTED_METAL)

# --- valley wall (light wood; dark-trim lip on BOTH faces — either fighter
#     can hide on either side). Lip material is 108, deliberately OUTSIDE
#     the walkable floor family — a 106 lip on the h7 dip would sit at
#     0.45m = stepUpMax and become a legal staircase over the wall. ---
for x0, run, h in VALLEY:
    x1, z0, z1 = x0 + run, VAL_Z0, VAL_Z0 + VAL_D
    put(x0, x1, 2, h, z0, z1, LIGHT_WOOD)
    put(x0, x1, h - 1, h, z0, z0 + 1, DARK_TRIM)   # south lip
    put(x0, x1, h - 1, h, z1 - 1, z1, DARK_TRIM)   # north lip

# --- flank stubs (stone) ---
for x0, x1, z0, run, h in FLANKS:
    put(x0, x1, 2, h, z0, z0 + run, STONE)

# --- pillars (red brick — monument hide-only cover) ---
for x0, x1, z0, z1 in PILLARS:
    put(x0, x1, 2, PILLAR_H, z0, z1, RED_BRICK)

# --- planters (stone, crouch height) ---
for x0, x1, z0, z1 in PLANTERS:
    put(x0, x1, 2, PLANTER_H, z0, z1, STONE)

# --- marker pads (dark wood surface plates) carrying the pick anchors ---
marker_pads = {}
for key, cx in PAD_AT.items():
    for side, pz in ((key, PAD_S), (key + "_n", PAD_N)):
        put(cx - 2, cx + 2, 2, 3, pz, pz + 3, DARK_WOOD)
        marker_pads[side] = [(x, 2, z) for x in range(cx - 2, cx + 2)
                                       for z in range(pz, pz + 3)]

# --- building event points (painted regions → centroids; same contract as
#     the pit's cover_stand_a/_b + pick-pad groups) ---
PART_DEFS = [
    {"id": 0, "name": "Body",          "key": "body",          "color": "#888888", "desc": "Unassigned"},
    {"id": 1, "name": "Cover Stand A", "key": "cover_stand_a", "color": "#ff6b6b", "desc": "South stand spot — primary combatant (Vinny)"},
    {"id": 2, "name": "Cover Stand B", "key": "cover_stand_b", "color": "#00ddff", "desc": "North stand spot — opposing combatant (Tan Suit, back to the facade)"},
    {"id": 3, "name": "Cover Deep",    "key": "cover_deep",    "color": "#cc3333", "desc": "Pick anchor — center dip h7 (Low/null — expected reject)"},
    {"id": 4, "name": "Cover Mid",     "key": "cover_mid",     "color": "#dd8833", "desc": "Pick anchor — valley h9 Low band (expected reject)"},
    {"id": 5, "name": "Cover Crouch",  "key": "cover_crouch",  "color": "#ddcc33", "desc": "Pick anchor — valley h12 crouch step"},
    {"id": 6, "name": "Cover Wall",    "key": "cover_wall",    "color": "#33cc55", "desc": "Pick anchor — valley h16 Full end"},
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
# firing_* aliases so the Target Practice order treats the arena like a range.
attachment_points["firing_position"] = attachment_points["cover_stand_a"]
attachment_points["firing_target"]   = attachment_points["cover_stand_b"]

item_parts = {f"{x},{y},{z}": part_ids[k] for k, pts in painted.items() for x, y, z in pts
              if k in part_ids}   # *_n marker pads carry anchors, no part def

def mat(i, name, r, g, b):
    h = '#%02x%02x%02x' % (r, g, b)
    return {"id": i, "name": name, "r": r, "g": g, "b": b, "hex": h, "category": "building"}

materials = [
    mat(100, "Red Brick", 156, 74, 59), mat(101, "Stone", 122, 107, 87),
    mat(102, "Sidewalk", 150, 145, 138), mat(106, "Dark Wood", 76, 45, 25),
    mat(107, "Light Wood", 153, 107, 64), mat(108, "Dark Trim", 60, 35, 18),
    mat(111, "Painted Metal", 230, 224, 209),
]

data = {
    "name": "cover_arena",
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

out = "VoxelAssetStudio/JSON Models In Progress/cover_arena.json"
with open(out, "w", encoding="utf-8") as f:
    json.dump(data, f)
print(f"Wrote {out}: {len(data['voxels'])} voxels, {W}x{H}x{D} @ 0.05m")
print(f"  stands: a={attachment_points['cover_stand_a']} b={attachment_points['cover_stand_b']}")
