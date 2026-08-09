"""
Add a single BALL 'waist' joint between the pelvis ROOT and the spine bone in
ActorSymmetricV4.stasset.

Before: pelvis (ROOT) --[ConfigurableJoint, special-cased in C#]--> spine
After:  pelvis (ROOT) --[waist joint, BALL]--> spine

No new bone/body is introduced — the spine bone still connects directly to
the pelvis Rigidbody in Unity (VoxelActor2Joints.BuildJoints resolves the
parent body via GetParentBone(), which returns null for any bone whose
parent_joint == root_joint OR isn't any other bone's child_joint — so the
physical connection is unchanged). Only the pivot's TYPE changes from ROOT
to BALL, which routes VoxelActor2Joints.ConfigureAngular into the existing
generic Ball-limit branch instead of the special-cased spine+Root block.
"""
import sys
sys.path.insert(0, '.')
from stasset_io import load_stasset, save_stasset

SRC = '../My project/Assets/StreamingAssets/ActorSymmetricV4.stasset'
DST = '../My project/Assets/StreamingAssets/ActorSymmetricV4_1.stasset'

# Waist swing/twist limits (degrees). Tune these in Voxel Studio's joint
# editor later if needed — same fields the Rigging Panel's BALL joint UI uses.
WAIST_MAX_ANGLE_X = 90.0
WAIST_MAX_ANGLE_Y = 45.0
WAIST_MAX_ANGLE_Z = 45.0

voxels, dims, skel = load_stasset(SRC)
joints = skel['joints']
bones = skel['bones']

jmap = {j['name']: j['id'] for j in joints}
if 'waist' in jmap:
    print("Joint 'waist' already exists — aborting (no changes made).")
    sys.exit(0)

pelvis_joint = next(j for j in joints if j['id'] == skel['root_joint'])
spine_bone = next(b for b in bones if b['role'] == 'spine')

if spine_bone['parent_joint'] != pelvis_joint['id']:
    print(f"Spine's parent_joint ({spine_bone['parent_joint']}) is not the "
          f"pelvis ROOT joint ({pelvis_joint['id']}) — aborting to avoid "
          f"clobbering an unexpected rig shape.")
    sys.exit(1)

new_waist_id = len(joints)
waist_joint = {
    'id': new_waist_id,
    'name': 'waist',
    'type': 'BALL',
    'position': list(pelvis_joint['position']),
    'max_angle_x': WAIST_MAX_ANGLE_X,
    'max_angle_y': WAIST_MAX_ANGLE_Y,
    'max_angle_z': WAIST_MAX_ANGLE_Z,
    'chain_origin': False,
}
joints.append(waist_joint)

spine_bone['parent_joint'] = new_waist_id

skel['joints'] = joints
skel['bones'] = bones

save_stasset(DST, voxels, skel)

# Verify
v2, d2, s2 = load_stasset(DST)
print(f"\nSaved to {DST}")
print(f"(original {SRC} left untouched)")
print(f"Joints: {len(s2['joints'])}")
for j in s2['joints']:
    print(f"  {j['id']}: {j['name']} ({j['type']})" +
          (f" limits=({j.get('max_angle_x')},{j.get('max_angle_y')},{j.get('max_angle_z')})"
           if j['type'] == 'BALL' else ''))
spine = next(b for b in s2['bones'] if b['role'] == 'spine')
print(f"\nSpine bone parent_joint = {spine['parent_joint']} "
      f"({'waist' if spine['parent_joint'] == new_waist_id else '???'})")
