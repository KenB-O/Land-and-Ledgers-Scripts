using LandLedgers.Orchestration.Scenarios;
using LandLedgers.Orchestration.Scenarios.Generation;
using LandLedgers.Persistence;
using LandLedgers.UI;
using LandLedgers.Time;
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
        [Header("Authored objective HUD")]
        [SerializeField] private GameObject authoredObjectivePanel;
        [SerializeField] private TMP_Text authoredObjectiveLabel;
        [SerializeField] private RectTransform authoredObjectiveViewport;
        [SerializeField] private Button authoredObjectiveToggle;

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
                // The start panel promises that a fresh campaign clock begins
                // only after scenario selection. Hold the authoritative clock
                // while that choice is on screen; restored active runs keep
                // their serialized pause/speed state.
                if (TimeManager.Instance != null)
                {
                    TimeManager.Instance.SetPaused(true);
                }

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
            Transform authoredPanel = parent.Find("ScenarioStartPanel");
            if (authoredPanel != null)
            {
                panel = authoredPanel.gameObject;
                Button authoredStartButton = panel.GetComponentInChildren<Button>(true);
                if (authoredStartButton != null)
                {
                    authoredStartButton.onClick.RemoveListener(StartScenario);
                    authoredStartButton.onClick.AddListener(StartScenario);
                }

                panel.SetActive(true);
                return;
            }

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

#if UNITY_EDITOR
        public GameObject AuthorStartPanelForScene()
        {
            if (canvas == null)
            {
                canvas = ResolveGameplayCanvas(null);
            }

            RectTransform parent = canvas != null ? canvas.transform as RectTransform : null;
            if (parent == null)
            {
                return null;
            }

            BuildPanel(parent);
            if (panel != null)
            {
                panel.SetActive(false);
            }

            return panel;
        }
