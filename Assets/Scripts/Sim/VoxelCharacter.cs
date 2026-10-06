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
        [Tooltip("Filename relative to StreamingAssets/voxel_characters/")]
        public string assetFileName = "Civilian1.json";

        [Header("Voxel Grid")]
        [Tooltip("World units per voxel. Buildings use 0.1, characters use 0.01 (upscaled 96³ standard).")]
        public float voxelSize = 0.01f;

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

        // Shared parsed asset (JSON path) — see CharacterAssets registry /
        // docs/systems/CHARACTER_ASSET_LIFECYCLE.md. Null for .stasset assets.
        private CharacterAsset asset;

        // Ready notification — subscribers (WeaponMount etc.) attach via WhenReady
        // instead of polling IsInitialized in a coroutine (race-free ordering).
        private System.Action<VoxelCharacter> readyHandlers;

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

        /// <summary>The shared parsed asset for this character (null for .stasset assets).</summary>
        public CharacterAsset Asset => asset;

        /// <summary>
        /// Subscribe for the moment this character is fully initialized (asset loaded,
        /// registered, anim params uploaded, clothing attached). Fires IMMEDIATELY if
        /// already initialized — safe to call from any Start/awake regardless of order.
        /// </summary>
        public void WhenReady(System.Action<VoxelCharacter> cb)
        {
            if (cb == null) return;
            if (initialized) cb(this);
            else readyHandlers += cb;
        }

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
                // Anim params are optional — a parse failure must never abort Start
                // (it previously skipped FindCollisionWorld/initialized/ClothingSystem).
                try { LoadAndApplyAnimParams(); }
                catch (System.Exception e) { Debug.LogError($"[VoxelCharacter] Anim params failed for {assetFileName} — continuing with defaults: {e.Message}"); }
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

            // Flush ready queue LAST — every subsystem above is guaranteed done.
            readyHandlers?.Invoke(this);
            readyHandlers = null;
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
            if (assetFileName.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
            {
                // Single shared parse via the registry — geometry, groups, regions,
                // pivots, attach points, and defaults-filled anim params all come
                // from the same CharacterAsset (see CHARACTER_ASSET_LIFECYCLE.md).
                asset = CharacterAssets.Get(assetFileName);
                if (asset == null)
                {
                    Debug.LogError($"[VoxelCharacter] Failed to load asset {assetFileName}");
                    return;
                }
                voxelData = asset.Voxels;
            }
            else
            {
                string path = Path.Combine(Application.streamingAssetsPath, "voxel_characters", assetFileName);
                if (!File.Exists(path))
                {
                    Debug.LogError($"[VoxelCharacter] Asset not found: {path}");
                    return;
                }
                voxelData = StAssetReader.LoadVoxels(path);
            }

            if (voxelData == null)
            {
                Debug.LogError($"[VoxelCharacter] Failed to load voxel data from {assetFileName}");
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
        /// Uploads animation data to the GPU. Two source paths produce the SAME
        /// defaults-filled table (VoxelCharacterAnimator.AnimParamsData), then emit
        /// identical upload arrays — one default source for CPU weld and GPU pose
        /// (see docs/systems/CHARACTER_ASSET_LIFECYCLE.md, gotcha G4).
        ///   - Consolidated .character.json: the shared CharacterAsset (single parse).
        ///   - Legacy .stasset: {name}.anim.json parsed through LoadFromAnimJson.
        /// </summary>
        void LoadAndApplyAnimParams()
        {
            VoxelCharacterAnimator.AnimParamsData p;
            Dictionary<int, Vector3> pivots;
            Dictionary<int, Vector3> jointOffsets;

            if (asset != null)
            {
                // Per-asset GPU uploads — first registered instance wins, the rest
                // share its buffers (SetPivots/SetWalkKeyframes key on assetFileName).
                if (chunkManager == null) return;
                if (chunkManager.HasAnimUploads(assetFileName)) return; // live group already has them
                p = asset.ParamsData;
                pivots = asset.Pivots;
                jointOffsets = asset.JointOffsets;
            }
            else
            {
                // Legacy path: separate {name}.anim.json next to the .stasset.
                string animFileName = Path.GetFileNameWithoutExtension(assetFileName) + ".anim.json";
                string animPath = Path.Combine(Application.streamingAssetsPath, "voxel_characters", animFileName);
                if (!File.Exists(animPath))
                {
                    Debug.Log($"[VoxelCharacter] No .anim.json found at {animPath} — using shader default animation.");
                    return;
                }
                var anim = VoxelCharacterAnimator.LoadFromAnimJson(File.ReadAllText(animPath));
                if (anim == null || anim.paramsData == null)
                {
                    Debug.LogWarning($"[VoxelCharacter] Failed to parse anim params — using default animation.");
                    return;
                }
                p = anim.paramsData;
                pivots = anim.pivots;
                jointOffsets = anim.jointOffsets;
            }

            // ---- Pivots — ALWAYS upload before anything optional (trimmed anim
            // params must never starve joint geometry — the "separated arms" bug).
            if (pivots != null && pivots.Count > 0)
            {
                var pivotArray = new Vector4[10];
                for (int i = 0; i < 10; i++)
                {
                    // Canonical fallback fills any gid the authored+painted data
                    // lacks — identical joints on CPU weld and GPU pose.
                    if (!pivots.TryGetValue(i, out var pv) &&
                        !CharacterAsset.CanonicalPivotFallback.TryGetValue(i, out pv))
                        continue;
                    pivotArray[i] = new Vector4(pv.x, pv.y, pv.z, 0);
                }
                chunkManager.SetPivots(assetFileName, pivotArray);
                Debug.Log($"[VoxelCharacter] Authored pivots uploaded — {pivots.Count} groups");
            }

            if (p == null)
            {
                Debug.LogWarning($"[VoxelCharacter] No anim params table — shader default pose.");
                return;
            }

            var wkf = p.walkKeyframes;
            // ParamsData is defaults-filled — wkf is only null if the table came
            // from a non-standard source; guard so pivots/statics still upload.
            if (wkf == null || wkf.kf0 == null || wkf.kf1 == null)
            {
                Debug.LogWarning($"[VoxelCharacter] No/incomplete walkKeyframes — using default walk animation.");
            }
            else
            {

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
            VoxelCharacterAnimator.WalkKFPose kf2 = autoMirror ? wkf.kf0 : (wkf.kf2 ?? wkf.kf0);
            VoxelCharacterAnimator.WalkKFPose kf3 = autoMirror ? wkf.kf1 : (wkf.kf3 ?? wkf.kf1);

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

            // Build the 7 float4 joint config buffer — emitted directly from the
            // shared defaults-filled ParamsData. No local fallback constants:
            // they were a second default table that diverged from the CPU
            // animator's (swapped aim arms, mirrored legStride/torsoTwist signs —
            // the "weapon on the wrong hand" class of bug). Missing sections
            // cannot occur after LoadFromAnimJson's default fill.
            var jc = new Vector4[7];
            jc[0] = new Vector4(p.armSwing.axisL, p.armSwing.axisR, p.armSwing.signL, p.armSwing.signR);
            jc[1] = new Vector4(p.legStride.axisL, p.legStride.axisR, p.legStride.signL, p.legStride.signR);
            jc[2] = new Vector4(p.elbowBend.axisL, p.elbowBend.axisR, p.elbowBend.signL, p.elbowBend.signR);
            jc[3] = new Vector4(p.kneeBend.axisL, p.kneeBend.axisR, p.kneeBend.signL, p.kneeBend.signR);
            jc[4] = new Vector4(p.legTwist.leftRest, p.legTwist.rightRest, 0, 0);
            jc[5] = new Vector4(p.restPose.leftArmZ, p.restPose.rightArmZ,
                                p.elbowBend.leftRest, p.elbowBend.rightRest);
            jc[6] = new Vector4(p.kneeBend.leftRest, p.kneeBend.rightRest, 0, 0);

            // Walk config: (cycleDuration, bodyBobAmp, weightShiftAmp, autoMirror)
            float bobAmp = wkf.bodyBob != null ? wkf.bodyBob.amplitude : 0f;
            float shiftAmp = wkf.weightShift != null ? wkf.weightShift.amplitude : 0f;
            var walkConfig = new Vector4(wkf.cycleDuration, bobAmp, shiftAmp, autoMirror ? 1f : 0f);

            chunkManager.SetWalkKeyframes(assetFileName, kfs, jc, walkConfig);
            Debug.Log($"[VoxelCharacter] GPU params {assetFileName}: restPose L/R=({p.restPose.leftArmZ:F2},{p.restPose.rightArmZ:F2}) " +
                      $"armSign=({p.armSwing.signL},{p.armSwing.signR}) legSign=({p.legStride.signL},{p.legStride.signR}) " +
                      $"elbowSign=({p.elbowBend.signL},{p.elbowBend.signR}) aimArmL/R=({p.aiming.armSwingL},{p.aiming.armSwingR})");
            Debug.Log($"[VoxelCharacter] Animation parameters loaded — keyframe walk enabled");
            }

            // Pack and upload static animation params (looking/aiming/crouching/jointOffset/aimSweep)
            // as 13 float4s for the GPU shader — same ParamsData source as above.
            var asp = new Vector4[13];
            // [0] = looking params
            var lp = p.looking;
            asp[0] = new Vector4(lp.headYaw, lp.headYawFreq, lp.headPitch, lp.headPitchFreq);
            // [1] = aiming torso/head
            var ap = p.aiming;
            asp[1] = new Vector4(ap.torsoTwist, ap.headYaw, ap.headPitch, ap.headTilt);
            // [2] = aiming arms/shoulders
            asp[2] = new Vector4(ap.armSwingL, ap.armSwingR, ap.shoulderReachL, ap.shoulderReachR);
            // [3] = aiming elbows + crouching lower
            var cp = p.crouching;
            asp[3] = new Vector4(ap.elbowBendL, ap.elbowBendR, cp.bodyLower, cp.modelLower);
            // [4] = crouching lean/head/arms
            asp[4] = new Vector4(cp.bodyLean, cp.headPitch, cp.armSwingL, cp.armSwingR);
            // [5] = crouching legs/knees
            asp[5] = new Vector4(cp.legStrideL, cp.legStrideR, cp.kneeBendL, cp.kneeBendR);
            // [6] = elbow twist
            var eb = p.elbowBend;
            asp[6] = new Vector4(eb.twistL, eb.twistR, 0, 0);
            // [7..11] = jointOffsets for groups 1..5
            for (int i = 0; i < 5; i++)
            {
                int gid = i + 1;
                asp[7 + i] = jointOffsets != null && jointOffsets.TryGetValue(gid, out var jo)
                    ? new Vector4(jo.x, jo.y, jo.z, 0)
                    : Vector4.zero;
            }
            // [12] = aim sweep (state 10): amplitude, frequency, headFollow
            var sw = p.aimSweep;
            asp[12] = new Vector4(sw.amp, sw.freq, sw.headFollow, 0);
            chunkManager.SetAnimStaticParams(assetFileName, asp);
            Debug.Log($"[VoxelCharacter] Static anim params packed and uploaded (looking/aiming/crouching/jointOffset)");
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
