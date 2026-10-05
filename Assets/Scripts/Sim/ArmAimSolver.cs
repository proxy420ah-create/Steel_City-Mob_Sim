using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Closed-form 2-bone arm IK for the voxel-group rig — ports the SteelTide
    /// ArmIKDrive math (law-of-cosines elbow placement, twist-constrained joint
    /// bases, reach clamping) onto the compute-shader pose pipeline.
    ///
    /// Instead of driving ConfigurableJoints it writes model-space rotation
    /// overrides into two channels that must stay in sync:
    ///   1. VoxelCharacterAnimator.ikOverrides — CPU pose (drives the WeaponMount
    ///      weld, so the muzzle follows the solved hand exactly).
    ///   2. InstancedCharacter.ik* fields — packed into _InstanceIKData and
    ///      applied inside CharacterPoseCompute.CSPose for the rendered voxels.
    ///
    /// Shoulder (arm gid) solves elbow placement via law of cosines; forearm
    /// (hand gid) slams the measured bore axis (bore_root → muzzle) onto the
    /// target line directly — the weapon's own authored geometry is the aim
    /// truth, not an assumed hand axis.
    /// </summary>
    public class ArmAimSolver : MonoBehaviour
    {
        [Header("Tuning")]
        [Tooltip("Fraction of full arm extension allowed — keeps the elbow slightly bent at max reach")]
        [Range(0.5f, 1f)] public float reachFraction = 0.97f;
        [Tooltip("Blend ramp speed on engage (higher = snappier raise)")]
        public float blendInSpeed = 2.5f;
        [Tooltip("Blend ramp speed on release")]
        public float blendOutSpeed = 3.5f;
        [Tooltip("Elbow preferred direction in model space — projected perp to the aim line")]
        public Vector3 elbowHint = new Vector3(0.4f, -1f, 0.2f);
        [Tooltip("Roll reference for the upper arm basis (model space)")]
        public Vector3 armTwistRef = new Vector3(0f, 0f, 1f);
        [Tooltip("Azimuth offset applied to the target direction (deg, model space Y)")]
        public float azimuthOffsetDeg = 0f;
        [Tooltip("Elevation offset applied to the target direction (deg)")]
        public float elevationOffsetDeg = 0f;

        [Header("Torso Assist (turret)")]
        [Tooltip("Rotate the upper body (gid 0) toward the target — torso takes the gross yaw, arm does fine aim")]
        public bool torsoAssist = true;
        [Tooltip("Fraction of the yaw error the torso absorbs; the arm solves the residual")]
        [Range(0f, 1f)] public float torsoShare = 0.6f;
        [Tooltip("Max torso yaw (deg)")]
        public float maxTorsoYawDeg = 70f;
        [Tooltip("Sign flip if the torso yaws away from the target (model-forward convention)")]
        public float torsoYawSign = 1f;
        public bool debugLogs = false;

        // --- State ---
        public bool IsAiming { get; private set; }
        public float Blend { get; private set; }
        public Vector3 TargetWorld { get; private set; }
        public string LastError { get; private set; }
        public float LastAngularErrorDeg { get; private set; }
        public float LastTargetDistanceM { get; private set; }
        public int ArmGid { get; private set; } = -1;
        public int ForearmGid { get; private set; } = -1;

        private VoxelCharacter character;
        private WeaponMount mount;
        private VoxelChunkManager.InstancedCharacter instHandle;
        private string sightBeamKey;
        private Quaternion torsoQuat = Quaternion.identity; // gid-0 override from torso assist

        /// <summary>Engage the solver toward a world-space target. Call once per toggle-on.</summary>
        public bool Arm(Vector3 targetWorld)
        {
            string err = Validate();
            if (err != null)
            {
                LastError = err;
                Debug.LogWarning($"[ArmAimSolver] Arm rejected: {err}");
                return false;
            }
            TargetWorld = targetWorld;
            IsAiming = true;
            LastError = null;
            if (debugLogs)
                Debug.Log($"[ArmAimSolver] Engaged — arm gid {ArmGid}, forearm gid {ForearmGid}, target {targetWorld}");
            return true;
        }

        /// <summary>Release — blend ramps down, then the override channel clears.</summary>
        public void Disarm()
        {
            IsAiming = false;
            if (debugLogs) Debug.Log("[ArmAimSolver] Released");
        }

        /// <summary>Move the aim point while engaged (also safe pre-arm).</summary>
        public void SetTarget(Vector3 targetWorld) => TargetWorld = targetWorld;

        /// <summary>Null when the solver has everything it needs; otherwise a failure reason.</summary>
        public string Validate()
        {
            if (character == null) character = GetComponent<VoxelCharacter>();
            if (character == null || !character.IsInitialized) return "no initialized VoxelCharacter";
            if (mount == null) mount = GetComponent<WeaponMount>();
            if (mount == null || !mount.IsEquipped) return "no weapon mounted";
            if (mount.Animator == null) return "mount animator not ready";
            if (mount.HandGid < 0) return "no hand attachment gid";

            int fore = mount.HandGid;
            int arm = mount.Animator.GetParentGroup(fore);
            if (!mount.Pivots.ContainsKey(fore)) return $"no pivot for hand group {fore}";
            if (arm >= 0 && !mount.Pivots.ContainsKey(arm)) return $"no pivot for arm group {arm}";
            ForearmGid = fore;
            ArmGid = arm; // -1 → single-bone fallback (bore aimed at the shoulder group itself)
            return null;
        }

        void Update()
        {
            if (!IsAiming && Blend <= 0f) return;

            Blend = Mathf.MoveTowards(Blend, IsAiming ? 1f : 0f,
                Time.deltaTime * (IsAiming ? blendInSpeed : blendOutSpeed));

            string err = Validate();
            if (err != null)
            {
                // Context lost mid-aim (weapon unequipped etc) — release rather than freeze.
                LastError = err;
                IsAiming = false;
            }
            else if (IsAiming) LastError = null;

            if (Blend > 0.0001f && ForearmGid >= 0 && mount != null && mount.Animator != null)
            {
                Solve(TargetWorld, out Quaternion qArm, out Quaternion qFore);
                WriteChannels(qArm, qFore);
            }

            if (!IsAiming && Blend <= 0f)
            {
                if (mount != null && mount.Animator != null)
                {
                    mount.Animator.ikOverrides.Clear();
                    mount.Animator.ikBlend = 1f;
                }
                if (instHandle != null) { instHandle.ikEnabled = false; instHandle.ikTorsoEnabled = false; }
            }
        }

        /// <summary>
        /// Solve shoulder + forearm ownRot overrides in uncentered voxel space
        /// (the frame the FK chain rotates in). qFore is invalid when ArmGid &lt; 0.
        /// </summary>
        private void Solve(Vector3 targetWorld, out Quaternion qArm, out Quaternion qFore)
        {
            qArm = Quaternion.identity;
            qFore = Quaternion.identity;

            var dims = mount.Dims;
            float vs = mount.CharVoxelSize;
            Vector3 dimsF = new Vector3(dims.x, dims.y, dims.z);

            // Same world↔voxel bridge as WeaponMount's weld:
            // world = center + yawRot * ((v + 0.5)*vs - half)
            Vector3 half = dimsF * vs * 0.5f;
            Vector3 center = transform.position + half;
            Quaternion yawRot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

            Vector3 sv = ScalePivot(mount.Pivots[ArmGid >= 0 ? ArmGid : ForearmGid], dimsF);
            Vector3 tv = WorldToVoxel(targetWorld, center, yawRot, half, vs);

            // Torso assist (turret): yaw gid 0 toward the target about the body
            // pivot, then solve the arm in the PRE-torso frame — the chain applies
            // bodyRot outermost (T * qArm * qFore), so transforming the target by
            // the effective inverse yaw keeps the bore landing exactly.
            torsoQuat = Quaternion.identity;
            if (torsoAssist)
            {
                Vector3 stFlat = tv - sv;
                stFlat.y = 0f;
                if (stFlat.sqrMagnitude > 1e-6f)
                {
                    float yawErr = Vector3.SignedAngle(Vector3.forward, stFlat, Vector3.up);
                    float yawDeg = Mathf.Clamp(yawErr * torsoYawSign * torsoShare, -maxTorsoYawDeg, maxTorsoYawDeg);
                    torsoQuat = Quaternion.AngleAxis(yawDeg, Vector3.up);
                    Quaternion torsoEff = Quaternion.Slerp(Quaternion.identity, torsoQuat, Blend);
                    Vector3 bp = mount.Pivots.TryGetValue(0, out var bp0)
                        ? ScalePivot(bp0, dimsF)
                        : dimsF * 0.5f;
                    tv = bp + Quaternion.Inverse(torsoEff) * (tv - bp);
                }
            }

            // Az/el tuning offsets rotate the target about the shoulder
            if (azimuthOffsetDeg != 0f || elevationOffsetDeg != 0f)
            {
                Vector3 toRaw = tv - sv;
                Vector3 up = Vector3.up;
                Vector3 right = Vector3.Cross(up, toRaw.normalized);
                if (right.sqrMagnitude > 1e-6f)
                    toRaw = Quaternion.AngleAxis(azimuthOffsetDeg, up)
                          * Quaternion.AngleAxis(elevationOffsetDeg, right.normalized)
                          * toRaw;
                tv = sv + toRaw;
            }

            Vector3 st = tv - sv;
            float dist = st.magnitude;
            Vector3 toT = dist > 1e-6f ? st / dist : Vector3.forward;
            LastTargetDistanceM = dist * vs;

            // Weapon bore axis in the hand group's model-space frame:
            // worldBore = yawRot * qR * basisB * attachRot * boreAxisLocal
            Vector3 K = (mount.BasisB * mount.ItemAttachRot * mount.BoreAxisLocal).normalized;
            Vector3 wUp = mount.BasisB * mount.ItemAttachRot * Vector3.up;

            // Desired cumulative rotation on the hand's group: bore → toT, sights → up.
            Quaternion qRDesired = BasisDelta(K, toT, wUp, Vector3.up);

            if (ArmGid < 0)
            {
                // No elbow available — slam the bore axis at the hand's own group.
                qArm = qRDesired;
            }
            else
            {
                Vector3 ev = ScalePivot(mount.Pivots[ForearmGid], dimsF);
                Vector3 hv = mount.HandPoint;
                float L1 = (ev - sv).magnitude;
                float L2 = (hv - ev).magnitude;

                if (L1 < 1e-4f || L2 < 1e-4f)
                {
                    qArm = qRDesired; // degenerate chain — single-bone fallback
                }
                else
                {
                    // Law-of-cosines elbow: clamp reach, place elbow in the bend plane
                    float d = Mathf.Clamp(dist, Mathf.Abs(L1 - L2) + 0.01f, (L1 + L2) * reachFraction);
                    float cosA = Mathf.Clamp((L1 * L1 + d * d - L2 * L2) / (2f * L1 * d), -1f, 1f);
                    float angA = Mathf.Acos(cosA);
                    Vector3 bendRef = PerpOf(elbowHint, toT);
                    Vector3 elbowDir = (toT * Mathf.Cos(angA) + bendRef * Mathf.Sin(angA)).normalized;

                    // Shoulder: bind arm direction → solved elbow direction, twist-stable
                    Vector3 bindArm = (ev - sv).normalized;
                    qArm = BasisDelta(bindArm, elbowDir, armTwistRef, armTwistRef);

                    // Forearm: cumulative must equal qRDesired → ownRot = inv(qArm) * qRDesired
                    qFore = Quaternion.Inverse(qArm) * qRDesired;
                }
            }

            // Real angular error: welded bore dir vs muzzle→target (1 frame stale — ground truth)
            Vector3 toTargetWorld = targetWorld - mount.MuzzleWorld;
            if (mount.IsEquipped && toTargetWorld.sqrMagnitude > 1e-8f)
                LastAngularErrorDeg = Vector3.Angle(mount.AimDirection, toTargetWorld);
        }

        /// <summary>Push the solve into both the CPU animator (weld) and GPU per-instance channel.</summary>
        private void WriteChannels(Quaternion qArm, Quaternion qFore)
        {
            var animator = mount.Animator;
            animator.ikBlend = Blend;
            int armSlot = ArmGid >= 0 ? ArmGid : ForearmGid;
            animator.ikOverrides[armSlot] = qArm;
            if (ArmGid >= 0) animator.ikOverrides[ForearmGid] = qFore;
            // Torso override rides gid 0 — propagates to arm/forearm chains via
            // the body-transform path (outermost rotation in the FK chain).
            if (torsoAssist) animator.ikOverrides[0] = torsoQuat;
            else animator.ikOverrides.Remove(0);

            instHandle = character.GetInstancedHandle(); // re-fetch — handle can change on re-registration
            if (instHandle != null)
            {
                instHandle.ikEnabled = true;
                instHandle.ikBlend = Blend;
                instHandle.ikGidA = armSlot;
                instHandle.ikQuatA = qArm;
                instHandle.ikGidB = ArmGid >= 0 ? ForearmGid : -1;
                instHandle.ikQuatB = ArmGid >= 0 ? qFore : Quaternion.identity;
                instHandle.ikTorsoEnabled = torsoAssist;
                instHandle.ikTorsoQuat = torsoQuat;
            }

            // Sight beam — muzzle → target while engaged (residual aim error is
            // visible against WeaponMount's red bore ray).
            var pdr = PathDebugRenderer.Instance;
            if (pdr != null)
            {
                if (sightBeamKey == null) sightBeamKey = $"aim_sight_{GetInstanceID()}";
                if (IsAiming)
                    pdr.SetCustomBeam(sightBeamKey, mount.MuzzleWorld, TargetWorld,
                        0.006f, new Color(0.2f, 1f, 1f, 0.55f));
                else
                    pdr.ClearCustomBeam(sightBeamKey);
            }
        }

        // --- Math helpers (voxel-space, matching animator/shader conventions) ---

        private static Vector3 ScalePivot(Vector3 normalizedPivot, Vector3 dimsF)
            => new Vector3(normalizedPivot.x * dimsF.x, normalizedPivot.y * dimsF.y, normalizedPivot.z * dimsF.z);

        /// <summary>Inverse of the weld's world = center + yawRot * ((v+0.5)*vs - half).</summary>
        private static Vector3 WorldToVoxel(Vector3 w, Vector3 center, Quaternion yawRot, Vector3 half, float vs)
        {
            Vector3 local = Quaternion.Inverse(yawRot) * (w - center);
            return (local + half) / vs - Vector3.one * 0.5f;
        }

        private static Vector3 PerpOf(Vector3 refV, Vector3 f)
        {
            Vector3 u = refV - f * Vector3.Dot(refV, f);
            if (u.sqrMagnitude < 1e-6f)
                u = Vector3.Cross(f, Mathf.Abs(f.y) < 0.9f ? Vector3.up : Vector3.right);
            return u.normalized;
        }

        /// <summary>
        /// Twist-stable rotation taking bindF→solF (SteelTide SolveJointRotationWithTwist port):
        /// full bases built from projected refs, so no arbitrary roll about the bone axis.
        /// refB/refS are projected perp to bindF/solF respectively.
        /// </summary>
        private static Quaternion BasisDelta(Vector3 bindF, Vector3 solF, Vector3 refB, Vector3 refS)
        {
            Quaternion bBind = Quaternion.LookRotation(bindF, PerpOf(refB, bindF));
            Quaternion bSol = Quaternion.LookRotation(solF, PerpOf(refS, solF));
            return bSol * Quaternion.Inverse(bBind);
        }


    }
}
