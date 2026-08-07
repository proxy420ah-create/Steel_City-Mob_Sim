using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    public enum SimEventType
    {
        HoodMove,
        HoodArrive,
        DialogStart,
        DialogProgress,
        DialogEnd,
        OrderResolve,
        Wander,
        TrafficWait,
        TickBudgetExhausted,
        WeekComplete,
        PathFound,
        NoPath
    }

    public class SimEvent
    {
        public SimEventType type;
        public int tickElapsed;
        public int tickRemaining;

        public Vector3 fromPos;
        public Vector3 toPos;
        public float duration;
        public int tickCost;
        public string linkType;
        public string nodeId;
        public string blockId;
        public string orderType;
        public bool success;
        public string details;
        public int wanderTicks;
        public int pathNodeCount;
        public int dialogTicksRemaining;
        public int dialogTotalTicks;
        public string message;

        public static SimEvent Move(Vector3 from, Vector3 to, float duration, int tickCost, string linkType,
                                     string nodeId, int tickElapsed, int tickRemaining)
        {
            return new SimEvent
            {
                type = SimEventType.HoodMove,
                fromPos = from,
                toPos = to,
                duration = duration,
                tickCost = tickCost,
                linkType = linkType,
                nodeId = nodeId,
                tickElapsed = tickElapsed,
                tickRemaining = tickRemaining
            };
        }

        public static SimEvent Arrive(string blockId, int tickElapsed, int tickRemaining)
        {
            return new SimEvent
            {
                type = SimEventType.HoodArrive,
                blockId = blockId,
                tickElapsed = tickElapsed,
                tickRemaining = tickRemaining
            };
        }

        public static SimEvent OrderResolved(string orderType, string blockId, bool success,
                                              string details, int tickElapsed, int tickRemaining)
        {
            return new SimEvent
            {
                type = SimEventType.OrderResolve,
                orderType = orderType,
                blockId = blockId,
                success = success,
                details = details,
                tickElapsed = tickElapsed,
                tickRemaining = tickRemaining
            };
        }

        public static SimEvent WanderEvent(int ticks, Vector3 pos, int tickElapsed, int tickRemaining)
        {
            return new SimEvent
            {
                type = SimEventType.Wander,
                wanderTicks = ticks,
                fromPos = pos,
                tickElapsed = tickElapsed,
                tickRemaining = tickRemaining
            };
        }

        public static SimEvent TrafficWaitEvent(int ticks, Vector3 pos, int tickElapsed, int tickRemaining)
        {
            return new SimEvent
            {
                type = SimEventType.TrafficWait,
                wanderTicks = ticks,
                fromPos = pos,
                tickElapsed = tickElapsed,
                tickRemaining = tickRemaining
            };
        }

        public static SimEvent TickExhausted(int tickElapsed)
        {
            return new SimEvent
            {
                type = SimEventType.TickBudgetExhausted,
                tickElapsed = tickElapsed,
                tickRemaining = 0
            };
        }

        public static SimEvent WeekCompleteEvent(int tickElapsed)
        {
            return new SimEvent
            {
                type = SimEventType.WeekComplete,
                tickElapsed = tickElapsed,
                tickRemaining = 0
            };
        }

        public static SimEvent PathFoundEvent(int nodeCount)
        {
            return new SimEvent
            {
                type = SimEventType.PathFound,
                pathNodeCount = nodeCount
            };
        }

        public static SimEvent NoPathEvent(string message)
        {
            return new SimEvent
            {
                type = SimEventType.NoPath,
                message = message
            };
        }

        public static SimEvent DialogStartEvent(string orderType, string blockId, int totalTicks,
                                                  int tickElapsed, int tickRemaining)
        {
            return new SimEvent
            {
                type = SimEventType.DialogStart,
                orderType = orderType,
                blockId = blockId,
                dialogTotalTicks = totalTicks,
                dialogTicksRemaining = totalTicks,
                tickElapsed = tickElapsed,
                tickRemaining = tickRemaining
            };
        }

        public static SimEvent DialogProgressEvent(string orderType, string blockId,
                                                     int remaining, int total,
                                                     int tickElapsed, int tickRemaining)
        {
            return new SimEvent
            {
                type = SimEventType.DialogProgress,
                orderType = orderType,
                blockId = blockId,
                dialogTicksRemaining = remaining,
                dialogTotalTicks = total,
                tickElapsed = tickElapsed,
                tickRemaining = tickRemaining
            };
        }

        public static SimEvent DialogEndEvent(string orderType, string blockId,
                                               int tickElapsed, int tickRemaining)
        {
            return new SimEvent
            {
                type = SimEventType.DialogEnd,
                orderType = orderType,
                blockId = blockId,
                tickElapsed = tickElapsed,
                tickRemaining = tickRemaining
            };
        }
    }

    public class SimEventStream
    {
        private readonly Queue<SimEvent> queue = new();
        private readonly List<SimEvent> allEvents = new();

        public int Count => queue.Count;
        public IReadOnlyList<SimEvent> AllEvents => allEvents;

        public void Enqueue(SimEvent evt)
        {
            queue.Enqueue(evt);
            allEvents.Add(evt);
        }

        public SimEvent Dequeue()
        {
            return queue.Count > 0 ? queue.Dequeue() : null;
        }

        public SimEvent Peek()
        {
            return queue.Count > 0 ? queue.Peek() : null;
        }

        public void Clear()
        {
            queue.Clear();
            allEvents.Clear();
        }
    }
}
