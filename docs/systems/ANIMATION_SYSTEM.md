# Animation System — Canonical Pipeline Reference

**Status**: ✅ Canonical — supersedes the fragmented coverage in
`Assets/docs/systems/CHARACTER_ANIMATION_PIPELINE.md` (GPU pipeline, still valid
for kernels/buffers) and `docs/systems/VOXEL_GROUP_ANIMATION.md` (group concept;
its "Approach B" shader path is deprecated).
**Companion docs**: `CHARACTER_ASSET_LIFECYCLE.md` (registry + pose authority
motivation), `COVER_HEIGHTFIELD.md` (measured-body doctrine), `CHARACTER_PIPELINE_REFERENCE.md` (HTML portrait pipeline),
`WEAPON_ATTACHMENT_SYSTEM.md` (attachment points), `VOXEL_COORDINATE_SPACES.md`.
**Canonical unit**: Vinny = 0.62m standing (62 vox @ `voxelSize=0.01`). World voxels = 0.05m.

---

## 1. Pipeline at a glance

```
AUTHOR (browser)                                    RUNTIME (Unity)
────────────────                                    ───────────────
character_pipeline.html   portrait→bust→body→rig
character_animator.html   pose authoring + preview
        │  save/export
        ▼
<name>.json (steelcity_character)  →  Assets/StreamingAssets/voxel_characters/
        │                                   │
        │            VoxelCharacter.Start   │
        │                                   ▼
        │                     CharacterAssets (single parse, shared truth)
        │                                   │
        │        ┌──────────────────────────┼───────────────────────────┐
        │        ▼                          ▼                           ▼
        │  VoxelCharacterAnimator      CharacterAnimation          VoxelChunkManager
        │  (CPU pose math,             (state lanes, transitions,  (per-frame instance
        │   ParamsData + defaults,      crouchDepth field,          data upload)
        │   measurement, weld)          RequestPose authority)          │
        │        │                          │                         ▼
        │        │                          └──── InstancedCharacter ─ _InstanceAnimData (3×float4)
        │        │                                fields (single      _InstanceIKData   (4×float4)
        │        │                                write site)              │
        │        │                                                      ▼
        │        └──────── parity twin ──────── CharacterPoseCompute.compute
        │                                             CSClear + CSPose
        │                                                      │
        ▼                                                      ▼
   pose math port (same equations)                    VoxelProxyRaymarch.shader
                                                    (samples posed buffer)
```

**Parity rule**: the pose math exists in THREE places that must agree —
`character_animator.html` (JS), `VoxelCharacterAnimator.cs` (CPU), and
`CharacterPoseCompute.compute` (GPU). The HTML is where poses are *authored*;
the C# file's `ComputeGroupRotation` is the normative port; the compute shader
mirrors it for rendering. Any change to pose evaluation must land in all three
or the preview, the weld/measurement path, and the rendered result diverge.

---

## 2. Authoring tools

### `character_pipeline.html`
Portrait-first character generator (see `CHARACTER_PIPELINE_REFERENCE.md`):
portrait pick → bust → full-body procedural build → auto-assign groups/pivots →
hands off to the animator via sessionStorage.

### `character_animator.html` — the pose authoring surface

- **GROUPS** (overridable via `bodyGroups` from the editor): gid 0 Body,
  1 Head, 2/3 L/R Arm, 4/5 L/R Leg, 6/7 L/R Shin, 8/9 L/R Forearm.
- **STATES** — the AnimState enum. Under the scalar model the UI grid is
  Idle/Walking/AimWalk/Aiming/Down/T-Pose/AimSweep/Sit; the retired states
  still EVALUATE (legacy JSON + pose transitions) but are no longer authored:

| id | name | role under the scalar model |
|----|------|------|
| 0 | Idle | standing still — `duck` renders the authored hold pose |
| 1 | Walking | keyframe gait — `duck` folds crumple deltas under the gait |
| 2 | Looking | **retired to a channel** — `lookScan` layers the yaw sweep over ANY pose |
| 3 | Aim Walk | aim upper body + gait lower body (debug/posterity) |
| 4 | Aiming | static aim, weapon presets (debug/posterity) |
| 5 | Crouching | **legacy** — reads `duck−1` as its old `crouchDepth` axis (1+d ≡ old depth d) |
| 6/7 | Flinching / Falling | enum-reserved |
| 8 | Down | lying/defeated |
| 9 | T-Pose | bind pose — NO rotations; rigging inspection |
| 10 | Aim Sweep | aim + sinusoidal shoulder yaw (debug/posterity) |
| 11 | Sit | seated — pelvis anchored by runtime transform |
| 12 | Crouch Walk | **archived** — walking duck is now state 1 + `duck` |

- **THE DUCK SCALAR (0..2)** — one silhouette/cover-height contract. The state
  picks which endpoint SET renders it:

