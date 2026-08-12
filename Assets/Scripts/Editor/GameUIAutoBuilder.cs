#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using SteelCity.Sim;
using SteelCity.UI;

namespace SteelCity.EditorTools
{
    /// <summary>
    /// One-click builder for the Gang Organizer UI using a native tabbed layout:
    /// Canvas, top bar, left-side info panel (tab bar + single content page shown
    /// at a time, including a Log tab), order buttons, and a bottom bar with the
    /// Run Week button.
    ///
    /// Only one InfoPanel page occupies space at a time (GameUIController.ShowTab
    /// toggles page visibility), so there's no flexible-height distribution to get
    /// wrong. All 7 pages (including the event log) use the same plain
    /// VerticalLayoutGroup page — no ScrollRect, no ContentSizeFitter anywhere.
    ///
    /// Usage: Steel City -> Build Game UI
    /// Re-running clears and rebuilds the "GameCanvas" GameObject if it exists.
    /// </summary>
    public static class GameUIAutoBuilder
    {
        private static readonly Color BgPanel = new(0.137f, 0.137f, 0.220f, 1f);
        private static readonly Color BgCard = new(0.094f, 0.094f, 0.157f, 1f);
        private static readonly Color Gold = new(0.886f, 0.690f, 0.290f, 1f);
        private static readonly Color TextBright = new(0.980f, 0.980f, 1.000f, 1f);
        private static readonly Color Green = new(0.290f, 0.860f, 0.460f, 1f);

        [MenuItem("Steel City/Build Game UI")]
        public static void BuildUI()
        {
            var existing = GameObject.Find("GameCanvas");
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("Rebuild UI?",
                    "A GameCanvas already exists. Rebuilding will destroy and recreate it (along with CityMap3D and GameUIController). Continue?",
                    "Rebuild", "Cancel"))
                    return;
                Object.DestroyImmediate(existing);

                var oldMap = GameObject.Find("CityMap3D");
                if (oldMap != null) Object.DestroyImmediate(oldMap);
                var oldController = GameObject.Find("GameUIController");
                if (oldController != null) Object.DestroyImmediate(oldController);
                var oldEventSystem = GameObject.Find("EventSystem");
                if (oldEventSystem != null) Object.DestroyImmediate(oldEventSystem);

                // Clean up any orphaned scrollbar/mask artifacts from previous builds.
                foreach (var obj in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                {
                    if (obj.name.Contains("Scrollbar") || obj.name.Contains("Handle") || obj.name.Contains("Sliding Area"))
                        Object.DestroyImmediate(obj);
                }
            }

