# Steel City: Mob Sim — Procedural 1920s Building Generators
# procedural_mob_buildings.py
#
# Each generator produces a 3D numpy uint16 array using MOB material IDs.
# Voxel layout: (width, height, depth) with Y as vertical axis.
# All buildings are designed to sit on a 32x32 block tile.

import numpy as np
from mob_materials import *

# Default block tile size
BLOCK_W = 32
BLOCK_D = 32
WALL_T = 2      # wall thickness
FLOOR_T = 2     # floor slab thickness


def _hollow_interior(grid, w, h, d, wt, floor_t):
    """Hollow out interior leaving walls intact."""
    grid[wt:w-wt, floor_t:h-1, wt:d-wt] = AIR


def _add_windows_all_sides(grid, w, h, d, wt, y_start, y_end, spacing=6, win_size=2, material=WINDOW_GLASS):
    """Add windows on all four walls at the given Y range."""
    for x in range(wt + spacing, w - wt - win_size, spacing + win_size):
        for y in range(y_start, min(y_end, h - 1)):
            # Front wall (z=0)
            grid[x:x+win_size, y, :wt] = material
            # Back wall (z=max)
            grid[x:x+win_size, y, d-wt:d] = material
    for z in range(wt + spacing, d - wt - win_size, spacing + win_size):
        for y in range(y_start, min(y_end, h - 1)):
            # Left wall (x=0)
            grid[:wt, y, z:z+win_size] = material
            # Right wall (x=max)
            grid[w-wt:w, y, z:z+win_size] = material


def _add_doorway(grid, w, d, wt, door_h=8, door_w=6, material=PAINTED_BROWN):
    """Add a centered doorway on the front wall (z=0)."""
    dx = w // 2 - door_w // 2
    grid[dx:dx+door_w, 1:door_h+1, :wt] = AIR
    # Door frame
    grid[dx-1:dx, 1:door_h+1, :wt] = DARK_WOOD
    grid[dx+door_w:dx+door_w+1, 1:door_h+1, :wt] = DARK_WOOD
    grid[dx:dx+door_w, door_h:door_h+1, :wt] = DARK_WOOD
    # Door itself (closed)
    grid[dx:dx+door_w, 1:door_h, 0] = material


def _add_flat_roof(grid, w, h, d, wt, material=TAR):
    """Add a flat tar roof with parapet."""
    grid[:, h-1, :] = material
    # Parapet edge
    grid[:wt, h-2:, :] = RED_BRICK
    grid[w-wt:, h-2:, :] = RED_BRICK
    grid[:, h-2:, :wt] = RED_BRICK
    grid[:, h-2:, d-wt:] = RED_BRICK


def _add_storefront(grid, w, d, wt, awning_mat=PAINTED_RED, door_h=8, door_w=6):
    """Add a storefront: large glass windows + awning on front wall.
    
    door_h: door height in voxels (default 8 = 0.8m, fits 0.48m NPC comfortably)
    door_w: door width in voxels (default 6 = 0.6m)
    """
    glass_top = door_h + 1
    # Large storefront windows (ground floor, flanking door)
    grid[wt:w-wt, 2:glass_top, :wt] = STOREFRONT_GLASS
    # Window frame top/bottom
    grid[wt:w-wt, 1:2, :wt] = DARK_WOOD
    grid[wt:w-wt, glass_top:glass_top+1, :wt] = DARK_WOOD
    # Awning above storefront
    grid[wt:w-wt, glass_top+1:glass_top+3, :wt] = awning_mat
    # Door
    dx = w // 2 - door_w // 2
    grid[dx:dx+door_w, 1:door_h+1, :wt] = PAINTED_BROWN
    grid[dx:dx+door_w, door_h:door_h+1, :wt] = DARK_WOOD


def _add_chimney(grid, w, h, d, cx=None, cz=None):
    """Add a chimney on the roof."""
    if cx is None: cx = w - 6
    if cz is None: cz = d - 6
    if cx + 2 < w and cz + 2 < d:
        grid[cx:cx+2, h-1:h+3, cz:cz+2] = RED_BRICK


