"""Convert a consolidated steelcity_stasset JSON back to a binary .stasset
for production (Assets/StreamingAssets/voxel_buildings/).

Writes v2 format when the JSON carries attachmentPoints/building metadata so
the data survives in the SKEL tail (note: Unity's StAssetReader currently
reads voxels only — consuming the tail is pending runtime work).

Usage:
  python Tools/json_to_stasset.py <in.json> [out.stasset]
"""
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'VoxelAssetStudio'))
# stasset_io prints emoji; cp1252 consoles (Windows default) can't encode them
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
from stasset_io import save_stasset


def main():
    src = sys.argv[1]
    dst = sys.argv[2] if len(sys.argv) > 2 else os.path.splitext(src)[0] + '.stasset'

    d = json.load(open(src, encoding='utf-8'))
    w, h, dep = d['dims']
    grid = np.zeros((w, h, dep), dtype=np.uint16)
    for x, y, z, mid in d['voxels']:
        grid[x, y, z] = mid

    building_meta = {}
    if d.get('attachmentPoints'):
        building_meta['attachmentPoints'] = d['attachmentPoints']
    if d.get('name'):
        building_meta['name'] = d['name']

    save_stasset(dst, grid, building_meta=building_meta or None)
    if building_meta:
        print(f'   metadata tail: {list(building_meta.keys())}')


if __name__ == '__main__':
    main()
