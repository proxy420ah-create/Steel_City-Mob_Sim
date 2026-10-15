# Glass Shatter Mechanic — Prototype Proposal

**Status**: ✅ P0 + P1 + sector-write half of P2 implemented — bullet puncture, cracked halo, unified voxel debris (in-grid rain → flying-voxel transport → cross-volume handoff), synthesized SFX, damage reset, mold bench tooling live; damage writes reach baked-sector merged buffers. **Demolition bench** added: spherical `VoxelDamageMath.Blast` (per-material resistance, seeded falloff) + PICK BLAST/DETONATE on the Demolitions tab. Remaining: gid pane semantics (cascade/blast-radius-over-gid) and P3 sim hooks
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

- **Flying voxels** — removed cells re-spawn as individual cell-sized debris
  voxels (the voxel IS the particle): velocity = rayDir + spread cone +
  gravity + fade, CPU-integrated via `ShardPhysics.StepFlyer` — point
  occupancy per 0.2 m stride, no Unity colliders, no swept rays. A voxel that
  reaches a volume's open cell joins that volume's falling-cell rain —
  see *Debris vs. the Voxel World* below.
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

## Debris vs. the Voxel World — unified cellular debris (implemented)

`Assets/Scripts/Sim/ShardPhysics.cs` (pure functions; offline-tested in
`Tools/dda_selftest` sections [13]–[18]) + `VoxelDamageSystem`.

**The voxel IS the particle.** Doomed cells are never packaged into proxy
particles and never unpacked — every cell keeps its own material and position
through all three phases:

- **Phase 1 — in-grid rain** (`StepFalling`): detached island cells are marked
  `falling` where they stand and stay live grid cells (data moves with the
  cell — the batched upload IS the animation). Per tick gravity + drag
  integrate `vel`; per-axis accumulators spend whole cells on single-step
  moves into OPEN neighbors only — no tunneling at any blast speed. Static
  solids absorb that axis's momentum; falling siblings block (columns rain as
  trains). A cell that can't move, rests on static support, and is under
  `cellLandSpeed` becomes rubble → `slumpQueue`. `SeedFalling` gives new
  islands a radial impulse (`cellImpulse`, decaying to zero at 2× blast
  radius) + deterministic hash jitter — rim cells are thrown, far ones drop.
- **Phase 2 — flying voxel transport** (`StepFlyer`): a cell that steps out of
  its volume's bounds — or was blast-ejected — becomes a world-space flying
  voxel (same cell, no rotation, no mass bundle). It moves in `FlyStepLen`
  (0.2 m) axis strides, each candidate position point-sampled through
  `VoxelCollisionWorld.SamplePoint` → `SolidVolumeSet.SamplePoint` (the same
  32 m XZ broadphase as the raycaster — one grid cell → few candidates →
  AABB reject → cell read). A point query is exact at sub-cell strides, so
  a swarm of thousands never pays for swept rays — the physics-sweep
  mitigation. Solids absorb that axis's momentum (no tunneling at any speed);
  `shardGroundY` + terrain solids are the floor.
- **Phase 3 — cross-volume handoff** (`TryEnterVolume`): a flyer stepping into
  a volume's AIR cell calls `EnsureDamageState` — **a pristine neighboring
  business flips into damaged state on first arrival** — then the cell resumes
  as a falling cell in THAT grid: velocity remapped to the target's cell axes,
  its voxel written immediately (falling cells render at their index). Debris
  can literally fly through a neighbor's window and rain onto the lobby floor.
  A 0.35 s re-entry cooldown on a flyer's source volume stops doorway
  ping-pong.
- **Emission** — blast cells re-spawn as flyers at their own world positions
  (`EmitFlyersRadial`: radial velocity + deterministic per-cell hash jitter,
  per shock-front wavefront); puncture spall flies along the shot direction
  (`EmitFlyersShot`). When a cavity exceeds `maxFlyingCells` (8192) emission
  stride-samples by cell hash — uniform coverage, deterministic, no
  truncation.
