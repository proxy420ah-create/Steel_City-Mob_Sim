"""
Steel Tide Voxel Asset Studio
Toolbox: stasset_rigid_limb_skeleton.py

Copies the full skeleton from the original rigged actor (e.g., ActorSymmetric)
and applies it to a bone-only voxel model (e.g., ActorBone_Connected), but with
all joints stiffened to zero rotation limits.

Result: a rigid body that still has proper arm/leg/torso colliders because each
limb is its own bone. Useful as a sanity check before unlocking joints and
turning the model into a real ragdoll.
"""

import argparse
import copy
import sys
from pathlib import Path

import numpy as np

SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPT_DIR.parent))
from stasset_io import load_stasset, save_stasset


def stiffen_skeleton(skeleton, hard_lock=False):
    """
    Return a deep copy of the skeleton with all non-ROOT joints locked.

    Two modes:
      - Normal: BALL/HINGE joints keep their type but get 0 angle limits.
                This is "limited to 0" in Unity, which still has some spring.
      - Hard lock: non-ROOT joints are changed to type ROOT. This causes
                VoxelRagdoll to set ConfigurableJointMotion.Locked for all
                angular axes, making the chain truly rigid.
    """
    skeleton = copy.deepcopy(skeleton)
    for joint in skeleton.get("joints", []):
        joint_type = joint.get("type", "").upper()
        if joint_type == "ROOT":
            continue

        if hard_lock:
            joint["type"] = "ROOT"
            # Remove angle fields that no longer apply.
            joint.pop("min_angle", None)
            joint.pop("max_angle", None)
            joint.pop("max_angle_x", None)
            joint.pop("max_angle_y", None)
            joint.pop("max_angle_z", None)
            joint.pop("axis", None)
        else:
            if joint_type == "HINGE":
                joint["min_angle"] = 0.0
                joint["max_angle"] = 0.0
            else:
                # BALL and anything else
                joint["max_angle_x"] = 0.0
                joint["max_angle_y"] = 0.0
                joint["max_angle_z"] = 0.0
    return skeleton


def apply_rigid_limb_skeleton(voxel_src, skeleton_src, dst_path, hard_lock=False):
    voxel_src = Path(voxel_src).resolve()
    skeleton_src = Path(skeleton_src).resolve()
    dst_path = Path(dst_path).resolve()

    if not voxel_src.exists():
        raise FileNotFoundError(voxel_src)
    if not skeleton_src.exists():
        raise FileNotFoundError(skeleton_src)

    voxels, dims, _ = load_stasset(str(voxel_src))
    _, _, skeleton = load_stasset(str(skeleton_src))
    if skeleton is None:
        raise ValueError(f"Skeleton source has no skeleton: {skeleton_src}")

    skeleton = stiffen_skeleton(skeleton, hard_lock=hard_lock)
    save_stasset(str(dst_path), voxels, skeleton)

    mode = "hard-locked" if hard_lock else "0-limit"
    print(f"Applied {mode} limb skeleton to {dst_path}")
    print(f"  Voxels: {int(np.count_nonzero(voxels))}")
    print(f"  Bones: {len(skeleton['bones'])}")
    print(f"  Joints: {len(skeleton['joints'])}")
    locked = sum(1 for j in skeleton["joints"] if j.get("type", "").upper() != "ROOT")
    print(f"  Non-ROOT joints: {locked}")


def main():
    parser = argparse.ArgumentParser(
        description="Copy a full skeleton onto a bone-only model with all joints stiffened."
    )
    default_voxel_src = str(SCRIPT_DIR / "../../My project/Assets/StreamingAssets/ActorBone_Connected.stasset")
    default_skeleton_src = str(SCRIPT_DIR / "../../My project/Assets/StreamingAssets/ActorSymmetric.stasset")
    default_dst = str(SCRIPT_DIR / "../../My project/Assets/StreamingAssets/ActorBone_RigidLimbs.stasset")
    parser.add_argument("voxel_src", nargs="?", default=default_voxel_src, help="Bone-only voxel model.")
    parser.add_argument("skeleton_src", nargs="?", default=default_skeleton_src, help="Rigged model to copy skeleton from.")
    parser.add_argument("dst", nargs="?", default=default_dst, help="Destination .stasset file.")
    parser.add_argument("--hard-lock", action="store_true", help="Change non-ROOT joints to ROOT type for truly locked ConfigurableJoints.")
    args = parser.parse_args()

    apply_rigid_limb_skeleton(args.voxel_src, args.skeleton_src, args.dst, hard_lock=args.hard_lock)


if __name__ == "__main__":
    main()
