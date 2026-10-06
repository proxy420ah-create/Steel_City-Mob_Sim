using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        public const uint MAT_WATER = 137;      // Water (river channel fill)

        // Terrain vocabulary — mirrors city_editor blockTypes / replica tile types.
        // blocks[].terrain absent = "land" (backward compatible).
        public static string NormalizeTerrain(string t)
        {
            switch ((t ?? "").Trim().ToLowerInvariant())
            {
                case "water": case "river": return "water";
                case "bridge": return "bridge";
                case "mainstreet": case "mainst": return "mainstreet";
                case "oob": return "oob";
                default: return "land";   // "", "land", "block", unknown
            }
        }

        // Channel-connected terrain (channel continues through bridges)
        public static bool IsChannelTerrain(string normalizedTerrain)
        {
            return normalizedTerrain == "water" || normalizedTerrain == "bridge";
        }

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
        // Pre-computed metadata for one terrain chunk (used for parallel generation)
        private struct ChunkMeta
        {
            public int row, col;
            public int w, h, d;
            public float cxMin, cxMax, czMin, czMax;
            public float blockX, blockZ;
            public string terrain;      // normalized: land/water/bridge/mainstreet/oob
            public int openMask;        // bits N=1 E=2 S=4 W=8: neighbor is channel (water|bridge)
            public int mainstMask;      // same bit order: neighbor is mainstreet
            public Vector3 worldOrigin;
            public Vector3 localOrigin;
        }

        // River channel profile (world units, from authored river_straight.json):
        //   across channel: promenade 0.8 | parapet 0.1 | gap 0.1 | quay wall 0.1 | channel core
        //   depth:          bed 0.2 | water 1.4 | air 0.3 | grade slab 0.1 | parapet above +0.4
        private const float PromenadeW = 0.8f;
        private const float ParapetW = 0.1f;
        private const float GapW = 0.1f;
        private const float QuayWallW = 0.1f;
        private const float RiverBedDepth = 0.2f;    // stone bed thickness below water
        private const float RiverWaterDepth = 1.4f;  // water column
        private const float RiverAirGap = 0.3f;      // air between water surface and grade slab
        private const float GradeSlabH = 0.1f;       // 2 voxels @ 0.05 — matches land slab
        private const float ParapetH = 0.4f;         // quay railing above grade
        private const float EndWallW = 0.1f;         // cap wall at terminated channel ends

        public static List<TerrainChunk> GeneratePerBlockTerrain(
            int minRow, int maxRow, int minCol, int maxCol,
            float centerRow, float centerCol,
            float spacing,
            float groundTileSize,
            float roadWidth,
            float voxelSize,
            float sidewalkWidth,
            Vector3 mapRootOffset,
            IReadOnlyDictionary<Vector2Int, string> terrainByCell,
            string[][] hSeams,   // [row][col] = seam between (row,col) and (row+1,col)
            string[][] vSeams,   // [row][col] = seam between (row,col) and (row,col+1)
            out Dictionary<string, Vector3> anchorPositions)
        {
            float halfRoad = roadWidth * 0.5f;
            const int hLand = 2; // flat terrain: 2 voxels thick
            float terrainTopY = mapRootOffset.y + hLand * voxelSize;
            const float eps = 0.001f;

            string TerrainAt(int col, int row)
            {
                if (terrainByCell != null &&
                    terrainByCell.TryGetValue(new Vector2Int(col, row), out var t))
                    return NormalizeTerrain(t);
                return "land";
            }

            string SeamAt(int row, int col, int edge) // edge: 0=N 1=E 2=S 3=W
            {
                string[][] grid; int r, c;
                switch (edge)
                {
                    case 0: grid = hSeams; r = row - 1; c = col; break; // seam with row-1
                    case 2: grid = hSeams; r = row;     c = col; break; // seam with row+1
                    case 1: grid = vSeams; r = row;     c = col; break; // seam with col+1
                    default:grid = vSeams; r = row;     c = col - 1; break; // seam with col-1
                }
                if (grid == null || r < 0 || r >= grid.Length) return "road";
                var rowArr = grid[r];
                if (rowArr == null || c < 0 || c >= rowArr.Length) return "road";
                return rowArr[c] ?? "road";
            }

            // Water/bridge chunks extend below grade: bed+water+air under the grade
            // slab, plus parapet headroom above it. Grade top stays at +0.1 world.
            int waterH = Mathf.CeilToInt((RiverBedDepth + RiverWaterDepth + RiverAirGap +
                                          GradeSlabH + ParapetH) / voxelSize);
            float waterYBase = -(RiverBedDepth + RiverWaterDepth + RiverAirGap);

            int rowCount = maxRow - minRow + 1;
            int colCount = maxCol - minCol + 1;
            int chunkCount = rowCount * colCount;

            // Phase 1 (main thread): pre-compute all chunk metadata
            var meta = new ChunkMeta[chunkCount];
            int idx = 0;
            for (int row = minRow; row <= maxRow; row++)
            {
                for (int col = minCol; col <= maxCol; col++)
                {
                    float blockX = (col - centerCol) * spacing;
                    float blockZ = -(row - centerRow) * spacing;

                    float cxMin = blockX - groundTileSize * 0.5f - halfRoad;
                    float cxMax = blockX + groundTileSize * 0.5f + halfRoad;
                    float czMin = blockZ - groundTileSize * 0.5f - halfRoad;
                    float czMax = blockZ + groundTileSize * 0.5f + halfRoad;

                    int w = Mathf.Max(1, Mathf.CeilToInt((cxMax - cxMin) / voxelSize));
                    int d = Mathf.Max(1, Mathf.CeilToInt((czMax - czMin) / voxelSize));

                    string terr = TerrainAt(col, row);
                    int openMask = 0, mainstMask = 0;
                    if (IsChannelTerrain(TerrainAt(col, row - 1))) openMask |= 1;
                    if (IsChannelTerrain(TerrainAt(col + 1, row))) openMask |= 2;
                    if (IsChannelTerrain(TerrainAt(col, row + 1))) openMask |= 4;
                    if (IsChannelTerrain(TerrainAt(col - 1, row))) openMask |= 8;
                    if (TerrainAt(col, row - 1) == "mainstreet") mainstMask |= 1;
                    if (TerrainAt(col + 1, row) == "mainstreet") mainstMask |= 2;
                    if (TerrainAt(col, row + 1) == "mainstreet") mainstMask |= 4;
                    if (TerrainAt(col - 1, row) == "mainstreet") mainstMask |= 8;

                    bool deep = IsChannelTerrain(terr);
                    int h = deep ? waterH : hLand;
                    float yBase = deep ? waterYBase : 0f;

                    meta[idx] = new ChunkMeta
                    {
                        row = row, col = col,
                        w = w, h = h, d = d,
                        cxMin = cxMin, cxMax = cxMax, czMin = czMin, czMax = czMax,
                        blockX = blockX, blockZ = blockZ,
                        terrain = terr, openMask = openMask, mainstMask = mainstMask,
                        worldOrigin = new Vector3(cxMin, yBase, czMin) + mapRootOffset,
                        localOrigin = new Vector3(cxMin, yBase, czMin)
                    };
                    idx++;
                }
            }

            // Phase 2 (parallel): fill voxel data for each chunk concurrently
            var chunkData = new uint[chunkCount][];
            for (int i = 0; i < chunkCount; i++)
                chunkData[i] = new uint[meta[i].w * meta[i].h * meta[i].d];

            // Unsupported water masks (corner/T/cross) → land fill + warning after the loop
            var unsupportedCells = new System.Collections.Concurrent.ConcurrentBag<string>();

            Parallel.For(0, chunkCount, i =>
            {
                ref var m = ref meta[i];
                var data = chunkData[i];
                int w = m.w, h = m.h, dh = m.d;

                float iMinX = m.blockX - groundTileSize * 0.5f;
                float iMaxX = m.blockX + groundTileSize * 0.5f;
                float iMinZ = m.blockZ - groundTileSize * 0.5f;
                float iMaxZ = m.blockZ + groundTileSize * 0.5f;

                switch (m.terrain)
                {
                    case "water":
                    case "bridge":
                        if (SupportedWaterMask(m.openMask))
                            FillChannelChunk(data, w, h, dh, m.localOrigin, voxelSize, halfRoad,
                                iMinX, iMaxX, iMinZ, iMaxZ, m.openMask, m.terrain == "bridge");
                        else { FillLandChunk(data, m, w, h, dh, voxelSize, halfRoad, centerRow, centerCol, spacing, groundTileSize, sidewalkWidth, eps, iMinX, iMaxX, iMinZ, iMaxZ, SeamAt); unsupportedCells.Add($"r{m.row}c{m.col}({m.terrain} mask {m.openMask})"); }
                        break;
                    case "oob":
                        FillCorridors(data, m, w, h, dh, voxelSize, halfRoad, centerRow, centerCol, spacing, eps, SeamAt);
                        FillOobTile(data, w, h, dh, m.localOrigin, voxelSize, iMinX, iMaxX, iMinZ, iMaxZ);
                        break;
                    case "mainstreet":
                        FillCorridors(data, m, w, h, dh, voxelSize, halfRoad, centerRow, centerCol, spacing, eps, SeamAt);
                        FillMainstreetTile(data, w, h, dh, m.localOrigin, voxelSize,
                            iMinX, iMaxX, iMinZ, iMaxZ, sidewalkWidth, m.mainstMask);
                        break;
                    default: // land
                        FillLandChunk(data, m, w, h, dh, voxelSize, halfRoad, centerRow, centerCol, spacing, groundTileSize, sidewalkWidth, eps, iMinX, iMaxX, iMinZ, iMaxZ, SeamAt);
                        break;
                }
            });

            if (!unsupportedCells.IsEmpty)
            {
                var list = new List<string>(unsupportedCells);
                Debug.LogWarning($"[VoxelTerrainBuilder] {list.Count} water/bridge cell(s) have unsupported " +
                    $"channel masks (corner/T/cross — corner recipe pending authored tile) — filled as land: " +
                    $"{string.Join(", ", list.GetRange(0, Mathf.Min(12, list.Count)))}{(list.Count > 12 ? " …" : "")}");
            }

            // Phase 3 (main thread): assemble results
            var chunks = new List<TerrainChunk>(chunkCount);
            anchorPositions = new Dictionary<string, Vector3>(chunkCount);

            for (int i = 0; i < chunkCount; i++)
            {
                ref var m = ref meta[i];
                chunks.Add(new TerrainChunk
                {
                    name = $"terrain_r{m.row}c{m.col}",
                    data = chunkData[i],
                    w = m.w, h = m.h, d = m.d,
                    worldOrigin = m.worldOrigin
                });

                // Anchor stays at grade top regardless of chunk depth (water chunks
                // hang below grade; anchors sit on the promenade/street surface)
                Vector3 anchorWorld = new Vector3(m.blockX, terrainTopY, m.blockZ) + mapRootOffset;
                anchorPositions[$"r{m.row}c{m.col}"] = anchorWorld;
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
        /// Channel masks we can build: straights (N|S, E|W), dead ends (single bit —
        /// only occurs at the map/oob edge; rivers never terminate mid-city),
        /// isolated (0 — defensive). Corners (adjacent bits), T and cross are
        /// authored-tile recipes pending the corner JSON.
        /// </summary>
        private static bool SupportedWaterMask(int mask)
        {
            switch (mask)
            {
                case 0b0101: // N|S straight
                case 0b1010: // E|W straight
                case 0b0001: case 0b0100:  // N / S end
                case 0b0010: case 0b1000:  // E / W end
                case 0:                    // isolated (defensive — not in replica)
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Original land recipe: four road strips + sidewalk-ring ground tile.
        /// Corridor fills respect seam types (mainstreet seams = plain asphalt).
        /// </summary>
        private static void FillLandChunk(uint[] data, ChunkMeta m, int w, int h, int d,
            float voxelSize, float halfRoad, float centerRow, float centerCol, float spacing,
            float groundTileSize, float sidewalkWidth, float eps,
            float iMinX, float iMaxX, float iMinZ, float iMaxZ,
            Func<int, int, int, string> seamAt)
        {
            FillCorridors(data, m, w, h, d, voxelSize, halfRoad, centerRow, centerCol, spacing, eps, seamAt);
            FillGroundTile(data, w, h, d, m.localOrigin, voxelSize, iMinX, iMaxX, iMinZ, iMaxZ, sidewalkWidth);
        }

        /// <summary>
        /// Fill the four corridor halves owned by this chunk, choosing fill per seam
        /// type: 'mainstreet' → plain asphalt boulevard; 'river' on a non-channel
        /// block shouldn't occur (road fallback); default/'road'/'bridge' → asphalt
        /// with cobble center stripe.
        /// </summary>
        private static void FillCorridors(uint[] data, ChunkMeta m, int w, int h, int d,
            float voxelSize, float halfRoad, float centerRow, float centerCol, float spacing,
            float eps, Func<int, int, int, string> seamAt)
        {
            // Horizontal road above (N edge — seam with row-1)
            float roadZAbove = -(m.row - 0.5f - centerRow) * spacing;
            if (roadZAbove >= m.czMin - eps && roadZAbove <= m.czMax + eps)
                FillSeamStrip(data, w, h, d, m.localOrigin, voxelSize,
                    m.cxMin, m.cxMax, roadZAbove - halfRoad, roadZAbove + halfRoad, true,
                    seamAt(m.row, m.col, 0));
            // Horizontal road below (S edge — seam with row+1)
            float roadZBelow = -(m.row + 0.5f - centerRow) * spacing;
            if (roadZBelow >= m.czMin - eps && roadZBelow <= m.czMax + eps)
                FillSeamStrip(data, w, h, d, m.localOrigin, voxelSize,
                    m.cxMin, m.cxMax, roadZBelow - halfRoad, roadZBelow + halfRoad, true,
                    seamAt(m.row, m.col, 2));
            // Vertical road left (W edge — seam with col-1)
            float roadXLeft = (m.col - 0.5f - centerCol) * spacing;
            if (roadXLeft >= m.cxMin - eps && roadXLeft <= m.cxMax + eps)
                FillSeamStrip(data, w, h, d, m.localOrigin, voxelSize,
                    roadXLeft - halfRoad, roadXLeft + halfRoad, m.czMin, m.czMax, false,
                    seamAt(m.row, m.col, 3));
            // Vertical road right (E edge — seam with col+1)
            float roadXRight = (m.col + 0.5f - centerCol) * spacing;
            if (roadXRight >= m.cxMin - eps && roadXRight <= m.cxMax + eps)
                FillSeamStrip(data, w, h, d, m.localOrigin, voxelSize,
                    roadXRight - halfRoad, roadXRight + halfRoad, m.czMin, m.czMax, false,
                    seamAt(m.row, m.col, 1));
        }

        private static void FillSeamStrip(uint[] data, int w, int h, int d, Vector3 origin,
            float voxelSize, float minX, float maxX, float minZ, float maxZ,
            bool isHorizontal, string seamType)
        {
            if (seamType == "mainstreet")
                FillAsphaltStrip(data, w, h, d, origin, voxelSize, minX, maxX, minZ, maxZ);
            else
                FillRoadStrip(data, w, h, d, origin, voxelSize, minX, maxX, minZ, maxZ, isHorizontal);
        }

        /// <summary>Plain asphalt strip (mainstreet boulevard corridors — no center stripe).</summary>
        private static void FillAsphaltStrip(uint[] data, int w, int h, int d, Vector3 origin,
            float voxelSize, float minX, float maxX, float minZ, float maxZ)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt((minX - origin.x) / voxelSize), 0, w - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((maxX - origin.x) / voxelSize), 0, w - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((minZ - origin.z) / voxelSize), 0, d - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - origin.z) / voxelSize), 0, d - 1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int y = 0; y < h; y++)
                        data[VoxelIndex(x, y, z, w, h, d)] = MAT_ASPHALT;
        }

        /// <summary>Out-of-bounds block: flat stone interior, no sidewalk ring.</summary>
        private static void FillOobTile(uint[] data, int w, int h, int d, Vector3 origin,
            float voxelSize, float minX, float maxX, float minZ, float maxZ)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt((minX - origin.x) / voxelSize), 0, w - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((maxX - origin.x) / voxelSize), 0, w - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((minZ - origin.z) / voxelSize), 0, d - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - origin.z) / voxelSize), 0, d - 1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int idx = VoxelIndex(x, 0, z, w, h, d);
                    if (data[idx] == MAT_AIR || data[idx] == MAT_SIDEWALK || data[idx] == MAT_STONE)
                        for (int y = 0; y < h; y++)
                            data[VoxelIndex(x, y, z, w, h, d)] = MAT_STONE;
                }
        }

        /// <summary>
        /// Mainstreet block (canonical Gangsters main street: a full block-wide
        /// boulevard, not a wide road). Concrete curb ring + asphalt interior +
        /// paired cobblestone trolley tracks along the avenue axis — axis derived
        /// from which neighbors are also mainstreet (mask, same bit order as water).
        /// </summary>
        private static void FillMainstreetTile(uint[] data, int w, int h, int d, Vector3 origin,
            float voxelSize, float minX, float maxX, float minZ, float maxZ,
            float sidewalkWidth, int mainstMask)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt((minX - origin.x) / voxelSize), 0, w - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((maxX - origin.x) / voxelSize), 0, w - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((minZ - origin.z) / voxelSize), 0, d - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - origin.z) / voxelSize), 0, d - 1);
            int sw = Mathf.Max(1, Mathf.RoundToInt(sidewalkWidth / voxelSize));

            float cx = (minX + maxX) * 0.5f, cz = (minZ + maxZ) * 0.5f;
            bool nsAvenue = (mainstMask & 0b0101) != 0;   // mainst neighbor N or S → tracks run N-S
            bool ewAvenue = (mainstMask & 0b1010) != 0;   // E or W → tracks run E-W
            if (!nsAvenue && !ewAvenue) ewAvenue = true;  // isolated mainst → default E-W
            float trackOff = 0.35f, trackHalf = voxelSize * 0.75f;

            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    bool isEdge = x < x0 + sw || x > x1 - sw || z < z0 + sw || z > z1 - sw;
                    uint mat = isEdge ? MAT_SIDEWALK : MAT_ASPHALT;

                    if (!isEdge)
                    {
                        float wx = origin.x + (x + 0.5f) * voxelSize;
                        float wz = origin.z + (z + 0.5f) * voxelSize;
                        if (nsAvenue && Mathf.Abs(Mathf.Abs(wx - cx) - trackOff) < trackHalf) mat = MAT_COBBLESTONE;
                        if (ewAvenue && Mathf.Abs(Mathf.Abs(wz - cz) - trackOff) < trackHalf) mat = MAT_COBBLESTONE;
                    }

                    for (int y = 0; y < h; y++)
                    {
                        int idx = VoxelIndex(x, y, z, w, h, d);
                        if (data[idx] == MAT_AIR || data[idx] == MAT_SIDEWALK || data[idx] == MAT_STONE)
                            data[idx] = mat;
                    }
                }
            }
        }

        /// <summary>
        /// River channel chunk (water) / bridge deck chunk (bridge) — reproduces the
        /// authored river_straight profile at runtime scale.
        ///
        /// Axis-agnostic: for a channel along Z (N|S mask) the cross-section varies
        /// with X; for a channel along X (E|W) it varies with Z. Corridor halves on
        /// channel-open edges continue the cross-section (the "river seam"); on
        /// closed edges they are ordinary road. Lateral corridors (channel flanks)
        /// are always riverside streets. Dead ends get a full-depth cap wall at the
        /// interior edge. Bridge cells get a grade-level deck over the channel plus
        /// rails at the deck edges overhanging open corridor water.
        /// </summary>
        private static void FillChannelChunk(
            uint[] data, int w, int h, int d, Vector3 origin, float voxelSize, float halfRoad,
            float iMinX, float iMaxX, float iMinZ, float iMaxZ, int openMask, bool bridge)
        {
            float gt = iMaxX - iMinX;
            bool flowEW = (openMask & 0b1010) != 0; // any E/W mouth → channel runs E-W
            // Along-flow strip edges: v<0 is W (EW) or S (NS); v>=gt is E or N.
            bool openStart = flowEW ? (openMask & 0b1000) != 0 : (openMask & 0b0100) != 0;
            bool openEnd   = flowEW ? (openMask & 0b0010) != 0 : (openMask & 0b0001) != 0;

            // Depth indices: bed | water | air | grade slab | parapet
            int bedV = Mathf.CeilToInt(RiverBedDepth / voxelSize);
            int waterV = Mathf.CeilToInt(RiverWaterDepth / voxelSize);
            int parapetV = Mathf.Max(1, Mathf.RoundToInt(ParapetH / voxelSize));
            int slabV = Mathf.Max(1, Mathf.RoundToInt(GradeSlabH / voxelSize));
            int slab0 = h - parapetV - slabV;      // first grade-slab index (38 @ h=48)
            int waterTop = bedV + waterV;          // water fills [bedV, waterTop)

            int promV = Mathf.RoundToInt(PromenadeW / voxelSize);
            int parapetEndV = promV + Mathf.RoundToInt(ParapetW / voxelSize);
            int gapEndV = parapetEndV + Mathf.RoundToInt(GapW / voxelSize);
            int wallEndV = gapEndV + Mathf.RoundToInt(QuayWallW / voxelSize);
            int capV = Mathf.Max(1, Mathf.RoundToInt(EndWallW / voxelSize));

            float latCobN = -halfRoad * 0.5f, latCobP = gt + halfRoad * 0.5f; // lateral corridor centerlines

            for (int z = 0; z < d; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    float wx = origin.x + (x + 0.5f) * voxelSize;
                    float wz = origin.z + (z + 0.5f) * voxelSize;
                    float u = (flowEW ? wz - iMinZ : wx - iMinX);          // across-channel rel interior
                    float v = (flowEW ? wx - iMinX : wz - iMinZ);          // along-channel rel interior
                    int uIdx = Mathf.RoundToInt(u / voxelSize);

                    if (u < 0f || u >= gt)
                    {
                        // Lateral corridor (riverside street) — asphalt + cobble center
                        bool cob = Mathf.Abs(u - latCobN) < voxelSize || Mathf.Abs(u - latCobP) < voxelSize;
                        WriteColumn(data, x, z, w, h, d, slab0, slabV, cob ? MAT_COBBLESTONE : MAT_ASPHALT);
                        continue;
                    }

                    int fZone = v < 0f ? -1 : (v >= gt ? 1 : 0);
                    bool stripOpen = fZone == 0 || (fZone < 0 ? openStart : openEnd);
                    if (!stripOpen)
                    {
                        // Closed flow end — ordinary road (perimeter street at map edge)
                        WriteColumn(data, x, z, w, h, d, slab0, slabV, MAT_ASPHALT);
                        continue;
                    }

                    // Channel cross-section, measured from nearest interior edge
                    int e = Mathf.Min(uIdx, Mathf.RoundToInt(gt / voxelSize) - uIdx);
                    int cls; // 0 promenade | 1 parapet | 2 gap | 3 quay wall | 4 channel
                    if (e < promV) cls = 0;
                    else if (e < parapetEndV) cls = 1;
                    else if (e < gapEndV) cls = 2;
                    else if (e < wallEndV) cls = 3;
                    else cls = 4;

                    // Cap wall across channel at closed interior ends (river meets oob)
                    bool capWall = cls == 4 && !bridge &&
                        ((v < EndWallW && !openStart) || (v > gt - EndWallW && !openEnd));
                    // Bridge deck rails at flow edges overhanging open corridor water
                    bool rail = bridge && cls == 4 &&
                        ((v < EndWallW && openStart) || (v > gt - EndWallW && openEnd));

                    for (int y = 0; y < h; y++)
                    {
                        uint mat = MAT_AIR;
                        if (capWall) mat = y < slab0 + slabV ? MAT_STONE : MAT_AIR;
                        else
                        {
                            switch (cls)
                            {
                                case 0: if (y >= slab0 && y < slab0 + slabV) mat = MAT_SIDEWALK; break;
                                case 1: if (y >= slab0 && y < slab0 + slabV) mat = MAT_SIDEWALK;
                                        else if (!bridge && y >= slab0 + slabV) mat = MAT_STONE; break;
                                case 2: if (y >= slab0 && y < slab0 + slabV) mat = MAT_SIDEWALK; break;
                                case 3: if (y < slab0 + slabV) mat = MAT_STONE; break;
                                default: // channel
                                    if (y < bedV) mat = MAT_STONE;
                                    else if (y < waterTop) mat = MAT_WATER;
                                    else if (bridge && y >= slab0 && y < slab0 + slabV) mat = MAT_ASPHALT;
                                    else if (rail && y >= slab0 + slabV) mat = MAT_STONE;
                                    break;
                            }
                        }
                        if (mat != MAT_AIR) data[VoxelIndex(x, y, z, w, h, d)] = mat;
                    }
                }
            }
        }

        /// <summary>Write the grade-slab voxels for one column.</summary>
        private static void WriteColumn(uint[] data, int x, int z, int w, int h, int d,
            int slab0, int slabV, uint mat)
        {
            for (int y = slab0; y < slab0 + slabV && y < h; y++)
                data[VoxelIndex(x, y, z, w, h, d)] = mat;
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
