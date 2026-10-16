# Voxel Coordinate Spaces — Steel City: Mob Sim

**Created**: March 2026
**Status**: 🔒 ACTIVE — normative reference for all voxel-space code

---

## Why This Document Exists

The eyes-centroid anchor bug (March 2026) consumed an entire debugging session because three *correct* implementations were written in **three different coordinate spaces**, and every fix verified the math within its own space rather than checking which space the renderer consumed. The final bug was a single `+0.5` — invisible in any single file, fatal across the boundary.

**Rule 0: Any code that produces a voxel-space position MUST declare which of the spaces below it is in — in the comment, at the point of computation.** If you cannot name the space, you have not finished the code.

---

## The Canonical Space: **Index Space**

> **"Voxel index = its rendered center."**

A voxel stored at integer index `i` renders as a cube **centered at `i`** (relative to `dims/2` centering). Index-space positions are continuous (`float`/`Vector3`-valued); integer values land on voxel centers, half-integers land on the seams between voxels.

This is the **interchange space** between the JSON on disk, the animator, and Unity. All derived positions — painted pivot centroids, region centroids, attachment points, eye anchors — are expressed here.

### Where each consumer puts the offset

| Consumer | Conversion | Convention |
|---|---|---|
| **Animator render** (`character_animator.html`) | `worldX = (indexX - dimsX/2) * voxelSize` | Offset baked at render; voxel `i` centered at `i - dims/2` |
| **Unity cell conversion** (`VoxelCharacter.PosePoint`, `ApplyPivotOverrides`) | `localCell = (posed + dims/2 + 0.5f) * voxelSize` | `+0.5` applied at cell conversion; voxel `i` lands centered on render cell `i - dims/2` |
| **JSON `pivots`** | `p.x * dimsX` (normalized → index space) | Stored normalized 0..1, unnormalized to index space at load |
| **Painted centroids** (editor pivot painting, attachment points) | Mean / bbox-midpoint of raw integer indices | Already index space — no conversion |
| **Region centroids** (`TryGetRegionCentroid`, `regionCentroids`) | `(min + max) / 2` per axis | Bbox midpoint in index space — see below |

Both render paths agree: **voxel index `i` has its rendered cell centered at `i - dims/2`**. The `+0.5` exists in exactly one place per pipeline (Unity's `localCell`, the animator's instanced-mesh positions) — it must never appear in computed index-space quantities.

---

## The Trap: Cell-Center Space

> **"Voxel `i` occupies cell `[i, i+1)`, center at `i + 0.5`."**

This is a *different* convention — common in tile-map/grid literature — where voxel centers sit at half-integers. **Nothing in this codebase uses it as a storage convention.** The eyes bug came from computing the region centroid with `+0.5` cell-center semantics, then feeding it to a renderer expecting index space → systematic +0.5 voxel offset on every axis.

Symptoms of a cell-center leak:

- Anchor/marker sits **centered on a voxel when the painted area's true center is a seam** (even-width spans)
- Diagnostics report `.0` where `.5` is expected, or the legend itself is inverted
- The offset is *exactly half a voxel* — small enough to look "almost right," large enough to be visibly wrong

### Index-space parity cheat-sheet

In index space, a painted span `[min, max]` (inclusive) has bbox midpoint `(min + max) / 2`:

| Span width | Midpoint parity | Lands on |
|---|---|---|
| **Even** (e.g. eyes `x44–51` → `47.5`) | `.5` | **Seam** between the two middle voxels |
| **Odd** (e.g. `x44–50` → `47.0`) | `.0` | Center of the middle voxel |

A diagnostic reading `.0` on an *even* span — or `.5` on an *odd* span — is a convention bug, not a paint problem. The animator's Vision status line prints `span x{min}–{max} …` with the legend `.5 = seam · .0 = voxel` for exactly this check.

---

## Centroid Semantics: Bbox Midpoint, Not Mass Mean

