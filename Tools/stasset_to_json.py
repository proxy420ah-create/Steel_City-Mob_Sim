"""Convert a binary .stasset to the consolidated steelcity_stasset JSON the
HTML voxel editor imports.

Binary format (Assets/Scripts/Sim/StAssetReader.cs):
  'STAS' magic | version u8 | flags u8 | W u16 | H u16 | D u16 | reserved 4B
  then uint16 voxels in X-major order (x fastest, then y, then z).

Usage:
  python Tools/stasset_to_json.py <in.stasset> [out.json] [--type building] [--voxel-size 0.1]
"""
import json
import os
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'VoxelAssetStudio'))
from mob_materials import MOB_MATERIALS


def read_stasset(path):
    data = open(path, 'rb').read()
    assert data[:4] == b'STAS', 'not a .stasset file'
    version = data[4]
    w, h, d = struct.unpack('<HHH', data[6:12])
    vox = []
    off = 16
    for z in range(d):
        for y in range(h):
            for x in range(w):
                mid = data[off] | (data[off + 1] << 8)
                if mid:
                    vox.append([x, y, z, mid])
                off += 2
    return version, (w, h, d), vox


def main():
    src = sys.argv[1]
    dst = sys.argv[2] if len(sys.argv) > 2 and not sys.argv[2].startswith('--') else \
        os.path.splitext(src)[0] + '.json'
    asset_type = 'building'
    voxel_size = 0.1
    if '--type' in sys.argv:
        asset_type = sys.argv[sys.argv.index('--type') + 1]
    if '--voxel-size' in sys.argv:
        voxel_size = float(sys.argv[sys.argv.index('--voxel-size') + 1])

    version, dims, vox = read_stasset(src)
    used_ids = sorted({v[3] for v in vox})
    def mat_entry(m):
        info = MOB_MATERIALS.get(m, {})
        r, g, b = (int(c * 255) for c in info.get("color", (1, 1, 1))[:3])
        # Editor palette contract: r/g/b are 0-255 ints (mesh colors) + hex (swatches)
        return {"id": m, "name": info.get("name", f"ID {m}"), "r": r, "g": g, "b": b,
                "hex": '#%02x%02x%02x' % (r, g, b)}
    materials = [mat_entry(m) for m in used_ids]
    out = {
        "format": "steelcity_stasset",
        "name": os.path.splitext(os.path.basename(src))[0],
        "assetType": asset_type,
        "voxelSize": voxel_size,
        "dims": list(dims),
        "voxels": vox,
        "groups": {}, "regions": {}, "attachmentPoints": {},
        "materials": materials,
    }
    json.dump(out, open(dst, 'w'))
    print(f'v{version} {dims[0]}x{dims[1]}x{dims[2]} -> {dst}: {len(vox)} voxels, {len(used_ids)} materials')


if __name__ == '__main__':
    main()
