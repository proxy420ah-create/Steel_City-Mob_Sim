#!/usr/bin/env python3
"""
Steel City Unified Model Inspector
===================================

A single tool that runs all Steel City quality checks on any .stasset model.

Checks performed:
  1. Dimensions & voxel count
  2. Material histogram + validity (flags unknown IDs, dark materials, low-alpha)
  3. Scale validation (voxel dims vs MODEL_DESIGN_STANDARD proportion table)
  4. Door height ratio (door height ÷ NPC height, must be >= 1.25x)
  5. Orientation verification (front-facing features at correct Z end)
  6. Left/right symmetry (per Y-slice, handles even & odd widths)
  7. Proportion check (W:H:D ratio against expected ranges per model type)
  8. Exterior wall closure (flags AIR gaps in outer walls that aren't doors)
  9. Internal hole detection (gaps in vertical columns)
 10. ASCII cross-section views (front, side, top-down slices)

Usage:
  python sc_inspector.py                              # inspect default (vehicle)
  python sc_inspector.py path/to/model.stasset        # inspect specific file
  python sc_inspector.py --type building path/to.stasset  # force model type
  python sc_inspector.py --compact path/to.stasset     # analysis only, no ASCII
  python sc_inspector.py --batch *.stasset             # inspect all files
  python sc_inspector.py --checks symmetry,materials   # run only specific checks

Model types: building, character, vehicle (auto-detected from dimensions if not forced)
"""

import argparse
import os
import sys
from pathlib import Path
from collections import Counter

import numpy as np

SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPT_DIR))

from stasset_io import load_stasset
from mob_materials import MOB_MATERIALS, get_material_name, get_material_color


# ---------------------------------------------------------------------------
# Constants from MODEL_DESIGN_STANDARD.md
# ---------------------------------------------------------------------------
NPC_HEIGHT_M = 0.64       # 32 char voxels × 0.02m
BUILDING_VOXEL = 0.1      # meters per voxel (buildings)
CHAR_VOXEL = 0.02         # meters per voxel (characters)
VEHICLE_VOXEL = 0.05      # meters per voxel (vehicles)

# Proportion reference table from MODEL_DESIGN_STANDARD.md Section 5
# (type, (W_min, W_max), (H_min, H_max), (D_min, D_max), voxel_size, label)
PROPORTION_TABLE = {
    "character":  ((16, 20),  (28, 32),  (10, 14),  CHAR_VOXEL,    "Character (NPC)"),
    "building_s": ((28, 36),  (14, 22),  (28, 36),  BUILDING_VOXEL, "Small business"),
    "building_a": ((28, 36),  (32, 40),  (28, 36),  BUILDING_VOXEL, "Apartments (small)"),
    "building_l": ((80, 100), (40, 50),  (80, 100), BUILDING_VOXEL, "Apartment block (large)"),
    "building_c": ((28, 36),  (22, 30),  (28, 36),  BUILDING_VOXEL, "Civic (police, casino, HQ)"),
    "vehicle":    ((18, 24),  (14, 20),  (26, 34),  VEHICLE_VOXEL,  "Vehicle (touring car)"),
}

# Door height standard from MODEL_DESIGN_STANDARD.md Section 3
# (class, min_voxels, max_voxels, min_ratio, label)
DOOR_STANDARDS = {
    "pedestrian":  (8, 8, 1.25,  "Pedestrian Standard"),
    "civic":       (10, 12, 1.56, "Civic / Grand"),
    "vehicle_bay": (6, 6, 0.0,   "Vehicle Bay (exempt from NPC ratio)"),
}

# Material symbols for ASCII rendering
MATERIAL_SYMBOLS = {
    0: " ",
    100: "#", 101: "S", 102: "C", 103: "s", 104: "-", 105: "c",
    106: "w", 107: "W", 108: "o", 109: "I", 110: "i", 111: "M",
    112: "g", 113: "L", 114: "G", 115: "R", 116: "U", 117: "N",
    118: "T", 119: "t", 120: "r", 121: "n", 122: "b", 123: "$",
    124: "*", 125: "F", 126: "K", 127: "H", 128: "h", 129: "u",
}


