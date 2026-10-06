#!/usr/bin/env python3
"""
Promote an editor-convention character JSON to a Unity runtime character JSON.

WHY: the voxel editor (three.js, right-handed) and Unity (left-handed) read the SAME voxel
indices, so every authored left/right label is mirrored once it crosses into Unity.
Models face +Z (thumbs/face slab point +Z). Facing +Z:
    editor (RH)  right = -X  (gid 3/5/7/9)
    Unity  (LH)  right = +X  (gid 2/4/6/8)
This script applies the flip ONCE, at promotion, so runtime files never need hand edits
and runtime code stays handedness-agnostic. See docs/systems/CHARACTER_ASSET_LIFECYCLE.md.

WHAT IT FLIPS
  attachmentPoints  right_* <-> left_* (positions + gids travel with the swap)
  animParams.aiming     L/R swap: armSwing, elbowBend, shoulderReach; Y-axis rotations negate:
                        torsoTwist, headYaw, shoulderReach, headTilt (Z) also negates
  animParams.crouching  L/R swap: armSwing, legStride, kneeBend
  animParams.legTwist / elbowBend / kneeBend rests  L/R swap (legTwist also negates: Y rotation)
  NOT touched: walkKeyframes (autoMirror half-cycle is symmetric), restPose (geometric, per-gid),
  pivots, jointOffset, voxels/groups/regions (geometry never changes).

USAGE
  python tools/promote_character_to_unity.py SRC.json [DST.json] [--name Civilian1] [--check]
  --check   report convention only (exit 1 if SRC is not editor-convention); write nothing
"""
import argparse, json, os, sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RUNTIME_DIR = os.path.join(REPO, "Assets", "StreamingAssets", "voxel_characters")


def swap_keys(d, a, b):
    """Swap the values of keys a/b when present (absent keys stay absent)."""
    ha, hb = a in d, b in d
    va, vb = d.get(a), d.get(b)
    if ha and hb: d[a], d[b] = vb, va
    elif ha: d[b] = d.pop(a)
    elif hb: d[a] = d.pop(b)


def neg(d, k):
    if k in d and isinstance(d[k], (int, float)): d[k] = -d[k]


def flip_attachment_names(ap):
    out = {}
    for k, v in ap.items():
        if k.startswith("right_"): k2 = "left_" + k[6:]
        elif k.startswith("left_"): k2 = "right_" + k[5:]
        elif k.endswith("_right"): k2 = k[:-6] + "_left"
        elif k.endswith("_left"): k2 = k[:-5] + "_right"
        else: k2 = k
        out[k2] = v
    return out


def right_hand_side(d):
    """'+X', '-X' or None — which side of center right_hand sits on (forward is +Z)."""
    rh = d.get("attachmentPoints", {}).get("right_hand")
    if not rh: return None
    return "+X" if rh["x"] > d["dims"][0] / 2.0 else "-X"


EDITOR_DEFAULT_AIMING = {  # mirrors VoxelCharacterAnimator.LoadFromAnimJson default fill (editor convention)
    "weaponType": "pistol", "torsoTwist": -0.2, "headYaw": 0, "headPitch": -0.05, "headTilt": 0,
    "armSwingL": 0, "armSwingR": -1.4, "shoulderReachL": 0, "shoulderReachR": 0,
    "elbowBendL": 0, "elbowBendR": 0.3,
}


def flip_params(p):
    # A trimmed file relies on the C# default aiming table, which is editor-convention
    # (right-armed). Materialize it so the flip is explicit instead of silently skipped.
    a = p.setdefault("aiming", dict(EDITOR_DEFAULT_AIMING))
    if a:
        for base in ("armSwing", "elbowBend", "shoulderReach"): swap_keys(a, base + "L", base + "R")
        for k in ("torsoTwist", "headYaw", "headTilt", "shoulderReachL", "shoulderReachR"): neg(a, k)
    c = p.get("crouching")
    if c:
        for base in ("armSwing", "legStride", "kneeBend"): swap_keys(c, base + "L", base + "R")
    lt = p.get("legTwist")
    if lt:
        swap_keys(lt, "leftRest", "rightRest"); neg(lt, "leftRest"); neg(lt, "rightRest")
    for sec in ("elbowBend", "kneeBend"):
        s = p.get(sec)
        if s: swap_keys(s, "leftRest", "rightRest")
    eb = p.get("elbowBend")
    if eb: swap_keys(eb, "twistL", "twistR")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src"); ap.add_argument("dst", nargs="?")
    ap.add_argument("--name"); ap.add_argument("--check", action="store_true")
    a = ap.parse_args()

    d = json.load(open(a.src, encoding="utf-8"))
    side = right_hand_side(d)
    marked = d.get("handedness") == "unity"
    print(f"[promote] {os.path.basename(a.src)}: dims={d['dims']} right_hand side={side} handedness={d.get('handedness', 'editor')}")

    if a.check:
        sys.exit(0 if (side == "-X" and not marked) else 1)
    if marked or side != "-X":
        sys.exit("[promote] ABORT: source is not editor-convention (already Unity, or right_hand is not on the -X side). "
                 "Refusing to double-flip.")

    d["attachmentPoints"] = flip_attachment_names(d.get("attachmentPoints", {}))
    flip_params(d.setdefault("animParams", {}))
    if a.name: d["name"] = a.name
    d["handedness"] = "unity"

    assert right_hand_side(d) == "+X", "post-flip right_hand must be on +X (forward +Z)"
    dst = a.dst or os.path.join(RUNTIME_DIR, (d["name"]) + ".json")
    json.dump(d, open(dst, "w", encoding="utf-8"), indent=2, ensure_ascii=False)
    print(f"[promote] wrote {dst} (right_hand now {d['attachmentPoints']['right_hand']})")


if __name__ == "__main__":
    main()
