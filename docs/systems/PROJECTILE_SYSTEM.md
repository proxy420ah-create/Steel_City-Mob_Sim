# Projectile System — Steel City: Mob Sim

**Created**: October 11, 2026
**Status**: 🟢 v1 implemented and playtested (Oct 11, 2026) — physical projectiles + voxel-DDA hits against terrain and building volumes confirmed at the firing range
**Design source**: `COMBAT_VEHICLE_DESIGN.md` §13 (physical projectiles) and `COMBAT_AUTOBATTLE.md` (cone of fire, cover as a physical ray)
**Code**: `ProjectileSystem.cs`, `VoxelDda.cs`, `SolidVolumeSet.cs`, `VoxelCollisionWorld.Raycast`, Combat tab in `DebugHUDManager.cs`

---

## What it does

A shot leaves the weapon's **measured muzzle** along its **bore axis** — i.e. wherever the IK solver actually pointed the gun, residual aim error included — perturbed by a **seeded random cone**. The bullet then flies at `muzzleSpeed`; each frame the segment it covered is ray-cast against the voxel world (terrain grid + every registered building volume), so a fast bullet cannot tunnel through a thin wall. On impact it records a `ShotReport`: exact volume, voxel cell, material id, face normal, distance, flight time, and how far the shot line passed from the intended target.

No Rigidbody, no Collider, no PhysX. Not here yet: character/vehicle hits, penetration, damage, bullet holes (see **Next steps**).

```
WeaponMount.MuzzleWorld / AimDirection          (ArmAimSolver has already slammed the bore onto the target line)
        │  Fire(mount, aimErr, intendedTarget)
        ▼
ProjectileSystem ── seeded cone (skill × base cone) ──► Live projectile (pos, vel)
        │  every frame: Raycast(pos, dir, speed·dt)   ← swept segment, no tunnelling
        ▼
VoxelCollisionWorld.Raycast
   ├─ terrain  : VoxelDda over the flat byte[] grid
   └─ volumes  : SolidVolumeSet → per-building AABB broadphase → VoxelDda over the SHARED uint[] asset array
        │  nearest hit
        ▼
ShotReport (HUD + console) + impact tick via PathDebugRenderer beams
```

---

## Shooter model — skill 1–10 and the cone

`half-angle = baseConeDeg × SkillSpreadMultiplier(skill)`, `SkillSpreadMultiplier = 1 + (10 − skill) × 0.75`.

| Skill | ×Cone | Cone at base 1.0° | Spread radius @ 7.75 m | Perfect-aim result at the range target* |
|---|---|---|---|---|
| **10** (default) | 1.00 | ±1.00° | 0.14 m | 69 % bullseye · 31 % paper · 0 % off |
| 8 | 2.50 | ±2.50° | 0.34 m | 11 % · 67 % · 22 % |
| 5 | 4.75 | ±4.75° | 0.64 m | 3 % · 38 % · 59 % |
| 1 | 7.75 | ±7.75° | 1.06 m | 1 % · 15 % · 84 % |

\* Monte-Carlo (200k shots) against the real target geometry — paper 24×9 voxels (1.2 × 0.45 m), bullseye 4×4 voxels (0.2 × 0.2 m) — assuming the bore points exactly at the bullseye centre. Real results shift with the IK **aim err** shown in the report.

- Defaults: skill **10**, base cone **1.0°** (slider 0–5°), speed **40 u/s** (slider 5–150).
- The skill value is a **placeholder** for the hood's Firearms stat; when hoods get real stats, bind `shooterSkill` to it (the cone formula in `COMBAT_AUTOBATTLE.md` also wants weapon base inaccuracy and a movement penalty — not wired yet).
- Spread is uniform over the cone's disc of directions (`θ = half·√u₁`, `φ = 2π·u₂`). It is deterministic per seed: the report prints the seed; set `ProjectileSystem.forcedSeed` ≠ 0 to repeat the identical spread on every shot.
- Speed is in **world units per second** — the city is a miniature, real-world m/s do not apply. ~40 u/s crosses the 7.8 m range in ≈ 0.2 s (visible tracer).

---

## Hit world — conventions (must match the renderer)

