// R19 (note 111 §3, reviewed in note 115): escrowed wages reach the
// worker. CreditWorkerHousehold escrows a deducted wage as a named payable
// when the household credit cannot land, and nothing ever settled that
// escrow. The SettleEscrowedWagePayables sweep settles unsettled
// CashAlreadyDeducted wage payables and credits the worker's household
// ledger with the same amount; it runs before the weekly payroll run on
// all three drivers. These tests pin the sweep contract on
// BusinessRuntimeState; the three driver call sites are harness-excluded
// and source-reviewed only (plan §8.5). The tests fail red at compile
// time before the slice (the sweep method does not exist at base).
//
// Fixture note (note 115 C5): no attendance provider is installed — the
// tests here never run ResolveWeeklyPayroll, and the sweep needs no
// attendance. The employment id in the escrow reasons is the fixture's
// real employment id ("r19-emp-1"); the plan's "E1" is that id's
// placeholder.
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
    /// R19 escrow settlement (Agent 8 round-3 C4, ruled by Agent 3 note 111
    /// §3, plan reviewed in note 115). A deducted wage whose household
    /// credit could not land sits as a named payable (CashAlreadyDeducted,
    /// reason starting "wage escrow"); the sweep settles it through the
    /// debt authority and credits the worker's household with the same
    /// amount, so business cash out == household in + escrowed payable
    /// finally closes. Settle-first, then credit, with re-escrow on
    /// credit failure — a failed credit never loses the wage. The sweep
    /// touches only CashAlreadyDeducted records whose reason starts with
    /// "wage escrow" (note 115 C2: shortfall liabilities ARE named
    /// payables with CashAlreadyDeducted=false, excluded BY THE PREDICATE).
    /// </summary>
    [TestFixture]
    public sealed class R19EscrowSettlementTests
    {
        private sealed class Fixture
        {
            public BusinessInstanceState Business;
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public EmploymentRelationship Employment;

            public Fixture(bool withRegistry, bool withEmploymentRecord, int cashCents)
            {
                BusinessProfileDefinition profile = BusinessProfileDefinition.CreateFallback(
                    BusinessType.Blacksmith, "Test Business");
                Business = BusinessInstanceState.Create(
                    "r19_escrow", profile, 0, BusinessOwnerIdentity.Player());
                Business.RuntimeState.SetCurrentCashCents(cashCents);
                Business.RuntimeState.HouseholdLedgers = Ledgers;
                Business.RuntimeState.PersonHouseholdIdLookup = Memberships.GetActiveHouseholdId;
                if (withRegistry)
                {
                    Business.RuntimeState.EmploymentRegistry = Employments;
                }
                Employment = new EmploymentRelationship
                {
                    Id = "r19-emp-1",
                    EmployeePersonId = 1,
                    EmployerBusinessId = Business.RuntimeState.BusinessId,
                    RoleDisplayName = "Hand",
                    Kind = EmploymentKind.Permanent,
                    LifecycleState = EmploymentLifecycleState.Active,
                    Compensation = CompensationTerms.FromWeeklyWage(1000, "test"),
                    StartDayIndex = 0,
                    Source = EmploymentSource.Authored,
                };
                if (withRegistry && withEmploymentRecord)
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

            public void WireAuthority()
            {
                Business.RuntimeState.ObligationAuthority = new FinancialObligationAuthority();
                Business.RuntimeState.ObligationIdRegistry = new EntityIdRegistry();
            }

            public void AddSlot(string workerId, int wageCents)
            {
                FieldInfo slotsField = typeof(BusinessRuntimeState).GetField(
                    "workerSlots", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(slotsField);
                var slots = (List<WorkerSlotState>)slotsField.GetValue(Business.RuntimeState);
                var slot = new WorkerSlotState("r19-slot", "Hand Slot", wageCents, true);
                slot.Assign(workerId, "Test Worker", wageCents);
                slots.Add(slot);
            }

            // DayIndex 10 is the escrow week (week 1); the sweep runs on
            // day 17 (week 2), so the escrow is always from a previous week.
            public void AccrueEscrow(string creditor, string reason)
            {
                Business.RuntimeState.PayrollDayIndex = 10;
                Business.RuntimeState.AccrueOperatingLiabilityCents(
                    1000, creditor, reason, cashAlreadyDeducted: true);
            }
        }

        private static bool DiagnosticNames(IReadOnlyList<string> diagnostics, string needle)
        {
            foreach (string line in diagnostics)
            {
                if (line != null && line.Contains(needle, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        [Test]
        public void T_R1_SettlesWhenWorkerGainsHousehold()
        {
            var f = new Fixture(withRegistry: true, withEmploymentRecord: true, cashCents: 9000);
            f.WireAuthority();
            // The escrow arose while the worker had no household: the lookup
            // returns -1 at accrual time. Cash 9000 models the business
            // AFTER the caller already deducted the 1000c wage.
            f.Business.RuntimeState.PersonHouseholdIdLookup = id => -1;
            f.AccrueEscrow("person:1",
                "wage escrow (weekly payroll) for P1 via r19-emp-1: worker has no household");
            Assert.AreEqual(1, f.Business.RuntimeState.NamedPayables.Count);
            BusinessNamedPayableRecord escrow = f.Business.RuntimeState.NamedPayables[0];
            Assert.IsTrue(escrow.CashAlreadyDeducted, "the escrow is a deducted wage.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(escrow.ObligationId),
                "the authority is wired, so the accrual posted an obligation id.");
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents,
                "accrual itself moves no business cash — it was already deducted.");

            // The worker gains a household; the sweep settles the escrow.
            f.Business.RuntimeState.PersonHouseholdIdLookup = f.Memberships.GetActiveHouseholdId;
            var diag = new List<string>();
            int settled = f.Business.RuntimeState.SettleEscrowedWagePayables(17, diag);

            Assert.AreEqual(1, settled, "one escrow settles.");
            Assert.AreEqual(0, f.Business.RuntimeState.NamedPayables.Count,
                "the settled payable is consumed.");
            Assert.AreEqual(1000, f.HouseholdBalance(),
                "the deducted wage reaches the worker's household.");
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents,
                "the sweep moves no business cash for a deducted wage.");
            Assert.IsTrue(DiagnosticNames(diag, "settled"),
                "the sweep reports the settlement: " + string.Join(" | ", diag));
            Assert.IsTrue(DiagnosticNames(f.Business.RuntimeState.LastWeeklyPayrollDiagnostics, "settled"),
                "the sweep writes its diagnostics to the runtime's own list (note 115 C7).");
        }

        [Test]
        public void T_R2_SecondSweepIsNoop_ShortfallUntouched()
        {
            var f = new Fixture(withRegistry: true, withEmploymentRecord: true, cashCents: 9000);
            f.WireAuthority();
            f.AccrueEscrow("person:1",
                "wage escrow (weekly payroll) for P1 via r19-emp-1: worker has no household");
            // A shortfall liability IS a named payable (note 115 C2) with
            // CashAlreadyDeducted=false and a non-escrow reason: the sweep
            // must leave it alone, excluded BY THE PREDICATE.
            f.Business.RuntimeState.AccrueOperatingLiabilityCents(
                1000, "person:1",
                "payroll eligibility lookup failed (lookup store unavailable) — weekly wage preserved as liability, not paid in cash",
                cashAlreadyDeducted: false);
            Assert.AreEqual(2, f.Business.RuntimeState.NamedPayables.Count);

            var diag = new List<string>();
            int first = f.Business.RuntimeState.SettleEscrowedWagePayables(17, diag);
            Assert.AreEqual(1, first, "only the escrow settles.");
            Assert.AreEqual(1, f.Business.RuntimeState.NamedPayables.Count);
            Assert.IsFalse(f.Business.RuntimeState.NamedPayables[0].CashAlreadyDeducted,
                "the record left standing is the shortfall liability.");
            Assert.AreEqual(1000, f.HouseholdBalance());

            var diag2 = new List<string>();
            int second = f.Business.RuntimeState.SettleEscrowedWagePayables(24, diag2);
            Assert.AreEqual(0, second, "the second sweep finds nothing to settle.");
            Assert.AreEqual(0, diag2.Count, "the second sweep reports nothing.");
            Assert.AreEqual(1, f.Business.RuntimeState.NamedPayables.Count,
                "the shortfall liability is still standing after both sweeps.");
            Assert.AreEqual(1000, f.HouseholdBalance(),
                "the household is not credited a second time.");
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents);
        }

        [Test]
        public void T_R3_NoHouseholdYet_StaysOpen()
        {
            var f = new Fixture(withRegistry: true, withEmploymentRecord: true, cashCents: 9000);
            f.WireAuthority();
            f.AccrueEscrow("person:1",
                "wage escrow (weekly payroll) for P1 via r19-emp-1: worker has no household");
            f.Business.RuntimeState.PersonHouseholdIdLookup = id => -1;

            var diag = new List<string>();
            int settled = f.Business.RuntimeState.SettleEscrowedWagePayables(17, diag);

            Assert.AreEqual(0, settled, "nothing settles without a household to credit.");
            Assert.AreEqual(1, f.Business.RuntimeState.NamedPayables.Count,
                "the payable stays open, re-attempted on later sweeps, never written off.");
            Assert.AreEqual(1000, f.Business.RuntimeState.NamedPayables[0].AmountCents,
                "the standing payable still holds the full wage.");
            Assert.AreEqual(0, f.HouseholdBalance(), "no ledger movement.");
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents);
            Assert.IsTrue(DiagnosticNames(diag, "P1"),
                "the diagnostic names the worker: " + string.Join(" | ", diag));
        }

        [Test]
        public void T_R4_EmptyObligationId_SkippedNeverSettledBySidePath()
        {
            var f = new Fixture(withRegistry: true, withEmploymentRecord: true, cashCents: 9000);
            // Authority deliberately unwired: the flush posts nothing.
            f.AccrueEscrow("person:1",
                "wage escrow (weekly payroll) for P1 via r19-emp-1: worker has no household");
            Assert.AreEqual("", f.Business.RuntimeState.NamedPayables[0].ObligationId,
                "setup: no authority, so the record carries no obligation id.");

            var diag = new List<string>();
            int settled = f.Business.RuntimeState.SettleEscrowedWagePayables(17, diag);

            Assert.AreEqual(0, settled, "nothing settles without an obligation id.");
            Assert.AreEqual(1, f.Business.RuntimeState.NamedPayables.Count);
            Assert.AreEqual("", f.Business.RuntimeState.NamedPayables[0].ObligationId,
                "the payable is still present with its id still empty.");
            Assert.AreEqual(0, f.HouseholdBalance());
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents);
            Assert.IsTrue(DiagnosticNames(diag, "obligation id"),
                "the diagnostic names the missing id: " + string.Join(" | ", diag));
        }

        [Test]
        public void T_R5_LegacySlotRef_ResolvesViaSpanMatchAfterEnsure()
        {
            var f = new Fixture(withRegistry: true, withEmploymentRecord: false, cashCents: 9000);
            f.WireAuthority();
            f.AddSlot("1", 1000);
            f.AccrueEscrow("person:1",
                "wage escrow (weekly payroll) for P1 via slot:r19_escrow:r19-slot: household ledger unwired");
            Assert.AreEqual(0, f.Employments.GetByEmployee(1).Count,
                "setup: no registry record for the worker yet — only the filled slot.");

            var diag = new List<string>();
            int settled = f.Business.RuntimeState.SettleEscrowedWagePayables(17, diag);

            Assert.AreEqual(1, settled, "the slot worker's escrow settles.");
            Assert.AreEqual(1, f.Employments.GetByEmployee(1).Count,
                "the ensure projected the slot worker's record.");
            Assert.AreEqual("r19_escrow:r19-slot", f.Employments.GetByEmployee(1)[0].Id,
                "the projected record carries the slot-derived id.");
            Assert.AreEqual(1000, f.HouseholdBalance(),
                "the wage reaches the household through the projected employment.");
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents);
        }

        [Test]
        public void T_R6_NoEmploymentResolvable_SkippedStaysOpen()
        {
            var f = new Fixture(withRegistry: true, withEmploymentRecord: false, cashCents: 9000);
            f.WireAuthority();
            // No registry record for P1 and no filled slot to project from.
            f.AccrueEscrow("person:1",
                "wage escrow (weekly payroll) for P1 via r19-emp-9: worker has no household");

            var diag = new List<string>();
            int settled = f.Business.RuntimeState.SettleEscrowedWagePayables(17, diag);

            Assert.AreEqual(0, settled, "nothing settles with no employment to credit through.");
            Assert.AreEqual(1, f.Business.RuntimeState.NamedPayables.Count,
                "the payable stays open — never deleted, no employment id invented.");
            Assert.AreEqual(0, f.HouseholdBalance());
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents);
            Assert.IsTrue(DiagnosticNames(diag, "employment"),
                "the diagnostic names the missing employment: " + string.Join(" | ", diag));
        }

        [Test]
        public void T_R7_CreditFailsAfterSettle_ReEscrowedIdentically()
        {
            var f = new Fixture(withRegistry: true, withEmploymentRecord: true, cashCents: 9000);
            f.WireAuthority();
            f.AccrueEscrow("person:1",
                "wage escrow (weekly payroll) for P1 via r19-emp-1: worker has no household");
            string oldObligationId = f.Business.RuntimeState.NamedPayables[0].ObligationId;
            Assert.IsFalse(string.IsNullOrWhiteSpace(oldObligationId));
            // The worker loses the household after the employment resolved:
            // the settle succeeds, then the credit cannot land.
            f.Business.RuntimeState.PersonHouseholdIdLookup = id => -1;

            var diag = new List<string>();
            int settled = f.Business.RuntimeState.SettleEscrowedWagePayables(17, diag);

            Assert.AreEqual(0, settled,
                "the sweep reports 0 NET settled — a settled-then-re-escrowed record counts as 0 (note 115 F3).");
            Assert.AreEqual(1, f.Business.RuntimeState.NamedPayables.Count,
                "one payable stands: the original was consumed and an identical one re-accrued.");
            BusinessNamedPayableRecord reEscrowed = f.Business.RuntimeState.NamedPayables[0];
            Assert.AreEqual("person:1", reEscrowed.Creditor, "same creditor.");
            Assert.AreEqual(1000, reEscrowed.AmountCents, "same amount.");
            Assert.IsTrue(reEscrowed.CashAlreadyDeducted, "still a deducted wage.");
            Assert.IsTrue(reEscrowed.Reason.Contains("re-escrowed from " + oldObligationId, StringComparison.Ordinal),
                "the re-accrued record references the original obligation (note 115 C6/Q3): " + reEscrowed.Reason);
            Assert.IsTrue(reEscrowed.Reason.StartsWith("wage escrow", StringComparison.Ordinal),
                "the reason prefix is preserved, so the next sweep still finds it.");
            Assert.AreNotEqual(oldObligationId, reEscrowed.ObligationId,
                "the re-escrow is a NEW obligation in the authority.");
            Assert.AreEqual(1000, f.Business.RuntimeState.AccruedLiabilityCents,
                "accrued liability nets zero across settle + re-accrue.");
            Assert.AreEqual(0, f.HouseholdBalance(), "no ledger movement.");
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents,
                "business cash unchanged across the whole sweep.");
            Assert.IsTrue(DiagnosticNames(diag, "re-escrowed"),
                "the diagnostic names the credit rejection and the re-escrow: " + string.Join(" | ", diag));
        }

        [Test]
        public void T_R8_HouseholdCreditor_ResolvesPersonFromReason()
        {
            var f = new Fixture(withRegistry: true, withEmploymentRecord: true, cashCents: 9000);
            f.WireAuthority();
            f.AccrueEscrow("household:11",
                "wage escrow (weekly payroll) for P1 via r19-emp-1: ledger rejected the credit");

            var diag = new List<string>();
            int settled = f.Business.RuntimeState.SettleEscrowedWagePayables(17, diag);

            Assert.AreEqual(1, settled, "the household-creditor escrow settles.");
            Assert.AreEqual(0, f.Business.RuntimeState.NamedPayables.Count);
            Assert.AreEqual(1000, f.HouseholdBalance(),
                "the person is parsed from the Reason's 'for P1' segment and the wage lands in H11.");
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents);
        }
    }
}
