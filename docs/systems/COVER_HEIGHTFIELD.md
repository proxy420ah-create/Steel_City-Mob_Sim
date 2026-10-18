# Cover Heightfield — Measured-Body Detection Rebuild

**Status**: � PHASE A IMPLEMENTED — measured `h(d)` classifier live, compile-verified (headless csc); pending runtime playtest → then Phase B
**Supersedes**: eye-anchor classification in `CoverSensor` (constants `standEye`/`crouchEye`)
**Canonical unit**: **Vinny = 0.62m** (62 vox @ `voxelSize=0.01`). World voxels = 0.05m.

## Doctrine

Cover classification must never consume a human-scale constant again. The system
measures the character's **posed silhouette top** as a function of crouch depth —
`h(d)` — against the **absolute ground under his feet**, and classifies every
column in the ring by comparing its solid top to `groundY + h(d)`.

- `standEye` / `crouchEye` / `EyeOffset` / `EyeOffsetDeep` constants: **deleted**.
- `TryGetEyeAnchor` stays — it answers "can he *see*" (vision cone, firing),
  not "is he hidden" (cover). Those were conflated; this doc separates them.
- Standing on voxels is additive by construction: `supportY` is a measured
  column top, not `pos.y` — rubble, curbs, crate-tops all raise the silhouette.

## Measured quantities

| Symbol | Meaning | Source |
|---|---|---|
| `H_stand` | silhouette top, Idle pose | `MeasureTopY(state=0)` |
| `h₀` | silhouette top, Crouching @ depth 0 | `MeasureTopY(state=5, depth=0)` |
| `h₁` | silhouette top, Crouching @ depth 1 | `MeasureTopY(state=5, depth=1)` |
| `supportY` | world Y of the solid column under the feet | downward probe |
| `h(d)` | `lerp(h₀, h₁, d)` — same depth-lerp contract the pose eval uses | derived |

Vinny reference values (to be confirmed at first runtime log):
`H_stand ≈ 0.61m`, `h₀ ≈ 0.50m`, `h₁ ≈ 0.45m` — the total crouch band is only
~5–8cm of head travel, which is why cover heights in a Vinny world differ by
1–2 voxels, not tens.

### `MeasureTopY(state, depth)` — `VoxelCharacterAnimator`/`VoxelCharacter`

- At asset load, precompute per-pose-group **top voxels**: the voxel with max
  local Y in each group (10–15 points total).
- `MeasureTopY` = `max over groups of PosePoint(topVoxel, gid, state, depth)` —
  the same FK path as `PoseToWorld`, no painted regions required.
- Cached per (state, depth); refreshed on asset reload only. Zero per-frame cost.
- Fallback without anim data: `H_stand = solidTopY × voxelSize` (the model's own
  bounds — still self-calibrating).

### `supportY` — `VoxelCollisionWorld` (new `ColumnTopY(worldXZ)`)

- Resolve (x,z) into the overlapping volume's footprint → `corner.y + (colBase +
  colSpan) × vs`; fall through volumes; terrain probe last.
- Used both for Vinny's own silhouette base AND for field-cell validation —
  cells at different elevation stop being silently wrong on slopes/steps.

## Classification — `CoverSensor.Scan` rewrite of the eye path

```
wallTop = w.spanTop                    # absolute world Y (unchanged data)
Full   ⇔ wallTop ≥ supportY + H_stand  # standing silhouette hidden
Crouch ⇔ wallTop ≥ supportY + h₁       # deepest duck hides it
Low    ⇔ otherwise                      # unusable

