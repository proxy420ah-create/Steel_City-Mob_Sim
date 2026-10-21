# Engagement Beats — Authored Combat Rhythm

**Status**: ✅ IMPLEMENTED — `CombatController.EngagementBeat` + `TickBeat()`;
F5 panel renders the third lane live. See "As-built deltas" for spec→build notes.
**Depends on**: EXPOSURE_ENGAGEMENT (gates), COMBAT_COVER_STATE (locomotion/cover)
**User direction**: "arena combat's first move is seeking cover; shots during
the approach are opportunity fire — covering fire, NOT an attack state. Arrival
→ settle → top-up → settle → rise to check lastSeen → stand and deliver."

## The doctrine

Geometry decides what is *possible*; **beats decide what he is doing**. The
exposure probe, convergence, cadence, and cylinder gates from
EXPOSURE_ENGAGEMENT stay exactly as built — this layer sequences intent above
them, replacing "fire whenever the geometry allows" with an authored rhythm:

```
SEEK ── OpportunityAdvance (covering fire channel — suppression, not attack)
  → arrival → SETTLE ───────────── the reusable seam: parameterized dwell
              ↓
  → TOP-UP (seat whatever opportunity fire spent; partial wheel —
            empty wheel cost skipped = the "weapons check" placeholder beat)
  → SETTLE
              ↓
  → RISE — deliberate pop-up; re-query against lastSeenTarget
      samples visible → ENGAGED
      nothing visible → Duck → SETTLE → (retry policy — DEFERRED)
  → ENGAGED — "stand and deliver": exposure gate runs freely until
      cylinder dry or target gone
  → dry/loss → DUCK → SETTLE → RELOAD → future branch point
```

## Beat list

| Beat | Posture | Fire authority | Exit |
|---|---|---|---|
| `OpportunityAdvance` | approach duck gait (existing) | armed+fire when voxels resolve (existing channel — semantic label only) | cover arrival |
| `Settle(d)` | cover hold at `targetDuck` | **none — stowed** | timer → next beat |
| `TopUp` | cover hold | none (reloading anyway) | `!Reloading` (cylinder full) |
| `Rise` | **full stand** (duck 0) | arm on first positive probe → transitions | pose converged → fresh probe: contact → `Engaged`; occluded → `Duck` |
| `Engaged` | stays at stand (duck 0) | **free** — visible+converged+cadence+cylinder | dry → `Duck`; `emptyProbesToDrop` consecutive empty probes → `Duck` |
| `Duck` | `targetDuck` | none | → `Settle` |

`OpportunityAdvance` is not a stored beat — it IS `SeekingCover` with the
existing armed channel; the doctrine rename matters for later legality/
witness semantics (suppression vs initiated attack — PERCEPTION_AWARENESS).

## `Settle` — the seam primitive

`Settle(duration)` is THE reusable inter-beat dwell — one mechanism, every
seam. During it: posture = cover hold, `wantAim` forced off (a beat owns the
arm, not the exposure probe). Its **exit is the decision point**: the
`NextBeatAfter` table/function selects the following beat — today it's the
fixed rhythm above; the planned "branch of actions" (reload | re-check |
reposition | lookScan) plugs in at exactly this point without touching beat
mechanics.

- `settleSec` default ~0.5s, Inspector-tunable.
- Transitions emit `Changed` — the panel lane lights each beat live.

## `Rise` — the atomic pop-up (full stand)

**Firing posture is uniform: full stand, duck 0.** The variable-duck search
(minimum shared duck clearing both anchors) is suspended — the posture split
is now binary and legible: **ducked = in cover, upright = attacking**. The
solve is a single stand-height verdict per wall:

```
stand over his own parapet iff
    eyeY(stand)    > wallTop + riseMargin   (TryGetEyeAnchor, full stand)
 AND bore ray free: muzzle→target unblocked (muzzle = eye−sightGap,
    pushed muzzleFwd toward target, floored at the lip)
```

- `wallTop` = the picked wall's covered-band top (`CoverSensor.WallMark.top`
  nearest his stand on his cover obstacle).
- `SolveRiseAtSpot(stand, yaw, wallTop, tgt, fwdHint, out verdict, out
  bestEye)` — one FK eval; verdict codes 0 cleared · 1 eye-eval-fail ·
  2 eye can't clear · 3 bore blocked · 4 cleared eye-only (no target).
