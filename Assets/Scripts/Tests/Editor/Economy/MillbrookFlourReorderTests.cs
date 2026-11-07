using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Freight;
using NUnit.Framework;

/// <summary>
/// Plan 02 §7 tests 13, 15–18 (Plan 10 slice E1): the repeatable
/// Millbrook flour reorder. The opening order is placed once by
/// DriveMillbrookAidTable; these tests pin the second-and-later order
/// path (PlaceReorder) and the negotiation predicate (FindShippableLot).
/// </summary>
[TestFixture]
public sealed class MillbrookFlourReorderTests
{
    private const string BuyerBusinessId = "bakery_001";
    private const string BuyerName = "Glen Pierce";

    private sealed class FakeBusinessCashPort : IBusinessCashPort
    {
        public int BalanceCents { get; private set; }

        public FakeBusinessCashPort(int balanceCents) => BalanceCents = balanceCents;

        public string TryDebit(int amountCents, string reason)
        {
            if (amountCents <= 0 || amountCents > BalanceCents)
                return "test cash port refused debit";
            BalanceCents -= amountCents;
            return null;
        }

        public string Credit(int amountCents, string reason)
        {
            if (amountCents <= 0) return "test cash port refused credit";
            BalanceCents += amountCents;
            return null;
        }
    }

    private static OffMapAidBusinessRuntime NewSupplier()
    {
        // The production Millbrook definition is asset-backed
        // (Resources.Load) and the harness has no Resources shim, so the
        // test builds the same table in code: values copied from
        // MillbrookGrainCo.asset (230 lb starting, 48c/sack, 10 lb per
        // sack, 18 bakery units per sack). Production still loads the
        // asset; this mirrors it, it does not replace it.
        var definition = new OffMapAidBusinessDefinition
        {
            BusinessId = "millbrook_grain_co",
            DisplayName = "Millbrook Grain Co.",
            PickupRouteMilesFromTown = 50f,
            Inventory = new List<OffMapAidInventoryRow>
            {
                new OffMapAidInventoryRow
                {
                    ItemId = "bakery_flour_sack",
                    DisplayName = "Flour",
                    InventoryUnit = "lb",
                    StartingUnits = 230,
                    OrderUnit = "10 lb sack",
                    InventoryUnitsPerOrderUnit = 10,
                    BakeryUnitsPerOrderUnit = 18,
                    OrderPriceCents = 48,
                },
            },
        };
        return new OffMapAidBusinessRuntime(definition);
    }

