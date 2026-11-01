using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy;
using LandLedgers.Economy.Estates;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Time;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Pass VIII Worker 7: provider loss actually changes surviving household
    /// decisions and employment choices. The Pass VII estate system is
    /// preserved (death still ends employment, opens the estate once, and
    /// invents no replacement earnings); this suite proves the NEXT link —
    /// the surviving household behaves differently because the provider is
    /// gone, and a disabled provider loses real work capacity with real wage
    /// consequences.
    /// - Death: the household's decision changes (NoAction → SeekIncome) once
    ///   the provider's wages stop; no automatic replacement earnings appear;
    ///   the estate opens exactly once; the survivor can pursue a real opening.
    /// - Disability: a serious injury cuts usable work minutes through the
    ///   health-aware capacity factor; a full shift can no longer be committed;
    ///   attendance-gated payroll pays no wage for unworked weeks — the teeth
    ///   that make disability an economic event, not a label.
    /// </summary>
    [TestFixture]
    public sealed class Pass8ProviderLossTests
    {
        private sealed class World
        {
            public int Day = 100;
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public PopulationState Population = new PopulationState();
            public KinshipRegistry Kinship = new KinshipRegistry();
            public HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public HouseholdLifecycleManager Lifecycle;
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public EstateService Estates = new EstateService();
            public ContactGraphService Contacts = new ContactGraphService();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public HousingSearchNeedRegistry HousingSearchNeeds = new HousingSearchNeedRegistry();
            public HouseholdDecisionService Decisions = new HouseholdDecisionService();
            public HouseholdDecisionDriver DecisionDriver;
            public HouseholdAutonomyDriver Autonomy = new HouseholdAutonomyDriver();
            public RecruitmentService Recruitment = new RecruitmentService();
            public OpportunityPursuitService Pursuit = new OpportunityPursuitService();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public List<string> Diag = new List<string>();

            public PersonState Provider;
            public PersonState Spouse;
            public PersonState Contact;
            public const int HouseholdId = 1;
            public const int ContactHouseholdId = 2;

            public World()
            {
                Lifecycle = new HouseholdLifecycleManager(Population, Memberships, Kinship, Ids);
                DecisionDriver = new HouseholdDecisionDriver(
                    Contacts, Employments, HousingSearchNeeds, Obligations, Ids);
                // The provider: employed adult. The spouse: unemployed adult.
                // The contact lives in ANOTHER household and works at the mill
                // (a legitimate opening source, not a household earner).
                Provider = AddPerson(1, "John", "Doe", HouseholdId,
                    AgeBand.Adult18Plus, LaborAccessLevel.FullLaborMarket);
                Spouse = AddPerson(2, "Mary", "Doe", HouseholdId,
                    AgeBand.Adult18Plus, LaborAccessLevel.FullLaborMarket);
                Contact = AddPerson(3, "Piotr", "Baker", ContactHouseholdId,
                    AgeBand.Adult18Plus, LaborAccessLevel.FullLaborMarket);
                Kinship.AddSpouses(Provider.id, Spouse.id, "test");
                // The spouse knows the contact: a documented search channel.
                Assert.IsNull(Contacts.AddLink(Spouse.id, Contact.id, ContactLinkKind.FormerCoworker, "test"));
                Employ(Provider.id, "biz-mill", 1000);
                Employ(Contact.id, "biz-mill", 700);
                // Modest reserves; rent due each evaluation.
                HouseholdLedger ledger = Ledgers.GetOrCreate(HouseholdId);
                Assert.IsNull(ledger.RecordInflow(Day, 2000, HouseholdIncomeSource.OtherDocumented,
                    "test-funding", "test", "test"));
            }

            private int nextId = 1;
            private int nextMembershipId = 1;
            private PersonState AddPerson(int id, string first, string last, int householdId,
                AgeBand band, LaborAccessLevel access)
            {
                var person = new PersonState
                {
                    id = id,
                    firstName = first,
                    lastName = last,
                    ageBand = band,
                    laborAccessLevel = access,
                    deathDayIndex = -1,
                    householdId = householdId,
                };
                Population.people.Add(person);
                Assert.IsNull(Memberships.Register(new HouseholdMembership
                {
                    MembershipId = nextMembershipId++,
                    PersonId = id,
                    HouseholdId = householdId,
                    Lifecycle = HouseholdMembershipLifecycle.Active,
                    StartDayIndex = 0,
                    EndDayIndex = -1,
                    Source = HouseholdMembershipSource.Authored,
                }));
                return person;
            }

            private void Employ(int personId, string businessId, int weeklyWageCents)
            {
                Assert.IsNull(Employments.Register(new EmploymentRelationship
                {
                    Id = $"emp-{personId}",
                    EmployeePersonId = personId,
                    EmployerBusinessId = businessId,
                    RoleDisplayName = "Hand",
                    Kind = EmploymentKind.Permanent,
                    LifecycleState = EmploymentLifecycleState.Active,
                    Compensation = CompensationTerms.FromWeeklyWage(weeklyWageCents, "test"),
                    StartDayIndex = 0,
                    Source = EmploymentSource.Authored,
                }));
            }

            public HouseholdAutonomyDriver.AutonomyContext Ctx()
            {
                return new HouseholdAutonomyDriver.AutonomyContext
                {
                    Ledgers = Ledgers,
                    Memberships = Memberships,
                    Employments = Employments,
                    ContactGraph = Contacts,
                    Decisions = Decisions,
                    DecisionDriver = DecisionDriver,
                    HousingSearchNeeds = HousingSearchNeeds,
                    FinancialObligations = Obligations,
                    Ids = Ids,
                    RentObligationsFor = householdId => new List<RentObligationState>
                    {
                        new RentObligationState
                        {
                            AgreementId = "ag-1",
                            AmountDueCents = 3000,
                            PaidCents = 0,
                            Status = RentObligationStatus.Current,
                        },
                    },
                };
            }

            public DemographicDriverContext DemoCtx()
            {
                return new DemographicDriverContext
                {
                    Population = Population,
                    Kinship = Kinship,
                    Lifecycle = Lifecycle,
                    Memberships = Memberships,
                    Estates = Estates,
                    Ids = Ids,
                    Ledgers = Ledgers,
                    Employments = Employments,
                    Anchor = CalendarAnchor.Default,
                    CurrentDayIndex = Day,
                    WorldSeed = 7,
                };
            }
        }

        [Test]
        public void ProviderDeath_ChangesSurvivingHouseholdDecisions()
        {
            var w = new World();
            // Day 100: the provider's wages cover the rent with cushion.
            w.Autonomy.EvaluateHouseholdDay(World.HouseholdId, w.Day, w.Ctx(), w.Diag);
            HouseholdDecision before = w.Decisions.LastDecisionFor(World.HouseholdId.ToString());
            Assert.IsNotNull(before);
            Assert.AreEqual(HouseholdDecisionKind.NoAction, before.Kind,
                "with the provider's wages, the household rationally does nothing: " + before.Reason);

            // The provider dies: employment ends, the estate opens once, and
            // nothing invents replacement earnings.
            int balanceBefore = w.Ledgers.GetOrCreate(World.HouseholdId).GetBalanceCents();
            MortalityDriver.RecordDeath(w.DemoCtx(), w.Provider, "test cause");
            Assert.AreEqual(0, w.Employments.GetActiveByEmployee(w.Provider.id).Count,
                "death ends the provider's employment");
            Assert.AreEqual(balanceBefore, w.Ledgers.GetOrCreate(World.HouseholdId).GetBalanceCents(),
                "death itself moves no money: no automatic replacement earnings");

            // Day 101: the same household, without the provider's wages,
            // decides differently.
            w.Day = 101;
            w.Autonomy.EvaluateHouseholdDay(World.HouseholdId, w.Day, w.Ctx(), w.Diag);
            HouseholdDecision after = w.Decisions.LastDecisionFor(World.HouseholdId.ToString());
            Assert.IsNotNull(after);
            Assert.AreNotEqual(HouseholdDecisionKind.NoAction, after.Kind,
                "without the provider's income the household must act: " + after.Reason);
            Assert.AreEqual(HouseholdDecisionKind.SeekIncome, after.Kind,
                "with a documented contact to search, the household seeks income: " + after.Reason);
        }

        [Test]
        public void ProviderDeath_SurvivorPursuesARealOpening()
        {
            var w = new World();
            MortalityDriver.RecordDeath(w.DemoCtx(), w.Provider, "test cause");

            // The survivor's household seeks income (discovers the mill opening
            // through the documented contact) and pursues it.
            w.Day = 101;
            w.Autonomy.EvaluateHouseholdDay(World.HouseholdId, w.Day, w.Ctx(), w.Diag);
            Assert.AreEqual(1, w.DecisionDriver.OpportunityLog.UnpursuedCount(World.HouseholdId),
                "SeekIncome discovered the mill opening through the contact");

            RecruitmentEffort effort = w.Recruitment.OpenEffort(
                "biz-mill", "mill hand", RecruitmentChannel.WordOfMouth, w.Day, 42, w.Diag);
            Assert.IsNotNull(effort);
            var pursuitCtx = new OpportunityPursuitService.PursuitContext
            {
                OpportunityLog = w.DecisionDriver.OpportunityLog,
                Memberships = w.Memberships,
                Population = w.Population,
                Employments = w.Employments,
                Recruitment = w.Recruitment,
            };
            int filed = w.Pursuit.PursueForHousehold(World.HouseholdId, w.Day, pursuitCtx, w.Diag);
            Assert.AreEqual(1, filed, "the surviving spouse applies to the real opening");
            foreach (CandidateInquiry inquiry in w.Recruitment.Inquiries)
            {
                Assert.AreEqual(w.Spouse.id, inquiry.PersonId,
                    "the survivor — not the deceased provider — is the applicant");
            }
        }

        [Test]
        public void ProviderDeath_JobLossNote_NamesTheDeceasedProvider()
        {
            var w = new World();
            w.Autonomy.EvaluateHouseholdDay(World.HouseholdId, w.Day, w.Ctx(), w.Diag);
            // The opening is known before the death (discovered earlier).
            Assert.IsTrue(w.DecisionDriver.OpportunityLog.RecordIfNew(
                World.HouseholdId, "biz-mill", "hand", "P2 knows P3 (former coworker)", w.Day));

            MortalityDriver.RecordDeath(w.DemoCtx(), w.Provider, "test cause");
            w.Day = 101;
            var diag = new List<string>();
            w.Autonomy.EvaluateHouseholdDay(World.HouseholdId, w.Day, w.Ctx(), diag);

            HouseholdDecision after = w.Decisions.LastDecisionFor(World.HouseholdId.ToString());
            Assert.IsNotNull(after);
            Assert.AreEqual(HouseholdDecisionKind.SeekIncome, after.Kind);
            StringAssert.Contains("job loss", after.Reason, "got: " + after.Reason);
            StringAssert.Contains("P1 (departed) employment ended", after.Reason,
                "the loss is attributed to the deceased provider, never to the survivor: " + after.Reason);
            Assert.IsFalse(after.Reason.Contains("P2 employment ended"),
                "the survivor must not be misreported as the job loser: " + after.Reason);
            Assert.IsTrue(diag.Exists(d => d.Contains("provider loss")),
                "diagnostics name the provider loss");
        }

        [Test]
        public void ProviderDeath_OpensTheEstateExactlyOnce()
        {
            var w = new World();
            MortalityDriver.RecordDeath(w.DemoCtx(), w.Provider, "test cause");
            MortalityDriver.RecordDeath(w.DemoCtx(), w.Provider, "test cause"); // double-drive
            int estates = 0;
            foreach (Estate e in w.Estates.AllEstates())
            {
                if (e != null && e.DecedentPersonId == w.Provider.id) estates++;
            }
            Assert.AreEqual(1, estates, "the estate opens exactly once (Pass VII preserved)");
        }

        [Test]
        public void ProviderDisability_CutsWorkCapacity_WithWageConsequences()
        {
            var w = new World();
            EntityId worker = EntityId.For(EntityKind.Person, w.Provider.id);
            w.Budgets.EnsureDay(w.Day);

            // Healthy: full usable minutes.
            w.Budgets.GetOrCreate(worker).SetCapacityMultiplier(
                DailyNeedsService.WorkCapacityFactor(w.Provider));
            Assert.AreEqual(WorkTimeBudget.DefaultDailyWorkMinutes,
                w.Budgets.GetOrCreate(worker).EffectiveBudgetMinutes);

            // A serious injury: 15% labor capacity (PopulationHealthEvaluator).
            w.Provider.health.ApplyCondition(
                HealthConditionKind.Injury, HealthConditionSeverity.Serious, 30, "test injury");
            w.Budgets.GetOrCreate(worker).SetCapacityMultiplier(
                DailyNeedsService.WorkCapacityFactor(w.Provider));
            Assert.AreEqual(90, w.Budgets.GetOrCreate(worker).EffectiveBudgetMinutes,
                "disability bites as real lost minutes: 600 * 15%");

            // A full 480-minute shift can no longer be committed: the worker
            // cannot show up for a full day.
            string rejection;
            Assert.IsFalse(w.Budgets.TryCommitTask(worker, 480, out rejection));
            Assert.IsNotNull(rejection);

            // And with attendance-gated payroll, the unworked week earns no
            // wage — disability is an economic event, not a label.
            var business = new BusinessRuntimeState();
            business.BindBusinessIdentity("biz-mill");
            business.EmploymentRegistry = w.Employments;
            business.HouseholdLedgers = w.Ledgers;
            business.PersonHouseholdIdLookup = w.Memberships.GetActiveHouseholdId;
            business.SetCurrentCashCents(10000);
            business.PayrollDayIndex = 6;
            business.WeeklyAttendanceProvider = (employmentId, weekIndex) => false; // no minutes recorded
            int balanceBefore = w.Ledgers.GetOrCreate(World.HouseholdId).GetBalanceCents();
            business.ResolveWeeklyPayroll();
            Assert.AreEqual(balanceBefore, w.Ledgers.GetOrCreate(World.HouseholdId).GetBalanceCents(),
                "no work, no wage: the disabled provider's household feels the loss");
            Assert.AreEqual(10000, business.CurrentCashCents);
        }
    }
}
