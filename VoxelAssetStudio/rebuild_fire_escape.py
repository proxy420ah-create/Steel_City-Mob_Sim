"""Rebuild the entire fire escape using the standardized pattern from expanded2.

Pattern (top to bottom):
- Canopy (3Y): posts + roof
- Top landing (1Y): z=0-3 solid, z=4-6 with 3v stair hole (x=7,8,9)
- 3× [Stair flight (4Y) + Landing (2Y) + Transition (2Y)] = 24Y
- Ground section (16Y): posts + gate

Total: 3 + 1 + 24 + 16 = 44Y

Standard: 3v doors/paths/stairs, 6v deep platform (4v solid inside + 3v stairwell outside)
"""
import numpy as np
from stasset_io import save_stasset

DARK_IRON = 109
W, H, D = 14, 44, 7

v = np.zeros((W, H, D), dtype=np.uint16)

def solid_inside(y, x_lo=1, x_hi=12):
    """Fill z=0-3 solid (inside walkway connecting to building)"""
    for x in range(x_lo, x_hi + 1):
        for z in range(0, 4):
            v[x, y, z] = DARK_IRON

def platform_with_hole(y, hole_lo=7, hole_hi=9):
    """z=0-3 solid, z=4-6 platform with 3v stair hole"""
    solid_inside(y)
    for x in range(1, 13):
        if hole_lo <= x <= hole_hi:
            continue
        for z in range(4, 7):
            v[x, y, z] = DARK_IRON

def transition_layer(y, gap_lo=4, gap_hi=7):
    """z=0-3 solid, z=4-6 with gap for stairs coming from above"""
    solid_inside(y)
    for x in range(1, 13):
        if gap_lo <= x <= gap_hi:
            continue
        for z in range(4, 7):
            v[x, y, z] = DARK_IRON

def stair_step(y, x_start):
    """Single stair step: 4v wide in X, 3v deep in Z (z=4,5,6)"""
    for x in range(x_start, x_start + 4):
        for z in range(4, 7):
            v[x, y, z] = DARK_IRON

def landing(y):
    """Standard landing: solid inside, platform with 3v hole outside"""
    platform_with_hole(y, hole_lo=7, hole_hi=9)

# === CANOPY (Y=41-43) ===
# Y=43: posts z=0-5, roof bar z=6
for z in range(0, 6):
    v[1, 43, z] = DARK_IRON
    v[12, 43, z] = DARK_IRON
for x in range(1, 13):
    v[x, 43, 6] = DARK_IRON

# Y=41-42: posts at z=6 only (connecting canopy to landing)
for y in [41, 42]:
    v[1, y, 6] = DARK_IRON
    v[12, y, 6] = DARK_IRON

# === TOP LANDING (Y=40) ===
platform_with_hole(40, hole_lo=7, hole_hi=9)

# === 3 CYCLES: Stairs(4Y) + Landing(2Y) + Transition(2Y) ===
# Stair X positions per step (matching original): 8, 6, 5, 4
stair_x_starts = [8, 6, 5, 4]

cycle_height = 8  # 4 stairs + 2 landing + 2 transition
base_y = 39  # first stair starts at Y=39

for cycle in range(3):
    # Stair flight (4Y, descending)
    for i, x_start in enumerate(stair_x_starts):
        y = base_y - (cycle * cycle_height) - i
        stair_step(y, x_start)
    
    # Landing (2Y)
    landing_y = base_y - (cycle * cycle_height) - 4
    landing(landing_y)
    landing(landing_y - 1)
    
    # Transition (2Y) - gap at x=4-7 for next stair flight
    trans_y = landing_y - 2
    transition_layer(trans_y, gap_lo=4, gap_hi=7)
    transition_layer(trans_y - 1, gap_lo=4, gap_hi=7)

# === GROUND SECTION (Y=0-15) ===
# Posts at x=1, x=12 on inside (z=0,1) and outside (z=5,6)
# Gate at x=6,7 on outside (z=5,6) for Y=1-8
for y in range(0, 16):
    # 4 corner posts
    for x in [1, 12]:
        for z in [0, 1, 5, 6]:
            v[x, y, z] = DARK_IRON
    # Gate bars at bottom (Y=1-8)
    if 1 <= y <= 8:
        for x in [6, 7]:
            for z in [5, 6]:
                v[x, y, z] = DARK_IRON

# === VERIFY ===
print(f"Dims: {W}x{H}x{D}")
print(f"Non-air voxels: {np.count_nonzero(v)}")

# Print all layers
for y in range(H - 1, -1, -1):
    layer = v[:, y, :]
    non_air = np.count_nonzero(layer)
    if non_air == 0:
        continue
    
    # Classify
    x_positions = set()
    for x in range(W):
        for z in range(D):
            if v[x, y, z] != 0:
                x_positions.add(x)
    x_count = len(x_positions)
    if x_count <= 5 and non_air <= 24:
        label = 'STAIR'
    elif x_count >= 10 and non_air >= 60:
        label = 'LANDING'
    elif non_air <= 12:
        label = 'POST'
    else:
        label = 'TRANS'
    
    print(f"\nY={y:2d}: {non_air:3d} voxels [{label}]")
    for z in range(D):
        row = ''
        for x in range(W):
            m = int(v[x, y, z])
            if m == 0:
                row += ' . '
            else:
                row += f'{m:3d}'
        print(f'  z={z}: {row}')

# Save
save_stasset("fire_escape_rebuilt.stasset", v, building_meta={
    "type": "fire_escape_component",
    "note": "Rebuilt with standardized 3v stairs, 6v deep platform, repeated pattern.",
    "standard": "3v doors/paths/stairs",
    "dims": [W, H, D],
})
print("\n✅ Saved fire_escape_rebuilt.stasset")
