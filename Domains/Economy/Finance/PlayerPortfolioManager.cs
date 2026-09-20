using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.MVP;
using LandLedgers.Persistence;
using LandLedgers.Time;
using LandLedgers.UI;
using UnityEngine;

namespace LandLedgers.Economy
{
    [Serializable]
    public sealed class OwnerDistributionCheckpoint
    {
        public string businessInstanceId = string.Empty;
        public int lastCashCheckpointCents;
        public int lastDistributionCents;
        public int lastDistributionWeekKey = -1;
        public int openedWeekKey = -1;

        public OwnerDistributionCheckpointSaveDto CaptureSaveDto()
        {
            return new OwnerDistributionCheckpointSaveDto
            {
                businessInstanceId = businessInstanceId ?? string.Empty,
                lastCashCheckpointCents = Mathf.Max(0, lastCashCheckpointCents),
                lastDistributionCents = Mathf.Max(0, lastDistributionCents),
                lastDistributionWeekKey = lastDistributionWeekKey,
                openedWeekKey = openedWeekKey
            };
        }

        public static OwnerDistributionCheckpoint FromSaveDto(OwnerDistributionCheckpointSaveDto dto)
        {
            return new OwnerDistributionCheckpoint
            {
                businessInstanceId = dto != null ? dto.businessInstanceId ?? string.Empty : string.Empty,
                lastCashCheckpointCents = Mathf.Max(0, dto != null ? dto.lastCashCheckpointCents : 0),
                lastDistributionCents = Mathf.Max(0, dto != null ? dto.lastDistributionCents : 0),
                lastDistributionWeekKey = dto != null ? dto.lastDistributionWeekKey : -1,
                openedWeekKey = dto != null ? dto.openedWeekKey : -1
            };
        }
    }

    public enum BusinessCashAutoTransferMode
    {
        LowerOnly = 0,
        UpperOnly = 1
    }

    public readonly struct PlayerWealthSnapshot
    {
        public PlayerWealthSnapshot(
            int liquidCashCents,
            int businessCashCents,
            int assetValueCents,
            int debtLiabilityCents,
            int netWorthCents,
            int winTargetCents,
            bool winReached)
            : this(
                liquidCashCents,
                businessCashCents,
                businessCashCents,
                0,
                assetValueCents,
                debtLiabilityCents,
                netWorthCents,
                winTargetCents,
                winReached)
        {
        }

        public PlayerWealthSnapshot(
            int liquidCashCents,
            int businessCashCents,
            int businessEquityCents,
            int businessLiabilityCents,
            int assetValueCents,
            int debtLiabilityCents,
            int netWorthCents,
            int winTargetCents,
            bool winReached)
        {
            LiquidCashCents = Mathf.Max(0, liquidCashCents);
            BusinessCashCents = Mathf.Max(0, businessCashCents);
            BusinessEquityCents = Mathf.Max(0, businessEquityCents);
            BusinessLiabilityCents = Mathf.Max(0, businessLiabilityCents);
            AssetValueCents = Mathf.Max(0, assetValueCents);
            DebtLiabilityCents = Mathf.Max(0, debtLiabilityCents);
            NetWorthCents = netWorthCents;
            WinTargetCents = Mathf.Max(0, winTargetCents);
            WinReached = winReached;
        }

        public int LiquidCashCents { get; }
        public int BusinessCashCents { get; }
        public int BusinessEquityCents { get; }
        public int BusinessLiabilityCents { get; }
        public int AssetValueCents { get; }
        public int DebtLiabilityCents { get; }
        public int NetWorthCents { get; }
        public int WinTargetCents { get; }
        public bool WinReached { get; }
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(284)]
    public sealed class PlayerPortfolioManager : MonoBehaviour
    {
        private const int DistributionDivisor = 4;
        private const int DistributionGraceWeeks = 4;
        private const int MinimumAutomaticTransferCents = 100;
        private const int DefaultNetWorthWinTargetCents = 1000000;
        private const string DefaultWealthComponentAuditSummary = "Wealth components not audited yet.";
        private const string DefaultMonthlyWealthReviewSummary = "Monthly wealth review not captured yet.";

        [Header("Sources")]
        [SerializeField]
        private TimeManager timeManager;

        [SerializeField]
        private LandLedgersHUDController hudController;

        [SerializeField]
        private GeneralStoreRuntimeManager generalStoreRuntime;

        [SerializeField]
        private SharedBusinessRuntimeManager sharedBusinessRuntime;

        [SerializeField]
        private AcquisitionMarketManager acquisitionMarket;

        [SerializeField]
        private PlayerDebtManager playerDebtManager;

        [Header("Owner Cash")]
        [SerializeField, Min(0)]
        private int ownerCashCents;

        [SerializeField]
        private bool initialized;

        [SerializeField, TextArea(1, 3)]
        private string lastStatusSummary = "Owner portfolio not initialized.";

        [SerializeField, TextArea(1, 3)]
        private string lastTransactionSummary = string.Empty;

        [Header("Wealth")]
        [SerializeField, Min(0)]
        private int netWorthWinTargetCents = DefaultNetWorthWinTargetCents;

        [SerializeField]
        private int lastNetWorthCents;

        [SerializeField]
        private int lastBusinessCashCents;

        [SerializeField]
        private int lastBusinessEquityCents;

        [SerializeField]
        private int lastBusinessLiabilityCents;

        [SerializeField]
        private int lastAssetValueCents;

        [SerializeField]
        private int lastDebtLiabilityCents;

        [SerializeField]
        private bool netWorthWinReached;

        [Header("Wealth Component Policy")]
        [SerializeField]
        [Tooltip("Enable only if BusinessPortfolioSummary.EquityContributionCents is confirmed to already include business cash. When enabled, business cash is subtracted from equity before Net Worth is assembled.")]
        private bool businessEquityContributionIncludesBusinessCash;

        [SerializeField, TextArea(2, 5)]
        private string lastWealthComponentAuditSummary = DefaultWealthComponentAuditSummary;

        [Header("Monthly Wealth Review")]
        [SerializeField]
        private int lastMonthlyWealthReviewKey = -1;

        [SerializeField]
        private int monthlyOpeningNetWorthCents;

        [SerializeField]
        private int monthlyClosingNetWorthCents;

        [SerializeField, Min(0)]
        private int monthlyOpeningDebtCents;

        [SerializeField, Min(0)]
        private int monthlyClosingDebtCents;

        [SerializeField, Min(0)]
        private int monthlyOpeningBusinessCashCents;

        [SerializeField, Min(0)]
        private int monthlyClosingBusinessCashCents;

        [SerializeField, TextArea(2, 5)]
        private string lastMonthlyWealthReviewSummary = DefaultMonthlyWealthReviewSummary;

        [Header("Weekly Owner Distributions")]
        [SerializeField, Min(0)]
        private int lastWeeklyDistributionCents;

        [SerializeField]
        private int lastWeeklyDistributionWeekKey = -1;

        [SerializeField, TextArea(2, 5)]
        private string lastWeeklyDistributionSummary = "No owner distributions resolved yet.";

        [SerializeField]
        private List<OwnerDistributionCheckpoint> distributionCheckpoints = new();

        public int OwnerCashCents => Mathf.Max(0, ownerCashCents);
        public bool IsInitialized => initialized;
        public int LastWeeklyDistributionCents => Mathf.Max(0, lastWeeklyDistributionCents);
        public int LastWeeklyDistributionWeekKey => lastWeeklyDistributionWeekKey;
        public string LastStatusSummary => lastStatusSummary ?? string.Empty;
        public string LastTransactionSummary => lastTransactionSummary ?? string.Empty;
        public int NetWorthCents => lastNetWorthCents;
        public int BusinessCashCents => Mathf.Max(0, lastBusinessCashCents);
        public int BusinessEquityCents => Mathf.Max(0, lastBusinessEquityCents);
        public int BusinessLiabilityCents => Mathf.Max(0, lastBusinessLiabilityCents);
        public int AssetValueCents => Mathf.Max(0, lastAssetValueCents);
        public int DebtLiabilityCents => Mathf.Max(0, lastDebtLiabilityCents);
        public int NetWorthWinTargetCents => Mathf.Max(0, netWorthWinTargetCents);
        public bool NetWorthWinReached => netWorthWinReached;
        public bool BusinessEquityContributionIncludesBusinessCash => businessEquityContributionIncludesBusinessCash;
        public string LastWealthComponentAuditSummary => lastWealthComponentAuditSummary ?? string.Empty;
        public int LastMonthlyWealthReviewKey => lastMonthlyWealthReviewKey;
        public string CurrentMonthlyWealthReviewWindowLabel => BuildMonthlyWealthReviewWindowLabel();
        public int MonthlyOpeningNetWorthCents => monthlyOpeningNetWorthCents;
        public int MonthlyClosingNetWorthCents => monthlyClosingNetWorthCents;
        public int MonthlyNetWorthDriftCents => SubtractCents(monthlyClosingNetWorthCents, monthlyOpeningNetWorthCents);
        public int MonthlyOpeningDebtCents => Mathf.Max(0, monthlyOpeningDebtCents);
        public int MonthlyClosingDebtCents => Mathf.Max(0, monthlyClosingDebtCents);
        public int MonthlyDebtDriftCents => SubtractCents(MonthlyClosingDebtCents, MonthlyOpeningDebtCents);
        public int MonthlyOpeningBusinessCashCents => Mathf.Max(0, monthlyOpeningBusinessCashCents);
        public int MonthlyClosingBusinessCashCents => Mathf.Max(0, monthlyClosingBusinessCashCents);
        public int MonthlyBusinessCashDriftCents => SubtractCents(MonthlyClosingBusinessCashCents, MonthlyOpeningBusinessCashCents);
        public string LastMonthlyWealthReviewSummary => lastMonthlyWealthReviewSummary ?? string.Empty;
        public string LastWeeklyDistributionSummary => lastWeeklyDistributionSummary ?? string.Empty;
        public IReadOnlyList<OwnerDistributionCheckpoint> DistributionCheckpoints => distributionCheckpoints;

