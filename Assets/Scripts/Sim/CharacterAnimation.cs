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
            Sitting = 11,   // seated pose — thighs forward, shins down, upright torso (seat anchor contract)
            Reloading = 13, // standing reload hold — reload params + tapPulse deltas per seated round
            ReloadCrouch = 14 // crouched reload — reload arms over the crouch body channel
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
        public static float transitionDuration = 0.32f;   // STATIC: universal — one blend pace for every character
        public float transitionDip = 0.45f;   // voxel units — peak of the settle dip
        [Range(0, 2)] public int transitionEase = 1; // 0 linear · 1 smoothstep · 2 cosine

        private float transT = -1f;           // <0 = idle
        private AnimState transFrom;
        private float transFromTime;
        private float transFromDepth;  // depth is a pose axis — frozen with state+time
        private float prevFrameDuck;   // duck as of the last Update — the honest
                                       // freeze: a same-frame drive write (cover
                                       // release snaps duck→0 before the pose
                                       // flip) would otherwise fold the outgoing
                                       // pose flat and the blend-out collapses.
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

        /// <summary>THE duck scalar 0..2 — one silhouette/cover-height contract.
        /// 0 upright · 1 shallow endpoint · 2 deep endpoint. Idle evaluates the
        /// authored HOLD pose (crouching→crouchDeep); Walking folds crumple
        /// DELTAS (walkDuckMid→walkDuckPose) under the gait; legacy state 5.x
        /// reads the deep band as its old crouchDepth axis (duck−1).</summary>
        [Range(0f, 2f)] public float duck = 0f;

        /// <summary>Legacy cover-depth write channel (0..1): combat/behaviors keep
        /// their depth semantics; 0 = no duck requirement, 1 = deep. Mapped onto
        /// duck — depth d writes duck = 1+d (the shallow→deep band); 0 stands up.</summary>
        public float crouchDepth
        {
            get => Mathf.Clamp01(duck - 1f);
            set => duck = value <= 0f ? 0f : 1f + Mathf.Clamp01(value);
        }

        /// <summary>"Behind cover" per whoever owns the cover verdict this frame —
        /// possess drive detection or combat's InCover pick. Class-agnostic:
        /// a Full wall sets it exactly like a crouch wall (covered is covered —
        /// he can't see over it either). ArmAimSolver's posture gate reads it;
        /// debug-ducked poses without a cover verdict are unaffected.</summary>
        public bool coverContext;

        /// <summary>Duck→gait-speed coupling: effective gait rate =
        /// walkSpeed × lerp(1 → duckSpeedMin, duck/2). Only the cycle rate slows —
        /// gaitTime is integrated, so duck changes never teleport the phase.
        /// In-game move speed should ride the same curve; this is the preview.</summary>
        [Header("Duck → speed coupling")]
        public bool duckSpeedScale = true;
        [Range(0.05f, 1f)] public float duckSpeedMin = 0.35f;
        public float DuckSpeedFactor => duckSpeedScale ? Mathf.Lerp(1f, duckSpeedMin, Mathf.Clamp01(duck * 0.5f)) : 1f;

        /// <summary>Look-scan channel — layers the authored 'looking' head-yaw
        /// sweep over ANY pose (crouched sentry = Idle + duck + scan). Yields to
        /// IK aim while overrides are active.</summary>
        [Header("Look-scan channel")]
        public bool lookScan = false;
        [Range(0f, 1f)] public float lookScanBlend = 1f;

        /// <summary>Reload tap channel (0..1) — driven by ReloadGestureSolver;
        /// the reload states (13/14) add authored tap* deltas on the support arm.</summary>
        [HideInInspector] public float tapPulse = 0f;

        private VoxelCharacter voxelChar;
        private float animTime = 0f;
        private float gaitTime = 0f;   // integrated gait clock — survives state changes
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
                    transFromDepth = prevFrameDuck;   // freeze the PRE-write duck
                                                      // — cover release already
                                                      // wrote 0 this frame; the
                                                      // outgoing pose must stay
                                                      // folded so the blend
                                                      // reads as a real un-fold
                    transT = 0f;
                }
                pendingPrevAnimTime = -1f;
                animTime = 0f;
                prevState = effective;
            }

            animTime += Time.deltaTime;
            // Integrated gait clock — rate = walkSpeed × duck coupling. Speed
            // changes alter the RATE of advance only; phase never teleports
            // (was: derived phase = animTime % (dur/speed) → pose snap on change).
            // NOT reset on state change — transitions carry stride phase.
            gaitTime += Time.deltaTime * walkSpeed * DuckSpeedFactor;

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
            instancedHandle.crouchDepth = duck;          // field carries THE duck scalar now
            instancedHandle.gaitTime = gaitTime;
            instancedHandle.lookScanBlend = lookScan ? lookScanBlend : 0f;
            instancedHandle.tapPulse = tapPulse;
            prevFrameDuck = duck;   // snapshot AFTER all writers — consumed by
                                    // the next flip's transFromDepth freeze
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
        /// <summary>Integrated gait clock — the walk cycle's phase source.</summary>
        public float GaitTime => gaitTime;
    }
}
