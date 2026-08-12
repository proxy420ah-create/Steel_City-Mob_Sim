#!/usr/bin/env python3
"""
Replaces the animation section of VoxelProxyRaymarch.shader with a fresh
implementation that exactly matches the CPU VoxelCharacterAnimator logic.

Changes:
1. Adds _AnimStaticParams buffer declaration (12 float4s)
2. Replaces GetGroupPivot, ComputeGroupRotation, GroupTransformOffset,
   ParentOfGroup, InverseGroupTransformOffset with new implementations
"""

import re

SHADER_PATH = r"c:\Users\NADECC\ATSTradingDashboard Project\Cursor Workshop\SteelCityMobSim\Assets\Resources\Shaders\VoxelProxyRaymarch.shader"

with open(SHADER_PATH, 'r', encoding='utf-8') as f:
    content = f.read()

# ---- 1. Insert _AnimStaticParams buffer declaration after _PivotsEnabled ----
BUFFER_DECL = """
            // --- Static animation parameters (looking/aiming/crouching/jointOffset) ---
            // Packed as 12 float4s, uploaded once per character type:
            // [0]  = (looking.headYaw, looking.headYawFreq, looking.headPitch, looking.headPitchFreq)
            // [1]  = (aiming.torsoTwist, aiming.headYaw, aiming.headPitch, aiming.headTilt)
            // [2]  = (aiming.armSwingL, aiming.armSwingR, aiming.shoulderReachL, aiming.shoulderReachR)
            // [3]  = (aiming.elbowBendL, aiming.elbowBendR, crouching.bodyLower, crouching.modelLower)
            // [4]  = (crouching.bodyLean, crouching.headPitch, crouching.armSwingL, crouching.armSwingR)
            // [5]  = (crouching.legStrideL, crouching.legStrideR, crouching.kneeBendL, crouching.kneeBendR)
            // [6]  = (elbowTwistL, elbowTwistR, 0, 0)
            // [7]  = (jointOffset_1.x, jointOffset_1.y, jointOffset_1.z, 0)  -- head
            // [8]  = (jointOffset_2.x, jointOffset_2.y, jointOffset_2.z, 0)  -- left arm
            // [9]  = (jointOffset_3.x, jointOffset_3.y, jointOffset_3.z, 0)  -- right arm
            // [10] = (jointOffset_4.x, jointOffset_4.y, jointOffset_4.z, 0)  -- left leg
            // [11] = (jointOffset_5.x, jointOffset_5.y, jointOffset_5.z, 0)  -- right leg
            StructuredBuffer<float4> _AnimStaticParams;
            int _AnimStaticParamsEnabled;"""

old_pivots = "            int _PivotsEnabled;\n"
assert old_pivots in content, "Could not find _PivotsEnabled declaration"
content = content.replace(old_pivots, old_pivots + BUFFER_DECL + "\n", 1)

# ---- 2. Replace the entire animation transform section ----
# From "// Compute per-group rotation" through the end of InverseGroupTransformOffset

