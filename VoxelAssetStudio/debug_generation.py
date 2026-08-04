"""
Debug the generation process to see where voxels get scrambled.
"""

import numpy as np

# Recreate the exact generation logic
width = 6
height = 20
depth = 4

UNIFORM = 11
FLESH = 4

grid = np.zeros((width, height, depth), dtype=np.uint16)

# Bottom half = UNIFORM (green)
grid[:, 0:10, :] = UNIFORM

# Top half = FLESH (pink)
grid[:, 10:20, :] = FLESH

print("🔍 Debugging Test_3_Solid_MultiMaterial generation")
print("=" * 70)
print(f"Grid shape: {grid.shape}")
print(f"Expected: grid[0, 15, 0] = {FLESH} (FLESH)")
print(f"Actual:   grid[0, 15, 0] = {grid[0, 15, 0]}")
print()

# Check the vertical slice
print("Vertical slice at X=0, Z=0:")
for y in range(height):
    value = grid[0, y, 0]
    mat_name = "UNIFORM" if value == UNIFORM else ("FLESH" if value == FLESH else f"Unknown({value})")
    print(f"  Y={y:2d}: {value:2d} ({mat_name})")

print()

# Now flatten and check indices
flattened = grid.flatten()
print(f"Flattened array length: {len(flattened)}")
print()

# Check specific indices
for y in [0, 5, 10, 15]:
    index = 0 + y * width + 0 * width * height
    value = flattened[index]
    mat_name = "UNIFORM" if value == UNIFORM else ("FLESH" if value == FLESH else f"Unknown({value})")
    print(f"[0, {y:2d}, 0] → index {index:3d} → value {value:2d} ({mat_name})")
