using LandLedgers.Economy;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// T1B: payroll cutover to the PKG-6 EmploymentRelationship registry (Tech X §4.1–4.3).
    /// Nobody is paid who was never hired: every wage flows through an employment record.
    /// </summary>
    [TestFixture]
    public sealed class EmploymentPayrollCutoverTests
    {
        private static BusinessInstanceState CreateBusiness(BusinessType businessType)
        {
            BusinessProfileDefinition profile = BusinessProfileDefinition.CreateFallback(businessType, "Test Business");
            return BusinessInstanceState.Create("test_payroll_cutover", profile, 0, BusinessOwnerIdentity.Player());
        }

        private static WorkerSlotState FirstSlot(BusinessInstanceState business)
        {
            Assert.Greater(business.RuntimeState.WorkerSlots.Count, 0);
            return business.RuntimeState.WorkerSlots[0];
        }

        private static EmploymentRelationship NewEmployment(string businessId, int personId, int weeklyWageCents)
        {
            return new EmploymentRelationship
            {
                Id = $"test-emp-{personId}",
                EmployeePersonId = personId,
                EmployerBusinessId = businessId,
                RoleDisplayName = "Hand",
                Kind = EmploymentKind.Permanent,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(weeklyWageCents, "test"),
                StartDayIndex = 0,
                Source = EmploymentSource.Authored,
            };
        }

        [Test]
        public void Payroll_PaysAgreedRegistryWage_NotSlotWage()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.Blacksmith);
            WorkerSlotState slot = FirstSlot(business);
            slot.Assign("5", "Test Worker", 1000); // slot template wage: 1000c

            var employments = new EmploymentRelationshipRegistry();
            employments.Register(NewEmployment(business.RuntimeState.BusinessId, 5, 800)); // agreed: 800c
            business.RuntimeState.EmploymentRegistry = employments;
            business.RuntimeState.SetCurrentCashCents(10000);

            business.RuntimeState.ResolveWeeklyPayroll();

            Assert.AreEqual(9200, business.RuntimeState.CurrentCashCents,
                "Payroll pays the registry's agreed wage (800c), not the slot template wage (1000c).");
            Assert.AreEqual(800, business.RuntimeState.LastWeeklyPayrollCents);
        }

        [Test]
        public void Payroll_ProjectsEmployment_ForSlotWorkerWithoutRecord()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.Blacksmith);
            WorkerSlotState slot = FirstSlot(business);
            slot.Assign("7", "Legacy Hire", slot.WeeklyWageCents);

            var employments = new EmploymentRelationshipRegistry();
            business.RuntimeState.EmploymentRegistry = employments;
            business.RuntimeState.SetCurrentCashCents(10000);
            int cashBefore = business.RuntimeState.CurrentCashCents;

            business.RuntimeState.ResolveWeeklyPayroll();

            Assert.AreEqual(1, employments.Count, "The slot assignment is hiring evidence: projected once.");
            Assert.IsTrue(employments.TryGetById($"{business.RuntimeState.BusinessId}:{slot.SlotId}", out EmploymentRelationship projected));
            Assert.AreEqual(EmploymentSource.ProjectedFromWorkerSlot, projected.Source);
            Assert.AreEqual(7, projected.EmployeePersonId);
            Assert.Less(business.RuntimeState.CurrentCashCents, cashBefore, "The projected worker is paid through the registry.");
        }

        [Test]
        public void Payroll_DoesNotPay_EndedEmployment()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.Blacksmith);
            WorkerSlotState slot = FirstSlot(business);
            slot.Assign("9", "Former Worker", 1000);

            var employments = new EmploymentRelationshipRegistry();
            EmploymentRelationship employment = NewEmployment(business.RuntimeState.BusinessId, 9, 800);
            employment.LifecycleState = EmploymentLifecycleState.Ended;
            employment.EndReason = EmploymentEndReason.Dismissed;
            employments.Register(employment);
            business.RuntimeState.EmploymentRegistry = employments;
            business.RuntimeState.SetCurrentCashCents(10000);

            business.RuntimeState.ResolveWeeklyPayroll();

            Assert.AreEqual(10000, business.RuntimeState.CurrentCashCents,
                "Ended employments are not paid — nobody is paid who was never (or no longer) hired.");
            Assert.AreEqual(0, business.RuntimeState.LastWeeklyPayrollCents);
        }

        [Test]
        public void Payroll_MissedPayroll_SuspendsEmployment_AndAccruesLiability()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.Blacksmith);
            WorkerSlotState slot = FirstSlot(business);
            slot.Assign("11", "Unpaid Worker", 1000);

            var employments = new EmploymentRelationshipRegistry();
            employments.Register(NewEmployment(business.RuntimeState.BusinessId, 11, 800));
            business.RuntimeState.EmploymentRegistry = employments;
            business.RuntimeState.SetCurrentCashCents(100); // cannot cover 800c

            business.RuntimeState.ResolveWeeklyPayroll();

            Assert.IsTrue(employments.TryGetById("test-emp-11", out EmploymentRelationship employment));
            Assert.AreEqual(EmploymentLifecycleState.Suspended, employment.LifecycleState);
            Assert.AreEqual(EmploymentSuspensionReason.EmployerPaymentDefault, employment.SuspensionReason);
            Assert.Greater(business.RuntimeState.AccruedLiabilityCents, 0);
            Assert.AreEqual(100, business.RuntimeState.CurrentCashCents, "Unpaid wages are not disbursed.");
        }

        [Test]
        public void FilledWeeklyPayrollCents_ReadsRegistry_WhenWired()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.Blacksmith);
            WorkerSlotState slot = FirstSlot(business);
            slot.Assign("13", "Worker", 1000);

            var employments = new EmploymentRelationshipRegistry();
            employments.Register(NewEmployment(business.RuntimeState.BusinessId, 13, 750));
            business.RuntimeState.EmploymentRegistry = employments;

            Assert.AreEqual(750, business.RuntimeState.FilledWeeklyPayrollCents,
                "The payroll read comes from the registry's agreed wages, not slot template wages.");
        }

        [Test]
        public void Payroll_LegacyFallback_Works_WhenRegistryNotWired()
        {
            // T1B: unwired contexts keep the legacy slot-wage loop (documented fallback).
            BusinessInstanceState business = CreateBusiness(BusinessType.Blacksmith);
            WorkerSlotState slot = FirstSlot(business);
            slot.Assign("15", "Legacy Worker", 1000);

            Assert.IsNull(business.RuntimeState.EmploymentRegistry);
            business.RuntimeState.SetCurrentCashCents(10000);
            business.RuntimeState.ResolveWeeklyPayroll();

            Assert.AreEqual(9000, business.RuntimeState.CurrentCashCents);
            Assert.AreEqual(1000, business.RuntimeState.FilledWeeklyPayrollCents);
        }
    }
}
