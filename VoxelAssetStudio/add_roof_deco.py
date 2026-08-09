"""Add water tower + parapet to firework3_dual_fe.json, avoiding ladders.

Roof slab is at Y=42-43. Roof buffer is Y=44-51 (8 voxels).
Features:
1. Parapet wall: 2v tall brick wall around roof perimeter (Y=44-45), skipping ladder exits
2. Water tower: 8x8 wooden tank on 4 iron legs, centered on roof

Must avoid:
- Roof ladder exit (check where DARK_IRON voxels are at Y>=42)
- Loop ladder (user's custom ladder to roof)
"""
import json
import numpy as np
from collections import Counter

with open(r'C:\Users\NADECC\Downloads\firework3_dual_fe.json') as f:
    data = json.load(f)

w, h, d = data['dims']
print(f"Dims: {w}x{h}x{d}")

grid = np.zeros((w, h, d), dtype=np.uint16)
for x, y, z, mid in data['voxels']:
    if 0 <= x < w and 0 <= y < h and 0 <= z < d:
        grid[x, y, z] = mid

DARK_IRON = 109
PAINTED_METAL = 111
FE_MATS = {DARK_IRON, PAINTED_METAL}

# Material IDs
RED_BRICK = 100
WEATHERED_WOOD = 108
DARK_WOOD = 106
TAR = 115  # roof material - check

# Find what material the roof is
print("\nRoof materials at Y=42-43:")
for y in [42, 43]:
    mats = Counter()
    for x in range(w):
        for z in range(d):
            mid = int(grid[x, y, z])
            if mid != 0:
                mats[mid] += 1
    print(f"  Y={y}: {dict(mats)}")

# Find ladder positions at roof level (Y>=40, FE materials)
print("\nFE voxels at Y>=40 (ladders/roof access):")
ladder_positions = []
for x in range(w):
    for y in range(40, h):
        for z in range(d):
            mid = int(grid[x, y, z])
            if mid in FE_MATS:
                ladder_positions.append((x, y, z, mid))
                print(f"  ({x},{y},{z}) mat={mid}")

# Find the roof extent (where roof material exists at Y=42-43)
roof_xs = set()
roof_zs = set()
for x in range(w):
    for z in range(d):
        if int(grid[x, 42, z]) != 0 or int(grid[x, 43, z]) != 0:
            roof_xs.add(x)
            roof_zs.add(z)
print(f"\nRoof X extent: {min(roof_xs)}-{max(roof_xs)}")
print(f"Roof Z extent: {min(roof_zs)}-{max(roof_zs)}")

# Find ladder X,Z positions to avoid
ladder_xz = set((x, z) for x, y, z, mid in ladder_positions)
print(f"Ladder positions (X,Z) at roof: {sorted(ladder_xz)}")

# === 1. Parapet wall ===
# 2 voxels tall (Y=44-45), 1 voxel thick, around roof perimeter
# Building core walls are at X=8-9 / X=86-87, Z=8-9 / Z=86-87
# Parapet sits ON TOP of the walls at Y=44-45
parapet_y_start = 44
parapet_y_end = 46  # exclusive (2 voxels tall)
parapet_added = 0

# Build parapet on all 4 sides of the building core
# Core spans X=8-87, Z=8-87 (80x80 + 8 buffer each side)
# Wall thickness = 2, so outer wall faces are at X=8, X=87, Z=8, Z=87
parapet_positions = set()

# Left wall (X=8-9, all Z)
for x in [8, 9]:
    for z in range(8, 88):
        parapet_positions.add((x, z))

# Right wall (X=86-87, all Z)
for x in [86, 87]:
    for z in range(8, 88):
        parapet_positions.add((x, z))

# Front wall (Z=8-9, all X) — skip door area
for z in [8, 9]:
    for x in range(8, 88):
        parapet_positions.add((x, z))

# Back wall (Z=86-87, all X)
for z in [86, 87]:
    for x in range(8, 88):
        parapet_positions.add((x, z))

print(f"\nParapet positions before filtering: {len(parapet_positions)}")

# Remove positions where ladders pass through (FE voxels at Y>=42)
# Ladder XZ positions at roof level
ladder_xz_roof = set()
for x, y, z, mid in ladder_positions:
    if y >= 42:
        ladder_xz_roof.add((x, z))

print(f"Ladder positions at roof (X,Z): {sorted(ladder_xz_roof)[:20]}... ({len(ladder_xz_roof)} total)")

# Filter: skip parapet positions that have ladder voxels at Y>=42
filtered_parapet = set()
for x, z in parapet_positions:
    if (x, z) in ladder_xz_roof:
        continue
    # Also skip if adjacent position has ladder (leave gap for ladder exit)
    has_ladder_neighbor = False
    for dx, dz in [(0,1),(0,-1),(1,0),(-1,0)]:
        if (x+dx, z+dz) in ladder_xz_roof:
            # Check if the ladder actually reaches above Y=43
            for ly in range(42, h):
                if int(grid[x+dx, ly, z+dz]) in FE_MATS:
                    has_ladder_neighbor = True
                    break
            if has_ladder_neighbor:
                break
    if has_ladder_neighbor:
        continue
    filtered_parapet.add((x, z))

print(f"Parapet positions after filtering: {len(filtered_parapet)}")

