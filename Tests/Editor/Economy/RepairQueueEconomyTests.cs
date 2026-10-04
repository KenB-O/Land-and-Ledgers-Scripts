using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Trade;
using LandLedgers.Economy.Wheelwright;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using NUnit.Framework;

namespace LandLedgers.Economy.Businesses.Blacksmith.Tests
{
    /// <summary>
    /// D4K: the repair queue economy — recorded quote offers (accept/decline/
    /// expire, never silent billing), throughput scheduling on smith-hours,
    /// rush + breakdown-vs-maintenance prioritization, lot-traced parts
    /// provenance, pickup/delivery with unclaimed flagging, and the artisan's
    /// lien as a recorded claim (never auto-seizure, never auto-sale).
    /// </summary>
    [TestFixture]
    public sealed class RepairQueueEconomyTests
    {
        private static EntityId Person(int n) => EntityId.For(EntityKind.Person, n);

        private static BlacksmithRuntime NewSmithy()
        {
            var smithy = new BlacksmithRuntime("smith-d4k", "Test Smithy", true);
            var diag = new List<string>();
            smithy.ReceiveMaterial(new ImportLot
            {
                LotId = "IMP-D4K-1", MaterialId = ImportCatalog.IronStockId,
                MaterialName = "Iron stock", Units = 60,
                OriginName = "Pittsburgh ironworks, via railhead", OrderId = "IMP-ORD-D4K-1",
            }, diag);
            smithy.ReceiveMaterial(new ImportLot
            {
                LotId = "IMP-D4K-2", MaterialId = ImportCatalog.IronStockId,
                MaterialName = "Iron stock", Units = 40,
                OriginName = "Pittsburgh ironworks, via railhead", OrderId = "IMP-ORD-D4K-2",
            }, diag);
            smithy.ReceiveMaterial(new ImportLot
            {
                LotId = "IMP-D4K-3", MaterialId = ImportCatalog.ForgeCoalId,
                MaterialName = "Forge coal", Units = 50,
                OriginName = "Off-map coal dealer", OrderId = "IMP-ORD-D4K-3",
            }, diag);
            return smithy;
        }

        private static WheelwrightRuntime NewWright()
        {
            var wright = new WheelwrightRuntime("wright-d4k", "Test Wheelwright", true);
            var diag = new List<string>();
            wright.ReceiveLumberLot(new ImportLot
            {
                LotId = "LBR-D4K-1", MaterialId = ImportCatalog.LumberId,
                MaterialName = "Lumber", Units = 100,
                OriginName = "Off-map timber country", OrderId = "LBR-ORD-1",
            }, diag);
            return wright;
        }

        private static SkillService SkilledSmith(EntityId smith)
        {
            var skills = new SkillService();
            BlacksmithRuntime.RegisterSkills(skills, new List<string>());
            skills.GrantPractice(smith, BlacksmithRuntime.SmithingSkillId, 100000);
            return skills;
        }

        private static RepairWorkOrder OpenDiagnosedOrder(BlacksmithRuntime smithy, int day = 10)
        {
            var diag = new List<string>();
            RepairWorkOrder order = smithy.Repairs.Intake(
                "Test Customer", "biz-cust-1", "EQ-1", "Moldboard plow",
                "share cracked", "smithing", "loc-1", RepairUrgency.Routine, day, diag);
            smithy.Repairs.Diagnose(order.WorkOrderId, "cracked plowshare",
                4, ImportCatalog.IronStockId, 120, null);
            return order;
        }

        // ---------- quotes: the recorded offer ----------

        [Test]
        public void QuoteIssued_FromDiagnosis_PartsPlusLabor()
        {
            var smithy = NewSmithy();
            RepairWorkOrder order = OpenDiagnosedOrder(smithy);
            var diag = new List<string>();
            List<RepairQuotePartLine> lines = RepairPartsProvenance.SmithIronStockLines(
                smithy, order.MaterialUnitsNeeded, 45, diag, out string refusal);
            Assert.IsNull(refusal, "parts refusal: " + refusal);
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("IMP-D4K-1", lines[0].LotId, "FIFO: first lot first");

            RepairQuoteOffer offer = smithy.RepairQuotes.IssueOffer(
                order, smithy.BusinessInstanceId, smithy.BusinessName, lines,
                RepairQuotePricing.DefaultShopRateCentsPerHour,
                RepairQuotePricing.DefaultPartsMarkup01, 10, diag);
            Assert.IsNotNull(offer);
            Assert.AreEqual(RepairQuoteStatus.Offered, offer.Status);
            // 4 units x 45c = 180c parts, +20% = 216c; 120 min = 2h x 25c = 50c labor
            Assert.AreEqual(216, offer.PartsSubtotalCents);
            Assert.AreEqual(50, offer.LaborSubtotalCents);
            Assert.AreEqual(266, offer.TotalAskCents);
            Assert.AreEqual(10 + RepairQuotePricing.DefaultQuoteValidityDays, offer.ExpiresDayIndex);
        }

