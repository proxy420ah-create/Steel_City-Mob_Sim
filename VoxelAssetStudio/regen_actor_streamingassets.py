"""
Regenerate the Actor .stasset (v2) with voxel bounds directly into Unity's
StreamingAssets so the Unity loader/ragdoll work against the current rig.

Run from the VoxelAssetStudio folder:
    python regen_actor_streamingassets.py
"""

import os

from skeleton_generator_actor import generate_actor_skeleton
from stasset_io import save_stasset, load_stasset

OUT_PATH = os.path.join("..", "My project", "Assets", "StreamingAssets", "Actor.stasset")


def main():
    voxels, skeleton = generate_actor_skeleton()

    os.makedirs(os.path.dirname(OUT_PATH), exist_ok=True)
    save_stasset(OUT_PATH, voxels, skeleton)

    # Read it back as a sanity check.
    rv, dims, rs = load_stasset(OUT_PATH)
    print(f"Wrote {OUT_PATH}")
    print(f"  dims = {dims[0]}x{dims[1]}x{dims[2]}")
    if rs is not None:
        print(f"  rig  = {len(rs['bones'])} bones, {len(rs['joints'])} joints, "
              f"root_joint = {rs.get('root_joint')}")
        # Verify voxel bounds are present.
        bones_with_bounds = sum(1 for b in rs['bones'] if 'voxel_bounds_min' in b)
        joints_with_bounds = sum(1 for j in rs['joints'] if 'voxel_bounds_min' in j)
        print(f"  voxel_bounds: {bones_with_bounds}/{len(rs['bones'])} bones, "
              f"{joints_with_bounds}/{len(rs['joints'])} joints")
    else:
        print("  rig  = (none) -- ERROR: skeleton block missing!")


if __name__ == "__main__":
    main()
