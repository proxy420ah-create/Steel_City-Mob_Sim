"""
Steel Tide Voxel Asset Studio
Toolbox: stasset_add_simple_spine.py

Adds a minimal skeleton (one root joint + one long spine bone) to a skeleton-free
.stasset model. Useful when a physics system needs at least a basic skeleton
block to register the asset, even though the voxel model itself is bone-only.
"""

import argparse
import sys
from pathlib import Path

import numpy as np

SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPT_DIR.parent))
from stasset_io import load_stasset, save_stasset


SKELETON_VERSION = 2


def make_simple_spine_skeleton(voxels, dims):
    """
    Build a minimal skeleton with a single root joint at the pelvis and a
    single spine bone running up the center of the voxel model.
    """
    width, height, depth = dims
    center_x = width // 2
    center_z = depth // 2

    # Find the lowest and highest occupied y positions along the center spine.
    occupied_y = []
    for y in range(height):
        if voxels[center_x, y, center_z] != 0:
            occupied_y.append(y)

    if not occupied_y:
        raise ValueError("No voxels found along the central spine column")

    pelvis_y = min(occupied_y)
    head_y = max(occupied_y)

    # Root joint at the pelvis.
    pelvis_joint = {
        "id": 0,
        "name": "pelvis_root",
        "type": "ROOT",
        "position": [center_x, pelvis_y, center_z],
        "voxel_bounds_min": [center_x, pelvis_y, center_z],
        "voxel_bounds_max": [center_x, pelvis_y, center_z],
    }

    # One long spine bone from pelvis to top of head.
    spine_bone = {
        "id": 0,
        "name": "spine",
        "role": "SPINE",
        "side": "CENTER",
        "start": [center_x, pelvis_y, center_z],
        "end": [center_x, head_y, center_z],
        "length": float(head_y - pelvis_y),
        "mass": 1.0,
        "parent_joint": 0,
        "child_joint": None,
        "voxel_bounds_min": [center_x, pelvis_y, center_z],
        "voxel_bounds_max": [center_x, head_y, center_z],
    }

    skeleton = {
        "version": SKELETON_VERSION,
        "root_joint": 0,
        "bones": [spine_bone],
        "joints": [pelvis_joint],
        "influence_map": {},
        "attachments": [],
        "materials": {},
    }
    return skeleton


def add_simple_spine(src_path, dst_path):
    src_path = Path(src_path).resolve()
    dst_path = Path(dst_path).resolve()

    if not src_path.exists():
        raise FileNotFoundError(src_path)

    voxels, dims, _ = load_stasset(str(src_path))
    skeleton = make_simple_spine_skeleton(voxels, dims)

    save_stasset(str(dst_path), voxels, skeleton)
    print(f"Added simple spine skeleton to {dst_path}")
    print(f"  Bones: {len(skeleton['bones'])}")
    print(f"  Joints: {len(skeleton['joints'])}")
    print(f"  Spine bone: y={skeleton['bones'][0]['start'][1]} -> y={skeleton['bones'][0]['end'][1]}")


def main():
    parser = argparse.ArgumentParser(
        description="Add a minimal spine skeleton to a skeleton-free .stasset model."
    )
    default_src = str(SCRIPT_DIR / "../../My project/Assets/StreamingAssets/ActorBone_Connected.stasset")
    default_dst = str(SCRIPT_DIR / "../../My project/Assets/StreamingAssets/ActorBone_Connected.stasset")
    parser.add_argument("src", nargs="?", default=default_src, help="Source .stasset file.")
    parser.add_argument("dst", nargs="?", default=default_dst, help="Destination .stasset file.")
    args = parser.parse_args()

    add_simple_spine(args.src, args.dst)


if __name__ == "__main__":
    main()
