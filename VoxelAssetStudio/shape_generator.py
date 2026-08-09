# Steel Tide: Voxel Asset Studio
# shape_generator.py - Procedural voxel shape generation

import numpy as np
from material_library import MATERIALS

def generate_cube(size, material_id=3):
    """
    Generate a solid cube of voxels
    
    Args:
        size: Tuple (width, height, depth) in voxels
        material_id: Material to fill with (default: Concrete = 3)
    
    Returns:
        numpy array of voxel data
    """
    voxels = np.zeros(size, dtype=np.uint16)
    voxels[:, :, :] = material_id
    
    print(f"✅ Generated {size[0]}×{size[1]}×{size[2]} cube")
    print(f"   Material: {material_id}")
    print(f"   Total voxels: {np.prod(size):,}")
    
    return voxels


def generate_simple_humanoid(size=(4, 14, 4), core_material=3, shell_material=5):
    """Generate a carved humanoid silhouette within the existing character block size."""
    width, height, depth = size
    voxels = np.zeros(size, dtype=np.uint16)

    def fill_block(x_slice, y_slice, z_slice):
        voxels[x_slice[0]:x_slice[1], y_slice[0]:y_slice[1], z_slice[0]:z_slice[1]] = core_material

    # Legs + feet
    fill_block((1, 2), (0, 5), (1, 2))  # Left leg
    fill_block((2, 3), (0, 5), (1, 2))  # Right leg
    fill_block((1, 2), (0, 1), (0, 3))  # Left foot
    fill_block((2, 3), (0, 1), (0, 3))  # Right foot

    # Hips / waist connector
    fill_block((1, 3), (4, 6), (1, 3))

    # Torso core
    fill_block((1, 3), (5, 11), (1, 3))

    # Shoulders bar
    fill_block((0, 4), (9, 11), (1, 3))

    # Arms
    fill_block((0, 1), (6, 10), (1, 3))
    fill_block((3, 4), (6, 10), (1, 3))

    # Neck and head
    fill_block((1, 3), (10, 11), (1, 3))  # Neck
    fill_block((1, 3), (11, height), (1, 3))  # Head

    # Apply armored shell (steel) to exposed surface voxels
    offsets = ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1))
    for x in range(width):
        for y in range(height):
            for z in range(depth):
                if voxels[x, y, z] != core_material:
                    continue
                exposed = False
                for dx, dy, dz in offsets:
                    nx, ny, nz = x + dx, y + dy, z + dz
                    if nx < 0 or nx >= width or ny < 0 or ny >= height or nz < 0 or nz >= depth:
                        exposed = True
                        break
                    if voxels[nx, ny, nz] == 0:
                        exposed = True
                        break
                voxels[x, y, z] = shell_material if exposed else core_material

    voxel_count = np.count_nonzero(voxels)
    print("🧍 Generated simple humanoid block:")
    print(f"   Grid: {size} | Voxels: {voxel_count}")
    print(f"   Materials → Shell:{shell_material} Core:{core_material}")

    return voxels


def generate_simple_humanoid_variant(size=(4, 14, 4), core_material=3, shell_material=5):
    """Placeholder variant for upcoming experiments (duplicates base humanoid for now)."""
    return generate_simple_humanoid(size=size, core_material=core_material, shell_material=shell_material)

def generate_sphere(grid_size, radius, center, material_id=3):
    """
    Generate a solid sphere of voxels
    
    Args:
        grid_size: Tuple (width, height, depth) for the grid
        radius: Radius in voxels
        center: Tuple (x, y, z) center position
        material_id: Material to fill with
    
    Returns:
        numpy array of voxel data
    """
    voxels = np.zeros(grid_size, dtype=np.uint16)
    
    for x in range(grid_size[0]):
        for y in range(grid_size[1]):
            for z in range(grid_size[2]):
                # Distance from center
                dx = x - center[0]
                dy = y - center[1]
                dz = z - center[2]
                distance = np.sqrt(dx*dx + dy*dy + dz*dz)
                
                # If inside sphere, set material
                if distance <= radius:
                    voxels[x, y, z] = material_id
    
    voxel_count = np.count_nonzero(voxels)
    print(f"✅ Generated sphere (radius={radius})")
    print(f"   Center: {center}")
    print(f"   Material: {material_id}")
    print(f"   Voxels filled: {voxel_count:,}")
    
    return voxels

