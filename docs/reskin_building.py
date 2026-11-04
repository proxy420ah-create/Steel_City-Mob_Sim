"""reskin_building.py -- apply a period skin (building_skin.py STYLE) to a building source JSON.

    python reskin_building.py office_complex            # apply style S1 (back-room brick); --style S2|S3 for the others
    # to switch style later: building_edit.py office_complex undo  (back to the un-skinned file), then reskin with the new --style
    python reskin_building.py office_complex --dry      # report only, write nothing
    python reskin_building.py office_complex --zones    # (re)detect rooms and write <name>.skin.json, then stop
    python building_edit.py office_complex undo         # restore the previous file (reskin backs up via building_edit.save)

GEOMETRY IS NEVER TOUCHED -- only material ids change, so mounts, collision, walk grid and room layout are identical.
  * WALLS  (y 3..62, wall materials): running-bond brick over oak wainscot, keyed on GLOBAL coordinates along each wall
            plane (no phase seams across modules; parallel walls get different brick tones).
  * ROOM FLOORS (zones.rooms rectangles): oak strip + walnut/maple picture-frame border, unique seed per room.
  * HALL FLOORS (every other floor cell): the same oak strip, generated globally so boards line up with the
    rooms (no threshold seams); walnut border + maple inlay painted by distance-to-wall, framing walls and doorways.
  * Felt (card-table discs) survives only inside zones.keepFelt rectangles; every other disc is re-laid with
    the local floor pattern (a green disc in a corridor or side room is a leftover).
  * Ceilings are untouched.
Zones live in <name>.skin.json (auto-detected the first time; edit it to override): {"rooms": [[x0,x1,z0,z1], ...]}
"""
import argparse, json, os, sys
import numpy as np
import building_skin as S
from building_edit import ITEMS, save

WALL_MATS = ('Brick A', 'Brick B', 'Mortar')
FLOOR_MATS = ('Plank A', 'Plank B', 'Seam')
FELT_MATS = ('Felt', 'Felt Edge')
WALL_Y0, WALL_Y1, FLOOR_Y1 = 3, 62, 2
ROOM = 150


def load(name):
    d = json.load(open(os.path.join(ITEMS, name + '.json'), encoding='utf-8'))
    v = np.asarray(d['voxels'], dtype=np.int32)
    names = {m['id']: m['name'] for m in d['materials']}
    W, H, D = d['dims']
    full = np.zeros((W, H, D), np.int16)
    full[v[:, 0], v[:, 1], v[:, 2]] = v[:, 3]
    return d, full, names


def ids(names, group):
    return [i for i, n in names.items() if n in group]


def runs(row):
    out, a = [], 0
    while a < len(row):
        if row[a]:
            e = a
            while e < len(row) and row[e]: e += 1
            out.append((a, e - 1)); a = e
        else: a += 1
    return out


def detect_rooms(full, names):
    """Rooms = 150x150 modules, found from their 150-long back walls (3 thick) with floor on one side."""
    wm = np.isin(full[:, 10, :], ids(names, WALL_MATS))
    fl = np.isin(full[:, 2, :], ids(names, FLOOR_MATS + FELT_MATS))
    rooms, seen = [], set()
    for z in range(wm.shape[1]):
        for a, b in runs(wm[:, z]):
            if b - a + 1 != ROOM or (a, z - 1) in seen: continue
            seen.add((a, z))
            nxt = (a, z + 1)
            seen.add(nxt)
            cx = a + ROOM // 2
            if z + 10 < fl.shape[1] and fl[cx, z + 10]:
                rooms.append([a, a + ROOM - 1, z, z + ROOM - 1])           # back wall at the room's north edge
            elif z - 10 >= 0 and fl[cx, z - 10]:
                rooms.append([a, a + ROOM - 1, z - ROOM + 3, z + 2])        # back wall at the room's south edge
    uniq = []
    for r in rooms:                                                          # a 3-row wall yields 3 hits; keep one per room
        if not any(abs(r[0] - u[0]) < 4 and abs(r[2] - u[2]) < 4 for u in uniq): uniq.append(r)
    return sorted(uniq, key=lambda r: (r[2], r[0]))


