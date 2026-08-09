# Voxel Asset Studio - UI Improvements

**Date:** June 27, 2026  
**Version:** 1.2.0

---

## ✨ **What Changed**

### **Removed:**
- ❌ **"Hollow Shell" shape** - Deprecated (was misleading, not actually hollow)
- ❌ **Mouse Controls panel** - Moved to Settings dialog
- ❌ **Keyboard shortcuts panel** - Moved to Settings dialog

### **Added:**
- ✅ **Brush Settings Panel** - Replaces controls panels in left sidebar
- ✅ **Brush Size Slider** - Adjust brush from 1-10 voxels
- ✅ **Settings Dialog** - Centralized settings (Options → Settings)
- ✅ **Highlight Hover Setting** - Toggle voxel highlighting under cursor

---

## 🎨 **New UI Layout**

### **Left Sidebar:**
```
┌─────────────────────┐
│  🛠️ Tools           │
│  • Paint            │
│  • Erase            │
│                     │
│  Material           │
│  • Concrete (3)     │
│  └─────────────────┘│
│                     │
│  🖌️ Brush Settings  │
│  Size: 1 voxel      │
│  [━━━━━━━━━━━]      │
│  1 ←────────→ 10    │
│                     │
└─────────────────────┘
```

### **Settings Dialog (Options → Settings):**
```
┌─────────────────────────────┐
│  Settings                   │
├─────────────────────────────┤
│  [Viewport] [Controls]      │
│                             │
│  Viewport Tab:              │
│  ┌─ Voxel Highlighting ───┐│
│  │ ☑ Highlight voxel under││
│  │   mouse cursor          ││
│  │ Shows which voxel you're││
│  │ about to edit           ││
│  └─────────────────────────┘│
│                             │
│  Controls Tab:              │
│  ┌─ 🖱️ Mouse Controls ────┐│
│  │ Left-Click: Paint       ││
│  │ Middle-Click: Pan       ││
│  │ Right-Click: Orbit      ││
│  │ Wheel: Zoom             ││
│  └─────────────────────────┘│
│  ┌─ ⌨️ Keyboard Shortcuts ─┐│
│  │ WASD: Pan Camera        ││
│  │ Q/E: Pan Up/Down        ││
│  │ Home: Reset Camera      ││
│  │ Ctrl+S: Save            ││
│  └─────────────────────────┘│
│                             │
│         [OK] [Cancel]       │
└─────────────────────────────┘
```

---

## 🎮 **How to Use**

### **Brush Size:**
1. Look at left sidebar → "🖌️ Brush Settings"
2. Drag slider to adjust size (1-10 voxels)
3. Label updates: "Size: X voxel(s)"
4. Status bar shows confirmation

### **Voxel Highlighting:**
1. Menu → Options → Settings
2. Viewport tab
3. Check/uncheck "Highlight voxel under mouse cursor"
4. Click OK
5. Hover over voxels to see highlight (when enabled)

### **View Controls Reference:**
1. Menu → Options → Settings
2. Controls tab
3. See all mouse and keyboard controls

---

## 🔧 **Technical Details**

### **New Files:**
- `brush_panel.py` - Brush settings UI panel
- `settings_dialog.py` - Settings dialog with tabs
- `UI_IMPROVEMENTS.md` - This document

### **Modified Files:**
- `voxel_editor.py` - Replaced ControlsPanel with BrushPanel, added Settings dialog
- Removed imports: `ControlsPanel`, `MouseConfigDialog`
- Added imports: `BrushPanel`, `SettingsDialog`

### **Removed Files:**
- None (kept for backward compatibility)

### **Settings Storage:**
```python
self.settings = {
    'highlight_hover': True,  # Show voxel under cursor
    'brush_size': 1           # Current brush size (1-10)
}
```

---

## 🚀 **Future Enhancements**

### **Planned Features:**
- **Brush Shape** - Circle, square, sphere brush patterns
- **Brush Falloff** - Soft edges on large brushes
- **Color Picker** - Visual material selection
- **Undo/Redo** - Full edit history
- **Layers** - Multiple voxel layers
- **Symmetry Mode** - Mirror edits across axes

### **Settings to Add:**
- Grid visibility toggle
- Wireframe mode
- Background color picker
- Auto-save interval
- Performance settings (LOD, render quality)

---

## 📊 **Benefits**

### **Cleaner UI:**
- ✅ More space for viewport
- ✅ Less visual clutter
- ✅ Easier to find tools
- ✅ Centralized settings

### **Better Workflow:**
- ✅ Quick brush size adjustment
- ✅ Easy to enable/disable hover highlight
- ✅ Controls reference always available
- ✅ Settings persist across sessions (future)

### **Scalability:**
- ✅ Easy to add new settings
- ✅ Tabbed dialog supports many options
- ✅ Brush panel can grow with more tools
- ✅ Clean separation of concerns

---

## 🐛 **Known Issues**

- **Hover highlighting not yet implemented in viewport** (placeholder in settings)
- **Brush size not yet applied to painting** (TODO in code)
- **Settings don't persist** (need to add save/load)

These will be addressed in future updates!

---

## 📝 **Changelog**

### Version 1.2.0 (June 27, 2026)
- Removed deprecated "Hollow Shell" shape
- Removed Mouse Controls and Keyboard panels from sidebar
- Added Brush Settings panel with size slider
- Added Settings dialog with Viewport and Controls tabs
- Added "Highlight Hover" setting (UI only, implementation pending)
- Improved UI layout and space utilization

### Version 1.1.0 (June 27, 2026)
- Added Truly Hollow Sphere and Cube shapes
- Created SHAPE_REFERENCE.md documentation

### Version 1.0.0 (June 25, 2026)
- Initial release
