using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SteelCity.Sim
{
    public class TickHUD : MonoBehaviour
    {
        private Canvas hudCanvas;
        private TMP_Text tickCounterText;
        private TMP_Text phaseText;
        private TMP_Text ticksRemainingText;
        private TMP_Text orderText;
        private TMP_Text fpsText;
        private ScrollRect eventLogScroll;
        private RectTransform eventLogContent;
        private readonly Queue<GameObject> logEntries = new();
        private const int MaxLogEntries = 30;

        private Color goldColor = new(0.89f, 0.69f, 0.29f);
        private Color greenColor = new(0.29f, 0.86f, 0.46f);
        private Color redColor = new(0.92f, 0.29f, 0.29f);
        private Color textColor = new(0.87f, 0.87f, 0.91f);
        private Color dimColor = new(0.53f, 0.53f, 0.62f);
        private Color bgColor = new(0.05f, 0.05f, 0.08f, 0.85f);

        public void Initialize()
        {
            hudCanvas = gameObject.GetComponent<Canvas>();
            if (hudCanvas == null)
                hudCanvas = gameObject.AddComponent<Canvas>();
            hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hudCanvas.sortingOrder = 50;

            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            BuildHUD();
            Debug.Log("[TickHUD] Initialized");
        }

        void BuildHUD()
        {
            var panelObj = new GameObject("TickHUDPanel");
            panelObj.transform.SetParent(hudCanvas.transform, false);
            var panelRT = panelObj.AddComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.01f, 0.02f);
            panelRT.anchorMax = new Vector2(0.22f, 0.98f);
            panelRT.offsetMin = Vector2.zero;
            panelRT.offsetMax = Vector2.zero;
            var panelImg = panelObj.AddComponent<Image>();
            panelImg.color = new Color(0.05f, 0.05f, 0.08f, 0.65f);

            var vlg = panelObj.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var titleObj = CreateText(panelObj.transform, "WORKING WEEK", goldColor, 18, true);
            var titleLE = titleObj.AddComponent<LayoutElement>();
            titleLE.preferredHeight = 26;

            phaseText = CreateTextComponent(panelObj.transform, "Phase: Idle", textColor, 14, true);
            tickCounterText = CreateTextComponent(panelObj.transform, "Tick: 0", textColor, 14, true);
            ticksRemainingText = CreateTextComponent(panelObj.transform, "Remaining: 12000", dimColor, 14, true);
            orderText = CreateTextComponent(panelObj.transform, "Order: None", goldColor, 14, true);
            fpsText = CreateTextComponent(panelObj.transform, "-- FPS", dimColor, 14, true);

            var dividerObj = new GameObject("Divider");
            dividerObj.transform.SetParent(panelObj.transform, false);
            var divImg = dividerObj.AddComponent<Image>();
            divImg.color = new Color(0.3f, 0.3f, 0.35f, 0.5f);
            var divLE = dividerObj.AddComponent<LayoutElement>();
            divLE.preferredHeight = 2;

            var logTitle = CreateText(panelObj.transform, "EVENT LOG", dimColor, 12, true);
            var logTitleLE = logTitle.AddComponent<LayoutElement>();
            logTitleLE.preferredHeight = 18;

            var scrollObj = new GameObject("EventLogScroll");
            scrollObj.transform.SetParent(panelObj.transform, false);
            var scrollRect = scrollObj.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            var scrollRT = scrollObj.GetComponent<RectTransform>();
            scrollRT.anchorMin = Vector2.zero;
            scrollRT.anchorMax = Vector2.one;
            var scrollLE = scrollObj.AddComponent<LayoutElement>();
            scrollLE.preferredHeight = 600;
            scrollLE.flexibleHeight = 1;

            var viewportObj = new GameObject("Viewport");
            viewportObj.transform.SetParent(scrollObj.transform, false);
            var viewportRT = viewportObj.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = Vector2.zero;
            var viewportImg = viewportObj.AddComponent<Image>();
            viewportImg.color = new Color(0, 0, 0, 0);
            var viewportMask = viewportObj.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;
            scrollRect.viewport = viewportRT;

            eventLogContent = new GameObject("EventLogContent").AddComponent<RectTransform>();
            eventLogContent.SetParent(viewportObj.transform, false);
            eventLogContent.anchorMin = new Vector2(0, 1);
            eventLogContent.anchorMax = Vector2.one;
            eventLogContent.pivot = new Vector2(0.5f, 1f);
            eventLogContent.offsetMin = Vector2.zero;
            eventLogContent.offsetMax = Vector2.zero;
            var eventLogLayout = eventLogContent.gameObject.AddComponent<VerticalLayoutGroup>();
            eventLogLayout.spacing = 2;
            eventLogLayout.padding = new RectOffset(4, 4, 4, 4);
            eventLogLayout.childControlWidth = true;
            eventLogLayout.childForceExpandWidth = true;
            eventLogLayout.childForceExpandHeight = false;
            eventLogLayout.childAlignment = TextAnchor.UpperLeft;
            var csf = eventLogContent.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.content = eventLogContent;
            eventLogScroll = scrollRect;
        }

        public void UpdatePhase(string phaseName, int tickElapsed, int tickRemaining)
        {
            if (phaseText != null)
                phaseText.text = $"Phase: {phaseName}";
            if (tickCounterText != null)
                tickCounterText.text = $"Tick: {tickElapsed}";
            if (ticksRemainingText != null)
                ticksRemainingText.text = $"Remaining: {tickRemaining}";
        }

        public void UpdateOrder(string orderType, string targetBlock)
        {
            if (orderText != null)
                orderText.text = $"Order: {orderType} -> {targetBlock}";
        }

        public void AddLogEntry(string text, Color color)
        {
            if (eventLogContent == null) return;

            var entryObj = new GameObject($"Log_{logEntries.Count}");
            entryObj.transform.SetParent(eventLogContent, false);
            var tmp = entryObj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 11;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.raycastTarget = false;
            var le = entryObj.AddComponent<LayoutElement>();
            le.preferredHeight = 16;
            le.flexibleHeight = 0;

            logEntries.Enqueue(entryObj);
            while (logEntries.Count > MaxLogEntries)
            {
                var old = logEntries.Dequeue();
                if (old != null) Destroy(old);
            }

            if (eventLogScroll != null)
            {
                Canvas.ForceUpdateCanvases();
                eventLogScroll.verticalNormalizedPosition = 0f;
            }
        }

        public void ClearLog()
        {
            foreach (var entry in logEntries)
                if (entry != null) Destroy(entry);
            logEntries.Clear();
        }

        public void UpdateFPS(string text, Color color)
        {
            if (fpsText != null)
            {
                fpsText.text = text;
                fpsText.color = color;
            }
        }

        public void Shutdown()
        {
            if (hudCanvas != null)
                hudCanvas.enabled = false;
        }

        public void Show()
        {
            if (hudCanvas != null)
                hudCanvas.enabled = true;
        }

        private GameObject CreateText(Transform parent, string text, Color color, int fontSize, bool bold)
        {
            var obj = new GameObject($"Text_{text.GetHashCode():X}");
            obj.transform.SetParent(parent, false);
            var tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.raycastTarget = false;
            return obj;
        }

        private TMP_Text CreateTextComponent(Transform parent, string text, Color color, int fontSize, bool bold)
        {
            var obj = new GameObject($"Text_{System.Guid.NewGuid().ToString().Substring(0, 8)}");
            obj.transform.SetParent(parent, false);
            var tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.raycastTarget = false;
            var le = obj.AddComponent<LayoutElement>();
            le.preferredHeight = 20;
            return tmp;
        }
        private TMP_Text perfStatsText;

        public void UpdatePerfStats(string text)
        {
            if (perfStatsText == null)
            {
                var obj = new GameObject("PerfStatsText");
                obj.transform.SetParent(hudCanvas.transform, false);
                var rt = obj.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 0);
                rt.anchorMax = new Vector2(0.5f, 0);
                rt.anchoredPosition = new Vector2(10, 10);
                rt.sizeDelta = new Vector2(400, 60);
                perfStatsText = obj.AddComponent<TextMeshProUGUI>();
                perfStatsText.fontSize = 12;
                perfStatsText.color = new Color(0.85f, 1f, 0.85f, 0.9f);
                perfStatsText.alignment = TextAlignmentOptions.BottomLeft;
                perfStatsText.raycastTarget = false;
            }
            perfStatsText.text = text;
        }
    }
}
