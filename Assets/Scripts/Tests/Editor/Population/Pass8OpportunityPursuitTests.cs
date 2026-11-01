using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Pass VIII Worker 7: closes the first missing connection in the
    /// person-decision cycle (household circumstances → person decisions →
    /// ACTUAL WORK). SeekIncome discoveries land in the HouseholdOpportunityLog;
    /// OpportunityPursuitService turns unpursued entries into real
    /// person-initiated job applications (RecruitmentService.ApplyForOpening).
    /// - An eligible unemployed member applies to a genuinely open effort.
    /// - No open effort → no application; the entry waits (nothing invented).
    /// - The dead, children, and the already-employed never apply.
    /// - An application is an inquiry, never an employment: hiring stays the
    ///   employer's decision (HireFromInquiry).
    /// - One attempt per opportunity (no daily re-application spam).
    /// </summary>
    [TestFixture]
    public sealed class Pass8OpportunityPursuitTests
    {
        private sealed class Fixture
        {
            public PopulationState Population = new PopulationState();
            public HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public HouseholdOpportunityLog OpportunityLog = new HouseholdOpportunityLog();
            public RecruitmentService Recruitment = new RecruitmentService();
            public OpportunityPursuitService Pursuit = new OpportunityPursuitService();
            public List<string> Diag = new List<string>();

            public int NextPersonId = 1;

            public int AddPerson(int householdId, AgeBand band, LaborAccessLevel access, bool dead = false)
            {
                int id = NextPersonId++;
                Population.people.Add(new PersonState
                {
                    id = id,
                    firstName = "Test",
                    lastName = "Person" + id,
                    ageBand = band,
                    laborAccessLevel = access,
                    deathDayIndex = dead ? 0 : -1,
                    householdId = householdId,
                });
                var membership = new HouseholdMembership
                {
                    MembershipId = id,
                    PersonId = id,
                    HouseholdId = householdId,
                    Lifecycle = HouseholdMembershipLifecycle.Active,
                    StartDayIndex = 0,
                    EndDayIndex = -1,
                    Source = HouseholdMembershipSource.Authored,
                };
                Assert.IsNull(Memberships.Register(membership));
                return id;
            }

            public void Employ(int personId, string businessId)
            {
                Assert.IsNull(Employments.Register(new EmploymentRelationship
                {
                    Id = $"emp-{personId}",
                    EmployeePersonId = personId,
                    EmployerBusinessId = businessId,
                    RoleDisplayName = "Hand",
                    Kind = EmploymentKind.Permanent,
                    LifecycleState = EmploymentLifecycleState.Active,
                    Compensation = CompensationTerms.FromWeeklyWage(500, "test"),
                    StartDayIndex = 0,
                    Source = EmploymentSource.Authored,
                }));
            }

            public RecruitmentEffort OpenEffort(string businessId, string role)
            {
                RecruitmentEffort effort = Recruitment.OpenEffort(
                    businessId, role, RecruitmentChannel.WordOfMouth, 0, 42, Diag);
                Assert.IsNotNull(effort, "effort should open: " + string.Join("; ", Diag));
                return effort;
            }

            public void Discover(int householdId, string businessId)
            {
                Assert.IsTrue(OpportunityLog.RecordIfNew(householdId, businessId, "hand",
                    "P1 knows P9 (former coworker)", 0));
            }

            public OpportunityPursuitService.PursuitContext Ctx()
            {
                return new OpportunityPursuitService.PursuitContext
                {
                    OpportunityLog = OpportunityLog,
                    Memberships = Memberships,
                    Population = Population,
                    Employments = Employments,
                    Recruitment = Recruitment,
                };
            }
        }

        [Test]
        public void EligibleMember_AppliesToOpenEffort()
        {
            var f = new Fixture();
            int applicant = f.AddPerson(10, AgeBand.Adult18Plus, LaborAccessLevel.FullLaborMarket);
            f.AddPerson(10, AgeBand.Child0To9, LaborAccessLevel.None); // child: never applies
            f.Discover(10, "biz-mill");
            RecruitmentEffort effort = f.OpenEffort("biz-mill", "mill hand");

            int filed = f.Pursuit.PursueForHousehold(10, 1, f.Ctx(), f.Diag);

            Assert.AreEqual(1, filed);
            Assert.AreEqual(0, f.OpportunityLog.UnpursuedCount(10), "the attempt marks the entry pursued");
            Assert.AreEqual(1, f.Recruitment.Inquiries.Count);
            foreach (CandidateInquiry inquiry in f.Recruitment.Inquiries)
            {
                Assert.AreEqual(applicant, inquiry.PersonId);
                Assert.AreEqual(effort.EffortId, inquiry.EffortId);
                Assert.AreEqual(InquiryStatus.Open, inquiry.Status);
            }
            Assert.AreEqual(0, f.Employments.GetActiveByEmployee(applicant).Count,
                "an application is not an employment");
        }

        [Test]
        public void NoOpenEffort_NoApplication_EntryWaits()
        {
            var f = new Fixture();
            f.AddPerson(10, AgeBand.Adult18Plus, LaborAccessLevel.FullLaborMarket);
            f.Discover(10, "biz-mill");
            // No effort opened at biz-mill.

            int filed = f.Pursuit.PursueForHousehold(10, 1, f.Ctx(), f.Diag);

            Assert.AreEqual(0, filed);
            Assert.AreEqual(0, f.Recruitment.Inquiries.Count, "nothing invented to apply to");
            Assert.AreEqual(1, f.OpportunityLog.UnpursuedCount(10), "the entry waits for a real opening");
        }

        [Test]
        public void DeadChildAndEmployedMembers_NeverApply()
        {
            var f = new Fixture();
            f.AddPerson(10, AgeBand.Adult18Plus, LaborAccessLevel.FullLaborMarket, dead: true);
            f.AddPerson(10, AgeBand.Child0To9, LaborAccessLevel.None);
            int employed = f.AddPerson(10, AgeBand.Adult18Plus, LaborAccessLevel.FullLaborMarket);
            f.Employ(employed, "biz-other");
            f.Discover(10, "biz-mill");
            f.OpenEffort("biz-mill", "mill hand");

            int filed = f.Pursuit.PursueForHousehold(10, 1, f.Ctx(), f.Diag);

            Assert.AreEqual(0, filed, "nobody eligible: the dead, children, and employed never apply");
            Assert.AreEqual(0, f.Recruitment.Inquiries.Count);
        }

        [Test]
        public void OneAttemptPerOpportunity_NoDailySpam()
        {
            var f = new Fixture();
            f.AddPerson(10, AgeBand.Adult18Plus, LaborAccessLevel.FullLaborMarket);
            f.Discover(10, "biz-mill");
            f.OpenEffort("biz-mill", "mill hand");

            Assert.AreEqual(1, f.Pursuit.PursueForHousehold(10, 1, f.Ctx(), f.Diag));
            Assert.AreEqual(0, f.Pursuit.PursueForHousehold(10, 2, f.Ctx(), f.Diag),
                "the same opportunity is not re-pursued daily");
            Assert.AreEqual(1, f.Recruitment.Inquiries.Count);
        }

        [Test]
        public void ApplyForOpening_HonorsEligibilityDirectly()
        {
            var f = new Fixture();
            int dead = f.AddPerson(10, AgeBand.Adult18Plus, LaborAccessLevel.FullLaborMarket, dead: true);
            RecruitmentEffort effort = f.OpenEffort("biz-mill", "mill hand");

            CandidateInquiry inquiry = f.Recruitment.ApplyForOpening(
                effort.EffortId, dead, f.Population, f.Employments, 1, "heard of it", f.Diag);
            Assert.IsNull(inquiry, "the dead cannot apply");
            Assert.AreEqual(0, f.Recruitment.Inquiries.Count);
        }

        [Test]
        public void Application_CanBecomeEmployment_ThroughHireFromInquiry()
        {
            var f = new Fixture();
            int applicant = f.AddPerson(10, AgeBand.Adult18Plus, LaborAccessLevel.FullLaborMarket);
            f.Discover(10, "biz-mill");
            RecruitmentEffort effort = f.OpenEffort("biz-mill", "mill hand");
            Assert.AreEqual(1, f.Pursuit.PursueForHousehold(10, 1, f.Ctx(), f.Diag));

            // The employer's decision: hire from the inquiry.
            string inquiryId = null;
            foreach (CandidateInquiry q in f.Recruitment.Inquiries) inquiryId = q.InquiryId;
            Assert.IsNotNull(inquiryId);
            string refusal = f.Recruitment.HireFromInquiry(
                inquiryId, 600, EmploymentKind.Permanent, 2, f.Employments, f.Diag);
            Assert.IsNull(refusal, "hire should succeed: " + string.Join("; ", f.Diag));
            Assert.AreEqual(1, f.Employments.GetActiveByEmployee(applicant).Count,
                "discovery → application → hire: the full person-decision chain");
        }
    }
}
