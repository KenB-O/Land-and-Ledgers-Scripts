using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.FuelDealer;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.FreightFuel;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Review 2 Pass A (general): decision-free Roslyn tests only.
    /// These tests pin DOCUMENTED contracts from the Series 2 reviews
    /// that no earlier suite asserted; they change NO production
    /// behaviour and assert NO decided rule. In particular:
    /// - Retail adapters post cash AFTER stock moves, so a failing
    ///   cash port leaves the sale standing with a WARNING. That is
    ///   the documented Pass 7 pattern, not a fix: Pass 9 proved the
    ///   production port (Series2BusinessRuntimeCashPort) cannot
    ///   fail a non-negative credit on a non-null runtime, so the
    ///   WARNING branch is defensive dead code on the production
    ///   port. These tests prove both halves: the WARNING fires
    ///   with a failing port, and it does NOT fire with the
    ///   production port.
    /// - The bakery adapter's CheckTradingCapability ignores its
    ///   absolute-day argument and refuses only while DayIndex is
    ///   unset; the sale-day price guard fires only when the sale
    ///   price differs from the quoted price. Day equality is NOT
    ///   enforced by the adapter — the day driver must set DayIndex
    ///   at every day start. That is a binding obligation, left
    ///   for Codex, not silently "fixed" here.
    /// - The Pass 8 dealer fuel source's partial-supply nuance:
    ///   a dealer-only refusal dispenses nothing, but once an
    ///   off-map callback is wired, a dealer portion moves into
    ///   the bakery store BEFORE the callback can refuse or
    ///   under-deliver, and that portion stays. Fuel is not lost,
    ///   but a refused supply is NOT a clean rollback. The
    ///   production driver must run the Pass 6 pre-check first
    ///   or explicitly accept partial-store state.
    /// All prices, quantities, and conversions are test parameters,
    /// never canon. No fixture data, no Ethan/Tia/Shane data, no
    /// nine-person fixture is used here. Unity compilation, Test
    /// Runner, PlayMode, and scene wiring are NOT claimed.
    /// </summary>
    [TestFixture]
    public sealed class Review2PassAGeneralTests
    {
        private sealed class FailingCashPort : IBusinessCashPort
        {
            public int BalanceCents => 999999;
            public string TryDebit(int amountCents, string reason) => null;
            public string Credit(int amountCents, string reason) => "test cash port refused";
        }

        private sealed class BakerySetup
        {
            public List<string> Diag = new List<string>();
            public BusinessRuntimeState BakeryRuntime = new BusinessRuntimeState();
            public BakeryShopRuntime Bakery = new BakeryShopRuntime("pierce-bakery-1");
            public EntityIdRegistry Ids = new EntityIdRegistry();
        }

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

        private static BakerySetup BakeOneBatch(int bakeryCash, int bakeDay)
        {
            var s = new BakerySetup();
            s.BakeryRuntime.BindBusinessIdentity("pierce-bakery-1");
            s.BakeryRuntime.AddCashCents(bakeryCash);
            Assert.IsNull(s.Bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, s.Diag), "oven installation should succeed");
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = 24,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-flour-order",
                OriginName = "Test mill",
            }, s.Diag), "flour lot should be received");
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-notions-order",
                OriginName = "Test notions",
            }, s.Diag), "notions lot should be received");
            Assert.IsNull(s.Bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-fuel-order",
                OriginName = "Test fuel",
            }, s.Diag), "fuel lot should be received");
            var driver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, bakeDay, s.Diag));
            int baked = driver.RunScheduledDay(
                bakeDay, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag, s.Ids);
            Assert.AreEqual(1, baked, "one batch should bake; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount, "one batch yields 24 loaves");
            return s;
        }

        private static ShoppingSettlement CashSettlement()
        {
            return new ShoppingSettlement { Mode = ShoppingSettlementMode.CashFromLedger };
        }

        private static bool HasWarning(List<string> diag)
        {
            foreach (string line in diag) if (line != null && line.Contains("WARNING")) return true;
            return false;
        }

        private static Series2FuelDealerSupplierAdapter NewFuelAdapter(
            IBusinessCashPort port, out FuelYardByproductIntake intake, out BusinessRuntimeState dealerRuntime)
        {
            intake = new FuelYardByproductIntake();
            dealerRuntime = new BusinessRuntimeState();
            dealerRuntime.BindBusinessIdentity("fuel-dealer-1");
            dealerRuntime.AddCashCents(1000);
            var ids = new EntityIdRegistry();
            var diag = new List<string>();
            Assert.IsNull(intake.ReceiveByproductLot("fuel-dealer-1", new SlabOffcutFuelLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FuelWoodUnits = 20,
                SourceMillBusinessId = "mill-1",
                SourceLogLotId = "log-1",
                StandId = "stand-1",
                Species = "pine",
                ProducedDayIndex = 0,
            }, diag), "intake lot with full provenance should be received; diag: " + string.Join("; ", diag));
            return new Series2FuelDealerSupplierAdapter(
                intake, port, "fuel-dealer-1", "Fuel Dealer", "fuel-yard", 28);
        }

        private static Series2FuelDealer DealerWith(int unitsA, int unitsB)
        {
            var ids = new EntityIdRegistry();
            var intake = new FuelYardByproductIntake();
            var dealer = new Series2FuelDealer(intake, "fuel-dealer-1");
            var diag = new List<string>();
            Assert.IsNull(dealer.ReceiveSawmillLot(new SlabOffcutFuelLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FuelWoodUnits = unitsA,
                SourceMillBusinessId = "mill-1",
                SourceLogLotId = "log-lot-1",
                StandId = "stand-1",
                Species = "pine",
                ProducedDayIndex = 1,
            }, diag));
            if (unitsB > 0)
                Assert.IsNull(dealer.ReceiveSawmillLot(new SlabOffcutFuelLot
                {
                    LotId = ids.Allocate(EntityKind.Lot),
                    FuelWoodUnits = unitsB,
                    SourceMillBusinessId = "mill-1",
                    SourceLogLotId = "log-lot-1",
                    StandId = "stand-1",
                    Species = "pine",
                    ProducedDayIndex = 2,
                }, diag));
            return dealer;
        }

        // ---- Retail adapter cash failure: WARNING-only, sale stands ----

        [Test]
        public void BakeryAdapter_FailingCashPort_SaleStandsWithWarning_CashUnposted()
        {
            BakerySetup s = BakeOneBatch(bakeryCash: 2000, bakeDay: 2);
            var adapter = new Series2BakerySupplierAdapter(
                s.Bakery, new FailingCashPort(),
                "pierce-bakery-1", "Pierce Bakery", "bakery");
            adapter.DayIndex = 2;
            var diag = new List<string>();
            Assert.AreEqual(5, adapter.SellItem("bread", 5, 2, CashSettlement(), diag),
                "a failing cash port must not turn a completed shelf sale into a refusal; diag: " + string.Join("; ", diag));
            Assert.AreEqual(19, s.Bakery.BreadShelf[0].UnitCount, "shelf loses exactly the units sold");
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents, "no cash was posted through the failing port");
            Assert.IsTrue(HasWarning(diag), "the failure must be diagnosed loudly as a WARNING, never silently dropped; diag: " + string.Join("; ", diag));
        }

        [Test]
        public void FuelAdapter_FailingCashPort_SaleStandsWithWarning_CashUnposted()
        {
            Series2FuelDealerSupplierAdapter adapter = NewFuelAdapter(
                new FailingCashPort(), out FuelYardByproductIntake intake, out BusinessRuntimeState dealerRuntime);
            var diag = new List<string>();
            Assert.AreEqual(5, adapter.SellItem("fuel_wood", 5, 2, CashSettlement(), diag),
                "a failing cash port must not turn a completed dispense into a refusal; diag: " + string.Join("; ", diag));
            Assert.AreEqual(15, intake.TotalFuelWoodUnitsFor("fuel-dealer-1"), "intake loses exactly the units dispensed");
            Assert.AreEqual(1000, dealerRuntime.CurrentCashCents, "no cash was posted through the failing port");
            Assert.IsTrue(HasWarning(diag), "the failure must be diagnosed loudly as a WARNING; diag: " + string.Join("; ", diag));
        }

        // ---- Production port: the WARNING branch is defensive dead code ----

        [Test]
        public void BakeryAdapter_ProductionPort_SalePostsCashWithNoWarning()
        {
            BakerySetup s = BakeOneBatch(bakeryCash: 2000, bakeDay: 2);
            var adapter = new Series2BakerySupplierAdapter(
                s.Bakery, new Series2BusinessRuntimeCashPort(s.BakeryRuntime),
                "pierce-bakery-1", "Pierce Bakery", "bakery");
            adapter.DayIndex = 2;
            var diag = new List<string>();
            Assert.AreEqual(5, adapter.SellItem("bread", 5, 2, CashSettlement(), diag));
            Assert.AreEqual(2000 + 40, s.BakeryRuntime.CurrentCashCents, "production port posts exactly 5 x 8c");
            Assert.IsFalse(HasWarning(diag), "the production port must not trigger the WARNING branch; diag: " + string.Join("; ", diag));
        }

        [Test]
        public void FuelAdapter_ProductionPort_SalePostsCashWithNoWarning()
        {
            var intake = new FuelYardByproductIntake();
            var dealerRuntime = new BusinessRuntimeState();
            dealerRuntime.BindBusinessIdentity("fuel-dealer-1");
            dealerRuntime.AddCashCents(1000);
            var ids = new EntityIdRegistry();
            var setupDiag = new List<string>();
            Assert.IsNull(intake.ReceiveByproductLot("fuel-dealer-1", new SlabOffcutFuelLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FuelWoodUnits = 20,
                SourceMillBusinessId = "mill-1",
                SourceLogLotId = "log-1",
                StandId = "stand-1",
                Species = "pine",
                ProducedDayIndex = 0,
            }, setupDiag), "intake lot should be received; diag: " + string.Join("; ", setupDiag));
            var adapter = new Series2FuelDealerSupplierAdapter(
                intake, new Series2BusinessRuntimeCashPort(dealerRuntime),
                "fuel-dealer-1", "Fuel Dealer", "fuel-yard", 28);
            var diag = new List<string>();
            Assert.AreEqual(5, adapter.SellItem("fuel_wood", 5, 2, CashSettlement(), diag));
            Assert.AreEqual(1000 + 140, dealerRuntime.CurrentCashCents, "production port posts exactly 5 x 28c");
            Assert.IsFalse(HasWarning(diag), "the production port must not trigger the WARNING branch; diag: " + string.Join("; ", diag));
        }

        // ---- Bakery DayIndex gating: what the adapter does and does not enforce ----

        [Test]
        public void BakeryAdapter_CheckTradingCapability_IgnoresDayArgument_RefusesOnlyWhenUnset()
        {
            BakerySetup s = BakeOneBatch(bakeryCash: 2000, bakeDay: 2);
            var adapter = new Series2BakerySupplierAdapter(
                s.Bakery, new Series2BusinessRuntimeCashPort(s.BakeryRuntime),
                "pierce-bakery-1", "Pierce Bakery", "bakery");
            Assert.IsNotNull(adapter.CheckTradingCapability(2, 0), "unset DayIndex refuses regardless of the argument day");
            Assert.IsNotNull(adapter.CheckTradingCapability(99, 0), "unset DayIndex refuses for any argument day");
            adapter.DayIndex = 2;
            Assert.IsNull(adapter.CheckTradingCapability(99, 0),
                "once DayIndex is set, the capability check ignores its day argument — it does NOT enforce day equality; the day driver must set DayIndex daily");
            Assert.IsNull(adapter.CheckTradingCapability(2, 0));
        }

        [Test]
        public void BakeryAdapter_SaleDayPriceGuard_RefusesOnPriceDifference_StockIntact()
        {
            BakerySetup s = BakeOneBatch(bakeryCash: 2000, bakeDay: 2);
            var adapter = new Series2BakerySupplierAdapter(
                s.Bakery, new Series2BusinessRuntimeCashPort(s.BakeryRuntime),
                "pierce-bakery-1", "Pierce Bakery", "bakery");
            adapter.DayIndex = 2;
            Assert.AreEqual(8, adapter.ItemPricePerUnitCents("bread"), "fresh quote is 8c on day 2");
            var diag = new List<string>();
            Assert.AreEqual(0, adapter.SellItem("bread", 5, 3, CashSettlement(), diag),
                "a day-3 sale at the day-old price (4c) against a day-2 quote (8c) must refuse, not post a mismatched sale; diag: " + string.Join("; ", diag));
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount, "refused sale leaves shelf intact");
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents, "refused sale posts no cash");
            adapter.DayIndex = 3;
            Assert.AreEqual(5, adapter.SellItem("bread", 5, 3, CashSettlement(), diag),
                "with DayIndex set to the sale day, quote and sale agree and the sale proceeds");
            Assert.AreEqual(2000 + 20, s.BakeryRuntime.CurrentCashCents, "day-old revenue is 5 x 4c");
        }

        // ---- Dealer fuel source: partial supply is real, not a clean rollback ----

        [Test]
        public void DealerSource_OffMapRefusal_LeavesDealerPortionInBakeryStore()
        {
            var dealer = DealerWith(2, 0);
            var ids = new EntityIdRegistry();
            var bakery = new BakeryShopRuntime("bakery-1");
            Series2OffMapFuelReceipt refusingReceipt = (BakeryFuelStock stock, int needed, int day, EntityIdRegistry registry, List<string> d) =>
                "test off-map import refused";
            var source = new Series2DealerBakeryFuelSource(dealer, 1, refusingReceipt, ids);
            var diag = new List<string>();
            Assert.IsNotNull(source.SupplyTo(bakery, 5, 5, diag),
                "a refused off-map receipt must refuse the supply, never bake on uncovered fuel; diag: " + string.Join("; ", diag));
            Assert.AreEqual(2, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "CONTRAST with the dealer-only refusal (which dispenses nothing): the dealer portion moved BEFORE the callback refused, and it stays in the bakery store — fuel is not lost, but the refusal is not a clean rollback");
            Assert.AreEqual(0, dealer.StockUnits, "dealer stock lost exactly the portion it dispensed");
        }

        [Test]
        public void DealerSource_OffMapUnderDelivery_LeavesDealerAndImportPortionsInBakeryStore()
        {
            var dealer = DealerWith(2, 0);
            var ids = new EntityIdRegistry();
            var bakery = new BakeryShopRuntime("bakery-1");
            Series2OffMapFuelReceipt underDeliveringReceipt = (BakeryFuelStock stock, int needed, int day, EntityIdRegistry registry, List<string> d) =>
                BakeryFuelSupply.ReceiveImportArrival(stock, 1, "import-order-1", "Off-map test origin", day, registry, d);
            var source = new Series2DealerBakeryFuelSource(dealer, 1, underDeliveringReceipt, ids);
            var diag = new List<string>();
            Assert.IsNotNull(source.SupplyTo(bakery, 5, 5, diag),
                "an import that covers only part of the remainder must refuse the supply; diag: " + string.Join("; ", diag));
            Assert.AreEqual(3, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "bakery store holds the 2 dealer units plus the 1 import unit — both portions stay after the refusal");
            Assert.AreEqual(0, dealer.StockUnits);
        }
    }
}
