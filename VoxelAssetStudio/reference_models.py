# Steel Tide: Voxel Asset Studio
# reference_models.py - Scale reference system for viewport

import numpy as np
from material_library import MATERIALS

# Scale conversion constants
VOXELS_PER_METER = 8
METERS_PER_VOXEL = 0.125  # 12.5 cm

def real_to_voxels(meters):
    """Convert real-world meters to voxel count"""
    return round(meters * VOXELS_PER_METER)

def voxels_to_real(voxels):
    """Convert voxel count to real-world meters"""
    return voxels * METERS_PER_VOXEL

def voxels_to_unity(voxels):
    """Convert voxel count to Unity units (1 unit = 1 meter)"""
    return voxels * METERS_PER_VOXEL


class ReferenceModel:
    """A scale reference object in the viewport"""
    
    def __init__(self, name, voxels, real_size_m, position, icon="📏"):
        """
        Args:
            name: Display name (e.g. "Basketball")
            voxels: 3D numpy array of voxel data
            real_size_m: Tuple (width, height, depth) in meters
            position: Tuple (x, y, z) in viewport space
            icon: Emoji icon for UI display
        """
        self.name = name
        self.voxels = voxels
        self.real_size_m = real_size_m
        self.position = position
        self.icon = icon
        
        # Visual properties
        self.visible = True
        self.opacity = 0.3  # Semi-transparent
        self.color_tint = (1.0, 1.0, 0.5)  # Yellow tint
        
    def get_voxel_size(self):
        """Return size in voxels (width, height, depth)"""
        return self.voxels.shape
    
    def get_unity_size(self):
        """Return size in Unity units (1 unit = 1 meter)"""
        return self.real_size_m
    
    def get_info_text(self):
        """Return formatted info string for tooltips"""
        voxel_size = self.get_voxel_size()
        unity_size = self.get_unity_size()
        
        return (
            f"{self.icon} {self.name}\n"
            f"─────────────────\n"
            f"Voxels: {voxel_size[0]}×{voxel_size[1]}×{voxel_size[2]}\n"
            f"Unity: {unity_size[0]:.2f}×{unity_size[1]:.2f}×{unity_size[2]:.2f} units\n"
            f"Real: {unity_size[0]:.2f}m × {unity_size[1]:.2f}m × {unity_size[2]:.2f}m"
        )


