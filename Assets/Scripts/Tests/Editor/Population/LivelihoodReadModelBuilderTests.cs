using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Reporting;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// Worker C (Pass III): the livelihood read model derives every field from
    /// actual state — employment registry, schedule tracker, the household's own
    /// recorded decision reason. Observer visibility: own household and
    /// documented contacts see full detail; strangers see masked placeholders
    /// (Canon XXXI Part IV §4.4 — known information is evidence).
    /// </summary>
    [TestFixture]
    public sealed class LivelihoodReadModelBuilderTests
    {
        private sealed class Fixture
        {
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public PersonScheduleTracker Schedules = new PersonScheduleTracker();
            public ContactGraphService Contacts = new ContactGraphService();
            public HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public HouseholdDecisionService Decisions = new HouseholdDecisionService();
            public LivelihoodReadModelBuilder Builder;

            public Fixture()
            {
                Builder = new LivelihoodReadModelBuilder(Employments, Schedules, Contacts, Memberships, Decisions);
            }

            public void AddMember(int personId, int householdId)
            {
                Memberships.Register(new HouseholdMembership
                {
                    MembershipId = personId * 100 + householdId,
                    PersonId = personId,
                    HouseholdId = householdId,
                    Lifecycle = HouseholdMembershipLifecycle.Active,
                    StartDayIndex = 0,
                });
            }

            public void Employ(int personId, string employmentId, string businessId, string role, int wagePerWeek)
            {
                Employments.Register(new EmploymentRelationship
                {
                    Id = employmentId,
                    EmployeePersonId = personId,
                    EmployerBusinessId = businessId,
                    RoleDisplayName = role,
                    LifecycleState = EmploymentLifecycleState.Active,
                    Compensation = CompensationTerms.FromWeeklyWage(wagePerWeek, ""),
                });
            }
        }

        [Test]
        public void Build_AllFieldsTraceToActualState()
        {
            var f = new Fixture();
            f.AddMember(3, 7);
            f.AddMember(4, 7);
            f.Employ(3, "emp-3", "store-1", "clerk", 320);
            f.Schedules.TryReserve(3, PersonActivityKind.Work, 100, 360, 120, "store shift");
            // The household's own recorded SeekIncome decision supplies the reason.
            f.Decisions.Evaluate(new HouseholdPressureInput
            {
                HouseholdId = "7",
                Day = 99,
                ReservesCents = 400,
                ObligationsDueCents = 6000,
                ExpectedIncomeCentsPerDay = 100,
                Dependents = 2,
                KnownOpportunityCount = 1,
            });
            f.Builder.CapabilitySummaryProvider = pid => "Experienced at stocking; adequate bookkeeping.";

            // Observer P4: same household -> full visibility.
            LivelihoodReadModel model = f.Builder.Build(3, 100, 400, 4, null);

            Assert.AreEqual("P3", model.PersonId);
            Assert.AreEqual("7", model.HouseholdId);
            Assert.AreEqual(100, model.AsOfDay);
            Assert.IsTrue(model.CurrentWorkDescription.Contains("clerk"), model.CurrentWorkDescription);
            Assert.IsTrue(model.CurrentWorkDescription.Contains("store-1"), model.CurrentWorkDescription);
            Assert.AreEqual("Accepted, active; seeking more", model.EmploymentState);
            Assert.IsNotNull(model.SeekingReason);
            Assert.IsTrue(model.SeekingReason.Contains("SeekIncome"), model.SeekingReason);
            Assert.IsTrue(model.CurrentActivity.Contains("Work") || model.CurrentActivity.Length > 0, model.CurrentActivity);
            Assert.AreEqual("Experienced at stocking; adequate bookkeeping.", model.CapabilitySummary);
            Assert.IsTrue(model.ExpectedConsequence.Contains("320c/week"), model.ExpectedConsequence);
        }

        [Test]
        public void Build_StrangerObserver_PrivateFieldsMasked()
        {
            var f = new Fixture();
            f.AddMember(3, 7);
            f.AddMember(9, 11); // observer in another household, no contact link
            f.Employ(3, "emp-3", "store-1", "clerk", 320);

            LivelihoodReadModel model = f.Builder.Build(3, 100, 400, 9, null);

            Assert.AreEqual("P3", model.PersonId); // identity is public
            Assert.AreEqual("not known to observer", model.CurrentWorkDescription);
            Assert.AreEqual("not known to observer", model.EmploymentState);
            Assert.IsNull(model.SeekingReason);
            Assert.AreEqual("not known to observer", model.CurrentActivity);
            Assert.AreEqual("not known to observer", model.ExpectedConsequence);
        }

        [Test]
        public void Build_ContactLinkedObserver_SeesFullDetail()
        {
            var f = new Fixture();
            f.AddMember(3, 7);
            f.AddMember(9, 11);
            f.Employ(3, "emp-3", "store-1", "clerk", 320);
            // Documented link: the observer legitimately knows P3.
            f.Contacts.SeedFormerCoworkerLinks(9, "store-1", new[] { 3 });

            LivelihoodReadModel model = f.Builder.Build(3, 100, 400, 9, null);

            Assert.IsTrue(model.CurrentWorkDescription.Contains("clerk"), model.CurrentWorkDescription);
            Assert.AreEqual("Accepted, active", model.EmploymentState);
        }

        [Test]
        public void Build_UnemployedPerson_HonestEmptyState()
        {
            var f = new Fixture();
            f.AddMember(3, 7);

            LivelihoodReadModel model = f.Builder.Build(3, 100, 400, 3, null);

            Assert.AreEqual("no employment", model.CurrentWorkDescription);
            Assert.AreEqual("Not employed", model.EmploymentState);
            Assert.IsNull(model.SeekingReason);
            Assert.AreEqual("no expected employment income", model.ExpectedConsequence);
            Assert.AreEqual("capability summary not yet integrated", model.CapabilitySummary);
        }
    }
}
