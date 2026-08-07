using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    public enum TickPhase
    {
        Idle,
        WalkingToTarget,
        ResolvingOrder,
        WalkingHome,
        Complete
    }

    public class TickSimulation : MonoBehaviour
    {
        [Header("Tick Settings")]
        [Tooltip("Seconds per tick. 0.05 = 20 ticks/sec (fast), 0.1 = 10 ticks/sec (visible walk).")]
        public float tickInterval = 0.08f;

        [Tooltip("Maximum ticks per hood per week (from RE findings).")]
        public const int TickBudget = 12000;

        [Tooltip("Yaw offset in degrees to correct voxel model facing. 0 = model front faces +Z. " +
                 "If model faces -Z, use 180. If model faces +X, use -90. If model faces -X, use 90.")]
        public float modelFacingOffset = 180f;

        [Tooltip("How fast character rotates to face movement direction (higher=snappier).")]
        public float turnSpeed = 5f;

        [Header("References")]
        public VoxelCharacter character;
        public WaypointGraph waypointGraph;
        public Transform mapRoot;

        public System.Action<string> OnLog;
        public System.Action<TickPhase, int, int> OnPhaseChanged;
        public System.Action OnComplete;

        private GameEngine gameEngine;

        private Pathfinder pathfinder;
        private List<string> currentPath;
        private int pathIndex;
        private TickPhase phase = TickPhase.Idle;
        private int ticksElapsed;
        private int ticksRemaining;
        private float tickTimer;
        private bool running;

        private Order activeOrder;
        private string startBlockId;
        private string targetBlockId;
        private Vector3 startPos;
        private Vector3 targetPos;


        public TickPhase Phase => phase;
        public int TicksElapsed => ticksElapsed;
        public int TicksRemaining => ticksRemaining;
        public bool IsRunning => running;

        public void Initialize(
            WaypointGraph graph,
            Transform root,
            VoxelCharacter charComponent,
            GameEngine engine = null)
        {
            waypointGraph = graph;
            mapRoot = root;
            character = charComponent;
            pathfinder = new Pathfinder(graph);
            gameEngine = engine;
        }

        public void StartSimulation(Order order, string startBlock, Vector3 startLocalPos, string targetBlock, Vector3 targetLocalPos)
        {
            activeOrder = order;
            startBlockId = startBlock;
            targetBlockId = targetBlock;
            startPos = startLocalPos;
            targetPos = targetLocalPos;
            ticksElapsed = 0;
            ticksRemaining = TickBudget;
            pathIndex = 0;
            running = true;

            SetPhase(TickPhase.WalkingToTarget);
            FindPathToTarget();
        }

        public void Stop()
        {
            running = false;
            SetPhase(TickPhase.Idle);
        }

        void Update()
        {
            if (!running) return;

            tickTimer += Time.deltaTime;
            if (tickTimer < tickInterval) return;
            tickTimer = 0f;

            ProcessTick();
        }

        void ProcessTick()
        {
            if (phase == TickPhase.WalkingToTarget || phase == TickPhase.WalkingHome)
            {
                if (currentPath == null || pathIndex >= currentPath.Count)
                {
                    OnArrivedAtDestination();
                    return;
                }

                string currentNodeId = currentPath[pathIndex];
                var node = waypointGraph.Nodes[currentNodeId];

                // Determine link cost from previous node to this one
                int linkCost = 1;
                string linkType = "walk";
                if (pathIndex > 0)
                {
                    string prevNodeId = currentPath[pathIndex - 1];
                    if (waypointGraph.Nodes.TryGetValue(prevNodeId, out var prevNode))
                    {
                        foreach (var link in prevNode.links)
                        {
                            if (link.targetId == currentNodeId)
                            {
                                linkCost = link.baseTickCost;
                                linkType = link.type.ToString();
                                break;
                            }
                        }
                    }
                }

                MoveCharacterToNode(node);

                ticksElapsed += linkCost;
                ticksRemaining -= linkCost;

                string dir = phase == TickPhase.WalkingToTarget ? ">" : "<";
                Log($"[Tick {ticksElapsed}] {dir} {node.id} ({node.type}) [+{linkCost} {linkType}]");

                pathIndex++;

                if (ticksRemaining <= 0)
                {
                    Log($"[Tick {ticksElapsed}] OUT OF TICKS -- week budget exhausted!");
                    OnArrivedAtDestination();
                }
            }
            else if (phase == TickPhase.ResolvingOrder)
            {
                OnArrivedAtDestination();
            }
        }

        void FindPathToTarget()
        {
            currentPath = pathfinder.FindPathBlockToBlock(
                startBlockId, startPos,
                targetBlockId, targetPos);

            if (currentPath == null || currentPath.Count == 0)
            {
                Log($"[TickSim] No path found {startBlockId} to {targetBlockId}!");
                SetPhase(TickPhase.Complete);
                running = false;
                OnComplete?.Invoke();
                return;
            }

            pathIndex = 0;
            Log($"[TickSim] Path found: {currentPath.Count} nodes");
        }

        void FindPathHome()
        {
            currentPath = pathfinder.FindPathBlockToBlock(
                targetBlockId, targetPos,
                startBlockId, startPos);

            if (currentPath == null || currentPath.Count == 0)
            {
                Log($"[TickSim] No path home {targetBlockId} to {startBlockId}!");
                SetPhase(TickPhase.Complete);
                running = false;
                OnComplete?.Invoke();
                return;
            }

            pathIndex = 0;
            Log($"[TickSim] Path home: {currentPath.Count} nodes");
        }

        void OnArrivedAtDestination()
        {
            if (phase == TickPhase.WalkingToTarget)
            {
                Log($"[Tick {ticksElapsed}] Arrived at {targetBlockId} after {ticksElapsed} ticks!");
                SetPhase(TickPhase.ResolvingOrder);
                ResolveOrderAtTarget();
                SetPhase(TickPhase.WalkingHome);
                FindPathHome();
            }
            else if (phase == TickPhase.WalkingHome)
            {
                Log($"[Tick {ticksElapsed}] Returned to {startBlockId}! Mission complete. Total ticks: {ticksElapsed}");
                SetPhase(TickPhase.Complete);
                running = false;
                OnComplete?.Invoke();
            }
        }

        void ResolveOrderAtTarget()
        {
            if (activeOrder == null)
            {
                Log("[TickSim] No active order to resolve!");
                return;
            }

            var hood = FindHoodForOrder(activeOrder);
            if (hood == null)
            {
                Log($"[TickSim] Could not find hood {activeOrder.hoodId}!");
                return;
            }

            if (!FindEngineBlocks().TryGetValue(targetBlockId, out var block))
            {
                Log($"[TickSim] Could not find block {targetBlockId}!");
                return;
            }

            switch (activeOrder.orderType)
            {
                case "extort":
                    var engine = FindEngine();
                    if (engine == null)
                    {
                        Log("[TickSim] No GameEngine found for extortion resolution!");
                        return;
                    }
                    var (success, details, targetNpc) = CrimeSystem.ResolveExtortion(
                        hood, block, engine.npcs, engine.businesses, engine.data.constants);

                    if (success)
                    {
                        if (block.ownerGang == null || block.extortionStrength < 30)
                        {
                            block.ownerGang = activeOrder.gangId;
                            block.extortionStrength = Mathf.Max(block.extortionStrength, 20);
                            Log($"[EXTORT] SUCCESS! {block.name} now controlled by {activeOrder.gangId} (strength {block.extortionStrength})");
                        }
                        else
                        {
                            block.extortionStrength = Mathf.Min(100, block.extortionStrength + 10);
                            Log($"[EXTORT] SUCCESS! {block.name} strength increased to {block.extortionStrength}");
                        }
                    }
                    else
                    {
                        Log($"[EXTORT] FAILED — {details}");
                    }
                    break;

                default:
                    Log($"[TickSim] Order type '{activeOrder.orderType}' not yet implemented in tick sim.");
                    break;
            }
        }

        void MoveCharacterToNode(WaypointNode node)
        {
            if (character == null || mapRoot == null) return;

            Vector3 worldPos = node.localPos + mapRoot.position;
            Vector3 prevPos = character.transform.position + character.WorldSize * 0.5f;
            worldPos.y = character.transform.position.y;

            if (character.useWorldPosition)
                character.PlaceAtCenter(worldPos);
            else
                character.transform.localPosition = node.localPos;

            // Rotate character to face movement direction
            Vector3 moveDir = worldPos - prevPos;
            moveDir.y = 0f;
            if (moveDir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(moveDir.normalized, Vector3.up) *
                                       Quaternion.Euler(0f, modelFacingOffset, 0f);
                character.transform.rotation = Quaternion.Slerp(
                    character.transform.rotation, targetRot, turnSpeed * Time.deltaTime);
            }
        }

        void SetPhase(TickPhase newPhase)
        {
            phase = newPhase;
            OnPhaseChanged?.Invoke(phase, ticksElapsed, ticksRemaining);
        }

        void Log(string msg)
        {
            Debug.Log($"[TickSim] {msg}");
            OnLog?.Invoke(msg);
        }

        GameEngine FindEngine()
        {
            if (gameEngine != null) return gameEngine;
            var controller = FindFirstObjectByType<GameUIController>();
            return controller?.GetType().GetField("engine",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(controller) as GameEngine;
        }

        Hood FindHoodForOrder(Order order)
        {
            var engine = FindEngine();
            return engine?.FindHood(order.hoodId);
        }

        Dictionary<string, Block> FindEngineBlocks()
        {
            var engine = FindEngine();
            return engine?.blocks;
        }
    }
}
