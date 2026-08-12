using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    public enum SimState
    {
        Idle,
        WalkingToTarget,
        DialogPhase,
        ResolvingOrder,
        WalkingHome,
        Complete
    }

    public class SimulationManager
    {
        public const int TickBudget = 12000;

        public float tickInterval = 0.08f;

        // Ticks per world unit at base walk speed. This single constant drives both
        // the weekly budget consumption (tick cost) and the visual movement duration
        // (duration = cost * tickInterval). Future movement modes (run, crouch, drive)
        // will apply a multiplier to this base rate.
        public const float TicksPerWorldUnit = 3f;

        // Order action durations (in ticks) from original game data
        public static readonly Dictionary<string, int> OrderActionTicks = new()
        {
            { "extort", 166 },
            { "collect", 166 },
            { "intimidate", 333 },
            { "recruit", 166 },
            { "bomb", 333 },
            { "torch", 333 },
            { "assault", 6000 },
            { "kill", 6000 },
            { "stand", 0 }
        };

        public SimState State => state;
        public int TicksElapsed => ticksElapsed;
        public int TicksRemaining => ticksRemaining;
        public bool IsComplete => state == SimState.Complete;

        public SimEventStream Events => eventStream;

        private SimState state = SimState.Idle;
        private int ticksElapsed;
        private int ticksRemaining;
        private bool started;

        private readonly WaypointGraph waypointGraph;
        private readonly Pathfinder pathfinder;
        private readonly GameEngine gameEngine;

        private Order activeOrder;
        private string startBlockId;
        private string targetBlockId;
        private Vector3 startPos;
        private Vector3 targetPos;

        private List<string> currentPath;
        private int pathIndex;

        public List<string> CurrentPath => currentPath;
        public WaypointGraph Graph => waypointGraph;
        public int PathIndex => pathIndex;
        private int dialogTicksRemaining;
        private int dialogTotalTicks;
        private bool entryMovePending;  // emit final move into building center after last waypoint

        private readonly SimEventStream eventStream = new();

        public SimulationManager(WaypointGraph graph, GameEngine engine = null)
        {
            waypointGraph = graph;
            pathfinder = new Pathfinder(graph);
            gameEngine = engine;
        }

        public void StartSimulation(Order order, string startBlock, Vector3 startLocalPos,
                                     string targetBlock, Vector3 targetLocalPos)
        {
            activeOrder = order;
            startBlockId = startBlock;
            targetBlockId = targetBlock;
            startPos = startLocalPos;
            targetPos = targetLocalPos;
            ticksElapsed = 0;
            ticksRemaining = TickBudget;
            pathIndex = 0;
            started = true;

            if (order.orderType == "stand")
            {
                Debug.Log("[SimulationManager] STAND order — Vinny holds position, no movement. Sim stays active for camera debugging.");
                SetState(SimState.Idle);
                return;
            }

            SetState(SimState.WalkingToTarget);
            FindPathToTarget();
        }

        public void Tick()
        {
            if (!started || state == SimState.Complete) return;

            if (state == SimState.WalkingToTarget || state == SimState.WalkingHome)
            {
                ProcessWalkingTick();
            }
            else if (state == SimState.DialogPhase)
            {
                ProcessDialogTick();
            }
            else if (state == SimState.ResolvingOrder)
            {
                OnArrivedAtTarget();
            }
        }

        void ProcessWalkingTick()
        {
            if (currentPath == null || pathIndex >= currentPath.Count)
            {
                // Emit final move from last sidewalk waypoint into building center
                if (state == SimState.WalkingToTarget && entryMovePending)
                {
                    Vector3 lastWaypointPos = CurrentPos();
                    entryMovePending = false;
                    float entryDist = Vector3.Distance(lastWaypointPos, targetPos);
                    float entryCost = Mathf.Max(2f, entryDist * TicksPerWorldUnit);
                    float entryDuration = entryCost * tickInterval;
                    int entryCostRounded = Mathf.RoundToInt(entryCost);
                    eventStream.Enqueue(SimEvent.Move(
                        lastWaypointPos, targetPos, entryDuration, entryCost, "enter",
                        "building_center", ticksElapsed + entryCostRounded, ticksRemaining - entryCostRounded));
                    ticksElapsed += entryCostRounded;
                    ticksRemaining -= entryCostRounded;
                }
                OnArrivedAtDestination();
                return;
            }

            string currentNodeId = currentPath[pathIndex];
            if (!waypointGraph.Nodes.TryGetValue(currentNodeId, out var node))
            {
                eventStream.Enqueue(SimEvent.NoPathEvent($"Path node missing in graph: {currentNodeId}"));
                SetState(SimState.Complete);
                return;
            }

            float linkCost = 1f;
            string linkType = "walk";
            Vector3 fromPos = CurrentPos();

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
                            fromPos = prevNode.localPos;
                            break;
                        }
                    }
                }
            }
            else
            {
                // First node: no previous link, compute cost from actual distance
                // (handles exit from building center when walking home)
                float dist = Vector3.Distance(fromPos, node.localPos);
                linkCost = Mathf.Max(2f, dist * TicksPerWorldUnit);
                linkType = state == SimState.WalkingHome ? "exit" : "start";
            }

            Vector3 toPos = node.localPos;
            float duration = linkCost * tickInterval;
            int linkCostRounded = Mathf.RoundToInt(linkCost);

            eventStream.Enqueue(SimEvent.Move(
                fromPos, toPos, duration, linkCost, linkType,
                node.id, ticksElapsed + linkCostRounded, ticksRemaining - linkCostRounded));

            ticksElapsed += linkCostRounded;
            ticksRemaining -= linkCostRounded;

            pathIndex++;

            if (ticksRemaining <= 0)
            {
                eventStream.Enqueue(SimEvent.TickExhausted(ticksElapsed));
                OnArrivedAtDestination();
            }
        }

        Vector3 CurrentPos()
        {
            if (currentPath != null && pathIndex > 0 && pathIndex <= currentPath.Count)
            {
                if (waypointGraph.Nodes.TryGetValue(currentPath[pathIndex - 1], out var n))
                    return n.localPos;
            }
            // When walking home, Vinny starts from the target building center, not HQ
            if (state == SimState.WalkingHome)
                return targetPos;
            return startPos;
        }

        void FindPathToTarget()
        {
            currentPath = pathfinder.FindPathBlockToBlock(
                startBlockId, startPos,
                targetBlockId, targetPos);

            if (currentPath == null || currentPath.Count == 0)
            {
                eventStream.Enqueue(SimEvent.NoPathEvent(
                    $"No path found {startBlockId} to {targetBlockId}!"));
                SetState(SimState.Complete);
                return;
            }

            if (!ValidatePathContinuity(currentPath, "to_target"))
            {
                eventStream.Enqueue(SimEvent.NoPathEvent(
                    $"Invalid path continuity {startBlockId} to {targetBlockId}!"));
                SetState(SimState.Complete);
                return;
            }

            pathIndex = 0;
            entryMovePending = true;  // need to walk into building center after last sidewalk waypoint
            eventStream.Enqueue(SimEvent.PathFoundEvent(currentPath.Count));
            LogPathTrace("to_target", startBlockId, targetBlockId, currentPath);
        }

        void FindPathHome()
        {
            currentPath = pathfinder.FindPathBlockToBlock(
                targetBlockId, targetPos,
                startBlockId, startPos);

            if (currentPath == null || currentPath.Count == 0)
            {
                eventStream.Enqueue(SimEvent.NoPathEvent(
                    $"No path home {targetBlockId} to {startBlockId}!"));
                SetState(SimState.Complete);
                return;
            }

            if (!ValidatePathContinuity(currentPath, "to_home"))
            {
                eventStream.Enqueue(SimEvent.NoPathEvent(
                    $"Invalid path continuity {targetBlockId} to {startBlockId}!"));
                SetState(SimState.Complete);
                return;
            }

            pathIndex = 0;
            eventStream.Enqueue(SimEvent.PathFoundEvent(currentPath.Count));
            LogPathTrace("to_home", targetBlockId, startBlockId, currentPath);
        }

        void OnArrivedAtDestination()
        {
            if (state == SimState.WalkingToTarget)
            {
                eventStream.Enqueue(SimEvent.Arrive(targetBlockId, ticksElapsed, ticksRemaining));

                // Enter dialog/action phase — original game spends ticks at target
                int actionTicks = GetOrderActionTicks(activeOrder);
                if (actionTicks > 0)
                {
                    dialogTotalTicks = actionTicks;
                    dialogTicksRemaining = actionTicks;
                    SetState(SimState.DialogPhase);
                    eventStream.Enqueue(SimEvent.DialogStartEvent(
                        activeOrder.orderType, targetBlockId, actionTicks,
                        ticksElapsed, ticksRemaining));
                }
                else
                {
                    // No dialog phase (e.g. stand order) — resolve immediately
                    SetState(SimState.ResolvingOrder);
                }
            }
            else if (state == SimState.WalkingHome)
            {
                eventStream.Enqueue(SimEvent.Arrive(startBlockId, ticksElapsed, ticksRemaining));
                eventStream.Enqueue(SimEvent.WeekCompleteEvent(ticksElapsed));
                SetState(SimState.Complete);
            }
        }

        void OnArrivedAtTarget()
        {
            ResolveOrderAtTarget();
            SetState(SimState.WalkingHome);
            FindPathHome();
        }

        void ProcessDialogTick()
        {
            dialogTicksRemaining--;
            ticksElapsed++;
            ticksRemaining--;

            // Emit periodic dialog progress events
            if (dialogTicksRemaining % 50 == 0 && dialogTicksRemaining > 0)
            {
                eventStream.Enqueue(SimEvent.DialogProgressEvent(
                    activeOrder.orderType, targetBlockId,
                    dialogTicksRemaining, dialogTotalTicks,
                    ticksElapsed, ticksRemaining));
            }

            if (dialogTicksRemaining <= 0)
            {
                eventStream.Enqueue(SimEvent.DialogEndEvent(
                    activeOrder.orderType, targetBlockId,
                    ticksElapsed, ticksRemaining));
                SetState(SimState.ResolvingOrder);
            }
            else if (ticksRemaining <= 0)
            {
                eventStream.Enqueue(SimEvent.TickExhausted(ticksElapsed));
                SetState(SimState.Complete);
            }
        }

        int GetOrderActionTicks(Order order)
        {
            if (order == null) return 0;
            if (OrderActionTicks.TryGetValue(order.orderType, out int ticks))
                return ticks;
            return 100; // default fallback for unknown orders
        }

        void ResolveOrderAtTarget()
        {
            if (activeOrder == null)
            {
                eventStream.Enqueue(SimEvent.OrderResolved(
                    "none", targetBlockId, false, "No active order", ticksElapsed, ticksRemaining));
                return;
            }

            var hood = gameEngine?.FindHood(activeOrder.hoodId);
            if (hood == null)
            {
                eventStream.Enqueue(SimEvent.OrderResolved(
                    activeOrder.orderType, targetBlockId, false,
                    $"Could not find hood {activeOrder.hoodId}", ticksElapsed, ticksRemaining));
                return;
            }

            if (gameEngine?.blocks == null || !gameEngine.blocks.TryGetValue(targetBlockId, out var block))
            {
                eventStream.Enqueue(SimEvent.OrderResolved(
                    activeOrder.orderType, targetBlockId, false,
                    $"Could not find block {targetBlockId}", ticksElapsed, ticksRemaining));
                return;
            }

            switch (activeOrder.orderType)
            {
                case "extort":
                    var (success, details, _) = CrimeSystem.ResolveExtortion(
                        hood, block, gameEngine.npcs, gameEngine.businesses, gameEngine.data.constants);

                    if (success)
                    {
                        if (block.ownerGang == null || block.extortionStrength < 30)
                        {
                            block.ownerGang = activeOrder.gangId;
                            block.extortionStrength = Mathf.Max(block.extortionStrength, 20);
                        }
                        else
                        {
                            block.extortionStrength = Mathf.Min(100, block.extortionStrength + 10);
                        }
                    }

                    eventStream.Enqueue(SimEvent.OrderResolved(
                        activeOrder.orderType, targetBlockId, success, details,
                        ticksElapsed, ticksRemaining));
                    break;

                default:
                    eventStream.Enqueue(SimEvent.OrderResolved(
                        activeOrder.orderType, targetBlockId, false,
                        $"Order type '{activeOrder.orderType}' not yet implemented",
                        ticksElapsed, ticksRemaining));
                    break;
            }
        }

        void LogPathTrace(string label, string fromBlock, string toBlock, List<string> path)
        {
            if (path == null || path.Count == 0)
                return;

            int sample = Mathf.Min(6, path.Count);
            var head = new List<string>(sample);
            for (int i = 0; i < sample; i++)
            {
                string nodeId = path[i];
                if (waypointGraph.Nodes.TryGetValue(nodeId, out var node))
                    head.Add($"{nodeId}({node.localPos.x:F1},{node.localPos.z:F1})");
                else
                    head.Add($"{nodeId}(missing)");
            }

            string tail = string.Empty;
            if (path.Count > sample)
            {
                string lastId = path[path.Count - 1];
                if (waypointGraph.Nodes.TryGetValue(lastId, out var lastNode))
                    tail = $" ... {lastId}({lastNode.localPos.x:F1},{lastNode.localPos.z:F1})";
                else
                    tail = $" ... {lastId}(missing)";
            }

            Debug.Log($"[SimulationManager] PathTrace {label} {fromBlock}->{toBlock} nodes={path.Count} route={string.Join(" -> ", head)}{tail}");
        }

        bool ValidatePathContinuity(List<string> path, string label)
        {
            if (path == null || path.Count <= 1)
                return true;

            for (int i = 0; i < path.Count - 1; i++)
            {
                string current = path[i];
                string next = path[i + 1];

                if (!waypointGraph.Nodes.TryGetValue(current, out var currentNode))
                {
                    Debug.LogWarning($"[SimulationManager] PathTrace {label} invalid: missing node '{current}' at index {i}");
                    return false;
                }
                if (!waypointGraph.Nodes.TryGetValue(next, out var nextNode))
                {
                    Debug.LogWarning($"[SimulationManager] PathTrace {label} invalid: missing node '{next}' at index {i + 1}");
                    return false;
                }

                bool linked = false;
                foreach (var link in currentNode.links)
                {
                    if (link.targetId == next)
                    {
                        linked = true;
                        break;
                    }
                }

                if (!linked)
                {
                    Debug.LogWarning(
                        $"[SimulationManager] PathTrace {label} invalid: no direct link {current} -> {next} (index {i}->{i + 1}) | " +
                        $"from: block={currentNode.blockId} edge={currentNode.edgeIndex} type={currentNode.type} pos=({currentNode.localPos.x:F2},{currentNode.localPos.z:F2}) | " +
                        $"to: block={nextNode.blockId} edge={nextNode.edgeIndex} type={nextNode.type} pos=({nextNode.localPos.x:F2},{nextNode.localPos.z:F2})");
                    return false;
                }
            }

            return true;
        }

        void SetState(SimState newState)
        {
            state = newState;
        }
    }
}
