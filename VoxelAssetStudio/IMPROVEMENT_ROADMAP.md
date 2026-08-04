# Voxel Asset Studio - Improvement Roadmap

**Version:** 2.0 Design Document  
**Date:** June 29, 2026  
**Status:** 📋 Planning Phase  
**Author:** Development Team

---

## 📊 Executive Summary

This document outlines a comprehensive improvement plan for the Voxel Asset Studio, transforming it from a basic voxel editor into a professional-grade asset creation tool for Steel Tide: First Device.

### Current State
- **Version:** 1.2.0
- **Status:** Functional but basic
- **Primary Use:** Simple voxel painting and procedural generation
- **Main Limitation:** No undo/redo, limited editing tools, primitive workflow

### Target State
- **Version:** 2.0.0
- **Status:** Professional asset creation suite
- **Capabilities:** Full editing workflow with undo/redo, selection tools, layers, advanced viewport
- **Timeline:** 4-6 weeks (phased implementation)

---

## 🎯 Design Goals

### Primary Objectives
1. **Professional Workflow** - Undo/redo, copy/paste, selection tools
2. **Efficiency** - Reduce repetitive manual work by 80%
3. **Flexibility** - Support complex models with layers and organization
4. **Speed** - Maintain 60 FPS with advanced features
5. **Unity Integration** - Seamless .stasset workflow preservation

### Non-Goals (Out of Scope)
- Animation system (future consideration)
- Multiplayer editing (not needed)
- Scripting/plugin system (defer to v3.0)
- Non-voxel formats as primary (export only)

---

## 📋 Table of Contents

