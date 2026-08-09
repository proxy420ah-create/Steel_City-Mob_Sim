using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SteelCity.Sim
{
    /// <summary>
    /// Stress test: spawns N VoxelCharacter instances, each uses real A* pathfinding
    /// through the WaypointGraph to reach a random target block, then paths home and self-destructs.
    /// Activate via F8 key or call RunTest() from code.
    /// </summary>
    public class StressTestSpawner : MonoBehaviour
    {
        [Header("Test Parameters")]
        [SerializeField] private int characterCount = 100;
        [SerializeField] private string characterAsset = "character_hoodlum_0.stasset";
        [SerializeField] private float spawnDelay = 0.05f; // stagger spawns to avoid spike
        [SerializeField] private float atTargetDuration = 2f; // seconds to "extort" before returning
        [SerializeField] private int maxPathsPerFrame = 8; // time-sliced path computation

        [Header("References")]
        [SerializeField] private CityMap3D cityMap;
        [SerializeField] private VoxelChunkManager chunkManager;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = false;
        [Tooltip("Press this key to spawn Vinnys. Default: F8")]
        [SerializeField] private Key spawnKey = Key.F8;
        [Tooltip("Press this key to cycle path beam count. Default: F7")]
        [SerializeField] private Key beamCycleKey = Key.F7;

        private static readonly int[] BeamLevels = { 0, 5, 10, 25, 50, 100, int.MaxValue };
        private int beamLevelIndex = 6; // defaults to max (all agents show beams)

        private List<StressTestAgent> activeAgents = new();
        private Dictionary<string, Block> blocks;
        private Transform mapRoot;
        private float spacing;
        private float centerRow, centerCol;
        private bool testRunning;
        private WaypointGraph waypointGraph;
        private Pathfinder pathfinder;

        void Start()
        {
            if (cityMap == null)
                cityMap = FindFirstObjectByType<CityMap3D>();
            if (chunkManager == null)
                chunkManager = FindFirstObjectByType<VoxelChunkManager>();

            Debug.Log($"[StressTest] Live. Press {spawnKey} to spawn {characterCount} Vinnys.");
        }

        private float ResolveWalkSpeed()
        {
            return 0.6f; // matches VoxelCharacter default walk speed
        }

        private void BuildWaypointGraph()
        {
            if (waypointGraph != null) return;
            var layout = cityMap.CachedLayout;
            if (layout == null)
            {
                Debug.LogError("[StressTest] No city layout — cannot build waypoint graph");
                return;
            }
            waypointGraph = new WaypointGraph();
            waypointGraph.GenerateFromLayout(
                layout,
                cityMap.Spacing,
                cityMap.GroundTile,
                cityMap.SidewalkW,
                cityMap.MapRoot.position);
            pathfinder = new Pathfinder(waypointGraph);
            Debug.Log($"[StressTest] WaypointGraph built: {waypointGraph.Nodes.Count} nodes");
        }

        void Update()
        {
            // Manual trigger via keyboard (Input System)
            if (Keyboard.current != null && Keyboard.current[spawnKey].wasPressedThisFrame && !testRunning)
            {
                Debug.Log($"[StressTest] {spawnKey} pressed — launching stress test!");
                RunTest();
            }

            // F7 cycles beam count levels
            if (Keyboard.current != null && Keyboard.current[beamCycleKey].wasPressedThisFrame && testRunning)
            {
                beamLevelIndex = (beamLevelIndex + 1) % BeamLevels.Length;
                int targetCount = BeamLevels[beamLevelIndex];
                Debug.Log($"[StressTest] {beamCycleKey} pressed — path beams: {(targetCount == int.MaxValue ? "ALL" : targetCount.ToString())}");
                UpdatePathBeams(targetCount);
            }

            if (!testRunning) return;

            // Process queued path requests (time-sliced)
            if (pathfinder != null && pathfinder.PendingRequests > 0)
                pathfinder.ProcessQueue(maxPathsPerFrame);

            // Auto-register path beams for agents that got paths since last check
            if (beamLevelIndex > 0 && Time.frameCount % 30 == 0)
            {
                AutoRegisterNewPathBeams();
            }

            // Periodic status log silenced — use F7 for beam cycling and P key for perf snapshots
            // if (Time.frameCount % 300 == 0)
            // {
            //     int alive = 0, moving = 0, returning = 0, done = 0, awaiting = 0;
            //     foreach (var a in activeAgents)
            //     {
            //         if (a == null) continue;
            //         alive++;
            //         if (a.state == AgentState.AwaitingPath) awaiting++;
            //         else if (a.state == AgentState.PathingToTarget) moving++;
            //         else if (a.state == AgentState.PathingHome) returning++;
            //         else if (a.state == AgentState.Completed) done++;
            //     }
            //
            //     float fps = 1f / Time.smoothDeltaTime;
            //     Debug.Log($"[StressTest] fps={fps:F0} alive={alive} awaiting={awaiting} moving={moving} returning={returning} done={done} pending={pathfinder?.PendingRequests ?? 0} cache={pathfinder?.CacheSize ?? 0} hits={pathfinder?.CacheHits ?? 0} misses={pathfinder?.CacheMisses ?? 0} chunks={chunkManager.PerfTotalChunks} drawn={chunkManager.PerfDrawnChunks}");
            // }

            // Update TickHUD perf stats every 10 frames (~6x/sec)
            if (Time.frameCount % 10 == 0)
            {
                var hud = FindFirstObjectByType<TickHUD>();
                if (hud != null && pathfinder != null)
                {
                    int alive = 0, awaiting = 0, moving = 0, returning = 0;
                    foreach (var a in activeAgents)
                    {
                        if (a == null) continue;
                        alive++;
                        if (a.state == AgentState.AwaitingPath) awaiting++;
                        else if (a.state == AgentState.PathingToTarget) moving++;
                        else if (a.state == AgentState.PathingHome) returning++;
                    }
                    hud.UpdatePerfStats($"Agents: {alive} alive ({awaiting} queued, {moving} outgoing, {returning} returning)\nPathfinder: {pathfinder.PendingRequests} pending | Cache: {pathfinder.CacheSize} paths, {pathfinder.CacheHits} hits / {pathfinder.CacheMisses} misses");
                }
            }

            // Clean up completed agents
            activeAgents.RemoveAll(a => a == null || a.state == AgentState.Completed);
        }

        public void RunTest()
        {
            if (testRunning) return;
            if (cityMap == null || chunkManager == null)
            {
                Debug.LogError("[StressTest] Missing CityMap3D or VoxelChunkManager reference");
                return;
            }

            StartCoroutine(RunTestCoroutine());
        }

        private IEnumerator RunTestCoroutine()
        {
            testRunning = true;
            activeAgents.Clear();

            // Gather city data
            blocks = cityMap.CachedBlocks;
            mapRoot = cityMap.MapRoot != null ? cityMap.MapRoot : cityMap.transform;

            spacing = cityMap.Spacing;
            // Compute center from blocks
            centerRow = 0f; centerCol = 0f;
            if (blocks != null && blocks.Count > 0)
            {
                int minR = int.MaxValue, maxR = int.MinValue, minC = int.MaxValue, maxC = int.MinValue;
                foreach (var b in blocks.Values)
                {
                    if (b.row < minR) minR = b.row;
                    if (b.row > maxR) maxR = b.row;
                    if (b.col < minC) minC = b.col;
                    if (b.col > maxC) maxC = b.col;
                }
                centerRow = (minR + maxR) * 0.5f;
                centerCol = (minC + maxC) * 0.5f;
            }

            if (blocks == null || blocks.Count == 0)
            {
                Debug.LogError("[StressTest] No blocks available — build city first");
                testRunning = false;
                yield break;
            }

            // Build waypoint graph for real pathfinding
            BuildWaypointGraph();
            if (pathfinder == null)
            {
                Debug.LogError("[StressTest] Failed to build waypoint graph");
                testRunning = false;
                yield break;
            }

            // Find player HQ as spawn point
            Block spawnBlock = null;
            foreach (var b in blocks.Values)
            {
                if (b.isPlayerHq) { spawnBlock = b; break; }
            }
            if (spawnBlock == null)
            {
                var e = blocks.Values.GetEnumerator();
                e.MoveNext();
                spawnBlock = e.Current;
            }

            float spawnX = (spawnBlock.col - centerCol) * spacing;
            float spawnZ = -(spawnBlock.row - centerRow) * spacing;
            float groundY = cityMap != null ? cityMap.GetVoxelSize() * 2f : 0.1f;

            Debug.Log($"[StressTest] Starting: {characterCount} agents, spawn at {spawnBlock.id} ({spawnX:F1}, {spawnZ:F1})");

            // Find or create Characters parent
            var charParent = mapRoot.Find("StressTestCharacters");
            if (charParent == null)
            {
                var cp = new GameObject("StressTestCharacters");
                cp.transform.SetParent(mapRoot, false);
                charParent = cp.transform;
            }

            // Collect valid target blocks (non-HQ, non-police)
            var targetBlocks = new List<Block>();
            foreach (var b in blocks.Values)
            {
                if (!b.isPlayerHq && !b.isPoliceStation)
                    targetBlocks.Add(b);
            }

            if (targetBlocks.Count == 0)
            {
                Debug.LogError("[StressTest] No valid target blocks found");
                testRunning = false;
                yield break;
            }

            int spawned = 0;
            for (int i = 0; i < characterCount; i++)
            {
                // Pick random target
                Block target = targetBlocks[Random.Range(0, targetBlocks.Count)];
                float targetX = (target.col - centerCol) * spacing;
                float targetZ = -(target.row - centerRow) * spacing;

                // Create character GameObject
                var charObj = new GameObject($"StressAgent_{i}");
                charObj.transform.SetParent(charParent, false);
                var vc = charObj.AddComponent<VoxelCharacter>();
                vc.assetFileName = characterAsset;
                vc.voxelSize = cityMap.CharacterVoxelSize;
                vc.chunkManager = chunkManager;
                vc.collisionWorld = FindFirstObjectByType<VoxelCollisionWorld>();
                vc.centerPosition = new Vector3(spawnX, groundY, spawnZ);
                vc.useWorldPosition = false;
                vc.showGizmo = false;
                vc.showGroundProbe = false;

                // Create agent controller with real pathfinding
                float walkSpeed = ResolveWalkSpeed();
                var agent = charObj.AddComponent<StressTestAgent>();
                agent.Initialize(
                    spawnPos: new Vector3(spawnX, groundY, spawnZ),
                    targetPos: new Vector3(targetX, groundY, targetZ),
                    spawnBlock: spawnBlock.id,
                    targetBlock: target.id,
                    speed: walkSpeed,
                    atTargetDuration: atTargetDuration,
                    pathfinder: pathfinder,
                    mapRoot: mapRoot,
                    agentIndex: i,
                    verbose: verboseLogging);

                activeAgents.Add(agent);
                spawned++;

                if (verboseLogging && i % 10 == 0)
                    Debug.Log($"[StressTest] Spawned {spawned}/{characterCount}");

                // Stagger spawns
                if (spawnDelay > 0f)
                    yield return new WaitForSeconds(spawnDelay);
            }

            Debug.Log($"[StressTest] All {spawned} agents spawned. Registering path beams for all...");

            // Auto-register path beams for all agents by default
            UpdatePathBeams(BeamLevels[beamLevelIndex]);

            // Wait for all agents to complete
            while (activeAgents.Count > 0)
            {
                yield return new WaitForSeconds(1f);
            }

            Debug.Log("[StressTest] All agents completed and destroyed. Test finished.");
            testRunning = false;
        }

        public void StopTest()
        {
            testRunning = false;
            StopAllCoroutines();

            var pdr = PathDebugRenderer.Instance;
            foreach (var a in activeAgents)
            {
                if (a != null)
                {
                    pdr?.UnregisterPath(a.transform);
                    Destroy(a.gameObject);
                }
            }
            activeAgents.Clear();
            beamLevelIndex = 0;
            Debug.Log("[StressTest] Test stopped.");
        }

        void OnDestroy()
        {
            StopTest();
        }

        private void UpdatePathBeams(int targetCount)
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr == null)
            {
                var pdrObj = new GameObject("PathDebugRenderer");
                pdr = pdrObj.AddComponent<PathDebugRenderer>();
            }
            if (cityMap != null)
                pdr.SetMapRoot(cityMap.MapRoot);

            // Unregister all agents first
            foreach (var a in activeAgents)
            {
                if (a != null)
                    pdr.UnregisterPath(a.transform);
            }

            if (targetCount == 0) return;

            // Register beams for the first N agents that have a path
            int registered = 0;
            foreach (var a in activeAgents)
            {
                if (a == null || a.Path == null || a.Path.Count == 0) continue;
                if (registered >= targetCount) break;

                var vc = a.GetComponent<VoxelCharacter>();
                pdr.RegisterPath(
                    a.transform,
                    vc != null ? vc.WorldSize : Vector3.one,
                    () => a.Path,
                    (nodeId) => a.Graph.Nodes.TryGetValue(nodeId, out var n) ? n.localPos : new Vector3(float.NaN, 0, 0),
                    PathDebugType.Pedestrian,
                    () => a.PathIndex);
                registered++;
            }

            Debug.Log($"[StressTest] Registered {registered}/{(targetCount == int.MaxValue ? "ALL" : targetCount.ToString())} path beams");
        }

        /// <summary>
        /// Registers path beams for agents that have acquired paths since the last call.
        /// Does not unregister existing beams — only adds new ones.
        /// </summary>
        private void AutoRegisterNewPathBeams()
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr == null) return;

            int targetCount = BeamLevels[beamLevelIndex];
            int currentCount = pdr.ActivePathCount;
            if (targetCount != int.MaxValue && currentCount >= targetCount) return;

            int registered = 0;
            foreach (var a in activeAgents)
            {
                if (a == null || a.Path == null || a.Path.Count == 0) continue;

                // RegisterPath calls UnregisterPath internally first, so this is idempotent
                var vc = a.GetComponent<VoxelCharacter>();
                pdr.RegisterPath(
                    a.transform,
                    vc != null ? vc.WorldSize : Vector3.one,
                    () => a.Path,
                    (nodeId) => a.Graph.Nodes.TryGetValue(nodeId, out var n) ? n.localPos : new Vector3(float.NaN, 0, 0),
                    PathDebugType.Pedestrian,
                    () => a.PathIndex);
                registered++;

                if (targetCount != int.MaxValue && currentCount + registered >= targetCount) break;
            }

            if (registered > 0)
                Debug.Log($"[StressTest] Auto-registered {registered} new path beams (total: {pdr.ActivePathCount})");
        }
    }

    public enum AgentState
    {
        AwaitingPath,
        PathingToTarget,
        AtTarget,
        PathingHome,
        Completed
    }

    /// <summary>
    /// Pathfinding agent: uses A* through WaypointGraph to reach target, then paths home.
    /// Moves at constant speed (same as real Vinny) — no tick-based duration.
    /// </summary>
    public class StressTestAgent : MonoBehaviour
    {
        private Vector3 spawnPos;
        private string spawnBlockId;
        private string targetBlockId;
        private float speed;
        private float atTargetDuration;
        private Pathfinder pathfinder;
        private Transform mapRoot;
        private int index;
        private bool verbose;

        public AgentState state = AgentState.AwaitingPath;
        private float stateTimer;
        private List<string> path;
        private int pathIndex;
        private Vector3 currentWaypoint;
        private Vector3 prevWaypoint;
        private float segmentElapsed;
        private float segmentDuration;
        private bool awaitingReturnPath;

        public List<string> Path => path;
        public int PathIndex => pathIndex;
        public WaypointGraph Graph => pathfinder.Graph;

        public void Initialize(
            Vector3 spawnPos, Vector3 targetPos,
            string spawnBlock, string targetBlock,
            float speed, float atTargetDuration,
            Pathfinder pathfinder, Transform mapRoot,
            int agentIndex, bool verbose)
        {
            this.spawnPos = spawnPos;
            this.spawnBlockId = spawnBlock;
            this.targetBlockId = targetBlock;
            this.speed = speed;
            // Jitter atTargetDuration so paired agents don't all request return paths simultaneously
            this.atTargetDuration = atTargetDuration + Random.Range(0f, 1.5f);
            this.pathfinder = pathfinder;
            this.mapRoot = mapRoot;
            this.index = agentIndex;
            this.verbose = verbose;
            state = AgentState.AwaitingPath;
            stateTimer = 0f;
            awaitingReturnPath = false;

            // Queue path request (time-sliced, not computed synchronously)
            pathfinder.EnqueueRequest(
                spawnBlock, spawnPos,
                targetBlock, targetPos,
                OnPathReceived);
        }

        private void OnPathReceived(List<string> receivedPath)
        {
            if (receivedPath == null || receivedPath.Count == 0)
            {
                if (verbose)
                    Debug.Log($"[Agent {index}] No path found, skipping");
                state = AgentState.Completed;
                return;
            }

            path = receivedPath;
            pathIndex = 0;
            prevWaypoint = transform.localPosition;
            StartNextSegment();
        }

        private void StartNextSegment()
        {
            if (path == null || pathIndex >= path.Count)
            {
                // Reached end of path
                if (state == AgentState.PathingToTarget || (awaitingReturnPath && state == AgentState.AwaitingPath))
                {
                    if (awaitingReturnPath)
                    {
                        state = AgentState.PathingHome;
                        awaitingReturnPath = false;
                    }
                    else
                    {
                        state = AgentState.AtTarget;
                        stateTimer = 0f;
                        if (verbose)
                            Debug.Log($"[Agent {index}] Arrived at {targetBlockId}, extorting...");
                    }
                }
                else if (state == AgentState.PathingHome)
                {
                    state = AgentState.Completed;
                    if (verbose)
                        Debug.Log($"[Agent {index}] Returned home, self-destructing");
                }
                return;
            }

            string nodeId = path[pathIndex];
            var node = pathfinder.Graph.Nodes[nodeId];
            currentWaypoint = node.localPos;

            float distance = Vector3.Distance(prevWaypoint, currentWaypoint);
            segmentDuration = distance / speed;
            segmentElapsed = 0f;

            // Transition from AwaitingPath to walking
            if (state == AgentState.AwaitingPath)
            {
                state = awaitingReturnPath ? AgentState.PathingHome : AgentState.PathingToTarget;
                if (awaitingReturnPath && verbose)
                    Debug.Log($"[Agent {index}] Return path received, heading home");
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;

            switch (state)
            {
                case AgentState.AwaitingPath:
                    // Idle — waiting for pathfinder queue to process our request
                    break;

                case AgentState.PathingToTarget:
                case AgentState.PathingHome:
                    WalkPath(dt);
                    break;

                case AgentState.AtTarget:
                    stateTimer += dt;
                    if (stateTimer >= atTargetDuration)
                    {
                        // Queue return path request (time-sliced)
                        state = AgentState.AwaitingPath;
                        awaitingReturnPath = true;
                        pathfinder.EnqueueRequest(
                            targetBlockId, currentWaypoint,
                            spawnBlockId, spawnPos,
                            OnPathReceived);
                        if (verbose)
                            Debug.Log($"[Agent {index}] Extortion done, queuing return path");
                    }
                    break;

                case AgentState.Completed:
                    Destroy(gameObject);
                    break;
            }
        }

        private void WalkPath(float dt)
        {
            if (path == null || pathIndex >= path.Count)
            {
                StartNextSegment();
                return;
            }

            segmentElapsed += dt;
            float t = segmentDuration > 0f ? Mathf.Clamp01(segmentElapsed / segmentDuration) : 1f;

            Vector3 pos = Vector3.Lerp(prevWaypoint, currentWaypoint, t);
            pos.y = transform.localPosition.y;
            transform.localPosition = pos;

            // Face movement direction
            Vector3 dir = currentWaypoint - prevWaypoint;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 5f * dt);
            }

            if (t >= 1f)
            {
                prevWaypoint = currentWaypoint;
                pathIndex++;
                StartNextSegment();
            }
        }
    }
}
