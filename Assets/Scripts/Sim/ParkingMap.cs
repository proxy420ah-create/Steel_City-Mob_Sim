using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// One curbside parking slot on a directed road link — computed geometry, not painted.
    /// Slots live on the RIGHT side of the link's travel direction: pos is the parked
    /// center (parkOffset, hard against the curb), lanePos is the same point on the
    /// driving lane (laneOffset) — the pull-in/pull-out jogs connect the two.
    /// </summary>
    public class ParkingSpace
    {
        public Vector3 pos;          // parked center, local space
        public Vector3 lanePos;      // same station along the link, at lane offset
        public Vector3 heading;      // link direction (normalized) — parked yaw
        public Vector3 station;      // point on link centerline (offset basis for pos/lanePos)
        public Vector3 side;         // unit right-of-travel vector
        public string fromId;        // owning directed link fromId → toId
        public string toId;
        public bool occupied;
        public VehicleAgent occupant;
    }

    /// <summary>
    /// Derived parking inventory for the whole city — built once from RoadGraph links,
    /// rebuilt whenever the graph regenerates. Each directed link lays slots along its
    /// right curb: keep-clear margins at both ends, one car-length + gap per slot.
    /// Bridge decks / mainstreet spines are excluded via the graph's own conditioning
    /// (their links are through-cell spines, not ordinary corridors, when they exist).
    /// </summary>
    public class ParkingMap
    {
        public readonly List<ParkingSpace> spaces = new();

        public int Count => spaces.Count;

        // Slot geometry (matches ROAD_LANES_AND_TRAFFIC.md §Parking)
        private const float KeepClear = 2.0f;    // no parking within this of an intersection
        private const float SlotGap = 0.5f;      // bumper gap between slots

        private float parkOffset;                // lane→curb distance at build time
        private float laneOffset;
        private float extraOffset;               // user slider: pushes parked pos past the curb

        /// <summary>
        /// Shift every slot's parked pos further toward/past the curb by <paramref name="extra"/>.
        /// Mutates in place — occupants and reservations stay valid; parked agents re-snap.
        /// </summary>
        public void SetExtraOffset(float extra)
        {
            extraOffset = extra;
            float park = parkOffset + extraOffset;
            foreach (var s in spaces)
                s.pos = s.station + s.side * park;
        }

        public static ParkingMap Build(RoadGraph graph, float roadWidth,
            float carLength = 1.5f, float carWidth = 1.0f, float extraOffset = 0f)
        {
            var map = new ParkingMap();
            if (graph == null) return map;

            float pitch = carLength + SlotGap;
            map.parkOffset = roadWidth * 0.5f - carWidth * 0.5f;   // ≈1.0 m — at the curb
            map.laneOffset = roadWidth * 0.25f;                  // ≈0.75 m — driving lane
            map.extraOffset = extraOffset;
            float park = map.parkOffset + extraOffset;

            foreach (var (fromId, node) in graph.Nodes)
            {
                foreach (var link in node.links)
                {
                    if (!graph.Nodes.TryGetValue(link.targetId, out var to)) continue;
                    Vector3 seg = to.localPos - node.localPos;
                    float len = seg.magnitude;
                    float usable = len - KeepClear * 2f;
                    int count = Mathf.FloorToInt(usable / pitch);
                    if (count <= 0) continue;

                    Vector3 dir = seg / len;
                    Vector3 side = Vector3.Cross(Vector3.up, dir);  // right of travel

                    for (int i = 0; i < count; i++)
                    {
                        float t = KeepClear + pitch * (i + 0.5f);
                        Vector3 station = node.localPos + dir * t;
                        map.spaces.Add(new ParkingSpace
                        {
                            pos = station + side * park,
                            lanePos = station + side * map.laneOffset,
                            heading = dir,
                            station = station,
                            side = side,
                            fromId = fromId,
                            toId = link.targetId,
                        });
                    }
                }
            }
            return map;
        }

        public void Occupy(ParkingSpace s, VehicleAgent agent)
        {
            if (s == null) return;
            s.occupied = true;
            s.occupant = agent;
        }

        public void Release(ParkingSpace s)
        {
            if (s == null) return;
            s.occupied = false;
            s.occupant = null;
        }

        /// <summary>Nearest free space to a position, any direction (spawn placement).</summary>
        public ParkingSpace NearestFree(Vector3 pos)
        {
            ParkingSpace best = null;
            float bestD = float.MaxValue;
            foreach (var s in spaces)
            {
                if (s.occupied) continue;
                float d = (s.pos - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        /// <summary>
        /// Nearest free space that is AHEAD of the car on a link matching its travel
        /// direction — right-side parking only, so opposite-direction slots are rejected
        /// by the heading test rather than inviting U-turns to the far curb.
        /// </summary>
        public ParkingSpace NearestFreeAhead(Vector3 pos, Vector3 heading)
        {
            ParkingSpace best = null;
            float bestD = float.MaxValue;
            foreach (var s in spaces)
            {
                if (s.occupied) continue;
                if (Vector3.Dot(s.heading, heading) < 0.3f) continue;      // same side of street
                float ahead = Vector3.Dot(s.pos - pos, heading);
                if (ahead < 1f) continue;                                // must be ahead
                float d = (s.pos - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }
    }
}
