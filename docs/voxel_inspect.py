"""voxel_inspect.py -- one-shot structural inspection of a voxel asset JSON (see docs/core/VOXEL_INSPECTION_PROTOCOL.md).

    python docs/voxel_inspect.py commission_pendant              # bare name -> Assets/StreamingAssets/voxel_items/<name>.json
    python docs/voxel_inspect.py office_complex --no-sym         # skip symmetry (buildings are not symmetric by design)
    python docs/voxel_inspect.py path/to/file.json --axis 19.5   # symmetry about a custom centre axis (voxel units)

Checks (all numpy / bounded, safe on 1M+ voxels):
  1. summary       dims, voxel size, real-world size, bbox, fill %, materials with counts
  2. integrity     out-of-bounds voxels, duplicate coordinates, unknown material ids
  3. components    6-connected components (floating pieces), sizes + bboxes; component 0 = largest
  4. symmetry      mismatch counts for mirror-X, mirror-Z, diagonal (about the bbox centre unless --axis)
  5. material mix  per-Y-layer dominant materials (spot layering / exposed-lining bugs)
Exit code 0 = clean (one component, no integrity errors), 1 = findings.
"""
import argparse, json, os, sys
from collections import Counter
import numpy as np

ITEMS = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', 'StreamingAssets', 'voxel_items')
MAX_DENSE = 400_000_000      # refuse dense grids above this many cells (== ~400MB bool) -- fall back to sparse mode


def load(arg):
    p = arg if arg.lower().endswith('.json') else os.path.join(ITEMS, arg + '.json')
    return p, json.load(open(p, encoding='utf-8'))


def components(v, dims):
    """6-connected components via dense label grid + iterative frontier expansion. Only VOXEL cells are ever visited
    (a flood fill that expands into empty space is the classic way to eat all RAM -- never do that)."""
    W, H, D = dims
    n = len(v)
    idx = -np.ones((W, H, D), np.int32) if W * H * D <= MAX_DENSE else None
    if idx is None:
        return components_sparse(v)
    idx[v[:, 0], v[:, 1], v[:, 2]] = np.arange(n, dtype=np.int32)
    lab = -np.ones(n, np.int32)
    comp = 0
    nbr = ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1))
    for s in range(n):
        if lab[s] >= 0: continue
        lab[s] = comp
        frontier = v[s:s + 1, :3]
        while len(frontier):
            nxt = []
            for dx, dy, dz in nbr:
                q = frontier + (dx, dy, dz)
                ok = (q[:, 0] >= 0) & (q[:, 0] < W) & (q[:, 1] >= 0) & (q[:, 1] < H) & (q[:, 2] >= 0) & (q[:, 2] < D)
                q = q[ok]
                j = idx[q[:, 0], q[:, 1], q[:, 2]]
                j = j[j >= 0]
                j = j[lab[j] < 0]
                if len(j):
                    j = np.unique(j); lab[j] = comp; nxt.append(v[j, :3])
            frontier = np.concatenate(nxt) if nxt else frontier[:0]
        comp += 1
    return lab, comp