- **Atomic completion — no recon window.** The beat ends when the pose
  converges (`|anim.duck − riseDuck| < 0.02`, same epsilon the `Duck` beat
  uses): a FRESH `TryGetVisibleVoxels` probe is taken at that instant —
  `visible > 0` → `Engaged`; `0` → "risen, zero contact (target occluded)"
  → `Duck` → seam. `riseHoldSec`/`riseUntil` are gone — the deadline
  started at beat entry while the head was still below the lip, so "rose
  clean" could fire before the eye was physically up.
- Unsolvable (over-tall parapet / no wall mark / bore buried at every
  stand height) → `Duck`+`Settle` — or the relocate branch when one exists.
- Marginal case: muzzle clears top but bore→centroid ray clips the parapet
  lip — accept `riseMargin` as the pragmatic bound; `BoreBuried` (the real
  welded muzzle ray) remains the post-rise truth.

## `Engaged` — stand and deliver

User ruling: free authority. Once Rise confirms contact the exposure gate
runs exactly as built — he stays up and fires on
`visible && converged && cadence && cylinder`. Ends when:
- **dry** → `Duck` → `Settle` → `TopUp`(=reload, already staggered) →
  `Settle` → `Rise` — the rhythm loops; and/or
- **`emptyProbesToDrop` consecutive empty probes** (target relocated/covered,
  not a dropped frame — event-count, not a clock) → `Duck` → `Settle` →
  re-check.

## API surface (implementation sketch)

```csharp
// CombatController
enum EngagementBeat : byte { None, Settle, TopUp, Rise, Engaged, Duck }
public EngagementBeat Beat { get; private set; }
public float settleSec = 0.5f;             // the seam — one knob, every seam
public int emptyProbesToDrop = 4;          // consecutive empty probes before Duck
float SolveRiseDuck();                     // the stand-height verdict above
void EnterBeat(EngagementBeat b);          // +Changed on every transition
```

`wantAim` gains a beat term: `beatAllowsFire` — `OpportunityAdvance`/`Rise`
(exposure-gated) and `Engaged` (free) true; `Settle`/`TopUp`/`Duck` false.
The beat lives inside `InCover`/`HoldPosition`; `SeekingCover` stays as-is.

## Panel / flow-chart surface

Third lane on the F5 chart — the beat row, same lit-node rendering:

```
SEEK ▸ SETTLE ▸ TOPUP ▸ SETTLE ▸ RISE ▸ ENGAGED ▸ DUCK ↺
```

Lit amber while `Settle` counts down (show `settle s left` inline),
`RELOAD` dots already live on the fire lane and tick during `TopUp`.

## Deferred (explicitly NOT this build)

- **Pop-out firing (the side-exit branch)** — the *relocation* half shipped:
  a refused rise re-picks a different hidden-validated face whose solve
  clears and re-seeks to it (below). Still unbuilt: stepping/leaning around
  a wall's END when NO face anywhere clears — fire around the corner
  instead of over the top. `can't see over`/`sees but can't shoot` verdicts
  already mark the trigger.
