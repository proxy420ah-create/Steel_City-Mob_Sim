using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Drives voxel group animation state for a VoxelCharacter.
    /// Updates animState/animTime/animSpeed on the InstancedCharacter handle
    /// so the raymarch shader can apply per-group limb transforms.
    ///
    /// Animation states (must match shader GroupTransformOffset logic):
    ///   0 = Idle, 1 = Walking, 2 = Looking, 3 = AimWalk,
    ///   4 = Aiming, 5 = Crouching, 6 = Flinching, 7 = Falling, 8 = Down, 9 = T-Pose,
    ///   10 = Aim Sweep, 11 = Sit
    /// </summary>
    public class CharacterAnimation : MonoBehaviour
    {
        public enum AnimState : int
        {
            Idle = 0,
            Walking = 1,
            Looking = 2,
            AimWalk = 3,
            Aiming = 4,
            Crouching = 5,
            Flinching = 6,
            Falling = 7,
            Down = 8,
            TPose = 9,
            AimSweep = 10,  // aiming pose + shoulder-yaw sweep (must match animator/shader state)
            Sitting = 11    // seated pose — thighs forward, shins down, upright torso (seat anchor contract)
        }

        [Header("Animation")]
        [Tooltip("Base-lane animation state (locomotion: idle/walk). Drives shader per-group transforms when no pose override is held.")]
        public AnimState currentState = AnimState.Idle;
        [Tooltip("Walk speed multiplier. 1.0 = normal, 1.5 = jogging.")]
        public float walkSpeed = 1.0f;
        [Tooltip("If true, auto-detect walking state from velocity.")]
        public bool autoDetectWalking = true;
        [Tooltip("Minimum velocity magnitude to be considered walking.")]
        public float walkVelocityThreshold = 0.1f;

        // --- Pose-override channel (CHARACTER_ASSET_LIFECYCLE.md, gotcha G3) ---
        // Every writer used to SetState() the same field — auto-detect, LookAround
        // timers, hotkeys — so a manually-set pose got stomped on the next tick.
        // Now: SetState writes the BASE lane (locomotion); RequestPose holds an
        // OVERRIDE lane that wins while held. autoDetectWalking pauses under an
        // override; timed overrides auto-release, so nothing needs to "restore".
        public const int PRIORITY_BEHAVIOR = 10;  // LookAround, CoastClearCheck, scripted beats
        public const int PRIORITY_COMBAT = 30;    // aim/combat AI
        public const int PRIORITY_DEBUG = 50;     // rigs, hotkeys — nothing stomps these

        private bool overrideActive;
        private AnimState overrideState;
        private int overridePriority;
        private float overrideExpiry = -1f; // <0 = persistent until ReleasePose

        // ---- Pose transition (dual-eval blend) ----
        // On every effective-state change the outgoing pose is FROZEN as
        // (transFrom, transFromTime) and the GPU lerps voxel positions from it
        // into the live state over transitionDuration — a parametric
        // "whatever pose it was → authored pose" engine; the same ease curves
        // and procedural settle the walk cycle uses. transDip adds the
        // bob-flavored dip-and-recover on the vertical channel.
        [Header("Pose transitions (snapshot → target lerp)")]
        public bool poseTransitions = true;
        public float transitionDuration = 0.32f;
        public float transitionDip = 0.45f;   // voxel units — peak of the settle dip
        [Range(0, 2)] public int transitionEase = 1; // 0 linear · 1 smoothstep · 2 cosine

        private float transT = -1f;           // <0 = idle
        private AnimState transFrom;
        private float transFromTime;
        private float transFromDepth;  // depth is a pose axis — frozen with state+time
        private float pendingPrevAnimTime = -1f; // SetState resets animTime before Update reads it
        /// <summary>Blend weight of the LIVE pose (1 = transition done/off).</summary>
        public float TransBlend { get; private set; } = 1f;
        /// <summary>Signed vertical settle offset in voxel units (0 at both ends).</summary>
        public float TransDip { get; private set; }
        /// <summary>True while a hold outranks combat — a debug pose owns the
        /// pose AND the crouch-depth channel (combat's depth ease yields; the
        /// wall's requirement resumes when the hold releases).</summary>
        public bool DebugOwnsPose => overrideActive && overridePriority > PRIORITY_COMBAT;

        public AnimState TransFrom => transFrom;
        public float TransFromTime => transFromTime;

        /// <summary>True while a pose override is held (base lane suspended).</summary>
        public bool HasOverride => overrideActive;
        /// <summary>The state actually pushed to the GPU this frame.</summary>
        public AnimState EffectiveState => overrideActive ? overrideState : currentState;

        /// <summary>
        /// Hold a pose override. Higher or equal priority replaces the current hold;
        /// lower priority is rejected. duration &lt; 0 = persistent until ReleasePose.
        /// </summary>
        public void RequestPose(AnimState s, int priority, float duration = -1f)
        {
            if (overrideActive && priority < overridePriority) return;
            overrideActive = true;
            overrideState = s;
            overridePriority = priority;
            overrideExpiry = duration >= 0f ? Time.time + duration : -1f;
        }

        /// <summary>
        /// Release the override. priority &gt;= 0 only releases a hold AT or BELOW
        /// that priority (a behavior can't release a debug pose). -1 releases any.
        /// </summary>
        public void ReleasePose(int priority = -1)
        {
            if (!overrideActive) return;
            if (priority >= 0 && priority < overridePriority) return;
            overrideActive = false;
        }

        /// <summary>Adaptive crouch depth 0..1 — lerps every authored crouch
        /// scalar shallow→deep inside the pose eval (CPU + GPU). Written by
        /// CombatController from picked cover height (eased); also drivable
        /// directly for authoring/debug preview.</summary>
        [Range(0f, 1f)] public float crouchDepth = 0f;

        private VoxelCharacter voxelChar;
        private float animTime = 0f;
        private AnimState prevState;
        private Vector3 lastPos;

        void Start()
        {
            voxelChar = GetComponent<VoxelCharacter>();
            prevState = currentState;
            lastPos = transform.position;
        }

        void Update()
        {
            // Re-fetch EVERY frame (gotcha G7) — the handle object is swapped if the
            // character re-registers; a cached handle would keep writing to a dead
            // instance and the pose would silently freeze at the new handle's default.
            var instancedHandle = voxelChar != null ? voxelChar.GetInstancedHandle() : null;
            if (instancedHandle == null) return;

            // Expire timed overrides — auto-release, no coroutine needed.
            if (overrideActive && overrideExpiry >= 0f && Time.time >= overrideExpiry)
                overrideActive = false;

            // Auto-detect walking from movement — BASE LANE ONLY. Suspended while
            // an override is held (this was the "Aim resets to idle" stomp).
            // lastPos updates unconditionally so a held override doesn't leave a
            // stale sample that spikes velocity the frame the override releases.
            Vector3 velocity = (transform.position - lastPos) / Mathf.Max(Time.deltaTime, 1e-5f);
            lastPos = transform.position;
            if (autoDetectWalking && !overrideActive)
            {
                float horSpeed = new Vector2(velocity.x, velocity.z).magnitude;

                if (currentState != AnimState.Looking && currentState != AnimState.AimWalk)
                {
                    if (horSpeed > walkVelocityThreshold)
                    {
                        if (currentState != AnimState.Walking)
                            SetState(AnimState.Walking);
                        walkSpeed = Mathf.Clamp(horSpeed * 0.5f, 0.5f, 2.0f);
                    }
                    else
                    {
                        if (currentState == AnimState.Walking)
                            SetState(AnimState.Idle);
                    }
                }
            }

            AnimState effective = overrideActive ? overrideState : currentState;

            // Reset animTime on state change for clean transitions
            if (effective != prevState)
            {
                if (poseTransitions)
                {
                    // Freeze the outgoing pose — the GPU keeps evaluating it
                    // under this (state, time) pair while the blend ramps in.
                    transFrom = prevState;
                    transFromTime = pendingPrevAnimTime >= 0f ? pendingPrevAnimTime : animTime;
                    transFromDepth = crouchDepth;
                    transT = 0f;
                }
                pendingPrevAnimTime = -1f;
                animTime = 0f;
                prevState = effective;
            }

            animTime += Time.deltaTime;

            // Advance the transition clock once per frame — eased blend weight
            // plus a one-cycle settle dip (peak mid-transition, zero at ends).
            if (transT >= 0f)
            {
                transT += Time.deltaTime;
                float t = transitionDuration > 0f ? Mathf.Clamp01(transT / transitionDuration) : 1f;
                TransBlend = transitionEase == 0 ? t
                    : transitionEase == 2 ? 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI)
                    : t * t * (3f - 2f * t);
                TransDip = -Mathf.Sin(t * Mathf.PI) * transitionDip;
                if (t >= 1f) { transT = -1f; TransBlend = 1f; TransDip = 0f; }
            }

            // Push to GPU via instance buffer — single authoritative write.
            instancedHandle.animState = (float)effective;
            instancedHandle.animTime = animTime;
            instancedHandle.animSpeed = walkSpeed;
            instancedHandle.transStateA = (float)transFrom;
            instancedHandle.transTimeA = transFromTime;
            instancedHandle.transBlend = TransBlend;
            instancedHandle.transDip = TransDip;
            instancedHandle.transDepthA = transFromDepth;
            instancedHandle.crouchDepth = crouchDepth;
        }

        /// <summary>Set base-lane animation state (locomotion). Does not break a held pose override.</summary>
        public void SetState(AnimState newState)
        {
            if (currentState != newState)
            {
                currentState = newState;
                pendingPrevAnimTime = animTime; // stash — Update needs the outgoing pose's time
                animTime = 0f;
            }
        }

        /// <summary>Current animation time (seconds since state change).</summary>
        public float AnimTime => animTime;
    }
}
