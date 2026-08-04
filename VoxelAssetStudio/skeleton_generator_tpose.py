# Steel Tide: Voxel Asset Studio
# skeleton_generator_tpose.py - Generate T-Pose skeleton with actual bone/joint voxels

import numpy as np
from typing import Dict, Tuple

from material_library import get_material_mass

# Material IDs
BONE_MATERIAL = 12  # White/Beige bone voxels
JOINT_MATERIAL = 21  # Red joint voxels

def generate_tpose_biped_skeleton(grid_size: Tuple[int, int, int] = (18, 32, 8)) -> Tuple[np.ndarray, Dict]:
    """
    Generate a T-Pose biped skeleton made of actual bone and joint voxels,
    plus a complete bone+joint hierarchy in the returned metadata.
    
    Grid layout (18×32×8):
    - Arms extended horizontally (T-Pose)
    - Single voxel wide bones
    - Single red voxel joints
    - Feet, legs, knees, thighs, hips, spine, shoulders, arms, neck, head
    
    Returns:
        (voxels, skeleton_data): Voxel array + skeleton metadata where
        skeleton_data['bones'] and ['joints'] are both fully populated and
        cross-reference each other by id (parent_joint / child_joint).
    """
    voxels = np.zeros(grid_size, dtype=np.uint16)  # uint16 for .stasset compatibility
    
    center_x = grid_size[0] // 2  # 9
    center_z = grid_size[2] // 2  # 4
    width = grid_size[0]
    
    bones: list = []
    joints: list = []
    
    def add_joint(name, position, jtype, **extra):
        jid = len(joints)
        joint = {
            'id': jid,
            'name': name,
            'type': jtype,
            'position': [int(position[0]), int(position[1]), int(position[2])],
        }
        joint.update(extra)
        joints.append(joint)
        return jid
    
    def add_bone(name, start, end, parent_joint, child_joint, role='', side=''):
        bid = len(bones)
        start_v = np.array(start, dtype=float)
        end_v = np.array(end, dtype=float)
        length = float(np.linalg.norm(end_v - start_v))
        # Estimate voxel count from length (single-voxel-wide bones).
        voxel_count = max(1, int(round(length)))
        mass = voxel_count * get_material_mass(BONE_MATERIAL)
        bones.append({
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
        })
        return bid
    
    def fill_column(x, y0, y1, z, material):
        for y in range(y0, y1 + 1):
            voxels[x, y, z] = material
    
    # The skeleton forms a SINGLE tree rooted at the pelvis. Convention:
    #   parent_joint = the joint nearer the root (the bone's pivot)
    #   child_joint  = the joint farther from the root (None for a tip bone)
    # A bone's PARENT BONE is the bone whose child_joint == this bone's parent_joint.
    # The root joint (pelvis) is never any bone's child_joint, so bones whose
    # parent_joint == pelvis attach directly to the root body.
    
    # ===== PELVIS ROOT (y=16, center) =====
    voxels[center_x, 16, center_z] = JOINT_MATERIAL
    pelvis = add_joint('pelvis', (center_x, 16, center_z), 'ROOT')
    
    # ===== LEGS (Left and Right) =====
    for side, x_offset in [('left', -1), ('right', 1)]:
        leg_x = center_x + x_offset
        
        # Bone voxels: foot (0-2), lower leg (4-9), thigh (11-15)
        fill_column(leg_x, 0, 2, center_z, BONE_MATERIAL)
        fill_column(leg_x, 4, 9, center_z, BONE_MATERIAL)
        fill_column(leg_x, 11, 15, center_z, BONE_MATERIAL)
        
        # Joints: hip (16), knee (10), ankle (3)
        voxels[leg_x, 16, center_z] = JOINT_MATERIAL
        hip = add_joint(f'{side}_hip', (leg_x, 16, center_z), 'BALL',
                        max_angle_x=45.0, max_angle_y=30.0, max_angle_z=30.0)
        
        voxels[leg_x, 10, center_z] = JOINT_MATERIAL
        knee = add_joint(f'{side}_knee', (leg_x, 10, center_z), 'HINGE',
                         axis='X', min_angle=-150.0, max_angle=0.0)
        
        voxels[leg_x, 3, center_z] = JOINT_MATERIAL
        ankle = add_joint(f'{side}_ankle', (leg_x, 3, center_z), 'HINGE',
                          axis='X', min_angle=-30.0, max_angle=30.0)
        
        # Bone chain rooted at the pelvis (pelvis -> hip -> knee -> ankle -> tip):
        side_upper = side.upper()[0]  # 'L' or 'R'
        add_bone(f'{side}_pelvis', (center_x, 16, center_z), (leg_x, 16, center_z), pelvis, hip, role='pelvis', side=side_upper)
        add_bone(f'{side}_thigh', (leg_x, 16, center_z), (leg_x, 10, center_z), hip, knee, role='thigh', side=side_upper)
        add_bone(f'{side}_shin', (leg_x, 10, center_z), (leg_x, 3, center_z), knee, ankle, role='shin', side=side_upper)
        add_bone(f'{side}_foot', (leg_x, 3, center_z), (leg_x, 0, center_z), ankle, None, role='foot', side=side_upper)
    
    # ===== SPINE (y=17 to y=23) =====
    fill_column(center_x, 17, 23, center_z, BONE_MATERIAL)
    
    # Mid-spine joint (y=20)
    voxels[center_x, 20, center_z] = JOINT_MATERIAL
    mid_spine = add_joint('mid_spine', (center_x, 20, center_z), 'BALL',
                          max_angle_x=30.0, max_angle_y=20.0, max_angle_z=20.0)
    
    # Lower spine bone (pelvis -> mid-spine), attaches to the root.
    add_bone('spine_lower', (center_x, 16, center_z), (center_x, 20, center_z), pelvis, mid_spine, role='spine')
    
    # ===== SHOULDERS / CHEST (y=24) =====
    # Wide shoulder line (7 voxels) for the T-Pose arms.
    for x in range(center_x - 3, center_x + 4):
        voxels[x, 24, center_z] = BONE_MATERIAL
    
    # Chest joint (center of the shoulder line) and upper spine bone.
    voxels[center_x, 24, center_z] = JOINT_MATERIAL
    chest = add_joint('chest', (center_x, 24, center_z), 'BALL',
                      max_angle_x=20.0, max_angle_y=20.0, max_angle_z=20.0)
    add_bone('spine_upper', (center_x, 20, center_z), (center_x, 24, center_z), mid_spine, chest, role='spine')
    
    # Shoulder joints (left and right)
    for side, x_offset in [('left', -3), ('right', 3)]:
        shoulder_x = center_x + x_offset
        voxels[shoulder_x, 24, center_z] = JOINT_MATERIAL
        shoulder = add_joint(f'{side}_shoulder', (shoulder_x, 24, center_z), 'BALL',
                             max_angle_x=180.0, max_angle_y=90.0, max_angle_z=90.0)
        # Collar bone (chest -> shoulder).
        side_upper = side.upper()[0]  # 'L' or 'R'
        add_bone(f'{side}_collar', (center_x, 24, center_z), (shoulder_x, 24, center_z), chest, shoulder, role='collar', side=side_upper)
    
    # ===== ARMS (Extended horizontally for T-Pose) =====
    # direction: which way the arm extends from the shoulder along X.
    for side, direction, shoulder_x in [('left', -1, center_x - 3),
                                        ('right', 1, center_x + 3)]:
        shoulder_id = next(j['id'] for j in joints if j['name'] == f'{side}_shoulder')
        
        # Upper arm bone voxels (2 voxels outward from the shoulder)
        for step in (1, 2):
            ax = shoulder_x + direction * step
            if 0 <= ax < width:
                voxels[ax, 24, center_z] = BONE_MATERIAL
        
        # Elbow joint (3 voxels out from the shoulder)
        elbow_x = shoulder_x + direction * 3
        voxels[elbow_x, 24, center_z] = JOINT_MATERIAL
        elbow = add_joint(f'{side}_elbow', (elbow_x, 24, center_z), 'HINGE',
                          axis='Z', min_angle=0.0, max_angle=150.0)
        
        # Forearm bone voxels (2 voxels outward from the elbow)
        hand_x = elbow_x
        for step in (1, 2):
            fx = elbow_x + direction * step
            if 0 <= fx < width:
                voxels[fx, 24, center_z] = BONE_MATERIAL
                hand_x = fx
        
        side_upper = side.upper()[0]  # 'L' or 'R'
        add_bone(f'{side}_upper_arm', (shoulder_x, 24, center_z), (elbow_x, 24, center_z),
                 shoulder_id, elbow, role='upper_arm', side=side_upper)
        add_bone(f'{side}_forearm', (elbow_x, 24, center_z), (hand_x, 24, center_z),
                 elbow, None, role='forearm', side=side_upper)
    
    # ===== NECK (y=25 to y=27) =====
    fill_column(center_x, 25, 26, center_z, BONE_MATERIAL)
    voxels[center_x, 27, center_z] = JOINT_MATERIAL
    neck = add_joint('neck', (center_x, 27, center_z), 'BALL',
                     max_angle_x=60.0, max_angle_y=45.0, max_angle_z=45.0)
    add_bone('neck', (center_x, 24, center_z), (center_x, 27, center_z), chest, neck, role='neck')
    
    # ===== HEAD (y=28 to y=31) =====
    fill_column(center_x, 28, 31, center_z, BONE_MATERIAL)
    add_bone('head', (center_x, 27, center_z), (center_x, 31, center_z), neck, None, role='head')
    
    # Assemble skeleton metadata (bones AND joints fully populated).
    # 'root_joint' names the pelvis so consumers don't have to infer it.
    skeleton_data = {
        'root_joint': pelvis,
        'bones': bones,
        'joints': joints,
        'influence_map': {},
        'attachments': []
    }
    
    return voxels, skeleton_data
