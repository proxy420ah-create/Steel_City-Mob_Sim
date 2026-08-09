import json
import numpy as np
from stasset_io import load_stasset, save_stasset
from skeleton_generator_actor_v2 import generate_actor_skeleton_v2

def regenerate_actorsymmetric_v2():
    """Generate ActorSymmetricV2.stasset with AMS-enhanced skeleton metadata."""
    
    # Load current ActorSymmetric voxel data (to preserve visual model)
    print("=== LOADING CURRENT ACTORSYMMETRIC VOXELS ===")
    sym_voxels, sym_dims, sym_skeleton = load_stasset('../My project/Assets/StreamingAssets/ActorSymmetric.stasset')
    print(f"Original ActorSymmetric: {sym_dims}, {np.count_nonzero(sym_voxels)} voxels")
    
    # Generate new v2 skeleton
    print("\n=== GENERATING V2 SKELETON ===")
    new_voxels, new_skeleton = generate_actor_skeleton_v2()
    print(f"Generated v2 skeleton: {len(new_skeleton['bones'])} bones, {len(new_skeleton['joints'])} joints")
    
    # Verify dimensions match
    if new_voxels.shape != sym_dims:
        print(f"ERROR: Dimension mismatch! Generated {new_voxels.shape}, expected {sym_dims}")
        return False
    
    # Use ORIGINAL voxels (already symmetric/trimmed) + new V2 skeleton metadata
    final_voxels = sym_voxels.copy()
    final_skeleton = new_skeleton

    # Re-paint joint voxels: ensure all voxels within joint bounds are JOINT_MATERIAL
    # The original model has some joint-bound positions painted as BONE or empty (from trimming).
    # This aligns the voxel data with the joint metadata.
    JOINT_MAT = 21
    BONE_MAT = 12
    repainted = 0
    for j in new_skeleton.get('joints', []):
        if 'voxel_bounds_min' in j:
            bmin = j['voxel_bounds_min']
            bmax = j['voxel_bounds_max']
            for x in range(max(0, bmin[0]), min(final_voxels.shape[0], bmax[0] + 1)):
                for y in range(max(0, bmin[1]), min(final_voxels.shape[1], bmax[1] + 1)):
                    for z in range(max(0, bmin[2]), min(final_voxels.shape[2], bmax[2] + 1)):
                        if final_voxels[x, y, z] != JOINT_MAT:
                            final_voxels[x, y, z] = JOINT_MAT
                            repainted += 1
    print(f"\nRe-painted {repainted} voxels to JOINT_MATERIAL within joint bounds")

    # Fill collar bone gaps: original model has air at x=7,x=9 between chest and shoulders
    # Fill with BONE_MATERIAL to connect the skeleton
    chest_y = 13
    cz = 4
    arm_z0 = cz - 1  # z=3
    arm_z1 = cz      # z=4
    collar_filled = 0
    for gap_x in [7, 9]:  # x=7 (left collar gap), x=9 (right collar gap)
        for z in range(arm_z0, arm_z1 + 1):
            if final_voxels[gap_x, chest_y, z] == 0:
                final_voxels[gap_x, chest_y, z] = BONE_MAT
                collar_filled += 1
    print(f"Filled {collar_filled} collar bone gap voxels (x=7, x=9 at y=13)")
    
    # Save as ActorSymmetricV2
    output_path = '../My project/Assets/StreamingAssets/ActorSymmetricV2.stasset'
    print(f"\n=== SAVING ACTORSYMMETRIC V2 ===")
    save_stasset(output_path, final_voxels, final_skeleton)
    
    # Verify the save
    print("\n=== VERIFICATION ===")
    verify_voxels, verify_dims, verify_skeleton = load_stasset(output_path)
    print(f"Verified: {verify_dims}, {np.count_nonzero(verify_voxels)} voxels")
    print(f"Verified skeleton: {len(verify_skeleton.get('bones', []))} bones, {len(verify_skeleton.get('joints', []))} joints")
    
    # Check AMS metadata
    ams = verify_skeleton.get('ams', {})
    if ams:
        print(f"AMS metadata: version={ams.get('version')}, voxel_size={ams.get('voxel_size')}")
        print(f"Chains: {list(ams.get('chain_map', {}).keys())}")
        for ch, env in ams.get('reach_envelopes', {}).items():
            print(f"  {ch}: proximal={env['proximal_reach']:.2f}, distal={env['distal_reach']:.2f}")
    else:
        print("WARNING: No AMS metadata found!")
    
    # Check bone metadata
    bones_with_world_len = sum(1 for b in verify_skeleton.get('bones', []) if 'world_length' in b)
    bones_with_chain = sum(1 for b in verify_skeleton.get('bones', []) if 'chain' in b)
    bones_with_contact = sum(1 for b in verify_skeleton.get('bones', []) if 'contact_role' in b)
    print(f"\nBone metadata:")
    print(f"  world_length: {bones_with_world_len}/{len(verify_skeleton.get('bones', []))}")
    print(f"  chain: {bones_with_chain}/{len(verify_skeleton.get('bones', []))}")
    print(f"  contact_role: {bones_with_contact}/{len(verify_skeleton.get('bones', []))}")
    
    # Print all bones with new metadata
    print("\n=== BONE DETAILS ===")
    for b in verify_skeleton.get('bones', []):
        print(f"  {b['name']:20s} role={b.get('role',''):10s} side={b.get('side',''):2s} "
              f"chain={b.get('chain',''):6s} contact={b.get('contact_role','none'):7s} "
              f"vlen={b.get('length',0):.1f} wlen={b.get('world_length',0):.2f} "
              f"mass={b.get('mass',0):5.1f}")
    
    return True

if __name__ == "__main__":
    regenerate_actorsymmetric_v2()
