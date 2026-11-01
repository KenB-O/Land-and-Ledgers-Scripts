using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Population;
using LandLedgers.World.Property;
using LandLedgers.Economy.Businesses.Lawyer;
using NUnit.Framework;

namespace LandLedgers.Economy.Estates.Tests
{
    /// <summary>
    /// D4C: will contest mechanics. Canon §17.7-adjacent probate detail is a
    /// research hold (Tech X §10.1), so grounds, standing, and the proceeding
    /// follow 1870s American practice (Banks v Goodfellow 1870 capacity;
    /// undue-influence doctrine in the American treatises; heirs-at-law
    /// standing; later wills revoking earlier ones), while exact fees stay
    /// parameterized. A contest is a claim resolved through process —
    /// never auto-invalidating, never decided by fiat.
    /// </summary>
    public sealed class WillContestTests
    {
        private List<string> diag;
        private EntityIdRegistry ids;
        private PopulationState population;
        private TitleAuthority titles;
        private KinshipRegistry kinship;

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
            population.people.Add(Person(1, "Testator"));
            population.people.Add(Person(2, "Child"));
            population.people.Add(Person(3, "Witness"));
            population.people.Add(Person(4, "Witness2"));
            population.people.Add(Person(5, "Executor"));
            population.people.Add(Person(6, "Stranger"));
            population.people.Add(Person(7, "Creditor"));
            titles = new TitleAuthority();
            kinship = new KinshipRegistry();
            kinship.AddParentChild(1, 2, "test"); // P2 is the testator's child
        }

        /// <summary>
        /// Builds a probated will (one parcel bequest p-1 to P2) on a fresh
        /// estate with p-1 inventoried. Returns the will and estate.
        /// </summary>
        private void SetUpProbatedEstate(
            out ProbateService probate, out EstateService estates,
            out Estate estate, out Will will)
        {
            probate = new ProbateService();
            estates = new EstateService();
            will = probate.RecordWill(ids, 1, "Testator", 5,
                new List<int> { 3, 4 }, 10, population, diag);
            Assert.IsNotNull(will);
            Assert.IsNull(probate.AddBequest(will, new Bequest
            {
                Kind = BequestKind.Parcel, BeneficiaryPersonId = 2,
                BeneficiaryName = "Child", ParcelId = "p-1",
            }, population, diag));
            titles.RegisterParcel("p-1", "lot", 1f, "Testator", TitleBasis.HomesteadClaim, 0, diag);
            estate = estates.OpenEstate(ids, 1, "Testator", kinship, 20, diag);
            estates.RegisterDecedentParcel(estate, "p-1", titles, diag);
            Assert.IsNull(probate.ProbateWill(will, estate, titles, 20, diag));
        }

        private WillContest FileHeirContest(
            WillContestService contests, ProbateService probate,
            Will will, Estate estate, int day = 21)
        {
            return contests.FileContest(ids, probate, will, estate,
                2, "Child", WillContestStanding.HeirAtLaw, null,
                new List<WillContestGroundClaim>
                {
                    new WillContestGroundClaim
                    {
                        Ground = WillContestGround.UndueInfluence,
                        ClaimStatement = "The executor isolated the testator in the final month.",
                    },
                },
                kinship, population, day, diag);
        }

        [Test]
        public void FilingPausesProbateDistribution()
        {
            var contests = new WillContestService();
            SetUpProbatedEstate(out ProbateService probate, out EstateService estates,
                out Estate estate, out Will will);

            WillContest contest = FileHeirContest(contests, probate, will, estate);
            Assert.IsNotNull(contest, "The child is an heir at law with a stated ground — filing succeeds.");
            Assert.AreEqual(WillContestStatus.Open, contest.Status);
            Assert.AreEqual(1, contest.GroundClaims.Count);
            Assert.IsTrue(will.Contested, "Filing pauses probate through the NX-3C API.");

            string refused = probate.DistributeBequest(will, estate, will.Bequests[0], ids, titles, 22, diag);
            Assert.IsNotNull(refused, "Contested wills pause — never resolved by fiat.");
            Assert.AreEqual("Testator", titles.CurrentHolder("p-1"), "Nothing moved while the contest is open.");
        }

