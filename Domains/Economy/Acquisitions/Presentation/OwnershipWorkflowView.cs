using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LandLedgers.UI
{
    [DisallowMultipleComponent]
    public sealed class OwnershipWorkflowView : MonoBehaviour
    {
        [SerializeField] private RectTransform root;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text stageText;
        [SerializeField] private RawImage previewImage;
        [SerializeField] private TMP_Text previewFallbackText;
        [SerializeField] private TMP_Text summaryText;
        [SerializeField] private TMP_Text siteText;
        [SerializeField] private TMP_Text postureText;
        [SerializeField] private TMP_Text ledgerText;
        [SerializeField] private RectTransform infoViewport;
        [SerializeField] private RectTransform infoContent;
        [SerializeField] private RectTransform actionRoot;
        [SerializeField] private RectTransform processActionRow;
        [SerializeField] private RectTransform diligenceActionRow;
        [SerializeField] private RectTransform footerActionRow;
        [SerializeField] private Button watchButton;
        [SerializeField] private Button conditionButton;
        [SerializeField] private Button staffingButton;
        [SerializeField] private Button supplierButton;
        [SerializeField] private Button demandButton;
        [SerializeField] private Button valueButton;
        [SerializeField] private Button reliabilityButton;
        [SerializeField] private Button seriousnessButton;
        [SerializeField] private Button stanceButton;
        [SerializeField] private Button primaryButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button loanMinusHundredButton;
        [SerializeField] private Button loanMinusTenButton;
        [SerializeField] private Button loanPlusTenButton;
        [SerializeField] private Button loanPlusHundredButton;
        [SerializeField] private ScrollRect notesScrollRect;

        private bool actionModeLoanActive;
        private bool leadActionsRequestedVisible = true;

        public RectTransform Root => root != null ? root : transform as RectTransform;
        public TMP_Text TitleText => titleText;
        public TMP_Text StageText => stageText;
        public RawImage PreviewImage => previewImage;
        public TMP_Text PreviewFallbackText => previewFallbackText;
        public TMP_Text SummaryText => summaryText;
        public TMP_Text SiteText => siteText;
        public TMP_Text PostureText => postureText;
        public TMP_Text LedgerText => ledgerText;
        public Button WatchButton => watchButton;
        public Button ConditionButton => conditionButton;
        public Button StaffingButton => staffingButton;
        public Button SupplierButton => supplierButton;
        public Button DemandButton => demandButton;
        public Button ValueButton => valueButton;
        public Button ReliabilityButton => reliabilityButton;
        public Button SeriousnessButton => seriousnessButton;
        public Button StanceButton => stanceButton;
        public Button PrimaryButton => primaryButton;
        public Button CloseButton => closeButton;
        public Button LoanMinusHundredButton => loanMinusHundredButton;
        public Button LoanMinusTenButton => loanMinusTenButton;
        public Button LoanPlusTenButton => loanPlusTenButton;
        public Button LoanPlusHundredButton => loanPlusHundredButton;

        public static OwnershipWorkflowView GetOrCreate(Canvas canvas)
        {
            if (canvas == null)
            {
                return null;
            }

            OwnershipWorkflowView existing = canvas.GetComponentInChildren<OwnershipWorkflowView>(true);
            if (existing != null)
            {
                existing.EnsureRuntimeState();
                return existing;
            }

            GameObject rootObject = new("Ownership Workflow Overlay", typeof(RectTransform), typeof(Image));
            rootObject.transform.SetParent(canvas.transform, false);
            OwnershipWorkflowView view = rootObject.AddComponent<OwnershipWorkflowView>();
            view.Build();
            view.EnsureRuntimeState();
            return view;
        }

        public void SetVisible(bool visible)
        {
            EnsureRuntimeState();
            if (Root == null)
            {
                return;
            }

            Root.gameObject.SetActive(visible);
            if (visible)
            {
                ResetNotesScrollPosition();
            }
        }

        public void SetActionMode(bool loanMode, bool leadActionsVisible)
        {
            EnsureRuntimeState();
            actionModeLoanActive = loanMode;
            leadActionsRequestedVisible = leadActionsVisible;
            ApplyActionModeVisibility();
        }

        public void SetLoanMode(bool loanMode)
        {
            EnsureRuntimeState();
            actionModeLoanActive = loanMode;
            ApplyActionModeVisibility();
        }

        public void SetLeadActionVisibility(bool visible)
        {
            EnsureRuntimeState();
            leadActionsRequestedVisible = visible;
            ApplyActionModeVisibility();
        }

        private void Build()
        {
            root = transform as RectTransform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            Image backdrop = GetComponent<Image>();
            backdrop.color = new Color(0.025f, 0.027f, 0.025f, 0.965f);
            backdrop.raycastTarget = true;

            VerticalLayoutGroup layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 20);
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            titleText = CreateText("Workflow_Title", root, "Formal Process", 24f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(titleText, LandLedgersTypography.TextRole.ModalTitle);
            titleText.margin = new Vector4(0f, 0f, 0f, 4f);
            AddLayout(titleText.rectTransform, -1f, 34f, 1f, 0f);

            stageText = CreateText("Workflow_Stage", root, "Stage", 15f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(stageText, LandLedgersTypography.TextRole.SectionHeader);
            stageText.color = new Color(0.82f, 0.86f, 0.8f, 1f);
            ConfigureAutoHeight(stageText.rectTransform, 24f);

            RectTransform body = CreateRect("Workflow_Body", root);
            HorizontalLayoutGroup bodyLayout = body.gameObject.AddComponent<HorizontalLayoutGroup>();
            bodyLayout.spacing = 14f;
            bodyLayout.childAlignment = TextAnchor.UpperLeft;
            bodyLayout.childControlWidth = true;
            bodyLayout.childControlHeight = true;
            bodyLayout.childForceExpandWidth = true;
            bodyLayout.childForceExpandHeight = true;
            AddLayout(body, -1f, -1f, 1f, 1f);

            RectTransform previewColumn = CreatePanel("Workflow_PreviewPanel", body);
            AddLayout(previewColumn, 520f, -1f, 0f, 1f);
            VerticalLayoutGroup previewLayout = previewColumn.gameObject.AddComponent<VerticalLayoutGroup>();
            previewLayout.padding = new RectOffset(10, 10, 10, 10);
            previewLayout.spacing = 8f;
            previewLayout.childControlWidth = true;
            previewLayout.childControlHeight = true;
            previewLayout.childForceExpandWidth = true;
            previewLayout.childForceExpandHeight = false;

            TMP_Text previewHeading = CreateText("Workflow_PreviewHeading", previewColumn, "Site Preview", 15f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(previewHeading, LandLedgersTypography.TextRole.SectionHeader);
            AddLayout(previewHeading.rectTransform, -1f, 24f, 1f, 0f);

            RectTransform previewFrame = CreatePanel("Workflow_PreviewFrame", previewColumn);
            AddLayout(previewFrame, -1f, 360f, 1f, 0f);
            AspectRatioFitter previewAspect = previewFrame.gameObject.AddComponent<AspectRatioFitter>();
            previewAspect.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
            previewAspect.aspectRatio = 4f / 3f;

            previewImage = new GameObject("Workflow_PreviewImage", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            previewImage.transform.SetParent(previewFrame, false);
            previewImage.raycastTarget = false;
            RectTransform previewRect = previewImage.transform as RectTransform;
            previewRect.anchorMin = Vector2.zero;
            previewRect.anchorMax = Vector2.one;
            previewRect.offsetMin = new Vector2(8f, 8f);
            previewRect.offsetMax = new Vector2(-8f, -8f);

            previewFallbackText = CreateText("Workflow_PreviewFallback", previewFrame, string.Empty, 15f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(previewFallbackText, LandLedgersTypography.TextRole.HelperText);
            previewFallbackText.alignment = TextAlignmentOptions.Center;
            previewFallbackText.color = new Color(0.84f, 0.86f, 0.82f, 0.95f);
            RectTransform fallbackRect = previewFallbackText.rectTransform;
            fallbackRect.anchorMin = Vector2.zero;
            fallbackRect.anchorMax = Vector2.one;
            fallbackRect.offsetMin = new Vector2(18f, 18f);
            fallbackRect.offsetMax = new Vector2(-18f, -18f);

            RectTransform rightColumn = CreateRect("Workflow_InfoColumn", body);
            VerticalLayoutGroup rightLayout = rightColumn.gameObject.AddComponent<VerticalLayoutGroup>();
            rightLayout.spacing = 10f;
            rightLayout.childAlignment = TextAnchor.UpperLeft;
            rightLayout.childControlWidth = true;
            rightLayout.childControlHeight = true;
            rightLayout.childForceExpandWidth = true;
            rightLayout.childForceExpandHeight = false;
            AddLayout(rightColumn, -1f, -1f, 1f, 1f);

            infoViewport = CreateRect("Workflow_InfoViewport", rightColumn);
            AddLayout(infoViewport, -1f, -1f, 1f, 1f);
            Image infoViewportImage = infoViewport.gameObject.AddComponent<Image>();
            infoViewportImage.color = new Color(0.055f, 0.06f, 0.055f, 0.38f);
            infoViewportImage.raycastTarget = true;
            infoViewport.gameObject.AddComponent<RectMask2D>();

            infoContent = CreateRect("Workflow_InfoContent", infoViewport);
            infoContent.anchorMin = new Vector2(0f, 1f);
            infoContent.anchorMax = new Vector2(1f, 1f);
            infoContent.pivot = new Vector2(0.5f, 1f);
            infoContent.anchoredPosition = Vector2.zero;
            infoContent.sizeDelta = Vector2.zero;

            VerticalLayoutGroup infoContentLayout = infoContent.gameObject.AddComponent<VerticalLayoutGroup>();
            infoContentLayout.padding = new RectOffset(0, 0, 0, 0);
            infoContentLayout.spacing = 10f;
            infoContentLayout.childControlWidth = true;
            infoContentLayout.childControlHeight = true;
            infoContentLayout.childForceExpandWidth = true;
            infoContentLayout.childForceExpandHeight = false;

            ContentSizeFitter infoFitter = infoContent.gameObject.AddComponent<ContentSizeFitter>();
            infoFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            infoFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            notesScrollRect = rightColumn.gameObject.AddComponent<ScrollRect>();
            notesScrollRect.viewport = infoViewport;
            notesScrollRect.content = infoContent;
            notesScrollRect.horizontal = false;
            notesScrollRect.vertical = true;
            notesScrollRect.movementType = ScrollRect.MovementType.Clamped;
            notesScrollRect.scrollSensitivity = 28f;

            RectTransform leadPanel = CreateSectionPanel("Workflow_LeadPanel", infoContent, "Lead Summary", out summaryText, 126f);
            AddLayout(leadPanel, -1f, -1f, 1f, 0f);
            summaryText.fontSize = 14f;

            RectTransform sitePanel = CreateSectionPanel("Workflow_SitePanel", infoContent, "Site Inspection", out siteText, 140f);
            AddLayout(sitePanel, -1f, -1f, 1f, 0f);
            siteText.fontSize = 13.5f;

            RectTransform posturePanel = CreateSectionPanel("Workflow_PosturePanel", infoContent, "Funding & Readiness", out postureText, 164f);
            AddLayout(posturePanel, -1f, -1f, 1f, 0f);
            postureText.fontSize = 13.5f;

            RectTransform notesPanel = CreatePanel("Workflow_NotesPanel", infoContent);
            AddLayout(notesPanel, -1f, -1f, 1f, 0f);
            ConfigureAutoHeight(notesPanel, 220f);
            VerticalLayoutGroup notesLayout = notesPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            notesLayout.padding = new RectOffset(12, 12, 12, 12);
            notesLayout.spacing = 8f;
            notesLayout.childAlignment = TextAnchor.UpperLeft;
            notesLayout.childControlWidth = true;
            notesLayout.childControlHeight = true;
            notesLayout.childForceExpandWidth = true;
            notesLayout.childForceExpandHeight = false;

            TMP_Text notesHeading = CreateText("Workflow_NotesHeading", notesPanel, "Process Notes", 15f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(notesHeading, LandLedgersTypography.TextRole.SectionHeader);
            AddLayout(notesHeading.rectTransform, -1f, 22f, 1f, 0f);

            ledgerText = CreateText("Workflow_Ledger", notesPanel, string.Empty, 13.5f, FontStyles.Normal);
            LandLedgersTypography.ApplyRole(ledgerText, LandLedgersTypography.TextRole.LogBody);
            ConfigureAutoHeight(ledgerText.rectTransform, 160f);
            AddLayout(ledgerText.rectTransform, -1f, -1f, 1f, 0f);

            actionRoot = CreateRect("Workflow_Actions", root);
            VerticalLayoutGroup actions = actionRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            actions.spacing = 8f;
            actions.childControlWidth = true;
            actions.childControlHeight = true;
            actions.childForceExpandWidth = true;
            actions.childForceExpandHeight = false;
            ContentSizeFitter actionFitter = actionRoot.gameObject.AddComponent<ContentSizeFitter>();
            actionFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            actionFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            AddLayout(actionRoot, -1f, -1f, 1f, 0f);

            processActionRow = CreateActionRow("Workflow_ProcessActions", actionRoot);
            diligenceActionRow = CreateActionRow("Workflow_DiligenceActions", actionRoot);
            footerActionRow = CreateActionRow("Workflow_FooterActions", actionRoot);

            watchButton = CreateButton("Workflow_Watch", processActionRow, "Mark Later", 118f);
            seriousnessButton = CreateButton("Workflow_Seriousness", processActionRow, "Seriousness", 118f);
            stanceButton = CreateButton("Workflow_Stance", processActionRow, "Stance", 102f);

            conditionButton = CreateButton("Workflow_Condition", diligenceActionRow, "Condition", 110f);
            staffingButton = CreateButton("Workflow_Staffing", diligenceActionRow, "Staffing", 102f);
            supplierButton = CreateButton("Workflow_Supplier", diligenceActionRow, "Supplier", 102f);
            demandButton = CreateButton("Workflow_Demand", diligenceActionRow, "Demand", 98f);
            valueButton = CreateButton("Workflow_Value", diligenceActionRow, "Value", 92f);
            reliabilityButton = CreateButton("Workflow_Reliability", diligenceActionRow, "Reliability", 118f);

            loanMinusHundredButton = CreateButton("Workflow_LoanMinus100", footerActionRow, "-$100", 78f);
            loanMinusTenButton = CreateButton("Workflow_LoanMinus10", footerActionRow, "-$10", 72f);
            loanPlusTenButton = CreateButton("Workflow_LoanPlus10", footerActionRow, "+$10", 72f);
            loanPlusHundredButton = CreateButton("Workflow_LoanPlus100", footerActionRow, "+$100", 78f);
            primaryButton = CreateButton("Workflow_Primary", footerActionRow, "Advance", 204f);
            closeButton = CreateButton("Workflow_Close", footerActionRow, "Back", 100f);

            SetVisible(false);
        }

        private void EnsureRuntimeState()
        {
            root ??= transform as RectTransform;
            if (root == null)
            {
                return;
            }

            if (transform.childCount <= 0)
            {
                Build();
            }

            titleText ??= FindNamedComponent<TMP_Text>("Workflow_Title");
            stageText ??= FindNamedComponent<TMP_Text>("Workflow_Stage");
            previewImage ??= FindNamedComponent<RawImage>("Workflow_PreviewImage");
            previewFallbackText ??= FindNamedComponent<TMP_Text>("Workflow_PreviewFallback");
            summaryText ??= FindNamedComponent<TMP_Text>("Workflow_LeadPanel_Body");
            siteText ??= FindNamedComponent<TMP_Text>("Workflow_SitePanel_Body");
            postureText ??= FindNamedComponent<TMP_Text>("Workflow_PosturePanel_Body");
            ledgerText ??= FindNamedComponent<TMP_Text>("Workflow_Ledger");
            infoViewport ??= FindNamedComponent<RectTransform>("Workflow_InfoViewport");
            infoContent ??= FindNamedComponent<RectTransform>("Workflow_InfoContent");
            actionRoot ??= FindNamedComponent<RectTransform>("Workflow_Actions");
            processActionRow ??= FindNamedComponent<RectTransform>("Workflow_ProcessActions");
            diligenceActionRow ??= FindNamedComponent<RectTransform>("Workflow_DiligenceActions");
            footerActionRow ??= FindNamedComponent<RectTransform>("Workflow_FooterActions");
            watchButton ??= FindNamedComponent<Button>("Workflow_Watch");
            conditionButton ??= FindNamedComponent<Button>("Workflow_Condition");
            staffingButton ??= FindNamedComponent<Button>("Workflow_Staffing");
            supplierButton ??= FindNamedComponent<Button>("Workflow_Supplier");
            demandButton ??= FindNamedComponent<Button>("Workflow_Demand");
            valueButton ??= FindNamedComponent<Button>("Workflow_Value");
            reliabilityButton ??= FindNamedComponent<Button>("Workflow_Reliability");
            seriousnessButton ??= FindNamedComponent<Button>("Workflow_Seriousness");
            stanceButton ??= FindNamedComponent<Button>("Workflow_Stance");
            primaryButton ??= FindNamedComponent<Button>("Workflow_Primary");
            closeButton ??= FindNamedComponent<Button>("Workflow_Close");
            loanMinusHundredButton ??= FindNamedComponent<Button>("Workflow_LoanMinus100");
            loanMinusTenButton ??= FindNamedComponent<Button>("Workflow_LoanMinus10");
            loanPlusTenButton ??= FindNamedComponent<Button>("Workflow_LoanPlus10");
            loanPlusHundredButton ??= FindNamedComponent<Button>("Workflow_LoanPlus100");
            notesScrollRect ??= FindInfoScrollRect();

            if (stageText != null)
            {
                ConfigureAutoHeight(stageText.rectTransform, 24f);
            }

            if (summaryText != null)
            {
                ConfigureAutoHeight(summaryText.rectTransform, 64f);
            }

            if (siteText != null)
            {
                ConfigureAutoHeight(siteText.rectTransform, 64f);
            }

            if (postureText != null)
            {
                ConfigureAutoHeight(postureText.rectTransform, 64f);
            }

            if (ledgerText != null)
            {
                ConfigureAutoHeight(ledgerText.rectTransform, 160f);
            }

            ApplyActionModeVisibility();
        }

        private void ApplyActionModeVisibility()
        {
            // Lead-action visibility is tracked as requested state and then resolved against the current loan-mode flag.
            // This keeps the workflow rows stable even if controller calls arrive in a different order later.
            bool showLeadActions = !actionModeLoanActive && leadActionsRequestedVisible;
            SetButtonVisible(watchButton, showLeadActions);
            SetButtonVisible(conditionButton, showLeadActions);
            SetButtonVisible(staffingButton, showLeadActions);
            SetButtonVisible(supplierButton, showLeadActions);
            SetButtonVisible(demandButton, showLeadActions);
            SetButtonVisible(valueButton, showLeadActions);
            SetButtonVisible(reliabilityButton, showLeadActions);
            SetButtonVisible(seriousnessButton, showLeadActions);
            SetButtonVisible(stanceButton, showLeadActions);

            SetButtonVisible(loanMinusHundredButton, actionModeLoanActive);
            SetButtonVisible(loanMinusTenButton, actionModeLoanActive);
            SetButtonVisible(loanPlusTenButton, actionModeLoanActive);
            SetButtonVisible(loanPlusHundredButton, actionModeLoanActive);
            RefreshActionRowVisibility();
        }

        private void RefreshActionRowVisibility()
        {
            SetRowVisible(processActionRow, HasVisibleChild(processActionRow));
            SetRowVisible(diligenceActionRow, HasVisibleChild(diligenceActionRow));
            SetRowVisible(footerActionRow, HasVisibleChild(footerActionRow));
        }

        private static bool HasVisibleChild(RectTransform row)
        {
            if (row == null)
            {
                return false;
            }

            for (int i = 0; i < row.childCount; i++)
            {
                if (row.GetChild(i).gameObject.activeSelf)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetRowVisible(RectTransform row, bool visible)
        {
            if (row != null)
            {
                row.gameObject.SetActive(visible);
            }
        }

        private ScrollRect FindInfoScrollRect()
        {
            ScrollRect[] candidates = GetComponentsInChildren<ScrollRect>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                ScrollRect candidate = candidates[i];
                if (candidate != null && candidate.viewport == infoViewport && candidate.content == infoContent)
                {
                    return candidate;
                }
            }

            return null;
        }

        private void ResetNotesScrollPosition()
        {
            if (notesScrollRect == null)
            {
                return;
            }

            // The dossier body is reused across desk, inspection, live, and inactive states.
            // Force it back to the top on open so tall auto-height sections do not preserve a stale offset from the prior file.
            Canvas.ForceUpdateCanvases();
            notesScrollRect.StopMovement();
            notesScrollRect.horizontalNormalizedPosition = 0f;
            notesScrollRect.verticalNormalizedPosition = 1f;
        }

        private T FindNamedComponent<T>(string objectName) where T : Component
        {
            T[] candidates = GetComponentsInChildren<T>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] != null && candidates[i].gameObject.name == objectName)
                {
                    return candidates[i];
                }
            }

            return null;
        }

        private static void ConfigureAutoHeight(RectTransform rect, float minHeight)
        {
            if (rect == null)
            {
                return;
            }

            LayoutElement layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = Mathf.Max(layout.minHeight, minHeight);
            layout.preferredHeight = -1f;
            layout.flexibleHeight = 0f;

            ContentSizeFitter fitter = rect.GetComponent<ContentSizeFitter>() ?? rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private static RectTransform CreateSectionPanel(string name, Transform parent, string header, out TMP_Text bodyText, float minHeight)
        {
            RectTransform panel = CreatePanel(name, parent);
            ConfigureAutoHeight(panel, minHeight);
            VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            TMP_Text heading = CreateText($"{name}_Heading", panel, header, 15f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(heading, LandLedgersTypography.TextRole.SectionHeader);
            AddLayout(heading.rectTransform, -1f, 22f, 1f, 0f);

            bodyText = CreateText($"{name}_Body", panel, string.Empty, 14f, FontStyles.Normal);
            LandLedgersTypography.ApplyRole(bodyText, LandLedgersTypography.TextRole.DenseBody);
            bodyText.lineSpacing = 2f;
            ConfigureAutoHeight(bodyText.rectTransform, 64f);
            return panel;
        }

        private static RectTransform CreateActionRow(string name, Transform parent)
        {
            RectTransform row = CreateRect(name, parent);
            HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            AddLayout(row, -1f, 38f, 1f, 0f);
            return row;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.transform as RectTransform;
        }

        private static RectTransform CreatePanel(string name, Transform parent)
        {
            RectTransform rect = CreateRect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.07f, 0.075f, 0.07f, 0.94f);
            image.raycastTarget = false;
            return rect;
        }

        private static TMP_Text CreateText(string name, Transform parent, string value, float size, FontStyles style)
        {
            TMP_Text text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            text.transform.SetParent(parent, false);
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = new Color(0.93f, 0.94f, 0.9f, 1f);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, string label, float width)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = new Color(0.12f, 0.13f, 0.12f, 1f);
            Button button = go.GetComponent<Button>();
            AddLayout(go.transform as RectTransform, width, 36f, 0f, 0f);

            TMP_Text text = CreateText("Label", go.transform, label, 13f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(text, LandLedgersTypography.TextRole.ButtonLabel);
            text.alignment = TextAlignmentOptions.Center;
            RectTransform textRect = text.transform as RectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return button;
        }

        private static void AddLayout(RectTransform rect, float preferredWidth, float preferredHeight, float flexibleWidth, float flexibleHeight)
        {
            LayoutElement layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = preferredWidth;
            layout.preferredHeight = preferredHeight;
            layout.flexibleWidth = flexibleWidth;
            layout.flexibleHeight = flexibleHeight;
        }

        private static void SetButtonVisible(Button button, bool visible)
        {
            if (button != null)
            {
                button.gameObject.SetActive(visible);
            }
        }
    }
}
