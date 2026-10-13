# Glass Shatter Mechanic — Prototype Proposal

**Status**: ✅ P0 + P1 + sector-write half of P2 implemented — bullet puncture, cracked halo, shard burst, synthesized SFX, damage reset, mold bench tooling live; damage writes reach baked-sector merged buffers. **Demolition bench** added: spherical `VoxelDamageMath.Blast` (per-material resistance, seeded falloff) + PICK BLAST/DETONATE in the Range tab. Remaining: gid pane semantics (cascade/blast-radius-over-gid) and P3 sim hooks
**Created**: October 9, 2026
**Parent**: [`VOXEL_GLASS_TRANSPARENCY.md`](VOXEL_GLASS_TRANSPARENCY.md) §Windows ↔ Projectiles — this doc resolves the "Shattering" option that section framed
**Depends on**: `groupMap` gid tagging (editor), runtime dirty-cell writes (`docs/core/PERFORMANCE_PATTERNS.md` #1–2), Tier 3 individual-mutation draw path

---

## The Core Idea — Choreographed Randomness, Not Physics

No rigidbody physics anywhere in this design. Voxel glass breakage is
**discrete state transitions + one GPU particle system** — the "simulation" is
fake in the best way: a seeded RNG decides which cells die, and the particles
sell the chaos.

**The key trick — removed voxels ARE the particles.** The cells a break removes
become the shard-burst spawn list: each dead voxel reappears as a tiny cube
particle at its exact position, tinted by its palette color, continuing along
the projectile's direction. The eye reads it as *the voxels themselves flying
off* — the building disintegrates precisely where geometry vanished. At 0.05m
voxel scale, shards are basically sparks — exactly what glass looks like at
city zoom. Rigidbody debris is consciously rejected: full collider cost for
confetti nobody can see.

## Material vs. Behavior (the data split)

- **Material 112/114** answers "how does it render" — already shipped, unchanged.
- **The pane gid tag** answers "how does it behave" — pane-unit semantics.
  A window is a cluster of pane voxels sharing a gid in `groupMap`; the same
  machinery Q+click selects by in the editor. Tags live in the `.stasset`
  SKEL tail / `.groups` sidecar.
- Orthogonal by design: reinforced/decorative glass can skip the tag
  (never breaks); non-glass breakables (thin doors, boards) can take the tag
  later without being glass material.

## Pane Groups as a Render Primitive

The gid pane unit has a **second consumer beyond shatter**: the raymarch glass
path. A `_PaneIDs` per-voxel buffer (indexed exactly like `_VoxelData`) lets the
shader charge **one surface's worth of tint per pane** instead of per voxel
face crossed:

```
ray hits glass voxel → paneId = _PaneIDs[idx]
if paneId != 0 and paneId == lastPaneAlongRay → march through, no re-tint
else → accumulate tint once, remember paneId
```

**What it fixes**: grazing a *wide uninterrupted pane* — a ray sliding through
a storefront band crosses dozens of cells of one pane and used to stack tint on
each. Now the pane charges once. Separate windows along a ray still charge
separately (they are optically different surfaces — that accumulation is real).

**ID source — authored gid with flood-fill fallback**:

- Preferred: generators emit pane gids per window (authoring contract below).
- Fallback (shipped): `VoxelChunkManager.ComputePaneIDs` runs a 6-connected
  flood fill over glass materials (112/114/147) at load → contiguous cells
  share an ID. Works on untagged assets and — critically — is the same pass
  that **re-structures** a pane after partial breakage: re-flood the gid's
  survivors and a pane split in half becomes two panes.

**Budget interaction**: skipped same-pane cells also skip the
`_GlassMaxLayers`/throughput accounting, so a grazing ray through one wide
pane consumes one layer slot, not N.

## The Shatter Model

```
damagePane(hitCell, rayDir, damageType, seed):
  impact cell   → removed + shard burst along rayDir
  ring 1        → ~80% removed (seeded), survivors → cracked material
  ring 2        → ~30% removed, survivors → cracked
  pane gid      → if type == BLAST or > FAIL_FRACTION of pane gone
                  → cascade-release remaining cells
```

Output: an irregular **bullet-hole** with a white spider-web **cracked halo**
— not the whole window politely deleting itself. Every shot leaves a different
scar because ring removal is seeded-random.

## Damage Type → Break Radius

| Source | Radius | Result |
|--------|--------|--------|
| Bullet | 1–2 voxels | Surgical puncture + crack halo; spall sprays **inward** along ray |
| Shotgun / melee | 3–5 voxels | Big ragged hole; ~half the pane gone |
| Blast / vehicle ram | whole gid | Entire pane cascades — every cell shard-releases |

Directionality is free: debris direction = projectile ray direction
(spall through the pane into the room — a real-looking detail at zero cost).

## Presentation Layer (downstream of sim, same split as firearms/tracers)

- **Shard burst** — instanced mini-cube particles; spawn list = removal delta;
  velocity = rayDir + spread cone + gravity + fade. CPU-integrated; each shard
  sweeps the voxel world with one DDA ray per frame (`ShardPhysics.Step`, no
  Unity colliders) and a shard that comes to rest on a damaged volume is
  written back as rubble — see *Debris vs. the Voxel World* below.
- **Cracked halo** — surviving ring cells swap to a "cracked glass" material
  variant (whiter, higher alpha). Persistent — the window looks wounded
  until it fails. Palette id **134 "Cracked Glass"** now exists
  (`mob_materials.py` + `StAssetReader.MobColors` + `IsGlassMaterial` +
  shader glass-lighting branch); 130-133 were taken by the editor's char
  palette.
- **Cascade-release** — on pane failure, remaining cells don't pop in one
  frame: they release over ~0.3s radiating from the impact point
  (event-scheduled, not physics). The window *crumbles*.
- **Sparkle-on-survivors** — brief emissive pulse on cracked cells
  ("disturbed but intact") — emissive material slots already exist.
- **Sound by removed-count** — 3 cells = tink, 40 cells = crash.

## Debris vs. the Voxel World (implemented — swept bounce + rubble)

`Assets/Scripts/Sim/ShardPhysics.cs` (pure functions; offline-tested in
`Tools/dda_selftest` sections [13]–[15]) + `VoxelDamageSystem.UpdateShards`.

- **Swept bounce** — a shard is a sphere (radius = half its cube edge) swept along
  its velocity by one `VoxelCollisionWorld.Raycast` per frame. The DDA visits every
  cell the step crosses, so a fast shard cannot tunnel a 1-voxel floor. A hit stops
  it a radius off the face and reflects velocity about the face normal
  (restitution `shardBounce`, tangential ×0.6). Floor, roof, wall and rubble are
  the same code — only `hit.normal` differs. The flat `shardGroundY` plane remains
  the fallback; a shard whose center ends up inside solid fades in 0.15 s.
- **Rest → rubble** — a shard that comes to rest on a floor-like face (normal.y > 0.7,
  speed < 0.4 m/s) of a volume that **already has damage state** writes its OWN
  material back into the AIR cells it settled on (`Deposit`: never over solid,
  clipped to the grid, one dirty-span upload). The material is the cell's original
  one, or the majority material of an island chunk, so debris is literally the
  building's remains put back lower down — no new palette entry, no flag bit
  (a bombed building is inert for the rest of the week, so rubble needs no special
  blast/connectivity semantics). Dust = 1 cell on the first air layer above the
  surface; a chunk POURS its `mass` via `DepositHeap` — bounded cells per frame
  (`rubblePourPerSec`, default 8000/s) while the rendered cube shrinks with
  ∛remaining-mass, so the chunk visibly melts into the mound rather than popping
  one into existence. The mound keeps the square shrinking-layer silhouette
  (−1 half-extent every 2 layers, footprint cap `rubbleMaxCells`) but the
  outermost ring of each layer is deterministically ragged (~1/8 cells skipped
  by index hash). Each written cell drops down its column to the lowest open
  cell, so a mound over a blast crater fills the hole first — form-fit, not a
  flat base floating over the cavity. Ring order is deterministic, so each
  pour tick resumes the same pile without double-writes; a pouring shard
  outlives its expiry until its mass is spent. Piles stack — the next shard
  lands on the last one.
- **Angle-of-repose slump** — every deposited cell joins the volume's rubble
  set + candidate queue, and `ShardPhysics.Slump` runs a bounded slice each
  frame (`PumpRubbleSlump`, 4096 moves/frame): an exposed rubble cell slides
  one step down into an open lateral-down neighbor (45° repose, hash-rotated
  direction → no axis bias). Only rubble-set cells ever move — structural
  voxels are untouched — and moved cells re-check + expose the cells below/
  beside the vacancy, so the pass converges and mass + material identity are
  preserved per cell. Net effect: mounds relax into slopes, rubble on ledges
  spills over the edge, and side-blast piles cascade out of the cavity mouth.
  A rubble cell re-blasted to AIR drops out of the set on its next pop.
- **Material blending** — `BuildBuckets` records each bucket's dominant AND
  runner-up material with exact counts; the shard carries `mat2` + `mix2`
  (secondary share) and `DepositHeap` picks per cell by a deterministic index
  hash weighted by the share — a 60/40 brick/asphalt boundary bucket writes a
  genuinely mottled 60/40 mound instead of majority-vote flattening.
- **Mass-conserving emission** — removed cells don't spawn 1:1 particles:
  `ShardPhysics.BuildBuckets` spatial-hashes each wavefront's removed cells (and
  the whole detached span as one range) into `debrisClusterM`-pitch buckets →
  ONE shard per bucket carrying the bucket's cell count as `mass`. The shard
  budget is explicit: blast fronts share ~70% of `maxShards` proportional to
  their AIR-cell counts, the island drop gets ~30%, and `EmitClusterRange`
  auto-coarsens the pitch until a range's bucket count fits its share — every
  removed cell lands in a bucket, so emission is mass-conserving at any budget
  (a 400k-cell tenement blast emits ~226 shards, not ~15,000 candidates that
  get evicted — the earlier failure mode). Each bucket spawns its shard at a
  real removed cell nearest the centroid — the raw centroid can sit inside
  surviving solid (measured: 429 embedded starts on a center blast → 0).
  In flight a shard renders as a plain cube, edge ∛count **capped at 5 voxels**
  — a chunk reads as a chunk, never as a true-shape slab (render size ≠ deposit
  mass). Launch speed and tumble ÷ √edge (big sections drift and thud, dust
  flies far). Bullet spall stays 1-cell dust (already 1:1).
