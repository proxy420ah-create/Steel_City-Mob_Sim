# Possess-Mode Body Debugging — "The Body Is The Probe"

**Created**: Feb 2026
**Status**: ✅ ACTIVE — live tool, extend on demand
**Relates to**: `Assets/Scripts/Sim/VoxelCollisionWorld.cs` (shared occupancy primitives), `Assets/Scripts/Sim/PossessController.cs` (MoveWithCollision + BuildUnstuckReport), `Assets/Scripts/Sim/CoverField.cs` (EdgeOpen transition cert), `COVER_HEIGHTFIELD.md`, `RECENT_CHANGES.md`

---

## The Method

The possessed character is the most efficient debugging instrument in the project: it is a **player-driven body sweeping the real collision world**, running the identical physical verdicts the map certifies and the AI executes. When the world *looks* passable but the body refuses — or refuses where it shouldn't — that disagreement is the bug, and it can name itself.

The loop:

1. **Drive the body at the boundary** — walls, seams, curbs, stairs, debris, doorways.
2. **The mover detects the disagreement automatically** — every lane (combined + both axis slides) refused while input is held = a wedge the player can't exit.
3. **Hold ~0.75s → teleport + one structured emit** — the entire stack's verdict at that instant, in a single log block.
4. **Read the slice** — it names the failing sample, the owning volume, the step verdict, and the field's own opinion of the cells involved.

This works because of the architecture underneath: there is *one* direction table (`SquareDirs`), *one* column law (`ColumnFreeDetail`), and *one* square test (`SquareFree`). The diagnostic **iterates the same samples the verdict uses** — it cannot lie about what the mover tested, and it can never drift apart from the runtime behavior.

---

## The UNSTUCK Emit

**Trigger**: `PossessController.MoveWithCollision` — all move lanes refuse while input is held for `stuckT ≥ 0.75s`. Fires alongside `TryNearestFreeSquare` teleport (ring scan, ±1.5m level band, skinned `SquareFree`).

**Format** (real slice, decoded):

```
[Possess] UNSTUCK t=23.7 held=0.76s
  pos (8.65,0.10,-63.98) → free (8.90,0.10,-63.98)  esc 0.25m
  input (0.016,0.031)  body sq 0.38 stepup 0.15
  blocked (8.67,0.10,-63.95)
  sq9: ctr=ok +x+z=feet:vol:block_26_building#368944@(8.83,0.16,-63.78) +x-z=ok …
  stepup: top=0.25 offered but raised square refused
  dst ground=0.25 (Δfeet +0.15)
  field: OFF-WINDOW by (0.0,2.3)m of 16m field at (4.0,-61.0)
  zone=Low reqD=0.00  combat=Idle  duck=0.00
```

| Line | Answers |
|---|---|
| `pos → free / esc` | Where he wedged, where the teleport put him, how far it had to reach |
| `input` | Which direction he was pushing when it refused |
| `body sq / stepup` | The physical specs in force (resolved footprint, step band) |
| `sq9` | All 9 columns re-probed at the blocked destination — `name=ok` or `name=<height>:<vol:id#cell|terrain>@<world pos of the failing sample>` |
| `stepup` | Whether a ledge was offered (and why it was refused) or nothing in band |
| `dst ground` | The real surface height at the refused spot — proves the step-band math |
| `field` | The map's own opinion: `walk=`/`reach=` per cell, or `OFF-WINDOW by (dx,dz)m` when outside the scan window |
| `zone / combat / duck` | Posture context — a crouch-depth snag shows itself here |

## How to Read a Slice

- **sq9 names the culprit geometry directly.** `feet:vol:block_26_building#368944@(x,y,z)` = the feet-height sample of that square column lands inside that volume's voxel — *walkable-to coordinates of the exact offending cell*. Stand there and look at it.
- **Which samples failed tells you the wedge shape.** Leading-edge failures (`+z` when pushing +z) = driving into a face. Side + diagonal failures = a pocket or seam. `chest:`/`waist:` failures = overhang/awning caught, not a ground problem.
- **`stepup offered but refused`** = a ledge was in band, but the raised square still hit geometry — the stair-into-facade case: he can mount the step, he just can't be *inside the wall* at the top.
- **`field: cell[-1]` / OFF-WINDOW** = there is no map opinion here — he walked outside the 16m field window. The diagnostic can't say "the map lied" because the map doesn't cover this ground. A ped's flowfield has the same boundary.
- **`walk=True reach=True` on a wedged cell** = the *map* lied — a cert hole to hunt (report it). `walk=False` = the map knew; the snag was player-steered into a red zone (honest).