        [Test]
        public void StrangerHasNoStanding()
        {
            var contests = new WillContestService();
            SetUpProbatedEstate(out ProbateService probate, out EstateService estates,
                out Estate estate, out Will will);

            WillContest heirFiling = contests.FileContest(ids, probate, will, estate,
                6, "Stranger", WillContestStanding.HeirAtLaw, null,
                new List<WillContestGroundClaim>
                {
                    new WillContestGroundClaim { Ground = WillContestGround.FraudOrForgery, ClaimStatement = "Suspicion only." },
                },
                kinship, population, 21, diag);
            Assert.IsNull(heirFiling, "A stranger is no heir at law — no standing, no filing.");
            Assert.IsFalse(will.Contested, "A refused filing pauses nothing.");

            WillContest beneficiaryFiling = contests.FileContest(ids, probate, will, estate,
                6, "Stranger", WillContestStanding.NamedBeneficiary, null,
                new List<WillContestGroundClaim>
                {
                    new WillContestGroundClaim { Ground = WillContestGround.FraudOrForgery, ClaimStatement = "Suspicion only." },
                },
                kinship, population, 21, diag);
            Assert.IsNull(beneficiaryFiling, "A stranger takes nothing under the will — no standing.");
        }

        [Test]
        public void NamedBeneficiaryHasStanding()
        {
            var contests = new WillContestService();
            SetUpProbatedEstate(out ProbateService probate, out EstateService estates,
                out Estate estate, out Will will);

            WillContest contest = contests.FileContest(ids, probate, will, estate,
                2, "Child", WillContestStanding.NamedBeneficiary, null,
                new List<WillContestGroundClaim>
                {
                    new WillContestGroundClaim { Ground = WillContestGround.ImproperExecution, ClaimStatement = "Only one credible witness attested." },
                },
                kinship, population, 21, diag);
            Assert.IsNotNull(contest, "P2 takes under the will — standing as named beneficiary.");
        }

        [Test]
        public void CreditorHasStandingOnlyWithRecordedDebt()
        {
            var contests = new WillContestService();
            SetUpProbatedEstate(out ProbateService probate, out EstateService estates,
                out Estate estate, out Will will);
            Assert.IsNull(estates.RecordEstateDebt(estate, "debt-9", diag));

            WillContest contest = contests.FileContest(ids, probate, will, estate,
                7, "Creditor", WillContestStanding.EstateCreditor, "debt-9",
                new List<WillContestGroundClaim>
                {
                    new WillContestGroundClaim { Ground = WillContestGround.UndueInfluence, ClaimStatement = "The will impairs the recorded debt." },
                },
                kinship, population, 21, diag);
            Assert.IsNotNull(contest, "A recorded estate debt gives the creditor standing.");
            Assert.IsTrue(contest.StandingDetail.Contains("debt-9"));
            Assert.IsNull(contests.WithdrawContest(contest, probate, 22, diag));

            WillContest phantom = contests.FileContest(ids, probate, will, estate,
                7, "Creditor", WillContestStanding.EstateCreditor, "debt-bogus",
                new List<WillContestGroundClaim>
                {
                    new WillContestGroundClaim { Ground = WillContestGround.UndueInfluence, ClaimStatement = "The will impairs the recorded debt." },
                },
                kinship, population, 23, diag);
            Assert.IsNull(phantom, "No recorded debt, no creditor standing — claims are never invented.");
        }

        [Test]
        public void GroundsAreRecordedClaimsNotProof()
        {
            var contests = new WillContestService();
            SetUpProbatedEstate(out ProbateService probate, out EstateService estates,
                out Estate estate, out Will will);
            // A later will, recorded and real.
            Will laterWill = probate.RecordWill(ids, 1, "Testator", 5,
                new List<int> { 3, 4 }, 30, population, diag);
            Assert.IsNotNull(laterWill);

            WillContest contest = contests.FileContest(ids, probate, will, estate,
                2, "Child", WillContestStanding.HeirAtLaw, null,
                new List<WillContestGroundClaim>
                {
                    new WillContestGroundClaim { Ground = WillContestGround.ImproperExecution, ClaimStatement = "Only one credible witness attested." },
                    new WillContestGroundClaim { Ground = WillContestGround.LaterWillDiscovered, ClaimStatement = "A later will was found in the desk.", LaterWillId = laterWill.WillId },
                },
                kinship, population, 21, diag);
            Assert.IsNotNull(contest);
            Assert.AreEqual(2, contest.GroundClaims.Count);
            Assert.IsFalse(will.InvalidatedByContest, "Recording grounds proves nothing — the will stands until the proceeding rules.");
            Assert.IsFalse(will.Bequests[0].InvalidatedByContest);
            Assert.AreEqual("Testator", titles.CurrentHolder("p-1"), "Filing moves nothing.");
        }

