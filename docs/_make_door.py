"""commission_door.json — 1920s office door LEAF, authored procedurally.

THE DOOR-BAY CONTRACT: this file is the reusable stamp — the swinging leaf and
its hinge-side trim ONLY. Everything structural (jambs, head, lintel, reveal,
casing, glazing) is authored in the building model itself so each storefront
can be custom while every door bay accepts any leaf. The bay contract: leaf
.60 x .80 (plus hinge knuckles .015 proud of the hinge edge), pivot at the
leaf's west-edge bottom, arc 1.75 rad. Variations may restyle the leaf — the
envelope, pivot, and arc are fixed.

THE LEAF IS BARBER-SCALE — the authority is the barber_shop WIP door verified
against the reference character in the studio: leaf .60 x .80 (kick .45 +
glass upper .25 + top rail .10, center stile). The .96m cast fills 120% of it —
chibi doors are deliberately smaller than people.

Leaf voxels are built in their own map so itemParts tagging is airtight; the
loader parents them to a pivot group — `door.pivot` declares the pivot voxel,
`door.arc` the swing in rad (positive = far edge toward office/-z),
front:'z+' = the hall face (moldings, knob plate, kick plate all z-high).

vs .0075 — lawdesk grain. Right-handed truth (no truthView stamp): generated,
not sculpted against the Unity preview; the commission renders it as-authored.
Each part is shelled (surface voxels only, 26-neighbour test — a 6-conn test
culls slab faces buried under applied reliefs and floats the reliefs).
"""
import json

VS = 0.0075
W, H, D = 80, 107, 12                   # wraps the LEAF exactly (.60 x .8025 x .09) — dims are the leaf envelope, not the doorway canvas

MATS = [
    (1,  'Walnut Dark',   '#33200f'),   # jambs, casing post, transom bar
    (2,  'Mahogany',      '#4a2a15'),   # leaf slab + center stile
    (3,  'Mahogany Light','#6b4222'),   # leaf panels + moldings
    (4,  'Frosted Glass', '#aec6cf'),   # name carries 'Glass' — voxEmit splits it transparent
    (5,  'Brass',         '#8a6d2a'),   # knob
    (6,  'Brass Dark',    '#54401c'),   # kick plate, backplate, hinge knuckles
]

leaf, fr = {}, {}                       # (x,y,z) -> mat, per part — last write wins
def box(dst, x0, x1, y0, y1, z0, z1, m):          # inclusive ranges
    for x in range(x0, x1 + 1):
        for y in range(y0, y1 + 1):
            for z in range(z0, z1 + 1):
                if 0 <= x < W and 0 <= y < H and 0 <= z < D: dst[(x, y, z)] = m
def clear(dst, x0, x1, y0, y1, z0, z1):
    for x in range(x0, x1 + 1):
        for y in range(y0, y1 + 1):
            for z in range(z0, z1 + 1):
                dst.pop((x, y, z), None)

# ── the leaf fills the file edge-to-edge: x0..79 — dims wrap the leaf so the
#    stamp carries its own envelope (no dead canvas). Pivot stays at the leaf's
#    west-edge bottom x0 → identical world hinge as the old x60 in the 200-canvas.
XO = -7
# ── frame REMOVED — the door-bay contract makes all structure the building's
#    job (_door_frames.py authors jamb posts, head jamb, lintel and casing in
#    office_complex.json). Original unit layout kept below for reference: ──
#box(fr, XO + 0, XO + 6, 0, H - 1, 0, D - 1, 1)      # STRIPPED: west jamb
#box(fr, XO + 87, XO + 92, 0, 227, 0, D - 1, 1)      # STRIPPED: east jamb / casing post
#box(fr, XO + 0, XO + 92, 228, 235, 0, D - 1, 1)     # STRIPPED: head over the door unit

# ── leaf: x60..139 y0..106 z3..8 = .60m x .80m — THE BARBER LEAF, verbatim scale.
#    Composition mirrors it too: solid kick ~.45, glazed upper ~.25 split by a
#    center stile, top rail ~.10 ────────────────────────────────────────────
box(leaf, XO + 7, XO + 86, 0, 106, 3, 8, 2)                    # slab
# twin panes SYMMETRIC about the stile (x98..101, slab centre x99.5): west
# composition mirrored to the east; pane bottoms stop at y63 — a proper lock
# rail below the glass where the knob backplate mounts (clears the pane).
clear(leaf, XO + 11, XO + 44, 64, 92, 3, 8); clear(leaf, XO + 49, XO + 82, 64, 92, 3, 8)   # two pane cutouts
box(leaf, XO + 10, XO + 45, 63, 93, 5, 6, 4); box(leaf, XO + 48, XO + 83, 63, 93, 5, 6, 4) # panes — edges buried in the sash
box(leaf, XO + 45, XO + 48, 0, 106, 3, 8, 1)                   # center stile (barber's DD) — overwrites glass seam
box(leaf, XO + 11, XO + 43, 18, 54, 8, 9, 3); box(leaf, XO + 50, XO + 82, 18, 54, 8, 9, 3) # twin raised kick panels, hall face
box(leaf, XO + 14, XO + 40, 21, 51, 9, 9, 2); box(leaf, XO + 53, XO + 79, 21, 51, 9, 9, 2) # inset fields, hall face
box(leaf, XO + 12, XO + 81, 6, 13, 9, 10, 6)                   # brass kick plate, hall face
box(leaf, XO + 9, XO + 84, 103, 106, 8, 9, 3)                  # top-rail lip, hall face
box(leaf, XO + 78, XO + 84, 55, 62, 9, 10, 6)                  # knob backplate — y~58 = .44m, char hand height
box(leaf, XO + 79, XO + 84, 56, 61, 10, 12, 5)                 # knob — proud of the plate
# face detail is SYMMETRIC — every hall-face relief mirrors across the slab
# midplane onto the office face (z9->z2, z9..10->z1..2, z10..12->z0..2):
box(leaf, XO + 11, XO + 43, 18, 54, 2, 3, 3); box(leaf, XO + 50, XO + 82, 18, 54, 2, 3, 3) # twin raised kick panels, office face
box(leaf, XO + 14, XO + 40, 21, 51, 2, 2, 2); box(leaf, XO + 53, XO + 79, 21, 51, 2, 2, 2) # inset fields, office face
box(leaf, XO + 12, XO + 81, 6, 13, 1, 2, 6)                    # brass kick plate, office face
box(leaf, XO + 9, XO + 84, 103, 106, 2, 3, 3)                  # top-rail lip, office face
box(leaf, XO + 78, XO + 84, 55, 62, 1, 2, 6)                   # knob backplate, office face
box(leaf, XO + 79, XO + 84, 56, 61, 0, 2, 5)                   # knob, office face
# hinge knuckles REMOVED — the invisible-hinge leaf: nothing protrudes the
# pivot plane (x60), so the swing can never clip the jamb. Hardware reads
# inside the building's frame instead.
#box(leaf, XO + 5, XO + 8, 28, 40, 2, 3, 6); box(leaf, XO + 5, XO + 8, 80, 92, 2, 3, 6)  # STRIPPED: hinge knuckles, office face

