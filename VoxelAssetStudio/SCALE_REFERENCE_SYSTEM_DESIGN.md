# Scale Reference System - Design Document

**Version:** 1.0  
**Date:** June 29, 2026  
**Status:** 🚀 Ready for Implementation  
**Priority:** P0 (Top Priority)

---

## 🎯 Vision

Create an **atomic scale pipeline** from real world → Unity → voxels by placing physical 3D reference models directly in the viewport workspace. These models act as "scale rulers" that are always visible while working, ensuring accurate real-world proportions.

---

## 📐 The Atomic Scale Pipeline

```
REAL WORLD → UNITY → VOXELS
   (meters)   (units)  (count)

Core Conversion:
├─ 1 Unity Unit = 1 Meter
├─ 8 Voxels = 1 Meter
└─ 1 Voxel = 0.125 meters (12.5 cm)

Example: Basketball
├─ Real World: 0.24m diameter
├─ Unity: 0.24 units (1:1 with meters)
└─ Voxels: 2×2×2 (0.24m ÷ 0.125m/voxel ≈ 2 voxels)

Example: Street Light
├─ Real World: 0.3m × 4.0m tall
├─ Unity: 0.3 × 4.0 units
└─ Voxels: 2×32×2 (4.0m × 8 voxels/m = 32 voxels)
```

---

## 🎨 Visual Implementation

### In-Viewport Reference Models

Reference models are **actual voxel objects** rendered in the 3D viewport:

```
┌─────────────────────────────────────────────────────────┐
│  [Top] [Front] [Side] [Iso] [Persp]  ← Camera buttons  │
├─────────────────────────────────────────────────────────┤
│                                                         │
│    📏 REFERENCE MODELS (left side, semi-transparent)    │
│    ┌─────────────────────────────────────────┐         │
│    │  🏀 Basketball (2v = 0.24m)             │         │
│    │  🗑️ Trash Can (5×8v = 0.6×1.0m)        │         │
│    │  🧍 Human (4×14v = 0.5×1.8m)           │         │
│    │  🚮 Dumpster (14×16v = 1.8×2.0m)       │         │
│    │  💡 Street Light (2×32v = 0.3×4.0m)    │         │
│    │  🌳 Tree (12×48v = 1.5×6.0m)           │         │
│    └─────────────────────────────────────────┘         │
│                                                         │
│              YOUR WORKING MODEL                         │
│              (Center, origin 0,0,0)                     │
│                                                         │
│    Grid: 8-voxel major lines = 1 meter markers         │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

### Key Features

1. **Always Visible** - References stay in viewport while you work
2. **Semi-Transparent** - 30% opacity, yellow tint, non-intrusive
3. **Fixed Position** - Positioned at X=-20, staggered Y/Z to avoid overlap
4. **Toggleable** - Checkbox panel to show/hide individual models
5. **Hover Info** - Shows dimensions when hovering over reference

---

## 🏗️ Architecture

### Core Classes

```python
class ReferenceModel:
    """A scale reference object in the viewport"""
    - name: str                    # "Basketball", "Human", etc.
    - voxels: np.ndarray          # 3D voxel data
    - real_size_m: tuple          # (width, height, depth) in meters
    - position: tuple             # (x, y, z) in viewport space
    - visible: bool               # Toggle visibility
    - opacity: float              # 0.3 (semi-transparent)
    - color_tint: tuple           # (1.0, 1.0, 0.5) yellow tint

class ReferenceModelLibrary:
    """Manages all reference models"""
    - models: List[ReferenceModel]
    - load_default_references()   # Load 6 standard models
    - add_model()                 # Add custom reference
    - toggle_model()              # Show/hide specific model
    - get_model_at_position()     # For hover detection

class ReferenceModelPanel(QWidget):
    """UI panel to control references"""
    - Checkbox list for each model
    - Info card showing scale conversion
    - Hover tooltips with dimensions
