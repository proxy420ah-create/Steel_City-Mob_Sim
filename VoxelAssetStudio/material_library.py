# Steel Tide: Voxel Asset Studio
# material_library.py - Material definitions and colors
# MUST MATCH Unity VoxelBits.cs and VoxelRenderer.cs!
#
# 'mass' is a relative density per voxel (Bone = 1.8 reference, steel ~7.8).
# It feeds the Unity physics pipeline: a rigged bone's body mass is the sum of
# its voxels' material masses. User edits are persisted to material_properties.json.

# Material IDs - Complete synchronized system
# Theme: Distant Planet Border Colony (Futuristic Sci-Fi)
MATERIALS = {
    # Core Building Materials (0-5)
    0: {"name": "Air", "color": (0.0, 0.0, 0.0, 0.0), "mass": 0.0},                      # Transparent
    1: {"name": "Prefab Composite", "color": (0.65, 0.6, 0.55, 1.0), "mass": 1.2},       # Tan - Modular colony buildings
    2: {"name": "Regolith Concrete", "color": (0.25, 0.2, 0.18, 1.0), "mass": 2.2},      # Dark brown - Local roads/foundations
    3: {"name": "Concrete", "color": (0.5, 0.5, 0.5, 1.0), "mass": 2.4},                 # Gray - Standard structures
    4: {"name": "Flesh", "color": (1.0, 0.6, 0.6, 1.0), "mass": 1.0},                    # Pink - Organic matter
    5: {"name": "Durasteel", "color": (0.28, 0.3, 0.32, 1.0), "mass": 7.8},              # Blue-gray - Reinforced metal alloy
    
    # Terrain Materials (6-10)
    6: {"name": "Regolith", "color": (0.4, 0.3, 0.2, 1.0), "mass": 1.6},                 # Brown - Alien planet soil
    7: {"name": "Xenoflora", "color": (0.15, 0.55, 0.5, 1.0), "mass": 0.6},              # Teal - Alien plant life
    8: {"name": "Basalt", "color": (0.25, 0.25, 0.27, 1.0), "mass": 2.9},                # Dark gray - Volcanic rock
    9: {"name": "Wood", "color": (0.6, 0.4, 0.2, 1.0), "mass": 0.7},                     # Tan - Imported lumber
    10: {"name": "Transparent Aluminum", "color": (0.85, 0.92, 1.0, 0.4), "mass": 3.9},  # Light blue - Sci-fi transparent metal
    
    # Utility/Clothing (11-12)
    11: {"name": "Uniform", "color": (0.2, 0.3, 0.2, 1.0), "mass": 0.3},                 # Dark green - Military uniform
    12: {"name": "Bone", "color": (0.9, 0.86, 0.78, 1.0), "mass": 1.8},                   # White/Beige - Skeleton bones (indestructible)
    
    # Damage States (13-15)
    13: {"name": "Damaged Concrete", "color": (0.85, 0.15, 0.15, 1.0), "mass": 2.0},     # Crimson - Battle damage
    14: {"name": "Damaged Steel", "color": (0.9, 0.4, 0.1, 1.0), "mass": 7.0},           # Orange - Heat damage
    15: {"name": "Damaged Armor", "color": (0.8, 0.2, 0.2, 1.0), "mass": 6.0},           # Dark red - Heavy damage
    
    # Advanced Materials (16-20)
    16: {"name": "Ablative Plating", "color": (0.15, 0.16, 0.18, 1.0), "mass": 5.0},     # Charcoal - Heat-resistant armor
    17: {"name": "Reactive Armor", "color": (0.35, 0.38, 0.36, 1.0), "mass": 6.5},       # Gunmetal - Explosive tiles
    18: {"name": "Foam-Crete", "color": (0.75, 0.75, 0.78, 1.0), "mass": 0.5},           # Light gray - Quick-deploy foam
    19: {"name": "Nanomesh Fabric", "color": (0.2, 0.25, 0.3, 1.0), "mass": 0.4},        # Dark blue - Smart fabric
    20: {"name": "Plasteel Panels", "color": (0.45, 0.48, 0.5, 1.0), "mass": 4.0},       # Medium gray - Plastic-metal hybrid
    
    # Skeleton Materials (21)
    21: {"name": "Joint", "color": (1.0, 0.25, 0.25, 1.0), "mass": 1.8},                  # Red - Skeleton joints (indestructible)
}

