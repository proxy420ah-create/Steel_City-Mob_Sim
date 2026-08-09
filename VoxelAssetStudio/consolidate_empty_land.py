"""Consolidate 882 identical empty_land_*.stasset files into 1 empty_land.stasset.

Also updates city_layout.json to point all empty_land references to the single file.
Deletes the 881 redundant copies.
"""
import os
import json
import shutil

BUILDINGS_DIR = r'..\Assets\StreamingAssets\voxel_buildings'
LAYOUT_PATH = r'..\Assets\StreamingAssets\city_layout.json'

# Step 1: Copy empty_land_0.stasset → empty_land.stasset (keep as the single source)
src = os.path.join(BUILDINGS_DIR, 'empty_land_0.stasset')
dst = os.path.join(BUILDINGS_DIR, 'empty_land.stasset')
shutil.copy2(src, dst)
print(f"Created: {dst} ({os.path.getsize(dst)} bytes)")

# Step 2: Update city_layout.json — replace all empty_land_N.stasset → empty_land.stasset
with open(LAYOUT_PATH, 'r') as f:
    layout = json.load(f)

replaced = 0
for block in layout.get('blocks', []):
    for building in block.get('buildings', []):
        stasset = building.get('stasset', '')
        if 'empty_land_' in stasset and 'empty_land.stasset' not in stasset:
            building['stasset'] = 'voxel_buildings/empty_land.stasset'
            replaced += 1

print(f"Updated city_layout.json: {replaced} references → empty_land.stasset")

with open(LAYOUT_PATH, 'w') as f:
    json.dump(layout, f, indent=2)
print(f"Saved: {LAYOUT_PATH}")

# Step 3: Delete all empty_land_N.stasset files (keep empty_land.stasset)
deleted = 0
for f in os.listdir(BUILDINGS_DIR):
    if f.startswith('empty_land_') and f.endswith('.stasset'):
        os.remove(os.path.join(BUILDINGS_DIR, f))
        deleted += 1

print(f"Deleted {deleted} redundant empty_land_*.stasset files")
print(f"\nDone! Single empty_land.stasset remains ({os.path.getsize(dst)} bytes)")
