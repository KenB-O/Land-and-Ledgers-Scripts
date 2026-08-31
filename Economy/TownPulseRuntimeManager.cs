using System;
using System.Collections.Generic;
using LandLedgers.Persistence;
using LandLedgers.Time;
using LandLedgers.UI;
using UnityEngine;

namespace LandLedgers.Economy
{
    [Serializable]
    public sealed class TownPulseDemandLine
    {
        [SerializeField]
        private string categoryId = string.Empty;

        [SerializeField]
        private string displayName = string.Empty;

        [SerializeField, Min(0)]
        private int unitsPerDay;

        public TownPulseDemandLine()
        {
        }

        public TownPulseDemandLine(string categoryId, string displayName, int unitsPerDay)
        {
            this.categoryId = categoryId ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
            this.unitsPerDay = Mathf.Max(0, unitsPerDay);
        }

        public string CategoryId => categoryId ?? string.Empty;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? CategoryId : displayName;
        public int UnitsPerDay => Mathf.Max(0, unitsPerDay);
    }

    [Serializable]
    public sealed class TownPulseDefinition
    {
        [SerializeField]
        private string pulseId = string.Empty;

        [SerializeField]
        private string displayName = string.Empty;

        [SerializeField]
        private string alertDemandLabel = string.Empty;

        [SerializeField, Min(1)]
        private int durationDays = 1;

        [SerializeField]
        private TownPulseDemandLine[] demandLines = Array.Empty<TownPulseDemandLine>();

        public TownPulseDefinition()
        {
        }

        public TownPulseDefinition(
            string pulseId,
            string displayName,
            string alertDemandLabel,
            int durationDays,
            params TownPulseDemandLine[] demandLines)
        {
            this.pulseId = pulseId ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
            this.alertDemandLabel = alertDemandLabel ?? string.Empty;
            this.durationDays = Mathf.Max(1, durationDays);
            this.demandLines = demandLines ?? Array.Empty<TownPulseDemandLine>();
        }

        public string PulseId => string.IsNullOrWhiteSpace(pulseId) ? DisplayName : pulseId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? PulseId : displayName;
        public string AlertDemandLabel => string.IsNullOrWhiteSpace(alertDemandLabel) ? BuildDemandLabel() : alertDemandLabel;
        public int DurationDays => Mathf.Max(1, durationDays);
        public IReadOnlyList<TownPulseDemandLine> DemandLines => demandLines ?? Array.Empty<TownPulseDemandLine>();

        public int TotalRequestedUnitsPerDay
        {
            get
            {
                int total = 0;
                IReadOnlyList<TownPulseDemandLine> lines = DemandLines;
                for (int i = 0; i < lines.Count; i++)
                {
                    total += lines[i] != null ? lines[i].UnitsPerDay : 0;
                }

                return total;
            }
        }

        private string BuildDemandLabel()
        {
            IReadOnlyList<TownPulseDemandLine> lines = DemandLines;
            if (lines.Count == 0)
            {
                return "town demand";
            }

            List<string> labels = new();
            for (int i = 0; i < lines.Count; i++)
            {
                TownPulseDemandLine line = lines[i];
                if (line != null && !string.IsNullOrWhiteSpace(line.DisplayName))
                {
                    labels.Add(line.DisplayName);
                }
            }

            return labels.Count == 0 ? "town demand" : string.Join(" + ", labels) + " demand";
        }
    }

    [Serializable]
    public sealed class TownPulseCategoryResult
    {
        public string categoryId = string.Empty;
        public int requestedUnits;
        public int fulfilledUnits;
        public int missedUnits;

        public void Add(int requested, int fulfilled)
        {
            requestedUnits += Mathf.Max(0, requested);
            fulfilledUnits += Mathf.Max(0, fulfilled);
            missedUnits = Mathf.Max(0, requestedUnits - fulfilledUnits);
        }
    }

    [Serializable]
    public sealed class TownPulseRuntimeState
    {
        public bool active;
        public string activePulseId = string.Empty;
        public int activeDefinitionIndex = -1;
        public int activeStartDayIndex = -1;
        public int activeEndDayIndexExclusive = -1;
        public int nextPulseStartDayIndex = TownPulseRuntimeManager.FirstPulseStartDayIndex;
        public int nextDefinitionIndex;
        public int lastResolvedDayIndex = -1;
        public int lastDailyRequestedUnits;
        public int lastDailyFulfilledUnits;
        public int lastDailyMissedUnits;
        public List<TownPulseCategoryResult> lastDailyCategoryResults = new();

