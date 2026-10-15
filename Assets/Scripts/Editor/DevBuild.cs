#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SteelCity.EditorTools
{
    /// <summary>
    /// One-click Development player build — for verifying performance claims that the
    /// editor profiler can't answer (URP GPU markers are unsupported, and UpdateScene /
    /// WaitForLastPresentation noise swamps the CPU view). A real build runs the same
    /// Debug HUD with its own FPS readout, so tab-cost hypotheses test directly.
    /// Output: Build/Dev/SteelCityMobSim.exe using the enabled Build Settings scenes.
    /// </summary>
    public static class DevBuild
    {
        private const string OutDir = "Build/Dev";
        private const string OutExe = OutDir + "/SteelCityMobSim.exe";

        [MenuItem("Steel City/Build Dev Player (Windows)")]
        public static void Build()
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes == null || scenes.Length == 0)
            {
                Debug.LogError("[DevBuild] No enabled scenes in Build Settings.");
                return;
            }
            foreach (var s in scenes) Debug.Log($"[DevBuild] scene: {s.path}");

            Directory.CreateDirectory(OutDir);
            var report = BuildPipeline.BuildPlayer(scenes, OutExe,
                BuildTarget.StandaloneWindows64,
                BuildOptions.Development | BuildOptions.ShowBuiltPlayer);

            var sum = report.summary;
            if (sum.result == BuildResult.Succeeded)
                Debug.Log($"[DevBuild] OK → {OutExe}  ({sum.totalSize / (1024f * 1024f):F0} MB, {sum.totalTime.TotalSeconds:F0}s)");
            else
                Debug.LogError($"[DevBuild] {sum.result} — {sum.totalErrors} error(s), see Console");
        }
    }
}
#endif
