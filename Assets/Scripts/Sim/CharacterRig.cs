using UnityEngine;
using UnityEngine.InputSystem;

namespace SteelCity.Sim
{
    /// <summary>
    /// Character animation rig — controls Vinny's animation state via hotkeys.
    /// Uses GPU instanced rendering path (VoxelCharacter + CharacterAnimation).
    ///
    /// Hotkeys:
    ///   T = T-Pose (9), I = Idle (0), W = Walking (1),
    ///   L = Looking (2), A = Aiming (4), C = Crouching (5)
    ///   Space = Play/Pause, +/- = Speed
    /// </summary>
    public class CharacterRig : MonoBehaviour
    {
        [Header("Character Asset")]
        [Tooltip("Base filename in StreamingAssets/voxel_characters/ (without extension).")]
        [SerializeField] private string assetBaseName = "Vinny";

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

        /// <summary>Exposes the VoxelCharacter for external systems (e.g. CityMap3D.SpawnedCharacter).</summary>
        public VoxelCharacter Character => voxelChar;

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
            yield return null;
            yield return null;
            yield return null;
            InitCharacter();
        }

        void Update()
        {
            if (voxelChar == null || anim == null) return;

            HandleInput();

            if (isPlaying)
            {
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
                Debug.Log($"[CharRig] {(isPlaying ? "Playing" : "Paused")}");
                anim.walkSpeed = isPlaying ? animSpeed : 0f;
            }

            if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame)
            {
                animSpeed = Mathf.Min(animSpeed + 0.25f, 4f);
                Debug.Log($"[CharRig] Speed = {animSpeed}");
                if (isPlaying) anim.walkSpeed = animSpeed;
            }
            if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame)
            {
                animSpeed = Mathf.Max(animSpeed - 0.25f, 0.1f);
                Debug.Log($"[CharRig] Speed = {animSpeed}");
                if (isPlaying) anim.walkSpeed = animSpeed;
            }
        }

        void SetState(float state)
        {
            currentAnimState = state;
            int stateInt = Mathf.RoundToInt(state);
            var animState = (CharacterAnimation.AnimState)stateInt;
            anim.SetState(animState);
            Debug.Log($"[CharRig] State -> {STATE_NAMES[stateInt]} ({stateInt})");
        }

        /// <summary>
        /// Adds VoxelCharacter + CharacterAnimation to this same GameObject.
        /// No separate child object — everything lives on one entity.
        /// </summary>
        void InitCharacter()
        {
            if (chunkManager == null)
            {
                Debug.LogError("[CharRig] No VoxelChunkManager found!");
                return;
            }

            transform.position = spawnPosition;

            // Add VoxelCharacter (GPU instanced path) on same GameObject
            voxelChar = gameObject.GetComponent<VoxelCharacter>();
            if (voxelChar == null)
                voxelChar = gameObject.AddComponent<VoxelCharacter>();
            voxelChar.assetFileName = assetBaseName + ".stasset";
            voxelChar.voxelSize = voxelSize;
            voxelChar.chunkManager = chunkManager;
            voxelChar.useInstancing = true;
            voxelChar.useWorldPosition = false;
            voxelChar.centerPosition = spawnPosition;
            voxelChar.showGizmo = true;
            voxelChar.showGroundProbe = false;

            // Add CharacterAnimation for state control on same GameObject
            anim = gameObject.GetComponent<CharacterAnimation>();
            if (anim == null)
                anim = gameObject.AddComponent<CharacterAnimation>();
            anim.autoDetectWalking = false;
            anim.walkSpeed = animSpeed;
            anim.SetState(CharacterAnimation.AnimState.TPose);

            Debug.Log($"[CharRig] Initialized character on '{gameObject.name}' at {spawnPosition} " +
                      $"(asset={assetBaseName}.stasset, voxelSize={voxelSize})");
            Debug.Log("[CharRig] Hotkeys: T=TPose I=Idle W=Walk L=Look A=Aim C=Crouch " +
                      "Space=Play/Pause +/-=Speed");
        }
    }
}
