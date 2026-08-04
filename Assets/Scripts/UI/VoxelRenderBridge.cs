using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

namespace SteelCity.Sim
{
    /// <summary>
    /// URP-safe voxel raymarch bridge.
    /// Instead of blitting to the camera output (which overwrites UI in URP),
    /// this creates a RawImage in the canvas that displays the raymarch RenderTexture.
    /// The RawImage is positioned to match the map camera's viewport rect.
    ///
    /// This approach:
    ///   - Never touches the screen buffer → UI stays visible
    ///   - Works in URP and Built-in RP
    ///   - Shows raymarched buildings ON TOP of mesh ground tiles, roads, NPCs
    ///   - Can be toggled on/off without camera disruption
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class VoxelRenderBridge : MonoBehaviour
    {
        public VoxelChunkManager chunkManager;

        private Camera _camera;
        private RawImage overlayImage;
        private bool initialized;

        void OnEnable()
        {
            _camera = GetComponent<Camera>();
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            if (overlayImage != null)
            {
                overlayImage.gameObject.SetActive(false);
            }
        }

        void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera) return;
            if (chunkManager == null) return;

            EnsureOverlayImage();

            // Dispatch compute shader → renders all chunks into colorRT
            chunkManager.RenderChunks();

            // Assign the raymarch result to the RawImage
            var result = chunkManager.GetColorTexture();
            if (result != null && overlayImage != null)
            {
                overlayImage.texture = result;
                overlayImage.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// Create or find a RawImage in the canvas that overlays the map viewport.
        /// </summary>
        private void EnsureOverlayImage()
        {
            if (initialized && overlayImage != null) return;

            // Find the main canvas
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[VoxelRenderBridge] No Canvas found in scene — cannot create raymarch overlay.");
                return;
            }

            // Create a child GameObject for the raymarch overlay
            var obj = new GameObject("RaymarchOverlay");
            obj.transform.SetParent(canvas.transform, false);

            var rt = obj.AddComponent<RectTransform>();
            var img = obj.AddComponent<RawImage>();

            // Position to match the map camera's viewport rect
            var camRect = _camera.rect;
            rt.anchorMin = new Vector2(camRect.xMin, camRect.yMin);
            rt.anchorMax = new Vector2(camRect.xMax, camRect.yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Raymarched buildings render on top of mesh ground tiles
            img.color = Color.white;
            img.raycastTarget = false; // Let clicks pass through to the map

            overlayImage = img;
            initialized = true;

            Debug.Log($"[VoxelRenderBridge] Created RawImage overlay at viewport rect {camRect}");
        }
    }
}
