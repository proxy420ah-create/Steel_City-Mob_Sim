# Steel Tide: Voxel Asset Studio
# skeleton_generator_actor.py - Generate a stable, human-proportioned actor skeleton
#
# Designed per VOXEL_ACTOR_SYSTEM_DESIGN.md:
#   - 2-3 voxel wide load-bearing bones (legs, pelvis, spine)
#   - 1-2 voxel wide non-load-bearing bones (arms, neck)
#   - Wide flat feet (3 long, 2 wide) for balance controller support polygon
#   - Human-scale height (~18 voxels = 2.25m, slightly taller than 1.8m reference)
#   - Per-bone mass from material density (Bone = 1.8 per voxel)
#   - Full role/side metadata for VoxelActor collider scheme + pose system
#
# Grid: 14 x 20 x 8  (1.75m x 2.5m x 1.0m)
# The skeleton occupies y=0 (feet) to y=18 (head top).

import numpy as np
from typing import Dict, Tuple

from material_library import get_material_mass

# Material IDs
BONE_MATERIAL = 12   # White/Beige bone voxels
JOINT_MATERIAL = 21  # Red joint voxels

# Bone width constants (in voxels) — per design doc recommendations
LEG_WIDTH = 2        # thigh, shin (X)
LEG_DEPTH = 2        # thigh, shin (Z) — 2x2 cross-section
FOOT_LENGTH = 4      # forward from ankle
FOOT_WIDTH = 2       # across
PELVIS_WIDTH = 5     # hip-to-hip (wider stance)
PELVIS_DEPTH = 2     # pelvis depth (Z)
SPINE_WIDTH = 2      # lower + upper spine (X)
SPINE_DEPTH = 2      # spine depth (Z) — 2x2 cross-section
ARM_WIDTH = 2        # upper arm, forearm length in X
ARM_DEPTH = 2        # arm depth (Z) — 2-wide cross-section
NECK_WIDTH = 2       # articulation (X)
NECK_DEPTH = 2       # neck depth (Z)
HEAD_WIDTH = 3       # head width (X)
HEAD_HEIGHT = 3      # head height (Y)
HEAD_DEPTH = 3       # head depth (Z) — blocky roundish shape


