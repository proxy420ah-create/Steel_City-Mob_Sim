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
    if d.get('name'):
        building_meta['name'] = d['name']
    # StAssetReader.LoadAttachmentPoints regex-scans the SKEL payload for an
    # "attachmentPoints" object — it must live under building_meta, which
    # save_stasset emits as payload['building'].
    if d.get('attachmentPoints'):
        building_meta['attachmentPoints'] = {
            n: (p if isinstance(p, dict) else {'x': p[0], 'y': p[1], 'z': p[2]})
            for n, p in d['attachmentPoints'].items()}
    # Painted part regions + defs ride along so stasset_to_json round-trips them.
    for k in ('itemParts', 'itemPartDefs'):
        if d.get(k):
            building_meta[k] = d[k]

    skeleton = None
    if d.get('attachmentPoints'):
        # Match gen_vehicle_490.py: named points go in payload['attachments']
        skeleton = {'attachments': [
            {'name': n, 'position': p if isinstance(p, list) else [p['x'], p['y'], p['z']]}
            for n, p in d['attachmentPoints'].items()]}

    save_stasset(dst, grid, skeleton=skeleton, building_meta=building_meta or None)
    if building_meta:
        print(f'   metadata tail: {list(building_meta.keys())}')


if __name__ == '__main__':
    main()