def _add_basement_foundation(grid, w, d, wt):
    """Add a stone foundation layer at the bottom."""
    grid[:, 0:FLOOR_T, :] = STONE
    grid[:wt, 0:FLOOR_T+1, :] = STONE
    grid[w-wt:, 0:FLOOR_T+1, :] = STONE
    grid[:, 0:FLOOR_T+1, :wt] = STONE
    grid[:, 0:FLOOR_T+1, d-wt:] = STONE


# ============================================================================
# Business Generators
# ============================================================================

def generate_butcher_shop(w=BLOCK_W, h=20, d=BLOCK_D, seed=None):
    """Red brick storefront with awning, ground floor shop + small upper office."""
    if seed is not None: np.random.seed(seed)
    grid = np.zeros((w, h, d), dtype=np.uint16)
    # Walls
    grid[:, :, :] = RED_BRICK
    _add_basement_foundation(grid, w, d, WALL_T)
    _hollow_interior(grid, w, h, d, WALL_T, FLOOR_T)
    # Storefront on front wall
    _add_storefront(grid, w, d, WALL_T, awning_mat=PAINTED_RED)
    # Upper floor windows
    _add_windows_all_sides(grid, w, h, d, WALL_T, 10, 16, spacing=8, win_size=2)
    # Interior floor slab
    grid[WALL_T:w-WALL_T, 9:10, WALL_T:d-WALL_T] = DARK_WOOD
    # Roof
    _add_flat_roof(grid, w, h, d, WALL_T, TAR)
    _add_chimney(grid, w, h, d)
    # Sign
    grid[4:8, 8:9, :WALL_T] = DARK_IRON
    grid[5:7, 8:9, :WALL_T] = GOLD_BRASS
    return grid


def generate_bakery(w=BLOCK_W, h=18, d=BLOCK_D, seed=None):
    """Cream stucco storefront with green awning protruding from front."""
    if seed is not None: np.random.seed(seed)
    grid = np.zeros((w, h, d), dtype=np.uint16)
    grid[:, :, :] = STUCCO
    _add_basement_foundation(grid, w, d, WALL_T)
    _hollow_interior(grid, w, h, d, WALL_T, FLOOR_T)
    # Storefront with green awning
    _add_storefront(grid, w, d, WALL_T, awning_mat=PAINTED_GREEN)
    # Side windows
    _add_windows_all_sides(grid, w, h, d, WALL_T, 9, 14, spacing=8, win_size=2)
    # Roof
    _add_flat_roof(grid, w, h, d, WALL_T, TAR)
    # Sign
    grid[4:8, 8:9, :WALL_T] = DARK_IRON
    grid[5:7, 8:9, :WALL_T] = GOLD_BRASS

    # Pad front with 2 air voxels for protruding awning
    PROTRUDE = 2
    padded = np.zeros((w, h, d + PROTRUDE), dtype=np.uint16)
    padded[:, :, PROTRUDE:] = grid
    # Protruding green awning
    padded[WALL_T:w-WALL_T, 6:8, :PROTRUDE] = PAINTED_GREEN
    return padded


def generate_barbershop(w=BLOCK_W, h=20, d=BLOCK_D, seed=None):
    """Small white stucco shop with barber pole and striped awning protruding from front.
    
    Scale: 20v tall (2.0m), 8v door (0.8m = 1.67x NPC height).
    """
    if seed is not None: np.random.seed(seed)
    grid = np.zeros((w, h, d), dtype=np.uint16)
    grid[:, :, :] = STUCCO
    _add_basement_foundation(grid, w, d, WALL_T)
    _hollow_interior(grid, w, h, d, WALL_T, FLOOR_T)
    _add_storefront(grid, w, d, WALL_T, awning_mat=PAINTED_RED, door_h=8, door_w=6)
    # Barber pole in front wall (left of door, full height of ground floor)
    pole_x = w // 2 - 8
    for y in range(2, 16):
        grid[pole_x, y, :WALL_T] = PAINTED_METAL if (y % 2 == 0) else PAINTED_RED
    # Upper windows
    _add_windows_all_sides(grid, w, h, d, WALL_T, 12, 18, spacing=10, win_size=2)
    # Roof
    _add_flat_roof(grid, w, h, d, WALL_T, TAR)

    # Pad front with 2 air voxels for protruding features
    PROTRUDE = 2
    padded = np.zeros((w, h, d + PROTRUDE), dtype=np.uint16)
    padded[:, :, PROTRUDE:] = grid

    # Protruding awning (striped: alternate painted metal + painted red)
    for x in range(WALL_T, w - WALL_T):
        padded[x, 10:12, :PROTRUDE] = PAINTED_METAL if (x % 2 == 0) else PAINTED_RED

    # Protruding barber pole (sticks out 2 voxels from front wall)
    for y in range(2, 16):
        padded[pole_x, y, :PROTRUDE] = PAINTED_METAL if (y % 2 == 0) else PAINTED_RED

    return padded