def zones(name, full, names, force=False):
    p = os.path.join(ITEMS, name + '.skin.json')
    if os.path.isfile(p) and not force:
        return json.load(open(p))
    z = {'rooms': detect_rooms(full, names), 'keepHallFelt': False, 'keepFelt': [],
         'note': 'rooms = [x0,x1,z0,z1] inclusive voxel rectangles; every other floor cell is hall. '
                 'keepFelt = rectangles where felt discs survive (e.g. under the meeting table); all other felt is re-laid. Edit freely.'}
    json.dump(z, open(p, 'w'), indent=1)
    return z


def wall_keys(full, names):
    """Per wall voxel column (x,z): (orientation, plane-start) so each wall plane gets its own seeded pattern and
    the pattern runs along the wall (u = x for walls along x, u = z for walls along z)."""
    wm = np.isin(full[:, 10, :], ids(names, WALL_MATS))
    W, D = wm.shape
    runx = np.zeros((W, D), np.int32); runz = np.zeros((W, D), np.int32)
    startx = np.zeros((W, D), np.int32); startz = np.zeros((W, D), np.int32)
    for z in range(D):
        for a, b in runs(wm[:, z]):
            runx[a:b + 1, z] = b - a + 1; startx[a:b + 1, z] = a
    for x in range(W):
        for a, b in runs(wm[x, :]):
            runz[x, a:b + 1] = b - a + 1; startz[x, a:b + 1] = a
    along_x = runx >= runz
    return wm, along_x, startz, startx


