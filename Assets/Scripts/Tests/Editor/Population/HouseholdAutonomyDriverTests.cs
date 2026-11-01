using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// Worker C (Pass III): NPC autonomous household responses (brief §13).
    /// Declining reserves + rent due -> income search through real contacts;
    /// adequate resources -> NoAction; job loss -> job search vs. relying on
    /// reserves; shelter risk -> negotiation/relocation. All inputs are the
    /// household's own records or legitimately-known facts — no omniscient
    /// planner. Save/load preserves job-loss fingerprints without re-triggering.
    /// </summary>
    [TestFixture]
    public sealed class HouseholdAutonomyDriverTests
    {
        private sealed class Fixture
        {
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public ContactGraphService Contacts = new ContactGraphService();
            public HouseholdDecisionService Decisions = new HouseholdDecisionService();
            public HousingSearchNeedRegistry HousingNeeds = new HousingSearchNeedRegistry();
            public FinancialObligationAuthority Finance = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public HouseholdDecisionDriver Driver;
            public HouseholdAutonomyDriver Autonomy = new HouseholdAutonomyDriver();
            public List<RentObligationState> RentStates = new List<RentObligationState>();
            public bool ShelterRisk;

            public Fixture()
            {
                Driver = new HouseholdDecisionDriver(Contacts, Employments, HousingNeeds, Finance, Ids);
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

            public void Fund(int householdId, int cents)
            {
                Ledgers.GetOrCreate(householdId).RecordInflow(
                    0, cents, HouseholdIncomeSource.OtherDocumented, "test", "test funding", "test");
            }

            public void Spend(int householdId, int cents)
            {
                Ledgers.GetOrCreate(householdId).RecordOutflow(1, cents, "test spending", "test");
            }

            public void Employ(int personId, string employmentId, string businessId)
            {
                Employments.Register(new EmploymentRelationship
                {
                    Id = employmentId,
                    EmployeePersonId = personId,
                    EmployerBusinessId = businessId,
                    RoleDisplayName = "clerk",
                    LifecycleState = EmploymentLifecycleState.Active,
                    Compensation = CompensationTerms.FromWeeklyWage(300, ""),
                });
            }

            public HouseholdAutonomyDriver.AutonomyContext Context()
            {
                return new HouseholdAutonomyDriver.AutonomyContext
                {
                    Ledgers = Ledgers,
                    Memberships = Memberships,
                    Employments = Employments,
                    ContactGraph = Contacts,
                    Decisions = Decisions,
                    DecisionDriver = Driver,
                    HousingSearchNeeds = HousingNeeds,
                    FinancialObligations = Finance,
                    Ids = Ids,
                    RentObligationsFor = hid => hid == 7 ? RentStates : new List<RentObligationState>(),
                    ShelterRiskFor = hid => hid == 7 && ShelterRisk,
                };
            }
        }

        [Test]
        public void DecliningReserves_RentDue_SearchesKnownIncome()
        {
            var f = new Fixture();
            f.AddMember(3, 7);
            f.AddMember(4, 7);
            f.Fund(7, 5000);
            f.Spend(7, 4600); // reserves now 400c and declining
            f.RentStates.Add(new RentObligationState
            {
                AgreementId = "agree-1",
                AmountDueCents = 6000,
                PaidCents = 0,
                Status = RentObligationStatus.Overdue,
            });
            // P3 knows P5 (documented link); P5 works at store-1.
            f.Contacts.SeedFormerCoworkerLinks(3, "mill-1", new[] { 5 });
            f.Employ(5, "emp-5", "store-1");

            var diag = new List<string>();
            f.Autonomy.EvaluateHouseholdDay(7, 100, f.Context(), diag);

            HouseholdDecision last = f.Decisions.LastDecisionFor("7");
            Assert.IsNotNull(last);
            Assert.AreEqual(HouseholdDecisionKind.SeekIncome, last.Kind);
            // The decision executed a REAL discovery, not just a record.
            List<DiscoveredOpportunity> opps = f.Driver.OpportunityLog.GetForHousehold(7);
            Assert.AreEqual(1, opps.Count);
            Assert.AreEqual("store-1", opps[0].BusinessId);
        }

        [Test]
        public void AdequateResources_NoAction_DespiteRentObligation()
        {
            var f = new Fixture();
            f.AddMember(3, 7);
            f.Fund(7, 12000);
            f.RentStates.Add(new RentObligationState
            {
                AgreementId = "agree-1",
                AmountDueCents = 6000,
                PaidCents = 0,
                Status = RentObligationStatus.Current,
            });

            f.Autonomy.EvaluateHouseholdDay(7, 100, f.Context(), null);

            HouseholdDecision last = f.Decisions.LastDecisionFor("7");
            Assert.AreEqual(HouseholdDecisionKind.NoAction, last.Kind);
            Assert.AreEqual(0, f.Driver.OpportunityLog.GetForHousehold(7).Count);
        }

        [Test]
        public void JobLoss_WithKnownOpportunity_SeeksIncome()
        {
            var f = new Fixture();
            f.AddMember(3, 7);
            f.Fund(7, 3000);
            f.Employ(3, "emp-3", "store-1"); // P3 employed on day 100...
            f.RentStates.Add(new RentObligationState
            {
                AgreementId = "agree-1",
                AmountDueCents = 6000,
                PaidCents = 0,
                Status = RentObligationStatus.Overdue,
            });

            f.Autonomy.EvaluateHouseholdDay(7, 100, f.Context(), null);

            // ...employment ends before day 101.
            foreach (EmploymentRelationship rel in f.Employments.GetActiveByEmployee(3))
                rel.LifecycleState = EmploymentLifecycleState.Ended;

            // P3 knows P5 who works at mill-1.
            f.Contacts.SeedFormerCoworkerLinks(3, "store-1", new[] { 5 });
            f.Employ(5, "emp-5", "mill-1");
            // Discover once so the opportunity is known before job loss hits.
            f.Driver.OpportunityLog.RecordIfNew(7, "mill-1", "hand", "P3↔P5 FormerCoworker: test", 100);

            var diag = new List<string>();
            f.Autonomy.EvaluateHouseholdDay(7, 101, f.Context(), diag);

            HouseholdDecision last = f.Decisions.LastDecisionFor("7");
            Assert.AreEqual(HouseholdDecisionKind.SeekIncome, last.Kind);
            Assert.IsTrue(last.Reason.Contains("job loss"), "got: " + last.Reason);
            Assert.IsTrue(diag.Exists(d => d.Contains("job loss")), "diagnostics must note the job loss");
        }

        [Test]
        public void JobLoss_AdequateReserves_ReliesOnHouseholdResources()
        {
            var f = new Fixture();
            f.AddMember(3, 7);
            f.Fund(7, 12000);
            f.Employ(3, "emp-3", "store-1");
            f.RentStates.Add(new RentObligationState
            {
                AgreementId = "agree-1",
                AmountDueCents = 6000,
                PaidCents = 0,
                Status = RentObligationStatus.Current,
            });

            f.Autonomy.EvaluateHouseholdDay(7, 100, f.Context(), null);

            foreach (EmploymentRelationship rel in f.Employments.GetActiveByEmployee(3))
                rel.LifecycleState = EmploymentLifecycleState.Ended;

            f.Autonomy.EvaluateHouseholdDay(7, 101, f.Context(), null);

            HouseholdDecision last = f.Decisions.LastDecisionFor("7");
            Assert.AreEqual(HouseholdDecisionKind.NoAction, last.Kind);
            Assert.IsTrue(last.Reason.Contains("relying on household reserves"), "got: " + last.Reason);
        }

        [Test]
        public void ShelterRisk_OpensNegotiationOrRelocation()
        {
            var f = new Fixture();
            f.AddMember(3, 7);
            f.Fund(7, 2000);
            f.ShelterRisk = true;
            f.RentStates.Add(new RentObligationState
            {
                AgreementId = "agree-1",
                AmountDueCents = 6000,
                PaidCents = 0,
                Status = RentObligationStatus.Overdue,
            });
            var ctx = f.Context();
            ctx.ExecutionParams.AgreementId = "agree-1";
            ctx.ExecutionParams.LandlordHolderId = "P-landlord";
            ctx.ExecutionParams.RequestedChange = "extend due date by 14 days";

            // Temporary shortfall (no expected income but reserves exist):
            // structural check uses income only -> 0 < 6000 -> RelocateCheaper.
            f.Autonomy.EvaluateHouseholdDay(7, 100, ctx, null);
            HouseholdDecision last = f.Decisions.LastDecisionFor("7");
            Assert.AreEqual(HouseholdDecisionKind.RelocateCheaper, last.Kind);
            Assert.IsNotNull(f.HousingNeeds.GetOpenNeed(7));
        }

        [Test]
        public void SaveLoad_PreservesFingerprints_NoRepeatJobLossTrigger()
        {
            var f = new Fixture();
            f.AddMember(3, 7);
            f.Fund(7, 12000);
            f.Employ(3, "emp-3", "store-1");

            f.Autonomy.EvaluateHouseholdDay(7, 100, f.Context(), null);

            HouseholdAutonomyDriverSaveDto dto = f.Autonomy.CaptureSaveDto();
            var restored = new HouseholdAutonomyDriver();
            restored.LoadFromSaveDto(dto);

            // End the employment, then evaluate on the RESTORED driver: it must
            // still detect the transition exactly once...
            foreach (EmploymentRelationship rel in f.Employments.GetActiveByEmployee(3))
                rel.LifecycleState = EmploymentLifecycleState.Ended;

            var ctx = f.Context(); // fresh context, restored autonomy driver
            var diag1 = new List<string>();
            restored.EvaluateHouseholdDay(7, 101, ctx, diag1);
            Assert.IsTrue(diag1.Exists(d => d.Contains("job loss")), "first post-load evaluation must detect the transition");

            // ...and a second evaluation must NOT re-trigger it.
            var diag2 = new List<string>();
            restored.EvaluateHouseholdDay(7, 102, ctx, diag2);
            Assert.IsFalse(diag2.Exists(d => d.Contains("job loss")), "job loss must not re-trigger");

            // Clear-then-load replaces state.
            restored.LoadFromSaveDto(dto);
            var diag3 = new List<string>();
            restored.EvaluateHouseholdDay(7, 103, ctx, diag3);
            Assert.IsTrue(diag3.Exists(d => d.Contains("job loss")));
        }
    }
}
