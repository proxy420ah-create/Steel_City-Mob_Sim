using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SteelCity.EditorTools
    {
    public enum AssetType
    {
        Character,
        Building,
        Accessory,
        Decor
    }

    /// <summary>
    /// Category-aware voxel asset importer.
    /// Reads a Project JSON from the HTML animator and produces .stasset/.groups/.anim.json
    /// in the appropriate StreamingAssets subfolder.
    /// </summary>
    public static class ImportVoxelAsset
    {
        [MenuItem("Tools/Voxel Import/Character")]
        public static void ImportCharacter() => Import(AssetType.Character);

        [MenuItem("Tools/Voxel Import/Building")]
        public static void ImportBuilding() => Import(AssetType.Building);

        [MenuItem("Tools/Voxel Import/Accessory")]
        public static void ImportAccessory() => Import(AssetType.Accessory);

        [MenuItem("Tools/Voxel Import/Decor")]
        public static void ImportDecor() => Import(AssetType.Decor);

        private static readonly string[] Folders =
        {
            "voxel_characters",
            "voxel_buildings",
            "voxel_accessories",
            "voxel_decor"
        };

        private static readonly string[] Labels =
        {
            "Character",
            "Building",
            "Accessory",
            "Decor"
        };

        public static void Import(AssetType type)
        {
            int t = (int)type;
            string folder = Folders[t];
            string label = Labels[t];

            // Open file panel starting in the correct folder (if it exists)
            string startDir = Path.Combine(Application.dataPath, "StreamingAssets", folder);
            if (!Directory.Exists(startDir))
                startDir = Path.Combine(Application.dataPath, "StreamingAssets");

            string sourcePath = EditorUtility.OpenFilePanel(
                $"Select {label} Project JSON",
                startDir,
                "json");

            if (string.IsNullOrEmpty(sourcePath))
                return;

            if (!File.Exists(sourcePath))
            {
                EditorUtility.DisplayDialog("Error", $"File not found:\n{sourcePath}", "OK");
                return;
            }

            string jsonText = File.ReadAllText(sourcePath);
            var data = JsonUtility.FromJson<ProjectJSON>(jsonText);

            // Fallback: try manual parse if JsonUtility fails on nested arrays
            if (data == null || data.dims == null || data.dims.Length < 3)
            {
                data = ParseManual(jsonText);
            }

            if (data == null || data.dims == null || data.dims.Length < 3)
            {
                EditorUtility.DisplayDialog("Error", "Failed to parse JSON. Ensure it's a Project JSON from the animator.", "OK");
                return;
            }

            // Always manually parse ALL nested fields — JsonUtility CANNOT correctly parse
            // int-keyed dicts (pivots), nested anonymous objects (animParams), or arrays-of-arrays
            // (voxels/groups: [[x,y,z,mid],...]). Critically, JsonUtility does NOT fail loudly on
            // these — it silently produces zero-filled entries matching the outer array length
            // (e.g. VoxelEntry[1544] where every x/y/z/mid == 0), so a null/length check is not
            // sufficient to detect the failure. Always override with manual parsing.
            data.pivots = ParsePivotsArray(jsonText) ?? data.pivots;
            data.animParamsRaw = ExtractJsonObject(jsonText, "\"animParams\"");
            data.animParams = data.animParamsRaw != null ? new object() : null;
            data.voxels = ParseVoxelArray(jsonText) ?? data.voxels;
            data.groups = ParseGroupArray(jsonText) ?? data.groups;

            int w = data.dims[0], h = data.dims[1], d = data.dims[2];

            // Ask for output name
            string defaultName = Path.GetFileNameWithoutExtension(sourcePath);
            // Clean up common prefixes from the animator's auto-naming
            if (defaultName.StartsWith("character_project_"))
                defaultName = "character";

            string outputName = EditorInputDialog.Show(
                $"{label} Asset Name",
                $"Enter the {label.ToLower()} asset name (no extension):",
                defaultName);

            if (string.IsNullOrEmpty(outputName))
                return;

            string outDir = Path.Combine(Application.dataPath, "StreamingAssets", folder);
            Directory.CreateDirectory(outDir);

            string stassetPath = Path.Combine(outDir, outputName + ".stasset");
            string groupsPath = Path.Combine(outDir, outputName + ".groups");
            string animPath = Path.Combine(outDir, outputName + ".anim.json");

            // --- 1. Write .stasset ---
            int voxelCount = data.voxels != null ? data.voxels.Length : 0;
            WriteStasset(stassetPath, w, h, d, data.voxels);
            Debug.Log($"[VoxelImport] Wrote {stassetPath} ({voxelCount} voxels, {w}x{h}x{d})");

            // --- 2. Write .groups ---
            int groupCount = 0;
            if (data.groups != null && data.groups.Length > 0)
            {
                groupCount = data.groups.Length;
                WriteGroups(groupsPath, w, h, d, data.groups);
                Debug.Log($"[VoxelImport] Wrote {groupsPath} ({groupCount} group assignments)");
            }
            else
            {
                Debug.Log("[VoxelImport] No group data — skipping .groups file");
            }

            // --- 3. Write .anim.json (only for characters) ---
            bool hasAnim = (data.animParams != null || data.pivots != null);
            if (hasAnim && type == AssetType.Character)
            {
                WriteAnimJson(animPath, data);
                Debug.Log($"[VoxelImport] Wrote {animPath}");
            }
            else if (hasAnim)
            {
                Debug.Log($"[VoxelImport] Skipping .anim.json — {label}s don't use animation");
            }

            AssetDatabase.Refresh();

            // Build summary message
            string summary = $"Imported '{outputName}' to {folder}/\n\n" +
                $"  .stasset: {voxelCount} voxels ({w}x{h}x{d})\n" +
                $"  .groups: {groupCount} assignments\n" +
                $"  .anim.json: {(hasAnim && type == AssetType.Character ? "yes" : "skipped")}";

            if (type == AssetType.Character)
                summary += $"\n\nSet Asset Base Name = \"{outputName}\" on ForwardTransformTestRig.";
            else if (type == AssetType.Building)
                summary += $"\n\nAsset will appear in city layout as '{outputName}.stasset'.";
            else
                summary += $"\n\n{label} asset ready in StreamingAssets/{folder}/.";

            EditorUtility.DisplayDialog($"{label} Import Complete", summary, "OK");
        }

        #region Binary Writers

        static void WriteStasset(string path, int w, int h, int d, VoxelEntry[] voxels)
        {
            using var fs = new FileStream(path, FileMode.Create);
            using var bw = new BinaryWriter(fs);

            // Header: magic(4) + version(1) + flags(1) + w(2) + h(2) + d(2) + reserved(4) = 16 bytes
            bw.Write((byte)'S'); bw.Write((byte)'T'); bw.Write((byte)'A'); bw.Write((byte)'S');
            bw.Write((byte)1);   // version
            bw.Write((byte)0);   // flags
            bw.Write((ushort)w);
            bw.Write((ushort)h);
            bw.Write((ushort)d);
            bw.Write(0); // 4 bytes reserved

            // Voxel data: ushort per voxel, X-major order (x + y*w + z*w*h)
            var grid = new ushort[w * h * d];
            if (voxels != null)
            {
                foreach (var v in voxels)
                {
                    if (v.x >= 0 && v.x < w && v.y >= 0 && v.y < h && v.z >= 0 && v.z < d)
                        grid[v.x + v.y * w + v.z * w * h] = (ushort)v.mid;
                }
            }

            for (int i = 0; i < grid.Length; i++)
                bw.Write(grid[i]);
        }

        static void WriteGroups(string path, int w, int h, int d, GroupEntry[] groups)
        {
            using var fs = new FileStream(path, FileMode.Create);
            using var bw = new BinaryWriter(fs);

            // Header: STAG magic
            bw.Write((byte)'S'); bw.Write((byte)'T'); bw.Write((byte)'A'); bw.Write((byte)'G');
            bw.Write((byte)1);   // version
            bw.Write((byte)0);   // flags
            bw.Write((ushort)w);
            bw.Write((ushort)h);
            bw.Write((ushort)d);
            bw.Write(0); // 4 bytes reserved

            // Group data: ushort per voxel, X-major order
            var grid = new ushort[w * h * d];
            if (groups != null)
            {
                foreach (var g in groups)
                {
                    if (g.x >= 0 && g.x < w && g.y >= 0 && g.y < h && g.z >= 0 && g.z < d)
                        grid[g.x + g.y * w + g.z * w * h] = (ushort)g.gid;
                }
            }

            for (int i = 0; i < grid.Length; i++)
                bw.Write(grid[i]);
        }

        static void WriteAnimJson(string path, ProjectJSON data)
        {
            // Re-serialize as clean anim params JSON
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"format\": \"anim_params\",\n");
            sb.Append("  \"version\": 1,\n");

            // Pivots
            sb.Append("  \"pivots\": ");
            if (data.pivots != null && data.pivots.Length > 0)
            {
                sb.Append("{\n");
                for (int i = 0; i < data.pivots.Length; i++)
                {
                    var p = data.pivots[i];
                    sb.Append($"    \"{p.gid}\": {{\"x\": {p.x}, \"y\": {p.y}, \"z\": {p.z}}}");
                    if (i < data.pivots.Length - 1) sb.Append(",");
                    sb.Append("\n");
                }
                sb.Append("  },\n");
            }
            else
            {
                sb.Append("{},\n");
            }

            // Params — embed raw JSON if available, otherwise empty
            sb.Append("  \"params\": ");
            if (data.animParams != null && !string.IsNullOrEmpty(data.animParamsRaw))
            {
                sb.Append(data.animParamsRaw);
            }
            else
            {
                sb.Append("{}");
            }
            sb.Append("\n}\n");

            File.WriteAllText(path, sb.ToString());
        }

        #endregion

        #region Manual JSON Parser (fallback for nested arrays JsonUtility can't handle)

        static ProjectJSON ParseManual(string jsonText)
        {
            var result = new ProjectJSON();

            // Parse dims
            result.dims = ParseIntArray(jsonText, "\"dims\"");
            if (result.dims == null || result.dims.Length < 3) return null;

            // Parse voxels array
            result.voxels = ParseVoxelArray(jsonText);

            // Parse groups object
            result.groups = ParseGroupArray(jsonText);

            // Parse pivots object
            result.pivots = ParsePivotsArray(jsonText);

            // Parse animParams (raw substring)
            result.animParamsRaw = ExtractJsonObject(jsonText, "\"animParams\"");
            result.animParams = result.animParamsRaw != null ? new object() : null;

            return result;
        }

        static int[] ParseIntArray(string json, string key)
        {
            int idx = json.IndexOf(key);
            if (idx < 0) return null;
            int start = json.IndexOf('[', idx);
            int end = json.IndexOf(']', start);
            if (start < 0 || end < 0) return null;

            string inner = json.Substring(start + 1, end - start - 1);
            var parts = inner.Split(',');
            var list = new System.Collections.Generic.List<int>();
            foreach (var p in parts)
            {
                if (int.TryParse(p.Trim(), out int val))
                    list.Add(val);
            }
            return list.ToArray();
        }

        static VoxelEntry[] ParseVoxelArray(string json)
        {
            int idx = json.IndexOf("\"voxels\"");
            if (idx < 0) return null;
            int start = json.IndexOf('[', idx);
            // Find matching close bracket
            int depth = 0;
            int end = start;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']') { depth--; if (depth == 0) { end = i; break; } }
            }

            string inner = json.Substring(start + 1, end - start - 1);
            var list = new System.Collections.Generic.List<VoxelEntry>();
            // Each voxel is [x,y,z,mid]
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
                    list.Add(new VoxelEntry { x = x, y = y, z = z, mid = mid });
                }
                pos = close + 1;
            }
            return list.ToArray();
        }

        static GroupEntry[] ParseGroupArray(string json)
        {
            int idx = json.IndexOf("\"groups\"");
            if (idx < 0) return null;

            // Check if groups is an array [...] or object {...}
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

            if (isArray)
            {
                // Array format: [[x,y,z,gid], ...] from saveProject()
                int depth = 0;
                int end = start;
                for (int i = start; i < json.Length; i++)
                {
                    if (json[i] == '[') depth++;
                    else if (json[i] == ']') { depth--; if (depth == 0) { end = i; break; } }
                }

                string inner = json.Substring(start + 1, end - start - 1);
                var list = new System.Collections.Generic.List<GroupEntry>();
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
                        list.Add(new GroupEntry { x = x, y = y, z = z, gid = gid });
                    }
                    pos = close + 1;
                }
                return list.ToArray();
            }
            else
            {
                // Dict format: {"x,y,z": gid, ...} from exportGroupsJSON()
                int depth = 0;
                int end = start;
                for (int i = start; i < json.Length; i++)
                {
                    if (json[i] == '{') depth++;
                    else if (json[i] == '}') { depth--; if (depth == 0) { end = i; break; } }
                }

                string inner = json.Substring(start + 1, end - start - 1);
                var list = new System.Collections.Generic.List<GroupEntry>();

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
                        list.Add(new GroupEntry { x = x, y = y, z = z, gid = gid });
                    }

                    pos = valEnd;
                }
                return list.ToArray();
            }
        }

        static PivotEntry[] ParsePivotsArray(string json)
        {
            int idx = json.IndexOf("\"pivots\"");
            if (idx < 0) return null;
            int start = json.IndexOf('{', idx);
            if (start < 0) return null;

            int depth = 0;
            int end = start;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') { depth--; if (depth == 0) { end = i; break; } }
            }

            string inner = json.Substring(start + 1, end - start - 1);
            var list = new System.Collections.Generic.List<PivotEntry>();

            // Parse "gid": {"x": val, "y": val, "z": val}
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
                {
                    list.Add(new PivotEntry { gid = gid, x = x, y = y, z = z });
                }

                pos = objEnd + 1;
            }
            return list.ToArray();
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
            float.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val);
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

        #endregion

        #region Data Classes

        [Serializable]
        public class ProjectJSON
        {
            public int[] dims;
            public VoxelEntry[] voxels;
            public GroupEntry[] groups;
            public PivotEntry[] pivots;
            public object animParams;
            public string animParamsRaw;
        }

        [Serializable]
        public class VoxelEntry
        {
            public int x, y, z, mid;
        }

        [Serializable]
        public class GroupEntry
        {
            public int x, y, z, gid;
        }

        [Serializable]
        public class PivotEntry
        {
            public int gid;
            public float x, y, z;
        }

        #endregion
    }

    /// <summary>
    /// Simple input dialog for getting a string from the user.
    /// </summary>
    public static class EditorInputDialog
    {
        public static string Show(string title, string message, string defaultValue)
        {
            var window = EditorWindow.GetWindow<DialogWindow>(true);
            window.titleContent = new GUIContent(title);
            window.message = message;
            window.value = defaultValue;
            window.ShowModal();

            return window.result;
        }

        private class DialogWindow : EditorWindow
        {
            public string message;
            public string value;
            public string result;

            private bool firstFrame = true;

            void OnGUI()
            {
                GUILayout.Space(10);
                GUILayout.Label(message);
                GUILayout.Space(5);

                GUI.SetNextControlName("TextField");
                value = GUILayout.TextField(value);
                GUILayout.Space(10);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Cancel"))
                {
                    result = null;
                    Close();
                }
                if (GUILayout.Button("OK") || (Event.current.isKey && Event.current.keyCode == KeyCode.Return))
                {
                    result = value;
                    Close();
                }
                GUILayout.EndHorizontal();

                if (firstFrame)
                {
                    GUI.FocusControl("TextField");
                    firstFrame = false;
                }
            }

        }
    }
}
