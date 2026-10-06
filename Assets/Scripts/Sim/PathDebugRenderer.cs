using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SteelCity.Sim
{
    public enum PathDebugType
    {
        Pedestrian,       // thin orange — outbound leg
        Car,              // thick purple
        Trolley,          // thickest green
        PedestrianReturn  // thin cyan — same path data, distinct color for the walk home
    }

    /// <summary>
    /// Instanced box-beam debug path renderer.
    /// Each path segment is a thin oriented box drawn via CommandBuffer.DrawMeshInstanced
    /// into the voxel render texture, composited on top of raymarched voxels.
    /// No GameObjects or LineRenderers — compatible with the voxel raymarch RawImage overlay.
    /// </summary>
    public class PathDebugRenderer : MonoBehaviour
    {
        [Header("Style Per Type")]
        [SerializeField] private float pedestrianWidth = 0.06f;
        [SerializeField] private Color pedestrianColor = new(1f, 0.5f, 0f, 0.85f);

        [SerializeField] private float carWidth = 0.16f;
        [SerializeField] private Color carColor = new(0.6f, 0.2f, 1f, 0.85f);

        [SerializeField] private float trolleyWidth = 0.30f;
        [SerializeField] private Color trolleyColor = new(0.2f, 1f, 0.3f, 0.85f);

        [SerializeField] private Color pedestrianReturnColor = new(0.2f, 0.9f, 1f, 0.85f);

        [Header("Node Markers")]
        [SerializeField] private float nodeMarkerHeight = 0.15f;
        [SerializeField] private bool showNodeMarkers = true;

        [Header("Waypoint Graph Debug")]
        [Tooltip("When true, renders ALL waypoint graph links and nodes as beams in the Game view.")]
        public bool showWaypointGraph = false;
        [SerializeField] private float graphLinkWidth = 0.04f;
        [SerializeField] private Color graphSidewalkColor = new(0.2f, 0.5f, 1f, 0.4f);
        [SerializeField] private Color graphCrosswalkColor = new(1f, 0.8f, 0.2f, 0.5f);
        [SerializeField] private float graphNodeSize = 0.08f;
        [SerializeField] private Color graphCornerColor = new(0f, 1f, 1f, 0.7f);
        [SerializeField] private Color graphMidColor = new(0.5f, 1f, 0.5f, 0.7f);

        private WaypointGraph debugGraph;

        public void SetDebugGraph(WaypointGraph graph) => debugGraph = graph;

        public bool DebugEnabled => showWaypointGraph;

        [Header("Flow Field Debug")]
        [Tooltip("Draw a directional gradient segment at each node along its next hop — the hot tip points toward the goal; band color = remaining cost (green near → red far).")]
        public bool showFlowField = false;
        [SerializeField] private float flowStubWidth = 0.05f;
        [SerializeField] private float flowStubY = 0.25f;

        private FlowField debugFlowField;
        private System.Func<string, Vector3> flowResolver;
        private readonly List<Matrix4x4>[] flowBuckets = new List<Matrix4x4>[4]
            { new(), new(), new(), new() };
        private static readonly Color[] flowBandColors = new Color[4]
        {
            new Color(0.2f, 1f, 0.2f, 0.9f),   // near goal — green
            new Color(1f, 1f, 0.2f, 0.85f),    // yellow
            new Color(1f, 0.6f, 0.1f, 0.8f),   // orange
            new Color(1f, 0.15f, 0.15f, 0.75f) // far — red
        };

        /// <summary>Show a flow field's direction map. Resolver returns LOCAL pos (mapRoot is added).</summary>
        public void SetDebugFlowField(FlowField field, System.Func<string, Vector3> resolveLocalPos)
        {
            debugFlowField = field;
            flowResolver = resolveLocalPos;
        }

        public void ClearDebugFlowField()
        {
            debugFlowField = null;
            flowResolver = null;
        }

        [Header("Render")]
        [SerializeField] private Camera targetCamera;

        private Transform mapRoot;
        private Mesh boxMesh;
        private Material beamMaterial;
        private Material flowMaterial;   // gradient stubs (Unlit/FlowStub)

        private readonly List<ActivePath> activePaths = new();

        // Free-form world-space debug beams (muzzle rays, IK sight lines, etc).
        // Rendered through the same RT-composite pipeline as path beams.
        private readonly Dictionary<string, (Vector3 a, Vector3 b, float width, Color color)> customBeams = new();
        public void SetCustomBeam(string key, Vector3 a, Vector3 b, float width, Color color)
            => customBeams[key] = (a, b, width, color);
        public void ClearCustomBeam(string key) => customBeams.Remove(key);

        // Per-instance data buffers (reused each frame, no allocation)
        private const int MaxInstances = 2048;
        private readonly Matrix4x4[] segmentMatrices = new Matrix4x4[MaxInstances];
        private readonly Matrix4x4[] markerMatrices = new Matrix4x4[MaxInstances];

        // Per-type batch tracking (up to 3 types: Pedestrian, Car, Trolley)
        // Enum-driven so a new PathDebugType never silently folds into bucket 0.
        private static readonly int MaxTypes = System.Enum.GetValues(typeof(PathDebugType)).Length;
        private int segCount, markerCount;
        private Vector3[] worldPositions = new Vector3[256];
        private Matrix4x4[] batchBuffer = new Matrix4x4[MaxInstances];
        // DrawMeshInstanced silently clips past ~1023 instances per call (D3D11
        // constant-buffer limit) — stage slices into this buffer to stay under.
        private const int DrawChunk = 1023;
        private readonly Matrix4x4[] chunkBuffer = new Matrix4x4[DrawChunk];
        private bool _hasLoggedEntry;

        [Header("Diagnostics")]
        [Tooltip("Verbose per-frame debug logging to the console/Editor.log — off by default to keep the log sane. Menu: Steel City/Debug/Verbose Path Logging.")]
        [SerializeField] private bool verboseLogging = VerboseLoggingDefault;
        /// <summary>Default for newly spawned renderers — set by the debug menu.</summary>
        public static bool VerboseLoggingDefault;
        public bool VerboseLogging { get => verboseLogging; set => verboseLogging = value; }
        private void VLog(string msg) { if (verboseLogging) Debug.Log(msg); }
        private int segDrawCount, markerDrawCount;
        private MaterialPropertyBlock beamProps;

        private struct ActivePath
        {
            public Transform entity;
            public Vector3 entityWorldSize;
            public System.Func<List<string>> routeProvider;
            public System.Func<string, Vector3> resolveNodePos;
            public PathDebugType type;
            public System.Func<int> progressProvider;
            public float lateralOffset;   // right-of-direction lane offset (cars); 0 = centered
        }

        public static PathDebugRenderer Instance { get; private set; }

        /// <summary>Diagnostic: number of currently registered paths.</summary>
        public int ActivePathCount => activePaths.Count;

        void Awake()
        {
            Instance = this;

            // Create a unit cube mesh (1x1x1 centered at origin)
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            boxMesh = go.GetComponent<MeshFilter>().sharedMesh;
            Destroy(go);

            // Create an unlit instanced material for the beams
            var shader = Shader.Find("Unlit/InstancedColor");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            beamMaterial = new Material(shader);
            beamMaterial.enableInstancing = true;

            var flowShader = Shader.Find("Unlit/FlowStub");
            if (flowShader != null)
            {
                flowMaterial = new Material(flowShader);
                flowMaterial.enableInstancing = true;
            }
            else
            {
                Debug.LogWarning("[PathDebug] Unlit/FlowStub shader not found — flow field will render flat");
            }
            beamProps = new MaterialPropertyBlock();

            if (targetCamera == null)
                targetCamera = Camera.main;
        }

        void OnDestroy()
        {
            Instance = null;
            if (beamMaterial != null) Destroy(beamMaterial);
        }

        public void SetMapRoot(Transform root)
        {
            mapRoot = root;
        }

        private (float width, Color color) GetStyle(PathDebugType type)
        {
            return type switch
            {
                PathDebugType.Pedestrian => (pedestrianWidth, pedestrianColor),
                PathDebugType.Car => (carWidth, carColor),
                PathDebugType.Trolley => (trolleyWidth, trolleyColor),
                PathDebugType.PedestrianReturn => (pedestrianWidth, pedestrianReturnColor),
                _ => (pedestrianWidth, pedestrianColor)
            };
        }

        public void RegisterPath(Transform entity, Vector3 entityWorldSize,
            System.Func<List<string>> routeProvider,
            System.Func<string, Vector3> resolveNodePos,
            PathDebugType type,
            System.Func<int> progressProvider = null,
            float lateralOffset = 0f)
        {
            if (entity == null || routeProvider == null || resolveNodePos == null) return;

            UnregisterPath(entity);

            activePaths.Add(new ActivePath
            {
                entity = entity,
                entityWorldSize = entityWorldSize,
                routeProvider = routeProvider,
                resolveNodePos = resolveNodePos,
                type = type,
                progressProvider = progressProvider,
                lateralOffset = lateralOffset
            });
        }

        public void UnregisterPath(Transform entity)
        {
            for (int i = activePaths.Count - 1; i >= 0; i--)
            {
                if (activePaths[i].entity == entity)
                    activePaths.RemoveAt(i);
            }
        }

        /// <summary>DrawMeshInstanced hard-caps ~1023 matrices per call — slice larger batches.</summary>
        private void DrawInstanced(CommandBuffer cmd, Matrix4x4[] src, int count,
            Material mat, MaterialPropertyBlock props)
        {
            for (int s = 0; s < count; s += DrawChunk)
            {
                int n = Mathf.Min(DrawChunk, count - s);
                System.Array.Copy(src, s, chunkBuffer, 0, n);
                cmd.DrawMeshInstanced(boxMesh, 0, mat, 0, chunkBuffer, n, props);
            }
        }

        public void ClearAllPaths()
        {
            activePaths.Clear();
        }

        void Update()
        {
            // Rendering is done in RenderBeamsIntoCamera, called by VoxelRenderBridge
            // after voxel chunks are rendered into the RT.
            // If no VoxelRenderBridge is present, fall back to drawing here.
            if (mapRoot == null || boxMesh == null || beamMaterial == null) return;
            if (activePaths.Count == 0 && !showWaypointGraph && customBeams.Count == 0) return;

            var bridge = FindFirstObjectByType<VoxelRenderBridge>();
            if (bridge == null)
            {
                Camera cam = targetCamera;
                if (cam == null) cam = Camera.main;
                if (cam != null)
                    RenderBeamsInternal(null, cam);
            }
        }

        /// <summary>
        /// Called by VoxelRenderBridge after voxel chunks are rendered into the RT,
        /// but before the RT is assigned to the RawImage. Draws beams into the
        /// same render texture as the voxels using a CommandBuffer.
        /// </summary>
        public void RenderBeamsIntoCamera(Camera externalCam = null)
        {
            if (mapRoot == null || boxMesh == null || beamMaterial == null)
            {
                if (Time.frameCount % 120 == 0)
                    VLog($"[PathDebug] RenderBeamsIntoCamera SKIP: mapRoot={mapRoot != null}, boxMesh={boxMesh != null}, beamMat={beamMaterial != null}");
                return;
            }
            if (activePaths.Count == 0 && !showWaypointGraph && customBeams.Count == 0)
            {
                if (Time.frameCount % 120 == 0)
                    VLog("[PathDebug] RenderBeamsIntoCamera SKIP: no active paths, graph debug off, no custom beams");
                return;
            }

            try
            {
                // Get the active voxel render target from VoxelChunkManager
                var chunkManager = FindFirstObjectByType<VoxelChunkManager>();
                RenderTexture targetRT = chunkManager != null ? chunkManager.GetColorTexture() : null;
            Camera cam = externalCam != null ? externalCam : (targetCamera != null ? targetCamera : Camera.main);
            if (cam == null)
            {
                Debug.LogError("[PathDebug] RenderBeamsIntoCamera: cam is NULL (targetCamera and Camera.main both null)");
                return;
            }

            RenderBeamsInternal(targetRT, cam);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PathDebug] RenderBeamsIntoCamera EXCEPTION: {e}");
            }
        }

        private void RenderBeamsInternal(RenderTexture targetRT, Camera cam)
        {
            // One-time log on first call to confirm entry
            if (!_hasLoggedEntry)
            {
                _hasLoggedEntry = true;
                VLog($"[PathDebug] RenderBeamsInternal FIRST CALL: activePaths={activePaths.Count}, targetRT={(targetRT != null ? targetRT.name : "NULL")} ({(targetRT != null ? $"{targetRT.width}x{targetRT.height}" : "")}), cam={(cam != null ? cam.name : "NULL")}, mapRoot={(mapRoot != null ? mapRoot.position.ToString("F2") : "NULL")}, boxMesh={(boxMesh != null ? "OK" : "NULL")}, beamMat={(beamMaterial != null ? "OK" : "NULL")}");
            }

            // Diagnostic: log state every 60 frames
            if (Time.frameCount % 60 == 0)
            {
                VLog($"[PathDebug] RenderBeamsInternal: activePaths={activePaths.Count}, targetRT={(targetRT != null ? targetRT.name : "NULL")}, cam={(cam != null ? cam.name : "NULL")}, mapRoot={(mapRoot != null ? mapRoot.position.ToString("F2") : "NULL")}, boxMesh={(boxMesh != null ? "OK" : "NULL")}, beamMat={(beamMaterial != null ? "OK" : "NULL")}");
            }
            var segRanges = new (int start, int count)[MaxTypes];
            var markerRanges = new (int start, int count)[MaxTypes];
            for (int t = 0; t < MaxTypes; t++)
            {
                segRanges[t] = (0, 0);
                markerRanges[t] = (0, 0);
            }
            segCount = 0;
            markerCount = 0;

            // Sort active paths by type so segments of the same type are contiguous
            activePaths.Sort((a, b) => a.type.CompareTo(b.type));

            for (int i = activePaths.Count - 1; i >= 0; i--)
            {
                var ap = activePaths[i];
                if (ap.entity == null)
                {
                    activePaths.RemoveAt(i);
                    continue;
                }

                var nodeIds = ap.routeProvider?.Invoke();
                if (nodeIds == null || nodeIds.Count == 0)
                {
                    activePaths.RemoveAt(i);
                    continue;
                }

                float torsoY = ap.entity.position.y + ap.entityWorldSize.y * 0.5f;
                var (width, _) = GetStyle(ap.type);
                int typeIdx = (int)ap.type;
                if (typeIdx >= MaxTypes) typeIdx = 0;

                // Record batch start for this type if first segment of this type
                if (segRanges[typeIdx].count == 0)
                    segRanges[typeIdx] = (segCount, 0);
                if (markerRanges[typeIdx].count == 0)
                    markerRanges[typeIdx] = (markerCount, 0);

                // Diagnostic: log per-path details every 60 frames
                if (Time.frameCount % 60 == 0)
                {
                    var route = ap.routeProvider?.Invoke();
                    int prog = ap.progressProvider?.Invoke() ?? -1;
                    VLog($"[PathDebug] Path[{i}] type={ap.type}, entity={ap.entity?.name ?? "NULL"}, routeCount={route?.Count ?? -1}, progress={prog}, remaining={(route != null ? route.Count - prog : -1)}");
                }

                int progressIndex = ap.progressProvider?.Invoke() ?? 0;
                progressIndex = Mathf.Clamp(progressIndex, 0, nodeIds.Count);

                int remainingCount = nodeIds.Count - progressIndex;
                if (remainingCount <= 0)
                {
                    activePaths.RemoveAt(i);
                    continue;
                }

                // Resolve world positions for remaining nodes
                if (worldPositions.Length < remainingCount)
                    worldPositions = new Vector3[remainingCount];
                bool valid = true;
                for (int j = 0; j < remainingCount; j++)
                {
                    int nodeIdx = progressIndex + j;
                    Vector3 localPos = ap.resolveNodePos(nodeIds[nodeIdx]);
                    if (float.IsNaN(localPos.x))
                    {
                        valid = false;
                        break;
                    }
                    Vector3 pos = localPos + mapRoot.position;
                    pos.y = torsoY;
                    worldPositions[j] = pos;
                }

                if (!valid)
                {
                    if (Time.frameCount % 60 == 0)
                        VLog($"[PathDebug] Path[{i}] INVALID — node position returned NaN, removing");
                    activePaths.RemoveAt(i);
                    continue;
                }

                // Diagnostic: log first few resolved positions
                if (Time.frameCount % 60 == 0 && remainingCount > 0)
                {
                    VLog($"[PathDebug] Path[{i}] resolved {remainingCount} positions. First={worldPositions[0].ToString("F2")}, Last={worldPositions[remainingCount - 1].ToString("F2")}");
                }

                // Build segment boxes between consecutive waypoints
                Vector3 prevLaneEnd = Vector3.zero;
                bool havePrevLaneEnd = false;
                for (int j = 0; j < remainingCount - 1; j++)
                {
                    if (segCount >= MaxInstances) break;

                    Vector3 a = worldPositions[j];
                    Vector3 b = worldPositions[j + 1];
                    Vector3 dir = b - a;
                    float len = dir.magnitude;

                    if (len < 0.001f) continue;

                    // Lane offset: shift the segment right-of-direction so car beams sit
                    // on the lane, not the corridor centerline. Per-segment → the beam
                    // jogs through intersections the way a real lane does.
                    if (ap.lateralOffset != 0f)
                    {
                        Vector3 side = Vector3.Cross(Vector3.up, dir.normalized) * ap.lateralOffset;
                        a += side;
                        b += side;
                    }

                    // Turn connector: when the lane jogs at the shared node (direction
                    // changed), bridge incoming-lane end → outgoing-lane start so the
                    // beam stays continuous through the intersection instead of leaving
                    // a gap/overlap at the corner.
                    if (havePrevLaneEnd && segCount < MaxInstances &&
                        (a - prevLaneEnd).sqrMagnitude > 0.0004f)
                    {
                        Vector3 cdir = a - prevLaneEnd;
                        float clen = cdir.magnitude;
                        segmentMatrices[segCount] = Matrix4x4.TRS(
                            (prevLaneEnd + a) * 0.5f,
                            Quaternion.LookRotation(cdir / clen, Vector3.up),
                            new Vector3(width, width, clen));
                        segCount++;
                        segRanges[typeIdx] = (segRanges[typeIdx].start, segRanges[typeIdx].count + 1);
                    }
                    prevLaneEnd = b;
                    havePrevLaneEnd = true;

                    Vector3 mid = (a + b) * 0.5f;

                    // Orient box: local Z axis maps to segment direction
                    Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    Vector3 scale = new Vector3(width, width, len);

                    segmentMatrices[segCount] = Matrix4x4.TRS(mid, rot, scale);
                    segCount++;
                    segRanges[typeIdx] = (segRanges[typeIdx].start, segRanges[typeIdx].count + 1);
                }

                // Node marker boxes (small vertical boxes at each waypoint)
                if (showNodeMarkers)
                {
                    for (int j = 0; j < remainingCount; j++)
                    {
                        if (markerCount >= MaxInstances) break;

                        Vector3 pos = worldPositions[j];
                        // Lane-offset paths: markers ride the lane too. Interior/start nodes
                        // take the outgoing segment's side vector; the last node takes the
                        // incoming segment's (the lane it arrived on).
                        if (ap.lateralOffset != 0f && remainingCount > 1)
                        {
                            int segIdx = j < remainingCount - 1 ? j : j - 1;
                            Vector3 segDir = worldPositions[segIdx + 1] - worldPositions[segIdx];
                            if (segDir.sqrMagnitude > 0.001f)
                                pos += Vector3.Cross(Vector3.up, segDir.normalized) * ap.lateralOffset;
                        }
                        Vector3 scale = new Vector3(width * 1.5f, nodeMarkerHeight, width * 1.5f);

                        markerMatrices[markerCount] = Matrix4x4.TRS(pos, Quaternion.identity, scale);
                        markerCount++;
                        markerRanges[typeIdx] = (markerRanges[typeIdx].start, markerRanges[typeIdx].count + 1);
                    }
                }
            }

            // Diagnostic: log render batch summary every 60 frames
            if (Time.frameCount % 60 == 0)
            {
                int totalSeg = 0, totalMarker = 0;
                for (int t = 0; t < MaxTypes; t++)
                {
                    totalSeg += segRanges[t].count;
                    totalMarker += markerRanges[t].count;
                }
                VLog($"[PathDebug] Batches: segCount={segCount}, markerCount={markerCount}, totalSeg={totalSeg}, totalMarker={totalMarker}");
                for (int t = 0; t < MaxTypes; t++)
                {
                    if (segRanges[t].count > 0 || markerRanges[t].count > 0)
                        VLog($"[PathDebug] Type[{t}] segs={segRanges[t].count} (start={segRanges[t].start}), markers={markerRanges[t].count} (start={markerRanges[t].start})");
                }
            }

            // --- Waypoint graph debug rendering (all links + nodes) ---
            int graphSegCount = 0;
            int graphNodeCount = 0;
            if (showWaypointGraph && debugGraph != null && mapRoot != null)
            {
                Vector3 rootPos = mapRoot.position;

                // Draw all sidewalk links (blue) and crosswalk links (yellow)
                foreach (var kvp in debugGraph.Nodes)
                {
                    var node = kvp.Value;
                    Vector3 from = node.localPos + rootPos;
                    from.y = 0.15f;

                    foreach (var link in node.links)
                    {
                        if (!debugGraph.Nodes.TryGetValue(link.targetId, out var target)) continue;
                        // Only draw each link once (skip if target ID sorts before ours)
                        if (string.Compare(link.targetId, kvp.Key) < 0) continue;

                        if (graphSegCount >= MaxInstances) break;

                        Vector3 to = target.localPos + rootPos;
                        to.y = 0.15f;
                        Vector3 mid = (from + to) * 0.5f;
                        Vector3 dir = to - from;
                        float len = dir.magnitude;
                        if (len < 0.001f) continue;

                        Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                        Vector3 scale = new Vector3(graphLinkWidth, graphLinkWidth, len);
                        segmentMatrices[segCount + graphSegCount] = Matrix4x4.TRS(mid, rot, scale);
                        graphSegCount++;
                    }
                }

                // Draw node markers
                foreach (var kvp in debugGraph.Nodes)
                {
                    if (graphNodeCount >= MaxInstances) break;
                    var node = kvp.Value;
                    Vector3 pos = node.localPos + rootPos;
                    pos.y = 0.15f;
                    Vector3 scale = new Vector3(graphNodeSize, graphNodeSize * 2f, graphNodeSize);
                    markerMatrices[markerCount + graphNodeCount] = Matrix4x4.TRS(pos, Quaternion.identity, scale);
                    graphNodeCount++;
                }
            }

            // Render segments batched by type (one draw call per type with single color)
            var cmd = new CommandBuffer { name = "PathDebugBeams" };
            if (targetRT != null)
            {
                cmd.SetRenderTarget(targetRT);
                cmd.SetViewport(new Rect(0, 0, targetRT.width, targetRT.height));
            }
            cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
            segDrawCount = 0;
            markerDrawCount = 0;

            // --- Flow field debug: per-node directional stub toward its next hop ---
            // Drawn FIRST: the unlit transparent pass obeys painter's order, so the
            // field is the underlay — agent paths and markers composite on top of it.
            if (showFlowField && debugFlowField != null && flowResolver != null && mapRoot != null)
            {
                foreach (var bucket in flowBuckets) bucket.Clear();
                float maxCost = Mathf.Max(debugFlowField.MaxCost, 0.001f);
                Vector3 rootPos = mapRoot.position;

                foreach (var hop in debugFlowField.Hops)
                {
                    Vector3 a = flowResolver(hop.Key);
                    Vector3 b = flowResolver(hop.Value);
                    if (float.IsNaN(a.x) || float.IsNaN(b.x)) continue;
                    a += rootPos; b += rootPos;
                    a.y = flowStubY; b.y = flowStubY;

                    Vector3 dir = b - a;
                    float len = dir.magnitude;
                    if (len < 0.001f) continue;

                    int band = Mathf.Clamp(
                        Mathf.FloorToInt(debugFlowField.CostFrom(hop.Key) / maxCost * flowBuckets.Length),
                        0, flowBuckets.Length - 1);
                    if (flowBuckets[band].Count >= MaxInstances) continue;

                    // Full segment a→b; the FlowStub shader fades cold(-Z, node end) →
                    // hot(+Z, hop end) — the bright tip is the direction tracer.
                    flowBuckets[band].Add(Matrix4x4.TRS(
                        (a + b) * 0.5f,
                        Quaternion.LookRotation(dir.normalized, Vector3.up),
                        new Vector3(flowStubWidth, flowStubWidth, len)));
                }

                var mat = flowMaterial != null ? flowMaterial : beamMaterial;
                for (int band = 0; band < flowBuckets.Length; band++)
                {
                    if (flowBuckets[band].Count == 0) continue;
                    flowBuckets[band].CopyTo(batchBuffer);
                    beamProps.Clear();
                    Color hot = flowBandColors[band];
                    Color cold = hot * 0.35f;
                    cold.a = 0.12f;
                    if (mat == flowMaterial)
                    {
                        beamProps.SetColor("_ColdColor", cold);
                        beamProps.SetColor("_HotColor", hot);
                    }
                    else
                    {
                        beamProps.SetColor("_Color", hot);
                    }
                    DrawInstanced(cmd, batchBuffer, flowBuckets[band].Count, mat, beamProps);
                    segDrawCount++;
                }
            }

            for (int t = 0; t < MaxTypes; t++)
            {
                int start = segRanges[t].start;
                int count = segRanges[t].count;
                if (count == 0) continue;

                var (_, col) = GetStyle((PathDebugType)t);
                beamProps.Clear();
                beamProps.SetColor("_Color", col);

                System.Array.Copy(segmentMatrices, start, batchBuffer, 0, count);
                DrawInstanced(cmd, batchBuffer, count, beamMaterial, beamProps);
                segDrawCount++;
            }

            // Render markers batched by type
            for (int t = 0; t < MaxTypes; t++)
            {
                int start = markerRanges[t].start;
                int count = markerRanges[t].count;
                if (count == 0) continue;

                var (_, col) = GetStyle((PathDebugType)t);
                beamProps.Clear();
                beamProps.SetColor("_Color", col);

                System.Array.Copy(markerMatrices, start, batchBuffer, 0, count);
                DrawInstanced(cmd, batchBuffer, count, beamMaterial, beamProps);
                markerDrawCount++;
            }

            // Render waypoint graph debug links (all sidewalk + crosswalk links)
            if (graphSegCount > 0)
            {
                beamProps.Clear();
                beamProps.SetColor("_Color", graphSidewalkColor);
                System.Array.Copy(segmentMatrices, segCount, batchBuffer, 0, graphSegCount);
                DrawInstanced(cmd, batchBuffer, graphSegCount, beamMaterial, beamProps);
                segDrawCount++;
            }

            // Render waypoint graph debug nodes
            if (graphNodeCount > 0)
            {
                beamProps.Clear();
                beamProps.SetColor("_Color", graphCornerColor);
                System.Array.Copy(markerMatrices, markerCount, batchBuffer, 0, graphNodeCount);
                DrawInstanced(cmd, batchBuffer, graphNodeCount, beamMaterial, beamProps);
                markerDrawCount++;
            }

            // Free-form custom beams — one draw each (debug volume is tiny)
            foreach (var beam in customBeams.Values)
            {
                Vector3 dir = beam.b - beam.a;
                float len = dir.magnitude;
                if (len < 0.001f) continue;
                // LookRotation degenerates when dir ≈ ±up — swap the up ref
                Vector3 upRef = Mathf.Abs(dir.normalized.y) > 0.98f ? Vector3.forward : Vector3.up;
                Quaternion rot = Quaternion.LookRotation(dir.normalized, upRef);
                batchBuffer[0] = Matrix4x4.TRS((beam.a + beam.b) * 0.5f, rot,
                    new Vector3(beam.width, beam.width, len));
                beamProps.Clear();
                beamProps.SetColor("_Color", beam.color);
                DrawInstanced(cmd, batchBuffer, 1, beamMaterial, beamProps);
                segDrawCount++;
            }

            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Dispose();

            if (Time.frameCount % 60 == 0)
                VLog($"[PathDebug] CommandBuffer executed. segDraws={segDrawCount}, markerDraws={markerDrawCount}");
        }
    }
}
