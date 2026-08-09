"""Convert .stasset + .groups binary files to JSON for the character animator.
Usage: python stasset_to_json.py <asset_name>
Example: python stasset_to_json.py character_hoodlum_0
"""
import sys
import json
import numpy as np
from stasset_io import load_stasset

def stasset_to_json(asset_name, base_dir="..\\Assets\\StreamingAssets\\voxel_buildings"):
    stasset_path = f"{base_dir}\\{asset_name}.stasset"
    groups_path = f"{base_dir}\\{asset_name}.groups"

    # Load voxel data
    voxels, dims, skeleton = load_stasset(stasset_path)
    w, h, d = dims

    # Extract non-air voxels as [x, y, z, materialId]
    voxel_list = []
    for x in range(w):
        for y in range(h):
            for z in range(d):
                mid = int(voxels[x, y, z])
                if mid != 0:
                    voxel_list.append([x, y, z, mid])

    # Load groups if present
    group_list = []
    try:
        with open(groups_path, 'rb') as f:
            magic = f.read(4)
            if magic == b'STAG':
                f.read(2)  # version + flags
                gw = int.from_bytes(f.read(2), 'little')
                gh = int.from_bytes(f.read(2), 'little')
                gd = int.from_bytes(f.read(2), 'little')
                f.read(4)  # reserved
                gtotal = gw * gh * gd
                for i in range(gtotal):
                    gid = int.from_bytes(f.read(2), 'little')
                    if gid != 0:
                        # Convert linear index to xyz (X-major / Fortran order)
                        x = i % gw
                        y = (i // gw) % gh
                        z = i // (gw * gh)
                        group_list.append([x, y, z, gid])
                print(f"   Groups: {len(group_list)} non-zero assignments")
    except FileNotFoundError:
        print(f"   No .groups file found (all voxels default to group 0)")

    data = {
        'format': 'stasset_export',
        'dims': [w, h, d],
        'voxels': voxel_list,
        'groups': group_list,
    }

    out_path = f"{asset_name}.json"
    with open(out_path, 'w') as f:
        json.dump(data, f, indent=2)

    print(f"✅ Exported {out_path}")
    print(f"   Dims: {w}x{h}x{d} | Voxels: {len(voxel_list)} | Groups: {len(group_list)}")

if __name__ == "__main__":
    name = sys.argv[1] if len(sys.argv) > 1 else "character_hoodlum_0"
    stasset_to_json(name)
