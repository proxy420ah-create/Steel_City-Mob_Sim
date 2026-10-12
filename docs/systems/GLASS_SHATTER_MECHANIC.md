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
  velocity = rayDir + spread cone + gravity + fade. GPU-driven, no colliders.
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
