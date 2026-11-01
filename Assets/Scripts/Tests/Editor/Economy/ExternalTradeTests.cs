using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Pass VII Workstream A: external trade with explicit boundary accounting.
    /// External counterparties are identifiable with bounded budgets and finite
    /// supply; every inflow names its source; local cash is NOT conserved across
    /// the boundary without explicit accounting. Slice B (external commerce with
    /// boundary reconciliation) runs against the production authorities:
    /// ExternalMarketAuthority and BusinessRuntimeState.
    /// </summary>
    [TestFixture]
    public sealed class ExternalTradeTests
    {
        private sealed class Fixture
        {
            public ExternalMarketAuthority Authority = new ExternalMarketAuthority();
            public BusinessRuntimeState Mill = new BusinessRuntimeState();
            public List<string> Diag = new List<string>();

            public Fixture()
            {
                Authority.SetDayIndex(100);
                var kansas = new ExternalCounterparty
                {
                    CounterpartyId = "kansas-city-grain",
                    DisplayName = "Kansas City Grain Co.",
                    InitialBudgetCents = 50000,
                    TransitDays = 4,
                };
                kansas.SupplyUnits["flour"] = 200;
                Assert.IsNull(Authority.RegisterCounterparty(kansas));

                var stLouis = new ExternalCounterparty
                {
                    CounterpartyId = "st-louis-wholesaler",
                    DisplayName = "St. Louis Wholesaler",
                    InitialBudgetCents = 10000,
                    TransitDays = 6,
                };
                stLouis.SupplyUnits["tools_hardware"] = 60;
                Assert.IsNull(Authority.RegisterCounterparty(stLouis));

                Mill.BindBusinessIdentity("mill-2");
                Mill.SetCurrentCashCents(8000);
                Mill.EnsureCategoryStock("flour", 120, 200);
            }
        }

        // ---- External sale: identifiable counterparty, bounded budget, real goods. ----

        [Test]
        public void SliceB_ExternalSale_BoundedBudget_LocalSettlementWithProvenance()
        {
            var f = new Fixture();

            // The mill sells 40 flour to Kansas City at 300c/unit = 12000c.
            string receipt = f.Authority.RecordExternalSale(
                "kansas-city-grain", "flour", 40, 12000, f.Diag);
            Assert.IsFalse(string.IsNullOrEmpty(receipt), "sale within budget succeeds");

            // Local settlement through the production authority, buyer named, receipt as provenance.
            Assert.IsTrue(f.Mill.TryConsumeCategoryStockUnits("flour", 40, out int consumed));
            Assert.AreEqual(40, consumed);
            Assert.IsTrue(f.Mill.ResolveSettledSale("flour", 0, 12000, "external:kansas-city-grain"));

            // Counterparty budget bounded and decremented: 50000 -> 38000.
            Assert.AreEqual(38000, f.Authority.GetCounterparty("kansas-city-grain").BudgetCents);
            // Local cash explicitly increased by the inflow (NOT conserved: new money, accounted).
            Assert.AreEqual(20000, f.Mill.CurrentCashCents);
            // Real inventory declined on delivery.
            Assert.AreEqual(80, f.Mill.GetCategoryStock("flour").CurrentStockUnits);

            // Provenance: the trade record names counterparty, goods, price.
            ExternalTradeRecord trade = f.Authority.Trades[0];
            Assert.AreEqual(receipt, trade.ReceiptId);
            Assert.AreEqual("sale", trade.Direction);
            Assert.AreEqual("kansas-city-grain", trade.CounterpartyId);
            Assert.AreEqual("flour", trade.GoodsId);
            Assert.AreEqual(40, trade.Quantity);
            Assert.AreEqual(12000, trade.PriceCents);
            Assert.IsTrue(trade.Provenance.Contains("kansas-city-grain"));
        }

        [Test]
        public void ExternalSale_BudgetExceeded_Rejected_NoSale()
        {
            var f = new Fixture();

            // St. Louis has 10000c; a 15000c order must fail: never infinite demand.
            string receipt = f.Authority.RecordExternalSale(
                "st-louis-wholesaler", "flour", 50, 15000, f.Diag);

            Assert.IsTrue(string.IsNullOrEmpty(receipt), "counterparty cannot buy beyond its bounded budget");
            Assert.AreEqual(10000, f.Authority.GetCounterparty("st-louis-wholesaler").BudgetCents,
                "rejected sale leaves the budget untouched");
            Assert.AreEqual(0, f.Authority.Trades.Count, "no trade recorded");
            Assert.AreEqual(0, f.Authority.TotalInflowCents);
            Assert.AreEqual(8000, f.Mill.CurrentCashCents, "local cash untouched");
        }

        [Test]
        public void ExternalSale_UnknownCounterparty_Rejected()
        {
            var f = new Fixture();

            string receipt = f.Authority.RecordExternalSale("ghost-buyer", "flour", 10, 3000, f.Diag);

            Assert.IsTrue(string.IsNullOrEmpty(receipt));
            Assert.AreEqual(0, f.Authority.Trades.Count);
        }

        // ---- External purchase: finite supply, modeled (non-instant) fulfillment. ----

        [Test]
        public void ExternalPurchase_LocalCashPaysOut_RealOutflow()
        {
            var f = new Fixture();

            // The mill buys 20 tools from St. Louis at 500c/unit = 10000c.
            // (Mill cash is topped up first so the outflow is real.)
            f.Mill.AdjustCashCents(20000, recordNetDelta: true);
            string receipt = f.Authority.RecordExternalPurchase(
                "st-louis-wholesaler", "tools_hardware", 20, 10000, f.Diag);
            Assert.IsFalse(string.IsNullOrEmpty(receipt));

            // Local cash pays out at order time: a real outflow across the boundary.
            f.Mill.AdjustCashCents(-10000, recordNetDelta: true);
            Assert.AreEqual(18000, f.Mill.CurrentCashCents);
            Assert.AreEqual(10000, f.Authority.TotalOutflowCents);
            // Supply decremented at order time (reserved for us).
            Assert.AreEqual(40, f.Authority.GetCounterparty("st-louis-wholesaler").SupplyUnits["tools_hardware"]);
        }

        [Test]
        public void ExternalPurchase_OverSupply_Rejected()
        {
            var f = new Fixture();

            // St. Louis holds 60 tools; 61 is refused: external supply is finite.
            string receipt = f.Authority.RecordExternalPurchase(
                "st-louis-wholesaler", "tools_hardware", 61, 30500, f.Diag);

            Assert.IsTrue(string.IsNullOrEmpty(receipt));
            Assert.AreEqual(60, f.Authority.GetCounterparty("st-louis-wholesaler").SupplyUnits["tools_hardware"],
                "rejected purchase leaves supply untouched");
        }

        [Test]
        public void ExternalPurchase_ArrivesAfterTransit_NotInstant()
        {
            var f = new Fixture();

            string receipt = f.Authority.RecordExternalPurchase(
                "st-louis-wholesaler", "tools_hardware", 20, 10000, f.Diag);
            Assert.IsFalse(string.IsNullOrEmpty(receipt));

            ExternalTradeRecord trade = f.Authority.Trades[0];
            Assert.AreEqual(106, trade.FulfilledDayIndex, "6-day modeled transit, never instantaneous");
            Assert.IsFalse(trade.Fulfilled);

            // Before arrival: nothing to credit.
            List<ExternalTradeRecord> early = f.Authority.ResolveArrivals(105);
            Assert.AreEqual(0, early.Count);

            // On arrival day: the shipment arrives; the caller credits local stock.
            List<ExternalTradeRecord> arrived = f.Authority.ResolveArrivals(106);
            Assert.AreEqual(1, arrived.Count);
            Assert.AreEqual(receipt, arrived[0].ReceiptId);
            f.Mill.EnsureCategoryStock("tools_hardware", 0, 100);
            f.Mill.AddCategoryStockUnits("tools_hardware", arrived[0].Quantity);
            Assert.AreEqual(20, f.Mill.GetCategoryStock("tools_hardware").CurrentStockUnits);
        }

        // ---- Slice B: boundary reconciliation. ----

        [Test]
        public void SliceB_BoundaryReconciliation_InflowsOutflowsNet()
        {
            var f = new Fixture();

            f.Authority.RecordExternalSale("kansas-city-grain", "flour", 40, 12000, f.Diag);
            f.Authority.RecordExternalPurchase("st-louis-wholesaler", "tools_hardware", 20, 10000, f.Diag);

            (int inflow, int outflow, int net) = f.Authority.GetBoundaryReconciliation();

            Assert.AreEqual(12000, inflow, "cash that crossed IN is explicitly accounted");
            Assert.AreEqual(10000, outflow, "cash that crossed OUT is explicitly accounted");
            Assert.AreEqual(2000, net, "net boundary contribution reconciles");
            Assert.AreEqual(f.Authority.TotalInflowCents, inflow);
            Assert.AreEqual(f.Authority.TotalOutflowCents, outflow);
        }

        // ---- Mandatory: save/load cannot duplicate purchases. ----

        [Test]
        public void SaveLoad_CannotDuplicatePurchases()
        {
            var f = new Fixture();
            f.Authority.RecordExternalSale("kansas-city-grain", "flour", 40, 12000, f.Diag);
            f.Authority.RecordExternalPurchase("st-louis-wholesaler", "tools_hardware", 20, 10000, f.Diag);

            ExternalMarketAuthoritySaveDto dto = f.Authority.ExportState();

            var restored = new ExternalMarketAuthority();
            restored.ImportState(dto);
            // Importing the same snapshot again must not duplicate.
            restored.ImportState(dto);

            Assert.AreEqual(2, restored.Trades.Count, "no duplicated trade records");
            (int inflow, int outflow, int net) = restored.GetBoundaryReconciliation();
            Assert.AreEqual(12000, inflow, "totals recomputed from records, not doubled");
            Assert.AreEqual(10000, outflow);
            Assert.AreEqual(2000, net);
            Assert.AreEqual(38000, restored.GetCounterparty("kansas-city-grain").BudgetCents);
            Assert.AreEqual(40, restored.GetCounterparty("st-louis-wholesaler").SupplyUnits["tools_hardware"],
                "counterparty supply survives the round trip");
        }

        // ---- Mandatory: external trade preserves documented provenance. ----

        [Test]
        public void ExternalTrade_ProvenanceSurvivesSaveLoad()
        {
            var f = new Fixture();
            string receipt = f.Authority.RecordExternalSale("kansas-city-grain", "flour", 40, 12000, f.Diag);

            var restored = new ExternalMarketAuthority();
            restored.ImportState(f.Authority.ExportState());

            Assert.AreEqual(1, restored.Trades.Count);
            ExternalTradeRecord trade = restored.Trades[0];
            Assert.AreEqual(receipt, trade.ReceiptId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(trade.Provenance));
            Assert.IsTrue(trade.Provenance.Contains("kansas-city-grain"));
            Assert.IsTrue(trade.Provenance.Contains("flour"));
        }

        // ---- Counterparty budgets only move through modeled events. ----

        [Test]
        public void CounterpartyBudget_RestocksOnlyViaModeledEvent()
        {
            var f = new Fixture();
            f.Authority.RecordExternalSale("st-louis-wholesaler", "flour", 10, 10000, f.Diag);
            Assert.AreEqual(0, f.Authority.GetCounterparty("st-louis-wholesaler").BudgetCents);

            // A further sale is refused: the budget does not refill by fiat.
            Assert.IsTrue(string.IsNullOrEmpty(
                f.Authority.RecordExternalSale("st-louis-wholesaler", "flour", 1, 1000, f.Diag)));

            // An explicit modeled restock (new buying season) restores capacity, logged.
            f.Authority.RestockCounterparty("st-louis-wholesaler", 20000, "spring buying season", f.Diag);
            Assert.AreEqual(20000, f.Authority.GetCounterparty("st-louis-wholesaler").BudgetCents);
            Assert.IsFalse(string.IsNullOrEmpty(
                f.Authority.RecordExternalSale("st-louis-wholesaler", "flour", 1, 1000, f.Diag)));
        }
    }
}
