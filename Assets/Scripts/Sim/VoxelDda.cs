using System;

namespace SteelCity.Sim
{
    /// <summary>Read-only voxel source for <see cref="VoxelDda"/>. Struct implementations keep the traversal allocation- and delegate-free.</summary>
    public interface IVoxelSampler { uint At(int index); }

    public readonly struct UIntVoxels : IVoxelSampler
    {
        private readonly uint[] data;
        public UIntVoxels(uint[] data) { this.data = data; }
        public uint At(int index) => data[index];
    }

    public readonly struct ByteVoxels : IVoxelSampler
    {
        private readonly byte[] data;
        public ByteVoxels(byte[] data) { this.data = data; }
        public uint At(int index) => data[index];
    }

    /// <summary>First solid cell along a ray (cell space).</summary>
    public struct DdaHit
    {
        public int x, y, z;       // cell indices
        public int nx, ny, nz;    // entry-face normal (unit axis, pointing back toward the ray origin); all 0 when the origin is already inside a solid cell
        public double t;          // ray parameter at the entry face
        public uint voxel;        // raw voxel value
    }

    /// <summary>
    /// Pure 3D voxel-grid ray traversal (Amanatides-Woo DDA). Doubles in, ints out, no Unity types,
    /// so the same code serves terrain (byte grid), building volumes (packed uint grids) and, later,
    /// vehicles — and can be unit-tested outside the editor.
    ///
    /// Cell space: cell (i,j,k) spans [i,i+1) x [j,j+1) x [k,k+1); the grid is [0,w] x [0,h] x [0,d].
    /// The ray is origin + t * dir where dir is in cells per unit t (it need not be unit length): callers
    /// pass worldDir / voxelSize so that t stays in world units.
    ///
    /// Index layout and the solid test match the renderer: index = x + y*w + z*w*h
    /// (VoxelProxyRaymarch.shader VoxelIndex) and solid = (voxel &amp; 0x1FF) != 0 (VX_MATERIAL_MASK).
    /// Shape/rotation bits are ignored — every non-air voxel is treated as a full cube.
    /// Every axis is bounds-checked individually before the flat index is formed
    /// (VOXEL_ENGINE_GOTCHAS #1 — flat-index aliasing).
    /// </summary>
    public static class VoxelDda
    {
        /// <summary>Mirrors VX_MATERIAL_MASK in VoxelProxyRaymarch.shader.</summary>
        public const uint MaterialMask = 0x1FFu;

        public static bool Raycast(uint[] voxels, int w, int h, int d,
            double ox, double oy, double oz, double dx, double dy, double dz,
            double tMax, out DdaHit hit)
        {
            hit = default;
            if (voxels == null || (long)w * h * d > voxels.Length) return false;
            return Raycast(new UIntVoxels(voxels), w, h, d, ox, oy, oz, dx, dy, dz, tMax, out hit);
        }

        public static bool Raycast(byte[] voxels, int w, int h, int d,
            double ox, double oy, double oz, double dx, double dy, double dz,
            double tMax, out DdaHit hit)
        {
            hit = default;
            if (voxels == null || (long)w * h * d > voxels.Length) return false;
            return Raycast(new ByteVoxels(voxels), w, h, d, ox, oy, oz, dx, dy, dz, tMax, out hit);
        }

