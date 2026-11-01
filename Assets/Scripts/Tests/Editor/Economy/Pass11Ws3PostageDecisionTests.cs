using System.Collections.Generic;
using LandLedgers.Economy.Postal;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass XI WS3 (PASS9-004): the postage decision, locked as a
    /// documented honest-unset rather than an invented sender.
    /// The money-order advice letter has no funded sender in any
    /// production call context (zero production callers; the advice
    /// itself travels through the money-order estimator, not
    /// registered mail), and generic household/business mail has no
    /// production PostItem caller either. So no sender is invented:
    /// with a production-shaped resolver wired, an unfunded advice
    /// sender is REFUSED, never posted free; when a real sender
    /// household exists, the funded path debits its postage.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws3PostageDecisionTests
    {
        private static PostalService TwoOffices()
        {
            var postal = new PostalService();
            var diag = new List<string>();
            Assert.IsNull(postal.RegisterOffice("po-a", "town-a", "biz-po-a", new List<int> { 0 }, 7, diag));
            Assert.IsNull(postal.RegisterOffice("po-b", "town-b", "biz-po-b", new List<int> { 0 }, 9, diag));
            return postal;
        }

        private static PostalMoneyOrderAdvice Advice()
        {
            return new PostalMoneyOrderAdvice
            {
                SerialNumber = 7,
                IssuingOfficeId = "po-a",
                PayableOfficeId = "po-b",
                SenderName = "Kennedy",
                PayeeName = "Emma",
                AmountCents = 2000,
                SentDayIndex = 0,
            };
        }

        [Test]
        public void AdviceLetter_UnfundedSender_Refused_NeverFreePost()
        {
            PostalService postal = TwoOffices();
            var registry = new RegisteredMailService(postal);
            // Production-shaped wiring: a resolver exists, but no
            // household resolves for the advice sender (-1 is no person).
            registry.PersonHouseholdLedgerResolver = personId => null;
            var diag = new List<string>();

            RegisteredMailRecord record = registry.RegisterMoneyOrderAdviceLetter(
                Advice(), "Post Office", -1, 0, diag);

            Assert.IsNull(record, "an unfunded advice letter must not travel on free carriage");
            Assert.AreEqual(0, registry.Records.Count, "nothing was registered");
            bool diagnosed = false;
            foreach (string line in diag)
                if (line.Contains("no funded household")) diagnosed = true;
            Assert.IsTrue(diagnosed, "the refusal names the missing funded sender");
        }

        [Test]
        public void AdviceLetter_FundedSenderHousehold_PostageDebitedFromThatHousehold()
        {
            PostalService postal = TwoOffices();
            var registry = new RegisteredMailService(postal);
            var ledgers = new HouseholdLedgerRegistry();
            HouseholdLedger household = ledgers.GetOrCreate(11);
            Assert.IsNull(household.RecordInflow(0, 1000, HouseholdIncomeSource.Remittance, "test", "test funding", "test"));
            registry.PersonHouseholdLedgerResolver = personId => personId == 0 ? household : null;
            var diag = new List<string>();

            RegisteredMailRecord record = registry.RegisterMoneyOrderAdviceLetter(
                Advice(), "Kennedy", 0, 0, diag);

            Assert.IsNotNull(record, "a real funded sender can carry the letter");
            Assert.AreEqual(997, household.GetBalanceCents(),
                "letter postage (3c) leaves the sender household — carriage is paid, never minted");
            Assert.AreEqual("Postmaster of po-b", record.RecipientName);
        }
    }
}
