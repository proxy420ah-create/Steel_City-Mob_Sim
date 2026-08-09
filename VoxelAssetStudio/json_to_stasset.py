"""Convert exported voxel JSON from the web editor back to .stasset format.
Usage: python json_to_stasset.py <input.json> [output.stasset]
"""
import sys
import json
import numpy as np
from stasset_io import save_stasset

def json_to_stasset(json_path, stasset_path=None):
    with open(json_path, 'r') as f:
        data = json.load(f)
    
    w, h, d = data['dims']
    voxels = np.zeros((w, h, d), dtype=np.uint16)
    
    for entry in data['voxels']:
        x, y, z, mid = entry
        if 0 <= x < w and 0 <= y < h and 0 <= z < d:
            voxels[x, y, z] = mid
    
    if stasset_path is None:
        stasset_path = json_path.replace('.json', '.stasset')
    
    meta = {
        'type': 'edited_component',
        'note': 'Edited in web voxel editor',
        'dims': [w, h, d]
    }
    
    save_stasset(stasset_path, voxels, building_meta=meta)
    print(f"Saved {stasset_path}")
    print(f"  Dims: {w}x{h}x{d} | Voxels: {np.count_nonzero(voxels)}")

if __name__ == "__main__":
    infile = sys.argv[1] if len(sys.argv) > 1 else "voxel_export.json"
    outfile = sys.argv[2] if len(sys.argv) > 2 else None
    json_to_stasset(infile, outfile)
