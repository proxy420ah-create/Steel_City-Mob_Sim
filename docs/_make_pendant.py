"""commission_pendant.json — 1920s green-enamel dome pendant, authored procedurally.
Bottom-anchored (KIT loader: x/z centered on dims, y = bottom face). vs .012 (1.2cm)."""
import json, math

VS = 0.012
CX = 19.5                          # item center axis
R_SHADE, DOME_H = 18, 11           # dome: dia .43m, 11 tall
COLLAR_R, COLLAR_H = 6, 3
STEM_R = 1.2
CANOPY_H = 4
STEM_TOP = 47
H = 48; W = D = 40

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
    """Outer shell radius dy rows above the rim — flares COLLAR_R -> R_SHADE so the top row meets the collar."""
    return COLLAR_R + (R_SHADE - COLLAR_R) * math.sqrt(max(0.0, 1.0 - (dy / DOME_H) ** 2))

# ── shade: 2-thick shell. enamel where the outer skin OR the tread top is exposed;
#    milk glass only where a voxel sits directly above (the true underside).
shell = set()
for y in range(DOME_H + 1):
    ro = dome_r(y)
    for x in range(W):
        for z in range(D):
            d = math.hypot(x - CX, z - CX)
            ri = min(ro - 2, dome_r(y + 1) - 1)          # ring reaches under the ring above, so steep rows stay face-connected
            if ri < d <= ro:
                shell.add((x, y, z))
for (x, y, z) in sorted(shell, key=lambda t: (t[1], -math.hypot(t[0] - CX, t[2] - CX))):
    d = math.hypot(x - CX, z - CX)
    outer = d > dome_r(y) - 1
    tread = (x, y + 1, z) not in shell
    if y == 0 and d > dome_r(0) - 2.2:                # rim edge band
        put(x, y, z, 3)
    elif outer or tread:                            # visible from outside/above -> enamel
        put(x, y, z, 2 if (min(x, W - 1 - x) + min(z, D - 1 - z) + y) % 7 == 0 else 1)   # keyed on folded coords -> mirror-symmetric speckle
    else:                                           # true underside -> milk glass
        put(x, y, z, 4)

# ── collar: brass crown directly on the dome's top row (no gap)
for y in range(DOME_H + 1, DOME_H + 1 + COLLAR_H):
    for x in range(W):
        for z in range(D):
            if math.hypot(x - CX, z - CX) <= COLLAR_R - (0 if y < DOME_H + COLLAR_H else 1):
                put(x, y, z, 5)
# ── stem: thin brass rod collar -> canopy
for y in range(DOME_H + 1 + COLLAR_H, STEM_TOP - CANOPY_H + 1):
    for dx in (-1, 0, 1):
        for dz in (-1, 0, 1):
            if math.hypot(dx - 0.5, dz - 0.5) <= STEM_R:
                put(int(CX) + dx, y, int(CX) + dz, 5)
# ── canopy: small brass cone at the ceiling
for i in range(CANOPY_H):
    r = 5.5 - i * 1.2
    y = STEM_TOP - CANOPY_H + 1 + i
    for x in range(W):
        for z in range(D):
            if math.hypot(x - CX, z - CX) <= r:
                put(x, y, z, 5)
# ── drop rod + bulb: one connected conduit from the collar down inside the dome
for y in range(3, DOME_H + 1):
    for dx, dz in ((0, 0), (0, 1), (1, 0), (1, 1)):
        put(int(CX) + dx, y, int(CX) + dz, 6)
for y in (0, 1, 2):                                  # bulb — centered on the axis like the stem
    for dx, dz in ((0, 0), (0, 1), (1, 0), (1, 1)):
        put(int(CX) + dx, y, int(CX) + dz, 7)

d = {
    'name': 'commission_pendant', 'dims': [W, H, D], 'assetType': 'prop', 'voxelSize': VS,
    'materials': [{'id': i, 'name': n, 'hex': h,
                   'r': int(h[1:3], 16), 'g': int(h[3:5], 16), 'b': int(h[5:7], 16),
                   'category': 'furniture'} for i, n, h in MATS],
    'voxels': [list(v) for v in vox],
    'groups': [], 'regions': [], 'itemParts': [], 'attachmentPoints': [],
    'format': 'steelcity_voxel', 'version': 1, 'bgColor': '#101418',
}
p = '../Assets/StreamingAssets/voxel_items/commission_pendant.json'
json.dump(d, open(p, 'w'))

# connectivity check — every voxel must reach the canopy (6-neighbour flood fill)
cells = set(v[:3] for v in vox)
seen = set(); stack = [next(iter(cells))]
while stack:
    c = stack.pop()
    if c in seen: continue
    seen.add(c)
    x, y, z = c
    stack += [n for n in ((x+1,y,z),(x-1,y,z),(x,y+1,z),(x,y-1,z),(x,y,z+1),(x,y,z-1)) if n in cells and n not in seen]
n = len(cells - seen)
print('wrote %s — %d voxels, %d materials, disconnected: %d' % (p, len(vox), len(MATS), n))

# symmetry check — mirror about both axes and the diagonal
m = {v[:3]: v[3] for v in vox}
def sym(f): return sum(1 for (x, y, z), c in m.items() if m.get(f(x, y, z)) != c)
print('asymmetric voxels  mirror-X: %d  mirror-Z: %d  diagonal: %d' % (
    sym(lambda x, y, z: (W - 1 - x, y, z)), sym(lambda x, y, z: (x, y, D - 1 - z)), sym(lambda x, y, z: (z, y, x))))
