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
| `Settle(d)` | cover hold at `targetDuck`; yaw **holds** the committed settle-face (back / ±90° side — never threat-facing; **zero motion** — the roll and the turn both live at the arriving beat) | **none — stowed** | timer → next beat |
| `TopUp` | cover hold — **yaw holds `settleYawDeg`** (works the cylinder facing his committed posture) | none (reloading anyway) | `!Reloading ∧ floor` |
| `Rise` | **full stand** (duck 0); yaw **coupled to the unfold** — `LerpAngle(riseFromYawDeg→engageYawDeg, smoothstep(duckProgress))` — turn-and-stand is ONE motion, not two eases | arm on first positive probe → transitions | `converged ∧ floor` → fresh probe: contact → `Engaged`; occluded → `Duck` |
| `Engaged` | stays at stand (duck 0); **yaw tracks the live body** (`contactSeen ? ThreatFeet() : belief`) | **free** — visible+converged+cadence+cylinder | dry → `Duck`; `dropAfterSec` of sustained empty probes → `Duck` (both queued behind floor) |
| `Duck` | `targetDuck`; **fold-back coupled the same way** — yaw `LerpAngle(foldFromYawDeg→settleYawDeg, smoothstep(foldProgress))` — collapse-and-turn as one motion | none | `folded ∧ floor` → `Settle` |
| `Swing` | step out = walking gait at `targetDuck`; dwell = unfold to `swingDuck` + `Looking` sweep **over the armed IK** aiming `AimAnchor` (aim-without-fire) | **aim yes, fire no** — arm presents, trigger stays beat-owned | contact → `Engaged`; dwell end → step back → `Duck` → seam |

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

**Settle owns no motion — it is a breath, not a beat** (implemented): the
facing roll lives at `CrouchAndCover`, which every PHYSICAL ARRIVAL calls
(seek landing, swing step-back, the off-spot return leg) — and the TURN
to that fresh facing is owned by the arriving beat, never the seam:
the seek's last move is `FaceHold(settleYawDeg, "arrive")` (arrival =
position ∧ facing — `InCover` doesn't begin until he converges), the
step-back's turn rides `Duck`'s `FaceCurve(foldFrom → settleYawDeg, FoldT)`,
and the off-spot return leg ends in a `FaceHold(settleYawDeg, "return")`
posture turn that converges before `Settle` starts. In-place seams
therefore never re-face: `EnterBeat(Settle)` only declares
`FaceHold(settleYawDeg, "settle")` — no roll, no new demand. Each arrival
re-rolls `PeekSide` from `settleFace` (`Random` → −1 left / 0 back /
+1 right, **excluding the side he just used** — the other two, equally;
fresh spots roll all three). Hiding isn't watching: the vision cone points
away during the seam, so perception honestly only happens during the
peek's turn-to-engage (`Rise` eases to `engageYawDeg` = bearing to
`AimAnchor()` while unfolding). `PeekSide` is the `Swing` beat's step-out
commitment — a committed side swings that edge; back-facing leaves either
side open (rolled at peek entry). `SolveRiseDuck` evaluates at
`engageYawDeg`, not the settle-facing yaw.

**Facing is a single driver, not a lane** (the unified rewrite): every
yaw intent is a *declared mode* — `Free` / `Hold(deg)` / `Track(point)` /
`Curve(from, to, t-source)` — and **one function (`TickFacing`, last call
in `Update`) renders ALL combat yaw writes**. Declaration sites decide
*what* heading; the driver decides *how* it moves; the roll
(`PeekSide`/`settleYawDeg`/`engageYawDeg`) is pure decision data and
never touches the transform. Beat → declaration:

| Beat / state | Declares |
|---|---|
| `Settle` | `Hold(settleYawDeg)` — **pure breath**: no roll, no new demand; the roll lives at `CrouchAndCover` arrivals, the turn at the arriving beat |
| `TopUp` / parked `None` | `Hold(settleYawDeg)` — the seam keeps the committed facing |
| `Rise` | `Curve(riseFromYaw → engageYawDeg, RiseT)` — coupled to the unfold |
| `Engaged` | `Track(contactSeen ? ThreatFeet() : AimAnchor())` per-tick |
| `Duck` | `Curve(foldFromYaw → settleYawDeg, FoldT)` — coupled fold-back |
| `Swing` steps | `Track(dest)` via `StepToward` (movement-facing, `dbgMoveYaw`) |
| `Swing` dwell | `Track(AimAnchor())` per-tick |
| `SeekingCover` | `Track(CoverPoint)` while moving (`dbgMoveYaw`); arrival = position ∧ facing — `Hold(settleYawDeg, "arrive")` converges before `InCover` |
| `Duck` return leg | `Track(CoverPoint)` walking, then `Hold(settleYawDeg, "return")` posture turn — converges before `Settle` |
| `HoldPosition` (no cover) | `Track(AimAnchor())` |
| `EnterCombat` / `ExitCombat` / toggle-off | `Free` — writes nothing |

`EnterBeat` calls `FaceFree()` first — every beat re-declares, so intent
never goes stale. The driver is deadbanded (|Δ| ≤ 0.5° ⇒ no write) and
`dbgFacing` gates the whole driver: off = **zero** combat yaw, guaranteed
— this replaced the old split (servo lane + beat-owned writes) where a
lane-level toggle could never cover the beat-owned sites, which is what
made the bisect toggle feel contaminated.

**Coupled transitions — turn rides the fold**: `Rise`/`Duck` declare
`Curve` modes — `EnterBeat` captures `(duck, yaw)` endpoints, and the
driver projects yaw onto the DUCK's live normalized progress
(`RiseT`/`FoldT` read `anim.duck` per-frame): `yaw = LerpAngle(from, to,
SmoothStep(t))`. One scalar is the clock; turn-and-stand (and
fold-and-turn-back) are a single motion — the old independent eases
(540°/s yaw vs 3/s duck) finished at different times and read as two
forces fighting. If this still doesn't read as authored, the deeper
version is a pose-space root-yaw channel swept through the animator with
the transform committing at t=1 (root-motion extraction) — this coupling
is the cheap probe of whether desync was the whole issue.

**Torso assist is a stationary refinement** (ArmAimSolver): while the rig
locomotes (`EffectiveState` Walking/AimWalk) `torsoAssist` is skipped —
the root owns heading on the move; a second heading driver on the same
spine (seek yaw vs `ikOverrides[0]`) was the navigation-time tug-of-war.
The arm still solves the residual error on the move — the opportunity-fire
channel is untouched.

## `Swing` — the lateral peek primitive (implemented)

The lateral counterpart of `Rise` for walls too tall to fight **over** —
he can still look **around** them. Entered from the Rise-refusal path
(`SolveRiseDuck < 0`, unscripted): the step-out destination is the zone's
farthest **Hidden** member along `PeekSide`'s lateral axis
(`CoverField.TryZoneEdge` — the safety boundary under this threat, not the
band's geometric edge) + `swingMargin` past it — step-out distance
adapts to the cover's painted width. Phases: **step out** (Walking gait,
duck scalar holds the fold; faces the actual step destination — off-axis
edges no longer moonwalk him) → **dwell** at the corner (`beatDwell[Swing]` ≈
one authored lookScan cycle; unfolds to `swingDuck` — the corner lean —
while `AnimState.Looking` physically sweeps the vision cone AND the arm
IK presents at `AimAnchor` = belief — aim-without-fire: the fire gate
carries `BeatAllowsFire` so the raised gun can't discharge; verdict =
cone `AnyVisible` on the live-fed threat point OR the aimed silhouette
probe — he is physically exposed while out) → **step back** into
`CoverPoint` → `Duck` → seam. Contact short-circuits to `Engaged` **at
the rim** — the return leg is then owned by `Duck`: when the fold lands
off-spot (> `arriveEps` from `CoverPoint`), he duck-walks back to the
centroid (`return` owner) BEFORE the seam holds — settling at the rim
would park him in the open. If no
zone edge exists the beat falls through to the old path (relocate →
duck). The stationary pose/duck lane skips `Swing` entirely — the beat
owns its poses, otherwise the lane re-requests the cover fold and stomps
the sweep every frame.