def generate_diner(w=BLOCK_W, h=16, d=BLOCK_D, seed=None):
    """Streamline diner: stainless steel look with large windows and neon sign."""
    if seed is not None: np.random.seed(seed)
    grid = np.zeros((w, h, d), dtype=np.uint16)
    # Walls - light concrete with metal accents
    grid[:, :, :] = CONCRETE
    _add_basement_foundation(grid, w, d, WALL_T)
    _hollow_interior(grid, w, h, d, WALL_T, FLOOR_T)
    # Large storefront windows (diner style - almost full front)
    grid[WALL_T:w-WALL_T, 2:10, :WALL_T] = STOREFRONT_GLASS
    grid[WALL_T:w-WALL_T, 1:2, :WALL_T] = WINDOW_FRAME
    grid[WALL_T:w-WALL_T, 10:11, :WALL_T] = WINDOW_FRAME
    # Neon sign above
    grid[4:w-4, 11:12, :WALL_T] = NEON_RED
    grid[5:w-5, 11:12, :WALL_T] = NEON_BLUE
    # Door (standard 4v tall, 4v wide)
    dx = w // 2 - 2
    grid[dx:dx+4, 1:5, :WALL_T] = PAINTED_RED
    grid[dx:dx+4, 4:5, :WALL_T] = DARK_WOOD
    # Side windows
    _add_windows_all_sides(grid, w, h, d, WALL_T, 2, 12, spacing=8, win_size=3, material=WINDOW_GLASS)
    # Metal roof
    _add_flat_roof(grid, w, h, d, WALL_T, AGED_METAL)
    return grid


def generate_garage(w=BLOCK_W, h=14, d=BLOCK_D, seed=None):
    """Industrial garage with corrugated metal walls and large vehicle door."""
    if seed is not None: np.random.seed(seed)
    grid = np.zeros((w, h, d), dtype=np.uint16)
    # Corrugated metal walls
    grid[:, :, :] = AGED_METAL
    _add_basement_foundation(grid, w, d, WALL_T)
    _hollow_interior(grid, w, h, d, WALL_T, FLOOR_T)
    # Large vehicle door (front) — 6v tall for vehicle bay
    dx = 4
    grid[dx:dx+16, 1:7, :WALL_T] = DARK_WOOD
    # Door frame
    grid[dx-1:dx, 1:8, :WALL_T] = DARK_WOOD
    grid[dx+16:dx+17, 1:8, :WALL_T] = DARK_WOOD
    grid[dx:dx+16, 7:8, :WALL_T] = DARK_WOOD
    # Small office window
    grid[22:26, 4:7, :WALL_T] = WINDOW_GLASS
    # Side windows (high, small)
    _add_windows_all_sides(grid, w, h, d, WALL_T, 8, 12, spacing=10, win_size=2)
    # Metal roof
    _add_flat_roof(grid, w, h, d, WALL_T, AGED_METAL)
    return grid