        [Test]
        public void AcceptQuote_MarksOrderQuoted_WithAskPrice()
        {
            var smithy = NewSmithy();
            RepairWorkOrder order = OpenDiagnosedOrder(smithy);
            var diag = new List<string>();
            List<RepairQuotePartLine> lines = RepairPartsProvenance.SmithIronStockLines(
                smithy, order.MaterialUnitsNeeded, 45, diag, out _);
            RepairQuoteOffer offer = smithy.RepairQuotes.IssueOffer(
                order, smithy.BusinessInstanceId, smithy.BusinessName, lines,
                RepairQuotePricing.DefaultShopRateCentsPerHour,
                RepairQuotePricing.DefaultPartsMarkup01, 10, diag);
            string rejection = smithy.RepairQuotes.AcceptOffer(offer.OfferId, smithy.Repairs, 11, diag);
            Assert.IsNull(rejection, rejection);
            Assert.AreEqual(RepairQuoteStatus.Accepted, offer.Status);
            Assert.AreEqual(RepairOrderStatus.Quoted, order.Status);
            Assert.IsTrue(order.TermsAgreed);
            Assert.AreEqual(offer.TotalAskCents, order.AgreedPriceCents);
        }

        [Test]
        public void AcceptQuote_NegotiatedPrice_RespectsPricingFreedom()
        {
            var smithy = NewSmithy();
            RepairWorkOrder order = OpenDiagnosedOrder(smithy);
            var diag = new List<string>();
            List<RepairQuotePartLine> lines = RepairPartsProvenance.SmithIronStockLines(
                smithy, order.MaterialUnitsNeeded, 45, diag, out _);
            RepairQuoteOffer offer = smithy.RepairQuotes.IssueOffer(
                order, smithy.BusinessInstanceId, smithy.BusinessName, lines,
                RepairQuotePricing.DefaultShopRateCentsPerHour,
                RepairQuotePricing.DefaultPartsMarkup01, 10, diag);
            string rejection = smithy.RepairQuotes.AcceptOffer(
                offer.OfferId, smithy.Repairs, 11, diag, negotiatedPriceCents: 200);
            Assert.IsNull(rejection, rejection);
            Assert.AreEqual(200, order.AgreedPriceCents, "canon 7.2D: agreed price is negotiated");
        }

        [Test]
        public void DeclineQuote_MarksOrderDeclined_NoBill()
        {
            var smithy = NewSmithy();
            var diag = new List<string>();
            RepairWorkOrder first = smithy.Repairs.Intake(
                "A", "biz-a", "EQ-1", "Plow", "dull", "smithing", "loc-1",
                RepairUrgency.Routine, 10, diag);
            RepairWorkOrder second = smithy.Repairs.Intake(
                "B", "biz-b", "EQ-2", "Harrow", "bent", "smithing", "loc-1",
                RepairUrgency.Routine, 11, diag);
            smithy.Repairs.Diagnose(first.WorkOrderId, "dull share", 2, ImportCatalog.IronStockId, 60, null);
            List<RepairQuotePartLine> lines = RepairPartsProvenance.SmithIronStockLines(
                smithy, 2, 45, diag, out _);
            RepairQuoteOffer offer = smithy.RepairQuotes.IssueOffer(
                first, smithy.BusinessInstanceId, smithy.BusinessName, lines,
                RepairQuotePricing.DefaultShopRateCentsPerHour,
                RepairQuotePricing.DefaultPartsMarkup01, 12, diag);
            string rejection = smithy.RepairQuotes.DeclineOffer(offer.OfferId, smithy.Repairs, diag);
            Assert.IsNull(rejection, rejection);
            Assert.AreEqual(RepairQuoteStatus.Declined, offer.Status);
            Assert.AreEqual(RepairOrderStatus.Declined, first.Status);
            Assert.IsFalse(first.TermsAgreed);
            Assert.AreEqual(0, first.AgreedPriceCents, "declined = no bill");
            Assert.IsFalse(
                LandLedgers.Economy.Wheelwright.TurnaroundEstimator.IsOpen(first),
                "declined order leaves the active queue");
            RepairShopDayPlan plan = RepairQueueScheduler.BuildDayPlan(smithy.Repairs, 12, 480, 1, diag);
            Assert.IsFalse(plan.StartDayByOrder.ContainsKey(first.WorkOrderId),
                "declined order is not scheduled");
            Assert.IsTrue(plan.StartDayByOrder.ContainsKey(second.WorkOrderId));
        }

