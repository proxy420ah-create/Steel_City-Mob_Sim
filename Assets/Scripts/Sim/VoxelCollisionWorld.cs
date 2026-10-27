using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// CPU-side voxel collision world — SteelTide VoxelWorld approach.
    /// Stores terrain voxels in a sparse dictionary and provides DDA raymarch
    /// probing for ground collision. No Unity colliders needed.
    ///
    /// VoxelCharacter probes downward each frame to find ground height,
    /// then snaps to surface or applies gravity if airborne.
    /// </summary>
    public class VoxelCollisionWorld : MonoBehaviour
    {
        [Header("Debug")]
        public bool showDebugRays = false;

        // Flat voxel storage: indexed by x + y*gridW + z*gridW*gridH
        private byte[] voxelGrid;
        private int gridW, gridH, gridD;

        // World origin of the voxel grid (corner of the volume)
        private Vector3 gridOrigin;
        private float voxelSize = 0.1f;
        private bool initialized = false;

        // Solid building volumes (shared voxel arrays + placement) for ray queries — see SolidVolumeSet.
        private readonly SolidVolumeSet volumes = new SolidVolumeSet();

        public bool IsInitialized => initialized;

        /// <summary>The persistent world-space walkability layer — ONE owner
        /// of ground/material/fit verdicts for the whole map. Every write
        /// path below dirties exactly the cells it touched; CoverField
        /// windows bulk-copy the cache instead of re-probing per fighter.</summary>
        public WalkLayer Walk { get; private set; }

        void Awake() { Walk = new WalkLayer(this); }

        /// <summary>World AABB of a registered volume — exact when axis-aligned
        /// (yaw≈0, every current caller), conservative cover-all square for a
        /// rotated volume (rare; over-clears, re-probes lazily).</summary>
        public static Bounds VolumeAabb(Vector3 corner, int w, int h, int d, float vs, float yawDeg)
        {
            Vector3 size = new Vector3(w * vs, h * vs, d * vs);
            if (Mathf.Abs(yawDeg) < 0.01f)
                return new Bounds(corner + size * 0.5f, size);
            float diag = Mathf.Sqrt(size.x * size.x + size.z * size.z);
            return new Bounds(corner + size * 0.5f, new Vector3(diag * 2f, size.y, diag * 2f));
        }

        /// <summary>Number of registered building volumes.</summary>
        public int VolumeCount => volumes.Count;

        /// <summary>True when Raycast has anything to hit (terrain grid or at least one volume).</summary>
        public bool HasRaycastGeometry => initialized || volumes.Count > 0;

        /// <summary>World-space origin (corner) of the voxel grid.</summary>
        public Vector3 GridOrigin => gridOrigin;

        /// <summary>Voxel size in world units.</summary>
        public float VoxelSize => voxelSize;

        /// <summary>
        /// Get voxel material at a grid coordinate. Returns 0 (air) if not found.
        /// </summary>
        public byte GetVoxelAtGrid(Vector3Int gridPos)
        {
            int idx = gridPos.x + gridPos.y * gridW + gridPos.z * gridW * gridH;
            if (idx < 0 || idx >= voxelGrid.Length) return 0;
            return voxelGrid[idx];
        }

        /// <summary>
        /// Register terrain voxel data into the sparse grid.
        /// Called after VoxelTerrainBuilder generates the terrain chunk.
        /// </summary>
        public void RegisterTerrain(uint[] data, int w, int h, int d, Vector3 worldOrigin, float vs)
        {
            gridOrigin = worldOrigin;
            voxelSize = vs;
            gridW = w; gridH = h; gridD = d;
            voxelGrid = new byte[w * h * d];

            int registered = 0;
            for (int z = 0; z < d; z++)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        uint packed = data[x + y * w + z * w * h];
                        if (packed != 0)
                        {
                            voxelGrid[x + y * w + z * w * h] = (byte)(packed & 0xFF);
                            registered++;
                        }
                    }
                }
            }

            initialized = true;
            Debug.Log($"[VoxelCollisionWorld] Registered {registered:N0} terrain voxels ({w}x{h}x{d}) at origin {worldOrigin}, voxelSize={vs}");
            Walk?.InvalidateAll();   // wholesale rewrite — every verdict is stale
        }

        /// <summary>
        /// Append a single terrain chunk's voxel data into the sparse grid.
        /// Used for split terrain — each chunk has its own world origin but
        /// shares the same global voxel grid (all chunks use the same voxelSize
        /// and are positioned in world space, so grid coordinates are global).
        /// </summary>
        public void RegisterTerrainChunk(uint[] data, int w, int h, int d, Vector3 chunkWorldOrigin, float vs)
        {
            if (!initialized)
            {
                gridOrigin = chunkWorldOrigin;
                voxelSize = vs;
                gridW = w; gridH = h; gridD = d;
                voxelGrid = new byte[w * h * d];
                initialized = true;
            }

            // Offset of this chunk relative to the global grid origin
            Vector3 originOffset = (chunkWorldOrigin - gridOrigin) / voxelSize;
            int offX = Mathf.RoundToInt(originOffset.x);
            int offY = Mathf.RoundToInt(originOffset.y);
            int offZ = Mathf.RoundToInt(originOffset.z);

            // Compute new bounds accounting for negative offsets
            int newOriginX = Mathf.Min(0, offX); // shift in X (negative = chunks left of origin)
            int newOriginZ = Mathf.Min(0, offZ);
            int newOriginY = Mathf.Min(0, offY);
            int endX = Mathf.Max(gridW, offX + w);
            int endY = Mathf.Max(gridH, offY + h);
            int endZ = Mathf.Max(gridD, offZ + d);
            int newW = endX - newOriginX;
            int newH = endY - newOriginY;
            int newD = endZ - newOriginZ;

            // Re-allocate if bounds changed
            if (newW != gridW || newH != gridH || newD != gridD || newOriginX != 0 || newOriginY != 0 || newOriginZ != 0)
            {
                var newGrid = new byte[newW * newH * newD];
                // Copy existing data into new grid (shifted by -newOriginX/Y/Z)
                int shiftX = -newOriginX;
                int shiftY = -newOriginY;
                int shiftZ = -newOriginZ;
                for (int z = 0; z < gridD; z++)
                    for (int y = 0; y < gridH; y++)
                        for (int x = 0; x < gridW; x++)
                            newGrid[(x + shiftX) + (y + shiftY) * newW + (z + shiftZ) * newW * newH] =
                                voxelGrid[x + y * gridW + z * gridW * gridH];

                voxelGrid = newGrid;
                gridW = newW; gridH = newH; gridD = newD;

                // Shift grid origin in world space
                gridOrigin += new Vector3(newOriginX * voxelSize, newOriginY * voxelSize, newOriginZ * voxelSize);

                // Recompute offset relative to new origin
                offX -= newOriginX;
                offY -= newOriginY;
                offZ -= newOriginZ;
            }

            // Write chunk data into flat grid (O(1) per voxel — no dictionary hashing)
            for (int z = 0; z < d; z++)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        uint packed = data[x + y * w + z * w * h];
                        if (packed != 0)
                            voxelGrid[(x + offX) + (y + offY) * gridW + (z + offZ) * gridW * gridH] = (byte)(packed & 0xFF);
                    }
                }
            }
            // The chunk's footprint — only its cells' verdicts are stale.
            Vector3 csz = new Vector3(w * vs, h * vs, d * vs);
            Walk?.Invalidate(new Bounds(chunkWorldOrigin + csz * 0.5f, csz));
        }

        /// <summary>
        /// DDA raymarch downward from a world position. Returns ground hit info.
        /// This is the SteelTide VoxelWorld.RaymarchChunk pattern, simplified for
        /// straight-down ground probes. Covers terrain AND registered volumes —
        /// characters can stand on rooftops and floors, not just the street grid.
        /// </summary>
        public bool ProbeGround(Vector3 worldPos, float maxDistance, out float groundY, out Vector3 normal)
        {
            groundY = 0f;
            normal = Vector3.up;
            bool found = false;

            // Volume floors first — a down-raycast returns the nearest solid top
            // below the probe point (roof deck, interior floor, wall base).
            if (volumes.Raycast(worldPos, Vector3.down, maxDistance, out VoxelRayHit vh))
            {
                groundY = vh.point.y;
                normal = vh.normal;
                found = true;
            }

            // Convert world position to voxel grid coordinates
            Vector3 local = (worldPos - gridOrigin) / voxelSize;
            int vx = Mathf.FloorToInt(local.x);
            int vz = Mathf.FloorToInt(local.z);

            // Bounds check — probing outside the registered grid must fail
            // fast instead of falling through to the flat-array index math below,
            // which does NOT validate vx/vz and can alias into unrelated voxel
            // data at a completely different (bogus) height if left unchecked.
            if (vx < 0 || vx >= gridW || vz < 0 || vz >= gridD)
                return found;

            // Start from the character's Y and march downward
            int vy = Mathf.FloorToInt(local.y);
            if (vy >= gridH) vy = gridH - 1;

            // DDA straight down — just scan Y voxels from current position
            int steps = 0;
            int maxSteps = Mathf.CeilToInt(maxDistance / voxelSize) + 1;

            while (steps < maxSteps && vy >= 0)
            {
                int idx = vx + vy * gridW + vz * gridW * gridH;
                if (idx >= 0 && idx < voxelGrid.Length && voxelGrid[idx] != 0)
                {
                    // Hit solid voxel — ground is at the TOP of this voxel.
                    // A volume floor higher up still wins (e.g. a roof above street).
                    float ty = gridOrigin.y + (vy + 1) * voxelSize;
                    if (!found || ty > groundY) { groundY = ty; normal = Vector3.up; }
                    return true;
                }
                vy--;
                steps++;
            }

            return found; // terrain missed — a volume floor may still have hit
        }

        /// <summary>
        /// Check if there's solid ground at a world XZ position (any height).
        /// </summary>
        public bool HasGroundAt(Vector3 worldPos)
        {
            Vector3 local = (worldPos - gridOrigin) / voxelSize;
            int vx = Mathf.FloorToInt(local.x);
            int vz = Mathf.FloorToInt(local.z);

            // Check a few Y layers (terrain is 2 voxels thick)
            for (int vy = 0; vy < 10; vy++)
            {
                int idx = vx + vy * gridW + vz * gridW * gridH;
                if (idx >= 0 && idx < voxelGrid.Length && voxelGrid[idx] != 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Register (or replace, by id) a solid voxel volume — a building instance. The voxel array is
        /// referenced, not copied; treat it as read-only. corner = world min corner of the grid.
        /// </summary>
        public void RegisterVolume(string id, uint[] voxels, int w, int h, int d, Vector3 corner, float voxelSize, float yawDegrees = 0f)
        {
            volumes.Register(id, voxels, w, h, d, corner, voxelSize, yawDegrees);
            Walk?.Invalidate(VolumeAabb(corner, w, h, d, voxelSize, yawDegrees));
        }

        public bool UnregisterVolume(string id)
        {
            // Capture the footprint before removal — the cells under it must
            // re-probe to open space.
            Bounds b = default;
            bool had = TryGetVolume(id, out _, out int vw, out int vh, out int vd,
                                    out Vector3 vc, out float vvs, out float vyaw);
            if (had) b = VolumeAabb(vc, vw, vh, vd, vvs, vyaw);
            bool ok = volumes.Unregister(id);
            if (ok && had) Walk?.Invalidate(b);
            return ok;
        }

        /// <summary>Fetch a registered volume's array + placement — the array is shared/read-only (see SolidVolumeSet.TryGetVolume).</summary>
        public bool TryGetVolume(string id, out uint[] voxels, out int w, out int h, out int d,
            out Vector3 corner, out float voxelSize, out float yawDegrees)
            => volumes.TryGetVolume(id, out voxels, out w, out h, out d, out corner, out voxelSize, out yawDegrees);

        public void ClearVolumes() { volumes.Clear(); Walk?.InvalidateAll(); }

        /// <summary>Solid volumes overlapping the XZ circle (center, radius) — feeds cover awareness.</summary>
        public int QueryVolumes(Vector3 center, float radius, List<CoverObstacle> results)
            => volumes.QueryVolumes(center, radius, results);

        /// <summary>Cover-capable wall columns of volumes overlapping the XZ circle — feeds CoverSensor.</summary>
        public int QueryCoverWalls(Vector3 center, float radius, float minSpanWorld, float openAtWorld, List<CoverWall> results)
            => volumes.QueryCoverWalls(center, radius, minSpanWorld, openAtWorld, results);

        /// <summary>The 5-sample body column at a feet-center position —
        /// feet+0.06 / waist 0.35h / mid 0.55h / chest 0.75h / head 0.9h
        /// (the top band was once untested — balcony lips and lintels at
        /// head height passed invisibly). THE atomic body-space
        /// primitive: the cover field's cell verdicts, the continuous
        /// footprint probes, both movers, and the corridor sweeps all answer
        /// "is the body column clear here" through THIS — one owner, no
        /// re-derivation, no driftable copies. (The waist sample is what
        /// keeps bodies out of car cabins.)</summary>
        public bool ColumnFree(Vector3 feetCenter, float bodyH)
            => ColumnFreeDetail(feetCenter, bodyH, out _, out _);

        /// <summary>ColumnFree with the verdict explained — on failure `why`
        /// names the failed sample (feet/waist/chest) and WHAT it hit
        /// (terrain or the owning volume's id+cell), and `hitPos` is the
        /// sample's world position — walkable-to coordinates of the exact
        /// offending voxel. One implementation serves the hot bool path and
        /// the snag diagnostic — the law can't fork.</summary>
        public bool ColumnFreeDetail(Vector3 feetCenter, float bodyH,
                                     out string why, out Vector3 hitPos)
        {
            hitPos = feetCenter + Vector3.up * 0.06f;
            var f = SamplePoint(hitPos);
            if (Solid(f.kind)) { why = $"feet:{Hit(f)}"; return false; }
            hitPos = feetCenter + Vector3.up * (bodyH * 0.35f);
            var w = SamplePoint(hitPos);
            if (Solid(w.kind)) { why = $"waist:{Hit(w)}"; return false; }
            hitPos = feetCenter + Vector3.up * (bodyH * 0.55f);
            var m = SamplePoint(hitPos);
            if (Solid(m.kind)) { why = $"mid:{Hit(m)}"; return false; }
            hitPos = feetCenter + Vector3.up * (bodyH * 0.75f);
            var c = SamplePoint(hitPos);
            if (Solid(c.kind)) { why = $"chest:{Hit(c)}"; return false; }
            hitPos = feetCenter + Vector3.up * (bodyH * 0.9f);
            var h = SamplePoint(hitPos);
            if (Solid(h.kind)) { why = $"head:{Hit(h)}"; return false; }
            why = null; hitPos = feetCenter; return true;

            static bool Solid(PointKind k)
                => k == PointKind.TerrainSolid || k == PointKind.VolumeSolid;
            static string Hit(in PointSample s)
                => s.kind == PointKind.VolumeSolid ? $"vol:{s.volumeId}#{s.cellIdx}" : "terrain";
        }

        // The square's 9-column pattern as unit directions — center first,
        // then corners, then edge midpoints. SquareFree, TrySquareStepUp, and
        // ExplainSquare all iterate THIS table: the verdict, the climb probe,
        // and the diagnostic can never drift apart.
        private static readonly (int x, int z)[] SquareDirs =
            { (0,0), (1,1), (1,-1), (-1,1), (-1,-1), (1,0), (-1,0), (0,1), (0,-1) };
        private static readonly string[] SquareNames =
            { "ctr", "+x+z", "+x-z", "-x+z", "-x-z", "+x", "-x", "+z", "-z" };

        /// <summary>Collision skin — the tested square runs this much wider
        /// than the spec'd footprint on every side, so the body can never
        /// press flush against a face. Sub-skin discontinuities (volume-seam
        /// steps, half-cell lips, recess wedges) live INSIDE the skin zone
        /// the tested square never enters: wall-following slides OVER the
        /// seam instead of jamming into it. One constant inside the shared
        /// primitive — the field certifies, corridors sweep, and both movers
        /// execute the identical inflated envelope (a mover-only skin would
        /// certify cells the body can't reach).</summary>
        public const float BodySkin = 0.06f;

        /// <summary>The body SQUARE at a feet-center — the column at center
        /// plus the 4 perimeter corners AND the 4 edge midpoints, all at
        /// off = half + BodySkin − 0.03. Corners alone left a half-wide
        /// blind span along each edge — a wall corner or yawed face slid
        /// between them and the square nosed ~`half` deep before the center
        /// column hit. THE verdict for "can this body stand centered here":
        /// the map's certification, the mover's executor, and possess mode
        /// all consume the identical square — physically impossible to
        /// diverge.</summary>
        public bool SquareFree(Vector3 feetCenter, float half, float bodyH)
        {
            if (!ColumnFree(feetCenter, bodyH)) return false;
            float off = Mathf.Max(0f, half + BodySkin - 0.03f);
            for (int i = 1; off > 0f && i < SquareDirs.Length; i++)
                if (!ColumnFree(feetCenter + new Vector3(SquareDirs[i].x * off, 0f,
                        SquareDirs[i].z * off), bodyH))
                    return false;
            return true;
        }

        /// <summary>Per-column verdict dump of the IDENTICAL SquareFree
        /// pattern — the snag diagnostic: which of the 9 columns failed, at
        /// which body height, and inside which volume's voxel. Appends
        /// "name=ok|feet:vol:id…" tokens so a blocked move can report exactly
        /// which sample of the certified envelope refused.</summary>
        public void ExplainSquare(Vector3 feetCenter, float half, float bodyH,
                                  System.Text.StringBuilder sb)
        {
            float off = Mathf.Max(0f, half + BodySkin - 0.03f);
            for (int i = 0; i < SquareDirs.Length; i++)
            {
                if (i > 0 && off <= 0f) break;
                Vector3 p = i == 0 ? feetCenter
                    : feetCenter + new Vector3(SquareDirs[i].x * off, 0f,
                                               SquareDirs[i].z * off);
                bool ok = ColumnFreeDetail(p, bodyH, out string why, out Vector3 hitPos);
                if (i > 0) sb.Append(' ');
                sb.Append(SquareNames[i]);
                if (ok) sb.Append("=ok");
                else sb.Append('=').Append(why)
                       .Append($"@({hitPos.x:F2},{hitPos.y:F2},{hitPos.z:F2})");
            }
        }

        /// <summary>Step-up probe over the body square — every column of
        /// SquareFree's own 9-sample pattern gets its column top; the
        /// highest top inside (feet+0.04, feet+stepUp] wins. A thin obstacle
        /// that only touches the square's LEADING EDGE now offers its top —
        /// a center-only probe read the ground beyond the chunk and never
        /// climbed (the snag-on-debris class). Caller re-verifies the whole
        /// square at the raised height — a wall still walls.</summary>
        public bool TrySquareStepUp(Vector3 feetCenter, float half, float stepUpMax,
                                    out float topY)
        {
            float feetY = feetCenter.y;
            float best = feetY + 0.04f;
            bool found = false;
            void TryCol(Vector3 p)
            {
                if (TryColumnTop(p, stepUpMax + 0.06f, 1f, out float t)
                    && t > best && t <= feetY + stepUpMax)
                { best = t; found = true; }
            }
            TryCol(feetCenter);
            float off = Mathf.Max(0f, half + BodySkin - 0.03f);
            for (int i = 1; off > 0f && i < SquareDirs.Length; i++)
                TryCol(feetCenter + new Vector3(SquareDirs[i].x * off, 0f,
                                                SquareDirs[i].z * off));
            topY = best;
            return found;
        }

        /// <summary>The mover's whole placement question as ONE primitive:
        /// the square free at feetCenter, else a step-up top offered AND the
        /// raised square free — TryMove's exact composition. The field's
        /// walkable cert, both movers, and corridor sweeps all run this —
        /// "can the body be PLACED here, climbs included" can never fork
        /// into a map-answer the body doesn't execute.</summary>
        public bool TryBodySquare(Vector3 feetCenter, float half, float bodyH,
                                  float stepUp, out float placedY)
        {
            placedY = feetCenter.y;
            if (SquareFree(feetCenter, half, bodyH)) return true;
            if (TrySquareStepUp(feetCenter, half, stepUp, out float top))
            {
                Vector3 up = feetCenter; up.y = top;
                if (SquareFree(up, half, bodyH)) { placedY = top; return true; }
            }
            return false;
        }

        /// <summary>Unstuck recovery — nearest free body-square on real
        /// ground. Ring scan outward from `feetCenter` (8 directions per
        /// 0.25m ring): each candidate probes its column top (must land
        /// within ±1.5m of the current feet — a roof or a pit is a different
        /// place, not an escape) and runs the identical skinned SquareFree.
        /// Possess-mode escape only — routed movers never reach this state
        /// because their edges are transition-certified.</summary>
        public bool TryNearestFreeSquare(Vector3 feetCenter, float half, float bodyH,
                                         float maxRadius, out Vector3 free)
        {
            free = feetCenter;
            const int dirs = 8;
            for (float r = 0.25f; r <= maxRadius; r += 0.25f)
                for (int k = 0; k < dirs; k++)
                {
                    float ang = k * (Mathf.PI * 2f) / dirs;
                    Vector3 p = feetCenter + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                    p.y = feetCenter.y;
                    if (!TryColumnTop(p, 0.5f, 2.5f, out float topY)) continue;
                    if (Mathf.Abs(topY - feetCenter.y) > 1.5f) continue;
                    Vector3 cand = new Vector3(p.x, topY, p.z);
                    if (SquareFree(cand, half, bodyH)) { free = cand; return true; }
                }
            return false;
        }

        /// <summary>World Y of the solid column top under `pos` — the character's
        /// support surface for the cover heightfield. Downward ray from
        /// `pos + probeUp` through `probeDown`; hits volumes AND terrain, so
        /// standing on crates/rubble/curbs reads correctly (additive height).
        /// Returns false when nothing solid is found below.</summary>
        public bool TryColumnTop(Vector3 pos, float probeUp, float probeDown, out float topY)
        {
            topY = 0f;
            if (!Raycast(pos + Vector3.up * probeUp, Vector3.down, probeUp + probeDown,
                    out VoxelRayHit hit)) return false;
            topY = hit.point.y;
            return true;
        }

        /// <summary>
        /// 3D voxel raycast against the terrain grid and every registered volume; returns the nearest hit
        /// within maxDistance. direction need not be normalized. Allocation-free (~2 µs per shot on the
        /// firing-range asset). Solid = any non-air material; shape bits are ignored.
        /// </summary>
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out VoxelRayHit hit,
            bool includeTerrain = true, bool includeVolumes = true)
        {
            hit = default;
            float len = direction.magnitude;
            if (len < 1e-8f || !(maxDistance > 0f)) return false;
            Vector3 dir = direction / len;
            float best = maxDistance;
            bool found = false;

            if (includeVolumes && volumes.Raycast(origin, dir, best, out VoxelRayHit vh))
            {
                hit = vh;
                best = vh.distance;
                found = true;
            }

            if (includeTerrain && initialized && voxelGrid != null)
            {
                double inv = 1.0 / voxelSize;
                if (VoxelDda.Raycast(voxelGrid, gridW, gridH, gridD,
                        (origin.x - gridOrigin.x) * inv, (origin.y - gridOrigin.y) * inv, (origin.z - gridOrigin.z) * inv,
                        dir.x * inv, dir.y * inv, dir.z * inv, best, out DdaHit th))
                {
                    float t = (float)th.t;
                    hit = new VoxelRayHit
                    {
                        distance = t,
                        point = origin + dir * t,
                        normal = new Vector3(th.nx, th.ny, th.nz),
                        cell = new Vector3Int(th.x, th.y, th.z),
                        voxel = th.voxel,
                        material = (int)(th.voxel & VoxelDda.MaterialMask),
                        volumeId = null
                    };
                    found = true;
                }
            }
            return found;
        }

        public enum PointKind : byte { Free = 0, TerrainSolid = 1, VolumeOpen = 2, VolumeSolid = 3 }
        public struct PointSample { public PointKind kind; public string volumeId; public int cellIdx; }

        /// <summary>
        /// Cheap occupancy query for debris-scale objects (step ≤ ~1 voxel/frame —
        /// NOT a substitute for swept Raycast). A containing volume wins over terrain
        /// (its AABB is the more specific region); inside the volumes a solid cell
        /// beats an open one. Cell + id are only meaningful for VolumeOpen/Solid.
        /// </summary>
        public PointSample SamplePoint(Vector3 pos)
        {
            if (volumes.SamplePoint(pos, out string vid, out int idx, out bool solid))
                return new PointSample { kind = solid ? PointKind.VolumeSolid : PointKind.VolumeOpen, volumeId = vid, cellIdx = idx };
            if (initialized && voxelGrid != null)
            {
                Vector3 local = (pos - gridOrigin) / voxelSize;
                int gx = Mathf.FloorToInt(local.x), gy = Mathf.FloorToInt(local.y), gz = Mathf.FloorToInt(local.z);
                if (gx >= 0 && gy >= 0 && gz >= 0 && gx < gridW && gy < gridH && gz < gridD &&
                    voxelGrid[gx + gy * gridW + gz * gridW * gridH] != 0)
                    return new PointSample { kind = PointKind.TerrainSolid };
            }
            return new PointSample { kind = PointKind.Free };
        }

        public Vector3Int WorldToVoxelGrid(Vector3 worldPos)
        {
            Vector3 local = (worldPos - gridOrigin) / voxelSize;
            return new Vector3Int(
                Mathf.FloorToInt(local.x),
                Mathf.FloorToInt(local.y),
                Mathf.FloorToInt(local.z));
        }

        public Vector3 VoxelGridToWorld(Vector3Int gridPos)
        {
            return gridOrigin + new Vector3(
                gridPos.x * voxelSize + voxelSize * 0.5f,
                gridPos.y * voxelSize + voxelSize * 0.5f,
                gridPos.z * voxelSize + voxelSize * 0.5f);
        }

        void OnDrawGizmos()
        {
            if (!showDebugRays || !initialized) return;

            // Draw a few sample voxels for debugging
            int drawn = 0;
            for (int z = 0; z < gridD && drawn <= 500; z++)
            {
                for (int y = 0; y < gridH && drawn <= 500; y++)
                {
                    for (int x = 0; x < gridW && drawn <= 500; x++)
                    {
                        if (voxelGrid[x + y * gridW + z * gridW * gridH] != 0)
                        {
                            Vector3 pos = VoxelGridToWorld(new Vector3Int(x, y, z));
                            Gizmos.color = new Color(0.3f, 0.6f, 0.3f, 0.3f);
                            Gizmos.DrawCube(pos, Vector3.one * voxelSize * 0.9f);
                            drawn++;
                        }
                    }
                }
            }
        }
    }
}