1. [Current State Analysis](#current-state-analysis)
2. [Feature Gap Analysis](#feature-gap-analysis)
3. [Architecture Improvements](#architecture-improvements)
4. [Phase 1: Core Workflow](#phase-1-core-workflow)
5. [Phase 2: Advanced Editing](#phase-2-advanced-editing)
6. [Phase 3: Organization](#phase-3-organization)
7. [Phase 4: Polish](#phase-4-polish)
8. [Technical Specifications](#technical-specifications)
9. [Implementation Timeline](#implementation-timeline)
10. [Risk Assessment](#risk-assessment)

---

## 🔍 Current State Analysis

### Strengths ✅

#### Architecture
- **PyQt6 + pyqtgraph OpenGL** - Proven stack (same as Kalshi Dashboard)
- **Clean module separation** - Editor, viewport, tools, I/O isolated
- **Binary file format** - Efficient .stasset format, Unity-compatible
- **Material system** - 21 materials defined with proper IDs

#### Performance
- **60 FPS** with 32,768 voxels (32³ grid)
- **Fast I/O** - Load/save in <100ms
- **Efficient rendering** - Scatter plot with proper culling

#### Features
- **3D Viewport** - Orbit camera, pan, zoom, grid, axis compass
- **Basic Tools** - Paint (brush size 1-10), Erase
- **Hover Highlighting** - Real-time visual feedback
- **Procedural Generation** - 15+ generators (buildings, shapes, tilesets)
- **Re-Generate** - Random seed variation

### Weaknesses ❌

#### Critical Gaps
1. **No Undo/Redo** - Every action is permanent
2. **No Selection Tools** - Can't select/copy/paste regions
3. **No Transform Tools** - Can't move/rotate/mirror
4. **No Fill Tool** - Manual painting for enclosed areas

#### Major Limitations
5. **No Layer System** - Can't organize complex models
6. **Primitive Viewport** - No ortho views, slicing, or filtering
7. **Weak Procedural Integration** - Can't edit generated assets incrementally
8. **No Measurement Tools** - Guesswork for precise modeling

#### Code Quality Issues
- **No state management** - Direct voxel array manipulation
- **Tight coupling** - Viewport modifies data directly
- **No command pattern** - Can't implement undo without refactor
- **Limited error handling** - File I/O could fail gracefully
- **No unit tests** - Risky for refactoring



---

## 📊 Feature Gap Analysis

### Feature Completeness Matrix

| Category | Current | Target | Gap | Priority |
|----------|---------|--------|-----|----------|
| **File I/O** | 80% | 95% | Batch export, LOD auto-save, compression | Low |
| **Editing Tools** | 30% | 95% | Fill, select, copy/paste, transform, mirror | **CRITICAL** |
| **Viewport** | 50% | 85% | Ortho views, slicing, filters, wireframe | High |
| **Undo/Redo** | 0% | 100% | Full command history with memory management | **CRITICAL** |
| **Layers** | 0% | 90% | Layer stack, visibility, locking, merging | Medium |
| **Procedural** | 60% | 80% | Parameter preview, modifiers, noise | Medium |
| **Measurement** | 0% | 70% | Ruler, dimensions, grid snapping | Low |
| **UI/UX** | 40% | 85% | Shortcuts, presets, recent files, drag-drop | Medium |

### Impact vs Effort Analysis

```
High Impact, Low Effort (DO FIRST):
├─ Undo/Redo System ⭐⭐⭐ (4-6 hours)
├─ Fill Tool ⭐⭐⭐ (2-3 hours)
└─ Box Select ⭐⭐⭐ (3-4 hours)

High Impact, Medium Effort:
├─ Copy/Paste/Transform (6-8 hours)
├─ Orthographic Views (4-6 hours)
└─ Material Filter (2-3 hours)

High Impact, High Effort:
├─ Layer System (2-3 days)
├─ Advanced Selection (magic wand, grow/shrink) (1-2 days)
└─ Cross-section Slicing (1-2 days)

Medium Impact, Low Effort:
├─ Recent Files Menu (1 hour)
├─ Keyboard Shortcuts (2 hours)
└─ Auto-save (2-3 hours)
```

---

## 🏗️ Architecture Improvements

### Current Architecture (v1.2.0)

```
┌─────────────────────────────────────────────┐
│           VoxelEditor (QMainWindow)         │
│  ┌─────────────────────────────────────┐   │
│  │  Direct voxel array manipulation    │   │
│  │  No state management                │   │
│  │  Tight coupling                     │   │
│  └─────────────────────────────────────┘   │
│                    │                        │
│         ┌──────────┼──────────┐            │
│         ▼          ▼          ▼            │
│   ┌─────────┐ ┌──────────┐ ┌──────────┐   │
│   │ToolPanel│ │ Viewport │ │ Brushes  │   │
│   └─────────┘ └──────────┘ └──────────┘   │
│                    │                        │
│                    ▼                        │
│            numpy.ndarray (voxels)           │
└─────────────────────────────────────────────┘
```

**Problems:**
- Viewport directly modifies voxel array (no undo)
- No abstraction layer (can't track changes)
- State scattered across multiple widgets

### Target Architecture (v2.0.0)

```
┌─────────────────────────────────────────────────────────┐
│              VoxelEditor (QMainWindow)                  │
│  ┌─────────────────────────────────────────────────┐   │
│  │         Command Pattern + Event System          │   │
│  │         Centralized State Management            │   │
│  │         Loose Coupling via Signals              │   │
│  └─────────────────────────────────────────────────┘   │
│                         │                               │
│         ┌───────────────┼───────────────┐              │
│         ▼               ▼               ▼              │
│   ┌──────────┐   ┌──────────┐   ┌──────────┐          │
│   │ToolPanel │   │ Viewport │   │LayerPanel│          │
│   └──────────┘   └──────────┘   └──────────┘          │
│         │               │               │              │
│         └───────────────┼───────────────┘              │
│                         ▼                               │
│              ┌─────────────────────┐                   │
│              │   VoxelDocument     │                   │
│              │  ┌───────────────┐  │                   │
│              │  │ CommandStack  │  │ (Undo/Redo)       │
│              │  ├───────────────┤  │                   │
│              │  │ LayerManager  │  │ (Layers)          │
│              │  ├───────────────┤  │                   │
│              │  │ SelectionMgr  │  │ (Selection)       │
│              │  ├───────────────┤  │                   │
│              │  │ VoxelModel    │  │ (Data)            │
│              │  └───────────────┘  │                   │
│              └─────────────────────┘                   │
└─────────────────────────────────────────────────────────┘
```

**Benefits:**
- All edits go through commands (undo/redo)
- Centralized state (easier to debug)
- Loose coupling (easier to extend)
- Event-driven (reactive UI updates)

### New Core Classes

#### 1. VoxelDocument
```python
class VoxelDocument:
    """Central data model for voxel editing"""
    def __init__(self):
        self.voxel_model = VoxelModel()
        self.command_stack = CommandStack()
        self.layer_manager = LayerManager()
        self.selection_manager = SelectionManager()
        
        # Signals
        self.voxels_changed = pyqtSignal(list)  # Changed coords
        self.selection_changed = pyqtSignal()
        self.layer_changed = pyqtSignal(int)
```

#### 2. Command Pattern
```python
class Command(ABC):
    """Base class for all editable actions"""
    @abstractmethod
    def execute(self): pass
    
    @abstractmethod
    def undo(self): pass
    
    @abstractmethod
    def redo(self): pass

class PaintCommand(Command):
    """Paint voxels with material"""
    def __init__(self, coords, material, prev_materials):
        self.coords = coords
        self.material = material
        self.prev_materials = prev_materials
    
    def execute(self):
        for (x,y,z), mat in zip(self.coords, self.material):
            voxels[x,y,z] = mat
    
    def undo(self):
        for (x,y,z), mat in zip(self.coords, self.prev_materials):
            voxels[x,y,z] = mat
    
    def redo(self):
        self.execute()
```

#### 3. CommandStack
```python
class CommandStack:
    """Manages undo/redo history"""
    def __init__(self, max_size=100):
        self.undo_stack = []
        self.redo_stack = []
        self.max_size = max_size
    
    def execute(self, command):
        command.execute()
        self.undo_stack.append(command)
        self.redo_stack.clear()
        
        # Memory management
        if len(self.undo_stack) > self.max_size:
            self.undo_stack.pop(0)
    
    def undo(self):
        if self.undo_stack:
            cmd = self.undo_stack.pop()
            cmd.undo()
            self.redo_stack.append(cmd)
    
    def redo(self):
        if self.redo_stack:
            cmd = self.redo_stack.pop()
            cmd.redo()
            self.undo_stack.append(cmd)
```

---

## 🚀 Phase 1: Core Workflow (Week 1-2)

**Goal:** Transform editing experience with professional workflow tools

### 1.1 Undo/Redo System ⭐ CRITICAL

**Priority:** P0 (Blocking for all other features)  
**Effort:** 4-6 hours  
**Impact:** Transforms user confidence and creative freedom

#### Requirements
- [x] Command pattern for all edits
- [x] Undo stack (last 100 actions)
- [x] Redo stack (cleared on new action)
- [x] Keyboard shortcuts (Ctrl+Z, Ctrl+Shift+Z)
- [x] Memory-efficient (store diffs, not full copies)
- [x] History panel (optional, shows last 10 actions)

#### Implementation Details

**Command Types:**
```python
PaintCommand      # Paint single/multiple voxels
EraseCommand      # Erase voxels (set to air)
FillCommand       # Flood fill region
PasteCommand      # Paste copied region
TransformCommand  # Move/rotate/mirror selection
```

**Memory Optimization:**
```python
# BAD: Store entire voxel array (32³ × 2 bytes = 64 KB per action!)
class PaintCommand:
    def __init__(self):
        self.voxels_before = voxels.copy()  # ❌ 64 KB
        self.voxels_after = voxels.copy()   # ❌ 64 KB

# GOOD: Store only changed voxels
class PaintCommand:
    def __init__(self, coords, materials_before, materials_after):
        self.coords = coords              # ✅ 12 bytes per voxel
        self.before = materials_before    # ✅ 2 bytes per voxel
        self.after = materials_after      # ✅ 2 bytes per voxel
    # For 10 voxels: 160 bytes vs 128 KB (800x smaller!)
```

**UI Integration:**
- Edit menu: Undo, Redo
- Toolbar: Undo/Redo buttons with icons
- Status bar: "Undo: Paint 5 voxels" tooltip
- History panel (optional): List of last 10 actions

#### Testing Checklist
- [ ] Undo single paint stroke
- [ ] Undo large brush (100+ voxels)
- [ ] Redo after undo
- [ ] Undo/redo multiple times
- [ ] Redo stack clears on new action
- [ ] Memory usage stays under 10 MB for 100 actions
- [ ] Keyboard shortcuts work
- [ ] Undo disabled when stack empty

---

### 1.2 Selection Tools ⭐ HIGH PRIORITY

**Priority:** P0 (Enables copy/paste workflow)  
**Effort:** 6-8 hours  
**Impact:** Unlocks region-based editing

#### Requirements
- [x] Box selection (drag to select rectangular region)
- [x] Magic wand (select connected voxels of same material)
- [x] Invert selection
- [x] Grow/shrink selection
- [x] Clear selection (Esc key)
- [x] Visual feedback (highlight selected voxels)

#### Implementation Details

**Selection Data Structure:**
```python
class SelectionManager:
    def __init__(self):
        self.selected_coords = set()  # Set of (x,y,z) tuples
        self.selection_mode = 'replace'  # replace, add, subtract
    
    def select_box(self, start, end):
        """Select rectangular region"""
        x1, y1, z1 = start
        x2, y2, z2 = end
        
        coords = set()
        for x in range(min(x1,x2), max(x1,x2)+1):
            for y in range(min(y1,y2), max(y1,y2)+1):
                for z in range(min(z1,z2), max(z1,z2)+1):
                    if self._in_bounds(x,y,z):
                        coords.add((x,y,z))
        
        self._apply_selection(coords)
    
    def select_magic_wand(self, seed_coord, material):
        """Flood fill selection (BFS)"""
        coords = set()
        queue = [seed_coord]
        visited = set()
        
        while queue:
            coord = queue.pop(0)
            if coord in visited:
                continue
            
            x, y, z = coord
            if not self._in_bounds(x,y,z):
                continue
            
            if voxels[x,y,z] != material:
                continue
            
            coords.add(coord)
            visited.add(coord)
            
            # Add neighbors
            for dx, dy, dz in [(1,0,0), (-1,0,0), (0,1,0), 
                               (0,-1,0), (0,0,1), (0,0,-1)]:
                neighbor = (x+dx, y+dy, z+dz)
                queue.append(neighbor)
        
        self._apply_selection(coords)
```

**Visual Feedback:**
```python
# Render selected voxels with highlight color
def render_selection(self):
    if not self.selected_coords:
        return
    
    # Create scatter plot for selected voxels
    coords = np.array(list(self.selected_coords))
    colors = np.ones((len(coords), 4)) * [1.0, 1.0, 0.0, 0.5]  # Yellow, 50% opacity
    
    self.selection_plot = GLScatterPlotItem(
        pos=coords,
        color=colors,
        size=self.voxel_size * 1.1,  # Slightly larger than voxels
        pxMode=False
    )
    self.addItem(self.selection_plot)
```

**Keyboard Shortcuts:**
- **B** - Box select tool
- **W** - Magic wand tool
- **Ctrl+A** - Select all
- **Ctrl+D** - Deselect all
- **Ctrl+Shift+I** - Invert selection
- **Esc** - Clear selection

#### Testing Checklist
- [ ] Box select with mouse drag
- [ ] Magic wand selects connected voxels
- [ ] Selection highlights visible
- [ ] Invert selection works
- [ ] Grow/shrink selection
- [ ] Selection persists across tool changes
- [ ] Clear selection on Esc
- [ ] Selection mode (replace/add/subtract) works

---

### 1.3 Copy/Paste/Transform ⭐ HIGH PRIORITY

**Priority:** P0 (Core editing workflow)  
**Effort:** 6-8 hours  
**Impact:** Enables efficient repetitive work

#### Requirements
- [x] Copy selection to clipboard
- [x] Cut selection (copy + delete)
- [x] Paste at cursor position
- [x] Move selection (drag with mouse)
- [x] Rotate selection (90° increments)
- [x] Mirror selection (X/Y/Z axes)
- [x] Keyboard shortcuts (Ctrl+C, Ctrl+X, Ctrl+V)

#### Implementation Details

**Clipboard Data Structure:**
```python
class Clipboard:
    def __init__(self):
        self.voxels = None  # 3D numpy array
        self.origin = None  # (x,y,z) relative position
    
    def copy(self, selection_coords, voxel_array):
        """Copy selected voxels to clipboard"""
        if not selection_coords:
            return
        
        # Find bounding box
        coords = np.array(list(selection_coords))
        min_coord = coords.min(axis=0)
        max_coord = coords.max(axis=0)
        size = max_coord - min_coord + 1
        
        # Create clipboard array
        self.voxels = np.zeros(size, dtype=np.uint16)
        self.origin = min_coord
        
        # Copy voxels
        for x, y, z in selection_coords:
            rel_x = x - min_coord[0]
            rel_y = y - min_coord[1]
            rel_z = z - min_coord[2]
            self.voxels[rel_x, rel_y, rel_z] = voxel_array[x, y, z]
    
    def paste(self, target_pos, voxel_array):
        """Paste clipboard at target position"""
        if self.voxels is None:
            return []
        
        changed_coords = []
        for x in range(self.voxels.shape[0]):
            for y in range(self.voxels.shape[1]):
                for z in range(self.voxels.shape[2]):
                    if self.voxels[x,y,z] == 0:  # Skip air
                        continue
                    
                    target_x = target_pos[0] + x
                    target_y = target_pos[1] + y
                    target_z = target_pos[2] + z
                    
                    if self._in_bounds(target_x, target_y, target_z):
                        voxel_array[target_x, target_y, target_z] = self.voxels[x,y,z]
                        changed_coords.append((target_x, target_y, target_z))
        
        return changed_coords
```

**Transform Operations:**
```python
def rotate_90(voxels, axis='y'):
    """Rotate voxel array 90° around axis"""
    if axis == 'y':
        return np.rot90(voxels, k=1, axes=(0, 2))
    elif axis == 'x':
        return np.rot90(voxels, k=1, axes=(1, 2))
    elif axis == 'z':
        return np.rot90(voxels, k=1, axes=(0, 1))

def mirror(voxels, axis='x'):
    """Mirror voxel array across axis"""
    if axis == 'x':
        return np.flip(voxels, axis=0)
    elif axis == 'y':
        return np.flip(voxels, axis=1)
    elif axis == 'z':
        return np.flip(voxels, axis=2)
```

#### Testing Checklist
- [ ] Copy selection (Ctrl+C)
- [ ] Cut selection (Ctrl+X)
- [ ] Paste at cursor (Ctrl+V)
- [ ] Paste multiple times
- [ ] Rotate 90° (all axes)
- [ ] Mirror (all axes)
- [ ] Move selection with mouse
- [ ] Undo/redo works for all operations

---

### 1.4 Fill Tool ⭐ HIGH PRIORITY

**Priority:** P1 (Common use case)  
**Effort:** 2-3 hours  
**Impact:** Speeds up enclosed area painting

#### Requirements
- [x] Flood fill from clicked voxel
- [x] Fill only connected voxels of same material
- [x] Respect grid boundaries
- [x] Add to undo stack
- [x] Visual feedback during fill
- [x] Keyboard shortcut (F key)

#### Implementation Details

**Flood Fill Algorithm (BFS):**
```python
def flood_fill(start_coord, target_material, replacement_material):
    """Fill connected region with new material"""
    x, y, z = start_coord
    
    if voxels[x,y,z] != target_material:
        return []  # Nothing to fill
    
    if target_material == replacement_material:
        return []  # No change needed
    
    filled_coords = []
    queue = [start_coord]
    visited = set()
    
    while queue:
        coord = queue.pop(0)
        
        if coord in visited:
            continue
        
        x, y, z = coord
        
        if not in_bounds(x, y, z):
            continue
        
        if voxels[x,y,z] != target_material:
            continue
        
        # Fill this voxel
        voxels[x,y,z] = replacement_material
        filled_coords.append(coord)
        visited.add(coord)
        
        # Add 6-connected neighbors
        for dx, dy, dz in [(1,0,0), (-1,0,0), (0,1,0), 
                           (0,-1,0), (0,0,1), (0,0,-1)]:
            neighbor = (x+dx, y+dy, z+dz)
            queue.append(neighbor)
    
    return filled_coords
```

**Performance Optimization:**
```python
# For large fills (>1000 voxels), show progress
def flood_fill_with_progress(start_coord, target, replacement):
    filled = []
    queue = [start_coord]
    visited = set()
    
    progress_dialog = QProgressDialog("Filling...", "Cancel", 0, 0)
    progress_dialog.setWindowModality(Qt.WindowModal)
    
    batch_size = 100
    while queue:
        # Process in batches
        for _ in range(min(batch_size, len(queue))):
            if not queue:
                break
            
            coord = queue.pop(0)
            # ... fill logic ...
            filled.append(coord)
        
        # Update progress
        progress_dialog.setValue(len(filled))
        QApplication.processEvents()
        
        if progress_dialog.wasCanceled():
            break
    
    progress_dialog.close()
    return filled
```

#### Testing Checklist
- [ ] Fill small region (<100 voxels)
- [ ] Fill large region (>1000 voxels)
- [ ] Fill respects boundaries
- [ ] Fill only affects connected voxels
- [ ] Undo fill operation
- [ ] Cancel large fill operation
- [ ] Fill tool keyboard shortcut works

---

## 🎨 Phase 2: Advanced Editing (Week 3)

**Goal:** Speed up repetitive tasks with advanced tools

### 2.1 Brush Improvements

**Priority:** P2  
**Effort:** 4-6 hours  
**Impact:** More natural painting experience

#### Requirements
- [x] Sphere brush (smooth edges)
- [x] Cylinder brush (for pillars/walls)
- [x] Custom brush shapes (save/load)
- [x] Brush preview (ghost voxels)
- [x] Brush intensity (partial opacity)

#### Implementation
```python
class BrushShape(Enum):
    CUBE = "cube"
    SPHERE = "sphere"
    CYLINDER = "cylinder"
    CUSTOM = "custom"

def generate_sphere_brush(radius):
    """Generate sphere brush pattern"""
    size = radius * 2 + 1
    brush = np.zeros((size, size, size), dtype=bool)
    center = radius
    
    for x in range(size):
        for y in range(size):
            for z in range(size):
                dx = x - center
                dy = y - center
                dz = z - center
                dist = np.sqrt(dx*dx + dy*dy + dz*dz)
                
                if dist <= radius:
                    brush[x,y,z] = True
    
    return brush
```

---

### 2.2 Viewport Enhancements

**Priority:** P2  
**Effort:** 6-8 hours  
**Impact:** Better visibility of complex models

#### Requirements
- [x] Orthographic camera modes (Top/Front/Side/Perspective)
- [x] Cross-section slicing (hide voxels above/below plane)
- [x] Material filter (show only specific materials)
- [x] Wireframe toggle
- [x] Grid snapping

#### Orthographic Views
```python
class CameraMode(Enum):
    PERSPECTIVE = "perspective"
    TOP = "top"
    FRONT = "front"
    SIDE = "side"

def set_camera_mode(mode):
    if mode == CameraMode.TOP:
        self.setCameraPosition(distance=60, elevation=90, azimuth=0)
        self.opts['fov'] = 1  # Orthographic
    elif mode == CameraMode.FRONT:
        self.setCameraPosition(distance=60, elevation=0, azimuth=0)
        self.opts['fov'] = 1
    elif mode == CameraMode.SIDE:
        self.setCameraPosition(distance=60, elevation=0, azimuth=90)
        self.opts['fov'] = 1
    else:  # PERSPECTIVE
        self.setCameraPosition(distance=60, elevation=30, azimuth=45)
        self.opts['fov'] = 60
```

---

## 🗂️ Phase 3: Organization (Week 4)

**Goal:** Manage complex models with layers and organization

### 3.1 Layer System

**Priority:** P2  
**Effort:** 2-3 days  
**Impact:** Essential for complex models

#### Requirements
- [x] Create/delete/rename layers
- [x] Show/hide layers
- [x] Lock layers (prevent editing)
- [x] Merge layers
- [x] Layer opacity
- [x] Active layer indicator

#### Data Structure
```python
class Layer:
    def __init__(self, name="Layer"):
        self.name = name
        self.visible = True
        self.locked = False
        self.opacity = 1.0
        self.voxels = np.zeros((32,32,32), dtype=np.uint16)

class LayerManager:
    def __init__(self):
        self.layers = [Layer("Background")]
        self.active_layer_index = 0
    
    def add_layer(self, name):
        layer = Layer(name)
        self.layers.append(layer)
        return layer
    
    def merge_layers(self, layer1_idx, layer2_idx):
        """Merge two layers"""
        layer1 = self.layers[layer1_idx]
        layer2 = self.layers[layer2_idx]
        
        # Composite: layer2 over layer1
        mask = layer2.voxels != 0
        layer1.voxels[mask] = layer2.voxels[mask]
        
        # Remove layer2
        self.layers.pop(layer2_idx)
    
    def composite(self):
        """Composite all visible layers into single array"""
        result = np.zeros_like(self.layers[0].voxels)
        
        for layer in self.layers:
            if not layer.visible:
                continue
            
            mask = layer.voxels != 0
            result[mask] = layer.voxels[mask]
        
        return result
```

---

## ✨ Phase 4: Polish (Week 5-6)

**Goal:** Professional feel and quality of life improvements

### 4.1 UI Improvements

- [ ] Keyboard shortcut customization
- [ ] Tool presets (save brush settings)
- [ ] Recent files menu
- [ ] Drag-and-drop file loading
- [ ] Toolbar customization
- [ ] Dark/light theme toggle

### 4.2 Quality of Life

- [ ] Auto-save every N minutes
- [ ] Crash recovery (save temp file)
- [ ] Export to .obj format
- [ ] Export to .vox format (MagicaVoxel)
- [ ] Batch processing scripts
- [ ] Asset library browser

---

## 📐 Technical Specifications

### Performance Targets

| Metric | Current | Target | Notes |
|--------|---------|--------|-------|
| **FPS** | 60 | 60 | Maintain with all features |
| **Undo Memory** | N/A | <10 MB | For 100 actions |
| **Load Time** | <100ms | <100ms | 32³ grid |
| **Save Time** | <100ms | <100ms | With compression |
| **Fill Operation** | N/A | <500ms | For 1000 voxels |

### Memory Budget

```
Base Application:     ~50 MB
Voxel Data (32³):     ~64 KB
Undo Stack (100):     ~10 MB
Layer System (5):     ~320 KB
Selection Buffer:     ~1 MB
Total Target:         <100 MB
```

### File Format Extensions

**Current .stasset:**
```
Header (16 bytes) + Voxel Data (width×height×depth×2)
```

**Future .stasset v2 (with layers):**
```
Header (16 bytes)
├─ Magic: "STAS"
├─ Version: 2
├─ Flags: 0x01 (compressed), 0x02 (has layers)
└─ Dimensions: width, height, depth

Layer Count (2 bytes)

For each layer:
├─ Layer Header (32 bytes)
│  ├─ Name (24 bytes, UTF-8)
│  ├─ Flags (1 byte): visible, locked
│  ├─ Opacity (1 byte): 0-255
│  └─ Reserved (6 bytes)
└─ Voxel Data (compressed with zlib)
```

---

## 📅 Implementation Timeline

### Week 1: Foundation
- **Day 1-2:** Refactor architecture (VoxelDocument, Command pattern)
- **Day 3:** Implement undo/redo system
- **Day 4:** Add box selection tool
- **Day 5:** Add fill tool

### Week 2: Core Workflow
- **Day 1-2:** Implement copy/paste/transform
- **Day 3:** Add magic wand selection
- **Day 4-5:** Testing and bug fixes

### Week 3: Advanced Editing
- **Day 1-2:** Brush improvements (sphere, cylinder)
- **Day 3:** Orthographic views
- **Day 4:** Cross-section slicing
- **Day 5:** Material filter

### Week 4: Organization
- **Day 1-3:** Layer system implementation
- **Day 4:** Reference images
- **Day 5:** Testing and polish

### Week 5-6: Polish
- **Day 1-2:** UI improvements
- **Day 3-4:** Quality of life features
- **Day 5-10:** Testing, documentation, bug fixes

---

## ⚠️ Risk Assessment

### High Risk Items

**1. Performance Degradation**
- **Risk:** Advanced features slow down rendering
- **Mitigation:** Profile early, optimize hot paths, use numpy vectorization
- **Fallback:** Disable features for large grids (>64³)

**2. Memory Leaks**
- **Risk:** Undo stack grows unbounded
- **Mitigation:** Limit stack size, compress old commands
- **Fallback:** Manual memory management, clear old history

**3. File Format Compatibility**
- **Risk:** v2 .stasset breaks Unity loader
- **Mitigation:** Maintain v1 export option, version detection
- **Fallback:** Always save v1 alongside v2

### Medium Risk Items

**4. UI Complexity**
- **Risk:** Too many features overwhelm users
- **Mitigation:** Progressive disclosure, tooltips, tutorials
- **Fallback:** Simple/advanced mode toggle

**5. Cross-Platform Issues**
- **Risk:** PyQt6 behaves differently on Windows/Mac/Linux
- **Mitigation:** Test on all platforms, use Qt abstractions
- **Fallback:** Platform-specific workarounds

---

## 🎯 Success Metrics

### Quantitative
- [ ] 60 FPS maintained with all features enabled
- [ ] <100 MB memory usage with 5 layers
- [ ] <500ms for common operations (fill, paste)
- [ ] 100% undo/redo coverage for all edits
- [ ] <10% file size increase with compression

### Qualitative
- [ ] User can create complex model in <30 minutes
- [ ] Undo/redo feels instant and reliable
- [ ] Selection tools feel intuitive
- [ ] Layer system is easy to understand
- [ ] No crashes during normal workflow

---

## 📚 Appendix

### A. Command Reference

| Command | Shortcut | Description |
|---------|----------|-------------|
| Undo | Ctrl+Z | Undo last action |
| Redo | Ctrl+Shift+Z | Redo undone action |
| Copy | Ctrl+C | Copy selection |
| Cut | Ctrl+X | Cut selection |
| Paste | Ctrl+V | Paste clipboard |
| Select All | Ctrl+A | Select all voxels |
| Deselect | Ctrl+D | Clear selection |
| Invert Selection | Ctrl+Shift+I | Invert selection |
| Fill | F | Flood fill tool |
| Box Select | B | Box selection tool |
| Magic Wand | W | Magic wand tool |

### B. File Format Specification

See `docs/FILE_FORMAT_V2.md` for complete specification.

### C. Architecture Diagrams

See `docs/ARCHITECTURE_V2.md` for detailed class diagrams.

---

**Document Version:** 1.0  
**Last Updated:** June 29, 2026  
**Status:** 📋 Ready for Review

