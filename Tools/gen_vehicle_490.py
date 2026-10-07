# gen_vehicle_490.py — 1920 Chevrolet Series 490 Touring Car voxel generator
#
# Produces two artifacts:
#   1) Assets/StreamingAssets/voxel_vehicles/vehicle_490_touring.stasset  (runtime binary)
#   2) VoxelAssetStudio/JSON Models In Progress/vehicle_490_touring.json  (editor JSON)
#
# Scale: 0.01 m/voxel — the uniform voxel lattice (characters author AND render
# at 0.01 in Unity; vehicles match). One vehicle voxel == one character voxel
# in-world, 1:1 in the editor preview.
# Proportions matched to period reference photos: a standing character's head
# reaches the TOP of the windshield; seated occupants sit deep in the tub with
# head+shoulders well above the beltline.
#
# Axes: +X = vehicle right, +Y = up, +Z = forward (grille at high z).
# Ground plane = y 0 (wheel bottoms touch y 0).
#
# NOTE: no .groups sidecar is emitted — its presence auto-enables the character
# pose kernel, which would pose-mangle a rigid vehicle. Group/region/part data
# lives in the JSON for the editor until a vehicle pose mode exists.

import os
import sys
import math
import json

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'VoxelAssetStudio'))
from stasset_io import save_stasset

W, H, D = 70, 75, 155
VOXEL_SIZE = 0.01

# --- material palette ids (vehicle block 140-147 + shared) ---
MAT_PAINT    = 140  # Vehicle Paint   — body color, per-instance remappable
MAT_ACCENT   = 141  # Vehicle Accent  — fenders, running boards, canvas top
MAT_CHROME   = 142  # Vehicle Chrome  — grille surround, lamps, trim
MAT_TIRE     = 143  # Vehicle Tire
MAT_LIGHT    = 144  # Vehicle Light   — headlamp lenses
MAT_TAIL     = 145  # Vehicle Tail Light
MAT_INTERIOR = 146  # Vehicle Interior — seats, dash, floor
MAT_GLASS    = 147  # Vehicle Glass   — windshield
MAT_WOOD     = 107  # Light Wood      — wheel spokes/fellies (char palette, shared)
MAT_BLACK    = 126  # Black Fabric    — radiator shell face
MAT_BRASS    = 123  # Gold/Brass      — lamp bezels, radiator trim

# --- regions (Paint/Decal layer) ---
REG_PAINT, REG_ACCENT, REG_CHROME, REG_GLASS = 0, 1, 2, 3
REG_INTERIOR, REG_TIRES, REG_LIGHTS = 4, 5, 6

# --- groups (Car Parts layer) ---
GID_BODY, GID_WFL, GID_WFR, GID_WRL, GID_WRR = 0, 1, 2, 3, 4
GID_DOOR_L, GID_DOOR_R, GID_HOOD, GID_TRUNK = 5, 6, 7, 8

CX = W // 2  # 35 — lateral center

voxels   = {}   # (x,y,z) -> matID
groups   = {}   # (x,y,z) -> gid
regions  = {}   # (x,y,z) -> rid
parts    = {}   # part name -> [voxels]


def put(x, y, z, mid, gid=0, rid=REG_PAINT):
    x, y, z = int(round(x)), int(round(y)), int(round(z))
    if not (0 <= x < W and 0 <= y < H and 0 <= z < D):
        return
    voxels[(x, y, z)] = mid
    if gid:
        groups[(x, y, z)] = gid
    if rid:
        regions[(x, y, z)] = rid


def box(x0, y0, z0, x1, y1, z1, mid, gid=0, rid=REG_PAINT, shell=False):
    """Fill [x0,x1]x[y0,y1]x[z0,z1]. shell=True => faces only (hollow)."""
    for x in range(x0, x1 + 1):
        for y in range(y0, y1 + 1):
            for z in range(z0, z1 + 1):
                if shell and x0 < x < x1 and y0 < y < y1 and z0 < z < z1:
                    continue
                put(x, y, z, mid, gid, rid)


