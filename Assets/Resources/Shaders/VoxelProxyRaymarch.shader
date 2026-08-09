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

        Pass
        {
            Name "ProxyRaymarch"

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local BUILDING_INSTANCING
            #pragma exclude_renderers gles gles3

            #include "UnityCG.cginc"

            // ---- Bindings (same as compute shader) ----
            StructuredBuffer<uint>   _VoxelData;
            StructuredBuffer<float4> _MaterialColors;
            StructuredBuffer<float4> _ChunkTints;

            int    _MaterialCount;
            float3 _VolumeDims;
            float  _VoxelSize;
            float3 _VolumeOffset;
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

            // Compute per-group rotation for a given animation state.
            // Returns false if the state has no transform for this group (idle/unhandled).
            // pivot and rot are outputs. pivot is in world units (voxelSize already applied).
            bool ComputeGroupRotation(
                uint groupID, float3 dims, float voxelSize,
                float animState, float animTime, float animSpeed,
                out float3 pivot, out float3x3 rot)
            {
                float3 headPivot   = float3(dims.x * 0.5, dims.y * 0.78, dims.z * 0.5);
                float3 lArmPivot   = float3(dims.x * 0.25, dims.y * 0.75, dims.z * 0.5);
                float3 rArmPivot   = float3(dims.x * 0.75, dims.y * 0.75, dims.z * 0.5);
                float3 lLegPivot   = float3(dims.x * 0.375, dims.y * 0.34, dims.z * 0.5);
                float3 rLegPivot   = float3(dims.x * 0.625, dims.y * 0.34, dims.z * 0.5);
                float PI = 3.14159265;
                pivot = float3(0, 0, 0);
                rot = float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);

                if (groupID == 1u) // Head
                {
                    pivot = headPivot * voxelSize;
                    float headYaw = 0.0, headPitch = 0.0;
                    if (animState > 1.5 && animState < 3.5) { // Looking or Checking
                        headYaw = sin(animTime * 2.0) * 0.5;
                        headPitch = sin(animTime * 1.3) * 0.1;
                    } else if (animState > 3.5 && animState < 4.5) { // Aiming
                        headYaw = 0.3; headPitch = -0.1;
                    } else if (animState > 5.5 && animState < 6.5) { // Crouching
                        headPitch = 0.2;
                    } else if (animState > 6.5 && animState < 7.5) { // Flinching
                        headPitch = 0.4;
                    } else return false;
                    rot = mul(RotationY(headYaw), RotationX(headPitch));
                    return true;
                }
                else if (groupID == 2u) // Left arm
                {
                    pivot = lArmPivot * voxelSize;
                    float swing = 0.0;
                    if (animState > 0.5 && animState < 1.5) // Walking
                        swing = sin(animTime * 6.0 * animSpeed) * 0.3;
                    else if (animState > 3.5 && animState < 4.5) swing = -1.2; // Aiming
                    else if (animState > 5.5 && animState < 6.5) swing = 0.3;  // Crouching
                    else if (animState > 6.5 && animState < 7.5) swing = -1.5; // Flinching
                    else return false;
                    rot = RotationX(swing);
                    return true;
                }
                else if (groupID == 3u) // Right arm
                {
                    pivot = rArmPivot * voxelSize;
                    float swing = 0.0;
                    if (animState > 0.5 && animState < 1.5) // Walking
                        swing = sin(animTime * 6.0 * animSpeed + PI) * 0.3;
                    else if (animState > 3.5 && animState < 4.5) swing = -1.2; // Aiming
                    else if (animState > 5.5 && animState < 6.5) swing = -0.3; // Crouching
                    else if (animState > 6.5 && animState < 7.5) swing = -1.5; // Flinching
                    else return false;
                    rot = RotationX(swing);
                    return true;
                }
                else if (groupID == 4u) // Left leg
                {
                    pivot = lLegPivot * voxelSize;
                    float stride = 0.0;
                    if (animState > 0.5 && animState < 1.5) // Walking
                        stride = sin(animTime * 6.0 * animSpeed + PI) * 0.4;
                    else if (animState > 5.5 && animState < 6.5) stride = 0.6;  // Crouching
                    else if (animState > 7.5 && animState < 8.5) stride = -0.5; // Falling
                    else return false;
                    rot = RotationX(stride);
                    return true;
                }
                else if (groupID == 5u) // Right leg
                {
                    pivot = rLegPivot * voxelSize;
                    float stride = 0.0;
                    if (animState > 0.5 && animState < 1.5) // Walking
                        stride = sin(animTime * 6.0 * animSpeed) * 0.4;
                    else if (animState > 5.5 && animState < 6.5) stride = 0.6;  // Crouching
                    else if (animState > 7.5 && animState < 8.5) stride = 0.5;  // Falling
                    else return false;
                    rot = RotationX(stride);
                    return true;
                }
                return false;
            }

            // Forward transform: restPos → posedPos offset
            float3 GroupTransformOffset(
                uint groupID, float3 voxelLocalPos, float3 dims, float voxelSize,
                float animState, float animTime, float animSpeed)
            {
                if (groupID == 0u) return float3(0, 0, 0);
                float3 pivot; float3x3 rot;
                if (!ComputeGroupRotation(groupID, dims, voxelSize, animState, animTime, animSpeed, pivot, rot))
                    return float3(0, 0, 0);
                float3 relPos = voxelLocalPos - pivot;
                float3 transformedPos = mul(rot, relPos) + pivot;
                return transformedPos - voxelLocalPos;
            }

            // Inverse transform: posedPos → restPos offset
            // Used in the DDA loop to sample voxel data at rest positions while stepping through posed space.
            // For rotation matrices, inverse = transpose.
            float3 InverseGroupTransformOffset(
                uint groupID, float3 voxelLocalPos, float3 dims, float voxelSize,
                float animState, float animTime, float animSpeed)
            {
                if (groupID == 0u) return float3(0, 0, 0);
                float3 pivot; float3x3 rot;
                if (!ComputeGroupRotation(groupID, dims, voxelSize, animState, animTime, animSpeed, pivot, rot))
                    return float3(0, 0, 0);
                float3 relPos = voxelLocalPos - pivot;
                float3 restPos = mul(transpose(rot), relPos) + pivot;
                return restPos - voxelLocalPos;
            }

            // ---- Lighting (ported from compute shader) ----
            float3 GetLighting(float3 normal, uint mat)
            {
                if (mat == 113u || mat == 115u || mat == 116u || mat == 117u || mat == 124u)
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
                    output.yaw = 0.0;
                    output.instMeta = meta; // (bufferOffset, dimsX, dimsY, dimsZ)
                    output.voxelSize = pos.w;
                    output.animState = 0.0;
                    output.animTime = 0.0;
                    output.animSpeed = 1.0;
                #else
                    float4 instData = _InstanceOffsets[unity_InstanceID];
                    output.volumeOffset = instData.xyz;
                    output.yaw = instData.w;
                    output.instMeta = float4(0.0, _VolumeDims.x, _VolumeDims.y, _VolumeDims.z);
                    output.voxelSize = _VoxelSize;
                    // Read animation data from second half of instance buffer
                    float4 animData = _InstanceOffsets[unity_InstanceID + _InstanceCount];
                    output.animState = animData.x;
                    output.animTime = animData.y;
                    output.animSpeed = animData.z;
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
            FragOutput frag(Varyings input)
            {
                FragOutput o;
                UNITY_SETUP_INSTANCE_ID(input);
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

                // --- Per-instance volume data (instancing) or uniforms (non-instanced) ---
                uint bufferOffset = 0;
                int3 dims;

            #ifdef UNITY_INSTANCING_ENABLED
                float3 volOffset = input.volumeOffset;
                #if defined(BUILDING_INSTANCING)
                    bufferOffset = (uint)input.instMeta.x;
                    dims = int3((int)input.instMeta.y, (int)input.instMeta.z, (int)input.instMeta.w);
                    // Buildings are axis-aligned (no rotation in sector baking)
                    float3x3 volRot = float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
                    float3x3 volInvRot = float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
                #else
                    float yaw = input.yaw;
                    float yc = cos(yaw), ys = sin(yaw);
                    float3x3 volRot = float3x3(yc, 0, -ys, 0, 1, 0, ys, 0, yc);
                    float3x3 volInvRot = float3x3(yc, 0, ys, 0, 1, 0, -ys, 0, yc);
                    dims = VolumeDimsInt();
                #endif
            #else
                float3 volOffset = _VolumeOffset;
                float3x3 volRot = (float3x3)_VolumeRotation;
                float3x3 volInvRot = (float3x3)_VolumeInvRotation;
                dims = VolumeDimsInt();
            #endif

                // Transform ray into volume local space (handles rotation)
                float3 volumeCenter = volOffset + float3(dims) * voxelSize * 0.5;
                float3 localRo = mul(volRot, ro - volumeCenter) + volumeCenter;
                float3 localRd = mul(volRot, rd);

                // Volume bounds
                float3 volumeMin = volOffset;
                float3 volumeMax = volOffset + float3(dims) * voxelSize;

                // Ray-box intersection
                float tNear, tFar;
                if (!RayAABB(localRo, localRd, volumeMin, volumeMax, tNear, tFar))
                {
                    discard;
                }

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

                [loop]
                for (int i = 0; i < _MaxSteps; ++i)
                {
                    if (!InBounds(voxel, dims))
                        break;

                    // Inverse-transform sampling: when groupIDs are enabled, sample voxel data
                    // at the REST position (inverse of the group transform). This makes the ray
                    // "see" voxels at their posed positions, producing visible limb movement.
                    int3 sampleVoxel = voxel;
                    if (_GroupIDsEnabled != 0)
                    {
                        uint gid = _GroupIDs[bufferOffset + VoxelIndex(voxel, dims)];
                        if (gid > 0u)
                        {
                            float3 voxelLocalPos = (float3(voxel) + 0.5) * voxelSize;
                            float3 restOffset = InverseGroupTransformOffset(
                                gid, voxelLocalPos, float3(dims), voxelSize,
                                input.animState, input.animTime, input.animSpeed);
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
                            [loop]
                            for (int s = 0; s < _ShadowMaxSteps && _ShadowEnabled; s++)
                            {
                                if (!InBounds(shadowVoxel, dims))
                                    break;

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

                        hitColor = float4(baseColor.rgb * shadowedLighting, baseColor.a);

                        // Debug: solid-tint hit color by LOD tier so tiers are visible at a glance
                        if (_LodDebugEnabled != 0)
                            hitColor = float4(_LodDebugColor.rgb, hitColor.a);

                        worldHit = ro + rd * currentT;

                        hit = true;
                        break;
                    }

                    // Advance DDA
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

                if (!hit)
                {
                    discard;
                }

                // Write depth for compositing
                float4 clip = mul(UNITY_MATRIX_VP, float4(worldHit, 1.0));
                float ndcZ = clip.z / clip.w;

                o.color = hitColor;
                o.depth = ndcZ;
                return o;
            }
            ENDCG
        }
    }
}
