using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GrainElevator;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W5B: the grain-elevator runtime. Two books that never mix — custody
    /// (bailment, receipts name real stored lots) and dealer stock (forward
    /// lots name real held grain); grading as data; loud refusal of
    /// anonymous grain, anonymous customers, and double-selling; storage
    /// fees as data; save/load round-trips.
    /// </summary>
    [TestFixture]
    public sealed class GrainElevatorRuntimeTests
    {
        private static CropLot GrainLot(EntityIdRegistry registry, CropKind crop, int units, string farmId = "farm-7")
        {
            return new CropLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                Crop = crop,
                ProductKind = "grain",
                QuantityUnits = units,
                FieldId = "field-9",
                FarmId = farmId,
                HarvestDayIndex = 200,
                SeedSource = "saved seed",
            };
        }

        private static GrainElevatorShopRuntime NewElevator(EntityIdRegistry registry)
        {
            return new GrainElevatorShopRuntime("elevator-1", registry);
        }

        [Test]
        public void CustodyIntake_IssuesReceiptNamingStoredLot()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var receipt = elevator.ReceiveForStorage(
                GrainLot(registry, CropKind.Wheat, 100), "holder-1", "Ada Farmer",
                "no1", 210, diag);

            Assert.IsNotNull(receipt, "custody intake should succeed: " + string.Join(" | ", diag));
            Assert.IsTrue(receipt.ReceiptId.IsValid);
            Assert.AreEqual(100, receipt.UnitsIssued);
            Assert.AreEqual(100, receipt.UnitsRemaining);
            Assert.AreEqual("holder-1", receipt.HolderId);
            Assert.AreEqual("Ada Farmer", receipt.HolderName);
            Assert.AreEqual("no1", receipt.ElevatorGradeId);
            Assert.AreEqual(1, elevator.GrainStock.Lots.Count);
            var stored = elevator.GrainStock.Lots[0];
            Assert.AreEqual(receipt.ElevatorLotId, stored.ElevatorLotId, "the receipt names the real stored lot");
            Assert.AreEqual("farm-7", stored.FarmId, "provenance is copied, not re-authored");
            Assert.AreEqual(GrainElevatorLotKind.Custody, stored.Kind);
        }

        [Test]
        public void CustodyIntake_RefusesAnonymousCustomer()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var receipt = elevator.ReceiveForStorage(
                GrainLot(registry, CropKind.Wheat, 100), "", "", "no2", 210, diag);

            Assert.IsNull(receipt, "bailment needs a named owner — anonymous customers are refused loudly");
            Assert.AreEqual(0, elevator.GrainStock.Lots.Count);
        }

        [Test]
        public void Intake_RefusesAnonymousGrain()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var receipt = elevator.ReceiveForStorage(
                GrainLot(registry, CropKind.Wheat, 100, farmId: ""), "holder-1", "Ada Farmer",
                "no2", 210, diag);
            Assert.IsNull(receipt, "grain with no named farm is refused");

            string refusal = elevator.BuyForDealing(
                GrainLot(registry, CropKind.Wheat, 100, farmId: ""), "dealer-9", 40, "no2", 210, diag);
            Assert.IsNotNull(refusal, "anonymous grain refused on the dealer book too: " + string.Join(" | ", diag));
        }

        [Test]
        public void Intake_RefusesNonGrainLot()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var sheaves = GrainLot(registry, CropKind.Wheat, 100);
            sheaves.ProductKind = "sheaves";

            Assert.IsNull(elevator.ReceiveForStorage(sheaves, "holder-1", "Ada Farmer", "no2", 210, diag),
                "unthreshed sheaves are not storable grain");
            Assert.AreEqual(0, elevator.GrainStock.Lots.Count);
        }

        [Test]
        public void Intake_RefusesInvalidLotId()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var lot = GrainLot(registry, CropKind.Wheat, 100);
            lot.LotId = EntityId.Invalid;

            Assert.IsNull(elevator.ReceiveForStorage(lot, "holder-1", "Ada Farmer", "no2", 210, diag),
                "HF-1: a grain lot needs a real EntityId");
        }

        [Test]
        public void Intake_AssignsHonestDefaultGrade()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var receipt = elevator.ReceiveForStorage(
                GrainLot(registry, CropKind.Oats, 50), "holder-1", "Ada Farmer",
                null, 210, diag);

            Assert.IsNotNull(receipt, "undeclared grain is accepted: " + string.Join(" | ", diag));
            Assert.AreEqual(GrainElevatorGradeCatalog.DefaultGradeId, receipt.ElevatorGradeId);
            Assert.AreEqual("no2", receipt.ElevatorGradeId, "the honest default is the standard contract grade");
        }

        [Test]
        public void Intake_RefusesUnknownGradeId()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var receipt = elevator.ReceiveForStorage(
                GrainLot(registry, CropKind.Wheat, 50), "holder-1", "Ada Farmer",
                "prime-extra", 210, diag);

            Assert.IsNull(receipt, "grades are data — unknown ids are refused, not invented");
            Assert.AreEqual(0, elevator.GrainStock.Lots.Count);
        }

        [Test]
        public void ReGrade_UpdatesStoredLot()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var receipt = elevator.ReceiveForStorage(
                GrainLot(registry, CropKind.Wheat, 50), "holder-1", "Ada Farmer",
                "no3", 210, diag);
            Assert.IsNotNull(receipt);

            Assert.IsNull(elevator.ReGradeLot(receipt.ElevatorLotId, "no2", diag),
                "re-grade should succeed: " + string.Join(" | ", diag));
            Assert.AreEqual("no2", elevator.GrainStock.Lots[0].ElevatorGradeId);

            Assert.IsNotNull(elevator.ReGradeLot(receipt.ElevatorLotId, "bogus", diag),
                "unknown grade ids are refused on re-grade too");
            Assert.AreEqual("no2", elevator.GrainStock.Lots[0].ElevatorGradeId, "failed re-grade changes nothing");
        }

        [Test]
        public void Capacity_RefusesOverflow()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            elevator.SetStoragePolicy(new GrainElevatorStoragePolicy(capacityUnits: 100, custodyFeeCentsPerUnitPer30Days: 2));
            var diag = new List<string>();

            Assert.IsNotNull(elevator.ReceiveForStorage(
                GrainLot(registry, CropKind.Wheat, 80), "holder-1", "Ada Farmer", "no2", 210, diag));
            var refused = elevator.ReceiveForStorage(
                GrainLot(registry, CropKind.Wheat, 50), "holder-2", "Bo Farmer", "no2", 211, diag);
            Assert.IsNull(refused, "capacity is physical and real (Canon §7.4L)");
            Assert.AreEqual(80, elevator.GrainStock.StoredUnits);

            // Capacity is shared across both books.
            Assert.IsNotNull(elevator.BuyForDealing(
                GrainLot(registry, CropKind.Wheat, 50), "dealer-9", 40, "no2", 211, diag),
                "50u on top of 80u stored exceeds the 100u shared capacity — refused");
        }

        [Test]
        public void DoubleIntake_Refused()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var lot = GrainLot(registry, CropKind.Wheat, 100);
            Assert.IsNotNull(elevator.ReceiveForStorage(lot, "holder-1", "Ada Farmer", "no2", 210, diag));

            Assert.IsNull(elevator.ReceiveForStorage(lot, "holder-2", "Bo Farmer", "no2", 211, diag),
                "the same lot id can never enter twice — a lot is in exactly one place");
        }

        [Test]
        public void Redemption_ReleasesGrainAndAccruesFee()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            elevator.SetStoragePolicy(new GrainElevatorStoragePolicy(5000, custodyFeeCentsPerUnitPer30Days: 2));
            var diag = new List<string>();

            var receipt = elevator.ReceiveForStorage(
                GrainLot(registry, CropKind.Wheat, 100), "holder-1", "Ada Farmer", "no2", 200, diag);
            Assert.IsNotNull(receipt);

            // 60 days of custody = 2 periods x 2c x 100u = 400c.
            var redemption = elevator.RedeemReceipt(receipt.ReceiptId, "holder-1", 100, 260, diag);
            Assert.IsNotNull(redemption, "redemption should succeed: " + string.Join(" | ", diag));
            Assert.AreEqual(400, redemption.FeeCentsOwed, "storage fee accrues to the named storer");
            Assert.AreEqual(100, redemption.ReleasedLot.QuantityUnits);
            Assert.AreEqual("farm-7", redemption.ReleasedLot.FarmId, "released grain keeps its farm provenance");
            Assert.AreEqual("grain", redemption.ReleasedLot.ProductKind);
            Assert.IsTrue(elevator.Obligations.FindReceipt(receipt.ReceiptId).Closed, "fully redeemed receipts close");
            Assert.AreEqual(0, elevator.GrainStock.StoredUnits);
        }

        [Test]
        public void Redemption_PartialSplit_KeepsReceiptOpen()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var receipt = elevator.ReceiveForStorage(
                GrainLot(registry, CropKind.Wheat, 100), "holder-1", "Ada Farmer", "no2", 200, diag);
            Assert.IsNotNull(receipt);

            var redemption = elevator.RedeemReceipt(receipt.ReceiptId, "holder-1", 40, 230, diag);
            Assert.IsNotNull(redemption);
            Assert.AreEqual(40, redemption.ReleasedLot.QuantityUnits);
            Assert.IsFalse(elevator.Obligations.FindReceipt(receipt.ReceiptId).Closed, "partial redemption leaves the receipt open");
            Assert.AreEqual(60, elevator.Obligations.FindReceipt(receipt.ReceiptId).UnitsRemaining);
            Assert.AreEqual(60, elevator.GrainStock.StoredUnits, "the remainder stays honestly in custody");
        }

        [Test]
        public void Redemption_WrongHolder_Refused()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var receipt = elevator.ReceiveForStorage(
                GrainLot(registry, CropKind.Wheat, 100), "holder-1", "Ada Farmer", "no2", 200, diag);
            Assert.IsNotNull(receipt);

            Assert.IsNull(elevator.RedeemReceipt(receipt.ReceiptId, "holder-9", 100, 230, diag),
                "only the named holder can claim bailment grain");
            Assert.AreEqual(100, elevator.GrainStock.StoredUnits, "refused redemption moves nothing");
        }

        [Test]
        public void DealerIntake_RequiresNamedSeller()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            string refusal = elevator.BuyForDealing(
                GrainLot(registry, CropKind.Wheat, 100), "", 40, "no2", 210, diag);
            Assert.IsNotNull(refusal, "the elevator buys from named sellers, never thin air");

            Assert.IsNull(elevator.BuyForDealing(
                GrainLot(registry, CropKind.Wheat, 100), "dealer-9", 40, "no2", 210, diag),
                "named-seller intake should succeed: " + string.Join(" | ", diag));
            Assert.AreEqual(100, elevator.GrainStock.UnitsOnBook(GrainElevatorLotKind.Dealer));
            Assert.AreEqual(0, elevator.GrainStock.UnitsOnBook(GrainElevatorLotKind.Custody));
        }

        [Test]
        public void ForwardLot_NeverSellsWhatItDoesNotHold()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            Assert.IsNull(elevator.BuyForDealing(
                GrainLot(registry, CropKind.Wheat, 60), "dealer-9", 40, "no2", 210, diag));

            var forward = elevator.CreateForwardLot(
                CropKind.Wheat, "no2", 100, 55, "mill-1", "Grist Mill", 211, diag);
            Assert.IsNull(forward, "contracting 100u against 60u held is refused — MR-P001");

            forward = elevator.CreateForwardLot(
                CropKind.Wheat, "no2", 60, 55, "mill-1", "Grist Mill", 211, diag);
            Assert.IsNotNull(forward, "contracting exactly what is held succeeds: " + string.Join(" | ", diag));
            Assert.AreEqual(1, forward.PromisedElevatorLotIds.Count, "the forward lot names the real stored lot");
        }

        [Test]
        public void ForwardLot_Reservation_PreventsDoubleSelling()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            Assert.IsNull(elevator.BuyForDealing(
                GrainLot(registry, CropKind.Wheat, 100), "dealer-9", 40, "no2", 210, diag));

            Assert.IsNotNull(elevator.CreateForwardLot(
                CropKind.Wheat, "no2", 70, 55, "mill-1", "Grist Mill", 211, diag));
            Assert.IsNull(elevator.CreateForwardLot(
                CropKind.Wheat, "no2", 40, 55, "mill-2", "Second Mill", 211, diag),
                "70u already promised of 100u — the second contract would double-sell");

            // Cancelling releases the reservation honestly.
            var first = elevator.Obligations.ForwardLots[0];
            Assert.IsNull(elevator.CancelForwardLot(first.ForwardLotId, diag));
            Assert.IsNotNull(elevator.CreateForwardLot(
                CropKind.Wheat, "no2", 40, 55, "mill-2", "Second Mill", 212, diag),
                "cancellation frees the reservation: " + string.Join(" | ", diag));
        }

        [Test]
        public void ForwardLot_Delivery_NamesStoredGrain()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            Assert.IsNull(elevator.BuyForDealing(
                GrainLot(registry, CropKind.Wheat, 100, "farm-7"), "dealer-9", 40, "no1", 210, diag));

            var forward = elevator.CreateForwardLot(
                CropKind.Wheat, "no1", 100, 55, "mill-1", "Grist Mill", 211, diag);
            Assert.IsNotNull(forward);

            var delivery = elevator.DeliverForwardLot(forward.ForwardLotId, 100, 212, diag);
            Assert.IsNotNull(delivery, "delivery should succeed: " + string.Join(" | ", diag));
            Assert.AreEqual(100, delivery.BuyerLot.QuantityUnits);
            Assert.AreEqual("farm-7", delivery.BuyerLot.FarmId, "the buyer's lot keeps farm provenance");
            Assert.AreEqual("grain", delivery.BuyerLot.ProductKind);
            Assert.IsTrue(delivery.BuyerLot.LotId.IsValid, "the buyer lot takes a real HF-1 id");
            Assert.AreEqual(5500, delivery.SaleValueCents, "100u x 55c contract price");
            Assert.AreEqual(1, delivery.DispenseLines.Count);
            Assert.AreEqual(GrainElevatorForwardLotStatus.Fulfilled, forward.Status);
            Assert.AreEqual(0, elevator.GrainStock.StoredUnits);
        }

        [Test]
        public void ForwardLot_NeverTouchesCustodyGrain()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            // Only custody grain on hand — 500u of it.
            Assert.IsNotNull(elevator.ReceiveForStorage(
                GrainLot(registry, CropKind.Wheat, 500), "holder-1", "Ada Farmer", "no2", 210, diag));

            Assert.IsNull(elevator.CreateForwardLot(
                CropKind.Wheat, "no2", 50, 55, "mill-1", "Grist Mill", 211, diag),
                "canon bailment lock: custody grain is never for sale");
            Assert.AreEqual(500, elevator.GrainStock.StoredUnits, "the custody lot is untouched");
        }

        [Test]
        public void DealerRouteThrough_KeepsFarmProvenance()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            // Farm -> dealer -> elevator: the CRP-3 dealer aggregates, then
            // routes the real lot through the elevator's custody book.
            var dealer = new GrainDealer("dealer-9", workingCapitalCents: 100000);
            var farmerLedger = new HouseholdLedger(7);
            Assert.IsNull(dealer.BuyGrain(GrainLot(registry, CropKind.Corn, 80, "farm-3"), 40, 205, farmerLedger, diag));

            var dealerLedger = new HouseholdLedger(9);
            CropLot routed = dealer.SellGrain(80, 50, 208, dealerLedger, diag, registry);
            Assert.IsNotNull(routed);
            Assert.AreEqual("farm-3", routed.FarmId, "the dealer preserves the farm id on resale");

            var receipt = elevator.ReceiveForStorage(routed, "holder-2", "Co-op Society", "no2", 210, diag);
            Assert.IsNotNull(receipt, "dealer-routed grain enters custody: " + string.Join(" | ", diag));
            Assert.AreEqual("farm-3", elevator.GrainStock.Lots[0].FarmId,
                "farm -> dealer -> elevator: the full chain survives on the stored lot");
        }

        [Test]
        public void SaveLoad_RoundTripsEverything()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            elevator.SetStoragePolicy(new GrainElevatorStoragePolicy(7777, 3));
            var diag = new List<string>();

            var receipt = elevator.ReceiveForStorage(
                GrainLot(registry, CropKind.Wheat, 100), "holder-1", "Ada Farmer", "no1", 200, diag);
            Assert.IsNotNull(receipt);
            Assert.IsNull(elevator.BuyForDealing(
                GrainLot(registry, CropKind.Oats, 60), "dealer-9", 40, "no2", 205, diag));
            var forward = elevator.CreateForwardLot(
                CropKind.Oats, "no2", 40, 50, "mill-1", "Grist Mill", 206, diag);
            Assert.IsNotNull(forward);

            var dto = elevator.CaptureSaveDto();
            var restored = NewElevator(registry);
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(7777, restored.GrainStock.Policy.CapacityUnits);
            Assert.AreEqual(3, restored.GrainStock.Policy.CustodyFeeCentsPerUnitPer30Days);
            Assert.AreEqual(160, restored.GrainStock.StoredUnits);
            Assert.AreEqual(100, restored.GrainStock.UnitsOnBook(GrainElevatorLotKind.Custody));
            Assert.AreEqual(60, restored.GrainStock.UnitsOnBook(GrainElevatorLotKind.Dealer));
            Assert.AreEqual("no1", restored.GrainStock.Lots[0].ElevatorGradeId);
            Assert.AreEqual("Ada Farmer", restored.Obligations.Receipts[0].HolderName);
            Assert.AreEqual(40, restored.Obligations.ForwardLots[0].UnitsContracted);
            Assert.AreEqual("mill-1", restored.Obligations.ForwardLots[0].BuyerId);
            Assert.AreEqual(1, restored.Obligations.ForwardLots[0].PromisedElevatorLotIds.Count,
                "the named stored dealer lot survives the round trip");
        }
    }
}
