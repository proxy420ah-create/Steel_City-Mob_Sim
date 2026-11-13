"""building_skin.py -- period-appropriate floor / wall / hall skins for the Commission building.

Each pattern function returns a 2D array of MATERIAL NAMES (small fixed palettes, so the building keeps a
sane material list). Patterns are keyed on GLOBAL coordinates (u along the surface, v up/across) so adjacent
modules line up with no phase seams. Walls: array[v=0 at the floor, u]. Floors: array[z, x].

Voxel = 3 cm. Real-world rhythm: brick 7 wide + 1 mortar, course 3 + 1; oak strip 2 voxels (~2.25"); quarry tile 5 + 1 grout.
Setting: 1920s Pittsburgh -- a ward-boss / social-club back room where the Commission meets.
"""
import numpy as np

# ---------------------------------------------------------------- palettes (name -> hex), muted for the dim scene
PAL = {
    # oak strip (dark-stained quarter-sawn), end joints, walnut border, maple inlay
    'oak1': '#4a3322', 'oak2': '#42301f', 'oak3': '#3a2a1c', 'oak4': '#503824', 'oak5': '#36261a', 'oak6': '#46341f',
    'oakEnd': '#2b1e14', 'walnut1': '#2a1c12', 'walnut2': '#2f2015', 'walnut3': '#251912', 'maple': '#7a6342',
    # mahogany parquet (basket weave)
    'mah1': '#4a2f1f', 'mah2': '#3d281b', 'mah3': '#54372a', 'mah4': '#46301f', 'brass': '#8a6d2a',
    # battleship linoleum + border
    'lino1': '#3b3226', 'lino2': '#342c21', 'lino3': '#41382b', 'linoSeam': '#2a231a', 'oxblood': '#4a1f1c', 'blackline': '#15110f',
    # face brick (sorted light -> dark), tuck-pointed mortar, soot clinker
    'brk1': '#5a3a30', 'brk2': '#52352c', 'brk3': '#4a3028', 'brk4': '#432c25', 'brk5': '#3c2721', 'brk6': '#35221d', 'brk7': '#2c1c18',
    'mortar': '#6b6254', 'mortarDk': '#4f483d',
    # wainscot / trim
    'wain': '#2f2016', 'wainPanel': '#281b13', 'wainHi': '#4a3524', 'wainLo': '#1d130d', 'rail': '#54402b', 'base': '#241812',
    # plaster (sorted light -> nicotine-dark), crack, picture rail
    'pl1': '#8a8165', 'pl2': '#7f7659', 'pl3': '#756d52', 'pl4': '#6a6249', 'pl5': '#5e573f', 'crack': '#5f5942',
    'frieze': '#928968',
    # ward green two-tone
    'grn1': '#44614a', 'grn2': '#3d5a45', 'grn3': '#385340', 'cream1': '#938a6c', 'cream2': '#877e62', 'cream3': '#7b7358',
    # hall tile
    'quarry1': '#5e2c24', 'quarry2': '#55261f', 'quarry3': '#4c211b', 'groutDk': '#2a2220',
    'chkBlack': '#17130f', 'chkCream': '#8d8466', 'chkEdge': '#3a322a',
    'terr1': '#7d745a', 'terr2': '#756c54', 'terrFleck': '#3a3226', 'terrFleck2': '#5b4a3a',
}


def _rng(seed):
    return np.random.default_rng(seed)


