# Movement — locomotion model, gait coupling & tuning surface

**Status**: ✅ BASELINE AS-BUILT (first official pass, Oct 29 2026 — movement-feel exploration; ramps rolled back per user)
**Owners**: `CombatController.cs` (mover) · `CharacterAnimation.cs` (gait) · `DebugHUDManager.cs` (tuning surface)
**Related**: COMBAT_DICE_ENGINE.md (who issues the legs), AIM_RIG.md, ENGAGEMENT_BEATS.md, CAMERA_SYSTEM.md

---

## 1. The mover — `StepToward` / `StepRouted`

All combat movement funnels through **`StepToward(dest, who)`**:

- **Per-frame integration** — `step = moveSpeed × DuckSpeedFactor × Time.deltaTime`, applied through `MoveWithCollision`. Verified Oct 29: position advances per rendered frame, **not** on SimClock ticks — there is no tick-stepping jitter to interpolate away.
- **`MoveWithCollision`** — swept walk-space move with axis-slide fallback; handles ledges up to `maxStepUpVox` (curbs, aprons, thresholds).
- **Arrival** — `dist ≤ arriveEps` (0.06 m) takes the remaining sub-step as final placement; the leg reports arrived/aborted by distance re-verify.
- **Stuck guard** — ~0.6 s with no ≥5 cm progress toward the waypoint → up to 3 perpendicular nudges (±0.28 m, alternating side); exhausted budget returns "leg done" and callers re-verify (a corner trap is a *verdict*, never a fake arrival).
- **Routed legs** — `StepRouted` wraps corridor routing (Dijkstra flood + string-pull) around `StepToward`; `allowBare=false` at the dice-leg call site refuses no-corridor legs at the line instead of walking blind into walls.
- **Facing** — `FaceTrack(dest, who)` vs `FaceFree()` per step; `turnSpeedDeg` caps yaw rate at **540 °/s** (static — universal). Facing is demand-based, last write of the frame wins (movement-facing after `StepToward`, combat-facing overrides it when engaged).

## 2. Speed & action rates — STATIC + universal

```csharp
public static float moveSpeed = 0.85f;        // CombatController — base walk, the tuning foundation
public static float turnSpeedDeg = 540f;      // CombatController — yaw cap
public static float seekDuckEase = 2.5f;      // CombatController — duck fold-in during seeks
public static float riseDuckEase = 3f;        // CombatController — duck rise/drop during beats
public static float fireInterval = 0.9f;      // CombatController — shot cadence
public static float roundLoadInterval = 1.5f; // CombatController — reload: s per chamber seated
public static float transitionDuration = 0.32f; // CharacterAnimation — pose blend pace
```

- **Universal by construction** — one field, every character reads it; per-character tuning cannot drift (user decision, Oct 29 — extended from `moveSpeed` to all movement/action rates).
- **Caveat** — statics aren't serialized: Play-mode edits reset to the compiled default on stop. Tune live on the **Movement** HUD tab, then bake the chosen numbers into the defaults.
- **Scope** — universality currently covers **CombatController fighters** (+ `transitionDuration` on every `CharacterAnimation`). Citizens (`PedestrianController`) and the possess drive channel have their own movers and don't read these fields yet.
- `anim.walkSpeed = Mathf.Clamp(moveSpeed, 0.5f, 2f)` writers exist at four intent sites (seeks/legs) — they set the animation's *intent* rate; the measured auto-detect overrides it during actual walking.

## 3. Gait — `CharacterAnimation` is foot-locked to measured speed

```
velocity    = (transform.position - lastPos) / dt          // per-frame finite difference
horSpeed    = |velocity.xz|                                 // measured ground speed
walkSpeed   = clamp(horSpeed × 0.5, 0.5, 2.0)               // cycle rate (auto-detect lane)
gaitTime   += dt × walkSpeed × DuckSpeedFactor              // integrated phase clock — never resets
```

