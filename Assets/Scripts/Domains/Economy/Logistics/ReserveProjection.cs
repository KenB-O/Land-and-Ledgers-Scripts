// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice 6 — adaptive inventory reserves (directive P4, P9 plan
// Slice 6, Priority F). A planning-layer recommendation for how much stock
// a business should hold: recomputed from recent demand every time, never
// a stored constant, never a fixed 12-loaf or 24-loaf reserve.
//
// The formula, stated in full so any reader can check it (no hidden
// coefficients — every number in the result comes from a named input):
//   demand cover  = ceil(recent demand per day x lead time in days)
//   gross need    = demand cover + uncertainty buffer + existing
//                   commitments + forecast spoilage
//   net need      = gross need - usable stock on hand
//                             - confirmed incoming deliveries
//                             - producible during the lead time
//                             (floor of capacity per day x lead time:
//                              only whole units count as producible)
//   recommended   = max(0, net need)
// The cover rounds UP (a fractional loaf cannot be reserved); the
// producible rounds DOWN (a fractional loaf cannot be counted as baked).
//
// Unknown handling: the two inputs the cover cannot be computed without
// — recent demand and replenishment lead time — make the recommendation
// Unknown when they are Unknown, and the reasoning names the missing
// input instead of inventing a value. Every other Unknown input is
// EXCLUDED from the figure (counted as zero) and NAMED in the reasoning,
// so the projection stays computable while staying honest about what it
// left out. Confidence is High only when every input is Known, Reduced
// when any input that could move the number is Unknown (or the reserve
// exceeds available storage), and Unknown when the recommendation is.
//
// Usable stock discounts lots by their Slice 5 condition
// (PerishableLotState): a lot past its useful life (remaining life Known
// and zero or less) or in an Unsafe safety state counts as ZERO usable
// stock — 24 aging loaves do not necessarily count as 24 future sellable
// loaves. A Suspect lot is counted at full units with its caution named;
// there is no authored rule discounting Suspect, and this type invents
// none.
//
// NO AUTOMATIC PROCUREMENT: the projection is a recommendation. Nothing
// orders, nothing reserves stock in any live authority, nothing moves.
// All inputs are passed in, never hardcoded.
//
// GAPS, recorded not papered over:
//   - No authored replenishment lead time exists anywhere in v3.1, so a
//     caller must supply one; without it the projection is Unknown.
//   - The F2 BusinessStorage leg (PhysicalCapacityContracts.cs) reports
//     capacity axes (Total, Allocated, Occupied, IdentifiedLotUnits),
//     not item-level usable stock. The item-level stock figure is
//     BusinessRuntimeState.GetRetailItemStockUnits (BusinessRuntimeState
//     .cs:3581), which carries no Slice 5 condition data — so the
//     lot-level discount cannot be computed from the leg alone. The
//     caller supplies ActualUsableStockUnits, computed from lots where
//     Slice 5 states exist (UsableStockFromLots), and AvailableStorage
//     from the F2 leg where it answers.
//   - A Stale physical condition alone does not zero a lot here: the
//     Bakery driver never sells stale loaves, but no authored rule in
//     the condition model maps Stale to zero usable stock, and the
//     discount rule in this slice is remaining life plus safety state
//     only. A stale lot with Unknown remaining life is counted, and the
//     per-lot reason says so.
//
// UNWIRED BY DESIGN: plain data plus pure functions. No flow consults
// this type, no driver is activated, no shopping loop, Bakery driver, or
// live flow is touched.