# ---------------------------------------------------------------------------
# Model type auto-detection
# ---------------------------------------------------------------------------
def detect_model_type(dims):
    """Auto-detect model type from dimensions (W, H, D)."""
    w, h, d = dims
    # Characters: narrow width, tall height, shallow depth
    # Overcoat variant is wider (20) and deeper (14) than standard (16×10)
    if w <= 24 and h >= 28 and d <= 16:
        return "character"
    if w >= 80:
        return "building_l"
    if w <= 24 and d <= 34 and h <= 20:
        return "vehicle"
    if h >= 32:
        return "building_a"
    if h >= 22:
        return "building_c"
    return "building_s"


def get_voxel_size(model_type):
    """Return voxel size in meters for a model type."""
    if model_type == "character":
        return CHAR_VOXEL
    if model_type == "vehicle":
        return VEHICLE_VOXEL
    return BUILDING_VOXEL


# ---------------------------------------------------------------------------
# Check 1: Dimensions & voxel count
# ---------------------------------------------------------------------------
def check_dimensions(voxels, dims, model_type):
    w, h, d = dims
    solid = int(np.count_nonzero(voxels))
    vs = get_voxel_size(model_type)
    real_w, real_h, real_d = w * vs, h * vs, d * vs

    lines = []
    lines.append(f"Dimensions: {w} x {h} x {d} voxels")
    lines.append(f"Real size:  {real_w:.2f}m x {real_h:.2f}m x {real_d:.2f}m (voxel={vs}m)")
    lines.append(f"Solid voxels: {solid} / {w*h*d} ({100*solid/(w*h*d):.1f}% fill)")
    return "\n".join(lines), {"w": w, "h": h, "d": d, "solid": solid,
                               "real_w": real_w, "real_h": real_h, "real_d": real_d}


# ---------------------------------------------------------------------------
# Check 2: Material histogram + validity
# ---------------------------------------------------------------------------
def check_materials(voxels, dims, model_type):
    flat = voxels.flatten()
    counts = Counter(int(v) for v in flat if v != 0)

    lines = []
    lines.append(f"Material histogram ({len(counts)} unique materials):")
    lines.append(f"  {'ID':>5}  {'Name':<22} {'Count':>7}  {'%':>6}  Notes")
    lines.append("  " + "-" * 70)

    issues = []
    for mat_id in sorted(counts.keys()):
        name = get_material_name(mat_id)
        count = counts[mat_id]
        pct = 100.0 * count / sum(counts.values())
        notes = []

        if mat_id not in MOB_MATERIALS:
            notes.append("UNKNOWN ID")
            issues.append(f"Unknown material ID {mat_id} ({count} voxels)")

        color = get_material_color(mat_id)
        brightness = sum(color[:3]) / 3.0
        # Black Fabric (126) and Hair (128) are intentionally very dark
        intentionally_dark = {126, 128}
        if brightness < 0.10 and mat_id not in intentionally_dark:
            notes.append(f"very dark (brightness={brightness:.2f})")
            issues.append(f"Very dark material {mat_id} ({name}): brightness={brightness:.2f}")
        elif brightness < 0.15:
            notes.append(f"dark (brightness={brightness:.2f})")

        if color[3] < 0.8:
            notes.append(f"low alpha={color[3]:.2f}")

        note_str = ", ".join(notes) if notes else ""
        lines.append(f"  {mat_id:>5}  {name:<22} {count:>7}  {pct:>5.1f}%  {note_str}")

    if not issues:
        lines.append("\n  All materials valid.")
    else:
        lines.append(f"\n  {len(issues)} issue(s) found:")
        for iss in issues:
            lines.append(f"    - {iss}")

    return "\n".join(lines), {"issues": issues, "counts": counts}


# ---------------------------------------------------------------------------
# Check 3: Scale validation
# ---------------------------------------------------------------------------
def check_scale(voxels, dims, model_type):
    w, h, d = dims
    entry = PROPORTION_TABLE.get(model_type)
    if not entry:
        return f"No proportion table entry for type '{model_type}'", {"issues": []}

    (w_min, w_max), (h_min, h_max), (d_min, d_max), vs, label = entry

    lines = []
    lines.append(f"Scale validation against: {label}")
    lines.append(f"  Axis  | Model | Expected Range | Status")
    lines.append("  " + "-" * 50)

    issues = []
    for axis, val, lo, hi, name in [
        ("W", w, w_min, w_max, "Width"),
        ("H", h, h_min, h_max, "Height"),
        ("D", d, d_min, d_max, "Depth"),
    ]:
        if lo <= val <= hi:
            status = "OK"
        else:
            status = f"OUT OF RANGE (expected {lo}-{hi})"
            issues.append(f"{name}={val} outside expected {lo}-{hi}")
        lines.append(f"  {axis}     | {val:>5} | {lo:>3}-{hi:<3}          | {status}")

    if not issues:
        lines.append("\n  All dimensions within expected range.")
    else:
        lines.append(f"\n  {len(issues)} dimension(s) out of range:")
        for iss in issues:
            lines.append(f"    - {iss}")

    return "\n".join(lines), {"issues": issues}


