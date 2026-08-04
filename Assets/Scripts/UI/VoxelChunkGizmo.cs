using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Draws a wireframe gizmo for a voxel chunk in the Scene view.
    /// Shows the chunk's voxel volume bounds so you can verify positioning.
    /// Attach is automatic via VoxelChunkManager.LoadChunk().
    /// </summary>
    public class VoxelChunkGizmo : MonoBehaviour
    {
        [Header("Chunk Bounds")]
        public int dimsX = 96;
        public int dimsY = 44;
        public int dimsZ = 96;
        public float voxelSize = 0.1f;

        public void Initialize(int x, int y, int z, float vSize)
        {
            dimsX = x; dimsY = y; dimsZ = z; voxelSize = vSize;
        }

        void OnDrawGizmos()
        {
            Vector3 size = new Vector3(dimsX, dimsY, dimsZ) * voxelSize;
            Vector3 center = transform.position + size * 0.5f;

            Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
            Gizmos.DrawWireCube(center, size);

            // Origin marker
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, voxelSize * 2f);
        }
    }
}