        [Test]
        public void ExpiredQuote_CannotAccept_OrderStaysDiagnosed()
        {
            var smithy = NewSmithy();
            RepairWorkOrder order = OpenDiagnosedOrder(smithy);
            var diag = new List<string>();
            List<RepairQuotePartLine> lines = RepairPartsProvenance.SmithIronStockLines(
                smithy, order.MaterialUnitsNeeded, 45, diag, out _);
            RepairQuoteOffer offer = smithy.RepairQuotes.IssueOffer(
                order, smithy.BusinessInstanceId, smithy.BusinessName, lines,
                RepairQuotePricing.DefaultShopRateCentsPerHour,
                RepairQuotePricing.DefaultPartsMarkup01, 10, diag, validityDays: 7);
            var expired = smithy.RepairQuotes.ExpireOffers(18, diag);
            Assert.IsTrue(expired.Contains(offer.OfferId), "expired offer id should be in the expired list");
            Assert.AreEqual(RepairQuoteStatus.Expired, offer.Status);
            string rejection = smithy.RepairQuotes.AcceptOffer(offer.OfferId, smithy.Repairs, 18, diag);
            Assert.IsNotNull(rejection, "expired offer must refuse acceptance");
            Assert.AreEqual(RepairOrderStatus.Diagnosed, order.Status, "order re-quotable, never auto-billed");
        }

        [Test]
        public void NoSilentBilling_CompleteRefusesBeforeAcceptedQuote()
        {
            var smithy = NewSmithy();
            var skills = SkilledSmith(Person(1));
            RepairWorkOrder order = OpenDiagnosedOrder(smithy);
            var diag = new List<string>();
            // Quote issued (Offered) but never accepted — CompleteRepair must refuse.
            List<RepairQuotePartLine> lines = RepairPartsProvenance.SmithIronStockLines(
                smithy, order.MaterialUnitsNeeded, 45, diag, out _);
            smithy.RepairQuotes.IssueOffer(
                order, smithy.BusinessInstanceId, smithy.BusinessName, lines,
                RepairQuotePricing.DefaultShopRateCentsPerHour,
                RepairQuotePricing.DefaultPartsMarkup01, 10, diag);
            var asset = new EquipmentAsset { AssetId = "EQ-1", Kind = "plow", Condition01 = 0.4f };
            string rejection = smithy.CompleteRepair(
                order.WorkOrderId, asset, Person(1), 15, skills, null, diag);
            Assert.IsNotNull(rejection, "work must not complete without an accepted quote");
            Assert.AreNotEqual(RepairOrderStatus.Complete, order.Status);
        }

        // ---------- throughput: smith-hours as the constraint ----------

        [Test]
        public void Scheduler_BooksLaborIntoShopDays()
        {
            var smithy = NewSmithy();
            var diag = new List<string>();
            RepairWorkOrder a = smithy.Repairs.Intake(
                "A", "biz-a", "EQ-1", "Plow", "x", "smithing", "loc-1",
                RepairUrgency.Routine, 10, diag);
            RepairWorkOrder b = smithy.Repairs.Intake(
                "B", "biz-b", "EQ-2", "Harrow", "x", "smithing", "loc-1",
                RepairUrgency.Routine, 10, diag);
            smithy.Repairs.Diagnose(a.WorkOrderId, "x", 0, "", 300, null);
            smithy.Repairs.Diagnose(b.WorkOrderId, "x", 0, "", 300, null);

            RepairShopDayPlan plan = RepairQueueScheduler.BuildDayPlan(smithy.Repairs, 10, 480, 1, diag);
            Assert.AreEqual(10, plan.StartDayByOrder[a.WorkOrderId]);
            // 300 + 300 = 600 min > 480/day: second order spills into day 11.
            Assert.AreEqual(11, plan.EndDayByOrder[b.WorkOrderId]);
            int day10 = 0, day11 = 0;
            foreach (RepairShopDayEntry e in plan.Days[10]) day10 += e.Minutes;
            foreach (RepairShopDayEntry e in plan.Days[11]) day11 += e.Minutes;
            Assert.AreEqual(480, day10);
            Assert.AreEqual(120, day11);
        }