# ---------------------------------------------------------------------------
# Check 4: Door height ratio (buildings only)
# ---------------------------------------------------------------------------
def check_door_height(voxels, dims, model_type):
    if model_type.startswith("character") or model_type == "vehicle":
        return "Door height check: N/A (not a building)", {"applicable": False}

    w, h, d = dims
    # Scan the front face (Z=0..2) for vertical air gaps that look like doors.
    # A door is a vertical run of AIR in the front wall, width >= 3 voxels,
    # starting from ground level (Y=0) or near it.
    # Scan the front face exterior (Z=0) for vertical air gaps that look like doors.
    # A door is a vertical run of AIR at the exterior face (Z=0), starting from
    # ground level (Y=0). We only check Z=0 to avoid detecting hollow interiors
    # behind thin walls.
    door_candidates = []

    for x in range(2, w - 2):
        # Find the tallest continuous air gap at Z=0 starting from y=0
        air_run = 0
        for y in range(h):
            if voxels[x, y, 0] == 0:
                air_run += 1
            else:
                break
        if air_run >= 4:
            door_candidates.append((x, air_run))

    if not door_candidates:
        return "Door height check: No door opening detected on front face (Z=0)", {"applicable": True, "door_height": 0}

    # Group adjacent x values into openings
    raw_openings = []
    current_start = door_candidates[0][0]
    current_heights = [door_candidates[0][1]]
    for i in range(1, len(door_candidates)):
        x, height = door_candidates[i]
        if x == door_candidates[i-1][0] + 1:
            current_heights.append(height)
        else:
            raw_openings.append((current_start, current_start + len(current_heights) - 1, max(current_heights)))
            current_start = x
            current_heights = [height]
    raw_openings.append((current_start, current_start + len(current_heights) - 1, max(current_heights)))

    # Classify openings: doors (3-12v wide) vs large openings
    # Large openings may be legitimate storefronts (glass set back behind an
    # open archway) or actual missing wall panels. Check for glass materials
    # at Z=1..2 in the opening columns to distinguish.
    GLASS_IDS = {112, 113, 114}  # Window Glass, Lit Window, Storefront Glass
    doors = []
    large_openings = []
    for x0, x1, height in raw_openings:
        width = x1 - x0 + 1
        if width <= 12:
            doors.append((x0, x1, height))
        else:
            # Check if glass exists behind this opening (Z=1..3)
            has_glass_behind = False
            for x in range(x0, x1 + 1):
                for z in range(1, min(4, d)):
                    for y in range(min(height, h)):
                        if int(voxels[x, y, z]) in GLASS_IDS:
                            has_glass_behind = True
                            break
                    if has_glass_behind:
                        break
                if has_glass_behind:
                    break
            large_openings.append((x0, x1, height, has_glass_behind))

    lines = []
    lines.append(f"Door height check (front face Z=0):")
    if doors:
        lines.append(f"  Found {len(doors)} door opening(s):")
    else:
        lines.append("  No door-sized openings (3-12v wide) detected on front face.")

    issues = []
    for i, (x0, x1, height) in enumerate(doors):
        width = x1 - x0 + 1
        real_height = height * BUILDING_VOXEL
        ratio = real_height / NPC_HEIGHT_M
        ratio_status = "OK" if ratio >= 1.25 else f"FAIL (need >=1.25x, got {ratio:.2f}x)"
        if ratio < 1.25:
            issues.append(f"Door {i+1} at x={x0}-{x1} height={height}v ({real_height:.2f}m) ratio={ratio:.2f}x {ratio_status}")
        lines.append(f"  Door {i+1}: x={x0}-{x1} (w={width}v), height={height}v ({real_height:.2f}m), ratio={ratio:.2f}x NPC -> {ratio_status}")

    if large_openings:
        lines.append(f"\n  {len(large_openings)} large opening(s) (>12v wide):")
        for i, (x0, x1, height, has_glass) in enumerate(large_openings):
            width = x1 - x0 + 1
            label = "storefront (glass behind)" if has_glass else "POSSIBLE MISSING WALL"
            lines.append(f"  Opening {i+1}: x={x0}-{x1} (w={width}v), height={height}v — {label}")
            if not has_glass:
                issues.append(f"Large front opening at x={x0}-{x1} (w={width}v, h={height}v) — no glass detected behind, possible missing wall panel")

    if not issues:
        lines.append("\n  All doors pass the 1.25x NPC height ratio test.")
    else:
        lines.append(f"\n  {len(issues)} door(s) fail the ratio test:")
        for iss in issues:
            lines.append(f"    - {iss}")

    return "\n".join(lines), {"applicable": True, "doors": doors, "issues": issues}


