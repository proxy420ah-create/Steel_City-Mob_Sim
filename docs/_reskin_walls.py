"""_reskin_walls.py — repaint EXISTING wall cells with the current building_skin wall recipe.

Reskin can't re-run on a skinned file (it only sees raw WALL_MATS names), so this re-implements just the
wall pass on skin-material cells: same plane-key/seed logic reskin uses, so each wall plane keeps its
identity, but cells are repainted with the live wall_brick() recipe (chibi grain: wainscot .39m, .09m
courses, .18m bricks). Floors, ceilings, felt — untouched.

    python _reskin_walls.py office_complex            # repaint + save (auto .prev.json backup)
    python _reskin_walls.py office_complex --dry      # report only
    python _reskin_walls.py C:/path/to/file.json      # same, on any file path (backup beside it)
"""
import argparse, json, os, sys
import numpy as np
import building_skin as S
import building_edit as BE

WALL_Y0, WALL_Y1 = 3, 62                       # reskin's wall zone
SKIN_WALL = {'base', 'wain', 'wainHi', 'wainLo', 'wainPanel', 'rail',
             'mortar', 'mortarDk'} | {'brk%d' % i for i in range(1, 8)}


def load(src):
    if src.endswith('.json') or os.sep in src or '/' in src:
        return json.load(open(src, encoding='utf8'))
    return BE.load(src)


def save(src, d):
    if src.endswith('.json') or os.sep in src or '/' in src:
        bak = src[:-5] + '.prev.json'
        with open(bak, 'w', encoding='utf8', newline='') as f:
            json.dump(json.load(open(src, encoding='utf8')), f)
        with open(src, 'w', encoding='utf8', newline='') as f:
            json.dump(d, f)
        print('backup -> %s' % os.path.basename(bak))
    else:
        BE.save(src, d)


def runs(row):
    out, a = [], 0
    while a < len(row):
        if row[a]:
            e = a
            while e < len(row) and row[e]: e += 1
            out.append((a, e - 1)); a = e
        else: a += 1
    return out


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('name'); ap.add_argument('--dry', action='store_true')
    a = ap.parse_args()
    d = load(a.name)
    names = {m['id']: m['name'] for m in d['materials']}
    name2id = {m['name']: m['id'] for m in d['materials']}
    v = np.asarray(d['voxels'], dtype=np.int32)
    W, H, D = d['dims']
    wall_ids = {i for i, n in names.items() if n in SKIN_WALL}
    if not wall_ids:
        sys.exit('no skin-named wall cells found — is the file skinned?')

    full = np.zeros((W, H, D), np.int16)
    full[v[:, 0], v[:, 1], v[:, 2]] = v[:, 3]
    wmask = np.isin(full, list(wall_ids))
    wmask[:, :WALL_Y0, :] = False                                # floors never paint as wall
    wm2d = wmask[:, WALL_Y0:WALL_Y1 + 1, :].any(axis=1)          # wall columns (x,z) — detected in the wall zone

    # Per-CELL plane assignment — the column-level classifier breaks where a perpendicular
    # wall crosses behind a slab (the cross-run wins the length vote and the face paints
    # with constant-u → flat seam bands). Instead: a cell that has wall neighbours along
    # BOTH z faces runs along x (u = x); exposed in z = the face is x-running. A cell with
    # x neighbours and an exposed z-face also reads x. Only when the axis is ambiguous
    # (outside corner or buried interior) do the same-y run lengths arbitrate. Seed is
    # keyed per FACE coordinate (cell z for x-planes, cell x for z-planes) so each visible
    # face keeps a coherent tone set even at crossings.
    runx3 = np.zeros((W, H, D), np.int16); runz3 = np.zeros((W, H, D), np.int16)
    for y in range(WALL_Y0, H):
        for z in range(D):
            for a_, b_ in runs(wmask[:, y, z]):
                runx3[a_:b_ + 1, y, z] = b_ - a_ + 1
        for x in range(W):
            for a_, b_ in runs(wmask[x, y, :]):
                runz3[x, y, a_:b_ + 1] = b_ - a_ + 1

    cache, n, planes = {}, 0, set()
    out = full.copy()
    for x, z in np.argwhere(wm2d):
        for y in range(WALL_Y0, H):
            if not wmask[x, y, z]: continue
            buried_z = z > 0 and wmask[x, y, z - 1] and z + 1 < D and wmask[x, y, z + 1]
            buried_x = x > 0 and wmask[x - 1, y, z] and x + 1 < W and wmask[x + 1, y, z]
            if buried_z != buried_x:        # exactly one axis buried → that axis runs the plane
                ax_ = buried_x              # neighbours on ±x = an x-running wall → u = x
            else:                           # corner or interior → the longer run wins
                ax_ = runx3[x, y, z] >= runz3[x, y, z]
            key = ('x', z) if ax_ else ('z', x); planes.add(key)
            if key not in cache:
                cache[key] = S.wall_brick(WALL_Y1 - WALL_Y0 + 1, max(W, D),
                                          seed=200 + (key[1] % 97) * 7 + (3 if key[0] == 'z' else 0))
            u = x if ax_ else z
            out[x, y, z] = name2id[cache[key][min(y - WALL_Y0, WALL_Y1 - WALL_Y0), u]]; n += 1   # above-zone cells clamp to the top course

    changed = int((out != full).sum())
    print('repainted %d wall cells across %d planes — %d cells changed material' % (n, len(planes), changed))
    if a.dry:
        print('dry run — nothing written'); return
    xs, ys, zs = np.nonzero(full)                                # keep the ORIGINAL cell set — repaint never adds cells
    d['voxels'] = np.stack([xs, ys, zs, out[xs, ys, zs]], axis=1).astype(int).tolist()
    save(a.name, d)
    print('wrote %s' % a.name)


if __name__ == '__main__':
    main()
