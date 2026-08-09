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


def _add_doorway(grid, w, d, wt, door_h=8, door_w=3, material=PAINTED_BROWN):
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


def _add_storefront(grid, w, d, wt, awning_mat=PAINTED_RED, door_h=8, door_w=3):
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
    _add_storefront(grid, w, d, WALL_T, awning_mat=PAINTED_RED, door_h=8, door_w=3)
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

    meta = {
        "door_face": "front",
        "door_height": 8,
        "door_width": 3,
        "door_y": 1,
        "door_x_center": w // 2,
    }
    return padded, meta


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


def _add_fe_landing(padded, px, pz, pw, pd, y_landing, direction, hole=None, mat=DARK_IRON):
    """Add a fire escape landing platform with railings.

    Landing is a 2-voxel thick iron slab protruding from the wall.
    Railings (2v tall) run along the 3 outer edges (not the wall side).

    direction: 'front' (-Z), 'back' (+Z), 'left' (-X), 'right' (+X)
    px, pz: wall-parallel start coordinate
    pw: width along the wall
    pd: protrusion depth (how far it sticks out)
    y_landing: bottom of the landing slab
    hole: (start, width) tuple for a rectangular hole in the landing
          where stairs descend through. None = solid landing.
    """
    y_rail_bottom = y_landing + 2
    y_rail_top = y_landing + 4

    if direction == 'front':
        # Solid landing
        padded[px:px+pw, y_landing:y_landing+2, :pd] = mat
        # Carve hole if specified
        if hole:
            hs, hw = hole
            padded[px+hs:px+hs+hw, y_landing:y_landing+2, :pd] = 0
        # Railings: front edge + 2 sides (not wall side at z=pd-1)
        padded[px:px+pw, y_rail_bottom:y_rail_top, 0:1] = mat
        padded[px, y_rail_bottom:y_rail_top, :pd] = mat
        padded[px+pw-1, y_rail_bottom:y_rail_top, :pd] = mat
        # Railing around hole if present
        if hole:
            hs, hw = hole
            padded[px+hs-1:px+hs, y_rail_bottom:y_rail_top, :pd] = mat
            padded[px+hs+hw:px+hs+hw+1, y_rail_bottom:y_rail_top, :pd] = mat

    elif direction == 'back':
        sz = padded.shape[2] - pd
        padded[px:px+pw, y_landing:y_landing+2, sz:] = mat
        if hole:
            hs, hw = hole
            padded[px+hs:px+hs+hw, y_landing:y_landing+2, sz:] = 0
        padded[px:px+pw, y_rail_bottom:y_rail_top, sz+pd-1:sz+pd] = mat
        padded[px, y_rail_bottom:y_rail_top, sz:] = mat
        padded[px+pw-1, y_rail_bottom:y_rail_top, sz:] = mat
        if hole:
            hs, hw = hole
            padded[px+hs-1:px+hs, y_rail_bottom:y_rail_top, sz:] = mat
            padded[px+hs+hw:px+hs+hw+1, y_rail_bottom:y_rail_top, sz:] = mat

    elif direction == 'left':
        padded[:pd, y_landing:y_landing+2, pz:pz+pw] = mat
        if hole:
            hs, hw = hole
            padded[:pd, y_landing:y_landing+2, pz+hs:pz+hs+hw] = 0
        padded[0:1, y_rail_bottom:y_rail_top, pz:pz+pw] = mat
        padded[:pd, y_rail_bottom:y_rail_top, pz] = mat
        padded[:pd, y_rail_bottom:y_rail_top, pz+pw-1] = mat
        if hole:
            hs, hw = hole
            padded[:pd, y_rail_bottom:y_rail_top, pz+hs-1:pz+hs] = mat
            padded[:pd, y_rail_bottom:y_rail_top, pz+hs+hw:pz+hs+hw+1] = mat

    elif direction == 'right':
        sx = padded.shape[0] - pd
        padded[sx:, y_landing:y_landing+2, pz:pz+pw] = mat
        if hole:
            hs, hw = hole
            padded[sx:, y_landing:y_landing+2, pz+hs:pz+hs+hw] = 0
        padded[sx+pd-1:sx+pd, y_rail_bottom:y_rail_top, pz:pz+pw] = mat
        padded[sx:, y_rail_bottom:y_rail_top, pz] = mat
        padded[sx:, y_rail_bottom:y_rail_top, pz+pw-1] = mat
        if hole:
            hs, hw = hole
            padded[sx:, y_rail_bottom:y_rail_top, pz+hs-1:pz+hs] = mat
            padded[sx:, y_rail_bottom:y_rail_top, pz+hs+hw:pz+hs+hw+1] = mat


