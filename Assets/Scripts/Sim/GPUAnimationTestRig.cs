using UnityEngine;
using UnityEngine.InputSystem;

namespace SteelCity.Sim
{
    /// <summary>
    /// Clean GPU instanced animation test rig.
    /// Mirrors AnimationTestSpawner but uses the GPU instanced path
    /// (VoxelCharacter + CharacterAnimation + shader inverse-transform).
    ///
    /// No city integration, no ground probing, no camera tricks.
    /// Places adjacent to HoodSpawner for side-by-side comparison.
    ///
    /// Hotkeys (same as AnimationTestSpawner):
    ///   T = T-Pose (9), I = Idle (0), W = Walking (1),
    ///   L = Looking (2), A = Aiming (4), C = Crouching (5)
    ///   Space = Play/Pause, +/- = Speed, R = Reload
    /// </summary>
    public class GPUAnimationTestRig : MonoBehaviour
    {
        [Header("Test Asset")]
        [Tooltip("Base filename in StreamingAssets/voxel_characters/ (without extension).")]
        [SerializeField] private string assetBaseName = "animationtest1";

        [Header("Rendering")]
        [Tooltip("Voxel size in world units. Must match authoring.")]
        [SerializeField] private float voxelSize = 0.02f;
        [Tooltip("VoxelChunkManager for raymarch rendering. Auto-found if not assigned.")]
        [SerializeField] private VoxelChunkManager chunkManager;

        [Header("Position")]
        [Tooltip("Fixed spawn position (world space). No ground probe.")]
        [SerializeField] private Vector3 spawnPosition = new Vector3(0f, 0.1f, 0f);

        private VoxelCharacter voxelChar;
        private CharacterAnimation anim;
        private bool isPlaying = true;
        private float animSpeed = 1f;
        private float currentAnimState = 9f; // T-Pose

        private static readonly string[] STATE_NAMES = {
            "Idle", "Walking", "Looking", "AimWalk", "Aiming",
            "Crouching", "???", "???", "Down", "T-Pose"
        };

        void Start()
        {
            if (chunkManager == null)
                chunkManager = FindFirstObjectByType<VoxelChunkManager>();

            StartCoroutine(DelayedSpawn());
        }

        private System.Collections.IEnumerator DelayedSpawn()
        {
            // Wait for city build + HoodSpawner to complete
            yield return null;
            yield return null;
            yield return null;
            SpawnGPUCharacter();
        }

        void Update()
        {
            if (voxelChar == null || anim == null) return;

            HandleInput();

            if (isPlaying)
            {
                // CharacterAnimation.Update handles animTime internally,
                // but we need to sync animSpeed
                anim.walkSpeed = animSpeed;
            }
        }

        void HandleInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.tKey.wasPressedThisFrame) { SetState(9f); }
            if (kb.iKey.wasPressedThisFrame) { SetState(0f); }
            if (kb.wKey.wasPressedThisFrame) { SetState(1f); }
            if (kb.lKey.wasPressedThisFrame) { SetState(2f); }
            if (kb.aKey.wasPressedThisFrame) { SetState(4f); }
            if (kb.cKey.wasPressedThisFrame) { SetState(5f); }

            if (kb.spaceKey.wasPressedThisFrame)
            {
                isPlaying = !isPlaying;
                Debug.Log($"[GPUAnim] {(isPlaying ? "Playing" : "Paused")}");
                // Toggle by setting walkSpeed to 0 when paused
                anim.walkSpeed = isPlaying ? animSpeed : 0f;
            }

            if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame)
            {
                animSpeed = Mathf.Min(animSpeed + 0.25f, 4f);
                Debug.Log($"[GPUAnim] Speed = {animSpeed}");
                if (isPlaying) anim.walkSpeed = animSpeed;
            }
            if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame)
            {
                animSpeed = Mathf.Max(animSpeed - 0.25f, 0.1f);
                Debug.Log($"[GPUAnim] Speed = {animSpeed}");
                if (isPlaying) anim.walkSpeed = animSpeed;
            }
        }

        void SetState(float state)
        {
            currentAnimState = state;
            int stateInt = Mathf.RoundToInt(state);
            var animState = (CharacterAnimation.AnimState)stateInt;
            this.anim.SetState(animState);
            Debug.Log($"[GPUAnim] State -> {STATE_NAMES[stateInt]} ({stateInt})");
        }

        void SpawnGPUCharacter()
        {
            if (chunkManager == null)
            {
                Debug.LogError("[GPUAnim] No VoxelChunkManager found!");
                return;
            }

            Vector3 spawnPos = spawnPosition;

            // Parent under Characters hierarchy
            var cityMap = FindFirstObjectByType<CityMap3D>();
            Transform charParent = null;
            if (cityMap != null && cityMap.MapRoot != null)
            {
                charParent = cityMap.MapRoot.Find("Characters");
                if (charParent == null)
                {
                    var cp = new GameObject("Characters");
                    cp.transform.SetParent(cityMap.MapRoot, false);
                    charParent = cp.transform;
                }
            }

            // Create character GameObject
            var charObj = new GameObject($"GPU_AnimTest_{assetBaseName}");
            if (charParent != null)
                charObj.transform.SetParent(charParent, false);
            charObj.transform.position = spawnPos;

            // Add VoxelCharacter (GPU instanced path)
            voxelChar = charObj.AddComponent<VoxelCharacter>();
            voxelChar.assetFileName = assetBaseName + ".stasset";
            voxelChar.voxelSize = voxelSize;
            voxelChar.chunkManager = chunkManager;
            voxelChar.useInstancing = true;
            voxelChar.useWorldPosition = false;
            voxelChar.centerPosition = spawnPos;
            voxelChar.showGizmo = true;
            voxelChar.showGroundProbe = false;

            // Add CharacterAnimation for state control
            anim = charObj.AddComponent<CharacterAnimation>();
            anim.autoDetectWalking = false; // manual control only
            anim.walkSpeed = animSpeed;
            anim.SetState(CharacterAnimation.AnimState.TPose);

            Debug.Log($"[GPUAnim] Spawned GPU instanced character at {spawnPos} " +
                      $"(asset={assetBaseName}.stasset, voxelSize={voxelSize})");
            Debug.Log("[GPUAnim] Hotkeys: T=TPose I=Idle W=Walk L=Look A=Aim C=Crouch " +
                      "Space=Play/Pause +/-=Speed");
        }
    }
}