        public void Configure(TimeManager newTimeManager, LandLedgersHUDController newHudController)
        {
            timeManager = newTimeManager;
            hudController = newHudController;
            PushOwnerCashToHud();
        }

        public PlayerPortfolioSaveDto CaptureSaveDto()
        {
            Sanitize();
            BuildWealthSnapshot();
            PlayerPortfolioSaveDto dto = new()
            {
                initialized = initialized,
                ownerCashCents = OwnerCashCents,
                lastStatusSummary = LastStatusSummary,
                lastTransactionSummary = LastTransactionSummary,
                lastWeeklyDistributionCents = LastWeeklyDistributionCents,
                lastWeeklyDistributionWeekKey = lastWeeklyDistributionWeekKey,
                lastWeeklyDistributionSummary = LastWeeklyDistributionSummary,
                netWorthWinTargetCents = NetWorthWinTargetCents,
                lastNetWorthCents = NetWorthCents,
                lastBusinessCashCents = BusinessCashCents,
                lastBusinessEquityCents = BusinessEquityCents,
                lastBusinessLiabilityCents = BusinessLiabilityCents,
                lastAssetValueCents = AssetValueCents,
                lastDebtLiabilityCents = DebtLiabilityCents,
                netWorthWinReached = NetWorthWinReached
            };

            for (int i = 0; i < distributionCheckpoints.Count; i++)
            {
                OwnerDistributionCheckpoint checkpoint = distributionCheckpoints[i];
                if (checkpoint != null && !string.IsNullOrWhiteSpace(checkpoint.businessInstanceId))
                {
                    dto.distributionCheckpoints.Add(checkpoint.CaptureSaveDto());
                }
            }

            return dto;
        }

        public void LoadFromSaveDto(PlayerPortfolioSaveDto dto)
        {
            distributionCheckpoints.Clear();
            if (dto == null || !dto.initialized)
            {
                initialized = false;
                ownerCashCents = 0;
                lastStatusSummary = "Owner portfolio awaiting legacy initialization.";
                lastTransactionSummary = string.Empty;
                lastWeeklyDistributionCents = 0;
                lastWeeklyDistributionWeekKey = -1;
                lastWeeklyDistributionSummary = "No owner distributions resolved yet.";
                netWorthWinTargetCents = DefaultNetWorthWinTargetCents;
                lastNetWorthCents = 0;
                lastBusinessCashCents = 0;
                lastBusinessEquityCents = 0;
                lastBusinessLiabilityCents = 0;
                lastAssetValueCents = 0;
                lastDebtLiabilityCents = 0;
                netWorthWinReached = false;
                ResetMonthlyWealthReviewFromCurrentComponents();
                PushOwnerCashToHud();
                return;
            }

            initialized = true;
            ownerCashCents = Mathf.Max(0, dto.ownerCashCents);
            lastStatusSummary = string.IsNullOrWhiteSpace(dto.lastStatusSummary) ? "Owner portfolio restored." : dto.lastStatusSummary;
            lastTransactionSummary = dto.lastTransactionSummary ?? string.Empty;
            lastWeeklyDistributionCents = Mathf.Max(0, dto.lastWeeklyDistributionCents);
            lastWeeklyDistributionWeekKey = dto.lastWeeklyDistributionWeekKey;
            lastWeeklyDistributionSummary = string.IsNullOrWhiteSpace(dto.lastWeeklyDistributionSummary)
                ? "No owner distributions resolved yet."
                : dto.lastWeeklyDistributionSummary;
            netWorthWinTargetCents = dto.netWorthWinTargetCents > 0 ? dto.netWorthWinTargetCents : DefaultNetWorthWinTargetCents;
            lastNetWorthCents = dto.lastNetWorthCents;
            lastBusinessCashCents = Mathf.Max(0, dto.lastBusinessCashCents);
            lastBusinessEquityCents = Mathf.Max(0, dto.lastBusinessEquityCents);
            lastBusinessLiabilityCents = Mathf.Max(0, dto.lastBusinessLiabilityCents);
            lastAssetValueCents = Mathf.Max(0, dto.lastAssetValueCents);
            lastDebtLiabilityCents = Mathf.Max(0, dto.lastDebtLiabilityCents);
            netWorthWinReached = dto.netWorthWinReached;

            if (dto.distributionCheckpoints != null)
            {
                for (int i = 0; i < dto.distributionCheckpoints.Count; i++)
                {
                    OwnerDistributionCheckpoint checkpoint = OwnerDistributionCheckpoint.FromSaveDto(dto.distributionCheckpoints[i]);
                    if (!string.IsNullOrWhiteSpace(checkpoint.businessInstanceId))
                    {
                        distributionCheckpoints.Add(checkpoint);
                    }
                }
            }

            Sanitize();
            ResetMonthlyWealthReviewFromCurrentComponents();
            PushOwnerCashToHud();
        }

        public void InitializeFromLegacyBusinessCash(
            GeneralStoreRuntimeManager storeRuntime,
            SharedBusinessRuntimeManager sharedBusinessRuntime)
        {
            if (initialized)
            {
                RegisterOwnedBusinessCheckpoints(storeRuntime, sharedBusinessRuntime);
                PushOwnerCashToHud();
                return;
            }

            initialized = true;
            int migratedCents = 0;
            if (storeRuntime != null && storeRuntime.RuntimeState != null)
            {
                int protectedReserve = Mathf.Max(0, storeRuntime.ProtectedBusinessCashReserveCents);
                migratedCents = PositiveDifferenceCents(storeRuntime.CurrentCashCents, protectedReserve);
                if (migratedCents > 0)
                {
                    storeRuntime.RuntimeState.SpendCents(migratedCents);
                    AddOwnerCashInternal(migratedCents);
                }
            }

            RegisterOwnedBusinessCheckpoints(storeRuntime, sharedBusinessRuntime);
            lastStatusSummary = migratedCents > 0
                ? $"Owner portfolio initialized by moving {FormatMoney(migratedCents)} surplus General Store cash to owner cash."
                : "Owner portfolio initialized. No surplus General Store cash was available above operating reserve.";
            lastTransactionSummary = lastStatusSummary;
            PushOwnerCashToHud();
        }

        public bool TrySpendOwnerCash(int cents, string reason, out string message)
        {
            int amount = Mathf.Max(0, cents);
            string label = SanitizeReason(reason);
            if (OwnerCashCents < amount)
            {
                message = $"Not enough owner cash for {label}. Need {FormatMoney(amount)}, have {FormatMoney(OwnerCashCents)}.";
                lastStatusSummary = message;
                return false;
            }

            TrySpendOwnerCashInternal(amount);
            lastTransactionSummary = $"Spent {FormatMoney(amount)} owner cash on {label}.";
            lastStatusSummary = lastTransactionSummary;
            message = $"Purchased {label} for {FormatMoney(amount)}.";
            PushOwnerCashToHud();
            return true;
        }

        public void RefundOwnerCash(int cents, string reason)
        {
            int amount = Mathf.Max(0, cents);
            if (amount <= 0)
            {
                return;
            }

            AddOwnerCashInternal(amount);
            lastTransactionSummary = $"Refunded {FormatMoney(amount)} owner cash after failed {SanitizeReason(reason)}.";
            lastStatusSummary = lastTransactionSummary;
            PushOwnerCashToHud();
        }

        public void AddOwnerCash(int cents, string reason)
        {
            int amount = Mathf.Max(0, cents);
            if (amount <= 0)
            {
                return;
            }

            AddOwnerCashInternal(amount);
            string label = SanitizeReason(reason);
            lastTransactionSummary = $"Received {FormatMoney(amount)} owner cash from {label}.";
            lastStatusSummary = lastTransactionSummary;
            PushOwnerCashToHud();
        }