Region/painted-area centroids use the **bounding-box midpoint** `(min + max) / 2` per axis, *not* the voxel-count mean:

- A mass mean drifts toward whichever side carries more paint — an asymmetric eyes band anchors off the face midline.
- The bbox midpoint is the geometric center of the painted **area** — immune to paint-density asymmetry, lands on the seam/middle voxel deterministically.

Applies identically in `CharacterAsset.TryGetRegionCentroid` (Unity) and `rebuildRegionIndex` (animator). Both store span bounds alongside the centroid for diagnostics.

---

## Rules for New Code

1. **Declare the space** — every voxel-space variable/computation names its space in a comment (`// index space`), especially at file/space boundaries.
2. **Index space is canonical** — anything stored in JSON (`pivots`, `regions`, `attachmentPoints`) or passed between animator ↔ Unity is index space. Convert at the *render boundary*, not earlier.
3. **Centroids are bbox midpoints** — never voxel-count means for painted-area anchoring.
4. **Verify against the renderer, not the formula** — a correct formula in the wrong space produces a confidently wrong result. When adding a derived position, trace it to the actual `* voxelSize` conversion and confirm the convention matches.
5. **Diagnostics must share the consumer's space** — a status line that prints "seam vs voxel" parity in a different space than the renderer confirms a wrong answer. Print span bounds so parity can be checked *in the space the renderer uses*.
6. **Half-voxel offsets are the signature** — any "almost centered but on a voxel" report is a space mismatch until proven otherwise. Check this first, before paint-symmetry theories.

---

## Worked Example — The Eyes-Centroid Bug (March 2026)

**Report**: eye-anchor orb sits centered on a single voxel; painted Eyes span `x44–51` is 8 wide and should anchor on the seam.

**Chain**: `regions` (JSON `"x,y,z"` keys, integer indices) → animator `rebuildRegionIndex` → centroid → FK chain → `(pos - dims/2) * voxelSize` → instanced marker.

**Bug**: centroid computed `(min + max + 1) / 2 = 48.0` (cell-center space) but the render treats `48.0` as index-space → lands at `48 - 48 = 0` = center of the index-48 voxel. The true band seam is index-space `47.5` → renders at `-0.5`, between the index-47/48 voxels.

**Fix**: `(min + max) / 2 = 47.5` — index space, matching the renderer. One character deleted; the formula was never wrong — the space was.

**Diagnostic failure that hid it**: the status line printed `.0 = seam` in the wrong space, confirming the wrong answer. Corrected to `.5 = seam · .0 = voxel` with span bounds printed.

---

## Checklist — New Region-Driven Feature (glasses anchor, wound points, hat seat…)

- [ ] Source data is integer voxel indices (`regions` `"x,y,z"` → id) — index space on entry
- [ ] Derived position computed in index space (`(min+max)/2` or index-mean of *indices*)
- [ ] No `+0.5` anywhere in the index-space math — it belongs only at the render-cell conversion
- [ ] Unity path goes through `PosePoint`/`TryGetEyeAnchor` (shared `PoseToWorld` core) — the same FK the voxels ride
- [ ] Animator path goes through `regionCentroids` + `computeGroupRotation` — same chain as pivot gizmos
- [ ] Status/debug output prints span bounds + parity legend in index space
- [ ] Test asset: paint an **even-width** span — marker must land on a seam; paint **odd-width** — must land on the middle voxel

---

## Related Docs

- `docs/systems/VOXEL_ENGINE_GOTCHAS.md` — Gotcha #8 (this bug, narrative form)
- `docs/systems/VOXEL_GROUP_ANIMATION.md` — the FK chain (`computeGroupRotation` JS / `PosePoint` C#) both anchors ride
- `docs/systems/CHARACTER_ASSET_LIFECYCLE.md` — consolidated `.json` format (`regions`, `regionDefs`, `pivots`)
- `Assets/docs/COORDINATE_SYSTEM.md` — city-level positioning (MapRoot, corner-vs-center)
