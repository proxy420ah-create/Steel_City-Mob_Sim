# Perception & Awareness — city-wide detection architecture

**Created**: October 20, 2026
**Status**: 📝 SPEC — doctrine/architecture only; no implementation yet
**Depends on**: EXPOSURE_ENGAGEMENT.md (implemented), COMBAT_COVER_STATE.md,
ANIMATION_SYSTEM.md (Looking sweep, duck scalar), PROCEDURAL_AUDIO.md (SfxWaves)

---

## Goal

Generalize the two primitives already shipped — the **vision cone** (awareness)
and the **exposure probe** (fire geometry) — into a city-wide perception layer
shared by citizens, hoods, and police.

The split is deliberate and stays:

| Layer | Primitive | Question it answers |
|---|---|---|
| Awareness | `VisionSensor` cone + audio events | "Does he *notice*?" |
| Geometry | `CoverSensor.TryGetVisibleVoxels` | "Can his eye physically *reach* him?" |

**Noticed ≠ shootable.** A target inside the cone but fully tucked behind a wall
is detected yet exposes zero voxels — aware, unable to fire. Conversely a valid
`EnterCombat` order never requires cone detection; explicit orders bypass
awareness because the order itself establishes knowledge.

## Driving scenarios (user-authored)

1. **Citizens observe and report crime** — a witness's cone over a criminal act
   produces a report only if the witness actually saw it.
2. **Rival hoods keep tabs** — opposing-family hoods detect each other on the
   street; detection → a fight-instigation roll.
3. **Police read faces** — officers compare recognized actors against a wanted
   list; identification is perceptual, not database magic.
4. **Attacker/defender roles from witnessed initiation** — self-defense is
   legal, assault is not; law enforcement reacts to *what it saw*, so legality
   is a per-witness record, not a global flag.
5. **Back-turned ambush**: rival spots a defender facing away → initiates →
   defender hears the gunshot → turns (the universal gunshot reaction) → his
   cone sweeps over the attacker → combat begins.
6. **Combat resolution by search**: target flees/disappears → exposure zero →
   `lastSeenTarget` → the authored 3-step visual search → redetect or abandon
   → return to normal.

## Layer architecture

```
PERCEPTION — per actor, always on
  VisionSensor cone        eyes: FOV + LOS + dwell accumulation
  SfxWaves events          ears: gunshot/glass/shout, positional + radius
         │ detection events (actor in/out of awareness)
         ▼
COGNITION — per actor, memory
  awareness table: actorId → { level, lastSeenPos, lastSeenAt, identity }
  recognition: face/identity resolution against family + wanted lists
         │ verdicts (noticed / recognized / identified / lost)
         ▼
REACTION — shared channels
  turn-to-sound · investigate last-seen · alert/report · flee · ignore
       │ roles resolve (attacker/defender/witness)
       ▼
ENGAGEMENT — CombatController (implemented)
  cover pick → exposure-gated aim/fire → cylinder → reload beat
       │ target lost
       ▼
SEARCH — authored lookScan pattern over lastSeenTarget
  redetect → re-engage  |  decayed → return-to-normal
```

## Perception layer

### Vision (exists — `VisionSensor`)

Current contract, kept:

- Posed-eye cone: `TryGetEyeAnchor` origin + forward, `range`, `halfAngleDeg`,
  horizontal fan, `VoxelCollisionWorld` LOS ray per target.
- Detection `0..1` rises while visible (`riseTime`), decays out of cone
  (`decayTime`); sweep gaps produce natural detection stalls — the Looking
  state's authored head-yaw sweep physically sweeps the cone.
- **Live head-track channel**: `VoxelAnimator.headYawOverride` (radians,
  body-relative, NaN = off) poses the head group's yaw at the eval layer, so
  the eye anchor — and therefore the cone — can be aimed at a world target
  while the body faces elsewhere. `CombatController` drives it during
  threat-armed `SeekingCover` legs: eyes ride `AimAnchor()` (belief: freshest
  sighting else entry report, never live truth) while feet run the route —
  the transit opportunity channel can genuinely produce contact instead of
  holding `cone off` forever. In-cover beats release it to authored poses.
