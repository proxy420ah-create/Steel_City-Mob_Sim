# Steel Tide: Voxel Asset Studio
# shape_tools.py - Line and rectangle drawing algorithms

from typing import List, Tuple
import numpy as np

def bresenham_line_3d(start: Tuple[int, int, int], end: Tuple[int, int, int]) -> List[Tuple[int, int, int]]:
    """
    3D Bresenham line algorithm - returns list of voxel positions along line
    
    Args:
        start: (x, y, z) starting position
        end: (x, y, z) ending position
    
    Returns:
        List of (x, y, z) positions along the line
    """
    x1, y1, z1 = start
    x2, y2, z2 = end
    
    positions = []
    
    dx = abs(x2 - x1)
    dy = abs(y2 - y1)
    dz = abs(z2 - z1)
    
    xs = 1 if x2 > x1 else -1
    ys = 1 if y2 > y1 else -1
    zs = 1 if z2 > z1 else -1
    
    # Driving axis is X
    if dx >= dy and dx >= dz:
        p1 = 2 * dy - dx
        p2 = 2 * dz - dx
        while x1 != x2:
            positions.append((x1, y1, z1))
            x1 += xs
            if p1 >= 0:
                y1 += ys
                p1 -= 2 * dx
            if p2 >= 0:
                z1 += zs
                p2 -= 2 * dx
            p1 += 2 * dy
            p2 += 2 * dz
    
    # Driving axis is Y
    elif dy >= dx and dy >= dz:
        p1 = 2 * dx - dy
        p2 = 2 * dz - dy
        while y1 != y2:
            positions.append((x1, y1, z1))
            y1 += ys
            if p1 >= 0:
                x1 += xs
                p1 -= 2 * dy
            if p2 >= 0:
                z1 += zs
                p2 -= 2 * dy
            p1 += 2 * dx
            p2 += 2 * dz
    
    # Driving axis is Z
    else:
        p1 = 2 * dy - dz
        p2 = 2 * dx - dz
        while z1 != z2:
            positions.append((x1, y1, z1))
            z1 += zs
            if p1 >= 0:
                y1 += ys
                p1 -= 2 * dz
            if p2 >= 0:
                x1 += xs
                p2 -= 2 * dz
            p1 += 2 * dy
            p2 += 2 * dx
    
    # Add final position
    positions.append((x2, y2, z2))
    
    return positions


def draw_rectangle_3d(corner1: Tuple[int, int, int], corner2: Tuple[int, int, int], 
                      mode: str = 'filled') -> List[Tuple[int, int, int]]:
    """
    Draw a 3D rectangle (axis-aligned box face)
    
    Args:
        corner1: (x, y, z) first corner
        corner2: (x, y, z) opposite corner
        mode: 'filled' (solid rectangle) or 'outline' (just edges)
    
    Returns:
        List of (x, y, z) positions
    """
    x1, y1, z1 = corner1
    x2, y2, z2 = corner2
    
    # Ensure min/max order
    min_x, max_x = min(x1, x2), max(x1, x2)
    min_y, max_y = min(y1, y2), max(y1, y2)
    min_z, max_z = min(z1, z2), max(z1, z2)
    
    positions = []
    
    if mode == 'filled':
        # Fill entire rectangular volume
        for x in range(min_x, max_x + 1):
            for y in range(min_y, max_y + 1):
                for z in range(min_z, max_z + 1):
                    positions.append((x, y, z))
    
    elif mode == 'outline':
        # Draw only the 12 edges of the box
        # Bottom face edges
        positions.extend(bresenham_line_3d((min_x, min_y, min_z), (max_x, min_y, min_z)))
        positions.extend(bresenham_line_3d((max_x, min_y, min_z), (max_x, max_y, min_z)))
        positions.extend(bresenham_line_3d((max_x, max_y, min_z), (min_x, max_y, min_z)))
        positions.extend(bresenham_line_3d((min_x, max_y, min_z), (min_x, min_y, min_z)))
        
        # Top face edges
        positions.extend(bresenham_line_3d((min_x, min_y, max_z), (max_x, min_y, max_z)))
        positions.extend(bresenham_line_3d((max_x, min_y, max_z), (max_x, max_y, max_z)))
        positions.extend(bresenham_line_3d((max_x, max_y, max_z), (min_x, max_y, max_z)))
        positions.extend(bresenham_line_3d((min_x, max_y, max_z), (min_x, min_y, max_z)))
        
        # Vertical edges
        positions.extend(bresenham_line_3d((min_x, min_y, min_z), (min_x, min_y, max_z)))
        positions.extend(bresenham_line_3d((max_x, min_y, min_z), (max_x, min_y, max_z)))
        positions.extend(bresenham_line_3d((max_x, max_y, min_z), (max_x, max_y, max_z)))
        positions.extend(bresenham_line_3d((min_x, max_y, min_z), (min_x, max_y, max_z)))
        
        # Remove duplicates
        positions = list(set(positions))
    
    return positions


def draw_rectangle_plane(corner1: Tuple[int, int, int], corner2: Tuple[int, int, int]) -> List[Tuple[int, int, int]]:
    """
    Draw a 2D rectangle on the dominant plane between two points
    Automatically detects which axis is constant (or nearly constant)
    
    Args:
        corner1: (x, y, z) first corner
        corner2: (x, y, z) opposite corner
    
    Returns:
        List of (x, y, z) positions forming a planar rectangle
    """
    x1, y1, z1 = corner1
    x2, y2, z2 = corner2
    
    # Calculate deltas to determine plane
    dx = abs(x2 - x1)
    dy = abs(y2 - y1)
    dz = abs(z2 - z1)
    
    positions = []
    
    # XY plane (Z is constant or smallest)
    if dz <= dx and dz <= dy:
        z = z1  # Use first Z coordinate
        for x in range(min(x1, x2), max(x1, x2) + 1):
            for y in range(min(y1, y2), max(y1, y2) + 1):
                positions.append((x, y, z))
    
    # XZ plane (Y is constant or smallest)
    elif dy <= dx and dy <= dz:
        y = y1  # Use first Y coordinate
        for x in range(min(x1, x2), max(x1, x2) + 1):
            for z in range(min(z1, z2), max(z1, z2) + 1):
                positions.append((x, y, z))
    
    # YZ plane (X is constant or smallest)
    else:
        x = x1  # Use first X coordinate
        for y in range(min(y1, y2), max(y1, y2) + 1):
            for z in range(min(z1, z2), max(z1, z2) + 1):
                positions.append((x, y, z))
    
    return positions