- **Rendering** — `flyingDraw` (a plain list refilled each frame) batches into
  PathDebugRenderer's quantized-color instanced-cube pipeline — no per-entity
  string keys. `MaxInstances` raised to 8192 for swarm-size color buckets.
- **Landing policies** — open cell → falling → rubble → slump; solid surface
  → absorbed, fades at `tEnd`; terrain → absorbed, fades (not writable);
  `rubbleDeposit=false` → landed cells vanish. A flyer evicted past the pool
  cap simply never spawns (flight is presentation, not sim state).
- **Angle-of-repose slump** — unchanged: `PumpRubbleSlump` (4096 moves/frame)
  relaxes landed cells — unsupported cells fall, supported cells slide one
  step down a lateral-down neighbor (45° repose, hash-rotated direction);
  only rubble-set cells ever move, converging to natural piles.
- **Removed** — bundled shard structs, `DepositHeap` pours, bucket clustering
  (`BuildBuckets`/`EmitClusterRange`), string-keyed shard cubes, pour budgets,
  `planBusyUntil` ordering. `DepositHeap`/`BuildBuckets`/`Step` remain as
  library functions — selftest [13]–[15] still cover them.
- **Cost model** — ~8k flyers × ~2–6 point queries/frame ≈ 2–5 ms during
  swarm flight (~1–2 s); `maxFlyingCells` bounds it. `EnsureDamageState`
  clones a pristine target's full array on first arrival — a one-time few-ms
  hitch per newly-struck neighbor.
- **Toggles** (Demolitions tab → Damage &amp; debris): `debris collides with voxel world`,
  `debris settles into volume (rubble · N cells)`, `cell impulse` slider.
- **Debris test lot** — `block_48` ("[DEBRIS TESTBED]", next to the glass
  monolith): `debris_test_lot.stasset` (192×120×192) = a full-footprint asphalt
  pad every pile is guaranteed to land on + three distinct-material masses —
  a 72³ red-brick cube, a thin stone wall, a 30³ light-wood cube — so rubble
  color shows provenance at a glance. `blast_target*` attachment points mark
  deterministic aim centers. `Tools/gen_debris_lot.py` regenerates it; same
  caveat as block_47 — hand-maintained in `city_layout.json`, a
  `generate_city_layout.py` regen reverts it.
- **Known limits** — debris enters volumes through openings only (no impact
  damage through intact walls — a future tier); terrain landings fade (no
  persistent litter pool yet); rubble is not persisted (same as all damage —
  baking the [BOMBED] state is separate, open work); cells can overlap a
  falling sibling for a tick during cross-volume handoff races.

### Damage-gradient char tint — "damage" as a voxel field, not new materials (implemented)

Blast wounds now **scorch their rims**: a 4-bit damage nibble baked into the
voxel word's **runtime-only bits 16-19** (`VoxelDamageMath.DamageShift/Mask`).
`0` clean · `1-7` char (darken toward the wound) · `8-15` glint (lighten —
reserved for crack webbing).

- **Why bits 16+**: the `.stasset` payload is uint16 — bits 0-8 material,
  9-15 reserved for shape/rotation (unused today: all 36 assets scanned have
  zero upper bits; `VxShape`/`VxRotation` are dead helpers). The runtime
  buffer is uint32, so bits 16-19 are *unreachable by the format* — tint can
  never collide with authored data and never serializes back into an asset
  (damage is runtime state by definition).