reqDepth = (wallTop − margin − supportY − h₀) / (h₁ − h₀)   clamped 0..1
```

- `openAtWorld` band becomes `supportY + h₁` (deep silhouette) — same role,
  real value.
- `depthMargin` shrinks to ~0.02 (2cm — Vinny-scale nose clearance), or becomes
  a fraction of `H_stand`.
- `wallFloor` (minSpan) → Vinny-relative: `~0.15m` (below knee = not cover).
- `EyeOffset`/`EyeOffsetDeep` HUD readouts become `H_stand`/`h₀`/`h₁`.

## CoverField — bookkeeping + walkability

- `volIdOf[cell]` → **`wallIdxOf[cell]`** — the splatting wall index, not the
  volume id. Retires the `requireVol` pin patch and the block-volume identity
  gap (`COVER_PICK_SPLAT_OVERLAP_WRONG_VOLUME` / the `block_45` problem).
  `PickNear` returns the owning wall directly.
- New per-cell layer `Step`: `Δtop = colTop(neighbor) − colTop(cell)` against
  `stepHeight ≈ 0.15–0.20` (≈¼ Vinny) → `Walkable | StepUp | Blocked`. Same
  `colBase`/`colSpan` data — navigation reads the cover map for free.
  Downhill: `Δ ≥ −dropHeight (~0.3)`.
- Field-cell ground validation uses `ColumnTopY` (real support) instead of
  assuming flat ground.

## Painted heights — digit atlas blit

Goal: in possess/inspect mode the ring shows each column's top height as a
number **on the surface**.

- `_CoverHeightsTex` — a second `Texture2D` per sensor (or slice), covering the
  same world rect as the field, at `fieldRes × cellPx` (64×8 → 512²).
- **CPU-baked digits**: each emitted `CoverWall` (or each field cell, sampled
  at its dominant column) blits its height into the texture with a tiny 3×5
  font — digits rendered at the column's world position, colored by class
  (Full / Crouch / Low). Numbers in cm (`"80"` = 0.80m) or 1-decimal meters —
  pick at first visual pass.
- Shader: existing field sample + `heightsTex.Sample(sameUV)` → additive
  overlay; digits survive because they live at sub-cell resolution in their
  own texture (the 1-texel/cell field can't hold glyphs — that's why it's a
  second texture, not a channel).
- Toggle: COVER tab `heights` checkbox + a field-paint mode key; contributors
  union the same way as verdicts.

## Testbed — the gradient wall (replaces the 4-box ladder)

One continuous wall instead of discrete boxes — the wall top *is* the swept
`reqDepth` input. In voxels it's a staircase, which is a feature: each step is
a legible depth sample.

```
[=== FULL ===][ fine taper — 1 vox/step, stretched ][=== NULL ===]
  standing      ~1 voxel every 8-12 columns           drops to pad
  cover         (the whole crouch band is ~1-2        → Low class →
                 voxel-steps tall at Vinny scale)      stands/exposed
```

- **Full segment**: top ≥ `supportY + H_stand` (+margin) — long enough to walk
  behind standing.
- **Taper**: descends through `H_stand → h₁` in single-voxel steps, each step
  stretched horizontally — at Vinny scale the entire adaptive band is ~4-5
  steps, so the ramp is short in voxels but long in meters (that's correct).
- **Null tail**: continues below `h₁` — class flips Crouch → Low and the
  pose must release to standing/exposed. Tests the invalid-cover boundary.

## Possess-mode live cover drive (new requirement)

In possess mode, moving Vinny parallel to a wall drives the crouch channel
continuously:

- Per frame: nearest coverable column on the facing-open side → `reqDepth` →
  `anim.crouchDepth` eases toward it (the existing 3/s ease).
- Class changes drive pose state: Full → stand (Idle), Crouch → ducked at
  reqDepth, Low/none → release to Idle (stands back up — the "null" test).
- No combat needed — this is the inspection path the gradient wall is built
  for: walk the wall, watch him fold/unfold live, read the painted heights.

## Phasing / checkpoints

| Phase | Work | 🧪 Checkpoint |
|---|---|---|
| **A — measure** | `MeasureTopY`, `ColumnTopY`, classifier swap, delete eye constants | COVER tab prints measured `H_stand/h₀/h₁`; `🧱 [CoverPick]` classifies correctly on current boxes (all four = Full on Vinny — expected, proving the measurement) |
| **B — field** | `wallIdxOf`, Step layer, per-cell support probe | pick returns owning wall; curb (0.05) shows StepUp, boxes show Blocked |
| **B2 — possess drive** | nearest-column → `reqDepth` → eased `crouchDepth`; Full/Crouch/Low → Idle/Crouch/release | walk the gradient wall: folds/unfolds live, stands at the null tail |
| **C — paint** | heights texture + digit blit + shader overlay + toggle | possess mode: numbers on surfaces around Vinny |
| **D — later** | cover-object extraction, multi-span columns, threat-axis ray probes, six-shot pop-up loop | deferred until A–C verified |

## Risks / traps

- `pos.y` must not silently double as `supportY` — probe or the whole additive
  story breaks on rubble.
- `h(d)` during a mid-ease transition reads the eased depth, not the target —
  matches what the shader draws (correct).
- Field cells are coarser than columns — per-cell height = **max colTop** in
  the cell (conservative for cover) while digits paint per-column positions.
- Any other 0.1m-assumption content (`gen_shooting_range.py`,
  `generate_empty_land`) still risks half-scale geometry — sweep when the
  arena redesign lands.
- Keep `TryGetEyeAnchor` wired to the vision cone only — do not delete the
  function, just its cover duty.

## Open design points for the arena redesign (user's lane)

- Box tops live in a ~0.35–0.75m band for Vinny; exact ladder values come from
  measured `h(d)`, not guesses.
- "Cover objects" (grouped columns → named features) is the natural next layer
  once column data paints visibly.
