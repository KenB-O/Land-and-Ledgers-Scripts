// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice P5 — replenishment planning (directive P5). Connects
// a Slice 6 recommended reserve to the best supplier option for the
// shortfall. A planning-layer recommendation only: it orders nothing,
// reserves nothing, and changes nothing in any live authority.
//
// THE SHORTFALL IS THE RESERVE: a Slice 6 ReserveProjection already
// nets usable stock, confirmed incoming deliveries, and producible
// units out of the gross need (its stated formula), so the shortfall
// the plan covers is the recommended reserve itself — this type never
// re-subtracts stock and never double-counts. An Unknown recommendation
// (Unknown demand or lead time in the projection) means no plan: the
// reasoning names the missing input from the projection.
//
// SELECTION RULE, stated once: the first comparable option in the
// comparison's ranking that the business can afford — affordable means
// the quoted order's expected total cost fits the available cash where
// both are Known. A best-ranked option that cannot be afforded is
// skipped and the skip is named; a self-collect option is never
// selected (the quote books no journey, so the goods would never
// arrive); an incomparable option is never selected. Where cash is
// Unknown the best comparable option is still selected, with the
// affordability named Unknown. Where nothing is comparable, affordable,
// or recommended, the plan selects nothing and says why.
//
// QUANTITY RULE: the plan covers min(shortfall, stated availability
// where Known, quoted quantity where Known). A smaller cover than the
// shortfall names the uncovered remainder. Freight is quoted for the
// option's full quoted quantity: where the plan covers fewer units,
// the freight stays as quoted and the reasoning says a smaller order
// may re-quote freight. Expected spoilage is charged at the planned
// quantity only where the planned quantity equals the quoted quantity
// (the forecast covered that quantity); otherwise it is excluded and
// the reasoning says so. Expected profit is then revenue minus goods
// minus freight minus that spoilage, at the planned quantity.
//
// UNWIRED BY DESIGN: plain data plus a pure function. No flow consults
// this type, no driver is activated, no shopping loop, Bakery driver,
// or live flow is touched. It reads the Slice 6 ReserveProjection and
// the SupplierComparison result only from inside its own owning
// directory (Domains/Economy/Logistics), so no earlier
// UnwiredSystems.md row is voided.

