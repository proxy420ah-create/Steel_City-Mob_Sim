# gen_barber_shop.py — "Barber Shop" finalized small-lot building generator.
# Emits the consolidated stasset JSON the HTML editor / json_to_stasset import.
#
# Canonical lineage: the archive barber_0 (32x20x34 @0.1) was the first building
# authored for the small-lot variant — stucco shopfront, striped pole, striped
# awning, brown door, tar roof, brick parapet. Single story: period barbershops
# were ground-floor shops, and a low storefront mass differentiates the type
# from the tenement instead of copying it.
#
# Scale contract (post-upscale city grid — everything renders at 0.05m/voxel;
# SectorBaker ignores declared voxelSize, info.voxelSize = terrain 0.05):
#   door leaf   12w x 16h voxels (0.6m x 0.8m) — the tenement's own convention,
#               sized so a 0.68m-wide x 0.62m-tall NPC passes a hinged leaf
#   storefront  14v display glass (0.7m) — taller than the NPC, period bulkhead
#   awning edge ~0.95m — clears a walking NPC by ~6v
#   story pitch / cornice / parapet proportions taken from tenement_block_0
#
# A slot asset paints its own ground: 2v lot pad (cobble family = empty_land)
# + concrete front apron; base sits at terrain slab top.
#
# Layout (asset space; front = low Z, MODEL_DESIGN_STANDARD building conv.):
#   z 0..63     lot pad — cobble 105 (surface index y1 -> +0.2 world)
#   z 0..5      front apron — concrete 102 to the curb
#   x 10..53    building footprint 44v wide (10v side margins)
#   z 6..51     building depth 46v (rear yard z 52..63)
#
#   y 2..3      stone watertable + iron cellar grilles
#   y 4..5      wood bulkhead (storefront base)
#   y 6..17     display glass band, dark-wood mullions
#   y 18..19    transom panes
#   y 20..21    striped canvas awning protruding to z2 (edge ~0.95m)
#   y 2..17     recessed entry x13-24 (12v): L/R door leaves at z9 painted
#               door_leaf_l/r (interactive-leaf swap point), transom over leaf
#   y 22..23    painted-metal frieze band
#   y 24..25    cornice, 1v projection
#   y 26..31    brick parapet; raised center bay x22-41 to y34 carrying a dark
#               sign board + gold "BARBER" letters — the classic raised-parapet
#               signboard of the small one-story shop
#   y 32/35     stone coping
#   y 24        tar roof deck behind parapet
#   rear        service door + stoop, sash windows, brick chimney; interior
#               back-room partition with door gap
#
# Attachments: front_door / rear_door stoops, barber_pole, two rear-yard prop
# slots + door_leaf_l/r painted groups.
#
# Output: VoxelAssetStudio/JSON Models In Progress/barber_shop.json

import json

W, H, D = 64, 36, 64
RED_BRICK, STONE, CONCRETE, STUCCO = 100, 101, 102, 103
COBBLE = 105
DARK_WOOD, LIGHT_WOOD, WEATHERED_WOOD = 106, 107, 108
DARK_IRON, PAINTED_METAL = 109, 111
WINDOW_GLASS, STOREFRONT_GLASS = 112, 114
TAR, PAINTED_RED, PAINTED_BROWN = 118, 120, 122
GOLD_BRASS, WHITE_FABRIC, PAINTED_BLUE, CHROME = 123, 127, 129, 142

BX0, BX1 = 10, 54          # footprint x extent — 44v
BZ0, BZ1 = 6, 52           # footprint z — front face z=6, rear z=51
WALL = 2
DOOR_X0, DOOR_X1 = 13, 25  # 12v-wide entry (tenement convention), left of center
DOOR_Y0, DOOR_Y1 = 2, 18   # 16v leaf height
LEAF_SPLIT = 19            # two 6v leaves: x13-18 (L) + x19-24 (R)
POLE_X = 28                # pole right of door, before the glass run
BLADE_X = 47               # perpendicular blade sign, right end
SIGN_X0, SIGN_X1 = 22, 42  # raised parapet sign bay

vox = {}
def put(x0, x1, y0, y1, z0, z1, m):
    for x in range(x0, x1):
        for y in range(y0, y1):
            for z in range(z0, z1):
                vox[(x, y, z)] = m

def putv(x, y, z, m):
    vox[(x, y, z)] = m

def carve(x0, x1, y0, y1, z0, z1):
    for x in range(x0, x1):
        for y in range(y0, y1):
            for z in range(z0, z1):
                vox.pop((x, y, z), None)

