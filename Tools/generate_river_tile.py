#!/usr/bin/env python3
"""Generate the 'river_straight' prototype tile for the voxel editor.

Spec: docs/systems/RIVER_AND_BRIDGE_TILES.md

One block-interior tile (11.6m x 11.6m at voxelSize 0.1 => 116x116), carved
channel down the middle with flanking sidewalks at street grade:

    x:   0..9          10        11..104       105        106..115
         sidewalk    quay wall   channel      quay wall   sidewalk

    y:   24..63 headroom above grade — empty in the base tile; room for
                fences/lamps/decor painted on the sidewalk surface
         22..23 grade slab (concrete rim over walls)
         20..21 air gap (visible drop to water)
         6..19  water (MAT_WATER 137), 1.4m deep
         4..5   stone bed
         0..3   void below bed

Flow axis = Z (N-S straight). Channel open at z=0 and z=D-1 so adjacent
river tiles continue the channel.

Usage:
    python Tools/generate_river_tile.py            # writes JSON Models In Progress/river_straight.json
    python Tools/generate_river_tile.py --sidewalk 1.2 --headroom 6.0
"""

import argparse
import json
import os

MAT_STONE = 101
MAT_SIDEWALK = 102
MAT_WATER = 137  # new palette id (requires MaterialCount/MaxMaterials >= 138)

VOXEL_SIZE = 0.1          # building/item resolution (terrain is 0.05; halved for editability)
TILE_M = 11.6             # groundTileSize — block interior
BED_TOP = 6               # bed occupies y 4..5
GRADE_Y = 22              # 2-voxel grade slab at y 22..23 (terrain convention)


def build(sidewalk_m=1.0, water_top_y=19, headroom_m=4.0):
    w = d = round(TILE_M / VOXEL_SIZE)  # 116
    sw = round(sidewalk_m / VOXEL_SIZE)  # flanking sidewalk width in voxels
    wall_l, wall_r = sw, w - sw - 1    # quay wall columns
    ch_l, ch_r = wall_l + 1, wall_r - 1  # channel interior

    voxels = []
    add = voxels.append

    for z in range(d):
        for x in range(w):
            on_wall = x == wall_l or x == wall_r
            in_channel = ch_l <= x <= ch_r
            on_sidewalk = not on_wall and not in_channel

            # grade slab: sidewalk strips + concrete rim over quay walls
            if on_sidewalk or on_wall:
                add((x, GRADE_Y, z, MAT_SIDEWALK))
                add((x, GRADE_Y + 1, z, MAT_SIDEWALK))

            if on_wall:
                for y in range(BED_TOP - 2, GRADE_Y):  # y 4..21 — full-depth quay face
                    add((x, y, z, MAT_STONE))
            elif in_channel:
                add((x, BED_TOP - 2, z, MAT_STONE))  # bed y 4..5
                add((x, BED_TOP - 1, z, MAT_STONE))
                for y in range(BED_TOP, water_top_y + 1):  # water fill
                    add((x, y, z, MAT_WATER))

    return w, GRADE_Y + 2 + round(headroom_m / VOXEL_SIZE), d, voxels


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--sidewalk', type=float, default=1.0, help='flanking sidewalk width (m)')
    ap.add_argument('--headroom', type=float, default=4.0,
                    help='empty volume above grade for fences/decor (m)')
    ap.add_argument('--out', default=os.path.join(
        os.path.dirname(__file__), '..', 'VoxelAssetStudio',
        'JSON Models In Progress', 'river_straight.json'))
    args = ap.parse_args()

    w, h, d, voxels = build(sidewalk_m=args.sidewalk, headroom_m=args.headroom)

    model = {
        'format': 'steelcity_stasset',
        'version': 1,
        'name': 'river_straight',
        'assetType': 'building',
        'voxelSize': VOXEL_SIZE,
        'dims': [w, h, d],
        'voxels': voxels,
        'materials': [
            {'id': MAT_STONE, 'name': 'Stone', 'r': 122, 'g': 107, 'b': 87,
             'hex': '#7A6B57', 'category': 'building'},
            {'id': MAT_SIDEWALK, 'name': 'Concrete', 'r': 148, 'g': 148, 'b': 138,
             'hex': '#94948A', 'category': 'building'},
            {'id': MAT_WATER, 'name': 'Water', 'r': 26, 'g': 64, 'b': 115,
             'hex': '#1A4073', 'category': 'terrain'},
        ],
        'groups': [],
    }

    out = os.path.normpath(args.out)
    with open(out, 'w', encoding='utf-8') as f:
        json.dump(model, f, separators=(',', ':'))

    by_mat = {}
    for v in voxels:
        by_mat[v[3]] = by_mat.get(v[3], 0) + 1
    print(f'wrote {out}')
    print(f'dims {w}x{h}x{d} @ {VOXEL_SIZE}m = {w*VOXEL_SIZE:.1f}m x {h*VOXEL_SIZE:.1f}m x {d*VOXEL_SIZE:.1f}m')
    print(f'{len(voxels):,} voxels: ' + ', '.join(f'mat {k}={v:,}' for k, v in sorted(by_mat.items())))
    print(f'file size {os.path.getsize(out)/1024/1024:.1f} MB')


if __name__ == '__main__':
    main()
