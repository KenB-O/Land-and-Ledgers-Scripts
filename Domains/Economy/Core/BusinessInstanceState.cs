using System;
using System.Collections.Generic;
using LandLedgers.Persistence;
using LandLedgers.Reputation;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy
{
    public enum BusinessOwnerKind
    {
        Player = 0,
        Npc = 1,
        Town = 2
    }

    [Serializable]
    public sealed class BusinessCashTransferRuleState
    {
        public const int MinimumThresholdGapCents = 100;
        public const float DefaultSweepThresholdMultiplier = 1.25f;

        [SerializeField]
        private bool autoTransferEnabled = true;

        [SerializeField, Min(0)]
        private int lowerThresholdCents;

        [SerializeField, Min(0)]
        private int upperThresholdCents;

        [SerializeField, TextArea(1, 3)]
        private string lastTransferSummary = "No cash transfers yet.";

        [SerializeField]
        private int lastTransferWeekKey = -1;

        public bool AutoTransferEnabled => autoTransferEnabled;
        public int LowerThresholdCents => Mathf.Max(0, lowerThresholdCents);
        public int UpperThresholdCents => Mathf.Max(0, upperThresholdCents);
        public string LastTransferSummary => string.IsNullOrWhiteSpace(lastTransferSummary) ? "No cash transfers yet." : lastTransferSummary;
        public int LastTransferWeekKey => lastTransferWeekKey;

        public int GetEffectiveLowerThresholdCents(int protectedReserveCents)
        {
            return Mathf.Max(Mathf.Max(0, protectedReserveCents), LowerThresholdCents);
        }

        public int GetEffectiveUpperThresholdCents(int protectedReserveCents)
        {
            int lower = GetEffectiveLowerThresholdCents(protectedReserveCents);
            return CalculateDefaultSweepThresholdCents(lower);
        }

        public void SetAutoTransferEnabled(bool enabled)
        {
            autoTransferEnabled = enabled;
            lastTransferSummary = enabled ? "Auto transfer enabled." : "Auto transfer disabled.";
        }

        public void ConfigureAutoTransferThresholds(int lowerThresholdCents, int upperThresholdCents, int protectedReserveCents)
        {
            int reserve = Mathf.Max(0, protectedReserveCents);
            int lower = Mathf.Max(reserve, Mathf.Max(0, lowerThresholdCents));
            int upper = CalculateDefaultSweepThresholdCents(lower);
            this.lowerThresholdCents = lower;
            this.upperThresholdCents = upper;
            autoTransferEnabled = true;
            lastTransferSummary = $"Auto reserve on: refill below {FormatMoney(GetEffectiveLowerThresholdCents(reserve))}, sweep above {FormatMoney(GetEffectiveUpperThresholdCents(reserve))}.";
        }

        public void SetAutoTransferThresholds(int lowerThresholdCents, int upperThresholdCents, int protectedReserveCents)
        {
            int reserve = Mathf.Max(0, protectedReserveCents);
            int lower = Mathf.Max(reserve, Mathf.Max(0, lowerThresholdCents));
            int upper = CalculateDefaultSweepThresholdCents(lower);
            this.lowerThresholdCents = lower;
            this.upperThresholdCents = upper;
            autoTransferEnabled = true;
            lastTransferSummary = $"Reserve rules set: refill below {FormatMoney(GetEffectiveLowerThresholdCents(reserve))}, sweep above {FormatMoney(GetEffectiveUpperThresholdCents(reserve))}.";
        }

        public void AdjustLowerThresholdCents(int deltaCents, int protectedReserveCents)
        {
            int reserve = Mathf.Max(0, protectedReserveCents);
            int lower = GetEffectiveLowerThresholdCents(reserve);
            lowerThresholdCents = Mathf.Max(reserve, lower + deltaCents);
            upperThresholdCents = CalculateDefaultSweepThresholdCents(lowerThresholdCents);
            lastTransferSummary = $"Refill threshold set to {FormatMoney(GetEffectiveLowerThresholdCents(reserve))}; sweep follows at {FormatMoney(GetEffectiveUpperThresholdCents(reserve))}.";
        }

        public void AdjustUpperThresholdCents(int deltaCents, int protectedReserveCents)
        {
            int reserve = Mathf.Max(0, protectedReserveCents);
            int lower = GetEffectiveLowerThresholdCents(reserve);
            upperThresholdCents = CalculateDefaultSweepThresholdCents(lower);
            lastTransferSummary = $"Sweep threshold follows refill at 25% over: {FormatMoney(GetEffectiveUpperThresholdCents(reserve))}.";
        }

        public void RecordTransferSummary(string summary, int weekKey)
        {
            lastTransferSummary = string.IsNullOrWhiteSpace(summary) ? "No cash transfer." : summary.Trim();
            lastTransferWeekKey = weekKey;
        }

        public BusinessCashTransferRuleSaveDto CaptureSaveDto()
        {
            return new BusinessCashTransferRuleSaveDto
            {
                autoTransferEnabled = autoTransferEnabled,
                lowerThresholdCents = LowerThresholdCents,
                upperThresholdCents = UpperThresholdCents,
                lastTransferSummary = LastTransferSummary,
                lastTransferWeekKey = lastTransferWeekKey
            };
        }

        public static BusinessCashTransferRuleState FromSaveDto(BusinessCashTransferRuleSaveDto dto)
        {
            if (dto == null)
            {
                return new BusinessCashTransferRuleState();
            }

            string resolvedSummary = string.IsNullOrWhiteSpace(dto.lastTransferSummary) ? "No cash transfers yet." : dto.lastTransferSummary;
            bool legacyUnconfiguredDefault = !dto.autoTransferEnabled
                && dto.lastTransferWeekKey < 0
                && string.Equals(resolvedSummary, "No cash transfers yet.", StringComparison.Ordinal);

            return new BusinessCashTransferRuleState
            {
                autoTransferEnabled = dto.autoTransferEnabled || legacyUnconfiguredDefault,
                lowerThresholdCents = Mathf.Max(0, dto.lowerThresholdCents),
                upperThresholdCents = Mathf.Max(0, dto.upperThresholdCents),
                lastTransferSummary = resolvedSummary,
                lastTransferWeekKey = dto.lastTransferWeekKey
            };
        }

        public int NormalizeForStartup(int protectedReserveCents)
        {
            int repairs = 0;
            int reserve = Mathf.Max(0, protectedReserveCents);
            int resolvedLower = Mathf.Max(reserve, Mathf.Max(0, lowerThresholdCents));
            int resolvedUpper = CalculateDefaultSweepThresholdCents(resolvedLower);
            string resolvedSummary = string.IsNullOrWhiteSpace(lastTransferSummary)
                ? "No cash transfers yet."
                : lastTransferSummary.Trim();
            bool legacyUnconfiguredDefault = !autoTransferEnabled
                && lastTransferWeekKey < 0
                && string.Equals(resolvedSummary, "No cash transfers yet.", StringComparison.Ordinal);
            bool resolvedAutoTransferEnabled = autoTransferEnabled || legacyUnconfiguredDefault;

            if (resolvedLower != lowerThresholdCents
                || resolvedUpper != upperThresholdCents
                || resolvedAutoTransferEnabled != autoTransferEnabled
                || !string.Equals(resolvedSummary, lastTransferSummary, StringComparison.Ordinal))
            {
                repairs++;
            }

            lowerThresholdCents = resolvedLower;
            upperThresholdCents = resolvedUpper;
            autoTransferEnabled = resolvedAutoTransferEnabled;
            lastTransferSummary = resolvedSummary;
            return repairs;
        }

        public static int CalculateDefaultSweepThresholdCents(int refillThresholdCents)
        {
            int refill = Mathf.Max(0, refillThresholdCents);
            int sweep = Mathf.CeilToInt(refill * DefaultSweepThresholdMultiplier);
            return Mathf.Max(sweep, refill + MinimumThresholdGapCents);
        }

        private static string FormatMoney(int cents)
        {
            return $"${Mathf.Max(0, cents) / 100f:0.00}";
        }
    }

    [Serializable]
    public sealed class BusinessOwnerIdentity
    {
        [SerializeField]
        private BusinessOwnerKind ownerKind;

        [SerializeField]
        private int personId = -1;

        [SerializeField]
        private string displayName = "Player";

        [SerializeField]
        private string surname = "Player";

        public BusinessOwnerKind OwnerKind => ownerKind;
        public int PersonId => personId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? GetFallbackName() : displayName;
        public string Surname => string.IsNullOrWhiteSpace(surname) ? GetFallbackName() : surname;

        private string GetFallbackName()
        {
            return ownerKind == BusinessOwnerKind.Player ? "Player" : "Town";
        }

        public static BusinessOwnerIdentity Player()
        {
            return new BusinessOwnerIdentity
            {
                ownerKind = BusinessOwnerKind.Player,
                displayName = "Player",
                surname = "Player"
            };
        }

        public static BusinessOwnerIdentity Npc(int personId, string displayName, string surname)
        {
            return new BusinessOwnerIdentity
            {
                ownerKind = BusinessOwnerKind.Npc,
                personId = personId,
                displayName = displayName,
                surname = surname
            };
        }

        public static BusinessOwnerIdentity Town(string displayName = "Town")
        {
            return new BusinessOwnerIdentity
            {
                ownerKind = BusinessOwnerKind.Town,
                displayName = displayName,
                surname = displayName
            };
        }

        public BusinessOwnerSaveDto CaptureSaveDto()
        {
            return new BusinessOwnerSaveDto
            {
                ownerKind = OwnerKind,
                personId = PersonId,
                displayName = DisplayName,
                surname = Surname
            };
        }

        public static BusinessOwnerIdentity FromSaveDto(BusinessOwnerSaveDto dto)
        {
            if (dto == null)
            {
                return Town();
            }

            return new BusinessOwnerIdentity
            {
                ownerKind = dto.ownerKind,
                personId = dto.personId,
                displayName = dto.displayName,
                surname = dto.surname
            };
        }
    }

    public enum BusinessRoleCoverageStatus
    {
        Covered = 0,
        OwnerOperatorFallback = 1,
        MissingRequired = 2,
        Unstaffed = 3
    }

    public readonly struct BusinessRoleCoverageSnapshot
    {
        public BusinessRoleCoverageSnapshot(
            int requiredWorkerCount,
            int activeRequiredWorkerCount,
            int targetWorkerCount,
            int activeWorkerCount,
            float operatingEfficiency01,
            BusinessRoleCoverageStatus status)
        {
            RequiredWorkerCount = Mathf.Max(0, requiredWorkerCount);
            ActiveRequiredWorkerCount = Mathf.Clamp(activeRequiredWorkerCount, 0, RequiredWorkerCount);
            TargetWorkerCount = Mathf.Max(0, targetWorkerCount);
            ActiveWorkerCount = Mathf.Clamp(activeWorkerCount, 0, TargetWorkerCount);
            OperatingEfficiency01 = Mathf.Clamp01(operatingEfficiency01);
            Status = status;
        }

        public int RequiredWorkerCount { get; }
        public int ActiveRequiredWorkerCount { get; }
        public int TargetWorkerCount { get; }
        public int ActiveWorkerCount { get; }
        public float OperatingEfficiency01 { get; }
        public int EfficiencyPercent => Mathf.RoundToInt(OperatingEfficiency01 * 100f);
        public BusinessRoleCoverageStatus Status { get; }
        public bool HasRequiredCoverage => RequiredWorkerCount <= 0 || ActiveRequiredWorkerCount >= RequiredWorkerCount;

        public string BuildSummary()
        {
            string required = RequiredWorkerCount > 0
                ? $"Required {ActiveRequiredWorkerCount}/{RequiredWorkerCount}"
                : "Required none";
            string staff = $"Staff {ActiveWorkerCount}/{TargetWorkerCount}";
            string status = Status switch
            {
                BusinessRoleCoverageStatus.OwnerOperatorFallback => "owner-operator coverage",
                BusinessRoleCoverageStatus.MissingRequired => "required role missing",
                BusinessRoleCoverageStatus.Unstaffed => "unstaffed",
                _ => "covered"
            };

            return $"Coverage: {required} | {staff} | {EfficiencyPercent}% | {status}";
        }
    }

    public enum BusinessContinuityStatus
    {
        Operating = 0,
        OwnerOperatorFallback = 1,
        Understaffed = 2,
        MissingRequiredRoles = 3,
        Blocked = 4,
        CashBelowSurvivalReserve = 5,
        MissedPayroll = 6,
        Unavailable = 7,
        Pressured = 8
    }

    public readonly struct BusinessPortfolioSummary
    {
        public BusinessPortfolioSummary(
            string instanceId,
            string displayName,
            BusinessType businessType,
            string ownerDisplayName,
            int businessCashCents,
            int operatingReserveCents,
            int survivalReserveCents,
            int transferableCashCents,
            int businessLiabilityCents,
            int equityContributionCents,
            BusinessContinuityStatus continuityStatus,
            string continuitySummary,
            BusinessRoleCoverageSnapshot roleCoverage)
        {
            InstanceId = instanceId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? BusinessRuntimeNaming.GetBusinessTypeDisplayName(businessType) : displayName;
            BusinessType = businessType;
            OwnerDisplayName = string.IsNullOrWhiteSpace(ownerDisplayName) ? "Town" : ownerDisplayName;
            BusinessCashCents = Mathf.Max(0, businessCashCents);
            OperatingReserveCents = Mathf.Max(0, operatingReserveCents);
            SurvivalReserveCents = Mathf.Max(0, survivalReserveCents);
            TransferableCashCents = Mathf.Max(0, transferableCashCents);
            BusinessLiabilityCents = Mathf.Max(0, businessLiabilityCents);
            EquityContributionCents = Mathf.Max(0, equityContributionCents);
            ContinuityStatus = continuityStatus;
            ContinuitySummary = string.IsNullOrWhiteSpace(continuitySummary) ? "continuity unavailable" : continuitySummary.Trim();
            RoleCoverage = roleCoverage;
        }

        public BusinessPortfolioSummary(
            string instanceId,
            string displayName,
            BusinessType businessType,
            string ownerDisplayName,
            int businessCashCents,
            int equityContributionCents,
            int businessLiabilityCents,
            int protectedReserveCents,
            int availableAboveReserveCents,
            BusinessRoleCoverageSnapshot roleCoverage)
            : this(
                instanceId,
                displayName,
                businessType,
                ownerDisplayName,
                businessCashCents,
                protectedReserveCents,
                protectedReserveCents,
                availableAboveReserveCents,
                businessLiabilityCents,
                equityContributionCents,
                BusinessContinuityStatus.Operating,
                "operating",
                roleCoverage)
        {
        }

        public string InstanceId { get; }
        public string DisplayName { get; }
        public BusinessType BusinessType { get; }
        public string OwnerDisplayName { get; }
        public int BusinessCashCents { get; }
        public int OperatingReserveCents { get; }
        public int SurvivalReserveCents { get; }
        public int TransferableCashCents { get; }
        public int EquityContributionCents { get; }
        public int BusinessLiabilityCents { get; }
        public int ProtectedReserveCents => SurvivalReserveCents;
        public int AvailableAboveReserveCents => TransferableCashCents;
        public int AvailableAboveOperatingReserveCents => Mathf.Max(0, BusinessCashCents - OperatingReserveCents);
        public int CashNeededForOperatingReserveCents => Mathf.Max(0, OperatingReserveCents - BusinessCashCents);
        public int CashNeededForSurvivalReserveCents => Mathf.Max(0, SurvivalReserveCents - BusinessCashCents);
        public bool IsBelowOperatingReserve => CashNeededForOperatingReserveCents > 0;
        public bool IsBelowSurvivalReserve => CashNeededForSurvivalReserveCents > 0;
        public bool HasTransferableCash => TransferableCashCents > 0;
        public string CashPostureLabel => ResolveCashPostureLabel();
        public string CashPressureSummary => ResolveCashPressureSummary();
        public BusinessContinuityStatus ContinuityStatus { get; }
        public string ContinuitySummary { get; }
        public BusinessRoleCoverageSnapshot RoleCoverage { get; }
        public string FinanceHealthLabel => ResolveFinanceHealthLabel();
        public bool IsContinuityStable => ContinuityStatus == BusinessContinuityStatus.Operating || ContinuityStatus == BusinessContinuityStatus.Pressured;

        public string BuildLedgerLine()
        {
            return $"{DisplayName}: {BuildCashReserveLine()} | Equity {FormatMoney(EquityContributionCents)} | Liabilities {FormatMoney(BusinessLiabilityCents)} | Continuity {ContinuitySummary} | {RoleCoverage.BuildSummary()}";
        }

        public string BuildCashReserveLine()
        {
            return $"Business Cash {FormatMoney(BusinessCashCents)} | {CashPostureLabel} | Operating Reserve {FormatMoney(OperatingReserveCents)} | Survival Reserve {FormatMoney(SurvivalReserveCents)} | Above Operating {FormatMoney(AvailableAboveOperatingReserveCents)} | Transferable {FormatMoney(TransferableCashCents)} | {CashPressureSummary}";
        }

        private static string FormatMoney(int cents)
        {
            return $"${Mathf.Max(0, cents) / 100f:0.00}";
        }

        private string ResolveFinanceHealthLabel()
        {
            if (ContinuityStatus == BusinessContinuityStatus.MissedPayroll
                || ContinuityStatus == BusinessContinuityStatus.CashBelowSurvivalReserve)
            {
                return "Starving";
            }

            if (ContinuityStatus == BusinessContinuityStatus.Blocked
                || ContinuityStatus == BusinessContinuityStatus.MissingRequiredRoles
                || ContinuityStatus == BusinessContinuityStatus.OwnerOperatorFallback
                || ContinuityStatus == BusinessContinuityStatus.Understaffed
                || ContinuityStatus == BusinessContinuityStatus.Pressured
                || RoleCoverage.OperatingEfficiency01 < 0.5f)
            {
                return "Pressured";
            }

            bool strongCash = SurvivalReserveCents <= 0 || BusinessCashCents >= SurvivalReserveCents * 3;
            bool strongCoverage = RoleCoverage.Status == BusinessRoleCoverageStatus.Covered && RoleCoverage.ActiveWorkerCount >= RoleCoverage.TargetWorkerCount;
            return strongCash && strongCoverage && BusinessLiabilityCents <= 0 ? "Healthy" : "Stable";
        }

        private string ResolveCashPostureLabel()
        {
            if (IsBelowSurvivalReserve)
            {
                return "cash below survival reserve";
            }

            if (IsBelowOperatingReserve)
            {
                return "cash below operating reserve";
            }

            if (HasTransferableCash)
            {
                return "transferable surplus";
            }

            return "reserve covered";
        }

        private string ResolveCashPressureSummary()
        {
            if (IsBelowSurvivalReserve)
            {
                return $"needs {FormatMoney(CashNeededForSurvivalReserveCents)} to cover survival reserve";
            }

            if (IsBelowOperatingReserve)
            {
                return $"needs {FormatMoney(CashNeededForOperatingReserveCents)} to cover operating reserve";
            }

            if (HasTransferableCash)
            {
                return $"{FormatMoney(TransferableCashCents)} safely transferable above survival reserve";
            }

            return "cash held for operating protection";
        }
    }

    [Serializable]
    public sealed class BusinessInstanceState
    {
        private const float RequiredStaffBaselineEfficiency01 = 0.8f;
        private const float OwnerOperatedFallbackEfficiency01 = 0.45f;

        [SerializeField]
        private string instanceId;

        [SerializeField]
        private string profileId;

        [SerializeField]
        private BusinessType businessType;

        [SerializeField]
        private int assignedBuildingId = -1;

        [SerializeField]
        private string runtimeDisplayName;

        [SerializeField]
        private BusinessOwnerIdentity owner = BusinessOwnerIdentity.Player();

        [SerializeField]
        private BusinessControlState controlState = BusinessControlState.PlayerManaged;

        [SerializeField]
        private ManagerPolicyPreset managerPolicy = ManagerPolicyPreset.StabilityFirst;

        [SerializeField]
        private BusinessRuntimeState runtimeState;

        [SerializeField]
        private BusinessThroughputMode throughputMode;

        [SerializeField, Min(0)]
        private int baselineWeeklyThroughputUnits;

        [SerializeField, Min(0)]
        private int baselineDailyServiceCapacity;

        [SerializeField, Min(0)]
        private int lastWeeklyThroughputUnits;

        [SerializeField, Min(0)]
        private int lastDailyServiceVisits;

        [SerializeField]
        private BusinessCashTransferRuleState cashTransferRule = new();

        [SerializeField]
        private BusinessReputationState businessReputation = new();

        [SerializeField]
        private MineRuntimeState mineState;

        [SerializeField]
        [Tooltip("BIZ-2: commercial capabilities this business identity exposes (Tech X §3.1). One business, many capabilities — no synthetic subsidiaries.")]
        private List<string> capabilityIds = new List<string>();

        public string InstanceId => instanceId ?? string.Empty;
        public string ProfileId => profileId ?? string.Empty;
        public BusinessType BusinessType => businessType;
        public int AssignedBuildingId => assignedBuildingId;
        public string RuntimeDisplayName => string.IsNullOrWhiteSpace(runtimeDisplayName) ? BusinessRuntimeNaming.Build(owner, businessType) : runtimeDisplayName;
        public BusinessOwnerIdentity Owner => owner;
        public BusinessControlState ControlState => ManagerPolicyEffects.SanitizeControlState(controlState, owner);
        public ManagerPolicyPreset ManagerPolicy => ManagerPolicyEffects.SanitizePolicy(managerPolicy);
        public string ControlStateDisplayName => ManagerPolicyEffects.GetDisplayName(ControlState);
        public string ManagerPolicyDisplayName => ManagerPolicyEffects.GetDisplayName(ManagerPolicy);
        public BusinessRuntimeState RuntimeState => runtimeState;
        public BusinessThroughputMode ThroughputMode => throughputMode;
        public BusinessMainFocusState MainFocus => runtimeState != null ? runtimeState.MainFocus : BusinessMainFocusState.FromDefinition(null, businessType);
        public BusinessCapacityState Capacity => runtimeState != null ? runtimeState.Capacity : null;
        public float OperatingEfficiency01 => CalculateOperatingEfficiency01(runtimeState, owner);
        public int BaselineWeeklyThroughputUnits => Mathf.Max(0, baselineWeeklyThroughputUnits);
        public int BaselineDailyServiceCapacity => Mathf.Max(0, baselineDailyServiceCapacity);
        public int LastWeeklyThroughputUnits => Mathf.Max(0, lastWeeklyThroughputUnits);
        public int LastDailyServiceVisits => Mathf.Max(0, lastDailyServiceVisits);
        public string LastWeeklyBlockedReason => runtimeState != null ? runtimeState.LastWeeklyBlockedReason : string.Empty;
        public string LastWeeklyOperationSummary => runtimeState != null ? runtimeState.LastWeeklyOperationSummary : string.Empty;
        public string LastWeeklyTransferSummary => runtimeState != null ? runtimeState.LastWeeklyTransferSummary : string.Empty;
        public int LastWeeklyInputUnitsConsumed => runtimeState != null ? runtimeState.LastWeeklyInputUnitsConsumed : 0;
        public int LastWeeklyOutputUnitsProduced => runtimeState != null ? runtimeState.LastWeeklyOutputUnitsProduced : 0;
        public int LastWeeklyInputProcurementSpendCents => runtimeState != null ? runtimeState.LastWeeklyInputProcurementSpendCents : 0;
        public int LastWeeklyLocalTransferRevenueCents => runtimeState != null ? runtimeState.LastWeeklyLocalTransferRevenueCents : 0;
        public int LastWeeklyLocalTransferCostCents => runtimeState != null ? runtimeState.LastWeeklyLocalTransferCostCents : 0;
        public int LastWeeklyCashBeforeCents => runtimeState != null ? runtimeState.LastWeeklyCashBeforeCents : 0;
        public int LastWeeklyCashAfterCents => runtimeState != null ? runtimeState.LastWeeklyCashAfterCents : 0;
        public BusinessCashTransferRuleState CashTransferRule => cashTransferRule ??= new BusinessCashTransferRuleState();
        public BusinessReputationState BusinessReputation => businessReputation ??= new BusinessReputationState();
        public MineRuntimeState MineState => businessType == BusinessType.Mine ? mineState : null;

        /// <summary>BIZ-2: capability ids this business identity exposes (Tech X §3.1).</summary>
        public IReadOnlyList<string> CapabilityIds => capabilityIds;

        /// <summary>BIZ-2: adds a capability id if not already present.</summary>
        public void AddCapability(string capabilityId)
        {
            capabilityIds ??= new List<string>();
            if (!string.IsNullOrWhiteSpace(capabilityId) && !HasCapability(capabilityId))
            {
                capabilityIds.Add(capabilityId);
            }
        }

        /// <summary>BIZ-2: whether this business exposes a capability.</summary>
        public bool HasCapability(string capabilityId)
        {
            if (string.IsNullOrWhiteSpace(capabilityId) || capabilityIds == null)
            {
                return false;
            }

            foreach (string id in capabilityIds)
            {
                if (string.Equals(id, capabilityId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public BusinessRoleCoverageSnapshot BuildRoleCoverageSnapshot()
        {
            if (runtimeState == null)
            {
                return new BusinessRoleCoverageSnapshot(0, 0, 0, 0, 0f, BusinessRoleCoverageStatus.Unstaffed);
            }

            BusinessRoleCoverageStatus status;
            bool playerOwned = IsPlayerOwned(owner);
            if (runtimeState.RequiredWorkerCount > 0 && runtimeState.ActiveRequiredWorkerCount < runtimeState.RequiredWorkerCount)
            {
                status = playerOwned ? BusinessRoleCoverageStatus.OwnerOperatorFallback : BusinessRoleCoverageStatus.MissingRequired;
            }
            else if (runtimeState.TargetWorkerCount > 0 && runtimeState.ActiveWorkerCount <= 0)
            {
                status = playerOwned ? BusinessRoleCoverageStatus.OwnerOperatorFallback : BusinessRoleCoverageStatus.Unstaffed;
            }
            else
            {
                status = BusinessRoleCoverageStatus.Covered;
            }

            return new BusinessRoleCoverageSnapshot(
                runtimeState.RequiredWorkerCount,
                runtimeState.ActiveRequiredWorkerCount,
                runtimeState.TargetWorkerCount,
                runtimeState.ActiveWorkerCount,
                OperatingEfficiency01,
                status);
        }

        public BusinessPortfolioSummary BuildPortfolioSummary(int protectedReserveCents)
        {
            return BuildPortfolioSummary(protectedReserveCents, protectedReserveCents);
        }

        public BusinessPortfolioSummary BuildPortfolioSummary(int operatingReserveCents, int survivalReserveCents)
        {
            int cash = runtimeState != null ? runtimeState.CurrentCashCents : 0;
            int operatingReserve = Mathf.Max(0, operatingReserveCents);
            int survivalReserve = Mathf.Max(operatingReserve, Mathf.Max(0, survivalReserveCents));
            int liabilities = runtimeState != null ? runtimeState.AccruedLiabilityCents : 0;
            int transferable = Mathf.Max(0, cash - survivalReserve);
            int equity = Mathf.Max(0, transferable - liabilities);
            BusinessContinuityStatus continuity = ResolveContinuityStatus(runtimeState, BuildRoleCoverageSnapshot(), survivalReserve);
            return new BusinessPortfolioSummary(
                InstanceId,
                RuntimeDisplayName,
                BusinessType,
                owner != null ? owner.DisplayName : "Town",
                cash,
                operatingReserve,
                survivalReserve,
                transferable,
                liabilities,
                equity,
                continuity,
                BuildContinuitySummary(continuity, cash, survivalReserve),
                BuildRoleCoverageSnapshot());
        }

        private BusinessContinuityStatus ResolveContinuityStatus(
            BusinessRuntimeState runtime,
            BusinessRoleCoverageSnapshot coverage,
            int survivalReserveCents)
        {
            if (runtime == null)
            {
                return BusinessContinuityStatus.Unavailable;
            }

            if (runtime.LastSuspendedPayrollWorkerIds.Count > 0)
            {
                return BusinessContinuityStatus.MissedPayroll;
            }

            if (runtime.CurrentCashCents < Mathf.Max(0, survivalReserveCents))
            {
                return BusinessContinuityStatus.CashBelowSurvivalReserve;
            }

            if (!string.IsNullOrWhiteSpace(runtime.LastWeeklyBlockedReason))
            {
                return BusinessContinuityStatus.Blocked;
            }

            return coverage.Status switch
            {
                BusinessRoleCoverageStatus.OwnerOperatorFallback => BusinessContinuityStatus.OwnerOperatorFallback,
                BusinessRoleCoverageStatus.MissingRequired => BusinessContinuityStatus.MissingRequiredRoles,
                BusinessRoleCoverageStatus.Unstaffed => BusinessContinuityStatus.Understaffed,
                _ when runtime.TargetWorkerCount > 0 && runtime.FilledWorkerCount < runtime.TargetWorkerCount => BusinessContinuityStatus.Pressured,
                _ => BusinessContinuityStatus.Operating
            };
        }

        private static string BuildContinuitySummary(BusinessContinuityStatus status, int currentCashCents, int survivalReserveCents)
        {
            return status switch
            {
                BusinessContinuityStatus.MissedPayroll => "missed payroll",
                BusinessContinuityStatus.CashBelowSurvivalReserve => $"cash below survival reserve by {FormatMoney(Mathf.Max(0, survivalReserveCents - currentCashCents))}",
                BusinessContinuityStatus.Blocked => "blocked this cycle",
                BusinessContinuityStatus.MissingRequiredRoles => "required roles missing",
                BusinessContinuityStatus.OwnerOperatorFallback => "owner-operator fallback",
                BusinessContinuityStatus.Understaffed => "unstaffed",
                BusinessContinuityStatus.Pressured => "pressured but operating",
                BusinessContinuityStatus.Unavailable => "unavailable",
                _ => "operating"
            };
        }

        private static string FormatMoney(int cents)
        {
            return $"${Mathf.Max(0, cents) / 100f:0.00}";
        }

        public static float CalculateOperatingEfficiency01(BusinessRuntimeState runtime, BusinessOwnerIdentity owner)
        {
            if (runtime == null)
            {
                return 0f;
            }

            if (runtime.TargetWorkerCount <= 0)
            {
                return 1f;
            }

            int required = runtime.RequiredWorkerCount;
            if (required > 0 && runtime.ActiveRequiredWorkerCount < required)
            {
                return IsPlayerOwned(owner) ? OwnerOperatedFallbackEfficiency01 : 0f;
            }

            if (required <= 0)
            {
                if (runtime.ActiveWorkerCount <= 0 && IsPlayerOwned(owner))
                {
                    return OwnerOperatedFallbackEfficiency01;
                }

                return Mathf.Clamp01((float)runtime.ActiveWorkerCount / runtime.TargetWorkerCount);
            }

            int optionalSlots = Mathf.Max(0, runtime.TargetWorkerCount - required);
            if (optionalSlots <= 0)
            {
                return 1f;
            }

            int activeOptional = Mathf.Max(0, runtime.ActiveWorkerCount - runtime.ActiveRequiredWorkerCount);
            float optionalContribution = Mathf.Clamp01((float)activeOptional / optionalSlots) * (1f - RequiredStaffBaselineEfficiency01);
            return Mathf.Clamp01(RequiredStaffBaselineEfficiency01 + optionalContribution);
        }

        private static bool IsPlayerOwned(BusinessOwnerIdentity owner)
        {
            return owner != null && owner.OwnerKind == BusinessOwnerKind.Player;
        }

        public static BusinessInstanceState Create(string instanceId, BusinessProfileDefinition profile, int assignedBuildingId, BusinessOwnerIdentity owner)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            BusinessOwnerIdentity resolvedOwner = owner ?? BusinessOwnerIdentity.Town();
            BusinessType type = profile.Business.BusinessType;
            return new BusinessInstanceState
            {
                instanceId = string.IsNullOrWhiteSpace(instanceId) ? profile.Business.BusinessId : instanceId,
                profileId = profile.Business.BusinessId,
                businessType = type,
                assignedBuildingId = assignedBuildingId,
                owner = resolvedOwner,
                controlState = ManagerPolicyEffects.GetDefaultControlState(resolvedOwner),
                managerPolicy = ManagerPolicyPreset.StabilityFirst,
                runtimeDisplayName = BusinessRuntimeNaming.Build(resolvedOwner, type),
                runtimeState = profile.CreateRuntimeState(),
                throughputMode = profile.ThroughputMode,
                baselineWeeklyThroughputUnits = profile.BaselineWeeklyThroughputUnits,
                baselineDailyServiceCapacity = profile.BaselineDailyServiceCapacity,
                mineState = type == BusinessType.Mine ? MineRuntimeState.CreateDefault(MineralResourceKind.Coal) : null
            };
        }

        public void SetOwner(BusinessOwnerIdentity newOwner)
        {
            owner = newOwner ?? BusinessOwnerIdentity.Town();
            controlState = ManagerPolicyEffects.GetDefaultControlState(owner);
            managerPolicy = ManagerPolicyEffects.SanitizePolicy(managerPolicy);
            runtimeDisplayName = BusinessRuntimeNaming.Build(owner, businessType);
        }

        public void TransferToPlayerOwnership()
        {
            SetOwner(BusinessOwnerIdentity.Player());
        }

        public void EnsureOwnerOperatorStaffing(BusinessOwnerIdentity operatorOwner = null)
        {
            runtimeState?.TryAssignOwnerOperator(operatorOwner ?? owner);
        }

        public void SetControlState(BusinessControlState newControlState)
        {
            controlState = ManagerPolicyEffects.SanitizeControlState(newControlState, owner);
        }

        public BusinessControlState CycleControlState()
        {
            if (owner == null || owner.OwnerKind != BusinessOwnerKind.Player)
            {
                controlState = BusinessControlState.ManagerRun;
                return ControlState;
            }

            controlState = ControlState switch
            {
                BusinessControlState.PlayerManaged => BusinessControlState.Assisted,
                BusinessControlState.Assisted => BusinessControlState.ManagerRun,
                _ => BusinessControlState.PlayerManaged
            };

            return ControlState;
        }

        public void SetManagerPolicy(ManagerPolicyPreset newPolicy)
        {
            managerPolicy = ManagerPolicyEffects.SanitizePolicy(newPolicy);
        }

        public ManagerPolicyPreset CycleManagerPolicy()
        {
            managerPolicy = ManagerPolicy switch
            {
                ManagerPolicyPreset.ProfitFirst => ManagerPolicyPreset.ReputationFirst,
                ManagerPolicyPreset.ReputationFirst => ManagerPolicyPreset.StabilityFirst,
                _ => ManagerPolicyPreset.ProfitFirst
            };

            return ManagerPolicy;
        }

        public void ResolveWeeklyBaselineThroughput()
        {
            lastWeeklyThroughputUnits = throughputMode == BusinessThroughputMode.Service
                ? 0
                : Mathf.RoundToInt(Mathf.Max(0, baselineWeeklyThroughputUnits) * OperatingEfficiency01);
            RefreshCapacityState();
        }

        public void ResolveDailyBaselineService()
        {
            lastDailyServiceVisits = Mathf.RoundToInt(Mathf.Max(0, baselineDailyServiceCapacity) * OperatingEfficiency01);
            RefreshCapacityState();
        }

        public void ApplyWeeklyOperationResult(int weeklyThroughputUnits)
        {
            lastWeeklyThroughputUnits = Mathf.Max(0, weeklyThroughputUnits);
            RefreshCapacityState();
        }

        public void ApplyDailyServiceResult(int dailyServiceVisits)
        {
            lastDailyServiceVisits = Mathf.Max(0, dailyServiceVisits);
            RefreshCapacityState();
        }

        public void RefreshCapacityState()
        {
            runtimeState?.RefreshCapacity(
                throughputMode,
                BaselineWeeklyThroughputUnits,
                BaselineDailyServiceCapacity,
                LastWeeklyThroughputUnits,
                LastDailyServiceVisits);
            EnsureBusinessReputationInitializedFromRuntime();
        }

        public int NormalizeForStartup(BusinessProfileDefinition profile, string fallbackInstanceId, int protectedReserveCents)
        {
            int repairs = 0;
            BusinessOwnerIdentity resolvedOwner = owner ?? BusinessOwnerIdentity.Town();
            if (!ReferenceEquals(resolvedOwner, owner))
            {
                owner = resolvedOwner;
                repairs++;
            }

            string resolvedInstanceId = !string.IsNullOrWhiteSpace(instanceId)
                ? instanceId.Trim()
                : !string.IsNullOrWhiteSpace(fallbackInstanceId)
                    ? fallbackInstanceId.Trim()
                    : profile != null ? profile.Business.BusinessId : BusinessRuntimeNaming.GetBusinessTypeDisplayName(businessType);
            string resolvedProfileId = profile != null ? profile.Business.BusinessId : profileId?.Trim() ?? string.Empty;
            BusinessType resolvedType = profile != null ? profile.Business.BusinessType : businessType;
            int resolvedBuildingId = assignedBuildingId < 0 ? -1 : assignedBuildingId;
            string resolvedDisplayName = string.IsNullOrWhiteSpace(runtimeDisplayName)
                ? BusinessRuntimeNaming.Build(resolvedOwner, resolvedType)
                : runtimeDisplayName.Trim();
            if (resolvedType == BusinessType.Sawmill && resolvedDisplayName.IndexOf("Small Mill", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                resolvedDisplayName = resolvedDisplayName.Replace("Small Mill", "Small Sawmill").Replace("small mill", "Small Sawmill");
            }

            BusinessThroughputMode resolvedMode = profile != null ? profile.ThroughputMode : throughputMode;
            int resolvedWeeklyBaseline = Mathf.Max(Mathf.Max(0, baselineWeeklyThroughputUnits), profile != null ? profile.BaselineWeeklyThroughputUnits : 0);
            int resolvedDailyBaseline = Mathf.Max(Mathf.Max(0, baselineDailyServiceCapacity), profile != null ? profile.BaselineDailyServiceCapacity : 0);
            int resolvedLastWeekly = Mathf.Max(0, lastWeeklyThroughputUnits);
            int resolvedLastDaily = Mathf.Max(0, lastDailyServiceVisits);

            if (!string.Equals(resolvedInstanceId, instanceId, StringComparison.Ordinal)
                || !string.Equals(resolvedProfileId, profileId, StringComparison.Ordinal)
                || resolvedType != businessType
                || resolvedBuildingId != assignedBuildingId
                || !string.Equals(resolvedDisplayName, runtimeDisplayName, StringComparison.Ordinal)
                || resolvedMode != throughputMode
                || resolvedWeeklyBaseline != baselineWeeklyThroughputUnits
                || resolvedDailyBaseline != baselineDailyServiceCapacity
                || resolvedLastWeekly != lastWeeklyThroughputUnits
                || resolvedLastDaily != lastDailyServiceVisits)
            {
                repairs++;
            }

            instanceId = resolvedInstanceId;
            profileId = resolvedProfileId;
            businessType = resolvedType;
            assignedBuildingId = resolvedBuildingId;
            runtimeDisplayName = resolvedDisplayName;
            controlState = ManagerPolicyEffects.SanitizeControlState(controlState, resolvedOwner);
            managerPolicy = ManagerPolicyEffects.SanitizePolicy(managerPolicy);
            throughputMode = resolvedMode;
            baselineWeeklyThroughputUnits = resolvedWeeklyBaseline;
            baselineDailyServiceCapacity = resolvedDailyBaseline;
            lastWeeklyThroughputUnits = resolvedLastWeekly;
            lastDailyServiceVisits = resolvedLastDaily;
            cashTransferRule ??= new BusinessCashTransferRuleState();
            businessReputation ??= new BusinessReputationState();

            BusinessRuntimeState template = profile != null ? profile.CreateRuntimeState() : null;
            if (runtimeState == null && template != null)
            {
                runtimeState = template;
                repairs++;
            }
            else if (runtimeState != null)
            {
                repairs += runtimeState.NormalizeForStartup(template, resolvedProfileId, resolvedType, resolvedMode, resolvedWeeklyBaseline, resolvedDailyBaseline);
            }

            repairs += cashTransferRule.NormalizeForStartup(protectedReserveCents);
            businessReputation.Clamp();
            if (resolvedOwner.OwnerKind != BusinessOwnerKind.Player)
            {
                EnsureOwnerOperatorStaffing(resolvedOwner);
            }

            RefreshCapacityState();
            return repairs;
        }


        public string BuildCashReserveInspectionLine(int operatingReserveCents, int survivalReserveCents)
        {
            return BuildPortfolioSummary(operatingReserveCents, survivalReserveCents).BuildCashReserveLine();
        }

        public string BuildStockReadinessInspectionLine()
        {
            return runtimeState != null ? runtimeState.BuildStockReadinessLine() : "Stock readiness: runtime unavailable.";
        }

        public void EnsureBusinessReputationInitializedFromRuntime()
        {
            BusinessReputationState reputation = BusinessReputation;
            if (runtimeState == null)
            {
                reputation.Clamp();
                return;
            }

            if (reputation.initializedFromRuntime)
            {
                reputation.Clamp();
                return;
            }

            reputation.ApplyRuntimeBaseline(
                runtimeState.StockHealth01,
                runtimeState.Reliability01,
                OperatingEfficiency01);
        }

        public void RecordBusinessReputationObservation(BusinessReputationObservation observation)
        {
            EnsureBusinessReputationInitializedFromRuntime();
            new BusinessReputationEvaluator().ApplyObservation(BusinessReputation, observation);
        }

        public BusinessInstanceSaveDto CaptureSaveDto()
        {
            return new BusinessInstanceSaveDto
            {
                instanceId = InstanceId,
                profileId = ProfileId,
                businessType = BusinessType,
                assignedBuildingId = AssignedBuildingId,
                runtimeDisplayName = RuntimeDisplayName,
                owner = (owner ?? BusinessOwnerIdentity.Town()).CaptureSaveDto(),
                controlState = ControlState,
                managerPolicy = ManagerPolicy,
                throughputMode = ThroughputMode,
                baselineWeeklyThroughputUnits = BaselineWeeklyThroughputUnits,
                baselineDailyServiceCapacity = BaselineDailyServiceCapacity,
                lastWeeklyThroughputUnits = LastWeeklyThroughputUnits,
                lastDailyServiceVisits = LastDailyServiceVisits,
                cashTransferRule = CashTransferRule.CaptureSaveDto(),
                businessReputation = CaptureBusinessReputationSaveDto(BusinessReputation),
                runtime = runtimeState != null ? runtimeState.CaptureSaveDto() : null,
                mine = mineState != null ? mineState.CaptureSaveDto() : null
            };
        }

        public static BusinessInstanceState FromSaveDto(BusinessInstanceSaveDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.instanceId))
            {
                return null;
            }

            BusinessOwnerIdentity resolvedOwner = BusinessOwnerIdentity.FromSaveDto(dto.owner);
            BusinessInstanceState state = new()
            {
                instanceId = dto.instanceId,
                profileId = dto.profileId ?? string.Empty,
                businessType = dto.businessType,
                assignedBuildingId = dto.assignedBuildingId,
                runtimeDisplayName = string.IsNullOrWhiteSpace(dto.runtimeDisplayName)
                    ? BusinessRuntimeNaming.Build(resolvedOwner, dto.businessType)
                    : dto.runtimeDisplayName,
                owner = resolvedOwner,
                controlState = ManagerPolicyEffects.SanitizeControlState(dto.controlState, resolvedOwner),
                managerPolicy = ManagerPolicyEffects.SanitizePolicy(dto.managerPolicy),
                throughputMode = dto.throughputMode,
                baselineWeeklyThroughputUnits = Mathf.Max(0, dto.baselineWeeklyThroughputUnits),
                baselineDailyServiceCapacity = Mathf.Max(0, dto.baselineDailyServiceCapacity),
                lastWeeklyThroughputUnits = Mathf.Max(0, dto.lastWeeklyThroughputUnits),
                lastDailyServiceVisits = Mathf.Max(0, dto.lastDailyServiceVisits),
                cashTransferRule = BusinessCashTransferRuleState.FromSaveDto(dto.cashTransferRule),
                businessReputation = BusinessReputationFromSaveDto(dto.businessReputation),
                runtimeState = BusinessRuntimeState.FromSaveDto(dto.runtime, dto.businessType),
                mineState = dto.businessType == BusinessType.Mine
                    ? MineRuntimeState.FromSaveDto(dto.mine) ?? MineRuntimeState.CreateDefault(MineralResourceKind.Coal)
                    : null
            };

            if (state.businessType == BusinessType.Sawmill)
            {
                if (string.Equals(state.profileId, "sawmill_small_mill", StringComparison.OrdinalIgnoreCase))
                {
                    state.profileId = "sawmill_small_sawmill";
                }

                if (!string.IsNullOrWhiteSpace(state.runtimeDisplayName)
                    && state.runtimeDisplayName.IndexOf("Small Mill", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    state.runtimeDisplayName = state.runtimeDisplayName.Replace("Small Mill", "Small Sawmill");
                    state.runtimeDisplayName = state.runtimeDisplayName.Replace("small mill", "Small Sawmill");
                }
            }

            state.RefreshCapacityState();
            return state;
        }

        public void SetMineState(MineRuntimeState state)
        {
            mineState = businessType == BusinessType.Mine ? state : null;
        }

        private static BusinessReputationSaveDto CaptureBusinessReputationSaveDto(BusinessReputationState reputation)
        {
            BusinessReputationState source = reputation != null ? reputation.Clone() : new BusinessReputationState();
            source.Clamp();
            BusinessReputationSaveDto dto = new()
            {
                stockReliability01 = source.stockReliability01,
                valueFairness01 = source.valueFairness01,
                serviceExperience01 = source.serviceExperience01,
                conditionPresentationTrust01 = source.conditionPresentationTrust01,
                productTradeConfidence01 = source.productTradeConfidence01,
                initializedFromRuntime = source.initializedFromRuntime
            };

            if (source.stockoutMemory != null)
            {
                for (int i = 0; i < source.stockoutMemory.Count; i++)
                {
                    BusinessStockoutMemoryState memory = source.stockoutMemory[i];
                    if (memory == null || string.IsNullOrWhiteSpace(memory.categoryId))
                    {
                        continue;
                    }

                    dto.stockoutMemory.Add(new BusinessStockoutMemorySaveDto
                    {
                        categoryId = memory.categoryId,
                        lastMissedDayIndex = memory.lastMissedDayIndex,
                        lastMissedWeekKey = memory.lastMissedWeekKey,
                        oneOffMissCount = memory.oneOffMissCount,
                        consecutiveMissCount = memory.consecutiveMissCount,
                        recoveryStreak = memory.recoveryStreak,
                        lifetimeMissCount = memory.lifetimeMissCount,
                        lastSeverity01 = memory.lastSeverity01
                    });
                }
            }

            return dto;
        }

        private static BusinessReputationState BusinessReputationFromSaveDto(BusinessReputationSaveDto dto)
        {
            if (dto == null)
            {
                return new BusinessReputationState();
            }

            BusinessReputationState state = new(
                dto.stockReliability01,
                dto.valueFairness01,
                dto.serviceExperience01,
                dto.conditionPresentationTrust01,
                dto.productTradeConfidence01,
                dto.initializedFromRuntime);
            state.stockoutMemory.Clear();
            if (dto.stockoutMemory != null)
            {
                for (int i = 0; i < dto.stockoutMemory.Count; i++)
                {
                    BusinessStockoutMemorySaveDto saved = dto.stockoutMemory[i];
                    if (saved == null || string.IsNullOrWhiteSpace(saved.categoryId))
                    {
                        continue;
                    }

                    state.stockoutMemory.Add(new BusinessStockoutMemoryState
                    {
                        categoryId = saved.categoryId,
                        lastMissedDayIndex = saved.lastMissedDayIndex,
                        lastMissedWeekKey = saved.lastMissedWeekKey,
                        oneOffMissCount = saved.oneOffMissCount,
                        consecutiveMissCount = saved.consecutiveMissCount,
                        recoveryStreak = saved.recoveryStreak,
                        lifetimeMissCount = saved.lifetimeMissCount,
                        lastSeverity01 = saved.lastSeverity01
                    });
                }
            }

            state.Clamp();
            return state;
        }
    }

    public static class BusinessRuntimeNaming
    {
        public static string Build(BusinessOwnerIdentity owner, BusinessType businessType)
        {
            BusinessOwnerIdentity resolvedOwner = owner ?? BusinessOwnerIdentity.Town();
            string typeName = GetBusinessTypeDisplayName(businessType);
            if (resolvedOwner.OwnerKind == BusinessOwnerKind.Player)
            {
                return $"Player {typeName}";
            }

            string surname = string.IsNullOrWhiteSpace(resolvedOwner.Surname) ? resolvedOwner.DisplayName : resolvedOwner.Surname;
            if (businessType == BusinessType.Doctor)
            {
                return $"Town Doctor {surname}";
            }

            return $"{surname} {typeName}";
        }

        public static string GetBusinessTypeDisplayName(BusinessType businessType)
        {
            return businessType switch
            {
                BusinessType.GeneralStore => "General Store",
                BusinessType.Blacksmith => "Blacksmith",
                BusinessType.Butcher => "Butcher",
                BusinessType.Ranch => "Ranch",
                BusinessType.CropFarm => "Crop Farm",
                BusinessType.Doctor => "Doctor",
                BusinessType.Sawmill => "Small Sawmill",
                BusinessType.LumberYard => "Lumber Yard",
                BusinessType.BoardingHouse => "Boarding House",
                BusinessType.LiveryFreight => "Livery & Freight",
                BusinessType.Builder => "Builder",
                BusinessType.FuelDealer => "Fuel Dealer",
                BusinessType.GrainMill => "Grain Mill",
                BusinessType.Bakery => "Bakery",
                BusinessType.Tailor => "Tailor",
                BusinessType.Saloon => "Saloon",
                BusinessType.Barber => "Barber",
                BusinessType.Wheelwright => "Wheelwright",
                BusinessType.Mine => "Mine",
                _ => "Business"
            };
        }
    }
}
