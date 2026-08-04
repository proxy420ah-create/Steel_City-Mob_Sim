"""
Inspect .stasset city building files to diagnose black material issues.
Reports material IDs found on the top layer (roof) and ground layer (lot).
"""

import os
import sys
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from stasset_io import load_stasset
from mob_materials import MOB_MATERIALS, get_material_name

def mat_name(mat_id):
    if mat_id == 0:
        return "Air"
    return get_material_name(mat_id)

def inspect_file(filepath):
    voxels, dims, _ = load_stasset(filepath)
    w, h, d = dims
    
    print(f"\n{'='*70}")
    print(f"📄 {os.path.basename(filepath)}")
    print(f"   Dims: {w}x{h}x{d}  Total: {voxels.size:,}  Solid: {(voxels != 0).sum():,}")
    
    # Material histogram
    unique, counts = np.unique(voxels, return_counts=True)
    print(f"\n   Material histogram:")
    for u, c in sorted(zip(unique, counts), key=lambda x: -x[1]):
        if u == 0:
            continue
        name = mat_name(int(u))
        print(f"     ID={int(u):3d} ({name:25s}) count={c:6d}")
    
    # Top layer (roof) — y = h-1
    print(f"\n   TOP LAYER (y={h-1}, roof):")
    top = voxels[:, h-1, :]
    top_unique = np.unique(top)
    for u in top_unique:
        if u == 0:
            continue
        count = np.count_nonzero(top == u)
        print(f"     ID={int(u):3d} ({mat_name(int(u)):25s}) count={count}")
    
    # Also check y = h-2 (parapet level)
    if h >= 2:
        print(f"\n   LAYER y={h-2} (parapet/upper):")
        layer = voxels[:, h-2, :]
        layer_unique = np.unique(layer)
        for u in layer_unique:
            if u == 0:
                continue
            count = np.count_nonzero(layer == u)
            print(f"     ID={int(u):3d} ({mat_name(int(u)):25s}) count={count}")
    
    # Ground layer (y=0)
    print(f"\n   GROUND LAYER (y=0, lot/foundation):")
    ground = voxels[:, 0, :]
    ground_unique = np.unique(ground)
    for u in ground_unique:
        if u == 0:
            continue
        count = np.count_nonzero(ground == u)
        print(f"     ID={int(u):3d} ({mat_name(int(u)):25s}) count={count}")
    
    # Check for material IDs that have alpha=0 in the palette (would render as white via fallback)
    # or IDs that are very dark
    print(f"\n   Color check (from mob_materials.py):")
    for u in unique:
        if u == 0:
            continue
        mat_id = int(u)
        if mat_id in MOB_MATERIALS:
            color = MOB_MATERIALS[mat_id]["color"]
            brightness = (color[0] + color[1] + color[2]) / 3.0
            flag = ""
            if brightness < 0.15:
                flag = " ⚠️ VERY DARK"
            elif color[3] < 0.5:
                flag = " ⚠️ LOW ALPHA"
            print(f"     ID={mat_id:3d} ({mat_name(mat_id):25s}) color=({color[0]:.2f},{color[1]:.2f},{color[2]:.2f},{color[3]:.2f}) brightness={brightness:.2f}{flag}")
        else:
            print(f"     ID={mat_id:3d} (UNKNOWN) ⚠️ NOT IN PALETTE")


def main():
    base_dir = os.path.dirname(os.path.abspath(__file__))
    stasset_dir = os.path.join(
        os.path.dirname(base_dir),
        "Steel_City-Mob_Sim", "Assets", "StreamingAssets", "voxel_buildings"
    )
    
    if not os.path.isdir(stasset_dir):
        print(f"❌ Directory not found: {stasset_dir}")
        return
    
    # Inspect key files: apartment block, apartments, barber, empty land
    targets = [
        "apartment_block_0.stasset",
        "apartments_0.stasset",
        "barber_0.stasset",
    ]
    
    # Also find empty_land files
    for f in os.listdir(stasset_dir):
        if "empty_land" in f or "courtyard" in f:
            targets.append(f)
    
    for fname in targets:
        fpath = os.path.join(stasset_dir, fname)
        if os.path.exists(fpath):
            inspect_file(fpath)
        else:
            print(f"\n❌ {fname}: NOT FOUND at {fpath}")
    
    print(f"\n{'='*70}")
    print("Done. Check material IDs on roof/ground layers above.")
    print("If roof material has brightness < 0.15, it will appear black under lighting.")


if __name__ == "__main__":
    main()
