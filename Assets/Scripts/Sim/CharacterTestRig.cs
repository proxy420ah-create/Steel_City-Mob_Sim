using UnityEngine;
using UnityEngine.InputSystem;

namespace SteelCity.Sim
{
    /// <summary>
    /// Isolated, minimal test harness for verifying a single character export from
    /// character_animator.html. Deliberately has NO dependency on CityMap3D,
    /// HoodSpawner, or VoxelCollisionWorld — it spawns exactly one character at a
    /// fixed Inspector-configurable world position, with a dedicated
    /// VoxelChunkManager instance if needed, so nothing from the city/hood
    /// pipeline can cross-contaminate this test.
    ///
    /// Sterile test scope: only T-Pose (no .anim.json / no pose params — the raw
    /// rest pose) and Idle (state 0) are exposed. Press R to respawn (re-reads
    /// the .stasset/.groups/.anim.json from disk fresh — no stale cache).
    /// </summary>
    public class CharacterTestRig : MonoBehaviour
    {
        [Header("Test Asset")]
        [Tooltip("Asset base filename in Assets/StreamingAssets/voxel_characters/ (with .stasset extension).")]
        [SerializeField] private string characterAsset = "character_test_vehicle.stasset";
        [Tooltip("Voxel size in world units. Must match the size used when the model was authored.")]
        [SerializeField] private float voxelSize = 0.015f;

        [Header("Placement")]
        [SerializeField] private Vector3 spawnPosition = new Vector3(0f, 0f, 0f);

        [Header("References")]
        [Tooltip("Leave empty to auto-find or auto-create a VoxelChunkManager in the scene.")]
        [SerializeField] private VoxelChunkManager chunkManager;

        private GameObject spawnedObj;
        private CharacterAnimation spawnedAnim;

        void Start()
        {
            if (chunkManager == null)
                chunkManager = FindFirstObjectByType<VoxelChunkManager>();
            if (chunkManager == null)
            {
                var go = new GameObject("VoxelChunkManager_TestRig");
                chunkManager = go.AddComponent<VoxelChunkManager>();
                Debug.Log("[CharacterTestRig] No VoxelChunkManager found in scene — created a standalone instance for this test.");
            }

            Spawn();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.rKey.wasPressedThisFrame)
            {
                Debug.Log("[CharacterTestRig] Respawn requested — re-reading files fresh from disk.");
                Respawn();
            }

            if (spawnedAnim == null) return;

            // Sterile scope: only Idle is wired. T-Pose is whatever the character
            // renders as when no .anim.json is present (raw rest pose, no pose
            // params/walk keyframes uploaded) — that's the natural state, not a
            // hotkey toggle, so there's no extra logic path to introduce bugs.
            if (kb.iKey.wasPressedThisFrame)
            {
                spawnedAnim.SetState(CharacterAnimation.AnimState.Idle);
                Debug.Log("[CharacterTestRig] State -> Idle");
            }
        }

        public void Spawn()
        {
            if (spawnedObj != null) return;

            spawnedObj = new GameObject("CharacterTestRig_Character");
            spawnedObj.transform.SetParent(transform, false);

            var vc = spawnedObj.AddComponent<VoxelCharacter>();
            vc.assetFileName = characterAsset;
            vc.voxelSize = voxelSize;
            vc.chunkManager = chunkManager;
            vc.centerPosition = spawnPosition;
            vc.useWorldPosition = false;
            vc.showGizmo = true;
            vc.showGroundProbe = false;

            var anim = spawnedObj.AddComponent<CharacterAnimation>();
            anim.autoDetectWalking = false; // manual control only — no drift into Walking state
            anim.SetState(CharacterAnimation.AnimState.Idle);
            spawnedAnim = anim;

            Debug.Log($"[CharacterTestRig] Spawned '{characterAsset}' at {spawnPosition}. Press I for Idle, R to respawn (fresh read from disk).");
        }

        public void Despawn()
        {
            if (spawnedObj != null)
            {
                Destroy(spawnedObj);
                spawnedObj = null;
                spawnedAnim = null;
            }
        }

        public void Respawn()
        {
            Despawn();
            Spawn();
        }
    }
}
