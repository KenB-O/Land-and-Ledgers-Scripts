using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Civic
{
    /// <summary>
    /// Pass IX WS6 (A1-10): the schoolhouse teacher assignment as a real
    /// employment record — who teaches, which household they belong to,
    /// and whether the current term's wage has been settled. Before this
    /// pass the teacher was "paid" only through PersonState.
    /// RecordVisibleWorkerRole (a visible profile: no cash, no fund, no
    /// employment record).
    ///
    /// Pay rule: one term = 60 days at 200c/day = <see cref="SchoolTermWageCents"/>.
    /// When a term is due, the wage is debited from the civic treasury;
    /// any shortfall the treasury cannot cover becomes exactly ONE named
    /// Payable in the one debt authority (debtor "civic-school-fund",
    /// creditor = the teacher's household) — never fictional cash. Repeat
    /// calls inside a settled term are no-ops.
    ///
    /// Placement note: lives with the finance authorities so the Roslyn
    /// harness (which does not compile Domains/Civic) can exercise it;
    /// SchoolhouseState (Domains/Civic) delegates to it. Namespace keeps
    /// civic ownership.
    /// </summary>
    [Serializable]
    public sealed class SchoolhouseAssignment
    {
        public const int SchoolTermDays = 60;
        public const int DailyWageCents = 200;
        public const int SchoolTermWageCents = SchoolTermDays * DailyWageCents; // 12000
        public const string SchoolFundDebtor = "civic-school-fund";

        public int TeacherPersonId = -1;
        public int TeacherHouseholdId = -1;
        public string TeacherDisplayName = string.Empty;
        /// <summary>Start day of the term last settled (pay or payable); -1 = none settled yet.</summary>
        public int TermStartDayIndex = -1;
        public bool TermSettled;
        public int LastPaidFromTreasuryCents;
        public string LastPayableObligationId = string.Empty;

        public bool HasTeacher => TeacherPersonId >= 0 && TeacherHouseholdId >= 0;

        public void Assign(int teacherPersonId, int teacherHouseholdId, string displayName)
        {
            TeacherPersonId = teacherPersonId;
            TeacherHouseholdId = teacherHouseholdId;
            TeacherDisplayName = displayName ?? string.Empty;
            TermStartDayIndex = -1;
            TermSettled = false;
            LastPaidFromTreasuryCents = 0;
            LastPayableObligationId = string.Empty;
        }

        public void Clear()
        {
            TeacherPersonId = -1;
            TeacherHouseholdId = -1;
            TeacherDisplayName = string.Empty;
            TermStartDayIndex = -1;
            TermSettled = false;
            LastPaidFromTreasuryCents = 0;
            LastPayableObligationId = string.Empty;
        }

        /// <summary>The 60-day term containing a day index (terms are day-aligned blocks).</summary>
        public static int TermStartFor(int dayIndex)
        {
            int day = Math.Max(0, dayIndex);
            return day - (day % SchoolTermDays);
        }

        /// <summary>
        /// Settles the current term's wage. Returns true when this call
        /// settled the term (treasury debit and/or named Payable); false
        /// for no-ops (no teacher, term already settled) and refusals
        /// (shortfall with no debt authority supplied — nothing is debited
        /// in that case, so a later call with the authority can settle).
        /// </summary>
        public bool PayTeacher(
            CivicTreasuryService treasury, FinancialObligationAuthority obligations,
            EntityIdRegistry ids, int dayIndex, List<string> diag = null,
            HouseholdLedger teacherHouseholdLedger = null)
        {
            diag = diag ?? new List<string>();
            if (!HasTeacher)
            {
                diag.Add("SchoolhouseAssignment.PayTeacher: no teacher with a household is assigned — no pay due.");
                return false;
            }
            int termStart = TermStartFor(dayIndex);
            if (TermSettled && TermStartDayIndex == termStart)
                return false; // repeat call inside a settled term: no-op, exactly one settlement per term

            int available = treasury != null ? treasury.BalanceCents : 0;
            int fromTreasury = Math.Min(available, SchoolTermWageCents);
            int shortfall = SchoolTermWageCents - fromTreasury;
            if (shortfall > 0 && (obligations == null || ids == null))
            {
                diag.Add($"SchoolhouseAssignment.PayTeacher: treasury covers {fromTreasury}c of the {SchoolTermWageCents}c term wage " +
                    "but no obligation authority/registry was supplied for the shortfall Payable — nothing debited, term not settled.");
                return false;
            }

            // R-02: the funded portion must CREDIT the teacher's household
            // through HouseholdLedger in this same call — a treasury debit
            // with no recipient is money destruction. No ledger for the
            // teacher's household, no debit.
            if (fromTreasury > 0 && teacherHouseholdLedger == null)
            {
                diag.Add($"SchoolhouseAssignment.PayTeacher: treasury covers {fromTreasury}c of the term wage but no " +
                    $"household ledger for teacher household {TeacherHouseholdId} was supplied to credit it to — nothing debited, term not settled.");
                return false;
            }

            if (fromTreasury > 0)
            {
                if (!treasury.TryDebit(fromTreasury, CivicLedgerEntryKind.TeacherPayroll, dayIndex,
                    $"school term wage — teacher household {TeacherHouseholdId}, term from day {termStart}", diag))
                    return false; // balance changed between check and debit; do not book a Payable against a failed debit
            }

            string payableId = string.Empty;
            if (shortfall > 0)
            {
                FinancialObligation payable = obligations.Create(ids, FinancialObligationKind.Payable,
                    SchoolFundDebtor, "household:" + TeacherHouseholdId, shortfall, dayIndex,
                    "school term wage shortfall — civic treasury could not cover the term",
                    $"teacher pay — school term from day {termStart}");
                if (payable == null)
                {
                    // Compensate the debit (R-14 ordering flaw): the term
                    // is not settled, so the treasury keeps its cash.
                    if (fromTreasury > 0)
                    {
                        treasury.TryCredit(fromTreasury, CivicLedgerEntryKind.OtherCredit, dayIndex,
                            $"school term wage rollback — Payable refused, term from day {termStart} unsettled", diag);
                    }

                    diag.Add("SchoolhouseAssignment.PayTeacher: the obligation authority refused the shortfall Payable — debit rolled back, term not marked settled.");
                    return false;
                }
                payableId = payable.ObligationId;
            }

            if (fromTreasury > 0)
            {
                // The household credit leg: exactly the debited amount, so
                // treasury debit == household credit + payable principal.
                string creditRefusal = teacherHouseholdLedger.RecordInflow(dayIndex, fromTreasury,
                    HouseholdIncomeSource.WageEmployment, $"schoolhouse-term:{termStart}",
                    $"teacher pay — school term from day {termStart} (civic treasury)", SchoolFundDebtor);
                if (creditRefusal != null)
                {
                    diag.Add($"SchoolhouseAssignment.PayTeacher: household credit refused ({creditRefusal}) — " +
                        "this cannot happen for a positive documented inflow; flagged loudly.");
                    return false;
                }
            }

            TermStartDayIndex = termStart;
            TermSettled = true;
            LastPaidFromTreasuryCents = fromTreasury;
            LastPayableObligationId = payableId;
            diag.Add($"SchoolhouseAssignment.PayTeacher: term from day {termStart} settled — {fromTreasury}c from civic treasury" +
                (fromTreasury > 0 ? $" credited to teacher household {TeacherHouseholdId}" : "") +
                (shortfall > 0 ? $", {shortfall}c as Payable '{payableId}' (debtor {SchoolFundDebtor}, creditor household:{TeacherHouseholdId})" : "") + ".");
            return true;
        }
    }
}
