# gen_alley_block.py — "Alley Block" full-block asset generator.
# Emits the consolidated stasset JSON the HTML editor / json_to_stasset import.
#
# Container matches the tenement/testbed contract: 192 x 16 x 192 @ 0.05m/voxel
# (9.6m square lot — the 3x3 building-slot grid). Low profile: ground + lane
# only, facades/buildings arrive as separate per-slot .stassets later.
#
# An ALLEY BLOCK replaces the 9-slot grid's middle row with a pedestrian
# service lane — sidewalk flanks raised 1v over a narrow asphalt road that
# blends into the streets at both mouths — leaving 6 building plots
# (3 north, 3 south). Cars get no corridor: the lane lives inside the block
# so RoadGraph (seam-driven) never routes through it.
# Materials stay vanilla + match the city's other lots: ASPHALT(104) road,
# CONCRETE(102) lane sidewalks, COBBLESTONE(105) lot pads (same ground the
# empty_land lots use), STONE(101) debris + curb/plot seams — MAT_ALLEY(133)
# deferred until the waypoint scanner needs a distinct lane stamp.
#
# Layout (spec space = E-W lane along X; front = low Z):
#   z 0..63     plot row S — three 64x64 cobble pads + stone debris (bake)
#   z 64..79    lane sidewalk (raised 1v over the road = curb step)
#   z 80..111   alley road — asphalt, 32v wide, at base grade
#   z 112..127  lane sidewalk (raised)
#   z 128..191  plot row N — three 64x64 cobble pads + stone debris (bake)
#   perimeter   2v stone curb ring (index 2), OPEN at both lane mouths
#
# Heights: slab y0-1 → road surface y2 · raised layer y2 → sidewalk/plot
# surface y3 · curb voxel y2 → frame top y3 (flush with the pads).
#
# Debris is BAKED — a faithful port of ProceduralDebrisScatterer.
# ScatterSeeded (weighted mix, 1x1/2x2 clusters, weeds, fence remnants,
# rubble mounds), run per 64x64 pad. Baked because the runtime scatterer
# only targets 'empty_land' assets — if it's ever pointed at alley pads,
# remove the bake or the count doubles.
#
# --axis ns regenerates with the lane running N-S (spec space is always the
# E-W layout; the fill transposes every rect) → alley_block_ns.json.

import argparse
import json
import random

W, H, D = 192, 16, 192
STONE, SIDEWALK, ASPHALT, COBBLE = 101, 102, 104, 105
DARK_WOOD, WEATHERED_WOOD, AGED_METAL = 106, 108, 110
TAR, PAINTED_RED, PAINTED_GREEN, PAINTED_BROWN = 118, 120, 121, 122
CONCRETE = SIDEWALK  # palette id 102 — same material the scatterer calls CONCRETE

# Debris type table — mirror of ProceduralDebrisScatterer.DEBRIS_TYPES
# (mat, weight, maxHeight); keep in sync if the C# table changes.
DEBRIS_TYPES = [
    (CONCRETE,       0.25, 3),
    (WEATHERED_WOOD, 0.20, 4),
    (STONE,          0.15, 2),
    (AGED_METAL,     0.10, 2),
    (DARK_WOOD,      0.10, 3),
    (COBBLE,         0.08, 2),
    (TAR,            0.05, 1),
    (PAINTED_BROWN,  0.04, 2),
    (PAINTED_RED,    0.02, 2),
    (PAINTED_GREEN,  0.01, 2),
]
DEBRIS_DENSITY = 0.03  # matches ScatterVariant default

ROAD_TOP, WALK_TOP = 1, 2        # top voxel index: road slab vs raised walk
CURB_Y = 2                       # curb sits at index 2 on the edge slab
LANE_Z0, LANE_Z1 = 64, 128       # middle slot row
SW_A0, SW_A1 = 64, 80            # lane sidewalk band A (south side)
ROAD_Z0, ROAD_Z1 = 80, 112       # asphalt road band inside the lane
SW_B0, SW_B1 = 112, 128          # lane sidewalk band B (north side)
JOINTS = (63, 127)               # 2v stone joints between the three plot pads

