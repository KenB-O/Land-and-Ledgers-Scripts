using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Economy.Financing;
using LandLedgers.MVP;
using UnityEngine;

namespace LandLedgers.Economy
{
    public enum ExpansionReadinessKind
    {
        AcquisitionLand = 0,
        AcquisitionBusiness = 1,
        OwnedLand = 2,
        VacantShell = 3
    }

    [Serializable]
    public sealed class ExpansionReadinessInputLine
    {
        public ConstructionResourceKind resourceKind;
        public string displayName = string.Empty;
        public int requiredUnits;
        public int availableUnits;
        public string sourceLabel = string.Empty;

        public int MissingUnits => Mathf.Max(0, requiredUnits - availableUnits);
        public bool Ready => MissingUnits <= 0;
    }

    [Serializable]
    public sealed class ExpansionReadinessResult
    {
        public ExpansionReadinessKind Kind;
        public BusinessType SelectedBusinessType;
        public string SelectedBusinessName = string.Empty;
        public string SelectedShellName = string.Empty;
        public int OwnerCashCents;
        public int SafeWithdrawableSurplusCents;
        public int CashRequiredNowCents;
        public int EstimatedTotalPathCostCents;
        public int FinancingGapCents;
        public bool CanUseLoan;
        public int EstimatedLoanPaymentCents;
        public int ActiveDebtPaymentCents;
        public List<ExpansionReadinessInputLine> RequiredInputs = new();
        public List<ExpansionReadinessInputLine> AvailableInputs = new();
        public List<string> HardBlockers = new();
        public List<string> Warnings = new();
        public string RecommendedAction = string.Empty;

        public bool HasHardBlockers => HardBlockers != null && HardBlockers.Count > 0;

        public string BuildSummaryText()
        {
            StringBuilder builder = new();
            builder.AppendLine("Expansion Readiness");
            builder.AppendLine($"Path: {BuildPathLabel()}");
            builder.AppendLine($"Owner Cash {FormatMoney(OwnerCashCents)} | Safe surplus {FormatMoney(SafeWithdrawableSurplusCents)} | Need now {FormatMoney(CashRequiredNowCents)} | Full path {FormatMoney(EstimatedTotalPathCostCents)}");

            string funding = FinancingGapCents <= 0
                ? "Funding: ready from Owner Cash/safe surplus"
                : CanUseLoan
                    ? $"Funding: gap {FormatMoney(FinancingGapCents)}; financing likely available"
                    : $"Funding: gap {FormatMoney(FinancingGapCents)}";
            if (EstimatedLoanPaymentCents > 0)
            {
                funding += $" | Est payment {FormatMoney(EstimatedLoanPaymentCents)}/week";
            }

            if (ActiveDebtPaymentCents > 0)
            {
                funding += $" | Existing debt {FormatMoney(ActiveDebtPaymentCents)}/week";
            }

            builder.AppendLine(funding);

            string inputLine = BuildInputLine();
            if (!string.IsNullOrWhiteSpace(inputLine))
            {
                builder.AppendLine(inputLine);
            }

            if (Warnings != null && Warnings.Count > 0)
            {
                builder.AppendLine("Warnings: " + string.Join("; ", Warnings));
            }

            if (HardBlockers != null && HardBlockers.Count > 0)
            {
                builder.AppendLine("Blocked: " + string.Join("; ", HardBlockers));
            }

            builder.AppendLine($"Next: {RecommendedAction}");
            return builder.ToString();
        }

        private string BuildPathLabel()
        {
            string business = string.IsNullOrWhiteSpace(SelectedBusinessName)
                ? SelectedBusinessType.ToString()
                : SelectedBusinessName;
            return Kind switch
            {
                ExpansionReadinessKind.AcquisitionLand => "buy land, then improve it into a holding",
                ExpansionReadinessKind.AcquisitionBusiness => "buy an operating business",
                ExpansionReadinessKind.OwnedLand => string.IsNullOrWhiteSpace(SelectedShellName)
                    ? $"build a {business} shell"
                    : $"build {SelectedShellName} for {business}",
                ExpansionReadinessKind.VacantShell => $"start {business} in this shell",
                _ => "expand ownership"
            };
        }

