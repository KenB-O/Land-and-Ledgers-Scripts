// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice P5 — replenishment planning tests (directive P5).
// Pins: the plan connects a Slice 6 recommended reserve to the best
// supplier option for the shortfall and states expected cost, arrival,
// and profit; it selects the best affordable comparable option (skipping
// a dearer-profit option the business cannot afford and naming the skip);
// it selects nothing when no option is comparable, when the recommendation
// is Unknown, or when nothing is affordable; a self-collect option is
// never selected (the quote books no journey); quantity is capped by the
// quoted quantity and by any stated availability, with the uncovered
// remainder named; and the plan orders nothing and changes nothing in
// any live authority.

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Logistics;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class ReplenishmentPlanTests
    {
        private const int Day = 12;
        private const string FlourItem = "bakery_flour_sack";
        private const string MillbrookId = "millbrook_grain_co";
        private const int RevenuePerSackCents = 100;
        private const string RevenueBasis =
            "100c per sack = 20 loaves x 5c: the recorded bake converts 12 lb flour to 24 loaves, " +
            "so a 10 lb sack bakes 20 loaves, and the S6-1 sequence sells bread at 5c per loaf.";

        private static ReserveInputs BaseInputs(int demandPerDay = 10, double leadTimeDays = 2.0)
        {
            return new ReserveInputs
            {
                RecentDemandPerDay = ReserveQuantity.Known(demandPerDay),
                DemandUncertaintyUnits = ReserveQuantity.Unknown,
                ActualUsableStockUnits = ReserveQuantity.Known(0),
                ForecastSpoilageUnits = ReserveQuantity.Known(0),
                ExistingCommitmentsUnits = ReserveQuantity.Known(0),
                ConfirmedIncomingDeliveriesUnits = ReserveQuantity.Known(0),
                ProductionCapacityPerDay = ReserveQuantity.Unknown,
                ReplenishmentLeadTimeDays = leadTimeDays >= 0.0 ? LeadTimeDays.Known(leadTimeDays) : LeadTimeDays.Unknown,
                AvailableStorageUnits = ReserveQuantity.Unknown,
            };
        }

        private static SupplierOption SampleOption(
            string supplierId = MillbrookId,
            int quantityUnits = 16,
            int unitPriceCents = 48,
            int freightCents = 125,
            int revenuePerUnitCents = RevenuePerSackCents,
            int availableCashCents = 20000,
            bool selfCollect = false)
        {
            return new SupplierOption(
                supplierId: supplierId,
                itemId: FlourItem,
                quantityUnits: ReserveQuantity.Known(quantityUnits),
                quantityAvailable: ReserveQuantity.Unknown,
                unitPriceCents: CentsAmount.Known(unitPriceCents),
                unitPriceSource: "test option: caller-supplied price, source named per test",
                freightCostCents: CentsAmount.Known(freightCents),
                freightSource: "test option: caller-supplied freight, source named per test",
                consolidatedShipmentQuoted: false,
                consolidatedShipment: false,
                leadTimeDays: LeadTimeDays.Known(2.0),
                quotedArrivalDay: ReserveQuantity.Unknown,
                reliability: SupplierReliability.Unknown,
                storageFit: SupplierStorageFit.Unknown("test option: no storage check supplied"),
                expectedSpoilageUnits: ReserveQuantity.Unknown,
                revenuePerUnitCents: CentsAmount.Known(revenuePerUnitCents),
                revenueBasis: RevenueBasis,
                availableCashCents: availableCashCents >= 0 ? CentsAmount.Known(availableCashCents) : CentsAmount.Unknown,
                isSelfCollect: selfCollect);
        }

        private static SupplierOption WithAvailable(SupplierOption option, int availableUnits)
        {
            return new SupplierOption(
                option.SupplierId, option.ItemId, option.QuantityUnits,
                ReserveQuantity.Known(availableUnits),
                option.UnitPriceCents, option.UnitPriceSource, option.FreightCostCents, option.FreightSource,
                option.ConsolidatedShipmentQuoted, option.ConsolidatedShipment, option.LeadTimeDays,
                option.QuotedArrivalDay, option.Reliability, option.StorageFit, option.ExpectedSpoilageUnits,
                option.RevenuePerUnitCents, option.RevenueBasis, option.AvailableCashCents,
                option.IsSelfCollect);
        }

        private static SupplierComparisonResult Compare(params SupplierOption[] options) =>
            SupplierComparison.Compare(FlourItem, options, Day);

        private static string Reasoning(ReplenishmentPlan plan) =>
            string.Join(" | ", plan.Reasoning);

        private static OffMapAidBusinessDefinition MillbrookDefinition()
        {
            var definition = ScriptableObject.CreateInstance<OffMapAidBusinessDefinition>();
            definition.BusinessId = "millbrook_grain_co";
            definition.DisplayName = "Millbrook Grain Co.";
            definition.PickupRouteMilesFromTown = 50f;
            definition.Inventory = new List<OffMapAidInventoryRow>
            {
                new OffMapAidInventoryRow
                {
                    ItemId = "bakery_flour_sack", DisplayName = "Flour",
                    InventoryUnit = "lb", StartingUnits = 230,
                    OrderUnit = "10 lb sack", InventoryUnitsPerOrderUnit = 10,
                    BakeryUnitsPerOrderUnit = 18, OrderPriceCents = 48,
                },
            };
            return definition;
        }

        [Test]
        public void Plan_ConnectsReserveShortfallToBestOption_StatesCostArrivalProfit()
        {
            ReserveProjection projection = ReserveProjection.Compute(BaseInputs());
            Assert.AreEqual(20, projection.RecommendedReserveUnits.Units,
                "10/day over a 2-day lead time is a 20-unit reserve (Slice 6 arithmetic)");
            SupplierComparisonResult comparison = Compare(SampleOption());
            ReplenishmentPlan plan = ReplenishmentPlan.Plan(projection, comparison, Day);

            Assert.IsTrue(plan.HasSelection, "the plan selects an option: " + Reasoning(plan));
            Assert.AreEqual(MillbrookId, plan.SelectedSupplierId);
            Assert.AreEqual(20, plan.RecommendedReserve.Units, "the plan starts from the recommended reserve");
            Assert.AreEqual(20, plan.Shortfall.Units, "the projection already nets stock, so the shortfall is the reserve");
            Assert.AreEqual(16, plan.Quantity.Units,
                "the best option quotes 16 units, so the plan covers 16 of the 20-unit shortfall");
            Assert.IsTrue(plan.ExpectedCostCents.IsKnown);
            Assert.AreEqual(893, plan.ExpectedCostCents.Cents, "16 x 48c goods + 125c freight");
            Assert.IsTrue(plan.ExpectedArrivalDay.IsKnown);
            Assert.AreEqual(14, plan.ExpectedArrivalDay.Units, "day 12 + 2-day caller-supplied lead time");
            Assert.IsTrue(plan.ExpectedProfitCents.IsKnown);
            Assert.AreEqual(707, plan.ExpectedProfitCents.Cents, "16 x 100c revenue - 893c cost");
            string reasoning = Reasoning(plan);
            Assert.IsTrue(reasoning.Contains("20"), "the reasoning names the reserve: " + reasoning);
            Assert.IsTrue(reasoning.Contains("4"), "the reasoning names the 4 uncovered units: " + reasoning);
            Assert.IsTrue(reasoning.Contains("893"), "the reasoning names the expected cost: " + reasoning);
            Assert.IsTrue(reasoning.Contains("707"), "the reasoning names the expected profit: " + reasoning);
        }

        [Test]
        public void Plan_SelectsBestAffordable_WhenBestIsUnaffordable()
        {
            // Best by profit (707c) costs 893c — more than the 500c on hand.
            SupplierOption best = SampleOption(quantityUnits: 16, unitPriceCents: 48, freightCents: 125,
                availableCashCents: 500);
            // Lower profit (550c) but affordable: 8 x 25c + 50c freight = 250c.
            SupplierOption affordable = SampleOption(supplierId: "cheap-small-supplier",
                quantityUnits: 8, unitPriceCents: 25, freightCents: 50, availableCashCents: 500);
            ReserveProjection projection = ReserveProjection.Compute(BaseInputs());
            ReplenishmentPlan plan = ReplenishmentPlan.Plan(
                projection, Compare(best, affordable), Day);

            Assert.IsTrue(plan.HasSelection);
            Assert.AreEqual("cheap-small-supplier", plan.SelectedSupplierId,
                "the best option is unaffordable, so the plan takes the best affordable one");
            Assert.AreEqual(8, plan.Quantity.Units);
            Assert.AreEqual(250, plan.ExpectedCostCents.Cents);
            Assert.AreEqual(550, plan.ExpectedProfitCents.Cents, "8 x 100c - 250c");
            Assert.IsTrue(Reasoning(plan).Contains("cannot afford") || Reasoning(plan).Contains("unaffordable"),
                "the skip names the affordability: " + Reasoning(plan));
        }

        [Test]
        public void Plan_NoAffordableOption_NoSelection()
        {
            SupplierOption option = SampleOption(availableCashCents: 100);
            ReserveProjection projection = ReserveProjection.Compute(BaseInputs());
            ReplenishmentPlan plan = ReplenishmentPlan.Plan(projection, Compare(option), Day);

            Assert.IsTrue(!plan.HasSelection, "nothing affordable, nothing selected");
            Assert.IsTrue(string.IsNullOrEmpty(plan.SelectedSupplierId));
            Assert.IsTrue(!plan.ExpectedCostCents.IsKnown);
            Assert.IsTrue(Reasoning(plan).Contains("100") && Reasoning(plan).Contains("893"),
                "the reason names the cash on hand and the cheapest cost: " + Reasoning(plan));
        }

        [Test]
        public void Plan_UnknownRecommendation_NoSelection()
        {
            ReserveProjection projection = ReserveProjection.Compute(BaseInputs(leadTimeDays: -1.0));
            Assert.IsTrue(!projection.RecommendedReserveUnits.IsKnown,
                "no lead time, no recommendation (Slice 6 rule)");
            ReplenishmentPlan plan = ReplenishmentPlan.Plan(projection, Compare(SampleOption()), Day);

            Assert.IsTrue(!plan.HasSelection);
            Assert.IsTrue(Reasoning(plan).Contains("lead time"),
                "the reason names the missing lead time: " + Reasoning(plan));
        }

        [Test]
        public void Plan_NoComparableOptions_NoSelection()
        {
            var noPrice = new SupplierOption(
                MillbrookId, FlourItem, ReserveQuantity.Known(16), ReserveQuantity.Unknown,
                CentsAmount.Unknown, "test option: no price quoted",
                CentsAmount.Known(125), "test freight",
                false, false, LeadTimeDays.Known(2.0), ReserveQuantity.Unknown,
                SupplierReliability.Unknown,
                SupplierStorageFit.Unknown("test option: no storage check supplied"),
                ReserveQuantity.Unknown, CentsAmount.Known(RevenuePerSackCents), RevenueBasis,
                CentsAmount.Known(20000), false);
            ReserveProjection projection = ReserveProjection.Compute(BaseInputs());
            ReplenishmentPlan plan = ReplenishmentPlan.Plan(projection, Compare(noPrice), Day);

            Assert.IsTrue(!plan.HasSelection, "no option is comparable on profit, so there is nothing to select");
            Assert.IsTrue(Reasoning(plan).Contains("unit price"),
                "the reason names the missing input: " + Reasoning(plan));
        }

        [Test]
        public void Plan_UnknownCash_SelectsBestWithAffordabilityUnknown()
        {
            SupplierOption option = SampleOption(availableCashCents: -1);
            ReserveProjection projection = ReserveProjection.Compute(BaseInputs());
            ReplenishmentPlan plan = ReplenishmentPlan.Plan(projection, Compare(option), Day);

            Assert.IsTrue(plan.HasSelection, "with no cash figure the plan still selects the best option");
            Assert.AreEqual(MillbrookId, plan.SelectedSupplierId);
            Assert.IsTrue(Reasoning(plan).Contains("affordability unknown"),
                "but the affordability is named Unknown, never assumed: " + Reasoning(plan));
        }

        [Test]
        public void Plan_QuantityLimitedByStatedAvailability()
        {
            var order = new NamedSkuOrder
            {
                OrderId = "named-order-1",
                BusinessId = "general-store-1",
                SkuId = FlourItem,
                UnitsOrdered = 20,
                UnitGoodsCostCents = 48,
                FreightSurchargeCents = 125,
                TotalCostCents = 1085,
                SupplierId = "named-flour-co",
                SupplierName = "Named Flour Co.",
                OrderDayIndex = 12,
                ExpectedArrivalDayIndex = 14,
            };
            SupplierOption named = SupplierOption.FromNamedSkuOrder(
                order, LeadTimeDays.Unknown, SupplierReliability.Unknown,
                SupplierStorageFit.Unknown("test option: no storage check supplied"),
                ReserveQuantity.Known(0), CentsAmount.Known(RevenuePerSackCents), RevenueBasis,
                CentsAmount.Known(20000));
            named = WithAvailable(named, 10);
            ReserveProjection projection = ReserveProjection.Compute(BaseInputs());
            ReplenishmentPlan plan = ReplenishmentPlan.Plan(projection, Compare(named), Day);

            Assert.IsTrue(plan.HasSelection);
            Assert.AreEqual(10, plan.Quantity.Units,
                "the stated availability of 10 caps the plan even though the order quotes 20");
            Assert.AreEqual(605, plan.ExpectedCostCents.Cents, "10 x 48c + 125c freight (quoted for the order)");
            Assert.AreEqual(14, plan.ExpectedArrivalDay.Units,
                "the arrival is the order's own quoted arrival day, not a lead-time computation");
            Assert.IsTrue(Reasoning(plan).Contains("10"), "the cap is named: " + Reasoning(plan));
        }

        [Test]
        public void Plan_ArrivalFromQuotedOrderDay_NotLeadTime()
        {
            var order = new NamedSkuOrder
            {
                OrderId = "named-order-2",
                BusinessId = "general-store-1",
                SkuId = FlourItem,
                UnitsOrdered = 16,
                UnitGoodsCostCents = 48,
                FreightSurchargeCents = 125,
                TotalCostCents = 893,
                SupplierId = "named-flour-co",
                SupplierName = "Named Flour Co.",
                OrderDayIndex = 12,
                ExpectedArrivalDayIndex = 19,
            };
            SupplierOption named = SupplierOption.FromNamedSkuOrder(
                order, LeadTimeDays.Unknown, SupplierReliability.Unknown,
                SupplierStorageFit.Unknown("test option: no storage check supplied"),
                ReserveQuantity.Known(0), CentsAmount.Known(RevenuePerSackCents), RevenueBasis,
                CentsAmount.Known(20000));
            ReserveProjection projection = ReserveProjection.Compute(BaseInputs());
            ReplenishmentPlan plan = ReplenishmentPlan.Plan(projection, Compare(named), Day);

            Assert.IsTrue(plan.HasSelection);
            Assert.AreEqual(19, plan.ExpectedArrivalDay.Units,
                "with no lead time supplied, the arrival is the order's quoted day 19");
        }

        [Test]
        public void Plan_SelfCollectOption_NeverSelected()
        {
            SupplierOption selfCollect = SampleOption(supplierId: "self-collect-supplier",
                unitPriceCents: 40, freightCents: 0, selfCollect: true);
            SupplierOption millbrook = SampleOption();
            Assert.IsTrue(selfCollect.ExpectedProfitCents.Cents > millbrook.ExpectedProfitCents.Cents,
                "self-collect looks cheapest on paper (zero quoted freight)");
            ReserveProjection projection = ReserveProjection.Compute(BaseInputs());
            ReplenishmentPlan plan = ReplenishmentPlan.Plan(
                projection, Compare(selfCollect, millbrook), Day);

            Assert.IsTrue(plan.HasSelection);
            Assert.AreEqual(MillbrookId, plan.SelectedSupplierId,
                "self-collect is never selected: the quote books no journey, so the goods would never arrive");
            Assert.IsTrue(Reasoning(plan).Contains("journey"),
                "the skip names the missing journey: " + Reasoning(plan));
        }

        [Test]
        public void Plan_OrdersNothing_ChangesNothing()
        {
            var supplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            int cashBefore = supplier.CashCents;
            int stockBefore = supplier.GetInventoryUnits(FlourItem);
            int lotsBefore = supplier.Lots.Count;
            CentsAmount freight = SupplierOption.FreightFromShipment(
                new LogisticsShipmentState { freightChargeCents = 125 }, out string freightReason);
            SupplierOption millbrook = SupplierOption.FromMillbrook(
                supplier, FlourItem, ReserveQuantity.Known(16), freight, freightReason,
                LeadTimeDays.Known(2.0), SupplierReliability.Unknown,
                SupplierStorageFit.Unknown("test option: no storage check supplied"),
                ReserveQuantity.Unknown, CentsAmount.Known(RevenuePerSackCents), RevenueBasis,
                CentsAmount.Known(20000));
            ReserveProjection projection = ReserveProjection.Compute(BaseInputs());
            SupplierComparisonResult comparison = Compare(millbrook);
            ReplenishmentPlan first = ReplenishmentPlan.Plan(projection, comparison, Day);
            ReplenishmentPlan second = ReplenishmentPlan.Plan(projection, comparison, Day);

            Assert.IsTrue(first.HasSelection);
            Assert.AreEqual(first.SelectedSupplierId, second.SelectedSupplierId);
            Assert.AreEqual(first.Quantity.Units, second.Quantity.Units);
            Assert.AreEqual(first.ExpectedCostCents.Cents, second.ExpectedCostCents.Cents);
            Assert.AreEqual(first.ExpectedProfitCents.Cents, second.ExpectedProfitCents.Cents);
            Assert.AreEqual(cashBefore, supplier.CashCents, "the plan moves no supplier cash");
            Assert.AreEqual(stockBefore, supplier.GetInventoryUnits(FlourItem), "the plan moves no supplier stock");
            Assert.AreEqual(lotsBefore, supplier.Lots.Count, "the plan creates no lot");
            Assert.IsTrue(!supplier.HasOrder("plan-order"), "the plan places no order");
        }

        [Test]
        public void Plan_ReasoningNamesEveryInput()
        {
            ReserveProjection projection = ReserveProjection.Compute(BaseInputs());
            ReplenishmentPlan plan = ReplenishmentPlan.Plan(projection, Compare(SampleOption()), Day);
            string reasoning = Reasoning(plan);
            Assert.IsTrue(reasoning.Contains("reserve"), "names the recommended reserve: " + reasoning);
            Assert.IsTrue(reasoning.Contains(MillbrookId), "names the selected supplier: " + reasoning);
            Assert.IsTrue(reasoning.Contains("16"), "names the quantity: " + reasoning);
            Assert.IsTrue(reasoning.Contains("893"), "names the expected cost: " + reasoning);
            Assert.IsTrue(reasoning.Contains("14"), "names the expected arrival: " + reasoning);
            Assert.IsTrue(reasoning.Contains("707"), "names the expected profit: " + reasoning);
            Assert.IsTrue(reasoning.Contains("20000") || reasoning.Contains("cash"),
                "names the available cash: " + reasoning);
        }

        [Test]
        public void Plan_FullCover_NoRemainingShortfall()
        {
            // A 10/day demand over 2 days is 20; the option quotes 20, so the shortfall is fully covered.
            ReserveProjection projection = ReserveProjection.Compute(BaseInputs());
            SupplierOption option = SampleOption(quantityUnits: 20);
            ReplenishmentPlan plan = ReplenishmentPlan.Plan(projection, Compare(option), Day);

            Assert.IsTrue(plan.HasSelection);
            Assert.AreEqual(20, plan.Quantity.Units);
            Assert.AreEqual(1085, plan.ExpectedCostCents.Cents, "20 x 48c + 125c");
            Assert.AreEqual(915, plan.ExpectedProfitCents.Cents, "20 x 100c - 1085c");
            Assert.IsTrue(!Reasoning(plan).Contains("uncovered"),
                "a full cover names no uncovered remainder: " + Reasoning(plan));
        }
    }
}