        [Test]
        public void Overbook_RefusedLoudly()
        {
            var smithy = NewSmithy();
            var diag = new List<string>();
            for (int i = 0; i < 40; i++)
            {
                RepairWorkOrder o = smithy.Repairs.Intake(
                    "C" + i, "biz-c", "EQ-" + i, "Plow", "x", "smithing", "loc-1",
                    RepairUrgency.Routine, 10, diag);
                smithy.Repairs.Diagnose(o.WorkOrderId, "x", 0, "", 480, null); // 40 full shop-days
            }
            string refusal = RepairQueueScheduler.CheckOverbooked(smithy.Repairs, 10, 480, 1, 30, diag);
            Assert.IsNotNull(refusal, "40 days of backlog must refuse new intake");
            StringAssert.Contains("REFUSED", refusal);
        }

        // ---------- prioritization ----------

        [Test]
        public void Rush_BumpsUrgencyOneStep_AndFlagsPremium()
        {
            var smithy = NewSmithy();
            var diag = new List<string>();
            RepairWorkOrder order = smithy.Repairs.Intake(
                "A", "biz-a", "EQ-1", "Plow", "x", "smithing", "loc-1",
                RepairUrgency.Routine, 10, diag);
            Assert.AreEqual(1, order.QueuePosition);
            RepairWorkOrder other = smithy.Repairs.Intake(
                "B", "biz-b", "EQ-2", "Harrow", "x", "smithing", "loc-1",
                RepairUrgency.Routine, 9, diag);
            Assert.AreEqual(1, other.QueuePosition, "earlier routine first");

            string rejection = RepairPrioritization.RequestRush(smithy.Repairs, order.WorkOrderId, diag);
            Assert.IsNull(rejection, rejection);
            Assert.IsTrue(order.RushRequested);
            Assert.AreEqual(RepairUrgency.Urgent, order.Urgency);
            Assert.AreEqual(1, order.QueuePosition, "rush jumps the routine queue");

            // Rush premium prices into the quote.
            smithy.Repairs.Diagnose(order.WorkOrderId, "x", 0, "", 120, null);
            RepairQuoteOffer offer = smithy.RepairQuotes.IssueOffer(
                order, smithy.BusinessInstanceId, smithy.BusinessName, null,
                RepairQuotePricing.DefaultShopRateCentsPerHour,
                RepairQuotePricing.DefaultPartsMarkup01, 10, diag);
            Assert.IsTrue(offer.RushQuoted);
            Assert.AreEqual(62, offer.LaborSubtotalCents, "2h x 25c = 50c, rush x1.25 = 62.5 -> 62c (banker's rounding)");

            // Rush is a single flag per order: a second request is a no-op
            // (no double premium, no second bump) with a diagnostic note.
            rejection = RepairPrioritization.RequestRush(smithy.Repairs, order.WorkOrderId, diag);
            Assert.IsNull(rejection);
            Assert.AreEqual(RepairUrgency.Urgent, order.Urgency);
            Assert.IsTrue(order.RushRequested);
        }

        [Test]
        public void ClassifyJob_BreakdownVsMaintenance()
        {
            Assert.AreEqual("breakdown",
                RepairPrioritization.ClassifyJob(new EquipmentAsset { AssetId = "X", Condition01 = 0f }));
            Assert.AreEqual("maintenance",
                RepairPrioritization.ClassifyJob(new EquipmentAsset { AssetId = "X", Condition01 = 0.8f }));
            Assert.AreEqual("unknown", RepairPrioritization.ClassifyJob(null));
            Assert.AreEqual(RepairUrgency.Urgent,
                RepairPrioritization.SuggestIntakeUrgency("breakdown", RepairUrgency.Routine),
                "breakdown defaults to Urgent at intake");
            Assert.AreEqual(RepairUrgency.Routine,
                RepairPrioritization.SuggestIntakeUrgency("maintenance", RepairUrgency.Routine));
        }