**Leg mechanics — stuck is not arrived** (`StepToward`, shared by the
swing steps, the `return` leg, and now `SeekingCover` itself): the step
guard's no-progress clock (`swingStuckPos`/`T`) is reset at each phase
boundary — a stale clock once declared instant-arrival at the rim (the
dwell had already consumed the 0.6s budget). When a leg stalls the
guard spends **unstick sidesteps** — ±perpendicular 0.28m hops,
alternating sides, ≤3 per leg — because `MoveWithCollision`'s slide is
greedy axis work (dominant component first) that can't wrap a convex
corner lip. Only an exhausted nudge budget reports "leg done," and
**callers re-verify distance**: `swing-p2` verified-arrival → roll at
`CrouchAndCover`; aborted → `Duck`'s return leg owns the rest. A return
leg abort logs `⚠️ return leg aborted Nm off centroid` and stands where
it ended — no fake arrival, no infinite grind. `SeekingCover` aborts
the same way → `⚠️ cover unreachable` → `HoldPosition` last-stand
fallback (same semantics as no-cover). **Arrival is exact**: entering
`arriveEps` (0.06m) takes the remaining sub-eps step as final
placement — "arrived" means ON the cell center, not inside a
tolerance (GOTCHAS #13: the eps is a stall bound, not the residual —
INT picks which point; footwork lands on it).

- `beatDwell[]` — per-beat dwell FLOOR indexed by `EngagementBeat`, all
  5s default (the debug tempo): every beat holds its seconds minimum and
  event verdicts stay honest inside — the timer gates the LEAVE, never
  the observation (TopUp still exits early if the cylinder's already
  full, Rise waits `dwell ∧ converged`, Engaged's exits queue behind the
  floor while `buriedSince` keeps counting, Swing's corner dwell rides
  `beatDwell[Swing]`). Editable live in the HUD **TEMPO** table.
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
- **`dropAfterSec` of sustained empty probes** (target relocated/covered —
  a CLOCK now, not a count, so it holds at any `visInterval` including
  per-frame probing) → `Duck` → `Settle` → re-check.

## API surface (implementation sketch)