        private string BuildInputLine()
        {
            if (RequiredInputs == null || RequiredInputs.Count == 0)
            {
                return string.Empty;
            }

            List<string> inputs = new();
            for (int i = 0; i < RequiredInputs.Count; i++)
            {
                ExpansionReadinessInputLine line = RequiredInputs[i];
                if (line == null || line.requiredUnits <= 0)
                {
                    continue;
                }

                string label = string.IsNullOrWhiteSpace(line.displayName)
                    ? line.resourceKind.ToString()
                    : line.displayName;
                inputs.Add($"{label} {line.availableUnits}/{line.requiredUnits}");
            }

            return inputs.Count > 0 ? "Inputs: " + string.Join(" | ", inputs) : string.Empty;
        }

        private static string FormatMoney(int cents)
        {
            return "$" + (Mathf.Max(0, cents) / 100f).ToString("N2");
        }
    }

    public sealed class ExpansionReadinessService
    {
        private readonly AcquisitionMarketManager acquisitionMarket;
        private readonly PlayerPortfolioManager playerPortfolio;
        private readonly PlayerDebtManager playerDebtManager;
        private readonly GeneralStoreRuntimeManager storeRuntime;
        private readonly SharedBusinessRuntimeManager sharedBusinessRuntime;

        public ExpansionReadinessService(
            AcquisitionMarketManager acquisitionMarket,
            PlayerPortfolioManager playerPortfolio,
            PlayerDebtManager playerDebtManager,
            GeneralStoreRuntimeManager storeRuntime,
            SharedBusinessRuntimeManager sharedBusinessRuntime)
        {
            this.acquisitionMarket = acquisitionMarket;
            this.playerPortfolio = playerPortfolio;
            this.playerDebtManager = playerDebtManager;
            this.storeRuntime = storeRuntime;
            this.sharedBusinessRuntime = sharedBusinessRuntime;
        }

        public ExpansionReadinessResult BuildForSelectedAcquisition(AcquisitionMarketSection section)
        {
            if (acquisitionMarket == null
                || !acquisitionMarket.TryGetSelectedListing(section, out AcquisitionListing listing)
                || listing == null)
            {
                return BuildUnavailable(
                    section == AcquisitionMarketSection.Businesses
                        ? ExpansionReadinessKind.AcquisitionBusiness
                        : ExpansionReadinessKind.AcquisitionLand,
                    "Select an acquisition listing.");
            }

            ExpansionReadinessResult result = CreateBaseResult(listing.kind == AcquisitionListingKind.Business
                ? ExpansionReadinessKind.AcquisitionBusiness
                : ExpansionReadinessKind.AcquisitionLand);
            result.SelectedBusinessType = listing.businessType;
            result.SelectedBusinessName = !string.IsNullOrWhiteSpace(listing.businessTypeDisplayName)
                ? listing.businessTypeDisplayName
                : listing.businessDisplayName;

            AcquisitionDealState deal = FindDeal(listing.listingId);
            int totalPrice = Mathf.Max(1, deal != null && deal.tentativePriceCents > 0 ? deal.tentativePriceCents : listing.askingPriceCents);
            result.EstimatedTotalPathCostCents = totalPrice;

            switch (deal != null ? deal.stage : AcquisitionDealStage.None)
            {
                case AcquisitionDealStage.None:
                    result.CashRequiredNowCents = 0;
                    result.RecommendedAction = listing.aiEligible
                        ? "Can inquire now, but rival access is live."
                        : "Can inquire now while player-first access still holds.";
                    break;
                case AcquisitionDealStage.Failed:
                    result.CashRequiredNowCents = 0;
                    result.RecommendedAction = BuildFailedAcquisitionAction(deal);
                    if (!string.IsNullOrWhiteSpace(deal != null ? deal.closingFailureSummary : string.Empty))
                    {
                        result.Warnings.Add($"Latest failed file: {deal.closingFailureSummary.Trim()}");
                    }
                    break;
                case AcquisitionDealStage.Inquiry:
                    if (NeedsProofOfFunds(deal))
                    {
                        result.CashRequiredNowCents = 0;
                        result.RecommendedAction = "Present proof of funds before earnest; inquiry alone does not protect the lane.";
                    }
                    else
                    {
                        result.CashRequiredNowCents = Mathf.Clamp(deal.earnestMoneyCents, 0, totalPrice);
                        result.RecommendedAction = result.CashRequiredNowCents <= AvailableExpansionCashCents(result)
                            ? "Can commit earnest now to protect the lane."
                            : $"Need {FormatMoney(result.CashRequiredNowCents - AvailableExpansionCashCents(result))} more to protect the lane with earnest.";
                    }
                    break;
                case AcquisitionDealStage.EarnestCommitted:
                    result.CashRequiredNowCents = 0;
                    result.RecommendedAction = IsFormalDiligenceComplete(deal)
                        ? BuildOptionAwareTermsAction(deal)
                        : BuildOptionAwareDiligenceAction(deal);
                    break;
                case AcquisitionDealStage.DiligenceComplete:
                    result.CashRequiredNowCents = 0;
                    result.RecommendedAction = BuildOptionAwareTermsAction(deal);
                    break;
                case AcquisitionDealStage.TentativeAgreement:
                    result.CashRequiredNowCents = Mathf.Max(0, totalPrice - Mathf.Max(0, deal.earnestMoneyCents));
                    PreviewAcquisitionFinancing(result, listing, deal, totalPrice);
                    result.RecommendedAction = BuildCashAction(result, BuildClosingReadyAction(deal), BuildClosingBlockedAction(deal));
                    break;
                case AcquisitionDealStage.Closed:
                    result.CashRequiredNowCents = 0;
                    result.RecommendedAction = "Already closed.";
                    break;
                default:
                    result.CashRequiredNowCents = 0;
                    result.RecommendedAction = "Advance the selected deal.";
                    break;
            }

            result.FinancingGapCents = Mathf.Max(0, result.CashRequiredNowCents - AvailableExpansionCashCents(result));
            AddAcquisitionProcessWarnings(result, listing, deal);
            if (listing.kind == AcquisitionListingKind.Land)
            {
                AddLandAcquisitionWarnings(result, listing);
            }

            if (result.FinancingGapCents > 0 && result.CanUseLoan)
            {
                result.RecommendedAction = BuildLoanBridgeAcquisitionAction(deal);
            }
            else if (result.FinancingGapCents > 0 && result.RecommendedAction.StartsWith("Can ", StringComparison.OrdinalIgnoreCase))
            {
                result.RecommendedAction = $"Need {FormatMoney(result.FinancingGapCents)} more Owner Cash or safe surplus.";
            }

            return result;
        }

