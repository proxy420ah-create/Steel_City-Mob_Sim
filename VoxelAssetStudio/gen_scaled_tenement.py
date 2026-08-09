"""Generate a properly-scaled 1920s tenement block with detailed fire escapes.

Scale reference (at 0.1 units/voxel = building scale):
  - 1 Vinny (NPC)  = 6 voxels tall  (0.6m)
  - 1 door         = 8 voxels tall  (0.8m)
  - 1 story        = 30 voxels      (3.0m, standard 1920s tenement)
  - Fire escape landing spacing = 1 story = 30 voxels

This does NOT change the global city scale. The voxel size (0.1u/v) stays
the same — the building just has more voxels per story for correct proportions.
Block footprint (32x32) is unchanged; the building is simply taller.

Output: scaled_tenement.stasset
"""
import numpy as np
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mob_materials import *
from stasset_io import save_stasset

# --- Scale constants ---
VOXEL_M = 0.1  # meters per voxel (building scale)
STORY_V = 30   # voxels per story (3.0m)
DOOR_H = 8     # 0.8m door
DOOR_W = 3     # 0.3m door width
WALL_T = 2     # wall thickness
FLOOR_T = 2    # floor slab thickness
BUF = 4        # perimeter buffer for protruding features

# --- Building dimensions ---
BLOCK_W = 32
BLOCK_D = 32
N_STORIES = 5
GROUND_H = STORY_V  # ground floor same as upper stories
TOTAL_H = GROUND_H + (N_STORIES - 1) * STORY_V + 4  # +4 for roof/parapet

# Padded dimensions (with buffer for fire escape protrusion)
PW = BLOCK_W + BUF * 2   # 40
PH = TOTAL_H
PD = BLOCK_D + BUF * 2   # 40


def add_windows(grid, w, h, d, wt, y_base, y_top, spacing, win_w, win_h, mat=WINDOW_GLASS):
    """Add windows on all 4 walls at the given Y range."""
    # Front and back walls (z=0 and z=d-wt)
    for x in range(wt + spacing, w - wt - win_w, spacing + win_w):
        for y in range(y_base, min(y_base + win_h, y_top)):
            grid[x:x+win_w, y, :wt] = mat
            grid[x:x+win_w, y, d-wt:d] = mat
    # Left and right walls (x=0 and x=w-wt)
    for z in range(wt + spacing, d - wt - win_w, spacing + win_w):
        for y in range(y_base, min(y_base + win_h, y_top)):
            grid[:wt, y, z:z+win_w] = mat
            grid[w-wt:w, y, z:z+win_w] = mat


def add_cornice(grid, w, h, d, wt, y, mat=PAINTED_METAL):
    """Add a decorative cornice band around the building at height y."""
    grid[:wt, y:y+1, :] = mat
    grid[w-wt:w, y:y+1, :] = mat
    grid[:, y:y+1, :wt] = mat
    grid[:, y:y+1, d-wt:d] = mat


def add_fe_landing(padded, px, pz, pw, pd, y_landing, direction, mat=DARK_IRON):
    """Fire escape landing: 2v thick slab with 3v railings on outer edges."""
    y_rail_bot = y_landing + 2
    y_rail_top = y_landing + 5  # 3v tall railings

    if direction == 'front':
        padded[px:px+pw, y_landing:y_landing+2, :pd] = mat
        padded[px:px+pw, y_rail_bot:y_rail_top, 0:1] = mat
        padded[px, y_rail_bot:y_rail_top, :pd] = mat
        padded[px+pw-1, y_rail_bot:y_rail_top, :pd] = mat
    elif direction == 'back':
        sz = padded.shape[2] - pd
        padded[px:px+pw, y_landing:y_landing+2, sz:] = mat
        padded[px:px+pw, y_rail_bot:y_rail_top, sz+pd-1:sz+pd] = mat
        padded[px, y_rail_bot:y_rail_top, sz:] = mat
        padded[px+pw-1, y_rail_bot:y_rail_top, sz:] = mat
    elif direction == 'left':
        padded[:pd, y_landing:y_landing+2, pz:pz+pw] = mat
        padded[0:1, y_rail_bot:y_rail_top, pz:pz+pw] = mat
        padded[:pd, y_rail_bot:y_rail_top, pz] = mat
        padded[:pd, y_rail_bot:y_rail_top, pz+pw-1] = mat
    elif direction == 'right':
        sx = padded.shape[0] - pd
        padded[sx:, y_landing:y_landing+2, pz:pz+pw] = mat
        padded[sx+pd-1:sx+pd, y_rail_bot:y_rail_top, pz:pz+pw] = mat
        padded[sx:, y_rail_bot:y_rail_top, pz] = mat
        padded[sx:, y_rail_bot:y_rail_top, pz+pw-1] = mat