        [Test]
        public void LaterWillGroundRequiresRealWill()
        {
            var contests = new WillContestService();
            SetUpProbatedEstate(out ProbateService probate, out EstateService estates,
                out Estate estate, out Will will);

            WillContest contest = contests.FileContest(ids, probate, will, estate,
                2, "Child", WillContestStanding.HeirAtLaw, null,
                new List<WillContestGroundClaim>
                {
                    new WillContestGroundClaim { Ground = WillContestGround.LaterWillDiscovered, ClaimStatement = "A later will exists, somewhere.", LaterWillId = "no-such-will" },
                },
                kinship, population, 21, diag);
            Assert.IsNull(contest, "A phantom later will cannot ground a contest — no invented instruments.");
            Assert.IsFalse(will.Contested);
        }

        [Test]
        public void ScheduleHearingCannotPredateFiling()
        {
            var contests = new WillContestService();
            SetUpProbatedEstate(out ProbateService probate, out EstateService estates,
                out Estate estate, out Will will);
            WillContest contest = FileHeirContest(contests, probate, will, estate, 21);
            Assert.IsNotNull(contest);

            Assert.IsNotNull(contests.ScheduleHearing(contest, 20, 21, diag),
                "Canon §2.3: proceedings consume world time — the hearing cannot predate the filing.");
            Assert.AreEqual(WillContestStatus.Open, contest.Status);
            Assert.AreEqual(-1, contest.HearingDayIndex);
        }

        [Test]
        public void AdjudicationRequiresHearing()
        {
            var contests = new WillContestService();
            SetUpProbatedEstate(out ProbateService probate, out EstateService estates,
                out Estate estate, out Will will);
            WillContest contest = FileHeirContest(contests, probate, will, estate);
            Assert.IsNotNull(contest);

            string refused = contests.AdjudicateContest(contest, probate, will, estate,
                WillContestOutcome.WillUpheld, null, titles,
                new WillContestCostSchedule(), ContestCostAllocation.EachBearsOwn,
                25, population, diag);
            Assert.IsNotNull(refused, "No hearing, no ruling — the proceeding must run.");
            Assert.AreEqual(WillContestStatus.Open, contest.Status);
            Assert.IsTrue(will.Contested, "The pause holds until the proceeding ends.");
        }

        [Test]
        public void UpheldWillResumesProbateWithLoserPaysCosts()
        {
            var contests = new WillContestService();
            SetUpProbatedEstate(out ProbateService probate, out EstateService estates,
                out Estate estate, out Will will);
            WillContest contest = FileHeirContest(contests, probate, will, estate);
            Assert.IsNotNull(contest);
            Assert.IsNull(contests.ScheduleHearing(contest, 30, 25, diag));

            Assert.IsNull(contests.AdjudicateContest(contest, probate, will, estate,
                WillContestOutcome.WillUpheld, null, titles,
                new WillContestCostSchedule { FilingFeeCents = 200, HearingCostCents = 500 },
                ContestCostAllocation.LoserPays, 31, population, diag));
            Assert.AreEqual(WillContestStatus.Adjudicated, contest.Status);
            Assert.AreEqual(WillContestOutcome.WillUpheld, contest.Outcome);
            Assert.AreEqual(31, contest.ResolvedDayIndex);
            Assert.IsFalse(will.Contested, "The pause lifts when the proceeding ends.");
            Assert.AreEqual(700, contest.AssessedCostsCents, "200 filing + 500 hearing, assessed — recorded, not moved.");
            Assert.AreEqual(2, contest.AssessedAgainstPersonId, "The losing contestant bears the costs.");
            Assert.IsFalse(contest.AssessedAgainstEstate);

            Assert.IsNull(probate.DistributeBequest(will, estate, will.Bequests[0], ids, titles, 32, diag));
            Assert.AreEqual("Child", titles.CurrentHolder("p-1"), "Probate resumes after the ruling.");
        }