def generate_apartments(w=BLOCK_W, h=36, d=BLOCK_D, seed=None):
    """4-story brick apartment building with fire escape."""
    if seed is not None: np.random.seed(seed)
    grid = np.zeros((w, h, d), dtype=np.uint16)
    # Red brick walls
    grid[:, :, :] = RED_BRICK
    _add_basement_foundation(grid, w, d, WALL_T)
    _hollow_interior(grid, w, h, d, WALL_T, FLOOR_T)
    # Floor slabs for 4 stories
    story_h = (h - 2) // 4
    for i in range(1, 4):
        y = i * story_h
        grid[WALL_T:w-WALL_T, y:y+1, WALL_T:d-WALL_T] = DARK_WOOD
    # Windows on all floors
    for floor in range(4):
        y_base = 2 + floor * story_h
        _add_windows_all_sides(grid, w, h, d, WALL_T, y_base, y_base + story_h - 3, spacing=6, win_size=2)
    # Front door (standard 4v tall)
    _add_doorway(grid, w, d, WALL_T, door_h=4, door_w=4, material=PAINTED_BROWN)
    # Fire escape on the front facade
    for floor in range(1, 4):
        y = floor * story_h + 1
        # Railings
        grid[4:w-4, y:y+1, 0] = DARK_IRON
        # Stairs
        if floor < 3:
            grid[4:8, y:y+4, 0] = DARK_IRON
    # Roof
    _add_flat_roof(grid, w, h, d, WALL_T, TAR)
    # Water tower on roof
    grid[6:10, h:h+4, 6:10] = WEATHERED_WOOD
    grid[7:9, h+4:h+5, 7:9] = WEATHERED_WOOD
    # Chimney
    _add_chimney(grid, w, h, d, cx=w-8, cz=d-8)

    # Pad front with 2 air voxels for protruding fire escape
    PROTRUDE = 2
    padded = np.zeros((w, h, d + PROTRUDE), dtype=np.uint16)
    padded[:, :, PROTRUDE:] = grid
    # Protruding fire escape railings and stairs
    for floor in range(1, 4):
        y = floor * story_h + 1
        # Railings stick out
        padded[4:w-4, y:y+1, :PROTRUDE] = DARK_IRON
        # Stairs stick out
        if floor < 3:
            padded[4:8, y:y+4, :PROTRUDE] = DARK_IRON
    return padded


