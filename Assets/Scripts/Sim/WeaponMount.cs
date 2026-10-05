using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Welds a voxel item (voxel_items/*.json) to a character's hand attachment
    /// point. The item renders as its OWN raymarch volume via RegisterVolume —
    /// separate dims + voxelSize, never resampled into the character buffer.
    ///
    /// Direct port of voxel_editor.html attachItem():
    ///   itemWorld = posedHandWorld + yaw ∘ R ∘ B ∘ attachRot ∘ (voxel - grip)
    ///   R = cumulative FK rotation of the hand's owning group (CPU animator)
    ///   B = static rest-pose basis: item +X → forearm axis, item +Y → world up ⊥ arm
    ///   attachRot = the item file's attachRotation (author-baked correction)
    ///
    /// Coordinate convention: attachmentPoints are fractional centroids in
    /// INDEX space (voxel index = its center). Unity's raymarcher treats voxel
    /// i as cell [i*vs, (i+1)*vs] — so +0.5 is added when converting anchor
    /// points to world (it cancels inside item-space deltas). See
    /// docs/systems/WEAPON_ATTACHMENT_SYSTEM.md.
    /// </summary>
    [RequireComponent(typeof(VoxelCharacter))]
    public class WeaponMount : MonoBehaviour
    {
        public enum Hand { Right, Left }

        [Header("Item")]
        [Tooltip("Filename relative to StreamingAssets/voxel_items/")]
        public string itemFileName = "SW_Model_10.json";
        public Hand hand = Hand.Right;
        public bool equipOnStart = true;

        [Header("Debug")]
        public bool showGizmo = true;
        public bool debugLogs = true;
        [Tooltip("Render a long beam from the muzzle along the bore axis in Game view (via PathDebugRenderer)")]
        public bool showMuzzleRay = true;
        public float muzzleRayLength = 60f;
        public Color muzzleRayColor = new Color(1f, 0.15f, 0.15f, 0.9f);

        // Character data
        private VoxelCharacter character;
        private VoxelChunkManager chunkManager;
        private VoxelCharacterAnimator animator;
        private uint[] groupIDs;
        private Dictionary<int, Vector3> pivots = new Dictionary<int, Vector3>();
        private Vector3Int dims;
        private float voxelSize;
        private Vector3 handPoint;      // index-space centroid on the character
        private int handGid;

        // Item data
        private GameObject itemGO;
        private ComputeBuffer itemBuffer;
        private string itemVolumeName;
        private int itemDimX, itemDimY, itemDimZ;
        private float itemVoxelSize;
        private Vector3 gripPoint;      // index-space centroid on the item
        private Vector3 muzzlePoint;    // index-space; NaN when absent
        private Vector3 boreRootPoint;  // index-space breech end of bore axis; NaN when absent
        private Quaternion itemAttachRot = Quaternion.identity;

        // Static rest-pose basis (item +X → forearm axis) in character voxel space
        private Quaternion basisB = Quaternion.identity;

        private bool equipped = false;
        private Vector3 lastMuzzleWorld;
        private Vector3 lastAimDir = Vector3.forward;
        private string muzzleBeamKey; // per-instance PathDebugRenderer beam id
        private Vector3 boreAxisLocal = Vector3.right; // item-space bore axis; +X fallback when bore_root unpainted

        /// <summary>World-space muzzle position from the last weld update (projectile origin).</summary>
        public Vector3 MuzzleWorld => lastMuzzleWorld;
        /// <summary>World-space barrel direction — measured bore axis (muzzle−bore_root) when authored, else item +X convention.</summary>
        public Vector3 AimDirection => lastAimDir;
        public bool IsEquipped => equipped;

        // --- IK solver context (read-only, consumed by ArmAimSolver) ---
        public VoxelCharacterAnimator Animator => animator;
        public IReadOnlyDictionary<int, Vector3> Pivots => pivots;
        public Vector3Int Dims => dims;
        public float CharVoxelSize => voxelSize;
        public Vector3 HandPoint => handPoint;
        public int HandGid => handGid;
        public Quaternion BasisB => basisB;
        public Quaternion ItemAttachRot => itemAttachRot;
        public string ItemFileName => itemFileName;
        public Vector3 BoreAxisLocal => boreAxisLocal;

        IEnumerator Start()
        {
            character = GetComponent<VoxelCharacter>();
            while (!character.IsInitialized)
                yield return null;

            chunkManager = character.chunkManager != null
                ? character.chunkManager
                : FindFirstObjectByType<VoxelChunkManager>();
            if (chunkManager == null)
            {
                Debug.LogWarning("[WeaponMount] No VoxelChunkManager — item will not render.");
                yield break;
            }

            if (equipOnStart)
                Equip(itemFileName);
        }

        /// <summary>Load the item file and attach it to the configured hand.</summary>
        public void Equip(string fileName)
        {
            itemFileName = fileName;
            if (!LoadCharacterAttachData()) return;
            if (!LoadItem()) return;
            BuildRestBasis();
            equipped = true;
            if (debugLogs)
                Debug.Log($"[WeaponMount] Equipped {itemFileName} → {hand} hand (gid {handGid})");
        }

        public void Unequip()
        {
            equipped = false;
            var pdr = PathDebugRenderer.Instance;
            if (pdr != null && muzzleBeamKey != null) pdr.ClearCustomBeam(muzzleBeamKey);
            if (!string.IsNullOrEmpty(itemVolumeName) && chunkManager != null)
                chunkManager.UnregisterVolume(itemVolumeName);
            itemVolumeName = null;
            if (itemBuffer != null) { itemBuffer.Release(); itemBuffer = null; }
            if (itemGO != null) { Destroy(itemGO); itemGO = null; }
        }

        void OnDestroy() => Unequip();

        // ------------------------------------------------------------------
        // Loading
        // ------------------------------------------------------------------

        bool LoadCharacterAttachData()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "voxel_characters", character.assetFileName);
            if (!File.Exists(path))
            {
                Debug.LogError($"[WeaponMount] Character file not found: {path}");
                return false;
            }
            string json = File.ReadAllText(path);

            // Voxel geometry + group map (for forearm centroid + gid fallback)
            if (!CharacterJsonLoader.Load(path, out _, out uint[] groupIDs, out Dictionary<int, Vector3> pivots,
                    out string animParamsRaw, out _, out _))
            {
                return false;
            }
            this.groupIDs = groupIDs;
            this.pivots = pivots ?? new Dictionary<int, Vector3>();
            var d = character.Dims;
            dims = new Vector3Int(d.x, d.y, d.z);
            voxelSize = CharacterJsonLoader.ParseVoxelSize(json, character.voxelSize);

            var pts = CharacterJsonLoader.ParseAttachmentPoints(json);
            // Painted "pivot_N" attachment centroids override authored pivots —
            // solver (L1/L2 bone lengths) and weld both read this same dict.
            CharacterJsonLoader.ApplyPivotOverrides(this.pivots, pts, dims);
            string handKey = hand == Hand.Right ? "right_hand" : "left_hand";
            if (!pts.TryGetValue(handKey, out var ap))
            {
                Debug.LogWarning($"[WeaponMount] Character has no '{handKey}' attachment point — paint it in the editor.");
                return false;
            }
            handPoint = ap.pos;
            handGid = ap.gid >= 0 ? ap.gid : (hand == Hand.Right ? 9 : 8);
            if (ap.gid < 0 && groupIDs != null)
            {
                int gi = Mathf.RoundToInt(handPoint.x) + Mathf.RoundToInt(handPoint.y) * dims.x
                       + Mathf.RoundToInt(handPoint.z) * dims.x * dims.y;
                if (gi >= 0 && gi < groupIDs.Length && groupIDs[gi] > 0)
                    handGid = (int)groupIDs[gi];
            }

            // CPU animator for the pose chain (same params the GPU pose uses)
            string pivotsRaw = CharacterJsonLoader.ExtractPivotsRaw(json);
            if (animParamsRaw == null && pivotsRaw == null)
            {
                Debug.LogWarning("[WeaponMount] No animParams/pivots in character JSON — welding to rest pose only.");
                animator = null;
            }
            else
            {
                string animJson = "{\"format\":\"anim_params\",\"version\":1," +
                    "\"pivots\":" + (pivotsRaw ?? "{}") + "," +
                    "\"params\":" + (animParamsRaw ?? "{}") + "}";
                animator = VoxelCharacterAnimator.LoadFromAnimJson(animJson);
                // Share the (possibly pivot_N-overridden) pivot dict so the weld
                // animator uses the same joints as the solver and GPU pose.
                if (animator != null) animator.pivots = this.pivots;
            }
            return true;
        }

        bool LoadItem()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "voxel_items", itemFileName);
            if (!File.Exists(path))
            {
                Debug.LogError($"[WeaponMount] Item file not found: {path}");
                return false;
            }
            string json = File.ReadAllText(path);

            if (!CharacterJsonLoader.Load(path, out ushort[,,] voxels, out _, out _, out _, out _, out _) || voxels == null)
                return false;

            itemDimX = voxels.GetLength(0);
            itemDimY = voxels.GetLength(1);
            itemDimZ = voxels.GetLength(2);
            itemVoxelSize = CharacterJsonLoader.ParseVoxelSize(json, 0.005f);

            var pts = CharacterJsonLoader.ParseAttachmentPoints(json);
            string gripKey = hand == Hand.Right ? "grip_right" : "grip_left";
            if (!pts.TryGetValue(gripKey, out var grip))
            {
                Debug.LogWarning($"[WeaponMount] Item has no '{gripKey}' attachment point.");
                return false;
            }
            gripPoint = grip.pos;
            muzzlePoint = pts.TryGetValue("muzzle", out var m) ? m.pos : new Vector3(float.NaN, 0, 0);
            boreRootPoint = pts.TryGetValue("bore_root", out var br) ? br.pos : new Vector3(float.NaN, 0, 0);
            // Measured bore axis (bore_root → muzzle) when authored; +X convention otherwise.
            boreAxisLocal = Vector3.right;
            if (!float.IsNaN(muzzlePoint.x) && !float.IsNaN(boreRootPoint.x))
            {
                Vector3 axis = muzzlePoint - boreRootPoint;
                if (axis.sqrMagnitude > 1e-6f) boreAxisLocal = axis.normalized;
            }
            // attachRotation is authored for grip_right (primary-hand convention,
            // same as the editor's attachItem). A left-hand weld is the mirror
            // image — compensate with +180° roll about the grip axis.
            var e = CharacterJsonLoader.ParseEulerDeg(json, "attachRotation");
            if (hand == Hand.Left) e.x += 180f;
            itemAttachRot = Quaternion.Euler(e);

            // GPU buffer + registered volume on a dedicated GameObject
            UnequipVolumeOnly();
            int total = itemDimX * itemDimY * itemDimZ;
            var gpuData = new uint[total];
            int idx = 0;
            for (int z = 0; z < itemDimZ; z++)
                for (int y = 0; y < itemDimY; y++)
                    for (int x = 0; x < itemDimX; x++)
                        gpuData[idx++] = voxels[x, y, z];

            itemBuffer = new ComputeBuffer(total, sizeof(uint));
            itemBuffer.SetData(gpuData);

            itemGO = new GameObject($"Weapon_{Path.GetFileNameWithoutExtension(itemFileName)}");
            itemGO.transform.SetParent(transform, worldPositionStays: true);
            itemGO.transform.position = transform.position;

            itemVolumeName = $"item_{itemGO.GetInstanceID()}";
            chunkManager.RegisterVolume(itemVolumeName, itemGO, itemBuffer,
                itemDimX, itemDimY, itemDimZ, itemVoxelSize);
            return true;
        }

        void UnequipVolumeOnly()
        {
            if (!string.IsNullOrEmpty(itemVolumeName) && chunkManager != null)
                chunkManager.UnregisterVolume(itemVolumeName);
            itemVolumeName = null;
            if (itemBuffer != null) { itemBuffer.Release(); itemBuffer = null; }
            if (itemGO != null) { Destroy(itemGO); itemGO = null; }
        }

        // ------------------------------------------------------------------
        // Rest-pose basis: item +X (muzzle) points along the forearm,
        // item +Y is world-up projected perpendicular to the arm.
        // Ported from attachItem()'s B construction.
        // ------------------------------------------------------------------

        void BuildRestBasis()
        {
            Vector3 restDir = hand == Hand.Right ? Vector3.left : Vector3.right; // fallback axis guess

            if (groupIDs != null && pivots.TryGetValue(handGid, out Vector3 pv))
            {
                Vector3 pivotIdx = new Vector3(pv.x * dims.x, pv.y * dims.y, pv.z * dims.z);
                Vector3 centroid = Vector3.zero;
                int n = 0;
                for (int z = 0; z < dims.z; z++)
                    for (int y = 0; y < dims.y; y++)
                        for (int x = 0; x < dims.x; x++)
                        {
                            if (groupIDs[x + y * dims.x + z * dims.x * dims.y] == handGid)
                            {
                                centroid += new Vector3(x, y, z);
                                n++;
                            }
                        }
                if (n > 0)
                    restDir = centroid / n - pivotIdx;
                else
                    restDir = handPoint - pivotIdx;
            }

            if (restDir.sqrMagnitude < 1e-8f) restDir = hand == Hand.Right ? Vector3.left : Vector3.right;
            restDir.Normalize();

            // World-up projected onto the plane perpendicular to the arm
            Vector3 uAxis = Vector3.up - restDir * Vector3.Dot(restDir, Vector3.up);
            if (uAxis.sqrMagnitude < 1e-8f) uAxis = Vector3.forward;
            uAxis.Normalize();
            Vector3 rAxis = Vector3.Cross(restDir, uAxis); // item +Z column

            // Columns: +X→restDir, +Y→uAxis, +Z→rAxis → LookRotation(fwd=c2, up=c1)
            basisB = Quaternion.LookRotation(rAxis, uAxis);
        }

        // ------------------------------------------------------------------
        // Per-frame weld
        // ------------------------------------------------------------------

        void LateUpdate()
        {
            if (!equipped || itemGO == null || character == null) return;

            var ic = character.GetInstancedHandle();
            float state = ic?.animState ?? 0f;
            float time = ic?.animTime ?? 0f;
            float speed = ic?.animSpeed ?? 1f;

            // Posed hand point (voxel units, centered x/z) + cumulative group rotation
            Vector3 posed = animator != null
                ? animator.PosePoint(handPoint, handGid, dims, state, time, speed)
                : new Vector3(handPoint.x - dims.x * 0.5f, handPoint.y, handPoint.z - dims.z * 0.5f);

            Quaternion qR = Quaternion.identity;
            if (animator != null)
            {
                var gr = animator.ComputeGroupRotation(handGid, dims, 1.0f, state, time, speed);
                if (gr.HasValue && gr.Value.chain != null)
                    foreach (var e in gr.Value.chain)
                        qR = QuatFromMat3(e.rot) * qR;
            }

            // Hand world position: cell-space (+0.5) → rotate about volume center (shader convention)
            Vector3 half = new Vector3(dims.x, dims.y, dims.z) * voxelSize * 0.5f;
            Vector3 center = transform.position + half;
            Vector3 localCell = new Vector3(
                posed.x + dims.x * 0.5f + 0.5f,
                posed.y + 0.5f,
                posed.z + dims.z * 0.5f + 0.5f) * voxelSize;
            Quaternion yawRot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            Vector3 handWorld = center + yawRot * (localCell - half);

            // Item world rotation: yaw ∘ R ∘ B ∘ attachRot
            Quaternion qWorld = yawRot * qR * basisB * itemAttachRot;

            // Rendered item point world = GOpos + halfItem + qWorld·(p_local − halfItem).
            // Solve GOpos so the grip cell-center lands exactly on handWorld.
            Vector3 halfItem = new Vector3(itemDimX, itemDimY, itemDimZ) * itemVoxelSize * 0.5f;
            Vector3 gLocal = (gripPoint + Vector3.one * 0.5f) * itemVoxelSize;
            itemGO.transform.rotation = qWorld;
            itemGO.transform.position = handWorld - halfItem - qWorld * (gLocal - halfItem);

            // Muzzle world position + barrel direction (projectile origin later)
            Vector3 itemCenter = itemGO.transform.position + halfItem;
            if (!float.IsNaN(muzzlePoint.x))
            {
                Vector3 mLocal = (muzzlePoint + Vector3.one * 0.5f) * itemVoxelSize;
                lastMuzzleWorld = itemCenter + qWorld * (mLocal - halfItem);
            }
            else
            {
                lastMuzzleWorld = handWorld;
            }
            // Barrel direction: measured bore axis resolved at parse time (boreAxisLocal).
            lastAimDir = (qWorld * boreAxisLocal).normalized;

            // Live muzzle beam in Game view — true aim ray out of the barrel.
            var pdr = PathDebugRenderer.Instance;
            if (pdr != null)
            {
                if (muzzleBeamKey == null) muzzleBeamKey = $"muzzle_ray_{GetInstanceID()}";
                if (showMuzzleRay && !float.IsNaN(muzzlePoint.x))
                    pdr.SetCustomBeam(muzzleBeamKey, lastMuzzleWorld,
                        lastMuzzleWorld + lastAimDir * muzzleRayLength, 0.015f, muzzleRayColor);
                else
                    pdr.ClearCustomBeam(muzzleBeamKey);
            }
        }

        static Quaternion QuatFromMat3(float[,] m)
        {
            // Standard trace-based 3x3 → quaternion (columns = basis vectors)
            float tr = m[0, 0] + m[1, 1] + m[2, 2];
            float w, x, y, z;
            if (tr > 0f)
            {
                float s = Mathf.Sqrt(tr + 1f) * 2f;
                w = 0.25f * s;
                x = (m[2, 1] - m[1, 2]) / s;
                y = (m[0, 2] - m[2, 0]) / s;
                z = (m[1, 0] - m[0, 1]) / s;
            }
            else if (m[0, 0] > m[1, 1] && m[0, 0] > m[2, 2])
            {
                float s = Mathf.Sqrt(1f + m[0, 0] - m[1, 1] - m[2, 2]) * 2f;
                w = (m[2, 1] - m[1, 2]) / s;
                x = 0.25f * s;
                y = (m[0, 1] + m[1, 0]) / s;
                z = (m[0, 2] + m[2, 0]) / s;
            }
            else if (m[1, 1] > m[2, 2])
            {
                float s = Mathf.Sqrt(1f + m[1, 1] - m[0, 0] - m[2, 2]) * 2f;
                w = (m[0, 2] - m[2, 0]) / s;
                x = (m[0, 1] + m[1, 0]) / s;
                y = 0.25f * s;
                z = (m[1, 2] + m[2, 1]) / s;
            }
            else
            {
                float s = Mathf.Sqrt(1f + m[2, 2] - m[0, 0] - m[1, 1]) * 2f;
                w = (m[1, 0] - m[0, 1]) / s;
                x = (m[0, 2] + m[2, 0]) / s;
                y = (m[1, 2] + m[2, 1]) / s;
                z = 0.25f * s;
            }
            return new Quaternion(x, y, z, w).normalized;
        }

        void OnDrawGizmos()
        {
            if (!showGizmo || !equipped) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(lastMuzzleWorld, 0.01f);
            Gizmos.DrawLine(lastMuzzleWorld, lastMuzzleWorld + lastAimDir * 0.2f);
        }
    }
}
