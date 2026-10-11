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
        }

        public bool Unregister(string id)
        {
            if (id == null || !index.TryGetValue(id, out int i)) return false;
            int last = volumes.Count - 1;
            if (i != last) { volumes[i] = volumes[last]; index[volumes[i].id] = i; }
            volumes.RemoveAt(last);
            index.Remove(id);
            return true;
        }

        public void Clear() { volumes.Clear(); index.Clear(); }

        /// <summary>
        /// Nearest hit within maxDistance. dir must be a unit vector. Allocation-free.
        /// A linear broadphase over the registered volumes is plenty for a handful of projectiles per
        /// frame (~800 buildings); put a coarse grid in front of it before mass fire / per-frame LOS.
        /// </summary>
        public bool Raycast(Vector3 origin, Vector3 dir, float maxDistance, out VoxelRayHit hit)
        {
            hit = default;
            float best = maxDistance;
            bool found = false;
            for (int i = 0; i < volumes.Count; i++)
            {
                Volume v = volumes[i];
                if (!RayHitsBox(origin, dir, v.wMin, v.wMax, best)) continue;

                Vector3 o, dl;
                if (v.yawDeg == 0f) { o = origin - v.corner; dl = dir; }
                else { o = RotY(origin - v.center, v.cosY, -v.sinY) + v.half; dl = RotY(dir, v.cosY, -v.sinY); }

                // Cell space: scale by 1/vs; the ray stays unit-speed in world units so t is a world distance.
                double inv = 1.0 / v.vs;
                if (!VoxelDda.Raycast(v.voxels, v.w, v.h, v.d,
                        o.x * inv, o.y * inv, o.z * inv, dl.x * inv, dl.y * inv, dl.z * inv,
                        best, out DdaHit dh)) continue;

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
            return found;
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
