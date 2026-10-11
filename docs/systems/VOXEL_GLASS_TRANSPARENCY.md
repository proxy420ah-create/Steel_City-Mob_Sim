# Voxel Glass, Transparency & Emissive Surfaces — Steel City: Mob Sim

**Created**: October 9, 2026
**Status**: 🔒 ACTIVE — shipped, tuning in progress
**Files**: `Assets/Resources/Shaders/VoxelProxyRaymarch.shader`, `Assets/Scripts/UI/VoxelChunkManager.cs` (`ApplyGlassSettings`)

---

## How Transparency Works — the Mental Model

Transparency is **not a "layer" property — it is a per-cell event**. When the DDA
enters a voxel whose palette color has `alpha < 0.99`, the shader does NOT stop —
it accumulates a tinted surface-blend and keeps marching:

```
glassAccum += throughput * alpha * glassColor * cheapLighting
throughput *= (1 - alpha)          // light fraction remaining
```

Loop exits when an opaque voxel is hit. Budget exhaustion
(`throughput <= 0.03` or `glassLayers == _GlassMaxLayers`) does NOT fall
through to wall shading — the ray terminates as a **frosted pane**:
`glassAccum + throughput * paneTint`, depth at the first glass face. A glass
voxel is therefore always a tint, a frosted pane, or nothing — never a brick.

Final composite on opaque hit:

```
hitColor = glassAccum + throughput * (opaqueColor * lighting)
depth    = ray at glassFirstT       // nearest glass face, not the wall behind
```

**Pane dedup**: `_PaneIDs` (per-voxel pane group buffer, flood-filled at load
over contiguous glass cells) charges each pane once per ray — a ray sliding
through many cells of the same pane pays one tint, not N. Ungrouped glass
(`paneId == 0`) keeps per-voxel accumulation.

**Consequence**: what a window *looks like* is governed by **how many glass
panes the ray crosses** — the material supplies per-surface alpha, pane groups
collapse intra-pane cells into one surface.

## The Car-vs-Tenement Discovery (why they looked different)

| Asset | Pane thickness | Voxel scale | Physical glass | Blend events |
|-------|---------------|-------------|----------------|--------------|
| Car windshield | 1 cell | 0.01 m | ~1 cm | 1 (2 on rakes) |
| Tenement window | **4 cells** | 0.1 m | ~40 cm | 4 |

Verified against `tenement_block_0.json`: every pane is a 4-voxel-deep run on
all faces (2,304 columns of mat-112 at depth 4 per axis pair + 1,792 storefront
mat-114 runs) — the window bore goes *past* the 2-voxel `WALL_T` shell. 25,600
glass voxels total.

Same `alpha = 0.6` → car transmits `(1−0.6) = 40%`, tenement `(1−0.6)⁴ ≈ 2.6%` —
essentially frosted-solid, which is exactly the "walled-off" look that prompted
the investigation. **To match a 1-cell pane at N-cell thickness:
`(1−a)^N = target transmission`** → for 40% through 4 cells, `a ≈ 0.21`.

## Grazing-Angle Banding (reduced by pane dedup)

A ray nearly parallel to a pane slides *along* it, crossing 5–15 cells. With
`_PaneIDs` active those cells belong to one pane → **one tint charge**, not N —
the worst intra-pane compounding is gone. What remains is *inter*-pane
accumulation (a row of distinct windows legitimately tints more edge-on).
**Rule stands but softer: tune alphas looking straight-on; edge-on stays
heavier, mostly from crossing many distinct panes.**

## Material IDs & the Emissive List

| ID | Material | Rendering |
|----|----------|-----------|
| 112 | Window Glass | transparent (palette alpha) |
| 113 | Lit Window | **deprecated** — palette aliases it to 112's glass color; lit/unlit is now a runtime lighting concern, not a material |
| 114 | Storefront Glass | transparent (palette alpha) |
| 115–117 | Neons | **emissive** |
| 124 | Lamp Glow | **emissive** |

`GetLighting` (shader line ~842) early-outs these IDs → full-brightness surfaces.
**Emissive casts no light** — a lit window glows but does not illuminate walls or
sidewalk. Evening-city "light pooling" needs a composited light pass (same
deferred-visual family as tracers).

**Role of 113 under the 1-cell convention**: the emissive material is *not*
obsolete — it moves from pane to contents. A transparent 112 pane to a dark
interior shows darkness; an emissive voxel (113/124) inside the hollow core is
what produces the lit-room look (glass blend → emissive interior hit). Lit-window
alternation therefore changes shape: toggle an *interior* cell ↔ emissive
rather than flipping the pane material. Pane-style 113 still earns its keep for
solid-interior buildings (nothing to see), far-LOD lit rectangles (cheap), and
flat neon-adjacent glows.

**Migration path to world lighting**: interior emissive cells are placeholders
for future light sources. The pane position, bore air, and hollow shell carry
over unchanged — when a composited light pass exists, the same cells host
emitter volumes and light spills through the window naturally (plus onto
walls/sidewalk). Author interiors as if the source is already there; only the
cell's role changes (emissive surface → emitter volume), never the geometry.

## Knobs & Where They Live

- **Alpha** — per-material palette color (`autoGlassColor`, `windowGlassColor`,
  `storefrontGlassColor` on the inspector component). Serialized → the inspector
  values **are the bake**; `ApplyGlassSettings()` re-pushes every play.
