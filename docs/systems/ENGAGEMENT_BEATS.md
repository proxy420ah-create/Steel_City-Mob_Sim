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
| `Rise` | `riseDuck` (solved, below) | arm on first positive probe → transitions | probe positive → `Engaged`; rise completes with zero contact → `Duck` |
| `Engaged` | stays at `riseDuck` | **free** — visible+converged+cadence+cylinder | dry → `Duck`; sustained `visible==0` → `Duck` |
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

## `Rise` — the triangulated pop-up

**Not** "duck − 1 band". Solved per-wall: the minimum rise such that **both**
posed anchors clear his own parapet — the user's caveat: the eye clearing
means nothing if the muzzle is still buried in the wall.

```
riseDuck = min duck scalar d where
    eyeY(d)    > wallTop + margin     (TryGetEyeAnchor at posed d)
 AND muzzleY(d) > wallTop + margin    (PosePoint the hand/mount group at d)
```

- `wallTop` = the picked wall's covered-band top (`CoverSensor.WallMark.top`
  at his own cover obstacle).
- Muzzle anchor: pose-evaluate a sample on the weapon/hand group at
  candidate `d` (same `PosePoint` machinery as `TryGetEyeAnchor` — the mount
  rides hand voxels).
- Solver: descend `d` from `targetDuck` in ~0.2 steps toward 0; first `d`
  clearing both anchors wins. ~6 FK evals once per Rise — cheap.
- Unsolvable (Full wall = no rise needed — he already stands clear;
  genuinely over-tall parapet = stays ducked, beat skips to `Duck`+`Settle`).
- During Rise the exposure probe keeps running at `visInterval`; first
  `visible>0` re-queries `LastSeenTarget` → `Engaged`.
- Marginal case: muzzle clears top but bore→centroid ray clips the parapet
  lip — accept `margin` as the pragmatic bound (tunable); exact bore-line
  clearance is a later refinement.

## `Engaged` — stand and deliver

User ruling: free authority. Once Rise confirms contact the exposure gate
runs exactly as built — he stays up and fires on
`visible && converged && cadence && cylinder`. Ends when:
- **dry** → `Duck` → `Settle` → `TopUp`(=reload, already staggered) →
  `Settle` → `Rise` — the rhythm loops; and/or
- **sustained `visible==0`** (target relocated/covered, not a dropped
  frame — e.g., >~0.5s of empty probes) → `Duck` → `Settle` → re-check.

## API surface (implementation sketch)

```csharp
// CombatController
enum EngagementBeat : byte { None, Settle, TopUp, Rise, Engaged, Duck }
public EngagementBeat Beat { get; private set; }
public float settleSec = 0.5f;             // the seam — one knob, every seam
public float emptyProbeSec = 0.5f;         // sustained-zero before Duck
float SolveRiseDuck();                     // the two-anchor solve above
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
  across `EnterCombat` calls.
- `Duck` beat restores `targetDuck` exactly — it is not `anim.duck += 1`.
- `TopUp` relies on `StartReload()`'s partial-wheel support — already built.
- `Engaged`'s sustained-zero test must be *sustained*, not instantaneous —
  a blink/drop-frame probe must not bounce him to `Duck` (`emptyProbeSec`).

## As-built deltas (implementation notes)

- `NextBeatAfter` shipped as the `afterSettle` field — same role (the seam's
  branch point), a per-transition variable rather than a static table. Every
  `Settle` exit reads it: arrival sets `TopUp`, `TopUp`-exit sets `Rise`,
  `Engaged`-dry sets `TopUp`, failure exits (`Engaged`-silent, unsolvable
  or empty `Rise`) set `TopUp` while `loopBeats` is on, else `None`.
- Beats tick **inside `InCover` only** — `HoldPosition` (no valid cover)
  keeps the plain always-engaged exposure channel; there is no parapet to
  rise over so the rhythm doesn't apply.
- **Failure exits loop while `loopBeats` is on**: `SolveRiseDuck()`
  returning −1, a rise expiring with zero contact, and `Engaged` losing
  sustained contact all set `afterSettle = loopBeats ? TopUp : None`
  before `Duck` — ON (default) the rhythm re-cycles `Duck → Settle →
  TopUp → Settle → Rise` until contact or exit; OFF he settles once and
  holds. HUD: `loop beats (re-rise after fail)` in combat mode. The
  dry-exit already self-loops through `TopUp`.
- **Inside the rhythm, `TopUp` owns the wheel exclusively**: the auto-start
  on dry (`cylinder<=0 → StartReload`) is gated to `State != InCover` — a
  dry `Engaged` ducks and settles *first*, reload begins only when the beat
  reaches `TopUp` (user: "shooting done → duck → settle drains → then we
  reload all 6 → settle drains → proceed"). The seat tick also holds a
  carryover reload mid-motion during any non-`TopUp` beat — `nextRoundLoad`
  defers by the pause so `ReloadLeft`/`NextRoundIn` stay honest. Manual
  possess reloads bypass the hold (`!PossessController.Active`) — the R key
  isn't rhythm-owned.
- `Rise` carries fire authority (`BeatAllowsFire`) — a positive probe during
  the rise can already arm/fire in the same frame `TickBeat` promotes him to
  `Engaged`; the gate order is `TickExposureFire` then `TickBeat`, so the
  transition lands immediately after the arm edge.
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
  `anim.duck`, not `targetDuck` — state 5 can only express duck>1.02, so a
  rise solving below that boundary must ride `Idle`+`icr()` or the rendered
  eye never actually clears (solve said clear, render stayed folded — the
  same contract bug class as the armed-gait fix).
- Panel: the beat lane renders `SETTLE` twice (the seam brackets the
  rise-check; both nodes light during any settle). A `beat <name> · <s>s
  left · riseDuck <d>` line sits under the cadence readout; `BeatLeft`
  exposes the Settle/Rise timers.
- Tunables shipped: `settleSec 5` (debug — every seam watchable; intended
  ~0.5 for production feel), `disarmHoldSec .35`, `riseHoldSec 1.2`,
  `emptyProbeSec .5`, `riseDuckEase 3/s`, `riseMargin .02`,
  `sightGap .045`, `muzzleFwd .3`, `loopBeats true` — all
  Inspector-exposed under "Engagement beats".
- **Disarm hold**: `wantAim` is per-probe, but stow now requires a *sustained*
  miss (`disarmHoldSec` since `LastSeenAt`) — a lone dropped probe no longer
  pump-stows/re-arms every `visInterval`. Authority loss (reload start, no
  weapon, beat channel closed) still stows instantly — the arm stays
  beat-owned.
- Panel `── settle ──` section: draining `▓░` bar + `Ns / Ss → next: <beat>`
  (surfaces `afterSettle`, the seam's branch point). `EXPOSED` node uses the
  same hold window so a dropped probe can't flicker it.
- **`LastRiseDiag` — rise attempts are no longer silent**: every solve stamps
  the verdict — `no wall mark on <vol> within 2m of stand`, `eye eval failed`,
  `unsolvable: wall <h> vs eye≤<h> muzzle≤<h>`, `cleared: duck <d> · wall <h>`,
  `rose clean — zero contact`, `contact → Engaged`. Panel `rise <diag>` line +
  `🥊` console log on refusal/expiry. A refused rise means "standing over this
  parapet is pointless" (eye AND muzzle can't both clear — full exposure, zero
  payoff), never "he can't stand up".