        public ExpansionReadinessResult BuildForOwnedLand(int plotId, int businessOptionIndex, int shellOptionIndex)
        {
            ExpansionReadinessResult result = CreateBaseResult(ExpansionReadinessKind.OwnedLand);
            if (acquisitionMarket == null)
            {
                result.HardBlockers.Add("Acquisitions unavailable.");
                result.RecommendedAction = "Reconnect the acquisition market.";
                return result;
            }

            if (!acquisitionMarket.TryGetBusinessDevelopmentCandidate(businessOptionIndex, out BusinessActivationCandidateState candidate)
                || candidate == null)
            {
                result.HardBlockers.Add("No business type is selected.");
                result.RecommendedAction = "Choose a business type.";
                return result;
            }

            result.SelectedBusinessType = candidate.businessType;
            result.SelectedBusinessName = candidate.displayName;
            if (!candidate.eligible)
            {
                result.HardBlockers.Add(candidate.reason);
                result.RecommendedAction = "Choose another business type.";
                return result;
            }

            IReadOnlyList<LandLedgers.World.BuildingDefinition> shellOptions = acquisitionMarket.GetBusinessBuildOptionsForOwnedPlot(plotId, businessOptionIndex);
            if (shellOptions == null || shellOptions.Count <= 0)
            {
                result.HardBlockers.Add("No shell plan physically fits this plot.");
                result.RecommendedAction = "Choose another business or parcel.";
                return result;
            }

            LandLedgers.World.BuildingDefinition shell = shellOptions[WrapIndex(shellOptionIndex, shellOptions.Count)];
            result.SelectedShellName = shell != null ? shell.DisplayName : "Selected shell";

            if (acquisitionMarket.TryGetBusinessConstructionFitForOwnedPlotOption(plotId, businessOptionIndex, shellOptionIndex, out PlayerDevelopmentFitResult fit)
                && fit != null
                && fit.HasWarnings)
            {
                result.Warnings.Add(fit.WarningSummary);
            }

            if (acquisitionMarket.TryGetBusinessConstructionQuoteForOwnedPlotOption(plotId, businessOptionIndex, shellOptionIndex, out ConstructionInputQuote quote, out string message)
                && quote != null)
            {
                ApplyQuote(result, quote);
                result.EstimatedTotalPathCostCents = Mathf.Max(0, quote.CashCostCents + candidate.startupCostCents);
                if (!quote.CanProceed)
                {
                    AddQuoteBlockers(result, quote);
                }
            }
            else
            {
                result.HardBlockers.Add(message);
                result.EstimatedTotalPathCostCents = Mathf.Max(0, candidate.startupCostCents);
            }

            if (!string.IsNullOrWhiteSpace(candidate.fitWarningSummary))
            {
                result.Warnings.Add(candidate.fitWarningSummary);
            }

            result.FinancingGapCents = Mathf.Max(0, result.CashRequiredNowCents - AvailableExpansionCashCents(result));
            result.CanUseLoan = CanUseWorkingCapitalLoan();
            result.RecommendedAction = BuildConstructionAction(result, "Can build shell now.", "Build after funding/input blockers are resolved.");
            return result;
        }

