using System;
using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    public enum WaypointType
    {
        SidewalkCorner,
        SidewalkMid,
        CrosswalkCorner,
        BridgeDeck          // link crosses onto/through a bridge block — river crossing chokepoint
    }

    [Serializable]
    public class WaypointLink
    {
        public string targetId;
        public float baseTickCost;
        public float riskWeight;
        public WaypointType type;

        public WaypointLink(string targetId, float cost, float risk, WaypointType type)
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
            Vector3 mapRootPos,
            VoxelCollisionWorld collisionWorld = null)
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

            // Terrain lookup (row,col → normalized terrain) for conditioned emission:
            //   oob   → no nodes (unwalkable, never a routing target)
            //   water → corners + promenade-side mids only; the mids on the channel
            //           mouth edges (flow axis ends) stand over open water — dropped
            //   bridge/mainstreet/land → all 8 nodes
            var terrainByCell = new Dictionary<Vector2Int, string>(layout.blocks.Length);
            foreach (var b in layout.blocks)
                terrainByCell[new Vector2Int(b.col, b.row)] = VoxelTerrainBuilder.NormalizeTerrain(b.terrain);
            string TerrAt(int col, int row) =>
                terrainByCell.TryGetValue(new Vector2Int(col, row), out var t) ? t : "land";

            foreach (var b in layout.blocks)
            {
                float bx = (b.col - centerCol) * spacing;
                float bz = -(b.row - centerRow) * spacing;

                blockNodeIndex[b.block_id] = new List<string>();

                string terr = TerrAt(b.col, b.row);
                if (terr == "oob") continue;   // no nodes — unwalkable

                // Water blocks: drop the mids on the flow-axis ends (channel mouths
                // — or cap walls for end cells). Corners always sit on promenade ends.
                bool[] dropMid = new bool[4];
                if (terr == "water")
                {
                    int mask = 0;
                    if (VoxelTerrainBuilder.IsChannelTerrain(TerrAt(b.col, b.row - 1))) mask |= 1; // N
                    if (VoxelTerrainBuilder.IsChannelTerrain(TerrAt(b.col + 1, b.row))) mask |= 2; // E
                    if (VoxelTerrainBuilder.IsChannelTerrain(TerrAt(b.col, b.row + 1))) mask |= 4; // S
                    if (VoxelTerrainBuilder.IsChannelTerrain(TerrAt(b.col - 1, b.row))) mask |= 8; // W
                    // Supported masks: 0,1,2,4,5,8,10. Corners/T/cross fall back to
                    // land visuals in the builder — keep full nodes there to match.
                    bool supported = mask == 0 || mask == 1 || mask == 2 || mask == 4 ||
                                     mask == 8 || mask == 5 || mask == 10;
                    if (supported)
                    {
                        bool flowEW = (mask & 0b1010) != 0;
                        if (flowEW) { dropMid[1] = dropMid[3] = true; }  // E,W mids over channel mouths
                        else        { dropMid[0] = dropMid[2] = true; }  // N,S mids
                    }
                }

                // Measure the real sidewalk band per edge from the generated terrain
                // voxels and lane at its centroid — the formula assumes a 1:1
                // sidewalkWidth ring which doesn't hold on promenade/quay edges.
                float[] inset = { halfTile - halfSidewalk, halfTile - halfSidewalk,
                                  halfTile - halfSidewalk, halfTile - halfSidewalk };
                if (collisionWorld != null && collisionWorld.IsInitialized)
                    for (int e = 0; e < 4; e++)
                        inset[e] = halfTile - MeasureSidewalkLane(bx, bz, e, halfTile,
                            sidewalkWidth, collisionWorld, mapRootPos, halfSidewalk);

                for (int e = 0; e < 4; e++)
                {
                    float dx = 0, dz = 0, mx = 0, mz = 0;
                    switch (e)
                    {
                        case 0: dx = -inset[3]; dz = inset[0]; mx = 0; mz = inset[0]; break;   // N: c0 NW
                        case 1: dx = inset[1]; dz = inset[0]; mx = inset[1]; mz = 0; break;    // E: c1 NE
                        case 2: dx = inset[1]; dz = -inset[2]; mx = 0; mz = -inset[2]; break;  // S: c2 SE
                        case 3: dx = -inset[3]; dz = -inset[2]; mx = -inset[3]; mz = 0; break; // W: c3 SW
                    }

                    var cornerPos = new Vector3(bx + dx, 0, bz + dz);
                    var midPos = new Vector3(bx + mx, 0, bz + mz);

                    string cornerId = $"{b.block_id}_c{e}";
                    string midId = $"{b.block_id}_m{e}";

                    nodes[cornerId] = new WaypointNode(cornerId, cornerPos, WaypointType.SidewalkCorner, b.block_id, e);
                    blockNodeIndex[b.block_id].Add(cornerId);

                    if (!dropMid[e])
                    {
                        nodes[midId] = new WaypointNode(midId, midPos, WaypointType.SidewalkMid, b.block_id, e);
                        blockNodeIndex[b.block_id].Add(midId);

                        float cost = Mathf.Max(2f, Vector3.Distance(cornerPos, midPos) * 3f);
                        nodes[cornerId].links.Add(new WaypointLink(midId, cost, 0f, WaypointType.SidewalkMid));
                        nodes[midId].links.Add(new WaypointLink(cornerId, cost, 0f, WaypointType.SidewalkCorner));
                    }
                }

                // Connect each mid to BOTH adjacent corners, forming a perimeter loop:
                // c0 ↔ m0 ↔ c1 ↔ m1 ↔ c2 ↔ m2 ↔ c3 ↔ m3 ↔ c0
                // (water: only emitted mids link — promenade segments stay continuous)
                for (int e = 0; e < 4; e++)
                {
                    string mid = $"{b.block_id}_m{e}";
                    string nextCorner = $"{b.block_id}_c{(e + 1) % 4}";

                    if (!nodes.ContainsKey(mid) || !nodes.ContainsKey(nextCorner)) continue;

                    float cost = Mathf.Max(2f, Vector3.Distance(nodes[mid].localPos, nodes[nextCorner].localPos) * 3f);
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

                    string t1 = TerrAt(b1.col, b1.row), t2 = TerrAt(b2.col, b2.row);
                    if (t1 == "oob" || t2 == "oob") continue;

                    int e1, e2;
                    if (b2.col > b1.col) { e1 = 1; e2 = 3; }
                    else if (b2.col < b1.col) { e1 = 3; e2 = 1; }
                    else if (b2.row > b1.row) { e1 = 2; e2 = 0; }
                    else { e1 = 0; e2 = 2; }

                    string m1 = $"{b1.block_id}_m{e1}";
                    string m2 = $"{b2.block_id}_m{e2}";

                    // Links onto/through a bridge deck are the river crossings — tag
                    // them so AI can weigh chokepoints.
                    WaypointType crossType = (t1 == "bridge" || t2 == "bridge")
                        ? WaypointType.BridgeDeck : WaypointType.CrosswalkCorner;

                    // Mid-edge crosswalk: straight across at the middle of the block
                    // edge. Skipped when either mid was terrain-dropped (channel
                    // mouth) — water-water seams continue via corner links instead
                    // (promenade continuation), water-land seams keep the mid link
                    // (riverside street crosswalk).
                    if (nodes.ContainsKey(m1) && nodes.ContainsKey(m2))
                    {
                        float midCrossCost = Mathf.Max(2f, Vector3.Distance(nodes[m1].localPos, nodes[m2].localPos) * 3f);
                        nodes[m1].links.Add(new WaypointLink(m2, midCrossCost, 0f, crossType));
                        nodes[m2].links.Add(new WaypointLink(m1, midCrossCost, 0f, crossType));
                    }

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

                    // Corner crosswalks: distance-proportional cost (same formula as sidewalk links)
                    if (nodes.ContainsKey(c1a) && nodes.ContainsKey(c2b))
                    {
                        float cornerCrossCost1 = Mathf.Max(2f, Vector3.Distance(nodes[c1a].localPos, nodes[c2b].localPos) * 3f);
                        nodes[c1a].links.Add(new WaypointLink(c2b, cornerCrossCost1, 0f, crossType));
                        nodes[c2b].links.Add(new WaypointLink(c1a, cornerCrossCost1, 0f, crossType));
                    }
                    if (nodes.ContainsKey(c1b) && nodes.ContainsKey(c2a))
                    {
                        float cornerCrossCost2 = Mathf.Max(2f, Vector3.Distance(nodes[c1b].localPos, nodes[c2a].localPos) * 3f);
                        nodes[c1b].links.Add(new WaypointLink(c2a, cornerCrossCost2, 0f, crossType));
                        nodes[c2a].links.Add(new WaypointLink(c1b, cornerCrossCost2, 0f, crossType));
                    }
                }
            }

            Debug.Log($"[WaypointGraph] Generated {nodes.Count} nodes, " +
                $"{CountLinks()} links for {layout.blocks.Length} blocks");
        }

        /// <summary>
        /// Measure the sidewalk band on one block edge directly from generated
        /// terrain voxels: scan a line perpendicular to the edge at grade height,
        /// find the contiguous MAT_SIDEWALK run, return its centroid as distance
        /// inward from the tile edge. Falls back to the nominal half-sidewalk
        /// when no band is found (channel mouths, bridge deck ends, oob).
        /// </summary>
        private float MeasureSidewalkLane(float bx, float bz, int edge, float halfTile,
            float sidewalkWidth, VoxelCollisionWorld cw, Vector3 rootPos, float fallback)
        {
            const byte SIDEWALK = 102; // MAT_SIDEWALK
            const byte AIR = 0;
            float vs = cw.VoxelSize;
            float wy = rootPos.y + 0.075f;          // mid grade slab
            float wyAbove = wy + 0.45f;             // headroom probe (parapet has stone here)
            float step = vs * 0.5f;
            float sFrom = -0.10f;                    // start slightly inside the corridor
            float sTo = sidewalkWidth * 1.5f + 0.3f; // cover ring + margin

            float runA = float.NaN, runB = float.NaN;
            for (float s = sFrom; s <= sTo; s += step)
            {
                float px, pz;
                switch (edge)
                {
                    case 0: px = bx; pz = bz + halfTile - s; break;      // N edge, scan −z
                    case 1: px = bx + halfTile - s; pz = bz; break;      // E edge, scan −x
                    case 2: px = bx; pz = bz - halfTile + s; break;      // S edge, scan +z
                    default: px = bx - halfTile + s; pz = bz; break;     // W edge, scan +x
                }

                // Walkable lane cell = sidewalk slab with clear headroom above.
                // The quay parapet slab is sidewalk at grade but stone above —
                // the two-height check excludes it so the promenade lane centers
                // on the strip between tile edge and railing.
                byte mat = cw.GetVoxelAtGrid(cw.WorldToVoxelGrid(
                    new Vector3(px + rootPos.x, wy, pz + rootPos.z)));
                byte above = cw.GetVoxelAtGrid(cw.WorldToVoxelGrid(
                    new Vector3(px + rootPos.x, wyAbove, pz + rootPos.z)));
                bool walkable = mat == SIDEWALK && above == AIR;

                if (walkable)
                {
                    if (float.IsNaN(runA)) runA = s;
                }
                else if (!float.IsNaN(runA))
                {
                    runB = s;   // first non-walkable after the run → band end
                    break;
                }
            }
            if (float.IsNaN(runA)) return fallback;
            if (float.IsNaN(runB)) runB = sTo;      // band ran past scan window
            return (runA + runB) * 0.5f;            // centroid as distance from edge
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
