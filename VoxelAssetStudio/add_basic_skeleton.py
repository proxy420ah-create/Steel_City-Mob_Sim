#!/usr/bin/env python3
"""
Add a minimal rigid skeleton to a plain .stasset cube (or any shape).

Usage:
    python add_basic_skeleton.py "path/to/CubePhys.stasset"

Creates a single root joint at the volume center and a single bone whose
voxel bounds cover the whole filled shape. The result is a v2 .stasset
that can be loaded by VoxelActor for physics testing.
"""

import sys
import numpy as np
from stasset_io import load_stasset, save_stasset


def add_basic_skeleton(voxels, skeleton, name="cube"):
    """
    Build a minimal skeleton for a single rigid body.
    """
    if skeleton is not None and skeleton.get("bones"):
        print("File already has a skeleton. Aborting.")
        return None

    filled = np.argwhere(voxels > 0)
    if len(filled) == 0:
        print("No voxels found.")
        return None

    min_b = filled.min(axis=0)
    max_b = filled.max(axis=0)
    center = ((min_b + max_b) / 2).astype(int)

    # Root joint at center of the filled volume
    joints = [{
        "id": 0,
        "name": "root",
        "type": "ROOT",
        "position": [int(center[0]), int(center[1]), int(center[2])],
    }]

    # Single bone covering the whole shape
    bones = [{
        "id": 0,
        "name": name,
        "role": "box",
        "side": "",
        "start": [int(center[0]), int(center[1]), int(center[2])],
        "end": [int(center[0]), int(center[1]) + 1, int(center[2])],
        "length": 1.0,
        "mass": max(1.0, float(len(filled)) * 0.1),
        "parent_joint": 0,
        "child_joint": None,
        "voxel_bounds_min": [int(min_b[0]), int(min_b[1]), int(min_b[2])],
        "voxel_bounds_max": [int(max_b[0]), int(max_b[1]), int(max_b[2])],
    }]

    return {
        "version": 2,
        "root_joint": 0,
        "bones": bones,
        "joints": joints,
        "influence_map": {},
        "attachments": [],
        "materials": {},
    }


def main():
    if len(sys.argv) < 2:
        print("Usage: python add_basic_skeleton.py <input.stasset> [output.stasset]")
        sys.exit(1)

    input_path = sys.argv[1]
    output_path = sys.argv[2] if len(sys.argv) > 2 else input_path

    voxels, dims, skeleton = load_stasset(input_path)
    new_skeleton = add_basic_skeleton(voxels, skeleton)
    if new_skeleton is None:
        sys.exit(1)

    save_stasset(output_path, voxels, new_skeleton)
    print("\nYou can now load this .stasset into Unity with VoxelActor.")
    print("Set VoxelObject.assetFileName to this file and ensure")
    print("registerWithVoxelWorld is unchecked for the actor.")


if __name__ == "__main__":
    main()