# ---------------------------------------------------------------------------
# Check 5: Orientation verification
# ---------------------------------------------------------------------------
def check_orientation(voxels, dims, model_type):
    w, h, d = dims
    lines = []
    issues = []

    if model_type == "vehicle":
        # Vehicles: front at +Z. Check for headlights/grille materials at high Z.
        front_z = d - 1
        back_z = 0
        front_materials = set()
        back_materials = set()
        for x in range(w):
            for y in range(h):
                v = int(voxels[x, y, front_z])
                if v != 0:
                    front_materials.add(v)
                v = int(voxels[x, y, back_z])
                if v != 0:
                    back_materials.add(v)

        # Headlights = LAMP_GLOW (124) or GOLD_BRASS (123) should be at front
        has_headlights_front = 124 in front_materials or 123 in front_materials
        has_headlights_back = 124 in back_materials or 123 in back_materials

        lines.append("Orientation check (vehicle, front should be +Z):")
        lines.append(f"  Front (Z={front_z}) materials: {sorted(front_materials)}")
        lines.append(f"  Back  (Z={back_z}) materials: {sorted(back_materials)}")

        if has_headlights_front and not has_headlights_back:
            lines.append("  Headlights/grille at +Z front: OK")
        elif has_headlights_back and not has_headlights_front:
            lines.append("  WARNING: Headlight materials found at Z=0 (back), not at +Z front")
            issues.append("Vehicle appears to face -Z instead of +Z")
        else:
            lines.append("  NOTE: Could not confirm headlight position (may use non-standard materials)")

    elif model_type.startswith("building"):
        # Buildings: front at Z=0. Check for storefront glass / door / awning at low Z.
        front_z = 0
        back_z = d - 1
        front_has_glass = False
        front_has_door = False
        # Check first few Z layers for storefront features
        for z in range(min(4, d)):
            for x in range(w):
                for y in range(h):
                    v = int(voxels[x, y, z])
                    if v in (114, 112, 113):  # Storefront glass, window glass, lit window
                        front_has_glass = True
                    if v in (120, 121, 122):  # Painted doors
                        front_has_door = True

        lines.append("Orientation check (building, front should be Z=0):")
        lines.append(f"  Front (Z=0) has storefront glass: {front_has_glass}")
        lines.append(f"  Front (Z=0) has door material: {front_has_door}")
        if not front_has_glass and not front_has_door:
            lines.append("  NOTE: No storefront glass or door material at Z=0 — may face wrong way or use different materials")
            issues.append("No storefront features detected at Z=0 front face")

    elif model_type == "character":
        # Characters: face at low Z. Check for flesh (125) at low Z.
        front_z = 0
        back_z = d - 1
        front_has_flesh = False
        back_has_flesh = False
        for x in range(w):
            for y in range(h):
                if int(voxels[x, y, front_z]) == 125:
                    front_has_flesh = True
                if int(voxels[x, y, back_z]) == 125:
                    back_has_flesh = True

        lines.append("Orientation check (character, face should be low Z):")
        lines.append(f"  Front (Z=0) has flesh: {front_has_flesh}")
        lines.append(f"  Back  (Z={back_z}) has flesh: {back_has_flesh}")
        if not front_has_flesh and back_has_flesh:
            lines.append("  WARNING: Flesh material at high Z, not low Z — character may face backwards")
            issues.append("Character appears to face +Z instead of low Z")

    else:
        return "Orientation check: N/A (unknown model type)", {"issues": []}

    if not issues:
        lines.append("\n  Orientation OK.")
    else:
        lines.append(f"\n  {len(issues)} issue(s):")
        for iss in issues:
            lines.append(f"    - {iss}")

    return "\n".join(lines), {"issues": issues}


