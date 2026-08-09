"""Add a second fire escape on the left wall (high X), near the front-left corner.

Original FE (mirrored, back wall): X=10-36 (width), Z=87-93 (protrusion into +Z)
New FE (left wall): X=88-94 (protrusion into +X), Z=10-36 (width along left wall)

Rotation mapping (90° CCW around Y):
  new_x = old_z + 1    (87→88 wall face, 93→94 outermost)
  new_z = old_x        (10→10 near front, 36→36 further back)
  new_y = old_y        (unchanged)

Only copy FE voxels (DARK_IRON=109, PAINTED_METAL=111).
Don't overwrite existing voxels.
"""
import json
import numpy as np
from collections import Counter

with open(r'C:\Users\NADECC\Downloads\firework3_mirrored.json') as f:
    data = json.load(f)

w, h, d = data['dims']
DARK_IRON = 109
PAINTED_METAL = 111
FE_MATS = {DARK_IRON, PAINTED_METAL}

# Build grid for collision checking
grid = np.zeros((w, h, d), dtype=np.uint16)
for x, y, z, mid in data['voxels']:
    if 0 <= x < w and 0 <= y < h and 0 <= z < d:
        grid[x, y, z] = mid

# Collect FE voxels and rotate them onto the left wall
added = 0
new_fe_voxels = []
for x, y, z, mid in data['voxels']:
    if mid in FE_MATS:
        nx = z + 1   # protrusion into +X buffer (87→88, 93→94)
        nz = x        # width along Z (10→10, 36→36)
        ny = y        # same height
        if 0 <= nx < w and 0 <= ny < h and 0 <= nz < d:
            if grid[nx, ny, nz] == 0:  # don't overwrite building
                new_fe_voxels.append([nx, ny, nz, mid])
                grid[nx, ny, nz] = mid
                added += 1

print(f"Added {added} FE voxels on left wall (rotated 90°)")

# Combine original + new voxels
all_voxels = list(data['voxels']) + new_fe_voxels

# Verify both fire escapes
fe = [v for v in all_voxels if v[3] in FE_MATS]
fe_ys = [v[1] for v in fe]
yc = Counter(fe_ys)
avg = np.mean(list(yc.values()))
landings = sorted([y for y, c in yc.items() if c > avg * 1.5])
print(f"All landing Ys: {landings}")

# Show landing extents
for ly in landings:
    ly_voxels = [(v[0], v[2]) for v in fe if v[1] == ly]
    if not ly_voxels:
        continue
    xs = [v[0] for v in ly_voxels]
    zs = [v[1] for v in ly_voxels]
    # Split into back-wall (Z>=87) and left-wall (X>=88) groups
    back = [(x, z) for x, z in ly_voxels if z >= 87]
    left = [(x, z) for x, z in ly_voxels if x >= 88]
    if back:
        bxs = [v[0] for v in back]
        bzs = [v[1] for v in back]
        print(f"  Y={ly} back-wall:  X={min(bxs)}-{max(bxs)} Z={min(bzs)}-{max(bzs)} ({len(back)} voxels)")
    if left:
        lxs = [v[0] for v in left]
        lzs = [v[1] for v in left]
        print(f"  Y={ly} left-wall:  X={min(lxs)}-{max(lxs)} Z={min(lzs)}-{max(lzs)} ({len(left)} voxels)")

print(f"\nTotal voxels: {len(data['voxels'])} -> {len(all_voxels)}")

# Save
out_data = {
    'dims': data['dims'],
    'materials': data['materials'],
    'voxels': all_voxels
}
out_path = r'C:\Users\NADECC\Downloads\firework3_dual_fe.json'
with open(out_path, 'w') as f:
    json.dump(out_data, f)
print(f"Saved: {out_path}")

# Load into editor
import voxel_editor_html as veh
ROOF_BUF = 8
new_h = h + ROOF_BUF
big_grid = np.zeros((w, new_h, d), dtype=np.uint16)
for x, y, z, mid in all_voxels:
    if 0 <= x < w and 0 <= y < h and 0 <= z < d:
        big_grid[x, y, z] = mid
veh.voxel_to_editor(big_grid, (w, new_h, d), "voxel_editor.html",
                    title="Firework3 - Dual FE (Back-Right + Front-Left)")
print(f"Saved: voxel_editor.html ({w}x{new_h}x{d})")
