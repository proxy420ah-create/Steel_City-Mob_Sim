# Steel City: Mob Sim — Procedural Voxel Character Generators
# procedural_mob_characters.py
#
# Characters are larger voxel grids (16×32×10) rendered at a smaller voxel size
# than buildings, giving more detail per character while keeping proper world scale.
#
# Grid format: (width, height, depth) with Y as vertical.
# Indexing: grid[x, y, z] where x=width, y=height, z=depth

import numpy as np
from mob_materials import *

# Default character dimensions — large enough for recognizable detail
CHAR_W = 16      # width (shoulders + arms)
CHAR_H = 32      # height (including hat)
CHAR_D = 10      # depth (front-to-back)


def generate_hoodlum(seed=None):
    """A mob wise guy: black fedora, black suit, white shirt, dark red tie, sunglasses.

    Layout (16 wide × 32 tall × 10 deep):
      Y 0-1:    shoes
      Y 2-12:   legs (black pants)
      Y 13-22:  torso (black suit jacket + white shirt + tie)
      Y 23-24:  neck (flesh)
      Y 25-27:  head (flesh + hair + sunglasses)
      Y 28:     hat brim (wide, black)
      Y 29-31:  hat crown (black fedora + red band)
    """
    if seed is not None:
        np.random.seed(seed)

    w, h, d = CHAR_W, CHAR_H, CHAR_D
    grid = np.zeros((w, h, d), dtype=np.uint16)

    # === Shoes (Y=0-1) ===
    grid[3:6, 0:2, 2:5] = BLACK_FABRIC    # left shoe
    grid[10:13, 0:2, 2:5] = BLACK_FABRIC  # right shoe
    grid[3:6, 0:2, 5:8] = BLACK_FABRIC
    grid[10:13, 0:2, 5:8] = BLACK_FABRIC

    # === Legs (Y=2-12) — black pants ===
    for y in range(2, 13):
        grid[3:6, y, 2:8] = BLACK_FABRIC   # left leg
        grid[10:13, y, 2:8] = BLACK_FABRIC  # right leg

    # === Torso (Y=13-22) — black suit jacket ===
    for y in range(13, 23):
        grid[1:15, y, 1:9] = BLACK_FABRIC

    # White shirt V at chest (Y=15-19, center front z=1)
    for y in range(15, 20):
        grid[6:10, y, 1] = WHITE_FABRIC

    # Dark red tie down the middle (Y=15-22, front)
    for y in range(15, 23):
        grid[7:9, y, 1] = PAINTED_RED

    # Suit lapels (darker, Y=15-19)
    grid[5:6, 15:20, 1] = BLACK_FABRIC   # left lapel
    grid[10:11, 15:20, 1] = BLACK_FABRIC  # right lapel

    # === Shoulders (Y=22) — slightly wider ===
    grid[0:16, 22, 0:10] = BLACK_FABRIC

    # === Neck (Y=23-24) ===
    grid[6:10, 23:25, 3:7] = FLESH

    # === Head (Y=25-27) ===
    grid[4:12, 25:28, 2:8] = FLESH

    # === Hair (Y=25, back of head) ===
    grid[4:12, 25, 6:8] = HAIR
    grid[4:12, 26, 7:8] = HAIR

    # === Sunglasses (Y=26, front face z=2) — 4 voxels wide for visibility ===
    grid[4:8, 26, 2] = DARK_IRON   # left lens
    grid[8:12, 26, 2] = DARK_IRON  # right lens

    # === Hat brim (Y=28) — wider than head, fedora style ===
    grid[0:16, 28, 0:10] = BLACK_FABRIC

    # === Hat crown (Y=29-31) — narrower than brim ===
    grid[2:14, 29:32, 1:9] = BLACK_FABRIC

    # Hat band (dark red, Y=29)
    grid[2:14, 29, 1:9] = PAINTED_RED

    # === Arms (Y=13-22, sides) ===
    for y in range(13, 23):
        grid[0:1, y, 2:8] = BLACK_FABRIC   # left arm
        grid[15:16, y, 2:8] = BLACK_FABRIC  # right arm

    # Hands (flesh, at bottom of arms, Y=13-14 — waist level)
    grid[0:1, 13:15, 3:7] = FLESH   # left hand
    grid[15:16, 13:15, 3:7] = FLESH  # right hand

    return grid


def generate_hoodlum_overcoat(seed=None):
    """Hoodlum with tan overcoat over suit — bulkier silhouette."""
    if seed is not None:
        np.random.seed(seed)

    w, h, d = CHAR_W + 4, CHAR_H, CHAR_D + 4
    grid = np.zeros((w, h, d), dtype=np.uint16)

    # Shoes
    grid[4:7, 0:2, 3:6] = BLACK_FABRIC
    grid[13:16, 0:2, 3:6] = BLACK_FABRIC
    grid[4:7, 0:2, 6:9] = BLACK_FABRIC
    grid[13:16, 0:2, 6:9] = BLACK_FABRIC

    # Legs
    for y in range(2, 13):
        grid[4:7, y, 3:9] = BLACK_FABRIC
        grid[13:16, y, 3:9] = BLACK_FABRIC

    # Overcoat torso (tan, wider than suit)
    for y in range(13, 25):
        grid[0:20, y, 0:14] = LIGHT_WOOD

    # Coat opening (center front, shows black suit + shirt)
    for y in range(15, 22):
        grid[8:12, y, 0] = BLACK_FABRIC
    grid[8:12, 17:19, 0] = WHITE_FABRIC
    grid[9:11, 18:22, 0] = PAINTED_RED

    # Neck
    grid[8:12, 25:27, 4:8] = FLESH

    # Head
    grid[6:14, 27:30, 3:9] = FLESH

    # Hair (back)
    grid[6:14, 27, 7:9] = HAIR
    grid[6:14, 28, 8:9] = HAIR

    # Sunglasses
    grid[6:10, 28, 3] = DARK_IRON
    grid[10:14, 28, 3] = DARK_IRON

    # Hat brim
    grid[0:20, 30, 0:14] = BLACK_FABRIC

    # Hat crown
    grid[2:18, 31:34, 1:13] = BLACK_FABRIC
    grid[2:18, 31, 1:13] = PAINTED_RED  # hat band

    # Arms (overcoat sleeves)
    for y in range(13, 25):
        grid[0:1, y, 1:13] = LIGHT_WOOD
        grid[19:20, y, 1:13] = LIGHT_WOOD

    # Hands (at bottom of sleeves, waist level)
    grid[0:1, 13:15, 4:8] = FLESH
    grid[19:20, 13:15, 4:8] = FLESH

    return grid


