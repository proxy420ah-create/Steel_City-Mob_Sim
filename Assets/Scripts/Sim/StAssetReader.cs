using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Reads .stasset binary voxel files at runtime and converts them to Unity Meshes.
    /// Format: 16-byte header + uint16 voxel data (X-major / Fortran order).
    /// Material IDs map to colors via MOB_MATERIALS dictionary.
    /// </summary>
    public static class StAssetReader
    {
        // 1920s material palette — must match mob_materials.py
        private static readonly Color[] MobColors = new Color[256];
        private static readonly Color[] DefaultColors = new Color[256];

        static StAssetReader()
        {
            InitMaterialColors();
        }

        // Total defined materials (for tint buffer sizing).
        // 130-139 is the reserved gameplay block (crosswalk/cover/alley/obstacle/
        // trolley/water — see CITY_LAYOUT_PIPELINE.md); 140-147 is the vehicle
        // block (paint/accent/chrome/tire/lights/interior/auto-glass — VEHICLE_VOXEL_ASSETS.md).
        public const int MaterialCount = 148;

        private static void InitMaterialColors()
        {
            // --- Air ---
            MobColors[0] = new Color(0, 0, 0, 0);

            // --- Masonry (100-105) ---
            MobColors[100] = new Color(0.58f, 0.26f, 0.20f, 1f);  // Red Brick
            MobColors[101] = new Color(0.48f, 0.42f, 0.34f, 1f);  // Stone — granite/limestone
            MobColors[102] = new Color(0.58f, 0.58f, 0.54f, 1f);  // Concrete
            MobColors[103] = new Color(0.82f, 0.80f, 0.74f, 1f);  // Stucco
            MobColors[104] = new Color(0.18f, 0.18f, 0.20f, 1f);  // Asphalt
            MobColors[105] = new Color(0.42f, 0.38f, 0.34f, 1f);  // Cobblestone

            // --- Wood (106-108) ---
            MobColors[106] = new Color(0.30f, 0.18f, 0.10f, 1f);  // Dark Wood
            MobColors[107] = new Color(0.60f, 0.42f, 0.25f, 1f);  // Light Wood
            MobColors[108] = new Color(0.42f, 0.36f, 0.26f, 1f);  // Weathered Wood

            // --- Metal (109-111) ---
            MobColors[109] = new Color(0.28f, 0.24f, 0.22f, 1f);  // Dark Iron
            MobColors[110] = new Color(0.42f, 0.40f, 0.36f, 1f);  // Aged Metal
            MobColors[111] = new Color(0.90f, 0.88f, 0.82f, 1f);  // Painted Metal

            // --- Glass (112-114) ---
            MobColors[112] = new Color(0.45f, 0.55f, 0.65f, 0.6f); // Window Glass
            MobColors[113] = new Color(0.95f, 0.85f, 0.50f, 1f);  // Lit Window (emissive)
            MobColors[114] = new Color(0.55f, 0.65f, 0.70f, 0.5f); // Storefront Glass

            // --- Neon (115-117) — emissive ---
            MobColors[115] = new Color(0.95f, 0.15f, 0.15f, 1f);  // Neon Red
            MobColors[116] = new Color(0.15f, 0.30f, 0.95f, 1f);  // Neon Blue
            MobColors[117] = new Color(0.15f, 0.85f, 0.25f, 1f);  // Neon Green

            // --- Roofing (118-119) ---
            MobColors[118] = new Color(0.28f, 0.24f, 0.20f, 1f);  // Tar
            MobColors[119] = new Color(0.55f, 0.32f, 0.20f, 1f);  // Terracotta

            // --- Painted Surfaces (120-122, 129) ---
            MobColors[120] = new Color(0.45f, 0.12f, 0.10f, 1f);  // Painted Red
            MobColors[121] = new Color(0.15f, 0.28f, 0.18f, 1f);  // Painted Green
            MobColors[122] = new Color(0.22f, 0.12f, 0.08f, 1f);  // Painted Brown
            MobColors[129] = new Color(0.12f, 0.20f, 0.45f, 1f);  // Painted Blue

            // --- Metal Decorative (123-124) ---
            MobColors[123] = new Color(0.78f, 0.62f, 0.20f, 1f);  // Gold/Brass
            MobColors[124] = new Color(1.0f, 0.85f, 0.50f, 1f);   // Lamp Glow (emissive)

            // --- Character (125-128) ---
            MobColors[125] = new Color(0.82f, 0.68f, 0.55f, 1f);  // Flesh
            MobColors[126] = new Color(0.06f, 0.06f, 0.07f, 1f);  // Black Fabric
            MobColors[127] = new Color(0.88f, 0.86f, 0.82f, 1f);  // White Fabric
            MobColors[128] = new Color(0.12f, 0.08f, 0.06f, 1f);  // Hair

            // --- Terrain / gameplay (130-139) — reserved block ---
            MobColors[137] = new Color(0.10f, 0.25f, 0.45f, 1f);  // Water — matches river tile palette #1A4073

            // --- Vehicle (140-147) — VoxelAssetStudio "Vehicle" asset type palette ---
            MobColors[140] = new Color(0.23f, 0.49f, 0.36f, 1f);  // Vehicle Paint — REMAPPABLE per-instance (faction/owner)
            MobColors[141] = new Color(0.14f, 0.16f, 0.15f, 1f);  // Vehicle Accent — fenders, roof, pinstripe
            MobColors[142] = new Color(0.75f, 0.78f, 0.82f, 1f);  // Chrome — bumpers, grille, trim
            MobColors[143] = new Color(0.18f, 0.18f, 0.19f, 1f);  // Tire Rubber
            MobColors[144] = new Color(0.96f, 0.90f, 0.69f, 1f);  // Headlight (warm cream)
            MobColors[145] = new Color(0.71f, 0.16f, 0.14f, 1f);  // Taillight
            MobColors[146] = new Color(0.42f, 0.29f, 0.18f, 1f);  // Interior Leather
            MobColors[147] = new Color(0.59f, 0.67f, 0.73f, 0.6f);// Auto Glass (windshield/windows)

            // Snapshot defaults so runtime modifications can be reset
            for (int i = 0; i < 256; i++)
                DefaultColors[i] = MobColors[i];
        }

        public static Color GetMaterialColor(ushort materialId)
        {
            if (materialId < MobColors.Length && MobColors[materialId].a > 0f)
                return MobColors[materialId];
            return Color.white;
        }

        public static void SetMaterialColor(ushort materialId, Color color)
        {
            if (materialId < MobColors.Length)
                MobColors[materialId] = color;
        }

        public static Color GetDefaultMaterialColor(ushort materialId)
        {
            if (materialId < DefaultColors.Length && DefaultColors[materialId].a > 0f)
                return DefaultColors[materialId];
            return Color.white;
        }

        /// <summary>
        /// Load a consolidated .character.json file and return the voxel grid.
        /// Returns null if the file cannot be parsed.
        /// </summary>
        public static ushort[,,] LoadVoxelsFromJson(string filepath)
        {
            if (!File.Exists(filepath))
            {
                Debug.LogError($"[StAssetReader] File not found: {filepath}");
                return null;
            }

            CharacterJsonLoader.Load(filepath, out ushort[,,] voxels, out _, out _, out _);
            return voxels;
        }

        /// <summary>
        /// Load a .stasset file from disk and return the raw voxel grid.
        /// </summary>
        public static ushort[,,] LoadVoxels(string filepath)
        {
            if (!File.Exists(filepath))
            {
                Debug.LogError($"[StAssetReader] File not found: {filepath}");
                return null;
            }

            byte[] data = File.ReadAllBytes(filepath);
            return ParseVoxels(data);
        }

        /// <summary>
        /// Parse .stasset binary data into a 3D ushort array.
        /// Format: 'STAS' magic, version byte, flags byte, width(u16), height(u16), depth(u16), reserved(4), voxel data.
        /// Voxel data is uint16 in X-major (Fortran) order.
        /// </summary>
        public static ushort[,,] ParseVoxels(byte[] data)
        {
            if (data.Length < 16)
            {
                Debug.LogError("[StAssetReader] File too small for header.");
                return null;
            }

            // Check magic
            if (data[0] != (byte)'S' || data[1] != (byte)'T' ||
                data[2] != (byte)'A' || data[3] != (byte)'S')
            {
                Debug.LogError("[StAssetReader] Invalid magic bytes.");
                return null;
            }

            int version = data[4];
            // data[5] = flags (reserved)

            // Read dimensions (little-endian uint16)
            int width  = data[6]  | (data[7]  << 8);
            int height = data[8]  | (data[9]  << 8);
            int depth  = data[10] | (data[11] << 8);
            // data[12..15] = reserved

            int voxelCount = width * height * depth;
            int expectedBytes = 16 + voxelCount * 2;

            if (data.Length < expectedBytes)
            {
                Debug.LogError($"[StAssetReader] Truncated file. Expected {expectedBytes} bytes, got {data.Length}");
                return null;
            }

            // Read voxel data (X-major / Fortran order: x varies fastest, then y, then z)
            var voxels = new ushort[width, height, depth];
            int offset = 16;
            for (int z = 0; z < depth; z++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        ushort val = (ushort)(data[offset] | (data[offset + 1] << 8));
                        voxels[x, y, z] = val;
                        offset += 2;
                    }
                }
            }

            return voxels;
        }

        /// <summary>
        /// Read a v2 .stasset's SKEL metadata tail and extract
        /// building.attachmentPoints as name → voxel-space centroid.
        /// Returns an empty dict when absent — never throws.
        /// </summary>
        public static Dictionary<string, Vector3> LoadAttachmentPoints(string filepath)
        {
            var result = new Dictionary<string, Vector3>();
            if (!File.Exists(filepath)) return result;

            byte[] data = File.ReadAllBytes(filepath);
            if (data.Length < 16) return result;

            int width  = data[6]  | (data[7]  << 8);
            int height = data[8]  | (data[9]  << 8);
            int depth  = data[10] | (data[11] << 8);

            // Trailing blocks start right after header + voxel data
            int offset = 16 + width * height * depth * 2;
            while (offset + 8 <= data.Length)
            {
                if (data[offset] != (byte)'S' || data[offset + 1] != (byte)'K' ||
                    data[offset + 2] != (byte)'E' || data[offset + 3] != (byte)'L')
                    break;
                int jsonLen = data[offset + 4] | (data[offset + 5] << 8) |
                              (data[offset + 6] << 16) | (data[offset + 7] << 24);
                offset += 8;
                if (jsonLen <= 0 || offset + jsonLen > data.Length) break;
                string json = Encoding.UTF8.GetString(data, offset, jsonLen);
                offset += jsonLen;
                ParseAttachmentPointsJson(json, result);
            }
            return result;
        }

        // Matches  "name" : { "x" : 1.5, "y" : 2.0, "z" : 3.5 }
        private static readonly Regex AttachPointRx = new Regex(
            "\"(?<name>[^\"]+)\"\\s*:\\s*\\{\\s*\"x\"\\s*:\\s*(?<x>-?[0-9]+(?:\\.[0-9]+)?)\\s*," +
            "\\s*\"y\"\\s*:\\s*(?<y>-?[0-9]+(?:\\.[0-9]+)?)\\s*," +
            "\\s*\"z\"\\s*:\\s*(?<z>-?[0-9]+(?:\\.[0-9]+)?)\\s*\\}",
            RegexOptions.Compiled);

        private static void ParseAttachmentPointsJson(string json, Dictionary<string, Vector3> result)
        {
            // Scope to the attachmentPoints object — other SKEL sections
            // (bones, joints) may carry their own x/y/z objects.
            string section = ExtractJsonObject(json, "\"attachmentPoints\"");
            if (section == null) return;
            foreach (Match m in AttachPointRx.Matches(section))
            {
                float x = float.Parse(m.Groups["x"].Value, System.Globalization.CultureInfo.InvariantCulture);
                float y = float.Parse(m.Groups["y"].Value, System.Globalization.CultureInfo.InvariantCulture);
                float z = float.Parse(m.Groups["z"].Value, System.Globalization.CultureInfo.InvariantCulture);
                result[m.Groups["name"].Value] = new Vector3(x, y, z);
            }
        }

        /// <summary>Return the balanced {...} object following the given JSON key, or null.</summary>
        private static string ExtractJsonObject(string json, string quotedKey)
        {
            int ki = json.IndexOf(quotedKey, StringComparison.Ordinal);
            if (ki < 0) return null;
            int open = json.IndexOf('{', ki);
            if (open < 0) return null;
            int depth = 0;
            for (int i = open; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}' && --depth == 0)
                    return json.Substring(open, i - open + 1);
            }
            return null;
        }

        /// <summary>
        /// Load a .stasset file and convert it to a Unity Mesh with per-vertex colors.
        /// Uses simple face culling: only faces adjacent to air are generated.
        /// Each voxel = 1 unit. Mesh is centered at origin.
        /// </summary>
        public static Mesh LoadAsMesh(string filepath, float voxelSize = 0.0625f)
        {
            var voxels = LoadVoxels(filepath);
            if (voxels == null) return null;

            return VoxelBuildingMeshifier.BuildMesh(voxels, voxelSize);
        }
    }
}
