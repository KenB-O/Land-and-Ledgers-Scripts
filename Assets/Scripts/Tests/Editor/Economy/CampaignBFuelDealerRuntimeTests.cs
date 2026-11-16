// CAMPAIGN-B (DEF-003), package 18: the Fuel Dealer as a real business.
// Owner Decision 3 (Nov 15, 2026) requires the missing functional
// business: real inventory, purchasing, sales, cash accounting, custody,
// supplier relationships, and save/load. Source fuel from actual local
// sawmill output where available, with legitimate off-map procurement as
// a fallback. Decision 1 conversions apply (1 intake unit ~= 1 kg dry
// wood; 1 bakery fuel unit = 4 intake units; 1 household retail bundle
// = 10 intake units; sawmill byproduct 1c per intake unit). No imaginary
// Timberline without an authored supplier definition and supply path.
//
// These tests execute FuelDealerRuntime directly (plain C#, compiles in
// the Roslyn harness) AND pin the harness-excluded wiring by source
// inspection, because SimulationDrivers, the Pass 9 save state, the
// adapter, and the paid bakery source cannot compile in the harness —
// BusinessInstanceState / BusinessRuntimeState closures run through
// ScriptableObject and ~130 further types (the B-13 precedent). The
// wiring tests read the repo source, the B-13 pattern.

