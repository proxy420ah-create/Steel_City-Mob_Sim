# Exposure-Based Engagement — the crossfire model (Combat Phase 2)

**Created**: Oct 20, 2026
**Status**: ✅ IMPLEMENTED (Oct 20) — pop-up trigger, possess binding, search
pattern remain deferred below
**Depends on**: COMBAT_COVER_STATE.md Phase 1 (implemented), COVER_HEIGHTFIELD.md,
PROJECTILE_SYSTEM.md, ANIMATION_SYSTEM.md §6 (transitions), §8 (duck scalar)

---

## Goal

Replace verdict-flag fire control with **geometry-derived exposure**: a combatant
can IK-aim and fire exactly while some part of the enemy's *live posed silhouette*
is visible from his own *posed eye* — and what he aims at is the **centroid of the
visible set**, not a bone or a `feet + constant` point.

The doctrine beats (user-authored):

1. Combat starts → Vinny advances toward the picked cover (combat-owned duck gait).
2. While Tan's voxels are visible, he aims with IK and fires — including mid-duck
   over his own parapet.
3. As the approach tucks him in, his posed eye drops below his own wall → visible
   set decays to empty → IK releases → **crouch-idle, aware of `lastSeenTarget`**.
4. Explicit **pop-up** (the one authored override) stands him to re-engage —
   re-resolves to the live centroid if the enemy still shows.
5. **Six shots** (S&W Model 10 cylinder) → duck to reload — crouch-idle is the
   reload beat until a reload anim exists.

## The core primitive — `TryGetVisibleVoxels`

New query, spec target: `CoverSensor` (it already owns the ray world, tick
cadence, and threat context).

```csharp
// As built — the eye resolves internally (posed TryGetEyeAnchor, chest fallback):
public struct VisibleSet {
    public int visible, tested;
    public float fraction;
    public Vector3 centroid;
    public VoxelCharacter target;
}
public bool TryGetVisibleVoxels(VoxelCharacter target, out VisibleSet set);
public VisibleSet LastVisible { get; }   // combat reads this between probes
public Vector3 LastProbeEye { get; }     // where the rays left from (viz/HUD)
```

### Sample generation — posed silhouette points (as built)

