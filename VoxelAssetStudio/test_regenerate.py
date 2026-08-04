"""
Regenerate Test_3 and immediately verify it.
"""

import numpy as np
from stasset_io import save_stasset, load_stasset
import os

UNIFORM = 11
FLESH = 4

width = 6
height = 20
depth = 4

# Generate
grid = np.zeros((width, height, depth), dtype=np.uint16)
grid[:, 0:10, :] = UNIFORM
grid[:, 10:20, :] = FLESH

print("🔍 Before saving:")
print(f"  grid[0, 15, 0] = {grid[0, 15, 0]} (expected: {FLESH})")

# Save
output_dir = "../My project/Assets/StreamingAssets"
filepath = os.path.join(output_dir, "Test_3_Solid_MultiMaterial.stasset")
save_stasset(filepath, grid)

# Immediately reload and check
print("\n🔍 After loading back:")
loaded, dims, skeleton = load_stasset(filepath)
print(f"  loaded[0, 15, 0] = {loaded[0, 15, 0]} (expected: {FLESH})")

# Check if they match
if grid[0, 15, 0] == loaded[0, 15, 0]:
    print("\n✅ Data integrity verified!")
else:
    print(f"\n❌ DATA CORRUPTION! {grid[0, 15, 0]} → {loaded[0, 15, 0]}")
