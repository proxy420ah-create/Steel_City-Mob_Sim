"""
Steel Tide Voxel Asset Studio
Toolbox: stasset_inspector.py

Lightweight ASCII inspector for .stasset voxel actors.

What it prints:
  - Front view (X-Y plane, collapsed Z) with material symbols.
  - Side view (Z-Y plane, collapsed X).
  - Symmetry report per Y-slice.
  - Bone-width analysis: flags any bone column that is 2 or more voxels wide.
  - Hole detector: finds gaps in vertical bone columns.

Why this exists:
  Trimming and inspecting voxel skeletons is hard when you can only see the raw
  coordinates. This script turns the voxel grid into a console-friendly picture
  so problems (even-width limbs, asymmetry, holes) are obvious at a glance.
"""

import argparse
import sys
from pathlib import Path

import numpy as np

SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPT_DIR.parent))
from stasset_io import load_stasset


# ---------------------------------------------------------------------------
# Symbols and colors
# ---------------------------------------------------------------------------
MATERIAL_SYMBOLS = {
    0: " ",   # Air
    12: "B",  # Bone
    21: "J",  # Joint
}


def symbol_for(mat_id):
    """Return a single-character symbol for a material ID."""
    return MATERIAL_SYMBOLS.get(mat_id, str(mat_id)[-1])


# ---------------------------------------------------------------------------
# View renderers
# ---------------------------------------------------------------------------
def render_front_view(voxels, dims):
    """
    Front view: X across, Y down. Collapse Z by taking the first non-air
    material encountered along the Z axis.
    """
    lines = []
    lines.append("Front view (X=0..15 left-to-right, Y=0..19 bottom-to-top)")
    lines.append("Legend: B=Bone, J=Joint, digit=other material")
    lines.append("+" + "-" * dims[0] + "+")
    for y in range(dims[1] - 1, -1, -1):
        row = []
        for x in range(dims[0]):
            col = voxels[x, y, :]
            non_air = col[col != 0]
            if len(non_air) == 0:
                row.append(" ")
            else:
                row.append(symbol_for(non_air[0]))
        lines.append("|" + "".join(row) + "|")
    lines.append("+" + "-" * dims[0] + "+")
    return "\n".join(lines)


def render_side_view(voxels, dims):
    """
    Side view: Z across, Y down. Collapse X by taking the first non-air
    material encountered along the X axis.
    """
    lines = []
    lines.append("Side view (Z=0..7 left-to-right, Y=0..19 bottom-to-top)")
    lines.append("+" + "-" * dims[2] + "+")
    for y in range(dims[1] - 1, -1, -1):
        row = []
        for z in range(dims[2]):
            col = voxels[:, y, z]
            non_air = col[col != 0]
            if len(non_air) == 0:
                row.append(" ")
            else:
                row.append(symbol_for(non_air[0]))
        lines.append("|" + "".join(row) + "|")
    lines.append("+" + "-" * dims[2] + "+")
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Analysis
# ---------------------------------------------------------------------------
def symmetry_report(voxels, dims, center_x=None):
    """
    Compare left vs right voxel counts for each Y-slice. Reports any slice
    where the counts differ.
    """
    if center_x is None:
        center_x = dims[0] // 2

    lines = []
    lines.append(f"Symmetry axis: x={center_x}")
    lines.append("Y-slice | Left voxels | Right voxels | Match")
    lines.append("-" * 50)

    mismatches = 0
    for y in range(dims[1]):
        left = np.count_nonzero(voxels[0:center_x, y, :])
        right = np.count_nonzero(voxels[center_x + 1:dims[0], y, :])
        # Center column is counted separately; if it exists it should be 1 voxel.
        center = np.count_nonzero(voxels[center_x, y, :])
        match = left == right
        if not match or center > 1:
            mismatches += 1
            status = "MISMATCH" if not match else "CENTER>1"
            lines.append(f"y={y:2d}    | {left:11d} | {right:12d} | {status}")

    if mismatches == 0:
        lines.append("All slices are symmetric.")
    else:
        lines.append(f"Found {mismatches} asymmetric slice(s).")
    return "\n".join(lines)


def bone_width_report(voxels, dims, skeleton, center_x=None):
    """
    For each bone, measure how many unique X-coordinates its voxels occupy.

    - Width 1 is ideal: the bone line runs through the center of the voxel.
    - Odd widths (3, 5...) are acceptable if centered; they are flagged as
      informational only.
    - Even widths (2, 4...) are the problem: the bone line sits on the edge
      of an even-width column and appears off-center.
    """
    if center_x is None:
        center_x = dims[0] // 2

    lines = []
    lines.append("Bone width analysis (X-axis voxel span):")
    lines.append(f"{'Bone':20s} | {'x span':8s} | {'Status':15s}")
    lines.append("-" * 50)

    even_issues = 0
    for bone in skeleton.get("bones", []):
        bmin = bone["voxel_bounds_min"]
        bmax = bone["voxel_bounds_max"]
        # X-width is inclusive.
        x_width = bmax[0] - bmin[0] + 1
        if x_width <= 1:
            status = "OK"
        elif x_width % 2 == 0:
            status = "EVEN WIDE"
            even_issues += 1
        else:
            status = "ODD WIDE"
        lines.append(f"{bone['name']:20s} | {x_width:8d} | {status:15s}")

    lines.append("-" * 50)
    if even_issues == 0:
        lines.append("No even-width bones — bone lines are centered.")
    else:
        lines.append(f"{even_issues} bone(s) have even X-width — bone line sits off-center.")
    return "\n".join(lines)


