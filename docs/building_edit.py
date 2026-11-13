"""building_edit.py -- surgical, scriptable edits to an editable building/room source JSON.

    python building_edit.py office_complex components
    python building_edit.py office_complex delete-box X0 X1 Y0 Y1 Z0 Z1        # inclusive voxel ranges
    python building_edit.py office_complex fill-box X0 X1 Y0 Y1 Z0 Z1 --mat MATNAME  # add empty cells, painted MATNAME
    python building_edit.py office_complex reflect-x --module X0 X1 Z0 Z1       # mirror everything EXCEPT the module
    python building_edit.py office_complex undo

Every mutating command first copies the source to <name>.prev.json (one-level undo; `undo` swaps it back).
The server auto-rebakes on the next /bake request because the source mtime changes.

reflect-x: voxels outside the module box are mirrored about the module's centre plane (x' = x0 + x1 - x), then
everything is shifted so nothing goes negative. The module's own voxels are untouched (byte-identical), and the new
module offset is printed -- put it in bake_building.DEFAULT_ANCHOR.
"""
import argparse, json, os, shutil, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ITEMS = os.path.normpath(os.path.join(HERE, '..', 'Assets', 'StreamingAssets', 'voxel_items'))


def load(name):
    return json.load(open(os.path.join(ITEMS, name + '.json'), encoding='utf-8'))


def save(name, d):
    src = os.path.join(ITEMS, name + '.json')
    shutil.copyfile(src, os.path.join(ITEMS, name + '.prev.json'))
    with open(src + '.tmp', 'w', encoding='utf-8') as f:
        json.dump(d, f, separators=(',', ':'))
    os.replace(src + '.tmp', src)


def components(d, min_cells=1):
    """4-connected components of the (x,z) footprint -> [(cells, voxels, (x0,x1), (z0,z1), (y0,y1))]."""
    W, H, D = d['dims']
    v = np.asarray(d['voxels'], dtype=np.int32)
    fp = np.zeros((W, D), np.int32)
    np.add.at(fp, (v[:, 0], v[:, 2]), 1)
    occ = fp > 0
    seen = np.zeros_like(occ)
    out = []
    for x, z in np.argwhere(occ):
        if seen[x, z]:
            continue
        st = [(x, z)]; seen[x, z] = True; cells = []
        while st:
            cx, cz = st.pop(); cells.append((cx, cz))
            for nx, nz in ((cx + 1, cz), (cx - 1, cz), (cx, cz + 1), (cx, cz - 1)):
                if 0 <= nx < W and 0 <= nz < D and occ[nx, nz] and not seen[nx, nz]:
                    seen[nx, nz] = True; st.append((nx, nz))
        if len(cells) < min_cells:
            continue
        cx = np.array([c[0] for c in cells]); cz = np.array([c[1] for c in cells])
        sel = (v[:, 0] >= cx.min()) & (v[:, 0] <= cx.max()) & (v[:, 2] >= cz.min()) & (v[:, 2] <= cz.max())
        mask = np.zeros((W, D), bool); mask[cx, cz] = True
        inside = sel & mask[v[:, 0], v[:, 2]]
        ys = v[inside, 1]
        out.append((len(cells), int(inside.sum()), (int(cx.min()), int(cx.max())), (int(cz.min()), int(cz.max())), (int(ys.min()), int(ys.max()))))
    return sorted(out, reverse=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('name'); ap.add_argument('cmd', choices=['components', 'delete-box', 'fill-box', 'reflect-x', 'undo'])
    ap.add_argument('box', nargs='*', type=int)
    ap.add_argument('--mat')
    ap.add_argument('--module', nargs=4, type=int, metavar=('X0', 'X1', 'Z0', 'Z1'))
    a = ap.parse_args()

    if a.cmd == 'undo':
        prev = os.path.join(ITEMS, a.name + '.prev.json')
        if not os.path.isfile(prev):
            sys.exit('no .prev.json to restore')
        src = os.path.join(ITEMS, a.name + '.json')
        tmp = src + '.swap'
        os.replace(src, tmp); os.replace(prev, src); os.replace(tmp, prev)   # swap, so undo is also undoable
        print('restored previous version (the current one is now .prev.json)'); return

    d = load(a.name)
    v = np.asarray(d['voxels'], dtype=np.int32)

    if a.cmd == 'components':
        for i, (cells, vox, xr, zr, yr) in enumerate(components(d)):
            print(f'#{i}: footprint {cells:6d} cells  {vox:8d} voxels  x {xr[0]}..{xr[1]}  z {zr[0]}..{zr[1]}  y {yr[0]}..{yr[1]}')
        return

    if a.cmd == 'delete-box':
        x0, x1, y0, y1, z0, z1 = a.box
        k = (v[:, 0] >= x0) & (v[:, 0] <= x1) & (v[:, 1] >= y0) & (v[:, 1] <= y1) & (v[:, 2] >= z0) & (v[:, 2] <= z1)
        d['voxels'] = v[~k].tolist()
        save(a.name, d)
        print(f'deleted {int(k.sum()):,} voxels; {int((~k).sum()):,} remain'); return

    if a.cmd == 'fill-box':
        x0, x1, y0, y1, z0, z1 = a.box
        names = {m['name']: m['id'] for m in d['materials']}
        if a.mat not in names:
            sys.exit(f"unknown material {a.mat!r} -- known: {sorted(names)}")
        mid = names[a.mat]
        W, H, D = d['dims']
        occ = np.zeros((W, H, D), bool)
        occ[v[:, 0], v[:, 1], v[:, 2]] = True
        add = np.argwhere(~occ[x0:x1 + 1, y0:y1 + 1, z0:z1 + 1])
        add += [x0, y0, z0]
        d['voxels'] = np.vstack([v, np.hstack([add, np.full((len(add), 1), mid)])]).tolist() if len(add) else d['voxels']
        save(a.name, d)
        print(f'filled {len(add):,} voxels ({a.mat}={mid}) into x{x0}..{x1} y{y0}..{y1} z{z0}..{z1}'); return

    if a.cmd == 'reflect-x':
        mx0, mx1, mz0, mz1 = a.module
        W = d['dims'][0]
        inmod = (v[:, 0] >= mx0) & (v[:, 0] <= mx1) & (v[:, 2] >= mz0) & (v[:, 2] <= mz1)
        out = v.copy()
        out[~inmod, 0] = (mx0 + mx1) - v[~inmod, 0]
        shift = max(0, -int(out[:, 0].min()))
        out[:, 0] += shift
        if int(out[:, 0].max()) >= W:
            sys.exit(f'result needs x up to {int(out[:, 0].max())} but the volume is {W} wide -- enlarge dims first')
        d['voxels'] = out.tolist()
        save(a.name, d)
        print(f'reflected {int((~inmod).sum()):,} voxels about x={(mx0 + mx1) / 2 + .5}; shifted +{shift}; '
              f'module now at x {mx0 + shift}..{mx1 + shift}  ->  set DEFAULT_ANCHOR to ({mx0 + shift}, 0, {mz0})')


if __name__ == '__main__':
    main()
