# Steel Tide: Voxel Asset Studio
# procedural_buildings.py - Procedural building generation with auto-LOD

import numpy as np
from material_library import MATERIALS

# Material IDs - Futuristic Border Colony Materials
AIR = 0
PREFAB_COMPOSITE = 1      # Tan modular building panels
REGOLITH_CONCRETE = 2     # Dark brown local roads/foundations
CONCRETE = 3              # Gray standard structures
DURASTEEL = 5             # Blue-gray reinforced metal (renamed from STEEL)
BASALT = 8                # Dark gray volcanic rock (renamed from STONE)
WOOD = 9                  # Tan imported lumber
TRANSPARENT_ALUMINUM = 10 # Light blue sci-fi glass (renamed from GLASS)
DAMAGED_CONCRETE = 13
DAMAGED_STEEL = 14
ABLATIVE_PLATING = 16     # Charcoal heat-resistant armor
REACTIVE_ARMOR = 17       # Gunmetal explosive tiles
FOAM_CRETE = 18           # Light gray quick-deploy foam
PLASTEEL_PANELS = 20      # Medium gray plastic-metal hybrid

def generate_building_2story(
    width=32,
    height=64,
    depth=32,
    seed=None,
    window_spacing=4,
    wall_thickness=2,
    floor_thickness=2
):
    """
    Generate a 2-story building with windows, doors, and stairs.
    
    Args:
        width: Building width in voxels (default 32)
        height: Building height in voxels (default 64 for 2 stories)
        depth: Building depth in voxels (default 32)
        seed: Random seed for reproducible buildings
        window_spacing: Voxels between windows (default 4)
        wall_thickness: Wall thickness in voxels (default 2)
        floor_thickness: Floor slab thickness (default 2)
    
    Returns:
        numpy array of voxel data (width × height × depth)
    """
    if seed is not None:
        np.random.seed(seed)
    
    # Initialize empty grid (all air)
    grid = np.zeros((width, height, depth), dtype=np.uint16)
    
    # Calculate story height
    story_height = height // 2
    
    # Build foundation/floor - REGOLITH_CONCRETE (dark brown, local material)
    grid[:, 0:floor_thickness, :] = REGOLITH_CONCRETE
    
    # Build walls (outer shell) - PREFAB_COMPOSITE (tan modular panels)
    # Front and back walls
    grid[:wall_thickness, :, :] = PREFAB_COMPOSITE
    grid[-wall_thickness:, :, :] = PREFAB_COMPOSITE
    
    # Left and right walls
    grid[:, :, :wall_thickness] = PREFAB_COMPOSITE
    grid[:, :, -wall_thickness:] = PREFAB_COMPOSITE
    
    # Build second floor slab - REGOLITH_CONCRETE (structural)
    floor_y = story_height
    grid[:, floor_y:floor_y+floor_thickness, :] = REGOLITH_CONCRETE
    
    # Hollow out interior (leave walls intact!)
    interior_start = wall_thickness
    interior_end_x = width - wall_thickness
    interior_end_z = depth - wall_thickness
    
    # Hollow ground floor
    grid[interior_start:interior_end_x, floor_thickness:story_height, interior_start:interior_end_z] = AIR
    
    # Hollow second floor
    grid[interior_start:interior_end_x, story_height+floor_thickness:height-1, interior_start:interior_end_z] = AIR
    
    # Add windows to walls
    _add_windows(grid, width, height, depth, window_spacing, wall_thickness, story_height)
    
    # Add doorway (ground floor, front wall)
    _add_doorway(grid, width, depth, wall_thickness, story_height)
    
    # Add staircase
    _add_staircase(grid, width, depth, story_height, wall_thickness)
    
    return grid


