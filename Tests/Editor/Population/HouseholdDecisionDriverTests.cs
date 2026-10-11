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
    /// Worker C (Pass III): decisions trigger REAL operations, not just records.
    /// SeekIncome discovers opportunities through the contact graph (no omniscient
    /// lists); RelocateCheaper opens a real housing search need; Borrow creates a
    /// real obligation only with a legitimate known lender (never an invented
    /// counterparty); NegotiateTerms creates a pending request; Economize sets a
    /// real spending policy. Save/load never duplicates obligations.
    /// </summary>
    [TestFixture]
    public sealed class HouseholdDecisionDriverTests
    {
        private sealed class Fixture
        {
            public ContactGraphService Contacts = new ContactGraphService();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public HousingSearchNeedRegistry HousingNeeds = new HousingSearchNeedRegistry();
            public FinancialObligationAuthority Finance = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public HouseholdDecisionDriver Driver;

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

            public void Employ(int personId, string employmentId, string businessId, string role)
            {
                Employments.Register(new EmploymentRelationship
                {
                    Id = employmentId,
                    EmployeePersonId = personId,
                    EmployerBusinessId = businessId,
                    RoleDisplayName = role,
                    LifecycleState = EmploymentLifecycleState.Active,
                    Compensation = CompensationTerms.FromWeeklyWage(300, ""),
                });
            }
        }

        private static HouseholdDecisionService DecisionService() => new HouseholdDecisionService();

        private static HouseholdDecision DecideSeekIncome()
        {
            var d = DecisionService().Evaluate(new HouseholdPressureInput
            {
                HouseholdId = "7",
                Day = 100,
                ReservesCents = 400,
                ObligationsDueCents = 6000,
                ExpectedIncomeCentsPerDay = 100,
                Dependents = 2,
                KnownOpportunityCount = 1,
            });
            Assert.AreEqual(HouseholdDecisionKind.SeekIncome, d.Kind);
            return d;
        }

        [Test]
        public void SeekIncome_DiscoversOpportunitiesThroughDocumentedContacts()
        {
            var f = new Fixture();
            f.AddMember(3, 7);
            f.AddMember(4, 7);
            // P3 knows P5 through documented former-coworker link; P5 works at store-1.
            f.Contacts.SeedFormerCoworkerLinks(3, "mill-1", new[] { 5 });
            f.Employ(5, "emp-5", "store-1", "clerk");

            var diag = new List<string>();
            HouseholdDecisionDriver.DecisionExecutionResult result = f.Driver.Execute(
                DecideSeekIncome(), null,
                new HouseholdDecisionDriver.ExecutionContext { Memberships = f.Memberships },
                100, diag);

            Assert.IsTrue(result.Executed, result.Detail);
            List<DiscoveredOpportunity> opps = f.Driver.OpportunityLog.GetForHousehold(7);
            Assert.AreEqual(1, opps.Count);
            Assert.AreEqual("store-1", opps[0].BusinessId);
            Assert.AreEqual("clerk", opps[0].RoleHint);
            Assert.IsTrue(opps[0].Provenance.Contains("FormerCoworker"),
                "provenance must be documented, got: " + opps[0].Provenance);
        }

        [Test]
        public void SeekIncome_UnknownStranger_YieldsNoOpportunity()
        {
            var f = new Fixture();
            f.AddMember(3, 7);
            // P9 works at store-1 but P3 has NO documented link to P9.
            f.Employ(9, "emp-9", "store-1", "clerk");

            var diag = new List<string>();
            HouseholdDecisionDriver.DecisionExecutionResult result = f.Driver.Execute(
                DecideSeekIncome(), null,
                new HouseholdDecisionDriver.ExecutionContext { Memberships = f.Memberships },
                100, diag);

            Assert.IsTrue(result.Executed);
            Assert.AreEqual(0, f.Driver.OpportunityLog.GetForHousehold(7).Count);
        }

        [Test]
        public void SeekIncome_RepeatDiscovery_DoesNotDuplicate()
        {
            var f = new Fixture();
            f.AddMember(3, 7);
            f.Contacts.SeedFormerCoworkerLinks(3, "mill-1", new[] { 5 });
            f.Employ(5, "emp-5", "store-1", "clerk");
            var ctx = new HouseholdDecisionDriver.ExecutionContext { Memberships = f.Memberships };

            f.Driver.Execute(DecideSeekIncome(), null, ctx, 100, null);
            f.Driver.Execute(DecideSeekIncome(), null, ctx, 101, null);

            Assert.AreEqual(1, f.Driver.OpportunityLog.GetForHousehold(7).Count);
        }

        [Test]
        public void RelocateCheaper_OpensRealHousingSearchNeed()
        {
            var f = new Fixture();
            f.AddMember(3, 7);
            f.AddMember(4, 7);
            var decision = new HouseholdDecision
            {
                HouseholdId = "7",
                Day = 100,
                Kind = HouseholdDecisionKind.RelocateCheaper,
                Reason = "test",
            };

            HouseholdDecisionDriver.DecisionExecutionResult result = f.Driver.Execute(
                decision, null,
                new HouseholdDecisionDriver.ExecutionContext { Memberships = f.Memberships },
                100, null);

            Assert.IsTrue(result.Executed, result.Detail);
            HousingSearchNeed need = f.HousingNeeds.GetOpenNeed(7);
            Assert.IsNotNull(need);
            Assert.AreEqual(2, need.RequiredSleepingPlaces);
        }

        [Test]
        public void Borrow_WithKnownLender_CreatesRealObligation()
        {
            var f = new Fixture();
            var decision = new HouseholdDecision
            {
                HouseholdId = "7",
                Day = 100,
                Kind = HouseholdDecisionKind.Borrow,
                Reason = "test",
            };
            var pressure = new HouseholdPressureInput
            {
                HouseholdId = "7",
                Day = 100,
                ReservesCents = 400,
                ObligationsDueCents = 6000,
                ExpectedIncomeCentsPerDay = 100,
                KnownCreditorId = "P9",
            };

            HouseholdDecisionDriver.DecisionExecutionResult result =
                f.Driver.Execute(decision, pressure, null, 100, null);

            Assert.IsTrue(result.Executed, result.Detail);
            Assert.AreEqual(1, f.Finance.Obligations.Count);
            foreach (FinancialObligation o in f.Finance.Obligations)
            {
                Assert.AreEqual("household-7", o.Debtor);
                Assert.AreEqual("P9", o.Creditor);
                Assert.AreEqual(2600, o.OutstandingPrincipalCents); // 6000 - (400 + 3000)
                Assert.AreEqual(FinancialObligationKind.Loan, o.Kind);
            }
        }

        [Test]
        public void Borrow_WithoutKnownLender_RefusesToInventCounterparty()
        {
            var f = new Fixture();
            var decision = new HouseholdDecision
            {
                HouseholdId = "7",
                Day = 100,
                Kind = HouseholdDecisionKind.Borrow,
                Reason = "test",
            };
            var pressure = new HouseholdPressureInput
            {
                HouseholdId = "7",
                Day = 100,
                ReservesCents = 400,
                ObligationsDueCents = 6000,
                ExpectedIncomeCentsPerDay = 100,
                KnownCreditorId = string.Empty, // no legitimate lender known
            };

            HouseholdDecisionDriver.DecisionExecutionResult result =
                f.Driver.Execute(decision, pressure, null, 100, null);

            Assert.IsFalse(result.Executed);
            Assert.AreEqual(0, f.Finance.Obligations.Count);
        }

        [Test]
        public void NegotiateTerms_CreatesPendingRequest_NoAutomaticConcession()
        {
            var f = new Fixture();
            var decision = new HouseholdDecision
            {
                HouseholdId = "7",
                Day = 100,
                Kind = HouseholdDecisionKind.NegotiateTerms,
                Reason = "test",
            };
            var ctx = new HouseholdDecisionDriver.ExecutionContext();
            ctx.Params.AgreementId = "agree-1";
            ctx.Params.LandlordHolderId = "P-landlord";
            ctx.Params.RequestedChange = "extend due date by 14 days";

            HouseholdDecisionDriver.DecisionExecutionResult result =
                f.Driver.Execute(decision, null, ctx, 100, null);

            Assert.IsTrue(result.Executed, result.Detail);
            Assert.AreEqual(1, f.Driver.NegotiationRequests.Count);
            TenancyNegotiationRequest req = f.Driver.NegotiationRequests[0];
            Assert.AreEqual(TenancyNegotiationStatus.Pending, req.Status);
            Assert.AreEqual("agree-1", req.AgreementId);
            Assert.AreEqual("extend due date by 14 days", req.RequestedChange);
        }

        [Test]
        public void NegotiateTerms_WithoutAgreement_CannotExecute()
        {
            var f = new Fixture();
            var decision = new HouseholdDecision
            {
                HouseholdId = "7",
                Day = 100,
                Kind = HouseholdDecisionKind.NegotiateTerms,
                Reason = "test",
            };

            HouseholdDecisionDriver.DecisionExecutionResult result = f.Driver.Execute(
                decision, null, new HouseholdDecisionDriver.ExecutionContext(), 100, null);

            Assert.IsFalse(result.Executed);
            Assert.AreEqual(0, f.Driver.NegotiationRequests.Count);
        }

        [Test]
        public void Economize_SetsStandingSpendingPolicy()
        {
            var f = new Fixture();
            var decision = new HouseholdDecision
            {
                HouseholdId = "7",
                Day = 100,
                Kind = HouseholdDecisionKind.Economize,
                Reason = "thin cushion",
            };

            HouseholdDecisionDriver.DecisionExecutionResult result =
                f.Driver.Execute(decision, null, null, 100, null);

            Assert.IsTrue(result.Executed, result.Detail);
            HouseholdSpendingPolicy policy = f.Driver.GetSpendingPolicy(7);
            Assert.IsNotNull(policy);
            Assert.AreEqual(50, policy.DiscretionaryCapPercent);

            // A second Economize replaces, never stacks.
            f.Driver.Execute(decision, null, null, 101, null);
            Assert.AreEqual(1, f.Driver.SpendingPolicies.Count);
        }

        [Test]
        public void SaveLoad_DoesNotDuplicateObligationsOrRequests()
        {
            var f = new Fixture();
            f.AddMember(3, 7);
            f.Contacts.SeedFormerCoworkerLinks(3, "mill-1", new[] { 5 });
            f.Employ(5, "emp-5", "store-1", "clerk");

            var ctx = new HouseholdDecisionDriver.ExecutionContext { Memberships = f.Memberships };
            f.Driver.Execute(DecideSeekIncome(), null, ctx, 100, null);

            var borrowDecision = new HouseholdDecision { HouseholdId = "7", Day = 100, Kind = HouseholdDecisionKind.Borrow, Reason = "t" };
            var pressure = new HouseholdPressureInput
            {
                HouseholdId = "7", Day = 100, ReservesCents = 400,
                ObligationsDueCents = 6000, ExpectedIncomeCentsPerDay = 100, KnownCreditorId = "P9",
            };
            f.Driver.Execute(borrowDecision, pressure, null, 100, null);

            var negCtx = new HouseholdDecisionDriver.ExecutionContext();
            negCtx.Params.AgreementId = "agree-1";
            negCtx.Params.LandlordHolderId = "P-landlord";
            var negDecision = new HouseholdDecision { HouseholdId = "7", Day = 100, Kind = HouseholdDecisionKind.NegotiateTerms, Reason = "t" };
            f.Driver.Execute(negDecision, null, negCtx, 100, null);

            HouseholdDecisionDriverSaveDto dto = f.Driver.CaptureSaveDto();

            // Fresh driver, same finance authority: restore must not re-create anything.
            var restored = new HouseholdDecisionDriver(f.Contacts, f.Employments, f.HousingNeeds, f.Finance, f.Ids);
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.OpportunityLog.GetForHousehold(7).Count);
            Assert.AreEqual(1, restored.NegotiationRequests.Count);
            Assert.AreEqual(1, f.Finance.Obligations.Count); // still one: restore created none

            // Loading the same DTO twice replaces, never appends.
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(1, restored.OpportunityLog.GetForHousehold(7).Count);
            Assert.AreEqual(1, restored.NegotiationRequests.Count);
        }
    }
}
