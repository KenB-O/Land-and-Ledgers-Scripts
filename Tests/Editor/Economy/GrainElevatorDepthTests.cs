using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GrainElevator;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D2F: the elevator depth fill — storage-fee billing as a mechanism
    /// (billing periods, per-customer fee ledger, handling charges as
    /// policy data, the unpaid-fee withhold fork) and grade disputes as
    /// recorded data (file -> respond -> settle/withdraw/escalate, the
    /// runtime never auto-decides). All rates stay policy data (Canon R6
    /// §6 calibration hold); negotiability stays holder-only.
    /// </summary>
    [TestFixture]
    public sealed class GrainElevatorDepthTests
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

        private static GrainElevatorReceipt Receive(
            GrainElevatorShopRuntime elevator, EntityIdRegistry registry,
            int units, string holderId, string holderName, string gradeId, int day, List<string> diag)
        {
            return elevator.ReceiveForStorage(
                GrainLot(registry, CropKind.Wheat, units), holderId, holderName, gradeId, day, diag);
        }

        [Test]
        public void StorageFees_BillPeriods_PostsChargesAndTracksBalance()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var receipt = Receive(elevator, registry, 100, "holder-1", "Ada Farmer", "no2", 200, diag);
            Assert.IsNotNull(receipt, "intake should succeed: " + string.Join(" | ", diag));

            // One 30-day period closes at day 230: 100u x 2c x 1 period.
            int posted = elevator.BillStoragePeriods(230, diag);
            Assert.AreEqual(1, posted);
            Assert.AreEqual(1, elevator.FeeLedger.Charges.Count);
            var charge = elevator.FeeLedger.Charges[0];
            Assert.AreEqual(GrainElevatorFeeChargeKind.PeriodAccrual, charge.Kind);
            Assert.AreEqual(200, charge.AmountCents);
            Assert.AreEqual("holder-1", charge.CustomerId);
            Assert.AreEqual(200, elevator.StorageUnpaidBalance("holder-1"));
            Assert.IsTrue(elevator.FeeLedger.HasUnpaidFees("holder-1"));

            var payment = elevator.RecordStoragePayment("holder-1", "Ada Farmer", 150, 235, "partial", diag);
            Assert.IsNotNull(payment, "payment should record: " + string.Join(" | ", diag));
            Assert.AreEqual(50, elevator.StorageUnpaidBalance("holder-1"), "charges minus payments");
            Assert.AreEqual(230, elevator.GrainStock.Lots[0].LastBilledDayIndex, "the watermark advances on billing");
        }

        [Test]
        public void StorageFees_RedemptionFeeModel_Preserved()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var receipt = Receive(elevator, registry, 100, "holder-1", "Ada Farmer", "no2", 200, diag);
            Assert.IsNotNull(receipt);

            // No periodic billing ran: the release settles 2 periods exactly
            // as the pre-D2F model did (100u x 2c x 2 periods = 400c).
            var redemption = elevator.RedeemReceipt(receipt.ReceiptId, "holder-1", 100, 260, diag);
            Assert.IsNotNull(redemption, "redemption should succeed: " + string.Join(" | ", diag));
            Assert.AreEqual(400, redemption.FeeCentsOwed);
            Assert.AreEqual(1, elevator.FeeLedger.Charges.Count, "the release-time charge posts to the ledger");
            Assert.AreEqual(400, elevator.FeeLedger.Charges[0].AmountCents);
            Assert.AreEqual(400, elevator.StorageUnpaidBalance("holder-1"));
        }

        [Test]
        public void StorageFees_BillingPlusRedemption_NoDoubleCharge()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var receipt = Receive(elevator, registry, 100, "holder-1", "Ada Farmer", "no2", 200, diag);
            Assert.IsNotNull(receipt);

            Assert.AreEqual(1, elevator.BillStoragePeriods(230, diag), "one period billed through day 230");
            Assert.AreEqual(200, elevator.StorageUnpaidBalance("holder-1"));

            // The release at day 260 settles only the unbilled second period.
            var redemption = elevator.RedeemReceipt(receipt.ReceiptId, "holder-1", 100, 260, diag);
            Assert.IsNotNull(redemption);
            Assert.AreEqual(200, redemption.FeeCentsOwed, "only the unbilled period is charged at release");
            Assert.AreEqual(2, elevator.FeeLedger.Charges.Count);
            Assert.AreEqual(400, elevator.StorageUnpaidBalance("holder-1"),
                "200c billed + 200c at release = the honest 400c, never double-counted");
        }

        [Test]
        public void StorageFees_HandlingCharges_AsPolicyData()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            elevator.SetStoragePolicy(new GrainElevatorStoragePolicy(
                5000, 2, intakeHandlingCentsPerUnit: 5, outturnHandlingCentsPerUnit: 3));
            var diag = new List<string>();

            var receipt = Receive(elevator, registry, 100, "holder-1", "Ada Farmer", "no2", 200, diag);
            Assert.IsNotNull(receipt);
            Assert.AreEqual(500, elevator.StorageUnpaidBalance("holder-1"),
                "100u x 5c receiving charge posts at intake");

            // 10 days of storage = the minimum one period: 100u x 2c x 1 = 200c storage + 300c outturn.
            var redemption = elevator.RedeemReceipt(receipt.ReceiptId, "holder-1", 100, 210, diag);
            Assert.IsNotNull(redemption);
            Assert.AreEqual(500, redemption.FeeCentsOwed, "200c storage + 300c outturn handling");
            Assert.AreEqual(1000, elevator.StorageUnpaidBalance("holder-1"));
            Assert.AreEqual(3, elevator.FeeLedger.Charges.Count, "intake handling, period accrual, outturn handling");
        }

        [Test]
        public void StorageFees_WithholdRelease_ForkFlag()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            // Default policy: the fork flag is off — unpaid fees never block
            // release on their own.
            var receipt = Receive(elevator, registry, 100, "holder-1", "Ada Farmer", "no2", 200, diag);
            Assert.IsNotNull(receipt);
            elevator.BillStoragePeriods(260, diag);
            Assert.IsTrue(elevator.FeeLedger.HasUnpaidFees("holder-1"));
            Assert.IsNotNull(elevator.RedeemReceipt(receipt.ReceiptId, "holder-1", 100, 260, diag),
                "unpaid fees do not block release under the default policy: " + string.Join(" | ", diag));

            // Operator-set fork: withholding release for unpaid fees.
            var elevator2 = new GrainElevatorShopRuntime("elevator-2", registry);
            elevator2.SetStoragePolicy(new GrainElevatorStoragePolicy(
                5000, 2, withholdReleaseForUnpaidFees: true));
            var receipt2 = Receive(elevator2, registry, 100, "holder-9", "Bo Farmer", "no2", 300, diag);
            Assert.IsNotNull(receipt2);
            elevator2.BillStoragePeriods(330, diag);
            Assert.AreEqual(200, elevator2.StorageUnpaidBalance("holder-9"));

            Assert.IsNull(elevator2.RedeemReceipt(receipt2.ReceiptId, "holder-9", 100, 330, diag),
                "the fork flag withholds release while fees are unpaid");
            Assert.AreEqual(100, elevator2.GrainStock.StoredUnits, "withheld release moves nothing");

            Assert.IsNotNull(elevator2.RecordStoragePayment("holder-9", "Bo Farmer", 200, 331, "settled", diag));
            Assert.IsNotNull(elevator2.RedeemReceipt(receipt2.ReceiptId, "holder-9", 100, 331, diag),
                "once the account is settled, release proceeds: " + string.Join(" | ", diag));
        }

        [Test]
        public void StorageFees_PartialRedemption_NoLeak()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var receipt = Receive(elevator, registry, 100, "holder-1", "Ada Farmer", "no2", 200, diag);
            Assert.IsNotNull(receipt);

            var first = elevator.RedeemReceipt(receipt.ReceiptId, "holder-1", 40, 230, diag);
            Assert.IsNotNull(first);
            Assert.AreEqual(80, first.FeeCentsOwed, "40u x 2c x 1 period");

            var second = elevator.RedeemReceipt(receipt.ReceiptId, "holder-1", 60, 260, diag);
            Assert.IsNotNull(second);
            Assert.AreEqual(240, second.FeeCentsOwed,
                "the remaining 60u owe both periods from the received day — no free storage window");
            Assert.AreEqual(320, elevator.StorageUnpaidBalance("holder-1"));
        }

        [Test]
        public void GradeDispute_FileRespondSettle_AppliesGrade()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var receipt = Receive(elevator, registry, 100, "holder-1", "Ada Farmer", "no2", 200, diag);
            Assert.IsNotNull(receipt);

            var dispute = elevator.FileGradeDispute(
                receipt.ElevatorLotId, receipt.ReceiptId,
                "holder-1", "Ada Farmer", "no1", 0, "clean sample, no smut", 205, diag);
            Assert.IsNotNull(dispute, "filing should succeed: " + string.Join(" | ", diag));
            Assert.AreEqual(GrainElevatorGradeDisputeStatus.Filed, dispute.Status);
            Assert.AreEqual("no2", dispute.AssignedGradeId);
            Assert.AreEqual("no1", dispute.ClaimedGradeId);
            Assert.IsTrue(dispute.DisputeId.IsValid);

            Assert.IsNull(elevator.RespondToGradeDispute(dispute.DisputeId, "no2", "stands by inspection", 207, diag),
                "response should record: " + string.Join(" | ", diag));
            Assert.AreEqual(GrainElevatorGradeDisputeStatus.Responded,
                elevator.DisputeBook.FindDispute(dispute.DisputeId).Status);
            Assert.AreEqual("no2", elevator.GrainStock.Lots[0].ElevatorGradeId,
                "a response is a counter-position — the stored grade is unchanged");

            Assert.IsNull(elevator.SettleGradeDispute(dispute.DisputeId, "no1", "mutual agreement", 210, diag),
                "settlement should apply: " + string.Join(" | ", diag));
            var settled = elevator.DisputeBook.FindDispute(dispute.DisputeId);
            Assert.AreEqual(GrainElevatorGradeDisputeStatus.Settled, settled.Status);
            Assert.AreEqual("mutual agreement", settled.DecidedBy, "who decided is recorded as data");
            Assert.AreEqual("no1", elevator.GrainStock.Lots[0].ElevatorGradeId,
                "the settled grade is applied through the re-grade authority");
        }

        [Test]
        public void GradeDispute_Filing_Validates()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var receipt = Receive(elevator, registry, 100, "holder-1", "Ada Farmer", "no2", 200, diag);
            Assert.IsNotNull(receipt);

            Assert.IsNull(elevator.FileGradeDispute(
                receipt.ElevatorLotId, receipt.ReceiptId, "holder-1", "Ada Farmer",
                "prime-extra", 0, "", 205, diag),
                "unknown claimed grades are refused — grades are data");

            Assert.IsNull(elevator.FileGradeDispute(
                receipt.ElevatorLotId, receipt.ReceiptId, "holder-9", "Bo Farmer",
                "no1", 0, "", 205, diag),
                "only the named storer can dispute the grade");

            Assert.IsNull(elevator.FileGradeDispute(
                receipt.ElevatorLotId, receipt.ReceiptId, "holder-1", "Ada Farmer",
                "no2", 0, "", 205, diag),
                "claiming the grade already assigned is no dispute");

            Assert.IsNull(elevator.FileGradeDispute(
                receipt.ElevatorLotId, receipt.ReceiptId, "holder-1", "Ada Farmer",
                "no1", -5, "", 205, diag),
                "negative dockage claims are refused");

            Assert.IsNotNull(elevator.FileGradeDispute(
                receipt.ElevatorLotId, receipt.ReceiptId, "holder-1", "Ada Farmer",
                "no1", 0, "", 205, diag));
            Assert.IsNull(elevator.FileGradeDispute(
                receipt.ElevatorLotId, receipt.ReceiptId, "holder-1", "Ada Farmer",
                "no3", 0, "", 206, diag),
                "one open dispute per lot");
        }

        [Test]
        public void GradeDispute_Escalation_RecordedNotResolved()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var receipt = Receive(elevator, registry, 100, "holder-1", "Ada Farmer", "no3", 200, diag);
            Assert.IsNotNull(receipt);

            var dispute = elevator.FileGradeDispute(
                receipt.ElevatorLotId, receipt.ReceiptId, "holder-1", "Ada Farmer",
                "no2", 0, "borderline sample", 205, diag);
            Assert.IsNotNull(dispute);

            Assert.IsNull(elevator.EscalateGradeDispute(dispute.DisputeId, "referred to the county inspector", 208, diag),
                "escalation should record: " + string.Join(" | ", diag));
            Assert.AreEqual(GrainElevatorGradeDisputeStatus.Escalated,
                elevator.DisputeBook.FindDispute(dispute.DisputeId).Status);
            Assert.AreEqual("no3", elevator.GrainStock.Lots[0].ElevatorGradeId,
                "the runtime does not resolve formal proceedings — the grade stands until data arrives");

            // The outcome arrives later as data.
            Assert.IsNull(elevator.SettleGradeDispute(dispute.DisputeId, "no2", "board inspector", 230, diag),
                "an escalated dispute settles when the outcome arrives as data: " + string.Join(" | ", diag));
            Assert.AreEqual("no2", elevator.GrainStock.Lots[0].ElevatorGradeId);
            Assert.AreEqual("board inspector",
                elevator.DisputeBook.FindDispute(dispute.DisputeId).DecidedBy);
        }

        [Test]
        public void GradeDispute_Dockage_RecordedNotApplied()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var receipt = Receive(elevator, registry, 100, "holder-1", "Ada Farmer", "no2", 200, diag);
            Assert.IsNotNull(receipt);

            var dispute = elevator.FileGradeDispute(
                receipt.ElevatorLotId, receipt.ReceiptId, "holder-1", "Ada Farmer",
                "feed", 10, "damp sample", 205, diag);
            Assert.IsNotNull(dispute, "filing should succeed: " + string.Join(" | ", diag));
            Assert.AreEqual(10, dispute.ClaimedDockageUnits, "claimed dockage is recorded");
            Assert.AreEqual(100, elevator.GrainStock.Lots[0].GrainUnits,
                "claimed dockage never auto-applies — it is a Canon R6 §6 calibration hold");

            Assert.IsNull(elevator.SettleGradeDispute(dispute.DisputeId, "feed", "mutual agreement", 210, diag));
            Assert.AreEqual(100, elevator.GrainStock.Lots[0].GrainUnits,
                "settlement changes the grade, not the units");
            Assert.AreEqual("feed", elevator.GrainStock.Lots[0].ElevatorGradeId);
        }

        [Test]
        public void SaveLoad_RoundTripsFeesAndDisputes()
        {
            var registry = new EntityIdRegistry();
            var elevator = NewElevator(registry);
            var diag = new List<string>();

            var receipt = Receive(elevator, registry, 100, "holder-1", "Ada Farmer", "no2", 200, diag);
            Assert.IsNotNull(receipt);
            elevator.BillStoragePeriods(230, diag);
            Assert.IsNotNull(elevator.RecordStoragePayment("holder-1", "Ada Farmer", 50, 235, "partial", diag));
            var dispute = elevator.FileGradeDispute(
                receipt.ElevatorLotId, receipt.ReceiptId, "holder-1", "Ada Farmer",
                "no1", 0, "", 240, diag);
            Assert.IsNotNull(dispute);

            var dto = elevator.CaptureSaveDto();
            var restored = NewElevator(registry);
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(100, restored.GrainStock.StoredUnits);
            Assert.AreEqual(230, restored.GrainStock.Lots[0].LastBilledDayIndex, "the billing watermark survives");
            Assert.AreEqual(1, restored.FeeLedger.Charges.Count);
            Assert.AreEqual(200, restored.FeeLedger.Charges[0].AmountCents);
            Assert.AreEqual(1, restored.FeeLedger.Payments.Count);
            Assert.AreEqual(50, restored.FeeLedger.Payments[0].AmountCents);
            Assert.AreEqual(150, restored.StorageUnpaidBalance("holder-1"));
            Assert.AreEqual(1, restored.DisputeBook.Disputes.Count);
            var restoredDispute = restored.DisputeBook.Disputes[0];
            Assert.AreEqual(GrainElevatorGradeDisputeStatus.Filed, restoredDispute.Status);
            Assert.AreEqual("no1", restoredDispute.ClaimedGradeId);
            Assert.AreEqual("holder-1", restoredDispute.FiledByCustomerId);
        }
    }
}
