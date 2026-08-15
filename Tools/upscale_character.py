#!/usr/bin/env python3
"""
Upscale a Steel City character JSON model by 2x in each axis (nearest-neighbor).

Each voxel at (x,y,z) becomes 8 voxels at (2x..2x+1, 2y..2y+1, 2z..2z+1).
This is a sampling density fix — the silhouette stays identical, but the
raymarcher has 8x more voxels to sample, eliminating see-through gaps
during animation (same approach used for building railings).

Reversible: the original file is backed up to <name>.original.json before
writing the upscaled version.

Usage:
    python upscale_character.py <input.json> [--scale N] [--revert]

    --scale N   Upscale factor (default: 2)
    --revert    Restore the original backup (undo the upscale)

What gets scaled:
    - dims: [W,H,D] -> [W*N, H*N, D*N]
    - voxels: [[x,y,z,mid], ...] -> each voxel becomes N^3 voxels
    - groups: {"x,y,z": gid} -> keys scaled by N
    - regions: {"x,y,z": rid} -> keys scaled by N
    - jointOffset: voxel-space offsets -> multiplied by N
    - crouching.modelLower: voxel-space offset -> multiplied by N

What stays the same (normalized 0-1 or angle-based):
    - pivots: {x,y,z} fractions of dims — already resolution-independent
    - animParams angles (armSwing, legStride, etc.) — radians, not voxels
    - materials, regionDefs, groupDefs, states — metadata, not spatial
"""

import json
import sys
import os
import shutil
import argparse


def upscale_voxels(voxels, n):
    """Each [x,y,z,mid] -> n^3 voxels filling the n×n×n block."""
    out = []
    for v in voxels:
        x, y, z, mid = v[0], v[1], v[2], v[3]
        for dx in range(n):
            for dy in range(n):
                for dz in range(n):
                    out.append([x * n + dx, y * n + dy, z * n + dz, mid])
    return out


def upscale_key_dict(d, n):
    """Scale dict keys from "x,y,z" to "x*n,y*n,z*n" with n^3 entries each."""
    out = {}
    for key, val in d.items():
        parts = key.split(",")
        if len(parts) == 3:
            x, y, z = int(parts[0]), int(parts[1]), int(parts[2])
            for dx in range(n):
                for dy in range(n):
                    for dz in range(n):
                        out[f"{x*n+dx},{y*n+dy},{z*n+dz}"] = val
        else:
            # Non-spatial key — keep as-is
            out[key] = val
    return out


def upscale_joint_offsets(anim_params, n):
    """Scale voxel-space offsets in animParams."""
    # jointOffset: {"gid": {"x": voxels, "y": voxels, "z": voxels}}
    jo = anim_params.get("jointOffset")
    if jo:
        for gid, offset in jo.items():
            if isinstance(offset, dict):
                for axis in ("x", "y", "z"):
                    if axis in offset:
                        offset[axis] = offset[axis] * n

    # crouching.modelLower: voxel-space downward shift
    crouching = anim_params.get("crouching")
    if crouching and "modelLower" in crouching:
        crouching["modelLower"] = crouching["modelLower"] * n

    return anim_params


def upscale(data, n):
    """Upscale the full character JSON data structure."""
    # Dims
    old_dims = data["dims"]
    data["dims"] = [d * n for d in old_dims]

    # Voxels
    print(f"  Upscaling voxels: {len(data['voxels'])} -> {len(data['voxels']) * n**3}")
    data["voxels"] = upscale_voxels(data["voxels"], n)

    # Groups
    if "groups" in data and data["groups"]:
        old_count = len(data["groups"])
        data["groups"] = upscale_key_dict(data["groups"], n)
        print(f"  Upscaling groups: {old_count} -> {len(data['groups'])}")

    # Regions
    if "regions" in data and data["regions"]:
        old_count = len(data["regions"])
        data["regions"] = upscale_key_dict(data["regions"], n)
        print(f"  Upscaling regions: {old_count} -> {len(data['regions'])}")

    # Pivots — normalized 0-1, NO CHANGE
    # (Already resolution-independent)

    # Anim params — scale voxel-space offsets only
    if "animParams" in data and data["animParams"]:
        data["animParams"] = upscale_joint_offsets(data["animParams"], n)
        print(f"  Scaled jointOffset and crouching.modelLower by {n}x")

    # Materials, regionDefs, groupDefs, states — metadata, NO CHANGE

    return data


def revert(input_path):
    """Restore original backup."""
    backup_path = input_path.replace(".json", ".original.json")
    if os.path.exists(backup_path):
        shutil.copy2(backup_path, input_path)
        os.remove(backup_path)
        print(f"Reverted: {input_path} restored from backup")
    else:
        print(f"No backup found at {backup_path} — nothing to revert")
        sys.exit(1)


def main():
    parser = argparse.ArgumentParser(description="Upscale Steel City character JSON by N× in each axis (nearest-neighbor)")
    parser.add_argument("input", help="Path to the character .json file")
    parser.add_argument("--scale", type=int, default=2, help="Upscale factor (default: 2)")
    parser.add_argument("--revert", action="store_true", help="Restore original backup")
    args = parser.parse_args()

    input_path = os.path.abspath(args.input)

    if args.revert:
        revert(input_path)
        return

    if not os.path.exists(input_path):
        print(f"File not found: {input_path}")
        sys.exit(1)

    # Backup original
    backup_path = input_path.replace(".json", ".original.json")
    if not os.path.exists(backup_path):
        shutil.copy2(input_path, backup_path)
        print(f"Backup saved: {backup_path}")
    else:
        print(f"Backup already exists: {backup_path} (skipping backup)")

    # Load
    with open(input_path, "r", encoding="utf-8") as f:
        data = json.load(f)

    old_dims = data["dims"][:]
    print(f"Original dims: {old_dims} = {old_dims[0]*old_dims[1]*old_dims[2]} voxels")

    # Upscale
    data = upscale(data, args.scale)

    new_dims = data["dims"]
    print(f"New dims: {new_dims} = {new_dims[0]*new_dims[1]*new_dims[2]} voxels")

    # Update savedAt
    from datetime import datetime, timezone
    data["savedAt"] = datetime.now(timezone.utc).isoformat()

    # Write
    with open(input_path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)

    print(f"Written: {input_path}")
    print(f"To revert: python upscale_character.py \"{input_path}\" --revert")


if __name__ == "__main__":
    main()
