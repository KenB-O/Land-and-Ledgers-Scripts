using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

// C-7 (expanded campaign 2026-11-16): the logging camp's supply line.
// The camp orders flour sacks from the General Store at the store's live
// selling price, pays goods + freight from mill cash, hauls the sacks to
// the camp through the real freight chain, and records the delivery into
// the camp's supply ledger with the store named as supplier. No free
// goods, no instant delivery, no invented supplier: the store's real
// retail lots are proved before anything moves, the freight is the
// DEF-016 quote on the stand's authored miles, and the balance is the
// order state — a delivered order raises the balance above the reorder
// threshold, so no duplicate order fires.
//
// Fixture mirrors C-5 (store runtime seeded with an identified retail
// flour lot via TryAddRetailItemLot the way ScenarioOpeningStockEndowment
// does it; mill cash 15,000c) + C-6 (FreightShipmentService, wagon
// 450kg/4m3, rate card 25/1/round-trip, TaskAuthority /
// WorkTimeBudgetStore / PersonScheduleTracker, crew ids 41/42/43 + 1
// horse) + the camp (LoggingCampSite, 2 loggers rostered). Dev scenario
// numbers: 2 heads x 1 ration x 7 days = 14 units of cover; 1 sack =
// 5 units, so an empty camp orders 3 sacks. Flour at the store's live
// baseline 35c/sack; North Pine stand 2.0 miles: 3 sacks / 2 miles =
// 25c + 1c x (2 x 2) = 29c freight, one trip.
//
// Unity compilation, Test Runner, PlayMode, and the driver call site are
// NOT claimed; SimulationDrivers is harness-excluded, so the daily
// trigger is source-reviewed only and exercised here through the same
// public API it calls.
namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class LoggingCampSupplyC7Tests
    {
        private const int Day = 300;
        private const string MillId = "mill-1";
        private const string StoreId = "general-store-1";
        private const string CampId = "camp-1";
        private const string StandId = "stand-1";
        private const string StoreFlourLotId = "opening-endowment:dev:general-store:seed:flour";
        private const int FlourPriceCents = 35;
        private const float Miles = 2.0f;
        private const int TwoMileTripChargeCents = 29;
        private const int SacksPerOrder = 3;
        private const int GoodsPerOrderCents = SacksPerOrder * FlourPriceCents;
        private const int OrderTotalCents = GoodsPerOrderCents + TwoMileTripChargeCents;

        private sealed class World
        {
            public LoggingCampSite Camp;
            public LoggingCampSupplyService Service;
            public List<string> Diagnostics = new List<string>();
            public BusinessRuntimeState StoreRuntime = new BusinessRuntimeState();
            public TestBusinessCashPort MillCash;
            public TestBusinessCashPort CarrierCash;
            public FreightShipmentService Freight = new FreightShipmentService();
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public PersonScheduleTracker Schedules = new PersonScheduleTracker();
            public EquipmentAsset Wagon;
            public EquipmentAsset BlockerWagon;
            public DevelopmentTownConfiguration.FreightServiceRateCard RateCard;
        }

        /// <summary>
        /// C-6: in-memory business cash port. Mirrors the production
        /// Series2BusinessRuntimeCashPort contract: Credit cannot fail
        /// for a non-negative amount on a bound port; TryDebit is atomic
        /// (refuses when short, moving nothing) and returns null on
        /// success, a diagnostic on failure.
        /// </summary>
        private sealed class TestBusinessCashPort : IBusinessIdentityCashPort
        {
            public string BusinessId { get; }
            public int BalanceCents { get; private set; }
            public bool RefuseCredit { get; set; }
            public bool RefuseDebit { get; set; }

            public TestBusinessCashPort(string businessId, int openingBalanceCents)
            {
                BusinessId = businessId;
                BalanceCents = openingBalanceCents;
            }

            public void SetBalance(int balanceCents)
            {
                BalanceCents = balanceCents;
            }

            public string TryDebit(int amountCents, string reason)
            {
                if (RefuseDebit) return "TestBusinessCashPort: debit refused by the test double.";
                if (amountCents < 0) return "TestBusinessCashPort: negative debit.";
                if (BalanceCents < amountCents) return $"TestBusinessCashPort: insufficient funds ({BalanceCents}c for {amountCents}c).";
                BalanceCents -= amountCents;
                return null;
            }

            public string Credit(int amountCents, string reason)
            {
                if (RefuseCredit) return "TestBusinessCashPort: credit refused by the test double.";
                if (amountCents < 0) return "TestBusinessCashPort: negative credit.";
                BalanceCents += amountCents;
                return null;
            }
        }

        private static World NewWorld(int millCash = 15000, int storeCash = 5000, int storeFlourSacks = 48)
        {
            var camp = new LoggingCampSite(CampId, MillId, StandId, Day);
            var world = new World
            {
                Camp = camp,
                MillCash = new TestBusinessCashPort(MillId, millCash),
                CarrierCash = new TestBusinessCashPort("freight-co", 0),
                Wagon = new EquipmentAsset
                {
                    AssetId = "opening-freight-wagon",
                    Kind = "wagon",
                    DisplayName = "farm wagon",
                    PayloadMassCapacityKg = 450f,
                    CargoVolumeCapacityM3 = 4f,
                    Condition01 = 1f,
                },
                BlockerWagon = new EquipmentAsset
                {
                    AssetId = "blocker-wagon",
                    Kind = "wagon",
                    DisplayName = "second farm wagon",
                    PayloadMassCapacityKg = 450f,
                    CargoVolumeCapacityM3 = 4f,
                    Condition01 = 1f,
                },
                RateCard = new DevelopmentTownConfiguration.FreightServiceRateCard
                {
                    baseServiceFeeCents = 25,
                    ratePerMileCents = 1,
                    chargeRoundTrip = true,
                },
            };
            world.Service = new LoggingCampSupplyService(camp, world.Freight, world.Diagnostics);
            world.StoreRuntime.BindBusinessIdentity(StoreId);
            world.StoreRuntime.AddCashCents(storeCash);
            world.StoreRuntime.EnsureCategoryStock("staple_food", 0, 1000);
            if (storeFlourSacks > 0)
            {
                Assert.IsNull(world.StoreRuntime.TryAddRetailItemLot(
                    StoreFlourLotId, "flour", "staple_food", "10 lb sack",
                    storeFlourSacks, StoreId, StoreId,
                    "DevelopmentTownConfiguration opening inventory, general-store flour", 0),
                    "store flour lot seeding should succeed");
            }
            Assert.IsNull(camp.AssignWorker(EntityId.For(EntityKind.Person, 41), "feller", Day, world.Diagnostics));
            Assert.IsNull(camp.AssignWorker(EntityId.For(EntityKind.Person, 42), "feller", Day, world.Diagnostics));
            Assert.AreEqual(2, camp.CrewHeadcount, "The dev camp rosters 2 loggers.");
            return world;
        }

        private static LoggingCampSupplyRequest MakeRequest(World world, float miles = Miles)
        {
            return new LoggingCampSupplyRequest
            {
                StoreBusinessId = StoreId,
                MillBusinessId = MillId,
                Miles = miles,
                TransitGameSeconds = 10000f,
                ConditionMultiplier = 0f,
                CarrierLabel = "freight-co",
            };
        }

        private static LoggingCampSupplyExecution MakeExecution(World world)
        {
            return new LoggingCampSupplyExecution
            {
                StoreRuntime = world.StoreRuntime,
                RateCard = world.RateCard,
                Wagon = world.Wagon,
                MillCash = world.MillCash,
                CarrierCash = world.CarrierCash,
                LoaderId = EntityId.For(EntityKind.Person, 41),
                DriverId = EntityId.For(EntityKind.Person, 42),
                DraftAnimalIds = new List<EntityId> { EntityId.For(EntityKind.Animal, 1) },
                UnloaderId = EntityId.For(EntityKind.Person, 43),
                Tasks = world.Tasks,
                Budgets = world.Budgets,
                Schedules = world.Schedules,
                UnitPriceCents = FlourPriceCents,
            };
        }

        private static string Execute(World world, float miles = Miles)
        {
            return world.Service.ExecuteSupplyOrder(MakeRequest(world, miles), MakeExecution(world), Day, world.Diagnostics);
        }

        private static void AssertNothingMoved(World world, int millCash = 15000, int storeCash = 5000, int storeFlourSacks = 48)
        {
            Assert.AreEqual(millCash, world.MillCash.BalanceCents, "Mill cash untouched.");
            Assert.AreEqual(0, world.CarrierCash.BalanceCents, "Carrier cash untouched.");
            Assert.AreEqual(storeCash, world.StoreRuntime.CurrentCashCents, "Store cash untouched.");
            Assert.AreEqual(storeFlourSacks, world.StoreRuntime.GetRetailItemStockUnits("flour", "staple_food"), "Store flour untouched.");
            Assert.AreEqual(0f, world.Camp.SupplyLedger.Balance, "Ledger untouched.");
            Assert.AreEqual(0, world.Camp.SupplyLedger.Deliveries.Count, "No delivery recorded.");
            Assert.AreEqual(0, world.Freight.TrackedShipments.Count, "No freight created.");
        }

        [Test]
        public void C7_EndToEnd_ThreeSacks_SettlesGoodsAndFreightOnce()
        {
            World world = NewWorld();

            string refusal = Execute(world);

            Assert.IsNull(refusal, string.Join("; ", world.Diagnostics));
            Assert.AreEqual(15000 - OrderTotalCents, world.MillCash.BalanceCents,
                "Mill pays goods 105c + freight 29c = 134c, exactly once.");
            Assert.AreEqual(TwoMileTripChargeCents, world.CarrierCash.BalanceCents, "Carrier takes the freight only.");
            Assert.AreEqual(5000 + GoodsPerOrderCents, world.StoreRuntime.CurrentCashCents,
                "Store takes the goods money once, through its settlement.");
            Assert.AreEqual(48 - SacksPerOrder, world.StoreRuntime.GetRetailItemStockUnits("flour", "staple_food"),
                "Store flour 48 -> 45 sacks.");
            Assert.AreEqual(15f, world.Camp.SupplyLedger.Balance, "3 sacks x 5 provisions = 15 units in the ledger.");
            Assert.AreEqual(1, world.Camp.SupplyLedger.Deliveries.Count);
            Assert.AreEqual(StoreId, world.Camp.SupplyLedger.Deliveries[0].SupplierId,
                "The delivery names the store as supplier — never anonymous.");
            Assert.AreEqual(15f, world.Camp.SupplyLedger.Deliveries[0].ProvisionUnits);
            Assert.AreEqual(1, world.Freight.TrackedShipments.Count);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, world.Freight.TrackedShipments[0].Status);
            Assert.AreEqual("campsup-0", world.Freight.TrackedShipments[0].ShipmentId);
        }

        [Test]
        public void C7_QuotePins_ThreeSacks_TwoMiles_And_MultiTrip()
        {
            World world = NewWorld();

            Assert.IsTrue(LoggingCampSupplyService.TryQuoteSupplyCharge(
                3, Miles, world.RateCard, world.Wagon, 0f, out FreightServiceQuote oneTrip, out string reason1));
            Assert.AreEqual(TwoMileTripChargeCents, oneTrip.TotalPriceCents);
            Assert.AreEqual(1, oneTrip.TripCount);
            Assert.AreEqual(TwoMileTripChargeCents, oneTrip.PerTripTotalCents);

            Assert.IsTrue(LoggingCampSupplyService.TryQuoteSupplyCharge(
                100, Miles, world.RateCard, world.Wagon, 0f, out FreightServiceQuote fullLoad, out string reason2));
            Assert.AreEqual(1, fullLoad.TripCount, "100 sacks x 4.5 kg = 450 kg — exactly one wagon load (mass binds).");
            Assert.AreEqual(TwoMileTripChargeCents, fullLoad.TotalPriceCents);

            Assert.IsTrue(LoggingCampSupplyService.TryQuoteSupplyCharge(
                101, Miles, world.RateCard, world.Wagon, 0f, out FreightServiceQuote twoTrips, out string reason3));
            Assert.AreEqual(2, twoTrips.TripCount, "101 sacks needs a second trip.");
            Assert.AreEqual(2 * TwoMileTripChargeCents, twoTrips.TotalPriceCents);

            Assert.IsTrue(!LoggingCampSupplyService.TryQuoteSupplyCharge(
                3, 0f, world.RateCard, world.Wagon, 0f, out _, out string zeroMiles));
            Assert.IsNotNull(zeroMiles, "Zero miles refuses — never a guessed charge.");
        }

        [Test]
        public void C7_ShouldOrder_And_SacksToOrder_Boundary()
        {
            World world = NewWorld();

            Assert.IsTrue(LoggingCampSupplyService.ShouldOrder(world.Camp), "Empty camp orders.");
            Assert.AreEqual(3, LoggingCampSupplyService.SacksToOrder(world.Camp),
                "2 heads x 7 days = 14 units; ceil(14 / 5) = 3 sacks.");
            Assert.IsNull(Execute(world), string.Join("; ", world.Diagnostics));

            Assert.IsTrue(!LoggingCampSupplyService.ShouldOrder(world.Camp), "15 units covers 7 days — no order.");
            Assert.AreEqual(0, LoggingCampSupplyService.SacksToOrder(world.Camp));

            Assert.IsNull(world.Camp.SupplyLedger.Consume(9, Day, world.Diagnostics), "9 units eaten.");
            Assert.AreEqual(6f, world.Camp.SupplyLedger.Balance);
            Assert.IsTrue(!LoggingCampSupplyService.ShouldOrder(world.Camp),
                "6 units covers exactly 3 days — the reorder threshold is BELOW 3 days, not at it.");
            Assert.AreEqual(2, LoggingCampSupplyService.SacksToOrder(world.Camp),
                "The helper still answers the target (ceil(8 / 5) = 2) even when it is not yet time to order.");

            Assert.IsNull(world.Camp.SupplyLedger.Consume(1, Day, world.Diagnostics), "One more unit eaten.");
            Assert.AreEqual(5f, world.Camp.SupplyLedger.Balance);
            Assert.IsTrue(LoggingCampSupplyService.ShouldOrder(world.Camp), "5 units covers fewer than 3 days — order.");
            Assert.AreEqual(2, LoggingCampSupplyService.SacksToOrder(world.Camp), "ceil((14 - 5) / 5) = 2 sacks.");
        }

        [Test]
        public void C7_SecondExecute_AlreadySupplied_IsNoOp()
        {
            World world = NewWorld();
            Assert.IsNull(Execute(world), string.Join("; ", world.Diagnostics));

            string second = Execute(world);

            Assert.IsNull(second, "An already-supplied camp re-executes as a no-op, not a second order.");
            Assert.AreEqual(15000 - OrderTotalCents, world.MillCash.BalanceCents, "No second charge.");
            Assert.AreEqual(TwoMileTripChargeCents, world.CarrierCash.BalanceCents);
            Assert.AreEqual(1, world.Freight.TrackedShipments.Count, "No second shipment.");
            Assert.AreEqual(15f, world.Camp.SupplyLedger.Balance, "No second delivery.");
            Assert.AreEqual(1, world.Camp.SupplyLedger.Deliveries.Count);
        }

        [Test]
        public void C7_MillCannotPay_Refuses_NothingMoves()
        {
            World world = NewWorld(millCash: 100);

            string refusal = Execute(world);

            Assert.IsNotNull(refusal, "A mill that cannot pay goods + freight waits loudly.");
            Assert.IsTrue(refusal.Contains("holds 100c") || refusal.Contains("134c"), "The refusal names the shortfall: " + refusal);
            AssertNothingMoved(world, millCash: 100);
        }

        [Test]
        public void C7_StoreShortfall_Refuses_NothingMoves()
        {
            World world = NewWorld(storeFlourSacks: 2);

            string refusal = Execute(world);

            Assert.IsNotNull(refusal, "A store short of flour refuses before anything moves.");
            Assert.IsTrue(refusal.Contains("shortfall") || refusal.Contains("prove"), "The refusal names the shortfall: " + refusal);
            AssertNothingMoved(world, storeFlourSacks: 2);
        }

        [Test]
        public void C7_ZeroPrice_Refuses()
        {
            World world = NewWorld();
            var execution = MakeExecution(world);
            execution.UnitPriceCents = 0;

            string refusal = world.Service.ExecuteSupplyOrder(
                MakeRequest(world), execution, Day, world.Diagnostics);

            Assert.IsNotNull(refusal, "A zero price refuses — no free flour.");
            Assert.IsTrue(refusal.Contains("price"), refusal);
            AssertNothingMoved(world);
        }

        [Test]
        public void C7_NoWagon_Refuses()
        {
            World world = NewWorld();
            var execution = MakeExecution(world);
            execution.Wagon = null;

            string refusal = world.Service.ExecuteSupplyOrder(
                MakeRequest(world), execution, Day, world.Diagnostics);

            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("wagon"), refusal);
            AssertNothingMoved(world);
        }

        [Test]
        public void C7_NoRateCard_Refuses()
        {
            World world = NewWorld();
            var execution = MakeExecution(world);
            execution.RateCard = null;

            string refusal = world.Service.ExecuteSupplyOrder(
                MakeRequest(world), execution, Day, world.Diagnostics);

            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("rate card"), refusal);
            AssertNothingMoved(world);
        }

        [Test]
        public void C7_NoCarrier_Refuses()
        {
            World world = NewWorld();
            var request = MakeRequest(world);
            request.CarrierLabel = string.Empty;

            string refusal = world.Service.ExecuteSupplyOrder(
                request, MakeExecution(world), Day, world.Diagnostics);

            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("carrier"), refusal);
            AssertNothingMoved(world);
        }

        [Test]
        public void C7_CarrierCreditRefused_FareRefunded_RetryChargesOnce()
        {
            World world = NewWorld();
            world.CarrierCash.RefuseCredit = true;

            string refusal = Execute(world);

            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("refunded"), "The refusal names the refund: " + refusal);
            Assert.AreEqual(15000, world.MillCash.BalanceCents, "The mill is refunded the trip fare in full.");
            Assert.AreEqual(0, world.CarrierCash.BalanceCents);
            Assert.AreEqual(0, world.Freight.TrackedShipments.Count, "A payment refusal leaves no shipment behind.");
            AssertNothingMoved(world);

            world.CarrierCash.RefuseCredit = false;
            Assert.IsNull(Execute(world), string.Join("; ", world.Diagnostics));
            Assert.AreEqual(15000 - OrderTotalCents, world.MillCash.BalanceCents, "The delivery charge settles once, on the retry.");
            Assert.AreEqual(TwoMileTripChargeCents, world.CarrierCash.BalanceCents);
            Assert.AreEqual(1, world.Freight.TrackedShipments.Count);
            Assert.AreEqual("campsup-0", world.Freight.TrackedShipments[0].ShipmentId,
                "The retry mints the same first id — the failed attempt left no shipment behind.");
            Assert.AreEqual(15f, world.Camp.SupplyLedger.Balance);
        }

        [Test]
        public void C7_MillDebitRefused_NothingMoves()
        {
            World world = NewWorld();
            world.MillCash.RefuseDebit = true;

            string refusal = Execute(world);

            Assert.IsNotNull(refusal, "A refused mill debit is a refused order.");
            Assert.IsTrue(refusal.Contains("unpaid") || refusal.Contains("refused"), refusal);
            AssertNothingMoved(world);
        }

        [Test]
        public void C7_WagonConflict_PreDeparture_FareRefunded_GoodsUnpaid()
        {
            World world = NewWorld();
            world.Freight.RegisterWagon(world.Wagon);
            var blockerHooks = new FreightShipmentHooks
            {
                Tasks = world.Tasks,
                Budgets = world.Budgets,
                Schedules = world.Schedules,
                DayIndex = Day,
                Diagnostics = world.Diagnostics,
            };
            LogisticsShipmentState blocker = world.Freight.CreateShipment(
                "blocker-1", StoreId, MillId, 1, LoggingCampSupplyService.FlourSackSpec,
                world.Wagon.AssetId, blockerHooks, out string blockerReason);
            Assert.IsNotNull(blocker, "The blocker shipment is live on the wagon: " + blockerReason);

            string refusal = Execute(world);

            Assert.IsNotNull(refusal, "A wagon already hauling refuses the trip before it runs.");
            Assert.IsTrue(refusal.Contains("refunded"), "The refusal names the fare refund: " + refusal);
            Assert.AreEqual(15000, world.MillCash.BalanceCents, "The fare is refunded in full — the trip never departed.");
            Assert.AreEqual(0, world.CarrierCash.BalanceCents);
            Assert.AreEqual(1, world.Freight.TrackedShipments.Count, "Only the blocker shipment exists — no campsup shipment.");
            Assert.AreEqual("blocker-1", world.Freight.TrackedShipments[0].ShipmentId);
            Assert.AreEqual(5000, world.StoreRuntime.CurrentCashCents, "Goods never paid — the shipment was never created.");
            Assert.AreEqual(48, world.StoreRuntime.GetRetailItemStockUnits("flour", "staple_food"));
            Assert.AreEqual(0f, world.Camp.SupplyLedger.Balance);
        }

        [Test]
        public void C7_DriverConflict_AfterPayment_FareRefunded_GoodsQuarantined()
        {
            World world = NewWorld();
            world.Freight.RegisterWagon(world.Wagon);
            world.Freight.RegisterWagon(world.BlockerWagon);
            var blockerHooks = new FreightShipmentHooks
            {
                Tasks = world.Tasks,
                Budgets = world.Budgets,
                Schedules = world.Schedules,
                DayIndex = Day,
                Diagnostics = world.Diagnostics,
            };
            LogisticsShipmentState blocker = world.Freight.CreateShipment(
                "blocker-1", StoreId, MillId, 1, LoggingCampSupplyService.FlourSackSpec,
                world.BlockerWagon.AssetId, blockerHooks, out string blockerReason);
            Assert.IsNotNull(blocker, "The blocker shipment is created: " + blockerReason);
            Assert.IsTrue(world.Freight.BeginLoading(blocker, EntityId.For(EntityKind.Person, 44), blockerHooks, out string loadReason),
                "Blocker loads: " + loadReason);
            int guard = 0;
            while (!blocker.loadApplied && guard++ < 100)
                Assert.IsTrue(world.Freight.WorkLoading(blocker, 60, blockerHooks, out string workReason), "Blocker loading works: " + workReason);
            Assert.IsTrue(blocker.loadApplied, "Blocker loaded.");
            Assert.IsTrue(world.Freight.Dispatch(blocker, EntityId.For(EntityKind.Person, 42),
                new List<EntityId> { EntityId.For(EntityKind.Animal, 2) }, blockerHooks, out string dispatchReason),
                "Blocker dispatches with driver 42: " + dispatchReason);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, blocker.Status, "The blocker is live in transit with driver 42 bound.");

            string refusal = Execute(world);

            Assert.IsNotNull(refusal, "A driver already hauling refuses the dispatch — after the goods are paid.");
            Assert.AreEqual(15000 - GoodsPerOrderCents, world.MillCash.BalanceCents,
                "The fare is refunded (the trip never departed) but the goods stay paid — the flour is quarantined, not refunded.");
            Assert.AreEqual(0, world.CarrierCash.BalanceCents, "The carrier keeps nothing for a trip that never departed.");
            Assert.AreEqual(5000 + GoodsPerOrderCents, world.StoreRuntime.CurrentCashCents,
                "The store keeps the goods money — it sold flour into the shipment's custody.");
            Assert.AreEqual(48 - SacksPerOrder, world.StoreRuntime.GetRetailItemStockUnits("flour", "staple_food"));
            Assert.AreEqual(0f, world.Camp.SupplyLedger.Balance, "Nothing reaches the camp.");
            Assert.AreEqual(2, world.Freight.TrackedShipments.Count);
            LogisticsShipmentState failed = world.Freight.TrackedShipments[1];
            Assert.AreEqual("campsup-0", failed.ShipmentId);
            Assert.AreEqual(LogisticsShipmentStatus.Failed, failed.Status,
                "The trip failed in transit: the shipment is abandoned as Failed with its cargo quarantined — never falsely delivered.");
            Assert.IsTrue(failed.BlockedReason.Contains("STRANDED CARGO"),
                "The failure names the quarantined cargo: " + failed.BlockedReason);
            Assert.AreEqual(SacksPerOrder, failed.RemainingQuantityUnits, "The 3 sacks stay quarantined on the failed shipment.");
        }

        [Test]
        public void C7_ReceiverRefusal_StruckCamp_And_AnonymousSupplier()
        {
            World struckWorld = NewWorld();
            Assert.IsNull(struckWorld.Camp.StrikeCamp(Day, "test strike", struckWorld.Diagnostics));

            int accepted = struckWorld.Service.ReceiveCampDelivery(
                null, 3, StoreId, Day, struckWorld.Diagnostics, out string struckReason);

            Assert.AreEqual(0, accepted, "A struck camp receives nothing.");
            Assert.IsTrue(struckReason.Contains("struck"), struckReason);

            World world = NewWorld();
            Assert.IsNull(Execute(world), string.Join("; ", world.Diagnostics));
            LogisticsShipmentState delivered = world.Freight.TrackedShipments[0];

            int anonymous = world.Service.ReceiveCampDelivery(
                delivered, 3, string.Empty, Day, world.Diagnostics, out string anonymousReason);

            Assert.AreEqual(0, anonymous, "Provisions never arrive anonymously.");
            Assert.IsTrue(anonymousReason.Contains("supplier"), anonymousReason);
            Assert.AreEqual(15f, world.Camp.SupplyLedger.Balance, "The anonymous attempt adds nothing to the ledger.");
        }

        [Test]
        public void C7_Consume_Then_Reorder_SecondOrder()
        {
            World world = NewWorld();
            Assert.IsNull(Execute(world), string.Join("; ", world.Diagnostics));
            Assert.IsNull(world.Camp.SupplyLedger.Consume(10, Day + 1, world.Diagnostics), "Ten units eaten over the following days.");
            Assert.AreEqual(5f, world.Camp.SupplyLedger.Balance);
            Assert.IsTrue(LoggingCampSupplyService.ShouldOrder(world.Camp), "5 units covers fewer than 3 days — reorder.");

            string refusal = world.Service.ExecuteSupplyOrder(
                MakeRequest(world), MakeExecution(world), Day + 2, world.Diagnostics);

            Assert.IsNull(refusal, string.Join("; ", world.Diagnostics));
            Assert.AreEqual(15000 - OrderTotalCents - (2 * FlourPriceCents + TwoMileTripChargeCents),
                world.MillCash.BalanceCents, "Second order: 2 sacks (70c goods) + 29c freight.");
            Assert.AreEqual(2 * TwoMileTripChargeCents, world.CarrierCash.BalanceCents);
            Assert.AreEqual(5000 + GoodsPerOrderCents + 2 * FlourPriceCents, world.StoreRuntime.CurrentCashCents);
            Assert.AreEqual(48 - SacksPerOrder - 2, world.StoreRuntime.GetRetailItemStockUnits("flour", "staple_food"));
            Assert.AreEqual(15f, world.Camp.SupplyLedger.Balance, "Balance restored to full 7-day cover.");
            Assert.AreEqual(2, world.Camp.SupplyLedger.Deliveries.Count, "Both deliveries are recorded, each naming the store.");
            Assert.AreEqual(StoreId, world.Camp.SupplyLedger.Deliveries[1].SupplierId);
            Assert.AreEqual(2, world.Freight.TrackedShipments.Count);
            Assert.AreEqual("campsup-0", world.Freight.TrackedShipments[0].ShipmentId);
            Assert.AreEqual("campsup-1", world.Freight.TrackedShipments[1].ShipmentId,
                "The second order mints the next sequence id — never re-issuing campsup-0.");
        }

        [Test]
        public void C7_SaveLoad_RoundTrip_PreservesLedgerAndCrew()
        {
            World world = NewWorld();
            Assert.IsNull(Execute(world), string.Join("; ", world.Diagnostics));

            var restored = new LoggingCampSite("x", "y", "z", 0);
            restored.LoadFromSaveDto(world.Camp.CaptureSaveDto());

            Assert.AreEqual(CampId, restored.CampId);
            Assert.AreEqual(MillId, restored.BusinessInstanceId);
            Assert.AreEqual(StandId, restored.StandId);
            Assert.AreEqual(2, restored.CrewHeadcount, "The roster survives the round trip.");
            Assert.AreEqual(15f, restored.SupplyLedger.Balance, "The ledger balance survives the round trip.");
            Assert.AreEqual(1, restored.SupplyLedger.Deliveries.Count, "The delivery record survives the round trip.");
            Assert.AreEqual(StoreId, restored.SupplyLedger.Deliveries[0].SupplierId);
            Assert.AreEqual(15f, restored.SupplyLedger.Deliveries[0].ProvisionUnits);
            Assert.AreEqual(Day, restored.SupplyLedger.Deliveries[0].DayIndex);
            Assert.AreEqual(1f, restored.Parameters.ProvisionsPerHeadPerDay);
            Assert.IsTrue(!LoggingCampSupplyService.ShouldOrder(restored), "The restored camp is supplied — no duplicate order.");
        }
    }
}
