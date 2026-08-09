# Steel City: Mob Sim — 1920s Consolidated Material Palette
# mob_materials.py
#
# 30 physical materials (IDs 0, 100-129). Components are built from these
# base materials. Per-chunk per-material tinting in the shader handles
# variation (warm/cool/burned/boarded) without extra material IDs.
#
# MUST match StAssetReader.cs (Unity).

MOB_MATERIALS = {
    # --- Air ---
    0:   {"name": "Air",              "color": (0.0, 0.0, 0.0, 0.0)},

    # --- Masonry (100-105) ---
    100: {"name": "Red Brick",        "color": (0.58, 0.26, 0.20, 1.0)},
    101: {"name": "Stone",            "color": (0.48, 0.42, 0.34, 1.0)},
    102: {"name": "Concrete",         "color": (0.58, 0.58, 0.54, 1.0)},
    103: {"name": "Stucco",           "color": (0.82, 0.80, 0.74, 1.0)},
    104: {"name": "Asphalt",          "color": (0.18, 0.18, 0.20, 1.0)},
    105: {"name": "Cobblestone",      "color": (0.42, 0.38, 0.34, 1.0)},

    # --- Wood (106-108) ---
    106: {"name": "Dark Wood",        "color": (0.30, 0.18, 0.10, 1.0)},
    107: {"name": "Light Wood",       "color": (0.60, 0.42, 0.25, 1.0)},
    108: {"name": "Weathered Wood",   "color": (0.42, 0.36, 0.26, 1.0)},

    # --- Metal (109-111) ---
    109: {"name": "Dark Iron",        "color": (0.28, 0.24, 0.22, 1.0)},
    110: {"name": "Aged Metal",       "color": (0.42, 0.40, 0.36, 1.0)},
    111: {"name": "Painted Metal",    "color": (0.90, 0.88, 0.82, 1.0)},

    # --- Glass (112-114) ---
    112: {"name": "Window Glass",     "color": (0.45, 0.55, 0.65, 0.6)},
    113: {"name": "Lit Window",       "color": (0.95, 0.85, 0.50, 1.0)},   # emissive
    114: {"name": "Storefront Glass",  "color": (0.55, 0.65, 0.70, 0.5)},

    # --- Neon (115-117) — emissive ---
    115: {"name": "Neon Red",         "color": (0.95, 0.15, 0.15, 1.0)},
    116: {"name": "Neon Blue",        "color": (0.15, 0.30, 0.95, 1.0)},
    117: {"name": "Neon Green",       "color": (0.15, 0.85, 0.25, 1.0)},

    # --- Roofing (118-119) ---
    118: {"name": "Tar",              "color": (0.28, 0.24, 0.20, 1.0)},
    119: {"name": "Terracotta",       "color": (0.55, 0.32, 0.20, 1.0)},

    # --- Painted Surfaces (120-122, 129) ---
    120: {"name": "Painted Red",      "color": (0.45, 0.12, 0.10, 1.0)},
    121: {"name": "Painted Green",    "color": (0.15, 0.28, 0.18, 1.0)},
    122: {"name": "Painted Brown",    "color": (0.22, 0.12, 0.08, 1.0)},
    129: {"name": "Painted Blue",     "color": (0.12, 0.20, 0.45, 1.0)},

    # --- Metal Decorative (123-124) ---
    123: {"name": "Gold/Brass",       "color": (0.78, 0.62, 0.20, 1.0)},
    124: {"name": "Lamp Glow",        "color": (1.0, 0.85, 0.50, 1.0)},    # emissive

    # --- Character (125-128) ---
    125: {"name": "Flesh",            "color": (0.82, 0.68, 0.55, 1.0)},
    126: {"name": "Black Fabric",     "color": (0.06, 0.06, 0.07, 1.0)},
    127: {"name": "White Fabric",     "color": (0.88, 0.86, 0.82, 1.0)},
    128: {"name": "Hair",             "color": (0.12, 0.08, 0.06, 1.0)},
}

# --- Convenience constants for procedural generators ---
AIR = 0

# Masonry
RED_BRICK = 100
STONE = 101
CONCRETE = 102
STUCCO = 103
ASPHALT = 104
COBBLESTONE = 105

# Wood
DARK_WOOD = 106
LIGHT_WOOD = 107
WEATHERED_WOOD = 108

# Metal
DARK_IRON = 109
AGED_METAL = 110
PAINTED_METAL = 111

# Glass
WINDOW_GLASS = 112
LIT_WINDOW = 113
STOREFRONT_GLASS = 114

# Neon
NEON_RED = 115
NEON_BLUE = 116
NEON_GREEN = 117

# Roofing
TAR = 118
TERRACOTTA = 119

# Painted
PAINTED_RED = 120
PAINTED_GREEN = 121
PAINTED_BROWN = 122
PAINTED_BLUE = 129

# Decorative
GOLD_BRASS = 123
LAMP_GLOW = 124

# Character
FLESH = 125
BLACK_FABRIC = 126
WHITE_FABRIC = 127
HAIR = 128

# --- Backward-compatible aliases (for gradual migration) ---
STONE_FOUNDATION = STONE
ASPHALT_ROAD = ASPHALT
SIDEWALK = CONCRETE  # sidewalk = light concrete surface
TAR_ROOF = TAR
METAL_ROOF = AGED_METAL
DARK_BRICK = RED_BRICK  # dark brick = red brick with tint
TAN_BRICK = RED_BRICK   # tan brick = red brick with tint
STUCCO_WHITE = STUCCO
STUCCO_CREAM = STUCCO
WINDOW_FRAME = DARK_WOOD
DOOR_BROWN = PAINTED_BROWN
DOOR_GREEN = PAINTED_GREEN
DOOR_RED = PAINTED_RED
TRIM_WHITE = PAINTED_METAL
TRIM_DARK = DARK_IRON
CHIMNEY_BRICK = RED_BRICK
AWNING_RED = PAINTED_RED
AWNING_GREEN = PAINTED_GREEN
AWNING_STRIPED = PAINTED_METAL  # generator alternates with PAINTED_RED
SIGN_GOLD = GOLD_BRASS
SIGN_DARK = DARK_IRON
BARBER_POLE = PAINTED_METAL  # generator alternates with PAINTED_RED
POLICE_BLUE = PAINTED_BLUE
CASINO_CARPET = PAINTED_RED
SPEAKEASY_DARK = DARK_WOOD
GARAGE_METAL = AGED_METAL
APARTMENT_BEIGE = STUCCO  # warm tint
FIRE_ESCAPE = DARK_IRON
WATER_TOWER = WEATHERED_WOOD
STREET_LAMP = DARK_IRON
HQ_ACCENT = GOLD_BRASS
FLESH_SKIN = FLESH
WHITE_SHIRT = WHITE_FABRIC
BLACK_HAT = BLACK_FABRIC
SUNGLASSES_DARK = DARK_IRON
BLACK_SHOES = BLACK_FABRIC
TIE_DARK_RED = PAINTED_RED
HAIR_DARK = HAIR
OVERCOAT_TAN = LIGHT_WOOD  # tan fabric, closest base
CIGAR_BROWN = DARK_WOOD


def get_material_name(material_id):
    return MOB_MATERIALS.get(material_id, {}).get("name", f"Unknown ({material_id})")


def get_material_color(material_id):
    return MOB_MATERIALS.get(material_id, {}).get("color", (1.0, 1.0, 1.0, 1.0))


def get_material_color_255(material_id):
    c = get_material_color(material_id)
    return tuple(int(ch * 255) for ch in c)