        // ---------- parts provenance ----------

        [Test]
        public void SmithParts_TraceToRealLots_Fifo()
        {
            var smithy = NewSmithy();
            var diag = new List<string>();
            List<RepairQuotePartLine> lines = RepairPartsProvenance.SmithNailsLines(
                smithy, 70, 45, diag, out string refusal);
            Assert.IsNull(refusal, refusal);
            Assert.AreEqual(2, lines.Count, "70 units span two lots");
            Assert.AreEqual("iron-stock", lines[0].MaterialId, "nails consume iron-stock (EQP convention)");
            Assert.AreEqual("nails (iron stock)", lines[0].MaterialLabel);
            Assert.AreEqual("IMP-D4K-1", lines[0].LotId);
            Assert.AreEqual(60, lines[0].Units);
            Assert.AreEqual("IMP-D4K-2", lines[1].LotId);
            Assert.AreEqual(10, lines[1].Units);
        }

        [Test]
        public void SmithParts_ShortfallRefusesLoudly()
        {
            var smithy = NewSmithy();
            var diag = new List<string>();
            RepairPartsProvenance.SmithIronStockLines(smithy, 500, 45, diag, out string refusal);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("short", refusal);
        }

        [Test]
        public void SmithCompletion_ConsumesFromRealLots()
        {
            var smithy = NewSmithy();
            var skills = SkilledSmith(Person(1));
            RepairWorkOrder order = OpenDiagnosedOrder(smithy); // 4 units iron-stock
            var diag = new List<string>();
            List<RepairQuotePartLine> lines = RepairPartsProvenance.SmithIronStockLines(
                smithy, order.MaterialUnitsNeeded, 45, diag, out _);
            RepairQuoteOffer offer = smithy.RepairQuotes.IssueOffer(
                order, smithy.BusinessInstanceId, smithy.BusinessName, lines,
                RepairQuotePricing.DefaultShopRateCentsPerHour,
                RepairQuotePricing.DefaultPartsMarkup01, 10, diag);
            Assert.IsNull(smithy.RepairQuotes.AcceptOffer(offer.OfferId, smithy.Repairs, 11, diag));
            var asset = new EquipmentAsset { AssetId = "EQ-1", Kind = "plow", Condition01 = 0.4f };
            string rejection = smithy.CompleteRepair(
                order.WorkOrderId, asset, Person(1), 15, skills, null, diag);
            Assert.IsNull(rejection, rejection);
            Assert.AreEqual(56, smithy.LotUnitsOnHand(ImportCatalog.IronStockId, "IMP-D4K-1"),
                "4 units consumed FIFO from the first lot");
            Assert.AreEqual(40, smithy.LotUnitsOnHand(ImportCatalog.IronStockId, "IMP-D4K-2"));
            Assert.AreEqual(96, smithy.MaterialOnHand(ImportCatalog.IronStockId),
                "unit stock agrees with lot ledger");
        }

        [Test]
        public void WheelwrightLumberLines_UseRealLots_ShortfallRefuses()
        {
            var wright = NewWright();
            var diag = new List<string>();
            List<RepairQuotePartLine> lines = RepairPartsProvenance.WheelwrightLumberLines(
                wright, 30, 25, diag, out string refusal);
            Assert.IsNull(refusal, refusal);
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("LBR-D4K-1", lines[0].LotId);
            Assert.AreEqual(30, lines[0].Units);

            RepairPartsProvenance.WheelwrightLumberLines(wright, 500, 25, diag, out refusal);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("short", refusal);
        }

        [Test]
        public void WheelwrightIronworkLines_CarryMakerProvenance()
        {
            var diag = new List<string>();
            var part = new EquipmentAsset
            {
                AssetId = "IRON-1", Kind = "wagon-part", DisplayName = "Wagon ironwork",
                MadeByBusinessId = "smith-9", MadeByBusinessName = "Ninth Smithy",
            };
            List<RepairQuotePartLine> lines = RepairPartsProvenance.WheelwrightIronworkLines(
                new List<EquipmentAsset> { part }, 1, 200, diag, out string refusal);
            Assert.IsNull(refusal, refusal);
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("IRON-1", lines[0].LotId);
            StringAssert.Contains("Ninth Smithy", lines[0].ProvenanceNote);

            RepairPartsProvenance.WheelwrightIronworkLines(
                new List<EquipmentAsset> { part }, 2, 200, diag, out refusal);
            Assert.IsNotNull(refusal, "short ironwork refuses loudly");
        }