vox = {}
_axis = 'ew'
def put(x0, x1, y0, y1, z0, z1, m):
    if _axis == 'ns':
        x0, x1, z0, z1 = z0, z1, x0, x1     # transpose rect: lane runs N-S
    for x in range(x0, x1):
        for y in range(y0, y1):
            for z in range(z0, z1):
                vox[(x, y, z)] = m

def P(x, y, z):
    return (z, y, x) if _axis == 'ns' else (x, y, z)

def scatter_pad(vox, px0, pz0, seed):
    """Port of ProceduralDebrisScatterer.ScatterSeeded for one 64x64 pad.
    Pad-local coords; debris lands on the pad surface (top index WALK_TOP).
    Axis-transposed via P() so 'ns' blocks scatter identically."""
    rng = random.Random(seed)
    gy = WALK_TOP  # surface top index — debris stacks at gy+1+y

    def get(px, vy, pz):
        return vox.get(P(px0 + px, vy, pz0 + pz), 0)
    def set_(px, vy, pz, m):
        vox[P(px0 + px, vy, pz0 + pz)] = m

    # --- debris clusters: target = 64*64*0.03 = 123 placements ---
    target = round(64 * 64 * DEBRIS_DENSITY)
    placed = attempts = 0
    while placed < target and attempts < target * 3:
        attempts += 1
        x = rng.randrange(2, 62); z = rng.randrange(2, 62)
        r = rng.random(); cum = 0.0; m = CONCRETE
        for mm, wgt, _ in DEBRIS_TYPES:
            cum += wgt
            if r < cum: m = mm; break
        max_h = next(mh for mm, _, mh in DEBRIS_TYPES if mm == m)
        csize = rng.randrange(1, 3)             # 1x1 or 2x2 footprint
        cheight = rng.randrange(1, max_h + 1)
        placed_any = False
        for cx in range(csize):
            for cz in range(csize):
                px, pz = x + cx - csize // 2, z + cz - csize // 2
                if px < 1 or px >= 63 or pz < 1 or pz >= 63:
                    continue
                lh = max(1, cheight - rng.randrange(0, 2))
                for y in range(lh):
                    if get(px, gy + 1 + y, pz) == 0:
                        set_(px, gy + 1 + y, pz, m); placed_any = True
        if placed_any:
            placed += 1

    # --- weeds: 1-3 single painted-green cells ---
    for _ in range(rng.randrange(1, 4)):
        x = rng.randrange(2, 62); z = rng.randrange(2, 62)
        if get(x, gy + 1, z) == 0:
            set_(x, gy + 1, z, PAINTED_GREEN)

    # --- 25% broken fence remnant: weathered-wood row, len 3-7, h 2-4 ---
    if rng.random() < 0.25:
        flen = rng.randrange(3, 8)
        fx = rng.randrange(2, 64 - flen - 2); fz = rng.randrange(2, 62)
        fh = rng.randrange(2, 5)
        for i in range(flen):
            px = fx + i
            if px >= 63: break
            for y in range(fh):
                if get(px, gy + 1 + y, fz) == 0:
                    set_(px, gy + 1 + y, fz, WEATHERED_WOOD)

    # --- 15% rubble mound: cone, radius 2-4, height 2-3, concrete|stone ---
    if rng.random() < 0.15:
        mx = rng.randrange(16, 48); mz = rng.randrange(16, 48)
        rad = rng.randrange(2, 5); mh = rng.randrange(2, 4)
        mm = CONCRETE if rng.random() < 0.5 else STONE
        for dx in range(-rad, rad + 1):
            for dz in range(-rad, rad + 1):
                dist = (dx * dx + dz * dz) ** 0.5
                if dist > rad: continue
                px, pz = mx + dx, mz + dz
                if px < 1 or px >= 63 or pz < 1 or pz >= 63: continue
                lh = round(mh * (1 - dist / rad))
                for y in range(lh):
                    if get(px, gy + 1 + y, pz) == 0:
                        set_(px, gy + 1 + y, pz, mm)

