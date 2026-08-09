# Steel Tide: Voxel Asset Studio
# skeleton_generator_actor_v2.py - AMS-enhanced actor skeleton generator
#
# Upgraded from skeleton_generator_actor.py with:
#   - World-space bone lengths (pre-multiplied by voxel_size)
#   - Contact role tags per bone (tip/surface/none)
#   - Chain grouping metadata (arm_L, leg_R, spine, etc.)
#   - Reach envelope pre-computation
#   - T-pose reference directions
#   - Center of mass offset per bone
#   - Contact point offsets (where on bone is the contact surface)
#
# Grid: 16 x 20 x 8  (same as v1 — voxel layout unchanged)
# Voxel size: 0.25 world units per voxel

import numpy as np
from typing import Dict, Tuple

from material_library import get_material_mass

# Material IDs
BONE_MATERIAL = 12
JOINT_MATERIAL = 21

# World units per voxel
VOXEL_SIZE = 0.25

# Bone width constants (in voxels) — 1-wide for load-bearing bones (symmetric on 17-wide grid)
LEG_WIDTH = 1        # thigh, shin (X) — 1-wide for center symmetry
LEG_DEPTH = 2        # thigh, shin (Z) — 2-deep for stability
FOOT_LENGTH = 4      # forward from ankle
FOOT_WIDTH = 1       # across (X) — 1-wide for symmetry, foot extends in Z
PELVIS_WIDTH = 5     # hip-to-hip (wider stance)
PELVIS_DEPTH = 2     # pelvis depth (Z)
SPINE_WIDTH = 1      # lower + upper spine (X) — 1-wide for center symmetry
SPINE_DEPTH = 2      # spine depth (Z) — 2-deep
ARM_WIDTH = 2        # upper arm, forearm length in X
ARM_DEPTH = 2        # arm depth (Z) — 2-wide cross-section
NECK_WIDTH = 1       # articulation (X) — 1-wide for center symmetry
NECK_DEPTH = 2       # neck depth (Z)
HEAD_WIDTH = 3       # head width (X) — odd, centered
HEAD_HEIGHT = 3      # head height (Y)
HEAD_DEPTH = 3       # head depth (Z) — blocky roundish shape


