# Steel City: Mob Sim — Scale Reference Models
# mob_sim_reference_models.py
#
# Reference objects scaled to the Mob Sim universe where the NPC wise guy
# (0.48m tall) is the "human." All objects are at building voxel scale
# (voxelSize = 0.1m) for use alongside building generation in VoxelAssetStudio.

import numpy as np
from mob_materials import *

# Mob Sim scale constants
BUILDING_VOXEL_SIZE = 0.1   # meters per building voxel
CHAR_VOXEL_SIZE = 0.015     # meters per character voxel
NPC_HEIGHT_M = 0.48         # wise guy height in meters
SCALE_RATIO = 3.75          # real-world size / mob sim size


def real_to_mob_sim(meters):
    """Convert real-world meters to Mob Sim meters."""
    return meters / SCALE_RATIO


def mob_sim_to_building_voxels(meters):
    """Convert Mob Sim meters to building voxel count."""
    return round(meters / BUILDING_VOXEL_SIZE)


def real_to_building_voxels(meters):
    """Convert real-world meters directly to building voxel count."""
    return mob_sim_to_building_voxels(real_to_mob_sim(meters))


class MobSimReferenceModel:
    """A scale reference object for the Mob Sim universe."""

    def __init__(self, name, voxels, mob_sim_size_m, real_size_m, position, icon="📏"):
        self.name = name
        self.voxels = voxels
        self.mob_sim_size_m = mob_sim_size_m
        self.real_size_m = real_size_m
        self.position = position
        self.icon = icon
        self.visible = True
        self.opacity = 0.3
        self.color_tint = (1.0, 1.0, 0.5)

    def get_voxel_size(self):
        return self.voxels.shape

    def get_info_text(self):
        v = self.get_voxel_size()
        m = self.mob_sim_size_m
        r = self.real_size_m
        return (
            f"{self.icon} {self.name}\n"
            f"─────────────────\n"
            f"Voxels: {v[0]}×{v[1]}×{v[2]}\n"
            f"Mob Sim: {m[0]:.2f}m × {m[1]:.2f}m × {m[2]:.2f}m\n"
            f"Real equivalent: {r[0]:.1f}m × {r[1]:.1f}m × {r[2]:.1f}m"
        )