def build():
    vox.clear()

    # --- base slab — SIDEWALK(102) under everything (same stamp the terrain
    #     painter + WalkLayer treat as the walkable family) ---
    put(0, W, 0, ROAD_TOP + 1, 0, D, SIDEWALK)

    # --- alley road — asphalt at base grade (surface y2, 1v below walks);
    #     same material as the streets so the mouths read continuous ---
    put(0, W, 0, ROAD_TOP + 1, ROAD_Z0, ROAD_Z1, ASPHALT)

    # --- lane sidewalks — raised walk flanking the road ---
    put(0, W, ROAD_TOP + 1, WALK_TOP + 1, SW_A0, SW_A1, SIDEWALK)
    put(0, W, ROAD_TOP + 1, WALK_TOP + 1, SW_B0, SW_B1, SIDEWALK)

    # --- six plot pads — cobble lots on the raised layer (same ground as
    #     the city's empty_land plots) ---
    pads = []
    for z0 in (0, 128):                        # south row / north row
        for x0 in (0, 64, 128):                # west / middle / east
            put(x0, x0 + 64, ROAD_TOP + 1, WALK_TOP + 1, z0, z0 + 64, COBBLE)
            pads.append((x0, z0))

    # --- plot seam joints — flush 2v stone lines on the slot grid so the six
    #     pads read as distinct lots (snap guides for building placement) ---
    for z0, z1 in ((0, 64), (128, 192)):
        for gx in JOINTS:
            put(gx, gx + 2, WALK_TOP, WALK_TOP + 1, z0, z1, STONE)

    # --- baked lot debris — faithful port of ProceduralDebrisScatterer.
    #     ScatterSeeded (density 0.03), run per 64x64 pad in local space.
    #     Baked because the runtime scatterer only targets 'empty_land'
    #     assets — if it's ever pointed at alley pads, remove the bake or
    #     the count doubles. Seeds reuse the C# row/col hash so lots stay
    #     deterministic and distinct per pad ---
    for px0, pz0 in pads:
        row, col = pz0 // 64, px0 // 64
        seed = ((row * 73856093) ^ (col * 19349663) ^ (0 * 83492791)) & 0x7FFFFFFF
        scatter_pad(vox, px0, pz0, seed)

    # --- perimeter curb ring — 2v thick, OPEN at both lane mouths ---
    openings = [(0, 2, LANE_Z0, LANE_Z1), (W - 2, W, LANE_Z0, LANE_Z1)]
    def in_opening(x, z):
        return any(a <= x < b and c <= z < d for a, b, c, d in openings)
    for x in range(W):
        for z in (0, 1, D - 2, D - 1):
            if not in_opening(x, z): put(x, x + 1, CURB_Y, CURB_Y + 1, z, z + 1, STONE)
    for z in range(D):
        for x in (0, 1, W - 2, W - 1):
            if not in_opening(x, z): put(x, x + 1, CURB_Y, CURB_Y + 1, z, z + 1, STONE)

    # --- attachment points (real surface voxels → centroids) ---
    def surface(x, z, w2, d2, y):
        return [P(xx, y, zz) for xx in range(x, x + w2) for zz in range(z, z + d2)]

    en_a, en_b = ("w", "e") if _axis == "ew" else ("n", "s")
    painted = {
        f"alley_entry_{en_a}": surface(2,   92, 4, 4, ROAD_TOP),   # on the cobble
        f"alley_entry_{en_b}": surface(186, 92, 4, 4, ROAD_TOP),
        "alley_center":  surface(94,  92, 4, 4, ROAD_TOP),
        "prop_slot_0":   surface(46,  70, 4, 4, WALK_TOP),   # raised lane walks
        "prop_slot_1":   surface(142, 70, 4, 4, WALK_TOP),
        "prop_slot_2":   surface(46,  118, 4, 4, WALK_TOP),
        "prop_slot_3":   surface(142, 118, 4, 4, WALK_TOP),
    }

    PART_DEFS = [
        {"id": 0, "name": "Body",        "key": "body",        "color": "#888888", "desc": "Unassigned"},
        {"id": 1, "name": "Alley Entry", "key": "alley_entry", "color": "#dd8833", "desc": "Lane mouth — mobster path entry/exit point"},
        {"id": 2, "name": "Alley Mid",   "key": "alley_mid",   "color": "#ddcc33", "desc": "Lane center — choke/ambush reference"},
        {"id": 3, "name": "Prop Slot",   "key": "prop_slot",   "color": "#33cc55", "desc": "Cover prop placement (crate/ash bin/dumpster)"},
    ]
    part_ids = {d["key"]: d["id"] for d in PART_DEFS}
    key_of = {f"alley_entry_{en_a}": "alley_entry", f"alley_entry_{en_b}": "alley_entry",
              "alley_center": "alley_mid",
              "prop_slot_0": "prop_slot", "prop_slot_1": "prop_slot",
              "prop_slot_2": "prop_slot", "prop_slot_3": "prop_slot"}

    def centroid(pts):
        n = float(len(pts))
        return {k: round(sum(p[i] for p in pts) / n, 3) for i, k in enumerate("xyz")}

    attachment_points = {k: centroid(v) for k, v in painted.items()}
    item_parts = {f"{x},{y},{z}": part_ids[key_of[k]]
                  for k, pts in painted.items() for x, y, z in pts}
    return attachment_points, item_parts, PART_DEFS

