# Combat Performance Review — end-to-end pass (pathing · targeting · resolving · timing · logic)

**Date**: October 29, 2026
**Status**: ✅ Batch A+B landed (behavior-identical, proof harness in-repo) · 📋 Backlog needs decisions (behavior-affecting)
**Scope**: `CombatController`, `CoverSensor`/`CoverField`, `VisionSensor`, `ProjectileSystem`, `WeaponMount`, `ArmAimSolver`,
`VoxelCharacter`/`VoxelCharacterAnimator`, `SfxWaves`, `SimClock` (≈11.6k lines)
**Mandate**: no change to fundamental operation — only cost, reuse, and consistency. Everything shipped here is provably equivalent.
**Method / honesty note**: static read of every per-frame path + an offline equivalence-and-benchmark harness
(`Tools/anim_selftest/run.sh`). **No in-Unity Profiler capture was possible from this session** — absolute ms below are
offline micro-benchmarks, not frame-time truth. §6 lists the Profiler markers to confirm the ranking.

---

## 1. Headline

The expensive *infrastructure* is already good — it was built right:

| Already optimal (leave alone) | Evidence |
|---|---|
| World raycast | grid-broadphase `SolidVolumeSet` + `VoxelDda`, allocation-free, ~0.43 µs/ray @ 800 buildings |
| Walkability / edges | shared world-fixed `WalkLayer` cache — each world cell probed once ever, dirtied only by voxel writes; every `CoverField` window is a copy |
| Cover field scratch | zones/ranking/fills use pooled lists; floods stamped to `BuildStamp` (map never outlives its channels) |
| Event-driven decisions | dice/beat/seam run on `SimClock` edges, not per-frame polling; `Changed` bus is a dirty-flag, not a rebuild |
| Component lookups | cached at Awake/first use (one stray per-frame `GetComponent` — fixed) |

What was actually costing: **(a) the CPU pose evaluator recomputed + reallocated the same FK chain redundantly**, **(b) a handful of
per-frame debug allocations nobody noticed**, **(c) cadence/invalidation choices** (probe every frame, rescan-invalidates-floods).
(a)+(b) are fixed. (c) is behavior-affecting → backlog.

## 2. Shipped (Batch A + B) — all behavior-identical

| # | Change | Why it mattered | Gain |
|---|---|---|---|
| B1 | **Single-evaluation pose API** — `VoxelCharacterAnimator.PreparePose / ApplyPose / PoseRotation`; `PosePoint` is now a thin wrapper | A group's chain depends on (gid, pose) not the point. `PosePoint` re-ran `ComputeGroupRotation` (~20 small allocs: `float[3,3]` matrices + chain arrays) **per point**; eye-anchor/weld ran the identical chain a **second** time for orientation | see §3 |
| B2 | `SampleSilhouetteWorld` evaluates once **per group** (≈10) not per sample (≈40), scratch reused | the exposure probe — every armed fighter, every frame | probe pose ~3× faster, ~4–5× less garbage |
| B3 | `VoxelCharacter.PoseToWorld` (eye anchor + joint queries) and `WeaponMount.LateUpdate` (muzzle weld) use B1 | each ran the same chain 2× (4× in a transition) per frame per fighter; `VisionSensor` + probe + weld = ≥3 per-frame consumers | ~2× faster, ~half the garbage |
| B4 | `GetWalkPose` cache key: `ToString("F4")+":"+ToString("F2")` (3 string allocs, **per posed point** in walking states) → raw-float compare | walking/AimWalk is the common seek state | removes 3 allocs × every posed point; cache stays valid (6000 checks, 0 stale) |
| A1 | `CombatController.LateUpdate` yaw monitor: `new List<string>(YawForce.Keys)` every frame per fighter → pooled scratch | allocated every frame **even at Idle** | 1 alloc/frame/fighter → 0 |
| A2 | `DiceTraceTick` edge-detects on raw enums/bools/flag-bitmask; strings built **only on change** | interpolated state/flags/ctx strings + enum `ToString` boxing **every frame** per fighter (`diceTrace` defaults on) | ~4–6 allocs/frame/fighter → 0; trace output byte-identical |
| A3 | `VisionSensor.EyeAnchorDesc` built on read (HUD) not each `ResolveEye` | string alloc per frame per fighter | 1 alloc/frame/fighter → 0 |
| A4 | `OnSfxHeard`: distance filter first; shooter actor via `WeaponMount.Character` (new, cached) not `GetComponentInParent` per listener | one gunshot cost every listener a hierarchy walk — an N-listeners × shots/s term for group fights | O(N) walks/shot → O(in-earshot) cached reads |
| A5 | `CoverField.BuildDistMap` reuses its open-set + seed array (`PriorityQueue.Clear()`, added to the project heap) | fresh heap + `new[]{seed}` per flood, regrowing through thousands of enqueues | removes the flood's per-build heap/array garbage; pop order unchanged |
| A6 | `TickExposureFire` per-frame `vox.GetComponent<ArmAimSolver>()` → cached `solver` | stray per-frame lookup | trivial |

