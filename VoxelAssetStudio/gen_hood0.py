"""Generates character_hoodlum_0.json in the animator's project format.
Replicates generateCharacter + autoAssignGroups from character_pipeline.html.
Maps pipeline's 10 groups (0-9) down to animator's 6 groups (0-5).
"""
import json
import os
from datetime import datetime

W, H, D = 16, 32, 10
voxel_map = {}

def setV(x, y, z, mid):
    key = f"{x},{y},{z}"
    if mid == 0:
        voxel_map.pop(key, None)
    else:
        voxel_map[key] = mid

def skin_material(tone):
    if tone < 16: return 122
    if tone < 32: return 122
    if tone < 48: return 102
    return 103

# Hood 0 features from PORTRAIT_CATALOG
features = {
    "hair": 1, "hat": 1, "beard": 0, "glasses": 0,
    "skin": 0, "eyes": 1, "nose": 1, "mouth": 0,
    "body": "hoodlum", "scar": 0
}

skin = skin_material(features["skin"])
body_mat = 126  # hoodlum

# === LEGS (y=0-12, x=3-5 L / x=10-12 R, z=2-7) ===
for y in range(0, 13):
    for x in range(3, 6):
        for z in range(2, 8):
            setV(x, y, z, body_mat)
    for x in range(10, 13):
        for z in range(2, 8):
            setV(x, y, z, body_mat)

# === TORSO + ARMS (y=13-21, full width x=0-15, z=1-8) ===
for y in range(13, 22):
    for x in range(0, 16):
        for z in range(1, 9):
            setV(x, y, z, body_mat)

# Skin at torso edges (wrists)
for y in range(13, 15):
    for x in range(0, 16):
        setV(x, y, 1, skin)
        setV(x, y, 8, skin)

# Body detail accents (hoodlum)
for y in range(15, 20):
    setV(7, y, 1, 127)
    setV(8, y, 8, 127)
for y in range(15, 22):
    setV(7, y, 1, 120)
    setV(8, y, 8, 120)

# === SHOULDERS (y=22, full width x=0-15, z=0-9) ===
for x in range(0, 16):
    for z in range(0, 10):
        setV(x, 22, z, body_mat)

# === HEAD (y=23-27) ===
for y in range(23, 25):
    for x in range(6, 10):
        for z in range(3, 7):
            setV(x, y, z, skin)
for y in range(25, 28):
    for x in range(4, 12):
        for z in range(2, 8):
            setV(x, y, z, skin)

# === HAIR (mat 128) - case 1 ===
hair_mat = 128
for x in range(4, 12):
    setV(x, 25, 2, hair_mat)
    setV(x, 26, 2, hair_mat)
for z in range(2, 8):
    setV(4, 25, z, hair_mat)
    setV(11, 25, z, hair_mat)
    setV(4, 26, z, hair_mat)
    setV(11, 26, z, hair_mat)
for x in range(4, 12):
    setV(x, 25, 7, hair_mat)

# === EYES (mat 109) - case 1 (medium) ===
eye_y, eye_z, eye_lx, eye_rx = 26, 7, 6, 9
setV(eye_lx, eye_y, eye_z, 109)
setV(eye_rx, eye_y, eye_z, 109)

# === NOSE - case 1 ===
setV(7, 27, 7, skin)

# === MOUTH - case 0 (none) ===
# === GLASSES - case 0 (none) ===
# === BEARD - case 0 (none) ===
# === SCAR - case 0 (none) ===

# === HAT - case 1 (Flat Cap, Hood 0 default) ===
for x in range(0, 16):
    for z in range(0, 10):
        setV(x, 28, z, body_mat)
for x in range(2, 14):
    for z in range(1, 9):
        setV(x, 29, z, 120)
for y in range(30, 32):
    for x in range(2, 14):
        for z in range(1, 9):
            setV(x, y, z, body_mat)

