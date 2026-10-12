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