        public void Sanitize(int definitionCount)
        {
            int count = Mathf.Max(0, definitionCount);
            if (count <= 0)
            {
                active = false;
                activeDefinitionIndex = -1;
                nextDefinitionIndex = 0;
            }
            else
            {
                if (activeDefinitionIndex >= count)
                {
                    activeDefinitionIndex = -1;
                    active = false;
                }

                nextDefinitionIndex = WrapIndex(nextDefinitionIndex, count);
            }

            nextPulseStartDayIndex = Mathf.Max(TownPulseRuntimeManager.FirstPulseStartDayIndex, nextPulseStartDayIndex);
            if (!active)
            {
                activePulseId = string.Empty;
                activeStartDayIndex = -1;
                activeEndDayIndexExclusive = -1;
            }

            lastDailyRequestedUnits = Mathf.Max(0, lastDailyRequestedUnits);
            lastDailyFulfilledUnits = Mathf.Clamp(lastDailyFulfilledUnits, 0, lastDailyRequestedUnits);
            lastDailyMissedUnits = Mathf.Max(0, lastDailyRequestedUnits - lastDailyFulfilledUnits);
            lastDailyCategoryResults ??= new List<TownPulseCategoryResult>();
        }

        public void ResetDailyResultsForDay(int absoluteDayIndex, int requestedUnits)
        {
            lastResolvedDayIndex = absoluteDayIndex;
            lastDailyRequestedUnits = Mathf.Max(0, requestedUnits);
            lastDailyFulfilledUnits = 0;
            lastDailyMissedUnits = lastDailyRequestedUnits;
            lastDailyCategoryResults ??= new List<TownPulseCategoryResult>();
            lastDailyCategoryResults.Clear();
        }

        public void ResetDailyResults(int requestedUnits)
        {
            ResetDailyResultsForDay(lastResolvedDayIndex, requestedUnits);
        }

        public void RecalculateDailyTotals()
        {
            lastDailyRequestedUnits = Mathf.Max(0, lastDailyRequestedUnits);
            lastDailyFulfilledUnits = 0;
            if (lastDailyCategoryResults != null)
            {
                for (int i = 0; i < lastDailyCategoryResults.Count; i++)
                {
                    TownPulseCategoryResult result = lastDailyCategoryResults[i];
                    if (result != null)
                    {
                        lastDailyFulfilledUnits += Mathf.Max(0, result.fulfilledUnits);
                    }
                }
            }

            lastDailyFulfilledUnits = Mathf.Clamp(lastDailyFulfilledUnits, 0, lastDailyRequestedUnits);
            lastDailyMissedUnits = Mathf.Max(0, lastDailyRequestedUnits - lastDailyFulfilledUnits);
        }

        private static int WrapIndex(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int value = index % count;
            return value < 0 ? value + count : value;
        }
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(258)]
    public sealed class TownPulseRuntimeManager : MonoBehaviour
    {
        public const int FirstPulseStartDayIndex = 4;
        public const int PulseRotationIntervalDays = 7;
        public const int SaturdayDayOfWeekIndex = 5;

        [Header("Sources")]
        [SerializeField]
        private TimeManager timeManager;

        [SerializeField]
        private LandLedgersHUDController hudController;

        [Header("Pulse Catalog")]
        [SerializeField]
        private TownPulseDefinition[] pulseDefinitions = Array.Empty<TownPulseDefinition>();

        [Header("Runtime")]
        [SerializeField]
        private TownPulseRuntimeState runtimeState = new();

        [SerializeField, TextArea(1, 3)]
        private string currentAlertText = string.Empty;

        public TownPulseRuntimeState RuntimeState => runtimeState ??= new TownPulseRuntimeState();
        public IReadOnlyList<TownPulseDefinition> PulseDefinitions => pulseDefinitions ?? Array.Empty<TownPulseDefinition>();
        public bool HasActivePulse => RuntimeState.active;
        public string ActivePulseId => RuntimeState.activePulseId ?? string.Empty;
        public string CurrentAlertText => currentAlertText ?? string.Empty;

