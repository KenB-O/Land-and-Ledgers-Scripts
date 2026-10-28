using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass XII Worker C (PASS11-003, PASS11-007): exactly-once wage
    /// settlement. Pass XI WS6 proved the AUTOMATED weekly runs never
    /// re-pay an accrued throwing-lookup week (the next run pays only
    /// the next week's wage). What Pass XI left as a code comment only
    /// ("do NOT also pay this week's wage via PayAgreedWage") was the
    /// MANUAL path: PayAgreedWage consulted no payable, no settled-week
    /// marker and no (employment, week) key, so a manual re-pay after
    /// lookup recovery paid cash on top of the open liability, and
    /// settling that liability afterwards deducted the wage a second
    /// time. These tests reproduce that hole before the fix, plus the
    /// legacy-slot twin (a throwing lookup there refused with no
    /// accrual at all, so the week's wage was simply lost) and the
    /// direct-driver twin (ResolveWeeklyPayroll itself had no
    /// settled-week guard — only WageSettlementService did — so a
    /// repeated direct run in one week paid twice).
    /// </summary>
    [TestFixture]
    public sealed class Pass12CPayrollExactlyOnceTests
    {
        private sealed class Fixture
        {
            public BusinessInstanceState Business;
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public EmploymentRelationship Employment;

            public Fixture(bool withRegistry, int cashCents)
            {
                BusinessProfileDefinition profile = BusinessProfileDefinition.CreateFallback(
                    BusinessType.Blacksmith, "Test Business");
                Business = BusinessInstanceState.Create(
                    "pass12c_payroll", profile, 0, BusinessOwnerIdentity.Player());
                Business.RuntimeState.SetCurrentCashCents(cashCents);
                Business.RuntimeState.HouseholdLedgers = Ledgers;
                Business.RuntimeState.PersonHouseholdIdLookup = Memberships.GetActiveHouseholdId;
                if (withRegistry)
                {
                    Business.RuntimeState.EmploymentRegistry = Employments;
                }
                Employment = new EmploymentRelationship
                {
                    Id = "pass12c-emp-1",
                    EmployeePersonId = 1,
                    EmployerBusinessId = Business.RuntimeState.BusinessId,
                    RoleDisplayName = "Hand",
                    Kind = EmploymentKind.Permanent,
                    LifecycleState = EmploymentLifecycleState.Active,
                    Compensation = CompensationTerms.FromWeeklyWage(1000, "test"),
                    StartDayIndex = 0,
                    Source = EmploymentSource.Authored,
                };
                if (withRegistry)
                {
                    Assert.IsNull(Employments.Register(Employment));
                }
                Assert.IsNull(Memberships.Register(new HouseholdMembership
                {
                    MembershipId = 1001,
                    PersonId = 1,
                    HouseholdId = 11,
                    Lifecycle = HouseholdMembershipLifecycle.Active,
                    StartDayIndex = 0,
                    EndDayIndex = -1,
                    Source = HouseholdMembershipSource.Authored,
                }));
                Ledgers.GetOrCreate(11);
            }

            public int HouseholdBalance()
            {
                return Ledgers.GetOrCreate(11).GetBalanceCents();
            }

            public void AddSlot(string workerId, int wageCents)
            {
                FieldInfo slotsField = typeof(BusinessRuntimeState).GetField(
                    "workerSlots", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(slotsField);
                var slots = (List<WorkerSlotState>)slotsField.GetValue(Business.RuntimeState);
                var slot = new WorkerSlotState("pass12c-slot", "Hand Slot", wageCents, true);
                slot.Assign(workerId, "Test Worker", wageCents);
                slots.Add(slot);
            }
        }

        [Test]
        public void ManualPayAfterLookupFailureAccrual_IsRefused_LiabilityStandsOnce()
        {
            var f = new Fixture(withRegistry: true, cashCents: 10000);
            f.Business.RuntimeState.PersonUnavailableForPayrollLookup = id =>
                throw new InvalidOperationException("lookup store unavailable");
            f.Business.RuntimeState.ResolveWeeklyPayroll();
            Assert.AreEqual(1000, f.Business.RuntimeState.AccruedLiabilityCents,
                "setup: the refused week's wage is preserved as a liability.");

            f.Business.RuntimeState.PersonUnavailableForPayrollLookup = id => false;
            string refusal = f.Business.RuntimeState.PayAgreedWage(
                "pass12c-emp-1", 1000, "missed week re-pay", 10, new List<string>());

            Assert.IsNotNull(refusal,
                "A manual re-pay of an accrued lookup-failure week must be refused, not paid in cash.");
            Assert.AreEqual(10000, f.Business.RuntimeState.CurrentCashCents,
                "No cash moves on the refused manual re-pay.");
            Assert.AreEqual(0, f.HouseholdBalance(),
                "The worker holds no cash for the week yet — only the liability.");
            Assert.AreEqual(1000, f.Business.RuntimeState.AccruedLiabilityCents,
                "The liability still stands exactly once; settling it — not re-paying — is the remedy.");
        }

        [Test]
        public void ManualPayTwiceSamePeriod_SecondIsRefused()
        {
            var f = new Fixture(withRegistry: true, cashCents: 10000);
            string first = f.Business.RuntimeState.PayAgreedWage(
                "pass12c-emp-1", 1000, "partial week", 10, new List<string>());
            Assert.IsNull(first, "the first manual payment succeeds.");
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents);

            string second = f.Business.RuntimeState.PayAgreedWage(
                "pass12c-emp-1", 1000, "partial week", 10, new List<string>());
            Assert.IsNotNull(second, "the identical manual payment must not post twice.");
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents,
                "Cash moves exactly once for one manual period.");
            Assert.AreEqual(1000, f.HouseholdBalance());
        }

        [Test]
        public void ManualPayDifferentPeriod_AfterManualPay_Pays()
        {
            var f = new Fixture(withRegistry: true, cashCents: 10000);
            Assert.IsNull(f.Business.RuntimeState.PayAgreedWage(
                "pass12c-emp-1", 1000, "partial week A", 10, new List<string>()));
            Assert.IsNull(f.Business.RuntimeState.PayAgreedWage(
                "pass12c-emp-1", 1000, "partial week B", 17, new List<string>()),
                "A genuinely different period is a different wage, not a duplicate.");
            Assert.AreEqual(8000, f.Business.RuntimeState.CurrentCashCents);
            Assert.AreEqual(2000, f.HouseholdBalance());
        }

        [Test]
        public void ManualPayAfterLiabilitySettled_Pays()
        {
            var f = new Fixture(withRegistry: true, cashCents: 10000);
            var authority = new FinancialObligationAuthority();
            f.Business.RuntimeState.ObligationAuthority = authority;
            f.Business.RuntimeState.ObligationIdRegistry = new EntityIdRegistry();
            f.Business.RuntimeState.PersonUnavailableForPayrollLookup = id =>
                throw new InvalidOperationException("lookup store unavailable");
            f.Business.RuntimeState.ResolveWeeklyPayroll();
            Assert.AreEqual(1, f.Business.RuntimeState.NamedPayables.Count);
            string obligationId = f.Business.RuntimeState.NamedPayables[0].ObligationId;
            Assert.IsFalse(string.IsNullOrWhiteSpace(obligationId));

            f.Business.RuntimeState.PersonUnavailableForPayrollLookup = id => false;
            Assert.IsTrue(f.Business.RuntimeState.TrySettleNamedPayable(
                obligationId, 1000, 11, out string settleDiagnostic), settleDiagnostic);
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents,
                "settlement paid the preserved week from real cash.");
            Assert.AreEqual(0, f.Business.RuntimeState.AccruedLiabilityCents);

            // The week is now genuinely settled, so a later manual payment
            // for a NEW period is no longer blocked by the old liability.
            // (A re-pay of the settled week itself is still blocked by the
            // manual-payment key only when it repeats an earlier manual
            // payment — this payment was never made manually.)
            Assert.IsNull(f.Business.RuntimeState.PayAgreedWage(
                "pass12c-emp-1", 1000, "following week", 17, new List<string>()));
            Assert.AreEqual(8000, f.Business.RuntimeState.CurrentCashCents);
        }

        [Test]
        public void LegacySlot_ThrowingLookup_AccruesLiability_WithoutPaying()
        {
            var f = new Fixture(withRegistry: false, cashCents: 10000);
            f.AddSlot("1", 1000);
            f.Business.RuntimeState.PersonUnavailableForPayrollLookup = id =>
                throw new InvalidOperationException("lookup store unavailable");

            f.Business.RuntimeState.ResolveWeeklyPayroll();

            Assert.AreEqual(10000, f.Business.RuntimeState.CurrentCashCents,
                "No cash moves on an unknown eligibility outcome.");
            Assert.AreEqual(1000, f.Business.RuntimeState.AccruedLiabilityCents,
                "The legacy path must preserve the week's wage exactly like the employment path.");
            bool named = false;
            foreach (BusinessNamedPayableRecord record in f.Business.RuntimeState.NamedPayables)
            {
                if (record != null && record.Creditor == "person:1" && record.AmountCents == 1000
                    && record.Reason.Contains("payroll eligibility lookup failed"))
                    named = true;
            }
            Assert.IsTrue(named, "The legacy liability names the worker and the lookup failure.");
            bool diagnosed = false;
            foreach (string line in f.Business.RuntimeState.LastWeeklyPayrollDiagnostics)
                if (line.Contains("lookup failed") && line.Contains("liability")) diagnosed = true;
            Assert.IsTrue(diagnosed, "The diagnostic names the lookup failure and the liability.");
        }

        [Test]
        public void LegacySlot_ConfirmedUnavailable_AccruesNothing()
        {
            var f = new Fixture(withRegistry: false, cashCents: 10000);
            f.AddSlot("1", 1000);
            f.Business.RuntimeState.PersonUnavailableForPayrollLookup = id => true;

            f.Business.RuntimeState.ResolveWeeklyPayroll();

            Assert.AreEqual(10000, f.Business.RuntimeState.CurrentCashCents);
            Assert.AreEqual(0, f.Business.RuntimeState.AccruedLiabilityCents,
                "A confirmed dead/off-map person gets neither a wage nor a liability, on either path.");
        }

        [Test]
        public void LegacyAccrual_ThenProjectedEmploymentManualPay_IsRefused()
        {
            var f = new Fixture(withRegistry: false, cashCents: 10000);
            f.AddSlot("1", 1000);
            f.Business.RuntimeState.PersonUnavailableForPayrollLookup = id =>
                throw new InvalidOperationException("lookup store unavailable");
            f.Business.RuntimeState.ResolveWeeklyPayroll();
            Assert.AreEqual(1000, f.Business.RuntimeState.AccruedLiabilityCents);

            // The registry wires later; the slot projects to an employment.
            // A manual pay for that employment must still not duplicate the
            // legacy-path liability for the same worker.
            f.Business.RuntimeState.PersonUnavailableForPayrollLookup = id => false;
            f.Business.RuntimeState.EmploymentRegistry = f.Employments;
            f.Business.RuntimeState.EnsureEmploymentRecordsFromSlots();
            Assert.AreEqual(1, f.Employments.GetByEmployee(1).Count);
            string projectedId = f.Employments.GetByEmployee(1)[0].Id;

            string refusal = f.Business.RuntimeState.PayAgreedWage(
                projectedId, 1000, "missed week re-pay", 10, new List<string>());
            Assert.IsNotNull(refusal,
                "The legacy accrual blocks a manual re-pay for the same worker's preserved week.");
            Assert.AreEqual(10000, f.Business.RuntimeState.CurrentCashCents);
            Assert.AreEqual(1000, f.Business.RuntimeState.AccruedLiabilityCents);
        }

        [Test]
        public void WeeklyPayroll_TwiceSameWeek_PaysOnce()
        {
            var f = new Fixture(withRegistry: true, cashCents: 10000);
            f.Business.RuntimeState.PayrollDayIndex = 6; // week 0

            f.Business.RuntimeState.ResolveWeeklyPayroll();
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents);
            f.Business.RuntimeState.ResolveWeeklyPayroll();
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents,
                "A repeated direct weekly run in the same week must not pay twice.");
            Assert.AreEqual(1000, f.Business.RuntimeState.LastWeeklyPayrollCents);

            f.Business.RuntimeState.PayrollDayIndex = 13; // week 1
            f.Business.RuntimeState.ResolveWeeklyPayroll();
            Assert.AreEqual(8000, f.Business.RuntimeState.CurrentCashCents,
                "The next week still pays — the guard is per-week, not a latch.");
        }

        [Test]
        public void WeeklyPayroll_TwiceSameWeek_LegacyPath_PaysOnce()
        {
            var f = new Fixture(withRegistry: false, cashCents: 10000);
            f.AddSlot("1", 1000);
            f.Business.RuntimeState.PayrollDayIndex = 6; // week 0

            f.Business.RuntimeState.ResolveWeeklyPayroll();
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents);
            f.Business.RuntimeState.ResolveWeeklyPayroll();
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents,
                "The legacy path gets the same per-week guard.");
        }

        [Test]
        public void PartialPeriodCashPay_CountsInWeeklyPayrollTotal()
        {
            var f = new Fixture(withRegistry: true, cashCents: 10000);
            Assert.IsNull(f.Business.RuntimeState.PayAgreedWage(
                "pass12c-emp-1", 1000, "partial week", 10, new List<string>()));
            Assert.AreEqual(1000, f.Business.RuntimeState.LastWeeklyPayrollCents,
                "A cash wage paid mid-week is payroll paid this week; weekly payroll totals must include it.");
        }

        [Test]
        public void ExactlyOnceKeys_SurviveSaveLoad()
        {
            var f = new Fixture(withRegistry: true, cashCents: 10000);
            f.Business.RuntimeState.PayrollDayIndex = 6; // week 0
            f.Business.RuntimeState.ResolveWeeklyPayroll();
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents);
            Assert.IsNull(f.Business.RuntimeState.PayAgreedWage(
                "pass12c-emp-1", 500, "partial week", 10, new List<string>()));
            Assert.AreEqual(8500, f.Business.RuntimeState.CurrentCashCents);

            BusinessRuntimeSaveDto dto = f.Business.RuntimeState.CaptureSaveDto();
            BusinessRuntimeState restored = BusinessRuntimeState.FromSaveDto(dto, BusinessType.Blacksmith);
            restored.EmploymentRegistry = f.Employments;
            restored.HouseholdLedgers = f.Ledgers;
            restored.PersonHouseholdIdLookup = f.Memberships.GetActiveHouseholdId;
            restored.PayrollDayIndex = 6; // same week 0

            restored.ResolveWeeklyPayroll();
            Assert.AreEqual(8500, restored.CurrentCashCents,
                "Restore must not re-pay the already-resolved week.");
            string repeat = restored.PayAgreedWage(
                "pass12c-emp-1", 500, "partial week", 10, new List<string>());
            Assert.IsNotNull(repeat, "Restore must not forget a settled manual payment.");
            Assert.AreEqual(8500, restored.CurrentCashCents);
        }
    }
}