def generate_actor_skeleton(grid_size: Tuple[int, int, int] = (16, 20, 8)) -> Tuple[np.ndarray, Dict]:
    """
    Generate a stable, human-proportioned T-pose actor skeleton with wide
    load-bearing bones, flat feet, and full rig metadata.

    Grid layout (16 x 20 x 8):
        y=18  head top
        y=16  head bone (3 wide)
        y=15  neck joint
        y=14  neck bone
        y=13  chest joint + shoulders + arms (T-pose)
        y=10  mid_spine joint
        y=9   spine bone (2 wide)
        y=8   PELVIS ROOT + hips (3 wide)
        y=7   thigh bone (2 wide)
        y=5
        y=4   knee joint
        y=3   shin bone (2 wide)
        y=1
        y=0   ankle joint + foot (3 long, 2 wide)

    Returns:
        (voxels, skeleton_data): Voxel array + skeleton metadata.
    """
    voxels = np.zeros(grid_size, dtype=np.uint16)

    cx = grid_size[0] // 2  # 7
    cz = grid_size[2] // 2  # 4
    width = grid_size[0]

    bones: list = []
    joints: list = []

    # ---- helpers ----

    def add_joint(name, position, jtype, voxel_bounds=None, use_position_for_anchor=False, **extra):
        jid = len(joints)
        joint = {
            'id': jid,
            'name': name,
            'type': jtype,
            'position': [int(position[0]), int(position[1]), int(position[2])],
        }
        if voxel_bounds is not None:
            joint['voxel_bounds_min'] = list(voxel_bounds[0])
            joint['voxel_bounds_max'] = list(voxel_bounds[1])
        if use_position_for_anchor:
            joint['use_position_for_anchor'] = True
        joint.update(extra)
        joints.append(joint)
        return jid

    def add_bone(name, start, end, parent_joint, child_joint, role='', side='',
                 voxel_count_override=None, voxel_bounds=None):
        bid = len(bones)
        start_v = np.array(start, dtype=float)
        end_v = np.array(end, dtype=float)
        length = float(np.linalg.norm(end_v - start_v))
        # Voxel count: use override for multi-voxel-wide bones, else estimate from length.
        if voxel_count_override is not None:
            vc = voxel_count_override
        else:
            vc = max(1, int(round(length)))
        mass = vc * get_material_mass(BONE_MATERIAL)
        bone_dict = {
            'id': bid,
            'name': name,
            'role': role,
            'side': side,
            'start': [int(start[0]), int(start[1]), int(start[2])],
            'end': [int(end[0]), int(end[1]), int(end[2])],
            'length': length,
            'mass': mass,
            'parent_joint': parent_joint,
            'child_joint': child_joint,
        }
        if voxel_bounds is not None:
            bone_dict['voxel_bounds_min'] = list(voxel_bounds[0])
            bone_dict['voxel_bounds_max'] = list(voxel_bounds[1])
        bones.append(bone_dict)
        return bid

    def fill_box(x0, x1, y0, y1, z0, z1, material):
        """Fill a rectangular region with material. Returns (min_xyz, max_xyz)."""
        for x in range(max(0, x0), min(width, x1 + 1)):
            for y in range(max(0, y0), min(grid_size[1], y1 + 1)):
                for z in range(max(0, z0), min(grid_size[2], z1 + 1)):
                    voxels[x, y, z] = material
        return ([x0, y0, z0], [x1, y1, z1])

    def stamp_joint(x, y, z):
        """Stamp a single joint voxel. Returns (min_xyz, max_xyz)."""
        if 0 <= x < width and 0 <= y < grid_size[1] and 0 <= z < grid_size[2]:
            voxels[x, y, z] = JOINT_MATERIAL
        return ([x, y, z], [x, y, z])

    def stamp_joint_wide(x0, x1, y, z):
        """Stamp a multi-voxel-wide joint in X. Returns (min_xyz, max_xyz)."""
        for x in range(max(0, x0), min(width, x1 + 1)):
            if 0 <= y < grid_size[1] and 0 <= z < grid_size[2]:
                voxels[x, y, z] = JOINT_MATERIAL
        return ([x0, y, z], [x1, y, z])

    def stamp_joint_box(x0, x1, y, z0, z1):
        """Stamp a box-shaped joint (X x Z). Returns (min_xyz, max_xyz)."""
        for x in range(max(0, x0), min(width, x1 + 1)):
            for z in range(max(0, z0), min(grid_size[2], z1 + 1)):
                if 0 <= y < grid_size[1]:
                    voxels[x, y, z] = JOINT_MATERIAL
        return ([x0, y, z0], [x1, y, z1])

    # ===== PELVIS ROOT (y=8, 5 voxels wide, 2 deep) =====
    pelvis_y = 8
    pelvis_x0 = cx - PELVIS_WIDTH // 2       # wider stance
    pelvis_x1 = pelvis_x0 + PELVIS_WIDTH - 1
    pelvis_z0 = cz - PELVIS_DEPTH // 2
    pelvis_z1 = pelvis_z0 + PELVIS_DEPTH - 1
    pelvis_bounds = fill_box(pelvis_x0, pelvis_x1, pelvis_y, pelvis_y, pelvis_z0, pelvis_z1, JOINT_MATERIAL)
    pelvis = add_joint('pelvis', (cx, pelvis_y, cz), 'ROOT', voxel_bounds=pelvis_bounds)

    # ===== LEGS (Left and Right) — wider stance, 2x2 cross-section =====
    # Hip joints at pelvis edges, legs go straight down.
    for side, x_off in [('left', -2), ('right', 2)]:
        su = side.upper()[0]  # 'L' or 'R'
        leg_cx = cx + x_off
        leg_x0 = leg_cx - LEG_WIDTH // 2
        leg_x1 = leg_x0 + LEG_WIDTH - 1
        leg_z0 = cz - LEG_DEPTH // 2
        leg_z1 = leg_z0 + LEG_DEPTH - 1

        # Hip joint (2x2 to match leg cross-section)
        hip_x = leg_cx
        hip_bounds = stamp_joint_box(leg_x0, leg_x1, pelvis_y, leg_z0, leg_z1)
        hip = add_joint(f'{side}_hip', (hip_x, pelvis_y, cz), 'BALL',
                        voxel_bounds=hip_bounds,
                        max_angle_x=45.0, max_angle_y=30.0, max_angle_z=30.0)

        # Thigh bone (y=7 down to y=5, 2x2 cross-section)
        thigh_top = pelvis_y - 1   # 7
        thigh_bot = 5
        thigh_bounds = fill_box(leg_x0, leg_x1, thigh_bot, thigh_top, leg_z0, leg_z1, BONE_MATERIAL)
        thigh_voxels = (thigh_top - thigh_bot + 1) * LEG_WIDTH * LEG_DEPTH
        add_bone(f'{side}_thigh', (leg_cx, pelvis_y, cz), (leg_cx, thigh_bot, cz),
                 hip, None, role='thigh', side=su, voxel_count_override=thigh_voxels,
                 voxel_bounds=thigh_bounds)

        # Knee joint (2x2)
        knee_y = thigh_bot  # 5
        knee_bounds = stamp_joint_box(leg_x0, leg_x1, knee_y, leg_z0, leg_z1)
        knee = add_joint(f'{side}_knee', (leg_cx, knee_y, cz), 'HINGE',
                         voxel_bounds=knee_bounds,
                         axis='X', min_angle=-150.0, max_angle=0.0)
        bones[-1]['child_joint'] = knee

        # Shin bone (y=4 down to y=1, 2x2 cross-section)
        shin_top = knee_y - 1  # 4
        shin_bot = 1
        shin_bounds = fill_box(leg_x0, leg_x1, shin_bot, shin_top, leg_z0, leg_z1, BONE_MATERIAL)
        shin_voxels = (shin_top - shin_bot + 1) * LEG_WIDTH * LEG_DEPTH
        add_bone(f'{side}_shin', (leg_cx, knee_y, cz), (leg_cx, shin_bot, cz),
                 knee, None, role='shin', side=su, voxel_count_override=shin_voxels,
                 voxel_bounds=shin_bounds)

        # Ankle joint — 2x2 voxel bounds for proper gizmo sizing, but use explicit position for anchor
        ankle_y = shin_bot  # 1
        ankle_bounds = stamp_joint_box(leg_x0, leg_x1, ankle_y, leg_z0, leg_z1)
        ankle = add_joint(f'{side}_ankle', (leg_cx, ankle_y, cz), 'HINGE',
                          voxel_bounds=ankle_bounds, use_position_for_anchor=True,
                          axis='X', min_angle=-30.0, max_angle=30.0)
        bones[-1]['child_joint'] = ankle

        # Foot bone (flat, 1 voxel tall at y=0, 4 long forward, 2 wide)
        foot_y = 0
        foot_z0 = cz  # starts at ankle position (back of foot)
        foot_z1 = cz + FOOT_LENGTH - 1  # 4 voxels forward
        foot_x0 = leg_cx - FOOT_WIDTH // 2
        foot_x1 = foot_x0 + FOOT_WIDTH - 1
        foot_bounds = fill_box(foot_x0, foot_x1, foot_y, foot_y, foot_z0, foot_z1, BONE_MATERIAL)
        foot_voxels = (foot_z1 - foot_z0 + 1) * (foot_x1 - foot_x0 + 1)
        add_bone(f'{side}_foot', (leg_cx, ankle_y, cz), (leg_cx, foot_y, foot_z1),
                 ankle, None, role='foot', side=su, voxel_count_override=foot_voxels,
                 voxel_bounds=foot_bounds)

    # ===== SPINE (y=9 to y=12, 2x2 cross-section) =====
    spine_y0 = pelvis_y + 1   # 9
    mid_spine_y = 10
    spine_y1 = 12

    sp_x0 = cx - SPINE_WIDTH // 2
    sp_x1 = sp_x0 + SPINE_WIDTH - 1
    sp_z0 = cz - SPINE_DEPTH // 2
    sp_z1 = sp_z0 + SPINE_DEPTH - 1

    # Lower spine: pelvis -> mid_spine (y=9-10, 2x2)
    lower_spine_bounds = fill_box(sp_x0, sp_x1, spine_y0, mid_spine_y, sp_z0, sp_z1, BONE_MATERIAL)
    lower_spine_voxels = (mid_spine_y - spine_y0 + 1) * SPINE_WIDTH * SPINE_DEPTH
    mid_spine_bounds = stamp_joint_box(sp_x0, sp_x1, mid_spine_y, sp_z0, sp_z1)
    mid_spine = add_joint('mid_spine', (cx, mid_spine_y, cz), 'BALL',
                          voxel_bounds=mid_spine_bounds,
                          max_angle_x=30.0, max_angle_y=20.0, max_angle_z=20.0)
    add_bone('spine_lower', (cx, pelvis_y, cz), (cx, mid_spine_y, cz),
             pelvis, mid_spine, role='spine', voxel_count_override=lower_spine_voxels,
             voxel_bounds=lower_spine_bounds)

    # Upper spine: mid_spine -> chest (y=11-12, 2x2)
    upper_spine_bounds = fill_box(sp_x0, sp_x1, mid_spine_y + 1, spine_y1, sp_z0, sp_z1, BONE_MATERIAL)
    upper_spine_voxels = (spine_y1 - (mid_spine_y + 1) + 1) * SPINE_WIDTH * SPINE_DEPTH

    # ===== CHEST / SHOULDERS (y=13) =====
    chest_y = 13
    # Shoulder line: 5 voxels wide, 2 deep
    shoulder_x0 = cx - 2
    shoulder_x1 = cx + 2
    chest_bounds = fill_box(shoulder_x0, shoulder_x1, chest_y, chest_y, sp_z0, sp_z1, BONE_MATERIAL)

    # Chest joint at center (2x2)
    chest_joint_bounds = stamp_joint_box(sp_x0, sp_x1, chest_y, sp_z0, sp_z1)
    chest = add_joint('chest', (cx, chest_y, cz), 'BALL',
                      voxel_bounds=chest_joint_bounds,
                      max_angle_x=20.0, max_angle_y=20.0, max_angle_z=20.0)
    add_bone('spine_upper', (cx, mid_spine_y, cz), (cx, chest_y, cz),
             mid_spine, chest, role='spine', voxel_count_override=upper_spine_voxels,
             voxel_bounds=upper_spine_bounds)

    # Shoulder joints + collar bones (shoulders at pelvis-width edges for wider stance)
    for side, x_off in [('left', -2), ('right', 2)]:
        su = side.upper()[0]
        sh_x = cx + x_off
        sh_z0 = cz - ARM_DEPTH // 2
        sh_z1 = sh_z0 + ARM_DEPTH - 1
        sh_bounds = stamp_joint_box(sh_x - LEG_WIDTH // 2, sh_x + LEG_WIDTH // 2, chest_y, sh_z0, sh_z1)
        shoulder = add_joint(f'{side}_shoulder', (sh_x, chest_y, cz), 'BALL',
                             voxel_bounds=sh_bounds,
                             max_angle_x=180.0, max_angle_y=90.0, max_angle_z=90.0)
        collar_bounds = ([min(cx, sh_x), chest_y, cz], [max(cx, sh_x), chest_y, cz])
        add_bone(f'{side}_collar', (cx, chest_y, cz), (sh_x, chest_y, cz),
                 chest, shoulder, role='collar', side=su,
                 voxel_bounds=collar_bounds)

    # ===== ARMS (T-pose, 2-deep cross-section) =====
    arm_z0 = cz - ARM_DEPTH // 2
    arm_z1 = arm_z0 + ARM_DEPTH - 1
    for side, direction, sh_x in [('left', -1, cx - 2),
                                   ('right', 1, cx + 2)]:
        su = side.upper()[0]
        shoulder_id = next(j['id'] for j in joints if j['name'] == f'{side}_shoulder')

        # Upper arm: 2 voxels outward from shoulder, 2 deep in Z
        arm_x0 = min(sh_x, sh_x + direction * 2)
        arm_x1 = max(sh_x, sh_x + direction * 2)
        upper_arm_bounds = fill_box(arm_x0, arm_x1, chest_y, chest_y, arm_z0, arm_z1, BONE_MATERIAL)
        upper_arm_voxels = (arm_x1 - arm_x0 + 1) * ARM_DEPTH

        # Elbow joint (3 voxels out from shoulder, 2 deep)
        elbow_x = sh_x + direction * 3
        elbow_bounds = stamp_joint_box(elbow_x, elbow_x, chest_y, arm_z0, arm_z1) if 0 <= elbow_x < width else ([elbow_x, chest_y, arm_z0], [elbow_x, chest_y, arm_z1])
        elbow = add_joint(f'{side}_elbow', (elbow_x, chest_y, cz), 'HINGE',
                          voxel_bounds=elbow_bounds,
                          axis='Z', min_angle=0.0, max_angle=150.0)
        add_bone(f'{side}_upper_arm', (sh_x, chest_y, cz), (elbow_x, chest_y, cz),
                 shoulder_id, elbow, role='upper_arm', side=su,
                 voxel_count_override=upper_arm_voxels,
                 voxel_bounds=upper_arm_bounds)

        # Forearm: 2 voxels outward from elbow, 2 deep
        hand_x = elbow_x
        for step in (1, 2):
            fx = elbow_x + direction * step
            if 0 <= fx < width:
                fill_box(fx, fx, chest_y, chest_y, arm_z0, arm_z1, BONE_MATERIAL)
                hand_x = fx
        forearm_bounds = ([min(elbow_x, hand_x), chest_y, arm_z0], [max(elbow_x, hand_x), chest_y, arm_z1])
        forearm_voxels = (max(elbow_x, hand_x) - min(elbow_x, hand_x) + 1) * ARM_DEPTH
        add_bone(f'{side}_forearm', (elbow_x, chest_y, cz), (hand_x, chest_y, cz),
                 elbow, None, role='forearm', side=su,
                 voxel_count_override=forearm_voxels,
                 voxel_bounds=forearm_bounds)

    # ===== NECK (y=14, 2x2 cross-section) =====
    neck_y0 = chest_y + 1  # 14
    neck_y1 = 14
    neck_x0 = cx - NECK_WIDTH // 2
    neck_x1 = neck_x0 + NECK_WIDTH - 1
    neck_z0 = cz - NECK_DEPTH // 2
    neck_z1 = neck_z0 + NECK_DEPTH - 1
    neck_bounds = fill_box(neck_x0, neck_x1, neck_y0, neck_y1, neck_z0, neck_z1, BONE_MATERIAL)
    # Neck joint at top (2x2)
    neck_joint_y = 15
    neck_joint_bounds = stamp_joint_box(neck_x0, neck_x1, neck_joint_y, neck_z0, neck_z1)
    neck = add_joint('neck', (cx, neck_joint_y, cz), 'BALL',
                     voxel_bounds=neck_joint_bounds,
                     max_angle_x=60.0, max_angle_y=45.0, max_angle_z=45.0)
    neck_voxels = (neck_y1 - neck_y0 + 1) * NECK_WIDTH * NECK_DEPTH
    add_bone('neck', (cx, chest_y, cz), (cx, neck_joint_y, cz),
             chest, neck, role='neck', voxel_count_override=neck_voxels,
             voxel_bounds=neck_bounds)

    # ===== HEAD (y=16 to y=18, 3x3x3 block — roundish) =====
    head_y0 = neck_joint_y + 1  # 16
    head_y1 = head_y0 + HEAD_HEIGHT - 1  # 18
    head_x0 = cx - HEAD_WIDTH // 2
    head_x1 = head_x0 + HEAD_WIDTH - 1
    head_z0 = cz - HEAD_DEPTH // 2
    head_z1 = head_z0 + HEAD_DEPTH - 1
    head_bounds = fill_box(head_x0, head_x1, head_y0, head_y1, head_z0, head_z1, BONE_MATERIAL)
    head_voxels = (head_y1 - head_y0 + 1) * HEAD_WIDTH * HEAD_DEPTH
    add_bone('head', (cx, neck_joint_y, cz), (cx, head_y1, cz),
             neck, None, role='head', voxel_count_override=head_voxels,
             voxel_bounds=head_bounds)

    # ===== Assemble skeleton metadata =====
    skeleton_data = {
        'root_joint': pelvis,
        'bones': bones,
        'joints': joints,
        'influence_map': {},
        'attachments': [],
    }

    return voxels, skeleton_data


# ============================================================
# Regeneration helper (call from regen script or CLI)
# ============================================================

def regenerate_streaming_asset(out_path: str) -> None:
    """Generate the actor skeleton and save directly to a .stasset path."""
    import os
    from stasset_io import save_stasset, load_stasset

    voxels, skeleton = generate_actor_skeleton()
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    save_stasset(out_path, voxels, skeleton)

    rv, dims, rs = load_stasset(out_path)
    print(f"Wrote {out_path}")
    print(f"  dims = {dims[0]}x{dims[1]}x{dims[2]}")
    if rs is not None:
        print(f"  rig  = {len(rs['bones'])} bones, {len(rs['joints'])} joints, "
              f"root_joint = {rs.get('root_joint')}")
        total_mass = sum(b.get('mass', 0) for b in rs['bones'])
        print(f"  total bone mass = {total_mass:.1f}")
    else:
        print("  rig  = (none) -- ERROR: skeleton block missing!")


if __name__ == "__main__":
    # Quick test: generate and print stats
    v, s = generate_actor_skeleton()
    solid = int(np.count_nonzero(v))
    print(f"Grid: {v.shape}")
    print(f"Solid voxels: {solid}")
    print(f"Bones: {len(s['bones'])}")
    print(f"Joints: {len(s['joints'])}")
    print()
    for b in s['bones']:
        vb = f"  bounds=[{b.get('voxel_bounds_min','?')}-{b.get('voxel_bounds_max','?')}]" if 'voxel_bounds_min' in b else ""
        print(f"  {b['name']:20s} role={b['role']:10s} side={b['side']:2s} "
              f"mass={b['mass']:5.1f}  len={b['length']:.1f}{vb}")
    print()
    total_mass = sum(b['mass'] for b in s['bones'])
    print(f"Total bone mass: {total_mass:.1f}")
