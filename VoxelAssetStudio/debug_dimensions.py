from stasset_io import load_stasset
import os

output_dir = "../My project/Assets/StreamingAssets"

files_to_check = [
    "Ground_Voxel.stasset",
    "Test_Torso.stasset",
    "Test_Solid_TwoMaterial.stasset",
    "Character_V1.stasset"
]

print("📏 Checking .stasset file dimensions:")
print("=" * 60)

for filename in files_to_check:
    filepath = os.path.join(output_dir, filename)
    if os.path.exists(filepath):
        voxels, dims = load_stasset(filepath)
        print(f"\n{filename}:")
        print(f"  Dimensions: {dims[0]}×{dims[1]}×{dims[2]}")
        print(f"  Total voxels: {dims[0] * dims[1] * dims[2]:,}")
        print(f"  Non-air voxels: {(voxels != 0).sum():,}")
    else:
        print(f"\n{filename}: NOT FOUND")
