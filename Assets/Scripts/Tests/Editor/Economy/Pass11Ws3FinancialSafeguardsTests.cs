using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Labor;
using LandLedgers.Persistence;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass XI WS3 (PASS9-007): the offer-pricing feed and the payroll
    /// guard. The weekly operating feed excludes financing, so a
    /// borrowing week does not masquerade as operating strength; a
    /// throwing payroll eligibility lookup withholds the wage instead
    /// of paying on an unknown outcome.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws3FinancialSafeguardsTests
    {
        [Test]
        public void WeeklyOperatingProfit_ExcludesFinancing_WhileWeekToDateNetIncludesIt()
        {
            BusinessRuntimeState state = BusinessRuntimeState.FromSaveDto(
                new BusinessRuntimeSaveDto
                {
                    businessId = "biz-1",
                    businessType = BusinessType.GeneralStore,
                    currentCashCents = 10000,
                    weekToDateRevenueCents = 1000,
                    lastWeeklyPayrollCents = 1500,
                    lastWeeklyInputProcurementSpendCents = 0,
                    lastWeeklyLocalTransferRevenueCents = 0,
                    lastWeeklyLocalTransferCostCents = 0,
                    weekToDateNetCents = 5000,
                    allTimeNetCents = 5000,
                }, BusinessType.GeneralStore);
            Assert.IsNotNull(state);
            Assert.AreEqual(-500, state.WeeklyOperatingProfitCents,
                "1000 revenue minus 1500 payroll is a weak operating week, whatever financing did");
            Assert.AreEqual(5000, state.WeekToDateNetCents,
                "the old heuristic figure is financing-inflated and must not drive the offer multiplier");
            Assert.IsTrue(state.WeeklyOperatingProfitCents < 0 && state.WeekToDateNetCents >= 0,
                "the two figures disagree here by construction: the feed, not WeekToDateNet, is the weak-performance signal");
        }

        [Test]
        public void Payroll_ThrowingEligibilityLookup_WithholdsWage_FailClosed()
        {
            var employments = new EmploymentRelationshipRegistry();
            var ledgers = new HouseholdLedgerRegistry();
            var memberships = new HouseholdMembershipRegistry();
            BusinessProfileDefinition profile = BusinessProfileDefinition.CreateFallback(
                BusinessType.Blacksmith, "Test Business");
            BusinessInstanceState business = BusinessInstanceState.Create(
                "pass11_payroll", profile, 0, BusinessOwnerIdentity.Player());
            business.RuntimeState.EmploymentRegistry = employments;
            business.RuntimeState.HouseholdLedgers = ledgers;
            business.RuntimeState.PersonHouseholdIdLookup = memberships.GetActiveHouseholdId;
            business.RuntimeState.SetCurrentCashCents(10000);
            business.RuntimeState.PersonUnavailableForPayrollLookup = id =>
                throw new InvalidOperationException("lookup store unavailable");

            var employment = new EmploymentRelationship
            {
                Id = "pass11-emp-1",
                EmployeePersonId = 1,
                EmployerBusinessId = business.RuntimeState.BusinessId,
                RoleDisplayName = "Hand",
                Kind = EmploymentKind.Permanent,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(1000, "test"),
                StartDayIndex = 0,
                Source = EmploymentSource.Authored,
            };
            Assert.IsNull(employments.Register(employment));
            Assert.IsNull(memberships.Register(new HouseholdMembership
            {
                MembershipId = 1001,
                PersonId = 1,
                HouseholdId = 11,
                Lifecycle = HouseholdMembershipLifecycle.Active,
                StartDayIndex = 0,
                EndDayIndex = -1,
                Source = HouseholdMembershipSource.Authored,
            }));
            HouseholdLedger household = ledgers.GetOrCreate(11);
            int householdBefore = household.GetBalanceCents();

            business.RuntimeState.ResolveWeeklyPayroll();

            Assert.AreEqual(10000, business.RuntimeState.CurrentCashCents, "no wage left the till on an unknown outcome");
            Assert.AreEqual(householdBefore, household.GetBalanceCents(), "no wage reached any household");
            Assert.IsTrue(business.RuntimeState.PayrollEligibilityLookupFailed, "the failure is recorded, not swallowed");
            bool diagnosed = false;
            foreach (string line in business.RuntimeState.LastWeeklyPayrollDiagnostics)
                if (line.Contains("lookup failed")) diagnosed = true;
            Assert.IsTrue(diagnosed, "the refusal is diagnosed as a lookup failure, not as a confirmed dead or off-map person");

            // Once the lookup works, the same worker is payable again: the
            // withheld wage was never posted, so it cannot double-pay.
            business.RuntimeState.PersonUnavailableForPayrollLookup = id => false;
            business.RuntimeState.ResolveWeeklyPayroll();
            Assert.AreEqual(9000, business.RuntimeState.CurrentCashCents, "the wage pays exactly once, on the first good lookup");
        }

        [Test]
        public void PayAgreedWage_ThrowingEligibilityLookup_RefusesWithLookupFailure()
        {
            var employments = new EmploymentRelationshipRegistry();
            var ledgers = new HouseholdLedgerRegistry();
            var memberships = new HouseholdMembershipRegistry();
            BusinessProfileDefinition profile = BusinessProfileDefinition.CreateFallback(
                BusinessType.Blacksmith, "Test Business");
            BusinessInstanceState business = BusinessInstanceState.Create(
                "pass11_payroll2", profile, 0, BusinessOwnerIdentity.Player());
            business.RuntimeState.EmploymentRegistry = employments;
            business.RuntimeState.HouseholdLedgers = ledgers;
            business.RuntimeState.PersonHouseholdIdLookup = memberships.GetActiveHouseholdId;
            business.RuntimeState.SetCurrentCashCents(10000);
            business.RuntimeState.PersonUnavailableForPayrollLookup = id =>
                throw new InvalidOperationException("lookup store unavailable");
            var employment = new EmploymentRelationship
            {
                Id = "pass11-emp-2",
                EmployeePersonId = 2,
                EmployerBusinessId = business.RuntimeState.BusinessId,
                RoleDisplayName = "Hand",
                Kind = EmploymentKind.Permanent,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(1000, "test"),
                StartDayIndex = 0,
                Source = EmploymentSource.Authored,
            };
            Assert.IsNull(employments.Register(employment));
            Assert.IsNull(memberships.Register(new HouseholdMembership
            {
                MembershipId = 1002,
                PersonId = 2,
                HouseholdId = 12,
                Lifecycle = HouseholdMembershipLifecycle.Active,
                StartDayIndex = 0,
                EndDayIndex = -1,
                Source = HouseholdMembershipSource.Authored,
            }));
            ledgers.GetOrCreate(12);

            string refusal = business.RuntimeState.PayAgreedWage("pass11-emp-2", 1000, "test period", 10, new List<string>());
            Assert.IsNotNull(refusal, "a wage on an unknown outcome must refuse");
            StringAssert.Contains("lookup failed", refusal);
            Assert.AreEqual(10000, business.RuntimeState.CurrentCashCents, "no cash moved on the refusal");
        }
    }
}