        public ExpansionReadinessResult BuildForVacantShell(int buildingId, int optionIndex)
        {
            ExpansionReadinessResult result = CreateBaseResult(ExpansionReadinessKind.VacantShell);
            if (acquisitionMarket == null)
            {
                result.HardBlockers.Add("Acquisitions unavailable.");
                result.RecommendedAction = "Reconnect the acquisition market.";
                return result;
            }

            if (!acquisitionMarket.TryGetBusinessActivationCandidate(buildingId, optionIndex, out BusinessActivationCandidateState candidate)
                || candidate == null)
            {
                result.HardBlockers.Add("No business activation candidate is selected.");
                result.RecommendedAction = "Choose a business.";
                return result;
            }

            result.SelectedBusinessType = candidate.businessType;
            result.SelectedBusinessName = candidate.displayName;
            if (!string.IsNullOrWhiteSpace(candidate.fitWarningSummary))
            {
                result.Warnings.Add(candidate.fitWarningSummary);
            }

            if (acquisitionMarket.TryGetBusinessActivationQuote(buildingId, optionIndex, out ConstructionInputQuote quote, out string message)
                && quote != null)
            {
                ApplyQuote(result, quote);
                result.EstimatedTotalPathCostCents = quote.CashCostCents;
                if (!quote.CanProceed)
                {
                    AddQuoteBlockers(result, quote);
                }
            }
            else
            {
                result.CashRequiredNowCents = Mathf.Max(0, candidate.startupCostCents);
                result.EstimatedTotalPathCostCents = result.CashRequiredNowCents;
                result.HardBlockers.Add(message);
            }

            if (!candidate.eligible && !string.IsNullOrWhiteSpace(candidate.reason))
            {
                result.HardBlockers.Add(candidate.reason);
            }

            result.FinancingGapCents = Mathf.Max(0, result.CashRequiredNowCents - AvailableExpansionCashCents(result));
            result.CanUseLoan = CanUseWorkingCapitalLoan();
            result.RecommendedAction = BuildConstructionAction(result, "Can start business now.", "Start after funding/input blockers are resolved.");
            return result;
        }

        public string BuildExpansionCashText()
        {
            ExpansionReadinessResult result = CreateBaseResult(ExpansionReadinessKind.OwnedLand);
            string debt = result.ActiveDebtPaymentCents > 0
                ? $"Debt payment pressure {FormatMoney(result.ActiveDebtPaymentCents)}/week"
                : "Debt payment pressure none";
            string distribution = playerPortfolio != null
                ? $"Last distributions {FormatMoney(playerPortfolio.LastWeeklyDistributionCents)}"
                : "Last distributions unavailable";
            return $"Expansion Cash: Owner {FormatMoney(result.OwnerCashCents)} | Safe surplus {FormatMoney(result.SafeWithdrawableSurplusCents)} | {debt} | {distribution}";
        }

        private ExpansionReadinessResult CreateBaseResult(ExpansionReadinessKind kind)
        {
            return new ExpansionReadinessResult
            {
                Kind = kind,
                OwnerCashCents = playerPortfolio != null ? playerPortfolio.OwnerCashCents : 0,
                SafeWithdrawableSurplusCents = CalculateSafeWithdrawableSurplusCents(),
                ActiveDebtPaymentCents = playerDebtManager != null ? playerDebtManager.ActiveDebtPaymentCents : 0,
                RecommendedAction = "Choose an expansion action."
            };
        }