def clear(x, y, z):
    for m in (voxels, groups, regions):
        m.pop((x, y, z), None)


def disc_yz(cy, cz, r_out, r_in, x0, x1, mid, gid=0, rid=REG_ACCENT):
    """Ring disc in Y-Z plane (wheel/fender plane), thickness along X."""
    for y in range(cy - r_out - 1, cy + r_out + 2):
        for z in range(cz - r_out - 1, cz + r_out + 2):
            r = math.hypot(y - cy, z - cz)
            if r_in <= r <= r_out:
                for x in range(x0, x1 + 1):
                    put(x, y, z, mid, gid, rid)


def arc_yz(cy, cz, r_in, r_out, a0, a1, x0, x1, mid, rid=REG_ACCENT):
    """Partial ring (fender arc) in Y-Z plane. Angles in degrees, 0=+z."""
    for y in range(cy - r_out - 1, cy + r_out + 2):
        for z in range(cz - r_out - 1, cz + r_out + 2):
            dy, dz = y - cy, z - cz
            r = math.hypot(dy, dz)
            if not (r_in <= r <= r_out):
                continue
            ang = math.degrees(math.atan2(dy, dz)) % 360.0
            if a0 <= ang <= a1 or (a0 > a1 and (ang >= a0 or ang <= a1)):
                for x in range(x0, x1 + 1):
                    put(x, y, z, mid, 0, rid)


def disc_xy(cx, cy, r, z0, z1, mid, gid=0, rid=REG_CHROME):
    """Solid disc in X-Y plane facing +Z (headlamps), depth along Z."""
    for x in range(cx - r - 1, cx + r + 2):
        for y in range(cy - r - 1, cy + r + 2):
            if math.hypot(x - cx, y - cy) <= r:
                for z in range(z0, z1 + 1):
                    put(x, y, z, mid, gid, rid)


def attach(name, x, y, z, r=1, mid=10):
    """Paint a small blob as an attachment point source (importable)."""
    pts = []
    for dx in range(-r, r + 1):
        for dy in range(-r, r + 1):
            for dz in range(-r, r + 1):
                if dx*dx + dy*dy + dz*dz <= r*r:
                    px, py, pz = x + dx, y + dy, z + dz
                    if 0 <= px < W and 0 <= py < H and 0 <= pz < D:
                        put(px, py, pz, mid)
                        parts.setdefault(name, []).append((px, py, pz))


# ============================ WHEELS ============================
WHEEL_R = 14                       # 0.42 m dia
AXLE_Y = 14
Z_REAR, Z_FRONT = 34, 132          # 1.47 m wheelbase
XL0, XL1 = 3, 7                    # left wheel x-span (5 thick)
XR0, XR1 = W - 8, W - 4            # right wheel x-span

def wheel(x0, x1, cz, gid):
    """Wood-spoke wheel: rubber rim ring, wood fellies, 12 spokes, hub."""
    disc_yz(AXLE_Y, cz, WHEEL_R, 11, x0, x1, MAT_TIRE, gid, REG_TIRES)
    disc_yz(AXLE_Y, cz, 10, 8, x0 + 1, x1 - 1, MAT_WOOD, gid, REG_ACCENT)
    for s in range(12):
        a = math.radians(s * 30)
        for r in range(3, 9):
            y = AXLE_Y + int(round(r * math.sin(a)))
            z = cz + int(round(r * math.cos(a)))
            for x in range(x0 + 1, x1):
                put(x, y, z, MAT_WOOD, gid, REG_ACCENT)
                put(x, y + 1, z, MAT_WOOD, gid, REG_ACCENT)
    disc_yz(AXLE_Y, cz, 3, 0, x0, x1, MAT_CHROME, gid, REG_CHROME)

for cz, gx in ((Z_FRONT, (GID_WFL, GID_WFR)), (Z_REAR, (GID_WRL, GID_WRR))):
    wheel(XL0, XL1, cz, gx[0])
    wheel(XR0, XR1, cz, gx[1])