            // Disable stray scene cameras redundant with CityMap3D's dedicated camera.
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam.transform.parent == null && cam.gameObject.activeInHierarchy)
                {
                    cam.gameObject.SetActive(false);
                    Debug.Log($"[GameUIAutoBuilder] Disabled stray scene camera '{cam.name}'.");
                }
            }

            // --- Canvas ---
            var canvasObj = new GameObject("GameCanvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObj.AddComponent<GraphicRaycaster>();

            // --- EventSystem ---
            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
            }

            // --- Top Bar ---
            var topBar = CreatePanel("TopBar", canvasObj.transform, BgPanel);
            var topRect = topBar.GetComponent<RectTransform>();
            topRect.anchorMin = new Vector2(0, 1);
            topRect.anchorMax = new Vector2(1, 1);
            topRect.pivot = new Vector2(0.5f, 1);
            topRect.sizeDelta = new Vector2(0, 50);
            topRect.anchoredPosition = Vector2.zero;

            var weekText = CreateText("WeekText", topBar.transform, "Week 1", 18, TextBright);
            AnchorStretchVertical(weekText.rectTransform, 0f, 0.35f, 15, 0);
            weekText.alignment = TextAlignmentOptions.Left;

            var phaseText = CreateText("PhaseText", topBar.transform, "PLANNING", 18, new Color(0.98f, 0.82f, 0.29f));
            AnchorStretchVertical(phaseText.rectTransform, 0.35f, 0.65f, 0, 0);
            phaseText.alignment = TextAlignmentOptions.Center;
            phaseText.fontStyle = FontStyles.Bold;

            var treasuryText = CreateText("TreasuryText", topBar.transform, "$3000", 18, Green);
            AnchorStretchVertical(treasuryText.rectTransform, 0.65f, 0.85f, 0, 15);
            treasuryText.alignment = TextAlignmentOptions.Right;

            var charStatusText = CreateText("CharacterStatusText", topBar.transform, "Vinny Moretti [ ON STREET ]", 14, new Color(0.87f, 0.87f, 0.91f));
            AnchorStretchVertical(charStatusText.rectTransform, 0.85f, 1f, 0, 15);
            charStatusText.alignment = TextAlignmentOptions.Right;

            // --- Info Panel (tabbed, left side; map renders on the right) ---
            var infoPanel = CreatePanel("InfoPanel", canvasObj.transform, BgPanel);
            var ipRect = infoPanel.GetComponent<RectTransform>();
            // yMax=0.954 matches TopBar bottom (50px / 1080 ≈ 0.046, 1-0.046=0.954)
            // yMin=0.08 matches BottomBar top exactly
            ipRect.anchorMin = new Vector2(0f, 0.08f);
            ipRect.anchorMax = new Vector2(0.6f, 0.954f);
            ipRect.offsetMin = Vector2.zero;
            ipRect.offsetMax = Vector2.zero;

            // Single VerticalLayoutGroup — no ScrollRect, no ContentSizeFitter on the panel.
            var ipVlg = infoPanel.AddComponent<VerticalLayoutGroup>();
            ipVlg.spacing = 4;
            ipVlg.padding = new RectOffset(6, 6, 6, 6);
            ipVlg.childControlWidth = true;
            ipVlg.childControlHeight = true;
            ipVlg.childForceExpandWidth = true;
            ipVlg.childForceExpandHeight = false;

            // --- Tab Bar (fixed height, sits above the content area) ---
            var tabBar = new GameObject("TabBar");
            tabBar.transform.SetParent(infoPanel.transform, false);
            tabBar.AddComponent<RectTransform>();
            var tabHlg = tabBar.AddComponent<HorizontalLayoutGroup>();
            tabHlg.spacing = 2;
            tabHlg.childControlWidth = true;
            tabHlg.childControlHeight = true;
            tabHlg.childForceExpandWidth = true;
            tabHlg.childForceExpandHeight = true;
            tabBar.AddComponent<LayoutElement>().preferredHeight = 34;

            var hoodsTabBtn = CreateTabButton("HoodsTab", tabBar.transform, "Hoods");
            var blockTabBtn = CreateTabButton("BlockTab", tabBar.transform, "Block");
            var ordersTabBtn = CreateTabButton("OrdersTab", tabBar.transform, "Editor");
            var financeTabBtn = CreateTabButton("FinanceTab", tabBar.transform, "Finance");
            var policeTabBtn = CreateTabButton("PoliceTab", tabBar.transform, "Police");
            var investTabBtn = CreateTabButton("InvestTab", tabBar.transform, "Invest");
            var logTabBtn = CreateTabButton("LogTab", tabBar.transform, "Log");

            // --- Content Area (holds all 7 pages; GameUIController shows one at a time) ---
            var contentArea = CreatePanel("ContentArea", infoPanel.transform, BgCard);
            contentArea.AddComponent<LayoutElement>().flexibleHeight = 1;

            var hoodsPage = CreatePage("HoodsPage", contentArea.transform, out var hoodList);
            var blockInfoPage = CreatePage("BlockInfoPage", contentArea.transform, out var blockInfoContent);
            var ordersPage = CreatePage("OrdersPage", contentArea.transform, out var orderContent);
            var financePage = CreatePage("FinancePage", contentArea.transform, out var financeContent);
            var policePage = CreatePage("PolicePage", contentArea.transform, out var policeContent);
            var investigationPage = CreatePage("InvestigationPage", contentArea.transform, out var investContent);

            // Event log page — uses CreateScrollablePage so entries scroll instead of compressing.
            var eventLogPage = CreateScrollablePage("EventLogPage", contentArea.transform, out var eventLogContent);

            // Order buttons row — placed in Hoods page so player can select hood + assign order in one view
            var orderRow = new GameObject("OrderButtonRow");
            orderRow.transform.SetParent(hoodList, false);
            var hlg = orderRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = false;
            orderRow.AddComponent<LayoutElement>().preferredHeight = 32;

            var extortBtn = CreateButton("ExtortButton", orderRow.transform, "Extort", new Color(0.85f, 0.35f, 0.35f));
            var collectBtn = CreateButton("CollectButton", orderRow.transform, "Collect", new Color(0.35f, 0.75f, 0.45f));
            var patrolBtn = CreateButton("PatrolButton", orderRow.transform, "Patrol", new Color(0.35f, 0.55f, 0.85f));
            var intimidateBtn = CreateButton("IntimidateButton", orderRow.transform, "Intimidate", new Color(0.75f, 0.45f, 0.85f));
            var lieLowBtn = CreateButton("LieLowButton", orderRow.transform, "Lie Low", new Color(0.55f, 0.55f, 0.60f));

            // --- Bottom Bar (Run Week button only) ---
            var bottomBar = CreatePanel("BottomBar", canvasObj.transform, BgPanel);
            var bbRect = bottomBar.GetComponent<RectTransform>();
            bbRect.anchorMin = new Vector2(0, 0);
            bbRect.anchorMax = new Vector2(1, 0.08f);
            bbRect.offsetMin = Vector2.zero;
            bbRect.offsetMax = Vector2.zero;

            var runWeekBtn = CreateButton("RunWeekButton", bottomBar.transform, "RUN WEEK >", Green);
            var rwRect = runWeekBtn.GetComponent<RectTransform>();
            rwRect.anchorMin = new Vector2(0.35f, 0.15f);
            rwRect.anchorMax = new Vector2(0.65f, 0.85f);
            rwRect.offsetMin = Vector2.zero;
            rwRect.offsetMax = Vector2.zero;

            // --- 3D City Map (camera + empty root for CityMap3D) ---
            var mapObj = new GameObject("CityMap3D");
            var cityMap = mapObj.AddComponent<CityMap3D>();

            // Align camera viewport to match InfoPanel's complement exactly.
            var mapSo = new SerializedObject(cityMap);
            var vpYMinProp = mapSo.FindProperty("viewportYMin");
            var vpYMaxProp = mapSo.FindProperty("viewportYMax");
            if (vpYMinProp != null) vpYMinProp.floatValue = 0.08f;
            if (vpYMaxProp != null) vpYMaxProp.floatValue = 0.954f;
            mapSo.ApplyModifiedProperties();

            // --- GameUIController ---
            var controllerObj = new GameObject("GameUIController");
            var controller = controllerObj.AddComponent<GameUIController>();

            var so = new SerializedObject(controller);
            SetRef(so, "cityMap", cityMap);
            SetRef(so, "weekText", weekText);
            SetRef(so, "phaseText", phaseText);
            SetRef(so, "treasuryText", treasuryText);
            SetRef(so, "characterStatusText", charStatusText);
            SetRef(so, "hoodList", hoodList);
            SetRef(so, "blockInfoContent", blockInfoContent);
            SetRef(so, "financeContent", financeContent);
            SetRef(so, "policeContent", policeContent);
            SetRef(so, "investigationContent", investContent);
            SetRef(so, "extortButton", extortBtn.GetComponent<Button>());
            SetRef(so, "collectButton", collectBtn.GetComponent<Button>());
            SetRef(so, "patrolButton", patrolBtn.GetComponent<Button>());
            SetRef(so, "intimidateButton", intimidateBtn.GetComponent<Button>());
            SetRef(so, "lieLowButton", lieLowBtn.GetComponent<Button>());
            SetRef(so, "eventLogContent", eventLogContent);
            SetRef(so, "runWeekButton", runWeekBtn.GetComponent<Button>());
            SetRef(so, "hoodsPage", hoodsPage);
            SetRef(so, "blockInfoPage", blockInfoPage);
            SetRef(so, "ordersPage", ordersPage);
            SetRef(so, "financePage", financePage);
            SetRef(so, "policePage", policePage);
            SetRef(so, "investigationPage", investigationPage);
            SetRef(so, "eventLogPage", eventLogPage);
            SetRef(so, "hoodsTabButton", hoodsTabBtn.GetComponent<Button>());
            SetRef(so, "blockInfoTabButton", blockTabBtn.GetComponent<Button>());
            SetRef(so, "ordersTabButton", ordersTabBtn.GetComponent<Button>());
            SetRef(so, "financeTabButton", financeTabBtn.GetComponent<Button>());
            SetRef(so, "policeTabButton", policeTabBtn.GetComponent<Button>());
            SetRef(so, "investigationTabButton", investTabBtn.GetComponent<Button>());
            SetRef(so, "eventLogTabButton", logTabBtn.GetComponent<Button>());
            SetRef(so, "topBarRoot", topBar);
            SetRef(so, "infoPanelRoot", infoPanel);
            SetRef(so, "bottomBarRoot", bottomBar);
            so.ApplyModifiedProperties();

            Selection.activeGameObject = canvasObj;
            EditorUtility.DisplayDialog("Game UI Built",
                "Canvas + tabbed info panel + top/bottom bars created.\n\n" +
                "A GameUIController and CityMap3D GameObject were created and wired up automatically.\n\n" +
                "Press Play to test.",
                "OK");
        }

        private static void SetRef(SerializedObject so, string fieldName, Object value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop != null) prop.objectReferenceValue = value;
            else Debug.LogWarning($"[GameUIAutoBuilder] Field '{fieldName}' not found on GameUIController.");
        }

        private static GameObject CreatePanel(string name, Transform parent, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();
            var img = obj.AddComponent<Image>();
            img.color = color;
            return obj;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, string text, int fontSize, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void AnchorStretchVertical(RectTransform rect, float minX, float maxX, float leftPad, float rightPad)
        {
            rect.anchorMin = new Vector2(minX, 0);
            rect.anchorMax = new Vector2(maxX, 1);
            rect.offsetMin = new Vector2(leftPad, 0);
            rect.offsetMax = new Vector2(-rightPad, 0);
        }

        private static GameObject CreateButton(string name, Transform parent, string label, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();
            var img = obj.AddComponent<Image>();
            img.color = color;
            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = img;
            obj.AddComponent<ButtonHoverEffect>();

            var textObj = CreateText("Text", obj.transform, label, 13, Color.black);
            textObj.alignment = TextAlignmentOptions.Center;
            textObj.fontStyle = FontStyles.Bold;
            var tRect = textObj.rectTransform;
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = Vector2.one;
            tRect.offsetMin = Vector2.zero;
            tRect.offsetMax = Vector2.zero;

            return obj;
        }

        /// <summary>
        /// Creates a compact tab button for the InfoPanel's TabBar. Active/inactive
        /// coloring is handled at runtime by GameUIController.ShowTab().
        /// </summary>
        private static GameObject CreateTabButton(string name, Transform parent, string label)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();
            var img = obj.AddComponent<Image>();
            img.color = BgCard;
            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = img;
            obj.AddComponent<ButtonHoverEffect>();

            var textObj = CreateText("Text", obj.transform, label, 12, TextBright);
            textObj.alignment = TextAlignmentOptions.Center;
            textObj.fontStyle = FontStyles.Bold;
            var tRect = textObj.rectTransform;
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = Vector2.one;
            tRect.offsetMin = Vector2.zero;
            tRect.offsetMax = Vector2.zero;

            return obj;
        }

        /// <summary>
        /// Creates a full-height page inside the ContentArea. All pages stack in the
        /// same rect (stretch anchors) — GameUIController.ShowTab() toggles which one
        /// is active via SetActive(), so only one occupies visual space at a time.
        /// No ScrollRect, no ContentSizeFitter.
        /// </summary>
        private static GameObject CreatePage(string name, Transform parent, out Transform content)
        {
            var page = new GameObject(name);
            page.transform.SetParent(parent, false);
            var rect = page.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var vlg = page.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.padding = new RectOffset(10, 10, 8, 8);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            content = page.transform;
            return page;
        }

        /// <summary>
        /// Same as CreatePage but wraps content in a ScrollRect so entries
        /// maintain full height and scroll instead of compressing. Used for
        /// the event log which accumulates many lines over time.
        /// </summary>
        private static GameObject CreateScrollablePage(string name, Transform parent, out Transform content)
        {
            var page = new GameObject(name);
            page.transform.SetParent(parent, false);
            var pageRect = page.AddComponent<RectTransform>();
            pageRect.anchorMin = Vector2.zero;
            pageRect.anchorMax = Vector2.one;
            pageRect.offsetMin = Vector2.zero;
            pageRect.offsetMax = Vector2.zero;

            var scroller = page.AddComponent<ScrollRect>();
            scroller.horizontal = false;
            scroller.scrollSensitivity = 20f;

            var viewportObj = new GameObject("Viewport");
            viewportObj.transform.SetParent(page.transform, false);
            var vpRect = viewportObj.AddComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = Vector2.zero;
            vpRect.offsetMax = Vector2.zero;
            viewportObj.AddComponent<Image>().color = Color.white;
            var mask = viewportObj.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            scroller.viewport = vpRect;

            var contentObj = new GameObject("Content");
            contentObj.transform.SetParent(viewportObj.transform, false);
            var contentRect = contentObj.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 0);
            var vlg = contentObj.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.padding = new RectOffset(10, 10, 8, 8);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            contentObj.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroller.content = contentRect;

            content = contentObj.transform;
            return page;
        }
    }
}
#endif
