"""
Steel Tide Voxel Asset Studio
Toolbox: stasset_symmetry_trimmer.py

Reusable symmetry trimmer for .stasset voxel actors.

What it does:
  - Loads a .stasset v2 model (voxel grid + skeleton JSON block).
  - Trims the spine and legs to a configurable voxel width while preserving
    symmetry around the model's central axis.
  - Fills accidental holes left in the vertical bone columns after trimming.
  - Updates skeleton bone/joint voxel bounds to match the new voxel extents.
  - Backs up the original file before overwriting.

Use case:
  The ActorSymmetric model was originally built with a 2-voxel-wide spine and
  legs. Because the bone line sits on the edge of an even-width column, the
  yellow skeleton visualization runs along one side of the limb rather than
  through its center. Trimming to 1 voxel wide centers the bone line inside the
  remaining voxel column.
"""

import argparse
import json
import os
import shutil
import sys
import time
from pathlib import Path

import numpy as np

# Add parent directory so we can import the project's stasset I/O module.
SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPT_DIR.parent))
from stasset_io import load_stasset, save_stasset


# ---------------------------------------------------------------------------
# Configuration defaults
# ---------------------------------------------------------------------------
SPINE_X = 8               # Central column where the spine/head chain lives.
LEFT_LEG_X = 6            # Center column of the left leg.
RIGHT_LEG_X = 10          # Center column of the right leg.
DEFAULT_LEG_WIDTH = 1     # Target voxel width for legs.
DEFAULT_SPINE_WIDTH = 1   # Target voxel width for spine.
BONE_MATERIAL = 12        # Material ID used for bone voxels.


# ---------------------------------------------------------------------------
# Diagnostics
# ---------------------------------------------------------------------------
def print_slice_report(voxels, dims, title="Voxel slice report"):
    """Print a compact Y-slice summary of non-air voxels."""
    print(f"\n{title}")
    print("-" * len(title))
    for y in range(dims[1]):
        entries = []
        for z in range(dims[2]):
            for x in range(dims[0]):
                if voxels[x, y, z] != 0:
                    entries.append(f"({x},{z})={int(voxels[x, y, z])}")
        if entries:
            print(f"y={y:2d}: " + ", ".join(entries))


def print_skeleton_extent(skeleton):
    """Print bone and joint voxel bounds for quick sanity checking."""
    print("\nBone extents:")
    for b in skeleton.get("bones", []):
        print(
            f"  {b['name']:20s} x={b['voxel_bounds_min'][0]}-{b['voxel_bounds_max'][0]}  "
            f"y={b['voxel_bounds_min'][1]}-{b['voxel_bounds_max'][1]}  "
            f"z={b['voxel_bounds_min'][2]}-{b['voxel_bounds_max'][2]}"
        )
    print("\nJoint extents:")
    for j in skeleton.get("joints", []):
        print(
            f"  {j['name']:20s} x={j['voxel_bounds_min'][0]}-{j['voxel_bounds_max'][0]}  "
            f"y={j['voxel_bounds_min'][1]}-{j['voxel_bounds_max'][1]}  "
            f"z={j['voxel_bounds_min'][2]}-{j['voxel_bounds_max'][2]}"
        )


# ---------------------------------------------------------------------------
# Core trimming logic
# ---------------------------------------------------------------------------
def trim_spine(voxels, dims, spine_x=SPINE_X, width=DEFAULT_SPINE_WIDTH,
               head_y_range=(16, 18)):
    """
    Narrow the spine/torso vertical column to `width` voxels centered on
    `spine_x`. Also trim the head so it stays odd-width and centered.
    """
    # Spine runs from lower spine (y=9) up through neck joint (y=15).
    # Pelvis (y=8) is handled by trim_legs().
    for y in range(9, 16):
        for z in range(dims[2]):
            for x in range(dims[0]):
                if voxels[x, y, z] == 0:
                    continue
                if y == 13:
                    # Shoulder level: arms live at x=1-6 and x=10-14.
                    # Remove only the chest filler between arms and spine.
                    if x in (spine_x - 1, spine_x + 1):
                        voxels[x, y, z] = 0
                else:
                    # All other spine levels: keep only the central column.
                    if x != spine_x:
                        voxels[x, y, z] = 0

    # Head: force it to be exactly 3 voxels wide (x=7,8,9) so it remains
    # symmetric around the spine. Anything wider than 3 is trimmed from the
    # outside edges.
    head_min_y, head_max_y = head_y_range
    for y in range(head_min_y, head_max_y + 1):
        for z in range(dims[2]):
            for x in range(dims[0]):
                if voxels[x, y, z] != 0 and x not in (spine_x - 1, spine_x, spine_x + 1):
                    voxels[x, y, z] = 0
    return voxels


def trim_legs(voxels, dims, left_x=LEFT_LEG_X, right_x=RIGHT_LEG_X,
              width=DEFAULT_LEG_WIDTH, pelvis_y=8):
    """
    Narrow each leg to `width` voxels centered on its bone column. At the
    pelvis, keep only the hip connection points so the legs remain attached to
    the spine.
    """
    # Legs run from y=0 up to just below the pelvis.
    for y in range(0, pelvis_y):
        for z in range(dims[2]):
            for x in range(dims[0]):
                if voxels[x, y, z] == 0:
                    continue
                # Left leg: keep only the center column.
                if x < SPINE_X and x != left_x:
                    voxels[x, y, z] = 0
                # Right leg: keep only the center column.
                if x > SPINE_X and x != right_x:
                    voxels[x, y, z] = 0

    # Pelvis: keep hip joints and the spine root, remove everything else.
    for z in range(dims[2]):
        for x in range(dims[0]):
            if voxels[x, pelvis_y, z] != 0 and x not in (left_x, SPINE_X, right_x):
                voxels[x, pelvis_y, z] = 0
    return voxels


