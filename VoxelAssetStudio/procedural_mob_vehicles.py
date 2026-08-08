# Steel City: Mob Sim — Procedural Voxel Vehicle Generators
# procedural_mob_vehicles.py
#
# Vehicles use the same .stasset format as buildings and characters.
# Grid format: (width, height, depth) with Y as vertical.
# Indexing: grid[x, y, z] where x=width, y=height, z=depth
#
# Front of vehicle faces +Z (high Z values) to match Unity's LookRotation forward.
#
# Scale reference:
#   Characters are 16x32x10 at 0.02m/voxel = 0.32m x 0.64m x 0.20m
#   At vehicle voxel size (0.05m), each character ~= 6.4 x 12.8 x 4 voxels
#   A 1920s touring car needs to fit 4 characters (2+2 bench seating)

import numpy as np
from mob_materials import *

# Default vehicle dimensions — sized to fit 4 characters at 0.05m/voxel
VEH_W = 20   # width (2 characters side-by-side + body walls + wheel clearance)
VEH_H = 16   # height (wheels + cabin + roof supports)
VEH_D = 30   # depth (rear deck + 2 seat rows + dashboard + engine + grille)


def _draw_wheel(grid, x0, y0, z0):
    """Draw a 2x4x4 artillery-style wheel at (x0,y0,z0).
    Circular face in Y-Z plane (visible from the side), axle runs along X.
    Outer ring = tire (dark iron), inner = wooden spokes (light wood), center = hub (brass).
    """
    for dy in range(4):
        for dz in range(4):
            # Circular pattern: corners are air
            if (dy == 0 or dy == 3) and (dz == 0 or dz == 3):
                continue
            for dx in range(2):
                # Outer ring = tire
                if dy == 0 or dy == 3 or dz == 0 or dz == 3:
                    grid[x0 + dx, y0 + dy, z0 + dz] = DARK_IRON
                # Center = hub
                elif (dy == 1 or dy == 2) and (dz == 1 or dz == 2):
                    if dy == 1 and dz == 1 or dy == 2 and dz == 2:
                        grid[x0 + dx, y0 + dy, z0 + dz] = GOLD_BRASS
                    else:
                        grid[x0 + dx, y0 + dy, z0 + dz] = LIGHT_WOOD
                else:
                    grid[x0 + dx, y0 + dy, z0 + dz] = LIGHT_WOOD


def _draw_spare_tire(grid, x0, y0, z0):
    """Draw a 4x4x2 spare tire mounted vertically on the rear."""
    for dx in range(4):
        for dy in range(4):
            if (dx == 0 or dx == 3) and (dy == 0 or dy == 3):
                continue
            for dz in range(2):
                if dx == 0 or dx == 3 or dy == 0 or dy == 3:
                    grid[x0 + dx, y0 + dy, z0 + dz] = DARK_IRON
                else:
                    grid[x0 + dx, y0 + dy, z0 + dz] = LIGHT_WOOD


