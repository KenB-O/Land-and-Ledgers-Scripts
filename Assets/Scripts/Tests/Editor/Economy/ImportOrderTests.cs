using System.Collections.Generic;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Economy.Trade;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// EQU-1: off-map raw material imports. Every unit is ordered, paid, and
    /// delivered through a declared trade link — real cost, real transit days,
    /// named origins. No unpaid material ever enters the world.
    /// </summary>
    [TestFixture]
    public sealed class ImportOrderTests
    {
        private static HouseholdLedger FundedLedger(int householdId, int balanceCents)
        {
            var ledger = new HouseholdLedger(householdId);
            ledger.RecordInflow(0, balanceCents, HouseholdIncomeSource.OtherDocumented,
                "test-endowment", "test endowment", "test");
            return ledger;
        }

        [Test]
        public void Order_Pay_Deliver_Chain_CarriesProvenance()
        {
            var service = new ImportService();
            var ledger = FundedLedger(1, 100000);
            var diagnostics = new List<string>();

            ImportOrder order = service.PlaceOrder(
                "smith-1", "Test Smithy", ImportCatalog.IronStockId, 100, 10,
                ledger, null, null, diagnostics);

            Assert.IsNotNull(order, "order should be placed: " + string.Join("; ", diagnostics));
            Assert.AreEqual(ImportOrderStatus.Ordered, order.Status);
            Assert.AreEqual(10 + ImportCatalog.Get(ImportCatalog.IronStockId).TransitDays,
                order.ExpectedArrivalDayIndex);
            Assert.IsFalse(order.PaidOnCredit);
            // Cash left the ledger with provenance.
            Assert.AreEqual(100000 - order.TotalCents, ledger.GetBalanceCents());

            // Early: nothing due.
            List<ImportLot> early = service.ReceiveDueImports(11, diagnostics);
            Assert.AreEqual(0, early.Count);

            // On arrival: lots with import provenance.
            List<ImportLot> lots = service.ReceiveDueImports(order.ExpectedArrivalDayIndex, diagnostics);
            Assert.AreEqual(1, lots.Count);
            Assert.IsTrue(lots[0].Imported);
            Assert.AreEqual("Pittsburgh ironworks, via railhead", lots[0].OriginName);
            Assert.AreEqual(order.OrderId, lots[0].OrderId);
            Assert.AreEqual(100, lots[0].Units);
            Assert.AreEqual(ImportOrderStatus.Delivered, order.Status);
        }

        [Test]
        public void Order_Refused_When_NoCash_And_NoCredit()
        {
            var service = new ImportService();
            var ledger = FundedLedger(2, 10); // nearly broke
            var diagnostics = new List<string>();

            ImportOrder order = service.PlaceOrder(
                "smith-1", "Test Smithy", ImportCatalog.SteelStockId, 100, 10,
                ledger, null, null, diagnostics);

            Assert.IsNull(order, "unpaid material must never enter the world");
            Assert.AreEqual(10, ledger.GetBalanceCents(), "no money moved");
            Assert.AreEqual(0, service.Orders.Count);
        }

        [Test]
        public void Order_OnCredit_Creates_Honest_Payable()
        {
            var service = new ImportService();
            var ledger = FundedLedger(3, 10);
            var liabilities = new BusinessLiabilityLedger();
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();

            ImportOrder order = service.PlaceOrder(
                "smith-1", "Test Smithy", ImportCatalog.ForgeCoalId, 50, 10,
                ledger, liabilities, ids, diagnostics);

            Assert.IsNotNull(order, "credit path should work: " + string.Join("; ", diagnostics));
            Assert.IsTrue(order.PaidOnCredit);
            Assert.AreEqual(1, liabilities.All.Count);
            Assert.AreEqual(order.TotalCents, liabilities.All[0].BalanceCents);
            Assert.AreEqual(10, ledger.GetBalanceCents(), "cash untouched — the debt is real");
        }

        [Test]
        public void Catalog_ExtensionPath_Registers_NewMaterial()
        {
            string rejection = ImportCatalog.RegisterMaterial(new ImportMaterial(
                "copper-stock", "Copper stock", "Off-map copper dealer, via railhead", 200, 13, 90));
            Assert.IsNull(rejection, rejection);
            Assert.IsNotNull(ImportCatalog.Get("copper-stock"));

            // Anonymous origins are refused — no anonymous sources.
            string bad = ImportCatalog.RegisterMaterial(new ImportMaterial(
                "mystery-metal", "Mystery metal", "", 10, 5, 10));
            Assert.IsNotNull(bad);
        }
    }
}