        public bool TryTransferOwnerBusinessCash(
            BusinessInstanceState business,
            int signedCents,
            int protectedReserveCents,
            out string message)
        {
            message = string.Empty;
            if (!IsBusinessCashTransferEligible(business, out message))
            {
                lastStatusSummary = message;
                return false;
            }

            int amount = SafeAbsCents(signedCents);
            if (amount <= 0)
            {
                message = "Blocked: no transfer amount.";
                lastStatusSummary = message;
                business.CashTransferRule.RecordTransferSummary(message, GetCurrentWeekKey());
                return false;
            }

            if (signedCents > 0)
            {
                if (OwnerCashCents < amount)
                {
                    message = $"Blocked: owner cash {FormatMoney(OwnerCashCents)}.";
                    lastStatusSummary = message;
                    business.CashTransferRule.RecordTransferSummary(message, GetCurrentWeekKey());
                    return false;
                }

                TrySpendOwnerCashInternal(amount);
                business.RuntimeState.AdjustCashCents(amount);
                business.RuntimeState.RecordWeeklyCashTransferIn(amount);
                message = $"Deposited {FormatMoney(amount)} into {ResolveBusinessLabel(business, "business")} from owner cash.";
                CompleteBusinessCashTransfer(business, message, GetCurrentWeekKey());
                return true;
            }

            int businessCash = Mathf.Max(0, business.RuntimeState.CurrentCashCents);
            if (businessCash < amount)
            {
                message = $"Blocked: business cash {FormatMoney(businessCash)}.";
                lastStatusSummary = message;
                business.CashTransferRule.RecordTransferSummary(message, GetCurrentWeekKey());
                return false;
            }

            int reserve = Mathf.Max(0, protectedReserveCents);
            int remainingAfterWithdrawal = PositiveDifferenceCents(businessCash, amount);
            if (remainingAfterWithdrawal < reserve)
            {
                int safelyWithdrawable = PositiveDifferenceCents(businessCash, reserve);
                message = $"Blocked: {ResolveBusinessLabel(business, "business")} needs {FormatMoney(reserve)} protected reserve. Available to withdraw above reserve {FormatMoney(safelyWithdrawable)}.";
                lastStatusSummary = message;
                business.CashTransferRule.RecordTransferSummary(message, GetCurrentWeekKey());
                return false;
            }

            business.RuntimeState.AdjustCashCents(-amount);
            business.RuntimeState.RecordWeeklyCashTransferOut(amount);
            AddOwnerCashInternal(amount);
            message = $"Withdrew {FormatMoney(amount)} from {ResolveBusinessLabel(business, "business")} to owner cash.";
            CompleteBusinessCashTransfer(business, message, GetCurrentWeekKey());
            return true;
        }

        public bool TryResolveAutomaticBusinessCashTransfer(
            BusinessInstanceState business,
            int protectedReserveCents,
            int weekKey,
            BusinessCashAutoTransferMode mode,
            out int transferredCents,
            out string message)
        {
            transferredCents = 0;
            message = string.Empty;
            if (!IsBusinessCashTransferEligible(business, out message))
            {
                return false;
            }

            BusinessCashTransferRuleState rule = business.CashTransferRule;
            if (!rule.AutoTransferEnabled)
            {
                return false;
            }

            int reserve = Mathf.Max(0, protectedReserveCents);
            int lower = rule.GetEffectiveLowerThresholdCents(reserve);
            int upper = rule.GetEffectiveUpperThresholdCents(reserve);
            int currentCash = business.RuntimeState.CurrentCashCents;

            if (mode == BusinessCashAutoTransferMode.LowerOnly)
            {
                if (currentCash >= lower)
                {
                    return false;
                }

                int requested = lower - currentCash;
                int amount = Mathf.Min(requested, OwnerCashCents);
                if (amount < MinimumAutomaticTransferCents)
                {
                    message = $"Auto top-up skipped: owner cash {FormatMoney(OwnerCashCents)}.";
                    rule.RecordTransferSummary(message, weekKey);
                    return false;
                }

                TrySpendOwnerCashInternal(amount);
                business.RuntimeState.AdjustCashCents(amount);
                business.RuntimeState.RecordWeeklyCashTransferIn(amount);
                transferredCents = amount;
                message = $"Auto deposited {FormatMoney(amount)} into {ResolveBusinessLabel(business, "business")}.";
                CompleteBusinessCashTransfer(business, message, weekKey);
                return true;
            }

            if (currentCash <= upper)
            {
                return false;
            }

            int excessAboveUpper = currentCash - upper;
            int safelyWithdrawable = PositiveDifferenceCents(currentCash, reserve);
            int withdrawal = Mathf.Min(excessAboveUpper, safelyWithdrawable);
            if (withdrawal < MinimumAutomaticTransferCents)
            {
                message = $"Auto withdrawal skipped: below {FormatMoney(MinimumAutomaticTransferCents)}.";
                rule.RecordTransferSummary(message, weekKey);
                return false;
            }

            business.RuntimeState.AdjustCashCents(-withdrawal);
            business.RuntimeState.RecordWeeklyCashTransferOut(withdrawal);
            AddOwnerCashInternal(withdrawal);
            transferredCents = withdrawal;
            message = $"Auto withdrew {FormatMoney(withdrawal)} from {ResolveBusinessLabel(business, "business")} to owner cash.";
            CompleteBusinessCashTransfer(business, message, weekKey);
            return true;
        }

        public bool TrySetBusinessAutoTransferEnabled(
            BusinessInstanceState business,
            bool enabled,
            int protectedReserveCents,
            out string message)
        {
            if (!IsBusinessCashTransferEligible(business, out message))
            {
                lastStatusSummary = message;
                return false;
            }

            business.CashTransferRule.SetAutoTransferEnabled(enabled);
            int lower = business.CashTransferRule.GetEffectiveLowerThresholdCents(protectedReserveCents);
            int upper = business.CashTransferRule.GetEffectiveUpperThresholdCents(protectedReserveCents);
            message = enabled
                ? $"Auto transfer on: {FormatMoney(lower)}-{FormatMoney(upper)}."
                : "Auto transfer off.";
            business.CashTransferRule.RecordTransferSummary(message, GetCurrentWeekKey());
            lastStatusSummary = message;
            return true;
        }

        public bool TryAdjustBusinessCashLowerThreshold(
            BusinessInstanceState business,
            int deltaCents,
            int protectedReserveCents,
            out string message)
        {
            if (!IsBusinessCashTransferEligible(business, out message))
            {
                lastStatusSummary = message;
                return false;
            }

            business.CashTransferRule.AdjustLowerThresholdCents(deltaCents, protectedReserveCents);
            message = business.CashTransferRule.LastTransferSummary;
            lastStatusSummary = message;
            return true;
        }

        public bool TryAdjustBusinessCashUpperThreshold(
            BusinessInstanceState business,
            int deltaCents,
            int protectedReserveCents,
            out string message)
        {
            if (!IsBusinessCashTransferEligible(business, out message))
            {
                lastStatusSummary = message;
                return false;
            }

            business.CashTransferRule.AdjustUpperThresholdCents(deltaCents, protectedReserveCents);
            message = business.CashTransferRule.LastTransferSummary;
            lastStatusSummary = message;
            return true;
        }

        public bool TrySetBusinessCashThresholds(
            BusinessInstanceState business,
            int lowerThresholdCents,
            int upperThresholdCents,
            int protectedReserveCents,
            out string message)
        {
            if (!IsBusinessCashTransferEligible(business, out message))
            {
                lastStatusSummary = message;
                return false;
            }

            business.CashTransferRule.SetAutoTransferThresholds(lowerThresholdCents, upperThresholdCents, protectedReserveCents);
            int lower = business.CashTransferRule.GetEffectiveLowerThresholdCents(protectedReserveCents);
            int upper = business.CashTransferRule.GetEffectiveUpperThresholdCents(protectedReserveCents);
            message = $"Reserve rules set: refill below {FormatMoney(lower)}, sweep above {FormatMoney(upper)}.";
            business.CashTransferRule.RecordTransferSummary(message, GetCurrentWeekKey());
            lastStatusSummary = message;
            return true;
        }

        public void ConfigureDefaultBusinessCashTransfers(
            BusinessInstanceState business,
            int protectedReserveCents,
            int weeklyOperatingReserveCents,
            int weekKey)
        {
            if (!IsBusinessCashTransferEligible(business, out _))
            {
                return;
            }

            int survivalReserve = CalculateSurvivalReserveCents(
                protectedReserveCents,
                business.RuntimeState.FilledWeeklyPayrollCents,
                weeklyOperatingReserveCents);
            int operatingNeed = Mathf.Max(
                BusinessCashTransferRuleState.MinimumThresholdGapCents,
                business.RuntimeState.FilledWeeklyPayrollCents + Mathf.Max(0, weeklyOperatingReserveCents));
            int upper = AddNonNegativeCents(survivalReserve, MultiplyNonNegativeCents(operatingNeed, 2));
            business.CashTransferRule.ConfigureAutoTransferThresholds(survivalReserve, upper, protectedReserveCents);
            business.CashTransferRule.RecordTransferSummary(
                $"Auto transfer default: {FormatMoney(survivalReserve)}-{FormatMoney(upper)}.",
                weekKey);
        }

