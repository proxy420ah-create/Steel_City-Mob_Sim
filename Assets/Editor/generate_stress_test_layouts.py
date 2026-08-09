"""
Stress Test City Generator for Steel City: Mob Sim

Generates paired city_template + city_layout files at four scales matching
the original Gangsters: Organized Crime city of ~1,000 blocks:

  1. 25 blocks   (baseline, ~2.5% scale)
  2. 100 blocks  (10% scale)
  3. 500 blocks  (50% scale)
  4. 1000 blocks (full original game scale)

Two files must scale together:
  - city_template_NN.json  — game logic blocks (engine.blocks, businesses, NPCs, police)
  - city_layout_NN.json    — voxel rendering (.stasset building placement)

Building assets are reused across blocks (we have ~45 building .stasset files).
Each block gets 1-9 buildings with a realistic type mix:
  - 40% empty_land (vacant lots)
  - 20% apartments (residential)
  - 10% small business (bakery, barber, butcher, diner, garage)
  - 10% special (casino, speakeasy, courtyard)
  - 10% municipal (police, hq)
  - 10% mixed

Usage:
  python generate_stress_test_layouts.py

Output files are written to StreamingAssets/.
To test a tier: copy both city_template_NN.json and city_layout_NN.json
to city_template.json and city_layout.json respectively, then rebuild.
"""

import json
import os
import random
import math

# ---- Building asset pools ----
EMPTY_LAND = [
    "voxel_buildings/empty_land.stasset",
]

APARTMENTS = [
    "voxel_buildings/apartment_block_0.stasset",
    "voxel_buildings/apartments_0.stasset",
    "voxel_buildings/apartments_1.stasset",
    "voxel_buildings/apartments_2.stasset",
    "voxel_buildings/apartments_3.stasset",
    "voxel_buildings/apartments_4.stasset",
    "voxel_buildings/apartments_5.stasset",
    "voxel_buildings/apartments_6.stasset",
    "voxel_buildings/apartments_7.stasset",
    "voxel_buildings/apartments_8.stasset",
]

SMALL_BUSINESS = [
    "voxel_buildings/bakery_0.stasset",
    "voxel_buildings/bakery_2.stasset",
    "voxel_buildings/barber_0.stasset",
    "voxel_buildings/butcher_0.stasset",
    "voxel_buildings/butcher_1.stasset",
    "voxel_buildings/diner_0.stasset",
    "voxel_buildings/diner_3.stasset",
    "voxel_buildings/garage_0.stasset",
    "voxel_buildings/garage_4.stasset",
]

SPECIAL = [
    "voxel_buildings/casino_0.stasset",
    "voxel_buildings/casino_8.stasset",
    "voxel_buildings/speakeasy_0.stasset",
    "voxel_buildings/speakeasy_9.stasset",
    "voxel_buildings/courtyard_0.stasset",
]

MUNICIPAL = [
    "voxel_buildings/police_0.stasset",
    "voxel_buildings/police_station_6.stasset",
    "voxel_buildings/police_station_block_5.stasset",
    "voxel_buildings/hq_0.stasset",
    "voxel_buildings/hq_7.stasset",
    "voxel_buildings/hq_block_3.stasset",
    "voxel_buildings/hq_block_7.stasset",
]

ALL_BUILDINGS = EMPTY_LAND + APARTMENTS + SMALL_BUSINESS + SPECIAL + MUNICIPAL

# Business types for city_template (game logic side)
# Maps building type -> (business_type, is_illegal, population)
TEMPLATE_BIZ = {
    "empty_land":   ("empty_land", False, 0),
    "apartments":   ("apartments", False, 20),
    "apartment_block": ("apartments", False, 20),
    "bakery":       ("bakery", False, 3),
    "barber":       ("barber", False, 2),
    "butcher":      ("butcher", False, 3),
    "diner":        ("diner", False, 4),
    "garage":       ("garage", False, 2),
    "casino":       ("casino", True, 0),
    "speakeasy":    ("speakeasy", True, 0),
    "courtyard":    ("empty_land", False, 0),
    "police":       ("empty_land", False, 0),
    "police_station": ("empty_land", False, 0),
    "police_station_block": ("empty_land", False, 0),
    "hq":           ("empty_land", False, 0),
    "hq_block":     ("empty_land", False, 0),
}


