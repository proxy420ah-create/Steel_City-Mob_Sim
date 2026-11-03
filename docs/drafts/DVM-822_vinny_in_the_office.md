# DVM-822 — Vinny in the Office: a body at the table · v0.1.1

Commissioned by boss-DVM-822 — *"Bringing Vinny into the Office."*
Presented by CHAIR-820; openings closed zero-veto (CHAIR-836 synthesis, seven points).
Drafted by THE ARCHITECT. This spec ships the first *animate* entity in the
deliberation room — everything before it was furniture.

## Changelog

| Ver | Decreed by | Change | Ratification |
|-----|-----------|--------|--------------|
| v0.1 | CHAIR-836 (synthesis) | Charter draft — all seven consolidated points carried verbatim | FULL — 4 nits, none structural |
| v0.1.1 | CHAIR nit dispositions | ③ `VINNY_VS` = **0.01 exactly** (ratified measured fact); ① collider set enumerated; ② crown-check ε bounded; ④ idle placement + exit-while-seated ordering named | at gate |

## Design intent

Vinny is not a model, he is a **mode**. Three of the seven points are really one
decision seen from different angles — possess, sit, gait — and all of them hang
on a single invariant that keeps tonight's session safe:

**The deck's guest guarantees are never violated; they are suspended by name.**
The clamped camera, the pure-bus record, the cosmetic scene — each survives
intact as an invariant, and possession/sitting are named states that hold those
invariants in escrow rather than relaxing them. That is the whole architecture;
the rest is plumbing.

## Asset contract (locks the surfaces before the design)

- `vinny.json` — 1,544 voxels, 48³ grid, stasset_export shape → lands in
  `Assets/StreamingAssets/voxel_items/` so the existing whitelisted `/voxel/`
  route serves it. **Zero server change.**
- `vinny.groups.json` — **pre-baked** group assignments for the 10-part rig
  (Body/Head/L/R Arm/L/R Leg/L/R Shin/L/R Forearm), written once at export by
  the existing STAG tooling. The deck never parses binary `.groups`.
  - *Drift guard (AUDITOR-seconded):* the bake embeds `source_hash` +
    `source_mtime` of the `.groups` it was cut from; loader warns on mismatch
    but renders — stale is visible, never silent.
- `vinny.anim.json` — anim_params: 10 pivots (normalized 0–1) + procedural
  params (walkKeyframes, armSwing, legStride, kneeBend, elbowBend, restPose).
  Ported as data, not baked frames.
- World scale: **`VINNY_VS = 0.01` exactly** — ratified by the CHAIR as a
  measured fact (file metadata, Unity-honored via WeaponMount ParseVoxelSize),
  not a design choice. Named constant, never a magic number, never a hedge.

## The seven points

### ① Renderer — ten InstancedMeshes, ported pose eval

One `InstancedMesh` per rig group (10 total), each instance-shared
`BoxGeometry(1,1,1)` + `MeshLambertMaterial` — identical to the room kit.
Per-group transform:

```
M_group = T(pivot_world) · R(pose_angles) · T(−pivot_local)
```

`pose_angles` comes from a **pure eval function** ported from the game's
procedural params:

```
pose(gait, phase, t_state) → { group: {rx, ry, rz} }
```

Deterministic, data-driven, stateless — Sit is already proof the game runs
evals, not clips (state 11 = "thighs forward, shins bent down, torso upright,
arms at sides", a procedural eval).

**AUDITOR's crown check (acceptance, verbatim):** *ported Sit ≡ state-11 Sit.*
The port is judged by pose-equivalence: render Vinny seated via our eval and
diff against the game's state-11 eval. **The ε bound (v0.1.1, CHAIR's
suggestion carried):** `ε = 0` voxels on anchor-relevant coordinates —
pelvis→seat contact must land exact — and **≤ 1 voxel drift on free limbs**
(arms, head, shins). A falsifiable bound, not a vibes check. If Sit ports
faithfully, the eval is correct; the walk cycle inherits the proof.

### ② Groups — pre-bake, no runtime decoder

