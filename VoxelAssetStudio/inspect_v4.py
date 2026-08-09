import sys
sys.path.insert(0, '.')
from stasset_io import load_stasset
import numpy as np

v, d, s = load_stasset('../My project/Assets/StreamingAssets/ActorSymmetricV4.stasset')
print(f'Dims: {d}, Voxels: {v.size}, Non-zero: {np.count_nonzero(v)}')
if s:
    print(f'Bones: {len(s.get("bones", []))}')
    print(f'Joints: {len(s.get("joints", []))}')
    print('\nJoints:')
    for j in s.get('joints', []):
        print(f'  {j["id"]}: {j["name"]} (type={j.get("type","?")})')
    print('\nBones:')
    for b in s.get('bones', []):
        print(f'  {b["id"]}: {b["name"]} (role={b.get("role","?")}, chain={b.get("chain","?")})')
else:
    print('NO SKELETON')
