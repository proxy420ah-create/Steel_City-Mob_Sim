# Aim Rig — one focal point drives torso, head, arm(s) and weapon

**Status**: ✅ PHASES 1–3 IMPLEMENTED + PLAYTEST-VALIDATED (bore-on-line confirmed Oct 29 — muzzle on the eye line, `muz-eye` lateral closed); phase-4 channel plumbing shipped (dormant — awaits a long-gun asset with `grip_left`); phase 5 cleanup + rise-solve reuse outstanding (head parity + torso stance — blading reads correctly per playtest); phase-4 channel plumbing shipped (dormant); phases 3–5 solve work spec — born from the eye/muzzle mismatch found in the first auto dice duels
**Owner systems**: `ArmAimSolver`, `WeaponMount`, `VoxelCharacterAnimator`, `CharacterPoseCompute.compute`, `CombatController` (consumer)
**Related**: `ANIMATION_SYSTEM.md` (parity contract), `WEAPON_ATTACHMENT_SYSTEM.md` (anchors), `EXPOSURE_ENGAGEMENT.md`, `PERCEPTION_AWARENESS.md`

---

## 1. Problem (measured from assets + code, not guessed)

Today the arm IK aims the **bore direction along shoulder → target** and lets the muzzle land wherever the arm ends. Nothing ties the muzzle to the eye.

Civilian1 rest pose (1 voxel = 1 cm) and `SW_Model_10`:

| Point | Position |
|---|---|
| Eye (Eyes-region centroid) | x 47.5, **y 55.5**, z 51 |
| Gun-side shoulder pivot | x 35 (or 60), **y 45**, z 47 — i.e. **12.5 cm lateral**, 10.5 cm below, 5.5 cm behind the eye |
| Arm | ~23 cm total (L1 12.4 + L2 10.6), reach clamped to 97 % |
| Revolver grip → muzzle | ~25 cm along the barrel, +6.75 cm up |

Consequences:
- Muzzle sits ~4 cm below the eye (this is what `sightGap = 0.045` models) **and ~12.5 cm to the gun side** (modeled nowhere).
- Eye sees past a vertical wall edge while the gun-side arm is still behind it -> `see=1` + `bore buried` (side-dependent).
- `SolveRiseAtSpot` certifies stands by eye height only, evaluated at the **idle** pose (`TryGetEyeAnchor(…, 0f, 0f, 0f)`), not the aiming pose.
- Poses read robotic: torso gets yaw only (`torsoShare` 0.6, off while walking / deep duck), head is yaw-only via a CPU-only override, arms do all the fine work.

The runtime fix that shipped first (`muzzleShootGate`: shootable = eye ∧ muzzle visible) is a safety net, not the cure.

## 2. Goal

**Solve the aim line first, then pose the body to serve it.** One focal point `F`; one line `L = eye → F`; torso, head, arm(s) and weapon all organize around `L`.

Targets:
1. Muzzle within a few cm of `L` (eye↔muzzle gap shrinks from ~15 cm to a few cm) so *aware ≈ shootable*.
2. Natural stance: bladed torso + head turned back onto the target, not a turret.
3. Same rig serves one-handed pistols and two-handed rifles/shotguns.
4. CPU / GPU / JS poses stay in parity (the CPU pose places the probe eye and the weapon weld).

## 3. Solve order (per frame, from `F`)

1. **Head** — look along `L`: yaw, pitch, tilt, neck clamp ±1.35 rad (existing). Output: head quaternion.
2. **Torso** — yaw (and a little pitch/lean) toward the gun side so the gun shoulder approaches `L`; share limited by the neck clamp so head + torso together reach `F`. Output: gid-0 quaternion (the GPU channel already takes a full quaternion).
3. **Arm** — hand target `H` such that the **bore axis lies on `L`** (offset by `sightGap`): `H = Lpoint − gripToBore`. Reuse the law-of-cosines elbow; the forearm basis aligns the bore to `L`. Fallback to today's shoulder→target solve when `H` is unreachable (arm length 23 cm: expect a strongly bent elbow, pistol near the cheek).
4. **Support arm (two-handed)** — hand to the weapon's `grip_left`; the pair of grips defines the bore line and steadies it. Stock to `right_shoulder` / `cheek` anchors when authored.
5. **Aim feel** (sway/recoil) stays a perturbation of `F` before step 1, as now.

## 4. Infrastructure gaps (verified by reading, to be confirmed at runtime)

| Piece | Today | Needed |
|---|---|---|
| Torso | Full quaternion on the GPU (`ikTorsoQuat`, flag bit 2), propagates to head + arms; solver feeds yaw only | Pitch/lean/stance solve — **no shader change** |
| Arm | One arm + forearm channel (`ikGidA/B`) | ~~A second pair for the support arm~~ -> **shipped**: stride-7 `_InstanceIKData`, pair C/D + flag bit 3; the two-handed support-arm SOLVE remains |
| Head | ~~`headYawOverride` CPU-only, yaw-only~~ -> **done**: unified quat channel - `ikOverrides[1]` (CPU) = `ikHeadQuat` flag bit 2 (GPU `_InstanceIKData` slot 3) = JS `ikAim.headTrack`; written via `VoxelCharacter.SetHeadAim` / `ArmAimSolver.headTrack`. Buffer is now **stride 7** (`[A,B,torso,head,C,D,meta]`, four gids nibble-packed in `meta.x`) — the second arm pair (flag bit 3) shipped as phase-4 plumbing during the reload work | ✅ validated: rendered head tracks the threat through cover-seek legs and aim |
| Anchors | `SW_Model_10`: `grip_right`, `muzzle`, `bore_root` | `grip_left`, `cheek`, `right_shoulder` per weapon (docs already plan them); rifle/shotgun items to author |
| Eye | `TryGetEyeAnchor` (Eyes centroid + 1.5 cm) | One eye definition for probe, cone, rise solve (today: live pose, idle pose, crouch pose) |