def skeleton_hierarchy_report(skeleton):
    """Print bone hierarchy with parent_bone and collider_only annotations."""
    bones = skeleton.get("bones", [])
    joints = skeleton.get("joints", [])
    root_joint = skeleton.get("root_joint")

    joint_by_id = {j["id"]: j for j in joints}
    bone_by_name = {b["name"]: b for b in bones}
    bone_by_child_joint = {}
    for b in bones:
        cj = b.get("child_joint")
        if cj is not None:
            bone_by_child_joint[cj] = b

    def get_parent(b):
        pb = b.get("parent_bone")
        if pb and pb in bone_by_name:
            return bone_by_name[pb]
        pj = b.get("parent_joint")
        if pj is None or pj == root_joint:
            return None
        return bone_by_child_joint.get(pj)

    lines = []
    lines.append("Skeleton hierarchy:")
    lines.append(f"  Root joint: {root_joint} ({joint_by_id.get(root_joint, {}).get('name', '?')})")
    lines.append(f"  Bones: {len(bones)}, Joints: {len(joints)}")

    rb_count = sum(1 for b in bones if not b.get("collider_only", False))
    co_count = sum(1 for b in bones if b.get("collider_only", False))
    lines.append(f"  Rigidbody bones: {rb_count}, Collider-only bones: {co_count}")
    lines.append("")

    for b in bones:
        parent = get_parent(b)
        parent_name = parent["name"] if parent else "root"
        co = " [collider-only]" if b.get("collider_only", False) else ""
        pj = b.get("parent_joint")
        cj = b.get("child_joint")
        pj_name = joint_by_id.get(pj, {}).get("name", "-") if pj is not None else "-"
        cj_name = joint_by_id.get(cj, {}).get("name", "-") if cj is not None else "-"
        lines.append(f"  {b['name']:20s} parent={parent_name:20s} pj={pj_name:15s} cj={cj_name:15s}{co}")

    return "\n".join(lines)


def hole_report(voxels, dims, skeleton):
    """
    Walk the central spine/head column and each leg column, looking for gaps
    inside a single continuous vertical segment. A segment is a run of
    consecutive occupied voxels. Gaps between two separate segments (e.g.,
    a leg ending at y=7 and an arm starting at y=13) are not counted.
    """
    lines = []
    lines.append("Vertical hole detection (within connected segments):")
    lines.append("-" * 40)

    columns_to_check = []
    # Spine/head at x=8 across all Z slices.
    for z in range(dims[2]):
        columns_to_check.append((8, z, "spine/head"))
    # Left and right leg columns.
    for z in range(dims[2]):
        columns_to_check.append((6, z, "left leg"))
        columns_to_check.append((10, z, "right leg"))

    holes_found = 0
    for x, z, label in columns_to_check:
        col = voxels[x, :, z]
        occupied = np.where(col != 0)[0]
        if len(occupied) < 2:
            continue

        # Find connected segments (consecutive y values).
        segments = []
        start = occupied[0]
        prev = occupied[0]
        for y in occupied[1:]:
            if y == prev + 1:
                prev = y
            else:
                segments.append((start, prev))
                start = y
                prev = y
        segments.append((start, prev))

        # Check each segment for internal holes.
        for seg_start, seg_end in segments:
            if seg_start == seg_end:
                continue  # Single voxel segment has no internal holes.
            for y in range(seg_start, seg_end + 1):
                if col[y] == 0:
                    holes_found += 1
                    lines.append(f"  Hole at ({x},{y},{z}) in {label}")

    if holes_found == 0:
        lines.append("No vertical holes detected in spine/head/legs.")
    else:
        lines.append(f"Total holes found: {holes_found}")
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
def inspect(filepath, compact=False):
    filepath = Path(filepath).resolve()
    if not filepath.exists():
        raise FileNotFoundError(filepath)

    voxels, dims, skeleton = load_stasset(str(filepath))

    sections = []
    num_bones = len(skeleton.get("bones", [])) if skeleton else 0
    num_joints = len(skeleton.get("joints", [])) if skeleton else 0

    sections.append(f"File: {filepath}")
    sections.append(f"Dimensions: {dims[0]}x{dims[1]}x{dims[2]}")
    sections.append(f"Non-air voxels: {np.count_nonzero(voxels)}")
    sections.append(f"Bones: {num_bones}, Joints: {num_joints}")
    sections.append("")

    if not compact:
        sections.append(render_front_view(voxels, dims))
        sections.append("")
        sections.append(render_side_view(voxels, dims))
        sections.append("")

    sections.append(symmetry_report(voxels, dims))
    sections.append("")
    if skeleton:
        sections.append(bone_width_report(voxels, dims, skeleton))
        sections.append("")
        sections.append(skeleton_hierarchy_report(skeleton))
        sections.append("")
    sections.append(hole_report(voxels, dims, skeleton))

    return "\n".join(sections)


def main():
    parser = argparse.ArgumentParser(
        description="ASCII inspection and analysis for .stasset voxel actors."
    )
    default_path = str(SCRIPT_DIR / "../../My project/Assets/StreamingAssets/ActorSymmetric.stasset")
    parser.add_argument(
        "filepath",
        nargs="?",
        default=default_path,
        help="Path to the .stasset file to inspect.",
    )
    parser.add_argument(
        "--compact", "-c", action="store_true", help="Skip ASCII art; print analysis only."
    )
    args = parser.parse_args()

    print(inspect(args.filepath, compact=args.compact))


if __name__ == "__main__":
    main()
