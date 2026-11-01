using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Postal;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Population;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>Pass IX review R-03: production-shaped postage — senders are
    /// debited through the wired resolvers, exactly the postage later
    /// collected. No explicit payer argument is passed in these tests: the
    /// payer comes from the call context (hiring business / sender person /
    /// receipt-requesting recipient) via the production resolvers.</summary>
    [TestFixture]
    public sealed class Pass9ReviewFixPostagePayerTests
    {
        private static JourneyModel TwoTowns()
        {
            var model = new JourneyModel();
            model.RegisterLocation(new JourneyLocation("town-a", JourneyLocationKind.TownBuilding, "Town A", 0f, 0f));
            model.RegisterLocation(new JourneyLocation("town-b", JourneyLocationKind.TownBuilding, "Town B", 8f, 0f));
            model.AddEdge("town-a", "town-b", 8f, "stage road");
            return model;
        }

        private static PostalService TwoOffices()
        {
            var postal = new PostalService();
            var diag = new List<string>();
            Assert.IsNull(postal.RegisterOffice("po-a", "town-a", "biz-po-a", new List<int> { 0 }, 7, diag));
            Assert.IsNull(postal.RegisterOffice("po-b", "town-b", "biz-po-b", new List<int> { 0 }, 9, diag));
            return postal;
        }

        private static BusinessRuntimeState Runtime(string id, int cash)
        {
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(id);
            runtime.SetCurrentCashCents(cash);
            return runtime;
        }

        private static HouseholdLedger FundedLedger(int householdId, int cents)
        {
            var ledger = new HouseholdLedger(householdId);
            Assert.IsNull(ledger.RecordInflow(0, cents, HouseholdIncomeSource.OwnerContribution, "seed", "seed", "test"));
            return ledger;
        }

        [Test]
        public void RecruitmentLetter_BusinessSenderDebited_ExactlyThePostageLaterCollected()
        {
            JourneyModel journeys = TwoTowns();
            PostalService postal = TwoOffices();
            var diag = new List<string>();
            BusinessRuntimeState senderBusiness = Runtime("biz-store", 100);
            BusinessRuntimeState officeBusiness = Runtime("biz-po-a", 0);

            var recruiting = new RecruitmentService();
            // Production wiring (SimulationDrivers): resolver from the shared runtime.
            recruiting.SenderBusinessRuntimeResolver = id => id == "biz-store" ? senderBusiness : null;
            RecruitmentEffort effort = recruiting.OpenEffort("biz-store", "clerk",
                RecruitmentChannel.Correspondence, 0, 42, diag);
            Assert.IsNotNull(effort);

            RecruitmentLetter letter = recruiting.SendLetter(effort.EffortId, 7,
                "town-a", "town-b", journeys, 0, diag, postal);

            Assert.IsNotNull(letter);
            Assert.IsNotEmpty(letter.PostalMailId, "letter traveled by post, paid");
            Assert.AreEqual(97, senderBusiness.CurrentCashCents, "hiring business debited the 3c letter postage");
            Assert.AreEqual(3, postal.UncollectedPostageCents, "the pot holds exactly the sender's debit");

            int posted = postal.CollectAndPostPostageRevenue(officeBusiness, diag);
            Assert.AreEqual(3, posted);
            Assert.AreEqual(3, officeBusiness.CurrentCashCents, "office collected exactly what the sender was debited");
            Assert.AreEqual(100, senderBusiness.CurrentCashCents + officeBusiness.CurrentCashCents,
                "total business cash conserved: 97 sender + 3 office");
        }

        [Test]
        public void RegisteredLetter_HouseholdSenderDebited_ViaPersonResolver()
        {
            PostalService postal = TwoOffices();
            var diag = new List<string>();
            HouseholdLedger senderLedger = FundedLedger(11, 100);
            var registry = new RegisteredMailService(postal);
            registry.PersonHouseholdLedgerResolver = personId => personId == 10 ? senderLedger : null;

            RegisteredMailRecord record = registry.PostRegisteredLetter(
                "Kennedy", 10, 20, "Emma", "po-a", "po-b",
                500, false, false, 0, diag);

            Assert.IsNotNull(record);
            Assert.AreEqual(97, senderLedger.GetBalanceCents(), "sender household debited the 3c postage at posting");
            Assert.AreEqual(3, postal.UncollectedPostageCents);
        }

        [Test]
        public void ReturnReceipt_RecipientHouseholdAssignedAsPayer_AndDebited()
        {
            JourneyModel journeys = TwoTowns();
            PostalService postal = TwoOffices();
            var diag = new List<string>();
            HouseholdLedger senderLedger = FundedLedger(11, 100);
            HouseholdLedger recipientLedger = FundedLedger(22, 100);
            var registry = new RegisteredMailService(postal);
            registry.PersonHouseholdLedgerResolver = personId =>
                personId == 10 ? senderLedger : personId == 20 ? recipientLedger : null;

            RegisteredMailRecord record = registry.PostRegisteredLetter(
                "Kennedy", 10, 20, "Emma", "po-a", "po-b",
                500, true, false, 0, diag);
            Assert.IsNotNull(record);
            Assert.AreEqual(97, senderLedger.GetBalanceCents());
            MailItem item = postal.GetMailItem(record.MailId);
            postal.AdvanceDay(journeys, 0, diag);
            postal.AdvanceDay(journeys, 1, diag);
            Assert.AreEqual(MailStatus.Arrived, item.Status);

            Assert.IsNull(registry.RecordDelivery(item.MailId, 20, "Emma", 1, diag));

            Assert.IsNotNull(registry.ReturnReceiptPayer, "R-03: ReturnReceiptPayer assigned from the funded recipient");
            Assert.IsNotEmpty(record.ReturnReceiptMailId, "the receipt is a real mail item");
            Assert.AreEqual(97, recipientLedger.GetBalanceCents(), "recipient household debited the receipt's 3c postage");
            Assert.AreEqual(6, postal.UncollectedPostageCents,
                "pot = original sender debit (3c) + receipt payer debit (3c) — every collected cent was debited first");
        }

        [Test]
        public void UnresolvableSender_NoPayerFabricated_RecruitmentFallsBackToFootMessenger()
        {
            JourneyModel journeys = TwoTowns();
            PostalService postal = TwoOffices();
            var diag = new List<string>();
            var recruiting = new RecruitmentService();
            recruiting.SenderBusinessRuntimeResolver = id => null; // business not in the shared runtime
            RecruitmentEffort effort = recruiting.OpenEffort("biz-ghost", "clerk",
                RecruitmentChannel.Correspondence, 0, 42, diag);

            RecruitmentLetter letter = recruiting.SendLetter(effort.EffortId, 7,
                "town-a", "town-b", journeys, 0, diag, postal);

            Assert.IsNotNull(letter, "the letter still goes — by foot messenger, not free post");
            Assert.IsEmpty(letter.PostalMailId);
            Assert.AreEqual(0, postal.UncollectedPostageCents, "no postage accrued from an unpaid sender");
        }
    }
}
