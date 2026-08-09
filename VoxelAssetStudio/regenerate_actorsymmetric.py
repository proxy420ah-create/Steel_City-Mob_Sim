import json
import numpy as np
from stasset_io import load_stasset, save_stasset
from skeleton_generator_actor import generate_actor_skeleton

def regenerate_actorsymmetric():
    """Regenerate ActorSymmetric with proper skeleton using Actor as reference"""
    
    # Load current ActorSymmetric voxel data
    print("=== LOADING ACTUAL ACTORSYMMETRIC VOXELS ===")
    sym_voxels, sym_dims, sym_skeleton = load_stasset('../My project/Assets/StreamingAssets/ActorSymmetric.stasset')
    print(f"Original ActorSymmetric: {sym_dims}, {np.count_nonzero(sym_voxels)} voxels")
    
    # Generate new skeleton using Actor's generator (same dimensions)
    print("\n=== GENERATING NEW SKELETON ===")
    new_voxels, new_skeleton = generate_actor_skeleton()
    print(f"Generated skeleton: {len(new_skeleton['bones'])} bones, {len(new_skeleton['joints'])} joints")
    
    # Verify dimensions match
    if new_voxels.shape != sym_dims:
        print(f"ERROR: Dimension mismatch! Generated {new_voxels.shape}, expected {sym_dims}")
        return False
    
    # Preserve the original ActorSymmetric voxel data but add the new skeleton
    print("\n=== PRESERVING ORIGINAL VOXELS, ADDING SKELETON ===")
    
    # Count materials in original vs generated for comparison
    orig_materials, orig_counts = np.unique(sym_voxels, return_counts=True)
    gen_materials, gen_counts = np.unique(new_voxels, return_counts=True)
    
    print("Original ActorSymmetric materials:")
    for mat, count in zip(orig_materials, orig_counts):
        if mat != 0:
            print(f"  Material {mat}: {count} voxels")
    
    print("\nGenerated Actor materials:")
    for mat, count in zip(gen_materials, gen_counts):
        if mat != 0:
            print(f"  Material {mat}: {count} voxels")
    
    # Use original voxels but add the new skeleton
    final_voxels = sym_voxels.copy()
    final_skeleton = new_skeleton
    
    # Save the updated ActorSymmetric
    output_path = '../My project/Assets/StreamingAssets/ActorSymmetric.stasset'
    print(f"\n=== SAVING UPDATED ACTORSYMMETRIC ===")
    save_stasset(output_path, final_voxels, final_skeleton)
    
    # Verify the save
    print("\n=== VERIFICATION ===")
    verify_voxels, verify_dims, verify_skeleton = load_stasset(output_path)
    print(f"Verified: {verify_dims}, {np.count_nonzero(verify_voxels)} voxels")
    print(f"Verified skeleton: {len(verify_skeleton.get('bones', []))} bones, {len(verify_skeleton.get('joints', []))} joints")
    
    # Check that voxel bounds are present
    bones_with_bounds = sum(1 for bone in verify_skeleton.get('bones', []) 
                          if 'voxel_bounds_min' in bone and 'voxel_bounds_max' in bone)
    joints_with_bounds = sum(1 for joint in verify_skeleton.get('joints', []) 
                           if 'voxel_bounds_min' in joint and 'voxel_bounds_max' in joint)
    
    print(f"Bones with voxel bounds: {bones_with_bounds}/{len(verify_skeleton.get('bones', []))}")
    print(f"Joints with voxel bounds: {joints_with_bounds}/{len(verify_skeleton.get('joints', []))}")
    
    return True

if __name__ == "__main__":
    regenerate_actorsymmetric()
