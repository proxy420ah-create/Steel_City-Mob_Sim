"""Analyze fire escape landings on the back wall of firework2.json."""
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

# Fire escape is on the back wall, protruding into +Z buffer (Z > wall)
# The core building ends at Z ~87 (80 core + 8 buffer - 1 = 87, wall at Z=86-87)
# Fire escape protrudes beyond the wall into Z=88+
# But also some FE parts are at the wall face

# Find all FE voxels and their Z distribution
fe_voxels = [(x, y, z, mid) for x, y, z, mid in data['voxels'] if mid in (DARK_IRON, PAINTED_METAL)]
fe_zs = [v[2] for v in fe_voxels]
z_counts = Counter(fe_zs)
print("FE Z-level distribution:")
for z in sorted(z_counts.keys()):
    print(f"  Z={z:2d}: {z_counts[z]:3d} voxels")

# Identify the fire escape Z range (protruding from back wall)
# Look for FE voxels at Z >= 85 (back wall area + buffer)
fe_back = [(x, y, z, mid) for x, y, z, mid in fe_voxels if z >= 85]
print(f"\nFE voxels at Z>=85: {len(fe_back)}")

# Y distribution of back-wall FE
fe_back_ys = [v[1] for v in fe_back]
y_counts = Counter(fe_back_ys)
print("\nBack-wall FE Y distribution:")
for y in sorted(y_counts.keys()):
    print(f"  Y={y:2d}: {y_counts[y]:3d} voxels")

# Find landing levels
avg_count = np.mean(list(y_counts.values()))
landings = sorted([y for y, c in y_counts.items() if c > avg_count * 1.5])
print(f"\nLanding Y levels: {landings}")

# For each landing, show X extent and Z extent
for ly in landings:
    lv = [(x, z) for x, y, z, mid in fe_back if y == ly]
    if not lv:
        continue
    xs = [v[0] for v in lv]
    zs = [v[1] for v in lv]
    print(f"\n  Landing Y={ly}: {len(lv)} voxels")
    print(f"    X range: {min(xs)}-{max(xs)} (width={max(xs)-min(xs)+1})")
    print(f"    Z range: {min(zs)}-{max(zs)} (depth={max(zs)-min(zs)+1})")
    # Show X distribution to see windows covered
    x_counts = Counter(xs)
    print(f"    X distribution: {dict(sorted(x_counts.items()))}")

# Now check window positions on the back wall
# Windows are at Y=12-16, 20-24, 28-32, 34-38
# They use STOREFRONT_GLASS or similar material
# Let's find window material
print("\n=== Window materials on back wall ===")
# Check what materials are at the back wall (Z=86-87) at window Y levels
for y in range(12, 17):
    for x in range(8, 80):
        for z in [86, 87]:
            mid = grid[x, y, z]
            if mid != 0 and mid != 100:  # not air, not red brick
                print(f"  ({x},{y},{z}) = {mid}")

# Find windows by looking for non-brick, non-iron materials in the wall
print("\n=== Non-brick voxels in back wall (Z=86-87) at window Y levels ===")
window_mats = set()
for y in range(10, 44):
    for x in range(8, 80):
        for z in [86, 87]:
            mid = int(grid[x, y, z])
            if mid != 0 and mid != 100 and mid not in (DARK_IRON, PAINTED_METAL):
                window_mats.add(mid)
                if y in (12, 20, 28, 36):  # window sill levels
                    print(f"  ({x},{y},{z}) mat={mid}")

print(f"\nWindow materials found: {window_mats}")

# Find exact window X positions for each floor
for floor, (y_start, y_end) in enumerate([(12,16), (20,24), (28,32), (34,38)]):
    window_xs = set()
    for y in range(y_start, y_end+1):
        for x in range(8, 80):
            for z in [86, 87]:
                mid = int(grid[x, y, z])
                if mid != 0 and mid != 100 and mid not in (DARK_IRON, PAINTED_METAL):
                    window_xs.add(x)
    if window_xs:
        wxs = sorted(window_xs)
        # Group into windows (consecutive X ranges)
        windows = []
        start = wxs[0]
        prev = wxs[0]
        for x in wxs[1:]:
            if x == prev + 1:
                prev = x
            else:
                windows.append((start, prev))
                start = x
                prev = x
        windows.append((start, prev))
        print(f"\n  Floor {floor} (Y={y_start}-{y_end}): {len(windows)} windows")
        for i, (s, e) in enumerate(windows):
            print(f"    Window {i}: X={s}-{e} (width={e-s+1})")
