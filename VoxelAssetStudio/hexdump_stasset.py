"""
Hex dump tool to inspect raw bytes in .stasset files.

Usage: python hexdump_stasset.py [path/to/file.stasset]
Defaults to the Steel City vehicle model if no path is given.
"""

import struct
import os
import sys

output_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                           "..", "Assets", "StreamingAssets", "voxel_buildings")
filepath = sys.argv[1] if len(sys.argv) > 1 else os.path.join(output_dir, "vehicle_civilian_car_0.stasset")

with open(filepath, 'rb') as f:
    # Read header
    magic = f.read(4)
    version = struct.unpack('B', f.read(1))[0]
    flags = struct.unpack('B', f.read(1))[0]
    width = struct.unpack('<H', f.read(2))[0]
    height = struct.unpack('<H', f.read(2))[0]
    depth = struct.unpack('<H', f.read(2))[0]
    reserved = struct.unpack('<I', f.read(4))[0]
    
    print(f"📄 {os.path.basename(filepath)}")
    print(f"   Magic: {magic}")
    print(f"   Version: {version}")
    print(f"   Dimensions: {width}×{height}×{depth}")
    print(f"   Total voxels: {width * height * depth}")
    
    # Read voxel data
    voxel_count = width * height * depth
    voxels = []
    for i in range(voxel_count):
        voxel_bytes = f.read(2)
        voxel_value = struct.unpack('<H', voxel_bytes)[0]
        voxels.append(voxel_value)
    
    # Check specific indices
    print(f"\n🔍 Checking specific voxels:")
    
    # Calculate indices for [0, y, 0] where y = 0, 5, 10, 15
    for y in [0, 5, 10, 15]:
        index = 0 + y * width + 0 * width * height
        value = voxels[index]
        mat_name = "UNIFORM" if value == 11 else ("FLESH" if value == 4 else f"Unknown({value})")
        print(f"   [0, {y:2d}, 0] → index {index:3d} → value {value:2d} ({mat_name})")
    
    # Show a range of indices around index 90
    print(f"\n📊 Indices 85-95 (around index 90 = [0,15,0]):")
    for i in range(85, 96):
        value = voxels[i]
        mat_name = "UNIFORM" if value == 11 else ("FLESH" if value == 4 else f"Unknown({value})")
        # Calculate what [x,y,z] this index corresponds to
        z = i // (width * height)
        remainder = i % (width * height)
        y = remainder // width
        x = remainder % width
        print(f"   Index {i:3d} = [{x},{y:2d},{z}] → value {value:2d} ({mat_name})")
