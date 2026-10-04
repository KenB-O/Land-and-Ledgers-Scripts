using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.ImplementDealer;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// T1H: the agricultural-implement dealer as a real business. Honest equipment
    /// sourcing beyond the smith's forge: named suppliers, machines sold on notes,
    /// parts stocked, simple repairs in-house, heavy work referred to the blacksmith.
    /// </summary>
    [TestFixture]
    public sealed class ImplementDealerTests
    {
        private static ImplementDealer NewDealer()
        {
            return new ImplementDealer("dealer-1", "Hart & Sons Implements", "main-st");
        }

        [Test]
        public void StockModel_RequiresNamedSupplier()
        {
            var dealer = NewDealer();
            var diagnostics = new List<string>();

            string orphan = dealer.StockModel(new ImplementModel
            {
                ModelId = "plow-14",
                DisplayName = "14-inch Walking Plow",
                Kind = "plow",
                PriceCents = 4500,
                SupplierName = "", // orphan
            }, 2, diagnostics);

            Assert.NotNull(orphan, "Orphan inventory is refused.");
            Assert.AreEqual(0, dealer.Models.Count);

            Assert.IsNull(dealer.StockModel(new ImplementModel
            {
                ModelId = "plow-14",
                DisplayName = "14-inch Walking Plow",
                Kind = "plow",
                PriceCents = 4500,
                SupplierName = "Springfield Works",
                SupplierVia = "equ-1 import",
            }, 2, diagnostics));
            Assert.AreEqual(2, dealer.Models[0].UnitsOnHand);
        }

        [Test]
        public void SellImplement_CreatesAsset_OnNotes()
        {
            var dealer = NewDealer();
            var diagnostics = new List<string>();
            dealer.StockModel(new ImplementModel
            {
                ModelId = "plow-14",
                DisplayName = "14-inch Walking Plow",
                Kind = "plow",
                PriceCents = 4500,
                SupplierName = "Springfield Works",
            }, 2, diagnostics);

            var ids = new EntityIdRegistry();
            ImplementSale sale = dealer.SellImplement(ids, "plow-14", "household", "household-9",
                downPaymentCents: 900, installmentCount: 4, dayIndex: 410, diagnostics: diagnostics);

            Assert.NotNull(sale, string.Join("; ", diagnostics));
            Assert.IsFalse(string.IsNullOrWhiteSpace(sale.AssetId), "The sale creates a real equipment asset.");
            Assert.IsTrue(sale.SaleId.IsValid);
            Assert.AreEqual(900, sale.DownPaymentCents);
            Assert.AreEqual(4, sale.InstallmentsRemaining);
            Assert.AreEqual(900, sale.InstallmentCents, "(4500 - 900) / 4.");
            Assert.AreEqual(1, dealer.Models[0].UnitsOnHand, "Stock decrements.");

            // Collecting the notes settles the sale.
            int collected = 0;
            for (int i = 0; i < 4; i++) collected += sale.CollectInstallment();
            Assert.AreEqual(3600, collected);
            Assert.IsTrue(sale.Settled);
            Assert.AreEqual(0, sale.CollectInstallment(), "Settled sales collect nothing more.");
        }

        [Test]
        public void SellImplement_Refuses_AnonymousBuyer_OrEmptyStock()
        {
            var dealer = NewDealer();
            var diagnostics = new List<string>();
            dealer.StockModel(new ImplementModel
            {
                ModelId = "plow-14",
                DisplayName = "Plow",
                Kind = "plow",
                PriceCents = 4500,
                SupplierName = "Springfield Works",
            }, 1, diagnostics);

            var ids = new EntityIdRegistry();
            Assert.IsNull(dealer.SellImplement(ids, "plow-14", "household", "", 0, 1, 410, diagnostics),
                "No anonymous buyers.");

            dealer.SellImplement(ids, "plow-14", "household", "household-9", 0, 1, 410, diagnostics);
            Assert.IsNull(dealer.SellImplement(ids, "plow-14", "household", "household-9", 0, 1, 410, diagnostics),
                "Empty stock sells nothing.");
        }

        [Test]
        public void RepairDesk_SimpleInHouse_HeavyToSmith()
        {
            var dealer = NewDealer();
            var diagnostics = new List<string>();
            var smithQueue = new RepairQueue();

            int? simple = dealer.DiagnoseRepair("asset-1", "walking plow", "loose share",
                isHeavyWork: false, customerName: "Morrow", customerBusinessId: "farm-1",
                smithQueue: smithQueue, dayIndex: 411, diagnostics: diagnostics);

            Assert.AreEqual(dealer.SimpleRepairLaborCents, simple);
            Assert.AreEqual(0, smithQueue.Orders.Count, "Simple work never touches the smith's queue.");

            int? heavy = dealer.DiagnoseRepair("asset-2", "reaper", "cracked frame",
                isHeavyWork: true, customerName: "Morrow", customerBusinessId: "farm-1",
                smithQueue: smithQueue, dayIndex: 411, diagnostics: diagnostics);

            Assert.IsNull(heavy, "Heavy work is referred, not priced in-house.");
            Assert.AreEqual(1, smithQueue.Orders.Count, "The referral lands in the smith's queue — repair demand the smith feeds on.");
            Assert.AreEqual("smithing", smithQueue.Orders[0].RequiredCapability);
            Assert.AreEqual("asset-2", smithQueue.Orders[0].AssetId);
        }

        [Test]
        public void SellPart_MovesFiniteStock()
        {
            var dealer = NewDealer();
            var diagnostics = new List<string>();
            dealer.StockPart(new PartLine
            {
                PartId = "share-14",
                DisplayName = "14-inch Plowshare",
                FitsModelId = "plow-14",
                PriceCents = 120,
                SupplierName = "Springfield Works",
            }, 6, diagnostics);

            Assert.AreEqual(4, dealer.SellPart("share-14", 4, "farm-1", diagnostics));
            Assert.AreEqual(2, dealer.Parts[0].UnitsOnHand);
            // P3 verdict on the D4L mismatch: partial fulfillment is canon-correct
            // (Canon §35 — "Do not reduce every shortage to Agreement Failed";
            // §3.4 "reduce quantity"). The dealer sells what is on hand instead of
            // refusing the whole request; oversell (going below zero) never happens.
            Assert.AreEqual(2, dealer.SellPart("share-14", 4, "farm-1", diagnostics), "Short sale sells remaining stock.");
            Assert.AreEqual(0, dealer.Parts[0].UnitsOnHand);
            Assert.AreEqual(0, dealer.SellPart("share-14", 4, "farm-1", diagnostics), "Empty stock sells nothing.");
            Assert.AreEqual(0, dealer.Parts[0].UnitsOnHand);
        }

        [Test]
        public void SellPart_NonPositiveUnits_MoveNothing()
        {
            var dealer = NewDealer();
            var diagnostics = new List<string>();
            dealer.StockPart(new PartLine
            {
                PartId = "share-14",
                DisplayName = "14-inch Plowshare",
                FitsModelId = "plow-14",
                PriceCents = 120,
                SupplierName = "Springfield Works",
            }, 6, diagnostics);

            Assert.AreEqual(0, dealer.SellPart("share-14", 0, "farm-1", diagnostics));
            Assert.AreEqual(0, dealer.SellPart("share-14", -3, "farm-1", diagnostics));
            Assert.AreEqual(6, dealer.Parts[0].UnitsOnHand, "Non-positive quantities must never move stock.");
        }

        [Test]
        public void SellPart_ShortSale_RecordsMerchantEvidence()
        {
            var dealer = NewDealer();
            var diagnostics = new List<string>();
            dealer.StockPart(new PartLine
            {
                PartId = "share-14",
                DisplayName = "14-inch Plowshare",
                FitsModelId = "plow-14",
                PriceCents = 120,
                SupplierName = "Springfield Works",
            }, 2, diagnostics);

            diagnostics.Clear();
            Assert.AreEqual(2, dealer.SellPart("share-14", 4, "farm-1", diagnostics));
            Assert.IsTrue(diagnostics.Exists(d => d.Contains("short sale")),
                "Short sales must write merchant evidence: " + string.Join(" | ", diagnostics));
        }
    }
}
