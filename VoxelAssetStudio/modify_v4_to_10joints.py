"""Reduce ActorSymmetricV4 from 14 joints to 10 joints by editing the existing .stasset."""
import sys
sys.path.insert(0, '.')
from stasset_io import load_stasset, save_stasset
import numpy as np

VOXEL_SIZE = 0.25
BONE_MAT = 12
JOINT_MAT = 21

voxels, dims, skel = load_stasset('../My project/Assets/StreamingAssets/ActorSymmetricV4.stasset')
print(f"Loaded V4: {dims}, {np.count_nonzero(voxels)} voxels")
print(f"Before: {len(skel['bones'])} bones, {len(skel['joints'])} joints")

joints = skel['joints']
bones = skel['bones']

# Map joint names to IDs
jmap = {j['name']: j['id'] for j in joints}
bmap = {b['name']: b['id'] for b in bones}

# Joints to REMOVE: mid_spine, chest, left_ankle, right_ankle
remove_joint_names = ['mid_spine', 'chest', 'left_ankle', 'right_ankle']
remove_joint_ids = {jmap[n] for n in remove_joint_names}

# --- Step 1: Remove joints ---
new_joints = []
id_remap = {}
for j in joints:
    if j['id'] in remove_joint_ids:
        continue
    new_id = len(new_joints)
    id_remap[j['id']] = new_id
    new_j = dict(j)
    new_j['id'] = new_id
    new_joints.append(new_j)

# --- Step 2: Update bones ---
new_bones = []
for b in bones:
    new_b = dict(b)
    # Remap joint IDs
    if b['parent_joint'] in id_remap:
        new_b['parent_joint'] = id_remap[b['parent_joint']]
    if b['child_joint'] in id_remap:
        new_b['child_joint'] = id_remap[b['child_joint']]
    elif b['child_joint'] == -1:
        new_b['child_joint'] = -1
    else:
        # child_joint was removed; this bone becomes a tip bone
        new_b['child_joint'] = -1
    new_bones.append(new_b)

# --- Step 3: Merge spine_lower + spine_upper + neck into single spine bone ---
# Find indices
spine_lower_idx = next(i for i, b in enumerate(new_bones) if b['name'] == 'spine_lower')
spine_upper_idx = next(i for i, b in enumerate(new_bones) if b['name'] == 'spine_upper')
neck_bone_idx = next(i for i, b in enumerate(new_bones) if b['name'] == 'neck')

# The spine bone goes from pelvis to neck joint
pelvis_joint_id = id_remap[jmap['pelvis']]
neck_joint_id = id_remap[jmap['neck']]

# Create merged spine bone (reuse spine_lower entry)
spine_bone = new_bones[spine_lower_idx]
spine_bone['name'] = 'spine'
spine_bone['role'] = 'spine'
spine_bone['parent_joint'] = pelvis_joint_id
spine_bone['child_joint'] = neck_joint_id
# start stays at pelvis, end becomes neck joint position
spine_bone['end'] = list(joints[jmap['neck']]['position'])
# Update length and world_length
import math
start_v = np.array(spine_bone['start'], dtype=float)
end_v = np.array(spine_bone['end'], dtype=float)
vlen = float(np.linalg.norm(end_v - start_v))
spine_bone['length'] = vlen
spine_bone['world_length'] = vlen * VOXEL_SIZE
# mass: count voxels in the merged bounds
bmin = spine_bone.get('voxel_bounds_min', [int(min(start_v[0], end_v[0])), int(min(start_v[1], end_v[1])), int(min(start_v[2], end_v[2]))])
bmax = spine_bone.get('voxel_bounds_max', [int(max(start_v[0], end_v[0])), int(max(start_v[1], end_v[1])), int(max(start_v[2], end_v[2]))])
# Expand bounds to cover spine_upper + neck voxels too
upper_b = new_bones[spine_upper_idx]
if 'voxel_bounds_min' in upper_b:
    bmin = [min(bmin[i], upper_b['voxel_bounds_min'][i]) for i in range(3)]
    bmax = [max(bmax[i], upper_b['voxel_bounds_max'][i]) for i in range(3)]
neck_b = new_bones[neck_bone_idx]
if 'voxel_bounds_min' in neck_b:
    bmin = [min(bmin[i], neck_b['voxel_bounds_min'][i]) for i in range(3)]
    bmax = [max(bmax[i], neck_b['voxel_bounds_max'][i]) for i in range(3)]
