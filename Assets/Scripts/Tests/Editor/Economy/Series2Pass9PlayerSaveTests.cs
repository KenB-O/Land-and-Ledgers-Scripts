using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.FreightFuel;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Ordering;
using LandLedgers.Orchestration.Player;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Tests.Economy
{
    /// <summary>
    /// Series 2 Pass 9 tests: the production business cash port, the
    /// Pass 9 save round-trip (Pass 2/3/4/6/8), the player duty
    /// binding, and ledger-paid auto-care. Roslyn harness only.
    /// No Unity compilation, Test Runner, PlayMode, or scene
    /// wiring is claimed by this file.
    /// </summary>
    [TestFixture]
    public sealed class Series2Pass9PlayerSaveTests
    {
        // ---- helpers -------------------------------------------------

        private sealed class FakeTradeCreditPort : ITradeCreditPort
        {
            public string ObligationIdToReturn = "obl-test-1";

            public string CreateTradeCredit(
                string debtor, string creditor, int amountCents, int dayIndex,
                string terms, string reason, string agreementId, string paymentId)
            {
                return ObligationIdToReturn;
            }
        }

        private sealed class FakeFoodSeller : IPass4FoodSeller
        {
            public string LocationId { get; set; } = "shop-1";
            public bool IsOpen { get; set; } = true;
            public bool HasFood { get; set; } = true;
            public int FoodPriceCents { get; set; } = 40;
            public bool OffersTradeCredit { get; set; } = false;
            public int TradeCreditLimitCents { get; set; } = 0;
        }

        private sealed class FakeLodging : IPass4Lodging
        {
            public string LocationId { get; set; } = "inn-1";
            public bool IsAvailable { get; set; } = true;
            public int NightPriceCents { get; set; } = 60;
            public bool OffersTradeCredit { get; set; } = false;
            public int TradeCreditLimitCents { get; set; } = 0;
        }

        private sealed class FakeRoute : IPass4RouteProvider
        {
            public int Minutes = 15;

            public bool TryRoute(string fromLocationId, string toLocationId, out int routeMinutes, out string reason)
            {
                routeMinutes = Minutes;
                reason = null;
                return true;
            }
        }

        private sealed class FakeMigrationHolder : IPassXlMigrationFlagHolder
        {
            public bool SuppressMigrantArrivals { get; set; }
        }

        private sealed class FakePlayerHolder : IPassXlPlayerFlagHolder
        {
            public bool TreatPlayerAsNpc { get; set; }
        }

        private static HouseholdLedger NewLedger(int cents)
        {
            var ledger = new HouseholdLedger(1);
            Assert.IsNull(ledger.RecordInflow(
                0, cents, HouseholdIncomeSource.WageEmployment,
                "emp-1", "test wage", "employer"), "test wage inflow must be accepted");
            return ledger;
        }

        private static TaskAuthority NewAuthority()
        {
            var authority = new TaskAuthority();
            string rejection;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("test.sweep", "Sweep the store", 30), out rejection), "sweep definition must register: " + rejection);
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("test.cook", "Cook a meal", 45), out rejection), "cook definition must register: " + rejection);
            return authority;
        }

        private static Pass4PlayerDuty NewDuty(List<string> diag)
        {
            var duty = new Pass4PlayerDuty("player-1", "home-1", 600);
            Assert.IsTrue(duty.SetDuty(true, diag), "duty toggle must switch on; diag: " + string.Join("; ", diag));
            Assert.IsTrue(duty.AddDutyTask("test.sweep", 30, diag));
            Assert.IsTrue(duty.AddDutyTask("test.cook", 45, diag));
            return duty;
        }

        private static Series2Pass9SaveState NewState()
        {
            var state = new Series2Pass9SaveState();
            state.Catalog = new Pass3StoreCatalog();
            return state;
        }

        // ---- A. production cash port --------------------------------

        [Test]
        public void ProductionCashPort_CreditNonNegative_IsInfallible_AndDebitRefusesShortfall()
        {
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("cash-port-test-business");
            runtime.AddCashCents(1000);
            var port = new Series2BusinessRuntimeCashPort(runtime);

            Assert.AreEqual("cash-port-test-business", port.BusinessId,
                "the identity cash port must report the wrapped authoritative runtime identity");

            // Pass 7's proof: Credit for a non-negative amount on a
            // non-null runtime has no failure path in the runtime.
            Assert.IsNull(port.Credit(250, "settlement revenue"));
            Assert.AreEqual(1250, port.BalanceCents);
            Assert.IsNull(port.Credit(0, "zero credit is a no-op, not a failure"));
            Assert.AreEqual(1250, port.BalanceCents);
            Assert.IsNull(port.TryDebit(200, "settlement payment"));
            Assert.AreEqual(1050, port.BalanceCents);

            // Debits still refuse: shortfall, negatives, null runtime.
            Assert.IsNotNull(port.TryDebit(9999, "cannot cover"));
            Assert.AreEqual(1050, port.BalanceCents, "failed debit moves nothing");
            Assert.IsNotNull(port.Credit(-50, "negative credit is refused"));
            Assert.IsNotNull(port.TryDebit(-50, "negative debit is refused"));
            Assert.AreEqual(1050, port.BalanceCents);

            var nullPort = new Series2BusinessRuntimeCashPort(null);
            Assert.IsNotNull(nullPort.Credit(10, "null runtime cannot credit"));
            Assert.IsNotNull(nullPort.TryDebit(10, "null runtime cannot debit"));
            Assert.AreEqual(0, nullPort.BalanceCents);
        }

        // ---- B. Pass 9 save round-trip: Pass 2 ----------------------

        [Test]
        public void Save_RoundTrip_Pass2_SequenceAndReport_Idempotent()
        {
            var diag = new List<string>();
            var day = new Pass2TaskSequenceDay();
            day.AddTask("sweep", 30);
            day.MarkCompleted("sweep");
            day.AddTask("cook", 45);
            day.MarkMissed("cook", "no time");
            var report = new Pass2MissedWorkReport();
            report.AddMissed("cook", "player-1", "no time");
            report.RecordIdleMinutes(30);

            var source = NewState();
            source.SequenceDay = day;
            source.MissedReport = report;
            Series2Pass9SaveDto dto = source.Capture(diag);
            source.Restore(dto, null, diag);

            var target = NewState();
            target.SequenceDay = new Pass2TaskSequenceDay();
            target.MissedReport = new Pass2MissedWorkReport();
            target.Restore(dto, null, diag);
            Assert.AreEqual(2, target.SequenceDay.Entries.Count);
            Assert.AreEqual(1, target.SequenceDay.Entries.Count(e => e.Completed));
            Assert.AreEqual(1, target.SequenceDay.Entries.Count(e => !e.Completed));
            Assert.AreEqual("no time", target.SequenceDay.Entries.First(e => !e.Completed).MissedReason);
            Assert.AreEqual(1, target.MissedReport.Items.Count);
            Assert.AreEqual(30, target.MissedReport.IdleMinutes);
            Assert.AreEqual("cook", target.MissedReport.Items[0].TaskDefinitionId);

            // Idempotent restore: a second restore replaces, never doubles.
            target.Restore(dto, null, diag);
            Assert.AreEqual(2, target.SequenceDay.Entries.Count);
            Assert.AreEqual(1, target.MissedReport.Items.Count);
            Assert.AreEqual(30, target.MissedReport.IdleMinutes);
        }

        // ---- C. Pass 9 save round-trip: Pass 3 ----------------------

        [Test]
        public void Save_RoundTrip_Pass3_DraftsOrdersFuelWholesale_DeliveredSetSurvives()
        {
            var diag = new List<string>();
            var catalog = new Pass3StoreCatalog();
            Assert.IsTrue(catalog.AddProduct(new Pass3ProductLine
            {
                ProductId = "bread", DisplayName = "Bread", CostCents = 80, PriceCents = 100,
            }, diag), "bread line must be added; diag: " + string.Join("; ", diag));
            catalog.Find("bread").ShelfUnits = 5;
            Assert.IsTrue(catalog.AddProduct(new Pass3ProductLine
            {
                ProductId = "flour", DisplayName = "Flour", CostCents = 10, PriceCents = 20,
            }, diag), "flour line must be added; diag: " + string.Join("; ", diag));
            catalog.Find("flour").BackUnits = 10;

            var supplier = new Pass3SupplierOffer
            {
                SupplierId = "grain-1",
                SupplierName = "Grain Co.",
                DistanceMiles = 10,
                MinimumQuantityUnits = 1,
            };
            supplier.SkuOffers.Add(new Pass3SkuOffer { SkuId = "flour", UnitPriceCents = 10, UnitWeightLb = 10 });
            supplier.SkuOffers.Add(new Pass3SkuOffer { SkuId = "input-log", UnitPriceCents = 15, UnitWeightLb = 20 });
            var draft = new Pass3OrderDraft
            {
                Supplier = supplier,
                FreightOption = Pass3FreightOption.SellerFreight,
                FreightCentsPerMile = 5,
            };
            Assert.IsTrue(draft.AddLine("flour", 20, diag), "draft line must be added; diag: " + string.Join("; ", diag));
            List<Pass3OrderRequest> placed = draft.Place(diag);
            Assert.IsNotNull(placed, "draft must place; diag: " + string.Join("; ", diag));
            Assert.AreEqual(1, placed.Count);

            Pass3FuelInputOrder fuelOrder = Pass3FuelInputOrder.Create(
                "dealer-1", supplier, "input-log", 10,
                Pass3FreightOption.SellerFreight, 0, 0, 2.0, diag);
            Assert.IsNotNull(fuelOrder, "fuel input order must be created; diag: " + string.Join("; ", diag));
            fuelOrder.InputReceived = true;
            fuelOrder.InputLotId = "inlot-1";

            var intakeCatalog = new Pass3StoreCatalog();
            var intake = new Pass3WholesaleIntake();
            Assert.IsNotNull(intake.Receive("lot-1", "bread", 10, 5, 0, 0, intakeCatalog,
                "general-store-1", "bakery-1", diag),
                "intake must accept lot-1; diag: " + string.Join("; ", diag));

            var settled = new Pass3WholesaleSettlement
            {
                BakeryLotId = "lot-1",
                ProductId = "bread",
                Units = 10,
                UnitWholesalePriceCents = 50,
                PayerBusinessId = "bakery-1",
                PayeeBusinessId = "general-store-1",
                AmountCents = 500,
                Settled = true,
            };
            var unsettled = new Pass3WholesaleSettlement
            {
                BakeryLotId = "lot-2",
                ProductId = "bread",
                Units = 6,
                UnitWholesalePriceCents = 50,
                PayerBusinessId = "bakery-1",
                PayeeBusinessId = "general-store-1",
                AmountCents = 300,
                Settled = false,
            };

            var source = NewState();
            source.Catalog = catalog;
            source.Drafts.Add(draft);
            source.PlacedRequests.AddRange(placed);
            source.FuelInputOrders.Add(fuelOrder);
            source.WholesaleIntake = intake;
            source.WholesaleSettlements.Add(settled);
            source.WholesaleSettlements.Add(unsettled);
            Series2Pass9SaveDto dto = source.Capture(diag);

            var target = NewState();
            target.WholesaleIntake = new Pass3WholesaleIntake();
            target.Restore(dto, null, diag);

            Assert.AreEqual(100, target.Catalog.Find("bread").PriceCents);
            Assert.AreEqual(5, target.Catalog.Find("bread").ShelfUnits);
            Assert.AreEqual(10, target.Catalog.Find("flour").BackUnits);
            Assert.AreEqual(1, target.Drafts.Count);
            Assert.AreEqual("grain-1", target.Drafts[0].Supplier.SupplierId);
            Assert.AreEqual(1, target.Drafts[0].Lines.Count);
            Assert.AreEqual(20, target.Drafts[0].Lines[0].QuantityUnits);
            Assert.AreEqual(1, target.PlacedRequests.Count);
            Assert.AreEqual("grain-1", target.PlacedRequests[0].SupplierId);
            Assert.AreEqual("flour", target.PlacedRequests[0].SkuId);
            Assert.AreEqual(20, target.PlacedRequests[0].QuantityUnits);
            Assert.AreEqual(1, target.FuelInputOrders.Count);
            Assert.AreEqual("dealer-1", target.FuelInputOrders[0].DealerBusinessId);
            Assert.AreEqual(2.0, target.FuelInputOrders[0].ConversionRateFuelPerInputUnit);
            Assert.IsTrue(target.FuelInputOrders[0].InputReceived);
            Assert.AreEqual("inlot-1", target.FuelInputOrders[0].InputLotId);
            Assert.AreEqual(2, target.WholesaleSettlements.Count);
            Assert.IsTrue(target.WholesaleSettlements.First(s => s.BakeryLotId == "lot-1").Settled,
                "settled flag must survive the round-trip");
            Assert.IsFalse(target.WholesaleSettlements.First(s => s.BakeryLotId == "lot-2").Settled);

            // The adopted delivered set refuses a re-intake: a reload
            // can never re-intake a lot it already received.
            Assert.IsNull(target.WholesaleIntake.Receive(
                "lot-1", "bread", 10, 5, 0, 0, new Pass3StoreCatalog(),
                "general-store-1", "bakery-1", diag),
                "delivered lot-1 must be refused after restore");

            // And the restored settled record refuses a second settlement.
            var payer = new BusinessRuntimeState();
            payer.BindBusinessIdentity("bakery-1");
            payer.AddCashCents(5000);
            var payee = new BusinessRuntimeState();
            payee.BindBusinessIdentity("general-store-1");
            var result = Series2WholesaleSettlement.SettleCash(
                target.WholesaleSettlements.First(s => s.BakeryLotId == "lot-1"),
                new Series2BusinessRuntimeCashPort(payer),
                new Series2BusinessRuntimeCashPort(payee), diag);
            Assert.IsFalse(result.Settled, "already-settled record must refuse a second settlement");
            Assert.AreEqual(5000, payer.CurrentCashCents, "refused settlement moves nothing");
        }

        // ---- D. Pass 9 save round-trip: Pass 4 ----------------------

        [Test]
        public void Save_RoundTrip_Pass4_DutyAndAutoCare_Idempotent()
        {
            var diag = new List<string>();
            var duty = NewDuty(diag);
            Assert.IsTrue(duty.StartDutyTask("test.sweep", diag));
            Assert.IsTrue(duty.CompleteDutyTask("test.sweep", diag));
            Assert.IsTrue(duty.MissDutyTask("test.cook", "no time", diag));
            Assert.IsTrue(duty.AddDutyTask("test.errand", 20, diag));
            Assert.IsTrue(duty.OffloadTask("test.errand", "emp-1", "backlog 20 minutes", diag));
            duty.CashCents = 123;

            var autoEat = new Pass4AutoEat("home-1", 200);
            autoEat.SetToggle(true, diag);
            var seller = new FakeFoodSeller();
            Assert.IsTrue(autoEat.Start("hungry", diag));
            Assert.IsTrue(autoEat.StepFindSeller(seller, diag));
            Assert.IsTrue(autoEat.StepTravel(new FakeRoute(), diag));

            var autoSleep = new Pass4AutoSleep("home-1", 200);
            autoSleep.SetToggle(true, diag);
            var lodging = new FakeLodging();
            Assert.IsTrue(autoSleep.Start("tired", diag));
            Assert.IsTrue(autoSleep.StepFindLodging(lodging, diag));
            Assert.IsTrue(autoSleep.StepTravel(new FakeRoute(), diag));

            var source = NewState();
            source.Duty = duty;
            source.AutoEat = autoEat;
            source.AutoSleep = autoSleep;
            Series2Pass9SaveDto dto = source.Capture(diag);

            var target = NewState();
            target.Duty = new Pass4PlayerDuty("player-1", "home-1", 600);
            target.AutoEat = new Pass4AutoEat("home-1", 200);
            target.AutoSleep = new Pass4AutoSleep("home-1", 200);
            target.Restore(dto, null, diag);

            Assert.AreEqual(2, target.Duty.Tasks.Count, "sweep + cook stay on the roster; errand moves to offloaded");
            Assert.AreEqual(1, target.Duty.OffloadedTasks.Count);
            Assert.AreEqual("test.errand", target.Duty.OffloadedTasks[0].TaskDefinitionId);
            Assert.AreEqual(123, target.Duty.CashCents);
            Assert.AreEqual(1, target.Duty.Tasks.Count(t => t.Completed), "sweep completed");
            Assert.AreEqual(1, target.Duty.Tasks.Count(t => !string.IsNullOrEmpty(t.MissedReason)), "cook missed");
            Assert.AreEqual(Pass4AutoEatState.AtSeller, target.AutoEat.State);
            Assert.AreEqual("shop-1", target.AutoEat.SellerLocationId);
            Assert.AreEqual(Pass4AutoSleepState.AtLodging, target.AutoSleep.State);
            Assert.AreEqual("inn-1", target.AutoSleep.LodgingLocationId);

            // A restored auto-eat at a seller with no seller wired
            // fails safely: no cash moves, state stays AtSeller.
            Assert.IsFalse(target.AutoEat.StepEat(diag));
            Assert.AreEqual(200, target.AutoEat.CashCents);

            // Idempotent: a second restore replaces, never doubles.
            target.Restore(dto, null, diag);
            Assert.AreEqual(2, target.Duty.Tasks.Count);
            Assert.AreEqual(1, target.Duty.OffloadedTasks.Count);
        }

        // ---- E. Pass 9 save round-trip: Pass 6 ----------------------

        [Test]
        public void Save_RoundTrip_Pass6_PlannedDayAndBatchIds_Idempotent()
        {
            var diag = new List<string>();
            var bakery = new BakeryShopRuntime("pierce-bakery-1");
            Assert.IsNull(bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, diag), "oven installation should succeed; diag: " + string.Join("; ", diag));
            var driver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, diag),
                "planning should succeed; diag: " + string.Join("; ", diag));

            var source = NewState();
            source.BakeryDriver = driver;
            Series2Pass9SaveDto dto = source.Capture(diag);
            Assert.AreEqual(2, dto.BakeryPlannedDayIndex);
            Assert.AreEqual(1, dto.BakeryPlannedBatchIds.Count);

            // Restore into a fresh driver over the same bakery runtime
            // (the runtime itself is saved separately by the bakery DTO).
            var target = NewState();
            target.BakeryDriver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));
            target.Restore(dto, null, diag);
            Assert.AreEqual(2, target.BakeryDriver.PlannedDayIndex);
            Assert.AreEqual(1, target.BakeryDriver.PlannedBatchIds.Count);
            Assert.AreEqual(dto.BakeryPlannedBatchIds[0], target.BakeryDriver.PlannedBatchIds[0]);

            target.Restore(dto, null, diag);
            Assert.AreEqual(2, target.BakeryDriver.PlannedDayIndex);
            Assert.AreEqual(1, target.BakeryDriver.PlannedBatchIds.Count);
        }

        // ---- F. Pass 9 save round-trip: Pass 8 ----------------------

        [Test]
        public void Save_RoundTrip_Pass8_FuelLotsAndCarrierSettlement_NoDoubleCollect()
        {
            var diag = new List<string>();
            var intake = new FuelYardByproductIntake();
            Assert.IsNull(intake.ReceiveByproductLot("dealer-1", new SlabOffcutFuelLot
            {
                LotId = EntityId.For(EntityKind.Lot, 11),
                FuelWoodUnits = 30,
                SourceMillBusinessId = "mill-1",
                SourceLogLotId = "log-1",
                StandId = "stand-1",
                ProducedDayIndex = 0,
            }, diag), "fuel lot 1 should be received; diag: " + string.Join("; ", diag));
            Assert.IsNull(intake.ReceiveByproductLot("dealer-1", new SlabOffcutFuelLot
            {
                LotId = EntityId.For(EntityKind.Lot, 12),
                FuelWoodUnits = 20,
                SourceMillBusinessId = "mill-1",
                SourceLogLotId = "log-1",
                StandId = "stand-1",
                ProducedDayIndex = 0,
            }, diag), "fuel lot 2 should be received; diag: " + string.Join("; ", diag));

            var shipment = new LogisticsShipmentState
            {
                shipmentId = "ship-1",
                state = LogisticsShipmentStatus.Completed,
                carrierBusinessInstanceId = "carrier-1",
                freightPayerBusinessInstanceId = "payer-1",
                freightChargeCents = 100,
                deliveryApplied = true,
                freightRevenueCollected = true,
            };
            var freightServiceDto = new FreightShipmentService.FreightServiceSaveDto();
            freightServiceDto.Shipments.Add(shipment.CaptureSaveDto());
            var sourceDriver = new FreightProductionDriver();
            sourceDriver.RestoreSaveDto(freightServiceDto, null, diag);

            var dealer = new Series2FuelDealer(intake, "dealer-1");
            var source = NewState();
            source.FuelIntake = intake;
            source.FreightDriver = sourceDriver;
            source.FuelSource = new Series2DealerBakeryFuelSource(
                dealer, 2, (stock, units, day, ids, d) => null);
            Series2Pass9SaveDto dto = source.Capture(diag);

            var target = NewState();
            target.FuelIntake = new FuelYardByproductIntake();
            target.FreightDriver = new FreightProductionDriver();
            target.Restore(dto, null, diag);

            Assert.AreEqual(50, target.FuelIntake.TotalFuelWoodUnitsFor("dealer-1"),
                "fuel intake lots must survive the round-trip, not double or vanish");
            Assert.IsNotNull(target.RestoredFuelSourceParams);
            Assert.AreEqual("dealer-1", target.RestoredFuelSourceParams.DealerBusinessId);
            Assert.AreEqual(2, target.RestoredFuelSourceParams.IntakeUnitsPerBakeryUnit);
            Assert.IsTrue(target.RestoredFuelSourceParams.HadOffMapReceipt);

            // Restore twice: fuel stock must not double.
            target.Restore(dto, null, diag);
            Assert.AreEqual(50, target.FuelIntake.TotalFuelWoodUnitsFor("dealer-1"));

            // The restored flagged shipment rebuilds the settled set,
            // and a second collection attempt is refused with no cash moved.
            var restored = target.FreightDriver.AllShipments();
            Assert.AreEqual(1, restored.Count);
            Assert.IsTrue(restored[0].FreightRevenueCollected,
                "the persisted flag, not a second set, is the collection authority");
            var settlement = new Series2CarrierSettlement();
            settlement.RestoreFromShipments(restored);
            Assert.IsTrue(settlement.AlreadySettled("ship-1"));
            var payer = new BusinessRuntimeState();
            payer.AddCashCents(500);
            var carrier = new BusinessRuntimeState();
            var settleDiag = new List<string>();
            Assert.IsFalse(settlement.SettleOnAcceptance(
                restored[0],
                new Series2BusinessRuntimeCashPort(payer),
                new Series2BusinessRuntimeCashPort(carrier),
                settleDiag).Settled);
            Assert.AreEqual(500, payer.CurrentCashCents);
            Assert.AreEqual(0, carrier.CurrentCashCents);
        }

        [Test]
        public void CarrierSettlement_UnflaggedShipment_SettlesOnce_WithProductionPort()
        {
            var shipment = new LogisticsShipmentState
            {
                shipmentId = "ship-2",
                state = LogisticsShipmentStatus.Completed,
                carrierBusinessInstanceId = "carrier-1",
                freightPayerBusinessInstanceId = "payer-1",
                freightChargeCents = 100,
                deliveryApplied = true,
                freightRevenueCollected = false,
            };
            var payer = new BusinessRuntimeState();
            payer.AddCashCents(500);
            var carrier = new BusinessRuntimeState();
            var settlement = new Series2CarrierSettlement();
            var diag = new List<string>();
            Assert.IsTrue(settlement.SettleOnAcceptance(
                shipment,
                new Series2BusinessRuntimeCashPort(payer),
                new Series2BusinessRuntimeCashPort(carrier),
                diag).Settled);
            Assert.AreEqual(400, payer.CurrentCashCents);
            Assert.AreEqual(100, carrier.CurrentCashCents);
            Assert.IsTrue(shipment.FreightRevenueCollected);
            Assert.IsFalse(settlement.SettleOnAcceptance(
                shipment,
                new Series2BusinessRuntimeCashPort(payer),
                new Series2BusinessRuntimeCashPort(carrier),
                diag).Settled);
            Assert.AreEqual(400, payer.CurrentCashCents);
            Assert.AreEqual(100, carrier.CurrentCashCents, "second collection moves nothing");
        }

        // ---- G. coordinator wiring -----------------------------------

        [Test]
        public void Coordinator_CaptureRestore_Pass9Section_AndOldSave_RestoresEmpty()
        {
            var diag = new List<string>();
            var duty = NewDuty(diag);
            Assert.IsTrue(duty.CompleteDutyTask("test.sweep", diag));
            var state = NewState();
            state.Duty = duty;
            PassXlSliceSaveDto dto = PassXlSliceSaveCoordinator.Capture(
                new FakeMigrationHolder(), new FakePlayerHolder(), null, state);
            Assert.IsNotNull(dto.series2Pass9, "coordinator must carry the Pass 9 section");
            Assert.IsNotNull(dto.series2Pass9.Duty, "duty section must be captured");

            var target = NewState();
            target.Duty = new Pass4PlayerDuty("player-1", "home-1", 600);
            List<string> restoreDiag = PassXlSliceSaveCoordinator.Restore(
                dto, new FakeMigrationHolder(), new FakePlayerHolder(), null, target, null);
            Assert.AreEqual(2, target.Duty.Tasks.Count, "sweep + cook roster restored");
            Assert.IsTrue(restoreDiag.Any(d => d.Contains("Series2Pass9Save: restore complete")),
                "coordinator restore must include the Pass 9 restore diagnostic; diag: " + string.Join("; ", restoreDiag));

            // An old save with no Pass 9 section restores empty, never crashes.
            // The duty instance is left untouched when its section is absent.
            var oldDto = new PassXlSliceSaveDto();
            var oldTarget = NewState();
            oldTarget.Duty = NewDuty(diag);
            PassXlSliceSaveCoordinator.Restore(
                oldDto, new FakeMigrationHolder(), new FakePlayerHolder(), null, oldTarget, null);
            Assert.AreEqual(2, oldTarget.Duty.Tasks.Count, "absent duty section leaves the instance untouched, never invents tasks");
        }

        // ---- H. player duty binding ----------------------------------

        [Test]
        public void PlayerBinding_IssueStartAdvance_CompletesBothPlaces_AndUnknownRefuses()
        {
            var diag = new List<string>();
            var authority = NewAuthority();
            var budgetStore = new WorkTimeBudgetStore();
            var player = EntityId.For(EntityKind.Person, 1);
            var duty = NewDuty(diag);
            var binding = new Series2PlayerDutyBinding(duty, authority, player, budgetStore, null);

            Assert.IsTrue(binding.IssueDutyTask("test.sweep", 0, diag));
            Assert.AreEqual(1, authority.GetOpenTasks(player).Count);
            Assert.IsTrue(binding.StartDutyTask("test.sweep", 0, diag));
            Assert.IsTrue(binding.AdvanceDutyTask("test.sweep", 30, 0, diag));
            Assert.AreEqual(0, authority.GetOpenTasks(player).Count,
                "completed task leaves the open queue");
            Assert.AreEqual(1, duty.Tasks.Count(t => t.Completed), "duty roster records the completion");

            // Advancing again fails: the task is no longer issued.
            Assert.IsFalse(binding.AdvanceDutyTask("test.sweep", 30, 0, diag));

            // An unregistered definition is refused, never invented.
            Assert.IsTrue(duty.AddDutyTask("unknown.task", 10, diag));
            Assert.IsFalse(binding.IssueDutyTask("unknown.task", 0, diag));
            Assert.AreEqual(0, authority.GetOpenTasks(player).Count);
        }

        [Test]
        public void PlayerBinding_NpcMode_RefusesIssue_AndRosterPreserved()
        {
            var diag = new List<string>();
            var authority = NewAuthority();
            var player = EntityId.For(EntityKind.Person, 1);
            var duty = NewDuty(diag);
            duty.SetNpcMode(true, diag);
            var binding = new Series2PlayerDutyBinding(
                duty, authority, player, new WorkTimeBudgetStore(), null);
            Assert.IsFalse(binding.IssueDutyTask("test.sweep", 0, diag));
            Assert.AreEqual(0, authority.GetOpenTasks(player).Count);
            Assert.AreEqual(2, duty.Tasks.Count, "NPC refusal must not consume roster entries");
        }

        [Test]
        public void PlayerBinding_Offload_OneTaskOneEmployee_NoDoubleCount()
        {
            var diag = new List<string>();
            var authority = NewAuthority();
            var budgetStore = new WorkTimeBudgetStore();
            var player = EntityId.For(EntityKind.Person, 1);
            var employee = EntityId.For(EntityKind.Person, 2);
            var duty = NewDuty(diag);
            var binding = new Series2PlayerDutyBinding(duty, authority, player, budgetStore, null);

            Assert.IsTrue(binding.IssueDutyTask("test.cook", 0, diag));
            Assert.IsTrue(binding.StartDutyTask("test.cook", 0, diag));
            Assert.IsTrue(binding.OffloadDutyTask("test.cook", "emp-1", employee, "backlog 45 minutes", 0, diag));

            Assert.AreEqual(0, authority.GetOpenTasks(player).Count,
                "player queue must not retain the offloaded task");
            Assert.AreEqual(1, authority.GetOpenTasks(employee).Count,
                "exactly one employee queue holds the task");
            Assert.AreEqual("test.cook", authority.GetOpenTasks(employee)[0].DefinitionId);
            Assert.AreEqual(1, duty.OffloadedTasks.Count, "roster counts the offload once");
            Assert.AreEqual(0, duty.Tasks.Count(t => t.Completed));
            Assert.AreEqual(0, duty.Tasks.Count(t => !string.IsNullOrEmpty(t.MissedReason)));

            // The offloaded task is not missed work. (The Pass 2
            // report counts unfinished roster work as missed, so
            // the unfinished sweep entry legitimately appears;
            // the offloaded cook entry must not.)
            Pass2MissedWorkReport report = duty.BuildMissedWorkReport(0);
            Assert.IsFalse(report.Items.Any(i => i.TaskDefinitionId == "test.cook"),
                "offloaded work must not appear in the missed-work report");
            string console = binding.RenderMissedWorkConsole(0);
            Assert.IsFalse(console.Contains("test.cook"),
                "offloaded work must not appear in the console report; report: " + console);
        }

        [Test]
        public void PlayerBinding_MissedWork_ReportViaPass2Api()
        {
            var diag = new List<string>();
            var authority = NewAuthority();
            var player = EntityId.For(EntityKind.Person, 1);
            var duty = NewDuty(diag);
            var binding = new Series2PlayerDutyBinding(
                duty, authority, player, new WorkTimeBudgetStore(), null);
            Assert.IsTrue(duty.StartDutyTask("test.sweep", diag));
            Assert.IsTrue(duty.CompleteDutyTask("test.sweep", diag));
            Assert.IsTrue(duty.MissDutyTask("test.cook", "no time", diag));

            Pass2MissedWorkReport report = duty.BuildMissedWorkReport(0);
            Assert.AreEqual(1, report.Items.Count);
            Assert.AreEqual("test.cook", report.Items[0].TaskDefinitionId);
            Assert.AreEqual("no time", report.Items[0].Reason);
            string console = binding.RenderMissedWorkConsole(0);
            Assert.IsTrue(console.Contains("test.cook"),
                "console report must name the missed task; report: " + console);
        }

        // ---- I. ledger-paid auto-care --------------------------------

        [Test]
        public void AutoCareBinding_EatAndSleep_PayFromLedger_NoFreeMealOrNight()
        {
            var diag = new List<string>();
            var ledger = NewLedger(200);
            var binding = new Series2AutoCareLedgerBinding();

            var autoEat = new Pass4AutoEat("home-1", 200);
            autoEat.SetToggle(true, diag);
            var seller = new FakeFoodSeller();
            Assert.IsTrue(autoEat.Start("hungry", diag));
            Assert.IsTrue(autoEat.StepFindSeller(seller, diag));
            Assert.IsTrue(autoEat.StepTravel(new FakeRoute(), diag));
            Assert.IsTrue(binding.PayAndEat(autoEat, seller, ledger, 0, "household-1", "shop-1", new FakeTradeCreditPort(), diag),
                "meal must pay; diag: " + string.Join("; ", diag));
            Assert.AreEqual(Pass4AutoEatState.Done, autoEat.State);
            Assert.AreEqual(160, ledger.GetBalanceCents(), "meal paid from the ledger, never free");

            var autoSleep = new Pass4AutoSleep("home-1", 200);
            autoSleep.SetToggle(true, diag);
            var lodging = new FakeLodging();
            Assert.IsTrue(autoSleep.Start("tired", diag));
            Assert.IsTrue(autoSleep.StepFindLodging(lodging, diag));
            Assert.IsTrue(autoSleep.StepTravel(new FakeRoute(), diag));
            Assert.IsTrue(binding.PayAndSleep(autoSleep, lodging, ledger, 0, "household-1", "inn-1", new FakeTradeCreditPort(), diag),
                "night must pay; diag: " + string.Join("; ", diag));
            Assert.AreEqual(Pass4AutoSleepState.Done, autoSleep.State);
            Assert.AreEqual(100, ledger.GetBalanceCents(), "night paid from the ledger, never free");
        }

        [Test]
        public void AutoCareBinding_InsufficientCash_FallsBackToCredit_ThenFailsKnown()
        {
            var diag = new List<string>();
            var ledger = NewLedger(10);
            var binding = new Series2AutoCareLedgerBinding();

            var creditSeller = new FakeFoodSeller { OffersTradeCredit = true, TradeCreditLimitCents = 100 };
            var autoEat = new Pass4AutoEat("home-1", 200);
            autoEat.SetToggle(true, diag);
            Assert.IsTrue(autoEat.Start("hungry", diag));
            Assert.IsTrue(autoEat.StepFindSeller(creditSeller, diag));
            Assert.IsTrue(autoEat.StepTravel(new FakeRoute(), diag));
            Assert.IsTrue(binding.PayAndEat(autoEat, creditSeller, ledger, 0, "household-1", "shop-1", new FakeTradeCreditPort(), diag),
                "cash 10c cannot pay 40c, but trade credit covers it; diag: " + string.Join("; ", diag));
            Assert.AreEqual(Pass4AutoEatState.Done, autoEat.State);
            Assert.AreEqual(10, ledger.GetBalanceCents(), "credit path moves no cash");

            var noCreditSeller = new FakeFoodSeller { OffersTradeCredit = false };
            var autoEat2 = new Pass4AutoEat("home-1", 200);
            autoEat2.SetToggle(true, diag);
            Assert.IsTrue(autoEat2.Start("hungry", diag));
            Assert.IsTrue(autoEat2.StepFindSeller(noCreditSeller, diag));
            Assert.IsTrue(autoEat2.StepTravel(new FakeRoute(), diag));
            Assert.IsFalse(binding.PayAndEat(autoEat2, noCreditSeller, ledger, 0, "household-1", "shop-1", new FakeTradeCreditPort(), diag),
                "no cash and no credit must fail, not invent a meal");
            Assert.AreEqual(Pass4AutoEatState.AtSeller, autoEat2.State,
                "failure leaves a known state: still at the seller");
            Assert.AreEqual(10, ledger.GetBalanceCents(), "failed payment moves nothing");
        }

        [Test]
        public void AutoCareBinding_ClosedSeller_RefusesWithoutPayment()
        {
            var diag = new List<string>();
            var ledger = NewLedger(200);
            var binding = new Series2AutoCareLedgerBinding();
            var autoEat = new Pass4AutoEat("home-1", 200);
            autoEat.SetToggle(true, diag);
            var seller = new FakeFoodSeller();
            Assert.IsTrue(autoEat.Start("hungry", diag));
            Assert.IsTrue(autoEat.StepFindSeller(seller, diag));
            Assert.IsTrue(autoEat.StepTravel(new FakeRoute(), diag));
            seller.IsOpen = false;
            Assert.IsFalse(binding.PayAndEat(autoEat, seller, ledger, 0, "household-1", "shop-1", new FakeTradeCreditPort(), diag),
                "a seller that closed after travel must refuse payment");
            Assert.AreEqual(200, ledger.GetBalanceCents());
        }
    }
}
