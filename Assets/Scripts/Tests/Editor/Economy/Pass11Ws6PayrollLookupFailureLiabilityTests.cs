using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass XI WS6 first-gate (M3), option (a): a throwing payroll
    /// eligibility lookup in the weekly employment path no longer
    /// strands the week's wage. The WS3 comment claimed the wage could
    /// still be paid later through the next payroll run or PayAgreedWage;
    /// that was false — the next run pays only the next week's wage, and
    /// PayAgreedWage consults the same throwing lookup. The refused
    /// week's wage is now preserved as an operating liability (the same
    /// named-payable + scalar accrual as the shortfall branch), without
    /// suspending the employment (a broken lookup is not an employer
    /// payment default, and suspension would strand every future week
    /// too). A later good run pays the next week's wage in cash exactly
    /// once and neither clears nor double-pays the accrued liability.
    /// A CONFIRMED unavailable person still gets no accrual: no wage to
    /// a ghost, and no liability to one either.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws6PayrollLookupFailureLiabilityTests
    {
        private static BusinessInstanceState BusinessWithEmployment(
            EmploymentRelationshipRegistry employments,
            out EmploymentRelationship employment)
        {
            var ledgers = new HouseholdLedgerRegistry();
            var memberships = new HouseholdMembershipRegistry();
            BusinessProfileDefinition profile = BusinessProfileDefinition.CreateFallback(
                BusinessType.Blacksmith, "Test Business");
            BusinessInstanceState business = BusinessInstanceState.Create(
                "pass11_ws6_payroll", profile, 0, BusinessOwnerIdentity.Player());
            business.RuntimeState.EmploymentRegistry = employments;
            business.RuntimeState.HouseholdLedgers = ledgers;
            business.RuntimeState.PersonHouseholdIdLookup = memberships.GetActiveHouseholdId;
            business.RuntimeState.SetCurrentCashCents(10000);
            employment = new EmploymentRelationship
            {
                Id = "pass11-ws6-emp-1",
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
            ledgers.GetOrCreate(11);
            return business;
        }

        [Test]
        public void WeeklyPayroll_ThrowingLookup_AccruesLiability_WithoutSuspension_AndLaterRunPaysCurrentWeekOnlyOnce()
        {
            var employments = new EmploymentRelationshipRegistry();
            BusinessInstanceState business = BusinessWithEmployment(employments, out EmploymentRelationship employment);
            business.RuntimeState.PersonUnavailableForPayrollLookup = id =>
                throw new InvalidOperationException("lookup store unavailable");

            business.RuntimeState.ResolveWeeklyPayroll();

            Assert.AreEqual(10000, business.RuntimeState.CurrentCashCents,
                "No cash moves on an unknown eligibility outcome.");
            Assert.AreEqual(1000, business.RuntimeState.AccruedLiabilityCents,
                "The refused week's wage is preserved as an operating liability, not forfeited.");
            bool named = false;
            foreach (BusinessNamedPayableRecord record in business.RuntimeState.NamedPayables)
            {
                if (record != null && record.Creditor == "person:1" && record.AmountCents == 1000)
                    named = true;
            }
            Assert.IsTrue(named, "The liability names the worker (person:1) for 1000c.");
            Assert.AreEqual(EmploymentLifecycleState.Active, employment.LifecycleState,
                "A broken lookup is not an employer default: the employment stays active so future weeks still pay.");
            Assert.IsTrue(business.RuntimeState.PayrollEligibilityLookupFailed);
            bool diagnosed = false;
            foreach (string line in business.RuntimeState.LastWeeklyPayrollDiagnostics)
                if (line.Contains("lookup failed") && line.Contains("operating liability")) diagnosed = true;
            Assert.IsTrue(diagnosed, "The diagnostic names the lookup failure and the liability, not a confirmed death/departure.");

            // Recovery: the next weekly run pays the NEXT week's wage in
            // cash exactly once — and the accrued liability is untouched,
            // neither cleared (that would lose the refused week) nor paid
            // again (that would double-pay it).
            business.RuntimeState.PersonUnavailableForPayrollLookup = id => false;
            business.RuntimeState.ResolveWeeklyPayroll();
            Assert.AreEqual(9000, business.RuntimeState.CurrentCashCents,
                "Exactly one current-week wage left the till on the recovery run.");
            Assert.AreEqual(1000, business.RuntimeState.AccruedLiabilityCents,
                "Weekly payroll never re-pays arrears: the refused week's liability stands exactly once.");
            Assert.AreEqual(EmploymentLifecycleState.Active, employment.LifecycleState);
        }

        [Test]
        public void WeeklyPayroll_ConfirmedUnavailable_StillAccruesNothing()
        {
            var employments = new EmploymentRelationshipRegistry();
            BusinessInstanceState business = BusinessWithEmployment(employments, out _);
            business.RuntimeState.PersonUnavailableForPayrollLookup = id => true;

            business.RuntimeState.ResolveWeeklyPayroll();

            Assert.AreEqual(10000, business.RuntimeState.CurrentCashCents);
            Assert.AreEqual(0, business.RuntimeState.AccruedLiabilityCents,
                "A confirmed dead/off-map person gets neither a wage nor a liability.");
            Assert.IsFalse(business.RuntimeState.PayrollEligibilityLookupFailed);
        }
    }
}