        public void Configure(TimeManager newTimeManager, LandLedgersHUDController newHudController)
        {
            timeManager = newTimeManager;
            hudController = newHudController;
            InitializeIfNeeded();
            RefreshAlertForCurrentDate();
        }

        public void InitializeIfNeeded()
        {
            AutoWire();
            if (pulseDefinitions == null || pulseDefinitions.Length == 0)
            {
                pulseDefinitions = CreateDefaultPulseDefinitions();
            }

            RuntimeState.Sanitize(pulseDefinitions.Length);
        }

        public IReadOnlyList<TownPulseDemandLine> BeginDailyResolution(int absoluteDayIndex)
        {
            InitializeIfNeeded();
            if (RuntimeState.lastResolvedDayIndex == absoluteDayIndex)
            {
                RefreshAlert(absoluteDayIndex);
                return Array.Empty<TownPulseDemandLine>();
            }

            EnsureScheduleForDay(absoluteDayIndex);
            TownPulseDefinition active = GetActiveDefinition();
            if (active == null)
            {
                if (IsSaturdayTradeDay(absoluteDayIndex))
                {
                    IReadOnlyList<TownPulseDemandLine> saturdayLines = GetSaturdayTradeDemandLines();
                    RuntimeState.ResetDailyResultsForDay(absoluteDayIndex, GetTotalRequestedUnits(saturdayLines));
                    RefreshAlert(absoluteDayIndex);
                    return saturdayLines;
                }

                RuntimeState.ResetDailyResultsForDay(absoluteDayIndex, 0);
                RefreshAlert(absoluteDayIndex);
                return Array.Empty<TownPulseDemandLine>();
            }

            RuntimeState.ResetDailyResultsForDay(absoluteDayIndex, active.TotalRequestedUnitsPerDay);
            RefreshAlert(absoluteDayIndex);
            return active.DemandLines;
        }

        public void RecordDailyFulfillment(string categoryId, int requestedUnits, int fulfilledUnits)
        {
            TownPulseCategoryResult result = FindOrCreateResult(categoryId);
            result.Add(requestedUnits, fulfilledUnits);
            RuntimeState.RecalculateDailyTotals();
            RefreshAlertForCurrentDate();
        }

        public bool HasDailyResultForDay(int absoluteDayIndex)
        {
            InitializeIfNeeded();
            return RuntimeState.lastResolvedDayIndex == absoluteDayIndex;
        }

        public int GetMissedUnitsForDay(int absoluteDayIndex)
        {
            return HasDailyResultForDay(absoluteDayIndex) ? Mathf.Max(0, RuntimeState.lastDailyMissedUnits) : 0;
        }

        public int GetCurrentDateMissedUnits()
        {
            return GetMissedUnitsForDay(ResolveCurrentDayIndex());
        }

        public int GetMissedUnitsForCategoryForDay(int absoluteDayIndex, string categoryNeedle)
        {
            if (!HasDailyResultForDay(absoluteDayIndex) || string.IsNullOrWhiteSpace(categoryNeedle))
            {
                return 0;
            }

            string needle = categoryNeedle.Trim();
            List<TownPulseCategoryResult> results = RuntimeState.lastDailyCategoryResults;
            if (results == null)
            {
                return 0;
            }

            int missed = 0;
            for (int i = 0; i < results.Count; i++)
            {
                TownPulseCategoryResult result = results[i];
                if (result == null || string.IsNullOrWhiteSpace(result.categoryId))
                {
                    continue;
                }

                if (result.categoryId.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    missed += Mathf.Max(0, result.missedUnits);
                }
            }

            return missed;
        }

        public int GetCurrentDateMissedUnitsForCategory(string categoryNeedle)
        {
            return GetMissedUnitsForCategoryForDay(ResolveCurrentDayIndex(), categoryNeedle);
        }


        public void CompleteDailyResolution(int absoluteDayIndex)
        {
            RuntimeState.lastResolvedDayIndex = absoluteDayIndex;
            RuntimeState.RecalculateDailyTotals();
            RefreshAlert(absoluteDayIndex);
        }