def _add_fe_stairs(padded, px, pz, pw, pd, y_low, y_high, direction,
                    axis='along_x', mat=DARK_IRON):
    """Add straight stairs between two landings.

    Stairs go from y_low (bottom) to y_high (top).
    Each step is 1 voxel tall, 4 voxels wide (along wall), full buffer depth.
    The stair position shifts across the landing width as it rises.
    """
    n_steps = y_high - y_low
    if n_steps <= 0:
        return
    for i in range(n_steps):
        y = y_low + i
        t = i / max(1, n_steps - 1)
        if axis == 'along_x':
            sx = px + 1 + int(t * (pw - 6))
            sw = 4
            if direction == 'front':
                padded[sx:sx+sw, y:y+1, 0:pd] = mat
            elif direction == 'back':
                sz = padded.shape[2] - pd
                padded[sx:sx+sw, y:y+1, sz:] = mat
            elif direction == 'left':
                padded[0:pd, y:y+1, sx:sx+sw] = mat
            elif direction == 'right':
                sxr = padded.shape[0] - pd
                padded[sxr:, y:y+1, sx:sx+sw] = mat
        else:  # along_z
            sz = pz + 1 + int(t * (pw - 6))
            sw = 4
            if direction == 'front':
                padded[0:pd, y:y+1, sz:sz+sw] = mat
            elif direction == 'back':
                szb = padded.shape[2] - pd
                padded[0:pd, y:y+1, szb+sz:szb+sz+sw] = mat
            elif direction == 'left':
                padded[0:pd, y:y+1, sz:sz+sw] = mat
            elif direction == 'right':
                sxr = padded.shape[0] - pd
                padded[sxr:, y:y+1, sz:sz+sw] = mat


