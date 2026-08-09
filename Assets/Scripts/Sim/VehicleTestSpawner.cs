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
                    Debug.Log($"[VehicleTest] {driveKey} pressed — {(isDriving ? "START DRIVING" : "STOPPED (parked)")}");
                    foreach (var v in activeVehicles)
                    {
                        if (v == null) continue;
                        v.IsDriving = isDriving;

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

                            // Diagnostic logging
                            var route = v.PlannedRoute;
                            int rIdx = v.RouteIndex;
                            Debug.Log($"[VehicleTest] RegisterPath: entity={v.gameObject.name}, routeCount={route?.Count ?? -1}, routeIndex={rIdx}, remaining={(route != null ? route.Count - rIdx : -1)}");
                            if (route != null && route.Count > 0)
                            {
                                var sb = new System.Text.StringBuilder();
                                for (int ri = 0; ri < route.Count; ri++)
                                {
                                    string nodeId = route[ri];
                                    bool hasNode = roadGraph.Nodes.TryGetValue(nodeId, out var n);
                                    Vector3 pos = hasNode ? n.localPos : new Vector3(float.NaN, 0, 0);
                                    sb.Append($"\n  [{ri}] {nodeId} => {(hasNode ? pos.ToString("F2") : "NOT_FOUND")}");
                                }
                                Debug.Log($"[VehicleTest] Route details:{sb}");
                            }
                            Debug.Log($"[VehicleTest] PDR Instance={pdr != null}, mapRoot={(cityMap != null ? cityMap.MapRoot : null)}, roadGraphNodes={roadGraph?.Nodes?.Count ?? -1}");

                            pdr.RegisterPath(
                                v.transform,
                                vv != null ? vv.WorldSize : Vector3.one,
                                () => v.PlannedRoute,
                                (nodeId) => roadGraph.Nodes.TryGetValue(nodeId, out var n) ? n.localPos : new Vector3(float.NaN, 0, 0),
                                PathDebugType.Car,
                                () => v.RouteIndex);
                            Debug.Log($"[VehicleTest] RegisterPath done. PDR activePaths={PathDebugRenderer.Instance?.ActivePathCount ?? -1}");
                        }
                        else
                        {
                            pdr.UnregisterPath(v.transform);
                        }
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
            roadGraph.GenerateFromLayout(layout, cityMap.Spacing);
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

                var vehObj = new GameObject($"TestVehicle_{i}");
                vehObj.transform.SetParent(vehicleParent, false);

                var vv = vehObj.AddComponent<VoxelVehicle>();
                vv.assetFileName = vehicleAsset;
                vv.voxelSize = vehicleVoxelSize;
                vv.chunkManager = chunkManager;
                vv.centerPosition = startPos;

                var agent = vehObj.AddComponent<VehicleAgent>();
                agent.Initialize(roadGraph, startNode, driveSpeed);
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
    /// Drives a VoxelVehicle in an endless random walk across the RoadGraph — pick a random
    /// neighboring intersection (avoiding an immediate U-turn where possible), drive there,
    /// repeat. Plans several segments ahead so the debug path beam has a visible route.
    /// </summary>
    public class VehicleAgent : MonoBehaviour
    {
        private RoadGraph graph;
        private float speed;

        private string currentNodeId;
        private string previousNodeId;
        private Vector3 fromPos;
        private Vector3 toPos;
        private float segmentElapsed;
        private float segmentDuration;

        private readonly List<string> plannedRoute = new();
        private int routeIndex;
        private const int PlanAheadCount = 6;

        /// <summary>When false, the vehicle stays parked at its current position.</summary>
        public bool IsDriving { get; set; }

        /// <summary>Current planned route as a list of upcoming road node IDs.</summary>
        public List<string> PlannedRoute => plannedRoute;

        /// <summary>How many nodes in the planned route the vehicle has already passed.</summary>
        public int RouteIndex => routeIndex;

        public void Initialize(RoadGraph graph, string startNodeId, float speed)
        {
            this.graph = graph;
            this.speed = speed;
            currentNodeId = startNodeId;
            previousNodeId = null;
            fromPos = transform.localPosition;
            IsDriving = false;
            PlanRoute();
        }

        private void PlanRoute()
        {
            plannedRoute.Clear();
            routeIndex = 0;

            string cur = currentNodeId;
            string prev = previousNodeId;

            plannedRoute.Add(cur);

            for (int i = 0; i < PlanAheadCount; i++)
            {
                string next = graph.RandomNeighbor(cur, prev);
                if (next == null) break;
                plannedRoute.Add(next);
                prev = cur;
                cur = next;
            }
        }

        private void PickNextTarget()
        {
            // Advance through the planned route
            routeIndex++;

            if (routeIndex >= plannedRoute.Count - 1)
            {
                // Route exhausted — plan a new one from the current node
                currentNodeId = plannedRoute[plannedRoute.Count - 1];
                previousNodeId = plannedRoute.Count > 1 ? plannedRoute[plannedRoute.Count - 2] : null;
                PlanRoute();
                routeIndex = 0;
            }

            string fromId = plannedRoute[routeIndex];
            string toId = plannedRoute[routeIndex + 1];

            currentNodeId = toId;

            fromPos = graph.Nodes[fromId].localPos;
            toPos = graph.Nodes[toId].localPos;
            fromPos.y = transform.localPosition.y;
            toPos.y = transform.localPosition.y;

            float distance = Vector3.Distance(fromPos, toPos);
            segmentDuration = distance / Mathf.Max(speed, 0.01f);
            segmentElapsed = 0f;

            previousNodeId = fromId;
        }

        void Update()
        {
            if (graph == null || !IsDriving) return;

            if (segmentDuration <= 0f)
            {
                PickNextTarget();
                return;
            }

            segmentElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(segmentElapsed / segmentDuration);

            Vector3 pos = Vector3.Lerp(fromPos, toPos, t);
            transform.localPosition = pos;

            Vector3 dir = toPos - fromPos;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
            {
                // NOTE: adjust the extra rotation offset below to match the car model's authored
                // forward-facing axis, same as VoxelCharacter/StressTestAgent do for citizens.
                Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 6f * Time.deltaTime);
            }

            if (t >= 1f)
                PickNextTarget();
        }
    }
}
