# Voxel Inspection Protocol

**Purpose**: the standard, fastest-complete way to check a voxel asset (item, prop, room, building) for structural defects
before it ships to the Commission scene. Use this instead of improvising one-off scripts.

**Tool**: `docs/voxel_inspect.py` — one command, numpy-based, bounded memory, ~2 s on a 1.1M-voxel building.

```
python docs/voxel_inspect.py <name|path.json> [--no-sym] [--axis <voxel-centre>]
```
Bare names resolve to `Assets/StreamingAssets/voxel_items/<name>.json`. Exit code 0 = clean, 1 = findings.

---

## 1. The checklist (in this order — each step assumes the previous passed)

| # | Check | What it catches | Pass criterion |
|---|-------|-----------------|----------------|
| 1 | **Summary** (dims, voxel size, real-world size, bbox, fill %, material counts) | wrong scale, wrong asset type, stray materials | real size matches intent; bbox inside dims |
| 2 | **Integrity** (out-of-bounds, duplicate coords, unknown material ids) | corrupt export, bad generator math | all zero / none. OOB aborts the run |
| 3 | **Components** (6-connected) | floating voxels, detached pieces, rogue rooms | exactly 1 component, unless the asset is *meant* to be several parts |
| 4 | **Symmetry** (mirror-X, mirror-Z, diagonal mismatch counts) | uneven generators, off-axis parts, one-sided color marks | 0 for objects that should be symmetric. Skip (`--no-sym`) for buildings/rooms |
| 5 | **Per-layer material mix** | exposed lining on tread tops, wrong layering, colour bleed | each Y layer's dominant materials match the design |
| 6 | **Visual** (editor / headless shot) | everything the numbers can't say: reads right, proportions, lighting | a human (or screenshot) agrees |

Numbers first, pictures second. A picture can hide a 3-voxel orphan; a count never does.

## 2. Why this method (what it replaced)

| Approach | Verdict |
|----------|---------|
| Eyeballing in the editor only | misses single floating voxels, asymmetry of 1 voxel, duplicate coords. Use for step 6 only |
| Ad-hoc `python -` heredocs per question | slow to write, easy to get wrong, no standard pass criteria, and **dangerous** (see §3) |
| `voxel_inspect.py` | all five numeric checks in one pass, deterministic output, safe on huge files |

**Efficiency notes (why it's fast):**
- Components use a dense int32 index grid plus **vectorized frontier expansion** (numpy gathers per 6 directions), not a per-voxel Python stack. 1.1M voxels in about 2 s.
- If the dense grid would exceed 400M cells it falls back to a sparse dict BFS instead of allocating.
- Symmetry uses a coordinate dict of the voxels, so cost is O(voxels), not O(volume). It is the one slow-ish step on huge models, hence `--no-sym` for buildings.

## 3. The flood-fill rule (learned the hard way, 2026-11-04)

> **A flood fill / BFS must only ever step onto cells that are actual voxels. It must never expand into empty space.**

A connectivity check was written as `stack += all 6 neighbours` with no "is this a voxel and unseen?" filter. It walked
outward through empty space forever, and one Python process grew to **29 GB / 99% of system RAM**, stalling the
machine and killing the local server. The fix is one condition:

```python
# WRONG - expands into the void, unbounded:
stack += [(x+1,y,z), (x-1,y,z), ...]
# RIGHT - bounded by the model:
stack += [n for n in ((x+1,y,z), (x-1,y,z), ...) if n in cells and n not in seen]
```

Related guard rails for any ad-hoc voxel script:
1. Wrap runs in `timeout 60 python ...`. A hang should die, not eat the machine.
2. Test new loops on the 3 k-voxel pendant before a 1 M-voxel building.
3. A search whose frontier can exceed the model's voxel count has a bug. Assert on it.
4. If the machine slows down, check Task Manager for a Python process first. Do not blame "the environment".

## 4. Interpreting findings

- **FLOATING component**: look at its bbox. Usually a generator row that doesn't touch its neighbour (steep dome rows, a missing collar overlap). Fix by making adjacent rows' footprints overlap (see `_make_pendant.py`: ring inner radius reaches under the ring above).
- **Asymmetry on a should-be-symmetric item**: check for integer rounding off a half-voxel axis (`int(19.5)`), position-keyed noise (`(x+z+y)%7`), single-voxel colour marks. Key noise on **folded** coordinates: `min(x, W-1-x)`.
- **Unknown material ids**: the voxel list references an id missing from `materials[]`. The editor shows these grey; Commission falls back to `0x888888`.
- **Layer mix surprises**: e.g. milk-glass rows visible as stripes on a dome = a "lining" rule applied to tread tops. Rule of thumb: *enamel wherever the top face or outer skin is exposed, lining only on true undersides.*

## 5. Visual inspection (step 6)

**Editor deep link** (works for any asset in `voxel_items/`):
```
http://127.0.0.1:8723/studio/voxel_editor.html?type=<prop|room|building|character|vehicle>&model=<name>
```
A `model` param now suppresses the preset's own default model, which previously raced and won (it loaded the gun, not the pendant).

Editor tools useful for inspection: **0** frame model, **T** set orbit pivot to the voxel under the cursor, **Y/X/Z slice sliders** peel the model open, hover shows `Mat / region / group / cell coords`, **U** toggles the Unity-mirrored view.

**Headless shot** for evidence: use the Edge CDP harness pattern from `docs/core/PERFORMANCE_PATTERNS.md` lesson #18 (headless Edge + `--use-angle=swiftshader`, navigate, `Page.captureScreenshot`). Software GL means the shot proves *looks*, not *frame rate*.

## 6. Where this runs in the pipeline

1. **Generating / editing an asset** → run `voxel_inspect.py` before saving to `voxel_items/`.
2. **Building source (`office_complex`)** → run with `--no-sym` after any scripted edit (`building_edit.py`, `reskin_building.py`); expect `components: 1`. A second component means a rogue piece or a cut that split the building.
3. **Before committing a new item** → clean exit (0) and a visual shot in the scene. Record in `RECENT_CHANGES.md`.

## 7. Known limits

- Components are **6-connected (face)**. A voxel touching only along an edge or corner is reported floating. This is deliberate: face contact is what keeps a mesh watertight and what greedy-face bakes treat as attached.
- Symmetry assumes a vertical (Y) axis through the bbox centre. Pass `--axis` for off-centre designs.
- It validates structure, not taste. Proportion, colour and lighting still need step 6.
