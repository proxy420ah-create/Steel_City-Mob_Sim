# Phase 2: Core Workflow Features - Implementation Plan

**Date:** June 29, 2026  
**Status:** 🚀 Ready to Implement  
**Estimated Time:** 12-16 hours (Week 1)

---

## 🎯 Objectives

Transform Voxel Asset Studio from basic editor to professional workflow tool by adding:
1. **Undo/Redo System** - Command pattern with 50-command history
2. **Fill Tool** - 3D flood fill for efficient painting
3. **Box Select** - Rectangular selection in 3D space
4. **Copy/Paste** - Clipboard operations for voxel regions

---

## 📋 Implementation Checklist

### ✅ Completed
- [x] Command system architecture (`command_system.py`)
- [x] Fill tool algorithm (`fill_tool.py`)

### 🚧 In Progress
- [ ] Integrate command system into voxel_editor.py
- [ ] Add undo/redo UI (Edit menu + keyboard shortcuts)
- [ ] Implement fill tool in viewport
- [ ] Create selection system
- [ ] Add copy/paste functionality

---

## 🔧 Feature 1: Undo/Redo System

### Architecture
```
CommandHistory
├─ PaintVoxelCommand
├─ EraseVoxelCommand
├─ FillCommand
├─ ReplaceVoxelsCommand (procedural generation)
└─ Future: SelectCommand, TransformCommand, etc.
```

### Integration Steps

#### Step 1: Add CommandHistory to VoxelEditor
```python
# In voxel_editor.py __init__
from command_system import CommandHistory, PaintVoxelCommand, EraseVoxelCommand

self.command_history = CommandHistory(max_history=50)
```

#### Step 2: Modify Paint/Erase to Use Commands
```python
# Replace direct voxel modification with commands
def on_voxel_clicked(self, x, y, z):
    if self.tool_panel.current_tool == 'paint':
        # Get brush positions
        positions = self.get_brush_positions(x, y, z)
        material = self.tool_panel.current_material
        
        # Create and execute command
        cmd = PaintVoxelCommand(self.voxels, positions, material)
        if self.command_history.execute(cmd):
            self.viewport.update_voxels(self.voxels)
            self.mark_modified()
```

#### Step 3: Add Undo/Redo Menu Items
```python
# In init_menu()
edit_menu = menubar.addMenu("Edit")

undo_action = QAction("Undo", self)
undo_action.setShortcut("Ctrl+Z")
undo_action.triggered.connect(self.undo)
edit_menu.addAction(undo_action)

redo_action = QAction("Redo", self)
redo_action.setShortcut("Ctrl+Shift+Z")
redo_action.triggered.connect(self.redo)
edit_menu.addAction(redo_action)
```

#### Step 4: Implement Undo/Redo Methods
```python
def undo(self):
    if self.command_history.undo():
        self.viewport.update_voxels(self.voxels)
        desc = self.command_history.get_undo_description()
        self.statusBar().showMessage(f"Undone: {desc}", 2000)

def redo(self):
    if self.command_history.redo():
        self.viewport.update_voxels(self.voxels)
        desc = self.command_history.get_redo_description()
        self.statusBar().showMessage(f"Redone: {desc}", 2000)
```

---

## 🔧 Feature 2: Fill Tool

### UI Integration

#### Step 1: Add Fill Tool to ToolPanel
```python
# In tool_panel.py
fill_btn = QPushButton("🪣 Fill")
fill_btn.setCheckable(True)
fill_btn.clicked.connect(lambda: self.set_tool('fill'))
tool_layout.addWidget(fill_btn)
```

#### Step 2: Handle Fill Click in Viewport
```python
# In voxel_editor.py
from fill_tool import flood_fill_3d
from command_system import FillCommand

def on_voxel_clicked(self, x, y, z):
    if self.tool_panel.current_tool == 'fill':
        # Calculate filled positions
        filled_positions = flood_fill_3d(
            self.voxels, 
            (x, y, z), 
            self.tool_panel.current_material,
            max_fill=10000
        )
        
        if filled_positions:
            # Create fill command
            cmd = FillCommand(
                self.voxels,
                (x, y, z),
                self.tool_panel.current_material,
                filled_positions
            )
            
            if self.command_history.execute(cmd):
                self.viewport.update_voxels(self.voxels)
                self.statusBar().showMessage(f"Filled {len(filled_positions)} voxels", 2000)
```

