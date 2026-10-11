# Generator Studio — Voxel Editor Sub-Module Implementation Plan

**Created**: October 9, 2026
**Status**: 📐 Plan — approved direction, awaiting implementation
**Contract**: `docs/core/BUSINESS_TAXONOMY_ASSET_GENERATION.md` (archetypes, slot semantics, loose→locked→catalog lifecycle)
**Host**: `VoxelAssetStudio/voxel_editor.html`

---

## Purpose

Bring procedural building generation **into the voxel editor as a sub-module** so a model flows natively:

```
generator (seed + params) → preview grid → Accept → voxelMap → hand-edit → export JSON
```

The editor becomes the authoring surface for the lifecycle's creative phases:

- **Phase A (loose)** — grammar-open generation + sliders = concept-art machine
- **Phase B (locked)** — curated result + hand-edits = the reference model, captured as a lock spec
- **Phase C (catalog)** — locked spec + context params (zone/wear) = production variants

Reuse, don't rebuild: incremental rebuild, GPU slice, selection/extrude, tracked maps, delta history, material palette, JSON import/export — all already exist and are fast at 765k scale.

---

## Architecture

```
voxel_editor.html
 └─ <script src="gen_core.js">          classic script — Grid3D, seeded RNG,
 │     window.GenCore                    param-schema → slider UI, preview pipeline
 └─ <script src="gen/gen_tenement.js">   classic scripts, one per archetype
       window.GenArchetypes.tenement     (port of measured reference grammar)
```

**Why classic scripts**: the editor loads `type="module"` + CDN importmap — local ES-module imports are CORS-blocked on `file://`. Classic `<script src>` files work; each registers onto `window.GenArchetypes` (same pattern as palette/globals today).

### `gen_core.js` — the shared spine

- `Grid3D` — `Uint16Array(w*h*d)` + `setBox/getBox/fillBox` covering the numpy slice patterns generators use (`grid[x0:x1, y0:y1, :wt] = mat` → `fillBox`)
- `rng = mulberry32(seed)` — deterministic JS RNG; **cross-language seed parity with `np.random` is NOT required** (lock spec captures the artifact, not the RNG stream)
- Param schema → slider panel: each generator exports `params: [{key,label,min,max,step,group,phase:'structure'|'dressing'}]`; core builds UI automatically (same slider pattern as slice controls)
- Preview pipeline: generate into a scratch Grid3D → overlay render → **Accept writes voxelMap as ONE history entry** (regenerating never touches undo)

### Generator module contract

```js
window.GenArchetypes.tenement = {
  name: "Tenement Block",
  defaults: { w: 192, h: 120, d: 192 },          // canonical dims
  params: [ /* schema */ ],
  generate(rng, params) → { grid: Grid3D, meta: { door_face, door_height, … } }
}
```

`meta` flows to `building_meta` on export (door attachment data survives the round-trip — same shape Python emits).

---

## Editor integration points

| Existing infra | Reuse |
|---|---|
| Model load path (`dims` + voxelMap fill + rebuild) | Accept = same entry as JSON import — dims change handled once |
| Tracked maps + delta history | Accept = 1 history entry regardless of 765k cells |
| Incremental instances + partial uploads | Preview render reuses instance path (scratch overlay layer, or accept-only) |
| Slice clip uniforms | Free — inspect generated interiors instantly |
| Selection / extrude / flood fill | Touch-up edits land on accepted grid |
| Material palette + matVisible | Generator materials are palette IDs already |
| JSON export + `json_to_stasset.py` | Round-trip unchanged |

**Preview without pollution**: two options — (a) scratch-grid overlay rendered as a temporary instanced layer (richer, more code); (b) regen blocked until grid accepted/cleared (simpler — recommended for P0: "Generate" panel replaces current model only on Accept, discards preview otherwise).

### Generate panel (UI)

- Type dropdown (populated from `GenArchetypes`)
- Seed field + 🎲 reseed + Regenerate button
- Auto-built param sliders grouped by `phase`: `structure` (massing, wings, floors) vs `dressing` (signage, palette, wear)
- **Loose/Locked toggle** — Locked greys structure sliders (only dressing varies); that's the lifecycle switch in UI form
- Accept (→ voxelMap, 1 undo entry) / Discard

---

## Lock spec — the artifact between JS and Python

JSON sidecar emitted from the editor:

```json
{
  "type": "tenement_block", "spec_version": 1,
  "seed": 12345, "dims": [192, 120, 192],
  "structure": { "wings": "U", "courtyard": true, "stories": 12, … },
  "dressing":  { "awning": "striped_red", "paletteShift": 0.2, … },
  "meta": { "door_face": "front", "door_height": 8 },
  "conventions": { "panes": 1, "bore_air": true }
}
```

**Role in the pipeline**: JS editor produces lock specs interactively → Python batch generators read the same spec for headless emission (`generate_city_assets.py`). The spec is the shared truth; each language implements the grammar once. If parity ever drifts, the spec + a grid-diff harness is the arbiter — not the RNG stream.

---

## Implementation phases

| Phase | Scope | Verify |
|---|---|---|
| **P0 — Prove the loop** | `gen_core.js` (Grid3D+RNG+schema→sliders) + port ONE generator (recommend `gen_scaled_tenement` — smallest real grammar) + Generate panel with Accept-only flow | Generated grid → voxelMap → slice → export JSON opens correctly; panes verify depth-1 via run checker |
| **P1 — Param UI + loose/locked** | Schema-driven slider groups, phase toggle, seed history (last-N seeds) | Sliders regen live; Locked leaves only dressing editable |
| **P2 — Lock-spec export** | "Save Lock Spec" → sidecar JSON; load-spec restores params+seed | Spec round-trips; re-gen from spec = identical grid |
| **P3 — Catalog preview** | Type browser + context params (zone, wear) + variant strip | Same locked spec renders zone-flavored variants |
| **P4 — Python parity** | Python generators consume lock spec; grid-diff spot check vs JS | `generate_city_assets.py` emits spec-identical buildings headless |

## Pitfalls (recorded up front)

- **`file://` CORS** — classic scripts only; never ES-module `import` of local files (Pitfalls §11)
- **Undo pollution** — regen must NOT write voxelMap per iteration; preview scratch buffer only (see P0 decision)
- **Dims changes on Accept** — go through the existing load/import path; don't hand-patch voxelMap
- **`meta` survival** — door_face/door_height must reach `building_meta` on export or Unity door attachment breaks
- **RNG parity** — don't chase it; the lock spec is the artifact both sides honor
- **Sector budget** — variants are geometry-level (bake-in), no extra instances at runtime; still check `CITY_SCALE_ARCHITECTURE` sector invariants for mega-assets

## Open questions

1. Overlay preview (live scratch layer) vs accept-only — P0 ships accept-only; revisit if regen iteration feels blind
2. Should lock specs live beside model JSONs (`JSON Models In Progress/`) or a new `lock_specs/` dir
3. Grammar-sharing format: do generators eventually emit their *decision trace* (which branches fired) for debugging why a seed produced a given shape
4. Param-presets per type (the "dial-in" curation) — stored in the lock spec or a separate `type_presets.json`