        public TownPulseSaveDto CaptureSaveDto()
        {
            RuntimeState.Sanitize(PulseDefinitions.Count);
            TownPulseSaveDto dto = new()
            {
                active = RuntimeState.active,
                activePulseId = RuntimeState.activePulseId,
                activeDefinitionIndex = RuntimeState.activeDefinitionIndex,
                activeStartDayIndex = RuntimeState.activeStartDayIndex,
                activeEndDayIndexExclusive = RuntimeState.activeEndDayIndexExclusive,
                nextPulseStartDayIndex = RuntimeState.nextPulseStartDayIndex,
                nextDefinitionIndex = RuntimeState.nextDefinitionIndex,
                lastResolvedDayIndex = RuntimeState.lastResolvedDayIndex,
                lastDailyRequestedUnits = RuntimeState.lastDailyRequestedUnits,
                lastDailyFulfilledUnits = RuntimeState.lastDailyFulfilledUnits,
                lastDailyMissedUnits = RuntimeState.lastDailyMissedUnits
            };

            if (RuntimeState.lastDailyCategoryResults != null)
            {
                for (int i = 0; i < RuntimeState.lastDailyCategoryResults.Count; i++)
                {
                    TownPulseCategoryResult result = RuntimeState.lastDailyCategoryResults[i];
                    if (result == null)
                    {
                        continue;
                    }

                    dto.lastDailyCategoryResults.Add(new TownPulseCategoryResultSaveDto
                    {
                        categoryId = result.categoryId,
                        requestedUnits = Mathf.Max(0, result.requestedUnits),
                        fulfilledUnits = Mathf.Max(0, result.fulfilledUnits),
                        missedUnits = Mathf.Max(0, result.missedUnits)
                    });
                }
            }

            return dto;
        }

        public void LoadFromSaveDto(TownPulseSaveDto dto)
        {
            InitializeIfNeeded();
            runtimeState = new TownPulseRuntimeState();
            if (dto != null)
            {
                runtimeState.active = dto.active;
                runtimeState.activePulseId = dto.activePulseId ?? string.Empty;
                runtimeState.activeDefinitionIndex = dto.activeDefinitionIndex;
                runtimeState.activeStartDayIndex = dto.activeStartDayIndex;
                runtimeState.activeEndDayIndexExclusive = dto.activeEndDayIndexExclusive;
                runtimeState.nextPulseStartDayIndex = dto.nextPulseStartDayIndex;
                runtimeState.nextDefinitionIndex = dto.nextDefinitionIndex;
                runtimeState.lastResolvedDayIndex = dto.lastResolvedDayIndex;
                runtimeState.lastDailyRequestedUnits = Mathf.Max(0, dto.lastDailyRequestedUnits);
                runtimeState.lastDailyFulfilledUnits = Mathf.Max(0, dto.lastDailyFulfilledUnits);
                runtimeState.lastDailyMissedUnits = Mathf.Max(0, dto.lastDailyMissedUnits);
                runtimeState.lastDailyCategoryResults.Clear();
                if (dto.lastDailyCategoryResults != null)
                {
                    for (int i = 0; i < dto.lastDailyCategoryResults.Count; i++)
                    {
                        TownPulseCategoryResultSaveDto saved = dto.lastDailyCategoryResults[i];
                        if (saved == null)
                        {
                            continue;
                        }

                        runtimeState.lastDailyCategoryResults.Add(new TownPulseCategoryResult
                        {
                            categoryId = saved.categoryId ?? string.Empty,
                            requestedUnits = Mathf.Max(0, saved.requestedUnits),
                            fulfilledUnits = Mathf.Max(0, saved.fulfilledUnits),
                            missedUnits = Mathf.Max(0, saved.missedUnits)
                        });
                    }
                }
            }

            ResolveActiveDefinitionIndexFromId();
            RuntimeState.Sanitize(PulseDefinitions.Count);
            RefreshAlertForCurrentDate();
        }

        public void RefreshAlertForCurrentDate()
        {
            InitializeIfNeeded();
            int dayIndex = ResolveCurrentDayIndex();
            EnsureScheduleForDay(dayIndex);
            RefreshAlert(dayIndex);
        }

