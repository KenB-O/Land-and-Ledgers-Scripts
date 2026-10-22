using System.Collections.Generic;
using LandLedgers.Civic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>Pass IX review R-02: teacher pay credits the teacher's
    /// household — treasury debit == household credit + payable principal,
    /// exactly, in the same call.</summary>
    [TestFixture]
    public sealed class Pass9ReviewFixTeacherPayTests
    {
        private static SchoolhouseAssignment Assignment()
        {
            var assignment = new SchoolhouseAssignment();
            assignment.Assign(teacherPersonId: 42, teacherHouseholdId: 7, displayName: "Teacher");
            return assignment;
        }

        [Test]
        public void FundedTerm_TreasuryMinus12000_TeacherHouseholdPlus12000_NoPayable()
        {
            var treasury = new CivicTreasuryService();
            treasury.TryCredit(20000, CivicLedgerEntryKind.TaxReceived, 0, "tax");
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            var household = new HouseholdLedger(7);
            SchoolhouseAssignment assignment = Assignment();
            var diag = new List<string>();

            Assert.IsTrue(assignment.PayTeacher(treasury, obligations, ids, 0, diag, household));

            Assert.AreEqual(8000, treasury.BalanceCents, "treasury -12000");
            Assert.AreEqual(12000, household.GetBalanceCents(), "teacher household +12000 — the funded portion is PAID, not destroyed");
            Assert.AreEqual(0, obligations.Obligations.Count, "no payable for a funded term");
        }

        [Test]
        public void ShortfallTerm_HouseholdCreditedFundedPart_ExactlyOnePayable_RepeatCallNoOp()
        {
            var treasury = new CivicTreasuryService();
            treasury.TryCredit(5000, CivicLedgerEntryKind.TaxReceived, 0, "tax");
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            var household = new HouseholdLedger(7);
            SchoolhouseAssignment assignment = Assignment();
            var diag = new List<string>();

            Assert.IsTrue(assignment.PayTeacher(treasury, obligations, ids, 10, diag, household));

            Assert.AreEqual(0, treasury.BalanceCents, "treasury paid what it held");
            Assert.AreEqual(5000, household.GetBalanceCents(), "household credited the funded part");
            Assert.AreEqual(1, obligations.Obligations.Count, "exactly one Payable for the rest");
            FinancialObligation payable = obligations.Find(assignment.LastPayableObligationId);
            Assert.IsNotNull(payable);
            Assert.AreEqual(7000, payable.TotalOutstandingCents);
            Assert.AreEqual(12000, household.GetBalanceCents() + payable.TotalOutstandingCents,
                "household credit + payable principal == the term wage, exactly");

            // Repeat call in the same term changes nothing.
            Assert.IsFalse(assignment.PayTeacher(treasury, obligations, ids, 30, diag, household));
            Assert.AreEqual(0, treasury.BalanceCents);
            Assert.AreEqual(5000, household.GetBalanceCents());
            Assert.AreEqual(1, obligations.Obligations.Count);
        }

        [Test]
        public void FundedTerm_WithoutTeacherLedger_RefusesAndDebitsNothing()
        {
            var treasury = new CivicTreasuryService();
            treasury.TryCredit(20000, CivicLedgerEntryKind.TaxReceived, 0, "tax");
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            SchoolhouseAssignment assignment = Assignment();

            Assert.IsFalse(assignment.PayTeacher(treasury, obligations, ids, 0, new List<string>(), null),
                "no household to credit — the debit must not happen (that was the money-destroying path)");
            Assert.AreEqual(20000, treasury.BalanceCents);
            Assert.IsFalse(assignment.TermSettled);
            Assert.AreEqual(0, obligations.Obligations.Count);
        }
    }
}
