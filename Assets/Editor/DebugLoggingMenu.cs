using UnityEditor;
using UnityEngine;
using SteelCity.Sim;

namespace SteelCity.EditorTools
{
    /// <summary>
    /// Steel City/Debug — toggles verbose diagnostics on the debug renderers
    /// (PathDebugRenderer + VoxelRenderBridge). Works in edit and play mode;
    /// renderers spawned later inherit VerboseLoggingDefault.
    /// </summary>
    public static class DebugLoggingMenu
    {
        private const string MenuPath = "Steel City/Debug/Verbose Path Logging";

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            bool next = !PathDebugRenderer.VerboseLoggingDefault;
            PathDebugRenderer.VerboseLoggingDefault = next;

            int n = 0;
            foreach (var pdr in Object.FindObjectsByType<PathDebugRenderer>(FindObjectsSortMode.None))
            {
                pdr.VerboseLogging = next;
                n++;
            }
            foreach (var vrb in Object.FindObjectsByType<VoxelRenderBridge>(FindObjectsSortMode.None))
            {
                vrb.VerboseLogging = next;
                n++;
            }
            Debug.Log($"[DebugLogging] Verbose path logging {(next ? "ON" : "OFF")} ({n} live renderer(s) updated)");
        }

        [MenuItem(MenuPath, true)]
        private static bool Validate()
        {
            Menu.SetChecked(MenuPath, PathDebugRenderer.VerboseLoggingDefault);
            return true;
        }
    }
}
