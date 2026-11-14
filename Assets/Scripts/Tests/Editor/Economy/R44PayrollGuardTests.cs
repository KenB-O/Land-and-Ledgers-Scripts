// R44 (note 111 §10, reviewed in note 115): the direct payroll path
// cannot double-pay a week. ResolveWeeklyPayroll has no internal guard
// and neither direct caller consulted LastPayrollSettledWeekIndex, so a
// second WeekChanged in one week double-paid every specialized business.
// The guard lives at the two direct callers (TryBeginWeeklyPayroll /
// StampWeeklyPayrollSettled on BusinessRuntimeState, mirroring
// WageSettlementService) and the marker persists via
// BusinessRuntimeSaveDto. These tests pin the guard contract; the two
// caller sites are harness-excluded and source-reviewed only (plan §8.5).
// The tests fail red at compile time before the slice (the guard methods
// do not exist at base).
//
// Fixture note (note 115 C5): Pass12C installs NO attendance provider —
// the weekly run pays through the null-provider path (the gate at
// BusinessRuntimeState.cs:2711 is skipped when the provider is null).
// No provider is installed here, and none returning false for the test
// week is ever installed.
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// R44 payroll guard (Agent 8 round-3 C4, ruled by Agent 3 note 111
    /// §10, plan reviewed in note 115). The guard key is dayIndex / 7
    /// (0-based), NEVER the 1-based clock week: the service stamps
    /// absoluteDayIndex / 7 (SimulationDrivers.cs:8718) while
    /// TimeManager.cs:734 computes week = dayIndex / 7 + 1, so stamping
    /// the clock week would put two key spaces in the same field (note
    /// 115 Q1 corrects note 111 §10 item 1). dayIndex &lt; 0 means no
    /// guard (-1 / 7 == 0 would collide with real week 0). The guard is
    /// deliberately NOT inside ResolveWeeklyPayroll (Pass12C header).
    /// </summary>
    [TestFixture]
    public sealed class R44PayrollGuardTests
    {
        private sealed class Fixture
        {
            public BusinessInstanceState Business;
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public EmploymentRelationship Employment;

            public Fixture(int cashCents)
            {
                BusinessProfileDefinition profile = BusinessProfileDefinition.CreateFallback(
                    BusinessType.Blacksmith, "Test Business");
                Business = BusinessInstanceState.Create(
                    "r44_guard", profile, 0, BusinessOwnerIdentity.Player());
                Business.RuntimeState.SetCurrentCashCents(cashCents);
                Business.RuntimeState.HouseholdLedgers = Ledgers;
                Business.RuntimeState.PersonHouseholdIdLookup = Memberships.GetActiveHouseholdId;
                Business.RuntimeState.EmploymentRegistry = Employments;
                Employment = new EmploymentRelationship
                {
                    Id = "r44-emp-1",
                    EmployeePersonId = 1,
                    EmployerBusinessId = Business.RuntimeState.BusinessId,
                    RoleDisplayName = "Hand",
                    Kind = EmploymentKind.Permanent,
                    LifecycleState = EmploymentLifecycleState.Active,
                    Compensation = CompensationTerms.FromWeeklyWage(1000, "test"),
                    StartDayIndex = 0,
                    Source = EmploymentSource.Authored,
                };
                Assert.IsNull(Employments.Register(Employment));
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
        }

        [Test]
        public void T_G1_GuardRefusesSecondBeginSameWeek()
        {
            var f = new Fixture(cashCents: 10000);
            Assert.IsTrue(f.Business.RuntimeState.TryBeginWeeklyPayroll(10),
                "the first begin in week 1 is allowed.");
            f.Business.RuntimeState.StampWeeklyPayrollSettled(10);
            Assert.IsFalse(f.Business.RuntimeState.TryBeginWeeklyPayroll(13),
                "day 13 is the same week as day 10 (13/7 == 10/7 == 1) — the second begin refuses.");
        }

        [Test]
        public void T_G2_DifferentWeekAllowed()
        {
            var f = new Fixture(cashCents: 10000);
            Assert.IsTrue(f.Business.RuntimeState.TryBeginWeeklyPayroll(10));
            f.Business.RuntimeState.StampWeeklyPayrollSettled(10);
            Assert.IsTrue(f.Business.RuntimeState.TryBeginWeeklyPayroll(17),
                "day 17 is week 2 (17/7 == 2) — a new week may run.");
        }

        [Test]
        public void T_G3_BeginWithoutStampDoesNotConsumeWeek()
        {
            var f = new Fixture(cashCents: 10000);
            Assert.IsTrue(f.Business.RuntimeState.TryBeginWeeklyPayroll(10));
            Assert.IsTrue(f.Business.RuntimeState.TryBeginWeeklyPayroll(10),
                "begin without stamp does not consume the week — only the stamp claims it.");
            f.Business.RuntimeState.StampWeeklyPayrollSettled(10);
            Assert.IsFalse(f.Business.RuntimeState.TryBeginWeeklyPayroll(10),
                "after the stamp the same week refuses.");
        }

        [Test]
        public void T_G4_UnwiredClock_NoGuardNoStamp()
        {
            var f = new Fixture(cashCents: 10000);
            Assert.IsTrue(f.Business.RuntimeState.TryBeginWeeklyPayroll(-1),
                "dayIndex -1 (clock unwired) runs as today, no guard.");
            f.Business.RuntimeState.StampWeeklyPayrollSettled(-1);
            Assert.AreEqual(-1, f.Business.RuntimeState.LastPayrollSettledWeekIndex,
                "stamping with an unwired clock leaves the marker at its initial -1.");
            Assert.IsTrue(f.Business.RuntimeState.TryBeginWeeklyPayroll(-1),
                "the unwired clock never refuses — -1 / 7 == 0 must not collide with real week 0.");
        }

        [Test]
        public void T_G5_WeekKeyIsDayIndexOverSeven_NotCurrentWeek()
        {
            var f = new Fixture(cashCents: 10000);
            f.Business.RuntimeState.StampWeeklyPayrollSettled(10);
            Assert.AreEqual(1, f.Business.RuntimeState.LastPayrollSettledWeekIndex,
                "stamping day 10 stamps week 1 (10/7), not the clock week 2 (10/7+1).");
            Assert.IsFalse(f.Business.RuntimeState.TryBeginWeeklyPayroll(13),
                "day 13 is week 1 — refused. A CurrentWeek key would have stamped week 2 and let day 13 run.");
            Assert.IsTrue(f.Business.RuntimeState.TryBeginWeeklyPayroll(17),
                "day 17 is week 2 — allowed.");

            // T-G5b cross-path pin (note 115 C3): the service stamps the
            // marker directly after settling day 10 (week 1). A direct-path
            // begin on day 13 must refuse against that service-stamped
            // marker. Under a CurrentWeek-keyed implementation begin(13)
            // computes week 2, which differs from the stamped 1, so this
            // assertion fails red — T-G5's first half passes under either
            // key, this block is the discriminating pin.
            f.Business.RuntimeState.LastPayrollSettledWeekIndex = 1;
            Assert.IsFalse(f.Business.RuntimeState.TryBeginWeeklyPayroll(13),
                "a service-stamped week 1 refuses a direct begin on day 13 — the two paths share one key space.");
        }

        [Test]
        public void T_G6_MarkerSurvivesSaveLoad()
        {
            var f = new Fixture(cashCents: 10000);
            f.Business.RuntimeState.StampWeeklyPayrollSettled(10);
            BusinessRuntimeSaveDto dto = f.Business.RuntimeState.CaptureSaveDto();
            Assert.AreEqual(1, dto.lastPayrollSettledWeekIndex,
                "the capture carries the stamped week.");

            BusinessRuntimeState restored = BusinessRuntimeState.FromSaveDto(dto, BusinessType.Blacksmith);
            Assert.AreEqual(1, restored.LastPayrollSettledWeekIndex,
                "the marker restores from the DTO.");
            Assert.IsFalse(restored.TryBeginWeeklyPayroll(13),
                "the restored guard still refuses the already-settled week.");

            // An older save predates the additive field, so it restores
            // the default -1 (no week settled) — the safe direction: the
            // first post-Continue weekly run pays once, as today.
            dto.lastPayrollSettledWeekIndex = -1;
            BusinessRuntimeState fromOldSave = BusinessRuntimeState.FromSaveDto(dto, BusinessType.Blacksmith);
            Assert.AreEqual(-1, fromOldSave.LastPayrollSettledWeekIndex,
                "a DTO with the field at its default restores -1.");
            Assert.IsTrue(fromOldSave.TryBeginWeeklyPayroll(13),
                "with no week settled, the first run after a Continue is allowed.");
        }

        [Test]
        public void T_G7_DuplicateWeeklyRunPaysOnce()
        {
            var f = new Fixture(cashCents: 10000);
            // The null-provider path pays: no attendance provider is
            // installed (note 115 C5), so the attendance gate is skipped
            // and the Active employment's agreed wage is due.
            f.Business.RuntimeState.PayrollDayIndex = 10;

            Assert.IsTrue(f.Business.RuntimeState.TryBeginWeeklyPayroll(10),
                "the first begin in week 1 is allowed.");
            f.Business.RuntimeState.ResolveWeeklyPayroll();
            f.Business.RuntimeState.StampWeeklyPayrollSettled(10);
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents,
                "the weekly run paid the 1000c wage once.");
            Assert.AreEqual(1000, f.HouseholdBalance(),
                "the household holds exactly the week's wage.");
            Assert.AreEqual(1000, f.Business.RuntimeState.LastWeeklyPayrollCents);

            // The duplicate WeekChanged: the guard refuses, so the test
            // models the call-site shape and skips the second run — the
            // guard logic is what is pinned; the call sites are
            // source-reviewed (plan §8.5).
            Assert.IsFalse(f.Business.RuntimeState.TryBeginWeeklyPayroll(10),
                "the second begin in the same week refuses.");
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents,
                "business cash moved exactly 1000c once — no second payment.");
            Assert.AreEqual(1000, f.HouseholdBalance(),
                "the household holds exactly 1000c — no second payment.");
        }
    }
}
