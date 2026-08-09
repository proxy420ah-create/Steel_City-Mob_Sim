# Voxel Ordering Fix — .stasset Byte Order

**Date**: August 2, 2026
**Status**: ✅ Fixed
**Severity**: Critical (characters rendered as unrecognizable blobs)

---

## Problem

Procedurally generated voxel characters loaded from `.stasset` files appeared as scattered blobs in Unity. Buildings rendered correctly despite the same bug.

## Root Cause

The Python writer (`stasset_io.py`) outputs voxel data in **X-major** (Fortran) order — `x` varies fastest, then `y`, then `z`:

```
byte[0] → voxel(0,0,0)
byte[1] → voxel(1,0,0)
byte[2] → voxel(2,0,0)
...
byte[w] → voxel(0,1,0)   // y increments after x wraps
```

The Steel City `StAssetReader.ParseVoxels()` used the wrong nested loop order:

```csharp
// WRONG — Z-major (z varies fastest)
for (int x = 0; x < width; x++)
    for (int y = 0; y < height; y++)
        for (int z = 0; z < depth; z++)
            voxels[x, y, z] = data[offset++];
```

This reads `z` fastest, `x` slowest — the opposite of what the file contains. Every voxel was placed at the wrong `(x, y, z)` position.

## Why Buildings Looked Fine

Buildings are mostly uniform material (e.g., 90% `RED_BRICK`). Scrambling voxel positions is invisible when neighboring voxels share the same material. Characters have 2-voxel features (sunglasses, tie, hat band) that get scattered across the grid → unrecognizable blob.

## Fix

Swap the loop order so `x` varies fastest:

```csharp
// CORRECT — X-major (x varies fastest, matches Python Fortran-order output)
for (int z = 0; z < depth; z++)
    for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            voxels[x, y, z] = data[offset++];
```

This matches:
- Python writer: `np.asarray(voxels, dtype='<u2', order='F').ravel(order='F')`
- Steel Tide's reader: flat array with `index = x + y*dimX + z*dimX*dimY`

## Verification

Diagnostic script confirmed the save/load roundtrip is byte-exact in Python. The issue was purely in the C# parsing loop order.

## Files Changed

- `Assets/Scripts/Sim/StAssetReader.cs` — swapped loop order in `ParseVoxels()`

## Lesson

When two systems share a binary format, verify that **both sides agree on element ordering**. A flat byte stream is ambiguous — the writer's memory layout (Fortran vs C order) determines the on-disk sequence, and the reader must match.

The Steel Tide project (`My project/Assets/Voxels/StAssetReader.cs`) reads correctly because it uses a flat array with explicit indexing (`x + y*dimX + z*dimX*dimY`). The Steel City project used nested loops with the wrong axis order.
