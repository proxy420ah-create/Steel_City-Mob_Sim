using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SteelCity.Sim
{
    /// <summary>
    /// Unified debug HUD — single IMGUI window with tabs for all debug overlays.
    /// Replaces the scattered OnGUI() methods in FollowCamera and CityMap3D.
    ///
    /// Tabs: [Camera] [Render] [Clothing] [Path] [Keys]
    /// Toggle: Backquote (`) key or O key
    ///
    /// Auto-finds FollowCamera, VoxelChunkManager, CityMap3D, and ClothingSystem
    /// in the scene. Missing components just skip their tab. The Keys tab is always
    /// available and lists only the hotkeys whose handlers currently exist.
    /// </summary>
    public class DebugHUDManager : MonoBehaviour
    {
        public enum Tab { Camera, Render, Clothing, Path, Combat, Cover, Vision, Demolitions, Weapons, MatRes, BuildLog, Keys, Sfx, TanSuit, Forces }
        public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

        [Header("Config")]
        [SerializeField] private Tab defaultTab = Tab.Keys;
        [SerializeField] private bool showOnStart = true;
        [SerializeField] private float windowW = 560f;
        [SerializeField] private float windowH = 440f;
        [SerializeField] private float margin = 10f;

        private Corner currentCorner = Corner.TopLeft;

        private Tab activeTab;
        private bool visible = true;
        private bool minimized = false;
        private Vector2 scrollPos;
        private Texture2D rtProbeTex;   // BuildLog tab — proxyRT pixel probe

        // Drag state
        private bool isDragging;
        private Vector2 dragOffset;
        private Vector2 customPosition;
        private bool usingCustomPosition;

        // Canvas input blocker — transparent full-screen Image that captures raycasts
        // when the mouse is over the IMGUI panel, so IsPointerOverGameObject() returns true
        // and both 3D world clicks and Canvas UI clicks are blocked behind the panel.
        private GameObject blockerCanvasGO;
        private Image blockerImage;
        private Rect currentWindowRect;

        /// <summary>Static flag: true when mouse is currently over the debug panel.</summary>
        public static bool IsMouseOverPanel { get; private set; }

        // Cached references
        private FollowCamera followCamera;
        private VoxelChunkManager chunkManager;
        private CityMap3D cityMap;
        private GameUIController gameUI;
        private ClothingSystem clothingSystem; // currently selected
        private PathDebugRenderer pathDebug;

        // Combat tab state — set by clicking a range block while sim is Dwelling
        private string rangeBlockId;
        /// <summary>Cover pit opponent mode — Dummy: stands Idle (Vinny-only
        /// cover test); Hide: Tan seeks his first cover then parks the seam
        /// forever (`holdInCover` — the 3-stage-search test dummy); Fight:
        /// full symmetric duel — both combatants run the beat stack.</summary>
        private enum PitTanMode : byte { Dummy, Hide, Fight }
        private PitTanMode pitTanMode = PitTanMode.Fight;   // lab default — symmetric duel (was Dummy)
        /// Parity experiment — SETUP PIT assigns Vinny the NORTH plate and Tan
        /// the SOUTH. If "the south fighter" stumbles regardless of identity,
        /// the asymmetry lives in the geometry/field, not the character.
        private bool pitSwapSides = false;
        private string pitBlockId;
        private Dictionary<string, Block> pitScanSource;

        // ---- Scripted engagement director ---------------------------------
        // Each dropdown slot picks a cover POSITION (its zone centroid — the
        // middle of the painted gradient). Combat consumes slots in order;
        // filling the next slot is the "advance" order. See ENGAGEMENT_BEATS.
        private struct CoverSpot
        {
            public Vector3 stand;       // zone centroid — the feet target
            public string volId;
            public CoverSensor.WallClass cls;
            public float reqD;
            public bool usable;         // stand cell is Hidden under the current threat
        }
        private readonly List<CoverSpot> coverSpots = new List<CoverSpot>();
        private readonly List<CoverSpot> coverSpotsTan = new List<CoverSpot>();
        private bool openResScaleDrop;
        private bool showCoverSpots = true;
        private float nextCoverSpotRebuild;   // auto-refresh cadence while shown — verdicts stay live
        private static readonly Color[] SlotColors =
        {
            new Color(1f, .8f, .2f), new Color(1f, .4f, .3f),
            new Color(.5f, 1f, .5f), new Color(.9f, .5f, 1f),
            new Color(1f, 1f, .4f),  new Color(.4f, .8f, 1f),
            new Color(1f, .6f, .6f), new Color(.7f, 1f, .7f),
        };

        /// <summary>Every painted cover AREA = one post — the field's
        /// quality-layer zones (per volume/class/¼-duck band/face — uniform
        /// rectangles), each keyed on its band CENTROID — "the middle of the
        /// color gradient" — sorted nearest-first. Zones whose stand cell is
        /// not Hidden under the current threat still draw (dimmed): the
        /// geometric offer is real, the verdict is the threat layer's.</summary>
        private readonly List<CoverField.ZoneSpot> spotsScratch = new List<CoverField.ZoneSpot>(64);
        void RebuildCoverSpots(CoverSensor sensor, Vector3 fromPos, List<CoverSpot> into)
        {
            into.Clear();
            if (sensor == null) return;
            // Zones are threat-relative — the pickedAim→sensor.Threat sync
            // (every frame, tab-independent) owns threat establishment. This
            // only re-scans geometry + enumerates what the field knows.
            sensor.Refresh(true);
            sensor.Field.EnumerateZones(spotsScratch);
            foreach (var z in spotsScratch)
            {
                // Stand-cell verdict — the threat layer's gate on a quality
                // offer. Unusable zones still list so every band shows its post.
                bool usable = sensor.TryZoneAt(z.stand, out var zc, out float rd);
                into.Add(new CoverSpot
                {
                    stand = z.stand, volId = z.volId, usable = usable,
                    cls = usable ? zc : z.cls, reqD = usable ? rd : z.reqD
                });
            }
            into.Sort((a, b) =>
                (a.stand - fromPos).sqrMagnitude.CompareTo((b.stand - fromPos).sqrMagnitude));
        }

        int ScriptSlotAt(CombatController cc, Vector3 stand)
        {
            for (int i = 0; i < cc.scriptSlots.Count; i++)
                if (cc.scriptSlots[i].has && (cc.scriptSlots[i].stand - stand).sqrMagnitude < 0.04f) return i;
            return -1;
        }

        /// <summary>A post at every cover centroid — slot-assigned posts draw
        /// in that slot's color, thicker; usable spots cyan (vinny) / warm
        /// amber (tan); stands not Hidden under the current threat draw dim
        /// (real geometry, gated pick). `tag` suffixes the beam names so both
        /// fighters' sets coexist in the world.</summary>
        void PaintCoverSpots(CombatController cc, List<CoverSpot> spots, string tag, bool warm)
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr == null) return;
            for (int i = 0; i < 40; i++)
            {
                if (i >= spots.Count) { pdr.ClearCustomBeam($"cov{tag}_{i}"); continue; }
                var o = spots[i];
                int slot = cc != null ? ScriptSlotAt(cc, o.stand) : -1;
                if (slot >= 0)
                    pdr.SetCustomBeam($"cov{tag}_{i}", o.stand, o.stand + Vector3.up * 1.0f, 0.05f,
                        SlotColors[slot % SlotColors.Length]);
                else if (o.usable)
                    pdr.SetCustomBeam($"cov{tag}_{i}", o.stand, o.stand + Vector3.up * 0.8f, 0.03f,
                        warm ? new Color(0.95f, 0.62f, 0.3f, 0.8f)
                             : new Color(0.3f, 0.85f, 0.9f, 0.8f));
                else
                    pdr.SetCustomBeam($"cov{tag}_{i}", o.stand, o.stand + Vector3.up * 0.8f, 0.03f,
                        warm ? new Color(0.55f, 0.45f, 0.4f, 0.45f)
                             : new Color(0.5f, 0.5f, 0.55f, 0.45f));
            }
        }

        /// <summary>The scan-radius ring — dashed chords at `sensor.radius`
        /// around the fighter's feet: his cover-awareness bubble (vinny cyan,
        /// tan amber). Deliberately ground-hugging/dashed/dim so it can't be
        /// misread as the orbit standoff ring (taller, solid, brighter).</summary>
        void PaintRadiusRing(CoverSensor sensor, VoxelCharacter ch, string tag, bool warm)
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr == null) return;
            if (sensor == null || ch == null)
            { for (int bi = 0; bi < 24; bi++) pdr.ClearCustomBeam($"sen_rad{tag}{bi}"); return; }
            Vector3 c = CharFeetCenter(ch) + Vector3.up * 0.06f;
            float r = sensor.radius;
            var col = warm ? new Color(0.95f, 0.6f, 0.3f, 0.20f)
                           : new Color(0.3f, 0.85f, 0.9f, 0.20f);
            for (int bi = 0; bi < 24; bi++)
            {
                if ((bi & 1) == 1) { pdr.ClearCustomBeam($"sen_rad{tag}{bi}"); continue; }
                float a0 = bi * 15f * Mathf.Deg2Rad, a1 = (bi + 1) * 15f * Mathf.Deg2Rad;
                pdr.SetCustomBeam($"sen_rad{tag}{bi}",
                    c + new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * r,
                    c + new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * r, 0.015f, col);
            }
        }

        /// <summary>Feet square + cert square + committed-destination post +
        /// relocate beam — one fighter's set, `tag`-suffixed ("cov_dst",
        /// "cov_feet*", "cov_cert*", "scr_tgt" are vinny's legacy names;
        /// "_t" is tan's).</summary>
        /// Master gate for the fighter body-square/destination beam paint —
        /// off by default for clean inspection; re-enable from the cover fold.
        public bool fighterBeams = false;
        private bool fighterBeamsWasOn;

        void PaintFighterBeams(CombatController cc, VoxelCharacter ch, string tag)
        {
            var tpdr = PathDebugRenderer.Instance;
            if (tpdr == null) return;
            if (ch != null)
            {
                var fc = CharFeetCenter(ch) + Vector3.up * 0.04f;
                var gc = new Color(0.2f, 1.0f, 0.3f, 0.95f);
                // Physical body square — the column the mover actually sweeps;
                // grows/shrinks 1:1 with vc.footprintOverride.
                float h = Mathf.Max(0.05f, ch.BodyFootprint * 0.5f);
                var a = fc + new Vector3(-h, 0f, -h); var b = fc + new Vector3(h, 0f, -h);
                var c = fc + new Vector3(h, 0f, h);   var d = fc + new Vector3(-h, 0f, h);
                tpdr.SetCustomBeam($"cov_feet{tag}",  a, b, 0.035f, gc);
                tpdr.SetCustomBeam($"cov_feet2{tag}", b, c, 0.035f, gc);
                tpdr.SetCustomBeam($"cov_feet3{tag}", c, d, 0.035f, gc);
                tpdr.SetCustomBeam($"cov_feet4{tag}", d, a, 0.035f, gc);
                // Skin envelope — the TESTED square's outer edge (spec +
                // BodySkin). The band between green and this line IS the
                // standoff buffer — the body halts when the skin line kisses
                // a face, which is why the spec box floats ~6cm off walls.
                float hs = h + VoxelCollisionWorld.BodySkin;
                var sc = new Color(0.2f, 1.0f, 0.3f, 0.35f);
                var sa = fc + new Vector3(-hs, 0f, -hs); var sb = fc + new Vector3(hs, 0f, -hs);
                var scd = fc + new Vector3(hs, 0f, hs);  var sd = fc + new Vector3(-hs, 0f, hs);
                tpdr.SetCustomBeam($"cov_skin{tag}",  sa, sb, 0.018f, sc);
                tpdr.SetCustomBeam($"cov_skin2{tag}", sb, scd, 0.018f, sc);
                tpdr.SetCustomBeam($"cov_skin3{tag}", scd, sd, 0.018f, sc);
                tpdr.SetCustomBeam($"cov_skin4{tag}", sd, sa, 0.018f, sc);
                // Cert square — the K×K-cell block the map certifies. The gap
                // between the two outlines IS the grid-rounding padding; at a
                // commensurate cell size they collapse onto each other.
                float cert = cc != null ? cc.DebugCertMeters : 0f;
                if (cert > ch.BodyFootprint + 0.01f)
                {
                    float h2 = cert * 0.5f;
                    var dc = new Color(1.0f, 0.85f, 0.2f, 0.5f);
                    var a2 = fc + new Vector3(-h2, 0f, -h2); var b2 = fc + new Vector3(h2, 0f, -h2);
                    var c2 = fc + new Vector3(h2, 0f, h2);   var d2 = fc + new Vector3(-h2, 0f, h2);
                    tpdr.SetCustomBeam($"cov_cert{tag}",  a2, b2, 0.02f, dc);
                    tpdr.SetCustomBeam($"cov_cert2{tag}", b2, c2, 0.02f, dc);
                    tpdr.SetCustomBeam($"cov_cert3{tag}", c2, d2, 0.02f, dc);
                    tpdr.SetCustomBeam($"cov_cert4{tag}", d2, a2, 0.02f, dc);
                }
                else for (int i = 1; i <= 4; i++) tpdr.ClearCustomBeam($"cov_cert{(i > 1 ? i.ToString() : "")}{tag}");
            }
            else
            {
                for (int i = 1; i <= 4; i++)
                {
                    tpdr.ClearCustomBeam($"cov_feet{(i > 1 ? i.ToString() : "")}{tag}");
                    tpdr.ClearCustomBeam($"cov_cert{(i > 1 ? i.ToString() : "")}{tag}");
                    tpdr.ClearCustomBeam($"cov_skin{(i > 1 ? i.ToString() : "")}{tag}");
                }
            }
            bool live = cc != null && ch != null && cc.State != CombatController.CombatState.Idle;
            if (live)
            {
                var cp = cc.CoverPoint;
                tpdr.SetCustomBeam($"cov_dst{tag}", cp, cp + Vector3.up * 1.0f, 0.05f,
                    new Color(1.0f, 0.6f, 0.1f, 0.95f));
                if (cc.Relocating)
                    tpdr.SetCustomBeam($"scr_tgt{tag}",
                        CharFeetCenter(ch) + Vector3.up * 0.1f, cp + Vector3.up * 0.1f, 0.02f, Color.yellow);
                else tpdr.ClearCustomBeam($"scr_tgt{tag}");
            }
            else
            {
                tpdr.ClearCustomBeam($"cov_dst{tag}");
                tpdr.ClearCustomBeam($"scr_tgt{tag}");
            }
        }

        void ClearCoverSpots()
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr == null) return;
            for (int i = 0; i < 40; i++) { pdr.ClearCustomBeam($"cov_{i}"); pdr.ClearCustomBeam($"cov_t_{i}"); }
            for (int bi = 0; bi < 24; bi++) { pdr.ClearCustomBeam($"sen_rad{bi}"); pdr.ClearCustomBeam($"sen_rad_t{bi}"); }
            pdr.ClearCustomBeam("scr_tgt");
            pdr.ClearCustomBeam("scr_tgt_t");
        }

        /// <summary>The block carrying cover_stand_a — found by probing every
        /// laid block's attachment points once, re-derived if the block set
        /// changes (city rebuild). The pit is a first-class testbed: the menu
        /// stages it without an order, dwell state, or map click.</summary>
        private string PitBlockId
        {
            get
            {
                var blocks = cityMap.CachedBlocks;
                if (pitScanSource != blocks) { pitScanSource = blocks; pitBlockId = null; }
                if (pitBlockId == null && blocks != null)
                    foreach (var id in blocks.Keys)
                        if (cityMap.TryGetBuildingPointLocal(id, "cover_stand_a", out _)) { pitBlockId = id; break; }
                return pitBlockId;
            }
        }

        /// <summary>Selected firing lane (1=left, 2=center, 3=right). Read by
        /// GameUIController when resolving per-lane point names for
        /// test_week orders.</summary>
        public static int RangeLane = 2;
        /// <summary>When true (lane 3 only), the aim point is the glass mold's
        /// metal core ("glass_target") instead of the lane's firing_target.</summary>
        public static bool RangeAimMold = false;
        private ArmAimSolver solver;
        private bool blockClickHooked;

        // Free-fire picks — voxel clicks armed from the Combat/Demolitions tabs. PICK STAND
        // teleports the character (DebugSnapTo) onto the hit surface; PICK AIM
        // overrides the lane/mold aim point so shots can target anything.
        private enum PickSlot { None, Stand, Aim, Blast, Building, Inspect }
        private PickSlot pickArmed = PickSlot.None;
        private Vector3? pickedStand, pickedAim;
        private CoverSensor coverSensor;   // Vinny's always-on cover awareness (COVER tab)
        private VisionSensor visionSensor; // Vinny's vision sense (VISION tab)
        private string inspectHud;                             // last voxel-inspect report (HUD)
        private VoxelCollisionWorld World => world != null ? world : (world = cityMap != null ? cityMap.GetComponent<VoxelCollisionWorld>() : null);
        private VoxelCollisionWorld world;
        // Demolition bench — surface pick + inward depth push puts the blast inside.
        private Vector3? pickedBlast;
        private Vector3 blastNormal;
        private string blastVolume;
        private bool blastAtVolumeCenter;
        private float blastDepth = 0.8f, blastRadius = 6f, blastPower = 4f;

        // Optional test/debug components — used by the Keys tab to show only
        // hotkeys whose handlers actually exist in the scene right now.
        private VehicleTestSpawner vehicleTest;
        private StressTestSpawner stressTest;
        private StressTestDiagnostics stressDiag;
        private HoodSpawner hoodSpawner;
        private bool testRigPresent;

        // Clothing system multi-instance tracking
        private List<ClothingSystem> allClothingSystems = new();
        private int selectedClothingIndex = 0;
        private float clothingListRefreshTimer = 0f;
        /// <summary>Cached for DrawSfxTab — FindFirstObjectByType is a full scene
        /// scan and OnGUI fires several events per frame, so an inline find there
        /// was a sustained 40fps cap the moment the tab opened.</summary>
        private AudioListener sfxListener;
        private float sfxListenerRetryAt;
        private AudioListener ResolveSfxListener()
        {
            if (sfxListener != null) return sfxListener;
            if (Time.unscaledTime < sfxListenerRetryAt) return null;   // bounded null-retry — no per-event scene scan
            sfxListenerRetryAt = Time.unscaledTime + 1f;
            return sfxListener = FindFirstObjectByType<AudioListener>();
        }

        // --- Section foldouts — the HUD frame tax is widgets x GUI events, so a
        // collapsed section drawing 1 toggle instead of N controls is the lever.
        // State persists for the session; new sections follow secDefaultOpen.
        private readonly Dictionary<string, bool> secOpen = new Dictionary<string, bool>();
        private bool secDefaultOpen = true;
        private bool SecFold(string key, string title)
        {
            bool open = secOpen.TryGetValue(key, out bool o) ? o : secDefaultOpen;
            bool next = GUILayout.Toggle(open, (open ? "▼ " : "▶ ") + title, toggleStyle);
            if (next != open) secOpen[key] = next;
            return next;
        }
        private void SecSetAll(bool open)
        {
            secDefaultOpen = open;
            var keys = new List<string>(secOpen.Keys);
            foreach (var k in keys) secOpen[k] = open;
        }

        // Tan Suit resolved at most once/sec — FindRig scans every CharacterRig;
        // Combat/TanSuit tabs each called it several times per IMGUI event.
        private VoxelCharacter cachedTanSuit;
        private float tanSuitRefAt;
        private VoxelCharacter TanSuitRef
        {
            get
            {
                if (cachedTanSuit == null || Time.unscaledTime >= tanSuitRefAt)
                {
                    cachedTanSuit = FindCharacter("Civilian_02");
                    tanSuitRefAt = Time.unscaledTime + 1f;
                }
                return cachedTanSuit;
            }
        }

        // Character rig multi-instance tracking (for hotkey control routing)
        private List<CharacterRig> allCharacterRigs = new();
        private int selectedRigIndex = 0;

        // Cached styles
        private GUIStyle labelStyle;
        private GUIStyle toggleStyle;
        private GUIStyle boldStyle;
        private GUIStyle tabStyle;
        private GUIStyle tabActiveStyle;
        private GUIStyle bgStyle;
        private Texture2D bgTex;
        private Texture2D tabActiveTex;
        private Texture2D tabNormalTex;

        // Cached perf data for Render tab
        private float fps;
        private float frameTimeMs;
        private float frameTimeMin, frameTimeMax, frameTimeAvg;
        private float fpsAccumTime;
        private int fpsAccumFrames;

        // IMGUI cost control — OnGUI fires once per event (Layout + Repaint +
        // every input event), so each widget runs several times per frame and
        // scene-scanning every event is what tanked the frame rate. Text-only
        // tabs rebuild into ONE cached rich-text string at ~4 Hz; interactive
        // tabs (Clothing/Range) keep live widgets.
        private readonly System.Text.StringBuilder hudSB = new();
        private Texture2D seamRadarTex;          // cached — repainted on Repaint events only
        private readonly Color32[] seamRadarPx = new Color32[104 * 104];
        private bool seamPanelOpen = true;       // the standalone SEAM window (bottom-left)
        private Vector2 seamScrollPos;           // SEAM window's internal scroll
        private string tabText;
        private string footerText;
        private Tab lastBuiltTab = (Tab)(-1);
        private int matResSub;                                 // MatRes sub-tab: 0 building / 1 vehicle / 2 item
        private int matResSel = -1;                            // MatRes selected class (click a name) — promote-to-authored target
        private float nextTabRefresh;
        private float nextFooterRefresh;
        private float nextRefsRefresh;

        void Awake()
        {
            activeTab = defaultTab;
            visible = showOnStart;
            minimized = false;
            SetupBlockerCanvas();
            BuildDiag.Event("DebugHUDManager.Awake");
        }

        void Start()
        {
            RefreshReferences();
            RefreshClothingSystems();
        }

        /// <summary>
        /// Create a hidden Canvas with a transparent Image sized to match the IMGUI panel.
        /// When enabled, it captures raycasts so IsPointerOverGameObject() returns true
        /// for clicks inside the panel area — blocking both 3D world clicks and Canvas UI.
        /// </summary>
        void SetupBlockerCanvas()
        {
            blockerCanvasGO = new GameObject("DebugHUD_BlockerCanvas");
            blockerCanvasGO.transform.SetParent(transform);

            var canvas = blockerCanvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999; // Above game UI

            blockerCanvasGO.AddComponent<CanvasScaler>();
            blockerCanvasGO.AddComponent<GraphicRaycaster>();

            var imgGO = new GameObject("BlockerImage");
            imgGO.transform.SetParent(blockerCanvasGO.transform, false);
            blockerImage = imgGO.AddComponent<Image>();
            blockerImage.color = new Color(0, 0, 0, 0); // Fully transparent
            blockerImage.raycastTarget = true;

            // Anchor at bottom-left so position matches screen-space (0,0 = bottom-left)
            var rt = imgGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;

            blockerCanvasGO.SetActive(false);
        }

        /// Returns true when every reference resolved — a null ref left behind
        /// means an absent scene object (test rigs etc.) that callers should
        /// retry on a slow cadence rather than rescanning the scene rapidly.
        bool RefreshReferences()
        {
            if (followCamera == null) followCamera = FindFirstObjectByType<FollowCamera>();
            if (chunkManager == null) chunkManager = FindFirstObjectByType<VoxelChunkManager>();
            if (cityMap == null) cityMap = FindFirstObjectByType<CityMap3D>();
            if (gameUI == null) gameUI = FindFirstObjectByType<GameUIController>();
            if (cityMap != null && !blockClickHooked)
            {
                cityMap.OnBlockClicked += HandleBlockClicked;
                cityMap.OnVoxelPicked += HandleVoxelPicked;
                blockClickHooked = true;
            }
            if (pathDebug == null) pathDebug = FindFirstObjectByType<PathDebugRenderer>();
            if (vehicleTest == null) vehicleTest = FindFirstObjectByType<VehicleTestSpawner>();
            if (stressTest == null) stressTest = FindFirstObjectByType<StressTestSpawner>();
            if (stressDiag == null) stressDiag = FindFirstObjectByType<StressTestDiagnostics>();
            if (hoodSpawner == null) hoodSpawner = FindFirstObjectByType<HoodSpawner>();
            return followCamera != null && chunkManager != null && cityMap != null
                && gameUI != null && pathDebug != null && vehicleTest != null
                && stressTest != null && stressDiag != null && hoodSpawner != null;
        }

        void RefreshClothingSystems()
        {
            allClothingSystems.Clear();
            var found = FindObjectsByType<ClothingSystem>(FindObjectsSortMode.None);
            foreach (var cs in found)
                if (cs != null && cs.gameObject != null)
                    allClothingSystems.Add(cs);

            // Clamp selection
            if (selectedClothingIndex >= allClothingSystems.Count)
                selectedClothingIndex = Mathf.Max(0, allClothingSystems.Count - 1);

            // Set active clothing system
            clothingSystem = (allClothingSystems.Count > 0) ? allClothingSystems[selectedClothingIndex] : null;

            // Also refresh CharacterRigs
            allCharacterRigs.Clear();
            var rigs = FindObjectsByType<CharacterRig>(FindObjectsSortMode.None);
            foreach (var r in rigs)
                if (r != null && r.gameObject != null)
                    allCharacterRigs.Add(r);
            if (selectedRigIndex >= allCharacterRigs.Count)
                selectedRigIndex = Mathf.Max(0, allCharacterRigs.Count - 1);

            // Optional single-key test rigs — any one present means its keys are live
            testRigPresent = FindFirstObjectByType<ForwardTransformTestRig>() != null
                          || FindFirstObjectByType<AnimationTestSpawner>() != null
                          || FindFirstObjectByType<CharacterTestRig>() != null;
        }

        bool TabAvailable(Tab t) => t switch
        {
            Tab.Camera => followCamera != null,
            Tab.Render => chunkManager != null,
            Tab.Clothing => allClothingSystems.Count > 0,
            Tab.Path => pathDebug != null,
            Tab.Combat or Tab.Cover or Tab.Vision or Tab.Demolitions or Tab.TanSuit or Tab.Forces => cityMap != null,
            Tab.Sfx => true,
            _ => true,
        };

        System.Collections.Generic.List<Tab> GetAvailableTabs()
        {
            var tabs = new System.Collections.Generic.List<Tab>();
            foreach (Tab t in System.Enum.GetValues(typeof(Tab)))
                if (TabAvailable(t)) tabs.Add(t);
            return tabs;
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;

            if (kb.backquoteKey.wasPressedThisFrame || kb.oKey.wasPressedThisFrame)
            {
                visible = !visible;
            }

            // Remaining HUD keys only apply while the panel is visible
            if (visible)
            {
                // Tab cycling — only cycle through available tabs
                if (kb.tabKey.wasPressedThisFrame)
                {
                    RefreshReferences();
                    RefreshClothingSystems();
                    var avail = GetAvailableTabs();
                    if (avail.Count > 0)
                    {
                        int idx = avail.IndexOf(activeTab);
                        if (idx < 0) idx = 0;
                        idx = (idx + 1) % avail.Count;
                        activeTab = avail[idx];
                    }
                }

                // Corner cycling with Y key (resets custom position)
                if (kb.yKey.wasPressedThisFrame)
                {
                    currentCorner = (Corner)(((int)currentCorner + 1) % 4);
                    usingCustomPosition = false;
                }

                // Cover sensor threat sync — the PICK AIM point is the shared
                // threat context for both combat and cover inspection.
                // Resolve the sensor fresh: this channel must flow regardless
                // of which tab is open — threat exists upstream of combat.
                if (coverSensor == null && cityMap != null && cityMap.SpawnedCharacter != null)
                    coverSensor = cityMap.SpawnedCharacter.GetComponent<CoverSensor>()
                        ?? cityMap.SpawnedCharacter.gameObject.AddComponent<CoverSensor>();
                if (coverSensor != null)
                {
                    // Ownership split: pickedAim is the threat channel while
                    // he's NOT in combat; in combat the controller feeds the
                    // sensor the threat's live feet — don't fight that feed.
                    var cvc = coverSensor.GetComponent<CombatController>();
                    bool inCombat = cvc != null && cvc.State != CombatController.CombatState.Idle;
                    if (!inCombat && coverSensor.Threat != pickedAim)
                    {
                        coverSensor.Threat = pickedAim;
                        coverSensor.Refresh(true);
                    }
                }
                var vinCc = cityMap != null && cityMap.SpawnedCharacter != null
                    ? cityMap.SpawnedCharacter.GetComponent<CombatController>() : null;
                bool vinFighting = vinCc != null
                    && vinCc.State != CombatController.CombatState.Idle;
                // Vision testbed: the same pick point is "the thing he's
                // looking at" — out of combat only; inside combat the
                // controller's swing dwell feeds the cone the threat's live
                // feet (the same ownership split as Sensor.Threat).
                if (visionSensor != null && !vinFighting) visionSensor.SetTarget(pickedAim);

                // Live marker pair for the search testbed:
                //   ORANGE `pick_aim`  = where Tan's feet ACTUALLY are (truth)
                //   MAGENTA `last_seen` = where Vinny BELIEVES he is
                //     (CombatController.ThreatPos — the belief channel: entry
                //     sighting + probe refresh, never live feet) plus a
                //     direction beam Vinny→belief — the slerp arc the
                //     45°/90° hops will rotate around. Verify: markers
                //     overlap while he's visible, diverge once he hides.
                // Per-fighter live combat viz — each duel participant gets
                // the same marker set so cover choices and orbit patterns
                // compare on screen at once:
                //   ORANGE `pick_aim{tag}`   = where the OPPONENT's feet
                //                            actually are (truth)
                //   MAGENTA `last_seen{tag}` = where THIS fighter believes
                //                            him (ThreatPos) + beam to it
                //   WHITE  `search_ref{tag}` = orbit 0° — bearing at the miss
                //   YELLOW `search_aim{tag}` = GOAL spoke (origin + 90°·side)
                //   AMBER  `search_buf{tag}*`= standoff ring at bufferRadius
                //   GREEN  `search_pick{tag}`= anchor → this hop's stand
                //   CYAN   `search_leg{tag}*`= the routed corridor polyline
                //   post `search_tgt{tag}`   = hop destination (cyan → orange
                //                            as progress → 90°)
                // Vinny keeps the legacy beam names (tag ""); Tan's carry "_t"
                // with a dimmed palette so overlapping sets stay readable.
                var tanCh0 = TanSuitRef;
                var tanCc0 = tanCh0 != null ? tanCh0.GetComponent<CombatController>() : null;
                var vinCh0 = cityMap != null ? cityMap.SpawnedCharacter : null;
                PaintSearchViz(vinCc, vinCh0, tanCh0, "", 1f);
                PaintSearchViz(tanCc0, tanCh0, vinCh0, "_t", 0.72f);

                // Minimize toggle with M key
                if (kb.mKey.wasPressedThisFrame)
                {
                    minimized = !minimized;
                }

                // Lock toggle with L key — locked tabs render as cached text
                if (kb.lKey.wasPressedThisFrame)
                {
                    hudLocked = !hudLocked;
                }
            }

            // Periodically refresh clothing system list (every 1 second).
            // Gated on visible: the refresh runs 2 FindObjectsByType + 3
            // FindFirstObjectByType scene scans — doing that once per second
            // while the HUD is hidden was a periodic frame spike.
            if (visible)
            {
                clothingListRefreshTimer += Time.deltaTime;
                if (clothingListRefreshTimer >= 1f)
                {
                    clothingListRefreshTimer = 0f;
                    RefreshClothingSystems();
                    sfxListener = FindFirstObjectByType<AudioListener>();
                }
            }

            // Update IsMouseOverPanel flag using current mouse position vs panel rect.
            // currentWindowRect is stored in screen-space (bottom-left origin) in OnGUI.
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && visible)
            {
                Vector2 mp = mouse.position.ReadValue();
                IsMouseOverPanel = currentWindowRect.Contains(mp);

                if (blockerCanvasGO != null)
                {
                    bool shouldBlock = IsMouseOverPanel;
                    if (blockerCanvasGO.activeSelf != shouldBlock)
                        blockerCanvasGO.SetActive(shouldBlock);

                    if (shouldBlock)
                    {
                        // Position the blocker Image to exactly match the panel in screen space
                        var rt = blockerImage.GetComponent<RectTransform>();
                        rt.anchoredPosition = new Vector2(currentWindowRect.x, currentWindowRect.y);
                        rt.sizeDelta = new Vector2(currentWindowRect.width, currentWindowRect.height);
                    }
                }
            }
            else
            {
                IsMouseOverPanel = false;
                if (blockerCanvasGO != null && blockerCanvasGO.activeSelf)
                    blockerCanvasGO.SetActive(false);
            }
        }

        void OnDisable()
        {
            IsMouseOverPanel = false;
            if (blockerCanvasGO != null)
                blockerCanvasGO.SetActive(false);
        }

        void OnDestroy()
        {
            if (cityMap != null && blockClickHooked)
            {
                cityMap.OnBlockClicked -= HandleBlockClicked;
                cityMap.OnVoxelPicked -= HandleVoxelPicked;
            }
            if (blockerCanvasGO != null)
                DestroyImmediate(blockerCanvasGO);
        }

        void LateUpdate()
        {
            // FPS tracking
            fpsAccumTime += Time.unscaledDeltaTime;
            fpsAccumFrames++;
            if (fpsAccumTime >= 0.5f)
            {
                fps = fpsAccumFrames / fpsAccumTime;
                fpsAccumTime = 0f;
                fpsAccumFrames = 0;
            }
            frameTimeMs = Time.unscaledDeltaTime * 1000f;
            frameTimeMin = Mathf.Min(frameTimeMin > 0 ? frameTimeMin : frameTimeMs, frameTimeMs);
            frameTimeMax = Mathf.Max(frameTimeMax, frameTimeMs);
            frameTimeAvg = frameTimeAvg > 0 ? frameTimeAvg * 0.95f + frameTimeMs * 0.05f : frameTimeMs;
        }

        void InitStyles()
        {
            if (labelStyle != null) return;

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                richText = true,
                normal = { textColor = new Color(0.88f, 0.92f, 1f) }
            };

            boldStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = true,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.1f, 1f, 1f) }
            };

            // Toggles need the skin's toggle style — a label style renders the row
            // clickable but with no checkbox glyph.
            toggleStyle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 12,
                richText = true,
                normal = { textColor = new Color(0.88f, 0.92f, 1f) },
                onNormal = { textColor = new Color(0.88f, 0.92f, 1f) }
            };

            bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, new Color(0.06f, 0.06f, 0.10f, 0.90f));
            bgTex.Apply();

            bgStyle = new GUIStyle(GUI.skin.box) { normal = { background = bgTex } };

            tabActiveTex = new Texture2D(1, 1);
            tabActiveTex.SetPixel(0, 0, new Color(0.15f, 0.35f, 0.65f, 0.95f));
            tabActiveTex.Apply();

            tabNormalTex = new Texture2D(1, 1);
            tabNormalTex.SetPixel(0, 0, new Color(0.12f, 0.12f, 0.18f, 0.80f));
            tabNormalTex.Apply();

            tabStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                normal = { background = tabNormalTex, textColor = new Color(0.6f, 0.7f, 0.8f) },
                hover = { background = tabNormalTex, textColor = new Color(0.8f, 0.9f, 1f) },
                active = { background = tabNormalTex },
                padding = new RectOffset(8, 8, 4, 4)
            };

            tabActiveStyle = new GUIStyle(tabStyle)
            {
                normal = { background = tabActiveTex, textColor = Color.white },
                hover = { background = tabActiveTex, textColor = Color.white }
            };
        }

        // --- HUD self-profile: per-frame OnGUI cost + event count, printed in
        // the sections row. Settles the "is it widgets?" question on the wire —
        // a folded tab showing hud≈0ms while FPS is still low means the cost is
        // elsewhere; hud≈15ms confirms the widget×event tax.
        private int hudProfFrame = -1, hudProfEvents, hudProfEventsShown;
        private float hudProfMs, hudProfMsShown, hudProfPeak, hudProfPeakShown;
        private bool hudLite;   // suppress tab body — isolates native GUI draw cost
        /// <summary>Locked = cheap mode: every tab renders as ONE cached label
        /// rebuilt ~4Hz (the pattern the text tabs prove at 120fps). Unlock to
        /// tune, lock to verify at full framerate — the state mirror stays live
        /// so effects are observable while locked. [L] toggles.</summary>
        private bool hudLocked = true;
        private string lockedTabText;

        void OnGUI()
        {
            // Timestamp, not `Stopwatch.StartNew()` — that allocated an object per IMGUI event.
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            OnGUIInner();
            float ms = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            if (hudProfFrame != Time.frameCount)
            {
                hudProfMsShown = hudProfMs; hudProfEventsShown = hudProfEvents;
                hudProfPeakShown = hudProfPeak;
                hudProfMs = 0f; hudProfEvents = 0; hudProfPeak = 0f;
                hudProfFrame = Time.frameCount;
            }
            hudProfMs += ms; hudProfEvents++;
            if (ms > hudProfPeak) hudProfPeak = ms;
        }

        void OnGUIInner()
        {
            // Mouse-flood events FIRST. The Editor queues dozens of MouseMove/MouseDrag events per
            // frame and `PreUpdate.IMGUISendQueuedEvents` replays every one through every OnGUI. The
            // ticker + combat bubbles below (GetComponent, string building, WorldToScreenPoint, a
            // GUILayout text layout) used to run for EACH of them before the filter further down —
            // measured at ~47 % of an 81 ms frame mid-fight. Same predicate as the later early-out
            // (those events never paint and the window ignores them), so nothing is lost; nothing
            // layout-related is consumed on a skipped event, so GUILayout stays aligned.
            var ev0 = Event.current;
            if (ev0 != null && (ev0.type == EventType.MouseMove
                || (ev0.type == EventType.MouseDrag && !isDragging && GUIUtility.hotControl == 0)))
                return;
            InitStyles();
            DrawModeTicker();   // bottom banner — persists while a V-key debug view is on, even with the panel hidden
            DrawCombatBubbles();   // in-world fighter bubbles — same always-on overlay contract
            if (!visible) return;
            // FindFirstObjectByType scans the scene — 8 of them per IMGUI
            // event (~4-6 events/frame) was a measurable stall. Throttled.
            // Missing refs (absent test rigs) retry on a slow cadence — they
            // only matter if such a rig spawns mid-session.
            if (Time.unscaledTime >= nextRefsRefresh)
            {
                bool complete = RefreshReferences();
                nextRefsRefresh = Time.unscaledTime + (complete ? 1f : 3f);
            }

            // Position window — custom drag position or corner snap
            float sw = Screen.width;
            float sh = Screen.height;
            float x, y;
            if (usingCustomPosition)
            {
                x = customPosition.x;
                y = customPosition.y;
            }
            else
            {
                switch (currentCorner)
                {
                    case Corner.TopLeft:     x = margin;       y = margin;       break;
                    case Corner.TopRight:    x = sw - windowW - margin;  y = margin;       break;
                    case Corner.BottomLeft:  x = margin;       y = sh - windowH - margin;  break;
                    case Corner.BottomRight: x = sw - windowW - margin;  y = sh - windowH - margin;  break;
                    default:                 x = margin;       y = margin;       break;
                }
            }

            // Minimized = just the title bar
            float effectiveH = minimized ? 28f : windowH;
            var windowRect = new Rect(x, y, windowW, effectiveH);

            // Convert GUI rect (top-left origin) to screen rect (bottom-left origin)
            // so Mouse.current.position comparisons in Update() are correct.
            currentWindowRect = new Rect(x, Screen.height - y - effectiveH, windowW, effectiveH);

            // Drag handling — track state, but do NOT consume Event.current
            // (the Canvas blocker already stops game/Canvas clicks from behind the panel).
            var headerRect = new Rect(x, y, windowW, 28f);
            var ev = Event.current;
            if (ev != null && headerRect.Contains(ev.mousePosition))
            {
                if (ev.type == EventType.MouseDown && ev.button == 0)
                {
                    isDragging = true;
                    dragOffset = ev.mousePosition - new Vector2(x, y);
                }
            }
            if (isDragging && ev != null && ev.type == EventType.MouseDrag)
            {
                customPosition = ev.mousePosition - dragOffset;
                // Clamp to screen
                customPosition.x = Mathf.Clamp(customPosition.x, 0, sw - windowW);
                customPosition.y = Mathf.Clamp(customPosition.y, 0, sh - effectiveH);
                usingCustomPosition = true;
            }
            if (isDragging && ev != null && ev.type == EventType.MouseUp && ev.button == 0)
            {
                isDragging = false;
            }

            // IMGUI cost control: interactive tabs (Range/Clothing) redraw every
            // widget on every event — in the Editor, cursor movement over the
            // Game view floods MouseMove/MouseDrag events (dozens per frame), so
            // a moving camera pays the full tab rebuild many times per frame.
            // Nothing here consumes MouseMove (hover only), and a drag with no
            // GUI control hot and no panel drag in progress belongs to the
            // scene camera — safe to skip both.
            if (ev != null && (ev.type == EventType.MouseMove
                || (ev.type == EventType.MouseDrag && !isDragging && GUIUtility.hotControl == 0)))
                return;

            GUILayout.BeginArea(windowRect, bgStyle);
            GUILayout.BeginVertical();

            // Header — title + minimize button, then TWO tab rows (9 tabs would
            // clip a 560px window in one row): viewer/systems, then the sim suite
            GUILayout.BeginHorizontal(GUILayout.Height(24));
            GUILayout.Label("<b>DEBUG HUD</b>", boldStyle, GUILayout.Width(80));
            GUILayout.FlexibleSpace();

            // Minimize / expand button
            string minLabel = minimized ? "+" : "_";
            if (GUILayout.Button(minLabel, GUILayout.Width(24), GUILayout.Height(20)))
            {
                minimized = !minimized;
            }

            GUILayout.EndHorizontal();

            if (minimized)
            {
                GUILayout.EndVertical();
                GUILayout.EndArea();
                return;
            }

            DrawTabRow(Tab.Camera, Tab.Render, Tab.Sfx, Tab.Clothing, Tab.Path, Tab.Keys);
            DrawTabRow(Tab.Combat, Tab.Cover, Tab.Vision, Tab.TanSuit, Tab.Demolitions, Tab.Weapons, Tab.MatRes, Tab.Forces, Tab.BuildLog);
            GUILayout.BeginHorizontal();
            GUILayout.Label("sections:", labelStyle, GUILayout.Width(60));
            if (GUILayout.Button("open all", tabStyle, GUILayout.Height(16))) SecSetAll(true);
            if (GUILayout.Button("close all", tabStyle, GUILayout.Height(16))) SecSetAll(false);
            var liteStyle = hudLite ? tabActiveStyle : tabStyle;
            if (GUILayout.Button(hudLite ? "LITE on" : "lite", liteStyle, GUILayout.Height(16))) hudLite = !hudLite;
            var lockStyle = hudLocked ? tabActiveStyle : tabStyle;
            if (GUILayout.Button(hudLocked ? "LOCKED" : "edit", lockStyle, GUILayout.Width(60), GUILayout.Height(16))) hudLocked = !hudLocked;
            GUILayout.FlexibleSpace();
            GUILayout.Label($"<b>hud</b> {hudProfMsShown:F2}ms/frame · {hudProfEventsShown} ev · peak {hudProfPeakShown:F1}ms", labelStyle);
            GUILayout.EndHorizontal();

            // Separator
            GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));

            // Tab content
            scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));

            // LOCKED = the cheap architecture: live tabs render as ONE cached
            // label (same pattern as the text tabs — proven 120fps). Unlock to
            // tune, lock back to verify the change at full framerate.
            if (hudLocked && IsLiveTab(activeTab))
            {
                if (activeTab != lastBuiltTab
                        || Time.unscaledTime >= nextTabRefresh)
                    RebuildLockedTabText();
                GUILayout.Label(lockedTabText, labelStyle);
            }
            // hudLite: window chrome + tab rows still draw, but the tab BODY is
            // suppressed. A/B for the native IMGUI pass — if FPS recovers with
            // the body gone, the content's draw cost is the wall, not OnGUI C#.
            else if (hudLite)
            {
                GUILayout.Label("<i>hudLITE — tab content suppressed</i>", labelStyle);
            }
            else
            switch (activeTab)
            {
                case Tab.Clothing: DrawClothingTab(); break;
                case Tab.Combat:
                    UnityEngine.Profiling.Profiler.BeginSample("HUD.DrawCombatTab");
                    try { DrawCombatTab(); } finally { UnityEngine.Profiling.Profiler.EndSample(); }
                    break;
                case Tab.Demolitions:
                    UnityEngine.Profiling.Profiler.BeginSample("HUD.DrawDemolitionsTab");
                    try { DrawDemolitionsTab(); } finally { UnityEngine.Profiling.Profiler.EndSample(); }
                    break;
                case Tab.Cover: DrawCoverTab(); break;
                case Tab.Vision: DrawVisionTab(); break;
                case Tab.TanSuit: DrawTanSuitTab(); break;
                case Tab.Weapons: DrawWeaponsTab(); break;
                case Tab.Forces: DrawForcesTab(); break;
                case Tab.Sfx: DrawSfxTab(); break;
                case Tab.MatRes:
                    UnityEngine.Profiling.Profiler.BeginSample("HUD.DrawMatResTab");
                    try { DrawMatResTab(); } finally { UnityEngine.Profiling.Profiler.EndSample(); }
                    break;
                default:
                    if (activeTab != lastBuiltTab
                            || Time.unscaledTime >= nextTabRefresh)
                        RebuildTabText();
                    GUILayout.Label(tabText, labelStyle);
                    if (activeTab == Tab.Render) DrawShadowControls();
                    break;
            }

            GUILayout.EndScrollView();

            // Body/cert squares + destination posts — physical spec painted in
            // world space, not panel content. Outside the tab switch so the
            // beams track the body on ANY tab: a beam only moves when
            // SetCustomBeam runs again, so gating it behind one tab left a
            // stale corpse that read as "the square broke".
            if (fighterBeams)
            {
                var beamVinny = cityMap != null ? cityMap.SpawnedCharacter : null;
                var beamTan = TanSuitRef;
                PaintFighterBeams(beamVinny != null ? beamVinny.GetComponent<CombatController>() : null, beamVinny, "");
                PaintFighterBeams(beamTan != null ? beamTan.GetComponent<CombatController>() : null, beamTan, "_t");
            }
            else if (fighterBeamsWasOn)
            {
                // Toggled off — one clear pass so the corpses don't linger.
                PaintFighterBeams(null, null, "");
                PaintFighterBeams(null, null, "_t");
            }
            fighterBeamsWasOn = fighterBeams;

            // Footer — current tab + next tab + controls
            GUILayout.Space(4);

            if (Time.unscaledTime >= nextFooterRefresh)
                RebuildFooterText();
            GUILayout.Label(footerText ?? "", labelStyle);

            GUILayout.EndVertical();
            GUILayout.EndArea();

            // The SEAM window — its own BeginArea bottom-left, drawn AFTER
            // the main window closes: inside the parent's layout group a
            // nested area is clipped to the parent rect, which is why it
            // must live out here at OnGUIInner's top level.
            DrawSeamWindow(sw, sh);
        }

        void DrawTabRow(params Tab[] row)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(24));
            foreach (var t in row)
            {
                if (!TabAvailable(t)) continue;
                var style = activeTab == t ? tabActiveStyle : tabStyle;
                if (GUILayout.Button(t.ToString(), style, GUILayout.Height(20)))
                    activeTab = t;
            }
            GUILayout.EndHorizontal();
        }

        static string TabName(Tab t) => t.ToString();

        // boldStyle colour inline — cached text carries no per-line style
        static string H(string s) => $"<color=#19FFFF><b>{s}</b></color>";

        /// <summary>
        /// Rebuild the active text-only tab into one cached rich-text block —
        /// a single GUILayout.Label per repaint instead of ~40 widgets per
        /// IMGUI event. Interactive tabs stay live and skip this path.
        /// </summary>
        void RebuildTabText()
        {
            hudSB.Length = 0;
            switch (activeTab)
            {
                case Tab.Camera: BuildCameraTab(hudSB); break;
                case Tab.Render: BuildRenderTab(hudSB); break;
                case Tab.Path: BuildPathTab(hudSB); break;
                case Tab.BuildLog: BuildBuildLogTab(hudSB); break;
                default: BuildKeysTab(hudSB); break;
            }
            tabText = hudSB.ToString();
            lastBuiltTab = activeTab;
            nextTabRefresh = Time.unscaledTime + 0.25f;
        }

        void RebuildFooterText()
        {
            var avail = GetAvailableTabs();
            int curIdx = avail.IndexOf(activeTab);
            if (curIdx < 0) curIdx = 0;
            int nextIdx = (curIdx + 1) % avail.Count;
            hudSB.Length = 0;
            hudSB.AppendLine($"<b>Current:</b> {TabName(avail[curIdx])}    <b>Next [Tab]:</b> {TabName(avail[nextIdx])}    <b>Pos [Y]:</b> {(usingCustomPosition ? "Dragged" : currentCorner.ToString())}");
            hudSB.Append('\n');
            hudSB.AppendLine(H("Hotkeys") + " — full list in [Keys] tab");
            hudSB.AppendLine("[`] / [O] Hide  [Tab] Next tab  [Y] Corner  [M] Minimize");
            hudSB.AppendLine("[LMB] Select/focus  [MMB] Rotate  [RMB] Pan  [Wheel] Zoom");
            hudSB.AppendLine("[G] Possess Vinny (WASD/mouse/LMB)");
            hudSB.AppendLine("[F6] Free cam  [F7] Waypoints  [F8] Freeze ([.] step 0.08s)  [F10] Vehicles");
            hudSB.AppendLine("<i>Drag title bar to move. Click _ to minimize.</i>");
            footerText = hudSB.ToString();
            nextFooterRefresh = Time.unscaledTime + 0.25f;
        }

        /// <summary>
        /// Bottom-of-screen mode banner — answers "what debug view am I in"
        /// without opening the panel, plus the live numbers that mode reports.
        /// Draws whenever a V-key view is active (Off = no banner).
        /// </summary>
        void DrawModeTicker()
        {
            if (chunkManager == null) return;
            var m = chunkManager.iterVisMode;
            if (m == VoxelChunkManager.IterVisMode.Off) return;

            var sensor = cityMap != null && cityMap.SpawnedCharacter != null
                ? cityMap.SpawnedCharacter.GetComponent<CoverSensor>() : null;
            var vis = cityMap != null && cityMap.SpawnedCharacter != null
                ? cityMap.SpawnedCharacter.GetComponent<VisionSensor>() : null;
            var f = sensor != null ? sensor.Field : null;
            string coverData = f != null && f.HasData
                ? $"{f.ValidCells}/{f.TestedCells} hidden" +
                  (f.ChosenCell >= 0 ? $" · pick {f.ChosenPoint.x:F1},{f.ChosenPoint.z:F1}" : "")
                : "no threat";

            string mode, desc, data = "", color;
            switch (m)
            {
                case VoxelChunkManager.IterVisMode.IterationHeat:
                    mode = "ITER HEAT"; color = "#ff9a3c"; desc = "raymarch cost — blue<80 · green~160 · red>160"; break;
                case VoxelChunkManager.IterVisMode.StepChannels:
                    mode = "STEP CH"; color = "#3cb2ff"; desc = "R = voxel-march steps · B = shadow-march steps"; break;
                case VoxelChunkManager.IterVisMode.MissWaste:
                    mode = "MISS WASTE"; color = "#ff5555"; desc = "heat on rays that never hit"; break;
                case VoxelChunkManager.IterVisMode.CoverQuality:
                    mode = "COVER · QUALITY"; color = "#4dff8f";
                    desc = "hot = cover-capable face · red wash = open ground";
                    data = sensor != null ? $"{sensor.stats.wallCols} wall cols in radius" : "no sensor"; break;
                case VoxelChunkManager.IterVisMode.CoverThreat:
                    mode = "COVER · THREAT"; color = "#ffe14d";
                    desc = "composite score — white flash = the pick"; data = coverData; break;
                case VoxelChunkManager.IterVisMode.VisionCone:
                    mode = "VISION CONE"; color = "#ff8c1a";
                    desc = "orange = inside Vinny's sight wedge (angle+range; LOS lives in the sensor's rays)";
                    data = vis != null
                        ? $"{vis.range:F0}m · ±{vis.halfAngleDeg:F0}° · det {vis.MaxDetection:P0}"
                        : "no vision sensor"; break;
                case VoxelChunkManager.IterVisMode.CoverWalkable:
                    mode = "COVER · WALKABLE"; color = "#3ce65a";
                    desc = "the flood-served domain on the surfaces — green in · olive unpreferred · amber island · red blocked";
                    data = coverData; break;
                case VoxelChunkManager.IterVisMode.CoverReachable:
                    mode = "COVER · REACHABLE"; color = "#ffb01e";
                    desc = "flood domain on the surfaces — heat = corridor cost (red at his feet → blue far) · amber = floor but sealed";
                    data = coverData; break;
                default: // CoverExposure
                    mode = "COVER · EXPOSURE"; color = "#4dd8ff";
                    desc = "teal hidden · red exposed · slate invalid"; data = coverData; break;
            }

            var r = new Rect(Screen.width * 0.5f - 330f, Screen.height - 34f, 660f, 26f);
            GUI.Box(r, GUIContent.none, bgStyle);
            GUI.Label(new Rect(r.x + 10f, r.y + 3f, r.width - 20f, r.height - 4f),
                $"<b><color={color}>{mode}</color></b>  {desc}" +
                (data.Length > 0 ? $"   <color=#8fb8ff>{data}</color>" : ""), labelStyle);
        }

        void BuildCameraTab(System.Text.StringBuilder sb)
        {
            if (followCamera == null)
            {
                sb.AppendLine("No FollowCamera in scene.");
                return;
            }

            sb.AppendLine($"<b>Mode</b>: {(followCamera.ChaseMode ? "CHASE" : "ORBIT")}");
            sb.AppendLine($"<b>Position</b>: {followCamera.transform.position}");
            sb.AppendLine($"<b>FOV</b>: {followCamera.FieldOfView:F1}");
            sb.AppendLine($"<b>Distance</b>: {followCamera.Distance:F2}");
            sb.AppendLine($"<b>Height</b>: {followCamera.Height:F2}");
            sb.AppendLine($"<b>Yaw</b>: {followCamera.CurrentYaw:F1} deg");
            sb.AppendLine($"<b>Pitch</b>: {followCamera.CurrentPitch:F1} deg");
            sb.AppendLine($"<b>Follow Speed</b>: {followCamera.FollowSpeed:F1}");
            sb.AppendLine($"<b>Look Speed</b>: {followCamera.LookSpeed:F1}");
            sb.Append('\n');
            sb.AppendLine(H("Controls"));
            sb.AppendLine("[T] Chase/Orbit toggle");
            sb.AppendLine("[Shift] Free-look");
            sb.AppendLine("[Z] Reset camera");
            sb.AppendLine("[Q/E] Distance  [R/F] Height");
            sb.AppendLine("[C] Capture  [P] Perf log");
        }

        /// <summary>
        /// BuildLog — player-build diagnostics: what the build actually resolved
        /// (shaders, kernels, render path, draw counts) plus the init event trail.
        /// Lives on the cached-text path (4Hz rebuild) so it costs ~one label.
        /// </summary>
        void BuildBuildLogTab(System.Text.StringBuilder sb)
        {
            if (BuildDiag.StateKeys.Count == 0 && BuildDiag.Events.Count == 0)
            {
                sb.AppendLine("No diagnostics recorded yet —");
                sb.AppendLine("nothing instrumented has run.");
                return;
            }

            sb.AppendLine(H("Device / init state"));
            foreach (var key in BuildDiag.StateKeys)
            {
                if (key.StartsWith("render.") || key.StartsWith("bridge.")) continue;
                BuildDiag.TryGet(key, out var v);
                sb.AppendLine($"  {key}: {v}");
            }
            sb.Append('\n');

            sb.AppendLine(H("Render (live)"));
            bool any = false;
            foreach (var key in BuildDiag.StateKeys)
            {
                if (!key.StartsWith("render.") && !key.StartsWith("bridge.")) continue;
                BuildDiag.TryGet(key, out var v);
                sb.AppendLine($"  {key}: {v}");
                any = true;
            }
            if (!any) sb.AppendLine("  (no render calls yet)");
            // RT probe — sample a 48x48 center block of the live render texture.
            // Flat (lum range ≈ 0) = draws emit but nothing lands in the RT.
            if (chunkManager != null)
            {
                var rt = chunkManager.GetColorTexture();
                if (rt != null)
                {
                    int N = Mathf.Min(48, rt.width, rt.height);
                    if (rtProbeTex == null || rtProbeTex.width != N)
                        rtProbeTex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    rtProbeTex.ReadPixels(new Rect((rt.width - N) / 2, (rt.height - N) / 2, N, N), 0, 0);
                    rtProbeTex.Apply();
                    RenderTexture.active = prev;
                    var px = rtProbeTex.GetPixels();
                    float minL = 1f, maxL = 0f;
                    foreach (var p in px)
                    {
                        float l = p.grayscale;
                        if (l < minL) minL = l;
                        if (l > maxL) maxL = l;
                    }
                    sb.AppendLine($"  rt.probe: {N}px block, center {px[px.Length / 2]} lumRange {minL:F2}–{maxL:F2}" +
                        (maxL - minL < 0.02f ? " <b>FLAT — RT holds only clear color</b>" : " content present"));
                }
            }
            sb.Append('\n');

            sb.AppendLine(H($"Init log ({BuildDiag.Events.Count})"));
            int start = Mathf.Max(0, BuildDiag.Events.Count - 60);
            for (int i = start; i < BuildDiag.Events.Count; i++)
                sb.AppendLine(BuildDiag.Events[i]);
        }

        /// Live shadow controls appended under the Render tab's cached text —
        /// the GameUI "SHADOW DEBUG" accordion is buried in the Orders page.
        /// A handful of widgets per event keeps the text-tab cost profile.
        void DrawShadowControls()
        {
            if (cityMap == null || chunkManager == null) return;
            GUILayout.Space(6);
            bool on = chunkManager.GetShadowEnabled() == 1;
            if (GUILayout.Toggle(on, " shadows (in-volume sun march)", toggleStyle) != on)
                cityMap.SetShadowEnabled(!on);
            int maxSteps = Mathf.RoundToInt(HSlider("shadow max steps",
                chunkManager.GetShadowMaxSteps(), 1f, 64f, "F0"));
            if (maxSteps != chunkManager.GetShadowMaxSteps())
                cityMap.SetShadowMaxSteps(maxSteps);
            GUILayout.Space(4);
            chunkManager.proxyBackFacesOnly = GUILayout.Toggle(chunkManager.proxyBackFacesOnly,
                " proxy back-faces only  (A/B: halves fragment overdraw — same image)", toggleStyle);
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("render scale", labelStyle, GUILayout.Width(86));
            if (GUILayout.Button($"{chunkManager.CurrentResolutionScale:F2} ▾",
                    openResScaleDrop ? tabActiveStyle : tabStyle, GUILayout.Width(56), GUILayout.Height(20)))
                openResScaleDrop = !openResScaleDrop;
            GUILayout.EndHorizontal();
            if (openResScaleDrop)
                foreach (var s in VoxelChunkManager.ResolutionScales)
                    if (GUILayout.Button($"   {s:F2}", labelStyle))
                    {
                        chunkManager.SetResolutionScale(s);
                        openResScaleDrop = false;
                    }
        }

        void BuildRenderTab(System.Text.StringBuilder sb)
        {
            if (chunkManager == null)
            {
                sb.AppendLine("No VoxelChunkManager in scene.");
                return;
            }

            sb.AppendLine($"<b>FPS</b>: {fps:F0}  |  Frame: {frameTimeMs:F2}ms");
            sb.AppendLine($"  min:{frameTimeMin:F2}  max:{frameTimeMax:F2}  avg:{frameTimeAvg:F2}");
            sb.AppendLine($"<b>Resolution</b>: {chunkManager.RenderWidth}x{chunkManager.RenderHeight}   <b>scale</b> {chunkManager.CurrentResolutionScale:F2}  (dropdown below)");
            sb.AppendLine($"<b>Ortho</b>: {chunkManager.IsOrtho}  Size: {chunkManager.CameraOrthoSize:F1}");
            sb.Append('\n');
            sb.AppendLine(H("Draw Calls"));
            sb.AppendLine($"  Total: {chunkManager.PerfTotalDrawCalls}");
            sb.AppendLine($"  Sectors: {chunkManager.PerfSectorsDrawn}  Instanced chars: {chunkManager.InstancedCharacterCount}");
            sb.AppendLine($"  Chunks: {chunkManager.PerfTotalChunks} total / {chunkManager.PerfActiveChunks} active / {chunkManager.PerfDrawnChunks} drawn");
            sb.AppendLine($"  Baked: {chunkManager.BakedSectorCount} sectors / {chunkManager.BakedSectorBuildingCount} buildings");
            sb.Append('\n');
            sb.AppendLine(H("LOD Tiers"));
            sb.AppendLine($"  Near: {chunkManager.PerfLodNear}  Mid: {chunkManager.PerfLodMid}  Far: {chunkManager.PerfLodFar}");
            sb.AppendLine($"  Ultra: {chunkManager.PerfLodUltra}  Culled: {chunkManager.PerfLodCulled}");
            sb.AppendLine($"  ScreenRatio: {chunkManager.PerfMinScreenRatio:F4} - {chunkManager.PerfMaxScreenRatio:F4}");
            sb.Append('\n');
            sb.AppendLine(H("CPU Timing"));
            sb.AppendLine($"  Cull: {chunkManager.CpuCullMs:F2}ms  Draw: {chunkManager.CpuDrawMs:F2}ms  Total: {chunkManager.CpuTotalMs:F2}ms");
        }

        void DrawClothingTab()
        {
            if (allClothingSystems.Count == 0)
            {
                GUILayout.Label("No ClothingSystem in scene.", labelStyle);
                GUILayout.Label("ClothingSystem auto-adds to VoxelCharacter.", labelStyle);
                return;
            }

            // Character selector — controls which rig receives the Alt+pose hotkeys
            if (allCharacterRigs.Count > 0 && SecFold("l.chars", $"Character Control — {allCharacterRigs.Count} rigs (hotkeys)"))
            {
                GUILayout.BeginHorizontal();
                for (int i = 0; i < allCharacterRigs.Count; i++)
                {
                    var r = allCharacterRigs[i];
                    if (r == null) continue;
                    string label = $"#{i}: {r.gameObject.name}";
                    bool active = (i == selectedRigIndex);
                    var oldBg = GUI.backgroundColor;
                    if (active) GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                    if (GUILayout.Button(label, active ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
                    {
                        selectedRigIndex = i;
                        // Deactivate all, activate selected — Controllable alone
                        // didn't reach the pose path: HandleInput also needs
                        // AnimHotkeys, which used to have no code setter (the
                        // dead "select a rig → Alt+pose" path).
                        foreach (var rig in allCharacterRigs)
                            if (rig != null) { rig.Controllable = false; rig.AnimHotkeys = false; }
                        r.Controllable = true;
                        r.AnimHotkeys = true;
                    }
                    GUI.backgroundColor = oldBg;
                }
                GUILayout.EndHorizontal();

                // Pose transition knobs — the dual-eval blend (snapshot → target)
                // driving every state change on the selected rig's animation.
                var selRig = (selectedRigIndex >= 0 && selectedRigIndex < allCharacterRigs.Count)
                    ? allCharacterRigs[selectedRigIndex] : null;
                var selAnim = selRig != null ? selRig.GetComponent<CharacterAnimation>() : null;
                if (selAnim != null)
                {
                    GUILayout.Label("<b>Pose transition (snapshot → target lerp)</b>", boldStyle);
                    selAnim.poseTransitions = GUILayout.Toggle(selAnim.poseTransitions, " enabled", toggleStyle);
                    GUILayout.Label($"  duration: {selAnim.transitionDuration:F2}s", labelStyle);
                    selAnim.transitionDuration = GUILayout.HorizontalSlider(selAnim.transitionDuration, 0.05f, 1.0f);
                    GUILayout.Label($"  settle dip: {selAnim.transitionDip:F2} vox", labelStyle);
                    selAnim.transitionDip = GUILayout.HorizontalSlider(selAnim.transitionDip, 0f, 1.5f);
                    GUILayout.Label($"  ease: {(selAnim.transitionEase == 0 ? "linear" : selAnim.transitionEase == 2 ? "cosine" : "smoothstep")}  [click to cycle]", labelStyle);
                    if (GUILayout.Button("cycle ease", GUILayout.Width(90)))
                        selAnim.transitionEase = (selAnim.transitionEase + 1) % 3;
                    // Duck scalar preview — the shared silhouette axis.
                    // 0 upright · 1 shallow · 2 deep; Idle renders the hold
                    // pose, Walking the crumple under the gait.
                    GUILayout.Label($"  duck: {selAnim.duck:F2}", labelStyle);
                    selAnim.duck = GUILayout.HorizontalSlider(selAnim.duck, 0f, 2f);
                    GUILayout.Label($"  duck→speed {OnOff(selAnim.duckSpeedScale)}  floor {selAnim.duckSpeedMin:F2}  factor {selAnim.DuckSpeedFactor:F2}", labelStyle);
                    selAnim.duckSpeedMin = GUILayout.HorizontalSlider(selAnim.duckSpeedMin, 0.05f, 1f);
                    selAnim.lookScan = GUILayout.Toggle(selAnim.lookScan, " look scan");
                }
            }

            // Clothing instance selector
            if (SecFold("l.inst", $"Clothing Instance — {allClothingSystems.Count} in scene"))
            {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < allClothingSystems.Count; i++)
            {
                var cs = allClothingSystems[i];
                if (cs == null) continue;
                string label = $"#{i}: {cs.gameObject.name}";
                bool active = (i == selectedClothingIndex);
                var oldBg = GUI.backgroundColor;
                if (active) GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);
                if (GUILayout.Button(label, active ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
                {
                    selectedClothingIndex = i;
                    clothingSystem = cs;
                }
                GUI.backgroundColor = oldBg;
            }
            GUILayout.EndHorizontal();
            }
            GUILayout.Space(4);

            // Draw the selected instance's clothing controls
            if (clothingSystem != null)
                clothingSystem.DrawClothingTab();
            else
                GUILayout.Label("No instance selected.", labelStyle);
        }

        /// <summary>
        /// Hotkey cheat-sheet. Sections only appear when the component that owns
        /// those keys is actually present in the scene — stale keys are never listed.
        /// </summary>
        void BuildKeysTab(System.Text.StringBuilder sb)
        {
            sb.AppendLine(H("This Panel"));
            sb.AppendLine("[`] / [O]  Toggle panel      [Tab] Next tab");
            sb.AppendLine("[Y]  Cycle corner            [M] Minimize");
            sb.AppendLine("Drag title bar to reposition freely");
            sb.Append('\n');
            sb.AppendLine(H("Map Camera") + " (mouse over map viewport)");
            sb.AppendLine("[LMB] Click — select block / focus camera");
            sb.AppendLine("[MMB] Drag — rotate (yaw + pitch)");
            sb.AppendLine("[RMB] Drag — pan (execution mode needs [F6])");
            sb.AppendLine("[Wheel] — zoom in / out");
            sb.Append('\n');
            sb.AppendLine(H("Scene / Render Debug"));
            sb.AppendLine("[F6] Free camera (also disables debris)");
            sb.AppendLine("[F7] Waypoint graph beams");
            sb.AppendLine("[V] Cycle ray-march debug: heat / steps / miss waste /");
            sb.AppendLine("      cover Quality·Threat·Exposure (Vinny's field) / off");

            if (allCharacterRigs.Count > 0)
            {
                sb.Append('\n');
                sb.AppendLine(H("Character Control"));
                sb.AppendLine("[G] Possess: WASD move · mouse look · LMB fire · [R] Reload");
                sb.AppendLine("<b>Alt+pose latch — possessed OR the rig selected in</b>");
                sb.AppendLine("<b>Clothing tab's Character Control:</b>");
                sb.AppendLine("  [Alt+I] Idle   [Alt+W] Walk   [Alt+L] Look   [Alt+J] AimWalk");
                sb.AppendLine("  [Alt+A] Aim    [Alt+C] Crouch [Alt+F] Flinch [Alt+D] Down");
                sb.AppendLine("  [Alt+T] T-Pose [Alt+S] AimSweep              [Alt+B] Sit");
                sb.AppendLine("  [Alt+R] Reload stand   [Alt+E] Reload crouch");
                sb.AppendLine("  [Alt+X] release latch → back to locomotion");
                sb.AppendLine("<b>Numpad pose panel — same latch, no modifier/ding:</b>");
                sb.AppendLine("  [Num7] T-Pose [Num8] Down    [Num9] Sit");
                sb.AppendLine("  [Num4] Aim    [Num5] Crouch/Deep (toggle)  [Num6] Flinch");
                sb.AppendLine("  [Num1] Walk   [Num2] Look    [Num3] AimWalk");
                sb.AppendLine("  [Num0]/[Enter] release · [Num.] AimSweep · [Num+/-] speed");
            }

            if (vehicleTest != null)
            {
                sb.Append('\n');
                sb.AppendLine(H("Vehicles"));
                sb.AppendLine("[F10] Spawn parked cars / toggle driving");
            }

            if (stressTest != null || stressDiag != null)
            {
                sb.Append('\n');
                sb.AppendLine(H("Stress Test"));
                if (stressTest != null)
                {
                    sb.AppendLine("[F11] Spawn agents and run test");
                    sb.AppendLine("[F7] Cycle path-beam count (during test)");
                }
                if (stressDiag != null)
                    sb.AppendLine("[F11] Start diagnostics    [F9] Stop");
            }

            if (hoodSpawner != null || testRigPresent)
            {
                sb.Append('\n');
                sb.AppendLine(H("Test Rigs"));
                if (hoodSpawner != null)
                    sb.AppendLine("[1-9,0] Force animation state");
                if (testRigPresent)
                    sb.AppendLine("[T/I/W/L/A/C] States  [R] Reload  [Space] Pause  [=/-] Speed");
            }
        }

        void BuildPathTab(System.Text.StringBuilder sb)
        {
            if (pathDebug == null)
            {
                sb.AppendLine("No PathDebugRenderer in scene.");
                return;
            }

            sb.AppendLine($"<b>Active Paths</b>: {pathDebug.ActivePathCount}");
            sb.AppendLine($"<b>Debug Enabled</b>: {pathDebug.DebugEnabled}");
            sb.Append('\n');
            sb.AppendLine("Path debug beams render into the");
            sb.AppendLine("voxel raymarch overlay (right panel).");
            sb.AppendLine("Paths are drawn during execution phase");
            sb.AppendLine("when characters/vehicles are moving.");
        }

        // ------------------------------------------------------------------
        // Combat tab — live testbed tuning/debug.
        // Opens whenever a block with a combat anchor (firing_position /
        // cover_stand_a) is clicked — any sim state. Dwelling is a sim
        // feature (Test Week holds the week open), not a tool key.
        // ------------------------------------------------------------------

        private void HandleBlockClicked(string blockId)
        {
            if (cityMap == null) return;
            bool hasPos = cityMap.TryGetBuildingPointLocal(blockId, "firing_position", out _)
                       || cityMap.TryGetBuildingPointLocal(blockId, "cover_stand_a", out _);
            Debug.Log($"[DebugHUD] 🔵 Dwell click on {blockId} — combat anchor: {(hasPos ? "found" : "absent")}");
            if (!hasPos) return;
            OpenCombatTab(blockId);
        }

        /// <summary>
        /// Debug snap: on a clicked range block, move the spawned
        /// character onto lane N's firing_position and face him downrange at
        /// that lane's firing_target. Teleports — no pathing (bench tool,
        /// any sim state — lane-switching isn't part of the sim).
        /// </summary>
        private void MoveCharacterToLane(string blockId, int lane)
        {
            var ch = cityMap.SpawnedCharacter;
            var mount = ch != null ? ch.GetComponent<WeaponMount>() : null;
            if (mount == null) return;
            if (!cityMap.TryGetBuildingPointLocal(blockId, $"firing_position_{lane}", out var posLocal)) return;
            if (solver != null && solver.IsAiming) solver.Disarm();
            var ep = FindFirstObjectByType<EventPlayer>();
            if (ep == null) return;
            Vector3? faceWorld = cityMap.TryGetBuildingPointLocal(blockId, $"firing_target_{lane}", out var tgtLocal)
                ? cityMap.MapRoot.position + tgtLocal : (Vector3?)null;
            ep.DebugSnapTo(cityMap.MapRoot.position + posLocal, mount.Dims, mount.CharVoxelSize, faceWorld);
        }

        /// <summary>Find a CharacterRig by its GameObject name ("Civilian_02" etc).</summary>
        private CharacterRig FindRig(string goName)
        {
            foreach (var r in FindObjectsByType<CharacterRig>(FindObjectsSortMode.None))
                if (r != null && r.gameObject.name == goName) return r;
            return null;
        }

        /// <summary>VoxelCharacter by rig GameObject name — CharacterRig path
        /// first, raw GameObject+component lookup as fallback (covers a rig
        /// that spawned without a CharacterRig component).</summary>
        private VoxelCharacter FindCharacter(string goName)
        {
            var rig = FindRig(goName);
            if (rig != null && rig.Character != null) return rig.Character;
            var go = GameObject.Find(goName);
            return go != null ? go.GetComponent<VoxelCharacter>() : null;
        }

        /// <summary>Feet-level XZ center of a character's voxel volume — the
        /// ground point threat semantics expect (sensors add eye height).</summary>
        private static Vector3 CharFeetCenter(VoxelCharacter ch)
        {
            var d = ch.Dims; float vs = ch.voxelSize;
            var p = ch.transform.position;
            return new Vector3(p.x + d.x * vs * 0.5f, p.y, p.z + d.z * vs * 0.5f);
        }

        /// <summary>One fighter's live combat viz — truth X on the opponent,
        /// the magenta belief post + beam (ThreatPos), and the orbit-search
        /// spokes/ring/legs TryOrbitStep is solving. `tag` suffixes every beam
        /// name so two fighters' sets coexist ("" = vinny's legacy keys);
        /// `dim` scales the palette so overlapping viz stays readable.</summary>
        void PaintSearchViz(CombatController cc, VoxelCharacter ch, VoxelCharacter opp,
            string tag, float dim)
        {
            var pdrLive = PathDebugRenderer.Instance;
            if (pdrLive == null) return;
            bool fighting = cc != null && ch != null
                && cc.State != CombatController.CombatState.Idle;
            Color Dim(Color c) => new Color(c.r * dim, c.g * dim, c.b * dim, c.a);

            if (fighting && opp != null)
            {
                Vector3 truth = CharFeetCenter(opp);
                var oc = Dim(new Color(1f, 0.55f, 0.1f, 0.95f));
                pdrLive.SetCustomBeam($"pick_aim{tag}",  truth + Vector3.left * 0.25f,  truth + Vector3.right * 0.25f, 0.05f, oc);
                pdrLive.SetCustomBeam($"pick_aim2{tag}", truth + Vector3.down * 0.25f,  truth + Vector3.up * 0.25f,   0.05f, oc);
                Vector3 lp = cc.ThreatPos;   // belief = last seen
                var mc = Dim(new Color(1f, 0.15f, 0.85f, 0.95f));
                pdrLive.SetCustomBeam($"last_seen{tag}",    lp + Vector3.up * 0.05f, lp + Vector3.up * 1.2f, 0.09f, mc);
                pdrLive.SetCustomBeam($"last_seen_x{tag}",  lp + Vector3.left * 0.25f,    lp + Vector3.right * 0.25f, 0.05f, mc);
                pdrLive.SetCustomBeam($"last_seen_x2{tag}", lp + Vector3.forward * 0.25f, lp + Vector3.back * 0.25f,   0.05f, mc);
                pdrLive.SetCustomBeam($"last_seen_dir{tag}",
                    ch.transform.position + Vector3.up * 0.9f,
                    lp + Vector3.up * 0.9f, 0.04f, mc);
            }
            else
            {
                pdrLive.ClearCustomBeam($"pick_aim{tag}");
                pdrLive.ClearCustomBeam($"pick_aim2{tag}");
                pdrLive.ClearCustomBeam($"last_seen{tag}");
                pdrLive.ClearCustomBeam($"last_seen_x{tag}");
                pdrLive.ClearCustomBeam($"last_seen_x2{tag}");
                pdrLive.ClearCustomBeam($"last_seen_dir{tag}");
            }

            // Search-orbit triangulation — the geometry TryOrbitStep solves.
            // All spokes radiate from the belief anchor (the last_seen post):
            // ref = orbit 0° · aim = goal spoke (origin+90°·side) · buf = the
            // standoff ring the scorer hugs · pick = anchor→hop stand · legN =
            // the routed corridor · tgt = hop post (cyan → orange on progress).
            if (fighting && cc.Searching)
            {
                Vector3 an = cc.SearchAnchor;
                Vector3 Spoke(float deg) => new Vector3(
                    Mathf.Sin(deg * Mathf.Deg2Rad), 0f, Mathf.Cos(deg * Mathf.Deg2Rad));
                const float SPOKE = 5f;
                var wc = Dim(new Color(0.85f, 0.85f, 0.85f, 0.85f));
                var yc = Dim(new Color(1f, 0.85f, 0.15f, 0.9f));
                var gn = Dim(new Color(0.3f, 1f, 0.4f, 0.9f));
                var cy = Dim(new Color(0.2f, 0.9f, 1f, 0.9f));
                pdrLive.SetCustomBeam($"search_ref{tag}",
                    an + Vector3.up * 0.6f,
                    an + Vector3.up * 0.6f + Spoke(cc.SearchOriginBearing) * SPOKE, 0.03f, wc);
                pdrLive.SetCustomBeam($"search_aim{tag}",
                    an + Vector3.up * 0.6f,
                    an + Vector3.up * 0.6f + Spoke(cc.SearchTargetBearing) * SPOKE, 0.03f, yc);
                float rb = cc.SearchBufferRadius;
                if (rb > 0.01f)
                {
                    var bc = Dim(new Color(1f, 0.6f, 0.2f, 0.35f));
                    for (int bi = 0; bi < 16; bi++)
                        pdrLive.SetCustomBeam($"search_buf{tag}{bi}",
                            an + Vector3.up * 0.35f + Spoke(bi * 22.5f) * rb,
                            an + Vector3.up * 0.35f + Spoke((bi + 1) * 22.5f) * rb, 0.02f, bc);
                }
                if (cc.SearchLegOut)
                {
                    Vector3 st = cc.CoverPoint;
                    pdrLive.SetCustomBeam($"search_pick{tag}",
                        an + Vector3.up * 0.6f, st + Vector3.up * 0.6f, 0.03f, gn);
                    var rt = cc.LegRoute;
                    int ri = cc.LegRouteIndex, segs = 0;
                    Vector3 prev = CharFeetCenter(ch);
                    if (rt != null && ri < rt.Count)
                    {
                        for (; ri < rt.Count && segs < 24; ri++, segs++)
                        {
                            pdrLive.SetCustomBeam($"search_leg{tag}{segs}",
                                prev + Vector3.up * 0.5f, rt[ri] + Vector3.up * 0.5f, 0.04f, cy);
                            prev = rt[ri];
                        }
                    }
                    if (segs == 0)
                    {
                        pdrLive.SetCustomBeam($"search_leg{tag}0",
                            prev + Vector3.up * 0.5f, st + Vector3.up * 0.5f, 0.04f, cy);
                        segs = 1;
                    }
                    for (int ci = segs; ci < 24; ci++) pdrLive.ClearCustomBeam($"search_leg{tag}{ci}");
                    var tc = Color.Lerp(cy, Dim(new Color(1f, 0.55f, 0.1f, 0.95f)),
                        Mathf.Clamp01(cc.SearchProgress / 90f));
                    pdrLive.SetCustomBeam($"search_tgt{tag}",
                        st + Vector3.up * 0.05f, st + Vector3.up * 1.0f, 0.08f, tc);
                }
            }
            else
            {
                pdrLive.ClearCustomBeam($"search_ref{tag}");
                pdrLive.ClearCustomBeam($"search_aim{tag}");
                pdrLive.ClearCustomBeam($"search_pick{tag}");
                for (int ci = 0; ci < 24; ci++) pdrLive.ClearCustomBeam($"search_leg{tag}{ci}");
                pdrLive.ClearCustomBeam($"search_tgt{tag}");
                for (int bi = 0; bi < 16; bi++) pdrLive.ClearCustomBeam($"search_buf{tag}{bi}");
            }
        }

        /// <summary>
        /// Generic snap for non-EventPlayer characters (Civilian_02): same
        /// corner-offset math as EventPlayer.DebugSnapTo plus a down-raycast so
        /// feet land on whatever surface sits under the point. Facing = plain yaw
        /// (same convention CombatController uses for its facing updates).
        /// </summary>
        private void SnapCharacterTo(VoxelCharacter ch, Vector3 posWorld, Vector3? faceWorld)
        {
            if (ch == null) return;
            var d = ch.Dims; float vs = ch.voxelSize;
            Vector3 t = posWorld - new Vector3(d.x * vs * 0.5f, 0f, d.z * vs * 0.5f);
            t.y = posWorld.y;
            if (World != null && World.Raycast(posWorld + Vector3.up * 0.5f, Vector3.down, 8f, out var gh))
                t.y = gh.point.y;
            // Clearance check — same contract as EventPlayer.DebugSnapTo: a
            // snap must land on a square the body occupies or the mover wakes
            // embedded (SeekingCover + mv 0.00 — the stand-B statue class).
            if (World != null)
            {
                Vector3 center = new Vector3(posWorld.x, t.y, posWorld.z);
                float bh = ch.BodyFootprint * 0.5f;
                if (!World.SquareFree(center, bh, ch.WorldSize.y)
                    && World.TryNearestFreeSquare(center, bh, ch.WorldSize.y,
                                                  1.5f, out var free))
                {
                    Debug.Log($"[DebugHUD] snap adjusted → free square ({free.x:F1},{free.y:F2},{free.z:F1}) — authored point embedded");
                    t = free - new Vector3(d.x * vs * 0.5f, 0f, d.z * vs * 0.5f);
                }
            }
            ch.transform.position = t;
            if (faceWorld.HasValue)
            {
                Vector3 dir = faceWorld.Value - t; dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                    ch.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
        }

        /// <summary>
        /// Cover-pit setup (a block carrying cover_stand_a/_b points): Vinny
        /// teleports to stand A facing B, Civilian_02 "Tan Suit" to B facing A.
        /// Also arms the threat marker on Tan's feet so the cover field's
        /// threat-keyed layers go live before the duel is executed.
        /// </summary>
        private void SetupCoverPit(string blockId)
        {
            // No sim-state gate — this is a debug teleport; it stages the pit
            // any time. Failures log loudly instead of no-op'ing silently.
            if (!cityMap.TryGetBuildingPointLocal(blockId, "cover_stand_a", out var aL) ||
                !cityMap.TryGetBuildingPointLocal(blockId, "cover_stand_b", out var bL))
            {
                Debug.LogWarning($"[DebugHUD] 🥊 SetupCoverPit — '{blockId}' resolves no cover stands; aborting.");
                return;
            }
            Vector3 aW = cityMap.MapRoot.position + aL;
            Vector3 bW = cityMap.MapRoot.position + bL;
            // Parity experiment — swap which fighter owns which stand: if
            // "the south fighter" stumbles regardless of identity, the
            // asymmetry is the geometry/field, not the character.
            if (pitSwapSides) (aW, bW) = (bW, aW);

            var vinny = cityMap.SpawnedCharacter;
            var mount = vinny != null ? vinny.GetComponent<WeaponMount>() : null;
            var ep = FindFirstObjectByType<EventPlayer>();
            if (solver != null && solver.IsAiming) solver.Disarm();
            // Deterministic reset — a re-run stands both combatants down first:
            // out of combat, back to Idle ("standing, guns at side" per the
            // phase-1 spec), then teleported to their plates.
            if (vinny != null)
            {
                vinny.GetComponent<CombatController>()?.ExitCombat();
                vinny.GetComponent<CharacterAnimation>()?.SetState(CharacterAnimation.AnimState.Idle);
            }
            if (vinny != null && ep != null)
            {
                float? feetY = null;
                if (World != null && World.Raycast(aW + Vector3.up * 0.5f, Vector3.down, 8f, out var gh))
                    feetY = gh.point.y;
                if (mount != null)
                    ep.DebugSnapTo(aW, mount.Dims, mount.CharVoxelSize, bW, feetY);
                else
                    SnapCharacterTo(vinny, aW, bW);   // no mount yet — generic snap still lands him
            }

            var tan = FindCharacter("Civilian_02");
            if (tan == null)
                Debug.LogWarning("[DebugHUD] 🥊 SetupCoverPit — Civilian_02 not found; Tan snap skipped.");
            else
            {
                tan.GetComponent<CombatController>()?.ExitCombat();
                tan.GetComponent<CharacterAnimation>()?.SetState(CharacterAnimation.AnimState.Idle);
            }
            SnapCharacterTo(tan, bW, aW);
            if (tan != null)
            {
                pickedAim = CharFeetCenter(tan);
                var pdr = PathDebugRenderer.Instance;
                var c = new Color(1f, 0.55f, 0.1f, 0.95f);
                pdr?.SetCustomBeam("pick_aim",  pickedAim.Value + Vector3.left * 0.25f,  pickedAim.Value + Vector3.right * 0.25f, 0.05f, c);
                pdr?.SetCustomBeam("pick_aim2", pickedAim.Value + Vector3.down * 0.25f,  pickedAim.Value + Vector3.up * 0.25f,   0.05f, c);
            }
            Debug.Log($"[DebugHUD] 🥊 Cover pit setup — Vinny→stand_a {aW}, Tan→stand_b {bW}");
        }

        /// <summary>Shared duel execution — Vinny's EnterCombat keyed to Tan's
        /// live feet; AI-duel mode mirrors it onto Tan's controller.</summary>
        private void StartPitDuel(VoxelCharacter ch, VoxelCharacter tanCh)
        {
            var cc = ch.GetComponent<CombatController>();
            if (cc == null) return;
            Vector3 threat = CharFeetCenter(tanCh);
            pickedAim = threat;   // marker mirrors the live threat
            cc.RefillCylinder();   // arena loadout — combat entry itself never refills
            var tanCc = tanCh.GetComponent<CombatController>();
            // Dice-lab preset — Fight mode runs BOTH fighters on the dice,
            // auto (real-time, event-driven). Flags land BEFORE EnterCombat —
            // the dice branch reads them at frame entry. Toggles stay runtime-live.
            if (pitTanMode == PitTanMode.Fight)
            { cc.diceArbiter = true; cc.seamArbiter = false; cc.scriptedMode = false;
              cc.diceManual = false; }
            cc.traceLabel = "vinny";   // flight-recorder labels match the SEAM panel
            if (tanCc != null) tanCc.traceLabel = "tan";
            cc.EnterCombat(threat, tanCh);   // the BODY — exposure probe samples his silhouette
            if (tanCc != null && tanCc.State != CombatController.CombatState.Idle)
                tanCc.ExitCombat();   // stale combat on re-run — stand down first
            if (pitTanMode != PitTanMode.Dummy && tanCc != null)
            {
                tanCc.RefillCylinder();
                // Hide mode seeks first cover then parks — the search-test
                // dummy; Fight mode runs the full symmetric beat stack.
                tanCc.holdInCover = pitTanMode == PitTanMode.Hide;
                if (pitTanMode == PitTanMode.Fight)
                { tanCc.diceArbiter = true; tanCc.seamArbiter = false; tanCc.scriptedMode = false;
                  tanCc.diceManual = false; }
                tanCc.EnterCombat(CharFeetCenter(ch), ch);
            }
        }

        /// <summary>One row of duel diagnostics per fighter — the pick verdict
        /// (LastResult) plus the live state/beat/duck/arm picture. Answers
        /// "which path did he take" without a console scrape: pick-fail reads
        /// "no cover within Xm", corridor-fail reads "cover unreachable",
        /// arrival reads "in cover behind X".</summary>
        private void DuelStatusRow(string label, VoxelCharacter ch)
        {
            if (ch == null)
            { GUILayout.Label($"{label}: not found", labelStyle); return; }
            var cc = ch.GetComponent<CombatController>();
            var anim = ch.GetComponent<CharacterAnimation>();
            if (cc == null)
            { GUILayout.Label($"{label}: no CombatController", labelStyle); return; }
            float dToCover = cc.State == CombatController.CombatState.Idle ? -1f
                : Vector3.Distance(CharFeetCenter(ch), cc.CoverPoint);
            GUILayout.Label(
                $"{label}  <b>{cc.State}</b>/{cc.Beat.ToString().ToLowerInvariant()}"
                + (cc.Watching ? " <b>+WATCH</b>" : "")
                + $"  arm:{(cc.ExposureArmed ? "on" : "off")}  mv {cc.DebugMoved:F2}m"
                + $"  duck {(anim != null ? anim.duck : 0f):F2}→{cc.TargetDuck:F2}"
                + (cc.SearchLegOut ? "  SEARCH" : "")
                + (dToCover >= 0f ? $"  dst→{cc.CoverObstacleId} {dToCover:F1}m" : ""),
                labelStyle);
            GUILayout.Label($"     <i>{cc.LastResult}</i>", labelStyle);
            var cs = ch.GetComponent<CoverSensor>();
            if (cs != null)
                GUILayout.Label(
                    $"     scan {cs.stats.wallCols} walls · {cs.stats.valid}/{cs.stats.tested} cells · {cs.stats.supportProbes} ground probes"
                    + $" · r{cs.radius:F0}m · {(cs.Field != null && cs.Field.HasData ? $"field {cs.Field.N}²" : "no field")}",
                    labelStyle);
            SeamStatusBlock(label, cc);
        }

        /// <summary>Compact duel-row line — toggle + running program + last
        /// verdict. The full analysis surface lives in the SEAM window
        /// (bottom-left of the game view, persistent).</summary>
        private void SeamStatusBlock(string label, CombatController cc)
        {
            GUILayout.BeginHorizontal();
            // Mutually exclusive arbiters — ticking one unticks the other
            // so the systems never cross-contaminate a live frame.
            bool arb = GUILayout.Toggle(cc.seamArbiter && !cc.diceArbiter, " ARB", toggleStyle, GUILayout.Width(52));
            if (arb != (cc.seamArbiter && !cc.diceArbiter)) { cc.seamArbiter = arb; if (arb) cc.diceArbiter = false; }
            bool die = GUILayout.Toggle(cc.diceArbiter, " DICE", toggleStyle, GUILayout.Width(52));
            if (die != cc.diceArbiter) { cc.diceArbiter = die; if (die) cc.seamArbiter = false; }
            GUILayout.Label(
                cc.diceArbiter
                    ? $"<b>{cc.DiceRunning}</b>  <i>v:{cc.DiceVerdict}</i>"
                    : cc.seamArbiter
                        ? $"<b>{cc.ArbRunning}</b>  <i>v:{cc.ArbVerdict}</i>"
                        : "<i>authored beat chain</i>",
                labelStyle);
            GUILayout.EndHorizontal();
        }

        /// <summary>The standalone SEAM window — bottom-left of the game
        /// view, drawn OUTSIDE the DEBUG window's fold system so it stays
        /// up while the hud is used for something else (or hidden). One
        /// fighter block each: running program, verdict, blackboard chips,
        /// timestamped verdict history, intent radar, and ALL scores in
        /// FIXED order with the argmax marked — stable row positions across
        /// frames so pause/stepping is diff-able.</summary>
        private void DrawSeamWindow(float sw, float sh)
        {
            var vin = cityMap != null ? cityMap.SpawnedCharacter : null;
            var tan = TanSuitRef;
            var vcc = vin != null ? vin.GetComponent<CombatController>() : null;
            var tcc = tan != null ? tan.GetComponent<CombatController>() : null;
            if (vcc == null && tcc == null) return;
            // Wide short band — fighters lay out side-by-side instead of
            // stacked, so the window shows more information laterally and
            // stays clear of the main HUD's column.
            float w = Mathf.Min(sw - margin * 2f, 800f);
            float hh = seamPanelOpen ? 208f : 28f;
            // Adaptive cap — the main HUD hangs from the top-left corner, so
            // the SEAM panel must stop short of ITS bottom edge or the two
            // boxes overlap on short game views. When the main window is
            // dragged away from the left corner the cap frees up.
            if (seamPanelOpen)
            {
                float hudBottom = currentWindowRect.height > 0f
                    ? Screen.height - currentWindowRect.yMax   // GUI-space bottom edge
                    : windowH + margin;
                bool hudOnLeft = currentWindowRect.width <= 0f
                    || currentWindowRect.xMin < margin + w;    // only collides when left-anchored
                if (hudOnLeft)
                    hh = Mathf.Max(120f,
                        Mathf.Min(208f, sh - hudBottom - margin * 2f));
            }
            var rect = new Rect(margin, sh - hh - margin, w, hh);
            GUILayout.BeginArea(rect, bgStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(seamPanelOpen ? "\u25bc SEAM" : "\u25b6 SEAM", toggleStyle, GUILayout.Width(70)))
                seamPanelOpen = !seamPanelOpen;
            GUILayout.Label("<b>live intent field</b>", labelStyle);
            if (GUILayout.Button("\U0001F4DD MARK", toggleStyle, GUILayout.Width(74)))
            {   // Drop a flag + a full SNAP of both fighters into Logs/dice_trace.log
                DiceTrace.Write($"t{SimClock.Now:F2} ==== MARK — user flagged this moment ====");
                vcc?.TraceSnap(); tcc?.TraceSnap();
            }
            GUILayout.EndHorizontal();
            if (seamPanelOpen)
            {
                seamScrollPos = GUILayout.BeginScrollView(seamScrollPos);
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.Width(382));
                SeamPanel("vinny", vcc);
                GUILayout.EndVertical();
                GUILayout.Space(10);
                GUILayout.BeginVertical(GUILayout.Width(382));
                SeamPanel("tan", tcc);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }

        /// <summary>One fighter's block inside the SEAM window.</summary>
        private void SeamPanel(string label, CombatController cc)
        {
            GUILayout.BeginHorizontal();
            if (cc == null)
            { GUILayout.Label($"{label}: —", labelStyle); GUILayout.EndHorizontal(); return; }
            bool arb = GUILayout.Toggle(cc.seamArbiter && !cc.diceArbiter, " ARB", toggleStyle, GUILayout.Width(52));
            if (arb != (cc.seamArbiter && !cc.diceArbiter)) { cc.seamArbiter = arb; if (arb) cc.diceArbiter = false; }
            bool die = GUILayout.Toggle(cc.diceArbiter, " DICE", toggleStyle, GUILayout.Width(52));
            if (die != cc.diceArbiter) { cc.diceArbiter = die; if (die) cc.seamArbiter = false; }
            GUILayout.Label($"<b>{label}</b>  {(cc.diceArbiter ? cc.DiceRunning : cc.ArbRunning)}", labelStyle);
            GUILayout.EndHorizontal();
            if (cc.diceArbiter)
            {   // THE ROLL — the cycle log until the ENGAGEMENT panel lands (phase 7).
                // MANUAL row — the turn engine: every click resolves ONE
                // decision (ramp → face → sub-pick). The small dice pin the
                // face for testing a specific branch.
                GUILayout.BeginHorizontal();
                bool man = GUILayout.Toggle(cc.diceManual, " MANUAL", toggleStyle, GUILayout.Width(70));
                if (man != cc.diceManual) cc.diceManual = man;
                if (cc.diceManual)
                {
                    bool can = cc.DiceAwaiting && !cc.Reloading && cc.DiceRollReadyIn <= 0f;
                    GUI.enabled = can;
                    if (GUILayout.Button(
                            $"🎲 ROLL — {(cc.DiceAwaiting ? cc.DiceAwaitText : "…")}" +
                              (cc.DiceAwaiting && cc.DiceRollReadyIn > 0f ? $" · {cc.DiceRollReadyIn:F1}s" : ""),
                            can ? tabActiveStyle : tabStyle,
                            GUILayout.Width(210), GUILayout.Height(22)))
                        cc.DiceClick(0);
                    GUI.enabled = true;
                    GUILayout.Label("pin:", labelStyle, GUILayout.Width(24));
                    for (int f = 1; f <= 6; f++)
                    {
                        int ff = f;
                        GUI.enabled = can;
                        if (GUILayout.Button(ff.ToString(), tabStyle, GUILayout.Width(20), GUILayout.Height(18)))
                            cc.DiceClick(ff);
                        GUI.enabled = true;
                    }
                }
                else GUILayout.Label(
                    $"     {(cc.DiceAwaiting ? $"await {cc.DiceAwaitText}" + (cc.DiceRollReadyIn > 0f ? $" · {cc.DiceRollReadyIn:F1}s" : " · READY") : "executing…")}", labelStyle);
                GUILayout.EndHorizontal();
                GUILayout.Label(
                    $"     quantum #{cc.DiceCycle}  face {cc.DiceFace} ({CombatController.DiceFaceNames[cc.DiceFace]})"
                    + (cc.DiceSubRoll > 0 ? $"  · sub {cc.DiceSubRoll}/6" : ""), labelStyle);
                GUILayout.Label($"     verdict: {cc.DiceVerdict}", labelStyle);
                if (cc.DiceHist.Length > 0)
                    GUILayout.Label($"     history : <i>{cc.DiceHist}</i>", labelStyle);
                return;
            }
            if (!cc.seamArbiter)
            { GUILayout.Label("     <i>authored beat chain — tick ARB or DICE</i>", labelStyle); return; }
            GUILayout.Label($"     verdict: {cc.ArbVerdict}", labelStyle);
            if (cc.ArbBoard.Length > 0)
                GUILayout.Label($"     board   : {cc.ArbBoard}", labelStyle);
            if (cc.ArbHist.Length > 0)
                GUILayout.Label($"     history : <i>{cc.ArbHist}</i>", labelStyle);
            GUILayout.BeginHorizontal();
            Rect rr = GUILayoutUtility.GetRect(104, 104, GUILayout.Width(104), GUILayout.Height(104));
            PaintSeamRadar(rr, cc.ArbScores, cc.ArbRunning);
            GUILayout.BeginVertical();
            var s = cc.ArbScores;
            float best = 0f; int argmax = -1;
            for (int i = 0; i < s.Length; i++) if (s[i] > best) { best = s[i]; argmax = i; }
            for (int i = 0; i < s.Length; i++)
            {
                float w = s[i];
                int fill = Mathf.Clamp(Mathf.RoundToInt(w * 20f), 0, 20);
                string bar = new string('\u2588', fill).PadRight(20, '\u00b7');
                GUILayout.Label(
                    $"{(i == argmax && w > 0f ? "\u25b6" : " ")}{CombatController.ArbNames[i],-12} {bar} {w:F2}",
                    i == argmax && w > 0f ? boldStyle : labelStyle);
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        /// <summary>In-world combat bubbles — the live action floating over
        /// each fighter's head, drawn ahead of the panel-visible gate so the
        /// fight stays readable with the HUD hidden. Dice mode shows the
        /// turn state + the metrics the decision is using (remaining
        /// distance, reload seconds, cylinder); arbiter/authored show their
        /// running program. Amber = executing, blue = awaiting.</summary>
        void DrawCombatBubbles()
        {
            var vin = cityMap != null ? cityMap.SpawnedCharacter : null;
            var tan = TanSuitRef;
            var vcc = vin != null ? vin.GetComponent<CombatController>() : null;
            var tcc = tan != null ? tan.GetComponent<CombatController>() : null;
            if (vcc == null && tcc == null) return;
            // Project through the camera the voxel RT actually renders from — it
            // swaps between the follow cam and the map camera, so resolve per event.
            Camera cam = (chunkManager != null ? chunkManager.RenderCam : null)
                ?? (followCamera != null ? followCamera.GetComponent<Camera>() : null)
                ?? Camera.main;
            if (cam == null) return;
            if (vin != null && vcc != null) DrawBubble(vin.transform, vcc, cam);
            if (tan != null && tcc != null) DrawBubble(tan.transform, tcc, cam);
        }

        void DrawBubble(Transform t, CombatController cc, Camera cam)
        {
            // Emit whenever DICE is toggled on or combat is live — a lit bubble
            // over an idle fighter is the sanity check that the display works.
            if (cc.State == CombatController.CombatState.Idle && !cc.diceArbiter) return;
            Vector3 head = t.position + Vector3.up * 0.9f;
            Vector3 sp = cam.WorldToScreenPoint(head);
            if (sp.z <= 0f) return;
            float sx = sp.x, sy = Screen.height - sp.y;

            string l1, l2; bool hot;
            Vector3 feet = t.position;
            if (cc.diceArbiter)
            {
                if (cc.State == CombatController.CombatState.Idle)
                {   l1 = "🎲 DICE armed"; l2 = "awaiting combat"; hot = false; }
                else if (cc.Reloading)
                {   l1 = $"RELOADING {cc.Cylinder}/{cc.cylinderCapacity}";
                    l2 = $"+1 in {cc.NextRoundIn:F1}s · full {cc.ReloadLeft:F1}s"; hot = true; }
                else if (cc.DiceLegActive)
                {   l1 = cc.DiceRunning;
                    l2 = $"→ dest {Vector3.Distance(feet, cc.DiceLegTarget):F1}m"; hot = true; }
                else if (cc.DiceReconChecking)
                {   l1 = "recon — eyes on last-known";
                    l2 = cc.DiceReconLeft > 0f ? $"sweep {cc.DiceReconLeft:F1}s" : "peeking…"; hot = true; }
                else if (cc.DiceDeepActive)
                {   l1 = "deep recon — orbiting last-known";
                    l2 = cc.SearchLegOut ? "probe hop walking" : "scanning…"; hot = true; }
                else if (cc.State == CombatController.CombatState.SeekingCover)
                {   l1 = cc.DiceRunning;
                    l2 = $"→ {cc.CoverObstacleId} {Vector3.Distance(feet, cc.CoverPoint):F1}m"; hot = true; }
                else if (cc.DiceAwaiting)
                {   l1 = cc.DiceRampPending
                        ? $"🎲 {(cc.DiceRollReadyIn > 0f ? "holding → " : "")}reload check — spent {cc.cylinderCapacity - cc.Cylinder}/{cc.cylinderCapacity}"
                        : $"🎲 {cc.DiceAwaitText}";
                    if (cc.DiceRollReadyIn > 0f) l1 += $" · {cc.DiceRollReadyIn:F1}s";
                    l2 = $"{cc.DiceVerdict}"; hot = false; }
                else { l1 = cc.DiceRunning; l2 = cc.DiceVerdict; hot = false; }
            }
            else if (cc.ArbActive) { l1 = cc.ArbRunning; l2 = cc.ArbVerdict; hot = true; }
            else
            {   l1 = cc.Beat != CombatController.EngagementBeat.None
                    ? $"beat {cc.Beat}" : cc.State.ToString();
                l2 = cc.LastResult; hot = cc.State == CombatController.CombatState.SeekingCover; }

            if (cc.ThreatPos.sqrMagnitude > 0.01f)
                l2 += $" · threat {Vector3.Distance(feet, cc.ThreatPos):F0}m";
            if (l2.Length > 76) l2 = l2.Substring(0, 73) + "…";
            string col = hot ? "#ffb35c" : "#8fb8d8";
            var rect = new Rect(sx - 96f, sy - 46f, 192f, 48f);
            GUILayout.BeginArea(rect, bgStyle);
            GUILayout.Label($"<b><color={col}>{l1}</color></b>", labelStyle);
            GUILayout.Label($"<color=#aeb8c4>{l2}</color>", labelStyle);
            GUILayout.EndArea();
        }

        /// <summary>The intent radar — 10 axes, one polygon = the live score
        /// field. Rasterized into a cached texture on Repaint only.</summary>
        private void PaintSeamRadar(Rect r, float[] scores, string running)
        {
            const int SZ = 104;
            if (seamRadarTex == null || seamRadarTex.width != SZ)
            {
                if (seamRadarTex != null) Destroy(seamRadarTex);
                seamRadarTex = new Texture2D(SZ, SZ, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point };
            }
            if (Event.current.type != EventType.Repaint)
            { GUI.DrawTexture(r, seamRadarTex); return; }
            for (int i = 0; i < seamRadarPx.Length; i++) seamRadarPx[i] = new Color32(0, 0, 0, 40);
            int cx = SZ / 2, cy = SZ / 2; float rad = SZ * 0.42f;
            int n = scores.Length;
            // faint guide ring at 0.5 + spokes
            for (int a = 0; a < 72; a++)
            {
                float t = a * Mathf.PI * 2f / 72f;
                int px = cx + Mathf.RoundToInt(Mathf.Cos(t) * rad * 0.5f);
                int py = cy + Mathf.RoundToInt(Mathf.Sin(t) * rad * 0.5f);
                SetPx(seamRadarPx, SZ, px, py, new Color32(90, 90, 90, 80));
            }
            for (int i = 0; i < n; i++)
            {
                float t = -Mathf.PI / 2f + i * Mathf.PI * 2f / n;
                DrawLinePx(seamRadarPx, SZ, cx, cy,
                    cx + Mathf.RoundToInt(Mathf.Cos(t) * rad),
                    cy + Mathf.RoundToInt(Mathf.Sin(t) * rad),
                    new Color32(70, 70, 80, 90));
            }
            // score polygon
            int px0 = 0, py0 = 0, fx = 0, fy = 0; bool first = true;
            for (int i = 0; i <= n; i++)
            {
                int k = i % n;
                float t = -Mathf.PI / 2f + k * Mathf.PI * 2f / n;
                float sc = Mathf.Clamp01(scores[k]);
                int px = cx + Mathf.RoundToInt(Mathf.Cos(t) * rad * sc);
                int py = cy + Mathf.RoundToInt(Mathf.Sin(t) * rad * sc);
                if (first) { fx = px; fy = py; first = false; }
                else
                {
                    DrawLinePx(seamRadarPx, SZ, px0, py0, px, py,
                        new Color32(255, 210, 60, 220));
                    SetPx(seamRadarPx, SZ, px, py, new Color32(255, 255, 255, 255));
                }
                px0 = px; py0 = py;
            }
            DrawLinePx(seamRadarPx, SZ, px0, py0, fx, fy, new Color32(255, 210, 60, 220));
            seamRadarTex.SetPixels32(seamRadarPx);
            seamRadarTex.Apply(false);
            GUI.DrawTexture(r, seamRadarTex);
        }

        static void SetPx(Color32[] buf, int sz, int x, int y, Color32 c)
        {
            if ((uint)x >= (uint)sz || (uint)y >= (uint)sz) return;
            buf[y * sz + x] = c;
        }

        static void DrawLinePx(Color32[] buf, int sz, int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx - dy;
            for (int guard = 0; guard < 512; guard++)
            {
                SetPx(buf, sz, x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>The depth-ladder pick row — deep/mid/crouch/wall buttons
        /// force Vinny's cover to that box's anchor pad. Rides the real field
        /// (nearest Hidden cell) so reqDepth is honestly solved; works
        /// mid-combat for A/B sweeps.</summary>
        private void PitCoverPickRow(string blockId, VoxelCharacter vinny, VoxelCharacter tan)
        {
            GUI.enabled = vinny != null && tan != null;
            GUILayout.Label("pick Vinny's cover:", labelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("deep",   tabStyle, GUILayout.Height(20))) PitPickCover(blockId, vinny, tan, "cover_deep");
            if (GUILayout.Button("mid",    tabStyle, GUILayout.Height(20))) PitPickCover(blockId, vinny, tan, "cover_mid");
            if (GUILayout.Button("crouch", tabStyle, GUILayout.Height(20))) PitPickCover(blockId, vinny, tan, "cover_crouch");
            if (GUILayout.Button("wall",   tabStyle, GUILayout.Height(20))) PitPickCover(blockId, vinny, tan, "cover_wall");
            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        /// <summary>Force Vinny's cover pick to a named ladder anchor —
        /// threat keyed on Tan's live feet, same as the duel.</summary>
        private void PitPickCover(string blockId, VoxelCharacter ch, VoxelCharacter tanCh, string anchorName)
        {
            var cc = ch.GetComponent<CombatController>();
            if (cc == null) return;
            if (!cityMap.TryGetBuildingPointLocal(blockId, anchorName, out var aL))
            {
                Debug.LogWarning($"[DebugHUD] 🥊 cover pick — '{blockId}' has no '{anchorName}' anchor (regen the pit?)");
                return;
            }
            Vector3 threat = CharFeetCenter(tanCh);
            pickedAim = threat;
            cc.EnterCombat(threat, tanCh, cityMap.MapRoot.position + aL);
        }

        /// <summary>
        /// Voxel pick delivery — fires when CityMap3D's pick mode consumed a map click.
        /// STAND teleports the character onto the hit surface (facing the aim point if
        /// set); AIM stores the hit point as the fire target and re-arms a live solver.
        /// </summary>
        private void HandleVoxelPicked(VoxelRayHit hit)
        {
            var pdr = PathDebugRenderer.Instance;
            if (pickArmed == PickSlot.Stand)
            {
                // Wall-face picks push the stand point off the surface so he lands
                // beside the wall instead of inside it; a down-raycast (volumes +
                // terrain) finds the surface to stand on — roof, floor, or street.
                Vector3 snap = hit.point;
                Vector3 nXZ = new Vector3(hit.normal.x, 0f, hit.normal.z);
                if (nXZ.sqrMagnitude > 0.01f) snap += nXZ.normalized * 0.45f;
                float? feetY = null;
                if (World != null && World.Raycast(snap + Vector3.up * 0.05f, Vector3.down, 40f, out var gh))
                    feetY = gh.point.y;
                else if (hit.normal.y > 0.5f)
                    feetY = hit.point.y;
                pickedStand = new Vector3(snap.x, feetY ?? snap.y, snap.z);
                var ch = cityMap.SpawnedCharacter;
                var mount = ch != null ? ch.GetComponent<WeaponMount>() : null;
                var ep = FindFirstObjectByType<EventPlayer>();
                if (mount != null && ep != null)
                {
                    if (solver != null && solver.IsAiming) solver.Disarm();
                    ep.DebugSnapTo(snap, mount.Dims, mount.CharVoxelSize, pickedAim, feetY);
                }
                pdr?.SetCustomBeam("pick_stand", pickedStand.Value, pickedStand.Value + Vector3.up * 1.8f,
                    0.06f, new Color(0.2f, 1f, 0.35f, 0.9f));
                Debug.Log($"[DebugHUD] 🎯 STAND picked @ {pickedStand.Value} ({hit.volumeId ?? "terrain"} mat {hit.material})");
            }
            else if (pickArmed == PickSlot.Aim)
            {
                pickedAim = hit.point;
                pdr?.SetCustomBeam("pick_aim", hit.point + Vector3.left * 0.25f, hit.point + Vector3.right * 0.25f,
                    0.05f, new Color(1f, 0.55f, 0.1f, 0.95f));
                pdr?.SetCustomBeam("pick_aim2", hit.point + Vector3.down * 0.25f, hit.point + Vector3.up * 0.25f,
                    0.05f, new Color(1f, 0.55f, 0.1f, 0.95f));
                if (solver != null && solver.IsAiming) solver.Arm(hit.point);   // re-aim live
                Debug.Log($"[DebugHUD] 🎯 AIM picked @ {hit.point} ({hit.volumeId ?? "terrain"} mat {hit.material})");
            }
            else if (pickArmed == PickSlot.Blast)
            {
                pickedBlast = hit.point;
                blastNormal = hit.normal;
                blastVolume = hit.volumeId;
                var red = new Color(1f, 0.25f, 0.15f, 0.95f);
                pdr?.SetCustomBeam("pick_blast", hit.point + Vector3.left * 0.3f, hit.point + Vector3.right * 0.3f, 0.06f, red);
                pdr?.SetCustomBeam("pick_blast2", hit.point + Vector3.down * 0.3f, hit.point + Vector3.up * 0.3f, 0.06f, red);
                pdr?.SetCustomBeam("pick_blast3", hit.point + Vector3.forward * 0.3f, hit.point + Vector3.back * 0.3f, 0.06f, red);
                Debug.Log($"[DebugHUD] 💥 BLAST picked @ {hit.point} ({hit.volumeId ?? "terrain"} mat {hit.material})");
            }
            else if (pickArmed == PickSlot.Building)
            {
                // Building-center mode: the blast origin snaps to the volume's
                // geometric center — visualize an inside-out explosion.
                blastVolume = hit.volumeId;
                blastAtVolumeCenter = false;
                blastNormal = Vector3.up;
                pickedBlast = hit.point;
                var dmg = VoxelDamageSystem.GetOrCreate();
                if (!string.IsNullOrEmpty(blastVolume) && dmg.TryGetVolumeCenter(blastVolume, out Vector3 vc))
                {
                    pickedBlast = vc;
                    blastAtVolumeCenter = true;
                }
                var orange = new Color(1f, 0.55f, 0.1f, 0.95f);
                Vector3 bp = pickedBlast.Value;
                pdr?.SetCustomBeam("pick_blast", bp + Vector3.left * 0.3f, bp + Vector3.right * 0.3f, 0.06f, orange);
                pdr?.SetCustomBeam("pick_blast2", bp + Vector3.down * 0.3f, bp + Vector3.up * 0.3f, 0.06f, orange);
                pdr?.SetCustomBeam("pick_blast3", bp + Vector3.forward * 0.3f, bp + Vector3.back * 0.3f, 0.06f, orange);
                Debug.Log($"[DebugHUD] 🏢 BLDG picked {(blastAtVolumeCenter ? $"center @ {bp}" : $"surface @ {bp} — no volume")} ({blastVolume ?? "terrain"})");
            }
            else if (pickArmed == PickSlot.Inspect)
            {
                InspectVoxel(hit);
            }
            pickArmed = PickSlot.None;
        }

        private int inspectCount;

        /// <summary>
        /// Voxel inspector — full diagnostic dump for the picked cell: material/raw value, volume placement, damage
        /// delta vs the pristine array (the cell + its 6 neighbours), the shader's smooth-normal inputs (flags
        /// degenerate neighbourhoods) and a CPU replica of its sun + shadow-ray lighting. Marks the cell with a
        /// yellow cube, shows a short HUD summary, logs the report and appends it to Logs/voxel_inspect.log
        /// (survives editor restarts; Editor.log is too large to search).
        /// </summary>
        private void InspectVoxel(VoxelRayHit hit)
        {
            var pdr = PathDebugRenderer.Instance;
            var dmg = VoxelDamageSystem.GetOrCreate();
            int mat = hit.material;
            string matName = StAssetReader.GetMaterialName(mat);
            string vol = hit.volumeId ?? "terrain";
            var sb = new System.Text.StringBuilder(1024);
            sb.AppendLine($"🔍 VOXEL INSPECT #{++inspectCount}  t={Time.realtimeSinceStartup:F1}s frame={Time.frameCount}");
            sb.AppendLine($"  cell     : [{vol}] {hit.cell}  mat {mat} '{matName}'  raw 0x{hit.voxel:X}  flags 0x{hit.voxel & ~0x1FFu:X}");
            var cam = Camera.main;
            sb.AppendLine($"  hit      : world {hit.point:F2}  face n{hit.normal:F2}  dist {hit.distance:F2}m"
                + (cam != null ? $"  | camera {cam.transform.position:F1} fwd {cam.transform.forward:F2}" : ""));

            uint[] vox = null; int w = 0, h = 0, d = 0; Vector3 corner = default; float vs = 0.05f, yaw = 0f;
            bool isVol = !string.IsNullOrEmpty(hit.volumeId) && World != null
                && World.TryGetVolume(hit.volumeId, out vox, out w, out h, out d, out corner, out vs, out yaw);
            if (!isVol)
            {
                vs = World != null ? (World.VoxelGridToWorld(hit.cell + Vector3Int.right) - World.VoxelGridToWorld(hit.cell)).x : 0.05f;
                yaw = 0f;
            }

            int MatAt(Vector3Int cc)
            {
                if (isVol)
                    return cc.x >= 0 && cc.y >= 0 && cc.z >= 0 && cc.x < w && cc.y < h && cc.z < d
                        ? (int)(vox[cc.x + cc.y * w + cc.z * w * h] & 0x1FFu) : -1;
                return World != null ? World.GetVoxelAtGrid(cc) : -1;
            }
            bool damaged = isVol && dmg.TryGetOriginalVoxel(hit.volumeId, 0, out _);
            int OrigMat(Vector3Int cc)
            {
                int cur = MatAt(cc);
                if (!damaged || cur < 0) return cur;
                return dmg.TryGetOriginalVoxel(hit.volumeId, cc.x + cc.y * w + cc.z * w * h, out uint o) ? (int)(o & 0x1FFu) : cur;
            }

            if (isVol)
            {
                var ext = new Vector3(w, h, d) * vs;
                var loc = new Vector3(hit.cell.x + 0.5f, hit.cell.y + 0.5f, hit.cell.z + 0.5f) * vs;
                sb.AppendLine($"  volume   : {w}×{h}×{d} cells  vs {vs}  yaw {yaw}  corner {corner:F2}  extent {ext:F2}m  cell-center (local) {loc:F2}m");
            }
            else sb.AppendLine($"  terrain  : grid cell {hit.cell}  vs {vs:F3}  voxel byte {MatAt(hit.cell)}");

            int cellOrig = OrigMat(hit.cell);
            sb.AppendLine("  damage   : " + (!isVol ? "terrain (no damage tracking)"
                : !damaged ? "volume pristine (never damaged)"
                : $"volume DAMAGED · this cell original {cellOrig} '{StAssetReader.GetMaterialName(cellOrig)}' {(cellOrig == mat ? "(unchanged)" : "→ CHANGED")}"));

            string[] axes = { "-x", "+x", "-y", "+y", "-z", "+z" };
            var dl = new Vector3Int[] { new(-1, 0, 0), new(1, 0, 0), new(0, -1, 0), new(0, 1, 0), new(0, 0, -1), new(0, 0, 1) };
            var occ = new int[6];
            var nb = new System.Text.StringBuilder();
            int exposed = 0, changedNb = 0;
            for (int i = 0; i < 6; i++)
            {
                var nc = hit.cell + dl[i];
                int m = MatAt(nc), o = OrigMat(nc);
                occ[i] = m > 0 ? 1 : 0;
                if (m == 0) exposed++;
                string nm = m < 0 ? "oob" : m == 0 ? "air" : $"{m} {StAssetReader.GetMaterialName(m)}";
                if (m >= 0 && o != m) { nm += $" [was {o} {StAssetReader.GetMaterialName(o)} - REMOVED/CHANGED]"; changedNb++; }
                nb.Append($"{axes[i]}:{nm}  ");
            }
            sb.AppendLine($"  neighbors: {nb}({exposed}/6 exposed, {changedNb} changed by damage)");

            // Replica of the shader's shading inputs: hybrid normal = face normal (+ 30% SmoothNormal on side faces),
            // sun term = half-Lambert², shadow = voxel march toward the light (first N steps skipped, soft edge).
            float yr = -yaw * Mathf.Deg2Rad, ycos = Mathf.Cos(yr), ysin = Mathf.Sin(yr);
            Vector3 ToLocal(Vector3 v) => new Vector3(v.x * ycos + v.z * ysin, v.y, -v.x * ysin + v.z * ycos);
            Vector3 ToWorld(Vector3 v) => new Vector3(v.x * ycos - v.z * ysin, v.y, v.x * ysin + v.z * ycos);
            Vector3 nL = ToLocal(hit.normal);
            bool haveN = nL.sqrMagnitude > 0.5f;
            Vector3 g = new Vector3(-(occ[1] - occ[0]), -(occ[3] - occ[2]), -(occ[5] - occ[4]));
            bool sideFace = Mathf.Abs(nL.y) <= 0.5f, degenerate = g.sqrMagnitude < 1e-4f;
            Vector3 blended = haveN && sideFace && !degenerate ? (nL * 0.7f + g.normalized * 0.3f).normalized : nL;
            sb.AppendLine($"  shading  : face n(local) {nL:F2} → {(!haveN ? "unknown (ray started inside solid)" : sideFace ? "side face: smooth-normal blend applies" : "top/bottom face: raw normal, no blend")}");
            sb.AppendLine($"             neighbour gradient sum {g:F0} → {(degenerate ? "DEGENERATE (opposite neighbours cancel: 1-voxel strand/sheet)" + (sideFace ? " — blend skipped, flat face normal used (before the 2026-10-12 shader fix: normalize(0)=NaN → near-black face)" : " — not used for this face") : "ok")}  blended n {blended:F2}");

            string sunLine = "n/a (no lighting info)", sunShort = "n/a";
            Vector3 L = chunkManager != null ? chunkManager.GetLightDirection() : Vector3.zero;
            if (haveN && L.sqrMagnitude > 0.5f)
            {
                Vector3 sl = ToLocal(L);
                float ndl = Vector3.Dot(blended, sl), sunTerm = Mathf.Pow(ndl * 0.5f + 0.5f, 2f);
                int skip = chunkManager.GetShadowSkipSteps(), maxSteps = chunkManager.GetShadowMaxSteps();
                float nn = chunkManager.GetShadowNormalNudge(), ln = chunkManager.GetShadowLightNudge();
                string shadow;
                if (chunkManager.GetShadowEnabled() == 0) shadow = "shadows disabled";
                else
                {
                    float l1 = Mathf.Abs(sl.x) + Mathf.Abs(sl.y) + Mathf.Abs(sl.z);
                    float skipM = skip / l1 * vs, maxM = maxSteps / l1 * vs;
                    Vector3 org = hit.point + ToWorld(blended) * (vs * nn) + L * (vs * ln);
                    shadow = World != null && World.Raycast(org + L * skipM, L, Mathf.Max(0.01f, maxM - skipM), out var sh)
                        ? $"BLOCKED by [{sh.volumeId ?? "terrain"}] cell {sh.cell} mat {sh.material} '{StAssetReader.GetMaterialName(sh.material)}' ~{skipM + sh.distance:F2}m along the ray"
                        : "CLEAR";
                }
                bool blockedS = shadow.StartsWith("BLOCKED");
                sunShort = blockedS ? "SHADOWED" : ndl <= 0f ? "faces away" : "lit";
                sunLine = $"to-light dir {L:F2}  N·L {ndl:F2}  half-Lambert² sun term {sunTerm:F2}\n"
                    + $"             shadow ray (≈ skip {skip} / max {maxSteps} steps, nudges n×{nn} L×{ln} voxels): {shadow}\n"
                    + $"             verdict: {(blockedS ? "SHADOWED — sun term removed, ambient only" : ndl <= 0f ? "faces away from the sun" : "sun-lit")}";
            }
            sb.AppendLine("  sun      : " + sunLine);

            Vector3 markerPos; Quaternion markerRot;
            if (isVol)
            {
                Vector3 half = new Vector3(w, h, d) * (vs * 0.5f);
                Vector3 lc = new Vector3(hit.cell.x + 0.5f, hit.cell.y + 0.5f, hit.cell.z + 0.5f) * vs - half;
                float rad = yaw * Mathf.Deg2Rad, rc = Mathf.Cos(rad), rs = Mathf.Sin(rad);
                markerPos = corner + half + new Vector3(lc.x * rc - lc.z * rs, lc.y, lc.x * rs + lc.z * rc);
                markerRot = Quaternion.Euler(0f, yaw, 0f);
            }
            else { markerPos = World != null ? World.VoxelGridToWorld(hit.cell) : hit.point; markerRot = Quaternion.identity; }
            pdr?.SetCustomCube("inspect_cell", markerPos, markerRot, vs * 1.08f, new Color(1f, 0.9f, 0.1f, 0.95f));

            var resCls = VoxelDamageMath.ClassOf((uint)mat);
            int resProf = VoxelDamageMath.ResProfileOf(vol);
            inspectHud = $"🔍 {hit.cell} · {mat} {matName} · {vol}\n   {exposed}/6 exposed · res {VoxelDamageMath.MaterialResistance((uint)mat, resProf):F2} ({VoxelDamageMath.ResClassNames[(int)resCls]} · {VoxelDamageMath.ResProfileNames[resProf]}) · normal {(degenerate && sideFace ? "degenerate→flat" : "ok")} · sun {sunShort}{(damaged && changedNb > 0 ? $" · {changedNb} nb changed" : "")}";
            string report = sb.ToString().TrimEnd();
            Debug.Log($"[DebugHUD] {report}");
            try
            {
                string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Logs"));
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "voxel_inspect.log"), $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}  {report}\n\n");
            }
            catch (System.Exception e) { Debug.LogWarning($"[DebugHUD] voxel_inspect.log write failed: {e.Message}"); }
        }

        /// <summary>Arm/cancel a voxel pick — the click is consumed by CityMap3D's pick mode.</summary>
        private void ArmPick(PickSlot slot)
        {
            pickArmed = pickArmed == slot ? PickSlot.None : slot;
            cityMap.voxelPickArmed = pickArmed != PickSlot.None;
        }

        /// <summary>Open the HUD on the Combat tab for the given range block.</summary>
        public void OpenCombatTab(string blockId)
        {
            rangeBlockId = blockId;
            activeTab = Tab.Combat;
            visible = true;
            minimized = false;
        }

        /// <summary>Voxel-inspect readout — shared by Combat and Demolitions pick rows.</summary>
        void DrawInspectHud()
        {
            if (inspectHud == null) return;
            GUILayout.Label(inspectHud, labelStyle);
            if (GUILayout.Button("clear inspect", GUILayout.Height(18)))
            {
                inspectHud = null;
                PathDebugRenderer.Instance?.ClearCustomCube("inspect_cell");
            }
        }

        /// <summary>Stand/aim/blast pick status + clear — shared; picks persist across tabs.</summary>
        void DrawPickStatus()
        {
            if (!pickedStand.HasValue && !pickedAim.HasValue && !pickedBlast.HasValue) return;
            GUILayout.Label($"stand: {(pickedStand.HasValue ? pickedStand.Value.ToString("F1") : "—")}   aim: {(pickedAim.HasValue ? pickedAim.Value.ToString("F1") : "—")}   blast: {(pickedBlast.HasValue ? pickedBlast.Value.ToString("F1") : "—")}", labelStyle);
            if (GUILayout.Button("clear picks", GUILayout.Height(18)))
            {
                pickedStand = pickedAim = pickedBlast = null; blastVolume = null; blastAtVolumeCenter = false;
                var pdr = PathDebugRenderer.Instance;
                foreach (var n in new[] { "pick_stand", "pick_aim", "pick_aim2", "pick_blast", "pick_blast2", "pick_blast3", "blast_rx", "blast_ry", "blast_rz" })
                    pdr?.ClearCustomBeam(n);
            }
        }

        void DrawCombatTab()
        {
            var sim = gameUI != null ? gameUI.Sim : null;
            string simState = sim != null ? sim.State.ToString() : "no sim";

            GUILayout.Label($"<b>COMBAT</b>   sim: {simState}", boldStyle);

            // --- TEST LOTS — authored testbeds, always reachable -----------
            // Discovery = authored attachment points (the same anchor-routing
            // block clicks use). No order, dwell state, or map click needed —
            // the debug menu itself stages the pit any time.
            if (SecFold("c.lots", "TEST LOTS — cover pit"))
            {
            string pitId = PitBlockId;
            if (pitId != null)
            {
                bool clickedPit = rangeBlockId == pitId;   // combat section shows its own START DUEL then
                var pitTan = TanSuitRef;
                var pitVinny = cityMap.SpawnedCharacter;
                GUILayout.Space(4);
                GUILayout.Label($"<b>TEST LOTS</b>   cover pit <i>({pitId})</i>", labelStyle);
                if (GUILayout.Button("SETUP PIT — Vinny→A, Tan Suit→B", tabStyle, GUILayout.Height(24)))
                    SetupCoverPit(pitId);
                GUILayout.BeginHorizontal();
                GUILayout.Label("opponent:", labelStyle, GUILayout.Width(62));
                if (GUILayout.Toggle(pitTanMode == PitTanMode.Dummy, " dummy", pitTanMode == PitTanMode.Dummy ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
                {
                    pitTanMode = PitTanMode.Dummy;
                    var sd = pitTan != null ? pitTan.GetComponent<CombatController>() : null;
                    if (sd != null && sd.State != CombatController.CombatState.Idle) sd.ExitCombat();
                }
                if (GUILayout.Toggle(pitTanMode == PitTanMode.Hide, " hide", pitTanMode == PitTanMode.Hide ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
                    pitTanMode = PitTanMode.Hide;
                if (GUILayout.Toggle(pitTanMode == PitTanMode.Fight, " fight", pitTanMode == PitTanMode.Fight ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
                    pitTanMode = PitTanMode.Fight;
                GUILayout.EndHorizontal();
                pitSwapSides = GUILayout.Toggle(pitSwapSides,
                    " swap sides (parity test — vinny→north)", toggleStyle, GUILayout.Height(18));
                if (!clickedPit && pitVinny != null)
                {
                    var vcc = pitVinny.GetComponent<CombatController>();
                    if (vcc == null) vcc = pitVinny.gameObject.AddComponent<CombatController>();
                    GUI.enabled = pitTan != null && vcc.State == CombatController.CombatState.Idle;
                    if (GUILayout.Button(pitTanMode == PitTanMode.Fight ? "START DUEL — both seek cover"
                                         : pitTanMode == PitTanMode.Hide ? "START DUEL — Vinny hunts, Tan hides"
                                         : "START DUEL — Vinny vs target dummy",
                            tabStyle, GUILayout.Height(24)))
                        StartPitDuel(pitVinny, pitTan);
                    GUI.enabled = true;
                }
                if (pitVinny != null) PitCoverPickRow(pitId, pitVinny, pitTan);

                // DUEL — live per-fighter status. LastResult carries the
                // EnterCombat verdict verbatim ("no cover within 8m —
                // holding" vs "seeking cover behind X"), which is the whole
                // pick-vs-corridor-vs-arrival question in one string.
                if (SecFold("c.duel", "DUEL — live status"))
                {
                    DuelStatusRow("vinny", pitVinny);
                    DuelStatusRow("tan",   pitTan);
                }
                // TEMPO — per-beat dwell floors. Every beat holds its
                // seconds minimum; event verdicts stay honest inside (the
                // timer gates the LEAVE, never the observation).
                var scc = pitVinny != null ? pitVinny.GetComponent<CombatController>() : null;
                var pitTanCC = pitTan != null ? pitTan.GetComponent<CombatController>() : null;
                if (scc != null && SecFold("c.tempo", "TEMPO — beat lengths"))
                {
                GUILayout.BeginHorizontal();
                var pitTanSen = pitTan != null ? pitTan.GetComponent<CoverSensor>() : null;
                if (GUILayout.Button($"refresh cover spots ({coverSpots.Count}+{coverSpotsTan.Count})", GUILayout.Height(20)))
                {
                    RebuildCoverSpots(pitVinny.GetComponent<CoverSensor>(),
                        CharFeetCenter(pitVinny), coverSpots);
                    RebuildCoverSpots(pitTanSen,
                        pitTan != null ? CharFeetCenter(pitTan) : Vector3.zero, coverSpotsTan);
                }
                showCoverSpots = GUILayout.Toggle(showCoverSpots, " show", toggleStyle, GUILayout.Width(70));
                GUILayout.EndHorizontal();
                for (int i = 1; scc.beatDwell != null && i < scc.beatDwell.Length; i++)
                {
                    var bn = (CombatController.EngagementBeat)i;
                    bool active = scc.Beat == bn;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(active
                            ? $"▶ {bn.ToString().ToLowerInvariant()} · {scc.BeatLeft:F1}s left"
                            : $"   {bn.ToString().ToLowerInvariant()}",
                        active ? toggleStyle : labelStyle, GUILayout.Width(140));
                    if (GUILayout.Button("−", tabStyle, GUILayout.Width(22), GUILayout.Height(18)))
                        scc.beatDwell[i] = Mathf.Max(0.5f, scc.beatDwell[i] - 0.5f);
                    GUILayout.Label($"{scc.beatDwell[i]:F1}s", labelStyle, GUILayout.Width(38));
                    if (GUILayout.Button("+", tabStyle, GUILayout.Width(22), GUILayout.Height(18)))
                        scc.beatDwell[i] = Mathf.Min(30f, scc.beatDwell[i] + 0.5f);
                    GUILayout.EndHorizontal();
                }
                GUILayout.Label($"beat: {scc.Beat.ToString().ToLowerInvariant()} · state {scc.State}", labelStyle);
                if (showCoverSpots)
                {
                    // Auto-refresh — spot verdicts (usable/dimmed) are
                    // threat-relative; a manual button would go stale mid-fight.
                    if (Time.unscaledTime >= nextCoverSpotRebuild)
                    {
                        nextCoverSpotRebuild = Time.unscaledTime + 0.75f;
                        RebuildCoverSpots(pitVinny.GetComponent<CoverSensor>(),
                            CharFeetCenter(pitVinny), coverSpots);
                        RebuildCoverSpots(pitTanSen,
                            pitTan != null ? CharFeetCenter(pitTan) : Vector3.zero, coverSpotsTan);
                    }
                    // Both fighters' quality fields — vinny cyan posts, tan
                    // amber — plus each one's scan-radius ring (the awareness
                    // bubble the spots live inside).
                    PaintCoverSpots(scc, coverSpots, "", false);
                    PaintCoverSpots(pitTanCC, coverSpotsTan, "_t", true);
                    PaintRadiusRing(pitVinny.GetComponent<CoverSensor>(), pitVinny, "", false);
                    PaintRadiusRing(pitTanSen, pitTan, "_t", true);
                }
                else if (coverSpots.Count + coverSpotsTan.Count > 0)
                { ClearCoverSpots(); coverSpots.Clear(); coverSpotsTan.Clear(); }
                else ClearCoverSpots();
                }
                else ClearCoverSpots();
                // Feet/cert squares + destination posts paint at window level
                // (after EndScrollView) so they track the body on every tab.
                if (pitTan == null)
                    GUILayout.Label("<color=#ffaa44>Civilian_02 (Tan Suit) not found.</color>", labelStyle);
            }
            }

            // SFX preview — sim-state-independent; plays at the AudioListener, so
            // distance can't mask a pipeline problem.
            // Plays at the AudioListener, so distance can't mask a pipeline problem.
            var dmgSys = VoxelDamageSystem.GetOrCreate();

            // --- Free-fire picks (combat-facing; blast picks live on Demolitions) ---
            GUILayout.Space(4);
            if (SecFold("c.picks", "Free-fire &amp; picks"))
            {
            GUILayout.BeginHorizontal();
            bool standArmed = pickArmed == PickSlot.Stand;
            if (GUILayout.Button(standArmed ? "STAND: click map…" : "PICK STAND",
                    standArmed ? tabActiveStyle : tabStyle, GUILayout.Height(22))) ArmPick(PickSlot.Stand);
            bool aimArmed = pickArmed == PickSlot.Aim;
            if (GUILayout.Button(aimArmed ? "AIM: click map…" : "PICK AIM",
                    aimArmed ? tabActiveStyle : tabStyle, GUILayout.Height(22))) ArmPick(PickSlot.Aim);
            bool inspArmed = pickArmed == PickSlot.Inspect;
            if (GUILayout.Button(inspArmed ? "VOXEL: click map…" : "PICK VOXEL",
                    inspArmed ? tabActiveStyle : tabStyle, GUILayout.Height(22))) ArmPick(PickSlot.Inspect);
            GUILayout.EndHorizontal();
            DrawInspectHud();
            DrawPickStatus();
            }

            // --- Target source: a range block's lane points, a cover pit's
            //     stand points, or free-fire picks ---
            bool freeFire = string.IsNullOrEmpty(rangeBlockId);
            bool hasPos, hasTgt, pit = false;
            Vector3 posWorld = Vector3.zero, tgtWorld = Vector3.zero;
            // Target resolution is unconditional — the fold below only gates
            // the DISPLAY; fire controls + IK aim read hasPos/hasTgt/tgtWorld.
            List<string> pointNames = null;
            string posName = null, tgtName = null;
            Vector3 pitStandBW = Vector3.zero;
            if (freeFire)
            {
                hasPos = true;
                hasTgt = pickedAim.HasValue;
                if (hasTgt) tgtWorld = pickedAim.Value;
            }
            else
            {
                pointNames = cityMap.GetBuildingPointNames(rangeBlockId);
                // Split calls — a &&-chained out-var stays unassigned on
                // short-circuit, and `if (pit)` can't re-prove it (CS0165).
                bool pitA = cityMap.TryGetBuildingPointLocal(rangeBlockId, "cover_stand_a", out var pitALocal);
                bool pitB = cityMap.TryGetBuildingPointLocal(rangeBlockId, "cover_stand_b", out var pitBLocal);
                pit = pitA && pitB;
                if (pit)
                {
                    hasPos = true;
                    posWorld = cityMap.MapRoot.position + pitALocal;
                    pitStandBW = cityMap.MapRoot.position + pitBLocal;
                    var tanCh = TanSuitRef;
                    tgtWorld = (tanCh != null ? CharFeetCenter(tanCh) : pitStandBW) + Vector3.up * 1.3f;
                    hasTgt = true;
                }
                else
                {
                    bool moldAvailable = pointNames.Contains("glass_target");
                    if (!moldAvailable && RangeAimMold) RangeAimMold = false;
                    posName = $"firing_position_{RangeLane}";
                    tgtName = RangeAimMold ? "glass_target" : $"firing_target_{RangeLane}";
                    // fall back to the pre-multilane names for older range assets
                    hasPos = cityMap.TryGetBuildingPointLocal(rangeBlockId, posName, out var posLocal)
                          || cityMap.TryGetBuildingPointLocal(rangeBlockId, "firing_position", out posLocal);
                    hasTgt = cityMap.TryGetBuildingPointLocal(rangeBlockId, tgtName, out var tgtLocal)
                          || cityMap.TryGetBuildingPointLocal(rangeBlockId, "firing_target", out tgtLocal);
                    posWorld = cityMap.MapRoot.position + posLocal;
                    tgtWorld = cityMap.MapRoot.position + tgtLocal;
                }
                if (pickedAim.HasValue) { tgtWorld = pickedAim.Value; hasTgt = true; }
            }

            if (SecFold("c.target", freeFire ? "target — free-fire" : $"target — {rangeBlockId}"))
            {
            if (freeFire)
            {
                GUILayout.Label(hasTgt
                    ? $"free-fire target: <b>{tgtWorld.ToString("F1")}</b>"
                    : "PICK AIM a voxel to arm the fire controls.", labelStyle);
            }
            else
            {
            GUILayout.Label($"Block: <b>{rangeBlockId}</b>", labelStyle);
            GUILayout.Label($"Points: {(pointNames.Count > 0 ? string.Join(", ", pointNames) : "none")}", labelStyle);
            if (pit)
            {
                // --- Cover pit clicked — the pit controls live in TEST LOTS
                // above (no block click, order, or dwell needed).
                GUILayout.Label("<b>Cover pit</b> — controls in TEST LOTS above.", labelStyle);
                var tanCh = TanSuitRef;
                if (tanCh != null)
                {
                    var tp = CharFeetCenter(tanCh);
                    float off = Vector2.Distance(new Vector2(tp.x, tp.z), new Vector2(pitStandBW.x, pitStandBW.z));
                    GUILayout.Label($"Tan Suit at stand_b: {(off < 0.35f ? "<color=#66ff66>YES</color>" : $"<color=#ffaa44>off {off:F2}m</color>")}", labelStyle);
                }
            }
            else
            {
            // --- Lane + aim-point selection ---
            GUILayout.BeginHorizontal();
            GUILayout.Label("lane:", labelStyle, GUILayout.Width(38));
            for (int i = 1; i <= 3; i++)
            {
                if (GUILayout.Toggle(RangeLane == i, $" {i}", RangeLane == i ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
                {
                    if (RangeLane != i)
                    {
                        RangeLane = i;
                        MoveCharacterToLane(rangeBlockId, i);
                    }
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("aim:", labelStyle, GUILayout.Width(38));
            if (GUILayout.Toggle(!RangeAimMold, " lane target", !RangeAimMold ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
                RangeAimMold = false;
            bool moldAvailable = pointNames.Contains("glass_target");
            GUI.enabled = moldAvailable;
            if (GUILayout.Toggle(RangeAimMold, " glass mold", RangeAimMold ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
            {
                RangeAimMold = true;
                RangeLane = 3;   // the mold lives on lane 3
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label($"{posName}: {(hasPos ? "OK" : "<color=#ff6666>MISSING</color>")}   {tgtName}: {(hasTgt ? "OK" : "<color=#ff6666>MISSING</color>")}", labelStyle);
            }
            }
            }

            GUILayout.Space(6);

            // --- Character / weapon status ---
            var ch = cityMap.SpawnedCharacter;
            if (ch == null)
            {
                GUILayout.Label("<color=#ff6666>No spawned character.</color>", labelStyle);
                return;
            }
            var mount = ch.GetComponent<WeaponMount>();
            if (solver == null || solver.gameObject != ch.gameObject)
            {
                solver = ch.GetComponent<ArmAimSolver>();
                if (solver == null) solver = ch.gameObject.AddComponent<ArmAimSolver>();
            }

            if (SecFold("c.status", "character / weapon"))
            {
            // Position check — character transform is the volume corner; the
            // firing position target is where the XZ center should sit.
            if (!freeFire && mount != null && hasPos)
            {
                var d = mount.Dims; float vs = mount.CharVoxelSize;
                Vector3 chCenter = ch.transform.position + new Vector3(d.x * vs * 0.5f, 0f, d.z * vs * 0.5f);
                float offM = Vector3.Distance(new Vector3(chCenter.x, 0, chCenter.z), new Vector3(posWorld.x, 0, posWorld.z));
                GUILayout.Label($"In position: {(offM < 0.35f ? "<color=#66ff66>YES</color>" : $"<color=#ffaa44>off {offM:F2}m</color>")}", labelStyle);
            }
            else if (freeFire && pickedStand.HasValue)
                GUILayout.Label($"stand: <b>{pickedStand.Value.ToString("F1")}</b> — wherever PICK STAND left him", labelStyle);
            GUILayout.Label($"Weapon: {(mount != null && mount.IsEquipped ? mount.ItemFileName : "<color=#ff6666>none</color>")}", labelStyle);
            if (mount != null && mount.IsEquipped)
            {
                GUILayout.Label($"  bore axis: {(mount.BoreAxisLocal != Vector3.right ? "measured" : "+X fallback")}  hand gid {mount.HandGid}", labelStyle);
                mount.showMuzzleRay = GUILayout.Toggle(mount.showMuzzleRay, " muzzle ray (red bore axis)", toggleStyle);
                solver.showSightBeam = GUILayout.Toggle(solver.showSightBeam, " sight line (cyan, while aiming)", toggleStyle);
            }
            }

            GUILayout.Space(6);

            // --- Unified fighter aim/sight viz — the same five rays for every fighter,
            // toggled side-by-side instead of scattered across per-tab per-component folds.
            var projV = ProjectileSystem.GetOrCreate();
            if (SecFold("c.aimviz", "AIM & SIGHT — uniform per-fighter viz"))
            {
            FighterVizRow("VINNY", ch, projV);
            FighterVizRow("TAN", TanSuitRef, projV);
            GUILayout.Space(4);
            bool trV = GUILayout.Toggle(projV.showTracers, " tracers", toggleStyle);
            if (trV != projV.showTracers) { projV.showTracers = trV; if (!trV) projV.ClearTracers(); }
            bool imV = GUILayout.Toggle(projV.showImpactMarks, " impact marks", toggleStyle);
            if (imV != projV.showImpactMarks) { projV.showImpactMarks = imV; if (!imV) projV.ClearImpactMarks(); }
            fighterBeams = GUILayout.Toggle(fighterBeams, " fighter beams (body squares / dst posts)", toggleStyle);
            GUILayout.Label("<i>sight=cyan aim line · bore=red muzzle ray · fov=vision cone rim · eye→tgt=vision ray · det=detect marks · los=threat→eye probe · cone=spread wireframe (one owner at a time)</i>", labelStyle);
            }

            GUILayout.Space(6);

            // --- Combat mode: autonomous cover-seek (COMBAT_COVER_STATE.md) ---
            // Threat = the PICK AIM point — simulates a shooter standing there.
            var cc = ch.GetComponent<CombatController>();
            if (cc == null) cc = ch.gameObject.AddComponent<CombatController>();
            if (SecFold("c.combat", "combat mode (autonomous)"))
            {
            if (cc.State == CombatController.CombatState.Idle)
            {
                // Pit mode: symmetric duel — both combatants enter combat,
                // each keyed on the other's live feet (snapshot at press).
                var tanCh = pit ? TanSuitRef : null;
                // Dummy mode needs the body, not a working combat stack on it.
                bool hasThreat = pit ? tanCh != null : pickedAim.HasValue;
                GUI.enabled = hasThreat;
                string btn = pit
                    ? (tanCh != null
                        ? (pitTanMode == PitTanMode.Fight ? "START DUEL — both seek cover"
                           : pitTanMode == PitTanMode.Hide ? "START DUEL — Vinny hunts, Tan hides"
                           : "START DUEL — Vinny vs target dummy")
                        : "START DUEL (Civilian_02 not found)")
                    : (hasThreat ? "ENTER COMBAT — Vinny ONLY vs PICK AIM"
                                 : "ENTER COMBAT (PICK AIM a threat first)");
                if (GUILayout.Button(btn, tabStyle, GUILayout.Height(24)))
                {
                    if (pit) StartPitDuel(ch, tanCh);
                    else cc.EnterCombat(pickedAim.Value);
                }
                GUI.enabled = true;
            }
            else
            {
                // Readouts live on the F5 COVER+COMBAT panel — the tab keeps
                // controls only. The centroid feed stays: it's functional
                // input to the IK-aim section below, not a display label.
                if (GUILayout.Button("EXIT COMBAT", tabStyle, GUILayout.Height(22))) cc.ExitCombat();
                if (cc.ThreatCharacter != null)
                {
                    var vv = cc.Visibility;
                    if (vv.visible > 0) { tgtWorld = vv.centroid; hasTgt = true; }
                    else if (cc.LastSeenTarget.HasValue) { tgtWorld = cc.LastSeenTarget.Value; hasTgt = true; }
                }
            }
            // Exposure controls — probe viz + tuning. With the toggle on and
            // no combat probing, the HUD drives a slow manual probe so the
            // beams preview the duel's firing window; the verdict readout
            // itself is on the F5 panel.
            {
                var cs = ch.GetComponent<CoverSensor>() ?? ch.gameObject.AddComponent<CoverSensor>();
                var pitTan = pit ? TanSuitRef : null;
                cs.showTargetViz = GUILayout.Toggle(cs.showTargetViz, " probe rays (grn=visible, red=blocked, cyan=centroid)", toggleStyle);
                cs.showImpactMarks = GUILayout.Toggle(cs.showImpactMarks, " impact marks (grn=seen·in-cone, slate=off-cone, red=occluder)", toggleStyle);
                if ((cs.showTargetViz || cs.showImpactMarks) && pitTan != null
                    && cc.State == CombatController.CombatState.Idle)
                    cs.TryGetVisibleVoxels(pitTan, out _);   // per-frame — same live rate combat runs
                // Pre-fight controls — not gated on combat state: cover gap is
                // a pick-time input and the loop is a loadout decision you arm
                // BEFORE the duel starts.
                cc.loopBeats = GUILayout.Toggle(cc.loopBeats, " loop beats (re-rise after fail)", toggleStyle);
                cs.standoff = HSlider("cover standoff", cs.standoff, 0.1f, 0.8f);
                if (cc.State != CombatController.CombatState.Idle)
                {
                    cc.visInterval = HSlider("probe interval", cc.visInterval, 0.05f, 0.5f);
                    cc.fireInterval = HSlider("fire cadence", cc.fireInterval, 0.3f, 3f);
                    cc.roundLoadInterval = HSlider("round load", cc.roundLoadInterval, 0.2f, 3f);
                    // Master tempo — sets every beatDwell at once; the
                    // TEMPO table (TEST LOTS) tunes them individually.
                    float master = cc.beatDwell != null && cc.beatDwell.Length > 1
                        ? cc.beatDwell[1] : 5f;
                    float nm = HSlider("all beats dwell", master, 0.5f, 15f);
                    if (cc.beatDwell != null && !Mathf.Approximately(nm, master))
                        for (int bi = 1; bi < cc.beatDwell.Length; bi++) cc.beatDwell[bi] = nm;
                }
            }
            // Pit depth-ladder picks — same row as TEST LOTS so it's visible
            // whichever path the user took (block click or the lots panel).
            if (pit && PitBlockId != null)
                PitCoverPickRow(PitBlockId, ch, TanSuitRef);
            cc.moveSpeed = HSlider("move speed", cc.moveSpeed, 0.5f, 5f);
            GUILayout.Label("cover detection tuning lives on the COVER tab", labelStyle);
            }

            GUILayout.Space(6);

            // --- IK Aim toggle ---
            if (SecFold("c.aim", "IK aim &amp; telemetry"))
            {
            bool aiming = solver.IsAiming;
            var btnStyle = aiming ? tabActiveStyle : tabStyle;
            // Aim requires a resolved target context — a clicked testbed
            // block's points or a free-fire pick. Not a sim-state ceremony.
            GUI.enabled = hasTgt && mount != null && mount.IsEquipped && (freeFire || hasPos);
            string btnLabel = aiming ? "IK AIM — ENGAGED (click to release)" : "IK AIM";
            if (GUILayout.Button(btnLabel, btnStyle, GUILayout.Height(26)))
            {
                if (aiming) solver.Disarm();
                else solver.Arm(tgtWorld);
            }
            GUI.enabled = true;
            if (!string.IsNullOrEmpty(solver.LastError))
                GUILayout.Label($"<color=#ff6666>solver: {solver.LastError}</color>", labelStyle);

            if (SecFold("c.fire", "fire controls"))
                DrawFireControls(hasPos || freeFire, mount, tgtWorld, hasTgt);

            // --- Telemetry ---
            if (solver.Blend > 0f || aiming)
            {
                GUILayout.Space(4);
                GUILayout.Label($"blend {solver.Blend:F2}   arm {solver.ArmGid} → fore {solver.ForearmGid}", labelStyle);
                GUILayout.Label($"target dist: {solver.LastTargetDistanceM:F2}m   aim err: {solver.LastAngularErrorDeg:F1}°", labelStyle);
                if (mount != null && mount.IsEquipped)
                {
                    GUILayout.Label($"muzzle: {mount.MuzzleWorld}", labelStyle);
                    GUILayout.Label($"aim dir: {mount.AimDirection}", labelStyle);
                }
            }
            }

            GUILayout.Space(6);

            // --- Tuning sliders (live) ---
            if (SecFold("c.tuning", "tuning (IK solver)"))
            {
            solver.torsoAssist = GUILayout.Toggle(solver.torsoAssist, " torso assist (turret yaw)", toggleStyle);
            if (solver.torsoAssist)
            {
                solver.torsoShare = HSlider("torso share", solver.torsoShare, 0f, 1f);
                solver.maxTorsoYawDeg = HSlider("max torso °", solver.maxTorsoYawDeg, 0f, 120f);
            }
            solver.reachFraction = HSlider("reach fraction", solver.reachFraction, 0.5f, 1f);
            solver.blendInSpeed = HSlider("blend in", solver.blendInSpeed, 0.5f, 8f);
            solver.blendOutSpeed = HSlider("blend out", solver.blendOutSpeed, 0.5f, 8f);
            solver.azimuthOffsetDeg = HSlider("azimuth °", solver.azimuthOffsetDeg, -45f, 45f);
            solver.elevationOffsetDeg = HSlider("elevation °", solver.elevationOffsetDeg, -45f, 45f);
            if (GUILayout.Button("Reset tuning", GUILayout.Height(20)))
            {
                solver.reachFraction = 0.97f;
                solver.blendInSpeed = 2.5f;
                solver.blendOutSpeed = 3.5f;
                solver.azimuthOffsetDeg = 0f;
                solver.elevationOffsetDeg = 0f;
                solver.torsoShare = 0.6f;
                solver.maxTorsoYawDeg = 70f;
            }
            }

            GUILayout.Space(6);

            // --- Channel kill-switches — bisect which driver jitters him ---
            if (SecFold("c.dbg", "channel kill-switches (jitter bisect)"))
            {
            cc.dbgFacing      = GUILayout.Toggle(cc.dbgFacing,      " facing driver (ALL combat yaw writes)", toggleStyle);
            cc.dbgEngagedTrack = GUILayout.Toggle(cc.dbgEngagedTrack, " engaged live-track (body facing)", toggleStyle);
            cc.dbgArmSolver   = GUILayout.Toggle(cc.dbgArmSolver,   " arm solver may raise in combat", toggleStyle);
            cc.dbgMoveYaw     = GUILayout.Toggle(cc.dbgMoveYaw,     " movement yaw (seek/swing steps)", toggleStyle);
            cc.dbgVisionFeed  = GUILayout.Toggle(cc.dbgVisionFeed,  " swing-dwell cone feed", toggleStyle);
            cc.yawWatch       = GUILayout.Toggle(cc.yawWatch,       " yaw logging ([Yaw]/[Yaw?] console)", toggleStyle);
            GUILayout.Label("flip one, rerun, watch him — the channel that silences the wiggle is the culprit", labelStyle);
            }
        }

        /// <summary>
        /// FORCES tab — yaw force monitor. One L◀|0.0|▶R bar per yaw writer
        /// (EMA-smoothed °/s actually written to the transform). Live tug-of-war
        /// detector: two rows lit at once = two channels fighting; EXTERNAL in
        /// red = yaw moved with no combat writer. The driver row shows what the
        /// facing machine WANTS (demand) even while the deadband keeps it silent.
        /// </summary>
        void DrawForcesTab()
        {
            var ch = cityMap != null ? cityMap.SpawnedCharacter : null;
            var cc = ch != null ? ch.GetComponent<CombatController>() : null;
            if (cc == null)
            {
                GUILayout.Label("spawn the character first (sim running)", labelStyle);
                return;
            }

            GUILayout.Label($"<b>YAW FORCES</b>   {ch.name}", boldStyle);
            GUILayout.Label(
                $"mode <b>{cc.DebugFaceMode}</b> · owner <b>{cc.DebugFaceOwner}</b>"
                + $" · demand <b>{cc.FaceDemandDeg:+0.0;-0.0;0.0}°</b>"
                + $" · yaw <b>{cc.transform.eulerAngles.y:F1}°</b>"
                + $" · beat <b>{cc.Beat}</b> · <b>{cc.State}</b>", labelStyle);

            GUILayout.Space(4);
            cc.dbgFacing       = GUILayout.Toggle(cc.dbgFacing,       " facing driver (master)", toggleStyle);
            cc.dbgMoveYaw      = GUILayout.Toggle(cc.dbgMoveYaw,      " movement yaw (seek/swing)", toggleStyle);
            cc.dbgEngagedTrack = GUILayout.Toggle(cc.dbgEngagedTrack, " engaged live-track", toggleStyle);

            GUILayout.Space(6);

            // Scale header — L / 0.0 / R over the bar column
            GUILayout.BeginHorizontal();
            GUILayout.Space(96);
            var hr = GUILayoutUtility.GetRect(170, 12);
            var mini = new GUIStyle(labelStyle) { fontSize = 10 };
            GUI.Label(new Rect(hr.x - 6, hr.y, 20, 12), "<b>L</b>", mini);
            GUI.Label(new Rect(hr.center.x - 10, hr.y, 30, 12), "<b>0.0</b>", mini);
            GUI.Label(new Rect(hr.xMax - 12, hr.y, 20, 12), "<b>R</b>", mini);
            GUILayout.Label("°/s written", labelStyle);
            GUILayout.EndHorizontal();

            if (cc.YawForce.Count == 0)
                GUILayout.Label("<i>no writers yet — bars appear as owners fire</i>", labelStyle);

            // Live writers float to the top — the fight shows itself.
            var rows = cc.YawForce.OrderByDescending(kv => Mathf.Abs(kv.Value));
            foreach (var kv in rows)
                DrawYawForceBar(kv.Key, kv.Value, hr.width, cc);

            GUILayout.Space(6);
            GUILayout.Label(
                "<i>sign = direction (L ◀ counterclockwise · R ▶ clockwise) ·"
                + " two rows lit at once = tug-of-war · EXTERNAL = an unattributed"
                + " writer (watchdog-caught)</i>", labelStyle);
        }

        void DrawYawForceBar(string who, float degsPerSec, float barW, CombatController cc)
        {
            const float full = 270f;   // °/s for full deflection
            GUILayout.BeginHorizontal(GUILayout.Height(16));
            GUILayout.Label(who.Length > 11 ? who.Substring(0, 11) : who, labelStyle,
                GUILayout.Width(92));
            var r = GUILayoutUtility.GetRect(barW, 14);

            // track + center zero line
            GUI.color = new Color(1f, 1f, 1f, 0.08f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            GUI.DrawTexture(new Rect(r.center.x - 0.5f, r.y, 1f, r.height),
                Texture2D.whiteTexture);

            // signed fill from center — blue = turning right, orange = left
            float f = Mathf.Clamp(degsPerSec / full, -1f, 1f);
            if (Mathf.Abs(f) > 0.004f)
            {
                float w = Mathf.Abs(f) * r.width * 0.5f;
                var fill = f > 0f
                    ? new Rect(r.center.x, r.y + 2, w, r.height - 4)
                    : new Rect(r.center.x - w, r.y + 2, w, r.height - 4);
                GUI.color = who == "EXTERNAL" ? new Color(1f, 0.3f, 0.3f, 0.95f)
                    : f > 0f ? new Color(0.45f, 0.85f, 1f, 0.95f)
                             : new Color(1f, 0.72f, 0.35f, 0.95f);
                GUI.DrawTexture(fill, Texture2D.whiteTexture);
            }
            GUI.color = Color.white;

            GUILayout.Label($"{degsPerSec:+0;-0;0}", labelStyle, GUILayout.Width(50));
            // Active-owner marker — the row the driver currently serves
            if (who == cc.DebugFaceOwner && cc.DebugFaceMode != CombatController.FacingMode.Free)
                GUILayout.Label("<color=#66ff66>▶</color>", labelStyle, GUILayout.Width(16));
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// COVER tab — Vinny's always-on cover-awareness sense, inspected live.
        /// Passive layer: walls (low/crouch/full) inside the scan radius, volume
        /// footprints, the radius ring itself — no threat needed. Set a threat
        /// (PICK THREAT, shared with Combat's PICK AIM) and the active layer
        /// lights up: threat-facing walls, validated stand-spot candidates with
        /// reject reasons, and optionally the threat→eye LOS rays.
        /// </summary>
        void DrawCoverTab()
        {
            var ch = cityMap != null ? cityMap.SpawnedCharacter : null;
            if (ch == null)
            {
                GUILayout.Label("spawn the character first (sim running)", labelStyle);
                return;
            }
            var sensor = ch.GetComponent<CoverSensor>();
            if (sensor == null) sensor = ch.gameObject.AddComponent<CoverSensor>();
            coverSensor = sensor;
            // This tab is a live consumer — drive rate-limited scans while open
            // (the ambient scan gate only runs when someone watches).
            sensor.Refresh();

            // --- Threat context ---
            if (SecFold("v.threat", "threat"))
            {
            GUILayout.BeginHorizontal();
            bool thrArmed = pickArmed == PickSlot.Aim;
            if (GUILayout.Button(thrArmed ? "THREAT: click map…" : "PICK THREAT",
                    thrArmed ? tabActiveStyle : tabStyle, GUILayout.Height(22)))
                ArmPick(PickSlot.Aim);
            GUI.enabled = pickedAim.HasValue;
            if (GUILayout.Button("CLEAR", tabStyle, GUILayout.Width(60), GUILayout.Height(22)))
            {
                pickedAim = null;
                sensor.Threat = null;
                var pdr0 = PathDebugRenderer.Instance;
                pdr0?.ClearCustomBeam("pick_aim");
                pdr0?.ClearCustomBeam("pick_aim2");
                sensor.Refresh(true);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(pickedAim.HasValue
                ? $"threat: <b>{pickedAim.Value.ToString("F1")}</b>"
                : "no threat — passive wall awareness only", labelStyle);
            }

            // --- Detection tuning ---
            GUILayout.Space(6);
            if (SecFold("v.detect", "detection"))
            {
            bool dirty = false;
            float v;
            v = HSlider("cover radius", sensor.radius, 4f, 40f);
            if (v != sensor.radius) { sensor.radius = v; dirty = true; }
            v = HSlider("standoff", sensor.standoff, 0.15f, 1.5f);
            if (v != sensor.standoff) { sensor.standoff = v; dirty = true; }
            GUILayout.Label(sensor.HeightsMeasured
                ? $"  silhouette top: stand <b>{sensor.HStand:F2}m</b> · crouch {sensor.HShallow:F2}m → {sensor.HDeep:F2}m" +
                  $"  (+{sensor.EyeFwd:F2}m lean) — measured off posed model"
                : $"  silhouette top: {sensor.HStand:F2}/{sensor.HShallow:F2}/{sensor.HDeep:F2}m — FALLBACK (no anim data)", labelStyle);
            GUILayout.Label($"  support: <b>{sensor.SupportY:F2}m</b> — column top under feet (standing on voxels adds)", labelStyle);
            v = HSlider("field res", sensor.fieldRes, 32f, 128f);
            if (v != sensor.fieldRes) { sensor.fieldRes = Mathf.RoundToInt(v); dirty = true; }
            v = HSlider("prox weight", sensor.proxWeight, 0f, 1f);
            if (v != sensor.proxWeight) { sensor.proxWeight = v; dirty = true; }
            v = HSlider("facing dot", sensor.facingDot, -0.6f, 0.4f);
            if (v != sensor.facingDot) { sensor.facingDot = v; dirty = true; }
            var sensorVc = sensor.GetComponent<VoxelCharacter>();
            if (sensorVc != null)
            {
                // The slider shows and edits the RESOLVED spec — never the
                // raw field. (auto) = torso-measured; drag-to-0 returns to auto.
                bool auto = sensorVc.footprintOverride <= 0f;
                v = HSlider(auto ? "body footprint m (auto)" : "body footprint m (set)",
                            sensorVc.BodyFootprint, 0f, 1.5f, "F2");
                if (sensorVc.BodyFootprint <= 1.5f
                    && Mathf.Abs(v - sensorVc.BodyFootprint) > 0.0005f)
                { sensorVc.footprintOverride = v; dirty = true; }
                v = HSlider("step up m", sensorVc.stepUpMax, 0.05f, 1.0f, "F2");
                if (v != sensorVc.stepUpMax) { sensorVc.stepUpMax = v; dirty = true; }
            }
            if (sensor.Field != null && sensor.Field.bodyHalfMeters > 0f)
                GUILayout.Label($"  walk cert: <b>{sensor.Field.bodyHalfMeters * 2f:F2}m</b> square (continuous)" +
                                $" · {sensor.Field.bodyHalfMeters:F2}m stand-off from hazards", labelStyle);
            if (World != null && World.Walk != null)
                GUILayout.Label($"  walk layer: <b>{World.Walk.BlocksAllocated}</b> blocks" +
                                $" · {World.Walk.CellsProbed:N0} cells cached" +
                                $" · {World.Walk.CellsInvalidated:N0} invalidated", labelStyle);
            if (dirty) sensor.Refresh(true);
            }

            // --- Visualization toggles ---
            GUILayout.Space(6);
            if (SecFold("v.viz", "visualize"))
            {
            bool dirty = false;
            void Tog(ref bool f, string label)
            {
                bool nv = GUILayout.Toggle(f, label, toggleStyle);
                if (nv != f) { f = nv; dirty = true; }
            }
            GUILayout.Label("   <i>V-key modes 4-6 paint this field on the world — beams below are legacy debug.</i>", labelStyle);
            Tog(ref sensor.showRing, " scan radius ring");
            Tog(ref sensor.showFootprints, " obstacle footprints (local frame)");
            Tog(ref sensor.showWalls, " cover walls  (green full / yellow crouch / red low)");
            Tog(ref sensor.tintWallFacing, "   dim walls not facing threat");
            Tog(ref sensor.showCandidates, " candidates  (green ok / red rejected / cyan pick)");
            Tog(ref sensor.showLos, " threat→eye LOS rays");
            Tog(ref sensor.showHeat, " ground-quad field (use V-key instead)");
            GUILayout.BeginHorizontal();
            GUILayout.Label("   layer:", labelStyle, GUILayout.Width(52));
            foreach (CoverField.Layer lyr in System.Enum.GetValues(typeof(CoverField.Layer)))
            {
                bool sel = sensor.heatLayer == lyr;
                // Composite renders the threat-isolated score — the pick surface.
                string lbl = lyr == CoverField.Layer.Composite ? "Threat" : lyr.ToString();
                if (GUILayout.Toggle(sel, lbl, sel ? tabActiveStyle : tabStyle,
                        GUILayout.Height(18)) && !sel)
                { sensor.heatLayer = lyr; dirty = true; }
            }
            GUILayout.EndHorizontal();
            if (dirty) sensor.Refresh(true);
            if (GUILayout.Button("RESCAN NOW", tabStyle, GUILayout.Height(20)))
                sensor.Refresh(true);
            }

            // --- Live stats ---
            GUILayout.Space(6);
            if (SecFold("v.scan", "scan"))
            {
            var st = sensor.stats;
            GUILayout.Label($"  {st.obstacles} volumes · {st.wallCols} wall cols" +
                $"  (full {st.wallsFull} / crouch {st.wallsCrouch} / low {st.wallsLo})", labelStyle);
            if (sensor.HasThreat)
            {
                string reasons = st.reasons != null && st.reasons.Count > 0
                    ? "   (" + string.Join("  ", System.Linq.Enumerable.Select(
                        st.reasons, kv => $"{kv.Key}×{kv.Value}")) + ")"
                    : "";
                GUILayout.Label($"  {st.facing} threat-facing → {st.tested} candidates, " +
                    $"<b>{st.valid} valid</b>{reasons}", labelStyle);
            }
            }
        }

        /// <summary>
        /// VISION tab — Vinny's vision sense, inspected live. The cone rides the
        /// POSED head joint, so the Looking state's authored head-yaw sweep is the
        /// scan itself. Detection builds while a target sits inside an unblocked
        /// cone and decays outside it — the sweep produces intermittent exposure.
        /// Testbed target = the shared PICK AIM point ("the thing he's looking at").
        /// </summary>
        private VoxelCharacter spreadConeFighter;   // spread cone is single-owner — last checked fighter wins

        /// <summary>Uniform aim/sight debug toggles for one fighter — identical rays for
        /// Vinny and Tan so both can be verified side-by-side. Each toggle maps to the
        /// component that already owns that beam (ArmAimSolver/WeaponMount/VisionSensor/
        /// CoverSensor); the spread cone is single-owner (aimSource follows ONE mount).</summary>
        void FighterVizRow(string name, VoxelCharacter ch, ProjectileSystem proj)
        {
            GUILayout.Label($"<b>{name}</b>", labelStyle);
            if (ch == null) { GUILayout.Label("  <i>not spawned</i>", labelStyle); return; }
            var mnt = ch.GetComponent<WeaponMount>();
            var slv = ch.GetComponent<ArmAimSolver>();
            var vis = ch.GetComponent<VisionSensor>();
            var cov = ch.GetComponent<CoverSensor>();
            GUILayout.BeginHorizontal();
            if (slv != null) slv.showSightBeam = GUILayout.Toggle(slv.showSightBeam, " sight", toggleStyle);
            if (mnt != null)
            {
                GUI.enabled = mnt.IsEquipped;
                mnt.showMuzzleRay = GUILayout.Toggle(mnt.showMuzzleRay, " bore", toggleStyle);
                GUI.enabled = true;
            }
            if (vis != null) vis.showCone = GUILayout.Toggle(vis.showCone, " fov", toggleStyle);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (vis != null)
            {
                vis.showTargetRay = GUILayout.Toggle(vis.showTargetRay, " eye→tgt", toggleStyle);
                vis.showDetectMarks = GUILayout.Toggle(vis.showDetectMarks, " det", toggleStyle);
            }
            if (cov != null) cov.showLos = GUILayout.Toggle(cov.showLos, " los", toggleStyle);
            GUI.enabled = mnt != null && mnt.IsEquipped;
            bool coneOn = spreadConeFighter == ch && proj.showSpreadCone;
            if (GUILayout.Toggle(coneOn, " cone", toggleStyle) != coneOn)
            {
                if (!coneOn) { spreadConeFighter = ch; proj.showSpreadCone = true; }
                else { spreadConeFighter = null; proj.showSpreadCone = false; }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (spreadConeFighter == ch && proj.showSpreadCone && mnt != null)
            {
                proj.aimSource = mnt;
                proj.aimTarget = null;          // auto length (4 m fan)
                proj.aimSourceStamp = Time.time;
            }
        }

        void DrawVisionTab()
        {
            var ch = cityMap != null ? cityMap.SpawnedCharacter : null;
            if (ch == null)
            {
                GUILayout.Label("spawn the character first (sim running)", labelStyle);
                return;
            }
            var sensor = ch.GetComponent<VisionSensor>();
            if (sensor == null) sensor = ch.gameObject.AddComponent<VisionSensor>();
            visionSensor = sensor;
            // Inspect-claims-own: the sensor being tuned drives the shared
            // _VisCone globals (otherwise Tan Suit's sensor can overwrite them).
            if (VisionSensor.ConeOwner == null) VisionSensor.ConeOwner = sensor;

            // --- Target context ---
            GUILayout.Label("<b>Target</b>", labelStyle);
            GUILayout.BeginHorizontal();
            bool aimArmed = pickArmed == PickSlot.Aim;
            if (GUILayout.Button(aimArmed ? "TARGET: click map…" : "PICK TARGET",
                    aimArmed ? tabActiveStyle : tabStyle, GUILayout.Height(22)))
                ArmPick(PickSlot.Aim);
            GUILayout.EndHorizontal();
            GUILayout.Label(pickedAim.HasValue
                ? $"observing: <b>{pickedAim.Value.ToString("F1")}</b>"
                : "no target — pick a point for Vinny to watch", labelStyle);

            // --- Perception tuning ---
            GUILayout.Space(6);
            GUILayout.Label("<b>Perception</b>", labelStyle);
            sensor.range = HSlider("range", sensor.range, 5f, 60f);
            sensor.halfAngleDeg = HSlider("half angle °", sensor.halfAngleDeg, 15f, 90f);
            sensor.riseTime = HSlider("detect rise (s)", sensor.riseTime, 0.1f, 3f);
            sensor.decayTime = HSlider("forget decay (s)", sensor.decayTime, 0.5f, 5f);
            GUILayout.Label($"  eye: <b>{sensor.EyePos.y:F2}m</b> — {sensor.EyeAnchorDesc}", labelStyle);

            // --- Visualization ---
            GUILayout.Space(6);
            GUILayout.Label("<b>Visualize</b>", labelStyle);
            sensor.showCone = GUILayout.Toggle(sensor.showCone, " FOV cone (sweeps with head)", toggleStyle);
            sensor.showTargetRay = GUILayout.Toggle(sensor.showTargetRay, " eye→target rays", toggleStyle);
            sensor.showDetectMarks = GUILayout.Toggle(sensor.showDetectMarks, " detect marks (pale→amber→red on visible targets)", toggleStyle);

            // --- Live status ---
            GUILayout.Space(6);
            GUILayout.Label("<b>Status</b>", labelStyle);
            GUILayout.Label($"  detection: <b>{sensor.MaxDetection * 100f:F0}%</b>" +
                (sensor.AnyDetected ? "  <color=#ff5544>DETECTED</color>"
                 : sensor.AnyVisible ? "  <color=#ffbb44>in cone…</color>" : ""), labelStyle);
            GUILayout.Label("  cone: pale = watching · amber = detecting · red = detected", labelStyle);
        }

        /// <summary>
        /// TAN SUIT tab — full control of Civilian_02's detection + logic. His
        /// CoverSensor/VisionSensor/CombatController are his OWN components — the
        /// COVER/VISION tabs only resolve the spawned character, so his flags were
        /// previously unreachable. Every emitter defaults off; enable per-feature
        /// or kill whole components with the enabled toggles (perf isolation).
        /// </summary>
        void DrawTanSuitTab()
        {
            var ch = TanSuitRef;
            if (ch == null)
            {
                GUILayout.Label("<color=#ffaa44>Civilian_02 (Tan Suit) not found — spawn him or run SETUP PIT.</color>", labelStyle);
                return;
            }

            var cov = ch.GetComponent<CoverSensor>();
            var vis = ch.GetComponent<VisionSensor>();
            var cc  = ch.GetComponent<CombatController>();
            var solver = ch.GetComponent<ArmAimSolver>();

            // --- Status ---
            GUILayout.Label("<b>Status</b>", labelStyle);
            Vector3 tp = ch.transform.position;
            GUILayout.Label($"  pos: <b>{tp.x:F1}, {tp.y:F1}, {tp.z:F1}</b>", labelStyle);
            GUILayout.Label(cc != null
                ? $"  combat: <b>{cc.State}</b>" + (cc.State != CombatController.CombatState.Idle ? $" · threat {cc.ThreatPos.ToString("F1")}" : cc.Watching ? " · <b>watching</b>" : "")
                : "  combat: <i>no CombatController</i>", labelStyle);

            void Tog(ref bool f, string label)
            {
                f = GUILayout.Toggle(f, label, toggleStyle);
            }

            // --- Component kills (perf isolation: enabled=false stops Update entirely) ---
            GUILayout.Space(6);
            if (SecFold("t.comp", "Components — enabled=false kills Update + all emits"))
            {
            if (cov != null) cov.enabled = GUILayout.Toggle(cov.enabled, " CoverSensor", toggleStyle);
            if (vis != null) vis.enabled = GUILayout.Toggle(vis.enabled, " VisionSensor", toggleStyle);
            if (cc  != null) cc.enabled  = GUILayout.Toggle(cc.enabled,  " CombatController", toggleStyle);
            if (solver != null) solver.enabled = GUILayout.Toggle(solver.enabled, " ArmAimSolver", toggleStyle);
            GUILayout.BeginHorizontal();
            if (cov == null && GUILayout.Button("ATTACH CoverSensor", tabStyle, GUILayout.Height(20)))
                cov = ch.gameObject.AddComponent<CoverSensor>();
            if (vis == null && GUILayout.Button("ATTACH VisionSensor", tabStyle, GUILayout.Height(20)))
                vis = ch.gameObject.AddComponent<VisionSensor>();
            if (cc == null && GUILayout.Button("ATTACH Combat", tabStyle, GUILayout.Height(20)))
                cc = ch.gameObject.AddComponent<CombatController>();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("ALL VISUALS OFF (him)", tabStyle, GUILayout.Height(20)))
            {
                if (cov != null)
                {
                    cov.showRing = false; cov.showFootprints = false; cov.showWalls = false;
                    cov.showCandidates = false; cov.showLos = false; cov.showHeat = false;
                }
                if (vis != null) { vis.showCone = false; vis.showTargetRay = false; vis.showConeFill = false; }
                if (solver != null) solver.showSightBeam = false;
            }
            }

            // --- Detection (CoverSensor) ---
            if (cov != null && SecFold("t.cover", "detection — CoverSensor"))
            {
                bool dirty = false;
                float v = HSlider("cover radius", cov.radius, 4f, 40f);
                if (v != cov.radius) { cov.radius = v; dirty = true; }
                v = HSlider("standoff", cov.standoff, 0.15f, 1.5f);
                if (v != cov.standoff) { cov.standoff = v; dirty = true; }
                var covVc = cov.GetComponent<VoxelCharacter>();
                if (covVc != null)
                {
                    bool auto = covVc.footprintOverride <= 0f;
                    v = HSlider(auto ? "body footprint m (auto)" : "body footprint m (set)",
                                covVc.BodyFootprint, 0f, 1.5f, "F2");
                    if (covVc.BodyFootprint <= 1.5f
                        && Mathf.Abs(v - covVc.BodyFootprint) > 0.0005f)
                    { covVc.footprintOverride = v; dirty = true; }
                    v = HSlider("step up m", covVc.stepUpMax, 0.05f, 1.0f, "F2");
                    if (v != covVc.stepUpMax) { covVc.stepUpMax = v; dirty = true; }
                }
                if (cov.Field != null && cov.Field.bodyHalfMeters > 0f)
                    GUILayout.Label($"  walk cert: <b>{cov.Field.bodyHalfMeters * 2f:F2}m</b> square (continuous)" +
                                    $" · {cov.Field.bodyHalfMeters:F2}m stand-off", labelStyle);
                if (dirty) cov.Refresh(true);

                Tog(ref cov.showRing, " scan radius ring");
                Tog(ref cov.showFootprints, " obstacle footprints");
                Tog(ref cov.showWalls, " cover walls (grn full / yel crouch / red low)");
                Tog(ref cov.showCandidates, " candidates");
                Tog(ref cov.showLos, " threat→eye LOS rays");
                Tog(ref cov.showHeat, " ground-quad field");
                fighterBeams = GUILayout.Toggle(fighterBeams, " fighter beams (body squares / dst posts)", toggleStyle);
                if (GUILayout.Button("RESCAN NOW", tabStyle, GUILayout.Height(20)))
                    cov.Refresh(true);
                var st = cov.stats;
                GUILayout.Label($"  {st.obstacles} vols · {st.wallCols} wall cols (full {st.wallsFull} / crouch {st.wallsCrouch} / low {st.wallsLo})", labelStyle);
            }

            // --- Vision (VisionSensor) ---
            if (vis != null && SecFold("t.vision", "detection — VisionSensor"))
            {
                vis.range = HSlider("range", vis.range, 5f, 60f);
                vis.halfAngleDeg = HSlider("half angle °", vis.halfAngleDeg, 15f, 90f);
                vis.riseTime = HSlider("detect rise (s)", vis.riseTime, 0.1f, 3f);
                vis.decayTime = HSlider("forget decay (s)", vis.decayTime, 0.5f, 5f);

                Tog(ref vis.showCone, " FOV cone rim beams");
                Tog(ref vis.showTargetRay, " eye→target rays");
                Tog(ref vis.showDetectMarks, " detect marks");
                Tog(ref vis.showConeFill, " wedge fill (scene-space, legacy)");
                bool owns = VisionSensor.ConeOwner == vis;
                bool wantOwns = GUILayout.Toggle(owns, " owns V-mode cone globals", toggleStyle);
                if (wantOwns != owns) VisionSensor.ConeOwner = wantOwns ? vis : null;
                GUILayout.Label($"  detection: <b>{vis.MaxDetection * 100f:F0}%</b>" +
                    (vis.AnyDetected ? "  <color=#ff5544>DETECTED</color>"
                     : vis.AnyVisible ? "  <color=#ffbb44>in cone…</color>" : ""), labelStyle);
            }

            // --- Logic (CombatController) ---
            GUILayout.Space(6);
            if (SecFold("t.combat", "logic — CombatController + duel mode"))
            {
            if (cc == null)
            {
                GUILayout.Label("  not attached — attach above to run combat logic.", labelStyle);
            }
            else
            {
                GUILayout.Label($"  state: <b>{cc.State}</b>" +
                    (cc.State != CombatController.CombatState.Idle ? $" · threat {cc.ThreatPos.ToString("F1")}" : cc.Watching ? " · <b>watching</b>" : ""), labelStyle);
                cc.moveSpeed = HSlider("move speed", cc.moveSpeed, 0.5f, 5f);
                var vinny = cityMap.SpawnedCharacter;
                GUILayout.BeginHorizontal();
                GUI.enabled = cc.State == CombatController.CombatState.Idle;
                if (GUILayout.Button("ENTER COMBAT → Vinny", tabStyle, GUILayout.Height(22)) && vinny != null)
                    cc.EnterCombat(CharFeetCenter(vinny));
                if (GUILayout.Button("ENTER → picked aim", tabStyle, GUILayout.Height(22)) && pickedAim.HasValue)
                    cc.EnterCombat(pickedAim.Value);
                GUI.enabled = cc.State != CombatController.CombatState.Idle;
                if (GUILayout.Button("EXIT COMBAT", tabStyle, GUILayout.Height(22)))
                    cc.ExitCombat();
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            // --- Duel mode (same flag the Combat tab drives) ---
            GUILayout.BeginHorizontal();
            GUILayout.Label("duel mode:", labelStyle, GUILayout.Width(62));
            if (GUILayout.Toggle(pitTanMode == PitTanMode.Dummy, " dummy", pitTanMode == PitTanMode.Dummy ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
            {
                pitTanMode = PitTanMode.Dummy;
                if (cc != null && cc.State != CombatController.CombatState.Idle) cc.ExitCombat();
            }
            if (GUILayout.Toggle(pitTanMode == PitTanMode.Hide, " hide", pitTanMode == PitTanMode.Hide ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
                pitTanMode = PitTanMode.Hide;
            if (GUILayout.Toggle(pitTanMode == PitTanMode.Fight, " fight", pitTanMode == PitTanMode.Fight ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
                pitTanMode = PitTanMode.Fight;
            GUILayout.EndHorizontal();
            }
        }

        /// <summary>Demolitions: blast/inspect picks, the global voxel-damage &amp; debris
        /// system (char tint sliders, debris physics, rubble, reset — shot debris rides
        /// these too) and the blast bench. Shot-facing nibble toggles (glass glint /
        /// bullet decals) live on Combat's Firearm panel; the master gate is here.</summary>
        void DrawDemolitionsTab()
        {
            var sim = gameUI != null ? gameUI.Sim : null;
            string simState = sim != null ? sim.State.ToString() : "no sim";
            var dmgSys = VoxelDamageSystem.GetOrCreate();

            GUILayout.Label($"<b>DEMOLITIONS</b>   sim: {simState}", boldStyle);

            GUILayout.Space(4);
            if (SecFold("d.picks", "blast &amp; inspect picks"))
            {
            GUILayout.BeginHorizontal();
            bool blastArmed = pickArmed == PickSlot.Blast;
            if (GUILayout.Button(blastArmed ? "BLAST: click map…" : "PICK BLAST",
                    blastArmed ? tabActiveStyle : tabStyle, GUILayout.Height(22))) ArmPick(PickSlot.Blast);
            bool bldgArmed = pickArmed == PickSlot.Building;
            if (GUILayout.Button(bldgArmed ? "BLDG: click map…" : "PICK BLDG",
                    bldgArmed ? tabActiveStyle : tabStyle, GUILayout.Height(22))) ArmPick(PickSlot.Building);
            bool inspArmed = pickArmed == PickSlot.Inspect;
            if (GUILayout.Button(inspArmed ? "VOXEL: click map…" : "PICK VOXEL",
                    inspArmed ? tabActiveStyle : tabStyle, GUILayout.Height(22))) ArmPick(PickSlot.Inspect);
            GUILayout.EndHorizontal();
            DrawInspectHud();
            DrawPickStatus();
            }

            // --- Damage & debris — GLOBAL: shots AND blasts share these rules ---
            GUILayout.Space(4);
            if (SecFold("d.damage", "damage &amp; debris"))
            {
            dmgSys.damageTint = GUILayout.Toggle(dmgSys.damageTint, " damage tint (char gradient — master)", toggleStyle);
            if (dmgSys.damageTint)
            {
                dmgSys.charReach = Mathf.RoundToInt(HSlider("char reach cells", dmgSys.charReach, 0f, 8f, "F0"));
                dmgSys.charStrength = HSlider("char strength", dmgSys.charStrength, 0f, 1f, "F2");
                dmgSys.charRadial = HSlider("S-curve radial", dmgSys.charRadial, 0f, 1f, "F2");
                dmgSys.charAbsorb = HSlider("char absorb/cell", dmgSys.charAbsorb, 0.2f, 0.95f, "F2");
                dmgSys.debrisChar = HSlider("debris char", dmgSys.debrisChar, 0f, 1f, "F2");
            }
            dmgSys.shardVoxelCollision = GUILayout.Toggle(dmgSys.shardVoxelCollision, " debris collides with voxel world (walls / roofs)", toggleStyle);
            dmgSys.pairCells = GUILayout.Toggle(dmgSys.pairCells, " same-material pairing (brick dominoes)", toggleStyle);
            if (dmgSys.pairCells)
                dmgSys.pairChance = HSlider("pair chance", dmgSys.pairChance, 0f, 1f, "F2");
            dmgSys.rubbleDeposit = GUILayout.Toggle(dmgSys.rubbleDeposit, $" debris settles into volume (rubble · {dmgSys.RubbleCells} cells)", toggleStyle);
            if (dmgSys.DamagedVolumeCount > 0 &&
                GUILayout.Button($"RESET DAMAGE ({dmgSys.DamagedVolumeCount} volume{(dmgSys.DamagedVolumeCount > 1 ? "s" : "")}) — restore voxels", GUILayout.Height(22)))
                dmgSys.ResetAllDamage();
            }

            // --- Blast bench — only with a blast picked ---
            if (!pickedBlast.HasValue)
            {
                GUILayout.Space(4);
                GUILayout.Label("PICK BLAST a building face to arm the bench.", labelStyle);
                return;
            }
            GUILayout.Space(4);
            if (SecFold("d.bench", "demolition bench"))
            {
            GUILayout.Label($"<b>Demolition</b>  on {(blastVolume ?? "<color=#ffaa44>terrain — needs a building</color>")}{(blastAtVolumeCenter ? " · <color=#ffaa44>center</color>" : "")}", labelStyle);
            Vector3 blastCenter = blastAtVolumeCenter
                ? pickedBlast.Value
                : pickedBlast.Value - blastNormal.normalized * blastDepth;
            // live extent preview — 3 axis beams spanning the blast radius
            var pdrB = PathDebugRenderer.Instance;
            var ext = new Color(1f, 0.55f, 0.1f, 0.7f);
            pdrB?.SetCustomBeam("blast_rx", blastCenter - Vector3.right * blastRadius, blastCenter + Vector3.right * blastRadius, 0.05f, ext);
            pdrB?.SetCustomBeam("blast_ry", blastCenter - Vector3.up * blastRadius, blastCenter + Vector3.up * blastRadius, 0.05f, ext);
            pdrB?.SetCustomBeam("blast_rz", blastCenter - Vector3.forward * blastRadius, blastCenter + Vector3.forward * blastRadius, 0.05f, ext);
            GUI.enabled = !blastAtVolumeCenter;
            blastDepth = HSlider("depth into wall m", blastDepth, 0f, 4f, "F1");
            GUI.enabled = true;
            blastRadius = HSlider("radius m", blastRadius, 0.3f, 30f, "F1");
            blastPower = HSlider("power", blastPower, 0.1f, 8f, "F2");
            dmgSys.occlusionAware = GUILayout.Toggle(dmgSys.occlusionAware, " occlusion (walls shield)", toggleStyle);
            dmgSys.dropUnsupported = GUILayout.Toggle(dmgSys.dropUnsupported, " drop unsupported (collapse)", toggleStyle);
            if (dmgSys.dropUnsupported)
                dmgSys.cellImpulse = HSlider("cell impulse", dmgSys.cellImpulse, 0f, 20f, "F1");
            dmgSys.shockFront = GUILayout.Toggle(dmgSys.shockFront, " shock front (sim → playback)", toggleStyle);
            if (dmgSys.shockFront)
            {
                dmgSys.frontPasses = Mathf.RoundToInt(HSlider("front passes", dmgSys.frontPasses, 1f, 24f, "F0"));
                dmgSys.frontSec = HSlider("front sec", dmgSys.frontSec, 0.1f, 1.5f, "F2");
            }
            dmgSys.flyerRadialVelocity = GUILayout.Toggle(dmgSys.flyerRadialVelocity, " debris radial launch velocity", toggleStyle);
            bool canDet = !string.IsNullOrEmpty(blastVolume);
            GUI.enabled = canDet;
            if (GUILayout.Button($"💥 DETONATE ({blastRadius:F1}m)", canDet ? tabActiveStyle : tabStyle, GUILayout.Height(26)))
            {
                if (dmgSys.shockFront)
                    dmgSys.DetonateEvent(blastVolume, blastCenter, blastRadius, blastPower, dmgSys.frontPasses, dmgSys.frontSec);
                else
                    dmgSys.Detonate(blastVolume, blastCenter, blastRadius, blastPower);
            }
            GUI.enabled = true;
            }
        }

        /// <summary>Weapons placeholder — live weapon-stat tuning (damage, spread,
        /// muzzle velocity, recoil, ammo) lands here via the live-editor pipeline.</summary>
        void DrawWeaponsTab()
        {
            GUILayout.Label("<b>WEAPONS</b>", boldStyle);
            GUILayout.Space(4);
            GUILayout.Label("Placeholder — live weapon-stat tuning lands here.", labelStyle);
            GUILayout.Label("Planned knobs: damage · spread · muzzle velocity · recoil · ammo —", labelStyle);
            GUILayout.Label("wired through the live-editor pipeline (docs/core/LIVE_EDITOR_PIPELINE.md).", labelStyle);
            GUILayout.Space(4);
            GUILayout.Label("Current combat tuning lives on the Combat tab:", labelStyle);
            GUILayout.Label("skill / cone ° / muzzle speed / spread cone / aim feel.", labelStyle);
        }

        /// <summary>
        /// Firearm panel: shooter skill (1-10, default 10), cone slider, muzzle speed, FIRE and the last-shot
        /// report. FIRE needs the IK aim fully raised (blend ≥ 0.98) so the shot goes where the gun actually
        /// points; the report shows how far the shot line passed from firing_target and the solver's residual
        /// aim error at the trigger pull. Physics + hit resolution live in ProjectileSystem.
        /// </summary>
        private void DrawFireControls(bool armedCtx, WeaponMount mount, Vector3 targetWorld, bool hasTarget)
        {
            var proj = ProjectileSystem.GetOrCreate();

            // The cone wireframe follows this mount while the panel is up (stale-stamped, auto-clears).
            proj.aimSource = mount;
            proj.aimTarget = hasTarget ? targetWorld : (Vector3?)null;
            proj.aimSourceStamp = Time.time;

            GUILayout.Space(6);
            GUILayout.Label("<b>Firearm</b>", labelStyle);
            proj.shooterSkill = Mathf.RoundToInt(HSlider("skill (1-10)", proj.shooterSkill, 1f, 10f, "F0"));
            proj.baseConeDeg = HSlider("cone ° @10", proj.baseConeDeg, 0f, 5f);
            proj.muzzleSpeed = HSlider("speed u/s", proj.muzzleSpeed, 5f, 150f, "F0");
            GUILayout.Label($"effective cone ±{proj.EffectiveConeDeg:F2}°   (skill ×{ProjectileSystem.SkillSpreadMultiplier(proj.shooterSkill):F2})", labelStyle);
            proj.showSpreadCone = GUILayout.Toggle(proj.showSpreadCone, " spread cone (wireframe)", toggleStyle);

            bool tr = GUILayout.Toggle(proj.showTracers, " tracers (in-flight streak)", toggleStyle);
            if (tr != proj.showTracers)
            {
                proj.showTracers = tr;
                if (!tr) proj.ClearTracers();
            }
            bool im = GUILayout.Toggle(proj.showImpactMarks, " impact marks (red ticks)", toggleStyle);
            if (im != proj.showImpactMarks)
            {
                proj.showImpactMarks = im;
                if (!im) proj.ClearImpactMarks();
            }
            var dmg = VoxelDamageSystem.GetOrCreate();
            dmg.glassDamageEnabled = GUILayout.Toggle(dmg.glassDamageEnabled, " glass puncture (P0)", toggleStyle);
            if (dmg.glassDamageEnabled)
            {
                dmg.ring1RemoveFraction = HSlider("ring removal", dmg.ring1RemoveFraction, 0f, 1f, "F2");
                GUILayout.Label("  low = small hole + wide crack halo · high = chunk blown out", labelStyle);
                dmg.shardBurstEnabled = GUILayout.Toggle(dmg.shardBurstEnabled, " shard burst (flying cubes)", toggleStyle);
            }

            // Shot-facing damage-nibble features — the master gate (`damage tint`)
            // lives on Demolitions; grayed here while it's off so the dependency shows.
            GUI.enabled = dmg.damageTint;
            dmg.glassGlint = GUILayout.Toggle(dmg.glassGlint, " glass glint (retire cracked material)", toggleStyle);
            if (dmg.glassGlint)
                dmg.glintStrength = HSlider("glint strength", dmg.glintStrength, 0f, 1f, "F2");
            dmg.bulletImpactTint = GUILayout.Toggle(dmg.bulletImpactTint, " bullet impact decals (non-glass)", toggleStyle);
            if (dmg.bulletImpactTint)
                dmg.bulletScorchDepth = Mathf.RoundToInt(HSlider("scorch depth", dmg.bulletScorchDepth, 0f, 4f, "F0"));
            GUI.enabled = true;
            if (!dmg.damageTint)
                GUILayout.Label("<i>  tint inert — master 'damage tint' on Demolitions</i>", labelStyle);

            if (solver != null)
            {
                solver.feelSkill = proj.shooterSkill;   // placeholder for the hood's Firearms stat
                solver.aimFeelEnabled = GUILayout.Toggle(solver.aimFeelEnabled, " aim feel (sway + recoil)", toggleStyle);
                if (solver.aimFeelEnabled)
                {
                    solver.swayDeg = HSlider("sway ° @10", solver.swayDeg, 0f, 3f);
                    solver.kickDeg = HSlider("kick °", solver.kickDeg, 0f, 5f);
                    solver.settleSec = HSlider("settle s @10", solver.settleSec, 0.05f, 0.5f);
                    GUILayout.Label($"aim offset now {solver.AimOffsetDeg:F2}°", labelStyle);
                }
            }

            bool aimed = solver != null && solver.IsAiming && solver.Blend >= 0.98f;
            // Same wheel, same rules — the manual trigger spends a chamber too.
            var fcc = mount != null ? mount.GetComponent<CombatController>() : null;
            bool canFire = armedCtx && mount != null && mount.IsEquipped && aimed
                        && fcc != null && fcc.Cylinder > 0 && !fcc.Reloading;
            GUI.enabled = canFire;
            if (GUILayout.Button("FIRE", canFire ? tabActiveStyle : tabStyle, GUILayout.Height(30)))
            {
                if (proj.Fire(mount, solver.LastAngularErrorDeg, hasTarget ? targetWorld : (Vector3?)null))
                    fcc.SpendRound();
            }
            GUI.enabled = true;
            if (!canFire)
                GUILayout.Label(armedCtx && fcc != null && (fcc.Cylinder <= 0 || fcc.Reloading)
                    ? "(cylinder — reload: R in possess, auto in combat)"
                    : "(engage IK AIM and wait for blend 1.00)", labelStyle);
            if (proj.InFlight > 0)
                GUILayout.Label($"in flight: {proj.InFlight}", labelStyle);

            if (proj.HasShot)
            {
                var s = proj.LastShot;
                GUILayout.Space(4);
                GUILayout.Label($"<b>SHOT #{s.id}</b>  seed {s.seed:X8}  skill {s.skill}  cone ±{s.coneHalfDeg:F2}° (drew {s.spreadDeg:F2}°)", labelStyle);
                if (s.hit)
                {
                    string glass = ProjectileSystem.IsGlassMaterial(s.material) ? " (glass)" : "";
                    GUILayout.Label($"<color=#66ff66>HIT</color> {s.target}   mat {s.material}{glass}   cell {s.cell.x},{s.cell.y},{s.cell.z}", labelStyle);
                    GUILayout.Label($"dist {s.distance:F2}m   flight {s.flightTime:F2}s", labelStyle);
                    var dmgSys = VoxelDamageSystem.Instance;
                    if (dmgSys != null && ProjectileSystem.IsGlassMaterial(s.material) && dmgSys.LastRemoved + dmgSys.LastCracked > 0)
                        GUILayout.Label($"🔨 puncture: -{dmgSys.LastRemoved} cells, {dmgSys.LastCracked} cracked", labelStyle);
                }
                else
                {
                    GUILayout.Label($"<color=#ffaa44>MISS</color> — expired after {s.distance:F1}m", labelStyle);
                }
                if (s.hasIntended)
                    GUILayout.Label($"lateral miss {s.lateralMiss:F3}m   impact→target {s.impactOffset:F3}m", labelStyle);
                GUILayout.Label($"aim err at trigger {s.aimErrorDeg:F1}°", labelStyle);
            }
        }

        /// <summary>
        /// SFX home — every sound's in-game level is a TRIM on the studio file
        /// (1.00 = exactly what audio_studio authored). A/B calibration buttons
        /// compare the 2D studio reference (at the listener) against the real
        /// 3D in-game path. Tuned trims are serialized — they ARE the shipped mix.
        /// </summary>
        void DrawSfxTab()
        {
            var proj = ProjectileSystem.GetOrCreate();
            var dmg = VoxelDamageSystem.GetOrCreate();
            var ch = cityMap != null ? cityMap.SpawnedCharacter : null;
            var mnt = ch != null ? ch.GetComponent<WeaponMount>() : null;
            // Resolve once when the cache is empty so the button works the first
            // second after tab-open; the 1s refresh above keeps it current after.
            var listener = sfxListener != null ? sfxListener : ResolveSfxListener();

            GUILayout.Label("<b>SFX</b>   studio file = reference · trim tunes the game level", boldStyle);
            GUILayout.Label("<i>1.00 = exactly what audio_studio plays. Serialized — tuned values ARE the shipped mix.</i>", labelStyle);

            GUILayout.Space(4);
            if (SecFold("x.shot", "GUNSHOT — sounds/gunshot.stsound"))
            {
            proj.shotAudioEnabled = GUILayout.Toggle(proj.shotAudioEnabled, " gunshot sfx", toggleStyle);
            proj.shotAudioVolume = HSlider("level ×", proj.shotAudioVolume, 0.1f, 2f, "F2");
            proj.shotRefDistance = HSlider("full-vol radius m", proj.shotRefDistance, 0.5f, 20f, "F1");
            proj.shotAudibleRange = HSlider("report range m", proj.shotAudibleRange, 5f, 200f, "F0");
            GUILayout.BeginHorizontal();
            GUI.enabled = listener != null;
            if (GUILayout.Button("▶ reference", tabStyle, GUILayout.Height(22)))
            {
                var clip = ProceduralAudio.GetClip("sounds/gunshot.stsound");
                if (clip != null) AudioSource.PlayClipAtPoint(clip, listener.transform.position, 1f);
            }
            GUI.enabled = mnt != null && mnt.IsEquipped;
            if (GUILayout.Button("▶ muzzle", tabStyle, GUILayout.Height(22)))
            {
                var clip = ProceduralAudio.GetClip("sounds/gunshot.stsound");
                if (clip != null)
                {
                    ProceduralAudio.PlayAt(clip, mnt.MuzzleWorld, proj.shotAudioVolume,
                        proj.shotRefDistance, proj.shotAudibleRange);
                    SfxWaves.GetOrCreate().Emit(mnt.MuzzleWorld, proj.shotAudibleRange,
                        proj.shotRefDistance, "gunshot");
                }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("<i>reference = at the listener (studio loudness) · muzzle = real 3D path + trim, emits the wave</i>", labelStyle);
            if (mnt == null || !mnt.IsEquipped)
                GUILayout.Label("<i>  muzzle test needs Vinny armed — possess (G) or equip via Combat</i>", labelStyle);
            }

            GUILayout.Space(6);
            if (SecFold("x.reload", "RELOAD — sounds/reload_click.stsound (LiveTesting wins)"))
            {
            var rel = ReloadAudio.GetOrCreate();
            rel.reloadAudioEnabled = GUILayout.Toggle(rel.reloadAudioEnabled, " reload sfx", toggleStyle);
            if (rel.reloadAudioEnabled)
            {
                rel.reloadAudioVolume = HSlider("level × (src vol, caps at 1)", rel.reloadAudioVolume, 0.1f, 1f, "F2");
                rel.reloadRefDistance = HSlider("full-vol radius m", rel.reloadRefDistance, 0.2f, 20f, "F1");
                rel.reloadAudibleRange = HSlider("report range m", rel.reloadAudibleRange, 1f, 50f, "F0");
                GUILayout.BeginHorizontal();
                GUI.enabled = listener != null;
                if (GUILayout.Button("▶ preview", tabStyle, GUILayout.Height(22)))
                {
                    var clip = ProceduralAudio.GetClip("sounds/reload_click.stsound");
                    if (clip != null) AudioSource.PlayClipAtPoint(clip, listener.transform.position, 1f);
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Label($"<i>one click per seated round — the staggered wheel IS the rhythm · wave is display-only</i>", labelStyle);
                GUILayout.Label($"<i>last: {rel.LastInfo}</i>", labelStyle);
            }
            }

            GUILayout.Space(6);
            if (SecFold("x.ric", "SURFACE IMPACT — sounds/Impacts/ (LiveTesting wins)"))
            {
            var sfx = SurfaceImpactAudio.GetOrCreate();
            sfx.impactAudioEnabled = GUILayout.Toggle(sfx.impactAudioEnabled, " surface impact sfx", toggleStyle);
            if (sfx.impactAudioEnabled)
            {
                sfx.ricMinIncidenceDeg = HSlider("ricochet min incidence ° vs normal", sfx.ricMinIncidenceDeg, 20f, 89f, "F0");
                sfx.ricMaxDistanceM = HSlider("ricochet max muzzle dist m", sfx.ricMaxDistanceM, 1f, 300f, "F0");
                sfx.impactAudioVolume = HSlider("level × (src vol, caps at 1)", sfx.impactAudioVolume, 0.1f, 1f, "F2");
                sfx.impactGain = HSlider("gain × (samples — real loudness)", sfx.impactGain, 0.25f, 8f, "F2");
                sfx.impactRefDistance = HSlider("full-vol radius m", sfx.impactRefDistance, 0.5f, 20f, "F1");
                sfx.impactAudibleRange = HSlider("report range m", sfx.impactAudibleRange, 5f, 200f, "F0");
                GUILayout.Label($"<i>grazing+close → ricochet pool · otherwise → impact pool · glass owns its sfx</i>", labelStyle);
                GUILayout.Label($"<i>last: {sfx.LastInfo}</i>", labelStyle);
            }
            // Preview what the game resolves: LiveTesting/<name>.stsound shadows its
            // main-folder twin (GetClip resolves it); LIVE rows are test files.
            sfx.modEnabled = GUILayout.Toggle(sfx.modEnabled, " modulation — variant per hit", toggleStyle);
            if (sfx.modEnabled)
            {
                sfx.modJitter = HSlider("jitter (.12 subtle .30 med .55 wild)", sfx.modJitter, 0f, 0.6f, "F2");
                sfx.modLockMaster = GUILayout.Toggle(sfx.modLockMaster, " lock master knobs (structure only)", toggleStyle);
            }
            RicPoolGroup("Impacts", "Impacts (ricochet_* rows = ricochet pool)", listener);
            }

            GUILayout.Space(6);
            if (SecFold("x.glass", "GLASS — synthesized tink/crack/crash"))
            {
            dmg.glassAudioEnabled = GUILayout.Toggle(dmg.glassAudioEnabled, " glass sfx", toggleStyle);
            if (dmg.glassAudioEnabled)
            {
                dmg.glassAudioVolume = HSlider("level ×", dmg.glassAudioVolume, 0.1f, 2f, "F2");
                dmg.glassRefDistance = HSlider("full-vol radius m", dmg.glassRefDistance, 0.5f, 20f, "F1");
                dmg.glassAudibleRange = HSlider("report range m", dmg.glassAudibleRange, 5f, 200f, "F0");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("tink", tabStyle, GUILayout.Height(20))) dmg.PreviewGlassSound(0);
                if (GUILayout.Button("crack", tabStyle, GUILayout.Height(20))) dmg.PreviewGlassSound(1);
                if (GUILayout.Button("crash", tabStyle, GUILayout.Height(20))) dmg.PreviewGlassSound(2);
                GUILayout.EndHorizontal();
                GUILayout.Label("<i>shatters scale with cells removed (0.4+n/20) — trim is the base level</i>", labelStyle);
            }
            }

            GUILayout.Space(6);
            if (SecFold("x.waves", "REPORT WAVES — the audible edge visualized"))
            {
            var waves = SfxWaves.GetOrCreate();
            waves.showWaves = GUILayout.Toggle(waves.showWaves, " show report waves (TEAL loud → VIOLET fades)", toggleStyle);
            if (waves.showWaves)
            {
                waves.waveSpeed = HSlider("wave speed m/s", waves.waveSpeed, 5f, 343f, "F0");
                GUILayout.Label("<i>rim color = live audibility · inner ring = full-vol radius · edge = hearing radius (real 343 m/s slowed for viewing)</i>", labelStyle);
            }
            }

            GUILayout.Space(6);
            GUILayout.Label($"hot-reload <b>{(ProceduralAudio.liveReload ? "on" : "off")}</b> — save in audio_studio → next trigger pulls the new recipe", labelStyle);
        }

        private readonly Dictionary<string, float> poolScanAt = new();
        private readonly Dictionary<string, List<string>> poolLive = new(), poolMain = new();

        /// <summary>Scan a pool dir (main + LiveTesting) at ~2 Hz — the SFX panel draws every GUI frame.</summary>
        void RicPoolGroup(string pool, string title, Component listener)
        {
            poolScanAt.TryGetValue(pool, out float at);
            if (Time.unscaledTime >= at)
            {
                poolScanAt[pool] = Time.unscaledTime + 0.5f;
                string root = Path.Combine(Application.streamingAssetsPath, "sounds", pool);
                string live = Path.Combine(root, "LiveTesting");
                var liveList = poolLive[pool] = new List<string>();
                var mainList = poolMain[pool] = new List<string>();
                if (Directory.Exists(live))
                    liveList.AddRange(Directory.GetFiles(live, "*.stsound", SearchOption.TopDirectoryOnly)
                                               .Select(Path.GetFileName));
                if (Directory.Exists(root))
                    mainList.AddRange(Directory.GetFiles(root, "*.stsound", SearchOption.TopDirectoryOnly)
                                               .Select(Path.GetFileName));
                liveList.Sort(); mainList.Sort();
            }
            var lv = poolLive[pool]; var mn = poolMain[pool];
            if (lv.Count == 0 && mn.Count == 0)
            {
                GUILayout.Label($"<i>{title}: no .stsound in sounds/{pool}/ or LiveTesting/</i>", labelStyle);
                return;
            }
            if (lv.Count > 0)
            {
                GUILayout.Label($"<i>{title}/LiveTesting ({lv.Count}) — pool uses ONLY these:</i>", labelStyle);
                foreach (var fn in lv) PoolBtn(pool, fn, "LIVE", listener != null, listener);
            }
            if (lv.Count > 0 && mn.Count > 0)
                GUILayout.Label($"<i>{title} main ({mn.Count}) — shadowed files marked:</i>", labelStyle);
            foreach (var fn in mn)
                PoolBtn(pool, fn, lv.Contains(fn) ? "live" : "shipped", listener != null, listener);
        }

        private readonly HashSet<string> varExpanded = new();

        /// <summary>One preview row: plays the resolved clip at the listener (studio loudness).
        /// The ▸ expander lists the file's bounded children (v01..vNN) for audition; an
        /// unchecked child is CUT from the runtime pool and persisted to a sidecar.</summary>
        void PoolBtn(string pool, string fileName, string tag, bool canPlay, Component listener)
        {
            string rel = $"sounds/{pool}/{fileName}";
            var sfx = SurfaceImpactAudio.Instance;
            GUILayout.BeginHorizontal();
            GUI.enabled = canPlay;
            if (GUILayout.Button($"▶ {fileName} <i>({tag})</i>", tabStyle, GUILayout.Height(20)))
            {
                var r0 = ProceduralAudio.LoadRecipe(rel);
                var fm = SurfaceImpactAudio.FileMix(rel);
                var clip = r0 != null ? ProceduralAudio.SynthFromRecipe(r0,
                    vel: (sfx != null ? sfx.impactGain : 1f) * fm.gain, bypassCache: true) : null;
                if (clip != null) AudioSource.PlayClipAtPoint(clip, listener.transform.position,
                    Mathf.Clamp01((sfx != null ? sfx.impactAudioVolume : 1f) * fm.volume));
            }
            GUI.enabled = true;
            if (sfx != null)
            {
                bool ex = varExpanded.Contains(fileName);
                if (GUILayout.Button(ex ? "▾" : "▸", tabStyle, GUILayout.Width(24), GUILayout.Height(20)))
                {
                    if (ex) varExpanded.Remove(fileName); else varExpanded.Add(fileName);
                }
            }
            GUILayout.EndHorizontal();

            if (sfx == null || !varExpanded.Contains(fileName)) return;

            // per-file tuning — this SFX's own gain/volume (sidecar persists per recipe)
            var fmix = SurfaceImpactAudio.FileMix(rel);
            float ng = HSlider("  gain × this file", fmix.gain, 0.25f, 8f, "F2");
            float nv = HSlider("  level × this file", fmix.volume, 0.05f, 1f, "F2");
            if (ng != fmix.gain || nv != fmix.volume)
                SurfaceImpactAudio.SetFileMix(rel, ng, nv);

            if (!sfx.modEnabled) return;
            var culled = SurfaceImpactAudio.CulledSlots(rel);
            int n = sfx.modVariants;
            for (int i = 0; i < n; i += 4)
            {
                GUILayout.BeginHorizontal();
                for (int k = i; k < Mathf.Min(i + 4, n); k++)
                {
                    int slot = k;
                    GUI.enabled = canPlay;
                    if (GUILayout.Button("▶", tabStyle, GUILayout.Width(26), GUILayout.Height(18)))
                    {
                        var baseR = ProceduralAudio.LoadRecipe(rel);
                        var child = ProceduralAudio.MutateRecipe(baseR,
                            SurfaceImpactAudio.VariantSeed(fileName, slot), sfx.modJitter, sfx.modLockMaster);
                        var c = child != null ? ProceduralAudio.SynthFromRecipe(child,
                            vel: sfx.impactGain * fmix.gain, bypassCache: true) : null;
                        if (c != null) AudioSource.PlayClipAtPoint(c, listener.transform.position,
                            Mathf.Clamp01(sfx.impactAudioVolume * fmix.volume));
                    }
                    GUI.enabled = true;
                    bool keep = GUILayout.Toggle(!culled.Contains(slot), $"v{slot + 1:D2}", toggleStyle, GUILayout.Width(46));
                    if (keep == culled.Contains(slot))
                        SurfaceImpactAudio.SetSlotCulled(rel, slot, !keep);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("<i>audition ▶ · unchecked = cut from the pool (sidecar saved)</i>", labelStyle);
        }

        /// <summary>
        /// Material-resistance tuning + last-blast readout, sub-tabbed per model type.
        /// Edits the live static tables in VoxelDamageMath — detonations and occlusion
        /// marches pick up changes immediately; each blast resolves its table from the
        /// target volume id (veh_*/item_* conventions). Slider labels carry live kill
        /// probability at the current power dial: @center = power·(1−res), @½r = same
        /// with the linear distance falloff halved.
        /// </summary>
        void DrawMatResTab()
        {
            var dmgSys = VoxelDamageSystem.GetOrCreate();
            GUILayout.Label("<b>MATERIAL RESISTANCE</b>   <i>live</i>", boldStyle);

            // sub-tabs — one live table per model type
            GUILayout.BeginHorizontal();
            for (int p = 0; p < (int)VoxelDamageMath.ResProfile.Count; p++)
            {
                bool act = matResSub == p;
                if (GUILayout.Button(VoxelDamageMath.ResProfileNames[p],
                        act ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
                    matResSub = p;
            }
            GUILayout.EndHorizontal();
            int prof = matResSub;
            string profName = VoxelDamageMath.ResProfileNames[prof];
            if (prof != (int)VoxelDamageMath.ResProfile.Building)
                GUILayout.Label($"<i>no damageable {profName} volumes yet — this table goes live for '{profName}_*'/'*_{profName}_*' volume ids when they register</i>", labelStyle);

            int n = (int)VoxelDamageMath.ResClass.Count;   // hoisted — ops fold reads it
            if (SecFold("m.table", $"{profName} res table"))
            {
            GUILayout.Label("kill roll p = power·(1−dist/radius)·(1−res)·transmit — walls spend the same table per cell the occlusion march crosses", labelStyle);
            VoxelDamageMath.GlobalResistScale =
                HSlider("global ×scale (all types)", VoxelDamageMath.GlobalResistScale, 0f, 2f, "F2");

            var tbl = VoxelDamageMath.ResTable[prof];
            var work = VoxelDamageMath.WorkshopRes[prof];
            GUILayout.Label($"{profName} res — kill% @center / @½r at power {blastPower:F1}:", labelStyle);
            GUILayout.Label("<color=#6fb7ff>■ authored</color>  <color=#ffa64d>■ workshop baseline</color>  <color=#5fe07a>■ tuned this session</color>", labelStyle);
            for (int c = 0; c < n; c++)
            {
                float eff = Mathf.Clamp01(tbl[c] * VoxelDamageMath.GlobalResistScale);
                float pC = Mathf.Clamp01(blastPower * (1f - eff));
                float pH = Mathf.Clamp01(blastPower * 0.5f * (1f - eff));
                // provenance color: factory → blue, on the workshop baseline → orange, tuned → green
                string col = Mathf.Approximately(tbl[c], VoxelDamageMath.BaseResistance[c]) ? "#6fb7ff"
                           : Mathf.Approximately(tbl[c], work[c]) ? "#ffa64d" : "#5fe07a";
                int sel = c;   // closure
                tbl[c] = ResRow($"<color={col}>{(matResSel == c ? "► " : "  ")}{VoxelDamageMath.ResClassNames[c]}</color>  {pC:P0}/{pH:P0}",
                    tbl[c], () => matResSel = sel);
            }
            }
            if (SecFold("m.ops", "reset / promote"))
            {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"reset {profName} → workshop", GUILayout.Height(20)))
                VoxelDamageMath.ResetResistanceTable(prof);
            if (GUILayout.Button("reset ALL + scale", GUILayout.Height(20)))
                VoxelDamageMath.ResetResistanceTable();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"promote live → {profName} workshop", GUILayout.Height(20)))
                VoxelDamageMath.PromoteToWorkshop(prof);
            if (GUILayout.Button("restore AUTHORED", GUILayout.Height(20)))
                VoxelDamageMath.RestoreAuthoredDefaults();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            string selName = matResSel >= 0 && matResSel < n ? VoxelDamageMath.ResClassNames[matResSel] : null;
            GUI.enabled = selName != null;
            if (GUILayout.Button(selName != null ? $"promote '{selName}' → AUTHORED" : "click a name to select", GUILayout.Height(20)))
                PromoteResToAuthored(prof, matResSel);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("workshop = pending baseline (in code, seeds each play); AUTHORED = factory defaults — promote writes the .cs literal + recompiles (exits play mode)", labelStyle);
            }

            GUILayout.Space(4);
            if (SecFold("m.blast", "last blast"))
            {
            int tot = 0;
            var sb = new System.Text.StringBuilder();
            for (int c = 0; c < n; c++)
                if (dmgSys.lastBlastRemoved[c] > 0)
                {
                    sb.Append($"{VoxelDamageMath.ResClassNames[c]}×{dmgSys.lastBlastRemoved[c]:N0}  ");
                    tot += dmgSys.lastBlastRemoved[c];
                }
            GUILayout.Label(tot > 0 || dmgSys.lastBlastDetached > 0
                ? $"on a <b>{VoxelDamageMath.ResProfileNames[Mathf.Clamp(dmgSys.lastBlastProfile, 0, (int)VoxelDamageMath.ResProfile.Count - 1)]}</b> volume — crater removed {tot:N0}:\n   {sb}\ndetached/collapse {dmgSys.lastBlastDetached:N0} · flyers emitted {dmgSys.LastFlyEmitted:N0}\ndominoes {dmgSys.lastBlastPairs:N0} · paired cells {dmgSys.lastBlastPairCells:N0}"
                : "no blast yet — pick a point and DETONATE", labelStyle);
            }
        }

        // ======================= LOCKED MODE =======================
        // Locked = every live tab renders as ONE cached label rebuilt ~4Hz —
        // the pattern the text tabs already prove at 120fps. Mirrors read state
        // ONLY (GetComponent / .Instance — never GetOrCreate, AddComponent, or
        // Refresh) so a locked tab cannot wake a system or spawn a singleton.

        private bool IsLiveTab(Tab t) =>
            t == Tab.Combat || t == Tab.Cover || t == Tab.Vision || t == Tab.TanSuit
            || t == Tab.Demolitions || t == Tab.Sfx || t == Tab.MatRes || t == Tab.Clothing
            || t == Tab.Forces;

        private void RebuildLockedTabText()
        {
            lastBuiltTab = activeTab;
            nextTabRefresh = Time.unscaledTime + 0.25f;
            hudSB.Length = 0;
            switch (activeTab)
            {
                case Tab.Sfx:        LockSfx(hudSB);        break;
                case Tab.Clothing:   LockClothing(hudSB);   break;
                case Tab.Combat:     LockCombat(hudSB);     break;
                case Tab.Cover:      LockCover(hudSB);      break;
                case Tab.Vision:     LockVision(hudSB);     break;
                case Tab.TanSuit:    LockTanSuit(hudSB);    break;
                case Tab.Demolitions: LockDemolitions(hudSB); break;
                case Tab.MatRes:     LockMatRes(hudSB);     break;
                case Tab.Forces:     LockForces(hudSB);     break;
            }
            lockedTabText = hudSB.ToString();
        }

        private static string OnOff(bool b) => b ? "<color=#66ff66>on</color>" : "<color=#888888>off</color>";

        private void LockSfx(System.Text.StringBuilder sb)
        {
            var proj = ProjectileSystem.Instance;
            var dmg = VoxelDamageSystem.Instance;
            var waves = SfxWaves.Instance;
            sb.AppendLine("<b>SFX</b>  <i>(locked — L to edit)</i>");
            sb.AppendLine($"listener: {(sfxListener != null ? "<color=#66ff66>found</color>" : "<color=#ffaa44>none</color>")}   hot-reload: <b>{(ProceduralAudio.liveReload ? "on" : "off")}</b>");
            sb.AppendLine(proj != null
                ? $"gunshot  level×{proj.shotAudioVolume:F2}  full-vol {proj.shotRefDistance:F1}m  range {proj.shotAudibleRange:F0}m"
                : "gunshot  <i>(ProjectileSystem not spawned)</i>");
            sb.AppendLine(dmg != null
                ? $"glass  {OnOff(dmg.glassAudioEnabled)}  level×{dmg.glassAudioVolume:F2}  full-vol {dmg.glassRefDistance:F1}m  range {dmg.glassAudibleRange:F0}m"
                : "glass  <i>(VoxelDamageSystem not spawned)</i>");
            sb.AppendLine(waves != null
                ? $"report waves  {OnOff(waves.showWaves)}  speed {waves.waveSpeed:F0} m/s"
                : "report waves  <i>(SfxWaves not spawned)</i>");
        }

        private void LockClothing(System.Text.StringBuilder sb)
        {
            sb.AppendLine("<b>CLOTHING</b>  <i>(locked — L to edit)</i>");
            sb.AppendLine($"rigs: {allCharacterRigs.Count}  selected: {(selectedRigIndex >= 0 && selectedRigIndex < allCharacterRigs.Count ? $"#{selectedRigIndex} {allCharacterRigs[selectedRigIndex].gameObject.name}" : "none")}");
            var rig = (selectedRigIndex >= 0 && selectedRigIndex < allCharacterRigs.Count) ? allCharacterRigs[selectedRigIndex] : null;
            var anim = rig != null ? rig.GetComponent<CharacterAnimation>() : null;
            if (anim != null)
                sb.AppendLine($"pose transitions {OnOff(anim.poseTransitions)}  dur {anim.transitionDuration:F2}s  dip {anim.transitionDip:F2}  ease {anim.transitionEase}  duck {anim.duck:F2}  gait {anim.GaitTime:F2}  speed×{anim.DuckSpeedFactor:F2}  scan {OnOff(anim.lookScan)}");
            sb.AppendLine($"clothing systems: {allClothingSystems.Count}  selected: {(clothingSystem != null ? clothingSystem.gameObject.name : "none")}");
        }

        private void LockCombat(System.Text.StringBuilder sb)
        {
            var ch = cityMap != null ? cityMap.SpawnedCharacter : null;
            var mount = ch != null ? ch.GetComponent<WeaponMount>() : null;
            var slv = ch != null ? ch.GetComponent<ArmAimSolver>() : null;
            var cc = ch != null ? ch.GetComponent<CombatController>() : null;
            var sim = gameUI != null ? gameUI.Sim : null;
            sb.AppendLine($"<b>COMBAT</b>   sim: {(sim != null ? sim.State.ToString() : "no sim")}   <i>(locked — L to edit)</i>");
            sb.AppendLine($"block: {(rangeBlockId ?? "<i>none (free-fire)</i>")}   lane {RangeLane}{(RangeAimMold ? " → glass mold" : "")}   duel: {pitTanMode}");
            sb.AppendLine($"picks — stand: {(pickedStand.HasValue ? pickedStand.Value.ToString("F1") : "—")}   aim: {(pickedAim.HasValue ? pickedAim.Value.ToString("F1") : "—")}   armed: {(pickArmed != PickSlot.None ? pickArmed.ToString() : "—")}");
            sb.AppendLine($"weapon: {(mount != null && mount.IsEquipped ? mount.ItemFileName : "<i>none</i>")}" +
                (mount != null && mount.IsEquipped ? $"   muzzleRay {OnOff(mount.showMuzzleRay)}  sightBeam {OnOff(slv != null && slv.showSightBeam)}" : ""));
            if (slv != null)
                sb.AppendLine($"IK aim: {(slv.IsAiming ? "<color=#66ff66>ENGAGED</color>" : "idle")}  blend {slv.Blend:F2}  aimErr {slv.LastAngularErrorDeg:F1}°");
            if (cc != null)
            {
                sb.AppendLine($"combat: <b>{cc.State}</b>  cover {cc.CoverObstacleId ?? "—"}");
                if (!string.IsNullOrEmpty(cc.LastResult)) sb.AppendLine($"  {cc.LastResult}");
            }
        }

        private void LockCover(System.Text.StringBuilder sb)
        {
            var ch = cityMap != null ? cityMap.SpawnedCharacter : null;
            var sensor = ch != null ? ch.GetComponent<CoverSensor>() : null;
            sb.AppendLine("<b>COVER</b>  <i>(locked — L to edit)</i>");
            if (sensor == null) { sb.AppendLine("<i>no CoverSensor on spawned character</i>"); return; }
            sb.AppendLine($"enabled {OnOff(sensor.enabled)}   threat {(sensor.HasThreat ? sensor.Threat.Value.ToString("F1") : "—")}");
            sb.AppendLine(sensor.HeightsMeasured
                ? $"silhouette: stand {sensor.HStand:F2}m · crouch {sensor.HShallow:F2}→{sensor.HDeep:F2}m (+{sensor.EyeFwd:F2} lean)   support {sensor.SupportY:F2}m"
                : $"silhouette: {sensor.HStand:F2}/{sensor.HShallow:F2}/{sensor.HDeep:F2}m — FALLBACK (no anim data)");
            sb.AppendLine($"radius {sensor.radius:F1}m  standoff {sensor.standoff:F2}  fieldRes {sensor.fieldRes}  proxW {sensor.proxWeight:F2}  facingDot {sensor.facingDot:F2}");
            sb.AppendLine($"viz — ring {OnOff(sensor.showRing)}  footprints {OnOff(sensor.showFootprints)}  walls {OnOff(sensor.showWalls)}  cand {OnOff(sensor.showCandidates)}  los {OnOff(sensor.showLos)}  heat {OnOff(sensor.showHeat)}");
            var st = sensor.stats;
            sb.AppendLine($"scan: {st.obstacles} vols · {st.wallCols} wall cols (full {st.wallsFull}/crouch {st.wallsCrouch}/low {st.wallsLo})"
                + (sensor.HasThreat ? $"  → {st.tested} cand, {st.valid} valid" : ""));
        }

        private void LockForces(System.Text.StringBuilder sb)
        {
            var ch = cityMap != null ? cityMap.SpawnedCharacter : null;
            var cc = ch != null ? ch.GetComponent<CombatController>() : null;
            sb.AppendLine("<b>FORCES</b>  <i>(locked — L to edit)</i>");
            if (cc == null) { sb.AppendLine("<i>no spawned character</i>"); return; }
            sb.AppendLine($"facing {cc.DebugFaceMode} · owner {cc.DebugFaceOwner} · demand {cc.FaceDemandDeg:+0.0;-0.0;0.0}° · beat {cc.Beat} · {cc.State}");
            foreach (var kv in cc.YawForce.OrderByDescending(x => Mathf.Abs(x.Value)).Take(4))
                sb.AppendLine($"  {kv.Key}: {kv.Value:+0;-0;0}°/s");
        }

        private void LockVision(System.Text.StringBuilder sb)
        {
            var ch = cityMap != null ? cityMap.SpawnedCharacter : null;
            var vis = ch != null ? ch.GetComponent<VisionSensor>() : null;
            sb.AppendLine("<b>VISION</b>  <i>(locked — L to edit)</i>");
            if (vis == null) { sb.AppendLine("<i>no VisionSensor on spawned character</i>"); return; }
            sb.AppendLine($"enabled {OnOff(vis.enabled)}   target {(pickedAim.HasValue ? pickedAim.Value.ToString("F1") : "—")}   coneOwner {OnOff(VisionSensor.ConeOwner == vis)}");
            sb.AppendLine($"range {vis.range:F0}m  halfAngle {vis.halfAngleDeg:F0}°  rise {vis.riseTime:F1}s  decay {vis.decayTime:F1}s");
            sb.AppendLine($"viz — cone {OnOff(vis.showCone)}  targetRay {OnOff(vis.showTargetRay)}  wedgeFill {OnOff(vis.showConeFill)}");
            sb.AppendLine($"detection: <b>{vis.MaxDetection * 100f:F0}%</b>" +
                (vis.AnyDetected ? "  <color=#ff5544>DETECTED</color>"
                 : vis.AnyVisible ? "  <color=#ffbb44>in cone…</color>" : ""));
        }

        private void LockTanSuit(System.Text.StringBuilder sb)
        {
            var ch = cachedTanSuit;   // read-only: no scan in locked mode — last-known ref or null
            if (ch == null && Time.unscaledTime >= tanSuitRefAt)
            {   // single retry per second so a fresh spawn shows up
                cachedTanSuit = FindCharacter("Civilian_02");
                tanSuitRefAt = Time.unscaledTime + 1f;
                ch = cachedTanSuit;
            }
            sb.AppendLine("<b>TAN SUIT</b>  <i>(locked — L to edit)</i>");
            if (ch == null) { sb.AppendLine("<i>Civilian_02 not found</i>"); return; }
            var cov = ch.GetComponent<CoverSensor>();
            var vis = ch.GetComponent<VisionSensor>();
            var cc  = ch.GetComponent<CombatController>();
            var slv = ch.GetComponent<ArmAimSolver>();
            Vector3 tp = ch.transform.position;
            sb.AppendLine($"pos {tp.x:F1},{tp.y:F1},{tp.z:F1}   comps — cov {(cov != null ? OnOff(cov.enabled) : "—")}  vis {(vis != null ? OnOff(vis.enabled) : "—")}  cc {(cc != null ? OnOff(cc.enabled) : "—")}  aim {(slv != null ? OnOff(slv.enabled) : "—")}");
            if (cov != null)
            {
                var st = cov.stats;
                sb.AppendLine($"cover: r {cov.radius:F1}  standoff {cov.standoff:F2}  viz ring {OnOff(cov.showRing)}/walls {OnOff(cov.showWalls)}/cand {OnOff(cov.showCandidates)}/los {OnOff(cov.showLos)}   last scan {st.wallCols} cols");
            }
            if (vis != null)
                sb.AppendLine($"vision: {vis.range:F0}m ±{vis.halfAngleDeg:F0}°  det <b>{vis.MaxDetection * 100f:F0}%</b>  cone {OnOff(vis.showCone)}/ray {OnOff(vis.showTargetRay)}/fill {OnOff(vis.showConeFill)}  owner {OnOff(VisionSensor.ConeOwner == vis)}");
            sb.AppendLine(cc != null
                ? $"combat: <b>{cc.State}</b>{(cc.Watching ? $" <b>+ watch on {(cc.WatchTarget != null ? cc.WatchTarget.name : "?")}</b>" : "")}  move {cc.moveSpeed:F1}  targetDuck {cc.TargetDuck:F2}   duel {pitTanMode}"
                : "combat: <i>no CombatController</i>");
        }

        private void LockDemolitions(System.Text.StringBuilder sb)
        {
            var dmg = VoxelDamageSystem.Instance;
            sb.AppendLine("<b>DEMOLITIONS</b>  <i>(locked — L to edit)</i>");
            sb.AppendLine($"blast: {(pickedBlast.HasValue ? pickedBlast.Value.ToString("F1") : "—")} on {(blastVolume ?? "—")}   depth {blastDepth:F1}m  radius {blastRadius:F1}m  power {blastPower:F2}");
            if (dmg == null) { sb.AppendLine("<i>VoxelDamageSystem not spawned</i>"); return; }
            sb.AppendLine($"damage tint {OnOff(dmg.damageTint)}  reach {dmg.charReach}  str {dmg.charStrength:F2}  radial {dmg.charRadial:F2}  absorb {dmg.charAbsorb:F2}  debrisChar {dmg.debrisChar:F2}");
            sb.AppendLine($"voxelCollide {OnOff(dmg.shardVoxelCollision)}  pairing {OnOff(dmg.pairCells)}{(dmg.pairCells ? $" {dmg.pairChance:F2}" : "")}  rubble {OnOff(dmg.rubbleDeposit)} ({dmg.RubbleCells} cells)  damaged vols {dmg.DamagedVolumeCount}");
            sb.AppendLine($"occlusion {OnOff(dmg.occlusionAware)}  collapse {OnOff(dmg.dropUnsupported)}{(dmg.dropUnsupported ? $" imp {dmg.cellImpulse:F1}" : "")}  shockFront {OnOff(dmg.shockFront)}{(dmg.shockFront ? $" {dmg.frontPasses}p/{dmg.frontSec:F2}s" : "")}  radialLaunch {OnOff(dmg.flyerRadialVelocity)}");
        }

        private void LockMatRes(System.Text.StringBuilder sb)
        {
            int prof = Mathf.Clamp(matResSub, 0, (int)VoxelDamageMath.ResProfile.Count - 1);
            sb.AppendLine($"<b>MAT RES</b> — {VoxelDamageMath.ResProfileNames[prof]}  <i>(locked — L to edit)</i>");
            sb.AppendLine($"global ×scale {VoxelDamageMath.GlobalResistScale:F2}   power dial {blastPower:F1}");
            var tbl = VoxelDamageMath.ResTable[prof];
            var work = VoxelDamageMath.WorkshopRes[prof];
            int n = (int)VoxelDamageMath.ResClass.Count;
            for (int c = 0; c < n; c++)
            {
                string col = Mathf.Approximately(tbl[c], VoxelDamageMath.BaseResistance[c]) ? "#6fb7ff"
                           : Mathf.Approximately(tbl[c], work[c]) ? "#ffa64d" : "#5fe07a";
                sb.AppendLine($"  <color={col}>{VoxelDamageMath.ResClassNames[c]}</color>  {tbl[c]:F2}");
            }
        }

        /// <summary>HSlider with a wider label column so class name + live kill% fit;
        /// the name is a flat button when onName is supplied (MatRes class selection).</summary>
        private float ResRow(string name, float val, System.Action onName = null)
        {
            GUILayout.BeginHorizontal();
            if (onName != null)
            {
                if (GUILayout.Button(name, labelStyle, GUILayout.Width(150))) onName();
            }
            else GUILayout.Label(name, labelStyle, GUILayout.Width(150));
            float v = GUILayout.HorizontalSlider(val, 0f, 1f, GUILayout.ExpandWidth(true));
            GUILayout.Label(v.ToString("F2"), labelStyle, GUILayout.Width(42));
            GUILayout.EndHorizontal();
            return v;
        }

#if UNITY_EDITOR
        /// <summary>Locks a live resistance into the shipped table: rewrites the
        /// BaseResistance literal in VoxelDamageMath.cs and refreshes — the recompile
        /// + domain reload reseeds every layer (exits play mode).</summary>
        private void PromoteResToAuthored(int prof, int cls)
        {
            var vals = (float[])VoxelDamageMath.BaseResistance.Clone();
            vals[cls] = VoxelDamageMath.ResTable[prof][cls];
            var lit = "{ " + string.Join(", ", System.Array.ConvertAll(vals,
                v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "f")) + " }";
            string path = System.IO.Path.Combine(Application.dataPath, "Scripts/Sim/VoxelDamageMath.cs");
            string src = System.IO.File.ReadAllText(path);
            var rx = new System.Text.RegularExpressions.Regex(@"(BaseResistance\s*=\s*)\{[^}]*\}");
            if (!rx.IsMatch(src)) { Debug.LogError($"[MatRes] ❌ BaseResistance literal not found in {path}"); return; }
            System.IO.File.WriteAllText(path, rx.Replace(src, "$1" + lit, 1));
            Debug.Log($"[MatRes] 🔒 promoted {VoxelDamageMath.ResClassNames[cls]} → AUTHORED {vals[cls]:F2} — recompiling");
            UnityEditor.AssetDatabase.Refresh();
        }
#else
        private void PromoteResToAuthored(int prof, int cls) { }
#endif

        private float HSlider(string name, float val, float min, float max, string format = "F2")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, labelStyle, GUILayout.Width(105));
            float v = GUILayout.HorizontalSlider(val, min, max, GUILayout.ExpandWidth(true));
            GUILayout.Label(v.ToString(format), labelStyle, GUILayout.Width(42));
            GUILayout.EndHorizontal();
            return v;
        }
    }
}