| Item | Convention |
|---|---|
| Index | `x + y·w + z·w·h` (`VoxelProxyRaymarch.shader` `VoxelIndex`) |
| Solid | `(voxel & 0x1FF) != 0` (`VX_MATERIAL_MASK`). Shape (bits 12–15) and rotation (9–11) bits are **ignored** — every non-air voxel is a full cube |
| Cell → world | cell `i` spans `[corner + i·vs, corner + (i+1)·vs]`; `corner` = world min corner of the grid (sector `_BuildingPositions.xyz`) |
| Yaw (optional) | rotates the volume about its centre (`Quaternion.Euler(0,yaw,0)`, WeaponMount/shader convention). Sector-baked buildings use yaw 0 |
| Hit normal | axis-aligned face normal pointing back toward the ray origin; **zero** if the origin starts inside a solid cell |
| Per-axis bounds | every axis is checked before the flat index is formed (VOXEL_ENGINE_GOTCHAS #1, index aliasing) |

`VoxelDda` is a pure Amanatides–Woo traversal (doubles in, ints out, no Unity types); `SolidVolumeSet` is the registry + broadphase; `VoxelCollisionWorld.Raycast(origin, dir, maxDist, out VoxelRayHit, includeTerrain, includeVolumes)` is the single entry point (also the right call for cover/LOS queries later).

### Memory model — instancing-shaped

A volume is a **reference** to a voxel array that already exists for rendering (the packed `.stasset` cache or a pooled debris variant) plus a placement. 786 buildings of a handful of assets cost 786 small records and **no copied voxels** — the same "shared asset data, per-instance transform" shape the GPU side uses. Treat the arrays as read-only; destructive edits (bullet holes) must copy-on-write and re-register the volume (`RegisterVolume` replaces by id).

### Where volumes come from

| Render path | Registration |
|---|---|
| **Sector baking** (`useSectorBaking`, scene default) | `SectorBaker.BakeSector` records a `PlacedBuilding` per building (placement + reference to the array its sector region was filled from — for pooled empty-lot debris, the *writer's* variant, exactly what is rendered). `CityMap3D.RegisterSectorPlacements` feeds them to the collision world **and the address registry** |
| **Per building** | `BuildingFootprint.data` (new field, set by every `LoadChunkCentered*`) → `CityMap3D.RegisterBuildingVolume` |
| Rebake (debris toggle) | sector: re-register volumes by id (addresses unchanged); per-building: re-register after reload |
| `BuildMap` | clears volumes at the top together with the address registry |

Volume ids use the per-building convention (`{blockId}_building` for a block's only building, `{blockId}_building_{i}` otherwise), so `ShotReport.target` names the block you hit.

> **Related fix (same change):** sector mode used to leave the address registry empty, which silently disabled every building attachment point (`firing_position` / `firing_target`) — see `known_issues/runtime/SECTOR_MODE_ADDRESS_REGISTRY_EMPTY.md`.

---

## ShotReport

`id, seed, skill, coneHalfDeg, spreadDeg (angle actually drawn), aimErrorDeg (IK residual at the trigger pull), muzzle, aimDir (bore), shotDir (after cone), hit, target (volume id | "terrain" | "none"), material, cell, point, normal, distance (travelled path), flightTime, hasIntended, intendedTarget, lateralMiss (shot line ↔ target, closest approach), impactOffset (impact point ↔ target)`.

Surfaces: Combat tab (last shot), console line `[Projectile] #n HIT …`, and `ProjectileSystem.ShotResolved` / `LastShot` for code. This is exactly the payload later systems need — glass puncture wants the pane voxel (`material` 112–114/147 reports "(glass)"), cover wants the blocking volume, damage wants the material.

---

## Combat tab (Debug HUD) — Firearm panel

`skill (1-10)` · `cone ° @10` · `speed u/s` · effective-cone readout · **FIRE** · last-shot report.

FIRE is enabled only while the sim is Dwelling, a weapon is equipped, and **IK AIM is engaged with blend ≥ 0.98** — so the shot goes where the gun really points. The trigger is a button for now (hotkey / auto-fire later).

---

## Verification

**Offline (no Unity)** — `Tools/dda_selftest/run.sh` compiles `VoxelDda.cs` + `SolidVolumeSet.cs` with the editor-bundled Roslyn/.NET 6 and runs **148,016 checks**: DDA vs exhaustive brute force (108k random rays over 6 grid shapes × 3 densities, cells + t + normals), axis-aligned/edge cases, byte vs uint grids, registry (replace/swap-remove/nearest), yawed volumes vs an independent oriented-box reference (9.6k rays), and real shots through `shooting_range.json`:

| Shot (aim point) | Result |
|---|---|
| bullseye centre (`firing_target`) | Red Brick (100), cell (96,8,169), normal −Z |
| paper, off-centre | White Fabric (127), z = 169 |
| over the top of the target | backstop Red Brick (100) at z = 184 |
| floor in front | Concrete (102), y = 2, normal +Y |
| straight up | no hit |

Cost: **≈ 1.7 µs per shot** through the 192×120×192 range grid; the linear broadphase over ~790 volumes is a few µs.

**In editor** — compile check: Editor and Player-dev `Assembly-CSharp` build with Unity's own Roslyn, zero errors/warnings. Playtest checklist: see `RECENT_CHANGES.md` (Oct 11 — projectiles v1).

**Expected at the range:** muzzle→target ≈ 7.8 m, flight ≈ 0.2 s at 40 u/s; skill 10 / 1°: every shot on the paper, ≈ 2/3 on the bullseye (material 100, cells x94–97, y6–9, z169) when `aim err` is small; paper = material 127 (x84–107, y4–12); overshoots land on the red-brick backstop (z ≥ 184).

**Multi-lane range + ballistics bench (Oct 11):** `gen_shooting_range.py` now emits **three identical lanes** (dx −60/0/+60 → interiors x16–55 / x76–115 / x136–175) with per-lane points `firing_position_1..3`, `firing_target_1..3`, `backstop_1..3` — the unsuffixed names stay as lane-2 aliases for back-compat. **Lane 3 (right) carries the glass test bench**: an 8³ Window Glass (112) "ballistics mold" socketed into a dark-wood table at the 10 m mark, centroid **(155.5, 7.5, 112.5)** — exactly the `firing_target` aim height — with a 2³ Painted-Metal core at the same point painted `glass_target`, so the aim attachment survives the glass being destroyed. The Combat tab gets **lane (1-3) + aim (lane target / glass mold)** selectors; `target_practice` orders resolve the selected lane's point names. Glass still stops the bullet (first cell) until the shatter mechanic ships.

---

## Known limitations / next steps

1. **Characters and vehicles are not hit yet.** Planned per `COMBAT_VEHICLE_DESIGN.md` §13: an instance spatial hash queried along the same swept segment; `Raycast` already provides the world-geometry half.
2. **Aim parallax.** `ArmAimSolver` slams the bore axis parallel to the *shoulder→target* line, not muzzle→target, so a small residual (`aim err`, also shown in the report) is expected at close range. If it matters, iterate the solve from the muzzle position.
3. **Shape bits ignored** (slopes/half-blocks count as full cubes) and **no penetration** — the bullet stops at the first non-air voxel; the report carries the material so penetration tables (`COMBAT_AUTOBATTLE.md` data schema) can slot in.
4. **Glass puncture — P0 shipped (per-building chunks only)** (`GLASS_SHATTER_MECHANIC.md`): `VoxelDamageSystem` subscribes to `ShotResolved`; glass hits copy-on-write the volume array, run `VoxelDamageMath.Puncture` (impact + seeded ring-1 → AIR, survivors → 134 Cracked Glass), re-register the volume by id, and push dirty spans to the chunk buffer via `VoxelChunkManager.UploadVoxelEdits`. Sector-baked volumes are skipped (merged buffers unreachable) — sector damage is the next design problem.
5. **Broadphase** is linear over volumes — fine for a few shots per frame; put a coarse grid in front of it before mass fire or per-frame LOS for crowds.
6. **Gravity** exists (`gravity`, default 0). No ricochet, no suppression, no sound/noise events yet.
7. **Aim target is a constant** — testbed aim points still use `feet + offset`; designed replacement: centroid of the *target's visible posed voxels* (rays from the shooter's eye, same `Raycast` primitive) — also the FIRE/WAIT gate and pop-up/last-seen doctrine (`COMBAT_COVER_STATE.md` Phase 2).

---

## Aim feel — sway + recoil (COMBAT_VEHICLE_DESIGN.md §14, implemented)

A second-order spring (`AimFeelSpring`) injects feel into the aim channel *inside* `ArmAimSolver`: the spring offsets the target point about the muzzle **before** the IK solve, so torso assist and the elbow both chase the perturbed point and `WeaponMount` reports the deflected bore axis.

- **Sway**: two-octave Perlin wander of the spring equilibrium. Amplitude `swayDeg × SkillSpreadMultiplier(skill)` — a steady shooter sways less.
- **Recoil**: `ProjectileSystem.ShotFired` (new event, fires at trigger pull) → `feelSpring.Impulse(kick)` — 65% instant displacement + velocity carry. Kick is mostly up with a shot-seeded lateral component. `settleSec` scales ~0.3× per missing skill point.
- **Stacking, bounded**: an impulse while the spring is still displaced adds velocity to a non-zero position — rapid fire sprays progressively wider — but `pos`/`vel` are hard-clamped (`maxOffsetDeg`, anti-windup à la the SteelTide PID `iMax`) and the impulse snap scales by remaining headroom, so hammering FIRE saturates instead of flailing.
- **Stability**: integration substeps at a fixed 1/120 s (explicit Euler on stiff springs explodes on a single hitchy frame — the PID ran in `FixedUpdate` for the same reason); `dampingRatio` defaults to slightly overdamped (1.05) so recovery deadens instead of ringing; the sway octaves are kept low-frequency so the arm corrects calmly rather than chasing jitter.
- The trigger samples `AimDirection` *before* the impulse lands, so the kick only deflects follow-up shots.
- **Live wobble**: `solver.AimOffsetDeg` (current spring offset) + `aimErrorDeg` in the shot report both rise while unsettled.
- Combat tab: `aim feel` toggle + `sway ° @10`, `kick °`, `settle s @10` sliders; `feelSkill` mirrors the skill slider until hood stats exist.

## Spread cone viz

`showSpreadCone` draws a wireframe cone (6 edge rays + rim ring) at `EffectiveConeDeg` around the live `AimDirection` via `PathDebugRenderer` beams. It follows the actual bore axis, so it dances with sway/recoil and reshapes as the sliders move — the spread envelope is finally visible.
