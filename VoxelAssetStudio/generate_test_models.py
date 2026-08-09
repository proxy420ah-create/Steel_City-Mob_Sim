"""
Generate incremental V2 test models to isolate the spinning issue.

Produces 4 .stasset files, each adding one change on top of V1:
  test1: V1 voxels + V1 skeleton + AMS metadata fields only
  test2: test1 + collar gap fill (4 new bone voxels)
  test3: test2 + joint re-painting (bone→joint, air→joint in bounds)
  test4: test3 + V2 shoulder bounds (3-wide → 1-wide) = full V2
"""
import copy
import numpy as np
from stasset_io import load_stasset, save_stasset

V1_PATH = '../My project/Assets/StreamingAssets/ActorSymmetric.stasset'
OUT_DIR = '../My project/Assets/StreamingAssets/'

JOINT_MAT = 21
BONE_MAT = 12


def add_ams_metadata(skeleton):
    """Add AMS fields to bones/joints without changing bounds or positions."""
    VOXEL_SIZE = 0.25

    chain_map = {}
    for b in skeleton['bones']:
        wl = round(b['length'] * VOXEL_SIZE, 4)
        b['world_length'] = wl
        b.setdefault('chain', '')
        b.setdefault('contact_role', 'none')
        b.setdefault('contact_offset', [wl, 0.0, 0.0])
        b.setdefault('com_offset', [wl * 0.5, 0.0, 0.0])
        ch = b.get('chain', '')
        if ch:
            chain_map.setdefault(ch, []).append(b['name'])

    # Assign chains based on role/side if not already set
    for b in skeleton['bones']:
        if b.get('chain'):
            continue
        role = b.get('role', '')
        side = b.get('side', '')
        if role in ('thigh', 'shin', 'foot'):
            b['chain'] = f'leg_{side}'
        elif role in ('upper_arm', 'forearm', 'collar'):
            b['chain'] = f'arm_{side}'
        elif role in ('spine_lower', 'spine_upper'):
            b['chain'] = 'spine'
        elif role in ('neck', 'head'):
            b['chain'] = 'head'
        else:
            b['chain'] = ''
        ch = b['chain']
        if ch:
            chain_map.setdefault(ch, []).append(b['name'])

    # Contact roles
    for b in skeleton['bones']:
        if b.get('contact_role', 'none') != 'none':
            continue
        role = b.get('role', '')
        if role in ('thigh', 'shin', 'upper_arm', 'forearm'):
            b['contact_role'] = 'tip'
        elif role in ('foot',):
            b['contact_role'] = 'surface'
        else:
            b['contact_role'] = 'none'

    # Chain origins on joints
    for j in skeleton['joints']:
        j.setdefault('chain_origin', False)
    for j in skeleton['joints']:
        if j['name'] in ('left_shoulder', 'right_shoulder', 'left_hip', 'right_hip'):
            j['chain_origin'] = True

    # Reach envelopes
    reach = {}
    for chain_name in chain_map:
        if chain_name.startswith('arm_'):
            side = chain_name.split('_')[1]
            ua = next((b for b in skeleton['bones'] if b.get('chain') == chain_name and b['role'] == 'upper_arm'), None)
            fa = next((b for b in skeleton['bones'] if b.get('chain') == chain_name and b['role'] == 'forearm'), None)
            if ua and fa:
                reach[chain_name] = {
                    'origin_joint': f'{"left" if side == "L" else "right"}_shoulder',
                    'proximal_reach': ua['world_length'],
                    'distal_reach': ua['world_length'] + fa['world_length'],
                    'proximal_contact': 'elbow',
                    'distal_contact': 'hand',
                }
        elif chain_name.startswith('leg_'):
            side = chain_name.split('_')[1]
            th = next((b for b in skeleton['bones'] if b.get('chain') == chain_name and b['role'] == 'thigh'), None)
            sh = next((b for b in skeleton['bones'] if b.get('chain') == chain_name and b['role'] == 'shin'), None)
            if th and sh:
                reach[chain_name] = {
                    'origin_joint': f'{"left" if side == "L" else "right"}_hip',
                    'proximal_reach': th['world_length'],
                    'distal_reach': th['world_length'] + sh['world_length'],
                    'proximal_contact': 'knee',
                    'distal_contact': 'foot',
                }

    skeleton['ams'] = {
        'version': 2,
        'voxel_size': VOXEL_SIZE,
        't_pose_forward': [0.0, 0.0, 1.0],
        't_pose_up': [0.0, 1.0, 0.0],
        't_pose_right': [1.0, 0.0, 0.0],
        'chain_map': chain_map,
        'reach_envelopes': reach,
    }
    return skeleton


