# Voxel Asset Studio - Changelog

## Version 1.1.0 - June 27, 2026

### ✨ New Features

**Truly Hollow Shapes Added:**
- ⭕ **Truly Hollow Sphere** - Empty interior, shell only
- ⭕ **Truly Hollow Cube** - Empty interior, shell only

These shapes have **empty air inside** (material 0), unlike the armored shapes which are densely packed.

### 🔧 Changes

**Menu Updates:**
- Added separator between shape categories
- Added "Truly hollow shapes" section with new shapes
- Marked "Hollow Shell" as DEPRECATED (it's not actually hollow!)

**Shape Clarifications:**
- "Hollow Shell" now shows warning: "NOT hollow - has concrete core"
- Armored shapes section labeled as "DENSE" to clarify they're fully packed
- New hollow shapes section labeled as "LIGHTWEIGHT"

### 📚 Documentation

**New Files:**
- `SHAPE_REFERENCE.md` - Complete guide to all shapes with visuals and gameplay implications

**Updated Files:**
- `shape_generator.py` - Added `generate_truly_hollow_sphere()` and `generate_truly_hollow_cube()`
- `voxel_editor.py` - Added menu items and handlers for new shapes

### 🎮 Gameplay Impact

**Hollow vs Armored:**
- **Armored shapes**: Dense, durable, two-stage destruction, bullets stop
- **Hollow shapes**: Lightweight, fragile, bullets pass through interior

**Voxel Count Comparison:**
- 32³ Armored Cube: ~32,768 voxels (fully packed)
- 32³ Hollow Cube (2-voxel shell): ~7,000 voxels (77% less!)

### 🐛 Bug Fixes

- Fixed misleading "Hollow Shell" name (it was never hollow, just two-layer)
- Clarified all shape descriptions in UI

---

## How to Use New Shapes

1. **Launch Voxel Studio:** Run `VoxelStudio.bat`
2. **Generate Menu:** Click "Generate" → Scroll to "Truly hollow shapes"
3. **Select Shape:** Choose "⭕ Truly Hollow Sphere" or "⭕ Truly Hollow Cube"
4. **Save:** File → Save As → `MyHollowShape.stasset`
5. **Test in Unity:** Copy to StreamingAssets, create VoxelObject, shoot through it!

---

## Previous Versions

### Version 1.0.0 - June 25, 2026
- Initial release
- Basic shapes (cube, sphere)
- Armored shapes (cube, sphere, cylinder)
- Two-layer shell (misleadingly named "hollow")
- .stasset file format
- PyQt6 UI with 3D viewport
