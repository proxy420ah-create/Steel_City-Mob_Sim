using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Runtime clothing system for voxel characters.
    /// Uses per-instance material remapping via the GPU compute shader (CSPose kernel).
    /// Each instance can have a unique outfit without affecting other instances of the same asset.
    ///
    /// Attach to the same GameObject as VoxelCharacter.
    /// </summary>
    [RequireComponent(typeof(VoxelCharacter))]
    public class ClothingSystem : MonoBehaviour
    {
        private VoxelCharacter voxelChar;
        private VoxelChunkManager chunkManager;

        // Region data parsed from .character.json
        private Dictionary<string, int> regionMap;      // "x,y,z" → regionId
        private List<RegionDef> regionDefs;             // region definitions

        // Current outfit: regionId → materialId
        private Dictionary<int, ushort> currentOutfit = new();

        // Original materials (for "naked" reset and debug panel defaults)
        private Dictionary<int, ushort> originalMaterials = new();

        // Cached InstancedCharacter reference (looked up lazily)
        private VoxelChunkManager.InstancedCharacter cachedInstance;
        private bool instanceLookupAttempted = false;

        // Material presets for the debug panel
        private static readonly OutfitPreset[] Presets = new OutfitPreset[]
        {
            new OutfitPreset
            {
                Name = "Naked",
                RegionMaterials = new Dictionary<int, ushort>()
            },
            new OutfitPreset
            {
                Name = "Suit Blue",
                RegionMaterials = new Dictionary<int, ushort>
                {
                    { 3, 126 },  // Torso → blue suit
                    { 4, 126 },  // Arms → blue sleeves
                    { 6, 126 },  // Legs → blue pants
                    { 7, 105 }   // Feet → cobblestone (shoes)
                }
            },
            new OutfitPreset
            {
                Name = "Suit Brown",
                RegionMaterials = new Dictionary<int, ushort>
                {
                    { 3, 106 },  // Torso → brown
                    { 4, 106 },  // Arms → brown
                    { 6, 106 },  // Legs → brown
                    { 7, 105 }   // Feet → shoes
                }
            },
            new OutfitPreset
            {
                Name = "Overcoat",
                RegionMaterials = new Dictionary<int, ushort>
                {
                    { 3, 108 },  // Torso → weathered overcoat
                    { 4, 108 },  // Arms → overcoat sleeves
                    { 6, 126 },  // Legs → blue pants
                    { 7, 105 }   // Feet → shoes
                }
            },
            new OutfitPreset
            {
                Name = "Police",
                RegionMaterials = new Dictionary<int, ushort>
                {
                    { 3, 126 },  // Torso → blue uniform
                    { 4, 126 },  // Arms → blue
                    { 6, 126 },  // Legs → blue
                    { 7, 105 }   // Feet → shoes
                }
            },
            new OutfitPreset
            {
                Name = "Casual",
                RegionMaterials = new Dictionary<int, ushort>
                {
                    { 3, 107 },  // Torso → light
                    { 4, 107 },  // Arms → light
                    { 6, 104 },  // Legs → dark
                    { 7, 105 }   // Feet → shoes
                }
            }
        };

        // Debug panel state
        private bool debugPanelVisible = false;
        private int selectedPreset = -1;
        private Vector2 scrollPos;
        private int[] regionMaterialSelections;

        public bool IsInitialized { get; private set; }

        void Start()
        {
            voxelChar = GetComponent<VoxelCharacter>();
            chunkManager = voxelChar.chunkManager ?? FindFirstObjectByType<VoxelChunkManager>();

            if (chunkManager == null)
            {
                Debug.LogWarning("[ClothingSystem] No VoxelChunkManager found — clothing swap disabled.");
                return;
            }

            LoadRegionData();
        }

        void LoadRegionData()
        {
            if (voxelChar == null || string.IsNullOrEmpty(voxelChar.assetFileName))
                return;

            if (!voxelChar.assetFileName.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log("[ClothingSystem] Non-JSON asset — clothing system inactive (legacy .stasset).");
                return;
            }

            string path = Path.Combine(Application.streamingAssetsPath, "voxel_characters", voxelChar.assetFileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[ClothingSystem] File not found: {path}");
                return;
            }

            // Load with regions
            CharacterJsonLoader.Load(path, out _, out _, out _, out _, out regionMap, out regionDefs);

            if (regionMap == null || regionMap.Count == 0)
            {
                Debug.Log("[ClothingSystem] No region data in character JSON — clothing system inactive.");
                return;
            }

            // Read original materials from the shared voxel buffer (one-time read for defaults)
            int dimX, dimY, dimZ;
            if (!chunkManager.TryGetInstancedDims(voxelChar.assetFileName, out dimX, out dimY, out dimZ))
            {
                Debug.LogWarning("[ClothingSystem] Could not get instanced dims from chunk manager.");
                return;
            }
            uint[] gpuData = chunkManager.GetSharedVoxelData(voxelChar.assetFileName);
            if (gpuData == null)
            {
                Debug.LogWarning("[ClothingSystem] Could not get shared voxel data from chunk manager.");
                return;
            }

            // Store original materials per region (read-only, for reset/debug defaults)
            originalMaterials.Clear();
            foreach (var kvp in regionMap)
            {
                var parts = kvp.Key.Split(',');
                if (parts.Length == 3 &&
                    int.TryParse(parts[0], out int x) &&
                    int.TryParse(parts[1], out int y) &&
                    int.TryParse(parts[2], out int z))
                {
                    int flat = x + y * dimX + z * dimX * dimY;
                    int rid = kvp.Value;
                    if (flat >= 0 && flat < gpuData.Length && !originalMaterials.ContainsKey(rid))
                        originalMaterials[rid] = (ushort)gpuData[flat];
                }
            }

            // Initialize debug panel selections
            if (regionDefs != null && regionDefs.Count > 0)
            {
                regionMaterialSelections = new int[regionDefs.Count];
                for (int i = 0; i < regionDefs.Count; i++)
                {
                    regionMaterialSelections[i] = originalMaterials.TryGetValue(regionDefs[i].id, out var mat) ? mat : 0;
                }
            }

            IsInitialized = true;

            Debug.Log($"[ClothingSystem] Initialized — {originalMaterials.Count} regions, " +
                      $"{regionDefs?.Count ?? 0} region defs. Per-instance remap mode (GPU compute).");
        }

        /// <summary>Get the InstancedCharacter for this character's GameObject (lazy lookup).</summary>
        private VoxelChunkManager.InstancedCharacter GetInstancedCharacter()
        {
            if (cachedInstance != null && cachedInstance.gameObject == gameObject) return cachedInstance;
            if (instanceLookupAttempted) return cachedInstance;
            instanceLookupAttempted = true;
            cachedInstance = chunkManager.GetInstancedCharacter(voxelChar.assetFileName, gameObject);
            if (cachedInstance != null)
                Debug.Log("[ClothingSystem] InstancedCharacter reference acquired for per-instance remap.");
            return cachedInstance;
        }

        /// <summary>Apply an outfit (regionId → materialId mapping). Regions not in the dict keep their current material.</summary>
        public void ApplyOutfit(Dictionary<int, ushort> regionMaterials)
        {
            if (!IsInitialized) return;

            var ic = GetInstancedCharacter();
            if (ic == null)
            {
                Debug.LogWarning("[ClothingSystem] No InstancedCharacter found — cannot apply per-instance outfit.");
                return;
            }

            // Build the full remap array: start from current outfit, apply changes
            var fullRemap = new Dictionary<int, ushort>(currentOutfit);
            if (regionMaterials != null)
            {
                foreach (var kvp in regionMaterials)
                {
                    if (kvp.Value == 0)
                        fullRemap.Remove(kvp.Key);
                    else
                        fullRemap[kvp.Key] = kvp.Value;
                }
            }

            chunkManager.SetInstanceOutfit(ic, fullRemap);
            currentOutfit = fullRemap;
        }

        /// <summary>Set the entire outfit, replacing any previous remap. Empty dict = naked (no remap).</summary>
        public void SetOutfit(Dictionary<int, ushort> regionMaterials)
        {
            if (!IsInitialized) return;

            var ic = GetInstancedCharacter();
            if (ic == null)
            {
                Debug.LogWarning("[ClothingSystem] No InstancedCharacter found — cannot set per-instance outfit.");
                return;
            }

            var fullRemap = new Dictionary<int, ushort>();
            if (regionMaterials != null)
            {
                foreach (var kvp in regionMaterials)
                {
                    if (kvp.Value != 0)
                        fullRemap[kvp.Key] = kvp.Value;
                }
            }

            chunkManager.SetInstanceOutfit(ic, fullRemap);
            currentOutfit = fullRemap;
        }

        /// <summary>Reset to original (naked) materials.</summary>
        public void ResetToNaked()
        {
            var ic = GetInstancedCharacter();
            if (ic != null)
                chunkManager.ClearInstanceOutfit(ic);
            currentOutfit.Clear();
            if (regionMaterialSelections != null && regionDefs != null)
            {
                for (int i = 0; i < regionDefs.Count; i++)
                    regionMaterialSelections[i] = originalMaterials.TryGetValue(regionDefs[i].id, out var mat) ? mat : 0;
            }
            selectedPreset = 0;
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.oKey.wasPressedThisFrame)
            {
                debugPanelVisible = !debugPanelVisible;
            }
        }

        // IMGUI debug panel — drawn by DebugHUDManager via DrawClothingTab()
        public void DrawClothingTab()
        {
            if (!IsInitialized)
            {
                GUILayout.Label("Clothing system not initialized (no regions in asset).");
                return;
            }

            GUILayout.Label("<b>Outfit Presets</b>");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Presets.Length; i++)
            {
                bool active = selectedPreset == i;
                var oldBg = GUI.backgroundColor;
                if (active) GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);
                if (GUILayout.Button(Presets[i].Name, GUILayout.Height(24)))
                {
                    selectedPreset = i;
                    SetOutfit(Presets[i].RegionMaterials);
                    SyncSelectionsFromOutfit(Presets[i].RegionMaterials);
                }
                GUI.backgroundColor = oldBg;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            if (GUILayout.Button("Reset to Naked", GUILayout.Height(24)))
            {
                ResetToNaked();
            }

            GUILayout.Space(12);
            GUILayout.Label("<b>Per-Region Materials</b>");

            if (regionDefs == null || regionDefs.Count == 0)
            {
                GUILayout.Label("No region definitions found.");
                return;
            }

            scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.Height(200));

            for (int i = 0; i < regionDefs.Count; i++)
            {
                var def = regionDefs[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{def.name} (id={def.id})", GUILayout.Width(120));

                int current = regionMaterialSelections[i];
                string[] matOptions = GetMaterialOptions();
                int currentIdx = System.Array.IndexOf(GetMaterialIds(), (ushort)current);
                if (currentIdx < 0) currentIdx = 0;

                int newIdx = GUILayout.SelectionGrid(currentIdx, matOptions, 4, GUILayout.Width(300));
                if (newIdx != currentIdx)
                {
                    ushort newMat = GetMaterialIds()[newIdx];
                    regionMaterialSelections[i] = newMat;
                    ApplyOutfit(new Dictionary<int, ushort> { { def.id, newMat } });
                    selectedPreset = -1;
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            GUILayout.Space(8);
            GUILayout.Label("<b>Current Outfit</b>");
            foreach (var kvp in currentOutfit)
            {
                string regionName = regionDefs?.Find(r => r.id == kvp.Key)?.name ?? $"Region {kvp.Key}";
                GUILayout.Label($"  {regionName}: mat {kvp.Value}");
            }

            GUILayout.Space(4);
            GUILayout.Label($"Regions: {originalMaterials.Count} / {regionDefs?.Count ?? 0} defs (per-instance GPU remap)");
        }

        void SyncSelectionsFromOutfit(Dictionary<int, ushort> outfit)
        {
            if (regionDefs == null || regionMaterialSelections == null) return;
            for (int i = 0; i < regionDefs.Count; i++)
            {
                if (outfit.TryGetValue(regionDefs[i].id, out var mat))
                    regionMaterialSelections[i] = mat;
                else if (originalMaterials.TryGetValue(regionDefs[i].id, out var origMat))
                    regionMaterialSelections[i] = origMat;
            }
        }

        static readonly string[] MatOptionLabels = new string[]
        {
            "103 Skin", "122 Skin2", "126 Blue", "106 Brown", "108 Weathered",
            "107 Light", "104 Dark", "105 Cobble", "120 Red", "127 White",
            "109 Iron", "108 Brass", "0 None"
        };
        static readonly ushort[] MatOptionIds = new ushort[]
        {
            103, 122, 126, 106, 108, 107, 104, 105, 120, 127, 109, 108, 0
        };

        static string[] GetMaterialOptions() => MatOptionLabels;
        static ushort[] GetMaterialIds() => MatOptionIds;

        class OutfitPreset
        {
            public string Name;
            public Dictionary<int, ushort> RegionMaterials;
        }
    }
}