# ── transom REMOVED — transom glazing belongs to the site-built storefront in
#    the building (Storefront Glass cells), not the reusable leaf stamp.
#    Original layout kept below for reference: ──
#box(fr, XO + 7, XO + 86, 107, 114, 2, 9, 1)         # STRIPPED: transom bar (leaf swings under it)
#box(fr, XO + 7, XO + 86, 115, 227, 5, 6, 4)        # STRIPPED: transom glass
#box(fr, XO + 45, XO + 48, 115, 227, 4, 7, 1)       # STRIPPED: center stile continues — unbroken vertical
#box(fr, XO + 7, XO + 86, 160, 164, 4, 7, 1)        # STRIPPED: mid rail — four panes total

# dims ARE the leaf envelope (80x107x12). The leaf was already centred in the
# old 200-wide canvas, so the same KIT pos renders it at the identical world
# spot — no re-tuning, and now every variant inherits a tight wrap.

# ── shell each part by its own occupancy: a voxel survives if a 26-neighbour
#    is absent within its part (6-conn culls faces buried under reliefs) ─────
NB6 = ((1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1))
NB26 = tuple((dx, dy, dz) for dx in (-1,0,1) for dy in (-1,0,1) for dz in (-1,0,1) if (dx,dy,dz) != (0,0,0))
def shell(part):
    return {c: m for c, m in part.items()
            if any((c[0]+dx, c[1]+dy, c[2]+dz) not in part for dx, dy, dz in NB26)}
leaf, fr = shell(leaf), shell(fr)

cells = leaf                                   # door-bay contract: leaf only — no frame part
vox = sorted(cells.items())
d = {
    'name': 'commission_door', 'dims': [W, H, D], 'assetType': 'prop', 'voxelSize': VS,
    'materials': [{'id': i, 'name': n, 'hex': h,
                   'r': int(h[1:3], 16), 'g': int(h[3:5], 16), 'b': int(h[5:7], 16),
                   'category': 'furniture'} for i, n, h in MATS],
    'voxels': [list(c) + [m] for c, m in vox],
    # door contract — the hinge lives IN THE FILE: pivot = the leaf's west-edge
    # bottom voxel, arc = swing in rad (positive = far edge toward office/-z),
    # front:'z+' = the hall face. Loader parents leaf meshes to a pivot group.
    'door': {'hinge': 'x-', 'pivot': [XO + 7, 0, 5], 'arc': 1.75, 'front': 'z+'},
    'itemPartDefs': [
        {'id': 0, 'name': 'Body / Erase', 'key': 'body', 'color': '#888888',
         'desc': "Unassigned — painting with this clears a voxel's point tag (eraser)"},
        {'id': 1, 'name': 'Leaf', 'key': 'leaf', 'color': '#00ddff',
         'desc': 'Swinging leaf — panes, knob, kick plate ride with it on the hinge pivot'},
    ],
    'itemParts': {'%d,%d,%d' % c: 1 for c in sorted(leaf)},
    'groups': [], 'regions': [], 'attachmentPoints': [], 'pivots': {},
    'format': 'steelcity_voxel', 'version': 1, 'bgColor': '#101418',
}
p = '../Assets/StreamingAssets/voxel_items/commission_door.json'
json.dump(d, open(p, 'w'))

# connectivity — the leaf must be one component (knuckles ride on its hinge edge)
def components(sub):
    rest = set(sub); n = 0
    while rest:
        n += 1; stack = [rest.pop()]
        while stack:
            x, y, z = stack.pop()
            for nb in NB6:
                c = (x+nb[0], y+nb[1], z+nb[2])
                if c in rest: rest.remove(c); stack.append(c)
    return n
print('wrote %s — %d voxels (leaf only), %d materials' % (p, len(vox), len(MATS)))
print('components: leaf=%d' % components(set(leaf)))
print('extent x %d..%d  y %d..%d  z %d..%d' % (
    min(c[0] for c in cells), max(c[0] for c in cells),
    min(c[1] for c in cells), max(c[1] for c in cells),
    min(c[2] for c in cells), max(c[2] for c in cells)))
