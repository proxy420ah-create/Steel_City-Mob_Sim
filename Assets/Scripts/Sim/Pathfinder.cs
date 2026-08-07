using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    public class Pathfinder
    {
        private readonly WaypointGraph graph;

        public Pathfinder(WaypointGraph graph)
        {
            this.graph = graph;
        }

        public List<string> FindPath(string startNodeId, string endNodeId)
        {
            if (!graph.Nodes.ContainsKey(startNodeId) || !graph.Nodes.ContainsKey(endNodeId))
            {
                Debug.LogWarning($"[Pathfinder] Invalid node IDs: {startNodeId} to {endNodeId}");
                return null;
            }

            if (startNodeId == endNodeId)
                return new List<string> { startNodeId };

            var openSet = new PriorityQueue<string, float>();
            var cameFrom = new Dictionary<string, string>();
            var gScore = new Dictionary<string, float>();
            var closed = new HashSet<string>();

            gScore[startNodeId] = 0f;
            openSet.Enqueue(startNodeId, Heuristic(startNodeId, endNodeId));

            int iterations = 0;
            int maxIterations = graph.Nodes.Count * 4;

            while (openSet.Count > 0 && iterations++ < maxIterations)
            {
                string current = openSet.Dequeue();

                if (current == endNodeId)
                    return ReconstructPath(cameFrom, current);

                if (closed.Contains(current)) continue;
                closed.Add(current);

                var node = graph.Nodes[current];
                foreach (var link in node.links)
                {
                    if (closed.Contains(link.targetId)) continue;
                    if (!graph.Nodes.ContainsKey(link.targetId)) continue;

                    float tentativeG = gScore[current] + link.baseTickCost;

                    if (!gScore.TryGetValue(link.targetId, out float existing) || tentativeG < existing)
                    {
                        cameFrom[link.targetId] = current;
                        gScore[link.targetId] = tentativeG;
                        float f = tentativeG + Heuristic(link.targetId, endNodeId);
                        openSet.Enqueue(link.targetId, f);
                    }
                }
            }

            Debug.LogWarning($"[Pathfinder] No path found {startNodeId} to {endNodeId} after {iterations} iterations");
            return null;
        }

        public List<string> FindPathBlockToBlock(
            string startBlockId, Vector3 startPos,
            string endBlockId, Vector3 endPos)
        {
            string startNode = graph.FindNearestNode(startPos, startBlockId);
            string endNode = graph.FindNearestNode(endPos, endBlockId);

            if (startNode == null || endNode == null)
            {
                Debug.LogWarning($"[Pathfinder] Could not find nearest nodes for {startBlockId} to {endBlockId}");
                return null;
            }

            var path = FindPath(startNode, endNode);
            if (path != null)
            {
                int totalTicks = 0;
                for (int i = 0; i < path.Count - 1; i++)
                {
                    var node = graph.Nodes[path[i]];
                    foreach (var link in node.links)
                    {
                        if (link.targetId == path[i + 1])
                        {
                            totalTicks += link.baseTickCost;
                            break;
                        }
                    }
                }
                Debug.Log($"[Pathfinder] Path {startBlockId} to {endBlockId}: " +
                    $"{path.Count} nodes, ~{totalTicks} ticks, " +
                    $"start={startNode}, end={endNode}");
            }
            return path;
        }

        private float Heuristic(string a, string b)
        {
            return (graph.Nodes[a].localPos - graph.Nodes[b].localPos).magnitude * 40f;
        }

        private List<string> ReconstructPath(Dictionary<string, string> cameFrom, string current)
        {
            var path = new List<string> { current };
            while (cameFrom.TryGetValue(current, out string prev))
            {
                path.Add(prev);
                current = prev;
            }
            path.Reverse();
            return path;
        }
    }

    public class PriorityQueue<TElement, TPriority> where TPriority : System.IComparable<TPriority>
    {
        private readonly List<(TElement, TPriority)> items = new();

        public int Count => items.Count;

        public void Enqueue(TElement element, TPriority priority)
        {
            items.Add((element, priority));
            int i = items.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (items[i].Item2.CompareTo(items[parent].Item2) >= 0) break;
                (items[i], items[parent]) = (items[parent], items[i]);
                i = parent;
            }
        }

        public TElement Dequeue()
        {
            var result = items[0].Item1;
            items[0] = items[^1];
            items.RemoveAt(items.Count - 1);

            int i = 0;
            while (true)
            {
                int left = 2 * i + 1, right = 2 * i + 2, smallest = i;
                if (left < items.Count && items[left].Item2.CompareTo(items[smallest].Item2) < 0) smallest = left;
                if (right < items.Count && items[right].Item2.CompareTo(items[smallest].Item2) < 0) smallest = right;
                if (smallest == i) break;
                (items[i], items[smallest]) = (items[smallest], items[i]);
                i = smallest;
            }
            return result;
        }
    }
}