def generate_hollow_shell(grid_size, outer_radius, inner_radius, center, outer_material=5, inner_material=3):
    """
    Generate a TWO-LAYER shell (NOT truly hollow - has concrete core)
    
    Args:
        grid_size: Tuple (width, height, depth) for the grid
        outer_radius: Outer radius in voxels
        inner_radius: Inner radius in voxels
        center: Tuple (x, y, z) center position
        outer_material: Outer layer material (default: Steel = 5)
        inner_material: Inner layer material (default: Concrete = 3)
    
    Returns:
        numpy array of voxel data
    """
    voxels = np.zeros(grid_size, dtype=np.uint16)
    
    for x in range(grid_size[0]):
        for y in range(grid_size[1]):
            for z in range(grid_size[2]):
                dx = x - center[0]
                dy = y - center[1]
                dz = z - center[2]
                distance = np.sqrt(dx*dx + dy*dy + dz*dz)
                
                # Outer shell
                if distance <= outer_radius and distance > inner_radius:
                    voxels[x, y, z] = outer_material
                # Inner fill (NOT HOLLOW!)
                elif distance <= inner_radius:
                    voxels[x, y, z] = inner_material
    
    voxel_count = np.count_nonzero(voxels)
    print(f"✅ Generated two-layer shell (NOT hollow)")
    print(f"   Outer radius: {outer_radius}, Inner radius: {inner_radius}")
    print(f"   Outer material: {outer_material}, Inner material: {inner_material}")
    print(f"   Voxels filled: {voxel_count:,}")
    
    return voxels

def generate_truly_hollow_sphere(grid_size, outer_radius, shell_thickness, center, material=5):
    """
    Generate a TRULY HOLLOW sphere (empty interior, just shell)
    
    Args:
        grid_size: Tuple (width, height, depth) for the grid
        outer_radius: Outer radius in voxels
        shell_thickness: Thickness of shell in voxels
        center: Tuple (x, y, z) center position
        material: Shell material (default: Steel = 5)
    
    Returns:
        numpy array of voxel data (air in center!)
    """
    voxels = np.zeros(grid_size, dtype=np.uint16)
    inner_radius = outer_radius - shell_thickness
    
    for x in range(grid_size[0]):
        for y in range(grid_size[1]):
            for z in range(grid_size[2]):
                dx = x - center[0]
                dy = y - center[1]
                dz = z - center[2]
                distance = np.sqrt(dx*dx + dy*dy + dz*dz)
                
                # Only fill the shell (between inner and outer radius)
                if distance <= outer_radius and distance > inner_radius:
                    voxels[x, y, z] = material
                # Interior is AIR (material 0) - truly hollow!
    
    voxel_count = np.count_nonzero(voxels)
    print(f"✅ Generated TRULY HOLLOW sphere")
    print(f"   Outer radius: {outer_radius}, Shell thickness: {shell_thickness}")
    print(f"   Material: {material}")
    print(f"   Interior: EMPTY (air)")
    print(f"   Voxels filled: {voxel_count:,}")
    
    return voxels

