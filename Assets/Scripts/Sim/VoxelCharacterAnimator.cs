using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// C# port of character_animator.html's computeGroupRotation + rebuildAnimatedMesh.
    /// Performs forward-transform voxel posing: takes rest-pose voxels + groupIDs + anim params
    /// and produces posed voxel positions. This is the single source of truth for character
    /// animation, replacing the shader's inverse-transform approach.
    ///
    /// HTML state IDs (used here, NOT Unity's CharacterAnimation enum):
    ///   0=Idle, 1=Walking, 2=Looking, 3=AimWalk, 4=Aiming, 5=Crouching, 8=Down, 9=T-Pose, 10=Aim Sweep
    /// </summary>
    public class VoxelCharacterAnimator
    {
        // ---- Animation parameters (loaded from .anim.json) ----
        public AnimParamsData paramsData;
        public Dictionary<int, Vector3> pivots = new Dictionary<int, Vector3>(); // gid -> normalized pivot (0.0-1.0)
        public Dictionary<int, Vector3> jointOffsets = new Dictionary<int, Vector3>(); // gid -> offset (voxel units)

        // ---- Cached pose state ----
        private WalkPose _walkPoseCache;
        private float _walkKeyTime = float.NaN, _walkKeySpeed = float.NaN;   // NaN never equals — a cold cache always misses

        // ---- Constants ----
        private static readonly Dictionary<int, int> PARENT_OF = new Dictionary<int, int>
        {
            { 8, 2 }, { 9, 3 }, { 6, 4 }, { 7, 5 }
        };
        public int GetParentGroup(int gid) => PARENT_OF.TryGetValue(gid, out var p) ? p : -1;

        // --- IK override channel (solver-driven, e.g. ArmAimSolver) ---
        // Model-space rotation that REPLACES a group's param-derived ownRot.
        // Children still inherit through the FK chain; ikBlend ramps 0→1 for
        // a smooth engage/release. GPU path mirrors this via _InstanceIKData.
        public readonly Dictionary<int, Quaternion> ikOverrides = new Dictionary<int, Quaternion>();
        public float ikBlend = 1f;
        // Look-scan channel — the authored `looking` head-yaw oscillation
        // layered over any state's head pose (crouched alert sentry =
        // Idle + duck + scan). Yields to IK overrides while aiming.
        public bool lookScanEnabled = false;
        public float lookScanBlend = 1f;
        // Reload tap channel — ReloadGestureSolver pulses 0..1 per RoundSeated;
        // the reload states add authored tap* deltas on top. GPU twin = animData[3].x.
        public float tapPulse = 0f;
        // Reload gun side — +1 when the mount's gun arm is gid 3, -1 when gid 2.
        // Reload arm params are role-assigned (support arm = L column, gun arm =
        // R column); direction-sensitive terms (reach/twist) mirror by this sign.
        // Written per-eval from the instanced handle (GPU twin = animData[3].y).
        public float reloadGunSide = 1f;
        // Head-aim rides the shared quaternion channel: ikOverrides[1], written
        // by VoxelCharacter.SetHeadAim (ArmAimSolver while aiming — full yaw/
        // pitch track; CombatController while seeking — yaw). The SAME quat goes
        // to the GPU ikHeadQuat slot, so the rendered head == the CPU head that
        // places the probe eye (AIM_RIG phase 1). The override replaces the
        // head's whole ownRot — producers compose yaw/pitch/tilt themselves.
        // (headYawOverride retired: it was CPU-only, yaw-only, and the drawn
        // head silently diverged from the probe eye.)
        private static readonly int[] CHILD_GROUPS = { 6, 7, 8, 9 };
        private static readonly int[] UPPER_BODY_GROUPS = { 1, 2, 3 };
        private static readonly int[] ROOT_PARENT_GROUPS = { 1, 2, 3, 4, 5 };

        #region Math Helpers (ported from character_animator.html)

        private static float[,] RotationX(float angle)
        {
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            return new float[,] { { 1, 0, 0 }, { 0, c, -s }, { 0, s, c } };
        }

        private static float[,] RotationY(float angle)
        {
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            return new float[,] { { c, 0, -s }, { 0, 1, 0 }, { s, 0, c } };
        }

        private static float[,] RotationZ(float angle)
        {
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            return new float[,] { { c, -s, 0 }, { s, c, 0 }, { 0, 0, 1 } };
        }

        private static float[,] RotationByAxis(int axis, float angle)
        {
            if (axis == 1) return RotationY(angle);
            if (axis == 2) return RotationZ(angle);
            return RotationX(angle);
        }

        private static float[,] MatMul3(float[,] a, float[,] b)
        {
            var r = new float[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    for (int k = 0; k < 3; k++)
                        r[i, j] += a[i, k] * b[k, j];
            return r;
        }

        private static Vector3 MatVec3(float[,] m, Vector3 v)
        {
            return new Vector3(
                m[0, 0] * v.x + m[0, 1] * v.y + m[0, 2] * v.z,
                m[1, 0] * v.x + m[1, 1] * v.y + m[1, 2] * v.z,
                m[2, 0] * v.x + m[2, 1] * v.y + m[2, 2] * v.z
            );
        }

        /// <summary>float[,] 3x3 (row-major, MatVec3 convention) → Quaternion.</summary>
        public static Quaternion QuatFromMat3(float[,] m)
        {
            float tr = m[0, 0] + m[1, 1] + m[2, 2];
            float w, x, y, z;
            if (tr > 0f)
            {
                float s = Mathf.Sqrt(tr + 1f) * 2f;
                w = 0.25f * s;
                x = (m[2, 1] - m[1, 2]) / s;
                y = (m[0, 2] - m[2, 0]) / s;
                z = (m[1, 0] - m[0, 1]) / s;
            }
            else if (m[0, 0] > m[1, 1] && m[0, 0] > m[2, 2])
            {
                float s = Mathf.Sqrt(1f + m[0, 0] - m[1, 1] - m[2, 2]) * 2f;
                w = (m[2, 1] - m[1, 2]) / s;
                x = 0.25f * s;
                y = (m[0, 1] + m[1, 0]) / s;
                z = (m[0, 2] + m[2, 0]) / s;
            }
            else if (m[1, 1] > m[2, 2])
            {
                float s = Mathf.Sqrt(1f + m[1, 1] - m[0, 0] - m[2, 2]) * 2f;
                w = (m[0, 2] - m[2, 0]) / s;
                x = (m[0, 1] + m[1, 0]) / s;
                y = 0.25f * s;
                z = (m[1, 2] + m[2, 1]) / s;
            }
            else
            {
                float s = Mathf.Sqrt(1f + m[2, 2] - m[0, 0] - m[1, 1]) * 2f;
                w = (m[1, 0] - m[0, 1]) / s;
                x = (m[0, 2] + m[2, 0]) / s;
                y = (m[1, 2] + m[2, 1]) / s;
                z = 0.25f * s;
            }
            return new Quaternion(x, y, z, w).normalized;
        }

        /// <summary>Quaternion → float[,] 3x3 (row-major, matches RotationX/Y/Z convention).</summary>
        public static float[,] Mat3FromQuat(Quaternion q)
        {
            float x = q.x, y = q.y, z = q.z, w = q.w;
            return new float[,]
            {
                { 1f - 2f * (y * y + z * z), 2f * (x * y - z * w),     2f * (x * z + y * w)     },
                { 2f * (x * y + z * w),     1f - 2f * (x * x + z * z), 2f * (y * z - x * w)     },
                { 2f * (x * z - y * w),     2f * (y * z + x * w),     1f - 2f * (x * x + y * y) }
            };
        }

        private static readonly float[,] IDENTITY = new float[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

        private static float Smoothstep01(float t) { return t * t * (3f - 2f * t); }
        private static float CosineInterp(float t) { return 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI); }

        private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        #endregion

        #region Walk Keyframe Interpolation (ported from getWalkPose)

        public struct WalkPose
        {
            public Dictionary<string, float> pose;
            public float cyclePhase;
            public int kfAIdx, kfBIdx;
            public float interpT;
            public float bodyBobY;
            public float weightShiftX;
        }

        private static WalkKFPose MirrorWalkPose(WalkKFPose pose)
        {
            return new WalkKFPose
            {
                armSwingL = pose.armSwingR,     armSwingR = pose.armSwingL,
                legStrideL = pose.legStrideR,   legStrideR = pose.legStrideL,
                elbowBendL = pose.elbowBendR,   elbowBendR = pose.elbowBendL,
                kneeBendL = pose.kneeBendR,     kneeBendR = pose.kneeBendL,
                forearmTwistL = pose.forearmTwistR, forearmTwistR = pose.forearmTwistL,
            };
        }

        private static readonly WalkKFPose ZERO_WALK_POSE = new WalkKFPose();
        private bool _warnedNullKf = false;

        private WalkKFPose GetWalkKfPose(int idx, WalkKeyframesData wkf)
        {
            // kf0/kf1 CAN be null: JsonUtility materializes a non-null
            // WalkKeyframesData for a partial "walkKeyframes" object, so the
            // "section missing → default fill" check doesn't fire. A null slot
            // must never reach GetKFPoseValue (per-frame NRE inside WeaponMount's
            // LateUpdate = the weapon freezing at its last welded transform).
            WalkKFPose kf;
            if (idx == 0) kf = wkf.kf0;
            else if (idx == 1) kf = wkf.kf1;
            else if (idx == 2) kf = wkf.kf2 ?? (wkf.autoMirror && wkf.kf0 != null ? MirrorWalkPose(wkf.kf0) : wkf.kf0);
            else if (idx == 3) kf = wkf.kf3 ?? (wkf.autoMirror && wkf.kf1 != null ? MirrorWalkPose(wkf.kf1) : wkf.kf1);
            else kf = wkf.kf0;

            if (kf == null)
            {
                if (!_warnedNullKf)
                {
                    Debug.LogWarning($"[VCA] walkKeyframes kf{idx} is null — using zero pose (animParams export is missing keyframes)");
                    _warnedNullKf = true;
                }
                kf = ZERO_WALK_POSE;
            }
            return kf;
        }

        /// <summary>Walk-cycle period in seconds (0 when no gait data) —
        /// for worst-of-gait top-of-silhouette sampling.</summary>
        public float WalkCycleDuration => paramsData?.walkKeyframes?.cycleDuration ?? 0f;

        private WalkPose GetWalkPose(float animTime, float animSpeed, AnimParamsData p)
        {
            // One-entry cache keyed on the raw inputs. The key used to be built as
            // `animTime.ToString("F4") + ":" + animSpeed.ToString("F2")` — three string
            // allocations per call, on EVERY posed point in a walking state. Same-frame
            // callers pass identical floats, so the hit rate is unchanged.
            if (_walkPoseCache.pose != null && animTime == _walkKeyTime && animSpeed == _walkKeySpeed)
                return _walkPoseCache;

            var wkf = p.walkKeyframes;
            float cycleDur = wkf.cycleDuration / Mathf.Max(0.01f, animSpeed);
            float cyclePhase = ((animTime % cycleDur) + cycleDur) % cycleDur / cycleDur;

            float[] kfPositions = { 0f, 0.25f, 0.5f, 0.75f };
            int kfAIdx = 0, kfBIdx = 1;
            float t = 0f;

            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                float posA = kfPositions[i];
                float posB = kfPositions[next];
                if (next == 0)
                {
                    if (cyclePhase >= posA)
                    {
                        kfAIdx = i; kfBIdx = 0;
                        t = (cyclePhase - posA) / (1.0f - posA);
                        break;
                    }
                    else if (cyclePhase < posB)
                    {
                        kfAIdx = 3; kfBIdx = 0;
                        t = (cyclePhase + (1.0f - 0.75f)) / (1.0f - 0.75f + 0.0f);
                        break;
                    }
                }
                else
                {
                    if (cyclePhase >= posA && cyclePhase < posB)
                    {
                        kfAIdx = i; kfBIdx = next;
                        t = (cyclePhase - posA) / (posB - posA);
                        break;
                    }
                }
            }

            var kfA = GetWalkKfPose(kfAIdx, wkf);
            var kfB = GetWalkKfPose(kfBIdx, wkf);
            string interpMode = wkf.interpolation ?? "spline";

            int prevKfIdx = (kfAIdx - 1 + 4) % 4;
            int next2KfIdx = (kfBIdx + 1) % 4;
            var kfPrev = interpMode == "spline" ? GetWalkKfPose(prevKfIdx, wkf) : default(WalkKFPose);
            var kfNext2 = interpMode == "spline" ? GetWalkKfPose(next2KfIdx, wkf) : default(WalkKFPose);

            float smoothT = interpMode == "smoothstep" ? Smoothstep01(t)
                : interpMode == "cosine" ? CosineInterp(t)
                : t;

            var pose = new Dictionary<string, float>();
            string[] keys = { "armSwingL", "armSwingR", "legStrideL", "legStrideR",
                              "elbowBendL", "elbowBendR", "kneeBendL", "kneeBendR",
                              "forearmTwistL", "forearmTwistR" };
            foreach (var key in keys)
            {
                float valA = GetKFPoseValue(kfA, key);
                float valB = GetKFPoseValue(kfB, key);
                if (interpMode == "spline")
                {
                    float valPrev = GetKFPoseValue(kfPrev, key);
                    float valNext2 = GetKFPoseValue(kfNext2, key);
                    pose[key] = CatmullRom(valPrev, valA, valB, valNext2, t);
                }
                else
                {
                    pose[key] = valA + (valB - valA) * smoothT;
                }
            }

            float bobAmp = (wkf.bodyBob != null && wkf.bodyBob.enabled) ? wkf.bodyBob.amplitude : 0f;
            float bodyBobY = -Mathf.Cos(cyclePhase * 4f * Mathf.PI) * bobAmp;

            float shiftAmp = (wkf.weightShift != null && wkf.weightShift.enabled) ? wkf.weightShift.amplitude : 0f;
            float weightShiftX = Mathf.Sin(cyclePhase * 2f * Mathf.PI) * shiftAmp;

            var result = new WalkPose
            {
                pose = pose,
                cyclePhase = cyclePhase,
                kfAIdx = kfAIdx,
                kfBIdx = kfBIdx,
                interpT = smoothT,
                bodyBobY = bodyBobY,
                weightShiftX = weightShiftX
            };
            _walkPoseCache = result;
            _walkKeyTime = animTime; _walkKeySpeed = animSpeed;
            return result;
        }

        private static float GetKFPoseValue(WalkKFPose p, string key)
        {
            if (p == null) return 0f;
            switch (key)
            {
                case "armSwingL": return p.armSwingL;
                case "armSwingR": return p.armSwingR;
                case "legStrideL": return p.legStrideL;
                case "legStrideR": return p.legStrideR;
                case "elbowBendL": return p.elbowBendL;
                case "elbowBendR": return p.elbowBendR;
                case "kneeBendL": return p.kneeBendL;
                case "kneeBendR": return p.kneeBendR;
                case "forearmTwistL": return p.forearmTwistL;
                case "forearmTwistR": return p.forearmTwistR;
                default: return 0f;
            }
        }

        #endregion

        #region Transform Chain (ported from computeGroupRotation)

        public struct TransformEntry
        {
            public Vector3 pivot;
            public float[,] rot;
        }

        public struct GroupTransformResult
        {
            public TransformEntry[] chain;
            public Vector3 offset;
        }

        /// <summary>
        /// Compute the transform chain for a given group.
        /// Direct port of character_animator.html computeGroupRotation().
        /// Returns null if no transform applies (T-Pose or unhandled state).
        ///
        /// `duck` = THE shared scalar 0..2 (0 upright · 1 shallow · 2 deep):
        ///   Idle    → icr() — authored hold pose crouching→crouchDeep
        ///   Walking → wd()  — crumple deltas walkDuckMid→walkDuckPose under the gait
        ///   State 5.x (legacy) → crouchDepth axis at duck−1 (1+d ≡ old depth d)
        /// `gaitTime` = integrated gait clock (dt×speed accumulated) — phase never
        /// teleports on speed change; -1 falls back to animTime×animSpeed.
        /// </summary>
        public GroupTransformResult? ComputeGroupRotation(
            int gid, Vector3Int dims, float voxelSize,
            float animState, float animTime, float animSpeed,
            float duck = 0f, float gaitTime = -1f)
        {
            // T-Pose: raw bind pose — NO rotations applied
            if (animState > 8.5f && animState < 9.5f) return null;

            var ap = paramsData;
            // Adaptive crouch: the legacy state-5.x depth axis. duck 1+d maps
            // to old depth d (saturated) — identical pose for legacy callers.
            var cdeep = ap.crouchDeep ?? ap.crouching;
            float Cd = Mathf.Clamp01(duck - 1f);
            float Cr(float s, float d) => Mathf.Lerp(s, d, Cd);

            // === SHARED DUCK SCALAR — dual endpoint sets (prototype parity) ===
            // wd(): walkDuckMid lands at duck 1, walkDuckPose at duck 2 —
            // deltas composed UNDER the gait. GAIT states: Walking + AimWalk —
            // the fold must not vanish just because the arm is out (cover
            // posture overrides opportunity fire; LOS loss stows him).
            // icr(): authored hold pose crouching→crouchDeep — STATIONARY
            // states: Idle + Aiming (the solved riseDuck must render).
            bool isWalkAnim = (animState > 0.5f && animState < 1.5f)
                           || (animState > 2.5f && animState < 3.5f);
            float duckIn = isWalkAnim ? Mathf.Min(1f, duck) : 0f;
            float duckDeep = Mathf.Max(0f, duck - 1f);
            var wdm = ap.walkDuckMid ?? ap.crouching;
            var wdp = ap.walkDuckPose ?? ap.crouching;
            float Wd(System.Func<CrouchingData, float> f) => Mathf.Lerp(f(wdm), f(wdp), duckDeep) * duckIn;
            bool isStationaryAnim = (animState > -0.5f && animState < 0.5f)
                                 || (animState > 3.5f && animState < 4.5f);
            float idleIn = isStationaryAnim ? Mathf.Min(1f, duck) : 0f;
            float idleDp = duckDeep;
            float Icr(System.Func<CrouchingData, float> f) => Mathf.Lerp(f(ap.crouching), f(cdeep), idleDp) * idleIn;
            float gaitClock = gaitTime >= 0f ? gaitTime : animTime * animSpeed;
            WalkPose GetGaitPose() => GetWalkPose(gaitClock, 1f, ap);

            // === BODY TRANSFORM (torso twist + crouch lean + crouch lower) ===
            bool isSweepState = animState > 9.5f && animState < 10.5f;            // Aim Sweep (10)
            bool isAimingState = (animState > 2.5f && animState < 4.5f) || isSweepState; // Aim Walk (3), Aiming (4), Aim Sweep (10)
            // Aim Sweep: shoulder-yaw sinusoid on the aiming arm + gaze follow (port of
            // character_animator.html state 10). Sweeps only the arm whose aim swing is
            // non-zero, so it lands on the armed side regardless of handedness.
            float sweepYaw = isSweepState ? Mathf.Sin(animTime * ap.aimSweep.freq) * ap.aimSweep.amp : 0f;
            bool isCrouchingState = animState > 4.5f && animState < 5.5f; // Crouching (5)
            bool isSittingState = animState > 10.5f && animState < 11.5f; // Sit (11)
            bool isReloadState = animState > 12.5f && animState < 13.5f;          // Reload (13)
            bool isReloadCrouchState = animState > 13.5f && animState < 14.5f;    // Reload Crouch (14)
            var rl = (isReloadCrouchState && ap.reloadCrouch != null ? ap.reloadCrouch : ap.reload)
                     ?? ap.reload;   // reloadCrouch falls back to the shared block
            float torsoTwist = isAimingState ? ap.aiming.torsoTwist
                : (isReloadState || isReloadCrouchState) ? rl.torsoTwist : 0f;
            bool isCrouchBody = isCrouchingState || isReloadCrouchState;
            float bodyLean = isCrouchBody ? Cr(ap.crouching.bodyLean, cdeep.bodyLean) : Wd(d => d.bodyLean) + Icr(d => d.bodyLean);
            float bodyLower = isCrouchBody ? Cr(ap.crouching.bodyLower, cdeep.bodyLower) : Wd(d => d.bodyLower) + Icr(d => d.bodyLower);
            float modelLower = isCrouchBody ? Cr(ap.crouching.modelLower, cdeep.modelLower) : Wd(d => d.modelLower) + Icr(d => d.modelLower);
            float bodyForward = isCrouchBody ? Cr(ap.crouching.bodyForward, cdeep.bodyForward) : Wd(d => d.bodyForward) + Icr(d => d.bodyForward);
            bool hasBodyRot = torsoTwist != 0f || bodyLean != 0f;
            // Torso IK override (gid 0) — participates in bodyRot so it propagates
            // through the same body-transform path into arm/forearm/head chains.
            bool hasTorsoIk = ikOverrides.Count > 0 && ikOverrides.ContainsKey(0);
            if (hasTorsoIk) hasBodyRot = true;

            float[,] GetBodyTransform()
            {
                if (!hasBodyRot) return null;
                if (!pivots.ContainsKey(0)) return null;
                // Compose: Y(twist) × X(lean) — twist applied after lean
                float[,] rot = IDENTITY;
                if (bodyLean != 0f) rot = MatMul3(RotationX(bodyLean), rot);
                if (torsoTwist != 0f) rot = MatMul3(RotationY(torsoTwist), rot);
                if (hasTorsoIk)
                    rot = Mat3FromQuat(Quaternion.Slerp(QuatFromMat3(rot), ikOverrides[0], Mathf.Clamp01(ikBlend)));
                return rot;
            }

            Vector3 bodyOffset = new Vector3(0, -bodyLower - modelLower, bodyForward);

            // Group 0 (body): apply body rotation + lower offset
            if (gid == 0)
            {
                var bodyRot = GetBodyTransform();
                if (bodyRot == null && bodyLower == 0f && modelLower == 0f && bodyForward == 0f) return null;
                var bodyPivot = pivots.ContainsKey(0)
                    ? new Vector3(pivots[0].x * dims.x * voxelSize, pivots[0].y * dims.y * voxelSize, pivots[0].z * dims.z * voxelSize)
                    : Vector3.zero;
                var bodyChain = bodyRot != null
                    ? new TransformEntry[] { new TransformEntry { pivot = bodyPivot, rot = bodyRot } }
                    : new TransformEntry[0];
                return new GroupTransformResult { chain = bodyChain, offset = bodyOffset };
            }

            // Parent-child FK: get parent's transform chain first
            GroupTransformResult? parentResult = null;
            if (PARENT_OF.ContainsKey(gid))
            {
                parentResult = ComputeGroupRotation(PARENT_OF[gid], dims, voxelSize, animState, animTime, animSpeed, duck, gaitTime);
            }

            if (!pivots.ContainsKey(gid)) return parentResult;
            var p = pivots[gid];
            var ownPivot = new Vector3(p.x * dims.x * voxelSize, p.y * dims.y * voxelSize, p.z * dims.z * voxelSize);

            // Offset: children inherit parent's offset; parent groups use their own jointOffset
            Vector3 offset;
            if (parentResult.HasValue)
            {
                offset = parentResult.Value.offset;
            }
            else
            {
                var off = jointOffsets != null && jointOffsets.ContainsKey(gid)
                    ? jointOffsets[gid]
                    : Vector3.zero;
                offset = new Vector3(off.x, off.y, off.z);
            }
            // Add bodyLower to upper body parent groups (head, arms) — not legs
            if (bodyLower != 0f && (gid == 1 || gid == 2 || gid == 3))
                offset.y -= bodyLower;
            // bodyForward is the Z twin — torso assembly (head, arms) shifts fwd/back
            if (bodyForward != 0f && (gid == 1 || gid == 2 || gid == 3))
                offset.z += bodyForward;
            // Add modelLower to ALL root parent groups (1-5) — children inherit via parentResult
            if (modelLower != 0f && (gid == 1 || gid == 2 || gid == 3 || gid == 4 || gid == 5))
                offset.y -= modelLower;

            // === COMPUTE THIS GROUP'S OWN ROTATION ===
            float[,] ownRot = null;

            if (gid == 1) // Head
            {
                float headYaw = 0f, headPitch = 0f, headTilt = 0f;
                if (animState > 1.5f && animState < 2.5f) // Looking
                {
                    var lp = ap.looking;
                    headYaw = Mathf.Sin(animTime * lp.headYawFreq) * lp.headYaw;
                    headPitch = Mathf.Sin(animTime * lp.headPitchFreq) * lp.headPitch;
                }
                else if (isAimingState) // Aim Walk (3), Aiming (4), Aim Sweep (10)
                {
                    var aim = ap.aiming;
                    headYaw = aim.headYaw + sweepYaw * ap.aimSweep.headFollow; headPitch = aim.headPitch; headTilt = aim.headTilt;
                }
                else if (isReloadState || isReloadCrouchState) // Reload (13/14) — eyes to the work
                {
                    headYaw = rl.headYaw; headPitch = rl.headPitch; headTilt = rl.headTilt;
                }
                else if (animState > 4.5f && animState < 5.5f) // Crouching
                {
                    headPitch = Cr(ap.crouching.headPitch, cdeep.headPitch);
                }
                else if (isWalkAnim && duck > 0f) // Walk-duck — head deltas under the gait
                {
                    headPitch = Wd(d => d.headPitch);
                    headTilt = Wd(d => d.headTilt);
                }
                else if (idleIn > 0f) // Idle hold — authored crouch head
                {
                    headPitch = Icr(d => d.headPitch);
                }
                else if (isSittingState) // Sit
                {
                    headPitch = ap.sitting.headPitch;
                }
                else
                {
                    ownRot = IDENTITY; // Idle: head at rest
                }

                // Look-scan channel — yaw-only sweep layered over ANY state's
                // head pose (crouched sentry = Idle + duck + scan). Yields to
                // IK aim: the eye-line belongs to the weapon while overrides run.
                if (lookScanEnabled && lookScanBlend > 0f && ap.looking != null && ikOverrides.Count == 0)
                    headYaw += Mathf.Sin(animTime * ap.looking.headYawFreq) * ap.looking.headYaw * lookScanBlend;
                // (head overrides arrive as a full quat on ikOverrides[1] — applied below)

                if (ownRot == null)
                    ownRot = MatMul3(RotationY(headYaw), MatMul3(RotationX(headPitch), RotationZ(headTilt)));
            }
            else if (gid == 2) // Left arm (shoulder)
            {
                var asc = ap.armSwing;
                float swing = 0f;
                float reach = 0f;
                if (animState > 0.5f && animState < 1.5f) // Walking — keyframe pose
                {
                    var wp = GetGaitPose();
                    swing = asc.signL * (wp.pose["armSwingL"] + Wd(d => d.armSwingL));
                }
                else if (isAimingState) // Aim Walk (3), Aiming (4), Aim Sweep (10)
                {
                    swing = asc.signL * ap.aiming.armSwingL;
                    reach = ap.aiming.shoulderReachL + (swing != 0f ? sweepYaw : 0f);
                }
                else if (isReloadState || isReloadCrouchState) // Reload (13/14) — hold + tap delta
                {
                    // Role-assigned: L column = support arm, R column = gun arm.
                    // This arm (gid 2) is the gun arm when reloadGunSide < 0.
                    bool gunArm = reloadGunSide < 0f;
                    float gs = reloadGunSide >= 0f ? 1f : -1f;
                    swing = asc.signL * (gunArm ? rl.armSwingR : rl.armSwingL);
                    reach = gs * ((gunArm ? rl.shoulderReachR : rl.shoulderReachL)
                                + tapPulse * (gunArm ? rl.tapReachR : rl.tapReachL));
                }
                else if (animState > 4.5f && animState < 5.5f) // Crouching
                {
                    swing = asc.signL * Cr(ap.crouching.armSwingL, cdeep.armSwingL);
                }
                else if (isSittingState) // Sit
                {
                    swing = asc.signL * ap.sitting.armSwingL;
                }
                else
                {
                    swing = asc.signL * Icr(d => d.armSwingL); // Idle hold deltas (0 elsewhere)
                }
                if (ownRot == null)
                    ownRot = MatMul3(RotationY(reach), MatMul3(RotationByAxis(asc.axisL, swing), RotationZ(ap.restPose.leftArmZ)));
            }
            else if (gid == 3) // Right arm (shoulder)
            {
                var asc = ap.armSwing;
                float swing = 0f;
                float reach = 0f;
                if (animState > 0.5f && animState < 1.5f) // Walking — keyframe pose
                {
                    var wp = GetGaitPose();
                    swing = asc.signR * (wp.pose["armSwingR"] + Wd(d => d.armSwingR));
                }
                else if (isAimingState) // Aim Walk (3), Aiming (4), Aim Sweep (10)
                {
                    swing = asc.signR * ap.aiming.armSwingR;
                    reach = ap.aiming.shoulderReachR + (swing != 0f ? sweepYaw : 0f);
                }
                else if (isReloadState || isReloadCrouchState) // Reload (13/14) — hold + tap delta
                {
                    // Role-assigned (mirror of the gid-2 branch): this arm is
                    // the gun arm when reloadGunSide > 0.
                    bool gunArm = reloadGunSide > 0f;
                    float gs = reloadGunSide >= 0f ? 1f : -1f;
                    swing = asc.signR * (gunArm ? rl.armSwingR : rl.armSwingL);
                    reach = gs * ((gunArm ? rl.shoulderReachR : rl.shoulderReachL)
                                + tapPulse * (gunArm ? rl.tapReachR : rl.tapReachL));
                }
                else if (animState > 4.5f && animState < 5.5f) // Crouching
                {
                    swing = asc.signR * Cr(ap.crouching.armSwingR, cdeep.armSwingR);
                }
                else if (isSittingState) // Sit
                {
                    swing = asc.signR * ap.sitting.armSwingR;
                }
                else
                {
                    swing = asc.signR * Icr(d => d.armSwingR); // Idle hold deltas (0 elsewhere)
                }
                if (ownRot == null)
                    ownRot = MatMul3(RotationY(reach), MatMul3(RotationByAxis(asc.axisR, swing), RotationZ(ap.restPose.rightArmZ)));
            }
            else if (gid == 4) // Left leg (hip) — stride + Y twist
            {
                var ls = ap.legStride;
                float twist = ap.legTwist.leftRest;
                float stride = 0f;
                if ((animState > 0.5f && animState < 1.5f) || (animState > 2.5f && animState < 3.5f)) // Walking or Aim Walk
                {
                    var wp = GetGaitPose();
                    stride = ls.signL * (wp.pose["legStrideL"] + Wd(d => d.legStrideL));
                }
                else if (isCrouchBody) // Crouching / Reload Crouch
                {
                    stride = ls.signL * Cr(ap.crouching.legStrideL, cdeep.legStrideL);
                }
                else if (isSittingState) // Sit
                {
                    stride = ls.signL * ap.sitting.legStrideL;
                }
                else
                {
                    stride = ls.signL * Icr(d => d.legStrideL); // Idle hold deltas (0 elsewhere)
                }
                if (ownRot == null)
                    ownRot = MatMul3(RotationByAxis(ls.axisL, stride), RotationY(twist));
            }
            else if (gid == 5) // Right leg (hip) — stride + Y twist
            {
                var ls = ap.legStride;
                float twist = ap.legTwist.rightRest;
                float stride = 0f;
                if ((animState > 0.5f && animState < 1.5f) || (animState > 2.5f && animState < 3.5f)) // Walking or Aim Walk
                {
                    var wp = GetGaitPose();
                    stride = ls.signR * (wp.pose["legStrideR"] + Wd(d => d.legStrideR));
                }
                else if (isCrouchBody) // Crouching / Reload Crouch
                {
                    stride = ls.signR * Cr(ap.crouching.legStrideR, cdeep.legStrideR);
                }
                else if (isSittingState) // Sit
                {
                    stride = ls.signR * ap.sitting.legStrideR;
                }
                else
                {
                    stride = ls.signR * Icr(d => d.legStrideR); // Idle hold deltas (0 elsewhere)
                }
                if (ownRot == null)
                    ownRot = MatMul3(RotationByAxis(ls.axisR, stride), RotationY(twist));
            }
            else if (gid == 8) // Left forearm (elbow hinge + twist)
            {
                var eb = ap.elbowBend;
                float bend = eb.signL * (eb.leftRest + Icr(d => d.elbowBendL));
                float twist = eb.twistL;
                if (animState > 0.5f && animState < 1.5f) // Walking — keyframe pose
                {
                    var wp = GetGaitPose();
                    bend = eb.signL * (wp.pose["elbowBendL"] + Wd(d => d.elbowBendL));
                    twist = wp.pose["forearmTwistL"];
                }
                else if (isAimingState) // Aim Walk (3), Aiming (4), Aim Sweep (10)
                {
                    bend = eb.signL * ap.aiming.elbowBendL;
                }
                else if (isReloadState || isReloadCrouchState) // Reload (13/14) — hold + tap bend
                {
                    bool gunArm = reloadGunSide < 0f;
                    float gs = reloadGunSide >= 0f ? 1f : -1f;
                    bend = eb.signL * ((gunArm ? rl.elbowBendR : rl.elbowBendL)
                                     + tapPulse * (gunArm ? rl.tapBendR : rl.tapBendL));
                    twist = gs * (gunArm ? rl.forearmTwistR : rl.forearmTwistL);
                }
                else if (animState > 4.5f && animState < 5.5f) // Crouching — rest + static bend
                {
                    bend += eb.signL * Cr(ap.crouching.elbowBendL, cdeep.elbowBendL);
                }
                ownRot = RotationByAxis(eb.axisL, bend);
                if (twist != 0f) ownRot = MatMul3(ownRot, RotationX(twist));
            }
            else if (gid == 9) // Right forearm (elbow hinge + twist)
            {
                var eb = ap.elbowBend;
                float bend = eb.signR * (eb.rightRest + Icr(d => d.elbowBendR));
                float twist = eb.twistR;
                if (animState > 0.5f && animState < 1.5f) // Walking — keyframe pose
                {
                    var wp = GetGaitPose();
                    bend = eb.signR * (wp.pose["elbowBendR"] + Wd(d => d.elbowBendR));
                    twist = wp.pose["forearmTwistR"];
                }
                else if (isAimingState) // Aim Walk (3), Aiming (4), Aim Sweep (10)
                {
                    bend = eb.signR * ap.aiming.elbowBendR;
                }
                else if (isReloadState || isReloadCrouchState) // Reload (13/14) — hold + tap bend
                {
                    bool gunArm = reloadGunSide > 0f;
                    float gs = reloadGunSide >= 0f ? 1f : -1f;
                    bend = eb.signR * ((gunArm ? rl.elbowBendR : rl.elbowBendL)
                                     + tapPulse * (gunArm ? rl.tapBendR : rl.tapBendL));
                    twist = gs * (gunArm ? rl.forearmTwistR : rl.forearmTwistL);
                }
                else if (animState > 4.5f && animState < 5.5f) // Crouching — rest + static bend
                {
                    bend += eb.signR * Cr(ap.crouching.elbowBendR, cdeep.elbowBendR);
                }
                ownRot = RotationByAxis(eb.axisR, bend);
                if (twist != 0f) ownRot = MatMul3(ownRot, RotationX(twist));
            }
            else if (gid == 6) // Left shin (knee hinge)
            {
                var kb = ap.kneeBend;
                float bend = kb.signL * (kb.leftRest + Icr(d => d.kneeBendL));
                if ((animState > 0.5f && animState < 1.5f) || (animState > 2.5f && animState < 3.5f)) // Walking or Aim Walk
                {
                    var wp = GetGaitPose();
                    bend = kb.signL * (wp.pose["kneeBendL"] + Wd(d => d.kneeBendL));
                }
                else if (isCrouchBody) // Crouching / Reload Crouch — static knee bend
                {
                    bend += kb.signL * Cr(ap.crouching.kneeBendL, cdeep.kneeBendL);
                }
                else if (isSittingState) // Sit — static knee bend
                {
                    bend += kb.signL * ap.sitting.kneeBendL;
                }
                ownRot = RotationByAxis(kb.axisL, bend);
            }
            else if (gid == 7) // Right shin (knee hinge)
            {
                var kb = ap.kneeBend;
                float bend = kb.signR * (kb.rightRest + Icr(d => d.kneeBendR));
                if ((animState > 0.5f && animState < 1.5f) || (animState > 2.5f && animState < 3.5f)) // Walking or Aim Walk
                {
                    var wp = GetGaitPose();
                    bend = kb.signR * (wp.pose["kneeBendR"] + Wd(d => d.kneeBendR));
                }
                else if (isCrouchBody) // Crouching / Reload Crouch — static knee bend
                {
                    bend += kb.signR * Cr(ap.crouching.kneeBendR, cdeep.kneeBendR);
                }
                else if (isSittingState) // Sit — static knee bend
                {
                    bend += kb.signR * ap.sitting.kneeBendR;
                }
                ownRot = RotationByAxis(kb.axisR, bend);
            }

            // IK override channel — solver-supplied model-space rotation replaces
            // the param-derived ownRot for this group (arm aim, etc).
            if (ikOverrides.Count > 0 && ikOverrides.TryGetValue(gid, out var ikQ))
            {
                var baseQ = ownRot != null ? QuatFromMat3(ownRot) : Quaternion.identity;
                ownRot = Mat3FromQuat(Quaternion.Slerp(baseQ, ikQ, Mathf.Clamp01(ikBlend)));
            }

            if (ownRot == null) return parentResult;

            // Build transform chain: child's transform first, then parent's chain
            var ownTransform = new TransformEntry { pivot = ownPivot, rot = ownRot };
            TransformEntry[] chain;
            if (parentResult.HasValue && parentResult.Value.chain != null && parentResult.Value.chain.Length > 0)
            {
                chain = new TransformEntry[parentResult.Value.chain.Length + 1];
                chain[0] = ownTransform;
                for (int i = 0; i < parentResult.Value.chain.Length; i++)
                    chain[i + 1] = parentResult.Value.chain[i];
            }
            else
            {
                chain = new TransformEntry[] { ownTransform };
            }

            // Prepend body transform to upper body groups (outermost = applied last)
            if (hasBodyRot && (gid == 1 || gid == 2 || gid == 3))
            {
                var bodyRot = GetBodyTransform();
                if (bodyRot != null)
                {
                    var bodyPivot = new Vector3(pivots[0].x * dims.x * voxelSize, pivots[0].y * dims.y * voxelSize, pivots[0].z * dims.z * voxelSize);
                    var bodyTransform = new TransformEntry { pivot = bodyPivot, rot = bodyRot };
                    var newChain = new TransformEntry[chain.Length + 1];
                    Array.Copy(chain, newChain, chain.Length);
                    newChain[chain.Length] = bodyTransform;
                    chain = newChain;
                }
            }

            return new GroupTransformResult { chain = chain, offset = offset };
        }

        #endregion

        #region Voxel Posing (ported from rebuildAnimatedMesh)

        /// <summary>
        /// Transform rest-pose voxels to posed positions.
        /// Direct port of character_animator.html rebuildAnimatedMesh voxel loop.
        ///
        /// Input: rest voxel positions (x,y,z), groupIDs, dims, voxelSize, anim state
        /// Output: posed world-space positions (Vector3[])
        /// </summary>
        public Vector3[] PoseVoxels(
            Vector3Int[] restPositions, int[] groupIDs,
            Vector3Int dims, float voxelSize,
            float animState, float animTime, float animSpeed,
            float duck = 0f, float gaitTime = -1f)
        {
            int count = restPositions.Length;
            var posedPositions = new Vector3[count];
            float gaitClock = gaitTime >= 0f ? gaitTime : animTime * animSpeed;

            bool isWalkState = (animState > 0.5f && animState < 1.5f) || (animState > 2.5f && animState < 3.5f);
            WalkPose walkPose = isWalkState ? GetWalkPose(gaitClock, 1f, paramsData) : default(WalkPose);
            float bodyBobY = isWalkState ? walkPose.bodyBobY : 0f;
            float weightShiftX = isWalkState ? walkPose.weightShiftX : 0f;

            for (int i = 0; i < count; i++)
            {
                var v = restPositions[i];
                int gid = groupIDs[i];

                float px = v.x - dims.x * 0.5f;
                float py = v.y;
                float pz = v.z - dims.z * 0.5f;

                if (gid >= 0)
                {
                    var result = ComputeGroupRotation(gid, dims, voxelSize, animState, animTime, animSpeed, duck, gaitTime);
                    if (result.HasValue && result.Value.chain != null && result.Value.chain.Length > 0)
                    {
                        var pos = new Vector3(v.x, v.y, v.z);
                        for (int c = 0; c < result.Value.chain.Length; c++)
                        {
                            var entry = result.Value.chain[c];
                            var rel = pos - entry.pivot;
                            var transformed = MatVec3(entry.rot, rel);
                            pos = transformed + entry.pivot;
                        }
                        var off = result.Value.offset;
                        px = pos.x + off.x - dims.x * 0.5f;
                        py = pos.y + off.y;
                        pz = pos.z + off.z - dims.z * 0.5f;
                    }
                }

                // Apply body bob + weight shift for walk states
                if (isWalkState)
                {
                    px += weightShiftX;
                    py += bodyBobY;
                }

                posedPositions[i] = new Vector3(px, py, pz);
            }

            return posedPositions;
        }

        /// <summary>
        /// Pose a single FRACTIONAL rest-space point (e.g. an attachment centroid) through
        /// the same pipeline as PoseVoxels: group chain, offsets, body bob + weight shift.
        /// Returns the posed point in voxel units, centered on x/z (matching PoseVoxels output).
        /// </summary>
        /// <summary>PosePoint with pose-transition blending: while transBlend is
        /// in (0,1) the point is evaluated under BOTH the live state and the
        /// frozen outgoing pose (transStateA @ transTimeA) and lerped — keeps
        /// joint/anchor queries truthful mid-transition, mirroring the GPU
        /// dual-eval in CharacterPoseCompute. transDip is the signed settle
        /// offset (voxel units, applied on Y).</summary>
        public Vector3 PosePoint(
            Vector3 restPos, int gid, Vector3Int dims,
            float animState, float animTime, float animSpeed,
            float transStateA = -1f, float transTimeA = 0f,
            float transBlend = 1f, float transDip = 0f,
            float duck = 0f, float transDuckA = -1f, float gaitTime = -1f)
        {
            var gp = PreparePose(gid, dims, animState, animTime, animSpeed,
                transStateA, transTimeA, transBlend, transDip, duck, transDuckA, gaitTime);
            return ApplyPose(in gp, restPos, dims);
        }

        // ── Single-evaluation pose API ────────────────────────────────────
        // A group's FK chain depends only on (gid, pose params) — NOT on the point
        // being carried through it. PosePoint used to re-run ComputeGroupRotation
        // (≈20 small heap allocs: float[3,3] matrices + chain arrays) for EVERY point:
        // the exposure probe posed ~40 silhouette samples = ~40 chain evals, the eye
        // anchor and the weapon weld each ran the SAME chain twice (point, then
        // rotation). PreparePose evaluates it ONCE; ApplyPose/PoseRotation then reuse
        // it for any number of points / the orientation. PosePoint is now this pair,
        // so there is a single source of truth and results are bit-identical.

        /// <summary>One pose's per-group evaluation — the chain + offset + walk overlay.</summary>
        public struct PoseEval
        {
            public GroupTransformResult? result;
            public bool isWalk;
            public float bobY, shiftX;
            public bool hasGid;
        }

        /// <summary>A group under the live pose, plus the outgoing pose when a transition is blending.</summary>
        public struct GroupPose
        {
            public PoseEval b, a;
            public bool blend;
            public float blendT, dip;
        }

        PoseEval EvalPose(int gid, Vector3Int dims, float animState, float animTime, float animSpeed,
            float duck, float gaitTime)
        {
            var e = new PoseEval();
            float gaitClock = gaitTime >= 0f ? gaitTime : animTime * animSpeed;
            e.isWalk = (animState > 0.5f && animState < 1.5f) || (animState > 2.5f && animState < 3.5f);
            if (e.isWalk)
            {
                WalkPose walkPose = GetWalkPose(gaitClock, 1f, paramsData);
                e.bobY = walkPose.bodyBobY;
                e.shiftX = walkPose.weightShiftX;
            }
            e.hasGid = gid >= 0;
            if (e.hasGid)
                e.result = ComputeGroupRotation(gid, dims, 1.0f, animState, animTime, animSpeed, duck, gaitTime);
            return e;
        }

        /// <summary>Evaluate the group's pose once (dual-eval through a transition — same
        /// blend rule PosePoint always used: blending iff transBlend &lt; 1 and transStateA ≥ 0).</summary>
        public GroupPose PreparePose(int gid, Vector3Int dims,
            float animState, float animTime, float animSpeed,
            float transStateA = -1f, float transTimeA = 0f,
            float transBlend = 1f, float transDip = 0f,
            float duck = 0f, float transDuckA = -1f, float gaitTime = -1f)
        {
            var gp = new GroupPose { b = EvalPose(gid, dims, animState, animTime, animSpeed, duck, gaitTime) };
            if (transBlend >= 1f || transStateA < 0f) return gp;
            float duckA = transDuckA >= 0f ? transDuckA : duck;
            gp.blend = true; gp.blendT = transBlend; gp.dip = transDip;
            gp.a = EvalPose(gid, dims, transStateA, transTimeA, animSpeed, duckA, gaitTime);
            return gp;
        }

        static Vector3 ApplyEval(in PoseEval e, Vector3 restPos, Vector3Int dims)
        {
            float px = restPos.x - dims.x * 0.5f;
            float py = restPos.y;
            float pz = restPos.z - dims.z * 0.5f;

            if (e.hasGid && e.result.HasValue && e.result.Value.chain != null && e.result.Value.chain.Length > 0)
            {
                var chain = e.result.Value.chain;
                var pos = restPos;
                for (int c = 0; c < chain.Length; c++)
                {
                    var entry = chain[c];
                    var rel = pos - entry.pivot;
                    pos = MatVec3(entry.rot, rel) + entry.pivot;
                }
                var off = e.result.Value.offset;
                px = pos.x + off.x - dims.x * 0.5f;
                py = pos.y + off.y;
                pz = pos.z + off.z - dims.z * 0.5f;
            }

            if (e.isWalk)
            {
                px += e.shiftX;
                py += e.bobY;
            }
            return new Vector3(px, py, pz);
        }

        /// <summary>Carry one rest-space point through a prepared pose (voxel units, x/z centered).</summary>
        public static Vector3 ApplyPose(in GroupPose gp, Vector3 restPos, Vector3Int dims)
        {
            var pB = ApplyEval(in gp.b, restPos, dims);
            if (!gp.blend) return pB;
            var pA = ApplyEval(in gp.a, restPos, dims);
            var p = Vector3.Lerp(pA, pB, gp.blendT);
            p.y += gp.dip;
            return p;
        }

        static Quaternion ChainRotation(in PoseEval e)
        {
            Quaternion q = Quaternion.identity;
            if (e.result.HasValue && e.result.Value.chain != null)
                foreach (var entry in e.result.Value.chain)
                    q = QuatFromMat3(entry.rot) * q;
            return q;
        }

        /// <summary>Cumulative group orientation of a prepared pose — slerped through the
        /// transition window exactly as the weld / joint queries always did.</summary>
        public static Quaternion PoseRotation(in GroupPose gp)
        {
            Quaternion qB = ChainRotation(in gp.b);
            if (!gp.blend) return qB;
            return Quaternion.Slerp(ChainRotation(in gp.a), qB, gp.blendT);
        }

        /// <summary>
        /// Convenience overload: pose voxels and return both positions and the rest-space
        /// voxel indices for building a posed voxel buffer (for raymarch sampling).
        /// Each posed position maps back to a rest-space voxel index for material lookup.
        /// </summary>
        public void PoseVoxelsToBuffer(
            Vector3Int[] restPositions, int[] groupIDs,
            Vector3Int dims, float voxelSize,
            float animState, float animTime, float animSpeed,
            Vector3[] outPositions,
            float duck = 0f, float gaitTime = -1f)
        {
            int count = restPositions.Length;
            if (outPositions == null || outPositions.Length < count)
                outPositions = new Vector3[count];
            float gaitClock = gaitTime >= 0f ? gaitTime : animTime * animSpeed;

            bool isWalkState = (animState > 0.5f && animState < 1.5f) || (animState > 2.5f && animState < 3.5f);
            WalkPose walkPose = isWalkState ? GetWalkPose(gaitClock, 1f, paramsData) : default(WalkPose);
            float bodyBobY = isWalkState ? walkPose.bodyBobY : 0f;
            float weightShiftX = isWalkState ? walkPose.weightShiftX : 0f;

            // Invalidate walk pose cache for next frame
            _walkKeyTime = float.NaN;

            for (int i = 0; i < count; i++)
            {
                var v = restPositions[i];
                int gid = groupIDs[i];

                float px = v.x - dims.x * 0.5f;
                float py = v.y;
                float pz = v.z - dims.z * 0.5f;

                if (gid >= 0)
                {
                    var result = ComputeGroupRotation(gid, dims, voxelSize, animState, animTime, animSpeed, duck, gaitTime);
                    if (result.HasValue && result.Value.chain != null && result.Value.chain.Length > 0)
                    {
                        var pos = new Vector3(v.x, v.y, v.z);
                        for (int c = 0; c < result.Value.chain.Length; c++)
                        {
                            var entry = result.Value.chain[c];
                            var rel = pos - entry.pivot;
                            var transformed = MatVec3(entry.rot, rel);
                            pos = transformed + entry.pivot;
                        }
                        var off = result.Value.offset;
                        px = pos.x + off.x - dims.x * 0.5f;
                        py = pos.y + off.y;
                        pz = pos.z + off.z - dims.z * 0.5f;
                    }
                }

                if (isWalkState)
                {
                    px += weightShiftX;
                    py += bodyBobY;
                }

                outPositions[i] = new Vector3(px, py, pz);
            }
        }

        #endregion

        #region JSON Loading

        /// <summary>
        /// Load animation parameters from a .anim.json file.
        /// Parses ALL exported fields including pivots, jointOffset, looking, aiming, crouching.
        /// Uses JsonUtility for nested objects + manual regex parsing for int-keyed dicts.
        /// </summary>
        public static VoxelCharacterAnimator LoadFromAnimJson(string jsonText)
        {
            var jsonData = JsonUtility.FromJson<AnimParamsJson>(jsonText);
            if (jsonData == null || jsonData.@params == null)
                return null;

            var animator = new VoxelCharacterAnimator
            {
                paramsData = jsonData.@params
            };

            // Parse pivots manually (JsonUtility can't handle int-keyed dicts)
            animator.pivots = ParsePivotsManual(jsonText);

            // Parse jointOffset manually (same reason)
            animator.jointOffsets = ParseJointOffsetsManual(jsonText);

            // Fill defaults for missing optional sections
            var p = jsonData.@params;
            // JsonUtility can hand back NON-null, zero-filled objects for sections that
            // are absent from the JSON, so "== null" does not detect a missing section
            // (restPose 0/0 = arms never drop = permanent T-pose; zero signs = frozen
            // limbs). Presence is decided from the raw text; null is only a backstop.
            bool Missing(string key) => jsonText.IndexOf("\"" + key + "\"", System.StringComparison.Ordinal) < 0;
            if (Missing("restPose")) p.restPose = null;
            if (Missing("looking")) p.looking = null;
            if (Missing("aiming")) p.aiming = null;
            if (Missing("crouching")) p.crouching = null;
            if (Missing("sitting")) p.sitting = null;
            if (Missing("armSwing")) p.armSwing = null;
            if (Missing("legStride")) p.legStride = null;
            if (Missing("legTwist")) p.legTwist = null;
            if (Missing("elbowBend")) p.elbowBend = null;
            if (Missing("kneeBend")) p.kneeBend = null;
            if (Missing("walkKeyframes")) p.walkKeyframes = null;
            if (Missing("aimSweep")) p.aimSweep = null;
            if (Missing("reload")) p.reload = null;
            if (Missing("reloadCrouch")) p.reloadCrouch = null;
            if (Missing("walkDuckMid")) p.walkDuckMid = null;
            if (Missing("walkDuckPose")) p.walkDuckPose = null;
            if (p.aimSweep == null)
                p.aimSweep = new AimSweepData { amp = 0.55f, freq = 1.6f, headFollow = 0.6f };
            if (p.reload == null)
                p.reload = new ReloadData { armSwingL = -0.8f, armSwingR = -0.95f, shoulderReachL = 0.5f, shoulderReachR = -0.35f,
                    elbowBendL = 1.1f, elbowBendR = 0.9f, headPitch = 0.55f, headTilt = 0.08f, tapReachL = 0.2f, tapBendL = 0.25f };
            if (p.reloadCrouch == null)
                p.reloadCrouch = new ReloadData { armSwingL = p.reload.armSwingL, armSwingR = p.reload.armSwingR,
                    shoulderReachL = p.reload.shoulderReachL, shoulderReachR = p.reload.shoulderReachR,
                    elbowBendL = p.reload.elbowBendL, elbowBendR = p.reload.elbowBendR,
                    forearmTwistL = p.reload.forearmTwistL, forearmTwistR = p.reload.forearmTwistR,
                    headYaw = p.reload.headYaw, headPitch = p.reload.headPitch, headTilt = p.reload.headTilt,
                    torsoTwist = p.reload.torsoTwist,
                    tapReachL = p.reload.tapReachL, tapReachR = p.reload.tapReachR,
                    tapBendL = p.reload.tapBendL, tapBendR = p.reload.tapBendR };
            if (p.restPose == null)
                p.restPose = new RestPoseData { leftArmZ = -Mathf.PI / 2f, rightArmZ = Mathf.PI / 2f };
            if (p.looking == null)
                p.looking = new LookingData { headYaw = 0.5f, headYawFreq = 2f, headPitch = 0.035f, headPitchFreq = 1.3f };
            if (p.aiming == null)
                p.aiming = new AimingData { weaponType = "pistol", torsoTwist = -0.2f, headYaw = 0f, headPitch = -0.05f, headTilt = 0f, armSwingL = 0f, armSwingR = -1.4f, shoulderReachL = 0f, shoulderReachR = 0f, elbowBendL = 0f, elbowBendR = 0.3f };
            if (p.crouching == null)
                p.crouching = new CrouchingData { bodyLower = 0f, modelLower = 4f, bodyLean = 0f, headPitch = 0f, armSwingL = 0f, armSwingR = 0f, legStrideL = -1.15f, legStrideR = 0f, kneeBendL = 1.15f, kneeBendR = 1.40f, elbowBendL = 0f, elbowBendR = 0f };
            if (Missing("crouchDeep") || p.crouchDeep == null)
            {
                // No authored deep endpoint → clone the shallow crouch so the
                // depth channel exists but lerps a pose onto itself (inert).
                var s = p.crouching;
                p.crouchDeep = new CrouchingData { bodyLower = s.bodyLower, bodyForward = s.bodyForward, modelLower = s.modelLower, bodyLean = s.bodyLean, headPitch = s.headPitch, headTilt = s.headTilt, armSwingL = s.armSwingL, armSwingR = s.armSwingR, legStrideL = s.legStrideL, legStrideR = s.legStrideR, kneeBendL = s.kneeBendL, kneeBendR = s.kneeBendR, elbowBendL = s.elbowBendL, elbowBendR = s.elbowBendR };
            }
            // Walk-duck endpoints = CRUMPLE DELTAS under the gait, NOT poses —
            // never seed them from the authored crouch (absolute strides pin
            // legs against the cycle). Modest symmetric folds when absent.
            if (p.walkDuckMid == null)
                p.walkDuckMid = new CrouchingData { legStrideL = 0.15f, legStrideR = 0.15f, kneeBendL = 0.30f, kneeBendR = 0.30f, elbowBendL = 0.15f, elbowBendR = 0.15f, modelLower = 4f, bodyLean = 0.25f, headPitch = -0.12f };
            if (p.walkDuckPose == null)
                p.walkDuckPose = new CrouchingData { legStrideL = 0.30f, legStrideR = 0.30f, kneeBendL = 0.60f, kneeBendR = 0.60f, armSwingL = -0.10f, armSwingR = -0.10f, elbowBendL = 0.40f, elbowBendR = 0.40f, modelLower = 8f, bodyLean = 0.67f, headPitch = -0.26f };
            if (p.sitting == null)
                p.sitting = new SittingData { headPitch = 0f, armSwingL = 0.2f, armSwingR = 0.2f, legStrideL = 1.45f, legStrideR = 1.45f, kneeBendL = 1.45f, kneeBendR = 1.45f };
            // Sections below are dereferenced unconditionally in ComputeGroupRotation —
            // they MUST be filled or a paramless/trimmed file throws NullReferenceException.
            if (p.armSwing == null)
                p.armSwing = new ArmSwingData { axisL = 0, axisR = 0, signL = 1, signR = 1 };
            if (p.legStride == null)
                p.legStride = new LegStrideData { axisL = 0, axisR = 0, signL = -1, signR = -1 };
            if (p.legTwist == null)
                p.legTwist = new LegTwistData { leftRest = 0f, rightRest = 0f };
            if (p.elbowBend == null)
                p.elbowBend = new ElbowBendData { axisL = 1, axisR = 1, signL = 1, signR = -1, leftRest = 0f, rightRest = 0f, twistL = 0f, twistR = 0f, twistWalkAmp = 0.15f };
            if (p.kneeBend == null)
                p.kneeBend = new KneeBendData { axisL = 0, axisR = 0, signL = 1, signR = 1, leftRest = 0f, rightRest = 0f, walkAmp = 0.42f };
            if (p.walkKeyframes == null || (p.walkKeyframes.kf0 == null && p.walkKeyframes.kf1 == null))
            {
                // Section absent OR a partial JsonUtility-materialized object with
                // no usable keyframes — install the full default gait.
                p.walkKeyframes = new WalkKeyframesData
                {
                    autoMirror = true, cycleDuration = 1.2f, interpolation = "spline",
                    bodyBob = new BodyBobData { enabled = true, amplitude = 0.6f },
                    weightShift = new WeightShiftData { enabled = true, amplitude = 0.4f },
                    kf0 = new WalkKFPose { armSwingL = 0.3f, armSwingR = -0.3f, legStrideL = -0.4f, legStrideR = 0.4f, elbowBendL = 0.1f, elbowBendR = 0.1f, kneeBendL = 0f, kneeBendR = 0f, forearmTwistL = 0f, forearmTwistR = 0f },
                    kf1 = new WalkKFPose { armSwingL = 0f, armSwingR = 0f, legStrideL = 0.3f, legStrideR = -0.1f, elbowBendL = 0.3f, elbowBendR = 0.3f, kneeBendL = 0.8f, kneeBendR = 0.15f, forearmTwistL = 0f, forearmTwistR = 0f },
                    kf2 = null, kf3 = null,
                };
            }
            else
            {
                // Partial export (e.g. kf0 authored, kf1 trimmed) — fill the
                // missing core keyframes + cycle helpers so spline eval never
                // dereferences a null pose.
                var w = p.walkKeyframes;
                if (w.cycleDuration <= 0f) w.cycleDuration = 1.2f;
                if (string.IsNullOrEmpty(w.interpolation)) w.interpolation = "spline";
                if (w.bodyBob == null) w.bodyBob = new BodyBobData { enabled = true, amplitude = 0.6f };
                if (w.weightShift == null) w.weightShift = new WeightShiftData { enabled = true, amplitude = 0.4f };
                if (w.kf0 == null) w.kf0 = new WalkKFPose { armSwingL = 0.3f, armSwingR = -0.3f, legStrideL = -0.4f, legStrideR = 0.4f, elbowBendL = 0.1f, elbowBendR = 0.1f };
                if (w.kf1 == null) w.kf1 = new WalkKFPose { armSwingL = 0f, armSwingR = 0f, legStrideL = 0.3f, legStrideR = -0.1f, elbowBendL = 0.3f, elbowBendR = 0.3f, kneeBendL = 0.8f, kneeBendR = 0.15f };
            }

            // Debug: verify walk keyframes parsed correctly
            if (p.walkKeyframes != null)
            {
                var wkf = p.walkKeyframes;

                // JsonUtility instantiates a zero-filled WalkKFPose for explicit JSON "null"
                // values instead of leaving the C# field null. This breaks the autoMirror
                // fallback in GetWalkKfPose (which checks kf2/kf3 == null). Detect explicit
                // JSON null via raw text and force the field back to true null.
                if (IsWalkKfExplicitNull(jsonText, "kf2"))
                    wkf.kf2 = null;
                if (IsWalkKfExplicitNull(jsonText, "kf3"))
                    wkf.kf3 = null;

                Debug.Log($"[VCA] WalkKeyframes: interp={wkf.interpolation}, cycleDur={wkf.cycleDuration}, autoMirror={wkf.autoMirror}, " +
                          $"kf0={(wkf.kf0 != null ? $"aSL={wkf.kf0.armSwingL}" : "null")}, " +
                          $"kf1={(wkf.kf1 != null ? $"aSL={wkf.kf1.armSwingL}" : "null")}, " +
                          $"kf2={(wkf.kf2 != null ? "set" : "null (will auto-mirror kf0)")}, " +
                          $"kf3={(wkf.kf3 != null ? "set" : "null (will auto-mirror kf1)")}, " +
                          $"bodyBob={(wkf.bodyBob != null ? $"amp={wkf.bodyBob.amplitude}" : "null")}, " +
                          $"weightShift={(wkf.weightShift != null ? $"amp={wkf.weightShift.amplitude}" : "null")}");
            }
            else
            {
                Debug.LogWarning("[VCA] walkKeyframes is NULL after JsonUtility parse!");
            }

            return animator;
        }

        /// <summary>
        /// Checks the raw JSON text for an explicit `"key": null` literal for a given
        /// walk-keyframe field name (kf2/kf3). Needed because JsonUtility silently
        /// allocates a zero-filled object instead of leaving the field null.
        /// </summary>
        private static bool IsWalkKfExplicitNull(string jsonText, string key)
        {
            string pattern = $"\"{key}\"";
            int idx = jsonText.IndexOf(pattern);
            if (idx < 0) return false;
            int colon = jsonText.IndexOf(':', idx + pattern.Length);
            if (colon < 0) return false;
            int i = colon + 1;
            while (i < jsonText.Length && char.IsWhiteSpace(jsonText[i])) i++;
            return i + 4 <= jsonText.Length && jsonText.Substring(i, 4) == "null";
        }

        /// <summary>
        /// Parse pivots from raw JSON using regex.
        /// The .anim.json exports: "pivots": {"0": {"x": 0.5, "y": 0.4, "z": 0.5}, "1": {...}, ...}
        /// </summary>
        private static Dictionary<int, Vector3> ParsePivotsManual(string jsonText)
        {
            var result = new Dictionary<int, Vector3>();

            // Pivots are at the top level: "pivots": {...}, "params": ...
            // Extract the substring between "pivots": and "params":
            int pivotsStart = jsonText.IndexOf("\"pivots\"");
            int paramsStart = jsonText.IndexOf("\"params\"");
            if (pivotsStart < 0 || paramsStart < 0 || paramsStart <= pivotsStart)
            {
                // Fallback to defaults
                return GetDefaultPivots();
            }

            string pivotsSection = jsonText.Substring(pivotsStart, paramsStart - pivotsStart);

            // Match each "N": {"x": V, "y": V, "z": V} entry within the pivots section
            var entryPattern = new Regex(@"""(\d+)""\s*:\s*\{\s*""x""\s*:\s*([\-\d.eE+]+)\s*,\s*""y""\s*:\s*([\-\d.eE+]+)\s*,\s*""z""\s*:\s*([\-\d.eE+]+)\s*\}");
            foreach (Match m in entryPattern.Matches(pivotsSection))
            {
                int gid = int.Parse(m.Groups[1].Value);
                float x = float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                float y = float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                float z = float.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
                result[gid] = new Vector3(x, y, z);
            }

            if (result.Count == 0)
            {
                Debug.LogWarning("[VoxelCharacterAnimator] ParsePivotsManual found no entries — using defaults");
                return GetDefaultPivots();
            }
            return result;
        }

        private static Dictionary<int, Vector3> GetDefaultPivots()
        {
            return new Dictionary<int, Vector3>
            {
                { 0, new Vector3(0.5f, 0.4f, 0.5f) },
                { 1, new Vector3(0.5f, 0.78f, 0.5f) },
                { 2, new Vector3(0.25f, 0.75f, 0.5f) },
                { 3, new Vector3(0.75f, 0.75f, 0.5f) },
                { 4, new Vector3(0.375f, 0.34f, 0.5f) },
                { 5, new Vector3(0.625f, 0.34f, 0.5f) },
                { 8, new Vector3(0.25f, 0.75f, 0.5f) },
                { 9, new Vector3(0.75f, 0.75f, 0.5f) },
                { 6, new Vector3(0.375f, 0.20f, 0.5f) },
                { 7, new Vector3(0.625f, 0.20f, 0.5f) },
            };
        }

        /// <summary>
        /// Parse jointOffset from raw JSON using regex.
        /// The .anim.json exports: "jointOffset": {"1": {"x": 0, "y": 0, "z": 0}, "2": {...}, ...}
        /// </summary>
        private static Dictionary<int, Vector3> ParseJointOffsetsManual(string jsonText)
        {
            var result = new Dictionary<int, Vector3>();

            // jointOffset is inside params: "jointOffset": {"1": {"x": 0, "y": 0, "z": 0}, ...}
            // Find the section and extract entries by position
            int joStart = jsonText.IndexOf("\"jointOffset\"");
            if (joStart < 0) return result;

            // Find the next top-level params key after jointOffset to bound the search
            // The keys that follow jointOffset in the params object: walkKeyframes, armSwing, etc.
            int joEnd = jsonText.Length;
            string[] nextKeys = { "\"walkKeyframes\"", "\"armSwing\"", "\"legStride\"", "\"legTwist\"", "\"elbowBend\"", "\"kneeBend\"", "\"looking\"", "\"aiming\"", "\"crouching\"", "\"sitting\"" };
            foreach (var key in nextKeys)
            {
                int idx = jsonText.IndexOf(key, joStart);
                if (idx > 0 && idx < joEnd) joEnd = idx;
            }

            string joSection = jsonText.Substring(joStart, joEnd - joStart);

            var entryPattern = new Regex(@"""(\d+)""\s*:\s*\{\s*""x""\s*:\s*([\-\d.eE+]+)\s*,\s*""y""\s*:\s*([\-\d.eE+]+)\s*,\s*""z""\s*:\s*([\-\d.eE+]+)\s*\}");
            foreach (Match m in entryPattern.Matches(joSection))
            {
                int gid = int.Parse(m.Groups[1].Value);
                float x = float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                float y = float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                float z = float.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
                result[gid] = new Vector3(x, y, z);
            }

            return result;
        }

        #endregion

        #region JSON Data Classes (complete — matches all .anim.json exported fields)

        [System.Serializable]
        public class AnimParamsJson
        {
            public string format;
            public int version;
            public AnimParamsData @params;
            // pivots and states are parsed separately (JsonUtility limitation)
        }

        [System.Serializable]
        public class AnimParamsData
        {
            public RestPoseData restPose;
            // jointOffset parsed manually (int-keyed dict, not supported by JsonUtility)
            public WalkKeyframesData walkKeyframes;
            public ArmSwingData armSwing;
            public LegStrideData legStride;
            public LegTwistData legTwist;
            public ElbowBendData elbowBend;
            public KneeBendData kneeBend;
            public LookingData looking;
            public AimingData aiming;
            public CrouchingData crouching;
            /// <summary>Deep-crouch endpoint (same fields as crouching).
            /// crouchDepth 0..1 lerps every scalar crouching→crouchDeep —
            /// the adaptive-duck axis driven by picked cover height.</summary>
            public CrouchingData crouchDeep;
            /// <summary>Walk-duck D1 endpoint — crumple DELTAS under the gait,
            /// reached at duck=1. Reuses CrouchingData's field set
            /// (+headTilt/bodyForward; crouch sections leave them 0).</summary>
            public CrouchingData walkDuckMid;
            /// <summary>Walk-duck D2 endpoint — the deep crumple at duck=2.</summary>
            public CrouchingData walkDuckPose;
            public SittingData sitting;
            public AimSweepData aimSweep;
            public ReloadData reload;
            public ReloadData reloadCrouch;
        }

        [System.Serializable]
        public class ReloadData
        {
            public float armSwingL, armSwingR, shoulderReachL, shoulderReachR;
            public float elbowBendL, elbowBendR, forearmTwistL, forearmTwistR;
            public float headYaw, headPitch, headTilt, torsoTwist;
            public float tapReachL, tapReachR, tapBendL, tapBendR;
        }

        public class AimSweepData
        {
            public float amp;         // sweep amplitude (radians)
            public float freq;        // sweep speed (rad/s)
            public float headFollow;  // fraction of sweep the head tracks
        }

        [System.Serializable]
        public class RestPoseData
        {
            public float leftArmZ;
            public float rightArmZ;
        }

        [System.Serializable]
        public class WalkKeyframesData
        {
            public bool autoMirror;
            public float cycleDuration;
            public string interpolation;
            public WalkKFPose kf0;
            public WalkKFPose kf1;
            public WalkKFPose kf2;
            public WalkKFPose kf3;
            public BodyBobData bodyBob;
            public WeightShiftData weightShift;
        }

        [System.Serializable]
        public class WalkKFPose
        {
            public float armSwingL;
            public float armSwingR;
            public float legStrideL;
            public float legStrideR;
            public float elbowBendL;
            public float elbowBendR;
            public float kneeBendL;
            public float kneeBendR;
            public float forearmTwistL;
            public float forearmTwistR;
        }

        [System.Serializable]
        public class BodyBobData
        {
            public bool enabled;
            public float amplitude;
        }

        [System.Serializable]
        public class WeightShiftData
        {
            public bool enabled;
            public float amplitude;
        }

        [System.Serializable]
        public class ArmSwingData
        {
            public int axisL;
            public int axisR;
            public int signL;
            public int signR;
        }

        [System.Serializable]
        public class LegStrideData
        {
            public int axisL;
            public int axisR;
            public int signL;
            public int signR;
        }

        [System.Serializable]
        public class LegTwistData
        {
            public float leftRest;
            public float rightRest;
        }

        [System.Serializable]
        public class ElbowBendData
        {
            public int axisL;
            public int axisR;
            public int signL;
            public int signR;
            public float leftRest;
            public float rightRest;
            public float twistL;
            public float twistR;
            public float twistWalkAmp;
        }

        [System.Serializable]
        public class KneeBendData
        {
            public int axisL;
            public int axisR;
            public int signL;
            public int signR;
            public float leftRest;
            public float rightRest;
            public float walkAmp;
        }

        [System.Serializable]
        public class LookingData
        {
            public float headYaw;
            public float headYawFreq;
            public float headPitch;
            public float headPitchFreq;
        }

        [System.Serializable]
        public class AimingData
        {
            public string weaponType;
            public float torsoTwist;
            public float headYaw;
            public float headPitch;
            public float headTilt;
            public float armSwingL;
            public float armSwingR;
            public float shoulderReachL;
            public float shoulderReachR;
            public float elbowBendL;
            public float elbowBendR;
        }

        [System.Serializable]
        public class CrouchingData
        {
            public float bodyLower;
            public float bodyForward;   // Z torso offset — fwd/back twin of bodyLower
            public float modelLower;
            public float bodyLean;
            public float headPitch;
            public float headTilt;      // roll — walkDuck endpoints only (crouch sections leave 0)
            public float armSwingL;
            public float armSwingR;
            public float legStrideL;
            public float legStrideR;
            public float kneeBendL;
            public float kneeBendR;
            public float elbowBendL;
            public float elbowBendR;
        }

        [System.Serializable]
        public class SittingData
        {
            // State 11 — generic bench/vehicle seat pose. No bodyLower/modelLower:
            // the seat anchor transform positions the pelvis at runtime.
            public float headPitch;
            public float armSwingL;
            public float armSwingR;
            public float legStrideL;
            public float legStrideR;
            public float kneeBendL;
            public float kneeBendR;
        }

        #endregion
    }
}
