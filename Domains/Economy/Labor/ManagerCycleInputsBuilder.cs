// Pass IX WS5 (A3-07): ManagerCycleInputs built from LIVE business state.
//
// Before this pass the sole production caller (SimulationDrivers.
// DriveAutonomousLabor) set none of the ManagerBrain's evidence fields and
// hard-coded MaxBorrowingCents = 0, so five of the seven decision kinds
// (Procure, PriceChange, OrderMaintenance, Borrow, SellAsset) could never
// originate in production and ManagerBrain.WeeklyCycle had no production
// caller at all. This builder derives every evidence input from live
// state, on the record:
//
//   Shortage / procurement — runtime category stock vs its reorder target
//     (the runtime's own "low" posture: stock health at/below 0.7 with
//     units missing), plus generic retail lines at/below their reorder
//     point. The proposed amount is the missing units priced at the
//     business's recorded average unit cost for that stock. No recorded
//     cost -> no amount -> no proposal (no confident number w/o evidence).
//   Maintenance — the business's own equipment assets (register entries
//     whose owner is this business) below the worn threshold (condition
//     < 0.5, BusinessEquipmentRegister's Canon §11.5 threshold).
//   Pricing — the market-capture / price-index readout: weak capture
//     (below 0.5) or a price index at/above half the local tolerance
//     requests an adjustment. Calibration thresholds, documented here.
//   Borrowing — the cash-vs-reserve gap: cash below the survival reserve
//     proposes borrowing exactly the gap. MaxBorrowingCents is the
//     delegation's REAL bound: the largest MaxBorrowingCents among the
//     business's live standing instructions (zero when no instruction
//     authorizes borrowing — the brain escalates over-bound proposals).
//   Asset sale — lifecycle distress (cash below the survival reserve)
//     names a SPECIFIC asset from the business's own registers
//     (coordinator decision 3): the most worn equipment asset with
//     book-cost evidence, else the largest un-liquidated formation asset.
//     The proposed amount is the appraisal's ASKING price for THAT asset
//     (its estimated value — the owner policy approves at/above appraised
//     value; the liquidation value is the distinct close-down basis).
//     No evidence, no proposal.
//
// The builder is a plain class: SimulationDrivers (a MonoBehaviour,
// harness-excluded) assembles the live evidence; everything derived is
// testable here. It moves no cash and writes no authority — it only
// observes, exactly what a manager person could legitimately see.
using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Delegation;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// Live evidence for one manager cycle, assembled by the driver from
    /// production authorities. Plain data; every field is an observation,
    /// never a decision.
    /// </summary>
    public sealed class ManagerCycleLiveEvidence
    {
        public BusinessInstanceState Business;
        public LaborDemandReport DemandReport;
        public DelegatedAuthority Authority;
        public List<StandingInstruction> Instructions = new List<StandingInstruction>();
        public int ManagerPersonId = -1;
        public int DayIndex;
        /// <summary>The calibrated survival reserve (cash-vs-reserve gap basis).</summary>
        public int SurvivalReserveCents;
        /// <summary>Equipment register entries; the builder keeps only the
        /// ones this business owns.</summary>
        public IEnumerable<EquipmentAsset> EquipmentAssets;
        /// <summary>The formation asset register's entries (the register the
        /// lifecycle driver treats as the business's formation assets).</summary>
        public IEnumerable<NpcFormationAsset> FormationAssets;
        /// <summary>Market-capture readout availability + values (from the
        /// existing market-capture computation).</summary>
        public bool HasMarketReadout;
        public float MarketCapture01;
        public float PriceIndex01;
        public int JudgmentSeed;
        public float JudgmentQuality01 = 0.8f;
    }

    /// <summary>Builds ManagerCycleInputs from live evidence (A3-07).</summary>
    public sealed class ManagerCycleInputsBuilder
    {
        /// <summary>Stock health at/below this is the runtime's "low" posture
        /// (BusinessRuntimeState.BuildStockReadinessLine) — the shortage line.</summary>
        public const float ShortageStockHealth01 = 0.7f;

        /// <summary>Equipment below this condition needs maintenance
        /// (BusinessEquipmentRegister's worn threshold, Canon §11.5).</summary>
        public const float WornConditionThreshold01 = 0.5f;

        /// <summary>Calibration: capture below this requests a pricing review.</summary>
        public const float WeakCapture01 = 0.5f;

        /// <summary>Calibration: a price index at/above this requests a pricing review.</summary>
        public const float HighPriceIndex01 = 0.5f;

        private readonly AssetSaleAppraisalService appraisal;

        public ManagerCycleInputsBuilder(AssetSaleAppraisalService appraisal = null)
        {
            this.appraisal = appraisal ?? new AssetSaleAppraisalService();
        }

        public ManagerCycleInputs Build(ManagerCycleLiveEvidence evidence, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var inputs = new ManagerCycleInputs();
            if (evidence == null || evidence.Business == null)
            {
                diagnostics.Add("ManagerCycleInputsBuilder: no business evidence — empty inputs.");
                return inputs;
            }

            BusinessInstanceState business = evidence.Business;
            BusinessRuntimeState runtime = business.RuntimeState;
            int cash = runtime != null ? runtime.CurrentCashCents : 0;
            int reserve = Math.Max(0, evidence.SurvivalReserveCents);

            inputs.BusinessInstanceId = business.InstanceId ?? string.Empty;
            inputs.ManagerPersonId = evidence.ManagerPersonId;
            inputs.CashCents = cash;
            inputs.DemandReport = evidence.DemandReport;
            inputs.Instructions = evidence.Instructions ?? new List<StandingInstruction>();
            inputs.DelegatedScopes = ScopeNames(evidence.Authority != null ? evidence.Authority.Scopes : DelegationScope.None);
            inputs.DayIndex = evidence.DayIndex;
            inputs.JudgmentSeed = evidence.JudgmentSeed;
            inputs.JudgmentQuality01 = evidence.JudgmentQuality01;
            inputs.PurchaseLimitCents = evidence.Authority != null
                ? Math.Max(0, evidence.Authority.PurchaseLimitCents) : 0;
            inputs.MaxBorrowingCents = ResolveMaxBorrowingCents(inputs.Instructions, business.InstanceId);

            DeriveShortage(business, runtime, inputs, diagnostics);
            List<EquipmentAsset> ownedEquipment = OwnedEquipment(evidence, business);
            inputs.MaintenanceNeeded = DeriveMaintenance(ownedEquipment, diagnostics);
            inputs.PricingAdjustmentRequested = DerivePricing(evidence, diagnostics);

            // Borrowing: the cash-vs-reserve gap, no more and no less.
            if (cash < reserve)
            {
                inputs.ProposedBorrowingCents = reserve - cash;
                diagnostics.Add($"ManagerCycleInputsBuilder: '{business.InstanceId}' cash {cash}c is " +
                                $"{inputs.ProposedBorrowingCents}c below its {reserve}c reserve — borrowing proposed " +
                                $"(owner max {inputs.MaxBorrowingCents}c).");
            }

            DeriveAssetSale(evidence, business, ownedEquipment, cash, reserve, inputs, diagnostics);
            return inputs;
        }

        /// <summary>
        /// The delegation's real borrowing bound: the largest
        /// MaxBorrowingCents carried by the business's live standing
        /// instructions. No live instruction -> 0 (no borrowing authorized).
        /// </summary>
        public static int ResolveMaxBorrowingCents(List<StandingInstruction> instructions, string businessInstanceId)
        {
            int max = 0;
            if (instructions == null) return max;
            foreach (StandingInstruction instruction in instructions)
            {
                if (instruction == null || instruction.IsRevoked) continue;
                if (!string.IsNullOrWhiteSpace(businessInstanceId)
                    && !string.Equals(instruction.BusinessInstanceId, businessInstanceId, StringComparison.Ordinal))
                    continue;
                if (instruction.MaxBorrowingCents > max) max = instruction.MaxBorrowingCents;
            }

            return max;
        }

        private static void DeriveShortage(
            BusinessInstanceState business, BusinessRuntimeState runtime,
            ManagerCycleInputs inputs, List<string> diagnostics)
        {
            if (runtime == null) return;
            long proposed = 0;
            var counted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Category stock vs reorder target (the shared-runtime stock).
            foreach (CategoryStockState stock in runtime.CategoryStock)
            {
                if (stock == null || stock.TargetStockUnits <= 0) continue;
                if (stock.MissingTargetStockUnits <= 0) continue;
                if (stock.StockHealth01 > ShortageStockHealth01 && !stock.HasPendingReorder) continue;
                int unitCost = business.GenericConfiguration.GetInventoryAverageUnitCostCents(stock.CategoryId);
                if (unitCost <= 0) continue; // no recorded cost: no confident amount
                proposed += (long)stock.MissingTargetStockUnits * unitCost;
                counted.Add(stock.CategoryId);
            }

            // Generic retail lines at/below their reorder point.
            foreach (GenericInventoryPosition position in business.GenericConfiguration.Inventory)
            {
                if (position == null || string.IsNullOrWhiteSpace(position.ProductId)) continue;
                if (counted.Contains(position.ProductId)) continue;
                if (!business.GenericConfiguration.Retail.TryGetLine(position.ProductId, out GenericRetailProductLine line)
                    || line == null)
                    continue;
                if (!line.CanReorder(position.AvailableQuantity)) continue;
                float missing = line.TargetStock - position.AvailableQuantity;
                if (missing <= 0f) continue;
                int unitCost = position.AverageUnitCostCents;
                if (unitCost <= 0) continue;
                proposed += (long)Math.Ceiling(missing) * unitCost;
                counted.Add(position.ProductId);
            }

            if (proposed > 0)
            {
                inputs.ShortageObserved = true;
                inputs.ProposedProcurementCents = (int)Math.Min(proposed, int.MaxValue);
                diagnostics.Add($"ManagerCycleInputsBuilder: '{business.InstanceId}' shortage observed — " +
                                $"proposed procurement {inputs.ProposedProcurementCents}c (missing units at recorded unit cost).");
            }
        }

        private static List<EquipmentAsset> OwnedEquipment(
            ManagerCycleLiveEvidence evidence, BusinessInstanceState business)
        {
            var owned = new List<EquipmentAsset>();
            if (evidence.EquipmentAssets == null) return owned;
            foreach (EquipmentAsset asset in evidence.EquipmentAssets)
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.AssetId)) continue;
                if (string.Equals(asset.OwnerKind, "business", StringComparison.Ordinal)
                    && string.Equals(asset.OwnerId, business.InstanceId, StringComparison.Ordinal))
                    owned.Add(asset);
            }

            return owned;
        }

        private static bool DeriveMaintenance(List<EquipmentAsset> ownedEquipment, List<string> diagnostics)
        {
            foreach (EquipmentAsset asset in ownedEquipment)
            {
                if (asset.Condition01 < WornConditionThreshold01)
                {
                    diagnostics.Add($"ManagerCycleInputsBuilder: equipment '{asset.AssetId}' condition " +
                                    $"{asset.Condition01:P0} is below the worn threshold — maintenance needed.");
                    return true;
                }
            }

            return false;
        }

        private static bool DerivePricing(ManagerCycleLiveEvidence evidence, List<string> diagnostics)
        {
            if (!evidence.HasMarketReadout) return false;
            bool weakCapture = evidence.MarketCapture01 < WeakCapture01;
            bool highPrice = evidence.PriceIndex01 >= HighPriceIndex01;
            if (weakCapture || highPrice)
            {
                diagnostics.Add($"ManagerCycleInputsBuilder: pricing review requested — capture " +
                                $"{evidence.MarketCapture01:P0}, price index {evidence.PriceIndex01:P0}.");
                return true;
            }

            return false;
        }

        private void DeriveAssetSale(
            ManagerCycleLiveEvidence evidence, BusinessInstanceState business,
            List<EquipmentAsset> ownedEquipment, int cash, int reserve,
            ManagerCycleInputs inputs, List<string> diagnostics)
        {
            // Asset sales originate from lifecycle distress only: cash below
            // the survival reserve (the resolver re-checks this same line).
            if (cash >= reserve) return;

            // Candidate 1: the most worn owned equipment asset WITH
            // book-cost evidence (a worn asset is the least productive to
            // keep; without evidence it cannot be appraised, so it cannot
            // be proposed either).
            EquipmentAsset candidate = null;
            AssetAppraisalResult candidateValue = null;
            foreach (EquipmentAsset asset in ownedEquipment)
            {
                if (asset.IsReserved) continue;
                AssetAppraisalResult value = appraisal.AppraiseEquipment(business.InstanceId, asset);
                if (!value.HasEvidence || value.AskingPriceCents <= 0) continue;
                if (candidate == null || asset.Condition01 < candidate.Condition01)
                {
                    candidate = asset;
                    candidateValue = value;
                }
            }

            if (candidate != null)
            {
                // The proposed price is the ASKING price (the appraised
                // estimated value): the owner-resolution policy approves a
                // distress sale only at/above appraised value, so asking
                // the liquidation value would be denied by construction.
                // The liquidation value remains the close-down basis
                // (RegisterLiquidationPort), a distinct kind.
                inputs.ProposedAssetId = candidate.AssetId;
                inputs.ProposedAssetSaleCents = candidateValue.AskingPriceCents;
                diagnostics.Add($"ManagerCycleInputsBuilder: '{business.InstanceId}' is distressed " +
                                $"({cash}c < {reserve}c reserve) — proposes selling equipment '{candidate.AssetId}' " +
                                $"at its {candidateValue.AskingPriceCents}c asking price " +
                                $"(estimated value; liquidation value {candidateValue.LiquidationValueCents}c).");
                return;
            }

            // Candidate 2: the largest un-liquidated formation asset.
            NpcFormationAsset formationCandidate = null;
            AssetAppraisalResult formationValue = null;
            if (evidence.FormationAssets != null)
            {
                foreach (NpcFormationAsset asset in evidence.FormationAssets)
                {
                    if (asset == null || asset.Liquidated) continue;
                    AssetAppraisalResult value = appraisal.AppraiseFormation(asset);
                    if (!value.HasEvidence || value.AskingPriceCents <= 0) continue;
                    if (formationCandidate == null || asset.CostCents > formationCandidate.CostCents)
                    {
                        formationCandidate = asset;
                        formationValue = value;
                    }
                }
            }

            if (formationCandidate != null)
            {
                inputs.ProposedAssetId = AssetSaleAppraisalService.FormationAssetPrefix + formationCandidate.ItemId;
                inputs.ProposedAssetSaleCents = formationValue.AskingPriceCents;
                diagnostics.Add($"ManagerCycleInputsBuilder: '{business.InstanceId}' is distressed " +
                                $"({cash}c < {reserve}c reserve) — proposes selling formation asset " +
                                $"'{formationCandidate.ItemId}' at its {formationValue.AskingPriceCents}c asking price " +
                                $"(estimated value; liquidation value {formationValue.LiquidationValueCents}c).");
            }
        }

        private static List<string> ScopeNames(DelegationScope scopes)
        {
            var names = new List<string>();
            if (scopes.HasFlag(DelegationScope.Purchasing)) names.Add(ManagerScopeNames.Purchasing);
            if (scopes.HasFlag(DelegationScope.Scheduling)) names.Add(ManagerScopeNames.Scheduling);
            if (scopes.HasFlag(DelegationScope.Maintenance)) names.Add(ManagerScopeNames.Maintenance);
            if (scopes.HasFlag(DelegationScope.CashHandling)) names.Add(ManagerScopeNames.CashHandling);
            if (scopes.HasFlag(DelegationScope.Inventory)) names.Add(ManagerScopeNames.Inventory);
            if (scopes.HasFlag(DelegationScope.Staffing)) names.Add(ManagerScopeNames.Staffing);
            return names;
        }
    }

    /// <summary>
    /// Pass IX WS5 (A3-13): feeds the market-capture factor into weekly
    /// settlement. Competition pressure is the complement of the existing
    /// market-capture computation: the share of the local market the
    /// business does NOT capture (off-map or competitors) is exactly the
    /// competitive pressure its reports and valuation evidence should
    /// carry — previously the scalar was only ever set by a monthly-cadence
    /// method with no production caller, so reports carried a permanent 0.
    /// </summary>
    public static class MarketCaptureCompetitionFeed
    {
        /// <summary>Competition pressure implied by a capture share.</summary>
        public static float CompetitionPressureFromCapture(float capture01)
        {
            float capture = capture01 < 0f ? 0f : capture01 > 1f ? 1f : capture01;
            return 1f - capture;
        }

        /// <summary>Records the capture-implied pressure on the runtime
        /// state (the weekly settlement feed point).</summary>
        public static void FeedCompetitionPressure(BusinessRuntimeState runtime, float capture01)
        {
            if (runtime == null) return;
            runtime.RecordCompetitionPressure01(CompetitionPressureFromCapture(capture01));
        }
    }
}