using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Logistics
{
    /// <summary>
    /// A unit quantity that is Known or Unknown. Unknown is the honest
    /// answer where no authority supplies a figure — never a stored
    /// zero, never a default. A negative quantity is a data error and
    /// becomes Unknown, never a stored negative.
    /// </summary>
    public struct ReserveQuantity
    {
        private readonly bool isKnown;
        private readonly int units;

        public ReserveQuantity(bool known, int units)
        {
            isKnown = known && units >= 0;
            this.units = known && units >= 0 ? units : 0;
        }

        public bool IsKnown => isKnown && units >= 0;
        public int Units => IsKnown ? units : 0;

        public static ReserveQuantity Unknown => new ReserveQuantity(false, 0);
        public static ReserveQuantity Known(int units) => new ReserveQuantity(true, units);
    }

    /// <summary>
    /// A lead time in days that is Known or Unknown. No authored lead
    /// time exists anywhere in v3.1, so an Unknown here is the normal
    /// case until a caller supplies one. A non-finite or negative lead
    /// time becomes Unknown, never a stored number.
    /// </summary>
    public struct LeadTimeDays
    {
        private readonly bool isKnown;
        private readonly double days;

        public LeadTimeDays(bool known, double days)
        {
            isKnown = known && !double.IsNaN(days) && !double.IsInfinity(days) && days >= 0.0;
            this.days = isKnown ? days : 0.0;
        }

        public bool IsKnown => isKnown && !double.IsNaN(days) && !double.IsInfinity(days) && days >= 0.0;
        public double Days => IsKnown ? days : 0.0;

        public static LeadTimeDays Unknown => new LeadTimeDays(false, 0.0);
        public static LeadTimeDays Known(double days) => new LeadTimeDays(true, days);
    }

    /// <summary>
    /// Every input to a reserve projection, passed in by the caller —
    /// nothing here is hardcoded, and nothing is read from a live
    /// authority by this type. Unknown inputs are excluded from the
    /// figure and named in the projection's reasoning (see
    /// <see cref="ReserveProjection.Compute(ReserveInputs)"/>). A struct:
    /// a default instance is all-Unknown, which is the honest default —
    /// no input is ever assumed.
    /// </summary>
    public struct ReserveInputs
    {
        /// <summary>Recent demand, units per day, from the observation half (Unknown when no history).</summary>
        public ReserveQuantity RecentDemandPerDay;

        /// <summary>Observed demand spread, in units (Unknown when unmeasured — no buffer is then added).</summary>
        public ReserveQuantity DemandUncertaintyUnits;

        /// <summary>Usable stock on hand, in units — discounted by Slice 5 condition (see UsableStockFromLots).</summary>
        public ReserveQuantity ActualUsableStockUnits;

        /// <summary>Units expected to spoil before sale, from the Slice 5 condition model.</summary>
        public ReserveQuantity ForecastSpoilageUnits;

        /// <summary>Units already committed (held/reserved for a sale or shipment).</summary>
        public ReserveQuantity ExistingCommitmentsUnits;

        /// <summary>Units in confirmed incoming deliveries (ordered and confirmed, not merely hoped for).</summary>
        public ReserveQuantity ConfirmedIncomingDeliveriesUnits;

        /// <summary>Units the business can produce per day (a producer's capacity; Unknown for a pure retailer).</summary>
        public ReserveQuantity ProductionCapacityPerDay;

        /// <summary>Replenishment lead time in days (Unknown when no authored value — the projection is then Unknown).</summary>
        public LeadTimeDays ReplenishmentLeadTimeDays;

        /// <summary>Available storage in units, from the F2 BusinessStorage leg (Unknown where the leg does not answer).</summary>
        public ReserveQuantity AvailableStorageUnits;

        /// <summary>
        /// Bridges the observation half into inputs: recent demand per day
        /// is the ceiling of the average daily demand over the window
        /// ending at asOfDay, and uncertainty is the ceiling of the
        /// measured daily-demand spread. Both are Unknown when the window
        /// holds too little history (the diagnostic names the gap), and
        /// every other input stays Unknown — the bridge derives demand
        /// only, and invents no lead time, stock, or capacity.
        /// </summary>
        public static ReserveInputs FromObservation(DemandObservation observation,
            string businessId, string itemId, int recentDays, int asOfDay, out string diagnostic)
        {
            diagnostic = string.Empty;
            var inputs = new ReserveInputs();
            if (observation == null)
            {
                diagnostic = "ReserveInputs: no observation supplied — no demand rate can be derived.";
                return inputs;
            }
            if (recentDays <= 0)
            {
                diagnostic = "ReserveInputs: the recent window must be at least one day.";
                return inputs;
            }
            if (observation.TryAverageDailyDemand(businessId, itemId, recentDays, asOfDay,
                out double average))
            {
                inputs.RecentDemandPerDay = ReserveQuantity.Known((int)Math.Ceiling(average));
            }
            else
            {
                diagnostic = $"ReserveInputs: no demand history for {businessId}/{itemId} in the last {recentDays} day(s) ending day {asOfDay} — no demand rate can be derived.";
            }
            if (observation.TryDailyDemandStdDev(businessId, itemId, recentDays, asOfDay,
                out double stdDev))
            {
                inputs.DemandUncertaintyUnits = ReserveQuantity.Known((int)Math.Ceiling(stdDev));
            }
            else if (inputs.RecentDemandPerDay.IsKnown)
            {
                diagnostic += string.IsNullOrEmpty(diagnostic) ? string.Empty : " ";
                diagnostic += "Demand spread is unmeasured (fewer than two observed days), so no uncertainty buffer can be derived.";
            }
            return inputs;
        }
    }

    /// <summary>How much the projection can be trusted, from its inputs alone.</summary>
    public enum ReserveConfidence
    {
        Unknown = 0,
        Reduced = 1,
        High = 2,
    }

    /// <summary>
    /// The usable-stock assessment of a set of lots: how many units
    /// count as future sellable stock, and one reason per lot. A struct:
    /// a small value result, like the other assessment records in this
    /// layer.
    /// </summary>
    public struct UsableStockAssessment
    {
        public UsableStockAssessment(int usableUnits, IReadOnlyList<string> perLotReasons)
        {
            UsableUnits = usableUnits;
            PerLotReasons = perLotReasons ?? new List<string>().AsReadOnly();
        }

        public int UsableUnits { get; }
        public IReadOnlyList<string> PerLotReasons { get; }
    }

    /// <summary>
    /// An adaptive reserve recommendation: the target stock to hold,
    /// recomputed from the supplied inputs every time. A recommendation
    /// only — nothing orders, nothing reserves stock in any live
    /// authority. The reasoning names every input that moved the number
    /// and every Unknown input that was left out of it.
    /// </summary>
    public sealed class ReserveProjection
    {
        private ReserveProjection(ReserveQuantity recommendedReserveUnits,
            List<string> reasoning, ReserveConfidence confidence,
            ReserveQuantity uncertaintyBandUnits, bool fitsAvailableStorage,
            ReserveQuantity storageShortfallUnits)
        {
            RecommendedReserveUnits = recommendedReserveUnits;
            Reasoning = reasoning.AsReadOnly();
            Confidence = confidence;
            UncertaintyBandUnits = uncertaintyBandUnits;
            FitsAvailableStorage = fitsAvailableStorage;
            StorageShortfallUnits = storageShortfallUnits;
        }

        /// <summary>The recommended reserve, in units — Unknown when demand or lead time is Unknown.</summary>
        public ReserveQuantity RecommendedReserveUnits { get; }

        /// <summary>One line per input that moved the number, and one per Unknown input left out.</summary>
        public IReadOnlyList<string> Reasoning { get; }

        /// <summary>High only when every input is Known; Reduced when any input is Unknown or storage falls short.</summary>
        public ReserveConfidence Confidence { get; }

        /// <summary>The uncertainty band around the recommendation (± units) — the measured buffer when known.</summary>
        public ReserveQuantity UncertaintyBandUnits { get; }

        /// <summary>Whether the recommended reserve fits the available storage (true when storage is Unknown — uncheckable).</summary>
        public bool FitsAvailableStorage { get; }

        /// <summary>How far the recommendation exceeds available storage (zero when it fits or storage is Unknown).</summary>
        public ReserveQuantity StorageShortfallUnits { get; }

        /// <summary>
        /// Computes the projection from the supplied inputs. Pure: the
        /// inputs are never mutated, and no authority is read or written.
        /// </summary>
        public static ReserveProjection Compute(ReserveInputs inputs)
        {
            var reasoning = new List<string>();
            if (!inputs.RecentDemandPerDay.IsKnown)
            {
                reasoning.Add("recent demand unknown — no observed demand rate was supplied, so no reserve can be computed.");
                return new ReserveProjection(ReserveQuantity.Unknown, reasoning,
                    ReserveConfidence.Unknown, ReserveQuantity.Unknown, true, ReserveQuantity.Known(0));
            }
            if (!inputs.ReplenishmentLeadTimeDays.IsKnown)
            {
                reasoning.Add("lead time unknown — no authored replenishment lead time was supplied, so no demand cover can be computed.");
                return new ReserveProjection(ReserveQuantity.Unknown, reasoning,
                    ReserveConfidence.Unknown, ReserveQuantity.Unknown, true, ReserveQuantity.Known(0));
            }

            double leadDays = inputs.ReplenishmentLeadTimeDays.Days;
            int demandCover = (int)Math.Ceiling(inputs.RecentDemandPerDay.Units * leadDays);
            reasoning.Add($"demand cover: {inputs.RecentDemandPerDay.Units} units/day x {leadDays} day(s) lead time = {demandCover} units.");

            int buffer = 0;
            if (inputs.DemandUncertaintyUnits.IsKnown)
            {
                buffer = inputs.DemandUncertaintyUnits.Units;
                reasoning.Add($"uncertainty buffer: {buffer} units from the observed demand spread.");
            }
            else
            {
                reasoning.Add("uncertainty unknown — no buffer added (no measured demand spread was supplied).");
            }

            int commitments = 0;
            if (inputs.ExistingCommitmentsUnits.IsKnown)
            {
                commitments = inputs.ExistingCommitmentsUnits.Units;
                reasoning.Add($"existing commitments: {commitments} units already committed raise the reserve.");
            }
            else
            {
                reasoning.Add("existing commitments unknown — no commitments counted.");
            }

            int spoilage = 0;
            if (inputs.ForecastSpoilageUnits.IsKnown)
            {
                spoilage = inputs.ForecastSpoilageUnits.Units;
                reasoning.Add($"forecast spoilage: {spoilage} units expected to spoil before sale raise the reserve.");
            }
            else
            {
                reasoning.Add("forecast spoilage unknown — no spoilage added (no condition forecast was supplied).");
            }

            int grossNeed = demandCover + buffer + commitments + spoilage;

            int usableStock = 0;
            if (inputs.ActualUsableStockUnits.IsKnown)
            {
                usableStock = inputs.ActualUsableStockUnits.Units;
                reasoning.Add($"usable stock: {usableStock} units on hand reduce the reserve.");
            }
            else
            {
                reasoning.Add("usable stock unknown — the figure counts no stock on hand.");
            }

            int incoming = 0;
            if (inputs.ConfirmedIncomingDeliveriesUnits.IsKnown)
            {
                incoming = inputs.ConfirmedIncomingDeliveriesUnits.Units;
                reasoning.Add($"incoming deliveries: {incoming} confirmed incoming units reduce the reserve.");
            }
            else
            {
                reasoning.Add("incoming deliveries unknown — the figure counts no incoming stock.");
            }

            int producible = 0;
            if (inputs.ProductionCapacityPerDay.IsKnown)
            {
                producible = (int)Math.Floor(inputs.ProductionCapacityPerDay.Units * leadDays);
                reasoning.Add($"production capacity: {inputs.ProductionCapacityPerDay.Units} units/day producible over {leadDays} day(s) = {producible} units reduce the reserve.");
            }
            else
            {
                reasoning.Add("production capacity unknown — the figure counts no production during the lead time.");
            }

            int netNeed = grossNeed - usableStock - incoming - producible;
            int recommended = Math.Max(0, netNeed);
            reasoning.Add($"recommended reserve: {grossNeed} gross need - {usableStock} usable stock - {incoming} incoming - {producible} producible = {netNeed}, floored at zero = {recommended} units.");

            bool fitsStorage = true;
            ReserveQuantity shortfall = ReserveQuantity.Known(0);
            if (inputs.AvailableStorageUnits.IsKnown)
            {
                int storage = inputs.AvailableStorageUnits.Units;
                reasoning.Add($"available storage: {storage} units.");
                if (recommended > storage)
                {
                    fitsStorage = false;
                    shortfall = ReserveQuantity.Known(recommended - storage);
                    reasoning.Add($"storage shortfall: the {recommended}-unit reserve exceeds {storage} units of available storage by {shortfall.Units} units — the need is reported, not silently capped.");
                }
            }
            else
            {
                reasoning.Add("available storage unknown — the reserve is not checked against storage.");
            }

            bool allKnown = inputs.DemandUncertaintyUnits.IsKnown
                && inputs.ActualUsableStockUnits.IsKnown
                && inputs.ForecastSpoilageUnits.IsKnown
                && inputs.ExistingCommitmentsUnits.IsKnown
                && inputs.ConfirmedIncomingDeliveriesUnits.IsKnown
                && inputs.ProductionCapacityPerDay.IsKnown
                && inputs.AvailableStorageUnits.IsKnown;
            ReserveConfidence confidence = allKnown && fitsStorage
                ? ReserveConfidence.High
                : ReserveConfidence.Reduced;
            ReserveQuantity band = inputs.DemandUncertaintyUnits.IsKnown
                ? ReserveQuantity.Known(buffer)
                : ReserveQuantity.Unknown;
            return new ReserveProjection(ReserveQuantity.Known(recommended), reasoning,
                confidence, band, fitsStorage, shortfall);
        }

        /// <summary>
        /// Usable stock across a set of Slice 5 lot states. A lot past its
        /// useful life (remaining life Known and zero or less) or in an
        /// Unsafe safety state counts as ZERO usable stock. Every other
        /// lot counts at full units: a Suspect lot is counted with its
        /// caution named, and a Stale lot with Unknown remaining life is
        /// counted with the missing stale-discount rule named (see the
        /// file header). A null lot entry counts as zero. Pure: the states
        /// are never mutated.
        /// </summary>
        public static UsableStockAssessment UsableStockFromLots(IEnumerable<PerishableLotState> lots)
        {
            var reasons = new List<string>();
            int usable = 0;
            if (lots == null)
            {
                reasons.Add("no lots supplied — usable stock is zero, not unknown.");
                return new UsableStockAssessment(0, reasons);
            }
            foreach (PerishableLotState lot in lots)
            {
                if (lot == null)
                {
                    reasons.Add("a null lot entry counts as zero usable stock.");
                    continue;
                }
                string label = "lot " + lot.Key + " (" + lot.ItemId + ", " + lot.Units + "u)";
                if (lot.SafetyState == PerishableSafetyState.Unsafe)
                {
                    reasons.Add(label + ": unsafe — zero usable stock.");
                    continue;
                }
                if (lot.RemainingUsefulLife.IsKnown && lot.RemainingUsefulLife.Days <= 0.0)
                {
                    reasons.Add(label + ": past its useful life — zero usable stock.");
                    continue;
                }
                usable += lot.Units;
                if (lot.SafetyState == PerishableSafetyState.Suspect)
                {
                    reasons.Add(label + ": suspect — counted at full units; safety evidence unresolved: " + lot.SafetyEvidence);
                }
                else if (lot.PhysicalCondition == PerishablePhysicalCondition.Stale)
                {
                    reasons.Add(label + ": counted at full units (stale — no authored rule discounts stale condition alone; see the file header).");
                }
                else
                {
                    reasons.Add(label + ": counted at full units.");
                }
            }
            return new UsableStockAssessment(usable, reasons);
        }
    }
}