def vnoise(h, w, s, seed):
    """Smooth value noise in [0,1], feature size ~s voxels."""
    g = _rng(seed).random((h // s + 3, w // s + 3))
    ys, xs = np.arange(h) / s, np.arange(w) / s
    y0, x0 = ys.astype(int), xs.astype(int)
    fy, fx = (ys - y0)[:, None], (xs - x0)[None, :]
    a, b = g[y0][:, x0], g[y0][:, x0 + 1]
    c, d = g[y0 + 1][:, x0], g[y0 + 1][:, x0 + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def _pick(names, idx):
    return np.array(names, dtype=object)[np.clip(idx, 0, len(names) - 1)]


# ============================================================ FLOORS  (array[z, x])
def floor_oak_strip(W, D, seed=1, border=6):
    """Dark-stained oak strip: 2-voxel strips, random board lengths with staggered end joints, 6 tones,
    walnut picture-frame border with a maple inlay line. Seams are TONE changes, not black grid lines."""
    r = _rng(seed)
    out = np.empty((D, W), dtype=object)
    tones = ['oak1', 'oak2', 'oak3', 'oak4', 'oak5', 'oak6']
    wear = vnoise(D, W, 14, seed + 7)
    for z0 in range(0, D, 2):                                   # one strip = 2 rows
        x = -int(r.integers(0, 60))                              # random stagger per strip
        while x < W:
            ln = int(r.integers(34, 96)); t = int(r.integers(0, 6))
            mid = min(W - 1, max(0, x + ln // 2))
            if wear[min(D - 1, z0), mid] > 0.68: t = (t + 3) % 6                        # worn board = lighter tone, whole board
            for xx in range(max(0, x), min(W, x + ln)):
                for dz in range(2):
                    if z0 + dz < D:
                        out[z0 + dz, xx] = tones[t] if r.random() > .018 else tones[(t + 1) % 6]   # rare grain flecks
            if 0 <= x + ln - 1 < W:                              # end joint = 1 darker voxel column
                for dz in range(2):
                    if z0 + dz < D: out[z0 + dz, x + ln - 1] = 'oakEnd'
            x += ln
    b = border                                                   # walnut frame (strips run along each side) + maple inlay
    for z in range(D):
        for x in range(W):
            e = min(x, W - 1 - x, z, D - 1 - z)
            if e < b - 1:
                horiz = min(z, D - 1 - z) < min(x, W - 1 - x)
                out[z, x] = ['walnut1', 'walnut2', 'walnut3'][((z if horiz else x) // 2 + (x if horiz else z) // 30) % 3]
            elif e == b - 1:
                out[z, x] = 'maple'
    return out


def floor_basketweave(W, D, seed=2, border=6):
    """Basket-weave parquet: 8x8 squares of four 2-voxel strips, orientation alternating, mahogany tones per strip."""
    r = _rng(seed)
    out = np.empty((D, W), dtype=object)
    tones = ['mah1', 'mah2', 'mah3', 'mah4']
    for sz in range(0, D, 8):
        for sx in range(0, W, 8):
            vert = ((sx // 8) + (sz // 8)) % 2 == 0
            for s in range(4):
                t = tones[int(r.integers(0, 4))]
                for a in range(2):
                    for l in range(8):
                        z, x = (sz + l, sx + s * 2 + a) if vert else (sz + s * 2 + a, sx + l)
                        if z < D and x < W: out[z, x] = t
    for z in range(D):
        for x in range(W):
            e = min(x, W - 1 - x, z, D - 1 - z)
            if e < border - 2: out[z, x] = 'walnut1' if (min(x, z, W - 1 - x, D - 1 - z) // 2) % 2 == 0 else 'walnut2'
            elif e == border - 2: out[z, x] = 'brass'
            elif e == border - 1: out[z, x] = 'walnut3'
    return out


def floor_battleship(W, D, seed=3, border=8):
    """Battleship linoleum: mottled sheet, faint sheet seams every ~54 voxels, oxblood border stripe + black pin-line."""
    n = vnoise(D, W, 9, seed); n2 = vnoise(D, W, 3, seed + 1)
    f = _rng(seed + 2).random((D, W))                          # fine mottling, not blobs (a sheet, not camouflage)
    out = np.where(f > .93, 'lino3', np.where(f < .07, 'lino1', 'lino2')).astype(object)
    for x in range(53, W, 54): out[:, x] = 'linoSeam'
    for z in range(D):
        for x in range(W):
            e = min(x, W - 1 - x, z, D - 1 - z)
            if border - 4 <= e < border - 1: out[z, x] = 'oxblood'
            elif e == border - 1 or e == border - 5: out[z, x] = 'blackline'
    return out


# ============================================================ HALL FLOORS
def hall_quarry(W, D, seed=4):
    """Red quarry tile 5x5 + 1 grout, tone per tile, black-tile border."""
    r = _rng(seed); out = np.empty((D, W), dtype=object)
    tone = {(i, j): ['quarry1', 'quarry2', 'quarry3'][int(r.integers(0, 3))] for i in range(W // 6 + 2) for j in range(D // 6 + 2)}
    for z in range(D):
        for x in range(W):
            out[z, x] = 'groutDk' if (x % 6 == 5 or z % 6 == 5) else tone[(x // 6, z // 6)]
            if min(x, W - 1 - x, z, D - 1 - z) < 5: out[z, x] = 'chkBlack' if (x % 6 != 5 and z % 6 != 5) else 'groutDk'
    return out


def hall_checker(W, D, seed=5):
    """Black & cream marble checker, 10-voxel squares, 2-voxel dark edge band."""
    out = np.empty((D, W), dtype=object)
    for z in range(D):
        for x in range(W):
            out[z, x] = 'chkBlack' if ((x // 10) + (z // 10)) % 2 == 0 else 'chkCream'
            if min(x, W - 1 - x, z, D - 1 - z) < 2: out[z, x] = 'chkEdge'
    return out


def hall_terrazzo(W, D, seed=6):
    """Cream terrazzo: two-tone speckle with dark flecks, brass divider strips every 60 voxels, brass border."""
    r = _rng(seed)
    out = np.where(r.random((D, W)) > .72, 'terr2', 'terr1').astype(object)           # fine chips, not blobs
    f = r.random((D, W))
    out[f < .022] = 'terrFleck'; out[(f >= .022) & (f < .04)] = 'terrFleck2'
    for x in range(59, W, 60): out[:, x] = 'brass'
    for z in range(59, D, 60): out[z, :] = 'brass'
    for z in range(D):
        for x in range(W):
            if min(x, W - 1 - x, z, D - 1 - z) < 2: out[z, x] = 'brass'
    return out


# ============================================================ WALLS  (array[v=0 floor .. H-1, u])
def _wainscot(H, W, top=16, seed=11, stile=8):
    """Raised-panel wainscot, chibi-scaled: 3-row baseboard, framed recessed panels (colour only, so mounted
    features keep the flat face) with shadow/light inner edges, chair rail on top. Panel field sized so the
    geometry still reads at the .42m rail line."""
    out = np.empty((top, W), dtype=object)
    for v in range(top):
        for u in range(W):
            if v < 3: c = 'base'
            elif v >= top - 3: c = 'wainHi' if v == top - 3 else 'rail'
            else:
                su = u % stile
                rail_v = v < 4 or v > top - 7                               # bottom + top frame rails
                stile_u = su < 2 or su >= stile - 1
                if rail_v or stile_u: c = 'wain'
                else:
                    c = 'wainPanel'
                    if v == top - 7 or su == 2: c = 'wainLo'                # shadow along the top/left inner edge
                    elif v == 4 or su == stile - 2: c = 'wainHi'            # light catching the bottom/right edge
            out[v, u] = c
    return out


def wall_brick(H, W, seed=21, wainscot=19, course=3, run=6):
    """Common-bond face brick above oak wainscot — CHIBI-GRAIN recipe: rail at ~.48m (char lower chest /
    ~40% of a 1.2m chibi wall), 2+1 courses (.09m), 5+1 bricks (.18m), running-bond stagger, per-brick tone from 7
    shades, soot darkening toward the ceiling, occasional clinker, light tuck-pointed mortar, water-stain
    streaks. Fine repeating grain so a later wall-height reduction slices on a course boundary anywhere."""
    r = _rng(seed)
    out = np.empty((H, W), dtype=object)
    out[:wainscot] = _wainscot(H, W, wainscot, seed)
    shades = ['brk1', 'brk2', 'brk3', 'brk4', 'brk5', 'brk6', 'brk7']
    streak = vnoise(1, W, 9, seed + 3)[0]
    cache = {}
    for v in range(wainscot, H):
        c = (v - wainscot) // course
        for u in range(W):
            off = run // 2 if c % 2 else 0
            bu = (u + off) // run
            if (v - wainscot) % course == course - 1 or (u + off) % run == run - 1:
                out[v, u] = 'mortar' if (v - wainscot + u) % 17 else 'mortarDk'
                continue
            key = (c, bu)
            if key not in cache:
                base = r.random() * 3.2 + (v - wainscot) / (H - wainscot) * 2.2 + streak[(bu * run) % W] * 1.2   # soot + streaks darken
                if r.random() < .06: base += 2.0                                    # clinker
                cache[key] = int(base)
            out[v, u] = shades[min(6, cache[key])]
    return out


def wall_plaster(H, W, seed=31, wainscot=34):
    """Executive: walnut wainscot, nicotine-cream plaster with trowel noise + smoke gradient, picture rail, lighter frieze."""
    r = _rng(seed)
    out = np.empty((H, W), dtype=object)
    out[:wainscot] = _wainscot(H, W, wainscot, seed, stile=28)
    pls = ['pl1', 'pl2', 'pl3', 'pl4', 'pl5']
    blot = vnoise(H, W, 11, seed + 1); fine = r.random((H, W))
    for v in range(wainscot, H):
        for u in range(W):
            g = (v - wainscot) / (H - wainscot)
            k = int(g * 2.4 + (blot[v, u] - .5) * 1.3 + (fine[v, u] - .5) * .45 + .6)
            out[v, u] = pls[max(0, min(4, k))]
            if v >= H - 7: out[v, u] = 'frieze' if fine[v, u] > .15 else 'pl1'
    for u in range(W): out[H - 8, u] = 'base'; out[H - 9, u] = 'rail'              # picture rail
    lo = wainscot + 3; cr = r.integers(lo, max(lo + 1, H - 12), size=2)                                     # a few hairline cracks
    for c in cr:
        x = int(r.integers(8, W - 8)); y = int(c)
        for _ in range(int(r.integers(5, 9))):
            out[y, x] = 'crack'; y -= 1; x += int(r.integers(-1, 2)); x = max(0, min(W - 1, x))
    return out


def wall_ward(H, W, seed=41):
    """Ward-office: institutional green lower, cream upper, black rail stripe, dark baseboard, kick scuffs."""
    r = _rng(seed)
    out = np.empty((H, W), dtype=object)
    n = vnoise(H, W, 7, seed)
    for v in range(H):
        for u in range(W):
            if v < 5: c = 'base'
            elif v < 36:
                q = r.random(); c = 'grn2' if q < .86 else ('grn1' if q < .93 else 'grn3')   # roller-stipple paint
                if v < 9 and q > .55: c = 'grn3'                                              # grime near the floor
            elif v < 38: c = 'blackline'
            else:
                g = (v - 38) / (H - 38); q = r.random()
                c = 'cream2' if q < .84 else ('cream1' if q < .92 else 'cream3')
                if g > .65 and q > .5: c = 'cream3'                                           # smoke yellowing toward the ceiling
            out[v, u] = c
    for _ in range(int(W * .25)):                                                      # kick scuffs low on the wall
        out[int(r.integers(5, 12)), int(r.integers(0, W))] = 'grn3'
    return out


STYLES = {
    'S1': dict(title='1  BACK-ROOM BRICK - oak wainscot, face brick, oak-strip floor, quarry-tile hall',
               wall=wall_brick, floor=floor_oak_strip, hall=hall_quarry),
    'S2': dict(title='2  EXECUTIVE SUITE - walnut paneling, nicotine plaster, parquet, marble-checker hall',
               wall=wall_plaster, floor=floor_basketweave, hall=hall_checker),
    'S3': dict(title='3  WARD OFFICE - green/cream two-tone, battleship linoleum, terrazzo hall',
               wall=wall_ward, floor=floor_battleship, hall=hall_terrazzo),
}
