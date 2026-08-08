using System;
using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Simple street-centerline graph for vehicle navigation — separate from WaypointGraph,
    /// which traces sidewalks/crosswalks around the perimeter of each block for pedestrians.
    ///
    /// Nodes sit at street intersections (the grid corners BETWEEN blocks), not on sidewalks.
    /// For a rows x cols block layout, this produces a (rows+1) x (cols+1) intersection grid,
    /// connected along the road grid (each intersection links to its N/E/S/W neighbor).
    ///
    /// This is a first-pass approximation: it assumes a fully-populated rectangular block grid
    /// (true for the current city_template.json). For irregular/sparse layouts, intersections
    /// would need to be pruned to only where a real block edge exists alongside that segment.
    /// </summary>
    public class RoadGraph
    {
        public class RoadNode
        {
            public string id;
            public Vector3 localPos;
            public List<RoadLink> links = new();

            public RoadNode(string id, Vector3 pos)
            {
                this.id = id;
                this.localPos = pos;
            }
        }

        public class RoadLink
        {
            public string targetId;
            public float distance;

            public RoadLink(string targetId, float distance)
            {
                this.targetId = targetId;
                this.distance = distance;
            }
        }

        private readonly Dictionary<string, RoadNode> nodes = new();
        public IReadOnlyDictionary<string, RoadNode> Nodes => nodes;

        /// <summary>
        /// Build the intersection grid from a CityLayout. Uses the same block spacing/centering
        /// convention as WaypointGraph.GenerateFromLayout so road nodes align with the rendered city.
        /// </summary>
        public void GenerateFromLayout(CityLayout layout, float spacing)
        {
            nodes.Clear();

            if (layout == null || layout.blocks == null || layout.blocks.Length == 0)
            {
                Debug.LogWarning("[RoadGraph] No layout blocks to generate from");
                return;
            }

            int minRow = int.MaxValue, maxRow = int.MinValue, minCol = int.MaxValue, maxCol = int.MinValue;
            foreach (var b in layout.blocks)
            {
                if (b.row < minRow) minRow = b.row;
                if (b.row > maxRow) maxRow = b.row;
                if (b.col < minCol) minCol = b.col;
                if (b.col > maxCol) maxCol = b.col;
            }
            float centerRow = (minRow + maxRow) * 0.5f;
            float centerCol = (minCol + maxCol) * 0.5f;

            // Intersections form a lattice one larger than the block grid in each dimension —
            // intersection (r, c) sits at the corner shared by blocks (r-1,c-1)/(r-1,c)/(r,c-1)/(r,c).
            for (int r = minRow; r <= maxRow + 1; r++)
            {
                for (int c = minCol; c <= maxCol + 1; c++)
                {
                    float x = (c - 0.5f - centerCol) * spacing;
                    float z = -(r - 0.5f - centerRow) * spacing;
                    string id = IntersectionId(r, c);
                    nodes[id] = new RoadNode(id, new Vector3(x, 0f, z));
                }
            }

            // Connect each intersection to its East and South neighbor (reciprocal links added both ways).
            for (int r = minRow; r <= maxRow + 1; r++)
            {
                for (int c = minCol; c <= maxCol + 1; c++)
                {
                    string current = IntersectionId(r, c);

                    if (c < maxCol + 1)
                        LinkBothWays(current, IntersectionId(r, c + 1));

                    if (r < maxRow + 1)
                        LinkBothWays(current, IntersectionId(r + 1, c));
                }
            }

            Debug.Log($"[RoadGraph] Generated {nodes.Count} intersections, {CountLinks()} links for {layout.blocks.Length} blocks");
        }

        private void LinkBothWays(string aId, string bId)
        {
            if (!nodes.TryGetValue(aId, out var a) || !nodes.TryGetValue(bId, out var b)) return;
            float dist = Vector3.Distance(a.localPos, b.localPos);
            a.links.Add(new RoadLink(bId, dist));
            b.links.Add(new RoadLink(aId, dist));
        }

        private static string IntersectionId(int row, int col) => $"i_r{row}_c{col}";

        private int CountLinks()
        {
            int total = 0;
            foreach (var n in nodes.Values) total += n.links.Count;
            return total;
        }

        /// <summary>Random node ID, or null if the graph is empty.</summary>
        public string RandomNodeId()
        {
            if (nodes.Count == 0) return null;
            int idx = UnityEngine.Random.Range(0, nodes.Count);
            int i = 0;
            foreach (var id in nodes.Keys)
            {
                if (i == idx) return id;
                i++;
            }
            return null;
        }

        /// <summary>Pick a random neighbor of the given node, excluding the given "came from" node when possible.</summary>
        public string RandomNeighbor(string nodeId, string avoidId = null)
        {
            if (!nodes.TryGetValue(nodeId, out var node) || node.links.Count == 0) return null;

            if (node.links.Count > 1 && avoidId != null)
            {
                var candidates = new List<RoadLink>();
                foreach (var link in node.links)
                    if (link.targetId != avoidId) candidates.Add(link);

                if (candidates.Count > 0)
                    return candidates[UnityEngine.Random.Range(0, candidates.Count)].targetId;
            }

            return node.links[UnityEngine.Random.Range(0, node.links.Count)].targetId;
        }
    }
}
