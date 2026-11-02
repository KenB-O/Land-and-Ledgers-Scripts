using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Businesses.FuelDealer;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.FreightFuel;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Ordering;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Series 2 Pass 8 (freight + fuel chain): Roslyn acceptance,
    /// production classes as the system under test. No orchestrator
    /// shortcuts, no direct inventory injection for sales: fuel
    /// moves through the real intake dispense API, bakery fuel
    /// through the real fuel store, carrier legs through the real
    /// FreightProductionDriver, and household fuel through the real
    /// Pass 7 adapter plus the Pass 8 reserve link. All prices,
    /// quantities, conversions, and freight charges are test
    /// parameters, never canon. Unity compilation, Test Runner,
    /// PlayMode, and scene wiring are NOT claimed.
    /// </summary>
    [TestFixture]
    public sealed class Series2Pass8FreightFuelTests
    {
        private sealed class RuntimeCashPort : IBusinessCashPort
        {
            private readonly BusinessRuntimeState runtime;
            public RuntimeCashPort(BusinessRuntimeState runtime) { this.runtime = runtime; }
            public int BalanceCents => runtime != null ? runtime.CurrentCashCents : 0;
            public string TryDebit(int amountCents, string reason)
            {
                if (runtime == null) return "no runtime";
                return runtime.TrySpendCents(amountCents) ? null : "insufficient business cash";
            }
            public string Credit(int amountCents, string reason)
            {
                if (runtime == null) return "no runtime";
                runtime.AddCashCents(amountCents);
                return null;
            }
        }

        private sealed class FailingCreditPort : IBusinessCashPort
        {
            public int BalanceCents => 999999;
            public string TryDebit(int amountCents, string reason) => null;
            public string Credit(int amountCents, string reason) => "test credit failure";
        }

        private static SlabOffcutFuelLot Lot(EntityIdRegistry ids, string mill, int units, int day)
        {
            return new SlabOffcutFuelLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FuelWoodUnits = units,
                SourceMillBusinessId = mill,
                SourceLogLotId = "log-lot-1",
                StandId = "stand-1",
                Species = "pine",
                ProducedDayIndex = day,
            };
        }

        private static Series2FuelDealer DealerWith(int unitsA, int unitsB)
        {
            var ids = new EntityIdRegistry();
            var intake = new FuelYardByproductIntake();
            var dealer = new Series2FuelDealer(intake, "fuel-dealer-1");
            var diag = new List<string>();
            Assert.IsNull(dealer.ReceiveSawmillLot(Lot(ids, "mill-1", unitsA, 1), diag));
            if (unitsB > 0)
                Assert.IsNull(dealer.ReceiveSawmillLot(Lot(ids, "mill-1", unitsB, 2), diag));
            return dealer;
        }

        // ---------- dealer intake / dispense ----------

        [Test]
        public void Dealer_IntakeDispense_FifoShortfallDispensesNothing()
        {
            var dealer = DealerWith(10, 10);
            var diag = new List<string>();
            Assert.AreEqual(20, dealer.StockUnits);
            List<FuelYardByproductIntake.FuelYardDispenseLine> lines = dealer.DispenseUnits(15, 3, diag);
            Assert.IsNotNull(lines);
            Assert.AreEqual(2, lines.Count, "FIFO across two lots");
            Assert.AreEqual(10, lines[0].UnitsTaken);
            Assert.AreEqual(5, lines[1].UnitsTaken);
            Assert.AreEqual(5, dealer.StockUnits, "only the dispensed units left the yard");
            Assert.IsNull(dealer.DispenseUnits(6, 3, diag), "shortfall dispenses nothing, never a partial lot");
            Assert.AreEqual(5, dealer.StockUnits, "failed dispense leaves stock untouched");
            List<FuelYardByproductIntake.FuelYardDispenseLine> rest = dealer.DispenseUnits(5, 3, diag);
            Assert.IsNotNull(rest);
            Assert.AreEqual(0, dealer.StockUnits, "emptied lots are gone, never re-sold");
            Assert.IsNull(dealer.DispenseUnits(1, 3, diag));
        }

        [Test]
        public void Dealer_SawmillReceipt_PaysSawmillNothingAndSaysSo()
        {
            var ids = new EntityIdRegistry();
            var intake = new FuelYardByproductIntake();
            var dealer = new Series2FuelDealer(intake, "fuel-dealer-1");
            var diag = new List<string>();
            Assert.IsNull(dealer.ReceiveSawmillLot(Lot(ids, "mill-1", 12, 1), diag));
            Assert.AreEqual(12, dealer.StockUnits);
            bool saysNoPayment = false;
            foreach (string line in diag) if (line.Contains("no sawmill payment")) saysNoPayment = true;
            Assert.IsTrue(saysNoPayment, "receipt must diagnose that no sawmill payment was made, not silently price it at 0c: " + string.Join("; ", diag));
        }

        [Test]
        public void Dealer_OrphanLot_Refused()
        {
            var ids = new EntityIdRegistry();
            var intake = new FuelYardByproductIntake();
            var dealer = new Series2FuelDealer(intake, "fuel-dealer-1");
            var orphan = new SlabOffcutFuelLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FuelWoodUnits = 5,
                SourceMillBusinessId = string.Empty,
                SourceLogLotId = "log-lot-1",
                StandId = "stand-1",
                ProducedDayIndex = 1,
            };
            Assert.IsNotNull(dealer.ReceiveSawmillLot(orphan, new List<string>()));
            Assert.AreEqual(0, dealer.StockUnits);
        }

        // ---------- dealer -> bakery fuel source ----------

        [Test]
        public void DealerSource_SuppliesBakeryFuelStore_WithProvenance_ThenShortfallRefuses()
        {
            var dealer = DealerWith(10, 10);
            var ids = new EntityIdRegistry();
            var bakery = new BakeryShopRuntime("bakery-1");
            var source = new Series2DealerBakeryFuelSource(dealer, 1, null, ids);
            var diag = new List<string>();
            Assert.AreEqual(20, source.FuelUnitsOnHand());
            Assert.IsNull(source.SupplyTo(bakery, 6, 5, diag));
            Assert.AreEqual(6, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId));
            Assert.AreEqual(14, source.FuelUnitsOnHand());
            Assert.AreEqual(1, bakery.FuelStock.Lots.Count, "one bakery lot per dispense line so far (first lot only)");
            Assert.AreEqual("fuel-dealer-1", bakery.FuelStock.Lots[0].FuelDealerBusinessId);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(bakery.FuelStock.Lots[0].SourceFuelLotId), "dealer lot reference must survive into the bakery lot");
            Assert.IsNotNull(source.SupplyTo(bakery, 30, 5, diag), "shortfall must refuse, not partially fuel");
            Assert.AreEqual(6, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId), "refused supply leaves the fuel store untouched");
            Assert.AreEqual(14, dealer.StockUnits, "refused supply leaves dealer stock untouched");
        }

        [Test]
        public void DealerSource_ConversionFactor_HonouredWithoutRoundingLoss()
        {
            var dealer = DealerWith(10, 0);
            Assert.AreEqual(10, dealer.StockUnits);
            var ids = new EntityIdRegistry();
            var bakery = new BakeryShopRuntime("bakery-1");
            var source = new Series2DealerBakeryFuelSource(dealer, 2, null, ids);
            Assert.AreEqual(5, source.FuelUnitsOnHand(), "10 intake units at factor 2 = 5 bakery units");
            Assert.IsNull(source.SupplyTo(bakery, 3, 5, new List<string>()));
            Assert.AreEqual(3, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId));
            Assert.AreEqual(4, dealer.StockUnits, "6 intake units consumed for 3 bakery units");
            Assert.AreEqual(2, source.FuelUnitsOnHand());
        }

        [Test]
        public void DealerSource_OffMapFallback_CoversRemainder_AndUnwiredRefuses()
        {
            var dealer = DealerWith(1, 0);
            Assert.AreEqual(1, dealer.StockUnits);
            var ids = new EntityIdRegistry();
            var bakery = new BakeryShopRuntime("bakery-1");
            var unwired = new Series2DealerBakeryFuelSource(dealer, 1, null, ids);
            Assert.IsNotNull(unwired.SupplyTo(bakery, 3, 5, new List<string>()), "dealer-only source must refuse an uncovered request");
            Assert.AreEqual(1, dealer.StockUnits, "unwired refusal dispenses nothing");
            Assert.AreEqual(0, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId));

            Series2OffMapFuelReceipt importReceipt = (BakeryFuelStock stock, int needed, int day, EntityIdRegistry registry, List<string> d) =>
                BakeryFuelSupply.ReceiveImportArrival(stock, needed, "import-order-1", "Off-map cordwood, via railhead", day, registry, d);
            var wired = new Series2DealerBakeryFuelSource(dealer, 1, importReceipt, ids);
            Assert.IsNull(wired.SupplyTo(bakery, 3, 5, new List<string>()));
            Assert.AreEqual(3, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId), "1 dealer unit + 2 import units");
            Assert.AreEqual(0, dealer.StockUnits);
            bool hasImportLot = false;
            foreach (BakeryFuelLot lot in bakery.FuelStock.Lots)
                if (lot.ImportOrderId == "import-order-1") hasImportLot = true;
            Assert.IsTrue(hasImportLot, "import lot must carry its named order, not an orphan lot");
        }

        // ---------- household fuel via adapter + reserve link ----------

        [Test]
        public void HouseholdFuel_AdapterSalePlusReserveLink_FillsFuelWoodReserve()
        {
            var dealer = DealerWith(10, 10);
            var dealerRuntime = new BusinessRuntimeState();
            dealerRuntime.BindBusinessIdentity("fuel-dealer-1");
            dealerRuntime.AddCashCents(0);
            var adapter = new Series2FuelDealerSupplierAdapter(
                dealer.Intake, new RuntimeCashPort(dealerRuntime),
                "fuel-dealer-1", "Fuel Dealer", "yard-1", 28);
            var diag = new List<string>();
            int sold = adapter.SellItem(HouseholdItemCatalog.FuelWoodId, 4, 7,
                new ShoppingSettlement { Mode = ShoppingSettlementMode.CashFromLedger }, diag);
            Assert.AreEqual(4, sold, string.Join("; ", diag));
            Assert.AreEqual(16, dealer.StockUnits, "adapter sale dispensed real intake stock");
            Assert.AreEqual(112, dealerRuntime.CurrentCashCents, "4u x 28c revenue posted to the dealer, not invented");

            var household = new HouseholdState { id = 99, householdName = "Test Household" };
            household.EnsureHouseholdReservesInitialized();
            HouseholdReserveState before = household.GetReserve(HouseholdReserveCatalog.FuelWoodCategoryId);
            int beforeUnits = before != null ? before.currentUnits : 0;
            int credited = Series2HouseholdFuelReserveLink.CreditReserve(household, sold, 2, 7, diag);
            Assert.AreEqual(8, credited, "conversion factor 2 is a caller parameter, honoured here, not a game rate");
            HouseholdReserveState after = household.GetReserve(HouseholdReserveCatalog.FuelWoodCategoryId);
            Assert.IsNotNull(after);
            Assert.AreEqual(beforeUnits + 8, after.currentUnits);
            Assert.AreEqual(-1, Series2HouseholdFuelReserveLink.CreditReserve(null, 4, 1, 7, diag), "null household refuses, credits nothing");
            Assert.AreEqual(-1, Series2HouseholdFuelReserveLink.CreditReserve(household, 0, 1, 7, diag), "zero quantity refuses, credits nothing");
        }

        // ---------- carrier quotes ----------

        [Test]
        public void CarrierQuote_FreightBusinessWithoutCarrier_RefusedNotSellerFreight()
        {
            var quote = new OffMapFreightQuote
            {
                SupplierId = "offmap-flour-co",
                SkuId = "flour",
                QuantityUnits = 10,
                FreightOption = FreightOptionKind.FreightBusiness,
                GoodsCents = 500,
                FreightCents = 250,
                DeliveredTotalCents = 750,
            };
            Series2CarrierQuoteResult refused = Series2CarrierQuoteResolver.ResolveOffMapQuote(
                quote, "freight-co-1", false, "general-store-1", "ship-1", "wagon-1", new List<string>());
            Assert.IsTrue(refused.IsFreightBusiness);
            Assert.IsNull(refused.Leg, "no carrier means no leg, never a silent SellerFreight fallback");
            Assert.IsTrue(!string.IsNullOrWhiteSpace(refused.Refusal));

            var sellerQuote = new OffMapFreightQuote
            {
                SupplierId = "offmap-flour-co",
                SkuId = "flour",
                QuantityUnits = 10,
                FreightOption = FreightOptionKind.SellerFreight,
                GoodsCents = 500,
                FreightCents = 250,
                DeliveredTotalCents = 750,
            };
            Series2CarrierQuoteResult notCarrier = Series2CarrierQuoteResolver.ResolveOffMapQuote(
                sellerQuote, "freight-co-1", true, "general-store-1", "ship-2", "wagon-1", new List<string>());
            Assert.IsFalse(notCarrier.IsFreightBusiness, "SellerFreight is untouched by the carrier resolver");
            Assert.IsNull(notCarrier.Leg);
            Assert.AreEqual(string.Empty, notCarrier.Refusal);
        }

        [Test]
        public void CarrierQuote_Pass3FreightBusinessRequest_ReRoutedAtAllocatedFreight()
        {
            var request = new Pass3OrderRequest
            {
                SupplierId = "offmap-co",
                SkuId = "flour",
                QuantityUnits = 8,
                GoodsCents = 400,
                FreightAllocatedCents = 180,
                TotalCents = 580,
                FreightOption = Pass3FreightOption.FreightBusiness,
            };
            Series2CarrierQuoteResult result = Series2CarrierQuoteResolver.ResolvePass3Request(
                request, "freight-co-1", true, "general-store-1", "ship-9", "wagon-1", new List<string>());
            Assert.IsTrue(result.IsFreightBusiness);
            Assert.IsNotNull(result.Leg);
            Assert.AreEqual(180, result.Leg.FreightChargeCents, "leg charges the request's allocated freight, never a recomputed rate");
            Assert.AreEqual(8, result.Leg.QuantityUnits);
            Assert.AreEqual("general-store-1", result.Leg.DestinationBusinessId);
            Assert.AreEqual("general-store-1", result.Leg.FreightPayerBusinessId);
        }

        // ---------- carrier legs + settlement ----------

        private sealed class CarrierSetup
        {
            public List<string> Diag = new List<string>();
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public FreightShipmentService Service = new FreightShipmentService();
            public FreightProductionDriver Driver;
            public Series2FreightProductionCarrier Carrier;
            public FreightShipmentHooks Hooks;
            public EntityId Handler = EntityId.For(EntityKind.Person, 701);
            public EntityId DriverId = EntityId.For(EntityKind.Person, 702);
            public List<EntityId> Team = new List<EntityId> { EntityId.For(EntityKind.Animal, 11), EntityId.For(EntityKind.Animal, 12) };
            public Dictionary<string, int> RecipientStock = new Dictionary<string, int>(StringComparer.Ordinal);
            public bool RecipientAccepts = true;
            public BusinessRuntimeState CarrierRuntime = new BusinessRuntimeState();
        }

        private static CarrierSetup NewCarrierSetup()
        {
            var s = new CarrierSetup();
            s.Budgets.EnsureDay(42);
            s.Hooks = new FreightShipmentHooks
            {
                Tasks = s.Tasks,
                Budgets = s.Budgets,
                DayIndex = 42,
                Diagnostics = new List<string>(),
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    if (!s.RecipientAccepts) { reason = "recipient refused the delivery"; return 0; }
                    string key = shipment.destinationBusinessInstanceId ?? "nowhere";
                    s.RecipientStock[key] = s.RecipientStock.TryGetValue(key, out int have) ? have + quantityUnits : quantityUnits;
                    return quantityUnits;
                },
            };
            s.Service.RegisterWagon(new EquipmentAsset
            {
                AssetId = "wagon-1",
                Kind = "farm-wagon",
                DisplayName = "wagon-1",
                Condition01 = 0.9f,
                PayloadMassCapacityKg = 1000f,
                CargoVolumeCapacityM3 = 4f,
            });
            s.Driver = new FreightProductionDriver(s.Service);
            s.CarrierRuntime.BindBusinessIdentity("freight-co-1");
            s.Driver.EnsureCompany("freight-co-1", s.CarrierRuntime);
            // NOTE: the payer runtime is deliberately NOT registered
            // with the driver, so the driver's own collection leaves
            // this leg uncollected and the Pass 8 settlement below is
            // the ONLY settlement in this test. In production Codex
            // must still choose one settlement path per leg; the
            // freightRevenueCollected flag guards the other path.
            s.Carrier = new Series2FreightProductionCarrier(s.Driver, "freight-co-1");
            return s;
        }

        private static void TickUntil(CarrierSetup s, Func<bool> done)
        {
            int guard = 0;
            while (!done() && guard++ < 60)
                s.Carrier.AdvanceTick(3600f, s.Hooks, s.Handler, s.Diag);
        }

        [Test]
        public void CarrierLeg_Acceptance_SettlesOnce_DelayedSettlesNothing()
        {
            var s = NewCarrierSetup();
            Series2CarrierLeg leg = Series2SliceLegs.DealerToBakery(
                "ship-fuel-1", "freight-co-1", "fuel-dealer-1", "bakery-1", 10, 250, "wagon-1");
            leg.DriverId = s.DriverId;
            leg.AnimalIds = s.Team;
            Series2CarrierLegResult created = s.Carrier.RequestLeg(leg, s.Hooks, s.Diag);
            Assert.AreEqual(string.Empty, created.Refusal, created.Refusal);
            Assert.IsNotNull(created.Shipment);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, created.Shipment.Status);

            TickUntil(s, () => created.Shipment.Status == LogisticsShipmentStatus.Completed);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, created.Shipment.Status, string.Join("; ", s.Diag));
            Assert.AreEqual(10, created.Shipment.DeliveredQuantityUnits);
            Assert.AreEqual(10, s.RecipientStock["bakery-1"], "acceptance through the callback, exactly once");

            var payerRuntime = new BusinessRuntimeState();
            payerRuntime.BindBusinessIdentity("bakery-1");
            payerRuntime.AddCashCents(1000);
            var carrierRuntime = new BusinessRuntimeState();
            carrierRuntime.BindBusinessIdentity("freight-co-1");
            var settlement = new Series2CarrierSettlement();
            Series2CarrierSettlementResult settled = settlement.SettleOnAcceptance(
                created.Shipment, new RuntimeCashPort(payerRuntime), new RuntimeCashPort(carrierRuntime), s.Diag);
            Assert.IsTrue(settled.Settled, settled.Refusal);
            Assert.AreEqual(750, payerRuntime.CurrentCashCents, "payer debited exactly the freight charge");
            Assert.AreEqual(250, carrierRuntime.CurrentCashCents, "carrier credited exactly the freight charge");
            Series2CarrierSettlementResult again = settlement.SettleOnAcceptance(
                created.Shipment, new RuntimeCashPort(payerRuntime), new RuntimeCashPort(carrierRuntime), s.Diag);
            Assert.IsFalse(again.Settled, "second settlement of the same leg is refused");
            Assert.AreEqual(750, payerRuntime.CurrentCashCents);
            Assert.AreEqual(250, carrierRuntime.CurrentCashCents);

            // Delayed leg: recipient refuses, settlement refuses, custody holds.
            var s2 = NewCarrierSetup();
            s2.RecipientAccepts = false;
            Series2CarrierLeg leg2 = Series2SliceLegs.DealerToHousehold(
                "ship-fuel-2", "freight-co-1", "fuel-dealer-1", 42, 5, 100, "wagon-1");
            leg2.DriverId = s2.DriverId;
            leg2.AnimalIds = s2.Team;
            Series2CarrierLegResult created2 = s2.Carrier.RequestLeg(leg2, s2.Hooks, s2.Diag);
            Assert.IsNotNull(created2.Shipment);
            TickUntil(s2, () => created2.Shipment.Status == LogisticsShipmentStatus.Delayed);
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, created2.Shipment.Status, string.Join("; ", s2.Diag));
            Assert.AreEqual(5, created2.Shipment.RemainingQuantityUnits, "rejected cargo stays in wagon custody");
            Assert.IsFalse(created2.Shipment.deliveryApplied);
            var payer2 = new BusinessRuntimeState();
            payer2.BindBusinessIdentity("fuel-dealer-1");
            payer2.AddCashCents(500);
            var carrier2 = new BusinessRuntimeState();
            carrier2.BindBusinessIdentity("freight-co-1");
            Series2CarrierSettlementResult delayed = new Series2CarrierSettlement().SettleOnAcceptance(
                created2.Shipment, new RuntimeCashPort(payer2), new RuntimeCashPort(carrier2), s2.Diag);
            Assert.IsFalse(delayed.Settled, "a Delayed shipment settles nothing");
            Assert.AreEqual(500, payer2.CurrentCashCents);
            Assert.AreEqual(0, carrier2.CurrentCashCents);
        }

        [Test]
        public void CarrierSettlement_Atomic_RefundOnCarrierCreditFailure_AndTickRequirement()
        {
            var payerRuntime = new BusinessRuntimeState();
            payerRuntime.BindBusinessIdentity("bakery-1");
            payerRuntime.AddCashCents(1000);
            var payer = new RuntimeCashPort(payerRuntime);
            var shipment = new LogisticsShipmentState
            {
                shipmentId = "ship-settle-1",
                state = LogisticsShipmentStatus.Completed,
                deliveryApplied = true,
                freightChargeCents = 300,
            };
            var settlement = new Series2CarrierSettlement();
            Series2CarrierSettlementResult failed = settlement.SettleOnAcceptance(
                shipment, payer, new FailingCreditPort(), new List<string>());
            Assert.IsFalse(failed.Settled);
            Assert.AreEqual(1000, payerRuntime.CurrentCashCents, "carrier credit failure refunds the payer debit in full");
            Assert.IsFalse(settlement.AlreadySettled("ship-settle-1"), "a failed settlement must not mark the leg settled");

            var s = NewCarrierSetup();
            Assert.AreEqual(0, s.Carrier.AdvanceTick(3600f, new FreightShipmentHooks(), s.Handler, s.Diag),
                "a tick without TaskAuthority + budgets advances nothing, never on a timer alone");
            bool warned = false;
            foreach (string line in s.Diag) if (line.Contains("TaskAuthority")) warned = true;
            Assert.IsTrue(warned, "the tick requirement failure must be diagnosed, not silent");
        }

        [Test]
        public void CarrierLeg_WrongCarrier_AndSliceLegShapes()
        {
            var s = NewCarrierSetup();
            Series2CarrierLeg wrong = Series2SliceLegs.StoreToBakery(
                "ship-wrong-1", "other-carrier", "general-store-1", "bakery-1", 5, 100, "wagon-1");
            wrong.DriverId = s.DriverId;
            wrong.AnimalIds = s.Team;
            Series2CarrierLegResult refused = s.Carrier.RequestLeg(wrong, s.Hooks, s.Diag);
            Assert.IsNull(refused.Shipment, "a leg naming another carrier is refused, never silently re-carried");
            Assert.IsTrue(!string.IsNullOrWhiteSpace(refused.Refusal));

            Series2CarrierLeg wholesale = Series2SliceLegs.BakeryToStoreWholesale(
                "ship-ws-1", "freight-co-1", "bakery-1", "general-store-1", 24, 120, "wagon-1");
            Assert.AreEqual("bread-wholesale", wholesale.CargoKind);
            Series2CarrierLeg household = Series2SliceLegs.DealerToHousehold(
                "ship-hh-1", "freight-co-1", "fuel-dealer-1", 42, 5, 100, "wagon-1");
            Assert.AreEqual(42, household.DestinationHouseholdId);
            Series2CarrierLeg arrival = Series2SliceLegs.OffMapArrivalToBusiness(
                "ship-arr-1", "freight-co-1", "offmap-flour-co", "general-store-1", "flour", 20, 250, "wagon-1");
            Assert.AreEqual("offmap-flour-co", arrival.SourceBusinessId);
            Assert.AreEqual("general-store-1", arrival.DestinationBusinessId);
        }
    }
}
