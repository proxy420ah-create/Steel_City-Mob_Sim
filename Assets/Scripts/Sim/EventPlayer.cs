using UnityEngine;

namespace SteelCity.Sim
{
    public class EventPlayer : MonoBehaviour
    {
        [Header("Playback")]
        [Tooltip("Playback speed multiplier. 1=normal, 0.3=walking pace.")]
        public float playbackSpeed = 0.3f;

        [Tooltip("If true, simulation pauses. Events queue up but don't play.")]
        public bool isPaused = false;

        [Header("References")]
        public VoxelCharacter character;
        public Transform mapRoot;

        public System.Action<string> OnLog;
        public System.Action<SimState, int, int> OnStateChanged;
        public System.Action OnComplete;

        private SimulationManager simManager;
        private SimEvent currentMoveEvent;
        private float moveElapsed;
        private Vector3 currentFromWorld;
        private Vector3 currentToWorld;
        private bool running;
        private int visualPathIndex;

        /// <summary>Visual path index for PDR beams. Offset by -1 so beams include
        /// the segment Vinny is currently traversing, not just remaining nodes.</summary>
        public int VisualPathIndex => Mathf.Max(0, visualPathIndex - 1);

        private float tickAccumulator;

        [Tooltip("How fast character rotates to face movement direction (higher=snappier).")]
        public float turnSpeed = 5f;

        [Tooltip("Yaw offset in degrees to correct voxel model facing. 0 = model front faces +Z. " +
                 "If model faces -Z, use 180. If model faces +X, use -90. If model faces -X, use 90.")]
        public float modelFacingOffset = 0f;

        private Quaternion targetRotation;
        private bool hasTargetRotation;

        [Tooltip("If true, camera follows the character during execution.")]
    public bool cameraFollow = true;

        [Tooltip("How fast camera catches up to character (higher=snappier).")]
        public float cameraFollowSpeed = 3f;

        private Vector3 currentCameraTarget;
        private bool hasCameraTarget;
        private CityMap3D cachedCityMap;
        private CharacterAnimation charAnim;

        // Continuous walk: track whether we're in a walking sequence
        private bool isWalkingSequence;

        // Camera pan offset relative to character (user-adjustable)
        private Vector3 cameraPanOffset;

        public bool IsRunning => running;

        /// <summary>Called by CityMap3D when user pans during execution. Adds delta to pan offset.</summary>
        public void AddCameraPanOffset(Vector3 worldDelta)
        {
            cameraPanOffset += worldDelta;
        }

        public void Initialize(SimulationManager manager, VoxelCharacter charComponent, Transform root)
        {
            simManager = manager;
            character = charComponent;
            mapRoot = root;
            running = true;
            tickAccumulator = 0f;
            currentMoveEvent = null;
            visualPathIndex = 0;
            isWalkingSequence = false;
            cameraPanOffset = Vector3.zero;
            cachedCityMap = FindFirstObjectByType<CityMap3D>();

            // Initialize camera target from current camera focus to avoid a jarring snap
            // when transitioning from HQ focus to Vinny follow
            if (cachedCityMap != null)
            {
                currentCameraTarget = cachedCityMap.CameraFocusPoint;
                hasCameraTarget = true;
            }
            else
            {
                hasCameraTarget = false;
            }

            // Get CharacterAnimation for manual state control during execution
            if (character != null)
            {
                charAnim = character.GetComponent<CharacterAnimation>();
                if (charAnim != null)
                {
                    // Manual control only — autoDetectWalking's velocity threshold can flip
                    // state back to Idle on small per-frame movement deltas, fighting our
                    // explicit SetState calls in StartMove/Update.
                    charAnim.autoDetectWalking = false;
                    charAnim.SetState(CharacterAnimation.AnimState.Idle);
                    Debug.Log("[EventPlayer] CharacterAnimation manual control enabled, state set to Idle");
                }
            }
        }

        public void Shutdown()
        {
            running = false;
            if (charAnim != null)
                charAnim.SetState(CharacterAnimation.AnimState.Idle);
        }

