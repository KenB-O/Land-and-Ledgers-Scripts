// Delegated vacancy hiring tests (Land & Ledgers campaign, Agent B, 2026-11-15).
// Roslyn-harness tests for DelegatedHiringService: no Unity, no scene.
// Wage figures here are test parameters only — the service itself reads the
// slot's authored baseline wage, and the tests assert it passes that wage
// through unchanged (never invents one).
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class DelegatedHiringServiceTests
    {
        private PopulationState population;
        private EmploymentRelationshipRegistry employment;
        private RecruitmentService recruiting;
        private DelegatedHiringService hiring;
        private List<string> diag;
        private List<DelegatedHireResult> hired;

        private static PersonState Person(int id, string first, string last, int householdId)
        {
            return new PersonState
            {
                id = id,
                firstName = first,
                lastName = last,
                age = 30,
                ageBand = AgeBand.Adult18Plus,
                laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = householdId,
            };
        }

        private static HiringSlotNeed ClerkSlot(int wageCents, bool required = true, bool filled = false, string business = "bakery-1")
        {
            return new HiringSlotNeed
            {
                BusinessInstanceId = business,
                SlotId = "clerk_helper",
                SlotDisplayName = "Store Clerk",
                WeeklyWageCents = wageCents,
                RequiredForOpening = required,
                IsFilled = filled,
            };
        }

        [SetUp]
        public void SetUp()
        {
            population = new PopulationState();
            population.people.Add(Person(1, "Glen", "Pierce", 1));
            population.people.Add(Person(2, "Shane", "Bryan", 2));
            population.people.Add(Person(3, "Tia", "Boniecki", 3));
            employment = new EmploymentRelationshipRegistry();
            recruiting = new RecruitmentService();
            hiring = new DelegatedHiringService();
            diag = new List<string>();
            hired = new List<DelegatedHireResult>();
        }

        private int DriveNpcOwner(IReadOnlyList<HiringSlotNeed> slots, int day = 10, int seed = 99)
        {
            return hiring.DriveBusinessDay(
                "bakery-1", isPlayerOwned: false, actingPersonId: 1,
                authorityBasis: HiringAuthorityBasis.NpcOwner,
                slots: slots, recruiting: recruiting, employments: employment,
                isHireableCandidate: id => IsHireable(id),
                resolvePersonDisplayName: id => population.GetPerson(id)?.DisplayName ?? string.Empty,
                onHired: r => hired.Add(r),
                dayIndex: day, effortSeed: seed, diag: diag);
        }

        private bool IsHireable(int personId)
        {
            PersonState person = population.GetPerson(personId);
            if (person == null || person.deathDayIndex >= 0) return false;
            if (person.ageBand != AgeBand.Adult18Plus) return false;
            if (person.laborAccessLevel == LaborAccessLevel.None) return false;
            return employment.GetActiveByEmployee(personId).Count == 0;
        }

        private CandidateInquiry OpenInquiryFor(string businessInstanceId, int personId, int day = 10)
        {
            RecruitmentEffort effort = recruiting.OpenEffort(
                businessInstanceId, "Store Clerk", RecruitmentChannel.WordOfMouth, day, 99, diag);
            Assert.IsNotNull(effort);
            CandidateInquiry inquiry = recruiting.ApplyForOpening(
                effort.EffortId, personId, population, employment, day, "word of mouth", diag);
            Assert.IsNotNull(inquiry, "Test setup: the candidate must be able to apply.");
            return inquiry;
        }

        [Test]
        public void PlayerOwnedWithoutDelegate_NeverHires_RecordsPendingOnce()
        {
            var slots = new List<HiringSlotNeed> { ClerkSlot(1200, business: "general-store") };

            int hires = hiring.DriveBusinessDay(
                "general-store", isPlayerOwned: true, actingPersonId: -1,
                authorityBasis: HiringAuthorityBasis.None,
                slots: slots, recruiting: recruiting, employments: employment,
                isHireableCandidate: id => true,
                resolvePersonDisplayName: id => string.Empty,
                onHired: r => hired.Add(r),
                dayIndex: 10, effortSeed: 99, diag: diag);

            Assert.AreEqual(0, hires);
            Assert.AreEqual(0, hired.Count);
            Assert.AreEqual(0, recruiting.Efforts.Count, "No effort may be posted for a player-owned business without a delegate.");
            Assert.AreEqual(0, employment.GetByEmployer("general-store").Count);
            Assert.IsTrue(diag.Count > 0, "The pending owner decision must be visible, not silent.");

            int diagBefore = diag.Count;
            hiring.DriveBusinessDay(
                "general-store", isPlayerOwned: true, actingPersonId: -1,
                authorityBasis: HiringAuthorityBasis.None,
                slots: slots, recruiting: recruiting, employments: employment,
                isHireableCandidate: id => true,
                resolvePersonDisplayName: id => string.Empty,
                onHired: r => hired.Add(r),
                dayIndex: 11, effortSeed: 99, diag: diag);
            Assert.AreEqual(diagBefore, diag.Count, "The pending notice must not spam every day.");
        }

        [Test]
        public void NpcOwner_VacantRequiredSlot_PostsWordOfMouthEffort()
        {
            var slots = new List<HiringSlotNeed> { ClerkSlot(1200) };

            int hires = DriveNpcOwner(slots);

            Assert.AreEqual(0, hires, "A freshly posted effort has no inquiries yet.");
            Assert.AreEqual(1, recruiting.Efforts.Count);
            foreach (RecruitmentEffort effort in recruiting.Efforts)
            {
                Assert.AreEqual(RecruitmentChannel.WordOfMouth, effort.Channel, "Only the free channel may be posted without an expense flow.");
                Assert.AreEqual("Store Clerk", effort.RoleDisplayName);
                Assert.AreEqual("bakery-1", effort.BusinessInstanceId);
            }
        }

        [Test]
        public void NpcOwner_SecondDayWithOpenEffort_PostsNoDuplicate()
        {
            var slots = new List<HiringSlotNeed> { ClerkSlot(1200) };

            DriveNpcOwner(slots, day: 10);
            int hires = DriveNpcOwner(slots, day: 11);

            Assert.AreEqual(0, hires);
            Assert.AreEqual(1, recruiting.Efforts.Count, "One open effort per business — no duplicate posting.");
        }

        [Test]
        public void NpcOwner_OpenInquiry_HiresAtSlotWageAsCasual()
        {
            CandidateInquiry inquiry = OpenInquiryFor("bakery-1", 2);
            var slots = new List<HiringSlotNeed> { ClerkSlot(1200) };

            int hires = DriveNpcOwner(slots);

            Assert.AreEqual(1, hires);
            Assert.AreEqual(1, hired.Count);
            DelegatedHireResult result = hired[0];
            Assert.AreEqual(2, result.PersonId);
            Assert.AreEqual("clerk_helper", result.SlotId);
            Assert.AreEqual(1200, result.WeeklyWageCents, "The hire must pay the slot's authored wage, unchanged.");
            Assert.AreEqual($"emp-{inquiry.InquiryId}", result.EmploymentId);
            Assert.AreEqual(HiringAuthorityBasis.NpcOwner, result.AuthorityBasis);

            Assert.AreEqual(InquiryStatus.Hired, inquiry.Status);
            Assert.IsTrue(employment.TryGetById(result.EmploymentId, out EmploymentRelationship rel));
            Assert.AreEqual(EmploymentLifecycleState.Active, rel.LifecycleState);
            Assert.AreEqual(EmploymentKind.SeasonalOrCasual, rel.Kind,
                "Delegated hires are routine/casual per Canon §6.1; permanent conversion is an owner decision.");
            Assert.AreEqual(1200, rel.Compensation.AgreedWeeklyWageCents);
            Assert.AreEqual("bakery-1", rel.EmployerBusinessId);
        }

        [Test]
        public void SlotWithNoAuthoredWage_DisabledByConfig()
        {
            var slots = new List<HiringSlotNeed> { ClerkSlot(0) };

            int hires = DriveNpcOwner(slots);

            Assert.AreEqual(0, hires);
            Assert.AreEqual(0, recruiting.Efforts.Count, "A wageless slot must not post an effort.");
            Assert.IsTrue(diag.Count > 0);
            bool namesQuestion = false;
            foreach (string line in diag)
                if (line.Contains("OWNER QUESTION") && line.Contains("wage")) namesQuestion = true;
            Assert.IsTrue(namesQuestion, "The blocker must name the precise owner question.");
        }

        [Test]
        public void CandidateNoLongerHireable_SkipsHireKeepsInquiryOpen()
        {
            CandidateInquiry inquiry = OpenInquiryFor("bakery-1", 2);
            var slots = new List<HiringSlotNeed> { ClerkSlot(1200) };

            // The candidate took another job between applying and the hire drive.
            var other = new EmploymentRelationship
            {
                Id = "emp-other",
                EmployeePersonId = 2,
                EmployerBusinessId = "fuel-dealer-1",
                RoleDisplayName = "Dealer",
                Kind = EmploymentKind.Permanent,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(1000, "test"),
                StartDayIndex = 9,
                Source = EmploymentSource.Manual,
            };
            Assert.IsNull(employment.Register(other));

            int hires = DriveNpcOwner(slots);

            Assert.AreEqual(0, hires);
            Assert.AreEqual(0, hired.Count);
            Assert.AreEqual(InquiryStatus.Open, inquiry.Status, "An unhired inquiry stays open.");
        }

        [Test]
        public void NonRequiredSlot_IsUntouched()
        {
            var slots = new List<HiringSlotNeed> { ClerkSlot(1200, required: false) };

            int hires = DriveNpcOwner(slots);

            Assert.AreEqual(0, hires);
            Assert.AreEqual(0, recruiting.Efforts.Count, "Optional slots stay an owner/manager decision.");
        }

        [Test]
        public void TwoInquiriesOneSlot_OnlyOneHire()
        {
            CandidateInquiry first = OpenInquiryFor("bakery-1", 2, day: 10);
            CandidateInquiry second = OpenInquiryFor("bakery-1", 3, day: 11);
            var slots = new List<HiringSlotNeed> { ClerkSlot(1200) };

            int hires = DriveNpcOwner(slots, day: 12);

            Assert.AreEqual(1, hires);
            Assert.AreEqual(1, hired.Count);
            Assert.AreEqual(2, hired[0].PersonId, "Earliest inquiry hires first.");
            Assert.AreEqual(InquiryStatus.Hired, first.Status);
            Assert.AreEqual(InquiryStatus.Open, second.Status, "The second inquiry stays open for the next vacancy.");
        }

        [Test]
        public void StaffingDelegateMayHireForPlayerOwnedBusiness()
        {
            CandidateInquiry inquiry = OpenInquiryFor("general-store", 2);
            var slots = new List<HiringSlotNeed> { ClerkSlot(1200, business: "general-store") };

            int hires = hiring.DriveBusinessDay(
                "general-store", isPlayerOwned: true, actingPersonId: 7,
                authorityBasis: HiringAuthorityBasis.StaffingDelegate,
                slots: slots, recruiting: recruiting, employments: employment,
                isHireableCandidate: id => IsHireable(id),
                resolvePersonDisplayName: id => population.GetPerson(id)?.DisplayName ?? string.Empty,
                onHired: r => hired.Add(r),
                dayIndex: 10, effortSeed: 99, diag: diag);

            Assert.AreEqual(1, hires, "A Staffing delegate may hire for a player-owned business — that is what the delegation is for.");
            Assert.AreEqual(2, hired[0].PersonId);
            Assert.AreEqual(HiringAuthorityBasis.StaffingDelegate, hired[0].AuthorityBasis);
            Assert.AreEqual(InquiryStatus.Hired, inquiry.Status);
        }
    }
}
