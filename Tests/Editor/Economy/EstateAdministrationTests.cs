using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Population;
using LandLedgers.World.Property;
using LandLedgers.Economy.Financing;
using NUnit.Framework;

namespace LandLedgers.Economy.Estates.Tests
{
    /// <summary>
    /// D4D: estate administration — the executor's job as a real process.
    /// Canon §8.3 (death redistributes ownership and authority); the hard
    /// payment order and abatement classes follow 1870s American practice
    /// (administration/funeral first, then secured, then preferred taxes,
    /// then unsecured; residue abates first, then general pro rata, then
    /// specific last), while exact fee schedules and territorial statutes
    /// stay parameterized (Tech X §10.1 research hold; Canon 17.10). An open
    /// D4C contest freezes the estate. Executor malfeasance is never
    /// auto-detected — acts are recorded, future systems judge.
    /// </summary>
    public sealed class EstateAdministrationTests
    {
        private List<string> diag;
        private EntityIdRegistry ids;
        private PopulationState population;
        private TitleAuthority titles;
        private KinshipRegistry kinship;
        private EstateService estates;
        private EstateAdministrationService admin;
        private CreditRegistry credit;

        private static PersonState Person(int id, string name)
        {
            return new PersonState
            {
                id = id, firstName = name, lastName = "Test",
                age = 40, ageBand = AgeBand.Adult18Plus,
                laborAccessLevel = LaborAccessLevel.FullLaborMarket, householdId = 1,
            };
        }

        [SetUp]
        public void SetUp()
        {
            diag = new List<string>();
            ids = new EntityIdRegistry();
            population = new PopulationState();
            population.people.Add(Person(1, "Decedent"));
            population.people.Add(Person(2, "Child"));
            population.people.Add(Person(3, "Witness"));
            population.people.Add(Person(4, "Witness2"));
            population.people.Add(Person(5, "Executor"));
            population.people.Add(Person(6, "Stranger"));
            titles = new TitleAuthority();
            kinship = new KinshipRegistry();
            estates = new EstateService();
            admin = new EstateAdministrationService();
            credit = new CreditRegistry();
        }

        /// <summary>
        /// Builds a testate estate: P1 died, P2 is the child, P5 is the
        /// will-named executor, parcel p1 inventoried, will probated, P5's
        /// appointment accepted.
        /// </summary>
        private void SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will)
        {
            kinship.AddParentChild(1, 2, "test");
            estate = estates.OpenEstate(ids, 1, "Decedent Test", kinship, 100, diag);
            titles.RegisterParcel("p1", "40 acres", 40f, "Decedent Test", TitleBasis.HomesteadClaim, 10, diag);
            estates.RegisterDecedentParcel(estate, "p1", titles, diag);
            probate = new ProbateService();
            will = probate.RecordWill(ids, 1, "Decedent Test", 5, new List<int> { 3, 4 }, 90, population, diag);
            probate.AddBequest(will,
                new Bequest { Kind = BequestKind.Parcel, BeneficiaryPersonId = 2, BeneficiaryName = "Child Test", ParcelId = "p1" },
                population, diag);
            Assert.IsNull(probate.ProbateWill(will, estate, titles, 100, diag));
            Assert.IsNotNull(admin.AcceptExecutorAppointment(ids, estate, will, estates, 5, "Executor Test", population, 101, diag));
        }

        [Test]
        public void ExecutorAcceptance_RequiresProbatedWill()
        {
            kinship.AddParentChild(1, 2, "test");
            Estate estate = estates.OpenEstate(ids, 1, "Decedent Test", kinship, 100, diag);
            var probate = new ProbateService();
            Will will = probate.RecordWill(ids, 1, "Decedent Test", 5, new List<int> { 3, 4 }, 90, population, diag);
            Assert.IsNotNull(will);
            Assert.IsNull(admin.AcceptExecutorAppointment(ids, estate, will, estates, 5, "Executor Test", population, 101, diag),
                "Authority flows from probate — the paper alone grants nothing.");
            Assert.IsNull(admin.FindAppointment(estate.EstateId, 5));
        }