        [Test]
        public void LeatherLines_TraceToTanneryLot()
        {
            var diag = new List<string>();
            var lot = new LeatherLotView("TAN-7-leather-3", 20, "butcher hide lot H-11", "Seventh Tannery");
            List<RepairQuotePartLine> lines = RepairPartsProvenance.LeatherLines(
                lot, 8, 30, diag, out string refusal);
            Assert.IsNull(refusal, refusal);
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("leather", lines[0].MaterialId);
            Assert.AreEqual("TAN-7-leather-3", lines[0].LotId);
            StringAssert.Contains("H-11", lines[0].ProvenanceNote);

            RepairPartsProvenance.LeatherLines(lot, 99, 30, diag, out refusal);
            Assert.IsNotNull(refusal, "short leather refuses loudly");
        }

        // ---------- pickup / delivery ----------

        [Test]
        public void PickupFlow_ReadyToPickedUp()
        {
            var smithy = NewSmithy();
            var skills = SkilledSmith(Person(1));
            RepairWorkOrder order = OpenDiagnosedOrder(smithy);
            var diag = new List<string>();
            List<RepairQuotePartLine> lines = RepairPartsProvenance.SmithIronStockLines(
                smithy, order.MaterialUnitsNeeded, 45, diag, out _);
            RepairQuoteOffer offer = smithy.RepairQuotes.IssueOffer(
                order, smithy.BusinessInstanceId, smithy.BusinessName, lines,
                RepairQuotePricing.DefaultShopRateCentsPerHour,
                RepairQuotePricing.DefaultPartsMarkup01, 10, diag);
            Assert.IsNull(smithy.RepairQuotes.AcceptOffer(offer.OfferId, smithy.Repairs, 11, diag));
            var asset = new EquipmentAsset { AssetId = "EQ-1", Kind = "plow", Condition01 = 0.4f };
            // Auto-announce on the shop's own pickup ledger.
            Assert.IsNull(smithy.CompleteRepair(
                order.WorkOrderId, asset, Person(1), 15, skills, null, diag,
                null, null, smithy.RepairPickups));

            RepairPickupRecord record = smithy.RepairPickups.FindByOrder(order.WorkOrderId);
            Assert.IsNotNull(record);
            Assert.AreEqual(RepairPickupStatus.Ready, record.Status);
            Assert.IsNull(smithy.RepairPickups.RecordPickup(order.WorkOrderId, 16, diag));
            Assert.AreEqual(RepairPickupStatus.PickedUp, record.Status);
            Assert.AreEqual(16, record.PickedUpDayIndex);
        }

        [Test]
        public void Delivery_MovesAssetLocation()
        {
            var smithy = NewSmithy();
            RepairWorkOrder order = OpenDiagnosedOrder(smithy);
            order.Status = RepairOrderStatus.Complete;
            var diag = new List<string>();
            RepairPickupRecord record = smithy.RepairPickups.NotifyReady(order, 15, diag);
            Assert.IsNotNull(record);
            var asset = new EquipmentAsset
            {
                AssetId = "EQ-1", Kind = "plow", LocationId = "smith-d4k",
            };
            Assert.IsNull(smithy.RepairPickups.RecordDelivery(
                order.WorkOrderId, "farm-42", asset, 17, diag));
            Assert.AreEqual(RepairPickupStatus.Delivered, record.Status);
            Assert.AreEqual("farm-42", asset.LocationId, "the asset travels — physical truth");
        }

        [Test]
        public void Unclaimed_FlaggedAfterWindow_NothingSeized()
        {
            var smithy = NewSmithy();
            RepairWorkOrder order = OpenDiagnosedOrder(smithy);
            order.Status = RepairOrderStatus.Complete;
            var diag = new List<string>();
            smithy.RepairPickups.NotifyReady(order, 15, diag);
            var flagged = smithy.RepairPickups.ScanUnclaimed(15 + 89, diag);
            Assert.AreEqual(0, flagged.Count, "89 days: still the owner's");
            flagged = smithy.RepairPickups.ScanUnclaimed(15 + 90, diag);
            Assert.AreEqual(1, flagged.Count);
            RepairPickupRecord record = smithy.RepairPickups.FindByOrder(order.WorkOrderId);
            Assert.AreEqual(RepairPickupStatus.Unclaimed, record.Status);
        }