## 3. Measured (offline, `Tools/anim_selftest/run.sh`)

Equivalence: **156,089 checks vs the legacy algorithm, 0 differences, bit-identical** (all states, transitions, ducks, gids, random
IK-free param sets; `Quaternion.Slerp` is an engine intrinsic so blended *rotation* rows are skipped and counted — blended *points*
and non-blended rotations are compared). Benchmark (per call, JIT-warm):

| Scenario | 40-sample probe pose: legacy → new | weld / eye (point+orientation): legacy → new |
|---|---|---|
| Aiming (stable) | 40.7 µs / 26.2 KB → 15.4 µs / 6.5 KB (**2.6×**, 4.0× less garbage) | 4.4 µs / 2.3 KB → 1.9 µs / 1.1 KB (**2.3×**) |
| Aim-walk (gait) | 48.8 µs / 31.8 KB → 13.6 µs / 6.5 KB (**3.6×**, 4.9×) | 3.6 µs / 2.4 KB → 1.9 µs / 1.1 KB (**1.9×**) |
| Aim-walk + transition | 91.4 µs / 57.9 KB → 27.8 µs / 13.1 KB (**3.3×**, 4.4×) | point+chain only (Slerp skipped offline) — unchanged cost, rotation half removed in-engine |

**Read this correctly**: absolute CPU per fighter was small (~0.1 ms/frame). The real pre-fix problem was **garbage**: an armed
fighter in a gait generated ~35–60 KB per probe **per frame** (≈2–3.5 MB/s/fighter at 60 fps ⇒ tens of MB/s at group scale ⇒ GC
hitches). That is what B1–B4 cut ~4–5×.

## 4. Scaling map for group fighting (what grows how)