#endif

        private void StartScenario()
        {
            if (scenarioDirector == null || scenarioAsset == null)
            {
                return;
            }

            TimeManager clock = TimeManager.Instance;
            TimeSaveDto previousClock = clock != null ? clock.CaptureSaveDto() : null;
            bool hasScenarioStart = scenarioAsset.StartingWorld != null;
            string startDateLabel = hasScenarioStart
                ? $"{scenarioAsset.StartingWorld.StartYear:D4}-{scenarioAsset.StartingWorld.StartMonth:D2}-{scenarioAsset.StartingWorld.StartDay:D2}"
                : "missing from the scenario asset";
            bool clockStarted = hasScenarioStart && clock != null && clock.StartNewScenarioAtDate(
                scenarioAsset.StartingWorld.StartYear,
                scenarioAsset.StartingWorld.StartMonth,
                scenarioAsset.StartingWorld.StartDay);

            if (clockStarted && scenarioDirector.SwitchScenario(scenarioAsset.ScenarioId))
            {
                if (clock != null)
                {
                    clock.SetPaused(false);
                }

                if (panel != null)
                {
                    panel.SetActive(false);
                }

                BuildObjectivePanel(canvas != null ? canvas.transform as RectTransform : null);
            }
            else if (clock != null && previousClock != null)
            {
                clock.LoadFromSaveDto(previousClock);
                Debug.LogError($"[ScenarioHUD] Scenario '{scenarioAsset.ScenarioId}' could not start; its start date is {startDateLabel}. The prior clock state was restored.", this);
            }
        }

        /// <summary>
        /// Sets the selected scenario's calendar before New Game population and
        /// economic state are generated. The scenario is not activated here;
        /// the player's Start action still owns activation.
        /// </summary>
        public bool PrepareScenarioClockForWorldGeneration()
        {
            if (scenarioAsset == null || scenarioAsset.StartingWorld == null || TimeManager.Instance == null)
            {
                return false;
            }

            StartingWorldConfiguration start = scenarioAsset.StartingWorld;
            return TimeManager.Instance.StartNewScenarioAtDate(start.StartYear, start.StartMonth, start.StartDay);
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

            if (authoredObjectivePanel != null
                && authoredObjectiveLabel != null
                && authoredObjectiveViewport != null
                && authoredObjectiveToggle != null)
            {
                panel = authoredObjectivePanel;
                objectivePanelRect = authoredObjectivePanel.transform as RectTransform;
                objectiveCanvasRect = parent;
                objectiveViewport = authoredObjectiveViewport;
                objectiveToggle = authoredObjectiveToggle;
                objectiveLabel = authoredObjectiveLabel;

                if (objectivePanelRect == null)
                {
                    Debug.LogError("[ScenarioHUD] The authored objective panel requires a RectTransform.", authoredObjectivePanel);
                    objectiveLabel = null;
                    return;
                }

                if (objectivePanelRect.parent != parent)
                {
                    objectivePanelRect.SetParent(parent, false);
                }

                authoredObjectivePanel.SetActive(true);
                // The objective panel is an authored HUD layer. Put it above
                // dashboard windows so the active scenario remains available
                // while the player reviews another interface.
                objectivePanelRect.SetAsLastSibling();
                objectiveToggle.onClick.RemoveListener(ToggleObjectives);
                objectiveToggle.onClick.AddListener(ToggleObjectives);
                ApplyObjectivePanelGeometry();
                RenderObjectives();
                return;
            }

            Debug.LogWarning("[ScenarioHUD] Authored objective HUD references are incomplete; using the compatibility runtime panel.", this);

            GameObject objectiveObject = new GameObject("ScenarioObjectivePanel", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            panel = objectiveObject;
            authoredObjectivePanel = objectiveObject;
            RectTransform rect = objectiveObject.transform as RectTransform;
            objectivePanelRect = rect;
            objectiveCanvasRect = parent;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
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
            authoredObjectiveLabel = objectiveLabel;
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
            float width = Mathf.Min(460f, Mathf.Max(280f, canvasBounds.width * 0.25f));
            objectivePanelRect.anchorMin = new Vector2(0f, 1f);
            objectivePanelRect.anchorMax = new Vector2(0f, 1f);
            objectivePanelRect.pivot = new Vector2(0f, 1f);
            objectivePanelRect.anchoredPosition = new Vector2(24f, -topInset);
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

            ScenarioObjectiveService objectiveService = scenarioDirector.ObjectiveService;
            bool failed = objectiveService != null && objectiveService.IsFailed;
            string status = failed ? "FAILED"
                : total == 0 ? "SANDBOX"
                : completed == total ? "VICTORY"
                : "IN PROGRESS";
            var lines = new System.Text.StringBuilder();
            lines.Append(state.Asset.DisplayName).Append('\n');
            if (total > 0)
            {
                lines.Append(status).Append(" — ").Append(completed).Append(" of ").Append(total).Append(" required goals complete\n\n");
            }
            else
            {
                lines.Append("SANDBOX — no mandatory objectives\n\n");
            }

            int currentDayIndex = TimeManager.Instance != null ? TimeManager.Instance.CurrentAbsoluteDayIndex : 0;
            var presentationGoals = new System.Collections.Generic.List<ScenarioObjectivePresentation.GoalRow>();
            foreach (ScenarioGoal goal in state.Asset.Goals)
            {
                if (goal.Optional) continue;
                int deadlineDayIndex = -1;
                foreach (ScenarioQualifierRegistration registration in objectiveService?.State?.QualifierRegistrations
                    ?? new System.Collections.Generic.List<ScenarioQualifierRegistration>())
                {
                    if (registration == null || !string.Equals(registration.GoalId, goal.GoalId, System.StringComparison.Ordinal)) continue;
                    deadlineDayIndex = registration.DeadlineDayIndex == int.MaxValue ? -1 : registration.DeadlineDayIndex;
                    break;
                }

                string goalText = string.IsNullOrWhiteSpace(goal.TargetText)
                    ? goal.Text
                    : goal.Text + " — " + goal.TargetText;
                presentationGoals.Add(new ScenarioObjectivePresentation.GoalRow
                {
                    GoalId = goal.GoalId,
                    Text = goalText,
                    DeadlineDayIndex = deadlineDayIndex,
                });
            }

            ScenarioHeadlineSnapshot headline = objectiveService != null
                ? objectiveService.BuildHeadlineSnapshot(state.Asset.DisplayName, completed, total, string.Empty)
                : new ScenarioHeadlineSnapshot { Title = state.Asset.DisplayName };
            lines.Append(ScenarioObjectivePresentation.BuildObjectiveLines(
                headline,
                presentationGoals,
                state.CaptureCompletedGoalIds(),
                currentDayIndex));

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
