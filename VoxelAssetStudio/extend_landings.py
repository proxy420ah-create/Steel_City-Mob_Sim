"""Extend fire escape landings in firework2.json to cover 2 windows each.

Current landings: X=67-85 (covers window 5 at X=73-75)
Target landings:  X=59-85 (covers window 4 at X=62-64 AND window 5 at X=73-75)

Strategy: For each landing Y level, copy the leftmost 8 columns (X=67-74)
and paste them shifted -8 in X (to X=59-66), preserving the railing/platform
structure. Also extend any vertical supports and stair connections.
"""
import json
import numpy as np
from collections import Counter

with open(r'C:\Users\NADECC\Downloads\firework2.json') as f:
    data = json.load(f)

w, h, d = data['dims']
grid = np.zeros((w, h, d), dtype=np.uint16)
for x, y, z, mid in data['voxels']:
    if 0 <= x < w and 0 <= y < h and 0 <= z < d:
        grid[x, y, z] = mid

DARK_IRON = 109
PAINTED_METAL = 111
FE_MATS = {DARK_IRON, PAINTED_METAL}

# Landing Y levels - actual fire escape platform levels
# (Y=9,17,25,33 are cornice/floor levels with 600+ voxels building-wide)
# (Y=10,18,26,34 are the FE platform levels with ~110 voxels at X=67-85)
landing_ys = [10, 18, 26, 34]

# For each landing, extend 8 voxels to the left
# Copy X=67-74 structure to X=59-66
EXTEND = 8
SRC_X_START = 67
SRC_X_END = 75  # exclusive
DST_X_START = SRC_X_START - EXTEND  # 59

added = 0
for ly in landing_ys:
    # Copy the landing slice at this Y level
    for x in range(SRC_X_START, SRC_X_END):
        dx = x - EXTEND
        if dx < 0:
            continue
        for z in range(d):
            mid = int(grid[x, ly, z])
            if mid in FE_MATS:
                # Only place if destination is empty (don't overwrite building)
                if grid[dx, ly, z] == 0:
                    grid[dx, ly, z] = mid
                    added += 1

print(f"Extended landing platforms: +{added} voxels")

# Also extend railings - the vertical posts above/below landing edges
# Railings are typically 1-2 voxels above the landing platform
for ly in landing_ys:
    for x in range(SRC_X_START, SRC_X_END):
        dx = x - EXTEND
        if dx < 0:
            continue
        for z in range(d):
            # Check for railing posts above landing (up to 4 voxels above)
            for dy in range(1, 5):
                y = ly + dy
                if y >= h:
                    break
                mid = int(grid[x, y, z])
                if mid in FE_MATS:
                    if grid[dx, y, z] == 0:
                        grid[dx, y, z] = mid
                        added += 1
                else:
                    break  # railing is contiguous

print(f"After railings: +{added} total voxels added")

# Extend the leftmost vertical support posts down to ground
# The current leftmost support is at X=67. Add one at X=59.
for ly in landing_ys:
    for z in range(d):
        if int(grid[67, ly, z]) in FE_MATS:
            # Add support post from ground to this landing
            for y in range(0, ly):
                if grid[59, y, z] == 0:
                    grid[59, y, z] = DARK_IRON
                    added += 1
            break  # only one post per landing level

print(f"After support posts: +{added} total voxels added")

# Verify the new landing coverage
for ly in landing_ys:
    fe_at_ly = [(x, z) for x in range(w) for z in range(d) if int(grid[x, ly, z]) in FE_MATS]
    if fe_at_ly:
        xs = [v[0] for v in fe_at_ly]
        zs = [v[1] for v in fe_at_ly]
        print(f"  Landing Y={ly}: X={min(xs)}-{max(xs)} (width={max(xs)-min(xs)+1}) Z={min(zs)}-{max(zs)}")

# Check window coverage
windows = [(62, 64), (73, 75)]  # windows 4 and 5
for ly in landing_ys:
    fe_xs = set(x for x in range(w) for z in range(d) if int(grid[x, ly, z]) in FE_MATS)
    for wx_start, wx_end in windows:
        covered = all(x in fe_xs for x in range(wx_start, wx_end + 1))
        print(f"  Landing Y={ly} covers window X={wx_start}-{wx_end}: {covered}")

# Convert back to voxel list
new_voxels = []
for x in range(w):
    for y in range(h):
        for z in range(d):
            if grid[x, y, z] != 0:
                new_voxels.append([x, y, z, int(grid[x, y, z])])

print(f"\nTotal voxels: {len(data['voxels'])} -> {len(new_voxels)}")

# Save extended JSON
out_data = {
    'dims': data['dims'],
    'materials': data['materials'],
    'voxels': new_voxels
}
out_path = r'C:\Users\NADECC\Downloads\firework2_extended.json'
with open(out_path, 'w') as f:
    json.dump(out_data, f)
print(f"Saved: {out_path}")

# Load into editor with roof buffer
import voxel_editor_html as veh
ROOF_BUF = 8
new_h = h + ROOF_BUF
big_grid = np.zeros((w, new_h, d), dtype=np.uint16)
for x, y, z, mid in new_voxels:
    if 0 <= x < w and 0 <= y < h and 0 <= z < d:
        big_grid[x, y, z] = mid

veh.voxel_to_editor(big_grid, (w, new_h, d), "voxel_editor.html",
                    title="Firework2 - Extended Landings (2 windows)")
print(f"Saved: voxel_editor.html ({w}x{new_h}x{d})")