using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Logistics
{
    /// <summary>
    /// A replenishment plan: the reserve to cover, the supplier selected
    /// for the shortfall, and the expected cost, arrival, and profit of
    /// buying the planned quantity from that supplier. A recommendation
    /// only — nothing is ordered, nothing is reserved, nothing moves.
    /// </summary>
    public sealed class ReplenishmentPlan
    {
        private ReplenishmentPlan(ReserveQuantity recommendedReserve, ReserveQuantity shortfall,
            bool hasSelection, string selectedSupplierId, SupplierOption selectedOption,
            ReserveQuantity quantity, CentsAmount expectedCostCents, ReserveQuantity expectedArrivalDay,
            CentsAmount expectedProfitCents, List<string> reasoning)
        {
            RecommendedReserve = recommendedReserve;
            Shortfall = shortfall;
            HasSelection = hasSelection;
            SelectedSupplierId = selectedSupplierId ?? string.Empty;
            SelectedOption = selectedOption;
            Quantity = quantity;
            ExpectedCostCents = expectedCostCents;
            ExpectedArrivalDay = expectedArrivalDay;
            ExpectedProfitCents = expectedProfitCents;
            Reasoning = reasoning.AsReadOnly();
        }

        /// <summary>The recommended reserve the plan starts from (Slice 6).</summary>
        public ReserveQuantity RecommendedReserve { get; }

        /// <summary>The shortfall to cover — the recommended reserve itself (the projection already nets stock).</summary>
        public ReserveQuantity Shortfall { get; }

        /// <summary>Whether a supplier was selected.</summary>
        public bool HasSelection { get; }

        /// <summary>The selected supplier id, or empty when nothing was selected.</summary>
        public string SelectedSupplierId { get; }

        /// <summary>The selected option (default when nothing was selected).</summary>
        public SupplierOption SelectedOption { get; }

        /// <summary>The planned quantity, in the item's order units.</summary>
        public ReserveQuantity Quantity { get; }

        /// <summary>The expected cost of the planned quantity (goods plus freight as quoted).</summary>
        public CentsAmount ExpectedCostCents { get; }

        /// <summary>The expected arrival day of the planned quantity.</summary>
        public ReserveQuantity ExpectedArrivalDay { get; }

        /// <summary>The expected profit of the planned quantity.</summary>
        public CentsAmount ExpectedProfitCents { get; }

        /// <summary>One line per input that moved the plan, and one per input left out or skipped.</summary>
        public IReadOnlyList<string> Reasoning { get; }

        private static string MissingProfitInput(SupplierOption option)
        {
            if (!option.UnitPriceCents.IsKnown) return "unit price";
            if (!option.FreightCostCents.IsKnown) return "freight";
            if (!option.RevenuePerUnitCents.IsKnown) return "revenue";
            if (!option.QuantityUnits.IsKnown) return "quantity";
            return "a profit input";
        }

        /// <summary>
        /// Plans the replenishment for a recommended reserve against a
        /// supplier comparison, as of one day. Pure: the projection and
        /// the comparison are never mutated, and no authority is read or
        /// written. A null projection or comparison gives no selection
        /// with the gap named.
        /// </summary>
        public static ReplenishmentPlan Plan(
            ReserveProjection projection, SupplierComparisonResult comparison, int asOfDay)
        {
            var reasoning = new List<string>();
            if (projection == null)
            {
                reasoning.Add("no reserve projection was supplied — the plan cannot connect a reserve to a supplier.");
                return new ReplenishmentPlan(ReserveQuantity.Unknown, ReserveQuantity.Unknown,
                    false, string.Empty, default(SupplierOption), ReserveQuantity.Unknown,
                    CentsAmount.Unknown, ReserveQuantity.Unknown, CentsAmount.Unknown, reasoning);
            }
            if (!projection.RecommendedReserveUnits.IsKnown)
            {
                reasoning.Add("the recommended reserve is unknown — no plan can be made for an unknown shortfall.");
                foreach (string line in projection.Reasoning)
                {
                    reasoning.Add("projection: " + line);
                }
                return new ReplenishmentPlan(ReserveQuantity.Unknown, ReserveQuantity.Unknown,
                    false, string.Empty, default(SupplierOption), ReserveQuantity.Unknown,
                    CentsAmount.Unknown, ReserveQuantity.Unknown, CentsAmount.Unknown, reasoning);
            }
            if (comparison.Ranked == null || comparison.Ranked.Count == 0)
            {
                reasoning.Add($"the recommended reserve is {projection.RecommendedReserveUnits.Units} unit(s), but the comparison holds no options for '{comparison.ItemId}' — no supplier can be selected.");
                return new ReplenishmentPlan(projection.RecommendedReserveUnits, projection.RecommendedReserveUnits,
                    false, string.Empty, default(SupplierOption), ReserveQuantity.Unknown,
                    CentsAmount.Unknown, ReserveQuantity.Unknown, CentsAmount.Unknown, reasoning);
            }

            int shortfall = projection.RecommendedReserveUnits.Units;
            reasoning.Add($"recommended reserve: {shortfall} unit(s) — the Slice 6 projection already nets usable stock, incoming deliveries, and producible units, so the shortfall to cover is the reserve itself.");
            reasoning.Add($"shortfall to cover: {shortfall} unit(s) of '{comparison.ItemId}'.");

            RankedSupplierOption? selected = null;
            foreach (RankedSupplierOption entry in comparison.Ranked)
            {
                SupplierOption option = entry.Option;
                if (option.IsSelfCollect)
                {
                    reasoning.Add($"option '{option.SupplierId}' skipped: self-collect — the quote books no journey, so the goods would never arrive.");
                    continue;
                }
                if (!entry.IsComparableOnProfit)
                {
                    reasoning.Add($"option '{option.SupplierId}' skipped: incomparable on profit — the {MissingProfitInput(option)} is unknown, so it cannot be ranked.");
                    continue;
                }
                if (option.IsAffordableKnown && !option.IsAffordable)
                {
                    reasoning.Add($"option '{option.SupplierId}' (rank {entry.Rank}, expected profit {entry.ExpectedProfitCents.Cents}c) skipped: unaffordable — its {option.ExpectedTotalCostCents.Cents}c cost exceeds {option.AvailableCashCents.Cents}c available cash.");
                    continue;
                }
                selected = entry;
                break;
            }

            if (selected == null)
            {
                int cheapestKnown = int.MaxValue;
                string cheapestSupplier = string.Empty;
                foreach (RankedSupplierOption entry in comparison.Ranked)
                {
                    if (entry.Option.ExpectedTotalCostCents.IsKnown
                        && entry.Option.ExpectedTotalCostCents.Cents < cheapestKnown)
                    {
                        cheapestKnown = entry.Option.ExpectedTotalCostCents.Cents;
                        cheapestSupplier = entry.Option.SupplierId;
                    }
                }
                if (cheapestKnown != int.MaxValue)
                {
                    reasoning.Add($"no supplier selected: the cheapest quoted order is {cheapestKnown}c from '{cheapestSupplier}' and no comparable option is affordable.");
                }
                else
                {
                    reasoning.Add("no supplier selected: no comparable option is affordable and no quoted cost is known.");
                }
                return new ReplenishmentPlan(projection.RecommendedReserveUnits,
                    ReserveQuantity.Known(shortfall),
                    false, string.Empty, default(SupplierOption), ReserveQuantity.Unknown,
                    CentsAmount.Unknown, ReserveQuantity.Unknown, CentsAmount.Unknown, reasoning);
            }

            SupplierOption chosen = selected.Value.Option;
            reasoning.Add($"selected supplier '{chosen.SupplierId}' (rank {selected.Value.Rank}, expected profit {selected.Value.ExpectedProfitCents.Cents}c at the quoted quantity).");

            int cover = shortfall;
            if (chosen.QuantityAvailable.IsKnown)
            {
                cover = Math.Min(cover, chosen.QuantityAvailable.Units);
                if (cover < shortfall)
                {
                    reasoning.Add($"stated availability caps the order at {chosen.QuantityAvailable.Units} unit(s) — {shortfall - cover} unit(s) of the shortfall stay uncovered.");
                }
            }
            if (chosen.QuantityUnits.IsKnown)
            {
                int before = cover;
                cover = Math.Min(cover, chosen.QuantityUnits.Units);
                if (cover < shortfall && before == cover)
                {
                    reasoning.Add($"the option quotes {chosen.QuantityUnits.Units} unit(s) — {shortfall - cover} unit(s) of the shortfall stay uncovered.");
                }
            }
            if (cover <= 0)
            {
                reasoning.Add("the planned quantity is zero — no order is planned.");
                return new ReplenishmentPlan(projection.RecommendedReserveUnits,
                    ReserveQuantity.Known(shortfall),
                    false, string.Empty, default(SupplierOption), ReserveQuantity.Unknown,
                    CentsAmount.Unknown, ReserveQuantity.Unknown, CentsAmount.Unknown, reasoning);
            }
            reasoning.Add($"planned quantity: {cover} unit(s).");

            int goods = cover * chosen.UnitPriceCents.Cents;
            int freight = chosen.FreightCostCents.Cents;
            if (chosen.QuantityUnits.IsKnown && cover < chosen.QuantityUnits.Units)
            {
                reasoning.Add($"freight {freight}c was quoted for the {chosen.QuantityUnits.Units}-unit order; a smaller order may re-quote freight.");
            }
            int cost = goods + freight;
            reasoning.Add($"expected cost: {cover} x {chosen.UnitPriceCents.Cents}c goods ({goods}c) + {freight}c freight = {cost}c.");

            ReserveQuantity arrival = chosen.ExpectedArrivalDay(asOfDay);
            if (arrival.IsKnown)
            {
                reasoning.Add(chosen.QuotedArrivalDay.IsKnown
                    ? $"expected arrival day {arrival.Units} (quoted by the order)."
                    : $"expected arrival day {arrival.Units} (day {asOfDay} + {chosen.LeadTimeDays.Days}-day lead time).");
            }
            else
            {
                reasoning.Add("expected arrival unknown — no authored lead time was supplied and no arrival was quoted.");
            }

            int spoilageLoss = 0;
            if (chosen.ExpectedSpoilageUnits.IsKnown && cover == chosen.QuantityUnits.Units
                && chosen.ExpectedSpoilageLossCents.IsKnown)
            {
                spoilageLoss = chosen.ExpectedSpoilageLossCents.Cents;
                reasoning.Add($"expected spoilage loss: {chosen.ExpectedSpoilageUnits.Units} unit(s) x {chosen.RevenuePerUnitCents.Cents}c = {spoilageLoss}c.");
            }
            else if (chosen.ExpectedSpoilageUnits.IsKnown && cover != chosen.QuantityUnits.Units)
            {
                reasoning.Add($"expected spoilage excluded: the spoilage forecast covered the quoted {chosen.QuantityUnits.Units} unit(s), not the planned {cover} — the profit below excludes it.");
            }
            else
            {
                reasoning.Add("expected spoilage unknown — no spoilage forecast was supplied, so the profit below excludes unmeasured spoilage.");
            }

            int revenue = cover * chosen.RevenuePerUnitCents.Cents;
            int profit = revenue - goods - freight - spoilageLoss;
            reasoning.Add($"expected profit: {revenue}c revenue - {goods}c goods - {freight}c freight - {spoilageLoss}c spoilage = {profit}c ({chosen.RevenueBasis})");
            if (chosen.IsAffordableKnown)
            {
                reasoning.Add($"affordable: {cost}c against {chosen.AvailableCashCents.Cents}c available cash.");
            }
            else
            {
                reasoning.Add("affordability unknown — no available-cash figure was supplied, so affordability cannot be judged.");
            }
            return new ReplenishmentPlan(projection.RecommendedReserveUnits,
                ReserveQuantity.Known(shortfall),
                true, chosen.SupplierId, chosen, ReserveQuantity.Known(cover),
                CentsAmount.Known(cost), arrival, CentsAmount.Known(profit), reasoning);
        }
    }
}