# Axle bars + frame rails + leaf springs
box(XL1 + 1, AXLE_Y - 1, Z_FRONT - 1, XR0 - 1, AXLE_Y + 1, Z_FRONT + 1, MAT_ACCENT, 0, REG_ACCENT)
box(XL1 + 1, AXLE_Y - 1, Z_REAR - 1, XR0 - 1, AXLE_Y + 1, Z_REAR + 1, MAT_ACCENT, 0, REG_ACCENT)
box(16, 15, 10, 20, 18, 140, MAT_ACCENT, 0, REG_ACCENT)
box(50, 15, 10, 54, 18, 140, MAT_ACCENT, 0, REG_ACCENT)
for x0, x1 in ((3, 7), (W - 8, W - 4)):
    box(x0, 20, Z_FRONT - 9, x1, 21, Z_FRONT + 9, MAT_ACCENT, 0, REG_ACCENT)
    box(x0, 20, Z_REAR - 9, x1, 21, Z_REAR + 9, MAT_ACCENT, 0, REG_ACCENT)

# ============================ FENDERS ============================
for x0, x1 in ((2, 8), (W - 9, W - 3)):
    arc_yz(AXLE_Y, Z_REAR, 15, 17, 20, 200, x0, x1, MAT_ACCENT)    # rear arc
    arc_yz(AXLE_Y, Z_FRONT, 15, 17, -20, 160, x0, x1, MAT_ACCENT)  # front arc
    # front sweep tail — descends toward the bumper
    for i in range(0, 5):
        t = i / 4.0
        z = Z_FRONT + 14 + i                       # 146 -> 150
        y = int(round((AXLE_Y + 15) - t * 12))     # 29 -> 17
        box(x0, y - 1, z, x1, y, z + 1, MAT_ACCENT, 0, REG_ACCENT)

# Running boards between fenders
box(5, 10, Z_REAR + 18, 9, 12, Z_FRONT - 18, MAT_ACCENT, 0, REG_ACCENT)
box(W - 10, 10, Z_REAR + 18, W - 6, 12, Z_FRONT - 18, MAT_ACCENT, 0, REG_ACCENT)

# ============================ BODY SHELL ============================
FLOOR = 18
BELT = 46
BOXL, BOXR = 9, 61                  # body side walls

# cabin floor (footwell)
box(BOXL, FLOOR - 1, 40, BOXR, FLOOR, 100, MAT_INTERIOR, 0, REG_INTERIOR)
# rockers below doors
box(BOXL, FLOOR, 40, 11, FLOOR + 3, 100, MAT_PAINT)
box(BOXR - 2, FLOOR, 40, BOXR, FLOOR + 3, 100, MAT_PAINT)
# rear quarter panels — tapered tail
for z in range(5, 42):
    taper = max(0, int(round((39 - z) * 0.30)))
    inset = min(taper, 8)
    box(BOXL + inset, FLOOR, z, 11 + inset, BELT, z, MAT_PAINT)
    box(BOXR - 2 - inset, FLOOR, z, BOXR - inset, BELT, z, MAT_PAINT)
# rear transom — crowned top
for x in range(11, 59):
    dx = x - CX
    top = BELT - int(round(dx * dx / 120.0))
    box(x, FLOOR, 5, x, top, 6, MAT_PAINT)
# B-post + cowl post
box(BOXL, FLOOR, 66, 11, BELT, 72, MAT_PAINT)
box(BOXR - 2, FLOOR, 66, BOXR, BELT, 72, MAT_PAINT)
box(BOXL, FLOOR, 94, 11, BELT, 100, MAT_PAINT)
box(BOXR - 2, FLOOR, 94, BOXR, BELT, 100, MAT_PAINT)
# beltline chrome strip on fixed sides
box(BOXL, BELT, 40, 11, BELT + 1, 100, MAT_CHROME, 0, REG_CHROME)
box(BOXR - 2, BELT, 40, BOXR, BELT + 1, 100, MAT_CHROME, 0, REG_CHROME)

