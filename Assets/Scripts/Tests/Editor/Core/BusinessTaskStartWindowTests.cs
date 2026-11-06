// Slice F2 (2026-11-06, Objectives 18 + 45): business-task start-window
// reproduction tests.
//
// These tests reproduce the Nov 6 console mechanism — an Assigned task
// whose short reservation window is jumped over by the sparse ShortTick
// windows at 100x, so it never starts — against the pure decision seam
// (BusinessTaskStartWindow.Resolve) that SimulationDrivers.
// BeginScheduledBusinessPersonTask calls. SimulationDrivers.cs itself is
// harness-excluded; the harness pins this seam, the wiring is
// source-reviewed.
//
// Tick model used here (verified in source): ShortTick windows are 15
// game-minutes wide (SimulationTimeSettings.shortTickIntervalGameMinutes),
// and every tick fired in one frame reports the same TimeOfDay01
// (TimeManager.ProcessShortTicks), so at 100x (one game day per 0.6 real
// seconds, ~40 game-minutes per frame) consecutive evaluated tick windows
// are ~40 minutes apart. At 1x the windows tile the day contiguously.
//
// Failing-first tests: Resolve_JumpedOverWindow_CatchesUp and
// Resolve_100xJumpSequence_ReproducesStall_ThenCatchesUp assert the
// CatchUp outcome, which Resolve does not return until the fix lands.
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Core
{
    [TestFixture]
    public sealed class BusinessTaskStartWindowTests
    {
        // T44 (day 6): 10-minute hitch reservation [521, 531].
        private const int HitchStart = 521;
        private const int HitchEnd = 531;

        // T168: 1-minute dispatch reservation. The console shows T168
        // starting at minute 510 on day 19, so its own window happened to
        // be hit that day; the test pins the miss shape for a 1-minute
        // window that falls in a tick gap — [522, 523] sits between the
        // [505, 520] and [545, 560] tick windows, so the same sparse
        // sequence never overlaps it and only the catch-up can start it.
        private const int DispatchStart = 522;
        private const int DispatchEnd = 523;

        // Authored ShortTick width in game-minutes.
        private const int TickWidth = 15;

        [Test]
        public void Resolve_OverlappingWindow_Starts()
        {
            Assert.AreEqual(BusinessTaskStartDecision.Start,
                BusinessTaskStartWindow.Resolve(HitchStart, HitchEnd, 520, 535));
            // Tick window ending exactly on the reservation start does not
            // overlap; the next minute does.
            Assert.AreEqual(BusinessTaskStartDecision.Start,
                BusinessTaskStartWindow.Resolve(HitchStart, HitchEnd, 521, 536));
        }

        [Test]
        public void Resolve_FutureWindow_Waits()
        {
            Assert.AreEqual(BusinessTaskStartDecision.Wait,
                BusinessTaskStartWindow.Resolve(HitchStart, HitchEnd, 400, 415));
            // Tick ending exactly on the reservation start has not reached
            // the window yet.
            Assert.AreEqual(BusinessTaskStartDecision.Wait,
                BusinessTaskStartWindow.Resolve(HitchStart, HitchEnd, 506, 521));
        }

        [Test]
        public void Resolve_JumpedOverWindow_CatchesUp()
        {
            // First tick whose window starts at or after the reservation
            // end: the window was jumped over entirely. The old funnel
            // waited here forever; the fix must catch up.
            Assert.AreEqual(BusinessTaskStartDecision.CatchUp,
                BusinessTaskStartWindow.Resolve(HitchStart, HitchEnd, 545, 560));
            Assert.AreEqual(BusinessTaskStartDecision.CatchUp,
                BusinessTaskStartWindow.Resolve(HitchStart, HitchEnd, 531, 546));
        }

        [Test]
        public void Resolve_100xJumpSequence_ReproducesStall_ThenCatchesUp()
        {
            // Reproduction of the T44 gap: tick ends stepping ~40
            // game-minutes per frame at 100x (15-minute windows). Under
            // the old overlap-only rule no tick ever starts the task — the
            // [521, 531] reservation falls in the gap between the [505, 520]
            // and [545, 560] tick windows — and the task would sit Assigned
            // for the rest of the day.
            int[] tickEnds = { 480, 520, 560, 600, 640, 680, 720, 760, 800, 840, 880, 920, 960, 1000, 1040, 1080, 1120, 1160, 1200, 1240, 1280, 1320, 1360, 1400, 1440 };
            bool anyStart = false;
            BusinessTaskStartDecision firstPastWindow = BusinessTaskStartDecision.Wait;
            foreach (int end in tickEnds)
            {
                BusinessTaskStartDecision decision =
                    BusinessTaskStartWindow.Resolve(HitchStart, HitchEnd, end - TickWidth, end);
                if (decision == BusinessTaskStartDecision.Start) anyStart = true;
                if (decision == BusinessTaskStartDecision.CatchUp && firstPastWindow == BusinessTaskStartDecision.Wait)
                    firstPastWindow = decision;
            }
            Assert.IsTrue(!anyStart,
                "100x tick windows must never overlap the [521, 531] reservation under the old rule — that non-overlap IS the stall.");
            Assert.AreEqual(BusinessTaskStartDecision.CatchUp, firstPastWindow,
                "The first tick past the reservation window must catch up instead of waiting out the day.");
        }

        [Test]
        public void Resolve_100xJumpSequence_DispatchOneMinuteWindow_AlsoCatchesUp()
        {
            // T168's 1-minute window in a tick gap: the same family as
            // T44, 3-day wait. A 1-minute window is an even smaller target
            // for the sparse tick windows.
            int[] tickEnds = { 480, 520, 560, 600, 640, 680, 720, 760, 800, 840, 880, 920, 960, 1000, 1040, 1080, 1120, 1160, 1200, 1240, 1280, 1320, 1360, 1400, 1440 };
            bool anyStart = false;
            bool anyCatchUp = false;
            foreach (int end in tickEnds)
            {
                BusinessTaskStartDecision decision =
                    BusinessTaskStartWindow.Resolve(DispatchStart, DispatchEnd, end - TickWidth, end);
                if (decision == BusinessTaskStartDecision.Start) anyStart = true;
                if (decision == BusinessTaskStartDecision.CatchUp) anyCatchUp = true;
            }
            Assert.IsTrue(!anyStart, "The 1-minute dispatch window in a tick gap is never overlapped by 100x tick windows.");
            Assert.IsTrue(anyCatchUp, "A tick past the 1-minute window must catch up.");
        }

        [Test]
        public void Resolve_1xDenseSequence_StartsByOverlap()
        {
            // At 1x the 15-minute tick windows tile the day contiguously
            // (ends 0, 15, 30, ...), so the same [521, 531] reservation is
            // always overlapped: the defect is speed-dependent and does not
            // reproduce at 1x.
            bool anyStart = false;
            for (int end = TickWidth; end <= 1440; end += TickWidth)
            {
                if (BusinessTaskStartWindow.Resolve(HitchStart, HitchEnd, end - TickWidth, end)
                    == BusinessTaskStartDecision.Start)
                {
                    anyStart = true;
                    break;
                }
            }
            Assert.IsTrue(anyStart, "At 1x the tiling tick windows must overlap the [521, 531] reservation.");
        }
    }

    [TestFixture]
    public sealed class WorkCapacityBudgetProbeTests
    {
        // Slice F2 probe (Plan 03 §6, Note-21): what capacity 25% means
        // against the daily work budget. WorkCapacityFactor is nutrition x
        // health (DailyNeedsService.cs:38); the budget default is 600
        // minutes/day (WorkTimeBudget.DefaultDailyWorkMinutes). P7 at 25%
        // capacity therefore has 150 minutes/day — the "0 of 150 daily work
        // minutes remaining (capacity 25%)" in the Nov 6 console, day 5.
        // The probe pins the arithmetic; any change to the coupling itself
        // needs a Kennedy decision (Obj 45) and is NOT made here.
        [Test]
        public void WorkCapacity_25Percent_Means150Of600Minutes()
        {
            Assert.AreEqual(600, WorkTimeBudget.DefaultDailyWorkMinutes);
            var budget = new WorkTimeBudget(
                LandLedgers.Primitives.EntityId.For(LandLedgers.Primitives.EntityKind.Person, 7),
                WorkTimeBudget.DefaultDailyWorkMinutes);
            budget.SetCapacityMultiplier(0.25f);
            Assert.AreEqual(150, budget.EffectiveBudgetMinutes);
            Assert.AreEqual(150, budget.MinutesRemaining);
        }

        [Test]
        public void WorkCapacity_150MinuteBudget_CannotCoverDomesticDayPlusHitch()
        {
            // The domestic routine alone (fuel 41 + chores 41 + meal-prep
            // 61 = 143) nearly exhausts a 150-minute day; adding the
            // 10-minute hitch (153) exceeds it. This is why day-5 assignment
            // refused on budget — and why the budget explains day 5 only:
            // once T44 was Assigned on day 6 its 10 minutes were already
            // committed, so the budget cannot explain the 10-day
            // assignment-to-start gap.
            var budget = new WorkTimeBudget(
                LandLedgers.Primitives.EntityId.For(LandLedgers.Primitives.EntityKind.Person, 7),
                WorkTimeBudget.DefaultDailyWorkMinutes);
            budget.SetCapacityMultiplier(0.25f);
            Assert.IsTrue(budget.TryCommit(143, out string _));
            Assert.AreEqual(7, budget.MinutesRemaining);
            Assert.IsTrue(!budget.TryCommit(10, out string refusal));
            Assert.IsTrue(!string.IsNullOrWhiteSpace(refusal));
        }
    }
}
