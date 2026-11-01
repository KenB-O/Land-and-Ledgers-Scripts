using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Pass VIII Worker 7: no automatic employee attendance from a mere
    /// employment record. BusinessRuntimeState.WeeklyAttendanceProvider lets
    /// the payroll authority pay only weeks actually worked:
    /// - A worker with recorded attendance is paid (legacy path unchanged).
    /// - A worker with no attendance earns no wage: nothing is paid AND no
    ///   employer liability accrues (the wage was never earned).
    /// - The skip is loud in the payroll diagnostics.
    /// - WageSettlementService's PayrollEvent replay honors the same gate,
    ///   so the read model never credits a worker the business did not pay.
    /// - Null provider = legacy behavior exactly (no behavior change).
    /// </summary>
    [TestFixture]
    public sealed class Pass8AttendancePayrollTests
    {
        private sealed class Fixture
        {
            public BusinessInstanceState Business;
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public HashSet<string> Attended = new HashSet<string>();
            public List<string> Diag = new List<string>();

            public Fixture()
            {
                BusinessProfileDefinition profile = BusinessProfileDefinition.CreateFallback(
                    BusinessType.Blacksmith, "Test Business");
                Business = BusinessInstanceState.Create(
                    "test_attendance_payroll", profile, 0, BusinessOwnerIdentity.Player());
                Business.RuntimeState.EmploymentRegistry = Employments;
                Business.RuntimeState.HouseholdLedgers = Ledgers;
                Business.RuntimeState.PersonHouseholdIdLookup = Memberships.GetActiveHouseholdId;
                Business.RuntimeState.SetCurrentCashCents(10000);
                Business.RuntimeState.WeeklyAttendanceProvider =
                    (employmentId, weekIndex) => Attended.Contains(employmentId);
            }

            public EmploymentRelationship Hire(int personId, int householdId, int weeklyWageCents)
            {
                var employment = new EmploymentRelationship
                {
                    Id = $"test-emp-{personId}",
                    EmployeePersonId = personId,
                    EmployerBusinessId = Business.RuntimeState.BusinessId,
                    RoleDisplayName = "Hand",
                    Kind = EmploymentKind.Permanent,
                    LifecycleState = EmploymentLifecycleState.Active,
                    Compensation = CompensationTerms.FromWeeklyWage(weeklyWageCents, "test"),
                    StartDayIndex = 0,
                    Source = EmploymentSource.Authored,
                };
                Assert.IsNull(Employments.Register(employment));
                var membership = new HouseholdMembership
                {
                    MembershipId = personId,
                    PersonId = personId,
                    HouseholdId = householdId,
                    Lifecycle = HouseholdMembershipLifecycle.Active,
                    StartDayIndex = 0,
                    EndDayIndex = -1,
                    Source = HouseholdMembershipSource.Authored,
                };
                Assert.IsNull(Memberships.Register(membership));
                Ledgers.GetOrCreate(householdId);
                return employment;
            }

            public int HouseholdBalance(int householdId) =>
                Ledgers.GetOrCreate(householdId).GetBalanceCents();
        }

        [Test]
        public void AttendedWorker_IsPaid_Normally()
        {
            var f = new Fixture();
            EmploymentRelationship emp = f.Hire(5, 50, 800);
            f.Attended.Add(emp.Id);
            f.Business.RuntimeState.PayrollDayIndex = 6; // week 0

            f.Business.RuntimeState.ResolveWeeklyPayroll();

            Assert.AreEqual(9200, f.Business.RuntimeState.CurrentCashCents);
            Assert.AreEqual(800, f.HouseholdBalance(50), "the worked wage reaches the household ledger");
        }

        [Test]
        public void AbsentWorker_EarnsNothing_AndAccruesNoLiability()
        {
            var f = new Fixture();
            EmploymentRelationship emp = f.Hire(5, 50, 800);
            // No attendance recorded for emp.Id.
            f.Business.RuntimeState.PayrollDayIndex = 6; // week 0
            int liabilityBefore = f.Business.RuntimeState.AccruedLiabilityCents;

            f.Business.RuntimeState.ResolveWeeklyPayroll();

            Assert.AreEqual(10000, f.Business.RuntimeState.CurrentCashCents, "no cash moved");
            Assert.AreEqual(0, f.HouseholdBalance(50), "no wage credited");
            Assert.AreEqual(liabilityBefore, f.Business.RuntimeState.AccruedLiabilityCents,
                "an unearned wage is not employer debt");
            Assert.AreEqual(EmploymentLifecycleState.Active, emp.LifecycleState,
                "absence does not end the employment by itself");
            StringAssert.Contains("no attendance",
                string.Join(" ", f.Business.RuntimeState.LastWeeklyPayrollDiagnostics.ToArray()),
                "the skip is loud");
        }

        [Test]
        public void MixedWeek_AttendedPaid_AbsentSkipped()
        {
            var f = new Fixture();
            EmploymentRelationship present = f.Hire(5, 50, 800);
            EmploymentRelationship absent = f.Hire(6, 60, 700);
            f.Attended.Add(present.Id);
            f.Business.RuntimeState.PayrollDayIndex = 6;

            f.Business.RuntimeState.ResolveWeeklyPayroll();

            Assert.AreEqual(9200, f.Business.RuntimeState.CurrentCashCents, "only the worked wage left the till");
            Assert.AreEqual(800, f.HouseholdBalance(50));
            Assert.AreEqual(0, f.HouseholdBalance(60));
        }

        [Test]
        public void NullProvider_LegacyBehavior_Unchanged()
        {
            var f = new Fixture();
            f.Business.RuntimeState.WeeklyAttendanceProvider = null;
            f.Hire(5, 50, 800);
            f.Business.RuntimeState.PayrollDayIndex = 6;

            f.Business.RuntimeState.ResolveWeeklyPayroll();

            Assert.AreEqual(9200, f.Business.RuntimeState.CurrentCashCents,
                "without the seam, the employment record still decides");
            Assert.AreEqual(800, f.HouseholdBalance(50));
        }

        [Test]
        public void SettlementReplay_HonorsAttendance()
        {
            var f = new Fixture();
            EmploymentRelationship present = f.Hire(5, 50, 800);
            EmploymentRelationship absent = f.Hire(6, 60, 700);
            f.Attended.Add(present.Id);
            f.Business.RuntimeState.PayrollDayIndex = 6;

            var service = new WageSettlementService();
            string result = service.SettleWeeklyPayroll(
                f.Business.RuntimeState, f.Employments, 0, 6, f.Diag);

            Assert.AreEqual("settled", result);
            PayrollEvent evt = service.Events[service.Events.Count - 1];
            Assert.AreEqual(2, evt.Entries.Count);
            PayrollEntry presentEntry = evt.Entries.Find(e => e.EmploymentId == present.Id);
            PayrollEntry absentEntry = evt.Entries.Find(e => e.EmploymentId == absent.Id);
            Assert.IsNotNull(presentEntry);
            Assert.IsNotNull(absentEntry);
            Assert.IsTrue(presentEntry.Paid, "the worked week reads paid");
            Assert.IsFalse(absentEntry.Paid, "the unworked week reads unpaid — the event never credits what the till did not pay");
            Assert.AreEqual(800, evt.TotalPaidCents);
        }
    }
}