- **The model — exposure, not adjacency** (`VoxelDamageMath.ApplyDamageExposure`,
  selftest `[22]`): every solid cell within `radius + charReach` of the
  blast evaluates its own line of sight —
  `level = round(7 · distFactor · absorb^solids)` where
  `distFactor = 1 − charRadial·smoothstep(dist/range)` (S-curve: near-max
  hold through the doom zone, steep taper at the rim — "stronger from both
  ends", not a linear ramp) and `solids` counts intervening voxels along the
  straight blast→cell ray (`MarchSolidCount`, Beer-Lambert at `charAbsorb`
  per cell — **geometry only, never material resistance**). Three properties
  fall out for free: blast-**facing** surfaces char while shadowed sides stay
  clean; depth into material decays the same way (each covering cell
  absorbs); and **air transmits** — a blown window opening delivers scorch
  to the sill it reveals, even when nothing near the sill was doomed
  (selftest (b) proves through > shielded at equal distance). Doom-adjacency
  is implied, not required: cavity air lets rim cells see 0 solids → they
  char hardest. Max-merge: overlapping blasts only deepen.
- **Bullet decals use a different model** — `ApplyImpactTint` keeps the
  wound-adjacency BFS (`ApplyDamageGradient`): a contact mark is a pit +
  halo around the impact *cell*, not radiated energy. Both write the same
  nibble; the shader can't tell them apart.
- **Whole-lifecycle carry** — detached cells are tinted before `SeedFalling`
  snapshots `vox`, so rain carries char; ejected flyers get a hash-dithered
  `debrisChar` nibble (pair-anchored hash → a domino scorches uniformly) that
  `AddFlyer` folds into the draw color; `fly.vox` handoff writes the tinted
  word into rubble. Grid → rain → flyer → rubble: one continuous material
  history — a charred brick reads charred in flight *and* in the pile.
- **Shader cost** — one mask+shift after the palette/chunk-tint lookup in both
  raymarchers (`1-7` → multiply by `1 - lvl/7 · _DamageChar`; `8-15` → lerp to
  white by `_DamageGlint`; globals pushed per-frame). Applied to `baseColor`
  before the glass gate → a glinted pane reads frosted for free.
- **Knobs** (Demolitions → Damage &amp; debris): `damage tint`, `char reach` 4 (cells past
  the blast radius the scorch can still paint), `char strength` 0.65,
  `S-curve radial` 0.9 (distance-falloff strength; 0 = pure absorption
  shadowing), `char absorb/cell` 0.62 (transmission per intervening solid),
  `debris char` 0.6; shot debris emits glint instead of char.
- **Relation to cracked material 134** — the glint levels (8-15) now have a
  live implementation behind the **`glass glint` HUD toggle** (`VoxelDamageSystem.glassGlint`,
  default off): `TouchGlass` survivors keep their own glass material and write
  the fracture as glint 10-13 (hash-varied, max-merged — re-shots only
  brighten). The material slot is freed and pane bookkeeping needs no change —
  `UploadVoxelEdits` keys fresh damage-pane ids off the dirty cell's *current*
  material, so glinted survivors (still material 112) dedup as their own
  frosted surface. Material 134 remains the default until the speckled-glint
  scar wins the side-by-side eyeball; then `crackedMaterial` becomes fallback
  and 134 can retire.

### Domino entities — same-material pairing (implemented)

Detached same-class cells can fall as **2-cell pieces** instead of singles —
the "brick vs. broken brick" read. `ShardPhysics.PairPartner` partitions the
grid into 2×2 tiles; each tile hashes a pair axis (x or z) and whether it
pairs at all (`pairChance`, default 1.0 = every eligible tile). `SeedFalling`
preselects the pairs during the detonation sweep and registers **one shared
`FallCell` entity under both dict indices** — the pair is atomic by
construction (shared `vel`/`acc`), not two cells with correlated velocity.
`StepFalling`'s domino path steps both members as a unit: both targets free →
one move; a falling sibling blocking either → wait; static blocking both →
absorb the axis; static blocking ONE → **the piece fractures** — stuck member
keeps support, free member keeps the shared momentum and flies on. Landing
deposits both cells as adjacent rubble (a whole brick in the pile). A member
escaping bounds leaves as a lone flyer; a head destroyed externally is
adopted — the member converts to a single (no floating-voxel orphans, bug
`runtime/DOMINO_ORPHAN_FLOATING_VOXEL.md`). Pairing requires the same
resistance class — material seams split pairs, which is desirable. Flyer-phase
pairs stay velocity-glued (no entities in the transport phase). Coverage
report on the MatRes last-blast line (`dominoes N · paired cells M`);
selftested in `[19]`/`[20]` including 100%-pairing and split/fracture cases.

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
