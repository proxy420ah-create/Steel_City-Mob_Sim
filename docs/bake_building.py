"""bake_building.py -- compile an editable room/building asset into a compact scene bake.

    python bake_building.py office_complex                # anchor 62,0,0 (default for office_complex)
    python bake_building.py my_building --anchor 0,0,0 --module 150

SOURCE (editable truth):  Assets/StreamingAssets/voxel_items/<name>.json   (Voxel Studio export)
OUTPUT (generated, runtime): Assets/StreamingAssets/voxel_items/<name>.bake.json

What the scene needs that the raw voxel JSON can't give it cheaply:
  * exposed-face quads, greedy-merged by colour (~1M voxels -> ~1M tris instead of 12.6M),
    split into STAGE (the original office module: its floor + back wall -- the only part
    group view shows) and ENV (everything else -- hidden in group view, shown in possess),
  * a walk-clearance grid (cells where Vinny's body circle fits: floor present, no wall
    within body height, radius clear),
  * a bit-packed occupancy volume (boom/camera collision by grid DDA),
  * room centres (felt discs) for per-room lights, and the file's placements[].

Coordinates: voxel cell i spans [i, i+1). World x = (u - ax) * vs, z = (u - az) * vs,
y = u * vs + yBase. ax = anchor module centre; az = centre + wall thickness so the back wall's
front face is world z = -2.25 (where the wall-mounted features sit); yBase puts the floor top at y = 0.
"""
import argparse, base64, json, os, sys, time
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ITEMS = os.path.normpath(os.path.join(HERE, '..', 'Assets', 'StreamingAssets', 'voxel_items'))

DEFAULT_ANCHOR = {'office_complex': (244, 0, 0)}   # where the ORIGINAL office module sits inside the file
FLOOR_H = 3            # floor slab thickness (voxels)
WALL_TOP = 63          # walls end / ceiling begins
WALL_T = 3             # wall thickness
BODY_TOP = 25          # walls matter up to this y for walking (Vinny ~21 voxels tall)
WALK_R = 3.5           # body clearance in cells (~0.105 m, same margin the old FLOOR clamp used)
Y_BASE = -0.09         # floor bottom is world -0.09 so the floor top is y=0


def b64(a):
    return base64.b64encode(np.ascontiguousarray(a).tobytes()).decode('ascii')


QUAD = np.dtype([('x', '<i2'), ('y', '<i2'), ('z', '<i2'), ('d', 'u1'), ('m', 'u1'), ('w', '<u2'), ('h', '<u2')])
DIRS = [(0, 1), (0, -1), (1, 1), (1, -1), (2, 1), (2, -1)]   # (axis, sign): +x -x +y -y +z -z


def greedy(mask):
    """mask[a,b] = material index or 0 -> list of (a, b, w, h, mat); w along a, h along b."""
    out = []
    used = np.zeros(mask.shape, bool)
    A, B = mask.shape
    for a, b in np.argwhere(mask > 0):
        if used[a, b]:
            continue
        c = mask[a, b]
        h = 1
        while b + h < B and mask[a, b + h] == c and not used[a, b + h]:
            h += 1
        w = 1
        while a + w < A and (mask[a + w, b:b + h] == c).all() and not used[a + w, b:b + h].any():
            w += 1
        used[a:a + w, b:b + h] = True
        out.append((a, b, w, h, c))
    return out


def mesh(mat, occ_for_exposure, skip_bottom=True):
    """Greedy quads for every voxel of `mat` (uint8 palette idx volume) whose face is exposed
    against `occ_for_exposure`. Returns a QUAD record array."""
    quads = []
    solid = mat > 0
    for di, (axis, sign) in enumerate(DIRS):
        nb = np.zeros_like(occ_for_exposure)
        sl_dst = [slice(None)] * 3; sl_src = [slice(None)] * 3
        if sign > 0:
            sl_dst[axis] = slice(0, -1); sl_src[axis] = slice(1, None)
        else:
            sl_dst[axis] = slice(1, None); sl_src[axis] = slice(0, -1)
        nb[tuple(sl_dst)] = occ_for_exposure[tuple(sl_src)]
        exposed = solid & ~nb
        if skip_bottom and axis == 1 and sign < 0:
            exposed[:, 0, :] = False
        m = np.where(exposed, mat, 0).astype(np.uint8)
        for s in range(m.shape[axis]):
            sl = np.take(m, s, axis=axis)
            if not sl.any():
                continue
            for a, b, w, h, c in greedy(sl):
                if axis == 0:   x, y, z = s, a, b       # u=y, v=z
                elif axis == 1: x, y, z = a, s, b       # u=x, v=z
                else:           x, y, z = a, b, s       # u=x, v=y
                quads.append((x, y, z, di, c, w, h))
    return np.array(quads, dtype=QUAD) if quads else np.zeros(0, dtype=QUAD)


