using System.Collections.Generic;
using LandLedgers.Economy.Farming;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// FRM-1: farm produce → general store purchasing. Lots carry provenance
    /// (Tech X Part IV); the store can buy whatever (open product kinds); every sale
    /// records provenance on both sides (Canon XIII 13.2).
    /// </summary>
    [TestFixture]
    public sealed class FarmProduceSaleTests
    {
        private static BusinessRuntimeState NewStoreState()
        {
            var definition = new BusinessDefinition(BusinessType.GeneralStore, "S1", "Test Store");
            return BusinessRuntimeState.CreateFrom(
                definition,
                new List<ItemCategoryDefinition>(),
                new List<ItemDefinition>(),
                null,
                BusinessThroughputMode.Retail,
                0,
                0);
        }

        private static FarmProduceLot NewLot(string kind, int units)
        {
            return new FarmProduceLot("LOT-1", kind, units, 100, "F1", "Test Farm");
        }

        [Test]
        public void Sale_RecordsProvenanceOnBothSides()
        {
            BusinessRuntimeState store = NewStoreState();
            var ledger = new HouseholdLedger(7);
            var diagnostics = new List<string>();

            FarmProduceSale sale = FarmProduceMarket.ExecuteSale(
                NewLot("eggs", 36), store, "S1", "Test Store",
                10, 100, 0, ledger, diagnostics);

            Assert.IsNotNull(sale);
            Assert.AreEqual(36, sale.Units);
            Assert.AreEqual(360, sale.TotalCents);
            Assert.AreEqual("eggs", sale.ProductKind);

            // Seller household: SaleProceeds inflow with lot provenance.
            Assert.AreEqual(1, ledger.Entries.Count);
            Assert.AreEqual(HouseholdIncomeSource.SaleProceeds, ledger.Entries[0].Source);
            Assert.AreEqual("LOT-1", ledger.Entries[0].SourceReference);
            Assert.AreEqual(360, ledger.GetBalanceCents());

            // Store received the stock in a kind-derived category.
            CategoryStockState stock = store.GetCategoryStock(sale.StoreCategoryId);
            Assert.IsNotNull(stock);
            Assert.AreEqual(36, stock.CurrentStockUnits);
        }

        [Test]
        public void Store_CanBuyWhatever_OpenProductKindsAccepted()
        {
            BusinessRuntimeState store = NewStoreState();
            var ledger = new HouseholdLedger(7);
            var diagnostics = new List<string>();

            FarmProduceSale butter = FarmProduceMarket.ExecuteSale(
                new FarmProduceLot("LOT-2", "butter", 12, 100, "F1", "Test Farm"),
                store, "S1", "Test Store", 25, 100, 0, ledger, diagnostics);
            FarmProduceSale honey = FarmProduceMarket.ExecuteSale(
                new FarmProduceLot("LOT-3", "wildflower honey", 5, 100, "F1", "Test Farm"),
                store, "S1", "Test Store", 40, 100, 0, ledger, diagnostics);

            Assert.IsNotNull(butter);
            Assert.IsNotNull(honey);
            Assert.AreNotEqual(butter.StoreCategoryId, honey.StoreCategoryId);
            Assert.AreEqual(12, store.GetCategoryStock(butter.StoreCategoryId).CurrentStockUnits);
            Assert.AreEqual(5, store.GetCategoryStock(honey.StoreCategoryId).CurrentStockUnits);
        }

        [Test]
        public void Lot_SellsExactlyOnce()
        {
            BusinessRuntimeState store = NewStoreState();
            var ledger = new HouseholdLedger(7);

            FarmProduceLot lot = NewLot("eggs", 36);
            Assert.IsNotNull(FarmProduceMarket.ExecuteSale(
                lot, store, "S1", "Test Store", 10, 100, 0, ledger, new List<string>()));

            var second = new List<string>();
            Assert.IsNull(FarmProduceMarket.ExecuteSale(
                lot, store, "S1", "Test Store", 10, 101, 0, ledger, second));
            Assert.IsTrue(second[0].Contains("already sold"));
            Assert.AreEqual(360, ledger.GetBalanceCents()); // no double booking
        }

        [Test]
        public void Sale_BlockedWhenStoreCannotAfford()
        {
            BusinessRuntimeState store = NewStoreState(); // 25000c default cash
            var ledger = new HouseholdLedger(7);
            var diagnostics = new List<string>();

            // Price the lot above the store's whole cash.
            FarmProduceSale sale = FarmProduceMarket.ExecuteSale(
                NewLot("eggs", 36), store, "S1", "Test Store",
                100000, 100, 0, ledger, diagnostics);

            Assert.IsNull(sale);
            Assert.AreEqual(0, ledger.GetBalanceCents());
        }

        [Test]
        public void Sale_RequiresSellerLedgerForProvenance()
        {
            BusinessRuntimeState store = NewStoreState();
            var diagnostics = new List<string>();

            FarmProduceSale sale = FarmProduceMarket.ExecuteSale(
                NewLot("eggs", 36), store, "S1", "Test Store",
                10, 100, 0, null, diagnostics);

            Assert.IsNull(sale);
            Assert.IsTrue(diagnostics[0].Contains("provenance"));
        }
    }
}
