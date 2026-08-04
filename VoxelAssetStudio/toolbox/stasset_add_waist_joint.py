"""
Steel Tide Voxel Asset Studio
Toolbox: stasset_add_waist_joint.py

Creates the first ragdoll iteration of the bone-only actor: a two-bone skeleton
with a single waist joint. The pelvis body carries the hips and legs; the upper
spine body carries the torso, arms, neck, and head.

Use case:
  The smallest possible step from "rigid statue" to "ragdoll": test that one
  joint bends correctly under gravity/impact before adding more joints.
"""

import argparse
import sys
from pathlib import Path

import numpy as np

SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPT_DIR.parent))
from stasset_io import load_stasset, save_stasset


SKELETON_VERSION = 2


def make_waist_skeleton(voxels, dims):
    """
    Build a two-bone skeleton:
      - pelvis_root joint (ROOT) at the hips
      - waist joint (BALL) between pelvis and upper spine
      - pelvis bone from pelvis to waist
      - upper_spine bone from waist to top of head
    """
    width, height, depth = dims
    center_x = width // 2
    center_z = depth // 2

    # Find the lowest and highest occupied y along the center spine.
    occupied_y = [y for y in range(height) if voxels[center_x, y, center_z] != 0]
    if not occupied_y:
        raise ValueError("No voxels found along the central spine column")

    pelvis_y = min(occupied_y)
    head_y = max(occupied_y)

    # Waist is roughly where the torso narrows above the hips.
    # Use a point about 40% up the spine from the pelvis.
    waist_y = int(pelvis_y + (head_y - pelvis_y) * 0.4)
    # Ensure the waist is at least one voxel above the pelvis.
    if waist_y <= pelvis_y:
        waist_y = min(pelvis_y + 1, head_y - 1)

    # Joints
    pelvis_joint = {
        "id": 0,
        "name": "pelvis_root",
        "type": "ROOT",
        "position": [center_x, pelvis_y, center_z],
        "voxel_bounds_min": [center_x, pelvis_y, center_z],
        "voxel_bounds_max": [center_x, pelvis_y, center_z],
    }

    waist_joint = {
        "id": 1,
        "name": "waist",
        "type": "BALL",
        "position": [center_x, waist_y, center_z],
        "max_angle_x": 30.0,
        "max_angle_y": 30.0,
        "max_angle_z": 30.0,
        "voxel_bounds_min": [center_x, waist_y, center_z],
        "voxel_bounds_max": [center_x, waist_y, center_z],
    }

    # Bones
    pelvis_bone = {
        "id": 0,
        "name": "pelvis",
        "role": "pelvis",
        "side": "CENTER",
        "start": [center_x, pelvis_y, center_z],
        "end": [center_x, waist_y, center_z],
        "length": float(waist_y - pelvis_y),
        "mass": 1.0,
        "parent_joint": 0,
        "child_joint": 1,
        "voxel_bounds_min": [center_x, pelvis_y, center_z],
        "voxel_bounds_max": [center_x, waist_y, center_z],
    }

    upper_spine_bone = {
        "id": 1,
        "name": "upper_spine",
        "role": "spine",
        "side": "CENTER",
        "start": [center_x, waist_y, center_z],
        "end": [center_x, head_y, center_z],
        "length": float(head_y - waist_y),
        "mass": 1.0,
        "parent_joint": 1,
        "child_joint": None,
        "voxel_bounds_min": [center_x, waist_y, center_z],
        "voxel_bounds_max": [center_x, head_y, center_z],
    }

    skeleton = {
        "version": SKELETON_VERSION,
        "root_joint": 0,
        "bones": [pelvis_bone, upper_spine_bone],
        "joints": [pelvis_joint, waist_joint],
        "influence_map": {},
        "attachments": [],
        "materials": {},
    }
    return skeleton


def add_waist_joint(src_path, dst_path):
    src_path = Path(src_path).resolve()
    dst_path = Path(dst_path).resolve()

    if not src_path.exists():
        raise FileNotFoundError(src_path)

    voxels, dims, _ = load_stasset(str(src_path))
    skeleton = make_waist_skeleton(voxels, dims)

    save_stasset(str(dst_path), voxels, skeleton)
    print(f"Added waist-joint ragdoll skeleton to {dst_path}")
    print(f"  Bones: {len(skeleton['bones'])}")
    print(f"  Joints: {len(skeleton['joints'])}")
    print(f"  Waist at y={skeleton['joints'][1]['position'][1]}")
    print(f"  Pelvis bone: y={skeleton['bones'][0]['start'][1]} -> y={skeleton['bones'][0]['end'][1]}")
    print(f"  Upper spine bone: y={skeleton['bones'][1]['start'][1]} -> y={skeleton['bones'][1]['end'][1]}")


def main():
    parser = argparse.ArgumentParser(
        description="Add a two-bone pelvis/upper-spine ragdoll skeleton with a waist joint."
    )
    default_src = str(SCRIPT_DIR / "../../My project/Assets/StreamingAssets/ActorBone_Connected.stasset")
    default_dst = str(SCRIPT_DIR / "../../My project/Assets/StreamingAssets/ActorBone_Ragdoll_WaistTest.stasset")
    parser.add_argument("src", nargs="?", default=default_src, help="Source .stasset file.")
    parser.add_argument("dst", nargs="?", default=default_dst, help="Destination .stasset file.")
    args = parser.parse_args()

    add_waist_joint(args.src, args.dst)


if __name__ == "__main__":
    main()