### Safety Features
- **Max Fill Limit:** 10,000 voxels (prevent accidental full-grid fill)
- **Progress Indicator:** Show "Filling..." for large operations
- **Confirmation:** Ask user if fill > 5,000 voxels

---

## 🔧 Feature 3: Box Select

### Architecture
```python
class SelectionBox:
    def __init__(self):
        self.start_pos = None  # (x, y, z)
        self.end_pos = None    # (x, y, z)
        self.active = False
    
    def get_bounds(self):
        """Returns (min_x, max_x, min_y, max_y, min_z, max_z)"""
        if not self.active or not self.start_pos or not self.end_pos:
            return None
        
        x1, y1, z1 = self.start_pos
        x2, y2, z2 = self.end_pos
        
        return (
            min(x1, x2), max(x1, x2),
            min(y1, y2), max(y1, y2),
            min(z1, z2), max(z1, z2)
        )
    
    def get_voxels(self):
        """Returns list of (x, y, z) positions in selection"""
        bounds = self.get_bounds()
        if not bounds:
            return []
        
        min_x, max_x, min_y, max_y, min_z, max_z = bounds
        voxels = []
        
        for x in range(min_x, max_x + 1):
            for y in range(min_y, max_y + 1):
                for z in range(min_z, max_z + 1):
                    voxels.append((x, y, z))
        
        return voxels
```

### UI Integration

#### Step 1: Add Select Tool
```python
# In tool_panel.py
select_btn = QPushButton("📦 Select")
select_btn.setCheckable(True)
select_btn.clicked.connect(lambda: self.set_tool('select'))
tool_layout.addWidget(select_btn)
```

#### Step 2: Handle Selection Drag
```python
# In voxel_editor.py
self.selection_box = SelectionBox()

def on_voxel_clicked(self, x, y, z):
    if self.tool_panel.current_tool == 'select':
        if not self.selection_box.active:
            # Start selection
            self.selection_box.start_pos = (x, y, z)
            self.selection_box.end_pos = (x, y, z)
            self.selection_box.active = True
        else:
            # Finish selection
            self.selection_box.end_pos = (x, y, z)
            self.viewport.render_selection_box(self.selection_box)
```

#### Step 3: Render Selection Box
```python
# In viewport_widget.py
def render_selection_box(self, selection_box):
    """Draw wireframe box around selection"""
    bounds = selection_box.get_bounds()
    if not bounds:
        return
    
    min_x, max_x, min_y, max_y, min_z, max_z = bounds
    
    # Create wireframe box using GLLinePlotItem
    # (Implementation details...)
```

---

## 🔧 Feature 4: Copy/Paste

### Architecture
```python
class Clipboard:
    def __init__(self):
        self.voxels = None  # 3D numpy array
        self.origin = None  # (x, y, z) where copied from
        self.size = None    # (width, height, depth)
    
    def copy(self, voxel_data, selection_box):
        """Copy voxels from selection"""
        bounds = selection_box.get_bounds()
        min_x, max_x, min_y, max_y, min_z, max_z = bounds
        
        width = max_x - min_x + 1
        height = max_y - min_y + 1
        depth = max_z - min_z + 1
        
        self.voxels = np.zeros((width, height, depth), dtype=np.uint16)
        self.origin = (min_x, min_y, min_z)
        self.size = (width, height, depth)
        
        # Copy voxel data
        for x in range(width):
            for y in range(height):
                for z in range(depth):
                    self.voxels[x, y, z] = voxel_data[min_x + x, min_y + y, min_z + z]
    
    def paste(self, voxel_data, paste_pos):
        """Paste voxels at position"""
        if self.voxels is None:
            return []
        
        px, py, pz = paste_pos
        width, height, depth = self.size
        
        modified_positions = []
        
        for x in range(width):
            for y in range(height):
                for z in range(depth):
                    target_x = px + x
                    target_y = py + y
                    target_z = pz + z
                    
                    # Bounds check
                    if (0 <= target_x < voxel_data.shape[0] and
                        0 <= target_y < voxel_data.shape[1] and
                        0 <= target_z < voxel_data.shape[2]):
                        
                        voxel_data[target_x, target_y, target_z] = self.voxels[x, y, z]
                        modified_positions.append((target_x, target_y, target_z))
        
        return modified_positions
```

