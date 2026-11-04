using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy;
using LandLedgers.Primitives;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class OffMapAidBusinessRuntimeTests
{
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

    [Test]
    public void MillbrookFlourOrder_DuplicateRequestDoesNotRepeatCashOrInventoryMovement()
    {
        OffMapAidBusinessDefinition definition = Resources.Load<OffMapAidBusinessDefinition>(
            "Core/Economy/OffMapAidBusinesses/MillbrookGrainCo");
        Assert.IsNotNull(definition, "The production Millbrook definition must be available to this test.");

        var supplier = new OffMapAidBusinessRuntime(definition);
        var bakeryCash = new FakeBusinessCashPort(20000);
        const string requestId = "opening-flour-b1";

        string firstRefusal = supplier.TryPlaceOrder(
            requestId, "bakery_001", "Glen Pierce", 1, bakeryCash, 0,
            out OffMapAidCargoLot firstLot);

        Assert.IsNull(firstRefusal);
        Assert.IsNotNull(firstLot);
        Assert.AreEqual("aidlot-millbrook_grain_co:opening-flour-b1", firstLot.LotId);
        Assert.AreEqual(48, firstLot.TotalCostCents);
        Assert.AreEqual(10, firstLot.InventoryUnits);
        Assert.AreEqual(18, firstLot.BakeryUnits);
        Assert.AreEqual(19952, bakeryCash.BalanceCents);
        Assert.AreEqual(48, supplier.CashCents);
        Assert.AreEqual(220, supplier.GetInventoryUnits("bakery_flour_sack"));

        string duplicateRefusal = supplier.TryPlaceOrder(
            requestId, "bakery_001", "Glen Pierce", 1, bakeryCash, 0,
            out OffMapAidCargoLot duplicateLot);

        Debug.Log($"[OffMapAidTest] duplicate order refused: reason='{duplicateRefusal}', " +
            $"lots={supplier.Lots.Count}, transactions={supplier.CashTransactions.Count}, " +
            $"Bakery cash={bakeryCash.BalanceCents}c, Millbrook cash={supplier.CashCents}c, " +
            $"flour={supplier.GetInventoryUnits("bakery_flour_sack")} lb.");
        StringAssert.Contains("request already exists", duplicateRefusal);
        Assert.IsNull(duplicateLot);
        Assert.AreEqual(1, supplier.Lots.Count);
        Assert.AreEqual(1, supplier.CashTransactions.Count);
        Assert.AreEqual(19952, bakeryCash.BalanceCents);
        Assert.AreEqual(48, supplier.CashCents);
        Assert.AreEqual(220, supplier.GetInventoryUnits("bakery_flour_sack"));
    }

    [Test]
    public void PaidMillbrookLot_FreightCustodyReceivesTenPoundsOnceAndPreservesSourceId()
    {
        OffMapAidBusinessDefinition definition = Resources.Load<OffMapAidBusinessDefinition>(
            "Core/Economy/OffMapAidBusinesses/MillbrookGrainCo");
        Assert.IsNotNull(definition);
        var supplier = new OffMapAidBusinessRuntime(definition);
        var bakeryCash = new FakeBusinessCashPort(20000);
        Assert.IsNull(supplier.TryPlaceOrder("freight-custody-test", "bakery_001", "Glen Pierce",
            1, bakeryCash, 0, out OffMapAidCargoLot sourceLot));

        var bakery = new BakeryShopRuntime("bakery_001");
        var adapter = new MillbrookFlourFreightCustodyAdapter(supplier, bakery, new EntityIdRegistry());
        var shipment = new LogisticsShipmentState
        {
            shipmentId = "test-millbrook-to-bakery",
            sourceBusinessInstanceId = supplier.BusinessId,
            destinationBusinessInstanceId = bakery.BusinessInstanceId,
            cargoLotId = sourceLot.LotId,
            cargoSkuId = sourceLot.SkuId,
            plannedQuantityUnits = sourceLot.PackageUnits,
            requiresSourceLotCustody = true,
        };

        Assert.IsTrue(adapter.CommitSourceLot(shipment, sourceLot.PackageUnits, out string pickupReason), pickupReason);
        Assert.IsTrue(adapter.ReceiveCargoLot(shipment, sourceLot.PackageUnits, 1, out string receiptReason), receiptReason);
        Assert.AreEqual(1, bakery.FlourStock.Lots.Count);
        Assert.AreEqual(10, bakery.FlourStock.Lots[0].Units, "The inventory transfer is pounds, not the separate 18 bakery-unit quotation.");
        Assert.AreEqual(BakeryBreadCatalog.FlourItemId, bakery.FlourStock.Lots[0].FlourName);
        Assert.AreEqual(sourceLot.LotId, bakery.FlourStock.Lots[0].ImportOrderId);
        Assert.IsTrue(sourceLot.SourceCommitted);
        Assert.IsTrue(sourceLot.Received);

        Assert.IsTrue(adapter.ReceiveCargoLot(shipment, sourceLot.PackageUnits, 1, out string duplicateReason), duplicateReason);
        Assert.AreEqual(1, bakery.FlourStock.Lots.Count, "A repeated shipment receipt must not duplicate the flour lot.");
        Assert.AreEqual(220, supplier.GetInventoryUnits("bakery_flour_sack"));
    }
}
