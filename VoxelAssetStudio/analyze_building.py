"""Building model analyzer — scans voxel grids and reports protrusion/buffer needs.

Usage:
    python analyze_building.py <building_type>
    python analyze_building.py tenement_block
    python analyze_building.py --all
"""
import sys
import numpy as np
from procedural_mob_buildings import generate_building, BUILDING_GENERATORS

MATERIAL_NAMES = {
    0: "Air",
    100: "RedBrick", 101: "DarkWood", 102: "Stone", 103: "Cobblestone",
    104: "Tar", 105: "WeatheredWood", 106: "WindowGlass", 107: "StorefrontGlass",
    108: "Stucco", 109: "DarkIron", 110: "PaintedBrown",
    111: "PaintedMetal", 112: "StorefrontGlass", 114: "Stone",
    118: "Tar", 120: "PaintedRed", 122: "WeatheredWood",
}

DECORATION_MATERIALS = {
    109: "DarkIron",       # Fire escapes, railings
    111: "PaintedMetal",   # Cornices, balconies, columns
    120: "PaintedRed",     # Awnings, canopies
    102: "Stone",          # Steps
    105: "WeatheredWood",  # Water tower
    122: "WeatheredWood",  # Water tower
}


def analyze_building(btype, seed=42):
    """Analyze a building type and return a detailed report."""
    grid, dims, meta = generate_building(btype, seed=seed)
    w, h, d = dims

    print(f"\n{'='*60}")
    print(f"  BUILDING ANALYSIS: {btype}")
    print(f"{'='*60}")
    print(f"  Dimensions: {w}x{h}x{d} (W x H x D)")
    print(f"  Total voxels: {w*h*d:,}")
    print(f"  Non-air: {np.count_nonzero(grid):,} ({100*np.count_nonzero(grid)/(w*h*d):.1f}%)")
    print(f"  Meta: {meta}")

    # --- Per-face analysis ---
    print(f"\n  {'─'*56}")
    print(f"  FACE ANALYSIS (protrusion detection)")
    print(f"  {'─'*56}")

    faces = [
        ("Front (-Z)", grid[:, :, 0],  "z=0"),
        ("Back  (+Z)", grid[:, :, d-1], f"z={d-1}"),
        ("Left  (-X)", grid[0, :, :],  "x=0"),
        ("Right (+X)", grid[w-1, :, :], f"x={w-1}"),
    ]

    for name, face_slice, coord in faces:
        non_air = np.count_nonzero(face_slice)
        mats = np.unique(face_slice, return_counts=True)
        mat_info = ", ".join(
            f"{MATERIAL_NAMES.get(m, f'mat{m}')}:{c}"
            for m, c in zip(*mats) if m != 0
        )
        print(f"\n  {name} ({coord}):")
        print(f"    Non-air voxels: {non_air}")
        if mat_info:
            print(f"    Materials: {mat_info[:80]}")
            # Check for decoration materials
            decor_mats = {
                m: c for m, c in zip(*mats)
                if m in DECORATION_MATERIALS and m != 0
            }
            if decor_mats:
                print(f"    *** DECORATION DETECTED ***")
                for m, c in decor_mats.items():
                    print(f"      {MATERIAL_NAMES.get(m, f'mat{m}')}: {c} voxels")
        else:
            print(f"    (empty — no protrusion)")

    # --- Buffer zone analysis ---
    print(f"\n  {'─'*56}")
    print(f"  BUFFER ZONE ANALYSIS (outer 2-voxel shell)")
    print(f"  {'─'*56}")

    # Check each 1-voxel layer from the edge inward
    for face_name, axis, direction in [
        ("Front", 2, "neg"), ("Back", 2, "pos"),
        ("Left", 0, "neg"), ("Right", 0, "pos"),
    ]:
        print(f"\n  {face_name} layers:")
        for layer in range(5):
            if direction == "neg":
                if axis == 2:
                    slc = grid[:, :, layer]
                else:
                    slc = grid[layer, :, :]
            else:
                if axis == 2:
                    slc = grid[:, :, d-1-layer]
                else:
                    slc = grid[w-1-layer, :, :]

            non_air = np.count_nonzero(slc)
            decor_count = sum(
                np.count_nonzero(slc == m) for m in DECORATION_MATERIALS
            )
            marker = " <<<" if decor_count > 0 else ""
            print(f"    layer {layer}: {non_air:6d} non-air, {decor_count:4d} decoration{marker}")

    # --- Material summary ---
    print(f"\n  {'─'*56}")
    print(f"  MATERIAL SUMMARY")
    print(f"  {'─'*56}")
    all_mats = np.unique(grid, return_counts=True)
    for m, c in sorted(zip(*all_mats), key=lambda x: -x[1]):
        if m == 0:
            continue
        name = MATERIAL_NAMES.get(m, f"mat{m}")
        pct = 100 * c / (w * h * d)
        print(f"    {name:20s}: {c:8,} ({pct:5.1f}%)")

    # --- Protrusion recommendations ---
    print(f"\n  {'─'*56}")
    print(f"  PROTRUSION RECOMMENDATIONS")
    print(f"  {'─'*56}")

    # Find the deepest decoration protrusion on each face
    # Only count layers where decoration exists but wall structure does NOT
    # (i.e., the layer is mostly air — it's a protrusion, not the wall itself)
    for face_name, axis, direction in [
        ("Front", 2, "neg"), ("Back", 2, "pos"),
        ("Left", 0, "neg"), ("Right", 0, "pos"),
    ]:
        max_depth = 0
        size = w if axis == 0 else d
        for layer in range(min(6, size)):  # only scan buffer zone (4v buffer + 2v wall)
            if direction == "neg":
                if axis == 2:
                    slc = grid[:, :, layer]
                else:
                    slc = grid[layer, :, :]
            else:
                if axis == 2:
                    slc = grid[:, :, d-1-layer]
                else:
                    slc = grid[w-1-layer, :, :]

            total_non_air = np.count_nonzero(slc)
            decor_count = sum(
                np.count_nonzero(slc == m) for m in DECORATION_MATERIALS
            )
            # A protrusion layer is one where decoration exists but the
            # layer is mostly air (< 30% filled = not a wall layer)
            is_protrusion = decor_count > 0 and total_non_air < (slc.size * 0.3)
            if is_protrusion:
                max_depth = layer + 1

        if max_depth > 0:
            print(f"  {face_name}: decoration protrudes {max_depth}v from edge")
        else:
            print(f"  {face_name}: no decoration protrusion")

    print(f"\n  {'─'*56}")
    print(f"  BLOCK FIT ANALYSIS")
    print(f"  {'─'*56}")
    block_w = 96  # 3 * 32
    block_d = 96
    if w > block_w or d > block_d:
        print(f"  ⚠️  OVERSIZED: {w}x{d} exceeds block {block_w}x{block_d}")
        print(f"     Overrun: {w - block_w}v X, {d - block_d}v Z")
    else:
        print(f"  ✅ FITS: {w}x{d} within block {block_w}x{block_d}")
        print(f"     Margin: {block_w - w}v X, {block_d - d}v Z")

    print()


def main():
    if len(sys.argv) < 2 or sys.argv[1] == "--help":
        print("Usage: python analyze_building.py <building_type>")
        print("       python analyze_building.py --all")
        print(f"\nAvailable types: {', '.join(BUILDING_GENERATORS.keys())}")
        return

    if sys.argv[1] == "--all":
        for btype in BUILDING_GENERATORS:
            analyze_building(btype)
    else:
        analyze_building(sys.argv[1])


if __name__ == "__main__":
    main()
