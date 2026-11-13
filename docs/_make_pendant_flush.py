"""commission_pendant_flush.json — stemless hall variant of commission_pendant.
Same dome + collar, no stem/canopy: a flat brass mounting plate sits directly on the
beam underside (better clearance under the lowered ceiling). Bottom-anchored, vs .012.
Fixture = dome(12) + collar(3) + plate(2) = 17 cells = .204m — top face flush on beam."""
import json, math

VS = 0.012
CX = 19.5                          # item center axis
R_SHADE, DOME_H = 18, 11           # dome: dia .43m, 11 tall
COLLAR_R, COLLAR_H = 6, 3
PLATE_H = 2
TOP = DOME_H + COLLAR_H + PLATE_H  # last occupied y index = TOP-1
H = 48; W = D = 40                 # dims kept identical to commission_pendant — anchor conventions unchanged

MATS = [
    (1, 'Enamel Green',   '#1e4d33'),
    (2, 'Enamel Hi',      '#2e6b45'),
    (3, 'Enamel Lo',      '#163826'),
    (4, 'Milk Glass',     '#e4ddc0'),
    (5, 'Brass',          '#8a6d2a'),
    (6, 'Brass Dark',     '#54401c'),
    (7, 'Bulb Glow',      '#ffe9b0'),
]

vox = []
def put(x, y, z, m): vox.append((x, y, z, m))

def dome_r(dy):
    return COLLAR_R + (R_SHADE - COLLAR_R) * math.sqrt(max(0.0, 1.0 - (dy / DOME_H) ** 2))

# ── shade: identical to _make_pendant.py (2-thick shell, enamel out / milk-glass underside)
shell = set()
for y in range(DOME_H + 1):
    ro = dome_r(y)
    for x in range(W):
        for z in range(D):
            d = math.hypot(x - CX, z - CX)
            ri = min(ro - 2, dome_r(y + 1) - 1)
            if ri < d <= ro:
                shell.add((x, y, z))
for (x, y, z) in sorted(shell, key=lambda t: (t[1], -math.hypot(t[0] - CX, t[2] - CX))):
    d = math.hypot(x - CX, z - CX)
    outer = d > dome_r(y) - 1
    tread = (x, y + 1, z) not in shell
    if y == 0 and d > dome_r(0) - 2.2:
        put(x, y, z, 3)
    elif outer or tread:
        put(x, y, z, 2 if (min(x, W - 1 - x) + min(z, D - 1 - z) + y) % 7 == 0 else 1)
    else:
        put(x, y, z, 4)
# ── collar: brass crown on the dome's top row
for y in range(DOME_H + 1, DOME_H + 1 + COLLAR_H):
    for x in range(W):
        for z in range(D):
            if math.hypot(x - CX, z - CX) <= COLLAR_R - (0 if y < DOME_H + COLLAR_H else 1):
                put(x, y, z, 5)
# ── mounting plate: flat brass disc bolts the fixture straight to the beam underside
for y in range(DOME_H + 1 + COLLAR_H, TOP):
    for x in range(W):
        for z in range(D):
            if math.hypot(x - CX, z - CX) <= 5.5:
                put(x, y, z, 5 if y == TOP - 1 else 6)   # dark brass rim, bright top face (buried in beam anyway)
# ── drop rod + bulb: same conduit down inside the dome
for y in range(3, DOME_H + 1):
    for dx, dz in ((0, 0), (0, 1), (1, 0), (1, 1)):
        put(int(CX) + dx, y, int(CX) + dz, 6)
for y in (0, 1, 2):
    for dx, dz in ((0, 0), (0, 1), (1, 0), (1, 1)):
        put(int(CX) + dx, y, int(CX) + dz, 7)

d = {
    'name': 'commission_pendant_flush', 'dims': [W, H, D], 'assetType': 'prop', 'voxelSize': VS,
    'materials': [{'id': i, 'name': n, 'hex': h,
                   'r': int(h[1:3], 16), 'g': int(h[3:5], 16), 'b': int(h[5:7], 16),
                   'category': 'furniture'} for i, n, h in MATS],
    'voxels': [list(v) for v in vox],
    'groups': [], 'regions': [], 'itemParts': [], 'attachmentPoints': [],
    'format': 'steelcity_voxel', 'version': 1, 'bgColor': '#101418',
}
p = '../Assets/StreamingAssets/voxel_items/commission_pendant_flush.json'
json.dump(d, open(p, 'w'))

cells = set(v[:3] for v in vox)
seen = set(); stack = [next(iter(cells))]
while stack:
    c = stack.pop()
    if c in seen: continue
    seen.add(c)
    x, y, z = c
    stack += [n for n in ((x+1,y,z),(x-1,y,z),(x,y+1,z),(x,y-1,z),(x,y,z+1),(x,y,z-1)) if n in cells and n not in seen]
print('wrote %s — %d voxels, %d materials, disconnected: %d, height: %d cells (%.3fm)' % (p, len(vox), len(MATS), len(cells - seen), TOP, TOP * VS))
