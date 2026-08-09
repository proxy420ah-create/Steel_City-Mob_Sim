"""Generate a standalone .stasset with Vinny next to a standard door at TRUE world scale.

In-game: characters render at 0.02f/voxel, buildings at 0.1f/voxel (5x larger).
To show true relative scale in VS (which uses one voxel size), we downscale
Vinny by 5x using nearest-neighbor so both fit in the same grid at building scale.

Result: what you see in VS is exactly what you'd see in-game, side by side.
"""
import numpy as np
from procedural_mob_characters import generate_hoodlum
from mob_materials import *
from stasset_io import save_stasset

SCALE = 5  # building voxel / character voxel = 0.1 / 0.02

def downscale_voxels(grid, factor):
    """Nearest-neighbor downscale by integer factor."""
    w, h, d = grid.shape
    nw, nh, nd = w // factor, h // factor, d // factor
    result = np.zeros((nw, nh, nd), dtype=grid.dtype)
    for x in range(nw):
        for y in range(nh):
            for z in range(nd):
                block = grid[x*factor:(x+1)*factor, y*factor:(y+1)*factor, z*factor:(z+1)*factor]
                non_air = block[block != 0]
                if len(non_air) > 0:
                    vals, counts = np.unique(non_air, return_counts=True)
                    result[x, y, z] = vals[np.argmax(counts)]
    return result

# --- Generate Vinny at character scale, then downscale to building scale ---
vinny_full = generate_hoodlum(seed=42)
vw_f, vh_f, vd_f = vinny_full.shape  # 16, 32, 10
vinny = downscale_voxels(vinny_full, SCALE)  # 3, 6, 2
vw, vh, vd = vinny.shape
print(f"Vinny full: {vw_f}x{vh_f}x{vd_f} -> downscaled: {vw}x{vh}x{vd}")
print(f"  World height: {vh_f * 0.02:.2f}m (full) = {vh * 0.1:.2f}m (scaled)")

# --- Door at building scale (already correct) ---
DOOR_W = 3  # slimmed to 3v for tighter character proportion
DOOR_H = 8
DOOR_T = 2
GAP = 3

# --- Build combined grid ---
total_w = vw + GAP + DOOR_W + 4
total_h = max(vh, DOOR_H) + 3
total_d = max(vd, DOOR_T) + 4

grid = np.zeros((total_w, total_h, total_d), dtype=np.uint16)

# Ground plane
grid[:, 0, :] = COBBLESTONE

# Place Vinny on the left
vx, vy, vz = 2, 1, 2
grid[vx:vx+vw, vy:vy+vh, vz:vz+vd] = vinny

# Place door on the right
door_x = vx + vw + GAP
door_y = 1
door_z = 2

# Door frame
grid[door_x-1:door_x, door_y:door_y+DOOR_H+1, door_z:door_z+DOOR_T] = DARK_WOOD
grid[door_x+DOOR_W:door_x+DOOR_W+1, door_y:door_y+DOOR_H+1, door_z:door_z+DOOR_T] = DARK_WOOD
grid[door_x-1:door_x+DOOR_W+1, door_y+DOOR_H:door_y+DOOR_H+1, door_z:door_z+DOOR_T] = DARK_WOOD

# Door
grid[door_x:door_x+DOOR_W, door_y:door_y+DOOR_H, door_z] = PAINTED_BROWN

# Doorknob (on the right edge — like a single door)
grid[door_x+DOOR_W-1, door_y+4, door_z] = GOLD_BRASS

# Wall context around door
for y in range(door_y, door_y + DOOR_H + 2):
    if y < door_y or y >= door_y + DOOR_H:
        grid[door_x-1:door_x+DOOR_W+1, y, door_z:door_z+DOOR_T] = RED_BRICK

out_path = "scale_reference_vinny_door.stasset"
save_stasset(out_path, grid, building_meta={
    "type": "scale_reference",
    "description": "Vinny (downscaled 5x) next to standard door — true world scale",
    "vinny_original": [int(vw_f), int(vh_f), int(vd_f)],
    "vinny_scaled": [int(vw), int(vh), int(vd)],
    "door_dims": [DOOR_W, DOOR_H, DOOR_T],
    "scale_factor": SCALE,
    "note": "Both at building voxel size (0.1f). Vinny was 0.02f, downscaled 5x.",
})
print(f"\nDone! Grid: {grid.shape}, Non-air: {np.count_nonzero(grid)}")
print(f"Saved to: {out_path}")
