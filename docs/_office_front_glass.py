"""_office_front_glass.py -- P.I. office front on the office's hall-facing wall (slab z147-149).

Tight sidelights + glazed transom in the BUILDING (the door KIT stays leaf-only):

    sidelights: glass x102-104 + x133-135, y3-29 (door height), pane at slab mid z148,
                air either side (z147/z149 chipped) so sightlines pass through.
    transom:    glass x103-134, y31-37 over the head jamb (y30 walnut stays, y38 lintel cap
                stays), pane at z148, air either side.

Storefront Glass (id 114) is already in the file palette; name carries 'Glass' so voxEmit
splits it transparent in the commission. Its hex is recolored to the door KIT's Frosted
Glass #aec6cf so building glass and door glass match. Bore reveals are repainted to
walnut2 so the opening reads as wood-lined, not a raw brick cross-section.

    python _office_front_glass.py <office_complex.json> [--dry]
"""
import argparse, json, os, shutil

GLASS = 114          # Storefront Glass — hex recolored to the door KIT's Frosted Glass #aec6cf below
REVEAL = 160         # walnut2 — lines the glazed opening so the bore reads as a wood reveal, not raw brick cross-section
DOOR_GLASS_HEX = '#aec6cf'
SL_Z = (147, 148, 149)
SIDES = [(102, 104), (133, 135)]
TRANSOM = ((103, 134), (31, 37))


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('name'); ap.add_argument('--dry', action='store_true')
    a = ap.parse_args()
    d = json.load(open(a.name, encoding='utf8'))
    kill, add = set(), []
    for x0, x1 in SIDES:
        for x in range(x0, x1 + 1):
            for y in range(3, 30):
                kill.update({(x, y, SL_Z[0]), (x, y, SL_Z[2])})
                add.append([x, y, SL_Z[1], GLASS])
    (x0, x1), (y0, y1) = TRANSOM
    for x in range(x0, x1 + 1):
        for y in range(y0, y1 + 1):
            kill.update({(x, y, SL_Z[0]), (x, y, SL_Z[2])})
            add.append([x, y, SL_Z[1], GLASS])
    print(f'glass {len(add)} cells, chip {len(kill)}')
    if a.dry: return
    snap = a.name.replace('.json', '.snap-pre-frontglass.json')
    if not os.path.exists(snap): shutil.copyfile(a.name, snap)
    gset = {(t[0], t[1]) for t in add}
    reveal = set()
    for (x, y) in gset:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if (x + dx, y + dy) not in gset:
                for z in (SL_Z[0], SL_Z[2]): reveal.add((x + dx, y + dy, z))
    solid = {(t[0], t[1], t[2]): t for t in d['voxels'] if (t[0], t[1], t[2]) not in kill}
    rep = [(x, y, z) for (x, y, z) in reveal if (x, y, z) in solid]
    for p in rep: solid[p] = [p[0], p[1], p[2], REVEAL]
    print(f'reveal repaint {len(rep)} cells')
    for m in d['materials']:
        if m['id'] == GLASS:
            m['hex'] = DOOR_GLASS_HEX
            m['r'], m['g'], m['b'] = int(DOOR_GLASS_HEX[1:3], 16), int(DOOR_GLASS_HEX[3:5], 16), int(DOOR_GLASS_HEX[5:7], 16)
    d['voxels'] = list(solid.values()) + add
    json.dump(d, open(a.name, 'w', encoding='utf8', newline=''))
    print('written', len(d['voxels']))


main()
