using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SteelCity.Sim
{
    /// <summary>
    /// Spawns HoodAgent instances with ground-probe positioning.
    /// For debug: spawns a single hood in an empty plot so you can immediately
    /// verify rendering and Y level without moving the camera.
    ///
    /// Will eventually replace StressTestSpawner for production multi-hood spawning.
    /// </summary>
    public class HoodSpawner : MonoBehaviour
    {
        [Header("Debug Spawn")]
        [Tooltip("If true, spawns a single hood in an empty plot on Start for quick visual verification.")]
        [SerializeField] private bool debugSpawnOnStart = true;
        [Tooltip("Character asset to spawn.")]
        [SerializeField] private string characterAsset = "animationtest1.stasset";
        [Tooltip("Show ground probe debug rays.")]
        [SerializeField] private bool showGroundProbe = true;

        [Header("References")]
        [SerializeField] private CityMap3D cityMap;
        [SerializeField] private VoxelChunkManager chunkManager;

        [Header("Ground Probe")]
        [Tooltip("Height above expected ground to start probing from.")]
        [SerializeField] private float probeStartHeight = 50f;
        [Tooltip("Maximum downward probe distance.")]
        [SerializeField] private float probeMaxDistance = 100f;
        [Tooltip("Fallback Y if ground probe fails.")]
        [SerializeField] private float fallbackY = 0f;

        private VoxelCollisionWorld collisionWorld;
        private Transform mapRoot;

        void Start()
        {
            if (cityMap == null)
                cityMap = FindFirstObjectByType<CityMap3D>();
            if (chunkManager == null)
                chunkManager = FindFirstObjectByType<VoxelChunkManager>();

            collisionWorld = FindFirstObjectByType<VoxelCollisionWorld>();
            mapRoot = cityMap != null ? cityMap.MapRoot : null;

            if (debugSpawnOnStart)
            {
                // Delay one frame to let CityMap3D.BuildMap finish
                StartCoroutine(DebugSpawnNextFrame());
            }
        }

        private System.Collections.IEnumerator DebugSpawnNextFrame()
        {
            yield return null; // wait one frame
            yield return null; // wait another frame for BuildMap to complete
            SpawnDebugHood();
        }

        /// <summary>
        /// Spawn a single hood in an empty plot for visual debugging.
        /// Picks the first empty_land block from the city layout so the hood
        /// is clearly visible with no buildings obstructing the view.
        /// </summary>
        public void SpawnDebugHood()
        {
            if (cityMap == null || cityMap.CachedBlocks == null || cityMap.CachedBlocks.Count == 0)
            {
                Debug.LogError("[HoodSpawner] No city blocks available — build city first");
                return;
            }

            // Find an empty plot block — look for blocks where the layout has empty_land stassets
            Block debugBlock = FindEmptyPlotBlock();
            if (debugBlock == null)
            {
                // Fallback: use player HQ block
                foreach (var b in cityMap.CachedBlocks.Values)
                {
                    if (b.isPlayerHq) { debugBlock = b; break; }
                }
            }
            if (debugBlock == null)
            {
                // Fallback: first block
                var e = cityMap.CachedBlocks.Values.GetEnumerator();
                e.MoveNext();
                debugBlock = e.Current;
            }

            // Compute world position of block center
            float spacing = cityMap.Spacing;
            var layout = cityMap.CachedLayout;
            float centerRow = 0f, centerCol = 0f;
            if (layout != null && layout.blocks != null)
            {
                int minR = int.MaxValue, maxR = int.MinValue, minC = int.MaxValue, maxC = int.MinValue;
                foreach (var lb in layout.blocks)
                {
                    if (lb.row < minR) minR = lb.row;
                    if (lb.row > maxR) maxR = lb.row;
                    if (lb.col < minC) minC = lb.col;
                    if (lb.col > maxC) maxC = lb.col;
                }
                centerRow = (minR + maxR) * 0.5f;
                centerCol = (minC + maxC) * 0.5f;
            }

            float spawnX = (debugBlock.col - centerCol) * spacing;
            float spawnZ = -(debugBlock.row - centerRow) * spacing;

            // Ground probe: start at Y+50, probe downward
            float groundY = ProbeGroundAt(spawnX, spawnZ);

            // Find or create Characters parent
            var charParent = mapRoot.Find("HoodCharacters");
            if (charParent == null)
            {
                var cp = new GameObject("HoodCharacters");
                cp.transform.SetParent(mapRoot, false);
                charParent = cp.transform;
            }

            // Create character GameObject
            var charObj = new GameObject("Hood_Debug");
            charObj.transform.SetParent(charParent, false);
            var vc = charObj.AddComponent<VoxelCharacter>();
            vc.assetFileName = characterAsset;
            vc.voxelSize = cityMap.CharacterVoxelSize;
            vc.chunkManager = chunkManager;
            vc.collisionWorld = collisionWorld;
            vc.centerPosition = new Vector3(spawnX, groundY, spawnZ);
            vc.useWorldPosition = false;
            vc.showGizmo = true;
            vc.showGroundProbe = showGroundProbe;

            // Add CharacterAnimation to drive GPU animation states
            var anim = charObj.GetComponent<CharacterAnimation>();
            if (anim == null)
                anim = charObj.AddComponent<CharacterAnimation>();
            anim.autoDetectWalking = false; // debug: manual state control

            // Add PedestrianLookAround for idle look-around behavior
            var lookAround = charObj.GetComponent<PedestrianLookAround>();
            if (lookAround == null)
                lookAround = charObj.AddComponent<PedestrianLookAround>();

            spawnedAnim = anim;

            Debug.Log($"[HoodSpawner] 🔧 Debug hood spawned at block {debugBlock.id} ({debugBlock.name}) " +
                      $"world=({spawnX:F2}, {groundY:F3}, {spawnZ:F2}) " +
                      $"[probeStart={probeStartHeight}, groundY={groundY:F3}, fallback={fallbackY}]");
            Debug.Log("[HoodSpawner] 🎬 Animation debug: press 1-9 to cycle states, 0 for Idle");

            // Focus camera on the spawned hood for immediate visibility
            if (cityMap != null)
            {
                Vector3 worldPos = new Vector3(spawnX, groundY, spawnZ) + mapRoot.position;
                cityMap.SetCameraFocus(worldPos);
                cityMap.SetCameraOrthoSize(4f); // zoom in close for inspection
                Debug.Log($"[HoodSpawner] Camera focused on debug hood at {worldPos}");
            }
        }

        private CharacterAnimation spawnedAnim;
        private static readonly CharacterAnimation.AnimState[] debugStates = new[]
        {
            CharacterAnimation.AnimState.Idle,
            CharacterAnimation.AnimState.Walking,
            CharacterAnimation.AnimState.Looking,
            CharacterAnimation.AnimState.AimWalk,
            CharacterAnimation.AnimState.Aiming,
            CharacterAnimation.AnimState.Crouching,
            CharacterAnimation.AnimState.Flinching,
            CharacterAnimation.AnimState.Falling,
            CharacterAnimation.AnimState.Down,
            CharacterAnimation.AnimState.TPose
        };

        void Update()
        {
            if (spawnedAnim == null) return;

            // Debug keys: 1-9 cycle through animation states (Input System package)
            var kb = Keyboard.current;
            if (kb == null) return;

            for (int i = 0; i < debugStates.Length; i++)
            {
                var key = Key.Digit1 + i;
                if (kb[key].wasPressedThisFrame)
                {
                    var state = debugStates[i];
                    spawnedAnim.SetState(state);
                    Debug.Log($"[HoodSpawner] 🎬 Animation state → {state} ({(int)state})");
                }
            }
        }

        /// <summary>
        /// Find a block that has an empty_land stasset — these are empty plots
        /// with no buildings, ideal for debug spawning.
        /// </summary>
        private Block FindEmptyPlotBlock()
        {
            var layout = cityMap.CachedLayout;
            if (layout == null || layout.blocks == null) return null;

            // Build a lookup: block_id → FULLY vacant (every building slot is empty_land).
            // A block with even one real building isn't a clean empty plot for debug purposes.
            var emptyBlocks = new HashSet<string>();
            foreach (var lb in layout.blocks)
            {
                if (lb.buildings == null || lb.buildings.Length == 0) continue;
                bool allEmpty = true;
                foreach (var b in lb.buildings)
                {
                    bool isEmpty = b.stasset != null && b.stasset.Contains("empty_land") && !b.stasset.Contains("tenement");
                    if (!isEmpty) { allEmpty = false; break; }
                }
                if (allEmpty)
                    emptyBlocks.Add(lb.block_id);
            }

            if (emptyBlocks.Count == 0)
            {
                Debug.LogWarning("[HoodSpawner] No fully-vacant empty_land blocks found in city layout — falling back to HQ block");
                return null;
            }

            // Pick the first empty block that exists in cached blocks
            foreach (var blockId in emptyBlocks)
            {
                if (cityMap.CachedBlocks.TryGetValue(blockId, out var block))
                {
                    Debug.Log($"[HoodSpawner] Found empty plot: {blockId} ({block.name}) at r{block.row} c{block.col}");
                    return block;
                }
            }

            return null;
        }

        /// <summary>
        /// Probe ground at a given X/Z position. Starts at probeStartHeight above
        /// the expected ground and probes downward via VoxelCollisionWorld.
        /// Returns the ground surface Y, or fallbackY if probe fails.
        /// </summary>
        private float ProbeGroundAt(float x, float z)
        {
            if (collisionWorld == null || !collisionWorld.IsInitialized)
            {
                Debug.LogWarning("[HoodSpawner] No VoxelCollisionWorld — using fallback Y");
                return fallbackY;
            }

            // Probe from above — mapRoot position is the world origin offset
            Vector3 worldOrigin = mapRoot != null ? mapRoot.position : Vector3.zero;
            Vector3 probeOrigin = new Vector3(
                worldOrigin.x + x,
                worldOrigin.y + probeStartHeight,
                worldOrigin.z + z);

            if (collisionWorld.ProbeGround(probeOrigin, probeMaxDistance, out float groundY, out Vector3 normal))
            {
                // Convert world Y back to local Y (relative to mapRoot)
                float localGroundY = groundY - worldOrigin.y;
                Debug.Log($"[HoodSpawner] Ground probe hit at world Y={groundY:F3} (local Y={localGroundY:F3})");
                return localGroundY;
            }

            Debug.LogWarning($"[HoodSpawner] Ground probe MISS at ({x:F2}, {z:F2}) — using fallback Y={fallbackY}");
            return fallbackY;
        }
    }
}