        void Update()
        {
            if (!running || simManager == null) return;

            if (!isPaused)
            {
                tickAccumulator += Time.deltaTime * playbackSpeed;
            }

            while (tickAccumulator >= simManager.tickInterval && !simManager.IsComplete)
            {
                tickAccumulator -= simManager.tickInterval;
                simManager.Tick();
            }

            if (isPaused) return;

            if (currentMoveEvent != null)
            {
                moveElapsed += Time.deltaTime * playbackSpeed;
                float t = Mathf.Clamp01(moveElapsed / currentMoveEvent.duration);

                Vector3 pos = Vector3.Lerp(currentFromWorld, currentToWorld, t);
                pos.y = character.transform.position.y;
                PlaceCharacter(pos);

                // Rotate character to face movement direction
                if (character != null && hasTargetRotation)
                {
                    character.transform.rotation = Quaternion.Slerp(
                        character.transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
                }

                if (t >= 1f)
                {
                    currentMoveEvent = null;
                    // Don't set Idle between walk segments — animTime must keep
                    // accumulating for a continuous walk cycle. Idle is only set
                    // when the simulation transitions away from walking states.
                }
            }

            while (currentMoveEvent == null && simManager.Events.Count > 0)
            {
                ProcessNextEvent();
            }

            if (simManager.IsComplete && currentMoveEvent == null && simManager.Events.Count == 0)
            {
                running = false;
                OnComplete?.Invoke();
            }

            // Camera follow: smoothly track character position + user pan offset
            if (cameraFollow && character != null)
            {
                Vector3 charWorld = character.useWorldPosition
                    ? character.WorldCenter
                    : character.transform.localPosition + mapRoot.position;

                Vector3 desiredTarget = charWorld + cameraPanOffset;

                if (!hasCameraTarget)
                {
                    currentCameraTarget = desiredTarget;
                    hasCameraTarget = true;
                }
                else
                {
                    currentCameraTarget = Vector3.Lerp(
                        currentCameraTarget, desiredTarget,
                        cameraFollowSpeed * Time.deltaTime);
                }

                // Update camera focus using cached reference
                if (cachedCityMap != null)
                {
                    cachedCityMap.SetCameraFocus(currentCameraTarget);
                }
            }
        }

        void ProcessNextEvent()
        {
            var evt = simManager.Events.Dequeue();
            if (evt == null) return;

            // Non-move events end the walking sequence — set Idle and reset flag
            if (evt.type != SimEventType.HoodMove && isWalkingSequence)
            {
                isWalkingSequence = false;
                if (charAnim != null)
                    charAnim.SetState(CharacterAnimation.AnimState.Idle);
            }

            switch (evt.type)
            {
                case SimEventType.HoodMove:
                    StartMove(evt);
                    break;

                case SimEventType.HoodArrive:
                    Log($"[Tick {evt.tickElapsed}] Arrived at {evt.blockId}");
                    OnStateChanged?.Invoke(simManager.State, evt.tickElapsed, evt.tickRemaining);
                    break;

                case SimEventType.DialogStart:
                    Log($"[Tick {evt.tickElapsed}] {evt.orderType.ToUpper()} dialog started at {evt.blockId} ({evt.dialogTotalTicks} ticks)");
                    OnStateChanged?.Invoke(simManager.State, evt.tickElapsed, evt.tickRemaining);
                    break;

                case SimEventType.DialogProgress:
                    Log($"[Tick {evt.tickElapsed}] {evt.orderType.ToUpper()} in progress... ({evt.dialogTicksRemaining}/{evt.dialogTotalTicks} ticks left)");
                    OnStateChanged?.Invoke(simManager.State, evt.tickElapsed, evt.tickRemaining);
                    break;

                case SimEventType.DialogEnd:
                    Log($"[Tick {evt.tickElapsed}] {evt.orderType.ToUpper()} dialog complete at {evt.blockId}");
                    OnStateChanged?.Invoke(simManager.State, evt.tickElapsed, evt.tickRemaining);
                    break;

                case SimEventType.OrderResolve:
                    if (evt.success)
                        Log($"[EXTORT] SUCCESS — {evt.details}");
                    else
                        Log($"[EXTORT] FAILED — {evt.details}");
                    OnStateChanged?.Invoke(simManager.State, evt.tickElapsed, evt.tickRemaining);
                    break;

                case SimEventType.Wander:
                    if (evt.wanderTicks > 0)
                        Log($"[Tick {evt.tickElapsed}] Wandering... ({evt.wanderTicks} ticks left)");
                    OnStateChanged?.Invoke(simManager.State, evt.tickElapsed, evt.tickRemaining);
                    break;

                case SimEventType.TrafficWait:
                    Log($"[Tick {evt.tickElapsed}] Traffic light! +{evt.wanderTicks} ticks");
                    OnStateChanged?.Invoke(simManager.State, evt.tickElapsed, evt.tickRemaining);
                    break;

                case SimEventType.PathFound:
                    Log($"[SIM] Path found: {evt.pathNodeCount} nodes");
                    break;

                case SimEventType.NoPath:
                    Log($"[SIM] {evt.message}");
                    break;

                case SimEventType.TickBudgetExhausted:
                    Log($"[Tick {evt.tickElapsed}] OUT OF TICKS — week budget exhausted!");
                    OnStateChanged?.Invoke(simManager.State, evt.tickElapsed, 0);
                    break;

                case SimEventType.WeekComplete:
                    Log($"[Tick {evt.tickElapsed}] Mission complete. Total ticks: {evt.tickElapsed}");
                    OnStateChanged?.Invoke(SimState.Complete, evt.tickElapsed, 0);
                    break;
            }
        }

        void StartMove(SimEvent evt)
        {
            currentMoveEvent = evt;
            moveElapsed = 0f;
            visualPathIndex++;

            // Continuous walk cycle: only call SetState on the FIRST move of a
            // walking sequence. Subsequent segments keep animTime accumulating
            // so the procedural walk cycle never resets (no stitch).
            if (charAnim != null)
            {
                if (!isWalkingSequence)
                {
                    charAnim.SetState(CharacterAnimation.AnimState.Walking);
                    isWalkingSequence = true;
                }
                charAnim.walkSpeed = 1.0f;
            }

            currentFromWorld = evt.fromPos + mapRoot.position;
            currentToWorld = evt.toPos + mapRoot.position;

            // Compute target rotation to face movement direction
            Vector3 dir = currentToWorld - currentFromWorld;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
            {
                targetRotation = Quaternion.LookRotation(dir.normalized, Vector3.up) *
                                 Quaternion.Euler(0f, modelFacingOffset, 0f);
                hasTargetRotation = true;
            }
            else
            {
                hasTargetRotation = false;
            }

            string dirSymbol = simManager.State == SimState.WalkingToTarget ? ">" : "<";
            Log($"[Tick {evt.tickElapsed}] {dirSymbol} {evt.nodeId} [+{evt.tickCost:F1} {evt.linkType}]");

            OnStateChanged?.Invoke(simManager.State, evt.tickElapsed, evt.tickRemaining);
        }

        void PlaceCharacter(Vector3 worldCenter)
        {
            if (character == null) return;

            if (character.useWorldPosition)
                character.PlaceAtCenter(worldCenter);
            else
                character.transform.localPosition = worldCenter - mapRoot.position;
        }

        void Log(string msg)
        {
            Debug.Log($"[EventPlayer] {msg}");
            OnLog?.Invoke(msg);
        }
    }
}
