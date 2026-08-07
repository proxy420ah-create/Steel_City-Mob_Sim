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
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos   : TEXCOORD0;
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
            float3 SmoothNormal(int3 v, int3 dims)
            {
                float cx = 0, cy = 0, cz = 0;
                int3 nxp = v + int3(1, 0, 0);  int3 nxm = v + int3(-1, 0, 0);
                int3 nyp = v + int3(0, 1, 0);  int3 nym = v + int3(0, -1, 0);
                int3 nzp = v + int3(0, 0, 1);  int3 nzm = v + int3(0, 0, -1);

                if (InBounds(nxp, dims)) cx += (VxMaterial(_VoxelData[VoxelIndex(nxp, dims)]) != 0u) ? 1.0 : 0.0;
                if (InBounds(nxm, dims)) cx -= (VxMaterial(_VoxelData[VoxelIndex(nxm, dims)]) != 0u) ? 1.0 : 0.0;
                if (InBounds(nyp, dims)) cy += (VxMaterial(_VoxelData[VoxelIndex(nyp, dims)]) != 0u) ? 1.0 : 0.0;
                if (InBounds(nym, dims)) cy -= (VxMaterial(_VoxelData[VoxelIndex(nym, dims)]) != 0u) ? 1.0 : 0.0;
                if (InBounds(nzp, dims)) cz += (VxMaterial(_VoxelData[VoxelIndex(nzp, dims)]) != 0u) ? 1.0 : 0.0;
                if (InBounds(nzm, dims)) cz -= (VxMaterial(_VoxelData[VoxelIndex(nzm, dims)]) != 0u) ? 1.0 : 0.0;

                float3 n = normalize(float3(-cx, -cy, -cz));
                if (length(n) < 0.001) n = float3(0, 1, 0);
                return n;
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
                float4 worldPos = mul(unity_ObjectToWorld, float4(input.positionOS, 1.0));
                output.worldPos = worldPos.xyz;
                output.positionCS = mul(UNITY_MATRIX_VP, worldPos);
                return output;
            }

            // ---- Fragment shader ----
            FragOutput frag(Varyings input)
            {
                FragOutput o;

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

                // Transform ray into volume local space (handles rotation)
                float3 volumeCenter = _VolumeOffset + float3(_VolumeDims) * _VoxelSize * 0.5;
                float3 localRo = mul((float3x3)_VolumeRotation, ro - volumeCenter) + volumeCenter;
                float3 localRd = mul((float3x3)_VolumeRotation, rd);

                // Volume bounds
                float3 volumeMin = _VolumeOffset;
                float3 volumeMax = _VolumeOffset + float3(_VolumeDims) * _VoxelSize;

                // Ray-box intersection
                float tNear, tFar;
                if (!RayAABB(localRo, localRd, volumeMin, volumeMax, tNear, tFar))
                {
                    discard;
                }

                // DDA setup
                float tStart = max(tNear, 0.0);
                float3 startPos = localRo + localRd * (tStart + 0.001);

                int3 dims = VolumeDimsInt();
                float3 localStart = (startPos - _VolumeOffset) / _VoxelSize;
                int3 voxel = clamp((int3)floor(localStart), int3(0, 0, 0), dims - int3(1, 1, 1));

                int3 stepDir = (int3)sign(localRd);
                float3 deltaDist = abs(_VoxelSize / localRd);

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

                    uint packed = _VoxelData[VoxelIndex(voxel, dims)];
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
                            float3 smoothN = SmoothNormal(voxel, dims);
                            blendedN = normalize(normal * 0.7 + smoothN * 0.3);
                        }
                        blendedN = normalize(mul((float3x3)_VolumeInvRotation, blendedN));

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
                            float3 shadowDir = mul((float3x3)_VolumeRotation, _LightDirection);
                            float3 localN = normalize(mul((float3x3)_VolumeRotation, blendedN));
                            float3 shadowOrigin = hitWorldPos
                                + localN * (_VoxelSize * _ShadowNormalNudge)
                                + shadowDir * (_VoxelSize * _ShadowLightNudge);

                            float3 shadowLocal = (shadowOrigin - _VolumeOffset) / _VoxelSize;
                            int3 shadowVoxel = clamp((int3)floor(shadowLocal), int3(0, 0, 0), dims - int3(1, 1, 1));

                            int3 shadowStep = (int3)sign(shadowDir);
                            float3 shadowDelta = abs(_VoxelSize / shadowDir);

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

                                uint sPacked = _VoxelData[VoxelIndex(shadowVoxel, dims)];
                                uint sMat = VxMaterial(sPacked);

                                if (sMat != 0u)
                                {
                                    float3 voxelCenter = _VolumeOffset + (float3(shadowVoxel) + 0.5) * _VoxelSize;
                                    float3 toHit = shadowOrigin - voxelCenter;
                                    float perpDist = length(toHit - shadowDir * dot(toHit, shadowDir));
                                    float softness = saturate(perpDist / (_VoxelSize * 2.0));
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
