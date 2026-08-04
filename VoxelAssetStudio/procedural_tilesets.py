"""
Procedural Tileset Generation for Steel Tide
Generates seamless urban tiles for city building.
"""

import numpy as np
from material_library import MATERIALS

# Material IDs
AIR = 0
CONCRETE = 3
STONE = 8
GRASS = 7
DIRT = 6

# Standard tile size (16m × 0.25m × 16m)
TILE_WIDTH = 128   # 16m
TILE_HEIGHT = 2    # 0.25m
TILE_DEPTH = 128   # 16m

# Road dimensions
ROAD_WIDTH = 64    # 8m road (half the tile)
SIDEWALK_WIDTH = 16  # 2m sidewalk
GRASS_EDGE = 48    # 6m grass on each side


def generate_grass_plane(width=TILE_WIDTH, height=TILE_HEIGHT, depth=TILE_DEPTH, seed=None):
    """
    Generate a full grass plane tile.
    Perfect for parks, yards, open spaces.
    
    Args:
        width: Tile width in voxels (default 128 = 16m)
        height: Tile height in voxels (default 2 = 0.25m)
        depth: Tile depth in voxels (default 128 = 16m)
        seed: Random seed for variation
    
    Returns:
        3D numpy array (uint16) with voxel data
    """
    if seed is not None:
        np.random.seed(seed)
    
    # Base layer: dirt
    grid = np.full((width, height, depth), DIRT, dtype=np.uint16)
    
    # Top layer: grass with slight variation
    for x in range(width):
        for z in range(depth):
            # Random grass height variation (1-2 voxels)
            grass_height = 1 if np.random.random() < 0.7 else 2
            grid[x, height-grass_height:height, z] = GRASS
    
    return grid


def generate_road_straight(width=TILE_WIDTH, height=TILE_HEIGHT, depth=TILE_DEPTH, seed=None):
    """
    Generate a straight road tile (runs along Z axis).
    
    Layout (top view):
    [GRASS][SIDEWALK][ROAD][SIDEWALK][GRASS]
    
    Args:
        width: Tile width in voxels (default 128)
        height: Tile height in voxels (default 2)
        depth: Tile depth in voxels (default 128)
        seed: Random seed for variation
    
    Returns:
        3D numpy array (uint16) with voxel data
    """
    if seed is not None:
        np.random.seed(seed)
    
    # Start with grass base
    grid = generate_grass_plane(width, height, depth, seed)
    
    # Calculate road position (centered)
    road_start = (width - ROAD_WIDTH) // 2
    road_end = road_start + ROAD_WIDTH
    
    # Sidewalk positions
    sidewalk_left_start = road_start - SIDEWALK_WIDTH
    sidewalk_left_end = road_start
    sidewalk_right_start = road_end
    sidewalk_right_end = road_end + SIDEWALK_WIDTH
    
    # Left sidewalk (concrete)
    grid[sidewalk_left_start:sidewalk_left_end, :, :] = CONCRETE
    
    # Road (stone/asphalt)
    grid[road_start:road_end, :, :] = STONE
    
    # Right sidewalk (concrete)
    grid[sidewalk_right_start:sidewalk_right_end, :, :] = CONCRETE
    
    # Add random road wear/cracks
    for _ in range(np.random.randint(5, 15)):
        crack_x = np.random.randint(road_start, road_end)
        crack_z = np.random.randint(0, depth)
        crack_length = np.random.randint(2, 8)
        
        for i in range(crack_length):
            if crack_z + i < depth:
                grid[crack_x, height-1, crack_z+i] = DIRT  # Crack
    
    return grid


def generate_road_turn_left(width=TILE_WIDTH, height=TILE_HEIGHT, depth=TILE_DEPTH, seed=None):
    """
    Generate a left-turning road tile (90° curve).
    Road enters from bottom (Z=0) and exits left (X=0).
    
    Args:
        width: Tile width in voxels
        height: Tile height in voxels
        depth: Tile depth in voxels
        seed: Random seed for variation
    
    Returns:
        3D numpy array (uint16) with voxel data
    """
    if seed is not None:
        np.random.seed(seed)
    
    # Start with grass base
    grid = generate_grass_plane(width, height, depth, seed)
    
    # Draw curved road using circle approximation
    center_x = width // 2
    center_z = depth // 2
    radius = ROAD_WIDTH
    
    # Draw road curve
    for x in range(width):
        for z in range(depth):
            # Distance from corner (0, depth)
            dist_x = x
            dist_z = depth - z
            dist = np.sqrt(dist_x**2 + dist_z**2)
            
            # Check if in road area (curved band)
            inner_radius = radius - ROAD_WIDTH // 2
            outer_radius = radius + ROAD_WIDTH // 2
            
            if inner_radius < dist < outer_radius:
                grid[x, :, z] = STONE  # Road
            elif (inner_radius - SIDEWALK_WIDTH < dist < inner_radius or
                  outer_radius < dist < outer_radius + SIDEWALK_WIDTH):
                grid[x, :, z] = CONCRETE  # Sidewalk
    
    return grid


def generate_road_turn_right(width=TILE_WIDTH, height=TILE_HEIGHT, depth=TILE_DEPTH, seed=None):
    """
    Generate a right-turning road tile (90° curve).
    Road enters from bottom (Z=0) and exits right (X=width).
    """
    if seed is not None:
        np.random.seed(seed)
    
    # Generate left turn, then mirror it
    left_turn = generate_road_turn_left(width, height, depth, seed)
    
    # Mirror along X axis
    grid = np.flip(left_turn, axis=0).copy()
    
    return grid


