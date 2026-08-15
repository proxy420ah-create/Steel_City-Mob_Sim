using System.IO;
using UnityEngine;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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
        [Tooltip("Filename relative to StreamingAssets/voxel_characters/")]
        public string assetFileName = "Civilian1.json";

        [Header("Voxel Grid")]
        [Tooltip("World units per voxel. Buildings use 0.1, characters use 0.02 (Vinny standard).")]
        public float voxelSize = 0.02f;

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

            if (useInstancing && assetFileName.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
            {
                var clothing = gameObject.GetComponent<ClothingSystem>();
                if (clothing == null)
                    clothing = gameObject.AddComponent<ClothingSystem>();
            }
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
            string path = Path.Combine(Application.streamingAssetsPath, "voxel_characters", assetFileName);
            if (!File.Exists(path))
            {
                Debug.LogError($"[VoxelCharacter] Asset not found: {path}");
                return;
            }

            if (assetFileName.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
                voxelData = StAssetReader.LoadVoxelsFromJson(path);
            else
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
        /// alongside the .stasset in StreamingAssets/voxel_characters/.
        /// If no file exists, the shader falls back to hardcoded sin() animation.
        /// </summary>
        void LoadAndApplyAnimParams()
        {
            string jsonText = null;

            if (assetFileName.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
            {
                // Consolidated .character.json — animParams and pivots are in the same file
                string path = Path.Combine(Application.streamingAssetsPath, "voxel_characters", assetFileName);
                if (File.Exists(path))
                    jsonText = File.ReadAllText(path);

                if (jsonText == null)
                {
                    Debug.Log($"[VoxelCharacter] Consolidated JSON not found at {path} — using shader default animation.");
                    return;
                }
            }
            else
            {
                // Legacy path: look for separate {name}.anim.json
                string animFileName = Path.GetFileNameWithoutExtension(assetFileName) + ".anim.json";
                string animPath = Path.Combine(Application.streamingAssetsPath, "voxel_characters", animFileName);

                if (!File.Exists(animPath))
                {
                    Debug.Log($"[VoxelCharacter] No .anim.json found at {animPath} — using shader default animation.");
                    return;
                }

                jsonText = File.ReadAllText(animPath);
            }

            // For consolidated JSON, extract the animParams sub-object and wrap it
            // in the format that AnimParamsJson expects: { format, version, pivots, params: {...} }
            string animJsonText;
            if (assetFileName.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
            {
                string animParamsRaw = CharacterJsonLoader.ExtractAnimParamsRaw(jsonText);
                string pivotsRaw = ExtractPivotsRaw(jsonText);
                if (animParamsRaw == null && pivotsRaw == null)
                {
                    Debug.Log($"[VoxelCharacter] No animParams or pivots in consolidated JSON — using shader default animation.");
                    return;
                }
                // Build a synthetic anim JSON that matches the old .anim.json format
                animJsonText = "{";
                animJsonText += "\"format\":\"anim_params\",\"version\":1,";
                animJsonText += "\"pivots\":" + (pivotsRaw ?? "{}") + ",";
                animJsonText += "\"params\":" + (animParamsRaw ?? "{}");
                animJsonText += "}";
            }
            else
            {
                animJsonText = jsonText;
            }

            var jsonData = JsonUtility.FromJson<AnimParamsJson>(animJsonText);
            if (jsonData == null || jsonData.@params == null)
            {
                Debug.LogWarning($"[VoxelCharacter] Failed to parse anim params — using default animation.");
                return;
            }

            var p = jsonData.@params;
            var wkf = p.walkKeyframes;
            if (wkf == null)
            {
                Debug.LogWarning($"[VoxelCharacter] No walkKeyframes in anim params — using default animation.");
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
            Debug.Log($"[VoxelCharacter] Animation parameters loaded — keyframe walk enabled");

            // Authored per-model pivots — JsonUtility can't parse the int-keyed "pivots" dict,
            // so parse it manually. Without this, the shader falls back to a hardcoded
            // fractional pivot approximation that only matches the original hoodlum proportions.
            // Use animJsonText (synthetic or legacy) so we parse the right section.
            var pivotDict = ParsePivotsManual(animJsonText);
            if (pivotDict.Count > 0)
            {
                // Fallback fractions matching the shader's hardcoded approximation — used for
                // any core limb groupID (1-5) missing from the authored dict, so a partial
                // export doesn't degrade to corner-pivot rotation once pivots are enabled.
                // Forearms/shins (6-9) inherit their parent's pivot via the FK chain and don't
                // need a fallback here.
                var fallback = new Dictionary<int, Vector3>
                {
                    { 1, new Vector3(0.5f, 0.78f, 0.5f) },   // head
                    { 2, new Vector3(0.25f, 0.75f, 0.5f) },  // left arm
                    { 3, new Vector3(0.75f, 0.75f, 0.5f) },  // right arm
                    { 4, new Vector3(0.375f, 0.34f, 0.5f) }, // left leg
                    { 5, new Vector3(0.625f, 0.34f, 0.5f) }, // right leg
                };

                var pivotArray = new Vector4[10];
                for (int i = 0; i < 10; i++)
                {
                    if (pivotDict.TryGetValue(i, out var v))
                        pivotArray[i] = new Vector4(v.x, v.y, v.z, 0);
                    else if (fallback.TryGetValue(i, out var fv))
                        pivotArray[i] = new Vector4(fv.x, fv.y, fv.z, 0);
                }
                chunkManager.SetPivots(assetFileName, pivotArray);
                Debug.Log($"[VoxelCharacter] Authored pivots loaded — {pivotDict.Count} groups");
            }

            // Pack and upload static animation params (looking/aiming/crouching/jointOffset)
            // as 12 float4s for the GPU shader.
            var jointOffsets = ParseJointOffsetsManual(animJsonText);
            var asp = new Vector4[12];
            // [0] = looking params
            var lp = p.looking;
            asp[0] = new Vector4(lp != null ? lp.headYaw : 0.5f, lp != null ? lp.headYawFreq : 2.0f,
                                 lp != null ? lp.headPitch : 0.035f, lp != null ? lp.headPitchFreq : 1.3f);
            // [1] = aiming torso/head
            var ap = p.aiming;
            asp[1] = new Vector4(ap != null ? ap.torsoTwist : 0.2f, ap != null ? ap.headYaw : 0f,
                                 ap != null ? ap.headPitch : -0.05f, ap != null ? ap.headTilt : 0f);
            // [2] = aiming arms/shoulders
            asp[2] = new Vector4(ap != null ? ap.armSwingL : -1.4f, ap != null ? ap.armSwingR : 0f,
                                 ap != null ? ap.shoulderReachL : 0f, ap != null ? ap.shoulderReachR : 0f);
            // [3] = aiming elbows + crouching lower
            var cp = p.crouching;
            asp[3] = new Vector4(ap != null ? ap.elbowBendL : 0.3f, ap != null ? ap.elbowBendR : 0f,
                                 cp != null ? cp.bodyLower : 0f, cp != null ? cp.modelLower : 4f);
            // [4] = crouching lean/head/arms
            asp[4] = new Vector4(cp != null ? cp.bodyLean : 0f, cp != null ? cp.headPitch : 0f,
                                 cp != null ? cp.armSwingL : 0f, cp != null ? cp.armSwingR : 0f);
            // [5] = crouching legs/knees
            asp[5] = new Vector4(cp != null ? cp.legStrideL : -1.15f, cp != null ? cp.legStrideR : 0f,
                                 cp != null ? cp.kneeBendL : 1.15f, cp != null ? cp.kneeBendR : 1.40f);
            // [6] = elbow twist
            var eb = p.elbowBend;
            asp[6] = new Vector4(eb != null ? eb.twistL : 0f, eb != null ? eb.twistR : 0f, 0, 0);
            // [7..11] = jointOffsets for groups 1..5
            for (int i = 0; i < 5; i++)
            {
                int gid = i + 1;
                if (jointOffsets.TryGetValue(gid, out var jo))
                    asp[7 + i] = new Vector4(jo.x, jo.y, jo.z, 0);
                else
                    asp[7 + i] = Vector4.zero;
            }
            chunkManager.SetAnimStaticParams(assetFileName, asp);
            Debug.Log($"[VoxelCharacter] Static anim params packed and uploaded (looking/aiming/crouching/jointOffset)");
        }

        /// <summary>
        /// Extract the "pivots" sub-object as raw JSON string from a consolidated .character.json.
        /// </summary>
        private static string ExtractPivotsRaw(string json)
        {
            int idx = json.IndexOf("\"pivots\"");
            if (idx < 0) return null;
            int start = json.IndexOf('{', idx);
            if (start < 0) return null;

            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') { depth--; if (depth == 0) return json.Substring(start, i - start + 1); }
            }
            return null;
        }

        /// <summary>
        /// Parse pivots from raw JSON using regex — JsonUtility cannot deserialize int-keyed
        /// dictionaries like "pivots": {"0": {"x":..,"y":..,"z":..}, "1": {...}, ...}.
        /// </summary>
        private static Dictionary<int, Vector3> ParsePivotsManual(string jsonText)
        {
            var result = new Dictionary<int, Vector3>();

            int pivotsStart = jsonText.IndexOf("\"pivots\"");
            int paramsStart = jsonText.IndexOf("\"params\"");
            if (pivotsStart < 0 || paramsStart < 0 || paramsStart <= pivotsStart)
                return result;

            string pivotsSection = jsonText.Substring(pivotsStart, paramsStart - pivotsStart);

            var entryPattern = new Regex(@"""(\d+)""\s*:\s*\{\s*""x""\s*:\s*([\-\d.eE+]+)\s*,\s*""y""\s*:\s*([\-\d.eE+]+)\s*,\s*""z""\s*:\s*([\-\d.eE+]+)\s*\}");
            foreach (Match m in entryPattern.Matches(pivotsSection))
            {
                int gid = int.Parse(m.Groups[1].Value);
                float x = float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                float y = float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                float z = float.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
                result[gid] = new Vector3(x, y, z);
            }
            return result;
        }

        // ---- JSON data classes for .anim.json parsing ----
        // The animator exports: { format, version, pivots, params: {...}, states }
        // JsonUtility uses field names matching JSON keys (case-insensitive).
        [System.Serializable]
        public class AnimParamsJson
        {
            public string format;
            public int version;
            public AnimParamsData @params;
        }

        /// <summary>
        /// Parse jointOffset from raw JSON using regex — JsonUtility cannot deserialize
        /// int-keyed dictionaries like "jointOffset": {"1": {"x":0,"y":0,"z":0}, ...}.
        /// </summary>
        private static Dictionary<int, Vector3> ParseJointOffsetsManual(string jsonText)
        {
            var result = new Dictionary<int, Vector3>();
            int joStart = jsonText.IndexOf("\"jointOffset\"");
            if (joStart < 0) return result;
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
            public LookingData looking;
            public AimingData aiming;
            public CrouchingData crouching;
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
