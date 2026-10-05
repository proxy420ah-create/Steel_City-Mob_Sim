# Raymarch Traversal Optimization — Steel City: Mob Sim

**Created**: October 8, 2026
**Status**: 🔒 ACTIVE — Phase 1 shipped, Phase 2–3 deferred

---

## Purpose

Single home for the voxel raymarcher's *traversal efficiency* work: how the
DDA decides which cells to walk, what the step budget must cover, and the
known optimizations still on the table. Companion to
`GPU_DRIVEN_SECTOR_RENDERING.md` (draw-call/buffer strategy) — this doc is
about *per-pixel march cost inside a volume*, not how volumes get to screen.

Postmortem for the motivating bug lives at
`docs/known_issues/rendering/ANGLE_DEPENDENT_BUILDING_CULLING.md`.

---

## Core Mental Model

The raymarcher is a **DDA** (Amanatides & Woo): each loop iteration jumps to
the **next voxel-plane crossing**, i.e. one cell entered — it is *not* a
distance march. Two consequences that drive every decision here:

1. **Iteration count = cells crossed**, bounded by `sx + sy + sz` planes on a
   `sx × sy × sz` region (Manhattan bound) — NOT the Euclidean diagonal
   `√(sx²+sy²+sz²)`. A near-axis-parallel ray crosses ~`sx+sz+ε` cells.
   Under-budget → the loop exits mid-volume → content starves → **holes**.
2. **Every optimization is "spend fewer iterations proving empty space is
   empty."** The hierarchy of techniques below is ordered by how much air
   each one removes from the marched region.

---

## Phase 1 — ✅ SHIPPED (Oct 8, 2026)

### Tight-bounds march + self-computed step floor

**What**: the DDA used to `RayAABB`+march the **full container**
(`volOffset .. volOffset + dims·voxelSize`). Now the CPU sends
`_TightBoundsMin/Max` (voxel units, Max exclusive) per chunk and the shader
marches only the content AABB. Voxel indexing still uses container
`dims`/`volOffset` — stride unchanged.

**Step budget**: `stepCap = max(_MaxSteps, marchSX + marchSY + marchSZ + 4)`
computed **in-shader from the marched bounds** — authoritative for every
path (chunks, instanced, sectors) without per-path wiring. CPU floors
`_MaxSteps` the same way for the chunk path.

**Measured effect** (Unity Profiler, user-verified): the angle-dependent
culling is gone AND frame cost dropped — flat buildings save ~70% of
per-pixel traversal (range: ~120-layer container column → ~37-layer tight).

**Files**: `VoxelChunkManager.cs` (prop block), `VoxelProxyRaymarch.shader`
(uniforms, volumeMin/Max, `stepCap`).

**Remaining air still marched**: everything *inside* the tight box — e.g.
the range's y=2–36 band above the floor slab, the roof-gap beside a water
tower, courtyards.

---

## Phase 2 — ⏸ DEFERRED: Column-Occupancy Skip

**Goal**: skip interior air too — answer "is there content in my path?"
per-column instead of per-cell.

### Design sketch (from Oct 8 discussion)

Precompute during the existing tight-bounds CPU pass — it already walks
every voxel, so it's ~free there:

- **Per-(x,z) column interval map**: for each column store
  `minY`, `maxY` of occupied voxels. Buffer: `sx×sz` entries →
  192×192×4B ≈ **150KB** per full-block building (vs 8.8MB voxel buffer —
  trivial).
- **Shader integration**: inside the DDA loop, when the ray enters a new
  column, look up `[minY..maxY]`; if the ray's y-range over that column
  doesn't intersect it, advance `currentT` to the next column boundary
  (existing `tMax` machinery) — one lookup replaces ~34 cell-steps for the
  range's interior band.

```
Now:   enter column → step, empty → step, empty → …×37 → exit column
Skip:  enter column → lookup: content y≤1, ray at y≈5 → jump column
```

### Caveats / open questions

- **Disjoint spans**: min/max can't express floor-slab + floating platform
  (reads as one span, internal gap still marched — still *correct*, just
  less optimal). Full fix = per-column occupancy bitmask (2×uint64 for 120
  layers) or short interval lists. Start with min/max; vertical gaps are
  rare in the building vocabulary.
- **Axis choice**: the sketch is y-columns. A horizontal-air-heavy scene
  might want xz-column choice per-ray or a 3D macrocell grid instead
  (8³ macrocells w/ occupancy bit = coarse mip — more general, more work).
- **Cost model**: trade a `ComputeBuffer` + lookup for iterations. Only
  pays off on sparse-in-box volumes; empty_land-like solids gain nothing.
  Profile against `stepsAccum` / `[Perf]` line before/after.

---

## Phase 3 — ⏸ DEFERRED: Sector Path Adoption

Sector-baked buildings (`useSectorBaking=false` today, so dormant) draw via
`DrawMeshInstanced` with `instMeta = (bufferOffset, dimsX, dimsY, dimsZ)` —
**full container dims**, no tight bounds, and `sectorMaterial` gets the flat
`maxSteps=264`. If sectors are ever re-enabled:

- **Starvation risk is already fixed**: the in-shader `stepCap` floors at
  the container-cell bound for those draws automatically. (That's why the
  floor lives in the shader.)
- **Perf opportunity**: per-instance tight bounds would need a second
  instanced buffer (instMeta float4 is full: offset+dims). `SectorBaker`
  already computes per-building data at bake time — computing tight AABBs
  there is easy; the plumbing is a `_BuildingTightBounds` float4 buffer
  indexed by `instanceID`, mirrored on `buildingMetaBuffer`.
- Watch the merged-sector case: a sector containing both a pancake and a
  tower has a large union box; per-instance tight bounds fix the march,
  not the proxy rasterization (proxy already uses per-instance dims +
  padding in `BuildSectorMatrices`).

---

## Anti-Patterns (learned the hard way)

| Trap | Why it bites |
|------|--------------|
| Budget sized in distance/diagonal | DDA counts *plane crossings*; a grazing ray crosses far more cells than its length suggests. Worst case is `sx+sy+sz`. |
| Fixed step budget across volume sizes | A constant can't cover both a 64³ lot and a 192×120×192 block. Derive it from the marched region. |
| "Fail fast" caps as perf | A too-small cap doesn't save work — it *discards correct pixels*. The fast path is smaller marched regions, not smaller budgets. |

---

## Related Files

- `Assets/Scripts/UI/VoxelChunkManager.cs` — chunk prop block, LOD tiers,
  `stepCap` floor, sector draw path
- `Assets/Resources/Shaders/VoxelProxyRaymarch.shader` — `RayAABB`, DDA
  loop, `stepCap`, `_TightBoundsMin/Max`
- `Assets/Scripts/UI/SectorBaker.cs` — `buildingMeta` (per-instance dims),
  future tight-bounds buffer site
- `docs/known_issues/rendering/ANGLE_DEPENDENT_BUILDING_CULLING.md` —
  the motivating bug, full postmortem
- `docs/systems/INVARIANT_COMPUTATION_PRINCIPLE.md` — same philosophy:
  compute once (column intervals at load) vs repeat per-pixel