| | Idle | Walking | state 5.x (legacy) |
|---|------|---------|---------------------|
| endpoints | `crouching` → `crouchDeep` | `walkDuckMid` → `walkDuckPose` | same as Idle |
| semantics | **absolute hold pose** (icr) | **crumple deltas under the gait** (wd) | `Cd = saturate(duck−1)` |
| duck 1 | shallow hold | shallow crumple | shallow crouch |
| duck 2 | deep hold | deep crumple | deep crouch |

  ⚠ Never seed `walkDuck*` from the authored crouch — they're DELTAS under the
  gait; absolute strides pin legs against the cycle (the "idle pose overriding
  the walk" bug). Stance presets write `crouching`/`crouchDeep` only.

- **Endpoint fields** (`CrouchingData`): `legStride*`, `kneeBend*`, `armSwing*`,
  `elbowBend*`, `modelLower`, `bodyLower`, `bodyLean`, `bodyForward` (Z torso
  shift — post-rotation model-space, legs stay planted), `headPitch`,
  `headTilt` (walk-duck only). Slider ranges are deliberately wide (knees to
  ~3 rad = knees-to-chest territory).

- **CHANNELS layered over any state**:
  - `lookScan` — yaw-only head sweep (`looking.headYaw/headYawFreq`), yields to
    IK aim (the eye-line belongs to the weapon).
  - IK aim (`ArmAimSolver` → `ikOverrides`) — arm/torso solve layered over the
    whole pose stack.
  - `gaitTime` — the walk cycle runs on an INTEGRATED clock
    (`gaitTime += dt · walkSpeed · duckFactor`); phase derives from it, so
    speed/duck changes alter the RATE, never teleport the pose. `animTime`
    remains the wall-clock for state timing/oscillators.
  - `duckSpeedScale`/`duckSpeedMin` — duck→gait-speed coupling:
    `factor = lerp(1 → duckSpeedMin, duck/2)` — the preview of the in-game
    move-speed curve.

- **Param panels** are rendered per-state from `animParams`; sliders write live
  into the params object, so what you preview is exactly what exports.
- **Exports**:
  - `⬇ Export .anim.json` — standalone params file (`format: anim_params`):
    `{pivots, params, states}`. Legacy/debug path.
  - **Save (project)** — the real deliverable: `<name>.json`
    (`format: steelcity_character`), the consolidated character file. Export
    **diffs `animParams` against `DEFAULT_PARAMS`** (`diffAnimParams`) and only
    writes sections that differ — trimmed files inherit future default fixes.
  - **carriedFields**: `assetType`, `itemParts`, `itemPartDefs`,
    `attachmentPoints`, `attachRotation`, `handedness`, `bgColor` are cached
    verbatim on load and re-emitted on save — the animator is NOT their owner
    but must not strip them (this is what keeps weapon mounts alive across a
    round-trip).
  - `loadedCharName` tracks the loaded asset so re-saves hit the same file
    name Unity probes.

### File Unity consumes

`Assets/StreamingAssets/voxel_characters/<assetBaseName>.json` — the loader
probes by asset base name; the old `*.character.json` double extension
produced files the game never read.

```jsonc
{
  "format": "steelcity_character", "version": 1, "name": "Civilian1",
  "dims": [W,H,D], "voxelSize": 0.01,
  "materials": [...], "voxels": [[x,y,z,mid], ...],
  "groups":   {"x,y,z": gid},      // per-voxel pose group
  "bodyGroups": [...],             // group names/colors
  "regions":  {"x,y,z": rid},      // clothing/feature regions
  "regionDefs": [...],
  "pivots":   {"gid": {"x","y","z"}},  // normalized 0..1 per group
  "animParams": { /* only sections differing from DEFAULT_PARAMS */ },
  "attachmentPoints": {...},        // carried — weapon mounts
  "states": [...]
}
```

---

## 3. Runtime architecture — who owns what

| File | Owns |
|---|---|
| `CharacterAssets` (registry) | Single parse per file; shared truth (CHARACTER_ASSET_LIFECYCLE) |
| `VoxelCharacter.cs` | Asset load, instanced registration, `TryMeasureTopY` / `TryGetEyeAnchor`, exposes `JointAnimator` |
| `VoxelCharacterAnimator.cs` | **CPU pose truth**: `ComputeGroupRotation`, `PoseVoxels`, `PosePoint`, `AnimParamsData` schema + defaults fill (`LoadFromAnimJson`) |
| `CharacterAnimation.cs` | **State authority**: base lane + override lane (`RequestPose`/`ReleasePose`), transitions, **the `duck` scalar 0..2** (+ `crouchDepth` legacy alias: depth d → duck 1+d), integrated `gaitTime`, `duckSpeedScale`/`duckSpeedMin`, `lookScan` channel, per-frame handle writes |
| `ArmAimSolver.cs` | **IK channel**: `ikOverrides[gid]` + `ikBlend` (arm/forearm/torso); gated OFF while crouched |
| `VoxelChunkManager.cs` (UI/) | Per-frame upload: `InstancedCharacter` fields → `_InstanceAnimData`, `_InstanceIKData`, offsets |
| `CharacterPoseCompute.compute` | **GPU pose mirror**: CSClear + CSPose kernels |
| `VoxelProxyRaymarch.shader` | Samples the posed buffer (no animation logic) |
| `WeaponMount.cs` | CPU pose consumer — welds the weapon to the posed hand via `PosePoint` |
| `CombatController` / `PossessController` | Drive `crouchDepth` + `RequestPose` from cover state |
| `CharacterRig.cs` | Debug hotkeys (priority 50 — nothing stomps them) |

### Pose authority — two lanes (`CHARACTER_ASSET_LIFECYCLE`, gotcha G3)

- **Base lane** (`currentState`): locomotion. `autoDetectWalking` writes
  Idle↔Walking from horizontal velocity (`walkVelocityThreshold`, speed →
  `walkSpeed` clamped 0.5–2.0).
- **Override lane** (`RequestPose(state, priority, duration)`): wins while
  held. Priorities: `PRIORITY_BEHAVIOR=10` (LookAround, scripted beats),
  `PRIORITY_COMBAT=30` (aim/cover), `PRIORITY_DEBUG=50` (rigs/hotkeys).
  Higher-or-equal replaces; lower rejected. `duration ≥ 0` auto-expires —
  nothing needs to "restore" a pose.
- `EffectiveState` = override or base. Auto-detect is suspended under an
  override (the "Aim resets to Idle" bug). `DebugOwnsPose` (>combat priority)
  also owns the `crouchDepth` channel — combat's eased depth drive yields to
  a debug hold.

### Per-frame data flow (single write site)

`CharacterAnimation.Update` re-fetches the `InstancedCharacter` handle EVERY
frame (gotcha G7 — registration swaps the object) and writes:

```
instancedHandle.animState / animTime / animSpeed
instancedHandle.transStateA / transTimeA / transBlend / transDip / transDepthA
instancedHandle.crouchDepth
```

`VoxelChunkManager` packs them per frame:

```
_InstanceAnimData[i*3+0] = (animState, animTime, animSpeed, transBlend)
_InstanceAnimData[i*3+1] = (posedBufferOffset, transStateA, transTimeA, transDip)
_InstanceAnimData[i*3+2] = (duck, transDuckA, gaitTime, lookScanBlend)
_InstanceIKData  [i*4+0..3] = quat(gidA), quat(gidB), quat(torso),
                              (gidA, gidB, blend, flags{bit0 arm, bit1 torso})
```

`crouchDepth` on the handle now carries the duck scalar; `transDepthA` freezes
duck at transition start. `gaitTime`/`lookScanBlend` ride the two free slots —
no stride change.

---

## 4. Pose evaluation — the math all three evaluators share

### FK chain

`PARENT_OF = {8→2, 9→3, 6→4, 7→5}` — forearms inherit their arm, shins inherit
their leg. A group's transform chain = `[own (pivot, rot)] + parent's chain`;
upper-body groups (1,2,3) additionally get the **body transform appended
outermost** (torso twist + crouch lean + torso IK).

### Rest pose first

`restPose.leftArmZ/rightArmZ` rotate the T-pose arms down to the body before
any state logic — every state except T-Pose (9). Author sliders on TOP of the
rested pose, never on the bind pose.

### Per-group scalar sources by state

The duck channels compose additively with the state columns — `wd(·)` =
`lerp(walkDuckMid, walkDuckPose, duckDeep)·duckIn` (Walking only), `icr(·)` =
`lerp(crouching, crouchDeep, duckDeep)·idleIn` (Idle only), `Cd = saturate(duck−1)`,
`Cr(a,b) = lerp(a,b,Cd)`:

| gid | group | Idle | Walking/AimWalk | Looking | Aiming/Sweep | Crouching (5.x) | Sit |
|-----|-------|------|-----------------|---------|--------------|-----------|-----|
| 0 | Body | `icr` lean/lower/fwd | `wd` lean/lower/fwd | — | `aiming.torsoTwist` | `Cr(·)` + `bodyForward` | — |
| 1 | Head | `icr` headPitch + scan | `wd` headPitch/Tilt + scan | `looking.*` sinusoids | `aiming.head*` (+sweep) | `Cr(·)` headPitch | `sitting.headPitch` |
| 2/3 | Arms | rest Z + `icr` swing | kf + `wd` armSwing | — | `aiming.armSwing*` + reach | `Cr(·)` armSwing | `sitting.armSwing*` |
| 4/5 | Legs | twist + `icr` stride | kf + `wd` legStride | — | (AimWalk: kf) | `Cr(·)` legStride | `sitting.legStride*` |
| 6/7 | Shins | rest + `icr` knee | kf + `wd` kneeBend | — | — | rest + `Cr(·)` kneeBend | `sitting.kneeBend*` |
| 8/9 | Forearms | rest + `icr` elbow | kf + `wd` elbowBend | — | `aiming.elbowBend*` | rest + `Cr(·)` elbowBend | rest |

Walk overlay: `bodyBobY = -cos(4π·phase)·amp` on Y,
`weightShiftX = sin(2π·phase)·amp` on X — `phase` derives from the integrated
`gaitTime`, applied to every voxel after the chain. State 12's crouchWalk kf
data still parses but is superseded by state 1 + `duck`.

### bodyLower vs modelLower (distinct!)

- `bodyLower` — drops only upper-body groups (gids 1,2,3): the torso's mass
  sinks while legs keep their stride stance.
- `modelLower` — drops ALL root groups (1–5): the whole model squats.
- Both fold into gid 0's `bodyOffset` and the per-group `offset.y`.

### Walk keyframes (`walkKeyframes`)

4 slots at phases 0/0.25/0.5/0.75 of `cycleDuration / animSpeed`.
`interpolation`: `"spline"` = Catmull-Rom over kf[i-1,i,i+1,i+2] | `"smoothstep"`
| `"cosine"` | linear. `autoMirror` mirrors kf0→kf2, kf1→kf3 (L/R swapped).
10 scalars per kf: `armSwingL/R, legStrideL/R, elbowBendL/R, kneeBendL/R,
forearmTwistL/R`.

### The crouch scalar channel — `Cr(s, d)`

Inside `ComputeGroupRotation`, when `isCrouchingState`:

```csharp
var cdeep = ap.crouchDeep ?? ap.crouching;   // absent → inert clone
float Cd = Mathf.Clamp01(crouchDepth);
float Cr(float s, float d) => Mathf.Lerp(s, d, Cd);
```

`crouchDepth` 0→1 lerps EVERY authored scalar of `crouching` toward
`crouchDeep` — this is the adaptive-duck axis. It exists ONLY inside state 5;
it is orthogonal to the state enum.

### IK override channel

`ikOverrides[gid]` (model-space quaternion) + `ikBlend` REPLACE a group's
param-derived `ownRot` (`Slerp(base, ik, blend)`); children still inherit via
FK. gid 0 (torso) participates in the body transform so assist propagates to
arm/forearm chains. Written by `ArmAimSolver` (aim) — CPU mirror drives the
weapon weld; GPU mirror via `_InstanceIKData`.
**Posture gate**: the solver refuses to engage while crouched, and entering a
crouch drops the channel instantly — the crouch pose transition carries the
arm down; a stale torso quat would stomp the authored crouch lean
(`IK_TORSO_OVERRIDE_STOMPS_CROUCH_LEAN`).

---

## 5. `animParams` schema (complete)

Sections loaded by `LoadFromAnimJson`; **every missing section is filled with
the C# defaults below** (raw-text `Missing()` check — JsonUtility materializes
zero-filled objects instead of null). The HTML `DEFAULT_PARAMS` and this table
must agree.

| section | fields | semantics |
|---------|--------|-----------|
| `restPose` | `leftArmZ, rightArmZ` | T-pose→idle arm drop (rad); applied under every state but T-Pose |
| `jointOffset` | `{gid: {x,y,z}}` | post-rotation limb nudge (vox units); children inherit via PARENT_OF |
| `walkKeyframes` | `autoMirror, cycleDuration, interpolation, kf0..kf3, bodyBob{enabled,amplitude}, weightShift{enabled,amplitude}` | gait cycle; kf2/kf3 null→mirror |
| `crouchWalk` | *same fields as `walkKeyframes`* | state-12 gait — keyframed limb oscillation ADDITIVE on the ducked base; ONE set only (depth lives in the body channel, not in a second gait endpoint) |
| `armSwing` | `axisL/R, signL/R` | shoulder swing axis + sign convention |
| `legStride` | `axisL/R, signL/R` | hip stride axis + sign (note: sign=-1 is the corrected convention) |
| `legTwist` | `leftRest, rightRest` | static Y toe-in/out |
| `elbowBend` | `axisL/R, signL/R, leftRest, rightRest, twistL/R, twistWalkAmp` | forearm hinge + twist |
| `kneeBend` | `axisL/R, signL/R, leftRest, rightRest, walkAmp` | shin hinge |
| `looking` | `headYaw, headYawFreq, headPitch, headPitchFreq` | sinusoid scan |
| `aiming` | `weaponType, torsoTwist, headYaw/Pitch/Tilt, armSwingL/R, shoulderReachL/R, elbowBendL/R` | static aim base; `weaponType` = pistol/dual/rifle presets authored in HTML (`AIM_WEAPON_PRESETS`) |
| `aimSweep` | `amp, freq, headFollow` | state-10 shoulder-yaw sweep |
| `crouching` | `bodyLower, modelLower, bodyLean, headPitch, armSwingL/R, legStrideL/R, kneeBendL/R, elbowBendL/R` | **shallow endpoint (depth 0)** |
| `crouchDeep` | *same fields as crouching* | **deep endpoint (depth 1)** — absent → clones shallow (inert) |
| `sitting` | `headPitch, armSwingL/R, legStrideL/R, kneeBendL/R` | seat pose; NO bodyLower/modelLower — the seat anchor positions the pelvis |
| `vision` | `show, range, halfAngleDeg, fill, anchorFace` | FOV cone spec — `VisionSensor` consumes the same params |

Top-level (outside `params`): `pivots` (`gid → normalized 0..1`), `states`
(exported table for reference).

---

## 6. The two blend mechanisms — and the gap

The engine has TWO parametric blend systems; they cover different axes.

### 6a. Dual-eval pose transition (time-driven, pose→pose)

On every **effective-state** change, `CharacterAnimation` freezes the outgoing
pose as `(transFrom, transFromTime, transFromDepth)` and ramps `TransBlend`
0→1 over `transitionDuration` (0.32s) with `transitionEase` (linear /
smoothstep / cosine), plus `TransDip = -sin(πt)·dip` (settle dip, zero at both
ends — same bob flavor as the gait).

Both evaluators then evaluate the voxel/point **twice** — once under the
frozen A pose, once under the live B pose — and `lerp(pA, pB, TransBlend)` in
position space. CPU: `PosePoint(transStateA, transTimeA, transBlend, transDip,
transDepthA)`. GPU: CSPose's second pass over `instMeta.y/z/w` + `cDepthA`.
`transDepthA` freezes the depth axis WITH the pose so a crouch→stand doesn't
snap the duck mid-blend.

This is a general "whatever pose it was → authored pose" engine — but its
weight is a **clock**, not a parameter.

### 6b. `crouchDepth` (parameter-driven, inside one state)

`crouchDepth` 0→1 lerps every crouch scalar shallow→deep — a height-driven
axis bound to cover. Writers: `CombatController` (eased via
`crouchDepthSpeed`), `PossessController` (ring-probe / painted-zone verdict),
debug sliders; `DebugOwnsPose` lets a hold own the channel.

### 6c. The gap (pinned — the work this doc precedes)

`stand ↔ shallow` is reachable ONLY through the timed transition: there is no
continuous axis for the top half of the duck. On a wall that steps down
through the shallow band (h14 rise 0.60, h13 rise 0.55 — both `reqDepth=0`),
the pose is identical while the paint correctly differs.

**Unified duck axis** `duck ∈ [0,2]` — `0` stand → `1` shallow → `2` deep —
**now prototyped in the HTML animator as preview state 5.2**:

- Segment [0,1]: hold the dual-eval blend at weight `duck` instead of a timer —
  reuses the A/B machinery verbatim (`heldBlend = {from:0, weight:duck}`,
  evaluated inside `posedPosTransition`). A=Idle, B=Crouching@depth0.
- Segment [1,2]: today's `crouchDepth`.
- The Duck Axis state's slider writes `duckAxis`; `rebuildAnimatedMesh` maps it
  to (`heldBlend`, `crouchDepth`) each frame — the exact mapping Unity needs.
- Solver maps wall rise → `duck` continuously: `h(duck)` is piecewise-linear
  (0.62 → 0.50 → 0.45 measured), classes become `duck≤0` Full /
  `0<duck≤2` crouch band / `>2` Low.
- Consumers: `DepthForTop*` generalize to `DuckForTop`; pose drivers write one
  float instead of (state, depth) pairs.
- **Parity burden (Unity port — NOT yet done)**: CPU `PosePoint`, GPU CSPose,
  and `TryMeasureTopY` all need the held-blend eval; the HTML preview proved
  the math, not the port.
- **Risk**: position-space lerp between two poses shortens limbs where joint
  rotations differ sharply (watch forearms/shins at mid blend) — the 5.2 scrub
  exists precisely to eyeball this before any Unity code moves.

---

## 7. Silhouette measurement → cover contract

### `VoxelCharacter.TryMeasureTopY(state, depth)`

- At asset load: `topVoxels` = the max-Y rest voxel per group (~10 points).
- `TryMeasureTopY` poses each through `PosePoint` (same FK as the weld) and
  takes the max posed Y, `+0.5·voxelSize` (cell-center convention).
- Cached per `(state, depth)` — poses only change on asset reload, so this is
  free per frame.
- `TryGetEyeAnchor` is a DIFFERENT question: "where are the eyes" (vision cone,
  firing LOS). Cover never uses it for classification — that conflation was
  the old `standEye`/`crouchEye` bug (see COVER_HEIGHTFIELD doctrine).

### `CoverSensor` measures live every scan

```
HStand   = TryMeasureTopY(state=0, depth=0)   // ≈0.62
HShallow = TryMeasureTopY(state=5, depth=0)   // ≈0.50
HDeep    = TryMeasureTopY(state=5, depth=1)   // ≈0.45
EyeFwd / EyeFwdDeep = eye anchor XZ lean at depth 0 / 1 (pose lean, for LOS probe)
SupportY = world.TryColumnTop(pos)            // ground under the feet — additive
```

`h(d) = lerp(HShallow, HDeep, d)` is EXACT because the pose eval lerps every
scalar linearly — re-authoring a pose re-tunes cover for free.

### Classification

```
reqDepth(raw) = (wallTop − depthMargin − localFloor − HShallow) / (HDeep − HShallow)
d ≤ 0   → Full   (hides a standing silhouette)
0 < d ≤ 1 → Crouch (the band — pose = depth d)
d > 1   → Low    (unusable — never picked)
```

- `depthMargin` (0.02m) keeps the posed top below the wall top, not at it.
- `DepthForTop` clamps [0,1] for pose writers; `DepthForTopRaw` preserves the
  class. The old clamped-only solve hid Low rejections
  (`COVER_DEPTH_SOLVE_CLAMP_HIDES_REJECT`).
- The cover **field** (CoverField) paints per-cell verdicts from the same
  `h(d)` with per-cell `localFloor` probes — a curb inside a zone dims or
  unpaints that cell, and `TryZoneAt` reports the cell's own class+depth to
  pose drivers. Heat follows wall **rise**; posture follows **depth** — the
  two read the same verdict.
- `PossessController` drives `crouchDepth` from the painted zone / probe ring;
  `CombatController` eases toward the picked cover's requirement.

---

## 8. The collapsed animation model — scalars + channels over states

The HTML prototype collapsed the state grid (decided 2026-08, superseding
the CrouchWalk=12 plan below): depth, duck, looking, and aiming are now
**scalars and channels on surviving states**, not states of their own.

**Grid**: `Idle 0 · Walking 1 · Sit 11 · Down 8 · T-Pose 9` + the museum
trio `Aim Walk 3 · Aiming 4 · Aim Sweep 10` kept for debug/posterity.
Legacy states (2, 5.x, 12) still EVALUATE for saved JSON and pose
transitions but are unreachable from the grid.

**Retired into channels/scalars**:
- `Looking (2)` → `lookScan {enabled, blend}` — the authored `looking`
  yaw/pitch oscillation added over ANY state's head pose. BASE CROUCH +
  Look Scan = the crouched alert sentry. IK headTrack wins while aiming.
- `Crouching (5) / Crouch Deep (5.1)` → ONE shared **`duck` scalar 0–2**
  bound across both stationary and moving poses — same value = same
  silhouette height `h(duck)`, the uniform contract cover logic needs:
  - **on Idle** — the stationary hold: `icr(k) = lerp(crouching,
    crouchDeep, max(0,d-1)) × min(1,d)` composed over the rest pose.
    `crouching`/`crouchDeep` are AUTHORED POSES (BASE CROUCH is the
    default). No gait.
  - **on Walking** — the moving duck: `walkDuckMid`/`walkDuckPose` are
    CRUMPLE DELTAS under the mirrored gait (symmetric folds, NOT authored
    poses — absolute strides would pin a leg against the cycle).
    `wd(k) = lerp(mid, far, max(0,duck-1)) × min(1,duck)`.
  Unity: `PoseDriver.duck` is the ONLY height knob — state picks the
  endpoint set. Caveats: (a) gait bob modulates top-of-head on the moving
  side — `TryMeasureTopY` takes worst-of-gait; (b) endpoint sets are
  different data, so true `h(duck)` parity is a tuning step — match
  `walkDuckPose.modelLower` to the held-crouch head height.
- `Duck Axis (5.2)` → the scalar itself (preview absorbed).
- `Crouch Walk (12)` → Walking × Duck. Its dedicated `crouchWalk` gait
  presets remain as dormant experiment data (no consumer until/unless a
  crouch-gait slot is revived on Walking).

**Stance presets** write the duck endpoints (sh→mid, dp→far), so
BASE CROUCH lands authored-shallow at duck 1 and authored-deep at duck 2 —
verified field-for-field.

**Unity port shape**: `PoseDriver { stateId, duckLevel, aim{...},
scan{...} }` — a handful of floats replaces the state machine.

Historical contract (pre-collapse, for reference):

1. **ONE state: `CrouchWalk = 12`.** Depth is NOT encoded as states — same
   rule as `crouching`/`crouchDeep`: no `deepCrouchWalk`, no `crouchAimWalk`,
   no `deepCrouchAimWalk`. A single enum id, a single param section.
2. **Body channel** = crouch scalars at `crouchDepth` (`Cr` lerp) —
   bodyLean/bodyLower/modelLower/headPitch keep the silhouette at `h(d)`;
   limb baselines (`armSwing`, `legStride`, `kneeBend`, `elbowBend`) fold to
   the same depth. The duck axis stays the single source of posture truth.
3. **Limb channel** = ONE `crouchWalk` keyframe section (same schema as
   `walkKeyframes`: autoMirror/cycleDuration/interpolation/kf0–3/bodyBob/
   weightShift), evaluated additively: `sign·(Cr(base) + kf)`. A dedicated
   section — NOT shared `walkKeyframes` — because the duck gait needs shorter
   strides and halved bob/shift (a deep duck shuffles; the head must stay
   under the wall the depth says you're under). If deep-crouch locomotion
   reads wrong, the first fix is kf values, NOT a second endpoint.
4. **Aiming = an IK channel, NOT a state.** No authored crouch-aim poses:
   under the IK-only doctrine a character aims only when a real target is
   acquired AND a sight line + IK path validate — `ArmAimSolver` overrides
   the arm/forearm chain on top of whatever pose exists (crouch, crouch-walk,
   idle). Out of combat (waiting, reloading) → stand, weapon idle. Breaching
   cover or maneuvering with a valid target → the solver engages over the
   locomotion pose. The current "cannot aim while crouched" gate is a
   temporary combat doctrine to be revisited when target acquisition exists —
   NOT a reason to author crouch-aim states.
   **Debug harness (HTML, DONE)**: the animator's "IK Aim (debug)" block
   simulates the channel — `ikAimSolve` mirrors `ArmAimSolver.Solve`
   (law-of-cosines elbow in the hint plane + cumulative forearm rotation,
   body-lean folded into the target frame) and replaces `ownRot` on gids
   2/3/8/9 blended by `ikAim.blend`, over ANY state, with a red sphere/beam
   target marker. Toggles/sliders: enabled, **arm** (`right`/`left`/`both` —
   sides are as the UNITY VIEW renders them: `right` = gid 2/8, because the
   mirror flips model-X and the tool's "L/R Arm" labels were authored in the
   un-mirrored view), blend, dist, height (0=auto chest ≈0.58·H), reach,
   **torsoTwist** (fixed waist blade, default −0.2 = the authored pistol
   stance, sign-flipped per side), **torsoShare** (yaw tracking toward
   off-center targets — Unity's torso assist term). The elbow hint flips
   per arm side; the twist rides the same body transform the solve folds,
   so a bladed stance still lands the hand on target. **Head Track**
   (0..1, default 1) adds a head-aim override in the same channel: the
   look rotation is built by `basisDelta` (bind-forward → target dir,
   same construction as the arm solve) against the folded target,
   blended over the state's own head pose — aiming takes eyes, so the
   vision cone sweeps onto the aim point. **Torso pitch assist**
   (`ikAimTorsoPitch`) is the vertical twin of the yaw share — target
   elevation × `torsoShare` folds into `bodyLean`, so the torso now
   assists on both axes (`torsoTwist` stays a tunable blade constant;
   0 = pure solve). This is the visual proof that the rigid aim states
   collapse into simple poses under a live channel.
   **Look Scan** is the same doctrine applied to Looking: a debug channel
   that adds the authored `animParams.looking` yaw/pitch oscillation onto
   ANY state's head pose (× `lookScan.blend`, yielded to IK headTrack).
   BASE CROUCH + Look Scan = the crouched alert sentry — no CrouchLook
   state needed.
5. **Speed**: depth-scaled (PossessController already scales walk speed by
   crouch depth; the state must not re-introduce a fixed speed).
6. **Measurement**: `TryMeasureTopY` must evaluate the state across the gait
   phase and take the WORST top (head bob inside a crouch can breach the wall
   the depth says you're under — measure at several `animTime` samples, keep
   the max). The cover contract `h(d)` then holds for locomotion too.
7. **Authoring destination**: state `12` in the animator's STATES — the shared
   `buildKfEditor` renders its keyframes; `crouchDepth` slider previews depth;
   scrub `animTime` × depth together. (DONE in HTML.)
8. **Articulation families + preset catalog (DONE in HTML)** — verified by
   computing the pose math (`thigh = −legStride` through the `sign = −1`
   wrapper, `shin = thigh + kneeBend`; `+` = tip swings BACK):
   **PLANT** = thigh FORWARD (positive stride) with knee ≈ stride → shin
   ≈ vertical, forward foot flat; **TUCK** = thigh back (negative stride),
   shin folds past it (shin ≥ ~1.4×thigh); **KNEEL** = thigh ~vertical,
   shin flat behind; **LUNGE** = thigh forward + knee partially released
   → leg extended low-forward. THE AUTHORED CROUCH (Civilian1.json,
   replicated 1:1 as the `BASE CROUCH` preset — all 24 scalars verified)
   is **PLANT+TUCK** shallow (`+1.34/1.34` → thigh −77°, shin +0°; R
   `−0.51/1.05` → +29°/+89°) deepening to **fwd-LUNGE+TUCK** (L `+1.5/0.78`
   → thigh −86°, shin −41°; R `−0.92/0.66` → +53°/+91°), with
   `modelLower 11/14.5` dropping the body onto the folded legs — the fold
   and the lower are one pose. NOTE: `DEFAULT_PARAMS.crouching/crouchDeep`
   in the tool are a DIFFERENT pose (all-tuck, lower 4/8) — the tool
   defaults are not the authored pose. BAD ZONE: forward thigh +
   still-folded shin (lifted tucked foot) or back thigh + insufficient
   fold. `CROUCH_STANCE_PRESETS` are built as `CROUCH_BASE_STANCE` (the
   authored replica) **+ named deltas via `stanceDerive()`** — every preset
   inherits the authored PLANT+TUCK legs and varies one axis: `mirrored`
   (legs/arms swap), `hunched`, `upright`, `low`, `tall`, `relaxed`,
   `ready`. Rule: when varying the PLANT leg keep `kneeBendL ≈ legStrideL`
   so the shin stays ~0. × `CROUCH_GAIT_PRESETS` composite live — pickers
   on crouch states + state 12, and the dropdown shows the applied pick.
   GAIT MODEL: **lead-follow, `autoMirror: false`, 4 authored keyframes**
   (REACH→SETTLE→DRAG→RELOAD). Mirroring deltas over the asymmetric
   authored stance is invalid — the planted L leg absorbs the stepping
   leg's deltas and hyper-extends (verified: shin −89°). L holds PLANT
   with a small drive oscillation; R is the stepping leg (stride +d,
   knee −dk releases the fold at REACH; −d/+dk drags it back under).
   `DEFAULT_PARAMS.crouchWalk` = the `sneak` preset so fresh characters
   start on the correct model. All frames verified against the pose math —
   L stays PLANT, R cycles SWING→TUCK→DEEP-TUCK (march lifts L at DRAG
   deliberately). `duckwalk` ports the upright walk cycle verbatim
   (`autoMirror: true`, same kf deltas) onto the crouch stance — legs
   mirror but stay pinned to the asymmetric bases (one flagged frame:
   deep kf2 L −109°/−64° over-reach). The alternative **Walk Duck**
   channel lives on the WALKING state: `walkDuck` 0..1 scales the crouch
   BODY channel (lean/lower/head, depth-lerped) while `walkKeyframes`
   owns the limbs entirely — legs truly free to mirror. The channel is a
   single **0–2 Duck slider** on Walking driving the authored
   **`walkDuckPose` endpoint section** — the complete "level 2" pose
   (limb deltas + `modelLower` + `bodyLower` + `bodyLean` + `headPitch` +
   `headTilt`), lerped in by `duckT = walkDuck/2` so duck=2 IS the
   authored pose and un-crumpling is sliding back to 0. Authored on the
   Walking panel, exported per character; edits never touch
   `walkKeyframes` (kf edits WOULD contaminate the base gait — the
   endpoint section exists precisely to prevent that). TODO: `ready` stance arm deltas should become
   weapon-conditional (pull from `AIM_WEAPON_PRESETS`/weapon anchors).
9. **Parity — the Unity port checklist (NOT yet done)**: `AnimState` enum,
   `VoxelCharacterAnimator.ComputeGroupRotation` branches (body channel +
   `crouchWalk` gait), `PosePointCore`, `CharacterPoseCompute.compute` CSPose
   kernel, `isWalkState` flags in rebuild/bob code paths, cover drivers
   (`PossessController`, `CombatController`) selecting state 12 while
   moving-in-cover, and `TryMeasureTopY` worst-of-gait. The same pose engine
   also lives in `voxel_editor.html`, `character_pipeline.html`,
   `character_pose_engine.js`, and its generator `generate_pipeline_js.py` —
   port the proven math to ALL copies or the tools drift.

---

## 9. Authoring workflow — change a pose

1. Open `character_animator.html`, load `voxel_characters/<name>.json`.
2. Pick the state (5.1 for the deep endpoint). Move sliders / scrub depth.
3. Save → `<name>.json` (diffed params; carriedFields preserved) → drop into
   `Assets/StreamingAssets/voxel_characters/`.
4. Play — cover retunes itself (heights are measured, not constant).
5. If pose math changes (not just params): port to `VoxelCharacterAnimator`
   AND `CharacterPoseCompute.compute` — the parity checklist below.

## 10. Gotchas & parity checklist

- **JsonUtility fills missing sections with zero-objects**, not null —
  `Missing()` checks raw text; explicit `"kf2": null` is detected via
  `IsWalkKfExplicitNull` (zero-fill would break autoMirror).
- **`pivots`/`jointOffset` are int-keyed dicts** — JsonUtility can't parse
  them; regex extraction (`ParsePivotsManual`/`ParseJointOffsetsManual`).
- **Trimmed exports are intentional** — `diffAnimParams` writes only
  non-default sections; do NOT hand-write a full params block into the file
  or you freeze today's defaults.
- **Pivots are normalized 0..1** × dims × voxelSize in the evaluator —
  `VOXEL_COORDINATE_SPACES` conventions apply throughout (index space =
  rendered center; +0.5 offsets).
- **Group ids ≠ array index** — `GROUPS` is ordered 0..5,8,9,6,7; look up by
  `id` (the 6/7/8/9 inversion has bitten before).
- **Parity check** after any pose-math change: HTML preview == CPU `PosePoint`
  (weapon weld + `TryMeasureTopY`) == GPU render. Divergence shows as a
  weapon floating off the hand or cover misreading the silhouette.
- **State id range tests** use `>x.5 && <x+0.5` float bands in both evaluators
  — a new state id must fit the pattern (12 = next free).
- **`animTime` resets on state change**; `pendingPrevAnimTime` preserves the
  outgoing phase for the frozen A-pose.
- **`PoseVoxels` (weld) omits `crouchDepth`** in its public signature —
  `PosePoint` is the depth-aware entry; keep call sites honest.

## 11. Cross-references

- `CHARACTER_ASSET_LIFECYCLE.md` — parse dedup, WhenReady ordering, RequestPose rationale (G1–G8)
- `COVER_HEIGHTFIELD.md` — measured-body doctrine, Step layer, field spec
- `COMBAT_COVER_STATE.md` — combat pose driving, pop-up/fire/return loop
- `WEAPON_ATTACHMENT_SYSTEM.md` — attachmentPoints, grip alignment
- `CHARACTER_SPAWNING_SYSTEM.md` — component stack, instancing buffers
- `VOXEL_ENGINE_GOTCHAS.md` — animation-related traps (#4 Approach-A, #5 missing component, #7 handedness)
- `VOXEL_COORDINATE_SPACES.md` — index vs cell-center, centroid math
- Known issues: `IK_TORSO_OVERRIDE_STOMPS_CROUCH_LEAN`, `EXPORT_DROPS_ANIM_METADATA`, `POSE_ENGINE_BOBFN_CRASH`, `COVER_DEPTH_SOLVE_CLAMP_HIDES_REJECT`, `CHARACTER_LIFECYCLE_RACES`