def generate_actor_skeleton_v2(grid_size: Tuple[int, int, int] = (17, 20, 8)) -> Tuple[np.ndarray, Dict]:
    """
    Generate an AMS-enhanced actor skeleton with full contact probe metadata.
    
    Same voxel layout as v1, but with enriched bone/joint metadata for the
    Adaptive Motor System (contact probes, reach envelopes, chain mapping).
    
    Returns:
        (voxels, skeleton_data): Voxel array + enriched skeleton metadata.
    """
    voxels = np.zeros(grid_size, dtype=np.uint16)

    cx = grid_size[0] // 2   # 8
    cz = grid_size[2] // 2   # 4
    width = grid_size[0]

    bones: list = []
    joints: list = []

    # ---- helpers ----

    def add_joint(name, position, jtype, voxel_bounds=None, use_position_for_anchor=False,
                  chain_origin=False, **extra):
        jid = len(joints)
        joint = {
            'id': jid,
            'name': name,
            'type': jtype,
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
                 chain='', contact_role='none', voxel_count_override=None, voxel_bounds=None):
        bid = len(bones)
        start_v = np.array(start, dtype=float)
        end_v = np.array(end, dtype=float)
        voxel_length = float(np.linalg.norm(end_v - start_v))
        world_length = voxel_length * VOXEL_SIZE

        # Contact offset: where on the bone is the contact surface (relative to start, in world units)
        contact_offset = [0.0, 0.0, 0.0]
        if contact_role == 'tip':
            # Contact is at the end of the bone (distal tip)
            diff = end_v - start_v
            contact_offset = [float(diff[0] * VOXEL_SIZE),
                              float(diff[1] * VOXEL_SIZE),
                              float(diff[2] * VOXEL_SIZE)]

        # Center of mass: midpoint of the bone (relative to start, in world units)
        diff = (end_v - start_v) * 0.5
        com_offset = [float(diff[0] * VOXEL_SIZE),
                      float(diff[1] * VOXEL_SIZE),
                      float(diff[2] * VOXEL_SIZE)]

        if voxel_count_override is not None:
            vc = voxel_count_override
        else:
            vc = max(1, int(round(voxel_length)))
        mass = vc * get_material_mass(BONE_MATERIAL)

        bone_dict = {
            'id': bid,
            'name': name,
            'role': role,
            'side': side,
            'chain': chain,
            'contact_role': contact_role,
            'contact_offset': contact_offset,
            'com_offset': com_offset,
            'start': [int(start[0]), int(start[1]), int(start[2])],
            'end': [int(end[0]), int(end[1]), int(end[2])],
            'length': voxel_length,           # voxel units (legacy)
            'world_length': world_length,      # world units (NEW — use this for probes)
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

    # ===== PELVIS ROOT (y=8, 5 wide, 2 deep) =====
    pelvis_y = 8
    pelvis_x0 = cx - PELVIS_WIDTH // 2
    pelvis_x1 = pelvis_x0 + PELVIS_WIDTH - 1
    pelvis_z0 = cz - PELVIS_DEPTH // 2
    pelvis_z1 = pelvis_z0 + PELVIS_DEPTH - 1
    pelvis_bounds = fill_box(pelvis_x0, pelvis_x1, pelvis_y, pelvis_y, pelvis_z0, pelvis_z1, JOINT_MATERIAL)
    pelvis = add_joint('pelvis', (cx, pelvis_y, cz), 'ROOT', voxel_bounds=pelvis_bounds,
                       chain_origin=True)

    # ===== LEGS =====
    for side, x_off in [('left', -2), ('right', 2)]:
        su = side.upper()[0]
        chain_name = f'leg_{su}'
        leg_cx = cx + x_off
        leg_x0 = leg_cx  # 1-wide: x0 == x1 == leg_cx
        leg_x1 = leg_cx
        leg_z0 = cz - LEG_DEPTH // 2
        leg_z1 = leg_z0 + LEG_DEPTH - 1

        # Hip joint (chain origin for legs)
        hip_bounds = stamp_joint_box(leg_x0, leg_x1, pelvis_y, leg_z0, leg_z1)
        hip = add_joint(f'{side}_hip', (leg_cx, pelvis_y, cz), 'BALL',
                        voxel_bounds=hip_bounds, chain_origin=True,
                        max_angle_x=45.0, max_angle_y=30.0, max_angle_z=30.0)

        # Thigh bone (contact_role=tip — knee can plant here)
        thigh_top = pelvis_y - 1
        thigh_bot = 5
        thigh_bounds = fill_box(leg_x0, leg_x1, thigh_bot, thigh_top, leg_z0, leg_z1, BONE_MATERIAL)
        thigh_voxels = (thigh_top - thigh_bot + 1) * LEG_WIDTH * LEG_DEPTH
        add_bone(f'{side}_thigh', (leg_cx, pelvis_y, cz), (leg_cx, thigh_bot, cz),
                 hip, None, role='thigh', side=su, chain=chain_name,
                 contact_role='tip', voxel_count_override=thigh_voxels,
                 voxel_bounds=thigh_bounds)

        # Knee joint
        knee_y = thigh_bot
        knee_bounds = stamp_joint_box(leg_x0, leg_x1, knee_y, leg_z0, leg_z1)
        knee = add_joint(f'{side}_knee', (leg_cx, knee_y, cz), 'HINGE',
                         voxel_bounds=knee_bounds,
                         axis='X', min_angle=-150.0, max_angle=0.0)
        bones[-1]['child_joint'] = knee

        # Shin bone (contact_role=tip — foot can plant here)
        shin_top = knee_y - 1
        shin_bot = 1
        shin_bounds = fill_box(leg_x0, leg_x1, shin_bot, shin_top, leg_z0, leg_z1, BONE_MATERIAL)
        shin_voxels = (shin_top - shin_bot + 1) * LEG_WIDTH * LEG_DEPTH
        add_bone(f'{side}_shin', (leg_cx, knee_y, cz), (leg_cx, shin_bot, cz),
                 knee, None, role='shin', side=su, chain=chain_name,
                 contact_role='tip', voxel_count_override=shin_voxels,
                 voxel_bounds=shin_bounds)

        # Ankle joint
        ankle_y = shin_bot
        ankle_bounds = stamp_joint_box(leg_x0, leg_x1, ankle_y, leg_z0, leg_z1)
        ankle = add_joint(f'{side}_ankle', (leg_cx, ankle_y, cz), 'HINGE',
                          voxel_bounds=ankle_bounds, use_position_for_anchor=True,
                          axis='X', min_angle=-30.0, max_angle=30.0)
        bones[-1]['child_joint'] = ankle

        # Foot bone (contact_role=surface — flat contact surface)
        foot_y = 0
        foot_z0 = cz
        foot_z1 = cz + FOOT_LENGTH - 1
        foot_x0 = leg_cx - FOOT_WIDTH // 2
        foot_x1 = foot_x0 + FOOT_WIDTH - 1
        foot_bounds = fill_box(foot_x0, foot_x1, foot_y, foot_y, foot_z0, foot_z1, BONE_MATERIAL)
        foot_voxels = (foot_z1 - foot_z0 + 1) * (foot_x1 - foot_x0 + 1)
        add_bone(f'{side}_foot', (leg_cx, ankle_y, cz), (leg_cx, foot_y, foot_z1),
                 ankle, None, role='foot', side=su, chain=chain_name,
                 contact_role='surface', voxel_count_override=foot_voxels,
                 voxel_bounds=foot_bounds)

    # ===== SPINE =====
    spine_y0 = pelvis_y + 1
    mid_spine_y = 10
    spine_y1 = 12

    sp_x0 = cx  # 1-wide spine: x0 == x1 == cx
    sp_x1 = cx
    sp_z0 = cz - SPINE_DEPTH // 2
    sp_z1 = sp_z0 + SPINE_DEPTH - 1

    lower_spine_bounds = fill_box(sp_x0, sp_x1, spine_y0, mid_spine_y, sp_z0, sp_z1, BONE_MATERIAL)
    lower_spine_voxels = (mid_spine_y - spine_y0 + 1) * SPINE_WIDTH * SPINE_DEPTH
    mid_spine_bounds = stamp_joint_box(sp_x0, sp_x1, mid_spine_y, sp_z0, sp_z1)
    mid_spine = add_joint('mid_spine', (cx, mid_spine_y, cz), 'BALL',
                          voxel_bounds=mid_spine_bounds,
                          max_angle_x=30.0, max_angle_y=20.0, max_angle_z=20.0)
    add_bone('spine_lower', (cx, pelvis_y, cz), (cx, mid_spine_y, cz),
             pelvis, mid_spine, role='spine', chain='spine',
             contact_role='none', voxel_count_override=lower_spine_voxels,
             voxel_bounds=lower_spine_bounds)

    upper_spine_bounds = fill_box(sp_x0, sp_x1, mid_spine_y + 1, spine_y1, sp_z0, sp_z1, BONE_MATERIAL)
    upper_spine_voxels = (spine_y1 - (mid_spine_y + 1) + 1) * SPINE_WIDTH * SPINE_DEPTH

    # ===== CHEST / SHOULDERS =====
    chest_y = 13
    shoulder_x0 = cx - 2
    shoulder_x1 = cx + 2
    chest_bounds = fill_box(shoulder_x0, shoulder_x1, chest_y, chest_y, sp_z0, sp_z1, BONE_MATERIAL)

    chest_joint_bounds = stamp_joint_box(sp_x0, sp_x1, chest_y, sp_z0, sp_z1)
    chest = add_joint('chest', (cx, chest_y, cz), 'BALL',
                      voxel_bounds=chest_joint_bounds,
                      max_angle_x=20.0, max_angle_y=20.0, max_angle_z=20.0)
    add_bone('spine_upper', (cx, mid_spine_y, cz), (cx, chest_y, cz),
             mid_spine, chest, role='spine', chain='spine',
             contact_role='none', voxel_count_override=upper_spine_voxels,
             voxel_bounds=upper_spine_bounds)

    # Shoulder joints + collar bones
    for side, x_off in [('left', -2), ('right', 2)]:
        su = side.upper()[0]
        chain_name = f'arm_{su}'
        sh_x = cx + x_off
        sh_z0 = cz - ARM_DEPTH // 2
        sh_z1 = sh_z0 + ARM_DEPTH - 1
        sh_bounds = stamp_joint_box(sh_x, sh_x, chest_y, sh_z0, sh_z1)
        shoulder = add_joint(f'{side}_shoulder', (sh_x, chest_y, cz), 'BALL',
                             voxel_bounds=sh_bounds, chain_origin=True,
                             max_angle_x=180.0, max_angle_y=90.0, max_angle_z=90.0)
        # Fill collar bone voxels between chest and shoulder (full Z depth)
        collar_x0 = min(cx, sh_x)
        collar_x1 = max(cx, sh_x)
        collar_bounds = fill_box(collar_x0, collar_x1, chest_y, chest_y, sh_z0, sh_z1, BONE_MATERIAL)
        add_bone(f'{side}_collar', (cx, chest_y, cz), (sh_x, chest_y, cz),
                 chest, shoulder, role='collar', side=su, chain=chain_name,
                 contact_role='none', voxel_bounds=collar_bounds)

    # ===== ARMS =====
    arm_z0 = cz - ARM_DEPTH // 2
    arm_z1 = arm_z0 + ARM_DEPTH - 1
    for side, direction, sh_x in [('left', -1, cx - 2), ('right', 1, cx + 2)]:
        su = side.upper()[0]
        chain_name = f'arm_{su}'
        shoulder_id = next(j['id'] for j in joints if j['name'] == f'{side}_shoulder')

        # Upper arm (contact_role=tip — elbow can plant here)
        arm_x0 = min(sh_x, sh_x + direction * 2)
        arm_x1 = max(sh_x, sh_x + direction * 2)
        upper_arm_bounds = fill_box(arm_x0, arm_x1, chest_y, chest_y, arm_z0, arm_z1, BONE_MATERIAL)
        upper_arm_voxels = (arm_x1 - arm_x0 + 1) * ARM_DEPTH

        elbow_x = sh_x + direction * 3
        elbow_bounds = stamp_joint_box(elbow_x, elbow_x, chest_y, arm_z0, arm_z1) if 0 <= elbow_x < width else ([elbow_x, chest_y, arm_z0], [elbow_x, chest_y, arm_z1])
        elbow = add_joint(f'{side}_elbow', (elbow_x, chest_y, cz), 'HINGE',
                          voxel_bounds=elbow_bounds,
                          axis='Z', min_angle=0.0, max_angle=150.0)
        add_bone(f'{side}_upper_arm', (sh_x, chest_y, cz), (elbow_x, chest_y, cz),
                 shoulder_id, elbow, role='upper_arm', side=su, chain=chain_name,
                 contact_role='tip', voxel_count_override=upper_arm_voxels,
                 voxel_bounds=upper_arm_bounds)

        # Forearm (contact_role=tip — hand can plant here)
        hand_x = elbow_x
        for step in (1, 2):
            fx = elbow_x + direction * step
            if 0 <= fx < width:
                fill_box(fx, fx, chest_y, chest_y, arm_z0, arm_z1, BONE_MATERIAL)
                hand_x = fx
        forearm_bounds = ([min(elbow_x, hand_x), chest_y, arm_z0], [max(elbow_x, hand_x), chest_y, arm_z1])
        forearm_voxels = (max(elbow_x, hand_x) - min(elbow_x, hand_x) + 1) * ARM_DEPTH
        add_bone(f'{side}_forearm', (elbow_x, chest_y, cz), (hand_x, chest_y, cz),
                 elbow, None, role='forearm', side=su, chain=chain_name,
                 contact_role='tip', voxel_count_override=forearm_voxels,
                 voxel_bounds=forearm_bounds)

    # ===== NECK =====
    neck_y0 = chest_y + 1
    neck_y1 = 14
    neck_x0 = cx  # 1-wide neck: x0 == x1 == cx
    neck_x1 = cx
    neck_z0 = cz - NECK_DEPTH // 2
    neck_z1 = neck_z0 + NECK_DEPTH - 1
    neck_bounds = fill_box(neck_x0, neck_x1, neck_y0, neck_y1, neck_z0, neck_z1, BONE_MATERIAL)
    neck_joint_y = 15
    neck_joint_bounds = stamp_joint_box(neck_x0, neck_x1, neck_joint_y, neck_z0, neck_z1)
    neck = add_joint('neck', (cx, neck_joint_y, cz), 'BALL',
                     voxel_bounds=neck_joint_bounds,
                     max_angle_x=60.0, max_angle_y=45.0, max_angle_z=45.0)
    neck_voxels = (neck_y1 - neck_y0 + 1) * NECK_WIDTH * NECK_DEPTH
    add_bone('neck', (cx, chest_y, cz), (cx, neck_joint_y, cz),
             chest, neck, role='neck', chain='head',
             contact_role='none', voxel_count_override=neck_voxels,
             voxel_bounds=neck_bounds)

    # ===== HEAD =====
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
             contact_role='none', voxel_count_override=head_voxels,
             voxel_bounds=head_bounds)

    # ===== AMS METADATA: Chain map, reach envelopes, T-pose directions =====

    # Build chain map from bone data
    chain_map = {}
    for b in bones:
        ch = b.get('chain', '')
        if ch:
            if ch not in chain_map:
                chain_map[ch] = []
            chain_map[ch].append(b['name'])

    # Compute reach envelopes per chain
    reach_envelopes = {}
    for chain_name, bone_names in chain_map.items():
        if chain_name.startswith('arm_'):
            side = chain_name.split('_')[1]
            # Find by chain and role
            ua = next((b for b in bones if b.get('chain') == chain_name and b['role'] == 'upper_arm'), None)
            fa = next((b for b in bones if b.get('chain') == chain_name and b['role'] == 'forearm'), None)
            if ua and fa:
                proximal = ua['world_length']
                distal = ua['world_length'] + fa['world_length']
                reach_envelopes[chain_name] = {
                    'origin_joint': f'{"left" if side == "L" else "right"}_shoulder',
                    'proximal_reach': proximal,
                    'distal_reach': distal,
                    'proximal_contact': 'elbow',
                    'distal_contact': 'hand',
                }
        elif chain_name.startswith('leg_'):
            side = chain_name.split('_')[1]
            th = next((b for b in bones if b.get('chain') == chain_name and b['role'] == 'thigh'), None)
            sh = next((b for b in bones if b.get('chain') == chain_name and b['role'] == 'shin'), None)
            if th and sh:
                proximal = th['world_length']
                distal = th['world_length'] + sh['world_length']
                reach_envelopes[chain_name] = {
                    'origin_joint': f'{"left" if side == "L" else "right"}_hip',
                    'proximal_reach': proximal,
                    'distal_reach': distal,
                    'proximal_contact': 'knee',
                    'distal_contact': 'foot',
                }

    # ===== Re-paint joint voxels (ensure all voxels in joint bounds are JOINT_MATERIAL) =====
    # Bones may have overwritten joint voxels during fill_box calls.
    # This pass ensures every voxel inside a joint's bounds is marked as JOINT_MATERIAL.
    for j in joints:
        if 'voxel_bounds_min' in j:
            bmin = j['voxel_bounds_min']
            bmax = j['voxel_bounds_max']
            for x in range(max(0, bmin[0]), min(width, bmax[0] + 1)):
                for y in range(max(0, bmin[1]), min(grid_size[1], bmax[1] + 1)):
                    for z in range(max(0, bmin[2]), min(grid_size[2], bmax[2] + 1)):
                        voxels[x, y, z] = JOINT_MATERIAL

    # ===== Assemble skeleton metadata =====
    skeleton_data = {
        'root_joint': pelvis,
        'bones': bones,
        'joints': joints,
        'influence_map': {},
        'attachments': [],
        # AMS metadata
        'ams': {
            'version': 2,
            'voxel_size': VOXEL_SIZE,
            't_pose_forward': [0.0, 0.0, 1.0],   # +Z is forward in bind pose
            't_pose_up': [0.0, 1.0, 0.0],         # +Y is up
            't_pose_right': [1.0, 0.0, 0.0],      # +X is right
            'chain_map': chain_map,
            'reach_envelopes': reach_envelopes,
        },
    }

    return voxels, skeleton_data