def generate_truly_hollow_cube(size, shell_thickness, material=5):
    """
    Generate a TRULY HOLLOW cube (empty interior, just shell)
    
    Args:
        size: Tuple (width, height, depth) in voxels
        shell_thickness: Thickness of shell in voxels
        material: Shell material (default: Steel = 5)
    
    Returns:
        numpy array of voxel data (air in center!)
    """
    voxels = np.zeros(size, dtype=np.uint16)
    
    # Only fill the shell on all 6 faces
    # Top and bottom faces
    voxels[:, :, :shell_thickness] = material
    voxels[:, :, -shell_thickness:] = material
    
    # Front and back faces
    voxels[:, :shell_thickness, :] = material
    voxels[:, -shell_thickness:, :] = material
    
    # Left and right faces
    voxels[:shell_thickness, :, :] = material
    voxels[-shell_thickness:, :, :] = material
    
    # Interior is AIR (material 0) - truly hollow!
    
    voxel_count = np.count_nonzero(voxels)
    print(f"✅ Generated TRULY HOLLOW cube {size[0]}×{size[1]}×{size[2]}")
    print(f"   Shell thickness: {shell_thickness} voxels")
    print(f"   Material: {material}")
    print(f"   Interior: EMPTY (air)")
    print(f"   Voxels filled: {voxel_count:,}")
    
    return voxels

def generate_test_cube():
    """Generate a simple 8x8x8 test cube for quick testing"""
    return generate_cube((8, 8, 8), material_id=3)

def generate_armored_cube(size=(32, 32, 32), shell_thickness=2):
    """
    Generate a cube with Steel outer shell + Concrete core
    Matches HighDensity32 armor structure
    
    Args:
        size: Tuple (width, height, depth) in voxels
        shell_thickness: Thickness of steel shell in voxels
    
    Returns:
        numpy array with layered armor
    """
    voxels = np.zeros(size, dtype=np.uint16)
    
    # Fill entire cube with concrete (core)
    voxels[:, :, :] = 3  # Material 3 = Concrete
    
    # Add steel shell on all faces
    # Top and bottom faces
    voxels[:, :, :shell_thickness] = 5  # Steel
    voxels[:, :, -shell_thickness:] = 5
    
    # Front and back faces
    voxels[:, :shell_thickness, :] = 5
    voxels[:, -shell_thickness:, :] = 5
    
    # Left and right faces
    voxels[:shell_thickness, :, :] = 5
    voxels[-shell_thickness:, :, :] = 5
    
    print(f"✅ Generated armored cube {size[0]}×{size[1]}×{size[2]}")
    print(f"   Outer shell: Material 5 (Steel), {shell_thickness} voxels thick")
    print(f"   Inner core: Material 3 (Concrete)")
    print(f"   Total voxels: {np.prod(size):,}")
    
    return voxels

def generate_armored_sphere(grid_size=(32, 32, 32), radius=15, center=(16, 16, 16), shell_thickness=2):
    """
    Generate a sphere with Steel outer shell + Concrete core
    
    Args:
        grid_size: Tuple (width, height, depth) for the grid
        radius: Outer radius in voxels
        center: Tuple (x, y, z) center position
        shell_thickness: Thickness of steel shell in voxels
    
    Returns:
        numpy array with layered armor
    """
    voxels = np.zeros(grid_size, dtype=np.uint16)
    
    for x in range(grid_size[0]):
        for y in range(grid_size[1]):
            for z in range(grid_size[2]):
                dx = x - center[0]
                dy = y - center[1]
                dz = z - center[2]
                distance = np.sqrt(dx*dx + dy*dy + dz*dz)
                
                # Outer steel shell
                if distance <= radius and distance > radius - shell_thickness:
                    voxels[x, y, z] = 5  # Steel
                # Inner concrete core
                elif distance <= radius - shell_thickness:
                    voxels[x, y, z] = 3  # Concrete
    
    voxel_count = np.count_nonzero(voxels)
    print(f"✅ Generated armored sphere (radius={radius})")
    print(f"   Outer shell: Material 5 (Steel), {shell_thickness} voxels thick")
    print(f"   Inner core: Material 3 (Concrete)")
    print(f"   Center: {center}")
    print(f"   Voxels filled: {voxel_count:,}")
    
    return voxels