def generate_building_lshape(
    width=48,
    height=32,
    depth=48,
    seed=None,
    window_spacing=6,
    wall_thickness=2
):
    """
    Generate an L-shaped single-story building (horizontal L, not vertical).
    
    Voxel data uses Y as vertical axis. Viewport will swap Y/Z for rendering.
    
    Args:
        width: Building width in voxels (X-axis)
        height: Building height in voxels (Y-axis) - single story
        depth: Building depth in voxels (Z-axis)
        seed: Random seed
        window_spacing: Voxels between windows
        wall_thickness: Wall thickness in voxels
    
    Returns:
        numpy array of voxel data
    """
    if seed is not None:
        np.random.seed(seed)
    
    # Initialize empty grid (all air)
    grid = np.zeros((width, height, depth), dtype=np.uint16)
    
    # L-shape layout (horizontal, viewed from above):
    # ████████████████
    # ████████████████
    # ████████
    # ████████
    
    half_width = width // 2
    half_depth = depth // 2
    
    # Build Rectangle 1 (top horizontal bar) - full width, front half depth
    grid[:, :, :half_depth] = PREFAB_COMPOSITE  # Walls (tan modular panels)
    
    # Build Rectangle 2 (vertical bar) - left half width, back half depth
    grid[:half_width, :, half_depth:] = PREFAB_COMPOSITE  # Same material for consistency
    
    # Add floor at Y=0 (will appear horizontal after viewport Y/Z swap)
    grid[:, 0, :half_depth] = REGOLITH_CONCRETE  # Floor rectangle 1 (dark brown foundation)
    grid[:half_width, 0, half_depth:] = REGOLITH_CONCRETE  # Floor rectangle 2
    
    # Hollow out interiors (leave walls)
    interior_margin = wall_thickness
    
    # Hollow rectangle 1 (horizontal bar)
    grid[interior_margin:-interior_margin, 1:height-1, interior_margin:half_depth-interior_margin] = AIR
    
    # Hollow rectangle 2 (vertical bar)
    grid[interior_margin:half_width-interior_margin, 1:height-1, half_depth+interior_margin:-interior_margin] = AIR
    
    # Add windows and doors
    _add_windows_lshape(grid, width, height, depth, window_spacing, wall_thickness, half_width, half_depth)
    _add_doorway(grid, width, depth, wall_thickness, height)
    
    return grid


def generate_ground_plane(
    width=128,
    height=2,
    depth=128,
    material=REGOLITH_CONCRETE
):
    """
    Generate a flat ground plane for streets/terrain.
    
    Default material: REGOLITH_CONCRETE (dark brown, local roads)
    Alternative materials:
    - REGOLITH (6): Brown alien soil
    - XENOFLORA (7): Teal alien plants
    - CONCRETE (3): Gray concrete pavement
    - BASALT (8): Dark gray volcanic rock
    
    Args:
        width: Width in voxels (default 128 = 16m)
        height: Thickness in voxels (default 2 = 0.25m)
        depth: Depth in voxels (default 128 = 16m)
        material: Material ID for the plane (default REGOLITH_CONCRETE)
    
    Returns:
        3D numpy array (uint16) with voxel data (Y is up)
    """
    # Create solid plane
    grid = np.full((width, height, depth), material, dtype=np.uint16)
    
    return grid


def generate_building_simple_house(
    width=32,
    height=24,
    depth=24,
    seed=None,
    window_spacing=6,
    wall_thickness=2
):
    """
    Generate a simple rectangular house (single story).
    
    Voxel data uses Y as vertical axis. Viewport will swap Y/Z for rendering.
    
    Args:
        width: Building width in voxels
        height: Building height in voxels (single story, ~16)
        depth: Building depth in voxels
        seed: Random seed
        window_spacing: Voxels between windows
        wall_thickness: Wall thickness in voxels
    
    Returns:
        numpy array of voxel data
    """
    if seed is not None:
        np.random.seed(seed)
    
    # Initialize empty grid (all air)
    grid = np.zeros((width, height, depth), dtype=np.uint16)
    
    # Build walls (PREFAB_COMPOSITE for small residential building - tan modular)
    grid[:, :, :] = PREFAB_COMPOSITE
    
    # Add floor at Y=0 (will appear horizontal after viewport Y/Z swap)
    grid[:, 0, :] = REGOLITH_CONCRETE  # Dark brown foundation
    
    # Hollow out interior (leave walls)
    interior_start = wall_thickness
    interior_end_x = width - wall_thickness
    interior_end_y = height - 1
    interior_end_z = depth - wall_thickness
    
    grid[interior_start:interior_end_x, 1:interior_end_y, interior_start:interior_end_z] = AIR
    
    # Add windows and doors
    _add_windows_simple(grid, width, height, depth, window_spacing, wall_thickness)
    _add_doorway(grid, width, depth, wall_thickness, height)
    
    return grid


