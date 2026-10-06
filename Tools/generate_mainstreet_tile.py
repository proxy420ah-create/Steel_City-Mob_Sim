#!/usr/bin/env python3
"""Generate the 'mainstreet' prototype tile for the voxel editor.

Spec: docs/systems/RIVER_AND_BRIDGE_TILES.md + VoxelTerrainBuilder.FillMainstreetTile

One block-interior tile (11.6m x 11.6m at voxelSize 0.1 => 116x116), the
canonical block-wide boulevard:

    - concrete curb ring, sidewalkWidth = 1.0m (10 voxels)
    - asphalt interior (the whole block is road surface)
    - paired cobblestone trolley tracks running down the avenue axis,
      two rails at +/-0.35m off the block centerline

    y:   24..63 headroom above grade — room for lamps/signage
         22..23 grade slab (same convention as river_straight)
         0..21  empty below grade

Axis: 'ns' = tracks run along Z (avenue N-S), 'ew' = tracks along X.
Rotation covers the other orientation; both emitted for convenience.

Usage:
    python Tools/generate_mainstreet_tile.py            # writes mainstreet_ns.json
    python Tools/generate_mainstreet_tile.py --axis ew  # writes mainstreet_ew.json
"""

import argparse
import json
import os

MAT_COBBLESTONE = 105
MAT_ASPHALT = 104
MAT_SIDEWALK = 102

VOXEL_SIZE = 0.1
TILE_M = 11.6          # groundTileSize — block interior
CURB_M = 1.0           # sidewalkWidth (CityMap3D default)
GRADE_Y = 22           # 2-voxel grade slab at y 22..23
TRACK_OFF_M = 0.35     # rail offset from centerline (FillMainstreetTile.trackOff)


def build(axis='ns', headroom_m=4.0):
    w = d = round(TILE_M / VOXEL_SIZE)          # 116
    sw = round(CURB_M / VOXEL_SIZE)             # 10-voxel curb ring
    h = GRADE_Y + 2 + round(headroom_m / VOXEL_SIZE)
    c = w // 2                                  # block center index
    off = round(TRACK_OFF_M / VOXEL_SIZE)       # 3-4 voxel rail offset

    voxels = []
    add = voxels.append
    on_track = set()
    for i in (c - off, c - off - 1, c + off, c + off - 1):
        on_track.add(i)

    for z in range(d):
        for x in range(w):
            on_curb = x < sw or x >= w - sw or z < sw or z >= d - sw
            if on_curb:
                mat = MAT_SIDEWALK
            else:
                rail = x in on_track if axis == 'ns' else z in on_track
                mat = MAT_COBBLESTONE if rail else MAT_ASPHALT
            add((x, GRADE_Y, z, mat))
            add((x, GRADE_Y + 1, z, mat))

    return w, h, d, voxels


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--axis', choices=['ns', 'ew'], default='ns')
    ap.add_argument('--headroom', type=float, default=4.0,
                    help='empty volume above grade for lamps/signage (m)')
    ap.add_argument('--out', default=None)
    args = ap.parse_args()

    w, h, d, voxels = build(axis=args.axis, headroom_m=args.headroom)

    out = args.out or os.path.join(
        os.path.dirname(__file__), '..', 'VoxelAssetStudio',
        'JSON Models In Progress', f'mainstreet_{args.axis}.json')

    model = {
        'format': 'steelcity_stasset',
        'version': 1,
        'name': f'mainstreet_{args.axis}',
        'assetType': 'building',
        'voxelSize': VOXEL_SIZE,
        'dims': [w, h, d],
        'voxels': voxels,
        'materials': [
            {'id': MAT_SIDEWALK, 'name': 'Concrete', 'r': 148, 'g': 148, 'b': 138,
             'hex': '#94948A', 'category': 'building'},
            {'id': MAT_ASPHALT, 'name': 'Asphalt', 'r': 52, 'g': 52, 'b': 58,
             'hex': '#34343A', 'category': 'terrain'},
            {'id': MAT_COBBLESTONE, 'name': 'Cobblestone', 'r': 96, 'g': 90, 'b': 84,
             'hex': '#605A54', 'category': 'terrain'},
        ],
        'groups': [],
    }

    out = os.path.normpath(out)
    with open(out, 'w', encoding='utf-8') as f:
        json.dump(model, f, separators=(',', ':'))

    by_mat = {}
    for v in voxels:
        by_mat[v[3]] = by_mat.get(v[3], 0) + 1
    print(f'wrote {out}')
    print(f'dims {w}x{h}x{d} @ {VOXEL_SIZE}m = {w*VOXEL_SIZE:.1f}m x {h*VOXEL_SIZE:.1f}m x {d*VOXEL_SIZE:.1f}m')
    print(f'{len(voxels):,} voxels: ' + ', '.join(f'mat {k}={v:,}' for k, v in sorted(by_mat.items())))


if __name__ == '__main__':
    main()
