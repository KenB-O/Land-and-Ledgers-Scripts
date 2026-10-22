using System.Collections.Generic;
using LandLedgers.Civic;
using LandLedgers.Economy.Financing;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// Pass IX WS6 (A1-10): the schoolhouse teacher is paid from the civic
    /// treasury for real — a full term wage debits the treasury, a shortfall
    /// becomes exactly one named Payable in the one debt authority, and
    /// repeat calls inside a settled term are no-ops.
    /// (The assignment record is exercised directly; SchoolhouseState in
    /// Domains/Civic delegates to it and is not compiled by the harness.)
    /// </summary>
    [TestFixture]
    public sealed class Pass9SchoolhouseTeacherPayTests
    {
        private static SchoolhouseAssignment Assignment()
        {
            var assignment = new SchoolhouseAssignment();
            assignment.Assign(teacherPersonId: 42, teacherHouseholdId: 7, displayName: "Teacher");
            return assignment;
        }

        [Test]
        public void TermWage_Is12000_ForSixtyDaysAt200()
        {
            Assert.AreEqual(60, SchoolhouseAssignment.SchoolTermDays);
            Assert.AreEqual(200, SchoolhouseAssignment.DailyWageCents);
            Assert.AreEqual(12000, SchoolhouseAssignment.SchoolTermWageCents);
        }

        [Test]
        public void PayTeacher_FundedTerm_DebitsTreasuryExactlyOnce()
        {
            var treasury = new CivicTreasuryService();
            treasury.TryCredit(20000, CivicLedgerEntryKind.TaxReceived, 0, "tax");
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            SchoolhouseAssignment assignment = Assignment();
            var diag = new List<string>();

            Assert.IsTrue(assignment.PayTeacher(treasury, obligations, ids, 10, diag));
            Assert.AreEqual(20000 - 12000, treasury.BalanceCents);
            Assert.AreEqual(12000, assignment.LastPaidFromTreasuryCents);
            Assert.AreEqual(0, obligations.Obligations.Count, "a funded term creates no debt");
            Assert.AreEqual(-12000, treasury.TotalForKind(CivicLedgerEntryKind.TeacherPayroll));

            // Repeat calls inside the same term are no-ops.
            Assert.IsFalse(assignment.PayTeacher(treasury, obligations, ids, 20, diag));
            Assert.IsFalse(assignment.PayTeacher(treasury, obligations, ids, 59, diag));
            Assert.AreEqual(8000, treasury.BalanceCents);

            // The next term settles again: 8000c from the treasury and the
            // 4000c remainder as a named Payable.
            Assert.IsTrue(assignment.PayTeacher(treasury, obligations, ids, 60, diag));
            Assert.AreEqual(0, treasury.BalanceCents);
            Assert.AreEqual(8000, assignment.LastPaidFromTreasuryCents);
            Assert.AreEqual(1, obligations.Obligations.Count);
            FinancialObligation payable = obligations.Find(assignment.LastPayableObligationId);
            Assert.IsNotNull(payable);
            Assert.AreEqual(4000, payable.TotalOutstandingCents);
        }

        [Test]
        public void PayTeacher_Shortfall_BecomesExactlyOneNamedPayable_NeverFictionalCash()
        {
            var treasury = new CivicTreasuryService();
            treasury.TryCredit(5000, CivicLedgerEntryKind.TaxReceived, 0, "tax");
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            SchoolhouseAssignment assignment = Assignment();
            var diag = new List<string>();

            Assert.IsTrue(assignment.PayTeacher(treasury, obligations, ids, 10, diag));
            Assert.AreEqual(0, treasury.BalanceCents, "the treasury paid what it held — no negative balance, no invented cash");
            Assert.AreEqual(5000, assignment.LastPaidFromTreasuryCents);
            Assert.AreEqual(1, obligations.Obligations.Count, "exactly one Payable for the shortfall");
            FinancialObligation payable = obligations.Find(assignment.LastPayableObligationId);
            Assert.IsNotNull(payable);
            Assert.AreEqual(FinancialObligationKind.Payable, payable.Kind);
            Assert.AreEqual("civic-school-fund", payable.Debtor);
            Assert.AreEqual("household:7", payable.Creditor);
            Assert.AreEqual(7000, payable.TotalOutstandingCents, "treasury debit + payable == the full term wage");
            Assert.AreEqual(12000, assignment.LastPaidFromTreasuryCents + payable.TotalOutstandingCents);

            // No second Payable from repeat calls in the same term.
            Assert.IsFalse(assignment.PayTeacher(treasury, obligations, ids, 30, diag));
            Assert.AreEqual(1, obligations.Obligations.Count);
        }

        [Test]
        public void PayTeacher_EmptyTreasury_FullWageBecomesPayable()
        {
            var treasury = new CivicTreasuryService();
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            SchoolhouseAssignment assignment = Assignment();
            Assert.IsTrue(assignment.PayTeacher(treasury, obligations, ids, 0, new List<string>()));
            Assert.AreEqual(0, treasury.BalanceCents);
            FinancialObligation payable = obligations.Find(assignment.LastPayableObligationId);
            Assert.IsNotNull(payable);
            Assert.AreEqual(12000, payable.TotalOutstandingCents);
        }

        [Test]
        public void PayTeacher_NoTeacherOrNoAuthority_RefusesWithoutSideEffects()
        {
            var treasury = new CivicTreasuryService();
            treasury.TryCredit(1000, CivicLedgerEntryKind.TaxReceived, 0, "tax");
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();

            var unassigned = new SchoolhouseAssignment();
            Assert.IsFalse(unassigned.PayTeacher(treasury, obligations, ids, 0, new List<string>()));
            Assert.AreEqual(1000, treasury.BalanceCents);

            // Shortfall with no debt authority: nothing is debited, term stays unsettled.
            SchoolhouseAssignment assignment = Assignment();
            Assert.IsFalse(assignment.PayTeacher(treasury, null, null, 0, new List<string>()));
            Assert.AreEqual(1000, treasury.BalanceCents, "no partial debit without the Payable to carry the rest");
            Assert.IsFalse(assignment.TermSettled);
            Assert.AreEqual(0, obligations.Obligations.Count);
        }
    }
}
