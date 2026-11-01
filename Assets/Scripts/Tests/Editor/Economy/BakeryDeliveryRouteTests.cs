using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1D: delivery routes — wholesale loads move by wagon and team at a
    /// real operating cost (Canon §7.3E), never teleport (Tech X §9.3),
    /// age in transit, and arrive with provenance for the buyer's pantry.
    /// </summary>
    [TestFixture]
    public sealed class BakeryDeliveryRouteTests
    {
        private static Func<string, WorkstationComponentView?> OvenFinder()
        {
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "oven-chamber-1", "oven-chamber" },
                { "kneading-table-1", "kneading-table" },
                { "proofing-rack-1", "proofing-rack" },
                { "bake-peels-1", "bake-peels" },
            };
            return assetId => kinds.TryGetValue(assetId, out string kind)
                ? (WorkstationComponentView?)new WorkstationComponentView
                {
                    AssetId = assetId,
                    Kind = kind,
                    Condition01 = 0.9f,
                    IsUsable = true,
                }
                : null;
        }

        private static BakeryShopRuntime StockedShop(List<string> diag, int day = 290)
        {
            var runtime = new BakeryShopRuntime("bakery-biz-1");
            var registry = new EntityIdRegistry();
            BakeryFlourBootstrap.ApplyBootstrapEndowment(runtime.FlourStock, registry, day, diag);
            BakeryFuelBootstrap.ApplyBootstrapEndowment(runtime.FuelStock, registry, day, diag);
            Assert.Null(runtime.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, diag));
            return runtime;
        }

        private static int WorkDay(BakeryShopRuntime runtime, int day, List<string> diag)
        {
            return runtime.WorkDay(day, bakerKitUsable: true,
                WorkstationCatalog.BakeOven, OvenFinder(), diag, new EntityIdRegistry());
        }

        private static BakeryWholesaleDeliveryRecord FulfilledDelivery(
            BakeryShopRuntime runtime, int day, List<string> diag)
        {
            Assert.IsNull(runtime.AddWholesaleAccount(new BakeryWholesaleAccount
            {
                BuyerBusinessId = "boardinghouse-biz-1",
                BuyerType = BusinessType.BoardingHouse,
                ProductId = BakeryBreadCatalog.BreadLoafId,
                UnitsPerDelivery = 24,
                DeliveryCadenceDays = 1,
                NextDeliveryDayIndex = day,
                PricePerUnitCents = 6,
            }, diag));

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, day, diag));
            Assert.AreEqual(1, WorkDay(runtime, day, diag));
            return runtime.FulfillWholesale(runtime.WholesaleAccounts[0].AccountId, day, diag,
                new EntityIdRegistry());
        }

        private static BakeryHaulingTerms WagonTerms()
        {
            var terms = new BakeryHaulingTerms
            {
                WagonId = "delivery-wagon-1",
                TeamId = "team-mares-1",
            };
            terms.MilesToBuyer["boardinghouse-biz-1"] = 3;
            terms.TransitDaysToBuyer["boardinghouse-biz-1"] = 0;
            return terms;
        }

        [Test]
        public void StageDeliveryRun_WithWagon_CostsDriverLaborAndHaul()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            var record = FulfilledDelivery(runtime, 300, diag);

            var run = runtime.StageDeliveryRun(record, WagonTerms(), 300, diag);

            Assert.IsNotNull(run);
            Assert.AreEqual(BakeryDeliveryRunStatus.Staged, run.Status);
            Assert.AreEqual(24, run.TotalUnits);
            Assert.AreEqual(3 * 20, run.DriverLaborMinutes, "driver minutes = miles × rate");
            Assert.AreEqual(3 * 5, run.HaulCostCents, "haul cost = miles × rate");
            Assert.AreEqual("delivery-wagon-1", run.WagonId);
            Assert.AreEqual(LocalRecurringOrderHaulingResponsibility.Seller, run.Hauling);
        }

        [Test]
        public void StageDeliveryRun_WithoutWagon_WaitsForBuyerPickup()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            var record = FulfilledDelivery(runtime, 300, diag);

            var run = runtime.StageDeliveryRun(record, new BakeryHaulingTerms(), 300, diag);

            Assert.IsNotNull(run);
            Assert.AreEqual(BakeryDeliveryRunStatus.AwaitingBuyerPickup, run.Status,
                "no wagon/team — the buyer collects; bread never teleports");
            Assert.AreEqual(LocalRecurringOrderHaulingResponsibility.Buyer, run.Hauling);
            StringAssert.Contains("buyer pickup", string.Join("\n", diag).ToLower());
        }

        [Test]
        public void DepartAndArrive_SameDayDelivery_ArrivesFresh()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            var record = FulfilledDelivery(runtime, 300, diag);
            var run = runtime.StageDeliveryRun(record, WagonTerms(), 300, diag);

            runtime.DepartDeliveryRuns(300, diag);
            Assert.AreEqual(BakeryDeliveryRunStatus.InTransit, run.Status);
            Assert.AreEqual(300, run.ArrivalDayIndex, "in-town: same-day arrival");

            var arrived = runtime.ArriveDeliveryRuns(300, diag);
            Assert.AreEqual(1, arrived.Count);
            Assert.AreEqual(BakeryDeliveryRunStatus.Delivered, run.Status);
            Assert.AreEqual(24, run.TotalUnits);

            // The buyer's intake gets a lot snapshot aged to arrival.
            var snapshot = run.CargoLines[0].ToBreadLotSnapshot(300);
            Assert.AreEqual(BakeryGoodsCondition.Fresh, snapshot.Condition);
            Assert.AreEqual(24, snapshot.UnitCount);
            Assert.Greater(snapshot.InputProvenance.Count, 0, "flour/fuel provenance arrives with the bread");
        }

        [Test]
        public void Arrive_AfterTwoTransitDays_StaleCargoBecomesWaste()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            var record = FulfilledDelivery(runtime, 300, diag);

            var terms = WagonTerms();
            terms.TransitDaysToBuyer["boardinghouse-biz-1"] = 2; // out-of-town buyer
            var run = runtime.StageDeliveryRun(record, terms, 300, diag);

            runtime.DepartDeliveryRuns(300, diag);
            Assert.AreEqual(302, run.ArrivalDayIndex);

            var arrived = runtime.ArriveDeliveryRuns(302, diag);
            Assert.AreEqual(1, arrived.Count);
            Assert.AreEqual(BakeryDeliveryRunStatus.Delivered, run.Status);
            Assert.AreEqual(0, run.TotalUnits, "stale cargo is zeroed, never delivered");

            Assert.AreEqual(1, runtime.Waste.Count, "stale-in-transit is written off as waste");
            Assert.AreEqual(24, runtime.Waste[0].Units);
            Assert.AreEqual(300, runtime.Waste[0].BakedDayIndex);
            Assert.AreEqual(302, runtime.Waste[0].WastedDayIndex);
            StringAssert.Contains("stale in transit", string.Join("\n", diag).ToLower());
        }

        [Test]
        public void Arrive_OneTransitDay_ArrivesDayOld()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            var record = FulfilledDelivery(runtime, 300, diag);

            var terms = WagonTerms();
            terms.TransitDaysToBuyer["boardinghouse-biz-1"] = 1;
            var run = runtime.StageDeliveryRun(record, terms, 300, diag);
            runtime.DepartDeliveryRuns(300, diag);

            var arrived = runtime.ArriveDeliveryRuns(301, diag);
            Assert.AreEqual(1, arrived.Count);
            Assert.AreEqual(BakeryGoodsCondition.DayOld, run.CargoLines[0].ConditionOnArrival(301),
                "bread ages in transit exactly as on the shelf");
            Assert.AreEqual(24, run.TotalUnits, "day-old still delivers");
            Assert.AreEqual(0, runtime.Waste.Count);
        }

        [Test]
        public void MarkPickedUp_CollectsAwaitingRun()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            var record = FulfilledDelivery(runtime, 300, diag);
            var run = runtime.StageDeliveryRun(record, new BakeryHaulingTerms(), 300, diag);

            Assert.IsNull(runtime.MarkPickedUp(run.RunId, 301, diag));
            Assert.AreEqual(BakeryDeliveryRunStatus.Delivered, run.Status);
            Assert.AreEqual(24, run.TotalUnits);

            Assert.IsNotNull(runtime.MarkPickedUp("no-such-run", 301, diag),
                "unknown runs refuse loudly");
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesRuns()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            var record = FulfilledDelivery(runtime, 300, diag);
            var terms = WagonTerms();
            terms.TransitDaysToBuyer["boardinghouse-biz-1"] = 1;
            runtime.StageDeliveryRun(record, terms, 300, diag);
            runtime.DepartDeliveryRuns(300, diag);

            var dto = runtime.CaptureSaveDto();
            var revived = new BakeryShopRuntime("bakery-biz-1");
            revived.LoadFromSaveDto(dto);

            Assert.AreEqual(1, revived.DeliveryRuns.Count);
            var run = revived.DeliveryRuns[0];
            Assert.AreEqual(BakeryDeliveryRunStatus.InTransit, run.Status);
            Assert.AreEqual(301, run.ArrivalDayIndex);
            Assert.AreEqual(24, run.TotalUnits);
            Assert.AreEqual(1, revived.WholesaleDeliveries.Count);
            Assert.AreEqual(1, revived.WholesaleAccounts.Count);

            // The revived run still arrives.
            var arrived = revived.ArriveDeliveryRuns(301, new List<string>());
            Assert.AreEqual(1, arrived.Count);
        }
    }
}
