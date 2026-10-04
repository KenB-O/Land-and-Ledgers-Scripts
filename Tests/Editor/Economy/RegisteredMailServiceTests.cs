using System.Collections.Generic;
using LandLedgers.Economy.Postal;
using LandLedgers.Tasks;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D4F: registered mail with receipt chain — a service layer on the NX-2A
    /// postal movement. Historical: US registered mail began in 1855; in 1870
    /// the registry fee was 5 cents (8c only from Jan 1, 1893). Return receipt
    /// 3c at mailing / 5c after (Act of June 8, 1872, §128). Indemnity
    /// availability in 1870 is a documented fork — parameterized, default off.
    /// </summary>
    [TestFixture]
    public sealed class RegisteredMailServiceTests
    {
        private JourneyModel TwoTowns()
        {
            var model = new JourneyModel();
            model.RegisterLocation(new JourneyLocation("town-a", JourneyLocationKind.TownBuilding, "Town A", 0f, 0f));
            model.RegisterLocation(new JourneyLocation("town-b", JourneyLocationKind.TownBuilding, "Town B", 8f, 0f));
            model.AddEdge("town-a", "town-b", 8f, "stage road");
            return model;
        }

        private PostalService TwoOffices()
        {
            var postal = new PostalService();
            var diag = new List<string>();
            Assert.IsNull(postal.RegisterOffice("po-a", "town-a", "biz-po-a", new List<int> { 0 }, 7, diag)); // Monday
            Assert.IsNull(postal.RegisterOffice("po-b", "town-b", "biz-po-b", new List<int> { 0 }, 9, diag));
            return postal;
        }

        private RegisteredMailService RegistryOn(PostalService postal)
        {
            return new RegisteredMailService(postal);
        }

        [Test]
        public void RegisterItem_ChargesPeriodCorrectFiveCentFee()
        {
            var postal = TwoOffices();
            var registry = RegistryOn(postal);
            var diag = new List<string>();

            MailItem item = postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                "po-a", "po-b", 0, diag);
            Assert.IsNotNull(item);

            RegisteredMailRecord record = registry.RegisterItem(
                item.MailId, 500, false, false, 0, 0, diag);
            Assert.IsNotNull(record);
            // 1870 fee: 5 cents (the 8c fee began Jan 1, 1893).
            Assert.AreEqual(RegisteredMailFeeSchedule.RegistrationFeeCents, record.RegistrationFeeCents);
            Assert.AreEqual(5, record.RegistrationFeeCents);
            Assert.AreEqual("REG-po-a-0001", record.SerialNumber);
            Assert.IsTrue(item.IsRegistered);
            Assert.AreEqual(RegisteredMailStatus.Registered, record.Status);
            // Postage and registration fee are separate revenues.
            Assert.AreEqual(3, postal.UncollectedPostageCents);
            Assert.AreEqual(5, registry.UncollectedFeeCents);
            Assert.AreEqual(5, registry.CollectRegistrationFeeRevenue(diag));
            Assert.AreEqual(0, registry.UncollectedFeeCents, "Fee revenue collects once — no double counting.");
            // Receipt issued to sender at registration.
            Assert.AreEqual(1, record.CustodyLog.Count);
            Assert.AreEqual("registered", record.CustodyLog[0].Action);
            Assert.AreEqual("po-a", record.CustodyLog[0].CustodianOfficeId);
        }

        [Test]
        public void RegisterItem_RejectsMidStreamRegistration()
        {
            var journeys = TwoTowns();
            var postal = TwoOffices();
            var registry = RegistryOn(postal);
            var diag = new List<string>();

            MailItem item = postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                "po-a", "po-b", 0, diag);
            postal.AdvanceDay(journeys, 0, diag); // dispatched — no longer at the postmaster's counter
            Assert.AreEqual(MailStatus.InTransit, item.Status);

            RegisteredMailRecord record = registry.RegisterItem(
                item.MailId, 500, false, false, 0, 1, diag);
            Assert.IsNull(record, "Registration happens at posting, not mid-stream.");
            Assert.IsFalse(item.IsRegistered);
            Assert.AreEqual(0, registry.UncollectedFeeCents);
        }

        [Test]
        public void CustodyLog_AppendsAcrossHandoffs()
        {
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("town-a", JourneyLocationKind.TownBuilding, "A", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("town-mid", JourneyLocationKind.Waypoint, "Mid", 4f, 0f));
            journeys.RegisterLocation(new JourneyLocation("town-b", JourneyLocationKind.TownBuilding, "B", 8f, 0f));
            journeys.AddEdge("town-a", "town-mid", 4f, "stage road");
            journeys.AddEdge("town-mid", "town-b", 4f, "stage road");

            var postal = new PostalService();
            var diag = new List<string>();
            Assert.IsNull(postal.RegisterOffice("po-a", "town-a", "biz-a", new List<int> { 0 }, 7, diag));
            Assert.IsNull(postal.RegisterOffice("po-mid", "town-mid", "biz-mid", new List<int> { 0 }, 11, diag));
            Assert.IsNull(postal.RegisterOffice("po-b", "town-b", "biz-b", new List<int> { 0 }, 9, diag));
            var registry = RegistryOn(postal);

            MailItem item = postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                "po-a", "po-b", 0, diag);
            RegisteredMailRecord record = registry.RegisterItem(
                item.MailId, 500, false, false, 0, 0, diag);
            Assert.IsNotNull(record);

            postal.AdvanceDay(journeys, 0, diag); // dispatch day
            postal.AdvanceDay(journeys, 1, diag);
            postal.AdvanceDay(journeys, 2, diag); // allow a missed connection
            Assert.AreEqual(MailStatus.Arrived, item.Status);

            Assert.AreEqual(4, record.CustodyLog.Count, "registered, dispatched, intermediate handoff, arrived — append-only.");
            Assert.AreEqual("registered", record.CustodyLog[0].Action);
            Assert.AreEqual("po-a", record.CustodyLog[0].CustodianOfficeId);
            Assert.AreEqual("po-mid", record.CustodyLog[2].CustodianOfficeId);
            Assert.AreEqual("arrived", record.CustodyLog[3].Action);
            Assert.AreEqual("po-b", record.CustodyLog[3].CustodianOfficeId);
            Assert.AreEqual("po-b", record.LastKnownCustodianOfficeId);

            int countBefore = record.CustodyLog.Count;
            postal.AdvanceDay(journeys, 3, diag);
            Assert.AreEqual(countBefore, record.CustodyLog.Count, "Nothing more happens after arrival — the log is append-only, never rewritten.");
        }

        [Test]
        public void OrdinaryCollection_RefusesRegisteredItem()
        {
            var journeys = TwoTowns();
            var postal = TwoOffices();
            var registry = RegistryOn(postal);
            var diag = new List<string>();

            MailItem item = postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                "po-a", "po-b", 0, diag);
            Assert.IsNotNull(registry.RegisterItem(item.MailId, 500, false, false, 0, 0, diag));
            postal.AdvanceDay(journeys, 0, diag);
            postal.AdvanceDay(journeys, 1, diag);
            Assert.AreEqual(MailStatus.Arrived, item.Status);

            var collected = postal.CollectMail("po-b", 1, 1, diag);
            Assert.AreEqual(0, collected.Count, "Registered items are not released by ordinary collection.");
            Assert.AreEqual(MailStatus.Arrived, item.Status);
        }

        [Test]
        public void DeliverRegistered_RequiresMatchingSignature()
        {
            var journeys = TwoTowns();
            var postal = TwoOffices();
            var registry = RegistryOn(postal);
            var diag = new List<string>();

            MailItem item = postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                "po-a", "po-b", 0, diag);
            RegisteredMailRecord record = registry.RegisterItem(
                item.MailId, 500, false, false, 0, 0, diag);
            postal.AdvanceDay(journeys, 0, diag);
            postal.AdvanceDay(journeys, 1, diag);

            Assert.IsNotNull(registry.RecordDelivery(item.MailId, 99, "Mallory", 1, diag),
                "Wrong person cannot sign for it.");
            Assert.IsNotNull(registry.RecordDelivery(item.MailId, 1, "   ", 1, diag),
                "No signature, no delivery.");
            Assert.AreEqual(RegisteredMailStatus.Registered, record.Status);

            Assert.IsNull(registry.RecordDelivery(item.MailId, 1, "Emma", 1, diag));
            Assert.AreEqual(RegisteredMailStatus.Delivered, record.Status);
            Assert.AreEqual(MailStatus.Collected, item.Status);
            Assert.AreEqual("Emma", record.DeliverySignatureName);
            Assert.AreEqual(1, record.DeliveredDayIndex);

            Assert.IsNotNull(registry.RecordDelivery(item.MailId, 1, "Emma", 1, diag),
                "No double delivery.");
        }

        [Test]
        public void ReturnReceipt_TravelsAsRealMail()
        {
            var journeys = TwoTowns();
            var postal = TwoOffices();
            var registry = RegistryOn(postal);
            var diag = new List<string>();

            MailItem item = postal.PostItem(MailKind.Letter, "Kennedy", 0, "Emma",
                "po-a", "po-b", 0, diag);
            RegisteredMailRecord record = registry.RegisterItem(
                item.MailId, 500, true, false, 0, 0, diag);
            Assert.IsNotNull(record);
            // 5c registration + 3c return receipt at mailing (1872 Act §128).
            Assert.AreEqual(8, registry.UncollectedFeeCents);

            postal.AdvanceDay(journeys, 0, diag);
            postal.AdvanceDay(journeys, 1, diag);
            Assert.IsNull(registry.RecordDelivery(item.MailId, 0, "Emma", 1, diag));

            Assert.IsNotEmpty(record.ReturnReceiptMailId, "The return receipt is a real mail item, not a notification.");
            MailItem receipt = postal.GetMailItem(record.ReturnReceiptMailId);
            Assert.IsNotNull(receipt);
            Assert.AreEqual(MailKind.Letter, receipt.Kind);
            Assert.AreEqual("po-b", receipt.FromOfficeId);
            Assert.AreEqual("po-a", receipt.ToOfficeId);
            Assert.AreEqual(0, receipt.RecipientPersonId);
            Assert.AreEqual("Kennedy", receipt.RecipientName);
            Assert.AreEqual(MailStatus.Posted, receipt.Status);

            // The receipt rides the real schedule: po-b departs Mondays; day 7 is Monday.
            for (int day = 2; day <= 8; day++)
                postal.AdvanceDay(journeys, day, diag);
            Assert.AreEqual(MailStatus.Arrived, receipt.Status);
            var collected = postal.CollectMail("po-a", 0, 8, diag);
            Assert.AreEqual(1, collected.Count);
            Assert.AreEqual(receipt.MailId, collected[0].MailId);
        }

        [Test]
        public void RequestReturnReceiptAfterMailing_ChargesFiveCents()
        {
            var postal = TwoOffices();
            var registry = RegistryOn(postal);
            var diag = new List<string>();

            MailItem item = postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                "po-a", "po-b", 0, diag);
            RegisteredMailRecord record = registry.RegisterItem(
                item.MailId, 500, false, false, 0, 0, diag);
            Assert.IsNotNull(record);

            Assert.IsNull(registry.RequestReturnReceiptAfterMailing(item.MailId, 1, diag));
            Assert.IsTrue(record.ReturnReceiptRequested);
            Assert.AreEqual(RegisteredMailFeeSchedule.ReturnReceiptAfterMailingCents, record.ReturnReceiptFeeCents);
            Assert.AreEqual(10, registry.UncollectedFeeCents, "5c registration + 5c after-mailing receipt.");

            Assert.IsNotNull(registry.RequestReturnReceiptAfterMailing(item.MailId, 1, diag),
                "Only one return receipt per article.");
        }

        [Test]
        public void Loss_RecordedWithLastKnownCustodian_NeverAutoCompensated()
        {
            var journeys = TwoTowns();
            var postal = TwoOffices();
            var registry = RegistryOn(postal);
            var diag = new List<string>();

            MailItem item = postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                "po-a", "po-b", 0, diag);
            RegisteredMailRecord record = registry.RegisterItem(
                item.MailId, 3000, false, false, 0, 0, diag);
            postal.AdvanceDay(journeys, 0, diag); // dispatched from po-a

            Assert.IsNull(registry.RecordLoss(item.MailId, "pouch destroyed in stage wreck", 1, diag));
            Assert.AreEqual(RegisteredMailStatus.Lost, record.Status);
            Assert.AreEqual(MailStatus.Lost, item.Status);
            Assert.AreEqual("po-a", record.LastKnownCustodianOfficeId);
            Assert.AreEqual(1, record.LossRecordedDayIndex);
            Assert.IsNotEmpty(record.LossReason);

            // Never auto-compensated: indemnity is unavailable by default (the 1870 fork).
            Assert.IsFalse(registry.IndemnityAvailable);
            Assert.AreEqual(0, registry.ComputeIndemnity(item.MailId));
            Assert.AreEqual(0, record.IndemnityComputedCents);
            Assert.AreEqual(5, registry.UncollectedFeeCents, "Only the registration fee stands — no payout moved.");

            // Never re-resolved.
            Assert.IsNotNull(registry.RecordLoss(item.MailId, "found again", 2, diag));
            Assert.AreEqual(1, record.LossRecordedDayIndex);

            // A lost article cannot be delivered afterwards.
            Assert.IsNotNull(registry.RecordDelivery(item.MailId, 1, "Emma", 2, diag));
        }

        [Test]
        public void Indemnity_WhenEnabled_CappedAtTwentyFiveDollars()
        {
            var journeys = TwoTowns();
            var postal = TwoOffices();
            var registry = RegistryOn(postal);
            registry.IndemnityAvailable = true; // the fork's calibration switch
            var diag = new List<string>();

            MailItem big = postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                "po-a", "po-b", 0, diag);
            RegisteredMailRecord bigRecord = registry.RegisterItem(
                big.MailId, 3000, false, false, 0, 0, diag); // $30 declared
            postal.AdvanceDay(journeys, 0, diag);
            Assert.IsNull(registry.RecordLoss(big.MailId, "lost", 1, diag));
            Assert.AreEqual(2500, registry.ComputeIndemnity(big.MailId),
                "Indemnity capped at the $25 base tier (1872 Act §190 / 1904 Postal Information).");
            Assert.AreEqual(2500, bigRecord.IndemnityComputedCents);

            MailItem small = postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                "po-a", "po-b", 0, diag);
            RegisteredMailRecord smallRecord = registry.RegisterItem(
                small.MailId, 1000, false, false, 0, 0, diag); // $10 declared
            Assert.IsNull(registry.RecordLoss(small.MailId, "lost", 0, diag));
            Assert.AreEqual(1000, smallRecord.IndemnityComputedCents);

            // Computed, never paid: no money moves on its own.
            Assert.AreEqual(10, registry.UncollectedFeeCents, "Two registration fees — no indemnity payout moved.");
            Assert.AreEqual(0, registry.ComputeIndemnity("nope"));
        }

        [Test]
        public void AuditOverdue_DeclaresBrokenChain()
        {
            var postal = TwoOffices();
            var registry = RegistryOn(postal);
            var diag = new List<string>();

            MailItem item = postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                "po-a", "po-b", 0, diag);
            RegisteredMailRecord record = registry.RegisterItem(
                item.MailId, 500, false, false, 0, 0, diag);

            // Simulate the chain breaking: dispatched but never arriving.
            item.Status = MailStatus.InTransit;
            item.DueArrivalDayIndex = 0;
            registry.AuditOverdue(29, diag);
            Assert.AreEqual(RegisteredMailStatus.Registered, record.Status,
                "Within the overdue threshold the chain is not yet declared broken.");
            registry.AuditOverdue(31, diag);
            Assert.AreEqual(RegisteredMailStatus.Lost, record.Status);
            Assert.IsTrue(record.LossReason.Contains("overdue"));
        }

        [Test]
        public void OffMapRegistered_NoAddresseeReceiptReturned()
        {
            var journeys = TwoTowns();
            var postal = TwoOffices();
            var registry = RegistryOn(postal);
            var diag = new List<string>();

            MailItem item = postal.PostItem(MailKind.Letter, "Kennedy", -1, "Arnold (Winnipeg)",
                "po-a", "offmap", 0, diag);
            RegisteredMailRecord record = registry.RegisterItem(
                item.MailId, 500, true, false, 0, 0, diag);
            postal.AdvanceDay(journeys, 0, diag);
            postal.AdvanceDay(journeys, 1, diag);
            Assert.AreEqual(MailStatus.ForwardedOffMap, item.Status);

            Assert.IsNotNull(registry.RecordDelivery(item.MailId, -1, "Arnold (Winnipeg)", 1, diag),
                "An article that left the map cannot be delivered.");
            Assert.IsEmpty(record.ReturnReceiptMailId,
                "Off-map articles get no addressee receipt (1905 §36: none unless demanded).");
        }

        [Test]
        public void MoneyOrderAdvice_CanTravelRegistered()
        {
            var journeys = TwoTowns();
            var postal = TwoOffices();
            var registry = RegistryOn(postal);
            var diag = new List<string>();

            var advice = new PostalMoneyOrderAdvice
            {
                SerialNumber = 7,
                IssuingOfficeId = "po-a",
                PayableOfficeId = "po-b",
                SenderName = "Kennedy",
                PayeeName = "Emma",
                AmountCents = 2000,
                SentDayIndex = 0,
            };
            RegisteredMailRecord record = registry.RegisterMoneyOrderAdviceLetter(
                advice, "Kennedy", 0, 0, diag);
            Assert.IsNotNull(record);
            Assert.AreEqual("Postmaster of po-b", record.RecipientName);
            Assert.AreEqual(-1, record.RecipientPersonId);

            MailItem item = postal.GetMailItem(record.MailId);
            Assert.IsNotNull(item);
            Assert.AreEqual("po-a", item.FromOfficeId);
            Assert.AreEqual("po-b", item.ToOfficeId);
            Assert.IsTrue(item.IsRegistered);

            postal.AdvanceDay(journeys, 0, diag);
            postal.AdvanceDay(journeys, 1, diag);
            // The postmaster signs for it by the name the letter is addressed to.
            Assert.IsNull(registry.RecordDelivery(record.MailId, -1, "Postmaster of po-b", 1, diag));
            Assert.AreEqual(RegisteredMailStatus.Delivered, record.Status);
        }

        [Test]
        public void SaveRoundTrip_PreservesRegistry()
        {
            var journeys = TwoTowns();
            var postal = TwoOffices();
            var registry = RegistryOn(postal);
            registry.IndemnityAvailable = true;
            var diag = new List<string>();

            MailItem item = postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                "po-a", "po-b", 0, diag);
            Assert.IsNotNull(registry.RegisterItem(item.MailId, 500, true, false, 0, 0, diag));
            postal.AdvanceDay(journeys, 0, diag);
            Assert.AreEqual(8, registry.UncollectedFeeCents);

            RegisteredMailSaveDto dto = registry.CaptureSaveDto();
            var restored = new RegisteredMailService(postal);
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, new List<RegisteredMailRecord>(restored.Records).Count);
            RegisteredMailRecord record = restored.GetRecord(item.MailId);
            Assert.IsNotNull(record);
            Assert.AreEqual("REG-po-a-0001", record.SerialNumber);
            Assert.AreEqual(2, record.CustodyLog.Count, "registered + dispatched entries survive.");
            Assert.AreEqual(8, restored.UncollectedFeeCents);
            Assert.IsTrue(restored.IndemnityAvailable);

            // Registry-number state survives: the next registration continues the sequence.
            MailItem item2 = postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                "po-a", "po-b", 0, diag);
            RegisteredMailRecord record2 = restored.RegisterItem(
                item2.MailId, 0, false, false, 0, 0, diag);
            Assert.AreEqual("REG-po-a-0002", record2.SerialNumber);
        }

        [Test]
        public void TaskDefinitions_RegisterWithoutError()
        {
            var registry = RegistryOn(TwoOffices());
            var authority = new TaskAuthority();
            registry.RegisterTaskDefinitions(authority);
            Assert.IsNotNull(authority.GetDefinition(RegisteredMailService.RegisterItemTaskId));
            Assert.IsNotNull(authority.GetDefinition(RegisteredMailService.RecordDeliveryTaskId));
        }
    }
}
