using System.Collections.Generic;
using LandLedgers.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LandLedgers.UI
{
    [DisallowMultipleComponent]
    public sealed class AcquisitionSellerMeetingView : MonoBehaviour
    {
        private static readonly Color BackdropColor = new(0.025f, 0.027f, 0.025f, 0.965f);
        private static readonly Color PanelColor = new(0.07f, 0.075f, 0.07f, 0.94f);
        private static readonly Color RailCompletedColor = new(0.11f, 0.13f, 0.11f, 1f);
        private static readonly Color RailCurrentColor = new(0.23f, 0.19f, 0.11f, 1f);
        private static readonly Color RailUpcomingColor = new(0.10f, 0.11f, 0.10f, 1f);
        private static readonly Color DialogueNormalColor = new(0.12f, 0.13f, 0.12f, 1f);
        private static readonly Color DialogueSelectedColor = new(0.24f, 0.19f, 0.10f, 1f);
        private static readonly Color DialogueBorderNormalColor = new(0.20f, 0.21f, 0.19f, 1f);
        private static readonly Color DialogueBorderSelectedColor = new(0.72f, 0.59f, 0.33f, 1f);
        private const int MaxVisibleResponseOptions = 6;
        private const float ResponseRowHeight = 82f;
        private const float ResponseRowSpacing = 8f;

        [SerializeField] private RectTransform root;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text stageText;
        [SerializeField] private TMP_Text sellerIdentityText;
        [SerializeField] private TMP_Text selectedFileText;
        [SerializeField] private TMP_Text processActionText;
        [SerializeField] private RectTransform processRailRoot;
        [SerializeField] private TMP_Text sellerLineText;
        [SerializeField] private TMP_Text dialoguePromptText;
        [SerializeField] private TMP_Text postureText;
        [SerializeField] private TMP_Text readinessText;
        [SerializeField] private TMP_Text responseReadoutText;
        [SerializeField] private RectTransform responseRoot;
        [SerializeField] private ScrollRect bodyScrollRect;
        [SerializeField] private RectTransform bodyViewport;
        [SerializeField] private RectTransform bodyContent;
        [SerializeField] private Button primaryActionButton;
        [SerializeField] private Button closeButton;

        private readonly List<ResponseButtonVisual> responseButtonVisuals = new();
        private readonly List<Button> responseButtons = new();

        public RectTransform Root => root != null ? root : transform as RectTransform;
        public TMP_Text TitleText => titleText;
        public TMP_Text StageText => stageText;
        public TMP_Text SellerIdentityText => sellerIdentityText;
        public TMP_Text SelectedFileText => selectedFileText;
        public TMP_Text ProcessActionText => processActionText;
        public TMP_Text SellerLineText => sellerLineText;
        public TMP_Text DialoguePromptText => dialoguePromptText;
        public TMP_Text PostureText => postureText;
        public TMP_Text ReadinessText => readinessText;
        public TMP_Text ResponseReadoutText => responseReadoutText;
        public RectTransform ResponseRoot => responseRoot;
        public ScrollRect BodyScrollRect => bodyScrollRect;
        public RectTransform BodyContent => bodyContent;
        public Button PrimaryActionButton => primaryActionButton;
        public Button CloseButton => closeButton;
        public IReadOnlyList<Button> ResponseButtons => responseButtons;
        public bool IsVisible => Root != null && Root.gameObject.activeInHierarchy;

        private sealed class ResponseButtonVisual
        {
            public Button Button;
            public Image Background;
            public Image Border;
            public TMP_Text ShortcutText;
            public TMP_Text TitleText;
            public TMP_Text IntentText;
            public TMP_Text OutcomeText;
        }

        public static AcquisitionSellerMeetingView GetOrCreate(Canvas canvas)
        {
            if (canvas == null)
            {
                return null;
            }

            AcquisitionSellerMeetingView existing = canvas.GetComponentInChildren<AcquisitionSellerMeetingView>(true);
            if (existing != null)
            {
                existing.EnsureRuntimeState();
                return existing;
            }

            GameObject rootObject = new("Acquisition Seller Meeting Overlay", typeof(RectTransform), typeof(Image));
            rootObject.transform.SetParent(canvas.transform, false);
            AcquisitionSellerMeetingView view = rootObject.AddComponent<AcquisitionSellerMeetingView>();
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
        }

        public void Render(AcquisitionConversationState state)
        {
            EnsureRuntimeState();
            if (state == null)
            {
                RenderUnavailable(
                    "Seller Meeting Unavailable",
                    "No live acquisition lead is attached to this meeting.",
                    "Counterparty: not in file | Seller posture: unread | Buyer footing: not declared",
                    "Selected file: none | Return to the workflow and reopen a live lead.",
                    "No formal acquisition step is available. Return to the workflow desk and reopen a live file.",
                    BuildSellerLineText(null),
                    BuildDialoguePromptText(null),
                    BuildPostureText(null),
                    BuildReadinessText(null),
                    BuildDefaultReadoutText(null),
                    "No Action");
                return;
            }

            SetText(titleText, "Formal Seller Meeting");
            SetText(stageText, BuildStageText(state));
            SetText(sellerIdentityText, BuildSellerIdentityText(state));
            SetText(selectedFileText, BuildSelectedFileText(state));
            SetText(processActionText, BuildProcessActionText(state));
            SetText(sellerLineText, BuildSellerLineText(state));
            SetText(dialoguePromptText, BuildDialoguePromptText(state));
            SetText(postureText, BuildPostureText(state));
            SetText(readinessText, BuildReadinessText(state));
            SetText(responseReadoutText, BuildDefaultReadoutText(state));
            SetPrimaryActionLabel(!string.IsNullOrWhiteSpace(state.FormalActionLabel) ? state.FormalActionLabel : "No Action");
            SetPrimaryInteractable(state.CanAdvanceDeal);
            RebuildProcessRail(state);
            RebuildResponseButtons(state);
            SetSelectedResponseIndex(responseButtons.Count > 0 ? 0 : -1);
            ResetBodyScrollPosition();
        }

        public void RenderUnavailable(
            string title,
            string stage,
            string sellerIdentity,
            string selectedFile,
            string processAction,
            string sellerLine,
            string dialoguePrompt,
            string posture,
            string readiness,
            string responseReadout,
            string primaryActionLabel = "No Action")
        {
            EnsureRuntimeState();
            SetText(titleText, string.IsNullOrWhiteSpace(title) ? "Seller Meeting Unavailable" : title);
            SetText(stageText, stage);
            SetText(sellerIdentityText, sellerIdentity);
            SetText(selectedFileText, selectedFile);
            SetText(processActionText, processAction);
            SetText(sellerLineText, sellerLine);
            SetText(dialoguePromptText, dialoguePrompt);
            SetText(postureText, posture);
            SetText(readinessText, readiness);
            SetText(responseReadoutText, responseReadout);
            SetPrimaryActionLabel(primaryActionLabel);
            SetPrimaryInteractable(false);
            RebuildProcessRail(null);
            RebuildResponseButtons(null);
            SetSelectedResponseIndex(-1);
            ResetBodyScrollPosition();
        }

        public void SetPrimaryActionLabel(string label)
        {
            EnsureRuntimeState();
            SetButtonLabel(primaryActionButton, label);
        }

        public void SetPrimaryInteractable(bool interactable)
        {
            EnsureRuntimeState();
            if (primaryActionButton != null)
            {
                primaryActionButton.interactable = interactable;
            }
        }

        public void SetResponseReadout(string value)
        {
            EnsureRuntimeState();
            SetText(responseReadoutText, value);
        }

        public void SetSelectedResponseIndex(int index)
        {
            for (int i = 0; i < responseButtonVisuals.Count; i++)
            {
                bool selected = i == index;
                ResponseButtonVisual visual = responseButtonVisuals[i];
                if (visual.Background != null)
                {
                    visual.Background.color = selected ? DialogueSelectedColor : DialogueNormalColor;
                }

                if (visual.Border != null)
                {
                    visual.Border.color = selected ? DialogueBorderSelectedColor : DialogueBorderNormalColor;
                }
            }
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

            titleText ??= FindNamedComponent<TMP_Text>("SellerMeeting_Title");
            stageText ??= FindNamedComponent<TMP_Text>("SellerMeeting_Stage");
            sellerIdentityText ??= FindNamedComponent<TMP_Text>("SellerMeeting_SellerIdentity");
            selectedFileText ??= FindNamedComponent<TMP_Text>("SellerMeeting_SelectedFile");
            processActionText ??= FindNamedComponent<TMP_Text>("SellerMeeting_ProcessAction");
            processRailRoot ??= FindNamedComponent<RectTransform>("SellerMeeting_ProcessRail");
            sellerLineText ??= FindNamedComponent<TMP_Text>("SellerMeeting_SellerLine");
            dialoguePromptText ??= FindNamedComponent<TMP_Text>("SellerMeeting_DialoguePrompt");
            postureText ??= FindNamedComponent<TMP_Text>("SellerMeeting_Posture");
            readinessText ??= FindNamedComponent<TMP_Text>("SellerMeeting_Readiness");
            responseReadoutText ??= FindNamedComponent<TMP_Text>("SellerMeeting_ResponseReadout");
            responseRoot ??= FindNamedComponent<RectTransform>("SellerMeeting_ResponseRoot");
            bodyScrollRect ??= FindNamedComponent<ScrollRect>("SellerMeeting_ContentPanel");
            bodyViewport ??= FindNamedComponent<RectTransform>("SellerMeeting_ContentViewport");
            bodyContent ??= FindNamedComponent<RectTransform>("SellerMeeting_Content");
            primaryActionButton ??= FindNamedComponent<Button>("SellerMeeting_PrimaryAction");
            closeButton ??= FindNamedComponent<Button>("SellerMeeting_Close");

            if (stageText != null)
            {
                ConfigureAutoHeight(stageText.rectTransform, 22f);
            }

            if (sellerIdentityText != null)
            {
                ConfigureAutoHeight(sellerIdentityText.rectTransform, 22f);
            }

            if (selectedFileText != null)
            {
                ConfigureAutoHeight(selectedFileText.rectTransform, 22f);
            }

            if (processActionText != null)
            {
                ConfigureAutoHeight(processActionText.rectTransform, 30f);
            }

            if (sellerLineText != null)
            {
                ConfigureAutoHeight(sellerLineText.rectTransform, 44f);
            }

            if (postureText != null)
            {
                ConfigureAutoHeight(postureText.rectTransform, 62f);
            }

            if (readinessText != null)
            {
                ConfigureAutoHeight(readinessText.rectTransform, 86f);
            }

            if (dialoguePromptText != null)
            {
                ConfigureAutoHeight(dialoguePromptText.rectTransform, 24f);
            }

            if (responseReadoutText != null)
            {
                ConfigureAutoHeight(responseReadoutText.rectTransform, 88f);
            }
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



        private void ResetBodyScrollPosition()
        {
            if (bodyScrollRect == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            bodyScrollRect.StopMovement();
            bodyScrollRect.horizontalNormalizedPosition = 0f;
            bodyScrollRect.verticalNormalizedPosition = 1f;
        }

        private void Build()
        {
            root = transform as RectTransform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            Image backdrop = GetComponent<Image>();
            backdrop.color = BackdropColor;
            backdrop.raycastTarget = true;

            VerticalLayoutGroup layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(34, 34, 26, 26);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            titleText = CreateText("SellerMeeting_Title", root, "Formal Seller Meeting", 25f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(titleText, LandLedgersTypography.TextRole.ModalTitle);
            AddLayout(titleText.rectTransform, -1f, 34f, 1f, 0f);

            RectTransform headerPanel = CreatePanel("SellerMeeting_HeaderPanel", root);
            AddLayout(headerPanel, -1f, 86f, 1f, 0f);
            ConfigureAutoHeight(headerPanel, 86f);
            VerticalLayoutGroup headerLayout = headerPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            headerLayout.padding = new RectOffset(14, 14, 10, 10);
            headerLayout.spacing = 4f;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = true;
            headerLayout.childForceExpandHeight = false;

            stageText = CreateText("SellerMeeting_Stage", headerPanel, "Stage", 15f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(stageText, LandLedgersTypography.TextRole.SectionHeader);
            stageText.color = new Color(0.82f, 0.86f, 0.8f, 1f);
            ConfigureAutoHeight(stageText.rectTransform, 22f);

            sellerIdentityText = CreateText("SellerMeeting_SellerIdentity", headerPanel, string.Empty, 13.5f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(sellerIdentityText, LandLedgersTypography.TextRole.ImportantLabel);
            sellerIdentityText.color = new Color(0.96f, 0.94f, 0.84f, 1f);
            ConfigureAutoHeight(sellerIdentityText.rectTransform, 22f);

            selectedFileText = CreateText("SellerMeeting_SelectedFile", headerPanel, string.Empty, 13f, FontStyles.Normal);
            LandLedgersTypography.ApplyRole(selectedFileText, LandLedgersTypography.TextRole.HelperText);
            selectedFileText.color = new Color(0.82f, 0.84f, 0.78f, 0.96f);
            ConfigureAutoHeight(selectedFileText.rectTransform, 22f);

            RectTransform processPanel = CreatePanel("SellerMeeting_ProcessPanel", root);
            AddLayout(processPanel, -1f, 154f, 1f, 0f);
            ConfigureAutoHeight(processPanel, 154f);
            VerticalLayoutGroup processLayout = processPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            processLayout.padding = new RectOffset(16, 16, 12, 12);
            processLayout.spacing = 8f;
            processLayout.childControlWidth = true;
            processLayout.childControlHeight = true;
            processLayout.childForceExpandWidth = true;
            processLayout.childForceExpandHeight = false;

            TMP_Text processHeading = CreateText("SellerMeeting_ProcessHeading", processPanel, "Formal Transaction Spine", 15f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(processHeading, LandLedgersTypography.TextRole.SectionHeader);
            AddLayout(processHeading.rectTransform, -1f, 22f, 1f, 0f);

            processActionText = CreateText("SellerMeeting_ProcessAction", processPanel, string.Empty, 16f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(processActionText, LandLedgersTypography.TextRole.ImportantLabel);
            processActionText.color = new Color(0.96f, 0.94f, 0.84f, 1f);
            ConfigureAutoHeight(processActionText.rectTransform, 30f);

            processRailRoot = CreateRect("SellerMeeting_ProcessRail", processPanel);
            HorizontalLayoutGroup processRailLayout = processRailRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            processRailLayout.spacing = 8f;
            processRailLayout.childAlignment = TextAnchor.MiddleLeft;
            processRailLayout.childControlWidth = true;
            processRailLayout.childControlHeight = true;
            processRailLayout.childForceExpandWidth = true;
            processRailLayout.childForceExpandHeight = false;
            AddLayout(processRailRoot, -1f, 72f, 1f, 0f);

            RectTransform body = CreatePanel("SellerMeeting_ContentPanel", root);
            AddLayout(body, -1f, -1f, 1f, 1f);

            bodyViewport = CreateRect("SellerMeeting_ContentViewport", body);
            bodyViewport.anchorMin = Vector2.zero;
            bodyViewport.anchorMax = Vector2.one;
            bodyViewport.offsetMin = Vector2.zero;
            bodyViewport.offsetMax = Vector2.zero;
            Image viewportImage = bodyViewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.002f);
            RectMask2D viewportMask = bodyViewport.gameObject.AddComponent<RectMask2D>();
            viewportMask.padding = Vector4.zero;

            bodyContent = CreateRect("SellerMeeting_Content", bodyViewport);
            bodyContent.anchorMin = new Vector2(0f, 1f);
            bodyContent.anchorMax = new Vector2(1f, 1f);
            bodyContent.pivot = new Vector2(0.5f, 1f);
            bodyContent.anchoredPosition = Vector2.zero;
            bodyContent.sizeDelta = Vector2.zero;

            VerticalLayoutGroup bodyLayout = bodyContent.gameObject.AddComponent<VerticalLayoutGroup>();
            bodyLayout.padding = new RectOffset(14, 14, 14, 14);
            bodyLayout.spacing = 10f;
            bodyLayout.childControlWidth = true;
            bodyLayout.childControlHeight = true;
            bodyLayout.childForceExpandWidth = true;
            bodyLayout.childForceExpandHeight = false;

            ContentSizeFitter bodyFitter = bodyContent.gameObject.AddComponent<ContentSizeFitter>();
            bodyFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            bodyScrollRect = body.gameObject.AddComponent<ScrollRect>();
            bodyScrollRect.viewport = bodyViewport;
            bodyScrollRect.content = bodyContent;
            bodyScrollRect.horizontal = false;
            bodyScrollRect.vertical = true;
            bodyScrollRect.movementType = ScrollRect.MovementType.Clamped;
            bodyScrollRect.scrollSensitivity = 28f;

            sellerLineText = CreateText("SellerMeeting_SellerLine", bodyContent, string.Empty, 15.5f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(sellerLineText, LandLedgersTypography.TextRole.SectionHeader);
            ConfigureAutoHeight(sellerLineText.rectTransform, 44f);

            postureText = CreateText("SellerMeeting_Posture", bodyContent, string.Empty, 14f, FontStyles.Normal);
            LandLedgersTypography.ApplyRole(postureText, LandLedgersTypography.TextRole.DenseBody);
            ConfigureAutoHeight(postureText.rectTransform, 62f);

            readinessText = CreateText("SellerMeeting_Readiness", bodyContent, string.Empty, 14f, FontStyles.Normal);
            LandLedgersTypography.ApplyRole(readinessText, LandLedgersTypography.TextRole.DenseBody);
            ConfigureAutoHeight(readinessText.rectTransform, 86f);

            dialoguePromptText = CreateText("SellerMeeting_DialoguePrompt", bodyContent, "Choose one practical line of inquiry.", 14f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(dialoguePromptText, LandLedgersTypography.TextRole.SectionHeader);
            dialoguePromptText.color = new Color(0.84f, 0.86f, 0.82f, 1f);
            ConfigureAutoHeight(dialoguePromptText.rectTransform, 24f);

            responseRoot = CreateRect("SellerMeeting_ResponseRoot", bodyContent);
            VerticalLayoutGroup responseLayout = responseRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            responseLayout.spacing = 8f;
            responseLayout.childControlWidth = true;
            responseLayout.childControlHeight = true;
            responseLayout.childForceExpandWidth = true;
            responseLayout.childForceExpandHeight = false;
            AddLayout(responseRoot, -1f, 160f, 1f, 0f);

            responseReadoutText = CreateText("SellerMeeting_ResponseReadout", bodyContent, string.Empty, 14f, FontStyles.Normal);
            LandLedgersTypography.ApplyRole(responseReadoutText, LandLedgersTypography.TextRole.LogBody);
            ConfigureAutoHeight(responseReadoutText.rectTransform, 88f);

            RectTransform footer = CreateRect("SellerMeeting_Footer", root);
            HorizontalLayoutGroup footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 8f;
            footerLayout.childAlignment = TextAnchor.MiddleRight;
            footerLayout.childControlWidth = true;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = false;
            footerLayout.childForceExpandHeight = false;
            AddLayout(footer, -1f, 48f, 1f, 0f);

            primaryActionButton = CreateFooterButton("SellerMeeting_PrimaryAction", footer, "Advance", 300f, true);
            closeButton = CreateFooterButton("SellerMeeting_Close", footer, "Back", 104f, false);

            SetVisible(false);
        }

        private void RebuildProcessRail(AcquisitionConversationState state)
        {
            if (processRailRoot == null)
            {
                return;
            }

            ClearChildren(processRailRoot);
            if (state == null || state.ProcessSteps == null)
            {
                return;
            }

            for (int i = 0; i < state.ProcessSteps.Count; i++)
            {
                AcquisitionConversationProcessStep step = state.ProcessSteps[i];
                RectTransform card = CreatePanel($"SellerMeeting_ProcessStep_{i}", processRailRoot);
                AddLayout(card, -1f, 72f, 1f, 0f);

                Image cardImage = card.GetComponent<Image>();
                cardImage.color = ResolveRailColor(step != null ? step.Status : AcquisitionConversationProcessStatus.Upcoming);

                VerticalLayoutGroup layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(10, 10, 8, 8);
                layout.spacing = 2f;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;

                TMP_Text label = CreateText("Label", card, step != null ? step.Label : "Step", 13f, FontStyles.Bold);
                LandLedgersTypography.ApplyRole(label, LandLedgersTypography.TextRole.ButtonLabel);
                AddLayout(label.rectTransform, -1f, 18f, 1f, 0f);

                TMP_Text status = CreateText("Status", card, ResolveRailStatusLabel(step != null ? step.Status : AcquisitionConversationProcessStatus.Upcoming), 12f, FontStyles.Normal);
                LandLedgersTypography.ApplyRole(status, LandLedgersTypography.TextRole.Metadata);
                status.color = new Color(0.90f, 0.90f, 0.86f, 0.92f);
                AddLayout(status.rectTransform, -1f, 18f, 1f, 0f);

                TMP_Text summary = CreateText("Summary", card, step != null ? step.Summary : string.Empty, 11.5f, FontStyles.Normal);
                LandLedgersTypography.ApplyRole(summary, LandLedgersTypography.TextRole.HelperText);
                summary.color = new Color(0.84f, 0.86f, 0.82f, 0.84f);
                AddLayout(summary.rectTransform, -1f, 24f, 1f, 0f);
            }
        }

        private void RebuildResponseButtons(AcquisitionConversationState state)
        {
            responseButtons.Clear();
            responseButtonVisuals.Clear();
            if (responseRoot == null)
            {
                return;
            }

            ClearChildren(responseRoot);
            if (state == null || state.Options == null)
            {
                return;
            }

            int optionCount = Mathf.Min(MaxVisibleResponseOptions, state.Options.Count);
            int rowCount = Mathf.Max(1, (optionCount + 1) / 2);
            float responseHeight = Mathf.Max(160f, rowCount * ResponseRowHeight + Mathf.Max(0, rowCount - 1) * ResponseRowSpacing);
            AddLayout(responseRoot, -1f, responseHeight, 1f, 0f);

            RectTransform row = null;
            for (int i = 0; i < optionCount; i++)
            {
                if (i % 2 == 0)
                {
                    row = CreateResponseRow($"SellerMeeting_ResponseRow_{i / 2}", responseRoot);
                }

                AcquisitionConversationOption option = state.Options[i];
                ResponseButtonVisual visual = CreateDialogueButton($"SellerMeeting_Response_{i}", row != null ? row : responseRoot, option);
                responseButtonVisuals.Add(visual);
                responseButtons.Add(visual.Button);
            }
        }

        private ResponseButtonVisual CreateDialogueButton(string name, Transform parent, AcquisitionConversationOption option)
        {
            GameObject outer = new(name, typeof(RectTransform), typeof(Image), typeof(Button));
            outer.transform.SetParent(parent, false);

            Image border = outer.GetComponent<Image>();
            border.color = DialogueBorderNormalColor;
            Button button = outer.GetComponent<Button>();
            AddLayout(outer.transform as RectTransform, -1f, 82f, 1f, 0f);

            RectTransform inner = CreateRect("Body", outer.transform);
            inner.anchorMin = Vector2.zero;
            inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(1f, 1f);
            inner.offsetMax = new Vector2(-1f, -1f);

            Image background = inner.gameObject.AddComponent<Image>();
            background.color = DialogueNormalColor;

            HorizontalLayoutGroup rootLayout = inner.gameObject.AddComponent<HorizontalLayoutGroup>();
            rootLayout.padding = new RectOffset(10, 10, 8, 8);
            rootLayout.spacing = 10f;
            rootLayout.childAlignment = TextAnchor.MiddleLeft;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = false;
            rootLayout.childForceExpandHeight = false;

            TMP_Text shortcut = CreateText("Shortcut", inner, string.IsNullOrWhiteSpace(option?.ShortcutLabel) ? string.Empty : option.ShortcutLabel, 18f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(shortcut, LandLedgersTypography.TextRole.ImportantLabel);
            shortcut.alignment = TextAlignmentOptions.Center;
            shortcut.color = new Color(0.96f, 0.94f, 0.84f, 1f);
            AddLayout(shortcut.rectTransform, 20f, 40f, 0f, 0f);

            RectTransform copy = CreateRect("Copy", inner);
            VerticalLayoutGroup copyLayout = copy.gameObject.AddComponent<VerticalLayoutGroup>();
            copyLayout.spacing = 2f;
            copyLayout.childControlWidth = true;
            copyLayout.childControlHeight = true;
            copyLayout.childForceExpandWidth = true;
            copyLayout.childForceExpandHeight = false;
            AddLayout(copy, -1f, 60f, 1f, 0f);

            TMP_Text title = CreateText("Title", copy, option != null ? option.Label : "Review", 13f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(title, LandLedgersTypography.TextRole.ButtonLabel);
            AddLayout(title.rectTransform, -1f, 18f, 1f, 0f);

            TMP_Text intent = CreateText("Intent", copy, option != null ? option.IntentText : string.Empty, 12f, FontStyles.Normal);
            LandLedgersTypography.ApplyRole(intent, LandLedgersTypography.TextRole.HelperText);
            intent.color = new Color(0.84f, 0.86f, 0.82f, 0.9f);
            AddLayout(intent.rectTransform, -1f, 18f, 1f, 0f);

            TMP_Text outcome = CreateText("Outcome", copy, option != null ? option.OutcomeTag : string.Empty, 12f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(outcome, LandLedgersTypography.TextRole.Metadata);
            outcome.color = new Color(0.94f, 0.88f, 0.68f, 0.95f);
            AddLayout(outcome.rectTransform, -1f, 16f, 1f, 0f);

            return new ResponseButtonVisual
            {
                Button = button,
                Background = background,
                Border = border,
                ShortcutText = shortcut,
                TitleText = title,
                IntentText = intent,
                OutcomeText = outcome
            };
        }

        private static RectTransform CreateResponseRow(string name, Transform parent)
        {
            RectTransform row = CreateRect(name, parent);
            HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            AddLayout(row, -1f, 82f, 1f, 0f);
            return row;
        }

        private static Button CreateFooterButton(string name, Transform parent, string label, float width, bool primary)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = primary ? DialogueSelectedColor : DialogueNormalColor;
            Button button = go.GetComponent<Button>();
            AddLayout(go.transform as RectTransform, width, primary ? 42f : 36f, 0f, 0f);

            TMP_Text text = CreateText("Label", go.transform, label, primary ? 14f : 13f, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(text, LandLedgersTypography.TextRole.ButtonLabel);
            text.alignment = TextAlignmentOptions.Center;
            RectTransform textRect = text.transform as RectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return button;
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
            image.color = PanelColor;
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

        private static void AddLayout(RectTransform rect, float preferredWidth, float preferredHeight, float flexibleWidth, float flexibleHeight)
        {
            LayoutElement layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = preferredWidth;
            layout.preferredHeight = preferredHeight;
            layout.flexibleWidth = flexibleWidth;
            layout.flexibleHeight = flexibleHeight;
        }


        private static void ConfigureAutoHeight(RectTransform rect, float minHeight)
        {
            LayoutElement layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = Mathf.Max(layout.minHeight, minHeight);
            layout.preferredHeight = -1f;
            layout.flexibleHeight = 0f;

            ContentSizeFitter fitter = rect.GetComponent<ContentSizeFitter>() ?? rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }

        private static void SetButtonLabel(Button button, string label)
        {
            TMP_Text text = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (text != null)
            {
                text.text = label ?? string.Empty;
            }
        }

        private static void ClearChildren(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        private static Color ResolveRailColor(AcquisitionConversationProcessStatus status)
        {
            return status switch
            {
                AcquisitionConversationProcessStatus.Completed => RailCompletedColor,
                AcquisitionConversationProcessStatus.Current => RailCurrentColor,
                _ => RailUpcomingColor
            };
        }

        private static string ResolveRailStatusLabel(AcquisitionConversationProcessStatus status)
        {
            return status switch
            {
                AcquisitionConversationProcessStatus.Completed => "Completed",
                AcquisitionConversationProcessStatus.Current => "Current",
                _ => "Upcoming"
            };
        }

        private static string BuildSellerLineText(AcquisitionConversationState state)
        {
            if (state == null)
            {
                return "No seller is present because this file is not currently active.";
            }

            return string.IsNullOrWhiteSpace(state.SellerLine)
                ? "The seller has not offered a first line yet. Read the file and choose a practical opening."
                : state.SellerLine;
        }

        private static string BuildDialoguePromptText(AcquisitionConversationState state)
        {
            if (state == null)
            {
                return "No response lines are available.";
            }

            return string.IsNullOrWhiteSpace(state.DialoguePrompt)
                ? "Choose one practical line of inquiry before you advance the file."
                : state.DialoguePrompt;
        }

        private static string BuildPostureText(AcquisitionConversationState state)
        {
            if (state == null)
            {
                return "The meeting cannot proceed until a live acquisition lead is reopened from the workflow or acquisitions desk.";
            }

            string quality = string.IsNullOrWhiteSpace(state.LeadQualitySummary)
                ? "Lead quality remains only partially read."
                : state.LeadQualitySummary;
            string posture = string.IsNullOrWhiteSpace(state.PostureSummary)
                ? "Seller posture is still being read through the meeting."
                : state.PostureSummary;
            return $"{quality}\n{posture}".Trim();
        }

        private static string BuildReadinessText(AcquisitionConversationState state)
        {
            if (state == null)
            {
                return "Use Back to return to the formal workflow. If the lead expired or moved, reopen a live file before continuing.";
            }

            System.Collections.Generic.List<string> lines = new();
            if (!string.IsNullOrWhiteSpace(state.ProofOfFundsSummary))
            {
                lines.Add(state.ProofOfFundsSummary.Trim());
            }

            if (!string.IsNullOrWhiteSpace(state.ReadinessSummary))
            {
                lines.Add(state.ReadinessSummary.Trim());
            }

            if (!string.IsNullOrWhiteSpace(state.DiligenceSummary))
            {
                lines.Add(state.DiligenceSummary.Trim());
            }

            if (!string.IsNullOrWhiteSpace(state.ClosingRiskSummary))
            {
                lines.Add(state.ClosingRiskSummary.Trim());
            }

            if (lines.Count <= 0)
            {
                lines.Add("Readiness details are still thin. Confirm money, diligence coverage, and closing risk before you advance the file.");
            }

            return string.Join("\n", lines);
        }

        private static string BuildDefaultReadoutText(AcquisitionConversationState state)
        {
            if (state == null)
            {
                return "Return to the formal workflow and reopen a live acquisition lead before continuing.";
            }

            if (!string.IsNullOrWhiteSpace(state.FollowUpSummary))
            {
                return state.FollowUpSummary;
            }

            if (state.Options != null && state.Options.Count > 0)
            {
                return "Select a line of inquiry to preview the buyer tone, seller answer, and likely effect before you advance the file.";
            }

            return "No live response lines are on record for this stage. Advance only when the file, timing, and seller posture are clear.";
        }

        private static string BuildProcessActionText(AcquisitionConversationState state)
        {
            if (state == null)
            {
                return string.Empty;
            }

            string action = string.IsNullOrWhiteSpace(state.FormalActionLabel) ? "Review Lead" : state.FormalActionLabel;
            string process = string.IsNullOrWhiteSpace(state.ProcessSummary) ? "Confirm the money, timing, and seller posture before acting." : state.ProcessSummary;
            return $"{action}: {process}";
        }

        private static string BuildStageText(AcquisitionConversationState state)
        {
            if (state == null)
            {
                return string.Empty;
            }

            string stage = string.IsNullOrWhiteSpace(state.StageLabel) ? "Not contacted" : state.StageLabel;
            string next = string.IsNullOrWhiteSpace(state.NextStepSummary) ? "review the file" : state.NextStepSummary;
            return $"Stage: {stage}\nNext move: {next}";
        }

        private static string BuildSellerIdentityText(AcquisitionConversationState state)
        {
            if (state == null)
            {
                return string.Empty;
            }

            string seller = string.IsNullOrWhiteSpace(state.SellerDisplayName) ? "Seller" : state.SellerDisplayName;
            string posture = state.SellerPosture == AcquisitionSellerPosture.Unknown ? "unread" : state.SellerPosture.ToString().ToLowerInvariant();
            string buyerFooting = string.IsNullOrWhiteSpace(state.BuyerSeriousnessLabel) ? "not declared" : state.BuyerSeriousnessLabel;
            return $"Counterparty: {seller} | Seller posture: {posture} | Buyer footing: {buyerFooting}";
        }

        private static string BuildSelectedFileText(AcquisitionConversationState state)
        {
            if (state == null)
            {
                return string.Empty;
            }

            string kind = state.Kind == AcquisitionListingKind.Business ? "Business file" : "Land file";
            string title = string.IsNullOrWhiteSpace(state.Title) ? "Selected lead" : state.Title;
            string access = string.IsNullOrWhiteSpace(state.LeadAccessSummary) ? "Access not yet read." : state.LeadAccessSummary;
            return $"{kind}: {title} | Access: {access}";
        }
    }
}
