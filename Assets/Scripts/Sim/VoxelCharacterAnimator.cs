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
    ///   0=Idle, 1=Walking, 2=Looking, 3=AimWalk, 4=Aiming, 5=Crouching, 8=Down, 9=T-Pose
    /// </summary>
    public class VoxelCharacterAnimator
    {
        // ---- Animation parameters (loaded from .anim.json) ----
        public AnimParamsData paramsData;
        public Dictionary<int, Vector3> pivots = new Dictionary<int, Vector3>(); // gid -> normalized pivot (0.0-1.0)
        public Dictionary<int, Vector3> jointOffsets = new Dictionary<int, Vector3>(); // gid -> offset (voxel units)

        // ---- Cached pose state ----
        private WalkPose _walkPoseCache;
        private string _walkPoseCacheKey = "";

        // ---- Constants ----
        private static readonly Dictionary<int, int> PARENT_OF = new Dictionary<int, int>
        {
            { 8, 2 }, { 9, 3 }, { 6, 4 }, { 7, 5 }
        };
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

        private WalkKFPose GetWalkKfPose(int idx, WalkKeyframesData wkf)
        {
            if (idx == 0) return wkf.kf0;
            if (idx == 1) return wkf.kf1;
            if (idx == 2) return wkf.kf2 ?? (wkf.autoMirror ? MirrorWalkPose(wkf.kf0) : wkf.kf0);
            if (idx == 3) return wkf.kf3 ?? (wkf.autoMirror ? MirrorWalkPose(wkf.kf1) : wkf.kf1);
            return wkf.kf0;
        }

        private WalkPose GetWalkPose(float animTime, float animSpeed, AnimParamsData p)
        {
            string cacheKey = animTime.ToString("F4") + ":" + animSpeed.ToString("F2");
            if (_walkPoseCacheKey == cacheKey && _walkPoseCache.pose != null)
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
            _walkPoseCacheKey = cacheKey;
            return result;
        }

        private static float GetKFPoseValue(WalkKFPose p, string key)
        {
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
        /// </summary>
        public GroupTransformResult? ComputeGroupRotation(
            int gid, Vector3Int dims, float voxelSize,
            float animState, float animTime, float animSpeed)
        {
            // T-Pose: raw bind pose — NO rotations applied
            if (animState > 8.5f && animState < 9.5f) return null;

            var ap = paramsData;

            // === BODY TRANSFORM (torso twist + crouch lean + crouch lower) ===
            bool isAimingState = animState > 2.5f && animState < 4.5f; // Aim Walk (3) or Aiming (4)
            bool isCrouchingState = animState > 4.5f && animState < 5.5f; // Crouching (5)
            float torsoTwist = isAimingState ? ap.aiming.torsoTwist : 0f;
            float bodyLean = isCrouchingState ? ap.crouching.bodyLean : 0f;
            float bodyLower = isCrouchingState ? ap.crouching.bodyLower : 0f;
            float modelLower = isCrouchingState ? ap.crouching.modelLower : 0f;
            bool hasBodyRot = torsoTwist != 0f || bodyLean != 0f;

            float[,] GetBodyTransform()
            {
                if (!hasBodyRot) return null;
                if (!pivots.ContainsKey(0)) return null;
                // Compose: Y(twist) × X(lean) — twist applied after lean
                float[,] rot = IDENTITY;
                if (bodyLean != 0f) rot = MatMul3(RotationX(bodyLean), rot);
                if (torsoTwist != 0f) rot = MatMul3(RotationY(torsoTwist), rot);
                return rot;
            }

            Vector3 bodyOffset = new Vector3(0, -bodyLower - modelLower, 0);

            // Group 0 (body): apply body rotation + lower offset
            if (gid == 0)
            {
                var bodyRot = GetBodyTransform();
                if (bodyRot == null && bodyLower == 0f && modelLower == 0f) return null;
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
                parentResult = ComputeGroupRotation(PARENT_OF[gid], dims, voxelSize, animState, animTime, animSpeed);
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
                else if (animState > 2.5f && animState < 4.5f) // Aim Walk (3) or Aiming (4)
                {
                    var aim = ap.aiming;
                    headYaw = aim.headYaw; headPitch = aim.headPitch; headTilt = aim.headTilt;
                }
                else if (animState > 4.5f && animState < 5.5f) // Crouching
                {
                    headPitch = ap.crouching.headPitch;
                }
                else
                {
                    ownRot = IDENTITY; // Idle: head at rest
                }
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
                    var wp = GetWalkPose(animTime, animSpeed, ap);
                    swing = asc.signL * wp.pose["armSwingL"];
                }
                else if (animState > 2.5f && animState < 4.5f) // Aim Walk (3) or Aiming (4)
                {
                    swing = asc.signL * ap.aiming.armSwingL;
                    reach = ap.aiming.shoulderReachL;
                }
                else if (animState > 4.5f && animState < 5.5f) // Crouching
                {
                    swing = asc.signL * ap.crouching.armSwingL;
                }
                else
                {
                    ownRot = RotationZ(ap.restPose.leftArmZ); // Idle: rest pose only
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
                    var wp = GetWalkPose(animTime, animSpeed, ap);
                    swing = asc.signR * wp.pose["armSwingR"];
                }
                else if (animState > 2.5f && animState < 4.5f) // Aim Walk (3) or Aiming (4)
                {
                    swing = asc.signR * ap.aiming.armSwingR;
                    reach = ap.aiming.shoulderReachR;
                }
                else if (animState > 4.5f && animState < 5.5f) // Crouching
                {
                    swing = asc.signR * ap.crouching.armSwingR;
                }
                else
                {
                    ownRot = RotationZ(ap.restPose.rightArmZ); // Idle: rest pose only
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
                    var wp = GetWalkPose(animTime, animSpeed, ap);
                    stride = ls.signL * wp.pose["legStrideL"];
                }
                else if (animState > 4.5f && animState < 5.5f) // Crouching
                {
                    stride = ls.signL * ap.crouching.legStrideL;
                }
                else
                {
                    ownRot = RotationY(twist); // Idle: straight + twist
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
                    var wp = GetWalkPose(animTime, animSpeed, ap);
                    stride = ls.signR * wp.pose["legStrideR"];
                }
                else if (animState > 4.5f && animState < 5.5f) // Crouching
                {
                    stride = ls.signR * ap.crouching.legStrideR;
                }
                else
                {
                    ownRot = RotationY(twist);
                }
                if (ownRot == null)
                    ownRot = MatMul3(RotationByAxis(ls.axisR, stride), RotationY(twist));
            }
            else if (gid == 8) // Left forearm (elbow hinge + twist)
            {
                var eb = ap.elbowBend;
                float bend = eb.signL * eb.leftRest;
                float twist = eb.twistL;
                if (animState > 0.5f && animState < 1.5f) // Walking — keyframe pose
                {
                    var wp = GetWalkPose(animTime, animSpeed, ap);
                    bend = eb.signL * wp.pose["elbowBendL"];
                    twist = wp.pose["forearmTwistL"];
                }
                else if (animState > 2.5f && animState < 4.5f) // Aim Walk (3) or Aiming (4)
                {
                    bend = eb.signL * ap.aiming.elbowBendL;
                }
                ownRot = RotationByAxis(eb.axisL, bend);
                if (twist != 0f) ownRot = MatMul3(ownRot, RotationX(twist));
            }
            else if (gid == 9) // Right forearm (elbow hinge + twist)
            {
                var eb = ap.elbowBend;
                float bend = eb.signR * eb.rightRest;
                float twist = eb.twistR;
                if (animState > 0.5f && animState < 1.5f) // Walking — keyframe pose
                {
                    var wp = GetWalkPose(animTime, animSpeed, ap);
                    bend = eb.signR * wp.pose["elbowBendR"];
                    twist = wp.pose["forearmTwistR"];
                }
                else if (animState > 2.5f && animState < 4.5f) // Aim Walk (3) or Aiming (4)
                {
                    bend = eb.signR * ap.aiming.elbowBendR;
                }
                ownRot = RotationByAxis(eb.axisR, bend);
                if (twist != 0f) ownRot = MatMul3(ownRot, RotationX(twist));
            }
            else if (gid == 6) // Left shin (knee hinge)
            {
                var kb = ap.kneeBend;
                float bend = kb.signL * kb.leftRest;
                if ((animState > 0.5f && animState < 1.5f) || (animState > 2.5f && animState < 3.5f)) // Walking or Aim Walk
                {
                    var wp = GetWalkPose(animTime, animSpeed, ap);
                    bend = kb.signL * wp.pose["kneeBendL"];
                }
                else if (animState > 4.5f && animState < 5.5f) // Crouching — static knee bend
                {
                    bend += kb.signL * ap.crouching.kneeBendL;
                }
                ownRot = RotationByAxis(kb.axisL, bend);
            }
            else if (gid == 7) // Right shin (knee hinge)
            {
                var kb = ap.kneeBend;
                float bend = kb.signR * kb.rightRest;
                if ((animState > 0.5f && animState < 1.5f) || (animState > 2.5f && animState < 3.5f)) // Walking or Aim Walk
                {
                    var wp = GetWalkPose(animTime, animSpeed, ap);
                    bend = kb.signR * wp.pose["kneeBendR"];
                }
                else if (animState > 4.5f && animState < 5.5f) // Crouching — static knee bend
                {
                    bend += kb.signR * ap.crouching.kneeBendR;
                }
                ownRot = RotationByAxis(kb.axisR, bend);
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
            float animState, float animTime, float animSpeed)
        {
            int count = restPositions.Length;
            var posedPositions = new Vector3[count];

            bool isWalkState = (animState > 0.5f && animState < 1.5f) || (animState > 2.5f && animState < 3.5f);
            WalkPose walkPose = isWalkState ? GetWalkPose(animTime, animSpeed, paramsData) : default(WalkPose);
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
                    var result = ComputeGroupRotation(gid, dims, voxelSize, animState, animTime, animSpeed);
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
        /// Convenience overload: pose voxels and return both positions and the rest-space
        /// voxel indices for building a posed voxel buffer (for raymarch sampling).
        /// Each posed position maps back to a rest-space voxel index for material lookup.
        /// </summary>
        public void PoseVoxelsToBuffer(
            Vector3Int[] restPositions, int[] groupIDs,
            Vector3Int dims, float voxelSize,
            float animState, float animTime, float animSpeed,
            Vector3[] outPositions)
        {
            int count = restPositions.Length;
            if (outPositions == null || outPositions.Length < count)
                outPositions = new Vector3[count];

            bool isWalkState = (animState > 0.5f && animState < 1.5f) || (animState > 2.5f && animState < 3.5f);
            WalkPose walkPose = isWalkState ? GetWalkPose(animTime, animSpeed, paramsData) : default(WalkPose);
            float bodyBobY = isWalkState ? walkPose.bodyBobY : 0f;
            float weightShiftX = isWalkState ? walkPose.weightShiftX : 0f;

            // Invalidate walk pose cache for next frame
            _walkPoseCacheKey = "";

            for (int i = 0; i < count; i++)
            {
                var v = restPositions[i];
                int gid = groupIDs[i];

                float px = v.x - dims.x * 0.5f;
                float py = v.y;
                float pz = v.z - dims.z * 0.5f;

                if (gid >= 0)
                {
                    var result = ComputeGroupRotation(gid, dims, voxelSize, animState, animTime, animSpeed);
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
            if (p.restPose == null)
                p.restPose = new RestPoseData { leftArmZ = -Mathf.PI / 2f, rightArmZ = Mathf.PI / 2f };
            if (p.looking == null)
                p.looking = new LookingData { headYaw = 0.5f, headYawFreq = 2f, headPitch = 0.035f, headPitchFreq = 1.3f };
            if (p.aiming == null)
                p.aiming = new AimingData { weaponType = "pistol", torsoTwist = 0.2f, headYaw = 0f, headPitch = -0.05f, headTilt = 0f, armSwingL = -1.4f, armSwingR = 0f, shoulderReachL = 0f, shoulderReachR = 0f, elbowBendL = 0.3f, elbowBendR = 0f };
            if (p.crouching == null)
                p.crouching = new CrouchingData { bodyLower = 0f, modelLower = 4f, bodyLean = 0f, headPitch = 0f, armSwingL = 0f, armSwingR = 0f, legStrideL = -1.15f, legStrideR = 0f, kneeBendL = 1.15f, kneeBendR = 1.40f };

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
            string[] nextKeys = { "\"walkKeyframes\"", "\"armSwing\"", "\"legStride\"", "\"legTwist\"", "\"elbowBend\"", "\"kneeBend\"", "\"looking\"", "\"aiming\"", "\"crouching\"" };
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
            public float modelLower;
            public float bodyLean;
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