- V-mode 7 paints the cone on the raymarched world.

Required evolution:

- **Targets become actors, not `Vector3`s.** `targets: List<Vector3>` and
  `detection: Dictionary<Vector3, float>` are testbed scaffolding. The sim needs
  an actor registry (`SimActor` or equivalent) exposing identity, faction/family,
  and a sample point. Detection keyed by `actorId`, not position.
- **Target point for the cone test**: start with a body anchor (posed eye or
  chest-height point). Later refinement may reuse the silhouette sample cloud —
  cone over *any visible sample* — but that is an accuracy upgrade, not a
  requirement, because awareness is coarser than exposure.
- **Distance-proportioned cost**: per-frame cone eval per actor pair does not
  scale. Registry partitions by distance bucket; far actors are skipped or
  ticked at low cadence. Detection is per-observer — no shared truth table.

### Hearing (exists — `SfxWaves`; consumer channel needed)

`SfxWaves.Emit(pos, audibleRange, refDistance, kind)` already fans positional
sound events (used by gunshots). The missing piece is a per-actor **hearing
subscription**:

```
OnHeard(event) → if within range: queue reaction (turn-to-sound)
```

Reactions are authored once, consumed by every actor type — citizens turn and
stare; hoods may investigate; police respond per dispatch rules. No
per-scenario scripting.

## Cognition layer — the awareness table

Per observer, keyed by `actorId`:

```csharp
struct Awareness {
    string actorId;            // registry identity
    float level;               // 0..1 detection (cone dwell)
    Vector3 lastSeenPos;
    float lastSeenAt;
    IdentityState identity;    // Unknown → Suspected → Recognized
    float identityConfidence;  // face-reading progress
}
```

- **Recognition tiers**: `level` answers "someone there"; `identityConfidence`
  answers "I know who that is". Identity accrues with dwell + proximity
  (closer = faster ID); police compare recognized actors to the wanted list,
  hoods compare against family/rival rosters.
- **Decay**: `lastSeen` decays like detection; a target gone long enough drops
  to a stale memory — the input the search phase consumes.
- Citizens use the same table with trivial lists (report what was *seen* —
  crime acts, not faces).

## Reaction layer — shared channels

| Stimulus | Reaction channel | Consumers |
|---|---|---|
| Heard gunshot | `TurnToSound`: yaw to source → Looking sweep draws the cone over it | all citizens |
| Actor detected + recognized hostile | `Alert` / fight-roll evaluation | hoods, police |
| Crime act witnessed | `Report`: move to police / raise suspicion | citizens |
| Under fire + no LOS | `Flee` / defensive combat entry | defender |
| Stimulus resolved | `ReturnToNormal` | everyone |

The defender flow is emergent end-to-end: **hear → turn → sweep → detect →
enter combat** — defender status is earned through perception, not granted by
an "attacked" flag. A defender who never gets eyes on the attacker never
legally shoots back — honest to the self-defense rule.

## Roles & legality — witnessed initiation

- Each observer's awareness table records *what it saw*: initiation events
  observed inside its cone (who raised a weapon at whom), not a global
  "attacker" boolean.
- Police arriving mid-fight see two armed actors — reaction per witness
  record: an officer who watched the instigation treats attacker/defender
  differently than one who only saw exchange.
- Data shape: `WitnessedEvent { witnessId, eventKind, actorId, targetId?, pos, t }`
  — appended by the perception layer, consumed by law response + report flow.

## Engagement hand-off

- `EnterCombat` remains an **explicit order** for player-directed fights and
  for the perception→combat path once a reaction channel decides "engage".