def create_lod_version(detailed_grid, lod_factor=8):
    """
    Create a low-resolution LOD version of a detailed building.
    
    Downsamples the grid by lod_factor in each dimension.
    Uses the most common material in each region.
    
    Args:
        detailed_grid: High-res voxel grid (e.g., 32×64×32)
        lod_factor: Downsampling factor (default 8)
    
    Returns:
        Low-res voxel grid (e.g., 4×8×4)
    """
    detailed_shape = detailed_grid.shape
    
    lod_dims = (
        detailed_shape[0] // lod_factor,
        detailed_shape[1] // lod_factor,
        detailed_shape[2] // lod_factor
    )
    
    # Ensure at least 1 voxel in each dimension
    lod_dims = tuple(max(1, d) for d in lod_dims)
    
    lod_grid = np.zeros(lod_dims, dtype=np.uint16)
    
    # Sample each region
    for x in range(lod_dims[0]):
        for y in range(lod_dims[1]):
            for z in range(lod_dims[2]):
                # Get region bounds
                x_start = x * lod_factor
                x_end = min((x + 1) * lod_factor, detailed_shape[0])
                y_start = y * lod_factor
                y_end = min((y + 1) * lod_factor, detailed_shape[1])
                z_start = z * lod_factor
                z_end = min((z + 1) * lod_factor, detailed_shape[2])
                
                # Extract region
                region = detailed_grid[x_start:x_end, y_start:y_end, z_start:z_end]
                
                # Find most common non-air material
                materials, counts = np.unique(region, return_counts=True)
                
                # Filter out air
                non_air_mask = materials != AIR
                if np.any(non_air_mask):
                    non_air_materials = materials[non_air_mask]
                    non_air_counts = counts[non_air_mask]
                    most_common = non_air_materials[np.argmax(non_air_counts)]
                    lod_grid[x, y, z] = most_common
                else:
                    lod_grid[x, y, z] = AIR
    
    return lod_grid


# ============================================================================
# Helper Functions
# ============================================================================

def _add_windows(grid, width, height, depth, spacing, wall_thickness, story_height):
    """Add windows to building walls"""
    window_size = 2
    window_y_offset = 4  # Height from floor
    
    # Front wall windows (ground floor)
    for x in range(wall_thickness + spacing, width - wall_thickness, spacing + window_size):
        for y in range(window_y_offset, window_y_offset + window_size):
            for z in range(wall_thickness):
                if x + window_size < width - wall_thickness:
                    grid[x:x+window_size, y, z] = AIR
    
    # Front wall windows (second floor)
    for x in range(wall_thickness + spacing, width - wall_thickness, spacing + window_size):
        for y in range(story_height + window_y_offset, story_height + window_y_offset + window_size):
            for z in range(wall_thickness):
                if x + window_size < width - wall_thickness and y < height:
                    grid[x:x+window_size, y, z] = AIR
    
    # Back wall windows (mirror front)
    for x in range(wall_thickness + spacing, width - wall_thickness, spacing + window_size):
        for y in range(window_y_offset, window_y_offset + window_size):
            for z in range(depth - wall_thickness, depth):
                if x + window_size < width - wall_thickness:
                    grid[x:x+window_size, y, z] = AIR
    
    # Side walls (simplified - fewer windows)
    for z in range(wall_thickness + spacing * 2, depth - wall_thickness, spacing * 2):
        for y in range(window_y_offset, window_y_offset + window_size):
            # Left wall
            for x in range(wall_thickness):
                if z + window_size < depth - wall_thickness:
                    grid[x, y, z:z+window_size] = AIR
            # Right wall
            for x in range(width - wall_thickness, width):
                if z + window_size < depth - wall_thickness:
                    grid[x, y, z:z+window_size] = AIR