def type_from_stasset(path):
    name = os.path.basename(path).replace(".stasset", "")
    parts = name.rsplit("_", 1)
    if len(parts) == 2 and parts[1].isdigit():
        return parts[0]
    return name


def pick_building(rng):
    r = rng.random()
    if r < 0.40:
        pool = EMPTY_LAND
    elif r < 0.60:
        pool = APARTMENTS
    elif r < 0.70:
        pool = SMALL_BUSINESS
    elif r < 0.80:
        pool = SPECIAL
    elif r < 0.90:
        pool = MUNICIPAL
    else:
        pool = ALL_BUILDINGS
    return rng.choice(pool)


def generate_city(num_blocks, seed=42):
    """Generate both city_template and city_layout dicts for the given block count."""
    rng = random.Random(seed)

    grid_side = int(math.ceil(math.sqrt(num_blocks)))

    layout_blocks = []
    template_blocks = []
    police_beats = []
    block_num = 0
    player_hq_assigned = False
    rival_hq_assigned = False
    police_station_assigned = False

    directions = ["N", "S", "E", "W", "NE", "NW", "SE", "SW", "Central", "Mid"]
    block_types = ["Lot", "Block", "District", "Quarter", "Sector", "Zone", "Ward", "Heights", "Flats", "Row"]

    for row in range(grid_side):
        for col in range(grid_side):
            if block_num >= num_blocks:
                break

            block_id = f"block_{block_num + 1}"
            block_name = f"{rng.choice(directions)} {rng.choice(block_types)} {block_num + 1}"

            # Decide building count: 60% single, 30% 2-4, 10% 5-9
            r = rng.random()
            if r < 0.60:
                building_count = 1
            elif r < 0.90:
                building_count = rng.randint(2, 4)
            else:
                building_count = rng.randint(5, 9)

            # Generate buildings for layout file
            buildings = []
            template_biz_types = []
            total_pop = 0
            has_illegal = False

            for slot in range(building_count):
                stasset = pick_building(rng)
                btype = type_from_stasset(stasset)
                illegal = btype in ("casino", "speakeasy") and rng.random() < 0.5
                buildings.append({
                    "type": btype,
                    "illegal": illegal,
                    "stasset": stasset,
                    "slot": slot
                })

                # Map to template business type
                biz_type, default_illegal, pop = TEMPLATE_BIZ.get(btype, ("empty_land", False, 0))
                if illegal:
                    biz_type = "speakeasy" if btype == "speakeasy" else biz_type
                template_biz_types.append((biz_type, illegal or default_illegal))
                total_pop += pop
                if illegal:
                    has_illegal = True

            # Assign special blocks: player HQ, rival HQ, police station
            is_player_hq = False
            is_rival_hq = False
            is_police = False

            if not player_hq_assigned and block_num >= num_blocks // 4:
                is_player_hq = True
                player_hq_assigned = True
            elif not rival_hq_assigned and block_num >= num_blocks // 2:
                is_rival_hq = True
                rival_hq_assigned = True
            elif not police_station_assigned and block_num >= num_blocks * 3 // 4:
                is_police = True
                police_station_assigned = True

            # Build template businesses list (grouped by type with counts)
            biz_counts = {}
            for biz_type, is_illegal in template_biz_types:
                key = (biz_type, is_illegal)
                biz_counts[key] = biz_counts.get(key, 0) + 1

            template_businesses = []
            for (biz_type, is_illegal), count in biz_counts.items():
                template_businesses.append({
                    "type": biz_type,
                    "illegal": is_illegal,
                    "count": count
                })

            # Land value: higher for apartments/special, lower for empty
            land_value = 3
            if any(b["type"] in ("apartments", "apartment_block") for b in buildings):
                land_value = rng.randint(5, 8)
            elif any(b["type"] in ("casino", "speakeasy") for b in buildings):
                land_value = rng.randint(6, 10)
            elif any(b["type"] in ("bakery", "barber", "butcher", "diner", "garage") for b in buildings):
                land_value = rng.randint(4, 6)

            # Layout block
            layout_blocks.append({
                "block_id": block_id,
                "block_name": block_name,
                "row": row,
                "col": col,
                "buildings": buildings
            })

            # Template block
            template_blocks.append({
                "id": block_id,
                "name": block_name,
                "row": row,
                "col": col,
                "land_value": land_value,
                "businesses": template_businesses,
                "population": total_pop,
                "player_hq": is_player_hq,
                "rival_hq": is_rival_hq,
                "police_station": is_police
            })

            block_num += 1

    # Generate police beats — one officer per ~25 blocks, min 1
    num_police = max(1, num_blocks // 25)
    for i in range(num_police):
        # Each officer covers 3-5 random blocks
        beat_size = rng.randint(3, 5)
        beat = []
        for _ in range(beat_size):
            beat.append(f"block_{rng.randint(1, num_blocks)}")
        police_beats.append({
            "officer_id": f"officer_{i+1:03d}",
            "name": f"Patrolman #{i+1}",
            "beat": beat,
            "bribe_cost": rng.randint(200, 500)
        })

    # Build layout JSON
    building_types = {}
    for block in layout_blocks:
        for b in block["buildings"]:
            if b["type"] not in building_types:
                building_types[b["type"]] = [32, 20, 34]

    layout = {
        "_comment": f"Stress test city layout: {num_blocks} blocks. Generated by generate_stress_test_layouts.py",
        "blocks": layout_blocks,
        "building_types": building_types
    }

    # Build template JSON
    template = {
        "_comment": f"Stress test city template: {num_blocks} blocks. Generated by generate_stress_test_layouts.py",
        "version": "0.0.2",
        "grid": {"rows": grid_side, "cols": grid_side},
        "blocks": template_blocks,
        "police_beats": police_beats
    }

    return template, layout


def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    project_root = os.path.dirname(os.path.dirname(script_dir))
    streaming_assets = os.path.join(project_root, "Assets", "StreamingAssets")

    if not os.path.isdir(streaming_assets):
        print(f"ERROR: StreamingAssets not found at {streaming_assets}")
        return

    tiers = [25, 100, 500, 1000]

    print("=" * 70)
    print("Steel City Stress Test Generator")
    print("=" * 70)
    print(f"Output directory: {streaming_assets}")
    print()

    for num_blocks in tiers:
        template, layout = generate_city(num_blocks)

        chunk_count = sum(len(b["buildings"]) for b in layout["blocks"])
        total_dispatches = chunk_count + 1  # +1 for terrain

        template_file = os.path.join(streaming_assets, f"city_template_{num_blocks}.json")
        layout_file = os.path.join(streaming_assets, f"city_layout_{num_blocks}.json")

        with open(template_file, "w") as f:
            json.dump(template, f, indent=2)
        with open(layout_file, "w") as f:
            json.dump(layout, f, indent=2)

        print(f"  {num_blocks:5d} blocks | {chunk_count:5d} building chunks | ~{total_dispatches} dispatches/frame")
        print(f"        {os.path.basename(template_file)}")
        print(f"        {os.path.basename(layout_file)}")
        print()

    print("To test a tier (e.g. 25 blocks):")
    print(f"  copy city_template_25.json city_template.json")
    print(f"  copy city_layout_25.json   city_layout.json")
    print(f"  Then in Unity: Play -> REBUILD CITY")
    print()
    print("Restore originals:")
    print(f"  copy city_template_original.json city_template.json")
    print(f"  copy city_layout_original.json   city_layout.json")
    print()
    print("Done!")


if __name__ == "__main__":
    main()
