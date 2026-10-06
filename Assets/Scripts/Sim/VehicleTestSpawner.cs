using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SteelCity.Sim
{
    /// <summary>
    /// Test harness: spawns N VoxelVehicle instances parked near the player HQ block,
    /// then starts them driving randomly on key press (F10).
    ///
    /// Two-phase design:
    ///   Phase 1 (auto on Start): Vehicle spawns PARKED at the road intersection nearest
    ///     to the player HQ (Vinny's office). Visible during planning phase, not moving.
    ///   Phase 2 (F10 key press): Vehicle starts driving randomly between intersections.
    ///     Press F10 again to stop and park. F9 is NOT used (conflicts with StressTestDiagnostics).
    ///
    /// This exists to validate the vehicle rendering path (VoxelChunkManager's generalized
    /// per-asset InstancedGroup system) and basic road navigation before any drive-state AI
    /// (RE-informed vehicle flags, hood/gang-owned cars, walk-vs-drive decisions) is built.
    /// </summary>
    public class VehicleTestSpawner : MonoBehaviour
    {
        [Header("Test Parameters")]
        [SerializeField] private int vehicleCount = 1;
        [SerializeField] private string vehicleAsset = "vehicle_civilian_car_0.stasset";
        [SerializeField] private float vehicleVoxelSize = 0.05f;
        [SerializeField] private float driveSpeed = 3.0f;

        [Header("Auto-Spawn")]
        [Tooltip("If true, spawns vehicles automatically on Start (after city layout is ready). Vehicle appears near player HQ.")]
        [SerializeField] private bool autoSpawnOnStart = true;
        [Tooltip("Seconds to wait after Start before auto-spawning (lets city layout finish loading).")]
        [SerializeField] private float autoSpawnDelay = 1.0f;

        [Header("References")]
        [SerializeField] private CityMap3D cityMap;
        [SerializeField] private VoxelChunkManager chunkManager;

        [Header("Debug")]
        [Tooltip("Press this key to toggle driving. Default: F10 (F9 conflicts with StressTestDiagnostics)")]
        [SerializeField] private Key driveKey = Key.F10;

        private readonly List<VehicleAgent> activeVehicles = new();
        private RoadGraph roadGraph;
        private ParkingMap parkingMap;

        [Header("Parking")]
        [Tooltip("Extra curb offset (m) beyond the computed parked position — slides parked cars toward/past the curb. Live-adjustable while parked.")]
        [Range(0f, 1.5f)] public float extraParkOffset = 0.25f;

        void OnValidate()
        {
            parkingMap?.SetExtraOffset(extraParkOffset);
        }
        private bool vehiclesSpawned;
        private bool isDriving;

        void Start()
        {
            if (cityMap == null)
                cityMap = FindFirstObjectByType<CityMap3D>();
            if (chunkManager == null)
                chunkManager = FindFirstObjectByType<VoxelChunkManager>();

            if (autoSpawnOnStart)
            {
                StartCoroutine(AutoSpawnCoroutine());
            }
            else
            {
                Debug.Log($"[VehicleTest] Live. Press {driveKey} to spawn parked vehicle(s), press again to drive.");
            }
        }

        private IEnumerator AutoSpawnCoroutine()
        {
            // Wait for city layout to be ready
            float elapsed = 0f;
            while (cityMap != null && cityMap.CachedLayout == null && elapsed < 10f)
            {
                yield return new WaitForSeconds(0.25f);
                elapsed += 0.25f;
            }

            // Extra delay to ensure chunk manager is fully initialized
            yield return new WaitForSeconds(autoSpawnDelay);

            if (cityMap == null || cityMap.CachedLayout == null)
            {
                Debug.LogError("[VehicleTest] City layout not ready after waiting — auto-spawn failed.");
                yield break;
            }

            Debug.Log("[VehicleTest] Auto-spawning parked vehicle near player HQ...");
            SpawnVehicles();
        }

        void Update()
        {
            if (Keyboard.current == null) return;

            if (Keyboard.current[driveKey].wasPressedThisFrame)
            {
                if (!vehiclesSpawned)
                {
                    Debug.Log($"[VehicleTest] {driveKey} pressed — spawning parked vehicle(s)!");
                    SpawnVehicles();
                }
                else
                {
                    isDriving = !isDriving;
                    Debug.Log($"[VehicleTest] {driveKey} pressed — {(isDriving ? "DEPART (pull out + cruise)" : "PARK (find nearest slot ahead)")}");
                    foreach (var v in activeVehicles)
                    {
                        if (v == null) continue;
                        if (isDriving) v.RequestDepart();
                        else v.RequestPark();

                        var pdr = PathDebugRenderer.Instance;
                        if (pdr == null)
                        {
                            var pdrObj = new GameObject("PathDebugRenderer");
                            pdr = pdrObj.AddComponent<PathDebugRenderer>();
                        }
                        if (cityMap != null)
                            pdr.SetMapRoot(cityMap.MapRoot);

                        if (isDriving)
                        {
                            var vv = v.GetComponent<VoxelVehicle>();

                            Debug.Log($"[VehicleTest] RegisterPath: entity={v.gameObject.name}, lanePoints={v.LaneKeys.Count}, progress={v.LaneProgress}");
                            Debug.Log($"[VehicleTest] PDR Instance={pdr != null}, mapRoot={(cityMap != null ? cityMap.MapRoot : null)}, roadGraphNodes={roadGraph?.Nodes?.Count ?? -1}");

                            // The agent's lane polyline is the shared source of truth —
                            // beams draw exactly what the car drives (turn connectors and
                            // 90° corners included), so no renderer-side offset.
                            pdr.RegisterPath(
                                v.transform,
                                vv != null ? vv.WorldSize : Vector3.one,
                                () => v.LaneKeys,
                                key => v.ResolveLanePoint(key),
                                PathDebugType.Car,
                                () => v.LaneProgress,
                                0f);
                            Debug.Log($"[VehicleTest] RegisterPath done. PDR activePaths={PathDebugRenderer.Instance?.ActivePathCount ?? -1}");
                        }
                        // On park request the path stays registered: the beam drains as
                        // the polyline is consumed, so the pull-in leg remains visible
                        // (drawn == driven). The renderer drops the empty path itself.
                    }
                }
            }
        }

        private void BuildRoadGraph()
        {
            if (roadGraph != null) return;
            var layout = cityMap.CachedLayout;
            if (layout == null)
            {
                Debug.LogError("[VehicleTest] No city layout — cannot build road graph");
                return;
            }
            roadGraph = new RoadGraph();
            CityMap3D.ExtractTerrainAndSeams(layout, out _, out var hSeams, out var vSeams);
            roadGraph.GenerateFromLayout(layout, cityMap.Spacing, hSeams, vSeams);

            // Curbside parking inventory — computed from the same links (measured
            // vehicle_civilian_car_0 dims: 1.5 m long × 1.0 m wide @ 0.05 voxels).
            parkingMap = ParkingMap.Build(roadGraph, cityMap.GetRoadWidth(), 1.5f, 1.0f, extraParkOffset);
            Debug.Log($"[VehicleTest] ParkingMap: {parkingMap.Count} curbside slots generated");
        }

        public void SpawnVehicles()
        {
            if (vehiclesSpawned) return;
            if (cityMap == null || chunkManager == null)
            {
                Debug.LogError("[VehicleTest] Missing CityMap3D or VoxelChunkManager reference");
                return;
            }

            BuildRoadGraph();
            if (roadGraph == null || roadGraph.Nodes.Count == 0)
            {
                Debug.LogError("[VehicleTest] Failed to build road graph");
                return;
            }

            vehiclesSpawned = true;

            var mapRoot = cityMap.MapRoot != null ? cityMap.MapRoot : cityMap.transform;
            var vehicleParent = mapRoot.Find("TestVehicles");
            if (vehicleParent == null)
            {
                var vp = new GameObject("TestVehicles");
                vp.transform.SetParent(mapRoot, false);
                vehicleParent = vp.transform;
            }

            float groundY = cityMap != null ? cityMap.GetVoxelSize() * 2f : 0.1f;

            // Find the road intersection nearest to the player HQ block
            string hqStartNode = FindNearestNodeToHq();

            for (int i = 0; i < vehicleCount; i++)
            {
                string startNode = hqStartNode ?? roadGraph.RandomNodeId();
                if (startNode == null)
                {
                    Debug.LogError("[VehicleTest] Road graph has no nodes");
                    break;
                }
                Vector3 startPos = roadGraph.Nodes[startNode].localPos;
                startPos.y = groundY;

                // Spawn PARKED in the nearest free curbside slot to HQ — not mid-intersection.
                ParkingSpace space = parkingMap?.NearestFree(startPos);
                Vector3 spawnPos = space != null ? space.pos : startPos;
                spawnPos.y = groundY;

                var vehObj = new GameObject($"TestVehicle_{i}");
                vehObj.transform.SetParent(vehicleParent, false);
                if (space != null && space.heading.sqrMagnitude > 0.001f)
                    vehObj.transform.localRotation = Quaternion.LookRotation(space.heading, Vector3.up);

                var vv = vehObj.AddComponent<VoxelVehicle>();
                vv.assetFileName = vehicleAsset;
                vv.voxelSize = vehicleVoxelSize;
                vv.chunkManager = chunkManager;
                vv.centerPosition = spawnPos;

                var agent = vehObj.AddComponent<VehicleAgent>();
                float laneOffset = cityMap != null ? cityMap.GetRoadWidth() * 0.25f : 0f;
                agent.Initialize(roadGraph, startNode, driveSpeed, laneOffset, parkingMap, space);
                // Spawn PARKED — not driving until F10
                agent.IsDriving = false;

                activeVehicles.Add(agent);
            }

            Debug.Log($"[VehicleTest] Spawned {activeVehicles.Count} parked vehicle(s) at intersection {hqStartNode}. Press {driveKey} to start driving.");
        }

        /// <summary>
        /// Finds the road intersection nearest to the player HQ block (Vinny's office).
        /// Falls back to a random node if HQ block can't be found.
        /// </summary>
        private string FindNearestNodeToHq()
        {
            if (cityMap == null || cityMap.CachedBlocks == null) return null;

            Block hqBlock = null;
            foreach (var b in cityMap.CachedBlocks.Values)
            {
                if (b.isPlayerHq) { hqBlock = b; break; }
            }
            if (hqBlock == null) return null;

            // Compute HQ block center in local space (same convention as CityMap3D)
            var layout = cityMap.CachedLayout;
            int minRow = int.MaxValue, maxRow = int.MinValue, minCol = int.MaxValue, maxCol = int.MinValue;
            foreach (var lb in layout.blocks)
            {
                if (lb.row < minRow) minRow = lb.row;
                if (lb.row > maxRow) maxRow = lb.row;
                if (lb.col < minCol) minCol = lb.col;
                if (lb.col > maxCol) maxCol = lb.col;
            }
            float centerRow = (minRow + maxRow) * 0.5f;
            float centerCol = (minCol + maxCol) * 0.5f;
            float spacing = cityMap.Spacing;

            float hqX = (hqBlock.col - centerCol) * spacing;
            float hqZ = -(hqBlock.row - centerRow) * spacing;
            Vector3 hqPos = new Vector3(hqX, 0f, hqZ);

            // Find nearest road intersection
            string nearest = null;
            float nearestDist = float.MaxValue;
            foreach (var kvp in roadGraph.Nodes)
            {
                float dist = Vector3.Distance(kvp.Value.localPos, hqPos);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = kvp.Key;
                }
            }

            if (nearest != null)
                Debug.Log($"[VehicleTest] HQ block {hqBlock.id} at ({hqX:F1},{hqZ:F1}) — nearest intersection: {nearest} at dist {nearestDist:F1}");

            return nearest;
        }

        public void StopTest()
        {
            isDriving = false;
            vehiclesSpawned = false;

            var pdr = PathDebugRenderer.Instance;
            foreach (var v in activeVehicles)
            {
                if (v != null)
                {
                    pdr?.UnregisterPath(v.transform);
                    Destroy(v.gameObject);
                }
            }
            activeVehicles.Clear();
            Debug.Log("[VehicleTest] Test stopped.");
        }

        void OnDestroy()
        {
            StopTest();
        }
    }

    /// <summary>
    /// Drives a VoxelVehicle along a rolling lane-space polyline built from the RoadGraph.
    /// The polyline (not the node route) is the single source of truth: the same points the
    /// car drives are the points the debug beam draws. Route nodes append lazily as the car
    /// advances — each new node finalizes the turn geometry at the previous tail node, so
    /// the car only ever latches onto points that can no longer change.
    /// Turn geometry: right turn = the lane centerlines cross before the node → one hard 90°
    /// corner point; left turn = keep the 45° diagonal across the box (two points, so the
    /// connector is its own segment and stays drawn until the car enters it).
    /// </summary>
    public class VehicleAgent : MonoBehaviour
    {
        private RoadGraph graph;
        private float speed;

        private string currentNodeId;
        private Vector3 fromPos;
        private Vector3 toPos;
        private float segmentElapsed;
        private float segmentDuration;
        private float laneOffset;
        private VoxelVehicle vehicle;

        // Node route exists only to pick neighbors; the lane polyline drives+draws.
        private readonly List<string> plannedRoute = new();
        private readonly List<Vector3> laneRoute = new();
        private readonly List<string> laneKeys = new();                 // parallel to laneRoute (renderer ids)
        private readonly Dictionary<string, Vector3> laneLookup = new();
        private int laneIndex;        // next drive-target index in laneRoute
        private int laneKeyCounter;
        private const int PlanAheadCount = 6;

        // --- Parking state ---
        private ParkingMap parkingMap;
        private ParkingSpace parkedSpace;       // slot currently occupied
        private ParkingSpace parkTarget;        // slot being driven to
        private FlowField parkField;            // road field routed to parkTarget.fromId
        private bool parkRequested;             // F10-off: keep cruising until a slot is spliced
        private bool awaitingParkArrival;       // current drive leg ends IN the slot
        private Vector3 parkFinalPoint;

        /// <summary>When false, the vehicle stays parked at its current position.</summary>
        public bool IsDriving { get; set; }

        /// <summary>True when the car is pulled over in a ParkingSpace (parked pose).</summary>
        public bool IsParked => parkedSpace != null;

        /// <summary>Lane-polyline point keys in order — what the debug path renders.</summary>
        public List<string> LaneKeys => laneKeys;

        /// <summary>Index of the point the car is driving toward (consumed = before it).</summary>
        public int LaneProgress => Mathf.Max(0, laneIndex - 1);

        public Vector3 ResolveLanePoint(string key)
            => laneLookup.TryGetValue(key, out var p) ? p : new Vector3(float.NaN, 0, 0);

        /// <summary>World-lane center of the volume (inverse of the corner-anchored transform).</summary>
        private Vector3 CurrentCenter
        {
            get
            {
                Vector3 half = vehicle != null && vehicle.Dims.x > 0
                    ? new Vector3(vehicle.WorldSize.x, 0f, vehicle.WorldSize.z) * 0.5f
                    : Vector3.zero;
                return transform.localPosition + half;
            }
        }

        private Vector3 CurrentDirXZ
        {
            get
            {
                var d = toPos - fromPos;
                d.y = 0f;
                return d.sqrMagnitude > 0.001f ? d.normalized : Vector3.forward;
            }
        }

        public void Initialize(RoadGraph graph, string startNodeId, float speed,
            float laneOffset = 0f, ParkingMap parking = null, ParkingSpace startSpace = null)
        {
            this.graph = graph;
            this.speed = speed;
            this.laneOffset = laneOffset;
            parkingMap = parking;
            vehicle = GetComponent<VoxelVehicle>();
            currentNodeId = startNodeId;
            plannedRoute.Clear();
            laneRoute.Clear();
            laneKeys.Clear();
            laneLookup.Clear();
            laneIndex = 0;
            laneKeyCounter = 0;
            parkedSpace = null;
            parkTarget = null;
            parkField = null;
            parkRequested = false;
            awaitingParkArrival = false;
            segmentElapsed = 0f;
            segmentDuration = 0f;
            IsDriving = false;

            if (startSpace != null)
            {
                // Parked spawn: sit in the slot (curb lane), route seeded at the slot's
                // link so Depart can drive it out along the correct incoming direction.
                parkedSpace = startSpace;
                parkingMap?.Occupy(startSpace, this);
                plannedRoute.Add(startSpace.fromId);
                plannedRoute.Add(startSpace.toId);
                fromPos = toPos = startSpace.pos;
            }
            else
            {
                plannedRoute.Add(startNodeId);
                fromPos = toPos = transform.localPosition;
            }
        }

        /// <summary>
        /// F10-on: leave the parked slot — diagonal pull-out into the lane, merge point
        /// just ahead of the slot, then resume the rolling route from the link's end node.
        /// Not parked → simply resumes driving.
        /// </summary>
        public void RequestDepart()
        {
            if (parkedSpace == null)
            {
                // Not parked — either already cruising or mid park-seek: cancel the
                // parking intent and resume free cruising.
                parkRequested = false;
                parkTarget = null;
                parkField = null;
                awaitingParkArrival = false;
                IsDriving = true;
                return;
            }
            var space = parkedSpace;
            parkingMap?.Release(space);
            parkedSpace = null;

            Vector3 dir = space.heading;
            float y = transform.localPosition.y;
            Vector3 mergePt = space.lanePos + dir * 1.2f;              // pull-out diagonal target
            Vector3 laneEnd = graph.Nodes[space.toId].localPos + SideOf(dir);
            mergePt.y = y;
            laneEnd.y = y;

            plannedRoute.Clear();
            plannedRoute.Add(space.fromId);
            plannedRoute.Add(space.toId);   // incoming dir at toId stays correct
            laneRoute.Clear();
            laneKeys.Clear();
            laneLookup.Clear();
            laneIndex = 0;
            PushLanePoint(mergePt);          // driven + drawn — same polyline
            PushLanePoint(laneEnd);
            segmentDuration = 0f;            // force AdvanceTarget next frame
            IsDriving = true;
        }

        /// <summary>
        /// F10-off: don't freeze — find the nearest free slot ahead, build a road-graph
        /// flow field to its entry node, and splice a pull-in leg when a lane passes it.
        /// </summary>
        public void RequestPark()
        {
            if (parkedSpace != null || !IsDriving) return;
            parkRequested = true;
            PickParkTarget();
            TrySpliceParking();   // the in-flight leg may already cross the slot
        }

        private void PickParkTarget()
        {
            if (!parkRequested || parkingMap == null) return;
            parkTarget = parkingMap.NearestFreeAhead(CurrentCenter, CurrentDirXZ);
            parkField = parkTarget != null ? FlowField.Build(graph, parkTarget.fromId) : null;
        }

        private void FinishPark()
        {
            parkedSpace = parkTarget;
            parkingMap?.Occupy(parkedSpace, this);
            parkTarget = null;
            parkField = null;
            awaitingParkArrival = false;
            parkRequested = false;
            IsDriving = false;
            // Aligned parked pose — the diagonal's residual slerp yaw gets squared away.
            if (parkedSpace.heading.sqrMagnitude > 0.001f)
                transform.localRotation = Quaternion.LookRotation(parkedSpace.heading, Vector3.up);
            Debug.Log($"[VehicleTest] Parked at {parkedSpace.pos:F1} on link {parkedSpace.fromId}→{parkedSpace.toId}");
        }

        private Vector3 SideOf(Vector3 dir)
            => dir.sqrMagnitude > 0.001f
                ? Vector3.Cross(Vector3.up, dir.normalized) * laneOffset  // up×dir = right of travel
                : Vector3.zero;

        private void PushLanePoint(Vector3 p)
        {
            string key = "lp" + laneKeyCounter++;
            laneRoute.Add(p);
            laneKeys.Add(key);
            laneLookup[key] = p;
        }

        /// <summary>
        /// Appends one random route node, then rewrites the tentative tail lane point into
        /// the real turn geometry at the node whose directions are now both known.
        /// </summary>
        private bool ExtendRoute()
        {
            int n = plannedRoute.Count;
            string tail = plannedRoute[n - 1];
            string prev = n > 1 ? plannedRoute[n - 2] : null;

            string next;
            if (parkTarget != null && tail == parkTarget.fromId)
            {
                // At the slot's entry node — force traversal of the slot link itself.
                next = parkTarget.toId;
            }
            else if (parkField != null)
            {
                // Heading to a parking slot: follow the road flow field toward the
                // entry node (the same next-hop query peds will use on their graph).
                next = parkField.Next(tail) ?? graph.RandomNeighbor(tail, prev);
            }
            else
            {
                next = graph.RandomNeighbor(tail, prev);
            }
            if (next == null && prev != null)
                next = graph.RandomNeighbor(tail, null);   // dead end — allow a U-turn
            if (next == null) return false;
            plannedRoute.Add(next);
            FinalizeTailTurn();
            currentNodeId = tail;
            return true;
        }

        private void FinalizeTailTurn()
        {
            int n = plannedRoute.Count;   // >= 2 (the node just appended)
            Vector3 B = graph.Nodes[plannedRoute[n - 2]].localPos;
            Vector3 C = graph.Nodes[plannedRoute[n - 1]].localPos;
            Vector3 dOut = C - B;
            dOut.y = 0f;
            Vector3 sOut = SideOf(dOut);
            float y = transform.localPosition.y;

            // Drop the previous leg's tentative end point — replaced by real turn geometry.
            if (laneRoute.Count > 0)
            {
                laneLookup.Remove(laneKeys[laneKeys.Count - 1]);
                laneRoute.RemoveAt(laneRoute.Count - 1);
                laneKeys.RemoveAt(laneKeys.Count - 1);
            }

            Vector3 p;
            if (n >= 3)
            {
                Vector3 A = graph.Nodes[plannedRoute[n - 3]].localPos;
                Vector3 dIn = B - A;
                dIn.y = 0f;
                Vector3 sIn = SideOf(dIn);
                // Cross(dIn,dOut).y > 0 = clockwise = RIGHT turn (north→east = +1).
                float crossY = Vector3.Cross(dIn.normalized, dOut.normalized).y;
                const float eps = 0.01f;
                if (crossY > eps)
                {
                    // Right turn: lane centerlines cross at the near corner before the
                    // node — single hard 90° corner point, no connector triangle.
                    p = B + sIn + sOut; p.y = y; PushLanePoint(p);
                }
                else if (crossY < -eps)
                {
                    // Left turn: keep the 45° diagonal through the box — two points, so
                    // the connector is its own polyline segment (own waypoint + beam).
                    p = B + sIn;  p.y = y; PushLanePoint(p);
                    p = B + sOut; p.y = y; PushLanePoint(p);
                }
                else
                {
                    p = B + sIn; p.y = y; PushLanePoint(p);
                }
            }
            else
            {
                // First leg — pull-in point at the start node.
                p = B + sOut; p.y = y; PushLanePoint(p);
            }

            // New tentative tail: lane end of the leg into C — may be rewritten when the
            // next node appends, so the car never latches onto it (see AdvanceTarget).
            p = C + sOut; p.y = y; PushLanePoint(p);
        }

        private void AdvanceTarget()
        {
            // Bound the lists — drop fully-consumed points (keep the current target).
            if (laneIndex > 64)
            {
                for (int i = 0; i < laneIndex - 1; i++) laneLookup.Remove(laneKeys[i]);
                laneRoute.RemoveRange(0, laneIndex - 1);
                laneKeys.RemoveRange(0, laneIndex - 1);
                laneIndex = 1;
                int keepNodes = PlanAheadCount + 8;
                if (plannedRoute.Count > keepNodes)
                    plannedRoute.RemoveRange(0, plannedRoute.Count - keepNodes);
            }

            // Seeking a slot but lost it (overshot the link) — repick ahead of where we are.
            if (parkRequested && parkTarget == null)
                PickParkTarget();

            // Roll the polyline forward — the last lane point is tentative (the next node
            // can still rewrite it into turn geometry), so it is never a drive target.
            // While a pull-in is spliced the slot IS the terminal point — do not extend.
            while (laneIndex >= laneRoute.Count - 1 && !awaitingParkArrival)
            {
                if (!ExtendRoute())
                {
                    Debug.Log("[VehicleTest] Route dead-ended — parking.");
                    IsDriving = false;
                    return;
                }
            }

            StartLeg(laneRoute[laneIndex++]);
            TrySpliceParking();
        }

        /// <summary>
        /// If the just-started leg passes the target slot's lane position, retarget its
        /// end to a lead-in point and insert the slot as the next polyline point — the
        /// diagonal lane→curb jog becomes a real, drawn, driven segment.
        /// </summary>
        private void TrySpliceParking()
        {
            if (parkTarget == null || awaitingParkArrival) return;

            Vector3 seg = toPos - fromPos;
            float len = seg.magnitude;
            if (len < 0.5f) return;
            Vector3 dir = seg / len;

            float t = Vector3.Dot(parkTarget.lanePos - fromPos, dir);   // slot station along leg
            float carT = Vector3.Dot(CurrentCenter - fromPos, dir);     // car's progress along leg
            const float lead = 1.2f;

            // Lane match: the slot's lane pos must sit ON this leg's line.
            Vector3 closest = fromPos + dir * Mathf.Clamp(t, 0f, len);
            if ((parkTarget.lanePos - closest).sqrMagnitude > 0.04f) return;   // >20cm off-lane

            if (t < carT - 0.1f)
            {
                // Slot already behind the car on this heading — overshot, repick.
                parkTarget = null;
                parkField = null;
                return;
            }
            if (t - lead <= carT + 0.2f || t > len + 0.5f)
                return;   // too late to pull in smoothly, or beyond this leg — keep seeking

            Vector3 approach = parkTarget.lanePos - dir * lead;
            approach.y = transform.localPosition.y;
            toPos = approach;
            segmentDuration = Vector3.Distance(fromPos, toPos) / Mathf.Max(speed, 0.01f);

            InsertLanePoint(laneIndex, parkTarget.pos);   // next target = the slot itself

            // The slot is terminal: drop every point after it so the beam ends at the
            // curb instead of drawing the would-be route onward. Re-registers on depart.
            for (int i = laneRoute.Count - 1; i > laneIndex; i--)
            {
                laneLookup.Remove(laneKeys[i]);
                laneRoute.RemoveAt(i);
                laneKeys.RemoveAt(i);
            }

            parkFinalPoint = parkTarget.pos;
            awaitingParkArrival = true;
        }

        private void InsertLanePoint(int index, Vector3 p)
        {
            string key = "lp" + laneKeyCounter++;
            laneRoute.Insert(index, p);
            laneKeys.Insert(index, key);
            laneLookup[key] = p;
        }

        private void StartLeg(Vector3 target)
        {
            fromPos = toPos;
            fromPos.y = transform.localPosition.y;
            toPos = target;
            segmentDuration = Vector3.Distance(fromPos, toPos) / Mathf.Max(speed, 0.01f);
            segmentElapsed = 0f;
        }

        void Update()
        {
            if (graph == null) return;

            // Live re-snap a parked car when slot geometry is tuned (extraParkOffset slider).
            if (!IsDriving)
            {
                if (parkedSpace != null)
                {
                    Vector3 parkedCorner = vehicle != null && vehicle.Dims.x > 0
                        ? new Vector3(vehicle.WorldSize.x, 0f, vehicle.WorldSize.z) * 0.5f
                        : Vector3.zero;
                    Vector3 parkedCenter = parkedSpace.pos;
                    parkedCenter.y = transform.localPosition.y;
                    transform.localPosition = parkedCenter - parkedCorner;
                }
                return;
            }

            if (segmentDuration <= 0f)
            {
                AdvanceTarget();
                return;
            }

            segmentElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(segmentElapsed / segmentDuration);

            Vector3 dir = toPos - fromPos;
            dir.y = 0f;
            // Drive points are already in lane space (the polyline IS the path — same
            // points the beam draws) — the lerp is the physical path, no runtime offset.
            Vector3 center = Vector3.Lerp(fromPos, toPos, t);

            // VoxelVehicle is corner-anchored (transform = volume corner) and the raymarch
            // shader rotates about volumeCenter = worldOffset + halfDims — so the plain
            // half-footprint keeps the volume's center on the lane under any yaw.
            Vector3 corner = vehicle != null && vehicle.Dims.x > 0
                ? new Vector3(vehicle.WorldSize.x, 0f, vehicle.WorldSize.z) * 0.5f
                : Vector3.zero;
            transform.localPosition = center - corner;

            if (dir.sqrMagnitude > 0.001f)
            {
                // NOTE: adjust the extra rotation offset below to match the car model's authored
                // forward-facing axis, same as VoxelCharacter/StressTestAgent do for citizens.
                Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 6f * Time.deltaTime);
            }

            if (t >= 1f)
            {
                // Reached the pull-in leg's end — the car is in the slot.
                if (awaitingParkArrival && (toPos - parkFinalPoint).sqrMagnitude < 0.0001f)
                    FinishPark();
                else
                    AdvanceTarget();
            }
        }
    }
}