- **`_GlassSpecular`** — global specular lobe strength; high values bloom panes
  toward white (~0.3–0.6 reasonable; 1.42 bloomed).
- **`_GlassMaxLayers`** — global cap on blend events per ray; needs ≥4 for
  look-through-two-window scenes.
- Glass shading is deliberately cheap: ambient `dot(N, lightDir)·0.5+0.5` + spec —
  it skips the smooth-normal lighting path.
- **Shipping**: sync tuned values into `StAssetReader` defaults so authored
  palette = runtime palette.
- Instanced buildings get this for free — `sectorMaterial` is a clone of the
  proxy shader (`BUILDING_INSTANCING` keyword).

## Settled Convention: Windows Are 1 Voxel Thick

**Decision (Oct 9, 2026)**: all glass panes — windows, storefronts, windshields —
author at **1 cell thick**. Rationale:

- Every pane = exactly one blend event → **one shared alpha (~0.5–0.6) works for
  every asset**, no per-asset calibration against the `(1−a)^N` table.
- Matches the vehicle windshield behavior that already reads correctly.
- **Fill the remaining bore cells with AIR**, not wall — glass backed by opaque
  wall reads "curtained". On hollow interiors, recess the pane to the outermost
  cell → true see-through + the lit-room effect (emissive voxel visible inside)
  works immediately.
- Projectile semantics simplify: a pane is exactly one cell to penetrate/shatter.
- Through-two-window sightlines = 2 blends at `a=0.6` → 16% — still readable;
  keep `_GlassMaxLayers ≥ 4`.

## Authoring Guidance (VoxelAssetStudio)

- **Generators now emit the 1-cell convention** (baked 2026-10-09):
  `_add_windows_all_sides` (`procedural_mob_buildings.py`), `add_windows`
  (`gen_scaled_tenement.py`), and all direct glass writes place the pane at the
  **outermost cell** (`[..., 0]` / `[w-1]` / `[d-1]`) and AIR through the rest
  of the wall bore (`[1:wt]` etc.). Verified: every regenerated pane is
  depth-1 with air behind. The shipped `tenement_block_0` predates the bake
  (4-deep bores) and is being hand-repaired as the reference asset — regen
  output follows the new convention automatically.
- **Don't assume `WALL_T` bounds pane depth** — on the shipped tenement the
  window bore ran **4 deep** (verified in `tenement_block_0.json`). Root cause:
  the asset was generated with `:WALL_T` (2-deep glass) then **2×-upscaled** via
  `upscale_all_stassets.py` (`np.repeat` — wall 2→4v, panes 2→4v). Measure
  contiguous glass runs along the wall normal; generators now emit the
  convention natively, no upscale step.
- **True see-into-a-lit-room** needs a hollow interior — the tenement generator
  already air-fills cores. An emissive voxel (113/124) a few cells inside glows
  *through* the glass. Solid interior behind transparent glass reads as a
  "curtained window" — still fine, just no depth.
- **Random lit-window grids for free**: swap cells 112↔113 — zero new code.
- **Per-window opacity variants need new material IDs** (alpha is in the palette):
  a "Dim/Frosted Glass" line at `a ≈ 0.85` enables clear/dim/lit variety.
- Round-trip: editor JSON → `tools/json_to_stasset.py` → `voxel_buildings/*.stasset`.

## Windows ↔ Projectiles: the Design Boundary

**Visual transparency ≠ ballistic transparency.** The raymarch blends through
glass visually, but a gameplay ray treats the same voxel as **solid** — a bullet
stops on a window today. This is a *design decision to make*, not a bug:

| Option | Behavior | Cost |
|--------|----------|------|
| Glass blocks | Bullet stops at first glass cell | Free (current) |
| Penetrable | Ray continues with damage/accuracy falloff | Check mat ID during projectile DDA |
| **Shattering** | Projectile deletes/breaks the cell (112→0 or debris) | Needs runtime voxel edit — **now cheap**: tracked-write + partial `ComputeBuffer.SetData` (see `docs/core/PERFORMANCE_PATTERNS.md` #1–2) |

**→ Shattering is now specified** — seeded bullet-hole model: impact cell +
ring falloff removal, cracked-material halo, gid pane-unit cascade for blasts,
voxel→shard particle mapping, no physics. See
[`GLASS_SHATTER_MECHANIC.md`](GLASS_SHATTER_MECHANIC.md).

Why it matters for a mob game: drive-by shootings, sniper overwatch through
apartment windows, breaking storefronts during extortion ("smash-and-grab" as a
verb), and police shooting *through* windows during raids. Shattering also
produces the best feedback — the window you shot out stays shot out, and it's
one dirty-cell write away.

Open design question: does broken glass become a debris voxel (0→glass-shard
mat) or true air? Debris keeps collision realism (crawl-through opening); air is
simpler. *(Prototype answer in the shatter doc: start with AIR, revisit.)*

## What We Still Owe

- **Distance-based absorption** (`exp(−k·d)` transmission) — makes opacity
  proportional to path length in glass, immune to thickness differences.
  Elegant replacement for per-cell alpha if thickness keeps varying.
- **Dim/Frosted material** — one palette line for per-window variety.
- **Light spill pass** — emissive brightness is surface-only; real glow needs
  composited light volumes.
- **`_GlassMaxLayers` / spec / alpha defaults → `StAssetReader`** so shipped
  assets bake what the inspector shows.
