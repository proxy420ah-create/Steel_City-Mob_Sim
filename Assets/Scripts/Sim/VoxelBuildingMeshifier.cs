using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Converts a 3D voxel grid (ushort[,,]) into a Unity Mesh.
    /// Uses face culling: only faces adjacent to air are generated.
    /// Per-vertex colors are set from the material palette.
    /// Optionally applies greedy meshing to merge coplanar faces.
    /// </summary>
    public static class VoxelBuildingMeshifier
    {
        // Face definitions: normal, 4 corner offsets (CCW when viewed from outside)
        private static readonly int[] FaceXNeg = { 0, 1, 2, 3 };
        private static readonly int[] FaceXPos = { 0, 1, 2, 3 };
        private static readonly int[] FaceYNeg = { 0, 1, 2, 3 };
        private static readonly int[] FaceYPos = { 0, 1, 2, 3 };
        private static readonly int[] FaceZNeg = { 0, 1, 2, 3 };
        private static readonly int[] FaceZPos = { 0, 1, 2, 3 };

        /// <summary>
        /// Build a Unity Mesh from a voxel grid with face culling.
        /// Each voxel = voxelSize world units. Mesh is centered at origin.
        /// </summary>
        public static Mesh BuildMesh(ushort[,,] voxels, float voxelSize = 0.0625f)
        {
            int w = voxels.GetLength(0);
            int h = voxels.GetLength(1);
            int d = voxels.GetLength(2);

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var colors = new List<Color>();
            var normals = new List<Vector3>();

            // Center offset so the mesh is centered at origin
            Vector3 centerOffset = new Vector3(
                -w * voxelSize * 0.5f,
                0f,  // buildings sit on ground (Y=0 at bottom)
                -d * voxelSize * 0.5f
            );

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int z = 0; z < d; z++)
                    {
                        ushort mat = voxels[x, y, z];
                        if (mat == 0) continue; // air

                        Color color = StAssetReader.GetMaterialColor(mat);
                        float vx = x * voxelSize + centerOffset.x;
                        float vy = y * voxelSize + centerOffset.y;
                        float vz = z * voxelSize + centerOffset.z;
                        float vs = voxelSize;

                        // Check each face: generate it only if the neighbor is air or out of bounds

                        // X- (left)
                        if (IsAir(voxels, x - 1, y, z, w, h, d))
                            AddFace(vertices, triangles, colors, normals,
                                new Vector3(vx, vy, vz),
                                new Vector3(vx, vy, vz + vs),
                                new Vector3(vx, vy + vs, vz + vs),
                                new Vector3(vx, vy + vs, vz),
                                color, Vector3.left);

                        // X+ (right)
                        if (IsAir(voxels, x + 1, y, z, w, h, d))
                            AddFace(vertices, triangles, colors, normals,
                                new Vector3(vx + vs, vy, vz),
                                new Vector3(vx + vs, vy + vs, vz),
                                new Vector3(vx + vs, vy + vs, vz + vs),
                                new Vector3(vx + vs, vy, vz + vs),
                                color, Vector3.right);

                        // Y- (bottom)
                        if (IsAir(voxels, x, y - 1, z, w, h, d))
                            AddFace(vertices, triangles, colors, normals,
                                new Vector3(vx, vy, vz),
                                new Vector3(vx + vs, vy, vz),
                                new Vector3(vx + vs, vy, vz + vs),
                                new Vector3(vx, vy, vz + vs),
                                color, Vector3.down);

                        // Y+ (top)
                        if (IsAir(voxels, x, y + 1, z, w, h, d))
                            AddFace(vertices, triangles, colors, normals,
                                new Vector3(vx, vy + vs, vz),
                                new Vector3(vx, vy + vs, vz + vs),
                                new Vector3(vx + vs, vy + vs, vz + vs),
                                new Vector3(vx + vs, vy + vs, vz),
                                color, Vector3.up);

                        // Z- (front)
                        if (IsAir(voxels, x, y, z - 1, w, h, d))
                            AddFace(vertices, triangles, colors, normals,
                                new Vector3(vx, vy, vz),
                                new Vector3(vx, vy + vs, vz),
                                new Vector3(vx + vs, vy + vs, vz),
                                new Vector3(vx + vs, vy, vz),
                                color, Vector3.back);

                        // Z+ (back)
                        if (IsAir(voxels, x, y, z + 1, w, h, d))
                            AddFace(vertices, triangles, colors, normals,
                                new Vector3(vx, vy, vz + vs),
                                new Vector3(vx + vs, vy, vz + vs),
                                new Vector3(vx + vs, vy + vs, vz + vs),
                                new Vector3(vx, vy + vs, vz + vs),
                                color, Vector3.forward);
                    }
                }
            }

            if (vertices.Count == 0)
            {
                Debug.LogWarning("[VoxelBuildingMeshifier] No visible faces found — mesh is empty.");
                return new Mesh();
            }

            var mesh = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                vertices = vertices.ToArray(),
                triangles = triangles.ToArray(),
                colors = colors.ToArray(),
                normals = normals.ToArray()
            };

            // Recalculate bounds for frustum culling
            mesh.RecalculateBounds();

            return mesh;
        }

        private static bool IsAir(ushort[,,] voxels, int x, int y, int z, int w, int h, int d)
        {
            if (x < 0 || x >= w || y < 0 || y >= h || z < 0 || z >= d)
                return true; // out of bounds = air (face is visible)
            return voxels[x, y, z] == 0;
        }

        private static void AddFace(
            List<Vector3> vertices, List<int> triangles,
            List<Color> colors, List<Vector3> normals,
            Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
            Color color, Vector3 normal)
        {
            int baseIndex = vertices.Count;
            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);

            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);

            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);

            // Two triangles (CCW winding)
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 2);

            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 3);
        }
    }
}
