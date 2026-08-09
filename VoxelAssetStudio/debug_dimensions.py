from stasset_io import load_stasset
import os
import sys

output_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                           "..", "Assets", "StreamingAssets", "voxel_buildings")

files_to_check = sys.argv[1:] if len(sys.argv) > 1 else [
    "barber_0.stasset",
    "bakery_0.stasset",
    "butcher_0.stasset",
    "diner_0.stasset",
    "garage_0.stasset",
    "apartments_0.stasset",
    "apartment_block_0.stasset",
    "casino_0.stasset",
    "speakeasy_0.stasset",
    "police_station_6.stasset",
    "hq_block_3.stasset",
    "vehicle_civilian_car_0.stasset",
    "character_hoodlum_0.stasset",
]

print("📏 Checking .stasset file dimensions:")
print("=" * 60)

for filename in files_to_check:
    filepath = filename if os.path.isabs(filename) or os.path.exists(filename) else os.path.join(output_dir, filename)
    if os.path.exists(filepath):
        voxels, dims, _skeleton = load_stasset(filepath)
        print(f"\n{os.path.basename(filepath)}:")
        print(f"  Dimensions: {dims[0]}×{dims[1]}×{dims[2]}")
        print(f"  Total voxels: {dims[0] * dims[1] * dims[2]:,}")
        print(f"  Non-air voxels: {(voxels != 0).sum():,}")
    else:
        print(f"\n{filename}: NOT FOUND")