```csharp
// CombatController
enum EngagementBeat : byte { None, Settle, TopUp, Rise, Engaged, Duck, Swing }
public EngagementBeat Beat { get; private set; }
public float[] beatDwell;                  // per-beat dwell floors (5s debug default)
public float dropAfterSec = 0.5f;         // sustained empty-probe seconds before Duck
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
- **lookScan integration** — the `Swing` peek primitive is LIVE (above);
  the search driver is the **ORBIT** (collapsed doctrine, 2026-10-22):
  ANY "peeked and saw nothing" verdict arms `pendingSearch`
  (`ArmSearch()`) — the Engaged drop (N empty probes), a zero-contact
  `Rise`, and a zero-contact `Swing` dwell are the same miss. The FIRST
  miss of an episode records the orbit frame: anchor = `AimAnchor`
  (belief — freshest sighting else the entry report, so a never-seen
  target still leaves a point to circle), origin bearing = where he
  stands, buffer radius = the standoff held at the loss — now the
  orbit's PREFERRED radius (the drawn ring IS the hot path: scoring hugs
  it, any depth near or far is legal), side = rolled CW/CCW (the only
  discrete decision). Each Settle seam consumes the flag via
  `TryOrbitStep`: the pick is the cheapest Hidden-verdict zone that
  advances the arc ≥15° on the rolled side (cost = ring-adherence
  |dAnchor−orbitRadius| + 0.15·travel; flow-field path cost is the
  declared upgrade) AND whose cover face POINTS AWAY from the anchor —
  `Dot(zone.outDir, anchor→stand) > +0.3` — outDir is the wall's outward
  normal into the stand area, so coherent cover runs parallel to
  anchor→stand; a zone hidden by accident
  (third-party occlusion) rather than by its own wall is fragile cover
  and doesn't form a coherent hop path. Legs are ROUTED: `BuildRoute`
  Dijkstra-floods the cover field's own cell grid (voxel-solid
  walkability — the same SamplePoint predicate MoveWithCollision
  enforces, no corner-cutting — and `routeRingBias` adds a soft per-cell
  cost for distance off the orbit ring so corridors hug the arc), then
  string-pulls the chain into waypoints the seek steps through: zones
  are the nodes, the flood is the corridor between them. No corridor →
  the straight ray stands in; a waypoint abort counts as a leg abort
  (spent zone, re-pick). `search_legN` markers draw the actual corridor
  polyline. Hop classes (`orbitReconEvery`, default 3): every Nth leg is a RECON — `CommitSearchSeek` →
  SeekingCover → the normal rhythm re-runs the peek (`Rise` vertical,
  `Swing` lateral on refusal); the rest are TRANSIT — land, brief
  settle, and the seam re-issues the next step without peeking
  (2 transit → 1 recon). Hop legs run the SCOUT posture: locally-Hidden ground keeps
  the fold at the local wall's `reqD`, open ground stands `duck→0` +
  `AimWalk` + arm presented FORWARD (feet + facing·2.5m — training the
  gun on belief mid-leg would twist the torso toward memory the whole
  walk; a probe resolve retargets the arm instantly); `dbgSearchLook`
  (default off) A/B-layers the head sweep. An aborted leg marks the zone
  spent and re-picks the next arc step from where he stands — only full
  arc exhaustion or a satisfied flank ends the episode. A mid-leg
  sighting rolls 50/50: continue to the committed cover vs
  `TryNearestCover` park + restart. `progress ≥ 90°` → the peek at that
  stand is the answer; a following miss exits combat → duties. Panel:
  `SEARCH ▸ ORBIT n°·(transit|recon) ▸ FLANK 90°` lane under the beats. Markers:
  `search_ref` (white, origin bearing) / `search_aim` (yellow, the 90°
  goal spoke) / `search_buf` (amber standoff ring) / `search_pick`,
  `search_leg`, `search_tgt` (cyan→orange as progress → 90°).
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
- The muzzle gate is **vertical lip clearance** — the cover-side question
  "can the presented bore ride over this parapet": the aimed muzzle sits
  `sightGap` (0.045m, sights sit above the bore) below the eye, so it
  must clear `wallTop + riseMargin` on its own — `eye − sightGap ≥
  wallTop + riseMargin`. It deliberately does NOT ray the path to the
  target: whether *this* shot reaches is the fire gate's truth
  (`BoreBuried` on the real welded muzzle at trigger time), and failing
  a stand because the *target* sits behind the cover conflated a
  positional problem with a cover verdict (a stand you can see over and
  raise the gun over IS fightable — an occluded line just means relocate
  or swing). Target presence picks the verdict tier: `LastSeenTarget` →
  live threat; none = eye-only solve (a look, not a shot). Diag strings:
  `can't see over` / `bore under lip` / `cleared` — eye-vs-bore failures
  are distinct.
- The stationary pose pick (`Crouching` vs `Idle`) follows the **live**
  `anim.duck`, not `targetDuck` — state 5 can only express duck>1.02. With
  firing posture now fixed at stand, Rise/Engaged always ride `Idle`→`Aiming`;
  the Crouching band is cover/seam posture only.
- Panel: the beat lane renders `SETTLE` twice (the seam brackets the
  rise-check; both nodes light during any settle). A `beat <name> · <s>s
  left · riseDuck <d>` line sits under the cadence readout; `BeatLeft`
  exposes the active beat's dwell countdown (every beat is timed now).