def window(plane_axis, a0, a1, y0, y1, fixed, m=WINDOW_GLASS, frame=DARK_WOOD):
    """Glazed opening: pane on the outermost wall cell, bore behind = AIR
    (pane convention from procedural_mob_buildings._add_windows_all_sides)."""
    if plane_axis == 'z':
        put(a0, a1, y0, y1, fixed, fixed + 1, m)
        if fixed == BZ0:
            carve(a0, a1, y0, y1, BZ0 + 1, BZ0 + WALL)
        else:
            carve(a0, a1, y0, y1, BZ1 - WALL, fixed)
        put(a0 - 1, a1 + 1, y0 - 1, y0, fixed, fixed + 1, frame)
        put(a0 - 1, a1 + 1, y1, y1 + 1, fixed, fixed + 1, STONE)
    else:
        put(fixed, fixed + 1, y0, y1, a0, a1, m)
        if fixed == BX0:
            carve(fixed + 1, BX0 + WALL, y0, y1, a0, a1)
        else:
            carve(BX1 - WALL, fixed, y0, y1, a0, a1)
        put(fixed, fixed + 1, y0 - 1, y0, a0 - 1, a1 + 1, frame)
        put(fixed, fixed + 1, y1, y1 + 1, a0 - 1, a1 + 1, STONE)

# 3x5 block font for sign lettering
FONT = {
    'B': [[1,1,0],[1,0,1],[1,1,0],[1,0,1],[1,1,0]],
    'A': [[0,1,0],[1,0,1],[1,1,1],[1,0,1],[1,0,1]],
    'R': [[1,1,0],[1,0,1],[1,1,0],[1,0,1],[1,0,1]],
    'E': [[1,1,1],[1,0,0],[1,1,0],[1,0,0],[1,1,1]],
}
def paint_word(word, x0, y_top, z, m):
    cx = x0
    for ch in word:
        glyph = FONT[ch]
        for ry, row in enumerate(glyph):
            for rx, bit in enumerate(row):
                if bit:
                    putv(cx + rx, y_top - ry, z, m)
        cx += 4

