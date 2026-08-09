"""Mirror fire escape from back-left to back-right in firework3.json.

Front door is at Z=0. When facing the building from front:
  Left  = high X (current FE at X=59-85)
  Right = low X (target: X=10-36)

Mirror: new_x = W - 1 - old_x = 95 - old_x
Only mirror FE voxels (DARK_IRON=109, PAINTED_METAL=111).
"""
import json
import numpy as np

with open(r'C:\Users\NADECC\Downloads\firework3.json') as f:
    data = json.load(f)

w, h, d = data['dims']
DARK_IRON = 109
PAINTED_METAL = 111
FE_MATS = {DARK_IRON, PAINTED_METAL}

new_voxels = []
mirrored = 0
for x, y, z, mid in data['voxels']:
    if mid in FE_MATS:
        nx = w - 1 - x  # mirror X
        new_voxels.append([nx, y, z, mid])
        mirrored += 1
    else:
        new_voxels.append([x, y, z, mid])

print(f"Mirrored {mirrored} FE voxels across X axis")

# Verify new positions
fe = [v for v in new_voxels if v[3] in FE_MATS]
fe_ys = [v[1] for v in fe]
from collections import Counter
yc = Counter(fe_ys)
avg = np.mean(list(yc.values()))
landings = sorted([y for y, c in yc.items() if c > avg * 1.5])
print(f"Landing Ys: {landings}")
for ly in landings:
    xs = [v[0] for v in fe if v[1] == ly]
    zs = [v[2] for v in fe if v[1] == ly]
    print(f"  Y={ly}: X={min(xs)}-{max(xs)} Z={min(zs)}-{max(zs)}")

out_data = {
    'dims': data['dims'],
    'materials': data['materials'],
    'voxels': new_voxels
}
out_path = r'C:\Users\NADECC\Downloads\firework3_mirrored.json'
with open(out_path, 'w') as f:
    json.dump(out_data, f)
print(f"\nSaved: {out_path}")

# Load into editor
import voxel_editor_html as veh
ROOF_BUF = 8
new_h = h + ROOF_BUF
grid = np.zeros((w, new_h, d), dtype=np.uint16)
for x, y, z, mid in new_voxels:
    if 0 <= x < w and 0 <= y < h and 0 <= z < d:
        grid[x, y, z] = mid
veh.voxel_to_editor(grid, (w, new_h, d), "voxel_editor.html",
                    title="Firework3 - FE Mirrored to Back-Right")
print(f"Saved: voxel_editor.html ({w}x{new_h}x{d})")
