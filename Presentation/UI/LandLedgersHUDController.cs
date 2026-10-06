using System.Collections;
using System.Globalization;
using LandLedgers.Time;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LandLedgers.UI
{
    [DisallowMultipleComponent]
    public sealed class LandLedgersHUDController : MonoBehaviour
    {
        private const float TopBarHeight = 72f;
        private const float AlertStripHeight = 52f;
        private const float FirstSessionGuidanceTopGap = 14f;
        private static readonly Vector2 AlertStripAnchoredPosition = new(0f, -TopBarHeight);
        private static readonly Vector2 AlertStripSize = new(0f, AlertStripHeight);
        private static readonly Vector2 FirstSessionGuidanceAnchoredPosition = new(20f, -(TopBarHeight + AlertStripHeight + FirstSessionGuidanceTopGap));
        private static readonly Vector2 FirstSessionGuidanceSize = new(620f, 164f);
        private static readonly Vector2 SystemStatusAnchoredPosition = new(-20f, -(TopBarHeight + AlertStripHeight + FirstSessionGuidanceTopGap));
        private static readonly Vector2 SystemStatusSize = new(560f, 96f);

        [Header("Time Source")]
        [SerializeField]
        private TimeManager timeManager;

        [Header("Core Fields")]
        [SerializeField]
        private TMP_Text cashText;

        [SerializeField]
        private TMP_Text dateText;

        [SerializeField]
        private Button pauseButton;

        [SerializeField]
        private Button speed1xButton;

        [SerializeField]
        private Button speed2xButton;

        [SerializeField]
        private Button speed4xButton;

        [SerializeField]
        private Button speed6xButton;

        [SerializeField]
        private Button speed10xButton;

        [SerializeField]
        private Button speed25xButton;

        [SerializeField]
        private Button speed50xButton;

        [SerializeField]
        private Button speed100xButton;

        [SerializeField]
        private RectTransform alertStrip;

        [SerializeField]
        private TMP_Text townPulseAlertText;

        [Header("Control Hints")]
        [SerializeField]
        private RectTransform controlHintRoot;

        [SerializeField]
        private TMP_Text controlHintText;

        [SerializeField]
        private string controlHintMessage = "Manage C | Pause Space | Move WASD | Rotate RMB/QE | Zoom Wheel";

        [SerializeField]
        private Color controlHintBackgroundColor = new(0.08f, 0.09f, 0.08f, 0.82f);

        [SerializeField]
        private Color controlHintTextColor = new(0.95f, 0.91f, 0.78f, 1f);

        [Header("First Session Guidance")]
        [SerializeField]
        private RectTransform firstSessionGuidanceStrip;

        [SerializeField]
        private TMP_Text firstSessionObjectiveText;

        [SerializeField]
        private TMP_Text firstSessionWhyText;

        [SerializeField]
        private TMP_Text firstSessionActionText;

        [SerializeField]
        private TMP_Text firstSessionBlockerText;

        [Header("System Operation Status")]
        [SerializeField]
        private RectTransform systemStatusStrip;

        [SerializeField]
        private TMP_Text systemStatusTitleText;

        [SerializeField]
        private TMP_Text systemStatusMessageText;

        [SerializeField]
        private Color systemStatusNormalBackgroundColor = new(0.09f, 0.1f, 0.09f, 0.92f);

        [SerializeField]
        private Color systemStatusWarningBackgroundColor = new(0.22f, 0.14f, 0.08f, 0.94f);

        [SerializeField]
        private Color systemStatusTitleColor = new(0.98f, 0.95f, 0.84f, 1f);

        [SerializeField]
        private Color systemStatusMessageColor = new(0.86f, 0.86f, 0.8f, 1f);

        [Header("Playback Hotkeys")]
        [SerializeField]
        private Key pauseToggleKey = Key.Space;

        [Header("Hidden Testing Speeds")]
        [SerializeField]
        [Tooltip("Enable keyboard-only fast-forward testing shortcuts.")]
        private bool enableTestingSpeedShortcuts = true;

        [SerializeField]
        private Key speed10xKey = Key.Digit0;

        [SerializeField]
        private Key speed25xKey = Key.Minus;

        [SerializeField]
        private Key speed50xKey = Key.Equals;

        [Header("Wealth Diagnostics")]
        [SerializeField]
        [Tooltip("Shows portfolio component audit text inside the wealth tooltip for developer validation. Keep disabled for the normal player HUD.")]
        private bool includeWealthDiagnosticsInTooltip;

        [Header("Placeholder State")]
        [SerializeField]
        private long placeholderCashDollars = 12500;

        [SerializeField]
        private string fallbackDateText = "No time source";

        [Header("Speed Button Visuals")]
        [SerializeField]
        private Color inactiveButtonColor = new(0.16f, 0.18f, 0.17f, 0.92f);

        [SerializeField]
        private Color inactiveButtonTextColor = new(0.82f, 0.84f, 0.78f, 1f);

        [SerializeField]
        private Color activeButtonColor = new(0.68f, 0.52f, 0.27f, 1f);

        [SerializeField]
        private Color activeButtonTextColor = new(1f, 0.96f, 0.84f, 1f);

        private bool subscribed;
        private long currentCashDollars;
        private long currentNetWorthDollars;
        private long currentLiquidCashDollars;
        private bool wealthMode;
        private string wealthDiagnosticText = string.Empty;
        private string townPulseAlertMessage = string.Empty;
        private string opportunityNoticeAlertMessage = string.Empty;
        private Coroutine systemStatusHideRoutine;
        private int systemStatusRevision;
        private bool createdFallbackCashText;
        private bool createdFallbackDateText;

        public RectTransform AlertStrip => alertStrip;
        public string CurrentTownPulseAlertText => townPulseAlertMessage ?? string.Empty;
        public string CurrentOpportunityNoticeAlertText => opportunityNoticeAlertMessage ?? string.Empty;
        public string CurrentCombinedAlertText => BuildCombinedAlertText();
        public bool IsTownPulseAlertVisible => !string.IsNullOrWhiteSpace(townPulseAlertMessage)
            && townPulseAlertText != null
            && townPulseAlertText.gameObject.activeInHierarchy;
        public bool IsOpportunityNoticeAlertVisible => !string.IsNullOrWhiteSpace(opportunityNoticeAlertMessage)
            && EnsureTownPulseAlertText() != null
            && EnsureTownPulseAlertText().gameObject.activeInHierarchy;
        public bool IsFirstSessionGuidanceVisible => firstSessionGuidanceStrip != null
            && firstSessionGuidanceStrip.gameObject.activeInHierarchy;
        public string CurrentFirstSessionObjectiveText => firstSessionObjectiveText != null ? firstSessionObjectiveText.text : string.Empty;
        public string CurrentFirstSessionWhyText => firstSessionWhyText != null ? firstSessionWhyText.text : string.Empty;
        public string CurrentFirstSessionActionText => firstSessionActionText != null ? firstSessionActionText.text : string.Empty;
        public string CurrentFirstSessionBlockerText => firstSessionBlockerText != null ? firstSessionBlockerText.text : string.Empty;
        public string CurrentMoneyReadoutText => cashText != null ? cashText.text : string.Empty;
        public string CurrentWealthTooltipText => BuildWealthTooltipBody();
        public string CurrentWealthDiagnosticText => wealthDiagnosticText ?? string.Empty;
        public bool UsingFallbackCashField => createdFallbackCashText;
        public bool UsingFallbackDateField => createdFallbackDateText;
        public bool IsSystemStatusVisible => systemStatusStrip != null && systemStatusStrip.gameObject.activeInHierarchy;
        public string CurrentSystemStatusTitleText => systemStatusTitleText != null ? systemStatusTitleText.text : string.Empty;
        public string CurrentSystemStatusMessageText => systemStatusMessageText != null ? systemStatusMessageText.text : string.Empty;
        public Vector2 CurrentFirstSessionGuidanceAnchoredPosition => firstSessionGuidanceStrip != null
            ? firstSessionGuidanceStrip.anchoredPosition
            : Vector2.zero;
        public Vector2 CurrentFirstSessionGuidanceSizeDelta => firstSessionGuidanceStrip != null
            ? firstSessionGuidanceStrip.sizeDelta
            : Vector2.zero;

        private void Reset()
        {
            AutoWireFields();
        }

        private void OnValidate()
        {
            AutoWireFields();
            ApplyTypographyRoles();

            if (!Application.isPlaying)
            {
                currentCashDollars = placeholderCashDollars;
                if (cashText != null)
                {
                    RefreshCash();
                }
            }
        }

        private void Awake()
        {
            AutoWireFields();
            EnsureCoreHudFields();
            ApplyAlertStripLayout();
            ApplySystemStatusLayout();
            EnsureControlHintText();
            ApplyTypographyRoles();
            NormalizeRaycastTargets();
            ConfigureTooltips();
            currentCashDollars = placeholderCashDollars;
            RefreshCash();
            BindToTimeManager(timeManager != null ? timeManager : TimeManager.Instance);
        }

        private void OnEnable()
        {
            EnsureCoreHudFields();
            AddButtonListeners();
            BindToTimeManager(timeManager != null ? timeManager : TimeManager.Instance);
            ConfigureTooltips();
            ApplyTypographyRoles();
            ApplyAlertStripLayout();
            ApplySystemStatusLayout();
            RefreshAll();
        }

        private void OnDisable()
        {
            RemoveButtonListeners();
            UnsubscribeFromTimeManager();
        }

        private void Update()
        {
            if (timeManager == null)
            {
                BindToTimeManager(TimeManager.Instance);
            }

            HandleTestingSpeedShortcuts();
            HandlePauseToggleShortcut();
        }

        public void SetCashDollars(long cashDollars)
        {
            currentCashDollars = cashDollars;
            currentNetWorthDollars = cashDollars;
            currentLiquidCashDollars = cashDollars;
            wealthDiagnosticText = string.Empty;
            wealthMode = false;
            RefreshCash();
        }

        public void SetWealthCents(int netWorthCents, int liquidCashCents)
        {
            SetWealthCents(netWorthCents, liquidCashCents, null);
        }

        public void SetWealthCents(int netWorthCents, int liquidCashCents, string diagnosticText)
        {
            currentNetWorthDollars = CentsToDisplayDollars(netWorthCents);
            currentLiquidCashDollars = CentsToDisplayDollars(Mathf.Max(0, liquidCashCents));
            currentCashDollars = currentLiquidCashDollars;
            wealthDiagnosticText = SanitizeWealthDiagnosticText(diagnosticText);
            wealthMode = true;
            RefreshCash();
        }

        public void SetTownPulseAlert(string message)
        {
            townPulseAlertMessage = message ?? string.Empty;
            RefreshAlertStrip();
        }

        public void SetOpportunityNoticeAlert(string message)
        {
            opportunityNoticeAlertMessage = message ?? string.Empty;
            RefreshAlertStrip();
        }

        public void SetFirstSessionGuidance(string objective, string why, string action, string blocker = "")
        {
            bool hasGuidance = !string.IsNullOrWhiteSpace(objective)
                || !string.IsNullOrWhiteSpace(why)
                || !string.IsNullOrWhiteSpace(action)
                || !string.IsNullOrWhiteSpace(blocker);
            if (!hasGuidance)
            {
                ClearFirstSessionGuidance();
                return;
            }

            if (EnsureFirstSessionGuidanceStrip() == null)
            {
                return;
            }

            firstSessionGuidanceStrip.gameObject.SetActive(true);
            SetGuidanceText(firstSessionObjectiveText, FormatGuidanceLine(string.Empty, objective));
            SetGuidanceText(firstSessionWhyText, FormatGuidanceLine("Why", why));
            SetGuidanceText(firstSessionActionText, FormatGuidanceLine("Do Now", action));

            if (firstSessionBlockerText != null)
            {
                bool hasBlocker = !string.IsNullOrWhiteSpace(blocker);
                firstSessionBlockerText.gameObject.SetActive(hasBlocker);
                firstSessionBlockerText.text = hasBlocker ? FormatGuidanceLine("Blocked", blocker) : string.Empty;
            }
        }

        public void ClearFirstSessionGuidance()
        {
            AutoWireFields();
            SetGuidanceText(firstSessionObjectiveText, string.Empty);
            SetGuidanceText(firstSessionWhyText, string.Empty);
            SetGuidanceText(firstSessionActionText, string.Empty);

            if (firstSessionBlockerText != null)
            {
                firstSessionBlockerText.text = string.Empty;
                firstSessionBlockerText.gameObject.SetActive(false);
            }

            if (firstSessionGuidanceStrip != null)
            {
                firstSessionGuidanceStrip.gameObject.SetActive(false);
            }
        }

        public void SetSystemOperationStatus(string title, string message, bool warning = false, float durationSeconds = 5.5f)
        {
            RectTransform strip = EnsureSystemStatusStrip();
            if (strip == null)
            {
                return;
            }

            string resolvedTitle = string.IsNullOrWhiteSpace(title) ? (warning ? "Warning" : "System") : title.Trim();
            string resolvedMessage = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();

            ApplySystemStatusVisuals(warning);
            strip.gameObject.SetActive(true);
            SetGuidanceText(systemStatusTitleText, resolvedTitle);
            SetGuidanceText(systemStatusMessageText, resolvedMessage);

            systemStatusRevision++;
            if (systemStatusHideRoutine != null)
            {
                StopCoroutine(systemStatusHideRoutine);
                systemStatusHideRoutine = null;
            }

            if (durationSeconds > 0f && gameObject.activeInHierarchy)
            {
                systemStatusHideRoutine = StartCoroutine(HideSystemStatusAfterDelay(systemStatusRevision, durationSeconds));
            }
        }

        public void ClearSystemOperationStatus()
        {
            systemStatusRevision++;
            if (systemStatusHideRoutine != null)
            {
                StopCoroutine(systemStatusHideRoutine);
                systemStatusHideRoutine = null;
            }

            if (systemStatusTitleText != null)
            {
                systemStatusTitleText.text = string.Empty;
            }

            if (systemStatusMessageText != null)
            {
                systemStatusMessageText.text = string.Empty;
            }

            if (systemStatusStrip != null)
            {
                systemStatusStrip.gameObject.SetActive(false);
            }
        }

        private IEnumerator HideSystemStatusAfterDelay(int revision, float durationSeconds)
        {
            yield return new WaitForSecondsRealtime(durationSeconds);
            if (revision == systemStatusRevision)
            {
                ClearSystemOperationStatus();
            }
        }

        private void RefreshAlertStrip()
        {
            TMP_Text label = EnsureTownPulseAlertText();
            if (label == null)
            {
                return;
            }

            ApplyAlertStripLayout();
            string combinedAlertText = BuildCombinedAlertText();
            if (string.IsNullOrWhiteSpace(combinedAlertText))
            {
                if (label.name == "HUD_AlertStrip_Placeholder")
                {
                    label.text = "No active alerts";
                }
                else
                {
                    label.gameObject.SetActive(false);
                }

                return;
            }

            if (alertStrip != null)
            {
                alertStrip.gameObject.SetActive(true);
            }

            label.gameObject.SetActive(true);
            label.text = combinedAlertText;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        private string BuildCombinedAlertText()
        {
            string pulse = string.IsNullOrWhiteSpace(townPulseAlertMessage)
                ? string.Empty
                : $"Town Pulse: {townPulseAlertMessage.Trim()}";
            string notice = string.IsNullOrWhiteSpace(opportunityNoticeAlertMessage)
                ? string.Empty
                : opportunityNoticeAlertMessage.Trim();

            if (string.IsNullOrWhiteSpace(pulse))
            {
                return notice;
            }

            if (string.IsNullOrWhiteSpace(notice))
            {
                return pulse;
            }

            return pulse + "\n" + notice;
        }

        public void BindToTimeManager(TimeManager manager)
        {
            if (manager == timeManager && subscribed)
            {
                return;
            }

            UnsubscribeFromTimeManager();
            timeManager = manager;

            if (timeManager == null)
            {
                RefreshDate();
                RefreshSpeedState(SimulationSpeed.Paused);
                return;
            }

            timeManager.DayChanged += OnDayChanged;
            timeManager.WeekChanged += OnDateRangeChanged;
            timeManager.MonthChanged += OnDateRangeChanged;
            timeManager.VisualFrameUpdated += OnVisualFrameUpdated;
            timeManager.SpeedChanged += OnSpeedChanged;
            timeManager.PauseStateChanged += OnPauseStateChanged;
            subscribed = true;
            RefreshDate();
            RefreshSpeedState(timeManager.CurrentSpeed);
        }

        private void UnsubscribeFromTimeManager()
        {
            if (!subscribed || timeManager == null)
            {
                subscribed = false;
                return;
            }

            timeManager.DayChanged -= OnDayChanged;
            timeManager.WeekChanged -= OnDateRangeChanged;
            timeManager.MonthChanged -= OnDateRangeChanged;
            timeManager.VisualFrameUpdated -= OnVisualFrameUpdated;
            timeManager.SpeedChanged -= OnSpeedChanged;
            timeManager.PauseStateChanged -= OnPauseStateChanged;
            subscribed = false;
        }

        private void AddButtonListeners()
        {
            pauseButton?.onClick.AddListener(OnPauseButtonClicked);
            speed1xButton?.onClick.AddListener(OnSpeed1xButtonClicked);
            speed2xButton?.onClick.AddListener(OnSpeed2xButtonClicked);
            speed4xButton?.onClick.AddListener(OnSpeed4xButtonClicked);
            speed6xButton?.onClick.AddListener(OnSpeed6xButtonClicked);
            speed10xButton?.onClick.AddListener(OnSpeed10xButtonClicked);
            speed25xButton?.onClick.AddListener(OnSpeed25xButtonClicked);
            speed50xButton?.onClick.AddListener(OnSpeed50xButtonClicked);
            speed100xButton?.onClick.AddListener(OnSpeed100xButtonClicked);
        }

        private void RemoveButtonListeners()
        {
            pauseButton?.onClick.RemoveListener(OnPauseButtonClicked);
            speed1xButton?.onClick.RemoveListener(OnSpeed1xButtonClicked);
            speed2xButton?.onClick.RemoveListener(OnSpeed2xButtonClicked);
            speed4xButton?.onClick.RemoveListener(OnSpeed4xButtonClicked);
            speed6xButton?.onClick.RemoveListener(OnSpeed6xButtonClicked);
            speed10xButton?.onClick.RemoveListener(OnSpeed10xButtonClicked);
            speed25xButton?.onClick.RemoveListener(OnSpeed25xButtonClicked);
            speed50xButton?.onClick.RemoveListener(OnSpeed50xButtonClicked);
            speed100xButton?.onClick.RemoveListener(OnSpeed100xButtonClicked);
        }

        private void ConfigureTooltips()
        {
            AutoWireFields();
            EnsureCoreHudFields();
            UITooltipRegistry.Attach(pauseButton, "Pause", () =>
            {
                bool paused = timeManager == null || timeManager.CurrentSpeed == SimulationSpeed.Paused;
                return paused ? "Resume the town clock." : "Pause the town clock.";
            });

            AttachSpeedTooltip(speed1xButton, "1x Speed", SimulationSpeed.Speed1x, "Run the town clock at normal speed.");
            AttachSpeedTooltip(speed2xButton, "2x Speed", SimulationSpeed.Speed2x, "Run the town clock at a steady fast pace.");
            AttachSpeedTooltip(speed4xButton, "4x Speed", SimulationSpeed.Speed4x, "Fast-forward through short waits.");
            AttachSpeedTooltip(speed6xButton, "6x Speed", SimulationSpeed.Speed6x, "Fast-forward through routine town activity.");
            AttachSpeedTooltip(speed10xButton, "10x Speed", SimulationSpeed.Speed10x, "Fast-forward when you are waiting for sales, staffing, or construction.");
            AttachSpeedTooltip(speed25xButton, "25x Speed", SimulationSpeed.Speed25x, "Fast-forward through longer waits.");
            AttachSpeedTooltip(speed50xButton, "50x Speed", SimulationSpeed.Speed50x, "Very fast testing pace for long waits.");
            AttachSpeedTooltip(speed100xButton, "100x Speed", SimulationSpeed.Speed100x, "Maximum testing pace for long waits.");
            if (cashText != null)
            {
                EnsureCashTooltipRaycastTarget();
                UITooltipRegistry.Attach(cashText, "Wealth Overview", BuildWealthTooltipBody);
            }
        }

        private void AttachSpeedTooltip(Button button, string title, SimulationSpeed speed, string body)
        {
            UITooltipRegistry.Attach(button, title, () =>
            {
                bool active = timeManager != null && timeManager.CurrentSpeed == speed;
                return active ? $"{body} Currently active." : body;
            });
        }

        private void OnPauseButtonClicked()
        {
            timeManager?.TogglePaused();
        }

        private void OnSpeed1xButtonClicked()
        {
            timeManager?.SetSpeed(SimulationSpeed.Speed1x);
        }

        private void OnSpeed2xButtonClicked()
        {
            timeManager?.SetSpeed(SimulationSpeed.Speed2x);
        }

        private void OnSpeed4xButtonClicked()
        {
            timeManager?.SetSpeed(SimulationSpeed.Speed4x);
        }

        private void OnSpeed6xButtonClicked()
        {
            timeManager?.SetSpeed(SimulationSpeed.Speed6x);
        }

        private void OnSpeed10xButtonClicked()
        {
            timeManager?.SetSpeed(SimulationSpeed.Speed10x);
        }

        private void OnSpeed25xButtonClicked()
        {
            timeManager?.SetSpeed(SimulationSpeed.Speed25x);
        }

        private void OnSpeed50xButtonClicked()
        {
            timeManager?.SetSpeed(SimulationSpeed.Speed50x);
        }

        private void OnSpeed100xButtonClicked()
        {
            timeManager?.SetSpeed(SimulationSpeed.Speed100x);
        }

        private void OnDayChanged(SimulationDateChangedContext context)
        {
            RefreshDate();
        }

        private void OnVisualFrameUpdated(SimulationFrameContext context)
        {
            RefreshDate();
        }

        private void OnDateRangeChanged(SimulationDateChangedContext context)
        {
            RefreshDate();
        }

        private void OnSpeedChanged(SimulationSpeedChangedContext context)
        {
            RefreshSpeedState(context.CurrentSpeed);
        }

        private void OnPauseStateChanged(bool paused)
        {
            if (timeManager != null)
            {
                RefreshSpeedState(timeManager.CurrentSpeed);
            }
        }

        private void RefreshAll()
        {
            EnsureCoreHudFields();
            RefreshCash();
            RefreshDate();
            RefreshSpeedState(timeManager != null ? timeManager.CurrentSpeed : SimulationSpeed.Paused);
            RefreshControlHint();
        }

        private string BuildWealthTooltipBody()
        {
            string liquidCash = FormatWholeDollars(currentLiquidCashDollars);
            if (wealthMode)
            {
                string netWorth = FormatWholeDollars(currentNetWorthDollars);
                string body = $"Liquid Cash: {liquidCash}\nSpendable owner cash for immediate decisions.\n\nNet Worth: {netWorth}\nBroader portfolio position: owner cash, business cash, business equity, and owned assets, minus liabilities and active debt.";
                return AppendWealthDiagnosticIfEnabled(body);
            }

            return $"Liquid Cash: {liquidCash}\nSpendable owner cash for immediate decisions.\n\nBusiness operating cash stays inside each business until transferred or distributed.";
        }

        private string AppendWealthDiagnosticIfEnabled(string body)
        {
            if (!includeWealthDiagnosticsInTooltip || string.IsNullOrWhiteSpace(wealthDiagnosticText))
            {
                return body ?? string.Empty;
            }

            return $"{body}\n\nDeveloper audit: {wealthDiagnosticText}";
        }

        private static string SanitizeWealthDiagnosticText(string diagnosticText)
        {
            return string.IsNullOrWhiteSpace(diagnosticText) ? string.Empty : diagnosticText.Trim();
        }

        private static long CentsToDisplayDollars(int cents)
        {
            return cents / 100L;
        }

        private static string FormatWholeDollars(long dollars)
        {
            string sign = dollars < 0 ? "-" : string.Empty;
            ulong absolute = dollars < 0
                ? (ulong)(-(dollars + 1L)) + 1UL
                : (ulong)dollars;
            return sign + "$" + absolute.ToString("N0", CultureInfo.InvariantCulture);
        }

        private void RefreshCash()
        {
            if (cashText == null && Application.isPlaying)
            {
                EnsureCoreHudFields();
            }

            if (cashText == null)
            {
                return;
            }

            cashText.text = wealthMode
                ? "Liquid Cash " + FormatWholeDollars(currentLiquidCashDollars)
                    + " | Net Worth " + FormatWholeDollars(currentNetWorthDollars)
                : "Liquid Cash " + FormatWholeDollars(currentCashDollars);
        }

        private void RefreshDate()
        {
            if (dateText == null && Application.isPlaying)
            {
                EnsureCoreHudFields();
            }

            if (dateText == null)
            {
                return;
            }

            dateText.text = timeManager != null
                ? $"{timeManager.GetReadableDate()} | {timeManager.GetReadableClock()}"
                : fallbackDateText;
        }

        private void RefreshSpeedState(SimulationSpeed speed)
        {
            RefreshPauseButtonLabel(speed);
            ApplyButtonState(pauseButton, speed == SimulationSpeed.Paused);
            ApplyButtonState(speed1xButton, speed == SimulationSpeed.Speed1x);
            ApplyButtonState(speed2xButton, speed == SimulationSpeed.Speed2x);
            ApplyButtonState(speed4xButton, speed == SimulationSpeed.Speed4x);
            ApplyButtonState(speed6xButton, speed == SimulationSpeed.Speed6x);
            ApplyButtonState(speed10xButton, speed == SimulationSpeed.Speed10x);
            ApplyButtonState(speed25xButton, speed == SimulationSpeed.Speed25x);
            ApplyButtonState(speed50xButton, speed == SimulationSpeed.Speed50x);
            ApplyButtonState(speed100xButton, speed == SimulationSpeed.Speed100x);
        }

        private void RefreshPauseButtonLabel(SimulationSpeed speed)
        {
            TMP_Text label = pauseButton != null ? pauseButton.GetComponentInChildren<TMP_Text>(true) : null;
            if (label == null)
            {
                return;
            }

            label.text = speed == SimulationSpeed.Paused ? "Play" : "Pause";
        }

        private void RefreshControlHint()
        {
            TMP_Text label = controlHintText != null || Application.isPlaying ? EnsureControlHintText() : controlHintText;
            if (label == null)
            {
                return;
            }

            label.text = controlHintMessage;
            label.color = controlHintTextColor;
        }

        private void ApplyButtonState(Button button, bool active)
        {
            if (button == null)
            {
                return;
            }

            if (button.targetGraphic is Graphic background)
            {
                background.color = active ? activeButtonColor : inactiveButtonColor;
            }

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                LandLedgersTypography.ApplyRole(label, LandLedgersTypography.TextRole.HudUtility);
                label.color = active ? activeButtonTextColor : inactiveButtonTextColor;
            }
        }

        private void ApplyTypographyRoles()
        {
            LandLedgersTypography.ApplyRole(cashText, LandLedgersTypography.TextRole.FeaturedValue);
            LandLedgersTypography.ApplyRole(dateText, LandLedgersTypography.TextRole.HudLabel);
            LandLedgersTypography.ApplyRole(townPulseAlertText, LandLedgersTypography.TextRole.Badge);
            LandLedgersTypography.ApplyRole(controlHintText, LandLedgersTypography.TextRole.HudUtility);
            LandLedgersTypography.ApplyRole(firstSessionObjectiveText, LandLedgersTypography.TextRole.NoticeTitle);
            LandLedgersTypography.ApplyRole(firstSessionWhyText, LandLedgersTypography.TextRole.HelperText);
            LandLedgersTypography.ApplyRole(firstSessionActionText, LandLedgersTypography.TextRole.ImportantLabel);
            LandLedgersTypography.ApplyRole(firstSessionBlockerText, LandLedgersTypography.TextRole.Badge);
            LandLedgersTypography.ApplyRole(systemStatusTitleText, LandLedgersTypography.TextRole.NoticeTitle);
            LandLedgersTypography.ApplyRole(systemStatusMessageText, LandLedgersTypography.TextRole.HelperText);
            ApplySpeedButtonTypography(pauseButton);
            ApplySpeedButtonTypography(speed1xButton);
            ApplySpeedButtonTypography(speed2xButton);
            ApplySpeedButtonTypography(speed4xButton);
            ApplySpeedButtonTypography(speed6xButton);
            ApplySpeedButtonTypography(speed10xButton);
            ApplySpeedButtonTypography(speed25xButton);
            ApplySpeedButtonTypography(speed50xButton);
            ApplySpeedButtonTypography(speed100xButton);
        }

        private static void ApplySpeedButtonTypography(Button button)
        {
            if (button == null)
            {
                return;
            }

            LandLedgersTypography.ApplyRole(button.GetComponentInChildren<TMP_Text>(true), LandLedgersTypography.TextRole.HudUtility);
        }

        private void EnsureCoreHudFields()
        {
            // The authored HUD is preferred, but save/load and guidance readability depend on
            // these two fields existing. Create minimal runtime fallbacks rather than letting the top bar go blank.
            AutoWireFields();
            if (!Application.isPlaying && (cashText == null || dateText == null))
            {
                return;
            }
            RectTransform parent = FindChildComponent<RectTransform>("HUD_Root");
            parent ??= transform as RectTransform;
            if (parent == null)
            {
                return;
            }

            if (cashText == null)
            {
                cashText = CreateCoreHudText(parent, "HUD_CashText", TextAlignmentOptions.TopLeft, 18.5f, true);
                createdFallbackCashText = cashText != null;
            }

            if (dateText == null)
            {
                dateText = CreateCoreHudText(parent, "HUD_DateText", TextAlignmentOptions.TopRight, 15.5f, false);
                createdFallbackDateText = dateText != null;
            }

            ApplyCoreHudFieldLayout();
        }

        private void ApplyCoreHudFieldLayout()
        {
            if (createdFallbackCashText && cashText != null)
            {
                RectTransform rect = cashText.rectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(18f, -16f);
                rect.sizeDelta = new Vector2(640f, 28f);
                cashText.overflowMode = TextOverflowModes.Ellipsis;
                cashText.textWrappingMode = TextWrappingModes.NoWrap;
                EnsureCashTooltipRaycastTarget();
            }

            if (createdFallbackDateText && dateText != null)
            {
                RectTransform rect = dateText.rectTransform;
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(-18f, -18f);
                rect.sizeDelta = new Vector2(480f, 24f);
                dateText.overflowMode = TextOverflowModes.Ellipsis;
                dateText.textWrappingMode = TextWrappingModes.NoWrap;
                dateText.raycastTarget = false;
            }
        }

        private TMP_Text CreateCoreHudText(RectTransform parent, string objectName, TextAlignmentOptions alignment, float fontSize, bool featuredValue)
        {
            if (parent == null)
            {
                return null;
            }

            GameObject textObject = new(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);

            TMP_Text referenceStyle = cashText != null ? cashText : dateText;
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            if (referenceStyle != null)
            {
                text.font = referenceStyle.font;
                text.fontSharedMaterial = referenceStyle.fontSharedMaterial;
            }
            else if (TMP_Settings.defaultFontAsset != null)
            {
                text.font = TMP_Settings.defaultFontAsset;
            }

            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = featuredValue ? systemStatusTitleColor : inactiveButtonTextColor;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            LandLedgersTypography.ApplyRole(text, featuredValue ? LandLedgersTypography.TextRole.FeaturedValue : LandLedgersTypography.TextRole.HudLabel);
            return text;
        }

        private void AutoWireFields()
        {
            cashText ??= FindChildComponent<TMP_Text>("HUD_CashText");
            dateText ??= FindChildComponent<TMP_Text>("HUD_DateText");
            pauseButton ??= FindChildComponent<Button>("HUD_TimeSpeed_Pause");
            speed1xButton ??= FindChildComponent<Button>("HUD_TimeSpeed_1x");
            speed2xButton ??= FindChildComponent<Button>("HUD_TimeSpeed_2x");
            speed4xButton ??= FindChildComponent<Button>("HUD_TimeSpeed_4x");
            speed6xButton ??= FindChildComponent<Button>("HUD_TimeSpeed_6x");
            speed10xButton ??= FindChildComponent<Button>("HUD_TimeSpeed_10x");
            speed25xButton ??= FindChildComponent<Button>("HUD_TimeSpeed_25x");
            speed50xButton ??= FindChildComponent<Button>("HUD_TimeSpeed_50x");
            speed100xButton ??= FindChildComponent<Button>("HUD_TimeSpeed_100x");
            alertStrip ??= FindChildComponent<RectTransform>("HUD_AlertStrip");
            townPulseAlertText ??= FindChildComponent<TMP_Text>("HUD_TownPulseAlertText");
            townPulseAlertText ??= FindChildComponent<TMP_Text>("HUD_AlertStrip_Placeholder");
            controlHintRoot ??= FindChildComponent<RectTransform>("HUD_ControlHintRoot");
            controlHintText ??= FindChildComponent<TMP_Text>("HUD_ControlHintText");
            firstSessionGuidanceStrip ??= FindChildComponent<RectTransform>("HUD_FirstSessionObjectiveStrip");
            firstSessionObjectiveText ??= FindChildComponent<TMP_Text>("HUD_FirstSessionObjectiveText");
            firstSessionWhyText ??= FindChildComponent<TMP_Text>("HUD_FirstSessionWhyText");
            firstSessionActionText ??= FindChildComponent<TMP_Text>("HUD_FirstSessionActionText");
            firstSessionBlockerText ??= FindChildComponent<TMP_Text>("HUD_FirstSessionBlockerText");
            systemStatusStrip ??= FindChildComponent<RectTransform>("HUD_SystemStatusStrip");
            systemStatusTitleText ??= FindChildComponent<TMP_Text>("HUD_SystemStatusTitleText");
            systemStatusMessageText ??= FindChildComponent<TMP_Text>("HUD_SystemStatusMessageText");
        }

        private void ApplyAlertStripLayout()
        {
            if (alertStrip == null)
            {
                return;
            }

            alertStrip.anchorMin = new Vector2(0f, 1f);
            alertStrip.anchorMax = new Vector2(1f, 1f);
            alertStrip.pivot = new Vector2(0.5f, 1f);
            alertStrip.anchoredPosition = AlertStripAnchoredPosition;
            alertStrip.sizeDelta = AlertStripSize;
        }

        private void NormalizeRaycastTargets()
        {
            TMP_Text[] labels = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i].raycastTarget = false;
            }

            Image[] images = GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image.GetComponent<Button>() != null)
                {
                    image.raycastTarget = true;
                    continue;
                }

                if (image.color.a <= 0.01f || image.transform.name == "HUD_Root")
                {
                    image.raycastTarget = false;
                }
            }

            EnsureCashTooltipRaycastTarget();
        }

        private void EnsureCashTooltipRaycastTarget()
        {
            // Most HUD text ignores raycasts so it does not block scene interaction. The wealth readout is the
            // deliberate exception because its tooltip explains the liquid-cash versus net-worth split.
            if (cashText != null)
            {
                cashText.raycastTarget = true;
            }
        }

        private void HandleTestingSpeedShortcuts()
        {
            Keyboard keyboard = Keyboard.current;
            if (!enableTestingSpeedShortcuts || timeManager == null || keyboard == null)
            {
                return;
            }

            if (WasKeyPressed(keyboard, speed10xKey))
            {
                timeManager.SetSpeed(SimulationSpeed.Speed10x);
            }
            else if (WasKeyPressed(keyboard, speed25xKey))
            {
                timeManager.SetSpeed(SimulationSpeed.Speed25x);
            }
            else if (WasKeyPressed(keyboard, speed50xKey))
            {
                timeManager.SetSpeed(SimulationSpeed.Speed50x);
            }
        }

        private void HandlePauseToggleShortcut()
        {
            Keyboard keyboard = Keyboard.current;
            if (timeManager == null || keyboard == null || !WasKeyPressed(keyboard, pauseToggleKey))
            {
                return;
            }

            timeManager.TogglePaused();
        }

        private static bool WasKeyPressed(Keyboard keyboard, Key key)
        {
            return keyboard != null && key != Key.None && keyboard[key].wasPressedThisFrame;
        }

        private T FindChildComponent<T>(string childName) where T : Component
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (child.name == childName && child.TryGetComponent(out T component))
                {
                    return component;
                }
            }

            return null;
        }

        private TMP_Text EnsureControlHintText()
        {
            AutoWireFields();
            if (controlHintText != null)
            {
                return controlHintText;
            }

            RectTransform parent = FindChildComponent<RectTransform>("HUD_Root");
            parent ??= transform as RectTransform;
            if (parent == null)
            {
                return null;
            }

            if (controlHintRoot == null)
            {
                GameObject rootObject = new("HUD_ControlHintRoot", typeof(RectTransform), typeof(Image));
                rootObject.transform.SetParent(parent, false);

                controlHintRoot = rootObject.GetComponent<RectTransform>();
                controlHintRoot.anchorMin = new Vector2(0f, 0f);
                controlHintRoot.anchorMax = new Vector2(0f, 0f);
                controlHintRoot.pivot = new Vector2(0f, 0f);
                controlHintRoot.anchoredPosition = new Vector2(18f, 18f);
                controlHintRoot.sizeDelta = new Vector2(620f, 32f);

                Image background = rootObject.GetComponent<Image>();
                background.color = controlHintBackgroundColor;
                background.raycastTarget = false;
            }

            GameObject textObject = new("HUD_ControlHintText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(controlHintRoot, false);

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12f, 0f);
            rect.offsetMax = new Vector2(-12f, 0f);

            controlHintText = textObject.GetComponent<TMP_Text>();
            if (dateText != null)
            {
                controlHintText.font = dateText.font;
                controlHintText.fontSharedMaterial = dateText.fontSharedMaterial;
            }

            controlHintText.text = controlHintMessage;
            controlHintText.fontSize = 14f;
            LandLedgersTypography.ApplyRole(controlHintText, LandLedgersTypography.TextRole.HudUtility);
            controlHintText.alignment = TextAlignmentOptions.MidlineLeft;
            controlHintText.color = controlHintTextColor;
            controlHintText.textWrappingMode = TextWrappingModes.NoWrap;
            controlHintText.overflowMode = TextOverflowModes.Ellipsis;
            controlHintText.raycastTarget = false;
            return controlHintText;
        }

        private RectTransform EnsureFirstSessionGuidanceStrip()
        {
            AutoWireFields();
            if (HasCompleteFirstSessionGuidanceAuthoring())
            {
                NormalizeFirstSessionGuidanceAuthoring();
                return firstSessionGuidanceStrip;
            }

            RectTransform parent = FindChildComponent<RectTransform>("HUD_Root");
            parent ??= transform as RectTransform;
            if (parent == null)
            {
                return null;
            }

            if (firstSessionGuidanceStrip == null)
            {
                GameObject stripObject = new("HUD_FirstSessionObjectiveStrip", typeof(RectTransform), typeof(Image));
                stripObject.transform.SetParent(parent, false);

                firstSessionGuidanceStrip = stripObject.GetComponent<RectTransform>();
                firstSessionGuidanceStrip.anchorMin = new Vector2(0f, 1f);
                firstSessionGuidanceStrip.anchorMax = new Vector2(0f, 1f);
                firstSessionGuidanceStrip.pivot = new Vector2(0f, 1f);

                Image background = stripObject.GetComponent<Image>();
                background.color = new Color(0.08f, 0.09f, 0.08f, 0.94f);
                background.raycastTarget = false;
            }

            RectTransform contentRoot = FindChildComponent<RectTransform>("HUD_FirstSessionObjectiveContent");
            if (contentRoot == null)
            {
                GameObject contentObject = new("HUD_FirstSessionObjectiveContent", typeof(RectTransform));
                contentObject.transform.SetParent(firstSessionGuidanceStrip, false);

                contentRoot = contentObject.GetComponent<RectTransform>();
            }

            ConfigureGuidanceContentRoot(contentRoot);

            VerticalLayoutGroup layout = contentRoot.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                layout = contentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            }

            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            LayoutElement element = firstSessionGuidanceStrip.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = firstSessionGuidanceStrip.gameObject.AddComponent<LayoutElement>();
            }

            ApplyFirstSessionGuidanceLayout();

            firstSessionObjectiveText ??= CreateGuidanceText(contentRoot, "HUD_FirstSessionObjectiveText", 17f, FontStyles.Bold, activeButtonTextColor);
            firstSessionWhyText ??= CreateGuidanceText(contentRoot, "HUD_FirstSessionWhyText", 14.5f, FontStyles.Normal, inactiveButtonTextColor);
            firstSessionActionText ??= CreateGuidanceText(contentRoot, "HUD_FirstSessionActionText", 14.5f, FontStyles.Bold, activeButtonTextColor);
            firstSessionBlockerText ??= CreateGuidanceText(firstSessionGuidanceStrip, "HUD_FirstSessionBlockerText", 13.5f, FontStyles.Normal, new Color(1f, 0.74f, 0.48f, 1f));
            ConfigureGuidanceBlockerRect(firstSessionBlockerText.rectTransform);
            firstSessionBlockerText.gameObject.SetActive(false);
            return firstSessionGuidanceStrip;
        }

        private bool HasCompleteFirstSessionGuidanceAuthoring()
        {
            return firstSessionGuidanceStrip != null
                && firstSessionObjectiveText != null
                && firstSessionWhyText != null
                && firstSessionActionText != null
                && firstSessionBlockerText != null;
        }

        private void NormalizeFirstSessionGuidanceAuthoring()
        {
            RectTransform contentRoot = FindChildComponent<RectTransform>("HUD_FirstSessionObjectiveContent");
            if (contentRoot == null && firstSessionGuidanceStrip != null)
            {
                GameObject contentObject = new("HUD_FirstSessionObjectiveContent", typeof(RectTransform));
                contentObject.transform.SetParent(firstSessionGuidanceStrip, false);
                contentRoot = contentObject.GetComponent<RectTransform>();
            }

            if (contentRoot != null)
            {
                ConfigureGuidanceContentRoot(contentRoot);
                VerticalLayoutGroup layout = contentRoot.GetComponent<VerticalLayoutGroup>() ?? contentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(0, 0, 0, 0);
                layout.spacing = 8f;
                layout.childAlignment = TextAnchor.UpperLeft;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;

                MoveGuidanceTextToContent(firstSessionObjectiveText, contentRoot, 0);
                MoveGuidanceTextToContent(firstSessionWhyText, contentRoot, 1);
                MoveGuidanceTextToContent(firstSessionActionText, contentRoot, 2);
            }

            if (firstSessionBlockerText != null && firstSessionGuidanceStrip != null)
            {
                if (firstSessionBlockerText.transform.parent != firstSessionGuidanceStrip)
                {
                    firstSessionBlockerText.transform.SetParent(firstSessionGuidanceStrip, false);
                }

                ConfigureGuidanceBlockerRect(firstSessionBlockerText.rectTransform);
            }

            HorizontalLayoutGroup horizontal = firstSessionGuidanceStrip != null
                ? firstSessionGuidanceStrip.GetComponent<HorizontalLayoutGroup>()
                : null;
            if (horizontal != null)
            {
                horizontal.enabled = false;
            }
        }

        private static void MoveGuidanceTextToContent(TMP_Text text, RectTransform contentRoot, int siblingIndex)
        {
            if (text == null || contentRoot == null)
            {
                return;
            }

            if (text.transform.parent != contentRoot)
            {
                text.transform.SetParent(contentRoot, false);
            }

            text.transform.SetSiblingIndex(siblingIndex);
        }

        private void ApplyFirstSessionGuidanceLayout()
        {
            if (firstSessionGuidanceStrip == null)
            {
                return;
            }

            firstSessionGuidanceStrip.anchorMin = new Vector2(0f, 1f);
            firstSessionGuidanceStrip.anchorMax = new Vector2(0f, 1f);
            firstSessionGuidanceStrip.pivot = new Vector2(0f, 1f);
            firstSessionGuidanceStrip.anchoredPosition = FirstSessionGuidanceAnchoredPosition;
            firstSessionGuidanceStrip.sizeDelta = FirstSessionGuidanceSize;

            LayoutElement element = firstSessionGuidanceStrip.GetComponent<LayoutElement>();
            if (element != null)
            {
                element.preferredWidth = FirstSessionGuidanceSize.x;
                element.preferredHeight = FirstSessionGuidanceSize.y;
            }
        }

        private static void ConfigureGuidanceContentRoot(RectTransform contentRoot)
        {
            contentRoot.anchorMin = new Vector2(0f, 1f);
            contentRoot.anchorMax = new Vector2(1f, 1f);
            contentRoot.pivot = new Vector2(0.5f, 1f);
            contentRoot.anchoredPosition = Vector2.zero;
            contentRoot.offsetMin = new Vector2(16f, -116f);
            contentRoot.offsetMax = new Vector2(-16f, -12f);
        }

        private static void ConfigureGuidanceBlockerRect(RectTransform blockerRect)
        {
            blockerRect.anchorMin = new Vector2(0f, 0f);
            blockerRect.anchorMax = new Vector2(1f, 0f);
            blockerRect.pivot = new Vector2(0.5f, 0f);
            blockerRect.anchoredPosition = new Vector2(0f, 12f);
            blockerRect.sizeDelta = new Vector2(-32f, 24f);
        }

        private TMP_Text CreateGuidanceText(RectTransform textParent, string objectName, float fontSize, FontStyles style, Color color)
        {
            if (textParent == null)
            {
                return null;
            }

            GameObject textObject = new(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(textParent, false);

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = Vector2.zero;

            TMP_Text text = textObject.GetComponent<TMP_Text>();
            if (dateText != null)
            {
                text.font = dateText.font;
                text.fontSharedMaterial = dateText.fontSharedMaterial;
            }

            text.fontSize = fontSize;
            LandLedgersTypography.ApplyRole(
                text,
                objectName.Contains("Objective", System.StringComparison.Ordinal)
                    ? LandLedgersTypography.TextRole.NoticeTitle
                    : objectName.Contains("Action", System.StringComparison.Ordinal)
                    ? LandLedgersTypography.TextRole.ImportantLabel
                    : objectName.Contains("Blocker", System.StringComparison.Ordinal)
                    ? LandLedgersTypography.TextRole.Badge
                    : LandLedgersTypography.TextRole.HelperText);
            text.color = color;
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;

            LayoutElement layout = textObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            layout.preferredHeight = style == FontStyles.Bold && fontSize >= 15f ? 20f : 18f;
            return text;
        }

        private static string FormatGuidanceLine(string label, string value)
        {
            string trimmed = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(label) ? trimmed : $"{label}: {trimmed}";
        }

        private static void SetGuidanceText(TMP_Text text, string value)
        {
            if (text == null)
            {
                return;
            }

            bool hasText = !string.IsNullOrWhiteSpace(value);
            text.text = hasText ? value.Trim() : string.Empty;
            text.gameObject.SetActive(hasText);
        }


        private RectTransform EnsureSystemStatusStrip()
        {
            AutoWireFields();
            if (systemStatusStrip != null && systemStatusTitleText != null && systemStatusMessageText != null)
            {
                ApplySystemStatusLayout();
                ApplySystemStatusVisuals(false);
                return systemStatusStrip;
            }

            RectTransform parent = FindChildComponent<RectTransform>("HUD_Root");
            parent ??= transform as RectTransform;
            if (parent == null)
            {
                return null;
            }

            if (systemStatusStrip == null)
            {
                GameObject stripObject = new("HUD_SystemStatusStrip", typeof(RectTransform), typeof(Image));
                stripObject.transform.SetParent(parent, false);
                systemStatusStrip = stripObject.GetComponent<RectTransform>();

                Image background = stripObject.GetComponent<Image>();
                background.raycastTarget = false;
            }

            Image stripBackground = systemStatusStrip.GetComponent<Image>();
            if (stripBackground == null)
            {
                stripBackground = systemStatusStrip.gameObject.AddComponent<Image>();
                stripBackground.raycastTarget = false;
            }

            if (systemStatusTitleText == null)
            {
                systemStatusTitleText = CreateSystemStatusText(systemStatusStrip, "HUD_SystemStatusTitleText", 15.5f, FontStyles.Bold, systemStatusTitleColor, TextAlignmentOptions.TopLeft);
            }

            if (systemStatusMessageText == null)
            {
                systemStatusMessageText = CreateSystemStatusText(systemStatusStrip, "HUD_SystemStatusMessageText", 13.5f, FontStyles.Normal, systemStatusMessageColor, TextAlignmentOptions.TopLeft);
            }

            ConfigureSystemStatusTitleRect(systemStatusTitleText != null ? systemStatusTitleText.rectTransform : null);
            ConfigureSystemStatusMessageRect(systemStatusMessageText != null ? systemStatusMessageText.rectTransform : null);
            ApplySystemStatusLayout();
            ApplySystemStatusVisuals(false);
            systemStatusStrip.gameObject.SetActive(false);
            return systemStatusStrip;
        }

        private void ApplySystemStatusLayout()
        {
            if (systemStatusStrip == null)
            {
                return;
            }

            systemStatusStrip.anchorMin = new Vector2(1f, 1f);
            systemStatusStrip.anchorMax = new Vector2(1f, 1f);
            systemStatusStrip.pivot = new Vector2(1f, 1f);
            systemStatusStrip.anchoredPosition = SystemStatusAnchoredPosition;
            systemStatusStrip.sizeDelta = SystemStatusSize;
            ConfigureSystemStatusTitleRect(systemStatusTitleText != null ? systemStatusTitleText.rectTransform : null);
            ConfigureSystemStatusMessageRect(systemStatusMessageText != null ? systemStatusMessageText.rectTransform : null);
        }

        private void ApplySystemStatusVisuals(bool warning)
        {
            if (systemStatusStrip != null && systemStatusStrip.TryGetComponent(out Image background))
            {
                background.color = warning ? systemStatusWarningBackgroundColor : systemStatusNormalBackgroundColor;
                background.raycastTarget = false;
            }

            if (systemStatusTitleText != null)
            {
                systemStatusTitleText.color = systemStatusTitleColor;
            }

            if (systemStatusMessageText != null)
            {
                systemStatusMessageText.color = systemStatusMessageColor;
            }
        }

        private TMP_Text CreateSystemStatusText(RectTransform parent, string objectName, float fontSize, FontStyles style, Color color, TextAlignmentOptions alignment)
        {
            if (parent == null)
            {
                return null;
            }

            GameObject textObject = new(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            if (dateText != null)
            {
                text.font = dateText.font;
                text.fontSharedMaterial = dateText.fontSharedMaterial;
            }

            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            LandLedgersTypography.ApplyRole(text, style == FontStyles.Bold ? LandLedgersTypography.TextRole.NoticeTitle : LandLedgersTypography.TextRole.HelperText);
            return text;
        }

        private static void ConfigureSystemStatusTitleRect(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -10f);
            rect.offsetMin = new Vector2(14f, -28f);
            rect.offsetMax = new Vector2(-14f, -10f);
        }

        private static void ConfigureSystemStatusMessageRect(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(14f, 12f);
            rect.offsetMax = new Vector2(-14f, -34f);
        }

        private TMP_Text EnsureTownPulseAlertText()
        {
            AutoWireFields();
            if (townPulseAlertText != null)
            {
                return townPulseAlertText;
            }

            if (alertStrip == null)
            {
                return null;
            }

            GameObject textObject = new("HUD_TownPulseAlertText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(alertStrip, false);

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            townPulseAlertText = textObject.GetComponent<TMP_Text>();
            townPulseAlertText.fontSize = 16f;
            LandLedgersTypography.ApplyRole(townPulseAlertText, LandLedgersTypography.TextRole.Badge);
            townPulseAlertText.alignment = TextAlignmentOptions.MidlineLeft;
            townPulseAlertText.color = inactiveButtonTextColor;
            townPulseAlertText.raycastTarget = false;
            townPulseAlertText.text = string.Empty;
            townPulseAlertText.gameObject.SetActive(false);
            return townPulseAlertText;
        }
    }
}