def mat(i, name, r, g, b):
    return {"id": i, "name": name, "r": r, "g": g, "b": b,
            "hex": '#%02x%02x%02x' % (r, g, b), "category": "building"}

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--axis", choices=("ew", "ns"), default="ew",
                    help="lane axis: ew = E-W lane / mid slot ROW (default), ns = N-S / mid COLUMN")
    args = ap.parse_args()

    global _axis
    _axis = args.axis
    aps, parts, defs = build()
    name = "alley_block" if args.axis == "ew" else "alley_block_ns"
    data = {
        "name": name,
        "dims": [W, H, D],
        "assetType": "building",
        "voxelSize": 0.05,
        "materials": [
            mat(101, "Stone", 122, 107, 87), mat(102, "Sidewalk", 150, 145, 138),
            mat(104, "Asphalt", 46, 46, 51), mat(105, "Cobblestone", 107, 97, 87),
            mat(106, "Dark Wood", 77, 46, 26), mat(108, "Weathered Wood", 107, 92, 66),
            mat(110, "Aged Metal", 107, 102, 92), mat(118, "Tar", 71, 61, 51),
            mat(120, "Painted Red", 115, 31, 26), mat(121, "Painted Green", 38, 71, 46),
            mat(122, "Painted Brown", 56, 31, 20),
        ],
        "voxels": [[x, y, z, m] for (x, y, z), m in sorted(vox.items())],
        "groups": {}, "regions": {},
        "attachmentPoints": aps,
        "itemParts": parts, "itemPartDefs": defs,
        "format": "steelcity_stasset", "version": 1,
    }
    out = f"VoxelAssetStudio/JSON Models In Progress/{name}.json"
    with open(out, "w", encoding="utf-8") as f:
        json.dump(data, f)
    entries = {k: v for k, v in aps.items() if k.startswith("alley_entry")}
    print(f"Wrote {out}: {len(data['voxels'])} voxels, {W}x{H}x{D} @ 0.05m, axis={args.axis}")
    print(f"  entries: {entries} mid={aps['alley_center']}")

if __name__ == "__main__":
    main()