- **Foot-locked** — gait rate derives from *measured* displacement, not commanded speed. Feet can't treadmill: blocked/slowed movement reads as slower cadence automatically.
- **Phase continuity** — `gaitTime` is an integrated clock; speed changes alter the *rate* of advance, never teleport the phase (state changes carry stride phase through the transFrom-freeze blend).
- **Duck coupling** — `DuckSpeedFactor` slows both the transform step *and* the gait rate by the same factor — folded posture = shorter, slower steps, automatically.
- **Auto-detect** — `horSpeed > walkVelocityThreshold` flips Idle→Walking; sub-threshold flips back. `walkSpeed` param is also the shader's `animSpeed`.
- Pose transitions (`transFrom`/`transBlend`/`transDip`/`transitionEase`) — free-flowing interruption: a state change mid-blend re-freezes the live pose and blends from there. `transitionDuration`/`transitionDip`/`transitionEase` + `duck`/`duckSpeedMin` are already HUD sliders in the anim section.

## 4. Tuning surface (HUD)

The **Movement tab** ("MOVEMENT / ACTIONS SPEED", left HUD) is the canonical surface — every slider is a universal static:

| Fold | Sliders | Drives |
|---|---|---|
| `locomotion` | `walk speed m/s`, `turn deg/s` | `moveSpeed`, `turnSpeedDeg` |
| `pose & posture rates` | `pose blend s`, `seek duck ease /s`, `rise/drop duck /s` | `transitionDuration`, `seekDuckEase`, `riseDuckEase` |
| `action speeds` | `fire cadence s`, `reload — s per round` | `fireInterval`, `roundLoadInterval` |

Combat tab keeps the rules (not rates): `min threat m`, `probe interval`, `all beats dwell`. Per-character anim sliders (`transitionDip`, `transitionEase`, `duck`, `duckSpeedMin`) stay in the anim section; its `duration` slider now edits the shared static too.

Model: `moveSpeed → step → measured horSpeed → walkSpeed → gaitTime`. One direction only — nothing feeds back upstream.

## 5. Explored & ROLLED BACK (kept in git history, Oct 29)

Movement-feel pass that was tried and reverted to baseline per user — documented so the ideas aren't re-litigated:

- **Launch/arrival ramps** (`moveAccel`/`moveDecel`/`arriveSlowM` + `curSpeed`) — worked mechanically, but short combat legs spent most of their length in the ramp → fighters read as citizen-stroll. Rolled back to the direct step.
- **`turnSpeedDeg` 540→400** — rolled back with it.
- **`gaitScale`** (cadence-per-speed knob replacing the hardcoded 0.5) — removed; the fixed `×0.5` coupling remains.
- **Velocity smoothing** for gait rate — removed with it.

Baseline restored = direct `moveSpeed × DuckSpeedFactor × dt` step, `turnSpeedDeg 540`. The default then moved 2.4 → **0.85 m/s** as the user-chosen base-walk foundation.

## 6. Open / future work

- **Tiered move speeds** — per-leg-type pace (recon-peek creep / tactical advance / reload-cover-break hustle). The leg call sites already know their type; one multiplier per caller. Deferred — user wants base simple speed first.
- **Transition-duration tuning** — longer pose blends for more flowing posture changes (interruption is already free via transFrom freeze).
- **Lean/bank on turns, turn-in-place, stop-settle** — polish layer, deferred.
- **Citizens/possess unification** — extend the universal field (or a shared settings struct) to `PedestrianController` and the possess drive channel when those movers are touched next.
- **Determinism note** — movement integrates on `Time.deltaTime` per frame (real-time combat architecture); decisions gate on `SimClock`. Replay determinism holds at the decision layer, not per-frame positions.

---

*Keywords: movement, locomotion, StepToward, StepRouted, MoveWithCollision, moveSpeed, gait, gaitTime, walkSpeed, foot-locked, DuckSpeedFactor, turnSpeedDeg, arriveEps, stuck guard, min distance, camera detach, universal speed*