- Tunables shipped: `beatDwell[]` all-5s (debug tempo — HUD **TEMPO —
  beat lengths** table edits each live, ±0.5s step; intended ~0.5 for
  production feel), `disarmHoldSec .35`, `dropAfterSec .5`,
  `riseDuckEase 3/s`, `riseMargin .02`, `sightGap .045`, `pinRecheckSec 4`,
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
  point is the zone's centroid — owned by the QUALITY layer** (user model:
  "each cover position exposes a stand-point centroid from the area of
  its cover color gradient" → refined to "the broader measurement owns the
  centroid; threat simply determines which one he selects"): `BuildZones()`
  flood-fills the **claimed quality band** (`volIdOf` set, `quality ≥
  SeedMin`) once per Build into connected components keyed on
  `(volIdOf, clsOf, reqDepthOf ÷ ¼-duck, outDirOf)` — the face-normal key
  splits a box's corner-wrapped bands into per-face **rectangles**, and the
  stand is the band's **geometric center**: the owning columns' face
  extent (`wallIdxOf` → `facePos`/`vs`) projected out by the band depth —
  the wireframe box's middle, immune to raster asymmetry — so the centroid
  is threat-independent (it can't drift on belief updates — only legality
  changes). The threat layer gates MEMBERS:
  `TryRankInZone`/`TryZoneEdge` read Hidden members only; a zone whose
  stand cell isn't Hidden under the current threat still posts (dimmed) —
  the physical offer vs the verdict are now visibly distinct. Each
  `ZoneSpot` carries `stand` (the member cell nearest the centroid), `volId`,
  `outDir`, mean `reqD`, `cls`, `cells`. The relocate candidate loop iterates
  ZONES (not wall-face marks): each zone resolves its **own face's** nearest
  mark (outDir-matched — a same-volume mark on another face would solve the
  wrong wall top), re-validates `TryZoneAt` at the stand, and runs
  `SolveRiseAtSpot` there — the verdict applies to where he will actually
  stand, so a "cleared" can't disagree with arrival. The committed
  `CoverPoint` is a cached Vector3 — no per-frame recompute, nothing
  rewrites it mid-seek — which is what makes the relocate atomic: the only
  SeekingCover→InCover transition is `dist ≤ arriveEps` at that point.
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
- **TEMPO table — per-beat dwell floors** (replaced the scripted-director
  slot UI): the HUD `TEMPO — beat lengths` fold lists every beat
  (settle/topup/rise/engaged/duck/swing) with a live `▶ <beat> · Ns left`
  marker on the active row and `−`/`+` adjusters (±0.5s, 0.5–30 range) —
  tune the debug tempo while a duel runs. `refresh cover spots` + `show`
  still enumerate/paint the zone centroids (`cov_{i}` beams); `scr_tgt`
  draws feet→target while relocating. The scripted director's code path
  (`scriptedMode`, `scriptSlots`, `ScriptedGoTo`) remains in the
  controller for programmatic use — only its slot UI was retired.
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
  `can't see over: wall <h>+<m> vs eye≤<h>`, `bore under lip: muzzle <h> <
  wall <h>+<m>`, `cleared: stand · wall <h>`, `risen — zero contact`,
  `contact at stand → Engaged`. Panel `rise <diag>` line +
  `🥊` console log on refusal/expiry. A refused rise means "standing over this
  parapet is pointless" (eye or presented bore can't clear the lip — full
  exposure, zero payoff), never "he can't stand up".
- **Refusal verdict cascade — swing → relocate → PINNED**: a refused rise first
  tries the `Swing` lateral peek; a second refusal at the same stand (the swing
  burned its look) relocates to fighting cover first. When ALL exits fail —
  can't fight over, no rim to peek around, no fightable zone anywhere — the
  stand is **hide-only for this fight**: `pinned` parks him at `Beat.None`
  (head down, cover held, arm stowed) and re-arms ONE real rhythm cycle every
  `pinRecheckSec` (4s). A real rise is a real probe, so a moved threat or a
  cleared bore wakes the rhythm honestly; a re-refusal re-pins. The same
  verdict covers the `Engaged` sustained-burial relocate-fail path. This is
  the terminal answer — "pinned down" is a legitimate combat outcome, not a
  stalled machine.