NEW_ANIM_SECTION = r"""            // ---- Animation structs ----
            struct TransformEntry {
                float3 pivot;
                float3x3 rot;
            };

            struct GroupTransformResult {
                TransformEntry chain[3];  // max: own + parent + body
                int chainLength;
                float3 offset;
            };

            // ---- Pivot helper (voxel units, caller multiplies by voxelSize) ----
            float3 GetGroupPivotRaw(uint groupID, float3 dims)
            {
                if (_PivotsEnabled)
                    return _Pivots[groupID].xyz * dims;
                // Fallbacks matching CPU GetDefaultPivots
                if (groupID == 0u) return float3(0.5, 0.4, 0.5) * dims;
                if (groupID == 1u) return float3(0.5, 0.78, 0.5) * dims;
                if (groupID == 2u) return float3(0.25, 0.75, 0.5) * dims;
                if (groupID == 3u) return float3(0.75, 0.75, 0.5) * dims;
                if (groupID == 4u) return float3(0.375, 0.34, 0.5) * dims;
                if (groupID == 5u) return float3(0.625, 0.34, 0.5) * dims;
                if (groupID == 6u) return float3(0.375, 0.20, 0.5) * dims;
                if (groupID == 7u) return float3(0.625, 0.20, 0.5) * dims;
                if (groupID == 8u) return float3(0.25, 0.75, 0.5) * dims;
                if (groupID == 9u) return float3(0.75, 0.75, 0.5) * dims;
                return float3(0, 0, 0);
            }

            // ---- Static anim param helpers ----
            float4 ASP(int idx) { return _AnimStaticParamsEnabled ? _AnimStaticParams[idx] : float4(0,0,0,0); }

            // ---- Compute single group's own rotation (no FK chain) ----
            // Direct port of CPU VoxelCharacterAnimator.ComputeGroupRotation per-group logic.
            // Returns false if no rotation applies for this group in this state.
            bool ComputeOwnRotation(
                uint gid, float3 dims, float voxelSize,
                float animState, float animTime, float animSpeed,
                out float3x3 rot)
            {
                rot = float3x3(1,0,0, 0,1,0, 0,0,1);
                float PI = 3.14159265;

                bool isWalking = (animState > 0.5 && animState < 1.5) || (animState > 2.5 && animState < 3.5);
                bool isAimingState = animState > 2.5 && animState < 4.5; // Aim Walk (3) or Aiming (4)
                bool isCrouchingState = animState > 4.5 && animState < 5.5;

                float walkPhase = 0.0;
                if (isWalking && _WalkKeyframesEnabled != 0)
                    walkPhase = GetWalkCyclePhase(animTime, animSpeed);
                bool useKeyframes = isWalking && _WalkKeyframesEnabled != 0;

                // Joint config shortcuts
                float4 armCfg   = _JointConfigEnabled ? _JointConfig[0] : float4(0, 0, 1, 1);
                float4 legCfg   = _JointConfigEnabled ? _JointConfig[1] : float4(0, 0, 1, 1);
                float4 elbowCfg = _JointConfigEnabled ? _JointConfig[2] : float4(1, 1, 1, -1);
                float4 kneeCfg  = _JointConfigEnabled ? _JointConfig[3] : float4(0, 0, 1, 1);
                float4 twistCfg = _JointConfigEnabled ? _JointConfig[4] : float4(0, 0, 0, 0);
                float4 restCfg  = _JointConfigEnabled ? _JointConfig[5] : float4(-1.5708, 1.5708, 0, 0);
                float4 kneeRest = _JointConfigEnabled ? _JointConfig[6] : float4(0, 0, 0, 0);

                // Static anim params (with fallbacks matching CPU defaults)
                float4 lookP   = ASP(0); if (!_AnimStaticParamsEnabled) lookP = float4(0.5, 2.0, 0.035, 1.3);
                float4 aimP1   = ASP(1); if (!_AnimStaticParamsEnabled) aimP1 = float4(0.2, 0, -0.05, 0);
                float4 aimP2   = ASP(2); if (!_AnimStaticParamsEnabled) aimP2 = float4(-1.4, 0, 0, 0);
                float4 aimP3   = ASP(3); if (!_AnimStaticParamsEnabled) aimP3 = float4(0.3, 0, 0, 4);
                float4 crouchP1= ASP(4); if (!_AnimStaticParamsEnabled) crouchP1 = float4(0, 0, 0, 0);
                float4 crouchP2= ASP(5); if (!_AnimStaticParamsEnabled) crouchP2 = float4(-1.15, 0, 1.15, 1.40);
                float2 elbowTw = ASP(6).xy; if (!_AnimStaticParamsEnabled) elbowTw = float2(0, 0);

                if (gid == 1u) // Head
                {
                    float headYaw = 0.0, headPitch = 0.0, headTilt = 0.0;
                    if (animState > 1.5 && animState < 2.5) { // Looking (state 2 only)
                        headYaw = sin(animTime * lookP.y) * lookP.x;
                        headPitch = sin(animTime * lookP.w) * lookP.z;
                    } else if (isAimingState) { // Aim Walk (3) or Aiming (4)
                        headYaw = aimP1.y;
                        headPitch = aimP1.z;
                        headTilt = aimP1.w;
                    } else if (isCrouchingState) { // Crouching (5)
                        headPitch = crouchP1.y;
                    } else {
                        return false; // Idle: head at rest
                    }
                    rot = mul(RotationY(headYaw), mul(RotationX(headPitch), RotationZ(headTilt)));
                    return true;
                }
                else if (gid == 2u) // Left arm (shoulder)
                {
                    float swing = 0.0;
                    float reach = 0.0;
                    if (useKeyframes) { // Walking or Aim Walk
                        swing = armCfg.z * GetWalkPoseValue(0, walkPhase, _WalkConfig.w > 0.5);
                    } else if (isAimingState) { // Aiming (4, not walking)
                        swing = armCfg.z * aimP2.x;       // signL * aiming.armSwingL
                        reach = aimP2.z;                   // aiming.shoulderReachL
                    } else if (isCrouchingState) {
                        swing = armCfg.z * crouchP1.z;     // signL * crouching.armSwingL
                    } else {
                        rot = RotationZ(restCfg.x);        // Idle: restPose.leftArmZ
                        return true;
                    }
                    // Compose: Y(reach) * axis(swing) * Z(restPose)
                    rot = mul(RotationY(reach), mul(RotationByAxis((int)armCfg.x, swing), RotationZ(restCfg.x)));
                    return true;
                }
                else if (gid == 3u) // Right arm (shoulder)
                {
                    float swing = 0.0;
                    float reach = 0.0;
                    if (useKeyframes) {
                        swing = armCfg.w * GetWalkPoseValue(1, walkPhase, _WalkConfig.w > 0.5);
                    } else if (isAimingState) {
                        swing = armCfg.w * aimP2.y;        // signR * aiming.armSwingR
                        reach = aimP2.w;                    // aiming.shoulderReachR
                    } else if (isCrouchingState) {
                        swing = armCfg.w * crouchP1.w;     // signR * crouching.armSwingR
                    } else {
                        rot = RotationZ(restCfg.y);        // Idle: restPose.rightArmZ
                        return true;
                    }
                    rot = mul(RotationY(reach), mul(RotationByAxis((int)armCfg.y, swing), RotationZ(restCfg.y)));
                    return true;
                }
                else if (gid == 4u) // Left leg (hip)
                {
                    float twist = twistCfg.x; // legTwist.leftRest
                    float stride = 0.0;
                    if (useKeyframes) { // Walking or Aim Walk
                        stride = legCfg.z * GetWalkPoseValue(2, walkPhase, _WalkConfig.w > 0.5);
                    } else if (isCrouchingState) {
                        stride = legCfg.z * crouchP2.x;    // signL * crouching.legStrideL
                    } else {
                        rot = RotationY(twist);            // Idle: straight + twist
                        return true;
                    }
                    // Compose: axis(stride) * Y(twist)
                    rot = mul(RotationByAxis((int)legCfg.x, stride), RotationY(twist));
                    return true;
                }
                else if (gid == 5u) // Right leg (hip)
                {
                    float twist = twistCfg.y; // legTwist.rightRest
                    float stride = 0.0;
                    if (useKeyframes) {
                        stride = legCfg.w * GetWalkPoseValue(3, walkPhase, _WalkConfig.w > 0.5);
                    } else if (isCrouchingState) {
                        stride = legCfg.w * crouchP2.y;    // signR * crouching.legStrideR
                    } else {
                        rot = RotationY(twist);
                        return true;
                    }
                    rot = mul(RotationByAxis((int)legCfg.y, stride), RotationY(twist));
                    return true;
                }
                else if (gid == 8u) // Left forearm (elbow hinge + twist)
                {
                    float bend = elbowCfg.z * restCfg.z;   // signL * leftRest
                    float twist = elbowTw.x;                // eb.twistL
                    if (useKeyframes) {
                        bend = elbowCfg.z * GetWalkPoseValue(4, walkPhase, _WalkConfig.w > 0.5);
                        twist = GetWalkPoseValue(8, walkPhase, _WalkConfig.w > 0.5);
                    } else if (isAimingState) {
                        bend = elbowCfg.z * aimP3.x;        // signL * aiming.elbowBendL
                    }
                    rot = RotationByAxis((int)elbowCfg.x, bend);
                    if (twist != 0.0) rot = mul(rot, RotationX(twist));
                    return true;
                }
                else if (gid == 9u) // Right forearm (elbow hinge + twist)
                {
                    float bend = elbowCfg.w * restCfg.w;   // signR * rightRest
                    float twist = elbowTw.y;                // eb.twistR
                    if (useKeyframes) {
                        bend = elbowCfg.w * GetWalkPoseValue(5, walkPhase, _WalkConfig.w > 0.5);
                        twist = GetWalkPoseValue(9, walkPhase, _WalkConfig.w > 0.5);
                    } else if (isAimingState) {
                        bend = elbowCfg.w * aimP3.y;        // signR * aiming.elbowBendR
                    }
                    rot = RotationByAxis((int)elbowCfg.y, bend);
                    if (twist != 0.0) rot = mul(rot, RotationX(twist));
                    return true;
                }
                else if (gid == 6u) // Left shin (knee hinge)
                {
                    float bend = kneeCfg.z * kneeRest.x;   // signL * leftRest
                    if (useKeyframes) {
                        bend = kneeCfg.z * GetWalkPoseValue(6, walkPhase, _WalkConfig.w > 0.5);
                    } else if (isCrouchingState) {
                        bend += kneeCfg.z * crouchP2.z;    // signL * crouching.kneeBendL
                    }
                    rot = RotationByAxis((int)kneeCfg.x, bend);
                    return true;
                }
                else if (gid == 7u) // Right shin (knee hinge)
                {
                    float bend = kneeCfg.w * kneeRest.y;   // signR * rightRest
                    if (useKeyframes) {
                        bend = kneeCfg.w * GetWalkPoseValue(7, walkPhase, _WalkConfig.w > 0.5);
                    } else if (isCrouchingState) {
                        bend += kneeCfg.w * crouchP2.w;    // signR * crouching.kneeBendR
                    }
                    rot = RotationByAxis((int)kneeCfg.y, bend);
                    return true;
                }

                return false;
            }

            // ---- Compute group offset (matches CPU offset logic) ----
            float3 ComputeGroupOffset(uint gid, bool hasParent, uint parentGid,
                float bodyLower, float modelLower)
            {
                float3 offset = float3(0, 0, 0);
                if (hasParent) {
                    // Children inherit parent's offset — compute parent's offset recursively
                    // Parent is always a root group (2,3,4,5), so no grandparent
                    offset = ComputeGroupOffset(parentGid, false, 0u, bodyLower, modelLower);
                } else if (gid >= 1u && gid <= 5u) {
                    // Root parent groups: use jointOffset
                    int joIdx = 6 + (int)gid; // _AnimStaticParams[7..11] for gid 1..5
                    if (_AnimStaticParamsEnabled)
                        offset = _AnimStaticParams[joIdx].xyz;
                    // Add bodyLower to upper body parent groups (head, arms) — not legs
                    if (bodyLower != 0.0 && (gid == 1u || gid == 2u || gid == 3u))
                        offset.y -= bodyLower;
                    // Add modelLower to ALL root parent groups (1-5)
                    if (modelLower != 0.0 && (gid >= 1u && gid <= 5u))
                        offset.y -= modelLower;
                }
                return offset;
            }

            // ---- Compute full transform chain for a group (matches CPU ComputeGroupRotation) ----
            bool ComputeGroupRotation(
                uint groupID, float3 dims, float voxelSize,
                float animState, float animTime, float animSpeed,
                out GroupTransformResult result)
            {
                result.chainLength = 0;
                result.offset = float3(0, 0, 0);

                // T-Pose: no transforms
                if (animState > 8.5 && animState < 9.5) return false;

                // Body transform params
                bool isAimingState = animState > 2.5 && animState < 4.5; // Aim Walk (3) or Aiming (4)
                bool isCrouchingState = animState > 4.5 && animState < 5.5; // Crouching (5)
                float torsoTwist = isAimingState ? (_AnimStaticParamsEnabled ? _AnimStaticParams[1].x : 0.2) : 0.0;
                float bodyLean = isCrouchingState ? (_AnimStaticParamsEnabled ? _AnimStaticParams[4].x : 0.0) : 0.0;
                float bodyLower = isCrouchingState ? (_AnimStaticParamsEnabled ? _AnimStaticParams[3].z : 0.0) : 0.0;
                float modelLower = isCrouchingState ? (_AnimStaticParamsEnabled ? _AnimStaticParams[3].w : 4.0) : 0.0;
                bool hasBodyRot = (torsoTwist != 0.0) || (bodyLean != 0.0);

                float3x3 bodyRot = float3x3(1,0,0, 0,1,0, 0,0,1);
                if (hasBodyRot) {
                    if (bodyLean != 0.0) bodyRot = mul(RotationX(bodyLean), bodyRot);
                    if (torsoTwist != 0.0) bodyRot = mul(RotationY(torsoTwist), bodyRot);
                }
                float3 bodyPivot = GetGroupPivotRaw(0u, dims) * voxelSize;
                float3 bodyOffset = float3(0, -bodyLower - modelLower, 0);

                // Group 0 (body)
                if (groupID == 0u) {
                    if (hasBodyRot) {
                        result.chain[0].pivot = bodyPivot;
                        result.chain[0].rot = bodyRot;
                        result.chainLength = 1;
                    }
                    result.offset = bodyOffset;
                    return hasBodyRot || (bodyLower != 0.0) || (modelLower != 0.0);
                }

                // Parent mapping
                uint parentGid = 0u;
                bool hasParent = false;
                if (groupID == 8u) { parentGid = 2u; hasParent = true; }
                else if (groupID == 9u) { parentGid = 3u; hasParent = true; }
                else if (groupID == 6u) { parentGid = 4u; hasParent = true; }
                else if (groupID == 7u) { parentGid = 5u; hasParent = true; }

                // Compute own rotation
                float3x3 ownRot;
                bool hasOwnRot = ComputeOwnRotation(groupID, dims, voxelSize, animState, animTime, animSpeed, ownRot);

                // Compute parent rotation (if child)
                float3x3 parentRot;
                bool hasParentRot = false;
                if (hasParent) {
                    hasParentRot = ComputeOwnRotation(parentGid, dims, voxelSize, animState, animTime, animSpeed, parentRot);
                }

                // Check if body transform applies to this group or its parent
                bool bodyApplies = hasBodyRot && (
                    (!hasParent && (groupID == 1u || groupID == 2u || groupID == 3u)) ||
                    (hasParent && (parentGid == 2u || parentGid == 3u))
                );

                if (!hasOwnRot && !hasParentRot && !bodyApplies) {
                    // Still might have offset
                    result.offset = ComputeGroupOffset(groupID, hasParent, parentGid, bodyLower, modelLower);
                    return result.offset.x != 0.0 || result.offset.y != 0.0 || result.offset.z != 0.0;
                }

                // Build chain: [own, parent_own, body]
                // Chain order matches CPU: own first, then parent's chain, then body (outermost)
                int idx = 0;
                if (hasOwnRot) {
                    result.chain[idx].pivot = GetGroupPivotRaw(groupID, dims) * voxelSize;
                    result.chain[idx].rot = ownRot;
                    idx++;
                }
                if (hasParentRot) {
                    result.chain[idx].pivot = GetGroupPivotRaw(parentGid, dims) * voxelSize;
                    result.chain[idx].rot = parentRot;
                    idx++;
                }
                if (bodyApplies) {
                    result.chain[idx].pivot = bodyPivot;
                    result.chain[idx].rot = bodyRot;
                    idx++;
                }
                result.chainLength = idx;

                // Compute offset
                result.offset = ComputeGroupOffset(groupID, hasParent, parentGid, bodyLower, modelLower);

                return true;
            }

            // ---- Inverse transform: posedPos -> restPos offset ----
            // Used in the DDA loop to sample voxel data at rest positions.
            // Applies inverse of the full transform chain (body, parent, own) in reverse order,
            // then subtracts the offset. Matches CPU PoseVoxels inverse.
            float3 InverseGroupTransformOffset(
                uint groupID, float3 voxelLocalPos, float3 dims, float voxelSize,
                float animState, float animTime, float animSpeed)
            {
                if (groupID == 0u) {
                    // Body group: inverse of body transform + offset
                    GroupTransformResult r;
                    if (!ComputeGroupRotation(0u, dims, voxelSize, animState, animTime, animSpeed, r))
                        return float3(0, 0, 0);
                    float3 restPos = voxelLocalPos - r.offset;
                    for (int i = r.chainLength - 1; i >= 0; i--) {
                        float3 rel = restPos - r.chain[i].pivot;
                        restPos = mul(transpose(r.chain[i].rot), rel) + r.chain[i].pivot;
                    }
                    return restPos - voxelLocalPos;
                }

                GroupTransformResult result;
                if (!ComputeGroupRotation(groupID, dims, voxelSize, animState, animTime, animSpeed, result))
                    return float3(0, 0, 0);

                // Inverse: subtract offset first, then apply inverse chain in reverse order
                float3 restPos = voxelLocalPos - result.offset;
                for (int i = result.chainLength - 1; i >= 0; i--) {
                    float3 rel = restPos - result.chain[i].pivot;
                    restPos = mul(transpose(result.chain[i].rot), rel) + result.chain[i].pivot;
                }
                return restPos - voxelLocalPos;
            }

            // Forward transform: restPos -> posedPos offset (kept for reference/debugging)
            float3 GroupTransformOffset(
                uint groupID, float3 voxelLocalPos, float3 dims, float voxelSize,
                float animState, float animTime, float animSpeed)
            {
                if (groupID == 0u) return float3(0, 0, 0);
                GroupTransformResult result;
                if (!ComputeGroupRotation(groupID, dims, voxelSize, animState, animTime, animSpeed, result))
                    return float3(0, 0, 0);
                float3 pos = voxelLocalPos;
                for (int i = 0; i < result.chainLength; i++) {
                    float3 rel = pos - result.chain[i].pivot;
                    pos = mul(result.chain[i].rot, rel) + result.chain[i].pivot;
                }
                pos += result.offset;
                return pos - voxelLocalPos;
            }

            // PARENT_OF map for FK chains: child -> parent
            uint ParentOfGroup(uint gid)
            {
                if (gid == 8u) return 2u;
                if (gid == 9u) return 3u;
                if (gid == 6u) return 4u;
                if (gid == 7u) return 5u;
                return 0u; // no parent (body or unknown)
            }
"""

# Find the section to replace: from "// Compute per-group rotation" to end of InverseGroupTransformOffset
# The section starts with the comment before GetGroupPivot and ends before "// ---- Lighting"
old_section_start = "            // Compute per-group rotation for a given animation state."
old_section_end = "            // ---- Lighting (ported from compute shader) ----"

start_idx = content.index(old_section_start)
end_idx = content.index(old_section_end)

# Replace the section
content = content[:start_idx] + NEW_ANIM_SECTION + "\n" + content[end_idx:]

with open(SHADER_PATH, 'w', encoding='utf-8') as f:
    f.write(content)

print(f"Shader updated successfully. New length: {len(content)} chars")
