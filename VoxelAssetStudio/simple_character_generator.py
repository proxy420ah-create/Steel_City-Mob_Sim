"""
Simple Character Generator - Steel Tide
Builds characters using the SAME coordinate system as working cubes/planes.
Start simple, add complexity iteratively.
"""

import numpy as np
from stasset_io import save_stasset

# Material IDs (must match Unity and material_library.py)
AIR = 0
PREFAB_COMPOSITE = 1
REGOLITH_CONCRETE = 2
CONCRETE = 3
FLESH = 4
DURASTEEL = 5
REGOLITH = 6
XENOFLORA = 7
BASALT = 8
WOOD = 9
TRANSPARENT_ALUMINUM = 10
UNIFORM = 11
RESERVED = 12
DAMAGED_CONCRETE = 13
DAMAGED_STEEL = 14
DAMAGED_ARMOR = 15
ABLATIVE_PLATING = 16
REACTIVE_ARMOR = 17
FOAM_CRETE = 18
NANOMESH_FABRIC = 19
PLASTEEL_PANELS = 20

def generate_simple_torso(width=6, height=12, depth=4, material=UNIFORM):
    """
    Generate a simple rectangular torso - same as a cube but with character proportions.
    This uses the EXACT same approach as the working cube generator.
    
    Args:
        width: Width in voxels (X axis) - left/right
        height: Height in voxels (Y axis) - up/down
        depth: Depth in voxels (Z axis) - front/back
        material: Material ID to use
    
    Returns:
        3D numpy array (width, height, depth) with voxel data
    """
    # Create grid - SAME format as working cube generator
    grid = np.zeros((width, height, depth), dtype=np.uint16)
    
    # Fill entire volume with material (solid box)
    grid[:, :, :] = material
    
    return grid


def generate_character_v1(torso_material=UNIFORM, head_material=FLESH):
    """
    Version 1: Simple character with torso and head.
    Build it piece by piece to verify each part works.
    
    Character dimensions:
    - Total height: 20 voxels (2.5m at 0.125m/voxel)
    - Width: 6 voxels
    - Depth: 4 voxels
    """
    width = 6
    height = 20
    depth = 4
    
    # Create empty grid
    grid = np.zeros((width, height, depth), dtype=np.uint16)
    
    # TORSO (bottom 12 voxels)
    torso_height = 12
    grid[:, 0:torso_height, :] = torso_material
    
    # HEAD (top 8 voxels, centered)
    head_height = 8
    head_width = 4
    head_depth = 3
    
    # Center the head
    head_x_offset = (width - head_width) // 2
    head_z_offset = (depth - head_depth) // 2
    
    grid[head_x_offset:head_x_offset+head_width, 
         torso_height:torso_height+head_height, 
         head_z_offset:head_z_offset+head_depth] = head_material
    
    return grid


def generate_character_v2(
    torso_material=UNIFORM,
    head_material=FLESH,
    limb_material=PLASTEEL_PANELS
):
    """
    Version 2: Character with torso, head, and simple arm/leg stubs.
    """
    width = 8
    height = 24
    depth = 4
    
    # Create empty grid
    grid = np.zeros((width, height, depth), dtype=np.uint16)
    
    # LEGS (bottom 8 voxels, two columns)
    leg_height = 8
    leg_width = 2
    leg_spacing = 1
    
    # Left leg
    left_leg_x = 1
    grid[left_leg_x:left_leg_x+leg_width, 0:leg_height, :] = limb_material
    
    # Right leg
    right_leg_x = width - 1 - leg_width
    grid[right_leg_x:right_leg_x+leg_width, 0:leg_height, :] = limb_material
    
    # TORSO (middle 12 voxels)
    torso_height = 12
    torso_y_start = leg_height
    grid[:, torso_y_start:torso_y_start+torso_height, :] = torso_material
    
    # ARMS (attached to torso sides)
    arm_height = 8
    arm_width = 1
    arm_y_start = torso_y_start + 2
    
    # Left arm
    grid[0:arm_width, arm_y_start:arm_y_start+arm_height, :] = limb_material
    
    # Right arm
    grid[width-arm_width:width, arm_y_start:arm_y_start+arm_height, :] = limb_material
    
    # HEAD (top 4 voxels, centered)
    head_height = 4
    head_width = 4
    head_depth = 3
    head_y_start = torso_y_start + torso_height
    
    head_x_offset = (width - head_width) // 2
    head_z_offset = (depth - head_depth) // 2
    
    grid[head_x_offset:head_x_offset+head_width,
         head_y_start:head_y_start+head_height,
         head_z_offset:head_z_offset+head_depth] = head_material
    
    return grid


