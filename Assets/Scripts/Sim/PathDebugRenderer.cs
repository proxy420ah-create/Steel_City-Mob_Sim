using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SteelCity.Sim
{
    public enum PathDebugType
    {
        Pedestrian, // thin orange
        Car,        // thick purple
        Trolley     // thickest green
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

        [Header("Render")]
        [SerializeField] private Camera targetCamera;

        private Transform mapRoot;
        private Mesh boxMesh;
        private Material beamMaterial;

        private readonly List<ActivePath> activePaths = new();

        // Per-instance data buffers (reused each frame, no allocation)
        private const int MaxInstances = 2048;
        private readonly Matrix4x4[] segmentMatrices = new Matrix4x4[MaxInstances];
        private readonly Matrix4x4[] markerMatrices = new Matrix4x4[MaxInstances];

        // Per-type batch tracking (up to 3 types: Pedestrian, Car, Trolley)
        private const int MaxTypes = 3;
        private int segCount, markerCount;
        private Vector3[] worldPositions = new Vector3[256];
        private Matrix4x4[] batchBuffer = new Matrix4x4[MaxInstances];
        private bool _hasLoggedEntry;
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
                _ => (pedestrianWidth, pedestrianColor)
            };
        }

        public void RegisterPath(Transform entity, Vector3 entityWorldSize,
            System.Func<List<string>> routeProvider,
            System.Func<string, Vector3> resolveNodePos,
            PathDebugType type,
            System.Func<int> progressProvider = null)
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
                progressProvider = progressProvider
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
            if (activePaths.Count == 0 && !showWaypointGraph) return;

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
                    Debug.Log($"[PathDebug] RenderBeamsIntoCamera SKIP: mapRoot={mapRoot != null}, boxMesh={boxMesh != null}, beamMat={beamMaterial != null}");
                return;
            }
            if (activePaths.Count == 0 && !showWaypointGraph)
            {
                if (Time.frameCount % 120 == 0)
                    Debug.Log("[PathDebug] RenderBeamsIntoCamera SKIP: no active paths and graph debug off");
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
                Debug.Log($"[PathDebug] RenderBeamsInternal FIRST CALL: activePaths={activePaths.Count}, targetRT={(targetRT != null ? targetRT.name : "NULL")} ({(targetRT != null ? $"{targetRT.width}x{targetRT.height}" : "")}), cam={(cam != null ? cam.name : "NULL")}, mapRoot={(mapRoot != null ? mapRoot.position.ToString("F2") : "NULL")}, boxMesh={(boxMesh != null ? "OK" : "NULL")}, beamMat={(beamMaterial != null ? "OK" : "NULL")}");
            }

            // Diagnostic: log state every 60 frames
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"[PathDebug] RenderBeamsInternal: activePaths={activePaths.Count}, targetRT={(targetRT != null ? targetRT.name : "NULL")}, cam={(cam != null ? cam.name : "NULL")}, mapRoot={(mapRoot != null ? mapRoot.position.ToString("F2") : "NULL")}, boxMesh={(boxMesh != null ? "OK" : "NULL")}, beamMat={(beamMaterial != null ? "OK" : "NULL")}");
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
                    Debug.Log($"[PathDebug] Path[{i}] type={ap.type}, entity={ap.entity?.name ?? "NULL"}, routeCount={route?.Count ?? -1}, progress={prog}, remaining={(route != null ? route.Count - prog : -1)}");
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
                        Debug.Log($"[PathDebug] Path[{i}] INVALID — node position returned NaN, removing");
                    activePaths.RemoveAt(i);
                    continue;
                }

                // Diagnostic: log first few resolved positions
                if (Time.frameCount % 60 == 0 && remainingCount > 0)
                {
                    Debug.Log($"[PathDebug] Path[{i}] resolved {remainingCount} positions. First={worldPositions[0].ToString("F2")}, Last={worldPositions[remainingCount - 1].ToString("F2")}");
                }

                // Build segment boxes between consecutive waypoints
                for (int j = 0; j < remainingCount - 1; j++)
                {
                    if (segCount >= MaxInstances) break;

                    Vector3 a = worldPositions[j];
                    Vector3 b = worldPositions[j + 1];
                    Vector3 mid = (a + b) * 0.5f;
                    Vector3 dir = b - a;
                    float len = dir.magnitude;

                    if (len < 0.001f) continue;

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
                Debug.Log($"[PathDebug] Batches: segCount={segCount}, markerCount={markerCount}, totalSeg={totalSeg}, totalMarker={totalMarker}");
                for (int t = 0; t < MaxTypes; t++)
                {
                    if (segRanges[t].count > 0 || markerRanges[t].count > 0)
                        Debug.Log($"[PathDebug] Type[{t}] segs={segRanges[t].count} (start={segRanges[t].start}), markers={markerRanges[t].count} (start={markerRanges[t].start})");
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

            for (int t = 0; t < MaxTypes; t++)
            {
                int start = segRanges[t].start;
                int count = segRanges[t].count;
                if (count == 0) continue;

                var (_, col) = GetStyle((PathDebugType)t);
                beamProps.Clear();
                beamProps.SetColor("_Color", col);

                System.Array.Copy(segmentMatrices, start, batchBuffer, 0, count);
                cmd.DrawMeshInstanced(boxMesh, 0, beamMaterial, 0, batchBuffer, count, beamProps);
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
                cmd.DrawMeshInstanced(boxMesh, 0, beamMaterial, 0, batchBuffer, count, beamProps);
                markerDrawCount++;
            }

            // Render waypoint graph debug links (all sidewalk + crosswalk links)
            if (graphSegCount > 0)
            {
                beamProps.Clear();
                beamProps.SetColor("_Color", graphSidewalkColor);
                System.Array.Copy(segmentMatrices, segCount, batchBuffer, 0, graphSegCount);
                cmd.DrawMeshInstanced(boxMesh, 0, beamMaterial, 0, batchBuffer, graphSegCount, beamProps);
                segDrawCount++;
            }

            // Render waypoint graph debug nodes
            if (graphNodeCount > 0)
            {
                beamProps.Clear();
                beamProps.SetColor("_Color", graphCornerColor);
                System.Array.Copy(markerMatrices, markerCount, batchBuffer, 0, graphNodeCount);
                cmd.DrawMeshInstanced(boxMesh, 0, beamMaterial, 0, batchBuffer, graphNodeCount, beamProps);
                markerDrawCount++;
            }

            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Dispose();

            if (Time.frameCount % 60 == 0)
                Debug.Log($"[PathDebug] CommandBuffer executed. segDraws={segDrawCount}, markerDraws={markerDrawCount}");
        }
    }
}
