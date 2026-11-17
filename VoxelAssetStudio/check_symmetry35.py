import json, collections
from collections import deque

P = r"JSON Models In Progress/vehicle_490_touring.json"
d = json.load(open(P))
W, H, D = d["dims"]
vox = {(x, y, z): m for x, y, z, m in d["voxels"]}
mir = lambda x: 70 - x   # measured center: x=35 is the spine

missing, diffmat = [], []
for (x, y, z), m in vox.items():
    xm = mir(x)
    k = (xm, y, z)
    if xm < 0 or xm >= W or k not in vox:
        missing.append((x, y, z, m))
    elif vox[k] != m:
        diffmat.append((x, y, z, m, vox[k]))

print(f"mirror-missing: {len(missing)}   diff-mat: {len(diffmat)}")

def hist(items, idx, label, lo=None, hi=None):
    c = collections.Counter(i[idx] for i in items)
    if not c: return
    keys = sorted(k for k in c if (lo is None or k >= lo) and (hi is None or k <= hi))
    runs, start, prev = [], None, None
    for k in keys:
        if start is None: start = prev = k; continue
        if k != prev + 1: runs.append((start, prev)); start = k
        prev = k
    runs.append((start, prev))
    print(label + ":")
    for a, b in runs:
        print(f"  {a:3d}..{b:3d}  n={sum(c[k] for k in range(a,b+1))}")

hist(missing, 2, "missing by z")
hist(missing, 1, "missing by y")
hist(missing, 0, "missing by x")
hist(diffmat, 2, "diffmat by z")
hist(diffmat, 0, "diffmat by x")

# blob the asymmetric cells (both sides of each mismatched pair)
asym = set()
for x, y, z, m in missing:
    asym.add((x, y, z)); asym.add((mir(x), y, z))
for x, y, z, m, _ in diffmat:
    asym.add((x, y, z)); asym.add((mir(x), y, z))

seen = set(); blobs = []
for seed in list(asym):
    if seed in seen or seed[0] < 0 or seed[0] >= W: continue
    q = deque([seed]); seen.add(seed); blob = []
    while q:
        x, y, z = q.popleft(); blob.append((x, y, z))
        for dx, dy, dz in ((1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1)):
            np_ = (x+dx, y+dy, z+dz)
            if np_ in asym and np_ not in seen:
                seen.add(np_); q.append(np_)
    blobs.append(blob)

blobs.sort(key=len, reverse=True)
print(f"\nasymmetric blobs: {len(blobs)}")
for b in blobs[:25]:
    xs = [p[0] for p in b]; ys = [p[1] for p in b]; zs = [p[2] for p in b]
    mats = collections.Counter(vox.get(p) for p in b if p in vox)
    print(f"  n={len(b):5d}  x[{min(xs):2d}..{max(xs):2d}] y[{min(ys):2d}..{max(ys):2d}] z[{min(zs):3d}..{max(zs):3d}]  mats={dict(mats.most_common(5))}")
