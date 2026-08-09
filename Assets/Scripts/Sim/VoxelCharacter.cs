using System.IO;
using UnityEngine;

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
