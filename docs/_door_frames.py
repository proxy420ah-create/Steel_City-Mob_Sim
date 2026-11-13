"""_door_frames.py — finish the two doorway walls + install simple wood door frames.

The two gapped wall segments (office door at slab z147-149, the second opening at
slab z250-252) get IDENTICAL door frames under the door-bay contract:

    raw reveal x107-130 (24v) -> walnut2 jamb posts line x107/x130 + head jamb
    across y30 -> clear opening x108-129 x y3-29 = 22v (.66m) x 27v (.81m),
    seating the .615 x .802 leaf. Masonry lintel band fills y31-38 above the
    head (no transom — wall continues). A 1v-proud walnut architrave casing on
    each wall face covers every fill seam — "no blank places".

    office door (z147-149): margins already exist — chips x107/x130 to open the
        raw reveal, adds lintel band + frame.
    z250 doorway (z250-252): raw 50v gap — fills x94-106 + x131-143 with the
        live wall_brick recipe (same per-layer seed the reskin pass uses, so the
        fill lands cell-identical to the adjacent wall face), then the same
        frame. Intentionally left open — no leaf, frame only.

    python _door_frames.py C:/path/to/voxel_complexLow.json          # apply
    python _door_frames.py C:/path/to/voxel_complexLow.json --dry    # report only
"""
import argparse, json, os, sys
import numpy as np
import building_skin as S

RAW_X0, RAW_X1 = 107, 130            # raw reveal — 24v (.72m)
POST_Y0, POST_Y1 = 3, 29             # posts run the opening height — 27v (.81m)
HEAD_Y = 30                          # head jamb — leaf top ~y29.7
LINT_Y0, LINT_Y1 = 31, 38            # lintel band — wall over the head, no transom
WALL_Y0 = 3                          # reskin's wall zone base (row index = y-3)
WALL_H = 60                          # reskin's wall_brick height (rows 0..59)
FRAME_MAT = 'walnut2'                # #2f2015 — matches the door file's Walnut Dark family

# Each doorway: wall slab z-range, the two outer face planes, the masonry fills
# needed to leave the raw reveal, and margin cells to chip (office only).
DOORS = [
    dict(name='office', slab=range(147, 150), faces=(146, 150),
         fill=(), chip=(107, 130)),
    dict(name='z250',   slab=range(250, 253), faces=(249, 253),
         fill=((94, 106), (131, 143)), chip=()),
]


def seed_for(z):                     # x-running face seed — same formula _reskin_walls uses
    return 200 + (z % 97) * 7


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('name'); ap.add_argument('--dry', action='store_true')
    a = ap.parse_args()
    d = json.load(open(a.name, encoding='utf8'))
    name2id = {m['name']: m['id'] for m in d['materials']}
    if FRAME_MAT not in name2id:
        sys.exit(f'palette is missing {FRAME_MAT!r} — add it or pick another frame wood')
    W, H, D = d['dims']
    v = np.asarray(d['voxels'], dtype=np.int32)
    full = np.zeros((W, H, D), np.int16)
    full[v[:, 0], v[:, 1], v[:, 2]] = v[:, 3]

    # one wall_brick sheet per slab layer (seed per z — the recipe is a pure
    # function of (y-3, x, layer seed) so fills land identical to the wall face)
    bricks = {}
    def brick_mat(z):
        if z not in bricks:
            bricks[z] = S.wall_brick(WALL_H, max(W, D), seed=seed_for(z))
        return bricks[z]

    out = full.copy()
    frame_id = name2id[FRAME_MAT]
    n_fill = n_chip = n_frame = n_lint = n_stop = 0

    for door in DOORS:
        slab, (f0, f1) = door['slab'], door['faces']

        # 1. chip — open the raw reveal (office's existing margins narrow it)
        for x in door['chip']:
            for y in range(WALL_Y0, LINT_Y1 + 1):
                for z in slab:
                    if out[x, y, z]: out[x, y, z] = 0; n_chip += 1

        # 2. masonry fills — wall pattern continues across the old gap
        for (x0, x1) in door['fill']:
            for x in range(x0, x1 + 1):
                for y in range(WALL_Y0, LINT_Y1 + 1):
                    for z in slab:
                        if not out[x, y, z]:
                            out[x, y, z] = name2id[brick_mat(z)[min(y - WALL_Y0, WALL_H - 1), x]]; n_fill += 1

        # 3. lintel band — wall over the head (both doors, reveal was open to beam)
        for x in range(RAW_X0, RAW_X1 + 1):
            for y in range(LINT_Y0, LINT_Y1 + 1):
                for z in slab:
                    if not out[x, y, z]:
                        out[x, y, z] = name2id[brick_mat(z)[min(y - WALL_Y0, WALL_H - 1), x]]; n_lint += 1

        # 4. frame — jamb posts + head jamb line the raw reveal
        for y in range(POST_Y0, POST_Y1 + 1):
            for z in slab:
                out[RAW_X0, y, z] = frame_id; out[RAW_X1, y, z] = frame_id; n_frame += 2
        for x in range(RAW_X0, RAW_X1 + 1):
            for z in slab:
                out[x, HEAD_Y, z] = frame_id; n_frame += 1

        # 5. architrave casing on both wall faces — 2v legs + wider head cap,
        #    1v proud (covers the fill/reveal seams; add cells, never overwrite)
        for fz in (f0, f1):
            for x in (RAW_X0 - 2, RAW_X0 - 1, RAW_X1 + 1, RAW_X1 + 2):          # legs x105-106 / x131-132
                for y in range(WALL_Y0, HEAD_Y + 1):
                    if not out[x, y, fz]: out[x, y, fz] = frame_id; n_frame += 1
            for x in range(RAW_X0 - 3, RAW_X1 + 4):                            # head cap x104-133, y31-33
                for y in range(HEAD_Y + 1, HEAD_Y + 4):
                    if not out[x, y, fz]: out[x, y, fz] = frame_id; n_frame += 1

        # 6. door stop — 1v strips on the hall-side (f1) reveal edges, x108/x129.
        #    The .03 side reveal is exactly one cell: the stop covers it on the
        #    face the leaf shows to, the leaf rests ~.004 off its back, and the
        #    swing (toward -z/office) never meets it. Add cells, never overwrite.
        stop_z = f1 - 1                                                          # hall-side slab layer
        for x in (RAW_X0 + 1, RAW_X1 - 1):
            for y in range(POST_Y0, POST_Y1 + 1):
                if not out[x, y, stop_z]: out[x, y, stop_z] = frame_id; n_stop += 1

    print(f'fill {n_fill} wall cells | lintel {n_lint} | chipped {n_chip} margin cells | frame {n_frame} {FRAME_MAT} cells | stops {n_stop}')
    if a.dry:
        print('dry run — nothing written'); return

    xs, ys, zs = np.nonzero(out)
    d['voxels'] = np.stack([xs, ys, zs, out[xs, ys, zs]], axis=1).astype(int).tolist()
    bak = a.name[:-5] + '.prev.json'
    with open(bak, 'w', encoding='utf8', newline='') as f:
        json.dump(json.load(open(a.name, encoding='utf8')), f)
    with open(a.name, 'w', encoding='utf8', newline='') as f:
        json.dump(d, f)
    print(f'backup -> {os.path.basename(bak)}\nwrote {a.name}')


if __name__ == '__main__':
    main()
