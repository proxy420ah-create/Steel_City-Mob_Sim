using System.IO;
using UnityEngine;
using System.Collections.Generic;

namespace SteelCity.Sim
{
    /// <summary>
    /// Self-contained voxel character component — SteelTide VoxelObject approach.
    /// Place on a GameObject, set the asset filename and voxel size, and it:
    ///   1. Loads the .stasset voxel data
    ///   2. Creates a ComputeBuffer
    ///   3. Registers with VoxelChunkManager for raymarch rendering
    ///   4. Shows a volume box gizmo in Scene view
    ///
    /// The GameObject's transform.position IS the volume origin (corner, not center).
    /// Move the GameObject and the rendered volume follows.
    ///
    /// Extensible for simple skeletal joints (elbows, knees) later via
    /// re-voxelization into an oversized volume (like SteelTide's VoxelActor2Revoxel).
    /// </summary>
    public class VoxelCharacter : MonoBehaviour
    {
        [Header("Asset")]
        [Tooltip("Filename relative to StreamingAssets/voxel_buildings/")]
        public string assetFileName = "character_hoodlum_0.stasset";

        [Header("Voxel Grid")]
        [Tooltip("World units per voxel. Buildings use 0.1, characters typically 0.015-0.05.")]
        public float voxelSize = 0.015f;

        [Header("Rendering")]
        [Tooltip("Auto-find VoxelChunkManager in scene if not assigned.")]
        public VoxelChunkManager chunkManager;
        public bool showGizmo = true;

        [Header("Positioning")]
        [Tooltip("World-space center position for the character volume. Set externally before Start().")]
        public Vector3 centerPosition = Vector3.zero;
        [Tooltip("If true, position is treated as world-space. If false, local-space relative to parent.")]
        public bool useWorldPosition = true;

        [Header("Collision — SteelTide VoxelWorld approach")]
        [Tooltip("Reference to VoxelCollisionWorld for ground probing. Auto-found if not assigned.")]
        public VoxelCollisionWorld collisionWorld;
        [Tooltip("Gravity acceleration in world units/sec².")]
        public float gravity = 9.8f;
        [Tooltip("Probe distance for ground detection (world units below character feet).")]
        public float groundProbeDistance = 2f;
        [Tooltip("Snap distance — if within this of ground, snap instead of applying gravity.")]
        public float snapDistance = 0.05f;
        [Tooltip("Show debug rays for ground probes.")]
        public bool showGroundProbe = false;

        // Voxel data
        private ushort[,,] voxelData;
        private ComputeBuffer voxelBuffer; // only used in non-instanced mode
        private int dimX, dimY, dimZ;
        private bool initialized = false;

        // Registration name (unique per instance, non-instanced mode)
        private string volumeName;

        // Instanced mode handle
        private VoxelChunkManager.InstancedCharacter instancedHandle;

        [Header("Instancing")]
        [Tooltip("If true, uses GPU instancing (shared voxel buffer, 1 draw call for all instances). Requires all instances use the same .stasset.")]
        public bool useInstancing = true;

        // Physics state
        private float verticalVelocity = 0f;
        private bool onGround = false;

        /// <summary>True after asset loaded and registered with renderer.</summary>
        public bool IsInitialized => initialized;

        /// <summary>Access to the instanced render handle (for animation drivers). Null if not using instancing.</summary>
        public VoxelChunkManager.InstancedCharacter GetInstancedHandle() => instancedHandle;

        /// <summary>Voxel dimensions (x, y, z).</summary>
        public (int x, int y, int z) Dims => (dimX, dimY, dimZ);

        /// <summary>World-space size of the volume (dims * voxelSize).</summary>
        public Vector3 WorldSize => new Vector3(dimX, dimY, dimZ) * voxelSize;

        /// <summary>World-space center of the character volume (corner + half size).</summary>
        public Vector3 WorldCenter => transform.position + WorldSize * 0.5f;

        void Start()
        {
            LoadAsset();
            ApplyCenterPosition();

            if (useInstancing)
            {
                RegisterInstancedWithManager();
                LoadAndApplyAnimParams();
            }
            else
            {
                CreateComputeBuffer();
                RegisterWithManager();
            }

            FindCollisionWorld();
            initialized = true;
        }