```

---

## 📊 Reference Model Library

### Standard Reference Models

| Model | Real Size | Unity Size | Voxel Size | Position | Purpose |
|-------|-----------|------------|------------|----------|---------|
| **Basketball** | 0.24m ⌀ | 0.24 units | 2×2×2 | (-20, 1, -20) | Small object reference |
| **Trash Can** | 0.6m × 1.0m | 0.6 × 1.0 | 5×8×5 | (-20, 4, -15) | Medium object |
| **Human** | 0.5m × 1.8m | 0.5 × 1.8 | 4×14×4 | (-20, 7, 15) | Character scale |
| **Dumpster** | 1.8m × 2.0m | 1.8 × 2.0 | 14×16×10 | (-20, 8, -5) | Large object |
| **Street Light** | 0.3m × 4.0m | 0.3 × 4.0 | 2×32×2 | (-20, 16, 5) | Tall reference |
| **Tree** | 1.5m × 6.0m | 1.5 × 6.0 | 12×48×12 | (-20, 24, 20) | Very tall reference |

### Positioning Strategy

- **X-axis:** All at X = -20 (left side of viewport)
- **Y-axis:** Staggered heights to prevent overlap
- **Z-axis:** Spread out for depth separation
- **Working model:** Stays at origin (0, 0, 0)

---

## 🎯 Use Cases

### Use Case 1: Creating a Fence

**Goal:** Create a chest-high fence section

1. Look at Human reference (4×14v = 0.5m × 1.8m)
2. Chest height ≈ 1.2m (2/3 of human height)
3. Calculate: 1.2m × 8 voxels/m = 9.6 voxels ≈ **10 voxels tall**
4. Create fence: 16×10×2 voxels (2.0m × 1.2m × 0.25m)
5. Export to Unity: Automatically correct scale ✅

### Use Case 2: Basketball Hoop

**Goal:** Create regulation basketball hoop (3.05m tall)

1. Calculate: 3.05m × 8 voxels/m = 24.4 voxels ≈ **24 voxels**
2. Compare: Street light is 32v (4m), hoop should be 3/4 that height
3. Visual check: Hoop should be ~1.7× human height
4. Create: Pole = 2×24×2 voxels, backboard at top
5. Export: Unity receives 0.25m × 3.0m × 0.25m object ✅

### Use Case 3: Building a House

**Goal:** Create single-story house with 2.5m ceiling

1. Calculate: 2.5m × 8 voxels/m = **20 voxels tall**
2. Visual check: Slightly taller than dumpster (16v)
3. Door height: 2.0m = 16 voxels (human + headroom)
4. Window height: 1.2m = 10 voxels (chest-high)
5. Create: 32×20×24 voxels (4m × 2.5m × 3m) ✅

---

## 🚀 Implementation Plan

### Phase 1: Core System (Day 1 - 6 hours)

**Goal:** Get reference models rendering in viewport

1. **Create `reference_models.py` module** (1 hour)
   - `ReferenceModel` class
   - `ReferenceModelLibrary` class
   - Generate 6 default models (simple shapes)

2. **Integrate with viewport** (2 hours)
   - Add `self.reference_library` to `VoxelViewport`
   - Implement `_render_reference_models()`
   - Position models at X=-20 with staggered Y/Z

3. **Semi-transparent rendering** (1 hour)
   - Set opacity to 0.3
   - Add yellow tint (1.0, 1.0, 0.5)
   - Ensure references don't block working model

4. **Reference panel UI** (2 hours)
   - Create `ReferenceModelPanel` widget
   - Add checkbox for each model
   - Wire toggle signals to viewport
   - Add to right sidebar

**Deliverable:** 6 reference models visible in viewport, toggleable via checkboxes

---

### Phase 2: Enhanced Features (Day 2 - 4 hours)

**Goal:** Add hover info and grid system

1. **Hover detection** (1.5 hours)
   - Detect mouse over reference models
   - Show info overlay with dimensions
   - Format: "Basketball | 2×2×2v | 0.24m | 0.24 Unity units"

2. **Grid overlay system** (2 hours)
   - Major grid lines every 8 voxels (1 meter)
   - Minor grid lines every 1 voxel
   - Dimension labels at intersections
   - Toggle on/off with 'G' key

3. **Info card** (0.5 hours)
   - Permanent card showing scale conversion
   - "8 voxels = 1 meter | 1 voxel = 12.5cm"
   - Position in corner of viewport

**Deliverable:** Hover tooltips, grid overlay, scale info card

---

### Phase 3: Camera Modes (Day 3 - 4 hours)

**Goal:** Add orthographic and isometric views

1. **Camera mode system** (2 hours)
   - Implement 5 modes: Perspective, Top, Front, Side, Isometric
   - Add toolbar buttons
   - Lock rotation in ortho/iso modes

2. **Isometric view** (1 hour)
   - 45° horizontal, 35.264° vertical
   - Orthographic projection (no perspective distortion)
   - Perfect for voxel art

3. **Measurement ruler** (1 hour)
   - Click-drag to measure distance
   - Show result in voxels AND meters
   - Line stays visible until cleared

**Deliverable:** 5 camera modes, measurement tool

---

## 📋 Testing Checklist

### Reference Model System
- [ ] All 6 models render correctly
- [ ] Semi-transparent (30% opacity)
- [ ] Yellow tint applied
- [ ] Positioned at X=-20, no overlap
- [ ] Toggle checkboxes work
- [ ] Models don't interfere with working model

### Hover System
- [ ] Hover detection works for all models
- [ ] Info overlay shows correct dimensions
- [ ] Tooltip disappears when mouse leaves
- [ ] No performance impact

### Grid System
- [ ] Major lines every 8 voxels (1 meter)
- [ ] Minor lines every 1 voxel
- [ ] Labels show at intersections
- [ ] Toggle on/off works
- [ ] Grid doesn't obscure voxels

### Camera Modes
- [ ] All 5 modes work correctly
- [ ] Isometric view at correct angle
- [ ] Rotation locked in ortho/iso
- [ ] Smooth transitions between modes
- [ ] Reference models visible in all modes

### Scale Pipeline
- [ ] Create 10-voxel tall object → 1.25m in Unity ✅
- [ ] Create 32-voxel tall object → 4.0m in Unity ✅
- [ ] Basketball reference matches real size ✅
- [ ] Human reference matches real size ✅

---

## 🎨 Visual Design

### Reference Model Appearance

```python
# Semi-transparent yellow tint
color = (1.0, 1.0, 0.5, 0.3)  # RGBA

