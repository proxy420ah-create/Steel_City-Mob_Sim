using System;
using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Dijkstra map / next-hop table for one destination: a single reverse-Dijkstra from
    /// the goal node fills Next(nodeId) = the neighbor to step toward, for EVERY node.
    ///
    /// Agents hold no baked path — at each node they just query Next(). Path compute is
    /// therefore proportional to distinct destinations (one field per goal), not agents:
    /// 200 citizens heading downtown share one field.
    ///
    /// Works for both graphs — WaypointGraph (pedestrian sidewalks/crosswalks, cost =
    /// baseTickCost) and RoadGraph (vehicle corridors, cost = distance) — so pedestrians
    /// and cars navigate with the same "which neighbor next?" interface.
    /// </summary>
    public class FlowField
    {
        private readonly Dictionary<string, string> nextHop = new();
        private readonly Dictionary<string, float> costToGoal = new();

        public string GoalId { get; private set; }

        /// <summary>Largest finite cost-to-goal in the field — for debug gradient bands.</summary>
        public float MaxCost { get; private set; }

        /// <summary>Nodes that can reach the goal (the goal itself is reachable by definition).</summary>
        public int ReachableCount => nextHop.Count + 1;

        /// <summary>The next-hop table itself (node → hop) — for debug rendering of the field.</summary>
        public IEnumerable<KeyValuePair<string, string>> Hops => nextHop;

        /// <summary>The neighbor to step toward to reach the goal, or null if unreachable / at goal.</summary>
        public string Next(string nodeId)
            => nextHop.TryGetValue(nodeId, out var n) ? n : null;

        /// <summary>Total remaining cost to the goal from a node; +inf if unreachable.</summary>
        public float CostFrom(string nodeId)
            => costToGoal.TryGetValue(nodeId, out var c) ? c : float.PositiveInfinity;

        public bool Reaches(string nodeId)
            => nodeId == GoalId || nextHop.ContainsKey(nodeId);

        /// <summary>
        /// Walk the next-hop table into an explicit node-id list — same shape Pathfinder.
        /// FindPath returns, so existing consumers (baked paths, waypoint rays, tick sim)
        /// take flow-field output with zero changes.
        /// </summary>
        public List<string> Materialize(string fromId)
        {
            if (!Reaches(fromId)) return null;

            var path = new List<string> { fromId };
            string cur = fromId;
            int guard = nextHop.Count + 1;
            while (cur != GoalId && guard-- > 0)
            {
                cur = nextHop[cur];
                path.Add(cur);
            }
            return cur == GoalId ? path : null;
        }

        public static FlowField Build(WaypointGraph graph, string goalId)
        {
            if (graph == null || !graph.Nodes.ContainsKey(goalId)) return null;
            return BuildCore(goalId,
                id => graph.Nodes.TryGetValue(id, out var n)
                    ? MapLinks(n.links) : EmptyLinks());
        }

        public static FlowField Build(RoadGraph graph, string goalId)
        {
            if (graph == null || !graph.Nodes.ContainsKey(goalId)) return null;
            return BuildCore(goalId,
                id => graph.Nodes.TryGetValue(id, out var n)
                    ? MapLinks(n.links) : EmptyLinks());
        }

        // --- shared core: reverse-Dijkstra expanding outward from the goal. When edge
        // (target → cur) relaxes, target's best route to the goal goes through cur.
        // Both graphs' links are bidirectional, so edge direction is safe to invert.
        private static FlowField BuildCore(string goalId,
            Func<string, IEnumerable<(string target, float cost)>> linksOf)
        {
            var field = new FlowField { GoalId = goalId };
            var dist = field.costToGoal;
            var open = new PriorityQueue<string, float>();

            dist[goalId] = 0f;
            open.Enqueue(goalId, 0f);

            while (open.Count > 0)
            {
                string cur = open.Dequeue();
                float d = dist[cur];
                foreach (var (target, cost) in linksOf(cur))
                {
                    float nd = d + cost;
                    if (!dist.TryGetValue(target, out float existing) || nd < existing)
                    {
                        dist[target] = nd;
                        field.nextHop[target] = cur;
                        if (nd > field.MaxCost) field.MaxCost = nd;
                        open.Enqueue(target, nd);
                    }
                }
            }
            return field;
        }

        private static IEnumerable<(string, float)> MapLinks(List<WaypointLink> links)
        {
            foreach (var l in links) yield return (l.targetId, l.baseTickCost);
        }

        private static IEnumerable<(string, float)> MapLinks(List<RoadGraph.RoadLink> links)
        {
            foreach (var l in links) yield return (l.targetId, l.distance);
        }

        private static IEnumerable<(string, float)> EmptyLinks()
        {
            yield break;
        }
    }
}
