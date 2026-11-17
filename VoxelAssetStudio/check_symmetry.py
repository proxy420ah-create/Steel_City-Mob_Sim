import json, collections

P = r"JSON Models In Progress/vehicle_490_touring.json"
d = json.load(open(P))
W, H, D = d["dims"]
vox = {(x, y, z): m for x, y, z, m in d["voxels"]}
print(f"dims={d['dims']} voxels={len(vox)}")

mir = lambda x: W - 1 - x

missing = []   # voxel exists, mirror empty
diffmat = []   # voxel exists, mirror exists but different material
for (x, y, z), m in vox.items():
    k = (mir(x), y, z)
    if k not in vox:
        missing.append((x, y, z, m))
    elif vox[k] != m:
        diffmat.append((x, y, z, m, vox[k]))

print(f"mirror-missing: {len(missing)}  mirror-different-material: {len(diffmat)}")

# Where are they?  histogram by z (length axis) and by y (height axis)
def hist(items, idx, label):
    c = collections.Counter(i[idx] for i in items)
    if not c: return
    print(f"\n{label} (min..max shown):")
    keys = sorted(c)
    # compress runs
    runs = []
    start = prev = keys[0]
    for k in keys[1:]:
        if k != prev + 1:
            runs.append((start, prev))
            start = k
        prev = k
    runs.append((start, prev))
    for a, b in runs:
        n = sum(c[k] for k in range(a, b + 1))
        print(f"  {label[0]}={a:3d}..{b:3d}  count={n}")

hist(missing, 2, "missing by z")
hist(missing, 1, "missing by y")
hist(diffmat, 2, "diffmat by z")

# Asymmetric pairs: dedupe (a missing voxel's mirror-empty is one side of the pair)
# Cluster the missing voxels into contiguous blobs to identify FEATURES
asym = set((x, y, z) for x, y, z, m in missing)
asym |= set((mir(x), y, z) for x, y, z, m in missing)  # both sides of each asymmetric pair
seen = set()
blobs = []
from collections import deque
for seed in list(asym):
    if seed in seen: continue
    q = deque([seed]); seen.add(seed); blob = []
    while q:
        p = q.popleft(); blob.append(p)
        x, y, z = p
        for dx, dy, dz in ((1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1)):
            np_ = (x+dx, y+dy, z+dz)
            if np_ in asym and np_ not in seen:
                seen.add(np_); q.append(np_)
    blobs.append(blob)

blobs.sort(key=len, reverse=True)
print(f"\nasymmetric blobs: {len(blobs)}")
for b in blobs[:15]:
    xs = [p[0] for p in b]; ys = [p[1] for p in b]; zs = [p[2] for p in b]
    mats = collections.Counter(vox.get(p) for p in b if p in vox)
    print(f"  n={len(b):5d}  x[{min(xs)}..{max(xs)}] y[{min(ys)}..{max(ys)}] z[{min(zs)}..{max(zs)}]  mats={dict(mats.most_common(4))}")