- `VoxelCharacter.SampleSilhouetteWorld(Vector3[] dst)` — a one-time-built
  `(gid, rest)` list posed through `PosePoint` under the LIVE instanced state
  (state/time/duck/gait/transition — same params `TryGetJointWorld` reads),
  world-mapped via the shared `CellToWorld` (`PoseToWorld` was refactored onto
  it so the two paths can't drift).
- The set: per pose group, Y-sorted voxels → `clamp(⌈count/8⌉, 2, 8)`
  evenly-spaced picks — vertical coverage of every group scaled with its
  voxel count (~30–60 points). Simple strided picks, not analytic extremes —
  coverage, not minimal-optimal.
- Evaluated **fresh each query** on the target's live pose — a ducked or
  crumple-walking target exposes a different silhouette.
- No GPU readback — instanced characters keep the CPU eval path authoritative
  for logic.

### Ray semantics — the honest caveat (accepted)

**Characters are not in `VoxelCollisionWorld`.** The ray world holds terrain,
buildings and `veh_*` vehicles; a `VoxelCharacter` only *queries* it. So the
test is:

> `Raycast(eye → sample) unobstructed` ⇒ that world-space point is reachable ⇒
> the sample counts **visible**.

Consequences, accepted:

- **No self-occlusion** — a far-side sample counts visible through where the
  target's own body would be. Mild overcount; centroid biases slightly toward
  body-center, which is harmless-to-helpful for hits (center-mass is the aim).
- **The shooter's own wall blocks naturally** — his cover IS in the world, so
  tucking below the parapet starves the rays. That's the whole mechanism.
- If a truer surface-only centroid is ever wanted: restrict samples to the
  near-side facing set, or register a coarse character proxy volume. Deferred.

### Cost / cadence

~40 `PosePoint` evals + ~40 world raycasts per query — runs on the sensor's
existing tick (0.4s / move-threshold), not per-frame. Duel scale trivial; squad
scale gets a note (share the shooter's tick; N shooters × M targets later).

## Firing doctrine — the gates (as built)

| Gate | Source | Role |
|---|---|---|
| Posture | `ArmAimSolver` — gate **REMOVED** per user ruling | any pose can fire if the IK resolved. The one carved-out consequence: at `duck > 1.02` the solver drops *torso assist* inside `Solve` (its yaw would un-fold the crouch lean — `IK_TORSO_OVERRIDE` bound), arms-only reach. |
| Exposure | `visibleCount > 0` ∧ `ConeCovers(centroid)` → `canSee` | THE contact gate — geometric unocclusion alone is not detection; the vision cone supersedes all aiming (PERCEPTION_AWARENESS → detection stack) |
| Convergence | `solver.Blend ≥ 0.7` + `LastAngularErrorDeg ≤ aimGateDeg` (14°) **held** `fireSettleSec` (0.15s) | shots only leave while the bore's actually on — the error channel is a frame stale, so a retarget must persist inside the gate (`convergeSince` latch); acquisition edges re-arm it |
| Cadence | `fireInterval` (0.9s default) | max trigger rate |
| Ammo | `cylinder` = 6 (Model 10) → `roundLoadInterval` (1.5s) | empty → `Reloading` = arm dips while ONE chamber seats per interval — swing-out model, can't fire mid-load — full wheel → re-engage |

`coverContext` keeps its *posture* job (drive duck, pick pose) and leaves the
firing job — the Phase-1 equivalence is preserved emergently: fully covered ⇒
own wall eats every ray ⇒ zero visible ⇒ stowed.

## The aim target

`centroid` of the visible set feeds `solver.Arm(centroid)` — re-issued each
visibility tick so the aim point tracks the exposed cap as poses change.
Replaces `CharFeetCenter + up*1.3f` in the pit harness (that constant was a
1.7m-human chest on a 0.62m character — aiming ~0.7m over Tan's head).

Emergent: at partial duck only the enemy's head band shows → centroid = head —
**parapet fire is precision fire**. Accepted (user): recoil + sway + tiny voxel
count keep it honest. Standing = full silhouette = volume fire.

## AI fire loop (first one — Phase 1 had none; as built)

`CombatController.TickExposureFire()` runs every non-Idle frame (before the
pose pick so `ExposureArmed` selects the lane):

```
probe cadence (visInterval 0.12s) → Sensor.TryGetVisibleVoxels(ThreatCharacter)
canSee = visible>0 ∧ ConeCovers(centroid)   ← cone supersedes: out-of-fan = undetected
wantAim = (canSee && clearToArm) || peekAim (rise/swing → belief) || searchAim (scout → forward)
  arm point: live centroid | AimAnchor belief | forward — one IK channel, two modes
fire → canSee && converged-HELD (Blend≥0.7 && err≤gate for fireSettleSec)
     && cadence && cylinder>0 && !BoreBuried
     → ProjectileSystem.Fire(mount, err, centroid); cylinder--
     → cylinder==0 → reloading=true → nextRoundLoad = now+1.5s
        → cylinder++ each interval → full → reloading=false → re-engage
pose lanes: SeekingCover → AimWalk vs Walking; InCover/Hold → Aiming vs Crouching/Idle
```

`BeatAllowsFire` = the beat layer's fire authority (see
`ENGAGEMENT_BEATS.md`): `SeekingCover`/`HoldPosition` always-allowed,
`InCover` only during `Rise`/`Engaged` — the arm is beat-owned, a stray
positive probe during `Settle` never pops it up. `InCover` also hands the
`anim.duck` write to the beat (`riseDuck` while risen, `targetDuck` in
seams, eased at `riseDuckEase`).

**Cover posture overrides opportunity fire** (user doctrine): during the
approach the `anim.duck` fold toward `targetDuck` is unconditional — `wd()`
composes under **both** gait states (Walking *and* AimWalk), and `icr()`
covers **both** stationary states (Idle *and* Aiming) so `riseDuck` renders
while armed. The fold dropping his eye below the parapet → exposure decays
→ arm stows → he arrives tucked — that LOS loss is the *intended* mechanism
that ends opportunity fire, never a held-upright pose to keep shooting.
(Previously `duckIn`/`idleIn` gated to Walking/Idle only — armed poses
silently rendered upright.)

`MoveWithCollision` keeps stepping the whole time — firing never pauses the
advance. Firing is a *channel over* locomotion, exactly like AimWalk.

## `lastSeenTarget` (as built)

```csharp
public Vector3? LastSeenTarget { get; }   // last visible centroid (world)
public float   LastSeenAt    { get; }     // Time.unscaledTime written per positive probe
```

Updated on every positive probe; cleared on ExitCombat. The stale-point
consumers (pop-up re-query → live centroid or suppressing-fire burst, the
`lookScan` 3-step search) stay deferred — the memory exists now so those
phases only read it.

## Six-shot cylinder — staggered reload (as built)

`cylinder`/`cylinderCapacity` (6)/`roundLoadInterval` (1.5s) on
`CombatController`. Dry → `reloading` → one chamber seats per interval
(`cylinder++` + `Changed` per seat — the flow chart's `●○` wheel ticks on
those events) → full → `reloading=false` → re-engage if geometry allows.
Arm stays dipped the whole beat (swing-out model — can't fire mid-load;
the `!reloading` wantAim gate is the one-line to flip if per-chamber fire
is ever wanted). No ammo model exists yet — these fields ARE the start of
one. Flow chart shows the wheel inline after `RELOAD`: `●●●○○○`.

**One wheel, every trigger.** The cylinder is physical state on the
character, shared by all three fire paths — it carries over across mode
switches and `EnterCombat` never refills it:

| Trigger | Rounds | Reload |
|---|---|---|
| AI `TickExposureFire` | `SpendRound()` per shot | auto — `StartReload()` on dry |
| Possess LMB | `SpendRound()` gated on `Cylinder>0 && !Reloading` | **manual — R key** → `StartReload()` |
| Weapons-tab FIRE button | `SpendRound()` gated same | R in possess / auto in combat |

The reload seat tick lives at the top of `CombatController.Update` —
above the `Idle` gate — so a reload started in one mode finishes in
whatever mode he's in. `START DUEL` calls `RefillCylinder()` on both
fighters (arena loadout is a deliberate reset, not a hidden one).

## Symmetric crossfire

The identical query runs Tan-eye → Vinny-voxels — his pop-up opens the mutual
exposure window; who converts first is the skill race (`AimFeelSpring`
sway/recoil + stat-shaped cone). The pit's "AI duel" toggle gives Tan the
same combat stack (`EnterCombat(feet, ch)`) — Phase 2 lights both directions.

## Debug viz (the inspectability contract — as built)

- `CoverSensor.showTargetViz` toggle ("probe rays" in the combat section):
  **green** `tv_s{i}` eye→sample beams = unobstructed, **red** = blocked,
  **cyan cross** `tv_c{x,y,z}` at the live centroid. Cleared on toggle-off /
  ClearViz / empty-query.
- **Setup-mode preview**: the same toggle works at arena setup — with combat
  `Idle` the HUD drives a slow manual probe (0.25s) so the beams + verdict
  are inspectable *before* the duel starts.
- **Layering vs `VisionSensor`**: the cone answers *awareness* (inside FOV +
  detection accumulate) — the probe answers *geometry* (can the eye reach
  him). Same `TryGetEyeAnchor` origin; deliberately not gated on the cone —
  `EnterCombat` is an explicit order, so he already knows. If "can't engage
  what he hasn't noticed" is ever wanted, gating `wantAim` on
  `VisionSensor.AnyDetected` is a one-line policy, not a rebuild. The
  `lookScan` search phase is the cone's designed consumer — the city-wide
  awareness architecture is spec'd in `PERCEPTION_AWARENESS.md`.
- Readouts live on the F5 **COVER + COMBAT** panel (`CoverMetricsPanel`) —
  the centralized readout surface: `exposure n/m (%)` (shows at setup too —
  `tested>0` means the manual probe ran), `armed`, `cyl n/6`, cover pick +
  `reqDuck`, pick-scan verdict (`obs → pts → valid + reject reasons`),
  solver line (`aim err · blend · dist`), `cadence Ns · next shot Ns
  · reload Ns left`, `lastSeen` + age, Tan's mirror telemetry. The combat
  tab keeps only controls (buttons, probe-rays toggle, `probe interval`/
  `fire cadence` sliders) — no info labels.
- **Engagement flow chart** on the same panel — the pipeline drawn as two
  lanes, each node lit green when live, grey when stalled (FIRE/RELOAD
  amber): `IDLE ▸ SEEK ▸ COVER │ HOLD` (movement) over `PROBE ▸ EXPOSED ▸
  ARMED ▸ CONVERGED ▸ FIRE ▸ RELOAD` (fire chain). A greyed node marks
  exactly where the chain stalled; the telemetry lines above carry the
  numbers explaining why (blend/err/cadence/exposure). Node sources:
  `CombatController.State`, `Visibility.tested/visible`, `ExposureArmed`,
  solver `Blend`/`LastAngularErrorDeg`, `LastShotAt` (within 0.4s = lit),
  `Cylinder==0` = RELOAD; `NextFireIn`/`ReloadLeft`/`ShotsFired` feed the
  cadence line.
- **Event-driven panel contract**: `CombatController.Changed` and
  `CoverSensor.Changed` fire on every display-relevant mutation (state via
  `SetState`, picks, armed edge, shot, refill, scan, probe). The panel
  subscribes (`Rehook` tracks the live body), flags `dirty`, rebuilds next
  Update — events never rebuild inside themselves. A 2Hz "analog floor"
  runs only while live (combat/possess/probe preview) for continuous
  readouts (pos, blend, countdowns); idle = zero rebuilds. FIRE therefore
  lights on the shot event itself, not a poll window — `ShotsFired` is
  the persistent beat counter.
- While combat owns the gun, the displayed/manual target IS the live centroid
  (`LastSeenTarget` fills the tucked gap) — `feet+1.3` is only the
  pre-combat fallback.

## Explicitly deferred (settled later — do NOT build)

- AI pop-up trigger policy (cadence / staleness / player order) — tucked
  combatants currently wait for geometry to reopen on its own (enemy stray
  into LOS = emergent opportunity fire); the deliberate stand-up override is
  the next phase
- Possess-mode pop-up control binding (RMB aim-override candidate)
- `threatEye = 0.55` constant → posed threat-eye query (same machinery,
  applied to the other endpoint)
- `lookScan` 3-step visual search pattern (user has it papered — `LastSeenTarget`
  is in place for it)
- Hit-model question — outcome stays purely ballistic for now
- Squad/crowd budgeting of the query

## Risks / traps

- `Raycast` on the target's own position: characters are invisible to the
  world — a ray can never terminate *on* Tan; "visible" means "unobstructed
  path to the sample's world position". Don't test `hit.volumeId == target`.
- The eye must be the **posed** eye (`TryGetEyeAnchor`, duck-following), not a
  height constant — a flat eye would let him see over walls his pose can't.
- Sample set must track the posed silhouette — sampling the REST pose would
  report a standing man where a ducked one is.
- Transition freeze (`prevFrameDuck`) means pop-up's stand blend keeps the
  honest folded from-pose — don't write duck before the pose request.
- `DebugOwnsPose` / possess takeover: combat AI yields to direct control the
  same way the cover drive already does.

## Test plan (cover_arena — block_36, in front of the tenement door)

1. `SETUP PIT` → `START DUEL`, target-dummy mode:
   - Vinny advances folded; while Tan's voxels show, arm comes up, bore
     resolves to the visible centroid, shots fire at cadence.
   - `probe rays` paints green/red beams + centroid cross; `exposure n/m`
     reads on the F5 panel (setup preview works pre-combat).
   - Tucked → `visible 0` → arm stows, crouch-idle, `lastSeen` shows Tan's
     last exposed point.
2. Gradient wall: mid band → partial exposure → centroid sits at the exposed
   cap (head-band aim = the accepted emergent headshot).
3. Six shots → cylinder empty → forced duck + dwell → re-engage cycle.
4. Tan moved while tucked → pop-up re-query finds empty → burst at stale
   point (suppressing fire, emergent).
5. AI duel mode: both directions run the identical query — pop-up windows
   open symmetric exposure races.