def generate_apartment_block(w=88, h=44, d=88, seed=None, roof_buf=8):
    """Full-block 5-story apartment building. Occupies an entire city block.

    Core: 88x88 footprint, 4-voxel perimeter buffer → 96x96 total (fits block).
    44v tall (4.4m Mob Sim). Standard 8v door (0.8m = 1.67x NPC height).
    roof_buf: extra voxels above roof for water tower, chimneys, parapet, etc.
    Features: grand entrance with columns, redesigned fire escapes with
    2-voxel thick landings with holes for stairs, proper stair platforms,
    support posts, drop ladder, connecting bridge, balconies, cornices,
    water tower, chimneys.
    """
    if seed is not None: np.random.seed(seed)

    # --- Perimeter buffer: 4 voxels on all 4 sides ---
    BUF = 4
    pw = w + BUF * 2   # 96
    ph = h + roof_buf  # 44 + 8 = 52 (extra room for roof decorations)
    pd = d + BUF * 2   # 96
    padded = np.zeros((pw, ph, pd), dtype=np.uint16)

    # Build the core building in a sub-grid, then place it inside the buffer
    grid = np.zeros((w, ph, d), dtype=np.uint16)

    # --- Walls ---
    grid[:, :, :] = RED_BRICK
    _add_basement_foundation(grid, w, d, WALL_T)
    _hollow_interior(grid, w, ph, d, WALL_T, FLOOR_T)

    # --- Story layout ---
    ground_h = 10   # ground floor (8v door + headroom)
    story_h = 8     # upper stories
    n_floors = 5    # total floors

    # Floor slabs
    for i in range(1, n_floors):
        y = ground_h + (i - 1) * story_h
        grid[WALL_T:w-WALL_T, y:y+FLOOR_T, WALL_T:d-WALL_T] = DARK_WOOD

    # --- Grand entrance (front wall, z=0) ---
    door_w = 6     # double door (2x standard 3v)
    door_h = 8
    dx = w // 2 - door_w // 2
    grid[dx:dx+door_w, 1:door_h+1, :WALL_T] = PAINTED_BROWN
    grid[dx:dx+door_w, door_h:door_h+1, :WALL_T] = DARK_WOOD
    # Doorknobs (double door — knobs on inner edges at waist height)
    grid[dx + door_w // 2 - 1, 4, 0] = GOLD_BRASS  # left door, right edge
    grid[dx + door_w // 2, 4, 0] = GOLD_BRASS      # right door, left edge
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
    for lx in [24, 31]:
        for lz in [24, 31]:
            grid[lx, h-1:h, lz] = DARK_WOOD

    # Chimneys
    _add_chimney(grid, w, h, d, cx=12, cz=12)
    _add_chimney(grid, w, h, d, cx=w-14, cz=d-14)

    # Roof access shed
    grid[44:52, h:h+4, 44:52] = RED_BRICK
    grid[46:50, h+4:h+5, 46:50] = TAR

    # --- Place core building into padded grid with buffer ---
    padded[BUF:BUF+w, :, BUF:BUF+d] = grid

    # --- Now add protruding features into the perimeter buffer ---

    # Protruding entrance canopy (front, -Z buffer)
    canopy_x = BUF + dx - 3
    padded[canopy_x:canopy_x+door_w+6, door_h+1:door_h+3, :BUF] = PAINTED_RED

    # Protruding steps (front, -Z buffer)
    padded[canopy_x:canopy_x+door_w+6, 0:1, :BUF] = STONE

    # --- Redesigned fire escapes with holes and platforms ---
    # Rear fire escape: left side of back facade, protrudes into +Z buffer
    fe_w = 12   # width along wall (X direction) — wider for hole + stairs
    fe_d = 4    # protrusion depth (uses full 4-voxel buffer)
    fe_x = BUF + w - WALL_T - 4 - fe_w  # right side of rear facade in padded coords

    # Side fire escape: left wall (x=0 in core), protrudes into -X buffer
    sfe_w = 12   # width along wall (Z direction)
    sfe_d = 4    # protrusion depth
    sfe_z = BUF + WALL_T + 4  # offset from front edge in padded coords

    # Stair hole: 4-voxel wide slot in the landing for stairs to pass through
    hole_w = 4
    hole_offset = 3  # hole starts 3 voxels from left edge of landing

    for floor in range(1, n_floors):
        y_base = ground_h + (floor - 1) * story_h
        y_ceil = y_base + story_h
        y_landing = y_ceil - 2  # landing sits near top of floor

        # Rear fire escape landing with hole (protrudes +Z)
        # Alternate hole side each floor so stairs zigzag
        if floor % 2 == 1:
            f_hole = (hole_offset, hole_w)
        else:
            f_hole = (fe_w - hole_offset - hole_w, hole_w)
        _add_fe_landing(padded, fe_x, 0, fe_w, fe_d, y_landing, 'back', hole=f_hole)

        # Side fire escape landing with hole (protrudes -X)
        if floor % 2 == 1:
            s_hole = (hole_offset, hole_w)
        else:
            s_hole = (sfe_w - hole_offset - hole_w, hole_w)
        _add_fe_landing(padded, 0, sfe_z, sfe_w, sfe_d, y_landing, 'left', hole=s_hole)

        # Stairs descend from this landing's hole down to the landing below
        if floor > 1:
            y_prev_landing = ground_h + (floor - 2) * story_h + story_h - 2
            y_low = y_prev_landing + 2   # top of lower landing slab
            y_high = y_landing             # bottom of this landing slab
            # Rear stairs
            _add_fe_stairs(padded, fe_x, 0, fe_w, fe_d,
                           y_low, y_high, 'back', axis='along_x')
            # Side stairs
            _add_fe_stairs(padded, 0, sfe_z, sfe_w, sfe_d,
                           y_low, y_high, 'left', axis='along_z')

    # Vertical support posts (rear escape, from ground to first landing)
    y_first_landing = ground_h + story_h - 2
    rear_z = padded.shape[2] - 1  # last z index
    for sx in [fe_x, fe_x + fe_w - 1]:
        padded[sx, 0:y_first_landing, rear_z] = DARK_IRON
    # Mid support posts at buffer depth
    for sx in [fe_x, fe_x + fe_w - 1]:
        padded[sx, 0:y_first_landing, rear_z - fe_d + 1] = DARK_IRON

    # Vertical support posts (side escape)
    for sz in [sfe_z, sfe_z + sfe_w - 1]:
        padded[0, 0:y_first_landing, sz] = DARK_IRON
    for sz in [sfe_z, sfe_z + sfe_w - 1]:
        padded[fe_d - 1, 0:y_first_landing, sz] = DARK_IRON

    # Drop ladder from first landing to ground (rear escape, classic NYC)
    y_ground_landing = ground_h - 2
    ladder_x = fe_x + fe_w // 2 - 1
    padded[ladder_x:ladder_x+2, 1:y_ground_landing+1, rear_z] = DARK_IRON
    for ry in range(1, y_ground_landing, 2):
        padded[ladder_x:ladder_x+2, ry:ry+1, rear_z] = DARK_IRON

    # Drop ladder from first landing to ground (side escape)
    ladder_z = sfe_z + sfe_w // 2 - 1
    padded[0, 1:y_ground_landing+1, ladder_z:ladder_z+2] = DARK_IRON
    for ry in range(1, y_ground_landing, 2):
        padded[0, ry:ry+1, ladder_z:ladder_z+2] = DARK_IRON

    meta = {
        "door_face": "front",
        "door_height": 8,
        "door_width": 6,
        "door_y": 1,
        "door_x_center": (BUF + w) // 2,
    }
    return padded, meta


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
    "butcher":          generate_butcher_shop,
    "bakery":           generate_bakery,
    "barber":           generate_barbershop,
    "tenement_block":   generate_apartment_block,
    "empty_land":       generate_empty_land,
}

# --- Trimmed (do not adhere to 8v door standard) ---
# These generators still exist in the file for reference, but are removed
# from BUILDING_GENERATORS until they are rebuilt with 8v doors.
# Trimmed: diner (4v), garage (7v vehicle bay), apartments (4v),
#          speakeasy (4v), casino (5v), police_station (5v),
#          card_game (alias→speakeasy), loan_shark (alias→speakeasy),
#          hq (HQs are tenement blocks, not a special building type)


def generate_building(business_type, seed=None):
    """Generate a voxel building for the given business type.
    Returns (voxels, dims, building_meta) tuple. building_meta is None for
    generators that don't provide door metadata."""
    gen = BUILDING_GENERATORS.get(business_type, generate_empty_land)
    result = gen(seed=seed)
    if isinstance(result, tuple):
        grid, meta = result
    else:
        grid, meta = result, None
    return grid, grid.shape, meta


if __name__ == "__main__":
    # Quick test: generate one of each and print stats
    for btype, gen in BUILDING_GENERATORS.items():
        grid, dims, meta = generate_building(btype, seed=42)
        non_air = np.count_nonzero(grid)
        meta_str = f"  door={meta['door_face']}" if meta else "  no-meta"
        print(f"  {btype:20s}  dims={dims[0]:2d}x{dims[1]:2d}x{dims[2]:2d}  solid={non_air:5d}{meta_str}")
