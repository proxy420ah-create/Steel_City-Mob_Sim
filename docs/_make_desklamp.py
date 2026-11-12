"""commission_desklamp.json — 1920s Bradley & Hubbard style desk lamp, authored procedurally.
Weighted octagonal cast base -> brass baluster column -> neck knuckle -> under-slung
green half-capsule shade (emeralite profile) + pull chain. Bottom-anchored (KIT loader:
x/z centered on dims, y = bottom face). vs .005 — item/decor grain.
Shade cantilevers +X off a rear-underside bracket; the second flank mirrors via rotY:Math.PI."""
import json, math

VS = 0.005
W, H, D = 40, 54, 28                    # .20m x .27m x .14m
CX, CZ = 13, 13.5                       # column axis (arm sweeps toward +X)

MATS = [
    (1, 'Cast Iron',     '#221a10'),    # weighted base
    (2, 'Brass',         '#8a6d2a'),
    (3, 'Brass Dark',    '#54401c'),
    (4, 'Brass Hi',      '#b08a3e'),
    (5, 'Enamel Green',  '#1e4d33'),    # slag glass = the pendant enamel set
    (6, 'Enamel Hi',     '#2e6b45'),
    (7, 'Enamel Lo',     '#163826'),
    (8, 'Milk Glass',    '#e4ddc0'),
    (9, 'Bulb Glow',     '#ffe9b0'),
]

vox = []
def put(x, y, z, m):
    if 0 <= x < W and 0 <= y < H and 0 <= z < D: vox.append((x, y, z, m))

def disc(cx, cz, y, r, m, ell=1.0):
    for x in range(W):
        for z in range(D):
            if math.hypot(x - cx, (z - cz) / ell) <= r: put(x, y, z, m)

def ring(cx, cz, y, r0, r1, m):          # annulus r0<d<=r1
    for x in range(W):
        for z in range(D):
            d = math.hypot(x - cx, z - cz)
            if r0 < d <= r1: put(x, y, z, m)

def octagon(cx, cz, y, r, m):            # square with clipped corners = cast octagon
    for x in range(W):
        for z in range(D):
            if abs(x - cx) <= r and abs(z - cz) <= r and abs(x - cx) + abs(z - cz) <= r * 1.62:
                put(x, y, z, m)

def ball(cx, cy, cz, r, m):
    for x in range(W):
        for y in range(H):
            for z in range(D):
                if math.dist((x, y, z), (cx, cy, cz)) <= r: put(x, y, z, m)

# ── base: octagonal cast foot, stepped shoulder, brass belt (B&H bases were brass-bound iron)
octagon(CX, CZ, 0, 13, 1)                                    # foot slab .065m
octagon(CX, CZ, 1, 12, 1)
disc(CX, CZ, 1, 4, 1)                                        # heel pad under the axis
ring(CX, CZ, 2, 10, 12, 3); disc(CX, CZ, 2, 10, 1)           # step taper
ring(CX, CZ, 3, 8, 10, 4);  disc(CX, CZ, 3, 8, 1)            # brass belt edge
ring(CX, CZ, 4, 7, 9, 3);   disc(CX, CZ, 4, 7, 1)            # dark fillet
disc(CX, CZ, 5, 7, 1); disc(CX, CZ, 6, 5, 1)                 # shoulder to the socket
disc(CX, CZ, 7, 4, 2)                                        # brass column socket

# ── column: baluster turning — neck, vase swell, collars
for y in range(8, 14): disc(CX, CZ, y, 2.2, 2)               # lower neck
for dy, r in ((14, 3.4), (15, 4.2), (16, 4.2), (17, 3.4)):   # vase swell
    disc(CX, CZ, dy, r, 2)
disc(CX, CZ, 18, 4.6, 4)                                     # hi collar
for y in range(19, 33): disc(CX, CZ, y, 2.2, 2)              # upper neck — the lamp's height lives here
disc(CX, CZ, 33, 3.6, 4); disc(CX, CZ, 34, 3.0, 2)           # crown collar where the arm springs

SX = CX + 15; SY = 35                                       # capsule axis pierce target (shade centres here)

# ── neck: the shade mounts at the base of the neck, not over its crown —
#    a knuckle on the column top, then a strap ramp running forward under the
#    shade's rear third and bolting into the shell at the mouth's rear edge.
ball(CX, 35.2, CZ, 2.6, 3)                                   # knuckle on the crown collar
for x in range(15, 24):                                      # strap ramp: knuckle -> rear underside
    y = round(33.5 - 3.5 * (x - 15) / 8)
    put(x, y, int(CZ), 2); put(x, y - 1, int(CZ), 2)
    put(x, y, int(CZ) + 1, 2); put(x, y - 1, int(CZ) + 1, 2)
