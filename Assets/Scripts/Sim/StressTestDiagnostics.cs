using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SteelCity.Sim
{
    /// <summary>
    /// Frame-by-frame diagnostic logger activated during stress test.
    /// Logs every frame from F8 press until all agents have received their paths.
    /// Captures FPS, pathfinding throughput, GC, render stats, and timing.
    /// </summary>
    public class StressTestDiagnostics : MonoBehaviour
    {
        [Header("Auto-detect")]
        [SerializeField] private StressTestSpawner spawner;
        [SerializeField] private VoxelChunkManager chunkManager;

        [Header("Settings")]
        [SerializeField] private Key activationKey = Key.F8;
        [SerializeField] private Key stopKey = Key.F9;
        [SerializeField] private bool logToConsole = true;
        [SerializeField] private bool logToFile = true;
        [SerializeField] private string logFileName = "stresstest_diag.csv";

        private bool active;
        private int startFrame;
        private float startTime;
        private readonly StringBuilder csvBuilder = new();

        // Per-frame tracking
        private int prevCacheHits;
        private int prevCacheMisses;

        // Phase tracking
        private enum DiagPhase { Idle, Spawning, AllPathsResolved, Done }
        private DiagPhase diagPhase = DiagPhase.Idle;
        private int agentsSpawned;
        private int agentsWithPaths;
        private int agentsAwaiting;

        // GC tracking
        private long prevGCMemory;

        // Frame time history for spike detection
        private readonly float[] frameTimeHistory = new float[120];
        private int frameTimeIndex;
        private int frameTimeCount;

        // Cached references
        private Pathfinder cachedPathfinder;
        private TickHUD cachedHUD;
        private int expectedAgentCount = 100;

        void Start()
        {
            if (spawner == null)
                spawner = FindFirstObjectByType<StressTestSpawner>();
            if (chunkManager == null)
                chunkManager = FindFirstObjectByType<VoxelChunkManager>();
        }

        void Update()
        {
            // Activate on same key as stress test
            if (Keyboard.current != null && Keyboard.current[activationKey].wasPressedThisFrame && !active)
            {
                Activate();
            }

            // Manual stop key
            if (Keyboard.current != null && Keyboard.current[stopKey].wasPressedThisFrame && active)
            {
                Deactivate();
                return;
            }

            if (!active) return;

            LogFrame();

            // Check if all agents have resolved their paths
            if (diagPhase == DiagPhase.Spawning || diagPhase == DiagPhase.AllPathsResolved)
            {
                var agents = GetActiveAgents();
                if (agents != null && agents.Count > 0)
                {
                    int awaiting = 0;
                    int withPaths = 0;
                    foreach (var a in agents)
                    {
                        if (a == null) continue;
                        if (a.state == AgentState.AwaitingPath) awaiting++;
                        else if (a.state == AgentState.PathingToTarget || a.state == AgentState.AtTarget || a.state == AgentState.PathingHome) withPaths++;
                    }
                    agentsAwaiting = awaiting;
                    agentsWithPaths = withPaths;
                    agentsSpawned = agents.Count;

                    if (awaiting == 0 && agentsSpawned >= expectedAgentCount && diagPhase == DiagPhase.Spawning)
                    {
                        diagPhase = DiagPhase.AllPathsResolved;
                        float elapsed = Time.realtimeSinceStartup - startTime;
                        if (logToConsole)
                            Debug.Log($"[Diag] *** ALL PATHS RESOLVED at frame {Time.frameCount - startFrame}, {elapsed:F2}s elapsed, {agentsWithPaths} agents walking ***");
                    }
                }

                // Auto-deactivate only as safety net (5 minutes)
                if (diagPhase == DiagPhase.AllPathsResolved)
                {
                    if (Time.realtimeSinceStartup - startTime > 300f)
                    {
                        Deactivate();
                    }
                }
            }
        }

        private void Activate()
        {
            active = true;
            startFrame = Time.frameCount;
            startTime = Time.realtimeSinceStartup;
            diagPhase = DiagPhase.Spawning;
            csvBuilder.Clear();
            frameTimeIndex = 0;
            frameTimeCount = 0;

            prevGCMemory = System.GC.GetTotalMemory(false);
            prevCacheHits = 0;
            prevCacheMisses = 0;
            cachedPathfinder = null; // will be re-fetched each frame until spawner creates it

            // Try to read expected agent count from spawner
            var countField = typeof(StressTestSpawner).GetField("characterCount",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (countField != null)
                expectedAgentCount = (int)countField.GetValue(spawner);

            if (logToConsole)
                Debug.Log("[Diag] === STRESS TEST DIAGNOSTICS ACTIVATED ===");

            if (logToFile)
            {
                csvBuilder.AppendLine("frame,elapsed_ms,fps,frameTime_ms,deltaTime_ms,smoothDelta_ms,agents_spawned,agents_awaiting,agents_walking,pending_paths,cache_size,cache_hits,cache_misses,paths_this_frame,gc_memory_kb,gc_delta_kb,gc_collisions,chunks_total,chunks_drawn,cpu_total_ms,cpu_cull_ms,cpu_draw_ms,phase");
            }
        }

        private void Deactivate()
        {
            active = false;
            diagPhase = DiagPhase.Done;

            if (logToConsole)
                Debug.Log("[Diag] === DIAGNOSTICS COMPLETE ===");

            if (logToFile && csvBuilder.Length > 0)
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, logFileName);
                System.IO.File.WriteAllText(path, csvBuilder.ToString());
                Debug.Log($"[Diag] CSV written to: {path}");
            }

            LogSummary();
        }

        private void LogFrame()
        {
            int frame = Time.frameCount - startFrame;
            float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;
            float frameTimeMs = Time.unscaledDeltaTime * 1000f;
            float deltaTimeMs = Time.deltaTime * 1000f;
            float smoothDeltaMs = Time.smoothDeltaTime * 1000f;
            float fps = 1f / Mathf.Max(0.0001f, Time.smoothDeltaTime);

            // Pathfinder stats — re-fetch each frame until spawner creates it
            if (cachedPathfinder == null)
                cachedPathfinder = GetPathfinder();

            int pending = cachedPathfinder?.PendingRequests ?? 0;
            int cacheSize = cachedPathfinder?.CacheSize ?? 0;
            int cacheHits = cachedPathfinder?.CacheHits ?? 0;
            int cacheMisses = cachedPathfinder?.CacheMisses ?? 0;
            int pathsThisFrame = 0;

            if (cachedPathfinder != null)
            {
                pathsThisFrame = (cachedPathfinder.CacheHits - prevCacheHits) + (cachedPathfinder.CacheMisses - prevCacheMisses);
                prevCacheHits = cachedPathfinder.CacheHits;
                prevCacheMisses = cachedPathfinder.CacheMisses;
            }

            // GC memory
            long gcMemory = System.GC.GetTotalMemory(false);
            long gcDeltaKb = (gcMemory - prevGCMemory) / 1024;
            prevGCMemory = gcMemory;

            // GC collection count as pressure proxy (UnityStats is pro-only)
            int gcAllocCount = System.GC.CollectionCount(0);

            // Chunk stats (public properties — no reflection needed)
            int chunksTotal = chunkManager?.PerfTotalChunks ?? 0;
            int chunksDrawn = chunkManager?.PerfDrawnChunks ?? 0;
            float cpuTotalMs = chunkManager?.CpuTotalMs ?? 0f;
            float cpuCullMs = chunkManager?.CpuCullMs ?? 0f;
            float cpuDrawMs = chunkManager?.CpuDrawMs ?? 0f;

            // Track frame time history for spike detection
            frameTimeHistory[frameTimeIndex] = frameTimeMs;
            frameTimeIndex = (frameTimeIndex + 1) % frameTimeHistory.Length;
            if (frameTimeCount < frameTimeHistory.Length) frameTimeCount++;

            // Detect spikes (>2x average of last 30 frames)
            bool isSpike = false;
            if (frameTimeCount > 30)
            {
                float avg = 0f;
                for (int i = 0; i < 30; i++)
                {
                    int idx = (frameTimeIndex - 1 - i + frameTimeHistory.Length) % frameTimeHistory.Length;
                    avg += frameTimeHistory[idx];
                }
                avg /= 30f;
                isSpike = frameTimeMs > avg * 2f;
            }

            string phaseStr = diagPhase.ToString();

            // CSV row
            if (logToFile)
            {
                csvBuilder.AppendLine($"{frame},{elapsedMs:F1},{fps:F0},{frameTimeMs:F2},{deltaTimeMs:F2},{smoothDeltaMs:F2},{agentsSpawned},{agentsAwaiting},{agentsWithPaths},{pending},{cacheSize},{cacheHits},{cacheMisses},{pathsThisFrame},{gcMemory/1024},{gcDeltaKb},{gcAllocCount},{chunksTotal},{chunksDrawn},{cpuTotalMs:F2},{cpuCullMs:F2},{cpuDrawMs:F2},{phaseStr}");
            }

            // Console log: spikes, first 5 frames, every 30 frames, or transition frames
            bool shouldLog = isSpike || frame % 30 == 0 || frame < 5 ||
                             (diagPhase == DiagPhase.AllPathsResolved && frame < 10);

            if (logToConsole && shouldLog)
            {
                string spikeMarker = isSpike ? " *** SPIKE ***" : "";
                Debug.Log($"[Diag] f={frame} t={elapsedMs:F0}ms fps={fps:F0} ft={frameTimeMs:F1}ms | agents={agentsSpawned} await={agentsAwaiting} walk={agentsWithPaths} | pending={pending} cache={cacheSize} hits={cacheHits} misses={cacheMisses} paths/f={pathsThisFrame} | gc={gcDeltaKb}KB gcColl={gcAllocCount} chunks={chunksDrawn}/{chunksTotal} cpu={cpuTotalMs:F1}ms{spikeMarker}");
            }

            // Push to TickHUD if available
            if (cachedHUD == null)
                cachedHUD = FindFirstObjectByType<TickHUD>();
            if (cachedHUD != null)
            {
                cachedHUD.UpdatePerfStats(
                    $"[DIAG] f={frame} fps={fps:F0} ft={frameTimeMs:F1}ms | await={agentsAwaiting} walk={agentsWithPaths} | pending={pending} cache={cacheSize} hits={cacheHits} miss={cacheMisses} | gc={gcDeltaKb}KB chunks={chunksDrawn}/{chunksTotal} cpu={cpuTotalMs:F1}ms{(isSpike ? " *** SPIKE ***" : "")}");
            }
        }

        private void LogSummary()
        {
            var pathfinder = cachedPathfinder;
            float totalElapsed = Time.realtimeSinceStartup - startTime;

            var sb = new StringBuilder();
            sb.AppendLine("[Diag] === DIAGNOSTIC SUMMARY ===");
            sb.AppendLine($"  Duration: {totalElapsed:F2}s ({Time.frameCount - startFrame} frames)");
            sb.AppendLine($"  Agents spawned: {agentsSpawned}");
            sb.AppendLine($"  Agents with paths: {agentsWithPaths}");
            sb.AppendLine($"  Agents still awaiting: {agentsAwaiting}");

            if (pathfinder != null)
            {
                sb.AppendLine($"  Pathfinder cache: {pathfinder.CacheSize} unique paths");
                sb.AppendLine($"  Cache hits: {pathfinder.CacheHits}");
                sb.AppendLine($"  Cache misses: {pathfinder.CacheMisses}");
                float hitRate = pathfinder.CacheHits + pathfinder.CacheMisses > 0
                    ? (float)pathfinder.CacheHits / (pathfinder.CacheHits + pathfinder.CacheMisses) * 100f
                    : 0f;
                sb.AppendLine($"  Cache hit rate: {hitRate:F1}%");
                sb.AppendLine($"  Pending requests at end: {pathfinder.PendingRequests}");
            }

            sb.AppendLine($"  Final GC memory: {System.GC.GetTotalMemory(false) / 1024 / 1024}MB");
            sb.AppendLine($"  Final GC gen0 collections: {System.GC.CollectionCount(0)}");

            if (chunkManager != null)
            {
                sb.AppendLine($"  Final chunks: {chunkManager.PerfDrawnChunks} drawn / {chunkManager.PerfTotalChunks} total");
                sb.AppendLine($"  Final CPU: total={chunkManager.CpuTotalMs:F2}ms cull={chunkManager.CpuCullMs:F2}ms draw={chunkManager.CpuDrawMs:F2}ms");
            }

            // Frame time stats
            float minFt = float.MaxValue, maxFt = 0f, avgFt = 0f;
            if (frameTimeCount > 0)
            {
                for (int i = 0; i < frameTimeCount; i++)
                {
                    float ft = frameTimeHistory[i];
                    if (ft < minFt) minFt = ft;
                    if (ft > maxFt) maxFt = ft;
                    avgFt += ft;
                }
                avgFt /= frameTimeCount;
                sb.AppendLine($"  Frame time: min={minFt:F1}ms avg={avgFt:F1}ms max={maxFt:F1}ms");
                sb.AppendLine($"  FPS range: {1000f/maxFt:F0} - {1000f/minFt:F0}");
            }

            Debug.Log(sb.ToString());
        }

        // --- Helpers ---

        private Pathfinder GetPathfinder()
        {
            if (cachedPathfinder != null) return cachedPathfinder;
            if (spawner == null) return null;
            var field = typeof(StressTestSpawner).GetField("pathfinder",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
                cachedPathfinder = field.GetValue(spawner) as Pathfinder;
            return cachedPathfinder;
        }

        private List<StressTestAgent> GetActiveAgents()
        {
            if (spawner == null) return null;
            var field = typeof(StressTestSpawner).GetField("activeAgents",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return field?.GetValue(spawner) as List<StressTestAgent>;
        }

        void OnDestroy()
        {
            if (active && logToFile && csvBuilder.Length > 0)
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, logFileName);
                System.IO.File.WriteAllText(path, csvBuilder.ToString());
                Debug.Log($"[Diag] CSV written on destroy: {path}");
            }
        }
    }
}