        private ExpansionReadinessResult BuildUnavailable(ExpansionReadinessKind kind, string message)
        {
            ExpansionReadinessResult result = CreateBaseResult(kind);
            result.HardBlockers.Add(message);
            result.RecommendedAction = message;
            return result;
        }

        private void ApplyQuote(ExpansionReadinessResult result, ConstructionInputQuote quote)
        {
            result.CashRequiredNowCents = quote.CashCostCents;
            result.EstimatedTotalPathCostCents = Mathf.Max(result.EstimatedTotalPathCostCents, quote.CashCostCents);
            if (quote.resources == null)
            {
                return;
            }

            for (int i = 0; i < quote.resources.Count; i++)
            {
                ConstructionResourceQuoteLine line = quote.resources[i];
                if (line == null || line.requiredUnits <= 0)
                {
                    continue;
                }

                ExpansionReadinessInputLine input = new()
                {
                    resourceKind = line.resourceKind,
                    displayName = line.displayName,
                    requiredUnits = line.requiredUnits,
                    availableUnits = line.availableUnits,
                    sourceLabel = line.sourceLabel
                };
                result.RequiredInputs.Add(input);
                result.AvailableInputs.Add(input);
            }
        }

        private static void AddQuoteBlockers(ExpansionReadinessResult result, ConstructionInputQuote quote)
        {
            if (quote == null)
            {
                return;
            }

            string missing = quote.BuildMissingSummary();
            if (!string.IsNullOrWhiteSpace(missing) && !string.Equals(missing, "none", StringComparison.OrdinalIgnoreCase))
            {
                result.HardBlockers.Add(missing);
            }
        }

        private void PreviewAcquisitionFinancing(
            ExpansionReadinessResult result,
            AcquisitionListing listing,
            AcquisitionDealState deal,
            int finalPriceCents)
        {
            result.FinancingGapCents = Mathf.Max(0, result.CashRequiredNowCents - AvailableExpansionCashCents(result));
            if (result.FinancingGapCents <= 0 || playerDebtManager == null || listing == null)
            {
                return;
            }

            int cashContribution = Mathf.Clamp(result.OwnerCashCents + Mathf.Max(0, deal != null ? deal.earnestMoneyCents : 0), 0, finalPriceCents);
            LoanCollateralKind collateralKind = listing.kind == AcquisitionListingKind.Business
                ? LoanCollateralKind.OperatingBusiness
                : LoanCollateralKind.Land;
            string collateralId = listing.kind == AcquisitionListingKind.Business
                ? $"building_{listing.buildingId:000}"
                : $"plot_{listing.plotId:000}";
            FinancingApprovalResult approval = playerDebtManager.EvaluateAcquisitionFinancing(
                listing.listingId,
                finalPriceCents,
                cashContribution,
                Mathf.Max(finalPriceCents, deal != null ? deal.estimatedValueCents : 0),
                collateralKind,
                collateralId);
            result.CanUseLoan = approval != null && approval.Approved && approval.Offer != null;
            if (result.CanUseLoan)
            {
                result.EstimatedLoanPaymentCents = PaymentScheduleBuilder.EstimatePaymentCents(approval.Offer.termStructure);
            }
            else if (approval != null && approval.RequiredConditions != null && approval.RequiredConditions.Count > 0)
            {
                result.Warnings.Add("Financing likely blocked: " + string.Join("; ", approval.RequiredConditions));
            }
        }

        private int CalculateSafeWithdrawableSurplusCents()
        {
            int surplus = 0;
            if (storeRuntime != null && storeRuntime.CurrentBusiness != null)
            {
                surplus += Mathf.Max(0, storeRuntime.CurrentCashCents - storeRuntime.ProtectedBusinessCashReserveCents);
            }

            if (sharedBusinessRuntime == null)
            {
                return surplus;
            }

            IReadOnlyList<BusinessInstanceState> businesses = sharedBusinessRuntime.Businesses;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null
                    || business.RuntimeState == null
                    || business.Owner == null
                    || business.Owner.OwnerKind != BusinessOwnerKind.Player
                    || business.BusinessType == BusinessType.GeneralStore)
                {
                    continue;
                }