        void FindCollisionWorld()
        {
            if (collisionWorld == null)
                collisionWorld = FindFirstObjectByType<VoxelCollisionWorld>();

            if (collisionWorld == null)
                Debug.LogWarning("[VoxelCharacter] No VoxelCollisionWorld found — gravity disabled.");
            else
                Debug.Log("[VoxelCharacter] Found VoxelCollisionWorld — gravity enabled.");
        }

        void Update()
        {
            if (!initialized) return;
            ApplyGravity();
        }

        void ApplyGravity()
        {
            if (collisionWorld == null || !collisionWorld.IsInitialized) return;

            // Character feet = bottom-center of the volume
            Vector3 feetPos = transform.position + new Vector3(
                dimX * voxelSize * 0.5f,
                0f,
                dimZ * voxelSize * 0.5f);

            // Probe downward from slightly above feet to find ground
            Vector3 probeOrigin = feetPos + Vector3.up * 0.01f;

            if (showGroundProbe)
            {
                Debug.DrawRay(probeOrigin, Vector3.down * groundProbeDistance, Color.cyan, 0f, false);
            }

            bool hit = collisionWorld.ProbeGround(probeOrigin, groundProbeDistance, out float groundY, out Vector3 normal);

            if (hit)
            {
                float currentFeetY = transform.position.y;
                float distToGround = groundY - currentFeetY;

                if (distToGround <= snapDistance && distToGround >= -snapDistance)
                {
                    // Snap to ground
                    if (!onGround)
                    {
                        Debug.Log($"[VoxelCharacter] Snapped to ground Y={groundY:F3} (was {currentFeetY:F3})");
                    }
                    transform.position = new Vector3(
                        transform.position.x,
                        groundY,
                        transform.position.z);
                    verticalVelocity = 0f;
                    onGround = true;
                }
                else if (distToGround > snapDistance)
                {
                    // Ground is below us but not close enough to snap — fall toward it
                    bool wasOnGround = onGround;
                    onGround = false;
                    verticalVelocity -= gravity * Time.deltaTime;
                    float newY = transform.position.y + verticalVelocity * Time.deltaTime;
                    // Don't fall through ground
                    if (newY < groundY) newY = groundY;
                    transform.position = new Vector3(
                        transform.position.x,
                        newY,
                        transform.position.z);

                    if (newY >= groundY && verticalVelocity < 0)
                    {
                        if (wasOnGround == false)
                            Debug.Log($"[VoxelCharacter] Landed on ground Y={groundY:F3}");
                        verticalVelocity = 0f;
                        onGround = true;
                    }
                }
                else // distToGround < -snapDistance — character is below ground (embedded)
                {
                    // Push up to surface
                    transform.position = new Vector3(
                        transform.position.x,
                        groundY,
                        transform.position.z);
                    verticalVelocity = 0f;
                    onGround = true;
                }
            }
            else
            {
                // No ground found — free fall
                onGround = false;
                verticalVelocity -= gravity * Time.deltaTime;
                transform.position += Vector3.up * verticalVelocity * Time.deltaTime;

                if (showGroundProbe)
                {
                    Debug.Log($"[VoxelCharacter] No ground — falling (vel={verticalVelocity:F2})");
                }
            }
        }

        void ApplyCenterPosition()
        {
            // Offset so the CENTER of the voxel volume sits at centerPosition
            Vector3 cornerOffset = new Vector3(
                dimX * voxelSize * 0.5f,
                0f,
                dimZ * voxelSize * 0.5f);

            if (useWorldPosition)
            {
                transform.position = centerPosition - cornerOffset;
            }
            else
            {
                transform.localPosition = centerPosition - cornerOffset;
            }

            Debug.Log($"[VoxelCharacter] Positioned at corner {transform.position} (center={centerPosition}, offset={cornerOffset})");
        }

        void LoadAsset()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "voxel_buildings", assetFileName);
            if (!File.Exists(path))
            {
                Debug.LogError($"[VoxelCharacter] Asset not found: {path}");
                return;
            }

            voxelData = StAssetReader.LoadVoxels(path);
            if (voxelData == null)
            {
                Debug.LogError($"[VoxelCharacter] Failed to load voxel data from {path}");
                return;
            }

