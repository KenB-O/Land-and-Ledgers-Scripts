using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LandLedgers.UI
{
    /// <summary>
    /// Runtime tooltip panel shared by a canvas. Created on demand by tooltip targets.
    /// </summary>
    public sealed class UITooltipController : MonoBehaviour
    {
        private const float MaxWidth = 360f;
        private const float ScreenPadding = 8f;

        private RectTransform rectTransform;
        private RectTransform canvasRect;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI bodyText;
        private UITooltipTarget currentTarget;

        public static UITooltipController GetOrCreate(Canvas canvas)
        {
            if (canvas == null)
            {
                return null;
            }

            var existing = canvas.GetComponentInChildren<UITooltipController>(true);
            if (existing != null)
            {
                return existing;
            }

            var root = new GameObject("Runtime_Tooltip", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement), typeof(UITooltipController));
            root.transform.SetParent(canvas.transform, false);

            var tooltip = root.GetComponent<UITooltipController>();
            tooltip.BuildVisuals(canvas);
            root.SetActive(false);
            return tooltip;
        }

        public void Show(UITooltipTarget target, string title, string body, RectTransform anchor)
        {
            if (target == null)
            {
                return;
            }

            title = string.IsNullOrWhiteSpace(title) ? string.Empty : title.Trim();
            body = string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(body))
            {
                Hide(target);
                return;
            }

            EnsureInitialized();
            currentTarget = target;
            titleText.text = title;
            titleText.gameObject.SetActive(!string.IsNullOrEmpty(title));
            bodyText.text = body;
            bodyText.gameObject.SetActive(!string.IsNullOrEmpty(body));

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
            PositionNear(anchor);
        }

        public void Hide(UITooltipTarget target)
        {
            if (target != null && currentTarget != null && currentTarget != target)
            {
                return;
            }

            currentTarget = null;
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void BuildVisuals(Canvas canvas)
        {
            EnsureInitialized();
            canvasRect = canvas.transform as RectTransform;

            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(0f, 1f);
            rectTransform.pivot = new Vector2(0f, 1f);
            rectTransform.sizeDelta = new Vector2(MaxWidth, 64f);

            var image = GetComponent<Image>();
            image.color = new Color(0.08f, 0.09f, 0.08f, 0.98f);
            image.raycastTarget = false;

            var layout = GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 3f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var fitter = GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var layoutElement = GetComponent<LayoutElement>();
            layoutElement.preferredWidth = MaxWidth;
            layoutElement.flexibleWidth = 0f;

            titleText = CreateText("Title", 15f, FontStyles.Bold, new Color(0.94f, 0.89f, 0.74f, 1f));
            bodyText = CreateText("Body", 13.5f, FontStyles.Normal, new Color(0.86f, 0.87f, 0.8f, 1f));
        }

        private TextMeshProUGUI CreateText(string name, float size, FontStyles style, Color color)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            child.transform.SetParent(transform, false);

            var text = child.GetComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.fontStyle = style;
            LandLedgersTypography.ApplyRole(text, name == "Title" ? LandLedgersTypography.TextRole.TooltipTitle : LandLedgersTypography.TextRole.TooltipBody);
            text.color = color;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.margin = Vector4.zero;
            text.text = string.Empty;
            return text;
        }

        private void EnsureInitialized()
        {
            rectTransform ??= transform as RectTransform;
            canvasRect ??= GetComponentInParent<Canvas>()?.transform as RectTransform;
        }

        private void PositionNear(RectTransform anchor)
        {
            EnsureInitialized();
            if (rectTransform == null || canvasRect == null)
            {
                return;
            }

            var canvas = canvasRect.GetComponent<Canvas>();
            var scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

            Vector2 screenPosition;
            if (anchor != null)
            {
                var corners = new Vector3[4];
                anchor.GetWorldCorners(corners);
                screenPosition = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                screenPosition += new Vector2(14f * scale, -12f * scale);
            }
            else
            {
                screenPosition = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }

            var anchored = new Vector2(screenPosition.x / scale, -((Screen.height - screenPosition.y) / scale));
            var width = Mathf.Min(MaxWidth, Mathf.Max(120f, rectTransform.rect.width));
            var height = Mathf.Max(42f, rectTransform.rect.height);
            var canvasWidth = canvasRect.rect.width;
            var canvasHeight = canvasRect.rect.height;

            var maxX = Mathf.Max(ScreenPadding, canvasWidth - width - ScreenPadding);
            var minY = Mathf.Min(-ScreenPadding, -canvasHeight + height + ScreenPadding);
            anchored.x = Mathf.Clamp(anchored.x, ScreenPadding, maxX);
            anchored.y = Mathf.Clamp(anchored.y, minY, -ScreenPadding);
            rectTransform.anchoredPosition = anchored;
        }
    }
}