- ⚠️ **Corrected 2026-10-22** — the earlier draft said combat probes bypass
  the cone ("the shooter already knows the threat"). That conflated
  threat-AWARENESS with CONTACT and produced blind-fire bugs (shots at
  last-known while the cone pointed elsewhere). The order establishes that
  a fight exists and a belief anchor — **contact still requires the cone**.
  See the detection stack below.
- Roles feed combat metadata: `role = attacker | defender` for reports/heat.

### The detection → contact → aim → fire stack (implemented 2026-10-22)

Every "did he see him / can he shoot" question has exactly one owner layer;
lower layers never substitute for higher ones:

| Layer | Owner | Contract | Failure mode it prevents |
|---|---|---|---|
| **DETECTION** | `VisionSensor` (posed eye fan — `EyeDir` ± `halfAngleDeg`, `range`, LOS) | Is the target inside the cone *and* unoccluded? | awareness without attention — a flank-exposed actor the shooter never faced |
| **CONTACT** | `TickExposureFire` probe = `TryGetVisibleVoxels` ∧ `ConeCovers(centroid)` | Resolved voxels inside the fan, this cadence tick | geometric truth without attention = phantom sightings, stale belief writes |
| **AIM** | `ArmAimSolver`, one IK channel, two modes | TARGETED: `vis.centroid` on contact, `AimAnchor()` (belief) while peeking; AT-READY: forward present on scout legs | arm raised ≠ permission to fire — painting last-known is legal, firing at it isn't |
| **FIRE** | `TickExposureFire` gate | `canSee` ∧ held convergence (`convergeSince` + `fireSettleSec`) ∧ cadence ∧ cylinder ∧ `BoreBuried` | stale-error instant shots; belief discharge; bore-in-wall waste |

Invariants worth stating plainly:

- **Contact requires the cone.** `emptyProbes`, the acquisition edge, the
  last-known write, and the trigger all ride the same `canSee` — one gate,
  no side channels.
- **Aim ≠ fire.** Peek/scout/hold channels may keep the gun up on belief or
  forward indefinitely; the hammer needs contact. (Fires-on-belief was the
  shipped violation: `FIRES_ON_BELIEF_NO_CONTACT`.)
- **Converged means held.** The solver's error channel is a frame stale —
  a retarget must *persist* inside `aimGateDeg` for `fireSettleSec` before
  the trigger falls.
- **Head yaw is physical coverage.** `EyeDir` rides the posed head;
  `dbgSearchLook`'s sweep and `FaceTrack` move the fan — sightlines are a
  facing resource, not a boolean.

## Search & resolution — the authored 3-stage procedure (papered 2026-10-21)

Inputs: `LastSeenTarget` (implemented — written per positive probe), the
awareness-table `lastSeenPos`, and the cover field's zone centroids.

### Belief vs truth — the contract that keeps search honest

- Search **actions aim at belief**: every peek/rise/swing targets
  `lastSeenPos` (memory), never `ThreatCharacter`'s live position.
- **Reacquisition comes from perception**: `VisionSensor`'s cone evaluates
  the threat wherever he ACTUALLY is — a flank that reveals him
  mid-relocation behind different cover is a hit even when `lastSeenPos`
  reads empty. Search discovers; it doesn't just confirm.
- **The vision cone supersedes all aiming** (2026-10-22): `canSee` =
  geometric unocclusion ∧ `ConeCovers(centroid)` — resolved voxels must
  sit inside the posed eye fan (`EyeDir` ± `halfAngleDeg`, within
  `range`), including for `emptyProbes`. Voxels outside the fan are
  undetected — a flank-exposed leg can't phantom-snap. Head yaw sweeps
  the fan physically (`dbgSearchLook` widens mid-leg coverage), and
  movement-facing on legs means off-axis threats stay unseen until the
  corridor turns his head. Detection-LEVEL accumulation (hold-in-cone
  before acquisition) exists in `VisionSensor.detection` but is not yet
  wired into the contact contract.