class ReferenceModelLibrary:
    """Manages all reference models in the scene"""
    
    def __init__(self):
        self.models = []
        self.load_default_references()
    
    def load_default_references(self):
        """Load standard reference objects"""
        print("📏 Loading scale reference models...")
        
        # Basketball: 0.24m diameter
        # Position: Y=0 (sitting on ground) - left edge of viewport
        basketball = self._generate_sphere(radius=1, material=1)  # Prefab Composite (tan)
        self.add_model(
            "Basketball", 
            basketball, 
            (0.24, 0.24, 0.24), 
            position=(-120, 0, -60),  # Left edge, spread out
            icon="🏀"
        )
        
        # Trash Can: 0.6m × 1.0m tall
        # Position: Y=0 (sitting on ground) - left edge of viewport
        trash_can = self._generate_cylinder(radius=2, height=8, material=3)  # Concrete (gray)
        self.add_model(
            "Trash Can", 
            trash_can, 
            (0.6, 1.0, 0.6),
            position=(-120, 0, -30),  # Left edge, spread out
            icon="🗑️"
        )
        
        # Human: 0.5m × 1.8m tall
        # Position: Y=0 (standing on ground) - left edge of viewport
        human = self._generate_box((4, 14, 4), material=4)  # Flesh (skin tone)
        self.add_model(
            "Human", 
            human, 
            (0.5, 1.8, 0.5),
            position=(-120, 0, 0),  # Left edge, spread out
            icon="🧍"
        )
        
        # Dumpster: 1.8m × 2.0m × 1.2m
        # Position: Y=0 (sitting on ground) - left edge of viewport
        dumpster = self._generate_box((14, 16, 10), material=5)  # Durasteel (blue-gray)
        self.add_model(
            "Dumpster", 
            dumpster, 
            (1.8, 2.0, 1.2),
            position=(-120, 0, 30),  # Left edge, spread out
            icon="🚮"
        )
        
        # Street Light: 0.3m × 4.0m tall
        # Position: Y=0 (base on ground) - left edge of viewport
        light = self._generate_cylinder(radius=1, height=32, material=10)  # Transparent Aluminum (light blue)
        self.add_model(
            "Street Light", 
            light, 
            (0.3, 4.0, 0.3),
            position=(-120, 0, 60),  # Left edge, spread out
            icon="💡"
        )
        
        # Tree: 1.5m × 6.0m tall (trunk + canopy)
        # Position: Y=0 (trunk base on ground) - front edge (starts new line for bigger models)
        tree = self._generate_tree(trunk_radius=2, height=48)
        self.add_model(
            "Tree", 
            tree, 
            (1.5, 6.0, 1.5),
            position=(-80, 0, -120),  # Front edge, starts new line
            icon="🌳"
        )
        
        print(f"✅ Loaded {len(self.models)} reference models")
    
    def add_model(self, name, voxels, real_size_m, position, icon="📏"):
        """Add a reference model to the library"""
        model = ReferenceModel(name, voxels, real_size_m, position, icon)
        self.models.append(model)
        
        voxel_size = voxels.shape
        print(f"   {icon} {name}: {voxel_size[0]}×{voxel_size[1]}×{voxel_size[2]} voxels "
              f"= {real_size_m[0]:.2f}m × {real_size_m[1]:.2f}m × {real_size_m[2]:.2f}m")
        
        return model
    
    def get_model_by_name(self, name):
        """Get a reference model by name"""
        for model in self.models:
            if model.name == name:
                return model
        return None
    
    def toggle_model(self, name, visible):
        """Show/hide a specific reference model"""
        model = self.get_model_by_name(name)
        if model:
            model.visible = visible
    
    def get_visible_models(self):
        """Return list of currently visible models"""
        return [m for m in self.models if m.visible]
    
    # ========== SHAPE GENERATORS ==========
    
    def _generate_sphere(self, radius, material):
        """Generate sphere voxel model"""
        size = radius * 2 + 1
        voxels = np.zeros((size, size, size), dtype=np.uint16)
        center = radius
        
        for x in range(size):
            for y in range(size):
                for z in range(size):
                    dx = x - center
                    dy = y - center
                    dz = z - center
                    dist = np.sqrt(dx*dx + dy*dy + dz*dz)
                    
                    if dist <= radius:
                        voxels[x, y, z] = material
        
        return voxels
    
    def _generate_box(self, size, material):
        """Generate box voxel model"""
        voxels = np.zeros(size, dtype=np.uint16)
        voxels[:, :, :] = material
        return voxels
    
    def _generate_cylinder(self, radius, height, material):
        """Generate cylinder voxel model"""
        size = (radius*2+1, height, radius*2+1)
        voxels = np.zeros(size, dtype=np.uint16)
        center = radius
        
        for x in range(size[0]):
            for z in range(size[2]):
                dx = x - center
                dz = z - center
                dist = np.sqrt(dx*dx + dz*dz)
                
                if dist <= radius:
                    voxels[x, :, z] = material
        
        return voxels
    
    def _generate_tree(self, trunk_radius, height):
        """Generate tree voxel model (trunk + canopy)"""
        # Trunk: bottom 2/3
        trunk_height = int(height * 0.66)
        canopy_height = height - trunk_height
        
        # Total size
        canopy_radius = trunk_radius * 3
        size = (canopy_radius*2+1, height, canopy_radius*2+1)
        voxels = np.zeros(size, dtype=np.uint16)
        
        # Generate trunk (Wood material = 9)
        trunk_center_x = canopy_radius
        trunk_center_z = canopy_radius
        
        for x in range(size[0]):
            for z in range(size[2]):
                dx = x - trunk_center_x
                dz = z - trunk_center_z
                dist = np.sqrt(dx*dx + dz*dz)
                
                if dist <= trunk_radius:
                    voxels[x, 0:trunk_height, z] = 9  # Wood
        
        # Generate canopy (Xenoflora material = 7, green)
        canopy_center_y = trunk_height + canopy_height // 2
        
        for x in range(size[0]):
            for y in range(trunk_height, height):
                for z in range(size[2]):
                    dx = x - canopy_radius
                    dy = y - canopy_center_y
                    dz = z - canopy_radius
                    dist = np.sqrt(dx*dx + dy*dy + dz*dz)
                    
                    if dist <= canopy_radius:
                        voxels[x, y, z] = 7  # Xenoflora (green)
        
        return voxels


# ========== UTILITY FUNCTIONS ==========

def get_scale_info_text():
    """Return formatted scale conversion info"""
    return (
        "📏 Scale Reference\n"
        "─────────────────\n"
        f"{VOXELS_PER_METER} voxels = 1 meter\n"
        f"1 voxel = {METERS_PER_VOXEL}m (12.5cm)\n"
        "1 Unity unit = 1 meter"
    )


if __name__ == "__main__":
    # Test reference model generation
    print("Testing reference model library...")
    library = ReferenceModelLibrary()
    
    print("\n📊 Reference Model Summary:")
    for model in library.models:
        print(model.get_info_text())
        print()