- **Mass accounting + rescue** — the console proves the pipeline: `🧨 debris`
  logs emitted shard count, carried cell mass, evicted mass and a top-3
  per-material breakdown (`matId×cells`) per detonation — compare against pile
  colors to distinguish real pad removal from blend mixing; `🧱 rubble
  settling` logs written/carried per deposit (blend shown as `mat/mat2@share`). A settled shard evicted
  over the budget spills its heap before removal, and a shard that embeds in
  surviving solid after the collapse window spills its mass into the air around
  the wedge point (a clipped chunk leaves its cells in the wall's cavities).
- **Collapse ordering** — deposits wait out the shock-front playback window
  (`planBusyUntil`), so rubble never lands on an island that is about to drop and
  hang in mid-air; at deposit time the surface under the shard is re-probed and a
  shard whose support vanished wakes and falls again.
- **Toggles** (Range tab → Demolition): `debris bounces off voxels`,
  `debris settles into volume (rubble · N cells)`. Turn rubble off to keep the
  glass monolith a clean blast-field instrument.
- **Debris test lot** — `block_48` ("[DEBRIS TESTBED]", next to the glass
  monolith): `debris_test_lot.stasset` (192×120×192) = a full-footprint asphalt
  pad every pile is guaranteed to land on + three distinct-material masses —
  a 72³ red-brick cube, a thin stone wall, a 30³ light-wood cube — so rubble
  color shows provenance at a glance. `blast_target*` attachment points mark
  deterministic aim centers. `Tools/gen_debris_lot.py` regenerates it; same
  caveat as block_47 — hand-maintained in `city_layout.json`, a
  `generate_city_layout.py` regen reverts it.
- **Known limits** — a mound only fills the AIR cells inside its footprint cap,
  so a bucket bigger than the space can hold drops the excess; shards that fly
  out of the volume (terrain landings) and shards evicted while still flying
  take their mass with them — the `🧨 debris` log reports the evicted share.
  The center ray lets a large chunk graze into a wall by up to its radius while
  moving parallel to it. Untouched neighbouring buildings just linger and fade.
  Rubble is not persisted (same as all damage — baking the [BOMBED] state is
  separate, open work).

## Sim Layer (authoritative)

- **Grid delta** — pane cells → AIR (or a `glass_shard` debris material if
  crawl-through collision is wanted — open question below).
- **Noise event** — shatter radius → NPC/guard alert. More gameplay-relevant
  than any visual.
- **LOS opening** — a removed pane changes what projectile/sight raycasts see
  *permanently*: firing lanes into interiors, sniper overwatch out.
- **Entry semantics** — broken storefront = burglary access point; feeds
  smash-and-grab extortion verbs.
- **Persistence** — damage delta map saved with city state (the broken window
  stays broken).
- **Determinism** — all removal RNG is seeded; identical inputs → identical
  scar. Required for save/replay and any future netplay.

## The One Real Engineering Cost

Not physics — the **dirty-cell chain**:

```
cell removal → chunk/palette data update → raymarch grid refresh → LOS invalidation
```

The editor already proved this path (tracked writes + partial
`ComputeBuffer.SetData` — PERFORMANCE_PATTERNS #1–2). The runtime
`VoxelChunkManager` needs the same incremental path when a pane dies mid-game.
This is infrastructure work, and it's where feasibility actually lives.

## Prototype Plan

| Phase | Scope | Exit test |
|-------|-------|-----------|
| **P0** — puncture ✅ | Bullet ray hits glass → impact cell + ring-1 removal (seeded, `ring1RemoveFraction` tunable), AIR swap, COW array + dirty-cell upload to **voxel and pane buffers** (damaged cells get fresh pane ids — otherwise the shader dedups cracked cells into the intact pane and they never render through the shell). **Shipped as**: `VoxelDamageSystem` (ShotResolved → COW → `VoxelDamageMath.Puncture` → `VoxelChunkManager.UploadVoxelEdits`) + `VoxelCollisionWorld.TryGetVolume` + `ResetAllDamage`. Cracked-halo (134) pulled forward from P1 — a hole in a transparent mold is otherwise invisible. **Per-building chunks at P0** — P2 added the merged-buffer path (`bufferOffset + localIndex` writes into `mergedVoxelBuffer`), so sector-baked placements no longer skip. Verified offline + in playtest: `dda_selftest` [7] drills the mold — 8 shots / 58 cells → clean through-hole out the back face; metal **retainer** (relocated to the cube's base, off the bore axis) untouched | Shoot the mold → hole appears, persists, next shot goes deeper; ~8 shots punch clean through |
| **P1** — dressing | Shard-burst particles fed by removal delta; cracked-halo material swap; tink/crash sound by count | Hole reads as "shattered glass," not "missing voxels" |
| **P2** — sector writes + pane semantics | ✅ **Sector-baked damage writes shipped + playtested**: `sectorVolumeLookup` maps volume id → (sector, `bufferOffset`, dims); `UploadVoxelEdits`/`RefreshChunkVoxels` write dirty spans at `bufferOffset + localIndex` into `mergedVoxelBuffer` + `paneBuffer` (damage pane ids in a `0x8000_0000+` band); `RegisterSectorPlacements` feeds the map — same volume ids, same COW. Playtest: free-fire voxel picks (PICK STAND/PICK AIM) → Vinny shot up a tenement storefront in sector mode — holes + crack halos + shards + SFX all live. Remaining: gid pane tagging in editor → cascade-release + blast radius over gid clusters | Whole storefront crumbles on blast; bullet still punctures |
| **P3** — sim hooks | Noise events → NPC reaction; LOS invalidation; damage map persistence; smash-and-grab order hook | Broken window = entry + alert + saved scar |

P0–P1 are a self-contained vertical slice: one building, one gun, one pane.

## Open Questions

- **Debris voxel vs. true air** — `glass_shard` material keeps collision
  realism (a crawlable sill); AIR is simpler. Prototype with AIR; revisit.
- **Cracked = permanent?** — does the halo material persist until pane
  failure, or decay? (Leaning permanent — scars are information.)
- **Shard budget** — cap particles per break & per frame at city scale
  (blast on a 40-cell storefront shouldn't spawn 40 cubes × N panes at once;
  likely distance-gated shard density).
- **Chain breaks** — should blast near a gid boundary break *adjacent* panes
  (sphere query over gid clusters)? Feels right; tune in P2.

## Authoring Contract (what assets must provide)

- Pane = 1-voxel glass (settled convention — already baked into generators).
- Pane clusters share a gid → taggable in editor via the existing
  group/attach machinery; generators emit pane gids automatically.
- Optional `reinforced` flag = pane that takes damage but never
  cascade-fails (bank glass, armored storefronts).

## Cross-References

- [`VOXEL_GLASS_TRANSPARENCY.md`](VOXEL_GLASS_TRANSPARENCY.md) — alpha model, 1-voxel convention, design boundary this resolves
- [`../core/PERFORMANCE_PATTERNS.md`](../core/PERFORMANCE_PATTERNS.md) — dirty-cell write + partial buffer upload
- [`../core/BUSINESS_TAXONOMY_ASSET_GENERATION.md`](../core/BUSINESS_TAXONOMY_ASSET_GENERATION.md) — generators emit pane gids per the authoring contract
- [`DYNAMIC_OBJECT_RENDERING_TIERS.md`](DYNAMIC_OBJECT_RENDERING_TIERS.md) — Tier 3 individual-mutation path
- `voxel_editor.html` — Q+click gid select / G-paint group assign (the tagging tools)
