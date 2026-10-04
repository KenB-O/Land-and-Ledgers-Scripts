using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Civic;
using LandLedgers.FirstLedger;
using LandLedgers.Population;
using LandLedgers.Time;
using LandLedgers.UI;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy
{
    public enum OpportunityPressureType
    {
        Retail = 0,
        Housing = 1,
        Labor = 2,
        Service = 3,
        Expansion = 4
    }

    public enum OpportunityNoticeSeverity
    {
        Watch = 0,
        Pressure = 1,
        Urgent = 2
    }

    [Serializable]
    public sealed class OpportunityNoticeMemory
    {
        [SerializeField]
        private string noticeId = string.Empty;

        [SerializeField, Range(0f, 1f)]
        private float lastScore01;

        [SerializeField]
        private int consecutiveCycles = 1;

        [SerializeField]
        private int lastSeenRefreshIndex = -1;

        public string NoticeId
        {
            get => noticeId ?? string.Empty;
            set => noticeId = value ?? string.Empty;
        }

        public float LastScore01
        {
            get => Mathf.Clamp01(lastScore01);
            set => lastScore01 = Mathf.Clamp01(value);
        }

        public int ConsecutiveCycles
        {
            get => Mathf.Max(1, consecutiveCycles);
            set => consecutiveCycles = Mathf.Max(1, value);
        }

        public int LastSeenRefreshIndex
        {
            get => lastSeenRefreshIndex;
            set => lastSeenRefreshIndex = value;
        }
    }

    [Serializable]
    public sealed class OpportunityNotice
    {
        [SerializeField]
        private string noticeId = string.Empty;

        [SerializeField]
        private OpportunityPressureType pressureType;

        [SerializeField]
        private OpportunityNoticeSeverity severity;

        [SerializeField, Range(0f, 1f)]
        private float score01;

        [SerializeField]
        private string title = string.Empty;

        [SerializeField, TextArea(1, 3)]
        private string detail = string.Empty;

        [SerializeField, TextArea(1, 2)]
        private string actionText = string.Empty;

        [SerializeField]
        private string sourceLabel = string.Empty;

        [SerializeField]
        private string deskTarget = string.Empty;

        [SerializeField]
        private string lifecycleLabel = string.Empty;

        [SerializeField]
        private int consecutiveCycles = 1;

        public OpportunityNotice()
        {
        }

        public OpportunityNotice(
            string noticeId,
            OpportunityPressureType pressureType,
            OpportunityNoticeSeverity severity,
            float score01,
            string title,
            string detail,
            string actionText,
            string sourceLabel = "",
            string deskTarget = "")
        {
            this.noticeId = noticeId ?? string.Empty;
            this.pressureType = pressureType;
            this.severity = severity;
            this.score01 = Mathf.Clamp01(score01);
            this.title = title ?? string.Empty;
            this.detail = detail ?? string.Empty;
            this.actionText = actionText ?? string.Empty;
            this.sourceLabel = sourceLabel ?? string.Empty;
            this.deskTarget = deskTarget ?? string.Empty;
            lifecycleLabel = string.Empty;
            consecutiveCycles = 1;
        }

        public string NoticeId => noticeId ?? string.Empty;
        public OpportunityPressureType PressureType => pressureType;
        public OpportunityNoticeSeverity Severity => severity;
        public float Score01 => Mathf.Clamp01(score01);
        public string Title => title ?? string.Empty;
        public string Detail => detail ?? string.Empty;
        public string ActionText => actionText ?? string.Empty;
        public string SourceLabel => sourceLabel ?? string.Empty;
        public string DeskTarget => deskTarget ?? string.Empty;
        public string LifecycleLabel => lifecycleLabel ?? string.Empty;
        public int ConsecutiveCycles => Mathf.Max(1, consecutiveCycles);
        public int ScorePercent => Mathf.RoundToInt(Score01 * 100f);

        public void ApplyRuntimeContext(string lifecycle, int cycles)
        {
            lifecycleLabel = lifecycle ?? string.Empty;
            consecutiveCycles = Mathf.Max(1, cycles);
        }
    }

    [Serializable]
    public sealed class OpportunityPressureSnapshot
    {
        [SerializeField, Range(0f, 1f)]
        private float retailPressure01;

        [SerializeField, Range(0f, 1f)]
        private float housingPressure01;

        [SerializeField, Range(0f, 1f)]
        private float laborPressure01;

        [SerializeField, Range(0f, 1f)]
        private float servicePressure01;

        [SerializeField, Range(0f, 1f)]
        private float expansionPressure01;

        [SerializeField]
        private List<OpportunityNotice> notices = new();

        public float RetailPressure01 => Mathf.Clamp01(retailPressure01);
        public float HousingPressure01 => Mathf.Clamp01(housingPressure01);
        public float LaborPressure01 => Mathf.Clamp01(laborPressure01);
        public float ServicePressure01 => Mathf.Clamp01(servicePressure01);
        public float ExpansionPressure01 => Mathf.Clamp01(expansionPressure01);
        public IReadOnlyList<OpportunityNotice> Notices => notices ??= new List<OpportunityNotice>();
        public bool HasNotices => Notices.Count > 0;

        public float GetPressure01(OpportunityPressureType type)
        {
            return type switch
            {
                OpportunityPressureType.Retail => RetailPressure01,
                OpportunityPressureType.Housing => HousingPressure01,
                OpportunityPressureType.Labor => LaborPressure01,
                OpportunityPressureType.Service => ServicePressure01,
                OpportunityPressureType.Expansion => ExpansionPressure01,
                _ => 0f
            };
        }

        public void Replace(
            float retailPressure,
            float housingPressure,
            float laborPressure,
            float servicePressure,
            float expansionPressure,
            List<OpportunityNotice> newNotices)
        {
            retailPressure01 = Mathf.Clamp01(retailPressure);
            housingPressure01 = Mathf.Clamp01(housingPressure);
            laborPressure01 = Mathf.Clamp01(laborPressure);
            servicePressure01 = Mathf.Clamp01(servicePressure);
            expansionPressure01 = Mathf.Clamp01(expansionPressure);
            notices = newNotices ?? new List<OpportunityNotice>();
        }
    }

    [Serializable]
    public sealed class OpportunityPressureRuntimeState
    {
        public bool snapshotInitialized;
        public int refreshSerial;
        public float retailPressure01;
        public float housingPressure01;
        public float laborPressure01;
        public float servicePressure01;
        public float expansionPressure01;
        public string hudAlertText = string.Empty;
        public List<OpportunityNotice> activeNotices = new();
        public List<OpportunityNoticeMemory> noticeMemory = new();
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(270)]
    public sealed class OpportunityPressureRuntimeManager : MonoBehaviour
    {
        private const int HudNoticeLimit = 3;
        private const float RetailNoticeThreshold01 = 0.25f;
        private const float HousingNoticeThreshold01 = 0.90f;
        private const float LaborNoticeThreshold01 = 0.35f;
        private const float ServiceNoticeThreshold01 = 0.40f;
        private const float ExpansionNoticeThreshold01 = 0.50f;
        private const float HardwareSupplyNoticeThreshold01 = 0.35f;
        private const float ImmigrationHousingNoticeThreshold01 = 0.50f;
        private const float PropertyOpeningNoticeThreshold01 = 0.45f;
        private const float LogisticsNoticeThreshold01 = 0.35f;
        private const float MineSupportNoticeThreshold01 = 0.35f;
        private const float ConstructionSupportNoticeThreshold01 = 0.35f;
        private const string HardwareCategoryId = "tools_hardware";
        private const string HousingCapacityNoticeId = "housing_near_capacity";
        private const string ImmigrationHousingNoticeId = "immigration_housing_pressure";
        private const string RentingWaitlistNoticeId = "renting_waitlist_pressure";
        private const string SchoolhouseCivicNeedNoticeId = "schoolhouse_civic_need";
        private const string SchoolhouseTeacherVacancyNoticeId = "schoolhouse_teacher_vacancy";

        [Header("Sources")]
        [SerializeField]
        private TownWorldController townWorld;

        [SerializeField]
        private PopulationManager populationManager;

        [SerializeField]
        private GeneralStoreRuntimeManager generalStoreRuntime;

        [SerializeField]
        private SharedBusinessRuntimeManager sharedBusinessRuntime;

        [SerializeField]
        private TownPulseRuntimeManager townPulseRuntime;

        [SerializeField]
        private AcquisitionMarketManager acquisitionMarket;

        [SerializeField]
        private LogisticsRuntimeManager logisticsRuntime;

        [SerializeField]
        private CivicFoundationManager civicFoundation;

        [SerializeField]
        private TimeManager timeManager;

        [SerializeField]
        private LandLedgersHUDController hudController;

        [Header("Runtime")]
        [SerializeField]
        private OpportunityPressureSnapshot currentSnapshot = new();

        [SerializeField, TextArea(1, 4)]
        private string currentHudAlertText = string.Empty;

        [SerializeField]
        private List<OpportunityNoticeMemory> noticeMemory = new();

        [SerializeField]
        private int refreshSerial;

        [SerializeField]
        private bool currentSnapshotInitialized;

        private TimeManager subscribedTimeManager;

        public OpportunityPressureSnapshot CurrentSnapshot => currentSnapshot ??= new OpportunityPressureSnapshot();
        public IReadOnlyList<OpportunityNotice> CurrentNotices => CurrentSnapshot.Notices;
        public string CurrentHudAlertText => currentHudAlertText ?? string.Empty;

        public void Configure(
            TownWorldController newTownWorld,
            PopulationManager newPopulationManager,
            GeneralStoreRuntimeManager newGeneralStoreRuntime,
            SharedBusinessRuntimeManager newSharedBusinessRuntime,
            TownPulseRuntimeManager newTownPulseRuntime,
            TimeManager newTimeManager,
            LandLedgersHUDController newHudController)
        {
            if (subscribedTimeManager != null && subscribedTimeManager != newTimeManager)
            {
                UnsubscribeFromTime();
            }

            townWorld = newTownWorld;
            populationManager = newPopulationManager;
            generalStoreRuntime = newGeneralStoreRuntime;
            sharedBusinessRuntime = newSharedBusinessRuntime;
            townPulseRuntime = newTownPulseRuntime;
            timeManager = newTimeManager;
            hudController = newHudController;
            currentSnapshotInitialized = false;
            SubscribeToTime();
            RefreshNotices();
        }

        public OpportunityPressureSnapshot RefreshNotices()
        {
            AutoWire();
            refreshSerial++;
            List<OpportunityNotice> notices = new();
            float retailPressure = EvaluateRetailPressure(notices);
            float housingPressure = EvaluateHousingPressure(notices);
            float laborPressure = EvaluateLaborPressure(notices);
            float servicePressure = EvaluateServicePressure(notices);
            float expansionPressure = EvaluateExpansionPressure(
                retailPressure,
                housingPressure,
                laborPressure,
                servicePressure,
                notices);

            DecorateNoticeLifecycle(notices);

            notices.Sort(CompareNotices);
            CurrentSnapshot.Replace(
                retailPressure,
                housingPressure,
                laborPressure,
                servicePressure,
                expansionPressure,
                notices);

            currentHudAlertText = BuildHudAlertText(notices);
            currentSnapshotInitialized = true;
            hudController?.SetOpportunityNoticeAlert(currentHudAlertText);
            return CurrentSnapshot;
        }

        public string BuildNoticeBoardText(int maxNotices = 6)
        {
            EnsureSnapshotInitializedForRead();
            IReadOnlyList<OpportunityNotice> notices = CurrentNotices;
            if (notices == null || notices.Count == 0)
            {
                return "Opportunity Board\nNo material town pressure is posted.";
            }

            List<OpportunityNotice> displayNotices = BuildCompactDisplayNoticeList(notices, maxNotices);
            StringBuilder builder = new();
            builder.AppendLine("Opportunity Board");
            builder.AppendLine(BuildPressureDebugLine(CurrentSnapshot));

            for (int i = 0; i < displayNotices.Count; i++)
            {
                OpportunityNotice notice = displayNotices[i];
                if (notice == null)
                {
                    continue;
                }

                builder.Append("- ");
                builder.Append(BuildNoticeLaneLabel(notice));
                builder.Append(" | ");
                builder.Append(GetPressureLabel(notice.PressureType));
                builder.Append(" | ");
                builder.Append(notice.Title);
                builder.Append(" (");
                builder.Append(notice.ScorePercent);
                builder.Append("%): ");
                if (!string.IsNullOrWhiteSpace(notice.LifecycleLabel))
                {
                    builder.Append(notice.LifecycleLabel);
                    builder.Append(" | ");
                }

                builder.Append(notice.Detail);
                if (!string.IsNullOrWhiteSpace(notice.ActionText))
                {
                    builder.Append(" Next: ");
                    builder.Append(notice.ActionText);
                }

                if (!string.IsNullOrWhiteSpace(notice.DeskTarget))
                {
                    builder.Append(" Desk: ");
                    builder.Append(notice.DeskTarget);
                }

                if (!string.IsNullOrWhiteSpace(notice.SourceLabel))
                {
                    builder.Append(" Source: ");
                    builder.Append(notice.SourceLabel);
                }

                builder.AppendLine();
            }

            return builder.ToString().TrimEnd();
        }

        public string BuildDeskSummary(string deskTarget, int maxNotices = 3)
        {
            EnsureSnapshotInitializedForRead();
            if (string.IsNullOrWhiteSpace(deskTarget))
            {
                return string.Empty;
            }

            IReadOnlyList<OpportunityNotice> notices = CurrentNotices;
            StringBuilder builder = new();
            int appended = 0;
            for (int i = 0; i < notices.Count && appended < Mathf.Max(1, maxNotices); i++)
            {
                OpportunityNotice notice = notices[i];
                if (notice == null || !string.Equals(notice.DeskTarget, deskTarget, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (builder.Length == 0)
                {
                    builder.Append(deskTarget.Trim());
                    builder.Append(": ");
                }
                else
                {
                    builder.Append(" | ");
                }

                builder.Append(GetPressureLabel(notice.PressureType));
                builder.Append(" ");
                builder.Append(notice.ScorePercent);
                builder.Append("% ");
                builder.Append(string.IsNullOrWhiteSpace(notice.LifecycleLabel) ? "Active" : notice.LifecycleLabel);
                builder.Append(" - ");
                builder.Append(string.IsNullOrWhiteSpace(notice.ActionText) ? notice.Title : notice.ActionText);
                appended++;
            }

            return builder.ToString();
        }

        public bool TryGetNotice(string noticeId, out OpportunityNotice notice)
        {
            notice = null;
            if (string.IsNullOrWhiteSpace(noticeId))
            {
                return false;
            }

            EnsureSnapshotInitializedForRead();
            IReadOnlyList<OpportunityNotice> notices = CurrentNotices;
            for (int i = 0; i < notices.Count; i++)
            {
                OpportunityNotice candidate = notices[i];
                if (candidate != null && string.Equals(candidate.NoticeId, noticeId, StringComparison.OrdinalIgnoreCase))
                {
                    notice = candidate;
                    return true;
                }
            }

            return false;
        }

        public OpportunityPressureRuntimeState CaptureRuntimeState()
        {
            // Save capture must not synthesize notices or advance lifecycle state; it only freezes the latest authored runtime snapshot.
            return new OpportunityPressureRuntimeState
            {
                snapshotInitialized = currentSnapshotInitialized,
                refreshSerial = refreshSerial,
                retailPressure01 = CurrentSnapshot.RetailPressure01,
                housingPressure01 = CurrentSnapshot.HousingPressure01,
                laborPressure01 = CurrentSnapshot.LaborPressure01,
                servicePressure01 = CurrentSnapshot.ServicePressure01,
                expansionPressure01 = CurrentSnapshot.ExpansionPressure01,
                hudAlertText = CurrentHudAlertText,
                activeNotices = CloneNotices(CurrentSnapshot.Notices),
                noticeMemory = CloneNoticeMemory(noticeMemory)
            };
        }

        public void RestoreRuntimeState(OpportunityPressureRuntimeState state)
        {
            if (state == null)
            {
                ClearRuntimeState();
                return;
            }

            refreshSerial = Mathf.Max(0, state.refreshSerial);
            currentSnapshotInitialized = state.snapshotInitialized;
            noticeMemory = CloneNoticeMemory(state.noticeMemory);
            CurrentSnapshot.Replace(
                state.retailPressure01,
                state.housingPressure01,
                state.laborPressure01,
                state.servicePressure01,
                state.expansionPressure01,
                CloneNotices(state.activeNotices));
            currentHudAlertText = state.hudAlertText ?? string.Empty;
            hudController?.SetOpportunityNoticeAlert(currentHudAlertText);
        }

        public void ClearRuntimeState()
        {
            refreshSerial = 0;
            currentSnapshotInitialized = false;
            noticeMemory = new List<OpportunityNoticeMemory>();
            CurrentSnapshot.Replace(0f, 0f, 0f, 0f, 0f, new List<OpportunityNotice>());
            currentHudAlertText = string.Empty;
            hudController?.SetOpportunityNoticeAlert(string.Empty);
        }

        private void EnsureSnapshotInitializedForRead()
        {
            if (!currentSnapshotInitialized)
            {
                RefreshNotices();
            }
        }

        private void Awake()
        {
            AutoWire();
        }

        private void Start()
        {
            SubscribeToTime();
            RefreshNotices();
        }

        private void OnEnable()
        {
            SubscribeToTime();
        }

        private void OnDisable()
        {
            UnsubscribeFromTime();
            // Null-conditional dispatch checks only the managed reference. Unity
            // can destroy the HUD native object first during scene/test teardown,
            // so use Unity's overloaded null check before touching it.
            if (hudController != null)
            {
                hudController.SetOpportunityNoticeAlert(string.Empty);
            }
        }

        private void AutoWire()
        {
            townWorld ??= FindAnyObjectByType<TownWorldController>();
            populationManager ??= FindAnyObjectByType<PopulationManager>();
            generalStoreRuntime ??= FindAnyObjectByType<GeneralStoreRuntimeManager>();
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            townPulseRuntime ??= FindAnyObjectByType<TownPulseRuntimeManager>();
            acquisitionMarket ??= FindAnyObjectByType<AcquisitionMarketManager>();
            logisticsRuntime ??= FindAnyObjectByType<LogisticsRuntimeManager>();
            civicFoundation ??= FindAnyObjectByType<CivicFoundationManager>();
            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            hudController ??= FindAnyObjectByType<LandLedgersHUDController>();
        }

        private void SubscribeToTime()
        {
            AutoWire();
            if (timeManager == null || subscribedTimeManager == timeManager)
            {
                return;
            }

            if (subscribedTimeManager != null)
            {
                UnsubscribeFromTime();
            }

            timeManager.DayChanged += OnDatePressureSourceChanged;
            timeManager.WeekChanged += OnDatePressureSourceChanged;
            subscribedTimeManager = timeManager;
        }

        private void UnsubscribeFromTime()
        {
            if (subscribedTimeManager == null)
            {
                return;
            }

            subscribedTimeManager.DayChanged -= OnDatePressureSourceChanged;
            subscribedTimeManager.WeekChanged -= OnDatePressureSourceChanged;
            subscribedTimeManager = null;
        }

        private void OnDatePressureSourceChanged(SimulationDateChangedContext context)
        {
            RefreshNotices();
        }

        private float EvaluateRetailPressure(List<OpportunityNotice> notices)
        {
            int offMapUnits = generalStoreRuntime != null ? generalStoreRuntime.LastDailyReserveOffMapUnits : 0;
            int lostDemandCents = generalStoreRuntime != null ? generalStoreRuntime.LastDailyReserveOffMapLostDemandCents : 0;
            bool reorderNeeded = generalStoreRuntime != null && generalStoreRuntime.ReorderNeeded;
            int pulseMissedUnits = townPulseRuntime != null ? townPulseRuntime.GetCurrentDateMissedUnits() : 0;
            float stockGap = 0f;
            if (generalStoreRuntime != null && generalStoreRuntime.RuntimeState != null)
            {
                stockGap = 1f - generalStoreRuntime.RuntimeState.StockHealth01;
            }

            float pressure = Mathf.Max(
                Normalize(offMapUnits, 6),
                Normalize(lostDemandCents, 600),
                Normalize(pulseMissedUnits, 6),
                reorderNeeded ? 0.45f : 0f,
                stockGap,
                EvaluateHardwareSupplyPressure(notices));

            bool hasRetailSignal = offMapUnits > 0
                || lostDemandCents > 0
                || pulseMissedUnits > 0
                || reorderNeeded
                || stockGap >= RetailNoticeThreshold01;
            if (hasRetailSignal && pressure >= RetailNoticeThreshold01)
            {
                string detail = $"Missed {pulseMissedUnits} pulse units, {offMapUnits} off-map reserve units, stock gap {FormatPercent(stockGap)}.";
                string action = reorderNeeded || stockGap >= RetailNoticeThreshold01
                    ? "Restock thin categories and capture missed demand."
                    : "Add local stock or sellers for demand leaving town.";
                notices.Add(new OpportunityNotice(
                    "retail_lost_demand",
                    OpportunityPressureType.Retail,
                    ResolveSeverity(pressure),
                    pressure,
                    "Retail demand",
                    detail,
                    action,
                    "General Store",
                    "General Store"));
            }

            return pressure;
        }

        private float EvaluateHardwareSupplyPressure(List<OpportunityNotice> notices)
        {
            if (sharedBusinessRuntime == null)
            {
                return 0f;
            }

            List<BusinessInstanceState> businesses = BuildUniqueBusinessList();
            BusinessInstanceState strongestBlacksmith = null;
            bool hasBlacksmith = false;
            bool hasActiveBlacksmith = false;
            int activeHardwareStock = 0;
            int activeHardwareTarget = 0;
            float strongestEfficiency = 0f;

            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.BusinessType != BusinessType.Blacksmith)
                {
                    continue;
                }

                hasBlacksmith = true;
                float efficiency = Mathf.Clamp01(business.OperatingEfficiency01);
                if (efficiency > strongestEfficiency)
                {
                    strongestEfficiency = efficiency;
                    strongestBlacksmith = business;
                }

                if (business.RuntimeState == null || efficiency <= 0f)
                {
                    continue;
                }

                hasActiveBlacksmith = true;
                CategoryStockState stock = business.RuntimeState.GetCategoryStock(HardwareCategoryId);
                if (stock != null)
                {
                    activeHardwareStock += stock.CurrentStockUnits;
                    activeHardwareTarget += stock.TargetStockUnits;
                }
            }

            float pressure;
            string detail;
            if (!hasBlacksmith)
            {
                pressure = 0.85f;
                detail = "No blacksmith is supplying nails/simple hardware; regional freight can cover projects at higher landed cost.";
            }
            else if (!hasActiveBlacksmith)
            {
                pressure = 0.75f;
                string label = strongestBlacksmith != null ? strongestBlacksmith.RuntimeDisplayName : "Blacksmith";
                detail = $"{label} is not active enough to supply nails/simple hardware; regional freight can cover projects at higher landed cost.";
            }
            else
            {
                int target = Mathf.Max(1, activeHardwareTarget);
                float stock01 = Mathf.Clamp01((float)activeHardwareStock / target);
                pressure = 1f - stock01;
                detail = $"Active blacksmith hardware stock {activeHardwareStock}/{target}; regional freight can cover shortages at higher landed cost.";
            }

            if (pressure >= HardwareSupplyNoticeThreshold01)
            {
                notices.Add(new OpportunityNotice(
                    "hardware_supply_shortage",
                    OpportunityPressureType.Retail,
                    ResolveSeverity(pressure),
                    pressure,
                    "Hardware supply",
                    detail,
                    "Restore or staff blacksmith supply; use regional freight only as the stopgap.",
                    "Nails / simple hardware",
                    "General Store"));
            }

            return pressure;
        }

        private float EvaluateHousingPressure(List<OpportunityNotice> notices)
        {
            int households = CountHouseholds();
            int homeCapacity = CountResidentialCapacity();
            PopulationState state = populationManager != null ? populationManager.State : null;
            int applicants = state != null ? Mathf.Max(0, state.rentalApplicantCount) : 0;
            int strongerNeed = state != null ? Mathf.Max(0, state.strongerHousingNeedCount) : 0;
            int vacancies = state != null ? Mathf.Max(0, state.rentalVacancyCount) : 0;
            float rentalPressure = state != null ? Mathf.Clamp01(state.rentalPressure01) : 0f;
            if (homeCapacity <= 0)
            {
                float missingPressure = households > 0 || applicants > 0 ? 1f : 0f;
                if (households > 0 || applicants > 0)
                {
                    notices.Add(new OpportunityNotice(
                        HousingCapacityNoticeId,
                        OpportunityPressureType.Housing,
                        OpportunityNoticeSeverity.Urgent,
                        missingPressure,
                        "Housing shortage",
                        $"{households} households, {applicants} waiting applicants, and no tracked home capacity.",
                        "Immediate risk: arrivals cannot settle. Medium-term opening: build housing or mixed-use upper-floor homes.",
                        "Town housing",
                        "Renting & Boarding"));
                    AddApplicantHousingNotices(notices, missingPressure, applicants, strongerNeed, vacancies, state);
                }

                return missingPressure;
            }

            float occupancy01 = Mathf.Clamp01((float)households / homeCapacity);
            float applicantGap01 = applicants > 0
                ? Mathf.Clamp01((applicants + strongerNeed) / (float)Mathf.Max(1, vacancies + applicants))
                : 0f;
            float pressure = Mathf.Max(occupancy01, rentalPressure, applicantGap01);
            if (occupancy01 >= HousingNoticeThreshold01)
            {
                notices.Add(new OpportunityNotice(
                    HousingCapacityNoticeId,
                    OpportunityPressureType.Housing,
                    ResolveSeverity(pressure),
                    pressure,
                    occupancy01 >= 1f ? "Housing full" : "Housing near capacity",
                    $"{households}/{homeCapacity} household slots occupied; {vacancies} vacancies and {applicants} waiting applicants.",
                    "Immediate risk: rent pressure and unsettled arrivals. Medium-term opening: add rentable rooms or mixed-use housing.",
                    "Town housing",
                    "Renting & Boarding"));
            }

            AddApplicantHousingNotices(notices, pressure, applicants, strongerNeed, vacancies, state);

            return pressure;
        }

        private static void AddApplicantHousingNotices(
            List<OpportunityNotice> notices,
            float pressure,
            int applicants,
            int strongerNeed,
            int vacancies,
            PopulationState state)
        {
            if (notices == null || applicants <= 0 || pressure < ImmigrationHousingNoticeThreshold01)
            {
                return;
            }

            float immigrationPressure = Mathf.Max(pressure, applicants > vacancies ? 0.75f : pressure);
            notices.Add(new OpportunityNotice(
                ImmigrationHousingNoticeId,
                OpportunityPressureType.Housing,
                ResolveSeverity(immigrationPressure),
                immigrationPressure,
                "Immigration housing pressure",
                $"{applicants} waiting applicants, {strongerNeed} stronger housing needs, {vacancies} rentable vacancies.",
                "Immediate risk: workers and families leave unsettled. Medium-term opening: prepare rentals before the next arrival wave.",
                "Newcomer settlement",
                "Renting & Boarding"));

            if (vacancies > 0)
            {
                return;
            }

            int transients = state != null ? Mathf.Max(0, state.transientPersonCount) : 0;
            float waitlistPressure = Mathf.Max(pressure, 0.8f);
            notices.Add(new OpportunityNotice(
                RentingWaitlistNoticeId,
                OpportunityPressureType.Housing,
                ResolveSeverity(waitlistPressure),
                waitlistPressure,
                "Renting waitlist pressure",
                $"{applicants} waiting applicants, {strongerNeed} stronger housing needs, and {transients} transient residents still need rooms.",
                "Immediate risk: labor arrivals stall. Medium-term opening: open rentable rooms, boarding beds, or mixed-use upper floors.",
                "Settlement desk",
                "Renting & Boarding"));
        }

        private float EvaluateLaborPressure(List<OpportunityNotice> notices)
        {
            List<BusinessInstanceState> businesses = BuildUniqueBusinessList();
            int openSlots = 0;
            int inactiveRequiredSlots = 0;
            float strongestEfficiencyGap = 0f;
            int pressuredBusinesses = 0;

            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                BusinessRuntimeState runtime = business != null ? business.RuntimeState : null;
                if (runtime == null || runtime.TargetWorkerCount <= 0)
                {
                    continue;
                }

                int businessOpenSlots = 0;
                int businessInactiveRequiredSlots = 0;
                IReadOnlyList<WorkerSlotState> slots = runtime.WorkerSlots;
                for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
                {
                    WorkerSlotState slot = slots[slotIndex];
                    if (slot == null)
                    {
                        continue;
                    }

                    if (!slot.IsFilled)
                    {
                        openSlots++;
                        businessOpenSlots++;
                    }

                    if (slot.RequiredForOpening && !slot.IsPaidActive)
                    {
                        inactiveRequiredSlots++;
                        businessInactiveRequiredSlots++;
                    }
                }

                float efficiencyGap = 1f - runtime.OperatingEfficiency01;
                if (efficiencyGap > 0.25f || businessOpenSlots > 0 || businessInactiveRequiredSlots > 0)
                {
                    pressuredBusinesses++;
                    strongestEfficiencyGap = Mathf.Max(strongestEfficiencyGap, efficiencyGap);
                }
            }

            int availableWorkers = CountAvailableWorkers();
            int workerGap = Mathf.Max(0, openSlots - availableWorkers);
            float pressure = Mathf.Max(
                Normalize(inactiveRequiredSlots, 2),
                Normalize(workerGap, 4),
                Normalize(openSlots, 8) * 0.65f,
                strongestEfficiencyGap);

            bool hasLaborSignal = inactiveRequiredSlots > 0
                || workerGap > 0
                || strongestEfficiencyGap >= LaborNoticeThreshold01;
            if (hasLaborSignal && pressure >= LaborNoticeThreshold01)
            {
                notices.Add(new OpportunityNotice(
                    "labor_shortage",
                    OpportunityPressureType.Labor,
                    ResolveSeverity(pressure),
                    pressure,
                    "Labor shortage",
                    $"{openSlots} open roles, {inactiveRequiredSlots} required inactive, {availableWorkers} available workers.",
                    pressuredBusinesses > 1 ? "Hire across the most constrained businesses." : "Hire or fund required roles.",
                    "Town labor",
                    "Properties"));
            }

            return pressure;
        }

        private float EvaluateServicePressure(List<OpportunityNotice> notices)
        {
            List<BusinessInstanceState> businesses = BuildUniqueBusinessList();
            bool hasDoctor = false;
            int serviceCapacity = 0;
            int activeService = 0;
            int underStaffedServiceBusinesses = 0;

            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                BusinessCapacityState capacity = business != null ? business.Capacity : null;
                if (business != null && business.BusinessType == BusinessType.Doctor)
                {
                    hasDoctor = true;
                }

                if (capacity == null || capacity.ServiceCapacityVisitsPerDay <= 0)
                {
                    continue;
                }

                serviceCapacity += capacity.ServiceCapacityVisitsPerDay;
                activeService += capacity.CurrentServiceVisitsPerDay;
                if (business.RuntimeState != null && business.RuntimeState.OperatingEfficiency01 < 0.75f)
                {
                    underStaffedServiceBusinesses++;
                }
            }

            int medicineMissedUnits = CountPulseMisses("medicine") + CountPulseMisses("remedies");
            float serviceGap = serviceCapacity <= 0 ? 1f : 1f - Mathf.Clamp01((float)activeService / serviceCapacity);
            float pressure = Mathf.Max(
                !hasDoctor ? 0.55f : 0f,
                serviceCapacity <= 0 ? 0.65f : 0f,
                serviceGap,
                Normalize(underStaffedServiceBusinesses, 2),
                Normalize(medicineMissedUnits, 4));

            bool hasServiceSignal = !hasDoctor
                || serviceCapacity <= 0
                || serviceGap >= ServiceNoticeThreshold01
                || medicineMissedUnits > 0
                || underStaffedServiceBusinesses > 0;
            if (hasServiceSignal && pressure >= ServiceNoticeThreshold01)
            {
                string detail = hasDoctor
                    ? $"{activeService}/{serviceCapacity} daily service visits active, medicine misses {medicineMissedUnits}."
                    : $"{activeService}/{serviceCapacity} daily service visits active, no doctor practice in town.";
                notices.Add(new OpportunityNotice(
                    "service_gap",
                    OpportunityPressureType.Service,
                    ResolveSeverity(pressure),
                    pressure,
                    "Service gap",
                    detail,
                    hasDoctor ? "Staff service businesses and cover remedy demand." : "Open or attract a doctor practice.",
                    "Town services",
                    "Acquisitions"));
            }

            pressure = Mathf.Max(
                pressure,
                EvaluateSchoolhouseServicePressure(notices),
                EvaluateLogisticsServicePressure(notices),
                EvaluateMineSupportPressure(notices));

            return pressure;
        }

        private float EvaluateLogisticsServicePressure(List<OpportunityNotice> notices)
        {
            if (logisticsRuntime == null)
            {
                return 0f;
            }

            LogisticsTownPressureSnapshot snapshot = logisticsRuntime.CaptureTownPressureSnapshot();
            if (snapshot == null || snapshot.Pressure01 < LogisticsNoticeThreshold01)
            {
                return 0f;
            }

            notices.Add(new OpportunityNotice(
                "freight_bottleneck",
                OpportunityPressureType.Service,
                ResolveSeverity(snapshot.Pressure01),
                snapshot.Pressure01,
                "Freight bottleneck",
                snapshot.BuildPressureDetail(),
                snapshot.BuildActionText(),
                "Logistics",
                "Supply & Trade"));
            return snapshot.Pressure01;
        }

        private float EvaluateMineSupportPressure(List<OpportunityNotice> notices)
        {
            List<BusinessInstanceState> businesses = BuildUniqueBusinessList();
            BusinessInstanceState strongestBusiness = null;
            MineRuntimeState strongestMine = null;
            float strongestPressure = 0f;

            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                MineRuntimeState mine = business != null ? business.MineState : null;
                if (mine == null)
                {
                    continue;
                }

                float pressure = mine.CalculateMineSupportPressure01(business);
                if (pressure > strongestPressure)
                {
                    strongestPressure = pressure;
                    strongestMine = mine;
                    strongestBusiness = business;
                }
            }

            if (strongestMine == null || strongestPressure < MineSupportNoticeThreshold01)
            {
                return 0f;
            }

            notices.Add(new OpportunityNotice(
                "mine_support_pressure",
                OpportunityPressureType.Service,
                ResolveSeverity(strongestPressure),
                strongestPressure,
                "Mine support pressure",
                strongestMine.BuildMinePressureDetail(strongestBusiness),
                strongestMine.BuildMinePressureAction(strongestBusiness),
                "Mine support",
                "Supply & Trade"));
            return strongestPressure;
        }

        private float EvaluateSchoolhouseServicePressure(List<OpportunityNotice> notices)
        {
            SchoolhouseRuntimeSnapshot snapshot = civicFoundation != null
                ? civicFoundation.CaptureSchoolhouseRuntimeSnapshot()
                : new SchoolhouseState().CaptureRuntimeSnapshot(
                    populationManager != null ? populationManager.State : null,
                    SchoolhouseThresholds.Default,
                    SchoolhouseContributionWeights.Default);

            if (snapshot.OperationalState == SchoolhouseOperationalState.UnbuiltReady)
            {
                float pressure = Mathf.Clamp01(0.55f + Normalize(snapshot.SchoolAgeChildCount - snapshot.Eligibility.RequiredSchoolAgeChildren, 12) * 0.25f);
                notices.Add(new OpportunityNotice(
                    "schoolhouse_civic_need",
                    OpportunityPressureType.Service,
                    ResolveSeverity(pressure),
                    pressure,
                    "Schoolhouse pressure",
                    $"{snapshot.Eligibility.HouseholdCount} households, {snapshot.Eligibility.PopulationCount} people, and {snapshot.SchoolAgeChildCount} school-age children now justify a modest one-room Schoolhouse.",
                    "Establish a Schoolhouse as a civic institution before family settlement confidence weakens.",
                    "Civic pressure",
                    "Civic"));
                return pressure;
            }

            if (snapshot.OperationalState == SchoolhouseOperationalState.PressureBuilding)
            {
                float pressure = 0.42f;
                notices.Add(new OpportunityNotice(
                    "schoolhouse_family_signal",
                    OpportunityPressureType.Service,
                    OpportunityNoticeSeverity.Watch,
                    pressure,
                    "Schooling need visible",
                    snapshot.Eligibility.BuildNeedSummary(),
                    "Watch household growth and prepare a civic parcel if families keep settling.",
                    "Civic pressure",
                    "Civic"));
                return pressure;
            }

            if (snapshot.OperationalState == SchoolhouseOperationalState.TeacherVacant)
            {
                float pressure = Mathf.Max(0.62f, Normalize(snapshot.SchoolAgeChildCount, snapshot.OneRoomCapacity));
                notices.Add(new OpportunityNotice(
                    "schoolhouse_teacher_vacancy",
                    OpportunityPressureType.Service,
                    ResolveSeverity(pressure),
                    pressure,
                    "Teacher vacancy",
                    $"The Schoolhouse is built but has no teacher; {snapshot.SchoolAgeChildCount} school-age children receive no regular school term.",
                    "Hire or attract a Teacher so the Schoolhouse can operate.",
                    "Civic staffing",
                    "Civic"));
                return pressure;
            }

            if (snapshot.OperationalState == SchoolhouseOperationalState.Underused)
            {
                float pressure = 0.4f;
                notices.Add(new OpportunityNotice(
                    "schoolhouse_underused",
                    OpportunityPressureType.Service,
                    OpportunityNoticeSeverity.Watch,
                    pressure,
                    "Schoolhouse thin attendance",
                    $"The Schoolhouse remains open but only supports {snapshot.AttendanceSupportedCount}/{snapshot.OneRoomCapacity} seats.",
                    "Keep the teacher only if family settlement remains strong enough to justify the term.",
                    "Civic staffing",
                    "Civic"));
                return pressure;
            }

            return 0f;
        }

        private float EvaluateExpansionPressure(
            float retailPressure,
            float housingPressure,
            float laborPressure,
            float servicePressure,
            List<OpportunityNotice> notices)
        {
            int openPlots = CountVacantExpansionPlots();
            int idleOwnedShells = CountPlayerOwnedIdleShells();
            int opportunitySites = openPlots + idleOwnedShells;
            float strongestPressure = Mathf.Max(retailPressure, housingPressure, laborPressure, servicePressure);
            float sitePressure = Mathf.Clamp01(opportunitySites / 3f);
            float expansionPressure = strongestPressure >= ExpansionNoticeThreshold01 && opportunitySites > 0
                ? Mathf.Clamp01(strongestPressure * 0.75f + sitePressure * 0.25f)
                : 0f;

            if (expansionPressure >= ExpansionNoticeThreshold01)
            {
                notices.Add(new OpportunityNotice(
                    "expansion_window",
                    OpportunityPressureType.Expansion,
                    ResolveSeverity(expansionPressure),
                    expansionPressure,
                    "Expansion window",
                    $"{openPlots} empty plots and {idleOwnedShells} owned idle shells can answer current pressure.",
                    BuildExpansionAction(retailPressure, housingPressure, laborPressure, servicePressure),
                    "Town growth",
                    "Acquisitions"));
            }

            if (acquisitionMarket != null && !string.IsNullOrWhiteSpace(acquisitionMarket.LastTownActionSummary))
            {
                notices.Add(new OpportunityNotice(
                    "active_town_action",
                    OpportunityPressureType.Expansion,
                    ResolveSeverity(Mathf.Max(expansionPressure, strongestPressure)),
                    Mathf.Max(expansionPressure, strongestPressure),
                    "Town action",
                    acquisitionMarket.LastTownActionSummary,
                    "Review open listings before rivals and local owners absorb them.",
                    "Town growth",
                    "Acquisitions"));
            }

            expansionPressure = Mathf.Max(expansionPressure, EvaluateConstructionSupportPressure(notices));
            EvaluatePropertyOpeningNotices(notices, strongestPressure);
            return expansionPressure;
        }

        private float EvaluateConstructionSupportPressure(List<OpportunityNotice> notices)
        {
            if (acquisitionMarket == null || acquisitionMarket.ConstructionSupportNodes == null)
            {
                return 0f;
            }

            ConstructionSupportNodeState strongest = null;
            float strongestPressure = 0f;
            IReadOnlyList<ConstructionSupportNodeState> nodes = acquisitionMarket.ConstructionSupportNodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                ConstructionSupportNodeState node = nodes[i];
                if (node == null || node.ResourceKind != ConstructionResourceKind.Lumber)
                {
                    continue;
                }

                float pressure = node.CalculateConstructionSupportPressure01();
                if (pressure > strongestPressure)
                {
                    strongestPressure = pressure;
                    strongest = node;
                }
            }

            if (strongest == null || strongestPressure < ConstructionSupportNoticeThreshold01)
            {
                return 0f;
            }

            notices.Add(new OpportunityNotice(
                "construction_support_pressure",
                OpportunityPressureType.Expansion,
                ResolveSeverity(strongestPressure),
                strongestPressure,
                "Construction support pressure",
                strongest.BuildConstructionSupportPressureDetail(),
                strongest.BuildConstructionSupportPressureAction(),
                "Construction support",
                "Supply & Trade"));
            return strongestPressure;
        }

        private void EvaluatePropertyOpeningNotices(List<OpportunityNotice> notices, float strongestPressure)
        {
            if (acquisitionMarket == null)
            {
                return;
            }

            AcquisitionListing land = FindStrongestListing(acquisitionMarket.LandListings);
            if (land != null)
            {
                float pressure = Mathf.Max(strongestPressure, Mathf.Clamp01(land.pressure01), PropertyOpeningNoticeThreshold01);
                notices.Add(new OpportunityNotice(
                    "property_opening_land",
                    OpportunityPressureType.Expansion,
                    ResolveSeverity(pressure),
                    pressure,
                    "Property Openings",
                    BuildListingLeadDetail(land),
                    "Inspect the parcel, compare build fit, then decide whether to watch, finance, or offer.",
                    "Acquisition market",
                    "Acquisitions"));
            }

            AcquisitionListing business = FindStrongestListing(acquisitionMarket.BusinessListings);
            if (business != null)
            {
                float pressure = Mathf.Max(strongestPressure, Mathf.Clamp01(business.pressure01), PropertyOpeningNoticeThreshold01);
                notices.Add(new OpportunityNotice(
                    "property_opening_business",
                    OpportunityPressureType.Expansion,
                    ResolveSeverity(pressure),
                    pressure,
                    "Business Lead",
                    BuildListingLeadDetail(business),
                    "Inspect operations, check staffing and stock, then decide whether to negotiate.",
                    "Acquisition market",
                    "Acquisitions"));
            }
        }

        private static AcquisitionListing FindStrongestListing(IReadOnlyList<AcquisitionListing> listings)
        {
            if (listings == null)
            {
                return null;
            }

            AcquisitionListing best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < listings.Count; i++)
            {
                AcquisitionListing listing = listings[i];
                if (listing == null)
                {
                    continue;
                }

                float score = Mathf.Clamp01(listing.pressure01) + Mathf.Clamp01(listing.aiEligible ? 0.1f : 0f);
                if (best == null || score > bestScore)
                {
                    best = listing;
                    bestScore = score;
                }
            }

            return best;
        }

        private List<BusinessInstanceState> BuildUniqueBusinessList()
        {
            List<BusinessInstanceState> businesses = new();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            AddUniqueBusiness(generalStoreRuntime != null ? generalStoreRuntime.CurrentBusiness : null, businesses, seen);

            IReadOnlyList<BusinessInstanceState> sharedBusinesses = sharedBusinessRuntime != null
                ? sharedBusinessRuntime.Businesses
                : null;
            if (sharedBusinesses != null)
            {
                for (int i = 0; i < sharedBusinesses.Count; i++)
                {
                    AddUniqueBusiness(sharedBusinesses[i], businesses, seen);
                }
            }

            return businesses;
        }

        private int CountAvailableWorkers()
        {
            PopulationState state = populationManager != null ? populationManager.State : null;
            if (state == null || state.people == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < state.people.Count; i++)
            {
                PersonState person = state.people[i];
                if (person != null
                    && person.laborAccessLevel >= LaborAccessLevel.YoungWorker
                    && person.workplaceBuildingId < 0)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountHouseholds()
        {
            PopulationState state = populationManager != null ? populationManager.State : null;
            return state != null && state.households != null ? state.households.Count : 0;
        }

        private int CountResidentialCapacity()
        {
            if (townWorld == null)
            {
                return 0;
            }

            int capacity = 0;
            IReadOnlyList<PlacedBuilding> buildings = townWorld.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingDefinition definition = buildings[i] != null ? buildings[i].definition : null;
                if (definition != null)
                {
                    capacity += definition.ResidentHouseholdCapacity;
                }
            }

            return capacity;
        }

        private int CountPulseMisses(string categoryNeedle)
        {
            return townPulseRuntime != null
                ? townPulseRuntime.GetCurrentDateMissedUnitsForCategory(categoryNeedle)
                : 0;
        }

        private int CountVacantExpansionPlots()
        {
            if (townWorld == null)
            {
                return 0;
            }

            int count = 0;
            IReadOnlyList<TownPlot> plots = townWorld.Plots;
            for (int i = 0; i < plots.Count; i++)
            {
                TownPlot plot = plots[i];
                if (plot != null && plot.buildingId < 0 && !plot.reservedForLandSale)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountPlayerOwnedIdleShells()
        {
            if (townWorld == null)
            {
                return 0;
            }

            int count = 0;
            IReadOnlyList<PlacedBuilding> buildings = townWorld.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                PlacedBuilding building = buildings[i];
                if (building != null && building.playerOwned && !IsBuildingAssignedToBusiness(building.id))
                {
                    count++;
                }
            }

            return count;
        }

        private bool IsBuildingAssignedToBusiness(int buildingId)
        {
            List<BusinessInstanceState> businesses = BuildUniqueBusinessList();
            for (int i = 0; i < businesses.Count; i++)
            {
                if (businesses[i] != null && businesses[i].AssignedBuildingId == buildingId)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddUniqueBusiness(
            BusinessInstanceState business,
            List<BusinessInstanceState> businesses,
            HashSet<string> seen)
        {
            if (business == null || businesses == null || seen == null)
            {
                return;
            }

            string key = !string.IsNullOrWhiteSpace(business.InstanceId)
                ? business.InstanceId
                : $"{business.BusinessType}:{business.AssignedBuildingId}";
            if (seen.Add(key))
            {
                businesses.Add(business);
            }
        }

        private void DecorateNoticeLifecycle(List<OpportunityNotice> notices)
        {
            noticeMemory ??= new List<OpportunityNoticeMemory>();
            if (notices == null)
            {
                return;
            }

            for (int i = noticeMemory.Count - 1; i >= 0; i--)
            {
                OpportunityNoticeMemory memory = noticeMemory[i];
                if (memory == null)
                {
                    noticeMemory.RemoveAt(i);
                    continue;
                }

                if (memory.LastSeenRefreshIndex != refreshSerial - 1)
                {
                    memory.ConsecutiveCycles = 1;
                }
            }

            for (int i = 0; i < notices.Count; i++)
            {
                OpportunityNotice notice = notices[i];
                if (notice == null || string.IsNullOrWhiteSpace(notice.NoticeId))
                {
                    continue;
                }

                OpportunityNoticeMemory memory = FindOrCreateMemory(notice.NoticeId);
                bool seenLastRefresh = memory.LastSeenRefreshIndex == refreshSerial - 1;
                int cycles = seenLastRefresh ? memory.ConsecutiveCycles + 1 : 1;
                string lifecycle = ResolveLifecycleLabel(cycles, notice.Score01, memory.LastScore01, seenLastRefresh);
                memory.ConsecutiveCycles = cycles;
                memory.LastScore01 = notice.Score01;
                memory.LastSeenRefreshIndex = refreshSerial;
                notice.ApplyRuntimeContext(lifecycle, cycles);
            }
        }

        private OpportunityNoticeMemory FindOrCreateMemory(string noticeId)
        {
            for (int i = 0; i < noticeMemory.Count; i++)
            {
                OpportunityNoticeMemory memory = noticeMemory[i];
                if (memory != null && string.Equals(memory.NoticeId, noticeId, StringComparison.OrdinalIgnoreCase))
                {
                    return memory;
                }
            }

            OpportunityNoticeMemory created = new()
            {
                NoticeId = noticeId,
                ConsecutiveCycles = 1,
                LastScore01 = 0f,
                LastSeenRefreshIndex = -1
            };
            noticeMemory.Add(created);
            return created;
        }

        private static string ResolveLifecycleLabel(int cycles, float score01, float previousScore01, bool seenLastRefresh)
        {
            if (!seenLastRefresh || cycles <= 1)
            {
                return "New";
            }

            if (cycles >= 3 && score01 - previousScore01 >= 0.08f)
            {
                return "Worsening";
            }

            return cycles >= 3 ? "Persistent" : "Watching";
        }

        private static List<OpportunityNotice> CloneNotices(IReadOnlyList<OpportunityNotice> notices)
        {
            List<OpportunityNotice> clones = new();
            if (notices == null)
            {
                return clones;
            }

            for (int i = 0; i < notices.Count; i++)
            {
                OpportunityNotice clone = CloneNotice(notices[i]);
                if (clone != null)
                {
                    clones.Add(clone);
                }
            }

            return clones;
        }

        private static OpportunityNotice CloneNotice(OpportunityNotice notice)
        {
            if (notice == null)
            {
                return null;
            }

            OpportunityNotice clone = new(
                notice.NoticeId,
                notice.PressureType,
                notice.Severity,
                notice.Score01,
                notice.Title,
                notice.Detail,
                notice.ActionText,
                notice.SourceLabel,
                notice.DeskTarget);
            clone.ApplyRuntimeContext(notice.LifecycleLabel, notice.ConsecutiveCycles);
            return clone;
        }

        private static List<OpportunityNoticeMemory> CloneNoticeMemory(IReadOnlyList<OpportunityNoticeMemory> memory)
        {
            List<OpportunityNoticeMemory> clones = new();
            if (memory == null)
            {
                return clones;
            }

            for (int i = 0; i < memory.Count; i++)
            {
                OpportunityNoticeMemory item = memory[i];
                if (item == null || string.IsNullOrWhiteSpace(item.NoticeId))
                {
                    continue;
                }

                clones.Add(new OpportunityNoticeMemory
                {
                    NoticeId = item.NoticeId,
                    LastScore01 = item.LastScore01,
                    ConsecutiveCycles = item.ConsecutiveCycles,
                    LastSeenRefreshIndex = item.LastSeenRefreshIndex
                });
            }

            return clones;
        }

        private static string BuildHudAlertText(IReadOnlyList<OpportunityNotice> notices)
        {
            if (notices == null || notices.Count == 0)
            {
                return string.Empty;
            }

            List<OpportunityNotice> displayNotices = BuildCompactDisplayNoticeList(notices, HudNoticeLimit);
            if (displayNotices.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new("Notice Board: ");
            for (int i = 0; i < displayNotices.Count; i++)
            {
                OpportunityNotice notice = displayNotices[i];
                if (notice == null)
                {
                    continue;
                }

                if (i > 0)
                {
                    builder.Append(" | ");
                }

                builder.Append(GetPressureLabel(notice.PressureType));
                builder.Append(": ");
                builder.Append(BuildNoticeLaneLabel(notice));
                builder.Append(" - ");
                builder.Append(string.IsNullOrWhiteSpace(notice.ActionText) ? notice.Title : notice.ActionText);
                if (!string.IsNullOrWhiteSpace(notice.DeskTarget))
                {
                    builder.Append(" (");
                    builder.Append(notice.DeskTarget);
                    builder.Append(")");
                }
            }

            return builder.ToString();
        }

        private static List<OpportunityNotice> BuildCompactDisplayNoticeList(IReadOnlyList<OpportunityNotice> notices, int maxNotices)
        {
            List<OpportunityNotice> display = new();
            if (notices == null)
            {
                return display;
            }

            int limit = Mathf.Max(1, maxNotices);
            for (int i = 0; i < notices.Count && display.Count < limit; i++)
            {
                OpportunityNotice notice = notices[i];
                if (notice == null || ShouldSuppressNoticeForCompactDisplay(notice, notices))
                {
                    continue;
                }

                display.Add(notice);
            }

            // The full snapshot remains truthful; this fallback only prevents an overzealous display rule from hiding every notice.
            if (display.Count == 0)
            {
                for (int i = 0; i < notices.Count && display.Count < limit; i++)
                {
                    if (notices[i] != null)
                    {
                        display.Add(notices[i]);
                    }
                }
            }

            return display;
        }

        private static bool ShouldSuppressNoticeForCompactDisplay(OpportunityNotice notice, IReadOnlyList<OpportunityNotice> notices)
        {
            if (notice == null)
            {
                return false;
            }

            string noticeId = notice.NoticeId;
            if (string.Equals(noticeId, ImmigrationHousingNoticeId, StringComparison.OrdinalIgnoreCase)
                && ContainsNotice(notices, RentingWaitlistNoticeId))
            {
                return true;
            }

            if (string.Equals(noticeId, SchoolhouseCivicNeedNoticeId, StringComparison.OrdinalIgnoreCase)
                && ContainsNotice(notices, SchoolhouseTeacherVacancyNoticeId))
            {
                return true;
            }

            return false;
        }

        private static bool ContainsNotice(IReadOnlyList<OpportunityNotice> notices, string noticeId)
        {
            if (notices == null || string.IsNullOrWhiteSpace(noticeId))
            {
                return false;
            }

            for (int i = 0; i < notices.Count; i++)
            {
                OpportunityNotice notice = notices[i];
                if (notice != null && string.Equals(notice.NoticeId, noticeId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildExpansionAction(
            float retailPressure,
            float housingPressure,
            float laborPressure,
            float servicePressure)
        {
            float strongest = Mathf.Max(retailPressure, housingPressure, laborPressure, servicePressure);
            if (Mathf.Approximately(strongest, housingPressure))
            {
                return "Prioritize housing construction.";
            }

            if (Mathf.Approximately(strongest, servicePressure))
            {
                return "Fit out a town-facing service business.";
            }

            if (Mathf.Approximately(strongest, laborPressure))
            {
                return "Expand only where labor can be staffed.";
            }

            return "Add retail capacity or local supply.";
        }

        private static int CompareNotices(OpportunityNotice left, OpportunityNotice right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int severityCompare = right.Severity.CompareTo(left.Severity);
            if (severityCompare != 0)
            {
                return severityCompare;
            }

            int scoreCompare = right.Score01.CompareTo(left.Score01);
            if (scoreCompare != 0)
            {
                return scoreCompare;
            }

            int typeCompare = left.PressureType.CompareTo(right.PressureType);
            return typeCompare != 0
                ? typeCompare
                : string.Compare(left.NoticeId, right.NoticeId, StringComparison.OrdinalIgnoreCase);
        }

        private static OpportunityNoticeSeverity ResolveSeverity(float pressure01)
        {
            if (pressure01 >= 0.75f)
            {
                return OpportunityNoticeSeverity.Urgent;
            }

            return pressure01 >= 0.50f
                ? OpportunityNoticeSeverity.Pressure
                : OpportunityNoticeSeverity.Watch;
        }

        private static float Normalize(int value, int fullPressureValue)
        {
            if (fullPressureValue <= 0)
            {
                return value > 0 ? 1f : 0f;
            }

            return Mathf.Clamp01((float)Mathf.Max(0, value) / fullPressureValue);
        }

        private static string FormatPercent(float value01)
        {
            return $"{Mathf.RoundToInt(Mathf.Clamp01(value01) * 100f)}%";
        }

        private static string FormatMoney(int cents)
        {
            return "$" + (Mathf.Max(0, cents) / 100f).ToString("N2");
        }

        private static string BuildPressureDebugLine(OpportunityPressureSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "Pressure: unavailable";
            }

            return $"Pressure: retail {FormatPercent(snapshot.RetailPressure01)} | housing {FormatPercent(snapshot.HousingPressure01)} | labor {FormatPercent(snapshot.LaborPressure01)} | service {FormatPercent(snapshot.ServicePressure01)} | expansion {FormatPercent(snapshot.ExpansionPressure01)}";
        }

        private static string BuildNoticeLaneLabel(OpportunityNotice notice)
        {
            if (notice == null)
            {
                return "Watch";
            }

            if (notice.Severity == OpportunityNoticeSeverity.Urgent)
            {
                return "Immediate risk";
            }

            if (notice.NoticeId.StartsWith("property_opening", StringComparison.OrdinalIgnoreCase)
                || notice.NoticeId.StartsWith("expansion", StringComparison.OrdinalIgnoreCase))
            {
                return notice.Severity == OpportunityNoticeSeverity.Pressure
                    ? "Active opening"
                    : "Opening";
            }

            return notice.Severity == OpportunityNoticeSeverity.Pressure
                ? "Active pressure"
                : "Watch";
        }

        private static string BuildListingLeadDetail(AcquisitionListing listing)
        {
            if (listing == null)
            {
                return string.Empty;
            }

            string title = string.IsNullOrWhiteSpace(listing.title) ? "Unlabeled listing" : listing.title.Trim();
            string reason = string.IsNullOrWhiteSpace(listing.sourceReason)
                ? listing.reason
                : listing.sourceReason;
            string reasonRead = string.IsNullOrWhiteSpace(reason) ? "market listing" : reason.Trim();
            string siteRead = listing.kind == AcquisitionListingKind.Land
                ? $"Plot {listing.plotId:000}"
                : $"Building {listing.buildingId:000}";
            return $"{title} at {FormatMoney(listing.askingPriceCents)} | {siteRead} | pressure {FormatPercent(listing.pressure01)} | {reasonRead}.";
        }

        private static string GetPressureLabel(OpportunityPressureType type)
        {
            return type switch
            {
                OpportunityPressureType.Retail => "Retail",
                OpportunityPressureType.Housing => "Housing",
                OpportunityPressureType.Labor => "Labor",
                OpportunityPressureType.Service => "Service",
                OpportunityPressureType.Expansion => "Expansion",
                _ => "Town"
            };
        }
    }
}