# Doors — rear z 42-66, front z 72-94
for x0, x1, gd in ((BOXL, 11, GID_DOOR_L), (BOXR - 2, BOXR, GID_DOOR_R)):
    box(x0, FLOOR + 3, 42, x1, BELT, 66, MAT_PAINT, gd, REG_PAINT)
    box(x0, FLOOR + 3, 72, x1, BELT, 94, MAT_PAINT, gd, REG_PAINT)
    hx = x0 if x0 < CX else x1
    box(hx, BELT - 5, 61, hx, BELT - 4, 62, MAT_CHROME, gd, REG_CHROME)
    box(hx, BELT - 5, 89, hx, BELT - 4, 90, MAT_CHROME, gd, REG_CHROME)

# ============================ COWL / HOOD / GRILLE ============================
# Cowl — firewall rising from beltline to windshield base
for z in range(100, 106):
    t = (z - 100) / 5.0
    top = int(round(46 + t * 3))
    box(BOXL, FLOOR, z, 11, top, z, MAT_PAINT)
    box(BOXR - 2, FLOOR, z, BOXR, top, z, MAT_PAINT)
    box(11, top - 1, z, 58, top, z, MAT_PAINT)             # cowl deck
# dash (interior face)
box(13, 28, 100, 57, 46, 102, MAT_INTERIOR, 0, REG_INTERIOR)

# Hood — gid 7 for articulation; louvered side walls
box(20, 34, 103, 47, 36, 132, MAT_PAINT, GID_HOOD, REG_PAINT)      # hood top
for x0, x1 in ((20, 22), (45, 47)):
    box(x0, 20, 105, x1, 34, 132, MAT_PAINT, GID_HOOD, REG_PAINT)  # side wall
    for z in range(110, 129):                                       # louvers
        for y in range(25, 31, 3):
            clear(x0, y, z); clear(x0 + 1, y, z)
            clear(x1, y, z); clear(x1 - 1, y, z)
box(20, 20, 103, 47, 36, 105, MAT_PAINT, GID_HOOD, REG_PAINT)      # rear cap
box(20, 20, 131, 47, 36, 133, MAT_PAINT, GID_HOOD, REG_PAINT)      # front cap
# cowl shoulders — shell boxes flanking the hood at the scuttle
box(11, 20, 104, 20, 35, 106, MAT_PAINT, 0, REG_PAINT, shell=True)
box(47, 20, 104, 58, 35, 106, MAT_PAINT, 0, REG_PAINT, shell=True)

# Radiator / grille — upright face, slightly taller than the hood
box(21, 15, 143, 46, 39, 146, MAT_BLACK, 0, REG_ACCENT)            # core
box(19, 37, 142, 47, 41, 147, MAT_CHROME, 0, REG_CHROME)           # top shell
box(19, 15, 142, 21, 39, 147, MAT_CHROME, 0, REG_CHROME)           # side trims
box(45, 15, 142, 47, 39, 147, MAT_CHROME, 0, REG_CHROME)
box(33, 39, 144, 37, 42, 146, MAT_BRASS, 0, REG_CHROME)            # motometer cap

# Headlights — brass drums flanking the grille, lenses forward
for hx in (16, 50):
    disc_xy(hx, 38, 3, 138, 141, MAT_BRASS, 0, REG_CHROME)
    disc_xy(hx, 38, 2, 142, 143, MAT_LIGHT, 0, REG_LIGHTS)
    box(hx - 1, 34, 139, hx + 1, 36, 141, MAT_BRASS, 0, REG_CHROME)  # post
box(14, 34, 138, 52, 36, 140, MAT_CHROME, 0, REG_CHROME)           # support bar