# ---------------------------------------------------------------------------
# Check 6: Symmetry (left/right)
# ---------------------------------------------------------------------------
def check_symmetry(voxels, dims, model_type):
    w, h, d = dims
    lines = []
    lines.append(f"Symmetry check (X-axis mirror, width={w}, {'odd' if w % 2 else 'even'}):")
    lines.append(f"  Y-slice | Mismatched voxels | Status")
    lines.append("  " + "-" * 45)

    mismatches = 0
    total_diff = 0
    for y in range(h):
        slice_ = voxels[:, y, :] != 0
        mirrored = slice_[::-1, :]
        diff = int(np.count_nonzero(slice_ != mirrored)) // 2
        if diff > 0:
            mismatches += 1
            total_diff += diff
            lines.append(f"  y={y:<5} | {diff:>17} | MISMATCH")

    if mismatches == 0:
        lines.append("  All slices symmetric.")
        lines.append("\n  Symmetry: PERFECT")
    else:
        lines.append(f"\n  {mismatches} slice(s) with asymmetry, {total_diff} total mismatched voxels.")
        lines.append("  NOTE: Small asymmetries may be intentional (steering wheel, barber pole, etc.)")

    return "\n".join(lines), {"mismatches": mismatches, "total_diff": total_diff}


# ---------------------------------------------------------------------------
# Check 7: Proportion check (W:H:D ratio)
# ---------------------------------------------------------------------------
def check_proportions(voxels, dims, model_type):
    w, h, d = dims
    lines = []
    issues = []

    # Expected W:H:D ratios per model type, derived from actual model dimensions
    # in MODEL_DESIGN_STANDARD.md Section 5. Values are normalized to H=1.0
    # (i.e., W/H and D/H ratios, not raw W:H:D).
    expected_ratios = {
        "character":  (0.5625, 1.000, 0.375),  # ~18/30, 30/30, ~11.25/30 (covers both standard and overcoat)
        "vehicle":    (1.250, 1.000, 1.875),   # 20/16, 16/16, 30/16
        "building_s": (1.780, 1.000, 1.780),   # ~32/18, 18/18, ~32/18
        "building_a": (0.940, 1.000, 0.940),   # ~32/34, 34/34, ~32/34
        "building_l": (2.000, 1.000, 2.000),   # ~96/48, 48/48, ~96/48
        "building_c": (1.230, 1.000, 1.230),   # ~32/26, 26/26, ~32/26
    }

    ratios = expected_ratios.get(model_type)
    if not ratios:
        return "Proportion check: N/A (no expected ratios for this type)", {"issues": []}

    exp_w, exp_h, exp_d = ratios
    # Normalize to H=1.0
    if h > 0:
        actual_w = w / h
        actual_d = d / h
    else:
        actual_w = actual_d = 0

    lines.append(f"Proportion check (normalized to H=1.0):")
    lines.append(f"  Axis | Actual | Expected | Tolerance | Status")
    lines.append("  " + "-" * 50)

    for axis, actual, expected, name in [
        ("W/H", actual_w, exp_w, "Width/Height"),
        ("D/H", actual_d, exp_d, "Depth/Height"),
    ]:
        diff = abs(actual - expected)
        tol = 0.25  # 25% tolerance
        if diff <= tol:
            status = "OK"
        else:
            status = f"DRIFT (off by {diff:.2f}, tolerance={tol})"
            issues.append(f"{name} ratio {actual:.3f} vs expected {expected:.3f} (drift={diff:.3f})")
        lines.append(f"  {axis:<4} | {actual:.3f}  | {expected:.3f}    | {tol:.2f}      | {status}")

    if not issues:
        lines.append("\n  Proportions within tolerance.")
    else:
        lines.append(f"\n  {len(issues)} proportion drift(s):")
        for iss in issues:
            lines.append(f"    - {iss}")

    return "\n".join(lines), {"issues": issues}


