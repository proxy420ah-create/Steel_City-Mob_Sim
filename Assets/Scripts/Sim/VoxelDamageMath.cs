using System;
using System.Collections.Generic;

namespace SteelCity.Sim
{
    /// <summary>
    /// Pure-C# voxel damage math (no UnityEngine types) so the offline selftest
    /// (Tools/dda_selftest) compiles the exact code the runtime runs.
    ///
    /// P0 puncture per GLASS_SHATTER_MECHANIC.md: the impact cell is removed and the
    /// ring-1 neighbors in the plane perpendicular to the shot direction are
    /// seeded-removed at ring1RemoveFraction; survivors swap to the cracked material.
    /// Glass cells only — non-glass (the mold's metal core, brick sills) is never
    /// touched, which makes indestructible cores free by construction.
    /// </summary>
    public static class VoxelDamageMath
    {
        public const uint MaterialMask = 0x1FFu;   // VoxelDda.MaterialMask — duplicated so this file stays dependency-free

        /// <summary>Palette ids that are see-through glass (StAssetReader palette).</summary>
        public static bool IsGlassMaterial(int mat) => mat == 112 || mat == 113 || mat == 114 || mat == 134 || mat == 147;

        /// <summary>
        /// Carve a bullet puncture into data at (hx,hy,hz). Fills dirtyIndices with every
        /// written flat index (x + y·w + z·w·h). Returns the removed-cell count;
        /// cracked = survivor cells swapped to crackedMaterial.
        /// </summary>
        public static int Puncture(uint[] data, int w, int h, int d,
            int hx, int hy, int hz, double dirX, double dirY, double dirZ,
            uint seed, float ring1RemoveFraction, int crackedMaterial,
            List<int> dirtyIndices, out int cracked)
        {
            int removed = TouchGlass(data, w, h, d, hx, hy, hz, seed, force: true,
                ring1RemoveFraction, crackedMaterial, dirtyIndices) ? 1 : 0;

            // Ring plane ⊥ shot: drop the shot's dominant axis, jitter the other two.
            double ax = dirX < 0 ? -dirX : dirX, ay = dirY < 0 ? -dirY : dirY, az = dirZ < 0 ? -dirZ : dirZ;
            int drop = ax >= ay ? (ax >= az ? 0 : 2) : (ay >= az ? 1 : 2);
            for (int a = -1; a <= 1; a++)
            for (int b = -1; b <= 1; b++)
            {
                if (a == 0 && b == 0) continue;
                int x = hx, y = hy, z = hz;
                if (drop == 0) { y += a; z += b; }
                else if (drop == 1) { x += a; z += b; }
                else { x += a; y += b; }
                if (TouchGlass(data, w, h, d, x, y, z, seed, force: false,
                        ring1RemoveFraction, crackedMaterial, dirtyIndices)) removed++;
            }
            cracked = dirtyIndices.Count - removed;
            return removed;
        }

        /// <summary>
        /// Glass cell → AIR (returns true) when forced or the seeded roll passes;
        /// otherwise the cell survives and swaps to the cracked material. Non-glass,
        /// air, and out-of-bounds cells are untouched. Per-cell seed = shot seed XOR
        /// cell index — same shot reproduces the identical scar.
        /// </summary>
        private static bool TouchGlass(uint[] data, int w, int h, int d, int x, int y, int z,
            uint seed, bool force, float ring1RemoveFraction, int crackedMaterial, List<int> dirty)
        {
            if (x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= d) return false;
            int idx = x + y * w + z * w * h;
            uint v = data[idx];
            if (!IsGlassMaterial((int)(v & MaterialMask))) return false;

            uint cellSeed = seed ^ (uint)idx * 0x9E3779B9u;
            if (force || Hash01(cellSeed) < ring1RemoveFraction)
            {
                data[idx] = 0u;      // punctured — AIR
                dirty.Add(idx);
                return true;
            }
            data[idx] = (v & ~MaterialMask) | (uint)crackedMaterial;   // wounded — cracked halo
            dirty.Add(idx);
            return false;
        }