### Worked example — the tenement stoop wedge

The first slice (above): pushing +Z at the tenement frontage, five perimeter columns failed at `feet:` inside `block_26_building` — the stoop mass. Step-up offered `top=0.25` (exactly the 0.15 band edge above feet 0.10) but the raised square still caught the flanking facade → refused, correctly. `OFF-WINDOW` — outside the field. Escape 0.25m sideways. Verdict: real geometry pocket, honest refusal, minimal teleport. Not a bug — but now a *measured* non-bug.

---

## Design Rules (why it stays truthful)

1. **The diagnostic is the law.** `ExplainSquare`/`ColumnFreeDetail`/`SquareFree`/`TrySquareStepUp` all iterate the same `SquareDirs` table — verdict, climb probe, and report cannot diverge.
2. **Recovery uses the same primitives.** `TryNearestFreeSquare` requires the skinned `SquareFree` — the teleport can't place the body somewhere the body can't stand, and can't tunnel (nearest ring wins, ±1.5m level band).
3. **Emit once, not a stream.** The wedge state accumulates (`stuckT`) and the report fires on resolution — one dense block per incident, pastable, greppable.
4. **Report before the teleport.** The snapshot captures the wedged position and the refused destination — not the post-escape state.
5. **Failure names the owner.** `vol:<id>` ties a snag to a specific registered volume + cell — registration defects are debuggable, not mysterious.

---

## Current Uses

- **Movement boundary probing** — possess-drive every wall class, curb, stair, debris pile, doorway; snags self-report.
- **Field-vs-body divergence detection** — `walk/reach` on a wedged cell splits "map lied" from "player steered into red."
- **Off-window detection** — quantifies how far outside the scan window a snag occurs (ped-flowfield coverage questions).
- **Spec calibration** — `body sq`/`stepup`/`dst ground` on each emit = the numbers that were in force; tuning `stepUpMax`/`footprintOverride`/`BodySkin` can be validated against real snags.
- **Volume data QA** — repeated `vol:<id>#<cell>` hits on a visually-flat face flag bad rasterization in that registration.

---

## Future Additions (roadmap)

Ordered roughly by leverage — each is an additive emit on the same primitives:

1. **Combat-mover snag emit** — the same wedge detector on `CombatController.MoveWithCollision`: a relocate/seek leg that stalls mid-route reports its blocked square the same way. Converts "he ground against the wall-end" from observation to data. (Different meaning than possess: a *routed* snag = the field lied — higher-value bug.)
2. **Route-commit verification** — `CorridorFree`-swept legs that fail could emit the failing sweep step (which world position, which sample) so route materialization failures are diagnosed, not just rerouted.
3. **Spatial bookmark on emit** — drop a persistent marker beam/cube at the wedge point (`PathDebugRenderer.SetCustomBeam`) so the spot is findable in-scene without reading coordinates off the log.
4. **HUD mirror** — last snag report rendered in a debug tab panel; no console fishing. (Pattern: `lockedTabText` for cheap IMGUI.)
5. **`snag.jsonl` artifact mode** — optional file-write of each emit as JSON lines for offline diffing across sessions / regression comparison after collision changes.
6. **Per-character event ring buffer** — last N emits retained per character (movement events now; later: hits, falls, step-ups) — the base of a body telemetry timeline.
7. **Health/damage emits on the same shape** — projectile hit → one block: hit volume/voxel/material, posture (duck, zone, cover class), speed, exposure at the instant. Same single-emit doctrine.
8. **Emit taxonomy** — `[UNSTUCK]`, `[STEP]`, `[ROUTE]`, `[HIT]` prefixes so log slices are greppable by event class.
9. **Ped-flowfield snag watch** — fleeing civilians following the gradient: emit when their mover refuses a certified hop (should now be impossible post-`EdgeOpen` transition cert — emits = regression alarm).
10. **Bug-file draft** — emit optionally writes a `docs/known_issues/` skeleton (symptom + slice + related volumes) so a confirmed snag becomes a tracked bug in one step.

---

## Verification

- Wedge Vinny into any seam/pocket → hold input ~0.75s → teleport + one `[Possess] UNSTUCK` block.
- `sq9` names a volume id + cell + world pos for every failed sample — physically walk to `@(x,y,z)` and confirm a voxel is really there.
- `OFF-WINDOW` vs `walk=` verdicts agree with mode 8/9 paint at the same spot.
- No emit while sliding normally — `stuckT` resets on any successful lane.
