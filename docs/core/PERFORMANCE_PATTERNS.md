# ⚡ Performance Patterns — Editor-Proven, Game-Ready

**Purpose**: Catalog of optimization techniques developed in `VoxelAssetStudio/voxel_editor.html`
while making a 765k-voxel model interactive. Each pattern solved a measured, real stall —
and each maps directly to a Steel City runtime system (voxel chunks, instanced agents,
raymarch shaders, sim bookkeeping).

**Context**: The editor renders the same kind of data the game does — hundreds of thousands
of voxel instances with per-cell metadata — so the bottlenecks are identical: O(model-size)
work repeated per small change, full-buffer uploads, string-key lookups, and synchronous
scans on an interactive thread.

---

## 1. Tracked-Write Maps → O(edit) instead of O(model)

**Problem**: Every place/erase click rebuilt all 765k instances — a full `getVisibleVoxels()`
scan (string-split per key) + complete instance-matrix rewrite (~400–600ms per click).

**Fix**: `voxelMap` and the tag maps are `Map` subclasses that record every written key in
`voxDirty` — zero call-site changes across 105 mutation sites. `rebuildMesh()` fast-paths:
append / recolor / swap-pop only the touched instance slots.

**Game reuse — the single most applicable pattern**: C# voxel chunk writes go through a
wrapper (property setter or `Set(x,y,z,mid)`) that enqueues the cell index in a dirty list.
Frame end: flush dirty list → partial `ComputeBuffer.SetData`. Runtime window-light flips
(112↔113), bullet holes, building damage — all become byte-cost edits instead of chunk
re-uploads.

## 2. Partial GPU Buffer Uploads

**Problem**: `instanceMatrix.needsUpdate = true` re-uploaded the **entire** instanced buffer
per edit — ~58MB for a 76-byte change.

**Fix**: Record `addUpdateRange(start,count)` spans per touched slot; `needsUpdate` then
emits `bufferSubData` on just those ranges. 58MB → 64B+12B per edit.

**Game reuse**: `ComputeBuffer.SetData(array, managedStart, bufferStart, count)` — same API
shape, same win. Any pooled GPU data (agents, tracers, chunk voxels) should upload dirty
*spans*, never whole buffers. Combine with #1: the dirty list already knows the ranges.

## 3. Delta History Instead of Snapshots

**Problem**: Undo cloned the full `voxelMap` (765k entries) + 3 tag maps per edit —
~50–150ms click tax and ~200MB held across 50 history entries.