def components_sparse(v):
    cells = {tuple(c): i for i, c in enumerate(v[:, :3].tolist())}
    lab = np.full(len(v), -1, np.int32); comp = 0
    for s in range(len(v)):
        if lab[s] >= 0: continue
        lab[s] = comp; stack = [tuple(v[s, :3])]
        while stack:
            x, y, z = stack.pop()
            for n in ((x+1,y,z),(x-1,y,z),(x,y+1,z),(x,y-1,z),(x,y,z+1),(x,y,z-1)):
                j = cells.get(n)
                if j is not None and lab[j] < 0: lab[j] = comp; stack.append(n)
        comp += 1
    return lab, comp


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('asset'); ap.add_argument('--no-sym', action='store_true'); ap.add_argument('--axis', type=float, default=None)
    a = ap.parse_args()
    path, d = load(a.asset)
    W, H, D = d['dims']; vs = d.get('voxelSize')
    v = np.asarray(d['voxels'], dtype=np.int64).reshape(-1, 4)
    names = {m['id']: m['name'] for m in d['materials']}
    bad = 0
    print(f'== {os.path.basename(path)}  type={d.get("assetType")}  dims={W}x{H}x{D}  vs={vs}')
    if vs: print(f'   real size {W*vs:.3f} x {H*vs:.3f} x {D*vs:.3f} m')

    # 1 summary
    lo, hi = v[:, :3].min(0), v[:, :3].max(0)
    print(f'   voxels {len(v):,}  bbox {lo.tolist()}..{hi.tolist()}  fill {len(v)/(W*H*D)*100:.2f}% of volume')
    mc = Counter(v[:, 3].tolist())
    print('   materials:', ', '.join(f'{names.get(k, "?"+str(k))}={n:,}' for k, n in mc.most_common(12)) + (' ...' if len(mc) > 12 else ''))

    # 2 integrity
    oob = int(((v[:, 0] < 0) | (v[:, 0] >= W) | (v[:, 1] < 0) | (v[:, 1] >= H) | (v[:, 2] < 0) | (v[:, 2] >= D)).sum())
    flat = (v[:, 0] * H + v[:, 1]) * D + v[:, 2]
    dup = len(flat) - len(np.unique(flat))
    unk = sorted(set(mc) - set(names))
    print(f'-- integrity: out-of-bounds {oob}, duplicate coords {dup}, unknown material ids {unk or "none"}')
    bad += (oob > 0) + (dup > 0) + bool(unk)
    if oob: sys.exit('out-of-bounds voxels -- fix before any other check')

    # 3 components
    lab, nc = components(v, (W, H, D))
    sizes = np.bincount(lab, minlength=nc)
    order = np.argsort(-sizes)
    print(f'-- components (6-connected): {nc}')
    for r, c in enumerate(order[:8]):
        m = lab == c; p = v[m, :3]
        print(f'   #{r}: {sizes[c]:,} voxels  bbox {p.min(0).tolist()}..{p.max(0).tolist()}' + ('   <-- main body' if r == 0 else '   <-- FLOATING'))
    if nc > 8: print(f'   ... and {nc-8} more small pieces (total {int(sizes[order[8:]].sum()):,} voxels)')
    bad += nc > 1

    # 4 symmetry
    if not a.no_sym:
        cx = a.axis if a.axis is not None else (lo[0] + hi[0]) / 2
        cz = a.axis if a.axis is not None else (lo[2] + hi[2]) / 2
        key = {(int(x), int(y), int(z)): int(m) for x, y, z, m in v.tolist()}
        def mism(f): return sum(1 for (x, y, z), m in key.items() if key.get(f(x, y, z)) != m)
        mx = mism(lambda x, y, z: (int(round(2 * cx - x)), y, z))
        mz = mism(lambda x, y, z: (x, y, int(round(2 * cz - z))))
        dg = mism(lambda x, y, z: (int(round(cx + (z - cz))), y, int(round(cz + (x - cx)))))
        print(f'-- symmetry about ({cx}, {cz}): asymmetric voxels  mirror-X {mx}  mirror-Z {mz}  diagonal {dg}  (0 = perfect)')

    # 5 layer mix
    print('-- per-layer dominant materials (Y = layer):')
    for y in range(int(lo[1]), int(hi[1]) + 1):
        c = Counter(v[v[:, 1] == y, 3].tolist())
        if c: print(f'   y={y:3d} {sum(c.values()):>8,}  ' + ', '.join(f'{names.get(k, k)} {n:,}' for k, n in c.most_common(3)))
    print('RESULT:', 'CLEAN' if not bad else f'{int(bad)} finding group(s) above')
    sys.exit(0 if not bad else 1)


if __name__ == '__main__':
    main()