# Front bumper + taillight + exhaust
box(14, 14, 148, 52, 16, 151, MAT_CHROME, 0, REG_CHROME)
box(9, 27, 4, 12, 30, 6, MAT_TAIL, 0, REG_LIGHTS)
box(38, 10, 2, 42, 12, 4, MAT_ACCENT, 0, REG_ACCENT)

# ============================ WINDSHIELD + TOP ============================
# Two-pane windshield, slight rake — top edge ~ Vinny's standing head
for y in range(47, 63):
    z = 100 - int(round((y - 47) * 0.08))
    for x in range(12, 58):
        if 33 <= x <= 35:
            put(x, y, z, MAT_CHROME, 0, REG_CHROME)
        else:
            put(x, y, z, MAT_GLASS, 0, REG_GLASS)
box(11, 46, 99, 12, 64, 101, MAT_CHROME, 0, REG_CHROME)            # frame L
box(57, 46, 99, 58, 64, 101, MAT_CHROME, 0, REG_CHROME)            # frame R
box(11, 61, 98, 58, 64, 101, MAT_CHROME, 0, REG_CHROME)            # top rail

# Canvas top — corner posts + crowned slab
for px in (9, 58):
    box(px, BELT, 44, px + 1, 66, 47, MAT_ACCENT, 0, REG_ACCENT)   # rear posts
    box(px, BELT, 97, px + 1, 66, 100, MAT_ACCENT, 0, REG_ACCENT)  # front posts
for z in range(42, 102):
    for x in range(10, 60):
        dx = (x - CX) / 25.0
        dz = (z - 72) / 30.0
        crown = 71 - int(round((dx * dx + dz * dz) * 5))
        for y in range(crown - 1, crown + 1):
            put(x, y, z, MAT_ACCENT, 0, REG_ACCENT)

# ============================ INTERIOR ============================
# Front bench — sits deep in the tub (LHD driver at low x)
box(13, 24, 80, 56, 26, 90, MAT_INTERIOR, 0, REG_INTERIOR)       # cushion
box(13, 26, 87, 56, 48, 90, MAT_INTERIOR, 0, REG_INTERIOR)       # backrest
# Rear bench
box(13, 24, 50, 56, 26, 60, MAT_INTERIOR, 0, REG_INTERIOR)
box(13, 26, 57, 56, 48, 60, MAT_INTERIOR, 0, REG_INTERIOR)
# Steering column + wheel (driver x ~19)
for i in range(0, 13):
    put(19, 27 + i, 93 + int(i * 0.15), MAT_INTERIOR, 0, REG_INTERIOR)
    put(20, 27 + i, 93 + int(i * 0.15), MAT_INTERIOR, 0, REG_INTERIOR)
wx, wy, wz = 19, 40, 96
for i in range(360):
    a = math.radians(i)
    y = wy + int(round(4 * math.sin(a)))
    z = wz + int(round(4 * math.cos(a) * 0.4))
    for x in range(wx - 1, wx + 2):
        put(x, y, z, MAT_INTERIOR, 0, REG_INTERIOR)
# rear-view mirror
box(30, 57, 99, 38, 60, 101, MAT_CHROME, 0, REG_CHROME)

# ============================ ATTACHMENT POINTS ============================
attach('axle_fl', XL1 + 1, AXLE_Y, Z_FRONT)
attach('axle_fr', XR0 - 1, AXLE_Y, Z_FRONT)
attach('axle_rl', XL1 + 1, AXLE_Y, Z_REAR)
attach('axle_rr', XR0 - 1, AXLE_Y, Z_REAR)
attach('door_hinge_l', 10, 44, 93)       # front-left door, front edge
attach('door_hinge_r', 60, 44, 93)
attach('seat_driver', 19, 28, 85)        # pelvis anchor, cushion-top +~2
attach('seat_passenger', 51, 28, 85)
attach('entry_l', 7, 13, 87)             # running board by front door
attach('entry_r', 62, 13, 87)
attach('exhaust_tip', 40, 11, 3)
attach('hood_hinge', CX, 36, 104)