def fill_vertical_holes(voxels, dims, spine_x=SPINE_X, material=BONE_MATERIAL):
    """
    After trimming, small gaps can appear where a voxel was missing from the
    original even-width model. Walk the spine/neck column and fill any empty
    cell between two occupied cells in the same (x,z) column.
    """
    for x in (spine_x,):
        for z in range(dims[2]):
            col = voxels[x, :, z]
            occupied = np.where(col != 0)[0]
            if len(occupied) < 2:
                continue
            min_y, max_y = occupied[0], occupied[-1]
            for y in range(min_y, max_y + 1):
                if col[y] == 0:
                    col[y] = material
    return voxels


def update_skeleton_bounds(skeleton, spine_x=SPINE_X, left_x=LEFT_LEG_X,
                           right_x=RIGHT_LEG_X):
    """
    Shrink bone and joint voxel bounds to match the new single-voxel columns.
    Bounding boxes are kept tight so other tooling (physics, raymarch, etc.)
    gets accurate extents.
    """
    bone_updates = {
        "left_thigh": (left_x, left_x),
        "left_shin": (left_x, left_x),
        "left_foot": (left_x, left_x),
        "right_thigh": (right_x, right_x),
        "right_shin": (right_x, right_x),
        "right_foot": (right_x, right_x),
        "spine_lower": (spine_x, spine_x),
        "spine_upper": (spine_x, spine_x),
        "neck": (spine_x, spine_x),
        # Head stays 3 voxels wide, centered on spine.
        "head": (spine_x - 1, spine_x + 1),
    }

    for bone in skeleton.get("bones", []):
        if bone["name"] in bone_updates:
            mn, mx = bone_updates[bone["name"]]
            bone["voxel_bounds_min"][0] = mn
            bone["voxel_bounds_max"][0] = mx

    joint_updates = {
        "left_hip": (left_x, left_x),
        "left_knee": (left_x, left_x),
        "left_ankle": (left_x, left_x),
        "right_hip": (right_x, right_x),
        "right_knee": (right_x, right_x),
        "right_ankle": (right_x, right_x),
        "mid_spine": (spine_x, spine_x),
        "chest": (spine_x, spine_x),
        "neck": (spine_x, spine_x),
    }

    for joint in skeleton.get("joints", []):
        if joint["name"] in joint_updates:
            mn, mx = joint_updates[joint["name"]]
            joint["voxel_bounds_min"][0] = mn
            joint["voxel_bounds_max"][0] = mx
    return skeleton


# ---------------------------------------------------------------------------
# Main workflow
# ---------------------------------------------------------------------------
def trim_actor_symmetric(filepath, dry_run=False, verbose=False, output_path=None):
    """
    Load ActorSymmetric.stasset, trim it symmetrically, and save it.
    If output_path is None, overwrites the original (after backup). Otherwise
    writes to output_path. Returns a dict with before/after statistics.
    """
    filepath = Path(filepath).resolve()
    if not filepath.exists():
        raise FileNotFoundError(filepath)

    voxels, dims, skeleton = load_stasset(str(filepath))
    before = int(np.count_nonzero(voxels))

    save_path = filepath if output_path is None else Path(output_path).resolve()

    if not dry_run and output_path is None:
        # Backup only when overwriting the original file.
        backup_path = filepath.parent / (
            filepath.stem + f".backup_{time.strftime('%Y%m%d_%H%M%S')}" + filepath.suffix
        )
        shutil.copy2(filepath, backup_path)
        print(f"Backup saved: {backup_path}")

    # Perform the trim.
    voxels = trim_spine(voxels, dims)
    voxels = trim_legs(voxels, dims)
    voxels = fill_vertical_holes(voxels, dims)
    skeleton = update_skeleton_bounds(skeleton)

    after = int(np.count_nonzero(voxels))

    if verbose:
        print_slice_report(voxels, dims, title="After trimming")
        print_skeleton_extent(skeleton)

    if not dry_run:
        save_stasset(str(save_path), voxels, skeleton)

    return {
        "before": before,
        "after": after,
        "removed": before - after,
        "dims": dims,
        "bones": len(skeleton.get("bones", [])),
        "joints": len(skeleton.get("joints", [])),
    }


def main():
    parser = argparse.ArgumentParser(
        description="Trim an ActorSymmetric-style .stasset model to 1-voxel-wide spine/legs."
    )
    default_path = str(SCRIPT_DIR / "../../My project/Assets/StreamingAssets/ActorSymmetric.stasset")
    parser.add_argument(
        "filepath",
        nargs="?",
        default=default_path,
        help="Path to the .stasset file to trim.",
    )
    parser.add_argument(
        "--dry-run", action="store_true", help="Analyze only; do not modify the file."
    )
    parser.add_argument(
        "--verbose", "-v", action="store_true", help="Print full slice and skeleton reports."
    )
    parser.add_argument(
        "--output", "-o", help="Write the trimmed result to a new file instead of overwriting the original."
    )
    args = parser.parse_args()

    stats = trim_actor_symmetric(
        args.filepath, dry_run=args.dry_run, verbose=args.verbose, output_path=args.output
    )
    print(f"\nNon-air voxels: {stats['before']} -> {stats['after']} (removed {stats['removed']})")
    print(f"Dimensions: {stats['dims'][0]}x{stats['dims'][1]}x{stats['dims'][2]}")
    print(f"Bones: {stats['bones']}, Joints: {stats['joints']}")


if __name__ == "__main__":
    main()