spine_bone['voxel_bounds_min'] = bmin
spine_bone['voxel_bounds_max'] = bmax
# Count voxels for mass
vx_count = 0
for x in range(bmin[0], bmax[0]+1):
    for y in range(bmin[1], bmax[1]+1):
        for z in range(bmin[2], bmax[2]+1):
            if voxels[x,y,z] != 0:
                vx_count += 1
from material_library import get_material_mass
spine_bone['mass'] = vx_count * get_material_mass(BONE_MAT)

# Remove spine_upper and neck bones from list
del_indices = sorted([spine_upper_idx, neck_bone_idx], reverse=True)
for idx in del_indices:
    del new_bones[idx]

# --- Step 4: Make feet collider-only ---
for b in new_bones:
    if b['role'] == 'foot':
        b['colliderOnly'] = True
        # foot's parent_joint was ankle (removed); remap to knee
        side = b['side']
        knee_name = f'{"left" if side == "L" else "right"}_knee'
        b['parent_joint'] = id_remap[jmap[knee_name]]
        b['child_joint'] = -1

# --- Step 5: Update collar bones to parent from neck instead of chest ---
for b in new_bones:
    if b['role'] == 'collar':
        b['parent_joint'] = neck_joint_id

# --- Step 6: Update root_joint ---
skel['root_joint'] = pelvis_joint_id

# --- Step 7: Repaint removed joint voxels to bone material ---
for jname in remove_joint_names:
    j = joints[jmap[jname]]
    if 'voxel_bounds_min' in j:
        bmin = j['voxel_bounds_min']
        bmax = j['voxel_bounds_max']
        for x in range(max(0, bmin[0]), min(voxels.shape[0], bmax[0]+1)):
            for y in range(max(0, bmin[1]), min(voxels.shape[1], bmax[1]+1)):
                for z in range(max(0, bmin[2]), min(voxels.shape[2], bmax[2]+1)):
                    if voxels[x, y, z] == JOINT_MAT:
                        voxels[x, y, z] = BONE_MAT

# --- Step 8: Update AMS metadata ---
ams = skel.get('ams', {})
if ams:
    ams['version'] = 4
    # Rebuild chain_map
    chain_map = {}
    for b in new_bones:
        ch = b.get('chain', '')
        if ch:
            chain_map.setdefault(ch, []).append(b['name'])
    ams['chain_map'] = chain_map
    # Rebuild reach_envelopes (same logic, just fewer bones)
    reach = {}
    for ch, _ in chain_map.items():
        if ch.startswith('arm_'):
            side = ch.split('_')[1]
            ua = next((b for b in new_bones if b.get('chain') == ch and b['role'] == 'upper_arm'), None)
            fa = next((b for b in new_bones if b.get('chain') == ch and b['role'] == 'forearm'), None)
            if ua and fa:
                reach[ch] = {
                    'origin_joint': f'{"left" if side == "L" else "right"}_shoulder',
                    'proximal_reach': ua['world_length'],
                    'distal_reach': ua['world_length'] + fa['world_length'],
                    'proximal_contact': 'elbow', 'distal_contact': 'hand',
                }
        elif ch.startswith('leg_'):
            side = ch.split('_')[1]
            th = next((b for b in new_bones if b.get('chain') == ch and b['role'] == 'thigh'), None)
            sh = next((b for b in new_bones if b.get('chain') == ch and b['role'] == 'shin'), None)
            if th and sh:
                reach[ch] = {
                    'origin_joint': f'{"left" if side == "L" else "right"}_hip',
                    'proximal_reach': th['world_length'],
                    'distal_reach': th['world_length'] + sh['world_length'],
                    'proximal_contact': 'knee', 'distal_contact': 'foot',
                }
    ams['reach_envelopes'] = reach

skel['bones'] = new_bones
skel['joints'] = new_joints

# --- Step 9: Save ---
out_path = '../My project/Assets/StreamingAssets/ActorSymmetricV4.stasset'
save_stasset(out_path, voxels, skel)

# Verify
v2, d2, s2 = load_stasset(out_path)
print(f"\nSaved to {out_path}")
print(f"After: {len(s2['bones'])} bones, {len(s2['joints'])} joints")
print("Joints:")
for j in s2['joints']:
    print(f"  {j['id']}: {j['name']} ({j['type']})")
print("Bones:")
for b in s2['bones']:
    co = "[COLLIDER-ONLY]" if b.get('colliderOnly') else ""
    print(f"  {b['name']:20s} role={b['role']:10s} chain={b.get('chain',''):6s} {co}")