# ---------------------------------------------------------------------------
# Check 8: Exterior wall closure
# ---------------------------------------------------------------------------
def check_wall_closure(voxels, dims, model_type):
    """Check for unintended AIR gaps in exterior walls (the 'open door' bug)."""
    w, h, d = dims
    lines = []
    issues = []

    if model_type == "character":
        return "Wall closure check: N/A (character model)", {"issues": []}

    # Check the 4 exterior faces: X=0, X=w-1, Z=0, Z=d-1
    # For each face, count AIR voxels that are surrounded by solid neighbors
    # (i.e., gaps that should be filled)
    faces = [
        ("X=0 (left wall)",    [(0, y, z) for y in range(h) for z in range(d)]),
        ("X=w-1 (right wall)", [(w-1, y, z) for y in range(h) for z in range(d)]),
        ("Z=0 (front wall)",   [(x, y, 0) for x in range(w) for y in range(h)]),
        ("Z=d-1 (back wall)",  [(x, y, d-1) for x in range(w) for y in range(h)]),
    ]

    total_gaps = 0
    for face_name, coords in faces:
        face_gaps = 0
        for x, y, z in coords:
            if voxels[x, y, z] == 0:
                # Check if this air voxel has solid neighbors on both sides along the face plane
                # This detects holes in walls, not intentional openings
                if face_name.startswith("X="):
                    # Check along Y and Z for surrounding solid
                    above = y < h - 1 and voxels[x, y+1, z] != 0
                    below = y > 0 and voxels[x, y-1, z] != 0
                    z_plus = z < d - 1 and voxels[x, y, z+1] != 0
                    z_minus = z > 0 and voxels[x, y, z-1] != 0
                    if (above and below) or (z_plus and z_minus):
                        # Check if it's a door opening (runs from ground)
                        if y > 2:  # Not a ground-level opening
                            face_gaps += 1
                elif face_name.startswith("Z="):
                    above = y < h - 1 and voxels[x, y+1, z] != 0
                    below = y > 0 and voxels[x, y-1, z] != 0
                    x_plus = x < w - 1 and voxels[x+1, y, z] != 0
                    x_minus = x > 0 and voxels[x-1, y, z] != 0
                    if (above and below) or (x_plus and x_minus):
                        if y > 2:
                            face_gaps += 1

        if face_gaps > 0:
            lines.append(f"  {face_name}: {face_gaps} unexpected air gap(s) above ground level")
            total_gaps += face_gaps
            if face_gaps > 5:
                issues.append(f"{face_name} has {face_gaps} gaps — possible missing wall panels")
        else:
            lines.append(f"  {face_name}: clean")

    lines.insert(0, "Exterior wall closure check:")
    if total_gaps == 0:
        lines.append("\n  All exterior walls closed (no unexpected gaps).")
    else:
        lines.append(f"\n  {total_gaps} total gap(s) found above ground level.")
        lines.append("  NOTE: Small counts may be windows/vents. Large counts indicate missing panels.")

    return "\n".join(lines), {"total_gaps": total_gaps, "issues": issues}


# ---------------------------------------------------------------------------
# Check 9: Internal hole detection
# ---------------------------------------------------------------------------
def check_internal_holes(voxels, dims, model_type):
    """Detect unexpected air pockets inside solid structures."""
    w, h, d = dims
    lines = []
    holes = 0

    # Scan interior voxels (not on the surface) for AIR surrounded by solid on all 6 sides
    for x in range(1, w - 1):
        for y in range(1, h - 1):
            for z in range(1, d - 1):
                if voxels[x, y, z] == 0:
                    neighbors = [
                        voxels[x+1, y, z], voxels[x-1, y, z],
                        voxels[x, y+1, z], voxels[x, y-1, z],
                        voxels[x, y, z+1], voxels[x, y, z-1],
                    ]
                    solid_count = sum(1 for n in neighbors if n != 0)
                    if solid_count >= 5:  # 5 of 6 neighbors solid = likely an unintended hole
                        holes += 1

    lines.append("Internal hole detection (interior air surrounded by 5+ solid neighbors):")
    if holes == 0:
        lines.append("  No internal holes detected.")
    else:
        lines.append(f"  {holes} potential internal hole(s) found.")
        if holes > 10:
            lines.append("  WARNING: High hole count — may indicate structural issues.")
        else:
            lines.append("  NOTE: Small counts may be intentional (hollow interior, windows).")

    return "\n".join(lines), {"holes": holes}


# ---------------------------------------------------------------------------
# Check 10: ASCII cross-section views
# ---------------------------------------------------------------------------
def symbol_for(mat_id):
    s = MATERIAL_SYMBOLS.get(mat_id)
    if s:
        return s
    return str(mat_id)[-1]