`vinny.groups.json` is a build artifact, committed and inspectable. Loader
reads voxel→group indices as plain JSON; the ten InstancedMeshes are populated
in one pass. Rationale stands from the opening: the deck shouldn't carry a
binary parser for frozen data, and a baked artifact is diffable in review.

### ③ Possess — a named deck mode, clamp in escrow

`deck.mode ∈ { observe, possess }`, default `observe`.

- `observe` — today's behavior: guest-clamped orbit cam, input routes to
  camera + dashboard controls.
- `possess` — entered by explicit gesture (key/button on Vinny's inspector);
  the camera clamp is **suspended, not removed**: the follow rig takes the cam
  (third-person, shoulder-height, collision-aware pull-in), input routes to
  Vinny's controller.
- Exit possess → **byte-identical restore** (AUDITOR's acceptance): camera
  position/target/fov serialize out on entry and restore verbatim on exit.
  Restore is a load of a saved snapshot, not an inverse computation.

**Input-focus gate (INFORMANT's pin, mandatory):** typed text never moves
Vinny. The possess input route is armed only while the deck canvas holds
focus; any focus into a text input (transcript box, side-table box) suspends
Vinny control until the canvas regains focus. Acceptance: typing "wasd" into
the compose box while possessed moves the cursor, not the man.

### ④ Collision — capsule-vs-AABB, derived from render placements

Analytic only, no physics lib. Vinny is a capsule `(r, h)` swept against the
collider set. Axis-resolved push-out, smallest-penetration axis first —
sliding and step-up fall out of that for free.

**The collider set — enumerated (v0.1.1):**

| Member | Treatment |
|--------|-----------|
| Floor plate | **Support plane** — capsule clamps to its top (`y=0`), never pushed out. Walking off its edge is impossible: the room bounds first |
| Walls (incl. B-style wall) | Bounding AABBs — cannot be exited |
| Commission table | Furniture AABB |
| 7 chairs + BOSS throne | Furniture AABB each (throne collides — Vinny sits *onto* it via the anchor, never walks through it) |
| Filing cabinet | Furniture AABB |
| Desk stamper, monitor, pegboard, shelf, tray | **Collapse into the wall slab** — they mount flush; their volume is unreachable behind the wall plane, so they add no separate volumes |

Membership law: *every solid placement in the active style's render set
contributes exactly one collider — support (floor), bound (walls), or AABB
(furniture).* Walk-out-of-the-room is impossible by construction.

**The A/B question — answered (charter said must):** the collision world is
derived from the *active* style's placement data — **colliders ≡ visible
placements, always.** On style-swap the collision world rebuilds in the same
pass that rebuilds the meshes; Vinny can never clip furniture he sees nor hit
furniture he doesn't. Degenerate case named: if a swap leaves Vinny inside a
new AABB, the same smallest-penetration push-out ejects him — no special case,
no teleport.

### ⑤ Sit binding — cosmetic-only, pelvis→anchor, derived not hardcoded

- The BOSS throne carries a `SeatAnchor { position, facing, animState: 11 }`
  **derived from .01-world placement data** — the chair's own transform plus a
  *relative* pelvis offset (seat-height fraction of the chair model), never a
  hardcoded world coordinate. Tonight's re-scale is exactly why: a constant
  would have rotted in place.
- Proximity prompt → play state-11 eval → pelvis snaps to anchor, facing
  applied. Stand reverses (prompt → rest pose → control resumes).
- **Cosmetic only:** sit state lives entirely in deck scene state. The bus
  stays a pure deliberation record. Any future "Vinny sits when the boss holds
  floor" is a display *derivation* off feed state — never a write.

### ⑤b Idle placement & possess ordering (v0.1.1 — the gate's fourth nit)

- **Unpossessed idle:** Vinny stands **at-ease beside the BOSS throne** — an
  idle anchor *derived* from the throne's placement (offset vector beside the
  chair, facing the table), same .01-world derivation law as the seat anchor.
  The boss's body waits by his chair; no arbitrary world coordinate exists.
- **Exit possess while seated → Vinny stays seated.** Sit is a *scene state*,
  possess is a *mode* — orthogonal. Un-possess releases control and restores
  the clamped cam; the man remains on the throne. (You don't yank a sitting
  man upright because you looked away.)
- **Re-possess while seated → prompt offers STAND first.** Control resumes
  only after the stand reverses (state-11 → restPose), then movement unlocks.
  Sit and walk never blend.

### ⑥ Gait phase — from velocity, never wall-clock

`phase = f(distance traveled)` — stride cycle indexed by arc length, so the
walk reads correct at any speed and freezes honestly when Vinny stops.
Wall-clock gait is the C6-class bug this pin exists to prevent: a timer-driven
walk slides the feet.

### ⑦ Pose-state HUD (BUTTON's add — carried)

A small readout in Vinny's inspector: current `mode`, pose-state name
(`restPose`/`walk`/`sit`), gait phase, capsule position. Cost is trivial and it
makes the crown check observable — the AUDITOR verifies pose-equivalence by
*reading* it, not guessing.

## Seat-anchor contract (the mirror-lane fold — CHAIR-835)

The queued studio item shares one contract with this spec, named here once:

```
SeatAnchor { anchor: Vec3(.01-world), facing: deg, animState: int, carrier: objectRef }
```

**Sit binds to the object carrying the anchor — whichever object that is.**
In the deck that's the BOSS throne; in the voxel studio's mirror-lane that's
the *mirrored copy* while the editable stays sculptable. The binding contract
is identical: resolve the anchor's carrier at interaction time, snap pelvis to
it, apply facing, eval `animState`. The mirror-lane draft inherits this clause
verbatim — no second design of the same idea.

## Acceptance

1. **Crown check:** ported Sit ≡ state-11 Sit — `ε=0` on pelvis→seat anchor
   coordinates, ≤1 voxel drift on free limbs (HUD-readable joint readout).
2. **Clamp escrow:** exit possess → camera restores byte-identical.
3. **Focus gate:** typing into any text input while possessed moves the
   cursor, never Vinny.
4. **Collision:** capsule vs the enumerated set — floor supports, walls bound,
   furniture AABBs push out; walk-out-of-the-room impossible on *both* styles;
   style-swap rebuilds colliders in the same pass; inside-AABB ejects via
   push-out.
5. **Sit:** proximity→Sit→pelvis-on-anchor (ε=0)→stand reverses; throne anchor
   derives from placement (rescale the kit and the anchor follows);
   exit-while-seated leaves him seated, re-possess offers STAND first.
6. **Gait:** feet don't slide — phase advances with distance, halts at rest.
7. **Cosmetic law:** zero bus writes from any Vinny system — asserted by
   construction (no Vinny code path holds a feed handle).

## Surfaces

| Surface | Change | Size |
|---------|--------|------|
| `docs/DELIBERATION_MACHINE.html` | rig loader (10×IM + groups.json + eval), possess mode (input route, follow rig, clamp escrow), collision world (capsule vs active-style AABBs), throne interactable, HUD readout | ~300–500 lines (BUTTON's bench) |
| `Assets/StreamingAssets/voxel_items/` | `vinny.json`, `vinny.groups.json` (+hash/mtime), `vinny.anim.json` | bake artifacts |
| `docs/COMMISSION_PROTOCOL.md` | SeatAnchor contract + possess-mode clause enter law | text |
| bake step | `.groups` STAG → `vinny.groups.json` via existing studio export tooling | one-off |

## Exclusions

- **Mirror-lane sit** (studio-side implementation) — queued next business;
  this spec only fixes the shared contract clause.
- **Feed-coupled posture** (sit-on-boss-floor, etc.) — cosmetic law forbids
  bus writes; display derivations are a separate design if ever wanted.
- **NPC seats** — Vinny is the only body; seat occupancy stays icon/lamp.
- **Physics** — no solver, no rigid bodies; analytic capsule vs static AABBs.
- **Runtime `.groups`/`STAG` decoding** — the deck never parses binary.
