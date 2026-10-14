using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>Nearest solid voxel along a world-space ray (terrain grid or a registered volume).</summary>
    public struct VoxelRayHit
    {
        public float distance;       // world units from the ray origin
        public Vector3 point;        // world-space point on the entered face
        public Vector3 normal;       // world-space face normal (zero when the origin starts inside a solid cell)
        public Vector3Int cell;      // voxel indices in the hit grid (volume-local, or the terrain grid)
        public uint voxel;           // raw packed voxel value
        public int material;         // voxel & 0x1FF
        public string volumeId;      // registered volume id; null = terrain
    }

    /// <summary>
    /// CPU-side registry of solid voxel volumes for ballistic / line-of-sight queries.
    ///
    /// Each entry is a *reference* to a voxel array that is already in memory for rendering (the packed
    /// .stasset cache or a pooled debris variant) plus its placement — nothing is copied, so a thousand
    /// building instances of a dozen assets cost a thousand small records. This is the same model the
    /// instancing architecture uses on the GPU: shared asset data, per-instance transform.
    ///
    /// Placement convention is the renderer's: cell (i,j,k) of a volume spans
    /// [corner + (i,j,k)*vs, corner + (i+1,j+1,k+1)*vs]. An optional yaw rotates the volume about its
    /// center (WeaponMount / shader convention); sector-baked buildings use yaw = 0.
    /// </summary>
    public sealed class SolidVolumeSet
    {
        private sealed class Volume
        {
            public string id;
            public uint[] voxels;
            public int w, h, d;
            public Vector3 corner;
            public float vs;
            public float yawDeg;
            public Vector3 center, half;
            public float cosY = 1f, sinY;
            public Vector3 wMin, wMax;   // world AABB (broadphase)
        }

        private readonly List<Volume> volumes = new List<Volume>();
        private readonly Dictionary<string, int> index = new Dictionary<string, int>();

        // XZ uniform grid over volume AABBs — Raycast walks only the cells the
        // segment crosses instead of every volume in the city. A volume is
        // inserted into every cell its world AABB overlaps; per-query dedup via
        // the `seen` stamp means multi-cell volumes test once. Rebuilt lazily on
        // the next query after any Register/Unregister/Clear.
        private const float gridCell = 32f;
        private readonly Dictionary<int, List<int>> grid = new();
        private int[] seen;
        private int seenStamp;
        private bool gridDirty = true;

        public int Count => volumes.Count;

        /// <summary>Register (or replace, by id) a volume. The voxel array is referenced, never copied — treat it as read-only.</summary>
        public void Register(string id, uint[] voxels, int w, int h, int d, Vector3 corner, float voxelSize, float yawDegrees = 0f)
        {
            if (string.IsNullOrEmpty(id) || voxels == null || w <= 0 || h <= 0 || d <= 0 || voxelSize <= 0f) return;
            if ((long)w * h * d > voxels.Length) return;

            var v = new Volume { id = id, voxels = voxels, w = w, h = h, d = d, corner = corner, vs = voxelSize, yawDeg = yawDegrees };
            Vector3 size = new Vector3(w, h, d) * voxelSize;
            v.half = size * 0.5f;
            v.center = corner + v.half;
            if (yawDegrees == 0f)
            {
                v.wMin = corner;
                v.wMax = corner + size;
            }
            else
            {
                float rad = yawDegrees * Mathf.Deg2Rad;
                v.cosY = Mathf.Cos(rad);
                v.sinY = Mathf.Sin(rad);
                Vector3 mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3((i & 1) != 0 ? size.x : 0f, (i & 2) != 0 ? size.y : 0f, (i & 4) != 0 ? size.z : 0f);
                    Vector3 wc = v.center + RotY(c - v.half, v.cosY, v.sinY);
                    mn = Vector3.Min(mn, wc);
                    mx = Vector3.Max(mx, wc);
                }
                v.wMin = mn;
                v.wMax = mx;
            }

            if (index.TryGetValue(id, out int existing)) volumes[existing] = v;
            else { index[id] = volumes.Count; volumes.Add(v); }
            gridDirty = true;
        }

        /// <summary>
        /// Fetch a registered volume's voxel array + placement. The array is shared
        /// (asset cache / debris pool) — treat it as read-only; damage paths clone it
        /// (copy-on-write) then Register under the same id to swap the reference.
        /// </summary>
        public bool TryGetVolume(string id, out uint[] voxels, out int w, out int h, out int d,
            out Vector3 corner, out float voxelSize, out float yawDegrees)
        {
            if (id != null && index.TryGetValue(id, out int i))
            {
                Volume v = volumes[i];
                voxels = v.voxels; w = v.w; h = v.h; d = v.d;
                corner = v.corner; voxelSize = v.vs; yawDegrees = v.yawDeg;
                return true;
            }
            voxels = null; w = h = d = 0; corner = Vector3.zero; voxelSize = 0f; yawDegrees = 0f;
            return false;
        }

        public bool Unregister(string id)
        {
            if (id == null || !index.TryGetValue(id, out int i)) return false;
            int last = volumes.Count - 1;
            if (i != last) { volumes[i] = volumes[last]; index[volumes[i].id] = i; }
            volumes.RemoveAt(last);
            index.Remove(id);
            gridDirty = true;
            return true;
        }

        public void Clear() { volumes.Clear(); index.Clear(); gridDirty = true; }

        /// <summary>
        /// Nearest hit within maxDistance. dir must be a unit vector. Allocation-free.
        /// Broadphase: a 32 m XZ cell grid walked by a 2D DDA — only volumes overlapping the cells the
        /// segment crosses get tested (segment-AABB reject, slab test, then voxel DDA), in ray order,
        /// stopping at the first cell beyond the best hit. ~0.43 us per short ray at 800 buildings; ~190
        /// debris shards sweeping every frame ≈ 0.08 ms (was a flat scan: ~3.7 us/ray, ~0.7 ms per
        /// swarm frame — the selftest's 800-volume fuzz ray now measures the grid walk directly).
        /// </summary>
        public bool Raycast(Vector3 origin, Vector3 dir, float maxDistance, out VoxelRayHit hit)
        {
            hit = default;
            float best = maxDistance;
            bool found = false;

            // Coarse grid broadphase: 2D-DDA the segment through the XZ cell
            // grid and test only volumes overlapping visited cells, in ray order.
            // The first cell that starts beyond the best hit ends the walk — the
            // same early-out the flat loop never had (it scanned all N always).
            if (gridDirty) RebuildGrid();
            if (seenStamp == int.MaxValue) { seenStamp = 0; System.Array.Clear(seen, 0, seen.Length); }
            ++seenStamp;

            int cx = Mathf.FloorToInt(origin.x / gridCell);
            int cz = Mathf.FloorToInt(origin.z / gridCell);
            int stepX = dir.x >= 0f ? 1 : -1, stepZ = dir.z >= 0f ? 1 : -1;
            float tDeltaX = Mathf.Abs(dir.x) > 1e-9f ? Mathf.Abs(gridCell / dir.x) : float.MaxValue;
            float tDeltaZ = Mathf.Abs(dir.z) > 1e-9f ? Mathf.Abs(gridCell / dir.z) : float.MaxValue;
            float boundX = (stepX > 0 ? cx + 1 : cx) * gridCell;
            float boundZ = (stepZ > 0 ? cz + 1 : cz) * gridCell;
            float tMaxX = Mathf.Abs(dir.x) > 1e-9f ? Mathf.Max(0f, (boundX - origin.x) / dir.x) : float.MaxValue;
            float tMaxZ = Mathf.Abs(dir.z) > 1e-9f ? Mathf.Max(0f, (boundZ - origin.z) / dir.z) : float.MaxValue;

            for (float t = 0f; t <= best;)
            {
                if (grid.TryGetValue(cx * 65536 + cz, out var list))
                    for (int li = 0; li < list.Count; li++)
                    {
                        int i = list[li];
                        if (seen[i] == seenStamp) continue;
                        seen[i] = seenStamp;
                        TestVolume(i, origin, dir, ref best, ref found, ref hit);
                    }
                if (tMaxX < tMaxZ) { t = tMaxX; tMaxX += tDeltaX; cx += stepX; }
                else               { t = tMaxZ; tMaxZ += tDeltaZ; cz += stepZ; }
            }
            return found;
        }

        /// <summary>Segment-AABB reject, slab test, then the volume-space voxel DDA — one registered volume.</summary>
        private void TestVolume(int i, Vector3 origin, Vector3 dir, ref float best, ref bool found, ref VoxelRayHit hit)
        {
            Volume v = volumes[i];
            float maxDistance = best;
            const float pad = 1e-4f;
            Vector3 end = origin + dir * maxDistance;
            if (Mathf.Min(origin.x, end.x) - pad > v.wMax.x || Mathf.Max(origin.x, end.x) + pad < v.wMin.x) return;
            if (Mathf.Min(origin.y, end.y) - pad > v.wMax.y || Mathf.Max(origin.y, end.y) + pad < v.wMin.y) return;
            if (Mathf.Min(origin.z, end.z) - pad > v.wMax.z || Mathf.Max(origin.z, end.z) + pad < v.wMin.z) return;
            if (!RayHitsBox(origin, dir, v.wMin, v.wMax, best)) return;

            Vector3 o, dl;
            if (v.yawDeg == 0f) { o = origin - v.corner; dl = dir; }
            else { o = RotY(origin - v.center, v.cosY, -v.sinY) + v.half; dl = RotY(dir, v.cosY, -v.sinY); }

            // Cell space: scale by 1/vs; the ray stays unit-speed in world units so t is a world distance.
            double inv = 1.0 / v.vs;
            if (!VoxelDda.Raycast(v.voxels, v.w, v.h, v.d,
                    o.x * inv, o.y * inv, o.z * inv, dl.x * inv, dl.y * inv, dl.z * inv,
                    best, out DdaHit dh)) return;

            best = (float)dh.t;
            found = true;
            Vector3 n = new Vector3(dh.nx, dh.ny, dh.nz);
            if (v.yawDeg != 0f) n = RotY(n, v.cosY, v.sinY);
            hit.distance = best;
            hit.point = origin + dir * best;
            hit.normal = n;
            hit.cell = new Vector3Int(dh.x, dh.y, dh.z);
            hit.voxel = dh.voxel;
            hit.material = (int)(dh.voxel & VoxelDda.MaterialMask);
            hit.volumeId = v.id;
        }

        /// <summary>
        /// Point containment for debris-scale objects (≤~1 voxel/frame motion — not a
        /// substitute for the swept Raycast). Rides the same 32 m XZ broadphase as
        /// Raycast: one grid cell → few volume candidates → AABB reject → cell lookup.
        /// true = pos sits inside a volume's grid: volumeId + cellIdx address the cell,
        /// solid reports whether it currently holds material. When volumes overlap, a
        /// solid answer wins over an open one — debris may never step into material.
        /// </summary>
        public bool SamplePoint(Vector3 pos, out string volumeId, out int cellIdx, out bool solid)
        {
            volumeId = null; cellIdx = -1; solid = false;
            if (gridDirty) RebuildGrid();
            int key = Mathf.FloorToInt(pos.x / gridCell) * 65536 + Mathf.FloorToInt(pos.z / gridCell);
            if (!grid.TryGetValue(key, out var list)) return false;
            int openIdx = -1; string openId = null;
            for (int li = 0; li < list.Count; li++)
            {
                Volume v = volumes[list[li]];
                if (pos.x < v.wMin.x || pos.x > v.wMax.x ||
                    pos.y < v.wMin.y || pos.y > v.wMax.y ||
                    pos.z < v.wMin.z || pos.z > v.wMax.z) continue;
                Vector3 o = v.yawDeg == 0f ? pos - v.corner : RotY(pos - v.center, v.cosY, -v.sinY) + v.half;
                int cx = Mathf.FloorToInt(o.x / v.vs), cy = Mathf.FloorToInt(o.y / v.vs), cz = Mathf.FloorToInt(o.z / v.vs);
                if (cx < 0 || cy < 0 || cz < 0 || cx >= v.w || cy >= v.h || cz >= v.d) continue;
                int idx = cx + cy * v.w + cz * v.w * v.h;
                if ((v.voxels[idx] & VoxelDda.MaterialMask) != 0u)
                { volumeId = v.id; cellIdx = idx; solid = true; return true; }
                if (openIdx < 0) { openIdx = idx; openId = v.id; }
            }
            if (openIdx >= 0) { volumeId = openId; cellIdx = openIdx; return true; }
            return false;
        }

        /// <summary>(Re)build the XZ grid: every volume inserted into each cell its world AABB overlaps.</summary>
        private void RebuildGrid()
        {
            grid.Clear();
            if (seen == null || seen.Length < volumes.Count + 64)
                seen = new int[volumes.Count + 64];
            for (int i = 0; i < volumes.Count; i++)
            {
                Volume v = volumes[i];
                int x0 = Mathf.FloorToInt(v.wMin.x / gridCell), x1 = Mathf.FloorToInt(v.wMax.x / gridCell);
                int z0 = Mathf.FloorToInt(v.wMin.z / gridCell), z1 = Mathf.FloorToInt(v.wMax.z / gridCell);
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        int key = x * 65536 + z;
                        if (!grid.TryGetValue(key, out var list)) { list = new List<int>(4); grid[key] = list; }
                        list.Add(i);
                    }
            }
            gridDirty = false;
        }

        /// <summary>Rotate about +Y by the angle whose cos/sin are given (Unity convention: +angle turns +Z toward +X). Pass -sin for the inverse.</summary>
        private static Vector3 RotY(Vector3 p, float c, float s) => new Vector3(p.x * c + p.z * s, p.y, -p.x * s + p.z * c);

        /// <summary>Slab test against a world AABB (padded slightly so float rounding never rejects a grazing ray the DDA would accept).</summary>
        private static bool RayHitsBox(Vector3 o, Vector3 d, Vector3 mn, Vector3 mx, float tMax)
        {
            const float pad = 1e-4f;
            float t0 = 0f, t1 = tMax;
            for (int a = 0; a < 3; a++)
            {
                float oa = o[a], da = d[a], lo = mn[a] - pad, hi = mx[a] + pad;
                if (Mathf.Abs(da) < 1e-12f)
                {
                    if (oa < lo || oa > hi) return false;
                    continue;
                }
                float inv = 1f / da;
                float ta = (lo - oa) * inv, tb = (hi - oa) * inv;
                if (ta > tb) { float tmp = ta; ta = tb; tb = tmp; }
                if (ta > t0) t0 = ta;
                if (tb < t1) t1 = tb;
                if (t0 > t1) return false;
            }
            return true;
        }
    }
}