# Default paint materials (all available materials for painting)
DEFAULT_MATERIALS = [
    0,   # Air
    1,   # Prefab Composite
    2,   # Regolith Concrete
    3,   # Concrete
    4,   # Flesh
    5,   # Durasteel
    6,   # Regolith
    7,   # Xenoflora
    8,   # Basalt
    9,   # Wood
    10,  # Transparent Aluminum
    11,  # Uniform
    13,  # Damaged Concrete
    14,  # Damaged Steel
    15,  # Damaged Armor
    16,  # Ablative Plating
    17,  # Reactive Armor
    18,  # Foam-Crete
    19,  # Nanomesh Fabric
    20,  # Plasteel Panels
    12,  # Bone
    21,  # Joint
]

def get_material_name(material_id):
    """Get material name from ID"""
    return MATERIALS.get(material_id, {}).get("name", f"Unknown ({material_id})")

def get_material_color(material_id):
    """Get RGBA color for material (0-1 range)"""
    return MATERIALS.get(material_id, {}).get("color", (1.0, 1.0, 1.0, 1.0))

def get_material_color_255(material_id):
    """Get RGBA color for material (0-255 range for Qt)"""
    color = get_material_color(material_id)
    return tuple(int(c * 255) for c in color)


# ============================================================ MASS / DENSITY
#
# 'mass' is a relative density per voxel. A rigged bone's physics mass is the
# sum of its voxels' material masses (computed at rig export). User overrides
# are persisted next to this module so they survive restarts.

import os as _os
import json as _json

_PROPS_PATH = _os.path.join(_os.path.dirname(_os.path.abspath(__file__)),
                            "material_properties.json")

# Pristine defaults captured before any user override is applied, so the editor
# can offer a "Reset to defaults" action.
DEFAULT_MATERIAL_MASS = {mid: float(m.get("mass", 1.0)) for mid, m in MATERIALS.items()}


def get_material_mass(material_id):
    """Relative density per voxel for a material (defaults to 1.0 if unknown)."""
    return float(MATERIALS.get(material_id, {}).get("mass", 1.0))


def set_material_mass(material_id, mass):
    """Set a material's per-voxel mass (in memory; call save_material_properties to persist)."""
    if material_id in MATERIALS:
        MATERIALS[material_id]["mass"] = max(0.0, float(mass))


def material_mass_table():
    """id -> mass map, suitable for embedding in a .stasset or exporting."""
    return {mid: get_material_mass(mid) for mid in MATERIALS}


def save_material_properties():
    """Persist current per-material mass overrides to material_properties.json."""
    data = {str(mid): {"mass": get_material_mass(mid)} for mid in MATERIALS}
    try:
        with open(_PROPS_PATH, "w", encoding="utf-8") as f:
            _json.dump(data, f, indent=2)
        print(f"\u2705 Saved material properties: {_PROPS_PATH}")
        return True
    except Exception as e:
        print(f"\u26a0\ufe0f Failed to save material properties: {e}")
        return False


def load_material_properties():
    """Load per-material mass overrides from material_properties.json (if present)."""
    if not _os.path.exists(_PROPS_PATH):
        return
    try:
        with open(_PROPS_PATH, "r", encoding="utf-8") as f:
            data = _json.load(f)
        for key, props in data.items():
            mid = int(key)
            if mid in MATERIALS and "mass" in props:
                MATERIALS[mid]["mass"] = max(0.0, float(props["mass"]))
    except Exception as e:
        print(f"\u26a0\ufe0f Failed to load material properties: {e}")


def reset_material_masses():
    """Restore all per-material masses to their built-in defaults (in memory)."""
    for mid, mass in DEFAULT_MATERIAL_MASS.items():
        MATERIALS[mid]["mass"] = mass


# Apply any saved overrides at import time.
load_material_properties()
