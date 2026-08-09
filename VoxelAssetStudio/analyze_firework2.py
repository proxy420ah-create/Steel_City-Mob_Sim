"""Analyze firework2.json - landing dimensions, window coverage, drop ladders."""
import json
import numpy as np
from collections import Counter

with open(r'C:\Users\NADECC\Downloads\firework2.json') as f:
    data = json.load(f)

w, h, d = data['dims']
print(f"Dims: {w}x{h}x{d}")
print(f"Total voxels: {len(data['voxels'])}")
print(f"Materials: {len(data['materials'])}")

# Build grid
grid = np.zeros((w, h, d), dtype=np.uint16)
for x, y, z, mid in data['voxels']:
    if 0 <= x < w and 0 <= y < h and 0 <= z < d:
        grid[x, y, z] = mid

# Find DARK_IRON (109) and PAINTED_METAL (111) voxels - fire escape
DARK_IRON = 109
PAINTED_METAL = 111

fe_voxels = [(x, y, z, mid) for x, y, z, mid in data['voxels'] if mid in (DARK_IRON, PAINTED_METAL)]
print(f"\nFire escape voxels (iron+metal): {len(fe_voxels)}")

# Y distribution of FE voxels
fe_ys = [v[1] for v in fe_voxels]
y_counts = Counter(fe_ys)
print("\nFE Y-level distribution (count):")
for y in sorted(y_counts.keys()):
    print(f"  Y={y:2d}: {y_counts[y]:3d} voxels")

# Identify landing levels (high-count Y levels)
avg_count = np.mean(list(y_counts.values()))
landings = sorted([y for y, c in y_counts.items() if c > avg_count * 1.5])
print(f"\nLanding Y levels: {landings}")

# For each landing, find X and Z extents
for ly in landings:
    landing_voxels = [(x, y, z) for x, y, z, mid in fe_voxels if y == ly]
    if not landing_voxels:
        continue
    xs = [v[0] for v in landing_voxels]
    zs = [v[1] for v in landing_voxels]  # wait, z is index 2
    zs = [v[2] for v in landing_voxels]
    print(f"\n  Landing Y={ly}: {len(landing_voxels)} voxels")
    print(f"    X range: {min(xs)}-{max(xs)} (width={max(xs)-min(xs)+1})")
    print(f"    Z range: {min(zs)}-{max(zs)} (depth={max(zs)-min(zs)+1})")

# Check what's at the landing Y levels in the tenement walls (windows)
# Window sills: Y=12,20,28,36. Windows span Y=12-16, 20-24, 28-32, 34-38
# Check X positions of windows on the back wall (high Z)
print("\n=== Window positions on back wall (Z>=80) ===")
for y_range, label in [((12,16), "Floor0"), ((20,24), "Floor1"), ((28,32), "Floor2"), ((34,38), "Floor3")]:
    wall_voxels = []
    for x in range(w):
        for z in range(80, d):
            for y in range(y_range[0], y_range[1]+1):
                if grid[x, y, z] != 0 and grid[x, y, z] not in (DARK_IRON, PAINTED_METAL):
                    wall_voxels.append((x, y, z, grid[x, y, z]))
    if wall_voxels:
        xs = set(v[0] for v in wall_voxels)
        zs = set(v[2] for v in wall_voxels)
        mats = Counter(v[3] for v in wall_voxels)
        print(f"  {label} (Y={y_range[0]}-{y_range[1]}): X={sorted(xs)[:20]}... Z={sorted(zs)} mats={dict(mats)}")

# Now load into editor with roof buffer
import voxel_editor_html as veh
ROOF_BUF = 8
new_h = h + ROOF_BUF
big_grid = np.zeros((w, new_h, d), dtype=np.uint16)
for x, y, z, mid in data['voxels']:
    if 0 <= x < w and 0 <= y < h and 0 <= z < d:
        big_grid[x, y, z] = mid

veh.voxel_to_editor(big_grid, (w, new_h, d), "voxel_editor.html", title="Firework2 - Analysis")
print(f"\nSaved: voxel_editor.html ({w}x{new_h}x{d})")