def generate_solid_test(width=6, height=20, depth=4):
    """
    Test: Create a SOLID block with two materials (no air).
    This will tell us if the issue is with air voxels or multi-material indexing.
    """
    grid = np.zeros((width, height, depth), dtype=np.uint16)
    
    # Bottom half = UNIFORM (green)
    grid[:, 0:10, :] = UNIFORM
    
    # Top half = FLESH (pink)
    grid[:, 10:20, :] = FLESH
    
    return grid


def generate_hollow_box_test(width=8, height=12, depth=8):
    """
    Test: Create a hollow box (like buildings have).
    Outer shell = material, inner core = air.
    """
    grid = np.zeros((width, height, depth), dtype=np.uint16)
    
    # Fill entire volume
    grid[:, :, :] = UNIFORM
    
    # Hollow out the center (leave 1-voxel thick walls)
    grid[1:-1, 1:-1, 1:-1] = AIR
    
    return grid


if __name__ == "__main__":
    import os
    
    # Output directory
    output_dir = "../My project/Assets/StreamingAssets"
    os.makedirs(output_dir, exist_ok=True)
    
    print("🎨 Simple Character Generator - Clean Test Suite")
    print("=" * 60)
    
    # Test 1: Solid single-material (baseline)
    print("\n1️⃣ Solid single-material box...")
    torso = generate_simple_torso(width=6, height=12, depth=4, material=UNIFORM)
    save_stasset(os.path.join(output_dir, "Test_1_Solid_SingleMaterial.stasset"), torso)
    
    # Test 2: Hollow box (air interior)
    print("\n2️⃣ Hollow box (sparse volume)...")
    hollow_test = generate_hollow_box_test()
    save_stasset(os.path.join(output_dir, "Test_2_Hollow_Box.stasset"), hollow_test)
    
    # Test 3: Solid multi-material (no air gaps)
    print("\n3️⃣ Solid multi-material...")
    solid_multi = generate_solid_test()
    save_stasset(os.path.join(output_dir, "Test_3_Solid_MultiMaterial.stasset"), solid_multi)
    
    # Test 4: Character V1 (multi-material WITH air gaps)
    print("\n4️⃣ Character V1 (multi-material + air gaps)...")
    char_v1 = generate_character_v1()
    save_stasset(os.path.join(output_dir, "Test_4_Character_V1.stasset"), char_v1)
    
    # Test 5: Character V2 (full body)
    print("\n5️⃣ Character V2 (full body with limbs)...")
    char_v2 = generate_character_v2()
    save_stasset(os.path.join(output_dir, "Test_5_Character_V2.stasset"), char_v2)
    
    print("\n" + "=" * 60)
    print("✅ Clean test suite generated!")
    print("\n📋 Test Order:")
    print("   1. Test_1_Solid_SingleMaterial.stasset - Baseline")
    print("   2. Test_2_Hollow_Box.stasset - Sparse volume")
    print("   3. Test_3_Solid_MultiMaterial.stasset - Material transitions")
    print("   4. Test_4_Character_V1.stasset - Complex (multi-mat + air)")
    print("   5. Test_5_Character_V2.stasset - Full character")
    print("\n🎯 If Test 1-3 work but Test 4 fails:")
    print("   → Issue is with multi-material + air gap combination")
    print("   → Check character generator voxel placement logic")