        // ---------- artisan's lien ----------

        [Test]
        public void Lien_RecordedOnUnpaidCompletion_ReleasedOnPayment()
        {
            var smithy = NewSmithy();
            var skills = SkilledSmith(Person(1));
            RepairWorkOrder order = OpenDiagnosedOrder(smithy);
            var diag = new List<string>();
            List<RepairQuotePartLine> lines = RepairPartsProvenance.SmithIronStockLines(
                smithy, order.MaterialUnitsNeeded, 45, diag, out _);
            RepairQuoteOffer offer = smithy.RepairQuotes.IssueOffer(
                order, smithy.BusinessInstanceId, smithy.BusinessName, lines,
                RepairQuotePricing.DefaultShopRateCentsPerHour,
                RepairQuotePricing.DefaultPartsMarkup01, 10, diag);
            Assert.IsNull(smithy.RepairQuotes.AcceptOffer(offer.OfferId, smithy.Repairs, 11, diag));
            var asset = new EquipmentAsset { AssetId = "EQ-1", Kind = "plow", Condition01 = 0.4f };
            // customerLedger == null: the work completes UNPAID (book credit / tab).
            Assert.IsNull(smithy.CompleteRepair(
                order.WorkOrderId, asset, Person(1), 15, skills, null, diag,
                null, smithy.RepairLiens, null));

            ArtisanLienClaim claim = smithy.RepairLiens.FindOpenByOrder(order.WorkOrderId);
            Assert.IsNotNull(claim, "unpaid completion records the lien");
            Assert.AreEqual(ArtisanLienStatus.Recorded, claim.Status);
            Assert.AreEqual(offer.TotalAskCents, claim.ClaimAmountCents);
            Assert.AreEqual(266, claim.UnpaidBalanceCents);

            // Partial payment, then full: released.
            Assert.IsNull(smithy.RepairLiens.RecordPayment(claim.ClaimId, 100, 16, diag));
            Assert.AreEqual(166, claim.UnpaidBalanceCents);
            Assert.AreEqual(ArtisanLienStatus.Recorded, claim.Status);
            Assert.IsNull(smithy.RepairLiens.RecordPayment(claim.ClaimId, 166, 17, diag));
            Assert.AreEqual(ArtisanLienStatus.Released, claim.Status);
            Assert.IsNull(smithy.RepairLiens.FindOpenByOrder(order.WorkOrderId));
        }

        [Test]
        public void Lien_NoDebt_NoClaim()
        {
            var smithy = NewSmithy();
            RepairWorkOrder order = OpenDiagnosedOrder(smithy);
            order.Status = RepairOrderStatus.Complete;
            order.AgreedPriceCents = 0; // free work — no debt, no lien
            var diag = new List<string>();
            ArtisanLienClaim claim = smithy.RepairLiens.RecordClaim(
                order, smithy.BusinessInstanceId, smithy.BusinessName, 15, diag);
            Assert.IsNull(claim);
        }

        [Test]
        public void Lien_EnforcementLadder_NoticeThenExplicitSale()
        {
            var smithy = NewSmithy();
            RepairWorkOrder order = OpenDiagnosedOrder(smithy);
            order.Status = RepairOrderStatus.Complete;
            order.AgreedPriceCents = 266;
            var diag = new List<string>();
            ArtisanLienClaim claim = smithy.RepairLiens.RecordClaim(
                order, smithy.BusinessInstanceId, smithy.BusinessName, 15, diag);
            Assert.IsNotNull(claim);

            // Too early: notice refused.
            string refusal = smithy.RepairLiens.ServeEnforcementNotice(
                claim.ClaimId, true, 15 + 89, diag);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(ArtisanLienStatus.Recorded, claim.Status);

            // After the window, with the item unclaimed: enforceable.
            Assert.IsNull(smithy.RepairLiens.ServeEnforcementNotice(
                claim.ClaimId, true, 15 + 90, diag));
            Assert.AreEqual(ArtisanLienStatus.Enforceable, claim.Status);

            // Sale before the notice period ran: refused. No auto-sale anywhere.
            refusal = smithy.RepairLiens.RecordLienSale(
                claim.ClaimId, 400, 20, "Buyer X", 15 + 90 + 5, diag,
                out _, out _);
            Assert.IsNotNull(refusal);

            // Explicit sale after notice: proceeds pay claim + costs, surplus to owner.
            refusal = smithy.RepairLiens.RecordLienSale(
                claim.ClaimId, 400, 20, "Buyer X", 15 + 90 + 14, diag,
                out int shopShare, out int surplus);
            Assert.IsNull(refusal, refusal);
            Assert.AreEqual(286, shopShare, "266c claim + 20c costs");
            Assert.AreEqual(114, surplus, "surplus returns to the debtor");
            Assert.AreEqual(ArtisanLienStatus.SatisfiedBySale, claim.Status);
        }