def _add_windows_lshape(grid, width, height, depth, spacing, wall_thickness, half_width, half_depth):
    """Add windows to L-shaped building"""
    window_size = 3
    window_y_offset = 4
    
    # Front wall (horizontal bar)
    for x in range(wall_thickness + spacing, width - wall_thickness, spacing + window_size + 2):
        for y in range(window_y_offset, min(window_y_offset + window_size, height)):
            for z in range(wall_thickness):
                if x + window_size < width - wall_thickness:
                    grid[x:x+window_size, y, z] = AIR
    
    # Left wall (vertical bar)
    for z in range(half_depth + spacing, depth - wall_thickness, spacing + window_size + 2):
        for y in range(window_y_offset, min(window_y_offset + window_size, height)):
            for x in range(wall_thickness):
                if z + window_size < depth - wall_thickness:
                    grid[x, y, z:z+window_size] = AIR


def _add_windows_simple(grid, width, height, depth, spacing, wall_thickness):
    """Add windows to simple rectangular building"""
    window_size = 3
    window_y_offset = 4
    
    # Front wall (Z=0)
    for x in range(wall_thickness + spacing, width - wall_thickness, spacing + window_size + 2):
        for y in range(window_y_offset, min(window_y_offset + window_size, height - 2)):
            if x + window_size < width - wall_thickness:
                grid[x:x+window_size, y, :wall_thickness] = AIR
    
    # Back wall (Z=max)
    for x in range(wall_thickness + spacing, width - wall_thickness, spacing + window_size + 2):
        for y in range(window_y_offset, min(window_y_offset + window_size, height - 2)):
            if x + window_size < width - wall_thickness:
                grid[x:x+window_size, y, -wall_thickness:] = AIR
    
    # Left wall (X=0)
    for z in range(wall_thickness + spacing, depth - wall_thickness, spacing + window_size + 2):
        for y in range(window_y_offset, min(window_y_offset + window_size, height - 2)):
            if z + window_size < depth - wall_thickness:
                grid[:wall_thickness, y, z:z+window_size] = AIR
    
    # Right wall (X=max)
    for z in range(wall_thickness + spacing, depth - wall_thickness, spacing + window_size + 2):
        for y in range(window_y_offset, min(window_y_offset + window_size, height - 2)):
            if z + window_size < depth - wall_thickness:
                grid[-wall_thickness:, y, z:z+window_size] = AIR


def _add_doorway(grid, width, depth, wall_thickness, story_height):
    """Add doorway to front wall"""
    door_width = 4
    door_height = 8
    door_x = width // 2 - door_width // 2
    
    # Clear doorway
    for x in range(door_x, door_x + door_width):
        for y in range(1, min(door_height, story_height)):
            for z in range(wall_thickness):
                grid[x, y, z] = AIR


def _add_staircase(grid, width, depth, story_height, wall_thickness):
    """Add simple diagonal staircase"""
    stair_width = 4
    stair_x = width - wall_thickness - stair_width - 2
    stair_z_start = depth // 2 - 8
    stair_z_end = depth // 2 + 8
    
    num_steps = story_height - 2
    step_depth = (stair_z_end - stair_z_start) / num_steps
    
    for step in range(num_steps):
        step_y = 2 + step
        step_z_start = int(stair_z_start + step * step_depth)
        step_z_end = int(stair_z_start + (step + 1) * step_depth)
        
        if step_y < grid.shape[1] and step_z_end < grid.shape[2]:
            grid[stair_x:stair_x+stair_width, step_y, step_z_start:step_z_end] = CONCRETE
