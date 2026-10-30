using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SteelCity.EditorTools
{
    /// <summary>
    /// Unity menu wrapper for Tools/json_to_stasset.py — the WIP → runtime
    /// conversion step of the building pipeline:
    ///
    ///   VoxelAssetStudio/JSON Models In Progress/*.json  (editable source)
    ///     →  Assets/StreamingAssets/voxel_buildings/*.stasset  (runtime)
    ///
    /// Converts only assetType=="building" files (characters/vehicles/items
    /// live in other folders with their own import paths). Stale check:
    /// JSON newer than the .stasset (or stasset missing) → reconvert.
    ///
    /// NOTE: json_to_stasset takes the output path as a POSITIONAL argument —
    /// there is no -o flag; pass dst as the second arg or the file lands in
    /// the CWD named after whatever flag was used (this bit us once).
    /// </summary>
    public static class StassetBuildMenu
    {
        private const string ScriptRelPath = "Tools/json_to_stasset.py";
        private const string WipRelDir = "VoxelAssetStudio/JSON Models In Progress";
        private const string OutRelDir = "Assets/StreamingAssets/voxel_buildings";

        [MenuItem("Steel City/Voxel Assets/Convert WIP → Stassets (stale only)")]
        public static void ConvertStale()
        {
            Run(forceAll: false);
        }

        [MenuItem("Steel City/Voxel Assets/Convert WIP → Stassets (force all)")]
        public static void ConvertAll()
        {
            Run(forceAll: true);
        }

        private static void Run(bool forceAll)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string script = Path.Combine(projectRoot, ScriptRelPath);
            string wipDir = Path.Combine(projectRoot, WipRelDir);
            string outDir = Path.Combine(projectRoot, OutRelDir);

            if (!File.Exists(script)) { EditorUtility.DisplayDialog("Stassets",
                $"Converter not found:\n{script}", "OK"); return; }
            if (!Directory.Exists(wipDir)) { EditorUtility.DisplayDialog("Stassets",
                $"WIP folder not found:\n{wipDir}", "OK"); return; }
            Directory.CreateDirectory(outDir);

            string python = FindPython();
            if (python == null)
            {
                EditorUtility.DisplayDialog("Stassets",
                    "Python not found on PATH (tried 'python' and 'py').\n" +
                    $"Run manually:\npython {ScriptRelPath} <src.json> <dst.stasset>", "OK");
                return;
            }

            var converted = new List<string>();
            var skipped = new List<string>();
            var nonBuilding = new List<string>();
            var failed = new List<string>();

            foreach (string jsonPath in Directory.GetFiles(wipDir, "*.json"))
            {
                string stem = Path.GetFileNameWithoutExtension(jsonPath);
                if (ReadAssetType(jsonPath) != "building")
                {
                    nonBuilding.Add(stem);
                    continue;
                }

                string dst = Path.Combine(outDir, stem + ".stasset");
                bool stale = forceAll || !File.Exists(dst) ||
                             File.GetLastWriteTimeUtc(jsonPath) > File.GetLastWriteTimeUtc(dst);
                if (!stale) { skipped.Add(stem); continue; }

                if (Convert(python, script, projectRoot, jsonPath, dst, stem))
                    converted.Add(stem);
                else
                    failed.Add(stem);
            }

            AssetDatabase.Refresh();

            string summary = $"Converted {converted.Count} · skipped {skipped.Count} up-to-date" +
                             (nonBuilding.Count > 0 ? $" · {nonBuilding.Count} non-building ignored" : "") +
                             (failed.Count > 0 ? $"\n\nFAILED: {string.Join(", ", failed)}" : "");
            if (converted.Count > 0)
                Debug.Log($"[Stassets] Converted: {string.Join(", ", converted)}");
            if (failed.Count > 0)
                Debug.LogError($"[Stassets] FAILED: {string.Join(", ", failed)} — see console stderr");

            string msg = summary;
            if (EditorApplication.isPlaying)
                msg += "\n\nNote: in Play mode — the active session keeps its baked sectors. " +
                       "Stop + Play to rebuild with the new assets.";
            EditorUtility.DisplayDialog("Stassets", msg, "OK");
        }

        private static bool Convert(string python, string script, string workDir,
                                    string src, string dst, string stem)
        {
            var psi = new ProcessStartInfo
            {
                FileName = python,
                Arguments = $"\"{script}\" \"{src}\" \"{dst}\"",
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit(120000);
                    if (p.ExitCode == 0 && File.Exists(dst))
                    {
                        if (!string.IsNullOrEmpty(stderr))
                            Debug.LogWarning($"[Stassets] {stem} stderr: {stderr.Trim()}");
                        return true;
                    }
                    Debug.LogError($"[Stassets] {stem}: exit {p.ExitCode}\n{stderr.Trim()}");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Stassets] {stem}: launch failed — {e.Message}");
                return false;
            }
        }

        // Cheap header sniff — only needs assetType, avoids a full JSON parse
        // of multi-MB voxel lists.
        private static string ReadAssetType(string jsonPath)
        {
            try
            {
                using (var r = new StreamReader(jsonPath))
                {
                    char[] buf = new char[4096];
                    int n = r.ReadBlock(buf, 0, buf.Length);
                    string head = new string(buf, 0, n);
                    var m = Regex.Match(head, "\"assetType\"\\s*:\\s*\"(\\w+)\"");
                    return m.Success ? m.Groups[1].Value : null;
                }
            }
            catch { return null; }
        }

        private static string FindPython()
        {
            foreach (var cand in new[] { "python", "py" })
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = cand, Arguments = "--version",
                        RedirectStandardOutput = true, RedirectStandardError = true,
                        UseShellExecute = false, CreateNoWindow = true,
                    };
                    using (var p = Process.Start(psi))
                    {
                        p.WaitForExit(5000);
                        if (p.ExitCode == 0) return cand;
                    }
                }
                catch { /* not on PATH */ }
            }
            return null;
        }
    }
}
