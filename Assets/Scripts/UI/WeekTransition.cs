using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SteelCity.Sim
{
    /// <summary>
    /// Handles the fade-to-black transition between Planning and Working Week.
    /// Flow: fade out → "Loading..." → camera setup → "Week N Starts..." → fade in.
    /// </summary>
    public class WeekTransition : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Seconds for fade out to black.")]
        public float fadeOutDuration = 1.0f;
        [Tooltip("Seconds to show 'Loading...' before camera setup.")]
        public float loadingHoldDuration = 1.5f;
        [Tooltip("Seconds to show 'Week N Starts...' before fade in.")]
        public float weekStartHoldDuration = 1.5f;
        [Tooltip("Seconds for fade in from black.")]
        public float fadeInDuration = 1.0f;
        [Tooltip("Seconds to hold after fade-in before starting the action (let scene settle).")]
        public float postFadeHoldDuration = 5.0f;

        [Header("Text Style")]
        public Font textFont;
        public int fontSize = 36;
        public Color textColor = new Color(0.89f, 0.69f, 0.29f, 1f);
        public Color loadingColor = new Color(0.6f, 0.6f, 0.7f, 1f);

        private CanvasGroup overlayGroup;
        private TMP_Text statusText;
        private Canvas canvas;
        private bool initialized;

        /// <summary>
        /// Runs the full transition sequence. Calls onReady during the "Loading..." phase
        /// so camera/sim setup can happen while screen is black.
        /// </summary>
        public IEnumerator RunTransition(int weekNumber, System.Action onReady, System.Action onStart = null)
        {
            EnsureInitialized();

            // Phase 1: Fade out to black
            statusText.text = "";
            statusText.color = loadingColor;
            yield return StartCoroutine(FadeAlpha(0f, 1f, fadeOutDuration));

            // Phase 2: "Loading..." — camera positions, sim gets ready
            statusText.text = "Loading...";
            statusText.color = loadingColor;
            yield return new WaitForSeconds(loadingHoldDuration);

            // Camera and sim setup happens here (screen is fully black)
            onReady?.Invoke();

            // Brief pause to let everything settle
            yield return new WaitForSeconds(0.3f);

            // Phase 3: "Week N Starts..."
            statusText.text = $"Week {weekNumber} Starts...";
            statusText.color = textColor;
            yield return new WaitForSeconds(weekStartHoldDuration);

            // Phase 4: Fade in from black
            statusText.text = "";
            yield return StartCoroutine(FadeAlpha(1f, 0f, fadeInDuration));

            // Hide overlay when fully transparent
            overlayGroup.blocksRaycasts = false;
            overlayGroup.interactable = false;

            // Phase 5: Hold on the scene before starting action
            if (postFadeHoldDuration > 0f)
                yield return new WaitForSeconds(postFadeHoldDuration);

            // Start the simulation/action
            onStart?.Invoke();
        }

        IEnumerator FadeAlpha(float from, float to, float duration)
        {
            overlayGroup.blocksRaycasts = true;
            overlayGroup.interactable = true;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                overlayGroup.alpha = Mathf.Lerp(from, to, t);
                yield return null;
            }
            overlayGroup.alpha = to;
        }

        void EnsureInitialized()
        {
            if (initialized) return;

            // Create canvas if not present
            canvas = GetComponent<Canvas>();
            if (canvas == null)
                canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            // Full-screen black image with CanvasGroup
            var imageObj = new GameObject("TransitionOverlay");
            imageObj.transform.SetParent(transform, false);
            var image = imageObj.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = true;

            // Stretch to fill
            var imageRT = image.rectTransform;
            imageRT.anchorMin = Vector2.zero;
            imageRT.anchorMax = Vector2.one;
            imageRT.offsetMin = Vector2.zero;
            imageRT.offsetMax = Vector2.zero;

            overlayGroup = imageObj.AddComponent<CanvasGroup>();
            overlayGroup.alpha = 0f;
            overlayGroup.blocksRaycasts = false;
            overlayGroup.interactable = false;

            // Status text centered on screen
            var textObj = new GameObject("StatusText");
            textObj.transform.SetParent(imageObj.transform, false);
            statusText = textObj.AddComponent<TextMeshProUGUI>();
            statusText.fontSize = fontSize;
            statusText.alignment = TextAlignmentOptions.Center;
            statusText.color = textColor;
            statusText.raycastTarget = false;

            var textRT = statusText.rectTransform;
            textRT.anchorMin = new Vector2(0.5f, 0.5f);
            textRT.anchorMax = new Vector2(0.5f, 0.5f);
            textRT.sizeDelta = new Vector2(800, 100);
            textRT.anchoredPosition = Vector2.zero;

            initialized = true;
        }
    }
}