        [Test]
        public void WithdrawalResumesProbate()
        {
            var contests = new WillContestService();
            SetUpProbatedEstate(out ProbateService probate, out EstateService estates,
                out Estate estate, out Will will);
            WillContest contest = FileHeirContest(contests, probate, will, estate);
            Assert.IsNotNull(contest);

            Assert.IsNull(contests.WithdrawContest(contest, probate, 25, diag));
            Assert.AreEqual(WillContestStatus.Withdrawn, contest.Status);
            Assert.AreEqual(WillContestOutcome.WillUpheld, contest.Outcome);
            Assert.IsFalse(will.Contested, "Withdrawal ends the challenge — the pause lifts.");

            Assert.IsNull(probate.DistributeBequest(will, estate, will.Bequests[0], ids, titles, 26, diag));
            Assert.AreEqual("Child", titles.CurrentHolder("p-1"));
        }

        [Test]
        public void SettlementVoidsBequestInPart()
        {
            var contests = new WillContestService();
            var probate = new ProbateService();
            var estates = new EstateService();
            Will will = probate.RecordWill(ids, 1, "Testator", 5,
                new List<int> { 3, 4 }, 10, population, diag);
            Assert.IsNull(probate.AddBequest(will, new Bequest
            {
                Kind = BequestKind.Parcel, BeneficiaryPersonId = 2,
                BeneficiaryName = "Child", ParcelId = "p-1",
            }, population, diag));
            Assert.IsNull(probate.AddBequest(will, new Bequest
            {
                Kind = BequestKind.Parcel, BeneficiaryPersonId = 6,
                BeneficiaryName = "Stranger", ParcelId = "p-2",
            }, population, diag));
            titles.RegisterParcel("p-1", "lot", 1f, "Testator", TitleBasis.HomesteadClaim, 0, diag);
            titles.RegisterParcel("p-2", "lot", 1f, "Testator", TitleBasis.HomesteadClaim, 0, diag);
            Estate estate = estates.OpenEstate(ids, 1, "Testator", kinship, 20, diag);
            estates.RegisterDecedentParcel(estate, "p-1", titles, diag);
            estates.RegisterDecedentParcel(estate, "p-2", titles, diag);
            Assert.IsNull(probate.ProbateWill(will, estate, titles, 20, diag));

            WillContest contest = FileHeirContest(contests, probate, will, estate);
            Assert.IsNotNull(contest);

            Assert.IsNull(contests.RecordSettlement(contest, probate, will,
                new List<ContestSettlementTerm>
                {
                    new ContestSettlementTerm { BequestIndex = 1, TermKind = ContestSettlementTermKind.Voided, Note = "p-2 returns to the estate by agreement." },
                },
                0, -1, -1, "Partial settlement.", population, 26, diag));
            Assert.AreEqual(WillContestStatus.Settled, contest.Status);
            Assert.AreEqual(WillContestOutcome.WillInvalidatedInPart, contest.Outcome);
            Assert.AreEqual(1, contest.VoidedBequestIndices.Count);
            Assert.IsTrue(will.Bequests[1].InvalidatedByContest);
            Assert.IsFalse(will.Bequests[0].InvalidatedByContest, "The unvoided bequest stands.");
            Assert.IsFalse(will.Contested, "Settlement lifts the pause.");

            Assert.IsNull(probate.DistributeBequest(will, estate, will.Bequests[0], ids, titles, 27, diag));
            Assert.AreEqual("Child", titles.CurrentHolder("p-1"));
            Assert.IsNotNull(probate.DistributeBequest(will, estate, will.Bequests[1], ids, titles, 27, diag),
                "A bequest voided by settlement distributes to no one.");
            Assert.AreEqual("Testator", titles.CurrentHolder("p-2"));
        }

        [Test]
        public void SettlementCashTermsAreRecordedNotMoved()
        {
            var contests = new WillContestService();
            SetUpProbatedEstate(out ProbateService probate, out EstateService estates,
                out Estate estate, out Will will);
            WillContest contest = FileHeirContest(contests, probate, will, estate);
            Assert.IsNotNull(contest);

            Assert.IsNull(contests.RecordSettlement(contest, probate, will,
                new List<ContestSettlementTerm>(),
                5000, 2, 3, "P2 pays P3 5000c to drop the claim.", population, 26, diag));
            Assert.AreEqual(WillContestStatus.Settled, contest.Status);
            Assert.AreEqual(WillContestOutcome.WillUpheld, contest.Outcome);
            Assert.AreEqual(5000, contest.SettlementCashCents);
            Assert.AreEqual(2, contest.SettlementPayerPersonId);
            Assert.AreEqual(3, contest.SettlementPayeePersonId);
            Assert.AreEqual("Testator", titles.CurrentHolder("p-1"),
                "Cash terms are the parties' agreement — this service moves no money.");
        }

