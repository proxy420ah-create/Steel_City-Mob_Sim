using System;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Procedural debris scatterer for empty land plots.
    /// Takes a base voxel grid (from empty_land.stasset) and adds random
    /// debris — rubble piles, trash, weeds, broken crates, old lumber —
    /// using a deterministic seed derived from the block's grid coordinates.
    /// Each empty lot looks different but is reproducible.
    /// </summary>
    public static class ProceduralDebrisScatterer
    {
        /// <summary>Toggle to disable debris scatter on empty plots for debugging.</summary>
        public static bool Enabled = true;
        // Material IDs (must match StAssetReader palette)
        private const ushort AIR = 0;
        private const ushort STONE = 101;
        private const ushort CONCRETE = 102;
        private const ushort ASPHALT = 104;
        private const ushort COBBLESTONE = 105;
        private const ushort DARK_WOOD = 106;
        private const ushort LIGHT_WOOD = 107;
        private const ushort WEATHERED_WOOD = 108;
        private const ushort AGED_METAL = 110;
        private const ushort TAR = 118;
        private const ushort PAINTED_RED = 120;
        private const ushort PAINTED_GREEN = 121;
        private const ushort PAINTED_BROWN = 122;

        // Debris type weights
        private static readonly (ushort mat, float weight, int maxHeight)[]
            DEBRIS_TYPES = new[]
            {
                (CONCRETE,     0.25f, 3),  // concrete chunks
                (WEATHERED_WOOD, 0.20f, 4), // old lumber / boards
                (STONE,        0.15f, 2),   // rubble stones
                (AGED_METAL,   0.10f, 2),   // scrap metal
                (DARK_WOOD,    0.10f, 3),   // broken crates
                (COBBLESTONE,  0.08f, 2),   // cobblestone fragments
                (TAR,          0.05f, 1),   // tar patches
                (PAINTED_BROWN, 0.04f, 2),  // painted wood scraps
                (PAINTED_RED,  0.02f, 2),   // red painted fragments
                (PAINTED_GREEN, 0.01f, 2),  // green painted fragments
            };

        /// <summary>
        /// Scatter procedural debris on an empty land voxel grid.
        /// Modifies the grid in-place. Deterministic per (row, col, subIndex).
        /// </summary>
        /// <param name="voxels">Packed uint[] voxel data (X-major: x + y*w + z*w*h)</param>
        /// <param name="w">Grid width</param>
        /// <param name="h">Grid height</param>
        /// <param name="d">Grid depth</param>
        /// <param name="row">Block row (for seeding)</param>
        /// <param name="col">Block column (for seeding)</param>
        /// <param name="subIndex">Building sub-index within block (for seeding)</param>
        /// <param name="density">Debris density 0..1 (default 0.15 = ~15% of surface covered)</param>
        public static void Scatter(uint[] voxels, int w, int h, int d,
            int row, int col, int subIndex, float density = 0.03f)
        {
            if (!Enabled) return;

            // Deterministic seed from grid position
            int seed = (row * 73856093) ^ (col * 19349663) ^ (subIndex * 83492791);
            seed = seed & 0x7FFFFFFF; // ensure positive
            var rng = new System.Random(seed);

            // Find the ground surface Y (top of the base terrain in this grid)
            int groundY = FindGroundSurface(voxels, w, h, d);

            // Scatter debris clusters
            int totalSurface = w * d;
            int targetCount = Mathf.RoundToInt(totalSurface * density);

            int placed = 0;
            int attempts = 0;
            int maxAttempts = targetCount * 3;

            while (placed < targetCount && attempts < maxAttempts)
            {
                attempts++;

                // Pick a random surface position
                int x = rng.Next(2, w - 2);
                int z = rng.Next(2, d - 2);

                // Pick a debris type
                ushort mat = PickDebrisType(rng);
                int maxH = GetMaxHeight(mat);

                // Random cluster size (1-2 voxels footprint — sparse scatter)
                int clusterSize = rng.Next(1, 3);
                int clusterHeight = rng.Next(1, maxH + 1);

                // Place the cluster
                bool placedAny = false;
                for (int cx = 0; cx < clusterSize; cx++)
                {
                    for (int cz = 0; cz < clusterSize; cz++)
                    {
                        int px = x + cx - clusterSize / 2;
                        int pz = z + cz - clusterSize / 2;
                        if (px < 1 || px >= w - 1 || pz < 1 || pz >= d - 1)
                            continue;

                        // Random height variation within cluster
                        int localH = Mathf.Max(1, clusterHeight - rng.Next(0, 2));
                        for (int y = 0; y < localH; y++)
                        {
                            int vy = groundY + 1 + y;
                            if (vy >= h) break;
                            int idx = px + vy * w + pz * w * h;
                            if (voxels[idx] == AIR)
                            {
                                voxels[idx] = mat;
                                placedAny = true;
                            }
                        }
                    }
                }

                if (placedAny) placed++;
            }

            // Add a few weeds/grass patches (very sparse, low height)
            int weedCount = rng.Next(1, 4);
            for (int i = 0; i < weedCount; i++)
            {
                int x = rng.Next(2, w - 2);
                int z = rng.Next(2, d - 2);
                int idx = x + (groundY + 1) * w + z * w * h;
                if (idx < voxels.Length && voxels[idx] == AIR)
                    voxels[idx] = PAINTED_GREEN; // weeds (reuse green)
            }

            // Occasionally add a broken wooden post or fence remnant
            if (rng.NextDouble() < 0.25)
            {
                int fenceLen = rng.Next(3, 8);
                int fx = rng.Next(2, w - fenceLen - 2);
                int fz = rng.Next(2, d - 2);
                int fenceH = rng.Next(2, 5);
                for (int i = 0; i < fenceLen; i++)
                {
                    int px = fx + i;
                    if (px >= w - 1) break;
                    for (int y = 0; y < fenceH; y++)
                    {
                        int vy = groundY + 1 + y;
                        if (vy >= h) break;
                        int idx = px + vy * w + fz * w * h;
                        if (voxels[idx] == AIR)
                            voxels[idx] = WEATHERED_WOOD;
                    }
                }
            }

            // Occasionally add a small rubble mound (3-5 voxels tall)
            if (rng.NextDouble() < 0.15)
            {
                int moundX = rng.Next(w / 4, 3 * w / 4);
                int moundZ = rng.Next(d / 4, 3 * d / 4);
                int moundRadius = rng.Next(2, 5);
                int moundH = rng.Next(2, 4);
                ushort moundMat = rng.NextDouble() < 0.5 ? CONCRETE : STONE;

                for (int dx = -moundRadius; dx <= moundRadius; dx++)
                {
                    for (int dz = -moundRadius; dz <= moundRadius; dz++)
                    {
                        float dist = Mathf.Sqrt(dx * dx + dz * dz);
                        if (dist > moundRadius) continue;
                        int px = moundX + dx;
                        int pz = moundZ + dz;
                        if (px < 1 || px >= w - 1 || pz < 1 || pz >= d - 1) continue;

                        // Cone-shaped mound: taller in center
                        int localH = Mathf.RoundToInt(moundH * (1f - dist / moundRadius));
                        for (int y = 0; y < localH; y++)
                        {
                            int vy = groundY + 1 + y;
                            if (vy >= h) break;
                            int idx = px + vy * w + pz * w * h;
                            if (voxels[idx] == AIR)
                                voxels[idx] = moundMat;
                        }
                    }
                }
            }

        }

        /// <summary>
        /// Find the top surface of the base terrain (highest Y with non-air voxels
        /// at the edges, which are likely ground).
        /// </summary>
        private static int FindGroundSurface(uint[] voxels, int w, int h, int d)
        {
            // Scan from bottom up at a few sample positions
            for (int y = 0; y < h; y++)
            {
                // Check center area
                int idx = (w / 2) + y * w + (d / 2) * w * h;
                if (idx < voxels.Length && voxels[idx] != AIR)
                    return y;
            }
            return 0; // fallback: ground at Y=0
        }

        private static ushort PickDebrisType(System.Random rng)
        {
            float r = (float)rng.NextDouble();
            float cumulative = 0f;
            foreach (var (mat, weight, _) in DEBRIS_TYPES)
            {
                cumulative += weight;
                if (r < cumulative) return mat;
            }
            return CONCRETE;
        }

        private static int GetMaxHeight(ushort mat)
        {
            foreach (var (m, _, maxH) in DEBRIS_TYPES)
            {
                if (m == mat) return maxH;
            }
            return 2;
        }
    }
}