def generate_armored_cylinder(grid_size=(32, 32, 32), radius=12, height=28, center=(16, 16, 16), shell_thickness=2):
    """
    Generate a cylinder with Steel outer shell + Concrete core
    Useful for pillars, towers, etc.
    
    Args:
        grid_size: Tuple (width, height, depth) for the grid
        radius: Radius in voxels (XZ plane)
        height: Height in voxels (Y axis)
        center: Tuple (x, y, z) center position
        shell_thickness: Thickness of steel shell in voxels
    
    Returns:
        numpy array with layered armor
    """
    voxels = np.zeros(grid_size, dtype=np.uint16)
    
    half_height = height / 2
    
    for x in range(grid_size[0]):
        for y in range(grid_size[1]):
            for z in range(grid_size[2]):
                dx = x - center[0]
                dy = y - center[1]
                dz = z - center[2]
                
                # Distance from center axis (XZ plane)
                radial_distance = np.sqrt(dx*dx + dz*dz)
                
                # Check if within height
                if abs(dy) <= half_height:
                    # Outer steel shell
                    if radial_distance <= radius and radial_distance > radius - shell_thickness:
                        voxels[x, y, z] = 5  # Steel
                    # Inner concrete core
                    elif radial_distance <= radius - shell_thickness:
                        voxels[x, y, z] = 3  # Concrete
    
    voxel_count = np.count_nonzero(voxels)
    print(f"✅ Generated armored cylinder (radius={radius}, height={height})")
    print(f"   Outer shell: Material 5 (Steel), {shell_thickness} voxels thick")
    print(f"   Inner core: Material 3 (Concrete)")
    print(f"   Center: {center}")
    print(f"   Voxels filled: {voxel_count:,}")
    
    return voxels

def generate_material_sampler():
    """
    Generate a clean grid pattern of all materials for color sampling.
    Creates a 5x5 grid of 8x8x8 blocks, each with a different material.
    
    Returns:
        numpy array of voxel data (40x40x40 grid)
    """
    # Get all material IDs (excluding only Air). Includes Bone (12) and Joint (21)
    # so the grid shows the full 22-material list (21 sampleable blocks).
    material_ids = [mid for mid in sorted(MATERIALS.keys()) if mid != 0]
    
    # Grid layout: 5x5 blocks (max 25 materials)
    block_size = 8  # Each material block is 8x8x8 voxels
    grid_size = 5   # 5x5 grid
    padding = 2     # 2 voxels between blocks
    
    # Calculate total grid size
    total_size = grid_size * (block_size + padding) - padding
    
    voxels = np.zeros((total_size, total_size, total_size), dtype=np.uint16)
    
    # Place each material in a grid position
    for i, material_id in enumerate(material_ids):
        # Calculate grid position (row, column)
        row = i // grid_size
        col = i % grid_size
        
        # Calculate block start position
        start_x = col * (block_size + padding)
        start_y = row * (block_size + padding)
        start_z = 0  # All blocks at bottom layer
        
        # Fill block with material
        end_x = start_x + block_size
        end_y = start_y + block_size
        end_z = start_z + block_size
        
        voxels[start_x:end_x, start_y:end_y, start_z:end_z] = material_id
        
        material_name = MATERIALS[material_id]["name"]
        print(f"   [{i+1:2d}] Material {material_id:2d}: {material_name:25s} at ({start_x:2d}, {start_y:2d}, {start_z:2d})")
    
    voxel_count = np.count_nonzero(voxels)
    print(f"✅ Generated Material Sampler Grid")
    print(f"   Grid size: {total_size}×{total_size}×{total_size} voxels")
    print(f"   Block size: {block_size}×{block_size}×{block_size} voxels per material")
    print(f"   Materials sampled: {len(material_ids)}")
    print(f"   Total voxels: {voxel_count:,}")
    print(f"   Use this to sample colors for Unity material map!")
    
    return voxels
