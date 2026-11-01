using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// Pass XI WS1 (PASS9-006): the domestic tick watermark and its
    /// save/load reset. Loading an earlier-time save in the same session
    /// used to stall DomesticLaborDriver.AdvanceTick until game time
    /// passed the old watermark. Resetting the watermark to the loaded
    /// clock restores advancement on the next tick while preserving all
    /// completed work and person-time reservations, which live outside
    /// the gate.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws1DomesticTickGateTests
    {
        [Test]
        public void Gate_OnlyForwardMotionAdvances()
        {
            var gate = new DomesticTickGate();
            Assert.IsTrue(gate.ShouldAdvance(0f), "fresh gate starts at -1");
            gate.MarkAdvanced(1000f);
            Assert.IsFalse(gate.ShouldAdvance(1000f), "a repeated tick never re-advances");
            Assert.IsFalse(gate.ShouldAdvance(999f), "a backwards tick never advances");
            Assert.IsTrue(gate.ShouldAdvance(1001f), "forward motion advances");
        }

        [Test]
        public void Gate_ResetToEarlierLoadedTime_UnblocksTheNextTick()
        {
            var gate = new DomesticTickGate();
            gate.MarkAdvanced(200 * 86400f);
            Assert.IsFalse(gate.ShouldAdvance(100 * 86400f + 60f), "old watermark stalls the loaded session");

            gate.Reset(100 * 86400f);
            Assert.AreEqual(100 * 86400f, gate.PreviousTickGameSeconds);
            Assert.IsTrue(gate.ShouldAdvance(100 * 86400f + 60f), "reset to the loaded clock unblocks the next tick");
        }

        [Test]
        public void LoadWithEarlierTime_PreservedPendingWork_ContinuesDeterministically()
        {
            // Plan a day of domestic work, then save the driver mid-day
            // (pending work preserved) and load it into a fresh driver.
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            var schedules = new PersonScheduleTracker();
            var domestic = new DomesticLaborService();
            var dueEvents = new SimpleDueEventScheduler();
            var diag = new List<string>();
            DomesticTaskCatalog.RegisterAll(tasks, diag);

            var driver = new DomesticLaborDriver();
            const int day = 100;
            driver.DriveDay(new DomesticLaborDriver.DomesticDayInputs
            {
                HouseholdId = 7,
                MemberIds = new List<int> { 3, 4 },
                AgeBandFor = _ => AgeBand.Adult18Plus,
                MealsAtHomeCount = 2,
                ColdSeason = false,
                HasAnimals = false,
            }, day, tasks, budgets, schedules, domestic, dueEvents, diag);
            Assert.IsTrue(driver.Pending.Count > 0, "day planning must produce pending work");

            var loaded = new DomesticLaborDriver();
            loaded.LoadFromSaveDto(driver.CaptureSaveDto());
            Assert.AreEqual(driver.Pending.Count, loaded.Pending.Count,
                "load preserves pending work exactly, no work lost and none invented");

            // The session watermark sat at a later time (day 200). Without
            // a reset the loaded day-100 tick would stall. Reset to the
            // loaded clock, then advance at the first pending window.
            var gate = new DomesticTickGate();
            gate.MarkAdvanced(200 * 86400f);
            float windowTime = day * 86400f + loaded.Pending[0].StartMinuteOfDay * 60f;
            Assert.IsFalse(gate.ShouldAdvance(windowTime), "pre-reset gate stalls the loaded tick");
            gate.Reset(day * 86400f);
            Assert.IsTrue(gate.ShouldAdvance(windowTime), "post-reset gate admits the loaded tick");

            int pendingBefore = loaded.Pending.Count;
            var tickDiag = new List<string>();
            loaded.AdvanceTick(windowTime, tasks, budgets, schedules, domestic, dueEvents, tickDiag);
            Assert.IsTrue(loaded.Pending.Count < pendingBefore,
                "preserved pending work starts on the first tick after the reset\n" + string.Join("\n", tickDiag));
        }
    }
}
