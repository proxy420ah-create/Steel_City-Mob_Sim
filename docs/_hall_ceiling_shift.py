"""_hall_ceiling_shift.py -- re-grid the hall's coffered ceiling onto the office beam rhythm.

Office wing beams are 8v (.24m) on a 48v pitch (z46, 94, 142 -> next 190, 238). The hall's
ceiling was authored 4v (west, x<245) / 6v (east, x>=245) late, with a 10v spine. This pass
rewrites ONLY the beam layer (y39-41, mat BEAM) in the hall so the grid continues the office:

    west (x<245):  bay 150-189 | spine 190-197 | bay 198-237 | north beam 238-249 (wall z250-252)
    east (x>=245): bay 150-189 | spine 190-197 | bay 198-245 | north beam 246-257 (unchanged edge)

Cross beams (x direction) are snapped to the office grid, 8v on a 48v pitch: x44, 92, 140, 188, 236(+wall to 251),
290, 338, 386 (west of x44 keeps the existing pattern). Coffer layer
(y42-44, mat COFFER) is untouched. Spine center = z194 edge -> world z 3.48.

    python _hall_ceiling_shift.py <office_complex.json> [--dry]
"""
import argparse, json, os, shutil
import numpy as np

BEAM, COFFER = 157, 156
Y0, Y1 = 39, 41
WEST_X1 = 245


XBEAMS = [(44, 51), (92, 99), (140, 147), (188, 195), (236, 251), (290, 297), (338, 345), (386, 399)]


def xb_new(x):
    return any(a <= x <= b for a, b in XBEAMS)


def zbeam(x, z):
    if z < 150: return False
    if 190 <= z <= 197: return True
    if x < WEST_X1: return z >= 238
    return z >= 246


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('name'); ap.add_argument('--dry', action='store_true')
    a = ap.parse_args()
    d = json.load(open(a.name, encoding='utf8'))
    v = np.array(d['voxels'], dtype=np.int32)
    G = np.zeros((600, 69, 600), np.int16); G[v[:, 0], v[:, 1], v[:, 2]] = v[:, 3] + 1
    has = (G[:, 10:, :] > 0)
    face = np.where(has.any(1), has.argmax(1) + 10, 0)
    xbeam = {x: (xb_new(x) if x >= 44 else face[x, 170] == 39) for x in range(30, 391) if face[x, 170] in (39, 42)}
    add, rem = [], []
    for x, xb in xbeam.items():
        zmax = 249 if x < WEST_X1 else 257
        for z in range(150, zmax + 1):
            if face[x, z] not in (39, 42): continue
            want = xb or zbeam(x, z)
            for y in range(Y0, Y1 + 1):
                cur = G[x, y, z] - 1
                if want and cur != BEAM:
                    if cur >= 0: rem.append((x, y, z))
                    add.append([x, y, z, BEAM])
                elif not want and cur == BEAM:
                    rem.append((x, y, z))
    print(f'add {len(add)} cells, remove {len(rem)} cells')
    if a.dry: return
    snap = a.name.replace('.json', '.snap-pre-hallshift.json')
    if not os.path.exists(snap): shutil.copyfile(a.name, snap)
    rs = set(rem)
    keep = [t for t in d['voxels'] if (t[0], t[1], t[2]) not in rs]
    d['voxels'] = keep + add
    json.dump(d, open(a.name, 'w', encoding='utf8', newline=''))
    print('written', len(d['voxels']))


main()
