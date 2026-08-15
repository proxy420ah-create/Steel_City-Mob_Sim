#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace SteelCity.Sim
{
    [CustomEditor(typeof(ClothingTestSpawner))]
    public class ClothingTestSpawnerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var spawner = (ClothingTestSpawner)target;
            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(spawner.IsSpawned);
            if (GUILayout.Button("Spawn Characters", GUILayout.Height(28)))
            {
                spawner.Spawn();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!spawner.IsSpawned);
            if (GUILayout.Button("Despawn Characters", GUILayout.Height(28)))
            {
                spawner.Despawn();
            }

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Re-Apply Outfits", GUILayout.Height(28)))
            {
                spawner.ApplyOutfits();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(8);

            if (spawner.IsSpawned)
            {
                EditorGUILayout.HelpBox(
                    "Characters are spawned.\n" +
                    "Change outfit presets above, then click 'Re-Apply Outfits'.\n" +
                    "Use Debug HUD (O key) → Clothing tab for per-instance control.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No characters spawned yet.\n" +
                    "Click 'Spawn Characters' or enable 'Auto Spawn On Start'.",
                    MessageType.None);
            }
        }
    }
}
#endif
