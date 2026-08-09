"""
Steel Tide Voxel Asset Studio
Toolbox: stasset_fit_colliders_to_voxels.py

The VoxelRagdoll collider for each bone is sized from the bone's voxel_bounds
metadata, not from the voxels actually assigned to the bone. If the bounds are
too tight, the model ends up resting on a tiny pelvis collider while the legs
and arms clip through the ground.

This script:
  1. Loads a v2 .stasset with a skeleton.
  2. Assigns every non-air voxel to its nearest bone segment (same logic as
     VoxelRagdoll).
  3. Computes the tight axis-aligned bounding box of voxels assigned to each bone.
  4. Writes the bounding box back into the bone (and joint) voxel_bounds.
  5. Saves a new .stasset so the colliders actually cover the voxels they move.

This does not add new bones or joints — it only makes the existing colliders fit.
"""

import argparse
import sys
from pathlib import Path

import numpy as np

SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPT_DIR.parent))
from stasset_io import load_stasset, save_stasset


def dist_point_segment(p, a, b):
    """Return the shortest distance from point p to the segment a-b."""
    ab = b - a
    len2 = np.dot(ab, ab)
    if len2 < 1e-12:
        return np.linalg.norm(p - a)
    t = max(0.0, min(1.0, np.dot(p - a, ab) / len2))
    closest = a + t * ab
    return np.linalg.norm(p - closest)


def fit_colliders_to_voxels(src_path, dst_path):
    src_path = Path(src_path).resolve()
    dst_path = Path(dst_path).resolve()

    if not src_path.exists():
        raise FileNotFoundError(src_path)

    voxels, dims, skeleton = load_stasset(str(src_path))
    if skeleton is None:
        raise ValueError("Source file has no skeleton (v1).")

    bones = skeleton.get("bones", [])
    if not bones:
        raise ValueError("Skeleton has no bones.")

    # Build numpy arrays for bone starts/ends and initialize bounds.
    bone_starts = []
    bone_ends = []
    bone_min = []
    bone_max = []
    for bone in bones:
        start = np.array(bone["start"], dtype=float)
        end = np.array(bone["end"], dtype=float)
        bone_starts.append(start)
        bone_ends.append(end)
        bone_min.append(np.array([float('inf')] * 3))
        bone_max.append(np.array([-float('inf')] * 3))

    # Assign every non-air voxel to its nearest bone and expand that bone's bounds.
    width, height, depth = dims
    for z in range(depth):
        for y in range(height):
            for x in range(width):
                if voxels[x, y, z] == 0:
                    continue

                p = np.array([x + 0.5, y + 0.5, z + 0.5], dtype=float)
                best_dist = float('inf')
                best_bone = 0
                for i in range(len(bones)):
                    d = dist_point_segment(p, bone_starts[i], bone_ends[i])
                    if d < best_dist:
                        best_dist = d
                        best_bone = i

                bone_min[best_bone] = np.minimum(bone_min[best_bone], p)
                bone_max[best_bone] = np.maximum(bone_max[best_bone], p)

    # Convert to inclusive integer voxel bounds and update the skeleton.
    for i, bone in enumerate(bones):
        mn = np.floor(bone_min[i]).astype(int)
        mx = np.floor(bone_max[i]).astype(int)
        # Ensure at least one voxel if the bone has no assigned voxels (fallback).
        if np.any(mx < mn):
            mn = np.array(bone["start"], dtype=int)
            mx = mn
        bone["voxel_bounds_min"] = [int(mn[0]), int(mn[1]), int(mn[2])]
        bone["voxel_bounds_max"] = [int(mx[0]), int(mx[1]), int(mx[2])]

    # Update joints to match their associated bone endpoints.
    joint_by_id = {j["id"]: j for j in skeleton.get("joints", [])}
    bone_by_child = {}
    for bone in bones:
        if bone.get("child_joint") is not None:
            bone_by_child[bone["child_joint"]] = bone

    for bone in bones:
        parent_id = bone["parent_joint"]
        if parent_id in joint_by_id:
            j = joint_by_id[parent_id]
            pos = np.array(j.get("position", bone["start"]), dtype=float)
            j["voxel_bounds_min"] = [int(pos[0]), int(pos[1]), int(pos[2])]
            j["voxel_bounds_max"] = [int(pos[0]), int(pos[1]), int(pos[2])]

        child_id = bone.get("child_joint")
        if child_id is not None and child_id in joint_by_id:
            j = joint_by_id[child_id]
            pos = np.array(j.get("position", bone["end"]), dtype=float)
            j["voxel_bounds_min"] = [int(pos[0]), int(pos[1]), int(pos[2])]
            j["voxel_bounds_max"] = [int(pos[0]), int(pos[1]), int(pos[2])]

    save_stasset(str(dst_path), voxels, skeleton)

    print(f"Fitted colliders to assigned voxels: {dst_path}")
    for bone in bones:
        bmin = bone["voxel_bounds_min"]
        bmax = bone["voxel_bounds_max"]
        size = [bmax[i] - bmin[i] + 1 for i in range(3)]
        print(f"  {bone['name']:20s} bounds={bmin}..{bmax}  size={size}")


def main():
    parser = argparse.ArgumentParser(
        description="Resize bone colliders to fit the voxels actually assigned to each bone."
    )
    default_src = str(SCRIPT_DIR / "../../My project/Assets/StreamingAssets/ActorBone_Ragdoll_WaistTest.stasset")
    default_dst = str(SCRIPT_DIR / "../../My project/Assets/StreamingAssets/ActorBone_Ragdoll_WaistTest_Fitted.stasset")
    parser.add_argument("src", nargs="?", default=default_src, help="Source .stasset file.")
    parser.add_argument("dst", nargs="?", default=default_dst, help="Destination .stasset file.")
    args = parser.parse_args()

    fit_colliders_to_voxels(args.src, args.dst)


if __name__ == "__main__":
    main()
