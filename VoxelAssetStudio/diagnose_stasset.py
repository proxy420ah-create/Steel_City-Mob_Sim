"""
Diagnostic tool to inspect .stasset files and verify data integrity.

Usage: python diagnose_stasset.py [file1.stasset file2.stasset ...]
Defaults to a few representative Steel City models if no args given.
"""

from stasset_io import load_stasset
import os
import sys

output_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                           "..", "Assets", "StreamingAssets", "voxel_buildings")

files_to_check = sys.argv[1:] if len(sys.argv) > 1 else [
    "barber_0.stasset",
    "apartment_block_0.stasset",
    "vehicle_civilian_car_0.stasset",
]

print("🔍 Diagnosing .stasset files")
print("=" * 70)

for filename in files_to_check:
    filepath = filename if os.path.isabs(filename) or os.path.exists(filename) else os.path.join(output_dir, filename)
    if not os.path.exists(filepath):
        print(f"\n❌ {filename}: NOT FOUND")
        continue
    
    voxels, dims, _skeleton = load_stasset(filepath)
    
    print(f"\n📄 {os.path.basename(filepath)}")
    print(f"   Dimensions: {dims[0]}×{dims[1]}×{dims[2]}")
    print(f"   Shape: {voxels.shape}")
    print(f"   Total voxels: {voxels.size:,}")
    print(f"   Non-air voxels: {(voxels != 0).sum():,}")
    
    # Sample some voxels
    print(f"\n   Sample voxels:")
    print(f"   [0, 0, 0] = {voxels[0, 0, 0]}")
    print(f"   [0, 5, 0] = {voxels[0, 5, 0]}")
    print(f"   [0, 10, 0] = {voxels[0, 10, 0]}")
    print(f"   [0, 15, 0] = {voxels[0, 15, 0] if dims[1] > 15 else 'N/A'}")
    print(f"   [0, 19, 0] = {voxels[0, 19, 0] if dims[1] > 19 else 'N/A'}")
    
    # Check if it's the multi-material test
    if "MultiMaterial" in filename:
        print(f"\n   Expected: Bottom half (Y=0-9) = 11 (UNIFORM)")
        print(f"   Expected: Top half (Y=10-19) = 4 (FLESH)")
        
        # Check a vertical slice
        print(f"\n   Vertical slice at X=0, Z=0:")
        for y in range(min(20, dims[1])):
            mat = voxels[0, y, 0]
            mat_name = "UNIFORM" if mat == 11 else ("FLESH" if mat == 4 else f"Unknown({mat})")
            print(f"      Y={y:2d}: material {mat:2d} ({mat_name})")
