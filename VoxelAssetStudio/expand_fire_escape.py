"""Expand fire_escape_example1.stasset: add 2 voxels to the OUTSIDE (z=0 side)
of the entire model, making it 6 voxels deep total (was 4).

Shifts original z=0..4 to z=2..6, then fills z=0,1 by mirroring the pattern
from the outermost original layers. Applied uniformly to ALL layers.
"""
import numpy as np
from stasset_io import load_stasset_full, save_stasset

DARK_IRON = 109

# Load the example
voxels, dims, scale, meta = load_stasset_full("fire_escape_example1.stasset")
w, h, d = dims
print(f"Original: {w}x{h}x{d}")

# Expand Z by 2 on the OUTSIDE (z=0, z=1 are new, original shifts to z=2..6)
new_d = d + 2
new_voxels = np.zeros((w, h, new_d), dtype=np.uint16)
new_voxels[:, :, 2:] = voxels  # original z=0..4 → new z=2..6

# Fill z=0,1 for every Y layer by copying from the outermost SOLID layer
# Original z=4 (now at z=6) is the outermost solid face for most layers.
# Copy z=6 pattern into z=0 and z=1 for uniform 2-voxel outside extension.
for y in range(h):
    for z_new in [0, 1]:
        for x in range(w):
            if new_voxels[x, y, 6] != 0:
                new_voxels[x, y, z_new] = DARK_IRON

# Print the modified top layers for verification
print(f"\nNew dims: {w}x{h}x{new_d}")
print(f"Non-air voxels: {np.count_nonzero(new_voxels)} (was {np.count_nonzero(voxels)})")

for y in range(h-1, 38, -1):
    layer = new_voxels[:, y, :]
    non_air = np.count_nonzero(layer)
    if non_air == 0:
        continue
    print(f"\nY={y}: {non_air} voxels")
    for z in range(new_d):
        row = ''
        for x in range(w):
            m = int(new_voxels[x, y, z])
            if m == 0:
                row += ' . '
            else:
                row += f'{m:3d}'
        print(f'  z={z:2d}: {row}')

# Also show a mid-level landing
for y in [35, 28]:
    layer = new_voxels[:, y, :]
    non_air = np.count_nonzero(layer)
    if non_air == 0:
        continue
    print(f"\nY={y}: {non_air} voxels")
    for z in range(new_d):
        row = ''
        for x in range(w):
            m = int(new_voxels[x, y, z])
            if m == 0:
                row += ' . '
            else:
                row += f'{m:3d}'
        print(f'  z={z:2d}: {row}')

# Save
save_stasset("fire_escape_example1_expanded.stasset", new_voxels, building_meta={
    "type": "fire_escape_component",
    "note": "Expanded outside by 2v (was 4v deep, now 6v). Uniform across all layers.",
    "original_dims": list(dims),
    "new_dims": [w, h, new_d],
})
print("\n✅ Saved fire_escape_example1_expanded.stasset")
