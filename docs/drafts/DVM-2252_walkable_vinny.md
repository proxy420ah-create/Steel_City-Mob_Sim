# DVM-2252 — Walkable Vinny in the Office v0.2

**Status**: tabled — OPENINGS · **Floor**: ARCHITECT · **Revives**: DVM-822 (amends, not replaces — the authored pipeline made the 822 spec obsolete)

**Ask (boss, verbatim)**: load the authored `voxel_characters/Civilian1.json` rig (10 bodyGroups, pivots, animParams, states) into the room as a third-person controlled character. Idle default; walk gait on move; Unity-parity speeds — walk 2.5 m/s, sprint ×2.2, turnSpeed 10, camera-relative WASD. Scope: character serve route, JS poser port (per-group FK), WASD controller, floor bounds.

## Verified file-side (this recon)

- `Assets/StreamingAssets/voxel_characters/Civilian1.json` — 96³ dims @ .01 world scale, **10 bodyGroups** (Body, Head, L/R Arm, L/R Leg, L/R Forearm, L/R Shin — same rig topology as the old Vinny), `animParams` param blocks (walkDuck*, crouch*, aiming), `attachmentPoints`, `itemParts`. Authored schema — no binary STAG decode needed (822's blocker ② is dead).
- Room voxel pipeline: `/voxel/<name>` → `voxel_items/<name>.json`, Lambert + InstancedMesh, instanced per seat. The character needs its own route + its own render path (posed, not instanced).

## Design — five pieces, one seam each

**① Serve route** — `GET /char/<name>` → `Assets/StreamingAssets/voxel_characters/<name>.json`, same JSON static path as `/voxel/`. ~10 lines in `commission_server.py` beside the voxel route.

**② Poser port (the fidelity core)** — one `InstancedMesh` per bodyGroup, transformed about the group's pivot: `M = T(pivot_world) · R(pose) · T(−pivot_local)`. Pose = deterministic function `pose(state, t)` mapping animParams blocks onto the rig — **idle** (rest) and **walk** (phase-advanced legStride/kneeBend/armSwing). Walk cycle = phase `φ = (distance / strideLen) mod 1` — gait keyed to ground truth, not wall-clock, so speed changes don't footskate.

**③ Controller** — camera-relative WASD: `dir = camBasis × input`, `vel = dir × speed`, `yaw → heading` at turnSpeed 10 rad/s. Constants ported verbatim: `WALK = 2.5`, `SPRINT = 2.5 × 2.2`, `TURN = 10`. Shift = sprint. State machine: `speed > ε → WALK else IDLE` (event-driven on input, never time-driven).

**④ Floor bounds + collision** — capsule (radius ~0.15 m, height ~1.7 m) vs the furniture AABB set (table, 7 chairs, cabinet, walls) + room floor rectangle. Analytic pushback, no physics lib: `pos = clamp(resolve_capsule_AABB(pos))` per frame.

**⑤ Camera mode** — third-person follow rig behind heading, OR deck-mode toggle. Guest-clamped room cam stays the default; possession enters/exits cleanly (Esc or deck button).

## Explicitly out of scope (v0.2 boundary)

Sit-binding to the chair (animParams state exists — separate item), aiming/crouch param sets, other characters, feed coupling (the character never touches the bus — cosmetic layer, 822's ruling stands).

## Acceptance predicate (the live drill)

1. `GET /char/Civilian1` serves the JSON (200, 10 bodyGroups).
2. Character spawns at room floor, Idle pose, zero input.
3. WASD moves camera-relative; walk gait advances with distance (no skate at 2.5 vs sprint).
4. Heading turns at 10 rad/s toward velocity; sprint = ×2.2 walk speed, verified against a known distance.
5. Capsule stops on furniture AABBs; cannot exit the floor rectangle.
6. Cam toggle returns cleanly to guest-clamped view.

## Surface estimate

serve route ~10 · poser ~120 (the eval port) · controller ~60 · bounds ~50 · cam mode ~40 = **~280 lines HTML+server, zero schema/feed changes.**
