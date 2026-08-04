"""
Debug numpy flatten order to understand the mismatch.
"""

import numpy as np

# Create a small test array
width, height, depth = 2, 3, 2
grid = np.arange(width * height * depth, dtype=np.uint16).reshape((width, height, depth))

print("🔍 Understanding numpy flatten order")
print("=" * 70)
print(f"Grid shape: {grid.shape} (width={width}, height={height}, depth={depth})")
print()
print("3D array:")
for z in range(depth):
    print(f"  Z={z}:")
    for y in range(height):
        row = [grid[x, y, z] for x in range(width)]
        print(f"    Y={y}: {row}")
print()

# Flatten with C-order (default)
flat_c = grid.flatten()  # or grid.flatten('C')
print("Flattened (C-order, default):")
print(f"  {list(flat_c)}")
print()

# Flatten with Fortran-order
flat_f = grid.flatten('F')
print("Flattened (Fortran-order):")
print(f"  {list(flat_f)}")
print()

# Check what index [0, 2, 0] maps to with our formula
x, y, z = 0, 2, 0
our_index = x + y * width + z * width * height
print(f"Our index formula for [{x}, {y}, {z}]:")
print(f"  index = {x} + {y} * {width} + {z} * {width} * {height}")
print(f"  index = {our_index}")
print(f"  grid[{x}, {y}, {z}] = {grid[x, y, z]}")
print(f"  flat_c[{our_index}] = {flat_c[our_index]}")
print(f"  Match: {grid[x, y, z] == flat_c[our_index]}")