- **Rise retry cadence** — rise→nothing→settle→rise-again cycle: held until
  Tan gains mobility (per user: "wait till i roll out the next phases after
  we get tan suit moving"). Single rise-check only this build.
- **Interruption policy** — stray LOS during `Settle`/`TopUp` (Tan strays
  into view mid-top-up): held for the same mobility phase. Beats hold.
- **lookScan integration** — Rise finding nothing is where the 3-step
  visual search slots (PERCEPTION_AWARENESS).
- **Tan-side beats** — Tan runs the same stack in AI-duel; his mobility/
  response behavior is a separate phase.
- **Branch table** — `NextBeatAfter` is fixed this build; the decision
  function (threat moved? ammo low? wounded? flee?) is the next phase.
- **Weapons-check flourish** — `TopUp` with a full wheel currently just
  passes the seam through; a real inspect-the-cylinder beat is a later
  animation/pose concern.
- **Per-weapon cylinder profiles** — capacity/interval per weapon when the
  arsenal grows; today the Model 10's numbers are the gun.

## Traps

- **The arm is beat-owned now**: `ExposureArmed`/`wantAim` must AND with the
  beat's fire authority — a stray positive probe during `Settle` must NOT
  pop the arm up. This is the single biggest regression risk vs today's
  "any exposure = arm" logic.
- `riseDuck` is per-**wall** — re-solve on every cover re-pick; don't cache
  across `EnterCombat` calls. (Currently always `0` when solvable — firing
  posture is full stand; the per-duck search stays suspended until a
  graduated rise returns.)
- `Duck` beat restores `targetDuck` exactly — it is not `anim.duck += 1`.
- `TopUp` relies on `StartReload()`'s partial-wheel support — already built.
- `Engaged`'s drop must be *sustained*, not instantaneous — a blink/drop-
  frame probe must not bounce him to `Duck` (`emptyProbesToDrop`
  consecutive empties, counted per probe tick).

## As-built deltas (implementation notes)

- `NextBeatAfter` shipped as the `afterSettle` field — same role (the seam's
  branch point), a per-transition variable rather than a static table. Every
  `Settle` exit reads it: arrival sets `TopUp`, `TopUp`-exit sets `Rise`,
  `Engaged`-dry sets `TopUp`, failure exits (`Engaged`-silent, unsolvable
  or empty `Rise`) set `TopUp` while `loopBeats` is on, else `None`.
- Beats tick **inside `InCover` only** — `HoldPosition` (no valid cover)
  keeps the plain always-engaged exposure channel; there is no parapet to
  rise over so the rhythm doesn't apply.
- **`loopBeats` is a LIVE gate at the post-Duck seam, not an entry-time
  read**: every `Duck` entry arms `loopGate`; the settle that follows a
  duck is the re-cycle gate and decides park-vs-restart *when it drains*
  — a mid-seam toggle takes effect. All four duck exits (dry, refused,
  empty, contact-loss) write `afterSettle=TopUp` unconditionally; the
  gate converts it to `None` when off. The dry exit is gated too — loop
  OFF = run the rhythm once and park, even mid-cycle. Parked at the gate
  sets `awaitingLoop`: a live toggle-ON restarts `Settle→TopUp→…` from
  the `None` case. `NextAfterSettle` previews the gated verdict. HUD:
  `loop beats (re-rise after fail)`, pre-fight — not combat-gated.
- **Inside the rhythm, `TopUp` owns the wheel exclusively**: the auto-start
  on dry (`cylinder<=0 → StartReload`) is gated to `State != InCover` — a
  dry `Engaged` ducks and settles *first*, reload begins only when the beat
  reaches `TopUp` (user: "shooting done → duck → settle drains → then we
  reload all 6 → settle drains → proceed"). The seat tick also holds a
  carryover reload mid-motion during any non-`TopUp` beat — `nextRoundLoad`
  defers by the pause so `ReloadLeft`/`NextRoundIn` stay honest. Manual
  possess reloads bypass the hold (`!PossessController.Active`) — the R key
  isn't rhythm-owned.
- `Rise` carries fire authority (`BeatAllowsFire`) — a positive probe mid-
  transition can already arm him before the convergence verdict fires; the
  gate order is `TickExposureFire` then `TickBeat`.
- The muzzle gate is the **bore line anchored to the sight line** — aimed
  fire aligns the sights to the eye, so the presented muzzle is
  `eye − sightGap` (0.045m, sights sit above the bore) pushed `muzzleFwd`
  (~0.3m) horizontally toward `LastSeenTarget`/threat; require
  `!sensor.LosBlocked(muzzle, tgt)` — the bullet path must be free on the
  same world collision the probe uses. Two earlier models measured the
  wrong thing: the rest-pose wrist pivot sat below the real presented
  height (wall 0.70 vs eye 0.76 clears, pivot 0.68 fails — refused while
  the shot path was free), and a hand+forward-reach ray stayed descending
  under any top (no forward reach fixes a descending bore). Target
  priority: `LastSeenTarget` → live threat; none = eye-only solve (a look,
  not a shot). Diag strings: `can't see over` / `sees but can't shoot` /
  `cleared` — eye-vs-bore failures are distinct.
- The stationary pose pick (`Crouching` vs `Idle`) follows the **live**
  `anim.duck`, not `targetDuck` — state 5 can only express duck>1.02. With
  firing posture now fixed at stand, Rise/Engaged always ride `Idle`→`Aiming`;
  the Crouching band is cover/seam posture only.
- Panel: the beat lane renders `SETTLE` twice (the seam brackets the
  rise-check; both nodes light during any settle). A `beat <name> · <s>s
  left · riseDuck <d>` line sits under the cadence readout; `BeatLeft`
  exposes the Settle timer (Rise is event-bound — no clock).
- Tunables shipped: `settleSec 5` (debug — every seam watchable; intended
  ~0.5 for production feel), `disarmHoldSec .35`, `emptyProbesToDrop 4`,
  `riseDuckEase 3/s`, `riseMargin .02`, `sightGap .045`, `muzzleFwd .3`,
  `loopBeats true` — all Inspector-exposed under "Engagement beats".
- **Disarm hold**: `wantAim` is per-probe, but stow now requires a *sustained*
  miss (`disarmHoldSec` since `LastSeenAt`) — a lone dropped probe no longer
  pump-stows/re-arms every `visInterval`. Authority loss (reload start, no
  weapon, beat channel closed) still stows instantly — the arm stays
  beat-owned.
- Panel `── settle ──` section: draining `▓░` bar + `Ns / Ss → next: <beat>`
  (surfaces `afterSettle`, the seam's branch point). `EXPOSED` node uses the
  same hold window so a dropped probe can't flicker it.
- **The welded muzzle ray is the runtime truth — the buried-bore verdict**:
  the rise solve's `eye − sightGap` MODEL can say `cleared` while the real
  welded muzzle (`mount.MuzzleWorld` — what the red debug ray emits) still
  kisses the lip. Two uses: (a) the fire gate adds `!BoreBuried(aimPoint)`
  — a converged-into-the-wall bore never wastes a round into brick; (b)
  inside `Engaged`, `solver.Blend≥0.7 && LosBlocked(muzzleWorld, aimPoint)`
  sustained `boreBurySec` (0.4s) = an unfightable-wall verdict → stamps
  `bore buried at the wall — real muzzle ray` and runs the same
  relocation path as a refused rise. The arm itself is never gated on it
  (no arm-flap — the verdict changes the COVER, not the pose).
- **Relocation branch — a refused rise re-picks FIGHTING cover**: the
  spec's `reload | re-check | reposition` seam gains its third option.
  `SolveRiseDuck` was split into `SolveRiseAtSpot(stand, yaw, wallTop,
  startDuck, tgt)` — an arbitrary-stand evaluator (the posed eye anchor
  re-anchors to the candidate spot: local offset yaw-unrotated, carried
  forward). `TryRelocateToFightingCover()` on a refused rise: marks the
  failed FACE (position key, not volId — one volume carries faces at
  different heights), then scans `sensor.walls` for the nearest face that
  is both hidden-validated (`TryZoneAt` — the same truth the zone paints)
  AND rise-solvable, re-targets `CoverPoint`/`CoverObstacleId`/`targetDuck`
  (the lower wall = deeper cover duck — auto duck-gait on approach) and
  re-enters `SeekingCover` — the whole rhythm replays at the new spot.
  No fightable face → falls back to the seam gate as before. Triggered
  from two places: a refused rise (solve says no) AND a buried-bore
  verdict inside `Engaged` (solve said yes, the real muzzle ray says no).
- **Stand-point centroids — a cover position is a ZONE, and its stand
  point is the zone's centroid** (user model: "each cover position
  exposes a stand-point centroid from the area of its cover color
  gradient"): `CoverField.EnumerateZones()` flood-fills Hidden cells into
  connected components keyed on `(volIdOf, clsOf, reqDepthOf ÷ ¼-duck)`
  — a stepped terrace yields one zone per step, a corner overlap splits
  per volume. Each `ZoneSpot` carries `stand` (the member cell nearest
  the raw centroid — visually identical, guaranteed inside the paint
  even for concave zones), `volId`, mean `reqD`, `cls`, `cells`. The
  relocate candidate loop iterates ZONES (not wall-face marks — the
  earlier per-face anchoring produced ~7 spots on a 4-step terrace,
  biased off-band): each zone borrows its owner volume's nearest face
  mark only for the wall top the solve needs, re-validates `TryZoneAt`
  at the stand, and runs `SolveRiseAtSpot` there — the verdict applies
  to where he will actually stand, so a "cleared" can't disagree with
  arrival. The committed `CoverPoint` is a cached Vector3 — no per-frame
  recompute, nothing rewrites it mid-seek — which is what makes the
  relocate atomic: the only SeekingCover→InCover transition is
  `dist ≤ arriveEps` at that point.
  `LastRelocateDiag` stamps `→ <volId> stand@(x,z) · duck <d>` /
  `refused — n face(s) failed` — surfaced as a `relocate` panel line.
- **Relocate is a named phase, not a stealth re-seek** (user model: "the
  initial-seek primitive, reused — duck-gait move, opportunity shots while
  reloaded, then settle and restart the rhythm"): `Relocating` is set on
  the re-pick, cleared at arrival — the SEEK lane relabels **RELOCATE**
  live. The opportunity channel stays armed during the move
  (`BeatAllowsFire` covers `SeekingCover`) — he can fire mid-relocation
  because `TopUp` usually topped the wheel before the failed rise.
  Relocation hygiene: `buriedSince`, `loopGate`, `anim.coverContext` all
  reset — EnterCombat fresh-seek parity, nothing stale steps on the
  sequence.
- **Scripted engagement director — user-authored cover orders**: a debug
  tool for studying movement sequences. HUD `SCRIPT — engagement director`
  fold (TEST LOTS): a `refresh cover spots` button enumerates every
  painted cover zone (`EnumerateZones` — the same one-spot-per-section
  stand point relocation uses), sorted nearest-first; `show` paints a
  post at every centroid (`cov_{i}` beams — dim cyan free, slot-colored
  when assigned). Eight
  dropdown slots each hold one order: `{volId} stand@(x,z) cls d{req}`.
  With `scripted — combat waits for slot orders` ON, `EnterCombat` skips
  the auto pick — he holds at spawn until slot 1 fills (the first order
  is a normal `SeekingCover`, later slots show `RELOCATE`). Each slot
  commits through `ScriptedGoTo` — identical atomicity to a fighting-cover
  relocation: cached stand point, beat state reset, full
  `SETTLE→TOPUP→SETTLE→RISE→ENGAGED` replay on arrival. **Advance rule**:
  filling the NEXT slot is the order — it fires the moment the current
  stage reaches a verdict (`ScriptStageDone` = first `Engaged` contact OR
  any `Duck` entry: refused/empty/buried/dry/lost) — never mid-move, so
  slots can't step on each other. In scripted mode auto-relocation is
  suppressed (`TryRelocateToFightingCover` returns false + counts the
  stage done) — the script owns position. Slots persist across
  `ExitCombat`/`START DUEL` iterations — same script, re-runnable.
  `cursor: slot n · stage done|running` readout; `scr_tgt` beam draws
  feet→target while relocating.
- **Threat upstream of combat — the channel has two owners, split by
  mode, never thrashing**: OUT of combat `pickedAim` is the shared
  awareness channel — a per-frame sync pushes it into Vinny's
  `CoverSensor.Threat` (forced refresh on change), resolved fresh from
  `SpawnedCharacter` each frame rather than the field the COVER tab
  lazily assigned — previously the channel only flowed if that tab had
  drawn once, so a staged threat never painted zones on the COMBAT tab
  (`0/0 hidden`). Now SETUP PIT's `pickedAim = tanFeet` arms the sensor
  next frame regardless of mode/tab — zones exist at setup, combat just
  consumes them, and `ExitCombat`'s clear is re-armed by the sync
  (threat persists past disengage — honest awareness). IN combat the
  controller owns the feed instead: `ThreatPos` tracks
  `ThreatCharacter`'s LIVE feet each frame and feeds `Sensor.Threat`,
  so zones re-paint at the scan cadence as the threat moves — the
  pickedAim sync stands down while combat is active so the two feeds
  never fight. The director's `refresh cover spots` never arms the
  threat (it just re-scans geometry); 0 spots honestly reports "no
  threat armed" or "no hidden zones in range".
- **`LastRiseDiag` — rise attempts are no longer silent**: every solve stamps
  the verdict — `no wall mark on <vol> within 2m of stand`, `eye eval failed`,
  `unsolvable: wall <h> vs eye≤<h> muzzle≤<h>`, `cleared: duck <d> · wall <h>`,
  `rose clean — zero contact`, `contact → Engaged`. Panel `rise <diag>` line +
  `🥊` console log on refusal/expiry. A refused rise means "standing over this
  parapet is pointless" (eye AND muzzle can't both clear — full exposure, zero
  payoff), never "he can't stand up".
