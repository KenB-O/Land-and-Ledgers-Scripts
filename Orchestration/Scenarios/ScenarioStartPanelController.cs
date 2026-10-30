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
    [DefaultExecutionOrder(260)]
    public sealed class ScenarioStartPanelController : MonoBehaviour
    {
        [SerializeField] private ScenarioAsset scenarioAsset;
        [SerializeField] private ScenarioDirector scenarioDirector;
        [SerializeField] private Canvas canvas;

        private GameObject panel;
        private TMP_Text objectiveLabel;
        private RectTransform objectiveViewport;
        private RectTransform objectivePanelRect;
        private RectTransform objectiveCanvasRect;
        private Button objectiveToggle;
        private string objectiveHeader = "SCENARIO OBJECTIVES";
        private bool objectivesExpanded = true;
        private float nextObjectiveRefreshTime;

        private void Awake()
        {
            scenarioDirector ??= FindFirstObjectByType<ScenarioDirector>();
            if (scenarioDirector == null)
            {
                Debug.LogError("[ScenarioHUD] No authoritative ScenarioDirector is present. The HUD will not create a competing scenario authority.", this);
                enabled = false;
                return;
            }

            if (scenarioAsset != null)
            {
                scenarioDirector.RegisterScenarioAsset(scenarioAsset);
            }
        }

        private void Start()
        {
            canvas = ResolveGameplayCanvas(canvas);
            if (canvas == null || scenarioDirector == null)
            {
                Debug.LogError("[ScenarioHUD] Could not resolve the active gameplay Canvas; scenario presentation was not created.", this);
                return;
            }

            RectTransform parent = canvas.transform as RectTransform;
            if (parent == null)
            {
                Debug.LogError($"[ScenarioHUD] Canvas '{canvas.name}' has no RectTransform.", canvas);
                return;
            }

            Debug.Log($"[ScenarioHUD] Bound Canvas '{canvas.name}' (mode={canvas.renderMode}, order={canvas.sortingOrder}); activeScenario={scenarioDirector.HasActiveScenario}.", this);
            if (!scenarioDirector.HasActiveScenario)
            {
                BuildPanel(parent);
                return;
            }

            // Main Scene can restore or bootstrap an active scenario before this
            // controller starts. The previous implementation only created the
            // objective surface after the start button was clicked, leaving
            // already-active scenarios with no visible goals.
            BuildObjectivePanel(parent);
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
            if (scenarioDirector == null)
            {
                return;
            }

            if (canvas == null)
            {
                canvas = ResolveGameplayCanvas(null);
            }

            if (scenarioDirector.HasActiveScenario && objectiveLabel == null && canvas != null)
            {
                if (panel != null) panel.SetActive(false);
                BuildObjectivePanel(canvas.transform as RectTransform);
            }

            if (objectiveLabel == null || !scenarioDirector.HasActiveScenario)
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

        private static Canvas ResolveGameplayCanvas(Canvas configured)
        {
            if (configured != null && configured.isActiveAndEnabled && configured.gameObject.activeInHierarchy)
            {
                return configured;
            }

            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas candidate = canvases[i];
                if (candidate != null
                    && candidate.isActiveAndEnabled
                    && candidate.gameObject.activeInHierarchy
                    && candidate.renderMode == RenderMode.ScreenSpaceOverlay
                    && string.Equals(candidate.gameObject.name, "UI Canvas", System.StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            // The authored HUD may be renamed by a scene-specific variant. In that
            // case, prefer an active overlay over world-space or camera-space canvases.
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas candidate = canvases[i];
                if (candidate != null
                    && candidate.isActiveAndEnabled
                    && candidate.gameObject.activeInHierarchy
                    && candidate.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    return candidate;
                }
            }

            return null;
        }

        private void BuildObjectivePanel(RectTransform parent)
        {
            if (parent == null || objectiveLabel != null)
            {
                return;
            }

            GameObject objectiveObject = new GameObject("ScenarioObjectivePanel", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            panel = objectiveObject;
            RectTransform rect = objectiveObject.transform as RectTransform;
            objectivePanelRect = rect;
            objectiveCanvasRect = parent;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            ApplyObjectivePanelGeometry();
            objectiveObject.GetComponent<Image>().color = new Color(0.055f, 0.06f, 0.055f, 0.94f);
            objectiveObject.transform.SetAsLastSibling();

            objectiveToggle = CreateButton(objectiveObject.transform, "SCENARIO OBJECTIVES  −");
            RectTransform toggleRect = objectiveToggle.transform as RectTransform;
            toggleRect.anchorMin = new Vector2(0f, 1f);
            toggleRect.anchorMax = new Vector2(1f, 1f);
            toggleRect.pivot = new Vector2(0.5f, 1f);
            toggleRect.anchoredPosition = new Vector2(0f, -8f);
            toggleRect.sizeDelta = new Vector2(-20f, 42f);
            objectiveToggle.onClick.AddListener(ToggleObjectives);

            GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            RectTransform viewport = viewportObject.transform as RectTransform;
            viewport.SetParent(rect, false);
            viewport.anchorMin = new Vector2(0f, 0f);
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(12f, 12f);
            viewport.offsetMax = new Vector2(-12f, -56f);
            objectiveViewport = viewport;

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

        private void ToggleObjectives()
        {
            objectivesExpanded = !objectivesExpanded;
            if (objectiveViewport != null)
            {
                objectiveViewport.gameObject.SetActive(objectivesExpanded);
            }

            if (panel != null)
            {
                ApplyObjectivePanelGeometry();
            }

            TMP_Text toggleLabel = objectiveToggle != null
                ? objectiveToggle.GetComponentInChildren<TMP_Text>()
                : null;
            if (toggleLabel != null)
            {
                toggleLabel.text = objectiveHeader + (objectivesExpanded ? "  −" : "  +");
            }
        }

        private void ApplyObjectivePanelGeometry()
        {
            if (objectivePanelRect == null || objectiveCanvasRect == null)
            {
                return;
            }

            Rect canvasBounds = objectiveCanvasRect.rect;
            float topInset = Mathf.Clamp(canvasBounds.height * 0.13f, 96f, 142f);
            float availableHeight = Mathf.Max(220f, canvasBounds.height - topInset - 24f);
            float expandedHeight = Mathf.Min(620f, availableHeight);
            float width = Mathf.Min(620f, Mathf.Max(300f, canvasBounds.width - 48f));
            objectivePanelRect.anchoredPosition = new Vector2(-24f, -topInset);
            objectivePanelRect.sizeDelta = new Vector2(width, objectivesExpanded ? expandedHeight : 58f);

            if (objectiveToggle != null)
            {
                RectTransform toggleRect = objectiveToggle.transform as RectTransform;
                if (toggleRect != null)
                {
                    toggleRect.sizeDelta = new Vector2(-20f, 42f);
                }
            }
        }

        private void RenderObjectives()
        {
            ScenarioRuntimeState state = scenarioDirector.Service.ActiveState;
            if (state == null || objectiveLabel == null)
            {
                return;
            }

            objectiveHeader = string.IsNullOrWhiteSpace(state.Asset.DisplayName)
                ? "SCENARIO OBJECTIVES"
                : state.Asset.DisplayName.ToUpperInvariant() + "  ·  OBJECTIVES";
            TMP_Text toggleLabel = objectiveToggle != null
                ? objectiveToggle.GetComponentInChildren<TMP_Text>()
                : null;
            if (toggleLabel != null)
            {
                string header = objectiveHeader + (objectivesExpanded ? "  −" : "  +");
                if (!string.Equals(toggleLabel.text, header, System.StringComparison.Ordinal))
                {
                    toggleLabel.text = header;
                }
            }

            int completed = 0;
            int total = 0;
            foreach (ScenarioGoal goal in state.Asset.Goals)
            {
                if (goal.Optional) continue;
                total++;
                if (state.IsGoalCompleted(goal.GoalId)) completed++;
            }

            string status = scenarioDirector.ObjectiveService != null && scenarioDirector.ObjectiveService.IsFailed
                ? "FAILED"
                : "IN PROGRESS";
            var lines = new System.Text.StringBuilder();
            lines.Append(state.Asset.DisplayName).Append('\n');
            lines.Append(status).Append(" — ").Append(completed).Append(" of ").Append(total).Append(" required goals complete\n\n");
            foreach (ScenarioGoal goal in state.Asset.Goals)
            {
                if (goal.Optional) continue;
                lines.Append(state.IsGoalCompleted(goal.GoalId) ? "✓ " : "□ ");
                lines.Append(goal.Text);
                if (!string.IsNullOrWhiteSpace(goal.TargetText))
                {
                    lines.Append(" — ");
                    lines.Append(goal.TargetText);
                }
                lines.Append('\n');
            }

            // Pass XI WS5 (UI refresh performance): this runs every 0.5s
            // while a scenario is active. Assigning identical text still
            // forces a TextMeshPro layout pass, so skip the assignment
            // when nothing changed. Behaviour is identical; only the
            // redundant rebuild is gone.
            string rendered = lines.ToString();
            if (!string.Equals(objectiveLabel.text, rendered, System.StringComparison.Ordinal))
            {
                objectiveLabel.text = rendered;
            }
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
