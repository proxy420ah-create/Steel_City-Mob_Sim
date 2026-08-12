using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SteelCity.UI
{
    /// <summary>
    /// Adds hover/press/disabled visual polish to any uGUI button.
    /// Hover: scale to 105%. Press: squash to 95%. Release: elastic bounce.
    /// Disabled: 40% opacity. No external dependencies.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ButtonHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [Header("Config")]
        [SerializeField] private float hoverScale = 1.05f;
        [SerializeField] private float pressScale = 0.95f;
        [SerializeField] private float animDuration = 0.08f;
        [SerializeField] private float disabledAlpha = 0.4f;

        [Header("Optional: color shift on hover")]
        [SerializeField] private bool useColorShift = false;
        [SerializeField] private Color hoverColorTint = new Color(1.15f, 1.15f, 1.15f, 1f);

        private Button button;
        private Image buttonImage;
        private Vector3 baseScale;
        private Color baseColor;
        private bool isPointerInside;
        private Coroutine scaleCo;

        private void Awake()
        {
            button = GetComponent<Button>();
            buttonImage = GetComponent<Image>();
            baseScale = transform.localScale;
            if (buttonImage != null) baseColor = buttonImage.color;
        }

        private void OnEnable()
        {
            // Reset to base on enable
            transform.localScale = baseScale;
            isPointerInside = false;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            isPointerInside = true;
            if (button != null && button.interactable)
            {
                AnimateTo(baseScale * hoverScale);
                if (useColorShift && buttonImage != null)
                    buttonImage.color = baseColor * hoverColorTint;
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isPointerInside = false;
            if (button != null && button.interactable)
            {
                AnimateTo(baseScale);
                if (useColorShift && buttonImage != null)
                    buttonImage.color = baseColor;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (button != null && button.interactable)
                AnimateTo(baseScale * pressScale);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (button != null && button.interactable)
            {
                // Bounce back with slight overshoot
                if (isPointerInside)
                    AnimateTo(baseScale * hoverScale);
                else
                    AnimateTo(baseScale);
            }
        }

        private void Update()
        {
            if (button == null || buttonImage == null) return;

            // Handle disabled state opacity
            if (!button.interactable)
            {
                var c = buttonImage.color;
                c.a = disabledAlpha;
                buttonImage.color = c;
            }
            else if (buttonImage.color.a < 1f)
            {
                // Restore full opacity when re-enabled
                var c = buttonImage.color;
                c.a = 1f;
                buttonImage.color = c;
            }
        }

        private void AnimateTo(Vector3 target)
        {
            if (scaleCo != null) StopCoroutine(scaleCo);
            scaleCo = StartCoroutine(AnimateScale(transform.localScale, target));
        }

        private IEnumerator AnimateScale(Vector3 from, Vector3 to)
        {
            float elapsed = 0f;
            while (elapsed < animDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = elapsed / animDuration;
                // EaseOutQuad
                t = 1f - (1f - t) * (1f - t);
                transform.localScale = Vector3.Lerp(from, to, t);
                yield return null;
            }
            transform.localScale = to;
        }
    }
}
