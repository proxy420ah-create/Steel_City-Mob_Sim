# Steel Tide: Voxel Asset Studio
# fill_tool.py - Flood fill algorithm for voxel painting

from typing import List, Tuple, Set
from collections import deque
import numpy as np

def flood_fill_3d(voxel_data: np.ndarray, start_pos: Tuple[int, int, int], 
                  target_material: int, max_fill: int = 10000) -> List[Tuple[int, int, int]]:
    """
    3D flood fill algorithm to find connected voxels of same material.
    
    Args:
        voxel_data: 3D numpy array of voxel materials
        start_pos: (x, y, z) starting position
        target_material: Material ID to replace
        max_fill: Maximum voxels to fill (safety limit)
    
    Returns:
        List of (x, y, z) positions that should be filled
    """
    x_start, y_start, z_start = start_pos
    x_max, y_max, z_max = voxel_data.shape
    
    # Get material at start position
    original_material = voxel_data[x_start, y_start, z_start]
    
    # If already the target material, nothing to do
    if original_material == target_material:
        return []
    
    # BFS flood fill
    filled = []
    visited = set()
    queue = deque([start_pos])
    visited.add(start_pos)
    
    # 6-connected neighbors (up, down, left, right, forward, back)
    neighbors = [
        (1, 0, 0), (-1, 0, 0),   # X axis
        (0, 1, 0), (0, -1, 0),   # Y axis
        (0, 0, 1), (0, 0, -1)    # Z axis
    ]
    
    while queue and len(filled) < max_fill:
        x, y, z = queue.popleft()
        filled.append((x, y, z))
        
        # Check all 6 neighbors
        for dx, dy, dz in neighbors:
            nx, ny, nz = x + dx, y + dy, z + dz
            
            # Bounds check
            if not (0 <= nx < x_max and 0 <= ny < y_max and 0 <= nz < z_max):
                continue
            
            # Already visited
            if (nx, ny, nz) in visited:
                continue
            
            # Check if same material as original
            if voxel_data[nx, ny, nz] == original_material:
                queue.append((nx, ny, nz))
                visited.add((nx, ny, nz))
    
    return filled


def flood_fill_2d_slice(voxel_data: np.ndarray, start_pos: Tuple[int, int, int],
                        axis: str, target_material: int, max_fill: int = 10000) -> List[Tuple[int, int, int]]:
    """
    2D flood fill on a single slice (useful for orthographic views).
    
    Args:
        voxel_data: 3D numpy array
        start_pos: (x, y, z) starting position
        axis: 'x', 'y', or 'z' - which axis to slice along
        target_material: Material to fill with
        max_fill: Safety limit
    
    Returns:
        List of positions to fill (only on the specified slice)
    """
    x_start, y_start, z_start = start_pos
    x_max, y_max, z_max = voxel_data.shape
    
    original_material = voxel_data[x_start, y_start, z_start]
    
    if original_material == target_material:
        return []
    
    filled = []
    visited = set()
    queue = deque([start_pos])
    visited.add(start_pos)
    
    # Define 2D neighbors based on axis
    if axis == 'x':
        # Fill in YZ plane at fixed X
        neighbors = [(0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)]
        fixed_coord = x_start
    elif axis == 'y':
        # Fill in XZ plane at fixed Y
        neighbors = [(1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1)]
        fixed_coord = y_start
    else:  # axis == 'z'
        # Fill in XY plane at fixed Z
        neighbors = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0)]
        fixed_coord = z_start
    
    while queue and len(filled) < max_fill:
        x, y, z = queue.popleft()
        
        # Ensure we stay on the same slice
        if axis == 'x' and x != fixed_coord:
            continue
        if axis == 'y' and y != fixed_coord:
            continue
        if axis == 'z' and z != fixed_coord:
            continue
        
        filled.append((x, y, z))
        
        for dx, dy, dz in neighbors:
            nx, ny, nz = x + dx, y + dy, z + dz
            
            if not (0 <= nx < x_max and 0 <= ny < y_max and 0 <= nz < z_max):
                continue
            
            if (nx, ny, nz) in visited:
                continue
            
            if voxel_data[nx, ny, nz] == original_material:
                queue.append((nx, ny, nz))
                visited.add((nx, ny, nz))
    
    return filled


def get_enclosed_region(voxel_data: np.ndarray, start_pos: Tuple[int, int, int],
                        max_fill: int = 10000) -> List[Tuple[int, int, int]]:
    """
    Find all air voxels in an enclosed region (useful for filling hollow objects).
    
    Args:
        voxel_data: 3D numpy array
        start_pos: Starting position (should be air/0)
        max_fill: Safety limit
    
    Returns:
        List of air voxel positions in the enclosed region
    """
    x_start, y_start, z_start = start_pos
    x_max, y_max, z_max = voxel_data.shape
    
    # Only fill air voxels
    if voxel_data[x_start, y_start, z_start] != 0:
        return []
    
    filled = []
    visited = set()
    queue = deque([start_pos])
    visited.add(start_pos)
    
    neighbors = [
        (1, 0, 0), (-1, 0, 0),
        (0, 1, 0), (0, -1, 0),
        (0, 0, 1), (0, 0, -1)
    ]
    
    # Track if we hit the boundary (not enclosed)
    hit_boundary = False
    
    while queue and len(filled) < max_fill:
        x, y, z = queue.popleft()
        
        # Check if we're at the boundary
        if x == 0 or x == x_max - 1 or y == 0 or y == y_max - 1 or z == 0 or z == z_max - 1:
            hit_boundary = True
            # Don't break - continue to find all connected air
        
        filled.append((x, y, z))
        
        for dx, dy, dz in neighbors:
            nx, ny, nz = x + dx, y + dy, z + dz
            
            if not (0 <= nx < x_max and 0 <= ny < y_max and 0 <= nz < z_max):
                continue
            
            if (nx, ny, nz) in visited:
                continue
            
            # Only fill air voxels
            if voxel_data[nx, ny, nz] == 0:
                queue.append((nx, ny, nz))
                visited.add((nx, ny, nz))
    
    # If we hit boundary, this region is not enclosed
    # Return empty list or return all (depending on desired behavior)
    # For now, return all found air voxels regardless
    return filled