def generate_apartment_block(w=96, h=44, d=96, seed=None):
    """Full-block 5-story apartment building. Occupies an entire city block.

    Scale: 96x96 footprint (3x standard block), 44v tall (4.4m Mob Sim).
    Standard 8v door (0.8m = 1.67x NPC height).
    Features: grand entrance with columns, detailed fire escape with zigzag
    stairs, balconies on upper floors, decorative cornices, water tower, chimneys.
    """
    if seed is not None: np.random.seed(seed)
    grid = np.zeros((w, h, d), dtype=np.uint16)

    # --- Walls ---
    grid[:, :, :] = RED_BRICK
    _add_basement_foundation(grid, w, d, WALL_T)
    _hollow_interior(grid, w, h, d, WALL_T, FLOOR_T)

    # --- Story layout ---
    ground_h = 10   # ground floor (8v door + headroom)
    story_h = 8     # upper stories
    n_floors = 5    # total floors

    # Floor slabs
    for i in range(1, n_floors):
        y = ground_h + (i - 1) * story_h
        grid[WALL_T:w-WALL_T, y:y+FLOOR_T, WALL_T:d-WALL_T] = DARK_WOOD

    # --- Grand entrance (front wall, z=0) ---
    door_w = 12
    door_h = 8
    dx = w // 2 - door_w // 2
    grid[dx:dx+door_w, 1:door_h+1, :WALL_T] = PAINTED_BROWN
    grid[dx:dx+door_w, door_h:door_h+1, :WALL_T] = DARK_WOOD
    # Columns flanking entrance
    grid[dx-3:dx, 1:door_h+2, :WALL_T] = PAINTED_METAL
    grid[dx+door_w:dx+door_w+3, 1:door_h+2, :WALL_T] = PAINTED_METAL
    # Steps
    grid[dx-3:dx+door_w+3, 0:1, :WALL_T] = STONE

    # --- Storefront windows flanking entrance (ground floor) ---
    grid[WALL_T:dx-3, 2:ground_h, :WALL_T] = STOREFRONT_GLASS
    grid[dx+door_w+3:w-WALL_T, 2:ground_h, :WALL_T] = STOREFRONT_GLASS
    grid[WALL_T:w-WALL_T, ground_h:ground_h+1, :WALL_T] = DARK_WOOD
    grid[WALL_T:w-WALL_T, 1:2, :WALL_T] = DARK_WOOD

    # --- Windows on all 4 sides for each upper floor ---
    for floor in range(n_floors - 1):
        y_base = ground_h + floor * story_h + 2
        y_top = y_base + 4
        _add_windows_all_sides(grid, w, h, d, WALL_T, y_base, y_top, spacing=8, win_size=3)

    # --- Decorative cornice between floors ---
    for i in range(1, n_floors):
        y = ground_h + (i - 1) * story_h
        grid[:WALL_T, y-1:y, :] = PAINTED_METAL
        grid[w-WALL_T:, y-1:y, :] = PAINTED_METAL
        grid[:, y-1:y, :WALL_T] = PAINTED_METAL
        grid[:, y-1:y, d-WALL_T:] = PAINTED_METAL

    # --- Fire escape (left side of front facade) ---
    fe_x = WALL_T + 6
    fe_w = 10
    for floor in range(1, n_floors):
        y_base = ground_h + (floor - 1) * story_h
        y_rail = y_base + story_h - 1
        # Landing
        grid[fe_x:fe_x+fe_w, y_rail:y_rail+1, :WALL_T] = DARK_IRON
        # Railings (vertical posts)
        grid[fe_x, y_base+2:y_rail, :WALL_T] = DARK_IRON
        grid[fe_x+fe_w-1, y_base+2:y_rail, :WALL_T] = DARK_IRON
        # Stairs (zigzag — alternate direction each floor)
        if floor < n_floors - 1:
            for sy in range(y_base + 2, y_rail):
                t = (sy - (y_base + 2)) / max(1, (y_rail - y_base - 3))
                if floor % 2 == 1:
                    sx = fe_x + int(t * (fe_w - 4))
                else:
                    sx = fe_x + fe_w - 4 - int(t * (fe_w - 4))
                grid[sx:sx+4, sy:sy+1, :WALL_T] = DARK_IRON

    # --- Balconies (right side of front facade, floors 2-4) ---
    bal_x = w - WALL_T - 24
    bal_w = 16
    for floor in range(1, n_floors - 1):
        y_base = ground_h + floor * story_h + 2
        # Balcony floor
        grid[bal_x:bal_x+bal_w, y_base:y_base+1, :WALL_T] = DARK_WOOD
        # Railings
        grid[bal_x, y_base+1:y_base+4, :WALL_T] = PAINTED_METAL
        grid[bal_x+bal_w-1, y_base+1:y_base+4, :WALL_T] = PAINTED_METAL
        grid[bal_x:bal_x+bal_w, y_base+3:y_base+4, :WALL_T] = PAINTED_METAL

    # --- Side fire escape (right wall, x=0) ---
    sfe_z = WALL_T + 6
    sfe_w = 10
    for floor in range(1, n_floors):
        y_base = ground_h + (floor - 1) * story_h
        y_rail = y_base + story_h - 1
        grid[:WALL_T, y_rail:y_rail+1, sfe_z:sfe_z+sfe_w] = DARK_IRON
        grid[:WALL_T, y_base+2:y_rail, sfe_z] = DARK_IRON
        grid[:WALL_T, y_base+2:y_rail, sfe_z+sfe_w-1] = DARK_IRON
        if floor < n_floors - 1:
            for sy in range(y_base + 2, y_rail):
                t = (sy - (y_base + 2)) / max(1, (y_rail - y_base - 3))
                if floor % 2 == 1:
                    sz = sfe_z + int(t * (sfe_w - 4))
                else:
                    sz = sfe_z + sfe_w - 4 - int(t * (sfe_w - 4))
                grid[:WALL_T, sy:sy+1, sz:sz+4] = DARK_IRON

    # --- Roof ---
    _add_flat_roof(grid, w, h, d, WALL_T, TAR)

    # Decorative parapet cornice
    grid[:WALL_T, h-3:h-2, :] = PAINTED_METAL
    grid[w-WALL_T:, h-3:h-2, :] = PAINTED_METAL
    grid[:, h-3:h-2, :WALL_T] = PAINTED_METAL
    grid[:, h-3:h-2, d-WALL_T:] = PAINTED_METAL

    # Water tower
    grid[24:32, h:h+5, 24:32] = WEATHERED_WOOD
    grid[27:29, h+5:h+6, 27:29] = WEATHERED_WOOD
    # Support legs
    for lx in [24, 31]:
        for lz in [24, 31]:
            grid[lx, h-1:h, lz] = DARK_WOOD

    # Chimneys
    _add_chimney(grid, w, h, d, cx=12, cz=12)
    _add_chimney(grid, w, h, d, cx=w-14, cz=d-14)

    # Roof access shed
    grid[44:52, h:h+4, 44:52] = RED_BRICK
    grid[46:50, h+4:h+5, 46:50] = TAR

    # --- Pad front for protruding features ---
    PROTRUDE = 2
    padded = np.zeros((w, h, d + PROTRUDE), dtype=np.uint16)
    padded[:, :, PROTRUDE:] = grid

    # Protruding entrance canopy
    padded[dx-3:dx+door_w+3, door_h+1:door_h+3, :PROTRUDE] = PAINTED_RED

    # Protruding balconies
    for floor in range(1, n_floors - 1):
        y_base = ground_h + floor * story_h + 2
        padded[bal_x:bal_x+bal_w, y_base:y_base+1, :PROTRUDE] = DARK_WOOD
        padded[bal_x, y_base+1:y_base+4, :PROTRUDE] = PAINTED_METAL
        padded[bal_x+bal_w-1, y_base+1:y_base+4, :PROTRUDE] = PAINTED_METAL
        padded[bal_x:bal_x+bal_w, y_base+3:y_base+4, :PROTRUDE] = PAINTED_METAL

    # Protruding fire escape
    for floor in range(1, n_floors):
        y_base = ground_h + (floor - 1) * story_h
        y_rail = y_base + story_h - 1
        padded[fe_x:fe_x+fe_w, y_rail:y_rail+1, :PROTRUDE] = DARK_IRON
        padded[fe_x, y_base+2:y_rail, :PROTRUDE] = DARK_IRON
        padded[fe_x+fe_w-1, y_base+2:y_rail, :PROTRUDE] = DARK_IRON
        if floor < n_floors - 1:
            for sy in range(y_base + 2, y_rail):
                t = (sy - (y_base + 2)) / max(1, (y_rail - y_base - 3))
                if floor % 2 == 1:
                    sx = fe_x + int(t * (fe_w - 4))
                else:
                    sx = fe_x + fe_w - 4 - int(t * (fe_w - 4))
                padded[sx:sx+4, sy:sy+1, :PROTRUDE] = DARK_IRON

    # Protruding steps
    padded[dx-3:dx+door_w+3, 0:1, :PROTRUDE] = STONE

    return padded