        /// <summary>
        /// How strongly a material resists blast removal (0 = dies to any blast,
        /// 1 = nearly indestructible). Bullet puncture stays glass-only — this table
        /// is the bomb's world: glass shatters, masonry craters, metal holds.
        /// </summary>
        public static float MaterialResistance(uint mat)
        {
            switch ((int)mat)
            {
                case 112: case 113: case 114: case 134: case 147:          // glass
                    return 0.05f;
                case 115: case 116: case 117: case 124:                   // neon, lamp glow
                case 125: case 126: case 127: case 128:                   // flesh, fabric, hair
                case 137:                                                // water
                    return 0.10f;
                case 106: case 107: case 108:                            // wood
                case 119:                                                // terracotta
                case 120: case 121: case 122: case 129:                  // painted wood
                case 143:                                                // tire rubber
                    return 0.25f;
                case 103: return 0.35f;                                  // stucco/plaster
                case 100: case 105: case 118: return 0.50f;              // brick, cobblestone, tar roof
                case 101: case 102: case 104: return 0.60f;              // stone, concrete, asphalt
                case 109: case 110: case 142: return 0.80f;              // iron, aged metal, chrome
                case 111: case 140: case 141: return 0.90f;              // painted metal, vehicle body
                default: return 0.5f;                                    // unlisted = today's behavior
            }
        }

        /// <summary>
        /// Spherical blast removal. Every non-air cell within `radius` cells of
        /// (cx,cy,cz) rolls removal at p = power·(1 − dist/radius)·(1 − resistance)·transmit:
        /// full strength at the center, decaying to zero at the shell, tempered by
        /// material — and attenuated by whatever stands between the bomb and the cell.
        /// With occlusion on, a DDA march from the blast center multiplies transmit
        /// by (1 − resistance) per solid cell crossed: open air is free, a pane
        /// barely shields, a brick wall roughly halves the blast per cell, a metal
        /// beam nearly blocks it. The march reads the PRE-BLAST grid (two-phase:
        /// decisions first, writes after), so a wall removed this blast still
        /// shields what was behind it — blast one weakens the wall, blast two
        /// punches through. Surviving glass swaps to crackedMaterial; surviving
        /// non-glass stays untouched. Deterministic: same seed + same grid
        /// reproduces the identical crater.
        /// </summary>
        public static int Blast(uint[] data, int w, int h, int d,
            float cx, float cy, float cz, float radius, float power,
            uint seed, int crackedMaterial,
            List<int> dirty, List<int> removedIdx, List<uint> removedMats, out int cracked,
            bool occlusion = true)
        {
            removedIdx.Clear(); removedMats.Clear();
            var crackedIdx = new List<int>();
            int removed = 0; cracked = 0;
            int x0 = Math.Max(0, (int)Math.Floor(cx - radius)), x1 = Math.Min(w - 1, (int)Math.Ceiling(cx + radius));
            int y0 = Math.Max(0, (int)Math.Floor(cy - radius)), y1 = Math.Min(h - 1, (int)Math.Ceiling(cy + radius));
            int z0 = Math.Max(0, (int)Math.Floor(cz - radius)), z1 = Math.Min(d - 1, (int)Math.Ceiling(cz + radius));
            int wh = w * h;
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy, dz = z + 0.5f - cz;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (dist > radius) continue;
                int idx = x + y * w + z * wh;
                uint v = data[idx];
                uint mat = v & MaterialMask;
                if (mat == 0) continue;
                float p = power * (1f - dist / radius) * (1f - MaterialResistance(mat));
                float roll = Hash01(seed ^ (uint)idx * 0x9E3779B9u);
                if (roll < p)
                {
                    // would-be removal — only these cells pay for the occlusion march
                    float t = occlusion
                        ? MarchTransmit(data, w, h, d, cx, cy, cz, x + 0.5f, y + 0.5f, z + 0.5f)
                        : 1f;
                    if (roll < p * t)
                    {
                        removedIdx.Add(idx);
                        removedMats.Add(mat);
                        removed++;
                        continue;
                    }
                }
                if (IsGlassMaterial((int)mat))
                {
                    crackedIdx.Add(idx);
                    cracked++;
                }
            }
            // phase 2 — apply every write at once (the march sees the intact grid)
            for (int i = 0; i < removedIdx.Count; i++)
            {
                data[removedIdx[i]] = 0u;
                dirty.Add(removedIdx[i]);
            }
            foreach (int idx in crackedIdx)
            {
                data[idx] = (data[idx] & ~MaterialMask) | (uint)crackedMaterial;
                dirty.Add(idx);
            }
            return removed;
        }

