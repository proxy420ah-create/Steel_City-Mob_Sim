using UnityEngine;
using UnityEngine.InputSystem;

namespace SteelCity.Sim
{
    /// <summary>
    /// Character animation rig — controls a character's animation state via hotkeys.
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
        [SerializeField] private string assetBaseName = "Civilian1";

        [Header("Rendering")]
        [Tooltip("Voxel size in world units. Must match authoring.")]
        [SerializeField] private float voxelSize = 0.01f;
        [Tooltip("VoxelChunkManager for raymarch rendering. Auto-found if not assigned.")]
        [SerializeField] private VoxelChunkManager chunkManager;

        [Header("Weapon")]
        [Tooltip("Item file in StreamingAssets/voxel_items/ to weld to the right hand. Empty = unarmed.")]
        [SerializeField] internal string equipItem = "SW_Model_10.json";

        [Header("Position")]
        [Tooltip("Fixed spawn position (world space). No ground probe.")]
        [SerializeField] internal Vector3 spawnPosition = new Vector3(0f, 0.1f, 0f);

        private VoxelCharacter voxelChar;
        private CharacterAnimation anim;
        private bool isPlaying = true;
        private float animSpeed = 1f;
        private float currentAnimState = 9f; // T-Pose
        private float depthTarget;         // debug duck-scalar target (1 shallow / 2 deep)
        private bool depthEaseActive;      // ease owns the channel only until reached
        /// <summary>Depth-ease state for diagnostics (CoverMetricsPanel).</summary>
        public bool DepthEaseActive => depthEaseActive;
        public float DepthTarget => depthTarget;
        private const float CrouchDepthSpeed = 3f; // matches CombatController's settle rate

        /// <summary>Exposes the VoxelCharacter for external systems (e.g. CityMap3D.SpawnedCharacter).</summary>
        public VoxelCharacter Character => voxelChar;

        /// <summary>When true, this rig processes hotkey input. Only one rig should be active at a time.</summary>
        public bool Controllable
        {
            get => _controllable;
            set
            {
                _controllable = value;
                if (value) ActiveRig = this;
            }
        }
        [SerializeField] private bool _controllable = true;

        /// <summary>Currently active rig that receives hotkey input. Set via Controllable property.</summary>
        public static CharacterRig ActiveRig { get; private set; }

        /// <summary>
        /// Debug anim hotkeys (Alt+T/I/W/L/A/C/S/B/Space/±) — gated off by
        /// default so pose letters can't collide with camera/possess input.
        /// Code-enabled via AnimHotkeys: the primary rig gets it at spawn, the
        /// Clothing tab's Character Control selector transfers it between rigs.
        /// PossessController still gates input via Controllable while active.
        /// </summary>
        [SerializeField] private bool animHotkeys = false;
        public bool AnimHotkeys { get => animHotkeys; set => animHotkeys = value; }

        private static readonly string[] STATE_NAMES = {
            "Idle", "Walking", "Looking", "AimWalk", "Aiming",
            "Crouching", "???", "???", "Down", "T-Pose", "Aim Sweep", "Sit"
        };

        void Start()
        {
            if (chunkManager == null)
                chunkManager = FindFirstObjectByType<VoxelChunkManager>();

            if (_controllable && ActiveRig == null)
                ActiveRig = this;

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

            // Debug duck drift — the shared scalar DRIFTS to target so the
            // silhouette morphs continuously (Idle hold ↔ walk crumple share
            // the channel). Releases on arrival so combat's depth targeting
            // isn't contested. Dropped when the rig loses control (possess
            // hands duck to the cover drive).
            if (!_controllable) depthEaseActive = false;
            if (depthEaseActive)
            {
                anim.duck = Mathf.MoveTowards(anim.duck, depthTarget,
                    CrouchDepthSpeed * Time.deltaTime);
                if (Mathf.Approximately(anim.duck, depthTarget)) depthEaseActive = false;
            }

            if (isPlaying)
            {
                anim.walkSpeed = animSpeed;
            }
        }

