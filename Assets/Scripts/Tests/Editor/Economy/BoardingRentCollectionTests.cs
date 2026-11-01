using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Phase D (Real People): rent collection. BoarderRentDue computes dues;
    /// the collection service settles them through the real authorities:
    /// tenant HouseholdLedger.RecordOutflow → boarding-house cash inflow.
    /// Ledger conservation (tenant out == business in) holds cent for cent.
    /// Insufficient funds become REAL obligations (negotiated arrears, Canon
    /// §12.7H), never informal debt. Eviction is a process with notice
    /// (canon-gated, provisional — Canon §12.7G research hold).
    /// </summary>
    [TestFixture]
    public sealed class BoardingRentCollectionTests
    {
        private sealed class FakeCashSink : IRentCashSink
        {
            public string BusinessInstanceId => "boarding-1";
            public string BusinessName => "Widow Hart's Boarding House";
            public int CashCents { get; private set; }
            public readonly List<string> InflowLabels = new List<string>();
            public void RecordCashInflow(int cents, string label, int dayIndex)
            {
                CashCents += cents;
                InflowLabels.Add(label);
            }
        }

        private sealed class Fixture
        {
            public RentCollectionService Service = new RentCollectionService();
            public FakeCashSink Sink = new FakeCashSink();
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public List<string> Diag = new List<string>();
            public int NextMembershipId = 1;

            public void AddMember(int personId, int householdId)
            {
                string refusal = Memberships.Register(new HouseholdMembership
                {
                    MembershipId = NextMembershipId++,
                    PersonId = personId,
                    HouseholdId = householdId,
                    Lifecycle = HouseholdMembershipLifecycle.Active,
                    StartDayIndex = 0,
                    Source = HouseholdMembershipSource.Authored,
                });
                Assert.IsNull(refusal);
            }

            public void FundHousehold(int householdId, int cents)
            {
                string refusal = Ledgers.GetOrCreate(householdId).RecordInflow(
                    0, cents, HouseholdIncomeSource.WageEmployment, "emp-1",
                    "test wages", "employer");
                Assert.IsNull(refusal);
            }

            public BoarderRentDue WeeklyDue(int personId, int cents)
            {
                return new BoarderRentDue
                {
                    PersonId = personId, RoomId = "room-a", StayKind = BoarderStayKind.Weekly,
                    BoardIncluded = false, NightsCovered = 7, CentsDue = cents,
                    Label = $"weekly room only — person {personId}",
                };
            }
        }

        [Test]
        public void RentCollectionConservesEveryCent()
        {
            var f = new Fixture();
            f.AddMember(1, 10);
            f.FundHousehold(10, 5000);

            f.Service.SettleDues(new List<BoarderRentDue> { f.WeeklyDue(1, 800) },
                f.Sink, f.Ledgers, f.Memberships, f.Obligations, f.Ids, 7, f.Diag);

            Assert.AreEqual(4200, f.Ledgers.Get(10).GetBalanceCents());
            Assert.AreEqual(800, f.Sink.CashCents); // tenant out == business in
            Assert.AreEqual(0, f.Service.OpenArrears().Count);
        }

        [Test]
        public void InsufficientFundsBecomeRealObligationsNotInformalDebt()
        {
            var f = new Fixture();
            f.AddMember(1, 10);
            f.FundHousehold(10, 300); // cannot cover 800c rent

            f.Service.SettleDues(new List<BoarderRentDue> { f.WeeklyDue(1, 800) },
                f.Sink, f.Ledgers, f.Memberships, f.Obligations, f.Ids, 7, f.Diag);

            // Partial payment moved what the household held (Canon 12.7G tolerance)...
            Assert.AreEqual(300, f.Sink.CashCents);
            Assert.AreEqual(0, f.Ledgers.Get(10).GetBalanceCents());
            // ...and the remainder is a REAL obligation, not informal debt.
            List<RentArrearsRecord> arrears = f.Service.OpenArrears();
            Assert.AreEqual(1, arrears.Count);
            Assert.AreEqual(500, arrears[0].OutstandingCents);
            FinancialObligation obligation = f.Obligations.Find(arrears[0].ObligationId);
            Assert.IsNotNull(obligation);
            Assert.AreEqual(500, obligation.OutstandingPrincipalCents);
            Assert.AreEqual("household:10", obligation.Debtor);
            Assert.AreEqual("business:boarding-1", obligation.Creditor);
        }

        [Test]
        public void BrokeHouseholdOwesFullRentAsArrears()
        {
            var f = new Fixture();
            f.AddMember(1, 10);
            f.Ledgers.GetOrCreate(10); // empty ledger — no cash

            f.Service.SettleDues(new List<BoarderRentDue> { f.WeeklyDue(1, 800) },
                f.Sink, f.Ledgers, f.Memberships, f.Obligations, f.Ids, 7, f.Diag);

            Assert.AreEqual(0, f.Sink.CashCents);
            List<RentArrearsRecord> arrears = f.Service.OpenArrears();
            Assert.AreEqual(1, arrears.Count);
            Assert.AreEqual(800, arrears[0].OutstandingCents);
            Assert.IsNotNull(f.Obligations.Find(arrears[0].ObligationId));
        }

        [Test]
        public void ArrearsRepaymentMovesCashAndPaysDownTheObligation()
        {
            var f = new Fixture();
            f.AddMember(1, 10);
            f.FundHousehold(10, 300);
            f.Service.SettleDues(new List<BoarderRentDue> { f.WeeklyDue(1, 800) },
                f.Sink, f.Ledgers, f.Memberships, f.Obligations, f.Ids, 7, f.Diag);
            RentArrearsRecord record = f.Service.OpenArrears()[0];

            f.FundHousehold(10, 500); // wages arrive
            Assert.IsNull(f.Service.PayArrears(record.ArrearsId, 500, f.Sink, f.Ledgers, f.Obligations, 14, f.Diag));

            Assert.AreEqual(RentArrearsStatus.Repaid, record.Status);
            Assert.AreEqual(0, f.Ledgers.Get(10).GetBalanceCents());
            Assert.AreEqual(800, f.Sink.CashCents); // 300 partial + 500 repayment
            Assert.AreEqual(0, f.Obligations.Find(record.ObligationId).OutstandingPrincipalCents);
            Assert.AreEqual(0, f.Service.OpenArrears().Count);
        }

        [Test]
        public void EvictionRequiresNoticeAndWaitingPeriod()
        {
            var f = new Fixture();
            f.AddMember(1, 10);
            f.Ledgers.GetOrCreate(10);
            f.Service.SettleDues(new List<BoarderRentDue> { f.WeeklyDue(1, 800) },
                f.Sink, f.Ledgers, f.Memberships, f.Obligations, f.Ids, 7, f.Diag);

            var checkedOut = new List<int>();
            Func<int, string> checkout = personId => { checkedOut.Add(personId); return null; };
            RentArrearsRecord arrearsRecord = f.Service.OpenArrears()[0];

            EvictionNotice notice = f.Service.IssueEvictionNotice(1, 10, "boarding-1",
                "unpaid rent arrears", 7, f.Diag);
            Assert.IsTrue(notice.LegalProcessProvisional);
            Assert.AreEqual(7 + 14, notice.EffectiveDayIndex);

            // Before the notice period ends: no removal, even with arrears open.
            f.Service.ProcessEvictions(20, checkout, f.Diag);
            Assert.AreEqual(0, checkedOut.Count);
            Assert.AreEqual(EvictionNoticeStatus.Issued, notice.Status);

            // After the notice period with arrears still open: removal through the real path.
            f.Service.ProcessEvictions(21, checkout, f.Diag);
            Assert.AreEqual(1, checkedOut.Count);
            Assert.AreEqual(1, checkedOut[0]);
            Assert.AreEqual(EvictionNoticeStatus.Executed, notice.Status);
            // The debt survives eviction — it is not forgiven.
            Assert.AreEqual(RentArrearsStatus.Evicted, arrearsRecord.Status);
            Assert.AreEqual(800, f.Obligations.Find(arrearsRecord.ObligationId).OutstandingPrincipalCents);
        }

        [Test]
        public void RepaidArrearsWithdrawTheNotice()
        {
            var f = new Fixture();
            f.AddMember(1, 10);
            f.FundHousehold(10, 300);
            f.Service.SettleDues(new List<BoarderRentDue> { f.WeeklyDue(1, 800) },
                f.Sink, f.Ledgers, f.Memberships, f.Obligations, f.Ids, 7, f.Diag);
            RentArrearsRecord record = f.Service.OpenArrears()[0];

            EvictionNotice notice = f.Service.IssueEvictionNotice(1, 10, "boarding-1",
                "unpaid rent arrears", 7, f.Diag);

            f.FundHousehold(10, 500);
            Assert.IsNull(f.Service.PayArrears(record.ArrearsId, 500, f.Sink, f.Ledgers, f.Obligations, 10, f.Diag));

            var checkedOut = new List<int>();
            f.Service.ProcessEvictions(30, personId => { checkedOut.Add(personId); return null; }, f.Diag);

            Assert.AreEqual(0, checkedOut.Count);
            Assert.AreEqual(EvictionNoticeStatus.Withdrawn, notice.Status);
        }

        [Test]
        public void PersonWithoutHouseholdSettlesNothingAndFakesNothing()
        {
            var f = new Fixture();
            // P99 has no membership at all.
            f.Service.SettleDues(new List<BoarderRentDue> { f.WeeklyDue(99, 800) },
                f.Sink, f.Ledgers, f.Memberships, f.Obligations, f.Ids, 7, f.Diag);

            Assert.AreEqual(0, f.Sink.CashCents);
            Assert.AreEqual(0, f.Service.OpenArrears().Count);
        }

        [Test]
        public void SaveLoadRoundTripHasNoDuplicatedArrears()
        {
            var f = new Fixture();
            f.AddMember(1, 10);
            f.Ledgers.GetOrCreate(10);
            f.Service.SettleDues(new List<BoarderRentDue> { f.WeeklyDue(1, 800) },
                f.Sink, f.Ledgers, f.Memberships, f.Obligations, f.Ids, 7, f.Diag);
            f.Service.IssueEvictionNotice(1, 10, "boarding-1", "unpaid rent", 7, f.Diag);

            RentCollectionService.RentCollectionServiceSaveDto dto = f.Service.CaptureSaveDto();
            var reloaded = new RentCollectionService();
            reloaded.LoadFromSaveDto(dto);
            reloaded.LoadFromSaveDto(dto); // double import must not duplicate

            Assert.AreEqual(1, reloaded.OpenArrears().Count);
            Assert.AreEqual(1, reloaded.Notices.Count);
        }
    }
}