        public static bool Raycast<T>(T grid, int w, int h, int d,
            double ox, double oy, double oz, double dx, double dy, double dz,
            double tMax, out DdaHit hit) where T : struct, IVoxelSampler
        {
            hit = default;
            if (w <= 0 || h <= 0 || d <= 0 || !(tMax > 0.0)) return false;
            if (dx == 0.0 && dy == 0.0 && dz == 0.0) return false;

            // Clip the ray to the grid box; remember which face it entered through.
            double tNear = 0.0, tFar = tMax;
            int entryAxis = -1, entrySign = 0;
            if (!Slab(ox, dx, w, 0, ref tNear, ref tFar, ref entryAxis, ref entrySign)) return false;
            if (!Slab(oy, dy, h, 1, ref tNear, ref tFar, ref entryAxis, ref entrySign)) return false;
            if (!Slab(oz, dz, d, 2, ref tNear, ref tFar, ref entryAxis, ref entrySign)) return false;

            double px = ox + dx * tNear, py = oy + dy * tNear, pz = oz + dz * tNear;
            int ix = ClampCell(px, w), iy = ClampCell(py, h), iz = ClampCell(pz, d);

            // The cell the ray starts in (or first touches) can already be solid.
            uint v = grid.At(ix + iy * w + iz * w * h);
            if ((v & MaterialMask) != 0u)
            {
                hit.x = ix; hit.y = iy; hit.z = iz; hit.t = tNear; hit.voxel = v;
                if (entryAxis == 0) hit.nx = entrySign;
                else if (entryAxis == 1) hit.ny = entrySign;
                else if (entryAxis == 2) hit.nz = entrySign;
                return true;
            }

            int sx = dx > 0.0 ? 1 : (dx < 0.0 ? -1 : 0);
            int sy = dy > 0.0 ? 1 : (dy < 0.0 ? -1 : 0);
            int sz = dz > 0.0 ? 1 : (dz < 0.0 ? -1 : 0);
            double tDeltaX = sx != 0 ? Math.Abs(1.0 / dx) : double.PositiveInfinity;
            double tDeltaY = sy != 0 ? Math.Abs(1.0 / dy) : double.PositiveInfinity;
            double tDeltaZ = sz != 0 ? Math.Abs(1.0 / dz) : double.PositiveInfinity;
            double tMaxX = sx > 0 ? tNear + (ix + 1 - px) / dx : (sx < 0 ? tNear + (ix - px) / dx : double.PositiveInfinity);
            double tMaxY = sy > 0 ? tNear + (iy + 1 - py) / dy : (sy < 0 ? tNear + (iy - py) / dy : double.PositiveInfinity);
            double tMaxZ = sz > 0 ? tNear + (iz + 1 - pz) / dz : (sz < 0 ? tNear + (iz - pz) / dz : double.PositiveInfinity);

            // A ray crosses at most w+h+d cell boundaries inside the box.
            int steps = w + h + d + 3;
            for (int n = 0; n < steps; n++)
            {
                double t;
                int axis;
                if (tMaxX <= tMaxY && tMaxX <= tMaxZ) { t = tMaxX; axis = 0; }
                else if (tMaxY <= tMaxZ) { t = tMaxY; axis = 1; }
                else { t = tMaxZ; axis = 2; }
                if (t > tFar) return false;

                if (axis == 0) { ix += sx; tMaxX += tDeltaX; if ((uint)ix >= (uint)w) return false; }
                else if (axis == 1) { iy += sy; tMaxY += tDeltaY; if ((uint)iy >= (uint)h) return false; }
                else { iz += sz; tMaxZ += tDeltaZ; if ((uint)iz >= (uint)d) return false; }

                v = grid.At(ix + iy * w + iz * w * h);
                if ((v & MaterialMask) != 0u)
                {
                    hit.x = ix; hit.y = iy; hit.z = iz; hit.t = t; hit.voxel = v;
                    if (axis == 0) hit.nx = -sx;
                    else if (axis == 1) hit.ny = -sy;
                    else hit.nz = -sz;
                    return true;
                }
            }
            return false;
        }

        private static int ClampCell(double p, int size)
        {
            int i = (int)Math.Floor(p);
            return i < 0 ? 0 : (i >= size ? size - 1 : i);
        }

        /// <summary>Slab clip on one axis. Entering through the min face means the face normal points to -axis.</summary>
        private static bool Slab(double o, double dir, int size, int axis,
            ref double tNear, ref double tFar, ref int entryAxis, ref int entrySign)
        {
            if (dir == 0.0) return o >= 0.0 && o < size;
            double inv = 1.0 / dir;
            double t1 = (0.0 - o) * inv;
            double t2 = (size - o) * inv;
            int sign = -1;
            if (t1 > t2) { double tmp = t1; t1 = t2; t2 = tmp; sign = 1; }
            if (t1 > tNear) { tNear = t1; entryAxis = axis; entrySign = sign; }
            if (t2 < tFar) tFar = t2;
            return tNear <= tFar;
        }
    }
}