using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LandLedgers.Economy.Businesses.FuelDealer;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class CampaignBFuelDealerRuntimeTests
    {
        private const string DealerId = "fuel-dealer-1";
        private const string MillId = "mill-1";

        // --- Fakes and builders ---------------------------------------------

        private sealed class FakeCashPort : IBusinessCashPort
        {
            public int BalanceCents { get; set; }

            public string TryDebit(int cents, string reason)
            {
                if (cents < 0) return "cannot debit a negative amount.";
                if (cents > BalanceCents)
                    return "insufficient funds: " + BalanceCents + "c on hand, " + cents + "c requested.";
                BalanceCents -= cents;
                return null;
            }

            public string Credit(int cents, string reason)
            {
                if (cents < 0) return "cannot credit a negative amount.";
                BalanceCents += cents;
                return null;
            }
        }

        private static FuelDealerRuntime Runtime(
            FuelYardByproductIntake intake, FakeCashPort cash, string dealerId = DealerId)
        {
            return new FuelDealerRuntime(dealerId, intake, cash);
        }

        private static SlabOffcutFuelLot MillLot(EntityIdRegistry ids, int fuelWoodUnits)
        {
            return new SlabOffcutFuelLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FuelWoodUnits = fuelWoodUnits,
                OwnerId = MillId,
                SourceMillBusinessId = MillId,
                SourceLogLotId = "log-1",
                StandId = "stand-1",
                Species = "pine",
                ProducedDayIndex = 0,
            };
        }

        private static void StockMillFuel(FuelYardByproductIntake intake, EntityIdRegistry ids, int units)
        {
            var diag = new List<string>();
            Assert.IsNull(intake.ReceiveByproductLot(DealerId, MillLot(ids, units), diag),
                "test setup: mill lot receipt must succeed: " + string.Join("; ", diag));
        }

        private static FuelDealerRuntime StockedRuntime(
            int stockUnits, int cashCents, out FuelYardByproductIntake intake, out FakeCashPort cash)
        {
            intake = new FuelYardByproductIntake();
            cash = new FakeCashPort { BalanceCents = cashCents };
            var ids = new EntityIdRegistry();
            if (stockUnits > 0) StockMillFuel(intake, ids, stockUnits);
            return Runtime(intake, cash);
        }

        // --- Construction and policy ---------------------------------------

        [Test]
        public void Construction_HoldsIdentityAndDefaults()
        {
            var intake = new FuelYardByproductIntake();
            var cash = new FakeCashPort { BalanceCents = 500 };
            FuelDealerRuntime runtime = Runtime(intake, cash);

            Assert.AreEqual(DealerId, runtime.DealerBusinessId);
            Assert.AreEqual(0, runtime.StockIntakeUnits);
            Assert.AreEqual(FuelDealerRuntime.DefaultTargetStockIntakeUnits, runtime.TargetStockIntakeUnits);
            Assert.AreEqual(FuelDealerRuntime.DefaultReorderPointIntakeUnits, runtime.ReorderPointIntakeUnits);
            Assert.AreEqual(FuelDealerRuntime.DefaultColdSeasonTargetMultiplier, runtime.ColdSeasonTargetMultiplier, 1e-9);
            Assert.AreEqual(0, runtime.Suppliers.Count);
            Assert.AreEqual(0, runtime.Orders.Count);
            Assert.AreEqual(0, runtime.CashBook.Count);
            Assert.AreEqual(0, runtime.ConfiguredBundlePriceCents);
        }

        [Test]
        public void Construction_NullIntakeThrows()
        {
            var cash = new FakeCashPort();
            bool threw = false;
            try
            {
                new FuelDealerRuntime(DealerId, null, cash);
            }
            catch (ArgumentNullException)
            {
                threw = true;
            }
            Assert.IsTrue(threw, "a runtime without its intake stock authority must throw, not run stockless.");
        }

        [Test]
        public void Construction_BlankDealerIdThrows()
        {
            var intake = new FuelYardByproductIntake();
            bool threw = false;
            try
            {
                new FuelDealerRuntime("  ", intake, null);
            }
            catch (ArgumentException)
            {
                threw = true;
            }
            Assert.IsTrue(threw, "a runtime without a dealer business id must throw, not record against the void.");
        }

        [Test]
        public void TargetStock_ScalesInColdSeason()
        {
            FuelDealerRuntime runtime = Runtime(new FuelYardByproductIntake(), new FakeCashPort());
            Assert.AreEqual(200, runtime.TargetStockIntakeUnitsFor(false));
            Assert.AreEqual(300, runtime.TargetStockIntakeUnitsFor(true),
                "cold season (Nov-Mar) target is 200 x 1.5 = 300 intake units.");
            runtime.TargetStockIntakeUnits = 100;
            Assert.AreEqual(150, runtime.TargetStockIntakeUnitsFor(true));
            runtime.ColdSeasonTargetMultiplier = 2.0;
            Assert.AreEqual(200, runtime.TargetStockIntakeUnitsFor(true));
        }

        [Test]
        public void MarginPerBundle_UsesDecidedCost()
        {
            FuelDealerRuntime runtime = Runtime(new FuelYardByproductIntake(), new FakeCashPort());
            Assert.AreEqual(0, runtime.MarginPerBundleCents(),
                "no configured price means no margin to report.");
            runtime.SetConfiguredBundlePriceCents(28);
            Assert.AreEqual(18, runtime.MarginPerBundleCents(),
                "28c bundle minus 10 intake units at 1c each = 18c margin.");
            runtime.SetConfiguredBundlePriceCents(22);
            Assert.AreEqual(12, runtime.MarginPerBundleCents(),
                "22c offcut bundle minus 10c cost = 12c margin.");
            runtime.SetConfiguredBundlePriceCents(0);
            Assert.AreEqual(12, runtime.MarginPerBundleCents(),
                "a non-positive price is ignored; the last configured price stands.");
        }

        // --- Local supplier relationship ------------------------------------

        [Test]
        public void SyncLocalSupplier_RegistersMillWithDecidedTerms()
        {
            FuelDealerRuntime runtime = Runtime(new FuelYardByproductIntake(), new FakeCashPort());
            runtime.SyncLocalSupplier(MillId, 3);

            Assert.AreEqual(1, runtime.Suppliers.Count);
            FuelDealerRuntime.FuelDealerSupplierRelationship mill = runtime.Suppliers[0];
            Assert.AreEqual(MillId, mill.SupplierId);
            Assert.AreEqual(FuelDealerRuntime.FuelDealerSupplierKind.LocalSawmill, mill.Kind);
            Assert.AreEqual(1, mill.PriceCentsPerInputUnit);
            Assert.AreEqual(1.0, mill.ConversionRateFuelPerInputUnit, 1e-9);
            Assert.AreEqual(FuelDealerRuntime.FuelDealerSupplierStatus.Active, mill.Status);
            Assert.AreEqual(3, mill.FirstSeenDayIndex);
            Assert.AreEqual(-1, mill.LastDeliveryDayIndex);
        }

        [Test]
        public void SyncLocalSupplier_NullMillMarksUnavailableKeepsTotals()
        {
            FuelDealerRuntime runtime = StockedRuntime(0, 500, out _, out _);
            runtime.SyncLocalSupplier(MillId, 1);
            runtime.RecordLocalPurchase(2, MillId, 35, 35, 2);
            Assert.AreEqual(35, runtime.Suppliers[0].TotalFuelUnitsReceived);

            runtime.SyncLocalSupplier(null, 3);

            Assert.AreEqual(1, runtime.Suppliers.Count);
            Assert.AreEqual(FuelDealerRuntime.FuelDealerSupplierStatus.Unavailable, runtime.Suppliers[0].Status);
            Assert.AreEqual(35, runtime.Suppliers[0].TotalFuelUnitsReceived,
                "losing the mill must not erase what it already delivered.");
            Assert.AreEqual(35, runtime.Suppliers[0].TotalCentsPaid);
        }

        // --- Procurement assessment -----------------------------------------

        [Test]
        public void Assess_BelowTargetWithLocalOutput_BuysTheShortfall()
        {
            FuelDealerRuntime runtime = StockedRuntime(35, 500, out _, out _);
            FuelDealerRuntime.FuelDealerProcurementAssessment assessment =
                runtime.AssessLocalPurchase(4, 35, false);

            Assert.AreEqual(FuelDealerRuntime.FuelDealerProcurementDecision.Buy, assessment.Decision);
            Assert.AreEqual(165, assessment.WantIntakeUnits, "want = target 200 - stock 35.");
            Assert.AreEqual(4, runtime.LastProcurementDayIndex);
            Assert.IsTrue(runtime.LastProcurementNote.Length > 0);
        }

        [Test]
        public void Assess_AtTargetWithLocalOutput_DeclinesFull()
        {
            FuelDealerRuntime runtime = StockedRuntime(200, 500, out _, out _);
            FuelDealerRuntime.FuelDealerProcurementAssessment assessment =
                runtime.AssessLocalPurchase(4, 35, false);

            Assert.AreEqual(FuelDealerRuntime.FuelDealerProcurementDecision.DeclineFull, assessment.Decision);
            Assert.AreEqual(0, assessment.WantIntakeUnits);
            StringAssert.Contains("at/above target", assessment.Reason);
        }

        [Test]
        public void Assess_ColdSeasonTargetIsHigher()
        {
            // Stock 250 is above the warm target (200) but below the cold
            // target (300): the same yard buys in winter and declines in summer.
            FuelDealerRuntime runtime = StockedRuntime(250, 500, out _, out _);
            Assert.AreEqual(FuelDealerRuntime.FuelDealerProcurementDecision.DeclineFull,
                runtime.AssessLocalPurchase(4, 35, false).Decision);
            FuelDealerRuntime.FuelDealerProcurementAssessment cold =
                runtime.AssessLocalPurchase(4, 35, true);
            Assert.AreEqual(FuelDealerRuntime.FuelDealerProcurementDecision.Buy, cold.Decision);
            Assert.AreEqual(50, cold.WantIntakeUnits, "want = cold target 300 - stock 250.");
        }

        [Test]
        public void Assess_NoLocalOutputAboveReorder_Sufficient()
        {
            FuelDealerRuntime runtime = StockedRuntime(80, 500, out _, out _);
            FuelDealerRuntime.FuelDealerProcurementAssessment assessment =
                runtime.AssessLocalPurchase(4, 0, false);

            Assert.AreEqual(FuelDealerRuntime.FuelDealerProcurementDecision.Sufficient, assessment.Decision);
            Assert.AreEqual(0, assessment.WantIntakeUnits);
        }

        [Test]
        public void Assess_NoLocalOutputBelowReorder_NoAuthoredSupplier_NoSupply()
        {
            FuelDealerRuntime runtime = StockedRuntime(10, 500, out _, out _);
            FuelDealerRuntime.FuelDealerProcurementAssessment assessment =
                runtime.AssessLocalPurchase(4, 0, false);

            Assert.AreEqual(FuelDealerRuntime.FuelDealerProcurementDecision.NoSupply, assessment.Decision);
            StringAssert.Contains("no authored off-map supplier", assessment.Reason);
            StringAssert.Contains("no vendor is conjured", assessment.Reason);
        }

        [Test]
        public void Assess_NoLocalOutputBelowReorder_WithOffMapSupplier_OffMapAvailable()
        {
            FuelDealerRuntime runtime = StockedRuntime(10, 500, out _, out _);
            var diag = new List<string>();
            Assert.IsNull(runtime.RegisterOffMapSupplier("millbrook-fuel", "cordwood", 2, 1.0, diag));

            FuelDealerRuntime.FuelDealerProcurementAssessment assessment =
                runtime.AssessLocalPurchase(4, 0, false);

            Assert.AreEqual(FuelDealerRuntime.FuelDealerProcurementDecision.OffMapAvailable, assessment.Decision);
            Assert.AreEqual(190, assessment.WantIntakeUnits, "want = target 200 - stock 10.");
            StringAssert.Contains("millbrook-fuel", assessment.Reason);
        }

        // --- Local purchase recording ---------------------------------------

        [Test]
        public void RecordLocalPurchase_UpdatesSupplierAndCashBook()
        {
            FuelDealerRuntime runtime = StockedRuntime(0, 500, out _, out _);
            runtime.SyncLocalSupplier(MillId, 1);
            runtime.RecordLocalPurchase(2, MillId, 35, 35, 2);

            Assert.AreEqual(35, runtime.Suppliers[0].TotalFuelUnitsReceived);
            Assert.AreEqual(35, runtime.Suppliers[0].TotalCentsPaid);
            Assert.AreEqual(2, runtime.Suppliers[0].LastDeliveryDayIndex);
            Assert.AreEqual(1, runtime.CashBook.Count);
            FuelDealerRuntime.FuelDealerCashBookEntry entry = runtime.CashBook[0];
            Assert.AreEqual(FuelDealerRuntime.FuelDealerCashBookKind.LocalPurchase, entry.Kind);
            Assert.AreEqual(MillId, entry.CounterpartyId);
            Assert.AreEqual(35, entry.IntakeUnits);
            Assert.AreEqual(-35, entry.AmountCents, "a purchase is cash out: negative in the book.");
            Assert.AreEqual(2, entry.DayIndex);
        }

        [Test]
        public void RecordLocalPurchase_ZeroUnitsIsVacuous()
        {
            FuelDealerRuntime runtime = StockedRuntime(0, 500, out _, out _);
            runtime.SyncLocalSupplier(MillId, 1);
            runtime.RecordLocalPurchase(2, MillId, 0, 0, 0);

            Assert.AreEqual(0, runtime.CashBook.Count);
            Assert.AreEqual(0, runtime.Suppliers[0].TotalFuelUnitsReceived);
            Assert.AreEqual(-1, runtime.Suppliers[0].LastDeliveryDayIndex);
        }

        // --- Off-map suppliers and orders -----------------------------------

        [Test]
        public void RegisterOffMapSupplier_ValidRegistersWithTerms()
        {
            FuelDealerRuntime runtime = Runtime(new FuelYardByproductIntake(), new FakeCashPort());
            var diag = new List<string>();
            Assert.IsNull(runtime.RegisterOffMapSupplier("millbrook-fuel", "cordwood", 2, 1.0, diag));

            Assert.AreEqual(1, runtime.Suppliers.Count);
            FuelDealerRuntime.FuelDealerSupplierRelationship supplier = runtime.Suppliers[0];
            Assert.AreEqual("millbrook-fuel", supplier.SupplierId);
            Assert.AreEqual(FuelDealerRuntime.FuelDealerSupplierKind.OffMap, supplier.Kind);
            Assert.AreEqual("cordwood", supplier.InputSkuId);
            Assert.AreEqual(2, supplier.PriceCentsPerInputUnit);
            Assert.AreEqual(1.0, supplier.ConversionRateFuelPerInputUnit, 1e-9);
            Assert.AreEqual(FuelDealerRuntime.FuelDealerSupplierStatus.Active, supplier.Status);
        }

        [Test]
        public void RegisterOffMapSupplier_RefusesBlankIdSkuPriceRateAndDuplicate()
        {
            FuelDealerRuntime runtime = Runtime(new FuelYardByproductIntake(), new FakeCashPort());
            var diag = new List<string>();
            Assert.IsTrue(runtime.RegisterOffMapSupplier("", "cordwood", 2, 1.0, diag) != null);
            Assert.IsTrue(runtime.RegisterOffMapSupplier("s1", "", 2, 1.0, diag) != null);
            Assert.IsTrue(runtime.RegisterOffMapSupplier("s1", "cordwood", 0, 1.0, diag) != null);
            Assert.IsTrue(runtime.RegisterOffMapSupplier("s1", "cordwood", 2, 0, diag) != null);
            Assert.AreEqual(0, runtime.Suppliers.Count, "no refused registration leaves a supplier behind.");
            Assert.IsNull(runtime.RegisterOffMapSupplier("s1", "cordwood", 2, 1.0, diag));
            Assert.IsTrue(runtime.RegisterOffMapSupplier("s1", "cordwood", 3, 1.0, diag) != null,
                "a duplicate id must refuse, never silently overwrite the terms.");
            Assert.AreEqual(1, runtime.Suppliers.Count);
            Assert.AreEqual(2, runtime.Suppliers[0].PriceCentsPerInputUnit, "the original terms stand.");
        }

        [Test]
        public void PlaceOffMapOrder_UnknownSupplierRefuses()
        {
            FuelDealerRuntime runtime = Runtime(new FuelYardByproductIntake(), new FakeCashPort());
            var diag = new List<string>();
            Assert.IsNull(runtime.PlaceOffMapOrder("timberline", 10, 5, diag),
                "no authored supplier named 'timberline' exists — the order must refuse, not conjure a vendor.");
            Assert.IsTrue(diag.Count > 0);
            StringAssert.Contains("no authored off-map supplier", string.Join("; ", diag));
            Assert.AreEqual(0, runtime.Orders.Count);
        }

        [Test]
        public void PlaceOffMapOrder_ValidPlacesAndTracksOutstanding()
        {
            FuelDealerRuntime runtime = Runtime(new FuelYardByproductIntake(), new FakeCashPort());
            var diag = new List<string>();
            Assert.IsNull(runtime.RegisterOffMapSupplier("millbrook-fuel", "cordwood", 2, 1.0, diag));
            FuelDealerRuntime.FuelDealerOrder order = runtime.PlaceOffMapOrder("millbrook-fuel", 10, 5, diag);

            Assert.IsTrue(order != null);
            Assert.AreEqual(FuelDealerRuntime.FuelDealerOrderStatus.Placed, order.Status);
            Assert.AreEqual("millbrook-fuel", order.SupplierId);
            Assert.AreEqual("cordwood", order.InputSkuId);
            Assert.AreEqual(10, order.InputUnits);
            Assert.AreEqual(2, order.PriceCentsPerInputUnit);
            Assert.AreEqual(5, order.PlacedDayIndex);
            Assert.IsTrue(order.OrderId.Length > 0);
            Assert.AreEqual(1, runtime.OutstandingOrders.Count);
            Assert.IsNull(runtime.PlaceOffMapOrder("millbrook-fuel", 0, 5, diag),
                "a zero-quantity order must refuse.");
            Assert.AreEqual(1, runtime.Orders.Count);
        }

        [Test]
        public void ReceiveOffMapOrder_PaysAndStocksWithProvenance()
        {
            FuelDealerRuntime runtime = StockedRuntime(0, 100, out FuelYardByproductIntake intake, out FakeCashPort cash);
            var ids = new EntityIdRegistry();
            var diag = new List<string>();
            Assert.IsNull(runtime.RegisterOffMapSupplier("millbrook-fuel", "cordwood", 2, 1.0, diag));
            FuelDealerRuntime.FuelDealerOrder order = runtime.PlaceOffMapOrder("millbrook-fuel", 10, 5, diag);

            Assert.IsNull(runtime.ReceiveOffMapOrder(order.OrderId, "input-lot-1", 10, 6, ids, diag),
                "receipt must succeed: " + string.Join("; ", diag));

            Assert.AreEqual(FuelDealerRuntime.FuelDealerOrderStatus.Received, order.Status);
            Assert.AreEqual(6, order.ClosedDayIndex);
            Assert.AreEqual("input-lot-1", order.InputLotId);
            Assert.AreEqual(10, order.FuelUnitsReceived);
            Assert.AreEqual(0, runtime.OutstandingOrders.Count);
            Assert.AreEqual(80, cash.BalanceCents, "10u input at 2c each = 20c paid on receipt.");
            Assert.AreEqual(10, intake.TotalFuelWoodUnitsFor(DealerId));
            Assert.AreEqual(10, runtime.StockIntakeUnits);
            IReadOnlyList<SlabOffcutFuelLot> held = intake.LotsFor(DealerId);
            Assert.AreEqual(1, held.Count);
            StringAssert.Contains("off-map supplier millbrook-fuel", held[0].ProvenanceChain());
            StringAssert.Contains("order " + order.OrderId, held[0].ProvenanceChain());
            StringAssert.Contains("input lot input-lot-1", held[0].ProvenanceChain());
            Assert.AreEqual("cordwood", held[0].Species);
            Assert.AreEqual(1, runtime.CashBook.Count);
            Assert.AreEqual(FuelDealerRuntime.FuelDealerCashBookKind.OffMapPurchase, runtime.CashBook[0].Kind);
            Assert.AreEqual(-20, runtime.CashBook[0].AmountCents);
            Assert.AreEqual(10, runtime.CashBook[0].IntakeUnits);
            Assert.AreEqual(10, runtime.Suppliers[0].TotalFuelUnitsReceived);
            Assert.AreEqual(20, runtime.Suppliers[0].TotalCentsPaid);
            Assert.AreEqual(6, runtime.Suppliers[0].LastDeliveryDayIndex);
        }

        [Test]
        public void ReceiveOffMapOrder_ConversionFloorsNeverRoundsUp()
        {
            FuelDealerRuntime runtime = StockedRuntime(0, 100, out FuelYardByproductIntake intake, out FakeCashPort cash);
            var ids = new EntityIdRegistry();
            var diag = new List<string>();
            Assert.IsNull(runtime.RegisterOffMapSupplier("millbrook-fuel", "cordwood", 2, 0.5, diag));
            FuelDealerRuntime.FuelDealerOrder order = runtime.PlaceOffMapOrder("millbrook-fuel", 5, 5, diag);

            Assert.IsNull(runtime.ReceiveOffMapOrder(order.OrderId, "input-lot-1", 5, 6, ids, diag));
            Assert.AreEqual(2, order.FuelUnitsReceived, "floor(5 x 0.5) = 2 fuel units; fractional input never conjures a whole unit.");
            Assert.AreEqual(2, intake.TotalFuelWoodUnitsFor(DealerId));
            Assert.AreEqual(90, cash.BalanceCents, "the dealer still pays for all 5 input units received (5 x 2c).");
        }

        [Test]
        public void ReceiveOffMapOrder_CannotPayRefusesOrderStaysPlaced()
        {
            FuelDealerRuntime runtime = StockedRuntime(0, 5, out FuelYardByproductIntake intake, out FakeCashPort cash);
            var ids = new EntityIdRegistry();
            var diag = new List<string>();
            Assert.IsNull(runtime.RegisterOffMapSupplier("millbrook-fuel", "cordwood", 2, 1.0, diag));
            FuelDealerRuntime.FuelDealerOrder order = runtime.PlaceOffMapOrder("millbrook-fuel", 10, 5, diag);

            Assert.IsTrue(runtime.ReceiveOffMapOrder(order.OrderId, "input-lot-1", 10, 6, ids, diag) != null);
            Assert.AreEqual(FuelDealerRuntime.FuelDealerOrderStatus.Placed, order.Status,
                "a receipt the dealer cannot pay leaves the order placed for a later day, never half-received.");
            Assert.AreEqual(0, intake.TotalFuelWoodUnitsFor(DealerId));
            Assert.AreEqual(5, cash.BalanceCents, "no cash moved.");
            Assert.AreEqual(0, runtime.CashBook.Count);
            Assert.AreEqual(1, runtime.OutstandingOrders.Count);
        }

        [Test]
        public void ReceiveOffMapOrder_DoubleReceiptRefusesStockAndCashUnchanged()
        {
            FuelDealerRuntime runtime = StockedRuntime(0, 100, out FuelYardByproductIntake intake, out FakeCashPort cash);
            var ids = new EntityIdRegistry();
            var diag = new List<string>();
            Assert.IsNull(runtime.RegisterOffMapSupplier("millbrook-fuel", "cordwood", 2, 1.0, diag));
            FuelDealerRuntime.FuelDealerOrder order = runtime.PlaceOffMapOrder("millbrook-fuel", 10, 5, diag);
            Assert.IsNull(runtime.ReceiveOffMapOrder(order.OrderId, "input-lot-1", 10, 6, ids, diag));

            Assert.IsTrue(runtime.ReceiveOffMapOrder(order.OrderId, "input-lot-1", 10, 7, ids, diag) != null,
                "a received order can never be received again.");
            Assert.AreEqual(10, intake.TotalFuelWoodUnitsFor(DealerId), "no second lot.");
            Assert.AreEqual(80, cash.BalanceCents, "no second payment.");
            Assert.AreEqual(1, runtime.CashBook.Count, "no second book entry.");
        }

        [Test]
        public void ReceiveOffMapOrder_UnknownCancelledAndZeroRefuse()
        {
            FuelDealerRuntime runtime = StockedRuntime(0, 100, out _, out _);
            var ids = new EntityIdRegistry();
            var diag = new List<string>();
            Assert.IsTrue(runtime.ReceiveOffMapOrder("no-such-order", "input-lot-1", 10, 6, ids, diag) != null);
            Assert.IsNull(runtime.RegisterOffMapSupplier("millbrook-fuel", "cordwood", 2, 1.0, diag));
            FuelDealerRuntime.FuelDealerOrder order = runtime.PlaceOffMapOrder("millbrook-fuel", 10, 5, diag);
            Assert.IsTrue(runtime.ReceiveOffMapOrder(order.OrderId, "input-lot-1", 0, 6, ids, diag) != null);
            Assert.IsTrue(runtime.ReceiveOffMapOrder(order.OrderId, "", 10, 6, ids, diag) != null,
                "an unnamed input lot must refuse.");
            Assert.AreEqual(FuelDealerRuntime.FuelDealerOrderStatus.Placed, order.Status);
            Assert.IsNull(runtime.CancelOffMapOrder(order.OrderId, 6, diag));
            Assert.IsTrue(runtime.ReceiveOffMapOrder(order.OrderId, "input-lot-1", 10, 7, ids, diag) != null,
                "a cancelled order can never be received.");
        }

        [Test]
        public void CancelOffMapOrder_MovesNothingAndCannotReopen()
        {
            FuelDealerRuntime runtime = StockedRuntime(0, 100, out FuelYardByproductIntake intake, out FakeCashPort cash);
            var ids = new EntityIdRegistry();
            var diag = new List<string>();
            Assert.IsNull(runtime.RegisterOffMapSupplier("millbrook-fuel", "cordwood", 2, 1.0, diag));
            FuelDealerRuntime.FuelDealerOrder order = runtime.PlaceOffMapOrder("millbrook-fuel", 10, 5, diag);

            Assert.IsNull(runtime.CancelOffMapOrder(order.OrderId, 6, diag));
            Assert.AreEqual(FuelDealerRuntime.FuelDealerOrderStatus.Cancelled, order.Status);
            Assert.AreEqual(6, order.ClosedDayIndex);
            Assert.AreEqual(0, runtime.OutstandingOrders.Count);
            Assert.AreEqual(0, intake.TotalFuelWoodUnitsFor(DealerId));
            Assert.AreEqual(100, cash.BalanceCents);
            Assert.AreEqual(0, runtime.CashBook.Count);
            Assert.IsTrue(runtime.CancelOffMapOrder(order.OrderId, 7, diag) != null,
                "a cancelled order cannot be cancelled again.");
            Assert.IsTrue(runtime.CancelOffMapOrder("no-such-order", 7, diag) != null);
            FuelDealerRuntime.FuelDealerOrder received = runtime.PlaceOffMapOrder("millbrook-fuel", 10, 8, diag);
            Assert.IsNull(runtime.ReceiveOffMapOrder(received.OrderId, "input-lot-9", 10, 9, ids, diag));
            Assert.IsTrue(runtime.CancelOffMapOrder(received.OrderId, 10, diag) != null,
                "a received order cannot be cancelled.");
        }

        // --- Cash book ------------------------------------------------------

        [Test]
        public void RecordRetailSale_CashAndCreditAndRefused()
        {
            FuelDealerRuntime runtime = Runtime(new FuelYardByproductIntake(), new FakeCashPort());
            runtime.RecordRetailSale(4, 2, 20, 56, "rcpt-1", true, false);
            runtime.RecordRetailSale(5, 1, 10, 28, "rcpt-2", false, true);
            runtime.RecordRetailSale(6, 1, 10, 28, "rcpt-3", false, false);
            runtime.RecordRetailSale(7, 0, 0, 0, "rcpt-4", true, false);

            Assert.AreEqual(3, runtime.CashBook.Count, "the zero-bundle report is vacuous.");
            Assert.AreEqual(56, runtime.CashBook[0].AmountCents);
            Assert.AreEqual(FuelDealerRuntime.FuelDealerCashBookKind.RetailSale, runtime.CashBook[0].Kind);
            Assert.AreEqual(20, runtime.CashBook[0].IntakeUnits);
            Assert.AreEqual(0, runtime.CashBook[1].AmountCents,
                "a trade-credit sale moves no cash; the receivable is in the note, not the amount.");
            StringAssert.Contains("trade credit", runtime.CashBook[1].Note);
            Assert.AreEqual(0, runtime.CashBook[2].AmountCents,
                "a sale whose cash credit was refused records 0, not revenue that never arrived.");
            StringAssert.Contains("refused", runtime.CashBook[2].Note);
        }

        [Test]
        public void RecordBakerySale_RecordsRevenueAndLot()
        {
            FuelDealerRuntime runtime = Runtime(new FuelYardByproductIntake(), new FakeCashPort());
            runtime.RecordBakerySale(4, 3, 12, 33, "bakery-1", "bakery-lot-9");
            runtime.RecordBakerySale(5, 0, 0, 0, "bakery-1", "bakery-lot-10");

            Assert.AreEqual(1, runtime.CashBook.Count, "the zero-unit report is vacuous.");
            FuelDealerRuntime.FuelDealerCashBookEntry entry = runtime.CashBook[0];
            Assert.AreEqual(FuelDealerRuntime.FuelDealerCashBookKind.BakerySale, entry.Kind);
            Assert.AreEqual("bakery-1", entry.CounterpartyId);
            Assert.AreEqual(12, entry.IntakeUnits);
            Assert.AreEqual(33, entry.AmountCents);
            StringAssert.Contains("bakery-lot-9", entry.Note);
        }

        // --- Save / load -----------------------------------------------------

        [Test]
        public void SaveLoad_RoundTripsSuppliersOrdersBookAndPolicy()
        {
            FuelDealerRuntime runtime = StockedRuntime(0, 500, out _, out _);
            var ids = new EntityIdRegistry();
            var diag = new List<string>();
            runtime.SyncLocalSupplier(MillId, 1);
            runtime.RecordLocalPurchase(2, MillId, 35, 35, 2);
            Assert.IsNull(runtime.RegisterOffMapSupplier("millbrook-fuel", "cordwood", 2, 1.0, diag));
            FuelDealerRuntime.FuelDealerOrder placed = runtime.PlaceOffMapOrder("millbrook-fuel", 10, 5, diag);
            FuelDealerRuntime.FuelDealerOrder received = runtime.PlaceOffMapOrder("millbrook-fuel", 10, 5, diag);
            Assert.IsNull(runtime.ReceiveOffMapOrder(received.OrderId, "input-lot-1", 10, 6, ids, diag));
            runtime.RecordRetailSale(7, 2, 20, 56, "rcpt-1", true, false);
            runtime.RecordBakerySale(8, 3, 12, 33, "bakery-1", "bakery-lot-9");
            runtime.TargetStockIntakeUnits = 150;
            runtime.ReorderPointIntakeUnits = 40;
            runtime.ColdSeasonTargetMultiplier = 2.0;
            runtime.SetConfiguredBundlePriceCents(28);
            runtime.AssessLocalPurchase(9, 0, false);

            FuelDealerRuntime.FuelDealerRuntimeSaveDto dto = runtime.CaptureSaveDto();
            FuelDealerRuntime restored = Runtime(new FuelYardByproductIntake(), new FakeCashPort { BalanceCents = 0 });
            Assert.IsTrue(restored.LoadFromSaveDto(dto), "same dealer id must load.");

            Assert.AreEqual(150, restored.TargetStockIntakeUnits);
            Assert.AreEqual(40, restored.ReorderPointIntakeUnits);
            Assert.AreEqual(2.0, restored.ColdSeasonTargetMultiplier, 1e-9);
            Assert.AreEqual(28, restored.ConfiguredBundlePriceCents);
            Assert.AreEqual(2, restored.Suppliers.Count);
            Assert.AreEqual(35, restored.Suppliers[0].TotalFuelUnitsReceived);
            Assert.AreEqual(35, restored.Suppliers[0].TotalCentsPaid);
            Assert.AreEqual(2, restored.Suppliers[0].LastDeliveryDayIndex);
            Assert.AreEqual("millbrook-fuel", restored.Suppliers[1].SupplierId);
            Assert.AreEqual(10, restored.Suppliers[1].TotalFuelUnitsReceived);
            Assert.AreEqual(20, restored.Suppliers[1].TotalCentsPaid);
            Assert.AreEqual(2, restored.Orders.Count);
            Assert.AreEqual(FuelDealerRuntime.FuelDealerOrderStatus.Placed, restored.Orders[0].Status);
            Assert.AreEqual(placed.OrderId, restored.Orders[0].OrderId);
            Assert.AreEqual(FuelDealerRuntime.FuelDealerOrderStatus.Received, restored.Orders[1].Status);
            Assert.AreEqual(10, restored.Orders[1].FuelUnitsReceived);
            Assert.AreEqual("input-lot-1", restored.Orders[1].InputLotId);
            Assert.AreEqual(1, restored.OutstandingOrders.Count, "only the placed order is outstanding after restore.");
            Assert.AreEqual(4, restored.CashBook.Count);
            Assert.AreEqual(-35, restored.CashBook[0].AmountCents);
            Assert.AreEqual(-20, restored.CashBook[1].AmountCents);
            Assert.AreEqual(56, restored.CashBook[2].AmountCents);
            Assert.AreEqual(33, restored.CashBook[3].AmountCents);
            Assert.AreEqual(9, restored.LastProcurementDayIndex);
            Assert.IsTrue(restored.LastProcurementNote.Length > 0);
            // A third order placed after restore must not reuse an id.
            FuelDealerRuntime.FuelDealerOrder third = restored.PlaceOffMapOrder("millbrook-fuel", 5, 10, diag);
            Assert.IsTrue(third != null);
            Assert.IsTrue(third.OrderId != placed.OrderId && third.OrderId != received.OrderId,
                "the order sequence survives the round-trip; ids never repeat.");
        }

        [Test]
        public void SaveLoad_LoadTwiceIsIdempotent_NullClears_MismatchRefuses()
        {
            FuelDealerRuntime runtime = StockedRuntime(0, 500, out _, out _);
            runtime.SyncLocalSupplier(MillId, 1);
            runtime.RecordLocalPurchase(2, MillId, 35, 35, 2);
            FuelDealerRuntime.FuelDealerRuntimeSaveDto dto = runtime.CaptureSaveDto();

            FuelDealerRuntime restored = Runtime(new FuelYardByproductIntake(), new FakeCashPort());
            Assert.IsTrue(restored.LoadFromSaveDto(dto));
            int suppliersAfterFirst = restored.Suppliers.Count;
            int bookAfterFirst = restored.CashBook.Count;
            Assert.IsTrue(restored.LoadFromSaveDto(dto), "loading the same DTO twice must succeed.");
            Assert.AreEqual(suppliersAfterFirst, restored.Suppliers.Count, "no duplicated suppliers on second load.");
            Assert.AreEqual(bookAfterFirst, restored.CashBook.Count, "no duplicated book entries on second load.");

            Assert.IsTrue(restored.LoadFromSaveDto(null), "a null DTO is the honest old-save default.");
            Assert.AreEqual(0, restored.Suppliers.Count);
            Assert.AreEqual(0, restored.CashBook.Count);
            Assert.AreEqual(FuelDealerRuntime.DefaultTargetStockIntakeUnits, restored.TargetStockIntakeUnits);

            FuelDealerRuntime.FuelDealerRuntimeSaveDto wrongDealer = runtime.CaptureSaveDto();
            wrongDealer.DealerBusinessId = "another-dealer";
            Assert.IsFalse(restored.LoadFromSaveDto(wrongDealer),
                "a DTO for another dealer must be refused (identity guard).");
            Assert.AreEqual(0, restored.Suppliers.Count,
                "a refused load leaves the live state untouched — nothing was loaded, nothing was cleared.");
        }

        // --- Intake off-map receipt ------------------------------------------

        [Test]
        public void Intake_ReceiveOffMapLot_ValidatesChainAndDuplicates()
        {
            var intake = new FuelYardByproductIntake();
            var ids = new EntityIdRegistry();
            var diag = new List<string>();
            var lot = new SlabOffcutFuelLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FuelWoodUnits = 10,
                OwnerId = DealerId,
                Species = "cordwood",
                ProducedDayIndex = 6,
                OffMapSupplierId = "millbrook-fuel",
                OffMapOrderId = "fuel-order-1",
                OffMapInputLotId = "input-lot-1",
            };
            Assert.IsNull(intake.ReceiveOffMapLot(DealerId, lot, diag));
            Assert.AreEqual(10, intake.TotalFuelWoodUnitsFor(DealerId));
            StringAssert.Contains("off-map supplier millbrook-fuel", lot.ProvenanceChain());
            StringAssert.Contains("order fuel-order-1", lot.ProvenanceChain());
            StringAssert.Contains("input lot input-lot-1", lot.ProvenanceChain());

            // The same lot id cannot appear in a second yard.
            Assert.IsTrue(intake.ReceiveOffMapLot("fuel-dealer-2", lot, diag) != null);
            Assert.AreEqual(0, intake.TotalFuelWoodUnitsFor("fuel-dealer-2"));

            // A lot claiming both chains is refused.
            var bothChains = new SlabOffcutFuelLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FuelWoodUnits = 5,
                OwnerId = DealerId,
                SourceMillBusinessId = MillId,
                SourceLogLotId = "log-1",
                StandId = "stand-1",
                Species = "pine",
                ProducedDayIndex = 6,
                OffMapSupplierId = "millbrook-fuel",
                OffMapOrderId = "fuel-order-2",
                OffMapInputLotId = "input-lot-2",
            };
            Assert.IsTrue(intake.ReceiveOffMapLot(DealerId, bothChains, diag) != null);

            // A lot with no off-map chain is refused.
            var noChain = new SlabOffcutFuelLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FuelWoodUnits = 5,
                OwnerId = DealerId,
                Species = "cordwood",
                ProducedDayIndex = 6,
            };
            Assert.IsTrue(intake.ReceiveOffMapLot(DealerId, noChain, diag) != null);
            Assert.AreEqual(10, intake.TotalFuelWoodUnitsFor(DealerId), "no refused receipt leaves stock behind.");
        }

        [Test]
        public void Intake_OffMapLotsDispenseAndSaveRoundTrip()
        {
            var intake = new FuelYardByproductIntake();
            var ids = new EntityIdRegistry();
            var diag = new List<string>();
            StockMillFuel(intake, ids, 20);
            var offMapLot = new SlabOffcutFuelLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FuelWoodUnits = 10,
                OwnerId = DealerId,
                Species = "cordwood",
                ProducedDayIndex = 6,
                OffMapSupplierId = "millbrook-fuel",
                OffMapOrderId = "fuel-order-1",
                OffMapInputLotId = "input-lot-1",
            };
            Assert.IsNull(intake.ReceiveOffMapLot(DealerId, offMapLot, diag));
            Assert.AreEqual(30, intake.TotalFuelWoodUnitsFor(DealerId));

            // FIFO across both provenances: the mill lot (older) dispenses first.
            List<FuelYardByproductIntake.FuelYardDispenseLine> lines =
                intake.TryDispenseUnits(DealerId, 25, 7, diag);
            Assert.IsTrue(lines != null && lines.Count == 2);
            Assert.AreEqual(5, intake.TotalFuelWoodUnitsFor(DealerId), "25 of 30 dispensed; 5 off-map units remain.");

            // The DTO round-trip preserves off-map provenance (the Capture
            // copy must include the three new fields).
            FuelYardByproductIntake.FuelYardByproductIntakeSaveDto dto = intake.CaptureSaveDto();
            var restored = new FuelYardByproductIntake();
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(5, restored.TotalFuelWoodUnitsFor(DealerId));
            IReadOnlyList<SlabOffcutFuelLot> held = restored.LotsFor(DealerId);
            Assert.AreEqual(1, held.Count);
            Assert.AreEqual("millbrook-fuel", held[0].OffMapSupplierId);
            Assert.AreEqual("fuel-order-1", held[0].OffMapOrderId);
            Assert.AreEqual("input-lot-1", held[0].OffMapInputLotId);
            StringAssert.Contains("off-map supplier millbrook-fuel", held[0].ProvenanceChain());
        }

        // --- Structural: harness-excluded wiring (B-13 pattern) -------------

        private const string RuntimePath =
            "Assets/Scripts/Domains/Economy/Businesses/FuelDealer/FuelDealerRuntime.cs";
        private const string AdapterPath =
            "Assets/Scripts/Domains/Economy/Businesses/FuelDealer/Series2FuelDealerSupplierAdapter.cs";
        private const string DriversPath =
            "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";
        private const string Pass9SavePath =
            "Assets/Scripts/Infrastructure/Persistence/Series2Pass9Save.cs";
        private const string IntakePath =
            "Assets/Scripts/Domains/Economy/Businesses/Sawmill/SawmillByproductIntake.cs";
        private const string PaidSourcePath =
            "Assets/Scripts/Orchestration/Player/ThreeBusinessSlice.cs";
        private const string FuelDealerPath =
            "Assets/Scripts/Domains/Economy/FreightFuel/Series2FuelDealer.cs";

        [Test]
        public void Wiring_DriverCreatesRuntimeAndReportsRetailSales()
        {
            string code = ReadRepoFile(DriversPath);
            Assert.IsTrue(code.Contains("EnsureFuelDealerRuntime(pass9, business, priceCents)"),
                "registration must create (once) the per-instance runtime: " + DriversPath);
            Assert.IsTrue(code.Contains("adapter.OnRetailSaleRecorded = fuelDealerRuntime.RecordRetailSale"),
                "registration must wire retail sales into the dealer's cash book: " + DriversPath);
            Assert.IsTrue(code.Contains("FuelDealerRuntime.FuelDealerProcurementDecision.DeclineFull"),
                "the daily transfer must consult the purchasing decision: " + DriversPath);
            Assert.IsTrue(code.Contains("fuelDealerRuntime?.RecordLocalPurchase(dayIndex, millBusiness?.InstanceId,"),
                "the transfer must record the settled purchase from actual numbers: " + DriversPath);
            Assert.IsTrue(code.Contains("source.OnBakerySaleRecorded = pass9.FuelDealerRuntime.RecordBakerySale"),
                "the paid bakery source must report bakery sales into the cash book: " + DriversPath);
        }

        [Test]
        public void Wiring_AdapterReportsSalesAndPaidSourceReportsSales()
        {
            string adapter = ReadRepoFile(AdapterPath);
            Assert.IsTrue(adapter.Contains("public FuelDealerRetailSaleRecord OnRetailSaleRecorded { get; set; }"),
                "the adapter needs the cash-book hook property: " + AdapterPath);
            Assert.IsTrue(adapter.Contains("OnRetailSaleRecorded?.Invoke(dayIndex, sold, dispensed, revenue, lastReceiptId, cashCredited, onTradeCredit)"),
                "the adapter must report each settled retail sale: " + AdapterPath);
            string paid = ReadRepoFile(PaidSourcePath);
            Assert.IsTrue(paid.Contains("public FuelDealerBakerySaleRecord OnBakerySaleRecorded { get; set; }"),
                "the paid bakery source needs the cash-book hook property: " + PaidSourcePath);
            Assert.IsTrue(paid.Contains("OnBakerySaleRecorded?.Invoke(dayIndex, sale.Units, sale.IntakeUnits, sale.TotalCents, target.BusinessInstanceId, sale.BakeryLotId)"),
                "the paid source must report each settled bakery sale: " + PaidSourcePath);
        }

        [Test]
        public void Wiring_Pass9SaveHoldsRuntimeAndPending()
        {
            string code = ReadRepoFile(Pass9SavePath);
            Assert.IsTrue(code.Contains("public LandLedgers.Economy.Businesses.FuelDealer.FuelDealerRuntime FuelDealerRuntime;"),
                "the Pass 9 state must hold the live runtime: " + Pass9SavePath);
            Assert.IsTrue(code.Contains("public LandLedgers.Economy.Businesses.FuelDealer.FuelDealerRuntime.FuelDealerRuntimeSaveDto FuelDealerRuntime;"),
                "the Pass 9 DTO must carry the runtime section: " + Pass9SavePath);
            Assert.IsTrue(code.Contains("dto.FuelDealerRuntime = FuelDealerRuntime.CaptureSaveDto();"),
                "Capture must save the runtime: " + Pass9SavePath);
            Assert.IsTrue(code.Contains("FuelDealerRuntime.LoadFromSaveDto(dto.FuelDealerRuntime)"),
                "Restore must load the runtime in place: " + Pass9SavePath);
            Assert.IsTrue(code.Contains("RestoredFuelDealerRuntime = dto.FuelDealerRuntime;"),
                "Restore must hold the runtime DTO as pending when the holder does not exist yet: " + Pass9SavePath);
            Assert.IsTrue(code.Contains("public string ApplyPendingFuelDealerRuntime()"),
                "the state must offer the pending-runtime application: " + Pass9SavePath);
            Assert.IsTrue(code.Contains("RestoredFuelIntake = dto.FuelIntake;"),
                "Restore must hold the intake DTO as pending instead of dropping fresh-Continue lots: " + Pass9SavePath);
            Assert.IsFalse(code.Contains("fuel lots NOT restored."),
                "the old drop-the-lots branch must be gone: " + Pass9SavePath);
        }

        [Test]
        public void Wiring_IntakeAcceptsOffMapLotsWithProvenance()
        {
            string code = ReadRepoFile(IntakePath);
            Assert.IsTrue(code.Contains("public string ReceiveOffMapLot("),
                "the intake needs the off-map receipt path: " + IntakePath);
            Assert.IsTrue(code.Contains("OffMapSupplierId = lot.OffMapSupplierId,"),
                "the save copy must carry off-map provenance, or it is lost on save: " + IntakePath);
            Assert.IsFalse(code.Contains("There is no FuelDealer per-instance business package yet"),
                "the intake header must no longer claim there is no per-instance package: " + IntakePath);
        }

        [Test]
        public void Wiring_StaleOpenDecisionLanguageIsGone()
        {
            string code = ReadRepoFile(FuelDealerPath);
            Assert.IsFalse(code.Contains("byproduct price is an open Kennedy decision"),
                "the byproduct price is DECIDED (Decision 1, 1c/u); the open-decision language must be gone: " + FuelDealerPath);
            Assert.IsTrue(code.Contains("DECIDED by owner Decision 1"),
                "the custody class must name the decided price and its caller-side settlement: " + FuelDealerPath);
            string runtime = ReadRepoFile(RuntimePath);
            Assert.IsFalse(runtime.Contains("\"timberline\""),
                "no hardcoded Timberline supplier id may appear anywhere in the runtime: " + RuntimePath);
            Assert.IsTrue(runtime.Contains("no vendor is conjured"),
                "the runtime must refuse off-map requests loudly when no supplier is authored: " + RuntimePath);
        }

        // --- Repo-root discovery (B-13 pattern, copied from B-17) -----------

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrEmpty(fromEnv) && File.Exists(Path.Combine(fromEnv, RuntimePath)))
            {
                return fromEnv;
            }

            var starts = new[]
            {
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory,
            };
            var tried = new StringBuilder();
            foreach (string start in starts)
            {
                string dir = start;
                while (!string.IsNullOrEmpty(dir))
                {
                    foreach (string candidate in new[]
                    {
                        dir,
                        Path.Combine(dir, "work-b"),
                    })
                    {
                        tried.AppendLine("  " + candidate);
                        if (File.Exists(Path.Combine(candidate, RuntimePath)))
                        {
                            return candidate;
                        }
                    }

                    DirectoryInfo parent = Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign B (DEF-003): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign B (DEF-003): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }
    }
}
