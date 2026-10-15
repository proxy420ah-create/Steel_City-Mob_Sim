using UnityEngine;

/// <summary>
/// D3D12's upload ring rejects a single SetData request larger than its
/// current segment — observed in the dev player: "upload buffer was too
/// small ... Requested: 49825008" — leaving whole sector voxel atlases and
/// occupancy grids unwritten on the GPU (draws ran, raymarched nothing).
/// Chunk every upload so no single call exceeds a safe budget.
/// Enforced by BuildReadinessAudit (raw .SetData call = WARN).
/// </summary>
public static class GpuUpload
{
    private const int MaxUploadBytesPerCall = 4 * 1024 * 1024;

    public static void SetData<T>(ComputeBuffer buf, T[] data) where T : struct
        => SetData(buf, data, 0, 0, data.Length);

    public static void SetData<T>(ComputeBuffer buf, T[] data, int managedStart, int computeStart, int count) where T : struct
    {
        int perCall = System.Math.Max(1, MaxUploadBytesPerCall / buf.stride);
        for (int done = 0; done < count; done += perCall)
            buf.SetData(data, managedStart + done, computeStart + done, System.Math.Min(perCall, count - done));
    }
}