def build():
    vox.clear()

    # ---- lot pad: 2v cobble + concrete front apron ----
    put(0, W, 0, 2, 0, D, COBBLE)
    put(0, W, 0, 2, 0, BZ0, CONCRETE)

    # ---- shell: stone watertable + stucco walls (single story, y2-25) ----
    put(BX0, BX1, 2, 4, BZ0, BZ0 + WALL, STONE)
    put(BX0, BX1, 2, 4, BZ1 - WALL, BZ1, STONE)
    put(BX0, BX0 + WALL, 2, 4, BZ0, BZ1, STONE)
    put(BX1 - WALL, BX1, 2, 4, BZ0, BZ1, STONE)
    for (x0, x1, z0, z1) in ((BX0, BX1, BZ0, BZ0 + WALL), (BX0, BX1, BZ1 - WALL, BZ1),
                             (BX0, BX0 + WALL, BZ0, BZ1), (BX1 - WALL, BX1, BZ0, BZ1)):
        put(x0, x1, 4, 26, z0, z1, STUCCO)

    # interior back-room partition with a door gap (front shop / back room)
    put(BX0 + WALL, BX1 - WALL, 2, 24, 40, 41, STUCCO)
    carve(28, 34, 2, 16, 40, 41)                       # interior door gap
    put(28, 34, 16, 17, 40, 41, DARK_WOOD)             # gap header

    # ---- storefront (front face z = BZ0) ----
    # bulkhead + glass band + transom; door bay carved separately
    put(BX0 + 2, BX1 - 2, 4, 6, BZ0, BZ0 + 1, DARK_WOOD)
    mull = list(range(BX0 + 2, BX1 - 1, 7))
    for x0 in mull:
        x1 = min(x0 + 6, BX1 - 2)
        put(x0, x1, 6, 18, BZ0, BZ0 + 1, STOREFRONT_GLASS)
        carve(x0, x1, 6, 18, BZ0 + 1, BZ0 + WALL)
        if x1 < BX1 - 2:
            put(x1, x1 + 1, 4, 18, BZ0, BZ0 + WALL, DARK_WOOD)
    # transom panes y18-19 over every bay
    for x0 in mull:
        put(x0, min(x0 + 6, BX1 - 2), 18, 20, BZ0, BZ0 + 1, STOREFRONT_GLASS)
        carve(x0, min(x0 + 6, BX1 - 2), 18, 20, BZ0 + 1, BZ0 + WALL)
    # mullions continue through transom + header above
    for x0 in mull[1:]:
        put(x0 - 1, x0, 18, 20, BZ0, BZ0 + WALL, DARK_WOOD)

    # ---- recessed entry x13-24, 12v wide, leaves at z9 ----
    carve(DOOR_X0, DOOR_X1, DOOR_Y0, DOOR_Y1 + 2, BZ0, BZ0 + 3)
    # cheeks closing the recess sides at z8
    put(DOOR_X0 - 1, DOOR_X0, DOOR_Y0, DOOR_Y1 + 2, BZ0 + 2, BZ0 + 3, DARK_WOOD)
    put(DOOR_X1, DOOR_X1 + 1, DOOR_Y0, DOOR_Y1 + 2, BZ0 + 2, BZ0 + 3, DARK_WOOD)
    # transom glass over the leaves (y18-19, inside the recess at z8)
    put(DOOR_X0, DOOR_X1, DOOR_Y1, DOOR_Y1 + 2, BZ0 + 2, BZ0 + 3, STOREFRONT_GLASS)
    # door leaves L/R at z9 — painted as door_leaf_l/r item groups so a runtime
    # interactive-leaf system can swap them for hinged models later
    for leaf, lx in (("door_leaf_l", (DOOR_X0, LEAF_SPLIT)),
                     ("door_leaf_r", (LEAF_SPLIT, DOOR_X1))):
        pass  # painted after PART_DEFS exist; geometry first:
    put(DOOR_X0, DOOR_X1, DOOR_Y0, 11, BZ0 + 3, BZ0 + 4, PAINTED_BROWN)
    put(DOOR_X0, DOOR_X1, 11, 16, BZ0 + 3, BZ0 + 4, WINDOW_GLASS)
    put(DOOR_X0, DOOR_X1, 16, DOOR_Y1, BZ0 + 3, BZ0 + 4, DARK_WOOD)
    # center meeting stile + threshold
    put(LEAF_SPLIT - 1, LEAF_SPLIT + 1, DOOR_Y0, DOOR_Y1, BZ0 + 3, BZ0 + 4, DARK_WOOD)
    put(DOOR_X0 - 1, DOOR_X1 + 1, 1, 2, BZ0 - 2, BZ0 + 3, STONE)

    # awning: striped canvas over the storefront, protruding z2-5, edge ~y19-20
    for x in range(BX0 + 2, BX1 - 2):
        m = PAINTED_RED if ((x - BX0) // 3) % 2 == 0 else PAINTED_METAL
        put(x, x + 1, 20, 21, 4, BZ0, m)
        put(x, x + 1, 19, 20, 2, 4, m)
        put(x, x + 1, 18, 19, 2, 3, m)      # valance drop

    # frieze band + projecting cornice
    put(BX0 + 1, BX1 - 1, 22, 24, BZ0 - 1, BZ0 + WALL, PAINTED_METAL)
    for (x0, x1, z0, z1) in ((BX0 - 1, BX1 + 1, BZ0 - 1, BZ0 + WALL),
                             (BX0 - 1, BX1 + 1, BZ1 - WALL, BZ1 + 1),
                             (BX0 - 1, BX0 + WALL, BZ0 - 1, BZ1 + 1),
                             (BX1 - WALL, BX1 + 1, BZ0 - 1, BZ1 + 1)):
        put(x0, x1, 24, 26, z0, z1, PAINTED_METAL)

    # ---- parapet: brick, stepped — center bay raised, carrying signboard ----
    put(BX0, BX1, 26, 32, BZ0, BZ0 + WALL, RED_BRICK)
    put(BX0, BX1, 26, 32, BZ1 - WALL, BZ1, RED_BRICK)
    put(BX0, BX0 + WALL, 26, 32, BZ0, BZ1, RED_BRICK)
    put(BX1 - WALL, BX1, 26, 32, BZ0, BZ1, RED_BRICK)
    put(SIGN_X0, SIGN_X1, 32, 34, BZ0, BZ0 + WALL, RED_BRICK)          # raised bay
    # coping: sides at y32, raised bay at y34
    put(BX0 - 1, BX1 + 1, 32, 33, BZ0 - 1, BZ0 + WALL, STONE)
    put(BX0 - 1, BX1 + 1, 32, 33, BZ1 - WALL, BZ1 + 1, STONE)
    put(BX0 - 1, BX0 + WALL, 32, 33, BZ0 - 1, BZ1 + 1, STONE)
    put(BX1 - WALL, BX1 + 1, 32, 33, BZ0 - 1, BZ1 + 1, STONE)
    put(SIGN_X0 - 1, SIGN_X1 + 1, 34, 35, BZ0 - 1, BZ0 + WALL, STONE)  # bay cap
    # signboard on the raised bay face + gold BARBER letters
    put(SIGN_X0 + 1, SIGN_X1 - 1, 27, 33, BZ0 - 1, BZ0, DARK_IRON)
    paint_word('BARBER', SIGN_X0 + 3, 31, BZ0 - 1, GOLD_BRASS)
    # tar roof deck behind the parapet
    put(BX0 + WALL, BX1 - WALL, 24, 25, BZ0 + WALL, BZ1 - WALL, TAR)

    # ---- barber pole: bracket + 2x2 spiral + chrome cap, right of door ----
    put(POLE_X - 1, POLE_X + 1, 5, 7, BZ0 - 1, BZ0, DARK_IRON)
    stripes = (PAINTED_RED, WHITE_FABRIC, PAINTED_BLUE)
    for y in range(7, 17):
        for dx in (0, 1):
            for dz in (0, 1):
                putv(POLE_X - 1 + dx, y, BZ0 - 3 + dz, stripes[(y + dx + dz) % 3])
    put(POLE_X - 1, POLE_X + 1, 17, 18, BZ0 - 3, BZ0 - 1, CHROME)
    put(POLE_X - 1, POLE_X + 1, 6, 7, BZ0 - 3, BZ0 - 1, DARK_IRON)

    # ---- blade sign: perpendicular board hung off the front, right end ----
    put(BLADE_X, BLADE_X + 1, 22, 29, 3, BZ0, DARK_IRON)
    put(BLADE_X, BLADE_X + 1, 22, 23, 3, BZ0, GOLD_BRASS)
    put(BLADE_X, BLADE_X + 1, 28, 29, 3, BZ0, GOLD_BRASS)
    put(BLADE_X, BLADE_X + 1, 23, 28, 3, 4, GOLD_BRASS)
    put(BLADE_X, BLADE_X + 1, 23, 28, BZ0 - 1, BZ0, GOLD_BRASS)

    # ---- cellar grilles in the watertable, clear of the door bay ----
    for x0 in (34, 44):
        carve(x0, x0 + 3, 2, 4, BZ0, BZ0 + WALL)
        put(x0, x0 + 3, 2, 4, BZ0, BZ0 + 1, DARK_IRON)

    # ---- side windows: high-set bathroom/office sash (1 per side) ----
    window('x', 24, 29, 8, 14, BX0)
    window('x', 24, 29, 8, 14, BX1 - 1)

    # ---- rear: service door + stoop, two sash windows ----
    carve(29, 35, 2, 16, BZ1 - WALL, BZ1)
    put(29, 35, 2, 16, BZ1 - 1, BZ1, DARK_WOOD)
    put(28, 29, 2, 16, BZ1 - WALL, BZ1, STONE)
    put(35, 36, 2, 16, BZ1 - WALL, BZ1, STONE)
    put(28, 36, 16, 17, BZ1 - WALL, BZ1, STONE)
    put(28, 36, 1, 2, BZ1, BZ1 + 2, STONE)
    window('z', 16, 20, 8, 14, BZ1 - 1)
    window('z', 42, 46, 8, 14, BZ1 - 1)

    # ---- chimney: red brick rear-left, stone cap ----
    put(14, 18, 24, 32, 44, 48, RED_BRICK)
    put(13, 19, 32, 33, 43, 49, STONE)

    # ---- attachment points ----
    def surface(x, z, w2, d2, y):
        return [(xx, y, zz) for xx in range(x, x + w2) for zz in range(z, z + d2)]

    painted = {
        "front_door":  surface(DOOR_X0, 4, DOOR_X1 - DOOR_X0, 4, 1),
        "rear_door":   surface(29, 52, 6, 2, 1),
        "barber_pole": [(POLE_X - 1 + dx, 8, BZ0 - 3 + dz) for dx in (0, 1) for dz in (0, 1)],
        "prop_slot_0": surface(16, 55, 4, 4, 1),
        "prop_slot_1": surface(44, 55, 4, 4, 1),
        "door_leaf_l": [(x, y, z) for x in range(DOOR_X0, LEAF_SPLIT)
                        for y in range(DOOR_Y0, DOOR_Y1) for z in (BZ0 + 3,)],
        "door_leaf_r": [(x, y, z) for x in range(LEAF_SPLIT, DOOR_X1)
                        for y in range(DOOR_Y0, DOOR_Y1) for z in (BZ0 + 3,)],
    }
    PART_DEFS = [
        {"id": 0, "name": "Body",        "key": "body",        "color": "#888888", "desc": "Unassigned"},
        {"id": 1, "name": "Front Door",  "key": "front_door",  "color": "#dd8833", "desc": "Main entry — shop waypoint/entry point"},
        {"id": 2, "name": "Rear Door",   "key": "rear_door",   "color": "#33ccdd", "desc": "Service entry — rear yard access"},
        {"id": 3, "name": "Barber Pole", "key": "barber_pole", "color": "#dd3333", "desc": "Shop sign marker"},
        {"id": 4, "name": "Prop Slot",   "key": "prop_slot",   "color": "#33cc55", "desc": "Yard prop placement (crate/barrel/privy)"},
        {"id": 5, "name": "Door Leaf L", "key": "door_leaf_l", "color": "#8855dd", "desc": "Left hinged leaf — interactive-door swap point"},
        {"id": 6, "name": "Door Leaf R", "key": "door_leaf_r", "color": "#bb55dd", "desc": "Right hinged leaf — interactive-door swap point"},
    ]
    part_ids = {d["key"]: d["id"] for d in PART_DEFS}
    key_of = {"front_door": "front_door", "rear_door": "rear_door",
              "barber_pole": "barber_pole",
              "prop_slot_0": "prop_slot", "prop_slot_1": "prop_slot",
              "door_leaf_l": "door_leaf_l", "door_leaf_r": "door_leaf_r"}

    def centroid(pts):
        n = float(len(pts))
        return {k: round(sum(p[i] for p in pts) / n, 3) for i, k in enumerate("xyz")}

    attachment_points = {k: centroid(v) for k, v in painted.items()}
    item_parts = {f"{x},{y},{z}": part_ids[key_of[k]]
                  for k, pts in painted.items() for x, y, z in pts}
    return attachment_points, item_parts, PART_DEFS


def mat(i, name, hexstr):
    h = hexstr.lstrip('#')
    return {"id": i, "name": name, "r": int(h[0:2], 16), "g": int(h[2:4], 16),
            "b": int(h[4:6], 16), "hex": hexstr, "category": "building"}


def main():
    aps, parts, defs = build()
    data = {
        "name": "barber_shop",
        "dims": [W, H, D],
        "assetType": "building",
        "voxelSize": 0.05,
        "materials": [
            mat(100, "Red Brick", "#934233"), mat(101, "Stone", "#7a6b56"),
            mat(102, "Sidewalk", "#96918a"), mat(103, "Stucco", "#d1ccc0"),
            mat(105, "Cobblestone", "#6b6157"), mat(106, "Dark Wood", "#4c2d19"),
            mat(107, "Light Wood", "#996b40"), mat(108, "Weathered Wood", "#6b5b42"),
            mat(109, "Dark Iron", "#473d38"), mat(111, "Painted Metal", "#e5e0d1"),
            mat(112, "Window Glass", "#728ca5"), mat(114, "Storefront Glass", "#8ca5b2"),
            mat(118, "Tar", "#473d33"), mat(120, "Painted Red", "#721e19"),
            mat(122, "Painted Brown", "#381e14"), mat(123, "Gold/Brass", "#c69e33"),
            mat(127, "White Fabric", "#e0dcd1"), mat(129, "Painted Blue", "#1f3373"),
            mat(142, "Chrome", "#bfc7d1"),
        ],
        "voxels": [[x, y, z, m] for (x, y, z), m in sorted(vox.items())],
        "groups": {}, "regions": {},
        "attachmentPoints": aps,
        "itemParts": parts, "itemPartDefs": defs,
        "format": "steelcity_stasset", "version": 1,
        "meta": {
            "door_face": "front", "door_height": 16, "door_width": 12,
            "door_y": 2, "door_x_center": 19,
            "door_leaf_l": {"x": [13, 18], "hinge_x": 13, "z": 9},
            "door_leaf_r": {"x": [19, 24], "hinge_x": 24, "z": 9},
            "rear_door_face": "rear", "rear_door_height": 14, "rear_door_width": 6,
            "lot_pad": "cobble", "style": "barber_1story",
            "scale_note": "authored for the 0.05m grid — door 12x16v = 0.6x0.8m (tenement convention); opening fits 0.68m-wide NPC",
        },
    }
    out = "VoxelAssetStudio/JSON Models In Progress/barber_shop.json"
    with open(out, "w", encoding="utf-8") as f:
        json.dump(data, f)
    print(f"Wrote {out}: {len(data['voxels'])} voxels, {W}x{H}x{D} @ 0.05m")
    print(f"  anchors: {aps}")


if __name__ == "__main__":
    main()