        [Test]
        public void ExecutorAcceptance_GrantsAuthorityOnlyToTheNamed()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            Assert.IsNull(admin.RequireAuthority(estate, 5, "act", diag), "Accepted executor may act.");
            Assert.IsNotNull(admin.RequireAuthority(estate, 6, "act", diag), "No appointment, no authority.");
            Assert.IsNull(admin.AcceptExecutorAppointment(ids, estate, will, estates, 6, "Stranger Test", population, 102, diag),
                "The will names P5, not P6 — authority is never assumed.");
        }

        [Test]
        public void CoExecutor_RequiresNominationCitation()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            Assert.IsNull(admin.AcceptCoExecutorAppointment(ids, estate, will, estates, 6, "Stranger Test",
                string.Empty, population, 102, diag), "The nomination must be cited — never assumed.");
            EstateAdministratorAppointment co = admin.AcceptCoExecutorAppointment(ids, estate, will, estates, 6,
                "Stranger Test", "will clause 3 (second executor)", population, 102, diag);
            Assert.IsNotNull(co);
            Assert.AreEqual(EstateAppointmentKind.CoExecutor, co.Kind);
            Assert.IsTrue(co.Accepted);
            Assert.IsNull(admin.RequireAuthority(estate, 6, "act", diag), "An accepted co-executor holds authority too.");
        }

        [Test]
        public void Administrator_IntestateOnly()
        {
            // Testate: refused.
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            Assert.IsNull(admin.GrantAdministrator(ids, estate, estates, 6, "Stranger Test",
                "letters of administration", population, 102, diag), "Testate estates get executors, not administrators.");

            // Intestate: granted.
            kinship.AddParentChild(1, 2, "test");
            Estate intestate = estates.OpenEstate(ids, 1, "Decedent Test", kinship, 100, diag);
            EstateAdministratorAppointment appointment = admin.GrantAdministrator(ids, intestate, estates, 5,
                "Executor Test", "letters of administration, county court, day 101", population, 101, diag);
            Assert.IsNotNull(appointment);
            Assert.AreEqual(EstateAppointmentKind.Administrator, appointment.Kind);
            Assert.IsNull(admin.RequireAuthority(intestate, 5, "act", diag), "The administrator reuses the same pipeline.");
            Assert.IsNull(admin.GrantAdministrator(ids, intestate, estates, 6, "Stranger Test",
                string.Empty, population, 102, diag), "The court grant is the authority — it must be recorded.");
        }

        [Test]
        public void MarshalParcel_OnlyInventoriedParcels()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            Assert.IsNull(admin.MarshalParcel(estate, 5, "p1", "40 acres", 200000, 102, diag));
            Assert.AreEqual(1, admin.InventoryFor(estate.EstateId).Count);
            Assert.AreEqual(EstateInventoryLineKind.Parcel, admin.InventoryFor(estate.EstateId)[0].Kind);
            titles.RegisterParcel("p9", "stray acres", 10f, "Stranger", TitleBasis.HomesteadClaim, 10, diag);
            Assert.IsNotNull(admin.MarshalParcel(estate, 5, "p9", "stray acres", 50000, 102, diag),
                "Inventory assembles from the registries — never invented.");
            Assert.AreEqual(1, admin.InventoryFor(estate.EstateId).Count);
        }

        [Test]
        public void MarshalDepositAccount_DecedentOnly()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            var deposits = new BankDepositLedger("First Bank");
            deposits.OpenAccount("a1", "Decedent Test", DepositKind.Demand, 10000, 50, 0, 0, diag);
            deposits.OpenAccount("a2", "Stranger", DepositKind.Demand, 99999, 50, 0, 0, diag);
            Assert.IsNull(admin.MarshalDepositAccount(estate, 5, deposits, "a1", 102, diag));
            Assert.AreEqual(10000, admin.InventoryFor(estate.EstateId)[0].EstimatedValueCents);
            Assert.IsNotNull(admin.MarshalDepositAccount(estate, 5, deposits, "a2", 102, diag),
                "Another person's account is not the estate's asset.");
            Assert.IsNotNull(admin.MarshalDepositAccount(estate, 5, deposits, "nope", 102, diag),
                "No invented accounts.");
        }

        [Test]
        public void MarshalNoteReceivable_DecedentPayeeOnly()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            PromissoryNote good = credit.IssuePromissoryNote(ids, "Maker Test", "Decedent Test", 25000, "6 months", 60, null, diag);
            Assert.IsNull(admin.MarshalNoteReceivable(estate, 5, good, 25000, 102, diag));
            PromissoryNote other = credit.IssuePromissoryNote(ids, "Maker Test", "Stranger", 25000, "6 months", 60, null, diag);
            Assert.IsNotNull(admin.MarshalNoteReceivable(estate, 5, other, 25000, 102, diag),
                "Paper payable to someone else is not the estate's asset.");
            good.Status = CreditInstrumentStatus.Satisfied;
            Assert.IsNotNull(admin.MarshalNoteReceivable(estate, 5, good, 25000, 102, diag),
                "Discharged paper is not an asset.");
        }

        [Test]
        public void ClaimValidation_AcceptsRealDebt()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            PromissoryNote note = credit.IssuePromissoryNote(ids, "Decedent Test", "Creditor Test", 50000, "6 months", 60, null, diag);
            string noteId = note.InstrumentId.ToString();
            estates.RecordEstateDebt(estate, noteId, diag);
            EstateCreditorClaim claim = admin.RecordCreditorClaim(estate, noteId, EstateClaimRank.UnsecuredCreditor,
                50000, note, 5, 102, diag);
            Assert.IsNotNull(claim);
            Assert.IsTrue(claim.Validated);
            Assert.AreEqual("Creditor Test", claim.CreditorName);
            Assert.AreEqual(EstateClaimRank.UnsecuredCreditor, claim.Rank);
            Assert.AreEqual(EstateStatus.DebtsSettling, estate.Status);
        }

        [Test]
        public void ClaimValidation_RankConsistency()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            PromissoryNote note = credit.IssuePromissoryNote(ids, "Decedent Test", "Creditor Test", 50000, "6 months", 60, null, diag);
            string noteId = note.InstrumentId.ToString();
            estates.RecordEstateDebt(estate, noteId, diag);
            Assert.IsNull(admin.RecordCreditorClaim(estate, noteId, EstateClaimRank.SecuredDebt,
                50000, note, 5, 102, diag), "A plain note is not secured — rank it honestly.");
            MortgageDeed mortgage = credit.IssueMortgage(ids, "Decedent Test", "Bank Test", "p1", "40 acres", 200000, "10 years", 60, diag);
            string mortgageId = mortgage.InstrumentId.ToString();
            estates.RecordEstateDebt(estate, mortgageId, diag);
            Assert.IsNull(admin.RecordCreditorClaim(estate, mortgageId, EstateClaimRank.UnsecuredCreditor,
                200000, mortgage, 5, 102, diag), "A mortgage IS secured — never ranked below its kind.");
            Assert.IsNotNull(admin.RecordCreditorClaim(estate, mortgageId, EstateClaimRank.SecuredDebt,
                200000, mortgage, 5, 102, diag));
        }

        [Test]
        public void ClaimValidation_RefusesUnknownDebtAndExcessAmount()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            PromissoryNote note = credit.IssuePromissoryNote(ids, "Decedent Test", "Creditor Test", 50000, "6 months", 60, null, diag);
            Assert.IsNull(admin.RecordCreditorClaim(estate, "nope-1", EstateClaimRank.UnsecuredCreditor,
                50000, note, 5, 102, diag), "Claims are validated against recorded debts.");
            string noteId = note.InstrumentId.ToString();
            estates.RecordEstateDebt(estate, noteId, diag);
            Assert.IsNull(admin.RecordCreditorClaim(estate, noteId, EstateClaimRank.UnsecuredCreditor,
                60000, note, 5, 102, diag), "The claim cannot exceed the recorded obligation.");
            PromissoryNote wrong = credit.IssuePromissoryNote(ids, "Decedent Test", "Creditor Test", 50000, "6 months", 60, null, diag);
            Assert.IsNull(admin.RecordCreditorClaim(estate, noteId, EstateClaimRank.UnsecuredCreditor,
                50000, wrong, 5, 102, diag), "No proxy claims — the instrument must BE the named debt.");
            GuarantyAgreement guaranty = credit.IssueGuaranty(ids, "Decedent Test", "Bank Test", "Other Test",
                "covered-1", 10000, "terms", 60, diag);
            string guarantyId = guaranty.InstrumentId.ToString();
            estates.RecordEstateDebt(estate, guarantyId, diag);
            Assert.IsNull(admin.RecordCreditorClaim(estate, guarantyId, EstateClaimRank.UnsecuredCreditor,
                10000, guaranty, 5, 102, diag), "Contingent exposure is not a payable claim.");
        }

        /// <summary>
        /// The hard order: funeral (rank 2) → secured (rank 3) → unsecured
        /// (rank 5). Hand-traced: receipt 300000c; funeral 5000 → 295000;
        /// mortgage 200000 → 95000; note 50000 → 45000.
        /// </summary>
        [Test]
        public void HardOrder_PaysRanksInSequence()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            PromissoryNote note = credit.IssuePromissoryNote(ids, "Decedent Test", "Creditor Test", 50000, "6 months", 60, null, diag);
            MortgageDeed mortgage = credit.IssueMortgage(ids, "Decedent Test", "Bank Test", "p1", "40 acres", 200000, "10 years", 60, diag);
            string noteId = note.InstrumentId.ToString();
            string mortgageId = mortgage.InstrumentId.ToString();
            estates.RecordEstateDebt(estate, noteId, diag);
            estates.RecordEstateDebt(estate, mortgageId, diag);
            EstateCreditorClaim funeral = admin.RecordFuneralObligation(estate, estates, 5000, "burial costs", 5, 102, diag);
            EstateCreditorClaim secured = admin.RecordCreditorClaim(estate, mortgageId, EstateClaimRank.SecuredDebt, 200000, mortgage, 5, 102, diag);
            EstateCreditorClaim unsecured = admin.RecordCreditorClaim(estate, noteId, EstateClaimRank.UnsecuredCreditor, 50000, note, 5, 102, diag);
            Assert.IsNotNull(funeral);
            Assert.IsNotNull(secured);
            Assert.IsNotNull(unsecured);

            Assert.IsNotNull(admin.OpenEstateAccount(estate, 5, 102, diag));
            Assert.IsNull(admin.RecordEstateReceipt(estate, 5, 300000, "sale of personal property", 103, diag));

            Assert.IsNotNull(admin.DisburseForClaim(estate, estates, unsecured, 5, 104, null, diag),
                "Rank 5 waits for rank 2 — the hard order is never skipped.");
            Assert.IsNull(admin.DisburseForClaim(estate, estates, funeral, 5, 104, null, diag));
            Assert.AreEqual(295000, admin.AccountFor(estate.EstateId).BalanceCents);
            Assert.IsNotNull(admin.DisburseForClaim(estate, estates, unsecured, 5, 105, null, diag),
                "Rank 5 waits for rank 3 now.");
            Assert.IsNull(admin.DisburseForClaim(estate, estates, secured, 5, 105, null, diag));
            Assert.AreEqual(95000, admin.AccountFor(estate.EstateId).BalanceCents);
            Assert.IsNull(admin.DisburseForClaim(estate, estates, unsecured, 5, 106, null, diag));
            Assert.AreEqual(45000, admin.AccountFor(estate.EstateId).BalanceCents);
            Assert.IsTrue(funeral.Settled && secured.Settled && unsecured.Settled);
            Assert.AreEqual(0, estate.EstateDebtIds.Count, "Every settled claim clears the estate debt record.");
        }

        [Test]
        public void Disburse_RefusesShortfallLoudly()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            PromissoryNote note = credit.IssuePromissoryNote(ids, "Decedent Test", "Creditor Test", 50000, "6 months", 60, null, diag);
            string noteId = note.InstrumentId.ToString();
            estates.RecordEstateDebt(estate, noteId, diag);
            EstateCreditorClaim claim = admin.RecordCreditorClaim(estate, noteId, EstateClaimRank.UnsecuredCreditor, 50000, note, 5, 102, diag);
            Assert.IsNotNull(admin.OpenEstateAccount(estate, 5, 102, diag));
            // No receipt — the account holds 0c.
            string problem = admin.DisburseForClaim(estate, estates, claim, 5, 104, null, diag);
            Assert.IsNotNull(problem, "Shortfalls are refused loudly — never auto-paid, never partial.");
            StringAssert.Contains("SHORTFALL", problem);
            Assert.IsFalse(claim.Settled);
            Assert.AreEqual(0, admin.AccountFor(estate.EstateId).BalanceCents);
        }

        [Test]
        public void ContestFreeze_BlocksDisbursementUntilResolved()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            PromissoryNote note = credit.IssuePromissoryNote(ids, "Decedent Test", "Creditor Test", 50000, "6 months", 60, null, diag);
            string noteId = note.InstrumentId.ToString();
            estates.RecordEstateDebt(estate, noteId, diag);
            EstateCreditorClaim claim = admin.RecordCreditorClaim(estate, noteId, EstateClaimRank.UnsecuredCreditor, 50000, note, 5, 102, diag);
            Assert.IsNotNull(admin.OpenEstateAccount(estate, 5, 102, diag));
            Assert.IsNull(admin.RecordEstateReceipt(estate, 5, 50000, "collected note", 103, diag));

            var contests = new WillContestService();
            var grounds = new List<WillContestGroundClaim>
            {
                new WillContestGroundClaim { Ground = WillContestGround.UndueInfluence, ClaimStatement = "The neighbor dictated the will." },
            };
            WillContest contest = contests.FileContest(ids, probate, will, estate, 2, "Child Test",
                WillContestStanding.HeirAtLaw, null, grounds, kinship, population, 110, diag);
            Assert.IsNotNull(contest, "Contest filed — the estate freezes.");
            string problem = admin.DisburseForClaim(estate, estates, claim, 5, 111, contests, diag);
            Assert.IsNotNull(problem, "An open contest freezes the estate.");
            StringAssert.Contains("FROZEN", problem);
            Assert.IsFalse(claim.Settled);

            Assert.IsNull(contests.WithdrawContest(contest, probate, 115, diag));
            Assert.IsNull(admin.DisburseForClaim(estate, estates, claim, 5, 116, contests, diag),
                "The proceeding ended — administration resumes.");
            Assert.IsTrue(claim.Settled);
        }

        [Test]
        public void FullFlow_DistributesBequestResidueAndCloses()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            PromissoryNote note = credit.IssuePromissoryNote(ids, "Decedent Test", "Creditor Test", 50000, "6 months", 60, null, diag);
            string noteId = note.InstrumentId.ToString();
            estates.RecordEstateDebt(estate, noteId, diag);
            EstateCreditorClaim claim = admin.RecordCreditorClaim(estate, noteId, EstateClaimRank.UnsecuredCreditor, 50000, note, 5, 102, diag);
            Assert.IsNotNull(admin.OpenEstateAccount(estate, 5, 102, diag));
            Assert.IsNull(admin.RecordEstateReceipt(estate, 5, 50000, "sale of tools", 103, diag));
            Assert.IsNull(admin.DisburseForClaim(estate, estates, claim, 5, 104, null, diag));

            Assert.IsNull(admin.AdministerBequestDistribution(estate, probate, will, will.Bequests[0],
                5, ids, titles, 105, null, diag), "Debts settled — the devise may convey.");
            Assert.AreEqual("Child Test", titles.CurrentHolder("p1"));

            Assert.IsNull(admin.RecordResidueDisbursement(estate, will, 2, "Child Test", 5, 106, null, diag));
            Assert.AreEqual(0, admin.AccountFor(estate.EstateId).BalanceCents, "The residue empties the account.");
            Assert.IsNull(admin.CloseAdministration(estate, estates, 5, 107, null, diag));
            Assert.AreEqual(EstateStatus.Closed, estate.Status);
        }

        [Test]
        public void Residue_RefusedWhileDebtsUnsettled()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            PromissoryNote note = credit.IssuePromissoryNote(ids, "Decedent Test", "Creditor Test", 50000, "6 months", 60, null, diag);
            string noteId = note.InstrumentId.ToString();
            estates.RecordEstateDebt(estate, noteId, diag);
            admin.RecordCreditorClaim(estate, noteId, EstateClaimRank.UnsecuredCreditor, 50000, note, 5, 102, diag);
            Assert.IsNotNull(admin.OpenEstateAccount(estate, 5, 102, diag));
            Assert.IsNull(admin.RecordEstateReceipt(estate, 5, 50000, "sale of tools", 103, diag));
            Assert.IsNotNull(admin.RecordResidueDisbursement(estate, will, 2, "Child Test", 5, 106, null, diag),
                "The residue is last — always.");
        }

        /// <summary>
        /// Insolvent estate: two 100000c legacies, only 100000c in the
        /// account. Hand-traced: shortfall 100000c; residue eliminated; each
        /// legacy reduced 50000c (pro rata: 100000*100000/200000; the last
        /// takes the remainder).
        /// </summary>
        [Test]
        public void Abatement_InsolventEstateAbatesInOrder()
        {
            kinship.AddParentChild(1, 2, "test");
            Estate estate = estates.OpenEstate(ids, 1, "Decedent Test", kinship, 100, diag);
            var probate = new ProbateService();
            Will will = probate.RecordWill(ids, 1, "Decedent Test", 5, new List<int> { 3, 4 }, 90, population, diag);
            probate.AddBequest(will,
                new Bequest { Kind = BequestKind.CashAmount, BeneficiaryPersonId = 2, BeneficiaryName = "Child Test", CashCents = 100000 },
                population, diag);
            probate.AddBequest(will,
                new Bequest { Kind = BequestKind.CashAmount, BeneficiaryPersonId = 6, BeneficiaryName = "Stranger Test", CashCents = 100000 },
                population, diag);
            Assert.IsNull(probate.ProbateWill(will, estate, titles, 100, diag));
            Assert.IsNotNull(admin.AcceptExecutorAppointment(ids, estate, will, estates, 5, "Executor Test", population, 101, diag));
            Assert.IsNotNull(admin.OpenEstateAccount(estate, 5, 102, diag));
            Assert.IsNull(admin.RecordEstateReceipt(estate, 5, 100000, "cash on hand", 103, diag));

            Assert.IsNull(admin.AssessAndRecordAbatement(estate, will, 5, 104, diag));
            List<EstateAbatementRecord> records = admin.AbatementsFor(estate.EstateId);
            Assert.AreEqual(3, records.Count, "Residue eliminated + two general bequests abated.");
            Assert.IsTrue(records.Exists(r => r.AbatementClass == EstateAbatementClass.Residuary && r.BequestIndex == -1),
                "The residue abates first.");
            int totalReduced = 0;
            int generalCount = 0;
            foreach (EstateAbatementRecord record in records)
            {
                if (record.AbatementClass == EstateAbatementClass.General)
                {
                    Assert.AreEqual(50000, record.ReducedCents);
                    totalReduced += record.ReducedCents;
                    generalCount++;
                }
            }
            Assert.AreEqual(2, generalCount);
            Assert.AreEqual(100000, totalReduced, "Pro rata reductions cover exactly the shortfall.");
        }

        [Test]
        public void Abatement_RefusedWhileDebtsUnsettled()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            PromissoryNote note = credit.IssuePromissoryNote(ids, "Decedent Test", "Creditor Test", 50000, "6 months", 60, null, diag);
            string noteId = note.InstrumentId.ToString();
            estates.RecordEstateDebt(estate, noteId, diag);
            admin.RecordCreditorClaim(estate, noteId, EstateClaimRank.UnsecuredCreditor, 50000, note, 5, 102, diag);
            Assert.IsNotNull(admin.AssessAndRecordAbatement(estate, will, 5, 104, diag),
                "Debts before bequests — abatement waits for creditors.");
        }

        [Test]
        public void Compensation_UnsetScheduleComputesNothing()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            Assert.AreEqual(-1, admin.ComputeCompensationCents(estate.EstateId, 100000, diag),
                "No invented default — the fee is unset until the caller files a schedule.");
        }

        [Test]
        public void Compensation_ScheduleComputesWithClamps()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            var schedule = new ExecutorCompensationSchedule
            {
                BasisPoints = 250, MinimumCents = 1000, MaximumCents = 5000, Note = "test schedule",
            };
            Assert.IsNull(admin.SetCompensationSchedule(estate, schedule, 5, diag));
            Assert.AreEqual(2500, admin.ComputeCompensationCents(estate.EstateId, 100000, diag), "250 bps of 100000c.");
            Assert.AreEqual(1000, admin.ComputeCompensationCents(estate.EstateId, 10000, diag), "250c floors to the 1000c minimum.");
            Assert.AreEqual(5000, admin.ComputeCompensationCents(estate.EstateId, 10000000, diag), "250000c caps at the 5000c maximum.");

            EstateCreditorClaim claim = admin.RecordCompensationClaim(estate, estates, 100000, 5, 105, diag);
            Assert.IsNotNull(claim);
            Assert.AreEqual(EstateClaimRank.AdministrationCosts, claim.Rank, "Compensation is first in the hard order.");
            Assert.AreEqual(2500, claim.AmountCents);
        }

        [Test]
        public void ActsRecorded_NotJudged()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            Assert.IsNull(admin.RecordExecutorAct(estate, 5, "sold the decedent's tools for 50000c", 110, diag));
            Assert.IsNotNull(admin.RecordExecutorAct(estate, 6, "sold the decedent's tools", 110, diag),
                "P6 holds no appointment — the act is refused, but the refusal is the record, not a fraud finding.");
            List<EstateExecutorAct> acts = admin.ActsFor(estate.EstateId);
            Assert.AreEqual(1, acts.Count);
            Assert.AreEqual(5, acts[0].ActorPersonId);
        }

        [Test]
        public void SaveLoad_RoundTrip()
        {
            SetUpTestateEstate(out ProbateService probate, out Estate estate, out Will will);
            PromissoryNote note = credit.IssuePromissoryNote(ids, "Decedent Test", "Creditor Test", 50000, "6 months", 60, null, diag);
            string noteId = note.InstrumentId.ToString();
            estates.RecordEstateDebt(estate, noteId, diag);
            admin.RecordCreditorClaim(estate, noteId, EstateClaimRank.UnsecuredCreditor, 50000, note, 5, 102, diag);
            Assert.IsNotNull(admin.OpenEstateAccount(estate, 5, 102, diag));
            Assert.IsNull(admin.RecordEstateReceipt(estate, 5, 75000, "cash on hand", 103, diag));
            Assert.IsNull(admin.MarshalParcel(estate, 5, "p1", "40 acres", 200000, 104, diag));

            EstateAdministrationService.EstateAdministrationSaveDto dto = admin.CaptureSaveDto();
            var restored = new EstateAdministrationService();
            restored.LoadFromSaveDto(dto);

            Assert.IsNotNull(restored.FindAppointment(estate.EstateId, 5));
            Assert.AreEqual(1, restored.ClaimsFor(estate.EstateId).Count);
            Assert.AreEqual(75000, restored.AccountFor(estate.EstateId).BalanceCents);
            Assert.AreEqual(1, restored.InventoryFor(estate.EstateId).Count);
            Assert.AreEqual("p1", restored.InventoryFor(estate.EstateId)[0].ReferenceId);
        }
    }
}