def render_front_view(voxels, dims):
    w, h, d = dims
    lines = []
    lines.append(f"Front view (X=0..{w-1} left-to-right, Y=0..{h-1} bottom-to-top)")
    lines.append("+" + "-" * w + "+")
    for y in range(h - 1, -1, -1):
        row = []
        for x in range(w):
            col = voxels[x, y, :]
            non_air = col[col != 0]
            row.append(symbol_for(int(non_air[0])) if len(non_air) > 0 else " ")
        lines.append("|" + "".join(row) + "|")
    lines.append("+" + "-" * w + "+")
    return "\n".join(lines)


def render_side_view(voxels, dims):
    w, h, d = dims
    lines = []
    lines.append(f"Side view (Z=0..{d-1} left-to-right, Y=0..{h-1} bottom-to-top)")
    lines.append("+" + "-" * d + "+")
    for y in range(h - 1, -1, -1):
        row = []
        for z in range(d):
            col = voxels[:, y, z]
            non_air = col[col != 0]
            row.append(symbol_for(int(non_air[0])) if len(non_air) > 0 else " ")
        lines.append("|" + "".join(row) + "|")
    lines.append("+" + "-" * d + "+")
    return "\n".join(lines)


def render_top_view(voxels, dims):
    """Top-down view at the middle Y slice."""
    w, h, d = dims
    mid_y = h // 2
    lines = []
    lines.append(f"Top view at Y={mid_y} (X=0..{w-1} left-to-right, Z=0..{d-1} top-to-bottom)")
    lines.append("+" + "-" * w + "+")
    for z in range(d):
        row = []
        for x in range(w):
            v = int(voxels[x, mid_y, z])
            row.append(symbol_for(v) if v != 0 else " ")
        lines.append("|" + "".join(row) + "|")
    lines.append("+" + "-" * w + "+")
    return "\n".join(lines)


def render_cross_sections(voxels, dims):
    sections = []
    sections.append(render_front_view(voxels, dims))
    sections.append("")
    sections.append(render_side_view(voxels, dims))
    sections.append("")
    sections.append(render_top_view(voxels, dims))
    return "\n".join(sections)


# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
def build_summary(model_type, results):
    """Build a pass/warn/fail summary from all check results."""
    summary = []
    passes = 0
    warnings = 0
    fails = 0

    check_names = [
        ("scale", "Scale validation"),
        ("materials", "Material validity"),
        ("door_height", "Door height ratio"),
        ("orientation", "Orientation"),
        ("symmetry", "Symmetry"),
        ("proportions", "Proportions"),
        ("wall_closure", "Wall closure"),
        ("internal_holes", "Internal holes"),
    ]

    for key, name in check_names:
        r = results.get(key, {})
        issues = r.get("issues", [])
        if not r.get("applicable", True):
            status = "N/A"
        elif not issues:
            # Check for non-issue warnings
            if key == "symmetry" and r.get("mismatches", 0) > 0:
                status = f"WARN ({r['mismatches']} slices)"
                warnings += 1
            elif key == "wall_closure" and r.get("total_gaps", 0) > 0:
                status = f"WARN ({r['total_gaps']} gaps)"
                warnings += 1
            elif key == "internal_holes" and r.get("holes", 0) > 0:
                status = f"WARN ({r['holes']} holes)"
                warnings += 1
            else:
                status = "PASS"
                passes += 1
        else:
            status = f"FAIL ({len(issues)})"
            fails += 1
        summary.append(f"  {name:<25} {status}")

    header = f"Summary: {passes} PASS, {warnings} WARN, {fails} FAIL"
    return header, "\n".join(summary)


# ---------------------------------------------------------------------------
# Main inspection
# ---------------------------------------------------------------------------
ALL_CHECKS = ["dimensions", "materials", "scale", "door_height", "orientation",
              "symmetry", "proportions", "wall_closure", "internal_holes"]


