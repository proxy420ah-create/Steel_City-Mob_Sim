using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SteelCity.UI
{
    /// <summary>
    /// A single collapsible section with a clickable header and animated content area.
    /// Used inside an AccordionGroup. Click header to expand/collapse.
    /// </summary>
    public class AccordionSection : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private float animDuration = 0.2f;
        [SerializeField] private bool startExpanded = false;

        [Header("Refs (auto-assigned)")]
        [SerializeField] private Button headerButton;
        [SerializeField] private TMP_Text headerLabel;
        [SerializeField] private TMP_Text arrowLabel;
        [SerializeField] private RectTransform contentRect;
        [SerializeField] private LayoutElement contentLE;

        private bool isExpanded;
        private float targetHeight;
        private Coroutine animCo;
        private AccordionGroup group;

        /// <summary>The content container — add child controls here.</summary>
        public Transform Content => contentRect;

        /// <summary>Is this section currently expanded?</summary>
        public bool IsExpanded => isExpanded;

        /// <summary>Set the header label text.</summary>
        public void SetHeader(string text)
        {
            if (headerLabel != null) headerLabel.text = text;
        }

        /// <summary>Set the header color.</summary>
        public void SetHeaderColor(Color color)
        {
            if (headerLabel != null) headerLabel.color = color;
        }

        /// <summary>Configure which group this section belongs to.</summary>
        public void SetGroup(AccordionGroup grp) => group = grp;

        /// <summary>Programmatic setup — called when creating accordion sections in code.</summary>
        public void SetupAccordion(Button hdrBtn, TMP_Text hdrLabel, TMP_Text arrow, RectTransform content, LayoutElement contentLayout, bool expanded)
        {
            headerButton = hdrBtn;
            headerLabel = hdrLabel;
            arrowLabel = arrow;
            contentRect = content;
            contentLE = contentLayout;
            startExpanded = expanded;
            isExpanded = expanded;

            if (headerButton != null)
                headerButton.onClick.AddListener(Toggle);
        }

        /// <summary>Force expand without animation (for initial setup).</summary>
        public void ExpandInstant()
        {
            isExpanded = true;
            if (contentRect != null) contentRect.gameObject.SetActive(true);
            if (contentLE != null) contentLE.preferredHeight = -1;
            UpdateArrow();
        }

        /// <summary>Force collapse without animation.</summary>
        public void CollapseInstant()
        {
            isExpanded = false;
            if (contentRect != null) contentRect.gameObject.SetActive(false);
            if (contentLE != null) contentLE.preferredHeight = 0;
            UpdateArrow();
        }

        // Listener is added in SetupAccordion() to avoid double-registration

        private void Start()
        {
            if (startExpanded)
                ExpandInstant();
            else
                CollapseInstant();
        }

        public void Toggle()
        {
            if (isExpanded)
            {
                // Collapse
                if (group != null) group.OnSectionToggled(this, false);
                AnimateCollapse();
            }
            else
            {
                // Expand
                if (group != null) group.OnSectionToggled(this, true);
                AnimateExpand();
            }
        }

        private void AnimateExpand()
        {
            isExpanded = true;
            if (contentRect != null) contentRect.gameObject.SetActive(true);

            // Measure preferred height
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            targetHeight = LayoutUtility.GetPreferredHeight(contentRect);

            UpdateArrow();
            if (animCo != null) StopCoroutine(animCo);
            animCo = StartCoroutine(AnimateHeight(0f, targetHeight, true));
        }

        private void AnimateCollapse()
        {
            isExpanded = false;
            UpdateArrow();

            float startH = contentLE != null ? contentLE.preferredHeight : targetHeight;
            if (startH < 0) startH = targetHeight;

            if (animCo != null) StopCoroutine(animCo);
            animCo = StartCoroutine(AnimateHeight(startH, 0f, false));
        }

        private IEnumerator AnimateHeight(float from, float to, bool expanding)
        {
            if (contentLE == null) yield break;

            float elapsed = 0f;
            while (elapsed < animDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = elapsed / animDuration;
                // EaseOutQuad
                t = 1f - (1f - t) * (1f - t);
                contentLE.preferredHeight = Mathf.Lerp(from, to, t);
                yield return null;
            }

            contentLE.preferredHeight = to;

            if (!expanding && contentRect != null)
                contentRect.gameObject.SetActive(false);
            else if (expanding)
                contentLE.preferredHeight = -1; // let layout take over
        }

        private void UpdateArrow()
        {
            if (arrowLabel != null)
                arrowLabel.text = isExpanded ? "▼" : "▶";
        }
    }
}
