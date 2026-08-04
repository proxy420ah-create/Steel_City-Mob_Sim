"""
Regenerate ActorSymmetricV5.stasset from skeleton_generator_actor_v5.py.

V5 changes from V4:
  - Waist BALL joint built in (no separate post-processing script)
  - AMS metadata includes root_collider: "capsule" so C# builds a CapsuleCollider
  - V4 assets are left untouched

Usage: cd VoxelAssetStudio && python regenerate_actorsymmetric_v5.py
"""
import sys
sys.path.insert(0, '.')

from stasset_io import save_stasset
from skeleton_generator_actor_v5 import generate_actor_skeleton_v5

DST = '../My project/Assets/StreamingAssets/ActorSymmetricV5.stasset'

voxels, skel = generate_actor_skeleton_v5()
save_stasset(DST, voxels, skel)

print(f"Saved to {DST}")
print(f"Grid: {voxels.shape}")
import numpy as np
print(f"Solid voxels: {int(np.count_nonzero(voxels))}")
print(f"Bones: {len(skel['bones'])}, Joints: {len(skel['joints'])}")
print(f"Root collider: {skel['ams']['root_collider']}")
print(f"AMS version: {skel['ams']['version']}")
print()
for j in skel['joints']:
    extra = ""
    if j['type'] == 'BALL':
        extra = f" limits=({j.get('max_angle_x')},{j.get('max_angle_y')},{j.get('max_angle_z')})"
    print(f"  Joint {j['id']}: {j['name']} ({j['type']}){extra}")
print()
for b in skel['bones']:
    co = " [COLLIDER-ONLY]" if b.get('colliderOnly') else ""
    print(f"  {b['name']:20s} role={b['role']:10s} chain={b.get('chain',''):6s}{co}")
print()
spine = next(b for b in skel['bones'] if b['role'] == 'spine')
waist = next(j for j in skel['joints'] if j['name'] == 'waist')
print(f"Spine parent_joint = {spine['parent_joint']} (waist id={waist['id']})")
