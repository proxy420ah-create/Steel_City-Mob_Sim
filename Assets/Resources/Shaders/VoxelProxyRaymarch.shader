// Upgrade NOTE: replaced 'UNITY_INSTANCE_ID' with 'UNITY_VERTEX_INPUT_INSTANCE_ID'

// Upgrade NOTE: commented out 'float4x4 _CameraToWorld', a built-in variable
// Upgrade NOTE: replaced '_CameraToWorld' with 'unity_CameraToWorld'

// Steel City: Mob Sim — Proxy-Box Voxel Raymarch Shader
// VoxelProxyRaymarch.shader
//
// Fragment shader that raymarches a voxel volume inside a proxy cube mesh.
// Only pixels covered by the cube's screen footprint run the shader.
// Off-screen cubes are frustum-culled by Unity's mesh pipeline automatically.
//
// Depth compositing: writes SV_Depth so multiple volumes composite correctly
// (far volumes drawn first, near volumes depth-test against them).
// Rays that miss all solid voxels discard, leaving existing pixels intact.
// Transparent materials (palette alpha < 1 — auto glass, window glass) blend
// their tinted surface front-to-back while the march continues through them,
// up to _GlassMaxLayers surfaces per ray; a ray whose glass budget is exhausted
// terminates as a frosted pane (never an opaque wall voxel); glass-only rays
// discard and preserve the content behind. Depth is written at the nearest
// visible surface (glass face).
//
// Ported from MobSimVoxelRaymarch.compute — same DDA, lighting, and shadow logic.

