# Model Transform Tool ✅

**Date:** June 29, 2026  
**Status:** 🎉 Complete  
**Feature:** Reposition loaded models to compare with reference models

---

## 🎯 What It Does

The **Model Transform Tool** allows you to move your loaded voxel model anywhere in the viewport, making it easy to:
- **Compare sizes** with reference models side-by-side
- **Snap to reference positions** with one click
- **Manually adjust** position with precise voxel-level control
- **Reset to origin** instantly

---

## 📋 How to Use

### UI Location
The Transform panel appears in the **right sidebar**, below the Reference Models panel.

### Manual Positioning
```
📐 Model Transform
├─ Position Offset (voxels)
│  ├─ X: [spinbox] ← Horizontal (Red axis)
│  ├─ Y: [spinbox] ← Vertical (Blue axis)
│  └─ Z: [spinbox] ← Depth (Green axis)
```

- Adjust X, Y, Z spinboxes to move model
- Step size: 8 voxels (snaps to grid)
- Range: -256 to +256 voxels

### Quick Snap Buttons
```
Quick Snap to Reference
├─ 🏀 Basketball
├─ 🗑️ Trash Can
├─ 🧍 Human
├─ 🚮 Dumpster
├─ 💡 Street Light
└─ 🌳 Tree
```

Click any button to instantly position your model next to that reference object!

### Reset Button
```
🔄 Reset to Origin
```
Returns model to (0, 0, 0) position.

---

## 🎮 Workflow Example

### Scenario: Check if your model is human-sized

1. **Load your model** (File → Open or Generate menu)
2. **Click "🧍 Human"** in Quick Snap section
3. **Model moves** to X=40, Z=-10 (next to human reference)
4. **Compare visually** - Is your model taller/shorter than the human?
5. **Adjust if needed** using X/Y/Z spinboxes

### Scenario: Line up multiple comparisons

1. Load model
2. Click "🏀 Basketball" → Compare with small object
3. Click "🗑️ Trash Can" → Compare with medium object
4. Click "🚮 Dumpster" → Compare with large object
5. Manually adjust Z to align perfectly

---

## 🔧 Technical Details

### Coordinate System
- **X** (Red axis): Left/Right
- **Y** (Blue axis): Up/Down
- **Z** (Green axis): Forward/Back

### Snap Logic
When you click a snap button:
1. Finds reference model position
2. Places loaded model at **X=+40** (opposite side from references at X=-20)
3. Matches **Z coordinate** of reference (same depth)
4. Sets **Y=0** (ground level)

**Result:** Your model appears on the right side of the viewport, aligned with the selected reference model's depth.

### Model Offset
- Stored as `(x, y, z)` tuple in voxels
- Applied during rendering: `world_pos = voxel_pos + offset`
- Does NOT modify actual voxel data
- Purely visual repositioning

---

## 📁 Files Created/Modified

### New Files
1. **`transform_panel.py`** - Transform UI panel
   - Offset spinboxes
   - Snap buttons
   - Reset button
   - Signal connections

### Modified Files
1. **`viewport_widget.py`**
   - Added `model_offset` field
   - Added `set_model_offset()` method
   - Added `snap_to_reference()` method
   - Applied offset in `render_voxels()`

2. **`voxel_editor.py`**
   - Imported `TransformPanel`
   - Added transform panel to right sidebar
   - Connected signals
   - Added `on_snap_to_reference()` handler

---

## ✅ Features

- [x] Manual X/Y/Z offset controls
- [x] 8-voxel grid snapping
- [x] Quick snap to any reference model
- [x] Reset to origin button
- [x] Real-time visual updates
- [x] Console logging for debugging
- [x] Status bar feedback
- [x] Tooltip help text

---

## 🎨 UI Design

```
┌─────────────────────────────────────┐
│  📐 Model Transform                 │
├─────────────────────────────────────┤
│  Move loaded model to compare with  │
│  reference models                   │
├─────────────────────────────────────┤
│  Position Offset (voxels)           │
│  ┌───────────────────────────────┐  │
│  │ X: [    0    ] ← Red axis     │  │
│  │ Y: [    0    ] ← Blue axis    │  │
│  │ Z: [    0    ] ← Green axis   │  │
│  └───────────────────────────────┘  │
│                                     │
│  Quick Snap to Reference            │
│  ┌───────────────────────────────┐  │
│  │ [🏀 Basketball]               │  │
│  │ [🗑️ Trash Can]                 │  │
│  │ [🧍 Human]                     │  │
│  │ [🚮 Dumpster]                  │  │
│  │ [💡 Street Light]              │  │
│  │ [🌳 Tree]                      │  │
│  └───────────────────────────────┘  │
│                                     │
│  [🔄 Reset to Origin]               │
└─────────────────────────────────────┘
```

---

## 🎯 Use Cases

### 1. Size Validation
**Problem:** "Is my character model the right size?"

**Solution:**
1. Load character model
2. Click "🧍 Human" snap button
3. Compare heights visually
4. Adjust model scale in Voxel Studio if needed

---

### 2. Asset Consistency
**Problem:** "Are all my props scaled consistently?"

**Solution:**
1. Load prop #1, snap to Basketball
2. Note relative size
3. Load prop #2, snap to Basketball
4. Compare - should be similar ratio

---

### 3. World Building
**Problem:** "Will this building fit in my scene?"

**Solution:**
1. Load building model
2. Snap to Dumpster (2m reference)
3. Check if building is 3-4 dumpsters tall
4. Validates realistic building height

---

## 📊 Benefits

1. **Visual Comparison** - See sizes side-by-side
2. **Quick Workflow** - One-click snap positioning
3. **Precise Control** - Manual voxel-level adjustment
4. **Non-Destructive** - Doesn't modify actual model data
5. **Grid Aligned** - 8-voxel snapping keeps things tidy
6. **Reversible** - Reset to origin anytime

---

## 🐛 Known Limitations

1. **Offset not saved** - Resets when you load a new file
2. **Single model only** - Can't offset reference models
3. **No rotation** - Only translation (X/Y/Z movement)
4. **Fixed snap position** - Always X=+40 (could be customizable)

---

## 🔮 Future Enhancements

- [ ] Save offset with .stasset file
- [ ] Rotation controls (X/Y/Z rotation)
- [ ] Scale controls (uniform/non-uniform)
- [ ] Multiple snap positions (left/right/front/back)
- [ ] Custom snap targets (user-defined positions)
- [ ] Offset presets (save/load favorite positions)
- [ ] Gizmo handles for visual dragging
- [ ] Snap to grid overlay

---

## 💡 Tips

1. **Use keyboard pan** (WASD) to orbit around snapped models
2. **Lower keyboard sensitivity** for precise camera positioning
3. **Toggle reference visibility** to reduce clutter
4. **Reset often** - Easy to get lost, reset brings you back
5. **Snap to smallest first** - Basketball → Human → Tree progression

---

**Status:** ✅ Complete and Working  
**Next:** Test with real user workflows and gather feedback!
