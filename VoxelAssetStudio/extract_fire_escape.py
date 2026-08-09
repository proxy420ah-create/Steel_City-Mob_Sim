"""Extract fire escape voxels from tenement block as standalone .stasset files.

Finds all DARK_IRON voxels, separates into clusters (rear + side),
crops each to its bounding box, and saves individually for hand-editing in VS.
"""
import numpy as np
from procedural_mob_buildings import generate_building
from mob_materials import *
from stasset_io import save_stasset

# Generate tenement block
grid, dims, meta = generate_building('tenement_block', seed=42)
w, h, d = dims
print(f"Tenement: {w}x{h}x{d}")

# Find all DARK_IRON voxels
iron_mask = grid == DARK_IRON
coords = np.argwhere(iron_mask)
print(f"Total DARK_IRON voxels: {len(coords)}")

# Separate into clusters by X-Z location
# Rear escape is at high Z (back), side escape is at low X (left)
rear_mask = coords[:, 2] > d // 2  # z > 48
side_mask = coords[:, 2] <= d // 2

clusters = {
    'rear': coords[rear_mask],
    'side': coords[side_mask],
}

for name, cluster_coords in clusters.items():
    if len(cluster_coords) == 0:
        print(f"  {name}: no voxels found")
        continue

    # Bounding box
    xmin, ymin, zmin = cluster_coords.min(axis=0)
    xmax, ymax, zmax = cluster_coords.max(axis=0)
    cw = xmax - xmin + 1
    ch = ymax - ymin + 1
    cd = zmax - zmin + 1

    # Extract with 1-voxel padding for context
    px = max(0, xmin - 1)
    py = max(0, ymin - 1)
    pz = max(0, zmin - 1)
    pw = min(w - px, cw + 2)
    ph = min(h - py, ch + 2)
    pd = min(d - pz, cd + 2)

    sub = grid[px:px+pw, py:py+ph, pz:pz+pd].copy()

    # Zero out non-iron voxels (keep only fire escape + air)
    # Actually keep everything for context — user can see where wall is
    # But let's also make a pure-iron version
    pure = sub.copy()
    pure[(pure != DARK_IRON) & (pure != 0)] = 0  # remove building materials, keep iron + air

    out_path = f"fire_escape_{name}.stasset"
    save_stasset(out_path, sub, building_meta={
        "type": "fire_escape_component",
        "location": name,
        "original_dims": [int(cw), int(ch), int(cd)],
        "offset_in_building": [int(px), int(py), int(pz)],
        "note": "Extracted from tenement_block. Edit and re-bake into building generator.",
    })
    print(f"  {name}: {pw}x{ph}x{pd} at offset ({px},{py},{pz}) — {np.count_nonzero(sub == DARK_IRON)} iron voxels")
    print(f"    Saved: {out_path}")

    # Also save pure version (only iron + air)
    pure_path = f"fire_escape_{name}_pure.stasset"
    save_stasset(pure_path, pure, building_meta={
        "type": "fire_escape_component_pure",
        "location": name,
        "note": "Iron-only extraction for clean editing.",
    })
    print(f"    Saved: {pure_path}")