def generate_touring_car(seed=None):
    """1920s Touring Car (Ford Model T style): open-top, 4/5 seater, artillery wheels.

    Layout (20 wide x 16 tall x 30 deep):
      Front at +Z (high Z), rear at -Z (low Z)

      Y 0-3:   Wheels (4x4x2 each, 4 wheels at corners)
      Y 4:     Chassis frame (dark iron)
      Y 5-6:   Lower body (painted green) + running boards (dark wood)
      Y 6-7:   Seat bases (dark wood = leather)
      Y 8-11:  Seat backrests + dashboard + cabin walls
      Y 11-13: Windshield + roof supports (touring car, top down)
      Y 12-14: Roof support pillars at corners only

      Z 0-1:   Rear deck + spare tire
      Z 2-8:   Rear seat area (2 passengers)
      Z 9-10:  Floor gap between seats
      Z 11-16: Front seat area (driver + 1 passenger)
      Z 17-18: Dashboard + windshield
      Z 19-25: Engine hood (narrower than cabin)
      Z 26:    Radiator grille
      Z 27-28: Headlights + front bumper
      Z 29:    Front bumper extension
    """
    if seed is not None:
        np.random.seed(seed)

    w, h, d = VEH_W, VEH_H, VEH_D
    grid = np.zeros((w, h, d), dtype=np.uint16)

    # === Wheels (Y=0-3) ===
    # Wheels are 2 thick in X (axle direction), 4 in Y, 4 in Z (along car length)
    # Front wheels at Z=22-25, rear wheels at Z=4-7
    _draw_wheel(grid, 0, 0, 22)    # front-left  (X=0-1, outside left body wall at X=3)
    _draw_wheel(grid, 18, 0, 22)   # front-right (X=18-19, outside right body wall at X=16)
    _draw_wheel(grid, 0, 0, 4)     # rear-left
    _draw_wheel(grid, 18, 0, 4)    # rear-right

    # === Chassis frame (Y=4) ===
    grid[3:17, 4, 2:28] = DARK_IRON

    # === Lower body (Y=5-6) — painted green ===
    grid[3:17, 5, 2:27] = PAINTED_GREEN
    grid[3:17, 6, 2:27] = PAINTED_GREEN

    # === Running boards (Y=5, sides) — dark wood ===
    grid[1:3, 5, 7:22] = DARK_WOOD    # left running board
    grid[17:19, 5, 7:22] = DARK_WOOD  # right running board

    # === Fenders over wheels (Y=4-5, curved mudguards) ===
    # Front fenders
    for z in range(21, 27):
        grid[0:4, 4, z] = PAINTED_GREEN   # left front fender
        grid[16:20, 4, z] = PAINTED_GREEN  # right front fender
    grid[0:4, 5, 22:26] = PAINTED_GREEN
    grid[16:20, 5, 22:26] = PAINTED_GREEN
    # Rear fenders
    for z in range(2, 9):
        grid[0:4, 4, z] = PAINTED_GREEN   # left rear fender
        grid[16:20, 4, z] = PAINTED_GREEN  # right rear fender
    grid[0:4, 5, 3:8] = PAINTED_GREEN
    grid[16:20, 5, 3:8] = PAINTED_GREEN

    # === Cabin walls (sides, Y=5-12) ===
    # Left wall (X=3) and right wall (X=16)
    for y in range(5, 13):
        grid[3, y, 2:22] = PAINTED_GREEN
        grid[16, y, 2:22] = PAINTED_GREEN

    # Door panels (closed by default — base model must not show interior
    # through the doors; an open-door variant/animation is a separate concern)
    # Front doors (Z=10-15)
    for y in range(6, 12):
        grid[3, y, 10:16] = PAINTED_GREEN
        grid[16, y, 10:16] = PAINTED_GREEN
    # Rear doors (Z=3-8)
    for y in range(6, 12):
        grid[3, y, 3:9] = PAINTED_GREEN
        grid[16, y, 3:9] = PAINTED_GREEN

    # Door outline (dark wood seam so the closed panel still reads as a door)
    for z in (9, 16):
        grid[3, 6:12, z] = DARK_WOOD
        grid[16, 6:12, z] = DARK_WOOD
    for z in (2, 21):
        grid[3, 6:12, z] = DARK_WOOD
        grid[16, 6:12, z] = DARK_WOOD

    # Door handles (brass, centered on each door)
    grid[3, 9, 12] = GOLD_BRASS
    grid[16, 9, 12] = GOLD_BRASS
    grid[3, 9, 5] = GOLD_BRASS
    grid[16, 9, 5] = GOLD_BRASS

    # === Rear seat (dark wood = leather upholstery) ===
    # Seat base: X=4-15, Y=6-7, Z=3-8
    grid[4:16, 6:8, 3:9] = DARK_WOOD
    # Seat backrest: X=4-15, Y=8-11, Z=3-4
    grid[4:16, 8:12, 3:5] = DARK_WOOD

    # === Front seat (driver + 1 passenger) ===
    # Seat base: X=4-15, Y=6-7, Z=11-16
    grid[4:16, 6:8, 11:17] = DARK_WOOD
    # Seat backrest: X=4-15, Y=8-11, Z=11-12
    grid[4:16, 8:12, 11:13] = DARK_WOOD

    # Seat divider (simple seam between driver and passenger, both rows)
    for y in range(6, 8):
        grid[9:11, y, 3:9] = BLACK_FABRIC    # rear seat divider
        grid[9:11, y, 11:17] = BLACK_FABRIC  # front seat divider

    # === Dashboard (Y=8-11, Z=17-18) ===
    grid[4:16, 8:12, 17:19] = DARK_WOOD

    # === Steering wheel (driver position, left side X=5-8, Y=10-11, Z=16) ===
    grid[5:9, 10, 16] = DARK_IRON   # steering wheel ring
    grid[6:8, 11, 16] = DARK_IRON   # steering column
    grid[7, 9, 16] = DARK_IRON      # steering column to dashboard

    # === Windshield (Y=11-13, Z=17-18) — glass with wood frame ===
    # Frame
    grid[3:17, 11:14, 17:19] = DARK_WOOD
    # Glass (center, X=5-14)
    grid[5:15, 11:13, 17:19] = WINDOW_GLASS

    # === Engine hood (Y=5-9, Z=19-25) — slightly narrower than cabin ===
    grid[5:15, 5:10, 19:26] = PAINTED_GREEN

    # Hood louvers (vents — dark iron lines on top of hood)
    for z in range(20, 26, 2):
        grid[6:14, 9, z] = DARK_IRON

    # === Radiator grille (Y=5-10, Z=26) ===
    grid[6:14, 5:11, 26] = AGED_METAL
    # Vertical grille bars
    for x in range(6, 15, 2):
        grid[x, 5:11, 26] = DARK_IRON

    # Radiator cap (brass, on top)
    grid[9:11, 10:12, 25:27] = GOLD_BRASS

    # === Headlights (Y=8-9, Z=27) ===
    # Left headlight
    grid[3:6, 8:10, 27:29] = GOLD_BRASS
    grid[4:5, 9, 28] = LAMP_GLOW   # lens
    # Right headlight
    grid[14:17, 8:10, 27:29] = GOLD_BRASS
    grid[15:16, 9, 28] = LAMP_GLOW  # lens

    # === Front bumper (Y=4, Z=27-29) ===
    grid[2:18, 4, 27:30] = DARK_IRON

    # === Rear deck (Y=5-7, Z=0-2) ===
    grid[3:17, 5:8, 0:3] = PAINTED_GREEN

    # === Spare tire (mounted on rear, X=8-11, Y=6-9, Z=0-1) ===
    _draw_spare_tire(grid, 8, 6, 0)

    # === Rear bumper (Y=4, Z=0-1) ===
    grid[2:18, 4, 0:2] = DARK_IRON

    # === Tail light (small, red) ===
    grid[3:5, 7:9, 0] = PAINTED_RED    # left tail light
    grid[15:17, 7:9, 0] = PAINTED_RED  # right tail light

    # === Roof supports (touring car with top down — just pillars) ===
    # Front pillars (at windshield corners)
    grid[3, 12:15, 17] = DARK_WOOD    # front-left
    grid[16, 12:15, 17] = DARK_WOOD   # front-right
    # Rear pillars
    grid[3, 12:15, 3] = DARK_WOOD     # rear-left
    grid[16, 12:15, 3] = DARK_WOOD    # rear-right

    # === Folded canvas top (behind rear seat, rolled up) ===
    grid[5:15, 12:14, 8:10] = WEATHERED_WOOD  # canvas roll (closest to tan fabric)

    # === Floor boards (Y=5, interior floor) ===
    grid[4:16, 5, 3:17] = LIGHT_WOOD

    # === Body trim (brass accent line along the body, Y=7) ===
    grid[3, 7, 2:22] = GOLD_BRASS    # left side trim
    grid[16, 7, 2:22] = GOLD_BRASS   # right side trim

    # === Front grille ornament (Ford-like, small brass piece) ===
    grid[9:11, 11:13, 26] = GOLD_BRASS

    return grid


# Registry
VEHICLE_GENERATORS = {
    "touring_car": generate_touring_car,
}


def generate_vehicle(vehicle_type, seed=None):
    """Generate a voxel vehicle. Returns the numpy grid."""
    gen = VEHICLE_GENERATORS.get(vehicle_type, generate_touring_car)
    return gen(seed=seed)


if __name__ == "__main__":
    import os
    import sys

    # Add the VoxelAssetStudio directory to path for stasset_io
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from stasset_io import save_stasset

    output_dir = os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        "Assets", "StreamingAssets", "voxel_buildings"
    )

    for vtype, gen in VEHICLE_GENERATORS.items():
        grid = gen(seed=42)
        non_air = np.count_nonzero(grid)
        w, h, d = grid.shape
        print(f"  {vtype:20s}  dims={w}x{h}x{d}  solid={non_air:4d}  total={w*h*d}")

        # Export as vehicle_civilian_car_0.stasset (matches VehicleTestSpawner default)
        filename = "vehicle_civilian_car_0.stasset"
        filepath = os.path.join(output_dir, filename)
        save_stasset(filepath, grid)
        print(f"  -> Exported to {filepath}")
