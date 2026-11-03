# Bake: Civilian1 (live char, 96-grid @ .01) seated into commission_chair (52-vox @ .01)
# Authored state-11 sit params + authored pivots from the character file itself.
import json, io, math, shutil

ROOT = r'C:\Users\NADECC\ATSTradingDashboard Project\Cursor Workshop\SteelCityMobSim'
CH = json.loads(io.open(ROOT + r'\Assets\StreamingAssets\voxel_items\commission_chair.json', encoding='utf-8').read())
CV = json.loads(io.open(ROOT + r'\Assets\StreamingAssets\voxel_characters\Civilian1.json', encoding='utf-8').read())

# --- authored sit (character_animator state 11) ---
LEG_STRIDE = 1.45
KNEE_BEND  = 1.45
ARM_SWING  = 0.2

D = CV['dims']  # 96^3
pv = {int(k): (v['x']*D[0], v['y']*D[1], v['z']*D[2]) for k, v in CV['pivots'].items()}
groups = CV['groups']  # 'x,y,z' -> gid

def rotX(p, pivot, a):  # y-z plane rotation (forward/back swing)
    y, z = p[1]-pivot[1], p[2]-pivot[2]
    c, s = math.cos(a), math.sin(a)
    return (p[0], pivot[1]+y*c-z*s, pivot[2]+y*s+z*c)

def rotZ(p, pivot, a):  # x-y plane rotation (T-pose -> arms down)
    x, y = p[0]-pivot[0], p[1]-pivot[1]
    c, s = math.cos(a), math.sin(a)
    return (pivot[0]+x*c-y*s, pivot[1]+x*s+y*c, p[2])

A = -LEG_STRIDE  # thigh world angle (forward swing)

def pose(p, gid):
    if gid in (4, 5):            # thighs: rotate about own hip pivot
        return rotX(p, pv[gid], A)
    if gid in (6, 7):            # shins: FK — hip rotation, then knee-bend about moved knee
        hip = pv[gid-2]
        p1 = rotX(p, hip, A)
        knee1 = rotX(pv[gid], hip, A)
        return rotX(p1, knee1, KNEE_BEND)
    if gid in (2, 3):            # upper arms: T->down via rest Z-rot, slight fwd swing
        rest = -math.pi/2 if gid == 2 else math.pi/2   # right arm +x -> -90° brings it DOWN
        p1 = rotZ(p, pv[gid], rest)
        return rotX(p1, pv[gid], ARM_SWING)
    if gid in (8, 9):            # forearms/hands: inherit shoulder transform (elbow 0)
        sh = pv[2] if gid == 8 else pv[3]
        rest = -math.pi/2 if gid == 8 else math.pi/2
        p1 = rotZ(p, sh, rest)
        return rotX(p1, sh, ARM_SWING)
    return p                     # 0 torso, 1 head: upright, static

posed = {}
for v in CV['voxels']:
    x, y, z, m = v
    gid = groups.get('%d,%d,%d' % (x, y, z), 0)
    nx, ny, nz = pose((x, y, z), gid)
    posed[(round(nx), round(ny), round(nz))] = m

xs = [p[0] for p in posed]; ys = [p[1] for p in posed]; zs = [p[2] for p in posed]
print('posed civ bounds: x %d..%d  y %d..%d  z %d..%d' % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))

# --- facing: toes (lowest-band voxels) skew toward +z => faces +z; flip 180 so he faces -z ---
low = [p[2] for p in posed if p[1] <= min(ys)+6]
print('feet z mean %.1f vs body z %.1f' % (sum(low)/len(low), (min(zs)+max(zs))/2))
W = D[0]
flipped = {(W-1-x, y, 95-z): m for (x, y, z), m in posed.items()}   # 180° about Y

# --- seat alignment: pelvis (gid-0 low band ~ y30..36 authored) rests on seat top ---
# chair seat slab top face measured: y26; backrest inner face ~z22 (back at z23-27)
xs = [p[0] for p in flipped]; ys = [p[1] for p in flipped]; zs = [p[2] for p in flipped]
butt_y = min(p[1] for p in flipped if abs(p[0]-(min(xs)+max(xs))/2) < 8 and p[2] > (min(zs)+max(zs))/2)
back_z = max(zs)
dx = 14 - (min(xs)+max(xs))//2          # center on chair (dims 29 -> center 14)
dy = 26 - butt_y                        # pelvis underside on seat top (y26)
dz = 22 - back_z                        # back snug against backrest inner face
print('shift dx=%d dy=%d dz=%d (butt_y=%d back_z=%d)' % (dx, dy, dz, butt_y, back_z))

# --- merge: chair voxels + civ (materials appended, ids offset) ---
mats = list(CH['materials'])
off = len(mats)
mats += CV['materials']
merged = [v if isinstance(v, list) else [v['x'], v['y'], v['z'], v['matId'] if 'matId' in v else v.get('m', 0)] for v in CH['voxels']]
cxs = [x+dx for (x, y, z) in flipped]
padx = max(0, -min(cxs))             # hands overhang chair edges — pad, don't clip
xmax = max(28+padx, max(cxs)+padx)   # right edge: chair or char, whichever reaches
czs = [z+dz for (x, y, z) in flipped]
padz = max(0, -min(czs))             # dangling feet overhang the front face too
zmax = max(28+padz, max(czs)+padz)
top = 52
for (x, y, z), m in flipped.items():
    nx, ny, nz = x+dx+padx, y+dy, z+dz+padz
    if ny >= top: top = ny+1
    merged.append([nx, ny, nz, m+off])
for v in merged[:len(CH['voxels'])]:
    v[0] += padx; v[2] += padz       # recentre chair under him in padded grid
out = dict(CH)
out['name'] = 'vinny_seated_preview'
out['voxelSize'] = 0.01
out['dims'] = [xmax+1, top, zmax+1]  # head + hands + feet clear chair grid — honest bounds
out['materials'] = mats
out['voxels'] = merged
out.pop('attachmentPoints', None)
dst = ROOT + r'\Assets\StreamingAssets\voxel_items\vinny_seated_preview.json'
io.open(dst, 'w', encoding='utf-8').write(json.dumps(out))
shutil.copy(dst, ROOT + r'\docs\vinny_seated_preview.json')
xs = [v[0] for v in merged]; ys = [v[1] for v in merged]; zs = [v[2] for v in merged]
print('merged %d voxels, bounds x %d..%d y %d..%d z %d..%d' % (len(merged), min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
