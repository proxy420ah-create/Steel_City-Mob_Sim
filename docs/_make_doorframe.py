# _make_doorframe.py — authors commission_doorframe.json: the standard frame for a
# leaf-only door KIT piece. Authored on the SAME .0075 grid as commission_door
# (80x107x12 leaf) so jamb/head clearances are whole voxels and the pair seals.
#   opening: x6..87 (82v = .615m, leaf .60 + .0075/side), y0..107 (108v, leaf +1v head)
#   jamb 6v thick, head 6v tall, depth 14v (.105 vs leaf .09 -> 1v reveal each face)
#   inner 1v lip of the opening, front+back faces, in Mahogany for a slim highlight
#   door STOP on the back face (z=13): 3v ring protruding into the opening behind the
#   leaf edges — real carpentry detail; kills light bleed through the clearance slits
import json, os

VS = .0075
JX, JY, JZ = 6, 108, 14              # jamb thickness / clear height / depth
OW, OH = 82, 108                     # clear opening w / h
W = JX * 2 + OW                      # 94
H = OH + JX                          # 114  (head == jamb thickness)
x0, x1 = JX, JX + OW - 1             # opening x: 6..87
yh = OH                              # head bottom: 108

voxels = []
for x in range(W):
    for y in range(H):
        for z in range(JZ):
            if x0 <= x <= x1 and y < OH:
                continue                                    # the opening
            mat = 1                                         # Walnut Dark
            edge = (x in (x0 - 1, x0, x1, x1 + 1) and y <= OH) or (x0 <= x <= x1 and y in (OH - 1, OH))
            if edge and z in (0, JZ - 1):
                mat = 2                                     # Mahogany lip around the opening, both faces
            voxels.append([x, y, z, mat])

# door stop — back face only (the closing side): ring 3v deep into the opening behind the
# leaf's edges, so the clearance slits read dark stop instead of whatever's behind the door
STOP = JZ - 1
for x in (x0, x0 + 1, x0 + 2):
    for y in range(OH):
        voxels.append([x, y, STOP, 1]); voxels.append([x1 - (x - x0), y, STOP, 1])
for x in range(x0, x1 + 1):
    for y in (OH - 3, OH - 2, OH - 1):
        voxels.append([x, y, STOP, 1])

item = {
    'name': 'commission_doorframe', 'assetType': 'furniture',
    'dims': [W, H, JZ], 'voxelSize': VS,
    'materials': [
        {'id': 1, 'name': 'Walnut Dark', 'hex': '#33200f', 'r': 51, 'g': 32, 'b': 15, 'category': 'furniture'},
        {'id': 2, 'name': 'Mahogany', 'hex': '#4a2a15', 'r': 74, 'g': 42, 'b': 21, 'category': 'furniture'},
    ],
    'voxels': voxels,
    'format': 'steelcity_item', 'version': 1,
}
out = os.path.join(os.path.dirname(__file__), '..', 'Assets', 'StreamingAssets', 'voxel_items', 'commission_doorframe.json')
json.dump(item, open(out, 'w'), separators=(',', ':'))
print('wrote', out, len(voxels), 'voxels', item['dims'])