        [Test]
        public void WholeInvalidationFallsBackToPriorWill()
        {
            var contests = new WillContestService();
            var probate = new ProbateService();
            var estates = new EstateService();
            // Prior will, signed day 5.
            Will priorWill = probate.RecordWill(ids, 1, "Testator", 5,
                new List<int> { 3, 4 }, 5, population, diag);
            Assert.IsNotNull(priorWill);
            // Contested will, signed day 10, probated for the estate.
            Will will = probate.RecordWill(ids, 1, "Testator", 5,
                new List<int> { 3, 4 }, 10, population, diag);
            Assert.IsNull(probate.AddBequest(will, new Bequest
            {
                Kind = BequestKind.Parcel, BeneficiaryPersonId = 2,
                BeneficiaryName = "Child", ParcelId = "p-1",
            }, population, diag));
            titles.RegisterParcel("p-1", "lot", 1f, "Testator", TitleBasis.HomesteadClaim, 0, diag);
            Estate estate = estates.OpenEstate(ids, 1, "Testator", kinship, 20, diag);
            estates.RegisterDecedentParcel(estate, "p-1", titles, diag);
            Assert.IsNull(probate.ProbateWill(will, estate, titles, 20, diag));
            Assert.AreEqual(will.WillId, estate.TestateWillId);

            WillContest contest = FileHeirContest(contests, probate, will, estate);
            Assert.IsNotNull(contest);
            Assert.IsNull(contests.ScheduleHearing(contest, 30, 25, diag));
            Assert.IsNull(contests.AdjudicateContest(contest, probate, will, estate,
                WillContestOutcome.WillInvalidatedWhole, null, titles,
                new WillContestCostSchedule(), ContestCostAllocation.EachBearsOwn,
                31, population, diag));

            Assert.AreEqual(WillContestStatus.Adjudicated, contest.Status);
            Assert.IsTrue(will.InvalidatedByContest);
            Assert.IsFalse(will.Contested);
            Assert.AreEqual(priorWill.WillId, estate.TestateWillId,
                "Whole invalidation falls back to the most recent prior valid will (NX-3C rule).");
            Assert.IsTrue(priorWill.Probated);
            Assert.AreEqual("Testator", titles.CurrentHolder("p-1"), "No distribution happened by fiat.");
        }

        [Test]
        public void WholeInvalidationFallsBackToIntestate()
        {
            var contests = new WillContestService();
            SetUpProbatedEstate(out ProbateService probate, out EstateService estates,
                out Estate estate, out Will will);
            WillContest contest = FileHeirContest(contests, probate, will, estate);
            Assert.IsNotNull(contest);
            Assert.IsNull(contests.ScheduleHearing(contest, 30, 25, diag));
            Assert.IsNull(contests.AdjudicateContest(contest, probate, will, estate,
                WillContestOutcome.WillInvalidatedWhole, null, titles,
                new WillContestCostSchedule(), ContestCostAllocation.EachBearsOwn,
                31, population, diag));

            Assert.IsTrue(will.InvalidatedByContest);
            Assert.AreEqual(string.Empty, estate.TestateWillId,
                "No prior will — the estate falls back to intestate under the NX-3C rules.");
            Assert.IsNotNull(probate.ProbateWill(will, estate, titles, 32, diag),
                "An invalidated will probates never again.");
            Assert.IsNotNull(probate.DistributeBequest(will, estate, will.Bequests[0], ids, titles, 32, diag),
                "An invalidated will distributes nothing.");

            // The intestate path: P2 is an heir from opening; the caller
            // distributes through EstateService.DistributeParcel.
            Assert.IsNull(estates.DistributeParcel(estate, "p-1", 2, "Child", ids, titles, 33, diag));
            Assert.AreEqual("Child", titles.CurrentHolder("p-1"));
        }