        /// <summary>
        /// Amanatides–Woo march from the blast center to a target cell, attenuating
        /// Beer–Lambert style: transmit *= (1 − resistance)^(path length inside the
        /// cell, in cell units). An axis-aligned crossing still costs exactly one
        /// cell of material (existing wall tuning unchanged — brick = ×0.5 per
        /// cell), but oblique rays pay for the distance they actually travel
        /// instead of a full cell per face clipped, so equal-shielding contours
        /// are spheres rather than taxicab diamonds. The target cell itself is
        /// excluded (its own resistance already factors the roll). Early-outs at
        /// 2% transmission — deeper shielding is darkness.
        /// </summary>
        public static float MarchTransmit(uint[] data, int w, int h, int d,
            float cx, float cy, float cz, float tx, float ty, float tz)
        {
            int x = (int)Math.Floor(cx), y = (int)Math.Floor(cy), z = (int)Math.Floor(cz);
            float dx = tx - cx, dy = ty - cy, dz = tz - cz;
            float len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            int sx = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int sy = dy > 0 ? 1 : (dy < 0 ? -1 : 0);
            int sz = dz > 0 ? 1 : (dz < 0 ? -1 : 0);
            float tdx = sx != 0 ? Math.Abs(1f / dx) : float.MaxValue;
            float tdy = sy != 0 ? Math.Abs(1f / dy) : float.MaxValue;
            float tdz = sz != 0 ? Math.Abs(1f / dz) : float.MaxValue;
            float tmx = BoundT(cx, dx, sx), tmy = BoundT(cy, dy, sy), tmz = BoundT(cz, dz, sz);
            float transmit = 1f;
            int wh = w * h, guard = w + h + d + 8;
            while (guard-- > 0)
            {
                float tEnter = Math.Min(tmx, Math.Min(tmy, tmz));   // this cell's exit boundary
                if (tEnter >= 1f) break;                            // the target lies inside this cell
                if (tmx <= tmy && tmx <= tmz) { x += sx; tmx += tdx; }
                else if (tmy <= tmz)          { y += sy; tmy += tdy; }
                else                          { z += sz; tmz += tdz; }
                if (x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= d) break;
                float tNext = Math.Min(tmx, Math.Min(tmy, tmz));    // the entered cell's exit
                if (tNext >= 1f) break;                             // entered the target cell — excluded
                uint m = data[x + y * w + z * wh] & MaterialMask;
                if (m != 0)
                {
                    transmit *= (float)Math.Pow(1f - MaterialResistance(m), (tNext - tEnter) * len);
                    if (transmit < 0.02f) break;
                }
            }
            return transmit;
        }

        /// <summary>Parametric t (0..1 along the march) of the first cell-boundary crossing on an axis.</summary>
        private static float BoundT(float c, float dir, int step)
        {
            if (step == 0) return float.MaxValue;
            float next = step > 0 ? (float)Math.Floor(c) + 1f : (float)Math.Ceiling(c) - 1f;
            float t = (next - c) / dir;
            return t < 0f ? 0f : t;
        }