def add_fe_stairs(padded, px, pz, pw, pd, y_low, y_high, direction, mat=DARK_IRON):
    """Straight stairs between landings. Each step = 1 voxel tall."""
    n_steps = y_high - y_low
    if n_steps <= 0:
        return
    for i in range(n_steps):
        y = y_low + i
        t = i / max(1, n_steps - 1)
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


def generate_scaled_tenement():
    """Generate a properly-scaled 5-story 1920s tenement with fire escapes."""
    padded = np.zeros((PW, PH, PD), dtype=np.uint16)

    # Core building grid
    w, h, d = BLOCK_W, TOTAL_H, BLOCK_D
    grid = np.zeros((w, h, d), dtype=np.uint16)

    # --- Walls: red brick ---
    grid[:, :, :] = RED_BRICK

    # --- Basement foundation ---
    grid[:, 0:2, :] = STONE  # 2v stone foundation

    # --- Hollow interior ---
    grid[WALL_T:w-WALL_T, FLOOR_T:h-1, WALL_T:d-WALL_T] = AIR

    # --- Floor slabs for each story ---
    for i in range(1, N_STORIES):
        y = i * STORY_V
        grid[WALL_T:w-WALL_T, y:y+FLOOR_T, WALL_T:d-WALL_T] = DARK_WOOD

    # --- Front entrance ---
    dx = w // 2 - DOOR_W // 2
    # Door opening
    grid[dx:dx+DOOR_W, 2:DOOR_H+2, :WALL_T] = AIR
    # Door frame
    grid[dx-1:dx, 2:DOOR_H+3, :WALL_T] = DARK_WOOD
    grid[dx+DOOR_W:dx+DOOR_W+1, 2:DOOR_H+3, :WALL_T] = DARK_WOOD
    grid[dx:dx+DOOR_W, DOOR_H+2:DOOR_H+3, :WALL_T] = DARK_WOOD
    # Door itself
    grid[dx:dx+DOOR_W, 2:DOOR_H+2, 0] = PAINTED_BROWN
    # Doorknob
    grid[dx+DOOR_W-1, 5, 0] = GOLD_BRASS
    # Stone steps
    grid[dx-2:dx+DOOR_W+2, 0:2, :WALL_T] = STONE

    # --- Storefront windows flanking entrance (ground floor) ---
    glass_top = DOOR_H + 2
    grid[WALL_T:dx-1, 3:glass_top, :WALL_T] = STOREFRONT_GLASS
    grid[dx+DOOR_W+1:w-WALL_T, 3:glass_top, :WALL_T] = STOREFRONT_GLASS
    # Storefront frame
    grid[WALL_T:w-WALL_T, 2:3, :WALL_T] = DARK_WOOD
    grid[WALL_T:w-WALL_T, glass_top:glass_top+1, :WALL_T] = DARK_WOOD

    # --- Windows on upper floors ---
    for floor in range(1, N_STORIES):
        y_base = floor * STORY_V + 4
        y_top = floor * STORY_V + STORY_V - 4
        add_windows(grid, w, h, d, WALL_T, y_base, y_top,
                    spacing=6, win_w=3, win_h=8, mat=WINDOW_GLASS)

    # --- Decorative cornices between floors ---
    for i in range(1, N_STORIES):
        y = i * STORY_V
        add_cornice(grid, w, h, d, WALL_T, y - 1, PAINTED_METAL)

    # --- Roof ---
    # Flat tar roof
    grid[WALL_T:w-WALL_T, h-2:h, WALL_T:d-WALL_T] = TAR
    # Parapet walls (2v above roof)
    grid[:WALL_T, h:h+2, :] = RED_BRICK
    grid[w-WALL_T:w, h:h+2, :] = RED_BRICK
    grid[:, h:h+2, :WALL_T] = RED_BRICK
    grid[:, h:h+2, d-WALL_T:d] = RED_BRICK
    # Parapet cornice
    add_cornice(grid, w, h+2, d, WALL_T, 0, PAINTED_METAL)

    # --- Water tower on roof ---
    wtx, wtz = 8, 8
    grid[wtx:wtx+6, h+2:h+7, wtz:wtz+6] = WEATHERED_WOOD
    grid[wtx+1:wtx+5, h+7:h+8, wtz+1:wtz+5] = WEATHERED_WOOD
    # Legs
    for lx in [wtx, wtx+5]:
        for lz in [wtz, wtz+5]:
            grid[lx, h:h+2, lz] = DARK_WOOD

    # --- Chimneys ---
    for cx, cz in [(4, 4), (w-6, d-6)]:
        grid[cx:cx+3, h:h+6, cz:cz+3] = RED_BRICK
        grid[cx:cx+3, h+6:h+7, cz:cz+3] = DARK_WOOD

    # --- Roof access shed ---
    sx, sz = w//2 - 4, d//2 - 4
    grid[sx:sx+8, h:h+4, sz:sz+8] = RED_BRICK
    grid[sx+1:sx+7, h+4:h+5, sz+1:sz+7] = TAR

    # --- Place core building into padded grid ---
    padded[BUF:BUF+w, :, BUF:BUF+d] = grid

    # --- Fire escapes (properly scaled, STORY_V apart) ---
    fe_w = 12   # width along wall
    fe_d = 4    # protrusion depth (full buffer)

    # Rear fire escape (back facade, +Z buffer)
    fe_x = BUF + w - WALL_T - 4 - fe_w

    # Side fire escape (left facade, -X buffer)
    sfe_w = 12
    sfe_z = BUF + WALL_T + 4

    for floor in range(1, N_STORIES):
        y_landing = floor * STORY_V - 2  # landing near top of each floor

        # Rear landing
        add_fe_landing(padded, fe_x, 0, fe_w, fe_d, y_landing, 'back')
        # Side landing
        add_fe_landing(padded, 0, sfe_z, sfe_w, fe_d, y_landing, 'left')

        # Stairs from this landing down to the one below
        if floor > 1:
            y_prev = (floor - 1) * STORY_V - 2
            y_low = y_prev + 2   # top of lower landing
            y_high = y_landing    # bottom of this landing
            add_fe_stairs(padded, fe_x, 0, fe_w, fe_d, y_low, y_high, 'back')
            add_fe_stairs(padded, 0, sfe_z, sfe_w, fe_d, y_low, y_high, 'left')

    # --- Vertical support posts (ground to first landing) ---
    y_first = STORY_V - 2
    rear_z = PD - 1
    for sx in [fe_x, fe_x + fe_w - 1]:
        padded[sx, 0:y_first, rear_z] = DARK_IRON
        padded[sx, 0:y_first, rear_z - fe_d + 1] = DARK_IRON

    for sz in [sfe_z, sfe_z + sfe_w - 1]:
        padded[0, 0:y_first, sz] = DARK_IRON
        padded[fe_d - 1, 0:y_first, sz] = DARK_IRON

    # --- Drop ladder from ground floor landing to ground (rear) ---
    y_ground_landing = GROUND_H - 2
    ladder_x = fe_x + fe_w // 2 - 1
    for ry in range(2, y_ground_landing + 1):
        padded[ladder_x:ladder_x+2, ry:ry+1, rear_z] = DARK_IRON

    # --- Drop ladder (side) ---
    ladder_z = sfe_z + sfe_w // 2 - 1
    for ry in range(2, y_ground_landing + 1):
        padded[0, ry:ry+1, ladder_z:ladder_z+2] = DARK_IRON

    # --- Entrance canopy (front, -Z buffer) ---
    canopy_x = BUF + dx - 3
    canopy_w = DOOR_W + 6
    padded[canopy_x:canopy_x+canopy_w, DOOR_H+3:DOOR_H+5, :BUF] = PAINTED_RED
    # Canopy support brackets
    padded[canopy_x, DOOR_H+2:DOOR_H+5, 0] = DARK_WOOD
    padded[canopy_x+canopy_w-1, DOOR_H+2:DOOR_H+5, 0] = DARK_WOOD

    # --- Protruding stone steps (front) ---
    padded[canopy_x:canopy_x+canopy_w, 0:2, :BUF] = STONE

    meta = {
        "door_face": "front",
        "door_height": DOOR_H,
        "door_width": DOOR_W,
        "door_y": 2,
        "door_x_center": (BUF + w) // 2,
        "stories": N_STORIES,
        "story_height_voxels": STORY_V,
        "story_height_meters": STORY_V * VOXEL_M,
        "total_height_voxels": PH,
        "total_height_meters": PH * VOXEL_M,
        "scale_note": "Properly scaled: 30v/story = 3.0m. Door 8v = 0.8m. Vinny 6v = 0.6m.",
    }

    return padded, meta


def main():
    grid, meta = generate_scaled_tenement()
    w, h, d = grid.shape

    print(f"Scaled Tenement Block")
    print(f"  Dimensions: {w}x{h}x{d} voxels")
    print(f"  Height: {h * VOXEL_M:.1f}m ({h} voxels)")
    print(f"  Stories: {N_STORIES} x {STORY_V}v = {STORY_V * VOXEL_M:.1f}m each")
    print(f"  Door: {DOOR_H}v = {DOOR_H * VOXEL_M:.1f}m")
    print(f"  Vinny reference: 6v = 0.6m (door is {DOOR_H/6:.1f}x Vinny)")
    print(f"  Non-air voxels: {np.count_nonzero(grid)}")

    out_path = "scaled_tenement.stasset"
    save_stasset(out_path, grid, building_meta=meta)
    print(f"Saved: {out_path}")

    # Also generate the editor HTML
    import voxel_editor_html as veh
    veh.voxel_to_editor(grid, (w, h, d), "voxel_editor.html", title="Scaled Tenement")
    print(f"Saved: voxel_editor.html")


if __name__ == "__main__":
    main()
