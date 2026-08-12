#!/usr/bin/env python3
"""
export_character_all.py — All-in-one character exporter.

Takes a character_animator.html "Save Project" JSON (or a voxel editor JSON
with dims + voxels + groups) and produces ALL THREE Unity files in one shot:
  1. <name>.stasset   — binary voxel data (STAS format)
  2. <name>.groups     — binary groupID data (STAG format)
  3. <name>.anim.json  — animation parameters (pivots + params + states)

Also works with the voxel editor's JSON format (dims + voxels as [x,y,z,mid]
arrays + groups as {"x,y,z": gid} dict), even if no animParams are present
(in that case, .anim.json is skipped).

Usage:
    python export_character_all.py <input_project.json> <output_name> [--out-dir DIR]
    python export_character_all.py "JSON Models In Progress/voxel_Rig2.json" character_rig2

    [--out-dir] defaults to ../Assets/StreamingAssets/voxel_characters
"""

import json
import struct
import sys
from pathlib import Path

HEADER_FMT = "<4sBBHHH4x"  # magic, version, flags, w, h, d, 4 bytes reserved
HEADER_SIZE = 16


def write_binary_grid(magic: bytes, dims, values_by_coord, out_path: Path, default=0):
    """Write a binary .stasset or .groups file from a {(x,y,z): value} dict."""
    w, h, d = dims
    total = w * h * d
    flat = [default] * total
    for (x, y, z), val in values_by_coord.items():
        if not (0 <= x < w and 0 <= y < h and 0 <= z < d):
            raise ValueError(f"Voxel coord out of bounds: {(x, y, z)} for dims {dims}")
        idx = x + y * w + z * w * h
        flat[idx] = val

    header = struct.pack(HEADER_FMT, magic, 1, 0, w, h, d)
    body = struct.pack(f"<{total}H", *[v & 0xFFFF for v in flat])

    out_path.parent.mkdir(parents=True, exist_ok=True)
    with open(out_path, "wb") as f:
        f.write(header)
        f.write(body)
    print(f"  ✅ {out_path.name} ({HEADER_SIZE + len(body):,} bytes, {w}x{h}x{d} = {total} voxels)")


def parse_voxels(voxels_data):
    """Parse voxels from either [x,y,z,mid] arrays or {x,y,z,m} objects."""
    coord_map = {}
    if isinstance(voxels_data, list):
        for v in voxels_data:
            if isinstance(v, list):
                x, y, z, mid = v
                if mid != 0:
                    coord_map[(x, y, z)] = mid
            elif isinstance(v, dict):
                x, y, z, mid = v.get('x', v.get(0)), v.get('y', v.get(1)), v.get('z', v.get(2)), v.get('m', v.get('mid', v.get(3)))
                if mid != 0:
                    coord_map[(x, y, z)] = mid
    return coord_map


def parse_groups(groups_data):
    """Parse groups from either {"x,y,z": gid} dict or [[x,y,z,gid]] array."""
    coord_map = {}
    if isinstance(groups_data, dict):
        for key, gid in groups_data.items():
            parts = key.split(',')
            if len(parts) == 3:
                x, y, z = int(parts[0]), int(parts[1]), int(parts[2])
                coord_map[(x, y, z)] = gid
    elif isinstance(groups_data, list):
        for entry in groups_data:
            if isinstance(entry, list) and len(entry) >= 4:
                x, y, z, gid = entry[0], entry[1], entry[2], entry[3]
                coord_map[(x, y, z)] = gid
    return coord_map


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)

    input_path = Path(sys.argv[1])
    output_name = sys.argv[2]

    # Determine output directory
    script_dir = Path(__file__).resolve().parent
    if len(sys.argv) > 4 and sys.argv[3] == '--out-dir':
        out_dir = Path(sys.argv[4])
    else:
        out_dir = script_dir.parent / "Assets" / "StreamingAssets" / "voxel_characters"

    print(f"📦 Exporting '{input_path.name}' → '{output_name}' in {out_dir}")
    print()

    # Load JSON
    with open(input_path, 'r') as f:
        data = json.load(f)

    dims = tuple(data['dims'])
    print(f"  Dimensions: {dims[0]}x{dims[1]}x{dims[2]}")

    # --- 1. Export .stasset (voxel data) ---
    voxel_coords = parse_voxels(data.get('voxels', []))
    if not voxel_coords:
        print("  ❌ No voxel data found!")
        sys.exit(1)
    print(f"  Non-air voxels: {len(voxel_coords)}")

    stasset_path = out_dir / f"{output_name}.stasset"
    write_binary_grid(b"STAS", dims, voxel_coords, stasset_path, default=0)

    # --- 2. Export .groups (groupID data) ---
    group_coords = parse_groups(data.get('groups', {}))
    if group_coords:
        print(f"  Group assignments: {len(group_coords)}")
        groups_path = out_dir / f"{output_name}.groups"
        write_binary_grid(b"STAG", dims, group_coords, groups_path, default=0)
    else:
        print("  ⚠️  No group data found — skipping .groups file")

    # --- 3. Export .anim.json (animation parameters) ---
    anim_params = data.get('animParams')
    pivots = data.get('pivots')

    if anim_params or pivots:
        anim_data = {
            'format': 'anim_params',
            'version': 1,
            'pivots': pivots or {},
            'params': anim_params or {},
        }
        anim_path = out_dir / f"{output_name}.anim.json"
        anim_path.parent.mkdir(parents=True, exist_ok=True)
        with open(anim_path, 'w') as f:
            json.dump(anim_data, f, indent=2)
        print(f"  ✅ {anim_path.name} ({anim_path.stat().st_size:,} bytes)")
        if pivots:
            print(f"     Pivots: {len(pivots)} entries")
        if anim_params:
            sections = [k for k in anim_params.keys() if anim_params[k] is not None]
            print(f"     Param sections: {sections}")
    else:
        print("  ⚠️  No animParams/pivots found — skipping .anim.json")
        print("     (Character will render as T-Pose only in ForwardTransformTestRig)")

    print()
    print(f"✅ Done! Set assetBaseName = \"{output_name}\" on ForwardTransformTestRig")


if __name__ == "__main__":
    main()
