"""Regenerate tenement_block_0 with 8-voxel buffer (was 4) and bolt on
the hand-designed fire escape. Total footprint stays 96x96.

Core: 80x80 (was 88x88), Buffer: 8 voxels each side (was 4), Total: 96x96
This gives 8 voxels of decoration room — enough for the 7-voxel-deep fire escape.
"""
import json
import numpy as np
import sys, os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from stasset_io import load_stasset_full, save_stasset
import procedural_mob_buildings as pmb

DARK_IRON = 109
PAINTED_METAL = 111

# --- Step 1: Regenerate tenement with w=80, d=80 core ---
# generate_apartment_block(w, h, d) builds core at w×d, then pads with BUF=4 → 88x88
# We then add 4 more voxels of buffer to get 96x96
print("=== Regenerating tenement with w=80, d=80 core ===")
padded_88, orig_meta = pmb.generate_apartment_block(w=80, h=44, d=80, seed=42)
print(f"Generated (with 4v buffer): {padded_88.shape}")

# Add 4 more voxels of buffer on X and Z to reach 96x96
extra = 4
W, H, D = 96, 44, 96
grid = np.zeros((W, H, D), dtype=np.uint16)
grid[extra:extra+88, :, extra:extra+88] = padded_88
print(f"Final tenement: {grid.shape}, non-air: {np.count_nonzero(grid)}")

# --- Step 2: Remove old fire escape from buffer zones ---
BUF = 8
removed = 0
for z in range(D - BUF, D):
    for x in range(W):
        for y in range(H):
            if grid[x, y, z] == DARK_IRON:
                grid[x, y, z] = 0
                removed += 1
for z in range(BUF):
    for x in range(W):
        for y in range(H):
            if grid[x, y, z] == DARK_IRON:
                grid[x, y, z] = 0
                removed += 1
for x in range(BUF):
    for y in range(H):
        for z in range(D):
            if grid[x, y, z] == DARK_IRON:
                grid[x, y, z] = 0
                removed += 1
for x in range(W - BUF, W):
    for y in range(H):
        for z in range(D):
            if grid[x, y, z] == DARK_IRON:
                grid[x, y, z] = 0
                removed += 1
print(f"Removed {removed} old fire escape voxels from buffer zones")

# --- Step 3: Load and align fire escape ---
with open(r'C:\Users\NADECC\Downloads\Fireescape_test.json') as f:
    fe_data = json.load(f)

fe_voxels = fe_data['voxels']
xs = [v[0] for v in fe_voxels]
ys = [v[1] for v in fe_voxels]
zs = [v[2] for v in fe_voxels]
fe_x0, fe_x1 = min(xs), max(xs)
fe_y0, fe_y1 = min(ys), max(ys)
fe_z0, fe_z1 = min(zs), max(zs)

from collections import Counter
y_counts = Counter(ys)
avg_count = np.mean(list(y_counts.values()))
landing_ys = sorted([y for y, c in y_counts.items() if c > avg_count * 2])
print(f"Fire escape landings: {landing_ys}")

# Target: floor slabs at Y=10, 18, 26, 34 (ground_h=10, story_h=8)
# Landings at top of each floor: Y=16, 24, 32, 40
target_landings = [16, 24, 32, 40]
y_shift = target_landings[0] - landing_ys[0]
print(f"Y shift: {y_shift}")

shifted_voxels = []
for x, y, z, mid in fe_voxels:
    ny = y + y_shift
    if ny >= 0:
        shifted_voxels.append([x, ny, z, mid])

fe_w = fe_x1 - fe_x0 + 1  # 19
fe_h = fe_y1 - fe_y0 + 1  # 38
fe_d = fe_z1 - fe_z0 + 1  # 7

# --- Step 4: Place fire escape in the 8-voxel back buffer ---
# Wall surface at z = D - BUF - 1 = 96 - 8 - 1 = 87
# Back buffer: z = 88..95 (8 voxels)
# FE z=0 at wall (z=87), z=6 at z=93 — fits within buffer!
wall_z = D - BUF - 1  # = 87
place_z = wall_z  # FE z=0 at wall surface
place_x = W - BUF - 2 - fe_w  # right-aligned in buffer
place_y = 0

print(f"\nPlacement: x={place_x}, z={place_z}")
print(f"  FE spans X=[{place_x},{place_x+fe_w-1}] Z=[{place_z},{place_z+fe_d-1}]")
print(f"  Wall at z={wall_z}, buffer z=[{wall_z+1},{D-1}]")

placed = 0
for x, y, z, mid in shifted_voxels:
    px = x - fe_x0 + place_x
    py = y
    pz = z - fe_z0 + place_z
    if 0 <= px < W and 0 <= py < H and 0 <= pz < D:
        if grid[px, py, pz] == 0 or mid == DARK_IRON or mid == PAINTED_METAL:
            grid[px, py, pz] = mid
            placed += 1
print(f"Placed {placed} fire escape voxels")

# --- Step 5: Drop ladder ---
ladder_x_start = place_x + fe_w // 2 - 1
ladder_z = place_z + fe_d - 1  # outermost edge
for y in range(1, target_landings[0] + 1):
    grid[ladder_x_start, y, ladder_z] = DARK_IRON
    grid[ladder_x_start + 1, y, ladder_z] = DARK_IRON
    if y % 2 == 0:
        grid[ladder_x_start, y, ladder_z - 1] = DARK_IRON
        grid[ladder_x_start + 1, y, ladder_z - 1] = DARK_IRON
# Guide rails
for y in range(1, target_landings[0] + 1):
    grid[ladder_x_start - 1, y, ladder_z] = DARK_IRON
    grid[ladder_x_start + 2, y, ladder_z] = DARK_IRON
print("Added drop ladder + guide rails")

# --- Step 6: Support posts ---
post_xs = [place_x, place_x + fe_w - 1]
post_z = place_z + fe_d - 1
for px in post_xs:
    for y in range(0, target_landings[0]):
        if grid[px, y, post_z] == 0:
            grid[px, y, post_z] = DARK_IRON
for px in post_xs:
    for y in range(0, target_landings[0]):
        if grid[px, y, post_z - 1] == 0:
            grid[px, y, post_z - 1] = DARK_IRON
print("Added support posts")

# --- Step 7: Save ---
out_path = "tenement_block_0_new_fe.stasset"
meta = {'door_face': 'front', 'door_height': 8, 'door_width': 6, 'door_y': 1, 'door_x_center': W // 2}
save_stasset(out_path, grid, building_meta=meta)
print(f"\nSaved: {out_path}")
print(f"  Dims: {W}x{H}x{D} | Non-air: {np.count_nonzero(grid)}")

# --- Step 8: Render ---
import voxel_editor_html as veh
veh.voxel_to_editor(grid, (W, H, D), "voxel_editor.html", title="Tenement Block 0 - 8v Buffer + New FE")
print("Saved: voxel_editor.html")
