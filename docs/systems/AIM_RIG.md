# Aim Rig — one focal point drives torso, head, arm(s) and weapon

**Status**: 📐 SPEC (not implemented) — born from the eye/muzzle mismatch found in the first auto dice duels
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
| Arm | One arm + forearm channel (`ikGidA/B`) | A second pair for the support arm (widen `_InstanceIKData` stride, shader + buffer) |
| Head | `headYawOverride` on the **CPU animator only**; no pitch/tilt override; no match in `CharacterPoseCompute.compute` | Head yaw/pitch/tilt override on CPU **and** GPU **and** JS animator. **Verify the rendered head follows the CPU head** — if not, the probe eye already diverges from what is drawn |
| Anchors | `SW_Model_10`: `grip_right`, `muzzle`, `bore_root` | `grip_left`, `cheek`, `right_shoulder` per weapon (docs already plan them); rifle/shotgun items to author |
| Eye | `TryGetEyeAnchor` (Eyes centroid + 1.5 cm) | One eye definition for probe, cone, rise solve (today: live pose, idle pose, crouch pose) |

## 5. Phases (each independently testable)

1. **Head parity** — CPU/GPU/JS head yaw+pitch+tilt overrides; verify rendered head == CPU head (probe eye).
2. **Torso stance + head look from one `F`** — existing torso channel; gate: `muz-eye(r u f)` lateral offset in the dice trace drops from ~0.12-0.20 m toward ~0.05 m.
3. **Aim-line arm solve** — bore on `L`; current solve as fallback; gate: `e/m/b` gap (eye-visible vs muzzle-visible) closes.
4. **Two-handed** — second IK channel pair + rifle/shotgun anchor authoring; gate: bore line from two grips, stable under sway.
5. **Cleanup** — rise solve reads the aimed eye/muzzle (not idle-pose eye + `sightGap`); retire `sightGap` as a constant; keep the muzzle shootable probe as a runtime safety net.

## 6. Verification

- Dice trace already logs `muz-eye(r u f)`, `eye→t / muz→t` LOS and `e/m/b` (eye / muzzle / both visible). Use before/after per phase on the same arena duel.
- Offline: eye/shoulder/muzzle numbers from `Civilian1.json` + `SW_Model_10.json` (script, no Unity).
- Visual: aim pose review in `character_animator.html` (JS parity) before Unity.

## 7. Open questions

1. Handedness: does the rig mirror per `handedness`, or is gun side a per-character property?
2. Crouch/deep-duck bands: torso assist is disabled there today (it un-folds the crouch lean — `IK_TORSO_OVERRIDE`). The rig needs a duck-aware torso solve instead of a hard gate.
3. Locomotion: root owns heading while walking. Is aim-stance blending allowed during `AimWalk`, or only when parked?
4. Animator authoring: do `aiming` params (`torsoTwist`, `headYaw/Pitch/Tilt`, per `weaponType` preset) become the *rest* the rig solves from, or are they replaced by it?
