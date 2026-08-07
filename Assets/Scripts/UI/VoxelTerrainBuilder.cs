using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Generates voxel data for ground tiles, sidewalks, and roads to match
    /// the existing mesh-based city layout 1:1. All terrain is rendered through
    /// the same GPU raymarch pipeline as buildings, giving unified depth
    /// compositing and shadows.
    ///
    /// Coordinate system matches CityMap3D:
    ///   - Blocks are positioned at (col - centerCol) * spacing on X, -(row - centerRow) * spacing on Z
    ///   - GroundTileSize = (BuildingVoxelWidth * buildingsPerRow * voxelSize) + sidewalkWidth * 2
    ///   - Spacing = GroundTileSize + roadWidth
    ///   - Roads run between blocks, centered at half-integer row/col positions
    ///
    /// Voxel packing: material ID in low 9 bits (matches StAssetReader / compute shader).
    /// Material IDs from the consolidated palette:
    ///   104 = Asphalt, 102 = Concrete (sidewalk), 105 = Cobblestone
    ///   101 = Stone (ground tile base)
    /// </summary>
    public static class VoxelTerrainBuilder
    {
        // Material IDs from consolidated palette (StAssetReader.cs)
        public const uint MAT_AIR = 0;
        public const uint MAT_ASPHALT = 104;
        public const uint MAT_SIDEWALK = 102;   // Concrete
        public const uint MAT_COBBLESTONE = 105;
        public const uint MAT_STONE = 101;

        /// <summary>
        /// One per-block terrain chunk for the split terrain system.
        /// </summary>
        public struct TerrainChunk
        {
            public string name;
            public uint[] data;
            public int w, h, d;
            public Vector3 worldOrigin;
        }

        /// <summary>
        /// Generate per-block terrain chunks. Each chunk covers one block's ground tile
        /// plus half the surrounding road on each side. This produces many small chunks
        /// instead of one massive one, so DDA rays exit each volume quickly.
        ///
        /// Roads are shared between adjacent chunks (each renders its half).
        /// The depth buffer composites correctly since terrain is flat at the same Y.
        ///
        /// anchorPositions: Returns world-space center of each block for building placement.
        /// </summary>
        public static List<TerrainChunk> GeneratePerBlockTerrain(
            int minRow, int maxRow, int minCol, int maxCol,
            float centerRow, float centerCol,
            float spacing,
            float groundTileSize,
            float roadWidth,
            float voxelSize,
            float sidewalkWidth,
            Vector3 mapRootOffset,
            out Dictionary<string, Vector3> anchorPositions)
        {
            var chunks = new List<TerrainChunk>();
            anchorPositions = new Dictionary<string, Vector3>();

            float halfRoad = roadWidth * 0.5f;
            float chunkSize = groundTileSize + roadWidth; // block tile + one full road (half on each side)
            int chunkVoxels = Mathf.Max(1, Mathf.CeilToInt(chunkSize / voxelSize));
            int h = 2; // 2 voxels thick
            float terrainTopY = mapRootOffset.y + h * voxelSize;
            const float eps = 0.001f; // tolerance for floating point boundary checks

            for (int row = minRow; row <= maxRow; row++)
            {
                for (int col = minCol; col <= maxCol; col++)
                {
                    float blockX = (col - centerCol) * spacing;
                    float blockZ = -(row - centerRow) * spacing;

                    // Chunk bounds: block center ± (groundTile/2 + halfRoad)
                    float cxMin = blockX - groundTileSize * 0.5f - halfRoad;
                    float cxMax = blockX + groundTileSize * 0.5f + halfRoad;
                    float czMin = blockZ - groundTileSize * 0.5f - halfRoad;
                    float czMax = blockZ + groundTileSize * 0.5f + halfRoad;

                    // Adjust chunk voxel dims to actual bounds (in case of rounding)
                    int w = Mathf.Max(1, Mathf.CeilToInt((cxMax - cxMin) / voxelSize));
                    int d = Mathf.Max(1, Mathf.CeilToInt((czMax - czMin) / voxelSize));

                    Vector3 worldOrigin = new Vector3(cxMin, 0f, czMin) + mapRootOffset;
                    Vector3 localOrigin = new Vector3(cxMin, 0f, czMin);

                    var data = new uint[w * h * d];

                    // Fill roads that pass through this chunk
                    // Use epsilon tolerance — road centers fall exactly on chunk boundaries
                    // and floating point precision can cause the check to fail.
                    // Horizontal road above (between row-1 and row)
                    float roadZAbove = -(row - 0.5f - centerRow) * spacing;
                    if (roadZAbove >= czMin - eps && roadZAbove <= czMax + eps)
                    {
                        FillRoadStrip(data, w, h, d, localOrigin, voxelSize,
                            cxMin, cxMax, roadZAbove - halfRoad, roadZAbove + halfRoad, true);
                    }
                    // Horizontal road below (between row and row+1)
                    float roadZBelow = -(row + 0.5f - centerRow) * spacing;
                    if (roadZBelow >= czMin - eps && roadZBelow <= czMax + eps)
                    {
                        FillRoadStrip(data, w, h, d, localOrigin, voxelSize,
                            cxMin, cxMax, roadZBelow - halfRoad, roadZBelow + halfRoad, true);
                    }
                    // Vertical road left (between col-1 and col)
                    float roadXLeft = (col - 0.5f - centerCol) * spacing;
                    if (roadXLeft >= cxMin - eps && roadXLeft <= cxMax + eps)
                    {
                        FillRoadStrip(data, w, h, d, localOrigin, voxelSize,
                            roadXLeft - halfRoad, roadXLeft + halfRoad, czMin, czMax, false);
                    }
                    // Vertical road right (between col and col+1)
                    float roadXRight = (col + 0.5f - centerCol) * spacing;
                    if (roadXRight >= cxMin - eps && roadXRight <= cxMax + eps)
                    {
                        FillRoadStrip(data, w, h, d, localOrigin, voxelSize,
                            roadXRight - halfRoad, roadXRight + halfRoad, czMin, czMax, false);
                    }

                    // Fill ground tile (sidewalk ring + stone interior)
                    FillGroundTile(data, w, h, d, localOrigin, voxelSize,
                        blockX - groundTileSize * 0.5f, blockX + groundTileSize * 0.5f,
                        blockZ - groundTileSize * 0.5f, blockZ + groundTileSize * 0.5f,
                        sidewalkWidth);

                    chunks.Add(new TerrainChunk
                    {
                        name = $"terrain_r{row}c{col}",
                        data = data,
                        w = w, h = h, d = d,
                        worldOrigin = worldOrigin
                    });

                    // Record anchor
                    Vector3 anchorWorld = new Vector3(blockX, terrainTopY, blockZ) + mapRootOffset;
                    anchorPositions[$"r{row}c{col}"] = anchorWorld;
                }
            }

            return chunks;
        }

        /// <summary>
        /// Generate a flat ground tile (sidewalk + building plot base) as voxel data.
        /// Matches the mesh-based ground tile: a flat slab of size GroundTileSize × GroundTileSize.
        ///
        /// The outer ring is sidewalk material, the inner area is building plot (stone/dark).
        /// Thickness: 1 voxel layer (flat on ground).
        /// </summary>
        public static uint[] GenerateGroundTile(
            int voxelsPerSide,     // GroundTileSize / voxelSize (rounded)
            int sidewalkVoxels,    // sidewalkWidth / voxelSize (rounded)
            out int w, out int h, out int d)
        {
            w = voxelsPerSide;
            h = 2; // 2 voxels thick for visibility
            d = voxelsPerSide;

            var data = new uint[w * h * d];

            for (int z = 0; z < d; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool isEdge = x < sidewalkVoxels || x >= w - sidewalkVoxels ||
                                  z < sidewalkVoxels || z >= d - sidewalkVoxels;

                    uint mat = isEdge ? MAT_SIDEWALK : MAT_STONE;

                    // Fill both layers
                    for (int y = 0; y < h; y++)
                    {
                        data[VoxelIndex(x, y, z, w, h, d)] = mat;
                    }
                }
            }

            return data;
        }

        /// <summary>
        /// Generate a road segment as voxel data.
        /// Roads are flat strips of asphalt, 1-2 voxels thick.
        /// </summary>
        public static uint[] GenerateRoadSegment(
            int lengthVoxels,   // length along the road's long axis
            int widthVoxels,    // roadWidth / voxelSize (rounded)
            out int w, out int h, out int d)
        {
            w = lengthVoxels;
            h = 2; // 2 voxels thick
            d = widthVoxels;

            var data = new uint[w * h * d];

            for (int z = 0; z < d; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Center stripe could be cobblestone for 1920s feel
                    bool isCenter = Mathf.Abs(z - d * 0.5f) < 1f;
                    uint mat = isCenter ? MAT_COBBLESTONE : MAT_ASPHALT;

                    for (int y = 0; y < h; y++)
                    {
                        data[VoxelIndex(x, y, z, w, h, d)] = mat;
                    }
                }
            }

            return data;
        }

        /// <summary>
        /// Generate a single large terrain chunk that contains ALL ground tiles and roads
        /// for the entire city. This is more efficient than many small chunks because:
        /// 1. One GPU dispatch instead of hundreds
        /// 2. Single depth buffer for the whole terrain
        /// 3. No gaps or seams between adjacent tiles
        ///
        /// World layout (top-down view, X=right, Z=down):
        ///   - Grid of blocks separated by roads
        ///   - Each block has a ground tile (sidewalk ring + building plot)
        ///   - Roads run between blocks (horizontal and vertical)
        ///   - Perimeter roads surround the outer blocks
        ///
        /// anchorPositions: Returns world-space center of each block for precise building placement.
        /// </summary>
        public static uint[] GenerateCityTerrain(
            int minRow, int maxRow, int minCol, int maxCol,
            float centerRow, float centerCol,
            float spacing,        // ComputedSpacing = GroundTileSize + roadWidth
            float groundTileSize, // GroundTileSize
            float roadWidth,
            float voxelSize,
            Vector3 mapRootOffset,  // World-space offset of the city root (e.g., (0,0,-100))
            out int w, out int h, out int d,
            out Vector3 worldOrigin,
            out Dictionary<string, Vector3> anchorPositions)
        {
            // Calculate world-space bounds of the entire city grid
            // Block (col,row) is at world position:
            //   X = (col - centerCol) * spacing
            //   Z = -(row - centerRow) * spacing
            // Each block has a ground tile of size groundTileSize centered at that position.
            // Roads run between blocks with width roadWidth.

            float minX = (minCol - centerCol) * spacing - groundTileSize * 0.5f - roadWidth;
            float maxX = (maxCol - centerCol) * spacing + groundTileSize * 0.5f + roadWidth;
            float minZ = -(maxRow - centerRow) * spacing - groundTileSize * 0.5f - roadWidth;
            float maxZ = -(minRow - centerRow) * spacing + groundTileSize * 0.5f + roadWidth;

            float cityWidth = maxX - minX;
            float cityDepth = maxZ - minZ;

            // Convert to voxel dimensions
            w = Mathf.Max(1, Mathf.CeilToInt(cityWidth / voxelSize));
            h = 2; // 2 voxels thick (flat terrain)
            d = Mathf.Max(1, Mathf.CeilToInt(cityDepth / voxelSize));

            // World origin = corner of the voxel volume, offset by mapRoot position
            // This is where the chunk sits in world space (used by LoadChunkFromData)
            worldOrigin = new Vector3(minX, 0f, minZ) + mapRootOffset;

            // Local origin (without mapRootOffset) for voxel index calculations
            // Road/ground positions are in city-local space, so we convert to voxel
            // indices relative to the local corner of the volume
            Vector3 localOrigin = new Vector3(minX, 0f, minZ);

            var data = new uint[w * h * d];

            // Fill the entire terrain with air first (already zero-initialized)

            // Build anchor positions for each block — exact world-space center
            // Buildings will snap to these positions
            anchorPositions = new Dictionary<string, Vector3>();
            float terrainTopY = mapRootOffset.y + h * voxelSize; // top of terrain surface

            // Generate road grid
            // Horizontal roads: between rows, at Z = -(minRow + i - 0.5 - centerRow) * spacing
            int ewCount = maxRow - minRow + 2;
            for (int i = 0; i < ewCount; i++)
            {
                float roadCenterZ = -(minRow + i - 0.5f - centerRow) * spacing;
                FillRoadStrip(data, w, h, d, localOrigin, voxelSize,
                    minX, maxX, roadCenterZ - roadWidth * 0.5f, roadCenterZ + roadWidth * 0.5f,
                    isHorizontal: true);
            }

            // Vertical roads: between cols, at X = (minCol + i - 0.5 - centerCol) * spacing
            int nsCount = maxCol - minCol + 2;
            for (int i = 0; i < nsCount; i++)
            {
                float roadCenterX = (minCol + i - 0.5f - centerCol) * spacing;
                FillRoadStrip(data, w, h, d, localOrigin, voxelSize,
                    roadCenterX - roadWidth * 0.5f, roadCenterX + roadWidth * 0.5f,
                    minZ, maxZ,
                    isHorizontal: false);
            }

            // Generate ground tiles for each block + record anchor positions
            for (int row = minRow; row <= maxRow; row++)
            {
                for (int col = minCol; col <= maxCol; col++)
                {
                    float blockX = (col - centerCol) * spacing;
                    float blockZ = -(row - centerRow) * spacing;

                    float tileMinX = blockX - groundTileSize * 0.5f;
                    float tileMaxX = blockX + groundTileSize * 0.5f;
                    float tileMinZ = blockZ - groundTileSize * 0.5f;
                    float tileMaxZ = blockZ + groundTileSize * 0.5f;

                    FillGroundTile(data, w, h, d, localOrigin, voxelSize,
                        tileMinX, tileMaxX, tileMinZ, tileMaxZ,
                        sidewalkWidth: 1.0f); // 1 world unit sidewalk ring

                    // Record anchor: world-space center of this block's ground tile
                    // Buildings sit at terrainTopY (on top of the 2-voxel-thick terrain)
                    Vector3 anchorWorld = new Vector3(blockX, terrainTopY, blockZ) + mapRootOffset;
                    string anchorKey = $"r{row}c{col}";
                    anchorPositions[anchorKey] = anchorWorld;
                }
            }

            return data;
        }

        /// <summary>
        /// Fill a rectangular region of the terrain with a road material.
        /// </summary>
        private static void FillRoadStrip(
            uint[] data, int w, int h, int d, Vector3 origin, float voxelSize,
            float minX, float maxX, float minZ, float maxZ, bool isHorizontal)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt((minX - origin.x) / voxelSize), 0, w - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((maxX - origin.x) / voxelSize), 0, w - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((minZ - origin.z) / voxelSize), 0, d - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - origin.z) / voxelSize), 0, d - 1);

            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    // Center stripe = cobblestone, edges = asphalt
                    bool isCenter;
                    if (isHorizontal)
                        isCenter = Mathf.Abs(z - (z0 + z1) * 0.5f) < 1f;
                    else
                        isCenter = Mathf.Abs(x - (x0 + x1) * 0.5f) < 1f;

                    uint mat = isCenter ? MAT_COBBLESTONE : MAT_ASPHALT;
                    for (int y = 0; y < h; y++)
                    {
                        data[VoxelIndex(x, y, z, w, h, d)] = mat;
                    }
                }
            }
        }

        /// <summary>
        /// Fill a rectangular region with sidewalk (edges) and stone (interior).
        /// </summary>
        private static void FillGroundTile(
            uint[] data, int w, int h, int d, Vector3 origin, float voxelSize,
            float minX, float maxX, float minZ, float maxZ, float sidewalkWidth)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt((minX - origin.x) / voxelSize), 0, w - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((maxX - origin.x) / voxelSize), 0, w - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((minZ - origin.z) / voxelSize), 0, d - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - origin.z) / voxelSize), 0, d - 1);

            int sw = Mathf.Max(1, Mathf.RoundToInt(sidewalkWidth / voxelSize));

            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    bool isEdge = x < x0 + sw || x > x1 - sw || z < z0 + sw || z > z1 - sw;
                    uint mat = isEdge ? MAT_SIDEWALK : MAT_STONE;

                    for (int y = 0; y < h; y++)
                    {
                        // Only write if not already a road (roads take priority)
                        int idx = VoxelIndex(x, y, z, w, h, d);
                        if (data[idx] == MAT_AIR || data[idx] == MAT_SIDEWALK || data[idx] == MAT_STONE)
                            data[idx] = mat;
                    }
                }
            }
        }

        /// <summary>
        /// 3D voxel index matching the compute shader's indexing scheme.
        /// X-major: x varies fastest, then y, then z.
        /// </summary>
        private static int VoxelIndex(int x, int y, int z, int w, int h, int d)
        {
            return x + y * w + z * w * h;
        }
    }
}
