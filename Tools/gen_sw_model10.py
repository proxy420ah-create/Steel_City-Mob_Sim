#!/usr/bin/env python3
"""
Generate S&W Model 10 revolver voxel model for Steel City.

Rebuilt at 0.005m/voxel (2x density vs the original 0.01m version) so the
revolver reads as a revolver — the original lacked a hammer, trigger,
trigger guard, a distinct cylinder, and a shaped grip.

Orientation (WEAPON_ITEM_MODEL_STANDARD.md):
    +X = muzzle direction (forward), lying flat on ground plane
    +Y = side profile height, bottom at Y=0
    +Z = thickness (cylinder is the widest part)

Dimensions: 48 x 26 x 10 voxels = 24cm x 13cm x 5cm
    Matches a real Model 10 4" service revolver (~23.5 x 13.5 x 3.7cm).

Materials:
    109 Dark Iron   — barrel, frame, hammer, trigger guard, backstrap
    110 Aged Metal  — cylinder, ejector rod
    123 Gold/Brass  — front sight blade, trigger
    106 Dark Wood   — grip stocks

Pre-seeds attachment points (itemParts) so the editor doesn't need them
painted by hand: grip_right (1), grip_left (2), muzzle (3).

Usage:
    python gen_sw_model10.py [--out SW_Model_10.json] [--preview]
"""

import json
import sys
import shutil
import os
from collections import defaultdict

# ---------------- model constants ----------------

VOXEL_SIZE = 0.005
DIMS = [48, 26, 10]

M_IRON = 109     # Dark Iron — frame, barrel, hammer, guard, backstrap
M_STEEL = 110    # Aged Metal — cylinder, ejector rod
M_BRASS = 123    # Gold/Brass — front sight, trigger
M_WOOD = 106     # Dark Wood — grip stocks

# Anatomy layout (X axis: 0 = grip rear, 47 = muzzle tip)
CYL_X0, CYL_X1 = 20, 29        # cylinder length along bore axis (~4.6cm)
CYL_CY, CYL_CZ, CYL_R = 14, 4.5, 4.2   # disc center/radius in YZ (~3.7cm dia)

BAR_X0, BAR_X1 = 28, 47        # barrel span
BAR_CY, BAR_CZ, BAR_R = 20, 4.5, 1.5   # barrel centerline/radius (~1.3cm OD)

# ---------------- voxel collector ----------------

voxels = {}   # (x,y,z) -> mid

def V(x, y, z, mid):
    if 0 <= x < DIMS[0] and 0 <= y < DIMS[1] and 0 <= z < DIMS[2]:
        voxels[(x, y, z)] = mid

def box(x0, x1, y0, y1, z0, z1, mid):
    for x in range(x0, x1 + 1):
        for y in range(y0, y1 + 1):
            for z in range(z0, z1 + 1):
                V(x, y, z, mid)

def disc_yz(x0, x1, cy, cz, r, mid):
    """Cylinder whose axis is X: circular cross-section in the Y-Z plane."""
    for x in range(x0, x1 + 1):
        for y in range(int(cy - r), int(cy + r) + 1):
            for z in range(int(cz - r), int(cz + r) + 1):
                if (y - cy) ** 2 + (z - cz) ** 2 <= r * r:
                    V(x, y, z, mid)

def oct_barrel(x0, x1, cy, cz, r, mid):
    """Round barrel along X with octagonal cross-section."""
    for x in range(x0, x1 + 1):
        for y in range(int(cy - r), int(cy + r) + 1):
            for z in range(int(cz - r), int(cz + r) + 1):
                dy, dz = abs(y - cy), abs(z - cz)
                if dy + dz <= r * 1.35 and max(dy, dz) <= r:
                    V(x, y, z, mid)

# ---------------- part builders ----------------

def build_cylinder():
    # 6-shot cylinder — the widest part of the gun (Z 0-9 edge-to-edge)
    disc_yz(CYL_X0, CYL_X1, CYL_CY, CYL_CZ, CYL_R, M_STEEL)

def build_frame():
    # Recoil shield (behind cylinder)
    box(15, CYL_X0 - 1, 9, 18, 2, 7, M_IRON)
    # Frame bottom (under cylinder)
    box(CYL_X0, CYL_X1, 8, 9, 2, 7, M_IRON)
    # Front junction (cylinder -> barrel)
    box(CYL_X1, 31, 14, 19, 2, 7, M_IRON)
    # Topstrap (over cylinder, connects frame to barrel)
    box(14, 31, 19, 20, 3, 6, M_IRON)
    # Rear sight notch: groove milled in topstrap rear — remove a row
    for x in range(15, 19):
        for z in (3, 4, 5, 6):
            voxels.pop((x, 20, z), None)
    # Frame web between trigger guard rear and grip front
    box(14, 15, 4, 8, 3, 6, M_IRON)

def build_barrel():
    oct_barrel(BAR_X0, BAR_X1, BAR_CY, BAR_CZ, BAR_R, M_IRON)
    # Top rib (sighting plane along the top of the barrel)
    box(30, BAR_X1, BAR_CY + 2, BAR_CY + 2, 4, 5, M_IRON)
    # Ejector rod housing — slim tube suspended below the barrel with an
    # air gap (reads as the classic Model 10 "two tubes" profile)
    box(30, BAR_X1 - 1, 15, 16, 3, 6, M_IRON)
    box(BAR_X1 - 1, BAR_X1, 15, 16, 4, 5, M_STEEL)