                int reserve = SharedBusinessRuntimeManager.CalculateSharedSurvivalCashReserveCents(business);
                surplus += Mathf.Max(0, business.RuntimeState.CurrentCashCents - reserve);
            }

            return surplus;
        }

        private bool CanUseWorkingCapitalLoan()
        {
            return playerDebtManager != null
                && !playerDebtManager.HasActiveLoan
                && !playerDebtManager.HasPendingApplication;
        }

        private static int AvailableExpansionCashCents(ExpansionReadinessResult result)
        {
            return result != null
                ? Mathf.Max(0, result.OwnerCashCents) + Mathf.Max(0, result.SafeWithdrawableSurplusCents)
                : 0;
        }

        private static string BuildCashAction(ExpansionReadinessResult result, string ready, string blocked)
        {
            if (result == null)
            {
                return blocked;
            }

            if (result.FinancingGapCents <= 0)
            {
                return ready;
            }

            return result.CanUseLoan ? blocked : $"Need {FormatMoney(result.FinancingGapCents)} more Owner Cash or safe surplus.";
        }

        private static string BuildConstructionAction(ExpansionReadinessResult result, string ready, string blocked)
        {
            if (result == null)
            {
                return blocked;
            }

            if (result.FinancingGapCents <= 0 && !result.HasHardBlockers)
            {
                return ready;
            }

            if (result.FinancingGapCents > 0 && result.CanUseLoan)
            {
                return "Use Finances to request a loan, manually draw business cash with reserve risk in mind, or choose a cheaper option.";
            }

            return blocked;
        }

        // Keep acquisition readiness stage-aware so failed or protected files are not flattened back into
        // the same recommendation as an untouched lead.
        private static string BuildFailedAcquisitionAction(AcquisitionDealState deal)
        {
            if (deal == null)
            {
                return "Review the failed file before reopening inquiry.";
            }

            string failurePoint = BuildFailureBreakpointLabel(deal.failedFromStage);
            return string.IsNullOrWhiteSpace(failurePoint)
                ? "Review the failed file before reopening inquiry."
                : $"Review the failed {failurePoint} file before reopening inquiry.";
        }

        private static string BuildOptionAwareDiligenceAction(AcquisitionDealState deal)
        {
            return deal != null && deal.optionDeadlineDayIndex >= 0
                ? $"Can continue diligence now while earnest holds through about day {deal.optionDeadlineDayIndex}."
                : "Can continue diligence now while earnest protects the lane.";
        }

        private static string BuildOptionAwareTermsAction(AcquisitionDealState deal)
        {
            return deal != null && deal.optionDeadlineDayIndex >= 0
                ? $"Can agree terms now while earnest holds through about day {deal.optionDeadlineDayIndex}."
                : "Can agree terms now while earnest protects the lane.";
        }

        private static string BuildClosingReadyAction(AcquisitionDealState deal)
        {
            return deal != null && deal.closingDeadlineDayIndex >= 0
                ? $"Can close now before about day {deal.closingDeadlineDayIndex}."
                : "Can close now.";
        }

        private static string BuildClosingBlockedAction(AcquisitionDealState deal)
        {
            return deal != null && deal.closingDeadlineDayIndex >= 0
                ? $"Close after funding the gap before about day {deal.closingDeadlineDayIndex}."
                : "Close after funding the gap.";
        }

        private static string BuildLoanBridgeAcquisitionAction(AcquisitionDealState deal)
        {
            return deal != null && deal.stage == AcquisitionDealStage.TentativeAgreement && deal.closingDeadlineDayIndex >= 0
                ? "Loan can bridge the closing gap before the protected closing window runs out; payments come from Owner Cash."
                : "Loan can bridge the closing gap; payments come from Owner Cash.";
        }

        private static void AddAcquisitionProcessWarnings(ExpansionReadinessResult result, AcquisitionListing listing, AcquisitionDealState deal)
        {
            if (result == null || listing == null)
            {
                return;
            }

            AcquisitionDealStage stage = deal != null ? deal.stage : AcquisitionDealStage.None;
            switch (stage)
            {
                case AcquisitionDealStage.None:
                    if (listing.aiEligible)
                    {
                        result.Warnings.Add("Rival access is live; do not treat this lead as exclusive.");
                    }
                    else if (listing.playerFirstUntilDayIndex >= 0)
                    {
                        result.Warnings.Add($"Player-first access appears to hold through about day {listing.playerFirstUntilDayIndex}.");
                    }
                    else
                    {
                        result.Warnings.Add("Player-first access appears to hold for now, but inquiry still needs to happen.");
                    }

                    if (listing.pressure01 >= 0.65f)
                    {
                        result.Warnings.Add("Seller pressure is high; delay can cool the lead or strengthen rival entry.");
                    }
                    break;
                case AcquisitionDealStage.Inquiry:
                    result.Warnings.Add("Inquiry alone does not protect the lane; earnest is what reserves it.");
                    if (NeedsProofOfFunds(deal))
                    {
                        result.Warnings.Add("Seller still expects proof discipline before earnest can be accepted.");
                    }
                    break;
                case AcquisitionDealStage.EarnestCommitted:
                case AcquisitionDealStage.DiligenceComplete:
                    if (deal.optionDeadlineDayIndex >= 0)
                    {
                        result.Warnings.Add($"Earnest appears to protect the lane through about day {deal.optionDeadlineDayIndex}.");
                    }
                    else
                    {
                        result.Warnings.Add("Earnest is protecting the lane while diligence and terms are being worked.");
                    }

                    if (deal.stalledReviewCount > 0)
                    {
                        result.Warnings.Add($"Review drift is building ({deal.stalledReviewCount} stalled review week(s)).");
                    }
                    break;
                case AcquisitionDealStage.TentativeAgreement:
                    if (deal.closingDeadlineDayIndex >= 0)
                    {
                        result.Warnings.Add($"Protected closing window appears to run through about day {deal.closingDeadlineDayIndex}.");
                    }
                    else
                    {
                        result.Warnings.Add("The file is in protected closing, but the final closing deadline is not yet clear.");
                    }

                    if (deal.financingNeedCents > 0 && deal.financingContingencyPresent)
                    {
                        result.Warnings.Add("Closing still depends on financing clearing in time.");
                    }
                    break;
            }
        }

        private static string BuildFailureBreakpointLabel(AcquisitionDealStage failedFromStage)
        {
            return failedFromStage switch
            {
                AcquisitionDealStage.Inquiry => "inquiry",
                AcquisitionDealStage.EarnestCommitted => "diligence",
                AcquisitionDealStage.DiligenceComplete => "terms",
                AcquisitionDealStage.TentativeAgreement => "closing",
                _ => string.Empty
            };
        }

        private static bool NeedsProofOfFunds(AcquisitionDealState deal)
        {
            return deal != null
                && deal.proofRequired
                && deal.proofStatus < AcquisitionProofStatus.Verified;
        }

        private static bool IsFormalDiligenceComplete(AcquisitionDealState deal)
        {
            return deal != null
                && deal.requiredDiligenceMask != 0
                && (deal.completedDiligenceMask & deal.requiredDiligenceMask) == deal.requiredDiligenceMask;
        }

        private AcquisitionDealState FindDeal(string listingId)
        {
            if (acquisitionMarket == null || string.IsNullOrWhiteSpace(listingId))
            {
                return null;
            }

            IReadOnlyList<AcquisitionDealState> deals = acquisitionMarket.ActiveDeals;
            for (int i = 0; i < deals.Count; i++)
            {
                AcquisitionDealState deal = deals[i];
                if (deal != null && string.Equals(deal.listingId, listingId, StringComparison.OrdinalIgnoreCase))
                {
                    return deal;
                }
            }

            return null;
        }

        private static void AddLandAcquisitionWarnings(ExpansionReadinessResult result, AcquisitionListing listing)
        {
            if (result == null || listing == null)
            {
                return;
            }

            if (listing.plotZone != LandLedgers.World.PlotZone.Agricultural)
            {
                result.Warnings.Add("Ranch, Crop Farm, and Small Sawmill startups need matching agricultural parcels; this listing is not agricultural.");
            }
            else
            {
                result.Warnings.Add("Agricultural startups still require the matching parcel role for ranch, crop farm, or sawmill use.");
            }
        }

        private static int WrapIndex(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            if (index < 0)
            {
                return count - 1;
            }

            return index % count;
        }

        private static string FormatMoney(int cents)
        {
            return "$" + (Mathf.Max(0, cents) / 100f).ToString("N2");
        }
    }
}