class MobSimReferenceLibrary:
    """Manages all Mob Sim scale reference models."""

    def __init__(self):
        self.models = []
        self.load_default_references()

    def load_default_references(self):
        print("📏 Loading Mob Sim scale reference models...")

        # Mob Sim NPC (wise guy equivalent) — 0.48m tall
        npc = self._generate_npc()
        self.add_model(
            "Mob Sim NPC", npc,
            (0.24, 0.48, 0.15),
            real_size_m=(0.9, 1.8, 0.6),
            position=(-60, 0, -30),
            icon="🧍"
        )

        # Standard door — 0.4m tall (4 building voxels)
        door = self._generate_box((4, 4, 2), DOOR_BROWN)
        self.add_model(
            "Standard Door", door,
            (0.4, 0.4, 0.2),
            real_size_m=(1.5, 1.5, 0.75),
            position=(-60, 0, -20),
            icon="🚪"
        )

        # Trash can — 0.27m tall
        trash = self._generate_cylinder(radius=2, height=3, material=CONCRETE)
        self.add_model(
            "Trash Can", trash,
            (0.4, 0.3, 0.4),
            real_size_m=(0.6, 1.0, 0.6),
            position=(-60, 0, -10),
            icon="🗑️"
        )

        # Bench — 0.13m tall
        bench = self._generate_box((8, 1, 3), DARK_WOOD)
        self.add_model(
            "Bench", bench,
            (0.8, 0.1, 0.3),
            real_size_m=(3.0, 0.5, 1.2),
            position=(-60, 0, 0),
            icon="🪑"
        )

        # Street light — 1.07m tall
        light = self._generate_cylinder(radius=1, height=11, material=WINDOW_GLASS)
        self.add_model(
            "Street Light", light,
            (0.2, 1.1, 0.2),
            real_size_m=(0.3, 4.0, 0.3),
            position=(-60, 0, 10),
            icon="💡"
        )

        # Car — 0.4m tall
        car = self._generate_box((12, 4, 6), GARAGE_METAL)
        self.add_model(
            "Car", car,
            (1.2, 0.4, 0.6),
            real_size_m=(4.5, 1.5, 2.2),
            position=(-60, 0, 25),
            icon="🚗"
        )

        # Dumpster — 0.53m tall
        dumpster = self._generate_box((6, 5, 4), CONCRETE)
        self.add_model(
            "Dumpster", dumpster,
            (0.6, 0.5, 0.4),
            real_size_m=(1.8, 2.0, 1.2),
            position=(-60, 0, 40),
            icon="🚮"
        )

        # Tree — 1.6m tall
        tree = self._generate_tree(trunk_radius=1, trunk_h=6, canopy_radius=4, canopy_h=10)
        self.add_model(
            "Tree", tree,
            (0.8, 1.6, 0.8),
            real_size_m=(1.5, 6.0, 1.5),
            position=(-60, 0, 55),
            icon="🌳"
        )

        # Building height references
        barber = self._generate_box((8, 16, 8), STUCCO_WHITE)
        self.add_model(
            "Barber Shop (16v)", barber,
            (0.8, 1.6, 0.8),
            real_size_m=(3.2, 6.0, 3.2),
            position=(-40, 0, -20),
            icon="💈"
        )

        police = self._generate_box((8, 26, 8), STONE_FOUNDATION)
        self.add_model(
            "Police Station (26v)", police,
            (0.8, 2.6, 0.8),
            real_size_m=(3.2, 9.75, 3.2),
            position=(-40, 0, 0),
            icon="🏛️"
        )

        apts = self._generate_box((8, 36, 8), RED_BRICK)
        self.add_model(
            "Apartments (36v)", apts,
            (0.8, 3.6, 0.8),
            real_size_m=(3.2, 13.5, 3.2),
            position=(-40, 0, 25),
            icon="🏢"
        )

        print(f"✅ Loaded {len(self.models)} Mob Sim reference models")

    def add_model(self, name, voxels, mob_sim_size_m, real_size_m, position, icon="📏"):
        model = MobSimReferenceModel(name, voxels, mob_sim_size_m, real_size_m, position, icon)
        self.models.append(model)
        v = voxels.shape
        print(f"   {icon} {name}: {v[0]}×{v[1]}×{v[2]} voxels "
              f"= {mob_sim_size_m[0]:.2f}m × {mob_sim_size_m[1]:.2f}m "
              f"(real: {real_size_m[0]:.1f}m × {real_size_m[1]:.1f}m)")
        return model

    def get_model_by_name(self, name):
        for model in self.models:
            if model.name == name:
                return model
        return None

    def toggle_model(self, name, visible):
        model = self.get_model_by_name(name)
        if model:
            model.visible = visible

    def get_visible_models(self):
        return [m for m in self.models if m.visible]

    # ========== SHAPE GENERATORS ==========

    def _generate_box(self, size, material):
        voxels = np.zeros(size, dtype=np.uint16)
        voxels[:, :, :] = material
        return voxels

    def _generate_cylinder(self, radius, height, material):
        size = (radius * 2 + 1, height, radius * 2 + 1)
        voxels = np.zeros(size, dtype=np.uint16)
        center = radius
        for x in range(size[0]):
            for z in range(size[2]):
                dx = x - center
                dz = z - center
                dist = np.sqrt(dx * dx + dz * dz)
                if dist <= radius:
                    voxels[x, :, z] = material
        return voxels

    def _generate_sphere(self, radius, material):
        size = radius * 2 + 1
        voxels = np.zeros((size, size, size), dtype=np.uint16)
        center = radius
        for x in range(size):
            for y in range(size):
                for z in range(size):
                    dx, dy, dz = x - center, y - center, z - center
                    if np.sqrt(dx * dx + dy * dy + dz * dz) <= radius:
                        voxels[x, y, z] = material
        return voxels

    def _generate_npc(self):
        """Simple NPC silhouette: head, body, legs."""
        voxels = np.zeros((3, 5, 2), dtype=np.uint16)
        # Legs (y=0-1)
        voxels[0:3, 0:2, 0:2] = BLACK_FABRIC
        # Torso (y=2-3)
        voxels[0:3, 2:4, 0:2] = BLACK_FABRIC
        # Head (y=4)
        voxels[0:3, 4:5, 0:2] = FLESH_SKIN
        return voxels

    def _generate_tree(self, trunk_radius, trunk_h, canopy_radius, canopy_h):
        total_h = trunk_h + canopy_h
        total_w = canopy_radius * 2 + 1
        voxels = np.zeros((total_w, total_h, total_w), dtype=np.uint16)
        # Trunk
        trunk = self._generate_cylinder(trunk_radius, trunk_h, DARK_WOOD)
        cx = total_w // 2
        voxels[cx - trunk_radius:cx + trunk_radius + 1, 0:trunk_h,
               cx - trunk_radius:cx + trunk_radius + 1] = trunk
        # Cantry (sphere)
        canopy = self._generate_sphere(canopy_radius, WINDOW_GLASS)
        canopy_h_actual = canopy.shape[1]  # sphere height = diameter
        cy = trunk_h
        voxels[:, cy:cy + canopy_h_actual, :] = canopy
        return voxels


if __name__ == "__main__":
    lib = MobSimReferenceLibrary()
    print(f"\n{'='*60}")
    print("Mob Sim Scale Reference Summary")
    print(f"{'='*60}")
    print(f"Scale ratio: 1:{SCALE_RATIO} (real → mob sim)")
    print(f"Building voxel: {BUILDING_VOXEL_SIZE}m")
    print(f"Character voxel: {CHAR_VOXEL_SIZE}m")
    print(f"NPC height: {NPC_HEIGHT_M}m ({int(NPC_HEIGHT_M / CHAR_VOXEL_SIZE)} char voxels)")
    print(f"NPC height: {NPC_HEIGHT_M}m ({int(NPC_HEIGHT_M / BUILDING_VOXEL_SIZE)} building voxels)")
    print(f"Standard door: 4 building voxels = {4 * BUILDING_VOXEL_SIZE}m")