def flood_centres(mask):
    """Connected components of a 2D bool mask -> list of (cx, cz) centroids (cell coords)."""
    seen = np.zeros(mask.shape, bool)
    res = []
    for x, z in np.argwhere(mask):
        if seen[x, z]:
            continue
        stack = [(x, z)]; seen[x, z] = True; sx = sz = n = 0
        while stack:
            cx, cz = stack.pop(); sx += cx; sz += cz; n += 1
            for nx, nz in ((cx + 1, cz), (cx - 1, cz), (cx, cz + 1), (cx, cz - 1)):
                if 0 <= nx < mask.shape[0] and 0 <= nz < mask.shape[1] and mask[nx, nz] and not seen[nx, nz]:
                    seen[nx, nz] = True; stack.append((nx, nz))
        if n > 200:
            res.append((round(sx / n, 1), round(sz / n, 1)))
    return res


def detect_anchor(full, felt_idx, lut_hex, palette, log=print):
    """Find where the ORIGINAL office module (commission_office.json) sits inside the building by testing each
    felt-disc candidate against it. Returns (x, 0, z) or None. Tolerates edits/moves of the rest of the layout."""
    ref_path = os.path.join(ITEMS, 'commission_office.json')
    if not os.path.isfile(ref_path) or not felt_idx:
        return None
    ref = json.load(open(ref_path, encoding='utf-8'))
    rhex = {m['id']: (m.get('hex') or '').lower() for m in ref['materials']}
    pal = [h.lower() for h in palette]
    rv = ref['voxels'][::300]
    top = np.isin(full[:, 2, :], felt_idx)
    best = None
    for cx, cz in flood_centres(top):
        ox, oz = int(round(cx)) - 74, int(round(cz)) - 74
        if ox < 0 or oz < 0:
            continue
        hit = tot = 0
        for x, y, z, m in rv:
            X, Z = x + ox, z + oz
            if 0 <= X < full.shape[0] and 0 <= Z < full.shape[2]:
                tot += 1
                if full[X, y, Z]:                      # geometry only - a re-skin changes every colour but never the shape
                    hit += 1
        if tot and (best is None or hit / tot > best[0]):
            best = (hit / tot, ox, oz)
    if best and best[0] >= 0.99:
        return (best[1], 0, best[2])
    log(f'[bake] WARNING: original module not found by auto-detect (best match {best[0] * 100:.1f}%)' if best else '[bake] WARNING: no felt disc to anchor on')
    return None


