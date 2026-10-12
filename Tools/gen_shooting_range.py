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
WINDOW_GLASS = 112   # ballistics mold glass
# (134 = "Cracked Glass" reserved for the future shatter halo — mob_materials)

FIRE_Z = 12          # firing line
LANE_X = (76, 116)   # lane edges (exclusive) — center lane, others via dx
TGT_Z = 169          # target board plane
BACK_Z0, BACK_Z1 = 184, 188  # backstop slab
LANE_DX = (-60, 0, 60)   # lanes 1/2/3 = left/center/right x offsets

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

# --- firing line + three lanes ---
# Lane layout is identical per lane; dx shifts each lane left/right.
# Lane 1 = dx -60 (x16-55), lane 2 = dx 0 (x76-115, original), lane 3 = dx +60 (x136-175).
put(0, W, 2, 3, FIRE_Z, FIRE_Z + 2, PAINTED_METAL)            # firing line stripe (full width)

for dx in LANE_DX:
    put(94 + dx, 98 + dx, 2, 3, FIRE_Z - 2, FIRE_Z, PAINTED_METAL)   # stand spot
    put(90 + dx, 102 + dx, 2, 5, FIRE_Z + 2, FIRE_Z + 4, LIGHT_WOOD) # bench by the line
    # lane pad + rails
    put(LANE_X[0] + dx, LANE_X[1] + dx, 2, 3, FIRE_Z + 4, 182, CONCRETE)
    put(LANE_X[0] - 2 + dx, LANE_X[0] + dx, 2, 4, FIRE_Z + 4, 182, STONE)   # left rail
    put(LANE_X[1] + dx, LANE_X[1] + 2 + dx, 2, 4, FIRE_Z + 4, 182, STONE)   # right rail
    # distance marks (5m / 10m / 15m from firing line)
    for dist in (50, 100, 150):
        z = FIRE_Z + dist
        put(LANE_X[0] + dx, LANE_X[1] + dx, 3, 4, z, z + 2, PAINTED_METAL)
    # target (posts, white board, red bullseye at NPC chest height)
    put(88 + dx, 91 + dx, 2, 15, TGT_Z, TGT_Z + 2, DARK_WOOD)          # left post
    put(101 + dx, 104 + dx, 2, 15, TGT_Z, TGT_Z + 2, DARK_WOOD)        # right post
    put(84 + dx, 108 + dx, 4, 13, TGT_Z, TGT_Z + 1, WHITE_FABRIC)      # board
    put(94 + dx, 98 + dx, 6, 10, TGT_Z, TGT_Z + 1, RED_BRICK)          # bullseye

# --- lane 3 ballistics bench: glass "mold" cube socketed in a table ---
# The mold is an 8^3 Window Glass cube whose centroid sits at the SAME height
# as the firing_target aim point (index 7.5), parked on the mid-range (10m)
# mark of lane 3. Because the pad surface tops out at index 3 and the cube's
# bottom row is index 4, the bench gets exactly two layers: legs at y3 and a
# tabletop ring at y4 — the cube's bottom glass row fills the ring's hole, so
# the block reads as socketed into the stand.
# A 2x2x2 painted-metal core sits at the cube's exact center and is painted
# "glass_target": the aim attachment lands on indestructible metal, so the
# point survives the surrounding glass being shot out.
DX3 = LANE_DX[2]
TX0, TX1 = 149, 163            # table footprint x149..162 (centroid 155.5)
TZ0, TZ1 = 106, 120            # z106..119 (centroid 112.5, on the 10m mark)
for lx in (TX0, TX1 - 2):
    for lz in (TZ0, TZ1 - 2):
        put(lx, lx + 2, 3, 4, lz, lz + 2, DARK_WOOD)               # corner legs