## 5. Phases (each independently testable)

1. **Head parity** - done in code: `_InstanceIKData` stride 4->5, head quat slot + flag bit 2 in `CharacterPoseCompute.compute`; `ArmAimSolver.headTrack` solves `qHead` (yaw +/-1.35 rad, pitch +/-0.9, `BasisDelta(forward->dir)`) in the same pre-torso frame as the arm; `CombatController` eyes-on-threat drives `SetHeadAim` (yaw-only quat) whenever the solver is not aiming. **Gate remaining**: rendered head == CPU head (probe eye) on both fighters - check AIM & SIGHT `sight`/`bore`/`eye->tgt` beams while aiming and during cover-seek legs.
2. **Torso stance + head look from one `F`** - done in code: `torsoQuat` composes yaw blade (neck-clamp share-up - torso absorbs past +/-77.2 deg so head+torso reach `F`) + **stance blade** (`torsoBladeShare` 0.5 / `torsoBladeMaxDeg` 40 deg: waist yaw swinging the gun shoulder's azimuth toward the aim azimuth - boxing-stance lead, SignedAngle = handedness-correct) + modest stance pitch (`torsoPitchShare` 0.25, cap 12 deg, sign param). NOTE: an earlier roll-about-the-aim-axis "lean" was reverted on sight - it tilted the whole torso sideways; blading is waist yaw, not roll. Gates: suspended while WALKING UN-AIMED (root owns heading) / deep duck; AimWalk keeps the stance (bladeWhileAimWalk) - upper body tracks the target while legs run the route. **Gate**: `muz-eye(r u f)` lateral drops toward ~0.05 m; fighter visibly blades into the shot.
3. **Aim-line arm solve** — **IMPLEMENTED** (compile-verified, awaiting playtest): `TrySolveBoreOnLine` in `ArmAimSolver`. With the hand orientation fixed by the bore direction, the muzzle can only sit on a sphere of radius = upper-arm length around `sv + qCum·(forearm + grip→muzzle offset)` — so the sight line meets it in a line∩sphere solve (`AimRigGeometry.MuzzleOnLine`: far intersection when reachable, nearest point otherwise — graceful degrade, no full fallback). Bore direction converges on F (2 refinement passes for the weak coupling). Solved in the pre-torso rest frame; the eye is the Eyes/Face rest centroid posed through the head solve (head block moved before the arm), +1.5 vox proud. Gates: deep-duck band (`duck>1.02`), headTrack 0, no muzzle anchor → legacy solve. `boreOnLine` [0..1] blend (default 1), `sightGapM` 0.045, `minMuzzleAheadM` 0.15. New accessors: `WeaponMount.HasMuzzle/MuzzleFromGripLocal`, `VoxelCharacter.TryGetEyeRest`. Gate metrics: `LastMuzzleLineGapM` (live welded muzzle → eye-target line, the true measure), `LastBoreResidualM` (solved miss), `LastBoreOnLine`. Verified: headless compile only — playtest: `muz-eye(r u f)` lateral ~0.05m, `e/m/b` gap closes; watch point-blank (parallel-bore guard <2 vox).
4. **Two-handed** — channel pair shipped (stride 7 + `ikPair2Enabled`); remains: the support-arm solve to `grip_left` + rifle/shotgun anchor authoring; gate: bore line from two grips, stable under sway.
5. **Cleanup** — rise solve reads the aimed eye/muzzle (not idle-pose eye + `sightGap`); retire `sightGap` as a constant; keep the muzzle shootable probe as a runtime safety net.

## 6. Verification

- Dice trace already logs `muz-eye(r u f)`, `eye→t / muz→t` LOS and `e/m/b` (eye / muzzle / both visible). Use before/after per phase on the same arena duel.
- Offline: eye/shoulder/muzzle numbers from `Civilian1.json` + `SW_Model_10.json` (script, no Unity).
- Visual: aim pose review in `character_animator.html` (JS parity) before Unity.

## 7. Open questions

1. Handedness: does the rig mirror per `handedness`, or is gun side a per-character property? — *partial answer shipped with the reload work*: `reloadGunSide` (±1) resolves per-character from `mount.HandGid`'s parent arm gid and mirrors the authored reload params by **role** (support vs gun). The aim solve can reuse the same resolution.
2. Crouch/deep-duck bands: torso assist is disabled there today (it un-folds the crouch lean — `IK_TORSO_OVERRIDE`). The rig needs a duck-aware torso solve instead of a hard gate.
3. ~~Locomotion: root owns heading while walking. Is aim-stance blending allowed during `AimWalk`, or only when parked?~~ -> **answered**: `AimWalk` keeps the stance (`bladeWhileAimWalk`, default on — aiming at a live target while moving makes the upper body track while legs run the route); plain `Walking` still suspends it.
4. Animator authoring: do `aiming` params (`torsoTwist`, `headYaw/Pitch/Tilt`, per `weaponType` preset) become the *rest* the rig solves from, or are they replaced by it?
