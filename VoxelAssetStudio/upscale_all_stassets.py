"""Upscale ALL building .stasset files by 2x for the new voxelSize=0.05 regime.

Each voxel at (x,y,z) becomes 8 voxels at (2x..2x+1, 2y..2y+1, 2z..2z+1).
Handles: empty_land_*.stasset, road_tile_0.stasset, tenement_block_1.stasset
Skips: tenement_block_0.stasset (already upscaled), character/vehicle assets,
        backup files.
"""
import os
import numpy as np
from stasset_io import load_stasset, save_stasset

BUILDINGS_DIR = r'..\Assets\StreamingAssets\voxel_buildings'

# Find all .stasset files to upscale
skip_patterns = ['character', 'vehicle', 'backup', 'tenement_block_0.stasset']
all_files = [f for f in os.listdir(BUILDINGS_DIR) if f.endswith('.stasset')]
to_upscale = [f for f in all_files if not any(p in f for p in skip_patterns)]

print(f"Found {len(all_files)} .stasset files, {len(to_upscale)} to upscale")

# Check if empty_land files are all identical by reading one
first_empty = None
empty_files = [f for f in to_upscale if 'empty_land' in f]
non_empty_files = [f for f in to_upscale if 'empty_land' not in f]

print(f"  {len(empty_files)} empty_land files (likely identical)")
print(f"  {len(non_empty_files)} other files: {non_empty_files}")

# Upscale one empty_land file, then copy to all
if empty_files:
    first_empty = empty_files[0]
    first_path = os.path.join(BUILDINGS_DIR, first_empty)
    voxels, skeleton, meta = load_stasset(first_path)
    w, h, d = voxels.shape
    print(f"\nUpscaling {first_empty}: {w}x{h}x{d} -> {w*2}x{h*2}x{d*2}")
    big = np.repeat(np.repeat(np.repeat(voxels, 2, axis=0), 2, axis=1), 2, axis=2)
    print(f"  Solid voxels: {np.count_nonzero(voxels)} -> {np.count_nonzero(big)}")

    # Save the first one
    save_stasset(first_path, big, building_meta=meta)
    print(f"  Saved: {first_path}")

    # Copy to all other empty_land files (they're identical)
    for f in empty_files[1:]:
        dst = os.path.join(BUILDINGS_DIR, f)
        save_stasset(dst, big, building_meta=meta)
    print(f"  Copied to all {len(empty_files)} empty_land files")

# Upscale non-empty files individually
for f in non_empty_files:
    fpath = os.path.join(BUILDINGS_DIR, f)
    voxels, skeleton, meta = load_stasset(fpath)
    w, h, d = voxels.shape
    print(f"\nUpscaling {f}: {w}x{h}x{d} -> {w*2}x{h*2}x{d*2}")
    big = np.repeat(np.repeat(np.repeat(voxels, 2, axis=0), 2, axis=1), 2, axis=2)
    print(f"  Solid voxels: {np.count_nonzero(voxels)} -> {np.count_nonzero(big)}")
    save_stasset(fpath, big, building_meta=meta)
    print(f"  Saved: {fpath}")

print(f"\nDone! Upscaled {len(to_upscale)} files.")