        public static TownPulseDefinition[] CreateDefaultPulseDefinitions()
        {
            return new[]
            {
                new TownPulseDefinition(
                    "repair_rush",
                    "Repair Rush",
                    "Tools / Hardware demand",
                    3,
                    new TownPulseDemandLine("tools_hardware", "Tools / Hardware", 3)),
                new TownPulseDefinition(
                    "boarding_house_bulk_order",
                    "Boarding-House Bulk Order",
                    "Staple Food + Meat demand",
                    2,
                    new TownPulseDemandLine("staple_food", "Staple Food", 6),
                    new TownPulseDemandLine("meat", "Meat", 2)),
                new TownPulseDefinition(
                    "mild_illness_wave",
                    "Mild Illness Wave",
                    "Medicine / Remedies demand",
                    3,
                    new TownPulseDemandLine("medicine_remedies", "Medicine / Remedies", 3))
            };
        }

        private void Awake()
        {
            InitializeIfNeeded();
        }

        private void OnEnable()
        {
            InitializeIfNeeded();
            RefreshAlertForCurrentDate();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (pulseDefinitions == null || pulseDefinitions.Length == 0)
            {
                pulseDefinitions = CreateDefaultPulseDefinitions();
            }

            runtimeState ??= new TownPulseRuntimeState();
            runtimeState.Sanitize(pulseDefinitions.Length);
        }
#endif

        private void AutoWire()
        {
            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            hudController ??= FindAnyObjectByType<LandLedgersHUDController>();
        }

        private void EnsureScheduleForDay(int absoluteDayIndex)
        {
            if (PulseDefinitions.Count == 0)
            {
                RuntimeState.active = false;
                return;
            }

            if (RuntimeState.active && absoluteDayIndex >= RuntimeState.activeEndDayIndexExclusive)
            {
                RuntimeState.active = false;
                RuntimeState.activePulseId = string.Empty;
                RuntimeState.activeDefinitionIndex = -1;
                RuntimeState.activeStartDayIndex = -1;
                RuntimeState.activeEndDayIndexExclusive = -1;
            }

            if (!RuntimeState.active && absoluteDayIndex >= RuntimeState.nextPulseStartDayIndex)
            {
                StartPulse(absoluteDayIndex);
            }
        }

        private void StartPulse(int absoluteDayIndex)
        {
            int definitionIndex = WrapIndex(RuntimeState.nextDefinitionIndex, PulseDefinitions.Count);
            TownPulseDefinition definition = PulseDefinitions[definitionIndex];
            RuntimeState.active = definition != null;
            RuntimeState.activeDefinitionIndex = RuntimeState.active ? definitionIndex : -1;
            RuntimeState.activePulseId = definition != null ? definition.PulseId : string.Empty;
            RuntimeState.activeStartDayIndex = RuntimeState.active ? absoluteDayIndex : -1;
            RuntimeState.activeEndDayIndexExclusive = definition != null ? absoluteDayIndex + definition.DurationDays : -1;
            RuntimeState.nextDefinitionIndex = WrapIndex(definitionIndex + 1, PulseDefinitions.Count);
            RuntimeState.nextPulseStartDayIndex = absoluteDayIndex + PulseRotationIntervalDays;
        }

        private TownPulseDefinition GetActiveDefinition()
        {
            if (!RuntimeState.active || PulseDefinitions.Count == 0)
            {
                return null;
            }

            int index = RuntimeState.activeDefinitionIndex;
            if (index < 0 || index >= PulseDefinitions.Count)
            {
                ResolveActiveDefinitionIndexFromId();
                index = RuntimeState.activeDefinitionIndex;
            }

            return index >= 0 && index < PulseDefinitions.Count ? PulseDefinitions[index] : null;
        }

        private void ResolveActiveDefinitionIndexFromId()
        {
            if (!RuntimeState.active || string.IsNullOrWhiteSpace(RuntimeState.activePulseId))
            {
                return;
            }

            for (int i = 0; i < PulseDefinitions.Count; i++)
            {
                TownPulseDefinition definition = PulseDefinitions[i];
                if (definition != null && string.Equals(definition.PulseId, RuntimeState.activePulseId, StringComparison.OrdinalIgnoreCase))
                {
                    RuntimeState.activeDefinitionIndex = i;
                    return;
                }
            }
        }

