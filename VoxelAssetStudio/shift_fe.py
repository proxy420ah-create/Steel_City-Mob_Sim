"""Shift fire escape landings down by 2 to align with tenement window sills.
Windows: Y=12-16, 20-24, 28-32, 34-38
Original landings: Y=14, 23, 31, 40
Shifted -2:        Y=12, 21, 29, 38
"""
import json
import numpy as np
import sys, os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import voxel_editor_html as veh

SHIFT = -2

with open(r'C:\Users\NADECC\Downloads\Fireescape_test.json') as f:
    data = json.load(f)

# Shift all voxel Y values
new_voxels = []
for x, y, z, mid in data['voxels']:
    ny = y + SHIFT
    if ny >= 0:
        new_voxels.append([x, ny, z, mid])
    # else: skip voxels that go below Y=0

data['voxels'] = new_voxels
print(f"Shifted {len(new_voxels)} voxels down by {abs(SHIFT)}")

# Check new landing positions
from collections import Counter
ys = [v[1] for v in new_voxels]
y_counts = Counter(ys)
avg_count = np.mean(list(y_counts.values()))
landings = sorted([y for y, c in y_counts.items() if c > avg_count * 2])
print(f"New landing Y levels: {landings}")
print(f"Tenement window sills: [12, 20, 28, 36]")
print(f"Tenement window tops:  [16, 24, 32, 40]")

# Save shifted JSON
out_json = r'C:\Users\NADECC\Downloads\Fireescape_test_shifted.json'
with open(out_json, 'w') as f:
    json.dump(data, f)
print(f"Saved: {out_json}")

# Load into editor
dims = data['dims']
w, h, d = dims
grid = np.zeros((w, h, d), dtype=np.uint16)
for x, y, z, mid in new_voxels:
    if 0 <= x < w and 0 <= y < h and 0 <= z < d:
        grid[x, y, z] = mid

veh.voxel_to_editor(grid, (w, h, d), "voxel_editor.html", title="Fire Escape - Shifted")
print("Saved: voxel_editor.html")