def inspect_file(filepath, model_type=None, compact=False, only_checks=None):
    filepath = Path(filepath).resolve()
    if not filepath.exists():
        return f"File not found: {filepath}"

    voxels, dims, skeleton = load_stasset(str(filepath))
    if model_type is None:
        model_type = detect_model_type(dims)

    results = {}
    sections = []

    sections.append(f"{'='*60}")
    sections.append(f"Steel City Model Inspector")
    sections.append(f"{'='*60}")
    sections.append(f"File: {filepath.name}")
    sections.append(f"Type: {model_type} (auto-detected)" if model_type == detect_model_type(dims)
                    else f"Type: {model_type} (forced)")
    sections.append("")

    checks_to_run = only_checks if only_checks else ALL_CHECKS

    if "dimensions" in checks_to_run:
        text, data = check_dimensions(voxels, dims, model_type)
        sections.append(f"--- Dimensions ---")
        sections.append(text)
        sections.append("")
        results["dimensions"] = data

    if "materials" in checks_to_run:
        text, data = check_materials(voxels, dims, model_type)
        sections.append(f"--- Materials ---")
        sections.append(text)
        sections.append("")
        results["materials"] = data

    if "scale" in checks_to_run:
        text, data = check_scale(voxels, dims, model_type)
        sections.append(f"--- Scale Validation ---")
        sections.append(text)
        sections.append("")
        results["scale"] = data

    if "door_height" in checks_to_run:
        text, data = check_door_height(voxels, dims, model_type)
        sections.append(f"--- Door Height ---")
        sections.append(text)
        sections.append("")
        results["door_height"] = data

    if "orientation" in checks_to_run:
        text, data = check_orientation(voxels, dims, model_type)
        sections.append(f"--- Orientation ---")
        sections.append(text)
        sections.append("")
        results["orientation"] = data

    if "symmetry" in checks_to_run:
        text, data = check_symmetry(voxels, dims, model_type)
        sections.append(f"--- Symmetry ---")
        sections.append(text)
        sections.append("")
        results["symmetry"] = data

    if "proportions" in checks_to_run:
        text, data = check_proportions(voxels, dims, model_type)
        sections.append(f"--- Proportions ---")
        sections.append(text)
        sections.append("")
        results["proportions"] = data

    if "wall_closure" in checks_to_run:
        text, data = check_wall_closure(voxels, dims, model_type)
        sections.append(f"--- Wall Closure ---")
        sections.append(text)
        sections.append("")
        results["wall_closure"] = data

    if "internal_holes" in checks_to_run:
        text, data = check_internal_holes(voxels, dims, model_type)
        sections.append(f"--- Internal Holes ---")
        sections.append(text)
        sections.append("")
        results["internal_holes"] = data

    if not compact:
        sections.append(f"--- Cross-Section Views ---")
        sections.append(render_cross_sections(voxels, dims))
        sections.append("")

    # Summary
    header, detail = build_summary(model_type, results)
    sections.append(f"--- Summary ---")
    sections.append(header)
    sections.append(detail)
    sections.append(f"{'='*60}")

    return "\n".join(sections)


def main():
    parser = argparse.ArgumentParser(
        description="Steel City Unified Model Inspector — run all quality checks on .stasset models."
    )
    default_dir = str(SCRIPT_DIR.parent / "Assets" / "StreamingAssets" / "voxel_buildings")
    default_file = os.path.join(default_dir, "vehicle_civilian_car_0.stasset")

    parser.add_argument(
        "filepath",
        nargs="?",
        default=default_file,
        help="Path to .stasset file to inspect (default: vehicle_civilian_car_0.stasset)",
    )
    parser.add_argument(
        "--type", "-t",
        choices=["building", "building_s", "building_a", "building_l", "building_c",
                 "character", "vehicle"],
        default=None,
        help="Force model type (auto-detected from dimensions if not specified)",
    )
    parser.add_argument(
        "--compact", "-c",
        action="store_true",
        help="Skip ASCII cross-section views (analysis only)",
    )
    parser.add_argument(
        "--batch", "-b",
        action="store_true",
        help="Inspect all .stasset files in the same directory",
    )
    parser.add_argument(
        "--checks",
        default=None,
        help=f"Comma-separated list of checks to run (default: all). "
             f"Available: {', '.join(ALL_CHECKS)}",
    )

    args = parser.parse_args()

    only_checks = args.checks.split(",") if args.checks else None

    if args.batch:
        directory = Path(args.filepath).parent if args.filepath else Path(default_dir)
        files = sorted(directory.glob("*.stasset"))
        if not files:
            print(f"No .stasset files found in {directory}")
            return
        for f in files:
            print(inspect_file(f, model_type=args.type, compact=args.compact,
                               only_checks=only_checks))
            print()
    else:
        print(inspect_file(args.filepath, model_type=args.type, compact=args.compact,
                           only_checks=only_checks))


if __name__ == "__main__":
    main()