        void HandleInput()
        {
            if (!_controllable || !animHotkeys) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            // Pose letters are Alt-gated — same mnemonics as PossessController's
            // test-pose latch, so the gesture is identical possessed or not.
            // Bare letters are deliberately NOT read here: in the live scene
            // WASD belongs to the map camera (the conflict that deprecated the
            // letter keys); the standalone test rigs keep their own bare keys.
            if (kb.altKey.isPressed)
            {
                if (kb.tKey.wasPressedThisFrame) { SetState(9f); }
                if (kb.iKey.wasPressedThisFrame) { SetState(0f); }
                if (kb.wKey.wasPressedThisFrame) { SetState(1f); }
                if (kb.lKey.wasPressedThisFrame) { ToggleLookScan(); }
                if (kb.jKey.wasPressedThisFrame) { SetState(3f); }
                if (kb.aKey.wasPressedThisFrame) { SetState(4f); }
                if (kb.cKey.wasPressedThisFrame) { SetCrouch(); }
                if (kb.fKey.wasPressedThisFrame) { SetState(6f); }
                if (kb.dKey.wasPressedThisFrame) { SetState(8f); }
                if (kb.sKey.wasPressedThisFrame) { SetState(10f); }
                if (kb.bKey.wasPressedThisFrame) { SetState(11f); }
                if (kb.xKey.wasPressedThisFrame) { SetState(0f); } // release — same as Alt+I
            }

            // Numpad = the pose panel — no modifier, so no Windows menu-beep
            // and no camera-key conflict. Same SetState → same DEBUG-priority
            // latch. Grid reads like the pad: extremes top row, combat middle,
            // locomotion bottom; 0/Enter release, +/- still drive anim speed.
            if (kb.numpad7Key.wasPressedThisFrame) { SetState(9f); }
            if (kb.numpad8Key.wasPressedThisFrame) { SetState(8f); }
            if (kb.numpad9Key.wasPressedThisFrame) { SetState(11f); }
            if (kb.numpad4Key.wasPressedThisFrame) { SetState(4f); }
            if (kb.numpad5Key.wasPressedThisFrame) { SetCrouch(); }
            if (kb.numpad6Key.wasPressedThisFrame) { SetState(6f); }
            if (kb.numpad1Key.wasPressedThisFrame) { SetState(1f); }
            if (kb.numpad2Key.wasPressedThisFrame) { ToggleLookScan(); }
            if (kb.numpad3Key.wasPressedThisFrame) { SetState(3f); }
            if (kb.numpad0Key.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) { SetState(0f); }
            if (kb.numpadPeriodKey.wasPressedThisFrame) { SetState(10f); }

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

        /// <summary>Num2 / Alt+L — toggle the look-scan CHANNEL (not a pose):
        /// the authored 'looking' head-yaw sweep layers over whatever pose is
        /// active — crouched sentry = Idle + duck + scan.</summary>
        void ToggleLookScan()
        {
            anim.lookScan = !anim.lookScan;
            Debug.Log($"[CharRig] Look scan -> {(anim.lookScan ? "ON" : "off")}");
        }

        /// <summary>Num5 / Alt+C — the duck scalar cycle: stand → shallow(1)
        /// → deep(2) → shallow… No state change — the CURRENT pose renders it
        /// (Idle = hold pose, Walking = crumple under the gait). Stand back up
        /// with I/X/numpad0 (release zeroes the scalar).</summary>
        void SetCrouch()
        {
            float cur = depthEaseActive ? depthTarget : anim.duck;
            depthTarget = cur < 0.5f ? 1f : (cur < 1.5f ? 2f : 1f);
            Debug.Log($"[CharRig] Duck -> {depthTarget} ({(depthTarget > 1.5f ? "deep" : "shallow")})");
            depthEaseActive = true;
        }

        void SetState(float state)
        {
            currentAnimState = state;
            int stateInt = Mathf.RoundToInt(state);
            if (stateInt != (int)CharacterAnimation.AnimState.Crouching)
                depthEaseActive = false;   // leaving crouch frees the depth channel
            var animState = (CharacterAnimation.AnimState)stateInt;
            if (stateInt == (int)CharacterAnimation.AnimState.Idle)
            {
                // I = release any held debug pose, return to plain idle — and
                // stand fully up: the duck scalar releases with the pose.
                anim.ReleasePose(CharacterAnimation.PRIORITY_DEBUG);
                anim.SetState(CharacterAnimation.AnimState.Idle);
                anim.duck = 0f;
            }
            else
            {
                // Hotkey poses hold at DEBUG priority — LookAround timers and
                // auto-detect can no longer stomp them back to idle.
                anim.RequestPose(animState, CharacterAnimation.PRIORITY_DEBUG);
            }
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
            voxelChar.assetFileName = assetBaseName + ".json";
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
            // All rigs start Idle (normal NPC look). T-Pose remains reachable
            // via the T hotkey on controllable rigs for pose debugging.
            anim.SetState(CharacterAnimation.AnimState.Idle);

            // Weapon attachment — welds the item volume to the posed hand each frame
            if (!string.IsNullOrEmpty(equipItem))
            {
                var mount = gameObject.GetComponent<WeaponMount>();
                if (mount == null)
                    mount = gameObject.AddComponent<WeaponMount>();
                mount.itemFileName = equipItem;
            }

            Debug.Log($"[CharRig] Initialized character on '{gameObject.name}' at {spawnPosition} " +
                      $"(asset={assetBaseName}.json, voxelSize={voxelSize})");
            if (animHotkeys)
                Debug.Log("[CharRig] Anim hotkeys: T=TPose I=Idle W=Walk L=Look A=Aim C=Crouch S=AimSweep " +
                          "Space=Play/Pause +/-=Speed");
        }
    }
}