### UI Integration
```python
# In voxel_editor.py
self.clipboard = Clipboard()

# Menu items
copy_action = QAction("Copy", self)
copy_action.setShortcut("Ctrl+C")
copy_action.triggered.connect(self.copy_selection)
edit_menu.addAction(copy_action)

paste_action = QAction("Paste", self)
paste_action.setShortcut("Ctrl+V")
paste_action.triggered.connect(self.paste_clipboard)
edit_menu.addAction(paste_action)

def copy_selection(self):
    if self.selection_box.active:
        self.clipboard.copy(self.voxels, self.selection_box)
        count = len(self.selection_box.get_voxels())
        self.statusBar().showMessage(f"Copied {count} voxels", 2000)

def paste_clipboard(self):
    if self.clipboard.voxels is not None:
        # Paste at current cursor position or selection origin
        # (Implementation details...)
```

---

## 📊 Testing Plan

### Undo/Redo Tests
- [ ] Paint single voxel → Undo → Redo
- [ ] Paint 100 voxels → Undo → Redo
- [ ] Undo 50 times (history limit)
- [ ] Procedural generation → Undo (entire grid)
- [ ] Memory usage stays under 100MB

### Fill Tool Tests
- [ ] Fill small enclosed region (10 voxels)
- [ ] Fill large region (1000 voxels)
- [ ] Fill entire grid (should hit 10k limit)
- [ ] Fill → Undo → Redo
- [ ] Fill with different materials

### Selection Tests
- [ ] Select 1×1×1 box
- [ ] Select 10×10×10 box
- [ ] Select across grid boundaries (clamp)
- [ ] Deselect (click outside)
- [ ] Selection renders correctly

### Copy/Paste Tests
- [ ] Copy 5×5×5 region → Paste elsewhere
- [ ] Copy → Paste → Undo
- [ ] Paste at grid edge (clamp)
- [ ] Copy air voxels (should work)

---

## 🎯 Success Criteria

- ✅ Undo/redo works for all edit operations
- ✅ Fill tool completes in <1 second for 1000 voxels
- ✅ Selection box renders clearly (wireframe)
- ✅ Copy/paste preserves exact voxel data
- ✅ Keyboard shortcuts work (Ctrl+Z, Ctrl+C, Ctrl+V)
- ✅ Status bar shows operation feedback
- ✅ No crashes or memory leaks

---

## 📁 Files to Modify

### New Files (Already Created)
- ✅ `command_system.py` - Undo/redo infrastructure
- ✅ `fill_tool.py` - Flood fill algorithms

### Files to Modify
- [ ] `voxel_editor.py` - Integrate command system, add menu items
- [ ] `tool_panel.py` - Add fill and select tools
- [ ] `viewport_widget.py` - Render selection box, handle fill
- [ ] `brush_panel.py` - (No changes needed)

### New Files to Create
- [ ] `selection_system.py` - SelectionBox and Clipboard classes
- [ ] `PHASE2_TESTING_RESULTS.md` - Test results documentation

---

## 🚀 Next Steps

1. **Review this plan** - Make sure approach is sound
2. **Start with Undo/Redo** - Highest impact, foundational
3. **Add Fill Tool** - Quick win, very useful
4. **Implement Selection** - Enables copy/paste
5. **Add Copy/Paste** - Completes core workflow

**Estimated Time:** 12-16 hours total
- Undo/Redo: 4-6 hours
- Fill Tool: 2-3 hours
- Selection: 3-4 hours
- Copy/Paste: 3-4 hours

---

**Ready to begin implementation!** 🎉