def generate_empty_land(w=BLOCK_W, h=4, d=BLOCK_D, seed=None):
    """Empty lot — flat cobblestone ground with scattered rubble."""
    if seed is not None: np.random.seed(seed)
    grid = np.zeros((w, h, d), dtype=np.uint16)
    # Ground = cobblestone
    grid[:, 0, :] = COBBLESTONE
    # Rubble: 20 random 2×2×1 clusters of stone at Y=1, kept away from perimeter
    for _ in range(20):
        rx = np.random.randint(4, w - 4)
        rz = np.random.randint(4, d - 4)
        grid[rx:rx+2, 1, rz:rz+2] = STONE
    return grid


def generate_casino(w=BLOCK_W, h=24, d=BLOCK_D, seed=None):
    """Casino with neon signs, large windows, and red carpet interior."""
    if seed is not None: np.random.seed(seed)
    grid = np.zeros((w, h, d), dtype=np.uint16)
    # Dark brick walls
    grid[:, :, :] = RED_BRICK
    _add_basement_foundation(grid, w, d, WALL_T)
    _hollow_interior(grid, w, h, d, WALL_T, FLOOR_T)
    # Large storefront windows
    grid[WALL_T:w-WALL_T, 2:8, :WALL_T] = STOREFRONT_GLASS
    grid[WALL_T:w-WALL_T, 1:2, :WALL_T] = DARK_WOOD
    grid[WALL_T:w-WALL_T, 8:9, :WALL_T] = DARK_WOOD
    # Neon signs (red and blue)
    grid[4:w-4, 9:10, :WALL_T] = NEON_RED
    grid[5:w-5, 10:11, :WALL_T] = NEON_BLUE
    grid[6:w-6, 11:12, :WALL_T] = NEON_RED
    # Grand door (civic 5v tall, 8v wide double door)
    dx = w // 2 - 4
    grid[dx:dx+8, 1:6, :WALL_T] = PAINTED_RED
    grid[dx:dx+8, 5:6, :WALL_T] = DARK_WOOD
    grid[dx-1:dx, 1:6, :WALL_T] = DARK_WOOD
    grid[dx+8:dx+9, 1:6, :WALL_T] = DARK_WOOD
    # Interior: red carpet floor
    grid[WALL_T:w-WALL_T, FLOOR_T:FLOOR_T+1, WALL_T:d-WALL_T] = PAINTED_RED
    # Upper floor windows
    _add_windows_all_sides(grid, w, h, d, WALL_T, 14, 20, spacing=6, win_size=2, material=LIT_WINDOW)
    # Roof
    _add_flat_roof(grid, w, h, d, WALL_T, TAR)
    # Gold accent on parapet
    grid[:WALL_T, h-3:h-2, :] = GOLD_BRASS
    grid[w-WALL_T:, h-3:h-2, :] = GOLD_BRASS
    grid[:, h-3:h-2, :WALL_T] = GOLD_BRASS
    grid[:, h-3:h-2, d-WALL_T:] = GOLD_BRASS
    return grid