# Render as scatter plot
GLScatterPlotItem(
    pos=coords,
    color=color,
    size=voxel_size,
    pxMode=False
)
```

### Grid Appearance

```python
# Major grid lines (1 meter)
major_color = (0.5, 0.5, 0.5, 0.8)  # Bright gray
major_width = 2.0

# Minor grid lines (1 voxel)
minor_color = (0.3, 0.3, 0.3, 0.3)  # Dim gray
minor_width = 1.0
```

### Info Overlay

```python
# Hover tooltip
background = rgba(0, 0, 0, 200)  # Black, 80% opacity
text_color = white
font_size = 12px
padding = 8px
border_radius = 4px
```

---

## 🔧 Technical Details

### Voxel Size Calculation

```python
def real_to_voxels(meters):
    """Convert real-world meters to voxel count"""
    return round(meters * 8)  # 8 voxels per meter

def voxels_to_real(voxels):
    """Convert voxel count to real-world meters"""
    return voxels * 0.125  # 0.125 meters per voxel

def voxels_to_unity(voxels):
    """Convert voxel count to Unity units"""
    return voxels * 0.125  # 1 Unity unit = 1 meter
```

### Reference Model Generation

```python
def generate_reference_sphere(radius_voxels, material_id):
    """Generate sphere reference model"""
    size = radius_voxels * 2 + 1
    voxels = np.zeros((size, size, size), dtype=np.uint16)
    center = radius_voxels
    
    for x in range(size):
        for y in range(size):
            for z in range(size):
                dx = x - center
                dy = y - center
                dz = z - center
                dist = np.sqrt(dx*dx + dy*dy + dz*dz)
                
                if dist <= radius_voxels:
                    voxels[x, y, z] = material_id
    
    return voxels

def generate_reference_box(size, material_id):
    """Generate box reference model"""
    voxels = np.zeros(size, dtype=np.uint16)
    voxels[:, :, :] = material_id
    return voxels

def generate_reference_cylinder(radius, height, material_id):
    """Generate cylinder reference model"""
    size = (radius*2+1, height, radius*2+1)
    voxels = np.zeros(size, dtype=np.uint16)
    center = radius
    
    for x in range(size[0]):
        for z in range(size[2]):
            dx = x - center
            dz = z - center
            dist = np.sqrt(dx*dx + dz*dz)
            
            if dist <= radius:
                voxels[x, :, z] = material_id
    
    return voxels
```

---

## 📚 Benefits

### For Users
1. **Intuitive Scale Understanding** - See real objects while working
2. **No Mental Math** - Visual comparison instead of calculations
3. **Prevents Scale Mistakes** - "This building is taller than a tree? Wrong!"
4. **Confidence** - Always know if proportions are correct
5. **Speed** - No need to export/test/iterate in Unity

### For Pipeline
1. **True 1:1 Scale** - Real world → Unity → Voxels all match
2. **Atomic Conversion** - Single source of truth (8 voxels = 1 meter)
3. **No Guesswork** - Exact measurements at every step
4. **Unity-Ready** - Exports are automatically correct scale
5. **Reversible** - Can work backwards from Unity to voxels

---

## 🎯 Success Criteria

- [ ] User can create 1.8m tall character → matches Human reference ✅
- [ ] User can create 4m tall streetlight → matches Light reference ✅
- [ ] Exported models have correct scale in Unity (no adjustment needed) ✅
- [ ] Reference models are non-intrusive (semi-transparent, off to side) ✅
- [ ] Hover info provides instant dimension feedback ✅
- [ ] Grid overlay helps with meter-based measurements ✅

---

**Status:** 📋 Design Complete - Ready for Implementation  
**Next Step:** Begin Phase 1 implementation (reference_models.py module)

