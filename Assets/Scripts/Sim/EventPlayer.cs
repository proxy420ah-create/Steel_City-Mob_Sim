using UnityEngine;

namespace SteelCity.Sim
{
    public class EventPlayer : MonoBehaviour
    {
        [Header("Playback")]
        [Tooltip("Playback speed multiplier. 1=normal, 2=double, 5=fast.")]
        public float playbackSpeed = 1f;

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

        private float tickAccumulator;

        public bool IsRunning => running;

        public void Initialize(SimulationManager manager, VoxelCharacter charComponent, Transform root)
        {
            simManager = manager;
            character = charComponent;
            mapRoot = root;
            running = true;
            tickAccumulator = 0f;
            currentMoveEvent = null;
        }

        public void Shutdown()
        {
            running = false;
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

                if (t >= 1f)
                {
                    currentMoveEvent = null;
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
        }

        void ProcessNextEvent()
        {
            var evt = simManager.Events.Dequeue();
            if (evt == null) return;

            switch (evt.type)
            {
                case SimEventType.HoodMove:
                    StartMove(evt);
                    break;

                case SimEventType.HoodArrive:
                    Log($"[Tick {evt.tickElapsed}] Arrived at {evt.blockId}");
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
                    Log($"[SIM] Path found: {evt.pathNodeCount} nodes, jaywalk bias={evt.jaywalkBias:F2}");
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

            currentFromWorld = evt.fromPos + mapRoot.position;
            currentToWorld = evt.toPos + mapRoot.position;

            string dir = simManager.State == SimState.WalkingToTarget ? ">" : "<";
            Log($"[Tick {evt.tickElapsed}] {dir} {evt.nodeId} [+{evt.tickCost} {evt.linkType}]");

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
