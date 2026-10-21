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
- The exposure probe stays combat-scoped and **not gated on the cone**: in
  combat the shooter already knows the threat; the cone remains for
  notice-level questions and the search phase.
- Roles feed combat metadata: `role = attacker | defender` for reports/heat.

## Search & resolution (deferred — user has the 3-step pattern papered)

- Inputs: `LastSeenTarget` (implemented), awareness-table `lastSeenPos`.
- The authored lookScan sequence drives the cone over the last-seen region:
  sweep → dwell → wider sweep (exact 3 steps per user design — do not invent).
- Outcomes: redetect → re-engage or resume tabs; decayed/absent →
  return-to-normal, awareness entry marked stale.

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

- The 3-step search pattern content (user-papered; do not invent).
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