def generate_police_officer(seed=None):
    """Police officer: blue uniform, cap, no sunglasses."""
    if seed is not None:
        np.random.seed(seed)

    w, h, d = CHAR_W, CHAR_H, CHAR_D
    grid = np.zeros((w, h, d), dtype=np.uint16)

    # Shoes
    grid[3:6, 0:2, 2:5] = BLACK_FABRIC
    grid[10:13, 0:2, 2:5] = BLACK_FABRIC
    grid[3:6, 0:2, 5:8] = BLACK_FABRIC
    grid[10:13, 0:2, 5:8] = BLACK_FABRIC

    # Legs (dark blue pants)
    for y in range(2, 13):
        grid[3:6, y, 2:8] = PAINTED_BLUE
        grid[10:13, y, 2:8] = PAINTED_BLUE

    # Torso (blue uniform)
    for y in range(13, 23):
        grid[1:15, y, 1:9] = PAINTED_BLUE

    # Badge (gold, on chest)
    grid[6:10, 15, 1] = GOLD_BRASS

    # White shirt at collar
    grid[6:10, 13, 1] = WHITE_FABRIC

    # Neck
    grid[6:10, 23:25, 3:7] = FLESH

    # Head
    grid[4:12, 25:28, 2:8] = FLESH

    # Hair
    grid[4:12, 25, 6:8] = HAIR
    grid[4:12, 26, 7:8] = HAIR

    # Police cap (brim + crown, blue with gold badge)
    grid[0:16, 28, 0:10] = PAINTED_BLUE
    grid[2:14, 29:32, 1:9] = PAINTED_BLUE
    grid[6:10, 29, 1] = GOLD_BRASS  # cap badge

    # Arms
    for y in range(13, 23):
        grid[0:1, y, 2:8] = PAINTED_BLUE
        grid[15:16, y, 2:8] = PAINTED_BLUE

    # Hands (at bottom of arms, waist level)
    grid[0:1, 13:15, 3:7] = FLESH
    grid[15:16, 13:15, 3:7] = FLESH

    return grid


def generate_civilian(seed=None):
    """Generic civilian: brown coat, no hat, varied."""
    if seed is not None:
        np.random.seed(seed)

    w, h, d = CHAR_W, CHAR_H - 4, CHAR_D  # no hat = shorter
    grid = np.zeros((w, h, d), dtype=np.uint16)

    # Shoes
    grid[3:6, 0:2, 2:5] = BLACK_FABRIC
    grid[10:13, 0:2, 2:5] = BLACK_FABRIC
    grid[3:6, 0:2, 5:8] = BLACK_FABRIC
    grid[10:13, 0:2, 5:8] = BLACK_FABRIC

    # Legs
    for y in range(2, 13):
        grid[3:6, y, 2:8] = DARK_WOOD  # brown pants
        grid[10:13, y, 2:8] = DARK_WOOD

    # Torso (brown coat)
    for y in range(13, 23):
        grid[1:15, y, 1:9] = DARK_WOOD

    # White shirt at collar
    grid[6:10, 13, 1] = WHITE_FABRIC

    # Neck
    grid[6:10, 23:25, 3:7] = FLESH

    # Head
    grid[4:12, 25:28, 2:8] = FLESH

    # Hair
    grid[4:12, 25, 6:8] = HAIR
    grid[4:12, 26, 7:8] = HAIR

    # Arms
    for y in range(13, 23):
        grid[0:1, y, 2:8] = DARK_WOOD
        grid[15:16, y, 2:8] = DARK_WOOD

    # Hands (at bottom of arms, waist level)
    grid[0:1, 13:15, 3:7] = FLESH
    grid[15:16, 13:15, 3:7] = FLESH

    return grid


# Registry
CHARACTER_GENERATORS = {
    "hoodlum": generate_hoodlum,
    "hoodlum_overcoat": generate_hoodlum_overcoat,
    "police": generate_police_officer,
    "civilian": generate_civilian,
}


def generate_character(char_type, seed=None):
    """Generate a voxel character. Returns the numpy grid."""
    gen = CHARACTER_GENERATORS.get(char_type, generate_civilian)
    return gen(seed=seed)


if __name__ == "__main__":
    for ctype, gen in CHARACTER_GENERATORS.items():
        grid = gen(seed=42)
        non_air = np.count_nonzero(grid)
        print(f"  {ctype:20s}  dims={grid.shape[0]}x{grid.shape[1]}x{grid.shape[2]}  solid={non_air:4d}")