def build_sights():
    # Front sight blade at muzzle — brass marks the "forward" end
    box(44, 45, BAR_CY + 3, BAR_CY + 3, 4, 5, M_BRASS)

def build_hammer():
    # Cocked hammer spur rising behind the topstrap, angled rearward
    box(13, 15, 21, 21, 3, 6, M_IRON)
    box(12, 14, 22, 22, 3, 6, M_IRON)
    box(12, 13, 23, 23, 4, 5, M_IRON)

def build_trigger_guard():
    # D-loop outline below the frame
    box(15, 20, 8, 8, 3, 6, M_IRON)   # top edge (under frame bottom)
    box(20, 21, 6, 7, 3, 6, M_IRON)   # front curve
    box(16, 19, 4, 5, 3, 6, M_IRON)   # bottom
    box(15, 15, 6, 7, 3, 6, M_IRON)   # rear edge

def build_trigger():
    box(17, 18, 6, 8, 4, 5, M_BRASS)

def build_grip():
    # Service grip: angled ~110 degrees from bore, rounded butt.
    # Back strap slopes more steeply than the front strap.
    for y in range(0, 10):
        x_front = round(9 + 0.6 * y)   # front strap: y0->9, y9->14
        x_back = round(4 + 0.9 * y)    # back strap:  y0->4, y9->12
        # Round-butt: clip the bottom two rows' corners
        if y == 0:
            x_back += 2
            x_front -= 1
        elif y == 1:
            x_back += 1
        for x in range(x_back, x_front + 1):
            for z in range(2, 8):
                # Exposed metal backstrap on the rear silhouette edge
                mid = M_IRON if (x == x_back and 3 <= z <= 6) else M_WOOD
                V(x, y, z, mid)

# ---------------- attachment points ----------------
# Keys must reference voxels that exist in the model.

# Named attachment points — the Unity-facing format per
# WEAPON_ATTACHMENT_SYSTEM.md. Both JSON fields derive from this table:
#   attachmentPoints = named {x,y,z} points (runtime alignment)
#   itemParts        = "x,y,z" -> partId voxel map (editor paint round-trip)
ATTACH_POINTS = {
    # outer faces of the grip at palm height
    'grip_right': (round(4 + 0.9 * 5), 5, 7),   # right side face (high Z)
    'grip_left':  (round(4 + 0.9 * 5), 5, 2),   # left side face (low Z)
    # center of the muzzle face
    'muzzle':     (BAR_X1, BAR_CY, 4),
}
ATTACH_IDS = {'grip_right': 1, 'grip_left': 2, 'muzzle': 3}

def build_attachment_points():
    return {name: {'x': p[0], 'y': p[1], 'z': p[2]}
            for name, p in ATTACH_POINTS.items()}

def build_item_parts():
    return {f"{p[0]},{p[1]},{p[2]}": ATTACH_IDS[name]
            for name, p in ATTACH_POINTS.items()}

# ---------------- output ----------------

def collect_materials(src_path):
    """Reuse the palette block from the existing file if present."""
    try:
        with open(src_path, 'r', encoding='utf-8') as f:
            return json.load(f).get('materials', [])
    except (OSError, json.JSONDecodeError):
        return []

def ascii_preview():
    print(f"\nSide profile (X -> muzzle right, Y up) — {len(voxels)} voxels")
    xs = [p[0] for p in voxels]; ys = [p[1] for p in voxels]
    for y in range(max(ys), min(ys) - 1, -1):
        row = ''
        for x in range(min(xs), max(xs) + 1):
            zs = [p for p in voxels if p[0] == x and p[1] == y]
            row += 'I' if zs and voxels[zs[0]] == M_IRON else \
                   'S' if zs and voxels[zs[0]] == M_STEEL else \
                   'B' if zs and voxels[zs[0]] == M_BRASS else \
                   'W' if zs else ' '
        print(f'y={y:2d} |{row}|')
    print('I=iron S=steel B=brass W=wood\n')

def main():
    out = 'SW_Model_10.json'
    preview = '--preview' in sys.argv
    for i, a in enumerate(sys.argv):
        if a == '--out' and i + 1 < len(sys.argv):
            out = sys.argv[i + 1]

    build_cylinder()
    build_frame()
    build_barrel()
    build_sights()
    build_hammer()
    build_trigger_guard()
    build_trigger()
    build_grip()

    if preview or '--preview' in sys.argv:
        ascii_preview()

    data = {
        'format': 'steelcity_item',
        'version': 1,
        'name': 'SW_Model_10',
        'assetType': 'prop',
        'voxelSize': VOXEL_SIZE,
        'dims': DIMS,
        'materials': collect_materials('Assets/StreamingAssets/voxel_items/SW_Model_10.json')
                   or collect_materials(out),
        'voxels': [[x, y, z, m] for (x, y, z), m in sorted(voxels.items())],
        'attachmentPoints': build_attachment_points(),
        'itemParts': build_item_parts(),
    }

    if os.path.exists(out) and '--no-backup' not in sys.argv:
        shutil.copy2(out, out.replace('.json', '.original.json'))
        print(f'Backed up existing -> {out.replace(".json", ".original.json")}')

    with open(out, 'w', encoding='utf-8') as f:
        json.dump(data, f, indent=1)
    print(f'Wrote {out}: {len(voxels)} voxels, dims {DIMS}, voxelSize {VOXEL_SIZE}')

if __name__ == '__main__':
    main()
