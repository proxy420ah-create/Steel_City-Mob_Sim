"""
Steel Tide Voxel Asset Studio
Toolbox: stasset_bone_only_clone.py

Creates a bone-only, skeleton-free clone of a .stasset model.

Use case:
  Rigid-body physics debugging in Unity. Some test rigs need a model with
  nothing but bone voxels (material 12) and no embedded skeleton metadata so
  the physics system can treat the whole voxel volume as a single rigid
  visual hull without any joint/rig interpretation.
"""

import argparse
import sys
import uuid
from pathlib import Path

import numpy as np

SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPT_DIR.parent))
from stasset_io import load_stasset, save_stasset


BONE_MATERIAL = 12


def clone_bone_only(src_path, dst_path):
    """
    Load src_path, convert every non-air voxel to bone material, strip the
    skeleton, and save to dst_path as a v1 .stasset file.
    """
    src_path = Path(src_path).resolve()
    dst_path = Path(dst_path).resolve()

    if not src_path.exists():
        raise FileNotFoundError(src_path)

    voxels, dims, _ = load_stasset(str(src_path))

    before_bone = int(np.count_nonzero(voxels == BONE_MATERIAL))
    before_joint = int(np.count_nonzero(voxels == 21))
    before_air = int(np.count_nonzero(voxels == 0))

    # Convert every non-air voxel to bone material.
    voxels[voxels != 0] = BONE_MATERIAL

    after_bone = int(np.count_nonzero(voxels == BONE_MATERIAL))

    # Save without skeleton => v1 file with no SKEL block.
    save_stasset(str(dst_path), voxels, skeleton=None)

    # Generate a Unity .meta file so the asset imports cleanly.
    meta_path = dst_path.with_suffix(dst_path.suffix + ".meta")
    meta_path.write_text(
        f"fileFormatVersion: 2\n"
        f"guid: {uuid.uuid4().hex}\n"
        f"DefaultImporter:\n"
        f"  externalObjects: {{}}\n"
        f"  userData: \n"
        f"  assetBundleName: \n"
        f"  assetBundleVariant: \n"
    )

    return {
        "before_bone": before_bone,
        "before_joint": before_joint,
        "before_air": before_air,
        "after_bone": after_bone,
    }


def main():
    parser = argparse.ArgumentParser(
        description="Create a bone-only, skeleton-free .stasset clone."
    )
    default_src = str(SCRIPT_DIR / "../../My project/Assets/StreamingAssets/ActorSymmetric.stasset")
    default_dst = str(SCRIPT_DIR / "../../My project/Assets/StreamingAssets/ActorBone.stasset")
    parser.add_argument("src", nargs="?", default=default_src, help="Source .stasset file.")
    parser.add_argument("dst", nargs="?", default=default_dst, help="Destination .stasset file.")
    args = parser.parse_args()

    stats = clone_bone_only(args.src, args.dst)
    print(f"\nSource: {args.src}")
    print(f"Destination: {args.dst}")
    print(f"Bone voxels: {stats['before_bone']} -> {stats['after_bone']}")
    print(f"Joint voxels converted: {stats['before_joint']}")
    print(f"Skeleton: removed")
    print(f"Unity meta: {Path(args.dst).resolve().with_suffix('.stasset.meta')}")


if __name__ == "__main__":
    main()
