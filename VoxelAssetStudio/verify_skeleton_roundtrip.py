"""
Round-trip verification for the .stasset v2 skeleton format.

Generates a T-pose skeleton, saves it (v2), loads it back, and asserts that
voxels and the full rig (bones + joints) survive the round-trip intact.
Also verifies a v1 (voxels-only) save still loads with skeleton=None.

Run from the VoxelAssetStudio folder:  python verify_skeleton_roundtrip.py
"""

import os
import tempfile

import numpy as np

from stasset_io import save_stasset, load_stasset
from skeleton_generator_tpose import generate_tpose_biped_skeleton, BONE_MATERIAL, JOINT_MATERIAL


def check(label, condition):
    status = "PASS" if condition else "FAIL"
    print(f"  [{status}] {label}")
    return condition


def main():
    all_ok = True
    tmpdir = tempfile.mkdtemp(prefix="stasset_verify_")

    # --- Generate ---
    print("== Generate T-pose ==")
    voxels, skeleton = generate_tpose_biped_skeleton()
    bones = skeleton['bones']
    joints = skeleton['joints']
    print(f"  bones={len(bones)} joints={len(joints)} "
          f"bone_voxels={int(np.count_nonzero(voxels == BONE_MATERIAL))} "
          f"joint_voxels={int(np.count_nonzero(voxels == JOINT_MATERIAL))}")

    all_ok &= check("bones list is non-empty", len(bones) > 0)
    all_ok &= check("joints list is non-empty", len(joints) > 0)
    all_ok &= check("bone voxels present in volume",
                    np.count_nonzero(voxels == BONE_MATERIAL) > 0)
    all_ok &= check("joint voxels present in volume",
                    np.count_nonzero(voxels == JOINT_MATERIAL) > 0)

    # Referential integrity: every bone's parent/child joint id must exist (or be None)
    joint_ids = {j['id'] for j in joints}
    refs_ok = True
    for b in bones:
        for key in ('parent_joint', 'child_joint'):
            ref = b[key]
            if ref is not None and ref not in joint_ids:
                refs_ok = False
                print(f"    bone '{b['name']}' has dangling {key}={ref}")
    all_ok &= check("all bone->joint references resolve", refs_ok)

    # Unique ids
    all_ok &= check("bone ids are unique", len({b['id'] for b in bones}) == len(bones))
    all_ok &= check("joint ids are unique", len(joint_ids) == len(joints))

    # --- Hierarchy: must be a single tree rooted at the pelvis ---
    print("== Hierarchy (single pelvis-rooted tree) ==")
    root_joint = skeleton.get('root_joint')
    all_ok &= check("root_joint is present", root_joint is not None and root_joint in joint_ids)

    # child_joint -> bone(s). A valid parent reference must resolve to exactly one bone.
    child_to_bone = {}
    dup_child = False
    for b in bones:
        cj = b['child_joint']
        if cj is None:
            continue
        if cj in child_to_bone:
            dup_child = True
            print(f"    joint {cj} is child_joint of multiple bones "
                  f"('{child_to_bone[cj]['name']}' and '{b['name']}')")
        child_to_bone[cj] = b
    all_ok &= check("each joint is the child_joint of at most one bone", not dup_child)

    # Root bones = parent_joint == root_joint. Every other bone must resolve to a parent bone.
    root_bones = [b for b in bones if b['parent_joint'] == root_joint]
    all_ok &= check("at least one root-attached bone", len(root_bones) > 0)

    parent_ok = True
    for b in bones:
        if b['parent_joint'] == root_joint:
            continue
        if b['parent_joint'] not in child_to_bone:
            parent_ok = False
            print(f"    bone '{b['name']}' parent_joint={b['parent_joint']} resolves to no parent bone")
    all_ok &= check("every non-root bone resolves to a parent bone", parent_ok)

    # Connectivity: walk from root bones down through child_joint links, reach every bone.
    reached = set()
    frontier = [b['id'] for b in root_bones]
    reached.update(frontier)
    # bones keyed by their parent_joint for downward traversal
    by_parent_joint = {}
    for b in bones:
        by_parent_joint.setdefault(b['parent_joint'], []).append(b)
    while frontier:
        nxt = []
        for bid in frontier:
            bone = bones[bid]
            cj = bone['child_joint']
            if cj is None:
                continue
            for child in by_parent_joint.get(cj, []):
                if child['id'] not in reached:
                    reached.add(child['id'])
                    nxt.append(child['id'])
        frontier = nxt
    all_ok &= check("tree reaches every bone from the root", len(reached) == len(bones))
    if len(reached) != len(bones):
        missing = [b['name'] for b in bones if b['id'] not in reached]
        print(f"    unreached bones: {missing}")

    # --- Save v2 + reload ---
    print("== Save (v2) and reload ==")
    path = os.path.join(tmpdir, "Tpose.stasset")
    save_stasset(path, voxels, skeleton)
    loaded_voxels, dims, loaded_skel = load_stasset(path)

    all_ok &= check("voxels round-trip exactly", np.array_equal(voxels, loaded_voxels))
    all_ok &= check("dims match", tuple(dims) == tuple(voxels.shape))
    all_ok &= check("skeleton present after load", loaded_skel is not None)
    if loaded_skel is not None:
        all_ok &= check("root_joint preserved",
                        loaded_skel.get('root_joint') == skeleton.get('root_joint'))
        all_ok &= check("bone count preserved", len(loaded_skel['bones']) == len(bones))
        all_ok &= check("joint count preserved", len(loaded_skel['joints']) == len(joints))
        # Deep-compare a representative bone and joint
        all_ok &= check("first bone preserved", loaded_skel['bones'][0] == bones[0])
        all_ok &= check("first joint preserved", loaded_skel['joints'][0] == joints[0])
        all_ok &= check("joint types preserved",
                        [j['type'] for j in loaded_skel['joints']] == [j['type'] for j in joints])

    # --- Save v1 (no skeleton) ---
    print("== Save (v1, voxels-only) and reload ==")
    path_v1 = os.path.join(tmpdir, "NoRig.stasset")
    save_stasset(path_v1, voxels)  # no skeleton arg
    _, _, skel_none = load_stasset(path_v1)
    all_ok &= check("v1 file loads with skeleton=None", skel_none is None)

    print()
    print("RESULT:", "ALL CHECKS PASSED ✅" if all_ok else "SOME CHECKS FAILED ❌")
    return 0 if all_ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
