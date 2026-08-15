using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Parses consolidated .character.json files (format: "steelcity_character").
    /// Returns voxel grid, group IDs, pivots, and raw animParams JSON string
    /// from a single file — replacing the old .stasset + .groups + .anim.json trio.
    /// </summary>
    public static class CharacterJsonLoader
    {
        /// <summary>
        /// Load a consolidated .character.json and return all sub-data.
        /// </summary>
        public static bool Load(string path, out ushort[,,] voxels, out uint[] groupIDs, out Dictionary<int, Vector3> pivots, out string animParamsRaw)
        {
            return Load(path, out voxels, out groupIDs, out pivots, out animParamsRaw, out _, out _);
        }

        /// <summary>
        /// Full load with regions support — returns region map and region definitions.
        /// </summary>
        public static bool Load(string path, out ushort[,,] voxels, out uint[] groupIDs,
            out Dictionary<int, Vector3> pivots, out string animParamsRaw,
            out Dictionary<string, int> regions, out List<RegionDef> regionDefs)
        {
            voxels = null;
            groupIDs = null;
            pivots = null;
            animParamsRaw = null;
            regions = null;
            regionDefs = null;

            if (!File.Exists(path))
            {
                Debug.LogError($"[CharacterJsonLoader] File not found: {path}");
                return false;
            }

            string json = File.ReadAllText(path);

            // Parse dims
            int[] dims = ParseDims(json);
            if (dims == null || dims.Length < 3)
            {
                Debug.LogError("[CharacterJsonLoader] Failed to parse dims");
                return false;
            }

            int w = dims[0], h = dims[1], d = dims[2];
            int total = w * h * d;

            // Parse voxels: [[x,y,z,mid], ...]
            voxels = ParseVoxels(json, w, h, d);
            if (voxels == null)
            {
                Debug.LogError("[CharacterJsonLoader] Failed to parse voxels");
                return false;
            }

            // Parse groups: dict {"x,y,z": gid} or array [[x,y,z,gid],...]
            groupIDs = ParseGroups(json, w, h, d, total);

            // Parse pivots: {"gid": {"x":..,"y":..,"z":..}}
            pivots = ParsePivots(json);

            // Extract animParams raw JSON substring
            animParamsRaw = ExtractJsonObject(json, "\"animParams\"");

            // Parse regions: {"x,y,z": regionId, ...}
            regions = ParseRegions(json);

            // Parse regionDefs: [{"id":0,"name":"Skin","color":"#...","desc":"..."}, ...]
            regionDefs = ParseRegionDefs(json);

            Debug.Log($"[CharacterJsonLoader] Loaded {path}: {w}x{h}x{d} = {total:N0} voxels, " +
                      $"{(groupIDs != null ? groupIDs.Length : 0)} groupIDs, " +
                      $"{(pivots != null ? pivots.Count : 0)} pivots, " +
                      $"animParams: {(animParamsRaw != null ? "yes" : "no")}, " +
                      $"regions: {(regions != null ? regions.Count : 0)}, " +
                      $"regionDefs: {(regionDefs != null ? regionDefs.Count : 0)}");

            return true;
        }

        /// <summary>Check if a file path points to a consolidated .character.json.</summary>
        public static bool IsCharacterJson(string path)
        {
            return path.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase);
        }

        #region Parsers

        static int[] ParseDims(string json)
        {
            int idx = json.IndexOf("\"dims\"");
            if (idx < 0) return null;
            int start = json.IndexOf('[', idx);
            int end = json.IndexOf(']', start);
            if (start < 0 || end < 0) return null;

            string inner = json.Substring(start + 1, end - start - 1);
            var parts = inner.Split(',');
            var list = new List<int>();
            foreach (var p in parts)
            {
                if (int.TryParse(p.Trim(), out int val))
                    list.Add(val);
            }
            return list.ToArray();
        }

        static ushort[,,] ParseVoxels(string json, int w, int h, int d)
        {
            int idx = json.IndexOf("\"voxels\"");
            if (idx < 0) return null;
            int start = json.IndexOf('[', idx);
            if (start < 0) return null;

            // Find matching close bracket for outer array
            int depth = 0;
            int end = start;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']') { depth--; if (depth == 0) { end = i; break; } }
            }

            string inner = json.Substring(start + 1, end - start - 1);
            var grid = new ushort[w, h, d];

            int pos = 0;
            while (pos < inner.Length)
            {
                int open = inner.IndexOf('[', pos);
                if (open < 0) break;
                int close = inner.IndexOf(']', open);
                if (close < 0) break;

                string entry = inner.Substring(open + 1, close - open - 1);
                var parts = entry.Split(',');
                if (parts.Length >= 4 &&
                    int.TryParse(parts[0].Trim(), out int x) &&
                    int.TryParse(parts[1].Trim(), out int y) &&
                    int.TryParse(parts[2].Trim(), out int z) &&
                    int.TryParse(parts[3].Trim(), out int mid))
                {
                    if (x >= 0 && x < w && y >= 0 && y < h && z >= 0 && z < d)
                        grid[x, y, z] = (ushort)mid;
                }
                pos = close + 1;
            }

            return grid;
        }

        static uint[] ParseGroups(string json, int w, int h, int d, int total)
        {
            int idx = json.IndexOf("\"groups\"");
            if (idx < 0) return null;

            int arrStart = json.IndexOf('[', idx);
            int objStart = json.IndexOf('{', idx);
            int start = -1;
            bool isArray = false;

            if (arrStart >= 0 && (objStart < 0 || arrStart < objStart))
            {
                start = arrStart;
                isArray = true;
            }
            else if (objStart >= 0)
            {
                start = objStart;
                isArray = false;
            }
            if (start < 0) return null;

            var result = new uint[total];

            if (isArray)
            {
                // Array format: [[x,y,z,gid], ...]
                int depth = 0;
                int end = start;
                for (int i = start; i < json.Length; i++)
                {
                    if (json[i] == '[') depth++;
                    else if (json[i] == ']') { depth--; if (depth == 0) { end = i; break; } }
                }

                string inner = json.Substring(start + 1, end - start - 1);
                int pos = 0;
                while (pos < inner.Length)
                {
                    int open = inner.IndexOf('[', pos);
                    if (open < 0) break;
                    int close = inner.IndexOf(']', open);
                    if (close < 0) break;

                    string entry = inner.Substring(open + 1, close - open - 1);
                    var parts = entry.Split(',');
                    if (parts.Length >= 4 &&
                        int.TryParse(parts[0].Trim(), out int x) &&
                        int.TryParse(parts[1].Trim(), out int y) &&
                        int.TryParse(parts[2].Trim(), out int z) &&
                        int.TryParse(parts[3].Trim(), out int gid))
                    {
                        if (x >= 0 && x < w && y >= 0 && y < h && z >= 0 && z < d)
                            result[x + y * w + z * w * h] = (uint)gid;
                    }
                    pos = close + 1;
                }
            }
            else
            {
                // Dict format: {"x,y,z": gid, ...}
                int depth = 0;
                int end = start;
                for (int i = start; i < json.Length; i++)
                {
                    if (json[i] == '{') depth++;
                    else if (json[i] == '}') { depth--; if (depth == 0) { end = i; break; } }
                }

                string inner = json.Substring(start + 1, end - start - 1);
                int pos = 0;
                while (pos < inner.Length)
                {
                    int q1 = inner.IndexOf('"', pos);
                    if (q1 < 0) break;
                    int q2 = inner.IndexOf('"', q1 + 1);
                    if (q2 < 0) break;

                    string key = inner.Substring(q1 + 1, q2 - q1 - 1);
                    int colon = inner.IndexOf(':', q2);
                    if (colon < 0) break;

                    int valStart = colon + 1;
                    while (valStart < inner.Length && (inner[valStart] == ' ' || inner[valStart] == '\t' || inner[valStart] == '\n' || inner[valStart] == '\r'))
                        valStart++;

                    int valEnd = valStart;
                    while (valEnd < inner.Length && inner[valEnd] != ',' && inner[valEnd] != '}' && inner[valEnd] != '\n')
                        valEnd++;

                    string valStr = inner.Substring(valStart, valEnd - valStart).Trim();

                    var keyParts = key.Split(',');
                    if (keyParts.Length == 3 &&
                        int.TryParse(keyParts[0], out int x) &&
                        int.TryParse(keyParts[1], out int y) &&
                        int.TryParse(keyParts[2], out int z) &&
                        int.TryParse(valStr, out int gid))
                    {
                        if (x >= 0 && x < w && y >= 0 && y < h && z >= 0 && z < d)
                            result[x + y * w + z * w * h] = (uint)gid;
                    }

                    pos = valEnd;
                }
            }

            return result;
        }

        static Dictionary<int, Vector3> ParsePivots(string json)
        {
            var result = new Dictionary<int, Vector3>();

            int idx = json.IndexOf("\"pivots\"");
            if (idx < 0) return result;
            int start = json.IndexOf('{', idx);
            if (start < 0) return result;

            int depth = 0;
            int end = start;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') { depth--; if (depth == 0) { end = i; break; } }
            }

            string inner = json.Substring(start + 1, end - start - 1);
            int pos = 0;
            while (pos < inner.Length)
            {
                int q1 = inner.IndexOf('"', pos);
                if (q1 < 0) break;
                int q2 = inner.IndexOf('"', q1 + 1);
                if (q2 < 0) break;

                string gidStr = inner.Substring(q1 + 1, q2 - q1 - 1);
                int objStart = inner.IndexOf('{', q2);
                if (objStart < 0) break;
                int objEnd = inner.IndexOf('}', objStart);
                if (objEnd < 0) break;

                string obj = inner.Substring(objStart + 1, objEnd - objStart - 1);

                float x = 0, y = 0, z = 0;
                ParseFloat(obj, "\"x\"", out x);
                ParseFloat(obj, "\"y\"", out y);
                ParseFloat(obj, "\"z\"", out z);

                if (int.TryParse(gidStr, out int gid))
                    result[gid] = new Vector3(x, y, z);

                pos = objEnd + 1;
            }

            return result;
        }

        static void ParseFloat(string json, string key, out float val)
        {
            val = 0;
            int idx = json.IndexOf(key);
            if (idx < 0) return;
            int colon = json.IndexOf(':', idx);
            if (colon < 0) return;
            int end = colon + 1;
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\n')
                end++;
            string numStr = json.Substring(colon + 1, end - colon - 1).Trim();
            float.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out val);
        }

        /// <summary>Extract the animParams sub-object as raw JSON string. Public for VoxelCharacter.</summary>
        public static string ExtractAnimParamsRaw(string json)
        {
            return ExtractJsonObject(json, "\"animParams\"");
        }

        static string ExtractJsonObject(string json, string key)
        {
            int idx = json.IndexOf(key);
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

        static Dictionary<string, int> ParseRegions(string json)
        {
            var result = new Dictionary<string, int>();

            int idx = json.IndexOf("\"regions\"");
            if (idx < 0) return result;
            int start = json.IndexOf('{', idx);
            if (start < 0) return result;

            int depth = 0;
            int end = start;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') { depth--; if (depth == 0) { end = i; break; } }
            }

            string inner = json.Substring(start + 1, end - start - 1);
            int pos = 0;
            while (pos < inner.Length)
            {
                int q1 = inner.IndexOf('"', pos);
                if (q1 < 0) break;
                int q2 = inner.IndexOf('"', q1 + 1);
                if (q2 < 0) break;

                string key = inner.Substring(q1 + 1, q2 - q1 - 1);
                int colon = inner.IndexOf(':', q2);
                if (colon < 0) break;

                int valStart = colon + 1;
                while (valStart < inner.Length && (inner[valStart] == ' ' || inner[valStart] == '\t' || inner[valStart] == '\n' || inner[valStart] == '\r'))
                    valStart++;

                int valEnd = valStart;
                while (valEnd < inner.Length && inner[valEnd] != ',' && inner[valEnd] != '}' && inner[valEnd] != '\n')
                    valEnd++;

                string valStr = inner.Substring(valStart, valEnd - valStart).Trim();

                if (int.TryParse(valStr, out int regionId))
                    result[key] = regionId;

                pos = valEnd;
            }

            return result;
        }

        static List<RegionDef> ParseRegionDefs(string json)
        {
            var result = new List<RegionDef>();

            int idx = json.IndexOf("\"regionDefs\"");
            if (idx < 0) return result;
            int start = json.IndexOf('[', idx);
            if (start < 0) return result;

            int depth = 0;
            int end = start;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']') { depth--; if (depth == 0) { end = i; break; } }
            }

            string inner = json.Substring(start + 1, end - start - 1);
            int pos = 0;
            while (pos < inner.Length)
            {
                int objStart = inner.IndexOf('{', pos);
                if (objStart < 0) break;
                int objEnd = inner.IndexOf('}', objStart);
                if (objEnd < 0) break;

                string obj = inner.Substring(objStart + 1, objEnd - objStart - 1);

                var def = new RegionDef();
                ParseIntField(obj, "\"id\"", out def.id);

                int nameIdx = obj.IndexOf("\"name\"");
                if (nameIdx >= 0)
                {
                    int nq1 = obj.IndexOf('"', nameIdx + 6);
                    int nq2 = obj.IndexOf('"', nq1 + 1);
                    if (nq1 >= 0 && nq2 > nq1)
                        def.name = obj.Substring(nq1 + 1, nq2 - nq1 - 1);
                }

                int descIdx = obj.IndexOf("\"desc\"");
                if (descIdx >= 0)
                {
                    int dq1 = obj.IndexOf('"', descIdx + 6);
                    int dq2 = obj.IndexOf('"', dq1 + 1);
                    if (dq1 >= 0 && dq2 > dq1)
                        def.desc = obj.Substring(dq1 + 1, dq2 - dq1 - 1);
                }

                result.Add(def);
                pos = objEnd + 1;
            }

            return result;
        }

        static void ParseIntField(string json, string key, out int val)
        {
            val = 0;
            int idx = json.IndexOf(key);
            if (idx < 0) return;
            int colon = json.IndexOf(':', idx);
            if (colon < 0) return;
            int end = colon + 1;
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\n')
                end++;
            string numStr = json.Substring(colon + 1, end - colon - 1).Trim();
            int.TryParse(numStr, out val);
        }

        #endregion
    }

    public class RegionDef
    {
        public int id;
        public string name;
        public string desc;
    }
}