| Cost | Scales | Notes |
|---|---|---|
| Exposure probe | **O(N)** (each fighter probes one `ThreatCharacter`) | ~40 samples + ≤80 rays — cheap now. **Multi-target selection will make this O(N·K)** — design the candidate set with a spatial query, not a scene scan |
| `CoverSensor` field scan | **O(N)** but the biggest single item | each fighter owns a 64×64 field, rescans every 0.4 m moved **or** 0.4 s, each rescan **bumps `BuildStamp` → invalidates all 3 floods**. The geometry layer is already shared (`WalkLayer`); the *threat-dependent* layer is per actor |
| Distance-map floods | O(N) × full-field Dijkstra | rebuilt per seed-cell change **and** per scan (see backlog #1) |
| Hearing | O(listeners) per shot | now allocation-free and early-outs on distance |
| `VisionSensor` | O(N) × targets | `targets` is a per-sensor list (1 entry today) — O(N²) if it ever auto-fills with every body |
| Projectiles | O(live) × 1 grid raycast/frame | fine; `Debug.Log` per resolve is the hidden cost in-Editor |

No O(N²) term exists today. The three places one could appear are all *future* (multi-target, vision target auto-fill, shared threat map).

## 5. Backlog — behavior-affecting, needs your call

Ranked by expected payoff at group scale. Each lists what changes observably.

| # | Item | Expected gain | Observable change / risk | Recommendation |
|---|---|---|---|---|
| 1 | **Rescan only when inputs changed** — ✅ **LANDED (timer path)**: `CoverSensor` skips the 0.4 s timer rescan when (owner pos < 1 mm, threat, `WalkLayer.Version` edit stamp, tunables) are unchanged AND no vehicle is moving AND no debug viz is active. Movement-triggered and forced (`Refresh(true)`) scans are untouched | removes the stationary/seamed-fighter rescans + the `BuildStamp++` that discarded every cached flood | Output identical when the key matches (moving cars, missing `WalkLayer`, or any viz toggle fall back to the legacy cadence). **Walking fighters still rescan every 0.4 m** — that is the remaining cost, see §8 | Re-capture with the new markers (§8) to see what's left |
| 2 | **Probe cadence** — `visInterval` is `0` (every frame) in combat but `max(visInterval, 0.1)` in `TickWatch`; `SimClock.Now` only changes per 0.08 s step | up to ~5× fewer probes | Fire/aim latency quantizes to 12.5 Hz; changes feel. **No code needed to trial: set `visInterval` = 0.08 in the Combat tab and A/B** | A/B first, then decide a default |
| 3 | **Timing consistency** — `CoverSensor` scans on `Time.unscaledTime` (runs under F8, wall-clock); `ProjectileSystem` steps on `Time.deltaTime`; aim sway on `Time.time` | not perf — determinism/replay | Scans under freeze waste CPU and bump `BuildStamp`; sway/projectile timing varies with frame rate so a seeded fight isn't frame-rate-identical. Projectiles on the 0.08 s step would make tracers visibly choppy | Move scan cadence to `SimClock` (safe); leave projectile/sway integration on frame time but **seed sway from sim ticks** if replay matters |
| 4 | **Matrix representation** — `float[3,3]` + `new TransformEntry[]` inside `ComputeGroupRotation` | removes the *remaining* ~20 allocs per chain eval | Core of CPU↔GPU↔JS pose parity — high blast radius. Harness (`anim_selftest`) is now the guard rail: any rewrite must stay bit-identical | Only if Profiler still shows `ComputeGroupRotation` after B1; use the harness |
| 5 | Per-shot/impact/dice `Debug.Log` (large interpolated strings, Editor stack capture) | modest in a firefight, real at group scale | Console diagnostics change; user relies on them | Add one `verboseCombatLog` toggle (default on = no change), gate the heavy ones |
| 6 | `DeriveCandidates` walks all 4096 cells per scan for **display-only** `candidates`/`candEyes` | small | HUD/V-mode candidate beams would need lazy build | Make lazy behind the viz flags |
| 7 | `ConsumersWantData()` does `FindFirstObjectByType<VoxelChunkManager>()` every frame while it's null | trivial (only when absent) | none | Cache the miss |
| 8 | **Locate the per-frame failing `GetComponent`** behind `GetComponentNullErrorMessage` (63 KB/frame in the Editor) | Editor-only garbage, but marks a repeated failed lookup | none | Profiler: GC Alloc column → call stack on that row; likely `VoxelVehicle.IsMoving`'s `GetComponent<VehicleAgent>()` on agent-less cars — cache a `agentChecked` bool |

## 6. Confirm with the Profiler (next step — I can't capture this from the CLI)

Capture a 10 s two-fighter firefight (Deep Profile off; add `Profiler.BeginSample` if you want finer):
1. **GC.Alloc per frame** — expect a large drop (probe/pose/trace/yaw allocs were the bulk).
2. `CoverSensor.Refresh`/`CoverField.Build` + `BuildDistMap` total ms — if these top the list, backlog #1 is the next move.
3. `VoxelCharacterAnimator.ComputeGroupRotation` — if still hot, backlog #4.
4. `TryGetVisibleVoxels` self-time — decides backlog #2.

## 7. Guard rails added

- `Tools/anim_selftest/run.sh` — offline bit-exactness proof + micro-benchmark for the pose API (same bundled-Roslyn trick as
  `Tools/dda_selftest`). **Run it after touching `VoxelCharacterAnimator`/pose math.**
- Pose API contract: `PosePoint` ≡ `ApplyPose(PreparePose(...))`; use `PreparePose` once per group when posing many points or
  needing orientation. Don't call `ComputeGroupRotation` in a loop.

## 8. Profiler capture #1 — reading + what changed because of it

Capture (Editor, 2 fighters): frame median 34 ms (min 25, **max 85**), CPU-bound **and** GPU-bound vs the 8.3 ms / 120 fps target.
Scripts ≈ 21–29 ms of the frame; single-frame: CPU 25.4 ms, GPU 31.1 ms, `WaitForGPU` ~6–30 ms.

| Marker (self time) | Reading |
|---|---|
| `CoverSensor` (top script marker on the longest frame **and** across the selection, above `CombatController`) | **Confirms backlog #1** — with Deep Profile off, all of `Update → Refresh → Scan → CoverField.Build → DeriveCandidates → Redraw` is attributed to this one bar. The 85 ms outlier is a scan spike |
| `TextGenerator.GenerateText` 7.4–7.8 ms + `Prepare` 3.1 ms + `HUD.DrawCombatTab` 2.3 ms | **~10–13 ms/frame is the IMGUI debug HUD**, not combat. (Pitfall #12: `OnGUI` fires several times per frame, so text layout multiplies.) Profile with the HUD hidden for combat truth |
| `GC.Alloc` 6.7 ms on the longest frame | Still visible — capture does not say whether it predates Batch A/B. Re-capture and compare; the remaining allocators are the scan (`BuildZones`, candidates) and HUD strings |
| `GfxDeviceD3D12.WaitForGPU` 6–31 ms, GPU 31 ms, 3.2 GB buffers, 459 draw calls | **The raymarch renderer is GPU-bound** — independent of combat. Combat optimization cannot reach 120 fps while the GPU frame is 31 ms |
| `EditorLoop` 36 ms, `Profiler.ParseThreadData` 26 ms | Editor/Profiler overhead — absent in a player build |

**Landed because of this**: (1) the scan-reuse gate (backlog #1, timer path), (2) Profiler samples so the next capture splits the
`CoverSensor` bar: `CoverSensor.Scan` › `Scan.Query` · `Scan.Classify` · `CoverField.Build` · `Scan.Vehicles` · `Scan.Candidates`,
plus `CoverSensor.Redraw`, `CoverSensor.Probe` (the exposure probe), `CoverField.BuildDistMap`.

**Next capture protocol**: HUD hidden (`` ` `` / `O`), Development Build or Editor with Profiler open, 10 s firefight, then read the
hierarchy under `CoverSensor.Update` — whichever of Query/Classify/Build/Candidates dominates is the next target (Build's
per-cell phase-2 ray/sample pass and `DeriveCandidates`' 4096-cell walk are the prime suspects).

## 9. Profiler capture #2 (markers live, HUD hierarchy) — reading + fix

Frame: CPU 25.6 ms, scripts 19.8 ms. **`CoverSensor.Update` = 12.9 ms = 50.3 % of the frame** (2 calls, 1 scan that frame).

| Row | Total | Self | GC | Reading |
|---|---|---|---|---|
| `CoverSensor.Scan` | 12.62 ms | 0.04 | 179.8 KB | the whole cost |
| ↳ `Scan.Classify` | — | **~9.0 ms** (top self-time marker in the 33 ms frame) | — | **71 % of the scan.** Per-wall-column loop; its dominant term is `LocalSupport` — a ground raycast per distinct 0.1 m stand-spot cell, cache **cleared every scan** |
| ↳ `CoverField.Build` | 2.20 ms | 2.08 | 178.4 KB | the 178 KB is almost certainly a **one-time `WalkLayer` block allocation** (a 64×64-cell block is ~124 KB) as the fighter entered a new 16 m tile — not steady state. Confirm: it should read ~0 KB in later frames in the same area |
| `CoverField.BuildDistMap` | 1.16 ms | 1.16 | 0 B | flood — allocation-free now ✅ |
| `CoverSensor.Probe` | 0.82 ms | 0.81 | 22.6 KB | cheap in time; the 22.6 KB is the remaining `float[3,3]` chain garbage (backlog #4) |
| `GetComponentNullErrorMessage` | — | — | 63.3 KB | **Editor-only** allocation: a `GetComponent<T>()` that returns null builds an error string. Some per-frame lookup is failing repeatedly (suspects: `VoxelVehicle.IsMoving`'s `GetComponent<VehicleAgent>()` on cars without an agent, other `if (x == null) x = GetComponent<>()` caches for components that legitimately don't exist). **Not yet located** — see backlog #8 |

**HUD A/B (your screenshots 4 vs 5)**: HUD on = 646 draw calls / 645 SetPass / 161 vertex uploads (58 KB); HUD off = 370 / 369 / 3 (1 KB).
The IMGUI HUD **doubles the draw calls and is the reason GPU time swings 31 ms → 12 ms**. With the HUD off the frame is CPU-bound,
and the CPU is half cover scan.

**Landed because of this**
1. **Persistent shared support cache** (`CoverSensor.supportCache`, static): ground-at-stand-spot is world data, not per-scan data. Shared by every fighter, kept across scans, evicted **per cell** from a new `WalkLayer` edit log (a bullet hole evicts a few cells; rotated/huge edits drop the lot; ring overflow or `InvalidateAll` = drop the lot). NaN = "probed, no ground" so each fighter's fallback is applied at read time (never baked in). Expected: Classify falls from ~9 ms to the remaining per-column bookkeeping once a region is warm.
2. **`WalkLayer` change log** — `Version` now has `ChangeAt(v)` / `ChangedSince(v, window)`: *where* each edit landed, 4096-entry ring.
3. **Scan gate is now region-aware** — rescan is skipped unless an edit intersected the scan window (was: global version). Moving traffic only defeats it when a car's 1.5 s sweep can reach the window (`VoxelVehicle.MovingNear`, was: any car anywhere).
4. **HUD line** now shows `N walls · valid/tested cells · P ground probes` — `P` is the number of raycasts that scan actually fired. **First scan in a new area: thousands. Warm: ~0.** That readout is the proof.

**Known semantic note**: the support key is (x, z) on a 0.1 m grid, as before — it ignores Y. In single-level terrain this is identical to the old per-scan cache; in genuinely stacked geometry (bridge over road) the first probe at an (x,z) now wins until evicted instead of until the scan ends. If that ever shows up, add quantized Y to `SupportKey`.

**Next capture**: same protocol, but run 20 s and read `Scan.Classify` Self on a frame *after* the first scan in the area, and glance at the HUD `ground probes` number. If Classify is still > 2 ms warm, the per-column loop itself (not the raycast) is the cost and the answer is a world-fixed wall-column classification layer (cache `WallMark` per column per world version, like `WalkLayer`) — larger change, wait for the data.

## 10. Capture #3 reading + Option A landed: world-fixed wall-column cache

**HUD**: Vinny 7,952 walls · **0 ground probes**; Tan 11,928 walls · **0 ground probes** — the support cache serves everything warm.
**Capture**: `Scan.Classify` 9.0 → **1.74 ms, 0 B**; `Scan.Query` 0.94 ms; `Vehicles` 0.16; `Candidates` 0.04. The remaining cost scaled with
wall-column COUNT (≈170 ns/column over 8–12k columns): the loop and the column emission, not raycasts.

**Option A (landed)** — classify once per world, not once per scan:
- `SolidVolumeSet.QueryCoverWallSets` — same traversal as `QueryCoverWalls`, but each volume's emitted columns come from a per-volume
  cache (`VolumeWalls`) keyed `(minSpan, openCell)`. It hangs off the `Volume` object, and `Register()` makes a new `Volume` on every
  replace/damage swap, so the cache dies exactly when `colBase/colSpan` do. LRU-trimmed at 400k columns (~40 MB).
- `CoverWallClassifier` (new, pure C#) — per-volume cached classification (`sy`, class, required duck, drop flag, class counts, deepest-wall
  aggregate) keyed on the body spec (`hStand/hShallow/hDeep/standoff/wallFloor/depthMargin`) and **validated against `WalkLayer`'s edit log
  over the volume's padded AABB** — an edit that lands in a volume reclassifies only that volume. Per scan it only copies cached entries
  into `WallMark`s and adds the genuinely per-scan terms: threat facing, and (rare) the owner's support fallback for columns with no ground.
- `CoverSensor.useWallCache` (default on) — off = the original loop (`ClassifyLegacy`, kept verbatim) for A/B.
- **Correctness fix found on the way**: `RegisterVolume` now also invalidates the *old* footprint on a replace (a car / damage swap vacated
  cells that were never re-probed). Every edit-log consumer needed it; it also repairs pre-existing `WalkLayer` staleness.

**Proof** (`Tools/cover_selftest/run.sh`, offline): 1,500 randomized iterations — random volumes/yaw/body specs/owner support/threat,
interleaved with voxel edits and damage swaps — marks, per-volume aggregates and class counts **bit-identical** to the legacy path
(6,000 checks, 0 fails; 5,521 cache hits / 1,055 rebuilds so the HIT path under edits is exercised hard). Warm scan, 35–61k columns:
**3.1–3.8× faster** (e.g. 15.1 → 4.0 ms) with the ground cache already warm on both sides. In-Engine numbers will be smaller (fewer columns,
~10k) — expect `Scan.Query + Scan.Classify` ≈ 2.7 ms → ≈ 0.8–1 ms; confirm in the Profiler.

**What this does NOT fix**: `CoverField.Build` still consumes ~10k walls per scan (splat per column) — merging columns (Option B) is the next
lever for Build itself, but it changes zone painting and needs a playtest.
