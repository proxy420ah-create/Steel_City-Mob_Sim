using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Clothing test spawner — creates 2 instanced VoxelCharacter instances
    /// using the same .json asset and dresses them in different outfits
    /// to verify per-instance material remapping via the GPU compute shader.
    ///
    /// Both characters share the same InstancedGroup (1 draw call) but render
    /// with different outfits. Inspector controls allow tweaking at runtime.
    ///
    /// Place on any GameObject in the scene. Auto-finds CityMap3D and VoxelChunkManager.
    /// </summary>
    public class ClothingTestSpawner : MonoBehaviour
    {
        public enum OutfitPresetType
        {
            Naked,
            SuitBlue,
            SuitBrown,
            Overcoat,
            Police,
            Casual
        }

        [Header("Asset")]
        [Tooltip("Base filename in StreamingAssets/voxel_characters/ (without extension).")]
        [SerializeField] private string assetBaseName = "Civilian1";

        [Tooltip("Voxel size in world units. Must match authoring.")]
        [SerializeField] private float voxelSize = 0.01f;

        [Header("Spawn Control")]
        [Tooltip("If true, spawns both test characters automatically on Start.")]
        [SerializeField] private bool autoSpawnOnStart = true;

        [Tooltip("Delay before spawning (seconds) to let city build complete.")]
        [SerializeField] private float spawnDelay = 1.0f;

        [Tooltip("Horizontal spacing between the two characters (world units).")]
        [SerializeField] private float spacing = 0.8f;

        [Tooltip("Spawn offset relative to this GameObject's position.")]
        [SerializeField] private Vector3 spawnOffset = Vector3.zero;

        [Header("Outfits")]
        [Tooltip("Outfit for Civilian_01.")]
        [SerializeField] private OutfitPresetType outfit1 = OutfitPresetType.SuitBlue;

        [Tooltip("Outfit for Civilian_02.")]
        [SerializeField] private OutfitPresetType outfit2 = OutfitPresetType.SuitBrown;

        [Header("Animation")]
        [Tooltip("Starting animation state for both characters.")]
        [SerializeField] private CharacterAnimation.AnimState initialState = CharacterAnimation.AnimState.TPose;

        [Tooltip("Walk speed for both characters.")]
        [SerializeField] private float walkSpeed = 1f;

        [Header("References (auto-found if not assigned)")]
        [SerializeField] internal VoxelChunkManager chunkManager;
        [SerializeField] private CityMap3D cityMap;
        [SerializeField] private Transform mapRoot;

        // ---- Runtime state ----
        private GameObject char1GO;
        private GameObject char2GO;
        private VoxelCharacter char1VC;
        private VoxelCharacter char2VC;
        private ClothingSystem char1Clothing;
        private ClothingSystem char2Clothing;
        private CharacterAnimation char1Anim;
        private CharacterAnimation char2Anim;
        private bool isSpawned = false;

        /// <summary>True if both test characters are alive in the scene.</summary>
        public bool IsSpawned => isSpawned;

        /// <summary>Access Civilian_01's VoxelCharacter.</summary>
        public VoxelCharacter Character1 => char1VC;

        /// <summary>Access Civilian_02's VoxelCharacter.</summary>
        public VoxelCharacter Character2 => char2VC;

        void Start()
        {
            if (chunkManager == null)
                chunkManager = FindFirstObjectByType<VoxelChunkManager>();
            if (cityMap == null)
                cityMap = FindFirstObjectByType<CityMap3D>();
            if (mapRoot == null)
                mapRoot = cityMap != null ? cityMap.MapRoot : null;

            if (autoSpawnOnStart)
                StartCoroutine(DelayedSpawn());
        }

        private IEnumerator DelayedSpawn()
        {
            yield return new WaitForSeconds(spawnDelay);
            Spawn();
        }

        /// <summary>
        /// Spawn both test characters. Call from inspector button or code.
        /// </summary>
        public void Spawn()
        {
            if (isSpawned)
            {
                Debug.LogWarning("[ClothingTest] Already spawned — despawn first.");
                return;
            }

            if (chunkManager == null)
            {
                Debug.LogError("[ClothingTest] No VoxelChunkManager found.");
                return;
            }

            string assetFileName = assetBaseName + ".json";
            string assetPath = Path.Combine(Application.streamingAssetsPath, "voxel_characters", assetFileName);
            if (!File.Exists(assetPath))
            {
                Debug.LogError($"[ClothingTest] Asset not found: {assetPath}");
                return;
            }

            // Find or create ClothingTest parent under this spawner's transform
            var testParent = transform.Find("ClothingTest");
            if (testParent == null)
            {
                var tp = new GameObject("ClothingTest");
                tp.transform.SetParent(transform, false);
                testParent = tp.transform;
            }

            Vector3 basePos = transform.position + spawnOffset;

            // Spawn Civilian_01
            char1GO = new GameObject("Civilian_01");
            char1GO.transform.SetParent(testParent, false);
            char1GO.transform.position = new Vector3(basePos.x - spacing, basePos.y, basePos.z);
            char1VC = char1GO.AddComponent<VoxelCharacter>();
            char1VC.assetFileName = assetFileName;
            char1VC.voxelSize = voxelSize;
            char1VC.chunkManager = chunkManager;
            char1VC.useInstancing = true;
            char1VC.useWorldPosition = true;
            char1VC.showGizmo = true;

            // Spawn Civilian_02
            char2GO = new GameObject("Civilian_02");
            char2GO.transform.SetParent(testParent, false);
            char2GO.transform.position = new Vector3(basePos.x + spacing, basePos.y, basePos.z);
            char2VC = char2GO.AddComponent<VoxelCharacter>();
            char2VC.assetFileName = assetFileName;
            char2VC.voxelSize = voxelSize;
            char2VC.chunkManager = chunkManager;
            char2VC.useInstancing = true;
            char2VC.useWorldPosition = true;
            char2VC.showGizmo = true;

            isSpawned = true;
            Debug.Log($"[ClothingTest] Spawned Civilian_01 and Civilian_02 at {basePos} (spacing={spacing}). " +
                      $"Waiting for ClothingSystem init...");

            // Wait for VoxelCharacter.Start() + ClothingSystem.Start() to complete
            StartCoroutine(InitializeAfterDelay());
        }

        private IEnumerator InitializeAfterDelay()
        {
            // VoxelCharacter.Start() runs next frame, ClothingSystem.Start() same frame
            // Wait for initialization to complete
            float elapsed = 0f;
            float maxWait = 3f;

            while (elapsed < maxWait)
            {
                char1Clothing = char1GO?.GetComponent<ClothingSystem>();
                char2Clothing = char2GO?.GetComponent<ClothingSystem>();

                if (char1Clothing != null && char1Clothing.IsInitialized &&
                    char2Clothing != null && char2Clothing.IsInitialized)
                    break;

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Get animation components
            char1Anim = char1GO?.GetComponent<CharacterAnimation>();
            char2Anim = char2GO?.GetComponent<CharacterAnimation>();

            // Apply animation state
            if (char1Anim != null)
            {
                char1Anim.autoDetectWalking = false;
                char1Anim.SetState(initialState);
                char1Anim.walkSpeed = walkSpeed;
            }
            if (char2Anim != null)
            {
                char2Anim.autoDetectWalking = false;
                char2Anim.SetState(initialState);
                char2Anim.walkSpeed = walkSpeed;
            }

            // Apply outfits
            ApplyOutfits();

            if (char1Clothing != null && char1Clothing.IsInitialized &&
                char2Clothing != null && char2Clothing.IsInitialized)
            {
                Debug.Log($"[ClothingTest] ✅ Both characters initialized. " +
                          $"Civilian_01: {outfit1}, Civilian_02: {outfit2}. " +
                          $"Open Debug HUD (O key) → Clothing tab to control individually.");
            }
            else
            {
                Debug.LogWarning("[ClothingTest] One or both ClothingSystems failed to initialize within timeout.");
            }
        }

        /// <summary>
        /// Apply the currently selected inspector outfits to both characters.
        /// Call this after changing outfit1/outfit2 in the inspector at runtime.
        /// </summary>
        public void ApplyOutfits()
        {
            if (char1Clothing != null && char1Clothing.IsInitialized)
            {
                char1Clothing.SetOutfit(PresetToRemap(outfit1));
                Debug.Log($"[ClothingTest] Civilian_01 dressed: {outfit1}");
            }
            if (char2Clothing != null && char2Clothing.IsInitialized)
            {
                char2Clothing.SetOutfit(PresetToRemap(outfit2));
                Debug.Log($"[ClothingTest] Civilian_02 dressed: {outfit2}");
            }
        }

        /// <summary>
        /// Despawn both test characters.
        /// </summary>
        public void Despawn()
        {
            if (char1GO != null) Destroy(char1GO);
            if (char2GO != null) Destroy(char2GO);
            char1GO = char2GO = null;
            char1VC = char2VC = null;
            char1Clothing = char2Clothing = null;
            char1Anim = char2Anim = null;
            isSpawned = false;
            Debug.Log("[ClothingTest] Despawned both test characters.");
        }

        /// <summary>Map an OutfitPresetType to a region→material dictionary.</summary>
        private static Dictionary<int, ushort> PresetToRemap(OutfitPresetType preset)
        {
            return preset switch
            {
                OutfitPresetType.Naked => new Dictionary<int, ushort>(),
                OutfitPresetType.SuitBlue => new Dictionary<int, ushort>
                {
                    { 3, 126 }, { 4, 126 }, { 6, 126 }, { 7, 105 }
                },
                OutfitPresetType.SuitBrown => new Dictionary<int, ushort>
                {
                    { 3, 106 }, { 4, 106 }, { 6, 106 }, { 7, 105 }
                },
                OutfitPresetType.Overcoat => new Dictionary<int, ushort>
                {
                    { 3, 108 }, { 4, 108 }, { 6, 126 }, { 7, 105 }
                },
                OutfitPresetType.Police => new Dictionary<int, ushort>
                {
                    { 3, 126 }, { 4, 126 }, { 6, 126 }, { 7, 105 }
                },
                OutfitPresetType.Casual => new Dictionary<int, ushort>
                {
                    { 3, 107 }, { 4, 107 }, { 6, 104 }, { 7, 105 }
                },
                _ => new Dictionary<int, ushort>()
            };
        }

        void OnDestroy()
        {
            Despawn();
        }

        void OnDrawGizmos()
        {
            if (!isSpawned) return;

            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            if (char1GO != null)
            {
                var size = new Vector3(1, 2, 1) * voxelSize * 10;
                Gizmos.DrawWireCube(char1GO.transform.position + new Vector3(0, size.y * 0.5f, 0), size);
            }
            if (char2GO != null)
            {
                var size = new Vector3(1, 2, 1) * voxelSize * 10;
                Gizmos.DrawWireCube(char2GO.transform.position + new Vector3(0, size.y * 0.5f, 0), size);
            }
        }
    }
}
