using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Trade;
using LandLedgers.Economy.Wheelwright;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// W6b: the wagon repair economy — parts + labor pricing, turnaround
    /// estimation, queue prioritization policy, draft-power wear generating
    /// repair demand, freight operators as customers, blacksmith ironwork as
    /// the upstream input. Rates and thresholds are calibration (Canon Part XV).
    /// </summary>
    [TestFixture]
    public sealed class WagonRepairEconomyTests
    {
        private static WheelwrightRuntime NewShop()
        {
            var shop = new WheelwrightRuntime("wheel-1", "Test Wheelwright", true);
            shop.ReceiveLumberLot(new ImportLot
            {
                LotId = "IMP-TEST-L1", MaterialId = ImportCatalog.LumberId,
                MaterialName = "Lumber (boards)", Units = 200,
                OriginName = "Local sawmill", OrderId = "IMP-ORD-9",
            }, new List<string>());
            return shop;
        }

        private static FreightResourcePool NewPool()
        {
            var pool = new FreightResourcePool();
            pool.AddWagon(new FreightWagon("WG-1", "Freight wagon 1", 600));
            pool.AddWagon(new FreightWagon("WG-2", "Freight wagon 2", 600));
            pool.AddWagon(new FreightWagon("WG-3", "Freight wagon 3", 600));
            return pool;
        }

        [Test]
        public void Pricing_PartsPlusLabor_MatchesHandCalc()
        {
            // 8 lumber x 50c x 1.20 markup = 480c parts; 240 min = 4 hr x 25c = 100c labor.
            Assert.AreEqual(580, WheelwrightPricing.PricePartsPlusLabor(8, 50, 240));
        }

        [Test]
        public void Pricing_LaborBillsStartedHour()
        {
            // 61 minutes bills 2 hours.
            Assert.AreEqual(50, WheelwrightPricing.PricePartsPlusLabor(0, 0, 61));
        }

        [Test]
        public void Pricing_QuoteFromOrder_UsesOrderFields()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            RepairWorkOrder order = shop.Repairs.Intake("A", "a-1", "WG-1", "Wagon",
                "rim split", WheelwrightRuntime.WheelwrightCapabilityCode,
                "yard", RepairUrgency.Routine, 10, diagnostics);
            Assert.IsNull(shop.Repairs.Diagnose(order.WorkOrderId, "rim split, spokes loose",
                8, ImportCatalog.LumberId, 240));

            int ask = WheelwrightPricing.QuoteFromOrder(order, 50);
            Assert.AreEqual(580, ask, "shop's ask: parts 480c + labor 100c");

            Assert.IsNull(shop.Repairs.AgreeTerms(order.WorkOrderId, ask));
            Assert.AreEqual(580, order.AgreedPriceCents);
        }

        [Test]
        public void Turnaround_EstimatesCompletion_FromQueueAhead()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            var queue = shop.Repairs;

            // Two orders ahead, 480 min each; mine needs 240 min; 480 min/day shop.
            RepairWorkOrder first = queue.Intake("A", "a-1", "WG-1", "Wagon 1", "x",
                WheelwrightRuntime.WheelwrightCapabilityCode, "yard", RepairUrgency.Routine, 10, diagnostics);
            Assert.IsNull(queue.Diagnose(first.WorkOrderId, "x", 0, string.Empty, 480));
            RepairWorkOrder second = queue.Intake("B", "b-1", "WG-2", "Wagon 2", "x",
                WheelwrightRuntime.WheelwrightCapabilityCode, "yard", RepairUrgency.Routine, 10, diagnostics);
            Assert.IsNull(queue.Diagnose(second.WorkOrderId, "x", 0, string.Empty, 480));
            RepairWorkOrder mine = queue.Intake("C", "c-1", "WG-3", "Wagon 3", "x",
                WheelwrightRuntime.WheelwrightCapabilityCode, "yard", RepairUrgency.Routine, 10, diagnostics);
            Assert.IsNull(queue.Diagnose(mine.WorkOrderId, "x", 0, string.Empty, 240));

            // Ahead: 960 min = 2 days; mine: 240 min = 1 day; day 10 -> done day 12.
            Assert.AreEqual(12, TurnaroundEstimator.EstimateCompletionDay(mine, queue, 10));
            // First in line with nothing ahead: done same day.
            Assert.AreEqual(10, TurnaroundEstimator.EstimateCompletionDay(first, queue, 10));
        }

        [Test]
        public void Turnaround_IgnoresClosedOrders()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            var queue = shop.Repairs;

            RepairWorkOrder done = queue.Intake("A", "a-1", "WG-1", "Wagon 1", "x",
                WheelwrightRuntime.WheelwrightCapabilityCode, "yard", RepairUrgency.Routine, 10, diagnostics);
            Assert.IsNull(queue.Diagnose(done.WorkOrderId, "x", 0, string.Empty, 480));
            done.Status = RepairOrderStatus.Complete; // finished yesterday, off the bench

            RepairWorkOrder mine = queue.Intake("B", "b-1", "WG-2", "Wagon 2", "x",
                WheelwrightRuntime.WheelwrightCapabilityCode, "yard", RepairUrgency.Routine, 10, diagnostics);
            Assert.IsNull(queue.Diagnose(mine.WorkOrderId, "x", 0, string.Empty, 240));

            Assert.AreEqual(10, TurnaroundEstimator.EstimateCompletionDay(mine, queue, 10));
        }

        [Test]
        public void Policy_EmergencyJumpsQueue_And_EscalatesOverdue()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            var queue = shop.Repairs;

            RepairWorkOrder routine = queue.Intake("A", "a-1", "WG-1", "Wagon 1", "x",
                WheelwrightRuntime.WheelwrightCapabilityCode, "yard", RepairUrgency.Routine, 10, diagnostics);
            RepairWorkOrder emergency = queue.Intake("B", "b-1", "WG-2", "Wagon 2", "x",
                WheelwrightRuntime.WheelwrightCapabilityCode, "yard", RepairUrgency.Emergency, 11, diagnostics);
            Assert.Less(emergency.QueuePosition, routine.QueuePosition);

            var escalated = WheelwrightRepairPolicy.EscalateOverdueOrders(queue, 10 + 14, diagnostics);
            Assert.Contains(routine.WorkOrderId, escalated);
            Assert.AreEqual(RepairUrgency.Urgent, routine.Urgency);
            Assert.AreEqual(RepairUrgency.Emergency, emergency.Urgency, "emergency is the ceiling");
        }

        [Test]
        public void Policy_FlagsHaulingBlockedOrders()
        {
            var shop = NewShop();
            var pool = NewPool();
            var diagnostics = new List<string>();
            var queue = shop.Repairs;

            RepairWorkOrder order = queue.Intake("Freight Co", "freight-1", "WG-1", "Freight wagon 1",
                "axle cracked", WheelwrightRuntime.WheelwrightCapabilityCode,
                "freight-yard", RepairUrgency.Urgent, 10, diagnostics);
            Assert.IsTrue(pool.TrySendToRepair("WG-1", diagnostics));

            var flagged = WheelwrightRepairPolicy.FlagHaulingBlockedOrders(queue, pool);
            Assert.Contains(order.WorkOrderId, flagged, "WG-1 cannot haul while InRepair");
        }

        [Test]
        public void Wear_GrowsWith_Use_Load_Road_Age_Defects()
        {
            float base_ = WagonWearModel.ComputeTripWear01(100f, 0f, 1f, false, 0f);
            float loaded = WagonWearModel.ComputeTripWear01(100f, 1f, 1f, false, 0f);
            float muddy = WagonWearModel.ComputeTripWear01(100f, 0f, 0f, false, 0f);
            float defective = WagonWearModel.ComputeTripWear01(100f, 0f, 1f, true, 0f);
            float old = WagonWearModel.ComputeTripWear01(100f, 0f, 1f, false, 20f);

            Assert.Greater(loaded, base_, "load wears");
            Assert.Greater(muddy, base_, "bad roads wear");
            Assert.Greater(defective, base_, "unresolved defects wear");
            Assert.Greater(old, base_, "age wears");
            Assert.AreEqual(0f, WagonWearModel.ComputeTripWear01(0f, 0f, 1f, false, 0f), "no miles, no wear");
            Assert.LessOrEqual(WagonWearModel.ComputeTripWear01(10000f, 1f, 0f, true, 50f),
                WagonWearModel.MaxTripWear01, "single-trip wear is capped");
        }

        [Test]
        public void Demand_ScanFleet_OpensOrders_ForWornWagons()
        {
            var shop = NewShop();
            var pool = NewPool();
            var diagnostics = new List<string>();

            pool.GetWagon("WG-1").Damage(0.55f); // condition 0.45 -> routine repair
            pool.GetWagon("WG-2").Damage(0.85f); // condition 0.15 -> emergency
            // WG-3 stays at 1.0 — no demand.

            var opened = RepairDemandService.ScanFleetForRepair(
                pool, shop, "freight-1", "Freight Co", 60, diagnostics);

            Assert.AreEqual(2, opened.Count);
            Assert.AreEqual(RepairUrgency.Routine, opened[0].Urgency);
            Assert.AreEqual(RepairUrgency.Emergency, opened[1].Urgency);
            Assert.AreEqual("WG-1", opened[0].AssetId);
            Assert.AreEqual(FreightWagonStatus.InRepair, pool.GetWagon("WG-1").Status);
            Assert.AreEqual(FreightWagonStatus.InRepair, pool.GetWagon("WG-2").Status);
            Assert.AreEqual(FreightWagonStatus.Available, pool.GetWagon("WG-3").Status);
            Assert.AreEqual(WheelwrightRuntime.WheelwrightCapabilityCode, opened[0].RequiredCapability);
        }

        [Test]
        public void Demand_ScanFleet_NeverDoubleOpens()
        {
            var shop = NewShop();
            var pool = NewPool();
            var diagnostics = new List<string>();

            pool.GetWagon("WG-1").Damage(0.55f);
            Assert.AreEqual(1, RepairDemandService.ScanFleetForRepair(
                pool, shop, "freight-1", "Freight Co", 60, diagnostics).Count);
            Assert.AreEqual(0, RepairDemandService.ScanFleetForRepair(
                pool, shop, "freight-1", "Freight Co", 61, diagnostics).Count,
                "wagon already InRepair with an open order — no duplicate");
        }

        [Test]
        public void IronworkSupply_FindsBlacksmithWagonParts()
        {
            var part = new EquipmentAsset
            {
                AssetId = "EQ-smith-1-007", Kind = "wagon-part", DisplayName = "Wagon ironwork",
                MadeByBusinessId = "smith-1", MadeByBusinessName = "Test Smithy",
            };
            // Hand the smith-made part over as the shop would receive it.
            var shop = NewShop();
            var diagnostics = new List<string>();

            Assert.IsNull(shop.ReceiveIronworkPart(part, diagnostics));
            Assert.AreEqual(1, shop.IronworkPartsOnHand);
            Assert.IsTrue(diagnostics[0].Contains("smith-1"), "provenance named on receipt");
        }

        [Test]
        public void IronworkSupply_FindAvailableIronwork_ScansSmithyStock()
        {
            var smithy = new BlacksmithRuntime("smith-1", "Test Smithy", true);
            var found = BlacksmithIronworkSupply.FindAvailableIronwork(smithy);
            Assert.IsNotNull(found);
            Assert.AreEqual(0, found.Count, "fresh smithy has no finished parts yet");

            Assert.IsNotNull(BlacksmithIronworkSupply.FindAvailableIronwork(null),
                "null smithy returns an empty list, not a crash");
        }
    }
}