        [Test]
        public void Lien_ScansNeverSellAutomatically()
        {
            var smithy = NewSmithy();
            RepairWorkOrder order = OpenDiagnosedOrder(smithy);
            order.Status = RepairOrderStatus.Complete;
            order.AgreedPriceCents = 266;
            var diag = new List<string>();
            ArtisanLienClaim claim = smithy.RepairLiens.RecordClaim(
                order, smithy.BusinessInstanceId, smithy.BusinessName, 15, diag);
            smithy.RepairPickups.NotifyReady(order, 15, diag);
            // A full year passes with scans running: still Recorded, nothing sold.
            smithy.RepairPickups.ScanUnclaimed(15 + 365, diag);
            Assert.AreEqual(ArtisanLienStatus.Recorded, claim.Status,
                "the register never sells on its own — enforcement is explicit");
        }

        // ---------- wheelwright through the shared authority ----------

        [Test]
        public void Wheelwright_QuoteAndLien_ThroughSharedAuthority()
        {
            var wright = NewWright();
            var diag = new List<string>();
            RepairWorkOrder order = wright.Repairs.Intake(
                "Freight Co", "biz-freight", "WG-1", "Freight wagon",
                "wheel loose", WheelwrightRuntime.WheelwrightCapabilityCode,
                "biz-freight", RepairUrgency.Routine, 10, diag);
            wright.Repairs.Diagnose(order.WorkOrderId, "loose wheel, re-tire",
                6, ImportCatalog.LumberId, 120, "wagon-wheel");
            List<RepairQuotePartLine> lines = RepairPartsProvenance.WheelwrightLumberLines(
                wright, order.MaterialUnitsNeeded, 25, diag, out string refusal);
            Assert.IsNull(refusal, refusal);
            RepairQuoteOffer offer = wright.RepairQuotes.IssueOffer(
                order, wright.BusinessInstanceId, wright.BusinessName, lines,
                RepairQuotePricing.DefaultShopRateCentsPerHour,
                RepairQuotePricing.DefaultPartsMarkup01, 10, diag);
            Assert.IsNotNull(offer);
            // 6 x 25c = 150c parts +20% = 180c; 2h x 25c = 50c labor
            Assert.AreEqual(230, offer.TotalAskCents);
            Assert.IsNull(wright.RepairQuotes.AcceptOffer(offer.OfferId, wright.Repairs, 11, diag));
            Assert.AreEqual(RepairOrderStatus.Quoted, order.Status);
        }

        // ---------- save DTOs round-trip ----------

        [Test]
        public void SaveDtos_RoundTrip()
        {
            var smithy = NewSmithy();
            RepairWorkOrder order = OpenDiagnosedOrder(smithy);
            var diag = new List<string>();
            List<RepairQuotePartLine> lines = RepairPartsProvenance.SmithIronStockLines(
                smithy, order.MaterialUnitsNeeded, 45, diag, out _);
            RepairQuoteOffer offer = smithy.RepairQuotes.IssueOffer(
                order, smithy.BusinessInstanceId, smithy.BusinessName, lines,
                RepairQuotePricing.DefaultShopRateCentsPerHour,
                RepairQuotePricing.DefaultPartsMarkup01, 10, diag);

            var book2 = new RepairQuoteBook();
            book2.LoadFromSaveDto(smithy.RepairQuotes.ToSaveDto());
            Assert.AreEqual(1, book2.Offers.Count);
            Assert.AreEqual(offer.OfferId, book2.Offers[0].OfferId);
            Assert.AreEqual(offer.TotalAskCents, book2.Offers[0].TotalAskCents);
            Assert.AreEqual(1, book2.Offers[0].PartLines.Count);
            Assert.AreEqual("IMP-D4K-1", book2.Offers[0].PartLines[0].LotId);
        }
    }
}
