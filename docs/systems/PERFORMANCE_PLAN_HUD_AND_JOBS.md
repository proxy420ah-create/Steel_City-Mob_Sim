# Performance Plan — Debug HUD (IMGUI) + Job System

**Date**: October 29, 2026
**Status**: 📝 PLAN — nothing here is implemented yet. Part 1 is evidence-driven and ready to execute; Part 2 is gated on scale data.
**Siblings**: `COMBAT_PERFORMANCE_REVIEW.md` (what was found/fixed in the sim), `UI_DEVELOPMENT_PITFALLS.md` (#12/#13 — the HUD lessons this builds on), `PERFORMANCE_PATTERNS.md`
**Hard constraint (from the owner)**: **wall-cover calculation is not changing** — no column merging, no altered zone painting. Everything below preserves cover output bit-for-bit (the `Tools/*_selftest` harnesses are the guard rail). The review's "Option B" is therefore **out of scope**.

---

# Part 1 — The HUD is now the single biggest CPU consumer

## 1.1 What the profile says (Editor, HUD on, 23.35 ms frame)

| Row | Total | Time | GC | Reading |
|---|---|---|---|---|
| `DrawScreenSpaceUI` → `GUI.Repaint` | **53.6 %** | **12.5 ms** | 0.8 MB | half the frame is IMGUI |
| ↳ `CoverMetricsPanel.OnGUI` | **32.6 %** | **7.63 ms** | 0.6 MB | the biggest single HUD cost — **not** the debug window |
| ↳ `DebugHUDManager.OnGUI` | 14.7 % | 3.43 ms | 207 KB | self only 0.74 ms — the rest is native playback |
| ↳ `GUIUtility.EndGUI` | 5.6 % | 1.31 ms | 36 KB | native flush of everything drawn |
| Earlier capture | | `TextGenerator.GenerateText` 7.4 ms + `Prepare` 3.1 ms | | the same cost seen from the other side |
| A/B (HUD on → off) | | | | draw calls **646 → 370**, SetPass 645 → 369, vertex upload **58 KB → 1 KB**, GPU frame 31 → 12 ms |

**This is already documented history, and it explains why the obvious fix didn't work**: `UI_DEVELOPMENT_PITFALLS.md` #13 (update) measured a
`Stopwatch` around `OnGUI` at ~1 ms while the frame ran 22 ms — *"the real wall was native, not managed: mesh/text generation billed to the
EditorLoop, scaling with drawn content, invisible to C# instrumentation."* The fix then was `hudLocked` (every tab = ONE cached label, rebuilt ~4 Hz).

**What `hudLocked` did not reach — `CoverMetricsPanel`** (`Enabled = true` by default, F5 toggles):
- It already does the *right managed things*: text rebuilt at 5 Hz behind a dirty flag, `GUIContent`/height cached, Repaint-only.
- But it hands IMGUI **one very large rich-text string** (`richText = true`, ~dozens of lines, flow-chart nodes with colour tags). IMGUI regenerates the
  glyph mesh for that whole string on **every Repaint**, regardless of how rarely the string changes — caching the *string* does not cache the *mesh*.
  That is the 7.6 ms / 0.6 MB: native text generation, every frame, for text that changes 5×/s.
- `DebugHUDManager` still runs `DrawModeTicker()` and `DrawCombatBubbles()` (in-world fighter bubbles) **even when the window is hidden**, on
  every IMGUI event.

**Root cause (one sentence)**: IMGUI is an immediate-mode renderer — it re-lays-out and re-meshes all text each Repaint — and we're using it for
*retained* data (readouts that change 4–5×/s). The fix is to stop paying per-frame for per-second data.

### 1.1b Stress capture — the HUD's *event* cost is even bigger than its paint cost

Mid-fight, HUD open, vehicle pathing active: **81.65 ms frame**, `PreUpdate.IMGUISendQueuedEvents` = **47.2 % (~38 ms)**, scripts 24.7 %.
That marker is the queued *input* events (the Editor floods dozens of MouseMove/MouseDrag per frame) being replayed through **every** `OnGUI` —
not painting. Audit of the handlers: `CityMap3D`, `FollowCamera`, `CoverMetricsPanel`, `PossessController` already bail out cheaply, but
`DebugHUDManager.OnGUIInner` ran `InitStyles` + `DrawModeTicker` (GetComponent ×2, string building) + `DrawCombatBubbles` (GetComponent, WorldToScreenPoint,
rich strings, a GUILayout text layout per fighter) for **every** event *before* its mouse-event filter. Cost scales as events/frame × fighters in combat — which is
exactly why a fight makes it worse. **This reorders Part 1: H3 (event discipline) is now first, ahead of H1 (baking).**

**Landed immediately (low-risk)**: the MouseMove/MouseDrag early-out is hoisted to the top of `OnGUIInner` (same predicate as the old later check; nothing
layout-related is consumed on a skipped event, so GUILayout's sequential `GetNext()` group matching stays aligned); per-event `Stopwatch` allocations in the
two `OnGUI` wrappers replaced with `GetTimestamp()`. **Deliberately NOT done**: skipping ticker/bubbles on *other* non-Repaint events (KeyDown/MouseDown/…) — IMGUI
matches `GUILayout.BeginArea` groups by call order on non-Layout events, so skipping an earlier area misaligns the window's controls and breaks clicks. That needs H1/H2.

**Verify**: re-run the same stress scenario — `IMGUISendQueuedEvents` should collapse; if it doesn't, the remaining events are non-mouse (key/scroll/Used) and H1/H2 is the answer.

## 1.2 Goal and budget

| Mode | Target |
|---|---|
| HUD fully visible (everything on) | **≤ 1.0 ms/frame**, ≤ 20 KB GC/frame |
| HUD "locked" (default) | **≤ 0.3 ms/frame** |
| HUD hidden | ~0 (no IMGUI events processed for HUD) |
| Draw calls added by HUD | ≤ ~10 (was ~276) |

Acceptance is measured with the same A/B you ran: draw calls, SetPass, vertex-upload KB, and `DrawScreenSpaceUI` ms.

## 1.3 Plan

### H0 — Make the cost visible and switchable (safe, minutes of code, do first)
1. **One master performance switch** `HudPerf.Mode { Full, Locked, Hidden }` read by *every* IMGUI source (`DebugHUDManager`, `CoverMetricsPanel`, `PossessController.OnGUI`, `FollowCamera.OnGUI`, `CityMap3D.OnGUI`, bubbles, ticker). Today F5, `` ` ``/O, and `L` are three unrelated flags; profiling needs one.
2. `Profiler.BeginSample("HUD.<source>")` around each source's OnGUI so the Hierarchy attributes cost per panel without a stopwatch.
3. A one-line HUD-cost readout (already half-exists: `hudProfMsShown`) — but note it under-reports (native playback is invisible to it); label it "managed ms".

### H1 — Stop regenerating text every Repaint (the core fix; no visual change)
**Bake the text, draw a quad.** For every panel whose text changes at ≤ 10 Hz:
- Render the text **once per rebuild** into a small `RenderTexture` (draw the `GUI.Label` into the RT during a Repaint event with `RenderTexture.active` + `GUI.matrix`), then every other Repaint is `GUI.DrawTexture(rect, rt)` — one textured quad instead of N glyph quads + a text-generation pass.
- Shared helper `BakedPanel` (`Rebuild(string text, GUIStyle, width)`, `Draw(Rect)`), reused by `CoverMetricsPanel`, the locked `DebugHUDManager` tab label, the mode ticker, and the combat bubbles.
- Expected: `CoverMetricsPanel` 7.6 ms → ~0.1 ms steady-state (+ one bake at 5 Hz ≈ one old frame's cost amortized over 12 frames); GC 0.6 MB/frame → ~0 steady.
- Risks: font crispness at non-1× DPI (bake at `Screen.dpi`-aware scale); RT recreation on resize; rich-text colour parity (verify visually); RT memory (a few MB at most). Fallback if baked text looks soft: H2.

### H2 — Move always-on readouts to a retained UI (structural)
IMGUI stays for the **interactive tuning window only** (sliders/toggles — where immediate mode is genuinely convenient, shown on demand). Every *read-only, always-on* readout moves to a retained renderer that rebuilds its mesh **only when `.text` is assigned**:
- **Option A — TextMeshPro/uGUI** (`com.unity.ugui 2.0.0`, `Assets/TextMesh Pro` present, a screen-space-overlay canvas pattern already exists for the input blocker). Assigning text at 5 Hz → mesh rebuilt 5×/s; other frames = a cached, batched canvas draw.
- **Option B — UI Toolkit runtime** (Unity 6): GPU-batched, USS-styled; heavier to adopt, best long-term for a real game HUD.
- **Decision gate**: spike `CoverMetricsPanel` in BOTH, capture both, keep the winner. (My expectation: TMP wins on integration effort; UI Toolkit wins if the HUD becomes a shipped feature.)
- Also fixes the wasted-event cost: retained UI has no per-event OnGUI storm (Layout + Repaint + input ≈ 2–4×/frame, plus MouseMove floods in the Editor).

### H3 — Event discipline for what stays IMGUI
- **Hidden ⇒ truly off**: `DrawModeTicker`/`DrawCombatBubbles` early-out unless their own enable flag is set (today they run whenever the script is enabled).
- **Repaint-only unless hovered**: when `hudLocked` and the pointer isn't over a HUD rect, process *only* `EventType.Repaint` (skip Layout and every input event). Interactive widgets need Layout; a locked text label does not.
- Keep the existing MouseMove/MouseDrag early-out.
- Cap per-frame string building inside `Draw*Tab`s (interpolation in tab bodies allocates per event ×4): cache formatted strings behind the same dirty flag.

### H4 — Governance (so it doesn't regress)
- `UI_DEVELOPMENT_PITFALLS.md` **#14**: *"An always-on `GUI.Label` with a large string pays native text generation every Repaint no matter how rarely the string changes. Bake (RT) or retain (uGUI/UI Toolkit)."* + the capture protocol.
- A HUD-cost budget line in the Profiler checklist; a regression is any frame where `DrawScreenSpaceUI` > 1 ms with the HUD in default mode.

### 1.4 Order and verification
H0 → H1 (CoverMetricsPanel first, then locked tab label, ticker, bubbles) → capture → H3 → capture → H2 spike → decide. After each step: HUD on/off A/B (draw calls, SetPass, vertex KB, `DrawScreenSpaceUI` ms, GC). Visual check: text identical, F5/`L`/`` ` `` still toggle, click-through unchanged (the canvas blocker is untouched).

---

# Part 2 — Job system plan (for group-fight scale)

## 2.1 Honest framing

Measured today, the sim is **not** thread-bound: with two fighters the scripts are ~1–13 ms and the frame is dominated by rendering/GPU wait plus the (now-fixable) HUD. Threads would not have fixed any capture so far. Jobs earn their keep **when N grows** — and the places an O(N²) term can appear (multi-target selection, shared threat maps) are all *new code* that can be job-native from day one. So:

1. **Build the scale scene first** (a spawner: 4 / 8 / 16 / 32 fighters, one duel graph) and chart per-system ms vs N. *That* decides the order below; the ranking here is my prediction, not a measurement.
2. **New group logic is written job-first**; legacy per-fighter systems are converted only where the chart shows they scale badly.

What is already true: Burst 1.8.28, Collections 2.4.3 and Mathematics 1.3.2 are in `packages-lock.json` (transitive via URP); **no game code uses them**. The only threading today is load-time `Parallel.For` in `VoxelChunkManager` (line ~234) and `VoxelTerrainBuilder` (~212). Multithreaded rendering is on (`m_MTRendering: 1`); Graphics Jobs not confirmed (Player Settings). There are **no `.asmdef`s** — all game code is one `Assembly-CSharp`.

## 2.2 Preconditions — make data blittable (in this order)

A job can't touch managed objects: no classes, `string`, `List<>`, `float[,]`, `Dictionary`, `MonoBehaviour`, `Transform`, `Physics`, `AudioClip`. The conversion order, cheapest-and-most-reused first:

1. **World ray data** (unlocks probes, cover validation, pose-sample LOS, projectiles — everything raycasts):
   - Terrain `VoxelCollisionWorld.voxelGrid` is a `byte[]` → make it (or mirror it as) a **persistent `NativeArray<byte>`**; single copy preferred (budget check first: measure `gridW*gridH*gridD`). Writers (terrain chunk register, damage) stay main-thread and update the native array + the existing `WalkLayer.Invalidate` log.
   - Volumes: `Volume.voxels` is a shared per-asset `uint[]` → one persistent `NativeArray<uint>` per **unique asset buffer** (shared, as today) + a flat `NativeArray<VolumeHeader>` (offset, w/h/d, corner, vs, cos/sin yaw, wMin/wMax). Damage's copy-on-write produces a new buffer → new native buffer + header swap (same trigger as today's re-register).
   - The XZ broadphase `Dictionary<int,List<int>>` → **CSR** (`cellStart[]`, `cellCount[]`, `volumeIndex[]`) rebuilt on the same `gridDirty` trigger.
   - `VoxelDda.Raycast` is already a pure static → port to `[BurstCompile]` over native pointers/spans. Cross-check with `Tools/dda_selftest` (brute-force fuzz) — it already proves the algorithm; run it against the Burst build via an EditMode test.
2. **Pose parameters**: `AnimParamsData` (nested classes), `float[,]` matrices, `TransformEntry[]` chains are all managed. Flatten per-model constants into a blittable `PoseModel` struct (floats/ints; ~100 values) and per-fighter live state into `PoseState` (state, time, duck, gait, transition quad, tap, gun side, IK quats). Port `ComputeGroupRotation` to `Unity.Mathematics` (`float3x3`/`quaternion`) — **this also eliminates the remaining ~20 allocs per chain evaluation** (review backlog #4). Guard rail: extend `Tools/anim_selftest` so the Burst/Mathematics version is checked against the managed one (tolerance-documented if op order differs; bit-exact if I keep the same expression order).
3. **Cover field arrays**: `CoverField` is already flat primitive arrays (`float[]`, `int[]`, `byte[]`, `bool[]`) — the easy conversion (`NativeArray<T>` persistent per fighter, same lengths). Two real blockers:
   - `string volIdOf[]`, `CoverWall.volumeId`, `WallMark.volId` → **intern to `int` volume ids** with a shared id table (strings stay for debug display only). The cached wall sets (`VolumeWalls`) already give a stable per-volume identity to hang the id on.
   - `Walkable()/EdgeOpen()` lazily call world probes mid-flood → add a **pre-bake step** that fills every edge/ground/fit flag for the window (`WalkLayer.FillWindow` already bulk-fills ground/fit/square; edges are the lazy remainder) *before* the flood job runs, so the job reads flat arrays only.
4. **Priority queue**: the project heap (`PriorityQueue` in `Pathfinder.cs`, `List<(T,P)>`-backed) → a **`NativeList`-backed binary heap with identical sift/tie-break rules**, so pop order — and therefore every flood result — stays identical (the same invariant that made `Clear()` reuse safe).
5. **Ids/seeds**: per-fighter RNG seeds and sim-tick numbers passed in as plain values; no `UnityEngine.Random` inside jobs.

## 2.3 Candidate jobs (predicted order; the scale chart reorders it)

| # | Job | Shape | Needs | Predicted value |
|---|---|---|---|---|
| **1** | **Group targeting / threat selection** (new) | spatial hash (`NativeParallelMultiHashMap` over fighter cells) → per-fighter nearest-K candidate query → LOS-test the K | world ray data (1), positions SoA | **Highest** — this is where O(N²) would otherwise appear. Greenfield: no legacy to convert |
| 2 | **Exposure probes** | `IJobParallelFor` over (fighter × ~40 samples) → per-sample visible/muzzle-visible bits → ordered per-fighter reduce (centroid sums in fixed order) | world ray data, pose samples (job or main) | Real only at larger N: 32 fighters × 80 rays × ~0.5 µs ≈ 1.3 ms single-thread — modest; the *GC* was the real cost and is already cut |
| 3 | **Pose sample points** | `IJobParallelFor` over fighters → per-group chain once (as `PreparePose`) → sample world points | pose data (2) | Bundles with #2; kills the matrix garbage for good |
| 4 | **Cover field Build** | one job per fighter (independent output slices) | field arrays + wall list + world rays + walk pre-bake (3) | Largest *per-scan* item (2–5 ms warm) — but **only after the wall-cover algorithm stays untouched**: port line-for-line, prove with a new `Tools/cover_selftest` extension (field arrays bit-identical) |
| 5 | **Distance-map floods** | one job per fighter needing a flood | pre-baked edges (3.3), native heap (4) | Low parallel width in practice (the scan gate keeps floods rare); worth it only if group re-seeding makes many fighters flood in the same tick |
| 6 | Wall classification rebuilds | per volume | classifier already pure/blittable-ready | Small; cache already removed most cost |

**Not job candidates (stay on main thread)**: anything touching `Transform`/`GetComponent`/`MonoBehaviour`, IMGUI/uGUI, `AudioClip.Create`, `ComputeBuffer` uploads, Debug beam rendering, decision logic (`CombatController` state machine — branching, event-driven, cheap).

**Cheap off-thread wins that need NO job conversion** (pure managed math, thread-safe today):
- **SFX variant pre-render**: `ProceduralAudio.SynthFromRecipe` is pure float math + own PRNG — render the impact-variant `float[]`s on a `Task` at load/idle, hand the arrays to the main thread for `AudioClip.Create`. Eliminates the first-play synth hitch (the `synth … took N ms` log).
- **WalkLayer cold-tile fill** (the likely cause of the 85 ms spike): needs world ray data first (1) to thread; until then **time-slice** it on the main thread (N cells/frame, the "chunked cooperative scan" pattern from `PERFORMANCE_PATTERNS.md` #7) or pre-warm the fight area at load.

## 2.4 Scheduling architecture — ride the `SimClock`

Don't invent a second clock. Combat decisions already live on the 0.08 s fixed step, so:
1. **Tick boundary** (`SimClock.Ticks` advanced): a single `CombatJobScheduler` (`[DefaultExecutionOrder(-100)]`) snapshots fighter state into SoA `NativeArray`s (positions, `PoseState`, threats) on the main thread.
2. It schedules the dependency chain `Pose → Probe → Reduce` (and later `CoverBuild`, `Flood`) as `JobHandle`s.
3. **Complete at the start of the next tick's consumers** (results are needed at decision cadence, not render cadence) — with the review's probe-cadence change (`visInterval` = 0.08) this is **zero perceptible latency** (the dice/beat step is 0.08 s anyway) and gives workers a full tick of slack, so the main thread is genuinely free to render.
4. Frozen (F8) ⇒ no tick ⇒ nothing scheduled — freeze/step semantics stay exact; `[.]` single-step schedules and completes one chain.
5. Results land in per-fighter `Visible/Cover/Flood` structs the existing `CombatController` code already reads (`LastVisible`, `Field`, `DistMap`) — the consumer API doesn't change.

## 2.5 Determinism and safety (non-negotiable for a replayable sim)

- `[BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.Standard)]` on sim jobs — cross-platform identical floats.
- **Ordered reductions only**: no atomic float adds, no `Parallel` reductions whose order depends on thread timing; per-fighter results reduced by a sequential job in fixed index order.
- Jobs write disjoint output slices (per fighter) — no shared mutable state; Editor safety checks (`NativeArray` race detector) stay ON during development.
- **Equivalence guard rails first, port second** (the pattern that worked for `anim_selftest`/`cover_selftest`): every converted routine ships with an EditMode test comparing the job path to the managed reference over randomized inputs. Blocker: with no `.asmdef`, tests can't reference `Assembly-CSharp` — **introduce one `SteelCity.Sim.asmdef` (+ Editor tests asmdef)** as step 0 of Part 2 (Unity Test Framework 1.6.0 is installed). Expect an asmdef migration chore (namespaces are already consistent: `SteelCity.Sim`/`UI`).
- Offline harnesses can't execute Burst/`Quaternion.Slerp`/`Bounds.Contains` (engine intrinsics) — they keep proving the *algorithms*; the EditMode tests prove the *Burst builds*.

## 2.6 Phasing and gates (no time estimates — relative size and trigger)

| Phase | Work | Size | Gate to start |
|---|---|---|---|
| **J0** | Scale scene (N = 4/8/16/32) + per-system ms-vs-N chart; `SteelCity.Sim.asmdef` + EditMode test project; pin Burst/Collections/Mathematics in `manifest.json` | M | **now** (also needed by Part 1 captures) |
| **J1** | World ray data native (terrain `NativeArray<byte>`, volume headers + CSR broadphase) + Burst `VoxelDda` + dda parity test | L | J0 chart shows raycast-bound work scaling, OR before any new group-logic job |
| **J2** | **Group targeting job (greenfield)** | M | group-fight design chosen; J1 done |
| **J3** | Pose → Probe → Reduce chain on the SimClock scheduler | L | chart shows probe+pose > ~1.5 ms at the target N |
| **J4** | Cover field Build job (+ int volume ids, edge pre-bake) | XL | chart shows scans dominate at target N; **cover output must stay bit-identical** |
| **J5** | Flood job + native heap | M | many-fighter simultaneous re-seed shows in the chart |
| **J6** | Off-thread SFX pre-render; time-sliced/pre-warmed WalkLayer fill | S | independent — can ship any time |

**DECIDED — scale target (owner, Oct 29)**: mass scale, first time. Peak world load: 4 gangs × up to 10 teams × 10 members ≈ **400 roaming agents**; peak simultaneous violence: 3–4 firefights of 4–5 hoods each ≈ **15–25 active combatants** plus police, FBI and fleeing citizens. Consequences: (a) the current stack is a 2-fighter stack that likely stretches to ~8–12 combatants — reaching this needs *tiered simulation* (full combat pipeline only for engaged fighters; cheap state machines for roaming/fleeing NPCs; cheaper still off-screen), (b) group targeting must be spatial-hash job-native (the O(N²) trap), (c) J-phases are all justified at this N — start with the J0 scale scene (stress-spawner: chart ms vs N) and J1 native ray data, (d) the GPU presentation question (the unexplained FPS flip) is *equally* on the critical path — 400 voxel characters on screen is a renderer problem before it is a sim problem.
