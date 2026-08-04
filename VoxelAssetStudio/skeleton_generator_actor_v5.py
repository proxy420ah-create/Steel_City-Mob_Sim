# Steel Tide: Voxel Asset Studio
# skeleton_generator_actor_v5.py — 10-joint actor skeleton with capsule root collider
#
# Identical voxel layout to v4. Key difference:
#   - AMS metadata includes root_collider: "capsule" so C# builds a CapsuleCollider
#     on the pelvis instead of a BoxCollider. A capsule rolls; a box sticks.
#   - Waist BALL joint is built in (no separate post-processing script needed).
#
# Grid: 17 x 20 x 8  (same as v4)
# Voxel size: 0.25 world units per voxel

import numpy as np
from typing import Dict, Tuple

from material_library import get_material_mass

BONE_MATERIAL = 12
JOINT_MATERIAL = 21
VOXEL_SIZE = 0.25

LEG_WIDTH = 1
LEG_DEPTH = 2
FOOT_LENGTH = 4
FOOT_WIDTH = 1
PELVIS_WIDTH = 5
PELVIS_DEPTH = 2
SPINE_WIDTH = 1
SPINE_DEPTH = 2
ARM_WIDTH = 2
ARM_DEPTH = 2
NECK_WIDTH = 1
NECK_DEPTH = 2
HEAD_WIDTH = 3
HEAD_HEIGHT = 3
HEAD_DEPTH = 3

# Waist BALL joint limits (degrees) — same as add_waist_ball_joint_v4.py
WAIST_MAX_ANGLE_X = 90.0
WAIST_MAX_ANGLE_Y = 45.0
WAIST_MAX_ANGLE_Z = 45.0