    [Test]
    public void RefusedRequestLotBlocksAllLaterReorders()
    {
        // Plan 07 Slice 0 discriminator, reorder half (AUD-334): the
        // bricked lot is uncommitted and unreceived forever, so the
        // single-outstanding rule refuses every later reorder naming it.
        // The lot itself is fine; the request for it is the bricked part.
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 0, diag, out OffMapAidCargoLot firstLot));
        Assert.IsNotNull(firstLot);
        Assert.AreEqual("aidlot-millbrook_grain_co:flour-reorder-d0-n1", firstLot.LotId);
        Assert.AreEqual(48, firstLot.TotalCostCents, "1 sack x 48c paid");
        Assert.AreEqual(19952, bakeryCash.BalanceCents, "buyer debited exactly 48c");
        Assert.AreEqual(220, supplier.GetInventoryUnits("bakery_flour_sack"), "230 lb - 10 lb");
        Assert.AreSame(firstLot, MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId),
            "the lot is shippable; only its freight request is bricked");

        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 0, diag, out OffMapAidCargoLot duplicateLot);

        Assert.IsNotNull(refusal, "a second reorder for a covered need must refuse");
        Assert.IsTrue(refusal.Contains(firstLot.LotId),
            "refusal names the outstanding lot; refusal: " + refusal);
        Assert.IsNull(duplicateLot);
        Assert.AreEqual(1, supplier.Lots.Count, "no second lot placed");
        Assert.AreEqual(19952, bakeryCash.BalanceCents, "refused attempt moves no buyer cash");
        Assert.AreEqual(220, supplier.GetInventoryUnits("bakery_flour_sack"), "refused attempt moves no stock");
    }

    [Test]
    public void ReopenedLotUnblocksThePipeline()
    {
        // Plan 07 Slice 1b (Q02, AUD-334): after the book-level reopen,
        // the same lot is still the shippable lot and the
        // single-outstanding rule still correctly refuses a duplicate
        // reorder while it is outstanding; once the lot commits,
        // FindShippableLot moves to the next lot.
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 0, diag, out OffMapAidCargoLot firstLot));
        Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 2, 0, diag, out OffMapAidCargoLot secondLot));
        Assert.AreSame(firstLot, MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId));

        var book = new FreightServiceAgreementBook();
        string requestId = "freight-request:" + firstLot.OrderId;
        var request = new FreightServiceRequest
        {
            RequestId = requestId, RequestingBusinessId = BuyerBusinessId,
            CarrierBusinessId = "livery_freight_003", PickupBusinessId = "millbrook_grain_co",
            DestinationBusinessId = BuyerBusinessId, CargoLotId = firstLot.LotId, Quantity = 1,
            RequestingPersonId = 4, CarrierOperatorPersonId = 7
        };
        Assert.IsNull(book.CreateRequest(request, out _));
        Assert.IsNull(book.SetStatus(requestId, FreightServiceRequestStatus.MeetingScheduled));
        Assert.IsNull(book.SetStatus(requestId, FreightServiceRequestStatus.Refused,
            "Bakery B1 has 40c available for a 125c agreed charge."));
        Assert.IsNull(book.ReopenRefusedRequest(requestId,
            "Lot '" + firstLot.LotId + "' is still shippable; reopening the refused request for a retried meeting."));
        Assert.AreEqual(FreightServiceRequestStatus.Open, book.FindRequest(requestId).Status);

        Assert.AreSame(firstLot, MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId),
            "the reopened request still targets the same lot");
        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 0, diag, out OffMapAidCargoLot duplicateLot);
        Assert.IsNotNull(refusal, "the outstanding lot still correctly blocks a duplicate reorder");
        Assert.IsTrue(refusal.Contains(firstLot.LotId), "refusal: " + refusal);
        Assert.IsNull(duplicateLot);

        Assert.IsNull(supplier.CommitFreightPickup(firstLot.LotId, "shipment-1", 1),
            "commit of the first lot should succeed");
        Assert.AreSame(secondLot, MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId),
            "after the first lot commits, the next lot is shippable");
    }

    [Test]
    public void PlaceReorder_PlacesUniqueLot_BuyerDebitedOnce_SupplierStockDebited()
    {
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 2, 3, diag, out OffMapAidCargoLot lot);

        Assert.IsNull(refusal, "reorder should succeed; diag: " + string.Join(" | ", diag));
        Assert.IsNotNull(lot);
        Assert.AreEqual("aidlot-millbrook_grain_co:flour-reorder-d3-n1", lot.LotId);
        Assert.AreEqual("millbrook_grain_co:flour-reorder-d3-n1", lot.OrderId);
        Assert.AreEqual(2, lot.PackageUnits);
        Assert.AreEqual(20, lot.InventoryUnits, "2 sacks = 20 lb");
        Assert.AreEqual(96, lot.TotalCostCents, "2 sacks x 48c");
        Assert.AreEqual(19904, bakeryCash.BalanceCents, "buyer debited exactly once");
        Assert.AreEqual(96, supplier.CashCents);
        Assert.AreEqual(210, supplier.GetInventoryUnits("bakery_flour_sack"), "230 lb - 20 lb");
        Assert.AreEqual(1, supplier.Lots.Count);
    }

    [Test]
    public void PlaceReorder_OutstandingLotBlocksDuplicate_RefusalNamesTheLot()
    {
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 3, diag, out OffMapAidCargoLot firstLot));
        Assert.IsNotNull(firstLot);

        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 3, diag, out OffMapAidCargoLot duplicateLot);

        Assert.IsNotNull(refusal, "a duplicate reorder for a covered need must refuse");
        Assert.IsTrue(refusal.Contains(firstLot.LotId),
            "refusal names the outstanding lot; refusal: " + refusal);
        Assert.IsNull(duplicateLot);
        Assert.AreEqual(1, supplier.Lots.Count, "no second lot placed");
        Assert.AreEqual(19952, bakeryCash.BalanceCents, "buyer debited once only (48c for the first lot)");
        Assert.AreEqual(220, supplier.GetInventoryUnits("bakery_flour_sack"), "supplier stock debited once only");
    }

    [Test]
    public void PlaceReorder_OutstandingLotSmallerThanNeed_PlacesSecondLot()
    {
        // The guard is "covers the need", not "any outstanding lot": a
        // 1-sack outstanding lot does not cover a 2-sack need, so the
        // second order is placed with a distinct request id.
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 3, diag, out OffMapAidCargoLot firstLot));
        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 2, 3, diag, out OffMapAidCargoLot secondLot);

        Assert.IsNull(refusal, "refusal: " + refusal);
        Assert.IsNotNull(secondLot);
        Assert.AreNotEqual(firstLot.LotId, secondLot.LotId);
        Assert.AreEqual("aidlot-millbrook_grain_co:flour-reorder-d3-n2", secondLot.LotId);
        Assert.AreEqual(2, supplier.Lots.Count);
    }

    [Test]
    public void PlaceReorder_ZeroSacksRefuses_NothingMoved()
    {
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 0, 3, diag, out OffMapAidCargoLot lot);

        Assert.IsNotNull(refusal);
        Assert.IsNull(lot);
        Assert.AreEqual(0, supplier.Lots.Count);
        Assert.AreEqual(20000, bakeryCash.BalanceCents);
        Assert.AreEqual(230, supplier.GetInventoryUnits("bakery_flour_sack"));
    }

    [Test]
    public void PlaceReorder_InsufficientSupplierStockRefuses_NothingMoved()
    {
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        // Fresh Millbrook stock is 230 lb = 23 sacks; 24 sacks cannot be covered.
        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 24, 3, diag, out OffMapAidCargoLot lot);

        Assert.IsNotNull(refusal);
        Assert.IsTrue(refusal.Contains("23 sack(s)"), "refusal names live availability; refusal: " + refusal);
        Assert.IsNull(lot);
        Assert.AreEqual(0, supplier.Lots.Count);
        Assert.AreEqual(20000, bakeryCash.BalanceCents);
        Assert.AreEqual(230, supplier.GetInventoryUnits("bakery_flour_sack"));
    }

    [Test]
    public void PlaceReorder_InsufficientBuyerCashRefuses_NothingMoved()
    {
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(10);
        var diag = new List<string>();

        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 3, diag, out OffMapAidCargoLot lot);

        Assert.IsNotNull(refusal);
        Assert.IsNull(lot);
        Assert.AreEqual(0, supplier.Lots.Count);
        Assert.AreEqual(10, bakeryCash.BalanceCents);
        Assert.AreEqual(230, supplier.GetInventoryUnits("bakery_flour_sack"));
    }

    [Test]
    public void FindShippableLot_ReturnsOldestThenNextThenNull()
    {
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        Assert.IsNull(MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId),
            "no lots yet — nothing shippable");
        Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 3, diag, out OffMapAidCargoLot firstLot));
        Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 2, 3, diag, out OffMapAidCargoLot secondLot));

        Assert.AreSame(firstLot, MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId),
            "oldest uncommitted lot first");
        Assert.IsNull(supplier.CommitFreightPickup(firstLot.LotId, "shipment-1", 1),
            "commit of the first lot should succeed");
        Assert.AreSame(secondLot, MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId),
            "after the first lot commits, the next uncommitted lot is shippable");
        Assert.IsNull(supplier.CommitFreightPickup(secondLot.LotId, "shipment-2", 2));
        Assert.IsNull(supplier.ReceiveAtBakery(secondLot.LotId, "shipment-2", BuyerBusinessId, out int bakeryUnits));
        Assert.AreEqual(36, bakeryUnits, "2 sacks x 18 bakery units (provenance only)");
        Assert.IsNull(MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId),
            "both lots committed/received — nothing shippable");
        Assert.IsNull(MillbrookFlourReorder.FindShippableLot(supplier, "some-other-buyer"),
            "lots for another buyer are never shippable for this buyer");
    }
}
