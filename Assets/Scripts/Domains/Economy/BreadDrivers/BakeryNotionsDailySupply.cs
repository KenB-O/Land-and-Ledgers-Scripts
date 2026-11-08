using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.BreadDrivers
{
    /// <summary>Caller inputs for one day of bakery notions supply.</summary>
    public sealed class BakeryNotionsDailySupplyInputs
    {
        public Series2NotionsSupply NotionsSupply;
        public BusinessRuntimeState StoreRuntime;
        public BusinessRuntimeState BakeryRuntime;
        public BakeryShopRuntime Bakery;
        public EntityIdRegistry IdRegistry;
        /// <summary>Caller-supplied vendor/SKU/price for the notions order.
        /// Production builds this from the ImportCatalog figures (10c/unit,
        /// 14 transit days, railhead origin) — never hardcoded here.</summary>
        public Series2CounterpartyRegistration Registration;
        public string StoreBusinessId = string.Empty;
        public string BakeryBusinessId = string.Empty;
        public int BatchesToday;
        public int DayIndex;
        /// <summary>Priority 3 Slice 4: caller-supplied pipeline depth
        /// in days — how many future days of demand the pipeline should
        /// cover beyond today. 0 = today's behaviour exactly (order
        // today's post-feed shortfall); the production default is 0
        /// until the owner names the policy (B-1). A parameter, not a
        /// value: no depth is invented here.</summary>
        public int PipelineDepthDays;
    }

    /// <summary>Outcome of one day of bakery notions supply.</summary>
    public sealed class BakeryNotionsDailySupplyResult
    {
        public bool SupplyWired;
        public string Refusal = string.Empty;
        /// <summary>Day's need in notion units (batches x 1).</summary>
        public int NotionsNeeded;
        public int NotionsOnHandBefore;
        /// <summary>Bin units fed from arrived store lots today.</summary>
        public int NotionsFed;
        /// <summary>Units ordered today for the pipeline (arrive later).</summary>
        public int NotionsOrdered;
        /// <summary>Arrival day of today's order, or 0 when none was placed.</summary>
        public int NotionsArrivalDayIndex;
    }

    /// <summary>
    /// Plan 04 (2026-11-06, N-slice): the bakery's daily notions step —
    /// buy only the day's need (1 notion per batch), through the
    /// designed procurement path, before Precheck runs. Plain C#, no
    /// Unity dependency; the daily driver calls this, the forwarder
    /// supplies the live service + catalog-derived registration.
    ///
    /// Order, per day: (a) receive due arrivals into the store service
    /// lots (the store pays the market on arrival, CashOnArrival);
    /// (b) feed the day's shortfall from sellable store lots into the
    /// bin at the catalog price, 1 store unit = 1 bin unit (notions
    /// are unit goods — the catalog prices them per unit — so the
    /// conversion is 1, stated in diagnostics on every feed by
    /// FeedNotionsToBakery); (c) order the remaining shortfall for the
    /// pipeline. An order placed today arrives after the authored
    /// transit (14 days in production), so it serves a future day's
    /// bake, never today's: the first bake after wiring still refuses
    /// on notions until the pipeline primes, and that refusal is the
    /// honest authored transit, not a defect.
    ///
    /// Pipeline accounting: the order is the day's shortfall AFTER
    /// feeding — on-hand + sellable, NOT minus in-transit. In-transit
    /// units are earmarked for the future days whose shortfalls ordered
    /// them; subtracting them would order one notion per transit
    /// window instead of one per bake (a bake every 14 days, not a
    /// daily bake). The duplicate guard is per-day, not per-pipeline:
    /// at most one notions order per day (an order for this SKU placed
    /// today already covers today's shortfall), so a repeated same-day
    /// call orders and feeds nothing twice.
    ///
    /// Priority 3 Slice 4 — pipeline depth (mechanism only; live
    /// activation BLOCKED on B-1). Caller-supplied
    /// PipelineDepthDays (D) names how many future days of demand the
    /// pipeline should cover beyond today; the production default is
    /// 0. FORMULA CORRECTION: the plan's literal formula gives zero
    /// orders at depth 0, which contradicts the depth-0 expectation
    /// that depth 0 reproduces today's cycle exactly — so depth 0 IS
    /// today's behaviour (order today's post-feed shortfall,
    /// unchanged), and depth D &gt; 0 orders up to a total target of
    /// (D + 1) x one day's need — today plus D future days — net of
    /// bin on-hand after feeding, sellable store lots remaining, and
    /// in-transit units arriving within the depth window. The per-day
    /// duplicate guard, the finite counterparty, and cash all still
    /// bound the order. The depth is a parameter, not a value.
    ///
    /// No invention: the vendor, SKU, price, and transit are
    /// caller-supplied (production: the catalog's authored terms via
    /// the forwarder; the market's registered counterparty governs the
    /// actual arrival). A missing service, registration, or identity
    /// refuses loudly and orders/feeds nothing — never a second supply
    /// path, never conjured notions.
    /// </summary>
    public static class BakeryNotionsDailySupply
    {
        /// <summary>Production notions vendor, from the catalog's authored
        /// origin "Off-map provision wholesaler, via railhead"
        /// (BakeryFlourSupply.EnsureBakeryImportables). No named business
        /// exists for notions, so the id is derived from that origin
        /// string — it is an identifier, not a rate or quantity.</summary>
        public const string NotionsSupplierId = "off-map-provision-wholesaler";
        public const string NotionsSupplierName = "Off-map provision wholesaler (via railhead)";

        /// <summary>One store unit is one bin notion: notions are priced
        /// per unit in the catalog, and the SKU is a single unit, not a
        /// sack or package.</summary>
        public const int NotionsUnitsPerStoreUnit = 1;

        /// <summary>Finite external order registered for the production
        /// counterparty (bootstrapper, feed-precedent shape): 30 units —
        /// the authored bootstrap endowment size
        /// (BakeryFlourBootstrap.BootstrapNotionsUnits), NOT canon. When
        /// it is exhausted the market refuses and the driver says so
        /// loudly; a standing wholesale supply term is owed
        /// (D-1-adjacent).</summary>
        public const int FiniteExternalOrderUnits = 30;

        public static BakeryNotionsDailySupplyResult RunDay(
            BakeryNotionsDailySupplyInputs inputs, List<string> diag)
        {
            diag = diag ?? new List<string>();
            var result = new BakeryNotionsDailySupplyResult();

            if (inputs == null || inputs.Bakery == null || inputs.NotionsSupply == null
                || inputs.Registration == null || inputs.StoreRuntime == null
                || inputs.BakeryRuntime == null || inputs.IdRegistry == null)
            {
                result.Refusal = "BakeryNotionsDailySupply: notions supply unwired — the procurement service, catalog registration, store/bakery runtimes, or id registry is missing; no notions ordered or fed (F19: the live PassXlProcurementService assignment is owed).";
                diag.Add(result.Refusal);
                return result;
            }

            OffMapNamedSkuProcurementService service =
                inputs.NotionsSupply.Procurement != null ? inputs.NotionsSupply.Procurement.Service : null;
            if (service == null)
            {
                result.Refusal = "BakeryNotionsDailySupply: notions supply unwired — the procurement driver holds no service; no notions ordered or fed (F19: the live PassXlProcurementService assignment is owed).";
                diag.Add(result.Refusal);
                return result;
            }
            if (!string.IsNullOrWhiteSpace(inputs.StoreBusinessId)
                && !string.Equals(service.BusinessId, inputs.StoreBusinessId, StringComparison.Ordinal))
            {
                result.Refusal = $"BakeryNotionsDailySupply: the procurement service belongs to '{service.BusinessId}', not the store '{inputs.StoreBusinessId}'; no notions ordered or fed.";
                diag.Add(result.Refusal);
                return result;
            }

            result.SupplyWired = true;
            if (inputs.BatchesToday <= 0)
            {
                diag.Add("BakeryNotionsDailySupply: no batches planned today; no notions ordered or fed.");
                return result;
            }

            // (a) Receive due arrivals first — the store pays the market
            // on arrival (CashOnArrival), so the lots exist before the feed.
            inputs.NotionsSupply.ReceiveArrivals(inputs.DayIndex, null, diag);

            int need = inputs.BatchesToday * BakeryBreadCatalog.StandardNotionsPerBatch;
            int onHand = inputs.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId);
            result.NotionsNeeded = need;
            result.NotionsOnHandBefore = onHand;
            int shortfall = need - onHand;
            int depthDays = Math.Max(0, inputs.PipelineDepthDays);
            if (shortfall <= 0 && depthDays <= 0)
            {
                diag.Add($"BakeryNotionsDailySupply: notions sufficient — {onHand}u on hand for {inputs.BatchesToday} batch(es); no order, no feed.");
                return result;
            }

            string skuId = inputs.Registration.SkuId;
            int unitPrice = inputs.Registration.UnitGoodsCostCents;

            // (b) Feed the shortfall from sellable store lots.
            int sellable = inputs.NotionsSupply.Procurement.SellableUnits(skuId);
            int toFeed = Math.Min(shortfall, sellable);
            if (toFeed > 0)
            {
                string feedProblem = inputs.NotionsSupply.FeedNotionsToBakery(
                    service, inputs.StoreRuntime, inputs.Bakery, inputs.BakeryRuntime, inputs.IdRegistry,
                    skuId, toFeed, NotionsUnitsPerStoreUnit, unitPrice, inputs.DayIndex, diag);
                if (feedProblem == null)
                {
                    result.NotionsFed = toFeed;
                    shortfall -= toFeed;
                }
                else
                {
                    diag.Add($"BakeryNotionsDailySupply: notions feed refused — {feedProblem}");
                }
            }

            // (c) Order for the pipeline. At depth 0 this is today's
            // behaviour exactly: order the remaining shortfall after
            // feeding. At depth D > 0 order up to the forward target
            // (D + 1) x one day's need, net of bin on-hand after
            // feeding, sellable store lots remaining, and in-transit
            // units arriving within the depth window. One order per
            // day: an order for this SKU placed today already covers
            // today's shortfall, so a repeated same-day call orders
            // nothing twice.
            int orderQty = shortfall;
            int forwardTarget = 0;
            int inTransitWithinDepth = 0;
            if (depthDays > 0)
            {
                forwardTarget = (depthDays + 1) * need;
                inTransitWithinDepth = InTransitNotionsWithinDepth(inputs, skuId, depthDays);
                int onHandAfterFeed = onHand + result.NotionsFed;
                int sellableRemaining = Math.Max(0, sellable - result.NotionsFed);
                orderQty = Math.Max(0, forwardTarget - onHandAfterFeed - sellableRemaining - inTransitWithinDepth);
            }
            if (orderQty > 0)
            {
                bool orderedToday = false;
                if (service.Orders != null)
                {
                    foreach (NamedSkuOrder existing in service.Orders)
                    {
                        if (existing == null) continue;
                        if (!string.Equals(existing.SkuId, skuId, StringComparison.Ordinal)) continue;
                        if (existing.OrderDayIndex != inputs.DayIndex) continue;
                        if (existing.Stage == NamedSkuOrderStage.Received
                            || existing.Stage == NamedSkuOrderStage.Cancelled) continue;
                        orderedToday = true;
                        break;
                    }
                }
                if (orderedToday)
                {
                    diag.Add($"BakeryNotionsDailySupply: a notions order for '{skuId}' was already placed today (day {inputs.DayIndex}); no second order.");
                }
                else
                {
                    NamedSkuOrder order = inputs.NotionsSupply.PlaceNotionsOrder(
                        inputs.Registration, orderQty, inputs.DayIndex,
                        NamedSkuPaymentMode.CashOnArrival, null, diag);
                    if (order != null)
                    {
                        result.NotionsOrdered = orderQty;
                        result.NotionsArrivalDayIndex = order.ExpectedArrivalDayIndex;
                    }
                    else
                    {
                        result.Refusal = "BakeryNotionsDailySupply: notions order refused — see diagnostics (unknown counterparty, exhausted finite supply, or service refusal); the bake will refuse at Precheck on notions until an order lands.";
                        diag.Add(result.Refusal);
                    }
                }
            }

            diag.Add($"BakeryNotionsDailySupply: notions quote {unitPrice}c/unit via {inputs.Registration.SupplierId} ('{skuId}'), quoted transit {Math.Max(1, inputs.Registration.TransitDays)}d; " +
                $"day {inputs.DayIndex}: need {need}u, on hand {onHand}u, sellable {sellable}u, fed {result.NotionsFed}u, ordered {result.NotionsOrdered}u" +
                (result.NotionsOrdered > 0 ? $" (arrives day {result.NotionsArrivalDayIndex})" : string.Empty) +
                (depthDays > 0 ? $" [pipeline depth {depthDays}d, forward target {forwardTarget}u, in transit within depth {inTransitWithinDepth}u]" : string.Empty) + ".");
            return result;
        }

        /// <summary>Priority 3 Slice 4: unreceived units of this SKU
        /// arriving after today and no later than
        /// DayIndex + depthDays. Units arriving after the depth window
        /// do not cover the window and are not counted; units already
        /// received are in the store lots (sellable) or the bin
        /// (on-hand), never double-counted here.</summary>
        private static int InTransitNotionsWithinDepth(BakeryNotionsDailySupplyInputs inputs, string skuId, int depthDays)
        {
            int total = 0;
            OffMapNamedSkuProcurementService service =
                inputs.NotionsSupply.Procurement != null ? inputs.NotionsSupply.Procurement.Service : null;
            if (service == null || service.Orders == null) return 0;
            foreach (NamedSkuOrder existing in service.Orders)
            {
                if (existing == null) continue;
                if (!string.Equals(existing.SkuId, skuId, StringComparison.Ordinal)) continue;
                if (existing.Stage == NamedSkuOrderStage.Received
                    || existing.Stage == NamedSkuOrderStage.Cancelled) continue;
                if (existing.ExpectedArrivalDayIndex <= inputs.DayIndex) continue;
                if (existing.ExpectedArrivalDayIndex > inputs.DayIndex + depthDays) continue;
                total += Math.Max(0, existing.UnitsOrdered);
            }
            return total;
        }
    }
}