for x, z in filtered_parapet:
    for y in range(parapet_y_start, parapet_y_end):
        if y < h and grid[x, y, z] == 0:
            grid[x, y, z] = RED_BRICK
            parapet_added += 1

print(f"Parapet: +{parapet_added} voxels")

# === 2. Water tower ===
# 8x8 wooden tank on 4 iron legs
# Place centered on roof, avoiding ladders
# Roof center: X=48, Z=48 (center of 96-wide grid)
# Check if center is clear
wt_cx = 48
wt_cz = 48
wt_base = 44  # sits on parapet level
wt_leg_h = 3  # 3 voxel legs (Y=44-46)
wt_tank_y = wt_base + wt_leg_h  # Y=47
wt_tank_h = 4  # 4 voxels tall tank (Y=47-50)
wt_size = 8  # 8x8 tank

# Check for conflicts
conflicts = False
for x in range(wt_cx - wt_size//2, wt_cx + wt_size//2):
    for z in range(wt_cz - wt_size//2, wt_cz + wt_size//2):
        for y in range(wt_base, wt_base + wt_leg_h + wt_tank_h):
            if y < h and grid[x, y, z] != 0:
                conflicts = True
                print(f"  CONFLICT at ({x},{y},{z}) mat={grid[x,y,z]}")

if conflicts:
    # Try offset position
    print("Center conflicts, trying offset...")
    # Check a few positions
    for try_cx, try_cz in [(30, 30), (60, 30), (30, 60), (60, 60), (48, 30), (30, 48)]:
        test_conflicts = False
        for x in range(try_cx - wt_size//2, try_cx + wt_size//2):
            for z in range(try_cz - wt_size//2, try_cz + wt_size//2):
                for y in range(wt_base, wt_base + wt_leg_h + wt_tank_h):
                    if 0 <= x < w and 0 <= z < d and y < h:
                        if grid[x, y, z] != 0:
                            test_conflicts = True
        if not test_conflicts:
            wt_cx, wt_cz = try_cx, try_cz
            print(f"  Using position ({wt_cx},{wt_cz})")
            conflicts = False
            break

if not conflicts:
    wt_added = 0
    # 4 iron legs at corners
    leg_offsets = [(-wt_size//2, -wt_size//2), (wt_size//2-1, -wt_size//2),
                   (-wt_size//2, wt_size//2-1), (wt_size//2-1, wt_size//2-1)]
    for lx, lz in leg_offsets:
        for y in range(wt_base, wt_base + wt_leg_h):
            x = wt_cx + lx
            z = wt_cz + lz
            if 0 <= x < w and 0 <= z < d and y < h:
                if grid[x, y, z] == 0:
                    grid[x, y, z] = DARK_IRON
                    wt_added += 1

    # Wooden tank (8x8, 4 tall)
    for x in range(wt_cx - wt_size//2, wt_cx + wt_size//2):
        for z in range(wt_cz - wt_size//2, wt_cz + wt_size//2):
            for y in range(wt_tank_y, wt_tank_y + wt_tank_h):
                if 0 <= x < w and 0 <= z < d and y < h:
                    if grid[x, y, z] == 0:
                        grid[x, y, z] = WEATHERED_WOOD
                        wt_added += 1

    # Tank top (cone-ish - just flat top)
    for x in range(wt_cx - wt_size//2, wt_cx + wt_size//2):
        for z in range(wt_cz - wt_size//2, wt_cz + wt_size//2):
            y = wt_tank_y + wt_tank_h
            if 0 <= x < w and 0 <= z < d and y < h:
                if grid[x, y, z] == 0:
                    grid[x, y, z] = DARK_WOOD
                    wt_added += 1

    print(f"Water tower: +{wt_added} voxels at ({wt_cx},{wt_cz})")
    print(f"  Legs: Y={wt_base}-{wt_base+wt_leg_h-1}")
    print(f"  Tank: Y={wt_tank_y}-{wt_tank_y+wt_tank_h-1} ({wt_size}x{wt_size})")
    print(f"  Top:  Y={wt_tank_y+wt_tank_h}")
else:
    print("WARNING: Could not find clear position for water tower!")
    wt_added = 0

# Convert back to voxel list
new_voxels = []
for x in range(w):
    for y in range(h):
        for z in range(d):
            if grid[x, y, z] != 0:
                new_voxels.append([x, y, z, int(grid[x, y, z])])

print(f"\nTotal voxels: {len(data['voxels'])} -> {len(new_voxels)}")

# Save
out_data = {
    'dims': data['dims'],
    'materials': data['materials'],
    'voxels': new_voxels
}
out_path = r'C:\Users\NADECC\Downloads\firework3_roof.json'
with open(out_path, 'w') as f:
    json.dump(out_data, f)
print(f"Saved: {out_path}")

# Load into editor
import voxel_editor_html as veh
ROOF_BUF = 8
new_h = h + ROOF_BUF
big_grid = np.zeros((w, new_h, d), dtype=np.uint16)
for x, y, z, mid in new_voxels:
    if 0 <= x < w and 0 <= y < h and 0 <= z < d:
        big_grid[x, y, z] = mid
veh.voxel_to_editor(big_grid, (w, new_h, d), "voxel_editor.html",
                    title="Firework3 - Roof Deco (Tower + Parapet)")
print(f"Saved: voxel_editor.html ({w}x{new_h}x{d})")