- Arena caveat: both actors are exposed at combat start, so the first
  positive probe seeds each side's `lastSeenPos` automatically.
- **Implemented — belief is EVENT-driven, and it stores the RING not the
  pixel**: `CombatController.ThreatPos` only moves on perception EDGES —
  the **acquire edge** (miss→hit) and the **loss edge** (hit→miss) both
  write `ThreatFeet()` — the entity position at the moment of
  confirmation, never the wandering visible-voxel centroid. While contact
  is sustained the belief does NOT drift — the live body is the truth
  channel and `Engaged` tracks it **contact-gated** (`contactSeen ?
  ThreatFeet() : AimAnchor()` — a live reference alone would be
  omniscient through his hiding). The hit-level channel is separate:
  the muzzle still aims `vis.centroid` (aim at what's visible) and the
  `visible>0` verdict still gates fire. `Sensor.Threat` (cover zones)
  and `AimAnchor` (intent) ride the frozen belief — zones no longer
  re-paint every probe tick. Debug pair: ORANGE `pick_aim` beam cross =
  Tan's live truth; MAGENTA `last_seen` post + direction beam = Vinny's
  belief — overlap while visible, diverge once he hides.

### The procedure (atomic procedures; only settle seams between)

1. **MISS IN POSITION** — ANY empty-peek verdict: contact lost in
   `Engaged` (`emptyProbesToDrop` consecutive empty probes), a
   zero-contact `Rise`, or a zero-contact `Swing` dwell → `Duck` →
   `Settle`. `lastSeenPos`/belief is fresh (or the entry report, if he
   was never seen — the search still orbits something). The Settle seam
   is the branch point.
2. **THE ORBIT (collapsed stages, 2026-10-22)** — the goal is not two
   fixed hops but **~90° of bearing around the anchor**, accumulated
   through as many Hidden-zone hops as geometry offers (the user:
   "across the street, down a block, around a block — even behind enemy
   lines"). At arm: anchor = `AimAnchor`, origin bearing = where he
   stands, **orbit radius** = the standoff held at the loss — the ring
   drawn in-world is the HOT PATH: hop scoring hugs it
   (|stand→anchor dist − radius| + 0.15·travel), any depth near or far
   is legal, side = rolled CW/CCW. Each `TryOrbitStep` picks the
   cheapest unvisited Hidden zone advancing the arc ≥15° (fallback: any
   forward progress) whose cover face **points away from the anchor**
   (`outDir · anchor→stand > +0.3` — outDir points outward into the
   stand area, so facing-away cover reads parallel; the hop path is a chain of
   genuinely opposing cover, not accidental hiding). Legs are ROUTED —
   `BuildRoute` Dijkstra-floods the cover field's own cell grid with
   voxel-solid walkability (the same SamplePoint predicate as
   MoveWithCollision), the orbit ring as a soft per-cell cost
   (`routeRingBias`), and a string-pull into waypoints; `search_legN`
   draws the corridor polyline.
   Hop classes (`orbitReconEvery` = 3): RECON legs land and run the
   peek; TRANSIT legs land → brief settle → next step without peeking
   (2 transit → 1 recon). An aborted leg marks the zone spent and
   re-picks from where he stands.
3. **RESOLUTION** — `progress ≥ 90°` = the flank is satisfied; the peek
   at that stand is the answer, and a following miss → `ExitCombat` →
   resume duties (stale awareness). Full arc exhaustion (no unvisited
   Hidden zone advances on the rolled side) ends it the same way.

**PEEK = two shapes**, both verdict-gated procedures ending on a fresh
visibility probe at the converged pose:

- `Rise` (vertical — existing beat; body faces the threat side of cover).
- `Swing` (lateral — new beat): step out to a point lateral of the zone
  centroid, body yaw facing the cover EDGE swung around (either side per
  the chosen flank). Gun aims at `lastSeenPos` throughout — the arm solver
  handles the twist, same as rise.

During the peek dwell the **lookScan sweep runs the Looking state** — the
authored head-yaw animation physically sweeps `VisionSensor`'s posed cone
over the last-seen region (the scan animation IS the mechanic — cone rise
is time-in-cone, not a binary).

**Reacquire** — aimed probe sees him at `lastSeenPos` OR cone detects him
anywhere (`AnyVisible`/`AnyDetected` on the threat's registered point) →
restart the engagement rhythm from scratch (seek/settle/rise/engaged) and
refresh `lastSeenPos`. The cone's `riseTime` is the honest dwell — a peek
has to hold long enough for detection to accumulate.

## Inspectability contract

- Per-actor awareness table in the debug HUD (level, identity state, age).
- Cone viz stays (V-mode 7, beams); probe rays stay (showTargetViz).
- Hearing events drawn as brief radius rings at the emitter.
- Reaction channel transitions logged with the existing emoji log style.

## Performance

- Cone eval cost = per observer × registered nearby actors (angle + LOS ray).
  Distance-bucketed registration + staggered ticks keep it bounded; identical
  budgeting as the cover field scan (0.4s cadence, move-threshold).
- One `SfxWaves` emit → O(nearby actors) cheap range checks; turn reactions
  are per-actor state, not per-frame work.
- No shared global truth — each observer evaluates independently; the city
  scales by culling observers, not by centralizing knowledge.

## Explicit non-goals / deferred

- ~~The 3-step search pattern content~~ — papered 2026-10-21,
  implemented 2026-10-22, then **collapsed to the ORBIT doctrine same
  day** (user: "the goal is a ±90° bearing offset to the anchor, hops
  however far the cover chain leads"): `ArmSearch()` arms at ANY
  empty-peek verdict and records the orbit frame (anchor = `AimAnchor`,
  origin bearing, buffer radius = standoff at loss, rolled CW/CCW side).
  `TryOrbitStep` — consumed at each Settle seam — picks the cheapest
  unvisited Hidden zone advancing the arc ≥15° at ≥ buffer radius
  (`searchVisited` prevents ping-pong; a two-pass min-step relaxes to
  "any advance" before declaring exhaustion). Progress is cumulative;
  overshoot past 90° is legal. Legs run the scout posture (local Hidden
  verdict folds to `reqD`, open ground = `duck 0` + `AimWalk` + arm
  presented FORWARD; `dbgSearchLook` A/Bs the head sweep). Abort → zone
  spent → re-pick; only arc exhaustion or `progress ≥ 90°` ends the
  episode. Mid-leg sighting rolls 50/50 continue-vs-`TryNearestCover`
  park. Panel lane `SEARCH ▸ ORBIT n° ▸ FLANK 90°`; markers
  `search_ref`/`search_aim` (goal spoke)/`search_buf` (standoff ring)/
  `search_pick`/`search_leg`/`search_tgt`. Still deferred: stat-weighted
  side choice, the lookScan-authored sweep during peeks, the **flow-
  field flood** replacing Euclidean hop cost + bounding where-Tan-could-
  be (reachable-set gate).
- Identity/face-recognition tuning constants.
- Dispatch/police response policy beyond the witness-record contract.
- Conversation/social perception (gestures, bribes) — out of scope.
- Persistence of awareness across week transitions — later, with city-state
  baking.

## Risks / traps

- Do not merge cone and probe into one system — they answer different
  questions; merging collapses "noticed" into "shootable" and breaks both.
- Detection must key on actors, not positions — a moved target must not reset
  recognition to zero.
- The cone is a horizontal fan today; elevation-blind awareness is accepted
  for street-level play (documented limitation).
- Turn-to-sound must be interruptible/serialized — a burst of audio events
  must not queue infinite yaws.
- `VisionSensor` currently evaluates every frame — actor-registry migration
  must keep the per-frame cost bounded before adding observers.