        /// <summary>
        /// Structural connectivity: BFS-flood from every solid cell in the y==0 anchor
        /// layer. A cell survives iff the flood reaches it — structural (non-glass)
        /// cells anchor ALL solid neighbors, glass anchors only glass (a pane hangs
        /// off its frame, but a brick can't rest on a pane). Unreached solid cells
        /// are detached: grouped into 6-connected islands (all materials connect for
        /// grouping — a pane embedded in a falling wall rides with it) and appended
        /// to detachedIdx/detachedMats, with islandEnds marking each island's
        /// exclusive end index. `visited` is an optional w·h·d scratch
        /// (bit1 = anchored, bit2 = islanded). Returns the detached cell count.
        /// </summary>
        public static int FindDetached(uint[] data, int w, int h, int d,
            List<int> detachedIdx, List<uint> detachedMats, List<int> islandEnds,
            byte[] visited = null)
        {
            detachedIdx.Clear(); detachedMats.Clear(); islandEnds.Clear();
            int n = w * h * d;
            if (visited == null || visited.Length < n) visited = new byte[n];
            else Array.Clear(visited, 0, n);
            var queue = new Queue<int>();
            int wh = w * h;

            // anchor pass — seed every solid cell on the ground layer
            for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
            {
                int idx = x + z * wh;
                if ((data[idx] & MaterialMask) != 0) { visited[idx] = 1; queue.Enqueue(idx); }
            }
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int cz = c / wh, crem = c - cz * wh, cy = crem / w, cx = crem - cy * w;
                bool cStruct = !IsGlassMaterial((int)(data[c] & MaterialMask));
                for (int a = 0; a < 6; a++)
                {
                    int nx = cx, ny = cy, nz = cz;
                    if (a == 0) nx++; else if (a == 1) nx--;
                    else if (a == 2) ny++; else if (a == 3) ny--;
                    else if (a == 4) nz++; else nz--;
                    if (nx < 0 || ny < 0 || nz < 0 || nx >= w || ny >= h || nz >= d) continue;
                    int ni = nx + ny * w + nz * wh;
                    if (visited[ni] != 0) continue;
                    uint nm = data[ni] & MaterialMask;
                    if (nm == 0) continue;
                    if (!cStruct && !IsGlassMaterial((int)nm)) continue;   // glass can't anchor structure
                    visited[ni] = 1;
                    queue.Enqueue(ni);
                }
            }