            dimX = voxelData.GetLength(0);
            dimY = voxelData.GetLength(1);
            dimZ = voxelData.GetLength(2);

            Debug.Log($"[VoxelCharacter] Loaded {assetFileName}: {dimX}x{dimY}x{dimZ} = {dimX * dimY * dimZ:N0} voxels (voxelSize={voxelSize})");
        }

        void CreateComputeBuffer()
        {
            if (voxelData == null) return;

            int totalVoxels = dimX * dimY * dimZ;
            var gpuData = new uint[totalVoxels];
            int idx = 0;
            for (int z = 0; z < dimZ; z++)
                for (int y = 0; y < dimY; y++)
                    for (int x = 0; x < dimX; x++)
                        gpuData[idx++] = (uint)voxelData[x, y, z];

            voxelBuffer = new ComputeBuffer(totalVoxels, sizeof(uint));
            voxelBuffer.SetData(gpuData);

            Debug.Log($"[VoxelCharacter] ComputeBuffer created: {totalVoxels:N0} voxels");
        }

        void RegisterWithManager()
        {
            if (voxelBuffer == null) return;

            if (chunkManager == null)
            {
                chunkManager = FindFirstObjectByType<VoxelChunkManager>();
            }

            if (chunkManager == null)
            {
                Debug.LogWarning("[VoxelCharacter] No VoxelChunkManager found in scene! Character will not render.");
                return;
            }

            volumeName = $"char_{GetInstanceID()}";
            chunkManager.RegisterVolume(volumeName, gameObject, voxelBuffer, dimX, dimY, dimZ, voxelSize);

            Debug.Log($"[VoxelCharacter] Registered with VoxelChunkManager as '{volumeName}' at {transform.position}");
        }

        void RegisterInstancedWithManager()
        {
            if (chunkManager == null)
                chunkManager = FindFirstObjectByType<VoxelChunkManager>();

            if (chunkManager == null)
            {
                Debug.LogWarning("[VoxelCharacter] No VoxelChunkManager found in scene! Character will not render.");
                return;
            }

            instancedHandle = chunkManager.RegisterInstancedCharacter(gameObject, assetFileName, voxelSize);
            if (instancedHandle != null)
                Debug.Log($"[VoxelCharacter] Registered as INSTANCED at {transform.position} (shared buffer, 1 draw call for all instances)");
            else
                Debug.LogWarning("[VoxelCharacter] Instanced registration failed — character will not render.");
        }

        /// <summary>
        /// Load animation parameters from a .anim.json file (exported by the HTML animator).
        /// The file must be named {assetFileName without .stasset}.anim.json and placed
        /// alongside the .stasset in StreamingAssets/voxel_buildings/.
        /// If no file exists, the shader falls back to hardcoded sin() animation.
        /// </summary>
        void LoadAndApplyAnimParams()
        {
            // Expected file: character_hoodlum_0.anim.json (next to character_hoodlum_0.stasset)
            string animFileName = Path.GetFileNameWithoutExtension(assetFileName) + ".anim.json";
            string animPath = Path.Combine(Application.streamingAssetsPath, "voxel_buildings", animFileName);

            if (!File.Exists(animPath))
            {
                Debug.Log($"[VoxelCharacter] No .anim.json found at {animPath} — using shader default animation.");
                return;
            }

            string jsonText = File.ReadAllText(animPath);
            var jsonData = JsonUtility.FromJson<AnimParamsJson>(jsonText);
            if (jsonData == null || jsonData.params == null)
            {
                Debug.LogWarning($"[VoxelCharacter] Failed to parse {animPath} — using default animation.");
                return;
            }

            var p = jsonData.params;
            var wkf = p.walkKeyframes;
            if (wkf == null)
            {
                Debug.LogWarning($"[VoxelCharacter] {animFileName} has no walkKeyframes — using default animation.");
                return;
            }

            // Build the 10 float4 walk keyframe buffer.
            // Index: 0=armSwingL, 1=armSwingR, 2=legStrideL, 3=legStrideR,
            //        4=elbowBendL, 5=elbowBendR, 6=kneeBendL, 7=kneeBendR,
            //        8=forearmTwistL, 9=forearmTwistR
            // Each Vector4 = (kf0, kf1, kf2, kf3)
            // When autoMirror is true, kf2 = mirror(kf0), kf3 = mirror(kf1).
            // Mirroring swaps L↔R: armSwingL.kf2 = armSwingR.kf0, etc.
            // When autoMirror is false, kf2/kf3 come from the JSON directly (may be null
            // if the animator didn't author them — fall back to kf0/kf1 in that case).
            bool autoMirror = wkf.autoMirror;
            WalkKFPose kf2 = autoMirror ? wkf.kf0 : (wkf.kf2 ?? wkf.kf0);
            WalkKFPose kf3 = autoMirror ? wkf.kf1 : (wkf.kf3 ?? wkf.kf1);

            var kfs = new Vector4[10];
            // For autoMirror: kf2 value for L = kf0 value for R (L↔R swap)
            kfs[0] = new Vector4(wkf.kf0.armSwingL, wkf.kf1.armSwingL,
                autoMirror ? wkf.kf0.armSwingR : kf2.armSwingL,
                autoMirror ? wkf.kf1.armSwingR : kf3.armSwingL);
            kfs[1] = new Vector4(wkf.kf0.armSwingR, wkf.kf1.armSwingR,
                autoMirror ? wkf.kf0.armSwingL : kf2.armSwingR,
                autoMirror ? wkf.kf1.armSwingL : kf3.armSwingR);
            kfs[2] = new Vector4(wkf.kf0.legStrideL, wkf.kf1.legStrideL,
                autoMirror ? wkf.kf0.legStrideR : kf2.legStrideL,
                autoMirror ? wkf.kf1.legStrideR : kf3.legStrideL);
            kfs[3] = new Vector4(wkf.kf0.legStrideR, wkf.kf1.legStrideR,
                autoMirror ? wkf.kf0.legStrideL : kf2.legStrideR,
                autoMirror ? wkf.kf1.legStrideL : kf3.legStrideR);
            kfs[4] = new Vector4(wkf.kf0.elbowBendL, wkf.kf1.elbowBendL,
                autoMirror ? wkf.kf0.elbowBendR : kf2.elbowBendL,
                autoMirror ? wkf.kf1.elbowBendR : kf3.elbowBendL);
            kfs[5] = new Vector4(wkf.kf0.elbowBendR, wkf.kf1.elbowBendR,
                autoMirror ? wkf.kf0.elbowBendL : kf2.elbowBendR,
                autoMirror ? wkf.kf1.elbowBendL : kf3.elbowBendR);
            kfs[6] = new Vector4(wkf.kf0.kneeBendL, wkf.kf1.kneeBendL,
                autoMirror ? wkf.kf0.kneeBendR : kf2.kneeBendL,
                autoMirror ? wkf.kf1.kneeBendR : kf3.kneeBendL);
            kfs[7] = new Vector4(wkf.kf0.kneeBendR, wkf.kf1.kneeBendR,
                autoMirror ? wkf.kf0.kneeBendL : kf2.kneeBendR,
                autoMirror ? wkf.kf1.kneeBendL : kf3.kneeBendR);
            kfs[8] = new Vector4(wkf.kf0.forearmTwistL, wkf.kf1.forearmTwistL,
                autoMirror ? wkf.kf0.forearmTwistR : kf2.forearmTwistL,
                autoMirror ? wkf.kf1.forearmTwistR : kf3.forearmTwistL);
            kfs[9] = new Vector4(wkf.kf0.forearmTwistR, wkf.kf1.forearmTwistR,
                autoMirror ? wkf.kf0.forearmTwistL : kf2.forearmTwistR,
                autoMirror ? wkf.kf1.forearmTwistL : kf3.forearmTwistR);

            // Build the 7 float4 joint config buffer
            // Null-guard each section for compatibility with older export files
            var jc = new Vector4[7];
            jc[0] = p.armSwing != null
                ? new Vector4(p.armSwing.axisL, p.armSwing.axisR, p.armSwing.signL, p.armSwing.signR)
                : new Vector4(0, 0, 1, 1);
            jc[1] = p.legStride != null
                ? new Vector4(p.legStride.axisL, p.legStride.axisR, p.legStride.signL, p.legStride.signR)
                : new Vector4(0, 0, 1, 1);
            jc[2] = p.elbowBend != null
                ? new Vector4(p.elbowBend.axisL, p.elbowBend.axisR, p.elbowBend.signL, p.elbowBend.signR)
                : new Vector4(1, 1, 1, -1);
            jc[3] = p.kneeBend != null
                ? new Vector4(p.kneeBend.axisL, p.kneeBend.axisR, p.kneeBend.signL, p.kneeBend.signR)
                : new Vector4(0, 0, 1, 1);
            jc[4] = p.legTwist != null
                ? new Vector4(p.legTwist.leftRest, p.legTwist.rightRest, 0, 0)
                : new Vector4(0, 0, 0, 0);
            jc[5] = new Vector4(
                p.restPose != null ? p.restPose.leftArmZ : -1.5708f,
                p.restPose != null ? p.restPose.rightArmZ : 1.5708f,
                p.elbowBend != null ? p.elbowBend.leftRest : 0f,
                p.elbowBend != null ? p.elbowBend.rightRest : 0f);
            jc[6] = p.kneeBend != null
                ? new Vector4(p.kneeBend.leftRest, p.kneeBend.rightRest, 0, 0)
                : new Vector4(0, 0, 0, 0);

            // Walk config: (cycleDuration, bodyBobAmp, weightShiftAmp, autoMirror)
            float bobAmp = wkf.bodyBob != null ? wkf.bodyBob.amplitude : 0f;
            float shiftAmp = wkf.weightShift != null ? wkf.weightShift.amplitude : 0f;
            var walkConfig = new Vector4(wkf.cycleDuration, bobAmp, shiftAmp, autoMirror ? 1f : 0f);

            chunkManager.SetWalkKeyframes(assetFileName, kfs, jc, walkConfig);
            Debug.Log($"[VoxelCharacter] Animation parameters loaded from {animFileName} — keyframe walk enabled");
        }

        // ---- JSON data classes for .anim.json parsing ----
        // The animator exports: { format, version, pivots, params: {...}, states }
        // JsonUtility uses field names matching JSON keys (case-insensitive).
        [System.Serializable]
        public class AnimParamsJson
        {
            public string format;
            public int version;
            public AnimParamsData params;
        }

        [System.Serializable]
        public class AnimParamsData
        {
            public RestPoseData restPose;
            public WalkKeyframesData walkKeyframes;
            public ArmSwingData armSwing;
            public LegStrideData legStride;
            public ElbowBendData elbowBend;
            public KneeBendData kneeBend;
            public LegTwistData legTwist;
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
        }

        [System.Serializable]
        public class LegTwistData
        {
            public float leftRest;
            public float rightRest;
        }

        // BoxCollider removed — collision is handled by VoxelCollisionWorld probing,
        // same as SteelTide's VoxelActor2Ground using VoxelWorld.RaymarchChunk().

        /// <summary>
        /// Move the character to a world position. The position is the CENTER of the volume
        /// (not the corner) — we offset internally so transform.position stays at the corner
        /// which is what the raymarcher expects.
        /// </summary>
        public void PlaceAtCenter(Vector3 worldCenter)
        {
            Vector3 corner = worldCenter - new Vector3(
                dimX * voxelSize * 0.5f,
                0f,
                dimZ * voxelSize * 0.5f);
            transform.position = corner;
        }

        void OnDestroy()
        {
            if (useInstancing && instancedHandle != null)
            {
                chunkManager?.UnregisterInstancedCharacter(instancedHandle);
                instancedHandle = null;
            }
            else if (chunkManager != null && !string.IsNullOrEmpty(volumeName))
            {
                chunkManager.UnregisterVolume(volumeName);
            }

            if (voxelBuffer != null)
            {
                voxelBuffer.Release();
                voxelBuffer = null;
            }
        }

        void OnDrawGizmos()
        {
            if (!showGizmo) return;

            Vector3 size = new Vector3(
                dimX > 0 ? dimX * voxelSize : 0.5f,
                dimY > 0 ? dimY * voxelSize : 1f,
                dimZ > 0 ? dimZ * voxelSize : 0.5f);

            Vector3 center = transform.position + size * 0.5f;

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f); // Orange for characters
            Gizmos.DrawWireCube(center, size);

            // Corner marker
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, voxelSize * 2f);
        }
    }
}