# === AUTO-ASSIGN GROUPS ===
ANATOMY1 = {
    "headY": 23, "kneeY": 6, "elbowY": 17, "legTopY": 13,
    "leftLegX": 6, "rightLegX": 10, "leftArmX": 3, "rightArmX": 13,
}

# Pipeline groups: 0=Body, 1=Head, 2=LUpperArm, 3=RUpperArm, 4=LThigh, 5=RThigh,
#                  6=LShin, 7=RShin, 8=LForearm, 9=RForearm
# Animator groups: 0=Body, 1=Head, 2=LeftArm, 3=RightArm, 4=LeftLeg, 5=RightLeg
GROUP_MAP = {0: 0, 1: 1, 2: 2, 3: 3, 4: 4, 5: 5, 6: 4, 7: 5, 8: 2, 9: 3}

group_map = {}
a = ANATOMY1
for key in voxel_map:
    x, y, z = map(int, key.split(","))
    gid = 0
    if y >= a["headY"]:
        gid = 1
    elif x < a["leftArmX"] and y >= a["elbowY"]:
        gid = 2
    elif x >= a["rightArmX"] and y >= a["elbowY"]:
        gid = 3
    elif x < a["leftArmX"] and y >= a["legTopY"] and y < a["elbowY"]:
        gid = 8
    elif x >= a["rightArmX"] and y >= a["legTopY"] and y < a["elbowY"]:
        gid = 9
    elif x < a["leftLegX"] and y >= a["kneeY"] and y < a["legTopY"]:
        gid = 4
    elif x >= a["rightLegX"] and y >= a["kneeY"] and y < a["legTopY"]:
        gid = 5
    elif x < a["leftLegX"] and y < a["kneeY"]:
        gid = 6
    elif x >= a["rightLegX"] and y < a["kneeY"]:
        gid = 7
    group_map[key] = GROUP_MAP[gid]

# === BUILD OUTPUT ===
voxels = []
for key, mid in voxel_map.items():
    x, y, z = map(int, key.split(","))
    voxels.append([x, y, z, mid])

groups = []
for key, gid in group_map.items():
    x, y, z = map(int, key.split(","))
    groups.append([x, y, z, gid])

pivots = {
    "1": {"x": 0.5, "y": 0.78, "z": 0.5},
    "2": {"x": 0.25, "y": 0.75, "z": 0.5},
    "3": {"x": 0.75, "y": 0.75, "z": 0.5},
    "4": {"x": 0.375, "y": 0.34, "z": 0.5},
    "5": {"x": 0.625, "y": 0.34, "z": 0.5},
}

anim_params = {
    "walk": {"armSwing": 0.3, "armFreq": 6.0, "legStride": 0.4, "legFreq": 6.0},
    "looking": {"headYaw": 0.5, "headYawFreq": 2.0, "headPitch": 0.1, "headPitchFreq": 1.3},
    "checking": {"headYaw": 0.5, "headYawFreq": 2.0, "headPitch": 0.1, "headPitchFreq": 1.3},
    "aiming": {"headYaw": 0.3, "headPitch": -0.1, "armSwing": -1.2},
    "crouching": {"headPitch": 0.2, "armSwingL": 0.3, "armSwingR": -0.3, "legStride": 0.6},
    "flinching": {"headPitch": 0.4, "armSwing": -1.5},
    "falling": {"legStrideL": -0.5, "legStrideR": 0.5},
}

output = {
    "format": "character_animator_project",
    "version": 1,
    "dims": [W, H, D],
    "voxels": voxels,
    "groups": groups,
    "pivots": pivots,
    "animParams": anim_params,
    "savedAt": datetime.utcnow().isoformat() + "Z",
    "name": "Hoodlum 0",
    "features": features,
}

out_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "character_hoodlum_0.json")
with open(out_path, "w") as f:
    json.dump(output, f, indent=2)

# Stats
group_counts = {}
for g in groups:
    group_counts[g[3]] = group_counts.get(g[3], 0) + 1

print(f"Written: {out_path}")
print(f"Voxels: {len(voxels)}, Groups: {len(groups)}")
print(f"Group counts: {group_counts}")
