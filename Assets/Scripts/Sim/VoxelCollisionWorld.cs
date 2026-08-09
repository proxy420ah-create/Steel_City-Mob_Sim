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

        public bool IsInitialized => initialized;

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
        }

        /// <summary>
        /// DDA raymarch downward from a world position. Returns ground hit info.
        /// This is the SteelTide VoxelWorld.RaymarchChunk pattern, simplified for
        /// straight-down ground probes.
        /// </summary>
        public bool ProbeGround(Vector3 worldPos, float maxDistance, out float groundY, out Vector3 normal)
        {
            groundY = 0f;
            normal = Vector3.up;

            // Convert world position to voxel grid coordinates
            Vector3 local = (worldPos - gridOrigin) / voxelSize;
            int vx = Mathf.FloorToInt(local.x);
            int vz = Mathf.FloorToInt(local.z);

            // Bounds check — probing outside the registered grid must fail
            // fast instead of falling through to the flat-array index math below,
            // which does NOT validate vx/vz and can alias into unrelated voxel
            // data at a completely different (bogus) height if left unchecked.
            if (vx < 0 || vx >= gridW || vz < 0 || vz >= gridD)
                return false;

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
                    // Hit solid voxel — ground is at the TOP of this voxel
                    groundY = gridOrigin.y + (vy + 1) * voxelSize;
                    normal = Vector3.up;
                    return true;
                }
                vy--;
                steps++;
            }

            return false; // No ground found within range
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
