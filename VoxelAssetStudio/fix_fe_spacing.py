"""Fix fire escape spacing to exactly 8 voxels between landings, then bolt onto tenement.

Current (after -2 shift): landings at Y=12, 21, 29, 38 → gaps 9, 8, 9
Target:                    landings at Y=12, 20, 28, 36 → gaps 8, 8, 8

Shift rule:
  Y < 21:  shift 0  (landing 1 + stairs to landing 2)
  21<=Y<29: shift -1 (landing 2 + stairs to landing 3)
  Y >= 29: shift -2 (landing 3 + stairs to landing 4 + roof ladder)
"""
import json
import numpy as np
import sys, os
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from stasset_io import save_stasset
import procedural_mob_buildings as pmb
import voxel_editor_html as veh

DARK_IRON = 109
PAINTED_METAL = 111

# === Step 1: Load shifted fire escape and fix spacing ===
with open(r'C:\Users\NADECC\Downloads\Fireescape_test_shifted.json') as f:
    data = json.load(f)

fixed_voxels = []
for x, y, z, mid in data['voxels']:
    if y < 21:
        ny = y - 2
    elif y < 38:
        ny = y - 3
    else:
        ny = y - 4
    if ny >= 0:
        fixed_voxels.append([x, ny, z, mid])

print(f"Fixed {len(fixed_voxels)} voxels")

# Verify landing positions
ys = [v[1] for v in fixed_voxels]
y_counts = Counter(ys)
avg_count = np.mean(list(y_counts.values()))
landings = sorted([y for y, c in y_counts.items() if c > avg_count * 2])
gaps = [landings[i+1] - landings[i] for i in range(len(landings)-1)]
print(f"Fixed landing Y levels: {landings}")
print(f"Gaps between landings: {gaps}")
print(f"Tenement window sills: [12, 20, 28, 36] — landings below: [10, 18, 26, 34] — match: {landings == [10, 18, 26, 34]}")

# === Step 2: Regenerate tenement with 80x80 core + 8v buffer + 8v roof buffer ===
print("\n=== Regenerating tenement (80x80 core, 8v side buffer, 8v roof buffer) ===")
padded_88, orig_meta = pmb.generate_apartment_block(w=80, h=44, d=80, seed=42, roof_buf=8)
extra = 4
W, H, D = 96, 52, 96
grid = np.zeros((W, H, D), dtype=np.uint16)
grid[extra:extra+88, :, extra:extra+88] = padded_88
print(f"Tenement: {grid.shape}, non-air: {np.count_nonzero(grid)}")

# === Step 3: Remove old fire escape from buffer ===
BUF = 8
removed = 0
for z in list(range(BUF)) + list(range(D - BUF, D)):
    for x in range(W):
        for y in range(H):
            if grid[x, y, z] == DARK_IRON:
                grid[x, y, z] = 0
                removed += 1
for x in list(range(BUF)) + list(range(W - BUF, W)):
    for y in range(H):
        for z in range(D):
            if grid[x, y, z] == DARK_IRON:
                grid[x, y, z] = 0
                removed += 1
print(f"Removed {removed} old fire escape voxels")

# === Step 4: Place fire escape in back buffer ===
fe_xs = [v[0] for v in fixed_voxels]
fe_ys = [v[1] for v in fixed_voxels]
fe_zs = [v[2] for v in fixed_voxels]
fe_x0, fe_x1 = min(fe_xs), max(fe_xs)
fe_z0, fe_z1 = min(fe_zs), max(fe_zs)
fe_w = fe_x1 - fe_x0 + 1
fe_d = fe_z1 - fe_z0 + 1

wall_z = D - BUF - 1  # = 87
place_z = wall_z
place_x = W - BUF - 2 - fe_w

print(f"\nPlacement: x={place_x}, z={place_z}")
print(f"  FE spans X=[{place_x},{place_x+fe_w-1}] Z=[{place_z},{place_z+fe_d-1}]")

placed = 0
for x, y, z, mid in fixed_voxels:
    px = x - fe_x0 + place_x
    py = y
    pz = z - fe_z0 + place_z
    if 0 <= px < W and 0 <= py < H and 0 <= pz < D:
        if grid[px, py, pz] == 0 or mid == DARK_IRON or mid == PAINTED_METAL:
            grid[px, py, pz] = mid
            placed += 1
print(f"Placed {placed} fire escape voxels")

# === Step 5: Drop ladder from landing 1 (Y=12) to ground ===
target_landings = [10, 18, 26, 34]
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

# === Step 6: Roof ladder from top landing (Y=36) to roof (Y=43) ===
roof_landing_y = target_landings[-1]  # 36
roof_y = H - 2  # 42 (just below roof slab)
roof_ladder_x = place_x + fe_w // 2
roof_ladder_z = place_z  # against the wall
for y in range(roof_landing_y + 1, roof_y + 1):
    grid[roof_ladder_x, y, roof_ladder_z] = DARK_IRON
    grid[roof_ladder_x + 1, y, roof_ladder_z] = DARK_IRON
    if y % 2 == 0:
        grid[roof_ladder_x, y, roof_ladder_z + 1] = DARK_IRON
        grid[roof_ladder_x + 1, y, roof_ladder_z + 1] = DARK_IRON
print(f"Added roof ladder from Y={roof_landing_y+1} to Y={roof_y}")

# === Step 7: Support posts from ground to first landing ===
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

# === Step 8: Save + render ===
out_path = "tenement_block_0_new_fe.stasset"
meta = {'door_face': 'front', 'door_height': 8, 'door_width': 6, 'door_y': 1, 'door_x_center': W // 2}
save_stasset(out_path, grid, building_meta=meta)
print(f"\nSaved: {out_path}")
print(f"  Dims: {W}x{H}x{D} | Non-air: {np.count_nonzero(grid)}")

veh.voxel_to_editor(grid, (W, H, D), "voxel_editor.html", title="Tenement - Fixed FE Spacing + Roof Ladder")
print("Saved: voxel_editor.html")
