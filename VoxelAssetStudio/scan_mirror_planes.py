import json

P = r"JSON Models In Progress/vehicle_490_touring.json"
d = json.load(open(P))
W, H, D = d["dims"]
vox = {(x, y, z): m for x, y, z, m in d["voxels"]}

# x-extent of the model
xs = [x for x, y, z in vox]
print(f"x range: {min(xs)}..{max(xs)}  (W={W}, naive mirror plane would be {(W-1)/2})")

# Scan candidate mirror planes c (x mirrors to 2c - x)
for c2 in range(60, 82):  # c in 30.0 .. 40.5  (2c integer)
    c = c2 / 2
    miss = diff = 0
    for (x, y, z), m in vox.items():
        xm = int(2 * c) - x
        if xm < 0 or xm >= W:
            miss += 1; continue
        k = (xm, y, z)
        if k not in vox: miss += 1
        elif vox[k] != m: diff += 1
    print(f"c={c:5.1f}  missing={miss:6d}  diffmat={diff:5d}  total={miss+diff:6d}")
