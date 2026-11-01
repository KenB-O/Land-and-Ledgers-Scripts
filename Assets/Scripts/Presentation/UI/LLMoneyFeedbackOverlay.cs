using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LandLedgers.UI
{
    [DisallowMultipleComponent]
    public sealed class LLMoneyFeedbackOverlay : MonoBehaviour
    {
#if UNITY_EDITOR
        private const string SmallMoneySpritePath = "Assets/Resources/UI/MoneyBills/historical_bill_5_worn_small_cash.jpeg";
        private const string MediumMoneySpritePath = "Assets/Resources/UI/MoneyBills/historical_bill_50_operating_money.jpeg";
        private const string HeavyMoneySpritePath = "Assets/Resources/UI/MoneyBills/historical_bill_100_capital_moment.jpeg";
#endif
        private const string SmallMoneyResourcePath = "UI/MoneyBills/historical_bill_5_worn_small_cash";
        private const string MediumMoneyResourcePath = "UI/MoneyBills/historical_bill_50_operating_money";
        private const string HeavyMoneyResourcePath = "UI/MoneyBills/historical_bill_100_capital_moment";

        [SerializeField] private Sprite smallMoneySprite;
        [SerializeField] private Sprite mediumMoneySprite;
        [SerializeField] private Sprite heavyMoneySprite;

        private RectTransform root;

        public static LLMoneyFeedbackOverlay GetOrCreate(Canvas canvas)
        {
            if (canvas == null)
            {
                return null;
            }

            LLMoneyFeedbackOverlay existing = canvas.GetComponentInChildren<LLMoneyFeedbackOverlay>(true);
            if (existing != null)
            {
                return existing;
            }

            GameObject overlayObject = new("LL_MoneyFeedbackOverlay", typeof(RectTransform), typeof(CanvasGroup), typeof(LLMoneyFeedbackOverlay));
            overlayObject.transform.SetParent(canvas.transform, false);

            RectTransform rect = overlayObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();

            CanvasGroup group = overlayObject.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            group.ignoreParentGroups = true;

            return overlayObject.GetComponent<LLMoneyFeedbackOverlay>();
        }

        private void Awake()
        {
            root = transform as RectTransform;
            AssignDefaultSpritesIfNeeded();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            AssignDefaultSpritesIfNeeded();
        }
#endif

        public void Show(LLFeedbackKind kind, string context, Vector2? anchoredPosition = null)
        {
            root ??= transform as RectTransform;
            if (root == null)
            {
                return;
            }

            string displayText = ResolveDisplayText(kind, context);
            GameObject item = new("LL_MoneyFeedbackItem", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            item.transform.SetParent(root, false);
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = ResolveSize(kind, displayText);
            rect.anchoredPosition = anchoredPosition ?? ResolveDefaultPosition(kind);

            Image image = item.GetComponent<Image>();
            image.sprite = ResolveSprite(kind);
            image.color = image.sprite != null ? new Color(1f, 1f, 1f, ResolveAlpha(kind)) : ResolveColor(kind);
            ConfigureBillImage(image, rect, kind, displayText);
            image.raycastTarget = false;

            TMP_Text label = CreateLabel(item.transform, kind, displayText);
            CanvasGroup group = item.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            StartCoroutine(AnimateAndDestroy(item, rect, group, label, ResolveLifetime(kind, displayText), ResolveRise(kind)));
        }

        private TMP_Text CreateLabel(Transform parent, LLFeedbackKind kind, string displayText)
        {
            GameObject labelObject = new("LL_MoneyFeedbackLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);
            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(10f, 4f);
            rect.offsetMax = new Vector2(-10f, -4f);

            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            label.text = displayText;
            label.fontSize = ResolveFontSize(kind, displayText);
            label.alignment = TextAlignmentOptions.MidlineRight;
            label.color = new Color(0.95f, 0.91f, 0.78f, 0.92f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            LandLedgersTypography.ApplyRole(label, LandLedgersTypography.TextRole.HudUtility);
            return label;
        }

        private IEnumerator AnimateAndDestroy(GameObject item, RectTransform rect, CanvasGroup group, TMP_Text label, float lifetime, float rise)
        {
            Vector2 start = rect.anchoredPosition;
            Vector2 end = start + new Vector2(0f, rise);
            float elapsed = 0f;
            while (elapsed < lifetime && item != null)
            {
                elapsed += global::UnityEngine.Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / lifetime);
                rect.anchoredPosition = Vector2.Lerp(start, end, Smooth(t));
                group.alpha = t < 0.65f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.65f) / 0.35f);
                yield return null;
            }

            if (item != null)
            {
                Destroy(item);
            }
        }

        private Sprite ResolveSprite(LLFeedbackKind kind)
        {
            AssignDefaultSpritesIfNeeded();
            return kind switch
            {
                LLFeedbackKind.SmallCash => smallMoneySprite,
                LLFeedbackKind.MediumCash => mediumMoneySprite,
                LLFeedbackKind.HeavyCash => heavyMoneySprite,
                _ => mediumMoneySprite
            };
        }

        private void AssignDefaultSpritesIfNeeded()
        {
#if UNITY_EDITOR
            smallMoneySprite = smallMoneySprite != null ? smallMoneySprite : LoadEditorSprite(SmallMoneySpritePath);
            mediumMoneySprite = mediumMoneySprite != null ? mediumMoneySprite : LoadEditorSprite(MediumMoneySpritePath);
            heavyMoneySprite = heavyMoneySprite != null ? heavyMoneySprite : LoadEditorSprite(HeavyMoneySpritePath);
#endif
            smallMoneySprite = smallMoneySprite != null ? smallMoneySprite : LoadResourceSprite(SmallMoneyResourcePath);
            mediumMoneySprite = mediumMoneySprite != null ? mediumMoneySprite : LoadResourceSprite(MediumMoneyResourcePath);
            heavyMoneySprite = heavyMoneySprite != null ? heavyMoneySprite : LoadResourceSprite(HeavyMoneyResourcePath);
        }

#if UNITY_EDITOR
        private static Sprite LoadEditorSprite(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? null : AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
#endif

        private static Sprite LoadResourceSprite(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            Texture2D texture = Resources.Load<Texture2D>(path);
            if (texture == null)
            {
                return null;
            }

            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
        }

        private static Vector2 ResolveSize(LLFeedbackKind kind, string displayText)
        {
            return ResolveSize(kind, displayText, null);
        }

        private static Vector2 ResolveSize(LLFeedbackKind kind, string displayText, Sprite sprite)
        {
            Vector2 baseSize = kind switch
            {
                LLFeedbackKind.SmallCash => new Vector2(112f, 26f),
                LLFeedbackKind.HeavyCash => new Vector2(184f, 42f),
                _ => new Vector2(148f, 34f)
            };

            int length = string.IsNullOrWhiteSpace(displayText) ? 0 : displayText.Length;
            float extraWidth = Mathf.Clamp((length - 16) * 3.5f, 0f, 132f);
            float nativeAspectWidth = sprite != null && sprite.rect.height > 0f
                ? baseSize.y * (sprite.rect.width / sprite.rect.height)
                : baseSize.x;
            return new Vector2(Mathf.Max(baseSize.x + extraWidth, nativeAspectWidth), baseSize.y);
        }

        private static void ConfigureBillImage(Image image, RectTransform rect, LLFeedbackKind kind, string displayText)
        {
            if (image == null || rect == null)
            {
                return;
            }

            image.preserveAspect = image.sprite != null;
            if (image.sprite == null)
            {
                return;
            }

            rect.sizeDelta = ResolveSize(kind, displayText, image.sprite);
        }

        private static Vector2 ResolveDefaultPosition(LLFeedbackKind kind)
        {
            return kind == LLFeedbackKind.HeavyCash ? new Vector2(-28f, 108f) : new Vector2(-28f, 78f);
        }

        private static Color ResolveColor(LLFeedbackKind kind)
        {
            return kind switch
            {
                LLFeedbackKind.SmallCash => new Color(0.18f, 0.21f, 0.17f, 0.72f),
                LLFeedbackKind.HeavyCash => new Color(0.16f, 0.11f, 0.08f, 0.84f),
                _ => new Color(0.18f, 0.16f, 0.12f, 0.78f)
            };
        }

        private static float ResolveAlpha(LLFeedbackKind kind)
        {
            return kind == LLFeedbackKind.HeavyCash ? 0.96f : 0.9f;
        }

        private static string ResolveDisplayText(LLFeedbackKind kind, string context)
        {
            string normalizedContext = NormalizeContext(context);
            if (!string.IsNullOrWhiteSpace(normalizedContext))
            {
                return kind switch
                {
                    LLFeedbackKind.StampApproval => $"Approved · {normalizedContext}",
                    LLFeedbackKind.LedgerClose => $"Ledger closed · {normalizedContext}",
                    _ => normalizedContext
                };
            }

            return kind switch
            {
                LLFeedbackKind.SmallCash => "Liquid Cash noted",
                LLFeedbackKind.MediumCash => "Funds moved",
                LLFeedbackKind.HeavyCash => "Capital recorded",
                LLFeedbackKind.StampApproval => "Approved",
                LLFeedbackKind.LedgerClose => "Ledger closed",
                _ => "Recorded"
            };
        }

        private static float ResolveLifetime(LLFeedbackKind kind, string displayText)
        {
            float baseLifetime = kind == LLFeedbackKind.HeavyCash ? 1.35f : 0.95f;
            if (string.IsNullOrWhiteSpace(displayText))
            {
                return baseLifetime;
            }

            return baseLifetime + Mathf.Clamp((displayText.Length - 26) * 0.012f, 0f, 0.4f);
        }

        private static float ResolveRise(LLFeedbackKind kind)
        {
            return kind == LLFeedbackKind.HeavyCash ? 18f : 12f;
        }

        private static float ResolveFontSize(LLFeedbackKind kind, string displayText)
        {
            if (!string.IsNullOrWhiteSpace(displayText) && displayText.Length > 34)
            {
                return kind == LLFeedbackKind.HeavyCash ? 13.5f : 12.25f;
            }

            return kind == LLFeedbackKind.HeavyCash ? 15f : 13f;
        }

        private static string NormalizeContext(string context)
        {
            if (string.IsNullOrWhiteSpace(context))
            {
                return string.Empty;
            }

            string raw = context.Trim()
                .Replace("Owner Cash", "Liquid Cash")
                .Replace("owner cash", "Liquid Cash")
                .Replace('\n', ' ')
                .Replace('\r', ' ');

            StringBuilder builder = new();
            bool previousWhitespace = false;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsWhiteSpace(c))
                {
                    if (!previousWhitespace)
                    {
                        builder.Append(' ');
                    }

                    previousWhitespace = true;
                    continue;
                }

                previousWhitespace = false;
                builder.Append(c);
            }

            string normalized = builder.ToString().Trim().TrimEnd('.', ';');
            return normalized.Length <= 56 ? normalized : normalized.Substring(0, 53) + "...";
        }

        private static float Smooth(float t)
        {
            return t * t * (3f - 2f * t);
        }
    }
}
