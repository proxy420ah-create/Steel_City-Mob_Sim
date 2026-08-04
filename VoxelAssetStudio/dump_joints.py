import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from stasset_io import load_stasset

asset_path = sys.argv[1] if len(sys.argv) > 1 else '../My project/Assets/StreamingAssets/ActorSymmetric.stasset'
voxels, dims, skel = load_stasset(asset_path)

print("\n=== JOINTS ===")
for j in skel.get('joints', []):
    print(f"  joint[{j['id']}] type={j.get('type','?')} axis={j.get('axis',[])} "
          f"min={j.get('min_angle','?')} max={j.get('max_angle','?')} "
          f"maxX={j.get('max_angle_x','?')} maxY={j.get('max_angle_y','?')} maxZ={j.get('max_angle_z','?')}")

print("\n=== BONES ===")
for b in skel.get('bones', []):
    print(f"  bone[{b['id']}] {b['name']:20s} role={b.get('role','?'):10s} side={b.get('side','?'):5s} "
          f"parent={b.get('parent_joint','?')} child={b.get('child_joint','?')}")

print(f"\nroot_joint = {skel.get('root_joint', '?')}")
