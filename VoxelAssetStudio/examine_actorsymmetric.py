import json
import numpy as np
from stasset_io import load_stasset

# Load both models for comparison
print("=== LOADING ACTORSYMMETRIC ===")
sym_voxels, sym_dims, sym_skeleton = load_stasset('../My project/Assets/StreamingAssets/ActorSymmetric.stasset')
print(f"Dimensions: {sym_dims}")
print(f"Total voxels: {sym_voxels.size}")
print(f"Non-air voxels: {np.count_nonzero(sym_voxels)}")
if sym_skeleton is not None:
    print(f"Bones: {len(sym_skeleton.get('bones', []))}")
    print(f"Joints: {len(sym_skeleton.get('joints', []))}")
else:
    print("NO SKELETON DATA FOUND")

print("\n=== LOADING ACTOR (REFERENCE) ===")
actor_voxels, actor_dims, actor_skeleton = load_stasset('../My project/Assets/StreamingAssets/Actor.stasset')
print(f"Dimensions: {actor_dims}")
print(f"Total voxels: {actor_voxels.size}")
print(f"Non-air voxels: {np.count_nonzero(actor_voxels)}")
if actor_skeleton is not None:
    print(f"Bones: {len(actor_skeleton.get('bones', []))}")
    print(f"Joints: {len(actor_skeleton.get('joints', []))}")
else:
    print("NO SKELETON DATA FOUND")

# Check voxel distribution
print("\n=== ACTORSYMMETRIC VOXEL DISTRIBUTION ===")
materials, counts = np.unique(sym_voxels, return_counts=True)
for mat, count in zip(materials, counts):
    if mat != 0:  # Skip air
        print(f"Material {mat}: {count} voxels")

print("\n=== ACTOR VOXEL DISTRIBUTION ===")
materials, counts = np.unique(actor_voxels, return_counts=True)
for mat, count in zip(materials, counts):
    if mat != 0:  # Skip air
        print(f"Material {mat}: {count} voxels")