        private TownPulseCategoryResult FindOrCreateResult(string categoryId)
        {
            RuntimeState.lastDailyCategoryResults ??= new List<TownPulseCategoryResult>();
            string key = categoryId ?? string.Empty;
            for (int i = 0; i < RuntimeState.lastDailyCategoryResults.Count; i++)
            {
                TownPulseCategoryResult result = RuntimeState.lastDailyCategoryResults[i];
                if (result != null && string.Equals(result.categoryId, key, StringComparison.OrdinalIgnoreCase))
                {
                    return result;
                }
            }

            TownPulseCategoryResult created = new()
            {
                categoryId = key
            };
            RuntimeState.lastDailyCategoryResults.Add(created);
            return created;
        }

        private void RefreshAlert(int absoluteDayIndex)
        {
            currentAlertText = BuildAlertText(absoluteDayIndex);
            hudController?.SetTownPulseAlert(currentAlertText);
        }

        private string BuildAlertText(int absoluteDayIndex)
        {
            TownPulseDefinition definition = GetActiveDefinition();
            if (definition == null)
            {
                if (IsSaturdayTradeDay(absoluteDayIndex))
                {
                    bool hasSaturdayResult = HasDailyResultForDay(absoluteDayIndex);
                    int saturdayRequested = hasSaturdayResult && RuntimeState.lastDailyRequestedUnits > 0
                        ? RuntimeState.lastDailyRequestedUnits
                        : GetTotalRequestedUnits(GetSaturdayTradeDemandLines());
                    int saturdayFulfilled = hasSaturdayResult
                        ? Mathf.Clamp(RuntimeState.lastDailyFulfilledUnits, 0, saturdayRequested)
                        : 0;
                    return $"Saturday trade: provisions and household goods under pressure | today {saturdayFulfilled}/{saturdayRequested}";
                }

                return string.Empty;
            }

            int daysRemaining = Mathf.Max(1, RuntimeState.activeEndDayIndexExclusive - Mathf.Max(RuntimeState.activeStartDayIndex, absoluteDayIndex));
            bool hasTodayResult = HasDailyResultForDay(absoluteDayIndex);
            int requested = hasTodayResult && RuntimeState.lastDailyRequestedUnits > 0
                ? RuntimeState.lastDailyRequestedUnits
                : definition.TotalRequestedUnitsPerDay;
            int fulfilled = hasTodayResult
                ? Mathf.Clamp(RuntimeState.lastDailyFulfilledUnits, 0, requested)
                : 0;
            string dayLabel = daysRemaining == 1 ? "1 day" : $"{daysRemaining} days";
            return $"Town Pulse: {definition.DisplayName} - {definition.AlertDemandLabel} for {dayLabel} | today {fulfilled}/{requested}";
        }

        private int ResolveCurrentDayIndex()
        {
            if (timeManager != null)
            {
                return timeManager.CurrentDate.AbsoluteDayIndex;
            }

            if (RuntimeState.lastResolvedDayIndex >= 0)
            {
                return RuntimeState.lastResolvedDayIndex;
            }

            return RuntimeState.activeStartDayIndex >= 0
                ? RuntimeState.activeStartDayIndex
                : RuntimeState.nextPulseStartDayIndex;
        }

        private static bool IsSaturdayTradeDay(int absoluteDayIndex)
        {
            return Mathf.Max(0, absoluteDayIndex) % 7 == SaturdayDayOfWeekIndex;
        }

        private static IReadOnlyList<TownPulseDemandLine> GetSaturdayTradeDemandLines()
        {
            return new[]
            {
                new TownPulseDemandLine("staple_food", "Staple Food", 5),
                new TownPulseDemandLine("meat", "Meat", 2),
                new TownPulseDemandLine("household_goods", "Household Goods", 3)
            };
        }

        private static int GetTotalRequestedUnits(IReadOnlyList<TownPulseDemandLine> lines)
        {
            int total = 0;
            if (lines == null)
            {
                return 0;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                total += lines[i] != null ? lines[i].UnitsPerDay : 0;
            }

            return Mathf.Max(0, total);
        }

        private static int WrapIndex(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int value = index % count;
            return value < 0 ? value + count : value;
        }
    }
}