Shader "SteelCity/VoxelProxyRaymarch"
{
    Properties
    {
        _BackgroundColor("Background Color", Color) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off
        ZWrite On
        ZTest LEqual

        // Shared raymarch implementation — the three passes below wrap fragCore:
        //   0 = combined legacy (opaque + glass in one march)
        //   1 = opaque only (glass cells treated as air)
        //   2 = glass only (accumulated tint, alpha-blended over pass 1)
        CGINCLUDE
            #include "UnityCG.cginc"

            // ---- Bindings (same as compute shader) ----
            StructuredBuffer<uint>   _VoxelData;
            StructuredBuffer<float4> _MaterialColors;
            StructuredBuffer<float4> _ChunkTints;
            // Per-voxel glass pane group ids (0 = non-glass/ungrouped). A ray
            // charges each contiguous pane's tint once, not per cell crossed.
            StructuredBuffer<uint>   _PaneIDs;
            // Two-level traversal: one bit per 4^3 brick — 1 = some solid
            // voxel inside. All-air bricks are jumped in one step instead of
            // ~11 cell iterations (MultiDDA brick-map style). Per-asset like
            // _PaneIDs: no bufferOffset on the uniform/instanced paths.
            StructuredBuffer<uint>   _OccupancyData;
            int    _HasOccupancy;                    // 0 = bind ignored, flat DDA
            // Sectors only: per-instance WORD offset into _OccupancyData so
            // each merged building reads its own brick grid. Carried to the
            // fragment stage on the otherwise-unused yaw varying.
            StructuredBuffer<uint>   _BuildingOccOffsets;
            // Sub-pixel ray dither amplitude in world units (0 = off) —
            // lateral origin jitter so openings narrower than the pixel
            // grid still get sampled (window-through-window bores).
            float  _RayJitter;
            float  _IterVis;    // debug vis: 0 off | 1 iter heat | 2 R=vox,G=skip,B=shadow | 3 miss-only heat
            int    _DebugShowProxy; // debug: instanced frags render solid magenta, march bypassed
            // Forced-glass material ids — glass even when palette alpha is
            // opaque (an emissive/solid id that should still transmit). 0 = empty.
            uint  _GlassForcedMat0; uint _GlassForcedMat1; uint _GlassForcedMat2; uint _GlassForcedMat3;
            uint  _GlassForcedMat4; uint _GlassForcedMat5; uint _GlassForcedMat6; uint _GlassForcedMat7;
            float _FlaggedGlassAlpha;   // transmission alpha for forced-glass mats

            int    _MaterialCount;
            float3 _VolumeDims;
            float  _VoxelSize;
            float3 _VolumeOffset;
            // Tight content AABB in voxel units (Max exclusive). When set, the DDA
            // marches this region instead of the full container — skips empty
            // container space above flat content. Zero/unset = full bounds.
            float3 _TightBoundsMin;
            float3 _TightBoundsMax;
            float4x4 _VolumeRotation;
            float4x4 _VolumeInvRotation;
            int    _MaxSteps;
            float4 _BackgroundColor;
            int    _IsOrthographic;
            int    _CheapShading; // skips SmoothNormal blend for distant/LOD'd chunks
            int    _UnlitLod;    // skips fill/cam lighting + shadow-adjust math entirely (ultra-far tier)
            int    _LodDebugEnabled; // debug: tint hit color by LOD tier
            float4 _LodDebugColor;

            float3 _LightDirection;
            float  _LightIntensity;
            float  _AmbientIntensity;
            float  _FillIntensity;
            float3 _LightColor;

            float _ShadowNormalNudge;
            float _ShadowLightNudge;
            int   _ShadowSkipSteps;
            int   _ShadowMaxSteps;
            int   _ShadowEnabled;

            // Glass/tuning globals — pushed from VoxelChunkManager.ApplyGlassSettings
            // (Shader.SetGlobal*); 0/unset degrades to fully-opaque behavior.
            float _GlassSpecular;
            int   _GlassMaxLayers;

            int _SunLightEnabled;
            int _AmbientEnabled;
            int _FillEnabled;
            int _CamLightEnabled;

            float4x4 _ProxyCamToWorld;
            float4x4 _ProxyInvProj;
            float2   _ScreenSize;
            float3   _ProxyCamOrigin;

            // --- Instancing: per-instance data (xyz = world offset, w = yaw radians) ---
            // Buffer layout: first N float4s = (pos, yaw), next N float4s = (animState, animTime, animSpeed, 0)
            StructuredBuffer<float4> _InstanceOffsets;
            int _InstanceCount;

            // --- Animation group IDs (per-voxel groupID for articulated limb transforms) ---
            StructuredBuffer<uint> _GroupIDs;
            int _GroupIDsEnabled;
            // 1 = bound _VoxelData is ONE shared rest buffer (static instanced asset with no
            // groupIDs, e.g. vehicles) so every instance reads offset 0. 0 = legacy behavior.
            int _SharedRestBuffer;

            // --- Walk keyframe system (per-character-type, shared by all instances) ---
            // 4 keyframes × 10 pose values = 40 floats, packed as 10 float4s (one per pose value, 4 KFs each).
            // Layout: _WalkKeyframes[i] = float4(kf0_val, kf1_val, kf2_val, kf3_val) for pose value i.
            // Pose value index: 0=armSwingL, 1=armSwingR, 2=legStrideL, 3=legStrideR,
            //   4=elbowBendL, 5=elbowBendR, 6=kneeBendL, 7=kneeBendR, 8=forearmTwistL, 9=forearmTwistR
            StructuredBuffer<float4> _WalkKeyframes;
            int _WalkKeyframesEnabled;

            // Per-joint config: axis (0=X,1=Y,2=Z) and sign (+1 or -1) for each limb pair.
            // Packed as: _JointConfig[0] = float4(armAxisL, armAxisR, armSignL, armSignR)
            //            _JointConfig[1] = float4(legAxisL, legAxisR, legSignL, legSignR)
            //            _JointConfig[2] = float4(elbowAxisL, elbowAxisR, elbowSignL, elbowSignR)
            //            _JointConfig[3] = float4(kneeAxisL, kneeAxisR, kneeSignL, kneeSignR)
            //            _JointConfig[4] = float4(legTwistL, legTwistR, 0, 0)
            //            _JointConfig[5] = float4(restPoseLArmZ, restPoseRArmZ, elbowRestL, elbowRestR)
            //            _JointConfig[6] = float4(kneeRestL, kneeRestR, 0, 0)
            StructuredBuffer<float4> _JointConfig;
            int _JointConfigEnabled;

            // Walk cycle config: (cycleDuration, bodyBobAmp, weightShiftAmp, autoMirror)
            float4 _WalkConfig;

            // --- Authored per-model joint pivots (from .anim.json "pivots" dict) ---
            // _Pivots[groupID].xyz = normalized fraction of dims (0.0-1.0). Overrides the
            // hardcoded fractional pivot approximation below when enabled, so models with
            // proportions different from the original hoodlum (16x32x10) still articulate
            // at the correct joint locations.
            StructuredBuffer<float4> _Pivots;
            int _PivotsEnabled;

            // --- Static animation parameters (looking/aiming/crouching/jointOffset/aimSweep/sitting) ---
            // Packed as 15 float4s, uploaded once per character type:
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
            // [12] = (aimSweep.amp, aimSweep.freq, aimSweep.headFollow, 0)  -- Aim Sweep (state 10)
            // [13] = (sitting.legStrideL, sitting.legStrideR, sitting.kneeBendL, sitting.kneeBendR)  -- Sit (state 11)
            // [14] = (sitting.armSwingL, sitting.armSwingR, sitting.headPitch, 0)
            StructuredBuffer<float4> _AnimStaticParams;
            int _AnimStaticParamsEnabled;

            // --- Building instancing (sector baking): per-building metadata in a flat merged buffer ---
            // _BuildingMeta[i] = (bufferOffset, dimsX, dimsY, dimsZ)
            // _BuildingPositions[i] = (worldOffsetX, worldOffsetY, worldOffsetZ, 0)
            StructuredBuffer<float4> _BuildingMeta;
            StructuredBuffer<float4> _BuildingPositions;

            // ---- Bit layout ----
            #define VX_SHAPE_SHIFT     12
            #define VX_ROTATION_SHIFT  9
            #define VX_MATERIAL_MASK   0x1FFu
            #define VX_SHAPE_MASK      0xFu
            #define VX_ROTATION_MASK   0x7u

            uint VxMaterial(uint v)  { return v & VX_MATERIAL_MASK; }
            uint VxShape(uint v)     { return (v >> VX_SHAPE_SHIFT) & VX_SHAPE_MASK; }
            uint VxRotation(uint v)  { return (v >> VX_ROTATION_SHIFT) & VX_ROTATION_MASK; }

            // ---- Structs ----
            struct Attributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos   : TEXCOORD0;
                float3 volumeOffset : TEXCOORD1;
                float  yaw         : TEXCOORD2;
                float4 instMeta    : TEXCOORD3; // (bufferOffset, dimsX, dimsY, dimsZ) for building instancing
                float  voxelSize   : TEXCOORD4; // per-building voxel size (pos.w) or uniform fallback
                float  animState   : TEXCOORD5; // animation state (0=Idle, 1=Walking, 2=Looking, etc.)
                float  animTime    : TEXCOORD6; // seconds since animation started
                float  animSpeed   : TEXCOORD7; // walk speed multiplier
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct FragOutput
            {
                float4 color : SV_Target0;
                float  depth : SV_Depth;
            };

            // ---- Helpers ----
            int3 VolumeDimsInt()
            {
                return int3(_VolumeDims + 0.5);
            }

            bool InBounds(int3 v, int3 dims)
            {
                return all(v >= int3(0, 0, 0)) && all(v < dims);
            }

            int VoxelIndex(int3 v, int3 dims)
            {
                return v.x + v.y * dims.x + v.z * dims.x * dims.y;
            }

            bool RayAABB(float3 rayOrigin, float3 rayDir,
                float3 boxMin, float3 boxMax, out float tNear, out float tFar)
            {
                float3 invDir = 1.0 / rayDir;
                float3 t0 = (boxMin - rayOrigin) * invDir;
                float3 t1 = (boxMax - rayOrigin) * invDir;

                float3 tMin = min(t0, t1);
                float3 tMax = max(t0, t1);

                tNear = max(max(tMin.x, tMin.y), tMin.z);
                tFar  = min(min(tMax.x, tMax.y), tMax.z);

                return tNear <= tFar && tFar >= 0.0;
            }

            // ---- DDA cell advance ----
            // Steps the march into the next voxel along the ray, updating the
            // hit distance and face normal. Shared by the loop tail and by the
            // transparent-surface path (which steps past glass instead of
            // terminating the march at it).
            void AdvanceDDA(inout int3 voxel, inout float3 tMax,
                            inout float currentT, inout float3 normal,
                            int3 stepDir, float3 tDelta)
            {
                if (tMax.x < tMax.y)
                {
                    if (tMax.x < tMax.z)
                    {
                        voxel.x += stepDir.x;
                        currentT = tMax.x;
                        tMax.x += tDelta.x;
                        normal = float3(-stepDir.x, 0, 0);
                    }
                    else
                    {
                        voxel.z += stepDir.z;
                        currentT = tMax.z;
                        tMax.z += tDelta.z;
                        normal = float3(0, 0, -stepDir.z);
                    }
                }
                else
                {
                    if (tMax.y < tMax.z)
                    {
                        voxel.y += stepDir.y;
                        currentT = tMax.y;
                        tMax.y += tDelta.y;
                        normal = float3(0, -stepDir.y, 0);
                    }
                    else
                    {
                        voxel.z += stepDir.z;
                        currentT = tMax.z;
                        tMax.z += tDelta.z;
                        normal = float3(0, 0, -stepDir.z);
                    }
                }
            }

            // ---- Cheap glass lighting ----
            // Ambient + wrapped diffuse + optional sun glint. Shared by the
            // accumulate-and-continue path and the terminal frosted-pane path.
            float3 GlassLit(float3 normal, float3 localRd,
                            float3x3 volRot, float3x3 volInvRot)
            {
                float3 glassN = normalize(mul(volInvRot, normal));
                float gDot = dot(glassN, _LightDirection) * 0.5 + 0.5;
                gDot *= gDot;
                float3 gLit = float3(_AmbientIntensity, _AmbientIntensity, _AmbientIntensity * 1.03)
                            + _LightColor * gDot * _LightIntensity;

                // Sun glint: reflect the local view ray about the face
                // normal; alignment with the local light dir = specular pop.
                if (_GlassSpecular > 0.0)
                {
                    float3 lLocal = mul(volRot, _LightDirection);
                    float spec = pow(saturate(dot(reflect(localRd, normal), lLocal)), 24.0);
                    gLit += _LightColor.rgb * spec * _GlassSpecular;
                }
                return gLit;
            }

            // --- Two-level traversal: 4^3 brick occupancy skip ---
            // _OccupancyData holds one bit per 4^3 voxel brick. An all-air
            // brick is crossed in a single boundary jump instead of ~11
            // voxel-cell DDA iterations — the dominant win on interior-heavy
            // volumes (buildings are mostly air inside their tight AABB).
            // 4³ over 8³: a thin slab only poisons the brick band it sits in,
            // freeing the air above to skip (fixes "sub-brick crawl" on
            // roofs/thin walls — visible in _IterVis mode 2 as pure-red).
            static const int BRICK_SHIFT = 2;
            static const int BRICK_SIZE  = 4;

            bool BrickOccupied(int3 brick, int3 bdims, uint occWordOffset)
            {
                // Out-of-grid bricks (ray leaving the volume) walk normally —
                // the caller's InBounds test ends the march next iteration.
                if (any(brick < 0) || any(brick >= bdims)) return true;
                int bidx = brick.x + brick.y * bdims.x + brick.z * bdims.x * bdims.y;
                return ((_OccupancyData[occWordOffset + (uint)(bidx >> 5)] >> (bidx & 31)) & 1u) != 0u;
            }

            // t (local-space ray param) at which the ray exits `brick`, plus
            // the exit-face normal = entry normal of the landing cell.
            float BrickExitT(float3 posL, float3 rd, float3 volOffset, float voxelSize,
                             int3 brick, out float3 exitNormal)
            {
                float3 bMin = volOffset + (float3)(brick << BRICK_SHIFT) * voxelSize;
                float3 bMax = bMin + (float)BRICK_SIZE * voxelSize;
                float3 face;
                face.x = rd.x > 0.0 ? bMax.x : bMin.x;
                face.y = rd.y > 0.0 ? bMax.y : bMin.y;
                face.z = rd.z > 0.0 ? bMax.z : bMin.z;
                // Parallel-to-axis rays get a huge t and lose the min() —
                // same trick as the flat DDA's inf deltaDist.
                float3 safeRd = abs(rd) < 1e-8 ? float3(1e-8, 1e-8, 1e-8) : rd;
                float3 tb = (face - posL) / safeRd;
                float t = min(min(tb.x, tb.y), tb.z);
                if (tb.x <= tb.y && tb.x <= tb.z) exitNormal = float3(-sign(rd.x), 0, 0);
                else if (tb.y <= tb.z)            exitNormal = float3(0, -sign(rd.y), 0);
                else                              exitNormal = float3(0, 0, -sign(rd.z));
                return t;
            }

            // Rebuild per-axis distance-to-next-plane after a brick jump
            // teleports the ray — mirrors the DDA init at the top of the march.
            float3 ResyncSideDist(float3 localStart, int3 voxel, float3 rd, float3 deltaDist)
            {
                float3 sideDist;
                if (rd.x < 0) sideDist.x = (localStart.x - float(voxel.x)) * deltaDist.x;
                else          sideDist.x = (float(voxel.x + 1) - localStart.x) * deltaDist.x;
                if (rd.y < 0) sideDist.y = (localStart.y - float(voxel.y)) * deltaDist.y;
                else          sideDist.y = (float(voxel.y + 1) - localStart.y) * deltaDist.y;
                if (rd.z < 0) sideDist.z = (localStart.z - float(voxel.z)) * deltaDist.z;
                else          sideDist.z = (float(voxel.z + 1) - localStart.z) * deltaDist.z;
                return sideDist;
            }

            // ---- Glass gate: palette alpha OR forced-glass material id ----
            bool IsGlassVoxel(uint mat, float baseAlpha)
            {
                if (baseAlpha < 0.99) return true;
                return mat == _GlassForcedMat0 || mat == _GlassForcedMat1
                    || mat == _GlassForcedMat2 || mat == _GlassForcedMat3
                    || mat == _GlassForcedMat4 || mat == _GlassForcedMat5
                    || mat == _GlassForcedMat6 || mat == _GlassForcedMat7;
            }

            // ---- Smooth normal from voxel neighbourhood ----
            float3 SmoothNormal(int3 v, int3 dims, uint bufferOffset)
            {
                float cx = 0, cy = 0, cz = 0;
                int3 nxp = v + int3(1, 0, 0);  int3 nxm = v + int3(-1, 0, 0);
                int3 nyp = v + int3(0, 1, 0);  int3 nym = v + int3(0, -1, 0);
                int3 nzp = v + int3(0, 0, 1);  int3 nzm = v + int3(0, 0, -1);

                if (InBounds(nxp, dims)) cx += (VxMaterial(_VoxelData[bufferOffset + VoxelIndex(nxp, dims)]) != 0u) ? 1.0 : 0.0;
                if (InBounds(nxm, dims)) cx -= (VxMaterial(_VoxelData[bufferOffset + VoxelIndex(nxm, dims)]) != 0u) ? 1.0 : 0.0;
                if (InBounds(nyp, dims)) cy += (VxMaterial(_VoxelData[bufferOffset + VoxelIndex(nyp, dims)]) != 0u) ? 1.0 : 0.0;
                if (InBounds(nym, dims)) cy -= (VxMaterial(_VoxelData[bufferOffset + VoxelIndex(nym, dims)]) != 0u) ? 1.0 : 0.0;
                if (InBounds(nzp, dims)) cz += (VxMaterial(_VoxelData[bufferOffset + VoxelIndex(nzp, dims)]) != 0u) ? 1.0 : 0.0;
                if (InBounds(nzm, dims)) cz -= (VxMaterial(_VoxelData[bufferOffset + VoxelIndex(nzm, dims)]) != 0u) ? 1.0 : 0.0;

                float3 n = normalize(float3(-cx, -cy, -cz));
                if (length(n) < 0.001) n = float3(0, 1, 0);
                return n;
            }

            // ---- Animation group transform ----
            // Returns the world-space offset for a voxel based on its groupID and animation state.
            // Approach B: inverse-transform sampling in DDA — the ray "sees" voxels at posed positions.
            // GroupTransformOffset is kept for reference/debugging but no longer applied to output.
            // Pivot points are in voxel-local space (voxel units, origin = volume corner).
            // For a 16x32x10 character: head pivot ~(8,25,5), shoulders ~(4,24,5)/(12,24,5), hips ~(6,11,5)/(10,11,5)
            // These are computed from dims at runtime.
            float3x3 RotationX(float angle)
            {
                float c = cos(angle), s = sin(angle);
                return float3x3(1, 0, 0, 0, c, -s, 0, s, c);
            }
            float3x3 RotationY(float angle)
            {
                float c = cos(angle), s = sin(angle);
                return float3x3(c, 0, -s, 0, 1, 0, s, 0, c);
            }
            float3x3 RotationZ(float angle)
            {
                float c = cos(angle), s = sin(angle);
                return float3x3(c, -s, 0, s, c, 0, 0, 0, 1);
            }
            float3x3 RotationByAxis(int axis, float angle)
            {
                if (axis == 1) return RotationY(angle);
                if (axis == 2) return RotationZ(angle);
                return RotationX(angle);
            }

            // ---- Walk keyframe interpolation (ported from animator) ----
            // Catmull-Rom spline: flows through keyframes with continuous velocity.
            // Pauses only at natural extremes (contact), max velocity at passing (neutral).
            float CatmullRom(float p0, float p1, float p2, float p3, float t)
            {
                float t2 = t * t;
                float t3 = t2 * t;
                return 0.5 * (
                    (2.0 * p1) +
                    (-p0 + p2) * t +
                    (2.0*p0 - 5.0*p1 + 4.0*p2 - p3) * t2 +
                    (-p0 + 3.0*p1 - 3.0*p2 + p3) * t3
                );
            }

            // Smoothstep: S-curve interpolation (0->1).
            float Smoothstep01(float t) { return t * t * (3.0 - 2.0 * t); }
            // Cosine: smooth sine ease in/out (0->1).
            float CosineInterp(float t) { return 0.5 - 0.5 * cos(t * 3.14159265); }

            // Get interpolated walk pose value for a given pose index (0-9) at cycle phase.
            // _WalkKeyframes[poseIdx] = float4(kf0, kf1, kf2, kf3)
            float GetWalkPoseValue(int poseIdx, float cyclePhase, bool autoMirror)
            {
                float4 kfs = _WalkKeyframes[poseIdx];
                float kf0 = kfs.x, kf1 = kfs.y, kf2 = kfs.z, kf3 = kfs.w;

                // Auto-mirror: kf2 = mirror(kf0), kf3 = mirror(kf1)
                // For armSwingL/R, legStrideL/R, etc., mirroring swaps L↔R values.
                // But since we store each pose value separately, the mirror is already
                // baked into the buffer by C# (kf2 for armSwingL = kf0 for armSwingR, etc.)
                // So the shader just reads the 4 values as-is.

                // Determine which segment we're in (0→1, 1→2, 2→3, 3→0)
                float segPhase = cyclePhase * 4.0;
                int seg = (int)floor(segPhase);
                seg = seg % 4;
                if (seg < 0) seg += 4;
                float t = segPhase - floor(segPhase);

                // Get the 4 control points for Catmull-Rom (cyclic)
                float p0, p1, p2, p3;
                if (seg == 0) { p0 = kf3; p1 = kf0; p2 = kf1; p3 = kf2; }
                else if (seg == 1) { p0 = kf0; p1 = kf1; p2 = kf2; p3 = kf3; }
                else if (seg == 2) { p0 = kf1; p1 = kf2; p2 = kf3; p3 = kf0; }
                else { p0 = kf2; p1 = kf3; p2 = kf0; p3 = kf1; }

                return CatmullRom(p0, p1, p2, p3, t);
            }

            // Compute the walk cycle phase (0.0 → 1.0) from animTime and animSpeed.
            float GetWalkCyclePhase(float animTime, float animSpeed)
            {
                float cycleDur = _WalkConfig.x / max(0.01, animSpeed);
                float phase = fmod(animTime, cycleDur);
                if (phase < 0.0) phase += cycleDur;
                return phase / cycleDur;
            }

            // ---- Animation structs ----
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

                bool isWalkState = animState > 0.5 && animState < 1.5;       // Walking (1) only
                bool isAimWalkState = animState > 2.5 && animState < 3.5;    // Aim Walk (3) only
                bool isSweepState = animState > 9.5 && animState < 10.5;     // Aim Sweep (10)
                bool isAimingState = (animState > 2.5 && animState < 4.5) || isSweepState; // Aim Walk (3), Aiming (4), Aim Sweep (10)
                bool isCrouchingState = animState > 4.5 && animState < 5.5;
                bool isSittingState = animState > 10.5 && animState < 11.5; // Sit (11)

                float walkPhase = 0.0;
                bool useKeyframesWalk = false;           // Walking only — for arms/forearms
                bool useKeyframesWalkOrAimWalk = false;  // Walking or Aim Walk — for legs/shins
                if ((isWalkState || isAimWalkState) && _WalkKeyframesEnabled != 0) {
                    walkPhase = GetWalkCyclePhase(animTime, animSpeed);
                    useKeyframesWalkOrAimWalk = true;
                    if (isWalkState) useKeyframesWalk = true;
                }

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
                float4 sweepP  = ASP(12); if (!_AnimStaticParamsEnabled) sweepP = float4(0.55, 1.6, 0.6, 0);
                // Sit (state 11): thighs forward, shins down, torso upright
                float4 sitP    = ASP(13); if (!_AnimStaticParamsEnabled) sitP = float4(1.45, 1.45, 1.45, 1.45);
                float4 sitP2   = ASP(14); if (!_AnimStaticParamsEnabled) sitP2 = float4(0.2, 0.2, 0, 0);

                // Aim Sweep (state 10): sinusoidal shoulder-yaw on the ARMED arm's
                // reach (the arm whose aim swing is non-zero); head follows at headFollow.
                float sweepYaw = isSweepState ? sin(animTime * sweepP.y) * sweepP.x : 0.0;

                if (gid == 1u) // Head
                {
                    float headYaw = 0.0, headPitch = 0.0, headTilt = 0.0;
                    if (animState > 1.5 && animState < 2.5) { // Looking (state 2 only)
                        headYaw = sin(animTime * lookP.y) * lookP.x;
                        headPitch = sin(animTime * lookP.w) * lookP.z;
                    } else if (isAimingState) { // Aim Walk (3), Aiming (4), Aim Sweep (10)
                        headYaw = aimP1.y + sweepYaw * sweepP.z;
                        headPitch = aimP1.z;
                        headTilt = aimP1.w;
                    } else if (isCrouchingState) { // Crouching (5)
                        headPitch = crouchP1.y;
                    } else if (isSittingState) { // Sit (11)
                        headPitch = sitP2.z;
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
                    if (useKeyframesWalk) { // Walking only — keyframe pose
                        swing = armCfg.z * GetWalkPoseValue(0, walkPhase, _WalkConfig.w > 0.5);
                    } else if (isAimingState) { // Aim Walk (3) or Aiming (4)
                        swing = armCfg.z * aimP2.x;       // signL * aiming.armSwingL
                        reach = aimP2.z + (swing != 0.0 ? sweepYaw : 0.0); // aiming.shoulderReachL + sweep on armed arm
                    } else if (isCrouchingState) {
                        swing = armCfg.z * crouchP1.z;     // signL * crouching.armSwingL
                    } else if (isSittingState) {
                        swing = armCfg.z * sitP2.x;        // signL * sitting.armSwingL
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
                    if (useKeyframesWalk) {
                        swing = armCfg.w * GetWalkPoseValue(1, walkPhase, _WalkConfig.w > 0.5);
                    } else if (isAimingState) {
                        swing = armCfg.w * aimP2.y;        // signR * aiming.armSwingR
                        reach = aimP2.w + (swing != 0.0 ? sweepYaw : 0.0); // aiming.shoulderReachR + sweep on armed arm
                    } else if (isCrouchingState) {
                        swing = armCfg.w * crouchP1.w;     // signR * crouching.armSwingR
                    } else if (isSittingState) {
                        swing = armCfg.w * sitP2.y;        // signR * sitting.armSwingR
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
                    if (useKeyframesWalkOrAimWalk) { // Walking or Aim Walk
                        stride = legCfg.z * GetWalkPoseValue(2, walkPhase, _WalkConfig.w > 0.5);
                    } else if (isCrouchingState) {
                        stride = legCfg.z * crouchP2.x;    // signL * crouching.legStrideL
                    } else if (isSittingState) {
                        stride = legCfg.z * sitP.x;        // signL * sitting.legStrideL
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
                    if (useKeyframesWalkOrAimWalk) {
                        stride = legCfg.w * GetWalkPoseValue(3, walkPhase, _WalkConfig.w > 0.5);
                    } else if (isCrouchingState) {
                        stride = legCfg.w * crouchP2.y;    // signR * crouching.legStrideR
                    } else if (isSittingState) {
                        stride = legCfg.w * sitP.y;        // signR * sitting.legStrideR
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
                    if (useKeyframesWalk) {
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
                    if (useKeyframesWalk) {
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
                    if (useKeyframesWalkOrAimWalk) {
                        bend = kneeCfg.z * GetWalkPoseValue(6, walkPhase, _WalkConfig.w > 0.5);
                    } else if (isCrouchingState) {
                        bend += kneeCfg.z * crouchP2.z;    // signL * crouching.kneeBendL
                    } else if (isSittingState) {
                        bend += kneeCfg.z * sitP.z;        // signL * sitting.kneeBendL
                    }
                    rot = RotationByAxis((int)kneeCfg.x, bend);
                    return true;
                }
                else if (gid == 7u) // Right shin (knee hinge)
                {
                    float bend = kneeCfg.w * kneeRest.y;   // signR * rightRest
                    if (useKeyframesWalkOrAimWalk) {
                        bend = kneeCfg.w * GetWalkPoseValue(7, walkPhase, _WalkConfig.w > 0.5);
                    } else if (isCrouchingState) {
                        bend += kneeCfg.w * crouchP2.w;    // signR * crouching.kneeBendR
                    } else if (isSittingState) {
                        bend += kneeCfg.w * sitP.w;        // signR * sitting.kneeBendR
                    }
                    rot = RotationByAxis((int)kneeCfg.y, bend);
                    return true;
                }

                return false;
            }

            // ---- Compute group offset (matches CPU offset logic) ----
            // Returns offset in WORLD units (raw JSON values scaled by voxelSize).
            // Inlined: HLSL doesn't allow recursion. Children's parent is always a root
            // group (2,3,4,5), so we inline the root-group offset logic directly.
            float3 ComputeGroupOffset(uint gid, bool hasParent, uint parentGid,
                float bodyLower, float modelLower, float voxelSize)
            {
                float3 offset = float3(0, 0, 0);
                if (hasParent) {
                    // Children inherit parent's offset — inline parent's root-group logic
                    uint pgid = parentGid;
                    int pJoIdx = 6 + (int)pgid;
                    if (_AnimStaticParamsEnabled)
                        offset = _AnimStaticParams[pJoIdx].xyz;
                    if (bodyLower != 0.0 && (pgid == 1u || pgid == 2u || pgid == 3u))
                        offset.y -= bodyLower;
                    if (modelLower != 0.0 && (pgid >= 1u && pgid <= 5u))
                        offset.y -= modelLower;
                } else if (gid >= 1u && gid <= 5u) {
                    // Root parent groups: use jointOffset (raw voxel units from JSON)
                    int joIdx = 6 + (int)gid;
                    if (_AnimStaticParamsEnabled)
                        offset = _AnimStaticParams[joIdx].xyz;
                    // Add bodyLower to upper body parent groups (head, arms) — not legs
                    if (bodyLower != 0.0 && (gid == 1u || gid == 2u || gid == 3u))
                        offset.y -= bodyLower;
                    // Add modelLower to ALL root parent groups (1-5)
                    if (modelLower != 0.0 && (gid >= 1u && gid <= 5u))
                        offset.y -= modelLower;
                }
                return offset * voxelSize;
            }

            // ---- Compute full transform chain for a group (matches CPU ComputeGroupRotation) ----
            bool ComputeGroupRotation(
                uint groupID, float3 dims, float voxelSize,
                float animState, float animTime, float animSpeed,
                out GroupTransformResult result)
            {
                result.chainLength = 0;
                result.offset = float3(0, 0, 0);
                result.chain[0].pivot = float3(0,0,0); result.chain[0].rot = float3x3(1,0,0, 0,1,0, 0,0,1);
                result.chain[1].pivot = float3(0,0,0); result.chain[1].rot = float3x3(1,0,0, 0,1,0, 0,0,1);
                result.chain[2].pivot = float3(0,0,0); result.chain[2].rot = float3x3(1,0,0, 0,1,0, 0,0,1);

                // T-Pose: no transforms
                if (animState > 8.5 && animState < 9.5) return false;

                // Body transform params
                bool isAimingState = (animState > 2.5 && animState < 4.5) || (animState > 9.5 && animState < 10.5); // Aim Walk (3), Aiming (4), Aim Sweep (10)
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
                float3 bodyOffset = float3(0, -bodyLower - modelLower, 0) * voxelSize;

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
                    result.offset = ComputeGroupOffset(groupID, hasParent, parentGid, bodyLower, modelLower, voxelSize);
                    return result.offset.x != 0.0 || result.offset.y != 0.0 || result.offset.z != 0.0;
                }

                // Build chain: [own, parent_own, body]
                // Chain order matches CPU: own first, then parent's chain, then body (outermost)
                // All indices are compile-time constants — HLSL requires this for local arrays.
                if (hasOwnRot && hasParentRot && bodyApplies) {
                    result.chain[0].pivot = GetGroupPivotRaw(groupID, dims) * voxelSize; result.chain[0].rot = ownRot;
                    result.chain[1].pivot = GetGroupPivotRaw(parentGid, dims) * voxelSize; result.chain[1].rot = parentRot;
                    result.chain[2].pivot = bodyPivot; result.chain[2].rot = bodyRot;
                    result.chainLength = 3;
                } else if (hasOwnRot && hasParentRot) {
                    result.chain[0].pivot = GetGroupPivotRaw(groupID, dims) * voxelSize; result.chain[0].rot = ownRot;
                    result.chain[1].pivot = GetGroupPivotRaw(parentGid, dims) * voxelSize; result.chain[1].rot = parentRot;
                    result.chainLength = 2;
                } else if (hasOwnRot && bodyApplies) {
                    result.chain[0].pivot = GetGroupPivotRaw(groupID, dims) * voxelSize; result.chain[0].rot = ownRot;
                    result.chain[1].pivot = bodyPivot; result.chain[1].rot = bodyRot;
                    result.chainLength = 2;
                } else if (hasParentRot && bodyApplies) {
                    result.chain[0].pivot = GetGroupPivotRaw(parentGid, dims) * voxelSize; result.chain[0].rot = parentRot;
                    result.chain[1].pivot = bodyPivot; result.chain[1].rot = bodyRot;
                    result.chainLength = 2;
                } else if (hasOwnRot) {
                    result.chain[0].pivot = GetGroupPivotRaw(groupID, dims) * voxelSize; result.chain[0].rot = ownRot;
                    result.chainLength = 1;
                } else if (hasParentRot) {
                    result.chain[0].pivot = GetGroupPivotRaw(parentGid, dims) * voxelSize; result.chain[0].rot = parentRot;
                    result.chainLength = 1;
                } else if (bodyApplies) {
                    result.chain[0].pivot = bodyPivot; result.chain[0].rot = bodyRot;
                    result.chainLength = 1;
                } else {
                    result.chainLength = 0;
                }

                // Compute offset
                result.offset = ComputeGroupOffset(groupID, hasParent, parentGid, bodyLower, modelLower, voxelSize);

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
                    if (2 < r.chainLength) { float3 rel = restPos - r.chain[2].pivot; restPos = mul(transpose(r.chain[2].rot), rel) + r.chain[2].pivot; }
                    if (1 < r.chainLength) { float3 rel = restPos - r.chain[1].pivot; restPos = mul(transpose(r.chain[1].rot), rel) + r.chain[1].pivot; }
                    if (0 < r.chainLength) { float3 rel = restPos - r.chain[0].pivot; restPos = mul(transpose(r.chain[0].rot), rel) + r.chain[0].pivot; }
                    return restPos - voxelLocalPos;
                }

                GroupTransformResult result;
                if (!ComputeGroupRotation(groupID, dims, voxelSize, animState, animTime, animSpeed, result))
                    return float3(0, 0, 0);

                // Inverse: subtract offset first, then apply inverse chain in reverse order
                float3 restPos = voxelLocalPos - result.offset;
                if (2 < result.chainLength) { float3 rel = restPos - result.chain[2].pivot; restPos = mul(transpose(result.chain[2].rot), rel) + result.chain[2].pivot; }
                if (1 < result.chainLength) { float3 rel = restPos - result.chain[1].pivot; restPos = mul(transpose(result.chain[1].rot), rel) + result.chain[1].pivot; }
                if (0 < result.chainLength) { float3 rel = restPos - result.chain[0].pivot; restPos = mul(transpose(result.chain[0].rot), rel) + result.chain[0].pivot; }
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
                if (0 < result.chainLength) { float3 rel = pos - result.chain[0].pivot; pos = mul(result.chain[0].rot, rel) + result.chain[0].pivot; }
                if (1 < result.chainLength) { float3 rel = pos - result.chain[1].pivot; pos = mul(result.chain[1].rot, rel) + result.chain[1].pivot; }
                if (2 < result.chainLength) { float3 rel = pos - result.chain[2].pivot; pos = mul(result.chain[2].rot, rel) + result.chain[2].pivot; }
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

            // ---- Lighting (ported from compute shader) ----
            float3 GetLighting(float3 normal, uint mat)
            {
                // Emissive mats skip shading. 113 Lit Window removed — palette
                // now aliases it to Window Glass, so it never reaches this path.
                if (mat == 115u || mat == 116u || mat == 117u || mat == 124u)
                    return float3(1.0, 1.0, 1.0);

                if (mat == 112u || mat == 114u)
                    return float3(0.85, 0.85, 0.85);

                float sky = dot(normal, _LightDirection) * 0.5 + 0.5;
                sky = sky * sky;

                float3 fillDir = normalize(float3(-_LightDirection.x * 0.5, _LightDirection.y * 0.3, -_LightDirection.z * 0.5));
                float fill = dot(normal, fillDir) * 0.5 + 0.5;
                fill = fill * fill;

                float3 camLight = normalize(float3(0.6, 0.5, 0.6));
                float cam = dot(normal, camLight) * 0.5 + 0.5;
                cam = cam * 0.20;

                float3 ambient = float3(_AmbientIntensity, _AmbientIntensity, _AmbientIntensity * 1.03);
                float3 mainLight = _LightColor * sky * _LightIntensity;
                float3 fillLight = fill * _FillIntensity;
                float3 result = float3(0, 0, 0);
                if (_AmbientEnabled)  result += ambient;
                if (_SunLightEnabled) result += mainLight;
                if (_FillEnabled)     result += fillLight;
                if (_CamLightEnabled) result += cam;
                return result;
            }

            // ---- Vertex shader ----
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float4 worldPos = mul(unity_ObjectToWorld, float4(input.positionOS, 1.0));
                output.worldPos = worldPos.xyz;
                output.positionCS = mul(UNITY_MATRIX_VP, worldPos);
            #ifdef UNITY_INSTANCING_ENABLED
                #if defined(BUILDING_INSTANCING)
                    float4 meta = _BuildingMeta[unity_InstanceID];
                    float4 pos = _BuildingPositions[unity_InstanceID];
                    output.volumeOffset = pos.xyz;
                    // yaw is unused by buildings — it carries this instance's
                    // WORD offset into the merged sector occupancy bitmask.
                    output.yaw = (float)_BuildingOccOffsets[unity_InstanceID];
                    output.instMeta = meta; // (bufferOffset, dimsX, dimsY, dimsZ)
                    output.voxelSize = pos.w;
                    output.animState = 0.0;
                    output.animTime = 0.0;
                    output.animSpeed = 1.0;
                #else
                    // Per-instance SRV reads moved to fragCore: MPB-bound
                    // StructuredBuffers reach the fragment stage but NOT the
                    // vertex stage on this pipeline — VS reads returned zeros
                    // (volOffset=0 → AABB at origin → instanced draws invisible).
                    // input.instanceID still flows to frag via the instancing
                    // varyings; frag does the _InstanceOffsets fetch itself.
                    output.volumeOffset = float3(0, 0, 0);
                    output.yaw = 0.0;
                    output.instMeta = float4(0.0, _VolumeDims.x, _VolumeDims.y, _VolumeDims.z);
                    output.voxelSize = _VoxelSize;
                    output.animState = 0.0;
                    output.animTime = 0.0;
                    output.animSpeed = 1.0;
                #endif
            #else
                output.volumeOffset = _VolumeOffset;
                output.yaw = 0.0;
                output.instMeta = float4(0.0, _VolumeDims.x, _VolumeDims.y, _VolumeDims.z);
                output.voxelSize = _VoxelSize;
                output.animState = 0.0;
                output.animTime = 0.0;
                output.animSpeed = 1.0;
            #endif
                return output;
            }

            // ---- Fragment shader ----
            // renderMode: 0 = combined legacy | 1 = opaque only | 2 = glass only
            FragOutput fragCore(Varyings input, int renderMode)
            {
                FragOutput o;
                UNITY_SETUP_INSTANCE_ID(input);

                // Debug probe: prove the instanced proxy rasterizes and frag
                // runs — solid magenta at near depth, bypasses the whole march.
                // Outside UNITY_INSTANCING_ENABLED so a non-instanced fallback
                // variant still shows it (that's a failure mode on its own).
                // depth=1.0: reversed-Z convention — NEAREST. (0.0 = farthest,
                // would lose to every other fragment that draws over us.)
                if (_DebugShowProxy == 1)
                {
                    o.color = float4(1.0, 0.0, 1.0, 1.0);
                    o.depth = 1.0;
                    return o;
                }
                float voxelSize = input.voxelSize;

                // Ray origin and direction
                float3 ro;
                float3 rd;

                if (_IsOrthographic)
                {
                    // Orthographic: reconstruct near-plane position from screen coords
                    float2 screenPos = input.positionCS.xy / _ScreenSize;
                    float2 ndc = screenPos * 2.0 - 1.0;
                    float4 clipNear = float4(ndc.x, ndc.y, -1.0, 1.0);
                    float4 viewNear = mul(_ProxyInvProj, clipNear);
                    viewNear /= viewNear.w;
                    float3 worldNear = mul(_ProxyCamToWorld, float4(viewNear.xyz, 1.0)).xyz;

                    ro = worldNear;
                    rd = normalize(mul((float3x3)_ProxyCamToWorld, float3(0, 0, -1)));
                }
                else
                {
                    // Perspective: ray from camera through fragment
                    ro = _ProxyCamOrigin;
                    rd = normalize(input.worldPos - ro);
                }

                // Sub-pixel ray jitter — shifts the ray origin laterally
                // inside its pixel so openings narrower than the pixel grid
                // (aligned window bores) still get sampled. Static hash per
                // pixel → stable stipple rather than temporal shimmer.
                if (_RayJitter > 0.0)
                {
                    float j1 = frac(sin(dot(input.positionCS.xy, float2(127.1, 311.7))) * 43758.5453);
                    float j2 = frac(sin(dot(input.positionCS.xy, float2(269.5, 183.3))) * 43758.5453);
                    float3 tanX = normalize(abs(rd.y) < 0.99 ? cross(rd, float3(0, 1, 0)) : cross(rd, float3(1, 0, 0)));
                    float3 tanY = cross(rd, tanX);
                    ro += (tanX * (j1 - 0.5) + tanY * (j2 - 0.5)) * _RayJitter;
                }

                // --- Per-instance volume data (instancing) or uniforms (non-instanced) ---
                uint bufferOffset = 0;
                uint occWordOffset = 0u;   // sectors offset into _OccupancyData; 0 elsewhere
                int3 dims;
                // Fetched per-instance in the group-instanced branch (frag
                // stage — VS-side MPB buffer reads return zero here); left at
                // defaults on chunk/sector paths.
                float animState = 0.0;
                float animTime = 0.0;
                float animSpeed = 1.0;

            #ifdef UNITY_INSTANCING_ENABLED
                #if defined(BUILDING_INSTANCING)
                    float3 volOffset = input.volumeOffset;
                    bufferOffset = (uint)input.instMeta.x;
                    // Building-instancing: yaw carries the occupancy word offset.
                    occWordOffset = (uint)input.yaw;
                    dims = int3((int)input.instMeta.y, (int)input.instMeta.z, (int)input.instMeta.w);
                    // Buildings are axis-aligned (no rotation in sector baking)
                    float3x3 volRot = float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
                    float3x3 volInvRot = float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
                #else
                    // Fetch per-instance data here — frag-stage SRV reads work
                    // (chunks' _VoxelData proves MPB buffers bind in FS); the
                    // same read in vert returned zeros on this pipeline.
                    float4 instData = _InstanceOffsets[input.instanceID];
                    float3 volOffset = instData.xyz;
                    float yaw = instData.w;
                    float yc = cos(yaw), ys = sin(yaw);
                    float3x3 volRot = float3x3(yc, 0, -ys, 0, 1, 0, ys, 0, yc);
                    float3x3 volInvRot = float3x3(yc, 0, ys, 0, 1, 0, -ys, 0, yc);
                    dims = VolumeDimsInt();
                    // Posed buffer slice for this instance (compute pose) or
                    // shared rest buffer offset 0 — same logic vert used to do.
                    uint instTotalVoxels = (uint)(_VolumeDims.x * _VolumeDims.y * _VolumeDims.z);
                    bufferOffset = (_GroupIDsEnabled == 0 && _SharedRestBuffer == 0)
                        ? (input.instanceID * instTotalVoxels) : 0u;
                    float4 animData = _InstanceOffsets[input.instanceID + _InstanceCount];
                    animState = animData.x;
                    animTime = animData.y;
                    animSpeed = animData.z;

                    // Body bob + weight shift (walking states only, keyframe system enabled)
                    // Skip when compute pose is active (_GroupIDsEnabled == 0) — already baked into posed buffer
                    bool isWalkingState = (animState > 0.5 && animState < 1.5) ||
                                          (animState > 2.5 && animState < 3.5);
                    if (isWalkingState && _WalkKeyframesEnabled != 0 && _GroupIDsEnabled != 0)
                    {
                        float phase = GetWalkCyclePhase(animTime, animSpeed);
                        // Body bob: -cos(phase * 4π) → lowest at contact (0, 0.5), highest at mid-stance (0.25, 0.75)
                        float bobAmp = _WalkConfig.y;
                        float bobY = -cos(phase * 4.0 * 3.14159265) * bobAmp * voxelSize;
                        // Weight shift: sin(phase * 2π) → shifts over stance leg at mid-stance
                        float shiftAmp = _WalkConfig.z;
                        float shiftX = sin(phase * 2.0 * 3.14159265) * shiftAmp * voxelSize;
                        // Apply in volume-local space (before yaw rotation)
                        volOffset.y += bobY;
                        volOffset.x += shiftX;
                    }
                #endif
            #else
                float3 volOffset = _VolumeOffset;
                float3x3 volRot = (float3x3)_VolumeRotation;
                float3x3 volInvRot = (float3x3)_VolumeInvRotation;
                dims = VolumeDimsInt();
            #endif

                // Probe mode 3: dump volOffset as color — world [-50,+50] → [0,1].
                // Middle gray (0.5,0.5,0.5) = volOffset is ZERO (SRV read failed).
                if (_DebugShowProxy == 3)
                {
                    o.color = float4(volOffset.x / 100.0 + 0.5,
                                     volOffset.y / 100.0 + 0.5,
                                     volOffset.z / 100.0 + 0.5, 1.0);
                    o.depth = 1.0;
                    return o;
                }

                // Transform ray into volume local space (handles rotation)
                float3 volumeCenter = volOffset + float3(dims) * voxelSize * 0.5;
                float3 localRo = mul(volRot, ro - volumeCenter) + volumeCenter;
                float3 localRd = mul(volRot, rd);

                // Volume bounds — tight content AABB when provided, else full
                // container. Indexing below still uses container dims/origin.
                bool useTight = _TightBoundsMax.x > 0.0;
                float3 volumeMin = volOffset + (useTight ? _TightBoundsMin : float3(0, 0, 0)) * voxelSize;
                float3 volumeMax = volOffset + (useTight ? _TightBoundsMax : (float3)dims) * voxelSize;

                // Ray-box intersection
                float tNear, tFar;
                if (!RayAABB(localRo, localRd, volumeMin, volumeMax, tNear, tFar))
                {
                    discard;
                }

                // Probe mode 2: green where the ray ENTERS the volume AABB —
                // validates volOffset/dims/voxelSize/yaw/camera uniforms before
                // any voxel data is consulted.
                if (_DebugShowProxy == 2)
                {
                    o.color = float4(0.0, 1.0, 0.0, 1.0);
                    o.depth = 1.0;
                    return o;
                }

                // The DDA counts voxel-plane crossings, so the worst case to
                // traverse the marched region is sizeX+sizeY+sizeZ cells — floor
                // the budget there so LOD tiers can't starve long in-volume rays.
                float3 marchSize = (volumeMax - volumeMin) / voxelSize;
                int stepCap = max(_MaxSteps, (int)(marchSize.x + marchSize.y + marchSize.z) + 4);

                // DDA setup
                float tStart = max(tNear, 0.0);
                float3 startPos = localRo + localRd * (tStart + 0.001);
                float3 localStart = (startPos - volOffset) / voxelSize;
                int3 voxel = clamp((int3)floor(localStart), int3(0, 0, 0), dims - int3(1, 1, 1));

                int3 stepDir = (int3)sign(localRd);
                float3 deltaDist = abs(voxelSize / localRd);

                float3 sideDist;
                if (localRd.x < 0) sideDist.x = (localStart.x - float(voxel.x)) * deltaDist.x;
                else sideDist.x = (float(voxel.x + 1) - localStart.x) * deltaDist.x;
                if (localRd.y < 0) sideDist.y = (localStart.y - float(voxel.y)) * deltaDist.y;
                else sideDist.y = (float(voxel.y + 1) - localStart.y) * deltaDist.y;
                if (localRd.z < 0) sideDist.z = (localStart.z - float(voxel.z)) * deltaDist.z;
                else sideDist.z = (float(voxel.z + 1) - localStart.z) * deltaDist.z;

                float3 tMax = tStart + sideDist;
                float3 tDelta = deltaDist;
                float currentT = tStart;
                float3 normal = float3(0, 1, 0);

                bool hit = false;
                float4 hitColor = _BackgroundColor;
                float3 worldHit = float3(0, 0, 0);

                // Two-level traversal state — occupancy bit test happens once
                // per brick entered, never per voxel step.
                int3 brickDims = (dims + (BRICK_SIZE - 1)) >> BRICK_SHIFT;
                int3 occBrick = int3(-2, -2, -2); // impossible start → forces first test

                // Transparency: materials with palette alpha < 1 (auto glass,
                // window glass) blend their tinted surface over whatever the
                // ray continues to hit instead of terminating the march.
                float3 glassAccum = float3(0, 0, 0);
                float  throughput = 1.0;   // light fraction remaining behind the glass
                float  glassFirstT = 0.0;  // nearest glass surface distance (for depth)
                int    glassLayers = 0;
                uint   lastPane = 0u;      // pane group already charged along this ray (0 = none)

                int iterCount = 0; // _IterVis counters
                int iterVox = 0;   // cell-level DDA steps taken (crawling)
                int iterSkip = 0;  // brick-jump iterations (skipping)
                int iterShadow = 0;
                for (int i = 0; i < stepCap; ++i)
                {
                    ++iterCount;
                    if (!InBounds(voxel, dims))
                        break;

                    // Brick skip — an all-air 4^3 brick is crossed in one
                    // boundary jump instead of ~11 cell steps. Occupancy is
                    // tested once per newly entered brick.
                    if (_HasOccupancy != 0)
                    {
                        int3 brick = voxel >> BRICK_SHIFT;
                        if (brick.x != occBrick.x || brick.y != occBrick.y || brick.z != occBrick.z)
                        {
                            occBrick = brick;
                            if (!BrickOccupied(brick, brickDims, occWordOffset))
                            {
                                float3 posL = localRo + localRd * currentT;
                                float tSkip = max(BrickExitT(posL, localRd, volOffset, voxelSize, brick, normal), currentT) + 0.0005;
                                currentT = tSkip;
                                float3 np = (localRo + localRd * currentT - volOffset) / voxelSize;
                                voxel = (int3)floor(np);
                                tMax = currentT + ResyncSideDist(np, voxel, localRd, deltaDist);
                                ++iterSkip;
                                continue;
                            }
                        }
                    }

                    // Inverse-transform sampling: when groupIDs are enabled, sample voxel data
                    // at the REST position (inverse of the group transform). This makes the ray
                    // "see" voxels at their posed positions, producing visible limb movement.
                    int3 sampleVoxel = voxel;
                    if (_GroupIDsEnabled != 0)
                    {
                        uint gid = _GroupIDs[bufferOffset + VoxelIndex(voxel, dims)];
                        {
                            float3 voxelLocalPos = (float3(voxel) + 0.5) * voxelSize;
                            float3 restOffset = InverseGroupTransformOffset(
                                gid, voxelLocalPos, float3(dims), voxelSize,
                                animState, animTime, animSpeed);
                            float3 restPos = voxelLocalPos + restOffset;
                            int3 restVoxel = (int3)floor(restPos / voxelSize);
                            if (InBounds(restVoxel, dims))
                                sampleVoxel = restVoxel;
                            // else: out-of-bounds = empty, keep original voxel (likely miss)
                        }
                    }

                    uint packed = _VoxelData[bufferOffset + VoxelIndex(sampleVoxel, dims)];
                    uint mat = VxMaterial(packed);

                    if (mat != 0u)
                    {
                        uint maxMat = max(1, (uint)_MaterialCount) - 1u;
                        mat = min(mat, maxMat);

                        float4 baseColor = _MaterialColors[mat];
                        float4 tint = _ChunkTints[mat];
                        baseColor.rgb *= tint.rgb;

                        // Transparent material (palette alpha < 1): accumulate the
                        // tinted surface front-to-back and keep marching — a
                        // windshield pixel resolves to whatever is behind it.
                        // Cheap ambient lighting only; glass skips the smooth-normal
                        // and shadow-ray work. Budget exhausted (layer cap hit or
                        // <3% light left) terminates as a frosted pane below.
                        if (IsGlassVoxel(mat, baseColor.a))
                        {
                            // Forced-glass materials may carry an opaque palette
                            // alpha — fall back to the configured transmission alpha.
                            float glassA = baseColor.a < 0.99 ? baseColor.a : _FlaggedGlassAlpha;
                            if (renderMode == 1)
                            {
                                // Opaque pass: glass is a hole — the ray keeps
                                // marching to real interior geometry behind it.
                                AdvanceDDA(voxel, tMax, currentT, normal, stepDir, tDelta);
                                continue;
                            }
                            // Pane dedup: a contiguous glass cluster is one
                            // surface — charge it once, march through its other
                            // cells for free (grazing a wide pane ≠ N tints).
                            // Pane IDs are per-asset (volume-space) — no
                            // bufferOffset, which is a per-instance/sector
                            // offset into the merged voxel buffer.
                            uint paneId = _PaneIDs[VoxelIndex(sampleVoxel, dims)];
                            if (paneId != 0u && paneId == lastPane)
                            {
                                AdvanceDDA(voxel, tMax, currentT, normal, stepDir, tDelta);
                                continue;
                            }

                            if (throughput > 0.03 && glassLayers < _GlassMaxLayers)
                            {
                                float3 gLit = GlassLit(normal, localRd, volRot, volInvRot);
                                glassAccum += throughput * glassA * baseColor.rgb * gLit;
                                if (glassLayers == 0) glassFirstT = currentT;
                                throughput *= 1.0 - glassA;
                                glassLayers++;
                                lastPane = paneId;
                                AdvanceDDA(voxel, tMax, currentT, normal, stepDir, tDelta);
                                continue;
                            }

                            // Transmission budget exhausted: absorb the remaining
                            // light as full-strength tint and stop — the pane reads
                            // as frosted glass instead of an opaque wall voxel.
                            hitColor = float4(glassAccum + throughput * (baseColor.rgb * GlassLit(normal, localRd, volRot, volInvRot)), 1.0);
                            if (_LodDebugEnabled != 0)
                                hitColor = float4(_LodDebugColor.rgb, hitColor.a);
                            worldHit = ro + rd * (glassLayers > 0 ? glassFirstT : currentT);
                            hit = true;
                            break;
                        }

                        if (renderMode == 2)
                        {
                            // Glass-only pass: the first opaque voxel ends the
                            // collection — pass 1 owns this surface already.
                            break;
                        }

                        // Blended normal (same as compute shader) — skipped entirely
                        // for cheap-shading LOD tier to avoid 6 extra buffer reads per pixel
                        float3 blendedN;
                        if (_CheapShading != 0 || _UnlitLod != 0)
                        {
                            blendedN = normal;
                        }
                        else if (abs(normal.y) > 0.5)
                        {
                            blendedN = normal;
                        }
                        else
                        {
                            float3 smoothN = SmoothNormal(sampleVoxel, dims, bufferOffset);
                            blendedN = normalize(normal * 0.7 + smoothN * 0.3);
                        }
                        blendedN = normalize(mul(volInvRot, blendedN));

                        float3 shadowedLighting;

                        if (_UnlitLod != 0)
                        {
                            // Ultra-far fast path: skips GetLighting's fill/cam branches AND the
                            // entire shadow-ray setup/loop (divisions, floor, sign, per-step buffer
                            // reads) below. Real ALU + memory-bandwidth savings per hit pixel,
                            // unlike _CheapShading alone which only skips the normal blend.
                            float skyDot = dot(blendedN, _LightDirection) * 0.5 + 0.5;
                            skyDot = skyDot * skyDot;
                            float3 ambient = float3(_AmbientIntensity, _AmbientIntensity, _AmbientIntensity * 1.03);
                            shadowedLighting = ambient + _LightColor * skyDot * _LightIntensity;
                        }
                        else
                        {
                            float3 lighting = GetLighting(blendedN, mat);

                            // Shadow ray (same as compute shader)
                            float3 hitWorldPos = localRo + localRd * currentT;
                            float3 shadowDir = mul(volRot, _LightDirection);
                            float3 localN = normalize(mul(volRot, blendedN));
                            float3 shadowOrigin = hitWorldPos
                                + localN * (voxelSize * _ShadowNormalNudge)
                                + shadowDir * (voxelSize * _ShadowLightNudge);

                            float3 shadowLocal = (shadowOrigin - volOffset) / voxelSize;
                            int3 shadowVoxel = clamp((int3)floor(shadowLocal), int3(0, 0, 0), dims - int3(1, 1, 1));

                            int3 shadowStep = (int3)sign(shadowDir);
                            float3 shadowDelta = abs(voxelSize / shadowDir);

                            float3 shadowSideDist;
                            if (shadowDir.x < 0) shadowSideDist.x = (shadowLocal.x - float(shadowVoxel.x)) * shadowDelta.x;
                            else shadowSideDist.x = (float(shadowVoxel.x + 1) - shadowLocal.x) * shadowDelta.x;
                            if (shadowDir.y < 0) shadowSideDist.y = (shadowLocal.y - float(shadowVoxel.y)) * shadowDelta.y;
                            else shadowSideDist.y = (float(shadowVoxel.y + 1) - shadowLocal.y) * shadowDelta.y;
                            if (shadowDir.z < 0) shadowSideDist.z = (shadowLocal.z - float(shadowVoxel.z)) * shadowDelta.z;
                            else shadowSideDist.z = (float(shadowVoxel.z + 1) - shadowLocal.z) * shadowDelta.z;

                            float shadowFactor = 1.0;
                            int3 sOccBrick = int3(-2, -2, -2); // forces first occupancy test
                            for (int s = 0; s < _ShadowMaxSteps && _ShadowEnabled; s++)
                            {
                                ++iterCount;
                                ++iterShadow;
                                if (!InBounds(shadowVoxel, dims))
                                    break;

                                // Brick skip — shadow rays get the same
                                // all-air jump as the primary march.
                                if (_HasOccupancy != 0)
                                {
                                    int3 sbrick = shadowVoxel >> BRICK_SHIFT;
                                    if (sbrick.x != sOccBrick.x || sbrick.y != sOccBrick.y || sbrick.z != sOccBrick.z)
                                    {
                                        sOccBrick = sbrick;
                                        if (!BrickOccupied(sbrick, brickDims, occWordOffset))
                                        {
                                            // sideDist−delta on each axis = t of that axis's
                                            // last plane crossing; max() = current ray t.
                                            float3 scrossed = shadowSideDist - shadowDelta;
                                            float shadowT = max(0.0, max(max(scrossed.x, scrossed.y), scrossed.z));
                                            float3 posL = shadowOrigin + shadowDir * shadowT;
                                            float3 sExitN;
                                            float tSkip = max(BrickExitT(posL, shadowDir, volOffset, voxelSize, sbrick, sExitN), shadowT) + 0.0005;
                                            float3 npL = (shadowOrigin + shadowDir * tSkip - volOffset) / voxelSize;
                                            shadowVoxel = (int3)floor(npL);
                                            shadowSideDist = tSkip + ResyncSideDist(npL, shadowVoxel, shadowDir, shadowDelta);
                                            continue;
                                        }
                                    }
                                }

                                if (s < _ShadowSkipSteps)
                                {
                                    if (shadowSideDist.x < shadowSideDist.y)
                                    {
                                        if (shadowSideDist.x < shadowSideDist.z)
                                        {
                                            shadowVoxel.x += shadowStep.x;
                                            shadowSideDist.x += shadowDelta.x;
                                        }
                                        else
                                        {
                                            shadowVoxel.z += shadowStep.z;
                                            shadowSideDist.z += shadowDelta.z;
                                        }
                                    }
                                    else
                                    {
                                        if (shadowSideDist.y < shadowSideDist.z)
                                        {
                                            shadowVoxel.y += shadowStep.y;
                                            shadowSideDist.y += shadowDelta.y;
                                        }
                                        else
                                        {
                                            shadowVoxel.z += shadowStep.z;
                                            shadowSideDist.z += shadowDelta.z;
                                        }
                                    }
                                    continue;
                                }

                                uint sPacked = _VoxelData[bufferOffset + VoxelIndex(shadowVoxel, dims)];
                                uint sMat = VxMaterial(sPacked);

                                if (sMat != 0u)
                                {
                                    float3 voxelCenter = volOffset + (float3(shadowVoxel) + 0.5) * voxelSize;
                                    float3 toHit = shadowOrigin - voxelCenter;
                                    float perpDist = length(toHit - shadowDir * dot(toHit, shadowDir));
                                    float softness = saturate(perpDist / (voxelSize * 2.0));
                                    shadowFactor = min(shadowFactor, softness);
                                    if (shadowFactor < 0.05) { shadowFactor = 0.0; break; }
                                }

                                if (shadowSideDist.x < shadowSideDist.y)
                                {
                                    if (shadowSideDist.x < shadowSideDist.z)
                                    {
                                        shadowVoxel.x += shadowStep.x;
                                        shadowSideDist.x += shadowDelta.x;
                                    }
                                    else
                                    {
                                        shadowVoxel.z += shadowStep.z;
                                        shadowSideDist.z += shadowDelta.z;
                                    }
                                }
                                else
                                {
                                    if (shadowSideDist.y < shadowSideDist.z)
                                    {
                                        shadowVoxel.y += shadowStep.y;
                                        shadowSideDist.y += shadowDelta.y;
                                    }
                                    else
                                    {
                                        shadowVoxel.z += shadowStep.z;
                                        shadowSideDist.z += shadowDelta.z;
                                    }
                                }
                            }

                            // Apply shadow factor
                            float skyDot = dot(blendedN, _LightDirection) * 0.5 + 0.5;
                            skyDot = skyDot * skyDot;
                            float3 sunContribution = _LightColor * skyDot * _LightIntensity;
                            float3 nonSunLighting = lighting - sunContribution;
                            shadowedLighting = nonSunLighting + sunContribution * shadowFactor;
                            float ambientFloor = max(0.0, _AmbientIntensity * 0.35);
                            shadowedLighting = max(shadowedLighting, float3(ambientFloor, ambientFloor, ambientFloor * 1.02));
                        }

                        // Fold in glass tint accumulated en route — the pass
                        // writes opaque coverage, so alpha resolves to 1.
                        hitColor = float4(glassAccum + throughput * (baseColor.rgb * shadowedLighting), 1.0);

                        // Debug: solid-tint hit color by LOD tier so tiers are visible at a glance
                        if (_LodDebugEnabled != 0)
                            hitColor = float4(_LodDebugColor.rgb, hitColor.a);

                        // Depth = nearest visible surface (glass face when present)
                        worldHit = ro + rd * (glassLayers > 0 ? glassFirstT : currentT);

                        hit = true;
                        break;
                    }

                    // Advance DDA
                    ++iterVox;
                    AdvanceDDA(voxel, tMax, currentT, normal, stepDir, tDelta);
                }

                if (renderMode == 2)
                {
                    if (!hit)
                    {
                        // Ray exited or stopped at opaque content: emit the
                        // accumulated tint; Blend(SrcAlpha,OneMinusSrcAlpha)
                        // turns this into glassAccum + throughput * dst.
                        if (glassLayers == 0)
                        {
                            if (_IterVis <= 0.0) discard;
                            worldHit = ro + rd * tFar; // marched air still costs — show it
                        }
                        else
                        {
                            hitColor = float4(glassAccum, 1.0 - throughput);
                            worldHit = ro + rd * glassFirstT;
                        }
                    }
                }
                else if (!hit)
                {
                    // Rays that saw only transparent voxels exit the volume —
                    // discard so the city pass content behind stays visible.
                    // _IterVis keeps them: a full-volume air march is the
                    // expensive case this view exists to expose.
                    if (_IterVis <= 0.0) discard;
                    worldHit = ro + rd * tFar;
                }

                // Write depth for compositing
                float4 clip = mul(UNITY_MATRIX_VP, float4(worldHit, 1.0));
                float ndcZ = clip.z / clip.w;

                // _IterVis debug views:
                //   1 = total iteration heat (blue<80 green~80-160 red>160)
                //   2 = channel split: R voxel-DDA steps, G brick jumps, B shadow
                //       steps — green-dominant = skipping, red = crawling cells
                //   3 = miss-waste: heat only on rays that exited with no hit
                //       (pure wasted march); hits render black
                if (_IterVis > 0.5)
                {
                    float h = saturate((float)iterCount / 160.0);
                    float3 heat = h < 0.5
                        ? lerp(float3(0.05, 0.2, 1.0), float3(0.1, 1.0, 0.3), h * 2.0)
                        : lerp(float3(0.1, 1.0, 0.3), float3(1.0, 0.12, 0.0), (h - 0.5) * 2.0);
                    if (_IterVis > 2.5)
                    {
                        o.color = hit ? float4(0, 0, 0, 1) : float4(heat, 1.0);
                    }
                    else if (_IterVis > 1.5)
                    {
                        o.color = float4(
                            saturate((float)iterVox / 128.0),
                            saturate((float)iterSkip / 24.0),
                            saturate((float)iterShadow / 96.0), 1.0);
                    }
                    else
                    {
                        o.color = float4(heat, 1.0);
                    }
                }
                else
                {
                    o.color = hitColor;
                }
                o.depth = ndcZ;
                return o;
            }
        ENDCG

        // Pass 0 — legacy combined path (default; unchanged behavior)
        Pass
        {
            Name "ProxyRaymarch"

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragCombined
            #pragma multi_compile_instancing
            #pragma multi_compile_local BUILDING_INSTANCING
            #pragma exclude_renderers gles gles3

            FragOutput fragCombined(Varyings input)
            {
                return fragCore(input, 0);
            }
            ENDCG
        }

        // Pass 1 — opaque only (composite mode): glass cells treated as air so
        // window openings render real interior geometry for pass 2 to tint.
        Pass
        {
            Name "ProxyRaymarchOpaque"

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragOpaque
            #pragma multi_compile_instancing
            #pragma multi_compile_local BUILDING_INSTANCING
            #pragma exclude_renderers gles gles3

            FragOutput fragOpaque(Varyings input)
            {
                return fragCore(input, 1);
            }
            ENDCG
        }

        // Pass 2 — glass only (composite mode): accumulated pane tint blended
        // over pass 1's pixels. ZWrite Off keeps depth owned by the opaque
        // pass while ZTest still occludes glass hidden behind walls.
        Pass
        {
            Name "ProxyRaymarchGlass"

            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragGlass
            #pragma multi_compile_instancing
            #pragma multi_compile_local BUILDING_INSTANCING
            #pragma exclude_renderers gles gles3

            FragOutput fragGlass(Varyings input)
            {
                return fragCore(input, 2);
            }
            ENDCG
        }
    }
}
