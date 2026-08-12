"""
export_for_unity.py — Convert character_animator.html JSON exports into the
binary .stasset / .groups files Unity's StAssetReader / VoxelChunkManager
expect, plus copy the .anim.json alongside them.

Binary format (confirmed against Assets/Scripts/Sim/StAssetReader.cs):
  Header (16 bytes): magic(4) + version(1) + flags(1) + width(u16) +
                      height(u16) + depth(u16) + reserved(4)
  Body: width*height*depth uint16 values, X-fastest order (x, then y, then z)
  .stasset magic = b"STAS" (voxel material IDs)
  .groups  magic = b"STAG" (animation groupIDs, 0 = body/no group)

Usage:
    python export_for_unity.py <name> --stasset path/to/stasset.json \
        --groups path/to/groups.json --anim path/to/anim.json \
        [--out-dir DIR]

Any of --stasset/--groups/--anim may be omitted if you're only updating one
file for a test asset. --out-dir defaults to the project's
Assets/StreamingAssets/voxel_buildings folder (both copies are written when
run from within VoxelAssetStudio's default project layout).
"""
import argparse
import json
import struct
import sys
from pathlib import Path

HEADER_FMT = "<4sBBHHH4x"  # magic, version, flags, w, h, d, 4 bytes reserved
HEADER_SIZE = 16


def write_binary_grid(magic: bytes, dims, values_by_coord, out_path: Path, default=0):
    """values_by_coord: dict {(x,y,z): value} OR a full list already in x-fastest order."""
    w, h, d = dims
    total = w * h * d

    if isinstance(values_by_coord, dict):
        flat = [default] * total
        for (x, y, z), val in values_by_coord.items():
            if not (0 <= x < w and 0 <= y < h and 0 <= z < d):
                raise ValueError(f"Voxel coord out of bounds: {(x, y, z)} for dims {dims}")
            idx = x + y * w + z * w * h
            flat[idx] = val
    else:
        flat = list(values_by_coord)
        if len(flat) != total:
            raise ValueError(f"Expected {total} values for dims {dims}, got {len(flat)}")

    header = struct.pack(HEADER_FMT, magic, 1, 0, w, h, d)
    body = struct.pack(f"<{total}H", *[v & 0xFFFF for v in flat])

    out_path.parent.mkdir(parents=True, exist_ok=True)
    with open(out_path, "wb") as f:
        f.write(header)
        f.write(body)
    print(f"  wrote {out_path} ({HEADER_SIZE + len(body)} bytes, {w}x{h}x{d} = {total} voxels)")


def convert_stasset(json_path: Path, out_path: Path):
    data = json.loads(json_path.read_text())
    dims = tuple(data["dims"])  # [w, h, d]
    voxels = data["voxels"]  # list of [x, y, z, materialId]

    coord_map = {}
    for v in voxels:
        x, y, z, mid = v
        coord_map[(x, y, z)] = mid

    write_binary_grid(b"STAS", dims, coord_map, out_path, default=0)


def convert_groups(json_path: Path, out_path: Path, dims_override=None):
    data = json.loads(json_path.read_text())
    dims = tuple(data.get("dims") or dims_override)
    if dims is None:
        raise ValueError("groups JSON has no 'dims' field — pass --stasset too so dims can be inferred")

    raw_groups = data["groups"]
    coord_map = {}
    if isinstance(raw_groups, dict):
        # exportGroupsJSON() format from character_animator.html: {"x,y,z": gid}
        for key, gid in raw_groups.items():
            x, y, z = (int(p) for p in key.split(","))
            coord_map[(x, y, z)] = gid
    else:
        # array-of-tuples format: [x, y, z, gid]
        for x, y, z, gid in raw_groups:
            coord_map[(x, y, z)] = gid

    write_binary_grid(b"STAG", dims, coord_map, out_path, default=0)


def copy_anim(json_path: Path, out_path: Path):
    data = json.loads(json_path.read_text())
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(data, indent=2))
    print(f"  wrote {out_path}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("name", help="Output asset base name, e.g. character_test_vehicle")
    ap.add_argument("--stasset", type=Path, help="Path to stasset JSON export (dims + voxels)")
    ap.add_argument("--groups", type=Path, help="Path to groups JSON export (dims + groups)")
    ap.add_argument("--anim", type=Path, help="Path to .anim.json export")
    ap.add_argument("--out-dir", type=Path, default=None,
                    help="Output directory (default: ../Assets/StreamingAssets/voxel_buildings relative to this script)")
    args = ap.parse_args()

    script_dir = Path(__file__).resolve().parent
    out_dir = args.out_dir or (script_dir.parent / "Assets" / "StreamingAssets" / "voxel_buildings")

    print(f"Exporting '{args.name}' -> {out_dir}")

    stasset_dims = None
    if args.stasset:
        data = json.loads(args.stasset.read_text())
        stasset_dims = tuple(data["dims"])
        convert_stasset(args.stasset, out_dir / f"{args.name}.stasset")

    if args.groups:
        convert_groups(args.groups, out_dir / f"{args.name}.groups", dims_override=stasset_dims)

    if args.anim:
        copy_anim(args.anim, out_dir / f"{args.name}.anim.json")

    if not (args.stasset or args.groups or args.anim):
        print("Nothing to do — pass at least one of --stasset / --groups / --anim", file=sys.stderr)
        sys.exit(1)

    print("Done.")


if __name__ == "__main__":
    main()
