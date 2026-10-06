using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SteelCity.EditorTools
{
    /// <summary>
    /// Unity menu wrappers for Tools/generate_city_layout.py.
    /// The generator writes city_layout.json + city_template.json into
    /// StreamingAssets; CityMap3D reads them on the next Play (cachedLayout
    /// is loaded once per session, so re-activating mid-Play won't rebuild).
    /// </summary>
    public static class CityLayoutGeneratorMenu
    {
        private const string ScriptRelPath = "Tools/generate_city_layout.py";

        [MenuItem("Steel City/City Layout/Generate + Activate: Straight-River Test (10x10)")]
        public static void ActivateRiverTest()
        {
            RunGenerator("--size 10 --river-row 6 --bridge-cols 3,7 --activate",
                "Straight-river test layout activated (river on row 6, bridges at cols 3 and 7).");
        }

        [MenuItem("Steel City/City Layout/Generate + Activate: Full Replica (32x32)")]
        public static void ActivateReplica()
        {
            if (!EditorUtility.DisplayDialog("Generate Replica Layout",
                "Generate the 32x32 replica (642 land / 220 mainstreet / 34 water / 4 bridge / 124 oob) " +
                "and activate it as city_layout.json?\n\nThis is the heavyweight build.",
                "Generate + Activate", "Cancel"))
                return;
            RunGenerator("--replica --activate",
                "32x32 replica layout activated (terrain + seam rows included).");
        }

        [MenuItem("Steel City/City Layout/Restore Stock 10x10")]
        public static void RestoreStock()
        {
            RunGenerator("--restore", "Restored stock 10x10 layout from backup.");
        }

        [MenuItem("Steel City/City Layout/Generate Only: Replica JSON (no activate)")]
        public static void GenerateReplicaNoActivate()
        {
            RunGenerator("--replica", "Replica written to city_layout_32_replica.json (not activated).");
        }

        private static void RunGenerator(string args, string okSummary)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string script = Path.Combine(projectRoot, ScriptRelPath);
            if (!File.Exists(script))
            {
                EditorUtility.DisplayDialog("City Layout Generator",
                    $"Script not found:\n{script}", "OK");
                return;
            }

            string python = FindPython();
            if (python == null)
            {
                EditorUtility.DisplayDialog("City Layout Generator",
                    "Python not found on PATH (tried 'python' and 'py').\n" +
                    $"Run manually:\npython {ScriptRelPath} {args}", "OK");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = python,
                Arguments = $"\"{script}\" {args}",
                WorkingDirectory = projectRoot,
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
                    p.WaitForExit(60000);

                    if (!string.IsNullOrEmpty(stdout))
                        Debug.Log($"[CityLayoutGen] {stdout.Trim()}");
                    if (!string.IsNullOrEmpty(stderr))
                        Debug.LogWarning($"[CityLayoutGen] stderr: {stderr.Trim()}");

                    if (p.ExitCode != 0)
                    {
                        EditorUtility.DisplayDialog("City Layout Generator",
                            $"Generator exited with code {p.ExitCode}.\nSee Console for details.", "OK");
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("City Layout Generator",
                    $"Failed to launch Python:\n{e.Message}", "OK");
                return;
            }

            AssetDatabase.Refresh();

            string msg = okSummary;
            if (EditorApplication.isPlaying)
                msg += "\n\nNote: currently in Play mode — the active session keeps its loaded layout. " +
                       "Stop + Play to rebuild with the new layout.";
            EditorUtility.DisplayDialog("City Layout Generator", msg, "OK");
        }

        private static string FindPython()
        {
            foreach (var exe in new[] { "python", "py" })
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using (var p = Process.Start(psi))
                    {
                        p.WaitForExit(5000);
                        if (p.ExitCode == 0) return exe;
                    }
                }
                catch { }
            }
            return null;
        }
    }
}
