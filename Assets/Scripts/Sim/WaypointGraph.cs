using System;
using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    public enum WaypointType
    {
        SidewalkCorner,
        SidewalkMid,
        CrosswalkCorner
    }

    [Serializable]
    public class WaypointLink
    {
        public string targetId;
        public int baseTickCost;
        public float riskWeight;
        public WaypointType type;

        public WaypointLink(string targetId, int cost, float risk, WaypointType type)
        {
            this.targetId = targetId;
            this.baseTickCost = cost;
            this.riskWeight = risk;
            this.type = type;
        }
    }

    [Serializable]
    public class WaypointNode
    {
        public string id;
        public Vector3 localPos;
        public WaypointType type;
        public string blockId;
        public int edgeIndex;
        public List<WaypointLink> links = new();

        public WaypointNode(string id, Vector3 pos, WaypointType type, string blockId, int edgeIndex)
        {
            this.id = id;
            this.localPos = pos;
            this.type = type;
            this.blockId = blockId;
            this.edgeIndex = edgeIndex;
        }
    }

    public class WaypointGraph
    {
        private readonly Dictionary<string, WaypointNode> nodes = new();
        private readonly Dictionary<string, List<string>> blockNodeIndex = new();
        private Vector3 mapRootOffset;

        public IReadOnlyDictionary<string, WaypointNode> Nodes => nodes;

        public void GenerateFromLayout(
            CityLayout layout,
            float spacing,
            float groundTileSize,
            float sidewalkWidth,
            Vector3 mapRootPos)
        {
            nodes.Clear();
            blockNodeIndex.Clear();
            mapRootOffset = mapRootPos;

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

            float halfTile = groundTileSize * 0.5f;
            float halfSidewalk = sidewalkWidth * 0.5f;

            foreach (var b in layout.blocks)
            {
                float bx = (b.col - centerCol) * spacing;
                float bz = -(b.row - centerRow) * spacing;

                blockNodeIndex[b.block_id] = new List<string>();

                Vector3[] corners = new Vector3[4];
                Vector3[] mids = new Vector3[4];

                for (int e = 0; e < 4; e++)
                {
                    float dx = 0, dz = 0, mx = 0, mz = 0;
                    switch (e)
                    {
                        case 0: dx = -halfTile + halfSidewalk; dz = halfTile - halfSidewalk; mx = 0; mz = halfTile - halfSidewalk; break;  // N
                        case 1: dx = halfTile - halfSidewalk; dz = halfTile - halfSidewalk; mx = halfTile - halfSidewalk; mz = 0; break;   // E
                        case 2: dx = halfTile - halfSidewalk; dz = -halfTile + halfSidewalk; mx = 0; mz = -halfTile + halfSidewalk; break; // S
                        case 3: dx = -halfTile + halfSidewalk; dz = -halfTile + halfSidewalk; mx = -halfTile + halfSidewalk; mz = 0; break;// W
                    }

                    var cornerPos = new Vector3(bx + dx, 0, bz + dz);
                    var midPos = new Vector3(bx + mx, 0, bz + mz);

                    string cornerId = $"{b.block_id}_c{e}";
                    string midId = $"{b.block_id}_m{e}";

                    nodes[cornerId] = new WaypointNode(cornerId, cornerPos, WaypointType.SidewalkCorner, b.block_id, e);
                    nodes[midId] = new WaypointNode(midId, midPos, WaypointType.SidewalkMid, b.block_id, e);

                    blockNodeIndex[b.block_id].Add(cornerId);
                    blockNodeIndex[b.block_id].Add(midId);

                    int cost = Mathf.Max(2, Mathf.RoundToInt(Vector3.Distance(cornerPos, midPos) * 3f));
                    nodes[cornerId].links.Add(new WaypointLink(midId, cost, 0f, WaypointType.SidewalkMid));
                    nodes[midId].links.Add(new WaypointLink(cornerId, cost, 0f, WaypointType.SidewalkCorner));
                }

                // Connect each mid to BOTH adjacent corners, forming a perimeter loop:
                // c0 ↔ m0 ↔ c1 ↔ m1 ↔ c2 ↔ m2 ↔ c3 ↔ m3 ↔ c0
                for (int e = 0; e < 4; e++)
                {
                    string mid = $"{b.block_id}_m{e}";
                    string nextCorner = $"{b.block_id}_c{(e + 1) % 4}";

                    int cost = Mathf.Max(2, Mathf.RoundToInt(Vector3.Distance(nodes[mid].localPos, nodes[nextCorner].localPos) * 3f));
                    nodes[mid].links.Add(new WaypointLink(nextCorner, cost, 0f, WaypointType.SidewalkCorner));
                    nodes[nextCorner].links.Add(new WaypointLink(mid, cost, 0f, WaypointType.SidewalkMid));
                }
            }

            foreach (var b1 in layout.blocks)
            {
                foreach (var b2 in layout.blocks)
                {
                    if (b1.block_id.CompareTo(b2.block_id) >= 0) continue;

                    bool adjacent = (b1.row == b2.row && Math.Abs(b1.col - b2.col) == 1) ||
                                    (b1.col == b2.col && Math.Abs(b1.row - b2.row) == 1);
                    if (!adjacent) continue;

                    int e1, e2;
                    if (b2.col > b1.col) { e1 = 1; e2 = 3; }
                    else if (b2.col < b1.col) { e1 = 3; e2 = 1; }
                    else if (b2.row > b1.row) { e1 = 0; e2 = 2; }
                    else { e1 = 2; e2 = 0; }

                    string c1 = $"{b1.block_id}_c{e1}";
                    string c2 = $"{b2.block_id}_c{e2}";
                    string m1 = $"{b1.block_id}_m{e1}";
                    string m2 = $"{b2.block_id}_m{e2}";

                    // Mid-edge crosswalk: straight across at the middle of the block edge
                    nodes[m1].links.Add(new WaypointLink(m2, 16, 0f, WaypointType.CrosswalkCorner));
                    nodes[m2].links.Add(new WaypointLink(m1, 16, 0f, WaypointType.CrosswalkCorner));

                    // Corner crosswalks: connect matching corners straight across the street.
                    // Corners go clockwise: c0=NW, c1=NE, c2=SE, c3=SW.
                    // Edge e has corners c{e} and c{(e+1)%4}.
                    // Facing edges have corners in OPPOSITE order along the shared boundary,
                    // so first corner of e1 connects to SECOND corner of e2 (and vice versa).
                    //
                    // Example: b1 east edge (e1=1) has corners c1(NE), c2(SE)
                    //          b2 west edge (e2=3) has corners c3(SW), c0(NW)
                    //          Correct: c1↔c0 (north side), c2↔c3 (south side)
                    string c1a = $"{b1.block_id}_c{e1}";              // first corner of b1's edge
                    string c1b = $"{b1.block_id}_c{(e1 + 1) % 4}";    // second corner of b1's edge
                    string c2a = $"{b2.block_id}_c{e2}";              // first corner of b2's edge
                    string c2b = $"{b2.block_id}_c{(e2 + 1) % 4}";    // second corner of b2's edge

                    // First corner of b1 ↔ SECOND corner of b2 (same side of street)
                    nodes[c1a].links.Add(new WaypointLink(c2b, 16, 0f, WaypointType.CrosswalkCorner));
                    nodes[c2b].links.Add(new WaypointLink(c1a, 16, 0f, WaypointType.CrosswalkCorner));
                    // Second corner of b1 ↔ FIRST corner of b2 (same side of street)
                    nodes[c1b].links.Add(new WaypointLink(c2a, 16, 0f, WaypointType.CrosswalkCorner));
                    nodes[c2a].links.Add(new WaypointLink(c1b, 16, 0f, WaypointType.CrosswalkCorner));
                }
            }

            Debug.Log($"[WaypointGraph] Generated {nodes.Count} nodes, " +
                $"{CountLinks()} links for {layout.blocks.Length} blocks");
        }

        private int CountLinks()
        {
            int total = 0;
            foreach (var n in nodes.Values) total += n.links.Count;
            return total;
        }

        public Vector3 GetWorldPos(string nodeId)
        {
            return nodes[nodeId].localPos + mapRootOffset;
        }

        public string FindNearestNode(Vector3 localPos, string preferBlockId = null)
        {
            string best = null;
            float bestDist = float.MaxValue;

            if (preferBlockId != null && blockNodeIndex.TryGetValue(preferBlockId, out var blockNodes))
            {
                foreach (var nid in blockNodes)
                {
                    float d = (nodes[nid].localPos - localPos).sqrMagnitude;
                    if (d < bestDist) { bestDist = d; best = nid; }
                }
            }

            if (best != null) return best;

            foreach (var (nid, node) in nodes)
            {
                float d = (node.localPos - localPos).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = nid; }
            }
            return best;
        }

        public List<string> GetBlockNodes(string blockId)
        {
            return blockNodeIndex.TryGetValue(blockId, out var list) ? list : null;
        }
    }
}