            // island pass — group everything the anchor flood never reached
            for (int idx = 0; idx < n; idx++)
            {
                if (visited[idx] != 0 || (data[idx] & MaterialMask) == 0) continue;
                visited[idx] = 2; queue.Enqueue(idx);
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    detachedIdx.Add(c); detachedMats.Add(data[c] & MaterialMask);
                    int cz = c / wh, crem = c - cz * wh, cy = crem / w, cx = crem - cy * w;
                    for (int a = 0; a < 6; a++)
                    {
                        int nx = cx, ny = cy, nz = cz;
                        if (a == 0) nx++; else if (a == 1) nx--;
                        else if (a == 2) ny++; else if (a == 3) ny--;
                        else if (a == 4) nz++; else nz--;
                        if (nx < 0 || ny < 0 || nz < 0 || nx >= w || ny >= h || nz >= d) continue;
                        int ni = nx + ny * w + nz * wh;
                        if (visited[ni] != 0 || (data[ni] & MaterialMask) == 0) continue;
                        visited[ni] = 2;
                        queue.Enqueue(ni);
                    }
                }
                islandEnds.Add(detachedIdx.Count);
            }
            return detachedIdx.Count;
        }

        /// <summary>
        /// A pre-computed explosion: every cell the blast will write, grouped by
        /// wavefront pass, with the detached islands appended as the final front.
        /// The plan IS the delta — applying writeIdx/writeVal to the source grid
        /// in order reproduces the simulated end state exactly, so detonation
        /// playback is pure scheduling (writes per frame, no hitches).
        /// </summary>
        public sealed class BlastPlan
        {
            public int passes;
            public readonly List<int> writeIdx = new();     // every written cell, grouped by pass
            public readonly List<uint> writeVal = new();    // final packed value (0 = AIR)
            public readonly List<uint> writeMat = new();    // ORIGINAL material (shard tint; AIR entries only)
            public readonly List<int> passEnds = new();     // writeIdx end index per pass
            public readonly List<int> islandEnds = new();   // absolute end idx per detached island
            public int detachedStart;                       // writeIdx[detachedStart..] = collapse writes
            public int totalRemoved;
        }

        /// <summary>
        /// Multi-pass expanding shock front on a SCRATCH clone — pass i detonates at
        /// radius·(i+1)/K so interior cells re-roll every pass (near-certain kill)
        /// while fringe cells see the front once. Because each pass mutates the
        /// scratch, occlusion frees as inner structure dies: the wave accelerates
        /// through cleared space. Connectivity (optional) runs on the final state —
        /// islands append as the last wavefront. Deterministic per baseSeed.
        /// </summary>
        public static BlastPlan SimulateBlast(uint[] scratch, int w, int h, int d,
            float cx, float cy, float cz, float radius, float power,
            uint baseSeed, int passes, int crackedMaterial,
            bool occlusion, bool dropUnsupported, byte[] visitedScratch = null)
        {
            var plan = new BlastPlan { passes = passes };
            var dirty = new List<int>(); var ri = new List<int>(); var rm = new List<uint>();
            var matOf = new Dictionary<int, uint>();
            for (int i = 0; i < passes; i++)
            {
                float passR = radius * (i + 1f) / passes;
                dirty.Clear(); ri.Clear(); rm.Clear();
                Blast(scratch, w, h, d, cx, cy, cz, passR, power, baseSeed + (uint)i,
                    crackedMaterial, dirty, ri, rm, out _, occlusion);
                matOf.Clear();
                for (int j = 0; j < ri.Count; j++) matOf[ri[j]] = rm[j];
                var removedSet = new HashSet<int>(ri);
                for (int j = 0; j < dirty.Count; j++)
                {
                    int idx = dirty[j];
                    plan.writeIdx.Add(idx);
                    if (removedSet.Contains(idx))
                    {
                        plan.writeVal.Add(0u);
                        plan.writeMat.Add(matOf[idx]);
                        plan.totalRemoved++;
                    }
                    else
                    {
                        plan.writeVal.Add(scratch[idx]);   // Blast already wrote the cracked value
                        plan.writeMat.Add(0u);
                    }
                }
                plan.passEnds.Add(plan.writeIdx.Count);
            }
            plan.detachedStart = plan.writeIdx.Count;
            if (dropUnsupported)
            {
                var detI = new List<int>(); var detM = new List<uint>(); var detE = new List<int>();
                FindDetached(scratch, w, h, d, detI, detM, detE, visitedScratch);
                for (int j = 0; j < detI.Count; j++)
                {
                    plan.writeIdx.Add(detI[j]);
                    plan.writeVal.Add(0u);
                    plan.writeMat.Add(detM[j]);
                }
                foreach (int e in detE) plan.islandEnds.Add(plan.detachedStart + e);
                plan.totalRemoved += detI.Count;
            }
            return plan;
        }

        /// <summary>lowbias32 integer hash → [0,1). Same constants as ProjectileSystem.Hash01.</summary>
        public static float Hash01(uint x)
        {
            unchecked
            {
                x ^= x >> 16; x *= 0x7feb352dU;
                x ^= x >> 15; x *= 0x846ca68bU;
                x ^= x >> 16;
                return (x & 0x00FFFFFFu) / 16777216f;
            }
        }
    }
}