def bake(name, anchor=None, module=150, out=None, log=print):
    t0 = time.time()
    src = os.path.join(ITEMS, name + '.json')
    explicit = anchor
    anchor = tuple(anchor or DEFAULT_ANCHOR.get(name, (0, 0, 0)))
    d = json.load(open(src, encoding='utf-8'))
    W, H, D = d['dims']
    vs = d.get('voxelSize') or 0.03
    vox = np.asarray(d['voxels'], dtype=np.int32)
    log(f'[bake] {name}: {len(vox):,} voxels, dims {W}x{H}x{D}, vs {vs}, anchor {anchor}')

    used_ids = sorted(set(int(m) for m in np.unique(vox[:, 3])))
    mats = {m['id']: m for m in d.get('materials', [])}
    palette = [(mats.get(i, {}).get('hex') or '#888888') for i in used_ids]
    lut = np.zeros(max(used_ids) + 1, np.uint8)
    for k, i in enumerate(used_ids):
        lut[i] = k + 1                                   # 0 = empty
    full = np.zeros((W, H, D), np.uint8)
    full[vox[:, 0], vox[:, 1], vox[:, 2]] = lut[vox[:, 3]]

    felt_idx0 = [k + 1 for k, i in enumerate(used_ids) if (mats.get(i, {}).get('name') or '').lower() == 'felt']
    if not explicit:
        found = detect_anchor(full, felt_idx0, None, palette, log)
        if found:
            if found != anchor:
                log(f'[bake] anchor auto-detected at {found} (default was {anchor})')
            anchor = found
        else:
            log(f'[bake] using default anchor {anchor} -- verify the original module is there')
    # STAGE = the original module's floor + its back wall (z < WALL_T); ENV = everything else
    ax0, ay0, az0 = anchor
    sm = np.zeros((W, H, D), bool)
    sm[ax0:ax0 + module, 0:FLOOR_H, az0:az0 + module] = True                       # floor plate
    sm[ax0:ax0 + module, FLOOR_H:WALL_TOP, az0:az0 + WALL_T] = True                # back wall
    stage = np.where(sm, full, 0).astype(np.uint8)
    env = np.where(sm, 0, full).astype(np.uint8)

    q_stage = mesh(stage, stage > 0)       # stage exposure vs stage alone (its edge faces stay closed in group view)
    q_env = mesh(env, full > 0)            # env exposure vs everything
    log(f'[bake] quads: stage {len(q_stage):,}  env {len(q_env):,}  ({time.time() - t0:.1f}s)')

    occ = full > 0
    floor = occ[:, 0:FLOOR_H, :].any(axis=1)
    blocked = occ[:, FLOOR_H:BODY_TOP, :].any(axis=1)
    passable = floor & ~blocked
    ok = passable.copy()
    r = int(np.ceil(WALK_R))
    for dx in range(-r, r + 1):
        for dz in range(-r, r + 1):
            if dx * dx + dz * dz <= WALK_R * WALK_R and (dx or dz):
                sh = np.zeros_like(passable)
                xs = slice(max(0, dx), W + min(0, dx)); xd = slice(max(0, -dx), W + min(0, -dx))
                zs = slice(max(0, dz), D + min(0, dz)); zd = slice(max(0, -dz), D + min(0, -dz))
                sh[xd, zd] = passable[xs, zs]
                ok &= sh
    felt_idx = [k + 1 for k, i in enumerate(used_ids) if (mats.get(i, {}).get('name') or '').lower() == 'felt']
    rooms = flood_centres(np.isin(full[:, 2, :], felt_idx)) if felt_idx else []

    bake_obj = {
        'v': 1, 'src': name, 'voxelSize': vs, 'dims': [W, H, D], 'yBase': Y_BASE,
        # The live scene's back wall sits OUTSIDE the 4.5m plate (front face at world z=-2.25, where the sign/board/
        # shelf/tray/pegboard mount); the merged asset keeps it inside the footprint. Shift z by the wall thickness
        # so that face lands on -2.25 exactly. x stays centred (side walls: interior is 9cm narrower per side).
        'anchor': {'ax': ax0 + module / 2, 'az': az0 + module / 2 + WALL_T, 'module': module, 'offset': [ax0, ay0, az0]},
        'palette': palette,
        'stage': {'n': int(len(q_stage)), 'b64': b64(q_stage)},
        'env': {'n': int(len(q_env)), 'b64': b64(q_env)},
        'walk': {'w': W, 'd': D, 'r': WALK_R, 'b64': b64(ok.astype(np.uint8))},
        'occ': {'w': W, 'h': H, 'd': D, 'b64': b64(np.packbits(occ.reshape(-1)))},
        'rooms': rooms,
        'placements': d.get('placements', []),
        'srcMTime': os.path.getmtime(src),
        'stats': {'voxels': int(len(vox)), 'walkCells': int(ok.sum())},
    }
    dst = out or os.path.join(ITEMS, name + '.bake.json')
    with open(dst + '.tmp', 'w', encoding='utf-8') as f:
        json.dump(bake_obj, f, separators=(',', ':'))
    os.replace(dst + '.tmp', dst)
    json.dump({'mtime': os.path.getmtime(src), 'size': os.path.getsize(src)}, open(dst[:-len('.json')] + '.stamp', 'w'))   # server compares this, not file timestamps
    log(f'[bake] wrote {dst}  {os.path.getsize(dst) / 1e6:.2f} MB  rooms={len(rooms)}  walkCells={int(ok.sum()):,}  total {time.time() - t0:.1f}s')
    return dst


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('name')
    ap.add_argument('--anchor', help='x,y,z offset of the original module inside the file (default per building)')
    ap.add_argument('--module', type=int, default=150)
    ap.add_argument('--out')
    a = ap.parse_args()
    bake(a.name, tuple(int(v) for v in a.anchor.split(',')) if a.anchor else None, a.module, a.out)