        public int ResolveOwnerDistribution(
            BusinessInstanceState business,
            int protectedReserveCents,
            int weekKey,
            string sourceLabel,
            int weeklyOperatingReserveCents = 0)
        {
            if (!IsDistributionEligible(business))
            {
                return 0;
            }

            BeginDistributionWeekIfNeeded(weekKey);
            OwnerDistributionCheckpoint checkpoint = FindCheckpoint(business.InstanceId);
            int currentCash = business.RuntimeState.CurrentCashCents;
            int weeklyReserve = weeklyOperatingReserveCents > 0 ? weeklyOperatingReserveCents : business.RuntimeState.LastWeeklyReorderBudgetCents;
            int survivalReserve = CalculateSurvivalReserveCents(
                protectedReserveCents,
                business.RuntimeState.FilledWeeklyPayrollCents,
                weeklyReserve);
            string businessLabel = ResolveBusinessLabel(business, sourceLabel);
            if (checkpoint == null)
            {
                distributionCheckpoints.Add(new OwnerDistributionCheckpoint
                {
                    businessInstanceId = business.InstanceId,
                    lastCashCheckpointCents = currentCash,
                    lastDistributionCents = 0,
                    lastDistributionWeekKey = weekKey,
                    openedWeekKey = weekKey
                });
                AppendDistributionSummary($"{businessLabel} established owner draw checkpoint; protected {FormatMoney(survivalReserve)}");

                return 0;
            }

            if (IsWithinDistributionGrace(checkpoint, weekKey))
            {
                checkpoint.lastCashCheckpointCents = currentCash;
                checkpoint.lastDistributionCents = 0;
                checkpoint.lastDistributionWeekKey = weekKey;
                AppendDistributionSummary($"{businessLabel} held cash for startup reserve ({FormatMoney(currentCash)} cash / {FormatMoney(survivalReserve)} reserve)");
                return 0;
            }

            int positiveGrowth = PositiveDifferenceCents(currentCash, checkpoint.lastCashCheckpointCents);
            if (business.RuntimeState.LastWeeklyCashBeforeCents > 0
                && currentCash <= business.RuntimeState.LastWeeklyCashBeforeCents)
            {
                checkpoint.lastCashCheckpointCents = currentCash;
                checkpoint.lastDistributionCents = 0;
                checkpoint.lastDistributionWeekKey = weekKey;
                AppendDistributionSummary($"{businessLabel} held cash: week closed weaker or flat (start {FormatMoney(business.RuntimeState.LastWeeklyCashBeforeCents)}, cash {FormatMoney(currentCash)})");
                PushOwnerCashToHud();
                return 0;
            }

            int cashAboveReserve = PositiveDifferenceCents(currentCash, survivalReserve);
            int distribution = Mathf.Min(positiveGrowth / DistributionDivisor, cashAboveReserve);
            if (distribution > 0)
            {
                business.RuntimeState.AdjustCashCents(-distribution);
                business.RuntimeState.RecordWeeklyOwnerDistribution(distribution);
                AddOwnerCashInternal(distribution);
                lastWeeklyDistributionCents = ClampToNonNegativeIntCents((long)LastWeeklyDistributionCents + distribution);
                AppendDistributionSummary($"{businessLabel} paid {FormatMoney(distribution)}; kept {FormatMoney(survivalReserve)} reserve");
                lastTransactionSummary = $"Received {FormatMoney(distribution)} owner distribution from {businessLabel}.";
                lastStatusSummary = lastTransactionSummary;
            }
            else
            {
                string reason = positiveGrowth <= 0
                    ? "growth was insufficient"
                    : $"cash above reserve was {FormatMoney(cashAboveReserve)}";
                AppendDistributionSummary($"{businessLabel} held cash: {reason} (cash {FormatMoney(currentCash)}, reserve {FormatMoney(survivalReserve)})");
            }

            checkpoint.lastCashCheckpointCents = business.RuntimeState.CurrentCashCents;
            checkpoint.lastDistributionCents = distribution;
            checkpoint.lastDistributionWeekKey = weekKey;
            PushOwnerCashToHud();
            return distribution;
        }

        public void RegisterOwnedBusinessCheckpoints(
            GeneralStoreRuntimeManager storeRuntime,
            SharedBusinessRuntimeManager sharedBusinessRuntime)
        {
            RegisterCurrentBusinessCashCheckpoint(storeRuntime != null ? storeRuntime.CurrentBusiness : null, GetCurrentWeekKey());
            if (sharedBusinessRuntime == null)
            {
                return;
            }

            IReadOnlyList<BusinessInstanceState> businesses = sharedBusinessRuntime.Businesses;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.BusinessType == BusinessType.GeneralStore)
                {
                    continue;
                }

