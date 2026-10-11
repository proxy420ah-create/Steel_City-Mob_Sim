using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Bakes static city buildings into sector-level merged voxel buffers.
    /// Each sector groups N blocks worth of buildings into a single ComputeBuffer,
    /// reducing draw calls from 1-per-building to 1-per-sector.
    ///
    /// Data format:
    ///   - mergedVoxelData: flat uint[] — all buildings' voxel arrays concatenated
    ///   - buildingMeta[i] = (bufferOffset, dimsX, dimsY, dimsZ)
    ///   - buildingPositions[i] = (worldOffsetX, worldOffsetY, worldOffsetZ, 0)
    ///
    /// The shader (BUILDING_INSTANCING keyword) reads per-building dims + buffer offset
    /// to index into the correct section of the flat buffer.
    /// </summary>
    public static class SectorBaker
    {
        /// <summary>
        /// Build data for a single sector. Returned to caller for RegisterSector().
        /// </summary>
        public class SectorData
        {
            public string name;
            public uint[] mergedVoxelData;
            public Vector4[] buildingMeta;
            public Vector4[] buildingPositions;
            public float voxelSize;
            public Vector3 sectorMin;
            public Vector3 sectorMax;
            public int buildingCount;
            /// <summary>One record per building that made it into this sector (see PlacedBuilding).</summary>
            public List<PlacedBuilding> placements = new List<PlacedBuilding>();
        }

        /// <summary>
        /// Info for a single building to be baked into a sector.
        /// </summary>
        public struct BuildingInfo
        {
            public string stassetPath;   // full path to .stasset file
            public Vector3 worldOffset;  // world-space corner position (not center)
            public float voxelSize;
            public int row;              // block row (for procedural seeding)
            public int col;              // block col (for procedural seeding)
            public int subIndex;         // building index within block (for procedural seeding)
            public int buildingsInBlock; // buildings on the layout block (1 = the block's only building)
        }

        /// <summary>
        /// CPU-side record of one placed building: the placement this baker computed plus a REFERENCE to the
        /// voxel array the sector buffer was filled from (shared asset data or pooled debris variant — never
        /// copied, treat as read-only). CityMap3D turns these into the address registry and the collision
        /// volumes, so sector mode resolves building attachment points and bullets exactly like the
        /// per-building path does.
        /// </summary>
        public struct PlacedBuilding
        {
            public string stassetPath;
            public int row, col, subIndex;
            public int buildingsInBlock;
            public Vector3 corner;       // world min corner of the voxel grid
            public float voxelSize;
            public int w, h, d;
            public uint[] voxels;
            public bool isEmptyLand;
        }

        /// <summary>Detect empty land stasset paths for procedural debris scattering.</summary>
        private static bool IsEmptyLand(string stassetPath)
        {
            return stassetPath != null &&
                   stassetPath.Contains("empty_land") &&
                   !stassetPath.Contains("tenement");
        }

        /// <summary>
        /// Bake a list of buildings into a single sector.
        /// Loads voxel data from the packed voxel cache (must be pre-loaded).
        ///
        /// worldOffset = corner position of the building (where the voxel grid starts).
        /// The shader uses this as volOffset — the min corner of the volume AABB.
        /// </summary>
        public static SectorData BakeSector(string sectorName, List<BuildingInfo> buildings)
        {
            if (buildings == null || buildings.Count == 0)
            {
                Debug.LogWarning($"[SectorBaker] BakeSector '{sectorName}': no buildings provided");
                return null;
            }

            int buildingCount = buildings.Count;
            var buildingMeta = new Vector4[buildingCount];
            var buildingPositions = new Vector4[buildingCount];

            // First pass: compute total voxel count and per-building offsets.
            // Empty lots pool into NumVariants shared debris variants — buildings
            // mapping to the same variant share one buffer region (meta.x offset).
            int totalVoxels = 0;
            var dimsList = new List<(int w, int h, int d)>(buildingCount);
            var variantOffsets = new Dictionary<int, int>();

            for (int i = 0; i < buildingCount; i++)
            {
                var info = buildings[i];
                var (packedData, w, h, d) = VoxelChunkManager.GetPackedVoxels(info.stassetPath);
                if (packedData == null)
                {
                    Debug.LogError($"[SectorBaker] Failed to load voxel data for {info.stassetPath}");
                    dimsList.Add((0, 0, 0));
                    buildingMeta[i] = new Vector4(0, 0, 0, 0);
                    buildingPositions[i] = new Vector4(info.worldOffset.x, info.worldOffset.y, info.worldOffset.z, 0);
                    continue;
                }

                int voxelCount = w * h * d;
                int bufferOffset;
                if (IsEmptyLand(info.stassetPath))
                {
                    int variant = ProceduralDebrisScatterer.GetVariantIndex(info.row, info.col, info.subIndex);
                    if (!variantOffsets.TryGetValue(variant, out bufferOffset))
                    {
                        bufferOffset = totalVoxels;
                        variantOffsets[variant] = bufferOffset;
                        totalVoxels += voxelCount;
                    }
                }
                else
                {
                    bufferOffset = totalVoxels;
                    totalVoxels += voxelCount;
                }
                buildingMeta[i] = new Vector4(bufferOffset, w, h, d);
                buildingPositions[i] = new Vector4(info.worldOffset.x, info.worldOffset.y, info.worldOffset.z, info.voxelSize);
                dimsList.Add((w, h, d));
            }

            if (totalVoxels == 0)
            {
                Debug.LogError($"[SectorBaker] BakeSector '{sectorName}': all buildings failed to load");
                return null;
            }

            // Second pass: concatenate voxel data into flat buffer
            var mergedVoxelData = new uint[totalVoxels];
            var placements = new List<PlacedBuilding>(buildingCount);
            var sourceByOffset = new Dictionary<int, uint[]>();   // buffer offset -> the array that region was filled from
            int writeOffset = 0;
            Vector3 sectorMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 sectorMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            for (int i = 0; i < buildingCount; i++)
            {
                var info = buildings[i];
                var (packedData, w, h, d) = VoxelChunkManager.GetPackedVoxels(info.stassetPath);
                if (packedData == null) continue;

                int voxelCount = w * h * d;

                // Only write regions allocated in pass 1 — buildings sharing a
                // pooled variant offset skip the copy (already written there).
                // `source` is the array the region was filled from (the pooled debris variant
                // for empty lots, the shared packed asset otherwise); a building that shares
                // an offset reuses the writer's array — exactly what the renderer shows there.
                bool emptyLand = IsEmptyLand(info.stassetPath);
                uint[] source;
                if ((int)buildingMeta[i].x == writeOffset)
                {
                    if (emptyLand)
                    {
                        int variant = ProceduralDebrisScatterer.GetVariantIndex(info.row, info.col, info.subIndex);
                        source = ProceduralDebrisScatterer.GetVariantData(packedData, w, h, d, variant);
                    }
                    else
                    {
                        source = packedData;
                    }
                    System.Array.Copy(source, 0, mergedVoxelData, writeOffset, voxelCount);
                    sourceByOffset[writeOffset] = source;
                    writeOffset += voxelCount;
                }
                else if (!sourceByOffset.TryGetValue((int)buildingMeta[i].x, out source))
                {
                    source = packedData;
                }

                placements.Add(new PlacedBuilding
                {
                    stassetPath = info.stassetPath,
                    row = info.row,
                    col = info.col,
                    subIndex = info.subIndex,
                    buildingsInBlock = info.buildingsInBlock,
                    corner = info.worldOffset,
                    voxelSize = info.voxelSize,
                    w = w, h = h, d = d,
                    voxels = source,
                    isEmptyLand = emptyLand
                });

                // Compute sector AABB from building world positions + sizes
                float vs = info.voxelSize;
                Vector3 buildingMin = info.worldOffset;
                Vector3 buildingMax = info.worldOffset + new Vector3(w * vs, h * vs, d * vs);
                sectorMin = Vector3.Min(sectorMin, buildingMin);
                sectorMax = Vector3.Max(sectorMax, buildingMax);
            }

            var data = new SectorData
            {
                name = sectorName,
                mergedVoxelData = mergedVoxelData,
                buildingMeta = buildingMeta,
                buildingPositions = buildingPositions,
                voxelSize = buildings[0].voxelSize,
                sectorMin = sectorMin,
                sectorMax = sectorMax,
                buildingCount = buildingCount,
                placements = placements
            };

            Debug.Log($"[SectorBaker] Baked sector '{sectorName}': {buildingCount} buildings, {totalVoxels:N0} voxels, bounds {sectorMin}..{sectorMax}");
            return data;
        }

        /// <summary>
        /// Register a baked sector with the VoxelChunkManager for rendering.
        /// </summary>
        public static void RegisterSector(VoxelChunkManager chunkManager, SectorData data)
        {
            if (chunkManager == null || data == null) return;
            chunkManager.RegisterSector(data.name, data.mergedVoxelData,
                data.buildingMeta, data.buildingPositions,
                data.voxelSize, data.sectorMin, data.sectorMax);
        }

        /// <summary>
        /// Group blocks into sectors of sectorSize x sectorSize blocks.
        /// Returns a list of sectors, each containing the building infos for that group.
        ///
        /// blockAnchors: maps "r{row}c{col}" to world-space center positions.
        /// layout: city layout with block -> buildings mapping.
        /// buildingsPerBlockRow: number of buildings per block row (3 = 3x3 grid).
        /// buildingVoxelWidth: voxel width of a single building (32).
        /// sidewalkWidth, roadWidth, voxelSize: from CityMap3D.
        /// centerRow, centerCol: city center grid coords.
        /// spacing: world-space distance between block centers.
        /// </summary>
        public static List<SectorData> BakeAllSectors(
            VoxelChunkManager chunkManager,
            CityLayout layout,
            Dictionary<string, Vector3> blockAnchors,
            int sectorSizeBlocks,
            int buildingsPerBlockRow,
            int buildingVoxelWidth,
            float sidewalkWidth,
            float roadWidth,
            float voxelSize,
            int centerRow, int centerCol,
            float spacing)
        {
            var sectors = new List<SectorData>();

            if (layout == null || layout.blocks == null || layout.blocks.Length == 0)
            {
                Debug.LogWarning("[SectorBaker] No layout blocks to bake");
                return sectors;
            }

            // Group blocks by sector
            var sectorGroups = new Dictionary<(int sr, int sc), List<CityLayoutBlock>>();

            foreach (var lb in layout.blocks)
            {
                int sr = lb.row / sectorSizeBlocks;
                int sc = lb.col / sectorSizeBlocks;
                var key = (sr, sc);
                if (!sectorGroups.TryGetValue(key, out var list))
                {
                    list = new List<CityLayoutBlock>();
                    sectorGroups[key] = list;
                }
                list.Add(lb);
            }

            Debug.Log($"[SectorBaker] Grouped {layout.blocks.Length} blocks into {sectorGroups.Count} sectors (sectorSize={sectorSizeBlocks}x{sectorSizeBlocks})");

            float groundTileSize = (buildingVoxelWidth * buildingsPerBlockRow * voxelSize) + sidewalkWidth * 2f;

            int sectorNum = 0;
            foreach (var kvp in sectorGroups)
            {
                sectorNum++;
                var (sr, sc) = kvp.Key;
                var blocksInSector = kvp.Value;
                var buildingInfos = new List<BuildingInfo>();

                foreach (var lb in blocksInSector)
                {
                    if (lb.buildings == null || lb.buildings.Length == 0) continue;

                    string anchorKey = $"r{lb.row}c{lb.col}";
                    Vector3 anchorPos;
                    if (blockAnchors == null || !blockAnchors.TryGetValue(anchorKey, out anchorPos))
                    {
                        // Fallback: compute from grid position
                        anchorPos = new Vector3(
                            (lb.col - centerCol) * spacing,
                            0f,
                            -(lb.row - centerRow) * spacing);
                    }

                    int buildingCount = lb.buildings.Length;

                    if (buildingCount == 1)
                    {
                        // Single building — centered on anchor
                        string fullPath = Path.Combine(Application.streamingAssetsPath, lb.buildings[0].stasset);
                        var (vw, vh, vd) = VoxelChunkManager.GetStassetDimensions(fullPath);
                        if (vw == 0) continue;

                        Vector3 cornerPos = anchorPos - new Vector3(vw * voxelSize * 0.5f, 0f, vd * voxelSize * 0.5f);
                        buildingInfos.Add(new BuildingInfo
                        {
                            stassetPath = fullPath,
                            worldOffset = cornerPos,
                            voxelSize = voxelSize,
                            row = lb.row,
                            col = lb.col,
                            subIndex = 0,
                            buildingsInBlock = buildingCount
                        });
                    }
                    else
                    {
                        // Check for full-block buildings (tenements)
                        bool hasFullBlock = false;
                        for (int i = 0; i < buildingCount; i++)
                        {
                            string fullPath = Path.Combine(Application.streamingAssetsPath, lb.buildings[i].stasset);
                            var (vw, vh, vd) = VoxelChunkManager.GetStassetDimensions(fullPath);
                            if (vw >= VoxelChunkManager.FullBlockVoxelThreshold ||
                                vd >= VoxelChunkManager.FullBlockVoxelThreshold)
                            {
                                hasFullBlock = true;
                                break;
                            }
                        }

                        if (hasFullBlock)
                        {
                            // Place first full-block building centered on anchor
                            for (int i = 0; i < buildingCount; i++)
                            {
                                string fullPath = Path.Combine(Application.streamingAssetsPath, lb.buildings[i].stasset);
                                var (vw, vh, vd) = VoxelChunkManager.GetStassetDimensions(fullPath);
                                if (vw >= VoxelChunkManager.FullBlockVoxelThreshold ||
                                    vd >= VoxelChunkManager.FullBlockVoxelThreshold)
                                {
                                    Vector3 cornerPos = anchorPos - new Vector3(vw * voxelSize * 0.5f, 0f, vd * voxelSize * 0.5f);
                                    buildingInfos.Add(new BuildingInfo
                                    {
                                        stassetPath = fullPath,
                                        worldOffset = cornerPos,
                                        voxelSize = voxelSize,
                                        row = lb.row,
                                        col = lb.col,
                                        subIndex = i,
                                        buildingsInBlock = buildingCount
                                    });
                                    break;
                                }
                            }
                        }
                        else
                        {
                            // Sub-grid placement
                            int cols = Mathf.CeilToInt(Mathf.Sqrt(buildingCount));
                            int rows = Mathf.CeilToInt((float)buildingCount / cols);
                            float plotSize = groundTileSize - sidewalkWidth * 2f;
                            float subSize = plotSize / cols;
                            float subOffset = plotSize * 0.5f - subSize * 0.5f;
                            float buildingMeshWidth = buildingVoxelWidth * voxelSize;

                            for (int i = 0; i < buildingCount; i++)
                            {
                                int r = i / cols;
                                int c = i % cols;
                                float px = -subOffset + c * subSize;
                                float pz = -subOffset + r * subSize;

                                string fullPath = Path.Combine(Application.streamingAssetsPath, lb.buildings[i].stasset);
                                var (vw, vh, vd) = VoxelChunkManager.GetStassetDimensions(fullPath);
                                if (vw == 0) continue;

                                float scale = subSize / buildingMeshWidth;
                                Vector3 buildingCenter = anchorPos + new Vector3(px, 0f, pz);
                                Vector3 cornerPos = buildingCenter - new Vector3(vw * voxelSize * scale * 0.5f, 0f, vd * voxelSize * scale * 0.5f);

                                buildingInfos.Add(new BuildingInfo
                                {
                                    stassetPath = fullPath,
                                    worldOffset = cornerPos,
                                    voxelSize = voxelSize * scale,
                                    row = lb.row,
                                    col = lb.col,
                                    subIndex = i,
                                    buildingsInBlock = buildingCount
                                });
                            }
                        }
                    }
                }

                if (buildingInfos.Count == 0) continue;

                string sectorName = $"sector_{sr}_{sc}";
                var sectorData = BakeSector(sectorName, buildingInfos);
                if (sectorData != null)
                {
                    sectors.Add(sectorData);
                    RegisterSector(chunkManager, sectorData);
                }
            }

            Debug.Log($"[SectorBaker] Baked {sectors.Count} sectors with {sectorGroups.Count} total groups");
            return sectors;
        }
    }
}
