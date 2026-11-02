using System.Collections.Generic;
using LandLedgers.Economy.Ordering;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass 3 (economy ordering): Roslyn tests for the new ordering
    /// surface only. Existing systems are not re-tested here; their
    /// own suites cover them. All prices, rates, quantities, and
    /// conversion rates are test parameters, never canon.
    /// </summary>
    [TestFixture]
    public sealed class Pass3EconomyOrderingTests
    {
        private static Pass3SupplierOffer Supplier(string id, int distanceMiles)
        {
            return new Pass3SupplierOffer
            {
                SupplierId = id,
                SupplierName = id,
                DistanceMiles = distanceMiles,
                SkuOffers = new List<Pass3SkuOffer>
                {
                    new Pass3SkuOffer { SkuId = "flour", UnitPriceCents = 100, UnitWeightLb = 10 },
                    new Pass3SkuOffer { SkuId = "bread", UnitPriceCents = 50, UnitWeightLb = 1 },
                    new Pass3SkuOffer { SkuId = "fuel-input", UnitPriceCents = 20, UnitWeightLb = 5 },
                },
            };
        }

        [Test]
        public void Margin_Percent_Amount_And_Price_Are_The_Same_Action()
        {
            var line = new Pass3ProductLine { ProductId = "flour", CostCents = 100, PriceCents = 100 };
            var diag = new List<string>();
            Assert.IsTrue(line.SetMarginPercent(50, diag));
            Assert.AreEqual(150, line.PriceCents);
            Assert.AreEqual(50, line.MarkupTotalCents());
            Assert.AreEqual(50.0, line.MarkupPercent());
            Assert.IsTrue(line.SetMarginAmount(25, diag));
            Assert.AreEqual(125, line.PriceCents);
            Assert.IsTrue(line.SetPrice(140, diag));
            Assert.AreEqual(40, line.MarkupTotalCents());
            Assert.AreEqual(40.0, line.MarkupPercent());
        }

        [Test]
        public void Margin_Rounding_Is_To_Nearest_Cent_And_Negative_Price_Refused()
        {
            var line = new Pass3ProductLine { ProductId = "flour", CostCents = 101, PriceCents = 101 };
            var diag = new List<string>();
            Assert.IsTrue(line.SetMarginPercent(50, diag));
            Assert.AreEqual(152, line.PriceCents); // 101 * 1.5 = 151.5, away from zero
            Assert.IsFalse(line.SetPrice(-1, diag));
            Assert.AreEqual(152, line.PriceCents);
            var noCost = new Pass3ProductLine { ProductId = "new", CostCents = 0, PriceCents = 0 };
            Assert.IsFalse(noCost.SetMarginPercent(50, diag));
        }

        [Test]
        public void Shelf_And_Back_Are_Separate_Pools_With_Capacity_And_Refusal()
        {
            var catalog = new Pass3StoreCatalog();
            var diag = new List<string>();
            var line = new Pass3ProductLine { ProductId = "bread", ShelfCapacityUnits = 10 };
            Assert.IsTrue(catalog.AddProduct(line, diag));
            Assert.AreEqual(0, line.ShelfUnits);
            Assert.AreEqual(0, line.BackUnits);
            // Simulate a back-storage intake by direct assignment in the
            // test only: the model itself adds back stock only via intake.
            line.BackUnits = 12;
            Assert.IsTrue(line.MoveToShelf(10, diag));
            Assert.AreEqual(10, line.ShelfUnits);
            Assert.AreEqual(2, line.BackUnits);
            Assert.IsFalse(line.MoveToShelf(1, diag)); // shelf capacity 10
            Assert.AreEqual(10, line.ShelfUnits);
            Assert.IsTrue(line.MoveToBack(4, diag));
            Assert.AreEqual(6, line.ShelfUnits);
            Assert.AreEqual(6, line.BackUnits);
            Assert.IsFalse(line.MoveToBack(99, diag));
        }

        [Test]
        public void Catalog_Add_Duplicate_Refused_And_Remove_With_Stock_Refused()
        {
            var catalog = new Pass3StoreCatalog();
            var diag = new List<string>();
            Assert.IsTrue(catalog.AddProduct(new Pass3ProductLine { ProductId = "flour" }, diag));
            Assert.IsFalse(catalog.AddProduct(new Pass3ProductLine { ProductId = "flour" }, diag));
            Assert.IsTrue(catalog.RemoveProduct("flour", diag));
            Assert.IsFalse(catalog.RemoveProduct("flour", diag));
            Assert.IsTrue(catalog.AddProduct(new Pass3ProductLine { ProductId = "bread" }, diag));
            catalog.Find("bread").BackUnits = 3;
            Assert.IsFalse(catalog.RemoveProduct("bread", diag));
            Assert.IsNotNull(catalog.Find("bread"));
        }

        [Test]
        public void Player_Automatic_Ordering_Refused_Employee_Delegate_Needs_Delegation()
        {
            var playerAuto = new Pass3OrderingSession { Actor = Pass3ActorKind.Player, IsAutomatic = true };
            Assert.IsNotNull(playerAuto.ValidateAutomatic());
            var delegateNoPermission = new Pass3OrderingSession { Actor = Pass3ActorKind.EmployeeDelegate, IsAutomatic = true, DelegationAllowed = false };
            Assert.IsNotNull(delegateNoPermission.ValidateAutomatic());
            var delegateAllowed = new Pass3OrderingSession { Actor = Pass3ActorKind.EmployeeDelegate, IsAutomatic = true, DelegationAllowed = true };
            Assert.IsNull(delegateAllowed.ValidateAutomatic());
            var playerManual = new Pass3OrderingSession { Actor = Pass3ActorKind.Player, IsAutomatic = false };
            Assert.IsNull(playerManual.ValidateAutomatic());
        }

        [Test]
        public void Order_Minimums_Refuse_Whole_Order_And_Never_Round_Up()
        {
            var supplier = Supplier("far", 100);
            supplier.MinimumAmountCents = 1000;
            supplier.MinimumQuantityUnits = 20;
            supplier.MinimumPoundageLb = 100;
            var diag = new List<string>();
            var draft = new Pass3OrderDraft { Supplier = supplier, FreightOption = Pass3FreightOption.SellerFreight, FreightCentsPerMile = 2 };
            Assert.IsTrue(draft.AddLine("flour", 5, diag)); // 500c, 5 units, 50 lb: below all three minimums
            Assert.IsNull(draft.Place(diag));
            Assert.AreEqual(5, draft.Lines[0].QuantityUnits); // not rounded up
            Assert.AreEqual(500, draft.GoodsTotalCents());
        }

        [Test]
        public void Order_Pickup_Only_Supplier_Refuses_Seller_And_Third_Party_Freight()
        {
            var supplier = Supplier("pickup-only", 50);
            supplier.IsPickupOnly = true;
            var diag = new List<string>();
            var draft = new Pass3OrderDraft { Supplier = supplier, FreightOption = Pass3FreightOption.SellerFreight, FreightCentsPerMile = 2 };
            Assert.IsTrue(draft.AddLine("flour", 2, diag));
            Assert.IsNull(draft.Place(diag));
            draft.FreightOption = Pass3FreightOption.SelfCollect;
            var requests = draft.Place(diag);
            Assert.IsNotNull(requests);
            Assert.AreEqual("AwaitingCollection", requests[0].Status);
            Assert.IsTrue(requests[0].RequiresJourney);
            Assert.AreEqual(0, requests[0].FreightAllocatedCents);
        }

        [Test]
        public void Order_Freight_Seller_SelfCollect_And_ThirdParty_And_Allocation_Sums_Exactly()
        {
            var supplier = Supplier("near", 100);
            var diag = new List<string>();
            var draft = new Pass3OrderDraft { Supplier = supplier, FreightOption = Pass3FreightOption.SellerFreight, FreightCentsPerMile = 2 };
            Assert.IsTrue(draft.AddLine("flour", 3, diag)); // 300c
            Assert.IsTrue(draft.AddLine("bread", 3, diag)); // 150c
            var requests = draft.Place(diag);
            Assert.IsNotNull(requests);
            int freightSum = 0;
            foreach (var r in requests) freightSum += r.FreightAllocatedCents;
            Assert.AreEqual(200, freightSum); // 100 miles x 2c
            Assert.AreEqual(300 + requests[0].FreightAllocatedCents, requests[0].TotalCents);
            draft.FreightOption = Pass3FreightOption.FreightBusiness;
            draft.FreightBusinessCommissionCents = 50;
            var third = draft.Place(diag);
            int thirdSum = 0;
            foreach (var r in third) thirdSum += r.FreightAllocatedCents;
            Assert.AreEqual(250, thirdSum);
        }

        [Test]
        public void Order_Unknown_Sku_Duplicate_Line_And_Empty_Draft_Refused()
        {
            var supplier = Supplier("near", 10);
            var diag = new List<string>();
            var draft = new Pass3OrderDraft { Supplier = supplier, FreightOption = Pass3FreightOption.SellerFreight, FreightCentsPerMile = 1 };
            Assert.IsFalse(draft.AddLine("cigarettes", 1, diag));
            Assert.IsTrue(draft.AddLine("flour", 1, diag));
            Assert.IsFalse(draft.AddLine("flour", 1, diag));
            var empty = new Pass3OrderDraft { Supplier = supplier, FreightOption = Pass3FreightOption.SellerFreight };
            Assert.IsNull(empty.Place(diag));
        }

        [Test]
        public void Reorder_Same_Supplier_Requoted_And_Blocked_When_Sku_Gone()
        {
            var supplier = Supplier("near", 10);
            var diag = new List<string>();
            var draft = new Pass3OrderDraft { Supplier = supplier, FreightOption = Pass3FreightOption.SellerFreight, FreightCentsPerMile = 1 };
            Assert.IsTrue(draft.AddLine("flour", 2, diag));
            var placed = draft.Place(diag);
            Assert.IsNotNull(placed);
            // Price change is visible on reorder, not hidden behind old prices.
            supplier.SkuOffers[0].UnitPriceCents = 120;
            var reordered = Pass3OrderDraft.ReorderFrom(supplier, placed, Pass3FreightOption.SellerFreight, 1, 0, Pass3ActorKind.Player, false, false, diag);
            Assert.IsNotNull(reordered);
            Assert.AreEqual(120, reordered.Lines[0].UnitPriceCents);
            // Supplier no longer offers the SKU: reorder blocked entirely.
            supplier.SkuOffers.RemoveAt(0);
            Assert.IsNull(Pass3OrderDraft.ReorderFrom(supplier, placed, Pass3FreightOption.SellerFreight, 1, 0, Pass3ActorKind.Player, false, false, diag));
        }

        [Test]
        public void Grouping_Multi_Supplier_Is_Guidance_Not_Refusal()
        {
            var diag = new List<string>();
            var a = new Pass3OrderDraft { Supplier = Supplier("a", 10) };
            var b = new Pass3OrderDraft { Supplier = Supplier("b", 200) };
            string note = Pass3SupplierGrouping.ConsolidationNote(new List<Pass3OrderDraft> { a, b });
            Assert.IsTrue(note.Contains("multi-supplier"));
            Assert.IsTrue(note.Contains("guidance"));
            string single = Pass3SupplierGrouping.ConsolidationNote(new List<Pass3OrderDraft> { a });
            Assert.IsTrue(single.Contains("single-supplier"));
        }

        [Test]
        public void Price_Randomiser_Deterministic_Within_Band_And_Never_Negative()
        {
            int first = Pass3PriceRandomiser.VariedPriceCents(100, 0.10, "supplier-a:flour");
            int second = Pass3PriceRandomiser.VariedPriceCents(100, 0.10, "supplier-a:flour");
            Assert.AreEqual(first, second);
            Assert.IsTrue(first >= 90 && first <= 110);
            Assert.AreEqual(0, Pass3PriceRandomiser.VariedPriceCents(0, 0.10, "x"));
            Assert.AreEqual(100, Pass3PriceRandomiser.VariedPriceCents(100, 0, "x"));
        }

        [Test]
        public void Fuel_Input_Generic_Sku_Conversion_Refuses_Unreceived_And_Converts_Received()
        {
            var supplier = Supplier("far", 100);
            var diag = new List<string>();
            var order = Pass3FuelInputOrder.Create("fuel-dealer", supplier, "fuel-input", 10, Pass3FreightOption.SellerFreight, 2, 0, 2.0, diag);
            Assert.IsNotNull(order);
            Assert.AreEqual("fuel-input", order.InputSkuId); // generic, caller-supplied, never defaulted
            Assert.IsNull(Pass3FuelInputConversion.Convert(order, diag)); // not received yet
            order.InputReceived = true;
            order.InputLotId = "input-lot-1";
            var output = Pass3FuelInputConversion.Convert(order, diag);
            Assert.IsNotNull(output);
            Assert.AreEqual(20, output.FuelUnits); // floor(10 x 2.0)
            Assert.IsTrue(output.Provenance.Contains("fuel-input"));
            Assert.IsTrue(output.Provenance.Contains("input-lot-1"));
        }

        [Test]
        public void Fuel_Input_Empty_Sku_Refused_And_No_Default_Input_Invented()
        {
            var supplier = Supplier("far", 100);
            var diag = new List<string>();
            Assert.IsNull(Pass3FuelInputOrder.Create("fuel-dealer", supplier, "", 10, Pass3FreightOption.SellerFreight, 2, 0, 2.0, diag));
            Assert.IsNull(Pass3FuelInputOrder.Create("fuel-dealer", supplier, null, 10, Pass3FreightOption.SellerFreight, 2, 0, 2.0, diag));
        }

        [Test]
        public void Transport_Pair_Horse_And_Wagon_Are_Distinct_Assets()
        {
            var pair = new Pass3TransportPair { HorseAssetId = "horse-1", WagonAssetId = "wagon-1", OwnerBusinessId = "fuel-dealer" };
            Assert.IsNull(pair.Validate());
            Assert.AreEqual("horse-1+wagon-1", pair.PairDisplayId());
            var same = new Pass3TransportPair { HorseAssetId = "asset-1", WagonAssetId = "asset-1" };
            Assert.IsNotNull(same.Validate());
            var missingWagon = new Pass3TransportPair { HorseAssetId = "horse-1", WagonAssetId = "" };
            Assert.IsNotNull(missingWagon.Validate());
        }

        [Test]
        public void Wholesale_Intake_Lands_In_Back_Only_With_Unsettled_Settlement()
        {
            var catalog = new Pass3StoreCatalog();
            var intake = new Pass3WholesaleIntake();
            var diag = new List<string>();
            var result = intake.Receive("bread-lot-1", "bread", 24, 6, 5, 5, catalog,
                "general-store-1", "bakery-1", diag);
            Assert.IsNotNull(result);
            Assert.AreEqual("Fresh", result.Condition);
            Assert.AreEqual(0, catalog.Find("bread").ShelfUnits);
            Assert.AreEqual(24, catalog.Find("bread").BackUnits);
            Assert.AreEqual(144, result.Settlement.AmountCents);
            Assert.IsFalse(result.Settlement.Settled);
            Assert.AreEqual("general-store-1", result.Settlement.PayerBusinessId);
            Assert.AreEqual("bakery-1", result.Settlement.PayeeBusinessId);
        }

        [Test]
        public void Wholesale_Intake_DayOld_Stale_And_Duplicate_All_Refused()
        {
            var catalog = new Pass3StoreCatalog();
            var intake = new Pass3WholesaleIntake();
            var diag = new List<string>();
            var dayOld = intake.Receive("bread-lot-2", "bread", 10, 6, 5, 6, catalog,
                "general-store-1", "bakery-1", diag);
            Assert.IsNotNull(dayOld);
            Assert.AreEqual("DayOld", dayOld.Condition);
            Assert.IsNull(intake.Receive("bread-lot-2", "bread", 10, 6, 5, 6, catalog,
                "general-store-1", "bakery-1", diag)); // duplicate
            Assert.IsNull(intake.Receive("bread-lot-3", "bread", 10, 6, 5, 7, catalog,
                "general-store-1", "bakery-1", diag)); // stale, 2 days past baked
            Assert.AreEqual(10, catalog.Find("bread").BackUnits); // only the day-old lot landed
        }

        [Test]
        public void Per_Item_Offer_Returns_One_Item_Never_The_Aggregate()
        {
            var catalog = new Pass3StoreCatalog();
            var diag = new List<string>();
            Assert.IsTrue(catalog.AddProduct(new Pass3ProductLine { ProductId = "flour", CostCents = 100, PriceCents = 120 }, diag));
            catalog.Find("flour").BackUnits = 5;
            var staple = new List<string> { "flour", "bread", "potatoes", "salt", "sugar", "coffee", "tea" };
            var flour = Pass3PerItemOffer.OfferForItem(staple, "flour", catalog, diag);
            Assert.IsNotNull(flour);
            Assert.AreEqual("flour", flour.ProductId);
            Assert.AreEqual(5, flour.BackUnits);
            Assert.IsNull(Pass3PerItemOffer.OfferForItem(staple, "bread", catalog, diag)); // in category, no catalog line
            Assert.IsNull(Pass3PerItemOffer.OfferForItem(staple, "soap", catalog, diag)); // not in category at all
        }
    }
}