def generate_road_intersection(width=TILE_WIDTH, height=TILE_HEIGHT, depth=TILE_DEPTH, seed=None):
    """
    Generate a 4-way intersection tile.
    Roads connect to all four edges.
    
    Args:
        width: Tile width in voxels
        height: Tile height in voxels
        depth: Tile depth in voxels
        seed: Random seed for variation
    
    Returns:
        3D numpy array (uint16) with voxel data
    """
    if seed is not None:
        np.random.seed(seed)
    
    # Start with grass base
    grid = generate_grass_plane(width, height, depth, seed)
    
    # Calculate intersection center
    center_x = width // 2
    center_z = depth // 2
    half_road = ROAD_WIDTH // 2
    
    # Horizontal road (runs along X axis)
    road_z_start = center_z - half_road
    road_z_end = center_z + half_road
    grid[:, :, road_z_start:road_z_end] = STONE
    
    # Vertical road (runs along Z axis)
    road_x_start = center_x - half_road
    road_x_end = center_x + half_road
    grid[road_x_start:road_x_end, :, :] = STONE
    
    # Sidewalks around intersection
    # Top sidewalk
    grid[:, :, road_z_start-SIDEWALK_WIDTH:road_z_start] = CONCRETE
    # Bottom sidewalk
    grid[:, :, road_z_end:road_z_end+SIDEWALK_WIDTH] = CONCRETE
    # Left sidewalk
    grid[road_x_start-SIDEWALK_WIDTH:road_x_start, :, :] = CONCRETE
    # Right sidewalk
    grid[road_x_end:road_x_end+SIDEWALK_WIDTH, :, :] = CONCRETE
    
    # Add crosswalk stripes (white concrete)
    stripe_width = 2
    for i in range(4):
        stripe_pos = road_z_start + i * (ROAD_WIDTH // 4)
        # Horizontal crosswalks
        grid[0:road_x_start-SIDEWALK_WIDTH, :, stripe_pos:stripe_pos+stripe_width] = CONCRETE
        grid[road_x_end+SIDEWALK_WIDTH:width, :, stripe_pos:stripe_pos+stripe_width] = CONCRETE
    
    return grid


def generate_road_t_junction(width=TILE_WIDTH, height=TILE_HEIGHT, depth=TILE_DEPTH, seed=None):
    """
    Generate a T-junction tile.
    Road enters from bottom, exits left and right (no top exit).
    
    Args:
        width: Tile width in voxels
        height: Tile height in voxels
        depth: Tile depth in voxels
        seed: Random seed for variation
    
    Returns:
        3D numpy array (uint16) with voxel data
    """
    if seed is not None:
        np.random.seed(seed)
    
    # Start with grass base
    grid = generate_grass_plane(width, height, depth, seed)
    
    center_x = width // 2
    center_z = depth // 2
    half_road = ROAD_WIDTH // 2
    
    # Horizontal road (full width)
    road_z_start = center_z - half_road
    road_z_end = center_z + half_road
    grid[:, :, road_z_start:road_z_end] = STONE
    
    # Vertical road (only bottom half)
    road_x_start = center_x - half_road
    road_x_end = center_x + half_road
    grid[road_x_start:road_x_end, :, road_z_end:depth] = STONE
    
    # Sidewalks
    grid[:, :, road_z_start-SIDEWALK_WIDTH:road_z_start] = CONCRETE
    grid[:, :, road_z_end:road_z_end+SIDEWALK_WIDTH] = CONCRETE
    grid[road_x_start-SIDEWALK_WIDTH:road_x_start, :, road_z_end:depth] = CONCRETE
    grid[road_x_end:road_x_end+SIDEWALK_WIDTH, :, road_z_end:depth] = CONCRETE
    
    return grid


def generate_road_dead_end(width=TILE_WIDTH, height=TILE_HEIGHT, depth=TILE_DEPTH, seed=None):
    """
    Generate a dead-end road tile.
    Road enters from bottom, ends in a cul-de-sac.
    
    Args:
        width: Tile width in voxels
        height: Tile height in voxels
        depth: Tile depth in voxels
        seed: Random seed for variation
    
    Returns:
        3D numpy array (uint16) with voxel data
    """
    if seed is not None:
        np.random.seed(seed)
    
    # Start with grass base
    grid = generate_grass_plane(width, height, depth, seed)
    
    center_x = width // 2
    half_road = ROAD_WIDTH // 2
    
    # Straight road from bottom to 3/4 up
    road_x_start = center_x - half_road
    road_x_end = center_x + half_road
    road_length = int(depth * 0.75)
    grid[road_x_start:road_x_end, :, 0:road_length] = STONE
    
    # Circular cul-de-sac at end
    circle_center_z = road_length
    circle_radius = ROAD_WIDTH
    
    for x in range(width):
        for z in range(depth):
            dist = np.sqrt((x - center_x)**2 + (z - circle_center_z)**2)
            if dist < circle_radius:
                grid[x, :, z] = STONE
            elif dist < circle_radius + SIDEWALK_WIDTH:
                grid[x, :, z] = CONCRETE
    
    # Sidewalks along straight section
    grid[road_x_start-SIDEWALK_WIDTH:road_x_start, :, 0:road_length] = CONCRETE
    grid[road_x_end:road_x_end+SIDEWALK_WIDTH, :, 0:road_length] = CONCRETE
    
    return grid


# Tileset registry for UI
URBAN_RESIDENTIAL_TILESET = {
    "Grass Plane": generate_grass_plane,
    "Road: Straight": generate_road_straight,
    "Road: Turn Left": generate_road_turn_left,
    "Road: Turn Right": generate_road_turn_right,
    "Road: 4-Way Intersection": generate_road_intersection,
    "Road: T-Junction": generate_road_t_junction,
    "Road: Dead End": generate_road_dead_end,
}