def fill_collar_gaps(voxels):
    """Fill x=7, x=9 at y=13, z=3..4 with BONE_MATERIAL."""
    count = 0
    for gap_x in [7, 9]:
        for z in [3, 4]:
            if voxels[gap_x, 13, z] == 0:
                voxels[gap_x, 13, z] = BONE_MAT
                count += 1
    return count


def repaint_joints(voxels, skeleton):
    """Re-paint all voxels within joint bounds as JOINT_MATERIAL."""
    count = 0
    for j in skeleton.get('joints', []):
        if 'voxel_bounds_min' in j:
            bmin = j['voxel_bounds_min']
            bmax = j['voxel_bounds_max']
            for x in range(max(0, bmin[0]), min(voxels.shape[0], bmax[0] + 1)):
                for y in range(max(0, bmin[1]), min(voxels.shape[1], bmax[1] + 1)):
                    for z in range(max(0, bmin[2]), min(voxels.shape[2], bmax[2] + 1)):
                        if voxels[x, y, z] != JOINT_MAT:
                            voxels[x, y, z] = JOINT_MAT
                            count += 1
    return count


def shrink_shoulder_bounds(skeleton):
    """Change shoulder bounds from 3-wide to 1-wide (V2 style)."""
    for j in skeleton.get('joints', []):
        if j['name'] == 'left_shoulder':
            j['voxel_bounds_min'][0] = 6
            j['voxel_bounds_max'][0] = 6
        elif j['name'] == 'right_shoulder':
            j['voxel_bounds_min'][0] = 10
            j['voxel_bounds_max'][0] = 10


# Load V1
print("=== Loading V1 ===")
v1_voxels, v1_dims, v1_skel = load_stasset(V1_PATH)
print(f"  {v1_dims}, {np.count_nonzero(v1_voxels)} voxels, {len(v1_skel['bones'])} bones, {len(v1_skel['joints'])} joints")

# Test 1: V1 voxels + V1 skeleton + AMS metadata only
print("\n=== Test 1: AMS metadata only ===")
t1_voxels = v1_voxels.copy()
t1_skel = copy.deepcopy(v1_skel)
add_ams_metadata(t1_skel)
save_stasset(OUT_DIR + 'ActorSymmetricV2_test1.stasset', t1_voxels, t1_skel)
print(f"  {np.count_nonzero(t1_voxels)} voxels — no voxel/bounds changes, just AMS fields")

# Test 2: + collar gap fill
print("\n=== Test 2: + collar gap fill ===")
t2_voxels = t1_voxels.copy()
t2_skel = copy.deepcopy(t1_skel)
filled = fill_collar_gaps(t2_voxels)
save_stasset(OUT_DIR + 'ActorSymmetricV2_test2.stasset', t2_voxels, t2_skel)
print(f"  {np.count_nonzero(t2_voxels)} voxels — +{filled} collar gap voxels")

# Test 3: + joint re-painting
print("\n=== Test 3: + joint re-painting ===")
t3_voxels = t2_voxels.copy()
t3_skel = copy.deepcopy(t2_skel)
repainted = repaint_joints(t3_voxels, t3_skel)
save_stasset(OUT_DIR + 'ActorSymmetricV2_test3.stasset', t3_voxels, t3_skel)
print(f"  {np.count_nonzero(t3_voxels)} voxels — +{repainted} joint re-paints")

# Test 4: + shoulder bounds shrink (full V2 equivalent)
print("\n=== Test 4: + shoulder bounds shrink (full V2) ===")
t4_voxels = t3_voxels.copy()
t4_skel = copy.deepcopy(t3_skel)
shrink_shoulder_bounds(t4_skel)
save_stasset(OUT_DIR + 'ActorSymmetricV2_test4.stasset', t4_voxels, t4_skel)
print(f"  {np.count_nonzero(t4_voxels)} voxels — shoulder bounds 3-wide -> 1-wide")

print("\n=== DONE ===")
print("Test files in StreamingAssets:")
print("  ActorSymmetricV2_test1.stasset — AMS metadata only")
print("  ActorSymmetricV2_test2.stasset — + collar gap fill")
print("  ActorSymmetricV2_test3.stasset — + joint re-painting")
print("  ActorSymmetricV2_test4.stasset — + shoulder bounds shrink (= full V2)")
print("\nTest each one in Unity. Switch assetFileName and play.")
print("The first one that spins identifies the culprit change.")
