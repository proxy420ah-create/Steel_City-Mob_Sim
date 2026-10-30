# gen_alley_lane.py — emit `alley_lane.json` — the INVISIBLE slot marker for
# the composable alley block — consolidated steelcity_stasset format,
# 64x8x64 voxels @ 0.05 m (one building sub-slot, empty_land dims).
#
# THE COMPOSABLE ALLEY BLOCK: a normal 9-slot city block whose middle slot
# row carries three of these markers instead of empty_land. Array order in
# city_layout.json buildings[] drives sub-grid placement (slot field unused):
#   [empty, empty, empty, lane, lane, lane, empty, empty, empty]  → EW lane
#   lane at array idx 1/4/7                                      → NS lane
#
# THE LANE IS PURE TERRAIN: VoxelTerrainBuilder.laneMask (from the block's
# `laneOpenings` flag) paints the full cross-section at grade — sidewalk 16v
# | asphalt road 32v | sidewalk 16v — plus the mouth curb cuts through the
# sidewalk ring. Flush with every other sidewalk/street in the city (+0.1).
# This asset contributes ZERO voxels: it exists only to hold the middle
# slot positions in the buildings[] grid and to carry the lane's anchor
# metadata (entry/mid/prop_slot points at road grade) for the future
# waypoint/prop systems.
#
# --axis ns emits the transposed N-S variant → alley_lane_ns.json.

import argparse
import json

W, H, D = 64, 8, 64
_axis = 'ew'

def Pt(x, y, z):
    """Marker point; transposes XZ for the ns variant."""
    return {"x": z, "y": y, "z": x} if _axis == 'ns' else {"x": x, "y": y, "z": z}

def build():
    # Zero voxels — the terrain lane strip renders the whole thing.
    vox = {}

    # Bare attachment points at road grade (y=0 = terrain surface +0.1).
    aps = {
        "alley_entry_a": Pt(3.5, 0, 31.5),   # mouth A — ped entry/exit
        "alley_entry_b": Pt(59.5, 0, 31.5),  # mouth B (block's two outermost = block entries)
        "alley_mid": Pt(31.5, 0, 31.5),      # lane center — choke/ambush reference
        "prop_slot_0": Pt(15.5, 0, 7.5),     # cover props on the sidewalk flanks
        "prop_slot_1": Pt(47.5, 0, 55.5),
    }

    PART_DEFS = [
        {"id": 0, "name": "Body", "key": "body", "color": "#888888", "desc": "Unassigned"},
        {"id": 1, "name": "Alley Entry", "key": "alley_entry", "color": "#dd8833",
         "desc": "Lane mouth — mobster path entry/exit (use the block's two outermost)"},
        {"id": 2, "name": "Alley Mid", "key": "alley_mid", "color": "#ddcc33",
         "desc": "Lane center — choke/ambush reference"},
        {"id": 3, "name": "Prop Slot", "key": "prop_slot", "color": "#33cc55",
         "desc": "Cover prop placement (crate/ash bin/dumpster)"},
    ]
    return aps, {}, PART_DEFS

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
        "voxels": [],
        "groups": {}, "regions": {},
        "attachmentPoints": aps,
        "itemParts": parts, "itemPartDefs": defs,
        "format": "steelcity_stasset", "version": 1,
    }
    out = f"VoxelAssetStudio/JSON Models In Progress/{name}.json"
    with open(out, "w", encoding="utf-8") as f:
        json.dump(data, f)
    entries = {k: v for k, v in aps.items() if k.startswith("alley_entry")}
    print(f"Wrote {out}: air marker (0 voxels), {W}x{H}x{D} @ 0.05m, axis={args.axis}")
    print(f"  entries: {entries} mid={aps['alley_mid']}")

if __name__ == "__main__":
    main()