def apply(name, style='S1', dry=False, log=print):
    d, full, names = load(name)
    W, H, D = d['dims']
    st = S.STYLES[style]
    zn = zones(name, full, names)
    rooms = zn['rooms']; keep_felt = zn.get('keepHallFelt', False)
    keepF = np.zeros((W, D), bool)                                             # felt survives only in keepFelt rects
    for a_, b_, c_, d_ in zn.get('keepFelt', []): keepF[a_:b_ + 1, c_:d_ + 1] = True
    log(f'[reskin] {name}: {len(rooms)} rooms {rooms}')

    out = full.copy()
    new_names = {}                                                         # skin name -> material id
    next_id = max(names) + 1

    def mid(n):
        nonlocal next_id
        if n not in new_names:
            new_names[n] = next_id; next_id += 1
        return new_names[n]

    # ---------------- floors
    plank = np.isin(full[:, :FLOOR_Y1 + 1, :], ids(names, FLOOR_MATS))
    felt = np.isin(full[:, :FLOOR_Y1 + 1, :], ids(names, FELT_MATS))
    floor2d = (plank | felt).any(axis=1)                                     # [x,z]
    in_room = np.zeros((W, D), bool)
    stats = {'room_floor': 0, 'hall_floor': 0, 'felt_removed': 0}
    for ri, (x0, x1, z0, z1) in enumerate(rooms):
        in_room[x0:x1 + 1, z0:z1 + 1] = True
        pat = st['floor'](x1 - x0 + 1, z1 - z0 + 1, seed=100 + ri * 17, border=9)   # [z,x]
        for z in range(z0, z1 + 1):
            for x in range(x0, x1 + 1):
                if not floor2d[x, z]: continue
                n = pat[z - z0, x - x0]
                for y in range(FLOOR_Y1 + 1):
                    if felt[x, y, z] and keepF[x, z]: continue
                    if plank[x, y, z] or felt[x, y, z]:
                        if felt[x, y, z]: stats['felt_removed'] += 1
                        out[x, y, z] = mid(n if y == FLOOR_Y1 else 'oak3'); stats['room_floor'] += 1
    hall = floor2d & ~in_room
    wall_cols = np.isin(full[:, WALL_Y0:WALL_Y1 + 1, :], ids(names, WALL_MATS)).any(axis=1)
    open_hall = hall & ~wall_cols
    open_hall = open_hall | (hall & felt.any(axis=1))                       # felt discs stand on open floor
    solidish = ~open_hall
    B = 9                                                                  # border depth, matching the rooms
    dist = np.full((W, D), B + 1, np.int16)                                # chebyshev distance to nearest wall/void/room
    dist[solidish] = 0
    frontier = solidish
    for r_ in range(1, B + 1):
        nxt = np.zeros((W, D), bool)
        nxt[1:, :] |= frontier[:-1, :]; nxt[:-1, :] |= frontier[1:, :]
        nxt[:, 1:] |= frontier[:, :-1]; nxt[:, :-1] |= frontier[:, 1:]
        nxt[1:, 1:] |= frontier[:-1, :-1]; nxt[:-1, :-1] |= frontier[1:, 1:]
        nxt[1:, :-1] |= frontier[:-1, 1:]; nxt[:-1, 1:] |= frontier[1:, :-1]
        frontier = nxt & (dist > B)
        dist[frontier] = r_
    q = st['floor'](W, D, seed=4, border=0)                               # one global strip grid -> boards continue rooms->hall
    wal = ['walnut1', 'walnut2', 'walnut3']
    for x, z in np.argwhere(hall):
        e = int(dist[x, z])
        n = wal[((x // 2) + (z // 30)) % 3] if e < B - 1 else ('maple' if e == B - 1 else q[z, x])
        for y in range(FLOOR_Y1 + 1):
            if felt[x, y, z] and (keep_felt or keepF[x, z]): continue
            if plank[x, y, z] or felt[x, y, z]:
                if felt[x, y, z]: stats['felt_removed'] += 1
                out[x, y, z] = mid(n if y == FLOOR_Y1 else 'oak3'); stats['hall_floor'] += 1

    # ---------------- walls
    wmask3 = np.isin(full[:, WALL_Y0:WALL_Y1 + 1, :], ids(names, WALL_MATS))
    wm, along_x, startz, startx = wall_keys(full, names)
    cache = {}
    nwall = 0
    for x, z in np.argwhere(wmask3.any(axis=1)):
        ax_ = bool(along_x[x, z])
        key = ('x', int(startz[x, z])) if ax_ else ('z', int(startx[x, z]))
        if key not in cache:
            cache[key] = st['wall'](WALL_Y1 - WALL_Y0 + 1, max(W, D), seed=200 + (key[1] % 97) * 7 + (3 if key[0] == 'z' else 0))
        pat = cache[key]; u = x if ax_ else z
        for y in np.nonzero(wmask3[x, :, z])[0]:
            out[x, WALL_Y0 + y, z] = mid(pat[y, u]); nwall += 1
    stats['wall_voxels'] = nwall
    log(f'[reskin] stats {stats}  | wall planes {len(cache)} | new materials {len(new_names)}')

    # ---------------- write
    if dry:
        log('[reskin] dry run - nothing written'); return stats
    mats = d['materials']
    for n, i in new_names.items():
        hx = S.PAL[n]
        mats.append({'id': i, 'name': n, 'hex': hx, 'r': int(hx[1:3], 16), 'g': int(hx[3:5], 16), 'b': int(hx[5:7], 16),
                     'category': 'Skin ' + style})
    xs, ys, zs = np.nonzero(out)
    d['voxels'] = np.stack([xs, ys, zs, out[xs, ys, zs]], axis=1).astype(int).tolist()
    save(name, d)
    log(f'[reskin] wrote {name}.json ({len(d["voxels"]):,} voxels, {len(mats)} materials); previous file kept as {name}.prev.json')
    return stats


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('name'); ap.add_argument('--style', default='S1'); ap.add_argument('--dry', action='store_true'); ap.add_argument('--zones', action='store_true')
    a = ap.parse_args()
    if a.zones:
        d, full, names = load(a.name)
        print(json.dumps(zones(a.name, full, names, force=True)))
    else:
        apply(a.name, a.style, a.dry)
