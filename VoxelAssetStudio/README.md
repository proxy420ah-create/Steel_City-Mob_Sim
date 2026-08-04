# Voxel Asset Studio

**Standalone voxel editor for Steel Tide: First Device**

Built with PyQt6 and OpenGL - leveraging the same tech stack as the Kalshi Dashboard!

---

## 🚀 Quick Start

### 1. Install Dependencies

```bash
cd VoxelAssetStudio
pip install -r requirements.txt
```

### 2. Run the Application

**Option A: Double-click the launcher**
```
VoxelStudio.bat (in parent folder)
```

**Option B: Run directly**
```bash
python main.py
```

---

## 🎮 Controls

### Mouse Controls
- **Left-click:** Paint/interact with voxels
- **Middle-click:** Pan camera (move up/down/left/right)
- **Right-click:** Orbit camera (rotate around model)
- **Mouse wheel:** Zoom in/out

### Tools
- **Paint:** Left-click voxel to change material
- **Erase:** Left-click voxel to set to Air

### Keyboard Shortcuts
- **Ctrl+O:** Open file
- **Ctrl+S:** Save file
- **Ctrl+Shift+S:** Save as
- **Ctrl+Q:** Quit

---

## 📂 File Format

**.stasset files** (Steel Tide Asset format)

```
Header (16 bytes):
├─ Magic: "STAS"
├─ Version: 1
├─ Dimensions: width, height, depth (uint16)
└─ Reserved: 4 bytes

Voxel Data:
└─ width × height × depth × 2 bytes (uint16, little-endian)
```

**Compatible with Unity's StAssetReader/StAssetWriter!**

---

## 🎨 Materials

| ID | Name | Color | Usage |
|----|------|-------|-------|
| 0 | Air | Transparent | Empty space |
| 2 | Chobham Armor | Brown | Highest durability |
| 3 | Concrete | Gray | Structural |
| 5 | Steel | Dark gray | High durability |
| 13 | Damaged Concrete | Red | First hit state |
| 14 | Damaged Steel | Orange | First hit state |

---

## 🛠️ Project Structure

```
VoxelAssetStudio/
├─ main.py                  # Entry point
├─ voxel_editor.py          # Main window
├─ viewport_widget.py       # 3D OpenGL view
├─ tool_panel.py            # Left sidebar tools
├─ material_library.py      # Material definitions
├─ stasset_io.py            # Import/Export .stasset
├─ requirements.txt         # Dependencies
└─ README.md                # This file
```

---

## 🔄 Workflow

```
1. Double-click VoxelStudio.bat
   ↓
2. App opens, loads HighDensity32.stasset
   ↓
3. Paint/modify voxels
   ↓
4. File → Save (Ctrl+S)
   ↓
5. Switch to Unity (already open!)
   ↓
6. Unity auto-reloads the asset
   ↓
7. Press Play → See your changes!
```

**Fast iteration!** No Unity recompilation needed!

---

## 📋 Phase 1 Features (Complete!)

- ✅ Load/save .stasset files
- ✅ 3D viewport with orbit camera
- ✅ Render voxels as scatter plot
- ✅ **Voxel clicking with raycasting** ⭐ NEW!
- ✅ **Paint tool (click to change material)** ⭐ NEW!
- ✅ **Erase tool (click to set to Air)** ⭐ NEW!
- ✅ Material selector
- ✅ Configurable mouse controls
- ✅ Status bar (grid size, voxel count, FPS)
- ✅ Dark theme (like Kalshi Dashboard!)

---

## 🚀 Future Features

### Phase 2: Enhanced Tools
- [ ] Brush sizes (1×1×1, 3×3×3, 5×5×5)
- [ ] Fill tool (flood fill)
- [ ] Copy/paste regions
- [ ] Undo/redo stack

### Phase 3: Procedural Generation
- [ ] Generate → Building menu
- [ ] Parameter sliders
- [ ] Preview before applying

---

## 🐛 Known Issues

- **Raycasting accuracy** - Simplified ray calculation, may need improvement for precision
- **Re-renders entire grid on paint** - Optimize to update only changed voxels
- **No undo/redo** - Coming in Phase 2

---

## 💡 Tips

- Start with small edits to test the workflow
- Save often! (Ctrl+S)
- Use the status bar to monitor FPS
- Reset camera (Home) if you get lost

---

**Version:** 1.0 (Phase 1)  
**Last Updated:** June 27, 2026