def generate_actor_skeleton_v5(grid_size: Tuple[int, int, int] = (17, 20, 8)) -> Tuple[np.ndarray, Dict]:
    """Generate a 10-joint actor skeleton with capsule root collider metadata."""
    voxels = np.zeros(grid_size, dtype=np.uint16)
    cx = grid_size[0] // 2
    cz = grid_size[2] // 2
    width = grid_size[0]
    bones: list = []
    joints: list = []

    def add_joint(name, position, jtype, voxel_bounds=None, use_position_for_anchor=False,
                  chain_origin=False, **extra):
        jid = len(joints)
        joint = {
            'id': jid, 'name': name, 'type': jtype,
            'position': [int(position[0]), int(position[1]), int(position[2])],
            'chain_origin': chain_origin,
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
                 chain='', contact_role='none', voxel_count_override=None, voxel_bounds=None,
                 collider_only=False):
        bid = len(bones)
        start_v = np.array(start, dtype=float)
        end_v = np.array(end, dtype=float)
        voxel_length = float(np.linalg.norm(end_v - start_v))
        world_length = voxel_length * VOXEL_SIZE
        contact_offset = [0.0, 0.0, 0.0]
        if contact_role == 'tip':
            diff = end_v - start_v
            contact_offset = [float(diff[0] * VOXEL_SIZE), float(diff[1] * VOXEL_SIZE), float(diff[2] * VOXEL_SIZE)]
        diff = (end_v - start_v) * 0.5
        com_offset = [float(diff[0] * VOXEL_SIZE), float(diff[1] * VOXEL_SIZE), float(diff[2] * VOXEL_SIZE)]
        if voxel_count_override is not None:
            vc = voxel_count_override
        else:
            vc = max(1, int(round(voxel_length)))
        mass = vc * get_material_mass(BONE_MATERIAL)
        bone_dict = {
            'id': bid, 'name': name, 'role': role, 'side': side, 'chain': chain,
            'contact_role': contact_role, 'contact_offset': contact_offset, 'com_offset': com_offset,
            'start': [int(start[0]), int(start[1]), int(start[2])],
            'end': [int(end[0]), int(end[1]), int(end[2])],
            'length': voxel_length, 'world_length': world_length, 'mass': mass,
            'parent_joint': parent_joint, 'child_joint': child_joint,
            'colliderOnly': collider_only,
        }
        if voxel_bounds is not None:
            bone_dict['voxel_bounds_min'] = list(voxel_bounds[0])
            bone_dict['voxel_bounds_max'] = list(voxel_bounds[1])
        bones.append(bone_dict)
        return bid

    def fill_box(x0, x1, y0, y1, z0, z1, material):
        for x in range(max(0, x0), min(width, x1 + 1)):
            for y in range(max(0, y0), min(grid_size[1], y1 + 1)):
                for z in range(max(0, z0), min(grid_size[2], z1 + 1)):
                    voxels[x, y, z] = material
        return ([x0, y0, z0], [x1, y1, z1])

    def stamp_joint_box(x0, x1, y, z0, z1):
        for x in range(max(0, x0), min(width, x1 + 1)):
            for z in range(max(0, z0), min(grid_size[2], z1 + 1)):
                if 0 <= y < grid_size[1]:
                    voxels[x, y, z] = JOINT_MATERIAL
        return ([x0, y, z0], [x1, y, z1])

    # PELVIS ROOT
    pelvis_y = 8
    pelvis_x0 = cx - PELVIS_WIDTH // 2
    pelvis_x1 = pelvis_x0 + PELVIS_WIDTH - 1
    pelvis_z0 = cz - PELVIS_DEPTH // 2
    pelvis_z1 = pelvis_z0 + PELVIS_DEPTH - 1
    pelvis_bounds = fill_box(pelvis_x0, pelvis_x1, pelvis_y, pelvis_y, pelvis_z0, pelvis_z1, JOINT_MATERIAL)
    pelvis = add_joint('pelvis', (cx, pelvis_y, cz), 'ROOT', voxel_bounds=pelvis_bounds, chain_origin=True)

    # WAIST BALL JOINT (between pelvis ROOT and spine — built in, no post-processing needed)
    waist = add_joint('waist', (cx, pelvis_y, cz), 'BALL',
                      max_angle_x=WAIST_MAX_ANGLE_X,
                      max_angle_y=WAIST_MAX_ANGLE_Y,
                      max_angle_z=WAIST_MAX_ANGLE_Z,
                      chain_origin=False)

    # LEGS
    for side, x_off in [('left', -2), ('right', 2)]:
        su = side.upper()[0]
        chain_name = f'leg_{su}'
        leg_cx = cx + x_off
        leg_x0 = leg_cx; leg_x1 = leg_cx
        leg_z0 = cz - LEG_DEPTH // 2
        leg_z1 = leg_z0 + LEG_DEPTH - 1

        hip_bounds = stamp_joint_box(leg_x0, leg_x1, pelvis_y, leg_z0, leg_z1)
        hip = add_joint(f'{side}_hip', (leg_cx, pelvis_y, cz), 'BALL',
                        voxel_bounds=hip_bounds, chain_origin=True,
                        max_angle_x=45.0, max_angle_y=30.0, max_angle_z=30.0)

        thigh_top = pelvis_y - 1
        thigh_bot = 5
        thigh_bounds = fill_box(leg_x0, leg_x1, thigh_bot, thigh_top, leg_z0, leg_z1, BONE_MATERIAL)
        thigh_voxels = (thigh_top - thigh_bot + 1) * LEG_WIDTH * LEG_DEPTH
        add_bone(f'{side}_thigh', (leg_cx, pelvis_y, cz), (leg_cx, thigh_bot, cz),
                 hip, None, role='thigh', side=su, chain=chain_name,
                 contact_role='tip', voxel_count_override=thigh_voxels, voxel_bounds=thigh_bounds)

        knee_y = thigh_bot
        knee_bounds = stamp_joint_box(leg_x0, leg_x1, knee_y, leg_z0, leg_z1)
        knee = add_joint(f'{side}_knee', (leg_cx, knee_y, cz), 'HINGE',
                         voxel_bounds=knee_bounds, axis='X', min_angle=-150.0, max_angle=0.0)
        bones[-1]['child_joint'] = knee

        shin_top = knee_y - 1
        shin_bot = 1
        shin_bounds = fill_box(leg_x0, leg_x1, shin_bot, shin_top, leg_z0, leg_z1, BONE_MATERIAL)
        shin_voxels = (shin_top - shin_bot + 1) * LEG_WIDTH * LEG_DEPTH
        add_bone(f'{side}_shin', (leg_cx, knee_y, cz), (leg_cx, shin_bot, cz),
                 knee, None, role='shin', side=su, chain=chain_name,
                 contact_role='tip', voxel_count_override=shin_voxels, voxel_bounds=shin_bounds)

        # Foot — collider-only, no ankle joint
        foot_y = 0
        foot_z0 = cz
        foot_z1 = cz + FOOT_LENGTH - 1
        foot_x0 = leg_cx - FOOT_WIDTH // 2
        foot_x1 = foot_x0 + FOOT_WIDTH - 1
        foot_bounds = fill_box(foot_x0, foot_x1, foot_y, foot_y, foot_z0, foot_z1, BONE_MATERIAL)
        foot_voxels = (foot_z1 - foot_z0 + 1) * (foot_x1 - foot_x0 + 1)
        add_bone(f'{side}_foot', (leg_cx, shin_bot, cz), (leg_cx, foot_y, foot_z1),
                 knee, None, role='foot', side=su, chain=chain_name,
                 contact_role='surface', voxel_count_override=foot_voxels,
                 voxel_bounds=foot_bounds, collider_only=True)

    # SPINE (single bone pelvis → neck, parent_joint = waist)
    spine_y0 = pelvis_y + 1
    spine_y1 = 14
    sp_x0 = cx; sp_x1 = cx
    sp_z0 = cz - SPINE_DEPTH // 2
    sp_z1 = sp_z0 + SPINE_DEPTH - 1
    spine_bounds = fill_box(sp_x0, sp_x1, spine_y0, spine_y1, sp_z0, sp_z1, BONE_MATERIAL)
    spine_voxels = (spine_y1 - spine_y0 + 1) * SPINE_WIDTH * SPINE_DEPTH

    # NECK
    neck_joint_y = 15
    neck_joint_bounds = stamp_joint_box(sp_x0, sp_x1, neck_joint_y, sp_z0, sp_z1)
    neck = add_joint('neck', (cx, neck_joint_y, cz), 'BALL',
                     voxel_bounds=neck_joint_bounds,
                     max_angle_x=60.0, max_angle_y=45.0, max_angle_z=45.0)

    add_bone('spine', (cx, pelvis_y, cz), (cx, neck_joint_y, cz),
             waist, neck, role='spine', chain='spine',
             contact_role='none', voxel_count_override=spine_voxels, voxel_bounds=spine_bounds)

    # SHOULDERS / COLLAR BONES (neck → shoulder)
    for side, x_off in [('left', -2), ('right', 2)]:
        su = side.upper()[0]
        chain_name = f'arm_{su}'
        sh_x = cx + x_off
        sh_z0 = cz - ARM_DEPTH // 2
        sh_z1 = sh_z0 + ARM_DEPTH - 1
        sh_bounds = stamp_joint_box(sh_x, sh_x, 13, sh_z0, sh_z1)
        shoulder = add_joint(f'{side}_shoulder', (sh_x, 13, cz), 'BALL',
                             voxel_bounds=sh_bounds, chain_origin=True,
                             max_angle_x=180.0, max_angle_y=90.0, max_angle_z=90.0)
        collar_x0 = min(cx, sh_x)
        collar_x1 = max(cx, sh_x)
        collar_bounds = fill_box(collar_x0, collar_x1, 13, 13, sh_z0, sh_z1, BONE_MATERIAL)
        add_bone(f'{side}_collar', (cx, neck_joint_y, cz), (sh_x, 13, cz),
                 neck, shoulder, role='collar', side=su, chain=chain_name,
                 contact_role='none', voxel_bounds=collar_bounds)

    # ARMS
    arm_z0 = cz - ARM_DEPTH // 2
    arm_z1 = arm_z0 + ARM_DEPTH - 1
    for side, direction, sh_x in [('left', -1, cx - 2), ('right', 1, cx + 2)]:
        su = side.upper()[0]
        chain_name = f'arm_{su}'
        shoulder_id = next(j['id'] for j in joints if j['name'] == f'{side}_shoulder')

        arm_x0 = min(sh_x, sh_x + direction * 2)
        arm_x1 = max(sh_x, sh_x + direction * 2)
        upper_arm_bounds = fill_box(arm_x0, arm_x1, 13, 13, arm_z0, arm_z1, BONE_MATERIAL)
        upper_arm_voxels = (arm_x1 - arm_x0 + 1) * ARM_DEPTH

        elbow_x = sh_x + direction * 3
        elbow_bounds = stamp_joint_box(elbow_x, elbow_x, 13, arm_z0, arm_z1) if 0 <= elbow_x < width else ([elbow_x, 13, arm_z0], [elbow_x, 13, arm_z1])
        elbow = add_joint(f'{side}_elbow', (elbow_x, 13, cz), 'HINGE',
                          voxel_bounds=elbow_bounds, axis='Z', min_angle=0.0, max_angle=150.0)
        add_bone(f'{side}_upper_arm', (sh_x, 13, cz), (elbow_x, 13, cz),
                 shoulder_id, elbow, role='upper_arm', side=su, chain=chain_name,
                 contact_role='tip', voxel_count_override=upper_arm_voxels, voxel_bounds=upper_arm_bounds)

        hand_x = elbow_x
        for step in (1, 2):
            fx = elbow_x + direction * step
            if 0 <= fx < width:
                fill_box(fx, fx, 13, 13, arm_z0, arm_z1, BONE_MATERIAL)
                hand_x = fx
        forearm_bounds = ([min(elbow_x, hand_x), 13, arm_z0], [max(elbow_x, hand_x), 13, arm_z1])
        forearm_voxels = (max(elbow_x, hand_x) - min(elbow_x, hand_x) + 1) * ARM_DEPTH
        add_bone(f'{side}_forearm', (elbow_x, 13, cz), (hand_x, 13, cz),
                 elbow, None, role='forearm', side=su, chain=chain_name,
                 contact_role='tip', voxel_count_override=forearm_voxels, voxel_bounds=forearm_bounds)

    # HEAD
    head_y0 = neck_joint_y + 1
    head_y1 = head_y0 + HEAD_HEIGHT - 1
    head_x0 = cx - HEAD_WIDTH // 2
    head_x1 = head_x0 + HEAD_WIDTH - 1
    head_z0 = cz - HEAD_DEPTH // 2
    head_z1 = head_z0 + HEAD_DEPTH - 1
    head_bounds = fill_box(head_x0, head_x1, head_y0, head_y1, head_z0, head_z1, BONE_MATERIAL)
    head_voxels = (head_y1 - head_y0 + 1) * HEAD_WIDTH * HEAD_DEPTH
    add_bone('head', (cx, neck_joint_y, cz), (cx, head_y1, cz),
             neck, None, role='head', chain='head',
             contact_role='none', voxel_count_override=head_voxels, voxel_bounds=head_bounds)

    # Re-paint joint voxels
    for j in joints:
        if 'voxel_bounds_min' in j:
            bmin = j['voxel_bounds_min']
            bmax = j['voxel_bounds_max']
            for x in range(max(0, bmin[0]), min(width, bmax[0] + 1)):
                for y in range(max(0, bmin[1]), min(grid_size[1], bmax[1] + 1)):
                    for z in range(max(0, bmin[2]), min(grid_size[2], bmax[2] + 1)):
                        voxels[x, y, z] = JOINT_MATERIAL

    # AMS metadata
    chain_map = {}
    for b in bones:
        ch = b.get('chain', '')
        if ch:
            chain_map.setdefault(ch, []).append(b['name'])

    reach_envelopes = {}
    for chain_name, _ in chain_map.items():
        if chain_name.startswith('arm_'):
            side = chain_name.split('_')[1]
            ua = next((b for b in bones if b.get('chain') == chain_name and b['role'] == 'upper_arm'), None)
            fa = next((b for b in bones if b.get('chain') == chain_name and b['role'] == 'forearm'), None)
            if ua and fa:
                reach_envelopes[chain_name] = {
                    'origin_joint': f'{"left" if side == "L" else "right"}_shoulder',
                    'proximal_reach': ua['world_length'],
                    'distal_reach': ua['world_length'] + fa['world_length'],
                    'proximal_contact': 'elbow', 'distal_contact': 'hand',
                }
        elif chain_name.startswith('leg_'):
            side = chain_name.split('_')[1]
            th = next((b for b in bones if b.get('chain') == chain_name and b['role'] == 'thigh'), None)
            sh = next((b for b in bones if b.get('chain') == chain_name and b['role'] == 'shin'), None)
            if th and sh:
                reach_envelopes[chain_name] = {
                    'origin_joint': f'{"left" if side == "L" else "right"}_hip',
                    'proximal_reach': th['world_length'],
                    'distal_reach': th['world_length'] + sh['world_length'],
                    'proximal_contact': 'knee', 'distal_contact': 'foot',
                }

    # Pelvis dimensions for capsule collider (in voxel units)
    pelvis_width_voxels = PELVIS_WIDTH
    pelvis_depth_voxels = PELVIS_DEPTH
    pelvis_height_voxels = 1  # single voxel layer

    skeleton_data = {
        'root_joint': pelvis,
        'bones': bones,
        'joints': joints,
        'influence_map': {},
        'attachments': [],
        'ams': {
            'version': 5,
            'voxel_size': VOXEL_SIZE,
            't_pose_forward': [0.0, 0.0, 1.0],
            't_pose_up': [0.0, 1.0, 0.0],
            't_pose_right': [1.0, 0.0, 0.0],
            'chain_map': chain_map,
            'reach_envelopes': reach_envelopes,
            # V5 addition: root collider type
            'root_collider': 'capsule',
            'root_collider_dims': {
                'width_voxels': pelvis_width_voxels,
                'depth_voxels': pelvis_depth_voxels,
                'height_voxels': pelvis_height_voxels,
            },
        },
    }

    return voxels, skeleton_data


if __name__ == "__main__":
    v, s = generate_actor_skeleton_v5()
    print(f"Grid: {v.shape}")
    print(f"Solid voxels: {int(np.count_nonzero(v))}")
    print(f"Bones: {len(s['bones'])}, Joints: {len(s['joints'])}")
    print(f"Root collider: {s['ams']['root_collider']}")
    for j in s['joints']:
        print(f"  {j['id']}: {j['name']} ({j['type']})" +
              (f" limits=({j.get('max_angle_x')},{j.get('max_angle_y')},{j.get('max_angle_z')})"
               if j['type'] == 'BALL' else ''))
    for b in s['bones']:
        co = "[COLLIDER-ONLY]" if b.get('colliderOnly') else ""
        print(f"  {b['name']:20s} role={b['role']:10s} chain={b.get('chain',''):6s} {co}")
    spine = next(b for b in s['bones'] if b['role'] == 'spine')
    waist = next(j for j in s['joints'] if j['name'] == 'waist')
    print(f"\nSpine parent_joint = {spine['parent_joint']} (waist id={waist['id']})")
