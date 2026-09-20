using System;
using System.Collections.Generic;
using LandLedgers.Persistence;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy
{
    public enum ConstructionResourceKind
    {
        Lumber = 0,
        Nails = 1,
        Labor = 2
    }

    public enum ConstructionProjectKind
    {
        BuildingShell = 0,
        BusinessFitOut = 1,
        HouseholdUpgrade = 2
    }

    [Serializable]
    public sealed class ConstructionResourceSourceAllocation
    {
        public BusinessType businessType;
        public string businessInstanceId = string.Empty;
        public string sourceLabel = string.Empty;
        public int allocatedUnits;
        public int unitCostCents;
        public int sellerScore;

        public int TotalCostCents => Mathf.Max(0, allocatedUnits) * Mathf.Max(0, unitCostCents);
        public bool HasBusinessSeller => !string.IsNullOrWhiteSpace(businessInstanceId);

        public static ConstructionResourceSourceAllocation Create(
            BusinessType businessType,
            string businessInstanceId,
            string sourceLabel,
            int allocatedUnits,
            int unitCostCents,
            int sellerScore)
        {
            return new ConstructionResourceSourceAllocation
            {
                businessType = businessType,
                businessInstanceId = businessInstanceId ?? string.Empty,
                sourceLabel = sourceLabel ?? string.Empty,
                allocatedUnits = Mathf.Max(0, allocatedUnits),
                unitCostCents = Mathf.Max(0, unitCostCents),
                sellerScore = sellerScore
            };
        }
    }

    [Serializable]
    public sealed class ConstructionResourceQuoteLine
    {
        public ConstructionResourceKind resourceKind;
        public string displayName = string.Empty;
        public int requiredUnits;
        public int availableUnits;
        public int unitCostCents;
        public string sourceLabel = string.Empty;
        public string procurementNote = string.Empty;
        public int procurementLeadWeeks;
        public List<ConstructionResourceSourceAllocation> sourceAllocations = new();

        public int MissingUnits => Mathf.Max(0, requiredUnits - availableUnits);
        public bool Available => MissingUnits <= 0;
        public bool HasSourceAllocations => sourceAllocations != null && sourceAllocations.Count > 0;

        public static ConstructionResourceQuoteLine Create(
            ConstructionResourceKind resourceKind,
            string displayName,
            int requiredUnits,
            int availableUnits,
            int unitCostCents,
            string sourceLabel)
        {
            return Create(resourceKind, displayName, requiredUnits, availableUnits, unitCostCents, sourceLabel, null);
        }

        public static ConstructionResourceQuoteLine Create(
            ConstructionResourceKind resourceKind,
            string displayName,
            int requiredUnits,
            int availableUnits,
            int unitCostCents,
            string sourceLabel,
            List<ConstructionResourceSourceAllocation> sourceAllocations)
        {
            return new ConstructionResourceQuoteLine
            {
                resourceKind = resourceKind,
                displayName = string.IsNullOrWhiteSpace(displayName) ? resourceKind.ToString() : displayName,
                requiredUnits = Mathf.Max(0, requiredUnits),
                availableUnits = Mathf.Max(0, availableUnits),
                unitCostCents = Mathf.Max(0, unitCostCents),
                sourceLabel = sourceLabel ?? string.Empty,
                procurementNote = string.Empty,
                procurementLeadWeeks = 0,
                sourceAllocations = sourceAllocations != null
                    ? new List<ConstructionResourceSourceAllocation>(sourceAllocations)
                    : new List<ConstructionResourceSourceAllocation>()
            };
        }
    }

    [Serializable]
    public sealed class ConstructionInputQuote
    {
        public ConstructionProjectKind projectKind;
        public string projectLabel = string.Empty;
        public int cashCostCents;
        public int availableCashCents;
        public string cashSourceLabel = "Player cash";
        public string blockReason = string.Empty;
        public List<ConstructionResourceQuoteLine> resources = new();

        public int CashCostCents => Mathf.Max(0, cashCostCents);
        public int AvailableCashCents => Mathf.Max(0, availableCashCents);
        public int CashMissingCents => Mathf.Max(0, cashCostCents - availableCashCents);
        public bool HasEnoughCash => CashMissingCents <= 0;
        public bool InputsAvailable
        {
            get
            {
                for (int i = 0; i < resources.Count; i++)
                {
                    if (resources[i] != null && !resources[i].Available)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public bool CanProceed => HasEnoughCash && InputsAvailable && string.IsNullOrWhiteSpace(blockReason);

        public ConstructionResourceQuoteLine GetLine(ConstructionResourceKind kind)
        {
            for (int i = 0; i < resources.Count; i++)
            {
                if (resources[i] != null && resources[i].resourceKind == kind)
                {
                    return resources[i];
                }
            }

            return null;
        }

        public string BuildMissingSummary()
        {
            List<string> missing = new();
            if (!string.IsNullOrWhiteSpace(blockReason))
            {
                missing.Add(blockReason);
            }

            if (!HasEnoughCash)
            {
                missing.Add($"cash short {FormatMoney(CashMissingCents)}");
            }

            for (int i = 0; i < resources.Count; i++)
            {
                ConstructionResourceQuoteLine line = resources[i];
                if (line != null && line.MissingUnits > 0)
                {
                    string source = string.IsNullOrWhiteSpace(line.sourceLabel)
                        ? string.Empty
                        : $" ({line.sourceLabel})";
                    missing.Add($"{line.displayName} short {line.MissingUnits}{source}");
                }
            }

            return missing.Count == 0 ? "none" : string.Join("; ", missing);
        }

        private static string FormatMoney(int cents)
        {
            return "$" + (cents / 100f).ToString("N2");
        }
    }

    [CreateAssetMenu(
        fileName = "ConstructionSupportNodeDefinition",
        menuName = "Land & Ledgers/Economy/Construction Support Node",
        order = 183)]
    public sealed class ConstructionSupportNodeDefinition : ScriptableObject
    {
        public const int DefaultSawmillStandingTimberUnits = 80;
        public const int DefaultSawmillLogStockUnits = 8;
        public const int DefaultSawmillWeeklyCutCapacityLogs = 10;
        public const int DefaultSawmillWeeklySawCapacityLogs = 10;
        public const int DefaultSawmillLumberPerLog = 4;
        public const int DefaultSawmillLumberStorageCapacityUnits = 360;
        public const int DefaultSawmillHardwareMaintenanceUnitsPerWeek = 1;
        public const int DefaultSawmillHiredHaulingCostPerLogCents = 35;

        [SerializeField]
        private string nodeId = "local_sawmill";

        [SerializeField]
        private string displayName = "Local Sawmill";

        [SerializeField]
        private ConstructionResourceKind resourceKind = ConstructionResourceKind.Lumber;

        [SerializeField, Min(0)]
        private int startingStockUnits = 240;

        [SerializeField, Min(0)]
        private int unitCostCents = 85;

        [SerializeField]
        private bool active = true;

        [Header("Remote Sawmill Production")]
        [SerializeField]
        private bool remoteProductionEnabled = true;

        [SerializeField, Min(0)]
        private int startingStandingTimberUnits = DefaultSawmillStandingTimberUnits;

        [SerializeField, Min(0)]
        private int startingLogStockUnits = DefaultSawmillLogStockUnits;

        [SerializeField, Min(0)]
        private int weeklyCutCapacityLogs = DefaultSawmillWeeklyCutCapacityLogs;

        [SerializeField, Min(0)]
        private int weeklySawCapacityLogs = DefaultSawmillWeeklySawCapacityLogs;

        [SerializeField, Min(1)]
        private int lumberPerLog = DefaultSawmillLumberPerLog;

        [SerializeField, Min(0)]
        private int lumberStorageCapacityUnits = DefaultSawmillLumberStorageCapacityUnits;

        [SerializeField, Min(0)]
        private int hardwareMaintenanceUnitsPerWeek = DefaultSawmillHardwareMaintenanceUnitsPerWeek;

        [SerializeField]
        private bool hasWorkerCabins;

        public string NodeId => string.IsNullOrWhiteSpace(nodeId) ? name : nodeId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? NodeId : displayName;
        public ConstructionResourceKind ResourceKind => resourceKind;
        public int StartingStockUnits => Mathf.Max(0, startingStockUnits);
        public int UnitCostCents => Mathf.Max(0, unitCostCents);
        public bool Active => active;
        public bool RemoteProductionEnabled => remoteProductionEnabled && resourceKind == ConstructionResourceKind.Lumber;
        public int StartingStandingTimberUnits => Mathf.Max(0, startingStandingTimberUnits);
        public int StartingLogStockUnits => Mathf.Max(0, startingLogStockUnits);
        public int WeeklyCutCapacityLogs => Mathf.Max(0, weeklyCutCapacityLogs);
        public int WeeklySawCapacityLogs => Mathf.Max(0, weeklySawCapacityLogs);
        public int LumberPerLog => Mathf.Max(1, lumberPerLog);
        public int LumberStorageCapacityUnits => Mathf.Max(0, lumberStorageCapacityUnits);
        public int HardwareMaintenanceUnitsPerWeek => Mathf.Max(0, hardwareMaintenanceUnitsPerWeek);
        public bool HasWorkerCabins => hasWorkerCabins;

        public string BuildRemoteProductionSummary()
        {
            if (!RemoteProductionEnabled)
            {
                return $"{DisplayName}: local stock {StartingStockUnits} units at ${UnitCostCents / 100f:N2} per unit.";
            }

            return $"{DisplayName}: timber {StartingStandingTimberUnits}, log stock {StartingLogStockUnits}, weekly cut {WeeklyCutCapacityLogs}, weekly saw {WeeklySawCapacityLogs}, lumber/log {LumberPerLog}, storage {LumberStorageCapacityUnits}, hardware upkeep {HardwareMaintenanceUnitsPerWeek}, hired hauling ${DefaultSawmillHiredHaulingCostPerLogCents / 100f:N2}/log, worker cabins {(HasWorkerCabins ? "present" : "not present")}.";
        }

        public string BuildConstructionTradeReadSummary()
        {
            if (resourceKind == ConstructionResourceKind.Lumber)
            {
                return $"Typical supply path: saw into lumber, protect storage, and support projects from local stock before freight fallback.";
            }

            return $"Construction support: {DisplayName} keeps {ResourceKind} available for projects and repairs.";
        }

        public string BuildProjectReadinessSummary(int typicalHouseWeeks, int typicalBusinessWeeks, int typicalRepairWeeks)
        {
            int houseWeeks = Mathf.Max(1, typicalHouseWeeks);
            int businessWeeks = Mathf.Max(1, typicalBusinessWeeks);
            int repairWeeks = Mathf.Max(1, typicalRepairWeeks);
            return $"Builder read: house ~{houseWeeks} week(s), business ~{businessWeeks} week(s), repair ~{repairWeeks} week(s). {BuildConstructionTradeReadSummary()}";
        }

        public string BuildBuilderAvailabilitySummary(TownGenerationSettings settings)
        {
            if (settings == null)
            {
                return HasWorkerCabins
                    ? "Builder availability unknown. Worker houses are present on site."
                    : "Builder availability unknown. Crew likely commutes from town.";
            }

            string builder = settings.BuildCarpenterAvailabilitySummary();
            string crew = HasWorkerCabins ? "Worker houses are present on site." : "Crew likely commutes from town.";
            return builder + " " + crew;
        }

        public string BuildProjectReadinessSummary(TownGenerationSettings settings, bool businessSite)
        {
            if (settings == null)
            {
                return BuildProjectReadinessSummary(2, 3, 1);
            }

            int weeks = businessSite ? settings.GetTypicalBusinessBuildWeeks() : settings.GetTypicalHouseBuildWeeks();
            string label = businessSite ? "Business build" : "House build";
            return $"{label}: typical {weeks} week(s). {BuildBuilderAvailabilitySummary(settings)} {BuildConstructionTradeReadSummary()}";
        }
    }

    [Serializable]
    public sealed class ConstructionSupportNodeState
    {
        [SerializeField]
        private string nodeId = "local_sawmill";

        [SerializeField]
        private string displayName = "Local Sawmill";

        [SerializeField]
        private ConstructionResourceKind resourceKind = ConstructionResourceKind.Lumber;

        [SerializeField, Min(0)]
        private int currentStockUnits = 240;

        [SerializeField, Min(0)]
        private int unitCostCents = 85;

        [SerializeField]
        private bool active = true;

        [SerializeField]
        private bool remoteProductionEnabled = true;

        [SerializeField]
        private bool remoteProductionInitialized = true;

        [SerializeField]
        private int remotePropertyPlotId = -1;

        [SerializeField, Min(0)]
        private int standingTimberUnits = ConstructionSupportNodeDefinition.DefaultSawmillStandingTimberUnits;

        [SerializeField, Min(0)]
        private int logStockUnits = ConstructionSupportNodeDefinition.DefaultSawmillLogStockUnits;

        [SerializeField, Min(0)]
        private int weeklyCutCapacityLogs = ConstructionSupportNodeDefinition.DefaultSawmillWeeklyCutCapacityLogs;

        [SerializeField, Min(0)]
        private int weeklySawCapacityLogs = ConstructionSupportNodeDefinition.DefaultSawmillWeeklySawCapacityLogs;

        [SerializeField, Min(1)]
        private int lumberPerLog = ConstructionSupportNodeDefinition.DefaultSawmillLumberPerLog;

        [SerializeField, Min(0)]
        private int lumberStorageCapacityUnits = ConstructionSupportNodeDefinition.DefaultSawmillLumberStorageCapacityUnits;

        [SerializeField, Min(0)]
        private int hardwareMaintenanceUnitsPerWeek = ConstructionSupportNodeDefinition.DefaultSawmillHardwareMaintenanceUnitsPerWeek;

        [SerializeField]
        private bool hasWorkerCabins;

        [SerializeField]
        private bool internalHaulingOwned = true;

        [SerializeField]
        private bool hiredHaulingFallbackAllowed = true;

        [SerializeField, Min(0)]
        private int hiredHaulingCostPerLogCents = ConstructionSupportNodeDefinition.DefaultSawmillHiredHaulingCostPerLogCents;

        [SerializeField, Min(0)]
        private int lastWeeklyTimberCutUnits;

        [SerializeField, Min(0)]
        private int lastWeeklyLogsProcessedUnits;

        [SerializeField, Min(0)]
        private int lastWeeklyLumberProducedUnits;

        [SerializeField, Min(0)]
        private int lastWeeklySlabsOffcutsProducedUnits;

        [SerializeField, Min(0)]
        private int lastWeeklySlabsOffcutsSoldUnits;

        [SerializeField, Min(0)]
        private int lastWeeklyLogsHauledUnits;

        [SerializeField, Min(0)]
        private int lastWeeklyHaulingCostCents;

        [SerializeField]
        private string lastWeeklyHaulingMode = string.Empty;

        [SerializeField]
        private int lastResolvedSawmillWeekKey = -1;

        [SerializeField]
        private string lastWeeklyProductionSummary = string.Empty;

        [SerializeField]
        private string lastWeeklyBlockedReason = string.Empty;

        [SerializeField, Min(0)]
        private int lastProjectConstraintScore;

        [SerializeField, Min(0)]
        private int blockedProjectReviewCount;

        [SerializeField, Min(0)]
        private int readyProjectReviewCount;

        [SerializeField]
        private string lastWeeklyProjectReviewSummary = string.Empty;

        public string NodeId => string.IsNullOrWhiteSpace(nodeId) ? displayName : nodeId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? NodeId : displayName;
        public ConstructionResourceKind ResourceKind => resourceKind;
        public int CurrentStockUnits => Mathf.Max(0, currentStockUnits);
        public int UnitCostCents => Mathf.Max(0, unitCostCents);
        public bool Active => active;
        public bool RemoteProductionEnabled => active && remoteProductionEnabled && resourceKind == ConstructionResourceKind.Lumber;
        public int RemotePropertyPlotId => remotePropertyPlotId;
        public int StandingTimberUnits => Mathf.Max(0, standingTimberUnits);
        public int LogStockUnits => Mathf.Max(0, logStockUnits);
        public int WeeklyCutCapacityLogs => Mathf.Max(0, weeklyCutCapacityLogs);
        public int WeeklySawCapacityLogs => Mathf.Max(0, weeklySawCapacityLogs);
        public int LumberPerLog => Mathf.Max(1, lumberPerLog);
        public int LumberStorageCapacityUnits => Mathf.Max(CurrentStockUnits, lumberStorageCapacityUnits);
        public int HardwareMaintenanceUnitsPerWeek => Mathf.Max(0, hardwareMaintenanceUnitsPerWeek);
        public bool HasWorkerCabins => hasWorkerCabins;

        public string BuildRemoteProductionSummary()
        {
            if (!RemoteProductionEnabled)
            {
                return $"{DisplayName}: local stock {CurrentStockUnits} units at ${UnitCostCents / 100f:N2} per unit.";
            }

            return $"{DisplayName}: timber {StandingTimberUnits}, log stock {LogStockUnits}, weekly cut {WeeklyCutCapacityLogs}, weekly saw {WeeklySawCapacityLogs}, lumber/log {LumberPerLog}, storage {LumberStorageCapacityUnits}, hardware upkeep {HardwareMaintenanceUnitsPerWeek}, hired hauling ${hiredHaulingCostPerLogCents / 100f:N2}/log, worker cabins {(HasWorkerCabins ? "present" : "not present")}.";
        }

        public string BuildConstructionTradeReadSummary()
        {
            if (resourceKind == ConstructionResourceKind.Lumber)
            {
                return $"Typical supply path: saw into lumber, protect storage, and support projects from local stock before freight fallback.";
            }

            return $"Construction support: {DisplayName} keeps {ResourceKind} available for projects and repairs.";
        }

        public string BuildProjectReadinessSummary(int typicalHouseWeeks, int typicalBusinessWeeks, int typicalRepairWeeks)
        {
            int houseWeeks = Mathf.Max(1, typicalHouseWeeks);
            int businessWeeks = Mathf.Max(1, typicalBusinessWeeks);
            int repairWeeks = Mathf.Max(1, typicalRepairWeeks);
            return $"Builder read: house ~{houseWeeks} week(s), business ~{businessWeeks} week(s), repair ~{repairWeeks} week(s). {BuildConstructionTradeReadSummary()}";
        }

        public string BuildBuilderAvailabilitySummary(TownGenerationSettings settings)
        {
            if (settings == null)
            {
                return HasWorkerCabins
                    ? "Builder availability unknown. Worker houses are present on site."
                    : "Builder availability unknown. Crew likely commutes from town.";
            }

            string builder = settings.BuildCarpenterAvailabilitySummary();
            string crew = HasWorkerCabins ? "Worker houses are present on site." : "Crew likely commutes from town.";
            return builder + " " + crew;
        }

        public string BuildProjectReadinessSummary(TownGenerationSettings settings, bool businessSite)
        {
            if (settings == null)
            {
                return BuildProjectReadinessSummary(2, 3, 1);
            }

            int weeks = businessSite ? settings.GetTypicalBusinessBuildWeeks() : settings.GetTypicalHouseBuildWeeks();
            string label = businessSite ? "Business build" : "House build";
            string hauling = internalHaulingOwned
                ? "Owned hauling should keep material flow steadier."
                : hiredHaulingFallbackAllowed
                    ? $"Hired hauling fallback about ${HiredHaulingCostPerLogCents / 100f:N2} per log."
                    : "Material hauling looks exposed.";
            string blocker = string.IsNullOrWhiteSpace(LastWeeklyBlockedReason)
                ? string.Empty
                : $" Last weekly blocker: {LastWeeklyBlockedReason}.";
            return $"{label}: typical {weeks} week(s). {BuildBuilderAvailabilitySummary(settings)} Lumber on hand {CurrentStockUnits}. {hauling}{blocker}";
        }

        public bool IsProjectLikelyStartReady(TownGenerationSettings settings, int requiredLumberUnits, int queueProjectsAhead, out string reason)
        {
            int required = Mathf.Max(0, requiredLumberUnits);
            int queue = Mathf.Max(0, queueProjectsAhead);
            bool materialTight = required > 0 && CurrentStockUnits < required;
            bool blocked = !string.IsNullOrWhiteSpace(LastWeeklyBlockedReason);
            bool builderThin = settings != null && !settings.guaranteeLocalCarpenter && queue > 0;
            bool ready = !materialTight && !blocked && !builderThin;

            if (ready)
            {
                reason = "Ready to start under current local builder and material conditions.";
                return true;
            }

            if (blocked)
            {
                reason = $"Delayed by last weekly blocker: {LastWeeklyBlockedReason}.";
                return false;
            }

            if (materialTight)
            {
                reason = $"Material-tight start. Needs about {required} lumber; only {CurrentStockUnits} on hand.";
                return false;
            }

            reason = "Builder capacity is thin and current queue pressure suggests delay.";
            return false;
        }

        public string BuildWeeklyConstructionReviewSummary(TownGenerationSettings settings, BuildingDefinition definition, int queueProjectsAhead, int requiredLumberUnits)
        {
            bool business = definition == null || definition.CanHostWorkplace;
            bool ready = IsProjectLikelyStartReady(settings, requiredLumberUnits, queueProjectsAhead, out string reason);
            bool materialTight = requiredLumberUnits > 0 && CurrentStockUnits < requiredLumberUnits;
            string queueRead = settings != null
                ? settings.BuildProjectQueueSummary(definition, queueProjectsAhead, materialTight)
                : BuildProjectReadinessSummary(2, 3, 1);
            string status = ready ? "Project stance: ready." : "Project stance: delayed.";
            string streak = BlockedProjectReviewCount > 0
                ? $"Blocked review streak {BlockedProjectReviewCount}."
                : ReadyProjectReviewCount > 0
                    ? $"Ready review streak {ReadyProjectReviewCount}."
                    : string.Empty;
            return status + " " + queueRead + " " + BuildProjectReadinessSummary(settings, business) + " " + reason + (string.IsNullOrWhiteSpace(streak) ? string.Empty : " " + streak);
        }

        public string ApplyWeeklyProjectReview(TownGenerationSettings settings, BuildingDefinition definition, int queueProjectsAhead, int requiredLumberUnits)
        {
            int score = EvaluateProjectConstraintScore(settings, definition, queueProjectsAhead, requiredLumberUnits);
            bool ready = IsProjectLikelyStartReady(settings, requiredLumberUnits, queueProjectsAhead, out string reason);
            lastProjectConstraintScore = Mathf.Max(0, score);
            if (ready)
            {
                readyProjectReviewCount = Mathf.Max(0, readyProjectReviewCount) + 1;
                blockedProjectReviewCount = 0;
                lastWeeklyBlockedReason = string.Empty;
            }
            else
            {
                blockedProjectReviewCount = Mathf.Max(0, blockedProjectReviewCount) + 1;
                readyProjectReviewCount = 0;
                lastWeeklyBlockedReason = reason ?? string.Empty;
            }

            lastWeeklyProjectReviewSummary = BuildWeeklyConstructionReviewSummary(settings, definition, queueProjectsAhead, requiredLumberUnits)
                + $" Constraint score {lastProjectConstraintScore}.";
            return LastWeeklyProjectReviewSummary;
        }

        public int EvaluateProjectConstraintScore(TownGenerationSettings settings, BuildingDefinition definition, int queueProjectsAhead, int requiredLumberUnits)
        {
            int score = 0;
            int required = Mathf.Max(0, requiredLumberUnits);
            if (required > 0 && CurrentStockUnits < required)
            {
                score += 3;
            }

            if (!string.IsNullOrWhiteSpace(LastWeeklyBlockedReason))
            {
                score += 2;
            }

            int queue = Mathf.Max(0, queueProjectsAhead);
            score += Mathf.Min(2, queue);

            if (settings != null && !settings.guaranteeLocalCarpenter && queue > 0)
            {
                score += 1;
            }

            if (definition != null && definition.CanHostWorkplace)
            {
                score += 1;
            }

            return Mathf.Max(0, score);
        }

        public string BuildProjectStartAuthoritySummary(TownGenerationSettings settings, BuildingDefinition definition, int queueProjectsAhead, int requiredLumberUnits)
        {
            bool ready = IsProjectLikelyStartReady(settings, requiredLumberUnits, queueProjectsAhead, out string reason);
            int score = EvaluateProjectConstraintScore(settings, definition, queueProjectsAhead, requiredLumberUnits);
            string stance = ready
                ? "Start stance: clear."
                : score >= 5
                    ? "Start stance: high-risk delay."
                    : score >= 3
                        ? "Start stance: pressured."
                        : "Start stance: manageable with caution.";
            return stance + " " + reason;
        }
        public bool HasConstructionPressureSignal => CalculateConstructionSupportPressure01() >= 0.35f;

        public float CalculateConstructionSupportPressure01()
        {
            float inactivePressure = Active ? 0f : 0.85f;
            float stockPressure = resourceKind == ConstructionResourceKind.Lumber
                ? 1f - Mathf.Clamp01(CurrentStockUnits / 120f)
                : 1f - Mathf.Clamp01(CurrentStockUnits / 40f);
            float blockerPressure = string.IsNullOrWhiteSpace(LastWeeklyBlockedReason) ? 0f : 0.75f;
            float projectPressure = Mathf.Clamp01(LastProjectConstraintScore / 100f);
            float blockedReviewPressure = Mathf.Clamp01(BlockedProjectReviewCount / 3f);
            float haulingPressure = !InternalHaulingOwned && !HiredHaulingFallbackAllowed ? 0.65f : 0f;
            float noOutputPressure = RemoteProductionEnabled && LogStockUnits > 0 && LastWeeklyLumberProducedUnits <= 0 ? 0.55f : 0f;
            float storagePressure = RemoteProductionEnabled && LumberStorageRoomUnits <= 0 && LogStockUnits > 0 ? 0.5f : 0f;
            float timberPressure = RemoteProductionEnabled && StandingTimberUnits <= 0 && LogStockUnits <= 0 ? 0.6f : 0f;

            return Mathf.Clamp01(Mathf.Max(
                inactivePressure,
                stockPressure,
                blockerPressure,
                projectPressure,
                blockedReviewPressure,
                haulingPressure,
                noOutputPressure,
                storagePressure,
                timberPressure));
        }

        public string BuildConstructionSupportPressureDetail()
        {
            return $"{DisplayName}: stock {CurrentStockUnits}/{LumberStorageCapacityUnits}, timber {StandingTimberUnits}, logs {LogStockUnits}, last lumber {LastWeeklyLumberProducedUnits}, project constraint {LastProjectConstraintScore}, blocked reviews {BlockedProjectReviewCount}. Last blocker: {(string.IsNullOrWhiteSpace(LastWeeklyBlockedReason) ? "none" : LastWeeklyBlockedReason)}";
        }

        public string BuildConstructionSupportPressureAction()
        {
            if (!Active)
            {
                return "Restore local construction support before relying on expansion projects.";
            }

            if (!string.IsNullOrWhiteSpace(LastWeeklyBlockedReason))
            {
                return "Clear the sawmill or construction-support blocker before starting more projects.";
            }

            if (CurrentStockUnits < 80)
            {
                return "Rebuild lumber stock or secure a freight route before project queues tighten.";
            }

            if (!InternalHaulingOwned && !HiredHaulingFallbackAllowed)
            {
                return "Add hauling capacity or authorize hired freight for lumber movement.";
            }

            return "Review lumber stock, hauling, saw output, and builder queue before expanding.";
        }

        public bool InternalHaulingOwned => internalHaulingOwned;
        public bool HiredHaulingFallbackAllowed => hiredHaulingFallbackAllowed;
        public int HiredHaulingCostPerLogCents => Mathf.Max(0, hiredHaulingCostPerLogCents);
        public int LastWeeklyTimberCutUnits => Mathf.Max(0, lastWeeklyTimberCutUnits);
        public int LastWeeklyLogsProcessedUnits => Mathf.Max(0, lastWeeklyLogsProcessedUnits);
        public int LastWeeklyLumberProducedUnits => Mathf.Max(0, lastWeeklyLumberProducedUnits);
        public int LastWeeklySlabsOffcutsProducedUnits => Mathf.Max(0, lastWeeklySlabsOffcutsProducedUnits);
        public int LastWeeklySlabsOffcutsSoldUnits => Mathf.Max(0, lastWeeklySlabsOffcutsSoldUnits);
        public int LastWeeklyLogsHauledUnits => Mathf.Max(0, lastWeeklyLogsHauledUnits);
        public int LastWeeklyHaulingCostCents => Mathf.Max(0, lastWeeklyHaulingCostCents);
        public string LastWeeklyHaulingMode => lastWeeklyHaulingMode ?? string.Empty;
        public int LastResolvedSawmillWeekKey => lastResolvedSawmillWeekKey;
        public string LastWeeklyProductionSummary => lastWeeklyProductionSummary ?? string.Empty;
        public string LastWeeklyBlockedReason => lastWeeklyBlockedReason ?? string.Empty;
        public int LastProjectConstraintScore => Mathf.Max(0, lastProjectConstraintScore);
        public int BlockedProjectReviewCount => Mathf.Max(0, blockedProjectReviewCount);
        public int ReadyProjectReviewCount => Mathf.Max(0, readyProjectReviewCount);
        public string LastWeeklyProjectReviewSummary => lastWeeklyProjectReviewSummary ?? string.Empty;
        public string SawmillCrewLabel => hasWorkerCabins ? "3 on-site worker houses" : "commuting remote crew";
        public int LumberStorageRoomUnits => Mathf.Max(0, LumberStorageCapacityUnits - CurrentStockUnits);

        public static ConstructionSupportNodeState FromDefinition(ConstructionSupportNodeDefinition definition)
        {
            if (definition == null)
            {
                return CreateFallbackSawmill();
            }

            return new ConstructionSupportNodeState
            {
                nodeId = definition.NodeId,
                displayName = definition.DisplayName,
                resourceKind = definition.ResourceKind,
                currentStockUnits = definition.StartingStockUnits,
                unitCostCents = definition.UnitCostCents,
                active = definition.Active,
                remoteProductionEnabled = definition.RemoteProductionEnabled,
                remoteProductionInitialized = definition.RemoteProductionEnabled,
                remotePropertyPlotId = -1,
                standingTimberUnits = definition.StartingStandingTimberUnits,
                logStockUnits = definition.StartingLogStockUnits,
                weeklyCutCapacityLogs = definition.WeeklyCutCapacityLogs,
                weeklySawCapacityLogs = definition.WeeklySawCapacityLogs,
                lumberPerLog = definition.LumberPerLog,
                lumberStorageCapacityUnits = definition.LumberStorageCapacityUnits,
                hardwareMaintenanceUnitsPerWeek = definition.HardwareMaintenanceUnitsPerWeek,
                hasWorkerCabins = definition.HasWorkerCabins,
                internalHaulingOwned = true,
                hiredHaulingFallbackAllowed = true,
                hiredHaulingCostPerLogCents = ConstructionSupportNodeDefinition.DefaultSawmillHiredHaulingCostPerLogCents
            };
        }

        public static ConstructionSupportNodeState FromSaveDto(ConstructionSupportNodeSaveDto dto)
        {
            if (dto == null)
            {
                return null;
            }

            ConstructionSupportNodeState state = new()
            {
                nodeId = dto.nodeId ?? string.Empty,
                displayName = dto.displayName ?? string.Empty,
                resourceKind = dto.resourceKind,
                currentStockUnits = Mathf.Max(0, dto.currentStockUnits),
                unitCostCents = Mathf.Max(0, dto.unitCostCents),
                active = dto.active,
                remoteProductionEnabled = dto.remoteProductionEnabled,
                remoteProductionInitialized = dto.remoteProductionInitialized,
                remotePropertyPlotId = dto.remotePropertyPlotId,
                standingTimberUnits = Mathf.Max(0, dto.standingTimberUnits),
                logStockUnits = Mathf.Max(0, dto.logStockUnits),
                weeklyCutCapacityLogs = Mathf.Max(0, dto.weeklyCutCapacityLogs),
                weeklySawCapacityLogs = Mathf.Max(0, dto.weeklySawCapacityLogs),
                lumberPerLog = Mathf.Max(1, dto.lumberPerLog),
                lumberStorageCapacityUnits = Mathf.Max(0, dto.lumberStorageCapacityUnits),
                hardwareMaintenanceUnitsPerWeek = Mathf.Max(0, dto.hardwareMaintenanceUnitsPerWeek),
                hasWorkerCabins = dto.hasWorkerCabins,
                internalHaulingOwned = dto.internalHaulingOwned,
                hiredHaulingFallbackAllowed = dto.hiredHaulingFallbackAllowed,
                hiredHaulingCostPerLogCents = Mathf.Max(0, dto.hiredHaulingCostPerLogCents),
                lastWeeklyTimberCutUnits = Mathf.Max(0, dto.lastWeeklyTimberCutUnits),
                lastWeeklyLogsProcessedUnits = Mathf.Max(0, dto.lastWeeklyLogsProcessedUnits),
                lastWeeklyLumberProducedUnits = Mathf.Max(0, dto.lastWeeklyLumberProducedUnits),
                lastWeeklySlabsOffcutsProducedUnits = Mathf.Max(0, dto.lastWeeklySlabsOffcutsProducedUnits),
                lastWeeklySlabsOffcutsSoldUnits = Mathf.Max(0, dto.lastWeeklySlabsOffcutsSoldUnits),
                lastWeeklyLogsHauledUnits = Mathf.Max(0, dto.lastWeeklyLogsHauledUnits),
                lastWeeklyHaulingCostCents = Mathf.Max(0, dto.lastWeeklyHaulingCostCents),
                lastWeeklyHaulingMode = dto.lastWeeklyHaulingMode ?? string.Empty,
                lastResolvedSawmillWeekKey = dto.lastResolvedSawmillWeekKey,
                lastWeeklyProductionSummary = dto.lastWeeklyProductionSummary ?? string.Empty,
                lastWeeklyBlockedReason = dto.lastWeeklyBlockedReason ?? string.Empty,
                lastProjectConstraintScore = Mathf.Max(0, dto.lastProjectConstraintScore),
                blockedProjectReviewCount = Mathf.Max(0, dto.blockedProjectReviewCount),
                readyProjectReviewCount = Mathf.Max(0, dto.readyProjectReviewCount),
                lastWeeklyProjectReviewSummary = dto.lastWeeklyProjectReviewSummary ?? string.Empty
            };

            state.NormalizeRemoteProductionDefaults();
            return state;
        }

        public static ConstructionSupportNodeState CreateFallbackSawmill()
        {
            return new ConstructionSupportNodeState
            {
                nodeId = "local_sawmill",
                displayName = "Local Sawmill",
                resourceKind = ConstructionResourceKind.Lumber,
                currentStockUnits = 240,
                unitCostCents = 85,
                active = true,
                remoteProductionEnabled = true,
                remoteProductionInitialized = true,
                remotePropertyPlotId = -1,
                standingTimberUnits = ConstructionSupportNodeDefinition.DefaultSawmillStandingTimberUnits,
                logStockUnits = ConstructionSupportNodeDefinition.DefaultSawmillLogStockUnits,
                weeklyCutCapacityLogs = ConstructionSupportNodeDefinition.DefaultSawmillWeeklyCutCapacityLogs,
                weeklySawCapacityLogs = ConstructionSupportNodeDefinition.DefaultSawmillWeeklySawCapacityLogs,
                lumberPerLog = ConstructionSupportNodeDefinition.DefaultSawmillLumberPerLog,
                lumberStorageCapacityUnits = ConstructionSupportNodeDefinition.DefaultSawmillLumberStorageCapacityUnits,
                hardwareMaintenanceUnitsPerWeek = ConstructionSupportNodeDefinition.DefaultSawmillHardwareMaintenanceUnitsPerWeek,
                internalHaulingOwned = true,
                hiredHaulingFallbackAllowed = true,
                hiredHaulingCostPerLogCents = ConstructionSupportNodeDefinition.DefaultSawmillHiredHaulingCostPerLogCents
            };
        }

        public ConstructionSupportNodeSaveDto CaptureSaveDto()
        {
            return new ConstructionSupportNodeSaveDto
            {
                nodeId = NodeId,
                displayName = DisplayName,
                resourceKind = ResourceKind,
                currentStockUnits = CurrentStockUnits,
                unitCostCents = UnitCostCents,
                active = Active,
                remoteProductionEnabled = remoteProductionEnabled,
                remoteProductionInitialized = remoteProductionInitialized,
                remotePropertyPlotId = remotePropertyPlotId,
                standingTimberUnits = StandingTimberUnits,
                logStockUnits = LogStockUnits,
                weeklyCutCapacityLogs = WeeklyCutCapacityLogs,
                weeklySawCapacityLogs = WeeklySawCapacityLogs,
                lumberPerLog = LumberPerLog,
                lumberStorageCapacityUnits = LumberStorageCapacityUnits,
                hardwareMaintenanceUnitsPerWeek = HardwareMaintenanceUnitsPerWeek,
                hasWorkerCabins = HasWorkerCabins,
                internalHaulingOwned = InternalHaulingOwned,
                hiredHaulingFallbackAllowed = HiredHaulingFallbackAllowed,
                hiredHaulingCostPerLogCents = HiredHaulingCostPerLogCents,
                lastWeeklyTimberCutUnits = LastWeeklyTimberCutUnits,
                lastWeeklyLogsProcessedUnits = LastWeeklyLogsProcessedUnits,
                lastWeeklyLumberProducedUnits = LastWeeklyLumberProducedUnits,
                lastWeeklySlabsOffcutsProducedUnits = LastWeeklySlabsOffcutsProducedUnits,
                lastWeeklySlabsOffcutsSoldUnits = LastWeeklySlabsOffcutsSoldUnits,
                lastWeeklyLogsHauledUnits = LastWeeklyLogsHauledUnits,
                lastWeeklyHaulingCostCents = LastWeeklyHaulingCostCents,
                lastWeeklyHaulingMode = LastWeeklyHaulingMode,
                lastResolvedSawmillWeekKey = LastResolvedSawmillWeekKey,
                lastWeeklyProductionSummary = LastWeeklyProductionSummary,
                lastWeeklyBlockedReason = LastWeeklyBlockedReason,
                lastProjectConstraintScore = LastProjectConstraintScore,
                blockedProjectReviewCount = BlockedProjectReviewCount,
                readyProjectReviewCount = ReadyProjectReviewCount,
                lastWeeklyProjectReviewSummary = LastWeeklyProjectReviewSummary
            };
        }

        public bool TryConsume(int units)
        {
            int requested = Mathf.Max(0, units);
            if (!active || requested > currentStockUnits)
            {
                return false;
            }

            currentStockUnits -= requested;
            return true;
        }

        public int AddStock(int units, bool respectStorageCapacity)
        {
            int requested = Mathf.Max(0, units);
            if (requested <= 0)
            {
                return 0;
            }

            int accepted = respectStorageCapacity ? Mathf.Min(requested, LumberStorageRoomUnits) : requested;
            currentStockUnits += Mathf.Max(0, accepted);
            return accepted;
        }

        public int CutStandingTimberToLogs(int requestedLogs)
        {
            int cut = Mathf.Min(Mathf.Max(0, requestedLogs), StandingTimberUnits);
            standingTimberUnits -= cut;
            logStockUnits += cut;
            return cut;
        }

        public int ProcessLogsToLumber(int requestedLogs, out int producedLumberUnits)
        {
            producedLumberUnits = 0;
            int processableLogs = Mathf.Min(Mathf.Max(0, requestedLogs), LogStockUnits);
            int logsByStorage = LumberPerLog > 0 ? LumberStorageRoomUnits / LumberPerLog : 0;
            processableLogs = Mathf.Min(processableLogs, logsByStorage);
            if (processableLogs <= 0)
            {
                return 0;
            }

            logStockUnits -= processableLogs;
            producedLumberUnits = AddStock(processableLogs * LumberPerLog, true);
            return processableLogs;
        }

        public void RecordSawmillWeeklyResult(
            int timberCutUnits,
            int logsProcessedUnits,
            int lumberProducedUnits,
            string summary,
            string blockedReason)
        {
            RecordSawmillWeeklyResult(
                timberCutUnits,
                logsProcessedUnits,
                lumberProducedUnits,
                0,
                0,
                LastWeeklyHaulingMode,
                LastWeeklyLogsHauledUnits,
                LastWeeklyHaulingCostCents,
                summary,
                blockedReason,
                LastResolvedSawmillWeekKey);
        }

        public void RecordSawmillWeeklyResult(
            int timberCutUnits,
            int logsProcessedUnits,
            int lumberProducedUnits,
            int slabsOffcutsProducedUnits,
            int slabsOffcutsSoldUnits,
            string haulingMode,
            int logsHauledUnits,
            int haulingCostCents,
            string summary,
            string blockedReason,
            int resolvedWeekKey)
        {
            lastWeeklyTimberCutUnits = Mathf.Max(0, timberCutUnits);
            lastWeeklyLogsProcessedUnits = Mathf.Max(0, logsProcessedUnits);
            lastWeeklyLumberProducedUnits = Mathf.Max(0, lumberProducedUnits);
            lastWeeklySlabsOffcutsProducedUnits = Mathf.Max(0, slabsOffcutsProducedUnits);
            lastWeeklySlabsOffcutsSoldUnits = Mathf.Max(0, slabsOffcutsSoldUnits);
            lastWeeklyHaulingMode = haulingMode ?? string.Empty;
            lastWeeklyLogsHauledUnits = Mathf.Max(0, logsHauledUnits);
            lastWeeklyHaulingCostCents = Mathf.Max(0, haulingCostCents);
            lastWeeklyProductionSummary = summary ?? string.Empty;
            lastWeeklyBlockedReason = blockedReason ?? string.Empty;
            lastResolvedSawmillWeekKey = resolvedWeekKey;
        }

        public void SetRemotePropertyPlotId(int plotId)
        {
            remotePropertyPlotId = plotId;
        }

        public string BuildSawmillStatusLine()
        {
            if (!RemoteProductionEnabled)
            {
                return string.Empty;
            }

            string last = !string.IsNullOrWhiteSpace(LastWeeklyProductionSummary)
                ? LastWeeklyProductionSummary
                : "no weekly production resolved yet";
            string hauling = !string.IsNullOrWhiteSpace(LastWeeklyHaulingMode)
                ? $", hauling {LastWeeklyHaulingMode} ({LastWeeklyLogsHauledUnits} logs, {FormatMoney(LastWeeklyHaulingCostCents)})"
                : string.Empty;
            string byproduct = LastWeeklySlabsOffcutsProducedUnits > 0 || LastWeeklySlabsOffcutsSoldUnits > 0
                ? $", slabs/offcuts +{LastWeeklySlabsOffcutsProducedUnits} sold {LastWeeklySlabsOffcutsSoldUnits}"
                : string.Empty;
            return $"Sawmill property: {StandingTimberUnits} standing timber, {LogStockUnits} staged logs, storage {CurrentStockUnits}/{LumberStorageCapacityUnits}, {SawmillCrewLabel}{hauling}{byproduct}; last week: {last}";
        }

        public bool HasResolvedSawmillWeek(int weekKey)
        {
            return weekKey >= 0 && lastResolvedSawmillWeekKey == weekKey;
        }

        public void SetStockForTests(int units)
        {
            currentStockUnits = Mathf.Max(0, units);
        }

        public void SetSawmillInventoryFromBusinessRuntime(int standingTimber, int logs, int lumber)
        {
            if (resourceKind != ConstructionResourceKind.Lumber)
            {
                return;
            }

            standingTimberUnits = Mathf.Max(0, standingTimber);
            logStockUnits = Mathf.Max(0, logs);
            currentStockUnits = Mathf.Max(0, lumber);
            lumberStorageCapacityUnits = Mathf.Max(lumberStorageCapacityUnits, currentStockUnits);
        }

        public void SetSawmillLumberStockFromBusinessRuntime(int lumber)
        {
            if (resourceKind != ConstructionResourceKind.Lumber)
            {
                return;
            }

            currentStockUnits = Mathf.Max(0, lumber);
            lumberStorageCapacityUnits = Mathf.Max(lumberStorageCapacityUnits, currentStockUnits);
        }

        public void SetConstructionAvailableStockFromSharedSellers(int units)
        {
            SetSawmillLumberStockFromBusinessRuntime(units);
        }

        public void SetSawmillProductionForTests(
            int standingTimber,
            int logs,
            int cutCapacity,
            int sawCapacity,
            int unitsPerLog,
            int storageCapacity,
            int hardwareMaintenanceUnits,
            bool workerCabins)
        {
            remoteProductionEnabled = resourceKind == ConstructionResourceKind.Lumber;
            remoteProductionInitialized = remoteProductionEnabled;
            standingTimberUnits = Mathf.Max(0, standingTimber);
            logStockUnits = Mathf.Max(0, logs);
            weeklyCutCapacityLogs = Mathf.Max(0, cutCapacity);
            weeklySawCapacityLogs = Mathf.Max(0, sawCapacity);
            lumberPerLog = Mathf.Max(1, unitsPerLog);
            lumberStorageCapacityUnits = Mathf.Max(CurrentStockUnits, storageCapacity);
            hardwareMaintenanceUnitsPerWeek = Mathf.Max(0, hardwareMaintenanceUnits);
            hasWorkerCabins = workerCabins;
            internalHaulingOwned = true;
            hiredHaulingFallbackAllowed = true;
            hiredHaulingCostPerLogCents = ConstructionSupportNodeDefinition.DefaultSawmillHiredHaulingCostPerLogCents;
            RecordSawmillWeeklyResult(0, 0, 0, 0, 0, string.Empty, 0, 0, string.Empty, string.Empty, -1);
        }

        public void SetSawmillHaulingForTests(bool ownedInternalHauling, bool hiredFallbackAllowed, int hiredCostPerLogCents)
        {
            internalHaulingOwned = ownedInternalHauling;
            hiredHaulingFallbackAllowed = hiredFallbackAllowed;
            hiredHaulingCostPerLogCents = Mathf.Max(0, hiredCostPerLogCents);
        }

        private void NormalizeRemoteProductionDefaults()
        {
            if (resourceKind != ConstructionResourceKind.Lumber)
            {
                remoteProductionEnabled = false;
                return;
            }

            if (!remoteProductionInitialized)
            {
                remoteProductionEnabled = true;
                remoteProductionInitialized = true;
                standingTimberUnits = ConstructionSupportNodeDefinition.DefaultSawmillStandingTimberUnits;
                logStockUnits = ConstructionSupportNodeDefinition.DefaultSawmillLogStockUnits;
                weeklyCutCapacityLogs = ConstructionSupportNodeDefinition.DefaultSawmillWeeklyCutCapacityLogs;
                weeklySawCapacityLogs = ConstructionSupportNodeDefinition.DefaultSawmillWeeklySawCapacityLogs;
                lumberPerLog = ConstructionSupportNodeDefinition.DefaultSawmillLumberPerLog;
                lumberStorageCapacityUnits = ConstructionSupportNodeDefinition.DefaultSawmillLumberStorageCapacityUnits;
                hardwareMaintenanceUnitsPerWeek = ConstructionSupportNodeDefinition.DefaultSawmillHardwareMaintenanceUnitsPerWeek;
                hasWorkerCabins = false;
                internalHaulingOwned = true;
                hiredHaulingFallbackAllowed = true;
                hiredHaulingCostPerLogCents = ConstructionSupportNodeDefinition.DefaultSawmillHiredHaulingCostPerLogCents;
                return;
            }

            lumberPerLog = Mathf.Max(1, lumberPerLog);
            lumberStorageCapacityUnits = Mathf.Max(CurrentStockUnits, lumberStorageCapacityUnits);
            if (hiredHaulingCostPerLogCents <= 0)
            {
                hiredHaulingCostPerLogCents = ConstructionSupportNodeDefinition.DefaultSawmillHiredHaulingCostPerLogCents;
            }
        }

        private static string FormatMoney(int cents)
        {
            return "$" + (Mathf.Max(0, cents) / 100f).ToString("N2");
        }
    }
}
