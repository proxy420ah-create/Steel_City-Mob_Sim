"""
Steel Tide Voxel Asset Studio
Toolbox: stasset_connect_components.py

Connects disconnected voxel components by adding minimal bridge voxels.

Use case:
  Physics debugging often requires a single connected rigid body. If a voxel
  model has separate floating parts (e.g., arms detached from the spine), this
  script adds the smallest possible bridge of bone voxels to connect them.
"""

import argparse
import sys
import uuid
from collections import deque
from pathlib import Path

import numpy as np

SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPT_DIR.parent))
from stasset_io import load_stasset, save_stasset


BONE_MATERIAL = 12


def find_components(voxels, dims):
    """
    Find all 6-connected components of non-air voxels.
    Returns a list of lists, each containing (x, y, z) tuples.
    """
    visited = np.zeros(dims, dtype=bool)
    components = []

    for x in range(dims[0]):
        for y in range(dims[1]):
            for z in range(dims[2]):
                if voxels[x, y, z] == 0 or visited[x, y, z]:
                    continue

                comp = []
                queue = deque([(x, y, z)])
                visited[x, y, z] = True

                while queue:
                    cx, cy, cz = queue.popleft()
                    comp.append((cx, cy, cz))
                    for dx, dy, dz in [(1,0,0), (-1,0,0), (0,1,0), (0,-1,0), (0,0,1), (0,0,-1)]:
                        nx, ny, nz = cx + dx, cy + dy, cz + dz
                        if 0 <= nx < dims[0] and 0 <= ny < dims[1] and 0 <= nz < dims[2]:
                            if voxels[nx, ny, nz] != 0 and not visited[nx, ny, nz]:
                                visited[nx, ny, nz] = True
                                queue.append((nx, ny, nz))

                components.append(comp)

    return components


def closest_pair(comp_a, comp_b):
    """Return the two closest voxels (one from each component) by Manhattan distance."""
    best_a = None
    best_b = None
    best_dist = float('inf')
    for a in comp_a:
        for b in comp_b:
            dist = abs(a[0]-b[0]) + abs(a[1]-b[1]) + abs(a[2]-b[2])
            if dist < best_dist:
                best_dist = dist
                best_a = a
                best_b = b
    return best_a, best_b, best_dist


def manhattan_path(a, b):
    """
    Generate a Manhattan path from a to b. Steps are ordered X, then Y, then Z.
    Returns a list of (x, y, z) positions including the endpoints.
    """
    path = [a]
    x, y, z = a
    tx, ty, tz = b

    while x != tx:
        x += 1 if tx > x else -1
        path.append((x, y, z))
    while y != ty:
        y += 1 if ty > y else -1
        path.append((x, y, z))
    while z != tz:
        z += 1 if tz > z else -1
        path.append((x, y, z))

    return path


def connect_components(voxels, dims):
    """
    Repeatedly connect the two closest components with a Manhattan bridge until
    only one component remains. Returns the number of bridge voxels added.
    """
    components = find_components(voxels, dims)
    if len(components) <= 1:
        return 0, components

    added = 0
    while len(components) > 1:
        # Find the closest pair of components.
        best_i = 0
        best_j = 1
        best_a = None
        best_b = None
        best_dist = float('inf')

        for i in range(len(components)):
            for j in range(i + 1, len(components)):
                a, b, dist = closest_pair(components[i], components[j])
                if dist < best_dist:
                    best_dist = dist
                    best_i = i
                    best_j = j
                    best_a = a
                    best_b = b

        # Build bridge between best_a and best_b.
        path = manhattan_path(best_a, best_b)
        for pos in path:
            x, y, z = pos
            if voxels[x, y, z] == 0:
                voxels[x, y, z] = BONE_MATERIAL
                added += 1

        # Merge the two components.
        merged = components[best_i] + components[best_j]
        # Remove in reverse order to keep indices valid.
        components.pop(max(best_i, best_j))
        components.pop(min(best_i, best_j))
        components.append(merged)

    return added, components


def main():
    parser = argparse.ArgumentParser(
        description="Connect disconnected voxel components with minimal bone bridges."
    )
    default_src = str(SCRIPT_DIR / "../../My project/Assets/StreamingAssets/ActorBone.stasset")
    default_dst = str(SCRIPT_DIR / "../../My project/Assets/StreamingAssets/ActorBone_Connected.stasset")
    parser.add_argument("src", nargs="?", default=default_src, help="Source .stasset file.")
    parser.add_argument("dst", nargs="?", default=default_dst, help="Destination .stasset file.")
    args = parser.parse_args()

    src_path = Path(args.src).resolve()
    dst_path = Path(args.dst).resolve()

    if not src_path.exists():
        raise FileNotFoundError(src_path)

    voxels, dims, skeleton = load_stasset(str(src_path))
    before = int(np.count_nonzero(voxels))

    components_before = find_components(voxels, dims)
    print(f"Found {len(components_before)} disconnected component(s)")
    for i, comp in enumerate(components_before):
        print(f"  Component {i}: {len(comp)} voxels")

    added, components_after = connect_components(voxels, dims)
    after = int(np.count_nonzero(voxels))

    save_stasset(str(dst_path), voxels, skeleton)

    # Generate a Unity .meta file so the asset imports cleanly.
    meta_path = dst_path.with_suffix(dst_path.suffix + ".meta")
    meta_path.write_text(
        f"fileFormatVersion: 2\n"
        f"guid: {uuid.uuid4().hex}\n"
        f"DefaultImporter:\n"
        f"  externalObjects: {{}}\n"
        f"  userData: \n"
        f"  assetBundleName: \n"
        f"  assetBundleVariant: \n"
    )

    print(f"\nBridge voxels added: {added}")
    print(f"Non-air voxels: {before} -> {after}")
    print(f"Components: {len(components_before)} -> {len(components_after)}")
    print(f"Saved: {dst_path}")
    print(f"Unity meta: {meta_path}")


if __name__ == "__main__":
    main()