def generate_speakeasy(w=BLOCK_W, h=18, d=BLOCK_D, seed=None):
    """Hidden speakeasy: looks like a nondescript storefront from outside."""
    if seed is not None: np.random.seed(seed)
    grid = np.zeros((w, h, d), dtype=np.uint16)
    # Unassuming tan brick
    grid[:, :, :] = RED_BRICK
    _add_basement_foundation(grid, w, d, WALL_T)
    _hollow_interior(grid, w, h, d, WALL_T, FLOOR_T)
    # Small window (not obvious)
    grid[w//2-2:w//2+2, 4:7, :WALL_T] = WINDOW_GLASS
    # Plain door (standard 4v tall, 4v wide)
    dx = w // 2 - 2
    grid[dx:dx+4, 1:5, :WALL_T] = PAINTED_GREEN
    grid[dx:dx+4, 4:5, :WALL_T] = DARK_WOOD
    # Dark interior
    grid[WALL_T:w-WALL_T, FLOOR_T:FLOOR_T+1, WALL_T:d-WALL_T] = DARK_WOOD
    # Small upper windows (lit)
    _add_windows_all_sides(grid, w, h, d, WALL_T, 10, 15, spacing=10, win_size=2, material=LIT_WINDOW)
    # Roof
    _add_flat_roof(grid, w, h, d, WALL_T, TAR)
    return grid


def generate_police_station(w=BLOCK_W, h=26, d=BLOCK_D, seed=None):
    """Police station: stone facade, blue accents, imposing entrance with protruding columns."""
    if seed is not None: np.random.seed(seed)
    grid = np.zeros((w, h, d), dtype=np.uint16)
    # Stone/concrete walls
    grid[:, :, :] = STONE
    _add_basement_foundation(grid, w, d, WALL_T)
    _hollow_interior(grid, w, h, d, WALL_T, FLOOR_T)
    # Large entrance with columns (civic 5v tall, 10v wide)
    dx = w // 2 - 5
    grid[dx:dx+10, 1:6, :WALL_T] = PAINTED_BROWN
    grid[dx:dx+10, 5:6, :WALL_T] = DARK_WOOD
    # Columns (front wall only, not extending into interior)
    grid[dx-1:dx, 1:6, :WALL_T] = PAINTED_METAL
    grid[dx+10:dx+11, 1:6, :WALL_T] = PAINTED_METAL
    # Police blue accent band
    grid[WALL_T:w-WALL_T, 5:6, :WALL_T] = PAINTED_BLUE
    # Windows
    _add_windows_all_sides(grid, w, h, d, WALL_T, 14, 22, spacing=6, win_size=2)
    # "POLICE" sign
    grid[dx:dx+10, 6:7, :WALL_T] = DARK_IRON
    grid[dx+1:dx+9, 6:7, :WALL_T] = PAINTED_BLUE
    # Roof
    _add_flat_roof(grid, w, h, d, WALL_T, CONCRETE)

    # Pad front with 2 air voxels for protruding columns
    PROTRUDE = 2
    padded = np.zeros((w, h, d + PROTRUDE), dtype=np.uint16)
    padded[:, :, PROTRUDE:] = grid
    # Protruding columns (match door height)
    padded[dx-1:dx, 1:6, :PROTRUDE] = PAINTED_METAL
    padded[dx+10:dx+11, 1:6, :PROTRUDE] = PAINTED_METAL
    return padded


def generate_hq(w=BLOCK_W, h=28, d=BLOCK_D, seed=None):
    """Gang HQ: well-maintained brick building with gold trim accents."""
    if seed is not None: np.random.seed(seed)
    grid = np.zeros((w, h, d), dtype=np.uint16)
    # Red brick walls (well maintained)
    grid[:, :, :] = STUCCO
    _add_basement_foundation(grid, w, d, WALL_T)
    _hollow_interior(grid, w, h, d, WALL_T, FLOOR_T)
    # Storefront with gold-trim awning
    _add_storefront(grid, w, d, WALL_T, awning_mat=PAINTED_RED)
    # Gold trim around door (standard 4v tall)
    dx = w // 2 - 2
    grid[dx-1:dx, 1:5, :WALL_T] = GOLD_BRASS
    grid[dx+4:dx+5, 1:5, :WALL_T] = GOLD_BRASS
    grid[dx:dx+4, 4:5, :WALL_T] = GOLD_BRASS
    # Upper floor windows with gold frames
    _add_windows_all_sides(grid, w, h, d, WALL_T, 12, 22, spacing=6, win_size=2, material=WINDOW_GLASS)
    # Gold trim around upper windows
    for x in range(WALL_T + 6, w - WALL_T - 2, 8):
        grid[x-1:x, 12:22, :WALL_T] = GOLD_BRASS
        grid[x+2:x+3, 12:22, :WALL_T] = GOLD_BRASS
    # Interior floor slab
    story_h = (h - 2) // 2
    grid[WALL_T:w-WALL_T, story_h:story_h+1, WALL_T:d-WALL_T] = DARK_WOOD
    # Roof
    _add_flat_roof(grid, w, h, d, WALL_T, TAR)
    # Gold accent on parapet
    grid[:WALL_T, h-3:h-2, :] = GOLD_BRASS
    grid[w-WALL_T:, h-3:h-2, :] = GOLD_BRASS
    grid[:, h-3:h-2, :WALL_T] = GOLD_BRASS
    grid[:, h-3:h-2, d-WALL_T:] = GOLD_BRASS
    # Chimney
    _add_chimney(grid, w, h, d, cx=w-8, cz=d-8)
    return grid


def generate_road_tile(w=BLOCK_W, h=2, d=BLOCK_D, seed=None):
    """Road tile: asphalt with sidewalk borders."""
    grid = np.zeros((w, h, d), dtype=np.uint16)
    grid[:, 0, :] = ASPHALT
    # Sidewalks on edges
    sw = 4
    grid[:sw, 0, :] = CONCRETE
    grid[w-sw:, 0, :] = CONCRETE
    grid[:, 0, :sw] = CONCRETE
    grid[:, 0, d-sw:] = CONCRETE
    return grid


# ============================================================================
# Registry: maps business type -> generator function
# ============================================================================

BUILDING_GENERATORS = {
    "butcher":      generate_butcher_shop,
    "bakery":       generate_bakery,
    "barber":       generate_barbershop,
    "diner":        generate_diner,
    "garage":       generate_garage,
    "apartments":   generate_apartments,
    "apartment_block": generate_apartment_block,
    "empty_land":   generate_empty_land,
    "casino":       generate_casino,
    "speakeasy":    generate_speakeasy,
    "card_game":    generate_speakeasy,   # card games run inside existing buildings
    "loan_shark":   generate_speakeasy,   # same - front business
    "police_station": generate_police_station,
    "hq":           generate_hq,
}


def generate_building(business_type, seed=None):
    """Generate a voxel building for the given business type.
    Returns (voxels, dims) tuple."""
    gen = BUILDING_GENERATORS.get(business_type, generate_empty_land)
    grid = gen(seed=seed)
    return grid, grid.shape


if __name__ == "__main__":
    # Quick test: generate one of each and print stats
    for btype, gen in BUILDING_GENERATORS.items():
        grid, dims = generate_building(btype, seed=42)
        non_air = np.count_nonzero(grid)
        print(f"  {btype:20s}  dims={dims[0]:2d}x{dims[1]:2d}x{dims[2]:2d}  solid={non_air:5d}")