put(TX0, TX1, 4, 5, TZ0, TZ1, DARK_WOOD)                            # tabletop slab
put(TX0, TX1, 4, 5, TZ0, TZ0 + 1, PAINTED_METAL)                    # banding: front
put(TX0, TX1, 4, 5, TZ1 - 1, TZ1, PAINTED_METAL)                    #           back
put(TX0, TX0 + 1, 4, 5, TZ0, TZ1, PAINTED_METAL)                    #           left
put(TX1 - 1, TX1, 4, 5, TZ0, TZ1, PAINTED_METAL)                    #           right
# glass mold 8x8x8 — bottom row embeds flush in the tabletop (socketed)
put(152, 160, 4, 12, 109, 117, WINDOW_GLASS)                        # centroid (155.5, 7.5, 112.5)
# metal aim core — replaces glass at the cube's exact center
put(155, 157, 7, 9, 112, 114, PAINTED_METAL)                        # 2x2x2 at centroid

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
    # --- per-lane duplicates of 1-3 (lane 2 = center keeps ids 1-3 semantics) ---
    {"id": 9,  "name": "Firing Position 1", "key": "firing_position_1", "color": "#ff8f8f", "desc": "Lane 1 (left) stand spot"},
    {"id": 10, "name": "Firing Target 1",   "key": "firing_target_1",   "color": "#66e5ff", "desc": "Lane 1 (left) aim point"},
    {"id": 11, "name": "Backstop 1",        "key": "backstop_1",        "color": "#ffbb66", "desc": "Lane 1 (left) facing reference"},
    {"id": 12, "name": "Firing Position 2", "key": "firing_position_2", "color": "#ff6b6b", "desc": "Lane 2 (center) stand spot"},
    {"id": 13, "name": "Firing Target 2",   "key": "firing_target_2",   "color": "#00ddff", "desc": "Lane 2 (center) aim point"},
    {"id": 14, "name": "Backstop 2",        "key": "backstop_2",        "color": "#ffaa00", "desc": "Lane 2 (center) facing reference"},
    {"id": 15, "name": "Firing Position 3", "key": "firing_position_3", "color": "#d46bff", "desc": "Lane 3 (right) stand spot"},
    {"id": 16, "name": "Firing Target 3",   "key": "firing_target_3",   "color": "#7dff9e", "desc": "Lane 3 (right) aim point"},
    {"id": 17, "name": "Backstop 3",        "key": "backstop_3",        "color": "#ffd24d", "desc": "Lane 3 (right) facing reference"},
    # --- ballistics bench on lane 3 ---
    {"id": 18, "name": "Glass Target",      "key": "glass_target",      "color": "#9ef0ff", "desc": "Aim point — metal core inside the glass mold (indestructible)"},
    {"id": 19, "name": "Glass Mold",        "key": "glass_mold",        "color": "#728ca5", "desc": "8^3 ballistic glass cube — destructible test medium"},
    {"id": 20, "name": "Ballistics Bench",  "key": "ballistics_bench",  "color": "#8a6a45", "desc": "Stand hosting the glass mold at lane-3 mid-range"},
]

painted = {}
for lane_i, dx in enumerate(LANE_DX, start=1):
    suf = "" if lane_i == 2 else f"_{lane_i}"
    # stand spot = painted square just behind the firing line (pad surface layer)
    painted[f"firing_position{suf}"] = [(x, 2, z) for x in range(94 + dx, 98 + dx) for z in range(FIRE_Z - 2, FIRE_Z)]
    # bullseye = the red center of the lane's target board
    painted[f"firing_target{suf}"]   = [(x, y, TGT_Z) for x in range(94 + dx, 98 + dx) for y in range(6, 10)]
    # backstop = patch on the inner face of the brick wall behind the target
    painted[f"backstop{suf}"]        = [(x, y, BACK_Z0) for x in range(88 + dx, 104 + dx) for y in range(8, 16)]

# lane-3 ballistics bench + mold (legs y3 under a y4 tabletop ring)
painted["ballistics_bench"] = (
    [(x, 4, z) for x in range(149, 163) for z in range(106, 120)]           # tabletop
    + [(x, 3, z) for x in (149, 150, 161, 162) for z in (106, 107, 118, 119)] # legs
)
painted["glass_target"] = [(x, y, z) for x in range(155, 157) for y in range(7, 9) for z in range(112, 114)]
painted["glass_mold"] = [
    (x, y, z) for x in range(152, 160) for y in range(4, 12) for z in range(109, 117)
    if (x, y, z) not in set(painted["glass_target"])
]
part_ids = {d["key"]: d["id"] for d in PART_DEFS}

def centroid(pts):
    n = float(len(pts))
    return {k: round(sum(p[i] for p in pts) / n, 3) for i, k in enumerate("xyz")}

attachment_points = {k: centroid(v) for k, v in painted.items()}
# lane-2 aliases so runtime code can use a uniform "_N" suffix per lane
for base in ("firing_position", "firing_target", "backstop"):
    attachment_points[f"{base}_2"] = attachment_points[base]
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
    mat(112, "Window Glass", 114, 140, 165), mat(134, "Cracked Glass", 209, 224, 235),
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
