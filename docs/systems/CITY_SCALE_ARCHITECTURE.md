# City Scale Architecture — 32x32 Production Size

**Created**: 2026-10-05
**Status**: ✅ M0 COMPLETE (32x32 renders end-to-end) — M1 (profile) in progress
**Relates to**: `GPU_DRIVEN_SECTOR_RENDERING.md`, `GPU_DRIVEN_RENDERING_PLAN.md`, `INSTANCED_RENDERING_PITFALLS.md`, `RAYMARCH_TRAVERSAL_OPTIMIZATION.md`, `../core/CITY_LAYOUT_PIPELINE.md`, `../core/CITY_STATE_PRESERVATION.md`, `../known_issues/rendering/TERRAIN_SECTOR_OVERFLOW_AT_SCALE.md`
**Code**: `Assets/Scripts/UI/CityMap3D.cs`, `VoxelChunkManager.cs`, `SectorBaker.cs`, `VoxelTerrainBuilder.cs`, `Assets/Scripts/Sim/VoxelCollisionWorld.cs`, `Tools/generate_city_layout.py`

---

## Why This Document Exists

The playable city had been a **10x10 (100-block) test rig**. The production blueprint in `city_editor.html` is **32x32 (1,024 blocks)** — ~10x the buildings. Those two sizes were designed against older architecture, and the render path has since been optimized (packed voxel cache, sector baking, split terrain). This doc records:

1. What happened when we scaled up (findings + measurements).
2. The **hard limits** the sector pipeline must respect (so the next scale step doesn't fail silently).
3. A prioritized **optimization backlog** with the evidence for each item.
4. The milestone plan from here (river/bridge → zones → integration).

Everything labeled *measured* comes from `buildmap_log.txt` / `Editor.log` of real runs. Everything labeled *hypothesis* or *estimate* is from reading code and has **not** been confirmed with a profiler.

---

## TL;DR

| Question | Answer |
|---|---|
| Does Unity handle a 32x32 city? | **Yes, as of M0.** Loads in ~14 s, renders the whole map, no errors in `Editor.log`. Frame-rate not yet profiled (M1). |
| What broke? | Terrain: **half the roads/sidewalks silently vanished.** One terrain sector held all 1,024 chunks (143M voxels) and exceeded sector limits. Fixed by banding terrain into 9 sectors. |
| What is expensive now? | **Memory** (~1.83 GB of voxel data) and **load time** (~14 s, superlinear in terrain/collision). Not yet known: GPU frame cost. |
| What is the single biggest win available? | Stop storing duplicate data: per-lot unique debris clones (1.2 GB) and near-identical terrain tiles (573 MB). |
| What is the highest-value *safety* item? | A loud guard in `RegisterSector` — today a cap violation renders *nothing* with *no error*. |

---

## Scale Facts: 10x10 vs 32x32

| | 10x10 (test rig) | 32x32 (production blueprint) |
|---|---|---|
| Blocks | 100 | 1,024 |
| Building slots | 876 | 9,192 (9,189 `empty_land`, 2 tenement, 1 shooting range) |
| Block spacing | 13.2 m | 13.2 m |
| City extent | ~132 m | ~422 m (editor reports 420 m) |
| Terrain voxels | 13,939,200 | 143,210,888 |
| Building voxels (sector-baked) | — | 314,376,192 |
| Unique `.stasset` files | 3 | 3 (voxel cache: 3 misses, 18,384 hits) |

Block spacing = `groundTile (64*3*0.05 + 2*1.0 = 11.6 m) + road (1.6 m)` = 13.2 m. Terrain chunk = 264 x 2 x 264 voxels (139,392). Empty lot = 64 x 8 x 64 = 32,768 voxels.

> **Correction on record**: an early estimate in conversation said the 32x32 was ~626 m and the 10x10 ~200 m. Those were wrong; 422 m / 132 m is right (and matches the editor's 420 m).

The active city is chosen by copying a layout+template **pair** to `StreamingAssets/city_layout.json` + `city_template.json` (both must change together — see `RECENT_CHANGES.md`). Tooling:

```
python Tools/generate_city_layout.py --activate     # 32x32, source = *_10x10_backup.json
python Tools/generate_city_layout.py --restore      # back to the 10x10
```

---

## Finding 1 — Terrain Sector Overflow (FIXED, confirmed in playtest)

**Symptom**: ~half the city had no road or sidewalk. Lots/buildings rendered floating on the background colour. Street names still showed on hover (game logic and block anchors were fine — only the terrain render was missing). **No errors or warnings anywhere in `Editor.log`.**

**Cause**: `BuildVoxelTerrain` baked every terrain chunk into a single sector (`terrain_sector`): 1,024 instances, 143M voxels. That violates three independent sector limits (table below). Any one is enough to break rendering; the 10x10 city (100 chunks, 13.9M voxels) was under all of them, which is why it never showed up.

**Fix** (`CityMap3D.BuildVoxelTerrain`): greedy-band the row-major chunk list into `terrain_sector_N` sectors, each ≤ 256 chunks **and** ≤ 2^24-1 voxels. Result at 32x32: **9 sectors** (8 at 16.67–16.78M voxels / 119–120 chunks, i.e. right at the float-exactness ceiling; the last has 67 chunks / 9.4M). A 10x10 city still produces exactly one sector. Collision registration is unchanged. Playtest confirmed the whole map now has roads and sidewalks.

**Honest caveat**: all three limits were fixed at once, so we do not know *which* one was the actual trigger. The ~50% (511/1024) match makes the 511-instance cap the prime suspect, but that is inference, not observation. It does not change the rule going forward: **respect all three.**

### The sector invariants (design rules)

Every baked sector — terrain or building — must satisfy:

| # | Limit | Value | Why | Current worst case |
|---|---|---|---|---|
| 1 | Instances per `DrawMeshInstanced` | **≤ 511** (1023 only with `assumeuniformscaling`) | Unity batch cap; excess silently not drawn. Documented in `GPU_DRIVEN_SECTOR_RENDERING.md` gap #3 | 144 (building), 120 (terrain) |
| 2 | Voxels per sector (float-exact offsets) | **≤ 2^24 − 1 = 16,777,215** | Buffer offset travels through `float4 _BuildingMeta.x`; floats are integer-exact only to 2^24 | 8,847,360 (building), 16,776,184 (terrain, 1,031 under) |
| 3 | Elements per structured-buffer SRV | **≤ 2^27 = 134,217,728** (D3D11) | Hardware/API ceiling | same as #2 |
| 4 | Array length in C# | ≤ 2^31 − 1 | `uint[]` merge arrays | far below |

Rule #2 is the binding one, and the terrain banding is currently only **1,031 voxels** under it on its fullest sector. If terrain tiles ever grow (wider roads, thicker ground, smaller voxels) the greedy banding adapts automatically — it is limit-driven, not count-driven.

> **Dangerous latent case**: building sectors are 4x4 blocks x 9 slots = 144 instances. Raising `sectorSizeBlocks` to 8 gives 64 blocks x 9 = **576 instances → exceeds 511 → silent failure**. See backlog item O1.

---

## Finding 2 — Layout Generator Schema Drift (FIXED)

A prior, unreviewed attempt at a 32x32 layout had drifted from the 10x10 schema: `building_types` changed from `{name: [w,h,d]}` to a bare list, and the template's `grid` changed from `{rows, cols}` to the integer `2`. The Unity loaders happened to ignore both, so it was benign — but it was wrong and non-reproducible (the generator was gone).

`Tools/generate_city_layout.py` replaces it. It derives everything from the 10x10 backups (source of truth), keeps the schema byte-compatible, and keeps special blocks (player HQ, rival HQ, shooting range) at their relative map position using an **edge-preserving** map `round(p*(N-1)/9)` (so the rival HQ stays on the west edge). Validated: schema/key parity, 1,024 unique ids covering rows/cols 0–31 once, layout/template alignment, exactly one player + one rival HQ, every `.stasset` path exists.

---

## Measurements (from `buildmap_log.txt`)

`BuildMap` phases (ms). 10x10 rows are the steady state of ~10 runs.

| Run | Blocks | 1A terrain gen | 1B terrain bake + collision | 2B buildings | Total |
|---|---|---|---|---|---|
| 10x10 (per-building path) | 100 | 0.03 s (25–65 ms) | **0.34–0.38 s** | 0.58–0.69 s | **~1.1–1.2 s** |
| 32x32, per-building path | 1,024 | 0.8–1.2 s | 9.3–11.7 s | 2.2–3.2 s | 12.5–16.5 s |
| 32x32, sector-baked (64 sectors) | 1,024 | 0.6–1.2 s | 8.4–11.5 s | 3.6–7.3 s | 12.8–20.3 s |
| 32x32, sector-baked, **terrain banded** | 1,024 | 0.7 s | 9.8 s (9 sectors) | 3.4 s | **14.0 s** |
| 32x32, banded + **O3 lot pooling (current)** | 1,024 | 1.4 s | 13.4–13.6 s (9 sectors) | **0.36–0.40 s** | **15.5–15.8 s** |

Observations:

- **Terrain bake + collision (1B) scales superlinearly.** 10.24x the chunks cost **~26–32x** the time (0.36 s → 9.3–11.7 s); linear scaling would predict ~3.7 s. (Parallel terrain *generation*, 1A, also grows ~20–40x — 0.03 s → 0.6–1.2 s — but stays ~1 s in absolute terms; it is allocation of 571 MB of chunk arrays, not the bottleneck.) *Evidence for* the quadratic regrow in `VoxelCollisionWorld` (backlog O2) — but **not isolated**; nobody has timed `RegisterTerrainChunk` separately.
- **Terrain banding costs nothing at load** (9.3 s → 9.8 s, within noise) — it is a render-correctness fix.
- **Sector baking does not make loading faster.** It reduces *runtime draw calls* (1 per sector vs 1 per building). Per-building load was actually quicker at 32x32 (2.2–3.2 s vs 3.4–7.3 s).
- ~~**Building bake (2B) has high variance (3.4–7.3 s)**~~ — **O3 confirmed the hypothesis**: after variant pooling, 2B = **0.36–0.40 s** (~10–18x faster, and consistent). Removing ~9,189 clone+scatter+merge passes eliminated the GC churn.
- **Frame rate at 32x32 (post O3+O5)**: ~110 FPS resting, ~80 FPS worst-case during fast camera spin, stabilizing >100 FPS. Sector culling + front-to-back sort + cheap/unlit LOD on far sectors = the win. (Machine-dependent; treat as a baseline for regression, not a target.)
- Phases are synchronous inside `GameUIController.Start`; the whole 14 s is a hard freeze.

### Memory (voxel data only)

| | Voxels | As `uint` (4 B) | Where |
|---|---|---|---|
| Building sectors (64) | ~~314,376,192~~ → ~34M (post-O3: ≤16 shared variants/sector + uniques) | ~~1.26 GB~~ → **~0.14 GB** | merged CPU array (transient) + GPU buffer |
| Terrain sectors (9) | 143,210,888 | **0.57 GB** | chunk arrays + merged array (CPU transient) + GPU buffer + 143 MB `byte[]` collision grid |
| **Total resident GPU** | | ~~1.83 GB~~ → **~0.71 GB** | |

At 10x10 this was ~0.2 GB — invisible. Post-O3, **terrain is now the dominant cost** (0.57 GB + collision grid + the ~10–13 s 1B phase) — the main thing left to attack is terrain dedupe (O4, after M2 seam design) and collision-grid pre-sizing (O2).

---

## Optimization Backlog

Prioritized. **Evidence** = what we actually know. **Status**: all 📝 *not implemented* unless marked. Measure before building anything beyond O1.

| ID | Item | Evidence | Est. win | Effort | Risk |
|---|---|---|---|---|---|
| **O1** | **Loud guard in `RegisterSector`**: `LogError` if instances > 511, voxels > 2^24−1, or elements > 2^27; ideally auto-split in `SectorBaker.BakeAllSectors` too | Verified: no validation exists today; failure is silent (Finding 1). `GPU_DRIVEN_SECTOR_RENDERING.md` already lists an "instance-cap guard" as Phase 1 | Turns silent failures into loud ones; prevents the `sectorSizeBlocks = 8` trap | ~1 hr | None |
| **O2** | **Collision grid: pre-size, don't regrow.** `VoxelCollisionWorld.RegisterTerrainChunk` reallocates + re-copies (triple nested loop, no `Array.Copy`) whenever a chunk extends bounds; row-major order extends −Z every new row | Verified in code. Timing superlinearity (see Measurements) is consistent. **Not profiled** | *Estimate*: most of 1B (several seconds) | ~2 hr (pass overall bounds in, allocate once) | Low |
| **O2b** | Instrument 1B (Stopwatch around `RegisterTerrainChunk`, the merge copy, `RegisterSector`) **before** O2 to confirm the split | — | Avoids optimizing the wrong thing | 15 min | None |
| **O3** | **Empty-lot variant pool.** `SectorBaker` cloned + debris-scattered every `empty_land` (seeded by row/col/slot) → 9,189 unique 131 KB arrays | ✅ **IMPLEMENTED 2026-10-05** — `ProceduralDebrisScatterer.NumVariants = 16`; `GetVariantIndex(row,col,subIndex)` hash-maps lots to a variant; `GetVariantData` scatters each variant once into a shared read-only pool. `SectorBaker.BakeSector` now aliases `buildingMeta[i].x` to the shared variant region (variant data written once per sector). Per-chunk path uses `LoadChunkCenteredShared` on the same pooled arrays via `CityMap3D.LoadEmptyLotChunk`. Scatter calls: 9,189 → ≤16 globally | Building data ~1.2 GB → ~135 MB (≤16 variants × ~131 KB per sector × 64 sectors, plus unique buildings); removes GC churn (likely explains 2B variance — verify against next 2B timing) | — | Visual repetition of debris (16 variants tunable via `NumVariants`; deterministic hash keeps rebakes stable). Per-sector duplication of variants is inherent to sector-local buffers |
| **O4** | **Terrain tile dedupe.** Hash each chunk's `data`; alias identical chunks to one copy via meta offsets (sector meta already carries per-chunk offsets) | *Hypothesis*: most interior blocks have identical seams → few unique tiles. **Unmeasured** — first step is just logging unique-hash count | 573 MB → potentially tens of MB; also shrinks per-sector size so banding relaxes | ~3 hr | **Interacts with river/bridge/alley/main-street** — seam-type combinations multiply variants. Do *after* M2 seam design, not before. Collision still needs per-chunk registration |
| **O5** | Sector LOD + nearest-first depth sort for baked sectors | ✅ **IMPLEMENTED 2026-10-05** — `RenderBakedSectors` now collects visible sectors into `sectorDrawList`, sorts by distance-to-AABB (nearest-first → early-Z rejects occluded fragments; shader writes `SV_Depth` with `ZWrite On`), and applies the same screen-ratio LOD tiers as the chunk path (`_MaxSteps`, `_CheapShading`, `_UnlitLod`, `_LodDebugEnabled/Color` per-sector via MPB). **Also fixed the likely reason `disableSectorCulling` defaulted true**: `DistanceToAABB` distance-culling included the camera's vertical gap, which would wrongly cull sectors directly below the top-down ortho camera — distance cull is now `!isOrtho`-gated (same as the chunk path); frustum culling runs for both projections. Note: `_MaxSteps` self-floors at each instance's cell-crossing bound (tenement ≈ 508 > maxSteps 264), so sector LOD savings come from `_CheapShading`/`_UnlitLod` (skip normal blend + shadow march), not march length | Reduces GPU fill-rate for distant/occluded sectors — biggest expected win at overview camera | — | Culling now active by default (`disableSectorCulling=false`) — watch for sector flicker/pop; padded bounds should prevent it |
| **O6** | 16-bit voxel storage | `.stasset` stores 2 bytes/voxel (`empty_land` = 16 + 65,536 B for 32,768 voxels) but GPU buffers are 4-byte `uint`. **Must first verify what the upper 16 bits carry** (material/flags) before assuming it's waste | Up to 2x on all voxel memory | ~1–2 days, shader change | Medium — touches every shader read |
| **O7** | Parallelize / pool `SectorBaker` (`uint[]` allocs, per-sector bake is single-threaded) | 2B = 3.4–7.3 s single-threaded | Seconds off load | ~half day | Low |
| **O8** | Load behind a loading screen / progressive build (BuildMap is a 14 s synchronous freeze) | Measured | Perceived, not actual | ~1 day | Low. `CITY_STATE_PRESERVATION.md` covers persistence across weeks so this cost is paid **once per session**, not per week |
| **O9** | GPU-driven indirect rendering (`DrawMeshInstancedIndirect`, global buffers, compute cull) | Designed in `GPU_DRIVEN_SECTOR_RENDERING.md` / `GPU_DRIVEN_RENDERING_PLAN.md` (Phases 2–3) | Removes the 511 cap and CPU culling entirely | Large | Pursue **only** if M1 profiling shows the per-sector path is the bottleneck (the existing doc's own recommendation) |

**Suggested order**: O1 → O2b → O2 → (M1 profiler data) → ~~O3~~ (done) → ~~O5~~ (done) → defer O4 until river/bridge seams exist → O6/O9 only if data demands.

### Systems not yet exercised at 32x32

Nothing has crashed, but these have only run at 10x10 and have **no evidence either way** at scale:

- `WaypointGraph` / `RoadGraph` (intersection grid is (rows+1) x (cols+1) = 33 x 33) and `Pathfinder` A* over the larger graph
- `SimulationManager` / `CityGen` with 1,024 blocks — the generated template has `population: 0` and only `empty_land` businesses, so **NPC and business load is effectively zero**; a real populated city is untested
- `VoxelCollisionWorld` probe cost against a 143 MB grid
- Camera extents, `maxRenderDistance`, minimap/overview framing for a 422 m map
- Week-transition rebake (`CITY_STATE_PRESERVATION.md`) at 64 + 9 sectors

---

## Milestone Plan

| | Milestone | Exit criteria | Status |
|---|---|---|---|
| **M0** | Render 32x32 with current assets (direct scale-up of the 10x10) | Loads, whole map visible, no errors | ✅ Done — terrain overflow found + fixed; load ~14 s |
| **M1** | Profile at scale | Profiler capture from the overview camera + a close-up: CPU ms, GPU ms, `perfSectorsDrawn`, VRAM; instrument 1B (O2b). Decide whether O2/O3/O5 are needed | 🔄 In progress — user reports ~110 FPS resting / >100 stabilized post-O3+O5; formal profiler capture still pending |
| **M2** | River + bridge system | See below | 📐 Next |
| **M3** | Blueprint → buildings bridge | Editor zones (core / commercial / industrial / residential, from `zoning_sandbox.html` params in `CityGen1.json`) drive which `.stasset` goes in each slot | ⏳ |
| **M4** | Integration | Waypoint scanner (pipeline task 3), populated template (NPCs/businesses), combat/AI budgets measured against **real** frame cost | ⏳ |

### M2 — River + Bridge (design notes)

The editor already authors this: block `terrain: "land" | "water" | …`, seam types incl. `bridge`, exported as the v3 JSON documented in `CITY_LAYOUT_PIPELINE.md` (`blocks[{row,col,block_id,terrain}]`, `hSeams`, `vSeams`, `gridSize: 32`, `groundTileSize: 11.6`, `spacing: 13.2` — these already match the Unity numbers above). Pipeline tasks 1 and 2 are the prerequisite work:

1. **Layout data**: `CityLayout`/`CityLayoutBlock` (in `CityMap3D.cs`) currently carry only `block_id/name/row/col/buildings`; add terrain + seam arrays. The editor's `block_id` is `r{row}c{col}`; Unity's is `block_{n}` — the generator needs an explicit mapping (row/col is the stable key).
2. **Generator**: extend `Tools/generate_city_layout.py` with `--from-editor <export.json>`: water blocks get **zero building slots** and an empty template (non-buildable), land blocks keep the 9-slot convention.
3. **Terrain painter**: `VoxelTerrainBuilder` paints water (`MAT_WATER` 137 proposed) and bridge (`MAT_WOOD` 10 / `MAT_BRIDGE` 138) per seam instead of uniform asphalt.
4. **Collision/walkability**: water impassable except via bridge seams (waypoint exclusion is pipeline task 3; for M2 a visual + non-buildable result is enough).

**Scale implications to check while doing it** (design questions, not yet answered):

- Terrain chunk size is `(groundTile + roadWidth)/voxelSize`. If seam widths vary (alley 1.8, main street 4.5, bridge 1.6) chunk dims may become **non-uniform**. The banding in `BuildVoxelTerrain` already handles non-uniform chunks (cumulative offsets, limit-driven), but block anchors / spacing elsewhere assume a uniform grid.
- Water/bridge/alley variants **increase unique-tile count**, which is the input to O4 (dedupe) — decide dedupe design *after* the seam set is real.
- Water blocks have no buildings → fewer instances; this *helps* every sector limit.
- Any new sector type must obey the four invariants above.

---

## How to Reproduce / Measure

- **Switch city size**: `python Tools/generate_city_layout.py --size 32 --activate` / `--restore`. (Writes `city_layout_32.json` + `city_template_32.json` and copies both to the active names.)
- **Per-phase build timings**: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Steel_City-Mob_Sim\buildmap_log.txt` — one block per run. Look for `PHASE 1B ... in N sector(s)` (N must be > 1 at 32x32), `PHASE 2B`, and `Voxel cache: ... hits / ... misses`.
- **Full console**: `%LOCALAPPDATA%\Unity\Editor\Editor.log` (previous session: `Editor-prev.log`). It is ~120 MB; read the tail. Useful lines: `[CityMap3D] Terrain: ... baked into N sector(s)`, `[VoxelChunkManager] Registered baked sector '...': <instances> buildings, <voxels> voxels` (check each against the invariants), `[SectorBaker] Baked ...`.
- **Runtime stats**: `perfSectorsDrawn`, `lastCpuCullMs`, chunk LOD counters in the `[Perf]` HUD line (see `RAYMARCH_TRAVERSAL_OPTIMIZATION.md`).
- **Sector sanity check one-liner** (after any layout/size change): confirm max instances/sector < 511 and max voxels/sector < 16,777,215 from the `Registered baked sector` lines.

---

## Lessons / Rules Going Forward

1. **Silent failure is the failure mode.** The sector path produces *nothing* (no error) when a limit is exceeded. Treat every new sector source as guilty until it logs its instance and voxel counts.
2. **Content scale ≠ asset count.** 9,192 buildings but only 3 unique files: the voxel cache made *loading* cheap, while per-lot unique data (debris) made *memory* expensive. Sharing must exist at the **GPU buffer** level, not only the CPU read cache.
3. **Measure the split before optimizing.** Several "likely culprits" above are code-reading hypotheses. Instrument (O2b) then fix.
4. **Don't dedupe terrain before the seam set exists** (O4 depends on M2).

---

## Revision History

| Date | Change |
|---|---|
| 2026-10-05 | Created. M0 complete; documented terrain sector overflow (fixed), generator schema drift (fixed), measurements, sector invariants, optimization backlog O1–O9, milestone plan M0–M4 |