        [Test]
        public void FrivolousDismissalAssessesCostsAgainstContestant()
        {
            var contests = new WillContestService();
            SetUpProbatedEstate(out ProbateService probate, out EstateService estates,
                out Estate estate, out Will will);
            WillContest contest = FileHeirContest(contests, probate, will, estate);
            Assert.IsNotNull(contest);

            var schedule = new WillContestCostSchedule { FilingFeeCents = 200, HearingCostCents = 500 };
            Assert.IsNull(contests.DismissContest(contest, probate, true, schedule, 28, diag));
            Assert.AreEqual(WillContestStatus.Dismissed, contest.Status);
            Assert.AreEqual(WillContestOutcome.WillUpheld, contest.Outcome);
            Assert.IsTrue(contest.DismissedAsFrivolous);
            Assert.AreEqual(700, contest.AssessedCostsCents, "Frivolous claims cost the contestant — recorded, not moved.");
            Assert.AreEqual(2, contest.AssessedAgainstPersonId);
            Assert.IsFalse(will.Contested, "Dismissal lifts the pause; the will stands.");

            // A non-frivolous dismissal assesses nothing.
            WillContest second = FileHeirContest(contests, probate, will, estate, 29);
            Assert.IsNotNull(second, "A new contest may be filed after the first ends.");
            Assert.IsNull(contests.DismissContest(second, probate, false, schedule, 30, diag));
            Assert.AreEqual(0, second.AssessedCostsCents);
            Assert.IsFalse(second.DismissedAsFrivolous);
        }

        [Test]
        public void CounselLinksToPracticeMatterReadOnly()
        {
            var contests = new WillContestService();
            SetUpProbatedEstate(out ProbateService probate, out EstateService estates,
                out Estate estate, out Will will);
            WillContest contest = FileHeirContest(contests, probate, will, estate);
            Assert.IsNotNull(contest);

            var practice = new LawyerPracticeRuntime("biz-law");
            practice.SetOffice(new LawyerOfficeRequirements
            {
                HasDesk = true, HasLawBooks = true, HasWritingSupplies = true,
                HasDocumentForms = true, HasSeal = true, HasSecureRecords = true,
            });
            LegalMatter matter = practice.OpenMatter(ids, 2, "Child",
                LegalMatterKind.DisputeRepresentation, "will contest counsel",
                22, 1, "Testator", diag);
            Assert.IsNotNull(matter);

            Assert.IsNull(contests.RecordCounsel(contest, practice, matter.MatterId,
                WillContestCounselSide.Contestant, diag));
            Assert.AreEqual(matter.MatterId, contest.ContestantCounselMatterId);

            LegalMatter strangersMatter = practice.OpenMatter(ids, 6, "Stranger",
                LegalMatterKind.DisputeRepresentation, "not the contestant",
                22, 1, "Testator", diag);
            Assert.IsNotNull(strangersMatter);
            Assert.IsNotNull(contests.RecordCounsel(contest, practice, strangersMatter.MatterId,
                WillContestCounselSide.Contestant, diag),
                "Counsel records only for the right client — sides must match.");
            Assert.IsNotNull(contests.RecordCounsel(contest, practice, "no-such-matter",
                WillContestCounselSide.Contestant, diag),
                "Counsel is never invented.");
        }

        [Test]
        public void ContestSaveRoundTrip()
        {
            var contests = new WillContestService();
            SetUpProbatedEstate(out ProbateService probate, out EstateService estates,
                out Estate estate, out Will will);
            WillContest contest = FileHeirContest(contests, probate, will, estate);
            Assert.IsNotNull(contest);
            Assert.IsNull(contests.ScheduleHearing(contest, 30, 25, diag));

            var restored = new WillContestService();
            restored.LoadFromSaveDto(contests.CaptureSaveDto());
            WillContest fetched = restored.Get(contest.ContestId);
            Assert.IsNotNull(fetched);
            Assert.AreEqual(WillContestStatus.HearingSet, fetched.Status);
            Assert.AreEqual(1, fetched.GroundClaims.Count);
            Assert.AreEqual(WillContestGround.UndueInfluence, fetched.GroundClaims[0].Ground);
            Assert.AreEqual(WillContestStanding.HeirAtLaw, fetched.Standing);
        }
    }
}
