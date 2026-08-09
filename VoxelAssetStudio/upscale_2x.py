"""Upscale firework3_roof.json by 2x in every axis.

Each voxel at (x,y,z) becomes 8 voxels at (2x, 2y, 2z) through (2x+1, 2y+1, 2z+1).
Dims: 96x60x96 → 192x120x192
World footprint unchanged when voxelSize goes from 0.1 to 0.05.
"""
import json
import numpy as np

with open(r'C:\Users\NADECC\Downloads\firework3_roof.json') as f:
    data = json.load(f)

w, h, d = data['dims']
print(f"Original: {w}x{h}x{d}, {len(data['voxels'])} voxels")

# New dims
nw, nh, nd = w * 2, h * 2, d * 2
print(f"Upscaled: {nw}x{nh}x{nd}")

# Build original grid
grid = np.zeros((w, h, d), dtype=np.uint16)
for x, y, z, mid in data['voxels']:
    if 0 <= x < w and 0 <= y < h and 0 <= z < d:
        grid[x, y, z] = mid

# Upscale by repeating each voxel into 2x2x2
big_grid = np.repeat(np.repeat(np.repeat(grid, 2, axis=0), 2, axis=1), 2, axis=2)

print(f"Upscaled grid: {big_grid.shape}, {np.count_nonzero(big_grid)} non-air voxels")

# Convert back to voxel list
new_voxels = []
for x in range(nw):
    for y in range(nh):
        for z in range(nd):
            mid = int(big_grid[x, y, z])
            if mid != 0:
                new_voxels.append([x, y, z, mid])

print(f"New voxel list: {len(new_voxels)} entries")

# Save
out_data = {
    'dims': [nw, nh, nd],
    'materials': data['materials'],
    'voxels': new_voxels
}
out_path = r'C:\Users\NADECC\Downloads\firework3_roof_2x.json'
with open(out_path, 'w') as f:
    json.dump(out_data, f)
print(f"Saved: {out_path}")

# Convert to stasset
from stasset_io import save_stasset
voxels = np.zeros((nw, nh, nd), dtype=np.uint16)
for x, y, z, mid in new_voxels:
    voxels[x, y, z] = mid

meta = {
    'type': 'edited_component',
    'note': '2x upscaled for thin-feature visibility',
    'dims': [nw, nh, nd]
}
stasset_path = r'..\Assets\StreamingAssets\voxel_buildings\tenement_block_0.stasset'
save_stasset(stasset_path, voxels, building_meta=meta)
print(f"Saved stasset: {stasset_path}")
print(f"  Dims: {nw}x{nh}x{nd} | Voxels: {np.count_nonzero(voxels)}")

# Also generate editor preview
import voxel_editor_html as veh
veh.voxel_to_editor(big_grid, (nw, nh, nd), "voxel_editor_2x.html",
                    title="Firework3 Roof 2x (192x120x192)")
print(f"Saved: voxel_editor_2x.html")