                RegisterCurrentBusinessCashCheckpoint(business, GetCurrentWeekKey());
            }
        }

        public void RegisterCurrentBusinessCashCheckpoint(BusinessInstanceState business, int weekKey)
        {
            RegisterCurrentBusinessCashCheckpoint(business, weekKey, false);
        }

        public void RegisterCurrentBusinessCashCheckpoint(BusinessInstanceState business, int weekKey, bool startDistributionGrace)
        {
            if (!IsDistributionEligible(business))
            {
                return;
            }

            OwnerDistributionCheckpoint checkpoint = FindCheckpoint(business.InstanceId);
            int currentCash = business.RuntimeState.CurrentCashCents;
            if (checkpoint == null)
            {
                distributionCheckpoints.Add(new OwnerDistributionCheckpoint
                {
                    businessInstanceId = business.InstanceId,
                    lastCashCheckpointCents = currentCash,
                    lastDistributionCents = 0,
                    lastDistributionWeekKey = weekKey,
                    openedWeekKey = startDistributionGrace ? weekKey : -1
                });
                return;
            }

            checkpoint.lastCashCheckpointCents = currentCash;
            checkpoint.lastDistributionCents = 0;
            checkpoint.lastDistributionWeekKey = weekKey;
            if (startDistributionGrace)
            {
                checkpoint.openedWeekKey = weekKey;
            }
        }

        public void RemoveDistributionCheckpoint(string businessInstanceId)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId))
            {
                return;
            }

            for (int i = distributionCheckpoints.Count - 1; i >= 0; i--)
            {
                if (distributionCheckpoints[i] != null
                    && string.Equals(distributionCheckpoints[i].businessInstanceId, businessInstanceId, StringComparison.OrdinalIgnoreCase))
                {
                    distributionCheckpoints.RemoveAt(i);
                }
            }
        }

        public PlayerWealthSnapshot BuildWealthSnapshot()
        {
            AutoWire();

            // The portfolio manager is the authority for assembled owner wealth, but the live
            // business, asset, and debt managers remain the authority for their own domains.
            // If a domain source is not available yet during load/bootstrap, retain the last
            // saved component instead of silently wiping Net Worth down to owner cash only.
            bool hasBusinessSource = generalStoreRuntime != null || sharedBusinessRuntime != null;
            List<BusinessPortfolioSummary> businessSummaries = hasBusinessSource
                ? CalculateOwnedBusinessPortfolioSummaries(generalStoreRuntime, sharedBusinessRuntime)
                : null;
            int assetValue = acquisitionMarket != null
                ? Mathf.Max(0, acquisitionMarket.CalculateOwnedAssetValueCents())
                : Mathf.Max(0, lastAssetValueCents);
            int debt = playerDebtManager != null
                ? Mathf.Max(0, playerDebtManager.ActiveDebtPrincipalCents)
                : Mathf.Max(0, lastDebtLiabilityCents);

            PlayerWealthSnapshot snapshot = hasBusinessSource
                ? CalculateWealthSnapshot(
                    OwnerCashCents,
                    businessSummaries,
                    assetValue,
                    debt,
                    NetWorthWinTargetCents,
                    businessEquityContributionIncludesBusinessCash)
                : CalculateWealthSnapshot(
                    OwnerCashCents,
                    lastBusinessCashCents,
                    lastBusinessEquityCents,
                    lastBusinessLiabilityCents,
                    assetValue,
                    debt,
                    NetWorthWinTargetCents,
                    businessEquityContributionIncludesBusinessCash);

            lastBusinessCashCents = snapshot.BusinessCashCents;
            lastBusinessEquityCents = snapshot.BusinessEquityCents;
            lastBusinessLiabilityCents = snapshot.BusinessLiabilityCents;
            lastAssetValueCents = snapshot.AssetValueCents;
            lastDebtLiabilityCents = snapshot.DebtLiabilityCents;
            lastNetWorthCents = snapshot.NetWorthCents;
            netWorthWinReached = snapshot.WinReached;
            UpdateMonthlyWealthReviewSnapshot(snapshot);
            lastWealthComponentAuditSummary = BuildWealthComponentAuditSummary(snapshot, businessSummaries, hasBusinessSource);
            return snapshot;
        }

        public string BuildPortfolioFinanceText()
        {
            PlayerWealthSnapshot wealth = BuildWealthSnapshot();
            string goal = wealth.WinReached
                ? $"Net worth goal reached: {FormatMoney(wealth.WinTargetCents)}."
                : $"Net worth goal: {FormatMoney(wealth.WinTargetCents)}.";
            int totalLiabilities = CombineLiabilitiesCents(wealth);
            return $"Liquid Cash: {FormatMoney(wealth.LiquidCashCents)}\nNet Worth: {FormatMoney(wealth.NetWorthCents)} (business equity {FormatMoney(wealth.BusinessEquityCents)}, business cash {FormatMoney(wealth.BusinessCashCents)}, assets {FormatMoney(wealth.AssetValueCents)}, liabilities {FormatMoney(totalLiabilities)})\nBusiness cash stays inside each business until transferred or paid as owner draw.\nLast owner distributions: {FormatMoney(LastWeeklyDistributionCents)} | {LastWeeklyDistributionSummary}\n{goal}";
        }



        public string BuildWealthHeadline()
        {
            PlayerWealthSnapshot wealth = BuildWealthSnapshot();
            string goalRead = wealth.WinReached
                ? $"Goal reached: {FormatMoney(wealth.WinTargetCents)}."
                : $"Goal: {FormatMoney(wealth.WinTargetCents)}.";
            return $"Net Worth {FormatMoney(wealth.NetWorthCents)} | Liquid {FormatMoney(wealth.LiquidCashCents)} | {goalRead}";
        }

        public string BuildWealthBreakdownSummary()
        {
            PlayerWealthSnapshot wealth = BuildWealthSnapshot();
            int bankableSupport = BuildBankableSupportCents();
            int totalLiabilities = CombineLiabilitiesCents(wealth);
            return $"Liquid {FormatMoney(wealth.LiquidCashCents)} | Business Equity {FormatMoney(wealth.BusinessEquityCents)} | Business Cash {FormatMoney(wealth.BusinessCashCents)} | Assets {FormatMoney(wealth.AssetValueCents)} | Bankable Support {FormatMoney(bankableSupport)} | Liabilities {FormatMoney(totalLiabilities)}";
        }

        public string BuildWealthGoalProgressSummary()
        {
            PlayerWealthSnapshot wealth = BuildWealthSnapshot();
            if (wealth.WinTargetCents <= 0)
            {
                return "No net worth goal set.";
            }

            int remaining = PositiveDifferenceFromSignedCents(wealth.WinTargetCents, wealth.NetWorthCents);
            return wealth.WinReached
                ? $"Net worth goal reached at {FormatMoney(wealth.NetWorthCents)}."
                : $"Need {FormatMoney(remaining)} more net worth to reach {FormatMoney(wealth.WinTargetCents)}.";
        }

        public string BuildWealthSaveReadSummary()
        {
            PlayerWealthSnapshot wealth = BuildWealthSnapshot();
            int bankableSupport = BuildBankableSupportCents();
            int totalLiabilities = CombineLiabilitiesCents(wealth);
            return $"Wealth Read — Net Worth {FormatMoney(wealth.NetWorthCents)} | Liquid {FormatMoney(wealth.LiquidCashCents)} | Business Equity {FormatMoney(wealth.BusinessEquityCents)} | Business Cash {FormatMoney(wealth.BusinessCashCents)} | Bankable Support {FormatMoney(bankableSupport)} | Liabilities {FormatMoney(totalLiabilities)}";
        }

        public string BuildWealthComponentAuditText()
        {
            BuildWealthSnapshot();
            return LastWealthComponentAuditSummary;
        }

        public string BuildMonthlyWealthReviewText()
        {
            BuildWealthSnapshot();
            return LastMonthlyWealthReviewSummary;
        }

        public void ResetMonthlyWealthReviewWindow()
        {
            PlayerWealthSnapshot snapshot = BuildWealthSnapshot();
            ResetMonthlyWealthReviewFromSnapshot(snapshot);
        }

        public int BuildBankableSupportCents()
        {
            PlayerWealthSnapshot wealth = BuildWealthSnapshot();
            int netBusinessSupport = PositiveDifferenceCents(wealth.BusinessEquityCents, wealth.BusinessLiabilityCents);
            // Keep lender-facing support narrower than net worth: operating cash stays separate,
            // and existing debt pressure reduces what the bank can plausibly treat as clean support.
            return PositiveDifferenceCents(AddNonNegativeCents(wealth.AssetValueCents, netBusinessSupport), wealth.DebtLiabilityCents);
        }

        public string BuildAcquisitionFundingSummary(int askingPriceCents, int earnestCents, int financingNeedCents)
        {
            PlayerWealthSnapshot wealth = BuildWealthSnapshot();
            int remainingDue = PositiveDifferenceCents(askingPriceCents, Mathf.Max(0, earnestCents));
            int bankableSupport = BuildBankableSupportCents();
            return $"Funding Read — Liquid {FormatMoney(wealth.LiquidCashCents)} | Bankable Support {FormatMoney(bankableSupport)} | Due After Earnest {FormatMoney(remainingDue)} | Financing Need {FormatMoney(Mathf.Max(0, financingNeedCents))} | Net Worth {FormatMoney(wealth.NetWorthCents)}";
        }

        public string BuildAcquisitionOwnerReadSummary(int askingPriceCents, int earnestCents, int financingNeedCents)
        {
            PlayerWealthSnapshot wealth = BuildWealthSnapshot();
            int asking = Mathf.Max(0, askingPriceCents);
            int earnest = Mathf.Max(0, earnestCents);
            int financingNeed = Mathf.Max(0, financingNeedCents);
            int dueAfterEarnest = PositiveDifferenceCents(asking, earnest);

            if (asking <= 0)
            {
                return "Owner Read — No priced commitment is selected yet.";
            }

            if (asking <= wealth.LiquidCashCents)
            {
                return $"Owner Read — Liquid cash can carry this move cleanly. Ask {FormatMoney(asking)} | Liquid {FormatMoney(wealth.LiquidCashCents)}.";
            }

            if (dueAfterEarnest <= wealth.LiquidCashCents)
            {
                return $"Owner Read — Earnest and near-term follow-through are realistic if you stage the move carefully. Due after earnest {FormatMoney(dueAfterEarnest)}.";
            }

            if (wealth.NetWorthCents >= asking && financingNeed > 0)
            {
                return $"Owner Read — Wealth is strong enough, but this deal likely needs financing or business transfers. Financing gap {FormatMoney(financingNeed)}.";
            }

            return $"Owner Read — This is better treated as a staged lead for now. Ask {FormatMoney(asking)} | Liquid {FormatMoney(wealth.LiquidCashCents)} | Net Worth {FormatMoney(wealth.NetWorthCents)}.";
        }

        public string BuildLiquidityPressureSummary()
        {
            PlayerWealthSnapshot wealth = BuildWealthSnapshot();
            if (wealth.LiquidCashCents <= 0)
            {
                return "Liquidity exhausted. New commitments likely require transfers, sales, or debt.";
            }

            if (wealth.DebtLiabilityCents > 0 && wealth.LiquidCashCents <= wealth.DebtLiabilityCents / 4)
            {
                return $"Liquidity is tight against current debt pressure. Liquid {FormatMoney(wealth.LiquidCashCents)} | Debt {FormatMoney(wealth.DebtLiabilityCents)}.";
            }

            if (IsGreaterThanScaled(wealth.BusinessCashCents, wealth.LiquidCashCents, 2))
            {
                return $"Most ready cash is still tied up inside businesses. Liquid {FormatMoney(wealth.LiquidCashCents)} | Business Cash {FormatMoney(wealth.BusinessCashCents)}.";
            }

            return $"Liquidity is serviceable for near-term moves. Liquid {FormatMoney(wealth.LiquidCashCents)} | Weekly owner draw last week {FormatMoney(LastWeeklyDistributionCents)}.";
        }

        public string BuildLoanReadinessSummary(int requestedAmountCents, int estimatedPaymentCents)
        {
            PlayerWealthSnapshot wealth = BuildWealthSnapshot();
            int request = Mathf.Max(0, requestedAmountCents);
            int payment = Mathf.Max(0, estimatedPaymentCents);
            int financingGap = PositiveDifferenceCents(request, wealth.LiquidCashCents);
            int bankableSupport = BuildBankableSupportCents();

            string readiness;
            if (request <= wealth.LiquidCashCents)
            {
                readiness = "Liquid cash can cover this request without debt.";
            }
            else if (bankableSupport >= request)
            {
                readiness = "Collateral support looks credible, but closing still depends on repayment headroom.";
            }
            else if (wealth.NetWorthCents >= request)
            {
                readiness = "Wealth is strong enough overall, but too much of it is still tied up away from clean bankable support.";
            }
            else
            {
                readiness = "This move would lean heavily on financing and tighter follow-through.";
            }

            string paymentRead = payment > 0
                ? $"Estimated payment {FormatMoney(payment)}."
                : "Estimated payment still needs review.";

            return $"Loan Read — Request {FormatMoney(request)} | Financing Gap {FormatMoney(financingGap)} | Bankable Support {FormatMoney(bankableSupport)} | {paymentRead} {readiness}";
        }

        public string BuildStagedCommitmentSummary(int requiredCashCents, int earnestCashCents, int projectedWeeklyBurdenCents)
        {
            PlayerWealthSnapshot wealth = BuildWealthSnapshot();
            int required = Mathf.Max(0, requiredCashCents);
            int earnest = Mathf.Clamp(earnestCashCents, 0, required);
            int dueAfterEarnest = PositiveDifferenceCents(required, earnest);
            int weeklyBurden = Mathf.Max(0, projectedWeeklyBurdenCents);

            if (required <= 0)
            {
                return "Commitment Read — no priced move is selected yet.";
            }

            if (dueAfterEarnest <= wealth.LiquidCashCents && weeklyBurden <= 0)
            {
                return $"Commitment Read — staged cash coverage looks clean. Earnest {FormatMoney(earnest)} | Due after earnest {FormatMoney(dueAfterEarnest)}.";
            }

            if (dueAfterEarnest <= wealth.LiquidCashCents)
            {
                return $"Commitment Read — cash can carry the staged move, but weekly burden needs monitoring. Weekly burden {FormatMoney(weeklyBurden)}.";
            }

            if (wealth.NetWorthCents >= required)
            {
                return $"Commitment Read — overall wealth supports the move, but liquid cash is still tight after earnest. Due after earnest {FormatMoney(dueAfterEarnest)}.";
            }

            return $"Commitment Read — this likely needs financing, transfers, or more runway before committing. Earnest {FormatMoney(earnest)} | Remaining {FormatMoney(dueAfterEarnest)}.";
        }

        public string BuildFormalProcessLedgerSummary(int requiredCashCents, int earnestCashCents, int projectedWeeklyBurdenCents)
        {
            string headline = BuildWealthHeadline();
            string liquidity = BuildLiquidityPressureSummary();
            string readiness = BuildStagedCommitmentSummary(requiredCashCents, earnestCashCents, projectedWeeklyBurdenCents);
            return $"{headline} | {liquidity} | {readiness}";
        }

        public int EvaluateCommitmentRiskScore(int requiredCashCents, int earnestCashCents, int projectedWeeklyBurdenCents, int activeProcesses, int stalledProcesses)
        {
            PlayerWealthSnapshot wealth = BuildWealthSnapshot();
            int required = Mathf.Max(0, requiredCashCents);
            int earnest = Mathf.Clamp(earnestCashCents, 0, required);
            int dueAfterEarnest = PositiveDifferenceCents(required, earnest);
            int weeklyBurden = Mathf.Max(0, projectedWeeklyBurdenCents);
            int score = 0;

            if (required > wealth.LiquidCashCents)
            {
                score++;
            }

            if (dueAfterEarnest > wealth.LiquidCashCents)
            {
                score++;
            }

            int comfortBurden = Mathf.Max(5000, Mathf.Max(0, LastWeeklyDistributionCents));
            if (weeklyBurden > comfortBurden)
            {
                score++;
            }

            if (wealth.DebtLiabilityCents > 0 && wealth.LiquidCashCents <= wealth.DebtLiabilityCents / 4)
            {
                score++;
            }

            if (activeProcesses >= 3)
            {
                score++;
            }

            if (stalledProcesses > 0)
            {
                score++;
            }

            return Mathf.Clamp(score, 0, 3);
        }

        public string BuildRunwayPressureSummary(int projectedWeeklyBurdenCents, int activeProcesses, int stalledProcesses)
        {
            PlayerWealthSnapshot wealth = BuildWealthSnapshot();
            int weeklyBurden = Mathf.Max(0, projectedWeeklyBurdenCents);
            int risk = EvaluateCommitmentRiskScore(MultiplyNonNegativeCents(weeklyBurden, 8), 0, weeklyBurden, activeProcesses, stalledProcesses);

            return risk switch
            {
                0 => $"Runway Read — Owner liquidity and weekly posture look clear for near-term process follow-through. Weekly burden {FormatMoney(weeklyBurden)}.",
                1 => $"Runway Read — The move looks manageable, but keep weekly burden and active process count in view. Active {activeProcesses} | Weekly burden {FormatMoney(weeklyBurden)}.",
                2 => $"Runway Read — Owner runway is getting tight once weekly burden, debt, and process load are combined. Liquid {FormatMoney(wealth.LiquidCashCents)} | Weekly burden {FormatMoney(weeklyBurden)}.",
                _ => $"Runway Read — This process posture is strained and likely needs staging, retained earnings, or financing support. Stalled {stalledProcesses} | Weekly burden {FormatMoney(weeklyBurden)}."
            };
        }

        public int EvaluateExecutionLoadScore(int activeProcesses, int stalledProcesses)
        {
            int active = Mathf.Max(0, activeProcesses);
            int stalled = Mathf.Max(0, stalledProcesses);
            int score = 0;

            if (active >= 2)
            {
                score++;
            }

            if (active >= 4)
            {
                score++;
            }

            if (stalled >= 1)
            {
                score++;
            }

            if (stalled >= 3)
            {
                score++;
            }

            return Mathf.Clamp(score, 0, 3);
        }

        public string BuildExecutionLoadSummary(int activeProcesses, int stalledProcesses)
        {
            int active = Mathf.Max(0, activeProcesses);
            int stalled = Mathf.Max(0, stalledProcesses);
            int score = EvaluateExecutionLoadScore(active, stalled);

            return score switch
            {
                0 => $"Execution Load — Process count looks controlled. Active {active} | Stalled {stalled}.",
                1 => $"Execution Load — The ownership slate is getting busier, but still looks manageable. Active {active} | Stalled {stalled}.",
                2 => $"Execution Load — Process load is pressuring follow-through. Active {active} | Stalled {stalled}.",
                _ => $"Execution Load — Too many live or stalled processes are competing for follow-through. Active {active} | Stalled {stalled}."
            };
        }

        public string BuildOwnerCommitmentClimateSummary(int requiredCashCents, int earnestCashCents, int projectedWeeklyBurdenCents, int activeProcesses, int stalledProcesses)
        {
            string ownerRead = BuildAcquisitionOwnerReadSummary(requiredCashCents, earnestCashCents, PositiveDifferenceCents(requiredCashCents, earnestCashCents));
            string staged = BuildStagedCommitmentSummary(requiredCashCents, earnestCashCents, projectedWeeklyBurdenCents);
            string runway = BuildRunwayPressureSummary(projectedWeeklyBurdenCents, activeProcesses, stalledProcesses);
            return $"{ownerRead} | {staged} | {runway}";
        }

        public string BuildOwnerExecutionClimateSummary(int requiredCashCents, int earnestCashCents, int projectedWeeklyBurdenCents, int activeProcesses, int stalledProcesses)
        {
            string commitmentClimate = BuildOwnerCommitmentClimateSummary(requiredCashCents, earnestCashCents, projectedWeeklyBurdenCents, activeProcesses, stalledProcesses);
            string executionLoad = BuildExecutionLoadSummary(activeProcesses, stalledProcesses);
            return $"{commitmentClimate} | {executionLoad}";
        }

        public string BuildCommitmentReadinessSummary(int requiredCashCents, int projectedWeeklyBurdenCents)
        {
            PlayerWealthSnapshot wealth = BuildWealthSnapshot();
            int required = Mathf.Max(0, requiredCashCents);
            int weeklyBurden = Mathf.Max(0, projectedWeeklyBurdenCents);

            if (required <= wealth.LiquidCashCents && weeklyBurden <= Mathf.Max(0, LastWeeklyDistributionCents))
            {
                return $"Commitment Read — Liquid cash can cover {FormatMoney(required)} and recent owner cashflow can likely absorb about {FormatMoney(weeklyBurden)} weekly.";
            }

            if (required <= wealth.NetWorthCents)
            {
                return $"Commitment Read — Wealth is strong enough for {FormatMoney(required)}, but liquid cashflow may need transfers, retained earnings, or financing support.";
            }

            return $"Commitment Read — This move likely needs tighter staging, asset sales, or financing. Required {FormatMoney(required)} | Weekly burden {FormatMoney(weeklyBurden)}.";
        }

        public void PushOwnerCashToHud()
        {
            if (hudController != null)
            {
                PlayerWealthSnapshot wealth = BuildWealthSnapshot();
                hudController.SetWealthCents(wealth.NetWorthCents, wealth.LiquidCashCents, LastWealthComponentAuditSummary);
            }
        }

        private void Awake()
        {
            AutoWire();
            PushOwnerCashToHud();
        }

        private void OnValidate()
        {
            Sanitize();
        }

        private void AutoWire()
        {
            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            hudController ??= FindAnyObjectByType<LandLedgersHUDController>();
            generalStoreRuntime ??= FindAnyObjectByType<GeneralStoreRuntimeManager>();
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            acquisitionMarket ??= FindAnyObjectByType<AcquisitionMarketManager>();
            playerDebtManager ??= FindAnyObjectByType<PlayerDebtManager>();
        }

        private void BeginDistributionWeekIfNeeded(int weekKey)
        {
            if (lastWeeklyDistributionWeekKey == weekKey)
            {
                return;
            }

            lastWeeklyDistributionWeekKey = weekKey;
            lastWeeklyDistributionCents = 0;
            lastWeeklyDistributionSummary = "No owner distributions this week.";
        }

        private OwnerDistributionCheckpoint FindCheckpoint(string businessInstanceId)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId))
            {
                return null;
            }

            for (int i = 0; i < distributionCheckpoints.Count; i++)
            {
                OwnerDistributionCheckpoint checkpoint = distributionCheckpoints[i];
                if (checkpoint != null
                    && string.Equals(checkpoint.businessInstanceId, businessInstanceId, StringComparison.OrdinalIgnoreCase))
                {
                    return checkpoint;
                }
            }

            return null;
        }

        public static int CalculateSurvivalReserveCents(
            int protectedReserveCents,
            int filledWeeklyPayrollCents,
            int weeklyOperatingReserveCents)
        {
            return CalculateSurvivalReserveCents(
                protectedReserveCents,
                filledWeeklyPayrollCents,
                weeklyOperatingReserveCents,
                0);
        }

        public static int CalculateSurvivalReserveCents(
            int protectedReserveCents,
            int filledWeeklyPayrollCents,
            int weeklyOperatingReserveCents,
            int localIntakeAllowanceCents)
        {
            long reserve = (long)Mathf.Max(0, protectedReserveCents)
                + (long)Mathf.Max(0, filledWeeklyPayrollCents) * 2L
                + Mathf.Max(0, weeklyOperatingReserveCents)
                + Mathf.Max(0, localIntakeAllowanceCents);
            return ClampToNonNegativeIntCents(reserve);
        }

        public static int CalculateOwnedBusinessCashCents(
            GeneralStoreRuntimeManager storeRuntime,
            SharedBusinessRuntimeManager sharedRuntime)
        {
            IReadOnlyList<BusinessPortfolioSummary> summaries = CalculateOwnedBusinessPortfolioSummaries(storeRuntime, sharedRuntime);
            long total = 0;
            for (int i = 0; i < summaries.Count; i++)
            {
                total += Mathf.Max(0, summaries[i].BusinessCashCents);
            }

            return ClampToNonNegativeIntCents(total);
        }

        public static PlayerWealthSnapshot CalculateWealthSnapshot(
            int ownerCashCents,
            IReadOnlyList<BusinessPortfolioSummary> businessSummaries,
            int assetValueCents,
            int debtLiabilityCents,
            int winTargetCents)
        {
            return CalculateWealthSnapshot(
                ownerCashCents,
                businessSummaries,
                assetValueCents,
                debtLiabilityCents,
                winTargetCents,
                false);
        }

        public static PlayerWealthSnapshot CalculateWealthSnapshot(
            int ownerCashCents,
            IReadOnlyList<BusinessPortfolioSummary> businessSummaries,
            int assetValueCents,
            int debtLiabilityCents,
            int winTargetCents,
            bool businessEquityIncludesBusinessCash)
        {
            long businessCash = 0;
            long businessEquity = 0;
            long businessLiability = 0;
            if (businessSummaries != null)
            {
                for (int i = 0; i < businessSummaries.Count; i++)
                {
                    BusinessPortfolioSummary summary = businessSummaries[i];
                    int cash = Mathf.Max(0, summary.BusinessCashCents);
                    businessCash += cash;
                    businessEquity += CalculateBusinessEquityContributionCents(
                        summary.EquityContributionCents,
                        cash,
                        businessEquityIncludesBusinessCash);
                    businessLiability += Mathf.Max(0, summary.BusinessLiabilityCents);
                }
            }

            return CalculateWealthSnapshot(
                ownerCashCents,
                ClampToIntCents(businessCash),
                ClampToIntCents(businessEquity),
                ClampToIntCents(businessLiability),
                assetValueCents,
                debtLiabilityCents,
                winTargetCents);
        }

        public static PlayerWealthSnapshot CalculateWealthSnapshot(
            int ownerCashCents,
            int businessCashCents,
            int businessEquityCents,
            int businessLiabilityCents,
            int assetValueCents,
            int debtLiabilityCents,
            int winTargetCents)
        {
            return CalculateWealthSnapshot(
                ownerCashCents,
                businessCashCents,
                businessEquityCents,
                businessLiabilityCents,
                assetValueCents,
                debtLiabilityCents,
                winTargetCents,
                false);
        }

        public static PlayerWealthSnapshot CalculateWealthSnapshot(
            int ownerCashCents,
            int businessCashCents,
            int businessEquityCents,
            int businessLiabilityCents,
            int assetValueCents,
            int debtLiabilityCents,
            int winTargetCents,
            bool businessEquityIncludesBusinessCash)
        {
            int liquid = Mathf.Max(0, ownerCashCents);
            int businessCash = Mathf.Max(0, businessCashCents);
            int businessEquity = CalculateBusinessEquityContributionCents(
                businessEquityCents,
                businessCash,
                businessEquityIncludesBusinessCash);
            int businessLiability = Mathf.Max(0, businessLiabilityCents);
            int assets = Mathf.Max(0, assetValueCents);
            int debt = Mathf.Max(0, debtLiabilityCents);
            int target = Mathf.Max(0, winTargetCents);
            long netWorthLong = (long)liquid + businessCash + businessEquity + assets - businessLiability - debt;
            int netWorth = ClampToIntCents(netWorthLong);
            return new PlayerWealthSnapshot(
                liquid,
                businessCash,
                businessEquity,
                businessLiability,
                assets,
                debt,
                netWorth,
                target,
                target > 0 && netWorth >= target);
        }

        public static List<BusinessPortfolioSummary> CalculateOwnedBusinessPortfolioSummaries(
            GeneralStoreRuntimeManager storeRuntime,
            SharedBusinessRuntimeManager sharedRuntime)
        {
            List<BusinessPortfolioSummary> summaries = new();
            if (storeRuntime != null
                && storeRuntime.CurrentBusiness != null
                && storeRuntime.CurrentBusiness.Owner != null
                && storeRuntime.CurrentBusiness.Owner.OwnerKind == BusinessOwnerKind.Player
                && storeRuntime.RuntimeState != null)
            {
                summaries.Add(storeRuntime.CurrentBusiness.BuildPortfolioSummary(
                    storeRuntime.OperatingBusinessCashReserveCents,
                    storeRuntime.ProtectedBusinessCashReserveCents));
            }

            IReadOnlyList<BusinessInstanceState> businesses = sharedRuntime != null ? sharedRuntime.Businesses : null;
            if (businesses != null)
            {
                for (int i = 0; i < businesses.Count; i++)
                {
                    BusinessInstanceState business = businesses[i];
                    if (business == null
                        || business.BusinessType == BusinessType.GeneralStore
                        || business.Owner == null
                        || business.Owner.OwnerKind != BusinessOwnerKind.Player
                        || business.RuntimeState == null)
                    {
                        continue;
                    }

                    summaries.Add(business.BuildPortfolioSummary(
                        SharedBusinessRuntimeManager.CalculateSharedOperatingCashReserveCents(business),
                        SharedBusinessRuntimeManager.CalculateSharedSurvivalCashReserveCents(business)));
                }
            }

            return summaries;
        }

        private static bool IsWithinDistributionGrace(OwnerDistributionCheckpoint checkpoint, int weekKey)
        {
            return checkpoint != null
                && checkpoint.openedWeekKey >= 0
                && weekKey >= 0
                && weekKey - checkpoint.openedWeekKey < DistributionGraceWeeks;
        }

        private void UpdateMonthlyWealthReviewSnapshot(PlayerWealthSnapshot snapshot)
        {
            int reviewKey = GetCurrentMonthlyReviewKey();
            if (lastMonthlyWealthReviewKey != reviewKey || string.IsNullOrWhiteSpace(lastMonthlyWealthReviewSummary) || lastMonthlyWealthReviewSummary == DefaultMonthlyWealthReviewSummary)
            {
                lastMonthlyWealthReviewKey = reviewKey;
                monthlyOpeningNetWorthCents = snapshot.NetWorthCents;
                monthlyOpeningDebtCents = snapshot.DebtLiabilityCents;
                monthlyOpeningBusinessCashCents = snapshot.BusinessCashCents;
            }

            monthlyClosingNetWorthCents = snapshot.NetWorthCents;
            monthlyClosingDebtCents = snapshot.DebtLiabilityCents;
            monthlyClosingBusinessCashCents = snapshot.BusinessCashCents;
            lastMonthlyWealthReviewSummary = BuildMonthlyWealthReviewSummary();
        }

        private void ResetMonthlyWealthReviewFromCurrentComponents()
        {
            lastMonthlyWealthReviewKey = GetCurrentMonthlyReviewKey();
            monthlyOpeningNetWorthCents = lastNetWorthCents;
            monthlyClosingNetWorthCents = lastNetWorthCents;
            monthlyOpeningDebtCents = Mathf.Max(0, lastDebtLiabilityCents);
            monthlyClosingDebtCents = Mathf.Max(0, lastDebtLiabilityCents);
            monthlyOpeningBusinessCashCents = Mathf.Max(0, lastBusinessCashCents);
            monthlyClosingBusinessCashCents = Mathf.Max(0, lastBusinessCashCents);
            lastMonthlyWealthReviewSummary = BuildMonthlyWealthReviewSummary();
        }

        private void ResetMonthlyWealthReviewFromSnapshot(PlayerWealthSnapshot snapshot)
        {
            lastMonthlyWealthReviewKey = GetCurrentMonthlyReviewKey();
            monthlyOpeningNetWorthCents = snapshot.NetWorthCents;
            monthlyClosingNetWorthCents = snapshot.NetWorthCents;
            monthlyOpeningDebtCents = snapshot.DebtLiabilityCents;
            monthlyClosingDebtCents = snapshot.DebtLiabilityCents;
            monthlyOpeningBusinessCashCents = snapshot.BusinessCashCents;
            monthlyClosingBusinessCashCents = snapshot.BusinessCashCents;
            lastMonthlyWealthReviewSummary = BuildMonthlyWealthReviewSummary();
        }

        private string BuildMonthlyWealthReviewSummary()
        {
            string windowLabel = BuildMonthlyWealthReviewWindowLabel();
            if (lastMonthlyWealthReviewKey < 0)
            {
                return $"Monthly Wealth Review — {windowLabel}; current drift window uses the loaded portfolio components. Net Worth {FormatMoney(monthlyClosingNetWorthCents)} | Debt {FormatMoney(monthlyClosingDebtCents)} | Business Cash {FormatMoney(monthlyClosingBusinessCashCents)}.";
            }

            return $"Monthly Wealth Review — {windowLabel}: net worth drift {FormatMoney(MonthlyNetWorthDriftCents)} | debt drift {FormatMoney(MonthlyDebtDriftCents)} | business cash drift {FormatMoney(MonthlyBusinessCashDriftCents)} | opening net worth {FormatMoney(monthlyOpeningNetWorthCents)} | current net worth {FormatMoney(monthlyClosingNetWorthCents)}.";
        }

        private string BuildMonthlyWealthReviewWindowLabel()
        {
            // Until a calendar-month key exists, portfolio cadence uses four-week buckets from TimeManager.CurrentWeek.
            return lastMonthlyWealthReviewKey < 0
                ? "time manager unavailable"
                : $"four-week review window {lastMonthlyWealthReviewKey}";
        }

        private string BuildWealthComponentAuditSummary(PlayerWealthSnapshot snapshot, IReadOnlyList<BusinessPortfolioSummary> businessSummaries, bool hasLiveBusinessSource)
        {
            if (!hasLiveBusinessSource)
            {
                return "Wealth Audit — live business source unavailable; preserving last saved business cash/equity/liability components until runtime managers are wired.";
            }

            int count = businessSummaries != null ? businessSummaries.Count : 0;
            if (count <= 0)
            {
                return "Wealth Audit — no player-owned business summaries contributed to Net Worth.";
            }

            string policy = businessEquityContributionIncludesBusinessCash
                ? "cash-inclusive equity policy; business cash is subtracted from equity before Net Worth"
                : "cash-separate equity policy; business equity is treated as separate from business cash";
            return $"Wealth Audit — {count} business summaries | {policy}. Components: Liquid {FormatMoney(snapshot.LiquidCashCents)}, Business Cash {FormatMoney(snapshot.BusinessCashCents)}, Business Equity {FormatMoney(snapshot.BusinessEquityCents)}, Liabilities {FormatMoney(CombineLiabilitiesCents(snapshot))}.";
        }

        private void CompleteBusinessCashTransfer(BusinessInstanceState business, string message, int weekKey)
        {
            initialized = true;
            business.CashTransferRule.RecordTransferSummary(message, weekKey);
            lastTransactionSummary = message;
            lastStatusSummary = message;
            RegisterCurrentBusinessCashCheckpoint(business, weekKey);
            PushOwnerCashToHud();
        }

        private static bool IsBusinessCashTransferEligible(BusinessInstanceState business, out string message)
        {
            if (business == null || business.RuntimeState == null)
            {
                message = "Blocked: business cash unavailable.";
                return false;
            }

            if (business.Owner == null || business.Owner.OwnerKind != BusinessOwnerKind.Player)
            {
                message = "Blocked: not player-owned.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static bool IsDistributionEligible(BusinessInstanceState business)
        {
            return business != null
                && business.RuntimeState != null
                && !string.IsNullOrWhiteSpace(business.InstanceId)
                && business.Owner != null
                && business.Owner.OwnerKind == BusinessOwnerKind.Player;
        }

        private void AppendDistributionSummary(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(lastWeeklyDistributionSummary)
                || string.Equals(lastWeeklyDistributionSummary, "No owner distributions this week.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(lastWeeklyDistributionSummary, "No owner distributions resolved yet.", StringComparison.OrdinalIgnoreCase))
            {
                lastWeeklyDistributionSummary = segment.Trim();
                return;
            }

            lastWeeklyDistributionSummary += "; " + segment.Trim();
        }

        private int GetCurrentWeekKey()
        {
            return timeManager != null ? Mathf.Max(0, timeManager.CurrentWeek) : -1;
        }

        private int GetCurrentMonthlyReviewKey()
        {
            int weekKey = GetCurrentWeekKey();
            return weekKey < 0 ? -1 : weekKey / 4;
        }

        private void Sanitize()
        {
            ownerCashCents = Mathf.Max(0, ownerCashCents);
            lastStatusSummary ??= string.Empty;
            lastTransactionSummary ??= string.Empty;
            lastWealthComponentAuditSummary = string.IsNullOrWhiteSpace(lastWealthComponentAuditSummary)
                ? DefaultWealthComponentAuditSummary
                : lastWealthComponentAuditSummary.Trim();
            monthlyOpeningDebtCents = Mathf.Max(0, monthlyOpeningDebtCents);
            monthlyClosingDebtCents = Mathf.Max(0, monthlyClosingDebtCents);
            monthlyOpeningBusinessCashCents = Mathf.Max(0, monthlyOpeningBusinessCashCents);
            monthlyClosingBusinessCashCents = Mathf.Max(0, monthlyClosingBusinessCashCents);
            lastMonthlyWealthReviewSummary = string.IsNullOrWhiteSpace(lastMonthlyWealthReviewSummary)
                ? DefaultMonthlyWealthReviewSummary
                : lastMonthlyWealthReviewSummary.Trim();
            lastWeeklyDistributionCents = Mathf.Max(0, lastWeeklyDistributionCents);
            lastWeeklyDistributionSummary ??= string.Empty;
            netWorthWinTargetCents = netWorthWinTargetCents <= 0 ? DefaultNetWorthWinTargetCents : netWorthWinTargetCents;
            // Net worth may be negative when liabilities exceed liquid cash, business value, and assets.
            // Preserve that truth instead of clamping it into a misleading zero balance.
            lastBusinessCashCents = Mathf.Max(0, lastBusinessCashCents);
            lastBusinessEquityCents = Mathf.Max(0, lastBusinessEquityCents);
            lastBusinessLiabilityCents = Mathf.Max(0, lastBusinessLiabilityCents);
            lastAssetValueCents = Mathf.Max(0, lastAssetValueCents);
            lastDebtLiabilityCents = Mathf.Max(0, lastDebtLiabilityCents);
            distributionCheckpoints ??= new List<OwnerDistributionCheckpoint>();
            for (int i = distributionCheckpoints.Count - 1; i >= 0; i--)
            {
                OwnerDistributionCheckpoint checkpoint = distributionCheckpoints[i];
                if (checkpoint == null || string.IsNullOrWhiteSpace(checkpoint.businessInstanceId))
                {
                    distributionCheckpoints.RemoveAt(i);
                    continue;
                }

                checkpoint.lastCashCheckpointCents = Mathf.Max(0, checkpoint.lastCashCheckpointCents);
                checkpoint.lastDistributionCents = Mathf.Max(0, checkpoint.lastDistributionCents);
                checkpoint.openedWeekKey = checkpoint.openedWeekKey < 0 ? -1 : checkpoint.openedWeekKey;
            }
        }

        private static string ResolveBusinessLabel(BusinessInstanceState business, string fallback)
        {
            if (business != null && !string.IsNullOrWhiteSpace(business.RuntimeDisplayName))
            {
                return business.RuntimeDisplayName;
            }

            return SanitizeReason(fallback);
        }

        private static string SanitizeReason(string reason)
        {
            return string.IsNullOrWhiteSpace(reason) ? "portfolio transaction" : reason.Trim();
        }

        private void AddOwnerCashInternal(int cents)
        {
            int amount = Mathf.Max(0, cents);
            if (amount <= 0)
            {
                return;
            }

            ownerCashCents = ClampToNonNegativeIntCents((long)OwnerCashCents + amount);
        }

        private bool TrySpendOwnerCashInternal(int cents)
        {
            int amount = Mathf.Max(0, cents);
            if (amount <= 0)
            {
                return true;
            }

            if (OwnerCashCents < amount)
            {
                return false;
            }

            ownerCashCents = ClampToNonNegativeIntCents((long)OwnerCashCents - amount);
            return true;
        }

        private static int SafeAbsCents(int cents)
        {
            return cents == int.MinValue ? int.MaxValue : Mathf.Abs(cents);
        }

        private static int CalculateBusinessEquityContributionCents(int equityContributionCents, int businessCashCents, bool equityIncludesBusinessCash)
        {
            int equity = Mathf.Max(0, equityContributionCents);
            if (!equityIncludesBusinessCash)
            {
                return equity;
            }

            return PositiveDifferenceCents(equity, businessCashCents);
        }

        private static int CombineLiabilitiesCents(PlayerWealthSnapshot wealth)
        {
            return AddNonNegativeCents(wealth.BusinessLiabilityCents, wealth.DebtLiabilityCents);
        }

        private static int AddNonNegativeCents(int firstCents, int secondCents)
        {
            return ClampToNonNegativeIntCents((long)Mathf.Max(0, firstCents) + Mathf.Max(0, secondCents));
        }

        private static int PositiveDifferenceCents(int leftCents, int rightCents)
        {
            long difference = (long)Mathf.Max(0, leftCents) - Mathf.Max(0, rightCents);
            return ClampToNonNegativeIntCents(difference);
        }

        private static int PositiveDifferenceFromSignedCents(int leftCents, int rightCents)
        {
            long difference = (long)Mathf.Max(0, leftCents) - rightCents;
            return ClampToNonNegativeIntCents(difference);
        }

        private static int MultiplyNonNegativeCents(int cents, int multiplier)
        {
            return ClampToNonNegativeIntCents((long)Mathf.Max(0, cents) * Mathf.Max(0, multiplier));
        }

        private static bool IsGreaterThanScaled(int leftCents, int rightCents, int multiplier)
        {
            long scaledRight = (long)Mathf.Max(0, rightCents) * Mathf.Max(0, multiplier);
            return Mathf.Max(0, leftCents) > scaledRight;
        }

        private static int SubtractCents(int closingCents, int openingCents)
        {
            return ClampToIntCents((long)closingCents - openingCents);
        }

        private static int ClampToNonNegativeIntCents(long cents)
        {
            if (cents <= 0)
            {
                return 0;
            }

            if (cents > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)cents;
        }

        private static int ClampToIntCents(long cents)
        {
            if (cents > int.MaxValue)
            {
                return int.MaxValue;
            }

            if (cents < int.MinValue)
            {
                return int.MinValue;
            }

            return (int)cents;
        }

        private static string FormatMoney(int cents)
        {
            long amount = cents;
            string sign = amount < 0 ? "-" : string.Empty;
            decimal dollars = Math.Abs(amount) / 100m;
            return $"{sign}${dollars:0.00}";
        }
    }
}
