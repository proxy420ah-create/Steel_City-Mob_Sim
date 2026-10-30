# gen_alley_lane.py — emit `alley_lane.json` — the slot-sized pedestrian
# service lane for VoxelAssetStudio — consolidated steelcity_stasset format,
# 64x8x64 voxels @ 0.05 m (one building sub-slot, matching empty_land dims).
#
# THE COMPOSABLE ALLEY BLOCK: a normal 9-slot city block whose middle slot
# row carries three of these segments instead of empty_land. Array order in
# city_layout.json buildings[] drives sub-grid placement (slot field unused):
#   [empty, empty, empty, lane, lane, lane, empty, empty, empty]  → EW lane
#   lane at array idx 1/4/7                                      → NS lane
#
# Why slots, not a monolith: SectorBaker detects "empty_land" by stasset path
# → pooled ProceduralDebrisScatterer variants (F6 toggle, shared buffer
# regions), and future buildings drop into the six lots by replacing a
# buildings[] entry. A full-block slot:0 asset walls off both.
#
# Cross-section (spec space = E-W lane along X; 64v deep):
#   z 0..15   lane sidewalk — raised 1v over road (curb step, surface y3)
#   z 16..47  alley road — asphalt, 32v wide (1.6 m), surface y2 = street grade
#   z 48..63  lane sidewalk — raised
# Same profile the terrain `laneOpenings` gap expects (center 32v of the
# interior edge). Cars get no corridor — the lane is block-interior and
# RoadGraph only reads seams. Ped-only for now; one-way flag is future work.
#
# --axis ns emits the transposed N-S variant → alley_lane_ns.json.

import argparse
import json
import random

W, H, D = 64, 8, 64
STONE, SIDEWALK, ASPHALT, COBBLE = 101, 102, 104, 105

ROAD_TOP, WALK_TOP = 1, 2        # road slab top / raised walk top (indices)
ROAD_Z0, ROAD_Z1 = 16, 48        # asphalt band (32v)
SW_A0, SW_A1 = 0, 16             # walk band A (low-z flank)
SW_B0, SW_B1 = 48, 64            # walk band B (high-z flank)

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

def build():
    vox.clear()

    # --- base slab — SIDEWALK under everything (walkable family) ---
    put(0, W, 0, ROAD_TOP + 1, 0, D, SIDEWALK)

    # --- alley road — asphalt at grade (surface y2 = street/lot grade) ---
    put(0, W, 0, ROAD_TOP + 1, ROAD_Z0, ROAD_Z1, ASPHALT)

    # --- lane sidewalks — raised 1v curb step over the road ---
    put(0, W, ROAD_TOP + 1, WALK_TOP + 1, SW_A0, SW_A1, SIDEWALK)
    put(0, W, ROAD_TOP + 1, WALK_TOP + 1, SW_B0, SW_B1, SIDEWALK)

    # --- painted anchors ---
    painted = {}
    def paint(key, x0, x1, y0, y1, z0, z1):
        pts = painted.setdefault(key, [])
        for x in range(x0, x1):
            for y in range(y0, y1):
                for z in range(z0, z1):
                    pts.append(P(x, y, z))
    # lane mouths at both ends (outermost two become block entries)
    paint("alley_entry_a", 2, 6, 1, 2, 28, 32)
    paint("alley_entry_b", W - 6, W - 2, 1, 2, 28, 32)
    # segment center — choke/ambush reference
    paint("alley_mid", W // 2 - 2, W // 2 + 2, 1, 2, 28, 32)
    # cover-prop markers on the raised walks
    paint("prop_slot_0", 14, 18, WALK_TOP, WALK_TOP + 1, 6, 10)
    paint("prop_slot_1", W - 18, W - 14, WALK_TOP, WALK_TOP + 1, 54, 58)

    PART_DEFS = [
        {"id": 0, "name": "Body", "key": "body", "color": "#888888", "desc": "Unassigned"},
        {"id": 1, "name": "Alley Entry", "key": "alley_entry", "color": "#dd8833",
         "desc": "Lane mouth — mobster path entry/exit (use the block's two outermost)"},
        {"id": 2, "name": "Alley Mid", "key": "alley_mid", "color": "#ddcc33",
         "desc": "Lane center — choke/ambush reference"},
        {"id": 3, "name": "Prop Slot", "key": "prop_slot", "color": "#33cc55",
         "desc": "Cover prop placement (crate/ash bin/dumpster)"},
    ]
    key_of = {"alley_entry_a": "alley_entry", "alley_entry_b": "alley_entry",
              "alley_mid": "alley_mid",
              "prop_slot_0": "prop_slot", "prop_slot_1": "prop_slot"}
    part_ids = {"alley_entry": 1, "alley_mid": 2, "prop_slot": 3}

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
                    help="lane axis: ew = E-W lane (middle slot row, array idx 3-5), ns = N-S (idx 1/4/7)")
    args = ap.parse_args()

    global _axis
    _axis = args.axis
    aps, parts, defs = build()
    name = "alley_lane" if args.axis == "ew" else "alley_lane_ns"
    data = {
        "name": name,
        "dims": [W, H, D],
        "assetType": "building",
        "voxelSize": 0.05,
        "materials": [
            mat(102, "Sidewalk", 150, 145, 138), mat(104, "Asphalt", 46, 46, 51),
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
    print(f"  entries: {entries} mid={aps['alley_mid']}")

if __name__ == "__main__":
    main()
