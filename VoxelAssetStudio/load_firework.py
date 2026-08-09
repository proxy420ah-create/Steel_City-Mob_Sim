"""Load firework1.json into the voxel editor for preview."""
import json
import numpy as np
import sys, os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import voxel_editor_html as veh

with open(r'C:\Users\NADECC\Downloads\firework3.json') as f:
    data = json.load(f)

w, h, d = data['dims']
# Expand height to give 8 voxels of roof buffer for decoration
ROOF_BUF = 8
new_h = h + ROOF_BUF
grid = np.zeros((w, new_h, d), dtype=np.uint16)
for x, y, z, mid in data['voxels']:
    if 0 <= x < w and 0 <= y < h and 0 <= z < d:
        grid[x, y, z] = mid

print(f"Loaded: {w}x{h}x{d} -> expanded to {w}x{new_h}x{d} (+{ROOF_BUF} roof buffer)")
print(f"  {len(data['voxels'])} voxels, {len(data['materials'])} materials")

# Check landing positions
from collections import Counter
ys = [v[1] for v in data['voxels']]
y_counts = Counter(ys)
avg_count = np.mean(list(y_counts.values()))
landings = sorted([y for y, c in y_counts.items() if c > avg_count * 2])
print(f"Landing Y levels: {landings}")

# Check for DARK_IRON (109) voxels - fire escape
fe_voxels = [v for v in data['voxels'] if v[3] == 109]
print(f"Dark Iron voxels: {len(fe_voxels)}")
if fe_voxels:
    fe_ys = [v[1] for v in fe_voxels]
    fe_y_counts = Counter(fe_ys)
    fe_avg = np.mean(list(fe_y_counts.values()))
    fe_landings = sorted([y for y, c in fe_y_counts.items() if c > fe_avg * 2])
    print(f"FE landing Y levels: {fe_landings}")

veh.voxel_to_editor(grid, (w, new_h, d), "voxel_editor.html", title="Firework1 - Roof Buffer")
print(f"Saved: voxel_editor.html ({w}x{new_h}x{d})")
