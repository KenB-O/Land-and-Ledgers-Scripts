using LandLedgers.Orchestration.Scenarios;
using LandLedgers.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LandLedgers.FirstLedger
{
    /// <summary>
    /// Lightweight player-facing scenario entry point for the one-scene workflow.
    /// It presents the authored scenario before the clock starts and delegates the
    /// actual run to ScenarioDirector.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScenarioStartPanelController : MonoBehaviour
    {
        [SerializeField] private ScenarioAsset scenarioAsset;
        [SerializeField] private ScenarioDirector scenarioDirector;
        [SerializeField] private Canvas canvas;

        private GameObject panel;
        private TMP_Text objectiveLabel;
        private float nextObjectiveRefreshTime;

        private void Awake()
        {
            scenarioDirector ??= FindAnyObjectByType<ScenarioDirector>();
            if (scenarioDirector == null)
            {
                scenarioDirector = gameObject.AddComponent<ScenarioDirector>();
            }

            if (scenarioAsset != null)
            {
                scenarioDirector.RegisterScenarioAsset(scenarioAsset);
            }
        }

        private void Start()
        {
            canvas ??= FindAnyObjectByType<Canvas>();
            if (canvas != null && scenarioDirector != null && !scenarioDirector.HasActiveScenario)
            {
                BuildPanel(canvas.transform as RectTransform);
            }
        }

        private void BuildPanel(RectTransform parent)
        {
            panel = new GameObject("ScenarioStartPanel", typeof(RectTransform), typeof(Image));
            RectTransform rect = panel.transform as RectTransform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(720f, 420f);
            panel.GetComponent<Image>().color = new Color(0.055f, 0.06f, 0.055f, 0.98f);

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(34, 34, 30, 30);
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;

            CreateText(panel.transform, scenarioAsset != null ? scenarioAsset.DisplayName : "Land & Ledgers", 28f, FontStyles.Bold);
            CreateText(panel.transform, scenarioAsset != null ? scenarioAsset.Description : "No authored scenario is available.", 16f, FontStyles.Normal);
            CreateText(panel.transform,
                "Choose a scenario to begin. The campaign clock starts only when you press Start.",
                14f, FontStyles.Normal);

            Button start = CreateButton(panel.transform, "START FIRST LEDGER");
            start.onClick.AddListener(StartScenario);
        }

        private void StartScenario()
        {
            if (scenarioDirector == null || scenarioAsset == null)
            {
                return;
            }

            if (scenarioDirector.SwitchScenario(scenarioAsset.ScenarioId))
            {
                if (panel != null)
                {
                    panel.SetActive(false);
                }

                BuildObjectivePanel(canvas != null ? canvas.transform as RectTransform : null);
            }
        }

        private void Update()
        {
            if (objectiveLabel == null || scenarioDirector == null || !scenarioDirector.HasActiveScenario)
            {
                return;
            }

            if (UnityEngine.Time.unscaledTime < nextObjectiveRefreshTime)
            {
                return;
            }

            nextObjectiveRefreshTime = UnityEngine.Time.unscaledTime + 0.5f;
            RenderObjectives();
        }

        private void BuildObjectivePanel(RectTransform parent)
        {
            if (parent == null || objectiveLabel != null)
            {
                return;
            }

            GameObject objectiveObject = new GameObject("ScenarioObjectivePanel", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            RectTransform rect = objectiveObject.transform as RectTransform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f, -24f);
            rect.sizeDelta = new Vector2(390f, 260f);
            objectiveObject.GetComponent<Image>().color = new Color(0.055f, 0.06f, 0.055f, 0.94f);

            GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            RectTransform viewport = viewportObject.transform as RectTransform;
            viewport.SetParent(rect, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(12f, 12f);
            viewport.offsetMax = new Vector2(-12f, -12f);

            GameObject contentObject = new GameObject("Content", typeof(RectTransform), typeof(ContentSizeFitter));
            RectTransform content = contentObject.transform as RectTransform;
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);
            ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = objectiveObject.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            ScrollRectInputRelay.Install(viewport, scroll);

            objectiveLabel = CreateText(content, string.Empty, 15f, FontStyles.Normal);
            objectiveLabel.alignment = TextAlignmentOptions.TopLeft;
            LayoutElement element = objectiveLabel.GetComponent<LayoutElement>();
            element.minHeight = 0f;
            element.preferredHeight = -1f;
            element.flexibleHeight = 0f;
            objectiveLabel.overflowMode = TextOverflowModes.Overflow;
            RenderObjectives();
        }

        private void RenderObjectives()
        {
            ScenarioRuntimeState state = scenarioDirector.Service.ActiveState;
            if (state == null || objectiveLabel == null)
            {
                return;
            }

            var lines = new System.Text.StringBuilder("SCENARIO OBJECTIVES\n");
            foreach (ScenarioGoal goal in state.Asset.Goals)
            {
                lines.Append(state.IsGoalCompleted(goal.GoalId) ? "✓ " : "□ ");
                lines.Append(goal.Text);
                if (!string.IsNullOrWhiteSpace(goal.TargetText))
                {
                    lines.Append(" — ");
                    lines.Append(goal.TargetText);
                }
                lines.Append('\n');
            }

            objectiveLabel.text = lines.ToString();
        }

        private static TMP_Text CreateText(Transform parent, string text, float size, FontStyles style)
        {
            GameObject go = new GameObject("ScenarioText", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.text = text ?? string.Empty;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = new Color(0.92f, 0.9f, 0.8f, 1f);
            label.textWrappingMode = TextWrappingModes.Normal;
            LandLedgersTypography.ApplyRole(label, ResolveTextRole(size, style), size);
            LayoutElement element = go.AddComponent<LayoutElement>();
            element.minHeight = size + 16f;
            element.preferredHeight = size + 28f;
            return label;
        }

        private static LandLedgersTypography.TextRole ResolveTextRole(float size, FontStyles style)
        {
            if ((style & FontStyles.Bold) != 0)
            {
                return size >= 24f
                    ? LandLedgersTypography.TextRole.ScreenTitle
                    : LandLedgersTypography.TextRole.ButtonLabel;
            }

            return size >= 18f
                ? LandLedgersTypography.TextRole.PanelHeader
                : LandLedgersTypography.TextRole.Body;
        }

        private static Button CreateButton(Transform parent, string label)
        {
            GameObject go = new GameObject("ScenarioStartButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = new Color(0.38f, 0.29f, 0.14f, 1f);
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            CreateText(go.transform, label, 18f, FontStyles.Bold);
            LandLedgersTypography.ApplyButtonLabel(button);
            LayoutElement element = go.AddComponent<LayoutElement>();
            element.minHeight = 52f;
            element.preferredHeight = 52f;
            return button;
        }
    }
}
