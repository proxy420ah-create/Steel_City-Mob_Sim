using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Atomic character-asset registry — see docs/systems/CHARACTER_ASSET_LIFECYCLE.md.
    ///
    /// Before this existed, FIVE code paths each read/parsed the same
    /// .character.json (VoxelCharacter voxels, VoxelChunkManager buffers,
    /// VoxelCharacter anim params, WeaponMount attachments, ClothingSystem
    /// regions) — each extracting a different subset, each free to disagree.
    /// CharacterAssets.Get() parses ONCE and hands every consumer the same
    /// object: geometry, groups, regions, pivots (painted overrides applied),
    /// attachment points, and the defaults-filled AnimParamsData that both the
    /// GPU pose upload AND the CPU weld animator emit from.
    ///
    /// CreateAnimator() returns a NEW VoxelCharacterAnimator per character —
    /// ParamsData/Pivots/JointOffsets are shared (immutable after load), but
    /// ikOverrides/ikBlend are per-instance: two hoodlums sharing Civilian1
    /// must not inherit each other's aim solve.
    /// </summary>
    public sealed class CharacterAsset
    {
        public string FileName;
        public string Path;

        // Geometry + semantic layers
        public ushort[,,] Voxels;
        public int DimX, DimY, DimZ;
        public uint[] GroupIDs;
        public Dictionary<string, int> Regions;
        public List<RegionDef> RegionDefs;

        // Rig data — pivots normalized 0..1 with painted pivot_N overrides applied;
        // missing gids filled from CanonicalPivotFallback so CPU weld and GPU pose
        // rotate around the SAME joints (fixes the divergent-fallback weapon bug).
        public Dictionary<int, Vector3> Pivots;
        public Dictionary<int, Vector3> JointOffsets;
        public Dictionary<string, CharacterJsonLoader.AttachmentPoint> AttachPoints;

        // Animation params — defaults already filled by LoadFromAnimJson.
        // Single default table for BOTH pose authorities (CPU + GPU).
        public VoxelCharacterAnimator.AnimParamsData ParamsData;
        public string AnimParamsRaw;

        // Raw source + metadata
        public string Json;
        public float VoxelSize;

        /// <summary>True when promoted with tools/promote_character_to_unity.py ("handedness":"unity"):
        /// L/R already flipped for Unity. Item attachRotation is still authored in editor convention,
        /// so the Unity "right" hand needs the editor-left roll compensation (see WeaponMount).</summary>
        public bool UnityHanded;

        /// <summary>Fractional pivots used when a gid is missing from authored+painted data.
        /// Matches the shader's hardcoded approximation — the previous GPU-side fallback —
        /// so CPU and GPU agree even on partial exports.</summary>
        public static readonly IReadOnlyDictionary<int, Vector3> CanonicalPivotFallback =
            new Dictionary<int, Vector3>
            {
                { 0, new Vector3(0.5f, 0.4f, 0.5f) },
                { 1, new Vector3(0.5f, 0.78f, 0.5f) },
                { 2, new Vector3(0.25f, 0.75f, 0.5f) },
                { 3, new Vector3(0.75f, 0.75f, 0.5f) },
                { 4, new Vector3(0.375f, 0.34f, 0.5f) },
                { 5, new Vector3(0.625f, 0.34f, 0.5f) },
                { 6, new Vector3(0.375f, 0.20f, 0.5f) },
                { 7, new Vector3(0.625f, 0.20f, 0.5f) },
                { 8, new Vector3(0.25f, 0.75f, 0.5f) },
                { 9, new Vector3(0.75f, 0.75f, 0.5f) },
            };

        // --- Region centroids ---
        // Geometric center of each region's painted extent — the bbox midpoint
        // of tagged voxel indices, NOT the mass mean: a mass centroid drifts
        // onto whichever side carries more paint (an eyes band 2 voxels tall
        // should center on the seam between the two rows, not inside the
        // heavier row). Index space, matching ApplyPivotOverrides: voxel index
        // = its center; consumers add +0.5 at the cell-space conversion.
        // Cached lazily; Regions is load-time data.
        private Dictionary<int, Vector3> regionCentroidCache;
        private Dictionary<int, int> regionCountCache;

        /// <summary>Geometric center (bbox midpoint) of a named wardrobe region
        /// ("Eyes", "Face", ...). False when the region isn't defined or has no tags.</summary>
        public bool TryGetRegionCentroid(string regionName, out Vector3 centroid, out int voxelCount)
        {
            centroid = default;
            voxelCount = 0;
            if (Regions == null || RegionDefs == null || Regions.Count == 0) return false;
            int rid = -1;
            for (int i = 0; i < RegionDefs.Count; i++)
                if (RegionDefs[i] != null && RegionDefs[i].name == regionName)
                { rid = RegionDefs[i].id; break; }
            if (rid < 0) return false;
            if (regionCentroidCache == null) BuildRegionCentroids();
            if (!regionCentroidCache.TryGetValue(rid, out centroid)) return false;
            voxelCount = regionCountCache[rid];
            return true;
        }

        private void BuildRegionCentroids()
        {
            var bmin = new Dictionary<int, Vector3Int>();
            var bmax = new Dictionary<int, Vector3Int>();
            var count = new Dictionary<int, int>();
            foreach (var kv in Regions)
            {
                string key = kv.Key;
                int c1 = key.IndexOf(',');
                int c2 = c1 < 0 ? -1 : key.IndexOf(',', c1 + 1);
                if (c2 < 0) continue;
                if (!int.TryParse(key.Substring(0, c1), out int x)) continue;
                if (!int.TryParse(key.Substring(c1 + 1, c2 - c1 - 1), out int y)) continue;
                if (!int.TryParse(key.Substring(c2 + 1), out int z)) continue;
                if (!bmin.TryGetValue(kv.Value, out var mn))
                { bmin[kv.Value] = new Vector3Int(x, y, z); bmax[kv.Value] = new Vector3Int(x, y, z); count[kv.Value] = 1; continue; }
                bmax[kv.Value] = new Vector3Int(
                    Mathf.Max(bmax[kv.Value].x, x), Mathf.Max(bmax[kv.Value].y, y), Mathf.Max(bmax[kv.Value].z, z));
                bmin[kv.Value] = new Vector3Int(
                    Mathf.Min(mn.x, x), Mathf.Min(mn.y, y), Mathf.Min(mn.z, z));
                count[kv.Value]++;
            }
            regionCentroidCache = new Dictionary<int, Vector3>();
            regionCountCache = new Dictionary<int, int>();
            foreach (var kv in bmin)
            {
                var mn = kv.Value;
                var mx = bmax[kv.Key];
                regionCentroidCache[kv.Key] = new Vector3(
                    (mn.x + mx.x) * 0.5f, (mn.y + mx.y) * 0.5f, (mn.z + mx.z) * 0.5f);
                regionCountCache[kv.Key] = count[kv.Key];
            }
        }

        /// <summary>
        /// Per-character pose context: shares ParamsData/Pivots/JointOffsets
        /// (read-only after load) but owns its ikOverrides/ikBlend so each
        /// character's aim solve is independent.
        /// </summary>
        public VoxelCharacterAnimator CreateAnimator()
        {
            return new VoxelCharacterAnimator
            {
                paramsData = ParamsData,
                pivots = Pivots,
                jointOffsets = JointOffsets
            };
        }
    }

    public static class CharacterAssets
    {
        private static readonly Dictionary<string, CharacterAsset> cache =
            new Dictionary<string, CharacterAsset>();

        /// <summary>Cached parse of StreamingAssets/voxel_characters/{fileName}. Null on failure.</summary>
        public static CharacterAsset Get(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            if (cache.TryGetValue(fileName, out var asset)) return asset;

            string path = Path.Combine(Application.streamingAssetsPath, "voxel_characters", fileName);
            if (!File.Exists(path))
            {
                Debug.LogError($"[CharacterAssets] File not found: {path}");
                return null;
            }

            if (!CharacterJsonLoader.Load(path, out ushort[,,] voxels, out uint[] groupIDs,
                    out Dictionary<int, Vector3> pivots, out string animParamsRaw,
                    out Dictionary<string, int> regions, out List<RegionDef> regionDefs))
                return null;

            string json = File.ReadAllText(path);
            var attachPoints = CharacterJsonLoader.ParseAttachmentPoints(json);

            var dims = new Vector3Int(
                voxels.GetLength(0), voxels.GetLength(1), voxels.GetLength(2));

            // Handedness guard. Models face +Z; Unity is left-handed, so the character's
            // right hand must sit on the +X side. Editor-convention files (right = -X)
            // render mirrored: weapon in the wrong hand, wrong arm raised on Aim.
            // Fix at promotion: tools/promote_character_to_unity.py.
            bool marked = System.Text.RegularExpressions.Regex.IsMatch(json, "\"handedness\"\\s*:\\s*\"unity\"");
            if (attachPoints.TryGetValue("right_hand", out var rhGuard) && rhGuard.pos.x < dims.x * 0.5f)
            {
                Debug.LogWarning($"[CharacterAssets] {fileName}: right_hand is on the -X side (x={rhGuard.pos.x:F1}, " +
                                 $"center={dims.x * 0.5f:F1}) — editor-convention data (handedness marker: {(marked ? "unity — INCONSISTENT" : "absent")}). " +
                                 "Unity will render the weapon/aim on the character's LEFT. Run tools/promote_character_to_unity.py.");
            }

            // Painted pivot_N attachment centroids override authored pivots —
            // applied ONCE here so every consumer (GPU upload, weld, IK solver)
            // rotates around identical joints.
            pivots = pivots ?? new Dictionary<int, Vector3>();
            int painted = CharacterJsonLoader.ApplyPivotOverrides(pivots, attachPoints, dims);
            foreach (var kv in CharacterAsset.CanonicalPivotFallback)
                if (!pivots.ContainsKey(kv.Key))
                    pivots[kv.Key] = kv.Value;

            // Build the canonical params table via the animator's loader — its
            // default-fill IS the single source of truth for missing sections.
            string pivotsRaw = CharacterJsonLoader.ExtractPivotsRaw(json);
            string animJson = "{\"format\":\"anim_params\",\"version\":1," +
                "\"pivots\":" + (pivotsRaw ?? "{}") + "," +
                "\"params\":" + (animParamsRaw ?? "{}") + "}";
            var template = VoxelCharacterAnimator.LoadFromAnimJson(animJson);

            asset = new CharacterAsset
            {
                FileName = fileName,
                Path = path,
                Voxels = voxels,
                DimX = dims.x,
                DimY = dims.y,
                DimZ = dims.z,
                GroupIDs = groupIDs,
                Regions = regions,
                RegionDefs = regionDefs,
                Pivots = pivots,
                JointOffsets = template != null ? template.jointOffsets : new Dictionary<int, Vector3>(),
                AttachPoints = attachPoints,
                ParamsData = template != null ? template.paramsData : null,
                AnimParamsRaw = animParamsRaw,
                Json = json,
                VoxelSize = CharacterJsonLoader.ParseVoxelSize(json, 0.01f),
                UnityHanded = marked,
            };

            cache[fileName] = asset;
            Debug.Log($"[CharacterAssets] Loaded {fileName}: {dims.x}x{dims.y}x{dims.z}, " +
                      $"{pivots.Count} pivots ({painted} painted), {attachPoints.Count} attach pts, " +
                      $"{(regions?.Count ?? 0)} regions, animParams: {(animParamsRaw != null ? "yes" : "no")}");
            return asset;
        }

        /// <summary>Drop the cached asset (forces a re-parse on next Get — hot-reload).</summary>
        public static void Invalidate(string fileName) => cache.Remove(fileName);

        /// <summary>Drop all cached assets.</summary>
        public static void InvalidateAll() => cache.Clear();
    }
}