# ── shade: half-capsule (emeralite / B&H banker's profile) — a half-pill running
#    along z: cylindrical mid-section + hemispherical end caps, mouth level with
#    the lip rolled under the equator so the shade reads enclosed from the side.
#    Pure glass — no brass linings; the socket mount clamps the crown above.
CAP_R = 6; CAP_S = 7; CAP_A = 0.0             # mouth half-width, cylinder half-length, tilt rad
CA_, SA_ = math.cos(CAP_A), math.sin(CAP_A)
# axis pierce — the crown (u=0, v=CAP_R) tops out at (SX, SY+.5), clean glass
PAX, PAY = SX - CAP_R * SA_, (SY + 0.5) - CAP_R * CA_
def frame(x, y, z):
    dx, dy = x - PAX, y - PAY
    u = dx * CA_ + dy * SA_; v = -dx * SA_ + dy * CA_
    q = max(0.0, abs(z - CZ) - CAP_S)                     # distance past the cylinder's end
    return u, v, math.sqrt(u * u + v * v + q * q), abs(z - CZ)
shell = set()
for x in range(W):
    for y in range(H):
        for z in range(D):
            u, v, dist, adz = frame(x, y, z)
            if v >= -1.6 and CAP_R - 1.7 < dist <= CAP_R: # ~1.7-thick skin; the lip rolls ~1.5 under the equator
                shell.add((x, y, z))
for (x, y, z) in sorted(shell):
    u, v, dist, adz = frame(x, y, z)
    tread = (x, y + 1, z) not in shell                    # up-face exposed -> outer skin
    if dist > CAP_R - 0.9 or tread:                       # outer skin -> slag sectors along the arc
        sect = int(math.atan2(v, u) / (math.pi / 8))      # z-free key — mirror about z=CZ stays exact
        put(x, y, z, 6 if sect % 3 == 1 else (7 if (sect * 5 + int(dist * 2)) % 13 == 0 else 5))
    else:
        put(x, y, z, 8)                                   # interior lining -> milk glass
# drop rod + bulb: conduit runs from the crown down the hollow to a glow that
# pokes just below the mouth plane (bulbs sit proud on these lamps, not hidden)
RX = int(round(PAX))                                       # rod column rides the axis
for y in range(27, 36):                                    # merges into the shell near the crown
    put(RX, y, int(CZ), 3); put(RX, y, int(CZ) + 1, 3)
for x in range(W):
    for y in range(H):
        for z in (int(CZ), int(CZ) + 1):
            u, v, dist, adz = frame(x, y, z)
            if -2.4 <= v <= -0.7 and dist <= 1.9: put(x, y, z, 9)   # glow fills the mouth, sits inside the lip
# pull chain: beads continue the rod below the bulb, dangling free under the shade
for i in range(3):
    put(RX, 26 - i, int(CZ), 4); put(RX, 26 - i, int(CZ) + 1, 4)

d = {
    'name': 'commission_desklamp', 'dims': [W, H, D], 'assetType': 'prop', 'voxelSize': VS,
    'materials': [{'id': i, 'name': n, 'hex': h,
                   'r': int(h[1:3], 16), 'g': int(h[3:5], 16), 'b': int(h[5:7], 16),
                   'category': 'furniture'} for i, n, h in MATS],
    'voxels': [list(v) for v in vox],
    # truthView — declared authoring truth = the left-handed (Unity) render, the
    # studio law for props. Right-handed consumers (thecommission) mirror z at
    # load to honor it; the file itself stays in canonical editor coords.
    'truthView': 'unity',
    'groups': [], 'regions': [], 'itemParts': [], 'attachmentPoints': [],
    'format': 'steelcity_voxel', 'version': 1, 'bgColor': '#101418',
}
p = '../Assets/StreamingAssets/voxel_items/commission_desklamp.json'
json.dump(d, open(p, 'w'))

# connectivity check — every voxel must reach the base (6-neighbour flood fill)
cells = set(v[:3] for v in vox)
seen = set(); stack = [next(iter(cells))]
while stack:
    c = stack.pop()
    if c in seen: continue
    seen.add(c)
    x, y, z = c
    stack += [n for n in ((x+1,y,z),(x-1,y,z),(x,y+1,z),(x,y-1,z),(x,y,z+1),(x,y,z-1)) if n in cells and n not in seen]
# mirror check — the lamp's symmetry plane is z = CZ (13.5 -> z' = 27 - z):
# the arm sweep breaks x-mirror by design, nothing may break z-mirror.
mat_at = {v[:3]: v[3] for v in vox}
mirror = lambda c: (c[0], c[1], 27 - c[2])
asym_pos = [c for c in mat_at if mirror(c) not in mat_at]
asym_mat = [c for c in mat_at if mirror(c) in mat_at and mat_at[mirror(c)] != mat_at[c]]
print('wrote %s — %d voxels, %d materials, disconnected: %d' % (p, len(vox), len(MATS), len(cells - seen)))
print('z-mirror asymmetric: %d cells missing twin, %d material mismatches' % (len(asym_pos), len(asym_mat)))
if asym_pos[:5] or asym_mat[:5]: print('  e.g.', (asym_pos + asym_mat)[:5])
print('extent x %d..%d  y %d..%d  z %d..%d' % (
    min(v[0] for v in vox), max(v[0] for v in vox), min(v[1] for v in vox), max(v[1] for v in vox),
    min(v[2] for v in vox), max(v[2] for v in vox)))
