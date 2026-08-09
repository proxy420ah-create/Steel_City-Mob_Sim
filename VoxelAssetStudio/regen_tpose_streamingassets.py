"""
Regenerate the T-pose biped .stasset (v2) directly into Unity's StreamingAssets
so the Unity loader/ragdoll work against the current pelvis-rooted rig.

Run from the VoxelAssetStudio folder:
    python regen_tpose_streamingassets.py
"""

import os

from skeleton_generator_tpose import generate_tpose_biped_skeleton
from stasset_io import save_stasset, load_stasset

OUT_PATH = os.path.join("..", "My project", "Assets", "StreamingAssets", "Tpose.stasset")


def main():
    voxels, skeleton = generate_tpose_biped_skeleton(grid_size=(18, 32, 8))

    os.makedirs(os.path.dirname(OUT_PATH), exist_ok=True)
    save_stasset(OUT_PATH, voxels, skeleton)

    # Read it back as a sanity check.
    rv, dims, rs = load_stasset(OUT_PATH)
    print(f"Wrote {OUT_PATH}")
    print(f"  dims = {dims[0]}x{dims[1]}x{dims[2]}")
    if rs is not None:
        print(f"  rig  = {len(rs['bones'])} bones, {len(rs['joints'])} joints, "
              f"root_joint = {rs.get('root_joint')}")
    else:
        print("  rig  = (none) -- ERROR: skeleton block missing!")


if __name__ == "__main__":
    main()