# ============================ OUTPUT ============================
def centroid(pts):
    n = len(pts)
    return [round(sum(p[0] for p in pts) / n, 1),
            round(sum(p[1] for p in pts) / n, 1),
            round(sum(p[2] for p in pts) / n, 1)]

import numpy as np
grid = np.zeros((W, H, D), dtype=np.uint16)
for (x, y, z), mid in voxels.items():
    grid[x, y, z] = mid

os.makedirs('Assets/StreamingAssets/voxel_vehicles', exist_ok=True)
save_stasset('Assets/StreamingAssets/voxel_vehicles/vehicle_490_touring.stasset',
             grid,
             skeleton={'attachments': [
                 {'name': n, 'position': centroid(p)} for n, p in parts.items()]})

doc = {
    'format': 'steelcity_vehicle',
    'assetType': 'vehicle',
    'name': 'vehicle_490_touring',
    'voxelSize': VOXEL_SIZE,
    'dims': [W, H, D],
    'voxels': [[x, y, z, mid] for (x, y, z), mid in sorted(voxels.items())],
    'groups': [[x, y, z, g] for (x, y, z), g in sorted(groups.items())],
    'regions': [[x, y, z, r] for (x, y, z), r in sorted(regions.items())],
    'itemParts': {n: p for n, p in parts.items()},
    'attachmentPoints': {n: centroid(p) for n, p in parts.items()},
    'itemPartDefs': [
        {'id': 'axle_fl', 'label': 'Axle Front-L'}, {'id': 'axle_fr', 'label': 'Axle Front-R'},
        {'id': 'axle_rl', 'label': 'Axle Rear-L'}, {'id': 'axle_rr', 'label': 'Axle Rear-R'},
        {'id': 'door_hinge_l', 'label': 'Door Hinge L'}, {'id': 'door_hinge_r', 'label': 'Door Hinge R'},
        {'id': 'hood_hinge', 'label': 'Hood Hinge'},
        {'id': 'seat_driver', 'label': 'Seat Driver'}, {'id': 'seat_passenger', 'label': 'Seat Passenger'},
        {'id': 'entry_l', 'label': 'Entry L'}, {'id': 'entry_r', 'label': 'Entry R'},
        {'id': 'exhaust_tip', 'label': 'Exhaust Tip'},
    ],
}
# Convention mirrors characters: WIP copy lives in JSON Models In Progress/,
# the FINAL JSON also ships in StreamingAssets/voxel_vehicles/ — runtime reads
# the .stasset for geometry, and the JSON carries groups/regions/attachment
# metadata in a form future vehicle systems (seating, doors) can parse directly.
json_text = json.dumps(doc)
for out in (os.path.join('VoxelAssetStudio', 'JSON Models In Progress', 'vehicle_490_touring.json'),
            os.path.join('Assets', 'StreamingAssets', 'voxel_vehicles', 'vehicle_490_touring.json')):
    with open(out, 'w', encoding='utf-8') as f:
        f.write(json_text)

bbox = lambda ax: (min(v[ax] for v in voxels), max(v[ax] for v in voxels))
print(f'vehicle_490_touring: {len(voxels):,} filled voxels')
print(f'  fill bbox x{bbox(0)} y{bbox(1)} z{bbox(2)}')
print(f'  world: {(bbox(0)[1]-bbox(0)[0]+1)*VOXEL_SIZE:.2f}m W x '
      f'{(bbox(1)[1]-bbox(1)[0]+1)*VOXEL_SIZE:.2f}m H x '
      f'{(bbox(2)[1]-bbox(2)[0]+1)*VOXEL_SIZE:.2f}m L')
print(f'  groups: {len(groups):,}  regions: {len(regions):,}  parts: {len(parts)}')
print('  wrote Assets/StreamingAssets/voxel_vehicles/vehicle_490_touring.stasset')
print('  wrote VoxelAssetStudio/JSON Models In Progress/vehicle_490_touring.json')