# ============================================================
# Regeneration helper
# ============================================================

def regenerate_streaming_asset(out_path: str) -> None:
    """Generate the v2 actor skeleton and save directly to a .stasset path."""
    import os
    from stasset_io import save_stasset, load_stasset

    voxels, skeleton = generate_actor_skeleton_v2()
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    save_stasset(out_path, voxels, skeleton)

    rv, dims, rs = load_stasset(out_path)
    print(f"Wrote {out_path}")
    print(f"  dims = {dims[0]}x{dims[1]}x{dims[2]}")
    if rs is not None:
        print(f"  rig  = {len(rs['bones'])} bones, {len(rs['joints'])} joints, "
              f"root_joint = {rs.get('root_joint')}")
        ams = rs.get('ams', {})
        if ams:
            print(f"  AMS  = v{ams.get('version')}, voxel_size={ams.get('voxel_size')}")
            print(f"  chains = {list(ams.get('chain_map', {}).keys())}")
            for ch, env in ams.get('reach_envelopes', {}).items():
                print(f"    {ch}: proximal={env['proximal_reach']:.2f}, distal={env['distal_reach']:.2f}")
        total_mass = sum(b.get('mass', 0) for b in rs['bones'])
        print(f"  total bone mass = {total_mass:.1f}")
    else:
        print("  rig  = (none) -- ERROR: skeleton block missing!")


if __name__ == "__main__":
    v, s = generate_actor_skeleton_v2()
    solid = int(np.count_nonzero(v))
    print(f"Grid: {v.shape}")
    print(f"Solid voxels: {solid}")
    print(f"Bones: {len(s['bones'])}")
    print(f"Joints: {len(s['joints'])}")
    print()
    for b in s['bones']:
        print(f"  {b['name']:20s} role={b['role']:10s} side={b['side']:2s} "
              f"chain={b.get('chain',''):6s} contact={b.get('contact_role','none'):7s} "
              f"vlen={b['length']:.1f} wlen={b['world_length']:.2f} "
              f"mass={b['mass']:5.1f}")
    print()
    ams = s.get('ams', {})
    print(f"AMS metadata:")
    print(f"  voxel_size = {ams.get('voxel_size')}")
    print(f"  chains = {ams.get('chain_map', {})}")
    print(f"  reach_envelopes:")
    for ch, env in ams.get('reach_envelopes', {}).items():
        print(f"    {ch}: {env}")
    total_mass = sum(b['mass'] for b in s['bones'])
    print(f"\nTotal bone mass: {total_mass:.1f}")