**Fix**: Each tracked write captures the cell's *previous* value; an undo entry is
`key → [oldVal, newVal]` per changed cell. Undo/redo replay deltas — so undo of a small
edit is itself a small edit (and rides the #1 incremental path).

**Game reuse**: Event journals — replay/delta saves, netcode sync, week-end state diffs in
the city preservation system. Store "what changed" not "what state was".

## 4. Shader-Side Visibility (Clip → Fragment Discard)

**Problem**: Slice sliders rebuilt the mesh every `input` tick — the "visibility filter"
lived in the CPU instance list.

**Fix**: `uVoxClipMin`/`uVoxClipMax` uniforms + a `vVoxOrigin` varying (the instance
translation) → fragments outside the slice `discard`. Slider = two uniform writes, zero CPU.
Uniforms refreshed once per frame in the render loop so **every** write site is covered —
no per-site wiring.

**Game reuse**:
- Cutaway/interior views for raids, planning, sniper LOS — one global uniform, no mesh work.
- Debug slice modes (already half-exists via raymarch DDA bounds — same principle).
- Per-frame `Shader.SetGlobal*` sync in ONE central place beats scattered invalidation calls.

## 5. Procedural Shader Detail Instead of Geometry

**Problem**: Grid lines needed a wireframe instance per voxel (~9M segments at 765k) and was
silently disabled above 60k.

**Fix**: `onBeforeCompile` fragment test — a fragment is on a box edge when ≥2 local coords
are near ±0.5. Zero extra instances, works at any voxel count, toggles via uniform.

**Game reuse**: Edge outlines, damage cracks, selection/building-boundary highlights,
debug overlays — all cheaper as fragment math on existing surfaces than extra geometry.
Same idea as the raymarcher shading the hit surface instead of merging coplanar faces.

## 6. Epoch-Marked Scratch Grid

**Problem**: `Set<string>` membership in flood fill / selection shell / dedupe — tens of
thousands of string allocs per hover.

**Fix**: Shared `Uint32Array` sized W·H·D; `buf[cellIdx] === epoch` means "seen" — bump the
epoch per call instead of clearing. Flat numeric BFS stacks. O(1) membership, zero allocs.

**Game reuse — probably the biggest sim win**: any repeated grid traversal — explosion
spread, fire propagation, territory/influence BFS, pathfinding scratch, LOS marks. A
`uint[]` scratch grid + epoch counter per system kills `HashSet` churn in hot sim paths.

## 7. Chunked Cooperative Scans

**Problem**: Hovering a 300k-cell region ran a synchronous ~150ms BFS — page froze.

**Fix**: Resumable flat stack processed in ~30k-cell rAF slices; tooltip reads
`Scanning region…` until done. Results memoize; sliding to a new region just queues.

**Game reuse**: Spread any large computation across frames — Unity coroutine or a
per-tick work budget in SIM_TICK: influence propagation, district scans, nav re-flow after
demolition. 60fps budget = process N cells/tick, resume next tick.

## 8. O(1) Region Membership via Member-Marks

**Problem**: Memoizing "which region is this cell in" cost a `Map` entry per member cell
(300k writes per region).

**Fix**: A dedicated `_memberBuf` — fills stamp their epoch on result cells; membership =
`memberBuf[cellIdx] === regionEpoch`. Entry cache is 64 region records, not 300k cells.
(Kept in a *separate* buffer from the visited-marks grid — other passes re-mark that one.)

**Game reuse**: "Which territory/room/district is this cell in?" queries — stamp cells with
a region id/epoch during the BFS that builds them; queries become int compares instead of
`Dictionary` lookups. Influence/zone membership checks per agent per tick.

## 9. Generation Counters for Async Staleness

**Problem**: A chunked fill could complete after the model was edited → stale memo.

**Fix**: `_fillGen++` on every mutation; async completion discarded if `gen` changed.
`_memberFloor` invalidates all cached member-marks the same way.

**Game reuse**: Any async op on mutable state — Addressables chunk loads, web requests,
Jobs completing next frame. Stamp the job with the state generation; discard on mismatch.
Cheaper and safer than cancellation tokens everywhere.

## 10. Instance↔Key Slot Bookkeeping

**Problem**: `instanceId` was used to index the visible-voxel array — broke silently once
incremental swap-pops reordered slots.

**Fix**: `slotByKey` (voxel→slot) + `slotKeys` (slot→voxel) maintained on every write —
raycast resolves `instanceId → slotKeys[id]` → authoritative voxel key.

**Game reuse**: GPU picking — `instanceID → entity` maps for clicking units/buildings in
city view; hit-resolution in the raymarch pipeline (cell coord → entity/chunk record).
Never derive identity from array order once buffers can compact/reorder.

## 11. View-State Signature for Incremental Safety

**Problem**: How do you know the fast path is legal? Hidden inputs (clip bounds, view modes,
pose params) could change without map writes.

**Fix**: `viewSig()` packs every non-tracked input into a signature string, captured at the
last full build. Mismatch → full rebuild. `FlagMap` (`matVisible`/`groupVisible`) forces
rebuild on writes automatically.

**Game reuse**: LOD/camera/bake-state hashing — "can I update this chunk incrementally or
must I re-bake?" is the same question. One signature beats N ad-hoc dirty flags that drift.

## 12. Swap-Pop + Grow-Only Buffers

**Problem**: Removing an instance mid-buffer either left holes or required a rebuild;
buffers re-allocated per size change.

**Fix**: Move the last live instance into the freed slot, shrink `count` — buffer stays
compact at O(1). Capacity doubles on overflow, never shrinks.

**Game reuse**: Standard pool practice — agent lists, tracer/beam pools, decal instances.
Applies to `ComputeBuffer`-backed collections: keep `count`, swap-pop removals, grow-only.

## 13. Two-Level DDA — Brick Occupancy Skip (SHELVED 2026-10-11)

**Status**: SUPERSEDED — re-implemented and shipped Oct 12, 2026 (Phase 4 in
`docs/systems/RAYMARCH_TRAVERSAL_OPTIMIZATION.md`). The revive condition below
was met by demolition creating march-bound hollow interiors (bombed tenement:
81 FPS close vs 120 pulled back, ~0.55 ms CPU). New design differences:
`_OccEnabled` opt-in replaces animated-asset brick dilation, per-building occ
offsets ride a dedicated `nointerpolation uint` varying (not `yaw`), landing
state resyncs by draining all voxel boundaries ≤ exit-T per axis (fp-identical
to sequential DDA, selftest-verified over 7,200 ray cases).

**Problem it targeted**: the proxy raymarcher held ~120 FPS top-down but
degraded at every other angle — angled rays march deep through building
interiors at voxel granularity, and interiors are mostly air (~21 cell
iterations per 8³ brick of empty room).

**What was built**: a 4³-brick occupancy bitmask per volume (one bit per brick,
set iff it holds any non-air voxel). The march tested occupancy once per newly
entered brick; an all-air brick was crossed in one boundary jump (exit-t along
the ray + DDA state resync), in both the primary and shadow marches. Per-asset
grids for chunks and instanced groups, per-building slices for merged sectors
(word offset riding the unused `yaw` varying), 2-brick dilation for
group-animated assets so posed limbs couldn't vanish into rest-air bricks.

**Why it was shelved**: the grazing-angle drop was traced to overdraw from
~780 stacked per-building volumes (`useSectorBaking` off) and fixed by baking;
no isolated before/after of the skip itself was recorded. The instancing
recovery then reverted the shader to its proven base and the production shader
was kept minimal. Thin structures (1-voxel rails, fire escapes) also defeat any
brick size — they poison whichever brick they sit in.

**Where it lives**: branch `archive/occupancy-dda` (shader half, together with
ray jitter and the original heatmap) and commit `57fb572` (C# half:
`GetOccupancy` / `ComputeOccupancy` / `DilateOccupancy`, the per-draw
`_HasOccupancy` binding). Both halves have to return together.

**Revive when**: the heatmap (#14) shows ray-march-bound cost — long step
counts on interior air — that sector baking doesn't remove.

**Prior art**: this is the MultiDDA/two-level grid of voxel-rendering
literature — benchmarks (e.g. dubiousconst282/VoxelRT) show ~4.7× ray
throughput over flat DDA on sparse-interior volumes. The tight-AABB proxy
already skipped air *above* content; brick maps skip the air *inside* it.

## 14. Keyword-Gated Debug Views (`VOXEL_DEBUG_VIEW`)

**Problem**: per-pixel debug counters inside a hot shader loop cost every pixel
forever, and editing the production path risks the very thing being measured.

**Fix**: `#pragma multi_compile _ VOXEL_DEBUG_VIEW` on each pass; the counters
and the color override live inside `#if defined(VOXEL_DEBUG_VIEW)`. C# flips the
global keyword whenever `iterVisMode != Off` (the `V` key cycles Off /
IterationHeat / StepChannels / MissWaste). With the keyword off, the
preprocessed shader is byte-for-byte the production shader — verified
mechanically by stripping the guarded blocks and diffing against the pre-edit
file.

**Views**: IterationHeat (blue <80, green ~80-160, red >160 total march +
shadow iterations); StepChannels (R voxel-march steps, B shadow-march steps);
MissWaste (heat only on rays that exited with no hit — pure wasted march). Miss
rays render instead of discarding so air-march cost is visible; pass 2 (glass)
draws nothing so it can't hide the heat.

**Extending**: new views (volumes-per-pixel overdraw, LOD tier, glass layers)
belong inside the same keyword so they never touch the production variant. A
volumes-per-pixel view would have exposed the 780-chunk overdraw at a glance.

**Gotchas**: `multi_compile` with a single keyword needs the `_` placeholder or
the variant is always on (see
`docs/known_issues/rendering/MULTI_COMPILE_SINGLE_KEYWORD_ALWAYS_ON.md`). In the
Editor the first `V` press compiles the debug variants on demand — expect a few
seconds of blank proxy volumes; player builds precompile every variant.

## 15. Grid DDA Pick Instead of Instance Raycast (2026-11-03)

**Problem**: `raycaster.intersectObjects([instancedMesh])` tests **every instance** — cost
is O(baked voxels), paid per call. The Room builder's aim mode picks on every mousemove;
after stamping one 62k-voxel office slice the pick measured **15–69 ms/call** (and the
editor's own highlight raycast was paying it a second time per move). Blank canvas: 0.2 ms —
which is why it only appeared once content was baked in.

**Fix**: `raycastGrid()` walks the `voxelMap` along the camera ray (Amanatides–Woo DDA):
clip the ray to the volume AABB, step cell to cell, stop at the first occupied cell; the hit
normal is the axis of the last step (entry face for the first cell); ground-plane fallback
when nothing is hit. Mirrored Unity view is handled by un-mirroring the ray origin/direction
X about `volumeWorldCenter().x`. Cost ≈ `W+H+D` steps regardless of model size: **~0.01 ms**.
Returns the same `{x,y,z,normal,placeX/Y/Z,mid}` shape as `raycastVoxelMesh`, so callers
don't care which one they got.

**Verified**: identical hit + placement cell vs the mesh pick on 118/118 sampled pixels
(Unity-mirrored) and 114/114 (unmirrored).

**Caveat**: DDA reads the raw `voxelMap`, so it does **not** skip slice-clipped or
hidden-material/group voxels the way the mesh pick does (`inClipBounds` / `isVoxelShown`).
Fine for placement aim; add those two checks inside the loop before using it for edit tools.
(The sweep found 1 grid-only hit — a cell the mesh pick skipped as hidden/clipped.)

**Game reuse**: CPU-side picking — cursor→building cell, projectile broadphase, LOS probes —
over the chunk/cell store with a DDA, not a physics raycast against per-voxel colliders or an
instance-list scan. Same traversal the raymarcher already does on the GPU.

## 16. Block-Merged Preview Geometry — O(blocks), Not O(voxels) (2026-11-03)

**Problem**: The Room builder's ghost silhouette drew one instance per voxel — 38,860
instances for a 62,340-voxel slice — with a matrix write, color write and (originally) six
string-key `voxelMap` lookups per voxel **every time the aim cell changed**: **31–38 ms**.
The first fix, culling interior voxels (all 6 neighbors filled), removed only 38% — walls
here are 3 voxels thick, so nearly every voxel *is* surface.

**Fix**: Bucket voxels into 4×4×4 blocks; each block draws **one box = the AABB of the
voxels actually inside it**. Thin walls/floors stay exact (the AABB shrinks to the content),
solids collapse ~64×. Built once per model and cached on it (`data._blocks`); rotation
transforms the block AABBs, not the voxels. 38,860 → **1,888 boxes, 1.5 ms** per rewrite.
Contact class (cyan free / yellow flush with a volume face or placed voxel / red pokes
outside) is evaluated per block with ≤6 lookups, and skipped entirely on an empty canvas.

**Rule**: a preview must be *representative*, not exact. Interior-culling is the wrong lever
for thin-shell content — merge instead. Scaled prefabs: position = `pos + idx*s` (same as
placements); the first ghost used unscaled offsets and would have spread a `.01` prop 3× wide.

**Game reuse**: build-placement previews, blueprint ghosts, distant-LOD proxies for
buildings, selection volumes — anything translucent drawn over a big opaque scene (overdraw
makes per-voxel ghosts doubly expensive).

## 17. Change-Gate Preview Work + One Cursor Owner (2026-11-03)

**Problem**: `mousemove` fires at display rate (60–120 Hz); the aim *cell* changes a few
times a second. Two systems (the editor's highlight and the ghost) each reacted to every
move and each paid O(model).

**Fix**:
- **Signature gate** — `refreshGhost` builds a signature of every input that affects its
  output (`model|pos|rot|origin|voxelSize|dims|voxelMap.size`) and returns early when
  unchanged. Beats debouncing: zero lag, zero wasted work. Include a model-change counter
  (`voxelMap.size`) so contact colors refresh after an edit.
- **Single cursor owner** — while aim mode is armed the ghost *is* the highlight, so
  `updateHighlight` is skipped. Don't stack two per-move systems.
- **Disarm must clear the visual** — hide the mesh and reset the signature. Changing mode
  without clearing the picture reads as "Esc did nothing" (it was filed as a bug).

**Game reuse**: any hover/preview system (tooltips, placement cursors, range rings) —
gate on a state signature, and make sure only one system owns the cursor at a time.

## 18. Measure Headless Before Fixing (2026-11-03)

**Problem**: Two fix rounds on the ghost (interior cull, raycast throttle) were guesses and
barely moved it. One measured pass named the actual costs in minutes.

**Recipe**:
1. Expose the module-scoped functions under test on a debug handle
   (`window.__room = { raycastGrid, raycastVoxelMesh, refreshGhost, stampSelectedPrefab, ... }`)
   — the editor is a `type="module"`, so nothing is global otherwise.
2. Drive Edge headless over CDP (`--headless=new --remote-debugging-port=... --use-angle=swiftshader`;
   template: `docs/_shot2.js`), `Runtime.evaluate` with `awaitPromise`, and time N calls with
   `performance.now()`.
3. Measure at **each content state** — blank, 1 stamp, 2 stamps. Cost scaled with what was
   *baked*, not what was previewed; the blank canvas looked perfect.
4. For a replacement path, diff it against the old one for **correctness** on a pixel sweep,
   in every view mode (here: mirrored and unmirrored).
5. Instrument phases of one-shot actions (`window.__stampT` = write / rebuild / history) so
   "slow commit" can be attributed. Stamp commit measured 240–480 ms: write ~80, `rebuildMesh`
   140–340, history ~25 — a one-time cost, not per-move.

6. **Use the CDP profiler when you don't know where the time goes**: `Profiler.enable` ->
   `Profiler.start` -> run N repetitions of the action -> `Profiler.stop`, then sum
   `timeDeltas` per `callFrame.functionName:line` from `profile.samples`. A self-time table
   (top 12) named the move-preview rebuild, its GC churn, and the highlight re-add in one run.

**Limits**: SwiftShader makes CPU-side timings trustworthy but says nothing about GPU frame
rate — judge fill-rate/overdraw on real hardware.

## 19. Translate, Don't Rebuild — Cached Shell + Mesh Offset (2026-11-03)

**Problem**: The native Move-mode paste preview rebuilt the whole highlight shell on **every
nudge**. For a 62,340-voxel stamp that was **~450 ms per PgUp/PgDn** — and held keys fire at
repeat rate, so presses queued and the editor froze. Profile (one run, 12 moves):
`buildShellMeshes` 43%, GC 34% (62k fresh `[x,y,z]` arrays per move), `showHighlight` 16%
(geometry dispose + re-add). The `movePaste` offset math itself was ~10 ms.

**Fix**: a pure translation doesn't change the shape. Build the shell **once per clipboard
shape** at offset 0 (cache key: clipboard identity + `_pasteRev` + origin + length),
`showHighlight` identity-skips it on every later call, and each nudge just does
`hlMesh.position.set(pasteOffset)`. Rotation bumps `_pasteRev` (rebuild); ⇄ Mirror Move gives
cells different offsets, so `_pasteUniform = false` falls back to the old per-cell path. The
in-bounds count shown in the tooltip is an allocation-free loop. **448 ms → 2.6–4.9 ms (~100×).**

**Rule**: when a preview is *dragged*, the geometry is constant and only the transform changes.
Key the cache on what changes the **shape** (rotation, clipboard contents), never on position.

**Game reuse**: placement ghosts, selection/blueprint previews, dragged LOD proxies — keep the
mesh/ComputeBuffer, move the Transform; cache by shape signature. Pairs with #16 (merge) and #17
(gate) for previews that are both big and dragged.

---

## Cross-Cutting Lessons

1. **Profile the bookkeeping, not just the draw** — every stall here was CPU-side Map/string
   churn or buffer management, not fragment shading. The GPU was never the bottleneck.
2. **Intercept at the data structure** — subclassing `Map` gave dirty-tracking for free at
   105 call sites. In C#: property setters / wrapped collections, not manual `MarkDirty()`
   calls people will forget.
3. **Uniforms > rebuilds** — if a change can be a shader uniform (slice, grid, visibility
   bitmask, palette LUT), it costs nothing per interaction. Reserve CPU work for actual
   topology changes.
4. **Membership is the hidden hot path** — `Set<string>`/`HashSet<string>` membership is
   where editor+sim code quietly burns frames. Epoch-marked flat grids (#6/#8) replace them.
5. **Async needs staleness guards** — the moment work spans frames, you need generations
   (#9) or results arrive into a world that already changed.
6. **IMGUI bills per event, not per frame** (2026-10-11 finding) — `OnGUI` runs once
   per event (Layout + Repaint + every input event ≈ 4–6×/frame), so a "cheap"
   60-widget debug HUD plus `FindFirstObjectByType` calls inside it was a ~25ms
   tax (32 FPS open → 120 minimized). Profiler shows the cost under
   EditorLoop/Others, not Scripts — Scripts read 0.98ms while the HUD burned the
   frame. Cache tab text into one rich-string label at ~4 Hz and throttle scene
   scans. See `docs/known_issues/rendering/IMGUI_HUD_FRAME_TAX.md`.

7. **Check this catalog before adding per-mousemove geometry** (2026-11-03) — the Room
   builder ghost reintroduced the exact anti-pattern `known_issues/editor/HIGHLIGHT_INSTANCED_REBUILD_LAG.md`
   documents ("rebuilt ~2xN instances every mousemove"). New interactive previews: surface
   shell or block-merge (#16), signature-gate (#17), one cursor owner.
8. **Test perf with content loaded, not on a blank canvas** (2026-11-03) — pick and ghost
   cost were ~0 on an empty volume and 15–69 ms after one stamp. Whatever scales with baked
   voxels (`intersectObjects`, full-instance scans) hides until the model is big (#15, #18).
9. **Interior-culling only helps solids** (2026-11-03) — thin-shell content (walls, floors,
   ceilings: 3–6 voxels thick) is already all surface. Merge into blocks instead (#16).

10. **Every mode that swallows input needs a disarm on EVERY exit path** (2026-11-03) — aim
    mode intercepts left-click on the canvas; it also had to disarm on tool change, asset-type
    switch and Room-tab leave, or it silently ate clicks elsewhere (found in the audit sweep,
    verified headless for each path).
11. **File metadata is not scale truth** (2026-11-03) — `commission_wall`/`commission_floor`
    carry `voxelSize` 0.025 but run at the room grid's 0.03 in the scene (the loader overrides).
    Derive preview/placement scale from the asset class (`prefabVS`: building/room = grid scale,
    props keep native), or ghosts draw 17% small and a strict mismatch guard rejects valid pieces.
12. **A guard added in an audit must be re-run against real assets** — the scale-mismatch guard
    rejected the wall prefab on first test; the headless regression pass caught it before it shipped.

## Deferred / Deliberately Not Done

- **GPU flood fill** (ping-pong texture spread): result must return to CPU anyway
  (`selVoxels`/sim need the cell list) — readback ≈ the BFS cost. Wrong problem.
- **GPU vertex-culling of clipped instances**: WebGL2 has no compute/indirect draw;
  revisit only at multi-million-voxel scale.
- **Shader-side material/group visibility bitmask** (next candidate if 👁 toggles ever
  stutter): `aMatId` attribute + `uMatHidden` bitmask → uniform-write toggles.
- **Selection mask texture** (kills `buildShellMeshes` + the 150k bbox fallback):
  `Data3DTexture` + fragment tint — the remaining GPU-able piece of the highlight path.
- **`cavity` fill preview** still synchronous (chained exterior-air compute);
  `computeExteriorAir` still uses `Set<string>` — candidate for the #6 treatment itself.
- **Grid pick honoring slice clip / hidden materials** (#15 caveat): add `inClipBounds` +
  `isVoxelShown` inside `raycastGrid`'s loop before it backs any *edit* tool.
- **Ghost via shell/greedy-mesh geometry** instead of boxes (#16): only if block boxes ever
  read as too coarse; the AABB-per-block approach is exact for thin walls and good enough
  elsewhere.
- **Per-cell Mirror Move preview** still rebuilds the shell per nudge (`_pasteUniform = false`
  path, #19) — rare; fix would be one cached shell per mirror half, each offset separately.
- **Placement meshes** are one InstancedMesh per voxel (`buildPlacementMesh`): placing a huge
  prefab (e.g. the 240k office) is O(voxels) once and again on any volume re-anchor. Block-merge
  (#16) is the lever if that ever matters.
